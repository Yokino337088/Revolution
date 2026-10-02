# 资源加载与 AB（RevResourceSystem 专题）

> 权威文档：`Revolution.Document/资源加载系统/资源加载系统使用说明.md`、`资源加载系统架构解析.md`。
> 本文件是"写代码时会用到的规则 + 最容易踩的坑"。

## 1. 一条链路（业务只需要记这个）

```text
RevResManager.Load / LoadAsync(根目录, 资源名, 类型, 分组)
   → 拼逻辑路径（两段式，缓存键用增量 FNV-1a，命中时零字符串分配）
   → 查缓存（命中：引用计数 +1、清 MarkedUnused、刷新 LRU）
   → 未命中：按注册顺序问策略链 → 第一个 Match 的策略负责 MapPath + 建 Loader
   → 加载 → 建句柄（IsLoaded / Content / ErrorReason）→ 入缓存
   → 业务 Release → 计数归零 → 进 _unused（延迟释放）→ FlushUnused()（切场景读条期）真卸
```

关键点：**"从哪加载"由策略链决定，业务代码一行不用改**（编辑器直读 / AB / Resources 三种来源同一套业务代码）。

## 2. 策略链与两种模式（最常踩的坑）

| 模式 | 注册了谁 | 表现 |
|---|---|---|
| **编辑器默认（开发模式）** | 只注册 `RevEditorResPolicy`，**直接 return** | 所有资源走 AssetDatabase 直读，**AB / Resources 完全不参与**；改文件立即生效 |
| **AB 模式（真机 / 编辑器手动开启）** | `RevABResPolicy`（除 `Res/` 前缀外全接管）→ `RevResourcesResPolicy`（链尾兜底） | AB 找不到时靠 `AllowFallback = true` 落到 Resources 兜底 |

- 编辑器想看 AB / 热更效果：`RevResBootstrap.UseABInEditor = true`（菜单 `Revolution.Tools/资源/AB 加载模式（编辑器）`，EditorPrefs 持久化）。
- 这个开关**切换后会立即重建策略**，不用重启编辑器。
- 兜底是"能用"也是"陷阱"：AB 里漏标的资源，在编辑器直读下**照样正常**，直到出真机才报 `FileNotExist` —— 所以打包前必须跑漏标检查（见 `editor-tools.md`）。

## 3. ResMap.txt（逻辑名 → 包名 + 资源名）

- 位置：`Assets/Revolution/Resources/ResourceSystem/ResMap.txt`（**随包体进 Resources**，运行时 `Resources.Load<TextAsset>("ResourceSystem/ResMap")` 读）。
- 格式三列：`逻辑名|包名|资源名`；`#` 开头跳过；**段数 ≠ 3 的整行跳过**。
  - 逻辑名 = 资源相对**资源根目录**（默认 `Assets/GameRes`，见 `ABBuildConfig.resRoot`）的路径去扩展名，如 `Hero/1001`
  - 包名 = 该资源的 AB 包名（=`assetBundleName`）
  - 资源名 = 包内资源名（文件名去扩展名）
- **它是"引导链的起点"**：表放在 `Resources` 里、路径写死（运行时 `"ResourceSystem/ResMap"` ↔ 编辑器 `ABBuildSetting.MapAssetPath` 是一对，改一个必须改另一个）——因为读表**不能依赖资源系统自身**（否则死循环）。
- **表缺失不崩**：返回空表 → 查不到映射 → 全部走 Resources 兜底。这也是"编辑器里一切正常、真机大面积失败"的经典成因。

## 4. 引用计数、分组与释放（写业务必须懂）

- 命中缓存 `RefCount++`；`Release` 归零后进 `_unused` **延迟释放**，由 `FlushUnused()` 统一卸（`Resources.UnloadUnusedAssets` 很贵，所以不能在 Release 里立刻做）。
- **失败句柄也入缓存**：避免每帧反复重试坏资源。
- **分组 `RevResGroup`（`Unknown/Battle/UI/Sound/Config/Scene`）只影响卸载，不影响加载**：
  - 归属**首次确定、永不覆盖**（`Unknown` 可被首次补齐，是唯一例外）；
  - 传 `Unknown` 的资源**永远不会**被 `UnloadGroup` 点名；
  - 跨域共享资源打 `RevResInstanceFlag.Resident`（连 `force` 都不参与卸载）；
  - `UnloadGroup(group, force: true)` 会把"引用计数 > 0"的一起清账 → **会误伤别的域的共享资源**，慎用。
- 切场景约定：`RevScene.AutoClearPool` 已自动清对象池；UI / 音效 / 资源分组要自己在 `RevScene.OnLoadStart` 里收（`RevUI.CloseAll` / `RevSound.StopAll` / `RevResManager.UnloadGroup`）。

## 5. 句柄（RevResHandle）

- 只暴露 `Content` / `Data` 两个属性（避免业务乱强转），外加：`IsLoaded`、`State`、`ContentType`、`ErrorReason`、`Group`、`RefCount`、`BundleName`。
- 失败原因看 `ErrorReason`（**框架本方案不打日志**）：`FileNotExist` / `LoadFail` / `TypeMismatch` 等。
- 同步 `Load` 在"未缓存 + 真机 AB"下拿不到内容（返回未完成句柄）→ 真机首次一律 `LoadAsync`。

## 6. AB 打包（编辑器工具侧）

- 菜单：`Revolution.Tools/资源/RevAB 打包工具`（6 页签：打包 / 分包浏览 / 依赖 / 体积 / 检查 / 快照）、`Revolution.Tools/资源/RevAB 分包浏览`。
- 产物：`AssetBundles/<平台名>/`（平台名规则见 `ABBuildSetting.GetPlatformName`：Standalone* → `PC`，其余 `target.ToString()`）；每个包一个 `<包名>.manifest`（含 CRC / Hash / 依赖）；另有 `BuildManifest.json`（框架自用：version / platform / buildTime / bundles）。
- 主包（AssetBundleManifest）名 = **产物目录名** = 平台名（运行时 `RevABLoader.MainName` 同源）。
- 分包方式（`ABBuildConfig.markMode`）：
  - `ScanExisting`（默认）：读资源 `.meta` 里已有的 `assetBundleName`，工具**绝不改标记**；
  - `AutoByFolder`：按 resRoot 下**顶层目录名**当包名重打（无子目录 → `default`）。
- 一键打包链（照抄这个顺序）：`Collect` → `Validate`（`CanBuild=false` 就别打）→ `ABBuilderCore.Build(target)` → 依赖分析 → `WriteResMap` + `WriteBuildManifest` + 布局快照 → `ABResPathGenerator.Generate()`（生成 `RevResPath` 常量）→ `CopyToStreamingAssets` → `AssetDatabase.Refresh()`。
- 校验会拦：命名非法、逻辑路径冲突、同包重名、空包、**漏标**（resRoot 下有资源没有 AB 标记）。

## 7. 写业务的四条硬规则

1. **只写逻辑路径**：用 `RevResPath.*` 生成常量（`Assets/Revolution/Generation/RevResPath.cs`）或两段式参数；**禁止** `StreamingAssets` / `dataPath` / `Resources.Load` 自己拼路径。
2. **成对 Release**：`Load`/`LoadAsync` 每次都要有对应释放点（面板 `OnClose` / 状态退出 / 分组卸载）。
3. **真机首次一律异步**：`LoadAsync` + 回调（或 `await` 包装）；同步 `Load` 只用于"已缓存 / 编辑器直读"。
4. **新增资源要打 AB 标记并重生成 ResMap**：资源放进 resRoot（默认 `Assets/GameRes`）→ 设 `assetBundleName`（或用分包浏览页签）→ 跑打包 / "仅生成映射" → 提交 `ResMap.txt` 与 `RevResPath.cs`。

## 8. 排查顺序

1. `handle.ErrorReason`（不是 null 就有话说）；
2. `ResMap.txt` 里有没有这条逻辑名（没有 → 没打标记 / 没重生成表）；
3. 编辑器直读 vs AB 模式是否一致（`RevResBootstrap.UseABInEditor`）；
4. `RevResManager.CachedCount` / `GetRefCount(...)` 看引用计数是否归零（不归零就永远不卸）；
5. 打包工具的「检查」页签看漏标 / 依赖 / 体积。
