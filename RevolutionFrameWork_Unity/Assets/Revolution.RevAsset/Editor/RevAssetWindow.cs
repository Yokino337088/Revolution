using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Revolution.RevAsset.Editor
{
    /// <summary>从项目选型、分包预览到构建与签名的 RevAsset 工作台。</summary>
    public sealed class RevAssetWindow : EditorWindow
    {
        /// <summary>新手说明与模块目录共用的文档入口。</summary>
        private const string GuideUrl = "https://github.com/Yokino337088/Revolution/wiki/RevAsset";

        /// <summary>表格每页在窗口中显示的行数。</summary>
        private const int PreviewPageSize = 10;

        /// <summary>项目设置中可选的两种资源后端。</summary>
        private static readonly string[] Backends = { "Revolution 内置", "RevAsset" };

        /// <summary>按 RevAssetPacking 的序号排列，方便在保持枚举值的同时显示中文。</summary>
        private static readonly string[] PackingChoices = { "沿用原生标签或按目录", "合成指定包", "按目录分包", "一项一包", "排除公开地址" };

        /// <summary>分包预览的两种查阅视角。</summary>
        private static readonly string[] PreviewChoices = { "原生包", "逻辑地址" };

        /// <summary>切换页签时将相应的任务和参数放在一起。</summary>
        private enum Page
        {
            /// <summary>项目后端与更新频道。</summary>
            Project,
            /// <summary>分包规则与结果预览。</summary>
            Rules,
            /// <summary>首包与签名发布。</summary>
            Build
        }

        /// <summary>当前工作阶段，重编译时保留。</summary>
        [SerializeField] private Page _page;

        /// <summary>本次构建选择的目标平台。</summary>
        [SerializeField] private BuildTarget _target = BuildTarget.NoTarget;

        /// <summary>本次构建的内容版本名。</summary>
        [SerializeField] private string _version = "1.0.0";

        /// <summary>频道使用的递增发布序号。</summary>
        [SerializeField] private long _revision = 1;

        /// <summary>当前页的滚动位置，切换时重置。</summary>
        private Vector2 _scroll;

        /// <summary>分包规则和首包配置共用的编辑对象。</summary>
        private SerializedObject _profile;

        /// <summary>单独编辑所选规则，列表只显示分包决策。</summary>
        private ReorderableList _rules;

        /// <summary>当前项目运行配置的编辑对象。</summary>
        private SerializedObject _settings;

        /// <summary>本窗口持有的主题资源。</summary>
        private RevAssetWindowTheme _theme;

        /// <summary>更新频道默认折叠，便于首次使用者先完成本地加载。</summary>
        private bool _showChannel;

        /// <summary>签名发布参数的折叠状态。</summary>
        private bool _showSigning;

        /// <summary>当前会话选择的私钥文件路径。只在本次构建时读取。</summary>
        private string _privateKey;

        /// <summary>最近一次操作的提示，出错时保留构建器给出的原因。</summary>
        private string _notice;

        /// <summary>与提示内容对应的显示类型。</summary>
        private MessageType _noticeType;

        /// <summary>最近一次分包预览结果，修改采集规则后清除。</summary>
        private RevAssetBuildPlan _preview;

        /// <summary>预览时固定的包顺序，绘制时可按页取行。</summary>
        private KeyValuePair<string, RevAssetBundlePlan>[] _previewBundles;

        /// <summary>当前预览表格在包列表与地址列表间的选择。</summary>
        private int _previewView;

        /// <summary>当前预览表格的一页。</summary>
        private int _previewPage;

        /// <summary>本窗口最近一次成功构建的实际产物回执。</summary>
        private RevAssetBuildResult _result;

        /// <summary>从框架资源菜单打开工作台。</summary>
        [MenuItem("Revolution.Tools/资源/RevAsset", false, 3)]
        public static void Open()
        {
            GetWindow<RevAssetWindow>("RevAsset").Show();
        }

        /// <summary>恢复窗体状态并连接现有构建规则，不创建运行配置。</summary>
        private void OnEnable()
        {
            minSize = new Vector2(620, 450);
            titleContent = new GUIContent("RevAsset", EditorGUIUtility.FindTexture("PreMatCube"));
            if (_target == BuildTarget.NoTarget)
            {
                _target = EditorUserBuildSettings.activeBuildTarget;
            }
            _profile = new SerializedObject(RevAssetBuildProfile.instance);
            _rules = new ReorderableList(_profile, _profile.FindProperty("Rules"), false, true, true, true);
            _rules.elementHeight = 27;
            _rules.index = _rules.count > 0 ? 0 : -1;
            _rules.drawHeaderCallback = DrawRuleHeader;
            _rules.drawElementCallback = DrawRuleRow;
            _rules.onAddCallback = AddRule;
        }

        /// <summary>窗口关闭或脚本重载时释放当前使用的主题纹理。</summary>
        private void OnDisable()
        {
            _theme?.Dispose();
            _theme = null;
        }

        /// <summary>绘制固定的顶部导航与当前页。播放时保留查看入口，停用修改和构建。</summary>
        private void OnGUI()
        {
            bool dark = EditorGUIUtility.isProSkin;
            if (_theme == null || _theme.IsDark != dark)
            {
                _theme?.Dispose();
                _theme = new RevAssetWindowTheme(dark);
            }
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), _theme.Background);
            }

            var settings = RevAssetEditorProject.GetSettings(false);
            bool selected = RevAssetEditorProject.Selected;
            Page visiblePage = _page;
            GUILayout.Space(14);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(16);
                using (new EditorGUILayout.VerticalScope())
                {
                    DrawHeader(selected, settings);
                    GUILayout.Space(10);
                    DrawNavigation(visiblePage);
                }
                GUILayout.Space(16);
            }
            GUILayout.Space(8);

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox("退出 Play 后可保存设置或构建资源。", MessageType.Info);
            }

            using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll, false, false))
            {
                _scroll = scroll.scrollPosition;
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(16);
                    using (new EditorGUILayout.VerticalScope())
                    {
                        if (!string.IsNullOrEmpty(_notice))
                        {
                            EditorGUILayout.HelpBox(_notice, _noticeType);
                            GUILayout.Space(8);
                        }
                        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                        {
                            switch (visiblePage)
                            {
                                case Page.Project:
                                    DrawProject(settings, selected);
                                    break;
                                case Page.Rules:
                                    DrawRules();
                                    break;
                                case Page.Build:
                                    DrawBuild();
                                    break;
                            }
                        }
                        GUILayout.Space(16);
                    }
                    GUILayout.Space(16);
                }
            }
        }

        /// <summary>集中显示工具标题、项目选择和使用说明链接。</summary>
        private void DrawHeader(bool selected, RevAssetSettings settings)
        {
            using (new EditorGUILayout.HorizontalScope(_theme.Header))
            {
                using (new EditorGUILayout.VerticalScope())
                {
                    GUILayout.Label("RevAsset", _theme.Title);
                    GUILayout.Label("资源构建与发布", _theme.HeaderNote);
                }
                GUILayout.FlexibleSpace();
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(155)))
                {
                    string mode = !selected ? "Revolution 内置" : settings == null || settings.EditorSimulation
                        ? "RevAsset · 编辑器模拟" : "RevAsset · 原生 AB";
                    GUILayout.Label(mode, _theme.Badge, GUILayout.ExpandWidth(true));
                    GUILayout.Space(4);
                    if (GUILayout.Button("使用说明", _theme.HeaderButton, GUILayout.ExpandWidth(true)))
                    {
                        Application.OpenURL(GuideUrl);
                    }
                }
            }
        }

        /// <summary>提供三个固定入口，鼠标和键盘可以直接切换工作阶段。</summary>
        private void DrawNavigation(Page visiblePage)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawTab(Page.Project, visiblePage, "项目设置");
                GUILayout.Space(6);
                DrawTab(Page.Rules, visiblePage, "分包规则");
                GUILayout.Space(6);
                DrawTab(Page.Build, visiblePage, "构建与发布");
            }
        }

        /// <summary>绘制一个页签，选择后在下一轮布局切换内容。</summary>
        private void DrawTab(Page target, Page visiblePage, string title)
        {
            if (GUILayout.Button(title, target == visiblePage ? _theme.ActiveTab : _theme.QuietButton, GUILayout.ExpandWidth(true)))
            {
                ChangePage(target);
            }
        }

        /// <summary>切换页面，回到新页顶部并释放旧输入框的焦点。</summary>
        private void ChangePage(Page next)
        {
            if (_page == next)
            {
                return;
            }
            _page = next;
            _scroll = Vector2.zero;
            _notice = null;
            GUI.FocusControl(null);
            Repaint();
        }

        /// <summary>设置项目的下次启动后端、编辑器模拟和可选更新频道。</summary>
        private void DrawProject(RevAssetSettings settings, bool selected)
        {
            using (new EditorGUILayout.VerticalScope(_theme.Card))
            {
                GUILayout.Label("资源后端", _theme.Heading);
                GUILayout.Space(9);
                int current = selected ? 1 : 0;
                int choice = EditorGUILayout.Popup("项目资源后端", current, Backends);
                if (choice != current)
                {
                    Run(() =>
                    {
                        RevAssetEditorProject.Select(choice == 1, settings == null || settings.EditorSimulation);
                        SetNotice("已保存项目后端，下次启动生效。");
                    });
                }
                GUILayout.Space(8);
                if (settings != null)
                {
                    if (_settings == null || _settings.targetObject != settings)
                    {
                        _settings = new SerializedObject(settings);
                    }
                    _settings.Update();
                    Field(_settings, "EditorSimulation", "编辑器模拟");
                    GUILayout.Label("开启后 Play 直接读取工程资产。关闭后 Play 读取本机平台的原生 AB。", _theme.Note);
                    GUILayout.Space(10);
                    Field(_settings, "Package", "资源包名");
                    Field(_settings, "Compatibility", "应用兼容代号");
                }
                else
                {
                    GUILayout.Label("选择 RevAsset 后可设置编辑器模拟和更新频道。", _theme.Note);
                }
                GUILayout.Space(10);
                if (GUILayout.Button("前往分包规则", _theme.PrimaryButton, GUILayout.Width(128)))
                {
                    ChangePage(Page.Rules);
                }
            }

            if (settings != null)
            {
                GUILayout.Space(10);
                using (new EditorGUILayout.VerticalScope(_theme.Card))
                {
                    _showChannel = EditorGUILayout.Foldout(_showChannel, "更新频道", true, EditorStyles.foldoutHeader);
                    if (_showChannel)
                    {
                        GUILayout.Space(8);
                        Field(_settings, "ChannelUrl", "发布目录 URL");
                        Field(_settings, "PublicKey", "发布公钥");
                        Field(_settings, "CheckOnStart", "启动后检查更新");
                    }
                }
                if (_settings.ApplyModifiedProperties())
                {
                    AssetDatabase.SaveAssets();
                }
            }
        }

        /// <summary>对齐规则列表的路径和打包方式两列。</summary>
        private static void DrawRuleHeader(Rect rect)
        {
            float split = rect.width * .6f;
            EditorGUI.LabelField(new Rect(rect.x + 6, rect.y, split - 6, rect.height), "采集路径");
            EditorGUI.LabelField(new Rect(rect.x + split + 6, rect.y, rect.width - split - 6, rect.height), "分包方式");
        }

        /// <summary>显示根目录、编辑规则与生成预览的入口。保存后使旧预览失效。</summary>
        private void DrawRules()
        {
            _profile.Update();
            RevAssetBuildPlan visiblePreview = _preview;
            using (new EditorGUILayout.VerticalScope(_theme.Card))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("采集与分包", _theme.Heading);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("预览分包", _theme.PrimaryButton, GUILayout.Width(104)))
                    {
                        Run(Preview);
                    }
                }
                GUILayout.Space(8);
                Field(_profile, "AddressRoot", "逻辑路径根目录");
                Field(_profile, "ExtractSharedDependencies", "归并共享依赖");
                GUILayout.Space(8);
                _rules.DoLayoutList();
                if (_rules.index >= 0 && _rules.index < _rules.count)
                {
                    DrawRuleDetail(_rules.serializedProperty.GetArrayElementAtIndex(_rules.index));
                }
            }
            if (_profile.ApplyModifiedProperties())
            {
                RevAssetBuildProfile.instance.SaveProfile();
                _preview = null;
                _previewBundles = null;
            }
            if (visiblePreview != null && ReferenceEquals(visiblePreview, _preview))
            {
                GUILayout.Space(10);
                DrawPreview(visiblePreview);
            }
        }

        /// <summary>显示一条规则的选择器和打包方式，完整长路径在提示中可读。</summary>
        private void DrawRuleRow(Rect rect, int index, bool active, bool focused)
        {
            SerializedProperty item = _rules.serializedProperty.GetArrayElementAtIndex(index);
            string path = item.FindPropertyRelative("Path").stringValue;
            string display = string.IsNullOrEmpty(path) ? "待填写资源路径" : path;
            var pathRect = new Rect(rect.x + 6, rect.y + 2, rect.width * .6f - 8, rect.height - 3);
            var packingRect = new Rect(pathRect.xMax + 9, pathRect.y, rect.width * .4f - 10, pathRect.height);
            GUI.Label(pathRect, new GUIContent(display, path), _theme.Cell);
            var packing = (RevAssetPacking)item.FindPropertyRelative("Packing").intValue;
            GUI.Label(packingRect, PackingName(packing), _theme.Cell);
        }

        /// <summary>新增空选择器，避免默认克隆旧规则导致同一路径被登记两次。</summary>
        private void AddRule(ReorderableList list)
        {
            SerializedProperty rules = list.serializedProperty;
            int index = rules.arraySize;
            rules.InsertArrayElementAtIndex(index);
            SerializedProperty item = rules.GetArrayElementAtIndex(index);
            item.FindPropertyRelative("Path").stringValue = "";
            item.FindPropertyRelative("Packing").intValue = (int)RevAssetPacking.NativeOrFolder;
            item.FindPropertyRelative("Bundle").stringValue = "";
            item.FindPropertyRelative("Variant").stringValue = "";
            item.FindPropertyRelative("Address").stringValue = "";
            SerializedProperty tags = item.FindPropertyRelative("Tags");
            tags.arraySize = 1;
            tags.GetArrayElementAtIndex(0).stringValue = "core";
            list.index = index;
        }

        /// <summary>只展开当前规则的参数，原生标签由 Unity 导入设置提供。</summary>
        private void DrawRuleDetail(SerializedProperty item)
        {
            GUILayout.Space(7);
            GUILayout.Label("规则详情", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                SerializedProperty path = item.FindPropertyRelative("Path");
                EditorGUILayout.PropertyField(path, new GUIContent("资源文件或目录"));
                string projectSelection = AssetDatabase.GetAssetPath(Selection.activeObject);
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(projectSelection) ||
                    (projectSelection != "Assets" && !projectSelection.StartsWith("Assets/", StringComparison.Ordinal))))
                {
                    if (GUILayout.Button("使用 Project 选中项", _theme.QuietButton, GUILayout.Width(142)))
                    {
                        path.stringValue = projectSelection;
                    }
                }
            }
            SerializedProperty property = item.FindPropertyRelative("Packing");
            var packing = (RevAssetPacking)property.intValue;
            int choice = EditorGUILayout.Popup("分包方式", (int)packing, PackingChoices);
            if (choice >= 0 && choice != (int)packing)
            {
                property.intValue = choice;
            }
            if (packing == RevAssetPacking.Together)
            {
                EditorGUILayout.PropertyField(item.FindPropertyRelative("Bundle"), new GUIContent("原生包名"));
                EditorGUILayout.PropertyField(item.FindPropertyRelative("Variant"), new GUIContent("原生变体"));
            }
            if (packing != RevAssetPacking.Exclude)
            {
                EditorGUILayout.PropertyField(item.FindPropertyRelative("Address"), new GUIContent("逻辑地址或前缀"));
                EditorGUILayout.PropertyField(item.FindPropertyRelative("Tags"), new GUIContent("标签"), true);
            }
        }

        /// <summary>把保存后的项目规则交给正式采集器，生成包与地址两份预览。</summary>
        private void Preview()
        {
            if (_profile.ApplyModifiedProperties())
            {
                RevAssetBuildProfile.instance.SaveProfile();
            }
            _preview = null;
            _previewBundles = null;
            RevAssetBuildPlan plan = RevAssetCollector.Collect(RevAssetBuildProfile.instance, true);
            _previewBundles = plan.Bundles.ToArray();
            _preview = plan;
            _previewPage = 0;
        }

        /// <summary>显示统计、包归属和实际逻辑地址。只绘制当前页的表格行。</summary>
        private void DrawPreview(RevAssetBuildPlan plan)
        {
            using (new EditorGUILayout.VerticalScope(_theme.Card))
            {
                GUILayout.Label("分包预览", _theme.Heading);
                GUILayout.Label($"{plan.Bundles.Count} 个包    {plan.Addresses.Count} 个公开地址    {plan.SharedAssets} 项共享依赖", _theme.Note);
                GUILayout.Space(6);
                int view = _previewView;
                int page = _previewPage;
                int chosen = GUILayout.Toolbar(view, PreviewChoices);
                if (chosen != view)
                {
                    _previewView = chosen;
                    _previewPage = 0;
                }
                int total = view == 0 ? _previewBundles.Length : plan.Addresses.Count;
                int pages = Mathf.Max(1, (total + PreviewPageSize - 1) / PreviewPageSize);
                int from = page * PreviewPageSize;
                GUILayout.Space(5);
                for (int i = from; i < Mathf.Min(total, from + PreviewPageSize); i++)
                {
                    if (view == 0)
                    {
                        var bundle = _previewBundles[i];
                        ResultRow(i, bundle.Key, $"{bundle.Value.Assets.Count} 项资产");
                    }
                    else
                    {
                        RevAssetAddressData address = plan.Addresses[i];
                        ResultRow(i, address.key, address.bundle);
                    }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label($"第 {page + 1} / {pages} 页", _theme.Note);
                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(page == 0))
                    {
                        if (GUILayout.Button("上一页", _theme.QuietButton, GUILayout.Width(64)))
                        {
                            _previewPage--;
                        }
                    }
                    using (new EditorGUI.DisabledScope(page + 1 >= pages))
                    {
                        if (GUILayout.Button("下一页", _theme.QuietButton, GUILayout.Width(64)))
                        {
                            _previewPage++;
                        }
                    }
                }
            }
        }

        /// <summary>绘制可查看完整文本的单行结果，长包名不会挤占相邻列。</summary>
        private void ResultRow(int index, string key, string detail)
        {
            Rect row = GUILayoutUtility.GetRect(1, 23, GUILayout.ExpandWidth(true));
            if ((index & 1) == 0 && Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(row, _theme.AlternateRow);
            }
            float split = Mathf.Floor(row.width * .57f);
            GUI.Label(new Rect(row.x + 7, row.y + 2, split - 12, row.height - 2), new GUIContent(key, key), _theme.Cell);
            GUI.Label(new Rect(row.x + split, row.y + 2, row.width - split - 7, row.height - 2), new GUIContent(detail, detail), _theme.Cell);
        }

        /// <summary>构建参数与首包操作优先显示，签名参数按需展开。</summary>
        private void DrawBuild()
        {
            _profile.Update();
            using (new EditorGUILayout.VerticalScope(_theme.Card))
            {
                GUILayout.Label("准备首包", _theme.Heading);
                GUILayout.Space(9);
                _target = (BuildTarget)EditorGUILayout.EnumPopup("构建平台", _target);
                _version = EditorGUILayout.TextField("内容版本", _version);
                _revision = EditorGUILayout.LongField("发布序号", _revision);
                Field(_profile, "BuiltinTags", "首包标签（空为全量）");
                Field(_profile, "Uncompressed", "不压缩 AB");
                GUILayout.Space(8);
                if (GUILayout.Button("构建并准备首包", _theme.PrimaryButton, GUILayout.Width(150)))
                {
                    Run(() => Build(true));
                }
            }

            GUILayout.Space(10);
            using (new EditorGUILayout.VerticalScope(_theme.Card))
            {
                _showSigning = EditorGUILayout.Foldout(_showSigning, "签名发布", true, EditorStyles.foldoutHeader);
                if (_showSigning)
                {
                    DrawSigning();
                }
            }
            if (_profile.ApplyModifiedProperties())
            {
                RevAssetBuildProfile.instance.SaveProfile();
            }
            if (_result != null)
            {
                GUILayout.Space(10);
                DrawBuildResult(_result);
            }
        }

        /// <summary>选择或生成本次构建的私钥，只有已选私钥才可签名。</summary>
        private void DrawSigning()
        {
            GUILayout.Space(7);
            GUILayout.Label(string.IsNullOrEmpty(_privateKey) ? "选择已有私钥，或生成一对新的发布密钥。" : $"私钥文件  {Path.GetFileName(_privateKey)}", _theme.Note);
            GUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("选择签名私钥", _theme.QuietButton, GUILayout.Width(115)))
                {
                    string key = EditorUtility.OpenFilePanel("选择发布私钥", "", "xml");
                    if (!string.IsNullOrEmpty(key))
                    {
                        _privateKey = key;
                    }
                }
                if (GUILayout.Button("生成发布密钥", _theme.QuietButton, GUILayout.Width(115)))
                {
                    Run(CreateKeys);
                }
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_privateKey)))
                {
                    if (GUILayout.Button("构建并签名发布", _theme.PrimaryButton, GUILayout.Width(145)))
                    {
                        Run(() => Build(false));
                    }
                }
            }
        }

        /// <summary>先保存本页发布配置，再执行实际构建。失败时保留窗口和原始错误。</summary>
        private void Build(bool builtin)
        {
            if (_profile.ApplyModifiedProperties())
            {
                RevAssetBuildProfile.instance.SaveProfile();
            }
            _result = null;
            _result = RevAssetBuilder.Build(_target, _version, _revision, builtin, signingKeyPath: builtin ? null : _privateKey);
            SetNotice(builtin ? "首包构建完成。" : "签名发布产物已生成。");
        }

        /// <summary>展示本次构建的真实平台、统计和产物路径。</summary>
        private void DrawBuildResult(RevAssetBuildResult result)
        {
            using (new EditorGUILayout.VerticalScope(_theme.Card))
            {
                GUILayout.Label(result.builtin ? "首包构建结果" : "签名发布结果", _theme.Heading);
                GUILayout.Label($"{result.target}  ·  {result.version}", _theme.Note);
                GUILayout.Label($"{result.bundles} 个包    {result.assets} 个地址    {EditorUtility.FormatBytes(result.bytes)}", _theme.Cell);
                if (result.builtin)
                {
                    GUILayout.Label($"首包包含 {result.builtinObjects} 个制品，占 {EditorUtility.FormatBytes(result.builtinBytes)}。", _theme.Note);
                }
                GUILayout.Space(6);
                EditorGUILayout.SelectableLabel(result.output, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                if (GUILayout.Button("打开产物目录", _theme.QuietButton, GUILayout.Width(116)))
                {
                    Run(() =>
                    {
                        if (!Directory.Exists(result.output))
                        {
                            throw new DirectoryNotFoundException("产物目录已移动或删除。");
                        }
                        EditorUtility.RevealInFinder(result.output);
                    });
                }
            }
        }

        /// <summary>生成 RSA 发布密钥，私钥放在项目外，公钥保存在项目运行配置中。</summary>
        private void CreateKeys()
        {
            string path = EditorUtility.SaveFilePanel("生成发布私钥", "", "revasset-private", "xml");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            string project = Path.GetFullPath(".") + Path.DirectorySeparatorChar;
            if (Path.GetFullPath(path).StartsWith(project, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("请把私钥保存在项目目录之外");
            }
            using (var key = new RSACryptoServiceProvider(2048) { PersistKeyInCsp = false })
            {
                RSAParameters value = key.ExportParameters(false);
                if (value.Modulus.Length < 256)
                {
                    throw new CryptographicException("Unity 编辑器无法生成 2048 位发布密钥");
                }
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                using (var writer = new StreamWriter(stream, RevAssetFiles.Utf8))
                {
                    writer.Write(key.ToXmlString(true));
                }
                var settings = RevAssetEditorProject.GetSettings(true);
                settings.PublicKey = Convert.ToBase64String(value.Modulus) + ":" + Convert.ToBase64String(value.Exponent);
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }
            _privateKey = path;
            SetNotice("发布公钥已保存到项目配置。");
        }

        /// <summary>保证序列化编辑行为与 Unity Inspector 一致。</summary>
        private static void Field(SerializedObject owner, string name, string label)
        {
            EditorGUILayout.PropertyField(owner.FindProperty(name), new GUIContent(label), true);
        }

        /// <summary>按规则真实枚举显示简短中文，便于快速浏览。</summary>
        private static string PackingName(RevAssetPacking packing)
        {
            return packing switch
            {
                RevAssetPacking.Together => "合成指定包",
                RevAssetPacking.ByFolder => "按目录",
                RevAssetPacking.ByAsset => "一项一包",
                RevAssetPacking.Exclude => "排除公开地址",
                _ => "原生标签或目录"
            };
        }

        /// <summary>保存最近一次操作完成提示。</summary>
        private void SetNotice(string message)
        {
            _notice = message;
            _noticeType = MessageType.Info;
        }

        /// <summary>接收真实操作的错误，并在当前页面顶部让用户可见。</summary>
        private void Run(Action action)
        {
            _notice = null;
            try
            {
                action();
            }
            catch (Exception error)
            {
                _notice = error.Message;
                _noticeType = MessageType.Error;
            }
            Repaint();
        }
    }
}
