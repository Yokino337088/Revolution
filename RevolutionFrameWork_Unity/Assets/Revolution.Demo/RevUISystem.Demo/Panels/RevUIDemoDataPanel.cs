// ============================================================
// RevUIDemoDataPanel.cs —— 演示《使用说明》第四章：带数据的面板（一个面板多处复用）
//
// 位置：Assets\Revolution.Demo\RevUISystem.Demo\Panels\
//
// ════════════════════════════════════════════════════════════
// 【先建立总观念：数据是怎么"流"进界面的】
//
//   数据永远从外面进来，界面只负责把它画出来（单向，框架不反向监听你的数据）：
//
//       业务代码                        面板内部                        界面变化
//       ─────────                      ─────────                      ────────
//   RevUI.Open<T, TData>(data)   ──→  Data = data                   （面板还没开，先记下不画）
//                                     → 等打开时统一画              → OnOpen → OnRefreshView
//   RevUI.Open<T, TData>(data)   ──→  SetData(data)（面板已开时）    → OnDataChanged → OnRefreshView
//     （面板已开，等价于 SetData）       → Data 换成新值                （OnOpen 不会再来一遍！）
//   panel.SetData(newData)       ──→  同上一条                       → 同上一条
//   原地改内容（class 数据）：                                            ❌ 界面不会动！
//     Data.Level++;                                                      → 要手动调 RefreshView()
// ════════════════════════════════════════════════════════════
//
// 【新手最关心的三个问题，这里一次讲清】
//
//  ① "数据怎么改变？"
//     改变的不是面板，而是你传给它的数据对象。有两条路：
//     · 换新对象：SetData(new ...) —— 框架会通知你（OnDataChanged）并自动重画（开着时）
//     · 原地改字段：Data.XXX = ... —— 框架不知道你改了，必须手动调 RefreshView()
//       ★ 本 Demo 的数据是 struct（值语义）：每次读 Data 都拿到一份拷贝，
//         "原地改"这条路根本走不通，只能换新对象 —— 详见下面 struct 的注释。
//
//  ② "数据变了之后要调用哪些接口？"
//     · 走 SetData 换新对象 → 什么都不用调，框架自动帮你刷（看日志：OnRefreshView 自己出现）
//     · 原地改了内容        → 手动调一次 RefreshView()（public 方法，随时可调）
//
//  ③ "OnDataChanged 和 OnRefreshView 分工是什么？"
//     · OnDataChanged(old, new)：SetData 换数据那一刻触发 —— 适合"增量刷新"（只改变化的控件）
//     · OnRefreshView()：打开时 / SetData 之后（可见时）/ 手动 RefreshView() 时触发
//       —— 适合"全量落屏"（按当前 Data 把整个界面画一遍），新手先只用它就够了
//
// 【"SetData 应该写在哪里？"——调用权归属（新手必看）】
//   SetData 是给【业务侧】用的接口，永远写在"数据产生 / 变化的地方"，不是写在面板里：
//
//   · 打开面板的那次交互（列表项点击 / 按钮回调）→ RevUI.Open<T, TData>(data)
//     （已开/没开不用自己区分：没开就存着等打开时画，已开就自动 SetData + 重画）
//   · 面板已开、想换内容（详情页点"下一个"）→ RevUI.Get<T>()?.SetData(新数据)
//   · 数据异步到达（网络回包 / 协程加载完成）→ 在回调里 panel.SetData(...)，
//     面板没开就 RevUI.Open<T, TData>(data) 顺手打开
//
//   面板内部一般不调 SetData —— 面板是数据的"消费方"，只负责画；
//   只有原地改了 class 数据内容之后，才在面板内部手动调 RefreshView()。
//   一句话：SetData 写在业务侧"数据出生的地方"，RefreshView() 留在面板里兜底。
//
// 【动手试】场景左侧"四"这一节：
//   · 点「打开（传数据：英雄详情）」→ 看右侧日志：OnBindView → OnOpen → OnDataChanged → OnRefreshView
//   · 再点「再传一份数据（SetData）」→ 只多出 OnDataChanged → OnRefreshView（OnOpen 没有出现）
// ════════════════════════════════════════════════════════════
using UnityEngine;
using UnityEngine.UI;

namespace Revolution.Demo.UI
{
    /// <summary>
    /// 面板要显示的数据（class / struct 都行，这里故意用 struct 演示"值语义"）。
    ///
    /// 【struct 和 class 在这里差别很大，新手必看】
    /// · struct（本例）：值语义 —— 基类的 <c>Data</c> 属性每次读都返回一份<b>拷贝</b>。
    ///   所以你在别处拿到的 Data 改了也不影响面板存的那份，"原地改 + RefreshView()"这条路走不通，
    ///   只能构造新对象走 SetData —— 好处是数据绝不会被外部偷偷改掉。
    /// · class：引用语义 —— Data 和外面持有的是同一个对象。
    ///   可以原地改，但改完<b>必须手动 RefreshView()</b>（框架不监听内容变化），
    ///   而且 OnDataChanged 的 old/new 若传的是同一个引用，两者相等，比较不出差异。
    /// · 结论：展示型小数据用 struct 最省心；字段多、要频繁局部更新的用 class + SetData。
    /// </summary>
    public struct RevUIDemoDetail
    {
        public int Id;
        public string Title;
        public string Body;
        public Color Accent;        // 换个主色，一眼看出"数据真的换了"
    }

    /// <summary>
    /// 泛型面板：数据从 <c>RevUI.Open&lt;T, TData&gt;(data)</c> 或 <c>SetData</c> 进来。
    ///
    /// 【三个钩子各管一件事（对照源码 RevUIPanel.Generic.cs）】
    ///   OnBindView()        —— 装配：拿控件、挂交互（只做一次）
    ///   OnRefreshView()     —— 落屏：把 Data 画到界面上（泛型面板里是必须实现的抽象方法）
    ///   OnDataChanged(o, n) —— 增量：数据被"换成"新对象那一刻的通知（可选）
    ///
    /// 【SetData 一次的完整内部顺序（记住这个就再也不会懵）】
    ///   SetData(新数据)
    ///     ├─ ① Data / DataObject 同步成新值          ← 先赋值
    ///     ├─ ② OnDataChanged(旧数据, 新数据)          ← 此时 Data 已是新值
    ///     └─ ③ 面板正开着？→ 自动 RefreshView()       ← 此时 Data 已是新值
    ///          （没开着就不画，等 OnOpen 那次统一画，省一次重复刷新）
    /// </summary>
    [RevUIPanel("RevUIDemo", RevUILayer.Normal, Mask = RevUIMaskMode.None)]
    public sealed class RevUIDemoDataPanel : RevUIPanel<RevUIDemoDetail>
    {
        [RevBind] private Text _txtTitle;
        [RevBind] private Text _txtBody;
        [RevBind] private Image _imgBadge;
        [RevBind] private Button _btnClose;

        private int _dataTimes;

        // ============================================================
        // 绑定（只一次）
        // ============================================================
        protected override void OnBindView()
        {
            _txtTitle.text = "④ 带数据的面板（一个面板多处复用）";
        }

        // ============================================================
        // 数据
        // ============================================================
        /// <summary>
        /// 数据换了：SetData / 打开传参那一刻触发（old = 上一次的数据，new = 这一次的）。
        ///
        /// 【新手起步建议】这个方法可以先不重写 —— 只实现 OnRefreshView 全量重画，功能完全正确；
        /// 等界面复杂了（长列表、重内容），再把"只跟某几个字段有关的更新"挪到这里做增量，省性能。
        ///
        /// ★ 本方法里<b>不需要</b>手动调 RefreshView()：
        ///   面板开着时，SetData 之后框架会自动再调一次 OnRefreshView（见类注释的顺序③）；
        ///   面板没开时，马上画也没意义，等 OnOpen 那次统一画。
        ///   （旧版 Demo 在这里手动刷了一次，结果每次换数据界面画两遍 —— 别学它。）
        ///
        /// 注意：本 Demo 的数据是 struct（值语义），old/new 天然是两份独立拷贝，逐字段比较
        /// 就能知道"哪里变了"；如果换用 class 且 SetData 传了同一个引用，old == new，比较无意义。
        /// </summary>
        protected override void OnDataChanged(RevUIDemoDetail oldData, RevUIDemoDetail newData)
        {
            _dataTimes++;
            RevUIDemoLog.Add("数据面板",
                _dataTimes == 1
                    ? $"OnDataChanged（首次数据：{newData.Title}）"
                    : $"OnDataChanged（{oldData.Title} → {newData.Title}，没有重开面板）");
        }

        /// <summary>
        /// 落屏：把当前 <see cref="Data"/> 画到界面上（泛型面板必须实现）。
        ///
        /// 【它什么时候被调用】三个时机，缺一不可都想到：
        ///   ① 每次打开时（首次 / 从池里复用，都会画一次）
        ///   ② 面板开着时 SetData(...) 之后（框架自动调，不用你动手）
        ///   ③ 你手动调 RefreshView() 时（原地改了 class 数据内容后的补救办法）
        ///
        /// 【这里拿到的 Data 一定是新值】：SetData 内部是"先赋值、再通知、最后重画"，
        /// 轮到本方法执行时 Data 已经换好了，直接用，不要自己缓存旧数据。
        ///
        /// 【纪律】这里只画界面 —— 不发请求、不改数据（那些放 OnOpen / 按钮回调里），
        /// 否则"刷新 → 改数据 → 又触发刷新"就是死循环。
        /// </summary>
        protected override void OnRefreshView()
        {
            RevUIDemoDetail d = Data;            // 泛型基类给的强类型数据（struct：这里是值拷贝）

            _txtTitle.text = $"④ {d.Title}（Id = {d.Id}）";
            _txtBody.text = d.Body;
            _imgBadge.color = d.Accent;

            RevUIDemoLog.Add("数据面板", $"OnRefreshView（显示「{d.Title}」）");
        }

        protected override void OnOpen()
        {
            // 每次打开都走这里，但换数据（SetData）不会走这里 —— 这就是"复用"的意义：
            // 面板对象不重建，只有数据换了。看右侧日志对比 OnOpen 和 OnDataChanged 出现的次数。
            RevUIDemoLog.Add("数据面板", "OnOpen（每次打开一次；再 SetData 不会走这里）");
        }

        // ============================================================
        // 交互
        // ============================================================
        protected override void OnClick(string nodeName)
        {
            if (nodeName == "btnClose") CloseSelf();
        }
    }
}
