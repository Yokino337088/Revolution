# AI 如何调用游戏 GM 指令（原理讲解）

> 说明：本文是 RevMCP + RevCLI + RevAutomationBridge 的链路原理，面向新手；所有结论均标注了对应的文件与行号。HTML 版为可独立浏览版，内容与本文一致。

## 目录

- 先回答三个疑问
- 五个角色与一条命令的旅程
- 传输层：其实就是“写 JSON 文件”
- 三种执行模式：live / batch / player
- MCP 这一层做了什么
- GM 的调用入口（代码级）
- 实时决策，还是提前编排？
- RevGame 里的三个真实例子
- 安全边界：为什么它不会乱动你的工程
- 新手常问的 8 个问题
本文所有结论都标了对应的文件与行号，可自行核对；不含推测。

## 1. 先回答三个疑问

| 你的疑问 | 直接答案 | 证据 |
|---|---|---|
| AI 是写一个 JSON / py 文件“注入”进去吗？ | 一半对：确实是写文件，但不是“写脚本注入”。AI 只发一条JSON 动作请求；Python 是常驻服务，不是每次生成。 | revautomation/client.py:143-153（请求 JSON + 投递 inbox） |
| 是提前编排好命令，还是 AI 实时决策？ | 两种都支持，默认是实时。 · 实时：AI 看动作目录 → 选一条 → 执行 → 看结果 → 再决定下一步； · 预编排：AI 先写一份 plan（steps/expect/until/cleanup），一次提交，由 rev_run 串行执行。 | revmcp/server.py（rev_call / rev_run 两个工具）、revmcp/workflow.py（计划执行） |
| GM 指令的调用入口在哪？ | Unity 编辑器侧只有一个入口：RevAutomationActions.ExecuteGM(line, confirm) → 校验 Play 状态与高危标记 → RevGM.Execute(line)。游戏里能用哪些 GM，取决于项目自己 RevGM.Register 注册了什么。 | Editor/RevAutomation/RevAutomationActions.cs:363-386 |

> 一句话总结：AI 不“控制 Unity”，它只是往一个约定好的文件夹里放请求；Unity 编辑器自己每帧去看有没有新请求，执行完再写回结果。MCP 只是把这个过程包装成 AI 能调用的工具。

## 2. 五个角色与一条命令的旅程

```
① AI 客户端（CodeBuddy / Codex）
        │  调用工具：rev_call { target:"gm", action:"gm.exec", arguments:{command:"RevGame/Boss技能 3"} }
        ▼
② RevMCP（Python，stdio JSON-RPC 常驻进程）
        │  校验 schema → 走 backends → 复用 RevCLI 的客户端
        ▼
③ RevCLI（Python，文件工单 RPC）
        │  写 Library/RevolutionAutomation/inbox/<uuid>.json
        ▼
④ Unity 编辑器：RevAutomationBridge（[InitializeOnLoad]，每编辑帧轮询）
        │  取工单 → 执行 → 写 Library/RevolutionAutomation/jobs/<uuid>/response.json
        ▼
⑤ 游戏运行时：RevGM.Execute("RevGame/Boss技能 3")
        │  找到项目注册的 handler，真正在 Play 中的游戏里执行
        ▼
   结果沿原路返回 → MCP 转成 AI 能读的 JSON
```

| 角色 | 是什么 | 代码位置 |
|---|---|---|
| AI 客户端 | 你的 CodeBuddy / Codex，通过 MCP 协议与 ② 通信 | 客户端配置 mcp.json |
| RevMCP | MCP 服务端：把“动作”包装成工具，管 schema、串行、取消 | Revolution.RevMCP/revmcp/server.py |
| RevCLI | 真正的传输实现：文件工单（也能单独命令行用） | Revolution.RevCLI/revautomation/client.py |
| 编辑器桥 | Unity 侧的“收发室”，每帧取工单、执行、写回执 | Assets/Revolution/Editor/RevAutomation/RevAutomationBridge.cs |
| RevGM | 运行时的命令门面，项目在上面注册业务命令 | Assets/Revolution/Runtime/RevGMCommand/Facade/RevGM.cs |

## 3. 传输层：其实就是“写 JSON 文件”

没有 socket、没有端口（编辑器模式）。所有请求都落在工程目录里：

```
<Unity工程>/Library/RevolutionAutomation/
├─ endpoint.json                 编辑器身份 + 心跳（Python 用它判断“Unity 还活着”）
├─ compilation.json              上一次编译诊断（editor.compilation 读它）
├─ inbox/                        投递箱：Python 写，Unity 读（读走即删）
│   └─ 8f3c….json                ← 一条动作请求
└─ jobs/
    └─ 8f3c…/
        ├─ request.json          请求备份（同一份内容）
        ├─ response.json         执行结果（Unity 原子写入）
        ├─ progress.json         长任务的进度（可选）
        ├─ cancel                取消标记（可选）
        └─ unity.log             batch 模式下的编辑器日志

动作目录不在这里：
Revolution.RevCLI/commands.json（31 KB，随工具包分发；列出 100+ 个动作的
id / 参数类型 / environment / readOnly，Python 按文件 mtime 缓存，改完即时生效）
```

### 3.1 请求长什么样

```
{
  "protocol": 1,
  "id": "8f3c…",                     // 唯一 ID，回执必须匹配
  "action": "gm.exec",
  "payloadJson": "{\"command\":\"RevGame/Boss技能 3\"}",   // 业务参数（字符串化的 JSON）
  "confirm": false,                  // 高危动作必须 true
  "sessionId": "…",                  // 绑定当前编辑器实例，防止投错窗口
  "timeoutSeconds": 120,
  "deadlineUtc": "2026-10-29T…"      // 过期就判 Expired，不让 Unity 傻等
}
```

### 3.2 为什么用文件，而不是 HTTP / WebSocket

| 原因 | 说明 |
|---|---|
| 天然串行 | 一次只允许一个“改变工程”的请求（_active 单槽），其他人拿 Busy。文件队列天然满足。 |
| 不怕端口冲突 | 不用开监听端口，不被防火墙拦，也不会和别的 Unity 工程抢端口。 |
| 域重载安全 | 改脚本会触发 Unity 域重载（静态字段清空）。桥接把“正在处理的请求”存进 SessionState，重载后知道要补一条失败回执，而不是重放。 |
| 原子写 | 先写 .tmp 再 File.Replace，读方永远读不到半个文件（client.py:62-77 同样逻辑）。 |

### 3.3 双方各多久看一次

| 侧 | 节奏 | 代码 |
|---|---|---|
| Unity 收工单 | 编辑器空闲时约 20ms 轮询一次 inbox | RevAutomationBridge.cs:212-227 |
| Python 等回执 | 起步 5ms → 稳定 20ms → 长任务 50ms（自适应） | client.py:163-187 |
| 心跳 | endpoint.json 里 heartbeatUnix 超出 15 秒即视为离线 | client.py:114-123 |

> 实测单次往返常在 几十毫秒；真正慢的是 Unity 编辑器 tick 和业务本身（例如等一帧截图、等 Play 起来）。

## 4. 三种执行模式：live / batch / player

| 模式 | 什么时候用 | 怎么工作 | 限制 |
|---|---|---|---|
| live（默认） | 你已经打开了 Unity 工程 | 投 inbox/*.json，编辑器自己执行 | 只能跑有图形界面的动作；gm.* 需要已进入 Play |
| batch | 工程没开、或要跑 CI | Python 直接拉起 Unity.exe -batchmode -executeMethod Revolution.Editor.RevAutomationBatch.Execute -revAutomationRequest <request.json> | 不能跑 live/play 类动作；工程已被占用时会拒绝，避免两个编辑器抢同一工程 |
| player（运行时） | 真机 / 独立包里的游戏 | 游戏内起一个 127.0.0.1 HTTP 服务，Python RuntimeClient 带 token 访问 POST /call、GET /jobs/{id} | 只绑定本机回环、强制 Bearer token + sessionId，不接受重定向 |

选择逻辑写死在 client.py:131-142：mode=auto 时，有在线桥接就用 live，否则用 batch；batch 模式遇到 gm.* 直接报错并提示“先 editor launch 再 editor play”。

## 5. MCP 这一层做了什么

MCP 服务只暴露 3 个稳定工具，其余全部是“动态动作”，按需发现：

| 工具 | 作用 | 典型用法 |
|---|---|---|
| rev_discover | 取当前工程真实可用的动作列表（含参数、环境、是否只读、是否高危） | AI 先看“能做什么”，避免凭空编命令 |
| rev_call | 执行一条动作 | 实时决策：改一次、拍一张、执行一条 GM |
| rev_run | 执行一份计划（多步 + 断言 + 清理） | 回归测试、可重复流程 |

三个细节值得知道：

- schema 是现算的：server.py:26-40 用 Unity 给的参数类型表动态生成 JSON Schema，所以你在项目里新注册一个 GM，AI 立刻能看到它的参数。
- 单 worker 串行：server.py 主循环收消息、一个 worker 顺序执行，长任务期间仍能收到 notifications/cancelled 并设置取消信号——既不会两个变更撞车，又能取消。
- 结束自动留证据：rev_run 结束后会把截图收进报告目录、合成 GIF、写 report.html（server.py 的 write_evidence）。

## 6. GM 的调用入口（代码级）

所有 GM 都从这一个方法进去，逻辑只有三段：

```
// Editor/RevAutomation/RevAutomationActions.cs:363
public static RevGMResult ExecuteGM(string line, bool confirm)
{
    // ① 记录活动日志（面板/报告里能看到“谁在什么时候执行了什么”）
    // ② 调用核心
    RevGMResult result = ExecuteGMCore(line, confirm);
    return result;
}

// :379-386
private static RevGMResult ExecuteGMCore(string line, bool confirm)
{
    // ① 必须已在 Play 且未暂停：GM 是给运行中的游戏用的
    if (!EditorApplication.isPlaying || EditorApplication.isPaused)
        throw new InvalidOperationException("GM 执行需要已进入且未暂停的 Play");

    string name = GMCommandName(line);           // 只取命令名，用于高危判定
    if (!RevGM.TryGet(name, out RevGMCommand command)) return RevGM.Execute(line);

    // ② 高危命令必须显式确认，避免脚本误删数据
    if (command.IsHighRisk && !confirm)
        throw new InvalidOperationException("高危 GM 需要 CLI --confirm：" + command.Name);

    // ③ 真正执行：参数解析、业务拒绝、返回原因全部由 RevGM 负责
    return RevGM.Execute(line);
}
```

### 6.1 游戏里到底有哪些 GM？由项目自己注册

```
// RevGame: Bootstrap/Diagnostics/GameGmCommands.cs（节选）
RevGM.Register("RevGame/Boss技能", "立即施放指定 Boss 技能（0–17）", args =>
{
    int index = args.Int(0);
    return Battle.CastBossSkill(index);      // 调真实业务入口
});
```

| 层 | 负责 | 不负责 |
|---|---|---|
| RevAutomationActions.ExecuteGM | Play 校验、高危判定、活动日志 | 不理解任何业务参数 |
| RevGM.Execute | 按空格/引号切参数、类型转换、调用 handler、把异常转成失败原因 | 不关心是 AI 还是人手敲的 |
| 项目 handler | 真正的业务动作（放技能、改数值、造局面） | 不直接操作 UI 或写文件 |

> 为什么必须进 Play？因为 RevGM.Register 写在运行时代码里，只有游戏跑起来才注册。编辑器没进 Play 时 gm.list 会返回 0 条，这时 AI 应该先 editor.play。

## 7. 实时决策，还是提前编排？

两者都存在，区别只在“决策发生在什么时候”：

|  | 实时决策（rev_call） | 预编排（rev_run） |
|---|---|---|
| 谁决定下一步 | AI，边看结果边决定 | 计划里已经写死（AI 或人提前写好） |
| 典型场景 | 排错、探索、“先看看现在什么状态” | 回归测试、每天跑的固定流程 |
| 失败处理 | AI 看到失败原因后自己换策略 | expect 断言失败即停（stopOnFailure） |
| 可重复性 | 弱（依赖 AI 判断） | 强（同一份 JSON 谁跑都一样） |
| 典型命令 | rev_call{gm.exec} | rev_run{steps:[…]} |

### 7.1 计划长什么样（workflow.py 支持的字段）

```
{
  "name": "boss-技能回归",
  "steps": [
    { "id": "prep",  "target": "gm", "action": "gm.exec",
      "arguments": { "command": "RevGame/Boss准备 6" } },

    { "target": "gm", "action": "gm.exec",
      "arguments": { "command": "RevGame/Boss技能 3" } },

    { "target": "gm", "action": "gm.exec",
      "arguments": { "command": "RevGame/Boss查询" },
      "expect": [ { "path": "data.message", "exists": true } ] }
  ],
  "cleanup": [
    { "target": "editor", "action": "editor.stop" }
  ]
}
```

| 字段 | 作用 |
|---|---|
| steps[].expect | 对返回 JSON 做断言（相等/存在），失败即判该步失败 |
| steps[].until | 轮询一个只读动作直到条件满足或超时（例如等编译结束） |
| steps[].id + #id 引用 | 后面的步骤可以引用前面步骤的返回值 |
| cleanup | 无论成败都执行的收尾（停 Play、回首页） |

> 注意：计划里的每一步依然是真实动作调用，不是“录制回放”。所以它和手动点按钮走的是同一条代码路径，不会出现“测试通过但玩家路径挂了”。

## 8. RevGame 里的三个真实例子

### 例 1：实时决策——测一个 Boss 技能

```
AI 看到“这个技能放不出来”，于是：
1) rev_call { target:"editor", action:"editor.status" }      → playing=false
2) rev_call { target:"editor", action:"editor.play", arguments:{scene:"Assets/Scenes/GameScene.unity"} }
3) rev_call { target:"gm", action:"gm.list" }                 → 55 条，含 RevGame/Boss技能
4) rev_call { target:"gm", action:"gm.exec", arguments:{command:"RevGame/Boss准备 6"} }
5) rev_call { target:"gm", action:"gm.exec", arguments:{command:"RevGame/Boss技能 3"} }
6) rev_call { target:"gm", action:"gm.exec", arguments:{command:"RevGame/Boss查询"} }
   → 根据第 6 步返回的状态，AI 决定下一步是重试还是报告失败
```

这条链没有“预写脚本”，每一步都是 AI 看了上一步结果才决定的。

### 例 2：预编排——18 个技能一次性回归

```
rev_run {
  name: "boss-18技能",
  steps: [ 对每个技能 i：准备 → 续航 → 清卫 → 靠近 → 施放 i → 断言状态 ],
  cleanup: [ editor.stop ]
}
→ 失败会指出“第几个技能、第几步、期望什么、实际什么”
```

### 例 3：游戏内自测（第三层，跑在游戏里）

RevGame 还有一条“游戏内自动化”：RevGame/自动化开始 <base64 计划> → GameAutomationRunner 用真实指针输入在游戏里点按钮、落子、验证。

| 层 | 跑在哪 | 适合 |
|---|---|---|
| 实时 rev_call | Python + 编辑器 | 探索、排错 |
| 预编排 rev_run | Python + 编辑器 | 跨步骤回归（含截图证据） |
| 游戏内 RevGame/自动化开始 | 游戏进程内 | 真实 UI 操作、长时间连续对局、真机 |

## 9. 安全边界：为什么它不会乱动你的工程

| 机制 | 代码 | 作用 |
|---|---|---|
| environment | 动作目录里的 edit / live / play | batch 模式直接拒绝 live/play 动作，防止“没开图形界面却要截图” |
| readOnly | 同上 | until 轮询只允许只读动作，避免轮询把变更执行几十遍 |
| confirm + IsHighRisk | RevAutomationActions.cs:384 | 删除资产、清 Console 等高危动作必须显式确认 |
| 单槽串行 + Busy | RevAutomationBridge.cs:29-34 | 同一时刻只有一个变更，其余立刻拿 Busy，不会偷偷排队执行 |
| 域重载不重放 | SessionState 记录 | 改脚本导致重载时，补一条失败回执而不是把动作再执行一遍 |
| 回环 + token | backends.py:34-38 | Player 模式只允许 127.0.0.1、强制 Bearer token、拒绝重定向 |
| 不执行任意代码 | 动作白名单 | 只有注册过的动作能跑；外部工具必须写进显式清单 |

## 10. 新手常问的 8 个问题

| 问题 | 答案 |
|---|---|
| AI 会自己写 Python 脚本注入 Unity 吗？ | 不会。Python 是常驻服务；AI 只发 JSON 动作。要扩展能力，是在 Unity 里注册动作或 GM。 |
| 为什么我的命令执行失败，说“需要 Play”？ | GM 只在运行时注册；先 editor.play，等 compiling=false 再执行。 |
| 能不用 MCP，直接命令行跑吗？ | 可以，RevCLI 就是命令行入口，和 MCP 走同一个文件桥。 |
| AI 一次能跑多条吗？ | 可以：batch.call（一次最多 64 步）或 rev_run（带断言与清理）。 |
| 为什么截图必须等一帧？ | 截图动作是“下一帧末捕获”，所以它必须能访问运行时；这也是 environment: play 的意义。 |
| 真机怎么调？ | 游戏内起 127.0.0.1 HTTP，Python 用 RuntimeClient 连；需要端口的设备做转发。 |
| 会不会两个 AI 同时操作打架？ | 不会：Unity 侧单槽串行，第二个请求直接 Busy。 |
| 结果在哪看？ | MCP 返回 JSON；同时 Library/RevolutionAutomation/jobs/<id>/ 下有完整请求/回执；rev_run 还会生成带图报告。 |
