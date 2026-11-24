using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Revolution.RevAsset
{
    /// <summary>Unity 原生请求用于小清单与流式文件下载。临时文件始终在正式对象所在卷。</summary>
    internal static class RevAssetTransfer
    {
        /// <summary>新建一次不跟随重定向的请求，避免将已验证的频道换成其他来源。</summary>
        private static UnityWebRequest Request(string url)
        {
            return new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET) { redirectLimit = 0, timeout = 45 };
        }

        /// <summary>校验发布目录。开发态也接受本机回环 HTTP 和 file 目录用于真实包验收。</summary>
        internal static string ChannelRoot(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri) || !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            {
                throw new InvalidDataException("RevAsset 发布目录 URL 无效");
            }
#if UNITY_EDITOR
            if (uri.IsFile || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            {
                return uri.AbsoluteUri.TrimEnd('/');
            }
#endif
            if (uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidDataException("发布频道需要 HTTPS");
            }
            return uri.AbsoluteUri.TrimEnd('/');
        }

        /// <summary>在最多 limit 字节内读取频道或清单。超过上限会中止请求。</summary>
        internal static async RevTask<byte[]> ReadAsync(string url, int limit, RevCancellationToken token)
        {
#if UNITY_EDITOR
            if (new Uri(url).IsFile)
            {
                return RevAssetFiles.ReadBounded(new Uri(url).LocalPath, limit);
            }
#endif
            using (UnityWebRequest request = Request(url))
            {
                var handler = new LimitedData(limit);
                request.downloadHandler = handler;
                await WaitAsync(request, limit, token);
                if (request.result != UnityWebRequest.Result.Success || handler.Overflowed)
                {
                    throw new IOException("读取发布文件失败，HTTP " + request.responseCode + " / " + request.result);
                }
                return handler.Read();
            }
        }

        /// <summary>从同一频道补齐一个缺失对象。校验成功后以摘要名提交，可断点复用未完成文件。</summary>
        internal static async RevTask FetchAsync(string root, string path, RevAssetBundleData bundle,
            int attempts, RevCancellationToken token, Action<long> progress)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string part = path + ".part";
            string url = root + "/" + RevAssetFiles.ObjectName(bundle.hash);
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                token?.ThrowIfCancelled();
                if (await MatchesAsync(part, bundle))
                {
                    token?.ThrowIfCancelled();
                    RevAssetFiles.PublishFile(part, path);
                    return;
                }
                long offset = File.Exists(part) ? new FileInfo(part).Length : 0;
                if (offset >= bundle.size)
                {
                    File.Delete(part);
                    offset = 0;
                }

                bool transferred;
#if UNITY_EDITOR
                if (new Uri(url).IsFile)
                {
                    // 编辑器试发目录走相同的 SHA-256 和提交路径。
                    File.Copy(new Uri(url).LocalPath, part, true);
                    progress?.Invoke(bundle.size);
                    transferred = true;
                }
                else
#endif
                {
                    transferred = await DownloadAsync(url, part, offset, bundle.size, token, progress);
                }
                if (transferred && await MatchesAsync(part, bundle))
                {
                    token?.ThrowIfCancelled();
                    RevAssetFiles.PublishFile(part, path);
                    return;
                }
                if (File.Exists(part) && new FileInfo(part).Length >= bundle.size)
                {
                    File.Delete(part);
                }
                if (attempt + 1 < attempts)
                {
                    await RevTask.Delay((attempt + 1) * 200);
                }
            }
            throw new IOException("资源对象无法下载或 SHA-256 不一致：" + bundle.hash);
        }

        /// <summary>逐块哈希在支持线程的平台移出主线程，浏览器使用同一验证规则。</summary>
        private static async RevTask<bool> MatchesAsync(string path, RevAssetBundleData bundle)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            await RevTaskScheduler.NextFrame();
            return RevAssetFiles.MatchesFile(path, bundle);
#else
            return await System.Threading.Tasks.Task.Run(() => RevAssetFiles.MatchesFile(path, bundle));
#endif
        }

        /// <summary>要求 Range 响应与续传请求一致，协议不符时仅撤回无效临时文件。</summary>
        private static async RevTask<bool> DownloadAsync(string url, string part, long offset, long length,
            RevCancellationToken token, Action<long> progress)
        {
            bool discard = false;
            try
            {
                using (UnityWebRequest request = Request(url))
                {
                    request.downloadHandler = new DownloadHandlerFile(part, offset > 0) { removeFileOnAbort = false };
                    if (offset > 0)
                    {
                        request.SetRequestHeader("Range", "bytes=" + offset.ToString(CultureInfo.InvariantCulture) + "-");
                    }
                    try
                    {
                        await WaitAsync(request, length - offset, token);
                    }
                    finally
                    {
                        progress?.Invoke((long)request.downloadedBytes);
                        discard = offset > 0 && request.responseCode != 0 &&
                            !ValidRange(request.responseCode, request.GetResponseHeader("Content-Range"), offset, length);
                    }
                    return !discard && request.result == UnityWebRequest.Result.Success;
                }
            }
            finally
            {
                if (discard)
                {
                    File.Delete(part);
                }
            }
        }

        /// <summary>逐帧观察大小、取消和实际时间。取消时保留可继续验证的部分文件。</summary>
        private static async RevTask WaitAsync(UnityWebRequest request, long limit, RevCancellationToken token)
        {
            token?.ThrowIfCancelled();
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            float start = Time.realtimeSinceStartup;
            while (!operation.isDone)
            {
                if (token?.IsCancelled == true || request.downloadedBytes > (ulong)limit ||
                    Time.realtimeSinceStartup - start > 45f)
                {
                    request.Abort();
                    token?.ThrowIfCancelled();
                    break;
                }
                await RevTaskScheduler.NextFrame();
            }
            token?.ThrowIfCancelled();
        }

        /// <summary>对照服务器的续传起点、终点和对象总长。</summary>
        private static bool ValidRange(long status, string range, long offset, long length)
        {
            return status == 206 && string.Equals(range, FormattableString.Invariant($"bytes {offset}-{length - 1}/{length}"),
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>小文件按块进入有上限的缓冲区，防止入口 URL 将响应无限放大。</summary>
        private sealed class LimitedData : DownloadHandlerScript
        {
            /// <summary>小文件正文缓冲。</summary>
            private readonly MemoryStream _body = new MemoryStream();

            /// <summary>允许的最大响应。</summary>
            private readonly int _limit;

            /// <summary>超过声明上限后停止接收。</summary>
            internal bool Overflowed { get; private set; }

            /// <summary>底层每次复用 32 KiB 回调缓冲。</summary>
            internal LimitedData(int limit) : base(new byte[32768])
            {
                _limit = limit;
            }

            /// <summary>只接收上限内的数据块。返回 false 时 Unity 终止响应。</summary>
            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (dataLength <= 0)
                {
                    return true;
                }
                if (_body.Length + dataLength > _limit)
                {
                    Overflowed = true;
                    return false;
                }
                _body.Write(data, 0, dataLength);
                return true;
            }

            /// <summary>返回已经读完的正文，供签名和目录校验。</summary>
            internal byte[] Read() => _body.ToArray();
        }
    }
}
