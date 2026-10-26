// ============================================================
// RevScopeFlowDemo.cs —— 游戏流程 × 状态机 × 作用域 联动示例
//
// 位置：Assets\Revolution.Demo\RevScope.Demo\
//
// 【一句话】用 RevLightStackFSM（轻量栈状态机）管理游戏流程，
//   一个流程状态 = 一个域：Push 状态时 Enter 域，Pop 状态时 Exit 域。
//   "打完一局该清干净"从业务代码里彻底消失——它变成了"切状态"这个动作本身。
//
// 【怎么跑】任意场景里建一个空物体，挂上 RevScopeFlowDemo → 点 Play：
//   · 进入战斗   → Push(BattleState)：Enter 战斗域（压在大厅域上面）
//   · 结算回大厅 → Pop()：Exit 战斗域（9 步回收，报告打在日志面板里），大厅原样恢复
//   · 退出登录   → Clear()：逐层 OnExit，每个域各回各家
//
// 【为什么用"栈状态机 + 嵌套域"，而不是"单状态机 + 平级域"】
//   · 栈式（本示例）：大厅常驻底层，战斗压在上面 —— 对应"进战斗不清大厅、回来大厅还在"。
//     状态被盖住时 OnSuspend → 把旧域 SetTickEnabled(false)（暂停不是回收）。
//   · 平级式：用 RevLightFSM<T>（单状态机），每次 ChangeTo 旧状态 OnExit 里 Exit 自己的域。
//     适合 Boot → 登录 → 大厅 → 战斗这种"一次只有一个阶段"的严格线性流程。
//   两种写法的"状态 ↔ 域"对应关系见《作用域系统使用说明》第六节。
//
// 【纯运行时】不依赖任何场景配置与资源，RevLogic / 计时器 / 事件全部走真实链路。
// ============================================================
using System.Collections.Generic;
using UnityEngine;

namespace Revolution.Demo.Scope
{
    // ============================================================
    // ① 流程状态基类：一个流程状态 = 一个域
    //    继承 RevLightStateBase（全回调空实现），每个状态只 override 自己关心的。
    //    ★ 纪律：每个状态只开 / 只关自己的域，永远不碰别人的域。
    // ============================================================
    public abstract class FlowState : RevLightStateBase
    {
        protected readonly RevScopeFlowDemo App;
        protected FlowState(RevScopeFlowDemo app) { App = app; }
    }

    // ============================================================
    // ② 大厅状态：OnEnter 开大厅域，OnSuspend/OnResume 暂停/恢复，OnExit 关域
    // ============================================================
    public sealed class LobbyState : FlowState
    {
        public LobbyState(RevScopeFlowDemo app) : base(app) { }

        public override void OnEnter()
        {
            App.Log("[大厅] OnEnter → RevScope.Enter(Lobby)：打开大厅域");
            RevScope.Enter(RevDomain.Lobby);

            // 大厅的"没有界面的业务"写成 Logic：域开着才能启动，域关了自动 Stop
            App.Log("[大厅] RevLogic.Start<LobbyNewsLogic>()：启动大厅业务单元");
            RevLogic.Start<LobbyNewsLogic>();

            // 下面两笔注册都"没写 owner"：大厅域开着 → 自动归大厅域（谁也没忘，因为没人需要记）
            RevTimer.Every(3f, () => App.Log("[大厅] 轮播计时器触发（没写 owner → 归大厅域）"));
            RevEvent.AddEventListener("Demo.Gift",
                () => App.Log("[大厅] 收到礼物事件（没写 owner → 归大厅域）"));
        }

        public override void OnSuspend()
        {
            // 被战斗盖住：暂停大厅域的每帧逻辑。★ 不是回收——对象、监听、状态全都在，回来继续用。
            App.Log("[大厅] OnSuspend → SetTickEnabled(Lobby,false)：暂停大厅域（计时器照走，OnTick 停）");
            RevScope.SetTickEnabled(RevDomain.Lobby, false);
        }

        public override void OnResume()
        {
            // 战斗弹出，自己重新露出：恢复每帧逻辑，状态没丢
            App.Log("[大厅] OnResume → SetTickEnabled(Lobby,true)：恢复大厅域");
            RevScope.SetTickEnabled(RevDomain.Lobby, true);
        }

        public override void OnExit()
        {
            // 只有"退出登录"（Clear 整条流程）才会走到这：整域回收
            App.Log("[大厅] OnExit → RevScope.Exit(Lobby)：大厅域整域回收");
            RevScope.Exit(RevDomain.Lobby);
        }
    }

    // ============================================================
    // ③ 战斗状态：压在大厅域上面的一层；Pop 时 9 步回收，报告当场可查
    // ============================================================
    public sealed class BattleState : FlowState
    {
        private readonly int _round;
        public BattleState(RevScopeFlowDemo app, int round) : base(app) { _round = round; }

        public override void OnEnter()
        {
            App.Log($"[战斗] OnEnter → RevScope.Enter(Battle, RevResGroup.Battle)：第 {_round} 局，战斗域压在大厅域上面");
            RevScope.Enter(RevDomain.Battle, RevResGroup.Battle);   // 第二个参数 = 关域时整组回收的资源分组

            App.Log("[战斗] RevLogic.Start<BattleBoardLogic>()：启动战斗业务单元");
            RevLogic.Start<BattleBoardLogic>();

            // 没写 owner 的注册：战斗域兜底 —— Exit(Battle) 时被 9 步一起收走
            RevTimer.After(1f, () => App.Log("[战斗] 一次性回调触发（没写 owner → 归战斗域）"));
            RevEvent.AddEventListener("Demo.Kill",
                () => App.Log("[战斗] 击杀事件（没写 owner → 归战斗域）"));
        }

        public override void OnExit()
        {
            // 一局结束：9 个回收步骤按钉死顺序执行（Logic → 界面 → 每帧 → 计时器 → 事件 → 池 → 资源 → 服务 → 扫尾）
            App.Log("[战斗] OnExit → RevScope.Exit(Battle)：9 步回收，报告如下");
            int reclaimed = RevScope.Exit(RevDomain.Battle);

            App.Log(RevScope.LastCloseReport.ToText());
            App.Log($"[战斗] Exit 共回收 {reclaimed} 个；当前域栈深度 {RevScope.Count}（大厅域还在下面）");
        }
    }

    // ============================================================
    // ④ 两个业务单元：注意它们身上没有一行清理代码 —— 解绑是框架的事
    // ============================================================
    [RevLogic(RevDomain.Lobby)]
    public sealed class LobbyNewsLogic : RevLogicBase
    {
        private int _count;
        private float _tickTime;

        protected override void OnStart()
        {
            // Every / Listen 由基类提供，自动带 owner: this —— Stop 时框架自动解绑，OnStop 只写业务收尾
            Every(5f, () => App.Log(
                $"[大厅] 公告轮播 #{++_count}（计时器照走；" +
                $"OnTick 累计 {_tickTime:F1}s —— 进战斗时它是停的：暂停≠回收，计时器≠帧回调）"));
        }

        // 重写了 OnTick → 参与每帧驱动；大厅域被 SetTickEnabled(false) 时这里不再被调
        protected override void OnTick(float deltaTime) { _tickTime += deltaTime; }

        protected override void OnStop()
            => App.Log("[大厅] LobbyNewsLogic.OnStop：业务收尾（框架已先解绑我注册的计时器/事件）");

        private RevScopeFlowDemo App => RevScopeFlowDemo.I;
    }

    [RevLogic(RevDomain.Battle)]
    public sealed class BattleBoardLogic : RevLogicBase
    {
        private int _score;

        protected override void OnStart()
        {
            Listen("Demo.Kill", () => _score++);          // 监听自动归本 Logic
            Every(4f, () => App.Log($"[战斗] 本局得分 {_score}（Every 注册，Stop 时自动停）"));
        }

        protected override void OnStop()
            => App.Log($"[战斗] BattleBoardLogic.OnStop：本局总分 {_score}（框架已先解绑事件/计时器）");

        private RevScopeFlowDemo App => RevScopeFlowDemo.I;
    }

    // ============================================================
    // ⑤ 演示入口：流程状态机 + 三个流程按钮 + 日志面板
    // ============================================================
    public sealed class RevScopeFlowDemo : MonoBehaviour
    {
        /// <summary>演示单例（Logic / 状态里打日志用；真实项目请走事件或注入，别学这个）。</summary>
        public static RevScopeFlowDemo I { get; private set; }

        /// <summary>
        /// 游戏流程状态机（栈式）：大厅常驻底层，进战斗压一层，回大厅弹一层 ——
        /// 与域的嵌套模型一一对应。
        /// </summary>
        internal RevLightStackFSM<FlowState> Flow { get; } = new RevLightStackFSM<FlowState>();

        private readonly List<string> _ui = new List<string>();
        private Vector2 _scroll;
        private int _round;

        private void Awake() { I = this; }

        private void Start()
        {
            // 全局对象的正解示范：显式写 owner —— 任何域关闭都清不掉它（对比文档"坑 5"）
            RevTimer.Every(8f, Heartbeat, -1, RevTimeDomain.Scaled, this);

            // 开机流程：Push 大厅 → OnEnter → Enter(Lobby)。之后进/出战斗只动栈顶。
            Flow.Push(new LobbyState(this));
            Log("流程就绪：大厅 ⇄ 战斗（左侧按钮切换）；全局心跳计时器显式写了 owner，永远不会被任何域回收。");
        }

        // 每帧推进：栈顶流程状态的 OnUpdate（RevLogic 的 OnTick 由框架自动驱动，不用管）
        private void Update() => Flow.Update(Time.deltaTime);

        private void Heartbeat() => Log("[全局] 心跳（owner: this 显式注册 —— 域关了也清不掉我）");

        /// <summary>给各状态 / Logic 写日志用的出口。</summary>
        internal void Log(string line)
        {
            _ui.Add(line);
            if (_ui.Count > 200) _ui.RemoveAt(0);
            _scroll.y = float.MaxValue;
        }

        // ==================== OnGUI：流程按钮 + 状态条 + 日志 ====================

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 460, Screen.height - 20));
            GUILayout.Label("<b>RevScope × 状态机 · 游戏流程演示</b>", TitleStyle());
            GUILayout.Label(
                $"流程：{(Flow.Current != null ? Flow.Current.GetType().Name : "无")}　" +
                $"域栈深度：{RevScope.Count}　当前域：{RevScope.CurrentName ?? "无"}　" +
                $"运行中的 Logic：{RevLogic.Count}", HintStyle());

            GUILayout.Space(4);

            if (GUILayout.Button("进入战斗（Push 战斗域，压在大厅上面）"))
            {
                if (Flow.Current is BattleState) Ui("已经在战斗里了。");
                else Flow.Push(new BattleState(this, ++_round));
            }

            if (GUILayout.Button("结算回大厅（Pop：Exit 战斗域，9 步回收）"))
            {
                if (Flow.Current is LobbyState || Flow.Current == null) Ui("现在不在战斗里。");
                else Flow.Pop();
            }

            if (GUILayout.Button("退出登录（Clear：逐层 OnExit → 每个域 Exit）"))
            {
                if (Flow.Count == 0) Ui("流程已经是空的。");
                else { Flow.Clear(); Log("[流程] Clear 完成：所有域已按自顶向下顺序关闭。"); }
            }

            if (GUILayout.Button("重新进大厅（重开后 Push）"))
            {
                if (Flow.Count > 0) Ui("流程还在跑：先退出登录再重进。");
                else Flow.Push(new LobbyState(this));
            }

            GUILayout.Space(4);
            if (GUILayout.Button("打印 RevScope.Dump()（域栈 + 归属 + 报告）"))
                Log(RevScope.Dump());

            GUILayout.Space(6);
            GUILayout.Label("状态在管\"现在在哪\"，域在管\"这段时间怎么收\"——切状态 = 开关域。", HintStyle());
            GUILayout.FlexibleSpace();
            GUILayout.EndArea();

            // ---------- 右侧：日志 ----------
            GUILayout.BeginArea(new Rect(Screen.width - 490, 10, 480, Screen.height - 20));
            GUILayout.Label("<b>流程日志（含关闭报告）</b>", TitleStyle());
            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (string line in _ui) GUILayout.Label(line);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void Ui(string line) => Log(line);

        // ---------- 简单样式 ----------
        private static GUIStyle _title, _hint;
        private static GUIStyle TitleStyle() => _title ??= new GUIStyle(GUI.skin.label) { richText = true, fontSize = 15 };
        private static GUIStyle HintStyle() => _hint ??= new GUIStyle(GUI.skin.label) { richText = true, fontSize = 11, wordWrap = true };
    }
}
