// ============================================================
// RevActionSequenceDemo.cs —— Demo 入口：把五个王者真实场景跑起来
//
// 位置：Assets\Revolution.Demo\RevActionSequence.Demo\
//
// 【怎么用】
//   把本脚本挂到场景里任意一个 GameObject 上（它同时充当"玩家"这个触发者），运行后按：
//
//     1  玩家进入「宝箱区域」   → 宝箱序列开始（播出现音效 → 等点击）
//     空格 模拟玩家点击交互     → 开箱 → 广播事件 → 自动触发「滑门打开」序列
//     2  玩家进入「灵果区域」   → 可采集提示
//     3  采集灵果（连按体验并发策略）→ 采集音效 ∥ 飞行动画 → 发奖 → 广播
//     4  玩家离开「灵果区域」   → 关闭提示
//     5 / 6  农场礼盒 进入 / 离开
//     7  NPC 特写   → 推近 → 等对白（空格关闭）
//     8  取消当前特写（体验 OnCancel 强制恢复镜头）
//     9  火箭演出（Inspector 勾 Low Quality Device 即走简化版）
//     A  取消所有序列（体验取消 + 收尾）
//     Q  打印当前活跃序列（对应调试面板）
//     P  await 一条序列（体验 PlayAsync）
//     O  等服务器回包再发奖 —— ★推荐写法（服务状态 + 每帧问，定义可缓存）
//     L  等异步任务（WaitTask：传 ctx => 任务，清单可缓存、策略也有效）
//
// 【为什么 Demo 用自己 new 的 Runner，而不是 RevSequencePlayer.Default】
//   为了同时演示"引擎不是单例、Tick 由谁决定"：
//     · 正式项目最省事是 <c>RevSequencePlayer.Default</c>（自动创建驱动者，零配置）；
//     · 单元测试里则是自己 new 一个 + 手动 Tick（本 Demo 就是这个姿势）。
// ============================================================
using System;
using UnityEngine;

namespace Revolution.Demo.ActionSequence
{
    /// <summary>动作序列框架 Demo 入口（按数字键体验五种王者真实场景）</summary>
    public sealed class RevActionSequenceDemo : MonoBehaviour
    {
        // 由 Inspector 赋值（代码里不该写它）；显式给默认值是为了消除 CS0649 警告（非 Unity 编译环境也会报）
        [Tooltip("勾上 = 演出走低画质简化版（对应原体系的设备等级降级）")]
        [SerializeField] private bool _lowQualityDevice = false;

        private RevSequenceRunner _runner;
        private RevDemoServiceHub _hub;

        // 触发源与绑定：注册什么就反注册什么（OnDestroy 里成对释放）
        private RevDemoZoneTriggerSource _chestZone;
        private IDisposable _chestZoneBinding;
        private IDisposable _chestOpenedBinding;

        // 当前演示用的句柄（句柄 = 判断有效性 + 取消）
        private RevSequenceHandle _closeupHandle;
        private RevSequenceHandle _rocketHandle;

        // ★ 框架不提供查询（没有"现在几条在跑""卡在哪一步"这类 API）：
        //   要观察就在回调里自己记 —— 下面是业务侧记录的示范
        private int _finishedCount;
        private string _lastFinishedInfo;

        private void Awake()
        {
            // ① 业务服务：框架零业务依赖的落点（Demo 用假实现，真项目换成你的系统）
            _hub = new RevDemoServiceHub { LowQualityDevice = _lowQualityDevice };

            // ② 引擎：自己 new 一个（不是单例）+ 注入服务容器
            _runner = new RevSequenceRunner(RevDemoServiceHub.CreateServices(_hub), new RevSequenceEventBus());

            // ③ 全局钩子：任意序列结束时通知 —— 这是"观察序列"的唯一方式（框架不打日志、不做查询）
            _runner.RunFinished += run =>
            {
                _finishedCount++;
                _lastFinishedInfo = $"{run}（{run.Status}，{run.StepCount} 步）";
                Debug.Log($"[完成] {_lastFinishedInfo}");
            };

            // ④ 触发源 → 序列 的绑定（对应原体系的 enterActions）
            _chestZone = new RevDemoZoneTriggerSource("次元\\宝箱区域", radius: 2f);
            _chestZoneBinding = _runner.Bind(_chestZone, ChestDemoSequences.ChestOpen);

            // ⑤ 事件 → 序列 的绑定（对应原体系的 listenEventActions = 强类型版）
            _chestOpenedBinding = _runner.BindEvents<ChestOpenedEvent>(
                _runner.Events, ChestDemoSequences.SlidingDoorOpen, e => e.Opener);

            Debug.Log("[Demo] 就绪：1 宝箱 / 2 灵果进入 / 3 采集 / 4 灵果离开 / 5,6 礼盒进出 / "
                    + "7 特写 / 8 取特写 / 9 火箭 / 空格 交互 / A 全取消 / P await / "
                    + "O 等回包（推荐写法）/ L WaitTask（对照）/ Q 状态");
        }

        private void Update()
        {
            // ★ Tick 是唯一的驱动入口：快进 / 慢放 / 暂停都由调用方决定（不读 Time.time）
            _runner.Tick(Time.deltaTime);

            // 让假演出（Timeline）自己有时间概念
            _hub.Tick(Time.deltaTime);

            HandleKeys();
        }

        private void HandleKeys()
        {
            if (Input.GetKeyDown(KeyCode.Space)) _hub.ClickPressed = true;      // 模拟交互输入

            if (Input.GetKeyDown(KeyCode.Alpha1)) _chestZone.RaiseEnter(gameObject);   // 玩家进入宝箱区域
            if (Input.GetKeyDown(KeyCode.Alpha2)) _runner.Play(LingGuoDemoSequences.ZoneEnter, gameObject);
            if (Input.GetKeyDown(KeyCode.Alpha3)) _runner.Play(LingGuoDemoSequences.Gather, gameObject);
            if (Input.GetKeyDown(KeyCode.Alpha4)) _runner.Play(LingGuoDemoSequences.ZoneExit, gameObject);
            if (Input.GetKeyDown(KeyCode.Alpha5)) _runner.Play(FarmGiftBoxDemoSequences.ZoneEnter, gameObject);
            if (Input.GetKeyDown(KeyCode.Alpha6)) _runner.Play(FarmGiftBoxDemoSequences.ZoneExit, gameObject);

            if (Input.GetKeyDown(KeyCode.Alpha7))
                _closeupHandle = _runner.Play(NpcCloseupDemoSequences.Closeup, gameObject);

            if (Input.GetKeyDown(KeyCode.Alpha8) && _closeupHandle.IsValid)
                Debug.Log($"[Demo] 取消特写：{(RevSequenceHandleStop(_closeupHandle) ? "已受理（下个 Tick 的步骤边界生效）" : "句柄已失效")}");

            if (Input.GetKeyDown(KeyCode.Alpha9))
            {
                // 句柄的 IsValid：演出正在播时再按一次 → 会被 RejectPerSource 忽略（不会打断正在播的演出）
                bool playing = _rocketHandle.IsValid;
                _rocketHandle = _runner.Play(
                    _lowQualityDevice ? RocketTimelineDemoSequences.ShortShow : RocketTimelineDemoSequences.Show,
                    gameObject);

                Debug.Log(playing
                    ? "[Demo] 火箭演出正在播 → 本次触发被 RejectPerSource 忽略（演出不被打断）"
                    : "[Demo] 火箭演出已启动");
            }

            if (Input.GetKeyDown(KeyCode.A))
            {
                _runner.StopAll();      // 全部取消：每条都会走各自的 finally 收尾
                Debug.Log("[Demo] 已取消所有序列（每条都会执行收尾步骤）");
            }

            if (Input.GetKeyDown(KeyCode.Q)) DumpActiveRuns();
            if (Input.GetKeyDown(KeyCode.P)) PlayChestAndDoorAsync();
            if (Input.GetKeyDown(KeyCode.O)) GatherWithServerConfirm();     // 推荐：服务状态 + 缓存定义
            if (Input.GetKeyDown(KeyCode.L)) GatherWithWaitTask();          // 对照：WaitTask
        }

        private static bool RevSequenceHandleStop(RevSequenceHandle handle) => handle.Stop();

        /// <summary>打印"业务自己记的完成情况"（信息全部来自 RunFinished 回调 —— 框架不提供查询）</summary>
        private void DumpActiveRuns()
            => Debug.Log(_lastFinishedInfo == null
                ? "[Demo] 还没有序列结束（框架不提供查询：想看什么请在回调里自己记）"
                : $"[Demo] 已完成 {_finishedCount} 条，最近一条：{_lastFinishedInfo}");

        /// <summary>体验 PlayAsync：await 一条序列跑完（返回值 = 是否正常跑完，被取消为 false）</summary>
        private async void PlayChestAndDoorAsync()
        {
            try
            {
                bool completed = await _runner.PlayAsync(ChestDemoSequences.ChestOpenAndDoorInline, gameObject);
                Debug.Log(completed
                    ? "[async] 「宝箱 + 开门」已正常跑完"
                    : "[async] 「宝箱 + 开门」被取消，未跑完（句柄仍可查询）");
            }
            catch (Exception e)
            {
                // 步骤抛异常不会到这里：引擎会记日志（带序列名与步骤）并按取消收尾，这里拿到 false。
                // 能到这里的只有 await 之后你自己的代码抛的异常。
                Debug.LogError($"[async] 异常：{e.Message}");
            }
        }

        /// <summary>
        /// 等"服务器回包"再发奖 —— <b>推荐写法</b>：请求与结果都在业务服务里，序列只每帧问一句，
        /// 于是这条定义能像别的定义一样缓存复用（并发策略也才有效）。
        /// </summary>
        private void GatherWithServerConfirm()
            => _runner.Play(LingGuoDemoSequences.GatherWithServerConfirm, gameObject);

        /// <summary>
        /// 等异步任务（<c>WaitTask</c>）：任务由工厂在<b>运行时</b>创建，所以这条清单也是缓存复用的一份，
        /// 而且它声明的"同源拒绝"策略会正常生效（连按只发一次请求）。
        /// <para>和按 <c>O</c> 那条的区别：O 等的是"业务服务的状态"，这条等的是"一个 RevTask 本身"。</para>
        /// </summary>
        private void GatherWithWaitTask()
            => _runner.Play(LingGuoDemoSequences.WaitTaskDemo, gameObject);

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(12f, 12f, 580f, 210f), GUI.skin.box);
            GUILayout.Label("动作序列 Demo（王者真实场景）");
            GUILayout.Label($"已完成 {_finishedCount} 条（按 Q 看最近一条；框架不提供查询：要什么就在回调里自己记）");
            GUILayout.Label("1 宝箱区域　空格 交互　2/3/4 灵果　5/6 礼盒　7 特写　8 取特写　9 火箭");
            GUILayout.Label("A 全取消　Q 活跃序列　P await 序列　O 等回包　L 等异步任务");
            GUILayout.EndArea();
        }

        private void OnDestroy()
        {
            // ★ 注册与反注册成对：绑定、触发源、引擎一个都不留
            _chestZoneBinding?.Dispose();
            _chestOpenedBinding?.Dispose();
            _chestZone?.Dispose();
            _runner.Dispose();
        }
    }
}
