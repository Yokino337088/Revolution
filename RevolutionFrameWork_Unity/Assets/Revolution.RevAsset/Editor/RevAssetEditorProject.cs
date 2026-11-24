using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Revolution.RevAsset.Editor
{
    /// <summary>维护工程设置和模拟策略，把实际安装路径统一供界面和 CLI 使用。</summary>
    internal static class RevAssetEditorProject
    {
        /// <summary>后端选择由框架读取，删除扩展后该配置仍能恢复内置模式。</summary>
        internal const string ChoicePath = "Assets/Resources/Revolution/ResourceBackend.asset";

        /// <summary>RevAsset 的 Player 公开频道与公钥配置。</summary>
        internal const string SettingsPath = "Assets/Resources/Revolution/RevAsset/Settings.asset";

        /// <summary>明确选择后才创建设置，安装扩展时不会改动工程。</summary>
        internal static RevAssetSettings GetSettings(bool create)
        {
            return GetAsset<RevAssetSettings>(SettingsPath, create);
        }

        /// <summary>读取当前工程选择，用于编辑器预览和出包处理。</summary>
        internal static bool Selected
        {
            get
            {
                var selection = GetAsset<RevResourceBackendSettings>(ChoicePath, false);
                return selection != null && selection.BackendId == RevAsset.BackendId;
            }
        }

        /// <summary>保存下次 Play 与 Player 使用的资源后端，不中断正在运行的资源请求。</summary>
        internal static void Select(bool enabled, bool simulation)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("退出 Play 后再保存资源后端选择");
            }
            var value = GetAsset<RevResourceBackendSettings>(ChoicePath, enabled);
            if (value == null)
            {
                return;
            }
            if (enabled)
            {
                RevAssetSettings settings = GetSettings(true);
                settings.EditorSimulation = simulation;
                EditorUtility.SetDirty(settings);
            }
            value.BackendId = enabled ? RevAsset.BackendId : "";
            EditorUtility.SetDirty(value);
            AssetDatabase.SaveAssets();
        }

        /// <summary>首包目录文件是固定平台名。Player 实际文件由构建处理器单独注入。</summary>
        internal static string CatalogAsset(string platform)
        {
            return "Assets/Resources/" + RevAsset.CatalogResourceRoot + platform + ".bytes";
        }

        /// <summary>复用工程现有设定，显式创建时保留原路径和 GUID。</summary>
        private static T GetAsset<T>(string path, bool create) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null && create)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                AssetDatabase.Refresh();
                asset = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, path);
                AssetDatabase.SaveAssets();
            }
            return asset;
        }

        /// <summary>用同一份采集计划给 Unity 编辑器直读策略提供逻辑地址。</summary>
        [InitializeOnLoadMethod]
        private static void RegisterSimulation()
        {
            RevAsset.EditorSimulationPolicy = () => new SimulationPolicy(RevAssetCollector.Collect(RevAssetBuildProfile.instance, false));
        }

        /// <summary>编辑器直读只定位到已采集的公开资源，实际加载由现有加载器完成。</summary>
        private sealed class SimulationPolicy : IRevResPolicy
        {
            /// <summary>已采集到的地址索引。</summary>
            private readonly System.Collections.Generic.Dictionary<string, string> _paths;

            /// <summary>复用框架原生 AssetDatabase 加载器。</summary>
            private readonly RevEditorLoader _loader = new RevEditorLoader();

            /// <summary>将公开地址转为一张模拟路径索引。</summary>
            internal SimulationPolicy(RevAssetBuildPlan plan)
            {
                _paths = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
                foreach (RevAssetAddressData address in plan.Addresses)
                {
                    _paths.Add(address.key, address.asset);
                }
            }

            /// <summary>保留显式 Resources 地址。</summary>
            public bool Match(string path) => !path.StartsWith("Res/", StringComparison.Ordinal);

            /// <summary>失败时交由框架的兜底策略处理。</summary>
            public bool AllowFallback => true;

            /// <summary>将公开逻辑名定位到工程真实资产路径。</summary>
            public string MapPath(string path, Type contentType) => _paths.TryGetValue(path, out string found) ? found : null;

            /// <summary>返回当前策略共享的编辑器加载器。</summary>
            public IRevResLoader CreateLoader() => _loader;
        }
    }
}
