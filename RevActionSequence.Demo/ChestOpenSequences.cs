// ============================================================
// ChestOpenSequences.cs —— 场景一：宝箱 → 等待点击 → 开门（原体系文档里唯一的完整样例）
//
// 位置：Assets\Revolution.Demo\RevActionSequence.Demo\
//
// 【王者原体系怎么配的】（《05_配置与使用指南》39~55 行）
//   GameObject「宝箱触发器」：SimpleTrigger（球半径 2000mm，触发次数 1）
//     enterActions = [ SimpleActionPlaySound(Play_Box_Appear) → SimpleActionWaitInput
//                      → SimpleActionSendEvent("chest_opened") ]
//   GameObject「门触发器」：SimpleTrigger（监听自定义事件 "chest_opened"）
//     listenEventActions = [ SimpleTriggerActionSlidingDoorOpen ]
//
// 【改成代码驱动之后，一一对应关系】
//   球半径 2000mm + 触发次数 1   → RevDemoZoneTriggerSource(半径 2f) + RejectPerSource（同源拒绝）
//   PlaySound(Play_Box_Appear)   → .Do(音效服务.PostEvent)
//   WaitInput                    → .Step(new RevWaitClickStep())   ← 阻塞，玩家点了才放行
//   SendEvent("chest_opened")    → .Do(ctx => ctx.Events.Publish(new ChestOpenedEvent(...)))  ← 强类型替代字符串
//   门监听事件 → SlidingDoorOpen  → runner.BindEvents&lt;ChestOpenedEvent&gt;(...)（见 Demo 入口）
//
// 【★ 用 .Publish(evt) 还是 .Do + Events.Publish？】
//   .Publish(evt) 的载荷在**构建期**就固定了，适合"常量事件"（如固定的开/关指令）；
//   载荷依赖运行期数据（触发者是谁、uid 是什么）时必须用 .Do + ctx.Events.Publish —— 本文件即如此。
// ============================================================
namespace Revolution.Demo.ActionSequence
{
    /// <summary>宝箱与滑门的两条序列（对应原体系的 enterActions / listenEventActions）</summary>
    public static class ChestDemoSequences
    {
        /// <summary>宝箱编号（对应服务器下发的物件 uid）</summary>
        public const string ChestUid = "chest_1601";

        /// <summary>
        /// 宝箱开启：出现音效 → 等玩家点击 → 开箱音效 → 广播"宝箱已开"。
        /// <para><b>并发策略 = 同源拒绝</b>：对应原体系"触发次数 1"，
        /// 同一个玩家反复进出区域不会重复播这套演出。</para>
        /// </summary>
        public static readonly RevSequenceDefinition ChestOpen =
            RevSequence.Create("次元\\宝箱开启", RevSequenceConcurrency.RejectPerSource)
                .Do("播放宝箱出现音效", ctx => Sound(ctx)?.PostEvent("Play_Box_Appear", ctx.Source))
                .Step(new RevWaitClickStep())                               // 阻塞：等玩家点交互键
                .Do("播放开箱音效", ctx => Sound(ctx)?.PostEvent("Play_Box_Open", ctx.Source))
                .Do("广播宝箱已开", ctx => ctx.Events.Publish(new ChestOpenedEvent(ctx.Source, ChestUid)))
                .Build();

        /// <summary>
        /// 滑门打开：**被"宝箱已开"事件触发**（对应原体系门触发器的 listenEventActions）。
        /// <para>用 <b>同源顶替</b>：同一个玩家重复触发时，旧的那扇门动画立刻让位给新的 —— 
        /// 这正是原体系"多 actor 并发触发同一节点互相踩状态"那个缺陷的正解：
        /// 状态不再存在节点上，而是由发动机按策略管。</para>
        /// </summary>
        public static readonly RevSequenceDefinition SlidingDoorOpen =
            RevSequence.Create("次元\\宝箱门开启（事件触发）", RevSequenceConcurrency.ReplacePerSource)
                .Do("播放滑门音效", ctx => Sound(ctx)?.PostEvent("Play_Door_Slide_Open"))
                .Do("滑门打开", ctx => Scene(ctx)?.SetSlidingDoor(true))
                .Build();

        /// <summary>
        /// 【对照用】如果不用事件、想让宝箱"直接开一条完整流程"，
        /// 就把门序列嵌套进来 —— 嵌套时父序列被取消，子序列也会被一起取消（不留孤儿演出）。
        /// </summary>
        public static readonly RevSequenceDefinition ChestOpenAndDoorInline =
            RevSequence.Create("次元\\宝箱开启并开门（嵌套写法）")
                .Sequence("先开宝箱", ChestOpen)
                .Sequence("再开门", SlidingDoorOpen)
                .Build();

        private static IDemoSoundService Sound(RevSequenceContext ctx) => ctx.Get<IDemoSoundService>();

        private static IDemoSceneService Scene(RevSequenceContext ctx) => ctx.Get<IDemoSceneService>();
    }
}
