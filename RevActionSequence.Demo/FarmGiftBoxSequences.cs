// ============================================================
// FarmGiftBoxSequences.cs —— 场景三：农场礼盒的"进入 / 离开"双向表现
//
// 位置：Assets\Revolution.Demo\RevActionSequence.Demo\
//
// 【王者原体系怎么做的】
//   A 项目源代码 \ SystemScripts \ DimensionObjectService.cs:360-431
//     礼盒 ServerObject 挂 TriggerParam("OnGiftBoxZoneEnter" ...)，
//     TS 侧在该 trigger 回调里 GetGiftBoxData(uid) 打开礼盒数据；
//   同一文件 1938-2011 行还有"运行时功能物件"的写法：
//     直接 AddComponent&lt;SimpleTrigger&gt;() + 组装 SimpleActionSendEvent，
//     注释写明"用 SimpleTrigger + DimensionTriggerService 替代 SphereCollider + Physics.OverlapCapsule"。
//
// 【这段演示的重点：双向 + 抖动保护】
//   进入 → 打开礼盒面板；离开 → 关面板。
//   但玩家在区域边缘来回走（擦边）会导致面板反复开关 —— 所以"离开"序列里加了一小段等待
//   （等 0.2s 看是不是真的走了）。这类"时间上的缓冲"用序列表达最自然，
//   用 if/else 写就会变成散落各处的"延迟调用/取消调用"。
// ============================================================
namespace Revolution.Demo.ActionSequence
{
    /// <summary>农场礼盒的进入/离开两条序列</summary>
    public static class FarmGiftBoxDemoSequences
    {
        /// <summary>礼盒 UID</summary>
        public const string GiftBoxUid = "giftbox_301";

        /// <summary>
        /// 进入礼盒区域：拉数据 → 打开面板。
        /// <para><b>同源拒绝</b>：同一个玩家在区域内来回走时，不会反复重开面板。</para>
        /// </summary>
        public static readonly RevSequenceDefinition ZoneEnter =
            RevSequence.Create("次元\\农场礼盒-进入", RevSequenceConcurrency.RejectPerSource)
                .Do("请求礼盒数据", ctx => UnityEngine.Debug.Log($"[礼盒] 请求数据 uid={GiftBoxUid}（对应 GetGiftBoxData）"))
                .WaitUntil("等礼盒数据就绪", ctx => true, timeoutSeconds: 3f)     // 真项目里换成"数据到达"的谓词
                .Do("打开礼盒面板", ctx => UnityEngine.Debug.Log("[礼盒] 打开面板（可领取/预览奖励）"))
                .Build();

        /// <summary>
        /// 离开礼盒区域：稍等一下确认真的走了 → 关面板。
        /// <para><b>同源顶替</b>：又进来了就作废这次关闭动作。</para>
        /// </summary>
        public static readonly RevSequenceDefinition ZoneExit =
            RevSequence.Create("次元\\农场礼盒-离开", RevSequenceConcurrency.ReplacePerSource)
                .Wait(0.2f)                     // 等一下确认真的离开（防擦边闪烁）
                .Do("关闭礼盒面板", ctx => UnityEngine.Debug.Log("[礼盒] 关闭面板"))
                .Build();

        /// <summary>
        /// 功能物件的 enter / exit 两条（对应 DimensionObjectService:1938-2011 的运行时功能物件）。
        /// <para>原体系在代码里动态 AddComponent 组两个 SimpleActionSendEvent；
        /// 现在只需两行：写两条序列 + 两个触发源绑定。</para>
        /// </summary>
        public static readonly RevSequenceDefinition FunctionalEnter =
            RevSequence.Create("次元\\功能物件-进入")
                .Do("广播功能物件进入", ctx => UnityEngine.Debug.Log("[功能物件] OnZoneEnter → 交给业务处理"))
                .Build();

        /// <summary>功能物件离开（与上面成对 —— 注册什么就反注册什么，是同一纪律）</summary>
        public static readonly RevSequenceDefinition FunctionalExit =
            RevSequence.Create("次元\\功能物件-离开")
                .Do("广播功能物件离开", ctx => UnityEngine.Debug.Log("[功能物件] OnZoneExit → 交给业务处理"))
                .Build();
    }
}
