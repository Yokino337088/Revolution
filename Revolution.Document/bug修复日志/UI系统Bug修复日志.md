# Revolution UI 系统 Bug 修复日志

> **本轮摘要**：对 `Assets/Revolution/Runtime/RevUISystem` 做了针对生命周期、资源引用、互斥打开、Part 事件绑定、动画和 EventSystem 的静态审计，并修复了发现的高风险路径。此文档记录的是本轮已修改的内容，不表示已经证明 UI 系统不存在其它 Bug。
>
> - 审查日期：2026-10-09
> - 审查范围：`RevolutionFrameWork_Unity/Assets/Revolution/Runtime/RevUISystem`
> - 静态检查：目标目录 lint 无诊断；`git diff --check` 无空白错误
> - Unity 验证：最终一轮 PlayMode / 编译回归未完成。进入 Play 后 Unity MCP 会话失去响应；运行时验证用的临时脚本和预制体已清理

---

## 本轮修复

### 1. 同一互斥组的异步面板可能同时打开

| 项目 | 内容 |
|------|------|
| 现象 | 同一 `ExclusiveGroup` 的 A 面板仍在加载时打开 B，A、B 都可能在加载完成后打开。 |
| 根因 | 原逻辑只关闭已登记到 `_exclusive` 的面板；异步加载中的请求尚未登记，因此没有被互斥处理。 |
| 修复 | `OpenRequest` 保留面板元数据；打开同组新面板时取消其它尚未完成的同组请求，并将其等待回调以 `null` 收尾。迟到的资源加载回调通过请求身份校验释放原句柄，不再打开旧面板。 |
| 文件 | `Runtime/RevUISystem/Implementation/RevUIManager.cs` |

### 2. 关闭动画终态残留到 KeepAlive 面板的下一次打开

| 项目 | 内容 |
|------|------|
| 现象 | 只配置 `HideAnimation` 而未配置 `ShowAnimation` 时，淡出、缩小或滑出后的面板再次从池中打开仍可能透明、缩小或位于屏幕外。 |
| 根因 | 隐藏动画把终态写入控件；`InternalReuse` 原先只清业务数据和事件，没有恢复动画目标的基准状态。 |
| 修复 | 复用时停止面板所有者动画，并遍历面板及后代的动画目标，恢复位置、缩放和透明度基准值。 |
| 文件 | `Runtime/RevUISystem/Core/RevUIPanel.cs`、`Runtime/RevUISystem/Animation/RevUIAnim.cs` |

### 3. ShutdownAll / 销毁面板时动画仍可能运行

| 项目 | 内容 |
|------|------|
| 现象 | `ShutdownAll()` 直接销毁面板时，动画引擎可能仍持有面板或 Part 的动画回调；异步转场的完成回调还可能在清理过程中继续执行。 |
| 根因 | Shutdown 路径没有经过正常关闭流程中的动画清理；释放时也没有先使面板的生命周期版本和状态失效。 |
| 修复 | `InternalRelease` 先将实例标记为已释放 / 已关闭、递增生命周期版本并取消 Part 异步创建，再停止面板与 Part 动画、恢复动画目标。Shutdown 先取消打开请求并清空回调，避免动画完成回调消费旧打开请求。释放操作具备幂等保护。 |
| 文件 | `Runtime/RevUISystem/Core/RevUIPanel.cs`、`Runtime/RevUISystem/Core/RevUIPart.cs`、`Runtime/RevUISystem/Implementation/RevUIManager.cs` |

### 4. 关闭 Domain Reload 后面板资源引用可能泄漏

| 项目 | 内容 |
|------|------|
| 现象 | 反复进入 Play 后，打开中、池中或关闭中的面板 prefab 引用计数可能无法归零。 |
| 根因 | UI 管理器只保存面板对象索引；新会话重置时 Unity 对象可能已经变成假 null，单靠面板属性无法再找回资源路径并配对释放。 |
| 修复 | 面板创建时保存带 `instance ID + 资源路径` 的资源租约。新会话、UI 根节点丢失和 `ShutdownAll` 可通过路径快照释放引用；正常销毁与异步关闭晚到回调共用租约状态，避免漏释放或重复释放。新会话还会丢弃旧打开请求回调，避免回调跨 Play 会话执行。 |
| 文件 | `Runtime/RevUISystem/Implementation/RevUIManager.cs`、`Runtime/RevUISystem/Implementation/RevUIRoot.cs` |

### 5. OnBindView / OnInit 重入 ShutdownAll 时实例可能漏清理

| 项目 | 内容 |
|------|------|
| 现象 | 面板在 `OnBindView` 或 `OnInit` 中触发 `ShutdownAll()`，新实例可能尚未进入打开索引，未被销毁或未归还 prefab 引用。 |
| 根因 | 实例在绑定和用户钩子执行前写入打开索引；原 Shutdown 快照只覆盖已登记面板和池中实例。 |
| 修复 | `ShutdownAll` 额外收集在途打开请求上已创建的面板实例；释放中的实例会使初始化流程失效。`InternalSetup` 在控件绑定及 `OnBindView` 后检查是否已释放，避免继续调用后续初始化钩子。 |
| 文件 | `Runtime/RevUISystem/Implementation/RevUIManager.cs`、`Runtime/RevUISystem/Core/RevUIPanel.cs` |

### 6. 嵌套 Part 的控件事件可能被父面板抢占

| 项目 | 内容 |
|------|------|
| 现象 | Part 子树中的按钮长按 / 松开可能分发到父面板；父面板与 Part 也可能同时为同一控件挂事件监听。 |
| 根因 | 父面板事件扫描会遍历整棵后代树；按住继电器只有一个 receiver，父面板后续绑定可能覆盖 Part 接收者。 |
| 修复 | 绑定器在安装 UGUI 及自定义控件事件前确定控件最近的 `IRevUIUserEvents` 所有者，只为该面板或 Part 安装监听。长按 / 松开继电器也只接受最近所有者的绑定。 |
| 文件 | `Runtime/RevUISystem/Support/RevUIBinder.cs`、`Runtime/RevUISystem/Support/RevUIButtonPressRelay.cs` |

### 7. 业务直接销毁面板后 UI 索引可能留下失效实例

| 项目 | 内容 |
|------|------|
| 现象 | 业务直接 `Destroy(panel.gameObject)` 后，同类型再次打开可能仍命中旧索引；池或互斥组也可能保留失效引用。 |
| 根因 | `RevUIPanel.OnDestroy` 原先只清理事件，不通知 UI 管理器摘除打开表、打开顺序、互斥组和池索引。 |
| 修复 | `OnDestroy` 通知管理器清理相关索引、资源租约和动画；`InternalRelease` 幂等，避免管理器正常释放与 Unity 销毁回调重复执行释放逻辑。 |
| 文件 | `Runtime/RevUISystem/Core/RevUIPanel.cs`、`Runtime/RevUISystem/Implementation/RevUIManager.cs`、`Runtime/RevUISystem/Implementation/RevUIPanelPool.cs` |

### 8. 自定义动画 `Wrap = Once` 错受 Loops 字段影响

| 项目 | 内容 |
|------|------|
| 现象 | 自定义规格设为 `Wrap = Once`，但 `Loops = -1` 时动画不结束；设为大于 1 时会重复播放。 |
| 根因 | 引擎结束条件对所有 Wrap 模式都检查 `Loops`，与 `RevUIAnimSpec` 中“Once 忽略 Loops”的契约不一致。 |
| 修复 | `Once` 在第一段结束时直接终止；循环和往返模式仍按 `Loops` 判定。 |
| 文件 | `Runtime/RevUISystem/Animation/RevUIAnimEngine.cs` |

### 9. Domain Reload 关闭时旧动画句柄可能撞上新会话动画

| 项目 | 内容 |
|------|------|
| 现象 | 业务静态字段保留旧动画句柄时，动画引擎重置 ID 并重新分配相同 ID / 版本，旧句柄可能误操作新动画。 |
| 根因 | `RevUIAnimEngine.Reset()` 会把动画 ID 计数器重置回 1。 |
| 修复 | 重置活动表和对象池时保留单调递增的 ID 计数；新会话生成的句柄不会与上一会话的旧句柄相同。 |
| 文件 | `Runtime/RevUISystem/Animation/RevUIAnimEngine.cs` |

### 10. 关闭 Domain Reload 时动画驱动可能因初始化顺序丢失

| 项目 | 内容 |
|------|------|
| 现象 | UI 动画驱动先注册 `RevMono` Update，随后 `RevMono` 的新会话复位清空监听；动画驱动仍认为自己已安装，动画不再推进。 |
| 根因 | 两个模块在 `SubsystemRegistration` 阶段注册 / 清理，同阶段回调执行顺序不能作为依赖保证。 |
| 修复 | 动画驱动在会话复位时只重置引擎和安装标记；延迟到首次实际播放动画时再注册 `RevMono` Update。 |
| 文件 | `Runtime/RevUISystem/Animation/RevUIAnimDriver.cs` |

### 11. 场景已有 inactive EventSystem 时可能创建重复实例

| 项目 | 内容 |
|------|------|
| 现象 | 场景中存在但暂时未激活的 `EventSystem` 时，自动兜底仍可能额外创建一个常驻 EventSystem；原 EventSystem 后续启用后可能重复处理输入。 |
| 根因 | `EnsureEventSystem` 只查找 active EventSystem。 |
| 修复 | 查找时包含未激活对象；发现已有实例就不再创建。 |
| 文件 | `Runtime/RevUISystem/Implementation/RevUIRoot.cs` |

---

## 验证状态与遗留事项

| 检查项 | 结果 |
|------|------|
| 修改目录静态 lint | 无诊断 |
| `git diff --check` | 通过 |
| Unity MCP 最终编译 | 未确认 |
| Unity PlayMode 回归 | 未完成 |
| 临时回归脚本 / 预制体 | 已删除 |

Unity MCP 在进入 Play 模式后停止响应，无法继续读取 Console、退出 Play 或完成最后的编译 / 运行时回归。因此，以上是**代码审计后实施的修复**，不是运行时全部验证通过的结论。建议 Unity MCP 恢复后优先验证：同组并发打开、隐藏动画后池化重开、`OnBindView` 重入 `ShutdownAll`、关闭 Domain Reload 的资源引用计数、嵌套 Part 长按事件和 Once + `Loops = -1`。

> 静态审查无法证明“没有任何 Bug”。后续应把上述复现路径纳入自动化 PlayMode 测试，并结合实际项目的 Canvas、输入模块和面板转场继续迭代。
