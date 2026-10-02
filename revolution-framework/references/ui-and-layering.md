# UI 与分层（RevUISystem 专题 + "要不要 System / BusinessLogic"）

> 权威文档：`Revolution.Document/UI系统/UI系统使用说明.md`、`UI系统架构解析.md`（**第十章**专门回答分层问题）。

## 1. UI 主链路（声明 → 绑定 → 打开 → 传数据 → 关闭）

```csharp
// ① 声明：特性写在面板类上（root 必填；name 省略 = 用类名）
[RevUIPanel(RevResPath.Data, RevUILayer.Normal, ExclusiveGroup = "BagTab")]
public sealed class BagPanel : RevUIPanel<BagData>
{
    [RevBind] private Button _btnClose;                 // 字段名 ↔ 节点名
    [RevBind("Top/Title")] private Text _title;         // 显式路径

    protected override void OnBindView() { }            // 只装配：找节点、挂事件
    protected override void OnRefreshView() { }         // 只落屏：把 Data 画上去（带数据面板必实现）
    protected override void OnDataChanged(BagData o, BagData n) { }   // 增量刷新（可选）
    protected override void OnClick(string path) { }    // 只转发：按钮点了该干什么
}

// ② 打开 / 传数据（业务侧）
RevUI.Open<BagPanel, BagData>(new BagData { UseCellLayout = true });     // 带数据（推荐）
RevUI.Open<BagPanel>(p => p.SetData(default));                            // 或者回调式
await RevUI.OpenAsync<BagPanel, BagData>(data);                           // await 版

// ③ 关闭
RevUI.Close<BagPanel>();      RevUI.Back();      RevUI.CloseAll(RevUILayer.Popup);
```

- 数据流是**单向**的：业务 `SetData` → 基类 `OnDataSet(object)` → 强类型 `Data` → `OnDataChanged(old, new)` → 可见则 `RefreshView()`。
- 带数据的 `RevUIPanel<TData>` 把 `OnRefreshView` 定成 **abstract**：有数据就必须写清"数据怎么落到界面上"，不允许糊过去。
- 复用时要连数据一起清（框架有 `InternalClearData`），否则会出现"上次的数据还在"。

## 2. 同步 Open vs 异步 Open（最常踩）

- `RevUI.Open<T>()`（无回调那个重载）**不加载任何资源**：只在"已打开过"或"实例池里有"时拿得到实例；需要加载时返回 `null` 并报错。
- 真机预制体在 AB 里 → **首次打开必然异步**：用 `Open<T>(onOpened)` / `Open<T, TData>(data, onOpened)` / `OpenAsync<T>()`。
- 想"点开秒现"就先 `RevUI.Preload<T>()`（预热），之后再走同步 `Open<T>()`。

## 3. 面板池 / Back 语义

- **面板池是 UI 专用池**（不套通用对象池）：关闭优先回池（`KeepAlive`），池满或 `DestroyOnClose` 才真销毁；上限 `RevUISetting.MaxCachedPanels`。取出时必须走 `OnReuse` 钩子（重置界面状态）。
- `RevUICacheMode`：`Unspecified / DestroyOnClose / KeepAlive`…
- **`RevUI.Back()` 只关"最晚打开、且 `InBackStack = true`"的面板**；飘字 / 常驻 HUD 一律 `InBackStack = false`。
- `ExclusiveGroup`（特性参数）= 同组互斥（背包页签这类）。
- 关闭优先级：面板自己的 `RequestClose()`（可被拦截，例如"有未保存内容"）→ `CloseSelf()`。

## 4. Part / 宿主

| 类型 | 怎么用 | 什么时候用 |
|---|---|---|
| 节点级 Part | 不加特性，用 `[RevBind]` / `GetComponent` 拿 | 只是把节点上的组件包一层内聚逻辑，零加载 |
| 预制体 Part | 类上加 `[RevUIPart(root, name)]`，`RevUIPart.Create<T>(host, parent, onCreated)` | 可跨面板复用的界面块（弹窗、列表项） |

- 面板即宿主：`PartRoot => transform`、`IsHostOpened`、`RequestClose`、`NotifyPartChanged`。
- Part 状态变化 → 通过宿主通知（`IRevUIPartHost.NotifyPartChanged`），**不要**让 Part 直接找别的面板。

## 5. 分层：要不要做 System / BusinessLogic？（结论：不做）

| 判定项 | 结论 |
|---|---|
| 能力上缺不缺 | **不缺**：跨界面共享 → 服务定位器；单屏流程 → 面板 + `TData`；界面间通信 → `RevEvent` + Part 宿主中转；数据更新 → `SetData` + `OnDataChanged` |
| 框架要不要提供基类 | **不要**：框架给"机制"（容器 + 面板基类），分层是"约定"；把约定做成基类 = 让所有人付代价 |
| 做了的成本 | 每屏 3~4 个文件、4 跳数据流、两套生命周期、排查路径变长 |

**分工规则（一句可执行）**：跨界面复用的 = 服务；只属于这一屏的 = 面板自己的。
判断法：这段逻辑"**关掉这个界面之后还该活着吗**"？该 → 服务；不该 → 面板里。

分档落地：

| 档 | 典型界面 | 逻辑放哪 | 文件数 |
|---|---|---|---|
| 简单 | 设置页、说明页、加载页 | 全在面板类 | 1 |
| 中等 | 背包、商店、邮件列表 | 面板 + `TData` 数据对象；数据从服务取 | 2 |
| 复杂 | 战斗结算、活动、跨界面流程 | 面板只管装配 / 落屏 / 转发；规则与流程抽成**业务服务**（纯 C#）；必要时再加 `XxxFlow` | 3~4 |

**什么时候必须抽走（信号）**：

1. 同一套流程被 ≥2 个界面复用（如"购买流程"）→ 抽领域服务（`IPurchaseService`），**不是**新增 UI 层；
2. 逻辑要"无 UI 也能跑"（单测 / 离线算 / 与服务端同规则）→ 必须是**纯 C#、不引用 UnityEngine** 的类，放服务层；
3. 单屏逻辑 > 500 行或状态机复杂（多分支、可中断、可回退）→ 抽 `XxxFlow`；
4. 将来真要上**代码热更** → 那时才需要"可热更逻辑 vs 不可热更表现"的物理分层。

**面板里出现这些就该抽走**：协议 / HTTP、配置表解析、跨面板共享状态、计时器与长任务、纯计算（公式 / 排序 / 筛选）、存档读写、跨模块事件路由。
抽的时候**不要去改面板基类**（改基类 = 把特例变成所有人的负担）。

## 6. 写 UI 的自检清单

- [ ] 面板类上有 `[RevUIPanel(root, layer)]`；路径用 `RevResPath.*` 常量，不手拼字符串。
- [ ] `[RevBind]` 字段名与节点名一致（或写全路径）；`OnBindView` 里不做业务。
- [ ] 有数据的面板：`OnRefreshView` 只用 `Data` 渲染，不在里面发请求 / 读表。
- [ ] 按钮点击统一走 `OnClick(path)` 或绑定的方法，不在多个地方散落逻辑。
- [ ] `ExclusiveGroup` / `InBackStack` / `RevUICacheMode` 按语义设置（弹窗不该进返回栈；常驻 HUD 不该来回销毁）。
- [ ] 打开一律 `Open<T>(onOpened)` 或 `OpenAsync`；需要秒开就先 `Preload`。
- [ ] 面板里没有"只给这一屏用"以外的重逻辑（跨屏复用的一律搬去服务）。
