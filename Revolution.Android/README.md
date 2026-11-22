# Revolution.Android

把 **Unity 编辑器、RevMCP 和 Android 真机**接到同一套自动化流程。编辑器继续用既有桥。手机上的测试包开启回环 HTTP，电脑用 USB 上的 ADB 把本机端口转发过去。RevMCP 从已注册的游戏动作拿状态、执行断言，把 JSON 结果原路交给电脑上的 Agent。 [ADB 官方文档](https://developer.android.com/tools/adb#forwardports)

```text
电脑 Agent ── MCP stdio ── RevMCP ── 既有桥 ── Unity Editor
                               └── 127.0.0.1 动态端口
                                      │ ADB forward / USB
                               Android 127.0.0.1 测试 Player
```

## 1. 先验线和测试包

- 准备 **Python 3.10+** 和 Android SDK 的 platform-tools。启用手机 USB 调试，用支持数据的线连电脑，在手机上允许这台电脑调试。多台设备时按实际串号选择一次。工具使用现有 ADB 服务，保留其他会话的映射。
- 从用户当前打开的 Unity 工程用 RevCLI `--mode live` 检查编辑态，切到 Android，等导入与编译稳定。按项目原流程更新 Android 资源包，隔离其他平台 AB，再用 `build.player` 的 `--automation --frame-timing` 出测试 APK。构建路径用本次空目录，核对 `rev-build.json` 和包中实际 AB。 [Android 构建与验包](https://github.com/Yokino337088/Revolution/blob/main/Skills/revolution-unity-editor/references/android-build-and-verification.md)
- 测试构建含 `REV_AUTOMATION` 或 `DEVELOPMENT_BUILD`。`build.player` 会为本次安卓测试包确保 `INTERNET` 权限，并恢复工程原值。测试 Activity 保留 Android 的 Intent 参数。项目若通过自定义 Activity 处理 Unity 启动参数，要保留传给 Unity 的 `unity` 参数。 [Unity 自定义 Android Activity](https://docs.unity3d.com/2022.3/Documentation/Manual/android-custom-activity.html)
- 示例用 RevGame 已登记的 `game.performance.*`。它需要 `--isolate`，计划启动前还会核对游戏确实使用隔离会话。其他游戏按自己的存档隔离和能力注册配对。测试包用独立应用标识便于把测试与正式存档分开。

### Windows 图形界面

WPF 桌面程序位于源仓库的 [Revolution.Android/Desktop](https://github.com/Yokino337088/Revolution/tree/main/Revolution.Android/Desktop)。在 Windows 使用 .NET 8 SDK 和 Python 3.10+，从框架仓库根运行下面的命令。编译输出的 EXE 和旁边的 `tools` 目录一起使用，其中已经包含同版本的三个 Python 工具。

```powershell
dotnet build Revolution.Android/Desktop/Revolution.Android.Desktop.csproj -c Release
& Revolution.Android/Desktop/bin/Release/net8.0-windows/Revolution.Android.Desktop.exe
```

1. **插线、解锁、扫描设备。** 开启手机 USB 调试并允许这台电脑。列表会显示串号、连接方式和授权状态，无法确认线路时写「连接方式待确认」。如果找不到 ADB，在「工具路径与构建报告」里选 Unity Android SDK 的 `platform-tools/adb.exe`。
2. **选需要连的 Unity 工程和 APK。** Unity 工程选含 `ProjectSettings` 的目录，检查编辑器可以单独进行。选 APK 后会读取包名，并填写它旁边存在的 `rev-build.json`。碰到旧版 aapt 读中文路径失败时，工作台会用临时副本完成验包。手机已装好同一测试包时，只填包名和相应构建报告。隔离测试存档、覆盖安装、重开已运行应用由用户选择。
3. **点击连接并启动。** 确认 MCP 和手机回执都通过后，按需发现动作、关心的时间窗外读取设备采样，或选择计划并运行。取消后等待计划的 `finally` 回执。结果可以复制或保存 JSON。断开会等待本轮的转发与测试进程收尾。

「复制 MCP 配置」会把选择好的启动参数复制为 Codex 配置。关闭桌面会话后，可以用同一个设备和同一份 APK 开自己的 Agent 会话。

在框架仓库根运行。下面的设备串号、包名、APK 与工程路径换成自己的实物。若 ADB 已在 PATH 或通过 `ANDROID_SDK_ROOT`、`ANDROID_HOME` 配置，`--adb` 可省略。

工具默认取相邻的 RevCLI 和 RevMCP。单独复制工具目录或从其它工程调用时，可用 `--tools-root` 指向同版本的框架仓库根或官方插件的 `tools` 目录。

```powershell
$adb = "D:/Android/Sdk/platform-tools/adb.exe"
$project = "D:/UnityBuilds/RevGameAndroid"
$apk = "D:/Builds/RevGameAndroid/Run01/RevGame.apk"
$serial = "实际设备串号"
$package = "实际安卓包名"

python Revolution.Android/rev_android.py --adb $adb devices
```

在 Editor 已切到 Android 且编译完成后，用同仓库的 RevCLI 出测试包。

```powershell
python Revolution.RevCLI/rev.py --project $project --mode live --timeout 3600 build player `
  --target Android --path $apk --backend il2cpp --automation --frame-timing
```

## 2. 跑一次并让 Agent 得到结果

```powershell
python Revolution.Android/rev_android.py --adb $adb --serial $serial run `
  --project $project --apk $apk --package $package --isolate --device-samples `
  --build-report (Join-Path (Split-Path $apk) "rev-build.json") `
  Revolution.Android/examples/revgame-perf.json
```

`--build-report` 可选，用来核对手机**实际运行的 Build ID**。通过 `--apk` 安装前会用 SDK 的 `aapt` 核对包名。已装好合适的测试 APK 时可以只传包名。若包已安装且要升级，显式加 `--replace`。若应用正在运行且本次确实允许重开，显式加 `--restart`。

`run` 用现成的 RevMCP 计划，支持文件或 `-` 从标准输入读取。示例会确认已隔离，经正式入口进入第 4 章并准备 81 棋，预热 5 秒并采两分钟，然后清理回首页。结果里需要一起看采样实际时长、开始和结束的 `runId`、前后棋数与攻击计数、长帧和回收。终端 JSON 包含设备、实际构建、完整计划回执和清理结果。需要核对内存或热状态时加 `--device-samples`，在计划边界读取系统 PSS（按比例分摊共享页后的进程占用）、热状态和电池温度。这些读数与 Unity 帧时间、GPU 时间各用各的口径。 [Android 系统诊断](https://developer.android.com/tools/dumpsys)

### 作为正式 MCP 入口复用同一台手机

把 Agent 的 MCP 命令配置为 Python，参数使用同样的路径和身份。示例用已安装的测试 APK，因此启动时只传包名。

```powershell
python Revolution.Android/rev_android.py --adb $adb --serial $serial mcp `
  --project $project --package $package --isolate --compact-tools
```

`rev_discover` 会发现实际 Editor 和手机能力。`rev_call` 的 `target=editor` 到编辑器，`target=game` 或 `gm` 到手机。多步业务、失败和收尾走 `rev_run`。同一连接可以反复跑计划，工具结束时关闭它启动并核验过的测试进程、移除它自己的映射和本机临时凭据。想留着画面观察可以传 `--keep-running`，下次重开仍按明确授权处理旧进程。

用 Codex MCP 配置时，给已安装的测试包留出启动时间。下面的路径和串号按本机填写。有需要再给 `rev_run` 调整工具超时，确保其覆盖完整预热、采样和清理。

```toml
[mcp_servers.revolution-android]
command = "python"
args = ["D:/RevolutionFramework/Revolution.Android/rev_android.py", "--adb", "D:/Android/Sdk/platform-tools/adb.exe", "--serial", "实际设备串号", "mcp", "--project", "D:/UnityBuilds/RevGameAndroid", "--package", "实际安卓包名", "--isolate", "--compact-tools"]
startup_timeout_sec = 120
tool_timeout_sec = 300
```

## 3. Unity 官方工具放在哪一步

| 工具 | 合作方式 |
| --- | --- |
| Unity Test Framework | 先跑 EditMode 规则和 PlayMode。需要在 Android Player 跑 PlayMode 时，用 Test Runner 的目标平台入口，或对应项目版本的 `-runTests -testPlatform Android -testResults <XML>`。XML 用下方命令读入同一份证据。 [Unity 命令行 Test Runner](https://docs.unity3d.com/Packages/com.unity.test-framework@1.1/manual/reference-command-line.html) |
| Unity Performance Testing Extension | 用于已知函数、资源加载和构建版本之间的可重复性能用例。版本从工程包清单核对。它的性能指标与整场真实战斗并列解读。 [Unity Performance Testing](https://docs.unity3d.com/Packages/com.unity.test-framework.performance@3.0/manual/index.html) |
| Unity Profiler | 对目标开发包单独采诊断轮，把 CPU 调用、渲染和内存分到各段。常规对照轮只保留轻量采样和游戏状态。 [Unity 连接 Android Player 的 Profiler](https://docs.unity3d.com/2022.3/Documentation/Manual/profiler-profiling-applications.html#android) |
| Device Simulator | 先查布局、安全区和屏幕比例，再到手机验实际输入、温度与帧时间。 [Unity Device Simulator](https://docs.unity3d.com/2022.3/Documentation/Manual/device-simulator.html) |

```powershell
python Revolution.Android/rev_android.py unity-results "D:/Evidence/AndroidPlayMode.xml"
```

也可给 `run` 加 `--unity-results`。工具把 Unity 测试结论和实际业务计划结论分别放在 `unityTests`、`plan`。Unity XML 由 Test Runner 自己生成，RevGame 计划由 RevMCP 自己执行。

## 4. 为什么这样接

- **Airtest（查阅时约 9.7k 星）与 Poco** 把设备接入和 Unity 层的可观察能力配合使用。这里的设备选择与 USB 连接在主机完成，业务状态沿项目已有注册表读取。 [Airtest](https://github.com/AirtestProject/Airtest) · [Poco](https://github.com/AirtestProject/Poco)
- **uiautomator2（约 8.5k 星）** 展示了按设备串号选择目标、把系统 UI 与主机工具连起来的做法。遇到安卓授权弹窗或系统栏，可用同类工具补做设备层检查。 [uiautomator2](https://github.com/openatx/uiautomator2)
- **scrcpy（约 155k 星）** 提供 USB 画面检查与手动操作，适合在定位轮观察手机。需要有效帧时对照时，把投屏与图形诊断安排到各自的测量窗口。 [scrcpy](https://github.com/Genymobile/scrcpy)

每次启动使用独立令牌和会话，电脑核对 Android 包名、启动 ID、进程与实际构建。主机端口由 ADB 分配，动作走原有接单、轮询、结果确认和取消。手机与电脑的时钟可以各自走，工单按手机收到时计算期限。可以用 `py -3.13 -m unittest discover -s Revolution.Android/tests` 在无设备环境验证这套协议与代理收尾。

## 验收时先看哪里

| 现象 | 核对 |
| --- | --- |
| ADB 空列表或 `unauthorized` | 数据线、USB 调试、手机上的授权提示。 |
| `/health` 没回执 | 实际 APK 的测试宏、Android 网络权限、Activity 启动、设备监听端口。查看该测试包的 Unity 日志。 |
| 构建或包身份不符 | APK 和 BuildReport 对应关系、目标设备串号、是否已经有旧进程。 |
| 采样完成但卡顿指标不对 | 实际攻击量、棋数、失焦帧、平均帧率和 P95/P99。把热状态、画质和显示刷新条件写进结果。 [安卓性能 Skill](https://github.com/Yokino337088/Revolution/blob/main/Skills/revolution-performance/references/android-performance.md) |
