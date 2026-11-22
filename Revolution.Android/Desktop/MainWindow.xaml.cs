using System.ComponentModel;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Revolution.Android.Desktop;

/// <summary>选择设备和构建、控制一次 USB 会话、展示可直接交给 Agent 的结果。</summary>
public partial class MainWindow : Window
{
    /// <summary>原样呈现中文回执，缩进仅用于阅读与用户主动导出。</summary>
    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    /// <summary>窗口掌管的当前 MCP 会话，断开并收尾后才释放。</summary>
    private McpSession? _session;
    /// <summary>已建立会话的 ADB 串号，扫到另一台设备时也不会偷换目标。</summary>
    private string? _sessionSerial;
    /// <summary>当前界面已发出的操作，关闭窗口时可等待它收尾。</summary>
    private Task _operation = Task.CompletedTask;
    /// <summary>串行执行连接及查询，避免多次点击重复安装、切换或启动。</summary>
    private bool _busy;
    /// <summary>完成 MCP 握手与手机回执检查后才允许发送测试计划。</summary>
    private bool _ready;
    /// <summary>运行计划期间开启取消入口，其他操作继续保持串行。</summary>
    private bool _runningPlan;
    /// <summary>窗口正在停止当前任务并归还设备，阻止再次开会话。</summary>
    private bool _closing;
    /// <summary>任务与子进程已经收尾，允许 WPF 正常关闭。</summary>
    private bool _closeAccepted;

    /// <summary>加载工作台和控件，稍后只自动扫描设备。</summary>
    public MainWindow()
    {
        InitializeComponent();
        UpdateControls();
    }

    /// <summary>取当前选择的 Python，默认复用 Windows py 启动器。</summary>
    private string Launcher => string.IsNullOrWhiteSpace(PythonPath.Text) ? "py" : PythonPath.Text.Trim();

    /// <summary>选中的设备来自最近一次 ADB 扫描。</summary>
    private AndroidDevice? SelectedDevice => Devices.SelectedItem as AndroidDevice;

    /// <summary>首次显示后用系统配置或在线 Unity 的 SDK 查找可用 ADB。</summary>
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(AdbPath.Text) &&
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT")) &&
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANDROID_HOME")))
        {
            AdbPath.Text = DesktopTools.FindUnityAdb() ?? "";
        }
        await StartOperationAsync("正在扫描 ADB 设备…", ScanAsync);
    }

    /// <summary>读取设备授权和连接方式，不安装 APK 或启动手机应用。</summary>
    private async Task ScanAsync()
    {
        string? selected = _sessionSerial ?? SelectedDevice?.Serial;
        Devices.ItemsSource = null;
        var reply = await DesktopTools.QueryAsync(Launcher, DesktopTools.AndroidEntry, [..AdbArguments(), "devices"]);
        AndroidDevice[] entries = reply.GetProperty("devices").Deserialize<AndroidDevice[]>() ?? [];
        Devices.ItemsSource = entries;
        var restored = entries.FirstOrDefault(item => item.Serial == selected);
        if (_session is null && restored is null)
        {
            AndroidDevice[] wired = entries.Where(item => item.Transport == "usb").ToArray();
            restored = wired.Length == 1 ? wired[0] : entries.Length == 1 ? entries[0] : null;
        }
        Devices.SelectedItem = restored;
        if (entries.Length == 0)
        {
            DeviceHint.Text = "ADB 尚未识别到设备。请解锁手机，启用 USB 调试并确认授权。";
        }
        else
        {
            int usb = entries.Count(item => item.Transport == "usb");
            DeviceHint.Text = $"发现 {entries.Length} 台设备，确认 USB 连接 {usb} 台。";
        }
        RefreshDeviceStatus();
        SetFooter("设备列表已更新。");
        ShowResult(reply);
    }

    /// <summary>手动刷新当前 ADB 清单，保持所选串号。</summary>
    private async void OnScan(object sender, RoutedEventArgs e) =>
        await StartOperationAsync("正在扫描 ADB 设备…", ScanAsync);

    /// <summary>按下连接后才验证本次配置，启动并认领实际手机会话。</summary>
    private async void OnConnect(object sender, RoutedEventArgs e)
    {
        await StartOperationAsync("正在启动手机测试包并建立 RevMCP 会话…", async () =>
        {
            if (string.IsNullOrWhiteSpace(PackageName.Text))
            {
                await InspectApkAsync();
            }
            string[] arguments = BuildSessionArguments();
            var session = new McpSession(DesktopTools.PythonProcess(Launcher, DesktopTools.AndroidEntry, arguments), AppendLog);
            _session = session;
            _sessionSerial = SelectedDevice?.Serial;
            McpStatus.Text = "正在握手";
            try
            {
                await session.InitializeAsync();
                await ReadIdentityAsync();
                _ready = true;
                McpStatus.Text = "已连接 · stdio";
                _ = ObserveExitAsync(session);
                SetFooter("已验证手机回执。可以查询能力或运行测试计划。");
            }
            catch
            {
                try
                {
                    await DisconnectAsync();
                }
                catch (Exception cleanup)
                {
                    AppendLog("启动失败后的收尾提示 · " + cleanup.Message);
                }
                throw;
            }
        });
    }

    /// <summary>退出当前工具实例，等待 Python 归还它创建的转发与进程。</summary>
    private async void OnDisconnect(object sender, RoutedEventArgs e) =>
        await StartOperationAsync("正在归还本次设备会话…", DisconnectAsync);

    /// <summary>保留子进程的实际退出码，以便区分成功清理与断线后的失败。</summary>
    private async Task DisconnectAsync()
    {
        if (_session is null)
        {
            return;
        }
        _ready = false;
        McpStatus.Text = "正在断开";
        int exit = await _session.StopAsync();
        _session = null;
        _sessionSerial = null;
        McpStatus.Text = "未连接";
        RefreshDeviceStatus();
        if (exit != 0)
        {
            throw new InvalidOperationException("RevMCP 退出码为 " + exit + "，请查看连接日志里的收尾结果");
        }
        SetFooter("会话已断开。");
    }

    /// <summary>工具意外退出时关闭业务入口，保留已有结果供复制。</summary>
    private async Task ObserveExitAsync(McpSession session)
    {
        await session.WaitForExitAsync();
        if (_session == session && _ready)
        {
            _ready = false;
            McpStatus.Text = "进程已退出";
            SetFooter("会话已结束，可查看连接日志后重新连接。");
            UpdateControls();
        }
    }

    /// <summary>读取实际编辑器；USB 尚未建立时走独立 RevCLI。</summary>
    private async void OnCheckEditor(object sender, RoutedEventArgs e)
    {
        await StartOperationAsync("正在读取 Unity 编辑器状态…", async () =>
        {
            string project = UnityProject(required: true)!;
            EditorStatus.Text = "正在检查";
            try
            {
                JsonElement reply = _ready
                    ? await _session!.CallAsync("rev_call", new { target = "editor", action = "editor.status" })
                    : await DesktopTools.QueryAsync(Launcher, DesktopTools.EditorEntry,
                        ["--project", project, "--mode", "live", "--timeout", "12", "editor", "status"]);
                ShowResult(reply);
                RequireSuccess(reply);
                JsonElement data = reply.GetProperty("data");
                bool playing = data.TryGetProperty("playing", out JsonElement state) && state.ValueKind == JsonValueKind.True;
                EditorStatus.Text = playing ? "已连接 · Play" : "已连接 · 编辑态";
                SetFooter("已读取编辑器的真实状态。");
            }
            catch
            {
                EditorStatus.Text = "连接检查失败";
                throw;
            }
        });
    }

    /// <summary>从当前 RevMCP 获取手机的真实身份、构建和运行环境。</summary>
    private async void OnReadIdentity(object sender, RoutedEventArgs e) =>
        await StartOperationAsync("正在核对手机回执…", ReadIdentityAsync);

    /// <summary>可重复确认运行时身份，当前设备出错时在状态栏说明。</summary>
    private async Task ReadIdentityAsync()
    {
        JsonElement reply = await _session!.CallAsync("rev_call", new { target = "game", action = "runtime.identity" });
        ShowResult(reply);
        if (DesktopTools.Failed(reply))
        {
            PhoneStatus.Text = "手机回执检查失败";
        }
        RequireSuccess(reply);
        JsonElement data = reply.GetProperty("data");
        if (data.GetProperty("platform").GetString() != "Android")
        {
            throw new InvalidOperationException("收到的回执不属于 Android Player");
        }
        PhoneStatus.Text = $"{SelectedDevice?.TransportLabel ?? "已选设备"} · Android 回执通过";
        SetFooter("已收到手机的运行时身份。");
    }

    /// <summary>查询当前 Editor 和 Player 公开的实际能力，使用 RevMCP 注册结果。</summary>
    private async void OnDiscover(object sender, RoutedEventArgs e)
    {
        await StartOperationAsync("正在发现当前实例的动作…", async () =>
        {
            JsonElement reply = await _session!.CallAsync("rev_discover", new { limit = 60, full = true });
            ShowResult(reply);
            RequireSuccess(reply);
            SetFooter("发现结果已返回。");
        });
    }

    /// <summary>按需从系统读取进程占用和热状态，测试计划运行时暂停此入口。</summary>
    private async void OnReadDeviceSample(object sender, RoutedEventArgs e)
    {
        await StartOperationAsync("正在读取手机侧内存与温度快照…", async () =>
        {
            string serial = _sessionSerial ?? SelectedDevice!.Serial;
            JsonElement reply = await DesktopTools.QueryAsync(Launcher, DesktopTools.AndroidEntry,
                [..AdbArguments(), "--serial", serial, "device-sample", "--package", PackageName.Text.Trim()]);
            ShowResult(reply);
            SetFooter("设备快照已返回。空值表示系统未提供该项。");
        });
    }

    /// <summary>把用户选定的计划交给现有 rev_run，窗口保留业务断言和最终清理。</summary>
    private async void OnRun(object sender, RoutedEventArgs e)
    {
        await StartOperationAsync("正在执行计划，按回执检查结果与清理…", async () =>
        {
            _runningPlan = true;
            UpdateControls();
            try
            {
                string body = await File.ReadAllTextAsync(PlanPath.Text.Trim());
                // 关闭可能发生在文件读取期间，此时还没有提交工单。
                if (_closing)
                {
                    return;
                }
                using var plan = JsonDocument.Parse(body);
                if (plan.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidOperationException("测试计划必须是 RevMCP JSON 对象");
                }
                JsonElement reply = await _session!.CallAsync("rev_run", new { plan = plan.RootElement, full = true }, isPlan: true);
                ShowResult(reply);
                SetFooter(DesktopTools.Failed(reply) ? "测试未通过，请查看失败步骤和清理回执。" : "测试计划已完成。");
            }
            finally
            {
                _runningPlan = false;
                UpdateControls();
            }
        });
    }

    /// <summary>发取消通知并等待计划正常返回，让其 finally 有机会执行。</summary>
    private async void OnCancel(object sender, RoutedEventArgs e)
    {
        await HandleSmallActionAsync(async () =>
        {
            if (await _session!.CancelPlanAsync())
            {
                SetFooter("已请求取消，等待测试清理回执。");
            }
            else
            {
                SetFooter("计划尚未提交或已经结束。");
            }
        });
    }

    /// <summary>显示或保存同一套命令对应的 Codex MCP 配置，供单独的 Agent 会话启动。</summary>
    private async void OnCopyConfig(object sender, RoutedEventArgs e)
    {
        await HandleSmallActionAsync(() =>
        {
            string[] args = DesktopTools.PythonArguments(Launcher, DesktopTools.AndroidEntry, BuildSessionArguments());
            string config = "[mcp_servers.revolution-android]\ncommand = " + JsonSerializer.Serialize(Launcher)
                + "\nargs = " + JsonSerializer.Serialize(args) + "\nstartup_timeout_sec = 305\ntool_timeout_sec = 300\n";
            Clipboard.SetText(config);
            SetFooter("已复制 MCP 配置。");
            return Task.CompletedTask;
        });
    }

    /// <summary>填写实际 APK 包名，读取过程无需手机。</summary>
    private async void OnInspectApk(object sender, RoutedEventArgs e) =>
        await StartOperationAsync("正在读取 APK 包名…", InspectApkAsync);

    /// <summary>通过相同 Python 验包入口推导 APK 包名，避免手填写错导致安装失败。</summary>
    private async Task InspectApkAsync()
    {
        if (string.IsNullOrWhiteSpace(ApkPath.Text))
        {
            throw new InvalidOperationException("请选择 APK 或填写已安装应用的包名");
        }
        JsonElement reply = await DesktopTools.QueryAsync(Launcher, DesktopTools.AndroidEntry,
            [..AdbArguments(), "inspect-apk", ApkPath.Text.Trim()]);
        PackageName.Text = reply.GetProperty("applicationId").GetString()!;
        ShowResult(reply);
        SetFooter("已按 APK 元数据填写包名。");
    }

    /// <summary>返回这次会话的整组启动参数，路径和串号各占一个参数。</summary>
    private string[] BuildSessionArguments()
    {
        AndroidDevice device = SelectedDevice ?? throw new InvalidOperationException("请先扫描并选择手机");
        if (device.State != "device")
        {
            throw new InvalidOperationException("请在手机上允许这台电脑调试后重新扫描");
        }
        string package = PackageName.Text.Trim();
        if (package.Length == 0)
        {
            throw new InvalidOperationException("请填写安卓包名或读取 APK 包名");
        }
        List<string> args = [..AdbArguments(), "--serial", device.Serial, "mcp", "--package", package, "--compact-tools"];
        AddOption(args, "--project", UnityProject(required: false));
        AddOption(args, "--apk", ApkPath.Text);
        AddOption(args, "--build-report", BuildReportPath.Text);
        if (Isolate.IsChecked is true)
        {
            args.Add("--isolate");
        }
        if (Replace.IsChecked is true)
        {
            args.Add("--replace");
        }
        if (Restart.IsChecked is true)
        {
            args.Add("--restart");
        }
        return args.ToArray();
    }

    /// <summary>ADB 可交给 CLI 自动查找。明确提供工具时则在本轮全部操作复用。</summary>
    private string[] AdbArguments() =>
        string.IsNullOrWhiteSpace(AdbPath.Text) ? [] : ["--adb", AdbPath.Text.Trim()];

    /// <summary>统一省略空值，让工具使用默认参数。</summary>
    private static void AddOption(List<string> args, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            args.Add(name);
            args.Add(value.Trim());
        }
    }

    /// <summary>确认是否选择了 Unity 工程根，手机单独测试时允许留空。</summary>
    private string? UnityProject(bool required)
    {
        string text = ProjectPath.Text.Trim();
        if (!required && text.Length == 0)
        {
            return null;
        }
        if (text.Length == 0 || !File.Exists(Path.Combine(text, "ProjectSettings", "ProjectVersion.txt")))
        {
            throw new InvalidOperationException("请选择含有 ProjectSettings 的 Unity 工程目录");
        }
        return Path.GetFullPath(text);
    }

    /// <summary>开始一项独占界面任务；取消与关闭仍可等当前任务安全退出。</summary>
    private Task StartOperationAsync(string status, Func<Task> action)
    {
        if (_busy || _closing)
        {
            return Task.CompletedTask;
        }
        _operation = PerformAsync();
        return _operation;

        async Task PerformAsync()
        {
            _busy = true;
            SetFooter(status);
            UpdateControls();
            try
            {
                await HandleSmallActionAsync(action);
            }
            finally
            {
                _busy = false;
                UpdateControls();
            }
        }
    }

    /// <summary>集中向用户说明进程、文件和连接错误，保留日志供排查。</summary>
    private async Task HandleSmallActionAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception error)
        {
            SetFooter(error.Message);
            AppendLog(error.Message);
        }
    }

    /// <summary>工具明确返回失败时保留其消息和原始回执。</summary>
    private static void RequireSuccess(JsonElement reply)
    {
        if (DesktopTools.Failed(reply))
        {
            throw new InvalidOperationException(DesktopTools.Message(reply, "操作未通过，请查看结果。"));
        }
    }

    /// <summary>离线状态和当前会话决定可操作入口，避免重复安装启动。</summary>
    private void UpdateControls()
    {
        bool idle = !_busy && !_closing;
        bool eligible = SelectedDevice?.State == "device";
        bool packageSet = !string.IsNullOrWhiteSpace(PackageName.Text);
        ConnectionSettings.IsEnabled = idle;
        SessionInputs.IsEnabled = _session is null;
        Devices.IsEnabled = _session is null;
        ConnectButton.IsEnabled = idle && _session is null && eligible &&
            (packageSet || !string.IsNullOrWhiteSpace(ApkPath.Text));
        DisconnectButton.IsEnabled = idle && _session is not null;
        CheckEditorButton.IsEnabled = idle && !string.IsNullOrWhiteSpace(ProjectPath.Text);
        IdentityButton.IsEnabled = idle && _ready && eligible;
        DiscoverButton.IsEnabled = idle && _ready;
        DeviceSampleButton.IsEnabled = idle && eligible && packageSet;
        CopyConfigButton.IsEnabled = idle && _session is null && eligible && packageSet;
        RunButton.IsEnabled = idle && _ready && eligible && File.Exists(PlanPath.Text.Trim());
        CancelButton.IsEnabled = _runningPlan && !_closing;
        ChoosePlanButton.IsEnabled = idle;
        PlanPath.IsReadOnly = !idle;
        ExampleButton.IsEnabled = idle;
        SaveResultButton.IsEnabled = CopyResultButton.IsEnabled = ResultText.Text.Length > 0 && !_closing;
        BusyProgress.Visibility = idle ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>呈现最近的 ADB 证据，断线时保留原来的会话串号。</summary>
    private void RefreshDeviceStatus()
    {
        if (SelectedDevice is { } device)
        {
            PhoneStatus.Text = $"{device.TransportLabel} · {device.StateLabel}";
        }
        else
        {
            PhoneStatus.Text = _session is null ? "尚未选到设备" : "会话设备未出现在 ADB";
        }
    }

    /// <summary>用户切换设备时立即检查可用状态。</summary>
    private void OnDeviceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            RefreshDeviceStatus();
            UpdateControls();
        }
    }

    /// <summary>修改工程时，清除上一工程的已连接标记。</summary>
    private void OnProjectChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded)
        {
            EditorStatus.Text = "待检查";
            UpdateControls();
        }
    }

    /// <summary>按习惯自动选中 APK 旁的构建报告。</summary>
    private void OnApkChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded)
        {
            string? folder = Path.GetDirectoryName(ApkPath.Text.Trim());
            string sidecar = string.IsNullOrEmpty(folder) ? "" : Path.Combine(folder, "rev-build.json");
            BuildReportPath.Text = File.Exists(sidecar) ? sidecar : "";
            UpdateControls();
        }
    }

    /// <summary>包名或计划变化后重新计算能否运行。</summary>
    private void OnSetupChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded)
        {
            UpdateControls();
        }
    }

    /// <summary>打开文件对话框并在取消时保留原设定。</summary>
    private bool SelectFile(TextBox field, string filter)
    {
        var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true };
        if (dialog.ShowDialog(this) != true)
        {
            return false;
        }
        field.Text = dialog.FileName;
        return true;
    }

    /// <summary>选择已打开的 Unity 项目以建立 Editor 路由。</summary>
    private void OnChooseProject(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择 Unity 工程" };
        if (dialog.ShowDialog(this) == true)
        {
            ProjectPath.Text = dialog.FolderName;
        }
    }

    /// <summary>选择 APK 后读取对应包名并替换旧选择。</summary>
    private async void OnChooseApk(object sender, RoutedEventArgs e)
    {
        if (SelectFile(ApkPath, "Android APK|*.apk"))
        {
            PackageName.Clear();
            await StartOperationAsync("正在读取 APK 包名…", InspectApkAsync);
        }
    }

    /// <summary>选择另一个 ADB 启动文件并立即刷新设备。</summary>
    private async void OnChooseAdb(object sender, RoutedEventArgs e)
    {
        if (SelectFile(AdbPath, "ADB|adb.exe|可执行程序|*.exe"))
        {
            await StartOperationAsync("正在扫描 ADB 设备…", ScanAsync);
        }
    }

    /// <summary>选择系统中的 Python 解释器后使用该版本。</summary>
    private void OnChoosePython(object sender, RoutedEventArgs e) => SelectFile(PythonPath, "Python|*.exe");

    /// <summary>选择与 APK 同批生成的构建报告。</summary>
    private void OnChooseReport(object sender, RoutedEventArgs e) => SelectFile(BuildReportPath, "构建报告|*.json");

    /// <summary>选择已存在的 RevMCP 计划，直到用户按运行才执行。</summary>
    private void OnChoosePlan(object sender, RoutedEventArgs e) => SelectFile(PlanPath, "RevMCP 计划|*.json");

    /// <summary>填入框架示例的计划路径。</summary>
    private void OnExample(object sender, RoutedEventArgs e) => PlanPath.Text = DesktopTools.ExamplePlan;

    /// <summary>保留结构化 JSON，便于查看、复制和导出。</summary>
    private void ShowResult(JsonElement result)
    {
        ResultText.Text = JsonSerializer.Serialize(result, Pretty);
        UpdateControls();
    }

    /// <summary>把提示同步到窗口底部，便于键盘操作时获知状态。</summary>
    private void SetFooter(string text) => FooterStatus.Text = text;

    /// <summary>跨线程保留最近诊断，避免长时间打开后无限累积。</summary>
    private void AppendLog(string line)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() => AppendLog(line));
            return;
        }
        LogText.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}\n");
        if (LogText.Text.Length > 24000)
        {
            LogText.Text = LogText.Text[^16000..];
        }
        LogText.ScrollToEnd();
    }

    /// <summary>用户主动复制时才占用系统剪贴板。</summary>
    private async void OnCopyResult(object sender, RoutedEventArgs e) =>
        await HandleSmallActionAsync(() =>
        {
            Clipboard.SetText(ResultText.Text);
            SetFooter("已复制 JSON 结果。");
            return Task.CompletedTask;
        });

    /// <summary>按用户所选路径导出结果，平时的回执仅留在窗口。</summary>
    private async void OnSaveResult(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "JSON|*.json", FileName = "rev-android-result.json" };
        if (dialog.ShowDialog(this) == true)
        {
            string result = ResultText.Text;
            await HandleSmallActionAsync(async () =>
            {
                await File.WriteAllTextAsync(dialog.FileName, result);
                SetFooter("已保存结果 " + dialog.FileName);
            });
        }
    }

    /// <summary>关闭前请求取消当前计划，再关闭 MCP 输入，让设备资源由原持有者清理。</summary>
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeAccepted || (_session is null && !_busy))
        {
            return;
        }
        e.Cancel = true;
        if (_closing)
        {
            return;
        }
        _closing = true;
        UpdateControls();
        try
        {
            if (_session is not null && _runningPlan)
            {
                await _session.CancelPlanAsync();
            }
            await _operation;
            await DisconnectAsync();
            _closeAccepted = true;
            Close();
        }
        catch (Exception error)
        {
            SetFooter(error.Message);
            AppendLog(error.Message);
        }
        finally
        {
            _closing = false;
            UpdateControls();
        }
    }
}
