# 日志（RevLog）—— 3 分钟上手

> **一句话**：`RevLog.Warn("配置缺失，用默认值", "Config")` —— 分级、带 tag、能关能查、正式包零成本。
> 8 个 `.cs` / 1053 行（注释 282 + 净代码 585）。**你要读的只有 1 个文件**：`Facade\RevLog.cs`。
> 内核是纯 C#（只有 2 个文件碰 UnityEngine），**41 条行为断言在工程外跑通**。

## 一、三行跑起来

```csharp
RevLog.Info("登录成功");                                  // 普通信息
RevLog.Warn("配置缺失，用默认值", "Config");                // 第二个参数就是 tag（不会写反）
RevLog.Exception(e, "战斗开始失败", "Battle");              // 带异常对象（控制台可点击跳转）
```
**零配置**：Unity 下自动装好控制台通道与帧号；纯 C# 环境走标准错误兜底，**任何情况都不静默**。

## 二、全部 API

| 你要做的事 | 一行 |
|---|---|
| 打日志（五个级别） | `RevLog.Debug / Info / Warn / Error / Exception(e, msg, tag)` |
| 指定级别 | `RevLog.Log(RevLogLevel.Warn, "消息", "Tag")` |
| 热循环里先问一句（省掉拼接） | `if (RevLog.IsEnabled(RevLogLevel.Info, "Net")) RevLog.Info("...", "Net")` |
| 按模块关日志 | `RevLog.MuteTag("Network", true)` / `RevLog.IsTagMuted("Network")` |
| 调输出阈值 | `RevLog.MinLevel = RevLogLevel.Warn;` |
| 落盘（异步、不卡帧） | `RevLog.EnableFileLog(Application.persistentDataPath + "/revlog");` |
| 接崩溃上报（默认不接） | `RevLog.OnReport += (e, msg) => 你的平台.Report(e, msg);` |
| 看现场（出事前的日志） | `RevLog.Dump(200)` / `RevLog.Recent(50)` / `RevLog.Count` |
| 加自己的通道 | `RevLog.AddSink(new 你的Sink())`（实现 `IRevLogSink` 三个方法） |
| 落盘 / 清空 / 自检 | `RevLog.Flush()` / `RevLog.Clear()` / `RevLog.SinkErrors` |

## 三、目录（你只需要读第一个）

| 文件 | 行数 | 说明 |
|---|---|---|
| `Facade\RevLog.cs` | 156 | ★ **唯一入口**：五个级别 + 开关 + 通道 + 现场 + 出口 |
| `Core\RevLogDefines.cs` | 125 | **级别 = 成本契约**、条目字段、上限（纯 C#） |
| `Core\RevLogRing.cs` | 94 | 环形缓冲：容量是特性（纯 C#，零分配写入） |
| `Interfaces\IRevLogSink.cs` | 37 | 输出通道契约（Write / Flush / Dispose） |
| `Implementation\RevLogCore.cs` | 260 | 内核：过滤 → 重复抑制 → 留底 → 派发（纯 C#，**不用读**） |
| `Implementation\RevFileSink.cs` | 259 | 异步写文件 + 大小/份数轮转（纯 C#，**不用读**） |
| `Support\RevConsoleSink.cs` | 58 | Unity 控制台通道（**不用读**） |
| `Support\RevLogUnityHooks.cs` | 64 | ★ **全框架日志接入点**（**不用读**） |

## 四、三条铁律

1. **选对级别**（因为级别就是成本）：
   `Debug` 正式包**不存在**（编译期删除，连字符串拼接都不发生）；`Info/Warn` 便宜（不采堆栈）；
   `Error/Exception` 才采堆栈（微秒级，只在真出错时付）。
2. **热循环里别拼字符串**：先 `RevLog.IsEnabled(...)` 再拼（`Debug` 例外：它在正式包里被删掉，随便写）。
3. **不打日志也要有代价意识**：筛掉的日志只花一次枚举比较；真正贵的是**参数构造**，这才是要避免的。

## 五、与参考实现日志系统的对照

### 5.1 抄来的（精华）

| 参考实现哲学 | 本框架怎么落 |
|---|---|
| `[Conditional]` 编译期删除调用点与参数表达式 | 保留：`Debug` 上是 `[Conditional("UNITY_EDITOR"), Conditional("REVLOG_DEBUG")]`（业务想要 Debug 就在自己工程加 `REVLOG_DEBUG`） |
| **级别即成本契约**（用注释三遍强调纪律） | 升级成**结构性保障**：堆栈只在 Error 及以上采，业务不用记纪律 |
| 门面不写文件、输出通道解耦 | `IRevLogSink` 列表（升级版：参考实现是单一事件，只能挂一个、还会漏挂） |
| 事件/门面为 null 时日志静默丢失 | **绝不静默**：没有通道就走兜底出口（Unity = Debug，纯 C# = 标准错误） |
| 异步文件写入（专用 Lowest 线程 + 双缓冲） | `RevFileSink` 同款；★ 但**入队的是条目不是字符串** —— 格式化在后台线程做，主线程连一次分配都没有 |
| 崩溃前尽量落盘（Dispose 先排空再关） | `Flush()` 等排空（≤1 秒）后自己兜底写完；Unity 退出时由钩子自动 Flush |
| 启动时探测写权限，失败不影响游戏 | 保留 + **升级**：失败会明确报进 `RevLog.SinkErrors`（参考实现是静默降级，日志失能没人知道） |
| 上报与日志解耦（参考实现：CrashSight 硬编码） | `RevLog.OnReport` 事件：接崩溃平台只需一行，不接就不报 |

### 5.2 补上的空白（参考文档里承认"没有解"的）

| 参考实现的空白 | 这里怎么做 |
|---|---|
| **无重复日志抑制/采样/聚合**（刷屏只能靠人工纪律 + 文件轮转兜底） | **连续重复抑制**：同一句连刷只留首条（立刻可见），等出现不同日志时补一条"重复 N 次"；10 万次相同日志 = **零分配** |
| **无模块级开关表**（文档承诺了，实现里没有） | `RevLog.MuteTag("Network")`（tag 静默表，运行期可改） |
| **无单文件大小上限**（只有"份数"，`SetLimit` 的 size 参数在文档里失传） | `RevFileSink` 大小 + 份数双上限（默认 4MB / 10 份） |
| **日志系统失能不可观测**（目录无权限 → 静默降级） | `RevLog.SinkErrors`（通道坏掉/写盘失败/目录不可写都计入），第一次失败还会喊一声 |
| **无"出事之前的现场"** | 环形缓冲（默认 2048 条，硬顶）→ `RevLog.Dump(200)` 直接复制现场，不用提前开文件 |
| **帧号只有渲染帧** | `FrameProvider`（Unity 接 `Time.frameCount`）；**没有时用 -1**，不用 0 冒充（第 0 帧是合法帧号） |
| **两个"频道"参数语义冲突**（`Debug(eLog, msg)` vs `Debug(msg, "Module")`，极易写反） | 只有**一个** tag 轴，且是**具名的第二个参数**：`RevLog.Warn(msg, tag)` —— 不可能写反 |

### 5.3 丢掉的（糟粕）

| 参考实现的问题 | 本框架的处理 |
|---|---|
| `SGameLog.cs` 约 1500 行 / 4 级 + 12 分类 + 17 个 API（Error 家族就有 6 个变体） | 8 个文件 / 1053 行；五个级别 + 6 个写入口；没有"命名不可推导"的变体家族 |
| 开关散落 6+ 处（编译宏 3 组 + 4 个运行期字段） | 三处：`MinLevel`（级别）、`MuteTag`（模块）、`[Conditional]`（编译期） |
| 12 个分类硬编码枚举（加一类要改枚举 + 重新编译全工程） | tag 是**字符串**，加分类零代码 |
| 门面里 `FilterLogLevel` 的过滤落在控制台函数内部 | 过滤在**派发之前**（筛掉的日志不进环形缓冲、不进任何通道，连格式化都不会发生） |
| `eLog.Exception` 与 `enLogType.Exception` 语义重复，且 `Exception()` 实际写的是 `Normal` 文件 | 级别就是级别；要不要单独文件由 Sink 决定，不是门面的负担 |
| 重登/换账号/切场景没有任何状态治理（静态 writer 常驻） | `ResetForNewSession()`（进 Play 自动调）：清历史 + **关掉所有通道**（文件句柄与后台线程一起收干净） |

## 六、全框架接入（"不制造第二套日志"）

**接入前**：框架有 5 个分散出口（各自打 Debug 或标准错误）+ 2 处无人接管（UI / 状态机直接打 Debug）+ 6 处裸 `Debug.Log`。

| 现在 | 接法 |
|---|---|
| 事件系统 | `RevEvent.Log/OnException` → `RevLog.Warn/Exception`（tag = Event） |
| 对象池 | `RevPoolLog.Sink` → `RevLog.Warn`（tag = Pool） |
| 计时器 | `RevTimer.Log/OnException` → `RevLog.Warn/Exception`（tag = Timer）；内核兜底也从"静默"改成 RevLog |
| UI 系统 | `RevUILog.Error/Warning/Info` → RevLog（tag = UI） |
| 状态机 | `RevHeavyFsmLog` 的默认出口 → RevLog（tag = StateMachine） |
| 宿主适配层告警 | 计时器驱动、动作序列驱动、编辑器直读策略的裸 `Debug.Log*` → RevLog |
| **音效 / 资源加载** | **不接**（它们的契约是"失败带原因码 + `Failed` 事件 / `handle.ErrorReason`"，业务订阅即可） |

> 结果：Runtime 里真正的 `Debug.Log*` 只剩 `RevLog` 自己的两个文件（控制台通道 + 兜底出口）。
> 一键换后端：`RevLog.AddSink/RemoveSink` + `RevLog.Fallback` + `RevLog.OnReport` 三个口子，业务不需要改任何调用点。

## 七、最容易踩的 6 个坑

1. **以为 `RevLog.Debug` 在正式包里"打得出来但被过滤"** → 它是**编译期删除**（连参数都不求值）。要在正式包看 Debug，给业务工程加 `REVLOG_DEBUG` 宏。
2. **在热循环里拼字符串** → 先 `IsEnabled` 再拼；`Debug` 例外。
3. **`Error` 级别什么都往上糊** → 它默认采堆栈（有成本）。"可疑但能跑"用 `Warn`。
4. **忘了 `Flush`** → 文件通道是异步的（这是不卡帧的代价）。切场景/退后台/出包前调一次 `RevLog.Flush()`（Unity 退出时钩子会自动调）。
5. **重复抑制看着像"日志丢了"** → 连续相同的只留首条 + 一条"重复 N 次"汇总；要每条都留就换 tag/消息（或 Flush 一次）。
6. **`SinkErrors > 0` 却继续跑** → 说明某个通道在坏（磁盘满/目录没权限/自定义 Sink 抛异常）。查 `RevLog.SinkErrors`，它在告诉你"日志可能在丢"。

## 八、验收（都是跑过的）

| 项 | 结果 |
|---|---|
| 工程外行为断言（无 Unity） | **41 / 41 通过** |
| 零分配 | 10 万次相同日志 = **0 B**；10 万条不同日志 = **0 B**（除日志条目本身） |
| 环形缓冲 | 硬顶 2048 条（写满覆盖最旧的） |
| 文件通道 | 异步落盘 + 大小轮转 + 份数上限 + 目录不可写时禁用并报错 |
| 编译 | `Revolution.Runtime` **0 错 0 警**；`Revolution.Editor` 0 错（4 条是 Unity 测试包自带 `CS0618`） |

断言覆盖的重点：级别阈值 / tag 静默 / **堆栈只按级别采** / **帧号缺失值 -1** / **重复抑制（首条立刻可见 + 汇总）** /
**通道隔离（一个通道炸了不影响别的、不传染调用方）** / 环形缓冲顺序与覆盖 / 异常对象与上报出口 /
**异步落盘与轮转** / **目录不可写时失能可见** / 性能与分配。

## 九、卡住了怎么排查

1. **看不到日志**：`RevLog.MinLevel` 是不是太高？tag 被 `MuteTag` 了？是不是 `Debug` 而正式包没有 `REVLOG_DEBUG`？
2. **日志重复刷屏**：连续相同会被自动抑制；如果还在刷，说明消息里带了变化的字段（时间/坐标）—— 那属于"真的不同"。
3. **文件里没有**：没调 `EnableFileLog`，或者目录不可写（查 `RevLog.SinkErrors`）。
4. **颜色/堆栈不好看**：`Exception` 走 `Debug.LogException`（可点击跳转）；`Error` 只是红字 + 堆栈文本。
5. **想接自己的日志后端**：`RevLog.AddSink(自定义)` 或直接 `RevLog.Fallback = 你的出口`（业务自定义出口后，框架所有模块的日志都会走它）。
