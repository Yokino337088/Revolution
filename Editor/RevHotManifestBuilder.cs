// ============================================================
// RevHotManifestBuilder.cs —— 生成热更清单（扫打包产物 → RevHotManifest.txt / RevHotBuiltin.txt）
//
// 位置：Assets\Revolution.HotUpdate\Editor\
//
// 【它做什么】
//   框架的打包工具打出 AB 产物后（AssetBundles/<平台>/），这份工具负责"看一遍产物"并生成：
//   ① <产物目录>/RevHotManifest.txt ：要上传到 CDN 的版本清单（唯一真相，最后上传）；
//   ② <产物目录>/RevHotBuiltin.txt  ：首包基线清单（原样拷进 StreamingAssets，客户端拿它算差量）；
//   ③ <产物目录>/ResMap.txt 副本    ：映射表跟着清单一起走（表与包同批更新）。
//
// 【数据从哪来（不重复造轮子）】
//   包列表   ：扫产物目录（排除主包 / .manifest / json / 我们自己的文件）；
//   大小     ：文件长度；
//   sha256   ：RevHotVerifier.ComputeSha256（运行时校验用的同一个算法）；
//   依赖     ：Unity 为每个包生成的 <包名>.manifest 文本里的 Dependencies 段；
//   unityHash / unityCrc：同一段文本里的 AssetFileHash / CRC（小游戏上交给引擎做缓存与校验）。
//   ★ Unity 的 .manifest 是文本，跨平台可读 —— 不需要"加载另一个平台的 AB"才能拿到依赖。
// ============================================================
using Revolution.Editor;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Revolution.HotUpdate.Editor
{
    /// <summary>清单生成器（窗口与 CI 都调它）。</summary>
    public static class RevHotManifestBuilder
    {
        /// <summary>映射表文件名（与框架 ABBuildSetting.MapAssetPath 指向的文件同名）。</summary>
        public const string ResMapFileName = "ResMap.txt";

        /// <summary>首包基线清单文件名（随包体发布的那份）。</summary>
        public const string BuiltinManifestFileName = "RevHotBuiltin.txt";

        /// <summary>生成清单。任何问题返回 null 并给出 error（人话）。</summary>
        public static RevHotManifest Build(BuildTarget target, string outputDir, string appVersion, string resVersion, string channel, string env, out string error)
        {
            error = null;

            if (Directory.Exists(outputDir) == false)
            {
                error = "产物目录不存在：" + outputDir + "（请先用 Revolution.Tools 的打包工具打一次 AB）";
                return null;
            }

            if (string.IsNullOrEmpty(appVersion) || string.IsNullOrEmpty(resVersion))
            {
                error = "AppVersion / ResVersion 不能为空（大版本锚定与差量计算都靠它们）";
                return null;
            }

            string platform = ABBuildSetting.GetPlatformName(target);
            string mainBundleName = platform;                       // Unity 用输出目录名给主包命名（主包只装 Manifest，不是业务包）

            var manifest = new RevHotManifest
            {
                AppVersion = appVersion,
                ResVersion = resVersion,
                MinAppVersion = appVersion,                         // 默认只兼容本大版本（补丁包语义）
                Platform = platform,
                Channel = channel,
                Env = env,
                HashAlgo = "sha256",
                BuiltAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                BaseManifestFile = "Baseline.txt",
            };

            var warnings = new List<string>();

            foreach (string file in Directory.GetFiles(outputDir))
            {
                string name = Path.GetFileName(file);

                if (name.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase)) continue;   // Unity 的清单文本
                if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;       // 框架的 BuildManifest.json
                if (name.StartsWith("RevHot", StringComparison.OrdinalIgnoreCase)) continue;    // 我们自己的产物（防二次生成互相污染）
                if (string.Equals(name, mainBundleName, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(name, ResMapFileName, StringComparison.OrdinalIgnoreCase)) continue;   // ★ 上次 WriteOutputs 拷进来的映射表副本 —— 不排除会被二次扫成"AB 包"进清单
                if (name.StartsWith(".", StringComparison.Ordinal)) continue;                   // 隐藏文件

                var bundle = new RevHotBundleInfo
                {
                    Name = name,
                    Size = new FileInfo(file).Length,
                    Sha256 = RevHotVerifier.ComputeSha256(file),
                };

                string unityManifestFile = file + ".manifest";
                if (File.Exists(unityManifestFile))
                {
                    List<string> dependencies;
                    string unityHash;
                    string unityCrc;
                    ParseUnityManifest(unityManifestFile, out dependencies, out unityHash, out unityCrc);
                    bundle.SetDependencies(string.Join(",", dependencies.ToArray()));
                    bundle.UnityHash = unityHash;
                    bundle.UnityCrc = unityCrc;
                }
                else
                {
                    // 没有对应 .manifest：依赖拿不到 → 客户端只能靠 Unity 的主包 Manifest 兜底。
                    // 不算致命（Unity 一定会生成），但要让人知道清单里这一包没有依赖信息。
                    warnings.Add(name + " 缺少对应的 .manifest 文件（依赖信息为空）");
                }

                manifest.Bundles.Add(bundle);
            }

            if (manifest.Bundles.Count == 0)
            {
                error = "产物目录里没有扫到任何 AB 包：" + outputDir;
                return null;
            }

            // ---------- 映射表：热更"新增资源"全靠它（缺失只警告不拦 —— 首版可能确实没有） ----------
            string resMapPath = ABBuildSetting.MapAssetPath;
            if (File.Exists(resMapPath))
            {
                manifest.ResMap = new RevHotFileInfo
                {
                    Name = ResMapFileName,
                    Sha256 = RevHotVerifier.ComputeSha256(resMapPath),
                    Size = new FileInfo(resMapPath).Length,
                };
            }
            else
            {
                warnings.Add("找不到映射表（" + resMapPath + "）：本清单不带 ResMap —— 新增资源将无法通过热更下发");
            }

            for (int i = 0; i < warnings.Count; i++)
            {
                Debug.LogWarning("[RevHotUpdate] " + warnings[i]);
            }

            return manifest;
        }

        /// <summary>写出清单文件（CDN 用的 + 首包基线用的），并把 ResMap 副本放在一起。</summary>
        public static void WriteOutputs(RevHotManifest manifest, string outputDir)
        {
            string text = manifest.Serialize();

            File.WriteAllText(Path.Combine(outputDir, "RevHotManifest.txt"), text);
            File.WriteAllText(Path.Combine(outputDir, BuiltinManifestFileName), text);

            if (manifest.ResMap != null)
            {
                string resMapSource = ABBuildSetting.MapAssetPath;
                if (File.Exists(resMapSource))
                {
                    File.Copy(resMapSource, Path.Combine(outputDir, manifest.ResMap.Name), true);
                }
            }

            // 出包用：如果 StreamingAssets 里已经有该平台的目录（框架打包时开着"拷贝到 StreamingAssets"），
            // 就把首包基线清单直接写进去 —— 客户端首次启动读的就是它
            string streamingDir = Path.Combine(Application.streamingAssetsPath, manifest.Platform);
            if (Directory.Exists(streamingDir))
            {
                File.WriteAllText(Path.Combine(streamingDir, BuiltinManifestFileName), text);
            }
        }

        /// <summary>
        /// 自检：清单里每个包都在磁盘上、大小一致；映射表存在；依赖引用的包都在清单里。
        /// 返回报告文本（空字符串 = 全部通过）。★ 自检拦下的每一处，都是真机上排查半天的问题。
        /// </summary>
        public static string SelfCheck(RevHotManifest manifest, string outputDir)
        {
            var problems = new List<string>();

            for (int i = 0; i < manifest.Bundles.Count; i++)
            {
                RevHotBundleInfo bundle = manifest.Bundles[i];
                string path = Path.Combine(outputDir, bundle.Name);

                if (File.Exists(path) == false)
                {
                    problems.Add("包不存在：" + bundle.Name);
                    continue;
                }

                long size = new FileInfo(path).Length;
                if (size != bundle.Size)
                {
                    problems.Add("包大小不一致：" + bundle.Name + "（清单 " + bundle.Size + " / 磁盘 " + size + "）");
                }
            }

            if (manifest.ResMap == null)
            {
                problems.Add("清单没有携带映射表（ResMap）—— 新增资源将无法热更下发");
            }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < manifest.Bundles.Count; i++) names.Add(manifest.Bundles[i].Name);

            for (int i = 0; i < manifest.Bundles.Count; i++)
            {
                RevHotBundleInfo bundle = manifest.Bundles[i];
                for (int d = 0; d < bundle.Dependencies.Count; d++)
                {
                    if (names.Contains(bundle.Dependencies[d]) == false)
                    {
                        problems.Add("包 " + bundle.Name + " 依赖的 " + bundle.Dependencies[d] + " 不在清单里");
                    }
                }
            }

            if (problems.Count == 0) return string.Empty;

            var sb = new System.Text.StringBuilder(256);
            for (int i = 0; i < problems.Count; i++) sb.AppendLine((i + 1) + ". " + problems[i]);
            return sb.ToString();
        }

        // ============================================================
        // Unity .manifest 文本解析（CRC / AssetFileHash / Dependencies）
        // ============================================================

        /// <summary>
        /// 解析 Unity 生成的 .manifest 文本。
        /// ★ 字段名以实测为准（不同 Unity 版本可能有出入）—— 第一次使用时先打开产物里的 .manifest 看一眼。
        /// </summary>
        private static void ParseUnityManifest(string path, out List<string> dependencies, out string assetFileHash, out string crc)
        {
            dependencies = new List<string>();
            assetFileHash = string.Empty;
            crc = string.Empty;

            string section = string.Empty;
            string[] lines = File.ReadAllLines(path);

            for (int i = 0; i < lines.Length; i++)
            {
                string text = lines[i].Trim();

                if (text.StartsWith("CRC:", StringComparison.OrdinalIgnoreCase))
                {
                    crc = text.Substring(4).Trim();
                    continue;
                }

                if (text.StartsWith("Hashes:", StringComparison.OrdinalIgnoreCase)) { section = "Hashes"; continue; }
                if (text.StartsWith("AssetFileHash:", StringComparison.OrdinalIgnoreCase)) { section = "AssetFileHash"; continue; }
                if (text.StartsWith("TypeTreeHash:", StringComparison.OrdinalIgnoreCase)) { section = "TypeTreeHash"; continue; }
                if (text.StartsWith("Assets:", StringComparison.OrdinalIgnoreCase)) { section = "Assets"; continue; }
                if (text.StartsWith("Dependencies:", StringComparison.OrdinalIgnoreCase)) { section = "Dependencies"; continue; }

                if (text.StartsWith("Hash:", StringComparison.OrdinalIgnoreCase))
                {
                    // 只取 AssetFileHash 段里那个 Hash（它就是"这个包内容"的指纹）
                    if (section == "AssetFileHash" && assetFileHash.Length == 0)
                    {
                        assetFileHash = text.Substring(5).Trim();
                    }

                    continue;
                }

                if (text.StartsWith("- ", StringComparison.Ordinal) && section == "Dependencies")
                {
                    dependencies.Add(text.Substring(2).Trim());
                }
            }
        }
    }
}
