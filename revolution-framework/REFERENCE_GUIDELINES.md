# Revolution 代码审查指南（Referece Guidelines）

> 用途：**审查入口**。按本清单逐条过一遍，输出"严重问题 → 规范问题 → 潜在风险 → 优化建议"。
> 每条都对应真实的坑（详见 `references/pitfalls.md`）与代码规范（`references/code-style.md`）。

## 0. 输出格式（固定四段）

| 段 | 内容 | 例子 |
|---|---|---|
| **严重问题** | 会导致崩溃 / 泄漏 / 线上事故 / 数据错 | 事件订阅没 owner 且对象会销毁；热更包实现成"只从持久化读"；引用计数不归零 |
| **规范问题** | 违反本框架硬规范 | 方法括号换行；`if` 不带大括号；类名缺 `Rev` 前缀；绕过门面读文件 |
| **潜在风险** | 现在能跑、边界会出问题 | 同步 `Load` 用在真机首次；`RevTimer.Paused` 当局部暂停；`force` 卸载分组 |
| **优化建议** | 更好写法，不强求 | 提局部变量让调用单行；把纯计算抽到服务以便工程外断言 |

**结论要可执行**：每条给出"改哪一行 / 改成什么"，别只说"建议优化"。

## 1. 清理与生命周期

- [ ] 每个 `RevTimer.After/Every`、`RevMono.Add*` 都有"能停"的路（`owner:` 或 `OpenScope()`），宿主销毁时有 `CancelAllOf` / `RemoveAllOf`。
- [ ] 每个 `RevEvent.AddEventListener` 都有对应移除：`RemoveAllByOwner(owner)` 或字段保存的委托（**匿名 lambda 按委托移除无效**）。
- [ ] 每个 `RevResManager.Load/LoadAsync` 都有对应 `Release`。
- [ ] 池对象：`OnPoolReturn` 清空字段；`Return` 后不再访问该对象；WebGL 用 `GetAsync`。
- [ ] `MonoBehaviour` 的 `OnEnable/OnDisable` 订阅与退订**成对且对称**（别在 `OnEnable` 订阅、在 `OnDestroy` 退订）。
- [ ] 静态字段/静态容器在关闭 Domain Reload 的场景下有复位钩子（照 `Support/*UnityHooks.cs`）。
- [ ] 服务定位器：`AddScoped` 的服务放进 `CreateScope()` 的 `using`；`AddSingleton(instance)` 的实例有人释放。

## 2. 资源（RevResourceSystem）

- [ ] 路径用 `RevResPath.*` 常量或两段式参数，**没有**自己拼 `StreamingAssets` / `dataPath` / `Resources.Load`。
- [ ] 真机首次加载用 `LoadAsync`（不是同步 `Load`）。
- [ ] 资源按域分了组（`Battle`/`UI`/`Config`/`Sound`），没有"什么都传 `Unknown`"（那样永远不会被分组卸载）。
- [ ] 没有用 `UnloadGroup(group, force: true)` 去"省事清理"（会误伤别的域共享资源）。
- [ ] 新增资源打了 AB 标记，并重新生成了 `ResMap.txt` / `RevResPath.cs`。
- [ ] 失败分支看的是 `handle.ErrorReason`（不是 `if (content == null)` 猜）。

## 3. UI（RevUISystem）

- [ ] 面板有 `[RevUIPanel(root, layer)]`，root 用常量；`[RevBind]` 字段名与节点名对得上。
- [ ] `OnBindView` 只装配、`OnRefreshView` 只落屏、`OnClick` 只转发（没有在视图钩子里做业务/发请求）。
- [ ] 有数据的面板走 `SetData` 单向流；`OnRefreshView` 只读 `Data`。
- [ ] 打开用 `Open<T>(onOpened)` / `OpenAsync<T>`；没有在真机路径上用"同步 `Open<T>()` 并假设它会加载"。
- [ ] 弹窗/常驻 HUD 正确设置 `InBackStack`；`ExclusiveGroup` 按语义设置；`RevUICacheMode` 与 `KeepAlive` 一致。
- [ ] 面板里没有"跨界面复用"的重逻辑（该抽成服务）；面板没有缓存未在 `OnReuse` 里重置的状态。

## 4. 异步（RevTask）

- [ ] 没有引入 `System.Threading.Tasks.Task` / 协程写业务（协程仅用于桥接旧代码）。
- [ ] `async void` 只出现在事件/按钮入口，且**整体 try/catch**（否则异常静默）。
- [ ] 关键节点有 `token.ThrowIfCancelled()`；宿主销毁 / 切场景能取消。
- [ ] 不 await 的任务显式 `Forget()`；没有在异步里 `new` 大对象 / 做重活（该分帧）。
- [ ] 没有依赖"续体立即全部执行"（每帧限量 128）。

## 5. 事件 / 时间 / 输入

- [ ] 事件名来自常量类；同一事件名只有一种签名；派发返回值 `0` 时有提示（`LogNoListener` 或显式 Warn）。
- [ ] 派发/回调里没有重活、没有跨线程调用。
- [ ] 计时器时间域正确：UI 倒计时/超时用 `Unscaled`；`At(...)` 前有 `SyncServerTime`。
- [ ] 输入：用到的 action 都 `Bind` 过（未绑定会静默返回 false）；换场景 / 打开弹窗时 `ResetAll` 或 `OpenScope().Block()`。

## 6. 音频 / 状态机 / 动作序列 / GM / 数据表

- [ ] 音效：3D 播放传了 target；接了 `Failed` / `VoiceFinished`（框架自身不打日志）；逻辑名与路径不一致时先 `Register`。
- [ ] 状态机：状态实例注册复用（不是每次 new）；没有在 `OnEnter/OnExit` 里再切状态（防重入）；异步切换后检查了 `CurrentState`。
- [ ] 动作序列：蓝图静态缓存只 `Build` 一次；没有把状态存在构建期字段；占用资源在 `Finally` / `OnCancel` 归还。
- [ ] GM：注册集中在一处；命令名不含空白字符、不是只有分隔符；参数用 `args.Int/Float/Bool/Str/Enum` 取；业务拒绝抛 `RevGMUsageException`（不静默 return）。
- [ ] 配置表：走 `RevDataTableManager`（不直接读文件）；字符串取表用 `TableName`（不是类名）；失败分支处理了 `RevResLoadErrorReason`。

## 7. 热更（如果项目装了扩展包）

- [ ] `InitializeAsync` 放在"加载任何业务资源"之前；`UpdateAsync` 用 `CheckAsync` 的原结果。
- [ ] 资源包语义是**覆盖式**（持久化优先 → 回退 StreamingAssets），不是"只从持久化读"。
- [ ] 上传顺序：内容先、清单最后；清单路径 no-cache、内容路径 immutable。
- [ ] 远端目录带大版本段；`@appVersion` 与 `Application.version` 一致。
- [ ] 替换包时先 `ShutdownAll()` → 换文件 → `Init()`（不在包被使用时覆盖文件）。

## 8. 编辑器 / 真机差异

- [ ] 只用编辑器直读验证过的逻辑，是否在 AB 模式下也成立（必要时开 `UseABInEditor` 复验）。
- [ ] 平台相关代码没有硬编码 `PC`（编辑器平台的默认值）；热更/AB 目录段与 `ABBuildSetting.GetPlatformName` 对齐。
- [ ] 生成物（`RevResPath.cs` / `ResMap.txt` / `RevSoundPath.cs`）没有被手改。

## 9. 代码规范（本项目硬规范）

- [ ] 没有"以 `(` 结尾"的行（括号后换行）；没有 `( 参数` 的多余空格。
- [ ] 所有 `if/for/foreach/while` 都带大括号。
- [ ] 类名 `Rev` 前缀；文件名 = 类名；命名空间正确。
- [ ] 新文件有文件头"为什么"段；关键/易错处有 `★` 注释（解释为什么，不复述代码）。
- [ ] 新增资产带 `.meta`；文件夹 meta 是**同级**命名。
- [ ] 没有新增分层 / 第二套 API（`System` / `BusinessLogic` / 自己的资源加载入口）。
- [ ] 文档（md + html）与 README 统计数字已同步。

## 10. 对照示例（反例 → 正例）

### 例 1：事件订阅

```csharp
// ✗ 匿名 lambda：无法按委托移除；对象销毁后回调仍被调用
RevEvent.AddEventListener<int>("Hp.Change", v => hpBar.Set(v));

// ✓ 存字段 + owner，销毁时一行清掉
private Action<int> _onHpChange;
private void Awake() { _onHpChange = OnHpChange; RevEvent.AddEventListener("Hp.Change", _onHpChange, owner: this); }
private void OnDestroy() { RevEvent.RemoveAllByOwner(this); }
```

### 例 2：计时器

```csharp
// ✗ 没留"能停"的路 + 时间域选错（暂停游戏后倒计时冻结）
RevTimer.Every(1f, () => countdown.text = (--sec).ToString());

// ✓ owner 可整体取消 + UI 倒计时用 Unscaled
RevTimer.Every(1f, () => countdown.text = (--sec).ToString(), owner: this, domain: RevTimeDomain.Unscaled);
```

### 例 3：资源加载

```csharp
// ✗ 绕开资源系统：没缓存、没引用计数、真机路径不对、热更无处落脚
string path = Application.streamingAssetsPath + "/UI/icon.png";

// ✓ 逻辑路径 + 分组 + 异步
RevResManager.LoadAsync<Sprite>(RevResPath.Data, "UI_Icon", s => image.sprite = s, RevResGroup.UI);
```

### 例 4：失败可见

```csharp
// ✗ 静默返回：使用者永远不知道"为什么没反应"
if (cfg == null) { return; }

// ✓ 带原因：句柄字段 / 抛出人话异常 / GM 面板显示
if (cfg == null) { throw new RevGMUsageException("配置未加载：先执行 工具/重载配置"); }
```

### 例 5：热更语义

```csharp
// ✗ 只认持久化目录：新装机 / 清数据后全部 BundleLoadFail（首屏黑）
return Path.Combine(localRoot, bundleName);

// ✓ 覆盖式：持久化有就用、没有回退首包、都没有才交还框架默认
if (File.Exists(local)) { return local; }
return null;      // null = 让框架走 StreamingAssets/<平台>/<包名>
```
