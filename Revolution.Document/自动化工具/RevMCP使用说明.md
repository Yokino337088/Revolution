# RevMCP · AI 控制编辑器与游戏使用说明

更新：2026-10-22。适用：Python 3.10+，Revolution 当前源码，Unity 2022.3。无需安装 Python 第三方库。

RevMCP 把 Unity Editor、游戏实际注册的 GM、运行时业务能力和项目登记的 CLI/Python 工具提供给 Agent。Agent 把一句话转成调用计划，工具负责执行、等待、断言、截图和收尾。玩法必须提供真实接口，工具不会凭空理解未开放的游戏业务。

## 1. 为什么需要 Unity 内的 RevMCP

有必要提供 Unity 内插件。它负责知道当前工程、Play 状态、真实命令和 Unity 主线程；外部服务负责 MCP 协议与 Agent 连接。当前实现采用两端配合：Unity 内有桥和控制台，外部运行 Python MCP 服务。独立开发版 Player 复用同一个运行时注册表。

```text
第一层  人的一句话：点击增加，再用 GM 加 2，验证计数 3，并截图
第二层  Agent + revolution-ai-automation Skill：发现、编排、判断结果
第三层  RevMCP：rev_discover / rev_call / rev_run + 动态工具
        ├─ Editor 文件工单 → Revolution.Automation
        ├─ 游戏注册表 → GM / 业务能力 / UGUI / 状态 / 截图
        └─ 显式清单 → CLI / Python 子进程
第四层  Unity 主线程 → 玩家共用的 Service / Logic / UI 入口
```

第三层是稳定契约。每个接口明确名称、参数、环境、是否只读、是否需要 confirm、结果和错误。Agent 先发现真实注册表，再调用。可与已有 Unity MCP 共存；两个连接不能同时修改同一工程。

维护者接线、线程/工单生命周期与扩展选型详见 [RevMCP 架构解析](RevMCP架构解析.html)。

## 2. 文件放在哪里

| 路径 | 职责 |
|---|---|
| `Revolution.RevMCP/rev_mcp.py` | 人/CI 的 CLI 与 Agent 的 stdio MCP 入口 |
| `Revolution.RevMCP/revmcp/` | 协议、统一调度、运行时 HTTP、外部进程和测试编排 |
| `Revolution.Automation/` | Editor CLI、文件工单和旧 GM scenario；保留相邻目录 |
| `Assets/Revolution/Editor/RevAutomation/` | Editor 接线与 RevMCP 控制台 |
| `Assets/Revolution/Runtime/RevAutomation/` | 开发态游戏接口注册表、UI/截图及本机 HTTP |
| `Assets/Revolution.Demo/RevAutomation.Demo/` | 可直接运行的教学场景 |
| `Skills/revolution-ai-automation/` | 一句话测试 Skill 源码 |

复制到其他项目时，同时保留 Python 两个工具目录和 Unity 里的框架代码。只拷贝 Python 无法控制没有桥的工程。无需为了使用 Editor 文件桥而开启 HTTP。

## 3. 新手先跑通 Demo

以下命令在仓库根目录的 PowerShell 执行。`--project` 填的是含 Assets/ProjectSettings 的 Unity 工程目录。先用 Unity 打开该工程并等待编译完成。

```powershell
$project = '<本仓库绝对路径>/RevolutionFrameWork_Unity'
$env:PYTHONIOENCODING = 'utf-8'
python Revolution.Automation/rev.py --project $project --mode live editor status
python Revolution.Automation/rev.py --project $project --mode live editor coverage
python Revolution.Automation/rev.py --project $project --mode live editor play --scene 'Assets/Revolution.Demo/RevAutomation.Demo/RevAutomationDemo.unity'
python Revolution.RevMCP/rev_mcp.py --project $project discover
python Revolution.RevMCP/rev_mcp.py --project $project run Revolution.RevMCP/examples/ui-gm.plan.json
python Revolution.Automation/rev.py --project $project --mode live editor stop
```

成功计划会完成：计数归零 → 查找真实按钮 → EventSystem 点击加 1 → GM 加 2 → 精确断言 3 → 截图 → 检查错误数 → finally 归零。截图中计数为 3，执行结束后状态为 0，这是清理成功的正常表现。

Unity 菜单 `Revolution.Tools/AI/RevMCP 控制台` 可查看工程/Play/GM 数量、启停 HTTP、复制配置和打开工单目录。编辑模式没有 game/GM 能力时，discover 的 unavailable 会给出原因；Editor 能力仍然可以使用。

## 4. 接到 Agent

菜单中点击“复制 MCP 配置（Editor 模式）”，把配置放入所用 Agent 的 MCP 设置。窗口仅复制配置，不修改全局设置。以下是使用 mcpServers JSON 的客户端示例；其他客户端按其实际配置格式填写同一 command 和 args。

```json
{
  "mcpServers": {
    "revolution": {
      "command": "python",
      "args": [
        "<本仓库绝对路径>/Revolution.RevMCP/rev_mcp.py",
        "--project", "<本仓库绝对路径>/RevolutionFrameWork_Unity",
        "serve"
      ]
    }
  }
}
```

把 command 改为本机 Python 的绝对路径可避免 PATH 差异。所有全局参数放在 serve/discover/call/run 前。stdio 由 Agent 启动并持有；命令行直接运行 serve 后等待输入属于正常状态。stdout 只输出 MCP JSON-RPC，调试信息不能混入 stdout。

上面示例里的 `<本仓库绝对路径>`、`<Python 3.10+ 安装目录>` 都是占位符，不是让你照抄的路径：MCP 客户端只接受绝对路径，请替换成你自己机器上的目录（本文档不写死任何人的机器路径）。

安装 Skill：把仓库 `Skills/revolution-ai-automation` 整个目录复制到你所用 Agent 的 skills 目录（例如 Codex 的 `~/.codex/skills/`、Claude 的 `~/.claude/skills/`）。复制后若工具列表没刷新，开一个新会话即可。

连接后可说：

> 使用 revolution-ai-automation，测试 RevAutomationDemo：点击增加按钮，再通过 GM 增加 2，断言计数为 3，截图并清理归零；给我结果和证据。

真实业务也可以说“测试背包添加道具后数量与格子刷新”，前提是项目已注册对应查询、动作与复位接口。Agent 根据实际接口组织测试，不要求用户手写 JSON。

## 5. 三个稳定 MCP 工具

| 工具 | 输入 | 作用 |
|---|---|---|
| `rev_discover` | `{}` | 读取当前 Editor、game、GM、CLI 能力及不可用原因 |
| `rev_call` | target/action/arguments/confirm | 调用一个真实注册接口 |
| `rev_run` | `{ "plan": { ... } }` | 执行带断言、等待和 finally 的完整测试 |

tools/list 还会把真实接口生成 `editor_*`、`game_*`、`gm_*`、`cli_*` 动态工具。动态工具名包含稳定摘要，中文 GM 完整名保留在说明中。动态工具参数是 `{ "arguments": { ... }, "confirm": false }`。场景/注册表变化后重新刷新工具列表或使用 rev_discover，再用稳定 rev_call 调用；服务不声明主动 listChanged 推送。

```json
{
  "target": "gm",
  "action": "自动化演示/增加",
  "arguments": { "arg0": 2 }
}
```

GM 参数使用 arg0/arg1 等位置名，类型、必填、枚举和 Int32 范围由真实 RevGMArg 推导。只允许完整注册名；HighRisk 必须 confirm=true。Hidden 是显示标记，不是权限隔离。GM 本体是否允许执行仍由 RevGM.Enabled 控制。

## 6. 注册自己的游戏能力

业务应先提供玩家共用入口，再登记受控能力。最小完整范例见 `Assets/Revolution.Demo/RevAutomation.Demo/RevAutomationDemo.cs`。下面是接入结构，`inventory` 是项目已有的业务服务，需要替换为你的实际实现。

```csharp
#if UNITY_EDITOR || DEVELOPMENT_BUILD || REV_AUTOMATION
// 在业务启动处登记一次。查询可轮询，动作调用玩家同一个服务。
RevRuntimeAutomation.Register(
    "inventory.state", "查询真实背包数量",
    "{\"type\":\"object\",\"additionalProperties\":false}",
    _ => new InventoryState { count = inventory.Count },
    readOnly: true, owner: this);

RevRuntimeAutomation.Register(
    "inventory.add", "测试账号调用正常添加道具入口",
    "{\"type\":\"object\",\"properties\":{\"amount\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":100}},\"required\":[\"amount\"],\"additionalProperties\":false}",
    json => {
        var args = JsonUtility.FromJson<AddArgs>(json);
        inventory.Add(args.amount);
        return new InventoryState { count = inventory.Count };
    }, owner: this);

// 场景退出/服务释放时清掉自己登记的委托。
RevRuntimeAutomation.UnregisterAllOf(this);
#endif
```

返回 DTO 要声明 `[Serializable]`，使用可序列化字段；也可返回字符串键字典、数组、数字、布尔或 null。失败抛出带原因的异常。不要返回 Unity 对象、任意复杂服务、循环引用或把 `success=false` 藏在普通业务 DTO 中代替工单失败；跨帧能力用 RevRuntimeOperation.Success/Message 明确结果。

回调只在 Unity 主线程调用，必须快速返回。网络登录/战斗等异步流程由现有 RevTask/业务生命周期启动，另提供 readOnly 状态查询；需要直接等待多帧时返回 RevRuntimeOperation，在 Tick 返回 true 时结束，并实现 Cancel/Dispose。取消不能撤销已发生的业务效果。

Schema 支持：单一 type（object/array/string/integer/number/boolean/null）、properties、required、additionalProperties 布尔值、items、enum、minimum/maximum、min/maxLength、min/maxItems，以及 description/title/default。default 只作说明，不自动补值；不支持 oneOf、远程引用或正则 pattern。未知关键字在注册时拒绝。数字内核使用 double，不承诺任意 Int64 无损；大 ID 用字符串。

## 7. 内置运行时能力与 UI

| 能力 | 用途 |
|---|---|
| `runtime.state` | 活动场景、帧、timeScale、作用域/服务/计时器/UI 状态和错误数 |
| `runtime.logs` | 最近 128 条日志、当前局累计 Error/Exception/Assert 数 |
| `runtime.ping` | 运行实例产品名和平台 |
| `runtime.screenshot` | 等待完整 PNG，返回路径、base64、mimeType，MCP 转为 image 内容 |
| `ui.dump` | 分页查询 UGUI/TMP Selectable、文本、可交互与中心射线可达状态 |
| `ui.click` | 目标 id/objectPath，真实 EventSystem 指针事件，拒绝禁用/中心遮挡 |
| `ui.input` | 输入框 text；submit=true 时触发结束编辑 |
| `ui.back` / `ui.close` | 使用 RevUI 的真实返回/关闭入口 |

先 dump 再点击当次 id；同名路径会拒绝，跨场景/重载后 id 过期。通用点击测试的是控件中心，不能代表所有边缘位置。非 UGUI 控件、拖拽、摇杆、战斗移动、虚拟列表等应登记项目专用能力。输入接口设置控件文本，不模拟输入法逐键交互。场景切换可以登记调用 RevScene 的能力，由业务清理和查询实际就绪。

截图保存在 `Application.persistentDataPath/RevolutionAutomation/screenshots/`，最大 8 MiB PNG。MCP 一次响应最多附带 4 张图片；报告省略重复 base64 并保留图片路径。截图需要图形渲染环境，不应用无图形模式验视觉。

## 8. 独立开发版 Player

勾选 Development Build 出包。启动时显式指定：

```powershell
& '<你的构建目录>/Player.exe' -revAutomation -revAutomationPort 18767
```

也可在开发态启动入口调用 `RevRuntimeAutomation.Start(18767)`。默认端口 18766。Editor 控制台启用服务时凭据在工程 `Library/RevolutionAutomation/runtime.json`；Player 在 `Application.persistentDataPath/RevolutionAutomation/runtime.json`。凭据含随机 token、session、pid 和本机 URL，不提交 Git、不贴到聊天。

```powershell
$endpoint = "$env:USERPROFILE\AppData\LocalLow\DefaultCompany\RevolutionFrameWork_Unity\RevolutionAutomation\runtime.json"
python Revolution.RevMCP/rev_mcp.py --runtime-endpoint $endpoint discover
python Revolution.RevMCP/rev_mcp.py --runtime-endpoint $endpoint run Revolution.RevMCP/examples/ui-gm.plan.json
```

Company/Product 改名后上述路径也会变化，以实际 persistentDataPath 为准。Player 的 MCP 配置将 --project 换成 --runtime-endpoint 与实际路径。游戏必须运行并持续刷新帧；服务启动期间临时启用 runInBackground，停止时恢复。

HTTP 是运行时内部传输，不是直接提供给 Agent 的 MCP Streamable HTTP；Agent 仍连接 Python stdio。只监听 loopback，要求 Bearer token 和 session，拒绝浏览器 Origin，不自动跟随重定向。POST /call 接单、GET /jobs/id 查询、DELETE /jobs/id 取消。队列最多 16，单 session 最多保存 1024 工单，达到后结束本轮再重启服务；旧请求不重放。

正式构建默认不包含运行时测试模块，除非项目主动定义 REV_AUTOMATION。WebGL 无本机 HttpListener，此版本明确拒绝；手机/小游戏真机、跨机器连接与端口转发未在本次验证。不要将本机监听直接改为公网监听。

## 9. 显式登记 CLI / Python

外部工具使用 JSON 清单，不扫描硬盘并执行任意脚本。复制 `Revolution.RevMCP/examples/external-tools.example.json`，按本机修改 command 和 cwd。Python.exe 用 `python -c "import sys; print(sys.executable)"` 查询。command 是固定参数数组，第一个元素必须为真实绝对可执行文件；py 文件由 Python.exe 启动。

```json
{
  "version": 1,
  "tools": [{
    "name": "project.echo",
    "description": "教学：返回收到的文本",
    "command": ["<Python 3.10+ 安装目录>/python.exe", "<本仓库绝对路径>/Revolution.RevMCP/examples/echo_tool.py"],
    "cwd": "<本仓库绝对路径>",
    "inputSchema": {"type":"object","properties":{"text":{"type":"string"}},"required":["text"],"additionalProperties":false},
    "readOnly": true, "confirm": false, "timeoutSeconds": 10, "output": "json"
  }]
}
```

```powershell
python Revolution.RevMCP/rev_mcp.py --project $project --external-manifest '<你的配置目录>/tools.json' discover
```

Agent 用 target=cli、action=project.echo、arguments={"text":"你好"}。参数通过 UTF-8 JSON stdin 传递；stdout 返回 JSON（或 output=text），stderr 用于日志。进程非零退出或 JSON 顶层 success=false 判失败。shell=False，不直接启动 cmd/bat/ps1；原工具只接受 argv 时，为它写固定 JSON 包装脚本。输出合计限制 4 MiB。取消会终止直接子进程，工具自己派生的进程应自行管理生命周期。

## 10. 带断言与清理的计划

计划顶层 name/steps/finally；步骤 target/action/arguments，可选 id/confirm/expect/until。完整可执行示例为 `Revolution.RevMCP/examples/ui-gm.plan.json`。

```json
{
  "name": "验证按钮增加",
  "steps": [
    {"target":"game","action":"demo.reset"},
    {"id":"button","target":"game","action":"ui.dump","arguments":{"query":"AutomationCanvas/Increment"},"expect":[{"path":"/data/total","equals":1}]},
    {"target":"game","action":"ui.click","arguments":{"id":{"$ref":"button#/data/items/0/id"}}},
    {"target":"game","action":"demo.state","until":{"path":"/data/count","equals":1,"timeoutSeconds":5,"intervalSeconds":0.2}},
    {"target":"game","action":"runtime.screenshot"}
  ],
  "finally": [{"target":"game","action":"demo.reset","expect":[{"path":"/data/count","equals":0}]}]
}
```

断言 path 是完整响应中的 JSON Pointer：结构化数据通常在 /data/count，GM 文本在 /data/message。支持 equals 或 exists，不能同时提供；精确比较，布尔值与数字不同。$ref 必须单独构成一个对象，只能引用先前步骤，保留数字、布尔、对象等原类型。

整份计划先预检，引用值在实际调用前再次验证。until 仅允许 readOnly=true 的查询，必须有期限，不能自动重复变更。单次请求期限由 --timeout 控制（RevMCP 最大 3600 秒）；条件期限可能要等一个已提交请求结束后才被观察到。finally 在失败、取消后继续逐条执行，清理失败使总结果失败。断言失败后的已执行动作不自动回滚。finally 尽量使用不依赖前面成功步骤的幂等复位。

三个格式不要混用：RevMCP plan 用 target/action/arguments；Automation pipeline 用 action/payload；旧 GM scenario 用 kind/command，并提供 repeat/warmup/cleanup/teardown 与性能采样。循环和性能测试仍可用 scenario，详见 [Automation 使用说明](Revolution自动化工具使用说明.html)。

## 11. 结果、故障与实际边界

CLI 退出码 0 表示顶层成功；MCP 看 isError 和 structuredContent.success，并核对每步业务断言、日志和 cleanup。结果默认写 `Revolution.RevMCP/results/`，可用 --reports 修改。Editor 原始工单在 `Library/RevolutionAutomation/jobs/`。不能以 progress、握手成功或按钮 dispatched 代替玩法通过。

| 现象 | 处理 |
|---|---|
| Editor unavailable | 检查 --project、桥是否编译、是否正在重载；已有工程不另开 batch |
| game/GM 不可用 | 进入目标场景 Play，确认注册/RevGM.Enabled；重新 discover |
| HTTP 401 / SessionChanged | 检查凭据属于当前 pid/session；不能重放刚才可能已执行的变更 |
| Busy / RuntimeBusy / TestsRunning | 等待或取消原工单后核对副作用；Editor/HTTP 串行操作 |
| 参数小数/拼错字段被拒绝 | 按真实 inputSchema 修改，不靠静默类型转换 |
| UI 中心被挡 | 先排查真实遮罩/滚动位置；不要绕过射线直接 Invoke 点击事件 |
| 测试被取消但引擎仍运行 | Test Framework 1.1 不支持公开强制取消；查询 test.result，保持变更门禁到完成 |
| 报告失败但业务似乎完成 | 查看各步、Unity Error、断言和清理结果；不能忽略清理失败 |

## 12. 2026-10-22 验证记录

本次本机验证覆盖 Python 自动化 19 项、RevMCP 13 项、Unity EditMode 24 项。真实 MCP stdio → Editor 文件桥，以及 stdio → 本机 HTTP → Windows Development Player 均完成 UI、GM、计数断言、PNG 和归零清理。Windows64 BuildReport 成功，0 错误、0 警告。当前实际 20 个 Revolution.Tools 菜单均有命令映射；离线清单 125 个动作，示例工程实时 122 个（另 3 个由 Python 客户端实现）。

首次集成曾发现重载恢复、测试夹具场景管理和 Player 输出目录问题，已修复后复测；零匹配测试会明确失败。本记录不代表每个编辑器动作、任意项目业务或所有平台逐一通过。Android、IL2CPP、小游戏和真机性能未验证。

脱敏证据见 [本次验证记录](验证记录/2026-10-22-RevMCP.md)，包括可核对的计数、构建摘要和截图；本机原始凭据/日志不入库。
