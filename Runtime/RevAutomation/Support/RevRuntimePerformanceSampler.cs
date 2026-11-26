// 常驻采样宿主。每帧仅读Recorder/写预分配数组；排序、JSON、截图和文件输出不在采样循环中执行。
#if UNITY_EDITOR || DEVELOPMENT_BUILD || REV_AUTOMATION
using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace Revolution
{
    /// <summary>在游戏帧内写入预分配采样窗口，测量结束后才统计分位数并释放本次缓冲。</summary>
    internal sealed class RevRuntimePerformanceSampler : MonoBehaviour
    {
        private static RevRuntimePerformanceSampler _instance;
        /// <summary>当前 Play 的采样报告。同一轮停止采样后仍可读，进入下一轮时复位。</summary>
        private static RevPerformanceReport _report = new RevPerformanceReport();

        /// <summary>关闭域重载时解除上一轮采样结果，首个 state 查询返回空闲。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetReport()
        {
            _report = new RevPerformanceReport();
        }
        // 环形窗口归本宿主所有，保留容量不保留无限历史。固定测量最多每秒1000样本，超出明确失败。
        private readonly double[] _rolling = new double[2048];
        private readonly FrameTiming[] _timings = new FrameTiming[1];
        private ProfilerRecorder[] _recorders;
        private int _cursor, _count, _errorStart;
        private double _previous, _start, _warmUntil, _lastGpu = -1;
        private ulong _stamp;
        private double[] _frames, _gpu, _sums;
        private int[] _valid;
        private bool _collecting;
        /// <summary>当前固定采样是否在计时窗外读取 Android 设备指标。</summary>
        private bool _androidDevice;
        /// <summary>当前轮完成或取消后归还临时采样宿主的请求。</summary>
        private bool _releaseOnFinish;
        /// <summary>已有调用方明确开启了滚动快照，终态不能替它关闭宿主。</summary>
        private bool _keepRolling;
        // 退出的墙钟期限，负值表示尚未收到退出请求。
        private double _quitAt = -1;
        // 仅在等待 Android 客户端领取退出结果时存在，收到 ACK 后立即释放。
        private Func<bool> _quitReceipt;
        private static readonly string[] Names =
        {
            "Draw Calls Count", "Total Batches Count", "SetPass Calls Count", "Triangles Count", "Vertices Count",
            "Dynamic Batches Count", "Static Batches Count", "Instanced Batches Count", "GC Allocated In Frame",
            "Total Used Memory", "Total Reserved Memory", "GC Used Memory", "Main Thread", "Render Thread"
        };

        internal static RevRuntimePerformanceSampler Ensure()
        {
            if (_instance == null)
            {
                var go = new GameObject("[RevRuntimePerformance]");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<RevRuntimePerformanceSampler>();
            }
            return _instance;
        }

        private void Awake()
        {
            _recorders = new ProfilerRecorder[Names.Length];
            for (int i = 0; i < Names.Length; i++)
            {
                ProfilerCategory category = i < 8 ? ProfilerCategory.Render : i < 12 ? ProfilerCategory.Memory : ProfilerCategory.Internal;
                // 不存在的计数器保持Invalid；不同平台/Release构建不得把缺样伪装成零。
                _recorders[i] = ProfilerRecorder.StartNew(category, Names[i], 1);
                if (i == 1 && !_recorders[i].Valid)
                {
                    // 2022.3 Player的原生标记名为Batches Count，手册展示名为Total Batches Count。
                    // 只在首选名称不存在时尝试已知别名；两者均缺失仍报告N/A。
                    _recorders[i].Dispose();
                    _recorders[i] = ProfilerRecorder.StartNew(category, "Batches Count", 1);
                }
            }
            _previous = Time.realtimeSinceStartupAsDouble;
        }

        /// <summary>用单调墙钟累计真实帧间隔，并独立检查 GPU 驱动读数；退出、预热和正式测量按顺序推进。</summary>
        private void Update()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (_quitReceipt != null && _quitReceipt())
            {
                // 电脑已取得最终工单，仍给手机回环 HTTP 留出发送 ACK 回执的时间。
                _quitReceipt = null;
                _quitAt = Math.Min(_quitAt, now + .25);
            }
            if (_quitAt >= 0 && now >= _quitAt)
            {
                Application.Quit();
                return;
            }
            double ms = (now - _previous) * 1000;
            _previous = now;
            _rolling[_cursor] = ms;
            _cursor = (_cursor + 1) % _rolling.Length;
            _count = Math.Min(_count + 1, _rolling.Length);
            FrameTimingManager.CaptureFrameTimings();
            bool newTiming = FrameTimingManager.GetLatestTimings(1, _timings) > 0 && _timings[0].frameStartTimestamp != _stamp;
            bool invalidGpu = false;
            _lastGpu = -1;
            if (newTiming)
            {
                _stamp = _timings[0].frameStartTimestamp;
                // GPU 帧不可能比游戏自启动以来经过的毫秒数更长。部分安卓虚拟驱动返回了时间戳量级的正数。
                double gpuMs = _timings[0].gpuFrameTime;
                invalidGpu = double.IsNaN(gpuMs) || double.IsInfinity(gpuMs) || gpuMs > now * 1000;
                if (!invalidGpu && gpuMs > 0)
                {
                    _lastGpu = gpuMs;
                }
            }
            if (_report.status != "running")
            {
                return;
            }
            if (!_collecting)
            {
                if (now < _warmUntil)
                {
                    return;
                }
                _collecting = true;
                _start = now;
                return;
            }
            if (_report.frames >= _frames.Length)
            {
                Finish("failed", "超过预分配样本上限，结果不截断冒充通过");
                return;
            }
            _frames[_report.frames++] = ms;
            if (_lastGpu > 0)
            {
                _gpu[_report.gpuSamples++] = _lastGpu;
            }
            else if (invalidGpu)
            {
                _report.gpuRejectedSamples++;
            }
            if (!Application.isFocused)
            {
                _report.unfocusedFrames++;
            }
            for (int i = 0; i < _recorders.Length; i++)
            {
                if (_recorders[i].Valid && _recorders[i].Count > 0)
                {
                    _sums[i] += _recorders[i].LastValue;
                    _valid[i]++;
                }
            }
            _report.seconds = now - _start;
            if (_report.seconds >= _report.requestedSeconds)
            {
                Finish("completed", "");
            }
        }

        /// <summary>有明确的滚动窗口消费者时保留它的 Recorder，直到调用方主动关闭。</summary>
        internal void KeepRolling()
        {
            _keepRolling = true;
        }

        /// <summary>准备有界采样。选填 ID 让调用方在网络回执丢失时仍只能操作自己发起的轮次。</summary>
        internal static RevPerformanceReport Begin(double seconds, double warmup, string runId, bool androidDevice, bool releaseOnFinish)
        {
            if (double.IsNaN(seconds) || double.IsNaN(warmup) || seconds < 1 || seconds > 600 || warmup < 0 || warmup > 60)
            {
                throw new ArgumentOutOfRangeException("seconds=1..600，warmup=0..60");
            }
            if (_report.status == "running")
            {
                throw new InvalidOperationException("已有测量作业，不能覆盖");
            }
            if (!string.IsNullOrEmpty(runId) && (!Guid.TryParseExact(runId, "N", out _) || runId == _report.runId))
            {
                throw new ArgumentException("runId 必须是尚未使用的 32 位十六进制 ID");
            }

            // PSS/热态查询可能要调用系统服务，完成后才开始预热计时。
            var before = androidDevice ? ReadAndroidDevice() : null;
            var host = Ensure();
            int capacity = (int)Math.Ceiling(seconds * 1000) + 1;
            host._frames = new double[capacity];
            host._gpu = new double[capacity];
            host._sums = new double[Names.Length];
            host._valid = new int[Names.Length];
            host._errorStart = RevRuntimeAutomation.ErrorCount;
            host._warmUntil = Time.realtimeSinceStartupAsDouble + warmup;
            host._collecting = false;
            host._androidDevice = androidDevice;
            host._releaseOnFinish = releaseOnFinish;
            _report = new RevPerformanceReport
            {
                runId = string.IsNullOrEmpty(runId) ? Guid.NewGuid().ToString("N") : runId, status = "running", requestedSeconds = seconds,
                warmupSeconds = warmup, startedUtc = DateTime.UtcNow.ToString("O"), environment = Identity(),
                androidBefore = before
            };
            return _report;
        }

        /// <summary>Android Player 的边界读数与帧统计各自出证据。系统服务缺测带原因返回。</summary>
        private static Dictionary<string, object> ReadAndroidDevice()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                return RevAndroidDeviceMetrics.Read();
            }
            catch (Exception error)
            {
                return new Dictionary<string, object> { ["sampleError"] = error.Message };
            }
#else
            throw new PlatformNotSupportedException("androidDevice 需要在安装的安卓测试 Player 中采样");
#endif
        }

        /// <summary>封存本次测量，可归属于整个采样窗的 GPU 数据才参与分位数；缺样保留默认 -1。</summary>
        private void Finish(string status, string error, bool destroying = false)
        {
            _report.status = status;
            _report.cancelled = status == "cancelled";
            _report.errors = RevRuntimeAutomation.ErrorCount - _errorStart;
            _report.error = error;
            _report.endedUtc = DateTime.UtcNow.ToString("O");
            _report.success = status == "completed" && _report.errors == 0 && _report.seconds >= _report.requestedSeconds && _report.frames > 0;
            if (status == "completed" && !_report.success)
            {
                _report.status = "failed";
                _report.error = "时长/样本/新增运行错误门禁失败";
            }
            if (_report.frames > 0)
            {
                Array.Sort(_frames, 0, _report.frames);
                _report.fps = _report.seconds > 0 ? _report.frames / _report.seconds : 0;
                _report.p50Ms = Percentile(_frames, _report.frames, .5);
                _report.p95Ms = Percentile(_frames, _report.frames, .95);
                _report.p99Ms = Percentile(_frames, _report.frames, .99);
                _report.maxMs = _frames[_report.frames - 1];
                foreach (double threshold in new[] { 12.5, 16.6667, 33.3333 })
                {
                    int over = 0;
                    for (int i = 0; i < _report.frames; i++)
                    {
                        if (_frames[i] > threshold)
                        {
                            over++;
                        }
                    }
                    double ratio = over / (double)_report.frames;
                    if (threshold == 12.5)
                    {
                        _report.over125Ratio = ratio;
                    }
                    else if (threshold < 20)
                    {
                        _report.over167Ratio = ratio;
                    }
                    else
                    {
                        _report.over333Ratio = ratio;
                    }
                }
            }
            if (_report.gpuSamples > 0)
            {
                Array.Sort(_gpu, 0, _report.gpuSamples);
                // 已运行很久的游戏也可能收到异常正数。比整个计时窗还长的单帧无法归入本轮，单独计数。
                while (_report.gpuSamples > 0 && _gpu[_report.gpuSamples - 1] > _report.seconds * 1000)
                {
                    _report.gpuSamples--;
                    _report.gpuRejectedSamples++;
                }
                if (_report.gpuSamples > 0)
                {
                    _report.gpuP50Ms = Percentile(_gpu, _report.gpuSamples, .5);
                    _report.gpuP95Ms = Percentile(_gpu, _report.gpuSamples, .95);
                }
            }
            _report.averages = Metrics(true);
            _frames = null;
            _gpu = null;
            _sums = null;
            _valid = null;

            // 先封存采样秒数和分位数，再调用 Android 服务。后者的开销不进入本轮帧统计。
            if (_androidDevice)
            {
                _report.androidAfter = ReadAndroidDevice();
            }
            _report.finished = true;
            if (!destroying && _releaseOnFinish && !_keepRolling && _quitAt < 0)
            {
                ReleaseHost();
            }
        }

        private static double Percentile(double[] sorted, int count, double fraction)
        {
            return count == 0 ? 0 : sorted[Math.Max(0, (int)Math.Ceiling(count * fraction) - 1)];
        }

        private RevPerformanceMetric[] Metrics(bool average)
        {
            var metrics = new RevPerformanceMetric[Names.Length];
            for (int i = 0; i < metrics.Length; i++)
            {
                bool valid = average ? _valid[i] > 0 : _recorders[i].Valid && _recorders[i].Count > 0;
                double value = valid ? (average ? _sums[i] / _valid[i] : _recorders[i].LastValue) : -1;
                if (valid && i >= 12)
                {
                    value /= 1000000;
                }
                metrics[i] = new RevPerformanceMetric { name = Names[i], unit = i < 8 ? "count" : i < 12 ? "bytes" : "ms", available = valid, value = value };
            }
            return metrics;
        }

        internal static RevPerformanceSnapshot Identity()
        {
            return new RevPerformanceSnapshot
            {
                product = Application.productName, buildId = Application.buildGUID, unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(), editor = Application.isEditor, development = Debug.isDebugBuild,
#if ENABLE_IL2CPP
                backend = "IL2CPP",
#else
                backend = "Mono",
#endif
                cpu = SystemInfo.processorType, gpu = SystemInfo.graphicsDeviceName, graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                resolution = Screen.width + "x" + Screen.height, focused = Application.isFocused, runInBackground = Application.runInBackground,
                targetFrameRate = Application.targetFrameRate, vSyncCount = QualitySettings.vSyncCount, enabled = _instance != null
            };
        }

        internal static RevPerformanceSnapshot Snapshot()
        {
            var result = Identity();
            if (_instance == null)
            {
                result.metrics = Array.Empty<RevPerformanceMetric>();
                return result;
            }
            int count = _instance._count;
            var sorted = new double[count];
            Array.Copy(_instance._rolling, sorted, count);
            double sum = 0;
            foreach (double value in sorted)
            {
                sum += value;
            }
            Array.Sort(sorted);
            result.frames = count;
            result.seconds = sum / 1000;
            result.fps = sum > 0 ? count * 1000 / sum : 0;
            result.p50Ms = Percentile(sorted, count, .5);
            result.p95Ms = Percentile(sorted, count, .95);
            result.p99Ms = Percentile(sorted, count, .99);
            result.maxMs = count > 0 ? sorted[count - 1] : 0;
            result.gpuMs = _instance._lastGpu;
            result.metrics = _instance.Metrics(false);
            return result;
        }

        /// <summary>返回指定或最近一轮的摘要。窗口释放后仍可按 ID 领取同一轮终态。</summary>
        internal static RevPerformanceReport State(string runId)
        {
            CheckRun(runId);
            return _report;
        }

        /// <summary>只取消调用者指定的轮次。旧客户端省略 ID 时仍可取消正在运行的轮次。</summary>
        internal static RevPerformanceReport Cancel(string runId)
        {
            CheckRun(runId);
            if (_instance != null && _report.status == "running")
            {
                _instance.Finish("cancelled", "调用方取消本轮采样");
            }
            return _report;
        }

        /// <summary>显式结束滚动窗口，先核对指定轮次并确保固定采样已经终止。</summary>
        internal static void Disable(string runId = null)
        {
            CheckRun(runId);
            if (_report.status == "running")
            {
                throw new InvalidOperationException("进行中的测量请先 cancel");
            }
            if (_instance != null)
            {
                _instance.ReleaseHost();
            }
        }

        /// <summary>防止旧回执或并发 Agent 的收尾误读、误取消另一轮。</summary>
        private static void CheckRun(string runId)
        {
            if (!string.IsNullOrEmpty(runId) && !string.Equals(runId, _report.runId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("当前轮次与指定 runId 不匹配");
            }
        }

        /// <summary>先取消更新与当前宿主引用，下一轮无需等待 Unity 帧末销毁。</summary>
        private void ReleaseHost()
        {
            _instance = null;
            enabled = false;
            Destroy(gameObject);
        }

        /// <summary>安卓 HTTP 等待本次退出工单被认领；电脑断线时最多等待十秒，其它入口沿用原有延迟。</summary>
        internal void ScheduleQuit()
        {
#if UNITY_ANDROID && !UNITY_EDITOR && !(UNITY_WEIXINMINIGAME || UNITY_BYTEDANCE_MINIGAME || UNITY_BYTEDANCEMINIGAME)
            _quitReceipt = RevRuntimeAutomationHost.CurrentQuitReceipt();
#endif
            _quitAt = Time.realtimeSinceStartupAsDouble + (_quitReceipt == null ? .75 : 10);
        }

        /// <summary>场景或游戏退出时封存当前轮，并归还本宿主的原生采样器。</summary>
        private void OnDestroy()
        {
            if (_instance == this && _report.status == "running")
            {
                Finish("cancelled", "采样宿主销毁", destroying: true);
            }
            if (_recorders != null)
            {
                for (int i = 0; i < _recorders.Length; i++)
                {
                    _recorders[i].Dispose();
                }
            }
            if (_instance == this)
            {
                _instance = null;
            }
        }
    }
}
#endif
