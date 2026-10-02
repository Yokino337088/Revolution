# 编辑器工具与菜单（一次找全）

> 全部菜单都挂在顶级菜单 `Revolution.Tools` 下。改代码前先确认"这件事有没有现成工具做"。

## 1. 菜单一览

| 菜单 | 用途 |
|---|---|
| `Revolution.Tools/资源/RevAB 打包工具` | **主工具，6 页签**：打包 / 分包浏览 / 依赖 / 体积 / 检查 / 快照。一键打包、"仅生成映射"都在这里 |
| `Revolution.Tools/资源/RevAB 分包浏览` | 直达「分包」页签：看包树、给资源打/改 AB 标记、Project 窗口行尾显示"属于哪个包" |
| `Revolution.Tools/资源/生成资源路径常量 (RevResPath)` | 重新生成 `Assets/Revolution/Generation/RevResPath.cs`（目录 → 常量） |
| `Revolution.Tools/资源/生成音效目录常量 (RevSoundPath)` | 重新生成 `RevSoundSystem/Generated/RevSoundPath.cs` |
| `Revolution.Tools/资源/AB 加载模式（编辑器）` | 勾选开关：编辑器里是否强制走 AB 模式（= `RevResBootstrap.UseABInEditor`，EditorPrefs 持久化） |
| `Revolution.Tools/GM 指令面板`（Ctrl+Shift+G） | 执行 GM 命令：联想、参数提示、**高危二次确认**、历史、返回值显示 |
| `Revolution.Tools/配置表/导表工具` | Excel → 数据（可只出数据 / 全量出代码+数据） |
| `Revolution.Tools/配置表/快速导出（仅数据，不生成代码）` | 只改数值时用：**不触发脚本编译** |
| `Revolution.Tools/配置表/快速导出（用上次的 Excel 源）` | 用上次源直接重导 |
| `Revolution.Tools/热更新/热更清单窗口` | 生成 `RevHotManifest.txt` / `RevHotBuiltin.txt` + 自检（`Revolution.HotUpdate` 扩展包） |
| `Revolution.Tools/热更新/Demo/① 一键：打包 + 清单 + 装配本地 CDN` | 热更 demo：构建序号 +1 → 收集校验 → 打 AB → 写映射表/产物清单 → 拷首包 → 生成清单 → 装配 `{工程根}/LocalCDN`（内容先、清单最后） |
| `Revolution.Tools/热更新/Demo/②③④` | 打开本地 CDN 目录 / 清空客户端本地热更目录（验证回退出包基线）/ 打印该填的 URL |

## 2. 打包工具关键事实

- **产物目录**：`AssetBundles/<平台名>/`（`ABBuildSetting.GetOutputDir(target)`）；平台名规则：Standalone* → `PC`，其余 `target.ToString()`（Android / iOS / WebGL）。
- **产物内容**：各 `<包名>`（无扩展名的 AB）+ 每包一份 `<包名>.manifest`（CRC / Hash / 依赖）+ 主包（名 = 平台名）+ `BuildManifest.json`（version / platform / buildTime / bundles）。
- **配置资产**：`Assets/Editor/ABBuildConfig.asset`（`ABBuildConfig.Instance`）：
  - `resRoot`（资源根目录，默认 `Assets/GameRes`）——逻辑名就是"相对它的路径去扩展名"；
  - `markMode`（`ScanExisting` 读已有标记 / `AutoByFolder` 按顶层目录名分包）；
  - `copyToStreamingAssets` + `copyTarget`（打包后自动拷进 `Assets/StreamingAssets/<平台>`，首包基线就靠它）；
  - `version`（写进 `BuildManifest.json`）、`excludeFolders`、`excludeExtensions`、`checkUnmarkedAssets`、`compressMode`、`forceRebuild`、`cleanOutputBeforeBuild`。
- **一键打包链顺序**（照抄）：`ABCollector.Collect()` → `ABValidator.Validate(collect)`（`CanBuild == false` 就别打）→ `ABBuilderCore.Build(target)` → 依赖分析 → `ABManifestWriter.WriteResMap(collect)` + `WriteBuildManifest(build)` + 布局快照 → `ABResPathGenerator.Generate()` → `ABBuilderCore.CopyToStreamingAssets(outputDir)` → `AssetDatabase.Refresh()`。
- **CI 无人值守**：`-executeMethod Revolution.Editor.ABCIBuild.Build`（失败会以非 0 退出码结束进程）。

## 3. 生成物（**不要手改**）

| 文件 | 谁生成 | 作用 |
|---|---|---|
| `Assets/Revolution/Resources/ResourceSystem/ResMap.txt` | 打包工具「仅生成映射」/ 一键打包 | 逻辑名 → 包名 + 资源名（运行时引导表，随包体进 Resources） |
| `Assets/Revolution/Generation/RevResPath.cs` | 资源路径常量生成器 | 资源目录常量（业务写 `RevResPath.Data` 而不是手拼字符串） |
| `Assets/Revolution/Runtime/RevSoundSystem/Generated/RevSoundPath.cs` | 音效目录常量生成器 | 音效目录常量 |
| `AssetBundles/<平台>/BuildManifest.json` | 打包链 | 版本 / 平台 / 时间 / 各包体积 |

> UPM 只读安装时这些位置写不进去，框架会**跳过生成并提示**（`ABBuildSetting.EnsureWritableForGeneratedCode`）。

## 4. 自动化（GitHub Actions，仓库 `main` 上）

| 工作流 | 触发（改动路径） | 产出 |
|---|---|---|
| `sync-package-branch.yml` | `Assets/Revolution/**` | `package` 分支 = 框架本体（分支根即包根，可 clone / 子模块 / UPM git URL） |
| `sync-demo-branch.yml` | `Assets/Revolution.Demo/**` | `demo` 分支 = 示例工程（18 个可运行场景） |
| `sync-hotupdate-branch.yml` | `Assets/Revolution.HotUpdate/**` | `hotupdate` 分支 = 热更扩展包 |
| `sync-skill-branch.yml` | `Skills/**` | `skill` 分支 = 给 AI Agent 用的技能包（`git clone -b skill ... .codebuddy/skills`） |
| `pages.yml` | `Revolution.Document/**` | 文档站（https://yokino337088.github.io/Revolution/ ） |

**三条生成分支都是"整份重建、不保留历史"**：改代码请改 `main`，不要往生成分支上提交。

## 5. demo 场景（18 个）

`Assets/Revolution.Demo/<模块>.Demo/<模块>Demo.unity`，索引在 `Assets/Revolution.Demo/RevDemoSceneList.txt` 与 `Assets/Revolution.Demo/README.md`。
每个场景：打开 → Play → 左侧按钮逐个点，右侧是步骤日志（讲清每一步发生了什么）。
写新 demo 照 `templates/RevXxxDemo.cs.txt` 的骨架（OnGUI：左按钮 + 右日志 + 简单样式）。
