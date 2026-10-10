# 计时器 RevTimer Bug 修复日志

> 面向小白：每条都按"问题怎么发生 → 原来会怎样 → 现在怎么改"来写，代码旁也有同样口吻的注释。
>
> - 日期：2026-10-10
> - 范围：`Assets/Revolution/Runtime/RevTimer`
> - 构建：完整解决方案成功，0 个警告（已屏蔽既有 Demo/依赖警告）、0 个错误；目录 lint 无诊断；`git diff --check` 通过
> - 未做的验证：未找到本模块的自动化测试，Unity MCP 此前无法连接，没有做 PlayMode / Domain Reload 实机验证

---

## 已修复

### 1. 启动后马上校准服务器时间，Server 域整体偏快（高）
- **怎么发生**：`SyncServerTime(DateTime)` 的锚点取"最近一次 Tick 的 realtime"。登录时往往先收到服务器时间，此时还没创建过计时器，Tick 一次都没跑，锚点就是 0。之后"服务器现在时间 = 校准时间 + 当前 realtime"，多算了"游戏启动到校准"那几十秒。
- **后果**：`At(...)` 计时器在第一帧就提前触发，`Left` 读数也错。
- **修复**：内核新增 `RealtimeProvider`，驱动层接到 `Time.realtimeSinceStartupAsDouble`；校准时直接取真实 realtime，并顺手确保驱动已创建。取不到时（纯 C# 环境）才退回 `LatestRealtime`。
- **代码**：`Implementation/RevTimerCore.cs`（`CurrentRealtime` / `SyncServerTime`）、`Facade/RevTimer.cs`、`Support/RevTimerDriver.cs`

### 2. 只手动调 `TickFixed` 会让其他三个域全部停摆（中高）
- **怎么发生**：宿主的 `Update` 与 `FixedUpdate` 共用一个 `ManualDriven` 标记。业务只在自己的 FixedUpdate 里调 `TickFixed`，标记变 true，宿主的 `Update` 也让位了；反过来只调 `Tick` 也会关掉 Fixed 域。
- **后果**：UI 倒计时、`After`、`At` 静默不再走，没有任何提示。
- **修复**：拆成 `ManualRenderDriven` 和 `ManualFixedDriven`，宿主两个入口各看各的；`ManualDriven` 保留为"任意一路被接管"的兼容属性。
- **代码**：`RevTimerCore.cs`、`Support/RevTimerDriver.cs`

### 3. 订阅者或日志出口抛异常，会中断整帧派发并丢回调（中）
- **怎么发生**：回调抛异常时，上报环节（`Failed` 订阅者、`OnException`、`Log`）没有再保护；它们自己出错，异常就冒出派发循环。一次性计时器已被标记"待回收"，下一帧会被直接释放，回调永远丢失；后面的域也没被推进，`_inTick` 卡在 true。
- **修复**：上报环节逐个订阅者 try/catch；`Tick`/`TickFixed` 用 try/finally 复位 `_inTick`，三个域各自保护，一个域出问题不影响其他域。
- **代码**：`RevTimerCore.cs`（`Fire`、`RaiseFailed`、`ReportInternalError`、`Tick`、`TickFixed`）

### 4. 回调里再次调用 Tick，会破坏外层派发（中低）
- **怎么发生**：手动驱动时，业务把 `Tick` 封装成"强制刷新"，在计时器回调里又调了一次。内层会清掉外层正在遍历的待触发清单，并提前把 `_inTick` 改回 false。
- **修复**：已在派发中时忽略重入的 `Tick`/`TickFixed`。
- **代码**：`RevTimerCore.cs`

### 5. NaN / Infinity 的时间增量会让所有计时器永久失效（低）
- **怎么发生**：旧检查只拦 `< 0`，NaN 能通过，累加后 `GameTime` 与所有 `Elapsed` 变成 NaN，而 `NaN >= Duration` 恒为假，计时器再也不触发。
- **修复**：用 `!(x >= 0)` 拦负数和 NaN，并单独拦 Infinity，一律当 0；非有限的 realtime 不记录。
- **代码**：`RevTimerCore.cs`

### 6. 已关闭的 Scope 仍能创建计时器，且无人再清理（低）
- **怎么发生**：`Dispose` 只清理一次。异步流程里晚到的代码对已 Dispose 的 scope 调 `Every(...)`，计时器以这个 scope 为 owner 创建，永远没人停它，直到 1024 上限。
- **修复**：scope 已关闭时 4 个创建入口都返回空句柄。
- **代码**：`Support/RevTimerScope.cs`

### 7. 创建时抛异常会留下拿不到句柄的"幽灵计时器"（低）
- **怎么发生**：旧顺序是占槽位、写字段，最后才创建驱动宿主；宿主创建失败时计时器已处于运行状态，但调用方没有句柄。
- **修复**：`EnsureDriver` 提前到 `Add` 开头。
- **代码**：`RevTimerCore.cs`

### 8. 参数边界（低）
- `times` 为 `-5` 这类负数会被当成无限循环 → 现在只接受 `-1` 表示无限，其他负数报 `InvalidDuration`。
- 传入 `DateTime.Now`（Local）会让 `Ticks` 差出整个时区 → `At` 的目标时刻与 `SyncServerTime` 的锚点都统一转 UTC（Unspecified 视为已是 UTC）。
- **代码**：`RevTimerCore.cs`、`Core/RevServerClock.cs`

### 9. 复位与钩子（低）
- 关闭 Domain Reload 时，上一局订阅的 `Failed` 处理器在新一局仍会被调用 → `ResetForNewSession` 清空 `Failed`。
- 钩子文件头写"只在自己没被设过时接管"，实际无条件覆盖业务设置的 `Log` / `OnException` → 改为仅在为空时设置默认值。
- **代码**：`RevTimerCore.cs`、`Support/RevTimerUnityHooks.cs`

---

## 审查中发现、本轮未修改（需要先确定语义）

| 编号 | 问题 | 说明 |
|---|---|---|
| A | `RevStopwatch` 是帧粒度：同一帧内读数恒为 0；全局暂停会冻结；复位后旧秒表读数可能为负；Fixed/Server 域被静默降级为 Scaled | 改成读真实时钟会改变现有行为，需要先定语义 |
| B | `Fixed` 域在 `timeScale = 0` 时随 `FixedUpdate` 停止，与文档"照走"不符 | 依据 Unity 引擎行为推断，需要先在引擎里验证再改文档或实现 |
| C | `At` 计时器的 `Pause` / `Restart` / `Progress` 语义不成立（暂停不推迟目标时刻，Progress 恒为 1） | 需要决定是平移目标时刻还是直接拒绝 |
| D | 同一帧内被 `Pause` 或 `Restart` 的计时器，仍可能在本帧触发 | 需要给条目加调度序号，改动较大 |
| E | 回调里"先 Stop 再创建"接近 1024 上限时可能误报 Overflow | 极端边界 |
| F | 回调里新建的其他域计时器，可能当帧就被后续域推进，违反"`After(0)` = 下一帧" | 需要创建帧序号 |
| G | 驱动宿主 `HideAndDontSave`，退出 Play 后可能残留于编辑器 | 依据 Unity 行为推断，需要实机验证 |
| H | 值类型作为 `owner` 时 `CancelAllOf` 因装箱永远匹配不上 | 建议文档禁止值类型 owner |
| I | README 中的行数、"55 条断言"与仓库不符，仓库内找不到对应测试文件 | 需要同步文档并补测试 |

> 本轮修复都有明确代码路径依据，但没有自动化测试和 Unity 运行验证，不能据此声称模块已无其他 Bug；上表是已知遗留项。
