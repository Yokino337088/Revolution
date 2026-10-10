# UI 系统 · 使用说明（手把手教程）

> 使用说明 · 从零到能写业务
>
> 这份文档只回答一个问题：**我该怎么用它？**（一个特性声明面板、一行打开、事件自动绑到方法）
> 读完你能做到：3 分钟写出第一个面板 · 分清 7 个生命周期回调谁先谁后 · 会给面板传数据 · 知道返回栈与层级怎么用
>
> ⚡ **想先看跑起来的效果**：`Assets/Revolution.Demo/RevUISystem.Demo/`（一个场景把本文每一章都演了一遍 ——
> 三种接法 / 生命周期 / 全部 API / 带数据 / 六层与遮罩 / 动画 / Part；左侧按章节点，右侧是共享日志）。

## 目录

- 〇、它是干什么的
- 一、3 分钟写出第一个面板
- 二、面板的生命周期（谁先谁后）
- 三、"我要做 X" 对照表（全部 API）
- 四、带数据的面板（一个面板多处复用）
- 五、层级 / 遮罩 / 返回栈
- 六、新手最容易踩的 6 个坑
- 七、相关文档

---


## 〇、它是干什么的

*先花 30 秒建立直觉，再动手写。*

> [!TIP]
> **只学 3 件事就能干活（真的）**
>
> // ① 声明一个面板：特性写"预制体在哪个目录、哪个名字"
> [RevUIPanel("UI/Bag", RevUILayer.Normal, "BagPanel")]
> public class BagPanel : RevUIPanel
> {
>     protected override void OnBindView() { }        // ② 唯一必须实现的方法：这里做绑定
>     protected override void OnClick(string nodeName) // ③ 点哪个按钮就走这里（不用手动挂监听）
>     {
>         if (nodeName == "BtnClose") RequestClose();
>     }
> }
> 
> // 打开 / 关闭（业务里就这两行）
> RevUI.Open<BagPanel>();
> RevUI.Close<BagPanel>();
> 带数据的、异步打开、返回栈、按组关闭……都在第三节的对照表里，**用到再查**。

> **人话** UI 系统 = 你只写"面板类 + 预制体名"，剩下的事（加载预制体、挂到哪个 Canvas、层级顺序、遮罩、关闭时回收还是销毁）它全包。
> 面板自己就是 MonoBehaviour，但**不要手动 Instantiate** —— 一律 `RevUI.Open`，否则层级、缓存、事件都接不上。

| 它替你解决的问题 | 怎么做的 |
|---|---|
| 面板预制体路径到处硬编码 | 写在特性里：`[RevUIPanel("UI/Bag", RevUILayer.Normal, "BagPanel")]`（目录, 层级, 预制体名），只有一处 |
| 按钮监听重复挂/忘记卸 | 节点名回调：`OnClick(nodeName)` / `OnToggleChanged` / `OnSliderChanged`… 由框架统一分发 |
| 打开顺序、层级盖住别人 | `RevUILayer` 四层（Scene / Normal / Popup / Toast）+ 同层按打开顺序 |
| 关了再开卡一下 | **面板池**：默认缓存复用，支持 `[RevUIPanel(..., CacheMode = …)]` 调策略 |
| 弹窗忘了加遮罩、点击穿透 | `Mask = RevUIMaskMode.Auto`：弹窗默认带遮罩挡住下面操作 |
| 返回键该关哪个面板 | `InBackStack` + `RevUI.Back()`：Toast 之类不进返回栈 |

---


## 一、3 分钟写出第一个面板

*准备：一个预制体放在资源根目录的 `UI/Bag/BagPanel.prefab`。*


```csharp
// ① 新建脚本 BagPanel.cs —— 就这么短，能跑
using UnityEngine;

[RevUIPanel("UI/Bag", RevUILayer.Normal, "BagPanel")]
public class BagPanel : RevUIPanel
{
    // RevUIPanel 是抽象类，这个方法必须实现 —— 做"拿组件、填初始内容"的事
    protected override void OnBindView()
    {
        // 例：拿到预制体里名为 Title 的文本并设置
        // (按你项目的绑定方式：RevBind 特性 / 自己 GetComponentInChildren)
    }

    protected override void OnOpen()
    {
        // 每次打开都会走这里：刷新数据、播开场动画
    }

    // 点了预制体里名为 BtnClose 的按钮（名字对得上就会进来）
    protected override void OnClick(string nodeName)
    {
        switch (nodeName)
        {
            case "BtnClose": RequestClose(); break;
            case "BtnSort":  SortBag();      break;
        }
    }
}

// ② 业务里打开它（哪都能调，不用挂物体）
RevUI.Open<BagPanel>();

// ③ 打开后拿到实例做点事
RevUI.Open<BagPanel>(panel => panel.RefreshView());

// ④ 关闭（面板内部也可以 RequestClose()）
RevUI.Close<BagPanel>();
```

> [!NOTE]
> **就这么点东西**
> 预制体放对目录 + 特性写对名字 = 能打开

---


### 控件事件的三种接法（挑一种就用，能混用）

同一个控件的事件，框架给三条路 —— **都可用、可混用**，按场景挑：

| 接法 | 长什么样 | 什么时候选它 |
|---|---|---|
| ① **方法特性**（最省事） | `[RevButtonClick("btnStart")] void OnStart() => StartGame();` | "点一下做一件事"：不写绑定字段、不重写回调，方法上标个特性就行 |
| ② **按节点名分发** | `protected override void OnClick(string nodeName) { … }` | 一个面板上按钮很多、想集中处理（`switch (nodeName)`） |
| ③ **绑字段 + 自己挂监听**（最灵活） | `[RevBind] Button _btn;` → `_btn.onClick.AddListener(...)` | 要用 UGUI 的其它回调（拖拽 / 悬停…）或接第三方控件 |

方法特性一共九个（标在面板 / Part 的**任意方法**上），覆盖全部控件事件：

```csharp
// 控件名 = 节点名（区分大小写）；写多级路径时只取最后一段（"Top/btnStart" 等价于 "btnStart"）
[RevButtonClick("btnStart")]       void OnStart()      => StartGame();      // 点击
[RevButtonLongPress("btnSkill")]   void OnSkillHold()  => ShowSkillTip();   // 长按（按住 ≥ 0.5s 后松开）
[RevButtonLoosen("btnMove")]       void OnMoveUp()     => StopMove();       // 松开（指针在控件上抬起）
[RevToggleChanged("tglSound")]     void OnSound(bool on)              => SetSound(on);
[RevSliderChanged("sldVolume")]    void OnVolume(float v)             => SetVolume(v);
[RevInputChanged("inpName")]       void OnName(string text)           => Preview(text);   // 单参数 = 文本
[RevInputEndEdit("inpName")]       void OnNameDone(string text)       => Submit(text);
[RevDropdownChanged("ddlQuality")] void OnQuality(int index)          => SetQuality(index);
[RevScrollChanged("scrollList")]   void OnScrolled(float x, float y)  => LoadMore(y);

// 同一个控件可以挂多个方法（都会被调用）；想同时要"节点名 + 值"就用两参数 / 三参数的形状
[RevButtonClick("btnBuy")]         void OnBuy(string nodeName)              => Buy(nodeName);
[RevToggleChanged("tglSound")]     void OnSound2(string nodeName, bool on)  => Log(nodeName, on);
```

| 事件 | 可用的参数形状（**只支持这些**，其它在装配时报错） |
|---|---|
| 点击 / 长按 / 松开 | `()` · `(string nodeName)` |
| Toggle 变化 | `()` · `(bool value)` · `(string nodeName, bool value)` |
| Slider 变化 | `()` · `(float value)` · `(string nodeName, float value)` |
| 输入框文本变化 / 结束编辑 | `()` · `(string text)` · `(string nodeName, string text)` |
| Dropdown 变化 | `()` · `(int index)` · `(string nodeName, int index)` |
| ScrollRect 滚动 | `()` · `(float x, float y)` · `(string nodeName, float x, float y)` |

> [!WARNING]
> **六条要记住的**
> · **长按的判定**：按住时长 ≥ `RevUISetting.ButtonLongPressSeconds`（默认 0.5 秒），**松开时**触发长按；同一次操作也会触发"松开"（不会和"点击"抢同一瞬间）。
> · **长按 / 松开只对 Button 生效**（UGUI 的 Button 本身不报这两个事件，框架给交互节点挂了一个小继电器）；自研控件用 `RevUI.RegisterAutoEvent<T>((d, c) => { … d.LongPress() … d.Loosen() })` 接进来。
> · **控件名写错会当场报错**：第一次装配就打"找不到名为 xxx 的节点"，不会静默成"点了没反应"。
> · 同一次事件里，"重写的 `OnToggleChanged(节点名, 值)`"这类回调和"方法特性"**两条路都会走到** —— 同一件事只放一处做。
> · **输入框那两个特性的单个 `string` 参数是"文本"**（不是节点名）—— 节点名靠特性声明，用不上；两个都要就写 `(string nodeName, string text)`。
> · **高频事件建议用重写回调**：特性走反射调用（每次触发一次小分配）。按钮 / Toggle / Dropdown 这类低频事件完全无所谓；**ScrollRect 滚动**与**输入框每次敲键**较频繁，若在意 GC 就重写 `OnScrollChanged` / `OnInputChanged`（直调、零分配）—— 两种写法可以并存。

## 二、面板的生命周期（谁先谁后）

*只要记住"创建一次、打开很多次"这一点，其余照着图用。*

OnInit → OnBindView → OnOpen → OnReuse（复用打开时） → OnRefreshView → OnCovered(true/false) → OnClose → OnRelease

| 回调 | 什么时候 | 该放什么 |
|---|---|---|
| `OnInit()` | 面板对象**第一次**创建时，一次 | 只做一次的初始化（缓存组件引用） |
| `OnBindView()` **（必须实现）** | 紧随 OnInit，一次 | 绑定 UI 元素、挂必要的内部结构 |
| `OnOpen()` | **每次**打开 | 刷新数据、播开场动画（最常用） |
| `OnReuse()` | 从池里**复用**打开时 | 想区分"全新"与"复用"时用（一般不需要） |
| `OnRefreshView()` | **每次打开时**自动调 · 面板**开着时** `SetData(...)` 自动调 · 你手动调 `RefreshView()` 时 | 只重刷显示内容（不动结构）。详见第四节"它到底什么时候被调用" |
| `OnCovered(bool)` | 上面盖了别人 / 别人关了 | 被盖住时停特效、暂停刷新（省性能） |
| `OnClose()` | 关闭时（对象还在池里） | 停协程/计时器、退订事件、**释放资源** |
| `OnRelease()` | 对象真正销毁时 | 清理非托管/静态引用（少见） |

**自定义开场 / 关闭动画**

*想让"打开动画播完再算打开完"，重写这两个方法即可*

protected override void
PlayOpenTransition(Action onDone)  {
/* 播动画，结束后 */
onDone?.Invoke(); }
protected override void
PlayCloseTransition(Action onDone) { onDone?.Invoke(); }
> 不重写就立即算完成（默认实现就是直接回调）。


---


## 三、"我要做 X" 对照表（全部 API）

*入口统一是 `RevUI`（动画入口是 `RevUIAnim`，见第五节"动画"一节）。所有泛型参数都要求 `T : RevUIPanel`。*

| 我想… | 这么写 |
|---|---|
| 打开（最常用） | `RevUI.Open<BagPanel>()` |
| 打开并拿到实例 | `RevUI.Open<BagPanel>(p => p.xxx)` |
| 打开并传数据 | `RevUI.Open<BagPanel, BagData>(data)` |
| 异步打开（可用 await） | `T p = await RevUI.OpenAsync<BagPanel>()` |
| 关闭 / 关闭某类型 | `RevUI.Close(panel)` · `RevUI.Close<BagPanel>()` |
| 关掉某一层 / 某一组 | `RevUI.CloseAll(RevUILayer.Popup)` · `RevUI.CloseGroup("Bag")` |
| 返回上一级（响应返回键） | `RevUI.Back()` |
| 全部关掉（切场景/回登录） | `RevUI.ShutdownAll()` |
| 判断是否开着 / 拿实例 | `RevUI.IsOpen<BagPanel>()` · `RevUI.Get<BagPanel>()` |
| 看某层最上面是谁 | `RevUI.TopOf(RevUILayer.Popup)` |
| 提前加载（避免首次打开卡） | `RevUI.Preload<BagPanel>()` |
| 调试：看当前所有面板 | `Debug.Log(RevUI.DumpStats())` |
| 给面板 / Part 加动效（一行） | `protected override RevUIAnimPreset ShowAnimation => RevUIAnimPreset.PopIn;` |
| 给任意控件加动效 | `RevUIAnim.PopIn(icon, owner: this)` · `RevUIAnim.SlideIn(this, RevUISlideDirection.Top)` |
| 跳过动画（直接到终态） | `RevUIAnim.ApplyEnd(panel, RevUIAnimPreset.PopIn)` |
| 停掉某个对象的动画 | `RevUIAnim.StopAllOf(this)` |
| 关掉全部动效（配置） | `RevUISetting.UIAnimationsEnabled = false` |
| 调试：看正在播的动画 | `Debug.Log(RevUIAnim.Dump())`（`RevUIAnim.ActiveCount` = 正在播几个） |

> [!WARNING]
> **关闭 ≠ 销毁**
> Close
> ShutdownAll()
> CacheMode
> 不要依赖 OnRelease 做常规清理
> OnClose

---


## 四、带数据的面板（一个面板多处复用）

*同一个界面既要显示"英雄详情"又要显示"道具详情"时，用泛型面板传数据。这一节专门讲清楚新手最容易混淆的一件事：**数据变化和界面刷新到底是什么关系**。*

### 4.1 基本写法（先照抄跑通）

```csharp
// ① 数据类：推荐用 class（纯 C#，别在里面碰 UnityEngine，方便单测）
public class DetailData
{
    public int Id;
    public string Title;
}

// ② 面板继承 RevUIPanel<TData>：三个钩子各管一件事
[RevUIPanel("UI/Common", RevUILayer.Normal, "DetailPanel")]
public class DetailPanel : RevUIPanel<DetailData>
{
    protected override void OnBindView() { }        // 装配：拿控件、挂交互（只做一次）

    protected override void OnRefreshView()          // 落屏：把 Data 画到界面上（★ 必须实现）
    {
        // ★ 这里直接用 Data 属性 —— 它一定已经是最新一次 SetData 进来的数据
        if (Data == null) { /* 面板复用回池后 Data 会被清掉，记得判空 */ return; }
        // _title.text = Data.Title; ...
    }

    protected override void OnDataChanged(DetailData oldData, DetailData newData)
    {
        // 增量刷新（可选）：数据被"换成"新对象时触发。
        // 想省性能就只更新变化的那几个控件，不整屏重画。
    }
}

// ③ 打开时传数据（面板还没开 → 先存着，打开时统一画）
RevUI.Open<DetailPanel, DetailData>(new DetailData { Id = 1001, Title = "亚瑟" });

// ④ 面板已经开着 → 换一份数据，界面自动重画（不用再调 RefreshView）
RevUI.Get<DetailPanel>()?.SetData(new DetailData { Id = 1002, Title = "妲己" });
```

### 4.2 核心概念：`Data` 是什么、什么时候有值

`RevUIPanel<TData>` 的 `Data` 属性 = **最新一次 `SetData(...)` 设置进来的强类型数据**。

调用 `SetData` 时框架内部按这个顺序走：

```
SetData(新数据)
  ├─ ① Data / DataObject 同步成新值          ← 先赋值
  ├─ ② OnDataChanged(旧数据, 新数据)          ← 此时 Data 已是新值
  └─ ③ 面板正开着？→ RefreshView() → OnRefreshView()   ← 此时 Data 已是新值
       （没开着就不画，等打开那次统一画，省一次重复刷新）
```

所以在 `OnRefreshView` / `OnDataChanged` 里读 `Data`，拿到的**永远是新数据**，不用担心拿到旧的。

> [!WARNING]
> **两个数据相关的坑**
> · **`SetData(null)` 会把 `Data` 清成 null** —— `OnRefreshView` 里用 `Data` 前要判空。
> · **面板回池再取出时 `Data` 也被清掉**（防"上次的数据还在"）—— 所以打开面板**没传数据**时 `OnRefreshView` 里 `Data` 是 null，必须判空兜底。

### 4.3 重点：`OnRefreshView()` 到底什么时候被调用

这是新手最容易混淆的地方，一张表说清：

| 场景 | 界面会自动刷新吗 | 你该做什么 |
|---|---|---|
| 打开面板（无论首次还是复用） | ✅ 会 | `OnRefreshView` 里写好"怎么画 Data"即可 |
| 面板开着时 `SetData(新对象)` | ✅ 会 | 什么都不用做，框架自动重画 |
| 对已开面板再 `RevUI.Open<T>(新数据)` | ✅ 会（内部走 `SetData`，顺带置顶） | 同上 |
| 面板还没开就 `SetData(...)`，之后才 `Open` | ✅ 会（打开时统一画一次） | 同上 |
| **原地改数据内容**：`Data.Id = 5;` / `Data.Items.Add(...)` | ❌ **不会** | **改完手动调 `RefreshView()`** |
| 面板关着，业务在别处改了数据 | ❌ 不会 | 下次打开时自然会画；急用就先 `SetData` 再开 |

一句话记忆：**框架不监听你的数据对象**（不是双向绑定）。"自动刷新"只发生在三个时机——**打开、SetData（可见时）、手动 RefreshView()**。数据对象内部字段变了，框架是不知道的。

```csharp
// 场景对比：同一个面板，两种"改数据"

// 写法 A：原地改内容 → 界面不会动，要手动刷
Data.Count++;
RefreshView();                    // ← 这行不能省

// 写法 B：换个新对象 → 自动刷，还能吃到 OnDataChanged 增量回调
SetData(new DetailData { Id = Data.Id, Count = Data.Count + 1 });
```

> [!TIP]
> **两种刷法怎么选**
> · 数据是一次性整体换掉（打开详情、切角色）→ 走 **SetData 换新对象**，自动刷、还能增量更新。
> · 数据是频繁的小改动（倒计时每秒 -1、金币 +1）→ 原地改 + **手动 RefreshView()**，或者干脆只刷新那一两个控件（不整屏重画，别在 `OnRefreshView` 里重建整个列表）。
>
> **为什么框架不做自动绑定**：双向绑定需要每个字段都包一层可观察对象（ObservableProperty），写起来啰嗦、性能开销也大。这里的取舍是"数据落屏"单向契约——`OnRefreshView` 只负责把 `Data` 画上去，改数据是你自己的事，改完说一声（`RefreshView()`）就行。逻辑一目了然，没有魔法。

### 4.4 `OnRefreshView` 与 `OnDataChanged` 的分工

| 钩子 | 触发时机 | 适合做什么 |
|---|---|---|
| `OnRefreshView()` | 打开时 + SetData（可见时）+ 手动 `RefreshView()` | **全量落屏**：按当前 `Data` 把界面画一遍（必写） |
| `OnDataChanged(old, new)` | 仅 `SetData` 换数据时 | **增量刷新**：只更新变化的那几个控件；`old`/`new` 由参数区分（若传的是同一个对象引用，两者相等） |

> [!NOTE]
> **新手最省心的起步方式**
> 一开始只实现 `OnRefreshView`（全量重画）就够了，功能完全正确；等界面复杂了、Profiler 显示整屏重刷太贵，再把变化频繁的那部分挪到 `OnDataChanged` 做增量。两条路不冲突。
>
> 另外注意纪律：`OnRefreshView` 里**只画界面**——不发请求、不改数据（发请求/改数据放 `OnOpen` 和按钮回调里）。在落屏回调里改数据再触发刷新，就是"刷新死循环"的来源。

### 4.5 `SetData` 在哪里调用？（调用权归属）

新手常见疑问："`SetData` 应该写在面板里吗？"——**不**。`SetData` 是给**业务侧**用的接口，永远写在"**数据产生 / 变化的地方**"：点按钮的地方、收到服务器数据的地方、算出新值的地方。面板从头到尾只做一件事——在 `OnRefreshView` 里把 `Data` 画出来（它是数据的**消费方**，不生产数据）。

| 谁在调 | 调什么 | 典型位置 |
|---|---|---|
| 业务代码：打开面板的那次交互 | `RevUI.Open<T, TData>(data)` | 点列表项打开详情（已开/没开不用区分，Open 一条路全包） |
| 业务代码：面板已开、想换内容 | `RevUI.Get<T>()?.SetData(新数据)` | 详情面板上点"下一个 / 上一个"切换目标，不重开面板 |
| 业务代码：数据异步到达 | 回调里 `panel.SetData(...)` | 网络回包、协程加载完成、定时器算出新值 |
| 面板内部 | 一般**不调** `SetData`；改了 class 数据内容后手动 `RefreshView()` | 面板只负责画，不负责"喂"数据 |
| 框架内部 | Open 流程自动 `SetData`、可见时自动 `RefreshView` | 你不用管 |

```csharp
// 场景 1：点列表项 → 打开详情（最常见，写"发起打开"的回调里）
private void OnItemClick(ItemConfig item)
{
    RevUI.Open<ItemDetailPanel, ItemData>(new ItemData { Id = item.Id, Title = item.Name });
}

// 场景 2：详情面板已开 → 点"下一个"换内容（不重开）
private void OnNextClick()
{
    RevUI.Get<ItemDetailPanel>()?.SetData(BuildData(_nextIndex));
}

// 场景 3：数据是异步来的 → 在回调里喂（面板没开就先 Open 顺手打开）
private void OnPlayerInfoReceived(PlayerInfo info)
{
    var data = new PlayerData { Name = info.name, Level = info.level };
    var panel = RevUI.Get<PlayerPanel>();
    if (panel != null) panel.SetData(data);          // 已开 → 换数据，自动重画
    else RevUI.Open<PlayerPanel, PlayerData>(data);  // 没开 → 存着等打开时画
}
```

> [!TIP]
> **一句话记忆**
> **`SetData` 永远写在"数据出生的地方"**（业务侧），**`RefreshView()` 只在面板内部、原地改了 class 数据内容之后才需要**；走 `SetData` 换数据的路，刷新是框架的事。

---


## 五、层级 / 遮罩 / 返回栈

*都在特性上声明，不用写代码。*

| 层级 | 放什么 | 特点 |
|---|---|---|
| `RevUILayer.Scene` | 主界面、大厅、全屏场景面板 | 最底层，会被普通界面盖住 |
| `RevUILayer.Normal` | 二级界面：背包、商店、设置 | 默认层级 |
| `RevUILayer.Popup` | 确认框、奖励结算 | **默认带遮罩**挡住下面的操作，进返回栈 |
| `RevUILayer.Toast` | 飘字、跑马灯 | 不挡操作、**不参与返回栈** |

**特性上还能配什么**

*一个面板的全部"外观策略"集中在这一行，不用散在代码里*

[RevUIPanel(
"UI/Common"
, RevUILayer.Popup,
"ConfirmDialog"
,
            CacheMode   = RevUICacheMode.Unspecified,
// 缓存策略（默认按框架规则）
Mask        = RevUIMaskMode.Auto,
// 遮罩：弹窗默认自动加
ExclusiveGroup =
"Dialog"
,
// 互斥组：同组只留一个
InBackStack =
true
)]
// 是否进返回栈（Back() 能关它）
> 这些字段都是可选的，不写就用默认值（默认值见 `RevUIPanelAttribute` 的字段声明）。


> [!NOTE]
> **返回键怎么接**
> RevUI.Back()
> false

---


### UI 根 Canvas 从哪来（默认：加载框架自带预制体）

> **架构建议：先使用单 Canvas。** 默认 `RevUISetting.CanvasArchitecture = RevUICanvasArchitecture.Single`，无需额外设置；即使面板里有动画、倒计时或滚动内容，只要在目标设备上满足帧预算，就继续使用单 Canvas。只有定位到 Canvas 合批确实成为瓶颈，且常规优化后仍不达标，才按下文步骤评估三 Canvas。

- **默认渲染模式是 `ScreenSpaceOverlay`** ✓（不需要相机、UI 永远最上层）—— 要改就设 `RevUISetting.CanvasMode`；
- 框架**默认加载** `Resources/RevUIPrefab/RevUICanvas.prefab` 来渲染 ✓（Overlay / 1920×1080 / match 0.5 / sortingOrder 100）；
- **载不到就代码兜底** ✓：预制体缺失或路径写错时，框架自己建 Canvas + CanvasScaler + GraphicRaycaster，并打一条 Warning 说明原因 —— 不会出现"整屏 UI 起不来"；
- 想改用**相机模式**（UI 可被 3D 遮挡 / 能进 RenderTexture）：

```csharp
RevUISetting.CanvasMode          = RevUICanvasMode.ScreenSpaceCamera;
RevUISetting.UICameraPrefabPath  = "RevUIPrefab/RevUICamera";   // 默认就是这个；也可直接 RevUISetting.UICamera = cam
RevUISetting.CanvasPlaneDistance = 100f;
```

| 你想… | 怎么设 |
|---|---|
| 用框架自带的 Canvas（默认） | 什么都不用做 |
| 用自己的 Canvas 预制体 | `RevUISetting.CanvasPrefabPath = "你的目录/你的Canvas"` |
| 完全用代码建（不依赖任何资源） | `RevUISetting.CanvasPrefabPath = string.Empty` |
| 在预制体里自己调渲染模式 | `RevUISetting.CanvasMode = RevUICanvasMode.Auto`（Auto = 跟随预制体） |
| UI 要被 3D 挡住 / 进 RenderTexture | 见上面那三行 |
| 六层挂点放哪 | 框架自己建（预制体里**不要**放六个层级节点） |

### 什么时候才切换到三 Canvas？

**优先坚持默认的单 Canvas**，不要仅因为有倒计时、动画或多个面板就切换。按以下顺序决策：

1. 在目标机型的典型场景中用 Profiler 测量 UI 合批（`Canvas.BuildBatch`）、重建 / 布局（`Canvas.SendWillRenderCanvases`）与总帧时间，记录帧率、Draw Call 和项目自己的帧预算。
2. 若超预算，先解决无意义的逐帧文本 / 布局更新、过多的射线检测、持续运行的动画以及长列表没有虚拟化等问题，按**同一测试条件**复测。若达到目标，继续使用单 Canvas。
3. 只有确定 **Canvas 合批仍是主要瓶颈**，且单 Canvas 优化后仍不达标，才在**第一次打开面板之前**启用三 Canvas：

```csharp
RevUISetting.CanvasArchitecture = RevUICanvasArchitecture.Split;

[RevUIPanel("UI/Main", RevUILayer.Scene, CanvasType = RevUICanvasType.Static)]
public sealed class MainBackgroundPanel : RevUIPanel
{
    protected override void OnBindView() { }
}

[RevUIPanel("UI/Main", RevUILayer.Scene, CanvasType = RevUICanvasType.Dynamic)]
public sealed class MainHudPanel : RevUIPanel
{
    protected override void OnBindView() { }
}
```

`Static` 放常驻且基本不变的 Scene 内容，`Dynamic` 放常驻且频繁变化的 Scene 内容；`Common`（默认）放其余所有面板。现有混合静态/动态内容的预制体若要分到两个画布，需要拆成两个 Scene 面板，**只配置一个属性不会自动把面板内部控件分离**。`Normal` / `Popup` / `Toast` / `Guide` / `Top` 声明成 `Static` 或 `Dynamic` 会被放回 `Common` 并告警，以保证弹窗和引导遮罩位于最上层。切换后继续测 CPU 合批和 Draw Call；没有改善就退回单 Canvas。详见[《架构解析》第五章 · 决策 11](UI系统架构解析.md)。

### 面板 / Part / 控件的动画（一行加动效，不依赖 DOTween）

框架自带一套 **UI 专用**的轻量动画库（代码在 `Runtime\RevUISystem\Animation\`，入口 `RevUIAnim`）：零第三方依赖 ✓、每帧零 GC ✓、掉帧不改变动画总时长 ✓（采样模型 + 帧余量结转）。

**面板：一行预设属性**（显示动画播完才算"打开完成"✓，隐藏动画播完才真正回收 ✓）：

```csharp
[RevUIPanel("UI/Panel")]
public sealed class BagPanel : RevUIPanel<BagData>
{
    protected override RevUIAnimPreset ShowAnimation => RevUIAnimPreset.PopIn;    // 打开时自动播
    protected override RevUIAnimPreset HideAnimation => RevUIAnimPreset.PopOut;   // 关闭时自动播（播完才关）

    // 想自己掌控节奏（与预设并存；★ 结束时必须调 onDone）
    protected override void PlayOpenTransition(Action onDone)
        => RevUIAnim.SlideIn(this, RevUISlideDirection.Top, 0.25f, onDone, owner: this);
}
```

**Part** 同理（重写 `ShowAnimation` ✓）。**任意控件**直接用门面（面板自己 / Image / Text / Button / RectTransform / CanvasGroup 都能传 ✓）：

```csharp
RevUIAnim.FadeIn(icon);                                        // 淡入
RevUIAnim.SlideIn(this, RevUISlideDirection.Top);              // 从上滑入
RevUIAnim.ScaleTo(icon, 1.2f, 0.12f);                          // 缩放到 1.2 倍（相对基准缩放）
RevUIAnim.FadeTo(icon, 0.5f);                                  // 淡到半透明
RevUIAnim.Breathe(tipIcon);                                    // 呼吸（透明度 0.45 ↔ 1 来回）
RevUIAnim.Breathe(tipIcon, useScale: true);                    // 换成缩放呼吸
RevUIAnim.AddHoverFeedback(btnClose);                          // 按钮：悬停放大 + 按下缩小
RevUIAnim.Play(icon, RevUIAnimPreset.PopIn, 0.3f, () => Tip("播完"), owner: this);
RevUIAnim.ApplyEnd(panel, RevUIAnimPreset.PopIn);              // 跳过动画：直接落到终态
RevUIAnim.StopAllOf(this);                                     // 停掉这个对象的全部动画
```

| 预设（共 14 个） | 效果 |
|---|---|
| `FadeIn` / `FadeOut` | 淡入 / 淡出（默认 0.18s） |
| `PopIn` / `PopOut` | 淡入 + 缩放回弹 / 淡出 + 缩小（默认 0.25s，**面板默认手感**） |
| `ScaleIn` / `ScaleOut` | 只做缩放（0.9 ↔ 1） |
| `SlideInFromTop` / `Bottom` / `Left` / `Right` | 从四个方向滑入（默认 0.28s） |
| `SlideOutToTop` / `Bottom` / `Left` / `Right` | 往四个方向滑出 |
| `None` | 不做动画（默认值；行为与没有动画库时完全一致） |

**门面 API 一览**（真实签名，可直接抄）：

| 我想… | 这么写 |
|---|---|
| 按预设播 | `Play(控件, 预设, 时长 = 0, 完成回调 = null, owner = null)` → 返回 `RevUIAnimHandle` |
| 淡入 / 淡出 / 弹入 / 弹出 / 缩放进出 | `FadeIn` · `FadeOut` · `PopIn` · `PopOut` · `ScaleIn` · `ScaleOut`（控件, 时长 = 0, 完成回调 = null, owner = null） |
| 四方向滑入 / 滑出 | `SlideIn(控件, RevUISlideDirection.Top, 时长 = 0, …)` · `SlideOut(控件, 方向, …)` |
| 到某个目标值 | `ScaleTo(控件, 1.2f, 时长 = 0.12f, ease = RevUIEase.CubicOut, …)` · `FadeTo(控件, 0.5f, …)` |
| 呼吸 / 闪烁（无限往返） | `Breathe(控件, 时长 = 0.6f, from = 0.45f, to = 1f, useScale = false)` |
| 控件反馈（悬停 / 按下） | `AddHoverFeedback(控件, hoverScale = 1.06f, pressScale = 0.94f, 时长 = 0.09f)` |
| 立即到终态（跳过动画） | `ApplyEnd(控件, 预设)` |
| 停止 / 查询 / 复位 | `Stop(句柄)` · `StopAllOf(owner)` → 返回停掉几个 · `StopAll()` · `IsPlaying(控件)` · `RestoreBase(控件)` |
| 诊断 | `RevUIAnim.ActiveCount`（正在播几个）· `Debug.Log(RevUIAnim.Dump())` |

> [!WARNING]
> **六条要记住的**
> · **时长不传（或 ≤ 0）= 用该预设的默认时长** —— 想改节奏就显式传，例如 `RevUIAnim.PopIn(panel, 0.35f)`。
> · **一定要传 `owner`**（面板 / Part 传自己）—— 关闭或销毁时框架会 `StopAllOf(owner)` 一行清干净；不传就可能留在池化过的面板上继续算。
> · **同一个控件上只留一个动画**：再起一个会自动顶掉上一个（不会两个动画抢同一个属性）；`Play` 返回的句柄可用 `.IsValid` 查它是否还在播。
> · **动画走 `unscaledDeltaTime`**：暂停（`timeScale = 0`）时 UI 动画照常播；全局倍速改 `RevUIAnim.GlobalSpeed` 一个数。
> · **要关动效**：`RevUISetting.UIAnimationsEnabled = false` —— 所有预设直接写终态，业务代码一行都不用改；只想跳过单个面板用 `RevUIAnim.ApplyEnd(控件, 预设)`。
> · **透明度写 `CanvasGroup`**（没有会自动补一个），缩放 / 位移写 `RectTransform`；要恢复基准态用 `RevUIAnim.RestoreBase(控件)`。

## 六、新手最容易踩的 6 个坑

| 坑 | 正确做法 |
|---|---|
| ① 自己 `Instantiate` 面板预制体 | 一律 `RevUI.Open` —— 否则层级/缓存/事件分发都失效 |
| ② 忘了实现 `OnBindView()` | 它是抽象方法，编译期就会拦你；里面做绑定，不要做业务 |
| ③ 在 `OnBindView` 里开计时器/协程 | 那是一次性的；每次打开要做的事写 `OnOpen` |
| ④ 关闭时不停计时器、不退订事件 | 面板会被**缓存复用**，早期计时器会在下次打开时乱跑 → 全放 `OnClose` 里清 |
| ⑤ 用 `CloseAll()` 当"关一个"用 | 要关单个用 `Close<T>()`；切场景才用 `ShutdownAll()` |
| ⑥ 打不开却没有任何提示 | 检查三处：预制体是否在资源根目录的对应路径下 · 特性里目录/名字是否写对 · 「打包」页签是否已生成映射 |

### 进阶：另外 7 条（多数和"复用 / 生命周期"有关）

| 坑 | 正确做法 |
|---|---|
| ⑦ 事件没用 `owner: this` 注册 | 关闭面板时框架会 `RevEvent.RemoveAllByOwner(this)`，但它只摘"登记在你名下"的；用别的 owner 注册的监听（例如挂在某个长期服务上）框架摘不掉，那类监听要自己按生命周期管 |
| ⑧ 在 `OnRefreshView` 里发请求 / 改数据 | 那就不是"落屏"而是逻辑了，会出现"刷新一次发一次请求"的死循环 → 请求放 `OnOpen` / 交互回调里 |
| ⑨ 面板里直接 `Destroy(gameObject)` | 绕过管理器会让索引、池、资源引用对不上 → 关自己请用 `CloseSelf()` |
| ⑩ 把飘字放在参与返回栈的层 | `Toast` 层不参与 `Back()`；自定义面板若不想被返回键关掉，设 `InBackStack = false` |
| ⑪ 遮罩把不该挡的挡住了 | 想"看一眼但不打断操作"的浮层，显式写 `Mask = RevUIMaskMode.None`（`Popup` / `Guide` / `Top` 层默认是挡的） |
| ⑫ 池里实例占内存 | `KeepAlive` 的界面会一直留着一份实例（连同它端的预制体引用）→ 大界面用 `CacheMode = DestroyOnClose`，或把 `MaxCachedPanels` 调小 |
| ⑬ 改了数据界面没反应 | 框架**不监听**数据对象内容：原地改（`Data.Count++`）后要手动 `RefreshView()`；换新对象走 `SetData(...)` 才会自动刷 → 详见 4.3 的场景对照表 |

> [!NOTE]
> **最贵的一课：面板会复用**
> 从池里拿、关掉放回
> OnOpen
> OnClose

---


## 七、相关文档

- [《UI 系统 · 架构解析》](UI系统架构解析.md) —— 设计论证：面板池策略、绑定机制、层级与遮罩的实现、为什么会复用
- [《资源加载系统 · 使用说明》](../资源加载系统/资源加载系统使用说明.md) —— 面板里加载图标的正确姿势（记得 Release）
- [《公共 Mono 模块 · 使用说明》](../公共Mono模块/公共Mono模块使用说明.md) —— 面板里要每帧跑的逻辑怎么写
- [文档总入口](../index.html) · [在线文档站](https://yokino337088.github.io/Revolution/)

---

在线文档站
