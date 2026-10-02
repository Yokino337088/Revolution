---
name: revolution-framework
description: 本指南适用于 Revolution（RevolutionFramework / Rev 前缀）Unity 游戏框架的开发与代码审查。当任务涉及以下任何内容时使用：Revolution 框架、Assets/Revolution、RevLog / RevTimer / RevMono / RevPool / RevEvent / RevServiceLocator / RevTask / RevScene / RevInput / RevSound / RevStateMachine / RevUI / RevResManager / RevDataTableManager / RevGM / RevSequence 等模块，资源加载与 AssetBundle（RevAB 打包工具、ResMap.txt、逻辑路径、引用计数、编辑器直读 vs AB 模式），UI 面板（RevUIPanel / RevUIPanel&lt;TData&gt; / [RevBind] / 面板池 / Back 语义），事件与计时器清理（owner / scope），异步 RevTask（await / 取消 / 分帧），服务定位器装配，GM 指令注册，配置表（导表工具），以及扩展包 RevHotUpdate 资源热更（清单 / 差量下载 / 版本目录 / 两个钩子 / 本地 CDN demo）。也适用于：按本框架规范生成或修改代码（Rev 前缀、方法括号不换行、方法必须带大括号、核心处写小白向注释）、排查本框架的运行时问题、工程外纯 C# 断言验证、以及 Revolution 项目的架构决策（要不要新增分层、用什么模块）。即使提问者没有明确说出“Revolution”或“框架”，只要代码里出现上述类型/目录，也应使用本 skill。
---

# Revolution 框架开发 Skill

Revolution 是一套**从零手写、可读可测、无魔法**的 Unity 游戏框架（C#）：**17 个运行期模块 + 2 套编辑器工具**（Runtime 179 个 `.cs` / 约 2.97 万行），
另外有一个**扩展包** `RevHotUpdate`（资源热更，独立程序集、按需安装）。

它的三条设计取向直接决定你该怎么写代码：

1. **模块化、门面唯一**：每个模块只暴露一个门面（`RevTimer` / `RevResManager` / `RevUI` …），业务只碰门面，内核（`Core` / `Implementation`）不改也不读。
2. **失败必须可见**：不吞异常、不静默 return —— 失败一律给原因枚举 / 句柄字段 / 返回值，日志有唯一出口 `RevLog`。
3. **内核尽量不依赖引擎**：路径、句柄、槽位表、池引擎、日志/计时器内核是纯 C#，**能链接进普通 .NET 工程跑断言**（见 `references/verify-and-test.md`）。

## 第一步：先定位，不要猜

| 你要做的事 | 先读 |
|---|---|
| 用某个模块写业务代码 | `references/modules-core.md`（日志/计时/公共Mono/对象池/事件/服务定位器/异步/场景/输入）或 `references/modules-content.md`（音效/状态机/资源/单例/UI/序列/GM/配置表/热更） |
| 加载资源 / 打包 AB / ResMap / 编辑器直读差异 | `references/resource-and-ab.md` |
| 写 UI 面板、面板传数据、Part、Back | `references/ui-and-layering.md` |
| 异步、取消、时间域、每帧回调、事件清理 | `references/modules-core.md` + `references/pitfalls.md` |
| 接热更 / 排查热更 | `references/hot-update.md` |
| 编辑器菜单（打包、导表、GM 面板、热更清单、demo 工具） | `references/editor-tools.md` |
| 代码/注释规范（**本项目的硬规范**） | `references/code-style.md` |
| 审查别人代码 | `REFERENCE_GUIDELINES.md` |
| 出过什么问题、什么写法会踩雷 | `references/pitfalls.md` |
| 怎么证明"改对了" | `references/verify-and-test.md` |
| 权威文档在哪（每个模块的《使用说明》《架构解析》） | `references/docs-map.md` |

## 技术铁律（写代码必须遵守）

1. **只用门面**：`RevLog` / `RevTimer` / `RevMono` / `RevPool` / `RevEvent` / `RevTask` / `RevScene` / `RevInput` / `RevSound` / `RevUI` / `RevResManager` / `RevDataTableManager` / `RevGM` / `RevSequence` / `RevHotUpdate`。不要 `new` 内核对象、不要反射、不要绕过门面直接读文件。
2. **失败必带原因**：资源看 `handle.ErrorReason`，GM 看 `RevGMResult.Message`，热更看 `RevHotUpdateResult.Error` / `RevHotUpdate.LastError`；业务拒绝就抛 `RevGMUsageException("理由")` 这类"带人话"的异常，**不要静默 return**。
3. **清理纪律**：计时器 / 每帧回调 / 事件订阅，**能传 `owner: this` 就传**，销毁时一行 `RevTimer.CancelAllOf(this)` / `RevMono.RemoveAllOf(this)` / `RevEvent.RemoveAllByOwner(this)`；资源**成对** `Load` → `Release`。匿名 lambda 注册事件后**无法按委托移除**，这是泄漏的头号来源。
4. **资源只写逻辑路径**：用生成的常量（`RevResPath.*`，见 `Assets/Revolution/Generation/RevResPath.cs`）或"两段式" `Load(根目录, 资源名, 类型, 分组)`；**禁止**自己拼 `StreamingAssets` / `Application.dataPath` 路径读资源（缓存、引用计数、平台差异、热更都会失效）。
5. **UI = 一个面板类 + 职责钩子**：`[RevUIPanel]` 声明路径、`[RevBind]` 绑节点、`OnBindView` 只装配、`OnRefreshView` 只落屏、`OnClick` 只转发；数据**单向** `SetData → OnDataChanged → RefreshView`；**框架不提供 `System` / `BusinessLogic` 层**（结论见 `references/ui-and-layering.md`）——跨界面复用的能力交给**服务定位器**，只属于这一屏的流程留在面板里。
6. **异步只用 `RevTask`**：`await RevTask.Delay/Yield`、`await RevScene.LoadAsync(...)`、`await RevHotUpdate.InitializeAsync(...)`；**不要**引入 `System.Threading.Tasks.Task`、不要用协程写业务（`RevMono.StartCoroutine` 只用于桥接旧代码）。取消用 `RevCancellationTokenSource` + `token.ThrowIfCancelled()`。
7. **事件**：`string` 事件名（常量集中在一个类里）+ **同一事件名只能有一种签名**；只在主线程注册/派发；派发过程中可以安全增删（框架做了标记删除），但回调里别做重活。
8. **时间**：`RevTimer.After/Every` 必须能停（`owner` 或 `RevTimer.OpenScope()`）；UI 倒计时 / 超时用 `RevTimeDomain.Unscaled`（`Scaled` 会被 `timeScale` 冻结）；`At(...)` 前必须 `SyncServerTime`。
9. **服务**：装配只写在一处（组合根 `RevServiceLocator.Create()...Build()`），装配后不可改；必需依赖用 `GetRequired<T>()`（漏注册启动期就炸），可选能力用 `Get<T>()`/`TryGet<T>()`；`AddScoped` 的服务**必须**放在 `CreateScope()` 的 `using` 里取。
10. **编辑器 vs 真机**：编辑器默认**只**注册 `RevEditorResPolicy`（AssetDatabase 直读，AB/Resources 完全不参与）；要看 AB / 热更效果，必须开 `RevResBootstrap.UseABInEditor = true`（菜单 `Revolution.Tools/资源/AB 加载模式（编辑器）`）。另外**编辑器里平台名恒为 `PC`**（`RevHotPlatform` 带 `!UNITY_EDITOR`）。
11. **不新增分层、不新增门面**：不要给框架加 `System` / `BusinessLogic` 这样的层，也不要给某个模块加"第二套加载 API"（会立刻长成第二套系统）。真需要扩展时，优先**服务 + 约定**，其次才是框架改动。
12. **改框架要照既有形状**：目录分层 `Core`（数据与契约）/ `Facade`（对外入口）/ `Implementation`（实现）/ `Interfaces`（接口）/ `Support`（宿主适配）；每个文件**开头写清"为什么这么写"**；新增资产要带 `.meta`（文件夹 meta 一律是**同级** `<文件夹名>.meta`）。

## 代码与注释规范（本项目硬规范，必须遵守）

1. **方法签名的 `(` 与参数不换行**：默认写一行；**只有太长（显示宽度 > 约 160 列）才换行**。长消息/长参数先提成局部变量，让"调用/抛出"本身保持单行。
2. **方法体必须带大括号**，哪怕只有一行（`if (x) return;` ✗ → `if (x) { return; }` ✓）。
3. **核心 / 关键 / 容易踩坑或被忽略的地方要写详细注释，面向小白**：解释"为什么这么做 / 不这么做会怎样"，而不是复述代码。
4. **类名一律 `Rev` 前缀**，文件名 = 类名；命名空间 `Revolution`（模块内部）或 `Revolution.Demo.<模块>`（示例）。
5. 中文注释、术语与文档保持一致（"门面 / 句柄 / 分组 / 逻辑路径 / 大版本锚定"…）。
6. 详细正反例与自检脚本思路见 `references/code-style.md`。

## 开发行为准则

以下四条偏向"稳"而不是"快"。琐碎任务自行判断。

### 1. 先想清楚再写

不要假设、不要藏疑惑、把取舍摆出来。动手前先明确：

- **模块选型**：这是"多久之后"（`RevTimer`）、"每帧"（`RevMono`）、"等一个异步结果"（`RevTask`）、还是"一次变化多处反应"（`RevEvent`）？
- **生命周期归属**：这段逻辑"关掉这个界面之后还该活着吗"？该 → 服务；不该 → 面板里。
- **失败出口**：失败时业务要拿到什么？（原因枚举 / 回调 / 异常 / 重试）
- **编辑器与真机差异**：这次改动在"编辑器直读"和"AB 模式"下都成立吗？

### 2. 最简优先

写解决问题的最少代码，不做没人要的抽象/配置/"以后可能用得上"。判断法：**加第四个监听者时，广播方要不要改代码？** 要改就别上事件。

### 3. 外科手术式改动

只动该动的：不顺手"美化"相邻代码、不重构没坏的、不删既有死代码（发现了**报告**即可）；匹配既有风格；自己改动造成的孤立引用要清掉。**改完要同步文档**（见下）。

### 4. 目标驱动、可验证

把任务变成可验证目标，别停在"能跑"：

```text
1. 读该模块《使用说明》→ 验证：写法与文档一致（门面、参数、清理方式）
2. 写代码 → 验证：Unity 编译 0 error（必要时用工程外桩工程先编译一遍纯 C# 部分）
3. 跑对应 demo 场景 → 验证：屏幕上"日志/结果"符合预期
4. 要动框架内核 → 验证：工程外纯 C# 断言全过（见 references/verify-and-test.md）
5. 交付 → 验证：文档 / demo / GM 命令 / change 说明同步
```

## 典型工作流

### A. 写新功能（7 步）

1. **定位模块**（上面的表）；没有合适模块就先用"服务 + 面板"实现，**不要**先动框架。
2. **读该模块的《使用说明》**（`references/docs-map.md` 给路径）——它是权威，本 skill 只是索引。
3. **找最近的 demo**（`Assets/Revolution.Demo/<模块>.Demo/`，18 个可运行场景）照着写。
4. 写代码，守住上文的铁律与规范。
5. **自我审查**：过一遍 `REFERENCE_GUIDELINES.md` 的清单。
6. **验证**：编译 + demo 场景 / GM 面板 / 工程外断言。
7. **同步文档**：改了行为 → 改该模块《使用说明》；改了设计 → 改《架构解析》；同时更新 md 与 html（`Revolution.Document`，在线站会自动发布）。

### B. 审查代码

读 `REFERENCE_GUIDELINES.md`，按"严重问题 → 规范问题 → 潜在风险 → 优化建议"输出；重点看：owner/scope 清理、成对 Release、事件签名与重复订阅、时间域、编辑器/真机差异、异常是否被吞。

### C. 排错（30 秒顺序）

1. `RevLog` 里的 tag 过滤（模块都有自己的 tag：`Scene` / `ActionSequence` / `HotUpdate`…）。
2. 句柄原因：`handle.ErrorReason`（资源）、`RevGMResult.Message`（GM）、`RevHotUpdate.LastError` + `RevHotUpdate.Dump()`（热更）。
3. 能力自检接口：`RevTimer.Count` / `RevMono.Count` / `RevEvent.GetListenerCount(名字)` / `RevResManager.CachedCount` / `RevPool.DumpStats()` / `RevLog.Dump()`。
4. 复现：打开对应 demo 场景点按钮；或写一条 GM 命令直接在编辑器面板里跑。

### D. 交付

- 代码 + **文档（md & html）** + 必要时 demo/GM 命令；
- 里程碑提交按仓库既有风格写提交信息（中文、说清"为什么"与验证数据）；
- 安装/分发走三条生成分支：`package`（框架本体）、`demo`（示例工程）、`hotupdate`（热更扩展包）。

## 参考文件导航

| 文件 | 何时读 |
|---|---|
| `references/modules-core.md` | 写 RevLog / RevTimer / RevMono / RevPool / RevEvent / RevServiceLocator / RevTask / RevScene / RevInput 代码时：门面签名、最小示例、铁律、文档路径 |
| `references/modules-content.md` | 写 RevSound / RevStateMachine / RevResManager / RevSingleton / RevUI / RevSequence / RevGM / RevDataTableManager / RevHotUpdate 代码时（同上格式） |
| `references/resource-and-ab.md` | 资源加载、引用计数、分组卸载、ResMap 三列、AB 打包工具、编辑器直读 vs AB、兜底策略 |
| `references/ui-and-layering.md` | UI 主链路、数据单向流、面板池与 Back、Part/宿主、以及"要不要 System / BusinessLogic 层"的结论 |
| `references/hot-update.md` | 接热更（一行 `InitializeAsync`）、两个钩子、远端目录约定、大版本锚定、本地假 CDN demo、失败收场 |
| `references/pitfalls.md` | 出问题 / 写之前避雷：编辑器直读、Domain Reload、匿名 lambda 泄漏、ResMap 引导悖论、平台名恒为 PC、池对象未清零… |
| `references/code-style.md` | 生成/修改代码前对齐规范：括号不换行、必须大括号、注释面向小白、文件头写法、命名 |
| `references/editor-tools.md` | 菜单路径汇总（打包 / 导表 / GM 面板 / 热更清单 / demo 工具）与各自用途、打包链顺序 |
| `references/verify-and-test.md` | 怎么验证：工程外纯 C# 断言（可链接的文件清单 + 实测数字）、18 个 demo 场景、GM 面板、破坏性测试 |
| `references/docs-map.md` | 找权威文档：每个模块的《使用说明》/《架构解析》路径 + 在线文档站 + 仓库/分支安装 |
| `REFERENCE_GUIDELINES.md` | **代码审查入口**：逐条清单（清理 / 资源 / UI / 异步 / 事件 / 时间 / 服务 / 编辑器差异 / 规范）+ 正反例 |
| `templates/RevXxxPanel.cs.txt` | 新建 UI 面板时的骨架（含 `[RevUIPanel]` / `[RevBind]` / 三个钩子 / 数据对象） |
| `templates/RevXxxService.cs.txt` | 新建业务服务时的骨架（纯 C#、接口 + 实现 + 组合根装配示例） |
| `templates/RevXxxDemo.cs.txt` | 新建模块 demo 场景脚本时的骨架（OnGUI：左侧按钮 + 右侧步骤日志） |
| `templates/offline-harness/` | 要验框架内核时：工程外纯 C# 断言工程模板（csproj + 断言入口 + 链接真实源码的清单） |
