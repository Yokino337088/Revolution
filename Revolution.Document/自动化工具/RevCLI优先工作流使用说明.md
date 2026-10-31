# RevCLI 优先工作流使用说明

2026-11-01 · Unity 2022.3 · Python 3.10+ 标准库 · 示例路径均为相对路径

> 开工和重启后用 `connect` 一条命令恢复，全链路能力见 [RevCLI全链路开发与重启连接使用说明](RevCLI全链路开发与重启连接使用说明.html)。本文只讲通道与耗时。

## 一、分工规则

| 要做的事 | 走哪里 | 原因 |
|---|---|---|
| 场景、对象、组件、变换、RectTransform | **RevCLI** | 编辑器同步操作，不需要常驻 MCP 握手 |
| UI 面板制作（13 种 UGUI 控件） | **RevCLI** | `ui.create` + `rect.set`，Undo 完整 |
| Prefab 创建、实例化、修改、写回、解包 | **RevCLI** | `prefab.*` |
| 资产、Importer、材质、Shader | **RevCLI** | `asset.*`、`material.*`、`shader.list` |
| 项目设置、构建设置、打包 Player | **RevCLI** | `settings.*`、`build.*` |
| 编译、Console、Test Runner | **RevCLI** | `editor.compilation/console`、`test.*` |
| Revolution 全链路（AB、Excel、热更、平台资源） | **RevCLI** | `ab.*`、`excel.*`、`hot.*`、`platform.*` |
| 游戏运行时的 GM 指令、运行时业务、带断言的端到端测试 | **RevMCP** | 需要本局动态注册表、跨帧等待、`finally` 清理和证据报告 |

一句话：**编辑器里能做的事先用 CLI，Play 之后调 GM 才用 MCP。** CLI 在 Play 时也能调 `gm.exec`，只是没有计划编排、断言和自动报告，所以正式的运行时测试仍交给 `rev_run`。

## 二、调用时间

### 做了什么

1. 常驻服务：`daemon start` 起一个本机后台进程，之后每条命令只做一次本机 HTTP 转发。服务端复用 argparse 解析器和与 Unity 的连接。
2. 快速路径：`rev.py` 在导入 argparse 与整个 `cli` 模块之前先判断命令能不能转发。常驻服务在运行时跳过约 200ms 的导入。判断不确定时（未知选项、`--help`、本地专属命令）原样走完整 CLI，语义不变。
3. 本地专属命令（`daemon`、`doctor`、`gif`、`evidence`、`editor launch`、`pipeline --validate` 等）和 `--mode batch` 不转发，避免常驻服务成为 Unity 的父进程或抢工程锁。

### 实测（同一工程、只读 `editor.status`、15 轮交替取样）

| 方式 | 中位数 | P95 |
|---|---:|---:|
| 无常驻：`python rev.py --no-daemon` | 329 ms | 406 ms |
| **常驻 + 快速路径：`python rev.py`** | **201 ms** | 434 ms |
| 常驻 + `rev.cmd` | 390 ms | 608 ms |
| 常驻 + `py -3.13` 启动器 | 278 ms | 853 ms |
| 直连 `curl.exe → /call`（不起 Python） | 130 ms | 270 ms |

结论：

- 直接用解释器路径调用 `python rev.py` 最快。**不要用 `rev.cmd` 或 `py` 启动器**，两者分别多出约 190ms 和 77ms 的壳启动成本，比无常驻的直调还慢。
- 130ms 是 Unity 往返加进程启动的下限。要继续压，只能用 `curl` 直连或把多步合并成一次调用，见下节。
- 这些数字来自本机一次测量，换机器或 Unity 负载会变。复测命令见第六节。

### 多步操作：合并成一次往返

| 方式 | 适用 | 说明 |
|---|---|---|
| `call batch.call` | 互不依赖的同步动作 | 一次最多 64 步，默认遇错即停。实测 10 步约 111ms，拆成 10 次约 994ms |
| `pipeline <文件>` | 有顺序依赖、需要引用前一步结果或 `finally` 清理 | 失败后仍执行 `finally` |

`batch.call` 不接受 Play、编译、构建、测试和 `gm.*`/`ui.dump` 等依赖帧推进的动作。

## 三、使用流程

```powershell
$py   = '<Python 解释器路径>'                       # 先 py -3.13 -c "import sys;print(sys.executable)"
$rev  = 'Revolution.RevCLI/rev.py'
$proj = '<Unity 工程绝对路径>'

& $py $rev --project $proj connect                    # 每个工作会话一次，含打开 Unity 和 daemon start；可重复执行
& $py $rev --project $proj --mode live editor status  # 之后都走常驻服务
& $py $rev --project $proj daemon status              # 看是否在跑
& $py $rev --project $proj daemon stop                # 收工
```

约定：

- 始终显式传 `--project`。已有 Unity 编辑器在运行时只用 `--mode live`，不另开 batch。
- 参数含中文或复杂对象时用 `call <动作> --json <文件>`，避免 PowerShell 转义破坏数据。
- 常驻服务发现 CLI 代码或清单更新后会自动退出，该次调用转本地执行，再跑一次 `connect` 即可。改了 Unity 桥后 `editor wait` 等编译完成。
- 没有启动常驻服务时，所有命令仍然可用，只是每次多付约 130ms 的进程与导入成本。

## 四、新增的制作能力

### UI 面板制作

| 动作 | 参数 | 要点 |
|---|---|---|
| `ui.create` | `kind`（Canvas/EventSystem/Panel/Image/RawImage/Text/Button/Toggle/Slider/Scrollbar/Dropdown/InputField/ScrollView）、`name`、`parent`、`scene` | 除 Canvas、EventSystem 外必须在 Canvas 之下；Text 是内置 `UnityEngine.UI.Text`；支持 Undo |
| `rect.get` | `id` 或 `objectPath`、`scene` | 返回锚点、轴心、位置、尺寸、偏移、本地矩形、所属 Canvas |
| `rect.set` | 同上，加 `anchorMin/anchorMax/pivot/anchoredPosition/sizeDelta/offsetMin/offsetMax` | `offsetMin/offsetMax` 与 `anchoredPosition/sizeDelta` 二选一；只写给出的字段 |

注意：单独改锚点会让矩形的实际位置变化。要保持位置，同时给出偏移。

### Prefab 全流程

| 动作 | 说明 |
|---|---|
| `prefab.create` | 场景对象保存为新 Prefab，不覆盖已有资产 |
| `prefab.instantiate` | 实例化到指定场景和父节点 |
| `prefab.inspect` / `prefab.modify` | 不打开窗口，直接读写子节点或组件属性并保存 |
| `prefab.apply` | 把实例的**全部覆盖**写回 Prefab 资产并保存。只允许 `Assets` 下的 Prefab；写盘不可 Undo |
| `prefab.unpack` | 解除实例与资产的关联，不可 Undo。`unpackMode=root`（默认，仅最外层）或 `completely`（含嵌套） |

`prefab.apply` 和 `prefab.unpack` 都不可撤销，调用前确认用户授权。

### 一个完整例子：做一个带按钮的面板并存成 Prefab

```powershell
& $py $rev --project $proj --mode live ui create --kind Canvas --name MainCanvas
& $py $rev --project $proj --mode live ui create --kind Panel  --name Panel  --parent <Canvas 的 id>
& $py $rev --project $proj --mode live ui create --kind Button --name BtnOk  --parent <Panel 的 id>
& $py $rev --project $proj --mode live rect set --id <Panel 的 id> --set anchorMin=[0,0] --set anchorMax=[1,1] --set offsetMin=[0,0] --set offsetMax=[0,0]
& $py $rev --project $proj --mode live rect get --id <Panel 的 id>        # 读回断言
& $py $rev --project $proj --mode live prefab create --id <Panel 的 id> --path Assets/UI/Panel.prefab
```

对象 `id` 来自上一条命令的返回，只在当次域有效；域重载后重新获取。

## 五、给 AI 的路由规则

- 编辑器操作先 `commands --action <动作>` 或 `commands --live` 查真实契约，再调用。离线清单只是说明，不代表已安装。
- 修改前先 `inspect/get`，修改后用同一查询读回断言。
- 编译、测试和构建用显式等待：`editor.wait`、`test.result`、`build.player` 的真实 BuildReport。
- 只在需要运行时 GM、本局业务状态、跨帧断言或证据报告时切到 RevMCP（`rev_run`）。
- CLI 与 MCP 共用同一个 Unity 桥，同一份参数契约，不会各自维护一套动作。

## 六、验证与复测

| 项 | 命令 | 本次结果 |
|---|---|---|
| Unity EditMode 全量 | `call test.run`（`mode=EditMode`，程序集 `Revolution.Automation.EditorTests`） | 146 项，其中制作动作 40 项；最后一次通过 146、失败 0 |
| 调用耗时 | `python Revolution.RevCLI/tests/benchmark_daemon.py --project <工程> --samples 15 --output <文件>` | 见第二节 |
| 实际注册动作 | `commands --live` | 138 个 Editor 动作；`commands.json` 共 141 项，含 3 个 Python 侧动作 |

已知情况：全量测试跑了三次，结果分别是 3 项失败、1 项失败、0 项失败。失败原因都是 Unity 在测试期间重写 `Assembly-CSharp.csproj` 时的 `IOException: Sharing violation`，随后被测试框架当作未处理日志。这是编辑器自身对 csproj 的写入竞争，与动作逻辑无关，换次重跑即消失；CI 若遇到，可在测试前先 `editor.wait` 等编译与导入静止。

以上为 2026-11-01 上午的阶段数字。全链路在隔离工程上的最新结果见 [全链路文档](RevCLI全链路开发与重启连接使用说明.html)。

## 七、边界

- 不提供任意 C# 或 shell 执行。
- `batch.call` 不能包含 Play、编译、构建、测试和运行时动作。
- 常驻服务只监听本机，请求带令牌头，空闲超时自动退出。
- 清单里存在的动作不等于旧编辑器已安装；以目标工程的实时 `commands --live` 为准。
