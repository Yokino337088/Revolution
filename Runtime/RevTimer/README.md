# 计时器（RevTimer）—— 3 分钟上手

> **一句话**：`RevTimer.After(2f, () => 关门())` —— 到点做事、每 N 秒做事、到某个服务器时刻做事。
> 10 个 `.cs` / 1419 行（注释 443 + 净代码 757）。**你要读的只有 1 个文件**：`Facade\RevTimer.cs`。
> 内核是纯 C#（10 个文件里只有 2 个碰 UnityEngine），**55 条行为断言在工程外用假时钟跑通**。

## 一、三行跑起来

```csharp
RevTimer.After(2f, () => RevUI.Close("Loading"));            // 2 秒后一次（最常用）
RevTimer.Every(1f, RefreshHp);                               // 每 1 秒一次（无限，记得停）
revTimerHandle = RevTimer.Every(1f, i => tip.text = $"{i}/10", times: 10);   // 10 次后自动结束
```

**零配置**：不摆场景物体、不挂脚本、不写 `Update` —— 第一次用到计时器时会自动创建隐藏宿主每帧推进。

## 二、全部 API（就这些）

| 你要做的事 | 一行 |
|---|---|
| N 秒后一次 | `RevTimer.After(2f, cb)` |
| 下一帧一次 | `RevTimer.NextFrame(cb)` |
| 每 N 秒一次（`times: -1` 无限） | `RevTimer.Every(1f, cb, times: 5)` |
| 每 N 秒一次 + 知道第几次 | `RevTimer.Every(1f, i => { }, times: 10)` |
| 到服务器某个时刻 | `RevTimer.SyncServerTime(服务器UTC)` → `RevTimer.At(activityEndUtc, cb)` |
| 秒表（游戏内耗时 / 真实耗时） | `var sw = RevTimer.StartStopwatch(); sw.Elapsed` |
| 停止 / 暂停 / 恢复 / 重置 | `h.Stop()` / `h.Pause()` / `h.Resume()` / `h.Restart()` |
| 读剩余时间 / 进度（做倒计时 UI） | `h.Left`（秒） / `h.Progress`（0~1） |
| 随对象销毁清干净（一行防泄漏） | `RevTimer.CancelAllOf(this)` 或 `using (var s = RevTimer.OpenScope()) { s.Every(...); }` |
| 全局暂停（打开设置面板/加载中） | `RevTimer.Paused = true` |
| 请求间隔 / 超时（不受 timeScale 影响） | 传 `RevTimeDomain.Unscaled` |

## 三、目录（你只需要读第一个）

| 文件 | 行数 | 说明 |
|---|---|---|
| `Facade\RevTimer.cs` | 215 | ★ **唯一入口**：4 个创建入口 + 清理 + 暂停 + 读数 + 事件 |
| `Core\RevTimerDefines.cs` | 83 | **四时间域**语义表 + 失败原因码 + 容量上限（纯 C#） |
| `Core\RevTimerHandle.cs` | 81 | 句柄：过期即安全空操作；`Left` / `Progress`（纯 C#） |
| `Core\RevTimerTable.cs` | 196 | 槽位表：代际号 / 延迟复用 / 校验（纯 C#，可工程外单测） |
| `Core\RevServerClock.cs` | 81 | 服务器时间：一次校准 + 本地外推（纯 C#） |
| `Core\RevStopwatch.cs` | 78 | 秒表（纯 C#） |
| `Implementation\RevTimerCore.cs` | 490 | 内核：四域推进 → 到期收集 → 执行 → 回收（**不用读**） |
| `Support\RevTimerDriver.cs` | 73 | 隐藏宿主：Update 推三域 + FixedUpdate 推 Fixed（**不用读**） |
| `Support\RevTimerUnityHooks.cs` | 53 | 出口接管 + 进 Play 复位（**不用读**） |
| `Support\RevTimerScope.cs` | 69 | `using` 一块，退出全停 |

## 四、三条铁律

1. **循环计时器必须留一条"能停"的路**：给 `owner`（`owner: this`）或用 `using (RevTimer.OpenScope())`，
   随对象销毁时一行 `CancelAllOf`。旧的计时器系统里，"忘停"是唯一能把整个模块拖死的错法。
2. **选对时间域**（第二常见的线上事故源，见第五节）：UI 倒计时用 `Unscaled`，表现演出用 `Scaled`，
   战斗推进用 `Fixed`，活动/榜单用 `Server`。默认是 `Scaled` —— 它**受 `timeScale` 影响**。
3. **回调里只做轻活**：回调是在主线程的 Tick 里同步执行的，干重活会吃掉这一帧（要干活请丢给异步设施）。

## 五、四时间域怎么选（本模块最需要学的一件事）

| 域 | 时间源 | 用在哪 | 切后台/卡顿 | timeScale=0 |
|---|---|---|---|---|
| `Scaled`（默认） | `deltaTime` | 表现、动画、战斗演出 | 不走 | **冻结** |
| `Unscaled` | `unscaledDeltaTime` | UI 倒计时、请求超时、轮询、音频收尾 | 不走 | 照走 |
| `Fixed` | `fixedDeltaTime` | 帧率无关的战斗推进（对齐参考实现 FrameSync） | 走 | 照走 |
| `Server` | 服务器 UTC 绝对时刻 | 活动结束、榜单刷新、防改设备时间 | **无关** ★ | 照走 |

> ★ `Server` 是**时刻式**（"到某个绝对时刻"），不是"从此刻起 X 秒" —— 所以它天然免疫卡顿、掉帧、
> 切后台。参考实现那份"恢复帧一次性补 60 万毫秒"的补偿尖峰问题，在这里直接用选域消化掉了，不需要补偿代码。
> 用之前必须先 `RevTimer.SyncServerTime(服务器UTC)`；没校准就用会报 `NoServerTime`（不瞎猜）。

## 六、与参考实现计时器的对照

### 6.1 抄来的（精华）

| 参考实现哲学 | 本框架怎么落 |
|---|---|
| 三形态各司其职（闹钟/秒表/时间轮） | 保留闹钟 + **秒表**（`RevStopwatch`，仿 .NET `Stopwatch`）；**时间轮不做** —— 它的适用量级是"Lua 侧上千个"，主线程几百个用线性扫描更快也更可预测（参考实现自己的结论：量级不需要的地方用复杂算法是负资产） |
| 四时间轴显式分离（Normal/FrameSync/Accurate/Server） | 合成 **四时间域**（`Scaled`/`Unscaled`/`Fixed`/`Server`），语义表见第五节 |
| 双到期模型（累积式 + 时刻式） | `After/Every` 累积式；`At` 时刻式（比绝对 UTC） |
| Accurate 通道的小数累积器（治毫秒截断漂移） | 全程 **double 秒累积**，一步到位 —— 不需要单列通道（参考实现那条通道的存在理由是它的 Normal 用 `(int)` 毫秒截断） |
| 池化纪律："复用前彻底归零 + 解绑委托" | `RevTimerEntry.ResetAll()` 清时间**并解绑全部委托**（回调 + owner）—— 参考实现把这条单列一条哲学，因为当年"给 OnRecycle 优化掉解绑"出过内存泄漏+幽灵回调 |
| 异常隔离到单个回调 | 每个回调独立 try/catch → `Failed(CallbackThrew)` + `OnException` 出口；**不自动终止该计时器**（与参考实现契约一致：循环计时器出错后下轮继续，但每次都上报） |
| 先收集后执行（LuaTimer 的重入安全） | 到期先收集进清单，清单收集完再派发 → 回调里 Stop/新建都安全 |
| 简单结构优先（主线程线性遍历，无堆/时间轮） | 固定 1024 槽位 + 线性扫描；实测 **1000 个计时器每帧 0.0139ms**（参考实现抽离方案的参考值是 <0.05ms） |
| 服务器时间：锚点 + 本地 realtime 外推 | `RevServerClock`；允许反复校准（抑制漂移）；误差来源写进了注释（网络单向延迟不在内） |

### 6.2 丢掉的（糟粕）

| 参考实现的问题 | 本框架的处理 |
|---|---|
| 句柄是裸 `int`：过期句柄静默空操作、还会误停被复用的新计时器 | 句柄 = 槽位 + **代际号** → 过期句柄是所有操作的安全空操作；槽位复用后老句柄打不中新计时器（**55 条断言里专门钉死这条**） |
| `RemoveTimerSafely(ref seq)` 这种"必须记得传 ref 清零"的纪律 | 根本不需要：句柄是值类型且自带失效校验 |
| `RemoveAllTimersByTarget` 靠反射委托的 Target，闭包/静态方法会漏 | `owner` 是**显式参数** + `ReferenceEquals` 匹配；再加 `RevTimerScope` 作用域，兜底不需要反射 |
| Normal 通道 `(int)` 截断误差被"误差契约"锁死不敢修 | 没有存量包袱：double 累积，10 分钟跑 600 次**恰好 600 次**（断言过） |
| 循环计时器余量不结转 → 间隔系统性偏长 | **余量结转**（`Elapsed -= Duration`），dt=0.4/间隔 0.5 跑 1000 帧 = 800 次（断言过） |
| 四通道里 Accurate/Server 的语义学习成本高、还会选错 | 三个常用域有默认值（`Scaled`），`Server` 独立入口 `At(...)`；选域错误在创建时就报失败码 |
| 挂起补偿"一次性注入 60 万毫秒" → 恢复帧回调洪峰 | `Server` 域天然免疫；其余域不做自动补偿（要补偿就选 `Server` 或自己按真实时间算） |
| 2000+ 行核心、10 个 `AddTimer` 重载、"预留第五通道"未实装 | 10 个文件 / 1419 行；**4 个创建入口**；没有预留半成品 |
| 框架自身风格：四处硬依赖（SGameLog/Bugly/UnityCache/Singleton） | 零硬依赖：内核纯 C#，日志/异常走**可替换出口**，单测用假时钟 |

## 七、与你早期计时器（`TimerMgr`）的对照

| 旧实现的硬伤（都在旧代码里能指到行） | 本框架怎么根治 |
|---|---|
| 结束后对象**两次归还**池 → 池里两份、回调 NRE（`delList` 与 `RemoveTimer` 双路径入池） | 池化交给 `RevPoolCore`：**重复归还拦截**（引用相等集合）+ 空壳懒清扫 |
| 回调抛异常 → `async` 循环 faulted → **整个计时器系统静默死亡**（只有外部主动 `Start()` 才能救） | 回调逐个隔离；没有任何常驻 async 循环，**不存在"整条泵死掉"的形态** |
| 边 `foreach` 边改字典 → 回调里建/删必 `InvalidOperationException` | 先收集后执行 + 派发期间不动表（本帧末尾统一回收） |
| `intervalTime` 默认 0 → 每 100ms 回调一次的风暴 | 创建时校验：循环必须给正间隔 → `InvalidDuration`（断言过） |
| 恒定 100ms 减法（拖帧时计时不足、<100ms 被抬到 100ms） | 按真实 delta 累积、double 精度；`NextFrame` 支持"下一帧"这种亚帧需求 |
| 句柄是裸 `int`（不可失效 / 溢出撞键 / 停不中也不报错） | 代际号句柄 + 明确的失败原因码 |
| 暂停/重置语义混淆；`ResetTimer` 会顺手恢复运行；没有剩余时间读数 | `Pause`/`Resume`/`Restart` 分开；`Left`/`Progress` 直接给 UI |
| 真时/缩放靠**两个字典 + 两条循环**（同一份逻辑写两遍） | 一个表 + 四个域，同一套推进代码 |
| 单例 + 私有构造里直接 `Start()` → 一碰就起两条常驻循环，永远不停 | 内核是**普通实例**（`new RevTimerCore()` 可多开、可单测）；宿主只在一个隐藏物体上，`ResetForNewSession` 收尾 |
| 死代码（`WaitForSecondsRealtime` 声明未用）、`Stop()` 不清字典/不还池 | 无死代码；`Clear()` / `ResetForNewSession()` 会把槽位与池都收干净 |

## 八、验收（都是跑过的）

| 项 | 结果 |
|---|---|
| 工程外行为断言（假时钟，无 Unity） | **55 / 55 通过** |
| 零 GC | 10 万次 创建+停止 总分配 **< 4KB**（池化生效） |
| 每帧成本 | 1000 个存活计时器 + 1024 槽位线性扫描 = **0.0139 ms/帧** |
| 编译 | `Revolution.Runtime` **0 错 0 警**；`Revolution.Editor` 0 错（4 条警告是 Unity 测试包自带 `CS0618`） |

断言覆盖的重点（都是"这类系统最容易错的地方"）：**代际失效**（过期句柄打不中新计时器）、
**四域隔离**（Scaled 不吃 unscaled，Fixed 不被渲染帧推进）、**误差契约**（60s/600s 恰好触发次）、
**余量结转**（无系统性偏长）、**重入安全**（回调里停别人/停自己/新建）、**异常隔离**、
**上限拒绝**（1024 满 → `Overflow`，不做静默淘汰）、**owner 与作用域清理**、**全局暂停**、**秒表**。

## 九、最容易踩的 8 个坑

1. **UI 倒计时用了默认的 `Scaled`** → `timeScale = 0`（暂停/慢放）时倒计时不动。要传 `Unscaled`。
2. **循环计时器忘了停** → 数量累积，到 1024 上限后新建会被拒（`Overflow`，会在 `Failed` 里说明原因）。
   正解：`owner: this` + `OnDestroy` 里 `CancelAllOf(this)`，或用 `OpenScope`。
3. **`At(...)` 没先 `SyncServerTime`** → `NoServerTime` + 空句柄（不瞎猜是刻意的）。
4. **以为 `Paused = true` 只影响 Scaled 域** → 它是整个模块的推进开关（含 Server/Fixed）。
5. **在回调里干重活** → 回调跑在 Tick 里，会直接吃掉这一帧。
6. **指望回调精确到帧** → 帧级抖动不可避免（要帧精确请用逻辑帧 `Fixed` 域 + 自己数帧）。
7. **用 `After(0)` 当"立刻"** → 它是**下一帧**，不是同步立即（要在当前调用栈里立刻做，就直接调）。
8. **高频间隔（如 0.01s）当成"一秒几十次"** → 每个计时器**每帧最多触发一次**，余量会保留、下帧继续补；
   真正的吞吐上限是帧率（这是不让回调风暴卡死一帧的刻意设计）。

## 十、卡住了怎么排查

1. **没触发**：域选错了吗（`Fixed` 需要 `FixedUpdate` 在跑；`Server` 需要校准）？`Paused` 是 true 吗？
   句柄是不是已经过期（`h.IsAlive`）？
2. **触发次数比预期多/少**：看第九节第 8 条；`Every` 的实际节奏 = 你给的间隔 + 帧长量化。
3. **报 `Overflow`**：先查循环计时器有没有漏停（用 `RevTimer.Count` 看一眼数量）。
4. **回调异常在刷屏**：订阅 `RevTimer.Failed` 看原因码，`RevTimer.OnException` 拿到原始堆栈；
   出错的计时器不会自动停（参考实现的契约），要停就自己 `Stop()`。
5. **想自己驱动**（例如希望跟着你的逻辑帧走）：`Update` 里 `RevTimer.Tick(Time.deltaTime, Time.unscaledDeltaTime, Time.realtimeSinceStartup)`，
   隐藏宿主会自动让位；逻辑帧再调一次 `RevTimer.TickFixed(Time.fixedDeltaTime)`。
