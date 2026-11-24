using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace Revolution.RevAsset.Editor
{
    /// <summary>从采集规则生成的原生 AB 计划。构建、可视预览和模拟加载共用此结果。</summary>
    internal sealed class RevAssetBuildPlan
    {
        /// <summary>逻辑地址与包归属。共享依赖包的内部资产不额外暴露业务地址。</summary>
        internal readonly List<RevAssetAddressData> Addresses = new List<RevAssetAddressData>();

        /// <summary>每个原生包只登记一份归属、标签和资源数组。</summary>
        internal readonly SortedDictionary<string, RevAssetBundlePlan> Bundles =
            new SortedDictionary<string, RevAssetBundlePlan>(StringComparer.Ordinal);

        /// <summary>从隐式引用提升为明确归属的共享资产数。</summary>
        internal int SharedAssets;

        /// <summary>把计划转成 Unity 原生构建结构。文件的完整路径是其包内地址。</summary>
        internal AssetBundleBuild[] NativeBuilds()
        {
            return Bundles.Values.Select(bundle => new AssetBundleBuild
            {
                assetBundleName = bundle.Name,
                assetBundleVariant = bundle.Variant,
                assetNames = bundle.Assets.OrderBy(path => path, StringComparer.Ordinal).ToArray()
            }).ToArray();
        }
    }

    /// <summary>构建阶段的单个原生包定义。</summary>
    internal sealed class RevAssetBundlePlan
    {
        /// <summary>Unity 包名的基础部分。</summary>
        internal string Name;

        /// <summary>Unity 分开传入的变体。</summary>
        internal string Variant;

        /// <summary>本包包含场景时，需要保持与普通资源分开。</summary>
        internal bool Scenes;

        /// <summary>有明确归属的资产路径。</summary>
        internal readonly List<string> Assets = new List<string>();

        /// <summary>包内业务资源继承的标签。</summary>
        internal readonly SortedSet<string> Tags = new SortedSet<string>(StringComparer.Ordinal);
    }

    /// <summary>规则采集和共享依赖归属。只读 Unity 资产配置，不改 AssetImporter 与 GUID。</summary>
    internal static class RevAssetCollector
    {
        /// <summary>扫描确定的路径集合。预览与构建展开共享依赖，模拟模式只需要公开地址。</summary>
        internal static RevAssetBuildPlan Collect(RevAssetBuildProfile profile, bool withShared)
        {
            string root = Normalize(profile.AddressRoot);
            var rules = new Dictionary<string, RevAssetBuildRule>(StringComparer.Ordinal);
            foreach (RevAssetBuildRule rule in profile.Rules)
            {
                if (rule == null || !rules.TryAdd(Normalize(rule.Path), rule))
                {
                    throw new InvalidDataException("分包规则的资源路径缺失或重复：" + rule?.Path);
                }
            }
            var sources = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var entry in rules)
            {
                if (entry.Value.Packing == RevAssetPacking.Exclude)
                {
                    continue;
                }
                if (AssetDatabase.IsValidFolder(entry.Key))
                {
                    foreach (string guid in AssetDatabase.FindAssets("", new[] { entry.Key }))
                    {
                        sources.Add(AssetDatabase.GUIDToAssetPath(guid));
                    }
                }
                else if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(entry.Key)))
                {
                    sources.Add(entry.Key);
                }
                else
                {
                    throw new InvalidDataException("分包规则找不到资产：" + entry.Key);
                }
            }

            var addresses = new HashSet<string>(StringComparer.Ordinal);
            var plan = new RevAssetBuildPlan();
            foreach (string path in sources)
            {
                if (!IsPackable(path))
                {
                    continue;
                }
                string selector = FindRule(rules, path);
                RevAssetBuildRule rule = rules[selector];
                if (rule.Packing == RevAssetPacking.Exclude)
                {
                    continue;
                }
                bool scene = path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase);
                var native = BundleFor(path, rule, scene);
                string full = Register(plan, native, path, scene, rule.Tags);
                string key = AddressFor(root, selector, rule, path);
                if (!addresses.Add(key))
                {
                    throw new InvalidDataException("多个资源使用同一逻辑地址，请设置文件级地址：" + key);
                }
                plan.Addresses.Add(new RevAssetAddressData { key = key, bundle = full, asset = path });
            }
            if (plan.Addresses.Count == 0)
            {
                throw new InvalidDataException("采集结果为空。请在 RevAsset 面板配置有效资源目录或文件");
            }
            // 在真正出包前验证业务地址，避免构建遍历后才发现非法键。
            RevResMap.FromLocations(plan.Addresses.Select(row => (row.key, row.bundle, row.asset)));

            if (withShared && profile.ExtractSharedDependencies)
            {
                ExtractShared(plan);
            }
            return plan;
        }

        /// <summary>把资产下沉到最具体的文件或目录规则，规则重排不会改变归属。</summary>
        private static string FindRule(Dictionary<string, RevAssetBuildRule> rules, string path)
        {
            string candidate = path;
            while (!rules.ContainsKey(candidate))
            {
                int separator = candidate.LastIndexOf('/');
                if (separator < 0)
                {
                    throw new InvalidDataException("找不到已采集资产的分包归属：" + path);
                }
                candidate = candidate.Substring(0, separator);
            }
            return candidate;
        }

        /// <summary>计算逻辑路径。具体文件可以直接指定已有业务地址。</summary>
        private static string AddressFor(string root, string selector, RevAssetBuildRule rule, string path)
        {
            if (!string.IsNullOrEmpty(rule.Address))
            {
                if (path == selector)
                {
                    return rule.Address;
                }
                return rule.Address.TrimEnd('/') + "/" + WithoutExtension(path.Substring(selector.Length + 1));
            }
            string relative = path.StartsWith(root + "/", StringComparison.Ordinal) ? path.Substring(root.Length + 1) : path.Substring(7);
            return WithoutExtension(relative);
        }

        /// <summary>取原生标签或根据规则计算稳定包名。自动包用 Unity GUID 避免同名目录冲突。</summary>
        private static (string name, string variant) BundleFor(string path, RevAssetBuildRule rule, bool scene)
        {
            if (rule.Packing == RevAssetPacking.Together)
            {
                if (string.IsNullOrEmpty(rule.Bundle))
                {
                    throw new InvalidDataException("合包规则需要包名：" + rule.Path);
                }
                return (rule.Bundle, rule.Variant ?? string.Empty);
            }
            if (rule.Packing == RevAssetPacking.NativeOrFolder)
            {
                string current = path;
                while (current.StartsWith("Assets/", StringComparison.Ordinal))
                {
                    AssetImporter importer = AssetImporter.GetAtPath(current);
                    if (importer != null && !string.IsNullOrEmpty(importer.assetBundleName))
                    {
                        return (importer.assetBundleName, importer.assetBundleVariant ?? string.Empty);
                    }
                    current = current.Substring(0, current.LastIndexOf('/'));
                }
            }

            string owner = rule.Packing == RevAssetPacking.ByAsset ? path : path.Substring(0, path.LastIndexOf('/'));
            string prefix = scene ? "scenes/" : "content/";
            return (prefix + AssetDatabase.AssetPathToGUID(owner), string.Empty);
        }

        /// <summary>登记原生包名、变体和资产。场景包与普通素材包按 Unity 要求隔离。</summary>
        private static string Register(RevAssetBuildPlan plan, (string name, string variant) native, string path,
            bool scene, IEnumerable<string> tags)
        {
            string full = native.variant.Length == 0 ? native.name : native.name + "." + native.variant;
            if (!RevAssetFiles.IsBundleName(full) || !RevAssetFiles.IsBundleName(native.name) ||
                (native.variant.Length > 0 && (!RevAssetFiles.IsId(native.variant) || native.variant.Contains("/"))))
            {
                throw new InvalidDataException("原生包名或变体无效：" + full);
            }
            if (!plan.Bundles.TryGetValue(full, out RevAssetBundlePlan bundle))
            {
                bundle = new RevAssetBundlePlan { Name = native.name, Variant = native.variant, Scenes = scene };
                plan.Bundles.Add(full, bundle);
            }
            if (bundle.Scenes != scene || bundle.Name != native.name || bundle.Variant != native.variant)
            {
                throw new InvalidDataException("原生包存在场景混装或变体名冲突：" + full);
            }
            bundle.Assets.Add(path);
            foreach (string tag in tags ?? Array.Empty<string>())
            {
                if (!RevAssetFiles.IsId(tag))
                {
                    throw new InvalidDataException("分包标签无效：" + tag);
                }
                bundle.Tags.Add(tag);
            }
            return full;
        }

        /// <summary>把两组以上共享的隐式依赖按实际消费者集合归并，避免所有业务互相拉包。</summary>
        private static void ExtractShared(RevAssetBuildPlan plan)
        {
            var explicitAssets = new HashSet<string>(plan.Addresses.Select(row => row.asset), StringComparer.Ordinal);
            var users = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var bundle in plan.Bundles)
            {
                foreach (string path in AssetDatabase.GetDependencies(bundle.Value.Assets.ToArray(), true))
                {
                    if (!explicitAssets.Contains(path) && IsPackable(path) &&
                        !path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!users.TryGetValue(path, out List<string> names))
                        {
                            names = new List<string>();
                            users.Add(path, names);
                        }
                        names.Add(bundle.Key);
                    }
                }
            }

            foreach (var dependency in users)
            {
                if (dependency.Value.Count > 1)
                {
                    string group = RevAssetFiles.HashBytes(RevAssetFiles.Utf8.GetBytes(string.Join("\n", dependency.Value)));
                    string name = "shared/" + group.Substring(0, 24);
                    Register(plan, (name, string.Empty), dependency.Key, false, Array.Empty<string>());
                    plan.SharedAssets++;
                }
            }
        }

        /// <summary>只采集真正可序列化为 Unity 内容的文件，避免把脚本和构建入口当资产。</summary>
        private static bool IsPackable(string path)
        {
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || AssetDatabase.IsValidFolder(path) ||
                path.Contains("/Editor/") || path.Contains("/Resources/") || path.StartsWith("Assets/StreamingAssets/", StringComparison.Ordinal))
            {
                return false;
            }
            Type type = AssetDatabase.GetMainAssetTypeAtPath(path);
            string extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
            return type != null && type != typeof(MonoScript) && extension != ".asmdef" && extension != ".asmref" &&
                extension != ".dll" && extension != ".rsp";
        }

        /// <summary>规则路径一律位于 Assets 内，拒绝跳转和空段。</summary>
        private static string Normalize(string path)
        {
            string value = (path ?? string.Empty).Replace('\\', '/').TrimEnd('/');
            if (!value.StartsWith("Assets/", StringComparison.Ordinal) && value != "Assets" ||
                value.Split('/').Any(segment => segment.Length == 0 || segment == "." || segment == ".."))
            {
                throw new InvalidDataException("采集路径需位于 Assets 下：" + path);
            }
            return value;
        }

        /// <summary>逻辑路径默认去掉原资产扩展名。</summary>
        private static string WithoutExtension(string path)
        {
            return path.Substring(0, path.Length - System.IO.Path.GetExtension(path).Length);
        }
    }
}
