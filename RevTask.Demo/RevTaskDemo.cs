// ============================================================
// RevTaskDemo.cs —— 异步任务开箱示例
//
// 位置：Assets\Revolution.Demo\RevTask.Demo\
//
// 【它解决什么】
//   "等一帧 / 等一段时间 / 等多个任务全完成" 的轻量异步：async RevTask 直接 await，
//   自带主线程调度器（NextFrame / Yield / Delay），续体回主线程 —— 不需要 Unity 的 Coroutine，
//   也没有 Task 的线程池线程问题。
//
// 【怎么用】打开配套场景 RevTaskDemo.unity → 点 Play → 左侧按钮逐个点。
//
// 【本示例演示什么】
//   ① async RevTask：一个方法里顺序写"等一帧 → 等 500ms → 完成"（像同步代码一样读）；
//   ② Yield / NextFrame：分散到帧执行（不卡当帧）；
//   ③ Delay：毫秒级延迟；
//   ④ WhenAll：并行等多个任务全部完成；
//   ⑤ 异常传播：任务里抛的异常在 await 处抛出（不会静默吞掉）；
//   ⑥ CompletionSource：把"回调式"的旧接口包成可 await 的新接口（适配旧代码的桥）。
//
// 【零配置】调度器是自动创建的 DontDestroyOnLoad 宿主，第一次 await 时就位。
// ============================================================
using System.Collections.Generic;
using UnityEngine;

namespace Revolution.Demo.Task
{
    public sealed class RevTaskDemo : MonoBehaviour
    {
        private readonly List<string> _ui = new List<string>();
        private Vector2 _scroll;

        private void Ui(string line)
        {
            _ui.Add($"[{Time.frameCount}] {line}");              // 带帧号：能看出"分散到了哪些帧"
            if (_ui.Count > 200) _ui.RemoveAt(0);
            _scroll.y = float.MaxValue;
        }

        // ---------- ① 一个完整的异步流程（async RevTask 顺序写，读起来像同步） ----------
        private async Revolution.RevTask RunFlow()
        {
            Ui("流程开始：先等 1 帧……");
            await Revolution.RevTask.Yield();                     // 等一帧（这一帧的剩余逻辑先跑完）
            Ui("1 帧已过：再等 500 毫秒……");
            await Revolution.RevTask.Delay(500);                  // 等 500ms（期间帧照常跑，不卡）
            Ui("500ms 已过：并行发 3 个子任务（WhenAll）……");

            // ④ WhenAll：三个子任务并行，全部完成才继续
            await Revolution.RevTask.WhenAll(SubTask("资源A"), SubTask("资源B"), SubTask("资源C"));
            Ui("三个子任务全部完成 → 流程结束 ✓（异常会沿 await 传播，不会静默）。");
        }

        private async Revolution.RevTask SubTask(string name)
        {
            await Revolution.RevTask.Delay(200);
            Ui($"  子任务 {name} 完成（200ms）");
        }

        // ---------- ⑥ CompletionSource：把回调式旧接口包成可 await ----------
        // 真实场景：老代码长这样 —— void GetConfig(Action<string> onDone)。
        // 用 RevTaskCompletionSource 包一层，新代码就能 await 它（桥接的标准写法）。
        private Revolution.RevTask<string> WaitLegacyApi(string fakeResult)
        {
            var source = new Revolution.RevTaskCompletionSource<string>();

            // 模拟旧接口"稍后回调给结果"（真实项目里这一行就是调用旧接口）
            _ = CallBackLater(() => source.SetResult(fakeResult));

            return source.Task;
        }

        private async Revolution.RevTask CallBackLater(System.Action done)
        {
            await Revolution.RevTask.Delay(300);                 // 模拟 300ms 后旧接口回调
            done();
        }

        // ==================== OnGUI ====================

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 430, Screen.height - 20));
            GUILayout.Label("<b>RevTask 异步任务演示</b>", TitleStyle());

            // ① 完整流程：Yield → Delay → WhenAll
            if (GUILayout.Button("① 跑一个完整异步流程（Yield → Delay → WhenAll）"))
            {
                _ = RunFlow();                                    // 故意不 await：用 .Forget() 表达同样意图（见框架文档）
                Ui("流程已启动（右侧日志带帧号，能看出每一步落在哪一帧）。");
            }

            // ② NextFrame：框架最常用的"等一帧"
            if (GUILayout.Button("② NextFrame：下一帧再做事"))
            {
                Ui("已排 NextFrame（本帧结束后来这里）……");
                _ = NextFrameStep();
            }

            // ③ Delay：毫秒延迟
            if (GUILayout.Button("③ Delay(1000)：1 秒后提醒"))
            {
                Ui("已排 1 秒提醒……");
                _ = DelayedReminder();
            }

            // ④ 异常传播：任务里的异常在 await 处抛出
            if (GUILayout.Button("④ 异常传播（任务抛异常 → await 处接住）"))
            {
                _ = ExceptionStep();
            }

            // ⑤ CompletionSource：回调式旧接口 → await
            if (GUILayout.Button("⑤ 适配旧接口（CompletionSource）"))
            {
                _ = LegacyStep();
            }

            GUILayout.Space(6);
            if (GUILayout.Button("清空演示日志")) _ui.Clear();

            GUILayout.Label("原则：异步流程里只做等待与编排，重活交给分帧；await 之后的代码回到主线程继续。", HintStyle());
            GUILayout.FlexibleSpace();
            GUILayout.EndArea();

            // ---------- 右侧：演示日志 ----------
            GUILayout.BeginArea(new Rect(Screen.width - 470, 10, 460, Screen.height - 20));
            GUILayout.Label("<b>演示步骤日志（帧号可见）</b>", TitleStyle());
            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (string line in _ui) GUILayout.Label(line);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ---------- 各按钮的异步体 ----------

        private async Revolution.RevTask NextFrameStep()
        {
            await Revolution.RevTaskScheduler.NextFrame();
            Ui("NextFrame 到期：这一帧做本帧内的事（比如等 UI 布局刷完再取位置）。");
        }

        private async Revolution.RevTask DelayedReminder()
        {
            await Revolution.RevTask.Delay(1000);
            Ui("1 秒已到：Delay 期间帧照常跑（右侧的帧号还在涨）。");
        }

        private async Revolution.RevTask ExceptionStep()
        {
            try
            {
                await Boom();
                Ui("不会走到这里。");
            }
            catch (System.InvalidOperationException e)
            {
                Ui($"在 await 处接住了任务里的异常：{e.Message}（失败必须可见，不能静默）。");
            }
        }

        private async Revolution.RevTask Boom()
        {
            await Revolution.RevTask.Yield();
            throw new System.InvalidOperationException("任务内部故意抛出");
        }

        private async Revolution.RevTask LegacyStep()
        {
            string result = await WaitLegacyApi("旧接口的回调结果");
            Ui($"CompletionSource 桥接完成：{result}（老回调接口不用改写法就能被 await）。");
        }

        // ---------- 简单样式 ----------
        private static GUIStyle _title, _hint;
        private static GUIStyle TitleStyle() => _title ??= new GUIStyle(GUI.skin.label) { richText = true, fontSize = 15 };
        private static GUIStyle HintStyle() => _hint ??= new GUIStyle(GUI.skin.label) { richText = true, fontSize = 11, wordWrap = true };
    }
}
