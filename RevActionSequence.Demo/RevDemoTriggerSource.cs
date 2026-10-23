// ============================================================
// RevDemoTriggerSource.cs —— 演示"区域触发源"（对应原体系的碰撞进入/离开触发器）
//
// 位置：Assets\Revolution.Demo\RevActionSequence.Demo\
//
// 【为什么框架不内置区域触发】
//   原体系把"碰撞盒 + 距离检测 + GridSAP 空间网格"做进了触发器系统
//   （DimensionTriggerService：每物理帧 × 每个 actor × 每个触发器），代价是：
//   这套数学与场景强耦合、单测必须进 PlayMode、而且只有它自己能用。
//   本框架只给契约：**区域检测留在业务侧**，检测到就喊一声，剩下的交给绑定。
//
// 【触发源只有一种写法：实现 RevITriggerSource】
//   · Unity 物理：让那个 MonoBehaviour 直接实现 RevITriggerSource，在 OnTriggerEnter 里
//     Triggered?.Invoke(new RevTriggerSignal(other.gameObject, Name))；
//   · 自研网格 / 服务端下发：你自己的检测循环里，往 Triggered 发信号；
//   · 本 Demo 的位置：本类是一个**纯 C# 触发源**（不是 MonoBehaviour），由入口脚本持有，
//     按键时调 RaiseEnter / RaiseExit —— 真项目里把这两个调用换到物理回调里即可。
// ============================================================
using System;
using System.Collections.Generic;

namespace Revolution.Demo.ActionSequence
{
    /// <summary>
    /// 区域触发源：目标进入/离开时各喊一声。
    /// <para>进出用两个**独立事件**表达（与原体系 enterActions / exitActions 对齐，它们跑两条不同序列）；
    /// <see cref="Triggered"/> 则走框架统一的绑定通道（<c>runner.Bind(source, 序列)</c>）。</para>
    /// </summary>
    public sealed class RevDemoZoneTriggerSource : RevITriggerSource
    {
        private readonly HashSet<object> _inside = new HashSet<object>();

        /// <summary>目标进入区域（参数：进入者）</summary>
        public event Action<object> Entered;

        /// <summary>目标离开区域（参数：离开者）</summary>
        public event Action<object> Exited;

        /// <inheritdoc/>
        public string Name { get; }

        /// <inheritdoc/>
        public event Action<RevTriggerSignal> Triggered;

        /// <summary>区域半径（米）—— 对应原体系触发器上的球半径（那边单位是毫米）</summary>
        public float Radius { get; }

        /// <summary>构造一个区域触发源</summary>
        /// <param name="name">区域名（日志用）</param>
        /// <param name="radius">半径（米）</param>
        public RevDemoZoneTriggerSource(string name, float radius)
        {
            Name = string.IsNullOrEmpty(name) ? "未命名区域" : name;
            Radius = radius;
        }

        /// <summary>
        /// 报告"某人进入区域"（真项目里由 OnTriggerEnter 调它）。
        /// <para>已经在里面的人不会重复触发 —— 等价于物理引擎不会重复抛 OnTriggerEnter。</para>
        /// </summary>
        public void RaiseEnter(object who)
        {
            if (who == null || !_inside.Add(who)) return;

            Entered?.Invoke(who);
            Triggered?.Invoke(new RevTriggerSignal(who, Name));   // 统一通道：Bind 到这个源的序列会在这里启动
        }

        /// <summary>报告"某人离开区域"（真项目里由 OnTriggerExit 调它）</summary>
        public void RaiseExit(object who)
        {
            if (who == null || !_inside.Remove(who)) return;

            Exited?.Invoke(who);
        }

        /// <summary>当前区域内人数（调试用）</summary>
        public int InsideCount => _inside.Count;

        /// <inheritdoc/>
        public void Dispose()
        {
            Entered = null;
            Exited = null;
            Triggered = null;
            _inside.Clear();
        }
    }
}
