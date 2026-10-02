# 文档地图（权威文档在哪）

> **文档是权威，本 skill 只是索引。** 写代码前先读对应模块的《使用说明》；要动设计先读《架构解析》。
> 文档根目录：`<仓库根>/Revolution.Document/`；在线站：https://yokino337088.github.io/Revolution/

## 1. 文档结构

```text
Revolution.Document/
├── index.html                 ← 文档站首页（带搜索；改文档后同步这里才会被站点收录）
├── README.md                  ← 文档目录（模块表 + 扩展包表）
├── 预览文档.cmd               ← 本地预览（等价在线站）
├── <模块>/
│   ├── <模块>使用说明.md / .html        ← 手把手：怎么用、边界、FAQ
│   └── <模块>架构解析.md / .html        ← 设计论证：为什么这么写、失败怎么收场
└── assets/                    ← 站点样式资源
```

- **md 与 html 都要改**（md 是权威版，html 是站点用的那版）；改完记得看 `index.html` 是否需要收录新页面。
- 文档写作口径：`> [!NOTE]` / `> [!WARNING]` 这类标注在 md 里保留；表格、代码块、目录锚点与现有文档一致。

## 2. 各模块文档（按模块找）

| 模块（门面） | 文档 |
|---|---|
| 日志 `RevLog` | `日志系统/日志系统使用说明.md`（另有模块 README：`Runtime/RevLog/README.md`） |
| 计时器 `RevTimer` | `计时器系统/计时器系统使用说明.md` · README：`Runtime/RevTimer/README.md` |
| 公共 Mono `RevMono` | `公共Mono模块/公共Mono模块使用说明.md` |
| 对象池 `RevPool` / `RevRefPool` | `对象池/对象池使用说明.md`、`对象池/对象池架构解析.md` |
| 事件 `RevEvent` | `事件系统/事件系统使用说明.md`、`事件系统/事件系统架构解析.md` |
| 服务定位器 `RevServiceLocator` | `服务定位器/服务定位器使用说明.md` · README：`Runtime/RevServiceLocator/README.md` |
| 异步 `RevTask` | **暂无专章**（看 `RevTask.Demo` 场景 + 源码注释 + 本 skill 的 `modules-core.md`） |
| 场景 `RevScene` | `场景系统/场景系统使用说明.md`、`场景系统/场景系统架构解析.md` |
| 输入 `RevInput` | `输入系统/输入系统使用说明.md`、`输入系统/输入系统架构解析.md` · README：`Runtime/RevInput/README.md` |
| 音效 `RevSound` | `音效系统/音效系统使用说明.md` · README：`Runtime/RevSoundSystem/README.md` |
| 状态机（三件套） | `状态机/状态机使用说明.md`、`状态机架构解析.md`、`状态机Demo示例讲解.md` |
| 资源 `RevResManager` | `资源加载系统/资源加载系统使用说明.md`、`资源加载系统架构解析.md` |
| 单例 `RevSingleton*` | **暂无专章**（相关：`公共Mono模块`、`服务定位器`） |
| UI `RevUI` / `RevUIPanel` | `UI系统/UI系统使用说明.md`、`UI系统架构解析.md`（**第十章**：要不要做 System / BusinessLogic） |
| 动作序列 `RevSequence` | `动作序列/动作序列使用说明.md`、`动作序列架构解析.md` · README：`Runtime/RevActionSequence/README.md` |
| GM 指令 `RevGM` | `GM指令/GM指令使用说明.md` · README：`Runtime/RevGMCommand/README.md` |
| 配置表 `RevDataTableManager` | `导表工具/导表工具使用说明.md`、`导表工具架构解析.md`、`导表工具对比与设计说明.md` |
| 热更（扩展包）`RevHotUpdate` | `热更新/RevHotUpdate使用说明.md`、`RevHotUpdate架构解析.md`、`RevHotUpdate技术方案.md` · README：`Assets/Revolution.HotUpdate/README.md` + `Assets/Revolution.Demo/RevHotUpdate.Demo/README.md` |

**有模块级 README 的只有 7 个**：`RevLog`、`RevTimer`、`RevInput`、`RevServiceLocator`、`RevSoundSystem`、`RevActionSequence`、`RevGMCommand`（其余模块的说明只在 `Revolution.Document` 下）。

## 3. 仓库与安装（交付面）

```text
仓库：https://github.com/<owner>/Revolution         （main 是唯一开发分支）
生成分支（整份重建、不要在上面提交）：
  package    ← Assets/Revolution/**            框架本体
  demo       ← Assets/Revolution.Demo/**       示例工程（18 个可运行场景）
  hotupdate  ← Assets/Revolution.HotUpdate/**  资源热更扩展包
  skill      ← Skills/**                       给 AI Agent 用的技能包（克隆进 .codebuddy/skills 等目录即可）

工程内安装（两种都行）：
  git clone -b package   https://github.com/Yokino337088/Revolution.git Assets/Revolution
  git clone -b demo      https://github.com/Yokino337088/Revolution.git Assets/Revolution.Demo
  git clone -b hotupdate https://github.com/Yokino337088/Revolution.git Assets/Revolution.HotUpdate
（也可以子模块 / UPM git URL；框架本体零第三方依赖）

UPM 安装注意：包目录只读时，`RevResPath.cs` / `ResMap.txt` / 音效路径常量这类**生成物写不进去**，
工具会跳过并提示（见 `ABBuildSetting.EnsureWritableForGeneratedCode`）。
```

## 4. 目录速览（工程内）

```text
RevolutionFrameWork_Unity/Assets/
├── Revolution/                 框架本体（Runtime 17 模块 + Editor 工具 + Generation/Resources）
│   ├── Runtime/<模块>/{Core,Facade,Implementation,Interfaces,Support}
│   ├── Editor/RevResourceSystem/ABTool/…  RevAB 打包工具（打包/分包/依赖/体积/检查/快照）
│   ├── Editor/RevDataLoad/…               导表工具
│   ├── Generation/RevResPath.cs           生成的资源路径常量（勿手改）
│   └── Resources/ResourceSystem/ResMap.txt 引导映射表（勿手改）
├── Revolution.HotUpdate/       扩展包：资源热更（独立程序集，可整体删除）
├── Revolution.Demo/            示例工程（18 个场景 + 各自 README）
├── GameRes/                    资源根目录（默认 resRoot，逻辑名相对它计算）
└── StreamingAssets/<平台>/      打包产物拷贝目标（首包基线；由打包工具自动拷）

<仓库根>/Revolution.Document/   文档（md 权威 + html 站点）
<仓库根>/Skills/                给 AI Agent 用的技能包（本文件所在处）
<仓库根>/README.md              框架总览（模块表 / 工程外验证 / 热更决策）
```

## 5. 找不到文档时

1. 先看模块的 `Facade/` 文件头注释（框架要求每个文件开头写清"为什么这么写"）；
2. 再看对应的 demo 场景脚本（`Assets/Revolution.Demo/<模块>.Demo/*.cs`）——**它能编译、能运行，是最可信的用法示例**；
3. 然后是本 skill 的 `references/modules-core.md` / `modules-content.md`；
4. 最后才是读内核（`Core` / `Implementation`）——**写业务时不需要读内核**，读了反而容易越界。
