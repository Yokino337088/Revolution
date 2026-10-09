# Revolution 输入框架 Bug 修复日志

> **本轮摘要**：审查 `Assets/Revolution/Runtime/RevInput` 的采集、动作表、手势、事件订阅、复位与 Unity 设备生命周期，修复了一组有明确代码依据的问题，并同步更正输入系统文档里与当前屏蔽语义不符的说法。
>
> - 审查日期：2026-10-09
> - 审查范围：`RevolutionFrameWork_Unity/Assets/Revolution/Runtime/RevInput`
> - 构建：Unity 解决方案 `dotnet build` 成功，0 个错误；构建输出有 Unity 依赖程序集版本冲突警告（非本次输入代码错误）
> - Unity Console：最新脚本刷新后无编译错误
> - EditMode：测试运行器报告 `0` 个测试（空套件通过，不代表自动回归覆盖）
> - 额外验证：通过 Unity 内存执行检查动作表、手势和输入内核；结果列于文末。未运行真机输入设备测试

---

## 本轮修复

### 1. 重入 `Tick` 会覆盖正在派发的当前帧

- **现象**：采集委托或输入回调再次调用 `RevInput.Tick`，内外两次推进共用同一个快照、动作状态和手势队列；外层帧可能被内层输入覆盖。
- **修复**：输入内核增加 `Tick` 重入保护；重入立即抛出明确异常，最外层仍通过 `finally` 复位保护标记。事件 / Poll 的已有异常隔离会捕获该误用，不会留下“永远正在 Tick”的状态。
- **文件**：`Runtime/RevInput/Implementation/RevInputCore.cs`

### 2. Poll 抛异常前写入的部分输入会污染本帧

- **现象**：Poll 先写 `KeyDown` 等字段、之后抛异常，异常处理虽记录“本帧无输入”，但已写数据仍进入动作判定。
- **修复**：采集异常时重置整份快照，再恢复当前帧时间与帧号元数据；部分采样不会继续流入判定。
- **文件**：`Runtime/RevInput/Implementation/RevInputCore.cs`

### 3. 回调内增删订阅会让同帧事件漏派或越界

- **现象**：在事件回调中移除其他 handler / listener 时，边遍历边修改订阅列表会改变索引，导致后续订阅漏收或越界；新加的订阅也可能意外收到半帧事件。
- **修复**：每帧动作判定完成后，把动作状态和各类订阅复制到可复用派发缓冲；本帧按开始时的订阅集合完成派发，增删从下一帧生效。订阅增长时同步扩容派发缓冲，避免在稳定热路径反复扩容。
- **文件**：`Runtime/RevInput/Implementation/RevInputCore.cs`、`Runtime/RevInput/Core/RevInputListener.cs`

### 4. Pointer 屏蔽没有作用于手势识别

- **现象**：屏蔽某根手指后，原手势识别器仍直接读取未过滤的整份快照，该手指仍可能触发 Tap / Swipe，或被算入 Pinch / Rotate。
- **修复**：手势内核接收单指屏蔽谓词；被屏蔽的轨迹会标记为静音，抬起不补发单指手势；双指组合只从未屏蔽指针中选取。原始快照不修改，业务仍可按输入 API 的屏蔽规则读取。
- **文件**：`Runtime/RevInput/Implementation/RevInputCore.cs`、`Runtime/RevInput/Core/RevGestureRecognizer.cs`

### 5. 双指采样顺序变化会造成旋转角跳变

- **现象**：手势内核原先取快照数组的前两根指针。数组顺序变化时，连接向量可能反向，计算出约 180° 的虚假旋转。
- **修复**：稳定选择 ID 最小的两根有效指针，并跟踪当前双指组合 ID；组合更换时重新建立基线，不把换指造成的角度跳变当作旋转事件。
- **文件**：`Runtime/RevInput/Core/RevGestureRecognizer.cs`

### 6. Swipe 使用了抬起前的旧位移判方向

- **现象**：结束采样坐标相较最后一个 Moved 样本继续变化时，距离按终点算、方向与增量却按旧轨迹数据算，Swipe 的 Direction / Delta 与终点不一致。
- **修复**：结束时用实际 Ended 坐标重新计算总位移、方向和事件增量。
- **文件**：`Runtime/RevInput/Core/RevGestureRecognizer.cs`

### 7. 手势事件 Frame 恒为 0，且时间轴零点不能识别双击 / 缓冲

- **现象**：所有手势构造路径都写入常量帧号 `0`；动作缓冲、`LastPressedFrame` 和双击判定又把时间 `0` 当作“从未按过”。
- **修复**：所有手势事件使用输入快照帧号；分别为动作最近按下和最近 Tap 增加显式有效标记，0 秒现在可作为合法时间点。
- **文件**：`Runtime/RevInput/Core/RevGestureRecognizer.cs`、`Runtime/RevInput/Core/RevInputActionTable.cs`、`Runtime/RevInput/Facade/RevInput.cs`

### 8. `RemoveAction` 删除绑定后仍保留订阅

- **现象**：删除并重新创建同名动作后，旧的按下 / 抬起 / 连发 / 轴处理器仍可能被触发；未重新创建时旧订阅也会继续持有 owner。
- **修复**：门面删除动作时同步删除对应的 Pressed、Released、Repeat、Axis 订阅和该动作的轴状态。当前派发快照会再检查动作是否仍存在，删除动作后不再派发其剩余旧回调。
- **文件**：`Runtime/RevInput/Facade/RevInput.cs`、`Runtime/RevInput/Implementation/RevInputCore.cs`

### 9. `OffAxis(axis, null)` 与 `OffRepeat(action, null)` 未退完所有处理器

- **现象**：同一轴 / 动作有多个 handler 时，传 `null` 本应全部退订，实际只删除一个。
- **修复**：持续遍历并删除所有匹配项；返回是否至少移除了一个。
- **文件**：`Runtime/RevInput/Implementation/RevInputCore.cs`

### 10. 已 Dispose 的输入 Scope 还能继续注册内容

- **现象**：`RevInputScope.Dispose()` 后仍可调用 `Block` / 订阅方法；由于 Dispose 幂等，后续再 Dispose 不会清掉新添的条目，造成屏蔽或订阅泄漏。
- **修复**：已 Dispose 的 Scope 拒绝继续创建屏蔽或订阅（屏蔽返回空句柄，布尔订阅返回 `false`，手势订阅 no-op）。
- **文件**：`Runtime/RevInput/Support/RevInputScope.cs`

### 11. 绑定文本重复动作名会破坏 map / list 一致性

- **现象**：相同动作出现多行时，字典只保留最后一条，列表却保留所有副本，后续动作状态和数量不一致。
- **修复**：解析时检测重复动作名，报告具体行号并拒绝整次导入；原有绑定表保持不变。空文本现在按“整体替换”语义清空绑定。
- **文件**：`Runtime/RevInput/Core/RevInputActionTable.cs`

### 12. 改键存档的浮点配置不是无损往返，非法值可能被吞掉

- **现象**：连发时长只保存 3 位小数、死区只保存 2 位小数；无效 deadzone 解析又可能静默保留默认值。超范围、NaN / Infinity 等数据也可能进入状态。
- **修复**：`SaveText` 使用 invariant round-trip 浮点格式；`LoadText` 严格校验死区为有限的 `0..1`、连发 delay / interval 为有限且语义有效的数值，错误时整体拒绝且不覆盖原绑定。`SetRepeat` 与 `SetDeadzone` 入口也检查 / 规范化参数。
- **文件**：`Runtime/RevInput/Core/RevInputBinding.cs`、`Runtime/RevInput/Core/RevInputActionTable.cs`、`Runtime/RevInput/Facade/RevInput.cs`

### 13. 鼠标按钮绑定的动作轴始终为 0

- **现象**：将鼠标按钮绑定到动作后读取 `RevInput.Axis(action)`，按钮按住仍不返回 `1`。
- **根因**：按钮退化轴只检查键盘 / 手柄键位，漏掉鼠标掩码。
- **修复**：按钮退化轴同时检查 `KeyHeld` 与 `MouseHeld`。
- **文件**：`Runtime/RevInput/Core/RevInputActionTable.cs`

### 14. 连发使用 `realtime`，暂停时仍可能继续触发

- **现象**：`Time.timeScale = 0` 时真实时间仍前进，连发继续触发，与动作表文档中“持续时间和连发按 deltaTime 累加、暂停停止”的约定矛盾。
- **修复**：按 `snapshot.DeltaTime` 累减剩余 delay / interval；暂停时不推进。重复间隔必须大于 0，delay 小于等于 0 表示关闭连发。
- **文件**：`Runtime/RevInput/Core/RevInputActionTable.cs`、`Runtime/RevInput/Facade/RevInput.cs`

### 15. 新会话沿用旧 Unity 设备内部缓存

- **现象**：关闭 Domain Reload 时，输入 Core 复位但 `RevInputUnityDevice` 的上次设备类型、键盘 held 并集、鼠标 delta 基线等字段仍跨 Play 保留。
- **修复**：Unity 设备适配器新增 `ResetForNewSession`，清理缓存状态和无效轴名列表；Core 在新会话复位时调用接入的 reset 委托。
- **文件**：`Runtime/RevInput/Support/RevInputUnityDevice.cs`、`Runtime/RevInput/Support/RevInputUnityHooks.cs`、`Runtime/RevInput/Implementation/RevInputCore.cs`

### 16. 新增 Axis 订阅者不会得到自己的初始轴值

- **现象**：全局轴值无变化时，后来新订阅的 `OnAxis` handler / listener 不会收到当前值；轴初始为 0 时更会被整体跳过。
- **修复**：轴处理器和每个 listener 独立记住自己已收到的值；首次派发即使当前值是 0 也回调，后续只在各自观察值变化时回调。绑定表替换或清空会重置对应派发基线。
- **文件**：`Runtime/RevInput/Implementation/RevInputCore.cs`、`Runtime/RevInput/Facade/RevInput.cs`

### 17. 重复键位 / 轴绑定与冲突检测遗漏

- **现象**：无效枚举数值、同一轴负正向使用相同键位或重复命名轴可能通过公开入口 / 冲突检测而未提示；键位输入超上限时可能留下部分绑定。
- **修复**：公开绑定入口校验枚举值和参数；键位数量先完整预检再写入，失败不落半份绑定；同名命名轴纳入 `FindConflicts`。绑定文本也拒绝重复 `repeat` / 命名轴 / 轴方向声明和空命名轴。
- **文件**：`Runtime/RevInput/Facade/RevInput.cs`、`Runtime/RevInput/Core/RevInputBinding.cs`、`Runtime/RevInput/Core/RevInputActionTable.cs`

### 18. 手柄按钮与鼠标滚轮的设备分类可能识别错

- **现象**：手柄按钮使用 `JoystickButtonN` 映射，却被计入键盘活动；滚轮输入没有参与鼠标活动判断；鼠标和手柄同时有输入时，手柄轴探测仍可能覆盖本帧的键鼠设备类型。
- **修复**：采集器分别记录手柄按钮与键盘按键活动；鼠标滚轮纳入鼠标活动判断；仅在本帧触屏、键盘、鼠标和手柄按钮均无活动时探测摇杆轴，再按明确优先级更新设备类型。
- **文件**：`Runtime/RevInput/Support/RevInputUnityDevice.cs`
- **验证限制**：代码已修正并编译；当前未使用真实手柄 / 鼠标设备做 PlayMode 或真机验证。

---

## 文档契约更正

代码现状是 `Block(World)` 屏蔽**所有动作相位和手势**；没有“系统动作”分类或 ESC / 返回键例外。此前使用说明、架构解析和根 README 声称“ESC 照常可用”，与实现不符。本轮先更正文档，避免使用者依赖不存在的例外；若后续需要弹窗期间保留 Cancel / Confirm，应另设计动作分类或独立 UI 输入通道。

`ResetAll()` 是**静默复位**：清空按键边沿、缓冲、连发计时和进行中的手势，不合成 `OnReleased`。需要在失焦 / 暂停时收尾的业务应订阅 `RevAppLifecycle`。

---

## 验证结果与范围限制

### 已执行

- `dotnet build RevolutionFrameWork_Unity.sln --no-restore`：成功，0 个错误；存在 27 个原有 Demo / Unity 依赖版本警告。
- Unity 脚本刷新后的 Console：0 个编译错误。
- 目标目录 lint：无诊断；`git diff --check`：通过。
- Unity 内存执行的纯代码冒烟：
  - 鼠标左键绑定动作轴返回 `1`。
  - 重复动作行、重复轴 / 连发 token、无效数字键名和空命名轴均拒绝，且保留旧绑定；空文本清除绑定。
  - 重复命名轴会被冲突检查发现；连发 / deadzone 浮点精确保存并读取。
  - 连发首按触发，`deltaTime = 0` 不继续计时，达到 delay 后按间隔触发。
  - 手势事件帧号正确；单指屏蔽不产生 Tap；Swipe 终点位移决定方向。
  - 回调中删除自身 owner 时同帧剩余快照监听者仍收到一次；`RemoveAction` 会截断该动作后续回调；`OffAxis(null)` 会退掉全部同轴处理器。
  - Poll 写入部分快照后抛异常时，本帧按无输入回滚；重入 Tick 被拒绝且外层 Tick 仍正常收尾。
  - 已 Dispose 的 Scope 不再能加屏蔽；新轴订阅即使初始值为 0 也收到自己的首值。
  - 双指样本顺序变化不会产生虚假旋转；编辑器内构造的 Swipe 以 Ended 终点计算方向和帧号。

### 未覆盖 / 尚未证明的部分

- Unity EditMode 测试运行器当前没有注册测试用例（`0` 项）；“Passed”是空套件通过，不代表有自动回归覆盖。
- 未在真实键鼠 / 手柄 / 多点触屏设备上验证平台差异。
- `FixedUpdate` 直接轮询 `Pressed` 属于快照式 API：同一输入帧的边沿可能被多个 FixedUpdate 读到；物理消费建议由 Update 捕获后传递。此为 API 时序约定，不在本轮修改。
- `Block(World)` 暂无可排除的系统动作种类，已按真实实现更正文档；是否新增动作分类是后续产品设计，不作为本轮未解决代码 Bug。

> 静态审计和有限冒烟测试无法证明“输入框架没有任何 Bug”。后续应把上述纯代码用例纳入可重复自动化测试，并补充目标平台的 PlayMode / 真机验证。
