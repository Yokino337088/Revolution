// ============================================================
// RevUIDemoPartPanel.cs —— 演示 Part 的两种挂法（宿主面板）
//
// 位置：Assets\Revolution.Demo\RevUISystem.Demo\Panels\
//
// 【两种挂法对着看】
//   ① 节点级：预制体里已经摆好一个挂着 RevUIDemoPart 的节点，面板用 [RevBind] 拿它
//      —— 绑定器赋值后自动完成初始化，零加载、随面板一起创建/关闭；
//   ② 预制体级：[RevUIPart("RevUIDemo", "RevUIDemoPart")] 声明 + RevUIPart.Create<T>(this, cb)
//      —— 运行时按需异步加载，用完 ClosePart()。**同槽位已有同类型会复用，不会重复建**。
//
//   两种都会把生命周期写进共享日志（Part 的 OnPartInit / OnPartOpen / OnPartClose）。
//   Part 里点按钮 → NotifyHost() → 宿主面板的 OnPartChanged(part) 被调到（见下面）。
// ============================================================
using UnityEngine;
using UnityEngine.UI;

namespace Revolution.Demo.UI
{
    /// <summary>Part 宿主面板：节点级 + 预制体级两种挂法。</summary>
    [RevUIPanel("RevUIDemo", RevUILayer.Normal, Mask = RevUIMaskMode.None)]
    public sealed class RevUIDemoPartPanel : RevUIPanel
    {
        [RevBind] private Text _txtTitle;
        [RevBind] private Text _txtInfo;

        // ① 节点级：预制体里摆好的 Part（绑定器自动初始化它）
        [RevBind] private RevUIDemoPart _nodePart;

        [RevBind] private Button _btnAddPart;
        [RevBind] private Button _btnClosePart;
        [RevBind] private Button _btnClose;

        // ② 预制体级：运行时 Create 出来的那个（自己留着引用才能关）
        private RevUIDemoPart _createdPart;

        protected override void OnBindView()
        {
            _txtTitle.text = "③ Part（可复用子界面块）· 两种挂法";
            Info("① 节点级：下面这张卡就是预制体里摆好的 Part（[RevBind] 拿到的）\n" +
                 "② 预制体级：点「Create」按需加载一个（同槽位已有会复用）");
        }

        /// <summary>Part 主动通知宿主时进来（Part 里点了按钮 → NotifyHost()）。</summary>
        protected override void OnPartChanged(RevUIPart part)
        {
            string who = part != null ? part.GetType().Name : "?";
            Info($"OnPartChanged ← {who} 通知宿主：Part 里的按钮被点了");
            RevUIDemoLog.Add("Part 面板", $"OnPartChanged（{who}）");
        }

        protected override void OnClick(string nodeName)
        {
            switch (nodeName)
            {
                case "btnAddPart":
                    // ② 预制体级：按需创建（宿主传 this；回调里的 p 失败时为 null）
                    RevUIPart.Create<RevUIDemoPart>(this, p =>
                    {
                        _createdPart = p;
                        Info(p != null
                            ? "② 预制体级 Part 已创建（RevUIPart.Create，挂在宿主的 PartRoot 下）"
                            : "创建失败：看 Console（一般是预制体没生成 / 资源根目录没配）");
                    });
                    break;

                case "btnClosePart":
                    if (_createdPart != null)
                    {
                        _createdPart.ClosePart();          // destroy = true：关掉并销毁
                        _createdPart = null;
                        Info("② 预制体级 Part 已关闭（ClosePart）");
                    }
                    else
                    {
                        Info("还没有创建过预制体级 Part —— 先点上面那个「Create」");
                    }
                    break;

                case "btnClose":
                    CloseSelf();
                    break;
            }
        }

        private readonly System.Collections.Generic.List<string> _recent = new System.Collections.Generic.List<string>(4);

        private void Info(string what)
        {
            _recent.Add(what);
            if (_recent.Count > 4) _recent.RemoveAt(0);

            var sb = new System.Text.StringBuilder(320);
            for (int i = _recent.Count - 1; i >= 0; i--) sb.AppendLine("· " + _recent[i]);
            if (_txtInfo != null) _txtInfo.text = sb.ToString();
        }
    }
}
