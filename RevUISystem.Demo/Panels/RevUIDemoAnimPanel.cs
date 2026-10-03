// ============================================================
// RevUIDemoAnimPanel.cs —— 演示《使用说明》第五章"面板 / Part / 控件的动画"
//
// 位置：Assets\Revolution.Demo\RevUISystem.Demo\Panels\
//
// 【三层动效，三种写法】
//   ① 面板：一行预设属性（本面板就是：ShowAnimation = PopIn / HideAnimation = PopOut）
//      —— "打开动画播完才算打开完，关闭动画播完才真正回收"，不用自己管时序；
//   ② 自定义转场：重写 PlayOpenTransition / PlayCloseTransition（见同文件的 Slide 面板，
//      ★ 结束时**必须**调 onDone，否则面板一直停在 Opening）；
//   ③ 任意控件：RevUIAnim.FadeIn / SlideIn / ScaleTo / FadeTo / Breathe / AddHoverFeedback / Play …
//      本面板下半部分的按钮就是这一排（动的是图标和提示文字）。
//
// 【六条要记住的（都在这块界面上能验证）】
//   · 时长不传（或 ≤ 0）= 用该预设的默认时长；
//   · **一定要传 owner**（面板 / Part 传自己）—— 关闭时框架会 StopAllOf(owner) 一行清干净；
//   · 同一个控件上只留一个动画：再起一个会自动顶掉上一个；
//   · 动画走 unscaledDeltaTime（timeScale = 0 也照播）；
//   · 想关全部动效：RevUISetting.UIAnimationsEnabled = false（场景入口有按钮）；
//   · 透明度写 CanvasGroup（没有会自动补），缩放 / 位移写 RectTransform。
// ============================================================
using UnityEngine;
using UnityEngine.UI;

namespace Revolution.Demo.UI
{
    /// <summary>动画演示面板：面板预设 + 控件动画全套。</summary>
    [RevUIPanel("RevUIDemo", RevUILayer.Normal, Mask = RevUIMaskMode.None)]
    public sealed class RevUIDemoAnimPanel : RevUIPanel
    {
        // ① 面板预设：打开自动播 PopIn（播完才算打开完），关闭先播 PopOut（播完才回收）
        protected override RevUIAnimPreset ShowAnimation => RevUIAnimPreset.PopIn;
        protected override RevUIAnimPreset HideAnimation => RevUIAnimPreset.PopOut;

        [RevBind] private Text _txtTitle;
        [RevBind] private Text _txtInfo;
        [RevBind] private Image _imgIcon;                 // 被动画演示的图标
        [RevBind] private Text _txtTip;                   // 被动画演示的文字
        [RevBind] private Button _btnHover;               // 悬停 / 按下反馈
        [RevBind] private Button _btnClose;

        protected override void OnBindView()
        {
            _txtTitle.text = "⑤ 动画（面板预设 + 控件动效）";
            _txtInfo.text =
                "本面板用了面板预设：打开 PopIn、关闭 PopOut（关的时候会先播完再回收）。\n" +
                "下面这些按钮直接调 RevUIAnim 门面，试着连点同一个按钮 —— 新动画会自动顶掉旧的。\n" +
                "最上那个「悬停反馈」按钮：鼠标悬停 / 按下自带缩放反馈。";
            _imgIcon.color = new Color(0.35f, 0.62f, 0.98f, 0.95f);

            // 控件反馈：悬停放大 + 按下缩小（重复调用只会更新参数）
            RevUIAnim.AddHoverFeedback(_btnHover);
        }

        protected override void OnOpen()
        {
            RevUIDemoLog.Add("动画面板", "打开（播了 PopIn 预设；下面按钮可以单独给控件播动画）");
        }

        protected override void OnClose()
        {
            // 面板关闭时框架会 StopAllOf(this) 兜底；这里只是记一笔方便对照日志
            RevUIDemoLog.Add("动画面板", $"关闭（HideAnimation = PopOut；此刻在播的动画 {RevUIAnim.ActiveCount} 个）");
        }

        protected override void OnClick(string nodeName)
        {
            switch (nodeName)
            {
                case "btnFadeIn":
                    RevUIAnim.FadeIn(_imgIcon, 0.25f, null, this);          // 淡入
                    Say("FadeIn(0.25s)：淡入");
                    break;

                case "btnSlideIn":
                    RevUIAnim.SlideIn(_imgIcon, RevUISlideDirection.Left, 0.28f, null, this);   // 从左滑入
                    Say("SlideIn(Left, 0.28s)：从左滑入");
                    break;

                case "btnScaleTo":
                    RevUIAnim.ScaleTo(_imgIcon, 1.25f, 0.12f);               // 缩放到 1.25 倍（相对基准缩放）
                    Say("ScaleTo(1.25)：放大到 1.25 倍（再点一次回到基准用 RestoreBase）");
                    break;

                case "btnFadeTo":
                    RevUIAnim.FadeTo(_imgIcon, 0.35f);                      // 淡到半透明
                    Say("FadeTo(0.35)：淡到半透明");
                    break;

                case "btnBreathe":
                    RevUIAnim.Breathe(_txtTip);                             // 呼吸（透明度 0.45 ↔ 1 无限往返）
                    Say("Breathe()：文字开始呼吸（无限往返，直到 StopAllOf / 关闭面板）");
                    break;

                case "btnPlay":
                    // 按预设播 + 播完回调（owner 一定要传：关闭时框架按 owner 一次清干净）
                    RevUIAnim.Play(_imgIcon, RevUIAnimPreset.PopIn, 0.3f,
                        () => Say("Play(PopIn) 播完了 → 这是 onDone 回调"), owner: this);
                    Say("Play(PopIn, 0.3s)：播完会打一条回调日志");
                    break;

                case "btnApplyEnd":
                    RevUIAnim.ApplyEnd(_imgIcon, RevUIAnimPreset.PopIn);     // 跳过动画：直接落到终态
                    RevUIAnim.RestoreBase(_imgIcon);                         // 顺手演示"恢复基准态"
                    Say("ApplyEnd + RestoreBase：不做动画，直接到终态 / 回到基准");
                    break;

                case "btnStopAll":
                {
                    int stopped = RevUIAnim.StopAllOf(this);                 // 停掉本面板发起的全部动画
                    Say($"StopAllOf(this)：停掉 {stopped} 个动画（ActiveCount = {RevUIAnim.ActiveCount}）");
                    break;
                }

                case "btnDump":
                    Debug.Log("[RevUIDemo] " + RevUIAnim.Dump());            // 诊断：在播 / 池 / 起播 / 播完 / 停
                    Say("已把 RevUIAnim.Dump() 打到 Console：在播 / 池 / 起播 / 播完 / 停 各几个");
                    break;

                case "btnClose":
                    CloseSelf();
                    break;
            }
        }

        private readonly System.Collections.Generic.List<string> _recent = new System.Collections.Generic.List<string>(4);

        private void Say(string what)
        {
            RevUIDemoLog.Add("动画面板", what);

            _recent.Add(what);
            if (_recent.Count > 4) _recent.RemoveAt(0);

            var sb = new System.Text.StringBuilder(256);
            for (int i = _recent.Count - 1; i >= 0; i--) sb.AppendLine("· " + _recent[i]);
            if (_txtInfo != null) _txtInfo.text = sb.ToString() + "\n（面板本身：打开 PopIn / 关闭 PopOut）";
        }
    }

    /// <summary>自定义转场演示：不用预设，自己掌控节奏（★ 结束时必须调 onDone）。</summary>
    [RevUIPanel("RevUIDemo", RevUILayer.Normal, "RevUIDemoSlidePanel", Mask = RevUIMaskMode.None)]
    public sealed class RevUIDemoSlidePanel : RevUIPanel
    {
        [RevBind] private Text _txtTitle;
        [RevBind] private Text _txtInfo;
        [RevBind] private Button _btnClose;

        /// <summary>打开：从屏幕上方滑入（0.25 秒），播完才算"打开完成"。</summary>
        protected override void PlayOpenTransition(System.Action onDone)
            => RevUIAnim.SlideIn(this, RevUISlideDirection.Top, 0.25f, onDone, owner: this);

        /// <summary>关闭：往上滑出（0.2 秒），播完才真正回收。</summary>
        protected override void PlayCloseTransition(System.Action onDone)
            => RevUIAnim.SlideOut(this, RevUISlideDirection.Top, 0.2f, onDone, owner: this);

        protected override void OnBindView()
        {
            _txtTitle.text = "⑤ 自定义转场（PlayOpenTransition）";
            _txtInfo.text =
                "这个面板没用预设，而是重写了 PlayOpenTransition / PlayCloseTransition：\n" +
                "打开 = 从上滑入 0.25s，关闭 = 往上滑出 0.2s。\n" +
                "★ 关键：结束时必须调 onDone —— 否则框架会一直等，面板停在 Opening。";
        }

        protected override void OnClick(string nodeName)
        {
            if (nodeName == "btnClose") CloseSelf();
        }
    }
}
