// ============================================================
// RevAsyncLoadPump.cs —— 异步加载泵
//
// 位置：Runtime\资源加载\
//
// 【为什么不能"每个请求各加载各的"？】
//   一个界面可能要加载上百个图标，如果同时发起上百个 IO 请求：
//   磁盘/内存峰值飙升，反而拖慢所有加载，还可能卡帧。
//   加载泵的做法是"排队 + 限流"：同时最多只跑 MaxConcurrent 个。
//
// 【双队列】
//   _waiting —— 排队中（还没开始加载）
//   _loading —— 正在加载（数量 ≤ MaxConcurrent）
//
// 【回调合并】同一资源被多处同时请求时只加载一次，回调挂在同一个 RevLoadJob 上。
// ============================================================
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Revolution
{
    public static class RevAsyncLoadPump
    {
        private class RevLoadJob
        {
            public RevResHandle handle;
            public IRevResLoader loader;
            public int priority;
            public bool cancelRequested;
            public bool finished;
            public readonly RevCancellationTokenSource cancellation = new RevCancellationTokenSource();
            public readonly List<Action<RevResHandle>> callbacks = new List<Action<RevResHandle>>();
        }

        private static readonly List<RevLoadJob> _waiting = new List<RevLoadJob>();
        private static readonly List<RevLoadJob> _loading = new List<RevLoadJob>();
        private static readonly Dictionary<ulong, RevLoadJob> _jobByKey = new Dictionary<ulong, RevLoadJob>();

        /// <summary>最大并发加载数（最小为 1，避免设为 0 后等待队列永久停滞）</summary>
        private static int _maxConcurrent = 4;
        public static int MaxConcurrent
        {
            get => _maxConcurrent;
            set => _maxConcurrent = value < 1 ? 1 : value;
        }

        private static bool _pumping;
        private static int _pumpGeneration;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void OnSubsystemRegistration() => ResetForNewSession();

        /// <summary>Domain Reload 关闭时结束上一会话任务，避免旧 pump 状态阻塞新一局。</summary>
        internal static void ResetForNewSession()
        {
            _pumpGeneration++;
            _pumping = false;
            var jobs = new List<RevLoadJob>(_waiting.Count + _loading.Count);
            jobs.AddRange(_waiting);
            jobs.AddRange(_loading);
            _waiting.Clear();
            _loading.Clear();
            _jobByKey.Clear();
            for (int i = 0; i < jobs.Count; i++) CancelJob(jobs[i], notifyNow: true);
        }

        public static int WaitingCount => _waiting.Count;
        public static int LoadingCount => _loading.Count;

        // ==================== 提交 ====================

        public static void Submit(RevResHandle handle, IRevResLoader loader, Action<RevResHandle> onFinished, int priority = 0)
        {
            if (handle == null) { onFinished?.Invoke(RevResHandle.Empty); return; }

            // 同资源已有且未取消的任务：合并回调。已取消的旧任务不接收新请求，
            // 否则 Shutdown 后立即重载会被合并进旧任务并收到 Cancelled。
            if (_jobByKey.TryGetValue(handle.Key, out RevLoadJob exist) && !exist.cancelRequested)
            {
                if (onFinished != null) exist.callbacks.Add(onFinished);
                return;
            }

            var job = new RevLoadJob { handle = handle, loader = loader, priority = priority };
            if (onFinished != null) job.callbacks.Add(onFinished);

            _waiting.Add(job);
            _jobByKey[handle.Key] = job;
            _waiting.Sort((a, b) => b.priority.CompareTo(a.priority));   // 优先级大者先加载

            EnsurePumping();
        }

        /// <summary>把回调挂到"正在加载中"的资源上</summary>
        public static void AddCallback(ulong key, Action<RevResHandle> cb)
        {
            if (cb == null) return;
            if (_jobByKey.TryGetValue(key, out RevLoadJob job)) job.callbacks.Add(cb);
        }

        // ==================== 驱动 ====================

        private static void EnsurePumping()
        {
            if (_pumping) return;
            _pumping = true;
            PumpLoop(_pumpGeneration).Forget();
        }

        private static async RevTask PumpLoop(int generation)
        {
            try
            {
                while (generation == _pumpGeneration && (_waiting.Count > 0 || _loading.Count > 0))
                {
                    while (generation == _pumpGeneration && _waiting.Count > 0 && _loading.Count < MaxConcurrent)
                    {
                        RevLoadJob job = _waiting[0];
                        _waiting.RemoveAt(0);
                        _loading.Add(job);
                        RunJob(job).Forget();
                    }

                    await RevTaskScheduler.NextFrame();
                }
            }
            finally
            {
                if (generation == _pumpGeneration)
                {
                    _pumping = false;
                    if (_waiting.Count > 0 || _loading.Count > 0) EnsurePumping();
                }
            }
        }

        private static async RevTask RunJob(RevLoadJob job)
        {
            if (job.finished) return;
            bool done = false;
            try
            {
                job.loader.LoadAsync(job.handle, h => done = true, job.cancellation.Token);
                while (!done && !job.finished) await RevTaskScheduler.NextFrame();
            }
            catch (Exception e)
            {
                job.handle.ErrorReason = job.cancelRequested
                    ? RevResLoadErrorReason.Cancelled
                    : RevResLoadErrorReason.BundleLoadFail;
                job.handle.MarkError();
                if (!job.cancelRequested) RevLog.Exception(e, "异步资源加载器异常", "Res");
            }

            if (job.finished) return;
            if (job.cancelRequested)
            {
                job.handle.ErrorReason = RevResLoadErrorReason.Cancelled;
                job.handle.MarkError();
            }

            CompleteJob(job, "异步资源加载回调异常");
        }

        // ==================== 清理 ====================

        /// <summary>取消指定业务分组的等待/在途任务；常驻资源不随分组场景卸载。</summary>
        public static void CancelGroup(RevResGroup group)
            => CancelWhere(job => job.handle.Group == group && !job.handle.HasFlag(RevResInstanceFlag.Resident));

        /// <summary>取消全部等待/在途任务，并确保排队任务收到终态回调。</summary>
        public static void CancelAll() => CancelWhere(_ => true);

        private static void CancelWhere(Predicate<RevLoadJob> predicate)
        {
            var jobs = new List<RevLoadJob>(_waiting.Count + _loading.Count);
            jobs.AddRange(_waiting);
            jobs.AddRange(_loading);
            for (int i = 0; i < jobs.Count; i++)
            {
                RevLoadJob job = jobs[i];
                if (!predicate(job)) continue;
                bool waiting = _waiting.Remove(job);
                CancelJob(job, notifyNow: waiting);
            }
        }

        private static void CompleteJob(RevLoadJob job, string callbackContext)
        {
            if (job.finished) return;
            job.finished = true;
            _waiting.Remove(job);
            _loading.Remove(job);
            if (_jobByKey.TryGetValue(job.handle.Key, out RevLoadJob registered) && ReferenceEquals(registered, job))
                _jobByKey.Remove(job.handle.Key);

            for (int i = 0; i < job.callbacks.Count; i++)
            {
                try { job.callbacks[i]?.Invoke(job.handle); }
                catch (Exception e) { RevLog.Exception(e, callbackContext, "Res"); }
            }
            job.callbacks.Clear();
        }

        private static void CancelJob(RevLoadJob job, bool notifyNow)
        {
            if (job.finished || job.cancelRequested) return;
            job.cancelRequested = true;
            job.handle.ErrorReason = RevResLoadErrorReason.Cancelled;
            job.handle.MarkError();
            job.cancellation.Cancel();

            if (!notifyNow)
            {
                if (_jobByKey.TryGetValue(job.handle.Key, out RevLoadJob registered) && ReferenceEquals(registered, job))
                    _jobByKey.Remove(job.handle.Key);
                return;
            }

            CompleteJob(job, "已取消资源加载回调异常");
        }

        /// <summary>兼容清队列入口：按取消语义安全终止任务，不遗失句柄/回调。</summary>
        public static void Clear() => CancelAll();
    }
}
