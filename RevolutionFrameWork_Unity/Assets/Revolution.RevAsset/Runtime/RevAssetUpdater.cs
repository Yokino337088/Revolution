using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Revolution.RevAsset
{
    /// <summary>用于启动界面、脚本和自动化的只读更新结果。</summary>
    public sealed class RevAssetUpdateResult
    {
        /// <summary>频道指向的内容版本。</summary>
        public string Version { get; internal set; }

        /// <summary>本次已提交可用的整版安装。</summary>
        public bool Installed { get; internal set; }

        /// <summary>经过验证且可在首包或缓存中复用的对象数。</summary>
        public int ReusedObjects { get; internal set; }

        /// <summary>本次新增或修复的对象数。</summary>
        public int DownloadedObjects { get; internal set; }

        /// <summary>候选中需要新增或修复的对象总数。</summary>
        public int MissingObjects { get; internal set; }

        /// <summary>需要新增或修复的对象完整大小。续传时实际传输可以更少。</summary>
        public long MissingBytes { get; internal set; }

        /// <summary>总传输字节，包括下载重试。</summary>
        public long ReceivedBytes { get; internal set; }

        /// <summary>运行结果。失败、取消时有原因，已使用的内容版本可继续加载。</summary>
        public string Error { get; internal set; }

        /// <summary>当前操作已结束，回执中的状态不会继续变化。</summary>
        public bool Finished { get; internal set; }

        /// <summary>候选预下载或安装成功。</summary>
        public bool Success => Finished && string.IsNullOrEmpty(Error);
    }

    /// <summary>读取签名目录、计算缺包、按限额下载并提交完整版本。</summary>
    internal sealed class RevAssetUpdater
    {
        /// <summary>安装所在存储及首包。</summary>
        private readonly RevAssetStore _store;

        /// <summary>更新任务共享取消令牌，任一下载失败都会停止其他工人继续接新对象。</summary>
        private readonly RevCancellationToken _token;

        /// <summary>等待下载的唯一内容摘要列表。</summary>
        private readonly Queue<RevAssetBundleData> _missing = new Queue<RevAssetBundleData>();

        /// <summary>本次阶段的可读取回执。</summary>
        private readonly RevAssetUpdateResult _result;

        /// <summary>玩家或应用取消前，至少等在途下载走完自己的资源清理。</summary>
        private bool _failed;

        /// <summary>构造一次更新或按标签预下载任务。</summary>
        internal RevAssetUpdater(RevAssetStore store, RevCancellationToken token, RevAssetUpdateResult result)
        {
            _store = store;
            _token = token;
            _result = result;
        }

        /// <summary>读取目录，用源端摘要比较首包与缓存。install 为 true 时必须补全所有依赖再原子安装。</summary>
        internal async RevTask RunAsync(string source, IReadOnlyCollection<string> tags, bool install, int parallel, int attempts)
        {
            string root = RevAssetTransfer.ChannelRoot(source);
            byte[] pointer = await RevAssetTransfer.ReadAsync(root + "/" + RevAssetRelease.FileName, RevAssetRelease.MaxBytes, _token);
            RevAssetRelease release = RevAssetRelease.Read(pointer, _store.Settings, _store.Platform);
            _store.CheckRevision(release, _store.ReadState());
            byte[] bytes = await RevAssetTransfer.ReadAsync(root + "/" + RevAssetFiles.CatalogName(release.catalog), release.length, _token);
            RevAssetCatalog catalog = release.ReadCatalog(bytes);
            _result.Version = catalog.Version;

            RevAssetBundleData[] objects = catalog.SelectBundles(install ? null : tags)
                .GroupBy(bundle => bundle.hash, StringComparer.Ordinal).Select(group => group.First()).ToArray();
            string[] existing = await FindExistingAsync(objects);
            for (int i = 0; i < objects.Length; i++)
            {
                if (existing[i] == null)
                {
                    _missing.Enqueue(objects[i]);
                    _result.MissingObjects++;
                    _result.MissingBytes = checked(_result.MissingBytes + objects[i].size);
                }
                else
                {
                    _result.ReusedObjects++;
                }
            }

            _token?.ThrowIfCancelled();
            var workers = new RevTask[Math.Min(parallel, _missing.Count)];
            for (int i = 0; i < workers.Length; i++)
            {
                workers[i] = FetchNextAsync(root, attempts);
            }
            await RevTask.WhenAll(workers);
            _token?.ThrowIfCancelled();
            if (install)
            {
                _store.Commit(release, pointer, bytes, catalog);
                _result.Installed = true;
            }
        }

        /// <summary>哈希扫描不走 Unity API 的工作移到线程。主线程先解析所有本地候选路径。</summary>
        private async RevTask<string[]> FindExistingAsync(RevAssetBundleData[] objects)
        {
            string[] paths = objects.Select(bundle => _store.Resolve(bundle)).ToArray();
#if UNITY_WEBGL && !UNITY_EDITOR
            for (int i = 0; i < paths.Length; i++)
            {
                _token?.ThrowIfCancelled();
                VerifyObject(paths, objects, i);
                if ((i & 15) == 15)
                {
                    await RevTaskScheduler.NextFrame();
                }
            }
#else
            await System.Threading.Tasks.Task.Run(() =>
            {
                for (int i = 0; i < paths.Length; i++)
                {
                    _token?.ThrowIfCancelled();
                    VerifyObject(paths, objects, i);
                }
            });
#endif
            return paths;
        }

        /// <summary>当前候选文件存在但摘要不匹配时安排重新下载。</summary>
        private static void VerifyObject(string[] paths, RevAssetBundleData[] objects, int index)
        {
            string path = paths[index];
            if (path != null && !path.Contains("://") && !RevAssetFiles.MatchesFile(path, objects[index]))
            {
                paths[index] = null;
            }
        }

        /// <summary>主线程协作调度，以同一内容摘要为单位避免重复下载。异常交给总任务汇报。</summary>
        private async RevTask FetchNextAsync(string root, int attempts)
        {
            try
            {
                while (!_failed && _missing.Count > 0)
                {
                    _token?.ThrowIfCancelled();
                    RevAssetBundleData bundle = _missing.Dequeue();
                    await RevAssetTransfer.FetchAsync(root, _store.ObjectPath(bundle.hash), bundle, attempts, _token,
                        count => _result.ReceivedBytes += count);
                    _result.DownloadedObjects++;
                }
            }
            catch
            {
                _failed = true;
                throw;
            }
        }
    }
}
