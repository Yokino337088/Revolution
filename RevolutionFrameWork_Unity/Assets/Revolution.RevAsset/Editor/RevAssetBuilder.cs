using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;
using UnityEditor;
using UnityEngine;

namespace Revolution.RevAsset.Editor
{
    /// <summary>资源构建回执。数值来自 Unity 实际产物及首包依赖闭包。</summary>
    [Serializable]
    public sealed class RevAssetBuildResult
    {
        /// <summary>频道发布目录。</summary>
        public string output;
        /// <summary>构建目标平台。</summary>
        public string target;
        /// <summary>内容版本名。</summary>
        public string version;
        /// <summary>发布序号。</summary>
        public long revision;
        /// <summary>原生包数。</summary>
        public int bundles;
        /// <summary>公开逻辑地址数。</summary>
        public int assets;
        /// <summary>被归并的共享资产数。</summary>
        public int sharedAssets;
        /// <summary>本次被引用的唯一制品总字节。</summary>
        public long bytes;
        /// <summary>在同一发布目录下已存在、经验证的制品。</summary>
        public int reusedObjects;
        /// <summary>是否生成有签名的频道入口。</summary>
        public bool signed;
        /// <summary>是否准备了可随 Player 一起分发的首包。</summary>
        public bool builtin;
        /// <summary>首包唯一制品数。</summary>
        public int builtinObjects;
        /// <summary>首包唯一制品字节数。</summary>
        public long builtinBytes;
    }

    /// <summary>使用 Unity 原生构建 API 打包并发布可重复使用的内容地址。</summary>
    public static class RevAssetBuilder
    {
        /// <summary>完整构建名固定在每个目标的缓存目录，供 Unity 做原生增量构建。</summary>
        private const string NativeFolder = "native";

        /// <summary>供面板、RevCLI 和定制发布脚本调用的正式构建入口。</summary>
        public static RevAssetBuildResult Build(BuildTarget target, string version, long revision, bool builtin,
            string output = null, string signingKeyPath = null)
        {
            return BuildProfile(RevAssetBuildProfile.instance, RevAssetEditorProject.GetSettings(true),
                target, version, revision, builtin, output, signingKeyPath);
        }

        /// <summary>验证配置并执行同一份采集、打包和发布流水线。</summary>
        internal static RevAssetBuildResult BuildProfile(RevAssetBuildProfile profile, RevAssetSettings settings,
            BuildTarget target, string version, long revision, bool builtin, string output, string signingKeyPath)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || target == BuildTarget.NoTarget ||
                !RevAssetFiles.IsId(settings.Package) || !RevAssetFiles.IsId(settings.Compatibility) ||
                !RevAssetFiles.IsId(version) || revision < 1)
            {
                throw new InvalidOperationException("请在编辑态设置有效的目标、内容版本、包名和递增发布序号");
            }
            if (!BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(target), target))
            {
                throw new InvalidOperationException("Unity 未安装目标构建模块：" + target);
            }
            string platform = target.ToString();
            string root = Path.GetFullPath(output ?? Path.Combine("Library", "RevAsset", "Publish", settings.Package, platform));
            if (root.StartsWith(Path.GetFullPath("Assets") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(root, Path.GetFullPath("Assets"), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("频道发布目录须在 Assets 之外");
            }
            Directory.CreateDirectory(root);

            using (RSA signingKey = ReadPrivateKey(signingKeyPath))
            {
                string cache = Path.GetFullPath(Path.Combine("Library", "RevAsset", "Work", settings.Package, platform));
                string native = Path.Combine(cache, NativeFolder);
                Directory.CreateDirectory(native);
                RevAssetBuildPlan plan = RevAssetCollector.Collect(profile, true);
                BuildAssetBundleOptions options = BuildAssetBundleOptions.StrictMode |
                    (profile.Uncompressed ? BuildAssetBundleOptions.UncompressedAssetBundle : BuildAssetBundleOptions.ChunkBasedCompression);
                AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(native, plan.NativeBuilds(), options, target);
                if (manifest == null)
                {
                    throw new InvalidOperationException("Unity AB 构建失败，请查 Editor Console");
                }

                RevAssetCatalogData data = Describe(settings, platform, version, revision, manifest, native, plan);
                byte[] bytes = Encode(data);
                RevAssetCatalog catalog = RevAssetCatalog.Parse(bytes, settings.Package, platform, settings.Compatibility);
                var release = new RevAssetRelease
                {
                    package = settings.Package, platform = platform, compatibility = settings.Compatibility,
                    version = version, revision = revision, catalog = RevAssetFiles.HashBytes(bytes), length = bytes.Length
                };
                if (signingKey != null)
                {
                    release.Sign(signingKey);
                    RevAssetRelease.Read(Encode(release), settings, platform);
                    EnsureChannelCanAdvance(root, release, settings, platform);
                }

                var result = new RevAssetBuildResult
                {
                    output = root, target = platform, version = version, revision = revision, signed = signingKey != null,
                    bundles = catalog.Bundles.Count, assets = catalog.Map.Count, sharedAssets = plan.SharedAssets, builtin = builtin
                };
                PublishObjects(root, native, data.bundles, result, false);
                RevAssetFiles.WriteAtomic(Path.Combine(root, RevAssetFiles.CatalogName(release.catalog)), bytes);
                if (builtin)
                {
                    PrepareBuiltin(profile, plan, settings, data, catalog, root, result);
                }
                // 指针放在最后。发布失败时旧入口仍指向原来的不可变清单与对象。
                if (result.signed)
                {
                    RevAssetFiles.WriteAtomic(Path.Combine(root, RevAssetRelease.FileName), Encode(release));
                }
                return result;
            }
        }

        /// <summary>从 Unity 原生产物计算内容摘要并记录实际依赖和原生验证信息。</summary>
        private static RevAssetCatalogData Describe(RevAssetSettings settings, string platform, string version, long revision,
            AssetBundleManifest manifest, string native, RevAssetBuildPlan plan)
        {
            var bundles = new List<RevAssetBundleData>();
            foreach (string name in manifest.GetAllAssetBundles().OrderBy(value => value, StringComparer.Ordinal))
            {
                string path = Path.Combine(native, name);
                if (!plan.Bundles.TryGetValue(name, out RevAssetBundlePlan group) ||
                    !BuildPipeline.GetCRCForAssetBundle(path, out uint crc))
                {
                    throw new InvalidDataException("Unity 包产物缺失或与分包计划不一致：" + name);
                }
                bundles.Add(new RevAssetBundleData
                {
                    name = name, hash = RevAssetFiles.HashFile(path), size = new FileInfo(path).Length,
                    unityHash = manifest.GetAssetBundleHash(name).ToString(), crc = crc,
                    dependencies = manifest.GetDirectDependencies(name).OrderBy(dep => dep, StringComparer.Ordinal).ToArray(),
                    tags = group.Tags.ToArray()
                });
            }
            if (bundles.Count != plan.Bundles.Count)
            {
                throw new InvalidDataException("Unity 原生结果未覆盖分包计划中的所有包");
            }
            return new RevAssetCatalogData
            {
                package = settings.Package, platform = platform, compatibility = settings.Compatibility, version = version,
                revision = revision, bundles = bundles.ToArray(), assets = plan.Addresses.OrderBy(row => row.key, StringComparer.Ordinal).ToArray()
            };
        }

        /// <summary>生成所选标签的依赖闭包与实际首包文件。首包由构建处理器只注入目标平台。</summary>
        private static void PrepareBuiltin(RevAssetBuildProfile profile, RevAssetBuildPlan plan, RevAssetSettings settings,
            RevAssetCatalogData data, RevAssetCatalog catalog, string published, RevAssetBuildResult result)
        {
            var selected = catalog.SelectBundles(profile.BuiltinTags).ToArray();
            var names = new HashSet<string>(selected.Select(bundle => bundle.name), StringComparer.Ordinal);
            string root = RevAssetFiles.BuiltinEditorRoot(settings.Package, data.platform);
            Directory.CreateDirectory(root);
            PublishObjects(root, published, selected, result, true);
            data.bundles = selected;
            data.assets = data.assets.Where(row => names.Contains(row.bundle)).ToArray();
            byte[] bytes = Encode(data);
            RevAssetCatalog.Parse(bytes, settings.Package, data.platform, settings.Compatibility);
            WriteLinkFile(root, plan);
            string catalogPath = RevAssetEditorProject.CatalogAsset(data.platform);
            RevAssetFiles.WriteAtomic(Path.GetFullPath(catalogPath), bytes);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        /// <summary>从原生产物或发布仓复制唯一对象，写后复验，避免同名对象被半写入。</summary>
        private static void PublishObjects(string root, string source, IEnumerable<RevAssetBundleData> bundles,
            RevAssetBuildResult result, bool builtin)
        {
            foreach (var bundle in bundles.GroupBy(item => item.hash, StringComparer.Ordinal).Select(group => group.First()))
            {
                string relative = RevAssetFiles.ObjectName(bundle.hash);
                string path = Path.Combine(root, relative);
                bool reused = RevAssetFiles.MatchesFile(path, bundle);
                if (!reused)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    string input = Path.Combine(source, builtin ? relative : bundle.name);
                    string temp = RevAssetFiles.TemporarySibling(path);
                    try
                    {
                        File.Copy(input, temp);
                        if (!RevAssetFiles.MatchesFile(temp, bundle))
                        {
                            throw new InvalidDataException("复制过程中资源内容发生变化：" + bundle.name);
                        }
                        RevAssetFiles.PublishFile(temp, path);
                    }
                    finally
                    {
                        File.Delete(temp);
                    }
                }
                if (builtin)
                {
                    result.builtinBytes += bundle.size;
                    result.builtinObjects++;
                }
                else
                {
                    result.bytes += bundle.size;
                    result.reusedObjects += reused ? 1 : 0;
                }
            }
        }

        /// <summary>确保同一发布目录的频道序号单调递增，同序号允许复验同一份内容。</summary>
        private static void EnsureChannelCanAdvance(string root, RevAssetRelease candidate, RevAssetSettings settings, string target)
        {
            string path = Path.Combine(root, RevAssetRelease.FileName);
            if (!File.Exists(path))
            {
                return;
            }
            RevAssetRelease previous = RevAssetRelease.Read(RevAssetFiles.ReadBounded(path, RevAssetRelease.MaxBytes), settings, target);
            if (candidate.revision < previous.revision ||
                (candidate.revision == previous.revision && candidate.catalog != previous.catalog))
            {
                throw new InvalidOperationException("发布目录含同序号不同内容或更新的版本，请提高发布序号");
            }
        }

        /// <summary>保护只由 AB 的预制体、配置或场景引用的脚本类型，供 UnityLinker 使用。</summary>
        private static void WriteLinkFile(string root, RevAssetBuildPlan plan)
        {
            string[] sources = plan.Bundles.Values.SelectMany(bundle => bundle.Assets).Distinct().ToArray();
            var scripts = AssetDatabase.GetDependencies(sources, true)
                .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                .Select(path => AssetDatabase.LoadAssetAtPath<MonoScript>(path)?.GetClass())
                .Where(type => type != null && type.FullName != null).Distinct().ToArray();
            var document = new XElement("linker", scripts.GroupBy(type => type.Assembly.GetName().Name)
                .OrderBy(group => group.Key, StringComparer.Ordinal).Select(group =>
                    new XElement("assembly", new XAttribute("fullname", group.Key),
                        group.OrderBy(type => type.FullName, StringComparer.Ordinal).Select(type =>
                            new XElement("type", new XAttribute("fullname", type.FullName.Replace('+', '/')),
                                new XAttribute("preserve", "all"))))));
            RevAssetFiles.WriteAtomic(Path.Combine(root, "link.xml"), RevAssetFiles.Utf8.GetBytes(document.ToString()));
        }

        /// <summary>从构建机读取 RSA XML 私钥。默认本机构建仅生成内置资源与不可变制品。</summary>
        private static RSA ReadPrivateKey(string file)
        {
            if (string.IsNullOrWhiteSpace(file))
            {
                return null;
            }
            string path = Path.GetFullPath(file);
            if (path.StartsWith(Path.GetFullPath("Assets") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(Path.GetFullPath("Packages") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("发布私钥必须放在项目资产和包目录之外");
            }
            RSA key = RSA.Create();
            try
            {
                key.FromXmlString(RevAssetFiles.Utf8.GetString(RevAssetFiles.ReadBounded(path, RevAssetRelease.MaxBytes)));
                return key;
            }
            catch
            {
                key.Dispose();
                throw;
            }
        }

        /// <summary>使用同一编码保存目录、链接和提交文件。</summary>
        private static byte[] Encode(object data) => RevAssetFiles.Utf8.GetBytes(JsonUtility.ToJson(data));
    }
}
