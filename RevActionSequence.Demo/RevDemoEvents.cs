// ============================================================
// RevDemoEvents.cs —— 演示用强类型事件（替代原体系的 SendEvent + param1~param5）
//
// 位置：Assets\Revolution.Demo\RevActionSequence.Demo\
//
// 【对应原体系什么】
//   原体系：SimpleActionSendEvent 发事件，带 5 个通用参数槽（GameObject×4 + string×1），
//           接收端 SimpleTrigger 用 eventTypeStr（字符串）匹配 → 改名字就断链、参数全是弱类型。
//   本框架：事件类型即键 + 泛型负载（想带什么字段就带什么，编译器全程把关）。
//
// 【为什么用 readonly struct】
//   零分配（值类型，不用 new 到堆上），语义清晰；作为事件键时也不会有"谁是谁"的歧义。
//   需要高频广播的事件建议是 struct；如果事件里要带类对象（如某个 Actor），
//   请再显式传 sourceSelector，避免 struct 装箱（见 RevEventTriggerSource 的文件头注释）。
// ============================================================
namespace Revolution.Demo.ActionSequence
{
    /// <summary>宝箱被开启（对应原体系的自定义事件 "chest_opened"）</summary>
    public readonly struct ChestOpenedEvent
    {
        /// <summary>开启者（会成为"开门"序列的 Source，用来判定并发策略）</summary>
        public readonly object Opener;

        /// <summary>宝箱编号</summary>
        public readonly string ChestUid;

        /// <summary>构造事件</summary>
        public ChestOpenedEvent(object opener, string chestUid)
        {
            Opener = opener;
            ChestUid = chestUid;
        }

        /// <inheritdoc/>
        public override string ToString() => $"宝箱开启({ChestUid})";
    }

    /// <summary>灵果被采集（对应 "LingGuoGather" 事件 + 后续发奖链路）</summary>
    public readonly struct LingGuoGatheredEvent
    {
        /// <summary>采集者</summary>
        public readonly object Gatherer;

        /// <summary>灵果 UID（服务器下发的物件唯一 id）</summary>
        public readonly string Uid;

        /// <summary>物品 id</summary>
        public readonly int ItemId;

        /// <summary>构造事件</summary>
        public LingGuoGatheredEvent(object gatherer, string uid, int itemId)
        {
            Gatherer = gatherer;
            Uid = uid;
            ItemId = itemId;
        }

        /// <inheritdoc/>
        public override string ToString() => $"采集灵果({Uid}/{ItemId})";
    }

    /// <summary>进入/离开采集范围（对应 "OnGatherZoneEnter" / "OnGatherZoneExit" 两个事件）</summary>
    public readonly struct GatherZoneEvent
    {
        /// <summary>玩家</summary>
        public readonly object Player;

        /// <summary>采集物 UID</summary>
        public readonly string Uid;

        /// <summary>true = 进入范围，false = 离开范围</summary>
        public readonly bool IsEnter;

        /// <summary>构造事件</summary>
        public GatherZoneEvent(object player, string uid, bool isEnter)
        {
            Player = player;
            Uid = uid;
            IsEnter = isEnter;
        }

        /// <inheritdoc/>
        public override string ToString() => $"采集范围{(IsEnter ? "进入" : "离开")}({Uid})";
    }

    /// <summary>农场礼盒区域进出（对应 "OnGiftBoxZoneEnter" 等）</summary>
    public readonly struct GiftBoxZoneEvent
    {
        /// <summary>玩家</summary>
        public readonly object Player;

        /// <summary>礼盒 UID</summary>
        public readonly string Uid;

        /// <summary>true = 进入，false = 离开</summary>
        public readonly bool IsEnter;

        /// <summary>构造事件</summary>
        public GiftBoxZoneEvent(object player, string uid, bool isEnter)
        {
            Player = player;
            Uid = uid;
            IsEnter = isEnter;
        }

        /// <inheritdoc/>
        public override string ToString() => $"礼盒{(IsEnter ? "进入" : "离开")}({Uid})";
    }

    /// <summary>滑门指令（对应原体系的 SlidingDoorOpen/Close 节点被事件触发）</summary>
    public readonly struct SlidingDoorCommandEvent
    {
        /// <summary>谁是这扇门的拥有者（并发策略按它判定）</summary>
        public readonly object DoorOwner;

        /// <summary>true = 开，false = 关</summary>
        public readonly bool Open;

        /// <summary>构造事件</summary>
        public SlidingDoorCommandEvent(object doorOwner, bool open)
        {
            DoorOwner = doorOwner;
            Open = open;
        }

        /// <inheritdoc/>
        public override string ToString() => $"滑门{(Open ? "开" : "关")}";
    }

    /// <summary>火箭演出播完（对应火箭玩法 Timeline 播完后的后续流程）</summary>
    public readonly struct RocketShowFinishedEvent
    {
        /// <summary>观看者</summary>
        public readonly object Viewer;

        /// <summary>是否走了低画质简化演出</summary>
        public readonly bool LowQuality;

        /// <summary>构造事件</summary>
        public RocketShowFinishedEvent(object viewer, bool lowQuality)
        {
            Viewer = viewer;
            LowQuality = lowQuality;
        }

        /// <inheritdoc/>
        public override string ToString() => $"火箭演出结束({(LowQuality ? "低画质" : "完整")})";
    }
}
