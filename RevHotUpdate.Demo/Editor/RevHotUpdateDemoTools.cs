// ============================================================
// RevHotUpdateDemoTools.cs —— 热更 demo 的编辑器工具（把"上传到对象存储"这一步在本地做掉）
//
// 位置：Assets\Revolution.Demo\RevHotUpdate.Demo\Editor\
//
// 【它做什么】菜单 Revolution.Tools / 热更新 / Demo / ：
//   ① 一键：打包 + 清单 + 装配本地 CDN
//      a. 把演示资源 Assets/GameRes/RevHotDemo/revhot_demo.txt 的"构建序号"+1（这样每次都能看到一轮真实的差量更新）
//      b. 跑框架的打包链：收集 → 校验 → 打 AB → 写 ResMap.txt / BuildManifest.json / RevResPath → 拷进 StreamingAssets（首包基线）
//      c. 生成热更清单 RevHotManifest.txt（hash / 依赖 / CRC 都来自 Unity 的 .manifest）
//      d. 按**远端目录约定**装配到 {工程根}/LocalCDN —— 内容先、清单最后（与真实上传同一条纪律）
//   ② 打开本地 CDN 目录
//   ③ 清空客户端本地热更目录（回到出包基线 —— 验证"我删掉热更内容会回退"）
//   ④ 打印本次要填的 URL（排错用）
//
// 【为什么远端目录长这样】
//   {root}/{环境?}/{平台}/{大版本}/{渠道?}/RevHotManifest.txt        ← 清单不带资源版本（它是"发现新版本"的入口）
//   {root}/{环境?}/{平台}/{大版本}/{渠道?}/{资源版本}/bundles/<包名>  ← 内容带版本段（不可变，可长缓存）
//   {root}/{环境?}/{平台}/{大版本}/{渠道?}/{资源版本}/ResMap.txt
//   唯一真相在包内的 RevHotUrlBuilder（internal）；本文件只是把同一套规则抄一份用来打日志。
// ============================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Revolution.Editor;
using Revolution.HotUpdate;
using Revolution.HotUpdate.Editor;      // RevHotManifestBuilder（生成清单）在这个命名空间
using UnityEditor;
using UnityEngine;

namespace Revolution.Demo.HotUpdate
{
    /// <summary>热更 demo 的编辑器工具（菜单入口：Revolution.Tools / 热更新 / Demo）。</summary>
    public static class RevHotUpdateDemoTools
    {
        // ============================================================
        // demo 约定（★ 改端口 / 目录时，面板里的 RemoteRoot 也要一起改）
        // ============================================================

        /// <summary>演示资源在哪个包里（这个标记写在 revhot_demo.txt.meta 上，工具会兜底补一次）。</summary>
        private const string DemoBundleName = "revhotdemo";

        /// <summary>演示资源路径（它就是"被热更的那张图/那份数据"在本 demo 里的替身）。</summary>
        private const string DemoDataAssetPath = "Assets/GameRes/RevHotDemo/revhot_demo.txt";

        /// <summary>本地 CDN 端口（与 起本地CDN.cmd 的默认端口一致）。</summary>
        private const int LocalCdnPort = 8000;

        /// <summary>渠道 / 环境：留空 → URL 里不出现这两段（演示最简形态）。</summary>
        private const string Channel = "";
        private const string Env = "";

        /// <summary>清单文件名：与运行时同一份真相（RevHotConfig.ManifestFileName），不要手写字符串。</summary>
        private static readonly string ManifestFileName = new RevHotConfig().ManifestFileName;

        /// <summary>装配版本号用的状态文件（放 Library 下：不进版本库，也不会被 Unity 当成资源）。</summary>
        private static string StateFilePath = Path.Combine("Library", "Revolution", "RevHotDemo", "state.txt");

        // ============================================================
        // ① 一键：打包 + 清单 + 装配本地 CDN
        // ============================================================

        [MenuItem("Revolution.Tools/热更新/Demo/① 一键：打包 + 清单 + 装配本地 CDN", false, 21)]
        public static void PrepareAll()
        {
            try
            {
                EditorUtility.DisplayProgressBar("RevHotDemo", "① 准备演示资源（构建序号 +1）…", 0.05f);

                int buildNumber = ReadBuildNumber() + 1;
                WriteDemoDataFile(buildNumber);
                EnsureBundleMark();

                BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
                string platform = ABBuildSetting.GetPlatformName(target);
                string outputDir = ABBuildSetting.GetOutputDir(target);
                string appVersion = Application.version;                     // 大版本锚点 = 客户端的大版本（Application.version）
                string resVersion = appVersion + "." + buildNumber;          // 资源版本 = 大版本内自增（1.0.3、1.0.4 …）

                Debug.Log($"[RevHotDemo] 本轮：平台 {platform}（构建目标 {target}）· 大版本 {appVersion} · 资源版本 {resVersion}");

                // ---------- ② 收集 + 校验（校验不过就别打包：把问题在打包前报出来） ----------
                EditorUtility.DisplayProgressBar("RevHotDemo", "② 收集分包信息并校验…", 0.15f);
                ABCollectResult collect = ABCollector.Collect();
                ABValidateResult validate = ABValidator.Validate(collect);
                if (validate.CanBuild == false)
                {
                    ReportValidateErrors(validate);
                    return;
                }
                Debug.Log($"[RevHotDemo] 分包：{collect.bundleToAssets.Count} 个包 / {collect.markedCount} 个资源");

                // ---------- ③ 打 AB ----------
                EditorUtility.DisplayProgressBar("RevHotDemo", "③ 打 AssetBundle…", 0.3f);
                ABBuildResult build = ABBuilderCore.Build(target);
                if (build == null || build.success == false)
                {
                    Debug.LogError("[RevHotDemo] 打包失败 —— 请打开 Revolution.Tools / 资源 / RevAB 打包工具 看详细报错。");
                    return;
                }

                // ---------- ④ 映射表 / 产物清单 / 路径常量 / 首包拷进 StreamingAssets ----------
                EditorUtility.DisplayProgressBar("RevHotDemo", "④ 写映射表与产物清单、拷首包…", 0.5f);
                int mapCount = ABManifestWriter.WriteResMap(collect);
                ABManifestWriter.WriteBuildManifest(build);
                ABResPathGenerator.Generate();
                ABBuilderCore.CopyToStreamingAssets(build.outputDir);
                if (ABBuildConfig.Instance.copyToStreamingAssets == false)
                {
                    Debug.LogWarning("[RevHotDemo] 打包配置里『打包后拷到 StreamingAssets』是关的 —— " +
                                     "热更包回退要用它当首包基线，建议在 RevAB 打包工具里打开。");
                }
                Debug.Log($"[RevHotDemo] ResMap 写入 {mapCount} 条 → {ABBuildSetting.MapAssetPath}");

                // ---------- ⑤ 生成热更清单（hash / 依赖 / CRC 来自 Unity 的 .manifest） ----------
                //   注意顺序：先 CopyToStreamingAssets（它会建出 StreamingAssets/<平台>/），
                //   再 WriteOutputs —— 这样 RevHotBuiltin.txt（首包基线清单）才会一并落到 StreamingAssets 里。
                EditorUtility.DisplayProgressBar("RevHotDemo", "⑤ 生成热更清单…", 0.7f);
                string error;
                RevHotManifest manifest = RevHotManifestBuilder.Build(target, outputDir, appVersion, resVersion, Channel, Env, out error);
                if (manifest == null)
                {
                    Debug.LogError($"[RevHotDemo] 生成清单失败：{error}");
                    return;
                }
                RevHotManifestBuilder.WriteOutputs(manifest, outputDir);

                string issues = RevHotManifestBuilder.SelfCheck(manifest, outputDir);
                if (string.IsNullOrEmpty(issues) == false)
                {
                    Debug.LogWarning($"[RevHotDemo] 清单自检发现问题（不影响演示，但请看一眼）：\n{issues}");
                }

                // ---------- ⑥ 装配到本地 CDN：内容先、清单最后 ----------
                EditorUtility.DisplayProgressBar("RevHotDemo", "⑥ 装配本地 CDN…", 0.9f);
                int contentCount = AssembleLocalCdn(build, manifest, outputDir, platform, appVersion, resVersion);

                WriteBuildNumber(buildNumber);
                AssetDatabase.Refresh();

                // ---------- ⑦ 汇总：告诉使用者"接下来做什么" ----------
                string manifestUrl = ComposeUrl(platform, appVersion, null, ManifestFileName);
                string bundleUrl = ComposeUrl(platform, appVersion, resVersion, "bundles/" + DemoBundleName);

                string summary =
                    "[RevHotDemo] 本轮准备完成 ✓\n" +
                    $"  大版本 {appVersion} · 资源版本 {resVersion}（构建序号 {buildNumber}）\n" +
                    $"  清单：{manifestUrl}\n" +
                    $"  包  ：{bundleUrl}\n" +
                    $"  CDN ：{LocalCdnRoot()}（内容 {contentCount} 个文件 + 清单最后写入）\n" +
                    "下一步：① 双击 起本地CDN.cmd（默认 8000 端口）② 打开 RevHotUpdateDemo.unity 点 Play ③ 依次点 检查更新 → 执行更新 → 加载演示资源。";
                Debug.Log(summary);

                if (platform != "PC")
                {
                    string platformWarning =
                        $"[RevHotDemo] 当前构建目标是 {target}，产物装配在 {platform}/ 目录；" +
                        "但编辑器里运行的客户端平台恒为 PC（RevHotPlatform 带 !UNITY_EDITOR）—— " +
                        "想在编辑器里验证请把 Build Target 切回 Standalone（或在面板里设 PlatformOverride）。";
                    Debug.LogWarning(platformWarning);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // ============================================================
        // ② 打开本地 CDN 目录 / ③ 清空客户端本地热更目录 / ④ 打印 URL
        // ============================================================

        [MenuItem("Revolution.Tools/热更新/Demo/② 打开本地 CDN 目录", false, 22)]
        public static void OpenCdnFolder()
        {
            string root = LocalCdnRoot();
            Directory.CreateDirectory(root);
            EditorUtility.RevealInFinder(root);
        }

        [MenuItem("Revolution.Tools/热更新/Demo/③ 清空客户端本地热更目录（回到出包基线）", false, 23)]
        public static void ClearClientLocalRoot()
        {
            // 客户端把热更内容放在 {persistentDataPath}/RevHotUpdate 下（见 RevHotPlatform.DefaultLocalRoot）。
            // 删掉它 = "这台设备从没热更过" → 下一次加载自动回退到 StreamingAssets 首包（这正是覆盖式语义的好处：一行操作即可关掉热更）。
            string localRoot = Path.Combine(Application.persistentDataPath, "RevHotUpdate");
            if (Directory.Exists(localRoot) == false)
            {
                Debug.Log($"[RevHotDemo] 客户端本地热更目录不存在（本来就在出包基线）：{localRoot}");
                return;
            }

            string message = "将删除：\n" + localRoot + "\n\n删完之后，下一次加载会自动回退到 StreamingAssets 里的首包基线。\n（编辑器的 Play 模式与真机共用这个目录）";
            bool ok = EditorUtility.DisplayDialog("清空客户端本地热更目录", message, "删除", "取消");
            if (ok == false) return;

            Directory.Delete(localRoot, true);
            Debug.Log($"[RevHotDemo] 已删除 {localRoot} —— 现在回到出包基线了（再点“检查更新”会发现又要重新下一遍）。");
        }

        [MenuItem("Revolution.Tools/热更新/Demo/④ 打印本次要填的 URL（排错用）", false, 24)]
        public static void PrintUrls()
        {
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            string platform = ABBuildSetting.GetPlatformName(target);
            string appVersion = Application.version;
            string resVersion = appVersion + "." + ReadBuildNumber();

            string urls =
                "[RevHotDemo] 当前应填的地址：\n" +
                $"  面板 RemoteRoot ：http://127.0.0.1:{LocalCdnPort}\n" +
                $"  清单（编辑器里就会去拉）：{ComposeUrl(platform, appVersion, null, ManifestFileName)}\n" +
                $"  最新资源版本包目录      ：{ComposeUrl(platform, appVersion, resVersion, "bundles/" + DemoBundleName)}\n" +
                $"  CDN 根（本机）          ：{LocalCdnRoot()}\n" +
                $"  客户端落地目录          ：{Path.Combine(Application.persistentDataPath, "RevHotUpdate")}";
            Debug.Log(urls);
        }

        // ============================================================
        // 内部实现
        // ============================================================

        /// <summary>把打包产物按远端目录约定装配到本地 CDN；返回拷贝的内容文件数。</summary>
        private static int AssembleLocalCdn(ABBuildResult build, RevHotManifest manifest, string outputDir, string platform, string appVersion, string resVersion)
        {
            string manifestDir = Path.Combine(LocalCdnRoot(), platform, appVersion);
            string versionDir = Path.Combine(manifestDir, resVersion);
            string bundleDir = Path.Combine(versionDir, "bundles");

            Directory.CreateDirectory(bundleDir);

            // ---------- ① 内容：AB 包 ----------
            //   只拷清单里登记过的包（= 打包工具扫出来的那批，主包与 .manifest 都不算内容）。
            //   这样"CDN 上的东西"与"清单里写的东西"永远一致 —— 不一致就是线上事故的常见来源。
            int copied = 0;
            for (int i = 0; i < manifest.Bundles.Count; i++)
            {
                string name = manifest.Bundles[i].Name;
                string source = Path.Combine(outputDir, name);
                if (File.Exists(source) == false)
                {
                    Debug.LogWarning($"[RevHotDemo] 清单里的包在产物目录找不到（跳过）：{source}");
                    continue;
                }
                File.Copy(source, Path.Combine(bundleDir, name), true);
                copied++;
            }

            // ---------- ② 内容：映射表（与包同批更新 —— 否则"新增资源"永远加载不到） ----------
            string mapSource = Path.Combine(outputDir, RevHotManifestBuilder.ResMapFileName);
            if (File.Exists(mapSource))
            {
                File.Copy(mapSource, Path.Combine(versionDir, RevHotManifestBuilder.ResMapFileName), true);
                copied++;
            }
            else
            {
                Debug.LogWarning($"[RevHotDemo] 产物目录里没有 {RevHotManifestBuilder.ResMapFileName}（打包链应该会写它，检查一下）。");
            }

            // ---------- ③ 清单：**最后**写 ----------
            //   ★ 这条纪律和真实上传完全一样：清单是"新版本已就绪"的开关。
            //     先传内容、后传清单 → 玩家要么拿到旧版本、要么拿到完整的新版本，不会拿到"半新半旧"。
            File.Copy(Path.Combine(outputDir, ManifestFileName), Path.Combine(manifestDir, ManifestFileName), true);

            return copied;
        }

        /// <summary>
        /// 重写演示资源：构建序号 +1。
        /// ★ 为什么由工具改文件：热更要看到效果，前提是"包内容真的变了" —— 自动 +1 省掉手工改文件这一步。
        /// </summary>
        private static void WriteDemoDataFile(int buildNumber)
        {
            string text =
                "RevHotUpdate 演示数据 —— 这一整份内容会被打进 AB 包，热更更新的就是它\n" +
                "构建序号：" + buildNumber + "\n" +
                "打包时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n" +
                "说明：本文件由菜单 Revolution.Tools/热更新/Demo/① 生成 —— 每次都会把构建序号 +1，\n" +
                "      于是包内容发生变化，客户端才会真的下到新版本（真实项目里换成你自己的资源即可）。\n";

            File.WriteAllText(DemoDataAssetPath, text, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(DemoDataAssetPath);
        }

        /// <summary>
        /// 兜底确保演示资源带 AB 标记（正常情况写在 revhot_demo.txt.meta 里；
        /// 万一 meta 被 Unity 重建掉了，这里补一次，省得你对着"包是空的"发懵）。
        /// </summary>
        private static void EnsureBundleMark()
        {
            AssetImporter importer = AssetImporter.GetAtPath(DemoDataAssetPath);
            if (importer == null)
            {
                Debug.LogWarning($"[RevHotDemo] 找不到演示资源：{DemoDataAssetPath}（它应该随 demo 一起提交）。");
                return;
            }
            if (importer.assetBundleName == DemoBundleName) return;

            importer.assetBundleName = DemoBundleName;
            importer.SaveAndReimport();
            Debug.Log($"[RevHotDemo] 已给 {DemoDataAssetPath} 补上 AB 标记：{DemoBundleName}");
        }

        private static void ReportValidateErrors(ABValidateResult validate)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[RevHotDemo] 打包前校验没通过，已中止（这是好事：问题在打包前暴露，而不是出真机才炸）");
            for (int i = 0; i < validate.errors.Count; i++) sb.AppendLine("  ✗ " + validate.errors[i]);
            Debug.LogError(sb.ToString());
        }

        private static string LocalCdnRoot()
        {
            // Application.dataPath = <工程根>/Assets → 往上一层就是工程根
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "LocalCDN"));
        }

        /// <summary>
        /// 拼一个远端地址（与包内 RevHotUrlBuilder.Compose 同一套规则：跳过空段）。
        /// ★ 这里只是为了让日志里能打印"该去拉哪个 URL"，真跑起来拼 URL 的是包里的实现。
        /// </summary>
        private static string ComposeUrl(string platform, string appVersion, string resVersion, string lastSegment)
        {
            var sb = new StringBuilder();
            sb.Append("http://127.0.0.1:").Append(LocalCdnPort).Append('/');
            AppendSegment(sb, Env);
            AppendSegment(sb, platform);
            AppendSegment(sb, appVersion);
            AppendSegment(sb, Channel);
            AppendSegment(sb, resVersion);
            sb.Append(lastSegment);
            return sb.ToString();
        }

        private static void AppendSegment(StringBuilder sb, string segment)
        {
            if (string.IsNullOrEmpty(segment)) return;
            sb.Append(segment).Append('/');
        }

        private static int ReadBuildNumber()
        {
            try
            {
                if (File.Exists(StateFilePath))
                {
                    int parsed;
                    if (int.TryParse(File.ReadAllText(StateFilePath).Trim(), out parsed)) return parsed;
                }
            }
            catch (Exception)
            {
                // 状态文件坏了就当第一次跑（顶多是资源版本号回退，不影响功能）
            }
            return 0;
        }

        private static void WriteBuildNumber(int buildNumber)
        {
            string dir = Path.GetDirectoryName(StateFilePath);
            if (Directory.Exists(dir) == false) Directory.CreateDirectory(dir);
            File.WriteAllText(StateFilePath, buildNumber.ToString());
        }
    }
}
