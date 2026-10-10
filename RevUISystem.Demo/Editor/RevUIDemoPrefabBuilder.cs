// ============================================================
// RevUIDemoPrefabBuilder.cs —— 一键生成"UI 系统演示"用到的全部预制体（编辑器专用）
//
// 位置：Assets\Revolution.Demo\RevUISystem.Demo\Editor\
//
// 【它做什么】把《UI 系统 · 使用说明》每个章节需要的面板预制体，全部用代码搭出来并存盘：
//
//   章节                        预制体                                    里面有什么
//   ────────────────────────────────────────────────────────────────────────────────────────
//   一 第一个面板               RevUIDemoPanel                            Bg / txtTitle / txtInfo / btnAdd / btnClose
//   二 生命周期                 RevUIDemoLifecyclePanel                   + txtLog / btnRefresh
//   一 九种控件事件             RevUIDemoWidgetsPanel                     Button×4 / Toggle / Slider / InputField / Dropdown / ScrollRect
//   四 带数据的面板             RevUIDemoDataPanel                        imgBadge / txtBody
//   五 六层（Scene…Top）        RevUIDemoScenePanel … RevUIDemoTopPanel   同款：Bg / txtTitle / txtInfo / btnClose
//   五 遮罩·返回栈对照          RevUIDemoNoMaskPopupPanel                 同上
//   五 互斥组                   RevUIDemoDialog[AB]Panel                  同上
//   五 三 Canvas                RevUIDemoStatic/DynamicScenePanel         同上
//   五 动画                     RevUIDemoAnimPanel / RevUIDemoSlidePanel  imgIcon / txtTip / btnHover + 9 个动画按钮
//   三 Part                     RevUIDemoPart / RevUIDemoPartPanel        Part 本体 + 宿主（含一个节点级 Part）
//
//   ★ 全部落在【资源根目录】下：Assets/GameRes/RevUIDemo/<预制体名>.prefab
//     因为编辑器直读 / AB 都按「资源根目录 + 逻辑路径」找资源 —— 特性里写的 "RevUIDemo"
//     就是这个逻辑目录（不是完整的 Assets/... 路径）。
//   ★ 每个预制体都会设 AB 名 revuidemo（= 逻辑路径第一段的小写；与 AB 打包工具的规则一致）。
//
// 【怎么用】菜单 Revolution.Tools/Demo/生成 UI 演示预制体（全部）；或演示场景里的按钮 ①。
//   生成一次即可（生成物已进版本库）。改了面板结构可以重复生成覆盖。
//
// 【为什么整份文件裹 #if UNITY_EDITOR】
//   本文件属于 Revolution.Demo 程序集（这个 Editor 目录下没有独立 asmdef），
//   里面用了 UnityEditor.* —— 不裹起来，**出包时会因为找不到 UnityEditor 而编译失败**。
// ============================================================
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Revolution.Demo.UI.Editor
{
    /// <summary>演示预制体生成器（面板结构全在代码里，改这里就能改演示界面）。</summary>
    internal static class RevUIDemoPrefabBuilder
    {
        /// <summary>预制体落盘目录：资源根目录（Assets/GameRes）下的 RevUIDemo/ —— 逻辑路径 RevUIDemo/&lt;名字&gt;。</summary>
        private const string PrefabDir = "Assets/GameRes/RevUIDemo";

        /// <summary>搬进资源根目录之前的老位置（若还在就删掉 —— 它永远加载不到，留着只会误导排查）。</summary>
        private const string LegacyPrefabPath = "Assets/Revolution.Demo/RevUISystem.Demo/RevUIDemoPanel.prefab";

        /// <summary>AB 名 = 逻辑路径第一段的小写（Assets/GameRes/RevUIDemo/… → revuidemo）。</summary>
        private const string BundleName = "revuidemo";

        // ---------- 配色 / 尺寸（改这里就能改演示面板长相） ----------
        private static readonly Color PanelBg = new Color(0.13f, 0.15f, 0.19f, 0.97f);
        private static readonly Color FieldBg = new Color(0.20f, 0.23f, 0.29f, 1f);
        private static readonly Color TextMain = new Color(0.90f, 0.94f, 1f);
        private static readonly Color TextSub = new Color(0.72f, 0.78f, 0.88f);
        private static readonly Color BtnBlue = new Color(0.25f, 0.55f, 0.95f);
        private static readonly Color BtnGray = new Color(0.32f, 0.36f, 0.44f);
        private static readonly Color BtnGreen = new Color(0.20f, 0.62f, 0.40f);
        private static readonly Color BtnRed = new Color(0.75f, 0.30f, 0.28f);

        // ============================================================
        // 入口
        // ============================================================
        [MenuItem("Revolution.Tools/Demo/生成 UI 演示预制体（全部）", false, 31)]
        private static void MenuBuildAll() => BuildAll();

        /// <summary>生成全部演示预制体。返回是否全部成功（任何一步失败都会在 Console 报原因）。</summary>
        internal static bool BuildAll()
        {
            Directory.CreateDirectory(PrefabDir);

            int ok = 0, total = 0;
            foreach (KeyValuePair<string, System.Func<bool>> item in Builders())
            {
                total++;
                if (item.Value()) ok++;
                else Debug.LogError($"[RevUIDemoPrefabBuilder] 生成失败：{item.Key}");
            }

            // 资源根目录之外的旧预制体：删掉（它永远加载不到）
            if (File.Exists(LegacyPrefabPath))
            {
                AssetDatabase.DeleteAsset(LegacyPrefabPath);
                Debug.Log($"[RevUIDemoPrefabBuilder] 已清理旧位置的预制体：{LegacyPrefabPath}");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[RevUIDemoPrefabBuilder] 演示预制体生成完成：{ok}/{total} 个 → {PrefabDir}/（AB 名 {BundleName}）");
            return ok == total;
        }

        /// <summary>预制体名 → 生成函数（字典顺序无关紧要，生成日志里会按名字逐个报）。</summary>
        private static Dictionary<string, System.Func<bool>> Builders()
        {
            var map = new Dictionary<string, System.Func<bool>>();

            // 一、第一个面板 + 二、生命周期 + 一、九种控件事件 + 四、带数据
            map["RevUIDemoPanel"] = BuildBasicPanel;
            map["RevUIDemoLifecyclePanel"] = BuildLifecyclePanel;
            map["RevUIDemoWidgetsPanel"] = BuildWidgetsPanel;
            map["RevUIDemoDataPanel"] = BuildDataPanel;

            // 五、六层 + 遮罩/返回栈对照 + 互斥组 + 三 Canvas（都是同款轻面板）
            map["RevUIDemoScenePanel"] = () => BuildLayerPanel("RevUIDemoScenePanel", "Scene 层（主界面）");
            map["RevUIDemoNormalPanel"] = () => BuildLayerPanel("RevUIDemoNormalPanel", "Normal 层（二级界面）");
            map["RevUIDemoPopupPanel"] = () => BuildLayerPanel("RevUIDemoPopupPanel", "Popup 层（带遮罩）");
            map["RevUIDemoToastPanel"] = () => BuildLayerPanel("RevUIDemoToastPanel", "Toast 层（不挡操作）");
            map["RevUIDemoGuidePanel"] = () => BuildLayerPanel("RevUIDemoGuidePanel", "Guide 层（引导）");
            map["RevUIDemoTopPanel"] = () => BuildLayerPanel("RevUIDemoTopPanel", "Top 层（最顶层）");
            map["RevUIDemoNoMaskPopupPanel"] = () => BuildLayerPanel("RevUIDemoNoMaskPopupPanel", "Popup · Mask = None");
            map["RevUIDemoDialogAPanel"] = () => BuildLayerPanel("RevUIDemoDialogAPanel", "互斥组 Dialog · A");
            map["RevUIDemoDialogBPanel"] = () => BuildLayerPanel("RevUIDemoDialogBPanel", "互斥组 Dialog · B");
            map["RevUIDemoStaticScenePanel"] = () => BuildLayerPanel("RevUIDemoStaticScenePanel", "Scene · Static");
            map["RevUIDemoDynamicScenePanel"] = () => BuildLayerPanel("RevUIDemoDynamicScenePanel", "Scene · Dynamic");

            // 五、动画
            map["RevUIDemoAnimPanel"] = BuildAnimPanel;
            map["RevUIDemoSlidePanel"] = BuildSlidePanel;

            // 三、Part（本体 + 宿主）
            map["RevUIDemoPart"] = BuildPart;
            map["RevUIDemoPartPanel"] = BuildPartPanel;

            return map;
        }

        // ============================================================
        // 一、基础面板（Bg / txtTitle / txtInfo / btnAdd / btnClose）
        // ============================================================
        private static bool BuildBasicPanel()
        {
            GameObject root = NewPanel("RevUIDemoPanel", 520, 320);
            AddBg(root);
            AddTitle(root, "① 基础面板（RevUIDemoPanel）");
            AddText(root, "txtInfo", "演示面板已打开。", 14, TextSub,
                new Vector2(0, 0.20f), new Vector2(1, 0.86f), new Vector2(20, 0), new Vector2(-20, -6));

            AddButton(root, "btnAdd", "加一条日志（接法① + 接法③）", BtnBlue,
                new Vector2(0, 0), new Vector2(0.55f, 0), new Vector2(20, 16), new Vector2(-8, 66));
            AddClose(root);

            return Save(root, "RevUIDemoPanel");
        }

        // ============================================================
        // 二、生命周期面板（+ txtLog / btnRefresh）
        // ============================================================
        private static bool BuildLifecyclePanel()
        {
            GameObject root = NewPanel("RevUIDemoLifecyclePanel", 660, 460);
            AddBg(root);
            AddTitle(root, "② 生命周期");

            AddText(root, "txtLog", "", 14, TextMain,
                new Vector2(0, 0.16f), new Vector2(1, 0.86f), new Vector2(20, 0), new Vector2(-20, -6));

            AddButton(root, "btnRefresh", "手动刷新 RefreshView()", BtnBlue,
                new Vector2(0, 0), new Vector2(0.5f, 0), new Vector2(20, 16), new Vector2(-8, 66));
            AddClose(root);

            return Save(root, "RevUIDemoLifecyclePanel");
        }

        // ============================================================
        // 一、九种控件事件面板
        //   节点名与 RevUIDemoWidgetsPanel 里的特性严格对应（名字对不上会在装配时报错）
        // ============================================================
        private static bool BuildWidgetsPanel()
        {
            GameObject root = NewPanel("RevUIDemoWidgetsPanel", 740, 620);
            AddBg(root);
            AddTitle(root, "① 九种控件事件");

            // 左半边上半：结果文本；左半边下半：滚动列表（两个控件事件里"高频"的那个）
            AddText(root, "txtResult", "", 13, TextMain,
                new Vector2(0, 0.44f), new Vector2(0.47f, 0.86f), new Vector2(20, 0), new Vector2(-8, -6));

            AddScroll(root, "scrollList", 12,
                new Vector2(0, 0.08f), new Vector2(0.47f, 0.40f), new Vector2(20, 0), new Vector2(-8, 0));

            // 右半边：九个控件（整列；第 0 行从标题下方 -70 开始，每行 26 高 + 8 间隔）
            const float x0 = 0.50f, x1 = 1f, left = 8f, right = -20f, top = -70f;
            AddButton(root, "btnStart", "btnStart · 点击", BtnBlue, x0, x1, left, right, top, 0);
            AddButton(root, "btnSkill", "btnSkill · 长按（≥0.5s 松手）", BtnGray, x0, x1, left, right, top, 1);
            AddButton(root, "btnMove", "btnMove · 松开", BtnGray, x0, x1, left, right, top, 2);

            AddToggle(root, "tglSound", "tglSound · 开关", x0, x1, left, right, top, 3);
            AddSlider(root, "sldVolume", x0, x1, left, right, top, 4);
            AddInput(root, "inpName", "inpName · 输入框（输入中走重写回调）", x0, x1, left, right, top, 5);

            AddDropdown(root, "ddlQuality", new[] { "流畅", "均衡", "高清", "极致" }, x0, x1, left, right, top, 6);
            AddButton(root, "btnDropdownNext", "↑ 用代码改 value（同样触发事件）", BtnGray, x0, x1, left, right, top, 7);

            AddClose(root);

            return Save(root, "RevUIDemoWidgetsPanel");
        }

        // ============================================================
        // 四、带数据面板（imgBadge / txtBody）
        // ============================================================
        private static bool BuildDataPanel()
        {
            GameObject root = NewPanel("RevUIDemoDataPanel", 620, 420);
            AddBg(root);
            AddTitle(root, "④ 带数据的面板");

            // 一个小色块：换数据时颜色会跟着换（一眼看出"数据真的换了"）
            GameObject badge = NewNode("imgBadge", root);
            var badgeImg = badge.AddComponent<Image>();
            badgeImg.color = BtnBlue;
            Anchor(badge, new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -110), new Vector2(76, -54));

            AddText(root, "txtBody", "", 14, TextMain,
                new Vector2(0, 0.08f), new Vector2(1, 0.72f), new Vector2(20, 0), new Vector2(-20, -6));

            AddClose(root);

            return Save(root, "RevUIDemoDataPanel");
        }

        // ============================================================
        // 五、轻面板（六层 / 对照 / 互斥组 / 三 Canvas 共用同一套结构）
        //   节点：Bg / txtTitle / txtInfo / btnClose
        // ============================================================
        private static bool BuildLayerPanel(string prefabName, string title)
        {
            GameObject root = NewPanel(prefabName, 620, 420);
            AddBg(root);
            AddTitle(root, "⑤ " + title);

            AddText(root, "txtInfo", "", 14, TextMain,
                new Vector2(0, 0.10f), new Vector2(1, 0.84f), new Vector2(20, 0), new Vector2(-20, -6));

            AddClose(root);

            return Save(root, prefabName);
        }

        // ============================================================
        // 五、动画面板（imgIcon / txtTip / btnHover + 一排动画按钮）
        // ============================================================
        private static bool BuildAnimPanel()
        {
            GameObject root = NewPanel("RevUIDemoAnimPanel", 700, 580);
            AddBg(root);
            AddTitle(root, "⑤ 动画");

            // 被动画演示的图标 + 提示文字（左半边）
            GameObject icon = NewNode("imgIcon", root);
            var iconImg = icon.AddComponent<Image>();
            iconImg.color = new Color(0.35f, 0.62f, 0.98f, 0.95f);
            Anchor(icon, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -140), new Vector2(104, -60));

            AddText(root, "txtTip", "我是被动画演示的文字（Breathe 会让我呼吸）", 13, TextSub,
                new Vector2(0, 0.30f), new Vector2(0.46f, 0.62f), new Vector2(24, 0), new Vector2(-8, 0));

            AddButton(root, "btnHover", "btnHover · 悬停/按下反馈", BtnGreen,
                new Vector2(0, 0), new Vector2(0.46f, 0), new Vector2(24, 16), new Vector2(-8, 66));

            AddText(root, "txtInfo", "", 13, TextMain,
                new Vector2(0, 0.10f), new Vector2(0.46f, 0.30f), new Vector2(24, 0), new Vector2(-8, -2));

            // 右半边：动画 API 按钮（整列；第 0 行从标题下方 -70 开始）
            const float x0 = 0.50f, x1 = 1f, left = 8f, right = -20f, top = -70f;
            AddButton(root, "btnFadeIn", "FadeIn · 淡入", BtnBlue, x0, x1, left, right, top, 0);
            AddButton(root, "btnSlideIn", "SlideIn(Left) · 从左滑入", BtnBlue, x0, x1, left, right, top, 1);
            AddButton(root, "btnScaleTo", "ScaleTo(1.25) · 放大", BtnBlue, x0, x1, left, right, top, 2);
            AddButton(root, "btnFadeTo", "FadeTo(0.35) · 淡到半透明", BtnBlue, x0, x1, left, right, top, 3);
            AddButton(root, "btnBreathe", "Breathe · 呼吸（无限往返）", BtnBlue, x0, x1, left, right, top, 4);
            AddButton(root, "btnPlay", "Play(PopIn) · 预设 + 播完回调", BtnGray, x0, x1, left, right, top, 5);
            AddButton(root, "btnApplyEnd", "ApplyEnd + RestoreBase · 跳过动画", BtnGray, x0, x1, left, right, top, 6);
            AddButton(root, "btnStopAll", "StopAllOf(this) · 停掉本面板动画", BtnRed, x0, x1, left, right, top, 7);
            AddButton(root, "btnDump", "RevUIAnim.Dump() · 诊断", BtnGray, x0, x1, left, right, top, 8);

            AddClose(root);

            return Save(root, "RevUIDemoAnimPanel");
        }

        // ============================================================
        // 五、自定义转场面板
        // ============================================================
        private static bool BuildSlidePanel()
        {
            GameObject root = NewPanel("RevUIDemoSlidePanel", 600, 360);
            AddBg(root);
            AddTitle(root, "⑤ 自定义转场（PlayOpenTransition）");

            AddText(root, "txtInfo", "", 14, TextMain,
                new Vector2(0, 0.10f), new Vector2(1, 0.84f), new Vector2(20, 0), new Vector2(-20, -6));

            AddClose(root);

            return Save(root, "RevUIDemoSlidePanel");
        }

        // ============================================================
        // 三、Part 本体（Bg / txtPart / btnPartAdd）
        // ============================================================
        private static bool BuildPart()
        {
            GameObject root = NewPanel("RevUIDemoPart", 340, 150);
            AddBg(root, new Color(0.18f, 0.26f, 0.38f, 0.97f));

            AddText(root, "txtPart", "我是 RevUIDemoPart", 14, TextMain,
                new Vector2(0, 0.42f), new Vector2(1, 1), new Vector2(14, 0), new Vector2(-14, -8));

            AddButton(root, "btnPartAdd", "点我 → NotifyHost()", BtnGreen,
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(14, 12), new Vector2(-14, 54));

            return Save(root, "RevUIDemoPart");
        }

        // ============================================================
        // 三、Part 宿主面板：含一个"节点级 Part"（预制体里就摆好）
        // ============================================================
        private static bool BuildPartPanel()
        {
            GameObject root = NewPanel("RevUIDemoPartPanel", 700, 520);
            AddBg(root);
            AddTitle(root, "③ Part（两种挂法）");

            AddText(root, "txtInfo", "", 14, TextMain,
                new Vector2(0, 0.42f), new Vector2(1, 0.84f), new Vector2(20, 0), new Vector2(-20, -6));

            // ---------- 节点级 Part：一个子节点，挂上 RevUIDemoPart，里面装它自己的控件 ----------
            GameObject partNode = NewNode("nodePart", root);
            Anchor(partNode, new Vector2(0, 0), new Vector2(1, 0), new Vector2(20, 96), new Vector2(-20, 236));
            partNode.AddComponent<RevUIDemoPart>();

            GameObject partBg = NewNode("Bg", partNode);
            var partBgImg = partBg.AddComponent<Image>();
            partBgImg.color = new Color(0.18f, 0.26f, 0.38f, 0.97f);
            Stretch(partBg);

            AddText(partNode, "txtPart", "① 节点级 Part（预制体里摆好，[RevBind] 拿到就自动初始化）", 14, TextMain,
                new Vector2(0, 0.40f), new Vector2(1, 1), new Vector2(14, 0), new Vector2(-14, -8));

            AddButton(partNode, "btnPartAdd", "点我 → NotifyHost() → 宿主 OnPartChanged", BtnGreen,
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(14, 12), new Vector2(-14, 50));

            // ---------- 预制体级 Part 的两个按钮 ----------
            AddButton(root, "btnAddPart", "② 预制体级：RevUIPart.Create<RevUIDemoPart>(this, …)", BtnBlue,
                new Vector2(0, 0), new Vector2(0.5f, 0), new Vector2(20, 52), new Vector2(-8, 90));

            AddButton(root, "btnClosePart", "② 关闭它：ClosePart()", BtnRed,
                new Vector2(0.5f, 0), new Vector2(1, 0), new Vector2(8, 52), new Vector2(-20, 90));

            AddClose(root);

            return Save(root, "RevUIDemoPartPanel");
        }

        // ============================================================
        // 存盘 + 设 AB 名
        // ============================================================
        private static bool Save(GameObject root, string prefabName)
        {
            string path = PrefabDir + "/" + prefabName + ".prefab";

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);                            // 场景里的临时对象用完即弃

            if (prefab == null)
            {
                Debug.LogError($"[RevUIDemoPrefabBuilder] 预制体保存失败：{path}");
                return false;
            }

            // AB 名（打包工具按"逻辑路径第一段的小写"命名，这里保持一致）—— 真机走 AB 时靠它找包
            AssetImporter importer = AssetImporter.GetAtPath(path);
            if (importer != null && importer.assetBundleName != BundleName)
            {
                importer.assetBundleName = BundleName;
            }

            return true;
        }

        // ============================================================
        // 搭界面用的小工具（全用锚点 + 显式尺寸，不依赖 LayoutGroup，行为可预测）
        // ============================================================

        /// <summary>面板根节点：带 RectTransform，尺寸固定（实例化后由框架挂到对应层）。</summary>
        private static GameObject NewPanel(string name, float width, float height)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(width, height);
            return go;
        }

        private static GameObject NewNode(string name, GameObject parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        private static void AddBg(GameObject root) => AddBg(root, PanelBg);

        private static void AddBg(GameObject root, Color color)
        {
            GameObject bg = NewNode("Bg", root);
            var img = bg.AddComponent<Image>();
            img.color = color;
            Stretch(bg);
        }

        private static void AddTitle(GameObject root, string text)
        {
            AddText(root, "txtTitle", text, 18, TextMain,
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(20, -48), new Vector2(-90, -12));
        }

        /// <summary>右上角的 ✕ 关闭按钮（所有演示面板共用）。</summary>
        private static void AddClose(GameObject root)
        {
            AddButton(root, "btnClose", "✕", BtnRed,
                new Vector2(1, 1), new Vector2(1, 1), new Vector2(-72, -44), new Vector2(-20, -14));
        }

        /// <summary>
        /// 放一段文本。锚点 / 偏移与 Unity 面板一致（anchorMin/anchorMax/offsetMin/offsetMax）。
        /// </summary>
        private static Text AddText(GameObject parent, string name, string content, int size, Color color,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offMin, Vector2 offMax)
        {
            GameObject go = NewNode(name, parent);
            var text = go.AddComponent<Text>();
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.font = BuiltinFont();
            Anchor(go, anchorMin, anchorMax, offMin, offMax);
            return text;
        }

        /// <summary>
        /// 放一个按钮。
        /// <paramref name="row"/> ≥ 0 时走"整列"排布：以面板**顶边**为基准，第 0 行贴着 <paramref name="top"/>（负数，往下），
        /// 每行 26 高、间隔 8；这时 x 只给左右边界（x0 / x1 是归一化锚点，left / right 是相对锚点的偏移）。
        /// <paramref name="row"/> &lt; 0 时按显式锚点 + 偏移摆放（自由布局）。
        /// </summary>
        private static void AddButton(GameObject parent, string name, string label, Color color,
            float x0, float x1, float left, float right, float top, int row)
        {
            GameObject go = NewNode(name, parent);
            var img = go.AddComponent<Image>();
            img.color = color;

            var button = go.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(color.r + 0.08f, color.g + 0.08f, color.b + 0.08f);
            colors.pressedColor = new Color(color.r - 0.06f, color.g - 0.06f, color.b - 0.06f);
            button.colors = colors;

            GameObject labelGo = NewNode("Text", go);
            var text = labelGo.AddComponent<Text>();
            text.text = label;
            text.fontSize = 14;
            text.fontStyle = FontStyle.Bold;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.font = BuiltinFont();
            Stretch(labelGo);

            Anchor(go, new Vector2(x0, 1), new Vector2(x1, 1),
                new Vector2(left, top - 26f - row * 34f), new Vector2(right, top - row * 34f));
        }

        /// <summary>自由摆放的按钮（给显式锚点 + 偏移）。</summary>
        private static void AddButton(GameObject parent, string name, string label, Color color,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offMin, Vector2 offMax)
        {
            GameObject go = NewNode(name, parent);
            var img = go.AddComponent<Image>();
            img.color = color;

            var button = go.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(color.r + 0.08f, color.g + 0.08f, color.b + 0.08f);
            colors.pressedColor = new Color(color.r - 0.06f, color.g - 0.06f, color.b - 0.06f);
            button.colors = colors;

            GameObject labelGo = NewNode("Text", go);
            var text = labelGo.AddComponent<Text>();
            text.text = label;
            text.fontSize = 14;
            text.fontStyle = FontStyle.Bold;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.font = BuiltinFont();
            Stretch(labelGo);

            Anchor(go, anchorMin, anchorMax, offMin, offMax);
        }

        /// <summary>整列排布时的公共定位：第 row 行（以顶边 top 为基准）。</summary>
        private static void AnchorRow(GameObject go, float x0, float x1, float left, float right, float top, int row)
            => Anchor(go, new Vector2(x0, 1), new Vector2(x1, 1),
                new Vector2(left, top - 26f - row * 34f), new Vector2(right, top - row * 34f));

        /// <summary>开关（Toggle）：一个方框 + 勾选块 + 右侧文字。</summary>
        private static void AddToggle(GameObject parent, string name, string label,
            float x0, float x1, float left, float right, float top, int row)
        {
            GameObject go = NewNode(name, parent);
            var bg = go.AddComponent<Image>();
            bg.color = FieldBg;

            var toggle = go.AddComponent<Toggle>();
            toggle.targetGraphic = bg;

            GameObject check = NewNode("Checkmark", go);
            var checkImg = check.AddComponent<Image>();
            checkImg.color = new Color(0.35f, 0.78f, 0.5f, 1f);
            Anchor(check, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(6, -7), new Vector2(20, 7));
            toggle.graphic = checkImg;
            toggle.isOn = false;

            AddText(go, "Label", label, 14, TextMain,
                new Vector2(0, 0), new Vector2(1, 1), new Vector2(26, 0), new Vector2(-8, 0));

            AnchorRow(go, x0, x1, left, right, top, row);
        }

        /// <summary>滑条（Slider）：底槽 + 填充 + 手柄。</summary>
        private static void AddSlider(GameObject parent, string name,
            float x0, float x1, float left, float right, float top, int row)
        {
            GameObject go = NewNode(name, parent);
            var slider = go.AddComponent<Slider>();

            GameObject background = NewNode("Background", go);
            var bgImg = background.AddComponent<Image>();
            bgImg.color = FieldBg;
            Anchor(background, new Vector2(0, 0.25f), new Vector2(1, 0.75f), new Vector2(0, 0), new Vector2(-14, 0));

            GameObject fillArea = NewNode("Fill Area", go);
            Anchor(fillArea, new Vector2(0, 0.25f), new Vector2(1, 0.75f), new Vector2(5, 0), new Vector2(-19, 0));

            GameObject fill = NewNode("Fill", fillArea);
            var fillImg = fill.AddComponent<Image>();
            fillImg.color = new Color(0.30f, 0.62f, 0.98f, 1f);
            Anchor(fill, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0), new Vector2(10, 0));

            GameObject handleArea = NewNode("Handle Slide Area", go);
            Anchor(handleArea, new Vector2(0, 0), new Vector2(1, 1), new Vector2(10, 0), new Vector2(-10, 0));

            GameObject handle = NewNode("Handle", handleArea);
            var handleImg = handle.AddComponent<Image>();
            handleImg.color = new Color(0.92f, 0.95f, 1f, 1f);
            Anchor(handle, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0), new Vector2(18, 0));

            slider.fillRect = (RectTransform)fill.transform;
            slider.handleRect = (RectTransform)handle.transform;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 0.6f;

            AnchorRow(go, x0, x1, left, right, top, row);
        }

        /// <summary>输入框（InputField）：底 + 文本 + 占位符。</summary>
        private static void AddInput(GameObject parent, string name, string placeholder,
            float x0, float x1, float left, float right, float top, int row)
        {
            GameObject go = NewNode(name, parent);
            var bg = go.AddComponent<Image>();
            bg.color = FieldBg;

            var input = go.AddComponent<InputField>();

            GameObject textGo = NewNode("Text", go);
            var text = textGo.AddComponent<Text>();
            text.fontSize = 14;
            text.color = TextMain;
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;
            text.font = BuiltinFont();
            Anchor(textGo, new Vector2(0, 0), new Vector2(1, 1), new Vector2(8, 0), new Vector2(-8, 0));

            GameObject phGo = NewNode("Placeholder", go);
            var ph = phGo.AddComponent<Text>();
            ph.text = placeholder;
            ph.fontSize = 14;
            ph.fontStyle = FontStyle.Italic;
            ph.color = new Color(0.62f, 0.68f, 0.78f, 0.8f);
            ph.alignment = TextAnchor.MiddleLeft;
            ph.font = BuiltinFont();
            Anchor(phGo, new Vector2(0, 0), new Vector2(1, 1), new Vector2(8, 0), new Vector2(-8, 0));

            input.textComponent = text;
            input.placeholder = ph;
            input.lineType = InputField.LineType.SingleLine;

            AnchorRow(go, x0, x1, left, right, top, row);
        }

        /// <summary>下拉框（Dropdown）：底 + 当前项文本 + 展开模板（Template/Viewport/Content/Item）。</summary>
        private static void AddDropdown(GameObject parent, string name, string[] options,
            float x0, float x1, float left, float right, float top, int row)
        {
            GameObject go = NewNode(name, parent);
            var bg = go.AddComponent<Image>();
            bg.color = FieldBg;

            var dropdown = go.AddComponent<Dropdown>();

            GameObject caption = NewNode("Label", go);
            var captionText = caption.AddComponent<Text>();
            captionText.fontSize = 14;
            captionText.color = TextMain;
            captionText.alignment = TextAnchor.MiddleLeft;
            captionText.font = BuiltinFont();
            Anchor(caption, new Vector2(0, 0), new Vector2(1, 1), new Vector2(10, 0), new Vector2(-24, 0));

            GameObject arrow = NewNode("Arrow", go);
            var arrowImg = arrow.AddComponent<Image>();
            arrowImg.color = TextSub;
            Anchor(arrow, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-20, -6), new Vector2(-8, 6));

            // ---------- 展开列表模板（Unity 要求：template 必须是子节点且默认失活） ----------
            GameObject template = NewNode("Template", go);
            var templateImg = template.AddComponent<Image>();
            templateImg.color = new Color(0.16f, 0.19f, 0.24f, 0.99f);
            Anchor(template, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, -152), new Vector2(0, 0));

            var scroll = template.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 20f;

            GameObject viewport = NewNode("Viewport", template);
            var viewportImg = viewport.AddComponent<Image>();
            viewportImg.color = new Color(1f, 1f, 1f, 0.02f);
            viewport.AddComponent<Mask>().showMaskGraphic = false;
            Stretch(viewport);

            GameObject content = NewNode("Content", viewport);
            var contentRect = (RectTransform)content.transform;
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = new Vector2(0, 28);

            var layout = content.AddComponent<VerticalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 2f;
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            GameObject item = NewNode("Item", content);
            var itemImg = item.AddComponent<Image>();
            itemImg.color = new Color(1f, 1f, 1f, 0.06f);
            var itemRect = (RectTransform)item.transform;
            itemRect.sizeDelta = new Vector2(0, 26);

            var itemToggle = item.AddComponent<Toggle>();
            itemToggle.targetGraphic = itemImg;

            GameObject itemCheck = NewNode("Item Checkmark", item);
            var itemCheckImg = itemCheck.AddComponent<Image>();
            itemCheckImg.color = new Color(0.35f, 0.78f, 0.5f, 1f);
            Anchor(itemCheck, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(6, -7), new Vector2(20, 7));
            itemToggle.graphic = itemCheckImg;

            GameObject itemLabel = NewNode("Item Label", item);
            var itemText = itemLabel.AddComponent<Text>();
            itemText.fontSize = 14;
            itemText.color = TextMain;
            itemText.alignment = TextAnchor.MiddleLeft;
            itemText.font = BuiltinFont();
            Anchor(itemLabel, new Vector2(0, 0), new Vector2(1, 1), new Vector2(26, 0), new Vector2(-8, 0));

            scroll.viewport = (RectTransform)viewport.transform;
            scroll.content = contentRect;

            dropdown.template = (RectTransform)template.transform;
            dropdown.captionText = captionText;
            dropdown.itemText = itemText;
            dropdown.options.Clear();
            for (int i = 0; i < options.Length; i++) dropdown.options.Add(new Dropdown.OptionData(options[i]));
            dropdown.value = 0;
            dropdown.RefreshShownValue();

            template.SetActive(false);             // ★ 必须失活：Dropdown.Show() 会自己克隆它

            AnchorRow(go, x0, x1, left, right, top, row);
        }

        /// <summary>滚动列表（ScrollRect）：Viewport(RectMask2D) + Content(VerticalLayoutGroup) + N 行文本。</summary>
        private static void AddScroll(GameObject parent, string name, int itemCount,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offMin, Vector2 offMax)
        {
            GameObject go = NewNode(name, parent);
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.10f, 0.12f, 0.16f, 0.9f);

            var scroll = go.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 24f;

            GameObject viewport = NewNode("Viewport", go);
            viewport.AddComponent<RectMask2D>();
            Stretch(viewport);

            GameObject content = NewNode("Content", viewport);
            var contentRect = (RectTransform)content.transform;
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = new Vector2(0, 0);

            var layout = content.AddComponent<VerticalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 2f;
            layout.padding = new RectOffset(6, 6, 6, 6);
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            for (int i = 0; i < itemCount; i++)
            {
                GameObject item = NewNode("Item" + i, content);
                var text = item.AddComponent<Text>();
                text.text = $"列表项 {i + 1}／{itemCount} —— 滚这里试试 [RevScrollChanged(\"{name}\")]";
                text.fontSize = 13;
                text.color = TextMain;
                text.alignment = TextAnchor.MiddleLeft;
                text.font = BuiltinFont();
                item.AddComponent<LayoutElement>().minHeight = 26f;
            }

            scroll.viewport = (RectTransform)viewport.transform;
            scroll.content = contentRect;

            Anchor(go, anchorMin, anchorMax, offMin, offMax);
        }

        /// <summary>铺满父节点。</summary>
        private static void Stretch(GameObject go)
        {
            var r = (RectTransform)go.transform;
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
        }

        /// <summary>按锚点与偏移摆一个矩形（offsetMin = 左下，offsetMax = 右上）。</summary>
        private static void Anchor(GameObject go, Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax)
        {
            var r = (RectTransform)go.transform;
            r.anchorMin = min;
            r.anchorMax = max;
            r.offsetMin = offMin;
            r.offsetMax = offMax;
        }

        /// <summary>Unity 内置字体（旧版 UI Text 用；TMP 不在本演示的依赖里）。</summary>
        private static Font BuiltinFont() => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }
}
#endif
