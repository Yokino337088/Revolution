// ============================================================
// RevUIDemoDataPanel.cs —— 演示《使用说明》第四章：带数据的面板（一个面板多处复用）
//
// 位置：Assets\Revolution.Demo\RevUISystem.Demo\Panels\
//
// 【这个演示想让你看到什么】
//   同一个界面既要显示"英雄详情"又要显示"道具详情"时，不要写两个面板，写一个泛型面板：
//
//   · 打开时传数据：RevUI.Open<RevUIDemoDataPanel, RevUIDemoDetail>(data)
//     → 第一次会走 OnBindView → OnOpen → OnDataChanged → OnRefreshView
//   · 面板已经开着时再传一份新数据（**不会重开**）：
//     RevUI.Get<RevUIDemoDataPanel>()?.SetData(data)
//     → 只走 OnDataChanged → OnRefreshView（看日志：OnOpen 只出现一次）
//
//   ★ 界面只管显示 Data，数据从外面进来 —— 这就是"一个面板多处复用"。
// ============================================================
using UnityEngine;
using UnityEngine.UI;

namespace Revolution.Demo.UI
{
    /// <summary>面板要显示的数据（class / struct 都行，这里用结构体演示"值语义"）。</summary>
    public struct RevUIDemoDetail
    {
        public int Id;
        public string Title;
        public string Body;
        public Color Accent;        // 换个主色，一眼看出"数据真的换了"
    }

    /// <summary>泛型面板：数据从 <c>RevUI.Open&lt;T, TData&gt;(data)</c> 或 <c>SetData</c> 进来。</summary>
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
        /// <summary>数据换了：刷新界面（业务最常写的地方）。</summary>
        protected override void OnDataChanged(RevUIDemoDetail oldData, RevUIDemoDetail newData)
        {
            _dataTimes++;
            RevUIDemoLog.Add("数据面板",
                _dataTimes == 1
                    ? $"OnDataChanged（首次数据：{newData.Title}）"
                    : $"OnDataChanged（{oldData.Title} → {newData.Title}，没有重开面板）");

            RefreshView();                       // 数据换了就刷显示（也可以等 OnRefreshView 统一刷）
        }

        /// <summary>只重刷显示内容、不动结构：SetData / Open 之后框架都会调它。</summary>
        protected override void OnRefreshView()
        {
            RevUIDemoDetail d = Data;            // 泛型基类给的强类型数据（不必拆箱）

            _txtTitle.text = $"④ {d.Title}（Id = {d.Id}）";
            _txtBody.text = d.Body;
            _imgBadge.color = d.Accent;

            RevUIDemoLog.Add("数据面板", $"OnRefreshView（显示「{d.Title}」）");
        }

        protected override void OnOpen()
        {
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
