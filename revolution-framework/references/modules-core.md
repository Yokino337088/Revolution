# 模块速查（一）：日志 / 计时 / 公共Mono / 对象池 / 事件 / 服务定位器 / 异步 / 场景 / 输入

> 9 个模块的门面**全部在 `namespace Revolution`**；路径相对 `Assets/Revolution/Runtime/`。
> 这里只列"最常用 + 最容易写错"的部分；完整 API 看源码 Facade 与对应《使用说明》（`references/docs-map.md`）。
> **阅读顺序建议**：写代码前先看该节"铁律"，再照"最小示例"写。

---

## 1. RevLog —— 日志（唯一日志出口）

- 门面：`Revolution.RevLog`（static）· `RevLog/Facade/RevLog.cs`
- 职责：五级别日志 + tag 过滤 + 重复抑制 + 环形缓冲 + 可插拔通道（文件/自定义 sink）。
- 常用 API：

```csharp
RevLog.Debug(string message, string tag = null)      // ★ 编译期删除（正式包不执行，参数也不求值）
RevLog.Info / Warn / Error(string message, string tag = null)
RevLog.Exception(Exception e, string message = null, string tag = null)
RevLog.MinLevel { get; set; }        RevLog.IsEnabled(RevLogLevel level, string tag = null)
RevLog.MuteTag(string tag, bool muted = true)
RevLog.EnableFileLog(string directory, string prefix = "revlog")   // 返回 RevFileSink
RevLog.Flush()                       RevLog.Recent(int count = 50)      RevLog.Dump(int count = 200)
RevLog.AddSink(IRevLogSink sink)     RevLog.SinkErrors { get; }
```

- 最小示例：

```csharp
RevLog.Warn("配置缺失，用默认值", "Config");
if (RevLog.IsEnabled(RevLogLevel.Info, "Net")) { RevLog.Info($"{state}", "Net"); }   // 热循环先判再拼
RevLog.Exception(e, "战斗开始失败", "Battle");
```

- 铁律 / 坑：
  1. `Debug` 是**编译期删除**，正式包看不到 —— 想留就定义 `REVLOG_DEBUG`。
  2. 热路径别直接拼字符串：贵的不是过滤，是参数构造 → 先 `IsEnabled`。
  3. 文件通道是**异步**的：切场景 / 退后台 / 出包前 `Flush()`。
  4. 连续相同日志会被抑制成"首条 + 重复 N 次"，看着像丢日志；`SinkErrors > 0` 说明某个通道在坏。
  5. **框架各模块的日志都汇到这里**（`RevTimer.Log` / `RevEvent.Log` / `RevPool.Log` / `RevScene` 失败原因…），排查从这里开始。
- 文档：`Revolution.Document/日志系统/日志系统使用说明.md` · 模块 README：`Runtime/RevLog/README.md`

---

## 2. RevTimer —— 计时器

- 门面：`Revolution.RevTimer`（static）· `RevTimer/Facade/RevTimer.cs`
- 职责：到点做事 / 每 N 秒做事 / 到服务器时刻做事；四时间域（Scaled / Unscaled / Fixed / Server），零配置自动建隐藏宿主。
- 常用 API：

```csharp
RevTimerHandle After(float seconds, Action callback, RevTimeDomain domain = RevTimeDomain.Scaled, object owner = null)
RevTimerHandle NextFrame(Action callback, object owner = null)
RevTimerHandle Every(float interval, Action callback, int times = -1, RevTimeDomain domain = ..., object owner = null)
RevTimerHandle Every(float interval, Action<int> callback, int times = -1, ...)      // 带第几次
RevTimerHandle At(DateTime serverUtc, Action callback, object owner = null)          // 需先 SyncServerTime
int CancelAllOf(object owner)      int Clear()      RevTimerScope OpenScope()
void SyncServerTime(DateTime serverUtc, double realtimeSinceStartup = ...)           // 登录后同步一次
bool Paused { get; set; }          int Count { get; }      RevStopwatch StartStopwatch(...)
```

- 最小示例：

```csharp
RevTimer.After(2f, () => RevUI.Close<LoadingPanel>(), owner: this);
RevTimer.Every(1f, i => tip.text = $"{i}/10", times: 10, owner: this);
private void OnDestroy() { RevTimer.CancelAllOf(this); }        // 一行防泄漏
```

- 铁律 / 坑：
  1. **循环计时器必须留"能停"的路**：`owner:` 或 `using (RevTimer.OpenScope())`。"忘停"是唯一能拖死这个模块的错法。
  2. **时间域别选错**：UI 倒计时 / 超时 / 暂停菜单用 `Unscaled`（默认 `Scaled` 会被 `timeScale=0` 冻结）。
  3. `At(...)` 前必须 `SyncServerTime`，否则报 `NoServerTime`（框架不瞎猜）；`Paused = true` 是**整模块**开关。
  4. 回调在主线程 Tick 里**同步**执行：别干重活；`After(0)` 表示"下一帧"。
- 文档：`Revolution.Document/计时器系统/计时器系统使用说明.md` · 模块 README：`Runtime/RevTimer/README.md`

---

## 3. RevMono —— 公共 Mono（给纯 C# 类每帧回调 / 协程）

- 门面：`Revolution.RevMono`（static）· `RevPublicMono/Facade/RevMono.cs`
- 职责：**不摆物体**就能拿到 Update / LateUpdate / FixedUpdate 与协程能力。
- 常用 API：

```csharp
RevMono.AddUpdate(Action action, object owner = null)         bool RemoveUpdate(Action action)
RevMono.AddLateUpdate(Action action, object owner = null)     RevMono.AddFixedUpdate(Action action, object owner = null)
RevMono.Add(RevMonoPhase phase, Action action, object owner = null) / bool Remove(...)
int RemoveAllOf(object owner)      int Clear()      RevMonoScope OpenScope()
Coroutine StartCoroutine(IEnumerator routine)      void StopAllCoroutines()
int UpdateCount / LateUpdateCount / Count { get; }      bool IsRunning { get; }
```

- 最小示例：

```csharp
RevMono.AddLateUpdate(FollowCamera, owner: this);      // 跟随/相机用 LateUpdate
RevMono.AddFixedUpdate(TickPhysics, owner: this);      // 帧率无关用 FixedUpdate
private void OnDestroy() { RevMono.RemoveAllOf(this); }
```

- 铁律 / 坑：
  1. **选对相位**：跟相机 / UI 的跟随用 `LateUpdate`；物理、帧率无关用 `FixedUpdate`。
  2. 随对象销毁**一行清干净**：`owner: this` + `RemoveAllOf(this)`，或 `OpenScope()`。忘记移除 = 已销毁对象还在被回调。
  3. 回调跑在主线程 Update，**别做重活**（要异步用 `RevTask`）。
  4. 分工：`RevTimer` = 多久之后 / 每隔多久；`RevTask` = await 一帧 / 等资源；**本模块 = 每帧回调 / 协程**。
- 文档：`Revolution.Document/公共Mono模块/公共Mono模块使用说明.md`

---

## 4. RevPool / RevRefPool —— 对象池（两个门面）

- 门面：`Revolution.RevPool`（Unity 对象，`RevObjectPool/Facade/RevPool.cs`）、`Revolution.RevRefPool`（纯 C# 对象，`Facade/RevRefPool.cs`）
- 职责：一行取出、一行归还；Unity 对象池基于资源系统（端着 `RevResHandle`），C# 对象池靠 `IRevPoolable.OnPoolReturn` 复位。
- 常用 API：

```csharp
GameObject go = RevPool.Get("Battle/Bullet/Blue", firePoint);     // (rootPath, resName, parent, group)
T comp = RevPool.Get<T>(rootPath, resName, parent, group);        // 取组件
RevResHandle h = RevPool.GetAsync(rootPath, resName, onFinished: go => {...});   // WebGL 必须用这个
bool RevPool.Return(GameObject item, int delayFrames = -1)        // delay=等 N 帧再回收（特效播完）
int RevPool.Clear(rootPath, resName) / ClearAll() / ClearGroup(group)
RevPoolStats RevPool.GetStats(rootPath, resName)                  string RevPool.DumpStats()

T msg = RevRefPool.Get<ChatMessage>();                            // where T : class, IRevPoolable, new()
RevRefPool.Return(msg);      RevRefPool.Clear<T>() / ClearAll()      RevRefPool.GetStats<T>()
```

- 最小示例：

```csharp
var bullet = RevPool.Get("Battle/Bullet/Blue", firePoint);
RevPool.Return(bullet, delayFrames: 5);

var msg = RevRefPool.Get<ChatMessage>();
msg.Init(sender, text);
RevRefPool.Return(msg);        // OnPoolReturn 里必须把字段清干净
```

- 铁律 / 坑：
  1. **`OnPoolReturn` 必须把字段清干净**：池对象既不销毁也不重置，"上次的数据还在"是池化最常见事故。
  2. **取用成对、类型/variant 一致**：找不到池多半是 variant 不一致（两个池）。
  3. **WebGL（含小游戏）同步加载不可用 → 必须 `GetAsync`**。
  4. 池内部按**引用相等**匹配（业务别重写 `Equals`）；只允许主线程。
- 文档：`Revolution.Document/对象池/对象池使用说明.md`、`对象池架构解析.md`

---

## 5. RevEvent —— 事件（字符串事件名）

- 门面：`Revolution.RevEvent`（static）· `RevEventSystem/Facade/RevEvent.cs`
- 职责：一对多解耦；0~4 参数泛型派发；派发中可安全增删（标记删除）。
- 常用 API：

```csharp
RevEvent.AddEventListener(string name, Action handler, int priority = 0, object owner = null)
RevEvent.AddEventListener<T1>(string name, Action<T1> handler, int priority = 0, object owner = null)   // T1~T4
int RevEvent.DispatchEvent(string name)      int DispatchEvent<T1>(string name, T1 arg1)               // 返回"实际被调用的订阅数"
bool RevEvent.RemoveEventListener(string name, Action handler) / RemoveEventListener<T1>(...)
int RevEvent.RemoveAllByOwner(object owner)      void Clear(string name) / ClearAll() / ResetAll()
int RevEvent.GetListenerCount(string name)       bool HasListener(string name)      List<string> GetAllEventNames()
bool RevEvent.RethrowOnException / LogNoListener { get; set; }      Action<string> Log / OnException
```

- 最小示例：

```csharp
public static class GameEventId { public const string HeroSkinAdd = "Hero.Skin.Add"; }
RevEvent.AddEventListener<HeroSkinAddArgs>(GameEventId.HeroSkinAdd, OnSkinAdd, owner: this);
int n = RevEvent.DispatchEvent(GameEventId.HeroSkinAdd, new HeroSkinAddArgs(heroId, skinId));
if (n == 0) { RevLog.Warn("没人监听 Hero.Skin.Add", "Event"); }
private void OnDestroy() { RevEvent.RemoveAllByOwner(this); }
```

- 铁律 / 坑：
  1. **同一事件名只能有一种签名**：注册成别的类型派发会明确报"签名不匹配"（不静默丢弃）。
  2. **匿名 lambda 注册后按委托 Remove 匹配不上** → 存成字段，或用 `owner` 批量移除。
  3. 派发中反注册是安全的（只标记，最外层结束才收尾），但**回调里别做重活**。
  4. 只在主线程注册/派发；跨线程先切回主线程。
  5. 返回值有用：`DispatchEvent` 返回 `0` = **没人听**（排查"为什么没反应"的第一现场）。
- 文档：`Revolution.Document/事件系统/事件系统使用说明.md`、`事件系统架构解析.md`

---

## 6. RevServiceLocator —— 服务定位器（依赖显式化）

- 门面：`Revolution.RevServiceLocator`（**实例**，不是 static）+ `RevServiceBuilder` · `RevServiceLocator/Facade/`
- 职责：把"底层框架调上层能力"从写死依赖/全局静态变成**显式注册 + 显式获取**（零静态可变状态、零反射构造）。
- 常用 API：

```csharp
RevServiceLocator locator = RevServiceLocator.Create(int expectedServiceCount = 16)
    .AddSingleton<TService, TImpl>()                                  // 容器负责创建与释放
    .AddSingleton<TService>(TService instance)                        // ★ 你创建的实例，容器不释放
    .AddSingleton<TService>(Func<RevIServiceLocator, TService> factory)
    .AddScoped<TService, TImpl>()                                     // 每局/每作用域一份
    .Build();                                                         // 装配结束，之后不可改

T GetRequired<T>() where T : class      T Get<T>()      bool TryGet<T>(out T service)
RevServiceLocator CreateScope()         void Tick(float deltaTime)      void Dispose()
```

- 最小示例：

```csharp
static readonly RevServiceLocator Services = RevServiceLocator.Create()
    .AddSingleton<ISoundService, SoundService>()
    .AddScoped<ICombatContext, CombatContext>()
    .Build();

var sound = Services.GetRequired<ISoundService>();                 // 必需依赖：没注册就炸（启动期暴露）
using (RevServiceLocator battle = Services.CreateScope())          // Scoped 必须放进 using
{
    battle.Tick(Time.deltaTime);
}
```

- 铁律 / 坑：
  1. **装配只写在一处**（组合根），`Build()` 之后任何地方都改不了 —— 不要在业务代码里"随手注册服务"。
  2. `GetRequired` 用于**必需**依赖；`Get` / `TryGet` 只用于可选能力。
  3. **谁创建谁释放**：容器释放"工厂创建的服务"；`AddSingleton(instance)` 传进去的由你释放。
  4. 根容器上取 `Scoped` 服务会**直接报错**，必须放进 `CreateScope()` 的 `using`。
  5. 分工：需要"每帧驱动 + 生命周期 + 逆序释放"的业务能力用服务；一次性工具类用静态方法即可。
- 文档：`Revolution.Document/服务定位器/服务定位器使用说明.md` · 模块 README：`Runtime/RevServiceLocator/README.md`

---

## 7. RevTask —— 轻量异步（框架唯一异步基建）

- 入口：`Revolution.RevTask` / `RevTask<T>`（readonly struct，带 `[AsyncMethodBuilder]`）· `RevTask/RevTask.cs`
  调度器：`RevTaskScheduler`（自动创建的 DontDestroyOnLoad 宿主）· 取消：`RevCancellationTokenSource` / `RevCancellationToken`
- 职责：**可 await、可传播异常、可取消**的轻量任务；替代协程 / UniTask；资源与场景加载都返回它。
- 常用 API：

```csharp
static RevTask Completed { get; }      static RevTask Delay(int milliseconds)      static RevTask Yield()
static RevTask WhenAll(params RevTask[] tasks)
static RevTask FromException(Exception e)      static RevTask<T> FromResult(T value)
static RevTaskCompletionSource CreateSource()  static RevTaskCompletionSource<T> CreateSource()
void Forget()                                  // 不 await 时必须显式 Forget（否则编译器警告）
// RevTaskScheduler：MaxContinuationsPerFrame（默认 128）/ Post / Yield / Delay / WhenAll
// RevCancellationToken：IsCancelled / Register(Action) / ThrowIfCancelled()
// RevTaskUnityExtensions：AsyncOperation.ToRevTask() / await someAsyncOperation
```

- 最小示例：

```csharp
private async void OnClickEnterBattle()
{
    try
    {
        await RevTask.Delay(100);
        token.ThrowIfCancelled();                       // 关键节点检查取消
        await RevScene.LoadAsync("Battle", p => bar.value = p);
    }
    catch (Exception e) { RevLog.Exception(e, "进入战斗失败", "Battle"); }
}
```

- 铁律 / 坑：
  1. **续体每帧限量执行**（`MaxContinuationsPerFrame`，默认 128）：一次唤醒几千个任务不会卡帧，但也别指望"立刻全跑完"。
  2. 不要在 awaiter 结构体里捕获状态（结构体拷贝 → 状态不共享）；需要共享状态用 `RevTaskCompletionSource`。
  3. 取消是"主线程轻量标记 + 回调"，不是 `CancellationTokenSource` 那套线程语义。
  4. 异步里只做"等待与编排"，重活分帧；`await` 之后的代码回到主线程。
  5. 事件/计时器/资源回调里不要 `await` 出长流程（先切场景/关面板会一脚踩空）—— 用 `owner` + 取消。
- 文档：**暂无专章**（写法见 `RevTask.Demo` 场景与源码注释；`RevScene` / `RevResManager` 的 async API 都以它为返回类型）

---

## 8. RevScene —— 场景

- 门面：`Revolution.RevScene`（static）· `RevScene/Facade/RevScene.cs`
- 职责：一行切场景，进度可接，切之前按约定清理（默认自动清对象池）。
- 常用 API：

```csharp
RevTask RevScene.LoadAsync(string sceneName, Action<float> onProgress = null, float minSeconds = -1f)
RevTask RevScene.LoadAsync(int buildIndex, Action<float> onProgress = null, float minSeconds = -1f)
void RevScene.Load(string sceneName)                 // 同步：会卡帧，只在"必须立刻切"时用
RevTask RevScene.ReloadAsync(Action<float> onProgress = null, float minSeconds = -1f)
string CurrentName / int CurrentIndex / bool IsLoading / float Progress / RevSceneLoadState State
Action<string> OnLoadStart / Action<float> OnProgress / Action<string> OnLoaded / Action<string> OnLoadFailed
bool AutoClearPool = true      float DefaultMinSeconds = 0f      bool VerboseLog
```

- 最小示例：

```csharp
RevScene.OnLoadStart  += name => loadingPanel.Open(name);
RevScene.OnLoaded     += name => loadingPanel.Close();
RevScene.OnLoadFailed += why  => RevLog.Error(why, "Scene");
await RevScene.LoadAsync("Battle", p => bar.value = p);           // 推荐：异步 + 进度
```

- 铁律 / 坑：
  1. 场景必须已加进 **Build Settings → Scenes In Build**，否则报错。
  2. `RevScene.Load` 同步切**会卡帧**；大场景一律 `LoadAsync` 且不要忘 `await`。
  3. `AutoClearPool = true` 已自动清对象池；你自己的 **UI / 音效 / 资源分组** 要在 `OnLoadStart` 里按约定收（`RevUI.CloseAll(...)` / `RevSound.StopAll()` / `RevResManager.UnloadGroup(...)`）。
  4. 发起 / 推进 / 收尾整体兜异常，否则模块会永久死锁（框架已修过这个 bug，业务侧别在回调里再抛）。
- 文档：`Revolution.Document/场景系统/场景系统使用说明.md`、`场景系统架构解析.md`

---

## 9. RevInput —— 输入

- 门面：`Revolution.RevInput`（static）· `RevInput/Facade/RevInput.cs`
- 职责：一行绑定、一行查询（键盘/鼠标/触屏/手柄/手势）；业务不写 `Input.GetKeyDown`。
- 常用 API：

```csharp
bool RevInput.Bind(string action, params RevKey[] keys)          bool Bind(string action, RevMouseButton button)
bool RevInput.BindAxis(string action, RevKey negative, RevKey positive)      bool BindNamedAxis(...)
void RevInput.SetDeadzone(string action, float deadzone)         void SetRepeat(string action, float delay, float interval)
bool RevInput.Pressed(string action) / Held / Released / Repeat(string action)
bool RevInput.PressedBuffered(string action, float windowSeconds = ...)      // 缓冲（连招/容错）
float RevInput.Axis(string action)      RevInputVector2 Vector(string action)
bool RevInput.Tap(out RevGestureEvent e) / Swipe(out ...) / HasGesture(RevGestureKind) / float PinchScale
RevInputBlock RevInput.Block(RevInputBlockKind kind = RevInputBlockKind.World, ...)      RevInputScope OpenScope()
void RevInput.ResetAll(string reason = null)
bool RevInput.IsPointerOverUI(int pointerId = ...)               string SaveBindings() / bool LoadBindings(string) / string FindConflicts()
```

- 最小示例：

```csharp
RevInput.Bind("Jump", RevKey.Space, RevKey.JoystickButton0);
RevInput.BindAxis("MoveX", RevKey.A, RevKey.D);
if (RevInput.Pressed("Jump")) { Jump(); }
float move = RevInput.Axis("MoveX");
RevInput.ResetAll("换场景");                                     // 宿主已处理切后台，换场景再补一次
```

- 铁律 / 坑：
  1. **先绑后用**：没绑过的动作**不报错**，永远返回 false（"按键没反应"的第一嫌疑）。
  2. 切后台 / 失焦宿主已自动复位，换场景再调 `RevInput.ResetAll("换场景")`。
  3. **弹窗用屏蔽、不要停模块**：`RevInput.OpenScope()` + `scope.Block()`；停模块会把 ESC / 返回键一起停掉。
  4. 不要用本模块点 UI（UI 走 UI 系统），用 `RevInput.IsPointerOverUI()` 分流。
- 文档：`Revolution.Document/输入系统/输入系统使用说明.md`、`输入系统架构解析.md` · 模块 README：`Runtime/RevInput/README.md`
