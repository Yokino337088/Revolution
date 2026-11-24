// ============================================================
// RevHotResBridge.cs —— 与框架资源系统的唯一接缝（整个热更包只碰这四个钩子）
//
// 位置：Assets\Revolution.HotUpdate\Runtime\Integration\
//
// 【接缝】（框架侧各只有几行，默认 null，不装热更包零影响）
//   RevABLoader.BundlePathResolver     ← RevHotStore.ResolveBundlePath      （包从哪读）
//   RevABLoader.BundleCacheKeyResolver ← RevHotStore.ResolveBundleCacheKey  （URL 型平台：引擎缓存标识 hash/crc）
//   RevABLoader.DependenciesOverride   ← RevHotStore.ResolveDependencies    （依赖图用热更清单的 —— 主包 Manifest 是出包时的静态快照）
//   RevResBootstrap.ResolvedResMapSnapshot ← RevHotStore.LoadHotResMap     （已解析的整版地址索引）
//
// 【为什么 Install 不触发资源系统 Init】
//   钩子是静态委托，设好后对"之后每一次 Init"都生效；
//   而 Init 要在"热更内容就位"之后调才有意义 —— 所以时序由 InitializeAsync 收尾统一安排：
//   检查 → 下载 → 落地 → 切版本 → ShutdownAll + Init（重读热更映射表、让新路径生效）。
// ============================================================
using Revolution;

namespace Revolution.HotUpdate
{
    /// <summary>框架桥：负责装/卸四个钩子，并按正确时序重装资源策略。</summary>
    internal static class RevHotResBridge
    {
        private const string LogTag = "HotUpdate";

        private static bool _installed;

        /// <summary>钩子是否已装载。</summary>
        public static bool IsInstalled { get { return _installed; } }

        /// <summary>装载钩子（幂等：重复调用只是把同一组委托再设一遍）。</summary>
        public static void Install()
        {
            if (_installed) return;

            // ★ 直接用方法组（不写 lambda）：没有闭包，就不会在"关闭 Domain Reload"的第二次
            //   Play 里持有已失效的对象引用 —— 这是框架里其它静态钩子的同款纪律。
            RevABLoader.BundlePathResolver = RevHotStore.ResolveBundlePath;
            RevABLoader.BundleCacheKeyResolver = RevHotStore.ResolveBundleCacheKey;
            RevABLoader.DependenciesOverride = RevHotStore.ResolveDependencies;
            RevResBootstrap.ResolvedResMapSnapshot = RevHotStore.LoadHotResMap;
            _installed = true;

            RevLog.Info("热更钩子已装载（BundlePathResolver / BundleCacheKeyResolver / DependenciesOverride / ResolvedResMapSnapshot）", LogTag);
        }

        /// <summary>重装策略，由资源系统先校验映射，再按原依赖归还旧资源。</summary>
        public static void ReinitResourceSystem()
        {
            RevResBootstrap.Instance.Init();
        }

        /// <summary>进 Play / 域重载复位（钩子会由下次 Install 重新装载）。</summary>
        public static void Reset()
        {
            _installed = false;
        }
    }
}
