// ============================================================
// RevUIDemoPanel.cs —— 演示《使用说明》第一章：3 分钟写出第一个面板 + 控件事件的三种接法
//
// 位置：Assets\Revolution.Demo\RevUISystem.Demo\
//
// 【这就是业务面板的全部样子】
//   · 类上 [RevUIPanel(资源目录, 层级)]：声明预制体在哪、挂哪一层（其余配置全有默认值）；
//   · [RevBind] 字段：按节点名自动拿控件（OnBindView 时已赋值）；
//   · OnBindView：只做"表现装配"（纪律：不写业务规则）；
//   · OnOpen / OnClose：每次打开 / 关闭的业务钩子。
//
// 【三种接法都在这一个面板上】同一个 btnAdd 按钮，三条路都挂着：
//   ① 方法特性        [RevButtonClick("btnAdd")] void OnBtnAddClick()
//   ② 按节点名分发     protected override void OnClick(string nodeName)   ← 这里用于 btnClose
//   ③ 绑字段 + 挂监听  [RevBind] Button _btnAdd → _btnAdd.onClick.AddListener(...)
//   ★ 两条路会同时走到（① 和 ③ 都挂着）—— 所以日志里一次点击会出现两条，这正是要演示的。
//   ★ 订阅纪律：**订阅放 OnOpen、退订放 OnClose**（成对出现）。
//     面板是"创建一次、打开很多次"的：只在 OnBindView 订阅一次、却在 OnClose 退订，
//     复用打开时监听就没了 —— 这是第六章第 ④ 个坑的同一类问题。
//
// 【层级用 Popup】框架按 Mask = RevUIMaskMode.Auto 自动推断"这一层带遮罩挡点击"。
//
// 【预制体从哪来】
//   Editor\RevUIDemoPrefabBuilder.cs 的"一键生成全部演示预制体"菜单（或场景入口的按钮 ①）。
//   ★ 生成到【资源根目录】下：Assets/GameRes/RevUIDemo/RevUIDemoPanel.prefab
//     因为编辑器直读/AB 都按「资源根目录 + 逻辑路径」找资源 —— 特性里写的 "RevUIDemo"
//     是相对 Assets/GameRes/ 的逻辑路径（不是完整的 Assets/... 路径）。
// ============================================================
using UnityEngine;
using UnityEngine.UI;

namespace Revolution.Demo.UI
{
    /// <summary>基础面板：Popup 层（自动带遮罩）+ 三种事件接法。</summary>
    [RevUIPanel("RevUIDemo", RevUILayer.Popup)]
    public sealed class RevUIDemoPanel : RevUIPanel
    {
        // ---------- [RevBind]：按节点名自动拿控件（路径默认 = 字段名去掉下划线） ----------
        [RevBind] private Text _txtTitle;          // 节点 txtTitle：标题
        [RevBind] private Button _btnAdd;          // 节点 btnAdd：加一条日志
        [RevBind] private Text _txtInfo;           // 节点 txtInfo：信息文本
        [RevBind] private Button _btnClose;        // 节点 btnClose：关闭面板

        private int _clicks;                        // 演示数据：按钮点击次数

        // ============================================================
        // 绑定（只一次）：拿控件、挂表现
        // ============================================================
        protected override void OnBindView()
        {
            _txtTitle.text = "① 基础面板（Popup 层 → 自动带遮罩）";
            Refresh();
        }

        // ============================================================
        // 每次打开：业务准备（接法 ③ 的订阅放这里，和 OnClose 的退订成对）
        // ============================================================
        protected override void OnOpen()
        {
            _clicks = 0;
            _btnAdd.onClick.AddListener(OnAddByListener);     // 接法③：自己挂 UGUI 监听
            RevUIDemoLog.Add("基础面板", "OnOpen：已订阅 btnAdd.onClick（接法③），并重置计数");
            Refresh();
        }

        // ============================================================
        // 接法② 按节点名集中分发（按钮多时用 switch）
        // ============================================================
        protected override void OnClick(string nodeName)
        {
            switch (nodeName)
            {
                case "btnClose":
                    CloseSelf();                     // 关自己：走框架关闭流程（关闭动画 → OnClose → 回池/销毁）
                    break;
            }
        }

        // ============================================================
        // 接法① 方法特性（最省事：不写字段、不重写回调）
        // ============================================================
        [RevButtonClick("btnAdd")]
        private void OnBtnAddClick()
        {
            _clicks++;
            RevUIDemoLog.Add("基础面板", $"接法① 方法特性：[RevButtonClick(\"btnAdd\")] 被调用（第 {_clicks} 次）");
            Refresh();
        }

        // ============================================================
        // 接法③ 自己挂的监听（要和 OnOpen 的订阅配对，见 OnClose）
        // ============================================================
        private void OnAddByListener()
        {
            RevUIDemoLog.Add("基础面板", "接法③ 绑字段 + AddListener：同一个按钮的监听也走到了");
        }

        // ============================================================
        // 关闭：停计时器 / 退订 / 释放资源 —— 面板会被复用，不清就串到下次打开（第六章坑④）
        // ============================================================
        protected override void OnClose()
        {
            _btnAdd.onClick.RemoveListener(OnAddByListener);
            RevUIDemoLog.Add("基础面板", "OnClose：已退订 btnAdd.onClick（下次打开不会重复触发）");
        }

        /// <summary>把点击次数刷到文本上（数据落屏只在本类内完成）。</summary>
        private void Refresh()
        {
            if (_txtInfo == null) return;

            _txtInfo.text =
                $"面板已打开。三个按钮都能试：\n" +
                $"· btnAdd  → 接法①（特性）+ 接法③（AddListener）都会触发，各记一条日志\n" +
                $"· btnClose → 接法②（OnClick 按节点名分发）\n" +
                $"\n点击次数：{_clicks}\n" +
                $"★ 本面板在 Popup 层：点面板外的遮罩会关掉它（RevUISetting.ClickMaskClosesTop）";
        }

        public override string ToString() => "RevUIDemoPanel（基础面板）";
    }
}
