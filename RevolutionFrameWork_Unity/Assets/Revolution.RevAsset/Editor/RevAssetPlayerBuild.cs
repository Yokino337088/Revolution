using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.UnityLinker;

namespace Revolution.RevAsset.Editor
{
    /// <summary>把目标平台的首包注入原生 Player 构建，并向链接器提供 AB 使用的脚本。</summary>
    public sealed class RevAssetPlayerBuild : BuildPlayerProcessor, IUnityLinkerProcessor
    {
        /// <summary>随 Unity 的标准构建回调运行。</summary>
        public override int callbackOrder => 0;

        /// <summary>首包目录构建成功后才允许发布启用了 RevAsset 的 Player。</summary>
        public override void PrepareForBuild(BuildPlayerContext context)
        {
            if (!RevAssetEditorProject.Selected)
            {
                return;
            }

            string target = context.BuildPlayerOptions.target.ToString();
            RevAssetSettings settings = RevAssetEditorProject.GetSettings(false);
            if (settings == null)
            {
                throw new BuildFailedException("RevAsset 缺少项目配置");
            }
            string catalogPath = RevAssetEditorProject.CatalogAsset(target);
            byte[] bytes = RevAssetFiles.ReadBounded(catalogPath, RevAssetCatalog.MaxBytes);
            RevAssetCatalog catalog = RevAssetCatalog.Parse(bytes, settings.Package, target, settings.Compatibility);
            string root = RevAssetFiles.BuiltinEditorRoot(settings.Package, target);
            foreach (var group in catalog.Bundles.Values.GroupBy(bundle => bundle.hash, StringComparer.Ordinal))
            {
                RevAssetBundleData bundle = group.First();
                string name = RevAssetFiles.ObjectName(bundle.hash);
                string source = Path.Combine(root, name);
                if (!RevAssetFiles.MatchesFile(source, bundle))
                {
                    throw new BuildFailedException("RevAsset 首包缺失或已损坏，请重新为目标构建：" + target + " / " + bundle.name);
                }
                context.AddAdditionalPathToStreamingAssets(source, "RevAsset/" + settings.Package + "/" + target + "/" + name);
            }
        }

        /// <summary>将本项目的 AB 脚本保留规则提供给 UnityLinker。</summary>
        public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
        {
            if (!RevAssetEditorProject.Selected)
            {
                return null;
            }
            RevAssetSettings settings = RevAssetEditorProject.GetSettings(false);
            string path = Path.Combine(RevAssetFiles.BuiltinEditorRoot(settings.Package, report.summary.platform.ToString()), "link.xml");
            if (!File.Exists(path))
            {
                throw new BuildFailedException("RevAsset 未生成目标平台的脚本保留清单，请重新构建首包");
            }
            return path;
        }

        /// <summary>构建准备阶段已经提供静态保留清单。</summary>
        public void OnBeforeRun(BuildReport report, UnityLinkerBuildPipelineData data) { }

        /// <summary>UnityLinker 读取缓存中的清单，无须修改项目配置。</summary>
        public void OnAfterRun(BuildReport report, UnityLinkerBuildPipelineData data) { }
    }
}
