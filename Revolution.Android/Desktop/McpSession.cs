using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Revolution.Android.Desktop;

/// <summary>持有一个真实 RevMCP stdio 会话。安卓工具接管 ADB 转发和游戏进程，窗口只持有管道。</summary>
internal sealed class McpSession
{
    /// <summary>这一次 USB 会话的 Python 子进程。</summary>
    private readonly Process _process;
    /// <summary>将每条 JSON-RPC 回执送回对应的请求。</summary>
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    /// <summary>把请求、取消和关闭 stdin 按完整消息依次写入。</summary>
    private readonly SemaphoreSlim _writer = new(1);
    /// <summary>持续消费 stdout 以免 MCP 进程在用户未等待结果时阻塞。</summary>
    private readonly Task _output;
    /// <summary>并行读取启动、断线和清理诊断。</summary>
    private readonly Task _errors;
    /// <summary>只记录工具写到 stderr 的日志，由窗口负责切回 UI 线程。</summary>
    private readonly Action<string> _log;
    /// <summary>本会话单调递增的 JSON-RPC 请求号。</summary>
    private int _requestId;
    /// <summary>可取消的计划工单号，零表示当前没有计划。</summary>
    private int _planId;
    /// <summary>关闭了输入后禁止再给该进程发送动作。</summary>
    private bool _inputClosed;
    /// <summary>保留一次退出结果，供重复关闭或失败回收读取。</summary>
    private int? _exitCode;
    /// <summary>进程意外退出时可提供的最近工具日志。</summary>
    private string? _lastError;

    /// <summary>启动 Python 并同时消费两个输出流，手机仍由 Python 完成启动和校验。</summary>
    public McpSession(ProcessStartInfo start, Action<string> log)
    {
        _log = log;
        _process = Process.Start(start) ?? throw new InvalidOperationException("无法启动 RevMCP");
        _output = ReadOutputAsync();
        _errors = ReadErrorsAsync();
    }

    /// <summary>让窗口发现子进程是否已结束，避免把断线会话显示为可用。</summary>
    public Task WaitForExitAsync() => _process.WaitForExitAsync();

    /// <summary>完成 RevMCP 初始化，直到手机握手能被真实服务确认。</summary>
    public async Task InitializeAsync()
    {
        var result = await RequestAsync("initialize",
            new { protocolVersion = "2024-11-05", capabilities = new { }, clientInfo = new { name = "Revolution.Android.Desktop", version = "1.0" } },
            TimeSpan.FromSeconds(305));
        if (!result.TryGetProperty("serverInfo", out JsonElement info) ||
            !info.TryGetProperty("name", out JsonElement name) || name.GetString() != "Revolution.RevMCP")
        {
            throw new InvalidOperationException("工具未返回 Revolution.RevMCP 的初始化回执");
        }
        await SendAsync(new { jsonrpc = "2.0", method = "notifications/initialized" });
    }

    /// <summary>调用现有的发现、查询和计划入口，结果以原有结构化 JSON 返回。</summary>
    public async Task<JsonElement> CallAsync(string name, object arguments, bool isPlan = false)
    {
        TimeSpan? timeout = isPlan ? null : TimeSpan.FromSeconds(35);
        JsonElement response = await RequestAsync("tools/call", new { name, arguments }, timeout, isPlan);
        return response.TryGetProperty("structuredContent", out JsonElement content) ? content : response;
    }

    /// <summary>通过同一条管道取消正在运行的计划，让 RevMCP 继续执行计划的 finally。</summary>
    public async Task<bool> CancelPlanAsync()
    {
        int id = Volatile.Read(ref _planId);
        if (id == 0)
        {
            return false;
        }
        await CancelAsync(id);
        return true;
    }

    /// <summary>发送 EOF，请求 Android 工具执行会话退出，等待子进程交付清理结果。</summary>
    public async Task<int> StopAsync()
    {
        await _writer.WaitAsync();
        try
        {
            if (!_inputClosed)
            {
                _inputClosed = true;
                try
                {
                    _process.StandardInput.Close();
                }
                catch (IOException) when (_process.HasExited)
                {
                    // 子进程退出时已没有接收端，继续收集它的诊断和退出码。
                }
            }
        }
        finally
        {
            _writer.Release();
        }
        if (_exitCode is int exitCode)
        {
            return exitCode;
        }
        await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));
        await Task.WhenAll(_output, _errors);
        _exitCode = _process.ExitCode;
        _process.Dispose();
        return _exitCode.Value;
    }

    /// <summary>先登记工单再写入，连接关闭和超时均会解除等待；长计划支持显式取消。</summary>
    private async Task<JsonElement> RequestAsync(string method, object arguments, TimeSpan? timeout, bool isPlan = false)
    {
        int id = Interlocked.Increment(ref _requestId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;
        try
        {
            await SendAsync(new { jsonrpc = "2.0", id, method, @params = arguments });
            if (isPlan)
            {
                Volatile.Write(ref _planId, id);
            }
            JsonElement message = timeout is TimeSpan wait ? await completion.Task.WaitAsync(wait) : await completion.Task;
            if (message.TryGetProperty("error", out JsonElement error))
            {
                throw new InvalidOperationException(DesktopTools.Message(error, "RevMCP 返回错误"));
            }
            return message.GetProperty("result");
        }
        catch (TimeoutException)
        {
            if (method != "initialize")
            {
                await CancelAsync(id);
            }
            throw new TimeoutException("等待 RevMCP 回执超时，请查看连接日志");
        }
        finally
        {
            _pending.TryRemove(id, out _);
            if (isPlan)
            {
                Volatile.Write(ref _planId, 0);
            }
        }
    }

    /// <summary>发送已有服务支持的取消通知，使用原工单号，避免重复发起游戏动作。</summary>
    private Task CancelAsync(int id) => SendAsync(new
    {
        jsonrpc = "2.0", method = "notifications/cancelled",
        @params = new { requestId = id, reason = "桌面工作台请求取消" }
    });

    /// <summary>逐条写入 UTF-8 JSON-RPC 并及时刷新，stderr 保持单独通道。</summary>
    private async Task SendAsync(object message)
    {
        await _writer.WaitAsync();
        try
        {
            if (_inputClosed || _process.HasExited)
            {
                throw new InvalidOperationException("RevMCP 已断开");
            }
            await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message));
            await _process.StandardInput.FlushAsync();
        }
        finally
        {
            _writer.Release();
        }
    }

    /// <summary>持续解析标准输出；无法继续通信时让每个等待者收到同一个断开原因。</summary>
    private async Task ReadOutputAsync()
    {
        Exception? failure = null;
        try
        {
            while (await _process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is string line)
            {
                using var json = JsonDocument.Parse(line);
                JsonElement response = json.RootElement;
                if (response.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.Number &&
                    id.TryGetInt32(out int key) &&
                    _pending.TryGetValue(key, out var pending))
                {
                    pending.TrySetResult(response.Clone());
                }
            }
        }
        catch (Exception error) when (error is JsonException or IOException or InvalidOperationException)
        {
            failure = error;
        }
        finally
        {
            foreach (var pending in _pending.Values)
            {
                pending.TrySetException(failure ?? new IOException(_lastError ?? "RevMCP 已结束，请查看连接日志"));
            }
        }
    }

    /// <summary>按行转发 stderr，尤其保留 APK 启动错误和资源回收诊断。</summary>
    private async Task ReadErrorsAsync()
    {
        while (await _process.StandardError.ReadLineAsync().ConfigureAwait(false) is string line)
        {
            _lastError = line;
            _log(line);
        }
    }
}
