# Revolution 安卓测试台

在手机上选择测试游戏，**由手机主动连接开发电脑**。电脑端 Agent 使用已有的 Revolution 插件或独立 RevMCP，向同一台手机下发游戏动作、GM 命令和压力测试计划。测试结果沿手机建立的 HTTPS 连接返回。局域网模式全程可拔掉 USB，手机无需开启 USB 调试。

```text
开发电脑                                             Android 手机
Agent → RevMCP → 电脑接线器（HTTPS 服务端） ← 主动连接 ← 原生测试台
                     │                              │
                     └─ 下发动作，接收结果、帧率 ←───┘
                                                    └─ 游戏回环 HTTP → Unity Player / RevGM
```

## 安装

- 仓库随附可直接安装的调试 APK，支持 Android 8.0 或以上。[下载仓库 APK](https://github.com/Yokino337088/Revolution/raw/refs/heads/main/Revolution.Android/Android_APP/dist/Revolution-Android-Test-Companion.apk)
- GitHub Actions 也会生成 `Revolution-Android-Test-Companion` 制品，解压后可取得 `app-debug.apk`。[APK 构建记录](https://github.com/Yokino337088/Revolution/actions/workflows/android-companion.yml)
- 从本目录自行构建可用 Android Studio 或 JDK 17、SDK Platform 35、Build Tools 34.0.0 和仓库自带的 Gradle Wrapper。Windows 运行 `.\gradlew.bat :app:assembleDebug`，macOS 或 Linux 运行 `./gradlew :app:assembleDebug`。输出文件为 `app/build/outputs/apk/debug/app-debug.apk`。CI 和本机均使用调试签名。不同签名无法覆盖安装，更新时应沿用同一签名。
- 电脑需要 Python 3.10+。Windows 双击 `start-test-service.cmd`，脚本会检查接线器依赖并输出局域网 IP 和监听端口。首次缺少依赖时在脚本目录准备虚拟环境。其他平台可在本目录运行 `python -m pip install -r requirements.txt`，再运行 `python connect.py lan --stay-open`。
- 安装游戏测试包时，使用带 `DEVELOPMENT_BUILD` 或 `REV_AUTOMATION`、保留启动 Intent 且有 INTERNET 权限的 Android Player。现有 `build.player --automation` 会准备网络权限，GPU 帧时另受平台、图形驱动和 Frame Timing Stats 支持限制。[框架 Android 构建说明](https://github.com/Yokino337088/Revolution/blob/main/Revolution.Android/README.md)

## 手机通过局域网接入

1. 手机和电脑连接同一局域网。在电脑双击 `start-test-service.cmd`，使用窗口显示的 IP 和端口。首次授权使用窗口上最新显示的八位配对码，等待时每三分钟自动更新。多网卡时可在终端运行 `.\start-test-service.cmd --listen 192.168.1.20` 指定网卡，端口默认 18767，支持 `--port`。
2. 手机打开「Revolution 测试台」，选择已安装游戏或本地 APK。列表优先展示本机验证过的安装版本和 Unity／游戏候选。自定义启动 Activity 可在「显示其他可启动应用」中找到。APK 经过系统安装器确认。同包同版本号也可以选择重新安装该文件。最终以实际运行的 Build ID 为准。
3. 在「局域网 HTTPS」输入电脑地址，点「连接电脑」。首次连接出现授权面板，核对指纹并填入八位码，再点「指纹一致，授权此电脑」。以后从测试台直接点「连接电脑」，同一台电脑重启服务也能使用保存的授权。电脑 IP 变化时更新地址即可。
4. 点击「启动测试游戏」。游戏需要冷启动以读取本轮 Intent，已运行的游戏请先从游戏内正常退出。
5. 电脑只有在确认包名、Android 平台、启动号、进程和 Build ID 后才输出私有 `runtime.json` 的路径。可在启动时额外传 `--package <包名> --build-report <rev-build.json>` 锁定预期游戏和构建。手机游戏保持前台，测试台通过前台服务维持连接。

测试台的「清除已保存授权」可让手机忘记电脑。电脑运行 `python connect.py revoke` 可撤销所有手机的重连授权，已开始的测试可执行完毕。授权记录位于当前用户的私有目录，Windows 使用 `%LOCALAPPDATA%\Revolution\AndroidCompanion`。

## 游戏悬浮窗

在测试台打开「在游戏上显示悬浮窗」，到 Android 系统设置为 Revolution 测试台开启「显示在其他应用上层」。启动游戏后可拖动「测试台」手柄，点击展开面板，查看游戏状态、收起、返回测试台或断开本轮连接。回到测试台时暂隐藏，断开后移除。游戏界面以外的系统安全页面可能暂时隐藏浮窗。

「Agent 操作记录」可在测试台或游戏悬浮窗中打开。展开后手机才读取电脑留存的工单和回执，收起时取消日志请求并清空手机的分页缓存。同一会话重新打开可补看关闭期间的操作。按每页 40 条向下读取更早记录，点击单行展开摘要，点「回到最新」恢复跟随。电脑为当前会话最多保留最近 4096 条，手机复用屏外行。

## Agent 下发指令并收结果

已安装 Revolution 插件时，将游戏对应的 Unity 工程绝对路径和电脑打印的端点路径交给 Agent。一次绑定可使用以下参数。

```json
{"project":"D:/Games/MyGame","runtimeEndpoint":"D:/Private/rev-phone/runtime.json"}
```

调用 `rev_bind`，再用 `rev_discover` 查当前 Player 登记的能力。通过 `rev_call` 指定 `target=game` 或 `target=gm`。多步测试用 `rev_run`，在 `expect` 检查实际游戏状态，在 `finally` 恢复测试数据。编辑器操作仍由已绑定工程的 `target=editor` 负责。以下两条命令从仓库根分别启动独立 RevMCP、运行性能计划。

```powershell
python Revolution.RevMCP/rev_mcp.py --runtime-endpoint $endpoint --compact-tools serve
python Revolution.RevMCP/rev_mcp.py --runtime-endpoint $endpoint --full run Revolution.Android/Android_APP/examples/android-performance.json
```

`$endpoint` 取电脑打印的完整路径。固定 Agent 配置可在起服务时传 `--endpoint D:/Private/rev-phone/runtime.json`，目录需已存在且文件名空闲。端点含本轮认证凭据，仅保存在用户私有目录，正常断开时自动回收。

## 电脑控制台

服务窗口用不同颜色标出 MCP、RevGM、安卓、回执、性能和失败。连接时显示监听地址、包名、进程和 Build ID。工单日志使用真实的 MCP 工具名、计划步骤、GM 命令、耗时及游戏返回数据，`rev_run` 完成后显示实际断言和收尾结论。性能采样完成后打印 FPS、P50/P95/P99、样本数、真实时长、失焦及错误。旧版 RevMCP 只提供游戏动作和回执，升级至同版本插件可显示 MCP 来源。终端重定向时默认为纯文本，可用 `--color always` 或 `--color never` 切换。

## 真机性能压测

- 先让游戏通过项目自身的入口进入指定的战斗、棋子数量或 UI 压力场景，用它实际登记的只读状态核对负载。随附的通用计划测量当前前台场景，预热 3 秒，采样 30 秒。支持的单轮采样范围是 1–600 秒。重复压测应逐轮恢复相同负载。[通用 Android 性能计划](https://github.com/Yokino337088/Revolution/blob/main/Revolution.Android/Android_APP/examples/android-performance.json) · [RevGame 第四章 81 棋计划](https://github.com/Yokino337088/Revolution/blob/main/Revolution.Android/examples/revgame-perf.json)
- Agent 在计时窗前后读 `runtime.android.device`，获取游戏进程 PID、PSS（KiB）、电池温度（°C）和 Android 系统热状态（API 29+）。这些值由游戏进程调用安卓系统取得，无需 ADB。旧游戏测试包需更新框架并重建才能登记这个新增动作。未提供的设备指标返回空值。
- `runtime.performance.begin` 返回 `runId`。用 `runtime.performance.state` 等到 `completed`，取同一 `runId` 的实际秒数、帧数、平均 FPS、P50/P95/P99 与最长帧时、超 33.3 ms 比例、失焦帧、运行错误和有效 GPU 样本。平均 FPS 等于实际帧数除以实际秒数。GPU 无有效样本时分位数为 -1，`gpuRejectedSamples` 给出被剔除的读数。用 `full=true` 或 CLI `--full` 取得完整回执。
- 手机持续挂起一条 HTTPS 请求，电脑收到 Agent 指令立即交付。手机向选定游戏的回环端点提交原始工单，接收其状态、JSON 或截图，并在下一次请求中回传。运行时 `POST /call` 表示接单，最终结果仍由 RevMCP 轮询、断言和确认。断线不会自动重放业务变更。通知栏、测试台「断开连接」或电脑 Ctrl+C 均可结束接线，已执行的游戏操作由计划负责收尾。

## 可选 USB

手机允许 USB 调试后，电脑运行 `python connect.py usb`。有多台设备时加 `--serial <串号>`，SDK 不在环境变量中时加 `--adb <adb 路径>`。手机选择 USB 并点击连接，首次授权后同一电脑可直接接入。电脑通过 `adb reverse --no-rebind` 将手机回环端口接入电脑监听器。退出时只移除本次创建的映射。

## 常见接线结果

| 看到的状态 | 核对位置 |
| --- | --- |
| 手机未取得电脑证书 | 核对电脑网卡地址、监听端口、防火墙和 Wi-Fi 客户端隔离。 |
| 配对被拒或证书变动 | 核对本轮电脑窗口中的指纹和配对码。每轮只接一台手机。 |
| 游戏打开但没有发布端点 | 确认已从旧进程退出，测试包包含框架自动化代码并保留启动 Intent，包和 BuildReport 匹配。 |
| 能连游戏但没有项目 GM | 用实时注册表查看当前场景和测试构建启用了哪些命令。 |
| 意外断线 | 电脑撤销端点，手机结束会话。重新配对并查询游戏现状后再决定下一步。强制杀掉电脑进程时可在确认它已经退出后清理遗留私有端点。 |

界面封面使用 Vishnu Mohanan 拍摄的电路板照片，由 Unsplash 授权。 [照片](https://unsplash.com/photos/green-and-black-circuit-board-uXFKr39XNBM) · [Unsplash 许可](https://unsplash.com/license)
