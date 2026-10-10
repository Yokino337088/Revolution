# RevInput · 输入系统

**一行绑定、一行查询的通用输入系统**（键盘 / 鼠标 / 触屏 / 手柄 / 手势），
`RevInput.Pressed("Jump")` —— 业务不写 `Input.GetKeyDown`，键位怎么绑由绑定表决定。

- 来源：`Assets/Revolution/Runtime/RevInput/`（15 个 `.cs`）
- 引擎依赖：**只有 `Support/` 两个文件碰 `UnityEngine`**（`RevInputDriver` / `RevInputUnityDevice`，
  外加接线用的 `RevInputUnityHooks`）；`Core/` 与 `Implementation/` 是纯 C#，可脱机跑断言
- 零配置：第一次用到就自动建隐藏宿主 `[RevInput]`，不用摆物体、不用挂脚本

## 3 分钟上手

```csharp
// ① 启动时绑一次（或从存档读：RevInput.LoadBindings(存档文本)）
RevInput.Bind("Jump", RevKey.Space, RevKey.JoystickButton0);
RevInput.Bind("Fire", RevMouseButton.Left);
RevInput.BindAxis("MoveX", RevKey.A, RevKey.D);
RevInput.BindAxis("MoveY", RevKey.S, RevKey.W);

// ② 业务里随便问
if (RevInput.Pressed("Jump")) Jump();                  // 本帧刚按下
if (RevInput.Held("Fire"))    Shoot();                 // 按住
if (RevInput.Released("Fire")) StopShoot();            // 本帧刚抬起
float x = RevInput.Axis("MoveX");                      // -1..1（已过死区）
var v = RevInput.Vector("Move");                        // 约定：读 MoveX / MoveY

// ③ 触屏 / 手势（编辑器里用鼠标拖拽就能试：Unity 默认会模拟触摸）
if (RevInput.Tap(out var tap))      Debug.Log(tap.X + "," + tap.Y);
if (RevInput.Swipe(out var sw) && sw.Direction == RevSwipeDirection.Left) Dodge();
if (RevInput.HasGesture(RevGestureKind.DoubleTap)) ZoomIn();
if (RevInput.HasGesture(RevGestureKind.Pinch)) Camera.OrthographicSize /= RevInput.PinchScale;

// ④ 弹窗 / 过场：挡掉世界动作与手势（注意：当前没有 ESC 等系统动作例外）
using (var scope = RevInput.OpenScope())
{
    scope.Block();                                     // 只挡"动作 + 手势"，指针位置还能读
    scope.OnPressed("Confirm", Save);
}
```

## 文件清单

| 文件 | 行数级别 | 读不读 |
|---|---|---|
| `Facade/RevInput.cs` | 唯一入口 | **只读这个就能用**（绑定 / 查询 / 手势 / 屏蔽 / 事件 / 自检全在这） |
| `Core/RevInputDefines.cs` | 词汇表 | 用到了再查：设备类型 / 相位 / 手势种类 / 屏蔽种类 / 错误码 / 全部默认值 |
| `Core/RevInputCodes.cs` | 键位码表 | 写绑定时查（成员名与 Unity `KeyCode` 一致）；`RevKeyMask` 是零分配的键位掩码 |
| `Core/RevInputSnapshot.cs` | 一帧快照 | 想看"每帧的输入长什么样"时读 |
| `Core/RevInputBinding.cs` | 动作绑定 | 想知道"文本格式长什么样"时读（改键存档就是它） |
| `Core/RevInputActionTable.cs` | 绑定表 + 状态 | 改键 / 冲突检测 / 文本存档往返 时读 |
| `Core/RevGestureRecognizer.cs` | 手势识别 | 调手势手感（阈值 / 方向 / 速度）时读 |
| `Core/RevInputBlock.cs` | 屏蔽句柄 | 一般不用读 |
| `Implementation/RevInputCore.cs` | 内核 | 一般不用读（想看"一帧做了什么"时读） |
| `Implementation/RevInputLog.cs` | 日志出口 | 一般不用读 |
| `Support/RevInputDriver.cs` | 隐藏宿主 | 不用读 —— 但它就是你"零配置"的那一行 |
| `Support/RevInputUnityDevice.cs` | 默认设备 | 要换 Input System 包时读（只改这一个文件） |
| `Support/RevInputUnityHooks.cs` | 生命周期接线 | 不用读（进 Play 复位 / 日志出口 / UI 命中判定） |
| `Support/RevInputScope.cs` | 作用域 | 用 `OpenScope()` 时扫一眼 |

## 四条铁律

1. **先绑后用**：`RevInput.Bind(...)` 一次（或 `LoadBindings` 读存档）；没绑过的动作**不报错**，永远返回 false。
2. **切后台必须复位**：宿主已自动处理（失焦 / 切后台 → 清按键、缓冲、连发计时与手势；不合成 `OnReleased`）；你换场景时再调一次 `RevInput.ResetAll("换场景")` 更稳。
3. **弹窗用屏蔽，不要停模块**：`RevInput.OpenScope()` + `scope.Block()`；停模块会把 ESC / 返回键一起停掉。
4. **不要用本模块点 UI**：UI 按钮走 UI 系统（`RevUIPanel.OnClick`）；本模块管世界输入，并提供 `RevInput.IsPointerOverUI()` 帮你分流。

## 详细文档

- 《输入系统 · 使用说明》（手把手）：`Revolution.Document/输入系统/输入系统使用说明.md`（网页版见 [在线文档站](https://yokino337088.github.io/Revolution/)）
- 《输入系统 · 架构解析》（设计论证）：`Revolution.Document/输入系统/输入系统架构解析.md`
