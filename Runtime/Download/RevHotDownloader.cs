// ============================================================
// RevHotDownloader.cs —— 下载引擎（并发 / 重试退避 / 多源降级 / 断点续传 / 校验 / 原子落地）
//
// 位置：Assets\Revolution.HotUpdate\Runtime\Download\
//
// 【失败处理的三层结构】（从里到外）
//   TryDownloadOnceAsync ：一次尝试 = 断点续传下载 → 校验 → 原子提交；
//                          失败抛 RevHotException，其中"校验失败"会先删掉半成品并标记为可重试。
//   DownloadOneAsync     ：一个文件 = 换源（外层）× 重试退避（内层）；换源前删 .part（不同源内容可能不一致）。
//   DownloadPlanAsync    ：整批 = N 个工人从共享游标领任务；任何失败 → 停止领新任务，等在跑的收尾后统一上抛。
//
// 【为什么"取消"要单独处理】
//   RevOperationCanceledException 是控制流：取消时既不能重试也不能换源，必须立刻向上传递
//   （并把 www 中止掉，否则下载会在后台继续烧流量）。
// ============================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Revolution;
using UnityEngine;
using UnityEngine.Networking;

namespace Revolution.HotUpdate
{
    /// <summary>下载引擎（纯静态：状态都在会话对象里，方便一次一清）。</summary>
    internal static class RevHotDownloader
    {
        // ============================================================
        // 对外一：拉取小文本（清单 / 内置清单）。多源 + 重试，返回文本。
        // ============================================================

        /// <summary>
        /// 拉取一段文本。urlBuilder 负责"给定源 → 拼出该源上的 URL"（换源重试时会对每个源都拼一次）。
        /// 全部失败抛 RevHotException(ManifestFetchFailed, 可重试)。
        /// </summary>
        public static async RevTask<string> FetchTextAsync(RevHotConfig config, Func<RevHotSource, string> urlBuilder, RevCancellationToken token)
        {
            List<RevHotSource> sources = RevHotSources.Build(config);
            if (sources.Count == 0)
            {
                throw new RevHotException(RevHotError.Of(RevHotErrorCode.ConfigInvalid, "没有可用的下载源（RemoteRoot 未配置）"), false);
            }

            RevHotException last = null;
            for (int s = 0; s < sources.Count; s++)
            {
                string url = urlBuilder(sources[s]);

                for (int attempt = 0; attempt <= config.RetryCount; attempt++)
                {
                    token.ThrowIfCancelled();
                    try
                    {
                        return await FetchTextOnceAsync(url, config, token);
                    }
                    catch (RevOperationCanceledException)
                    {
                        throw;
                    }
                    catch (RevHotException e) when (e.Retryable == false)
                    {
                        throw;
                    }
                    catch (RevHotException e)
                    {
                        last = e;
                        RevLog.Warn("清单拉取失败（" + sources[s] + "，第 " + (attempt + 1) + " 次）：" + e.Error.Message, config.LogTag);
                        await BackoffDelayAsync(BackoffMs(config, attempt), token);
                    }
                }

                RevLog.Warn("下载源不可用，切换下一个：" + sources[s], config.LogTag);
            }

            throw last ?? new RevHotException(RevHotError.Of(RevHotErrorCode.ManifestFetchFailed, "所有下载源都失败了（检查网络与 RemoteRoot 配置）"), true);
        }

        private static async RevTask<string> FetchTextOnceAsync(string url, RevHotConfig config, RevCancellationToken token)
        {
            using (UnityWebRequest www = UnityWebRequest.Get(url))
            {
                ApplyHeaders(www, config);
                www.timeout = config.TimeoutSeconds;

                var op = www.SendWebRequest();
                while (op.isDone == false)
                {
                    token.ThrowIfCancelled();
                    await RevTask.Yield();
                }

                if (www.result != UnityWebRequest.Result.Success)
                {
                    throw new RevHotException(RevHotError.Of(RevHotErrorCode.ManifestFetchFailed, "清单拉取失败（" + www.error + "）", url), true);
                }

                string text = www.downloadHandler.text;
                if (string.IsNullOrEmpty(text))
                {
                    // 拿到空内容多半是"上传到一半的清单"——按可重试处理，换源 / 重试都可能拿到完整的
                    throw new RevHotException(RevHotError.Of(RevHotErrorCode.ManifestFetchFailed, "清单内容为空", url), true);
                }

                return text;
            }
        }

        // ============================================================
        // 对外二：按差量计划下载全部文件
        // ============================================================

        /// <summary>
        /// 下载一个差量计划里的全部文件（并校验、原子落地）。
        /// 任何一个文件最终失败 → 抛 RevHotException；已完成的文件保留（下次从计划重算，hash 一致的自然跳过）。
        /// </summary>
        public static async RevTask DownloadPlanAsync(RevHotPlan plan, RevHotConfig config, string resVersion, Action<RevHotProgress> onProgress, RevCancellationToken token)
        {
            List<WorkItem> items = BuildItems(plan, resVersion);
            if (items.Count == 0) return;

            var aggregator = new RevHotProgressAggregator(RevHotState.Downloading, plan.TotalBytes, items.Count, onProgress, config.LogTag);

            var session = new DownloadSession(items);
            int workers = Math.Min(Math.Max(config.Concurrency, 1), items.Count);

            var tasks = new RevTask[workers];
            for (int i = 0; i < workers; i++)
            {
                tasks[i] = WorkerLoopAsync(session, config, resVersion, aggregator, token);
            }

            await RevTask.WhenAll(tasks);

            // ★ 取消必须在这里显式上抛：WhenAll 只计数完成、不看异常 —— 工人抛出的
            //   RevOperationCanceledException 到这里已经丢了。不补抛的话，"取消下载"会
            //   被当成"更新成功"继续走完 stamp / 切版本，半截版本直接上线。
            if (session.Cancelled) throw new RevOperationCanceledException();

            // 全部工人结束后：有失败就上抛（第一个错误最有诊断价值）
            if (session.FirstError != null) throw session.FirstError;
        }

        // ============================================================
        // 工人与单文件
        // ============================================================

        /// <summary>共享任务队列：工人从游标领任务；一旦有失败，停止领新任务（做完手头的就收工）。</summary>
        private sealed class DownloadSession
        {
            public readonly List<WorkItem> Items;
            public readonly object Gate = new object();
            public RevHotException FirstError;
            public bool Stop;

            /// <summary>
            /// 取消专用标志（与 Stop 分开：Stop 复用于"失败也停"，
            /// 聚合处要区分"该报失败"还是"该报取消"—— RevTask 的 WhenAll 不传播异常，取消只能靠它带出来）。
            /// </summary>
            public bool Cancelled;

            private int _cursor;

            public DownloadSession(List<WorkItem> items)
            {
                Items = items;
            }

            /// <summary>领下一个任务（没有 / 已失败停止 → null）。</summary>
            public WorkItem Next()
            {
                lock (Gate)
                {
                    if (Stop || _cursor >= Items.Count) return null;
                    WorkItem item = Items[_cursor];
                    _cursor++;
                    return item;
                }
            }

            /// <summary>记录失败（只记第一个：它最有诊断价值）。</summary>
            public void Fail(RevHotException error)
            {
                lock (Gate)
                {
                    Stop = true;
                    if (FirstError == null) FirstError = error;
                }
            }
        }

        /// <summary>一个待下载文件（包或映射表）。</summary>
        private sealed class WorkItem
        {
            public bool IsResMap;
            public string Name;
            public long Size;
            public string Sha256;
            public string LocalPath;

            /// <summary>
            /// 用构造参数填字段（而不是对象初始化块 `new WorkItem { ... }`）：
            /// 这样调用处能写成一行，避免"括号后面换行"。
            /// </summary>
            public WorkItem(bool isResMap, string name, long size, string sha256, string localPath)
            {
                IsResMap = isResMap;
                Name = name;
                Size = size;
                Sha256 = sha256;
                LocalPath = localPath;
            }
        }

        private static List<WorkItem> BuildItems(RevHotPlan plan, string resVersion)
        {
            // 本地路径必须与 RevHotStore 的目录约定严格一致（直接用它的方法算，避免两处拼路径出现分歧）
            var items = new List<WorkItem>(plan.FileCount);

            Action<RevHotBundleInfo> addBundle = delegate(RevHotBundleInfo bundle)
            {
                string localPath = Path.Combine(RevHotStore.BundlesDir(resVersion), bundle.Name);
                items.Add(new WorkItem(false, bundle.Name, bundle.Size, bundle.Sha256, localPath));
            };

            for (int i = 0; i < plan.Added.Count; i++) addBundle(plan.Added[i]);
            for (int i = 0; i < plan.Changed.Count; i++) addBundle(plan.Changed[i]);

            if (plan.ResMapChanged)
            {
                items.Add(new WorkItem(true, plan.ResMap.Name, plan.ResMap.Size, plan.ResMap.Sha256, RevHotStore.ResMapPath(resVersion)));
            }

            return items;
        }

        private static async RevTask WorkerLoopAsync(DownloadSession session, RevHotConfig config, string resVersion, RevHotProgressAggregator progress, RevCancellationToken token)
        {
            while (true)
            {
                if (session.Stop) return;

                WorkItem item = session.Next();
                if (item == null) return;

                try
                {
                    await DownloadOneAsync(item, config, resVersion, progress, token);
                    progress.CompleteFile(item.Name, item.Size);
                }
                catch (RevOperationCanceledException)
                {
                    session.Cancelled = true;                  // ★ 先记账：这个异常到 WhenAll 聚合处会被吞，只能靠标志带出去
                    session.Stop = true;                       // 取消：让别的工人也别再领新任务
                    throw;
                }
                catch (RevHotException e)
                {
                    // 记失败 + 停止领新任务：继续下载剩下的没有意义（玩家看到的将是"更新失败，可重试"）
                    session.Fail(e);
                }
            }
        }

        private static async RevTask DownloadOneAsync(WorkItem item, RevHotConfig config, string resVersion, RevHotProgressAggregator progress, RevCancellationToken token)
        {
            progress.BeginFile(item.Name);

            List<RevHotSource> sources = RevHotSources.Build(config);
            RevHotException last = null;

            for (int s = 0; s < sources.Count; s++)
            {
                // ★ 换源前删掉半成品：不同源的内容不能拼在一起（校验必挂）
                RevHotStore.DeleteFile(RevHotStore.PartPath(item.LocalPath));

                for (int attempt = 0; attempt <= config.RetryCount; attempt++)
                {
                    token.ThrowIfCancelled();
                    try
                    {
                        string url = item.IsResMap
                            ? RevHotUrlBuilder.ResMapUrl(config, sources[s].Root, resVersion)
                            : RevHotUrlBuilder.BundleUrl(config, sources[s].Root, resVersion, item.Name);

                        await TryDownloadOnceAsync(item, url, config, progress, token);
                        return;                                        // 成功
                    }
                    catch (RevOperationCanceledException)
                    {
                        throw;
                    }
                    catch (RevHotException e) when (e.Retryable == false)
                    {
                        throw;                                         // 磁盘满这类：重试没有意义
                    }
                    catch (RevHotException e)
                    {
                        last = e;
                        RevLog.Warn(item.Name + " 下载失败（" + sources[s] + "，第 " + (attempt + 1) + " 次）：" + e.Error.Message, config.LogTag);
                        await BackoffDelayAsync(BackoffMs(config, attempt), token);   // 指数退避：给 CDN / 网络喘口气（可取消）
                    }
                }

                RevLog.Warn(item.Name + " 在 " + sources[s] + " 全部重试失败，切换下一个源", config.LogTag);
            }

            throw last ?? new RevHotException(RevHotError.Of(RevHotErrorCode.DownloadFailed, item.Name + " 下载失败（所有下载源都已尝试）"), true);
        }

        /// <summary>一次尝试：断点续传下载 → 校验（失败删半成品并标记可重试）→ 原子提交。</summary>
        private static async RevTask TryDownloadOnceAsync(WorkItem item, string url, RevHotConfig config, RevHotProgressAggregator progress, RevCancellationToken token)
        {
            string partPath = RevHotStore.PartPath(item.LocalPath);

            // DownloadHandlerFile 不会自己建目录 —— 先把目录建出来
            string dir = Path.GetDirectoryName(item.LocalPath);
            if (string.IsNullOrEmpty(dir) == false) Directory.CreateDirectory(dir);

            long offset = RevHotStore.ExistingLength(partPath);
            if (offset > item.Size)
            {
                // 半成品比目标还大：内容已经没救了（多半是换过版本），从头再来
                RevHotStore.DeleteFile(partPath);
                offset = 0;
            }

            using (UnityWebRequest www = UnityWebRequest.Get(url))
            {
                ApplyHeaders(www, config);
                www.timeout = config.TimeoutSeconds;

                if (offset > 0)
                {
                    // 断点续传：从半成品的长度继续要
                    www.SetRequestHeader("Range", "bytes=" + offset + "-");
                }

                // ★ 追加写：offset > 0 时从 .part 的末尾继续写
                www.downloadHandler = new DownloadHandlerFile(partPath, offset > 0);

                var op = www.SendWebRequest();
                while (op.isDone == false)
                {
                    token.ThrowIfCancelled();
                    progress.SetFileBytes(item.Name, offset + (long)www.downloadedBytes);
                    await RevTask.Yield();
                }

                if (www.result != UnityWebRequest.Result.Success)
                {
                    throw new RevHotException(RevHotError.Of(RevHotErrorCode.DownloadFailed, item.Name + " 下载失败（" + www.error + "）", url), true);
                }

                // ★ 服务器不支持 Range 时会无视请求头、返回 200 + 整份内容：
                //   此时我们却在"追加写"，文件必然损坏 —— 删掉 .part，从头重试。
                if (offset > 0 && www.responseCode != 206)
                {
                    RevHotStore.DeleteFile(partPath);
                    string reason = item.Name + " 下载源不支持断点续传，已回退为重新下载";   // 消息先拼好，抛出语句就能写成单行
                    throw new RevHotException(RevHotError.Of(RevHotErrorCode.DownloadFailed, reason, url), true);
                }
            }

            // ---------- 校验（尺寸 + hash）。失败 → 删半成品 → 标记可重试（重试即从头下载） ----------
            try
            {
                await RevHotVerifier.VerifyAsync(partPath, item.Size, item.Sha256, config.VerifyMode, config, token);
            }
            catch (RevHotException e)
            {
                if (e.Error.Code == RevHotErrorCode.VerifyFailed)
                {
                    RevHotStore.DeleteFile(partPath);
                    throw new RevHotException(e.Error, true);
                }

                throw;
            }

            RevHotStore.CommitFile(partPath, item.LocalPath);
        }

        // ============================================================
        // 小工具
        // ============================================================

        /// <summary>把配置里的附加请求头套到请求上（临时令牌等）。</summary>
        private static void ApplyHeaders(UnityWebRequest www, RevHotConfig config)
        {
            if (config.ExtraHeaders == null) return;
            foreach (KeyValuePair<string, string> kv in config.ExtraHeaders)
            {
                if (string.IsNullOrEmpty(kv.Key)) continue;
                www.SetRequestHeader(kv.Key, kv.Value ?? string.Empty);
            }
        }

        /// <summary>单次退避的硬上限（配置值异常大时也最多等这么久）。</summary>
        private const int MaxBackoffMs = 30_000;

        /// <summary>指数退避：500ms → 1s → 2s → 4s（封顶左移 4 位，避免重试几十次后等几十秒）。</summary>
        private static int BackoffMs(RevHotConfig config, int attempt)
        {
            // 先升 long 再移位：int 移位在基数大时会把符号位移成负数（等待时间变 0 甚至更糟）
            long wait = (long)config.RetryBackoffMs << Math.Min(attempt, 4);
            if (wait > MaxBackoffMs) wait = MaxBackoffMs;
            return wait < 0 ? 0 : (int)wait;
        }

        /// <summary>
        /// 可取消的退避等待。★ 不用 RevTask.Delay：它不带取消令牌，玩家在退避期间取消，
        /// 还要白等满一次退避时长才响应 —— 改成逐帧检查，取消立刻生效。
        /// </summary>
        private static async RevTask BackoffDelayAsync(int milliseconds, RevCancellationToken token)
        {
            if (milliseconds <= 0) return;
            Stopwatch clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < milliseconds)
            {
                token.ThrowIfCancelled();
                await RevTask.Yield();
            }
        }
    }
}
