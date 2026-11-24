using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Revolution.RevAsset
{
    /// <summary>已安装版本的提交记录。一个文件同时保存新旧版本，断电时只会看到完整的一代。</summary>
    [Serializable]
    internal sealed class RevAssetInstallState
    {
        /// <summary>启动时优先验证的已安装清单摘要。</summary>
        public string current;
        /// <summary>保留用于回退的完整版本。</summary>
        public string previous;
        /// <summary>已接受过的最高发布序号，显式回退后仍生效。</summary>
        public long highest;
        /// <summary>最高发布序号对应的清单摘要，防止发布时覆盖同一序号。</summary>
        public string highestCatalog;
    }

    /// <summary>分离 Unity 内置首包、可写内容缓存与安装指针。</summary>
    internal sealed class RevAssetStore
    {
        /// <summary>提交记录引用 Player 首包时使用的固定符号。</summary>
        private const string BuiltinMarker = "builtin";

        /// <summary>目录、签名和发布频道的项目配置。</summary>
        internal RevAssetSettings Settings { get; }

        /// <summary>当前执行环境对应的 Unity 构建目标。</summary>
        internal string Platform { get; }

        /// <summary>缓存按包、平台与兼容代号隔离。</summary>
        internal string Root { get; }

        /// <summary>装在 Player 里的首包目录。新版本可复用其中摘要一样的文件。</summary>
        internal RevAssetCatalog Builtin { get; }

        /// <summary>构建确认随包发布的内容摘要及长度。</summary>
        private readonly Dictionary<string, long> _builtinObjects = new Dictionary<string, long>(StringComparer.Ordinal);

        /// <summary>本代首包文件的路径和目标在构造时固定。</summary>
        private readonly string _builtinRoot;

        /// <summary>可靠提交当前与回退版本的位置。</summary>
        private string StatePath => Path.Combine(Root, "installed.json");

        /// <summary>校验设置并读取一次随包目录。测试可提供隔离的缓存位置。</summary>
        internal RevAssetStore(RevAssetSettings settings, string platform, RevAssetCatalog builtin, string root = null)
        {
            Settings = settings;
            Platform = platform;
            if (!RevAssetFiles.IsId(settings.Package) || !RevAssetFiles.IsId(settings.Compatibility) || !RevAssetFiles.IsId(platform))
            {
                throw new InvalidDataException("RevAsset 项目包名、平台或兼容代号无效");
            }

            string storageId = RevAssetFiles.HashBytes(RevAssetFiles.Utf8.GetBytes(
                settings.Package + "\n" + platform + "\n" + settings.Compatibility)).Substring(0, 32);
            Root = root ?? Path.Combine(Application.persistentDataPath, "RevAsset", storageId);
            Builtin = builtin;
#if UNITY_EDITOR
            _builtinRoot = RevAssetFiles.BuiltinEditorRoot(settings.Package, platform);
#else
            _builtinRoot = Application.streamingAssetsPath + "/RevAsset/" + settings.Package + "/" + platform;
#endif
            if (Builtin != null)
            {
                foreach (RevAssetBundleData bundle in Builtin.Bundles.Values)
                {
                    _builtinObjects[bundle.hash] = bundle.size;
                }
            }
        }

        /// <summary>构造本机缓存中的正式对象路径。</summary>
        internal string ObjectPath(string hash) => Path.Combine(Root, RevAssetFiles.ObjectName(hash));

        /// <summary>多个 Player 进程共享目录时只允许一个写入者，读者继续使用完整快照。</summary>
        internal FileStream LockMutation()
        {
            Directory.CreateDirectory(Root);
            return new FileStream(Path.Combine(Root, ".writer"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }

        /// <summary>从首包或已安装对象中找可复用的制品。构建部署先写内容，再提交目录。</summary>
        internal string Resolve(RevAssetBundleData bundle, bool verifyHash = false)
        {
            string cached = ObjectPath(bundle.hash);
            if (IsAvailable(cached, bundle, verifyHash))
            {
                return cached;
            }
            if (_builtinObjects.TryGetValue(bundle.hash, out long size) && size == bundle.size)
            {
                string builtinPath = _builtinRoot + "/" + RevAssetFiles.ObjectName(bundle.hash);
                if (builtinPath.Contains("://") || IsAvailable(builtinPath, bundle, verifyHash))
                {
                    return builtinPath;
                }
            }
            return null;
        }

        /// <summary>读取受信且完整的当前版本、前一版本或 Player 首包。</summary>
        internal RevAssetCatalog LoadInstalled()
        {
            RevAssetInstallState state = ReadState();
            return TryReadInstalled(state.current) ?? TryReadInstalled(state.previous) ?? MountPaths(Builtin);
        }

        /// <summary>给回执和防旧版本检查读取一次安装记录。损坏记录退回首包。</summary>
        internal RevAssetInstallState ReadState()
        {
            if (File.Exists(StatePath))
            {
                try
                {
                    var state = JsonUtility.FromJson<RevAssetInstallState>(
                        RevResMapCodec.DecodeText(RevAssetFiles.ReadBounded(StatePath, RevAssetRelease.MaxBytes)));
                    if (state != null && IsPointer(state.current) &&
                        (string.IsNullOrEmpty(state.previous) || IsPointer(state.previous)) &&
                        state.highest > 0 && RevAssetFiles.IsHash(state.highestCatalog))
                    {
                        return state;
                    }
                }
                catch (Exception error) when (error is IOException || error is ArgumentException)
                {
                    RevLog.Warn("RevAsset 安装记录读取失败，尝试内置资源：" + error.Message, "RevAsset");
                }
            }
            return new RevAssetInstallState();
        }

        /// <summary>判断候选是否符合首次随包版本、最近安装和单调发布约束。</summary>
        internal void CheckRevision(RevAssetRelease release, RevAssetInstallState state)
        {
            long floor = Math.Max(Builtin?.Revision ?? 0, state.highest);
            if (release.revision < floor || (release.revision == state.highest && release.catalog != state.highestCatalog))
            {
                throw new InvalidDataException("频道版本旧于已安装版本，或复用了已有发布序号");
            }
        }

        /// <summary>完整性检查后写入不可变目录及签名，最后一次原子写提交版本号。</summary>
        internal void Commit(RevAssetRelease release, byte[] pointer, byte[] catalogBytes, RevAssetCatalog catalog)
        {
            foreach (RevAssetBundleData bundle in catalog.Bundles.Values)
            {
                if (Resolve(bundle) == null)
                {
                    throw new InvalidDataException("提交版本时缺少包：" + bundle.name);
                }
            }

            RevAssetInstallState state = ReadState();
            CheckRevision(release, state);
            RevAssetFiles.WriteAtomic(Path.Combine(Root, RevAssetFiles.CatalogName(release.catalog)), catalogBytes);
            RevAssetFiles.WriteAtomic(ReleasePath(release.catalog), pointer);
            if (state.current != release.catalog)
            {
                // 修复坏版本时保留仍然可用的回退版本。
                state.previous = TryReadInstalled(state.current) != null ? state.current : state.previous;
                if (string.IsNullOrEmpty(state.previous) && Builtin != null)
                {
                    state.previous = BuiltinMarker;
                }
                state.current = release.catalog;
            }
            state.highest = release.revision;
            state.highestCatalog = release.catalog;
            WriteState(state);
        }

        /// <summary>验证保留版本仍完整后交换当前与前一版本。已经装配的生效快照仍在原代服务。</summary>
        internal RevAssetCatalog Rollback()
        {
            RevAssetInstallState state = ReadState();
            RevAssetCatalog previous = TryReadInstalled(state.previous);
            if (previous == null)
            {
                throw new InvalidOperationException("没有完整的回退版本");
            }
            string old = state.current;
            state.current = state.previous;
            state.previous = old;
            WriteState(state);
            return previous;
        }

        /// <summary>回收已结束安装中与首包、当前和前一版本都无关的缓存对象。</summary>
        internal long ClearUnused(RevAssetCatalog mounted)
        {
            var keep = new HashSet<string>(_builtinObjects.Keys, StringComparer.Ordinal);
            RevAssetInstallState state = ReadState();
            foreach (RevAssetCatalog item in new[] { mounted, TryReadInstalled(state.current), TryReadInstalled(state.previous) })
            {
                if (item == null)
                {
                    continue;
                }
                keep.UnionWith(item.Bundles.Values.Select(bundle => bundle.hash));
            }

            long removed = 0;
            string folder = Path.Combine(Root, "objects");
            if (!Directory.Exists(folder))
            {
                return removed;
            }
            foreach (string path in Directory.EnumerateFiles(folder, "*.bundle"))
            {
                string hash = Path.GetFileNameWithoutExtension(path);
                if (RevAssetFiles.IsHash(hash) && !keep.Contains(hash))
                {
                    long size = new FileInfo(path).Length;
                    File.Delete(path);
                    removed += size;
                }
            }
            return removed;
        }

        /// <summary>签名、地址索引与对象齐全才可从磁盘装配。</summary>
        private RevAssetCatalog TryReadInstalled(string hash)
        {
            if (hash == BuiltinMarker)
            {
                return MountPaths(Builtin);
            }
            if (!RevAssetFiles.IsHash(hash))
            {
                return null;
            }
            try
            {
                byte[] pointer = RevAssetFiles.ReadBounded(ReleasePath(hash), RevAssetRelease.MaxBytes);
                RevAssetRelease release = RevAssetRelease.Read(pointer, Settings, Platform);
                if (hash != release.catalog)
                {
                    return null;
                }
                var catalog = release.ReadCatalog(RevAssetFiles.ReadBounded(
                    Path.Combine(Root, RevAssetFiles.CatalogName(hash)), release.length));
                return catalog.Revision >= (Builtin?.Revision ?? 0) ? MountPaths(catalog) : null;
            }
            catch (Exception error) when (error is IOException || error is ArgumentException ||
                error is FormatException || error is System.Security.Cryptography.CryptographicException || error is NotSupportedException)
            {
                return null;
            }
        }

        /// <summary>已校验频道指针按清单摘要单独保存，可离线验证与回退。</summary>
        private string ReleasePath(string hash) => Path.Combine(Root, "catalogs", hash + ".release");

        /// <summary>确认全量包来源齐全，再把这代路径交给原生加载器复用。</summary>
        private RevAssetCatalog MountPaths(RevAssetCatalog catalog)
        {
            if (catalog == null)
            {
                return null;
            }
            foreach (RevAssetBundleData bundle in catalog.Bundles.Values)
            {
                string path = Resolve(bundle);
                if (path == null)
                {
                    return null;
                }
                bundle.mountedPath = path;
            }
            return catalog;
        }

        /// <summary>原子提交安装记录。版本安装和回退通过同一出口。</summary>
        private void WriteState(RevAssetInstallState state)
        {
            RevAssetFiles.WriteAtomic(StatePath, RevAssetFiles.Utf8.GetBytes(JsonUtility.ToJson(state)));
        }

        /// <summary>安装指针只能引用内置快照或已签名的清单摘要。</summary>
        private static bool IsPointer(string value) => value == BuiltinMarker || RevAssetFiles.IsHash(value);

        /// <summary>挂载只确认文件存在与长度。安装或修复时校验完整 SHA-256。</summary>
        private static bool IsAvailable(string path, RevAssetBundleData bundle, bool verifyHash)
        {
            if (verifyHash)
            {
                return RevAssetFiles.MatchesFile(path, bundle);
            }
            var file = new FileInfo(path);
            return file.Exists && file.Length == bundle.size;
        }
    }
}
