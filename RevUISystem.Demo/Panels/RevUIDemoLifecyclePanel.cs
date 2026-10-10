// ============================================================
// RevUIDemoLifecyclePanel.cs —— 演示《使用说明》第二章：面板的生命周期（谁先谁后）
//
// 位置：Assets\Revolution.Demo\RevUISystem.Demo\Panels\
//
// 【这个演示想让你看到什么】
//   ① 第一次打开：OnInit → OnBindView → OnOpen → OnRefreshView（只此一次的两个在最早）；
//   ② 关掉再开（进程没退）：走的是 OnReuse，**不会**再走 OnInit / OnBindView ——
//      "面板会复用"这句抽象的话，在这里变成两行日志；
//   ③ 被别的面板盖住 / 别人关了：OnCovered(true/false)；
//   ④ 手动刷新内容：RefreshView() → OnRefreshView()；
//   ⑤ 关面板：OnClose；真正销毁（ShutdownAll / 缓存满被顶掉）才 OnRelease。
//
//   ★ 顺带把"第 6 章的两个坑"演示了：
//     坑③ "在 OnBindView 里开计时器" —— 你在这里数一下 OnBindView 只走了几次就明白了；
//     坑④ "关闭时不停计时器 / 不退订事件" —— OnClose 里做清理，复用打开才不会串。
//
// 【怎么看】把它打开 → 关掉 → 再打开；中间再开一个 Popup 面板盖它 → 关掉那个 Popup。
// ============================================================
using UnityEngine;
using UnityEngine.UI;

namespace Revolution.Demo.UI
{
    /// <summary>生命周期演示面板：把每个回调都写进共享日志（面板上也能看到自己这份）。</summary>
    [RevUIPanel("RevUIDemo", RevUILayer.Normal, Mask = RevUIMaskMode.None)]
    public sealed class RevUIDemoLifecyclePanel : RevUIPanel
    {
        [RevBind] private Text _txtTitle;
        [RevBind] private Text _txtLog;
        [RevBind] private Button _btnRefresh;
        [RevBind] private Button _btnClose;

        private readonly System.Text.StringBuilder _mine = new System.Text.StringBuilder(512);
        private int _openTimes;                 // 打开了几次（看 OnReuse 就靠它）
        private float _openAt;                  // 本次打开的时间（看"活了多久"）

        // ============================================================
        // 生命周期：按真实顺序排（一对一照文档那张表）
        // ============================================================
        protected override void OnInit()
        {
            // 只走一次：适合缓存组件引用等"一辈子一次"的事
            RevUIDemoLog.Add(Name, "OnInit（第一次创建，只一次）");
        }

        protected override void OnBindView()
        {
            // ★ 也只走一次（[RevBind] 的字段在这之前已经赋好值）
            _txtTitle.text = "② 生命周期（关掉再开看 OnReuse）";
            Note("OnBindView（只一次：绑定控件、装配结构）");
        }

        protected override void OnOpen()
        {
            // ★ 每次打开都走：刷新数据、播开场动画放这里（不是 OnBindView）
            _openTimes++;
            _openAt = Time.realtimeSinceStartup;
            Note($"OnOpen（第 {_openTimes} 次打开）");
            RefreshView();                      // 顺手演示"手动刷新"那条路
        }

        protected override void OnReuse()
        {
            // 从池里取出来复用：想区分"全新 / 复用"才用它（一般用不上）
            Note("OnReuse（从池里复用，注意：没再走 OnInit / OnBindView）");
        }

        protected override void OnRefreshView()
        {
            Note("OnRefreshView（刷新显示内容）");
            Flush();
        }

        protected override void OnCovered(bool covered)
        {
            Note(covered ? "OnCovered(true)（被上层盖住了）" : "OnCovered(false)（上层关掉了）");
            Flush();
        }

        protected override void OnClose()
        {
            // 坑④：计时器 / 订阅 / 资源 全在这一处清 —— 面板会被复用，不清就串到下次打开
            Note($"OnClose（本次打开 {Time.realtimeSinceStartup - _openAt:F2}s）");
            Flush();
        }

        protected override void OnRelease()
        {
            RevUIDemoLog.Add(Name, "OnRelease（真正销毁：ShutdownAll 或被缓存顶掉）");
        }

        // ============================================================
        // 交互
        // ============================================================
        protected override void OnClick(string nodeName)
        {
            switch (nodeName)
            {
                case "btnRefresh":                                  // 手动刷新：RefreshView() → OnRefreshView()
                    RefreshView();
                    break;

                case "btnClose":
                    CloseSelf();                                    // 面板自己关自己（等价于 RevUI.Close<T>()）
                    break;
            }
        }

        // ============================================================
        // 小工具
        // ============================================================
        private string Name => "生命周期面板";

        private void Note(string what)
        {
            RevUIDemoLog.Add(Name, what);
            _mine.AppendLine("· " + what);

            // 面板上留最近 8 条，够看清"这次打开都发生了什么"
            string[] lines = _mine.ToString().Split('\n');
            if (lines.Length > 9)
            {
                _mine.Clear();
                for (int i = lines.Length - 9; i < lines.Length; i++)
                {
                    if (lines[i].Length > 0) _mine.AppendLine(lines[i]);
                }
            }

            Flush();
        }

        private void Flush()
        {
            if (_txtLog != null) _txtLog.text = _mine.ToString();
        }
    }
}
