// ============================================================
// RevUISystemDemo.cs —— UI 系统演示入口（按《使用说明》章节分组，一条 API 不落）
//
// 位置：Assets\Revolution.Demo\RevUISystem.Demo\
//
// 【怎么用】打开配套场景 RevUISystemDemo.unity → 点 Play：
//   ① 第一次先点「① 一键生成全部演示预制体」（只需一次；同一份生成物已进版本库）；
//   ② 左侧按章节点：一章 = 一组按钮，覆盖《使用说明》里的每一个场景；
//   ③ 右侧看「状态」与「共享日志」—— 面板里发生的事（生命周期 / 事件 / 数据 / 遮罩）
//      都会写到这块白板上，谁先谁后一眼对上。
//
// 【章节 ↔ 按钮 对应表】
//   一、3 分钟写出第一个面板   → 打开 / 打开并回调 / 关闭 基础面板（三种接法都在它里面）
//   二、面板的生命周期         → 生命周期面板 + RefreshView + 开个 Popup 盖住它看 OnCovered
//   三、"我要做 X" 对照表      → 全部 API：异步打开 / 预加载 / 查询 / 关层 / 关组 / Back / ShutdownAll / DumpStats
//   四、带数据的面板           → 传数据打开（两种）+ SetData 不重开
//   五、层级 / 遮罩 / 返回栈   → 六层各开一遍 + 遮罩对照 + 互斥组 + Back
//   五、Canvas 从哪来          → 单 Canvas ↔ 三 Canvas（Static/Dynamic 面板）
//   五、动画                   → 面板预设 / 自定义转场 / 控件动效 / 全局开关 / Dump
//   三、Part                   → 两种挂法（节点级 + 预制体级）
//   六、6 个坑                 → 清单提示 + ShutdownAll 收尾
// ============================================================
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace Revolution.Demo.UI
{
    /// <summary>演示入口：左侧按章节列演示，右侧显示状态与共享日志。</summary>
    public sealed class RevUISystemDemo : MonoBehaviour
    {
        private Vector2 _scrollLeft;
        private Vector2 _scrollRight;
        private int _logVersion = -1;
        private string _status = "";
        private string _missing = "";

        // ============================================================
        // 起手：检查「资源根目录 + 演示预制体」这两件"没做就什么都打不开"的事
        // ============================================================
        private void Start()
        {
            RevUIDemoLog.Add("入口", "UI 系统演示就绪：左侧按《使用说明》章节分组，右侧是共享日志");

            string root = RevEditorResPolicy.ResRoot;
            RevUIDemoLog.Add("入口", root.Length == 0
                ? "★ 还没配置「资源根目录」：编辑器直读无法工作 —— 菜单 Revolution.Tools/资源/RevAB 打包工具里设置（本项目是 Assets/GameRes）"
                : $"资源根目录：{root}");

            _missing = MissingPrefabs();
            if (_missing.Length > 0)
            {
                RevUIDemoLog.Add("入口", "缺少演示预制体：" + _missing + " —— 点最上面的「① 一键生成全部演示预制体」");
            }
        }

        // ============================================================
        // OnGUI：左（章节按钮） / 右（状态 + 日志）
        // ============================================================
        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 470, Screen.height - 20));
            _scrollLeft = GUILayout.BeginScrollView(_scrollLeft);
            DrawSections();
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            GUILayout.BeginArea(new Rect(Screen.width - 540, 10, 530, Screen.height - 20));
            DrawStatusAndLog();
            GUILayout.EndArea();
        }

        private void DrawSections()
        {
            Title("<b>RevUI · UI 系统演示</b>（左侧按《使用说明》章节）");

            // ---------- 准备 ----------
#if UNITY_EDITOR
            if (GUILayout.Button("① 一键生成全部演示预制体" + (_missing.Length > 0 ? $"（缺 {CountOf(_missing)} 个）" : "（已就绪）"), GUILayout.Height(26)))
            {
                bool ok = Editor.RevUIDemoPrefabBuilder.BuildAll();
                _missing = MissingPrefabs();
                RevUIDemoLog.Add("入口", ok ? "★ 全部演示预制体已生成（资源根目录 Assets/GameRes/RevUIDemo/）" : "生成失败，详见 Console");
            }
#endif
            Hint($"资源根目录：{(RevEditorResPolicy.ResRoot.Length == 0 ? "（未配置）" : RevEditorResPolicy.ResRoot)}" +
                 (_missing.Length > 0 ? $"\n缺预制体：{_missing}" : ""));
            Space();

            // ---------- 一 ----------
            Section("一、3 分钟写出第一个面板");
            Btn("打开基础面板（RevUI.Open，Popup 层自动带遮罩）", () => RevUI.Open<RevUIDemoPanel>());
            Btn("打开并拿到实例做点事（Open + 回调）", () => RevUI.Open<RevUIDemoPanel>(OpenWithCallback));
            Btn("关闭基础面板（RevUI.Close<T>()）", () => RevUIDemoLog.Add("入口", "RevUI.Close<RevUIDemoPanel>() → " + RevUI.Close<RevUIDemoPanel>()));
            Hint("面板里三个按钮分别演示：① 方法特性 ② 按节点名分发 ③ 绑字段 + AddListener");
            Space();

            // ---------- 二 ----------
            Section("二、面板的生命周期（谁先谁后）");
            Btn("打开生命周期面板", () => RevUI.Open<RevUIDemoLifecyclePanel>());
            Btn("手动刷新（RefreshView → OnRefreshView）", () =>
            {
                RevUIDemoLifecyclePanel p = RevUI.Get<RevUIDemoLifecyclePanel>();
                if (p == null) { RevUIDemoLog.Add("入口", "生命周期面板没开着 —— 先点上面那个按钮"); return; }
                p.RefreshView();
            });
            Btn("开一个 Popup 盖住它（看 OnCovered(true)）", () => RevUI.Open<RevUIDemoPopupPanel>());
            Btn("★ 关掉再开一次（看走 OnReuse 而不是 OnInit/OnBindView）", () =>
            {
                RevUI.Close<RevUIDemoLifecyclePanel>();
                RevUI.Open<RevUIDemoLifecyclePanel>();
            });
            Hint("生命周期顺序：OnInit → OnBindView → OnOpen → OnRefreshView；复用打开走 OnReuse；被盖住走 OnCovered(bool)");
            Space();

            // ---------- 三 ----------
            Section("三、“我要做 X” 对照表（全部 API）");
            Btn("异步打开（await RevUI.OpenAsync<T>()）", OpenAsyncDemo);
            Btn("提前加载（RevUI.Preload<T>，避免首次打开卡）", () => RevUI.Preload<RevUIDemoDataPanel>(() => RevUIDemoLog.Add("入口", "Preload 完成：数据面板已预加载")));
            Btn("查询：IsOpen / Get / TopOf(Popup)", QueryDemo);
            Btn("关掉某一层（CloseAll(RevUILayer.Popup)）", () => RevUIDemoLog.Add("入口", "关掉 Popup 层 → " + RevUI.CloseAll(RevUILayer.Popup) + " 个"));
            Btn("关掉互斥组（CloseGroup(\"Dialog\")）", () => RevUIDemoLog.Add("入口", "CloseGroup(\"Dialog\") → " + RevUI.CloseGroup("Dialog") + " 个"));
            Btn("返回上一级（RevUI.Back()）", () => RevUIDemoLog.Add("入口", "RevUI.Back() → " + RevUI.Back()));
            Btn("真正销毁全部（RevUI.ShutdownAll()，切场景 / 回登录用）", () => RevUIDemoLog.Add("入口", "ShutdownAll → " + RevUI.ShutdownAll() + " 个（面板池也一起清空）"));
            Btn("调试：RevUI.DumpStats()", () => { string s = RevUI.DumpStats(); Debug.Log("[RevUIDemo]\n" + s); RevUIDemoLog.Add("入口", "DumpStats 已打 Console（右侧状态区也有一份摘要）"); });
            Space();

            // ---------- 四 ----------
            Section("四、带数据的面板（一个面板多处复用）");
            Btn("打开（传数据：英雄详情）", () => RevUI.Open<RevUIDemoDataPanel, RevUIDemoDetail>(HeroData()));
            Btn("再传一份数据（SetData，**不重开**面板）", () =>
            {
                RevUIDemoDataPanel p = RevUI.Get<RevUIDemoDataPanel>();
                if (p == null) { RevUIDemoLog.Add("入口", "数据面板没开着 —— 先点上面那个按钮"); return; }
                p.SetData(ItemData());
            });
            Btn("打开（传数据：道具详情）", () => RevUI.Open<RevUIDemoDataPanel, RevUIDemoDetail>(ItemData()));
            Hint("看日志：OnOpen 只出现一次，换数据走的是 OnDataChanged → OnRefreshView");
            Space();

            // ---------- 五：层级 ----------
            Section("五、层级 / 遮罩 / 返回栈");
            Btn("Scene 层（最底层，主界面）", () => RevUI.Open<RevUIDemoScenePanel>());
            Btn("Normal 层（二级界面，默认层）", () => RevUI.Open<RevUIDemoNormalPanel>());
            Btn("Popup 层（自动带遮罩，点遮罩会关它）", () => RevUI.Open<RevUIDemoPopupPanel>());
            Btn("Toast 层（不挡操作、不进返回栈）", () => RevUI.Open<RevUIDemoToastPanel>());
            Btn("Guide 层（引导，带遮罩）", () => RevUI.Open<RevUIDemoGuidePanel>());
            Btn("Top 层（最顶层）", () => RevUI.Open<RevUIDemoTopPanel>());
            Btn("对照：Popup 但 Mask = None / 不进返回栈", () => RevUI.Open<RevUIDemoNoMaskPopupPanel>());
            Btn("互斥组：开 Dialog A", () => RevUI.Open<RevUIDemoDialogAPanel>());
            Btn("互斥组：开 Dialog B（A 会被自动关掉）", () => RevUI.Open<RevUIDemoDialogBPanel>());
            Hint($"遮罩：Auto 规则 = Popup/Guide/Top 自动加；点击遮罩关闭 = {RevUISetting.ClickMaskClosesTop}；Back() 跳过 Toast 与 InBackStack = false 的面板");
            Space();

            // ---------- 五：Canvas ----------
            Section("五、UI 根 Canvas 从哪来（单 Canvas ↔ 三 Canvas）");
            Btn("切到三 Canvas（Split）", () =>
            {
                if (RevUIManager.Instance.OpenedCount > 0)
                {
                    RevUIDemoLog.Add("入口", "先 ShutdownAll 再切（三 Canvas 必须在“第一次打开面板之前”设置）");
                    return;
                }
                RevUISetting.CanvasArchitecture = RevUICanvasArchitecture.Split;
                RevUIDemoLog.Add("入口", "已切到三 Canvas：Static / Dynamic 两个 Scene 面板会各占一张画布（DumpStats 里能看到 /CanvasType）");
            });
            Btn("回到单 Canvas（Single，默认）", () =>
            {
                if (RevUIManager.Instance.OpenedCount > 0)
                {
                    RevUIDemoLog.Add("入口", "先 ShutdownAll 再切");
                    return;
                }
                RevUISetting.CanvasArchitecture = RevUICanvasArchitecture.Single;
                RevUIDemoLog.Add("入口", "已回到单 Canvas（默认架构：先用它，量到 Canvas 合批才是瓶颈再考虑三 Canvas）");
            });
            Btn("打开 Static 场景面板（常驻不变的内容）", () => RevUI.Open<RevUIDemoStaticScenePanel>());
            Btn("打开 Dynamic 场景面板（常驻但频繁变化）", () => RevUI.Open<RevUIDemoDynamicScenePanel>());
            Hint("默认渲染模式 = ScreenSpaceOverlay（不需要相机）；想换相机模式 / 自己的 Canvas 预制体见使用说明第五章");
            Space();

            // ---------- 五：动画 ----------
            Section("五、动画（面板预设 / 自定义转场 / 控件动效）");
            Btn("打开动画面板（ShowAnimation = PopIn / Hide = PopOut）", () => RevUI.Open<RevUIDemoAnimPanel>());
            Btn("打开自定义转场面板（重写 PlayOpenTransition）", () => RevUI.Open<RevUIDemoSlidePanel>());
            Btn($"全局动效开关（当前 {(RevUISetting.UIAnimationsEnabled ? "开" : "关")} → 点一下切）", () =>
            {
                RevUISetting.UIAnimationsEnabled = !RevUISetting.UIAnimationsEnabled;
                RevUIDemoLog.Add("入口", RevUISetting.UIAnimationsEnabled
                    ? "已开动效（预设恢复正常播放）"
                    : "已关动效：所有预设直接写终态，业务代码一行不用改");
            });
            Btn("调试：RevUIAnim.Dump()", () => { Debug.Log("[RevUIDemo] " + RevUIAnim.Dump()); RevUIDemoLog.Add("入口", "RevUIAnim.Dump() 已打 Console（在播 / 池 / 起播 / 播完 / 停）"); });
            Space();

            // ---------- Part ----------
            Section("三、Part（可复用的子界面块）");
            Btn("打开 Part 面板（节点级 + 预制体级两种挂法）", () => RevUI.Open<RevUIDemoPartPanel>());
            Space();

            // ---------- 六 ----------
            Section("六、新手最容易踩的 6 个坑");
            Hint("① 别自己 Instantiate 面板 → 一律 RevUI.Open\n" +
                 "② OnBindView 是抽象方法，编译期就拦你；里面只做绑定\n" +
                 "③ 别在 OnBindView 里开计时器/协程 → 那是“只一次”的，每次打开要做的写 OnOpen\n" +
                 "④ OnClose 里停计时器 / 退订事件（面板会被复用，见基础面板的 AddListener 示范）\n" +
                 "⑤ CloseAll 不是“关一个”→ 关单个用 Close<T>()；切场景才 ShutdownAll()\n" +
                 "⑥ 打不开先查三处：预制体在资源根目录对应路径下 · 特性里目录/名字写对 · 打包工具里已生成映射");
            Space();

            // ---------- 七 ----------
            Section("七、相关文档");
            Hint("《UI 系统 · 使用说明》（本场景逐个演示的对象）\n《UI 系统 · 架构解析》（面板池 / 绑定的设计论证）\n在线文档站：https://yokino337088.github.io/Revolution/");
            Space();
        }

        private void DrawStatusAndLog()
        {
            Title("<b>状态</b>");
            string top = "";
            var p = RevUI.TopOf(RevUILayer.Popup);
            if (p != null) top = "　顶层 Popup：" + p;

            Hint($"打开中 {RevUIManager.Instance.OpenedCount} · 加载中 {RevUIManager.Instance.LoadingCount} · 池中 {RevUIManager.Instance.CachedCount}" +
                 $"\n架构：{RevUISetting.CanvasArchitecture}（{RevUISetting.CanvasMode}）· 动效：{(RevUISetting.UIAnimationsEnabled ? "开" : "关")} · 长按阈值 {RevUISetting.ButtonLongPressSeconds}s" +
                 $"\n正在播的动画：{RevUIAnim.ActiveCount} 个{top}");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("刷新状态 / DumpStats")) _status = RevUI.DumpStats();
            if (GUILayout.Button("清空日志")) RevUIDemoLog.Clear();
            if (GUILayout.Button("打印 DumpStats 到 Console")) Debug.Log("[RevUIDemo]\n" + RevUI.DumpStats());
            GUILayout.EndHorizontal();

            if (_status.Length > 0) Hint(_status);

            Title("<b>共享日志（面板内部发生的事都会写到这里）</b>");
            _scrollRight = GUILayout.BeginScrollView(_scrollRight);
            List<string> lines = RevUIDemoLog.Lines;
            for (int i = 0; i < lines.Count; i++) GUILayout.Label(lines[i]);
            GUILayout.EndScrollView();

            // 日志变了就滚到底（不然自己往上翻会被每帧拽回来）
            if (_logVersion != RevUIDemoLog.Version)
            {
                _logVersion = RevUIDemoLog.Version;
                _scrollRight.y = float.MaxValue;
            }
        }

        // ============================================================
        // 各个演示动作
        // ============================================================

        /// <summary>一、打开并拿到实例（回调式）。</summary>
        private void OpenWithCallback(RevUIDemoPanel panel)
        {
            RevUIDemoLog.Add("入口", panel != null
                ? $"Open 回调拿到实例：{panel}（State = {panel.State}）"
                : "Open 失败：预制体没生成 / 资源根目录没配（看 Console 的人话报错）");
        }

        /// <summary>三、异步打开：await 之后面板已经开好了（这里用 async void 演示）。</summary>
        private async void OpenAsyncDemo()
        {
            RevUIDemoLog.Add("入口", "await RevUI.OpenAsync<RevUIDemoDataPanel>() ……");
            RevUIDemoDataPanel panel = await RevUI.OpenAsync<RevUIDemoDataPanel>();
            RevUIDemoLog.Add("入口", panel != null
                ? $"await 完成：{panel} 已打开（异步加载适合“首次打开要读盘”的场景）"
                : "await 返回 null：打开失败");
        }

        /// <summary>三、查询类 API。</summary>
        private void QueryDemo()
        {
            RevUIPanel top = RevUI.TopOf(RevUILayer.Popup);
            RevUIDemoLifecyclePanel life = RevUI.Get<RevUIDemoLifecyclePanel>();

            RevUIDemoLog.Add("入口",
                $"IsOpen<生命周期面板> = {RevUI.IsOpen<RevUIDemoLifecyclePanel>()} · " +
                $"Get<生命周期面板> = {(life == null ? "null" : life.ToString())} · " +
                $"TopOf(Popup) = {(top == null ? "（该层空）" : top.ToString())}");
        }

        // ============================================================
        // 演示数据
        // ============================================================
        private static RevUIDemoDetail HeroData() => new RevUIDemoDetail
        {
            Id = 1001,
            Title = "英雄详情 · 亚瑟",
            Body = "定位：战士 / 坦克\n被动：圣光守护（脱战后持续回血）\n" +
                   "★ 同一个面板类，换一份数据就能当“另一个界面”用 —— 不必写两个面板。",
            Accent = new Color(0.34f, 0.62f, 0.98f, 0.95f),
        };

        private static RevUIDemoDetail ItemData() => new RevUIDemoDetail
        {
            Id = 2001,
            Title = "道具详情 · 圣杯",
            Body = "类型：法术装备\n效果：+180 法术攻击 / +10% 冷却缩减\n" +
                   "★ 换数据走 OnDataChanged → OnRefreshView，OnOpen 不会再来一遍。",
            Accent = new Color(0.36f, 0.78f, 0.46f, 0.95f),
        };

        // ============================================================
        // 预制体检查 + 界面小工具
        // ============================================================

        /// <summary>演示要用到的全部预制体（生成器会一次性产出）。</summary>
        private static readonly string[] DemoPrefabs =
        {
            "RevUIDemoPanel", "RevUIDemoLifecyclePanel", "RevUIDemoWidgetsPanel", "RevUIDemoDataPanel",
            "RevUIDemoScenePanel", "RevUIDemoNormalPanel", "RevUIDemoPopupPanel", "RevUIDemoToastPanel",
            "RevUIDemoGuidePanel", "RevUIDemoTopPanel", "RevUIDemoNoMaskPopupPanel",
            "RevUIDemoDialogAPanel", "RevUIDemoDialogBPanel", "RevUIDemoStaticScenePanel", "RevUIDemoDynamicScenePanel",
            "RevUIDemoAnimPanel", "RevUIDemoSlidePanel", "RevUIDemoPartPanel", "RevUIDemoPart",
        };

        /// <summary>哪些演示预制体还没生成（只列名字，够点提示了）。</summary>
        private static string MissingPrefabs()
        {
            string dir = Path.Combine(Application.dataPath, "GameRes/RevUIDemo");
            var missing = new List<string>(4);

            for (int i = 0; i < DemoPrefabs.Length; i++)
            {
                if (!File.Exists(Path.Combine(dir, DemoPrefabs[i] + ".prefab")))
                {
                    missing.Add(DemoPrefabs[i]);
                }
            }

            return string.Join("、", missing.ToArray());
        }

        private static int CountOf(string list) => list.Length == 0 ? 0 : list.Split('、').Length;

        private static void Section(string text) => GUILayout.Label("<b>— " + text + " —</b>", SectionStyle());
        private static void Title(string text) => GUILayout.Label(text, TitleStyle());
        private static void Hint(string text) => GUILayout.Label(text, HintStyle());
        private static void Space() => GUILayout.Space(6);

        private static void Btn(string label, System.Action act)
        {
            if (GUILayout.Button(label, GUILayout.Height(24))) act();
        }

        private static GUIStyle _title, _section, _hint;

        private static GUIStyle TitleStyle()
            => _title ??= new GUIStyle(GUI.skin.label) { richText = true, fontSize = 15 };

        private static GUIStyle SectionStyle()
            => _section ??= new GUIStyle(GUI.skin.label) { richText = true, fontSize = 13 };

        private static GUIStyle HintStyle()
            => _hint ??= new GUIStyle(GUI.skin.label) { richText = true, fontSize = 11, wordWrap = true };
    }
}
