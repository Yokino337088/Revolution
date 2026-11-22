using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Revolution.Android.Desktop;

/// <summary>由 Python 枚举后返回的设备身份、调试状态及连接证据。</summary>
internal sealed record AndroidDevice
{
    /// <summary>ADB 命令使用的设备唯一串号。</summary>
    [JsonPropertyName("serial")] public string Serial { get; init; } = "";
    /// <summary>只有 device 状态表示调试已授权且 ADB 可用。</summary>
    [JsonPropertyName("state")] public string State { get; init; } = "";
    /// <summary>ADB 能取得时显示设备型号，缺失则显示串号。</summary>
    [JsonPropertyName("model")] public string? Model { get; init; }
    /// <summary>根据 ADB 证据识别到的连接方式。</summary>
    [JsonPropertyName("transport")] public string Transport { get; init; } = "unknown";

    /// <summary>说明网络、模拟器和未确认的连接状态。</summary>
    public string TransportLabel => Transport switch
    {
        "usb" => "USB",
        "network" => "网络",
        "emulator" => "模拟器",
        _ => "连接方式待确认"
    };

    /// <summary>给用户展示 ADB 当前可操作状态。</summary>
    public string StateLabel => State switch
    {
        "device" => "已授权",
        "unauthorized" => "待手机授权",
        "offline" => "离线",
        _ => State
    };

    /// <summary>在多设备时同时展示型号、串号和授权情况。</summary>
    public string Label => string.IsNullOrWhiteSpace(Model)
        ? $"{Serial} · {TransportLabel} · {StateLabel}"
        : $"{Model.Replace('_', ' ')} · {Serial} · {TransportLabel} · {StateLabel}";
}

/// <summary>以参数数组启动随桌面应用交付的工具，复用 Python 的 ADB 和身份校验。</summary>
internal static class DesktopTools
{
    /// <summary>构建时复制的同版本 RevCLI、RevMCP 和 Android 工具的共同目录。</summary>
    public static string ToolsRoot { get; } = Path.Combine(AppContext.BaseDirectory, "tools");

    /// <summary>按路径构造安卓 CLI 入口，用户在任意工作目录打开程序时也可用。</summary>
    public static string AndroidEntry { get; } = Path.Combine(ToolsRoot, "Revolution.Android", "rev_android.py");

    /// <summary>没有手机时也可以用同版本 RevCLI 单独查在线编辑器。</summary>
    public static string EditorEntry { get; } = Path.Combine(ToolsRoot, "Revolution.RevCLI", "rev.py");

    /// <summary>项目现有注册能力示例，只有用户明确运行时才提交动作。</summary>
    public static string ExamplePlan { get; } = Path.Combine(ToolsRoot, "Revolution.Android", "examples", "revgame-perf.json");

    /// <summary>桌面输出和输入均用 UTF-8，保留中文路径、诊断和参数。</summary>
    private static readonly Encoding Utf8 = new UTF8Encoding(false);

    /// <summary>构造 Python 参数。py 是 Windows 启动器，指定使用 Python 3。</summary>
    public static string[] PythonArguments(string launcher, string entry, IEnumerable<string> arguments)
    {
        string name = Path.GetFileNameWithoutExtension(launcher);
        string[] prefix = name.Equals("py", StringComparison.OrdinalIgnoreCase) ? ["-3", entry] : [entry];
        return [..prefix, ..arguments];
    }

    /// <summary>为短查询和长会话提供独立进程，不经过命令行外壳。</summary>
    public static ProcessStartInfo PythonProcess(string launcher, string entry, IEnumerable<string> arguments)
    {
        if (!File.Exists(entry))
        {
            throw new InvalidOperationException("找不到随桌面程序交付的工具文件 " + entry);
        }
        var start = new ProcessStartInfo(launcher)
        {
            WorkingDirectory = ToolsRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardInputEncoding = Utf8,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8
        };
        start.Environment["PYTHONUTF8"] = "1";
        foreach (string argument in PythonArguments(launcher, entry, arguments))
        {
            start.ArgumentList.Add(argument);
        }
        return start;
    }

    /// <summary>短查询只读取单个 JSON。异常时回报脚本的原始诊断，不把空结果当成功。</summary>
    public static async Task<JsonElement> QueryAsync(string launcher, string entry, IEnumerable<string> arguments)
    {
        using var process = Process.Start(PythonProcess(launcher, entry, arguments))
            ?? throw new InvalidOperationException("无法启动 Python");
        process.StandardInput.Close();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> errors = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(32));
        }
        catch (TimeoutException)
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
            throw new TimeoutException("查询超时，请检查 ADB 或 Unity 编辑器状态");
        }
        string body = await output;
        string stderr = await errors;
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? "工具没有返回结果" : stderr.Trim());
        }
        // RevCLI 可能在 JSON 前输出连接提示，从最后一条 JSON 响应读取状态。
        string json = body.Split('\n', StringSplitOptions.RemoveEmptyEntries).Last().Trim();
        if (entry == AndroidEntry)
        {
            json = body;
        }
        using var parsed = JsonDocument.Parse(json);
        if (process.ExitCode != 0 || Failed(parsed.RootElement))
        {
            throw new InvalidOperationException(Message(parsed.RootElement, stderr.Trim()));
        }
        return parsed.RootElement.Clone();
    }

    /// <summary>从设备列表和业务回执中识别明确的失败状态。</summary>
    public static bool Failed(JsonElement value) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty("success", out JsonElement success)
        && success.ValueKind == JsonValueKind.False;

    /// <summary>使用工具返回的人话提示，缺少消息时显示调用方给出的原因。</summary>
    public static string Message(JsonElement value, string fallback) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty("message", out JsonElement message) &&
        message.ValueKind == JsonValueKind.String ? message.GetString() ?? fallback : fallback;

    /// <summary>工具链无环境配置时，检查已经运行的 Unity 安装目录。</summary>
    public static string? FindUnityAdb()
    {
        foreach (Process process in Process.GetProcessesByName("Unity"))
        {
            using (process)
            {
                string? executable;
                try
                {
                    executable = process.MainModule?.FileName;
                }
                catch (Exception error) when (error is Win32Exception or InvalidOperationException)
                {
                    continue;
                }
                if (executable is null)
                {
                    continue;
                }
                string path = Path.Combine(Path.GetDirectoryName(executable)!,
                    "Data", "PlaybackEngines", "AndroidPlayer", "SDK", "platform-tools", "adb.exe");
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }
        string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Android", "Sdk", "platform-tools", "adb.exe");
        return File.Exists(local) ? local : null;
    }
}
