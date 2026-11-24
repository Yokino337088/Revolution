#if UNITY_EDITOR || DEVELOPMENT_BUILD || REV_AUTOMATION
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Revolution.RevAsset
{
    /// <summary>开发态 RevCLI 与 RevMCP 使用的运行时动作。发行包无需运行时反射。</summary>
    internal static class RevAssetDiagnostics
    {
        /// <summary>更新或按标签预下载的开发态参数。</summary>
        [Serializable]
        private sealed class Input
        {
            /// <summary>临时发布目录，缺省使用项目配置。</summary>
            public string channel;
            /// <summary>非空时只预下载这些标签。</summary>
            public string[] tags;
        }

        /// <summary>登记可发现的版本查询、更新、激活、回滚和缓存清理入口。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            const string empty = "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}";
            const string update = "{\"type\":\"object\",\"properties\":{\"channel\":{\"type\":\"string\"},\"tags\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}},\"additionalProperties\":false}";
            RevRuntimeAutomation.Register("revasset.status", "查询真实生效版本及下载回执", empty, _ => Snapshot(), readOnly: true);
            RevRuntimeAutomation.Register("revasset.download", "请求安装下一版或按标签预下载资源", update, Download);
            RevRuntimeAutomation.Register("revasset.cancel", "取消正在运行的更新", empty, _ =>
            {
                RevAsset.CancelUpdate();
                return Snapshot();
            });
            RevRuntimeAutomation.Register("revasset.activate", "只在资源租约释放后装配已安装版本", empty, _ =>
                new Dictionary<string, object> { ["activated"] = RevAsset.TryActivateInstalled(), ["mounted"] = RevAsset.MountedVersion });
            RevRuntimeAutomation.Register("revasset.rollback", "将安装指针回滚到上一个完整版本", empty, _ =>
                new Dictionary<string, object> { ["installed"] = RevAsset.RollbackInstalled(), ["mounted"] = RevAsset.MountedVersion });
            RevRuntimeAutomation.Register("revasset.clear-cache", "回收不属于保留版本的过期制品", empty, _ =>
                new Dictionary<string, object> { ["removedBytes"] = RevAsset.ClearObsoleteCache() });
        }

        /// <summary>发起一次任务，后续由状态动作查询实际终态。</summary>
        private static object Download(string json)
        {
            if (RevAsset.Updating || RevResourceBackends.ActiveId != RevAsset.BackendId || RevAsset.MountedVersion == "editor")
            {
                return new Dictionary<string, object> { ["accepted"] = false };
            }
            var input = JsonUtility.FromJson<Input>(json);
            if (input.tags?.Length > 0)
            {
                RevAsset.PrefetchAsync(input.tags, input.channel).Forget();
            }
            else
            {
                RevAsset.UpdateAsync(input.channel).Forget();
            }
            return new Dictionary<string, object> { ["accepted"] = true };
        }

        /// <summary>返回任务读取量、失败原因和当前资源版本。</summary>
        private static object Snapshot()
        {
            RevAssetUpdateResult result = RevAsset.LastUpdate;
            return new Dictionary<string, object>
            {
                ["backend"] = RevResourceBackends.ActiveId,
                ["mounted"] = RevAsset.MountedVersion,
                ["updating"] = RevAsset.Updating,
                ["candidate"] = result?.Version,
                ["installed"] = result?.Installed ?? false,
                ["error"] = result?.Error,
                ["downloadedObjects"] = result?.DownloadedObjects ?? 0,
                ["reusedObjects"] = result?.ReusedObjects ?? 0,
                ["missingObjects"] = result?.MissingObjects ?? 0,
                ["missingBytes"] = result?.MissingBytes ?? 0L,
                ["receivedBytes"] = result?.ReceivedBytes ?? 0L
            };
        }
    }
}
#endif
