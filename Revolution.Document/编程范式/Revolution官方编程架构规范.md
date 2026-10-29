# Revolution 官方编程架构规范

> 适用范围：使用 Revolution 开发游戏业务的团队及为项目编写代码、测试的 AI Agent。本文规定**业务如何组合已有框架能力**；不要求给框架增加一套 GameFlow、业务 System 或 GM/MCP 内核。以当前源码为准；下文业务接口与域名是项目方自行实现的示意契约，不是框架现成类型。
>
> 版本基线：2026-10-16。实现细节分别见[作用域使用说明](../作用域系统/作用域系统使用说明.md)、[状态机使用说明](../状态机/状态机使用说明.md)、[状态机 Demo 讲解](../状态机/状态机Demo示例讲解.md)与[GM 使用说明](../GM指令/GM指令使用说明.md)。

## 1. 总原则：常用能力走框架，业务只写规则与编排

**先找框架门面，再写游戏特有逻辑**。项目负责登录协议、玩家数据、关卡规则及它们的组合，不重新实现框架已经提供的加载、回收、派发、计时或调度。业务不要绕开公开 API 直接调用 `Core/Implementation/Support`，不要把示例中的业务接口误当成框架门面。

| 需求 | 使用框架现有入口 | 项目只实现 |
|---|---|---|
| 启动、场景切换 | `RevInitialize`、`RevScene` | 登录/大厅/战斗场景选择、失败回退 |
| 游戏阶段、返回式流程 | `RevLightFSM<T>`、`RevLightStackFSM<T>`；AI 用 `RevHeavyFSM<,>` | 阶段状态、合法转移条件、驱动宿主 |
| 阶段内资源与对象归属 | `RevScope`、`RevDomain`、`RevLogicBase` / `RevLogic` | 自定义域名、各阶段开关域、无界面局内逻辑 |
| 共享业务能力 | `RevServices`；同接口并行实例隔离才用 `RevServiceLocator.CreateScope()` | 登录/战斗等业务服务与其注册/注销 |
| UI / 资源 / 对象池 / 配置 | `RevUI`、`RevResManager`、`RevPool`、`RevDataTableManager` | 面板及数据、资源路径、业务配置 |
| 通知 / 时间 / 每帧 / 异步 / 动作 | `RevEvent`、`RevTimer`、`RevMono`、`RevTask`、`RevSequence` | 事件语义、任务取消、演出规则 |
| 输入 / 音频 / 观察 | `RevInput`、`RevSound`、`RevLog` | 绑定与交互、日志标签、失败处理 |
| 可观测业务测试入口 | `RevGM.Register`、`RevGM.Execute`、`RevGMResult` | 有权限边界的业务测试命令与断言 |

三种业务承载点按**生存期**选：仅当前屏幕的装配/显示放面板；无界面且只活一段玩法的业务放 `[RevLogic(域名)]` + `RevLogicBase`；跨阶段的长期能力放服务。不要因为看到“作用域”就把 `RevScope`（游戏域）与 `RevServiceLocator.CreateScope()`（服务实例隔离）混为一谈。

## 2. 整体流程必须由 FSM + 游戏域统一管理

游戏启动入口只初始化框架和项目服务/GM 注册一次；**一个明确的流程控制者**持有 FSM，负责推进与查询当前状态。状态负责自己域的 `Enter` / `Exit`，不越权清别人的域；`RevScope` 内核不依赖 FSM，项目用状态回调把两者接线。业务不应从任意按钮、面板或 GM 命令直接散落调用 `RevScope.ExitAll()` 来“切流程”。

### 2.1 两种合法拓扑

| 业务语义 | 选型 | 登录 → 大厅 → 战斗 → 结算 → 退出的处理 |
|---|---|---|
| 每一阶段互斥，离开大厅就销毁大厅 | `RevLightFSM<T>` | 旧状态 `OnExit` 关旧域，新状态 `OnEnter` 开新域；结算为单独状态/域；退出可进终止状态，再统一收尾 |
| 进入战斗时保留大厅，结算后回到原大厅 | `RevLightStackFSM<T>` | 登录完成后退出 Login 域，再 Push Lobby；Push Battle 时 Lobby `OnSuspend` 停 Tick；若结算有独立生命周期，Push Settlement 及其域；Pop Settlement、Pop Battle 回 Lobby；登出时 Clear 自顶向下退出各域 |

**结算不一定需要独立域**：若只是战斗结尾的一块 UI，可继续归 Battle 域；如果要单独拥有定时器、面板和卸载边界，就使用项目自定义 `GameDomain.Settlement`。退出可用 Clear/终止状态，具体退出应用由平台与业务决定，不能等同于仅卸载场景。线性与栈式不可混用而不说明“大厅是否常驻”的政策。

项目自定义域集中定义为字符串常量，例如 `GameDomain.Login`、`GameDomain.Settlement`；框架 `RevDomain` 只预置 `Global/Lobby/Battle`，不是封闭枚举。资源回收组 `RevResGroup` 仍是另一种枚举：必要时映射到已有分组，**不要为新增游戏域随意改资源组枚举**。`RevScene` 负责载入 Unity 场景，**不会**自动创建/销毁游戏域或替 FSM 切状态；先明确“谁先清理、谁后加载”，不把场景回调当第二台流程机。

### 2.2 状态与域的契约

1. `OnEnter` 进入本状态所属域并保存返回的 `RevScopeHandle`；失败（`Empty`）就报错，不继续启动域业务。只有本域开放后才调用所属 `RevLogic.Start<T>()`；返回 null 须处理。
2. `OnExit` 用保存的句柄只关本域；关非栈顶域会连同上面的域一起关，属于流程顺序错误。`RevLogic` 随域自动 Stop；`OnStop` 先解绑自身受控注册再做业务收尾，资源引用仍按契约释放。
3. 栈状态在 `OnSuspend/OnResume` 使用 `RevScope.SetTickEnabled(domain, false/true)` 控制域 Tick。**暂停不会停计时器、事件和显式 owner 的普通 Mono 回调**；需要这些也暂停时由业务显式处理。
4. 默认 `RevScopeOptions.TakeOverOwner = true` 时，活动域里没写 owner 的 Timer/Event/Mono 归域；显式 owner 优先。域服务不会自动归域，显式 `owner: RevScope.Current` 或把对应 owner `RevScope.Attach(owner)`；长期全局订阅务必用明确的长期 owner。
5. `Reclaim` 默认 `RevResGroup.Unknown`，不整组卸载对象池和资源。只有活动域允许兜底且设非 Unknown 回收组时，`RevResManager` 的 Unknown 才可能补为域组；**加载/释放仍须配对**。跨域共用资源不能误放进战斗专用整组回收范围。
6. 同名域默认拒绝重入；若配置允许重入只是复用已有域，不表示产生新栈层。晚到的加载/网络回调必须核对原域句柄是否仍 `IsOpen`、状态及取消令牌，再提交业务结果。

现成最接近的例子是 `Assets/Revolution.Demo/RevScope.Demo/RevScopeFlowDemo.cs`：大厅域 → 战斗域 → 回大厅 → 退出，含 `RevLogic` 与日志报告；**脚本需挂在任意场景，没有独立 Scope `.unity` 场景**。该 Demo 用域名开关域，工程里持有句柄可以减少误关，但这不是宣称现有 Demo 已改成句柄版。

### 2.3 异步准备期不要提前销毁旧域

`RevLightFSM<T>.ChangeToAsync` 的准备阶段先等待旧状态 `PrepareExitAsync` 和新状态 `PrepareEnterAsync`，**交割时才走旧 `OnExit` → 新 `OnEnter`**。准备期间旧状态仍在运行；新请求可以取消旧准备，await 正常返回也不保证目标真的成为 `CurrentState`，必须再检查。不要在“请求切换”时立刻关闭旧域；资源预加载用真实 `RevResManager` 异步 API 与句柄/释放契约，不把状态机源码注释里假设的 `LoadAsync(..., token)` 当现有资源签名。栈机没有异步 Push/Pop；先用业务侧准备阶段编排，再做同步栈操作。

## 3. 关键业务路径必须留可测试的 GM 指令

GM 是**业务行为的窄入口**，不是另一套业务逻辑。测试命令调用和玩家按钮/网络入口**同一项业务服务或流程控制者**，而不是直接改 UI 字段、FSM 私有表、资源内核、存档文件或服务器状态。优先暴露：状态/域快照、目标及关键计数、可控的阶段转移、受控的结算/失败注入；每个关键阶段至少有“一条可查询 + 一条可触发 + 一条可验失败”的测试路径。

- 在项目的集中注册点仅注册一次：`RevGM.Register(完整分组名, 描述, handler, RevGMArg...)`。业务拒绝抛 `RevGMUsageException("具体原因")`，或返回能观察结果的文本；由 `RevGM.Execute` 统一转成含 `Success` / `Message` / `ElapsedMs` 的 `RevGMResult`。
- `[RevGMEntry("说明")]` 仅用于**静态无参、只做注册**的方法，让编辑器非 Play 状态下列出目录；不是自动生成命令，也不是进入运行时的替代调用。要访问服务/场景的代码放 handler 在 Play 时执行，不能放在注册方法体里。
- `RevGMArg` 的展示默认值与 `args.Int/Float(..., 默认值)` 的实际默认值一致；命令名不能含空白或只含分隔符。命令失败不可静默 return 或仅打日志。
- 测试命令不应把业务类公有 API 都直接映射为命令；暴露最小、可重复、有前置条件的动作，提供状态快照让 AI 能做**前置 → 动作 → 后置**的判定。

以下**项目示意**中的 `IRevGameFlow`、`GameFlowService` 是业务方定义/实现，不是框架现成接口；流程控制者实现 FSM + Scope 接线，GM 只调用它的受限方法：

```csharp
using Revolution;

public interface IRevGameFlow
{
    string Snapshot();
    bool TryEnterBattle(out string reason);
}

public static class GameFlowGmCommands
{
    [RevGMEntry("游戏流程测试命令，只在开发测试环境注册")]
    public static void Register()
    {
        RevGM.Register("流程/快照", "读取当前流程与域状态", args =>
        {
            IRevGameFlow flow = RevServices.GetRequired<IRevGameFlow>();
            return flow.Snapshot();
        });

        RevGM.Register("流程/进入战斗", "从大厅进入战斗（测试环境）", args =>
        {
            IRevGameFlow flow = RevServices.GetRequired<IRevGameFlow>();
            if (!RevScope.IsActive(RevDomain.Lobby)) { throw new RevGMUsageException("大厅域未打开"); }
            if (!flow.TryEnterBattle(out string reason)) { throw new RevGMUsageException(reason); }
            return flow.Snapshot();
        }, RevGMFlags.HighRisk);
    }
}
```

项目 Boot 在测试环境中注册真实的 `IRevGameFlow` 实现后调用一次 `GameFlowGmCommands.Register()`；`[RevGMEntry]` 的编辑期目录扫描会调用注册方法生成目录快照，因此不能把测试环境检查、Service 取用或切场景动作放在注册方法**外层**。不允许在正式包仅靠 `HighRisk` 作保护：它只是编辑器面板的二次确认，直接 `RevGM.Execute` 不经过该确认；`Hidden` 也不是权限。正式环境应**不注册变更类命令**或禁止执行，真实授权由项目/服务端控制，敏感数据不写入返回消息或日志。

## 4. AI / MCP 自动化测试：约定与现状分开

项目可以让 AI 借 MCP 控制 Unity 编辑器 Play，并借**项目自己实现的安全适配器**调用 `RevGM.Execute(commandLine)`，收集 `RevGMResult`、`RevScope.Dump()`、`RevScope.LastCloseReport` 与 `RevLog` 作为证据。当前仓库虽然在 Unity `Packages` 中声明了 `com.coplaydev.unity-mcp`，**没有随框架提供的“GM → MCP”专用工具、自动测试调度器或已验连通性**；编辑器 GM 面板能在 Play 时执行，非 Play 只能列目录。运行时 `RevGM` 后端可供业务自制控制台，但框架未提供可直接复用的游戏内交互控制台。安装 MCP 包不等于 AI 已能执行 GM。

适配器由项目方负责，至少满足：仅在允许的开发/测试环境开启，主线程调用、精确命令白名单及参数上限、必要身份鉴别/访问控制、超时与审计、失败不盲重试、每次测试隔离或清理场景/域。不得把任意代码执行、任意反射、`ExitAll`、强制卸载、删档、真实支付等包装为开放式 GM/MCP 接口；`RevGMFlags.HighRisk` 不能替代权限。AI 不读取/外传凭据与玩家隐私。

### 标准自动验收矩阵（各项目把命令与预期状态映射到自己的业务）

| 步骤 | 调用业务/GM | 必须检查的证据 |
|---|---|---|
| 启动/登录 | 查询快照，触发测试登录 | 有登录结果及进入 Lobby 的明确状态；域只开一次，出错返回可读原因 |
| 大厅 → 战斗 | 快照 → `流程/进入战斗` → 快照 | `RevGMResult.Success`、FSM 当前状态、Lobby/Battle 域状态；栈式 Lobby 保留但 Tick 暂停 |
| 战斗 → 结算 | 触发可控结算/战斗结果 | 结果确实来自业务服务，战斗结束/结算域的生命周期与预期相符，资源和监听无新增残留 |
| 结算 → 大厅/退出 | 受控返回大厅或退出 | 关域报告无异常/扫尾残留（`LastCloseReport` 判空），Logic 数与归属回到基线；退出时无业务域 |
| 非法转移 | 在登录域请求进战斗、重复进入同名域等 | 失败带具体原因，FSM/域/资源状态未被部分修改 |
| 异步取消/场景切换 | 准备阶段取消、迟到回调 | 没有过早关旧域或在失效域里写入数据；`RevScene` 结果与 FSM 状态对齐 |

每条测试保存“环境/版本 → 输入命令 → `Success`、`Message` → 前后 FSM/Scope 快照 → 回收报告/异常日志 → 断言结果”。**有命令不等于测试通过**：还需在 CI/编辑器或目标设备实际执行。纯 C# 离线断言不证明 PlayMode、AB 真加载、Android 真机或 MCP 连通。对无法由 GM 安全覆盖的网络、音频、图形与真机行为另做对应集成/设备测试。

## 5. 新业务模块交付清单

1. 画出 Login/Lobby/Battle/Settlement/Exit 状态图，说明大厅是否常驻、各状态的域名、资源回收组、失败与取消去向；业务域名集中定义，FSM 只由流程控制者驱动。
2. 复用框架门面完成场景、面板、资源、输入、通知、计时与异步；每个回调/句柄标明生存期和清理者；域内 Logic/服务/面板按照域契约归属，避免冗余业务层。
3. 核实异常、非法转移、重复注册、迟到回调、跨域资源和重进 Play；有可执行的 GM 查询、触发与拒绝示例，运行时反馈可被机器判定。
4. 注册测试命令前写下目标环境、白名单与权限；MCP 调用仅在项目已实现并验证安全适配器之后启用，不依赖 `HighRisk` 单独保护。
5. 对照各模块《使用说明》与对应 Demo 执行编译、流程 Play、失败注入、域关闭报告；AB/热更/移动端变更另跑目标平台。区分实际通过与未执行，更新业务文档及测试证据。

## 6. 服务注册：接口做键还是类做键

`RevServices.Register<TService, TImpl>()` 的两个泛型参数分别是**取用键**与**实现类**，不是固定的「接口 + 实现」：

- `TService`：调用方 `GetRequired<TService>()` 用的键；
- `TImpl`：容器真正 `new` 的类型，框架约束 `TImpl : class, TService, new()`。

因此 `Register<X, X>()` 是合法的“自注册”，唯一含义是**让容器创建并按规定释放**。

| 重载 | 谁创建 | 注销时处置 | 适用 |
|---|---|---|---|
| `Register<TService, TImpl>()` | 容器（需公开无参构造） | 容器创建的实例会 `Dispose` | 实现 `IDisposable`、需要收尾 |
| `Register<TService>(instance)` | 调用方 | 只摘表，不 `Dispose` | 外部已托管实例 |
| `Register<TService>(factory)` | 工厂 | 会 `Dispose` | 需要构造参数 |

**判据：按“以后会不会换实现”选。**

| 条件 | 选择 | 例子 |
|---|---|---|
| 多份实现 / 单测要替换 / 只暴露能力 | 接口做键 | `Register<IBattleService, BattleService>()`、`Register<IScoreProgress, ScoreProgressStore>()` |
| 只有一份实现，且调用方要用具体成员 | 类做键 | `Register<StoryProgress, StoryProgress>()`、`Register<GameAudioSettings, GameAudioSettings>()` |
| 接口没有成员，或只有 `Dispose` | 不要抽，直接用类 | — |

> 两种都合规；**不合规的是同一入口里风格混用却没有说明**。出现两种写法时，在注册处用一行注释写明理由（示例见 RevGame `GameBootstrap`）。

> `Register<TService, TImpl>()` 是**懒创建**：不调用一次 `GetRequired` 就不会执行构造函数；依赖构造函数完成订阅或初始化的服务（图集回调、音量应用），必须在注册后立即取一次。`owner` 只解决“谁负责注销”，不等于“随域回收”。

> 本文是游戏项目的**编程架构约定**，不表示框架已提供完整业务流程、真实登录、业务 GM 命令或已连通的 MCP 测试桥。需要项目方实现的是规则和安全接线，而不是第二套框架。
