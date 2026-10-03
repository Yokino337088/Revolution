// ============================================================
// RevHotUpdateWindow.cs —— 热更清单工具窗口（打完 AB 后的"下一步"）
//
// 位置：Assets\Revolution.HotUpdate\Editor\
//
// 【操作顺序】
//   ① 用框架的打包工具打一次 AB（AssetBundles/&lt;平台&gt;/）
//   ② 打开本窗口：确认平台 / 版本号 → 点「生成清单」
//   ③ 点「自检」—— 有问题当场拦下（缺包 / 大小不符 / 依赖缺失）
//   ④ 点「打开目录」→ 把整个目录上传到 CDN（★ 先传内容、最后传 RevHotManifest.txt）
// ============================================================
using Revolution.Editor;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Revolution.HotUpdate.Editor
{
    /// <summary>热更清单工具窗口（够用就好：平台 / 版本 / 生成 / 自检 / 打开目录）。</summary>
    public sealed class RevHotUpdateWindow : EditorWindow
    {
        private string _outputDir = "";
        private string _appVersion = "1.0.0";
        private string _resVersion = "1.0.0.1";
        private string _channel = "";
        private string _env = "";
        private RevHotManifest _manifest;
        private string _report = "";
        private Vector2 _scroll;

        /// <summary>菜单入口。</summary>
        [MenuItem("Revolution.Tools/热更新/热更清单窗口", false, 11)]
        public static void Open()
        {
            GetWindow<RevHotUpdateWindow>("RevHotUpdate");
        }

        private void OnEnable()
        {
            // 默认输出目录 = 框架打包工具的约定路径（AssetBundles/&lt;当前平台&gt;）
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            _outputDir = ABBuildSetting.GetOutputDir(target).Replace('\\', '/');

            // 默认版本号 = 打包配置里填的那个（ABBuildConfig 是框架的打包配置资产）
            ABBuildConfig config = ABBuildConfig.Instance;
            if (config != null && string.IsNullOrEmpty(config.version) == false)
            {
                _appVersion = config.version;
                _resVersion = config.version + ".1";
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("先用框架的打包工具打出 AB，再在这里生成热更清单。上传顺序：先传所有内容，最后传 RevHotManifest.txt。", MessageType.Info);

            EditorGUILayout.LabelField("平台（当前 Build Target）", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("  " + ABBuildSetting.GetPlatformName(EditorUserBuildSettings.activeBuildTarget));

            EditorGUILayout.Space(4);
            _outputDir = EditorGUILayout.TextField("产物目录", _outputDir);
            _appVersion = EditorGUILayout.TextField("大版本（AppVersion）", _appVersion);
            _resVersion = EditorGUILayout.TextField("资源版本（ResVersion）", _resVersion);
            _channel = EditorGUILayout.TextField("渠道（可空）", _channel);
            _env = EditorGUILayout.TextField("环境（可空）", _env);

            EditorGUILayout.Space(8);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("① 生成清单", GUILayout.Height(28)))
            {
                Generate();
            }

            if (GUILayout.Button("② 自检", GUILayout.Height(28)))
            {
                Check();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("打开产物目录"))
            {
                string full = Path.GetFullPath(_outputDir);
                if (Directory.Exists(full)) EditorUtility.RevealInFinder(full);
                else EditorUtility.DisplayDialog("RevHotUpdate", "目录不存在：" + full, "好");
            }

            if (GUILayout.Button("重新加载"))
            {
                ReloadManifest();
            }
            EditorGUILayout.EndHorizontal();

            if (string.IsNullOrEmpty(_report) == false)
            {
                EditorGUILayout.Space(8);
                _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(120));
                EditorGUILayout.HelpBox(_report, _report.StartsWith("✓") ? MessageType.Info : MessageType.Warning);
                EditorGUILayout.EndScrollView();
            }
        }

        private void Generate()
        {
            string error;
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;      // 先取出来，调用就能写成单行
            _manifest = RevHotManifestBuilder.Build(target, _outputDir, _appVersion, _resVersion, _channel, _env, out error);

            if (_manifest == null)
            {
                _report = error;
                return;
            }

            RevHotManifestBuilder.WriteOutputs(_manifest, _outputDir);
            AssetDatabase.Refresh();

            _report = "✓ 已生成：\n"
                      + _outputDir + "/RevHotManifest.txt（上传 CDN，最后传）\n"
                      + _outputDir + "/" + RevHotManifestBuilder.BuiltinManifestFileName + "（首包基线，随包体 / StreamingAssets）\n"
                      + "包数量：" + _manifest.Bundles.Count + " · 总大小：" + RevHotProgress.FormatBytes(_manifest.TotalBytes());
        }

        private void Check()
        {
            if (_manifest == null)
            {
                ReloadManifest();
            }

            if (_manifest == null)
            {
                _report = "还没有可检查的清单：先「生成清单」，或确认产物目录里有 RevHotManifest.txt";
                return;
            }

            string problems = RevHotManifestBuilder.SelfCheck(_manifest, _outputDir);
            _report = problems.Length == 0 ? "✓ 自检通过：包齐全、大小一致、依赖闭环" : problems;
        }

        private void ReloadManifest()
        {
            string path = Path.Combine(_outputDir, "RevHotManifest.txt");
            if (File.Exists(path) == false)
            {
                _report = "产物目录里没有 RevHotManifest.txt：先生成一次";
                return;
            }

            string error;
            _manifest = RevHotManifest.Parse(File.ReadAllText(path), out error);
            _report = _manifest == null ? error : "✓ 已加载清单：" + _manifest.ResVersion + "（" + _manifest.Bundles.Count + " 个包）";
        }
    }
}
