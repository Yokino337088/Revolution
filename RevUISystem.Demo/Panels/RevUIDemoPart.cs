// ============================================================
// RevUIDemoPart.cs —— 演示 Part：可复用的"子界面块"
//
// 位置：Assets\Revolution.Demo\RevUISystem.Demo\Panels\
//
// 【Part 是什么】一块可以塞进任意面板的小界面（列表项、道具格子、通用信息卡…），
//   它自己也是个 MonoBehaviour，有和面板同款的回调与交互分发（OnBindView / OnClick / 控件事件特性），
//   但**生命周期跟着宿主面板走**：宿主打开它打开、宿主关闭它关闭。
//
// 【两种挂法】（本文件同时被这两种用，见 RevUIDemoPartPanel）
//   ① 节点级：面板上写 [RevBind] RevUIDemoPart _nodePart; —— 预制体里已经摆好这个节点，
//      绑定器赋值后自动完成初始化（零加载）；
//   ② 预制体级：[RevUIPart("目录", "名字")] + RevUIPart.Create<T>(host, p => …) —— 按需异步加载，
//      用完 ClosePart() 可关可销毁。**同槽位已有同类型会复用，不会重复建**。
// ============================================================
using UnityEngine;
using UnityEngine.UI;

namespace Revolution.Demo.UI
{
    /// <summary>演示用 Part：一张小信息卡 + 一个按钮（点了会 NotifyHost 通知宿主）。</summary>
    [RevUIPart("RevUIDemo", "RevUIDemoPart")]
    public sealed class RevUIDemoPart : RevUIPart
    {
        [RevBind] private Text _txtPart;
        [RevBind] private Button _btnPartAdd;

        /// <summary>Part 也能一行加动效（打开时播；★ Part 的关闭是同步的，Hide 动画通常没意义）。</summary>
        protected override RevUIAnimPreset ShowAnimation => RevUIAnimPreset.PopIn;

        protected override void OnBindView()
        {
            _txtPart.text = "我是 RevUIDemoPart（可复用子界面块）";
        }

        protected override void OnPartInit()
        {
            RevUIDemoLog.Add("Part", "OnPartInit（只一次）");
        }

        protected override void OnPartOpen()
        {
            RevUIDemoLog.Add("Part", "OnPartOpen（宿主打开时）");
        }

        protected override void OnPartClose()
        {
            RevUIDemoLog.Add("Part", "OnPartClose（宿主关闭 / ClosePart 时）");
        }

        protected override void OnClick(string nodeName)
        {
            if (nodeName != "btnPartAdd") return;

            // 通知宿主"我这边有事"→ 宿主面板的 OnPartChanged(part) 会被调到
            NotifyHost();
        }
    }
}
