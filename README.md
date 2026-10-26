# Revolution

**Revolution————具有革命性的unity客户端框架**

**让 Agent 不只是帮你写代码，而是按统一规范开发、操作 Unity、验证真实游戏结果。**

[![Unity](https://img.shields.io/badge/Unity-2022.3.15f1c1-blue.svg?style=flat-square&logo=unity&logoColor=white)](https://unity.com/)
[![C%23](https://img.shields.io/badge/C%23-.NET%20Standard%202.1-239120.svg?style=flat-square&logo=csharp&logoColor=white)](https://learn.microsoft.com/dotnet/csharp/)
[![Python](https://img.shields.io/badge/Python-3.10%2B%20%C2%B7%20stdlib-3776AB.svg?style=flat-square&logo=python&logoColor=white)](Revolution.RevMCP/README.md)
[![License](https://img.shields.io/badge/License-MIT-3DA639.svg?style=flat-square&logo=opensourceinitiative&logoColor=white)](LICENSE)
[![Stars](https://img.shields.io/github/stars/Yokino337088/Revolution?style=flat-square&logo=github)](https://github.com/Yokino337088/Revolution/stargazers)
[![Last Commit](https://img.shields.io/github/last-commit/Yokino337088/Revolution?style=flat-square&logo=git&logoColor=white)](https://github.com/Yokino337088/Revolution/commits/main)
[![Issues](https://img.shields.io/github/issues/Yokino337088/Revolution?style=flat-square&logo=githubissues&logoColor=white)](https://github.com/Yokino337088/Revolution/issues)

[![MCP](https://img.shields.io/badge/MCP-RevMCP%20server-6f42c1.svg?style=flat-square)](#-第二层革命让-ai-从只会写代码变成能操作-unity)
[![Agent](https://img.shields.io/badge/Agent-Skills%20ready-orange.svg?style=flat-square)](#-第一层革命让-ai-从自由发挥变成按工程规范开发)
[![GM](https://img.shields.io/badge/GM-commands-yellowgreen.svg?style=flat-square)](#-第三层革命让-ai-从口头交付变成用真实结果交付)
[![CLI](https://img.shields.io/badge/CLI-editor%20automation-informational.svg?style=flat-square)](#-第二层革命让-ai-从只会写代码变成能操作-unity)
[![Full-chain](https://img.shields.io/badge/Full--chain-AI%20automation-brightgreen.svg?style=flat-square)](#-把三层连起来一次完整的-agent-开发)
[![Docs](https://img.shields.io/badge/Docs-online%20site-blueviolet.svg?style=flat-square&logo=readthedocs&logoColor=white)](https://yokino337088.github.io/Revolution/)
[![PRs](https://img.shields.io/badge/PRs-welcome-ff69b4.svg?style=flat-square&logo=gitpullrequest&logoColor=white)](https://github.com/Yokino337088/Revolution/pulls)

## 🤔 Revolution 的革命性，到底革在什么地方？

Revolution是Agent时代进行unity客户端开发的最优选择，你拿到这一套框架之后不需要从头开始把框架的每一个模块都学习一遍，框架提供的这些模块都是为了用一套统一的
规范来把工程代码的架构直接锁死，防止AI乱写代码，让AI的代码更加规范，更加易读，更加易维护。并且框架还能使用自带的MCP工具+游戏内GM指令来自动化测试，也可以使用CLI来对框架当中的unity编辑器功能进行一系列的自动化操作，实现真正的全链路AI自动化。

**🔄 这不是“又多了一套资源加载、UI、计时器”，而是换了一种组织游戏开发的方式。**

过去，你主要关心“框架给程序员提供了哪些功能”；到了 Agent 时代，还要回答三个新问题：

1. ❓ **AI 写得快，但怎样让它始终按照同一套架构写？**
2. ❓ **AI 生成了代码，怎样让它继续完成 Unity 编辑器里的操作？**
3. ❓ **AI 说“已经完成”，怎样让它拿出游戏真的运行正确的证据？**

👉 Revolution 把这三个问题放到同一条链路里解决：**规范约束开发，工具连接执行，真实结果决定验收。**

> ⚠️ 这里的“锁死架构”，是把工程的职责分工、公开入口和清理规则先定下来，而不是冻结玩法、禁止扩展。它也不是一个能保证 AI 永远不犯错的神奇开关；规范、审查、编译和实际测试仍然缺一不可。

---

## 🧭 阅读导航

- [先看懂：Agent、Skill、MCP、GM、CLI 是什么？](#-先看懂agentskillmcpgmcli-是什么)
- [第一层革命：让 AI 从自由发挥，变成按工程规范开发](#-第一层革命让-ai-从自由发挥变成按工程规范开发)
- [第二层革命：让 AI 从只会写代码，变成能操作 Unity](#-第二层革命让-ai-从只会写代码变成能操作-unity)
- [第三层革命：让 AI 从口头交付，变成用真实结果交付](#-第三层革命让-ai-从口头交付变成用真实结果交付)
- [把三层连起来：一次完整的 Agent 开发](#-把三层连起来一次完整的-agent-开发)
- [为什么是 Agent 时代开发 Unity 客户端的最佳选择？](#-为什么是-agent-时代开发-unity-客户端的最佳选择)
- [已经跑通了什么？](#-已经跑通了什么)
- [新手怎样开始？](#-新手怎样开始)
- [能力边界与文档入口](#-能力边界与文档入口)

## 🔤 先看懂：Agent、Skill、MCP、GM、CLI 是什么？

不需要先学一堆缩写，记住下面这张表就够了。

| 名称 | 通俗理解 | 在 Revolution 里做什么 |
|---|---|---|
| **Agent** | 能理解任务、使用工具并推进工作的 AI 助手 | 根据你的需求开发和测试，而不只是回答问题 |
| **Skill** | 给 AI 的项目工作手册 | 告诉它用什么入口、代码放哪里、怎样清理和验证 |
| **MCP** | AI 与工具之间的连接方式 | 通过 `RevMCP` 访问工程里实际开放的编辑器、游戏和测试能力 |
| **GM 指令** | 游戏开发者预留的测试按钮 | 查询状态、执行受控业务动作、准备测试场景；不是让 AI 随便改内存 |
| **CLI** | 用命令代替鼠标点击工具窗口 | 让编辑器操作和测试流程能由脚本重复执行 |
| **断言** | 把“应该怎样”写成明确检查 | 例如：增加 2 次后，计数必须等于 3，不满足就失败 |

**💡 一句话：Skill 教 AI 怎么做，MCP 和 CLI 让它实际动手，GM 让它操作游戏业务，断言告诉它做得对不对。**

框架本体则是这套工作方式的底座：资源、界面、流程和生命周期都有一致的入口与规则，让 AI 不必为每个需求重新发明基础设施。

---

## 🧱 第一层革命：让 AI 从自由发挥，变成按工程规范开发

### 😵 为什么“让 AI 写代码”还不够？

你让 AI 加一个背包，下一次加一个商店，再下一次加一个战斗结算。每个功能单独看都能运行，但它可能用了三套完全不同的写法：

- 背包直接读文件，商店又写了一套资源管理器；
- 一个界面把业务塞进按钮，另一个界面新建好几层管理类；
- 离开战斗后，计时器和事件还在执行；
- 后来的 Agent 看不懂前面的约定，只能再加一套“自己的方案”。

**🎯 问题不是 AI 不会写某个功能，而是它每次都可能重新决定工程应该长什么样。**

Revolution 先把这些基础决策统一下来，再让 AI 实现玩法。你提出的是“我要一个背包”，而不是每次都要重新解释“资源怎么加载、界面怎么刷新、对象什么时候释放”。

### 📐 “锁死架构”不是限制创意，而是固定做事的规矩

| 需要先说清的事 | Revolution 的统一约定 | 对 Agent 开发的价值 |
|---|---|---|
| 常用功能从哪里调用？ | 优先使用现有 `Rev` 公开入口，不绕过框架另造一套 | 少一些随任务变化的写法，也少一些不存在的 API |
| 登录、大厅、战斗怎样切换？ | 状态机负责阶段，`RevScope` 负责阶段内的生命周期 | 流程切换和对象清理有共同规则 |
| UI 和业务怎样分工？ | 面板负责展示与交互，业务放到合适的 Logic 或服务 | 不把整个游戏写进按钮回调 |
| 用完的东西谁负责清理？ | 明确 owner、作用域和资源取用／释放配对 | 避免界面关了，旧事件和旧任务还在运行 |
| 策划想调数值怎么办？ | 数值走 Excel 导表与启动校验，代码读取配置 | 不用每次改攻击力都让 AI 重写逻辑 |
| 出错时怎样找到原因？ | 明确失败结果与统一日志出口 | 不再只得到一句“可能没加载成功” |

🔒 这就是原话里“防止 AI 乱写代码”的具体落点：**不是让 AI 少写，而是让它写出来的东西能放进同一个工程、被下一位开发者继续维护。**

### 🤖 Skill 让这套规范真正进入 AI 的工作过程

[`revolution-framework`](Skills/revolution-framework/SKILL.md) 不只是列出模块名字，而是告诉 Agent：

- 接到任务先怎样判断职责和生命周期；
- 去哪里查真实 API，而不是凭训练记忆猜接口；
- 常见问题应该先检查哪里；
- 改完怎样审查、编译、运行和说明验证范围。

[`revolution-ai-automation`](Skills/revolution-ai-automation/SKILL.md) 则指导它怎样发现真实工具、安排操作、检查业务结果、保存证据并收尾。

**🧠 你不必从头背完每个模块，但也不需要盲目信任 AI。** 先理解基本分工，具体接口按任务查；AI 查得到规范，你也查得到同一份规范。

深入阅读：[官方编程架构规范](Revolution.Document/编程范式/Revolution官方编程架构规范.md) · [Skill 安装与说明](Skills/README.md)。

---

## 🛠️ 第二层革命：让 AI 从只会写代码，变成能操作 Unity

### 🧩 游戏功能不只存在于 C# 文件里

AI 写好了脚本，不代表功能已经接进游戏。你通常还要做这些事：

- 建对象、加组件、设置字段；
- 处理预制体和场景引用；
- 导入资源、导出配置、检查分包；
- 等编译、进入 Play、查看日志，必要时构建。

如果这些步骤仍然全部由你来点，AI 实际上只是替你完成了中间的“写代码”一段。

### 🔌 RevMCP 把实际工具交到 Agent 手里

Revolution 自带 [`RevMCP`](Revolution.RevMCP/README.md)。它连接目标工程，把**已经实现、已经开放的能力**提供给 Agent。

Agent 可以先发现工程里有什么，再调用对应工具：查看场景和对象、处理资产和组件、检查编译与 Console、执行框架工具，以及操作已支持的 UI 和游戏接口。

这里最重要的不是“工具数量多”，而是**AI 不再靠猜测操作你的工程**：

> 🔍 先问工程“你提供什么能力”，再按真实名字和参数执行。没有开放的业务接口，就先明确接线，不能假装它已经存在。

### ⌨️ CLI 让鼠标操作变成可重复的流程

[`Revolution.RevCLI`](Revolution.RevCLI/README.md) 提供命令行入口。原先需要人反复打开窗口、点按钮的操作，可以用明确命令组织起来。

对新手而言，你不必一开始就会写复杂脚本。先理解区别：

- **手动操作**：这次你点了一遍，下一次还得重新点；
- **脚本化操作**：步骤和参数明确，Agent、开发者或自动化流程可以再次执行。

MCP 面向 AI 客户端，CLI 面向终端和脚本；它们连接的是实际能力，而不是两套互不相干的工作方式。

**⚡ 革命性就在这里：AI 能接着把功能装配进工程，而不是把一段代码交给你，再让你补完剩下的步骤。**

完整接入与操作范围见：[RevMCP 使用说明](Revolution.Document/自动化工具/RevMCP使用说明.md) · [CLI 使用说明](Revolution.Document/自动化工具/Revolution自动化工具使用说明.md)。

---

## ✅ 第三层革命：让 AI 从口头交付，变成用真实结果交付

### 🚦 “编译通过”为什么还不算功能完成？

编译通过只能说明代码没有被编译器拒绝。它不能证明：

- 按钮真的触发了业务；
- 奖励没有重复发放；
- 返回大厅后旧战斗资源已经释放；
- 再进一次游戏流程仍然正常。

截图也有价值，但截图不能单独证明余额、背包或资源引用这些内部状态正确。

**📌 Revolution 希望把“AI 说完成了”换成“AI 跑过了这些检查，结果在这里”。**

### 🎯 GM 让 AI 测到真正的游戏业务

GM 指令可以理解成开发时预留的“测试按钮”：例如查询金币、开始一场战斗、检查奖励、复位测试状态。

这些入口由项目开发者提供，并应调用玩家操作共用的业务逻辑。这样 Agent 才能测试“游戏怎么工作”，而不是直接把某个字段改成预期值，再宣布成功。

例如测试购买：

1. 查询购买前的金币和背包；
2. 调用真实购买逻辑；
3. 检查扣款和道具到账；
4. 再测试金币不足时，购买是否被拒绝、状态是否保持不变；
5. 清理测试数据。

这是**测试场景示例**，不是框架已经替所有游戏实现了商店。你开放了哪些查询、动作和复位入口，Agent 才能验证哪些业务。

### 🧪 一次值得相信的验收，要有四部分

| 部分 | 通俗解释 |
|---|---|
| **前置条件** | 先确认从什么状态开始，不拿上次残留的数据做测试 |
| **真实动作** | 点真实按钮，或者调用玩家共用的业务入口 |
| **结果检查** | 用断言检查状态，结合日志与截图解释现场 |
| **收尾清理** | 停止自己启动的测试、释放资源、恢复自己改变的状态 |

没有后置检查，只能证明“命令执行过”；没有收尾，下一次测试可能被上一次污染。

框架中的部分纯 C# 内核还可以脱离 Unity 做断言，但**内核测试、编辑器操作测试、游戏业务测试和真机测试是不同层次**。通过其中一层，不能代替其它层。

---

## 🔗 把三层连起来：一次完整的 Agent 开发

假设你想给游戏增加一个“领取奖励”功能。你可以这样提出任务：

> 按 Revolution 官方规范实现奖励领取。业务和界面分开；为测试开放状态查询、真实领取和复位入口。实现后检查首次领取成功、重复领取被拒绝，保存结果并清理。缺少什么接口先说明，不要直接改字段伪造通过。

接入所需工具与业务接口后，Agent 的工作不应停在“写好了”：

```text
你说明玩法与验收要求
        ↓
Agent 按 Skill 与官方规范查接口、安排职责
        ↓
修改代码，完成必要的场景／预制体／资源接线
        ↓
通过 MCP 或 CLI 检查编译、启动测试
        ↓
通过真实 UI／GM／业务接口执行操作
        ↓
断言结果，检查日志，按需截图
        ↓
清理测试状态，报告通过、失败与未验证范围
```

**🔁 “全链路 AI 自动化”说的就是这条闭环：开发、装配、执行、验证、收尾不再互相断开。**

它不是“一句话保证自动做完任意游戏”。没有接入的系统、没有提供的业务入口、没有运行的设备测试，仍然需要补齐，并在结果里明确说清。

人的职责也没有消失，只是更集中：**决定玩法、定义验收标准、授权必要操作、审查结果，而不是反复替 AI 做机械点击。**

---

## 🏆 为什么是 Agent 时代开发 Unity 客户端的最佳选择？

Revolution 所强调的“最优选择”，不是靠模块数和代码行数排名，而是围绕 Agent 开发最需要的条件来建设框架。

| 你真正需要的 | Revolution 的回答 |
|---|---|
| AI 多写几个功能后，工程仍然能维护 | 先给统一架构，再实现业务，不每次重新发明基础设施 |
| 新来的 Agent 能接着干，而不是推倒重来 | 共享规范、Skill、真实 API 和文档索引，而不是依赖上一段聊天记忆 |
| 不用先学习完整框架才能开始做游戏 | 新手先懂分工，再让 Agent 按任务查对应入口和示例 |
| AI 不只输出代码，还能完成工程操作 | MCP 与 CLI 接入实际 Unity 编辑器和框架工具 |
| 对“功能完成”有明确判断依据 | 真实 UI／GM／业务调用，加断言、日志、截图和收尾 |
| 出问题能复现、能定位 | 步骤、参数和失败原因可记录，不只剩一句“我这里能跑” |

**⚖️ 单独有一个聊天助手，不等于工程有规范；单独接一个 MCP，也不等于业务可测试；单独堆许多模块，更不等于形成交付闭环。**

Revolution 的核心价值，是把它们连起来，让“AI 能写”向“AI 能在这个工程里持续、规范、可验证地开发”推进。

这也是为什么本文不逐个展开资源、UI、音效、计时器、对象池等模块：**它们是共同规矩背后的现成能力，不是革命性的全部理由。** 常规模块与可选资源热更扩展按需查阅 [文档站](https://yokino337088.github.io/Revolution/) 即可。

> 🧭 “最佳选择”表达的是框架的定位与设计主张，不是对所有模型、项目和平台做完对比测试后的绝对排名。是否适合你的项目，最直接的判断方法是用自己的一个小功能跑通上述闭环。

---

## 📊 已经跑通了什么？

不是只提供一个“未来可以自动化”的方案。仓库已保留 **2026-10-22** 的实际验证记录，环境为 Windows、Unity `2022.3.15f1c1`、Python 3.11。

其中一条完整链路是：

**复位 → 点击真实按钮加 1 → 查询计数为 1 → GM 再加 2 → 断言计数为 3 → 截图 → 检查日志 → 最后归零。**

这条流程已在 MCP 连接的 Unity Editor 与独立 Windows 开发版 Player 中执行。它展示了 UI、GM、状态检查、截图与清理怎样组成同一条闭环。

| 已记录的验证 | 结果与范围 |
|---|---|
| ✅ Automation Python 行为回归 | 19/19，覆盖类型、引用、断言、等待和失败清理 |
| ✅ RevMCP Python 行为回归 | 13/13，含真实 stdio 子进程等检查 |
| ✅ Unity EditMode | 24/24，通过 |
| 🖥️ MCP → Editor / Windows 开发版 Player | 上述计数 Demo 的真实操作、断言、截图和清理通过 |

这些是**指定日期和环境下的记录，不是本次 README 修改重新跑出的结果**，也不代表全部编辑器动作、任意玩法或手机真机都已验收。

查看 [完整验证记录与截图](Revolution.Document/自动化工具/验证记录/2026-10-22-RevMCP.md) · [可复用的示例测试计划](Revolution.RevMCP/examples/ui-gm.plan.json)。

---

## 🚀 新手怎样开始？

**🎬 先跑通一个小闭环，再把它换成自己的玩法。不要一开始就学习所有模块。**

### 📦 1. 拿齐框架与自动化工具

新手可以先获取本仓库，用 Unity 打开 `RevolutionFrameWork_Unity/`；已有游戏工程的开发者，按 [框架安装说明](RevolutionFrameWork_Unity/Assets/Revolution/README.md) 接入框架本体。

使用 RevMCP 时，保留相邻的 `Revolution.RevMCP/` 和 `Revolution.RevCLI/`，并确保目标 Unity 工程安装了对应桥接代码。**只复制 Python 文件，不会自动让任意 Unity 工程具备工具能力。**

自动化侧需要 Python **3.10+**，仅使用标准库，无需安装 pip 第三方依赖。仓库开发工程的既有验证环境为 Unity `2022.3.15f1c1`；其它版本和平台应按实际工程验证，不从此处推断兼容结论。

### 📘 2. 给 Agent 安装两份工作手册

把完整的 `revolution-framework` 和 `revolution-ai-automation` Skill 目录安装到所用 Agent 支持的 skills 位置，具体方法见 [Skills 安装说明](Skills/README.md)。

一份负责**怎样规范地开发**，一份负责**怎样调用工具并验证**。安装目录及启用方式由客户端决定，不能只把文件放进去就假定它一定被加载。

### 🔗 3. 连接你的 MCP 客户端

打开目标 Unity 工程，等待编译完成。在 Unity 菜单 `Revolution.Tools/AI/RevMCP 控制台` 查看状态并复制配置，按所用客户端的配置方式接入。

配置的核心是：让客户端用本机 Python 启动 `rev_mcp.py`，指定**目标 Unity 工程的绝对路径**，使用 `serve` 模式。现成结构见 [MCP 配置示例](Revolution.RevMCP/examples/mcp-config.example.json)，里面的示例路径必须换成本机路径。

让 Agent **先调用 `rev_discover`**，确认实际能力；不要先让它按想象调用一批工具。

### 🕹️ 4. 跑官方 Demo，再接自己的游戏

先用 [RevMCP 入门流程](Revolution.RevMCP/README.md) 运行 `RevAutomationDemo.unity` 的计数测试，检查“操作、断言、截图、清理”是否都成立。

然后选自己游戏里最小的一段业务，例如开关面板、领取一次奖励或进入并退出战斗：明确规则，提供必要的真实接口，让 Agent 开发并验证。

**🏁 第一次成功，不是让它生成几千行代码，而是让一个小功能完成有证据的闭环。**

---

## 🚧 能力边界与文档入口

### 🛡️ 自动化不是无条件、无限制地控制工程

- **只调用实际开放的能力。** 项目业务要提供受控查询、动作和清理入口；没有接口就不能自动测试该业务。
- **不是所有界面交互都支持。** 已支持的 UGUI 操作与第三方控件、拖拽、摇杆等不能混为一谈。
- **危险操作按接口规则确认。** 部分删除、高风险 GM 等要求 `confirm`；不是所有写操作都强制确认，也不能把这个参数当成完整权限系统。
- **测试不会自动撤销一切修改。** 要设计收尾；操作超时不代表未执行，不应盲目重复变更。
- **开发测试环境与正式发行环境分开。** 独立 Player 的运行时接口需要显式启用，正式发行包默认不开放。
- **不默认执行任意 shell。** 外部 CLI／Python 工具需用 manifest 显式登记。
- **实测范围要如实报告。** Windows Demo 通过，不能直接宣称 Android、IL2CPP、WebGL／小游戏真机或所有玩法通过。

知道边界，不会削弱革命性；恰恰因为**能说清什么做过、什么没做过**，自动化结果才值得信任。

### 🗺️ 按你现在的问题查，不必通读

| 你想了解什么？ | 阅读入口 |
|---|---|
| AI 应该怎样组织工程代码？ | [官方编程架构规范](Revolution.Document/编程范式/Revolution官方编程架构规范.md) · [开发 Skill](Skills/revolution-framework/SKILL.md) |
| 怎样安装并启用 AI 工作手册？ | [Skills 安装说明](Skills/README.md) |
| 怎样让 Agent 操作和测试 Unity？ | [RevMCP 使用说明](Revolution.Document/自动化工具/RevMCP使用说明.md) · [自动化 Skill](Skills/revolution-ai-automation/SKILL.md) |
| 怎样用脚本执行编辑器操作？ | [Automation 入口](Revolution.RevCLI/README.md) · [CLI 使用说明](Revolution.Document/自动化工具/Revolution自动化工具使用说明.md) |
| 工具怎样连接、为什么这样设计？ | [RevMCP 架构解析](Revolution.Document/自动化工具/RevMCP架构解析.md) |
| 是否有实际运行证据？ | [2026-10-22 验证记录](Revolution.Document/自动化工具/验证记录/2026-10-22-RevMCP.md) |
| 临时需要查资源、UI、音效或热更新？ | [在线文档站](https://yokino337088.github.io/Revolution/) · [全部文档索引](Revolution.Document/README.md) |

欢迎用一个真实游戏需求验证这套工作方式，通过 [Issues](https://github.com/Yokino337088/Revolution/issues) 反馈缺少的入口、复现步骤或失败结果。贡献时保留 Unity 资产的 `.meta`，让代码、文档与测试接口同步。

---

**🌟 Revolution 革新的，不只是“怎样调用框架”，而是“怎样让 Agent 在统一工程里完成可信的游戏开发”。**

**你定义玩法与标准，Agent 按规范执行，真实运行结果负责证明。**

[Star](https://github.com/Yokino337088/Revolution) · [Issues](https://github.com/Yokino337088/Revolution/issues) · [文档站](https://yokino337088.github.io/Revolution/) · [MIT License](LICENSE)
