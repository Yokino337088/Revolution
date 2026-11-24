using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace Revolution.RevAsset.Editor
{
    /// <summary>项目的资源后端选择、分包规则、原生预览、首包和频道发布界面。</summary>
    public sealed class RevAssetWindow : EditorWindow
    {
        /// <summary>编辑器规则列表的序列化界面。</summary>
        private SerializedObject _profile;

        /// <summary>持久化公开运行配置的表单。</summary>
        private SerializedObject _settings;

        /// <summary>面板当前滚动位置。</summary>
        private Vector2 _scroll;

        /// <summary>本次使用的目标平台。</summary>
        private BuildTarget _target;

        /// <summary>当前要交付的可读内容版本。</summary>
        private string _version = "1.0.0";

        /// <summary>当前发布序号。</summary>
        private long _revision = 1;

        /// <summary>发布时单次选择的私钥位置。</summary>
        private string _privateKey;

        /// <summary>最近一次预览、构建或用户可处理的错误。</summary>
        private string _message;

        /// <summary>从框架资源菜单打开工作台。</summary>
        [MenuItem("Revolution.Tools/资源/RevAsset")]
        public static void Open()
        {
            GetWindow<RevAssetWindow>("RevAsset");
        }

        /// <summary>默认从当前 Unity 构建平台开始设置。</summary>
        private void OnEnable()
        {
            _profile = new SerializedObject(RevAssetBuildProfile.instance);
            _target = EditorUserBuildSettings.activeBuildTarget;
        }

        /// <summary>显示项目模式、规则和构建入口，全部写入操作只在编辑态可用。</summary>
        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                DrawBackend();
                DrawProfile();
                DrawBuild();
            }
            if (!string.IsNullOrEmpty(_message))
            {
                EditorGUILayout.HelpBox(_message, MessageType.Info);
            }
            EditorGUILayout.EndScrollView();
        }

        /// <summary>后端状态与编辑器模拟开关，默认项目保持框架自带策略。</summary>
        private void DrawBackend()
        {
            RevAssetSettings existing = RevAssetEditorProject.GetSettings(false);
            bool selected = RevAssetEditorProject.Selected;
            EditorGUI.BeginChangeCheck();
            int choice = EditorGUILayout.Popup("项目资源后端", selected ? 1 : 0, new[] { "Revolution 内置", "RevAsset" });
            if (EditorGUI.EndChangeCheck())
            {
                Run(() => RevAssetEditorProject.Select(choice == 1, existing == null || existing.EditorSimulation));
            }

            if (existing == null)
            {
                return;
            }
            if (_settings == null || _settings.targetObject != existing)
            {
                _settings = new SerializedObject(existing);
            }
            _settings.Update();
            EditorGUILayout.PropertyField(_settings.FindProperty("Package"), new GUIContent("资源包名"));
            EditorGUILayout.PropertyField(_settings.FindProperty("Compatibility"), new GUIContent("应用兼容代号"));
            EditorGUILayout.PropertyField(_settings.FindProperty("EditorSimulation"), new GUIContent("Play 使用模拟"));
            EditorGUILayout.PropertyField(_settings.FindProperty("ChannelUrl"), new GUIContent("发布目录 URL"));
            EditorGUILayout.PropertyField(_settings.FindProperty("PublicKey"), new GUIContent("发布公钥"));
            EditorGUILayout.PropertyField(_settings.FindProperty("CheckOnStart"), new GUIContent("启动后检查更新"));
            if (_settings.ApplyModifiedProperties())
            {
                AssetDatabase.SaveAssets();
            }
        }

        /// <summary>显示同一份可提交到版本库的资源分包规则。</summary>
        private void DrawProfile()
        {
            _profile.Update();
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(_profile.FindProperty("AddressRoot"), new GUIContent("逻辑路径根目录"));
            EditorGUILayout.PropertyField(_profile.FindProperty("Rules"), new GUIContent("采集与分包规则"), true);
            EditorGUILayout.PropertyField(_profile.FindProperty("ExtractSharedDependencies"), new GUIContent("归并共享依赖"));
            EditorGUILayout.PropertyField(_profile.FindProperty("Uncompressed"), new GUIContent("不压缩 AB"));
            EditorGUILayout.PropertyField(_profile.FindProperty("BuiltinTags"), new GUIContent("首包标签（空为全量）"), true);
            if (_profile.ApplyModifiedProperties())
            {
                RevAssetBuildProfile.instance.SaveProfile();
            }
            if (GUILayout.Button("预览分包"))
            {
                Run(() =>
                {
                    var plan = RevAssetCollector.Collect(RevAssetBuildProfile.instance, true);
                    _message = $"公开地址 {plan.Addresses.Count} 项，{plan.Bundles.Count} 个包，共享资产 {plan.SharedAssets} 项\n" +
                        string.Join("\n", plan.Bundles.Take(30).Select(bundle => $"{bundle.Key}  {bundle.Value.Assets.Count} 项"));
                });
            }
        }

        /// <summary>提供本机首包和可签名的频道发布，私钥只在本次面板会话中保留文件路径。</summary>
        private void DrawBuild()
        {
            EditorGUILayout.Space();
            _target = (BuildTarget)EditorGUILayout.EnumPopup("构建平台", _target);
            _version = EditorGUILayout.TextField("内容版本", _version);
            _revision = EditorGUILayout.LongField("发布序号", _revision);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("选择签名私钥"))
            {
                _privateKey = EditorUtility.OpenFilePanel("选择发布私钥", "", "xml");
            }
            if (GUILayout.Button("生成发布密钥"))
            {
                Run(CreateKeys);
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("构建并准备首包"))
            {
                Run(() => ShowBuild(RevAssetBuilder.Build(_target, _version, _revision, true)));
            }
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_privateKey)))
            {
                if (GUILayout.Button("构建并签名发布"))
                {
                    Run(() => ShowBuild(RevAssetBuilder.Build(_target, _version, _revision, false, signingKeyPath: _privateKey)));
                }
            }
        }

        /// <summary>保存私钥到项目资产外，并把同一把钥匙的公开部分写进项目。</summary>
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
            _message = "发布公钥已保存到项目配置";
        }

        /// <summary>显示产物位置和可以直接检查的文件数量。</summary>
        private void ShowBuild(RevAssetBuildResult result)
        {
            _message = $"{result.version}  {result.target}\n{result.bundles} 个包，{result.assets} 个地址，{result.bytes:N0} B\n{result.output}";
        }

        /// <summary>用一处错误反馈报告实际失败原因。</summary>
        private void Run(Action action)
        {
            _message = null;
            try
            {
                action();
            }
            catch (Exception error)
            {
                _message = error.Message;
            }
        }
    }
}
