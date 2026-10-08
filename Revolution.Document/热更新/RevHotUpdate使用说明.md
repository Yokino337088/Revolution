# RevHotUpdate · 使用说明（手把手）

> 定位：给 Revolution 框架补上"**AB 包从远端下载 + 版本管理 + 加载路径重定向**"的能力。
> 形态：**独立包**（`Assets/Revolution.HotUpdate/`，程序集 `Revolution.HotUpdate` + `Revolution.HotUpdate.Editor`）——
> **导入就有、不导入零影响**；业务侧的资源加载代码**一行都不用改**。
> 读法：先跑通第〇章，再按第四章配字段；出问题直接跳第八章 FAQ / 第九章错误码。
> ⚡ **想先看跑起来的效果**：`Assets/Revolution.Demo/RevHotUpdate.Demo/`（演示面板 + 一键"打包 + 清单 + 装配本地 CDN" + `起本地CDN.cmd` 假 CDN，3 步跑通全链路，见该目录 README）。

## 目录

- 〇、3 分钟跑通（最小示例）
- 一、它做什么、不做什么
- 二、装在哪里（目录、程序集、对框架的 2 处改动）
- 三、四个入口 API
- 四、配置字段表
- 五、出包与发布流程
- 六、本地目录长什么样 / 怎么回滚
- 七、平台差异（Android / iOS / PC / 小游戏）
- 八、常见问题（FAQ）
- 九、错误码表
- 十、接入自查清单

---

## 〇、3 分钟跑通（启动时调用一次，就是全部）

### 前置

1. 框架已经能用（`RevResManager.Load*` 能加载资源）；
2. 用框架的打包工具打过一次 AB（`AssetBundles/<平台>/`）；
3. 远端（CDN / 对象存储）上有一个目录，里面是"和打包产物同结构"的资源 + 一份清单。

### 代码（只有这一处：放在"加载任何业务资源"之前）

```csharp
RevHotUpdateResult result = await RevHotUpdate.InitializeAsync(
    new RevHotConfig
    {
        RemoteRoot = "https://cdn.example.com/gameA",     // 唯一必填项
    },
    p => loadingBar.Set(p.Percent, p.Text));              // 进度：p.Text 已是一句人话

if (result.Success == false)
{
    // 热更失败不等于"进不去游戏"：旧版本照常能玩，这里按你们的产品策略处理（重试 / 跳过 / 提示）
    tips.Show(result.Error.Message);
}

// 之后照旧写业务，加载 API 完全不变：
RevResManager.LoadAsync<Sprite>(RevResPath.UI_Icon, "Hero_1001", sprite => icon.sprite = sprite, RevResGroup.UI);
```

> ★ **这一行调用就是"整次热更"**：拉清单 → 大版本校验 → 版本比对 → 只下变化的包 → 校验落地 → 切版本 → 重装资源策略，全在 `InitializeAsync` 里完成。
> 它**不是**让你"手动去加载远端资源"——你不需要自己下载任何东西；这个调用返回后，直接写业务即可。

### 关于重复调用与等待

- **重复调用不会跑两遍**：热更进行中再次调用 `InitializeAsync`，会直接等正在跑的那一轮的结果（防止下载两遍、资源策略重装两次）；
- **别处想等它结束**（例如首场景的业务要等热更完成才加载）：调 `await RevHotUpdate.WaitReadyAsync()` —— 它**不发起新热更**，只是等正在进行的那一轮；
- **取消**：传一个 `RevCancellationToken`（框架自带）即可；已下载的内容会保留，下次继续。

### 三条必须记住的规矩

| # | 规矩 | 为什么 |
|---|---|---|
| 1 | `InitializeAsync` 要在**加载业务资源之前**调用 | 它是"把资源层切成热更后的状态"的那一步；太晚调用会把已经加载的旧资源卸载重载 |
| 2 | 远端目录要按 `{环境}/{平台}/{大版本}/{渠道}/{资源版本}/` 放好 | 清单不带资源版本（负责"发现新版本"），资源 URL 带资源版本（内容不可变，CDN 可长缓存） |
| 3 | 上传时**先传内容、最后传清单** | 清单是"原子切换开关"；先传清单会让玩家拿到一半新一半旧的组合 |

---

## 一、它做什么、不做什么

| ✅ 做 | 说明 |
|---|---|
| 拉版本清单 + 版本比对 | 主动发现"服务器上有新资源"，并算出**只下变化过的包** |
| 差量下载 | 包级差量（hash 变了的才下）；支持断点续传、多源降级、指数退避重试、磁盘预检 |
| 校验与落地 | 尺寸 + SHA-256；下载写 `.part`、校验通过才原子改名，**不会出现半个包被当成好包** |
| 原子切换与回滚 | 版本目录 + 完成标记 + 版本指针原子替换；保留 N 个历史版本，回滚只改指针 |
| 加载路径重定向 | 热更包从持久化目录读、没更新过的回退内置；**业务加载代码不变** |
| 映射表热更 | `ResMap.txt` 与包同批更新 → **新增资源也能热更下发** |
| 首包兜底 | 首包全量内置 + 覆盖式语义（持久化优先 → 内置兜底），新装机/清数据一样能进游戏 |

| ❌ 不做 | 为什么 |
|---|---|
| 代码热更（HybridCLR / ILRuntime） | 另一条技术线（要改 IL2CPP 与程序集拆分），且 iOS 审核风险最高 |
| AB 加密 / 防篡改 | AB 天生可解包，加密只提高门槛；需要时另做独立模块 |
| 二进制差量（按块打补丁） | LZ4 已块压缩，改一张图会波及多个块；包级差量已拿走绝大部分收益 |
| 灰度 / 分渠道分流 | 属于运营后台能力；本包只提供 `Channel` 目录段 |
| 按需下载（tag） | 阶段 4 的能力，当前是"清单里有就下" |
| 跳商店 / 整包下载 | 发行层的事；框架只负责"识别大版本不匹配 + 一次回调" |

---

## 二、装在哪里

```text
Assets/Revolution.HotUpdate/
├── Runtime/    asmdef: Revolution.HotUpdate         （引用 Revolution.Runtime）
└── Editor/     asmdef: Revolution.HotUpdate.Editor  （仅编辑器，引用 Revolution.Runtime / HotUpdate / Editor）
```

### 怎么拿到这个包（两种装法）

```text
# ① 分支安装（推荐，与框架本体配对）：每个包各一条命令
git clone -b package   https://github.com/Yokino337088/Revolution.git Assets/Revolution
git clone -b hotupdate https://github.com/Yokino337088/Revolution.git Assets/Revolution.HotUpdate

# 想跑本目录开头提到的那个 demo（示例工程在 demo 分支）：
git clone -b demo      https://github.com/Yokino337088/Revolution.git Assets/Revolution.Demo
# → Assets/Revolution.Demo/RevHotUpdate.Demo/（演示面板 + 场景 + 起本地CDN.cmd 假 CDN）

# ② 或者：直接把仓库里的 RevolutionFrameWork_Unity/Assets/Revolution.HotUpdate 拷进自己工程的 Assets 下
```

> ★ **配对前提（升级时必看）**：热更包的运行时程序集只引用 `Revolution.Runtime`，但它使用的两个钩子
> （`RevABLoader.BundlePathResolver` / `RevResBootstrap.ResMapOverride`）是框架侧后加的 ——
> 所以框架必须**不早于引入这两个钩子的那一版**，否则编译不过。单独升级热更包时请一并确认框架版本。
>
> `hotupdate` 是**生成分支**（CI 从 `main` 的 `Assets/Revolution.HotUpdate/` 整份重建，不保留历史），
> 见 `.github/workflows/sync-hotupdate-branch.yml`；本地改了包直接推 `main` 即可，不要往生成分支上提交。

**"不导入零影响"靠四条**：① 独立程序集，框架不反向引用它；② 零静态构造、零 `Update`，没人调用就不干活；③ 它给框架加的两个钩子**默认是 `null`**，不设置时框架行为与从前逐字节一致；④ 卸载 = 删掉这个目录 + 启动流程里那一行调用。

### 对框架的改动（就这两处）

| 位置 | 钩子 | 干什么 |
|---|---|---|
| `RevABLoader.BundlePathResolver` | `Func<string, string>` | 参数 = 包名，返回 = 该包的完整路径（本地）或 URL；**返回 null 就交还框架默认的 StreamingAssets 逻辑** |
| `RevResBootstrap.ResMapOverride` | `Func<Dictionary<string, string>>` | 提供"热更映射表"，框架把它**覆盖合并**到内置表上（新资源能热更、老资源有兜底） |

> 这两个钩子由 `RevHotUpdate.InitializeAsync` 自动装载（内部调 `Install()`）；只有"自己接管资源系统初始化"的工程才需要手动 `RevHotUpdate.Install()`，且必须在 `RevResBootstrap.Init()` 之前。

### 这两个钩子到底怎么用（面向小白，带可照抄的示例）

> **先给结论：用官方热更包时，这两处已经替你写好了** —— 你一行都不用写。
> 只有这两种情况才需要自己写：① 不用热更包，但你**有自己的下载器 / CDN 体系**；② 想改热更包的解析规则（多插一层目录、做灰度等）。

| 你的情况 | 要写代码吗 |
|---|---|
| 用 **RevHotUpdate** 官方热更包 | ❌ 什么都不用写（`InitializeAsync` 装的就是包里那套：持久化版本目录 → 首包落地 → 回退 StreamingAssets） |
| 只想在**编辑器**里看看 AB 模式的效果 | ❌ 不用钩子，开 `RevResBootstrap.UseABInEditor = true`（菜单 `Revolution.Tools/资源/AB 加载模式（编辑器）`） |
| 自己接对象存储 / CDN，不用热更包 | ✅ 写下面的 **示例 1**（回答"包从哪读"） |
| 自己下发映射表（自研热更 / 灰度） | ✅ 写下面的 **示例 2**（回答"表从哪来"） |
| 两者都要 | ✅ 示例 1 + 2，再按 **示例 3** 的顺序装配 |

#### 钩子 1：`RevABLoader.BundlePathResolver` —— "这个包该去哪读？"

**契约（先背下来）**：

- **参数**：包名。就是 `ResMap.txt` 第二列那个名字，也是 `AssetBundles/<平台>/` 下的**文件名**（**没有扩展名**，比如 `hero`）。
- **返回**：这个包的**完整本地文件路径**（`D:/.../hero`）或 **URL**（`https://cdn.../hero`，WebGL / 小游戏用）。
- **返回 `null`（或空串）= "我不管，按框架默认来"** → 框架走 `{StreamingAssets}/<平台名>/<包名>`。
- **调用时机**：每次要加载某个包时都会被问一次（包已缓存在内存里就不会再问）。它**只回答"从哪读"**：不负责下载，也不负责卸载。

**示例 1：自研"持久化优先 + 回退内置"**（≈ 官方那套的最小版，可直接抄）：

```csharp
using System.IO;
using Revolution;
using UnityEngine;

/// <summary>自研包路径解析：下过更新就用更新，没下过就回退出包自带的那份。</summary>
public static class MyCdnPaths
{
    /// <summary>更新下来的包放哪：{persistentDataPath}/MyCdn/&lt;平台&gt;/bundles/</summary>
    public static string LocalBundleDir
    {
        get
        {
            // ★ 平台名必须与打包产物的目录名一致（Standalone 一律是 "PC"）—— 对不上就会一直找不到文件
#if UNITY_ANDROID && !UNITY_EDITOR
            const string platform = "Android";
#elif UNITY_IOS && !UNITY_EDITOR
            const string platform = "iOS";
#else
            const string platform = "PC";
#endif
            return Path.Combine(Application.persistentDataPath, "MyCdn", platform, "bundles");
        }
    }

    /// <summary>★ 这个函数就是钩子本体：框架每要加载一个包，就来问一次"它从哪读"。</summary>
    public static string ResolveBundlePath(string bundleName)
    {
        // ① 更新目录里有 → 用它（"热更生效"就是这一步）
        string local = Path.Combine(LocalBundleDir, bundleName);
        if (File.Exists(local))
        {
            return local;
        }

        // ② 没有 → 返回 null = 交还框架默认（StreamingAssets/<平台名>/<包名>）
        //    ★ 新手最容易写错的地方：返回值只能是"完整路径 / URL / null"三种。
        //      随便拼一个不存在的路径返回，框架会直接去读它并失败 —— 连"回退内置"的机会都没有。
        return null;
    }
}
```

装配（**必须在资源系统 `Init()` 之前**，时机说明见示例 3）：

```csharp
// 启动流程里、RevResBootstrap.Instance.Init() 之前：
RevABLoader.BundlePathResolver = MyCdnPaths.ResolveBundlePath;   // ★ 用方法组，不要写 lambda（见坑 4）
```

> ★ **WebGL / 小游戏**：那边**没有本地文件系统**（`File.Exists`、`LoadFromFile` 都不可用），
> 钩子应直接返回 **CDN 的 URL**（`https://.../bundles/<包名>`，URL 里带版本段，见技术方案第十七章）；
> 可以在同一个函数里用 `#if UNITY_WEBGL && !UNITY_EDITOR` 分开写。

#### 钩子 2：`RevResBootstrap.ResMapOverride` —— "逻辑名 → 包名|资源名 的表从哪来？"

**契约**：

- **返回**：`Dictionary<string, string>`；**键 = 逻辑名**（如 `Hero/1001`），**值 = `包名|资源名`**（如 `hero|1001`，中间是**半角竖线**）。
- **返回 `null` = 不覆盖**（框架只用内置表）。
- **语义是"覆盖合并"**：你返回的表里**同名键覆盖**内置表，内置表独有的键**保留** —— 所以"新资源能热更、老资源仍有兜底"。
- **调用时机**：每次**资源系统初始化**（`RevResBootstrap.Init()`）时调一次，结果被拿去合并成最终表。

**示例 2：读一张自己下发的表**（格式与框架 `ResMap.txt` 完全一致：`逻辑名|包名|资源名`）：

```csharp
using System.Collections.Generic;
using System.IO;
using Revolution;

/// <summary>自研映射表覆盖：把"更新下来的表"交给框架合并。</summary>
public static class MyCdnResMap
{
    /// <summary>★ 钩子本体：返回 null = 不覆盖；返回表 = 覆盖合并到内置表上。</summary>
    public static Dictionary<string, string> LoadOverrideResMap()
    {
        string path = Path.Combine(MyCdnPaths.LocalBundleDir, "..", "ResMap.txt");
        if (File.Exists(path) == false)
        {
            return null;                     // ★ 没有"更新表"就什么都不做，让框架用自己的内置表
        }

        var map = new Dictionary<string, string>(512);
        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) { continue; }   // 空行与 # 注释跳过

            string[] parts = line.Split('|');
            if (parts.Length != 3) { continue; }                          // 列数不对就跳过（容错，别让一行坏数据炸掉整张表）

            map[parts[0]] = parts[1] + "|" + parts[2];                    // 逻辑名 → 包名|资源名
        }

        return map.Count > 0 ? map : null;   // 空表也没必要覆盖
    }
}
```

```csharp
// 装配（同样要在 RevResBootstrap.Instance.Init() 之前）：
RevResBootstrap.ResMapOverride = MyCdnResMap.LoadOverrideResMap;
```

> ★ **为什么这个钩子非有不可**：映射表在包体的 `Resources` 里、运行时**只读**。
> 新增的资源如果没有新表，逻辑名就查不到 → 落到 `Resources` 兜底：**编辑器里看着一切正常，真机上才报 `FileNotExist`**。
> 这也是"表必须和包**同批更新**"的原因（表里有的包必须都已就位）。

#### 示例 3：装配时机 + 还原（**顺序错了整套不生效**）

```csharp
/// <summary>自己接管资源系统初始化时的标准写法；用官方热更包则不需要（InitializeAsync 内部已处理）。</summary>
public static class MyCdnBootstrap
{
    public static void Install()
    {
        // ① 先装钩子：必须在资源系统 Init 之前 —— Init 时会读映射表、注册策略
        RevABLoader.BundlePathResolver = MyCdnPaths.ResolveBundlePath;
        RevResBootstrap.ResMapOverride = MyCdnResMap.LoadOverrideResMap;

        // ② 再让资源系统按新钩子重装一次（Init 是幂等的；已经 Init 过就先卸干净）
        RevResBootstrap.Instance.ShutdownAll();
        RevResBootstrap.Instance.Init();
    }

    /// <summary>还原：钩子置回 null = 回到框架默认行为（"临时关掉自研热更"最省事的办法）。</summary>
    public static void Uninstall()
    {
        RevABLoader.BundlePathResolver = null;
        RevResBootstrap.ResMapOverride = null;
        RevResBootstrap.Instance.ShutdownAll();
        RevResBootstrap.Instance.Init();
    }
}
```

#### 五个坑（新手必读）

1. **返回值只能是"完整文件路径 / URL / `null`"**：返回一个不存在的路径 = 框架会直接去读它然后失败，**连回退内置的机会都没有**。想"我不管"就返回 `null`。
2. **别在钩子里做重活**：它会被**每个包**问一次（首屏几十个包 = 几十次调用），别在里面解析大文件、发网络请求或拼一堆字符串。
3. **路径不含扩展名**：AB 在磁盘上就叫 `<包名>`（没有 `.bundle` / `.ab` 后缀），加后缀会读不到。
4. **用方法组，不要写捕获实例的 lambda**：`= MyCdnPaths.ResolveBundlePath;` ✓；写成 `= name => new MyCdn(name).Resolve(name)` 会持有闭包 —— 关闭 Domain Reload 的工程第二次进 Play 可能拿到失效对象。
5. **必须在 `RevResBootstrap.Init()` 之前设置**。判断法：**Init 之后再设 = 本次不生效**；要生效就 `ShutdownAll()` → 设钩子 → `Init()`（示例 3 就是这个顺序）。

#### 想知道"到底生效了哪一份"？

- 用官方热更包：看 `RevHotUpdate.LocalResVersion` 与 `RevHotUpdate.Dump()`；
- 自研：在 `ResolveBundlePath` 里**每个包第一次**返回时打一条日志 ——
  `RevLog.Info($"包 {bundleName} 来自 {local}", "MyCdn")`。「到底读的是哪一份」这类问题，靠这一行日志最容易定位。

---

## 三、入口 API 与进度

| API | 什么时候用 |
|---|---|
| `InitializeAsync(config, onProgress, token)` | **启动时的那一次调用**（最省事）：检查 →（有更新则）下载 → 落地 → 切版本 → 重装资源策略，一条龙。**重复调用不会跑两遍**——会直接等正在跑的那一轮 |
| `CheckAsync(config, onProgress, token)` | 只想先问一句"要不要更新 / 多大"（WIFI 提示、大包确认）时用；**不下任何东西** |
| `UpdateAsync(check, onProgress, token)` | 玩家确认后按 `check` 执行更新；必须把 `CheckAsync` 的返回值原样传进来 |
| `WaitReadyAsync()` | **不发起新热更，只等待**正在进行的那一轮结束（首场景的业务在加载资源前调它；没发起过则返回 `NotRun` 失败） |
| `Install()` / `Dump()` | 手动装钩子（少用）/ 排查时打一份状态快照 |
| `Progress`（事件） | 自动订阅进度的入口：任何入口发起的热更都会往这里推（和回调参数二选一即可） |
| `State` / `LastError` / `LocalResVersion` | 轮询状态 / 最近一次失败原因 / 当前生效的资源版本 |

### 分步用法（UI 自己掌控）

```csharp
// ① 只检查：拿到"有没有更新 / 要下多少"
RevHotCheckResult check = await RevHotUpdate.CheckAsync(config);
if (check.Success == false) { /* 失败：旧版本照常可用 */ }
if (check.ForceUpdateRequired) { /* 大版本不匹配：走你们的强更流程（跳商店 / 公告） */ }
if (check.HasUpdate == false) { return; }

// ② 玩家确认（弱网/大包策略都在这层做）
if (check.TotalBytes > 50 * 1024 * 1024 && IsWifi() == false) { showTips(check.TotalBytesText); return; }

// ③ 下载 + 校验 + 落地（可取消：token 用框架的 RevCancellationTokenSource）
RevHotUpdateResult result = await RevHotUpdate.UpdateAsync(check, p => bar.Set(p.Percent, p.Text), tokenSource.Token);
```

### 可观察的状态

| 成员 | 用途 |
|---|---|
| `RevHotUpdate.State` | `Idle / Checking / Downloading / Verifying / Applying / Ready / Failed` |
| `RevHotUpdate.LastError` | 最近一次失败（分类 + 人话 + 定位细节），失败后仍保留，便于上报 |
| `RevHotUpdate.LocalResVersion` | 本地当前生效的资源版本（首次启动为空） |
| `RevHotUpdate.Dump()` | 一行行打印状态、错误、路径快照（贴给同事排查用） |
| `RevHotProgress.Text` | 已拼好的人话（`正在更新 3/12 · 12.4 MB / 48.1 MB · 2.3 MB/s`），直接给 UI |

---

## 四、配置字段表

只需要填 `RemoteRoot` 就能跑；下表是"什么时候该改哪一项"。

| 字段 | 默认 | 说明 / 什么时候要改 |
|---|---|---|
| `RemoteRoot` | 无（**必填**） | 远端根地址，如 `https://cdn.example.com/gameA`。**换环境 = 换这一个字符串** |
| `FallbackRoots` | 空 | 备用源（主源失败自动切换）。安卓可填对象存储直连域名；**小游戏别配**（合法域名名额有限，容灾放 CDN 侧） |
| `Env` / `Channel` | 空 | 目录里的环境段 / 渠道段，留空即不出现在 URL 里 |
| `ExtraHeaders` | 空 | 附加请求头（临时令牌等）。★ 客户端**不要**放云厂商长期密钥 |
| `AppVersionOverride` | 空 | 覆盖大版本（默认取 `Application.version`），一般不用改 |
| `PlatformOverride` | 空 | 覆盖平台目录名（默认 PC / Android / iOS / WebGL 自动判定） |
| `LocalRootOverride` | 空 | 覆盖本地根（默认 `{persistentDataPath}/RevHotUpdate`）；想换盘位置时用 |
| `ResVersionOverride` | 空 | **调试用**：忽略本地基线、按全量重新拉这个版本 |
| `Concurrency` | 3 | 同时下载的文件数（1~8；手机上别超 4） |
| `TimeoutSeconds` | 30 | 单请求超时秒数 |
| `RetryCount` / `RetryBackoffMs` | 3 / 500 | 重试次数 / 退避基数（500ms → 1s → 2s → 4s） |
| `VerifyMode` | `Hash` | `SizeOnly`（最快，仅联调）/ `Hash`（尺寸 + SHA-256）/ `HashAndCrc` |
| `KeepVersions` | 2 | 保留几个历史资源版本（回滚用；多留一份多占一份磁盘） |
| `FirstPackageMode` | `CopyToLocal` | 首包处理：Android 必须 `CopyToLocal`；桌面 / iOS 可改 `ReadFromStreamingAssets` 省磁盘 |
| `VerifyBuiltinCopy` | `false` | 首包落地时是否做哈希校验（源是自家 APK，默认只比大小更快） |
| `ManifestFileName` | `RevHotManifest.txt` | 清单文件名（一般不改） |
| `BuiltinManifestFileName` | `RevHotBuiltin.txt` | 首包基线清单文件名（一般不改） |
| `LogTag` | `HotUpdate` | 走 `RevLog` 的 tag，Console 里按它过滤 |
| `AllowHttp` | `false` | 允许 `http://`（**仅内网联调**；正式必须 https） |
| `OnForceUpdateRequired` | `null` | 大版本不匹配时的回调（框架只"识别 + 回调"，怎么引导玩家由业务决定） |

---

## 五、出包与发布流程

1. **打 AB**：框架打包工具 → `AssetBundles/<平台>/`（开"拷贝到 StreamingAssets"把首包放进包体）；
2. **生成清单**：菜单 `Revolution.Tools / 热更新 / 热更清单窗口` → 填大版本 / 资源版本 → 点「生成清单」；
3. **自检**：同一窗口点「自检」——缺包、大小不符、依赖不闭环、清单没带映射表，都会当场列出来；
4. **上传内容**：把产物目录里的**包 + `ResMap.txt`** 传到 `{RemoteRoot}/{环境?}/{平台}/{大版本}/{渠道?}/{资源版本}/bundles|ResMap.txt`；
5. **最后上传清单**：把 `RevHotManifest.txt` 传到 `{RemoteRoot}/{环境?}/{平台}/{大版本}/{渠道?}/`（**不带资源版本**的那一层）；
6. **交给客户端**：客户端下次启动就会自己发现并更新。

配置侧的三条：

| 项 | 怎么配 |
|---|---|
| 缓存 | 资源路径设长缓存（内容不可变）；**清单路径必须 no-cache 或客户端带 `?t=`** |
| CORS | 允许 `GET` / `HEAD` / `Range` 与自定义 Header（小游戏必需，配错的现象是"网络错误"） |
| 小游戏白名单 | 域名要在公众平台配置 + 备案 + 有效证书；一个域名搞定所有环境（用路径区分） |

### 发布工具（三选一，都不用去云控制台手点上传）

| 工具 | 适合 | 位置 / 依赖 |
|---|---|---|
| **Revolution.Could**（WPF 可视化，主推腾讯云 COS） | Windows 上点几下就发布：分步引导、目标路径预览、进度与日志、可取消；上传前自动做两道安全预检（本地 SHA-256 对账 + 远端同路径内容比对，**同版本路径内容不同直接拒绝覆盖**） | 仓库根 `Revolution.Could/`：`dotnet run --project Revolution.Could` 或 `dotnet publish` 出单文件 exe；详细用法见 [`Revolution.Could/README.md`](../../Revolution.Could/README.md)。要求桶为**公有读 / 私有写** |
| **UploadToCos.cmd（腾讯云命令行）** | CI / 习惯命令行发版 | `Assets/Revolution.Demo/RevHotUpdate.Demo/UploadToCos.cmd`；依赖腾讯云官方 COSCLI（`coscli config init` 配密钥，密钥不进 Unity 工程） |
| **UploadToOss.cmd（阿里云命令行）** | 用阿里云 OSS 的项目 | 同目录 `UploadToOss.cmd`；依赖 ossutil |

三个工具都内置同一条顺序纪律：**内容（AB 包 + `ResMap.txt`）先传并设 `Cache-Control: immutable` 长缓存；`RevHotManifest.txt` 最后传并设 `no-cache`** —— "先传内容、后传清单"不会搞反，SDK 密钥也都不会写进 Unity 工程或提交到 Git。
上传完成后记得两件 CDN 侧的事：**清单 URL 刷新缓存**（否则边缘节点一直回旧清单，玩家收不到新版本）；WebGL / 小游戏在桶上配好 **CORS**（GET + HEAD）。

---

## 六、本地目录长什么样 / 怎么回滚

```text
{persistentDataPath}/RevHotUpdate/
├── current.txt                              ← 当前生效的资源版本（原子替换；删掉它 = 回到出包状态）
├── _builtin/{平台}/<包名>                    ← 首包落地（Android 从 APK 拷出来的那份）
└── {平台}/{大版本}/{资源版本}/
    ├── .stamp                               ← 完成标记（有它才允许被加载）
    ├── RevHotManifest.txt                   ← 该版本清单副本（下次差量计算的基线）
    ├── ResMap.txt                           ← 热更映射表
    └── bundles/<包名>
```

**回滚**（同一大版本内）：把 `current.txt` 改成上一个版本号（或让业务加个"回滚"按钮调你们的落地逻辑）→ 下次启动就加载旧版本。旧版本目录由 `KeepVersions` 保留。

**最坏兜底**：直接删掉整个 `RevHotUpdate` 目录 → 下次启动按"出包状态"跑（内置资源），Android 会重新落地首包。

---

## 七、平台差异

| 平台 | 首包（内置资源）从哪读 | 热更包从哪读 | 备注 |
|---|---|---|---|
| **Android** | **必须先落地**到 `_builtin/{平台}/`（APK 里的 `jar:` 路径不是文件，`LoadFromFile` 读不了） | 持久化版本目录 | 首次启动会拷贝一次，**幂等**（大小一致就跳过）；因此不要配 `ReadFromStreamingAssets`（配置校验会直接拦下） |
| **iOS** | 直接读 `StreamingAssets`（真实文件目录） | 持久化版本目录 | 可把 `FirstPackageMode` 改成 `ReadFromStreamingAssets` 省磁盘；审核口径：只做资源，不做代码 |
| **PC / 编辑器** | 直接读 `StreamingAssets` | 持久化版本目录 | 编辑器里默认走"直读资源"模式，**想验证热更要在打包工具里打开 AB 模式** |
| **小游戏 / WebGL** | 没有"包内 AB"（代码包体积受限），资源全在 CDN | **直接是 CDN 的版本化 URL** | 没有文件系统：不落地、不续传、不做文件级校验；缓存与校验交给引擎（清单里的 `unityHash` / `unityCrc` 就是给它的） |

---

## 八、常见问题（FAQ）

| 现象 | 先查什么 |
|---|---|
| **"能不能不要我调用，框架自己自动热更？"** | 当前刻意不做：自动意味着"配置从哪来、失败了谁处理、什么时机下载"都要框架替业务决定，而这三件事每家项目答案不同。保留一个显式入口（`InitializeAsync`），这三件事都归业务管 —— 且它本身就是"一次调用完成全部"，放进你们统一的启动流程即可 |
| **"首场景的业务怎么等热更结束？"** | 调 `await RevHotUpdate.WaitReadyAsync()`：不发起新热更、只等正在进行的那一轮；没人发起过会返回 `NotRun`（提示你还没接入） |
| `ManifestFetchFailed`（拉不到清单） | ① `RemoteRoot` 拼出来的 URL 对不对（用浏览器直接打开那个 URL）；② 清单是否真的上传了；③ 小游戏的域名白名单 / CORS |
| `ManifestInvalid`（清单格式错） | 是不是把"上传到一半"的清单读到了；用记事本打开远端清单，看首行是否有 `@appVersion` 与 `@resVersion` |
| `AppVersionMismatch`（要求更新客户端） | 远端清单的 `@appVersion` 与包体 `Application.version` 不一致 —— 这是**设计如此**（跨大版本必须重新出包）；跨大版本发布时记得把清单放到新大版本的目录里 |
| 更新成功但资源没变 | ① 清单里的 hash 有没有真的变（改了资源要重新打包 + 重新生成清单）；② 客户端是不是走 AB 模式（编辑器直读模式看不到热更效果）；③ `Dump()` 看 `LocalResVersion` 是不是新版本 |
| 断点续传没生效 | 服务器是否支持 `Range`（不支持时框架会**自动回退成整包重下**，日志里有写）；CDN 侧有没有把 Range 关掉 |
| `VerifyFailed` | 服务器上的包与清单里的 hash 不一致（多半是"传了包没重新生成清单"或"传错了目录"）；框架已自动删半成品重下过，仍失败才报错 |
| `DiskFull` | 本地可用空间不足（框架会预留 64MB 余量）；清理 `RevHotUpdate` 旧版本或换 `LocalRootOverride` |
| Android 上首包加载失败 | ① `FirstPackageMode` 是不是被改成了 `ReadFromStreamingAssets`；② 首启落地是否被中断（重启游戏会自动续上） |
| 小游戏上报"网络错误" | 99% 是 CORS 或域名白名单；先确认 `RevHotManifest.txt` 能在浏览器里直接打开 |
| 编辑器里怎么验证热更 | 打包工具里打开 **AB 加载模式** → 起个本地静态服务当 CDN（`RemoteRoot = "http://127.0.0.1:8000/..."` + `AllowHttp = true`）→ 改一张图重新打包生成清单 → 重启 Play 观察只下变化的包 |

---

## 九、错误码表

| `RevHotErrorCode` | 人话 | 现状 / 该做什么 |
|---|---|---|
| `ConfigInvalid` | 配置不对 | 启动就报，看消息里点出的字段；旧版本不受影响 |
| `ManifestFetchFailed` | 清单拉不下来 | 网络 / CDN / 路径问题；可重试（换源也会试） |
| `ManifestInvalid` | 清单内容不对 | 上传流程问题，客户端保持旧版本 |
| `AppVersionMismatch` | 需要更新客户端 | 走业务强更流程 |
| `DownloadFailed` | 文件下载失败 | 所有源与重试都用尽；`Error.Detail` 里有最后的 URL 与 HTTP 错误 |
| `VerifyFailed` | 校验不通过 | 尺寸或 hash 对不上（已自动重下过） |
| `DiskFull` | 磁盘空间不足 | 清理空间后重试 |
| `Cancelled` | 被取消 | 已下载内容保留，下次继续 |
| `ApplyFailed` | 切换版本失败 | 写指针 / 清理旧版本出错；旧版本仍可用 |
| `NotRun` | 还没执行过热更 | 调了 `WaitReadyAsync` 但没人发起过热更 —— 按《〇》接入即可 |
| `Unexpected` | 预期外异常 | 带 `Dump()` 反馈 |

---

## 十、接入自查清单

1. 远端 `RemoteRoot` 拼出来的清单 URL，浏览器能直接打开；
2. 清单里 `@appVersion` == 包体 `Application.version`；
3. Android 用默认 `CopyToLocal`（别改成直读）；
4. `InitializeAsync` 在加载任何业务资源**之前**；
5. 清单路径 no-cache、资源路径长缓存；
6. 上传顺序"内容先、清单后"；
7. 弱网/大包提示用的是 `check.TotalBytesText`（别自己算）；
8. 失败分支有 UI 提示 + 允许重试（用 `result.Error.Message`，它已经是人话）。

---

*对应代码版本：运行时 `Assets/Revolution.HotUpdate/Runtime/`（18 个 `.cs` / 3309 行）· 编辑器 `Assets/Revolution.HotUpdate/Editor/`（2 个 `.cs` / 427 行）· 框架侧两个钩子（`RevABLoader.BundlePathResolver` / `RevResBootstrap.ResMapOverride`，默认 `null`）。*
*demo：`Assets/Revolution.Demo/RevHotUpdate.Demo/`（示例工程在 `demo` 分支：`git clone -b demo https://github.com/Yokino337088/Revolution.git Assets/Revolution.Demo`）。*
