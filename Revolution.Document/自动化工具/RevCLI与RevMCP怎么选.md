# RevCLI 与 RevMCP：怎么选

两者连的是**同一套** Unity 能力：编辑器动作注册表、游戏运行时注册表、RevGM。区别在于“AI 怎么把请求送进去”，所以能做的事几乎一样，成本和习惯不一样。

## 一句话结论

- **默认用 RevCLI**：编辑器操作、出包、调 GM、点 UI，一套命令全搞定，不用配客户端。
- **想要“一次提交一整套带断言的测试，并自动出证据报告”时，用 RevMCP 的 `rev_run`**。

两者可以混用，互不冲突。

## 对比

| | RevCLI | RevMCP |
|---|---|---|
| 怎么接入 | 有 Python 3.10+ 就能用，`rev.py connect` 一条命令 | 要在 AI 客户端里配置 MCP 服务，重开会话生效 |
| 换电脑 / 重启后 | `connect` 自动打开 Unity、起服务 | 客户端自动拉起，但 Unity 仍要先开 |
| 每轮固定 token | **0** | 紧凑模式 325，全量模式 17,255 |
| 单次结果 token | 约 206 | 约 445 |
| 编辑器操作耗时 | 常驻服务下约 200ms/次 | 117–278ms/次 |
| 调游戏运行时 | `game gm` / `game call`，编辑器 Play 和独立 Player 同一套 | `rev_call` / `rev_run` |
| 多步编排 | `pipeline`（等待、断言、`batch.call`） | `rev_run`：一份计划带断言、等待、finally 清理，一次提交 |
| 证据报告 | `evidence` / `capture --report` 手动生成 | `rev_run` 自动生成 |
| 在 CI 和脚本里用 | 天然适合，命令可复制、可记录 | 不适合 |
| 出错时怎么排查 | 命令和输出就在终端里，可原样重跑 | 要看客户端日志 |

数字来自框架工程实测，换机器会变：token 见 `Skills/revolution-ai-automation/references/token-cost.md`，耗时见 [CLI 优先工作流](RevCLI优先工作流使用说明.md)。

## 什么时候选 MCP

1. 一次要跑十几步的业务验收，希望中途失败也一定执行清理，并且直接拿到 HTML 证据报告。
2. 团队已经在用支持 MCP 的客户端，且会话较短（全量模式每轮都要付 1.7 万 token）。

其余情况都建议 CLI。

## 用 CLI 调游戏运行时

编辑器 Play：

```text
rev.py --project <工程> editor play
rev.py --project <工程> runtime start            开启运行时服务（显式开启，默认不开）
rev.py --project <工程> game gms --query 道具     看本局有哪些 GM 和参数
rev.py --project <工程> game gm 背包/添加道具 1001 2
rev.py --project <工程> game call ui.click --set objectPath="Canvas/Start"
rev.py --project <工程> game call runtime.state
rev.py --project <工程> editor stop               退出 Play，服务自动关闭、凭据自动删除
```

独立 Player：

```text
rev.py player launch --executable <Game.exe> --directory <新的空会话目录>
rev.py --project <工程> game gm 背包/添加道具 1001 2 --session <会话目录>
rev.py player stop --directory <会话目录>
```

Player 要用带自动化的开发版：`ship --development --automation`。

| 命令 | 作用 |
|---|---|
| `game status` | 连通性和本局身份 |
| `game gms [--query]` | 本局注册的 GM、参数、是否高危 |
| `game actions [--query]` | 本局注册的游戏能力（`ui.click`、`runtime.state` 等） |
| `game gm <名称> [参数...]` | 执行一条 GM；高危 GM 要加 `--confirm` |
| `game call <能力> [--json 文件 / --set k=v]` | 调用一个游戏能力 |

### 为什么运行时服务要显式开启

手动 Play 调试时不该莫名多开一个本机端口。所以 `runtime start` 才开，`runtime stop` 或退出 Play 就关，凭据文件随之删除。服务只监听 127.0.0.1，每次开启都换新 token 和会话号，token 只写在工程 `Library` 下，不出现在命令输出里。

### 结果怎么判

- 返回 `success: true` 才算执行成功。期间 Unity 报了 Error，也会判失败，用 `game call runtime.logs` 看原因。
- 超时会报“结果未知，不要重放”：GM 可能已经执行了，先用 `runtime.state` 或业务 GM 读回确认，再决定下一步。CLI 自己绝不重发。
- 报“会话已更换”说明游戏重启或服务重开过，之前读到的对象 id 都作废，重新查询。

### 速度

一次 `game` 命令的时间主要花在启动 Python 进程上；真正的游戏往返实测约 40ms。要连续调很多次，写一个 Python 脚本在同一进程里用 `revautomation.game.GameClient`；只在编辑器 Play 里跑的话，也可以用 `pipeline` 一次提交多步。
