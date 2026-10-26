// ============================================================
// RevUIDemoLayerPanels.cs —— 演示《使用说明》第五章：层级 / 遮罩 / 返回栈
//
// 位置：Assets\Revolution.Demo\RevUISystem.Demo\Panels\
//
// 【一个面板的"外观策略"全写在特性那一行，不用散在代码里】
//   [RevUIPanel(目录, 层级, "预制体名", CacheMode = …, Mask = …, ExclusiveGroup = …, InBackStack = …)]
//
//   层级（从下往上）：Scene → Normal → Popup → Toast → Guide → Top
//     · Scene  ：主界面 / 大厅（最底层，会被普通界面盖住）
//     · Normal ：二级界面（默认层）
//     · Popup  ：确认框、结算（**默认带遮罩**挡住下面操作，进返回栈）
//     · Toast  ：飘字、跑马灯（不挡操作、**不进返回栈**）
//     · Guide  ：新手引导（带遮罩）
//     · Top    ：最顶层（带遮罩）
//
//   遮罩：Mask = Auto（默认）→ 规则是"Popup / Guide / Top 自动加，其余不加"；
//         想反着来就显式写 Mask = RevUIMaskMode.None / ClickBlock。
//         点击遮罩是否关掉最上层 = RevUISetting.ClickMaskClosesTop（默认 true）。
//   返回栈：InBackStack = false 的面板，RevUI.Back() 不会关它（Toast 层被框架硬排除）。
//   互斥组：ExclusiveGroup 相同的面板，开新的会自动关掉旧的那个（同一时刻只留一个）。
//
// 【怎么看】从场景入口按顺序点：Scene → Normal → Popup → Toast → Guide → Top，
//          每个都带一句"我是谁、我会不会盖住别人、Back 关不关我"。
// ============================================================
using UnityEngine;
using UnityEngine.UI;

namespace Revolution.Demo.UI
{
    /// <summary>层级演示面板的共同实现（每个具体层级只是换一行特性）。</summary>
    public abstract class RevUIDemoLayerPanel : RevUIPanel
    {
        [RevBind] private Image _bg;
        [RevBind] private Text _txtTitle;
        [RevBind] private Text _txtInfo;
        [RevBind] private Button _btnClose;

        /// <summary>层显示名（子类给）。</summary>
        protected abstract string LayerLabel { get; }

        /// <summary>这一层的主题色（一眼分辨谁盖谁）。</summary>
        protected abstract Color Theme { get; }

        /// <summary>面板上那句"人话"说明（子类给）。</summary>
        protected abstract string Explain { get; }

        private int _coveredTimes;

        protected override void OnBindView()
        {
            _txtTitle.text = "⑤ " + LayerLabel;
            _bg.color = Theme;
            RefreshInfo();
        }

        protected override void OnOpen()
        {
            RevUIDemoLog.Add(LayerLabel, "打开（" + Position() + "）");
            RefreshInfo();
        }

        protected override void OnCovered(bool covered)
        {
            if (covered) _coveredTimes++;
            RevUIDemoLog.Add(LayerLabel, covered ? "OnCovered(true)：被上面的面板盖住了" : "OnCovered(false)：上面的关了，恢复");
            RefreshInfo();
        }

        protected override void OnClose()
        {
            RevUIDemoLog.Add(LayerLabel, "关闭");
        }

        protected override void OnClick(string nodeName)
        {
            if (nodeName == "btnClose") CloseSelf();
        }

        private void RefreshInfo()
        {
            if (_txtInfo == null) return;

            _txtInfo.text =
                Explain + "\n\n" +
                "· 层级：" + LayerLabel + "（" + Position() + "）\n" +
                "· 遮罩：" + (Meta != null ? Meta.MaskResolved.ToString() : "?") + "（Mask = Auto 时按层级自动推断）\n" +
                "· 返回栈：" + (Meta != null && Meta.InBackStack ? "参与（RevUI.Back() 能关我）" : "**不参与**（Back() 跳过）") + "\n" +
                "· 当前状态：" + State + (IsCovered ? "（被盖住）" : "") + "，被盖住过 " + _coveredTimes + " 次";
        }

        private string Position()
        {
            switch (Layer)
            {
                case RevUILayer.Scene: return "最底层，会被普通界面盖住";
                case RevUILayer.Normal: return "默认层";
                case RevUILayer.Popup: return "弹窗层（默认带遮罩）";
                case RevUILayer.Toast: return "提示层（不挡操作、不进返回栈）";
                case RevUILayer.Guide: return "引导层（默认带遮罩）";
                default: return "最顶层（默认带遮罩）";
            }
        }
    }

    // ============================================================
    // 六层各一个（特性那一行就是全部的差别）
    // ============================================================

    /// <summary>Scene：主界面 / 大厅。</summary>
    [RevUIPanel("RevUIDemo", RevUILayer.Scene, "RevUIDemoScenePanel")]
    public sealed class RevUIDemoScenePanel : RevUIDemoLayerPanel
    {
        protected override string LayerLabel => "Scene 层";
        protected override Color Theme => new Color(0.16f, 0.22f, 0.32f, 0.97f);
        protected override string Explain => "主界面 / 大厅放这一层：最底层，二级界面一开就把它盖住。";
    }

    /// <summary>Normal：二级界面（默认层）。</summary>
    [RevUIPanel("RevUIDemo", RevUILayer.Normal, "RevUIDemoNormalPanel")]
    public sealed class RevUIDemoNormalPanel : RevUIDemoLayerPanel
    {
        protected override string LayerLabel => "Normal 层";
        protected override Color Theme => new Color(0.18f, 0.26f, 0.38f, 0.97f);
        protected override string Explain => "背包 / 商店 / 设置放这一层：不带遮罩（下面的还能点），参与返回栈。";
    }

    /// <summary>Popup：弹窗（默认带遮罩）。</summary>
    [RevUIPanel("RevUIDemo", RevUILayer.Popup, "RevUIDemoPopupPanel")]
    public sealed class RevUIDemoPopupPanel : RevUIDemoLayerPanel
    {
        protected override string LayerLabel => "Popup 层";
        protected override Color Theme => new Color(0.30f, 0.20f, 0.36f, 0.97f);
        protected override string Explain => "确认框 / 结算放这一层：Mask = Auto 自动带遮罩挡住下面操作；点遮罩会关掉最上面这个（RevUISetting.ClickMaskClosesTop）。";
    }

    /// <summary>Toast：飘字（不挡操作、不进返回栈）。</summary>
    [RevUIPanel("RevUIDemo", RevUILayer.Toast, "RevUIDemoToastPanel")]
    public sealed class RevUIDemoToastPanel : RevUIDemoLayerPanel
    {
        protected override string LayerLabel => "Toast 层";
        protected override Color Theme => new Color(0.20f, 0.34f, 0.26f, 0.97f);
        protected override string Explain => "飘字 / 跑马灯放这一层：不加遮罩（下面照常点）、不进返回栈（Back() 永远不会关它）。";
    }

    /// <summary>Guide：新手引导（默认带遮罩）。</summary>
    [RevUIPanel("RevUIDemo", RevUILayer.Guide, "RevUIDemoGuidePanel")]
    public sealed class RevUIDemoGuidePanel : RevUIDemoLayerPanel
    {
        protected override string LayerLabel => "Guide 层";
        protected override Color Theme => new Color(0.36f, 0.30f, 0.16f, 0.97f);
        protected override string Explain => "新手引导放这一层：默认带遮罩（挡住误点），但它仍然在 Toast 之下。";
    }

    /// <summary>Top：最顶层（默认带遮罩）。</summary>
    [RevUIPanel("RevUIDemo", RevUILayer.Top, "RevUIDemoTopPanel")]
    public sealed class RevUIDemoTopPanel : RevUIDemoLayerPanel
    {
        protected override string LayerLabel => "Top 层";
        protected override Color Theme => new Color(0.42f, 0.20f, 0.20f, 0.97f);
        protected override string Explain => "最顶层：谁也盖不住它（断线重连、全屏 Loading 这类放这里）。";
    }

    // ============================================================
    // 三个"反着来"的对照面板：把默认策略显式改掉
    // ============================================================

    /// <summary>Popup 但不带遮罩、且不进返回栈 —— 和上面那个 Popup 面板对着看。</summary>
    [RevUIPanel("RevUIDemo", RevUILayer.Popup, "RevUIDemoNoMaskPopupPanel",
        Mask = RevUIMaskMode.None,             // 显式关掉遮罩（下面的操作照旧）
        InBackStack = false)]                  // 显式不进返回栈（Back() 跳过它）
    public sealed class RevUIDemoNoMaskPopupPanel : RevUIDemoLayerPanel
    {
        protected override string LayerLabel => "Popup 层（Mask = None / 不进返回栈）";
        protected override Color Theme => new Color(0.26f, 0.24f, 0.44f, 0.97f);
        protected override string Explain => "对照用：同样是 Popup 层，但 Mask 显式写成 None、InBackStack 写成 false —— 点得到下面、Back() 也不会关它。";
    }

    /// <summary>互斥组 A：和 B 同属 "Dialog" 组，开 B 会自动关 A。</summary>
    [RevUIPanel("RevUIDemo", RevUILayer.Popup, "RevUIDemoDialogAPanel", ExclusiveGroup = "Dialog")]
    public sealed class RevUIDemoDialogAPanel : RevUIDemoLayerPanel
    {
        protected override string LayerLabel => "互斥组 Dialog · A";
        protected override Color Theme => new Color(0.34f, 0.24f, 0.30f, 0.97f);
        protected override string Explain => "和 B 同一个 ExclusiveGroup = \"Dialog\"：先开 A 再开 B，A 会被自动关掉（同一时刻只留一个）。\n也可以主动调 RevUI.CloseGroup(\"Dialog\")。";
    }

    /// <summary>互斥组 B：和 A 同属 "Dialog" 组。</summary>
    [RevUIPanel("RevUIDemo", RevUILayer.Popup, "RevUIDemoDialogBPanel", ExclusiveGroup = "Dialog")]
    public sealed class RevUIDemoDialogBPanel : RevUIDemoLayerPanel
    {
        protected override string LayerLabel => "互斥组 Dialog · B";
        protected override Color Theme => new Color(0.34f, 0.30f, 0.22f, 0.97f);
        protected override string Explain => "和 A 同一个互斥组：开 B 时 A 自动关；反过来开 A 也会把 B 关掉。";
    }

    // ============================================================
    // 三 Canvas（动静分离）用的两个 Scene 面板
    //   ★ 只在 RevUISetting.CanvasArchitecture = Split 时才有意义；
    //     且必须"在第一次打开面板之前"设置（场景入口有按钮，含说明）。
    // ============================================================

    /// <summary>Scene + CanvasType = Static：常驻且基本不变的内容（单独一张画布）。</summary>
    [RevUIPanel("RevUIDemo", RevUILayer.Scene, "RevUIDemoStaticScenePanel", CanvasType = RevUICanvasType.Static)]
    public sealed class RevUIDemoStaticScenePanel : RevUIDemoLayerPanel
    {
        protected override string LayerLabel => "Scene 层 · CanvasType = Static";
        protected override Color Theme => new Color(0.14f, 0.20f, 0.28f, 0.97f);
        protected override string Explain => "常驻、基本不变的内容（大厅背景）放 Static：三 Canvas 模式下它有自己一张画布，不会被频繁重建的内容带着一起重建。\n★ 只在 CanvasArchitecture = Split 时生效；单 Canvas 时 CanvasType 会被忽略。";
    }

    /// <summary>Scene + CanvasType = Dynamic：常驻但频繁变化的内容（单独一张画布）。</summary>
    [RevUIPanel("RevUIDemo", RevUILayer.Scene, "RevUIDemoDynamicScenePanel", CanvasType = RevUICanvasType.Dynamic)]
    public sealed class RevUIDemoDynamicScenePanel : RevUIDemoLayerPanel
    {
        protected override string LayerLabel => "Scene 层 · CanvasType = Dynamic";
        protected override Color Theme => new Color(0.20f, 0.28f, 0.22f, 0.97f);
        protected override string Explain => "常驻但频繁变化的内容（HUD：血量 / 倒计时）放 Dynamic：单独一张画布，随它自己重建，不牵连 Static。\n★ 只支持 Scene 层；Normal/Popup/… 声明 Static/Dynamic 会被放回 Common 并告警（保证弹窗在最上面）。";
    }
}
