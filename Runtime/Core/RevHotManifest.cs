// ============================================================
// RevHotManifest.cs —— 版本清单（整个热更系统的"唯一真相"）
//
// 位置：Assets\Revolution.HotUpdate\Runtime\Core\
//
// 【清单是什么】
//   一份由打包工具生成的文本（与 ResMap.txt 同族的行式格式），描述"这个大版本、这个平台、
//   这个资源版本里有哪些包、每个包的 hash/大小/依赖，以及映射表是哪个"。
//   玩家的客户端拿到它，才知道"我缺什么、该下什么"。
//
// 【为什么是行式文本，而不是 JSON】
//   ① 零依赖（不引 Newtonsoft，JsonUtility 又不支持字典）；
//   ② 出问题"记事本比一比"就能定位；
//   ③ 与框架的 ResMap.txt 同族 —— 一套心智，不记第二种格式。
//
// 【格式（# 是注释，空行忽略；@key|value 是头部；@bundle/@resmap 是条目）】
//   @appVersion|1.2.0
//   @resVersion|1.2.0.37
//   @bundleCount|6
//   @resmap|ResMap.txt|3f7a…|20481
//   @bundle|hero|5e6f…|15728640|common|hot
//
// 【易踩的坑（解析器都替你防了）】
//   · 首行 BOM（有些工具保存时带）→ 已剥；
//   · Windows 换行 \r\n → 已剥 \r；
//   · 未知 @key → 忽略（向前兼容：新版清单在旧客户端上最多"少用几个字段"，不会崩）；
//   · 数字段坏掉 → 整条报错（绝不能"尽量解析"，坏清单宁可失败并保持旧版本）。
// ============================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Revolution.HotUpdate
{
    /// <summary>一次热更的版本清单（解析结果 / 也可序列化回文本）。</summary>
    public sealed class RevHotManifest
    {
        /// <summary>大版本锚点（必须与客户端 Application.version 一致，否则走强更）。</summary>
        public string AppVersion = "";

        /// <summary>资源版本（同一大版本内自增，例如 1.2.0.37）。</summary>
        public string ResVersion = "";

        /// <summary>兼容下限：客户端包体版本低于它时不做热更、引导更新客户端（一般等于 AppVersion）。</summary>
        public string MinAppVersion = "";

        /// <summary>该大版本的基线清单文件名（随包体发布；差量计算与"回到出包状态"都靠它）。</summary>
        public string BaseManifestFile = "Baseline.txt";

        /// <summary>打包工具的版本字符串（给人 / CI 看，不参与逻辑）。</summary>
        public string Version = "";

        /// <summary>平台目录名（PC / Android / iOS / WebGL）。</summary>
        public string Platform = "";

        /// <summary>渠道段（可空）。</summary>
        public string Channel = "";

        /// <summary>环境段（可空）。</summary>
        public string Env = "";

        /// <summary>哈希算法名（sha256 / md5 …；目前实现按 sha256 处理）。</summary>
        public string HashAlgo = "sha256";

        /// <summary>生成时间（纯记录，给人看）。</summary>
        public string BuiltAt = "";

        /// <summary>全部 AB 包条目。</summary>
        public readonly List<RevHotBundleInfo> Bundles = new List<RevHotBundleInfo>();

        /// <summary>映射表（ResMap.txt）作为热更文件：名字 / hash / 大小。</summary>
        public RevHotFileInfo ResMap;

        /// <summary>按包名找条目（找不到返回 null）。</summary>
        public RevHotBundleInfo Find(string bundleName)
        {
            for (int i = 0; i < Bundles.Count; i++)
            {
                if (string.Equals(Bundles[i].Name, bundleName, StringComparison.OrdinalIgnoreCase)) return Bundles[i];
            }

            return null;
        }

        /// <summary>这一批包共多少字节（磁盘预检与进度条都靠它）。</summary>
        public long TotalBytes()
        {
            long total = 0;
            for (int i = 0; i < Bundles.Count; i++) total += Bundles[i].Size;
            if (ResMap != null) total += ResMap.Size;
            return total;
        }

        // ============================================================
        // 序列化（编辑器生成清单时用；运行时保存"本地清单副本"直接存原文即可）
        // ============================================================

        /// <summary>序列化成清单文本（字段顺序固定，便于 diff 与排查）。</summary>
        public string Serialize()
        {
            var sb = new StringBuilder(4096);
            sb.AppendLine("# RevHotManifest v1 —— 由 RevHotUpdate 编辑器工具生成，请勿手改");
            sb.AppendLine("@appVersion|" + NullToEmpty(AppVersion));
            sb.AppendLine("@resVersion|" + NullToEmpty(ResVersion));
            sb.AppendLine("@minAppVersion|" + NullToEmpty(MinAppVersion));
            sb.AppendLine("@baseManifest|" + NullToEmpty(BaseManifestFile));
            sb.AppendLine("@version|" + NullToEmpty(Version));
            sb.AppendLine("@platform|" + NullToEmpty(Platform));
            sb.AppendLine("@channel|" + NullToEmpty(Channel));
            sb.AppendLine("@env|" + NullToEmpty(Env));
            sb.AppendLine("@hashAlgo|" + NullToEmpty(HashAlgo));
            sb.AppendLine("@builtAt|" + NullToEmpty(BuiltAt));
            sb.AppendLine("@bundleCount|" + Bundles.Count);

            if (ResMap != null)
            {
                sb.AppendLine("@resmap|" + ResMap.Name + "|" + NullToEmpty(ResMap.Sha256) + "|" + ResMap.Size);
            }

            for (int i = 0; i < Bundles.Count; i++)
            {
                RevHotBundleInfo b = Bundles[i];
                sb.Append("@bundle|").Append(b.Name)
                  .Append('|').Append(NullToEmpty(b.Sha256))
                  .Append('|').Append(b.Size)
                  .Append('|').Append(string.Join(",", b.Dependencies.ToArray()))
                  .Append('|').Append(string.Join(",", b.Tags.ToArray()));
                if (!string.IsNullOrEmpty(b.UnityHash)) sb.Append('|').Append(b.UnityHash);
                if (!string.IsNullOrEmpty(b.UnityCrc)) sb.Append('|').Append(b.UnityCrc);
                sb.AppendLine();
            }

            return sb.ToString();
        }

        // ============================================================
        // 解析
        // ============================================================

        /// <summary>
        /// 解析清单文本；格式错误返回 null 并给出 error（人话）。
        /// ★ 绝不"尽量解析"：清单是版本的唯一真相，坏清单宁可失败（客户端保持旧版本继续玩）。
        /// </summary>
        public static RevHotManifest Parse(string text, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(text)) { error = "清单内容为空（文件不存在或下载到了半个文件）"; return null; }

            // ★ 剥 BOM：有些工具保存文本会带 BOM，不剥的话第一行的 key 会多出看不见的字符
            if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);

            RevHotManifest manifest = new RevHotManifest();
            int lineNumber = 0;

            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                lineNumber = i + 1;
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (!line.StartsWith("@")) continue;                       // 未来扩展的非 @ 行：忽略

                string[] fields = line.Split('|');
                string key = fields[0].Substring(1).Trim();                // 去掉开头的 '@'

                switch (key)
                {
                    case "appVersion": manifest.AppVersion = Value(fields); continue;
                    case "resVersion": manifest.ResVersion = Value(fields); continue;
                    case "minAppVersion": manifest.MinAppVersion = Value(fields); continue;
                    case "baseManifest": manifest.BaseManifestFile = Value(fields); continue;
                    case "version": manifest.Version = Value(fields); continue;
                    case "platform": manifest.Platform = Value(fields); continue;
                    case "channel": manifest.Channel = Value(fields); continue;
                    case "env": manifest.Env = Value(fields); continue;
                    case "hashAlgo": manifest.HashAlgo = Value(fields); continue;
                    case "builtAt": manifest.BuiltAt = Value(fields); continue;
                    case "bundleCount": continue;                          // 只是给人看的，行数即真相

                    case "resmap":
                    {
                        // @resmap|文件名|hash|字节数
                        if (fields.Length < 4) { error = ParseError(lineNumber, line, "resmap 行需要 4 段"); return null; }
                        manifest.ResMap = new RevHotFileInfo
                        {
                            Name = fields[1].Trim(),
                            Sha256 = fields[2].Trim(),
                            Size = ParseSize(fields[3], lineNumber, line, out error),
                        };
                        if (error != null) return null;
                        if (manifest.ResMap.Name.Length == 0) { error = ParseError(lineNumber, line, "resmap 文件名为空"); return null; }
                        continue;
                    }

                    case "bundle":
                    {
                        // @bundle|包名|hash|字节数|依赖|标签[|unityHash|unityCrc]
                        if (fields.Length < 6) { error = ParseError(lineNumber, line, "bundle 行需要 6 段"); return null; }

                        RevHotBundleInfo bundle = new RevHotBundleInfo
                        {
                            Name = fields[1].Trim(),
                            Sha256 = fields[2].Trim(),
                            Size = ParseSize(fields[3], lineNumber, line, out error),
                        };
                        if (error != null) return null;

                        if (bundle.Name.Length == 0) { error = ParseError(lineNumber, line, "包名为空"); return null; }

                        bundle.SetDependencies(fields[4]);
                        bundle.SetTags(fields[5]);

                        // 这两列只在小游戏 / WebGL 上需要（交给引擎做缓存与校验）；没有也不报错
                        if (fields.Length > 6) bundle.UnityHash = fields[6].Trim();
                        if (fields.Length > 7) bundle.UnityCrc = fields[7].Trim();

                        manifest.Bundles.Add(bundle);
                        continue;
                    }

                    default:
                        continue;                                          // 未知字段：忽略（向前兼容）
                }
            }

            // ---- 完整性自检：没有版本号 / 没有包，等于清单不可用 ----
            if (string.IsNullOrEmpty(manifest.ResVersion)) { error = "清单缺少 @resVersion（资源版本）"; return null; }
            if (string.IsNullOrEmpty(manifest.AppVersion)) { error = "清单缺少 @appVersion（大版本锚点）"; return null; }
            return manifest;
        }

        private static string Value(string[] fields)
        {
            return fields.Length > 1 ? fields[1].Trim() : string.Empty;
        }

        private static long ParseSize(string text, int lineNumber, string line, out string error)
        {
            error = null;
            long size;
            if (!long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out size) || size < 0)
            {
                error = ParseError(lineNumber, line, "字节数不是合法数字：「" + text + "」");
                return 0;
            }

            return size;
        }

        private static string ParseError(int lineNumber, string line, string reason)
        {
            return "清单第 " + lineNumber + " 行格式错误（" + reason + "）：" + line;
        }

        private static string NullToEmpty(string text)
        {
            return text ?? string.Empty;
        }
    }
}
