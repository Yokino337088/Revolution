// ============================================================
// RevUIDemoWidgetsPanel.cs —— 演示《使用说明》第一章"控件事件的三种接法"里的九种事件
//
// 位置：Assets\Revolution.Demo\RevUISystem.Demo\Panels\
//
// 【九种事件，一条都不少】
//   点击   [RevButtonClick("btnStart")]        （同一个控件挂两个方法 → 都会被调用）
//   长按   [RevButtonLongPress("btnSkill")]    （按住 ≥ RevUISetting.ButtonLongPressSeconds，默认 0.5s，松手时触发）
//   松开   [RevButtonLoosen("btnMove")]        （指针在控件上抬起）
//   Toggle [RevToggleChanged("tglSound")]      (bool) / (string nodeName, bool) 两种形状
//   Slider [RevSliderChanged("sldVolume")]     (float)
//   输入   ——— 这里故意**不写特性**，用重写 OnInputChanged 演示"高频走重写、零分配"
//   输入结束 [RevInputEndEdit("inpName")]      (string text) ← 单 string 参数是"文本"，不是节点名
//   下拉   [RevDropdownChanged("ddlQuality")]  (int index)
//   滚动   [RevScrollChanged("scrollList")]    (string nodeName, float x, float y) ← 三参数形状
//
// 【为什么滚动 / 输入用重写而不是特性】
//   特性走反射调用（MethodInfo.Invoke），每次触发有一次小分配；滚动与每次敲键都算高频。
//   两种写法**可以并存**（同一次事件两条路都会走到，所以同一件事别放两处做）。
//
// 【控件名写错会当场报错】第一次装配就打"找不到名为 xxx 的节点"，不会静默成"点了没反应"。
// ============================================================
using UnityEngine;
using UnityEngine.UI;

namespace Revolution.Demo.UI
{
    /// <summary>九种控件事件演示面板（特性优先，高频两个用重写回调）。</summary>
    [RevUIPanel("RevUIDemo", RevUILayer.Normal, Mask = RevUIMaskMode.None)]
    public sealed class RevUIDemoWidgetsPanel : RevUIPanel
    {
        [RevBind] private Text _txtTitle;
        [RevBind] private Text _txtResult;
        [RevBind] private Button _btnClose;
        [RevBind] private Dropdown _ddlQuality;      // 只有"用代码改 value"这一个按钮要拿它，其余控件靠特性即可

        private int _clicks;
        private int _scrollEvents;             // 滚动触发次数（用来直观感受"高频"）

        // ============================================================
        // ① 方法特性（九个事件里的八个；输入中的那个走重写，见下面 ②）
        // ============================================================

        // —— 点击：0 参 与 (string nodeName) 两种形状，挂在同一个按钮上，两个方法都会调用 ——
        [RevButtonClick("btnStart")]
        private void OnStart()
        {
            _clicks++;
            Report($"点击 0 参形状：第 {_clicks} 次");
        }

        [RevButtonClick("btnStart")]
        private void OnStartNamed(string nodeName)
        {
            Report($"点击 (nodeName) 形状：nodeName = {nodeName}");
        }

        // —— 长按（按住 ≥ 0.5s 后松手触发）/ 松开 ——
        [RevButtonLongPress("btnSkill")]
        private void OnSkillHold()
        {
            Report($"长按触发（阈值 {RevUISetting.ButtonLongPressSeconds}s）");
        }

        [RevButtonLoosen("btnMove")]
        private void OnMoveUp()
        {
            Report("松开触发（指针在控件上抬起）");
        }

        // —— Toggle：两种形状都写上，能看到"同一事件两个方法都被调用" ——
        [RevToggleChanged("tglSound")]
        private void OnSound(bool on)
        {
            Report($"(bool) 形状：声音 = {on}");
        }

        [RevToggleChanged("tglSound")]
        private void OnSoundNamed(string nodeName, bool on)
        {
            Report($"(nodeName, bool) 形状：{nodeName} = {on}");
        }

        // —— Slider ——
        [RevSliderChanged("sldVolume")]
        private void OnVolume(float v)
        {
            Report($"(float) 形状：音量 = {v:F2}");
        }

        // —— 输入框：结束编辑（★ 单 string 参数是"文本"，节点名靠特性声明）——
        [RevInputEndEdit("inpName")]
        private void OnNameDone(string text)
        {
            Report($"输入结束：文本 = \"{text}\"");
        }

        // —— 下拉 ——
        [RevDropdownChanged("ddlQuality")]
        private void OnQuality(int index)
        {
            Report($"(int) 形状：选中第 {index} 项（选项名要靠自己映射）");
        }

        // —— 滚动：三参数形状 (string nodeName, float x, float y) ——
        [RevScrollChanged("scrollList")]
        private void OnScrolled(string nodeName, float x, float y)
        {
            _scrollEvents++;
            // 滚动很密集：只每 20 次报一条，免得日志被刷爆（真实项目这里就该改成"重写回调"省 GC）
            if (_scrollEvents % 20 == 1) Report($"滚动：{nodeName} 位置 = ({x:F2}, {y:F2})，累计 {_scrollEvents} 次");
        }

        // ============================================================
        // ② 重写回调（同样的九种事件都有这个版本：直调、零分配 —— 高频事件推荐）
        // ============================================================

        protected override void OnInputChanged(string nodeName, string text)
        {
            // 每次敲键都会进来：框架这条分发路径是直调、零分配（不再走反射）
            // ★ 但下面这行"为了显示而拼字符串"本身是有分配的 —— 演示要看得见，真实项目里别这么写
            _txtResult.text = $"[重写回调，分发零分配] {nodeName} 正在输入：{text}\n\n" + Tail();
        }

        // ============================================================
        // 其余
        // ============================================================
        protected override void OnBindView()
        {
            _txtTitle.text = "① 九种控件事件（特性优先，滚动/输入走重写）";
            Report("面板已打开：点按钮、按住技能键、拖滑条、切开关、敲输入框、改下拉、滚列表都试试");
        }

        protected override void OnClick(string nodeName)
        {
            // 接法②：按节点名集中分发（说明它和特性可以混用）
            switch (nodeName)
            {
                case "btnClose":
                    CloseSelf();
                    break;

                case "btnDropdownNext":
                    // 用代码改 value 同样会触发 onValueChanged → [RevDropdownChanged] 照样收到
                    // （既是演示，也是"点不开下拉时"的兜底：不依赖展开列表也能看到事件通路）
                    if (_ddlQuality != null && _ddlQuality.options.Count > 0)
                    {
                        _ddlQuality.value = (_ddlQuality.value + 1) % _ddlQuality.options.Count;
                    }
                    break;
            }
        }

        // ============================================================
        // 小工具
        // ============================================================
        private readonly System.Collections.Generic.List<string> _recent = new System.Collections.Generic.List<string>(5);

        private void Report(string what)
        {
            RevUIDemoLog.Add("控件事件面板", what);

            _recent.Add(what);
            if (_recent.Count > 5) _recent.RemoveAt(0);

            if (_txtResult != null) _txtResult.text = Tail();
        }

        /// <summary>最近 5 条，倒序（最新的在最上面）。</summary>
        private string Tail()
        {
            var sb = new System.Text.StringBuilder(256);
            for (int i = _recent.Count - 1; i >= 0; i--) sb.AppendLine("· " + _recent[i]);
            return sb.ToString();
        }
    }
}
