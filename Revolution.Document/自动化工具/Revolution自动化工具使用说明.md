# Revolution 自动化工具 · 新手使用说明

> 一句话：**让 AI 替你操作 Unity**。你说“帮我跑一次打包，结果告诉我”，AI 去执行命令、读输出、给你结论。
>
> 你不需要背命令，也不需要会写 Unity 编辑器插件。本页的命令是**给 AI 和 CI 看的执行契约**，不是让你手抄的。

## 0. 先说结论：命令不该由你敲

**别手敲命令。** 正确姿势是：你提需求 → AI 通过 MCP 或 CLI 执行 → 你只看结论和证据。

这两个目录**不是二选一的两套系统**，而是「发动机」和「遥控器」的关系。

| | `Revolution.RevCLI`（本文档） | `Revolution.RevMCP`（另一份文档） |
|---|---|---|
| 本质 | 一个 **命令行工具**（`rev.py` / `rev.cmd`） | 一个 **MCP 服务**，给 AI 客户端（CodeBuddy、Codex 等）用 |
| 谁发起 | 排障/CI 时是你；日常是 AI | **AI**（日常默认） |
| 怎么用 | `rev.py editor play` 这种命令 | 在 AI 里说“帮我点一下增加按钮，再断言计数是 3” |
| 适合 | CI 流水线、底层排障、需要精确复现的一次性操作 | 日常所有事：测功能、跑流程、看截图要结论 |
| 需要配置 | **不需要**（Python 3.10+ 就行） | 需要把 MCP 配到 AI 客户端里（一次性） |
| 底层关系 | 它是**执行层**：真正干活的 125 个动作都在这里 | 它是**包装层**：把你的话翻译成 Automation 的动作，再额外打通游戏运行时 |

### 三种用法，按推荐程度排序

| 档次 | 怎么做 | 你要做的事 | 什么时候用 |
|---|---|---|---|
| **① 首选** | 配 **RevMCP**，直接跟 AI 说需求 | 配一次 MCP（3 分钟） | 日常全部：测功能、跑流程、要截图和结论 |
| **② 次选** | 让 **AI 直接执行 CLI**（把命令丢给 AI 跑） | 说一句话 | 暂时没配 MCP，但已经有 AI 助手 |
| **③ 兜底** | **自己敲命令** | 查本文档 | 底层排障、CI 脚本、没有 AI 的环境 |

> 一句话：**Automation 是手，RevMCP 是会说话的主人。** 手不该由你去掰——让主人（AI）指挥它。
>
> 所以：**日常一律让 AI 来**；只有“AI 说连不上、你想确认到底哪一层坏了”时，才需要你自己敲一条 `doctor`。

## 1. 30 秒跑通（让 AI 跑，不是让你敲）

前提：Unity 工程已打开并编译完（右下角不再转圈）。

**第 1 步**：配好 RevMCP（见 `RevMCP使用说明` 的「3 分钟接线」，只需一次）。

**第 2 步**：把下面这句话丢给 AI——

> 用 revolution 的 rev_call 依次执行 doctor 和 editor.status，把 Python 版本、工程路径、playing / compiling 三个值报给我；哪一步失败就告诉我失败原因。

**第 3 步**：它把结论给你。失败时它必须说清卡在哪一层（Python 版本 / 工程路径 / Unity 没开 / 还在编译），而不是丢一句“失败了”。

下面这三条是 AI 实际会执行的底层命令，**只在你排障时用来对照**：

```powershell
python Revolution.RevCLI/rev.py --project .\RevolutionFrameWork_Unity doctor
python Revolution.RevCLI/rev.py --project .\RevolutionFrameWork_Unity commands
python Revolution.RevCLI/rev.py --project .\RevolutionFrameWork_Unity --mode live editor status
```

三条命令分别回答三个问题：

| 命令 | 回答什么问题 | 期望结果 |
|---|---|---|
| `doctor` | 环境对不对？（Python 版本、工程路径、Unity 在哪） | 打印检查项，全部 OK |
| `commands` | 它到底能干什么？（列出全部动作） | 一大段 JSON 动作清单 |
| `editor status` | Unity 现在什么状态？（在不在 Play、有没有在编译） | `playing` / `compiling` 等字段 |

> `--project` 后面填的是**含 `Assets` 和 `ProjectSettings` 的那个目录**。路径写错时 `doctor` 会直接指出来——**让 AI 跑这一条就行**，不用你自己试。

## 2. 它到底怎么工作的（30 秒版）

```
你（或 AI）发起一次调用
   ↓
rev.py（Python，在仓库里）
   ↓  写一个"工单"文件
Unity 里的编辑器桥（Assets/Revolution/Editor/RevAutomation）
   ↓  在主线程执行：点菜单 / 调 GM / 改资产
结果写回文件 → 你的命令行打印出来
```

为什么要绕文件？因为 Unity 的编辑器 API **只能在主线程调用**，而命令行是另一个进程。用文件传递，不需要开端口、不需要装额外东西，断线也好恢复。

## 3. 命令速查（AI 参考用 / 你排障用）

```powershell
# 进出 Play
... editor play --scene Assets/Scenes/GameScene.unity
... editor stop

# 查状态、查 GM 列表、执行 GM
... --mode live editor status
... --mode live gm list
... --mode live gm exec 'RevGame/开始战斗'

# 资源打包（AB）
... ab config
... ab build

# 导表
... excel export

# 改场景 / 改组件 / 改资产
... editor menu 'Revolution.Tools/...'
```

| 前缀 | 管什么 | 新手最常用 |
|---|---|---|
| `editor.` | 编辑器：Play、刷新、菜单、Console、测试 | `editor.status`、`editor.play`、`editor.stop` |
| `gm.` | 游戏内 GM 指令（真实注册表） | `gm.list`、`gm.exec` |
| `ab.` | AssetBundle：分包、校验、构建、体积 | `ab.build` |
| `excel.` | Excel 导表 | `excel.export` |
| `ui.` / `asset.` / `platform.` | UI 生成、资产、平台切换与首包 | 按需 |
| `scenario.` | 多步编排（准备→执行→断言→清理） | 见下一节 |

> 这些命令**不需要你背**：AI 会自己 `rev_discover` 查清单。完整清单永远以 `commands` 的输出为准——清单会随版本变，抄进文档很快过期。

## 4. 失败时怎么看（新手最容易卡住的地方）

**记住一条：命令返回 `success: false` 时，`message` 才是真相，不要只看 exit code。**

| 现象 | 真实原因 | 怎么办 |
|---|---|---|
| `没有在线 Unity 桥接` | Unity 没开，或还在编译 | 打开工程，等编译完再跑 |
| 命令卡住不动 | 弹了 Unity 对话框（保存场景、导入资源） | 去 Unity 点掉对话框 |
| `timeout` | 动作比预期慢（大工程首次导入很慢） | 加 `--timeout 300` |
| 找不到工程 | `--project` 指错了 | 跑 `doctor` |
| 结果文件里有 error 但命令成功 | Unity 只是“执行了”，不代表“业务正确” | 看报告里的断言/错误计数 |

## 5. 多步流程：也交给 AI

“准备 → 点 → 断言 → 清理”这种四步走，你只要说清楚要什么，AI 会自己拼场景：

> 帮我测一下：进 Play、执行 GM「RevGame/开始战斗」、断言回合数变成 1、截一张图、然后 stop。

如果它需要落到文件，格式长这样（AI 会写，你只用来 review）：

```json
{
  "steps": [
    { "action": "editor.play", "args": { "scene": "Assets/Scenes/GameScene.unity" } },
    { "action": "gm.exec", "args": { "name": "RevGame/开始战斗" } },
    { "action": "assert", "args": { "path": "battle.round", "equals": 1 } },
    { "action": "screenshot", "args": { "file": "result.png" } }
  ],
  "cleanup": [
    { "action": "editor.stop" }
  ]
}
```

```powershell
... run my-scenario.json
```

跑完会在 `results/` 里留下 JSON / HTML 报告，能直接贴给人看。

## 6. 这几件事它做不到（别浪费时间试）

- **不能在没有 Unity 工程的情况下凭空生成游戏内容**——它只是“替人点按钮”，按钮背后必须有真实逻辑；
- **不能在 Play 暂停时执行时间相关操作**；
- **WebGL 包不支持运行时 HTTP 接口**（MCP 那条路在 WebGL 上走不通）；
- **正式发布的包默认不含测试接口**——别担心自动化接口被玩家用到。

## 7. 为什么默认就推荐 RevMCP

因为上面这些命令，**本来就是给程序调的，不该由人来敲**：

- AI 能自己 `rev_discover` 查有哪些动作，不需要你记；
- AI 能自己组织“准备 → 执行 → 断言 → 截图 → 清理”，不需要你写脚本；
- AI 能看截图和报告判断结果，而不是只看 JSON 里的布尔值；
- 你要测的是**独立开发版 Player** 时，也只有 RevMCP 那条路能连上。

**只有两种情况才回到本文档（CLI）**：接 CI（Jenkins / GitHub Actions，那里没有 AI），或者 AI 报“连不上”时你想亲手确认哪一层坏了。

## 8. 测试产出：HTML 报告 + 截图 + GIF（硬规矩）

**一次测试的交付物不是"success: true"，而是一份能直接发给别人看的报告。**

| 必须有 | 说明 |
|---|---|
| `report.html` | 视觉与《进度同步》那批文档一致（蓝色 hero、目录、卡片、表格、图墙），不要另造样式 |
| **截图** | 至少覆盖初始态、关键操作后、失败现场；截图只证画面，不能替代数值断言 |
| **GIF** | 技能、位移、粒子、UI 过渡这类"动起来才看得懂"的效果，必须连拍成动图 |
| 结论 | 通过/失败只由断言决定；写清环境、首个失败原因、未验证项 |

```powershell
# 一遍连拍 + 合成 + 出报告（Unity 在 Play 中时）
.\rev.py --project <工程> --mode live capture --out results\anim.gif --frames 12 --interval 120 --report

# 已有图，只补报告
.\rev.py evidence --directory results\<本次目录> --title "第一章流程自动化测试" --subtitle "截图与动图"

# 只有零散截图，先合动图（离线）
.\rev.py gif a.png b.png c.png --out results\anim.gif --delay 120
```

> 用 RevMCP 跑计划时会**自动**产出这份报告（见 `RevMCP使用说明`），不需要你手动补。

## 9. 验证口径

本工具在 2026-10-22 的验证覆盖：Python 自动化 19 项、RevMCP 13 项、Unity EditMode 24 项通过；Editor 与 Windows 开发版 Player 的实际按钮点击、GM、断言、截图、清理已完成。

**没有**以此声称 Android、IL2CPP、小游戏真机或全部动作逐一通过——需要哪个平台的结论，就在那个平台上单独跑一遍。
