// ============================================================
// RevDemoSteps.cs —— 演示"自定义步骤"（继承 RevStepBase）
//
// 位置：Assets\Revolution.Demo\RevActionSequence.Demo\
//
// 【什么时候必须写自定义步骤，而不是 DoBlocking】
//   <c>DoBlocking(name, execute, isCompleted)</c> 的谓词是**构建期一次性给定的**，
//   它没有"本次运行私有的状态"。于是：
//     · 谓词只从 <c>context</c> / 业务服务读状态          → ✔ 安全（服务是有状态的，如播放器）
//     · 谓词闭包捕获 lambda 外的字段（例如 _startTime）    → ✘ 危险：多个运行实例会是同一个字段，互相覆盖
//   （这正是原体系"状态写在节点组件字段里、多 actor 并发触发互相踩"的老问题！）
//
//   需要"跨帧私有状态"时，就写 RevStepBase 子类：状态放进状态槽，按运行实例隔离。
//   本文件的三个步骤分别对应原体系的三个真实节点：
//     RevWaitClickStep    ← SimpleActionWaitInput（等待玩家点击）
//     RevFlyParabolaStep  ← 灵果的抛物线飞向玩家（原体系用 MoveParam 的物理参数）
//     RevWaitTimelineStep ← SimpleActionPlayTimelineOrAnimator 的 isWaitUtilFinish
// ============================================================
namespace Revolution.Demo.ActionSequence
{
    /// <summary>
    /// 等待玩家点击（对应原体系 <c>SimpleActionWaitInput</c>）。
    /// <para>它是**阻塞步骤**：<see cref="IsCompleted"/> 返回 false 时序列就停在这一步，
    /// 每帧再问一次；玩家点了才放行下一步。</para>
    /// </summary>
    public sealed class RevWaitClickStep : RevStepBase
    {
        /// <inheritdoc/>
        public override string Name => "等待玩家点击";

        /// <inheritdoc/>
        public override void Execute(RevSequenceRun run, RevSequenceContext context)
        {
            // 每步只 Execute 一次，所以这里不需要"重置状态"
            // （原体系要担心重复触发把 m_inputTriggered 弄脏，本框架不存在这个问题）
            UnityEngine.Debug.Log("开始等待玩家交互输入");
        }

        /// <inheritdoc/>
        public override bool IsCompleted(RevSequenceRun run, RevSequenceContext context)
        {
            IDemoInputService input = context.Get<IDemoInputService>();
            if (input == null) return true;                 // 没接输入服务就当"直接通过"，避免永久卡死

            return input.ConsumeClick();
        }
    }

    /// <summary>
    /// 等灵果沿抛物线飞向玩家（对应原体系灵果的 MoveParam：水平速度 + 重力）。
    /// <para>这是"必须写自定义步骤"的典型例子：需要一个记录<b>本次飞行起点/时长</b>的状态，
    /// 而它必须按运行实例隔离（两个玩家同时采两颗灵果，两个飞行状态不能互相覆盖）。</para>
    /// </summary>
    public sealed class RevFlyParabolaStep : RevStepBase
    {
        private sealed class State
        {
            public float Duration;      // 本次飞行总时长
        }

        private readonly float _duration;
        private readonly float _gravity;
        private readonly string _stepName;

        /// <summary>构造：飞多久、受多大重力（只影响日志，演示用）</summary>
        public RevFlyParabolaStep(float duration = 0.35f, float gravity = 9.8f, string stepName = "抛物线飞向玩家")
        {
            _duration = duration;
            _gravity = gravity;
            _stepName = stepName;
        }

        /// <inheritdoc/>
        public override string Name => _stepName;

        /// <inheritdoc/>
        public override void Execute(RevSequenceRun run, RevSequenceContext context)
        {
            // ★ 状态写进"运行实例的状态槽"：随本次运行隔离，随运行实例池化复用（稳态零分配）
            State state = GetOrCreateState<State>(run);
            state.Duration = _duration;

            UnityEngine.Debug.Log($"起飞（时长 {_duration:F2}s，重力 {_gravity}）—— 本次运行状态槽已初始化");
        }

        /// <inheritdoc/>
        public override bool IsCompleted(RevSequenceRun run, RevSequenceContext context)
        {
            State state = GetState<State>(run);
            if (state == null) return true;

            // 时间从 run/context 取（不读 Time.time → 可单测、可快进）
            return run.Elapsed >= state.Duration;
        }
    }

    /// <summary>
    /// 等 Timeline 演出播完（对应原体系 <c>SimpleActionPlayTimelineOrAnimator</c> 的 isWaitUtilFinish）。
    /// <para>状态在**服务**里（谁在播、播多久），所以这个步骤本身不需要私有状态 ——
    /// 这也是"谓词从服务读状态"的安全用法。</para>
    /// </summary>
    public sealed class RevWaitTimelineStep : RevStepBase
    {
        /// <inheritdoc/>
        public override string Name => "等待演出播完";

        /// <inheritdoc/>
        public override void Execute(RevSequenceRun run, RevSequenceContext context)
        {
            // 本步不需要私有状态（谁在播、播多久都在服务里），所以这里只打一条诊断日志。
            // Execute 是抽象方法必须实现 —— 不需要做事就留空实现（框架刻意没给"空实现基类"，
            // 免得你忘了 Server.Ack(...) 这类必须做的事）。
            UnityEngine.Debug.Log("开始等待演出播放结束");
        }

        /// <inheritdoc/>
        public override bool IsCompleted(RevSequenceRun run, RevSequenceContext context)
        {
            IDemoTimelineService timeline = context.Get<IDemoTimelineService>();
            if (timeline == null) return true;

            return !timeline.IsPlaying;
        }
    }
}
