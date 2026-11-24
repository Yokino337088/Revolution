using System;
using UnityEngine;

namespace Revolution.RevAsset
{
    /// <summary>RevAsset 的版本和更新入口。资源加载继续使用 Revolution 的公开资源门面。</summary>
    public static class RevAsset
    {
        /// <summary>供项目设置和框架后端注册使用的固定标识。</summary>
        public const string BackendId = "revasset";

        /// <summary>首包清单的 Resources 目录，按实际运行平台分文件。</summary>
        internal const string CatalogResourceRoot = "Revolution/RevAsset/Builtin/";

        /// <summary>当前装配的内容。可独立于已经安装、等待生效的下一版。</summary>
        private static RevAssetCatalog _mounted;

        /// <summary>本局唯一的下载或安装任务的取消源。</summary>
        private static RevCancellationTokenSource _operation;

#if UNITY_EDITOR
        /// <summary>由 RevAsset 编辑器程序提供，用真实采集规则模拟逻辑地址。</summary>
        internal static Func<IRevResPolicy> EditorSimulationPolicy;
#endif

        /// <summary>当前逻辑地址实际命中的资源版本。模拟模式显示 editor。</summary>
        public static string MountedVersion => RevResourceBackends.ActiveId == BackendId ? _mounted?.Version ?? "editor" : null;

        /// <summary>正在运行或最近完成的一次更新回执。界面可按需读取而不必注册每帧任务。</summary>
        public static RevAssetUpdateResult LastUpdate { get; private set; }

        /// <summary>一个项目同一时间只提交一个资源版本。</summary>
        public static bool Updating => _operation != null;

        /// <summary>场景和脚本初始化前登记后端。与关闭域重载的 Play 循环兼容。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        public static void Register()
        {
            RevResourceBackends.Register(BackendId, CreateBinding);
        }

        /// <summary>退出上一轮后，停止旧下载并释放其静态引用。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            _operation?.Cancel();
            _operation = null;
            _mounted = null;
            LastUpdate = null;
        }

        /// <summary>项目启用自动检查后，在首场景后台准备已签名的下一版。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartAutomaticUpdate()
        {
            if (RevResourceBackends.ActiveId != BackendId || _mounted == null)
            {
                return;
            }
            var settings = Resources.Load<RevAssetSettings>(RevAssetSettings.ResourcePath);
            if (settings != null && settings.CheckOnStart && !string.IsNullOrWhiteSpace(settings.ChannelUrl))
            {
                UpdateAsync().Forget();
            }
        }

        /// <summary>按项目频道或显式目录下载完整版本，验证后提交为下次装配的版本。</summary>
        public static RevTask<RevAssetUpdateResult> UpdateAsync(string channel = null, int parallel = 4, int attempts = 3)
        {
            return RunAsync(channel, null, true, parallel, attempts);
        }

        /// <summary>把标签对应的内容和依赖下载进缓存，可被随后全量安装复用。</summary>
        public static RevTask<RevAssetUpdateResult> PrefetchAsync(string[] tags, string channel = null, int parallel = 4)
        {
            if (tags == null || tags.Length == 0)
            {
                throw new ArgumentException("预下载需要至少一个标签", nameof(tags));
            }
            return RunAsync(channel, (string[])tags.Clone(), false, parallel, 3);
        }

        /// <summary>请求取消当前任务，已校验的对象和在途部分文件可供重试使用。</summary>
        public static void CancelUpdate()
        {
            _operation?.Cancel();
        }

        /// <summary>退回上一个完整安装版本。需要在空闲交接点或下次启动重新装配。</summary>
        public static string RollbackInstalled()
        {
            RequireIdle();
            RevAssetStore store = OpenStore(ReadSettings());
            using (store.LockMutation())
            {
                return store.Rollback().Version;
            }
        }

        /// <summary>只有没有资源租约和在途请求时才切换到已安装版本。</summary>
        public static bool TryActivateInstalled()
        {
            return !Updating && _mounted != null && RevResourceBackends.ActiveId == BackendId &&
                RevResBootstrap.Instance.TryRefreshBackend();
        }

        /// <summary>保留当前、回退、首包和运行中版本，回收其余缓存对象并返回字节数。</summary>
        public static long ClearObsoleteCache()
        {
            RequireIdle();
            RevAssetStore store = OpenStore(ReadSettings());
            using (store.LockMutation())
            {
                return store.ClearUnused(_mounted);
            }
        }

        /// <summary>读取下一次启动能够实际使用的版本。</summary>
        public static string GetInstalledVersion()
        {
            return OpenStore(ReadSettings()).LoadInstalled()?.Version;
        }

        /// <summary>应用启动时读取已配置的目标和信任公钥。</summary>
        internal static RevAssetSettings ReadSettings()
        {
            return Resources.Load<RevAssetSettings>(RevAssetSettings.ResourcePath) ??
                throw new InvalidOperationException("请在 RevAsset 面板保存项目设置");
        }

        /// <summary>读取当前平台的随包资源清单。首包占用的 TextAsset 在解析后释放。</summary>
        internal static RevAssetStore OpenStore(RevAssetSettings settings)
        {
            string platform = RevAssetFiles.PlayerPlatform();
            TextAsset asset = Resources.Load<TextAsset>(CatalogResourceRoot + platform);
            RevAssetCatalog builtin = null;
            if (asset != null)
            {
                try
                {
                    builtin = RevAssetCatalog.Parse(asset.bytes, settings.Package, platform, settings.Compatibility);
                }
                finally
                {
                    Resources.UnloadAsset(asset);
                }
            }
            return new RevAssetStore(settings, platform, builtin);
        }

        /// <summary>同时装配逻辑地址和同版本的包来源。模拟模式走同一套业务门面。</summary>
        private static RevResourceBackend CreateBinding()
        {
            RevAssetSettings settings = ReadSettings();
#if UNITY_EDITOR
            if (settings.EditorSimulation)
            {
                if (EditorSimulationPolicy == null)
                {
                    throw new InvalidOperationException("RevAsset 编辑器采集器尚未注册");
                }
                RevResourceBackend simulation = RevResourceBackend.FromPolicy(EditorSimulationPolicy());
                _mounted = null;
                return simulation;
            }
#endif
            RevAssetStore store = OpenStore(settings);
            RevAssetCatalog catalog = store.LoadInstalled() ??
                throw new InvalidOperationException("RevAsset 找不到当前平台的首包或已安装版本");
            var backend = RevResourceBackend.FromBundles(catalog.Map, new BundleSource(catalog));
            _mounted = catalog;
            return backend;
        }

        /// <summary>统一处理并发、取消和失败。失败回执保留给启动或前端界面查询。</summary>
        private static async RevTask<RevAssetUpdateResult> RunAsync(string channel, string[] tags, bool install,
            int parallel, int attempts)
        {
            if (Updating)
            {
                return new RevAssetUpdateResult { Finished = true, Error = "已有 RevAsset 更新正在运行" };
            }
            if (parallel < 1 || parallel > 16 || attempts < 1 || attempts > 10)
            {
                throw new ArgumentOutOfRangeException(nameof(parallel), "并发 1–16，尝试次数 1–10");
            }

            var result = new RevAssetUpdateResult();
            var operation = new RevCancellationTokenSource();
            _operation = operation;
            LastUpdate = result;
            try
            {
                RevAssetSettings settings = ReadSettings();
                RevAssetStore store = OpenStore(settings);
                using (store.LockMutation())
                {
                    var updater = new RevAssetUpdater(store, operation.Token, result);
                    await updater.RunAsync(channel ?? settings.ChannelUrl, tags, install, parallel, attempts);
                }
            }
            catch (Exception error)
            {
                result.Error = error.Message;
            }
            finally
            {
                result.Finished = true;
                if (ReferenceEquals(_operation, operation))
                {
                    _operation = null;
                }
            }
            return result;
        }

        /// <summary>避免运维操作删除或切换正在下载的对象。</summary>
        private static void RequireIdle()
        {
            if (Updating)
            {
                throw new InvalidOperationException("请等当前 RevAsset 更新结束");
            }
        }

        /// <summary>框架本代加载器持有的 Unity 原生包位置和依赖。</summary>
        private sealed class BundleSource : IRevBundleSource
        {
            /// <summary>保持地址和依赖来源处于同一代。</summary>
            private readonly RevAssetCatalog _catalog;

            /// <summary>构建与逻辑地址同版的包来源。</summary>
            internal BundleSource(RevAssetCatalog catalog)
            {
                _catalog = catalog;
            }

            /// <summary>取用本代清单批准的内容。</summary>
            public string GetPath(string bundleName)
            {
                return _catalog.Bundles[bundleName].mountedPath;
            }

            /// <summary>返回当前代的完整有序依赖。</summary>
            public string[] GetDependencies(string bundleName) => _catalog.GetDependencies(bundleName);

            /// <summary>浏览器加载按 Unity Hash128 与 CRC 复用引擎缓存。</summary>
            public RevBundleCacheKey GetCacheKey(string bundleName)
            {
                RevAssetBundleData bundle = _catalog.Bundles[bundleName];
                return new RevBundleCacheKey { IsValid = true, Hash = bundle.parsedUnityHash, Crc = bundle.crc };
            }
        }
    }
}
