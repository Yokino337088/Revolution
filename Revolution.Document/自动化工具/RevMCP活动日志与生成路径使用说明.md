# RevMCP 活动日志与 RevResPath 生成路径

更新：2026-10-29。适用 Unity 2022.3、Python 3.10+。本次增强让你看得懂 AI 的操作，并可以选择 RevResPath.cs 的生成位置。RevSoundPath 的生成方式与位置沿用原版。

## 1. 打开窗口，先看 AI 在做什么

Unity 菜单：`Revolution.Tools/AI/RevMCP 控制台`。默认打开“活动日志”，另一页是“连接设置”。顶栏显示工程和游戏状态，常用的“复制 Agent 配置”就在右上方。

每条记录先用中文说明操作，再说明用途，例如：

| 原始命令 | 窗口里的说明 | 有什么用 |
|---|---|---|
| editor.status | 检查编辑器是否就绪 | 确认工程、编译和游戏状态，判断能否继续测试 |
| editor.refresh | 重新导入修改后的资源 | 让 Unity 发现代码与资源变化；还需等待编译完成 |
| ui.click | 模拟玩家点击控件 | 经真实 EventSystem 触发 UI，检查玩家能否完成操作 |
| gm.exec | 执行 GM 指令：自动化演示/增加 2 | 显示实际注册指令的说明和完整参数 |
| runtime.screenshot | 截取游戏画面作为证据 | 保存本次画面，配合业务数值断言验收 |
| rev_run | 执行一份测试计划 | 按步骤操作、核对断言，最后执行清理 |

时间、来源、执行中/成功/失败/取消和耗时在每条记录底部。点击一条记录，查看“在做什么”“有什么用”“AI 说明的目的”、原命令、参数和结果摘要。有编辑器工单时，可点“工单”打开证据目录。

## 2. AI 的目的与接口用途怎样区分

接口名称和 Description 能解释动作的用途，但不能知道 Agent 心里的想法。所以窗口明确区分两种来源：

- “有什么用”：来自确定的内置说明或接口注册的 Description。
- “AI 说明的目的”：来自 Agent 显式提供的 intent；没传时显示“Agent 没有提供目的说明”。

新版 `rev_call` 和动态工具可传外层 `intent`（最多 512 字符）：

```json
{
  "target": "editor",
  "action": "editor.status",
  "intent": "确认脚本编译完成后，再测试增加按钮",
  "arguments": {}
}
```

intent 只用于说明，不进入业务 arguments，不改变权限，也不会被当作另一条执行指令。外部 Agent 的思考、编辑文件或通过其它工具做的事情，不属于 RevMCP 日志覆盖范围。

测试计划也可以说明目的，步骤的 intent 优先于计划。finally 是收尾阶段，独立记录，不因失败或取消跳过。

```json
{
  "name": "检查编辑器就绪",
  "intent": "确认当前工程可以继续自动化测试",
  "steps": [
    {
      "target": "editor",
      "action": "editor.status",
      "intent": "核对编译状态",
      "expect": [{"path": "/data/compiling", "equals": false}]
    }
  ],
  "finally": []
}
```

## 3. 日志查看与筛选

可以按来源、执行状态筛选，搜索中文说明、原命令、参数、计划名或 intent。“自动跟随最新”会滚动到最新记录，点选历史记录后暂停跟随。“复制可见日志”只复制筛选结果，“清空视图”只清屏，不删工单与证据。

Agent 请求、Unity Editor 实际执行、游戏运行时、GM、外部 CLI、计划断言分别显示。一次跨层调用可能出现数条相关记录，这是同一次请求的不同执行层；每层自己的开始与完成使用同一 id 合并。HTTP 接单、单条调用成功和计划断言通过是不同结果。

界面每 250ms 读取增量文件，最多显示最近 500 条。日志在 `Library/RevolutionAutomation/activity`，Python 和 Unity 使用不同文件，不会竞争写同一个流。每个流最多保留四个约 2MiB 的分段；窗口只读最近 64 个流文件。旧进程留下的文件可以在证据不再需要时自行清理。常见 token、password、Authorization 等字段和截图 base64 不进入日志摘要；业务私密内容仍应避免放进自由文本。

脚本重载或重新打开窗口后重新读取本地账本。纯独立 Player 的 Unity 日志在 persistentDataPath 的 RevolutionAutomation/activity；MCP 使用 `--project` 时 Agent 调用记录会同时落入该工程的活动目录。Unity 窗口不主动扫描任意远程设备的文件。

CLI 也可只读查询：

```powershell
python Revolution.RevCLI/rev.py --project <工程绝对路径> --mode live call editor.activity --set limit=20
python Revolution.RevMCP/rev_mcp.py --project <工程绝对路径> call editor editor.status --intent "确认本工程编译完毕"
```

`editor.activity` 返回 entries 和 summaries（中文 operation、purpose、agentIntent）。窗口自刷新不通过 MCP，所以不会出现“查看日志又不断生成查看日志”的循环。

## 4. 连接设置更简单了

默认连接可调用 Editor，也可在 Play 中操作 GM/UI，无须启动 HTTP。按“复制默认连接配置”，把 JSON 添加到 Agent 客户端，然后先让 Agent 调用 rev_discover 确认工程。

“高级连接”提供 Python 命令、rev_mcp.py 文件选择和 HTTP 启停。脚本会在当前工程及上级目录中查找明确命名的 Revolution.RevMCP；找不到时可以选择实际文件。端口、凭据路径与命令行细节收在高级区，初次使用不需要先理解所有技术项。暂停游戏时也可停止 HTTP 服务。

升级需要同步 Unity 桥、Revolution.RevMCP 和相邻 Revolution.RevCLI，再重启 Agent 的 MCP 会话并重新发现工具。仅更新 Unity 时仍可看到 Unity 实际执行；外部 CLI、计划断言和 intent 需要新版 Python 服务。

## 5. 指定 RevResPath.cs 的生成路径

打开 RevAB 打包工具 → “打包” → “代码生成”。新增的“RevResPath 生成路径”支持：

| 输入 | 实际输出 |
|---|---|
| 留空或点“默认” | Assets/Revolution/Generation/RevResPath.cs |
| Assets/GameScripts/Generated | Assets/GameScripts/Generated/RevResPath.cs |
| Assets/GameScripts/Generated/RevResPath.cs | 这个文件 |
| 当前工程 Assets 内的绝对目录 | 自动转成 Assets/.../RevResPath.cs |

“选择…”用于选择目录；“实际输出”显示最终落点。点击生成，“打开 RevResPath.cs”打开同一位置。配置保存在 ABBuildConfig.resPathCodePath，重编译/重启不会丢。菜单生成、一键打包、仅生成映射、自动重生成和 CLI 使用相同位置。

输出路径放的是 C# 文件；资源根目录 resRoot 是生成器扫描资源的位置，两者不要混淆。类名继续是 Revolution.RevResPath。若自定义目录属于另一个 asmdef，使用常量的业务程序集要引用它；不能让 Runtime 反向依赖业务程序集。

迁移时只移动带本生成器标记的旧文件并保留 GUID，避免出现两份同名类。已有手写代码不覆盖，新旧位置都有生成物时明确报错。不能生成到工程外、Editor 目录、../ 路径或改成其它文件名。框架以只读 UPM 包安装时沿用原生成限制，先用 Assets 安装方式验证。

Agent 设置示例：

```json
{"configJson":"{\"resPathCodePath\":\"Assets/GameScripts/Generated\"}"}
```

将这份参数文件传给 `ab.configure`，再执行 `ab.res-paths`。传空 resPathCodePath 后再次生成，即移动回默认位置。RevSoundPath 仍使用原有音效配置和默认生成路径，本次不改音效加载接线。

## 6. 相关文档

- [RevMCP 完整使用说明](RevMCP使用说明.html)
- [RevMCP 架构解析](RevMCP架构解析.html)
- [本次验证记录](验证记录/RevMCP活动日志与生成路径测试_2026-10-29.html)
