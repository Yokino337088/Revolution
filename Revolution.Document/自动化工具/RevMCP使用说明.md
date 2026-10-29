# RevMCP · 让 AI 替你操作 Unity 和游戏

> **2026-10-29 更新**：Unity 控制台新增实时活动日志，用中文解释 AI 在做什么和有什么用，并独立显示 Agent 明确提供的 intent。连接设置收在单独页签。完整教程见 [活动日志与生成路径使用说明](RevMCP活动日志与生成路径使用说明.html)。RevResPath 支持自定义生成路径；RevSoundPath 保持原生成方式和位置。

> 一句话：**你说话，AI 动手**。你说“点一下增加按钮，再用 GM 加 2，确认计数是 3，截个图”，AI 会自己去发现接口、按顺序执行、把截图和结果给你。
>
> 它不是一个新软件，而是给 AI 装的一双“手”——这双手的肌肉，就是 `Revolution.RevCLI`。

## 0. 先搞清一件事：RevMCP 和 Automation 有啥区别？

很多人一上来就懵：仓库里怎么有两个自动化目录？其实它们是**上下游**，不是两个竞争品。

| | `Revolution.RevCLI` | `Revolution.RevMCP`（本文档） |
|---|---|---|
| 是什么 | **命令行工具**，真正执行动作的那一层 | **MCP 服务**，把 AI 的话翻译成动作 |
| 谁来用 | AI 客户端、CI 脚本、排障的你 | AI 客户端：CodeBuddy、Codex、Claude 等 |
| 入口 | `rev.py` / `rev.cmd` | `rev_mcp.py … serve`（stdio 协议） |
| 能做的事 | 125 个编辑器/打包/导表/GM 动作 | 上面这些 **＋** 游戏运行时（UI、状态、截图、业务接口） |
| 依赖关系 | 不依赖 RevMCP | **内部调用 Automation** 去执行 Editor 动作 |
| 配置成本 | 零（Python 3.10+ 直接跑） | 要在 AI 客户端里配一次 MCP |
| 你得到什么 | 一条命令的结果 | 一句自然语言的**测试结论 + 证据** |

```
你说：帮我测一下战斗能不能打起来
        ↓
   AI（CodeBuddy / Codex）+ Skill
        ↓  调用 MCP 工具
     RevMCP（本文档）───→ 游戏运行时能力（UI 点击、GM、断言、截图）
        ↓
   Revolution.RevCLI（真正干活的命令行）
        ↓
     Unity 编辑器 / 开发版 Player
```

### 该用哪个？——默认就用 RevMCP，别手敲命令

| 你的目的 | 建议 |
|---|---|
| 日常：测功能、跑流程、看截图、要测试结论 | **用 RevMCP**（让 AI 来） |
| 接 CI（Jenkins / GitHub Actions）跑固定脚本 | 用 Automation 的 CLI（CI 里没有 AI） |
| 底层排障：AI 说连不上，你想确认哪一层坏了 | 用 Automation 的 `doctor` / `editor.status` 单独试 |

**推荐路径**：**直接配 RevMCP**。不需要“先学会手敲命令”再上 AI——`doctor` 这类自检，让 AI 跑一遍就行，它跑不通会告诉你卡在哪。

手敲命令只留给两种情况：**CI 流水线**，以及 **AI 连不上时的底层排障**。

## 1. 3 分钟接线（以 CodeBuddy / Codex 为例）

MCP 服务就一条命令，本质是让客户端在后台启动它：

```
python <本仓库绝对路径>/Revolution.RevMCP/rev_mcp.py --project <Unity 工程绝对路径> serve
```

> ⚠️ 两个 `<…>` 是占位符，**必须换成你自己机器上的绝对路径**。MCP 客户端只接受绝对路径，这是客户端的硬要求，不是文档偷懒。文档不会写死任何人的机器目录。

**CodeBuddy**（`~/.codebuddy/mcp.json`）：

```json
{
  "mcpServers": {
    "revolution": {
      "type": "stdio",
      "command": "<Python 3.10+ 安装目录>/python.exe",
      "args": ["<本仓库绝对路径>/Revolution.RevMCP/rev_mcp.py",
               "--project", "<Unity 工程绝对路径>", "serve"],
      "env": { "PYTHONUTF8": "1", "PYTHONIOENCODING": "utf-8" },
      "description": "Revolution RevMCP：操作 Unity 编辑器与游戏运行时"
    }
  }
}
```

填完在 MCP 面板点 **Try to Run**，绿灯即成功。**不需要重启 IDE。**

**Codex**（`~/.codex/config.toml`）：

```toml
[mcp_servers.revolution]
command = '<Python 3.10+ 安装目录>/python.exe'
args = ['<本仓库绝对路径>/Revolution.RevMCP/rev_mcp.py', '--project', '<Unity 工程绝对路径>', 'serve']
startup_timeout_sec = 60
tool_timeout_sec = 300

[mcp_servers.revolution.env]
PYTHONUTF8 = '1'
PYTHONIOENCODING = 'utf-8'
```

验证：`codex mcp get revolution`，能看到 `enabled: true` 和正确的 args 就对了。

> **为什么要写死绝对路径？** 客户端是被别的程序（IDE/终端）拉起来的，工作目录不可控，相对路径会指到别的地方。所以这里必须绝对路径，正文其它地方一律用相对路径。

## 2. 接上之后先说这三句话（渐进式）

不要一上来就让它跑完整回归。按这个顺序：

**第 1 句——确认能连上：**

> 用 revolution 的 rev_discover 列出所有可用动作，告诉我一共多少个，并挑 3 个和编辑器状态相关的给我看。

**第 2 句——确认能操作编辑器：**

> 用 rev_call 调 editor.status，把 playing、compiling 报给我。

**第 3 句——确认能操作游戏：**

> 进入 Play，调 gm.list 看看有哪些 GM 指令，然后停掉。

三句话都通了，再让它测真实业务。

## 3. 三个稳定工具（AI 最常用的就这三个）

工具清单会变（当前 124 个），但下面三个是**稳定钉子**，名字永远不变：

| 工具 | 干什么 | 类比 |
|---|---|---|
| `rev_discover` | 列出真实可用的动作、参数、环境要求 | 先看菜单有什么菜 |
| `rev_call` | 调一个具体动作（target + action + 参数） | 点一道菜 |
| `rev_run` | 跑一份多步计划（准备→执行→断言→清理） | 点套餐 |

其余一百多个工具是**按动作动态生成**的，名字后面带一串哈希（例如 `editor_ab_build_d76143ddf55a0617`）。它们能用，但不适合让 AI 去记忆——**优先让 AI 用 `rev_discover` + `rev_call`**，这样接口变了也不会失效。

## 4. 让 AI 测你自己的游戏功能（关键一步）

AI 不可能凭空知道你的“背包加道具”怎么查。你得给游戏**注册能力**，AI 才有东西可调：

```csharp
// 在游戏侧登记一个可被自动化的业务能力（名称由你定）
RevRuntimeAutomation.Register("inventory.count", () => bag.Count);
RevRuntimeAutomation.Register("inventory.add", args => bag.Add(args.GetInt("id"), 1));
RevRuntimeAutomation.Register("inventory.reset", () => bag.Clear());
```

登记之后，AI 才可以说：

> 记录背包数量 → 加一个道具 → 断言数量 +1 → 截图 → 复位归零。

**这就是 RevMCP 的边界**：它负责“发现 + 调用 + 断言 + 证据”，业务接口必须由项目提供。工具不会猜你的游戏逻辑。

## 5. 常见问题（新手 90% 卡在这几条）

| 现象 | 原因 | 怎么办 |
|---|---|---|
| AI 说「没有在线 Unity 桥接」 | Unity 没开 / 还在编译 / 工程路径不对 | 打开工程等编译完；核对 `--project` |
| 客户端里看不到 revolution | JSON 写错（少逗号、BOM）或路径不存在 | 用 `python <路径>/rev_mcp.py --project <工程> discover` 手动验证能否跑 |
| 能发现动作，但一执行就失败 | 动作要求 Play 模式 / EditMode | 看动作的 `environment` 字段，先切到正确模式 |
| AI 跑完说“成功”但游戏没变 | 缺断言——它只调了动作，没验证结果 | 明确要求“断言 + 截图”，别只说“点一下” |
| 中文路径报错 | Python 默认编码不是 UTF-8 | 配置里加 `PYTHONUTF8=1` |
| 想用 WebGL 包做自动化 | **不支持** | WebGL 没有本机 HTTP 能力，只能用编辑器或开发版 Player |

## 6. 它做不到什么（提前说清，省得你试）

- **不能凭空理解未注册的业务**：没登记查询/动作/复位，AI 只能“点按钮”，无法断言业务结果；
- **WebGL 构建不支持**运行时测试接口；
- **正式发布的包默认不含这些接口**，只在 Development Build + 显式开启时存在；
- **两个 MCP 连接不能同时改同一个工程**（比如 Unity MCP 和 RevMCP 同时操作）；
- 断言失败、清理失败都会被记为失败——这是**故意的**，避免“跑完看起来绿了、其实脏了”。

## 7. 测试产出：报告 + 截图 + GIF（硬规矩）

跑完计划**不等于任务结束**。每次测试都必须留一份能直接发给别人看的报告，文件名是 `<中文标题>_<日期>.html`（例如 `登录流程验收_2026-10-26.html`），只有"success: true"的结果会被当成没做完。

- 报告样式沿用项目既有的工程手册风（纸色底 + 绿金主色：顶栏 / hero / 章节卡片 / 表格 / 图墙），样式由 `revautomation/report.py` 的 `REPORT_CSS` 提供，不另造一套；
- 必须带**本次运行的截图**（初始态、关键操作后、失败现场），并压到网页可用的体积；
- **动态效果必须给 GIF**（技能、位移、粒子、UI 过渡），单帧看不出节奏；
- 过程文件（markers / status / 逐帧 CSV）合进一个证据 zip，不要散落在报告目录里；
- 结论只由断言决定，图片只证画面。

`rev_run` 现在会**自动**做这件事：把计划里截图动作产出的 PNG 收进报告目录、超过 1 帧就合成 `anim.gif`，并写出带日期的报告，返回里带 `evidencePath`。完整的命名、目录结构与反例见 `Skills/revolution-ai-automation/references/report.md`。

手动补也可以（同一个引擎）：

```powershell
python Revolution.RevCLI/rev.py evidence --directory <结果目录> --title "本次测试" --subtitle "截图与动图"
python Revolution.RevCLI/rev.py --project <工程> --mode live capture --out results\anim.gif --frames 12 --interval 120 --report
```

## 8. 验证口径

2026-10-22 的验证：Python 自动化 19 项、RevMCP 13 项、Unity EditMode 24 项通过；真实 MCP stdio → Editor 文件桥，以及 stdio → 本机 HTTP → Windows Development Player 的“按钮 + GM + 断言 + 截图 + 归零”全链路已跑通。

**未**声称 Android、IL2CPP、小游戏真机通过。需要哪个平台结论，就在那个平台单独跑。

## 9. 相关文档

- [Revolution 自动化工具（命令行）](Revolution自动化工具使用说明.html) —— 底层那双手怎么用
- [RevMCP 架构解析](RevMCP架构解析.html) —— 写给维护者：线程、工单生命周期、扩展选型
- 仓库内：`Revolution.RevMCP/README.md`、`Revolution.RevCLI/README.md`
