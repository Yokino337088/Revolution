// 自动化门面的性能契约：不依赖Editor，面板和MCP读取相同采样器。正式包需显式REV_AUTOMATION。
#if UNITY_EDITOR || DEVELOPMENT_BUILD || REV_AUTOMATION
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting;

namespace Revolution
{
    /// <summary>计数器原生单位由unit标明；缺失值为-1，不能解释成零成本。</summary>
    [Serializable, Preserve]
    public sealed class RevPerformanceMetric
    {
        public string name, unit;
        public bool available;
        public double value = -1;
    }

    /// <summary>最近最多2048帧的窗口快照。FPS来自帧数/墙钟秒，非instantaneous FPS平均。</summary>
    [Serializable, Preserve]
    public sealed class RevPerformanceSnapshot
    {
        public string product, buildId, unityVersion, platform, backend, cpu, gpu, graphicsApi, resolution;
        public bool editor, development, focused, runInBackground, enabled;
        public int frames, targetFrameRate, vSyncCount;
        public double seconds, fps, p50Ms, p95Ms, p99Ms, maxMs, gpuMs = -1;
        public RevPerformanceMetric[] metrics;
    }

    /// <summary>一次固定时长作业；结束后才有分位数，success不代表达到任何项目性能预算。</summary>
    [Serializable, Preserve]
    public sealed class RevPerformanceReport
    {
        public string runId, status = "idle", error, startedUtc, endedUtc;
        public bool success, cancelled;
        /// <summary>终态数据全部发布后为 true，失败和取消也可结束客户端的等待。</summary>
        public bool finished;
        /// <summary>安卓测量在预热前采到的进程和系统指标。未启用时为空。</summary>
        public Dictionary<string, object> androidBefore;
        /// <summary>安卓测量在计时截止后采到的进程和系统指标。未启用时为空。</summary>
        public Dictionary<string, object> androidAfter;
        public int frames, gpuSamples, unfocusedFrames, errors;
        /// <summary>驱动返回非有限值、超过进程寿命或跨出整段计时窗的 GPU 样本数，不计入分位数。</summary>
        public int gpuRejectedSamples;
        public double requestedSeconds, warmupSeconds, seconds, fps, p50Ms, p95Ms, p99Ms, maxMs;
        public double gpuP50Ms = -1, gpuP95Ms = -1, over125Ratio, over167Ratio, over333Ratio;
        public RevPerformanceSnapshot environment;
        public RevPerformanceMetric[] averages;
    }

    public static partial class RevRuntimeAutomation
    {
        /// <summary>启动常驻轻量采样，不写盘、不启动Profiler录制；重复调用复用同一宿主。</summary>
        public static void EnablePerformance()
        {
            MainThread();
            RevRuntimePerformanceSampler.Ensure().KeepRolling();
        }

        /// <summary>返回窗口快照，调用时才排序/装配DTO；面板应低频读取，测量时应隐藏。</summary>
        public static RevPerformanceSnapshot PerformanceSnapshot()
        {
            MainThread();
            return RevRuntimePerformanceSampler.Snapshot();
        }

        /// <summary>关闭并释放Recorder；进行中的固定测量必须先取消，避免假装完成。</summary>
        public static void DisablePerformance()
        {
            MainThread();
            RevRuntimePerformanceSampler.Disable();
        }

        /// <summary>注册采样生命周期和只读设备查询；安卓额外在测量边界读取游戏进程的系统指标。</summary>
        internal static void RegisterPerformance()
        {
            const string empty = "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}";
            const string runId = "\"runId\":{\"type\":\"string\",\"minLength\":32,\"maxLength\":32}";
            const string scope = "{\"type\":\"object\",\"properties\":{" + runId + "},\"additionalProperties\":false}";
            const string options = "{\"type\":\"object\",\"properties\":{\"seconds\":{\"type\":\"number\",\"minimum\":1,\"maximum\":600}," +
                "\"warmup\":{\"type\":\"number\",\"minimum\":0,\"maximum\":60}," + runId + "," +
                "\"androidDevice\":{\"type\":\"boolean\"},\"releaseOnFinish\":{\"type\":\"boolean\"}},\"required\":[\"seconds\"],\"additionalProperties\":false}";
            Register("runtime.performance.enable", "启动常驻性能采样；不修改帧率/画质", empty, _ =>
            {
                EnablePerformance();
                return PerformanceSnapshot();
            });
            Register("runtime.performance.snapshot", "只读最近窗口FPS/计数器；缺失为-1", empty, _ => PerformanceSnapshot(), true);
            Register("runtime.performance.begin", "开始固定时长采样，可选安卓设备指标和结束后释放", options, json =>
            {
                var p = JsonUtility.FromJson<PerformanceOptions>(json);
                return RevRuntimePerformanceSampler.Begin(p.seconds, p.warmup, p.runId, p.androidDevice, p.releaseOnFinish);
            });
            Register("runtime.performance.state", "只读指定或最近一轮的状态和最终摘要", scope, json =>
            {
                return RevRuntimePerformanceSampler.State(JsonUtility.FromJson<PerformanceOptions>(json).runId);
            }, true);
            Register("runtime.performance.cancel", "取消指定或最近一轮的进行中测量，保留终态", scope, json =>
            {
                return RevRuntimePerformanceSampler.Cancel(JsonUtility.FromJson<PerformanceOptions>(json).runId);
            });
            Register("runtime.performance.disable", "释放采样 Recorder，指定 runId 时先核对归属", scope, json =>
            {
                RevRuntimePerformanceSampler.Disable(JsonUtility.FromJson<PerformanceOptions>(json).runId);
                return PerformanceSnapshot();
            });
            Register("runtime.identity", "只读Player构建/设备身份", empty, _ => RevRuntimePerformanceSampler.Identity(), true);
#if UNITY_ANDROID && !UNITY_EDITOR
            // 系统服务查询由 Agent 在计时窗前后调用，避免逐帧 JNI 和内存统计干扰压测。
            Register("runtime.android.device", "只读安卓游戏进程 PSS、系统热状态和电池温度", empty,
                _ => RevAndroidDeviceMetrics.Read(), readOnly: true);
#endif
            Register("runtime.quit", "延迟退出当前Player以便先返回响应；Editor中拒绝", empty, _ =>
            {
                if (Application.isEditor)
                {
                    throw new InvalidOperationException("Editor请使用editor.stop");
                }
                RevRuntimePerformanceSampler.Ensure().ScheduleQuit();
                return new { accepted = true };
            });
        }

        /// <summary>运行时固定采样的 JSON 参数，也供查询和清理核对工单归属。</summary>
        [Serializable, Preserve]
        private sealed class PerformanceOptions
        {
            /// <summary>正式采集时长，单位秒，范围 1 到 600。</summary>
            public double seconds;
            /// <summary>预热时长，单位秒，默认不预热。</summary>
            public double warmup;
            /// <summary>本轮的 32 位小写十六进制 ID。省略时沿用原来的最近一轮行为。</summary>
            public string runId;
            /// <summary>请求安卓 Player 采集两端的系统与进程指标。</summary>
            public bool androidDevice;
            /// <summary>终态自动释放本轮独立开启的采样器。</summary>
            public bool releaseOnFinish;
        }
    }
}
#endif
