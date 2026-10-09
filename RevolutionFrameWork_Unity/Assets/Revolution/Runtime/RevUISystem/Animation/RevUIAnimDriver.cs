// ============================================================
// RevUIAnimDriver.cs —— UI 动画的每帧驱动（Unity 侧）
//
// 位置：Runtime\RevUISystem\Animation\（UI 动画库）
//
// 【为什么不用新起一个隐藏宿主】
//   框架里已经有"公共 Mono 驱动"（<c>RevMono.AddUpdate</c>）—— 它自己会建隐藏宿主、
//   切场景不丢、域重载后自恢复。动画库没必要再造一个（少一个常驻对象，少一处要维护）。
//
// 【时间口径】
//   用 <c>Time.unscaledDeltaTime</c>：**暂停（timeScale = 0）时 UI 动画照常播** ——
//   打开背包、看结算界面这类"游戏暂停但 UI 还要动"的场景才是常态。
//   再乘一个全局倍速（<c>RevUIAnim.GlobalSpeed</c>），做"动画速度"设置项时改它一个数就行。
//
// 【域重载】
//   静态字段会跨 Play 存活（编辑器关掉域重载时）：进 Play 前清空引擎与驱动标记，
//   否则上一轮的运行时对象会被当成"还在播"。
// ============================================================
using UnityEngine;

namespace Revolution
{
    /// <summary>UI 动画每帧驱动（框架内部）</summary>
    internal static class RevUIAnimDriver
    {
        private static bool _installed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void OnSubsystemRegistration()
        {
            // 引擎里的静态列表 / 池不能跨 Play 存活
            RevUIAnimEngine.Reset();

            // 异常统一从框架日志出口出（采样回调 / 完成回调里写错了东西不至于静默）
            RevUIAnimEngine.OnException = (e, what) => RevUILog.Error($"{what} 抛异常（已隔离）：{e}");

            // 延迟到第一次实际播放时挂 Tick：SubsystemRegistration 回调之间没有可靠顺序，
            // 若此处先 AddUpdate，后执行的 RevMono.ResetForNewSession 会把它清掉，但 _installed 却为 true。
            _installed = false;
        }

        /// <summary>把每帧推进挂到框架的公共 Mono 驱动上（重复调用安全）。</summary>
        internal static void Install()
        {
            if (_installed) return;
            _installed = true;

            RevMono.AddUpdate(Tick);            // 框架自带的隐藏宿主，不用我们新建
            RevUILog.Info("UI 动画驱动已挂上（跟随 RevMono 每帧推进）");
        }

        private static void Tick()
        {
            if (RevUIAnimEngine.ActiveCount == 0) return;      // 空转零开销

            float speed = RevUIAnim.GlobalSpeed;
            if (speed <= 0f) return;

            RevUIAnimEngine.Step(Time.unscaledDeltaTime * speed);
        }
    }
}
