// ============================================================
// RevUIDemoLog.cs —— 演示用的极简共享日志（面板往里写，场景入口显示）
//
// 位置：Assets\Revolution.Demo\RevUISystem.Demo\
//
// 【为什么需要它】
//   使用说明里有好些场景（生命周期回调谁先谁后、被盖住 / 恢复、数据变化……）
//   发生在**面板内部**，而看的人盯的是场景入口那块界面。
//   所以面板把发生的事写进这里，入口面板再统一显示出来 —— 一眼就能对上"谁先谁后"。
//   真实项目里直接用 RevLog / 自己的日志系统即可，这只是一块演示用的白板。
//
// 【注意】入口面板每帧读 Version：变了才重排文本（不做无谓的字符串拼接）。
// ============================================================
using System.Collections.Generic;
using UnityEngine;

namespace Revolution.Demo.UI
{
    /// <summary>演示用共享日志（环形缓冲 + 版本号；线程不安全，只在主线程用）。</summary>
    public static class RevUIDemoLog
    {
        private const int MaxLines = 300;

        private static readonly List<string> _lines = new List<string>(MaxLines);

        /// <summary>内容变化就 +1 —— 界面据此判断要不要刷新（避免每帧拼字符串）。</summary>
        public static int Version { get; private set; }

        /// <summary>带时间戳的一行行记录（最新在最后）。</summary>
        public static List<string> Lines => _lines;

        /// <summary>记一条。who 一般是面板/Part 的显示名。</summary>
        public static void Add(string who, string what)
        {
            _lines.Add($"[{Time.realtimeSinceStartup:F2}s] {who} · {what}");

            if (_lines.Count > MaxLines) _lines.RemoveRange(0, _lines.Count - MaxLines);
            Version++;
        }

        /// <summary>清空（入口面板的按钮用）。</summary>
        public static void Clear()
        {
            _lines.Clear();
            Version++;
        }
    }
}
