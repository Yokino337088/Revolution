# revolution-framework

`revolution-framework` 是一个面向 **Revolution**（本仓库的 Unity 游戏框架，代码前缀 `Rev`）的专用 **开发与代码审查 Skill**。

它不是通用 C# / Unity 助手，而是围绕 Revolution 的真实写法，给 AI Agent 提供
**模块选型、API 速查、规范约束、坑位避雷、问题排查、代码审查、验证方式** 的一体化支持。

核心目标：

- 让 Agent 一上手就用**门面**写业务（`RevTimer` / `RevResManager` / `RevUI` / `RevEvent` / `RevTask` …），不越界碰内核；
- 按本框架的**硬规范**生成或检查代码（`Rev` 前缀、方法括号不换行、方法必须带大括号、核心处写小白向注释）；
- 把"编辑器直读 vs AB 模式""引用计数与分组卸载""面板池与 Back 语义""事件/计时器清理""热更覆盖式语义"这些**只有踩过才知道**的点提前说清；
- 提供**权威文档索引**（`Revolution.Document` 每个模块的《使用说明》《架构解析》），避免"照着过时记忆写代码"；
- 给出一致的**验证手段**：编译 → demo 场景（18 个）→ GM 面板 → 工程外纯 C# 断言 → 真机/破坏性测试。

## 覆盖范围

| 主题 | 覆盖内容 |
|---|---|
| 运行期模块（17 个） | 日志 `RevLog` · 计时器 `RevTimer` · 公共 Mono `RevMono` · 对象池 `RevPool`/`RevRefPool` · 事件 `RevEvent` · 服务定位器 `RevServiceLocator` · 异步 `RevTask` · 场景 `RevScene` · 输入 `RevInput` · 音效 `RevSound` · 状态机（轻量/栈/重量）· 资源 `RevResManager` · 单例（三种） · UI `RevUI`/`RevUIPanel` · 动作序列 `RevSequence` · GM `RevGM` · 配置表 `RevDataTableManager` |
| 扩展包 | 资源热更 `RevHotUpdate`（清单 / 差量下载 / 版本目录 / 两个钩子 / 覆盖式语义 / 大版本锚定 / 本地假 CDN demo） |
| 编辑器工具 | RevAB 打包工具（打包/分包/依赖/体积/检查/快照）、导表工具、GM 指令面板、热更清单窗口、热更 demo 工具、生成物（`RevResPath` / `ResMap.txt` / `RevSoundPath`） |
| 工程面 | 三条生成分支（`package` / `demo` / `hotupdate`）与安装方式、文档站、目录分层约定、`.meta` 规矩 |
| 工程化 | 代码/注释规范、审查清单、坑位集、工程外断言（可链接的纯 C# 文件清单 + 实测数字）、破坏性测试清单 |

## 目录结构

```text
revolution-framework/
├── SKILL.md                     ← 主入口：技术铁律 + 代码规范 + 行为准则 + 工作流 + 导航
├── REFERENCE_GUIDELINES.md      ← 代码审查入口：逐条清单（清理/资源/UI/异步/事件/时间/服务/热更/规范）+ 正反例
├── references/
│   ├── modules-core.md          ← 日志/计时/公共Mono/对象池/事件/服务定位器/异步/场景/输入：门面签名 + 示例 + 铁律 + 文档路径
│   ├── modules-content.md       ← 音效/状态机/资源/单例/UI/序列/GM/配置表/热更（同上格式）
│   ├── resource-and-ab.md       ← 资源链路、策略链与两种模式、ResMap 三列、引用计数与分组、AB 打包、排查顺序
│   ├── ui-and-layering.md       ← UI 主链路、数据单向流、面板池与 Back、Part/宿主、要不要 System/BusinessLogic（结论：不做）
│   ├── hot-update.md            ← 热更一行接法、两个钩子、远端目录、版本模型、平台差异、存储选型、装法
│   ├── pitfalls.md              ← 20 条高频踩坑：现象 → 原因 → 正确做法
│   ├── code-style.md            ← 本项目硬规范：括号不换行、必须大括号、注释面向小白、文件头、命名、自检脚本
│   ├── editor-tools.md          ← 全部菜单路径与用途、打包链顺序、生成物、CI 工作流
│   ├── verify-and-test.md       ← 五级验证、可链接文件清单、实测数字、破坏性测试 8 项、分支与文档站验证
│   └── docs-map.md              ← 文档地图（每个模块的使用说明/架构解析路径）+ 仓库与安装速览
└── templates/
    ├── RevXxxPanel.cs.txt       ← UI 面板骨架（[RevUIPanel]/[RevBind]/钩子/数据对象/复用与关闭清理）
    ├── RevXxxService.cs.txt     ← 业务服务骨架（接口 + 实现 + 组合根装配 + 失败出口）
    ├── RevXxxDemo.cs.txt        ← 模块 demo 场景脚本骨架（OnGUI：左按钮 + 右步骤日志）
    └── offline-harness/         ← 工程外断言模板（README + csproj + Program：断言形状、零分配实测、固定种子）
```

## 推荐安装方式

**推荐项目安装，而不是全局安装。**

原因：每个项目可以用自己的 skill 版本；skill 能和源码、文档、分支一起维护；不同项目之间互不污染；
更新 skill 时随项目一起提交；团队协作时所有人看到同一套规则。

## 安装

### 方式一：从 `skill` 分支克隆（推荐，一条命令）

```text
# 直接克隆进"项目级 skills 目录" —— 克隆完就是 .codebuddy/skills/revolution-framework/SKILL.md
git clone -b skill https://github.com/Yokino337088/Revolution.git .codebuddy/skills

# 换工具就换目录：
#   .claude/skills   .codex/skills   .opencode/skills   .openclaw/skills

# 只想装某一个 skill（比如以后仓库里有多个）：克隆后把需要的子目录拷走
#   cp -r .codebuddy/skills/revolution-framework <你的 skills 目录>/

# 团队共享同一份规范：用子模块（可 git submodule update --remote 更新）
git submodule add -b skill https://github.com/Yokino337088/Revolution.git .codebuddy/skills

# 全局安装（跨项目共享）：把 .codebuddy/skills 换成用户级目录
git clone -b skill https://github.com/Yokino337088/Revolution.git ~/.codebuddy/skills
```

> `skill` 是**生成分支**（CI 从 `main` 的 `Skills/` 整份重建，不保留历史）：改 skill 请改 `main`，不要往生成分支上提交。

### 方式二：直接拷贝目录

把整个 `revolution-framework/` 目录放进对应工具的 skills 目录（任选其一，按你用的工具）：

```text
项目安装（推荐）
<your-project>/.codebuddy/skills/revolution-framework/      ← CodeBuddy
<your-project>/.claude/skills/revolution-framework/         ← Claude
<your-project>/.codex/skills/revolution-framework/          ← Codex
<your-project>/.opencode/skills/revolution-framework/       ← OpenCode
<your-project>/.openclaw/skills/revolution-framework/       ← OpenClaw

全局安装（跨项目共享）
~/.codebuddy/skills/revolution-framework/
~/.claude/skills/revolution-framework/
```

> 本仓库里这份位于 `<仓库根>/Skills/revolution-framework/`（`Skills/README.md` 就是本文件）。
> 如果你把框架作为子模块装进工程（`Assets/Revolution`），也可以直接指向它、或用同步脚本复制到上面任一目录。

## 安装后如何验证命中

试着问这些问题（都应命中本 skill 并按 Revolution 的规范回答）：

```text
- RevTimer 的循环计时器怎么防止泄漏？
- 帮我写一个背包面板：带数据、能关闭、点开有动画
- 资源加载用 Load 还是 LoadAsync？为什么真机首次拿不到？
- RevEvent 里同一个事件名注册了两种签名会怎样？
- 这个项目里"编辑器直读"和"AB 模式"有什么区别？我怎么在编辑器里验证热更？
- 帮我审查这段代码有没有违反 Revolution 的规范（括号换行、事件没解绑、资源没释放）
- 热更的"覆盖式语义"是什么意思？为什么不能只从持久化目录读？
- 我想加一层 System / BusinessLogic，这个框架建议怎么做？
- 改完框架内核后，我怎么在不开 Unity 的情况下验证？
```

如果工具命中 `revolution-framework` 并开始按上面的口径回答，就说明安装成功。

## 升级方式

1. 保留整个目录结构不变，覆盖 `SKILL.md`、`REFERENCE_GUIDELINES.md`、`references/`、`templates/`；
2. 项目安装的，和项目一起提交；全局安装的，建议保留版本标签（避免多个项目感知不一致）；
3. **框架代码/文档改动后请顺手核对本 skill**：新增模块、改了菜单路径、改了 API 签名、改了规范，都要同步到这里；
4. 大改后拿真实工程代码做一次 review 验收。

## 推荐实践

- 让 Agent **先读该模块的《使用说明》**（`references/docs-map.md` 给路径），再照 `references/modules-*.md` 的门面签名写代码；
- 审查一律走 `REFERENCE_GUIDELINES.md`，输出固定四段（严重 / 规范 / 风险 / 建议）；
- 生成代码前用 `templates/` 里的骨架起头（它们已经带好注释与清理逻辑）；
- 动框架内核前先搭 `templates/offline-harness/`，把"不变量 + 零分配 + 性能数字"钉死；
- 交付时同步 md + html 文档，并按仓库既有风格写提交信息。

## 一句话总结

`revolution-framework` 把 Revolution 的**模块用法、规范约束、坑位与验证方式**打包成一份可被 AI Agent 直接使用的技能：
既能指导实现，也能检查问题，还能在你动手前告诉你"这个框架已经有什么、别重复造什么"。
