// ============================================================
// RevTaskScheduler.cs —— Update 驱动的调度器（替代协程）
//
// 位置：Runtime\RevTask\
//
// 【它替代了什么】
//   协程的"每帧推进"能力。区别：
//     · 协程：每个协程每帧一次 MoveNext（虚调用 + 状态机），yield 还有分配；
//     · 这里：一个 Action 出队 + 调用；配合零分配的 NextFrame awaiter，几乎没有开销。
//
// 【为什么不注入 PlayerLoop？】
//   Update 驱动的开销与 PlayerLoop 同量级，但代码量少一个数量级。
//   真到瓶颈再升级 —— 对外接口不变，只换内部实现。
// ============================================================
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Revolution
{
    /// <summary>RevTask 调度器：全局唯一，DontDestroyOnLoad</summary>
    public sealed class RevTaskScheduler : MonoBehaviour
    {
        // 帧内要执行的续体（Yield / 延时到期 / 异步操作完成）
        private static readonly Queue<Action> _frameQueue = new Queue<Action>(256);

        // 延时队列（每帧扫一遍到期项；节点数量通常很小）
        private static readonly List<DelayNode> _delays = new List<DelayNode>(64);

        /// <summary>每帧最多执行多少个续体，防止某一帧被大量回调卡住</summary>
        public static int MaxContinuationsPerFrame = 128;

        private static RevTaskScheduler _instance;

        private struct DelayNode
        {
            public float DueTime;            // 到期时刻（Time.realtimeSinceStartup）
            public Action Continuation;
        }

        // ============================================================
        // 对外：把续体排到下一帧
        // ============================================================
        public static void Post(Action continuation)
        {
            if (continuation == null) return;
            EnsureInstance();
            _frameQueue.Enqueue(continuation);
        }

        /// <summary>
        /// 零分配的"等一帧"：await RevTaskScheduler.NextFrame();
        /// 为什么零分配？直接把状态机的续体交给队列，不创建任何 Promise。
        /// </summary>
        public static RevNextFrameAwaiter NextFrame() => default;

        /// <summary>等一帧（返回 RevTask 的便捷版，有一次小分配；热路径请用 NextFrame()）</summary>
        public static RevTask Yield()
        {
            var source = new RevTaskCompletionSource();
            Post(source.SetResult);
            return source.Task;
        }

        /// <summary>延时（毫秒）</summary>
        public static RevTask Delay(int milliseconds)
        {
            var source = new RevTaskCompletionSource();

            if (milliseconds <= 0)
            {
                Post(source.SetResult);
                return source.Task;
            }

            EnsureInstance();
            _delays.Add(new DelayNode
            {
                DueTime = Time.realtimeSinceStartup + milliseconds * 0.001f,
                Continuation = source.SetResult
            });
            return source.Task;
        }

        /// <summary>等一批任务全部完成</summary>
        public static RevTask WhenAll(params RevTask[] tasks)
        {
            var source = new RevTaskCompletionSource();

            if (tasks == null || tasks.Length == 0)
            {
                source.SetResult();
                return source.Task;
            }

            int remain = tasks.Length;
            foreach (RevTask t in tasks)
            {
                t.GetAwaiter().OnCompleted(() =>
                {
                    if (--remain == 0) source.SetResult();
                });
            }

            return source.Task;
        }

        // ============================================================
        // 驱动
        // ============================================================
        private void Update()
        {
            // ① 延时到期的先入帧队列
            float now = Time.realtimeSinceStartup;
            for (int i = _delays.Count - 1; i >= 0; i--)
            {
                if (now < _delays[i].DueTime) continue;

                _frameQueue.Enqueue(_delays[i].Continuation);
                _delays.RemoveAt(i);
            }

            // ② 限量执行帧队列（本帧新入队的留到下一帧，避免卡帧）
            int budget = _frameQueue.Count < MaxContinuationsPerFrame
                ? _frameQueue.Count
                : MaxContinuationsPerFrame;

            while (budget-- > 0)
                _frameQueue.Dequeue()?.Invoke();
        }

        private void OnDestroy()
        {
            // 退出 / 切场景时清理，避免残留委托指向已销毁对象
            _frameQueue.Clear();
            _delays.Clear();
            _instance = null;
        }

        private static void EnsureInstance()
        {
            if (_instance != null) return;

            var go = new GameObject("[RevTaskScheduler]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<RevTaskScheduler>();
            go.hideFlags = HideFlags.HideAndDontSave;
        }
    }

    /// <summary>
    /// 零分配的"等一帧" awaiter：
    /// IsCompleted 恒为 false，所以状态机一定会在 OnCompleted 里把续体 Post 到下一帧。
    /// </summary>
    public readonly struct RevNextFrameAwaiter : ICriticalNotifyCompletion
    {
        public bool IsCompleted => false;

        /// <summary>
        /// 让 `await RevTaskScheduler.NextFrame();` 直接可用。
        /// ★ 少了这一行编译不过：await 找的是"表达式的 GetAwaiter()"，
        ///   而这个类型自己就是 awaiter，所以标准写法就是"返回自己"（readonly struct，零分配）。
        /// </summary>
        public RevNextFrameAwaiter GetAwaiter() => this;

        public void GetResult() { }

        public void OnCompleted(Action continuation) => RevTaskScheduler.Post(continuation);

        public void UnsafeOnCompleted(Action continuation) => RevTaskScheduler.Post(continuation);
    }
}
