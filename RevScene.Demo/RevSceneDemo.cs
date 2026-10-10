// ============================================================
// RevSceneDemo.cs —— 场景系统开箱示例
//
// 位置：Assets\Revolution.Demo\RevScene.Demo\
//
// 【它解决什么】
//   "切场景"从 Unity 的 LoadSceneAsync 细节里解脱出来：一行 await、进度永远 0~1（不卡 90%）、
//   事件挂一次全局生效、切之前按约定清理（默认清对象池）。
//
// 【怎么用】打开配套场景 RevSceneDemo.unity → 点 Play → 左侧按钮逐个点。
//
// 【本示例演示什么】
//   ① ReloadAsync：重载当前场景（死亡重开的标准写法）—— 顺带体验完整的加载进度与事件链；
//   ② OnLoadStart / OnProgress / OnLoaded / OnLoadFailed 四个事件的挂法（挂一次全局生效）；
//   ③ State / Progress / IsLoading 三个读数；
//   ④ OnLoadFailed：场景名写错时的"人话报错"（当场拦下，绝不黑屏）。
//
// 【注意】切换场景会卸载当前场景里的所有对象（包括本演示脚本）——
//   重载后演示会重新开始，这是演示"完整流程"最直观的方式；正式项目里把常驻逻辑放 DontDestroyOnLoad。
// ============================================================
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Revolution.Demo.Scene
{
    public sealed class RevSceneDemo : MonoBehaviour
    {
        private readonly List<string> _ui = new List<string>();
        private Vector2 _scroll;

        private void Ui(string line)
        {
            _ui.Add(line);
            if (_ui.Count > 200) _ui.RemoveAt(0);
            _scroll.y = float.MaxValue;
        }

        private void Start()
        {
            string scene = gameObject.scene.name;                // 本演示所在场景名（重载目标）

            Ui("场景系统演示就绪。");
            Ui($"当前场景：{scene}（buildIndex {gameObject.scene.buildIndex}）");

            // ---------- ② 事件挂一次全局生效（真实项目在游戏入口挂一次即可） ----------
            RevScene.OnLoadStart += name => Ui($"[OnLoadStart] 开始切到 {name}（在这里收自己的摊子：关 UI / 停音效 / 卸资源）");
            RevScene.OnProgress += p => { /* 演示里用下面的实时读数显示进度；这里也可更新进度条 */ };
            RevScene.OnLoaded += name => Ui($"[OnLoaded] 已进入 {name}（在这里关加载界面）");
            RevScene.OnLoadFailed += why => Ui($"[OnLoadFailed] {why}");

            // ---------- ③ 切之前按约定清理（默认清对象池，这里再示范关 UI） ----------
            RevScene.OnLoadStart += _ => { /* RevUI.ShutdownAll(); RevSound.StopAll(); */ };

            Ui("四个事件已挂好（OnLoadStart / OnProgress / OnLoaded / OnLoadFailed）。");
            Ui("点「重载本场景」体验完整加载链：进度会从 0 平滑走到 100%（框架处理了 Unity 的 90% 平台期）。");
        }

        // ==================== OnGUI ====================

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 430, Screen.height - 20));
            GUILayout.Label("<b>RevScene 场景系统演示</b>", TitleStyle());

            // ① 实时读数：状态机三件套
            GUILayout.Label($"State：{RevScene.State}　IsLoading：{RevScene.IsLoading}", HintStyle());
            GUILayout.Label($"进度：{RevScene.Progress:P0}　当前场景：{RevScene.CurrentName}", HintStyle());

            // 实时进度条（把 Progress 直接喂给进度条 —— 框架已处理 90% 平台期与回跳）
            Rect bar = GUILayoutUtility.GetRect(380, 18);
            GUI.Box(bar, GUIContent.none);
            GUI.Box(new Rect(bar.x + 2, bar.y + 2, (bar.width - 4) * Mathf.Clamp01(RevScene.Progress), bar.height - 4),
                    GUIContent.none, GUIStyle.none);

            GUILayout.Space(8);

            // ① 重载本场景：完整的异步加载链（进度 → 激活 → OnLoaded）
            if (GUILayout.Button("① 重载本场景（ReloadAsync，体验完整加载链）"))
            {
                if (RevScene.IsLoading) { Ui("正在切换中，本请求被忽略并告警（防重入是刻意的）。"); return; }
                Ui("发起 ReloadAsync……（场景重载后本演示会重新开始）");
                _ = RevScene.ReloadAsync();                       // 故意不 await：语义见 .Forget() 说明
            }

            // ② 同步切换：会卡帧 —— 演示它的存在，文档建议一律 LoadAsync
            if (GUILayout.Button("② 同步 Load（会卡帧，仅演示存在）"))
            {
                Ui("点下去这一帧会卡住（同步加载）—— 正式项目大场景一律用 LoadAsync。");
                RevScene.Load(gameObject.scene.name);
            }

            GUILayout.Space(6);

            // ③ 失败路径：场景名写错 → 当场报人话（不黑屏）
            if (GUILayout.Button("③ 加载一个不存在的场景（演示人话报错）"))
            {
                Ui("发起加载 NotExistScene —— 期待 OnLoadFailed 报『场景不存在，或没有加进 Build Settings』。");
                _ = RevScene.LoadAsync("NotExistScene");
            }

            // ④ 按索引加载：buildIndex 方式（场景名其实更稳，改名不会失效）
            if (GUILayout.Button("④ 按 buildIndex 加载 0 号场景"))
            {
                _ = RevScene.LoadAsync(0);
                Ui("已按 buildIndex 0 发起加载。");
            }

            GUILayout.Space(6);
            if (GUILayout.Button("清空演示日志")) _ui.Clear();

            GUILayout.Label("原则：切换期间再来的请求会被忽略（防互相踩）；完成用 OnLoaded / await 接，别轮询 IsLoading。", HintStyle());
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
