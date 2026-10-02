# 资源热更（扩展包 RevHotUpdate 专题）

> 权威文档：`Revolution.Document/热更新/RevHotUpdate使用说明.md`（怎么用）、`RevHotUpdate架构解析.md`（为什么这么设计、失败怎么收场）、
> `RevHotUpdate技术方案.md`（要不要做、平台核实、存储选型、大版本锚定、决策与分期）。
> 代码：`Assets/Revolution.HotUpdate/`（程序集 `Revolution.HotUpdate` / `Revolution.HotUpdate.Editor`）· demo：`Assets/Revolution.Demo/RevHotUpdate.Demo/`。

## 1. 一行接法（业务侧唯一必须写的代码）

```csharp
// 放在"加载任何业务资源"之前（启动流程里一次即可）
RevHotUpdateResult result = await RevHotUpdate.InitializeAsync(
    new RevHotConfig
    {
        RemoteRoot = "https://cdn.example.com/gameA",   // 必填；http:// 需要 AllowHttp = true
        Env = "", Channel = "",                          // 留空 → URL 里不出现这两段
        VerifyMode = RevHotVerifyMode.Hash,              // 尺寸 + SHA-256
        Concurrency = 3,                                 // 手机上别超过 4
        LogTag = "HotUpdate",
        OnForceUpdateRequired = info => ShowUpdateDialog(info.Message),
    },
    p => progressBar.value = (float)(p.Percent / 100.0));

if (result.Success == false) { RevLog.Warn(result.Error.Message, "HotUpdate"); }   // 失败不破坏旧版本
// 之后加载资源的代码完全不变：RevResManager.Load / LoadAsync
```

- 分步：`CheckAsync`（只拉清单算差量，给"要不要更新/下多少"的 UI）→ `UpdateAsync(check)`（**必须把 check 原样传入**）。
- 只等不发起：`WaitReadyAsync()`（从没跑过会返回 `NotRun`，提示"你还没接热更"）。
- 观察：`RevHotUpdate.State` / `LastError` / `LocalResVersion` / `Progress` 事件 / `Dump()`。

## 2. 框架侧只有两个钩子（默认 null，不装热更包零影响）

| 钩子 | 位置 | 作用 |
|---|---|---|
| `RevABLoader.BundlePathResolver` | `Func<string, string>`，参数 = 包名，返回 = 完整文件路径或 URL；**返回 null/空 = 交还框架默认（StreamingAssets/<平台>/<包名>）** | 包"从哪读"：持久化版本目录 → 首包落地目录 → 交还默认 |
| `RevResBootstrap.ResMapOverride` | `Func<Dictionary<string,string>>`，语义是**覆盖合并**（热更表覆盖内置表同名键，内置表独有键保留） | 映射表"用哪份"：否则**新增资源永远加载不到** |

- 钩子是静态委托，**必须发生在 `RevResBootstrap.Init` 之前**（`InitializeAsync` 已保证）；用方法组而不是 lambda（关 Domain Reload 时第二次 Play 不持有失效闭包）。
- 想手动接管资源系统初始化的工程才需要自己调 `RevHotUpdate.Install()`。

## 3. 覆盖式语义（**最关键的一条设计**）

```text
热更包 = "允许被覆盖"：持久化版本目录里有 → 用它；没有 → 回退 StreamingAssets 首包；两者都没有 → 才报错
```

- **不要**实现成"热更包只从持久化读" —— 新装机 / 清数据 / 换机时持久化目录是空的，会直接崩（首屏若在热更包里就是黑屏）。
- 失败**绝不破坏现有版本**：`current.txt` 没动 → 玩家照常玩旧版；最坏兜底 = **删掉 `{persistentDataPath}/RevHotUpdate` 目录**即回到出包状态。

## 4. 远端目录约定（唯一真相在包内 `RevHotUrlBuilder`）

```text
清单 ：{RemoteRoot}/{环境?}/{平台}/{大版本}/{渠道?}/RevHotManifest.txt          ← 不带资源版本（它是"发现新版本"的入口）
资源 ：{RemoteRoot}/{环境?}/{平台}/{大版本}/{渠道?}/{资源版本}/bundles/<包名>   ← 带版本段（内容不可变，可长缓存）
映射表：{RemoteRoot}/{环境?}/{平台}/{大版本}/{渠道?}/{资源版本}/ResMap.txt       ← 与包同批更新
```

- **上传纪律**：先传所有内容，**最后传清单**（清单是"新版本已就绪"的原子开关）→ 玩家要么拿旧版本、要么拿完整新版本，不会拿"半新半旧"。
- **两条缓存纪律**：内容路径 `immutable` 长缓存（URL 带版本段）；清单路径 **no-cache**（否则玩家永远拿到旧清单）。
- **平台名**：编辑器里恒为 `PC`（`RevHotPlatform` 带 `!UNITY_EDITOR`）；打包侧 `ABBuildSetting.GetPlatformName`（Standalone* → `PC`）——**两边必须对齐**，否则编辑器调试会 404。

## 5. 版本模型：大版本锚定（`@appVersion` = `Application.version`）

- 清单里 `@appVersion` 与客户端不一致 → `AppVersionMismatch`（**设计如此**）：跨大版本必须重新出包，资源层因此**不需要做向后兼容**。
- "大版本内热更资源、跨大版本重出包"是这个框架推荐的发版纪律（王者那套做法）；`ForceUpdateRequired` / `OnForceUpdateRequired` 只负责**识别 + 回调**，跳商店 / 整包下载由业务决定。
- 版本目录：`{LocalRoot}/{平台}/{大版本}/{资源版本}/`；保留 N 份（`KeepVersions`）就能回滚；切换是 `ShutdownAll() → 切 current → 删旧版本 → Init()`。

## 6. 本地假 CDN demo（不改真云就能跑通全链路）

```text
① 菜单：Revolution.Tools / 热更新 / Demo / ① 一键：打包 + 清单 + 装配本地 CDN
② 双击 Assets/Revolution.Demo/RevHotUpdate.Demo/起本地CDN.cmd（默认 http://127.0.0.1:8000）
③ 打开 RevHotUpdateDemo.unity 点 Play → 面板：① 检查更新 → ② 执行更新 → ③ 加载演示资源
```

- 演示资源 `Assets/GameRes/RevHotDemo/revhot_demo.txt` 的"构建序号"每次跑菜单 ① 都会 +1 → 于是每轮都能看到真实的**差量更新**（第二轮只下变化的那一个包）。
- 假 CDN 脚本支持 **Range**（断点续传）与两类缓存头，行为与真实对象存储一致；用它验证"清单 no-cache / 内容 immutable"这两条纪律。
- demo 面板还演示了两个最容易踩的坑：`RevResBootstrap.UseABInEditor`（编辑器默认直读，看不到热更）、"先 Release 再 Load"（缓存 + 引用计数）。

## 7. 平台差异（技术方案第十七、十八章）

| | 文件型平台（Android / iOS / PC） | URL 型平台（WebGL / 小游戏） |
|---|---|---|
| 下载 | `UnityWebRequest` + `DownloadHandlerFile` 落盘 | AB 由引擎按 URL 拉取并缓存（**不做**本地落地） |
| 校验 | 尺寸 + SHA-256（分帧流式） | 交给引擎（清单里带 Unity `Hash128` + CRC） |
| 续传 | `Range` + `.part` | 不需要 |
| 版本目录 | 本地 `…/{平台}/{大版本}/{资源版本}/` | **URL 里的版本段就是目录** |
| 特殊情况 | Android 首包在 APK 内（`jar:`），需要"首包落地"到 `persistentDataPath` | 没有文件系统；并发降到 2 |

存储选型（技术方案第十八章）：**对象存储 + CDN，首选腾讯云 COS + CDN**（与微信同生态），次选阿里云 OSS + CDN；
客户端只认"一个 HTTPS 域名 + 路径模板"，**不引任何云 SDK**（换家只改配置）；小游戏要在公众平台配"服务器域名"（备案 + HTTPS）。

## 8. 装法与分支

```text
git clone -b package   https://github.com/Yokino337088/Revolution.git Assets/Revolution
git clone -b hotupdate https://github.com/Yokino337088/Revolution.git Assets/Revolution.HotUpdate
```

- `hotupdate` 是**生成分支**（CI 从 `main` 的 `Assets/Revolution.HotUpdate/` 整份重建），不要往上提交。
- **配对前提**：热更包用的两个钩子是框架侧后加的 → 框架版本必须"不早于"引入钩子的那一版。
- 跨大版本**不要复用远端包目录**（哪怕 hash相同）：会给"资源与代码接口不匹配"埋雷。
