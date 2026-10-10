// ============================================================
// RevStateMachineDemo.cs —— 状态机开箱示例（轻量版）
//
// 位置：Assets\Revolution.Demo\RevStateMachine.Demo\
//
// 【怎么用】打开配套场景 RevStateMachineDemo.unity → 点 Play：
//   · 左侧按钮切换状态（巡逻 ↔ 追击 ↔ 攻击）；
//   · 状态机每帧 Update —— 每个状态在 OnUpdate 里做自己的事（日志能看到）。
//
// 【本示例演示什么】
//   ① 轻量状态机 RevLightStateMachine<T>：T = 业务的状态基类（三个状态共享的抽象基类）；
//   ② 状态三件套：OnEnter / OnExit / OnUpdate（继承 RevLightStateBase，只重写关心的）；
//   ③ 切换：ChangeTo<TState>()（旧状态 OnExit → 新状态 OnEnter）；重复切到当前状态被框架拦下；
//   ④ StateTime：在当前状态待了多久（AI 决策的常用输入）；
//   ⑤ StateChanged 事件：谁关心"状态变了"就订阅谁。
//
// 【什么时候用轻量 / 什么时候用重量级（RevHeavyFsm）】
//   纯逻辑轮换（AI 小状态、UI 流程）用轻量版；需要异步准备 / 分层的大型角色控制器用重量级。
// ============================================================
using System.Collections.Generic;
using UnityEngine;

namespace Revolution.Demo.StateMachine
{
    // ---------- 状态基类：T 就是它（三个状态共享的抽象基类，顺便携带公共上下文） ----------
    public abstract class DemoAiState : Revolution.RevLightStateBase
    {
        protected readonly RevStateMachineDemo Demo;
        protected DemoAiState(RevStateMachineDemo demo) { Demo = demo; }

        /// <summary>在当前状态待了多久（框架维护，AI 决策常用）。</summary>
        protected float StateTime => Demo.Machine.StateTime;
    }

    // ---------- 三个状态：每个状态"自己做自己的事" ----------

    /// <summary>巡逻：3 秒后发现玩家 → 自动流转到追击。</summary>
    public sealed class DemoPatrolState : DemoAiState
    {
        public DemoPatrolState(RevStateMachineDemo demo) : base(demo) { }
        public override void OnEnter() => Demo.Log("[巡逻] OnEnter：开始沿路线巡逻");
        public override void OnExit() => Demo.Log("[巡逻] OnExit：停止巡逻");
        public override void OnUpdate(float deltaTime)
        {
            if (StateTime > 3f) Demo.Machine.ChangeTo<DemoChaseState>();
        }
    }

    /// <summary>追击：2 秒后进入攻击距离 → 攻击。</summary>
    public sealed class DemoChaseState : DemoAiState
    {
        public DemoChaseState(RevStateMachineDemo demo) : base(demo) { }
        public override void OnEnter() => Demo.Log("[追击] OnEnter：发现玩家，冲过去！");
        public override void OnExit() => Demo.Log("[追击] OnExit：停止追击");
        public override void OnUpdate(float deltaTime)
        {
            if (StateTime > 2f) Demo.Machine.ChangeTo<DemoAttackState>();
        }
    }

    /// <summary>攻击：每 0.5 秒一击（站着打；用左侧按钮切走）。</summary>
    public sealed class DemoAttackState : DemoAiState
    {
        private float _nextHit;
        public DemoAttackState(RevStateMachineDemo demo) : base(demo) { }
        public override void OnEnter() { Demo.Log("[攻击] OnEnter：进入攻击距离，开打！"); _nextHit = 0f; }
        public override void OnExit() => Demo.Log("[攻击] OnExit：收手");
        public override void OnUpdate(float deltaTime)
        {
            _nextHit -= deltaTime;
            if (_nextHit <= 0f) { _nextHit = 0.5f; Demo.Log("[攻击] 挥了一刀（每 0.5 秒一击）"); }
        }
    }

    // ==================== 演示入口 ====================

    public sealed class RevStateMachineDemo : MonoBehaviour
    {
        private readonly List<string> _ui = new List<string>();
        private Vector2 _scroll;

        /// <summary>状态机实例（供各状态反查 StateTime 与切换 —— 真实项目里由持有者传入）。</summary>
        internal Revolution.RevLightStateMachine<DemoAiState> Machine { get; } =
            new Revolution.RevLightStateMachine<DemoAiState>();

        /// <summary>给各状态写日志用的出口（真实项目走 RevLog）。</summary>
        internal void Log(string line)
        {
            _ui.Add(line);
            if (_ui.Count > 200) _ui.RemoveAt(0);
            _scroll.y = float.MaxValue;
        }

        /// <summary>OnGUI 里的日志别名（界面文案与状态流转走同一个出口）。</summary>
        private void Ui(string line) => Log(line);

        private void Start()
        {
            // ---------- ① 注册状态实例（按具体类型注册；也有工厂重载 Register<TState>(Func<T>)） ----------
            Machine.Register(new DemoPatrolState(this));
            Machine.Register(new DemoChaseState(this));
            Machine.Register(new DemoAttackState(this));

            // ---------- ② StateChanged 事件：谁关心"状态变了"就订阅谁 ----------
            Machine.StateChanged += (from, to) => Log($"[StateChanged] {from.GetType().Name} → {to.GetType().Name}");

            // ---------- ③ 进入初始状态（泛型写法，会触发它的 OnEnter） ----------
            Machine.ChangeTo<DemoPatrolState>();

            Log("状态机演示就绪：巡逻 3 秒 → 追击 2 秒 → 攻击（自动流转）；左侧按钮可手动切换。");
        }

        private void Update()
        {
            // ---------- ④ 每帧推进：当前状态的 OnUpdate 在这里被调用 ----------
            Machine.Update(Time.deltaTime);
        }

        // ==================== OnGUI ====================

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 430, Screen.height - 20));
            GUILayout.Label("<b>RevStateMachine 状态机演示</b>", TitleStyle());

            GUILayout.Label($"当前状态：{Machine.CurrentState.GetType().Name}　已持续：{Machine.StateTime:F1}s", HintStyle());

            GUILayout.Space(6);

            // 手动切换：泛型 ChangeTo<TState>()（重复切到当前状态会被框架拦下）
            if (GUILayout.Button("切换到 巡逻（Patrol）"))
            {
                if (Machine.Is<DemoPatrolState>()) Ui("已经在巡逻里了：重复 ChangeTo 会被框架拦下（不会 Exit 自己再 Enter 自己）。");
                else Machine.ChangeTo<DemoPatrolState>();
            }
            if (GUILayout.Button("切换到 追击（Chase）"))
            {
                if (Machine.Is<DemoChaseState>()) Ui("已经在追击里了。");
                else Machine.ChangeTo<DemoChaseState>();
            }
            if (GUILayout.Button("切换到 攻击（Attack）"))
            {
                if (Machine.Is<DemoAttackState>()) Ui("已经在攻击里了。");
                else Machine.ChangeTo<DemoAttackState>();
            }

            GUILayout.Space(6);
            if (GUILayout.Button("清空演示日志")) _ui.Clear();

            GUILayout.Label("把状态逻辑放进状态类里（而不是一个大 switch）——" +
                "加状态不改旧代码，每个状态只做自己该做的事。", HintStyle());
            GUILayout.FlexibleSpace();
            GUILayout.EndArea();

            // ---------- 右侧：状态流转日志 ----------
            GUILayout.BeginArea(new Rect(Screen.width - 470, 10, 460, Screen.height - 20));
            GUILayout.Label("<b>状态流转日志</b>", TitleStyle());
            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (string line in _ui) GUILayout.Label(line);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ---------- 简单样式 ----------
        private static GUIStyle _title, _hint;
        private static GUIStyle TitleStyle() => _title ??= new GUIStyle(GUI.skin.label) { richText = true, fontSize = 15 };
        private static GUIStyle HintStyle() => _hint ??= new GUIStyle(GUI.skin.label) { richText = true, fontSize = 11, wordWrap = true };
    }
}
