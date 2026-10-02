# 高频踩坑集（现象 → 原因 → 正确做法）

> 全部来自框架的代码注释 / 文档 / 真实修过的 bug。写代码前扫一遍，能省掉大半返工。

## 1. "编辑器里一切正常，真机大面积失败"

- **现象**：资源/配置表在编辑器读得到，出包后 `FileNotExist`。
- **原因**：编辑器默认走 `RevEditorResPolicy`（AssetDatabase 直读），**AB / Resources 根本不参与**；资源没打 AB 标记（漏标）或 ResMap 没重新生成，编辑器里照样"能用"。
- **做法**：新增资源 → 放进 resRoot（默认 `Assets/GameRes`）→ 设 `assetBundleName` → 跑打包 / "仅生成映射"；打包前看「检查」页签的漏标报告；联调时把 `RevResBootstrap.UseABInEditor = true` 走一遍真链路。

## 2. "热更成功了，但内容还是旧的"

- **原因**：编辑器还开着直读模式；或者资源有缓存 + 引用计数没释放。
- **做法**：`RevResBootstrap.UseABInEditor = true`（菜单 `Revolution.Tools/资源/AB 加载模式（编辑器）`）；加载前先 `RevResManager.Release(根, 名)` 再 `LoadAsync`；必要时 `RevResManager.UnloadAll()` 模拟重启。

## 3. 事件/计时器/每帧回调泄漏（对象已销毁还在被调）

- **原因**：匿名 lambda 注册后**无法按委托移除**；循环计时器没留"能停"的路。
- **做法**：能传 `owner: this` 就传，销毁时一行 `RevEvent.RemoveAllByOwner(this)` / `RevTimer.CancelAllOf(this)` / `RevMono.RemoveAllOf(this)`；或用 `OpenScope()`；需要按委托移除时把回调**存成字段**。

## 4. 事件"没人听"或"签名不匹配"

- **原因**：事件名拼错（框架不会替你检查）；或同一个事件名被注册成了两种签名。
- **做法**：事件名常量集中一个类；`DispatchEvent` 的返回值 **0 = 没人听**，直接 `RevLog.Warn` 出来（`RevEvent.LogNoListener = true` 也能自动提示）；**同一事件名只允许一种签名**。

## 5. 关闭 Domain Reload（Enter Play Mode Options）导致的跨 Play 残留

- **现象**：第二次 Play 时命令重复注册报错 / 事件监听翻倍 / 热更状态错乱。
- **原因**：静态字段不清空。
- **做法**：框架各模块都带了 `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` 复位钩子（RevEvent / RevGM / RevHotUpdate…）；**你自己写的静态容器也要照做**（`Assets/Revolution/Runtime/*/Support/*UnityHooks.cs` 是范例）。

## 6. 资源引用计数不归零 → 永远不卸

- **原因**：`Load` 了没 `Release`；或者把资源传成 `RevResGroup.Unknown`（**永远不会被分组卸载点名**）。
- **做法**：成对释放；按域分组建模（`Battle` / `UI` / `Config` / `Sound`）；退出玩法用 `RevResBootstrap.Instance.Shutdown(RevResGroup.Battle)`；**慎用** `force: true`（会把 `RefCount > 0` 的一起清账，误伤别的域共享资源）。

## 7. 同步 `Load` 拿不到资源 / `RevUI.Open<T>()` 返回 null

- **原因**：真机首次是异步的（AB 在包外/包内需要加载）；同步 API 只在"已缓存 / 编辑器直读"时有效。
- **做法**：真机首次一律 `LoadAsync` / `RevUI.OpenAsync`；想秒开先 `Preload`。

## 8. `RevLog.Debug` 在正式包里"丢了日志"

- **原因**：`Debug` 是**编译期删除**（连参数表达式都不求值）。
- **做法**：要留就定义 `REVLOG_DEBUG`；热循环里先 `RevLog.IsEnabled(...)` 再拼字符串。

## 9. 时间域选错 → 暂停游戏后倒计时不动（或该冻结的没冻结）

- **原因**：默认 `RevTimeDomain.Scaled` 受 `timeScale` 影响。
- **做法**：UI 倒计时 / 超时 / 暂停菜单用 `Unscaled`；`RevTimer.At(...)` 前必须先 `SyncServerTime`；`RevTimer.Paused` 是**整模块**开关。

## 10. 对象池"上次的数据还在"

- **原因**：池对象既不销毁也不重置；`OnPoolReturn` 没清字段。
- **做法**：`IRevPoolable.OnPoolReturn` 里把字段清干净；`Return` 后不要再访问该对象（可能已被复用）；WebGL 一律 `RevPool.GetAsync`。

## 11. ResMap 读到空表 / 改了路径读不到表

- **原因**：表是**引导链起点**，运行时路径（`"ResourceSystem/ResMap"`）与编辑器路径（`ABBuildSetting.MapAssetPath`）是**两处平行硬编码**，改一个忘一个就落到"空表 → 全部 Resources 兜底"（编辑器里看不出问题）。
- **做法**：只改一处时同步另一处；表缺失时框架**不崩**但语义变了，排查时先确认表在不在。

## 12. 文件夹 `.meta` 位置写错

- **现象**：Unity 里文件夹没有 meta、或每个人生成一套不同 GUID 导致仓库天天冲突。
- **做法**：**必须是同级 `<文件夹名>.meta`**（写在文件夹内部的是废文件，Unity 不认）。

## 13. 热更包被实现成"只从持久化读" → 新装机必崩

- **原因**：新装机 / 清数据时 `{persistentDataPath}` 是空的，所有"热更包"全部 `BundleLoadFail`。
- **做法**：坚持**覆盖式语义**（持久化优先 → 回退 StreamingAssets 首包）；把"标记"理解成"允许被覆盖"而不是"必须从某处读"。

## 14. 替换/更新包之后"没生效"或替换失败

- **原因**：Windows 上文件被 `LoadFromFile` 映射后会**锁文件**；即使替换成功，**已加载的包仍在内存里**，本次进程不会生效。
- **做法**：`RevResBootstrap.Instance.ShutdownAll()` → 换文件 → `Init()`；开发期手工验证就"搬完包重进一次 Play"。**绝不在包被使用时覆盖文件**。

## 15. GM 命令注册报"已经注册过了"（但你没注册过同名命令）

- **原因**：名字归一化（`Trim().Trim('/')`）后变成空名（`/`、`///`、`" / "`），第一次注册出一个空名命令，第二次才报重名 —— 真正原因（名字不合法）被盖掉。
- **做法**：命令名不能为空、**不能含空白字符**、**不能只有分隔符**；分组用 `/`（如 `经济/加金币`）。

## 16. 服务定位器相关三连坑

- 组装完成后还想去注册服务 → `Build()` 之后**不可改**（装配只写在一处）；
- 在根容器上取 `AddScoped` 的服务 → **直接报错**（必须放进 `CreateScope()` 的 `using`）；
- `AddSingleton(instance)` 的服务没人释放 → **容器不管你自己创建的实例**（"谁创建谁释放"）。

## 17. 异步任务"没跑完就切场景了"

- **原因**：`RevTask` 的续体每帧**限量执行**（默认 128）；长流程里切场景/关面板会让后续步骤踩空。
- **做法**：长流程给"取消源"（`RevCancellationTokenSource`），关键节点 `ThrowIfCancelled()`；宿主销毁时取消；不要在不 await 的任务里捕获已销毁对象。

## 18. UI 面板复用后状态错乱

- **原因**：面板池取出的实例带着上一次的状态；没走 `OnReuse` 钩子；数据没清。
- **做法**：重置逻辑写在 `OnReuse`；关闭前清数据（框架会清 `Data`，但**你自己缓存的字段要清**）；弹窗/常驻 HUD 正确设置 `InBackStack` 与 `RevUICacheMode`。

## 19. 编辑器里平台/目录对不上（热更 404、AB 读不到）

- **原因**：编辑器里 `RevHotPlatform.Name` **恒为 `PC`**（带 `!UNITY_EDITOR`），而打包产物目录按 `BuildTarget` 命名（Android / iOS / WebGL）。
- **做法**：编辑器调试时把 Build Target 切回 Standalone；或在配置里用 `PlatformOverride` 对齐；demo 工具会打印警告。

## 20. 改完代码"文档没跟上"

- **现象**：文档与代码不一致 → 下一个人（或 AI）照着文档写出错的代码。
- **做法**：改行为 → 同步该模块《使用说明》；改设计/取舍 → 同步《架构解析》；**md 与 html 都要改**（在线站由 html 发布）；README 里的统计数字（模块数 / 行数）顺手核一遍。
