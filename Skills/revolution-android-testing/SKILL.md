---
name: revolution-android-testing
description: 用 USB ADB 连接 Unity Android 测试 APK 与 Revolution RevMCP，执行真实设备业务、性能与清理验证，并合并 Unity Test Framework 的独立结果。适用于安卓实包、真机热态、ADB 身份错配及测试接线排查。
---

# Revolution Android 真机测试

以 [Revolution.Android README](https://github.com/Yokino337088/Revolution/blob/main/Revolution.Android/README.md) 为使用契约。源仓库运行 `Revolution.Android/rev_android.py`。Codex 插件提供并排的 `tools/Revolution.Android`、`Revolution.RevCLI` 与 `Revolution.RevMCP`。

## 构建和设备

1. 查项目规则与真实注册表。用 Android 目标的 APK 和正确资源包，保留实际 BuildReport、包名和 Unity 版本。测试包用 `build.player` 的 `automation`、`frameTiming` 配置。切目标、签名、ASCII 副本与 AB 验证沿 [Android 构建配方](../revolution-unity-editor/references/android-build-and-verification.md)。
2. 用 `devices` 读真实 ADB 状态，再锁定已授权的串号。复用用户已配置的 ADB。先确认设备与包身份，按用户授权安装、替换或重开。已有正式进程应保留，测试优先独立包和隔离存档。RevGame 使用 `--isolate` 并在计划中断言隔离。
3. `mcp --project <工程> --package <包名>` 可同时接已打开的 Editor 和 USB 真机。先 `rev_discover`，分别核对 Editor 与游戏的 action。手机使用 `game`／`gm`。`run` 可直接执行同一份 RevMCP 计划，支持 stdin JSON。连接的 `/health` 只证明到了正确运行时。

## 真机性能验收

- 先用正式入口让游戏进入测量状态，确认真实棋子、攻击或触发路径。用 `runtime.performance.begin/state` 验实际 runId、采样秒数、错误、失焦、P95/P99 和反馈等待。性能验收要适用 [Android 平台测量与取舍](../revolution-performance/references/android-performance.md)。
- 计划用 `finally` 停采样、收拾游戏业务和资源。工具归还本次认领的转发及私有凭据。发现设备重启或断线时保留首错，先重新确认身份再发新任务。
- `run --device-samples` 在计时窗外记录 PSS（进程占用口径）、系统热状态与电池温度。热态看连续同负载、同画质的实际时间窗，记录缺测项。
- Unity Test Framework 的 Android Player 测试与业务计划各有结论。可以用 `unity-results` 读 NUnit XML，或用 `run --unity-results` 汇总到同一回执。Profiler 的定位轮和正式性能对照分开。 [官方工具互补与实包步骤](https://github.com/Yokino337088/Revolution/blob/main/Revolution.Android/README.md#3-unity-官方工具放在哪一步)

向用户交代实际 APK、设备、动作、结果与清理。设备清单为空时，可以验证 SDK、构建和桌面协议，真机项按待设备接入记录。
