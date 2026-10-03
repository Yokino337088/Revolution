# 服务定位器（RevServiceLocator）—— 3 分钟上手

> **一句话**：把"底层框架要调上层能力"从"写死依赖 / 全局静态"变成 **显式注册 + 显式获取**。
> 整套 8 个 `.cs` / 702 行（其中注释 250 行），**你要读的只有 2 个**（317 + 137 行，且一半是注释与示例）。

## 一、三步走

```csharp
// ① 启动期装配（只写一次，缓存到 static readonly）
static readonly RevServiceLocator Services = RevServiceLocator.Create()
    .AddSingleton<ISoundService, SoundService>()     // 全局一份（第一次被取用时才创建）
    .AddSingleton<IConfigService>(Config.Load())     // 已有实例（容器不释放它）
    .AddScoped<ICombatContext, CombatContext>()      // 每局一份
    .Build();

// ② 运行期取用（一行）
var sound = Services.GetRequired<ISoundService>();   // 必须有：没注册就报错，错误信息里列出已注册项
Services.Get<IDebugService>()?.Draw();               // 可选能力：没有就 null

// ③ 一局 / 一个场景的边界：作用域（共享 Singleton、各持一份 Scoped、退出即释放）
using (var battle = Services.CreateScope())
{
    var ctx = battle.GetRequired<ICombatContext>();
    battle.Tick(Time.deltaTime);                     // 驱动这一局里实现了 RevITickable 的服务
}   // ← 离开 using：按"逆创建序"释放这一局的服务
```

## 二、只需要读这 2 个文件

| 顺序 | 文件 | 内容 |
|---|---|---|
| ① | `Facade\RevServiceLocator.cs` | 唯一入口：`Create` / `GetRequired` / `Get` / `TryGet` / `CreateScope` / `Tick` / `Dispose` |
| ② | `Facade\RevServiceBuilder.cs` | 注册：`AddSingleton` / `AddScoped` / `Build`（含重复注册、空工厂的报错） |

**其余都不用读：**

| 目录 / 文件 | 是什么 | 要不要读 |
|---|---|---|
| `Interfaces\RevIServiceLocator.cs` | 取服务的**只读契约**（3 个方法）—— 底层模块只依赖它，不依赖具体容器 | 建议扫一眼（30 行） |
| `Core\RevServiceLifetime.cs` | `Singleton` / `Scoped` 两个生命周期 | 扫一眼 |
| `Implementation\RevServiceRegistry.cs` · `RevServiceDescriptor.cs` | 引擎内部：实例表、逆序释放、Tick 列表、循环依赖检测 | 不用读 |
| `Interfaces\RevIServiceInit.cs` · `RevITickable.cs` | 可选钩子：创建后取依赖、每帧做事 | 用到再读 |

## 三、与参考实现服务定位器的对照（取精华 / 去糟粕）

### 3.1 保留的精华（都有出处）

| # | 精华 | 出处 | 本框架怎么落实 |
|---|---|---|---|
| 1 | **显式注册 + 组合根唯一前置**（"运行期只读"） | `00_设计哲学.md:53-61` | 注册只能发生在 `RevServiceBuilder` 上，`Build()` 之后连改的机会都没有（纪律变成类型） |
| 2 | **接口优先 / 依赖倒置** | `00:38-43` | 注册与获取都面向接口；错误信息还会提醒"注册写的接口、取也要用接口" |
| 3 | **幂等注册** | `CSystemManager.cs:64-67` | 重复注册直接报错（不静默覆盖），从源头保证"一处装配" |
| 4 | **零反射构造**（IL2CPP/AOT 安全） | `CSystemManager.cs:60/69`、`04:58-71` | 泛型 `where TImpl : new()` 闭包工厂 —— 无需参考实现那 22 个手写 switch 分支 |
| 5 | **"先入表、再 Init"不变量** | `CSystemManager.cs:70-76` | 保留该顺序（避免 Init 里又去创建而打转），并补上它缺的"失败回滚" |
| 6 | **能力探测式挂载** | `CSystemManager.cs:86-132` | 创建时探测 `RevITickable` → 进 Tick 列表（挂钩子的范围收敛在"这个容器"里） |
| 7 | **逆序卸载** | `CSystemManager.cs:229-236` | 释放按"逆创建序"（后创建的常依赖先创建的） |
| 8 | **作用域隔离**（同类型多实例） | `DimensionBaseWorld.cs:58`、`04:5-19` | `CreateScope()` = World 的泛化：共享 Singleton、私有 Scoped，随作用域释放 |
| 9 | **Try 语义**（"必须有"与"可能有"分开） | `CSystemManager.cs:166-210` | `GetRequired` / `Get` / `TryGet` 三个入口，语义不重叠 |

### 3.2 改掉的糟粕

| # | 参考实现的问题 | 出处 | 本框架的处理 |
|---|---|---|---|
| 1 | **202 个 `public static` 可变字段**（谁都能改，只靠口头纪律） | `SystemManager.cs:17-281` | **零静态可变状态**：容器是实例，装配表 Build 后冻结 |
| 2 | Init 抛异常但实例**仍留在容器里**（半初始化） | `CSystemManager.cs:73-83` | 创建或 `OnInit` 失败 → **回滚摘除** + 异常上抛 |
| 3 | 全量卸载**不摘帧更新钩子**（与单个卸载不对称） | `CSystemManager.cs:229-239` | 释放即清空 Tick 列表与实例表，路径只有一条 |
| 4 | 内部集合**外泄**（`GetSystems()` 返回可变 List） | `CSystemManager.cs:49-52` | 不外泄任何集合；要看状态用 `ToString()` |
| 5 | 重复注册**静默覆盖**（旧实例还不销毁） | `DimensionBaseWorld.cs:191` | 重复注册 → 报错并指出两处来源 |
| 6 | 未注入返回 null 兜底，**掩盖错误**；调用点常不判空 | `SystemManager.cs:17`、`DimensionNpcCamera.cs:119` | `GetRequired` 抛异常且错误信息带"已注册清单 + 常见原因"；只有可选能力才用 `Get` |
| 7 | World 级服务被挂成**全局单例**就破坏多实例 | `00:121`、`05:184` | Scoped 服务**在根容器上取会直接报错**，并告诉你正确写法 |
| 8 | 容器绑死 **MonoBehaviour 单例**，无法脱离引擎单测 | `CSystemManager.cs:39-42` | 纯 C# 实例（不引用 UnityEngine / RevTask），可直接工程外单测 |
| 9 | 三套机制 + 5 个近义入口（`SafeGet`/`Get`/`ContainSystem`/`GetSystems`…），250 字段单文件 | `02:157`、`01:35-55` | 一套入口 3 个方法 + 2 个生命周期；文件按 `Core` / `Facade` / `Implementation` / `Interfaces` 分层 |
| 10 | 无循环依赖保护（靠"先入表"绕过，有死循环风险） | `CSystemManager.cs:75` | **循环依赖检测**：报错并打印依赖链 `A → B → A` |
| 11 | 热路径 `for + is T + as T` 线性扫描，业务还得自己缓存 | `DimensionBaseWorld.cs:213-226` | 类型字典 O(1) 查找（一次 `TryGetValue`） |
| 12 | 枚举 + 手写 switch 工厂的"扩展仪式"（22 个分支） | `04:60-71` | 泛型 `new()` 闭包，新增服务只写一行 `AddSingleton` |
| 13 | 作用域无限嵌套 / 状态机不完整 | `04:180`、`06:95` | 作用域**只允许一层**（`CreateScope` 由根调用），生命周期状态机只有"可用 / 已释放"两态 |

### 3.3 刻意不做的事

- **不做构造注入 / `[Inject]`**（保持"依赖一眼可见"）；**不做自动解析依赖图**；
- **不做 Transient**（"每次一个新对象"直接 `new` 就行，不需要容器）；
- **不打任何日志**（框架沉默，问题靠异常信息暴露，日志交给框架的统一日志系统）；
- **不提供全局静态入口**（`SysMgr` 那种形态是糟粕 #1；真的需要全局入口，在你自己的启动代码里放一个 `static` 字段并只赋一次值）。

## 四、三条铁律（踩了必出 bug）

1. **装配只在一处** —— 组合根写完就 `Build()`，之后任何地方都改不了；
2. **`GetRequired` 用于必需依赖**（漏注册启动期就炸），`Get` 只用于可选能力；
3. **谁创建谁负责释放** —— 容器释放"工厂创建的服务"；你自己 `AddSingleton(instance)` 传进去的对象由你释放。

## 五、常见错与排障

| 现象 | 原因 / 处理 |
|---|---|
| `取不到服务 IXxx（没有注册过）` | 错误信息里已经列出**已注册清单**：对照看是漏注册、还是"注册用接口、取用具体类" |
| `IXxx 是 Scoped 服务，不能在根容器上取` | 用 `using (var scope = Services.CreateScope())` 在边界里取 |
| `检测到循环依赖：A → B → A` | 两个服务在构造/`OnInit` 里互相取；把互相取用挪到 `OnInit` 之后，或改成"只取真正需要的那一个" |
| 启动卡顿（首次取服务慢） | 服务是**第一次被取用时才创建**；昂贵服务想提前建好，就在 `Build()` 之后立刻取一次（预热） |
| 一局结束后状态串味 | 这局的状态应该放 Scoped 服务里，并在 `using` 结束时释放；别放进 Singleton |

## 六、什么时候用它 / 别用它

- ✅ **用它**：底层模块需要调用上层能力（UI 要播声音、动作序列要发奖励）；需要"同一服务类型在不同场景/局面各一份"；需要把业务依赖从框架里赶出去。
- ❌ **别用它**：只为"少写一个构造参数"（那属于过度设计）；需要自动装配的复杂依赖图（那是 DI 容器的活，且与 IL2CPP 热路径相性差）；一个进程内只有唯一实现的简单工具类（`static` 类更直接）。

## 七、单元测试（可直接工程外跑）

本模块是纯 C#，最小编译集合：`Core\*.cs` + `Facade\*.cs` + `Implementation\*.cs` + `Interfaces\*.cs`（不含任何 Unity 引用）。测试要点：

```csharp
var root = RevServiceLocator.Create()
    .AddSingleton<IClock, Clock>()
    .AddScoped<ISession, Session>()
    .Build();

Assert.AreNotSame(root.CreateScope().GetRequired<ISession>(), root.CreateScope().GetRequired<ISession>()); // 作用域隔离
Assert.AreSame(root.GetRequired<IClock>(), root.CreateScope().GetRequired<IClock>());                        // Singleton 共享
```
