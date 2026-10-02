# 模块速查（二）：音效 / 状态机 / 资源 / 单例 / UI / 动作序列 / GM / 配置表 / 热更

> 门面命名空间：除 `RevHotUpdate`（`Revolution.HotUpdate`）外**全部在 `namespace Revolution`**；路径相对 `Assets/Revolution/Runtime/`。
> UI / 资源 / 热更这三块展开在各自专题里（`ui-and-layering.md` / `resource-and-ab.md` / `hot-update.md`），本节只给"够用 + 关键坑"。

---

## 1. RevSound —— 音效

- 门面：`Revolution.RevSound`（static）· `RevSoundSystem/Facade/RevSound.cs`
- 职责：一行播放（2D / 3D / BGM）、统一音量、可选音效表、两个观察事件（成功/失败）。
- 常用 API：

```csharp
RevSoundHandle RevSound.Play(string name, float? volume = null, RevSoundKind? kind = null)                 // 2D（UI）
RevSoundHandle RevSound.PlayOn(string name, GameObject target, float? volume = null, bool? loop = null, RevSoundKind? kind = null)
RevSoundHandle RevSound.PlayAt(string name, Vector3 position, float? volume = null, RevSoundKind? kind = null)
RevSoundHandle RevSound.PlayBgm(string name, float fadeSeconds = 0f, float? volume = null)
void RevSound.StopBgm(float fadeSeconds = 0f)      void StopAll(float fadeSeconds = 0f)
bool RevSound.Register(string name, string path, RevSoundKind kind = RevSoundKind.Sfx, float volume = 1f, bool loop = false)
void RevSound.Preload(params string[] names) / PreloadBgm(params string[] names)
float RevSound.GetVolume(RevSoundKind kind) / SetVolume(RevSoundKind kind, float volume)
float RevSound.MasterVolume / bool Enabled / Mute / int MaxVoices / RevSoundScope OpenScope()
event Action<string, RevSoundErrorReason> Failed      event Action<RevSoundHandle> VoiceFinished
```

- 最小示例：

```csharp
RevSound.Play("ui_click");                                    // 2D
RevSound.PlayOn("skill_cast", enemy.gameObject);              // 3D：挂在物体上，物体没了自动收声
RevSound.PlayAt("explosion", hitPoint);
RevSound.PlayBgm("login", fadeSeconds: 1f);
using (RevSoundScope s = RevSound.OpenScope()) { s.Play("battle_bgm"); }    // 出块全停
```

- 铁律 / 坑：
  1. **音效名 = 相对音效根目录的路径**；逻辑名 ≠ 路径时先 `Register`。路径只写在资源层一处。
  2. **不提供任何查询**：播完没播出来只能靠 `VoiceFinished` / `Failed` 事件；**框架自身不打日志**（要日志自己接 `Failed`）。
  3. 3D 必须给"挂在哪"：`target` 为 null 会以 `NoTarget` 失败。
  4. 首次播放是异步的（先返回句柄、加载完自动播）；想零延迟先 `Preload`。最终音量 = 单次 × 分类 × 主音量。
- 文档：`Revolution.Document/音效系统/音效系统使用说明.md` · 模块 README：`Runtime/RevSoundSystem/README.md`

---

## 2. RevStateMachine —— 状态机（三件套，无 Facade 目录）

- 入口（`RevStateMachine/` 下，均 `public sealed class`）：
  - `RevLightStateMachine<T>`（`Lightweight/Machines/`）—— 轻量单状态机：UI 流程 / 玩法阶段，**零额外分配**
  - `RevLightStackStateMachine<T>` —— 带栈的状态机：界面栈、需要"记住来路"的流程
  - `RevHeavyFsm<TStateEnum, TOwner>`（`Heavyweight/`）—— 重量级：怪物 AI / Boss AI（枚举 + 宿主接口）
- 常用 API：

```csharp
// 轻量
void Register<TState>(TState state) where TState : T     // 启动期注册一次，实例被复用
void ChangeTo(T target)  void ChangeTo<TState>()          RevTask ChangeToAsync(T target)
void Update(float deltaTime)      void CancelAsyncTransition()
event Action<T, T> StateChanged      T CurrentState / float StateTime / bool Is<TState>()
// 栈
void Push(T state) / Push<TState>()      void Change(T state) / Change<TState>()
event Action<T> StatePushed / StatePopped      T Current / int Count
// 重量
RevHeavyFsm(TOwner owner)      void AddState(TStateEnum type, RevHeavyFsmState<TStateEnum, TOwner> state)
bool ChangeState(TStateEnum target, string reason = "")      RevTask ChangeStateAsync(...)
void UpdateState(float dt) / FixedUpdateState / LateUpdateState      void Reset(TStateEnum initialState, string reason = "Reset")
```

- 最小示例：

```csharp
var sm = new RevLightStateMachine<ILightState>();
sm.Register(new LobbyState());
sm.ChangeTo<LobbyState>();
sm.Update(Time.deltaTime);                 // 宿主每帧驱动
await sm.ChangeToAsync<BattleState>();     // 资源加载完才交割
```

- 铁律 / 坑：
  1. **防重入**：在 `OnEnter` / `OnExit` 里再切状态会抛异常（Release 也拦）。
  2. 用**类型**做键；未注册就按类型切 → 立刻抛。**注册复用实例**，否则每次 `new` 都被当成新状态、反复 Exit/Enter。
  3. 异步切换三条规则：只保留最新目标（旧 token 被 Cancel）、交割前旧状态仍激活、`await` 后要用 `CurrentState` / `IsTransitioning` 确认是否真切过去。
  4. 重量机**不做分层**，需要嵌套就组合多台；状态显式 `new`（无反射，IL2CPP/AOT 安全）。
- 文档：`Revolution.Document/状态机/状态机使用说明.md`、`状态机架构解析.md`、`状态机Demo示例讲解.md`

---

## 3. RevResManager —— 资源（唯一入口）

- 门面：`Revolution.RevResManager`（static）· `RevResourceSystem/Facade/RevResManager.cs`
  装配：`RevResBootstrap : RevSingleton<RevResBootstrap>` · `Support/RevResBootstrap.cs`
- 职责：两个参数（根目录 + 资源名）→ 算 key → 查缓存 → 责任链选策略 → 建句柄 → 加载 → 入缓存。
- 常用 API：

```csharp
T RevResManager.Load<T>(string rootPath, string resName, RevResGroup group = RevResGroup.Unknown) where T : UnityEngine.Object
RevResHandle RevResManager.Load(string rootPath, string resName, Type contentType, RevResGroup group = RevResGroup.Unknown)
RevResHandle RevResManager.LoadAsync<T>(string rootPath, string resName, Action<T> onFinished,
        RevResGroup group = RevResGroup.Unknown, RevResLoadPriority priority = RevResLoadPriority.Normal)
RevResHandle RevResManager.LoadAsync(string rootPath, string resName, Type contentType, Action<RevResHandle> onFinished, ...)
void RevResManager.Release(string rootPath, string resName)      void FlushUnused()      RevResScope OpenScope()
void RevResManager.UnloadGroup(RevResGroup group, bool force = false)      void UnloadAll()
RevResHandle RevResManager.Get(string rootPath, string resName)      int GetRefCount(...)      int CachedCount { get; }
RevResBootstrap.Instance.Init() / Shutdown(group, force = true) / ShutdownAll()
RevResBootstrap.UseABInEditor      RevResBootstrap.ResMapOverride
```

- 最小示例：

```csharp
Sprite icon = RevResManager.Load<Sprite>(RevResPath.Data, "Hero_1001", RevResGroup.UI);       // 编辑器直读/已缓存才同步拿得到
RevResManager.LoadAsync<Sprite>(RevResPath.Data, "Hero_1002", s => image.sprite = s, RevResGroup.UI);   // 真机首次必须异步
RevResManager.Release(RevResPath.Data, "Hero_1001");                                          // 引用 -1
RevResBootstrap.Instance.Shutdown(RevResGroup.Battle);                                        // 退出战斗整组卸
```

- 铁律 / 坑（详见 `resource-and-ab.md`）：
  1. **策略链顺序 = 注册顺序**：编辑器默认只注册 `RevEditorResPolicy`（直读，AB/Resources 完全不注册）；AB 模式 `RevABResPolicy`（除 `Res/` 前缀全接管）→ `RevResourcesResPolicy`（链尾兜底）。
  2. `ResMap.txt` 三列 = `逻辑名|包名|资源名`；**表缺失 → 空表 → 全部落 Resources 兜底**（编辑器里照样"能用"，真机才炸）。
  3. **引用计数 + 延迟释放**：归零进 `_unused`，由 `FlushUnused()`（切场景读条期）统一卸；失败句柄**也入缓存**（避免每帧重试坏资源）。
  4. **分组归属首次确定、永不覆盖**（`Unknown` 是唯一例外）；传 `Unknown` 的资源永远不会被分组卸载点名；跨域共享资源打 `Resident`。
  5. 失败原因只看 `handle.ErrorReason`（框架本方案不打日志）。
- 文档：`Revolution.Document/资源加载系统/资源加载系统使用说明.md`、`资源加载系统架构解析.md`

---

## 4. RevSingleton —— 单例（三个基类）

- 入口（`RevSingleton/`，namespace `Revolution`）：
  - `RevSingleton<T>`（纯 C# 懒加载，`RevSingleton.cs`）
  - `RevSingletonMono<T> : MonoBehaviour`（自己摆在场景里）
  - `RevSingletonAutoMono<T> : MonoBehaviour`（不摆也行，首次访问自动建隐藏宿主）
- 常用 API：

```csharp
T RevSingleton<T>.Instance { get; }              // Lazy + ExecutionAndPublication（双检锁语义）
T RevSingletonMono<T>.Instance { get; }          bool HasInstance { get; }
T RevSingletonAutoMono<T>.Instance { get; }      bool HasInstance { get; }
```

- 最小示例：

```csharp
public sealed class GameAudio : RevSingletonAutoMono<GameAudio> { public void PlayBgm(string name) { /* ... */ } }
GameAudio.Instance.PlayBgm("login");             // 不摆物体也能用
```

- 铁律 / 坑：
  1. `T` **必须有无参构造函数**：显式写了带参构造会让首次访问 `Instance` 抛异常（补 `private T() { }`）。
  2. `RevSingleton<T>` 是纯 C# 类，**不能用于 MonoBehaviour**（要挂物体请用另两个）。
  3. Mono 版 `Instance` 是直取（没摆就是 null，故提供 `HasInstance`）；AutoMono 版业务方法必须 `public`。
  4. **优先用服务定位器**：单例是"就一个、且要全局访问"的容器，不是依赖注入的替代品。
- 文档：无专章（相关：`Revolution.Document/公共Mono模块/公共Mono模块使用说明.md`、`服务定位器/服务定位器使用说明.md`）

---

## 5. RevUI —— UI（门面 + 面板基类 + 特性）

- 门面：`Revolution.RevUI`（static）· `RevUISystem/Facade/RevUI.cs`；基类 `RevUIPanel` / `RevUIPanel<TData>`、`RevUIPart`
- 声明与绑定：`[RevUIPanel(root, layer)]`（面板类上）、`[RevBind]` / `[RevBind("Top/Title")]`（字段上）、`[RevUIPart(root, name)]`（Part 类上）
- 常用 API：

```csharp
void RevUI.Open<T>(Action<T> onOpened) where T : RevUIPanel                                   // 最简单
void RevUI.Open<T, TData>(TData data, Action<T> onOpened = null) where T : RevUIPanel<TData>  // 带数据（推荐）
RevTask<T> RevUI.OpenAsync<T>()      RevTask<T> RevUI.OpenAsync<T, TData>(TData data)          // await 版
T RevUI.Open<T>() where T : RevUIPanel            // ★ 仅"已打开过/池中"可用；需要加载时返回 null 并报错
bool RevUI.Close<T>()      int RevUI.CloseAll(RevUILayer? layer = null)      bool RevUI.Back()
T RevUI.Get<T>()      bool RevUI.IsOpen<T>()      RevUIPanel RevUI.TopOf(RevUILayer layer)
void RevUI.Preload<T>(Action onLoaded = null)     int RevUI.CloseGroup(string group)
void RevUI.RegisterAutoEvent<T>(Action<RevUIEventDispatch, T> bind) where T : Component
// 面板内：SetData(object) / RevUIPanel<TData>.SetData(TData) / CloseSelf() / RequestClose() / RefreshView()
//        FindPart<T>() / RefreshParts()        Part：RevUIPart.Create<T>(host, parent, onCreated)
```

- 最小示例：

```csharp
[RevUIPanel(RevResPath.Data, RevUILayer.Normal, ExclusiveGroup = "BagTab")]
public sealed class BagPanel : RevUIPanel<BagData>
{
    [RevBind] private ScrollRect _list;

    protected override void OnBindView() { }                                   // 只装配
    protected override void OnRefreshView() { _list.gameObject.SetActive(Data.UseCellLayout); }   // 只落屏
}

RevUI.Open<BagPanel, BagData>(new BagData { UseCellLayout = true });
```

- 铁律 / 坑（详见 `ui-and-layering.md`）：
  1. **同步 `Open<T>()` 不加载任何资源**，只在"已打开过或池里有"时拿得到实例；真机首次必须 `OpenAsync` / `Open<T>(onOpened)`。
  2. 数据流**单向**：`SetData → OnDataChanged → RefreshView`；带数据面板 `OnRefreshView` 是 abstract（不许糊过去）；复用要连数据一起清。
  3. **面板池是 UI 专用池**：关闭优先进池（`KeepAlive`），池满 / `DestroyOnClose` 才销毁（上限 `RevUISetting.MaxCachedPanels`）；取出必须走 `OnReuse` 钩子。
  4. **Back 只关"参与返回栈（`InBackStack = true`）的最晚打开面板"**；飘字 / 常驻 HUD 要设 false。
  5. 框架**没有** `System` / `BusinessLogic` 层：跨界面逻辑放服务，单屏逻辑放面板 + `TData`。
- 文档：`Revolution.Document/UI系统/UI系统使用说明.md`、`UI系统架构解析.md`（第十章专门回答"要不要做 System / BusinessLogic"）

---

## 6. RevSequence —— 动作序列

- 门面：`Revolution.RevSequence`（static）+ `RevSequenceBuilder`（`RevActionSequence/Facade/`）；产物 `RevSequenceDefinition`，播放句柄 `RevSequenceHandle`
- 职责：先 `Build` 一份**不可变蓝图**并缓存，运行期 `Play` 零构建成本；被取消时必然执行收尾。
- 常用 API：

```csharp
RevSequenceBuilder RevSequence.Create(string name, RevSequenceConcurrency concurrency = RevSequenceConcurrency.Free)
// Builder
RevSequenceBuilder Do(string name, Action<RevSequenceContext> body)
RevSequenceBuilder Wait(float seconds) / WaitFrames(int frames)
RevSequenceBuilder WaitUntil(string name, Func<RevSequenceContext, bool> predicate, float timeoutSeconds = 0f)
RevSequenceBuilder Tween(string name, float duration, Action<RevSequenceContext, float> update)      // 另有泛型重载
RevSequenceBuilder Parallel(string name, Action<RevSequenceBuilder> build)      Repeat(string name, int times, Action<RevSequenceBuilder> build)
RevSequenceBuilder WaitTask(Func<RevSequenceContext, RevTask> taskFactory, string name = "等异步任务")
RevSequenceBuilder Finally(Action<RevSequenceBuilder> buildSteps)      OnCancel(Action<RevSequenceBuilder> buildSteps)
RevSequenceBuilder OnCompleted(Action<RevSequenceRun> cb) / OnCancelled(Action<RevSequenceRun> cb)
RevSequenceDefinition Build()
// 运行：definition.Play(...) → RevSequenceHandle（.Stop()）
```

- 最小示例：

```csharp
static readonly RevSequenceDefinition ChestOpen = RevSequence
    .Create("宝箱开箱")
    .WaitUntil("等玩家点击", ctx => ctx.Require<IInput>().Clicked, 15f)
    .Finally(f => f.Do("恢复镜头", ctx => ctx.Require<ICamera>().Restore()))
    .Build();

RevSequenceHandle handle = ChestOpen.Play(chestObject);
handle.Stop();
```

- 铁律 / 坑：
  1. **蓝图只 Build 一次并缓存**（静态字段），别在运行期反复构建。
  2. **状态别存在构建期字段里**（用业务服务 / Tween 起始值 / 步骤状态槽），否则两个玩家同时触发会互踩。
  3. 占用了什么（生成的物件、推近的镜头）就在 `.Finally` 里还回去；只在取消时要做的放 `.OnCancel`。
  4. 防连点用 `RevSequenceConcurrency.RejectPerSource`；出错看 Console（tag = ActionSequence，会写明哪条序列第几步）。
- 文档：`Revolution.Document/动作序列/动作序列使用说明.md`、`动作序列架构解析.md` · 模块 README：`Runtime/RevActionSequence/README.md`

---

## 7. RevGM —— GM 指令（纯 C#，不引用 UnityEngine）

- 门面：`Revolution.RevGM`（static）· `RevGMCommand/Facade/RevGM.cs`
- 职责：一行注册、统一执行（解析 → 查命令 → 预校验 → 执行兜异常 → 返回结果 + 耗时）、模糊联想。
- 常用 API：

```csharp
void RevGM.Register(string name, string description, Func<RevGMArgs, string> handler, params RevGMArg[] args)
void RevGM.Register(string name, string description, Func<RevGMArgs, string> handler, RevGMFlags flags, params RevGMArg[] args)  // RevGMFlags.HighRisk
bool RevGM.Unregister(string name)      void RevGM.Clear()      int RevGM.Count { get; }
RevGMResult RevGM.Execute(string commandLine)          // 失败/成功都带人话信息
IReadOnlyList<RevGMCommand> RevGM.Suggest(string input, int max = 8)
bool RevGM.TryGet(string name, out RevGMCommand command)      bool RevGM.Enabled { get; set; }
```

- 最小示例：

```csharp
RevGM.Register("经济/加金币", "给当前玩家加金币", args => AddGold(args.Int(0, 1000)), RevGMArg.Int("数量", 1000));
RevGM.Register("战斗/清空敌人", "把所有敌人血量清零", args => ClearEnemy(), RevGMFlags.HighRisk);   // 面板会二次确认
// 编辑器里打开面板执行：Revolution.Tools / GM 指令面板（Ctrl+Shift+G）
```

- 铁律 / 坑：
  1. **名字硬约束**：不能为空、**不能含任何空白字符**（空格是命令与参数的分隔符，分组用 `/`）、**不能只有分隔符**（`/`、`///`、`" / "` 归一化后会变成空名命令，且第二次注册只会报"已注册过"，把真正原因盖掉）。
  2. **注册只写在一处**（组合根 / 一个静态类）；命令体里不要再注册别的命令；参数用 `args.Int(0, 默认值)` 取，**不要**自己解析字符串。
  3. 业务拒绝执行时抛 `RevGMUsageException("为什么不行")` —— 这句话原样显示在面板上，别静默 return。
  4. 正式包没有"编译裁剪"：**没人在启动期调 Register 就等于不存在**（零开销）；临时关闭用 `Enabled = false`。
- 文档：`Revolution.Document/GM指令/GM指令使用说明.md` · 模块 README：`Runtime/RevGMCommand/README.md`

---

## 8. RevDataTableManager —— 配置表（导表工具配套）

- 门面：`Revolution.RevDataTableManager`（static）· `RevDataLoad/Manager/RevDataTableManager.cs`
  容器基类：`RevDataTable<TKey, TData> : RevIDataTable` · `RevDataLoad/Core/RevDataTable.cs`
- 职责：表也是"一种资源"，统一交给资源系统取（白拿缓存 / 引用计数 / 分组卸载 / 平台透明）。
- 常用 API：

```csharp
RevTask<T> RevDataTableManager.LoadAsync<T>(RevResGroup group = RevResGroup.Config, RevResLoadPriority priority = ...)
        where T : class, RevIDataTable, new();                                   // await；失败抛 RevDataTableLoadException
void RevDataTableManager.LoadAsync<T>(Action<T, RevResLoadErrorReason> onFinished, ...)   // 失败回调（不抛）
T RevDataTableManager.Load<T>(...)          // 仅"编辑器直读 / 已缓存"时才有结果
T RevDataTableManager.Get<T>()              // 没加载过返回 null（不 new、不抛）
RevIDataTable RevDataTableManager.Get(string tableName)      bool TryGet<T>(out T table)
bool IsLoaded<T>() / Unload<T>() / UnloadAll()
// 容器：TableName { get; }  Count { get; }  FindByKey(TKey key, out TData data)  LoadText(string text, out int errorCount)
```

- 最小示例：

```csharp
HeroTable tbl = await RevDataTableManager.LoadAsync<HeroTable>();
if (tbl.FindByKey(1001, out Hero cfg)) { /* 用 cfg */ }

if (RevDataTableManager.TryGet<BuffTable>("Buff", out BuffTable buffs)) { buffs.FindByKey("BUFF_ATK_UP", out Buff b); }
```

- 铁律 / 坑：
  1. **禁止直接读 StreamingAssets / `Application.dataPath`**：Android 在 APK 内、iOS 只读、WebGL 是 URL；绕过资源系统 = 没缓存、没引用计数、表永远卸不掉，而且热更无处落脚。
  2. 字符串驱动时用的是 **`TableName`（如 `"Buff"`）而不是容器类名（`"BuffTable"`）**；主键类型由导表工具生成（int → `RevDataTable<int,T>`，string → `RevDataTable<string,T>`），运行时不关心。
  3. **同类型并发请求共享同一份加载任务**（框架已处理）：否则两个并发调用各 new 一个容器 → 引用计数回不到零 → TextAsset 泄漏。
  4. 两种失败风格随你挑：`await` 抛异常（不静默）/ 回调 `(null, 原因)`；默认分组 `RevResGroup.Config`（切场景不会被误卸载）；同步 `Load<T>` 真机首次拿不到结果。
- 文档：`Revolution.Document/导表工具/导表工具使用说明.md`、`导表工具架构解析.md`、`导表工具对比与设计说明.md`

---

## 9. RevHotUpdate —— 资源热更（**扩展包**，独立程序集）

- 门面：`Revolution.HotUpdate.RevHotUpdate`（static）· `Assets/Revolution.HotUpdate/Runtime/Facade/RevHotUpdate.cs`
- 职责：启动时一次热更（拉清单 → 大版本校验 → 版本比对 → 差量下载 → 校验落地 → 切版本 → 重装资源策略）；返回后资源系统已是热更后状态。
- 常用 API：

```csharp
RevTask<RevHotUpdateResult> RevHotUpdate.InitializeAsync(RevHotConfig config, Action<RevHotProgress> onProgress = null, RevCancellationToken token = null)   // ★ 一行接法
RevTask<RevHotCheckResult>  RevHotUpdate.CheckAsync(RevHotConfig config, ...)          // 只检查、不下东西
RevTask<RevHotUpdateResult> RevHotUpdate.UpdateAsync(RevHotCheckResult check, ...)     // 必须把 CheckAsync 的结果原样传入
RevTask<RevHotUpdateResult> RevHotUpdate.WaitReadyAsync()                              // 只等不发起（从没跑过会返回 NotRun）
void RevHotUpdate.Install()      string RevHotUpdate.Dump()
event Action<RevHotProgress> RevHotUpdate.Progress
RevHotState RevHotUpdate.State / RevHotError LastError / string LocalResVersion
```

- 最小示例：

```csharp
// 放在"加载任何业务资源"之前；这一行就是整次热更
RevHotUpdateResult result = await RevHotUpdate.InitializeAsync(
    new RevHotConfig { RemoteRoot = "https://cdn.example.com/gameA" },
    p => progressBar.value = (float)(p.Percent / 100.0));

if (result.Success == false) { RevLog.Warn(result.Error.Message, "HotUpdate"); }
// 之后照旧：RevResManager.LoadAsync<Sprite>(RevResPath.Data, "Hero_1001", s => icon.sprite = s, RevResGroup.UI);
```

- 铁律 / 坑（详见 `hot-update.md`）：
  1. `InitializeAsync` 内部自动 `Install()` + 重装资源策略，**不必**再手动调 `RevResBootstrap.Init()`；重复调用直接等正在跑的那一轮。
  2. 框架侧只有**两个默认 `null` 的钩子**：`RevABLoader.BundlePathResolver`（包从哪读）+ `RevResBootstrap.ResMapOverride`（映射表用热更的）；不装热更包 = 零影响。
  3. **覆盖式语义**：持久化版本目录优先 → 没有就回退首包（StreamingAssets）；热更失败**绝不破坏现有版本**（玩家照常玩旧版）。
  4. **大版本锚定**：清单 `@appVersion` 必须 = `Application.version`，不一致 → `AppVersionMismatch`（设计如此，跨大版本请重新出包）。
  5. 编辑器直读模式下**看不到热更效果** → 必须 `RevResBootstrap.UseABInEditor = true`。
- 文档：`Revolution.Document/热更新/RevHotUpdate使用说明.md`、`RevHotUpdate架构解析.md`、`RevHotUpdate技术方案.md` · README：`Assets/Revolution.HotUpdate/README.md` · demo：`Assets/Revolution.Demo/RevHotUpdate.Demo/README.md`
