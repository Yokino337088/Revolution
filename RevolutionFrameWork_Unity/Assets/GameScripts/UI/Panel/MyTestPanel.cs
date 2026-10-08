// 面板代码骨架：由 Revolution.Tools/UI/面板代码生成器生成。再次生成时会确认覆盖；手写逻辑请放在别的文件或生成后及时修改。
using Revolution;
using UnityEngine;
using UnityEngine.UI;
namespace RevGame.UI
{
    [RevUIPanel("UI", RevUILayer.Normal, "MyTestPanel")]
    public sealed class MyTestPanel : RevUIPanel
    {
        [RevBind("btnClose")] private Button _btnClose;
        [RevBind("tglSound")] private Toggle _tglSound;
        [RevBind("sldVolume")] private Slider _sldVolume;
        [RevBind("inpName")] private InputField _inpName;
        [RevBind("ddlQuality")] private Dropdown _ddlQuality;
        [RevBind("ddlQuality/Template/Viewport/Content/Item")] private Toggle _item;
        [RevBind("scrollList")] private ScrollRect _scrollList;

        protected override void OnBindView()
        {
            // 这里的 [RevBind] 字段已赋值；只做首次装配，不在此处重复订阅自动事件。
        }

        protected override void OnOpen()
        {
            // 每次打开面板都会调用（包括从池里复用）。
        }

        protected override void OnRefreshView()
        {
            // 在这里把数据绘制到已绑定的控件上。
        }

        [RevButtonClick("btnClose")]
        private void OnBtnCloseClick()
        {
            // 在此处理该控件事件。
        }

        [RevToggleChanged("tglSound")]
        private void OnTglSoundToggleChanged(bool value)
        {
            // 在此处理该控件事件。
        }

        [RevSliderChanged("sldVolume")]
        private void OnSldVolumeSliderChanged(float value)
        {
            // 在此处理该控件事件。
        }

        [RevInputEndEdit("inpName")]
        private void OnInpNameInputEndEdit(string value)
        {
            // 在此处理该控件事件。
        }

        [RevDropdownChanged("ddlQuality")]
        private void OnDdlQualityDropdownChanged(int index)
        {
            // 在此处理该控件事件。
        }

        [RevToggleChanged("Item")]
        private void OnItemToggleChanged(bool value)
        {
            // 在此处理该控件事件。
        }
    }
}
