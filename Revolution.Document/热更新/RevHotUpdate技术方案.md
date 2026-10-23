# RevHotUpdate · 资源热更新系统技术方案

> 定位：给 Revolution 框架补上"AB 包从远端下载 + 版本管理 + 加载路径重定向"的能力。
> 形态：**独立包**（目录 `Assets/Revolution.HotUpdate/`，程序集 `Revolution.HotUpdate`）—— 导入即有、不导入零影响。
> 对标：YooAsset 的 **Online 模式**这一条最常用路径，其余全部砍掉；不引任何第三方依赖。
>
> 本文回答两个问题：**① 这事能不能干（可行性）；② 具体怎么干（实现方案）**。
> 所有"现状"结论都带代码出处（文件:行），所有"要新增"的地方都标了 ⚠️。
>
> **阅读约定（2026-10-13 源码复核）**：第〇～十八章保留了立项时的论证、示意代码和目标验收，**不是当前实现保证**；其中“2 个钩子”“天然回滚”“杀进程后续传”“发布自检已校验 hash”等说法已与代码不符。以本文件新增的[第十九章：王者源码对照与修订后的实施方案](#十九王者源码对照与修订后的实施方案)为当前决策与验收依据，冲突时以第十九章为准。`Assets/Revolution.HotUpdate/` 已实现基础资源更新；本次仅修订方案，**没有修改或验证运行时代码**。
>
> 本次只借鉴 `F:\system\Program\GameScripts` 的资源更新链路；其脚本/数据热修复另有链路，不能写成 `RevHotUpdate` 已支持代码热更。使用方法见《RevHotUpdate 使用说明》，既有架构见《RevHotUpdate 架构解析》。
>
> **后续实施状态**：P0/P1 已开始落地到运行时代码（清单门禁、完整快照/来源哈希、候选校验/恢复、Range 续传与发布自检）；下文记录的是**设计目标和实施前风险快照**，不是 Unity/真机验收报告。上线前仍须逐项执行 19.4 的故障注入与真机验证。

---

## 目录

- 〇、结论速览
- 一、需求拆解与边界
- 二、现状盘点：框架已经给了什么、缺什么
- 三、三个必须承认的硬事实（可行性的关键判断）
- 四、总体架构
- 五、核心设计逐项论证
- 六、对外 API 草案
- 七、开发计划（4 阶段 + 验收标准）
- 八、验证方案
- 九、风险与对策
- 十、与 YooAsset 的对照
- 十一、框架侧需要的最小改动（含 diff 草案与零改动退路）
- 十二、目录与产出物清单
- 十三、需要拍板的 5 个决策
- **十四、按包标记来源的简化方案（第一档：分工层）** ← 推荐先落地（★ 主推 Android/小程序 时可再砍一层，见第十七章）
- **十五、历史推荐方案（以第十九章修订版为准）** ← 保留旧方案供对照，勿当作当前实现保证
- **十六、版本模型：大版本锚定（大版本内热更，跨大版本重出包）**
- **十七、平台核实：Android / 小程序上还需要"路径重定向"吗？** ← 主推平台的必要性核实与方案瘦身
- **十八、存储与 CDN 选型（Android + 小程序）** ← 用哪家：结论 + 硬约束 + 云端配置清单
- **十九、王者源码对照与修订后的实施方案** ← 2026-10-13 当前决策、真实实现差距、优先级与验收门槛
- 附录 A：清单样例全文
- 附录 B：一次完整热更的日志长什么样
- 附录 C：实施顺序建议（最不容易返工的顺序）

---

## 〇、结论速览

| 维度 | 结论 |
|---|---|
| **可行性** | ✅ **高**。框架的 AB 链路已经完成了热更最难的部分（依赖递归、包级引用计数、并发合并、异步加载），热更包只需要补"清单 + 下载 + 版本目录 + 路径重定向"四件事 |
| **要不要改框架** | **已接入 4 个默认 `null` 的钩子**：包路径、WebGL 缓存键、依赖覆盖、ResMap 覆盖；第十一章“2 处钩子”是旧草案，不得作为实施清单（见 19.2）。 |
| **代码量估算** | 运行时 ≈ 1600 行（含约 35% 注释）+ 编辑器工具 ≈ 600 行 + demo/文档 |
| **工期估算** | 阶段 1（可用最小闭环）3~4 天；到"能上项目"12~15 天（含真机验证与文档） |
| **第三方依赖** | ✅ **0 个**。只用 Unity 内置 `UnityWebRequest` + 框架自带 `RevTask`；清单用自研行式文本（连 JSON 库都不需要） |
| **业务改造量** | ✅ **资源加载 API 一行不改**（仍是 `RevResManager.Load/LoadAsync/Release`）；只在启动流程加 1 个 `await RevHotUpdate.InitializeAsync(...)` |
| **最大价值** | 不需要把业务迁到 YooAsset 的资源 API；热更是"加一层"，不是"换一套" |
| **最大风险** | iOS 审核合规（纯资源热更 OK，代码热更不行 —— 本方案不做代码热更，方向一致）；Android 首包的 jar 路径问题（第三章 3.3、14.6 有解） |
| **推荐起步方式** | ⭐ **先做第十四章的"分工层"**（编辑器里标哪些包走热更 + 持久化优先/内置兜底）—— 它是本文档阶段 1~4 的真子集，3~4 天可独立验收，后续加下载层不返工 |
| **最终推荐** | **第十九章修订方案为准**：先建立“能证明整版可加载”的门槛，再谈回滚、分包和发布自动化；第十五章是历史决策（表中实际有 10 条）。 |
| **版本模型** | **保留本项目的 App 大版本锚定**（第十六章）以隔离资源兼容窗口；王者源码体现的是 App/主资源包分层与 internal 包基准，**不是**可证明的“任意旧版指针回滚”或与 `Application.version` 完全相同的模型（见 19.1）。 |
| **存储/CDN** | ⭐ **对象存储 + CDN**，首选 **腾讯云 COS + CDN**（与微信小游戏同生态、国内社区实践最多），次选阿里云 OSS + CDN；客户端只认"一个 HTTPS 域名 + 路径模板"，**不引任何云 SDK**（第十八章） |

**一句话**：这不是"再造一个 YooAsset"，而是**把框架 README 里已经写明的那条扩展路（`IRevResPolicy` + `IRevResLoader`）真正做出来**，并且做得连策略都不用换 —— 只换"包从哪来"。

---

## 一、需求拆解与边界

### 1.1 你提的 7 件事，逐条落到能力

| 需求 | 落地成什么 | 归属 |
|---|---|---|
| ① 重定向 AB **下载路径** | 配置里的 `RemoteRoot` + `FallbackRoots`（多源降级），一个字符串换环境（测试/预发/正式） | RevHotUpdate |
| ② 重定向 AB **加载路径** | 运行时"**持久化目录优先 → StreamingAssets 兜底**"的路径解析（WebGL 直接给 CDN URL） | RevHotUpdate + 框架 1 个钩子 |
| ③ 从**腾讯云 COS / 阿里云 OSS** 下载 | 只做"HTTPS 直链下载 + 签名 URL + 自定义 Header"；**不引任何云厂商 SDK**（见 5.9） | RevHotUpdate |
| ④ 对标 **YooAsset** | 取它的 Online 模式：清单 → 版本比对 → 差量下载 → 校验 → 落地 → 加载。砍掉收集器/多运行模式/报告系统/加密/灰度（见第十章对照） | RevHotUpdate |
| ⑤ **轻量** | 无第三方依赖、清单是行式文本、下载落盘不占内存、复用框架的包管理与异步 | RevHotUpdate |
| ⑥ **热插拔**（导入就有、不导入零影响） | 独立 asmdef + 零静态构造 + 框架钩子默认 null + 卸载即删目录（见 4.3） | 设计约束 |
| ⑦ **基于框架现有 AB 管理方式** | 复用 `RevABLoader`（包级引用计数 / 依赖递归 / 并发合并）+ `RevABResPolicy`（映射表）+ `RevResManager`（资源级引用计数） | 集成方式 |

### 1.2 明确不做（边界清单，附理由）

| 不做 | 理由 |
|---|---|
| ❌ **代码热更**（HybridCLR / ILRuntime / 脚本热修复） | 与资源更新是独立的执行与安全边界。王者项目存在 Puerts 模块覆盖与数据热修复的独立入口，但不能推出已加载模块立即重新执行，更不能推出 Unity/IL2CPP 可直接热换 C# 程序集；本包不提供代码执行能力（见 19.5）。 |
| ❌ AB **加密 / 防篡改签名** | AB 本来就是可解包的二进制，加密只提高门槛、不解决根本问题，且会让"体积/加载耗时/排查难度"三样一起涨。需要时作为独立可选模块（钩子已留） |
| ❌ **二进制级差量**（bsdiff / 按 chunk 打补丁） | 收益不稳定（LZ4 已经块压缩，改一张图就会波及多个块），实现与验证成本极高。**做包级差量**：只重下"内容变了的包"（5.3） |
| ❌ **灰度发布 / 分渠道分流** | 属于运营后台能力。本方案只提供 `Channel` 目录段（`.../<平台>/<渠道>/`），分流策略由你们的 CDN/服务端做 |
| ❌ **依赖 COS/OSS 的 ListObjects** | 对象的"目录列表"要么要权限、要么有延迟，且无法表达"原子切换" —— **清单是唯一真相**（5.2） |
| ❌ 运行时多线程下载 | 主线程发 `UnityWebRequest` 即可（Unity 内部自带 IO 线程）；重活（大文件 hash）用"分帧流式"处理，避免卡帧（5.5） |

---

## 二、现状盘点：框架已经给了什么、缺什么

### 2.1 ✅ 已具备（都有代码证据，可直接复用）

| 能力 | 出处（相对 `Assets/Revolution/`） | 对热更的价值 |
|---|---|---|
| **逻辑名 → 包名+资源名 的映射表** | `Resources/ResourceSystem/ResMap.txt`（每行 `逻辑名\|包名\|资源名`）；解析在 `Runtime/RevResourceSystem/Support/RevResBootstrap.cs:100-127` | 热更的"资源坐标"只需重定向到新表，**业务路径不变** |
| **策略链（责任链 + 兜底）** | `Interfaces/IRevResPolicy.cs`（`Match` / `MapPath` / `CreateLoader` / `AllowFallback`） | 天然支持"新包找不到 → 落回旧包/Resources"的降级 |
| **AB 策略** | `Implementation/Policies/RevABResPolicy.cs:38-74`（构造 = 映射表 + 加载器；除 `Res/` 前缀外全接管；`AllowFallback = true`） | 可直接 `new RevABResPolicy(热更表, loader)` 复用 |
| **AB 加载器（核心资产）** | `Implementation/Loaders/RevABLoader.cs`：包级引用计数 `AcquireSingle/ReleaseSingle`(:164-218)、依赖递归 `AcquireBundle`(:131-151)、并发合并 `_loading`(:49,:478+)、异步统一 `RevTask`(:284,:307)、主包 Manifest(:90-109,:390-433)、平台分派(:113-128,:442-459) | **热更最难的部分已经写完了**：我们只需要让它"从哪读"可变 |
| **资源级引用计数 / 分组 / 缓存 / 延迟释放** | `Core/RevResManager.cs`（`RegisterPolicy` :55、缓存 `_cache`、`UnloadGroup`、`FlushUnused` :486、`UnloadAll` :645） | 业务 API 完全不变，内存纪律沿用现成的一套 |
| **包释放的挂钩点** | `Core/RevResManager.cs:671-681` → `handle.BundleLoader.ReleaseBundle(bundleName)`；字段 `Core/RevResHandle.cs:41-43` | **关键约束**：包释放依赖 `handle.BundleLoader`（具体类型 `RevABLoader`）—— 这决定了"必须复用框架的加载器"（见 3.1） |
| **启动装配** | `Support/RevResBootstrap.cs:62-98`（`ClearAllPolicies` → 注册策略 → 启动自动卸载门卫） | 热更包只需在 `Init()` 之前设好钩子，装配逻辑原样复用 |
| **打包工具链** | `Editor/RevResourceSystem/ABTool/`：`Pipeline/ABBuilderCore.cs`（打包 + 拷 StreamingAssets）、`ABManifestWriter.cs`（写 `ResMap.txt` + `BuildManifest.json`）、`ABDependencyAnalyzer.cs`、`ABCIBuild.cs`（无头 CI）、`Core/ABBuildSetting.cs:31,66-87`（`AssetBundles/<平台>` 与平台名规则） | 热更清单由**我们的编辑器工具从这些产物里生成**，框架工具一行不改 |
| **异步 / 取消 / 日志** | `Runtime/RevTask/`（`RevTask<T>`、`RevTaskCompletionSource<T>`、`WhenAll`、`Delay`：`RevTask.cs:64-95`；`RevCancellationToken`：`RevCancellation.cs:31-54`；`await AsyncOperation`：`RevTaskUnityExtensions.cs:28-40`）、`RevLog` | 下载层的并发/重试/超时/进度全部建立在既有异步设施上，不用引 UniTask |
| **无第三方依赖** | `Packages/manifest.json` 已含 `com.unity.modules.unitywebrequest` / `unitywebrequestassetbundle` | 下载能力开箱可用 |

### 2.2 ❌ 缺的（本方案要补的，全在"包外"）

| 缺口 | 影响（今天的实际表现） | 本方案的补法 |
|---|---|---|
| **加载根目录写死** | `RevABLoader.cs:61` 的 `StreamingRoot` 是 `private static`，只指向 `StreamingAssets/<平台名>/`；`LoadBundle`(:113) / `LoadBundleAsync`(:442) 是私有静态 —— 外部无法重定向 | 框架加 1 个静态钩子 `BundlePathResolver`（11 章 diff），热更包注入"本地优先"解析 |
| **无下载器 / 无 HTTP 层** | 全仓库只有 WebGL 分支里一处 `UnityWebRequest`（读本机 StreamingAssets） | 新增 `RevHotDownloader`（并发、重试、退避、超时、多源、Range 续传） |
| **无版本清单（无 hash / crc / 依赖字段）** | `BuildManifest.json`（`ABManifestWriter.cs:56-70`）只有 `version/platform/buildTime/name/size` | 新增 `RevHotManifest`：包级 `hash/size/deps/tags` + `ResMap` 也作为热更文件登记 |
| **无版本目录 / 无回滚 / 无断点续传** | 没有任何 `persistentDataPath` 相关代码（只有 `RevLog` 落盘用） | `RevHotStore`：版本目录 + `current` 指针 + `.tmp` + 原子提交 + 保留 2 版（5.1） |
| **ResMap 不能热更** | 它在包体的 `Resources` 里（只读）→ **"新增资源"这件事根本做不到** | 框架加 1 个钩子 `ResMapOverride`，热更表覆盖内置表（5.7） |
| **Android 首包读不出来** | `RevABLoader.cs:119-121` 在 Android 上用 `AssetBundle.LoadFromFile(streamingAssetsPath + ...)`，而 Android 的 StreamingAssets 在 **APK 内部**（`jar:file://...`），不是真实文件路径 | 首包**落地方案**：首次启动把内置包用 `UnityWebRequest` 拷到 `persistentDataPath`（5.1.4），此后一切加载都走持久化目录 |
| **无失败可观测** | 失败原因枚举 `RevResLoadErrorReason`（`Core/RevResDefine.cs:96-106`）是框架固定的 8 个值，热更的"校验失败/磁盘满/清单过期"塞不进去 | 热更自有 `RevHotError` 枚举 + `RevLog`（tag `HotUpdate`）+ `Dump()`；对框架只映射成 `BundleLoadFail` |

### 2.3 一句话总结现状

> **缺的全部在"包外"（清单 / 下载 / 磁盘 / 路径），"包内"的加载逻辑（依赖、引用计数、异步、卸载）可以原样复用。**
> 这就是"轻量"的底气 —— 我们不是在写资源框架，只是在给现有资源框架装一个"进料口"。

---

## 三、三个必须承认的硬事实（可行性的关键判断）

### 3.1 事实一：复用框架加载器 = 需要一个"路径钩子"，而且这是**唯一**的正确接法

证据链（三步）：

1. `RevABLoader.LoadBundle(string)` / `LoadBundleAsync(string)` 是 **private static**，路径由 **private static** `StreamingRoot` 拼出（`RevABLoader.cs:61,113,442`）→ 外部无法重定向；
2. 包级引用计数、依赖递归、并发合并、异步全在 `RevABLoader` 内部（`_bundles` / `_loading`）→ 重写一遍等于重写整个加载器（约 460 行，且要重做平台分派）；
3. 释放链路是 `RevResManager.ReleaseBundleOf(handle)` → `handle.BundleLoader.ReleaseBundle(name)`，而 `handle.BundleLoader` 的类型是**具体类 `RevABLoader`**、字段是 `internal`（`RevResHandle.cs:41-43`、`RevResManager.cs:671-681`）→ **自定义加载器无法参与这套生命周期管理**（连"把引用挂到句柄上"都做不到，`internal` 挡住）。

结论：
- **方案 A（推荐）**：框架给 `RevABLoader` 加一个静态钩子 `BundlePathResolver`（约 8 行，默认 null 行为不变）→ 热更包零侵入复用整条加载链路，`RevResBootstrap.ShutdownAll()` 也能正常释放我们的包；
- **方案 B（零改动退路）**：热更包自研 policy + loader，自己维护包级引用计数 —— 因为拿不到"资源释放"的回调（`BundleAcquired` 是 `internal`），只能自己扫 `RevResManager.CachedHandles`（公开）+ `handle.RealPath`（公开，格式 `包名|资源名`）反推存活集合，**在版本切换/回登录时手动触发包回收**。功能能跑通，但代码量约 +250 行、生命周期语义变模糊、且和框架的自动卸载门卫存在时序耦合。

### 3.2 事实二：ResMap 必须能热更，否则"新增资源"做不到

今天的链路：`RevResBootstrap.LoadResMap()` 从 **`Assets/Revolution/Resources/ResourceSystem/ResMap.txt`** 读表 → 这张表随包体发布、**运行时只读**。
如果热更只更新 AB 包、不更新映射表，那么"这次热更新增了一张 UI 图"这件事就没有任何办法被业务加载到（逻辑路径根本查不到）→ 产品上表现为"热更成功但新资源加载失败"。

所以 `ResMap.txt` **必须作为热更文件之一下发**，并在加载器建表时覆盖内置表（策略：**热更表优先 + 内置表兜底**，见 5.7）。这需要框架的第 2 个钩子（`RevResBootstrap.ResMapOverride`）。

### 3.3 事实三：Android 的首包不能直接 `LoadFromFile`（jar 路径问题）

- 桌面 / iOS：`Application.streamingAssetsPath` 是**真实文件目录** → `AssetBundle.LoadFromFile` 可用（框架现状 OK）；
- **Android**：`streamingAssetsPath == "jar:file:///data/app/<包名>.apk!/assets"` → `File.Exists` / `LoadFromFile` 都不是文件系统语义（框架 `RevABLoader.cs:119-121` 的注释也承认"File.Exists 不可用，直接尝试加载"，实际能否成功取决于 AB 是否被 APK 压缩，**不可靠**）；
- WebGL：没有本地文件概念 → 只能 `UnityWebRequest`（框架已按这个分支实现）。

**解法（本方案的核心设计之一）**：
> **首包也落地**。首次启动时，把内置在 StreamingAssets 里的首包 AB 用 `UnityWebRequest` 拷进 `persistentDataPath`（Android 上 `UnityWebRequest` 读 jar 内文件是可行的），此后**所有平台的加载路径统一为"持久化目录"**，`StreamingAssets` 只作为"首包来源"。
> 好处：一条加载路径打平三平台（桌面/iOS 也可以选择"不拷贝、直接读 StreamingAssets"以省磁盘，配置项控制，见 5.1.4）。

---

## 四、总体架构

### 4.1 分层与数据流

```text
                      ┌──────────────────────── 业务只碰这一层 ────────────────────────┐
                      │   await RevHotUpdate.InitializeAsync(config, onProgress)       │
                      │   （之后照旧：RevResManager.LoadAsync<Sprite>(路径, cb, 分组)）│
                      └───────────────────────────────┬───────────────────────────────┘
                                                      │
   ┌──────────────────────── 热更包 Revolution.HotUpdate（新增）────────────────────────┐
   │  门面 RevHotUpdate ── CheckAsync 检查更新 → UpdateAsync 下载 → Install 装钩子      │
   │      │                                                                           │
   │      ├─ 清单层 RevHotManifest（解析/序列化/版本比对；纯 C#，可工程外断言）          │
   │      ├─ 下载层 RevHotDownloader（并发 3 / 重试 / 退避 / 超时 / 多源 / Range 续传）  │
   │      ├─ 校验层 RevHotVerifier（尺寸 + SHA-256 流式分帧；可选复用 .manifest 的 CRC） │
   │      ├─ 存储层 RevHotStore（版本目录 / tmp / 原子提交 / current 指针 / 清理）       │
   │      └─ 桥接层 RevHotResBridge（设框架的 2 个静态钩子 → 触发一次 RevResBootstrap.Init）│
   └───────────────────────────────┬───────────────────────────────────────────────────┘
                                   │  钩子（默认 null，不装热更包时行为与现在完全一致）
   ┌───────────────────────────────▼───────────── 框架 Revolution.Runtime（不改加载逻辑）┐
   │  RevResBootstrap.Init → RevABResPolicy(映射表, RevABLoader) → RevResourcesResPolicy  │
   │  RevABLoader：依赖递归 / 包级引用计数 / 并发合并 / RevTask 异步 / 平台分派            │
   │  RevResManager：资源级引用计数 / 分组卸载 / 缓存 / 延迟释放                          │
   └─────────────────────────────────────────────────────────────────────────────────────┘

   远端（COS / OSS / CDN）：
     {RemoteRoot}/{环境?}/{平台}/{大版本}/{渠道?}/RevHotManifest.txt   ← 版本清单（唯一真相，最后上传）
     {RemoteRoot}/{环境?}/{平台}/{大版本}/{渠道?}/bundles/hero          ← AB 包（内容寻址，可长缓存）
     {RemoteRoot}/{环境?}/{平台}/{大版本}/{渠道?}/ResMap.txt            ← 映射表（热更它 = 支持新增资源）
     ★ "大版本"这一段是隔离墙：旧客户端的请求永远落在自己的目录里（见第十六章）
```

### 4.2 包目录结构（新增，与框架/示例并列）

```text
Assets/Revolution.HotUpdate/
├── package.json                          # 可选：UPM 发布用（name: com.revolution.hotupdate）
├── README.md                             # 3 分钟上手
├── Runtime/                              # asmdef: Revolution.HotUpdate（references: Revolution.Runtime）
│   ├── Facade/RevHotUpdate.cs            # 业务唯一入口：CheckAsync / UpdateAsync / Install / Progress
│   ├── Core/
│   │   ├── RevHotConfig.cs               # 配置（远端根 / 多源 / 并发 / 超时 / 校验 / 保留版本数…）
│   │   ├── RevHotManifest.cs             # 清单数据结构 + 解析 + 序列化（纯 C#）
│   │   ├── RevHotBundleInfo.cs           # 单个包：name/hash/size/deps/tags/builtin
│   │   ├── RevHotVersion.cs              # 版本号 + 平台 + minApp + 比较规则（纯 C#）
│   │   ├── RevHotPlan.cs                 # 差量计划：新增/变更/删除 三集合 + 需要下载的字节数
│   │   └── RevHotError.cs                # 失败原因枚举（热更自有，映射到框架的 BundleLoadFail）
│   ├── Download/
│   │   ├── RevHotDownloader.cs           # 并发调度 + 重试/退避 + 进度聚合
│   │   ├── RevHotSource.cs               # 下载源（主源 + 备用源，失败自动切换）
│   │   └── RevHotUrlBuilder.cs           # URL 拼装（远端根 + 平台 + 渠道 + 相对路径）
│   ├── Pipeline/
│   │   ├── RevHotVerifier.cs             # 校验（size / SHA-256 流式分帧 / 可选 CRC）
│   │   └── RevHotStore.cs                # 盘上布局：版本目录 / tmp / 原子提交 / 清理 / 首包落地
│   ├── Integration/
│   │   └── RevHotResBridge.cs            # 设框架钩子 + 调 RevResBootstrap.Init + 版本切换时的清理顺序
│   └── Support/
│       └── RevHotUpdateUnityHooks.cs     # 进 Play 复位钩子、清理残留 tmp（域重载纪律）
└── Editor/                               # asmdef: Revolution.HotUpdate.Editor（includePlatforms: [Editor]）
    ├── RevHotManifestBuilder.cs          # 读 AssetBundles/<平台> + *.manifest + ResMap.txt → 生成清单
    ├── RevHotUploadKit.cs                # 产出上传目录 + 生成上传命令（coscmd/ossutil/aws-cli 均可）
    └── Window/RevHotUpdateWindow.cs      # 面板：选平台/版本 → 生成清单 → 校验 → 打开目录 → 导出上传脚本
```

> 三处配套（可选但强烈建议，与既有工程习惯一致）：
> 1. ✅ **已落地**：`.github/workflows/sync-hotupdate-branch.yml`（照 `sync-package-branch.yml` 的写法，把 `Assets/Revolution.HotUpdate/` 同步到 `hotupdate` 分支，别人一条命令就能装上：
>    `git clone -b hotupdate https://github.com/Yokino337088/Revolution.git Assets/Revolution.HotUpdate`）；
> 2. ✅ **已落地**：`Assets/Revolution.Demo/RevHotUpdate.Demo/`（演示面板 + 联调场景 + 本机假 CDN 脚本 `起本地CDN.cmd`：
>    编辑器一键"打包 + 清单 + 装配本地 CDN"，服务器支持 Range 与两类缓存头 —— 无需真云就能跑通全链路）；
> 3. 本文档的 HTML 版（文档站可点开，md 先落地）。

### 4.3 热插拔的 4 条机制（"不导入就零影响"怎么保证）

| 机制 | 做法 | 不满足会怎样 |
|---|---|---|
| **独立程序集** | `Assets/Revolution.HotUpdate/` 自带 asmdef，`references: ["Revolution.Runtime"]`，反向零引用 | 混进框架程序集 → 删不掉 |
| **零静态构造 / 零 Update** | 门面是静态类，但只有业务调 `InitializeAsync` 才会动；不注册任何 `[RuntimeInitializeOnLoadMethod]` 之外的东西，且钩子里只做"复位" | 导入即产生启动开销 |
| **框架钩子默认 null** | 两个钩子都是 `public static Func<...> = null`，不设置时走原逻辑（并有断言守护"未设置时路径 = 旧行为"） | 框架行为被静默改变 |
| **卸载 = 删目录** | 删 `Assets/Revolution.HotUpdate/` + 删启动流程里那一行 `await RevHotUpdate.InitializeAsync(...)`，钩子再没人设 → 框架回到"只读 StreamingAssets" | 留一地残留 |

---

## 五、核心设计逐项论证

### 5.1 盘上布局与版本目录

#### 5.1.1 目录结构

```text
{persistentDataPath}/RevHotUpdate/                 ← 默认加载根（可配置 LocalRootOverride）
├── current.txt                                     ← 当前生效的版本号（一行文本；原子替换）
├── <平台>/                                         ← PC / Android / iOS / WebGL（WebGL 不用这条）
│   ├── 1.0.3/                                      ← 版本目录（只读语义：写完就不再改）
│   │   ├── RevHotManifest.txt                      ← 本次生效的清单副本（排查用，加载时读它）
│   │   ├── ResMap.txt                              ← 热更映射表（5.7）
│   │   ├── bundles/hero                            ← AB 包（文件名 = 包名，与框架一致）
│   │   ├── .tmp/hero.part                          ← 下载中的半成品（同一个包一个文件，天然续传）
│   │   └── .stamp                                  ← 版本完成标记（所有文件校验通过后才写）
│   └── 1.0.2/                                      ← 上一个版本（保留 N 份，默认 2；回滚用）
└── .lock                                           ← 单实例锁（防止两个进程同时写版本目录）
```

#### 5.1.2 三条纪律

1. **版本目录只写一次**：所有文件下载 + 校验完成 → 写 `.stamp` → 才更新 `current.txt`；**半成品版本永远不会被加载**（加载只认 `current.txt` 指向且带 `.stamp` 的目录）。
2. **`current.txt` 用"写临时文件 + 替换"的方式更新**（`File.Replace` / 先删后移），避免断电写出半行文本。
3. **清理规则**：启动时删掉 `临时目录` 里超过 7 天的 `.part`（上次没下完的），并保证"当前版本 + 上 1 个版本"之外的老版本目录被删（可配置 `KeepVersions`）。

#### 5.1.3 加载路径解析（钩子的实现）

```csharp
// RevHotStore.ResolveBundlePath（热更包内，静态方法 —— 不捕获实例，避免域重载后指向死对象）
internal static string ResolveBundlePath(string bundleName)
{
    // ① 当前生效版本目录里有 → 用它（热更下来的新包）
    string hot = Path.Combine(CurrentVersionDir, "bundles", bundleName);
    if (File.Exists(hot)) return hot;

    // ② 没有 → 首包兜底（桌面/iOS 的 StreamingAssets 是真文件；Android 已在首启落地到 persistentDataPath）
    return Path.Combine(FirstPackageDir, bundleName);   // 见 5.1.4
}
```

要点：
- **"本地优先 + 首包兜底"** 意味着**只下载变化过的包**，磁盘不再是"整份资源 × 2"；
- WebGL 下这个函数直接返回 `{RemoteRoot}/{平台}/bundles/{包名}`（远端 URL，框架的 WebGL 分支本身就吃 URL）→ **WebGL 连落盘都不需要**；
- 返回 `null` 表示"让它走框架默认路径"（存在这种场景：业务临时关掉热更）。

#### 5.1.4 首包（内置资源）的三种模式

| 模式 | 做法 | 适用 | 磁盘代价 |
|---|---|---|---|
| **A. 首包落地（Android 必选；也是默认）** | 首次启动：读内置清单 → 把 StreamingAssets 里的包用 `UnityWebRequest` 拷到 `persistentDataPath/RevHotUpdate/_builtin/<平台>/`（已存在且 hash 一致就跳过） | Android（jar 路径）；想统一三平台加载路径的项目 | 首包体积 × 2（可用"启动后清理"或"首包只放核心包"来降） |
| **B. 直接读 StreamingAssets** | 不拷贝，钩子返回 `streamingAssetsPath` 路径 | 桌面 / iOS（真实文件目录） | 0 |
| **C. 首包也为空（纯远端）** | 只把极小启动资源放进包体，其余全下 | 试玩包 / 小游戏 | 0 |

> **默认建议：A**。理由是"一条加载路径打平三平台"，排查时不需要区分"这个包是本地的还是热更的"；等出包优化阶段再对桌面/iOS 切 B（配置项一行）。

### 5.2 清单格式：为什么用行式文本，而不是 JSON

| 方案 | 体积 | 解析成本 | 依赖 | 人可读 | 结论 |
|---|---|---|---|---|---|
| JSON + `JsonUtility` | 大 | 中 | Unity 内置（但**不支持 Dictionary / 需要一堆 DTO 包装类**） | 一般 | ✗ |
| JSON + Newtonsoft | 大 | 中 | **第三方依赖**（框架坚持零依赖） | 一般 | ✗ |
| **行式文本（本方案）** | **小** | **低**（`Split('|')`） | **无** | **高**（和 `ResMap.txt` 同族） | ✅ |

选它还有两个工程上的理由：
1. 框架自己的映射表（`ResMap.txt`，`逻辑名|包名|资源名`）就是这个风格（`RevResBootstrap.cs:115-124`）——**同一套心智，不用记第二种格式**；
2. 出问题时"用记事本打开比一比"就能定位（`BuildManifest.json` 定位是"给人/CI 看"的，清单则是"运行时真正吃的"，可读性值钱）。

完整样例见 **附录 A**。核心字段：

```text
# RevHotManifest v1
@version|1.0.3
@platform|Android
@channel|official
@minAppVersion|1.0.0            ← 防"新资源配老包"（App 版本太低就强制更新包体）
@hashAlgo|sha256
@builtAt|2026-10-01 12:00:00
@bundleCount|128

@resmap|ResMap.txt|8f3c...|20481        ← 映射表本身也当热更文件（含 hash/size）
@bundle|hero|9a1b...|15728640|common,shader|builtin,hot
@bundle|ui_login|2c7d...|1048576|common|hot
...
```

### 5.3 版本比对与差量：算法（这是"只下变化的部分"的核心）

```text
输入：远端清单 R（刚下载的）、本地清单 L（= 当前版本目录里的清单副本；没有则视为空）
输出：RevHotPlan { Add[]  Change[]  Remove[]  TotalBytes  ResMapChanged }

对每个 R.bundle：
    L 里没有同名包            → Add（新增）
    L 里有，但 hash 不同       → Change（内容变了）
    L 里有，hash 相同          → 跳过（不下、不校验、不落盘）
对每个 L.bundle 而 R 里没有的 → Remove（标记为"下次清理时连同删除"）
R.resmap.hash != L.resmap.hash → ResMapChanged（必须下载，否则新资源加载不到）

TotalBytes = Σ(Add + Change 的 size)        → 用于给玩家显示"需要下载 XX MB"
若 TotalBytes == 0 且 !ResMapChanged        → 无更新，直接 Install 走人（零下载启动）
```

配套的产品化细节（都只有几行代码，但决定体验）：
- **WIFI / 流量提示**：`RevHotUpdate.CheckAsync` 返回的 `TotalBytes` 交给 UI 决定"是否现在就下"；
- **弱网/大包保护**：`Config.MaxBytesPerSession`（超过就本次不下，提示稍后）—— 可选；
- **失败可恢复**：没下完 / 校验失败 → 版本目录未打 `.stamp`，`current.txt` 没变 → **下次启动从 `Change/Add` 重新算一遍，已下好的包用 hash 比对跳过**（天然支持"暂停/继续"）。

### 5.4 下载器设计（轻量但该有的都有）

```csharp
// 核心：一个文件 = 一个 RevTask；并发由信号量控制；失败按指数退避重试；支持从 .part 续传
private async RevTask DownloadOneAsync(RevHotFileRef file, RevCancellationToken token)
{
    string part = _store.TempPathOf(file);
    long offset = RevHotStore.ExistingLength(part);           // 续传起点（0 = 从头下）
    string url  = _urlBuilder.Build(file.RemotePath);

    using (UnityWebRequest www = UnityWebRequest.Get(url))
    {
        if (offset > 0 && offset < file.Size)
            www.SetRequestHeader("Range", "bytes=" + offset + "-");   // COS/OSS 原生支持 Range
        else
            offset = 0;

        www.downloadHandler = new DownloadHandlerFile(part, append: offset > 0);  // ★ 落盘，不占内存
        www.timeout         = _config.TimeoutSeconds;
        await www.SendWebRequest();                            // RevTask 直接 await UnityWebRequestAsyncOperation
        if (www.result != UnityWebRequest.Result.Success)
            throw RevHotError.Network.Wrap(www.error, url);    // 上层决定换源 / 重试 / 中止
    }
    await _verifier.VerifyAsync(part, file, token);            // 尺寸 + hash（分帧，见 5.5）
    _store.Commit(part, file.LocalPath);                       // 原子 rename → 版本目录
}
```

| 特性 | 做法 | 说明 |
|---|---|---|
| 并发 | 默认 3（可配 1~4） | 手机端不要超过 4：CDN 限速 + 发热 + 抢占游戏 IO |
| 重试 | 默认 3 次，退避 0.5s→1s→2s | 只对网络类失败重试；**校验失败直接重下**（不带 offset） |
| 多源 | `Sources = [主源, 备用源]`；连续 N 次失败换源并**记住"哪个源健康"** | 换源不影响续传（URL 变了但 `.part` 保留） |
| 超时 | Cookie 超时 + 整体超时（`www.timeout`） | 大包用"无进度超时"（比如 30s 没收到数据就判死） |
| 进度 | `RevHotProgress { Percent, DownloadedBytes, TotalBytes, SpeedText, CurrentFile, FileIndex, FileCount }` | 聚合进度：完成文件字节 + 当前文件已下字节 |
| 磁盘预检 | 下载前 `DriveInfo.AvailableFreeSpace > TotalBytes * 1.2` | 磁盘满不该表现为"下到一半失败" |
| 取消/暂停 | `RevCancellationTokenSource`（框架自带） | 切后台/玩家取消 → `token.Cancel()`，`.part` 保留即可续传 |
| 内存 | `DownloadHandlerFile` 直接写盘 | 200MB 的包也不会出现 200MB 的内存峰值 |

### 5.5 校验：复用 Unity 已经算好的东西

两条路，**默认只做第一条**（够用且零成本）：

**① 复用 Unity 生成的信息（零成本）**
Unity 为每个包生成的 `<包名>.manifest`（纯文本）里已经带了 `CRC` 与 `Hashes.AssetFileHash`（MD5）。
我们的编辑器工具在生成清单时**直接读这些字段**填进 `@bundle` 行 —— 不需要自己实现 hash 计算，还能顺便用 `CRC` 做 `AssetBundle.LoadFromFile(path, crc, 0)` 的加载期校验（可作为可选钩子加在 `BundlePathResolver` 旁边）。
> ⚠️ 实施时先打包一次核对 `.manifest` 字段是否齐全（Unity 版本差异存在），缺字段就退到方案 ②。

**② 自己算 SHA-256（可选，推荐用于"防篡改 + 增量缓存"）**
用 `FileStream` 分块读（1MB 缓冲），每帧最多算 N MB（默认 8MB/帧）→ 避免几百 MB 的包一次性把帧卡住；进度可复用"下载进度条"的尾部阶段。

**校验策略配置**：

```csharp
public enum RevHotVerifyMode
{
    SizeOnly,      // 只比大小（最快，仅适合内网联调）
    Hash,          // 尺寸 + hash（默认；hash 算法取清单的 @hashAlgo）
    HashAndCrc,    // 再加 Unity 的 CRC（最严；加载期也校验）
}
```

### 5.6 加载重定向：框架侧钩子的完整 diff 草案

```csharp
// ===== 文件：Runtime/RevResourceSystem/Implementation/Loaders/RevABLoader.cs =====
/// <summary>
/// 包路径重定向钩子：参数 = 包名，返回 = 该包的完整路径（本地文件路径，或 WebGL 下的 URL）；
/// 返回 null / 空串 → 走默认的 StreamingAssets/&lt;平台名&gt;/&lt;包名&gt;。
/// ★ 热更包用它把加载指到 persistentDataPath 的版本目录；本框架自身不使用它。
/// ★ 不设置时（默认 null）行为与从前完全一致 —— 对不装热更包的工程是零影响。
/// </summary>
public static Func<string, string> BundlePathResolver { get; set; }

private static string ResolveBundlePath(string abName)
{
    string custom = BundlePathResolver?.Invoke(abName);
    return string.IsNullOrEmpty(custom) ? StreamingRoot + abName : custom;
}
```

然后把现有 **3 处** `StreamingRoot + abName` 换成 `ResolveBundlePath(abName)`：

| 位置 | 现状 | 改后 |
|---|---|---|
| `LoadBundle`（:119-127） | Android 分支 `LoadFromFile(StreamingRoot + abName)`、其他平台 `File.Exists` + `LoadFromFile` | `ResolveBundlePath(abName)`（保留 `File.Exists` 判断，因为它就是"存在性检查"） |
| `LoadBundleAsync`（:442-459） | WebGL 分支 `UnityWebRequestAssetBundle.GetAssetBundle(StreamingRoot + abName)`；其他平台 `LoadFromFileAsync(...)` | 同上（WebGL 分支此时能直接吃远端 URL，**顺带支持了 WebGL 的"不落地热更"**） |

> 附带的正确性说明：`LoadBundle` 里的 `File.Exists` 判断在 Android 分支被注释掉了（因为 jar 路径），改成钩子后**建议恢复统一判断**，因为热更路径是真实文件 —— 但这是"可选优化"，不影响本方案。

> ★ **想直接照抄"钩子怎么用"**（自己接 CDN / 自研映射表、装配时机、5 个新手坑）：见《RevHotUpdate 使用说明》§二 的
> 「这两个钩子怎么用（面向小白，含可照抄示例）」。本节给的是**框架侧 diff**，那边给的是**业务侧用法**。

### 5.7 ResMap 热更（让"新增资源"成立）

```csharp
// ===== 文件：Runtime/RevResourceSystem/Support/RevResBootstrap.cs =====
/// <summary>
/// ResMap 映射表的覆盖来源（热更包用它把"逻辑名 → 包名|资源名"换成热更版本，从而支持新增资源 / 改包）。
/// 返回 null → 只用内置表。★ 不设置时行为与现在完全一致。
/// </summary>
public static Func<Dictionary<string, string>> ResMapOverride { get; set; }

private Dictionary<string, string> LoadResMap()
{
    var map = new Dictionary<string, string>();

    // ① 内置表（随包体发布）—— 原逻辑不变
    TextAsset ta = Resources.Load<TextAsset>("ResourceSystem/ResMap");
    if (ta != null) { /* 原样解析：按行、Trim、跳过 # 与空行、Split('|') */ }

    // ② ★ 热更表覆盖内置表（同名键覆盖；内置表里独有的键保留）
    //    合并而不是替换，是为了"新资源能热更 + 老资源不用重下"两边都要：
    //    热更表本身就包含全部条目（编辑器工具生成的是全量表），合并只是给"清单损坏/不完整"留一层兜底。
    Dictionary<string, string> hot = ResMapOverride?.Invoke();
    if (hot != null) foreach (KeyValuePair<string, string> kv in hot) map[kv.Key] = kv.Value;

    return map;
}
```

热更表从哪来：`{版本目录}/ResMap.txt`（下载下来的），解析逻辑与框架 `RevResBootstrap` 完全一致（同样的格式、同样的 `#` 注释与 Trim 规则）。**解析这份表的代码有 2 份**（框架 1 份 + 热更 1 份）—— 建议后续把它抽成 `RevResMapReader` 供两边共用（属于"可选的小重构"，不做也能跑）。

### 5.8 生命周期与卸载：完全交给框架

因为走的是"复用 `RevABLoader`"的路线，**包级引用计数、资源的释放时机、版本切换时的清理顺序都由框架既有的机制负责**：

```csharp
// 版本切换（热更到新版本）时的正确顺序 —— 顺序错了会"卸载正在用的资源"
RevAsyncLoadPump.CancelAll();                 // ① 中断在途加载（否则新包还没落地就有人去读）
RevResPreloader.ReleaseAll();                 // ② 归还预加载持有
RevResManager.UnloadAll();                    // ③ 清资源缓存并归还包级引用
RevBootstrap 内部的 _abLoader.ReleaseAll();   // ④ 释放所有包 + 主包 Manifest（RevResBootstrap.ShutdownAll 已经做了 ①~④）
RevHotStore.Activate(newVersion);             // ⑤ 切 current.txt 指针
RevHotStore.DeleteOldVersions();              // ⑥ 删老版本目录（保留 KeepVersions）
RevResBootstrap.Instance.Init();              // ⑦ 重装策略（新清单/新映射表生效）
```

> 因为 `RevResBootstrap.ShutdownAll()`（`RevResBootstrap.cs:154-160`）已经封装了 ①~④，热更包只需要调它 + 做 ⑤~⑦ ——**"卸载"这件事我们一行新逻辑都不用写**。

### 5.9 对象存储适配（COS / OSS / CDN）

**原则：不引 SDK，只拼 URL。**

| 云 | 公读 URL 形态 | 私有桶（签名 URL） |
|---|---|---|
| 腾讯云 COS | `https://<bucket>-<appid>.cos.<region>.myqcloud.com/<key>` | 由**你们的服务端**用临时密钥签好 URL（`q-sign-algorithm=sha1&q-ak=...&q-sign-time=...&q-key-time=...&q-header-list=...&q-url-param-list=...&q-signature=...`），客户端只负责 GET |
| 阿里云 OSS | `https://<bucket>.oss-<region>.aliyuncs.com/<key>` | 同样由服务端签（`OSSAccessKeyId=&Expires=&Signature=`），或直接发 STS 给客户端自己签（不推荐把 AK 放客户端） |

配置长这样（**换环境 = 改一个字符串**）：

```csharp
new RevHotConfig
{
    RemoteRoot    = "https://cdn.example.com/gameA",     // 主源（一般是 CDN，回源到 COS/OSS）
    FallbackRoots = new[] { "https://bucket-1250000000.cos.ap-guangzhou.myqcloud.com/gameA" },  // 直连兜底
    Channel       = "official",                          // 可选：分渠道（.../<平台>/<渠道>/）
    ExtraHeaders  = new Dictionary<string, string> { ["x-game-token"] = "..." },  // 可选：临时令牌
}
```

三条实践纪律（都是踩过坑的）：
1. **清单别长缓存**：`RevHotManifest.txt` 请求带 `?t={unix秒}` 或让 CDN 对该路径设 `Cache-Control: no-cache`；包文件用**长缓存**（因为文件名/版本目录变了内容就变了，是 immutable 的）；
2. **上传顺序**：先传所有内容（包 + ResMap），**最后传清单** —— 清单是"原子切换开关"；反之会出现玩家拉到"半新半旧"的组合；
3. **CORS**（WebGL 必需）：COS/OSS 的跨域规则要放开 `GET`、`Range`、以及你们自定义的 Header，否则小游戏/网页端会以"网络错误"的形式失败（排查时非常费时间）。

### 5.10 安全与合规

| 项 | 做法 |
|---|---|
| 传输 | 强制 HTTPS（配置里只接受 `https://`，除非显式打开 `AllowHttp` 用于内网联调） |
| 完整性 | 尺寸 + hash（默认 SHA-256）；不匹配就丢弃重下，**绝不"带病加载"** |
| 防篡改 | hash 由你们的出包流程生成并上传到你们自己的桶 → 客户端只信任清单；**升级路径：清单签名**（服务端用私钥签清单尾部，客户端内置公钥验签，约 40 行，属于可选增强） |
| 密钥 | **客户端永不持有云厂商长期密钥**；私有桶一律用服务端签发的临时 URL |
| 审核（iOS） | 只做**资源**热更（不下载/不执行代码），符合常见审核口径；文档与代码里都要"看得见结论"（README 里明确写"不做代码热更"） |
| 不做加密 | 明确写在需求边界里（1.2）；需要时按 6.3 的"扩展钩子"另做 |

### 5.11 编辑器侧工具（清单生成是核心）

```csharp
// RevHotManifestBuilder：输入是"框架打包工具的产物"，输出是一份可直接上传的清单
//   输入：AssetBundles/<平台>/          （ABBuildSetting.GetOutputDir）
//         <平台>/<包名>.manifest         （Unity 原生清单：CRC / AssetFileHash / Dependencies）
//         <平台>/BuildManifest.json      （框架的产物清单：version / buildTime）
//         Assets/Revolution/Resources/ResourceSystem/ResMap.txt（框架生成的映射表）
//   输出：<输出目录>/RevHotManifest.txt + 复制一份 ResMap.txt + 校验报告
[MenuItem("Revolution.Tools/热更新/生成热更清单", false, 10)]
public static void Build() { ... }
```

面板（`RevHotUpdateWindow`）提供 5 个动作：

| 动作 | 做什么 |
|---|---|
| 平台 & 版本 | 选 `AssetBundles/PC|Android|iOS|WebGL`，填版本号（默认读 `ABBuildConfig.version`） |
| 生成清单 | 扫产物 → 生成 `RevHotManifest.txt`（含每包 hash/size/deps） |
| 自检 | 校验"清单里的每个包在磁盘上存在 + hash 一致 + ResMap 覆盖了所有包"（防止漏传/漏生成） |
| 打开上传目录 | 打开产物目录（可直接拖进 COS/OSS 控制台） |
| 导出上传脚本 | 生成 `upload.ps1` / `upload.sh`（调用 `coscmd` / `ossutil` / `aws s3 cp --recursive`），**先传包后传清单**的顺序已经写好 |

> 不内置云 SDK 的代价是"上传要装个 CLI"，收益是"引擎包里永远没有云厂商 SDK"。这条取舍写在文档里，用户可自行替换成自家 CI 管线。

### 5.12 可观测性（沿用框架的风格）

```csharp
RevHotUpdate.State          // Idle / Checking / Downloading / Verifying / Applying / Ready / Failed
RevHotUpdate.LastError      // RevHotError（含分类 + 人话消息 + 出错的 URL / 文件名）
RevHotUpdate.Dump()         // 一行行地打出：远端版本、本地版本、计划集合、每包状态（照 RevLog.Dump 的味道）
RevLog 出口                 // tag = "HotUpdate"；失败必带原因（不打日志也不静默失败）
```

失败分类（沿用框架"失败必带原因"的纪律）：

| `RevHotError` | 人话 |
|---|---|
| `ManifestFetchFailed` | 拉不到版本清单（网络/CDN/清单路径不对） |
| `ManifestInvalid` | 清单格式不对或版本号解析失败（上传流程出错） |
| `AppVersionTooLow` | 客户端版本太低，需要更新包体（`@minAppVersion`） |
| `DownloadFailed` | 某个包下载失败（含最后一次的 HTTP 错误与 URL） |
| `VerifyFailed` | 校验不通过（尺寸/hash），已重试 N 次 |
| `DiskFull` | 磁盘空间不足（预检拦下） |
| `Cancelled` | 玩家取消 / 切后台 |
| `ApplyFailed` | 切换版本失败（写 `current.txt` / 删旧版本失败） |

### 5.13 代码热更不是资源热更的自动延伸

`RevHotUpdate` 只负责 AB/ResMap 的版本、传输和加载来源，不接管脚本执行器。**不得把下载到的程序集当成资源直接 `Assembly.Load` 并声称支持 IL2CPP 热更**：这需要额外的运行时、程序集构建与平台合规评估。王者源码的 `CHotFixSystem`/Puerts 路径是独立的脚本与数据热修复系统，也不能等同于 C# 程序集替换；本期明确不实现。若未来单独立项，必须先定义代码签名/信任链、灰度/强更和平台能力，再设计与资源清单的兼容关系（详见 19.5）。

---

## 六、对外 API 草案（业务只用 3 个入口）

### 6.1 最省事（推荐，5 行）

```csharp
// 放在启动流程最前面（比 RevResBootstrap.Init 早 —— 热更包内部会替你把 Init 的时机安排对）
RevHotUpdateResult r = await RevHotUpdate.InitializeAsync(
    new RevHotConfig
    {
        RemoteRoot = "https://cdn.example.com/gameA",
        // 其余全默认：平台目录自动判定 / SHA-256 校验 / 并发 3 / 重试 3 次 / 保留 2 个版本
    },
    onProgress: p => loadingBar.Set(p.Percent, p.Text));      // p.Text 例如 "正在更新 3/12 · 12.4 MB / 48.1 MB · 2.3 MB/s"

if (!r.Success) { tips.Show($"更新失败：{r.Error}"); return; }   // r.Error 已是一句人话
// 之后照旧写业务：RevResManager.LoadAsync<Sprite>(RevResPath.UI_Icon, "Hero_1001", cb, RevResGroup.UI);
```

### 6.2 进阶（分步，UI 自己掌控）

```csharp
// ① 只检查（不下载）：拿"要不要更新 / 多大 / 什么版本"去做提示弹窗
RevHotCheckResult check = await RevHotUpdate.CheckAsync(config);
if (!check.HasUpdate) { RevHotUpdate.Install(); return; }

// ② 玩家确认后再下（WIFI 策略、大包提示都在这层做）
if (!check.ResMapChanged && check.TotalBytes > 50 * 1024 * 1024 && !IsWifi())
{
    if (!tips.Ask($"本次更新 {check.TotalBytesText}，建议在 WIFI 下进行")) return;
}

// ③ 下载 + 校验 + 落地（可取消：token 来自框架的 RevCancellationTokenSource）
RevHotUpdateResult r = await RevHotUpdate.UpdateAsync(check, tokenSource.Token, onProgress);

// ④ 生效（装钩子 + 重装策略；内部会先 ShutdownAll 再 Init）
if (r.Success) RevHotUpdate.Install();
```

### 6.3 配置对象（字段 + 默认值）

| 字段 | 默认 | 说明 |
|---|---|---|
| `RemoteRoot` | 无（必填） | 远端根，如 `https://cdn.example.com/gameA` |
| `FallbackRoots` | 空 | 备用源（按顺序降级） |
| `Channel` | `""` | 渠道目录段（`.../<平台>/<渠道>/`），留空则不分渠道 |
| `PlatformOverride` | `""` | 覆盖平台目录名（默认按运行时判定：`PC/Android/iOS/WebGL`，与 `ABBuildSetting.GetPlatformName` 保持一致） |
| `VersionOverride` | `""` | 强制目标版本（内网测试用：直接指定版本不再比对） |
| `LocalRootOverride` | `""` | 覆盖本地根（默认 `{persistentDataPath}/RevHotUpdate`） |
| `Concurrency` | `3` | 下载并发（1~4） |
| `TimeoutSeconds` | `30` | 单请求超时（含"无进度超时"） |
| `RetryCount` / `RetryBackoffMs` | `3` / `500` | 重试次数与退避基数（指数） |
| `VerifyMode` | `Hash` | `SizeOnly` / `Hash` / `HashAndCrc` |
| `KeepVersions` | `2` | 保留几个历史版本（回滚用） |
| `FirstPackageMode` | `CopyToLocal` | `CopyToLocal` / `ReadFromStreamingAssets` / `None`（见 5.1.4） |
| `CoreBundles` | 空 | "首包/最小包"包名清单（用于"首包瘦身 + 按需下载"进阶模式；留空 = 首包全量） |
| `ExtraHeaders` | 空 | 附加请求头（临时令牌等） |
| `AllowHttp` | `false` | 允许 http（仅内网联调） |
| `ManifestFileName` | `RevHotManifest.txt` | 清单文件名 |
| `LogTag` | `"HotUpdate"` | 走 `RevLog` 的 tag |

### 6.4 与框架的调用顺序（必须照这个顺序）

```text
App 启动
  │
  ├─ ①（可选）RevLog.EnableFileLog(...)          ← 热更期间日志落盘，真机排查靠它
  ├─ ② await RevHotUpdate.InitializeAsync(cfg)   ← 拉清单 → 比对 → 下载 → 校验 → 落地
  │        └─ 内部：RevHotResBridge.Install()
  │               ├─ RevABLoader.BundlePathResolver = RevHotStore.ResolveBundlePath
  │               ├─ RevResBootstrap.ResMapOverride   = RevHotStore.BuildResMap
  │               ├─ RevResBootstrap.Instance.Init()          ← 框架原样装配（策略 + 自动卸载门卫）
  │               └─ （若有更新）先 ShutdownAll() 再 Init()，确保新映射表/新包路径生效
  ├─ ③ 业务 Init（UI/场景/配置表…）              ← 此时加载的就是"热更后的资源"
  └─ ④ 进游戏
```

---

## 七、开发计划（4 阶段 + 验收标准）

| 阶段 | 内容 | 产出 | 验收标准（可量化） | 预估 |
|---|---|---|---|---|
| **1. 最小闭环** | 清单结构 + 解析（纯 C#）；本地目录布局；下载器（单文件、无重试）；哈希校验；`Install` 钩子接入；`InitializeAsync` | PC 真机（编辑器 AB 模式也行）能"从本地 HTTP 服务下一张图并加载出来" | ① 清单解析 + 版本比对 + 差集计算：**工程外纯 C# 断言 ≥ 40 条全过**；② PC 上"改一张图 → 重新打包 → 只下变化包 → 加载到新图"跑通 | 3~4 天 |
| **2. 生产可用** | 并发 + 重试/退避 + 多源 + Range 续传 + 磁盘预检 + 版本目录/`.stamp`/`current` + 保留版本与清理 + `Dump` | 断网可续、暂停可续、版本可回滚 | ① 破坏性测试 8 项全过（见 8.3）；② Android 真机首包落地 + 增量更新跑通；③ 400MB 包下载期间帧率不掉（分帧校验） | 4~5 天 |
| **3. 编辑器链路** | `RevHotManifestBuilder` + 自检 + 上传脚本导出 + 面板 | 一条"打包 → 生成清单 → 上传 → 客户端更新"的完整流水线 | ① 面板 3 个动作可用；② 自检能拦下"漏包 / hash 不符 / ResMap 缺条目"；③ 与 `ABCIBuild` 一起可在 CI 里无人值守跑 | 3 天 |
| **4. 打磨与交付** | ✅ demo + 本地 CDN 脚本（`Assets/Revolution.Demo/RevHotUpdate.Demo/`）+ README + 本文档的 HTML 版 + ✅ `hotupdate` 分支同步 | 别人 clone 下来 30 分钟能跑通 | ① ✅ demo 从"菜单①装配 CDN → 起本地CDN → 检查/执行更新 → 加载到新内容"全程可点；② ✅ 该目录 README 3 分钟上手；③ ✅ `sync-hotupdate-branch.yml` 已落地（产出 `hotupdate` 分支） | 3 天 |

> **不放进前 4 阶段的（按需再加）**：清单签名、按 tag 的按需下载、加密、灰度、`HashAndCrc` 加载期校验、`ResMapReader` 抽公共、WebGL 专项优化。

> ★ **实际推荐从"阶段 0"起步**：即第十四章的"**按包标记来源的分工层**"（编辑器里标哪些包走热更 + 持久化优先/内置兜底的路径解析）。
> 它是本文档阶段 1~4 的**真子集**（那边的东西它全保留），但能独立验收、3~4 天见效果，且不返工 —— 先跑通"两个根"，再加"下载与版本"。

---

## 八、验证方案

### 8.1 工程外纯 C# 断言（沿用框架既有习惯，跑完即删临时工程）

| 组 | 断言点 |
|---|---|
| 清单 | 行式文本解析（含 `#` 注释、空行、未知字段、字段数不足、BOM、`\r\n` 与 `\n`）；序列化往返一致；版本号比较（`1.10.0 > 1.9.0`） |
| 计划 | 新增/变更/删除三集合；hash 相同时**不下**；`ResMapChanged` 单独标记；`TotalBytes` 计算 |
| 存储 | 版本目录路径拼装；`.tmp` 续传起点；原子提交；`current.txt` 读写；老版本清理（保留 N） |
| URL | 平台目录名映射（与 `ABBuildSetting.GetPlatformName` 一致）；渠道拼接；多源切换；非法 `RemoteRoot`（缺协议、结尾斜杠）归一化 |
| 校验 | 尺寸先判（省一次 hash）；hash 不匹配报 `VerifyFailed`；空文件/截断文件的处理 |
| 错误 | 每类失败都有人话消息（**不允许空消息**，照 GM 模块的纪律） |

### 8.2 真机验收清单

| 平台 | 验收点 |
|---|---|
| PC（编辑器 AB 模式 + 独立包） | 全链路；切换 `RemoteRoot`；无更新启动耗时（应为毫秒级） |
| Android | 首包落地（jar → persistentDataPath）；增量更新；杀进程后续传；升级覆盖安装 |
| iOS | StreamingAssets 直读模式；增量更新；审核说明文档（只做资源热更） |
| WebGL（若需要） | 直接吃 CDN URL（不落地）；CORS 配置正确；`.br`/`.gz` 压缩传输（CDN 侧） |

### 8.3 破坏性测试（必做 8 项）

1. 下载中**断网** → 重连后从断点继续（`.part` 长度对得上）；
2. 下载中**杀进程** → 下次启动自动续传；
3. 手动**改坏一个已下好的包** → 校验失败 → 重下 → 成功；
4. 手动**篡改清单里的 hash** → `VerifyFailed`（不静默加载）；
5. **磁盘写满** → `DiskFull`（预检拦下，不留半成品版本）；
6. 远端清单**只有半个文件**（上传中被读到）→ `ManifestInvalid`，且**旧版本照常能玩**（`current.txt` 没动）；
7. **回滚**：把 `current.txt` 指回上一版本 → 加载的是老资源（保留版本策略生效）；
8. CDN 主源 404 / 超时 → 自动切备用源完成更新。

---

## 九、风险与对策

| # | 风险 | 概率 / 影响 | 对策 |
|---|---|---|---|
| 1 | **框架那 2 个钩子带来的维护成本**（改框架 = 以后同步升级要小心） | 中 / 中 | 两处都是"默认 null 行为不变"的纯增量；加断言守护"未设置钩子时路径与旧行为一致"；文档里写明"改这里必须同步"（照框架既有的 `StreamingRoot ↔ ABBuildSetting.GetPlatformName` 那条注释的写法） |
| 2 | **Android 首包落地占双份磁盘** | 高 / 中 | 默认只落地**首包**（不是全部资源）；提供"启动完成后删除多余内置包"的可选步骤；或改用"首包瘦身（CoreBundles）"只内置核心包 |
| 3 | **首包落地耗时**（首次启动多等几秒） | 高 / 中 | 落地放在 loading 最早阶段 + 进度条；已存在且 hash 一致则跳过；可配置"仅在 Android 落地" |
| 4 | **`.manifest` 字段（CRC/AssetFileHash）在你们 Unity 版本上不全** | 中 / 中 | 阶段 1 第一步实测一次；不全就切"自算 SHA-256"（能力已设计） |
| 5 | **iOS 审核对"下载资源"的认定** | 中 / 高 | 只做资源、不做代码；README 与提审说明里明确写清；不提供"下载可执行文件"的任何 API |
| 6 | **大包 hash 校验卡帧** | 中 / 中 | 分帧流式（默认 8MB/帧）+ 进度可见；校验期间不阻塞下载下一文件（流水线：下 A 的同时校验 B） |
| 7 | **CDN 缓存导致"更新了但玩家拿到旧清单"** | 中 / 高 | 清单 URL 带 `?t=` 或用 no-cache 规则；上传顺序"内容先、清单后"；`Dump()` 里打印实际请求 URL 便于排查 |
| 8 | **多源切换后续传不一致**（不同源的文件不同） | 低 / 中 | 切换源只对"续传点之后"生效；校验失败即整包重下（不尝试合并不同源的片段） |
| 9 | **框架侧钩子被未来重构删掉** | 低 / 高 | 钩子写在 `RevABLoader` / `RevResBootstrap` 的显眼处并附"热更包依赖此钩子"注释；本文档与 README 互相引用 |
| 10 | **UPM 只读安装时编辑器工具不能写文件** | 低 / 低 | 生成物一律写 `AssetBundles/` 与 `Library/`（不写包目录），照 `ABBuildSetting.EnsureWritableForGeneratedCode` 的既有约定 |
| 11 | **手写行式文本解析的健壮性** | 中 / 中 | 解析层全部有工程外断言（8.1 第一组就是它）；解析失败必抛 `ManifestInvalid`（不"尽量解析"） |
| 12 | **热更包自身成为"第二套资源系统"**（膨胀） | 中 / 高 | 硬约束：**热更包不允许新增资源加载 API**（不许出现 `RevHotUpdate.LoadAsync<T>`）；它只管"文件到位 + 路径重定向"，所有加载仍走 `RevResManager` |

---

## 十、与 YooAsset 的对照

| 维度 | YooAsset | RevHotUpdate（本方案） |
|---|---|---|
| 定位 | 独立资源框架（含编辑器收集器、4 种运行模式、报告系统） | **框架的补丁包**：只补"远端下载 + 版本管理 + 路径重定向" |
| 业务改造量 | 加载 API 全换（`YooAssets.LoadAssetAsync`），生命周期也要跟着换 | **0 行**（仍是 `RevResManager.Load/LoadAsync/Release`） |
| 依赖 | 无（但用了大量 Unity API 与自身清单格式） | 无（`UnityWebRequest` + 框架 `RevTask`） |
| 运行模式 | EditorSimulate / Offline / Online / Host 四种 | **只做 Online**（另加"首包直读"模式应付离线） |
| 清单格式 | 自研二进制清单（`PackageManifest`）+ 版本清单 | 行式文本（人可读、可 diff、和 `ResMap.txt` 同族） |
| 差量 | 包级（默认）/ 按需 tag | **包级**（不做二进制差量） |
| 下载器 | 多下载器（含 UnityWebRequest / 自定义）、并发、重试、续传、超时 | UnityWebRequest：并发 3、重试退避、多源、Range 续传、超时、磁盘预检 |
| 版本/回滚 | 有（含灰度、按玩家分组） | 有（版本目录 + `current` 指针 + 保留 N 版）；灰度交给你们的 CDN |
| 加密 | 支持（多种） | 不做（明确边界） |
| 代码量 | 约 3 万行量级 | 目标 ≈ 2200 行（含注释与编辑器工具） |
| 上手成本 | 半天~两天（学一套新 API） | **30 分钟**（只学 3 个入口） |
| 谁该选谁 | 项目准备重做资源层、需要加密/灰度/多运行模式 | **已经是 Revolution 工程、只想加一条热更链路**（本项目的情况） |

**结论**：不替换 YooAsset，也不打算做成它 —— 我们的目标是"**用最小的面积，把框架缺的那一格补上**"。

---

## 十一、框架侧需要的最小改动（diff 草案 + 零改动退路）

### 11.1 改动清单（2 个文件，共约 15 行）

| 文件 | 改动 | 行数 | 不改会怎样 |
|---|---|---|---|
| `Runtime/RevResourceSystem/Implementation/Loaders/RevABLoader.cs` | 新增静态钩子 `BundlePathResolver` + `ResolveBundlePath()`，3 处替换 | ≈ 11 | 无法重定向加载路径（除非重写整个加载器，见 3.1 方案 B） |
| `Runtime/RevResourceSystem/Support/RevResBootstrap.cs` | 新增静态钩子 `ResMapOverride` + `LoadResMap()` 里合并热更表 | ≈ 6 | 热更无法**新增资源**（只能改已有资源） |

两处都有三个共同特征：**默认 null = 行为与现在逐字节一致**；**框架自身永不设置它们**；**不装热更包的工程感受不到任何差别**。

### 11.2 断言守护（跟着进框架的脱机断言工程）

```text
✓ 未设置 BundlePathResolver 时：解析结果 == StreamingRoot + 包名（逐平台）
✓ 设置后返回 null / 空串 → 仍走默认路径
✓ 未设置 ResMapOverride 时：LoadResMap 结果与旧实现一致（键值全等）
✓ 设置后返回空表 → 与未设置等价；返回部分表 → 内置表独有键保留（合并语义）
```

### 11.3 如果坚持"框架一行都不改"（方案 B 的代价，供决策）

| 项 | 方案 A（加钩子，推荐） | 方案 B（零改动） |
|---|---|---|
| 加载器 | 复用框架 `RevABLoader` | **自研 loader + policy（约 +250 行）** |
| 包级引用计数 | 框架管（`handle.BundleLoader`） | 自研 + 反向扫描 `RevResManager.CachedHandles` 反推存活（因为 `BundleAcquired` 是 `internal`，拿不到释放回调） |
| 卸载时机 | 与框架一致（引用归零即卸） | 需要"显式回收点"（版本切换/回登录），否则包常驻内存 |
| 与自动卸载门卫的耦合 | 无 | 有时序耦合（门卫淘汰资源时我们收不到通知） |
| 风险 | 低（15 行增量） | 中（生命周期语义变模糊，排查更难） |
| 结论 | ✅ 推荐 | 可用但不建议；只有"框架目录严禁改动"的硬约束下才选 |

---

## 十二、目录与产出物清单

| 产出 | 路径 | 说明 |
|---|---|---|
| 热更包（运行时） | `Assets/Revolution.HotUpdate/Runtime/` | asmdef `Revolution.HotUpdate` |
| 热更包（编辑器） | `Assets/Revolution.HotUpdate/Editor/` | asmdef `Revolution.HotUpdate.Editor`（Editor only） |
| 包元数据 | `Assets/Revolution.HotUpdate/package.json` | UPM 用（可选） |
| 上手文档 | `Assets/Revolution.HotUpdate/README.md` | 3 分钟上手 + 边界声明 |
| 技术方案（本文） | `Revolution.Document/热更新/RevHotUpdate技术方案.md` | ✅ 本次已落地 |
| （可选）HTML 版 | `Revolution.Document/热更新/RevHotUpdate技术方案.html` | 进文档站，与其它模块同款样式 |
| ✅ 示例 | `Assets/Revolution.Demo/RevHotUpdate.Demo/` | 已落地：演示面板 + 场景 + 编辑器一键装配 + `起本地CDN.cmd`（`HttpListener` 静态服务，支持 Range / 分缓存头，无需真云） |
| ✅ 分支同步 | `.github/workflows/sync-hotupdate-branch.yml` | 已落地：推 `main` 时把 `Assets/Revolution.HotUpdate/` 重建到 `hotupdate` 分支（与 `package` / `demo` 同一套写法） |
| 框架侧改动 | `RevABLoader.cs` / `RevResBootstrap.cs` | 第十一章的 15 行 |

---

## 十三、需要拍板的 5 个决策（每条已给推荐）

> ★ 这 5 个决策的**最终选定结果**在第十五章（如果你只想看结论，直接跳到第十五章）。

| # | 决策 | 选项 | 我的推荐 | 理由 |
|---|---|---|---|---|
| 1 | 是否接受"框架加 2 个钩子" | 加钩子（A）/ 零改动（B） | **A** | 15 行 vs 250 行 + 生命周期语义变模糊；且钩子默认 null，风险可控 |
| 2 | 默认首包模式 | 落地（A）/ 直读 (B) / 纯远端 (C) | **Android 落地、桌面/iOS 直读** | 一条路径打平三平台的是 Android；桌面/iOS 直读省磁盘与时延 |
| 3 | 清单 hash 算法 | SHA-256 / MD5 / 复用 `.manifest` 的 AssetFileHash | **默认 SHA-256，允许复用 `.manifest` 作为"零成本档"** | 首次实施时按实测（风险 4）二选一，配置项已留 |
| 4 | 是否做"首包瘦身 + 按需下载" | 首包全量（简）/ 只内置核心包（进阶） | **先用"首包全量"跑通**，`CoreBundles` 留到阶段 4 之后 | 首包全量最不容易出错（不会有"缺包"的诡异失败） |
| 5 | 热更包要不要独立发分支 | 是（`hotupdate`）/ 否（就放主仓库） | **是** | 与 `package` / `demo` 一致的用法，别人一条命令就能装 |

---

## 十四、按包标记来源的简化方案（第一档：分工层）

> ★ **如果你主推 Android / 小程序（WebGL），这一章还可以再砍一层**：
> 那两个平台上"按包标记 + 混合来源"其实用不上 —— 见**第十七章**（结论：留下"一个可注入的根"，砍掉"按包分流"；
> 小程序上更是连"落地/断点续传/文件校验"整套都可以不要，下载与缓存交给引擎）。
> 本章的"按包标记"在 **PC / iOS 需要混合来源**时才真正必要。

> 这一章回答的是另一个更小的问题：
> **"我在编辑器里标出哪些 AB 包要走热更、哪些不用；要热更的从持久化路径读，不热更的从 StreamingAssets 读" —— 这样行不行？**
> 结论：**行，而且这是正确的地基；但有一处语义必须改（14.2），四个细节必须一起做掉（14.6），并且它本身还不是"热更"（14.8）。**

### 14.1 先回答"行不行"

| 问题 | 结论 |
|---|---|
| 这个分工本身成立吗 | ✅ 成立。它天然复用框架的依赖递归（`acquireBundle` 按包名逐个解析 → 每个包各自落到正确的根）与平台分派 |
| 能解决什么 | "同一个工程里两种来源的包都要能加载"；把"哪些包将来可变"变成**打包期就能看见的标记** |
| **不能解决什么** | **没有传输层**：持久化目录里的包得**有人放进去**（谁下？下哪些？下坏了怎么办？）—— 这些属于第二档（本文档阶段 1~4） |
| 要改框架几处 | 2 处钩子（约 17 行，默认 null 零影响；见第十一章） |
| 实现量 | 编辑器 ≈ 350 行 + 运行时 ≈ 250 行；**3~4 天含真机验收** |
| 与本文档主方案的关系 | **真子集**。它做的每件事在第二档里原样保留（表格式、钩子、目录约定、编辑器窗口都不改），第二档只是"在它上面加下载与版本" |

**所以：行，而且是推荐的起步方式。** 但它解决的问题是"**加载从哪来**"，不是"**新包怎么来**"。

### 14.2 ⚠️ 一处必须改的语义（否则新装机必崩）

你原话是"**需要热更新的就从持久化路径当中来加载**"。如果**严格按字面**实现（热更包只认持久化路径），会遇到一个致命场景：

```text
新装机 / 首次启动 / 玩家清了数据 / 换机
  → {persistentDataPath} 是空的
  → 所有"热更包"全部 BundleLoadFail
  → 首包连登录界面都拉不起来（如果登录 UI 在热更包里，直接黑屏）
```

| 语义 | 首装表现 | 结论 |
|---|---|---|
| ✗ 热更包 = **只**从持久化读 | 首装必崩（除非首启强制阻塞下载完所有热更包才能进游戏） | 不推荐 |
| ✅ 热更包 = **持久化优先，持久化没有就回退内置（StreamingAssets）** | 首装照常跑（用内置那份），有热更内容就用热更的 | **推荐** |
| ✅ 热更包 = 持久化优先 + 回退 + **强制要求该包必须已下载**（可配置 `RequireDownloaded`） | 首装崩（但错误信息明确："hero 需要先下载"） | 少数"必须在线"的场景 |

**推荐把标记语义定成"允许被覆盖"（overridable），而不是"必须从某处读"**。这带来三个额外好处：

1. 新装机、清数据、换机不用特殊处理（都是"回退内置"这条路径）；
2. 可以"临时关掉热更"（把持久化目录删掉/改个名就回到出包状态）——排查神器；
3. 打包期不用纠结"这个包到底放哪"：**非热更包 = 永不覆盖**，**热更包 = 内置一份 + 允许覆盖**，两者都能跑。

> 因此 14.5 的路径解析一定是"**先看持久化有没有 → 有就用 → 没有就用内置**"，而不是"按标记二选一"。
> 这也让"哪些包标成热更"变成一个**低风险决策**：标多了不会崩（只是允许被覆盖），标少了才会限制将来的灵活性。

### 14.3 标记配在哪、怎么变成运行时能读的东西

三条纪律：**不改框架的打包窗口**、**不改包名**、**生成物写在工程侧（不在 `Assets/Revolution` 里）**。

| 环节 | 落在哪 | 说明 |
|---|---|---|
| ① 配置（人写） | `Assets/Editor/RevHotUpdateConfig.asset`（工程侧 ScriptableObject） | 规则列表 + 兜底默认值 + 持久化根名（见 14.3.1） |
| ② 包清单来源 | **`Assets/Revolution/Resources/ResourceSystem/ResMap.txt` 的第 2 列去重** | 零框架 API、不用先打包、且这就是运行时真正用的表；表不存在时提示"先跑一次『仅生成映射』" |
| ③ 生成物（运行时读） | `Assets/Resources/RevHotUpdate/HotBundles.txt` | 与框架的 `ResMap.txt` 同族格式（`\|` 分隔 + `#` 注释）；`Resources.Load` 一次读入（几百包 ≈ 几 KB） |
| ④ 运行时判定 | `Revolution.HotUpdate` 程序集 | 解析成 `HashSet<string>` 做 O(1) 判定；**表缺失时全部按"内置"处理并 Warn 一次**（绝不猜） |

为什么生成物放**工程侧的 `Assets/Resources/`**（而不是框架目录、也不放 `Assets/Revolution.HotUpdate/`）：

- 放框架目录 = 热更包往框架里写东西，删热更包会留垃圾 ✗；
- 放 `Assets/Revolution.HotUpdate/` = UPM 只读安装时写不进去（框架对生成物有同样的降级处理，见 `ABBuildSetting.EnsureWritableForGeneratedCode`）✗；
- 放工程 `Assets/Resources/` = **UPM 只读也能用**、随包体发布（很小）、WebGL/Android 都能 `Resources.Load` ✓。

> 备选（想要"编译期判定"）：同一次生成再吐一个 `RevHotBundleFlags.cs` 常量类（业务可以用 `if (RevHotBundleFlags.IsHot("hero"))` 在编译期分支）。
> **默认只出 txt（单一真相，免编译等待）**，需要常量再打开开关 —— 避免"两份表不一致"。

#### 14.3.1 配置资产长什么样

```csharp
[CreateAssetMenu(menuName = "Revolution/热更包配置", fileName = "RevHotUpdateConfig")]
public sealed class RevHotUpdateConfig : ScriptableObject
{
    [Tooltip("持久化根目录名（{persistentDataPath}/<这个名字>/bundles/<包名>）")]
    public string localRootName = "RevHotUpdate";

    [Tooltip("没有命中任何规则的包，默认算热更还是内置（推荐 hot：标多了不会崩，只是允许被覆盖）")]
    public bool defaultHot = true;

    [Tooltip("规则：从上往下匹配，命中即止。支持 精确名 / 前缀（以 * 结尾）")]
    public List<Rule> rules = new List<Rule>();

    [Tooltip("生成时顺带写入每个包的 hash 与大小（供运行时可选校验；实测 .manifest 字段齐全才准确）")]
    public bool writeHashColumn = true;

    [Tooltip("运行时是否对'从持久化加载的包'做一次 hash 校验（默认关：档 1 以轻为主）")]
    public bool verifyOnLoad = false;

    [Serializable]
    public sealed class Rule
    {
        public string pattern;   // "ui_*" / "hero" / "scene_arena"
        public bool hot;         // true = 允许被覆盖
    }
}
```

编辑器窗口 `RevHotUpdateWindow`（挂在 `Revolution.Tools/热更新/热更包标记`）提供 4 个动作：

| 动作 | 做什么 |
|---|---|
| 列出所有包 | 读 `ResMap.txt` 第 2 列去重 → 表格显示"包名 / 资源数 / 当前判定（热更|内置）/ 命中哪条规则" |
| 勾选 | 直接勾/取消（写回规则：精确名规则自动追加，不用手写 pattern） |
| 一键默认 | "全部热更" / "全部内置" / "只热更某个前缀" |
| 生成 | 写 `Assets/Resources/RevHotUpdate/HotBundles.txt` + 自检（有规则但没有任何包命中 → 提示；包名与 `ResMap` 不一致 → 报错） |

### 14.4 生成物格式（`HotBundles.txt`）

```text
# RevHotBundles v1 —— 由 RevHotUpdate 编辑器工具生成，请勿手改（改了会被下次生成覆盖）
# 字段：@key|value / @bundle|包名|来源(hot|builtin)|hash|字节数
@generatedAt|2026-10-01 12:00:00
@localRoot|RevHotUpdate
@default|hot
@rule|ui_*|hot
@rule|scene_arena|builtin
@bundleCount|6

@bundle|common|hot|7c1e...|1572864
@bundle|ui_login|hot|9b2f...|1048576
@bundle|ui_bag|hot|1a3d...|2097152
@bundle|hero|hot|5e6f...|15728640
@bundle|effect_battle|hot|8d9c...|8388608
@bundle|scene_arena|builtin|2b4a...|25165824
```

- 与 `RevHotManifest`（附录 A）**同族**，第二档升级时只是"多几列、多一份远端来源"，不需要换解析器；
- `hash`/`字节数`两列就是 14.6 第 1 条的"搬错包"防线，成本几乎为零（编辑器侧本来就在读 `.manifest`）。

### 14.5 运行时实现（三段 + 一条时序约束）

#### 14.5.1 表

```csharp
// RevHotBundleTable：解析 HotBundles.txt（找不到表 → 全部按 builtin，Warn 一次）
internal static class RevHotBundleTable
{
    private static readonly Dictionary<string, RevHotBundleEntry> _map = new ...;   // 包名 → {hot, hash, size}
    internal static bool IsHot(string bundleName);
    internal static string LocalRoot { get; }                                        // {persistentDataPath}/{@localRoot}
}
```

#### 14.5.2 路径解析（钩子的实现，这一段就是整个方案的核心）

```csharp
// 唯一入口：框架的 RevABLoader.BundlePathResolver 指向它
internal static string ResolveBundlePath(string bundleName)
{
    // ① 标记为"允许被覆盖"的包：持久化目录里有就用它（这就是"热更生效"）
    if (RevHotBundleTable.IsHot(bundleName))
    {
        string local = Path.Combine(RevHotBundleTable.LocalRoot, "bundles", bundleName);
        if (File.Exists(local))
        {
            LogOnce(bundleName, "热更");        // ★ 首次加载时打一条 Info：这个包此刻从哪来 —— 排查全靠它
            return local;
        }
        // 持久化没有 → 回退内置（新装机 / 没下过 / 临时关热更，都走这里）—— 见 14.2
    }

    // ② 其余（内置包、或热更包还没下）：保持框架原行为
    return null;                                // 返回 null = 让框架走 StreamingAssets/<平台>/<包名>
}
```

> **返回 `null` 而不是自己拼 StreamingAssets 路径**：这样"默认行为"只有框架一个出处在维护（平台名、目录规则、Android/WebGL 分支都在框架里），我们只负责"要不要覆盖"。

#### 14.5.3 装配

```csharp
public static void Install()
{
    RevABLoader.BundlePathResolver  = RevHotPaths.ResolveBundlePath;   // 静态方法，不捕获实例
    RevResBootstrap.ResMapOverride  = RevHotPaths.LoadLocalResMap;     // 持久化里有 ResMap.txt 就用它，否则返回 null
    RevResBootstrap.Instance.Init();                                   // 框架原样装配
}
```

`LoadLocalResMap()`：读 `{LocalRoot}/ResMap.txt` → 解析成 `Dictionary<string,string>` → 没有文件就返回 `null`（框架继续用内置表）。
**为什么档 1 也要它**：业务往持久化目录搬"新包 + 新表"时，如果表还是内置那份，"新增资源"照样加载不到（3.2 那条硬事实在档 1 依然成立）。

#### 14.5.4 目录约定（档 1 扁平版）

```text
{persistentDataPath}/RevHotUpdate/
├── bundles/<包名>        ← 允许被覆盖的包放这里（档 1：人工/工具搬；档 2：下载器写）
├── ResMap.txt            ← 可选：热更映射表（有就用它）
└── .tmp/                 ← 档 2 的下载临时目录（档 1 空着即可）
```

#### 14.5.5 ★ 一条时序约束（必须遵守，否则会出诡异问题）

**不许在"包正被使用"时替换它的文件**。原因有两条，都很难排查：

1. Windows（编辑器 / PC 包）上，文件被 `LoadFromFile` 映射后**替换会失败**（文件锁）→ 表现为"更新成功但加载的还是旧内容"；
2. 即使替换成功，**已经加载的包仍留在内存里**，本次进程内不会生效 → 表现为"我明明覆盖了，怎么没变"。

正确顺序（档 2 会自动做，档 1 里由业务手工遵守）：

```text
RevResBootstrap.Instance.ShutdownAll();   // ① 卸载所有资源与包（①②③④ 都被它封装了）
（写入/替换持久化目录里的包 + ResMap.txt）
RevResBootstrap.Instance.Init();          // ② 重装策略 → 新文件生效
```

> 开发期手工验证时记住这条：**"搬完包先重进一次 Play"**（或先调 `ShutdownAll`）。

### 14.6 四个必须一起做掉的细节（缺一个都会变成"偶尔出问题"）

| # | 细节 | 不做的后果 | 做法 |
|---|---|---|---|
| 1 | **miss 回退 + 来源日志**（14.2 / 14.5.2） | 新装机黑屏；或"到底加载的是哪个版本"永远说不清 | 回退内置；每个包**首次加载时打一条 Info**（包名 + 来的根），并进 `Dump()` |
| 2 | **Android 上"非热更包"的读取** | Android 真机上内置包报 `BundleLoadFail`（`streamingAssetsPath` 是 `jar:file://...`，不是文件路径；`AssetBundle.LoadFromFile` 对压缩包读不了） | 三选一：**① 首包落地**（把内置包拷到持久化目录，加载路径统一，代价是双份磁盘）/ ② 内置包 **AB 不压缩**（`UncompressedAssetBundle`，`LoadFromFile` 可读 jar，代价是首包体积 +30%~50%）/ ③ 把钩子返回值扩成"URL 语义"（框架按 `http://`/`jar:` 前缀改用 `UnityWebRequestAssetBundle`，代价是内存峰值 + 首启解包耗时）。**推荐 ①** |
| 3 | **逐包生效 vs 整版本一致** | 档 1 是"逐包生效"：如果包 A 与包 B 有**互相依赖的改动**（A 用到 B 的新资源），先搬 A 后搬 B 的窗口里会出现"新 A + 旧 B" → 报错或表现诡异 | 档 1：搬包时**整批搬**（工具化：一次拷完再 `ShutdownAll`+`Init`）；档 2：版本目录 + `.stamp` 完成标记（5.1）天然解决 |
| 4 | **ResMap 与包同源** | 只换包不换表 → 新增资源加载不到（3.2）；只换表不换包 → 表里有、包里没有 → `AssetLoadFail` | 表与包**同批更新**；`LoadLocalResMap` 与包放在同一个持久化根下（档 2 里两者都在版本目录里，天然同源） |

### 14.7 验收标准（档 1 可独立验收）

**工程外纯 C# 断言（20+ 条，照框架习惯）**：表解析（含 `#` 注释 / 空行 / 字段不足 / BOM / `\r\n`）、规则匹配（精确 / 前缀 / 兜底默认值 / 规则顺序）、路径选择（hot+存在 → 持久化；hot+缺失 → 内置；builtin → 内置）、`LocalRoot` 拼装、缺表时的降级行为。

**真机手工验证 5 条**：

1. 把某个"热更包"的新版本（不同内容）拷进 `{persistent}/RevHotUpdate/bundles/` → 重进 Play → **加载到新内容**（并看到"来源：热更"的日志）；
2. 把该文件删掉 → 重进 Play → 回到内置内容（**来源：内置**）；
3. 一个包都不放 → 重进 Play → 一切与装热更包之前**完全一样**（验收"不导入零影响"）；
4. Android 真机：内置包能加载（14.6 第 2 条的方案生效）+ 热更包能被覆盖；
5. WebGL（若需要）：钩子返回 CDN URL 时能加载（这一条依赖第十一章钩子的 URL 语义）。

### 14.8 与第二档的关系、以及什么时候必须升级

| 档 1 里做的东西 | 第二档（本文档主方案）里怎么用 |
|---|---|
| `HotBundles.txt` 表格式与解析 | **原样保留**（并升级成 `RevHotManifest` 的一份子集：多一列"远端路径"、多一份远端清单做版本比对） |
| `ResolveBundlePath`（持久化优先 + 回退） | **原样保留**（只是"持久化目录"从扁平变成"版本目录"） |
| 两个框架钩子 | **原样保留**（钩子的语义不用变） |
| 编辑器窗口（列包 / 勾选 / 生成） | **原样保留**（再加"生成清单 / 上传辅助"两个按钮） |
| 目录约定（持久化根 + bundles + ResMap） | **平滑升级**：外面套一层版本目录 |
| — | 新增：下载器 / 版本比对 / 校验 / 版本目录与完成标记 / 回滚清理 |

**什么时候必须从档 1 升到档 2**：当"新包怎么进玩家手机"这件事**不能再靠人工/外部更新器**时 —— 也就是产品要求"玩家自己点一下更新、下完就能玩新版资源"。在那之前，档 1 已经完全能支撑：
- "我提前知道哪些包将来要改"（打包期就分好）；
- "测试同学手工换一版资源验证"（拷包 + 重进）；
- "同一份代码同时支持两种来源的包"（设备上混合加载）。

### 14.9 工作量与推荐

| 项 | 档 1（本章） | 档 2（本文档主方案） |
|---|---|---|
| 框架改动 | 2 处钩子（≈17 行） | 同（不再增加） |
| 运行时 | ≈ 250 行（表 + 路径 + 装配 + 日志） | ≈ 1600 行 |
| 编辑器 | ≈ 350 行（配置 + 窗口 + 生成 + 自检） | + ≈ 600 行（清单 / 上传辅助） |
| 工期 | **3~4 天（含真机）** | 再 9~12 天 |
| 能上线的形态 | 内部 / 测试环境；"资源随包或由外部更新器分发" | 玩家自助更新 |

> **推荐路径：先做档 1（本章），跑通"两个根"并验收；产品需要"玩家自助更新"时，再加档 2 的传输层 —— 档 1 的产物全部保留，不返工。**
> 这也是对"我这样行不行"最实用的回答：**行，但你得知道它是一半 —— 而且这一半必须做对（14.2 的语义 + 14.6 的四个细节）。**

---

## 十五、历史推荐方案（现以第十九章修订版为准）

> **历史草案，不可照此当作已交付能力。** 以下“旧版本可回滚、杀进程后续传、两钩子、坏包绝不加载”等目标未经现有实现满足；2026-10-13 的真实差距、优先级和验收标准以第十九章为准。此处保留原论证供比较。
> 一句话：**两个根 + 一份表 + 一个版本目录 + 两处不可见钩子。**

### 15.1 一张图看懂

```text
启动
 └─ await RevHotUpdate.InitializeAsync(cfg)
      ├─ ① Android：把内置首包"落地"到持久化（幂等 + hash 跳过；PC/iOS 跳过这一步）
      ├─ ② 拉远端清单（失败/损坏 → 保持当前版本，玩家照常进游戏）
      ├─ ③ 版本比对 → 只下变化过的包（Range 续传 / 重试 / 多源降级）
      ├─ ④ 校验（尺寸 + SHA-256）→ .tmp 原子改名 → 全齐后打 .stamp
      ├─ ⑤ 原子切换 current.txt（旧版本保留 N 份，可回滚）→ 删更老的
      └─ ⑥ 装钩子：持久化优先 → 内置兜底 → 都没有才报错；ResMap 同步热更
           └─ RevResBootstrap.Init()  ← 框架原样装配，业务加载代码一行不改
```

### 15.2 九条设计决策（每条都附"为什么这样最稳"）

| # | 决策 | 为什么这是最稳的 | 放弃了什么 |
|---|---|---|---|
| 1 | **接缝方式**：框架加 2 个默认 `null` 的静态钩子（`BundlePathResolver` / `ResMapOverride`），热更包复用框架的 `RevABLoader` | 包级引用计数、依赖递归、卸载时机全部沿用框架（`RevResHandle.BundleAcquired` / `BundleLoader` 是 `internal`，自研 loader 拿不到释放回调 → 生命周期只能"反推"，是**语义退化**，不是等价实现） | 框架 17 行改动（默认 null，有断言守护） |
| 2 | **标记语义**：**覆盖式** —— 持久化有就用、没有就回退内置、两者都没有才报错（**不是**"热更包只从持久化读"） | 新装机 / 清数据 / 换机 / 临时关热更，全都走同一条"回退内置"，**没有"首装崩"这一档故障** | 无（唯一的"代价"是包会同时存在两份，属空间而非正确性） |
| 3 | **生效粒度**：**版本目录 + `.stamp` 完成标记 + `current.txt` 原子切换**（不是逐包直接覆盖） | 消灭"半新半旧"这一整类问题；下载中断/校验失败**绝不污染**当前可用版本；天然可回滚 | 多一点目录管理代码（约 +120 行） |
| 4 | **首包策略**：**首包全量内置**（每个包在包体里都有一份） | 任何"缺包"的诡异失败（`BundleLoadFail`、黑屏）都不会发生；热更只下"变化的包"，正常玩家更新量 = 变化量 | 包体最省（"首包瘦身 + 按需下载"留到第 4 步） |
| 5 | **Android 首包读取**：**落地到持久化**（幂等 + hash 跳过）；PC / iOS 直接读 StreamingAssets | Android 的 `streamingAssetsPath` 是 `jar:file://...`，压缩 AB 用 `LoadFromFile` 读不了；落地后**三平台都是"文件路径"一条链路**，`LoadFromFile` 的加载速度与内存表现最好 | Android 上双份磁盘 + 首次启动的拷贝时间（用"只拷缺失 + hash 跳过"压到最小） |
| 6 | **校验**：下载期 **尺寸 + SHA-256**（分帧流式）；可选复用 Unity `.manifest` 的 `CRC` 做加载期校验 | 坏包**绝不进**版本目录；hash 是"重下 vs 跳过"的唯一判据 | 少量 CPU（分帧后无感）；`.manifest` 字段齐不齐需实测一次 |
| 7 | **清单与映射表**：行式文本（与 `ResMap.txt` 同族）；**ResMap 必须与包同批更新**，热更表覆盖内置表（合并语义） | 新增资源 / 改包只有靠它才成立；零第三方依赖；出问题能"记事本比一比" | JSON 生态便利（用不上） |
| 8 | **传输容错**：并发 3、重试指数退避、多源降级、Range 续传、磁盘预检；**清单拉取失败或损坏 → 保持当前版本** | 弱网 / 断网 / CDN 抖动 / 单源故障的表现全部可预期；玩家最坏情况是"这次没更新"，而不是"进不去游戏" | 无 |
| 9 | **切换时序**：`ShutdownAll()` → 切 `current` → 删旧版本 → `Init()`；**禁止在包被使用时覆盖文件** | 避开两个最阴的坑：Windows 文件锁导致"替换失败"，以及已加载包在内存里导致"覆盖了却不生效" | 无（这是一条纪律，不是代价） |
| 10 | **版本模型**：**大版本锚定** —— 大版本内热更资源，跨大版本重新出包（清单里的 `@appVersion` 不匹配就拒绝并引导强更） | 把"不可能兼容的变更"挡在包体更新这条路径上，资源层因此**不需要做向后兼容**；"基线 = 包体"让回滚有确定边界；清单/目录也可以只做"单一大版本内的差量"，省掉灰度矩阵与兼容区间（第十六章） | 大版本内不能改代码；跨大版本玩家要整包下载 |

### 15.3 分期落地（每步独立可验收、都不返工）

| 步 | 内容 | 产出验收 | 工期 |
|---|---|---|---|
| **1** | 钩子 + 标记表 + 路径解析（= 第十四章） | 手工把包拷进持久化目录 → 加载到新内容；删掉 → 回退内置；什么都不放 → 与装热更前完全一样 | 3~4 天 |
| **2** | 清单 + 下载器 + 校验 + 版本目录 + Android 首包落地 | "改一张图 → 重新打包 → 生成清单 → 放进本地 CDN → 真机只下变化包并生效"闭环；8 项破坏性测试全过 | 4~5 天 |
| **3** | 编辑器链路（标记 UI / 清单生成 / 自检 / 上传脚本）+ demo + CI | 面板 3 个动作可用；自检能拦下漏包与 hash 不符；与 `ABCIBuild` 一起能无人值守 | 3 天 |
| **4（按需）** | 首包瘦身 / 按需下载 / 清单签名 / 加密 / 灰度 | 按产品需要单独评估 | — |

### 15.4 "为了稳"清单（12 条；每条都在防一个具体故障）

| 做法 | 防的是什么 |
|---|---|
| 首包落地**幂等 + hash 跳过** | 重复拷贝、半截文件被当成好文件 |
| `.tmp` + **原子 rename** | "半个包"被加载（表现为随机崩溃或资源缺失） |
| 版本目录 **`.stamp` 完成标记** | 半新半旧：一部分包新、一部分旧 |
| `current.txt` **原子替换** | 断电/被杀进程时写出半行文本 → 版本指针损坏 |
| **保留 N 个版本** | 新版本出问题时回不去（线上事故的第一道逃生门） |
| 清单失败/损坏 → **保持当前版本** | CDN 抖动或误删清单导致"玩家集体进不去游戏" |
| 校验失败 → **整包重下（不带 offset）** | 续传点错位导致的"持续性损坏"（越续越坏） |
| 每个包**首次加载打印来源**（持久化 / 内置） | "到底跑的哪一版"永远说不清 → 排查成本爆炸 |
| 下载前**磁盘预检** | 下到一半失败（且留下垃圾） |
| **多源降级** | 单一 CDN 故障直接变成"全部玩家更新失败" |
| 切换前 **`ShutdownAll()`** | 文件锁 + 已加载包不生效这两个坑 |
| 表缺失时**全按内置 + Warn 一次** | "配置没生成"却悄悄改变了加载来源（最怕静默） |

### 15.5 "千万别做"清单（8 条）

1. **别让"热更包"只从持久化读** —— 新装机必崩（14.2）。
2. **别在包被使用时覆盖它的文件** —— 文件锁 + 内存不一致（15.2 第 9 条）。
3. **别用 `File.WriteAllBytes` 直接写最终文件名** —— 必须 `.tmp` → rename。
4. **别把对象存储的 `ListObjects` 当目录清单** —— 权限/延迟/无原子性，清单才是唯一真相。
5. **别忘了把 `ResMap` 放进更新内容** —— 否则"新增资源"永远加载不到。
6. **别把热更放在资源系统初始化之后** —— 那会先加载一整套旧资源，再被卸载重载。
7. **别在回调里做同步全量校验** —— 几百 MB 会直接卡帧，必须分帧流式。
8. **别给热更包加自己的资源加载 API**（`RevHotUpdate.LoadAsync<T>` 之类）—— 一旦有了它会立刻长成"第二套资源系统"，与框架的引用计数/分组/自动卸载彻底分家。

### 15.6 对第十三章 5 个决策的最终选定

| 决策 | 选定 |
|---|---|
| ① 是否接受框架加 2 个钩子 | **接受**（方案 A：17 行、默认 null、有断言守护）——比"零改动自研 loader"稳 |
| ② 默认首包模式 | **Android 落地，PC/iOS 直读 StreamingAssets** |
| ③ 清单 hash 算法 | **SHA-256 为默认**；有实测依据时可把"复用 `.manifest` 的 AssetFileHash"降级为"零成本档" |
| ④ 是否做首包瘦身 | **先不做**（首包全量最稳），留到第 4 步 |
| ⑤ 独立分支 | **要**（`hotupdate` 分支，与 `package` / `demo` 一致，一条命令就能装） |

### 15.7 一句话总结

> **最稳妥 = "覆盖式语义 + 版本目录原子切换 + 首包全量兜底 + 复用框架加载器（2 个不可见钩子）"；
> 最优 = 这套东西同时是"最小面积"：0 第三方依赖、业务加载代码 0 改动、约 2200 行、可分 3 步落地且每步都不返工。**
> 换句话说：**不追求"功能最全"，追求"每个失败都有确定的出口"** —— 这也正是这个框架一贯的取舍。

---

## 十六、版本模型：大版本锚定（大版本内热更，跨大版本重出包）

> 这一章是把一项**产品/发版纪律**写进技术方案：**大版本之内**用 RevHotUpdate 热更资源；**整个大版本切换**时重新打包、走商店更新。
> 结论：**这是这类项目最优的版本模型** —— 它不是"少做点功能"，而是用一条版本纪律**消掉方案里一大半的兜底复杂度**。

### 16.1 模型长什么样：两个版本号，一条隔离墙

```text
App 大版本（= Player Settings 里的 Application.version，如 1.2.0）  ← 锚点：只由"重新出包"改变
资源版本（如 1.2.0.37）                                            ← 锚点之内：可热更、可多次、可回滚

1.2.0 出包（基线）
  ├─ 1.2.0.1  … 1.2.0.37        资源热更（只下变化的包；同一大版本内可回滚到任意保留版本）
  └─ 1.3.0 重新出包             ← 新的大版本：全新的远端目录，老客户端永远不会拉到它

远端目录：{RemoteRoot}/{环境?}/{平台}/{大版本}/{渠道?}/…     ← "大版本"这一段就是隔离墙
```

| 谁 | 职责 |
|---|---|
| **客户端** | 拿自己的 `Application.version` 去请求清单；`@appVersion` 不匹配 → **拒绝更新 + 提示更新客户端**（绝不用不匹配的资源） |
| **服务端 / CDN** | 按大版本分开的目录；大版本切换 = 新目录（不复用旧的） |
| **发版流程** | 大版本切换必须重新出包；大版本内只做资源热更 |

### 16.2 为什么这是最优的版本策略（五条论证）

| # | 理由 | 省掉了什么 |
|---|---|---|
| 1 | **不需要资源层向后兼容**：跨大版本的资源与代码不兼容问题是"被禁止发生"的，不是"被小心处理"的 | 省掉版本兼容矩阵、资源与代码接口的双向兼容测试 |
| 2 | **基线是天然的**：包体里那批包就是该大版本的基线（清单随包体发布） | 省掉"第一次热更要先下一份基准"、"基准丢了怎么办"这一类问题 |
| 3 | **回滚有确定边界**：同一大版本内回滚 = 指回上一份资源版本（保留 N 份）；**最坏兜底 = 删持久化目录 → 回到出包状态**（一行操作） | 省掉"回滚目标可能跨大版本"这种没法处理的情况 |
| 4 | **清单与目录可以极简**：只描述"单一兼容窗口内的差量" | 省掉灰度矩阵、兼容区间、多版本同时在线的复杂查询 |
| 5 | **与框架零冲突**：锚点就是 `Application.version`，框架不需要任何新增机制 | 不需要为热更改框架的版本概念 |

附带收益：**如果将来引入代码热更（HybridCLR），这个模型就是它的公共地基** —— "大版本"天然界定了"哪些变更属于同一个兼容窗口"，这正是代码热更最需要的语义（但 iOS 审核红线不变，本期边界仍是"只做资源热更"）。

### 16.3 需要落地哪些机制（5 条，都是小东西）

| # | 机制 | 落点 |
|---|---|---|
| 1 | 清单头部加三个版本字段：`@appVersion`（精确锚点）、`@resVersion`（同大版本内自增）、`@minAppVersion`（兼容下限，"补丁包"场景用） | 清单生成器 + 解析器（附录 A 已更新样例） |
| 2 | 客户端校验 + 强更回调：不匹配 → `RevHotError.AppVersionMismatch`，并触发 `OnForceUpdateRequired(info)`（业务决定跳商店 / 弹公告 / 走整包下载） | `RevHotUpdate.CheckAsync` 的第一步（比清单比对更早，省一次无意义下载） |
| 3 | **远端目录按大版本隔离** | URL 拼装：`{RemoteRoot}/{环境?}/{平台}/{appVersion}/{渠道?}/…`（4.1 的图已更新） |
| 4 | **基线清单随包体发布**：`Assets/Resources/RevHotUpdate/Baseline.txt`（出包时由编辑器工具生成，内容 = 该大版本全部包的 `name\|hash\|size\|deps`） | 与 `HotBundles.txt` 同一个目录（14.3 的约定），运行时读它算差集、也用它做"回滚到出包状态"的判据 |
| 5 | 大版本切换的运维动作：新目录（不复用旧目录）+ 旧目录保留 N 天（给还没更新的老客户端） | 上传脚本 / CI（文档化的操作清单） |

> 框架/provider 侧**不做**：跳商店、整包下载、渠道分发 —— 那是发行层的事。框架只负责"**识别版本不匹配 + 给出明确原因 + 一次回调**"。

### 16.4 "大版本内能热更什么 / 必须重新出包"对照表（给策划与产品看）

| 内容 | 大版本内热更 | 说明 |
|---|---|---|
| 新增 / 替换美术资源（图集、模型、特效、音频） | ✅ | 纯资源 |
| 新增 UI 面板（用已有的面板基类 + 预制体 + 配置表拼装） | ✅ | 预制体是资源；行为仍由已有脚本驱动 |
| 数值 / 配置表（导表工具产出） | ✅ | 数据 |
| 新增活动、限时玩法（用已有系统拼装） | ✅ | 数据 + 资源 |
| Shader 参数 / 材质调整 | ⚠️ 需实测 | Shader 本身是资源，但可能牵动渲染管线设置 |
| 脚本代码（修 bug、新功能） | ❌（除非另接 HybridCLR，本期不做） | 代码在包体里 |
| 新的 Unity 插件 / 新的原生库 / 渲染管线与 Player Settings 变更 | ❌ | 属包体级变更 |
| 资源布局的**破坏性重构**（包名大改、依赖结构重排） | ⚠️ 技术可行（ResMap 同步 + 依赖重算），但**建议留到大版本** | 风险与收益不匹配 |
| 大版本之间复用资源包 | ❌ **禁止**（远期不是"不能"，而是"不许"） | 避免"看起来能用"的幻觉（16.6 第 4 条） |

### 16.5 这个模型给前面章节带来的简化

| 章节 | 原本要考虑 | 在大版本锚定下 |
|---|---|---|
| 5.3 版本比对 | 兼容矩阵 / 多版本在线 / 灰度 | 只需"**基线清单 vs 远端同大版本清单**"的纯差量 |
| 5.7 ResMap 合并语义 | 担心"热更表删了键、基线表残留" | 该风险只在跨大版本时出现，而它被禁止 → **合并语义在大版本内是安全的**（生成期再自检"热更表覆盖基线全部键"即可） |
| 5.1 版本目录 | 目录层级与清理规则 | 变成 `<平台>/<appVersion>/<resVersion>/` 两级，清理规则更明确：同一大版本内保留 N 份；跨大版本目录在切版本时**整目录删** |
| 14.2 标记语义 | "热更包缺了怎么办" | 基线永远在包体里 → 回退路径永远存在，"最坏=回到出包状态"成为可承诺的兜底 |
| 附录 A 清单 | 只有 minAppVersion | 补 `@appVersion` / `@resVersion` / `@baseManifest` |

### 16.6 边界与纪律（5 条，踩了会白干）

1. **服务端必须按大版本返回清单**（客户端带上自己的 `Application.version`；服务端不要"返回最新"）——否则旧客户端会拿到新资源清单；
2. **测试环境务必按大版本隔离** ——"测试同学用旧包、拉到新资源"是这类项目最常见、也最难查的一类"假 bug"；
3. **大版本内不要做资源布局破坏性重构** —— 包名大改、依赖结构重排留到下一个大版本；
4. **跨大版本不要复用远端包目录** —— 哪怕 hash 相同、看起来能加载，也会埋下"资源与代码接口不匹配"的雷；
5. **强更体验要提前设计** —— 包体大小、是否走"游戏内下载整包"、以及审核/渠道节奏，都不在框架范围内，但必须在发版流程里定死。

### 16.7 一句话

> **大版本锚定 = 把"必须重装"这件事从"随机发生的意外"变成"显式的版本纪律"**，于是资源层只需要面对"同一兼容窗口内的差量"这一种情况 ——
> 方案里那些"跨版本兼容"的复杂兜底因此不必存在，这也是"最稳妥"的一部分：**不是把边界兜住，而是让边界不存在。**

---

## 十七、平台核实：Android / 小程序上还需要"路径重定向"吗？

> 前提：这个框架**主推 Android + 小程序（微信 / QQ 小游戏，构建目标 = WebGL）**。
> 问题："AB 包的下载路径好像没必要重定向了吧？"
> 结论：**对了一半，而且是关键的那一半 ——**
> **能砍掉的是"按包分流 + 落地/断点续传/文件校验"这一整套（小游戏上完全用不到）；不能砍的是"根可注入"。**
> 在小游戏上，"重定向"甚至就是热更的**全部机制**（因为那边除了 URL 什么都没有）。

### 17.1 先把两个词分开（后面所有结论都建立在这个区分上）

| | **下载路径**（从哪拿） | **加载路径**（从哪读） |
|---|---|---|
| 是什么 | 远端根：`https://cdn/…/{环境}/{平台}/{大版本}/{渠道}` | 运行时把某个包解析到"本地文件 `或` URL"的那个点 |
| 配置形态 | 几个字段（主源 + 备用源 + 环境 + 渠道 + Header） | 一个函数（框架的 `BundlePathResolver` 钩子） |
| 为什么需要 | 换环境（dev/test/prod）、多源降级、签名 URL、大版本隔离 | 让"下载/更新后的那份"真正被加载到（否则热更不生效） |
| 能不能砍 | ❌ 砍不了（但**它只是配置，不是机制**） | ⚠️ 取决于平台（见 17.2~17.4） |

> 你原话里的"下载路径没必要重定向"其实混了这两件事。分开看之后，答案就清楚了：
> **下载侧：不需要"重定向机制"，只需要"一个可配的远端根"；
> 加载侧：Android 需要"根注入"，小游戏需要"根 URL"（而且它就是全部）。**

### 17.2 平台矩阵（事实核实）

| | **Android** | **小程序（WebGL）** | PC（编辑器/Windows） | iOS |
|---|---|---|---|---|
| 首包 AB 放在哪 | APK 内 `StreamingAssets`（**`jar:file://…!/assets`，不是文件路径**） | **没有"包内 AB"这回事**（代码包有体积上限，资源必须走 CDN） | `StreamingAssets` 真实目录 | `StreamingAssets` 真实目录 |
| 引擎怎么读 AB | `LoadFromFile`（**压缩包读不了 jar**）→ 落地或用 `UnityWebRequest` | 只能 `UnityWebRequestAssetBundle`（框架 WebGL 分支就是这么写的：`RevABLoader.cs:444-452`） | `LoadFromFile`（最快） | `LoadFromFile` |
| 有没有可写落地目录 | ✅ `persistentDataPath` | ❌ 没有文件系统语义（`persistentDataPath` 是 IndexedDB 虚拟路径；`File.Exists` / `LoadFromFile` 在小游戏里都不可用 —— 框架 WebGL 分支的注释原文就写了这条） | ✅ | ✅（但审核只允许资源热更） |
| "第二次启动不重复下载"靠什么 | 落地文件（我们自己管） | **引擎的 `UnityWebRequest` 缓存**（IndexedDB / 平台缓存） | 落地文件 | 落地文件 |
| 热更"生效"怎么做 | 让加载指向**落地后的那份** | **换 URL**（版本段变了，缓存自然失效；同名 URL 换内容是不行的） | 指向落地后的那份 | 同 Android |
| 需要"**按包分流**"（哪些包走持久化、哪些走内置）吗 | ❌ **不需要**（简化成"统一落地 + 基线兜底"两级即可） | ❌ **不需要**（只有一个来源：URL） | ⚠️ 可选（想省磁盘才需要） | ⚠️ 可选 |
| 需要"**根可注入**"吗 | ✅ **需要**（否则热更永不生效） | ✅ **需要，且是唯一机制** | ✅ 需要 | ✅ 需要 |
| 断点续传 / 文件级 hash 校验 / 原子落地 | ✅ 需要（我们自己实现） | ❌ **不需要**（交给引擎缓存与引擎校验） | ✅ 需要 | ✅ 需要 |
| 并发建议 | 3 | **2**（单线程 + 内存紧） | 3~4 | 3 |

> 换句话说：**主推的这两个平台把方案的形态"劈成两半"** ——
> **文件型平台（Android / iOS / PC）**：下载器 + 落地 + 校验 + 版本目录（本文档第五章那一整套）；
> **URL 型平台（WebGL / 小游戏）**：清单 + **URL 拼装（含版本段）** + 把 Unity 的 hash/crc 交给引擎；**下载、缓存、校验全是引擎的事**。

### 17.3 Android 核实：为什么"根"仍然必须可注入

推论链（每步都有依据）：

1. 热更生效 = "加载时读到的是**下载落地后的那份**"（这是热更的定义，没有别的做法）；
2. 而框架的加载根是**编译期写死的**：`RevABLoader.cs:61` 的 `StreamingRoot`（`private static`，永远指向 `StreamingAssets/<平台名>/`），`LoadBundle` / `LoadBundleAsync` 也都是 `private static`；
3. 所以在 Android 上**必须有"一个点"能把根换成持久化目录** —— 要么框架给钩子（11 行），要么自研 loader（方案 B：因为 `RevResHandle.BundleAcquired/BundleLoader` 是 `internal`，拿不到释放回调，生命周期语义退化）；
4. 而这**与"按包分流"无关**：采用"首包全量落地 + 只用版本目录一个根 + 基线兜底"之后，解析逻辑变成固定的两级（`当前版本根 → 基线根`），没有配置表、没有按包标记。

**所以在 Android 上：可以砍掉"按包标记"（第十四章降级为可选），不能砍掉"根注入"（第十一章那 11 行仍然要）。**

### 17.4 小程序（WebGL）核实：为什么"重定向"恰恰是全部

推论链：

1. 小游戏里 `AssetBundle.LoadFromFile` / `File.Exists` 都不可用（框架自己那行注释：`RevABLoader.cs:117`），AB 只能通过 `UnityWebRequestAssetBundle.GetAssetBundle(url, …)` 拿到；
2. 而 `url` 的来源在框架里是 **`StreamingRoot + abName`** = `{小游戏发布目录}/StreamingAssets/…`（`RevABLoader.cs:447`）；
3. 也就是说：**不改这个 URL，AB 永远只能来自"发布包自带的那个目录"** → 想热更就只能"覆盖发布目录里的同名文件"，那等于放弃原子切换、放弃回滚、放弃"同版本内多次热更"，而且有 CDN/浏览器缓存一致性风险；
4. 所以小游戏的正确做法是：**资源放到 CDN，URL 里带版本段**（`{cdn}/{平台}/{大版本}/{资源版本}/bundles/hero`），客户端按清单决定用哪个版本 → **"重定向"在这里就是热更的全部**；
5. 但反过来，小游戏上**不需要**：落地目录、`.tmp` + rename、断点续传、文件级 SHA-256（没有"下载完的文件"可以读回来算哈希）→ 这些在小游戏上是纯负担。

小游戏上的替代形态（都在本文档既有章节里，只是换个实现）：

| 能力 | 文件型平台 | 小游戏（URL 型） |
|---|---|---|
| 下载 | `UnityWebRequest` + `DownloadHandlerFile`（落盘） | `UnityWebRequestAssetBundle`（进引擎缓存，**不要用 `DownloadHandlerFile`**） |
| 完整性校验 | 尺寸 + SHA-256（分帧流式） | 把清单里的 **Unity hash（`Hash128`）+ CRC** 交给 `GetAssetBundle(url, hash, crc)`，**由引擎校验** |
| 断点续传 | `Range` + `.part` | ❌ 不做（缓存由引擎管；失败就重试整包） |
| 版本目录 | 本地 `…/<大版本>/<资源版本>/` | **URL 里的版本段**（本身就是"目录"，本地无需任何目录） |
| 回滚 | 指向上一份资源版本 | 指向上一份 URL 版本（引擎缓存里那份还在，但不要依赖它 —— 直接重新拉） |
| 进度 | 文件字节聚合 | 引擎给的是"每个 bundle 的下载进度"，聚合方式相同 |

### 17.5 抽取出的平台化设计（对前面章节的具体改动）

新增一个只有 **两个实现** 的小抽象（各约 30 行），把"文件型 / URL 型"分开：

```csharp
// 下载接收器：文件型落盘、URL 型交给引擎（小游戏）
internal interface IRevHotSink
{
    // 文件型：下载到 .tmp → 校验 → 原子 rename → 版本目录
    // URL 型：不落地，直接返回"该包应使用的最终 URL"（交给框架的 WebGL 分支去加载）
    RevTask<string> PrepareAsync(RevHotBundleInfo bundle, RevHotSource source, RevCancellationToken token);
}
internal sealed class RevHotFileSink : IRevHotSink { ... }    // Android / iOS / PC
internal sealed class RevHotUrlSink  : IRevHotSink { ... }    // WebGL / 小游戏（只拼 URL + 交给引擎校验）
```

| 章节 | 改动 |
|---|---|
| 5.4 下载器 | 保留文件型实现；URL 型退化为"URL 拼装 + 重试 + 进度"（不做续传、不落盘） |
| 5.5 校验 | 校验策略按平台分派：文件型 = 尺寸 + SHA-256；URL 型 = 引擎 hash/crc（清单里必须带 Unity 的 `Hash128` 与 CRC，见附录 A 的补列说明） |
| 5.6 钩子 | 用途表述改为：**"根注入"是必备，"按包分流"是可选**（PC/iOS 想省磁盘时才用按包标记） |
| 第十四章 | "按包标记 + 混合来源" → **降级为可选**（主推平台上用不到） |
| 15.2 决策 5 | "Android 首包落地"的完整表述应为："**文件型平台**首包落地（小游戏没有落地这回事）" |
| 11 章钩子草案 | 不变（那 11 行在两种形态下都要用） |

### 17.6 小游戏专项注意（8 条，全是真金白银的坑）

1. **代码包有体积上限（且平台会调整）** —— 所以"AB 全走 CDN"是前提，不是选择；首包只放"能跑起来的最小可玩集"；
2. **没有文件系统** —— 所有"落地 / 断点续传 / 文件校验"换成"引擎缓存 + 引擎校验"，别照搬 Android 那套；
3. **CORS 必须配** —— 对象存储/CDN 要放开 `GET`、`Range`（引擎可能用）与你们自定义的 Header；配错的表象是"网络错误"，排查极费时间；
4. **缓存策略**：内容文件用**带版本段的 URL**（可长缓存、天然不可变）；清单 URL 必须 **no-cache**（或带 `?t=`）；
5. **并发降到 2、包粒度做小** —— 单线程 + 内存上限，大包会带来内存峰值（这也是"分包粒度"在小游戏上比在手机上更重要的原因）；
6. **平台自己的更新机制管"代码包"，不管"资源"** —— 微信 `UpdateManager` / 分包加载解决的是代码包体积与更新，资源热更仍然是我们这套；
7. **清单里必须带 Unity 的 hash（`Hash128`）与 CRC** —— 引擎的缓存与校验吃的是这个，不是 SHA-256（生成方式见附录 A 的补列说明；实测确认一次）;
8. **首屏要"最小资源集"** —— 小游戏首次进入必然要下载，进度条 + 分阶段加载的体验设计比在手机上更关键（这也是第 4 步"首包瘦身 / 按需下载"在小游戏上是刚需而非可选项）。

### 17.7 回答"有没有必要"（总结）

| 问题 | 结论 |
|---|---|
| 下载路径要不要"可重定向"？ | **要可配置，但不需要"机制"** —— 它就是一个远端根 + 环境/渠道/多源字段。三平台都需要（因为三平台都要从 CDN 拿） |
| 加载路径在 **Android** 上要不要"可重定向"？ | **要（根注入），但可以砍掉"按包分流"**：统一落地 + 基线兜底两级解析即可；第十四章的"按包标记"降级为可选 |
| 加载路径在 **小程序** 上要不要"可重定向"？ | **要，而且它就是热更的唯一机制**（URL + 版本段）；但"落地 / 续传 / 文件校验 / 本地版本目录"这一整套**可以全部砍掉**，交给引擎 |
| 那能砍掉多少？ | 主推 Android + 小游戏时：**砍掉"按包标记表 + 混合来源"（约 250 行编辑器 + 80 行运行时）**；小游戏上再砍掉"落地/sink 的文件实现"那一路（约 150 行）。**框架那 11 行钩子保留**（它是"根注入"，两种平台都要） |
| 一句话 | **"AB 从哪来"这件事在文件型平台上是"本地两个根"，在 URL 型平台上就是"一个带版本段的 URL" —— 两者都不叫"重定向机制"，都只是"一个根"。你直觉里"没必要"的是复杂的那一半（按包分流），而"根"这一半恰恰是热更的命门。** |

---

## 十八、存储与 CDN 选型（Android + 小程序）

> 问题："有没有适合 Android + 小程序的 AB 存储方案？用哪家好？"
> 结论：**对象存储 + CDN，主流云都能做；选谁的决定性因素不是"存储"，而是"小游戏的域名白名单 / 备案 / 缓存规则配得顺不顺手"**。
> 首选 **腾讯云 COS + CDN**（与微信同生态、国内实践最多），次选 **阿里云 OSS + CDN**（团队已有链路就别换）。
> **对客户端代码零影响** —— 我们只用 HTTPS 直链，不引任何云 SDK，换家只改配置。

### 18.1 一句话选型表

| 方案 | 适合谁 | 说明 |
|---|---|---|
| ⭐ **腾讯云 COS + CDN** | 主推小程序 / 微信生态的项目（本项目的首选） | 与微信同生态（白名单、备案、证书流程顺）；国内小游戏社区实践最多；`coscmd` / S3 兼容 API / CDN 规则配置成熟 |
| ⭐ **阿里云 OSS + CDN** | 团队已有阿里云账号/链路 | 能力与 COS 基本等价，节点与文档同样成熟；别为了"换云"而换云 |
| **七牛云 / 又拍云（CDN）+ 任意对象存储做源** | 已经用它们做图片/静态资源，或纯流量大想压成本 | 纯 CDN 厂商价格常更灵活；对象存储的权限/生命周期管理弱于 COS/OSS |
| **火山引擎 TOS + CDN** | 字节系渠道、带宽议价能力强 | 游戏/短视频场景带宽价格有优势 |
| **华为云 OBS / 百度 BOS / 京东云** | 已有对应云账号 | 都可用，选"人熟"的那家 |
| **微信云开发 / 云托管静态托管（CloudBase）** | 小体量、快速验证、不想自己搭 CDN | 省内小游戏最省事的起步方式；体量大了再迁到 COS/OSS + CDN |
| **Cloudflare R2 + CDN** | **海外版**（无出网费，性价比高） | 国内版别用（备案 + 国内节点问题） |
| ❌ 自建（MinIO / nginx + 自建 CDN） | 只用于**内网测试环境** | 玩家侧不推荐：带宽成本、跨网质量、抗攻击都不是自建能扛的 |

### 18.2 小程序的硬约束（选谁都躲不过，务必先看这一节）

| # | 约束 | 对方案的影响 |
|---|---|---|
| 1 | **必须在微信公众平台配置"服务器域名"**（`request` / `downloadFile` / `socket` 三类都要配；域名必须 **ICP 备案 + HTTPS**，不支持 IP、不支持端口） | 这是"能不能下载"的门槛：本地开发可以在开发者工具里勾"不校验合法域名"，**真机/体验版必然校验** |
| 2 | **合法域名数量有上限**（官方给的名额是"十余~二十个"量级，具体以后台/官方文档为准） | ★ 不要用"多域名"做多源降级 —— **把多源/容灾下沉到 CDN 的多源站配置，客户端只认一个域名，用路径区分环境/版本/渠道** |
| 3 | `wx.request` / `wx.uploadFile` / `wx.downloadFile` **各 10 个并发**（微信官方文档）；Unity/团结引擎在小游戏底层也是走这套网络接口 | 下载并发设 **1~2**（我们还有游戏逻辑在同一条主线程上跑） |
| 4 | 引擎在底层用 **XHR 访问远程资源 + 用微信小游戏的文件存储系统做缓存**（微信官方文档《使用 Unity/团结引擎适配》+ 团结引擎《微信资源缓存》） | 印证第十七章：**小游戏不用自己做落地/续传**，缓存交给引擎；但"版本段"必须体现在 URL 上，否则缓存永远命中旧的 |
| 5 | 跨域（CORS） | 对象存储/CDN 要放开 `GET`（含 `Range`，引擎可能用）与自定义 Header；配错的表象是"网络错误" |

### 18.3 Android 侧的约束（宽松得多，但有几条会踩）

| # | 约束 | 说明 |
|---|---|---|
| 1 | **`Range` 必须可用** | 断点续传靠它；COS/OSS/CDN 默认支持，**但 CDN 侧若开了"分片缓存/拼接"等特殊配置要确认没关掉 Range** |
| 2 | **证书链必须完整** | 缺中间证书会导致一部分老机型直接失败（这类故障只在小批量机型上出现，最难查） |
| 3 | TLS 1.2+ | Android 5.0+ 基本无虞，但请确认 CDN 没只开 TLS 1.0/1.1 的老配置 |
| 4 | 域名数量不受限、可多源 | 但**为了与小程序行为一致，仍建议"客户端一个域名 + CDN 多源"**（同一条代码路径，少一套分支） |
| 5 | DNS 劫持/解析慢 | 可选 HTTPDNS（腾讯云/阿里云都有）——**本方案不引**（保持零依赖），真遇到再上 |

### 18.4 推荐组合与理由（为什么首选腾讯云 COS + CDN）

| 理由 | 说明 |
|---|---|
| **同生态** | 微信小游戏的域名白名单 / 备案 / 证书 / CDN 加速是腾讯云最熟的一条链路；出问题时能查到的**同场景案例最多** |
| **政策与工具** | `coscmd`（上传）、S3 兼容 API（CI 里 `aws s3 cp` 也能用）、CDN 路径级缓存规则、回源鉴权/私有读、生命周期规则（自动清旧版本目录）——**我们在 18.5 需要配的每一条它都有现成开关** |
| **成本可控** | 存储/流量单价都是"量级参考：存储 ~0.1 元/GB/月，CDN 流量 ~0.2 元/GB（国内、按量，具体以官网报价与合同为准）" |
| **可迁移性** | 因为客户端只用 HTTPS 直链、**不引 SDK**，将来换到阿里云只是"换一个域名 + 重新上传" —— 没有沉没成本 |

> 什么时候选阿里云：团队已有账号/账号体系/财务流程在那边，或者已有 CDN 用量合同 —— **能力的差异远小于流程的差异**。
> 什么时候选七牛/又拍：已经用它们做别的静态资源，或纯流量大且能谈到更好的带宽价。
> 什么时候先别上云：**开发期用本地静态服务**（文档第 14/17 章都提到 `起本地CDN.cmd`），联调通了再上测试桶 —— 别一上来就烧流量。

### 18.5 云端要配的东西（清单，缺一条都会变成"玄学 bug"）

| # | 要配什么 | 具体设置 | 不配的后果 |
|---|---|---|---|
| 1 | **目录结构** | `/{环境?}/{平台}/{大版本}/{渠道?}/{资源版本}/…`（第十六章） | 老客户端拉到新资源 → 线上事故 |
| 2 | **缓存规则（内容）** | 资源包路径：`Cache-Control: max-age=31536000, immutable`（URL 带版本段，天然不可变） | 每次都回源，流量白白翻倍 |
| 3 | **缓存规则（清单）** | 清单路径：`no-cache`（或客户端加 `?t=` 破缓存） | "更新了但玩家拿到旧清单"（最经典的假 bug） |
| 4 | **CORS** | 允许 `GET`/`HEAD`、`Range`、自定义 Header；`Access-Control-Allow-Origin` 按需（可先 `*`） | 小游戏报"网络错误"，排查极费时间 |
| 5 | **Range** | 确认 CDN/存储均支持分片请求（默认支持） | Android 断点续传失效 |
| 6 | **私有读 + 回源鉴权**（可选） | 桶设为私有，CDN 用回源鉴权/签名 URL 访问；客户端只拿 CDN 域名 | 资源被人直接盗链刷流量 |
| 7 | **上传顺序** | **先传所有内容、最后传清单**（清单是原子切换开关） | 玩家拿到"半新半旧"的组合 |
| 8 | **生命周期规则** | 旧版本目录保留 N 天（如 30 天）后自动删除 | 存储费用只涨不降；也避免"测试拿到几个月前的资源" |
| 9 | **预热（可选）** | 大版本发布时对"首屏必需包"做一次 CDN 预热 | 首发当天大量回源，首屏慢、回源带宽打满 |
| 10 | **备案与证书** | 域名备案 + 有效证书（含中间证书）+ 小程序白名单三处都要做 | 小游戏真机直接不可用 |

### 18.6 成本量级：为什么"差量更新"能直接省出云费

```text
下载量 ≈ 单次更新体积 × 更新次数 × 活跃玩家数

① 不做差量（每次全量 1 GB）：
   1 GB × 1 次/周 × 10 万玩家 ≈ 400 TB/月 → 按 ~0.2 元/GB ≈ 8 万元/月量级
② 做差量（每次更新 50 MB）：
   50 MB × 1 次/周 × 10 万玩家 ≈ 20 TB/月 → ≈ 4 千元/月量级

（单价为国内 CDN 常见的量级参考，实际以官网报价/合同为准；真正的结论是：
  "只下变化的包"这件事本身就是最大的一笔成本优化，而不是把 CDN 换一家。）
```

### 18.7 和小游戏平台自带的方案什么关系

| 方案 | 说明 | 取舍 |
|---|---|---|
| **Unity / 团结引擎官方的 AutoStreaming + UOS CDN** | 引擎侧给的"StreamingAssets 分离 + CDN 托管"方案；省去自己写清单/下载 | 绑定引擎与它的 CDN；清单/版本/回滚这些语义不透明，且与框架的 AB 链路是两套。**本项目不采用**（我们已经有自己的清单与版本模型），但值得知道它的存在 |
| **微信小游戏"分包加载"** | 解决**代码包**体积与更新 | 管不到 AB；AB 仍走 CDN（两条路各管一段） |
| **平台 UpdateManager** | 管"小游戏版本更新"（含强制更新提示） | 与我们的资源更新并行：代码包更新走平台，资源更新走 RevHotUpdate |

### 18.8 一句话结论

> **存储用"对象存储 + CDN"（首选腾讯云 COS + CDN），真正的活在于"路径带版本段 + 两套缓存规则 + CORS/Range/备案白名单"这几项配置；
> 客户端始终只认"一个 HTTPS 域名 + 路径模板"，不引任何云 SDK —— 所以选谁都能换，选谁都不影响代码。**
> 而省钱的关键不是"换一家 CDN"，是**只下变化的包**（18.6）。

---

## 附录 A：清单样例全文

```text
# RevHotManifest v1 —— 行式文本，与 ResMap.txt 同族（# 开头是注释，空行忽略）
# 字段：@key|value / @bundle|包名|hash|字节数|依赖(逗号分隔)|标签(逗号分隔)
@appVersion|1.2.0            ← 大版本锚点（= Application.version）；不匹配 → 拒绝更新并引导强更（第十六章）
@resVersion|1.2.0.37         ← 资源版本（同一大版本内自增；玩家看到的就是它）
@minAppVersion|1.2.0         ← 兼容下限（补丁包场景；一般 == @appVersion）
@baseManifest|Baseline.txt   ← 该大版本的基线清单（随包体发布的那份，供差量计算与回滚）
@version|1.2.0-p1            ← 打包工具的版本字符串（给人看，进 BuildManifest.json 的那个）
@platform|Android
@channel|official
@env|prod
@hashAlgo|sha256
@builtAt|2026-10-01 12:00:00
@bundleCount|6
@firstPackageBundles|common,ui_login

# 映射表也要热更（新增资源全靠它）
@resmap|ResMap.txt|3f7a1c9d4b6e8f20a1c2d3e4f5a6b7c8d9e0f1a2b3c4d5e6f708192a3b4c5d6e|20481

# 包：名称|hash|字节|依赖|标签（以下仅展示格式；省略号不是可用 hash，标签不代表已实现按需下载）
@bundle|common|7c1e...|1572864||builtin,hot
@bundle|ui_login|9b2f...|1048576|common|builtin,hot
@bundle|ui_bag|1a3d...|2097152|common,ui_login|hot
@bundle|hero|5e6f...|15728640|common|hot
@bundle|effect_battle|8d9c...|8388608|common,hero|hot
@bundle|scene_arena|2b4a...|25165824|common|hot,optional

# ★ 目标平台含 WebGL / 小游戏时，每行末尾再补两列（交给引擎做缓存与校验，见第十七章）：
#    @bundle|名称|sha256|字节|依赖|标签|unityHash|unityCrc
#    unityHash = AssetBundleManifest.GetAssetBundleHash(包名).ToString()
#    unityCrc  = 该包 .manifest 里的 CRC（无签名数字型）
# 例：@bundle|hero|5e6f...|15728640|common|hot|f3a1c2...|3524891730
```

## 附录 B：一次完整热更的日志长什么样

```text
[HotUpdate] 大版本 1.2.0（Application.version）· 本地资源版本 1.2.0.36（{persistent}/RevHotUpdate/Android/1.2.0/1.2.0.36）
[HotUpdate] 拉取清单：https://cdn.example.com/gameA/prod/Android/1.2.0/official/RevHotManifest.txt?t=1759300000 （18.4 KB，82 ms）
[HotUpdate] 远端：资源版本 1.2.0.37（builtAt 2026-10-01 12:00:00，appVersion 1.2.0 ✓ 与当前包体一致）
[HotUpdate] 差量（对比基线 Baseline.txt）：新增 1、变更 2、删除 0、映射表 有更新 → 需下载 17.8 MB（3 个文件）
[HotUpdate] 下载 1/3 ui_bag 2.0 MB ✓（sha256 校验通过，412 ms，4.9 MB/s）
[HotUpdate] 下载 2/3 hero 15.0 MB ✓（sha256 校验通过，3.1 s，4.8 MB/s）
[HotUpdate] 下载 3/3 ResMap.txt 20.0 KB ✓
[HotUpdate] 写入版本目录 1.2.0.37 → 打完成标记 .stamp → 切换 current.txt（原子替换）
[HotUpdate] 清理旧版本：删除 1.2.0.35（同一大版本内保留 2 份）
[HotUpdate] 装钩子：BundlePathResolver = RevHotStore.ResolveBundlePath（根注入），ResMapOverride = RevHotStore.BuildResMap
[HotUpdate] 重装资源策略（RevResBootstrap.Init）→ 就绪，总计 3.9 s

# 小游戏（WebGL / URL 型平台）同一次更新长这样 —— 没有落地、没有续传，缓存与校验交给引擎：
[HotUpdate] 大版本 1.2.0 · URL 型平台：资源直接走 CDN（不落地），版本段 1.2.0.37
[HotUpdate] 资源根：https://cdn.example.com/gameA/prod/WebGL/1.2.0/official/bundles/
[HotUpdate] 差量（对比内置基线）：变更 2、映射表 有更新 → 需加载 17.0 MB（引擎缓存命中 0）
[HotUpdate] hero 15.0 MB → 17.0 MB 已由引擎缓存/校验完成（hash + crc 均已下发）→ 总计 6.2 s
```

## 附录 C：实施顺序建议（最不容易返工的顺序）

```text
① 先定清单格式与解析（它同时决定编辑器工具与运行时的接口）—— 写完就跑工程外断言
② 再定盘上布局（版本目录 / tmp / current / 原子提交）—— 这是"能不能续传/回滚"的地基
③ 然后写下载器（先单文件跑通，再加并发/重试/多源/续传）
④ 最后接框架钩子（此时"文件已经到位"，钩子只是把路径指过去 —— 最不容易出问题）
⑤ 编辑器工具放在 ④ 之后（此时清单格式已经冻结，工具只是"填数据"）
```

---

## 十九、王者源码对照与修订后的实施方案

> **一句话总结：王者可借鉴的不是 QTS/IIPS 的体量，而是“启动流程分阶段 → 必需包与版本兼容先过关 → 安装/校验/挂载成功才对外宣布可用 → 中断和损坏有可执行的恢复路径”。** 本节记录 2026-10-13 对 `F:\system\Program\GameScripts` 与本仓库**实施前**源码的静态审阅；P0/P1 已随后按本章开始编码，下表“当前实现”仍是实施前风险快照。尚未运行 Unity、真机或故障注入，未验收的目标不得宣称已上线。

### 19.1 王者真实链路：迁移“关口”，不迁移基础设施

| 源码证据（绝对路径、行号） | 看到的机制 | RevHotUpdate 可借鉴的原则 |
|---|---|---|
| `F:\system\Program\GameScripts\BaseScripts\Domestic\Framework\GameState\VersionUpdateState.cs:142-174,240-249`；`F:\system\Program\GameScripts\SystemScripts\Domestic\GameSystem\VersionUpdate\chs\CQtsvfsVersionUpdateSystem.cs:679-755` | 版本更新在启动状态内执行，国内版按 App 更新、更新前配置、本地包处理/平台抽取、推荐下载、安装、整理、修复、导出、加载组织动作；Editor/`SGAME_LITE` 有跳过分支 | 更新是**进游戏前的有序门禁**，不是资源加载时临时发现新文件；每阶段失败都要有明确出口。`GLOBAL_GENERAL` 另有流程，不能把国内分支说成所有平台的统一实现 |
| `F:\system\Program\GameScripts\Plugins\Shared\Resource\ResourcePackage\CResourcePackageManager.cs:90-145`；`F:\system\Program\GameScripts\SystemScripts\Domestic\GameSystem\VersionUpdate\VersionUpdateAction\CVersionUpdateRecommendDownloadAction.cs:414-456` | internal 包列为必装/基准，需求列表与推荐下载列表分别处理 | 抽象“**启动必需集**”与可选资源，不照搬包 ID/下载器：必需集完整性先于进入游戏，其余按玩法触发（后期需求） |
| `F:\system\Program\GameScripts\Plugins\Shared\Resource\ResourcePackage\updater\CResourceDownLoaderOnStart.cs:359-456`；`F:\system\Program\GameScripts\SystemScripts\Domestic\GameSystem\VersionUpdate\VersionUpdateAction\CVersionUpdateGetPreUpdateConfigAction.cs:27-150` | 目标版本及文件信息由更新服务返回；缓存配置在使用前做 MD5 比对 | **目标版本与待安装集有单一来源**，先验配置/版本，再计划下载；主链使用 IIPS/fileInfo、`phoneSpecList.tsv` 等，并非一个 `manifest.json` |
| `F:\system\Program\GameScripts\SystemScripts\Domestic\GameSystem\VersionUpdate\VersionUpdateAction\CVersionUpdateExportAction.cs:37-61`；`F:\system\Program\GameScripts\Plugins\Shared\Resource\QtsvfsResourceSystem\CQtsvfsPackageManager.cs:2949-2989` | 安装后的包先检查可用、挂载成功，再导出/进入加载 | **已下载 ≠ 已生效**：候选版本须验证“能装载”，失败不得被标成 Ready |
| `F:\system\Program\GameScripts\Plugins\Shared\Resource\QtsvfsResourceSystem\CQtsvfsPackageManager.cs:554-606` | 启动时发现中断的 merge，尝试恢复；损坏包可卸载，其他恢复错误向上返回 | **恢复是明确状态机**：中断、坏包、未知错误分开处理；这不是“任意旧版一键回滚” |

**不能搬的部分：**IIPS/Apollo/QTS native、QTS 包格式/合并算法、TSV 字段、包 ID、按平台编译宏，都不是轻量 Unity AB 扩展包的依赖；继续复用 `RevABLoader` 与 `RevTask`，保持“导入即有、不导入零影响”。王者的 App 更新（商店/APK）与资源安装分层；其登录协议热修复 `F:\system\Program\GameScripts\BaseScripts\Shared\HotFix\CHotFixSystem.cs:46-105`、Puerts 模块覆盖 `F:\system\Program\GameScripts\SystemScripts\Domestic\GameSystem\PandoraSystem\PandoraSys_Puerts.cs:1992-2014` 属**另一条链**，不据此推断 C# 代码热更。

### 19.2 当前实现的边界（以下才是“已实现”）

| 环节 | 真实代码与现状 | 未兑现的旧承诺 |
|---|---|---|
| 框架接缝 | `RevolutionFrameWork_Unity\Assets\Revolution.HotUpdate\Runtime\Integration\RevHotResBridge.cs:31-56` 实际装 **4 个**钩子：`BundlePathResolver`、`BundleCacheKeyResolver`、`DependenciesOverride`、`ResMapOverride` | 前文第 3/4/11/14/15 章的“仅两处钩子/仅路径+映射表”是历史草案；新依赖与 WebGL 缓存也要随清单同步 |
| 计划与目录 | `...\Runtime\Pipeline\RevHotPlanner.cs:27-83` 同名 hash 相同直接跳过；`...\Runtime\Pipeline\RevHotStore.cs:243-273,750-803` 按当前目录→历史目录→首包找**存在**的包，旧目录按数量清理 | 当前并非“完整快照版本”：未变包可能只在被清理的旧目录；指回旧版本也可能命中更新的包，**不能承诺任意版本回滚** |
| 下载/恢复 | `...\Runtime\Download\RevHotDownloader.cs:266-383` 用 `.part`/Range 重试，但每次开始第一个源就删除 `.part`（273-278）；不支持 Range 时改从头下载 | 不保证杀进程后跨启动续传；换源不能安全拼接不同源片段；只靠计划 hash 并不复验历史文件 |
| 激活 | `...\Runtime\Facade\RevHotUpdate.cs:352-390` 先 stamp、切 `current`、清旧目录，**再** `ReinitResourceSystem`；其失败会返回错误 | “失败时 current 一定未变”“切版本后一定能加载旧版”“坏包绝不加载”均不是当前代码保证 |
| 清单/自检 | `...\Runtime\Core\RevHotManifest.cs:150-256` 检查必要版本、行格式和可选 bundleCount；`...\Editor\RevHotManifestBuilder.cs:168-219` 自检存在、大小与直接依赖 | 尚不校验每包内容 hash、包名路径安全/重名/自依赖/循环依赖/完整依赖闭包；tags/首包集合未形成可用的按需策略，`HashAndCrc` 不是已完成的加载期校验 |
| 平台 | 文件型先下载再加载；WebGL 不预下载 AB，依靠 `RevABLoader` 按 URL/可选 hash+CRC 拉取，参考 `...\Runtime\Pipeline\RevHotStore.cs:247-251,315-349` | 文件下载器的备用源、附加 Header、Range、SHA 文件校验**不会自动套到 WebGL AB 请求**；小游戏容器的缓存/CORS/域名限制仍需实测 |

> 上表中 `...` 均以 `RevolutionFrameWork_Unity\Assets\Revolution.HotUpdate` 为前缀；引用的行号基于本次静态审阅，后续改代码以实际版本复核。`Hash`/`CRC` 是完整性和缓存材料，不等于**清单来源可信**；若要抵御被篡改的清单需另行设计签名与公钥分发。

### 19.3 优化目标：轻量“候选版本门禁”，按失败代价分期

| 优先级 | 目标方案（尚未实现） | 为什么；通过条件 |
|---|---|---|
| **P0：启动必需集** | 当前清单明确主包、依赖闭包、ResMap 和首屏必需 AB。检查目标 App/平台/渠道、所有包路径合法、hash 非空且为 SHA-256、大小、名称唯一、无自依赖/环与依赖闭包；生成期与客户端加载前双重校验。未通过 → 不进入新版本 | 把王者 internal 必装基准转为可验证的“进入游戏的最低可玩集”；断言包含缺主包、缺依赖、跨平台清单、恶意相对路径 |
| **P0：内容索引与留存** | 为目标版本生成“包名 → {期望 SHA-256、实际来源目录/内置基线}”解析表；**只允许来源包 hash 与目标清单一致**。保留/清理旧目录前先计算新旧每个可启动版本的引用闭包；依赖旧目录的包要硬链接/拷贝进候选完整快照，或禁止删除被引用目录（二选一，先优先可审计的完整快照） | 消灭“历史目录任意存在即捡到文件”造成混版；10 次只有部分包变更、保留目录 2 份后仍能加载全量目标资源，断言每包 hash 与目标清单一致 |
| **P0：提交/恢复协议** | `Check → Plan → Download → Verify → Stage → ValidateCandidate → Activate → Reinit → Ready`；候选目录内容与依赖、映射表**整组**通过，才可提交指针。切指针前记录前一可用版本；指针切换后 `Init`/主包加载失败时恢复旧指针、重装旧策略，并暂缓删除旧依赖；断电/重启时根据阶段标记重试或撤销未完成候选 | 这才是“可恢复切换”，不是仅凭 `.stamp` 或手改 `current.txt`；故障注入覆盖下载中断、校验失败、切指针后 Init 失败、修复失败，任何失败都不能报告 Ready |
| **P0：业务静默切换点** | `RevHotResBridge.ReinitResourceSystem()` 会 `ShutdownAll → Init`；计划在启动未开放资源使用前，或由业务明确退出当前玩法/释放所有资源句柄后切换；检查活动引用/在途加载，非静默时只下载并暂存，下次安全点生效。`RevScope` 可负责收束玩法域，但不能盲目 `ExitAll` 或让热更包强制关闭 UI/场景 | 防止战斗中卸载仍被使用的 AB；验收“战斗过程中发现更新只下载不激活，回到启动/安全点后生效”，旧版在整个过渡期保持一致 |
| **P1：跨启动续传** | 首次进入同一源不要删 `.part`；按“目标版本+包名+期望 hash”记录半成品，变更源时清半成品或先验证同内容；接受 `206` 还须核验 `Content-Range` 起点，`200` 安全从头重试。取消时保留可恢复部分；校验失败删除并从 0 开始 | 断网/杀进程后确实只传剩余字节，错误 Range/换源不拼错文件；未经测试不在使用说明承诺此能力 |
| **P1：发布门禁** | 保留现有清单/编辑器工具；生成后从**最终上传目录**重算 SHA-256/大小、查 ResMap 引用与主包依赖闭包；拒绝空 hash/重复包/路径穿越/自依赖；检测平台渠道与基线一致。内容先上传、逐文件验证可取，再上传清单；窗口暂无上传脚本按钮，CI 集成单独安排 | 王者“安装前配置校验 + 可用性检查”移到编辑器门禁，减少将坏清单发给玩家；不可把现有 `SelfCheck`（只验大小）当完整发布门禁 |
| **P2：可选分包** | 待 P0 完成后，清单声明启动必需包 + 玩法可选包及依赖闭包，按业务入口显式请求，失败只影响该玩法，不破坏已激活版本。不要引入 QTS native 二进制 diff | 只借鉴“必需/推荐分离”，不为未有真实需求的项目强制增加下载状态机 |

**失败策略必须按阶段区别处理：**未开始更新且本地有**已验证可用**基线时，可按业务策略继续旧版/离线；资源与代码不兼容的强更、首屏必需包缺失时不可静默使用内置旧包；候选版本校验或挂载失败时不得切新；已经切新但重装失败则尝试受保护旧版，旧版也不可用就停在明确错误/修复入口，绝不“成功进游戏”。每阶段记录 App/资源版本、包名、目标 hash、实际来源、错误码、是否回退；不得在日志里输出令牌或签名 URL 的敏感参数。

### 19.4 平台分路与验证矩阵

| 平台 | 应保留的机制 | 额外门槛 |
|---|---|---|
| Android / PC / iOS（文件型） | 包级差量、首包/StreamingAssets 基线、`.part` + SHA-256 校验、版本目录；Android 首包从 APK 拷出 | 多次差量 + 旧目录清理 + 重启后续传 + 切指针后失败回退。iOS 对下载资源与内容审核做独立评估，不用本文替代平台规则 |
| Unity WebGL / 小游戏（URL 型） | 清单选择目标 URL，AssetBundle 使用引擎加载与缓存（Hash128/CRC 有值时传入） | **主包来源先验证**：主包若仍走 `StreamingAssets`，不能承诺“所有 AB 都来自 CDN”；AB 请求并不走文件下载器的多源/Header；微信/抖音等容器的合法域名、CORS、缓存容量与 URL 版本隔离分别真机测，无缓存/校验键时不能声称引擎保证 SHA-256 |

**验收最小集合（未执行）：**新装机离线基线；损坏本地包（同 hash 清单但文件被篡改）；10 次小增量且 `KeepVersions=2`；老版指针不能加载新版包；ResMap 删除/重命名包；断网、杀进程、Range 200/206 异常；manifest 截断/重名/路径穿越；候选验证失败；**指针切换后 Init 失败**；WebGL 主包与依赖同版本、URL/CORS/缓存测试。以上每项必须记录期望版本、实际包来源与结果，不能用“跑通 Demo”替代。第八章旧“8 项全过”是**验收清单，不是已通过报告**。

### 19.5 保留边界与最终决策

- **保留**独立包、4 个可空接缝、现有 `RevResManager` 业务 API、行式清单、包级差量、App 版本目录隔离、对象存储/CDN 的“内容先、清单后”原则；不加云 SDK、不复制 QTS/IIPS。
- **修正**第 5/7/8/11/14/15/16/17/18 章过度保证：旧方案中的任意旧版回滚、坏包绝不加载、跨启动续传、旧目录随意清理、WebGL 全走 CDN、已有 CI 上传脚本和“只加 2 个钩子”，均只能在上述门禁实现并验收后宣称。`ResMap` 合并只能覆盖/新增同名键，**不能表达删除旧键**，配置破坏性重构必须拒绝或另设计全量替换语义。
- **不做**代码热更、热修复命令执行、二进制差量、QTS native、灰度后台。王者的 `CHotFixSystem` 与数据表处理器属于独立生命周期与信任边界，不嵌进 `RevHotUpdate`。如未来确需脚本热更单独立项并做签名/审计/平台审核；本轮先保证资源更新在失败时**不发布一个不可用版本**。

**本篇结论：先把“目标版本每个包从哪里来、是否真能加载、失败后如何回到已验证版本”做成可检查的事实，再讨论分包与体验优化；王者的价值在更新门禁与恢复策略，不在把重型原生包系统照搬进轻量框架。**

---

*对应代码：`RevolutionFrameWork_Unity\Assets\Revolution.HotUpdate\` 与 `RevolutionFrameWork_Unity\Assets\Revolution\Runtime\RevResourceSystem\`；参考源码：`F:\system\Program\GameScripts\`。P0/P1 已开始编码，故障注入及 Unity/真机验收仍待执行。*
