// ============================================================
// LingGuoGatherSequences.cs —— 场景二：灵果采集（进出区域 + 抛物线收取 + 发奖）
//
// 位置：Assets\Revolution.Demo\RevActionSequence.Demo\
//
// 【王者原体系怎么做的】（A 项目源代码 \ SystemScripts \ DimensionObjectService.cs:585-666）
//   服务器下发灵果 → AddServerObject 生成物件，挂三个参数：
//     DimensionObjectTriggerParam("LingGuoGather", "", m_lingguoDistance, uid)   ← 采集事件 + 触发半径
//     MoveParam（水平速度 + 重力）                                              ← 抛物线飞向玩家
//     WwiseParam("Play_CiYuan_UI_LingGuo_Get",
//                "Play_CiYuan_UI_LingGuo_Disappear",
//                "Play_CiYuan_UI_LingGuo_Fall")                                 ← 三个音效
//   采集距离从全局配置读（RES_GLOBAL_CONF_TYPE_LINGGUO_CLIENT_COLLECT_DISTANCE）。
//
// 【四条序列 + 各自的并发策略选择理由】
//   ① 可采集提示（进入区域）：RejectPerSource —— 已在提示中就不要重复播
//   ② 采集灵果：ReplacePerSource —— 玩家连点只留最新一次（音效/表现都是"最后一次算数"）
//   ③ 离开区域关提示：Free —— 无所谓顺序，来几次关几次
//   ④ 等服务器确认再发奖：RejectPerSource —— 请求期间连点不重复发请求（见 GatherWithServerConfirm）
//
// 【★ "等异步"的两种写法（都能缓存清单，按业务形态挑一个）】
//   · 等"业务服务的状态"：DoBlocking / WaitUntil（适合"pending / 结果由我自己管"的场合）
//     → GatherWithServerConfirm
//   · 等"一个异步任务"：WaitTask(ctx => 任务)（适合"任务本身就是异步 API 的返回值"的场合）
//     → WaitTaskDemo
// ============================================================
using System;

namespace Revolution.Demo.ActionSequence
{
    /// <summary>灵果采集的三条序列（对应原体系灵果物件的进入/离开/采集三段表现）</summary>
    public static class LingGuoDemoSequences
    {
        /// <summary>灵果 UID（服务器下发）</summary>
        public const string LingGuoUid = "lingguo_2207";

        /// <summary>灵果物品 id</summary>
        public const int LingGuoItemId = 2207;

        /// <summary>采集距离（米）—— 对应全局配置里的采集距离</summary>
        public const float CollectDistance = 1.5f;

        /// <summary>
        /// 进入采集范围：落地音效 → 显示"可采集"提示。
        /// <para>对应原体系 <c>DimensionObjectTriggerParam("OnGatherZoneEnter", ...)</c>。</para>
        /// </summary>
        public static readonly RevSequenceDefinition ZoneEnter =
            RevSequence.Create("次元\\灵果-可采集提示", RevSequenceConcurrency.RejectPerSource)
                .Do("播放灵果落地音效", ctx => Sound(ctx)?.PostEvent("Play_CiYuan_UI_LingGuo_Fall"))
                .Do("显示可采集提示", ctx => UnityEngine.Debug.Log($"[灵果] 显示可采集提示（UID={LingGuoUid}）"))
                .Build();

        /// <summary>
        /// 离开采集范围：关掉提示。
        /// <para>对应 <c>OnGatherZoneExit</c>。这里刻意**不加等待**：离开是"撤销"，越快越好。</para>
        /// </summary>
        public static readonly RevSequenceDefinition ZoneExit =
            RevSequence.Create("次元\\灵果-关闭可采集提示", RevSequenceConcurrency.Free)
                .Do("隐藏可采集提示", ctx => UnityEngine.Debug.Log("[灵果] 隐藏可采集提示"))
                .Build();

        /// <summary>
        /// 采集灵果：采集音效 →（消失音效 ∥ 抛物线飞向玩家）→ 发奖 → 广播事件；被取消则回收物件。
        /// <para><b>为什么用 Parallel</b>："消失音效"与"飞行动画"在业务上是同时发生的，
        /// 不需要排队等 —— 这正是原体系纯线性序列做不到的事（原体系无并行）。</para>
        /// <para><b>为什么 OnCancel 必要</b>：玩家采到一半掉线/切场景，物件必须回收，
        /// 否则场上会留下飞到一半的灵果（原体系靠节点 OnDisable 自觉兜底，漏了就没救）。</para>
        /// </summary>
        public static readonly RevSequenceDefinition Gather =
            RevSequence.Create("次元\\灵果采集", RevSequenceConcurrency.ReplacePerSource)
                .Do("播放采集音效", ctx => Sound(ctx)?.PostEvent("Play_CiYuan_UI_LingGuo_Get"))
                .Parallel("消失表现与飞行动画同时进行", p => p
                    .Do("播放消失音效", ctx => Sound(ctx)?.PostEvent("Play_CiYuan_UI_LingGuo_Disappear"))
                    .Step(new RevFlyParabolaStep(duration: 0.35f, gravity: 9.8f)))
                .Do("发放灵果奖励", ctx => ctx.Get<IDemoRewardService>()?.Grant("LingGuo", 1, ctx.Source))
                .Do("广播采集事件", ctx => ctx.Events.Publish(
                        new LingGuoGatheredEvent(ctx.Source, LingGuoUid, LingGuoItemId)))
                .OnCancel(finallySteps => finallySteps
                    .Do("回收灵果物件", ctx => UnityEngine.Debug.Log($"[灵果] 采集被打断 → 回收物件 {LingGuoUid}")))
                .Build();

        /// <summary>
        /// 采集前等"服务器确认"再发奖 —— <b>推荐写法</b>（定义可缓存 + 可复用，策略有效）。
        /// <para><b>为什么用 <c>DoBlocking</c> 而不是 <c>WaitTask</c></b>：序列模板是加载期构建一次、运行期复用的，
        /// 而 <c>WaitTask</c> 的载荷（那个 task 对象）也是构建期固定的 —— "每次请求都要等不同的回包"根本没法缓存进定义。
        /// 于是把"等待"改造成 <b>服务状态 + 每帧问一次</b>：请求的 pending 与结果都放在业务服务里，定义照样缓存。</para>
        /// <para><b>为什么 RejectPerSource</b>：同一次采集请求期间连点，不重复发请求（也就不会重复发奖）。</para>
        /// </summary>
        public static readonly RevSequenceDefinition GatherWithServerConfirm =
            RevSequence.Create("次元\\灵果采集（等服务器确认）", RevSequenceConcurrency.RejectPerSource)
                .DoBlocking("请求服务器确认",
                    execute: ctx => Protocol(ctx)?.RequestGatherConfirm(LingGuoUid, ctx.Source),
                    isCompleted: ctx => Protocol(ctx)?.IsGatherConfirmDone(LingGuoUid) == true)
                .Do("按回包结果发放奖励", ctx =>
                {
                    if (Protocol(ctx)?.GetGatherConfirmResult(LingGuoUid) == true)
                        ctx.Get<IDemoRewardService>()?.Grant("LingGuo", 1, ctx.Source);
                    else
                        UnityEngine.Debug.LogWarning("[灵果] 服务器拒绝本次采集 → 不发奖（真实项目里这里还要回收物件）");
                })
                .Build();

        /// <summary>
        /// 用 <c>WaitTask</c> 直接等一个异步任务。
        /// <para>★ 传的是<b>工厂</b>（<c>ctx =&gt; 任务</c>），任务在运行时才创建 —— 所以这条清单和别的清单一样
        /// 可以 <c>static readonly</c> 缓存，而且连"同源拒绝"策略都能正常生效。</para>
        /// <para>和上面那条（等业务服务状态）的区别：这条直接等一个 <see cref="RevTask"/>，
        /// 适合"任务本身就是异步 API 的返回值"的场合（资源加载、网络回包）。</para>
        /// <para>★ <c>WaitTask</c> 只吃<b>无返回值</b>的 <see cref="RevTask"/> —— <c>RevTask</c> 与
        /// <c>RevTask&lt;T&gt;</c> 是两个独立结构体（刻意不做隐式转换）；带返回值的回调式接口先用
        /// <see cref="RevTaskCompletionSource"/> 包一层（见 <see cref="RequestGatherConfirm"/>）。</para>
        /// </summary>
        public static readonly RevSequenceDefinition WaitTaskDemo =
            RevSequence.Create("次元\\灵果采集（等异步任务）", RevSequenceConcurrency.RejectPerSource)
                .WaitTask(ctx => RequestGatherConfirm(ok => UnityEngine.Debug.Log($"[协议] 回调式回包：{ok}")),
                          "等异步任务返回")
                .Do("发放奖励", ctx => ctx.Get<IDemoRewardService>()?.Grant("LingGuo", 1, ctx.Source))
                .Build();

        /// <summary>
        /// 把"回调式协议"包成 <see cref="RevTask"/> 的标准写法（老接口大多是回调风格，这一步是接入序列的桥）。
        /// <para><c>Forget()</c> = "我故意不等它"，是框架提供的显式忽略入口。</para>
        /// </summary>
        public static RevTask RequestGatherConfirm(Action<bool> callback)
        {
            RevTaskCompletionSource source = RevTask.CreateSource();

            // 假协议：0.3 秒后回包
            FakeProtocolAsync(callback, source).Forget();

            return source.Task;
        }

        private static async RevTask FakeProtocolAsync(Action<bool> callback, RevTaskCompletionSource source)
        {
            await RevTask.Delay(300);
            callback?.Invoke(true);
            source.SetResult();
        }

        private static IDemoProtocolService Protocol(RevSequenceContext ctx) => ctx.Get<IDemoProtocolService>();

        private static IDemoSoundService Sound(RevSequenceContext ctx) => ctx.Get<IDemoSoundService>();
    }
}
