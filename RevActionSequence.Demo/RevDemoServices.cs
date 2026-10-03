// ============================================================
// RevDemoServices.cs —— 演示用"业务服务"（动作序列的业务依赖都从这里进）
//
// 位置：Assets\Revolution.Demo\RevActionSequence.Demo\
//
// 【这份文件演示框架最关键的一条约束】
//   框架自己**不认识任何业务**：所有"播音效 / 生成特效 / 推镜头 / 播 Timeline / 发奖"
//   都是通过 <c>context.Get&lt;IXXXService&gt;()</c> 拿到接口再调，
//   接口定义在业务侧（本文件），实现也在业务侧（下面那个 Hub）。
//
//   对照王者原体系（SimpleActionPlaySound 直接调 CSoundManager.GetInstance()）：
//   那边节点与业务系统硬耦合 → 无法单测、无法脱离项目编译；
//   这边同一个动作序列可以换一套服务实现（真实现 / Fake / 录播）跑，行为不变。
//
// 【七个接口对应原体系哪些节点 / 能力】
//   IDemoSoundService    ← SimpleActionPlaySound / SimpleActionResumeMusic
//   IDemoEffectService   ← SimpleActionSpawnObject（生成 + 回收）
//   IDemoCameraService   ← DimensionNpcCamera.StartCloseup / StopCloseup（相机特写）
//   IDemoTimelineService ← SimpleActionPlayTimelineOrAnimator（演出 + 预加载 + 降级）
//   IDemoInputService    ← SimpleActionWaitInput + DimensionTriggerService 的距离判定
//   IDemoRewardService   ← 发奖协议（原体系走 SendCustomEvent，属于业务）
//   IDemoProtocolService ← 网络请求（演示"等服务器回包"怎么接进序列，见 LingGuoGatherSequences）
//
// 【★ 注册时必须显式写接口类型】
//   services.Add&lt;IDemoSoundService&gt;(hub);   ← 正确：键是接口
//   services.Add(hub);                    ← 错：键变成具体实现类，按接口取不到
// ============================================================
using System.Collections.Generic;
using UnityEngine;

namespace Revolution.Demo.ActionSequence
{
    /// <summary>音效（对应 CSoundManager.PostEvent）</summary>
    public interface IDemoSoundService
    {
        /// <summary>播一个 Wwise 事件</summary>
        void PostEvent(string eventName, object emitter = null);
    }

    /// <summary>特效/生成物（对应 SimpleActionSpawnObject）</summary>
    public interface IDemoEffectService
    {
        /// <summary>生成一个演出物件（返回它的"句柄"，回收时用）</summary>
        object Spawn(string prefabPath, object owner);

        /// <summary>回收演出物件</summary>
        void Recycle(object spawned);
    }

    /// <summary>相机（对应 DimensionNpcCamera 的特写推近/恢复）</summary>
    public interface IDemoCameraService
    {
        /// <summary>推近到某个目标的特写（fov 越小越近）</summary>
        void PushIn(object target, float fov, float duration);

        /// <summary>恢复默认机位</summary>
        void Restore();
    }

    /// <summary>Timeline/演出（对应 SimpleActionPlayTimelineOrAnimator + 火箭玩法的预加载/降级）</summary>
    public interface IDemoTimelineService
    {
        /// <summary>资源是否已加载（对应"预加载完成"判定）</summary>
        bool IsLoaded(string prefabPath);

        /// <summary>预加载（对应 StartRocketTimelinePreload）</summary>
        void Load(string prefabPath);

        /// <summary>播放（<paramref name="lowQuality"/> = 低画质设备走简化演出）</summary>
        void Play(string prefabPath, bool lowQuality);

        /// <summary>是否正在播</summary>
        bool IsPlaying { get; }

        /// <summary>是否低画质设备（对应原体系"低画质替换 Show2/Show3 材质"的降级判定）</summary>
        bool IsLowQualityDevice { get; }

        /// <summary>停止并归还实例（对应 RecycleRocketTimelineGo）</summary>
        void Stop();
    }

    /// <summary>玩家交互（对应 DimensionHostInteractionService.PlayerActiveThisFrame + 距离判定）</summary>
    public interface IDemoInputService
    {
        /// <summary>本帧玩家是否点了交互键</summary>
        bool PlayerActiveThisFrame { get; }

        /// <summary>消费一次点击（消费掉之后同帧不会重复触发第二次）</summary>
        bool ConsumeClick();

        /// <summary>玩家到某个目标的距离（米）</summary>
        float DistanceTo(object target);
    }

    /// <summary>发奖（业务侧协议）</summary>
    public interface IDemoRewardService
    {
        /// <summary>发一份奖励</summary>
        void Grant(string rewardId, int count, object source);
    }

    /// <summary>场景表现（滑门、HUD、角色显隐 —— 业务类动作的落脚点）</summary>
    public interface IDemoSceneService
    {
        /// <summary>开/关滑门（对应原体系的 SlidingDoorOpen/Close 节点）</summary>
        void SetSlidingDoor(bool open);

        /// <summary>HUD 显隐（特写演出时隐藏 HUD）</summary>
        void SetHudVisible(bool visible);

        /// <summary>角色显隐</summary>
        void SetActorVisible(object actor, bool visible);
    }

    /// <summary>
    /// 网络请求（对应原体系的协议层）。
    /// <para>★ 这里刻意做成"<b>发起 + 查询状态</b>"三个方法，而不是"返回一个 Task" —— 原因很实际：
    /// 序列模板（<see cref="RevSequenceDefinition"/>）是<b>加载期构建一次、运行期复用</b>的，
    /// 而 <c>WaitTask</c> 的载荷（那个 task 对象）也是构建期就固定的。
    /// 所以"每次请求都要等不同的回包"必须用 <b>服务状态 + DoBlocking/WaitUntil（每帧问一次）</b> 表达，
    /// 这样定义才能被缓存、并发策略也才有意义（对比 <c>WaitTask</c>：它适合"任务在构建时刻就存在"的情形）。</para>
    /// </summary>
    public interface IDemoProtocolService
    {
        /// <summary>发起一次"采集确认"请求（异步回包）</summary>
        void RequestGatherConfirm(string uid, object player);

        /// <summary>这次请求是否已经回包（每帧被问一次）</summary>
        bool IsGatherConfirmDone(string uid);

        /// <summary>回包结果（true = 服务器同意采集）</summary>
        bool GetGatherConfirmResult(string uid);
    }

    /// <summary>
    /// 演示用假实现：所有调用打一条日志（真项目里换成你自己的系统）。
    /// <para>刻意做成"能计时"的 —— Timeline 播 N 秒后自动置为播完、协议 0.3s 后自动回包，
    /// 这样 Demo 里"等演出播完 / 等服务器回包"的阻塞步骤能被真实驱动起来。</para>
    /// </summary>
    public sealed class RevDemoServiceHub : IDemoSoundService,
                                            IDemoEffectService,
                                            IDemoCameraService,
                                            IDemoTimelineService,
                                            IDemoInputService,
                                            IDemoRewardService,
                                            IDemoSceneService,
                                            IDemoProtocolService
    {
        private readonly List<string> _spawned = new List<string>(4);
        private readonly HashSet<string> _loaded = new HashSet<string>();
        private string _playingPath;
        private float _playRemain;

        /// <summary>本帧玩家是否按了交互键（由 Demo 入口在按键时置位）</summary>
        public bool ClickPressed;

        /// <summary>假设的"玩家位置"，用于距离判定</summary>
        public Vector3 PlayerPosition = Vector3.zero;

        // ── IDemoSoundService ──────────────────────────────────────
        /// <inheritdoc/>
        public void PostEvent(string eventName, object emitter = null)
            => Debug.Log($"[音效] PostEvent({eventName})  触发者={(emitter == null ? "无" : emitter.GetType().Name)}");

        // ── IDemoEffectService ────────────────────────────────────
        /// <inheritdoc/>
        public object Spawn(string prefabPath, object owner)
        {
            _spawned.Add(prefabPath);
            Debug.Log($"[特效] 生成 {prefabPath}（当前已生成 {_spawned.Count} 个）");
            return prefabPath;
        }

        /// <inheritdoc/>
        public void Recycle(object spawned)
        {
            string path = spawned as string;
            if (path != null) _spawned.Remove(path);
            Debug.Log($"[特效] 回收 {path}（剩余 {_spawned.Count} 个）");
        }

        // ── IDemoCameraService ────────────────────────────────────
        /// <inheritdoc/>
        public void PushIn(object target, float fov, float duration)
            => Debug.Log($"[相机] 特写推近：目标={target?.GetType().Name} FOV={fov} 用时={duration:F2}s");

        /// <inheritdoc/>
        public void Restore() => Debug.Log("[相机] 恢复默认机位");

        // ── IDemoTimelineService ──────────────────────────────────
        /// <inheritdoc/>
        public bool IsLoaded(string prefabPath) => _loaded.Contains(prefabPath);

        /// <inheritdoc/>
        public void Load(string prefabPath)
        {
            _loaded.Add(prefabPath);
            Debug.Log($"[演出] 预加载完成 {prefabPath}");
        }

        /// <inheritdoc/>
        public void Play(string prefabPath, bool lowQuality)
        {
            _playingPath = prefabPath;
            _playRemain = lowQuality ? 1.2f : 2.0f;      // 低画质走简化版演出，时长更短
            Debug.Log($"[演出] 开始播放 {prefabPath}（{(lowQuality ? "低画质简化版" : "完整版")}）");
        }

        /// <inheritdoc/>
        public bool IsPlaying => _playRemain > 0f;

        /// <inheritdoc/>
        public bool IsLowQualityDevice => LowQualityDevice;

        /// <inheritdoc/>
        public void Stop()
        {
            if (_playingPath == null) return;
            Debug.Log($"[演出] 停止并回收 {_playingPath}");
            _playingPath = null;
            _playRemain = 0f;
        }

        // ── IDemoInputService ─────────────────────────────────────
        /// <inheritdoc/>
        public bool PlayerActiveThisFrame => ClickPressed;

        /// <inheritdoc/>
        public bool ConsumeClick()
        {
            if (!ClickPressed) return false;
            ClickPressed = false;       // 消费掉：避免同一帧被两个序列各吃掉一次
            return true;
        }

        /// <inheritdoc/>
        public float DistanceTo(object target) => 0.8f;     // 演示里恒定为"已靠近"

        // ── IDemoRewardService ────────────────────────────────────
        /// <inheritdoc/>
        public void Grant(string rewardId, int count, object source)
            => Debug.Log($"[发奖] {rewardId} × {count}  来源={(source == null ? "系统" : source.GetType().Name)}");

        // ── IDemoSceneService ─────────────────────────────────────
        /// <inheritdoc/>
        public void SetSlidingDoor(bool open) => Debug.Log($"[场景] 滑门{(open ? "打开" : "关闭")}");

        /// <inheritdoc/>
        public void SetHudVisible(bool visible) => Debug.Log($"[场景] HUD {(visible ? "显示" : "隐藏")}");

        /// <inheritdoc/>
        public void SetActorVisible(object actor, bool visible)
            => Debug.Log($"[场景] 角色{(visible ? "显示" : "隐藏")}");

        /// <summary>Demo 入口每帧调用：驱动假 Timeline 的播放进度（真项目里由 Timeline 自己推进）</summary>
        public void Tick(float deltaTime)
        {
            TickTimeline(deltaTime);
            TickProtocol(deltaTime);
        }

        private void TickTimeline(float deltaTime)
        {
            if (_playRemain <= 0f) return;

            _playRemain -= deltaTime;
            if (_playRemain <= 0f)
            {
                Debug.Log($"[演出] 播放结束 {_playingPath}");
                _playingPath = null;
                _playRemain = 0f;
            }
        }

        // ── IDemoProtocolService（假协议：按 uid 记录一次请求的待回包 / 结果）──
        private sealed class ConfirmState
        {
            public bool Pending;
            public bool Granted;
            public float Remain;
        }

        private readonly Dictionary<string, ConfirmState> _confirm = new Dictionary<string, ConfirmState>();

        /// <inheritdoc/>
        public void RequestGatherConfirm(string uid, object player)
        {
            _confirm[uid] = new ConfirmState { Pending = true, Remain = 0.3f };
            Debug.Log($"[协议] → 请求采集确认 uid={uid}（等服务器回包…）");
        }

        /// <inheritdoc/>
        public bool IsGatherConfirmDone(string uid)
            => _confirm.TryGetValue(uid, out ConfirmState state) && !state.Pending;

        /// <inheritdoc/>
        public bool GetGatherConfirmResult(string uid)
            => _confirm.TryGetValue(uid, out ConfirmState state) && state.Granted;

        private void TickProtocol(float deltaTime)
        {
            if (_confirm.Count == 0) return;

            // 注意：这里只改"值对象的字段"，没有增删字典键 —— 遍历中修改值是安全的
            foreach (KeyValuePair<string, ConfirmState> pair in _confirm)
            {
                ConfirmState state = pair.Value;
                if (!state.Pending) continue;

                state.Remain -= deltaTime;
                if (state.Remain > 0f) continue;

                state.Pending = false;
                state.Granted = true;
                Debug.Log($"[协议] ← 收到回包 uid={pair.Key} 结果=同意");
            }
        }

        /// <summary>建一个服务容器（内部自建一个 Hub）</summary>
        public static RevSequenceServices CreateServices() => CreateServices(new RevDemoServiceHub());

        /// <summary>
        /// 用一个已有的 Hub 建服务容器（Demo 入口要留着 Hub 引用来模拟输入/驱动假演出）。
        /// <para>★ 注意每个 Add 都显式写了接口类型 —— 这是本项目容易踩的坑（见文件头注释）。</para>
        /// </summary>
        public static RevSequenceServices CreateServices(RevDemoServiceHub hub)
        {
            return new RevSequenceServices(capacity: 8)
                .Add<IDemoSoundService>(hub)
                .Add<IDemoEffectService>(hub)
                .Add<IDemoCameraService>(hub)
                .Add<IDemoTimelineService>(hub)
                .Add<IDemoInputService>(hub)
                .Add<IDemoRewardService>(hub)
                .Add<IDemoSceneService>(hub)
                .Add<IDemoProtocolService>(hub);
        }

        /// <summary>是否低画质设备（决定演出走完整版还是简化版；对应原体系的材质替换降级）</summary>
        public bool LowQualityDevice { get; set; }
    }
}
