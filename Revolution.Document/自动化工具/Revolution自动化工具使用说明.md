# Revolution 自动化工具 · 新手使用说明

更新：2026-10-22，版本 2.0。Python 3.10+，仅标准库。工具用命令行驱动 Unity 编辑器和 Play 中的真实游戏接口；需要 Agent MCP 或独立开发版 Player，请读 [RevMCP 使用说明](RevMCP使用说明.html)。

## 1. 先理解三个角色

Revolution.Automation 是 Python CLI；Unity 里的 RevAutomationBridge 在主线程执行工单；游戏业务由项目已有 Service/Logic/GM 实现。工具不会代写任意玩法。Editor 实际安装能力从 editor.capabilities 获取，commands.json 只用于离线帮助。

```text
CLI 写 request → Unity 轮询 inbox → 校验参数与环境
             → 主线程执行/逐帧等待 → response.json
             → CLI 读取结果，导出报告，返回退出码
```

| 位置 | 内容 |
|---|---|
| Revolution.Automation/rev.py / rev.cmd | 命令行入口 |
| Revolution.Automation/commands.json | 动作名、参数白名单、类型、运行环境 |
| Revolution.Automation/revautomation/ | Python 客户端、pipeline、报告和校验 |
| Assets/Revolution/Editor/RevAutomation/ | Editor 桥与各类适配器 |
| Assets/Revolution/Runtime/RevAutomation/ | 共用游戏注册表与 UI/截图实现 |
| Unity工程/Library/RevolutionAutomation/ | 本机会话、工单、进度、结果，不提交 Git |

## 2. 最快连通

先用 Unity 打开正确工程，等待脚本编译。以下从仓库根目录执行；其他项目请替换 --project。

```powershell
$project = '<本仓库绝对路径>/RevolutionFrameWork_Unity'
$env:PYTHONIOENCODING = 'utf-8'
python Revolution.Automation/rev.py --project $project doctor
python Revolution.Automation/rev.py --project $project --mode live editor status
python Revolution.Automation/rev.py --project $project --mode live commands --live
python Revolution.Automation/rev.py --project $project --mode live editor coverage
```

需要工具启动 Unity 时指定真实 Unity.exe 路径，再执行 editor launch。已有工程绝不另开 batch 争用。

```powershell
$env:UNITY_EDITOR = '<Unity 安装目录>/2022.3.15f1c1/Editor/Unity.exe'
python Revolution.Automation/rev.py --project $project --timeout 300 editor launch
python Revolution.Automation/rev.py --project $project --mode live editor wait
```

全局参数 --project、--unity、--mode、--timeout、--output、--confirm、--no-graphics 放在子命令前。默认 timeout=120 秒，按每个 Unity 请求计时。rev.cmd 默认 py -3，可通过 REV_PYTHON 指定解释器；直接 python rev.py 更便于定位环境。

## 3. 发现命令与覆盖范围

```powershell
python Revolution.Automation/rev.py commands
python Revolution.Automation/rev.py commands --action component.set
python Revolution.Automation/rev.py --project $project --mode live commands --live --action component.set
```

当前离线清单 125 项，示例工程实时 Editor 122 项；editor.launch/editor.wait/scenario.validate 在 Python 本地实现。可选 HotUpdate、Demo、Test Framework 未安装时，相应真实动作不可用。menu coverage 会核对当前实际 Revolution.Tools 菜单的精确映射；新增菜单未登记会显示 covered=false。它不是对每个动作执行效果的验收报告。

| 动作组 | 手动操作的命令入口 |
|---|---|
| editor.* | 状态、刷新、能力/菜单覆盖、Console、编译、暂停/恢复/单帧、选择、Undo/Redo |
| scene.* | 已加载场景、层级、新建、打开、保存、关闭、活动场景 |
| gameobject.* | 查找、创建、读取/修改 Inspector、父子关系、复制、删除 |
| component.* | 组件/类型发现、添加、读写、移除 |
| asset.* | 搜索、资产/Importer 序列化读写、导入、建目录、创建 ScriptableObject、复制/移动/删除 |
| prefab.* | 实例化、创建、读取与保存 Prefab 子节点/组件 |
| settings.* | Player/Tags/Layers/Input/Graphics 等 .asset 项目设置读写 |
| build.* | 构建场景配置、切平台、真实 Player BuildReport |
| test.* | Test Runner 发现、运行、结果；无匹配测试不算通过 |
| runtime.* / ui.* | Play 中的状态、日志、UI、截图、HTTP 启停/共用能力调用；同时保留 UI 代码生成动作 |
| gm.* / scenario.* | 真实 GM 与带循环/清理/采样的业务测试 |
| ab.* / excel.* / platform.* / hot.* | 现有 AB、导表、裁剪、体积、热更窗口背后的真实服务 |

不能经通用序列化入口实现的第三方按钮或项目业务，需要用 RevAutomationAction 扩展专用动作。editor.menu 只打开受支持的工具窗口，真正执行使用专用接口。

## 4. 参数怎么传

优先写 UTF-8 JSON 文件，再调用 call 动作ID --json 文件。这样不用在 PowerShell 中手工嵌套转义。

```json
{"query":"AutomationFixture","includeInactive":true,"offset":0,"limit":100}
```

```powershell
python Revolution.Automation/rev.py --project $project --mode live call gameobject.find --json find.json
```

简单数字/布尔可以 --set limit=20、--set includeInactive=true。字符串 --set 仍需合法 JSON 双引号，因此中文/复杂值更适合文件。未知字段、错误类型、小数传整数和 Int32 溢出会拒绝。

对象返回 id（instance:数字）、完整类型名、层级路径和场景。instance id 只对当前会话/对象有效；重载、删除、换场景后重新查。重名路径会拒绝。分页结果有 total/offset/truncated，必要时继续下一页。

## 5. 修改场景、组件与资产

先 inspect/get 读取真实 property 名与类型，再 set；Inspector 显示名不一定等于序列化路径。以下以查到的 Transform 组件 id 为目标：

```json
{
  "id":"instance:用本次查询的真实组件ID替换",
  "changes":[{"property":"m_LocalPosition","value":"{\"x\":1,\"y\":2,\"z\":3}"}]
}
```

changes.value 固定为字符串：布尔写 "true"，数字写 "3"，向量写 JSON 对象字符串，对象引用写本次 id 或 "null"。只支持代码明确适配的序列化类型；不可写属性会拒绝。整组 changes 先检查再一次 Apply，使用 SerializedObject/Undo 保留 Unity 的正常编辑语义。

```powershell
python Revolution.Automation/rev.py --project $project --mode live component list --id 'instance:真实GameObjectID'
python Revolution.Automation/rev.py --project $project --mode live component get --id 'instance:真实组件ID'
python Revolution.Automation/rev.py --project $project --mode live call component.set --json transform.json
python Revolution.Automation/rev.py --project $project --mode live scene save
```

场景修改产生 dirty，需明确 scene.save 才落盘。切场景、构建或测试前遇未保存内容会失败，保护现有工作。删除对象/组件/资产等标记动作需要全局 --confirm；它表示调用参数，不替代用户授权。

asset.find 支持 Unity 搜索语法 t:Prefab；Importer 修改用 asset.importer-set 并重新导入。prefab.inspect/modify 使用 asset path，objectPath 为相对 Prefab 根的子路径，省略表示根。Prefab 内容结束后立即卸载，返回的临时对象身份不能用于后续调用。项目设置 path 为 ProjectSettings/*.asset。

## 6. Console、编译、测试与构建

```powershell
python Revolution.Automation/rev.py --project $project --mode live editor console --limit 100
python Revolution.Automation/rev.py --project $project --mode live editor compilation
python Revolution.Automation/rev.py --project $project --mode live test list --test-mode EditMode
python Revolution.Automation/rev.py --project $project --mode live --timeout 300 test run --test-mode EditMode
```

test.run 可用 JSON 提供 mode/testNames/categories/assemblies 过滤；assemblies 填真实 asmdef 名，例如本仓库 Revolution.Automation.EditorTests。测试结果必须 passed>0、failed/skipped/inconclusive=0 才通过。跨重载只恢复等待，不重复 Execute。Test Framework 1.1 没有公开强制取消，取消 CLI 等待后仍阻止编辑器变更，直到测试引擎完成；用 test.result 查询原 id。

build.configure 的参数：

```json
{"scenes":[{"path":"Assets/你的场景.unity","enabled":true}]}
```

build.player 的参数：

```json
{"target":"StandaloneWindows64","development":true}
```

```powershell
python Revolution.Automation/rev.py --project $project --mode live build settings
python Revolution.Automation/rev.py --project $project --mode live call build.configure --json build-scenes.json
python Revolution.Automation/rev.py --project $project --mode live --timeout 600 call build.player --json build-player.json
```

跨平台先 build.target 等实际切换完成，要求本机安装目标支持。构建只用启用场景，输出默认 Unity工程/Builds/RevAutomation/工单ID/Player.exe 等平台对应产物，报告仍在 Library 工单。自定义 path 必须位于本工程 Builds 内的新空目录，不覆盖旧产物。只有真实 BuildReport 成功且无错误才判成功。构建本身可能长时间占用 Unity 主线程，心跳变旧不能作为另开编辑器的理由。

## 7. Pipeline：编辑器步骤、引用、断言和清理

```json
{
  "name":"创建并验证临时对象",
  "steps":[
    {"id":"created","action":"gameobject.create","payload":{"name":"AutomationTemporary"}},
    {"action":"gameobject.inspect","payload":{"id":{"$ref":"created#/data/id"}},"expect":[{"path":"/data/target/name","equals":"AutomationTemporary"}]}
  ],
  "finally":[
    {"action":"gameobject.delete","payload":{"id":{"$ref":"created#/data/id"}}}
  ]
}
```

示例需要 --confirm 允许清理删除。场景 dirty 仍需管理。引用不存在（例如创建失败）会记录清理失败；测试优先提供不依赖成功步骤的幂等清理入口。

```powershell
python Revolution.Automation/rev.py --confirm pipeline workflow.json --validate
python Revolution.Automation/rev.py --project $project --mode live --confirm --output pipeline-result.json pipeline workflow.json
```

每步可用 payload 或 file（二选一，file 相对流程目录），id、expect、until。expect 是 JSON Pointer + equals/exists；$ref 保留原类型，仅引用此前步骤。until 只允许清单声明 readOnly 的查询，包含 timeoutSeconds/intervalSeconds。整份流程先静态预检，发生错误停止工作步骤，finally 逐条尝试。不是事务，不自动回滚/重试变更。

生成代码/导入后用 editor.wait 等新心跳稳定、无编译/导入和编译错误，再进入 Play。需要跨 Editor、Player、GM、外部工具的计划用 RevMCP target/action/arguments 格式。

## 8. GM 与旧 scenario 接入

```powershell
python Revolution.Automation/rev.py --project $project --mode live editor play --scene 'Assets/你的启动场景.unity'
python Revolution.Automation/rev.py --project $project --mode live gm list
python Revolution.Automation/rev.py --project $project --mode live gm exec '你的业务/真实完整命令 参数'
```

业务提供准备、动作、状态/断言、每轮复位、全场收尾。GM 调玩家同一个服务，注册/注销对称，限定开发环境。编辑模式 gm.list 的注册入口快照不等于 Play 真实注册表。RevGM.Execute 同步返回；异步业务通过状态命令有界等待，不能主线程忙等。

```json
{
  "name":"业务循环模板",
  "repeat":10,"warmup":0,
  "stopOnFailure":true,"failOnUnityError":true,
  "setup":[{"kind":"gm","command":"业务/准备"}],
  "steps":[
    {"kind":"gm","command":"业务/执行 {round}"},
    {"kind":"until","command":"业务/状态","expected":"完成","timeoutSeconds":30,"intervalSeconds":0.1},
    {"kind":"assert","command":"业务/断言","expected":"通过"},
    {"kind":"wait","frames":2}
  ],
  "cleanup":[{"kind":"gm","command":"业务/复位"}],
  "teardown":[{"kind":"gm","command":"业务/收尾"}],
  "thresholds":{"maxFrameP95Ms":0,"maxFrameMs":0,"maxMonoUsedBytes":0}
}
```

“业务/…”都是待接入模板，不能在未注册时宣称执行。until/assert 比较 GM Message 精确字符串，不执行表达式或模糊包含；复杂业务用专门查询或 RevMCP 结构化能力。setup 全场一次、cleanup 每轮执行、teardown 准备失败后仍尝试。repeat=1..1000000，warmup=0..1000000；{round} 含预热总轮号从 0 开始，{iteration} 正式轮从 0 开始、预热固定 0。

```powershell
python Revolution.Automation/rev.py scenario validate Revolution.Automation/examples/business.scenario.json
python Revolution.Automation/rev.py --project $project --mode live --timeout 86400 scenario run Revolution.Automation/examples/business.scenario.json --repeat 10000 --warmup 10
```

scenario 必须图形 Editor Play，不支持 batch。每帧最多推进一次步骤/查询，wait 秒数和超时用墙钟；编辑器暂停会中断。预热失败同样使整体失败。性能阈值为 0 表示关闭，不能说性能达标；Editor 帧间隔不是设备 CPU/GPU 耗时。

## 9. 运行时、MCP 与扩展动作

runtime.call 接受 runtimeAction 与 argumentsJson 字符串，在 Editor 中转发给共用注册表；runtime.state/screenshot、ui.dump/click/input/back/close 也有直接适配。runtime.start/stop 管本机 HTTP，Player 配置和业务注册详见 [RevMCP 教程](RevMCP使用说明.html)。

```csharp
[RevAutomationAction("project.check", Environment = "either", Keys = new[] { "path" }, Description = "核对项目资源规则")]
public static object Check(RevAutomationRequest request) {
    // 解析并校验自己的参数，调用实际服务，失败抛带原因的异常。
    return new RevAutomationActions.RevAutomationText("检查结果");
}
```

方法必须 static、一个 RevAutomationRequest 参数、非 void。只在实际编译安装后出现在实时目录。Environment 默认为 edit，可指定 either/live/play；Confirm 标记显式确认要求。跨帧返回 RevAutomationOperation；只有明确可恢复等待的动作才声明 ResumeAfterReload，不能重放导出或变更。离线 help/schema 还需同步 commands.json。新增 Revolution.Tools 菜单也要补 editor.coverage 精确映射。

## 10. 工单、取消与报告

```powershell
python Revolution.Automation/rev.py --project $project job '32位工单ID'
python Revolution.Automation/rev.py --project $project job '32位工单ID' --cancel
python Revolution.Automation/rev.py report '你的scenario-result.json' --directory '报告目录'
```

一个请求一个 jobs/id 目录。request.json 是请求，progress.json 是进度，response.json 是完成回执；scenario 另有 scenario-result.json、iterations.jsonl、unity-errors.jsonl 和导出的 HTML/CSV/JSON。CLI 解包后的业务数据在 data。退出码非零、response.success=false、业务断言失败、轮数不足或清理失败都不能算通过。

Ctrl+C/超时会请求取消；同步主线程函数不能被强行抢占，已发生效果不自动回滚。域重载默认标失败不重放；平台切换/Test Runner 等声明恢复能力的工单只继续等待。Editor/HTTP 共用变更门禁，Busy/TestsRunning 时先看原工单。

## 11. CI 与常见问题

关闭工程后，资产操作可用 batch；Play 回归应由图形编辑器 live 或开发版 Player 运行。示例：

```powershell
python Revolution.Automation/rev.py --project $project --mode batch --timeout 600 ab validate --target Android
if ($LASTEXITCODE -ne 0) { throw '资产校验失败' }
```

| 问题 | 排查 |
|---|---|
| 没有在线桥 | 确认工程路径、桥已安装、Unity Console 无编译错误 |
| 参数无效 | 查 commands --action 或实时能力；不要把 scenario/plan/pipeline 混用 |
| 未保存场景 | 保留原内容，明确保存到目标路径后再切场景 |
| 目标不唯一或 id 失效 | 按场景重新查对象与组件，用新 id |
| GM 不存在/高危拒绝 | 查 Play 实际 gm list，完整名/参数和 --confirm |
| 脚本生成后连接旧会话 | editor.wait 等待稳定，再发现能力 |
| 超时 | 查看原 id 的工单及已发生副作用，不自动重新执行 |

## 12. 本次验证口径

2026-10-22：Automation Python 19 项、RevMCP Python 13 项、Unity EditMode 24 项通过；MCP stdio 到 Editor 和独立 Windows 开发版 Player 的按钮/GM/断言/PNG/清理均通过，Player BuildReport 0 错误、0 警告。当前20个菜单映射完整。完整 [脱敏记录](验证记录/2026-10-22-RevMCP.md) 含截图。

本次未对全部 122 个 Editor 动作逐一做集成验收，未测 Android/IL2CPP/小游戏真机，也未跑任意业务一万轮或设备性能。业务项目需登记自己的真实契约；工具返回成功不能代替业务断言。
