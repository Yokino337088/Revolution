using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Revolution.RevAsset
{
    /// <summary>可序列化的整版资源清单，构建端从 Unity 真实 AB 清单填充依赖。</summary>
    [Serializable]
    public sealed class RevAssetCatalogData
    {
        /// <summary>清单格式版本。</summary>
        public int format = 1;
        /// <summary>内容包名。</summary>
        public string package;
        /// <summary>Unity 构建目标名称，例如 StandaloneWindows64。</summary>
        public string platform;
        /// <summary>允许装配此清单的应用兼容代号。</summary>
        public string compatibility;
        /// <summary>给制品和用户看的内容版本名。</summary>
        public string version;
        /// <summary>发布序号，同一包和兼容代内递增。</summary>
        public long revision;
        /// <summary>本版本使用的包及其真实依赖。</summary>
        public RevAssetBundleData[] bundles;
        /// <summary>业务可以按逻辑名取用的资源地址。</summary>
        public RevAssetAddressData[] assets;
    }

    /// <summary>原生包的内容地址、Unity 完整性信息和分包元数据。</summary>
    [Serializable]
    public sealed class RevAssetBundleData
    {
        /// <summary>Unity 的包名，包含原生变体后缀。</summary>
        public string name;
        /// <summary>二进制文件的 SHA-256，用来定位制品并跨版本复用。</summary>
        public string hash;
        /// <summary>文件字节数，计算下载量并提前检查截断。</summary>
        public long size;
        /// <summary>Unity 引擎的 Hash128，供浏览器等平台请求原生缓存。</summary>
        public string unityHash;
        /// <summary>Unity 包 CRC，用于原生 URL 加载。</summary>
        public uint crc;
        /// <summary>Unity 构建结果中的直接依赖。</summary>
        public string[] dependencies;
        /// <summary>用于首包、预下载和发布治理的业务标签。</summary>
        public string[] tags;

        /// <summary>装配时确定的文件或 URL，当前代的包冷请求可以直接使用。</summary>
        [NonSerialized] internal string mountedPath;

        /// <summary>目录解析时一次性读取的 Unity 哈希。</summary>
        [NonSerialized] internal Hash128 parsedUnityHash;
    }

    /// <summary>业务逻辑路径与 Unity 包内实际地址的绑定。</summary>
    [Serializable]
    public sealed class RevAssetAddressData
    {
        /// <summary>业务路径，沿用 Revolution 逻辑路径规则。</summary>
        public string key;
        /// <summary>资源所属原生包。</summary>
        public string bundle;
        /// <summary>Unity 资产的完整包内路径。</summary>
        public string asset;
    }

    /// <summary>经格式、地址、平台和依赖图验证的只读运行时快照。</summary>
    internal sealed class RevAssetCatalog
    {
        /// <summary>解析前的传输和文件大小上限。</summary>
        internal const int MaxBytes = 32 * 1024 * 1024;

        /// <summary>已验证的内容版本。</summary>
        internal string Version { get; }

        /// <summary>已验证的发布序号。</summary>
        internal long Revision { get; }

        /// <summary>一张框架可直接查询的逻辑地址索引。</summary>
        internal RevResMap Map { get; }

        /// <summary>按精确包名保存一份依赖和制品元数据。</summary>
        internal IReadOnlyDictionary<string, RevAssetBundleData> Bundles { get; }

        /// <summary>真正用到的包才建立闭包，持有期间可供多个句柄共享。</summary>
        private readonly Dictionary<string, string[]> _closures = new Dictionary<string, string[]>(StringComparer.Ordinal);

        /// <summary>将已验证的地址和包信息交给新版运行时。序列化资产行由调用端释放。</summary>
        private RevAssetCatalog(RevAssetCatalogData data, Dictionary<string, RevAssetBundleData> bundles, RevResMap map)
        {
            Version = data.version;
            Revision = data.revision;
            Bundles = bundles;
            Map = map;
        }

        /// <summary>完整验证一份目录。所有包、地址和依赖在激活前就检查。</summary>
        internal static RevAssetCatalog Parse(byte[] bytes, string package, string platform, string compatibility)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaxBytes)
            {
                throw new InvalidDataException("资源清单缺失或超过上限");
            }
            var data = JsonUtility.FromJson<RevAssetCatalogData>(RevResMapCodec.DecodeText(bytes));
            if (data == null || data.format != 1 || data.package != package || data.platform != platform ||
                data.compatibility != compatibility || data.revision < 1 || !RevAssetFiles.IsId(data.version) ||
                data.bundles == null || data.assets == null || data.bundles.Length > 65536 || data.assets.Length > 500000)
            {
                throw new InvalidDataException("资源清单的格式、平台、包或应用兼容代号不匹配");
            }

            var bundles = new Dictionary<string, RevAssetBundleData>(data.bundles.Length, StringComparer.Ordinal);
            var objects = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (RevAssetBundleData bundle in data.bundles)
            {
                if (bundle == null || !RevAssetFiles.IsBundleName(bundle.name) || !RevAssetFiles.IsHash(bundle.hash) ||
                    !RevAssetFiles.IsHex(bundle.unityHash, 32) || bundle.size <= 0 ||
                    bundle.dependencies == null || bundle.tags == null || !bundles.TryAdd(bundle.name, bundle))
                {
                    throw new InvalidDataException("非法或重复的资源包：" + bundle?.name);
                }
                if (objects.TryGetValue(bundle.hash, out long size) && size != bundle.size)
                {
                    throw new InvalidDataException("同一摘要的包大小冲突：" + bundle.name);
                }
                objects[bundle.hash] = bundle.size;
                bundle.parsedUnityHash = Hash128.Parse(bundle.unityHash);
                if (bundle.tags.Any(tag => !RevAssetFiles.IsId(tag)))
                {
                    throw new InvalidDataException("包标签无效：" + bundle.name);
                }
            }
            ValidateGraph(bundles);
            RevResMap map = RevResMap.FromLocations(EnumerateLocations(data.assets, bundles));
            return new RevAssetCatalog(data, bundles, map);
        }

        /// <summary>按依赖的后序遍历建立闭包，供 Unity 先加载底层依赖。</summary>
        internal string[] GetDependencies(string name)
        {
            if (_closures.TryGetValue(name, out string[] result))
            {
                return result;
            }
            if (Bundles[name].dependencies.Length == 0)
            {
                return Array.Empty<string>();
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var ordered = new List<string>();
            var pending = new Stack<(string name, int next)>();
            pending.Push((name, 0));
            while (pending.Count > 0)
            {
                var item = pending.Pop();
                string[] direct = Bundles[item.name].dependencies;
                if (item.next == direct.Length)
                {
                    if (item.name != name)
                    {
                        ordered.Add(item.name);
                    }
                    continue;
                }
                string dependency = direct[item.next];
                pending.Push((item.name, item.next + 1));
                if (seen.Add(dependency))
                {
                    pending.Push((dependency, 0));
                }
            }
            result = ordered.ToArray();
            _closures.Add(name, result);
            return result;
        }

        /// <summary>挑出指定标签和依赖。空标签集代表整版资源。</summary>
        internal IEnumerable<RevAssetBundleData> SelectBundles(IReadOnlyCollection<string> tags)
        {
            if (tags == null || tags.Count == 0)
            {
                return Bundles.Values;
            }
            var wanted = new HashSet<string>(StringComparer.Ordinal);
            var requested = new HashSet<string>(tags, StringComparer.Ordinal);
            var matched = new HashSet<string>(StringComparer.Ordinal);
            foreach (RevAssetBundleData bundle in Bundles.Values)
            {
                foreach (string tag in bundle.tags)
                {
                    if (!requested.Contains(tag))
                    {
                        continue;
                    }
                    matched.Add(tag);
                    wanted.Add(bundle.name);
                    wanted.UnionWith(GetDependencies(bundle.name));
                }
            }
            if (matched.Count != requested.Count)
            {
                throw new InvalidDataException("清单缺少标签：" + string.Join(", ", requested.Except(matched)));
            }
            return wanted.OrderBy(name => name, StringComparer.Ordinal).Select(name => Bundles[name]);
        }

        /// <summary>边校验资源所属的包边把结构化地址交给框架索引。</summary>
        private static IEnumerable<(string path, string bundle, string asset)> EnumerateLocations(
            RevAssetAddressData[] assets, Dictionary<string, RevAssetBundleData> bundles)
        {
            foreach (RevAssetAddressData item in assets)
            {
                if (item == null || item.key == null || item.bundle == null || !bundles.ContainsKey(item.bundle) ||
                    item.key.StartsWith("Res/", StringComparison.Ordinal))
                {
                    throw new InvalidDataException("资源地址指向未知包或使用了保留前缀：" + item?.key);
                }
                yield return (item.key, item.bundle, item.asset);
            }
        }

        /// <summary>通过消除入度检测循环和缺失依赖，深链不会占用调用栈。</summary>
        private static void ValidateGraph(Dictionary<string, RevAssetBundleData> bundles)
        {
            var counts = bundles.Keys.ToDictionary(name => name, _ => 0, StringComparer.Ordinal);
            foreach (RevAssetBundleData bundle in bundles.Values)
            {
                var direct = new HashSet<string>(StringComparer.Ordinal);
                foreach (string dependency in bundle.dependencies)
                {
                    if (dependency == null || !counts.TryGetValue(dependency, out int count) || !direct.Add(dependency))
                    {
                        throw new InvalidDataException("资源依赖缺失或重复：" + bundle.name + " → " + dependency);
                    }
                    counts[dependency] = count + 1;
                }
            }
            var ready = new Queue<string>(counts.Where(entry => entry.Value == 0).Select(entry => entry.Key));
            int visited = 0;
            while (ready.Count > 0)
            {
                visited++;
                foreach (string dependency in bundles[ready.Dequeue()].dependencies)
                {
                    if (--counts[dependency] == 0)
                    {
                        ready.Enqueue(dependency);
                    }
                }
            }
            if (visited != bundles.Count)
            {
                throw new InvalidDataException("检测到原生资源包循环依赖：" +
                    string.Join(", ", counts.Where(entry => entry.Value != 0).Take(8).Select(entry => entry.Key)));
            }
        }
    }
}
