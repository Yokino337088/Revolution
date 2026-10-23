# Revolution 自动化工具 · 新手使用说明

> 一句话：**让电脑替你操作 Unity**。你敲一条命令，它替你去点菜单、跑导表、打包、开 Play、查 Console，然后把结果写成报告。
>
> 你不需要会写 Unity 编辑器插件，会开命令行就行。

## 0. 先回答一个问题：我该用「Automation」还是「RevMCP」？

这两个东西**不是二选一的两套系统**，而是「发动机」和「遥控器」的关系。

| | `Revolution.Automation`（本文档） | `Revolution.RevMCP`（另一份文档） |
|---|---|---|
| 本质 | 一个 **命令行工具**（`rev.py` / `rev.cmd`） | 一个 **MCP 服务**，给 AI 客户端（CodeBuddy、Codex 等）用 |
| 谁在按键 | **你自己**，在命令行里敲 | **AI**，你说话它去调 |
| 怎么用 | `rev.py editor play` 这种命令 | 在 AI 里说“帮我点一下增加按钮，再断言计数是 3” |
| 适合 | 手动排查、跑一次导表/打包、CI 流水线、写测试脚本 | 让 AI 自己发现接口、自己组织测试步骤、自己看截图判断 |
| 需要配置 | **不需要**（Python 3.10+ 就行） | 需要把 MCP 配到 AI 客户端里（一次性） |
| 底层关系 | 它是**执行层**：真正干活的 125 个动作都在这里 | 它是**包装层**：把你的话翻译成 Automation 的动作，再额外打通游戏运行时 |

### 给新手的建议（按顺序来）

1. **先只用 Automation**。先把 `doctor`、`editor status` 跑通——这一步不涉及 AI，出问题好排查；
2. 觉得“每次都要背命令好麻烦”，再上 **RevMCP**，让 AI 替你想命令；
3. 不要一上来就配 MCP。MCP 出问题时，你分不清是 AI 的问题、配置的问题，还是 Unity 的问题。

> 一句话记忆：**Automation 是手，RevMCP 是会说话的主人。** 手不会自己动，主人也得靠手才能干活。

## 1. 30 秒跑通

前提：Unity 工程已经打开，并等它编译完（右下角不再转圈）。

```powershell
# 在仓库根目录执行
python Revolution.Automation/rev.py --project .\RevolutionFrameWork_Unity doctor
python Revolution.Automation/rev.py --project .\RevolutionFrameWork_Unity commands
python Revolution.Automation/rev.py --project .\RevolutionFrameWork_Unity --mode live editor status
```

三条命令分别回答三个问题：

| 命令 | 回答什么问题 | 期望结果 |
|---|---|---|
| `doctor` | 环境对不对？（Python 版本、工程路径、Unity 在哪） | 打印检查项，全部 OK |
| `commands` | 它到底能干什么？（列出全部动作） | 一大段 JSON 动作清单 |
| `editor status` | Unity 现在什么状态？（在不在 Play、有没有在编译） | `playing` / `compiling` 等字段 |

> `--project` 后面填的是**含 `Assets` 和 `ProjectSettings` 的那个目录**。第一次建议先跑 `doctor`，它会把路径问题直接指出来。

## 2. 它是怎么工作的（30 秒版）

```
你敲命令
   ↓
rev.py（Python，在仓库里）
   ↓  写一个"工单"文件
Unity 里的编辑器桥（Assets/Revolution/Editor/RevAutomation）
   ↓  在主线程执行：点菜单 / 调 GM / 改资产
结果写回文件 → 你的命令行打印出来
```

为什么要绕文件？因为 Unity 的编辑器 API **只能在主线程调用**，而命令行是另一个进程。用文件传递，不需要开端口、不需要装额外东西，断线也好恢复。

## 3. 常用命令速查（够用 90% 的场景）

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

> 完整清单永远以 `commands` 的输出为准，本文档不复制它——清单会随版本变，抄在文档里很快过期。

## 4. 失败时怎么看（新手最容易卡住的地方）

**记住一条：命令返回 `success: false` 时，`message` 才是真相，不要只看 exit code。**

| 现象 | 真实原因 | 怎么办 |
|---|---|---|
| `没有在线 Unity 桥接` | Unity 没开，或还在编译 | 打开工程，等编译完再跑 |
| 命令卡住不动 | 弹了 Unity 对话框（保存场景、导入资源） | 去 Unity 点掉对话框 |
| `timeout` | 动作比预期慢（大工程首次导入很慢） | 加 `--timeout 300` |
| 找不到工程 | `--project` 指错了 | 跑 `doctor` |
| 结果文件里有 error 但命令成功 | Unity 只是“执行了”，不代表“业务正确” | 看报告里的断言/错误计数 |

## 5. 写一个最小场景（多步编排）

一个动作一条命令当然可以，但“准备 → 点 → 断言 → 清理”这种四步走，用场景文件更省事：

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

## 7. 什么时候该升级去用 RevMCP？

出现下面任一情况，就去看 `RevMCP使用说明`：

- 你不想记命令，只想说“帮我测一下这个功能”；
- 你希望 AI 自己**发现**有哪些接口可用，而不是你告诉它；
- 你需要 AI **看截图判断**结果对不对，而不是只看 JSON 里的布尔值；
- 你要测的是**独立运行的开发版 Player**，不是编辑器。

反之，如果你要接 CI（Jenkins/GitHub Actions）跑固定脚本，**继续用 Automation**——CI 里不需要 AI，也不需要 MCP。

## 8. 验证口径

本工具在 2026-10-22 的验证覆盖：Python 自动化 19 项、RevMCP 13 项、Unity EditMode 24 项通过；Editor 与 Windows 开发版 Player 的实际按钮点击、GM、断言、截图、清理已完成。

**没有**以此声称 Android、IL2CPP、小游戏真机或全部动作逐一通过——需要哪个平台的结论，就在那个平台上单独跑一遍。
