// ============================================================
// RevHotStore.cs —— 盘上布局 / 版本指针 / 原子落盘 / 首包落地 / 加载路径解析
//
// 位置：Assets\Revolution.HotUpdate\Runtime\Pipeline\
//
// 【目录布局（文件型平台：Android / iOS / PC）】
//   {persistentDataPath}/RevHotUpdate/
//   ├── current.txt                              ← 当前生效的资源版本（原子替换）
//   ├── _builtin/{平台}/&lt;包名&gt;                  ← 首包落地（Android 从 APK 里拷出来的那一份）
//   └── {平台}/{大版本}/{资源版本}/
//       ├── .stamp                               ← 完成标记（有它才允许被加载）
//       ├── RevHotManifest.txt                   ← 该版本清单副本（回滚 / 排查用）
//       ├── ResMap.txt                           ← 热更映射表
//       ├── bundles/&lt;包名&gt;                       ← AB 包
//       └── bundles/&lt;包名&gt;.part                  ← 下载中的半成品（断点续传就靠它）
//
// 【五条纪律（每一条都在防一个真实故障）】
//   ① 版本目录写完才打 .stamp —— 半新半旧永远不可见（加载只认 current 指向且带 stamp 的版本）；
//   ② current.txt 用"写临时文件 + 替换"—— 防止断电写出半行文本；
//   ③ 下载一律写 .part，完成并校验通过才 rename —— 绝不会出现"半个包被当成好包"；
//   ④ 覆盖已存在的文件前先删再移 —— File.Move 在目标存在时会抛异常；
//   ⑤ 小游戏（WebGL）没有文件系统 —— 所有文件相关操作都必须先看 SupportsLocalFiles。
// ============================================================
using System;
using System.Collections.Generic;
using System.IO;
using Revolution;
using UnityEngine;
using UnityEngine.Networking;

namespace Revolution.HotUpdate
{
    /// <summary>本地存储：热更包的"磁盘管家"（同时也是框架那两个钩子的实现方）。</summary>
    internal static class RevHotStore
    {
        private static RevHotConfig _config;
        private static string _platform = "";
        private static string _appVersion = "";
        private static string _localRoot = "";
        private static string _builtinDir = "";
        private static string _activeResVersion = "";
        private static bool _configured;

        // URL 型平台（WebGL/小游戏）没有本地文件，"热更映射表"放在内存里，资源系统 Init 时取走
        private static Dictionary<string, string> _memoryResMap;

        private const string PlayerPrefsKeyPrefix = "RevHotUpdate.Current.";

        // ============================================================
        // 配置与状态
        // ============================================================

        /// <summary>装配置（InitializeAsync 第一步；之后的路径都从这里算）。</summary>
        public static void Configure(RevHotConfig config)
        {
            _config = config;
            _platform = config.Platform;
            _appVersion = config.AppVersion;
            _localRoot = config.LocalRoot;
            _builtinDir = Path.Combine(_localRoot, "_builtin", _platform);
            _configured = true;
        }

        /// <summary>当前生效的资源版本（加载路径解析就按它定位目录）。</summary>
        public static string ActiveResVersion { get { return _activeResVersion; } }

        /// <summary>把某个资源版本设为"生效"（之后 ResolveBundlePath 就指向它的 bundles 目录）。</summary>
        public static void SetActiveVersion(string resVersion)
        {
            _activeResVersion = resVersion ?? string.Empty;
        }

        /// <summary>URL 型平台：把"下载阶段拿到的热更映射表"先寄存在内存里（资源系统 Init 时取走）。</summary>
        public static void SetActiveResMap(Dictionary<string, string> resMap)
        {
            _memoryResMap = resMap;
        }

        /// <summary>
        /// 解析映射表文本（"逻辑名|包名|资源名"，# 注释、空行忽略）。
        /// 与框架 RevResBootstrap.LoadResMap 的解析规则保持一致 —— 改一处必须改另一处。
        /// </summary>
        public static Dictionary<string, string> ParseResMapText(string text)
        {
            var map = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(text)) return map;

            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;

                string[] parts = line.Split('|');
                if (parts.Length != 3) continue;

                map[parts[0]] = parts[1] + "|" + parts[2];
            }

            return map;
        }

        /// <summary>排查用：当前装配与路径快照（RevHotUpdate.Dump 的一部分）。</summary>
        public static string Describe()
        {
            if (_configured == false) return "（RevHotStore 尚未装配）";
            return "平台 " + _platform + " · 大版本 " + _appVersion
                 + " · 生效资源版本 " + (string.IsNullOrEmpty(_activeResVersion) ? "（无）" : _activeResVersion)
                 + "\n  本地根 " + _localRoot
                 + "\n  内置目录 " + _builtinDir;
        }

        // ============================================================
        // ★ 钩子一：加载路径解析（RevABLoader.BundlePathResolver 的实现）
        //   覆盖式语义：热更版本 → 首包落地 → 交还框架默认（StreamingAssets）
        // ============================================================

        /// <summary>
        /// 解析一个 AB 包从哪读：
        /// ① 当前资源版本目录里有 → 用它（热更生效）；
        /// ② 首包落地目录里有 → 用它（没更新过的内置包，Android 全靠这一层）；
        /// ③ 都没有 → 返回 null，让框架走默认的 StreamingAssets（PC / iOS 的内置包就在那）。
        /// </summary>
        public static string ResolveBundlePath(string bundleName)
        {
            if (_configured == false || string.IsNullOrEmpty(_activeResVersion)) return null;

            // ---------- WebGL / 小游戏：没有"本地"，只有 URL（版本段就是它的"目录"） ----------
            if (RevHotPlatform.IsWebGL)
            {
                return RevHotUrlBuilder.BundleUrl(_config, _config.RemoteRoot, _activeResVersion, bundleName);
            }

            // ---------- 文件型平台：两级根，顺序固定 ----------
            string hot = Path.Combine(BundlesDir(_activeResVersion), bundleName);
            if (File.Exists(hot)) return hot;

            if (string.IsNullOrEmpty(_builtinDir) == false)
            {
                string builtin = Path.Combine(_builtinDir, bundleName);
                if (File.Exists(builtin)) return builtin;
            }

            // 都没有 → 交还框架默认（典型场景：PC/iOS 上没动过的内置包，本来就在 StreamingAssets）
            return null;
        }

        // ============================================================
        // ★ 钩子二：热更映射表（RevResBootstrap.ResMapOverride 的实现）
        // ============================================================

        /// <summary>
        /// 提供热更映射表（"逻辑名 → 包名|资源名"）。没有热更表返回 null（框架继续用内置表）。
        /// 返回 null 时框架的合并逻辑会跳过 —— 这是刻意的：表缺失绝不能让资源系统初始化失败。
        /// </summary>
        public static Dictionary<string, string> LoadHotResMap()
        {
            // URL 型平台：用内存里那份（清单阶段拉下来的）
            if (RevHotPlatform.IsWebGL) return _memoryResMap;

            if (_configured == false || string.IsNullOrEmpty(_activeResVersion)) return null;

            // ① 当前资源版本目录；② 首包落地目录（Android 上"还没热更过"的那一次就靠它）
            string path = ResMapPath(_activeResVersion);
            if (File.Exists(path) == false && string.IsNullOrEmpty(_builtinDir) == false)
            {
                string builtin = Path.Combine(_builtinDir, "ResMap.txt");
                if (File.Exists(builtin)) path = builtin;
            }

            if (File.Exists(path) == false) return null;

            try
            {
                // 解析规则与框架 RevResBootstrap.LoadResMap 严格一致（见 ParseResMapText 的注释）
                Dictionary<string, string> map = ParseResMapText(File.ReadAllText(path));
                if (map.Count == 0) return null;
                return map;
            }
            catch (Exception e)
            {
                RevLog.Warn("热更映射表读取失败，本次继续用内置表：" + e.Message, LogTag());
                return null;
            }
        }

        // ============================================================
        // 版本指针（current.txt / PlayerPrefs）
        // ============================================================

        /// <summary>读当前生效的资源版本（没有返回空串）。</summary>
        public static string ReadCurrentVersion()
        {
            if (RevHotPlatform.IsWebGL)
            {
                return PlayerPrefs.GetString(PlayerPrefsKey(), string.Empty);
            }

            string path = CurrentPath();
            if (File.Exists(path) == false) return string.Empty;
            try
            {
                return File.ReadAllText(path).Trim();
            }
            catch (Exception e)
            {
                RevLog.Warn("current.txt 读取失败（按未更新处理）：" + e.Message, LogTag());
                return string.Empty;
            }
        }

        /// <summary>
        /// 原子地写版本指针：先写临时文件，再用 File.Replace / Move 覆盖。
        /// ★ 直接 WriteAllText 到 current.txt，断电瞬间可能留下半行文本 —— 版本指针一坏，整个热更就"失忆"了。
        /// </summary>
        public static void WriteCurrentVersion(string resVersion)
        {
            if (RevHotPlatform.IsWebGL)
            {
                PlayerPrefs.SetString(PlayerPrefsKey(), resVersion ?? string.Empty);
                PlayerPrefs.Save();
                return;
            }

            string path = CurrentPath();
            string tmp = path + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(tmp, (resVersion ?? string.Empty) + "\n");

            if (File.Exists(path))
            {
                File.Replace(tmp, path, null);
            }
            else
            {
                File.Move(tmp, path);
            }
        }

        private static string PlayerPrefsKey()
        {
            return PlayerPrefsKeyPrefix + _platform + "." + _appVersion;
        }

        // ============================================================
        // 目录与标记
        // ============================================================

        /// <summary>建好所有需要的目录（幂等；WebGL 上什么都不做）。</summary>
        public static void PrepareLocalRoot()
        {
            if (RevHotPlatform.SupportsLocalFiles == false) return;

            Directory.CreateDirectory(_localRoot);
            Directory.CreateDirectory(AppVersionDir());
            if (RevHotPlatform.NeedsBuiltinCopy || _config.FirstPackageMode == RevHotFirstPackageMode.CopyToLocal)
            {
                Directory.CreateDirectory(_builtinDir);
            }

            ClearStaleParts();
        }

        /// <summary>{本地根}/{平台}/{大版本} 目录。</summary>
        public static string AppVersionDir()
        {
            return Path.Combine(_localRoot, Path.Combine(_platform, _appVersion));
        }

        /// <summary>{…}/{资源版本} 目录。</summary>
        public static string VersionDir(string resVersion)
        {
            return Path.Combine(AppVersionDir(), resVersion);
        }

        /// <summary>版本指针文件（current.txt）。</summary>
        public static string CurrentPath()
        {
            return Path.Combine(_localRoot, "current.txt");
        }

        /// <summary>某资源版本的 AB 包目录。</summary>
        public static string BundlesDir(string resVersion)
        {
            return Path.Combine(VersionDir(resVersion), "bundles");
        }

        /// <summary>某资源版本的清单副本。</summary>
        public static string ManifestPath(string resVersion)
        {
            return Path.Combine(VersionDir(resVersion), _config.ManifestFileName);
        }

        /// <summary>某资源版本的热更映射表。</summary>
        public static string ResMapPath(string resVersion)
        {
            return Path.Combine(VersionDir(resVersion), "ResMap.txt");
        }

        /// <summary>某资源版本是否已"完整"（.stamp 在 = 所有文件校验通过）。</summary>
        public static bool IsVersionComplete(string resVersion)
        {
            if (RevHotPlatform.IsWebGL) return ReadCurrentVersion() == resVersion;
            if (string.IsNullOrEmpty(resVersion)) return false;
            return File.Exists(Path.Combine(VersionDir(resVersion), ".stamp"));
        }

        /// <summary>给版本目录打完成标记（打上之后这个版本才允许被 current 指向）。</summary>
        public static void MarkVersionComplete(string resVersion)
        {
            if (RevHotPlatform.IsWebGL)
            {
                WriteCurrentVersion(resVersion);
                return;
            }

            string stamp = Path.Combine(VersionDir(resVersion), ".stamp");
            Directory.CreateDirectory(VersionDir(resVersion));
            File.WriteAllText(stamp, "ok\n");
        }

        /// <summary>首包落地目录（Android 从 APK 拷出来的那份；PC/iOS 配了 CopyToLocal 也会有）。</summary>
        public static string BuiltinDir()
        {
            return _builtinDir;
        }

        /// <summary>清理临时残留：超过 7 天没动过的 .part（上次没下完又再也不可能续上的）。</summary>
        public static void ClearStaleParts()
        {
            if (RevHotPlatform.SupportsLocalFiles == false) return;

            try
            {
                string appDir = AppVersionDir();
                if (Directory.Exists(appDir) == false) return;

                string[] parts = Directory.GetFiles(appDir, "*.part", SearchOption.AllDirectories);
                DateTime threshold = DateTime.UtcNow.AddDays(-7);
                for (int i = 0; i < parts.Length; i++)
                {
                    if (File.GetLastWriteTimeUtc(parts[i]) < threshold)
                    {
                        File.Delete(parts[i]);
                    }
                }
            }
            catch (Exception e)
            {
                // 清理失败不影响主流程（下次还会再试）
                RevLog.Warn("清理临时下载文件失败（忽略）：" + e.Message, LogTag());
            }
        }

        // ============================================================
        // 文件级原子操作
        // ============================================================

        /// <summary>半成品路径（与目标同目录、多一个 .part 后缀 —— 断点续传就从它的长度继续）。</summary>
        public static string PartPath(string finalPath)
        {
            return finalPath + ".part";
        }

        /// <summary>已存在的半成品长度（没有就是 0）。</summary>
        public static long ExistingLength(string path)
        {
            if (RevHotPlatform.SupportsLocalFiles == false) return 0;
            try
            {
                if (File.Exists(path) == false) return 0;
                return new FileInfo(path).Length;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>删除文件（不存在不算错误）。</summary>
        public static void DeleteFile(string path)
        {
            if (RevHotPlatform.SupportsLocalFiles == false) return;
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception e)
            {
                RevLog.Warn("删除文件失败：" + path + "，" + e.Message, LogTag());
            }
        }

        /// <summary>
        /// 原子提交：半成品 → 正式文件。
        /// ★ 先删目标再 Move —— File.Move 在目标存在时会抛异常；而此时该版本尚未生效，不存在"删掉正在用的包"。
        /// </summary>
        public static void CommitFile(string tmpPath, string finalPath)
        {
            if (RevHotPlatform.SupportsLocalFiles == false) return;

            string dir = Path.GetDirectoryName(finalPath);
            if (string.IsNullOrEmpty(dir) == false && Directory.Exists(dir) == false)
            {
                Directory.CreateDirectory(dir);
            }

            if (File.Exists(finalPath)) File.Delete(finalPath);
            File.Move(tmpPath, finalPath);
        }

        /// <summary>磁盘预检：可用空间必须比"要下载的量"多出一截（临时文件 + 系统余量）。</summary>
        public static void EnsureDiskSpace(long requiredBytes)
        {
            if (RevHotPlatform.SupportsLocalFiles == false) return;

            try
            {
                string full = Path.GetFullPath(_localRoot);
                string rootPath = Path.GetPathRoot(full);
                if (string.IsNullOrEmpty(rootPath)) return;

                DriveInfo drive = new DriveInfo(rootPath);
                long need = requiredBytes + 64L * 1024 * 1024;               // 多留 64MB：解压/临时文件/系统
                if (drive.AvailableFreeSpace < need)
                {
                    string reason = "磁盘空间不足：需要 " + RevHotProgress.FormatBytes(requiredBytes) + "，可用 " + RevHotProgress.FormatBytes(drive.AvailableFreeSpace);
                    throw new RevHotException(RevHotError.Of(RevHotErrorCode.DiskFull, reason + "，请清理后重试"), false);
                }
            }
            catch (RevHotException)
            {
                throw;                                                       // 磁盘满是我们抛的，原样往上送
            }
            catch
            {
                // 拿不到磁盘信息（部分平台 / 特殊目录）→ 跳过预检，让后面的下载自己暴露问题
            }
        }

        // ============================================================
        // 首包落地（Android 的命门：APK 里的包不落地，LoadFromFile 读不到）
        // ============================================================

        /// <summary>内置清单（首包基线）的读取地址：Android 是 jar: URL，桌面 / iOS 是文件路径。</summary>
        public static string BuiltinManifestSource()
        {
            return RevHotUrlBuilder.BuiltinManifestPath(_config);
        }

        /// <summary>读一段文本（自动区分 URL 与本地文件）：Android 的 StreamingAssets 只能用 UnityWebRequest 读。</summary>
        public static async RevTask<string> ReadTextAsync(string pathOrUrl, RevCancellationToken token)
        {
            bool isUrl = pathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                         || pathOrUrl.StartsWith("jar:", StringComparison.OrdinalIgnoreCase);

            if (isUrl == false)
            {
                return File.ReadAllText(pathOrUrl);
            }

            using (UnityWebRequest www = UnityWebRequest.Get(pathOrUrl))
            {
                www.timeout = _config.TimeoutSeconds;
                var op = www.SendWebRequest();
                while (op.isDone == false)
                {
                    token.ThrowIfCancelled();
                    await RevTask.Yield();
                }

                if (www.result != UnityWebRequest.Result.Success)
                {
                    throw new RevHotException(RevHotError.Of(RevHotErrorCode.ManifestFetchFailed, "读取内置文件失败（" + www.error + "）", pathOrUrl), false);
                }

                return www.downloadHandler.text;
            }
        }

        /// <summary>
        /// 首包落地：把 StreamingAssets 里的内置包拷到 persistentDataPath（幂等 —— 大小一致的直接跳过）。
        /// Android 用 UnityWebRequest 从 APK 里读；桌面 / iOS 直接 File.Copy。
        /// </summary>
        public static async RevTask CopyBuiltinPackagesAsync(RevHotManifest builtin, RevHotConfig config, Action<RevHotProgress> onProgress, RevCancellationToken token)
        {
            if (RevHotPlatform.SupportsLocalFiles == false) return;
            if (builtin == null || builtin.Bundles.Count == 0) return;

            Directory.CreateDirectory(_builtinDir);

            long total = 0;
            for (int i = 0; i < builtin.Bundles.Count; i++) total += builtin.Bundles[i].Size;
            var progress = new RevHotProgressAggregator(RevHotState.Applying, total, builtin.Bundles.Count, onProgress, LogTag());

            for (int i = 0; i < builtin.Bundles.Count; i++)
            {
                RevHotBundleInfo bundle = builtin.Bundles[i];
                token.ThrowIfCancelled();
                progress.BeginFile("内置/" + bundle.Name);

                string target = Path.Combine(_builtinDir, bundle.Name);

                // 幂等：已在且大小一致 → 跳过（二次启动 / 断点续装都靠这一条）
                if (File.Exists(target) && new FileInfo(target).Length == bundle.Size)
                {
                    progress.CompleteFile(bundle.Size);
                    continue;
                }

                string source = RevHotUrlBuilder.BuiltinBundlePath(config, bundle.Name);
                await CopyBuiltinFileAsync(source, target, config, token);

                // 内置包来自自家 APK / 安装目录，默认只比大小（快）；要更严可以打开 VerifyBuiltinCopy
                if (config.VerifyBuiltinCopy)
                {
                    await RevHotVerifier.VerifyAsync(target, bundle.Size, bundle.Sha256, config.VerifyMode, config, token);
                }

                progress.CompleteFile(bundle.Size);
            }

            // ---------- 映射表也一并落地（Android 没有它，"未热更的客户端"读不到表） ----------
            if (builtin.ResMap != null)
            {
                string resMapTarget = Path.Combine(_builtinDir, builtin.ResMap.Name);
                if (File.Exists(resMapTarget) == false || new FileInfo(resMapTarget).Length != builtin.ResMap.Size)
                {
                    string resMapSource = RevHotUrlBuilder.BuiltinBundlePath(config, builtin.ResMap.Name);
                    await CopyBuiltinFileAsync(resMapSource, resMapTarget, config, token);
                }
            }
        }

        /// <summary>
        /// 拷一个内置文件到目标：Android 用 UnityWebRequest 读 APK（jar: URL），桌面 / iOS 直接 File.Copy。
        /// </summary>
        private static async RevTask CopyBuiltinFileAsync(string source, string target, RevHotConfig config, RevCancellationToken token)
        {
            bool sourceIsUrl = source.StartsWith("jar:", StringComparison.OrdinalIgnoreCase)
                               || source.StartsWith("http", StringComparison.OrdinalIgnoreCase);

            if (sourceIsUrl == false)
            {
                File.Copy(source, target, true);
                await RevTask.Yield();                                   // 每个文件让一帧：大拷贝不冻结 loading
                return;
            }

            string tmp = PartPath(target);
            using (UnityWebRequest www = UnityWebRequest.Get(source))
            {
                www.downloadHandler = new DownloadHandlerFile(tmp, false);
                www.timeout = config.TimeoutSeconds;

                var op = www.SendWebRequest();
                while (op.isDone == false)
                {
                    token.ThrowIfCancelled();
                    await RevTask.Yield();
                }

                if (www.result != UnityWebRequest.Result.Success)
                {
                    DeleteFile(tmp);
                    string reason = "内置资源拷贝失败（" + www.error + "）—— 请重启游戏重试";
                    throw new RevHotException(RevHotError.Of(RevHotErrorCode.DownloadFailed, reason, source), true);
                }
            }

            CommitFile(tmp, target);
        }

        // ============================================================
        // 旧版本清理
        // ============================================================

        /// <summary>
        /// 只保留当前版本 + 最新的 N 个版本，其余删掉（回滚能力就来自"多留的这几份"）。
        /// ★ 只删当前大版本目录下的内容 —— 跨大版本的目录等"切换大版本"时整体处理，这里绝不越界。
        /// </summary>
        public static void DeleteOldVersions(int keep, string currentVersion)
        {
            if (RevHotPlatform.SupportsLocalFiles == false) return;

            string appDir = AppVersionDir();
            if (Directory.Exists(appDir) == false) return;

            List<string> dirs = new List<string>(Directory.GetDirectories(appDir));

            // 排序规则先起个名字，Sort 调用就能写成单行（可读性也更好：一眼看出"按版本号倒序"）
            Comparison<string> newestFirst = delegate(string a, string b)
            {
                string va = Path.GetFileName(a);
                string vb = Path.GetFileName(b);
                return RevHotVersion.Compare(vb, va);                    // 新的在前
            };
            dirs.Sort(newestFirst);

            int kept = 0;
            for (int i = 0; i < dirs.Count; i++)
            {
                string name = Path.GetFileName(dirs[i]);
                bool isCurrent = string.Equals(name, currentVersion, StringComparison.Ordinal);

                if (isCurrent)
                {
                    kept++;
                    continue;
                }

                if (kept < keep)
                {
                    kept++;
                    continue;
                }

                try
                {
                    Directory.Delete(dirs[i], true);
                    RevLog.Info("清理旧资源版本：" + name, LogTag());
                }
                catch (Exception e)
                {
                    // 删不掉（被占用等）不算失败：下次启动还会再试
                    RevLog.Warn("清理旧版本失败（忽略）：" + name + "，" + e.Message, LogTag());
                }
            }
        }

        // ============================================================
        // 重置（进 Play / 域重载时清静态状态 —— 不动磁盘上的任何文件）
        // ============================================================

        public static void ResetForNewSession()
        {
            _config = null;
            _platform = "";
            _appVersion = "";
            _localRoot = "";
            _builtinDir = "";
            _activeResVersion = "";
            _memoryResMap = null;
            _configured = false;
        }

        private static string LogTag()
        {
            return _config == null ? "HotUpdate" : _config.LogTag;
        }
    }
}
