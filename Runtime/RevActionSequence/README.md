# 动作序列（RevActionSequence）—— 3 分钟上手

> **一句话**：用代码写"先 A 后 B、B 要等条件、中途能取消"的演出流程。
> 整套文件里，**你要读的只有 3 个**。

## 一、三行跑起来

```csharp
// ① 构建清单（加载期一次，缓存到 static readonly）
static readonly RevSequenceDefinition ChestOpen = RevSequence
    .Create("宝箱开箱")
    .ScaleTo("宝箱弹出", Vector3.one, 0.25f, RevEase.OutBack)
    .WaitUntil("等玩家点击", ctx => ctx.Require<IInputService>().Clicked, 15f)
    .Do("开门", ctx => ctx.Require<IDoorService>().Open())
    .Finally(f => f.Do("恢复镜头", ctx => ctx.Require<ICameraService>().Restore()))
    .Build();

// ② 播放（一行，零配置：框架自动每帧驱动；宝箱被 Destroy 时序列自动取消）
ChestOpen.Play(宝箱物体);
```

服务注册一次：`RevSequencePlayer.Default.Services.Add<IDoorService>(门系统);`

## 二、只需要读这 3 个文件

| 顺序 | 文件 | 内容 |
|---|---|---|
| ① | `Facade\RevSequence.cs` | 构建入口 + 三条铁律 |
| ② | `Facade\RevSequenceBuilder.cs` | 常用方法：`Do` / `Wait` / `WaitUntil` / `Tween` / `If` / `OnCancel` / `Finally` / `Build` |
| ③ | `Support\RevSequenceUnityExtensions.cs` | Unity 现成步骤：`MoveTo` / `ScaleTo` / `FadeTo` / `SetActive`，以及 `清单.Play(gameObject)` |

用到才查：`Facade\RevSequenceBuilder.Advanced.cs`（并行 / 重复 / 嵌套 / 事件 / 等异步 / 自定义步骤）。

**其余都不用读：**

| 目录 | 是什么 | 要不要读 |
|---|---|---|
| `Implementation\` | 引擎内部（Runner / Run / Handle / 池化 / 并发策略 / 事件总线） | 不用读；调优排查时再进 |
| `Implementation\Steps\` | 内置步骤库（等待 / 补间 / 委托 / 事件 / 并行 / 重复 / 分支 / 嵌套的实现） | 不用读；看名字就懂 |
| `Interfaces\` + `Implementation\RevEventTriggerSource.cs` | 触发源契约与实现（可选能力） | 用到再读 |
| `Support\RevSequencePlayer.cs` | 每帧 Tick 的驱动组件 | 不用读 |

## 三、三条铁律（踩了必出 bug）

1. **清单只 Build 一次并缓存** —— `Build()` 之后再改会抛异常，运行期反复构建是浪费；
2. **状态别存在构建期字段里** —— 用业务服务、`Tween` 的起始值，或自定义步骤的"状态槽"（否则两个玩家同时触发会互踩）；
3. **占用了什么（生成的物件、推近的镜头），就在 `.Finally` 里还回去** —— 跑完、取消、出错都会执行；只在取消时才要做的放 `.OnCancel`。

## 四、框架替你兜住的事

| 你可能会犯的错 | 框架怎么处理 |
|---|---|
| 某一步的代码抛异常 | Console 打出「哪条序列、第几步、步骤名」，按取消收尾，其它序列不受影响 |
| 等待条件永远不成立 | 设了超时就放行并告警；`onTimeout` 可以自己处理 |
| 触发者物体被销毁了 | 序列下一帧自动取消，不会再去碰已销毁的对象 |
| 在步骤里 `Stop` 自己 / 调 `StopAll` / `Clear` | 后面的步骤不再执行，收尾正常进行，不会越界、不会重复执行 |
| 同一个步骤实例放进两条序列 | `Build()` 直接报错 |
| 收尾里放了 `Wait` / `Tween` 这类要等的步骤 | `Build()` 直接报错（收尾不会等待） |
| `Services.Add(new SoundImpl())` 写成了具体类型 | `ctx.Get<ISound>()` 照样能取到 |
| 必需的服务没注册 | `ctx.Require<T>()` 报错并告诉你怎么注册 |
| 用了同源策略却没传 `source` | 提醒一次 |

## 五、完整教程

`Revolution.Document\动作序列\`
- **使用说明** = 从"能跑"到"写对"（含五个真实场景 Demo 讲解）
- **架构解析** = 设计动机、通用性论证、与原体系的逐条对照

代码 Demo：`Assets\Revolution.Demo\RevActionSequence.Demo\`（宝箱 / 灵果 / 农场礼盒 / NPC 特写 / 火箭演出）
