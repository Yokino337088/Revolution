// RevHotDemoAutomation.cs —— 热更演示工具的无人值守入口，仍复用原打包链与目录约定。
using System;
using System.IO;
using Revolution.Editor;
using UnityEditor;

namespace Revolution.Demo.HotUpdate
{
    public static class RevHotDemoAutomation
    {
        [RevAutomationAction("hot.demo-prepare", Keys = new string[0], Description = "装配热更 Demo、构建产物和本地 CDN", Confirm = true)]
        public static object Prepare(RevAutomationRequest request)
        {
            if (!request.confirm) { throw new InvalidOperationException("演示准备会更新演示资源、版本及打包产物，需要 --confirm"); }
            RevHotUpdateDemoTools.PrepareAll();
            return new RevAutomationActions.RevAutomationText(RevHotUpdateDemoTools.DescribeUrls());
        }

        [RevAutomationAction("hot.demo-clear", Keys = new string[0], Description = "清理热更 Demo 客户端缓存", Confirm = true)]
        public static object Clear(RevAutomationRequest request)
        {
            if (!request.confirm) { throw new InvalidOperationException("删除客户端热更缓存需要 --confirm"); }
            return new RevAutomationActions.RevAutomationText(RevHotUpdateDemoTools.DeleteClientCache());
        }

        [RevAutomationAction("hot.demo-urls", Keys = new string[0], Description = "查询热更 Demo URL", Confirm = false)]
        public static object Urls(RevAutomationRequest request)
        {
            return new RevAutomationActions.RevAutomationText(RevHotUpdateDemoTools.DescribeUrls());
        }

        [RevAutomationAction("hot.demo-open", Keys = new string[0], Description = "显示本地 CDN 目录", Confirm = false)]
        public static object Open(RevAutomationRequest request)
        {
            string root = Path.GetFullPath("LocalCDN");
            Directory.CreateDirectory(root);
            if (!UnityEngine.Application.isBatchMode) { EditorUtility.RevealInFinder(root); }
            return new RevAutomationActions.RevAutomationText(root);
        }
    }
}
