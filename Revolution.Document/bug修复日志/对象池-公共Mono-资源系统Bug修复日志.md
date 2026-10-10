# 对象池、公共 Mono、资源系统 Bug 修复日志

> **本轮摘要**：静态审计 `RevObjectPool`、`RevPublicMono`、`RevResourceSystem` 全部运行时代码，并修复了确认的资源租约、对象池代际、回调派发、作用域、缓存类型和异步取消清理问题。审计不能证明“没有任何 Bug”；本轮 Unity MCP 会话未能连接，因此最后一轮 Unity PlayMode 回归没有完成。
>
> - 审查日期：2026-10-10
> - 审查范围：`Assets/Revolution/Runtime/RevObjectPool`、`RevPublicMono`、`RevResourceSystem`，以及它们共用的 `RevTaskScheduler` 生命周期接缝
> - 构建：`RevolutionFrameWork_Unity.sln` 编译成功，0 个错误；本次最终构建有 2 个 Unity 依赖版本警告（完整重建时还会显示既有 Demo 字段未赋值警告）
> - 静态检查：三个目标目录 lint 无诊断；`git diff --check` 通过
> - Unity 测试：MCP 当前连接失败，未完成编译后的 Console 检查和 PlayMode 测试；EditMode 测试运行器此前没有注册用例

---

## 一、对象池 `RevObjectPool`

### 1. 异步取对象时，池与调用方共用同一份资源引用

- **严重度：高**
- **现象**：调用方按 `LoadAsync` 句柄契约释放 `RevPool.GetAsync` 返回值后，池本身仍以为持有同一份引用；池内 prefab 可能提前卸载，销毁池时又可能多减一次。
- **修复**：异步加载的原始引用作为调用方租约返回；新建池时额外 `AddRef`，由池独立持有。池命中时仍为本次调用获取独立句柄。补充文档，说明调用方用完返回的 `RevResHandle` 后应调用 `RevResManager.DecRef(handle)`。
- **文件**：`RevObjectPool/Core/RevGameObjectPools.cs`、`RevObjectPool/Facade/RevPool.cs`

### 2. `ClearGroup` 只清空闲对象，却被描述成可配对资源组卸载

- **严重度：高**
- **现象**：执行 `RevResBootstrap.Shutdown(group)` 后，池注册表、池持有的 prefab 引用和借出实例仍可能存在；之后同路径取对象可能继续命中旧池。
- **修复**：保留 `ClearGroup` 原有“只清空闲实例、池和引用仍保留”语义；新增 `RevPool.DestroyGroup(group)`，它摘除并销毁该组所有池、空闲/延迟/借出实例，并归还各池 prefab 租约。资源组整体卸载顺序改为先 `DestroyGroup`，再 `Shutdown(group)`。
- **文件**：`RevObjectPool/Facade/RevPool.cs`、`RevObjectPool/Core/RevGameObjectPools.cs`、`RevObjectPool/Core/RevGameObjectPool.cs`；同步对象池使用说明与架构解析 HTML / Markdown。

### 3. `Rebind` 后旧代借出对象可能进入新 prefab 池

- **严重度：高**
- **现象**：原 prefab 被卸载 / 替换时，旧的借出实例仍带相同 `PoolId`；归还后可能进入绑定新 prefab 的池，后续 Get 取到旧 prefab 实例。
- **修复**：跟踪当前借出实例；池 `Rebind` / `Dispose` 时强制销毁旧代活动对象。销毁对象前清掉它的池身份，防止延迟销毁期间被归还到新池。Rebind 时同时摘除旧 prefab ID 索引。
- **文件**：`RevObjectPool/Core/RevPoolCore.cs`、`RevObjectPool/Core/RevGameObjectPool.cs`、`RevObjectPool/Core/RevGameObjectPools.cs`、`RevObjectPool/Core/RevPooledMember.cs`

### 4. `OnPoolGet` 晚于 `OnEnable`

- **严重度：中**
- **现象**：重新激活对象会先触发业务 `OnEnable`，之后框架才调用 `OnPoolGet` 重置数据，`OnEnable` 可能读到上次使用残留状态。
- **修复**：在对象挂到目标父节点后，先调用 `OnPoolGet`，再 `SetActive(true)` 触发 `OnEnable`。
- **文件**：`RevObjectPool/Core/RevGameObjectPool.cs`

### 5. prefab 已销毁后 `DestroyPool(prefab)` 不能通过保存的实例 ID 清理池

- **严重度：中**
- **修复**：入口区分 C# null 与 Unity fake null；用保存的 instance ID 找池，并允许以同一已销毁 prefab 引用销毁对应池。
- **文件**：`RevObjectPool/Core/RevGameObjectPools.cs`

### 6. `ActiveCount` 将延迟回收对象误报为仍在使用

- **严重度：低**
- **修复**：用引用身份集合跟踪当前借出对象；归还时立即移出 Active，延迟对象仅计入 `Recycling`。同时拒绝非活动对象归还与 `OnPoolReturn` 中重入归还。
- **文件**：`RevObjectPool/Core/RevPoolCore.cs`、`RevObjectPool/Core/RevPoolDefines.cs`；同步架构文档。

---

## 二、公共 Mono `RevPublicMono`

### 7. 异常报告处理器抛错会中断同相位其余监听者

- **严重度：中**
- **修复**：`Failed` 和 `OnException` 的每个处理器单独隔离；报告器抛错只记录日志，不再中断当前相位后续监听者。协程失败 / 非 Play 错误也走相同的安全报告入口。
- **文件**：`RevPublicMono/Implementation/RevMonoCore.cs`、`RevPublicMono/Support/RevMonoDriver.cs`

### 8. Dispose 后 Scope 还能添加监听或启动协程

- **严重度：中**
- **修复**：已释放 Scope 拒绝继续添加每帧监听和启动协程。`RevMono.RemoveAllOf(scope)` 现在同时移除 Scope 的监听并停止其协程。
- **文件**：`RevPublicMono/Support/RevMonoScope.cs`、`RevPublicMono/Facade/RevMono.cs`

### 9. Scope 协程句柄泄漏、首个 yield 前重入关闭的竞态，以及嵌套协程未受异常保护

- **严重度：中**
- **修复**：Scope 协程跟踪器在自然结束时移除句柄；用版本号处理 `StartCoroutine` 首次推进时重入 `Close/Dispose`；停止时先快照句柄，避免 Stop 导致迭代列表被修改；嵌套 `IEnumerator` 由单层跟踪器逐级推进，异常继续落入统一隔离路径。
- **文件**：`RevPublicMono/Support/RevMonoScope.cs`、`RevPublicMono/Support/RevMonoDriver.cs`

### 10. 关闭 Domain Reload 后驱动状态 / 旧协程状态不一致

- **严重度：中，需目标 Editor 配置做运行验证**
- **修复**：新会话复位时停止仍存活的 `RevMonoDriver` 宿主上的上一局协程；宿主仍有效时同步恢复 `DriverReady`，避免 `IsRunning` 为 false 但驱动仍存在。
- **文件**：`RevPublicMono/Support/RevMonoDriver.cs`、`RevPublicMono/Support/RevMonoUnityHooks.cs`

### 11. 公共相位 API 收到未定义 enum 会越界

- **严重度：低**
- **现象**：调用泛型 `RevMono.Add/Remove` 或内部 `Tick/CountOf` 时传入 `(RevMonoPhase)999`，会按数组索引越界。
- **修复**：校验相位；非法 Add 安全失败并报 `InvalidPhase`，非法 Remove / Tick 安全返回，CountOf 返回 0。
- **文件**：`RevPublicMono/Core/RevMonoDefines.cs`、`RevPublicMono/Implementation/RevMonoCore.cs`

---

## 三、资源系统 `RevResourceSystem`

### 12. 同路径不同内容类型会错误复用缓存句柄

- **严重度：中**
- **现象**：同一资源路径先按一种 Unity 类型加载，再用不兼容类型请求时，原代码命中路径缓存，泛型强转返回 null，但没有 `TypeMismatch` 失败原因；在途请求类型不同时也可能被合并。
- **修复**：同步命中、异步就绪命中和在途合并前均检查目标类型是否可满足；不兼容时返回独立 `TypeMismatch` 失败句柄，不增加原句柄引用数。
- **文件**：`RevResourceSystem/Facade/RevResManager.cs`

### 13. 同步加载失败并 Release 后不能重试

- **严重度：中**
- **现象**：同步失败句柄引用归零后仍留在缓存；再次同步 Load 继续命中旧失败句柄，不会重新走策略。异步路径原先已有失败句柄重试逻辑，行为不一致。
- **修复**：同步和异步加载在失败句柄已完成且 `RefCount <= 0` 时，先释放其已取得的 AB 租约并摘除旧缓存，再尝试新加载；仍被持有的失败句柄不被替换。
- **文件**：`RevResourceSystem/Facade/RevResManager.cs`

### 14. AB 取消 / 资源组卸载可能重复释放共享依赖

- **严重度：高**
- **现象**：异步 AB 资源已取得目标包后，资源组 Shutdown 先通过句柄释放包和依赖；加载任务稍后遇到取消又按本地依赖列表释放一次，可能把其它资源仍在使用的共享依赖减到 0 并卸载。
- **修复**：目标包取得后，把本次准确获取的依赖列表所有权转移到 `RevResHandle` 并清空任务本地列表。句柄释放按取得时快照归还依赖，不在释放时重新查询可能已变化的依赖覆盖钩子。这样晚到取消路径不会重复扣减。
- **文件**：`RevResourceSystem/Core/RevResHandle.cs`、`RevResourceSystem/Implementation/Loaders/RevABLoader.cs`、`RevResourceSystem/Facade/RevResManager.cs`

### 15. 直接调用 `UnloadGroup` 会跳过在途任务

- **严重度：中**
- **修复**：`RevResManager.UnloadGroup` 自身先取消该组的等待 / 加载任务，再清理缓存，不再要求调用方必须绕经 `RevResBootstrap.Shutdown` 才能处理在途任务；Bootstrap 的重复取消是幂等的。
- **文件**：`RevResourceSystem/Facade/RevResManager.cs`、`RevResourceSystem/Support/RevAsyncLoadPump.cs`

### 16. Domain Reload 关闭后旧加载泵状态可能永久阻塞新会话

- **严重度：高，涉及新会话生命周期**
- **现象**：上一局等待 `RevTaskScheduler.NextFrame()` 的泵协程随 Scheduler 队列清理而不再恢复；若静态 `_pumping/_loading` 保留，新请求可能一直无法启动。
- **修复**：加载泵在 `SubsystemRegistration` 清理新旧等待/加载索引、取消旧令牌并将所有旧回调收口一次；泵循环携带代际编号，旧循环晚到时不能修改新会话 `_pumping` 状态；Job 完成和回调派发具备一次性保护。
- **文件**：`RevResourceSystem/Support/RevAsyncLoadPump.cs`

### 17. 自定义同步 Loader 抛异常会逃出资源门面

- **严重度：中**
- **修复**：同步策略 Loader 异常被转成 `BundleLoadFail` 并记日志，失败句柄仍进入资源缓存的统一引用释放路径，避免异常直接打断业务加载流程。
- **文件**：`RevResourceSystem/Facade/RevResManager.cs`

---

## 验证范围与未结项

- **已验证**：完整 Unity 解决方案 `dotnet build` 成功，0 个错误；目标运行时代码 lint 无诊断；`git diff --check` 通过。
- **Unity Editor / PlayMode**：本轮 MCP 请求无法连接，最后修改后没有完成 Unity Console 检查、EditMode / PlayMode 测试，也没有在 Editor 的关闭 Domain Reload 配置下复现加载泵重置。
- **自动化测试现状**：工程的 EditMode 测试运行器此前返回 0 个已注册用例，不能视为自动回归覆盖。
- **仍建议在恢复 Unity 后验证**：异步 `RevPool.GetAsync` 分别释放调用方句柄与销毁池后的引用计数；`DestroyGroup` 销毁活动对象后再 Shutdown；旧 prefab Rebind 后归还；关闭 Domain Reload 后旧加载回调和新泵；多个资源共享 AB 依赖时取消其一并核对依赖引用计数。
- **未解决但已识别**：AB 底层 `LoadFromFileAsync` / WebGL `UnityWebRequest` 没有完全受单个资源句柄取消控制，已开始的共享 AB 读取仍可能继续到引擎请求结束；在不破坏共享 bundle 加载的前提下中断需要额外的请求级引用 / 协作取消设计，本轮未冒险改写。

> 本轮按可确认的代码路径持续修复并做了多轮编译检查，但静态审计和编译都不能证明三个模块绝无其他 Bug；尤其真实 AB 包、网络请求、场景切换和关闭 Domain Reload 仍需 Unity 运行验证。
