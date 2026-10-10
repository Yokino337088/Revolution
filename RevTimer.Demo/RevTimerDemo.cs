// ============================================================
// RevTimerDemo.cs —— 计时器系统开箱示例
//
// 位置：Assets\Revolution.Demo\RevTimer.Demo\
//
// 【怎么用】打开配套场景 RevTimerDemo.unity → 点 Play → 左侧按钮逐个点。
//
// 【本示例演示什么】
//   ① After：N 秒后做一次（最常用）；
//   ② Every：每 N 秒一次（无限 / 限次数，回调可带"第几次"）；
//   ③ 句柄：暂停 / 恢复 / 重启 / 停止，剩余时间与进度直接读；
//   ④ NextFrame：下一帧做一次（比"等 0.01 秒"语义清楚）；
//   ⑤ 全局暂停 RevTimer.Paused：整个模块的推进开关（四个时间域全冻结）；
//   ⑥ Server 域：先 SyncServerTime 校准才能用 At（没校准会拿到空句柄 + NoServerTime 原因）；
//   ⑦ owner 防泄漏：随对象销毁一行清干净；
//   ⑧ 秒表：测一段代码的耗时。
//
// 【核心认知】时间用 double 秒累积 → 没有帧漂移；回调跑在主线程 Tick 里 → 别在回调里做重活。
// ============================================================
using System.Collections.Generic;
using UnityEngine;

namespace Revolution.Demo.Timer
{
    public sealed class RevTimerDemo : MonoBehaviour
    {
        // ---------- 演示状态 ----------
        private readonly List<string> _ui = new List<string>();
        private Vector2 _scroll;

        private RevTimerHandle _everyHandle;        // 每秒循环计时器的句柄（存下来才能 Pause / Stop）
        private RevTimerHandle _atHandle;           // Server 域定时器的句柄
        private RevStopwatch _stopwatch;            // 秒表

        private void Ui(string line)
        {
            _ui.Add($"[{Time.frameCount}] {line}");
            if (_ui.Count > 200) _ui.RemoveAt(0);
            _scroll.y = float.MaxValue;
        }

        private void Start()
        {
            Ui("计时器演示就绪。左侧逐个点，读数区实时显示句柄状态。");
            Ui($"当前存活计时器：{RevTimer.Count} 个。");
        }

        private void Update()
        {
            // 每帧把"每秒循环计时器"的剩余量刷进状态（演示读数用；业务里按需读）
        }

        // ==================== OnGUI ====================

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 420, Screen.height - 20));
            GUILayout.Label("<b>RevTimer 计时器演示</b>", TitleStyle());

            // ① After：N 秒后一次。返回句柄 —— 想中途停就存下来。
            if (GUILayout.Button("① After 3 秒 → 打一条日志"))
            {
                RevTimer.After(3f, () =>
                {
                    RevLog.Info("[RevTimerDemo] 3 秒到了！", "Demo");
                    Ui("3 秒回调触发 ✓");
                });
                Ui("已创建 After(3s)：3 秒后回调。期间可以点下面的暂停 / 停止试试。");
                
            }

            // ② Every：每秒一次，共 5 次；回调带"第几次"。
            if (GUILayout.Button("② Every 1 秒 × 5 次（带第几次）"))
            {
                _everyHandle = RevTimer.Every(1f, i => Ui($"第 {i} 次触发（Every 1s）"), times: 5);
                Ui("已创建 Every(1s × 5)。存了句柄，下面的暂停/重启按钮对它生效。");
            }

            // ③ 句柄操作：暂停 / 恢复 / 重启 / 停止
            if (GUILayout.Button("③a 句柄.Pause()（冻结剩余量）")) { _everyHandle.Pause(); Ui("已暂停：剩余量保留，恢复后接着走。"); }
            if (GUILayout.Button("③b 句柄.Resume()")) { _everyHandle.Resume(); Ui("已恢复。"); }
            if (GUILayout.Button("③c 句柄.Restart()（清零重来）")) { _everyHandle.Restart(); Ui("已重启：已走时长清零、次数复原。"); }
            if (GUILayout.Button("③d 句柄.Stop()（回收；重复调用安全）")) { _everyHandle.Stop(); Ui("已停止回收。过期句柄之后的任何调用都是安全空操作。"); }

            // ④ NextFrame：下一帧做一次。比 After(0.001f) 语义清楚。
            if (GUILayout.Button("④ NextFrame → 下一帧做一次"))
            {
                RevTimer.NextFrame(() => Ui($"下一帧回调触发（当前帧 {Time.frameCount}）"));
                Ui("已排一个 NextFrame：下一帧执行。");
            }

            GUILayout.Space(6);

            // ⑤ 全局暂停：整个模块冻结（四个时间域全停），恢复后接着走
            if (GUILayout.Button(RevTimer.Paused ? "⑤ 全局暂停中 → 恢复 RevTimer.Paused = false"
                                                 : "⑤ 全局暂停 RevTimer.Paused = true"))
            {
                RevTimer.Paused = !RevTimer.Paused;
                Ui(RevTimer.Paused ? "已全局暂停：所有计时器冻结（含 Server / Fixed 域）。"
                                   : "已恢复：计时器从冻结处继续（不会把暂停时长补跑一遍）。");
            }

            // ⑥ Server 域：必须先校准服务器时间，At 才可用
            if (GUILayout.Button("⑥a SyncServerTime（用本机时间模拟服务器校准）"))
            {
                RevTimer.SyncServerTime(System.DateTime.UtcNow);      // 真实项目：用服务器下发的 UTC
                Ui("已校准服务器时间：现在可以用 At（到某个绝对时刻做事）。");
            }
            if (GUILayout.Button("⑥b At（10 秒后，Server 域）"))
            {
                _atHandle = RevTimer.At(System.DateTime.UtcNow.AddSeconds(10),
                    () => { RevLog.Info("[RevTimerDemo] 活动结束（At 触发）", "Demo"); Ui("At 触发 ✓（改设备时间也骗不过它）"); });
                Ui(_atHandle.IsAlive
                    ? "已创建 At（10 秒后）。校准过就能用；改本机时间骗不过 Server 域。"
                    : "拿到了空句柄 —— 原因 NoServerTime：At 之前必须先 SyncServerTime（订阅 RevTimer.Failed 能看到原因码）。");
            }

            GUILayout.Space(6);

            // ⑦ owner 防泄漏：随对象销毁一行清干净（这里是 this，销毁场景时全部带走）
            if (GUILayout.Button("⑦ 创建 10 个带 owner 的循环计时器（泄漏演示）"))
            {
                for (int i = 0; i < 10; i++) RevTimer.Every(3600f, () => { }, owner: this);
                Ui($"创建了 10 个（owner: this，当前共 {RevTimer.Count} 个）。销毁场景时框架按 owner 一行全清 —— 这是防泄漏的正确姿势。");
            }
            if (GUILayout.Button("⑦b CancelAllOf(this)（按 owner 全清）"))
            {
                int n = RevTimer.CancelAllOf(this);
                Ui($"按 owner 清掉了 {n} 个。这就是 OnDestroy 里该写的那一行。");
            }

            // ⑧ 秒表：测一段代码耗时（与计时器无关，纯读数工具）
            if (GUILayout.Button("⑧ 秒表：测一段代码耗时"))
            {
                _stopwatch = RevTimer.StartStopwatch();
                int sum = 0;
                for (int i = 0; i < 100000; i++) sum += i;            // 一段"有耗时"的代码
                Ui($"秒表：这段循环耗时 {_stopwatch.Elapsed:F4} 秒（sum={sum}，防止被优化）。Stop/Reset/Restart 都有。");
            }

            GUILayout.Space(6);
            if (GUILayout.Button("清空演示日志")) _ui.Clear();

            GUILayout.Label($"存活计时器：{RevTimer.Count}　全局暂停：{RevTimer.Paused}", HintStyle());

            // 实时读数：句柄的剩余时间与进度（给 UI 进度条用）
            if (_everyHandle.IsAlive)
                GUILayout.Label($"每秒循环计时器：剩 {_everyHandle.Left:F1}s · 进度 {_everyHandle.Progress:P0}", HintStyle());
            if (_atHandle.IsAlive)
                GUILayout.Label($"At 定时器：剩 {_atHandle.Left:F1}s · 进度 {_atHandle.Progress:P0}", HintStyle());

            GUILayout.FlexibleSpace();
            GUILayout.EndArea();

            // ---------- 右侧：演示日志 ----------
            GUILayout.BeginArea(new Rect(Screen.width - 470, 10, 460, Screen.height - 20));
            GUILayout.Label("<b>演示步骤日志</b>", TitleStyle());
            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (string line in _ui) GUILayout.Label(line);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // 销毁时的防泄漏示范：owner 是 this 的计时器全部带走
        private void OnDestroy() => RevTimer.CancelAllOf(this);

        // ---------- 简单样式 ----------
        private static GUIStyle _title, _hint;
        private static GUIStyle TitleStyle() => _title ??= new GUIStyle(GUI.skin.label) { richText = true, fontSize = 15 };
        private static GUIStyle HintStyle() => _hint ??= new GUIStyle(GUI.skin.label) { richText = true, fontSize = 11, wordWrap = true };
    }
}
