# Revolution

**一套从零手写的 Unity 游戏框架（C#）** —— 可读、可测、无魔法

[![Unity](https://img.shields.io/badge/Unity-2022.3.15f1c1-blue.svg?style=flat-square)](https://unity.com/)
[![License](https://img.shields.io/github/license/Yokino337088/Revolution?style=flat-square)](LICENSE)
[![Last Commit](https://img.shields.io/github/last-commit/Yokino337088/Revolution?style=flat-square)](https://github.com/Yokino337088/Revolution)
[![Issues](https://img.shields.io/github/issues/Yokino337088/Revolution?style=flat-square)](https://github.com/Yokino337088/Revolution/issues)
[![Top Language](https://img.shields.io/github/languages/top/Yokino337088/Revolution?style=flat-square)](https://github.com/Yokino337088/Revolution)
[![Runtime](https://img.shields.io/badge/Runtime%20模块-19%20个%20·%203.03%20万行-brightgreen?style=flat-square)](#-核心模块)

---

## 📖 简介

**Revolution** 是一套**从零手写**的 Unity 游戏框架：**19 个运行期模块 + 2 套编辑器工具**（Runtime 184 个 `.cs` / 约 3.03 万行），
覆盖资源加载 / UI / 动作序列 / 状态机 / 音效 / 计时器 / 输入 / 日志 / 事件 / GM 指令 / 导表 等常规需求。

> ⚠️ **先说清楚：框架本体不自带热更新** —— 既没有代码热更（HybridCLR / ILRuntime / xLua），也没有 AB 远程下载与版本管理
> （AB 只从本机 `StreamingAssets` 读）。但**资源热更现在有官方扩展包 `RevHotUpdate`**（独立包、按需安装、不装零影响）；
> 也支持自己扩展资源层或整体接入 YooAsset —— 三条路都不需要改上层业务代码，详见
> [**不做什么：热更新与远程更新**](#-不做什么热更新与远程更新)。

它跟大多数框架最大的不同是**"内核尽量不依赖引擎"**：路径、句柄、槽位表、对象池引擎、计时器内核这些纯 C# 部分
**可以链接进普通 .NET 工程直接跑断言**（本仓库已有 **145 条行为断言**在工程外跑通，不打开 Unity；见 [工程外验证](#-工程外验证纯-c-跑断言)）。
所以这里没有"猜它能不能工作"，只有"跑给看"。

### ✨ 核心特性

- 🧩 **模块化** - 每个模块一个门面（`RevSound` / `RevTimer` / `RevLog` …），可单独拿走、可整块删除
- 🚀 **一行就能用** - 播放音效、延迟 2 秒、每帧回调、分级日志都是**一行调用**，零配置（不摆物体、不挂脚本）
- 🧪 **可脱离 Unity 验证** - 内核是纯 C#，`dotnet run` 就能跑断言（误差契约 / 代际失效 / 异常隔离都钉死过）
- 🛡 **防漏防崩** - 句柄代际校验（过期句柄不会误伤别人）、循环计时器 `owner` / 作用域一行清理、逐回调异常隔离
- 🚫 **推荐关闭代码裁剪** - 反射 / 泛型 / 场景里挂的原生组件这三类代码，`link.xml` 覆盖不全，漏一个就是"编辑器正常、真机失效"（刚体不响应重力、面板空白、没声音）。关掉的代价只有包体，而包体多少 **3 分钟就能自己测准** → [《代码裁剪 · 使用说明》](Revolution.Document/代码裁剪/代码裁剪使用说明.md)
- 📉 **性能有数字** - 1000 个计时器每帧 0.0139ms；10 万条日志 0 分配；对象池 10 万次创建 <4KB
- 🔍 **失败必带原因** - 框架不吞错误：失败一律给**原因枚举**（外加统一日志系统，可静默可按模块关）
- 📦 **编辑器工具齐** - RevAB 打包窗口（对标 AssetBundle Browser：包树 + 资源表拖拽分包 / 目标平台可选 / Project 窗口包名角标 / 体积依赖漏标检查 / 布局快照对比）+ 导表工具（Unity 编辑器版：拖入 Excel 一键导出，改表自动重读、只写有变化的文件，**仅数据 / 全量两种导出模式**——只改数值不动表结构时不生成代码、不触发脚本编译；另有不依赖 Unity 的 WPF 版）
- 🧭 **小白友好** - 每个模块一份《使用说明》（手把手）＋ 每个文件开头都写清"为什么这么写"

---

## 📚 目录

- [🚀 快速开始](#-快速开始)
- [⚠️ 不做什么：热更新与远程更新](#-不做什么热更新与远程更新)
- [🧩 核心模块](#-核心模块)
- [📁 项目结构](#-项目结构)
- [💻 系统要求](#-系统要求)
- [📚 文档导航](#-文档导航)
- [🧪 工程外验证（纯 C# 跑断言）](#-工程外验证纯-c-跑断言)
- [🎯 设计取向](#-设计取向)
- [💡 为什么用 Revolution？](#-为什么用-revolution)
- [🎮 示例项目](#-示例项目)
- [🤝 贡献与支持](#-贡献与支持)

---

## 🚀 快速开始

### 环境要求

- **Unity 版本**：2022.3.15f1c1（其它 2022.3.x 亦可）
- **依赖**：无第三方依赖，只用 Unity 官方包（见 `RevolutionFrameWork_Unity/Packages/manifest.json`）
- **平台**：Windows / macOS（编辑器）· Android / iOS / WebGL（运行期无平台相关代码）

### 装到你自己工程（三选一）

框架本体 = `Assets/Revolution/`。**在你的工程根目录**执行：

| 方式 | 命令 | 落在哪 |
|---|---|---|
| ① **推荐** | `git clone -b package --depth 1 https://github.com/Yokino337088/Revolution.git Assets/Revolution` | `Assets/Revolution` |
| ② 子模块 | `git submodule add -b package https://github.com/Yokino337088/Revolution.git Assets/Revolution` | `Assets/Revolution` |
| ③ Package Manager | Add package from git URL → `https://github.com/Yokino337088/Revolution.git?path=/RevolutionFrameWork_Unity/Assets/Revolution` | `Packages/` |

  **示例工程（可选）**：17 个模块各配一个带详尽注释的 Demo 与可运行场景 ——
  `git clone -b demo --depth 1 https://github.com/Yokino337088/Revolution.git Assets/Revolution.Demo`（落在 `Assets/Revolution.Demo`）。

> **为什么推荐 ①/②**：Unity 的 `Resources.Load` 只保证加载**工程 `Assets` 下**的 `Resources` ——
> 框架自带 `ResMap.txt` 与两个 UI 预制体；另外两个**生成物**（`RevResPath.cs`、`RevSoundPath.cs`）必须写在框架目录里（包缓存只读）。
> 装在 `Packages/` 里也能跑（Canvas 用代码建、缺 ResMap 时编辑器直读），打包工具会**跳过生成并提示**。
>
> 更新：① `git -C Assets/Revolution pull`　② `git submodule update --remote Assets/Revolution`　③ Package Manager 选中包点 Update。
> `package` 分支由 GitHub Actions 自动同步（只同步 `Assets/Revolution/`）。

### 装完三步

1. 工程里准备**资源根目录**：新建 `Assets/GameRes`（或你已有的目录）
2. 菜单 `Revolution.Tools / 资源 / RevAB 打包工具` → **「打包」页签 → 资源目录** → 把「资源根目录」设为 `Assets/GameRes`
3. 开始写业务 👇

```csharp
RevSound.Play("ui_click");                                  // 播放音效（一行）
RevUI.Open<LoginPanel>();                                   // 打开面板
RevTimer.After(2f, () => RevUI.Close<LoadingPanel>());       // 2 秒后关掉
RevLog.Info("登录成功", "Login");                            // 打日志（分级 + 模块标签）
RevMono.AddUpdate(OnTick, owner: this);                     // 让纯 C# 类每帧跑一次
```

### 直接跑示例

**方式 A：只装示例**（配合上面已装的框架本体）：

```bash
git clone -b demo --depth 1 https://github.com/Yokino337088/Revolution.git Assets/Revolution.Demo
```

**方式 B：克隆整个仓库**：

```bash
git clone https://github.com/Yokino337088/Revolution.git
```

用 Unity 2022.3.15f1c1 打开 `RevolutionFrameWork_Unity/`（首次导入几分钟）。
`Assets/Revolution.Demo/` 下 **17 个模块各有一个可运行场景**：打开场景点 Play，
左侧按钮逐个点、右侧步骤日志讲清楚每一步（索引见 `Assets/Revolution.Demo/RevDemoSceneList.txt` 与该目录 README）。

---

## ⚠️ 不做什么：热更新与远程更新

**框架本体不自带热更新** —— 这是刻意的取舍，不是遗漏文档的缺口。但**资源热更已有官方扩展包**，用之前请先确认你要走哪条路。

| 能力 | 有没有 | 说明 |
|---|---|---|
| 资源**打包**（AB） | ✅ 有 | RevAB 打包工具：分包浏览（自动同步）/ Project 包名角标 / 依赖 / 体积 / 漏标检查 / 布局快照 |
| AB **本机**加载 | ✅ 有 | 从 `StreamingAssets` 读；编辑器直读 / AB 两条后端自动切换 |
| AB **远程下载** + 版本管理 + 差量更新 | ✅ **扩展包提供** | 官方扩展包 **RevHotUpdate**（`Assets/Revolution.HotUpdate`，分支 `hotupdate`）：清单驱动版本比对 / 差量下载 / 断点续传 / 多源降级 / 尺寸+SHA-256 校验 / 版本目录原子切换与回滚 / 首包落地（Android）/ URL 模式（小游戏）。**不装 = 与从前逐字节一致** |
| **代码热更新**（HybridCLR / ILRuntime / xLua） | ❌ **没有** | 全仓库 0 处相关代码（扩展包也不做） |
| 运行框架本体（19 个模块：资源 / UI / 序列 / 状态机 / 音效 / 计时器 / 日志 …） | ✅ 有 | 与热更**解耦**：热更接上之后这些模块照常工作，业务代码不用改 |

> 源码里就是这么写的（`Runtime/RevResourceSystem/Implementation/RevABLoader.cs` 头部注释原文）：
> 「① 从 `streamingAssetsPath` 加载 AB（**本框架不做热更新，没有 `persistentDataPath` 覆盖路径**）」。

### 为什么不做

- 热更不是资源层的一行开关：它牵涉**版本清单 / 下载器 / 断点续传 / 差分 / CDN / 灰度 / 失败回滚**，每个项目的要求都不一样；
- 框架的定位是"可读、可测、无魔法"：与其塞一套半成品 ✗，不如把**扩展点留干净** ——
  资源层对外只有 `IRevResPolicy` / `IRevResLoader` **两个接口**，接什么资源后端由你决定 ✓；
- 内核（路径 / 句柄 / 池 / 计时器 / 日志）刻意不依赖引擎，也**不关心"资源从哪来"** ——
  所以下面哪条路都不需要改上层业务代码 ✓；
- **代码热更仍然不做**（涉及 IL2CPP 与各平台审核红线，本框架不碰）；**资源热更**这一块已经由扩展包（方案 0）承担，
  另外两条路留给"想用自己的下载/CDN 体系"或"想整体换成 YooAsset"的项目。

### 方案 0（首选）：装官方扩展包 RevHotUpdate

本体只为它留了 **2 个默认 `null` 的钩子**（`RevABLoader.BundlePathResolver` = 包路径重定向、`RevResBootstrap.ResMapOverride` = 映射表覆盖），
其余全在扩展包里：清单 + 版本比对（大版本锚定）+ 下载器（并发 / 重试退避 / 多源 / Range 续传 / 磁盘预检）+ 校验（尺寸 + SHA-256）
+ 版本目录原子切换与回滚 + Android 首包落地 + 小游戏版本化 URL。

```text
git clone -b package   https://github.com/Yokino337088/Revolution.git Assets/Revolution
git clone -b hotupdate https://github.com/Yokino337088/Revolution.git Assets/Revolution.HotUpdate
```

文档：《使用说明》（手把手）·《架构解析》（设计论证与失败收场）·《技术方案》（可行性、平台核实、存储选型、决策与分期），
见 `Revolution.Document/热更新/`（网页版在文档站"扩展包"一节）。
想直接跑起来看：`Assets/Revolution.Demo/RevHotUpdate.Demo/`（一键"打包 + 清单 + 装配本地 CDN" → 起本地假 CDN → 面板点三步，全程不需要真云账号）。
发布侧配套工具：**Revolution.Could**（腾讯云 COS 热更发布 WPF 可视化工具，源码在仓库根 `Revolution.Could/`）与
`UploadToCos` / `UploadToOss` 命令行脚本（`Assets/Revolution.Demo/RevHotUpdate.Demo/`，给 CI 用）——
三者都按"内容先传、清单最后传"的顺序发布，详见 [`Revolution.Could/README.md`](Revolution.Could/README.md)。

> ★ 配对前提：扩展包用的两个钩子是本体后加的 —— **本体版本必须"不早于"引入钩子的那一版**，否则编译不过。
> 覆盖范围：**只做资源热更（AB）**；不做代码热更、加密、二进制差量、灰度。

### 方案 A：自己扩展资源层（改动最小）

**接热更 = 实现两个接口**：

| 扩展点 | 位置 | 你要做的 |
|---|---|---|
| `IRevResPolicy` | `Runtime/RevResourceSystem/Interfaces/IRevResPolicy.cs` | `Match(standardPath)` 判断这条路径归你管 · `MapPath(...)` 把逻辑路径换成你体系里的真实位置 · `CreateLoader()` 返回你的加载器 · `AllowFallback` 决定要不要让框架兜底 |
| `IRevResLoader` | `…/Interfaces/IRevResLoader.cs` | `Load(...)` / `LoadAsync(...)`：调你的下载/加载实现，把结果填进 `handle` —— **照抄 `RevABLoader` 即可，它就是同接口的现成范例** |
| 注册 | `RevResManager.RegisterPolicy(policy)` | 或在 `Runtime/RevResourceSystem/Support/RevResBootstrap.cs` 里替换掉 `RevABResPolicy` 那一行 |

配套要自己补的（框架不提供）：

- **版本清单**：打包时记录每个 AB 的 hash / CRC / 大小（现有产物清单 `BuildManifest.json` 已带版本号字段，可扩展）；
- **下载器**：`UnityWebRequest` + 并发控制 + 断点续传 + 失败重试 + 进度事件；
- **覆盖优先级**：`persistentDataPath` 下的更新包**优先于** `StreamingAssets` —— 加载前先看本地有没有更新过的包（热更的核心一步）；
- **校验与回滚**：下完校验 hash，坏了能退回上一版；
- 可选：差量（文件级 / bsdiff）、CDN 多域名、灰度开关。

> 优点：上层（`RevResManager` / 对象池 / UI / 表）**一行都不用改** ✓，RevAB 打包工具继续用 ✓。
> 代价：下载器与版本体系要自己写、自己维护 ✗（这部分最容易出线上事故 ✗）。

### 方案 B：接入 YooAsset 等第三方资源框架（要真上线的项目推荐）

**思路**：把「打包 + 版本 + 下载 + 更新」整块交给 YooAsset，只写一层**适配**把它接进本框架的资源层 —— 上层 19 个模块与全部业务代码不动 ✓。

步骤（本质就是方案 A，只是"你的实现"变成"调用 YooAsset"）：

1. 写 `YooAssetResPolicy : IRevResPolicy`：`Match` 认你的逻辑路径前缀；`MapPath` 转成 YooAsset 的 **location**（例如 `"ui/bag/icon_sword"`）；`CreateLoader` 返回 `YooAssetResLoader`；
2. 写 `YooAssetResLoader : IRevResLoader`：`LoadAsync` 调 `package.LoadAssetAsync<T>(location)`，完成回调里填 `handle`；需要同步时用 `LoadAssetSync<T>`；
3. 启动流程里初始化 YooAsset：初始化 package → 请求版本 → 更新清单 → 创建下载器 → 下载 → 清理无用缓存（这几步按 YooAsset 官方示例来即可）；
4. `RevResManager.RegisterPolicy(new YooAssetResPolicy(...))`（或在 `RevResBootstrap` 里换掉 `RevABResPolicy`）。

迁移时要注意的：

| 项 | 处理方式 |
|---|---|
| RevAB 打包工具 | 不再使用（YooAsset 有自己的收集器与构建器）；「分包 / 依赖 / 体积 / 漏标 / 布局快照」这些**查看能力**会一起失去 —— 想保留就把这些视图接到 YooAsset 的 collector 数据上 |
| `ResMap.txt` | 不再需要（YooAsset 用 location + 收集器） |
| `RevResPath.cs`（生成的路径常量） | 可以保留当业务侧常量表，但要自己维护一份和 location 命名规则一致的生成逻辑 |
| 资源分组 `RevResGroup` | 映射成 YooAsset 的**资源标签 / 收集器分组** |
| UI / 表 / 音效 / 对象池 | 都通过资源系统加载 → **不用改** ✓ |

同类可选：**Addressables**（Unity 官方，功能最全，构建与调试成本更高）· **YooAsset**（轻、中文文档全、社区活跃）· **自研**（回到方案 A）。

> 也可以**混用**：真机走 YooAsset 热更，编辑器里仍然走框架的 `RevEditorResPolicy` 直读（`Match` 按"编辑器 / 真机"分流即可），开发体验不变 ✓。

### 方案 C：代码热更新（与 A / B 正交）

资源热更 ≠ 代码热更：前者换资源，后者换**程序集**。两者要配套发布（版本对齐）。

- **HybridCLR**（推荐，Unity 2022.3 可用）：补充元数据 + 热更程序集；接入后本框架**无需改动**（它只是普通程序集）；
  ⚠️ 本框架泛型使用较多（`RevTask` / 对象池 / 事件 等），务必按官方指引跑一遍 **AOT 泛型收集**（`link.xml` / AOTGenericReferences）再上真机；
- **ILRuntime / xLua**：虚拟机 + 桥接方案，性能与开发体验代价更大（框架里大量泛型 API 会变成桥接开销）；
- 顺序建议：**先跑通资源热更（A 或 B），再做代码热更** —— 代码热更的坑更难查。

### 一句话决策

| 你的情况 | 建议 |
|---|---|
| 单机 / Demo / 内部工具 | 什么都不用做，直接用（本机 AB 或编辑器直读） |
| 要做资源热更、又不想自己写下载器 | **方案 B（YooAsset）** |
| 已有自己的下载 / CDN / 版本体系 | **方案 A**（实现两个接口，上层零改动） |
| 还要改逻辑代码不发版 | **方案 C（HybridCLR）** ＋ A 或 B |

---

## 🆕 最近更新（2026-10-08）

- **热更框架稳定性大修**：多轮审查修复 WebGL 同会话二次热更映射表残留、清单下载聚合器并发互踩等严重缺陷（明细见 `Revolution.Document/bug修复日志/`）。
- **热更清单窗口改版**：按"环境 → 配置 → 生成/自检 → 上传"流程分区，新增大版本与 Player Settings 一致性校验（不一致会触发全量强更，窗口内一键同步）、资源版本一键 +1、上传顺序可视化清单。
- **新增云发布工具链**：`Revolution.Could/` —— 腾讯云 COS 热更发布 **WPF 可视化工具**（分步引导、SHA-256 预检、内容先传清单最后传、同版本路径内容不同拒绝覆盖，附完整中文使用说明）；另配 `UploadToCos` / `UploadToOss` 命令行脚本（CI 友好）。用法见 [`Revolution.Could/README.md`](Revolution.Could/README.md) 与《[RevHotUpdate 使用说明](Revolution.Document/热更新/RevHotUpdate使用说明.md)》第五章。

## 🆕 最近更新（2026-09-30）

- **稳定性大扫除**：计时器 / 服务定位器 / 公共 Mono / 场景 / 输入 / UI / 日志 七个模块合计 25+ 处缺陷修复，
  每处都带复现断言与成因注释 —— 明细见 `Assets/Revolution/CHANGELOG.md` 的「未发布」段。
- **导表工具新增「仅生成数据」导出模式**（编辑器版 + WPF 版）：只改数值不动表结构时只重写数据 txt，不再触发脚本编译。
- **配置表装载改名 DataLoad → RevDataLoad**：目录与全部类型加 Rev 前缀，与框架命名风格统一（导表模板已同步，重新导出即可）。
- **新增示例工程**：`Assets/Revolution.Demo/` —— 17 个模块各配一个带详尽注释的 Demo 与可运行场景，
  由工作流自动发布到 demo 分支（与 package 分支同机制）。

## 🧩 核心模块

> 完整清单见 [模块一览](#模块一览)；每个模块的目录里都有一份"3 分钟上手"的 `README.md`。

### 📦 资源加载（RevResourceSystem）

- ✅ **两条后端**：编辑器直读（不打包直接跑）＋ AssetBundle（`ResMap.txt` 逻辑路径 → 真实路径）
- ✅ **只走异步**：不为"看起来同步"的 API 付代价，不会出现"同步命中正在加载的句柄 → 拿到空内容"
- ✅ **引用计数 + 自动卸载**，失败带**原因枚举**（`handle.ErrorReason`）

### 🎨 UI 系统（RevUISystem）

- ✅ **声明式配置**：`[RevUIPanel(root, layer)]` 一个特性搞定层级与根节点
- ✅ **控件事件三种接法**：九种事件（点击 / 长按 / 松开 / Toggle / Slider / 输入框 / 结束编辑 / Dropdown / 滚动）都能用**方法特性**一行接上（`[RevButtonClick("btnStart")]` · `[RevToggleChanged("tglSound")]` · `[RevScrollChanged("scrollList")]` …）；也可重写 `OnClick(节点名)` 这类回调集中处理，或 `[RevBind]` 字段 + 自己挂监听（最灵活）
- ✅ **内置 UI 动画库**（不依赖 DOTween）：面板 / Part 一行预设 `ShowAnimation => RevUIAnimPreset.PopIn`（显示隐藏动画**播完才回调**）；控件一行 `RevUIAnim.FadeIn / SlideIn / ScaleTo / Breathe / AddHoverFeedback`；引擎走采样模型 + 帧余量结转（掉帧不改变动画总时长），600 帧稳态零 GC
- ✅ **Canvas 架构按需选择**：**主推单 Canvas（默认、零配置）**；只有目标设备的 Profiler 证明 UI 合批成为瓶颈、常规优化仍不达标，才启用常用 / 静态 / 动态三 Canvas。并非有动态 UI 就必须拆，详见 [架构解析 · 决策 11](Revolution.Document/UI系统/UI系统架构解析.md)
- ✅ **与对象池联动**：关闭即回收，重复打开不重建
- ✅ **纯代码路径可用**：没有 `Resources` 时 Canvas 用代码建（降级不崩）

### 🎬 动作序列（RevActionSequence）

- ✅ **链式 DSL** 表达"播放 → 等待 → 并行 → 嵌套"：

  ```csharp
  RevSequenceDefinition openChest = RevSequence.Create("开宝箱")
      .Do("播放开箱动画", ctx => Anim.Play("open"))
      .Wait(0.5f)
      .Parallel("一起飞", p => p.Do("飘字", ctx => Tip.Show("+10 钻石")))
      .OnCompleted(run => RefreshBag())
      .Build();                       // 建议加载期构建一次并缓存，运行期播放零构建成本
  ```
- ✅ **取消收尾契约**：中途取消也保证收尾（不会留下半截动画），`OnCancel` / `OnCompleted` / `OnCancelled` 三个回调齐
- ✅ **步骤库可扩展**：`Implementation/Steps/` 里 6 个现成步骤（等待 / 委托 / 并发 / 嵌套 / 发布 / 等待任务）

### 🎮 状态机（RevStateMachine）

- ✅ **两种形态**：轻量流程（GameFlow，进登录/进战斗/回大厅）＋ 重量级 AI（分层状态机）
- ✅ 状态切换有明确的生命周期回调与日志出口

### 🔊 音效系统（RevSoundSystem）

- ✅ **一行播放**：`RevSound.Play("ui_click")`（2D）/ `RevSound.PlayAt("爆炸", pos)`（3D）
- ✅ **BGM 与音量总线**：BGM 淡入淡出、总音量/分组音量
- ✅ **作用域**：`using (var s = RevSound.OpenScope()) { ... }` 出块全停
- ✅ 音效表 + 路径常量由工具生成（写错编译不过）

### ⏱ 计时器（RevTimer）

- ✅ **一行创建**：`RevTimer.After(2f, cb)` / `Every(1f, cb, times: 10)` / `NextFrame(cb)` / `At(服务器时刻, cb)`
- ✅ **四种时间域**：受 timeScale 影响 / 不受影响 / 逻辑帧 / **服务器绝对时刻**（切后台、改设备时间都不怕）
- ✅ **句柄可读**：`h.Left`（剩余秒）/ `h.Progress`（0~1）直接喂倒计时 UI；过期句柄所有操作都是安全空操作
- ✅ **一行防泄漏**：`owner: this` + `RevTimer.CancelAllOf(this)`，或 `using (RevTimer.OpenScope())`
- ✅ 数值实测：**1000 个计时器每帧 0.0139ms**；1 秒循环跑 10 分钟 = 恰好 600 次（无漂移）

### ⌨️ 输入系统（RevInput）

- ✅ **动作名绑定**：业务只写 `RevInput.Pressed("Jump")`，键位/鼠标/触屏/手柄怎么绑由绑定表决定（`RevInput.Bind(...)`，或从存档 `LoadBindings` 读）
- ✅ **键鼠 + 触屏 + 手柄全覆盖**：三套输入压成同一份快照，判定只有一份逻辑；设备类型自动识别
- ✅ **7 种手势**：点击 / 双击 / 长按 / 拖动 / **八向滑动（带速度）** / 双指捏合 / 双指旋转，阈值全可调
- ✅ **屏蔽栈（精确到指针）**：`using (var s = RevInput.OpenScope()) { s.Block(); }` —— 弹窗挡世界输入、ESC 照常可用；屏蔽期间**轴读数一并归零**（角色不会在弹窗里继续移动）、**仍记按下时间**，解除瞬间缓冲输入立刻生效
- ✅ **手感补偿**：输入缓冲窗口 `PressedBuffered(action, 0.15s)`、连发节拍 `SetRepeat`、死区 `SetDeadzone`
- ✅ **切后台自动复位**：失焦/切后台清按键与手势（"切回来角色一直跑"的解药）
- ✅ **事件驱动接入**：`RevInput.AddListener(this)` 登记一次，按下 / 抬起 / 连发 / 轴变化 / 手势全部由框架推给你 —— **业务里不用写 Update 轮询**（轮询 API 仍保留，问"此刻状态"时用）
- ✅ **可断言 + 零分配**：Core/Facade 零引擎依赖，**86 条断言工程外跑通**（含 600 帧零分配）

### 📝 日志（RevLog）

- ✅ **级别就是成本契约**：`RevLog.Debug` 正式包**编译期删除**（连字符串拼接都不发生）
- ✅ **模块标签 + 静音**：`RevLog.Warn("msg", "Network")` / `RevLog.MuteTag("Network")`
- ✅ **刷屏抑制**：连续相同的日志只留首条 + 一条"重复 N 次"汇总（10 万条 0 分配）
- ✅ **出事前的现场**：环形缓冲常驻最近 2048 条，`RevLog.Dump(200)` 直接复制
- ✅ **异步落盘 + 上报可插拔**：`EnableFileLog(目录)` / `OnReport += ...`（全框架出口已统一到这里）

### 🧵 公共 Mono 模块（RevPublicMono）

- ✅ **让纯 C# 类也能每帧跑**：`RevMono.AddUpdate / AddLateUpdate / AddFixedUpdate`
- ✅ **协程宿主**：`RevMono.StartCoroutine(MyRoutine())`（含异常隔离，不再静默中断）
- ✅ 去重 / 上限 / owner 与作用域清理（避免"忘记移除 → 每帧还在跑"）

### 📣 事件系统（RevEventSystem）

- ✅ 强类型事件 + **订阅句柄**（取消订阅不用记委托）
- ✅ 派发期间增删订阅安全的语义（写在文档里，可依赖）

### 🕹 GM 指令（RevGMCommand）

- ✅ **一行注册**：给方法加个特性就行 —— `[RevGMEntry("给道具：give 1001 5")] static void Give(int id, int count)`
- ✅ 游戏内控制台 + 编辑器面板共用同一套指令

### 📊 配置表（RevDataLoad）

- ✅ **表 = 资源**：走资源系统加载，和 AB 体系天然一致
- ✅ 按类型取表，表结构由导表工具生成

### 🧭 服务定位器（RevServiceLocator）

- ✅ **把业务依赖挡在框架之外**：`RevServiceLocator.Create().AddSingleton<IAudioService, AudioService>().Build()`
- ✅ **三档取服务**：`GetRequired<T>()`（"必须有"，忘了注册启动期就炸出来）/ `Get<T>()`（可选能力，没有返回 null）/ `TryGet<T>()`（用在 `if` 里最顺）
- ✅ 生命周期可控（`RevIServiceInit` / `RevITickable`）

### 📦 对象池（RevObjectPool）

- ✅ GameObject 池 + **纯 C# 对象池**（`RevPoolCore<T>`）
- ✅ 与资源加载打通（池端着资源引用，不会被卸载掉）

### ⚡ 异步任务（RevTask）

- ✅ **`await` 一帧 / 等 N 毫秒 / 等一组**：`await RevTask.Yield();` · `await RevTask.Delay(500);` · `await RevTask.WhenAll(a, b);`
- ✅ **等资源加载完成**：`await asyncOperation;`（`AsyncOperation` 扩展方法）
- ✅ 不依赖 UniTask，零第三方依赖

### 🧱 单例基类（RevSingleton）

- ✅ 纯 C#（`RevSingleton<T>`）/ 自己摆（`RevSingletonMono<T>`）/ 自动创建（`RevSingletonAutoMono<T>`）
- ✅ 重复实例告警、销毁自动清引用、子类无法覆盖 `Awake` 漏掉赋值

### 模块一览

**运行期**（`Assets/Revolution/Runtime/`，19 个模块 / 184 个 `.cs` / 30,258 行）

| 模块 | 一句话 | 规模 |
|---|---|---|
| `RevResourceSystem` | 资源加载：编辑器直读 + AB 两条后端、引用计数、自动卸载、失败原因可查；URL 型平台（小游戏）带失败重试 / 超时兜底 / 引擎缓存标识（hash·crc） | 18 / 3,139 |
| `RevScene` | 场景加载：一行切换（await 或挂事件）、进度永远 0~1（不卡 90%）、最短展示时长、切前按约定清理（默认清对象池） | 5 / 541 |
| `RevInput` | 输入：事件驱动接入（监听者 / 轴 / 连发事件）/ 动作名绑定 / 键鼠触屏手柄 / 7 种手势 / 屏蔽栈 / 可断言内核 | 15 / 3,733 |
| `RevUISystem` | UI：面板声明式配置 / 层级 / 控件事件三种接法（九个方法特性 · 节点名分发 · 字段绑定）/ **优先单 Canvas、性能瓶颈时按需启用三 Canvas** / 内置 UI 动画库（预设一行加动效 · 不依赖 DOTween）/ 与资源池联动 | 26 / 6,110 |
| `RevActionSequence` | 动作序列：一行 DSL 表达"播放 → 等待 → 并行 → 嵌套"，含取消收尾契约 | 24 / 3,145 |
| `RevStateMachine` | 状态机：轻量流程 / 重量级 AI 两种形态 | 10 / 1,598 |
| `RevSoundSystem` | 音效：一行播放、BGM、音量总线、作用域、音效表；小游戏 / WebGL 的音频约束查 `RevSoundPlatform` | 11 / 1,718 |
| `RevAppLifecycle` | 应用生命周期：前后台 / 焦点 / 退出事件（零配置自动就位；Pause 与 Focus 双触发自动去重，后台时长可查） | 2 / 175 |
| `RevStripGuard` | 代码裁剪自检：进游戏自动确认"物理 / UI / 动画这些关键类型还在不在"，被裁就报出【缺什么 + 什么症状 + 怎么修】 | 1 / 128 |
| `RevTimer` | 计时器：四时间域、句柄代际、作用域、秒表 | 10 / 1,440 |
| `RevLog` | 日志：分级（Debug 编译期零成本）、tag 静默、重复抑制、环形缓冲、异步落盘、上报；WebGL / 小游戏自动禁用文件通道 | 8 / 1,117 |
| `RevPublicMono` | 公共 Mono：给纯 C# 类每帧回调与协程宿主 | 6 / 682 |
| `RevEventSystem` | 事件：强类型事件 + 订阅句柄 | 5 / 1,167 |
| `RevGMCommand` | GM 指令：一行注册，游戏内控制台 | 12 / 1,077 |
| `RevObjectPool` | 对象池：GameObject 池 + 纯 C# 对象池 | 10 / 2,073 |
| `RevDataLoad` | 配置表：表 = 资源，按类型取表 | 6 / 758 |
| `RevServiceLocator` | 服务定位器：把业务依赖挡在框架之外 | 8 / 738 |
| `RevTask` | 异步：`await` 一帧 / 等资源加载完成 | 4 / 652 |
| `RevSingleton` | 单例基类三件套（尽量少用） | 3 / 267 |

**编辑器**（`Assets/Revolution/Editor/`，3 套工具 + 平台辅助 / 43 个 `.cs` / 12,738 行）

| 工具 | 一句话 | 规模 |
|---|---|---|
| `RevResourceSystem` | **RevAB 打包工具**（原名 LiteAB，对标 AssetBundle Browser）：包树 + 资源表（拖拽建包 / 换包、F2 改名、Delete、右键）/ 目标平台可选（小游戏目标自动归一到 WebGL 目录）/ Project 窗口包名角标 / 依赖 / 体积 / 漏标检查（一键处理）/ 布局快照对比；生成 `ResMap.txt` 与路径常量 | 27 / 7,095 |
| `RevExcelTool` | **导表工具（编辑器版）**：拖入 Excel → 预览 / 逐行校验 → 一键导出代码 + 数据；Excel 开着也能读、改表自动重读、只写有变化的文件、新增表自动生成映射；生成物与 WPF 版逐字节一致；带 CI 入口 | 12 / 4,407 |
| `RevGMCommand` | GM 指令的编辑器面板 | 2 / 856 |
| `RevPlatform` | **平台辅助工具**：IL2CPP 裁剪保护生成（`link.xml`，防 UI 反射绑定被裁）、首包体积审计（扫 `Resources` 目录，小游戏首包预算用） | 2 / 380 |

---

## 📁 项目结构

```text
Revolution/
├── RevolutionFrameWork_Unity/            Unity 工程
│   ├── Assets/Revolution/                  ★ 框架本体（包分支的仓库根就是它）
│   │   ├── Runtime/                            19 个运行期模块（RevScene / RevResourceSystem / RevUISystem / RevInput / RevAppLifecycle / RevStripGuard / …）
│   │   ├── Editor/                             编辑器工具（RevAB 打包 / GM 面板）
│   │   ├── Generation/                         生成的路径常量（RevResPath.cs）
│   │   ├── Resources/                          框架自带运行时资源（ResMap、UI 预制体）
│   │   ├── package.json / README.md            包描述与包内说明（UPM 用）
  │   ├── Assets/Revolution.Demo/             示例工程（17 个模块：Demo + 可运行场景，demo 分支的仓库根就是它）
│   ├── Assets/Scenes/                      示例场景（SampleScene）
│   ├── Assets/GameRes/                     资源根目录（放你的资源，RevAB 从这里扫）
│   └── Packages/ · ProjectSettings/        Unity 工程配置
├── Revolution.Document/                  设计文档（每个模块：使用说明 + 架构解析）
├── Skills/                              给 AI Agent 用的技能包（模块速查 / 规范 / 坑位 / 模板）
├── Revolution.ExcelTool/                 导表工具（WPF：Excel → C# 类 + 数据文件）
├── Revolution.Could/                     腾讯云 COS 热更发布工具（WPF：选产物 → 校验 → 按安全顺序上传）
└── .github/workflows/                    CI（自动同步 package / demo / hotupdate / skill 分支）
```

> 仓库只收录**必要的代码与文档**：`Library/` `Temp/` `Logs/`、构建产物、IDE 工程文件（`*.csproj` `*.sln`）、
> 本机打包配置都不入库 —— 规则见 [`.gitignore`](.gitignore)。

---

## 💻 系统要求

- **Unity**：2022.3.15f1c1（推荐）· 2022.3.x 系列可用
- **平台支持**：Windows · macOS · Android · iOS · WebGL
- **开发环境**：.NET Standard 2.1（Unity 内置）· Visual Studio 2022 / Rider / VS Code
- **第三方依赖**：**无**（导表工具是独立 WPF 工程，可选）
- **内核验证环境**（可选）：.NET 8 SDK —— 用来跑纯 C# 断言，不装也能正常用框架

---

## 🚫 出包设置：建议关闭代码裁剪

**主推建议：把 `Project Settings → Player → Other Settings → Managed Stripping Level` 改成 `Disabled`（或至少 `Minimal`），然后重新出包。**

**省的是什么**：包体 —— 但对**安卓原包**来说，体积大头是贴图 / 音频 / 模型，代码占比并不高。

**赔的是什么**：**"编辑器好好地、真机突然瘸"**，而且**不报错** —— 刚体不响应重力、`Physics.Raycast` 恒返回 false、面板空白、动画不动、没声音。这种故障十有八九想不到是裁剪干的，等发现时往往已经发版。

关键在于：业务里有三类代码 `link.xml` **天然覆盖不全**：

1. **反射** —— UI 的 `[RevBind]` 字段、事件方法、`[RevUIPanel]` 特性；
2. **泛型** —— `RevUIPanel<TData>` / `RevSingleton<T>` 这类；
3. **场景里挂的原生组件** —— 刚体 / 碰撞体 / Animator 的真实现 native，托管 wrapper 会被一起裁掉，而场景里那半边还好好挂着。

所以护栏只能"**降低故障概率**"，不构成"可以放心开裁剪"的许可证。

> 关掉裁剪的代价**只有包体**，而这个数字 **3 分钟就能自己测准**：保持当前设置出一包 → 改成 `Disabled` 再出一包 → **差值就是你的真实代价**。

框架仍配了三层护栏，**万一确实要开裁剪**时用：框架 `link.xml` 固定保住物理 / UI / 动画 / 音频 / 粒子 · 菜单一键生成业务侧 `link.xml` · `RevStripGuard` 进游戏自动自检并报人话。

**完整说明（含代价实测方法、症状反查表）→ [《代码裁剪 · 使用说明》](Revolution.Document/代码裁剪/代码裁剪使用说明.md)**

---

## 📚 文档导航

> 🌐 **在线文档站**：<https://yokino337088.github.io/Revolution/> —— 一页看全所有模块，带分组、搜索与阅读顺序建议。
> 💻 **本地预览**：双击 [`Revolution.Document/预览文档.cmd`](Revolution.Document/预览文档.cmd)（或直接双击 `index.html`）；
> 文档站由 [`pages.yml`](.github/workflows/pages.yml) 自动发布，改动 `Revolution.Document/` 即重新发布。
>
> 🤖 **给 AI Agent 用的技能包**：[`Skills/revolution-framework/`](Skills/revolution-framework/SKILL.md) —— 模块门面速查 / 代码规范 / 20 条坑位 / 审查清单 / 模板与工程外断言工程；
> 一条命令装进任何支持 skills 的工具（工具有 `skill` 生成分支，改动 `Skills/` 会自动重建）：
>
> ```text
> git clone -b skill https://github.com/Yokino337088/Revolution.git .codebuddy/skills
> # → .codebuddy/skills/revolution-framework/SKILL.md（换工具就换目录：.claude/skills 等）
> ```

**每个模块都有《使用说明》（手把手，照着做就能跑通）**；带 ✅ 的还有《架构解析》（设计论证：为什么这么设计、与别的方案差在哪）。

下表每行两个入口，点一下直接跳：

- **网页** → 打开 [在线文档站](https://yokino337088.github.io/Revolution/) 里对应的那一页（带排版、目录、站内搜索；手机上也好看）
- **md** → 仓库里的 Markdown 版本（GitHub 直接渲染，便于 diff 与离线阅读）

| 模块 | 《使用说明》手把手 | 《架构解析》设计论证 |
|---|---|---|
| 📝 日志系统 | [网页](https://yokino337088.github.io/Revolution/日志系统/日志系统使用说明.html) · [md](Revolution.Document/日志系统/日志系统使用说明.md) | — |
| ⏱ 计时器系统 | [网页](https://yokino337088.github.io/Revolution/计时器系统/计时器系统使用说明.html) · [md](Revolution.Document/计时器系统/计时器系统使用说明.md) | — |
| 🧵 公共Mono模块 | [网页](https://yokino337088.github.io/Revolution/公共Mono模块/公共Mono模块使用说明.html) · [md](Revolution.Document/公共Mono模块/公共Mono模块使用说明.md) | — |
| 📦 资源加载系统 | [网页](https://yokino337088.github.io/Revolution/资源加载系统/资源加载系统使用说明.html) · [md](Revolution.Document/资源加载系统/资源加载系统使用说明.md) | [网页](https://yokino337088.github.io/Revolution/资源加载系统/资源加载系统架构解析.html) · [md](Revolution.Document/资源加载系统/资源加载系统架构解析.md) |
| ⌨️ 输入系统 | [网页](https://yokino337088.github.io/Revolution/输入系统/输入系统使用说明.html) · [md](Revolution.Document/输入系统/输入系统使用说明.md) | [网页](https://yokino337088.github.io/Revolution/输入系统/输入系统架构解析.html) · [md](Revolution.Document/输入系统/输入系统架构解析.md) |
| 🎨 UI系统 | [网页](https://yokino337088.github.io/Revolution/UI系统/UI系统使用说明.html) · [md](Revolution.Document/UI系统/UI系统使用说明.md) | [网页](https://yokino337088.github.io/Revolution/UI系统/UI系统架构解析.html) · [md](Revolution.Document/UI系统/UI系统架构解析.md) |
| 🎬 动作序列 | [网页](https://yokino337088.github.io/Revolution/动作序列/动作序列使用说明.html) · [md](Revolution.Document/动作序列/动作序列使用说明.md) | [网页](https://yokino337088.github.io/Revolution/动作序列/动作序列架构解析.html) · [md](Revolution.Document/动作序列/动作序列架构解析.md) |
| 🎮 状态机 | [网页](https://yokino337088.github.io/Revolution/状态机/状态机使用说明.html) · [md](Revolution.Document/状态机/状态机使用说明.md) | [网页](https://yokino337088.github.io/Revolution/状态机/状态机架构解析.html) · [md](Revolution.Document/状态机/状态机架构解析.md) · [网页](https://yokino337088.github.io/Revolution/状态机/状态机Demo示例讲解.html) · [md](Revolution.Document/状态机/状态机Demo示例讲解.md) |
| 🔊 音效系统 | [网页](https://yokino337088.github.io/Revolution/音效系统/音效系统使用说明.html) · [md](Revolution.Document/音效系统/音效系统使用说明.md) | — |
| 📣 事件系统 | [网页](https://yokino337088.github.io/Revolution/事件系统/事件系统使用说明.html) · [md](Revolution.Document/事件系统/事件系统使用说明.md) | [网页](https://yokino337088.github.io/Revolution/事件系统/事件系统架构解析.html) · [md](Revolution.Document/事件系统/事件系统架构解析.md) |
| 📦 对象池 | [网页](https://yokino337088.github.io/Revolution/对象池/对象池使用说明.html) · [md](Revolution.Document/对象池/对象池使用说明.md) | [网页](https://yokino337088.github.io/Revolution/对象池/对象池架构解析.html) · [md](Revolution.Document/对象池/对象池架构解析.md) |
| 🧭 服务定位器 | [网页](https://yokino337088.github.io/Revolution/服务定位器/服务定位器使用说明.html) · [md](Revolution.Document/服务定位器/服务定位器使用说明.md) | [网页](https://yokino337088.github.io/Revolution/服务定位器/依赖注入vs服务定位器.html) |
| 🕹 GM指令 | [网页](https://yokino337088.github.io/Revolution/GM指令/GM指令使用说明.html) · [md](Revolution.Document/GM指令/GM指令使用说明.md) | — |
| 📊 导表工具 | [网页](https://yokino337088.github.io/Revolution/导表工具/导表工具使用说明.html) · [md](Revolution.Document/导表工具/导表工具使用说明.md) | [网页](https://yokino337088.github.io/Revolution/导表工具/导表工具架构解析.html) · [md](Revolution.Document/导表工具/导表工具架构解析.md) · [网页](https://yokino337088.github.io/Revolution/导表工具/导表工具对比与设计说明.html) · [md](Revolution.Document/导表工具/导表工具对比与设计说明.md) |

> 📂 全部文档的文件清单与命名规则见 [`Revolution.Document/README.md`](Revolution.Document/README.md)。

### 代码里的"3 分钟上手"

`Assets/Revolution/Runtime/` 下这些模块自带 `README.md`：
`RevSoundSystem` · `RevActionSequence` · `RevGMCommand` · `RevTimer` · `RevInput` · `RevLog` · `RevPublicMono`

---

## 🧪 工程外验证（纯 C# 跑断言）

框架里有一批**不引用 `UnityEngine`** 的文件，可以把它们链接进普通 .NET 控制台工程直接跑断言：

```text
RevResourceSystem/Core/RevResPathUtil.cs            路径拼接 + 缓存键（含"两段键 == 完整路径键"不变量）
RevTimer/Core/RevTimerTable.cs                   计时器槽位表（代际号 / 延迟复用 / 句柄校验）
RevTimer/Core/RevServerClock.cs                  服务器时间（一次校准 + 本地 realtime 外推）
RevSoundSystem/Core/RevSoundVoiceTable.cs        声音槽位表（代际号 / 同帧去重 / 上限淘汰）
RevObjectPool/Core/RevPoolCore.cs                池引擎（重复归还拦截）
RevLog/Core/RevLogRing.cs                        日志环形缓冲（定长 / 零分配写入）
RevLog/Implementation/RevLogCore.cs              日志内核（过滤 / 重复抑制 / 通道隔离）
RevPublicMono/Implementation/RevMonoCore.cs      监听列表内核（去重 / 上限 / 快照派发 / 异常隔离）
Editor/RevResourceSystem/ABTool/Snapshot/ABLayoutSnapshot.cs   AB 布局快照差异（改名 / 换包 / 增删识别）
```

已验证的部分（都是跑出来的数字，不是估算）：

| 模块 | 断言 | 实测 |
|---|---|---|
| RevTimer | 55 / 55 | 1000 个计时器每帧 **0.0139ms**；10 万次创建+停止 <4KB |
| RevLog | 41 / 41 | 10 万条日志 **0 分配**；环形缓冲硬顶 2048 条 |
| RevPublicMono | 25 / 25 | 10 万次派发 **32B** |
| ABTool 布局快照 | 24 / 24 | 改名 / 换包 / 增删识别 |

---

## 🎯 设计取向

1. **内核尽量不依赖引擎**。纯 C# 部分可脱离 Unity 编译与断言；引擎相关收敛在适配层（`Support/`、`*Driver`/`*Player` 组件）。
2. **不静默**。框架不吞错误：失败一律给原因枚举，异常有统一出口（`RevLog`）。
3. **只走异步加载**。不为"看起来同步"的 API 付代价。
4. **能用生成代码就不手写常量**。目录前缀生成成 `const`，写错编译不过。
5. **默认值取安全侧**。不静默卸载、不停播、不静音；破坏性行为必须显式传参。
6. **门面只留一个**。每个模块一个入口文件，其余按"要不要读"分层（`Core` / `Facade` / `Implementation` / `Interfaces` / `Support`），小白只需读第一个。

---

## 💡 为什么用 Revolution？

### 1. 看得懂

每个模块一个门面、每个文件开头写清"为什么这么写"；`Core` / `Facade` / `Implementation` / `Interfaces` / `Support` 五层词汇表统一，
不需要先读完两万行才知道从哪下手。

### 2. 敢改

没有隐藏的反射魔法、没有第三方依赖、没有"只有作者知道"的约定；
编辑器工具（RevAB / 导表）源码都在仓库里，改起来没有黑盒。

### 3. 有证据

内核能脱离 Unity 跑断言 —— 误差契约、句柄失效、异常隔离、零分配都不是"设计文档说的"，
而是**跑出来的**（145 条）。性能同样有数字（每帧 0.0139ms / 0 分配 / <4KB）。

### 4. 不埋雷

句柄带代际校验（过期句柄不会误伤新对象）、循环计时器有 `owner` 与作用域两套清理、
逐回调异常隔离、进 Play 自动清静态残留 —— 这些"上线才会痛"的点都在框架层堵住了。

---

## 🎮 示例项目

| 示例 | 位置 |
|---|---|
| 动作序列 / GM 指令 / 状态机 | `RevolutionFrameWork_Unity/Assets/Revolution.Demo/` |
| 导表 Demo（Excel + 生成的代码与数据） | `Revolution.Demo/ExcelTool/` |
| 状态机示例源码（Boss AI / 游戏流程 / UI 栈） | `Revolution.Demo/Unity/RevStateMachine/` |

---

## 🤝 贡献与支持

欢迎提交 [Issue](https://github.com/Yokino337088/Revolution/issues) 和 Pull Request！

> 提交代码前请留意两点：
> ① **`.meta` 文件必须一起提交**（里面存的是资产的 GUID 与导入设置，缺了会让引用断裂、设置回默认值，甚至丢分包标记）；
> ② 新增模块请沿用现有目录词汇（`Core` / `Facade` / `Implementation` / `Interfaces` / `Support`）与文件头注释格式。

---

**Made with ❤️ by Revolution**

[⭐ Star](https://github.com/Yokino337088/Revolution) | [🐛 Issues](https://github.com/Yokino337088/Revolution/issues) | [📖 文档](Revolution.Document) | [MIT License](LICENSE)
