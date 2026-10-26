// ============================================================
// RocketTimelineSequence.cs —— 场景五：火箭玩法演出（预加载 + 并行 + 降级 + 回收）
//
// 位置：Assets\Revolution.Demo\RevActionSequence.Demo\
//
// 【王者原体系怎么做的】
//   DimensionSceneMgr.cs:2268-2403
//     演出 Prefab 预加载 / 缓存池（StartRocketTimelinePreload → GetRocketTimelineGo → RecycleRocketTimelineGo），
//     低画质设备替换 Show2/Show3 的材质走简化演出（:2432）。
//   注意原体系这套"预加载 → 取用 → 播 → 回收"是**散在几个方法里的**：
//   谁先谁后靠调用顺序保证，没有一处能一眼看完整条流程。
//
// 【本框架怎么表达同一件事】
//   一条序列从上到下读完：预加载 → 等就绪 →（音乐 ∥ 演出）→ 等演出完 → 收尾 → 广播。
//   两个"原体系做不到"的点：
//     ① 等就绪用了带超时的 WaitUntil（超时是"放行 + 告警"，不会永久卡死）；
//     ② 被取消时由 OnCancel 保证回收（原体系回收散在回调里，漏一处就是资源泄漏）。
//
// 【关于"低画质降级"这件业务判定】
//   框架不内建条件分支步骤（保持轻量），降级就是一个普通 if：
//   在步骤里读服务给的结果即可。要"两条完全不同的流程"就构建两个 Definition，
//   在入口处按设备等级选一条播 —— 比在序列里塞 if 更清楚。
// ============================================================
namespace Revolution.Demo.ActionSequence
{
    /// <summary>火箭玩法的演出序列（对应 DimensionSceneMgr 的 Timeline 预加载/播完回收）</summary>
    public static class RocketTimelineDemoSequences
    {
        /// <summary>演出 Prefab 路径（原体系里是 Prefab_RocketGame_Timeline）</summary>
        public const string TimelinePath = "Prefab_RocketGame_Timeline";

        /// <summary>
        /// 完整演出：预加载 → 等就绪 →（背景乐 ∥ 演出本体）→ 等播完 → 收尾广播。
        /// <para><b>同源拒绝</b>：演出期间重复触发直接忽略（对应原体系"演出期间不接受新请求"）。</para>
        /// </summary>
        public static readonly RevSequenceDefinition Show =
            RevSequence.Create("次元\\火箭演出", RevSequenceConcurrency.RejectPerSource)
                .Do("预加载演出资源", ctx => Timeline(ctx)?.Load(TimelinePath))
                .WaitUntil("等预加载完成", ctx => Timeline(ctx)?.IsLoaded(TimelinePath) == true,
                           timeoutSeconds: 10f)
                .Parallel("演出与音乐同时开始", p => p
                    .Do("播放背景音乐", ctx => Sound(ctx)?.PostEvent("Play_CiYuan_Rocket_Music"))
                    .Do("播放演出", ctx => Timeline(ctx)?.Play(TimelinePath, IsLowQuality(ctx))))
                .Step(new RevWaitTimelineStep())                        // 阻塞：等演出播完
                .Do("播放收尾音效", ctx => Sound(ctx)?.PostEvent("Play_CiYuan_Rocket_End"))
                .Do("广播演出结束", ctx => ctx.Events.Publish(
                        new RocketShowFinishedEvent(ctx.Source, IsLowQuality(ctx))))
                .OnCancel(finallySteps => finallySteps
                    .Do("停止演出并回收实例", ctx => Timeline(ctx)?.Stop()))
                .Build();

        /// <summary>
        /// 【对照用】低画质设备走"另一条更短的流程"（而不是在一条序列里塞 if 分支）。
        /// <para>入口处按设备等级二选一：<c>runner.Play(IsLowQuality() ? ShortShow : Show)</c>。</para>
        /// </summary>
        public static readonly RevSequenceDefinition ShortShow =
            RevSequence.Create("次元\\火箭演出（低画质简化版）", RevSequenceConcurrency.RejectPerSource)
                .Do("预加载简化演出资源", ctx => Timeline(ctx)?.Load(TimelinePath))
                .WaitUntil("等预加载完成", ctx => Timeline(ctx)?.IsLoaded(TimelinePath) == true,
                           timeoutSeconds: 6f)
                .Do("播放简化演出", ctx => Timeline(ctx)?.Play(TimelinePath, lowQuality: true))
                .Step(new RevWaitTimelineStep())
                .Do("广播演出结束", ctx => ctx.Events.Publish(new RocketShowFinishedEvent(ctx.Source, true)))
                .OnCancel(finallySteps => finallySteps
                    .Do("停止演出并回收实例", ctx => Timeline(ctx)?.Stop()))
                .Build();

        private static bool IsLowQuality(RevSequenceContext ctx)
            => ctx.Get<IDemoTimelineService>()?.IsLowQualityDevice ?? false;

        private static IDemoTimelineService Timeline(RevSequenceContext ctx) => ctx.Get<IDemoTimelineService>();

        private static IDemoSoundService Sound(RevSequenceContext ctx) => ctx.Get<IDemoSoundService>();
    }
}
