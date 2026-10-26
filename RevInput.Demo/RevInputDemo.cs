// ============================================================
// RevInputDemo.cs —— 输入系统开箱示例
//
// 位置：Assets\Revolution.Demo\RevInput.Demo\
//
// 【它解决什么】
//   业务永远不写 Input.GetKeyDown(...) —— 只问"动作名"：键位怎么绑由绑定表决定，
//   改键、屏蔽、缓冲、连发、手势全都跟着绑定走。
//
// 【怎么用】打开配套场景 RevInputDemo.unity → 点 Play：
//   · 按 空格 / A / D 试试按下与抬起；
//   · 按住 D 看轴读数变化（MoveX = +1）；
//   · 按住 A 键体验连发节拍；
//   · 鼠标在 Game 视图里快速滑动体验手势（Tap / Swipe）。
//   左侧按钮演示屏蔽与存档。
//
// 【本示例演示什么】
//   ① 绑定：键位 / 键位轴 / 连发节拍，全部按"动作名"；
//   ② 轮询：Pressed（一帧）/ Held（按住）/ Axis（-1..1）；
//   ③ 事件驱动：OnPressed / OnRepeat / OnAxis —— 登记一次，输入由框架推给你；
//   ④ 屏蔽：Block(World) 弹窗挡世界输入（轴一并归零）；屏蔽中按下时间仍被记录（缓冲不丢）；
//   ⑤ 手势：Tap 与 Swipe（八向 + 速度）；
//   ⑥ 改键存档：SaveBindings / LoadBindings（含连发配置，无损往返）与 FindConflicts。
// ============================================================
using System.Collections.Generic;
using UnityEngine;

// ★ 命名空间用 InputSystem 而不是 Input：避免遮蔽 UnityEngine.Input，
//   否则 Revolution.Demo 下任何代码写 Input.GetKeyDown(...) 都会解析失败。
namespace Revolution.Demo.InputSystem
{
    public sealed class RevInputDemo : MonoBehaviour
    {
        private readonly List<string> _ui = new List<string>();
        private Vector2 _scroll;

        private int _taps;                       // Tap 手势计数
        private int _jumps;                      // Jump 按下计数（事件驱动）
        private float _lastSwipeSpeed;           // 最近一次滑动的速度
        private readonly DemoListener _listener = new DemoListener();   // 事件驱动的监听者（见文件底部）

        private void Ui(string line)
        {
            _ui.Add(line);
            if (_ui.Count > 200) _ui.RemoveAt(0);
            _scroll.y = float.MaxValue;
        }

        private void Awake()
        {
            // ---------- ① 绑定（启动期一次；或从玩家存档 LoadBindings） ----------
            RevInput.Bind("Jump", RevKey.Space);                      // 跳跃：空格
            RevInput.Bind("Attack", RevKey.A);                        // 攻击：A 键
            RevInput.BindAxis("MoveX", RevKey.A, RevKey.D);           // 移动轴：A = -1，D = +1
            RevInput.SetRepeat("Attack", 0.3f, 0.1f);                 // 连发：按住 0.3 秒后每 0.1 秒一次（delay ≤ 0 = 关连发）

            // 监听者把日志出口接到本示例的界面上
            _listener.Log += Ui;
        }

        private void OnEnable() => RevInput.AddListener(_listener, owner: this);   // ② 登记监听者：输入由框架推过来
        private void OnDisable() => RevInput.OffAllOf(this);                  // ③ 一行退订（事件 + 屏蔽一起清）

        /// <summary>
        /// 事件驱动的监听者：继承 RevInputListenerBase，只重写用到的回调。
        /// （轮询风格与事件风格二选一即可；大项目推荐事件驱动 —— 输入由框架推给你。）
        /// </summary>
        private sealed class DemoListener : Revolution.RevInputListenerBase
        {
            /// <summary>日志出口（由 RevInputDemo 注入）。</summary>
            public System.Action<string> Log;

            private int _taps;
            private int _jumps;
            private float _lastSwipeSpeed;

            // 按下：一帧只成立一次（按住请用 OnHeld 轮询 / Held()）
            public override void OnInputPressed(string action)
            {
                if (action == "Jump") { _jumps++; Log?.Invoke($"[OnPressed] Jump（第 {_jumps} 次）—— 按下只成立一帧，按住请用 Held"); }
            }

            // 连发：框架按 SetRepeat 的节拍推给你
            public override void OnInputRepeat(string action)
            {
                if (action == "Attack") Log?.Invoke("[OnRepeat] Attack 连发节拍触发");
            }

            // 轴变化：只在数值变化时回调（手柄摇杆 / 键位轴都走这里）
            public override void OnInputAxis(string axis, float value)
            {
                if (axis == "MoveX" && Mathf.Abs(value) > 0f) Log?.Invoke($"[OnAxis] MoveX = {value:+0;-0}");
            }

            // 手势：框架识别好推过来（Tap / Swipe / DoubleTap / LongPress）
            public override void OnInputGesture(RevGestureEvent g)
            {
                switch (g.Kind)
                {
                    case RevGestureKind.Tap: _taps++; Log?.Invoke($"[手势] Tap @({g.X:F0},{g.Y:F0})，共 {_taps} 次"); break;
                    case RevGestureKind.Swipe:
                        _lastSwipeSpeed = g.Value;
                        Log?.Invoke($"[手势] Swipe 方向 {g.Direction}，速度 {g.Value:F0} 像素/秒");
                        break;
                }
            }
        }

        // ==================== OnGUI ====================

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 430, Screen.height - 20));
            GUILayout.Label("<b>RevInput 输入系统演示</b>", TitleStyle());

            // ---------- ② 轮询三件套（每帧读状态） ----------
            GUILayout.Label($"Jump（空格）：{(RevInput.Pressed("Jump") ? "本帧刚按下 ▼" : RevInput.Held("Jump") ? "按住中" : RevInput.Released("Jump") ? "本帧刚抬起 ▲" : "—")}", HintStyle());
            GUILayout.Label($"Attack（A）：{(RevInput.Held("Attack") ? "按住中" : "—")}　连发：{(RevInput.Repeat("Attack") ? "本帧触发" : "—")}", HintStyle());
            GUILayout.Label($"MoveX 轴：{RevInput.Axis("MoveX"):+0.00;-0.00;0.00}　鼠标：{RevInput.MousePosition}", HintStyle());
            GUILayout.Label($"最近滑动速度：{_lastSwipeSpeed:F0} 像素/秒（Game 视图里快速拖动鼠标）", HintStyle());

            GUILayout.Space(6);

            // ---------- ④ 屏蔽（弹窗挡世界输入） ----------
            if (GUILayout.Button(RevInput.WorldBlocked ? "解除屏蔽（World）" : "屏蔽世界输入 Block(World)（演示弹窗）"))
            {
                if (RevInput.WorldBlocked) { RevInput.UnblockAllOf(this); Ui("已解除屏蔽。"); }
                else
                {
                    RevInput.Block(RevInputBlockKind.World, owner: this);
                    Ui("已屏蔽：按空格试试 —— Pressed 为 false、ActionBlocked(\"Jump\") 为 true（能区分『没按』与『被挡』）；" +
                       "轴也一并归零（弹窗里角色不会继续移动）。屏蔽中按下的时间仍被记录，解除后缓冲窗口依然有效。");
                }
            }
            GUILayout.Label($"WorldBlocked：{RevInput.WorldBlocked}　Jump 被挡：{RevInput.ActionBlocked("Jump")}", HintStyle());

            GUILayout.Space(6);

            // ---------- ⑤ 缓冲（手感补偿：落地前按的跳跃，落地瞬间仍生效） ----------
            if (GUILayout.Button("缓冲演示：刚才 0.15 秒内按过 Jump 吗？"))
                Ui($"PressedBuffered(\"Jump\") = {RevInput.PressedBuffered("Jump")}（窗口可自定义，默认 0.15 秒）");

            GUILayout.Space(6);

            // ---------- ⑥ 改键存档与冲突 ----------
            if (GUILayout.Button("存档：SaveBindings()（结果打到 Console）"))
            {
                string text = RevInput.SaveBindings();
                Debug.Log("[RevInputDemo] 当前绑定表：\n" + text);
                Ui("绑定表已导出到 Console（含键位 / 轴 / 连发配置 —— 玩家改键就存这段文本）。");
            }
            if (GUILayout.Button("查冲突：FindConflicts()"))
            {
                string conflicts = RevInput.FindConflicts();
                Ui(conflicts == null ? "无冲突（轴键也在检测范围内）。" : "发现冲突：\n" + conflicts);
            }
            if (GUILayout.Button("复位：ResetAll(\"换场景\")"))
            {
                RevInput.ResetAll("演示复位");
                Ui("已清按键与进行中的手势（绑定与屏蔽保留）—— 切后台由框架自动做，这里是手动保险。");
            }

            GUILayout.Space(6);
            if (GUILayout.Button("清空演示日志")) _ui.Clear();

            GUILayout.Label("原则：业务只认动作名；键位差异 / 改键 / 屏蔽 / 缓冲全部由绑定表与框架处理。", HintStyle());
            GUILayout.FlexibleSpace();
            GUILayout.EndArea();

            // ---------- 右侧：演示日志 ----------
            GUILayout.BeginArea(new Rect(Screen.width - 470, 10, 460, Screen.height - 20));
            GUILayout.Label("<b>演示步骤日志</b>", TitleStyle());
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
