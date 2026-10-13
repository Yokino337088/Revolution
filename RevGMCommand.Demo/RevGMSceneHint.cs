// ============================================================
// RevGMSceneHint.cs —— GM 指令演示场景的场景提示组件
//
// 位置：Assets\Revolution.Demo\RevGMCommand.Demo\
//
// 【为什么需要它】
//   GM 指令的演示本体是纯 C#（RevGMCommandDemo.Register 注册命令，见同目录），
//   指令面板在编辑器菜单里打开 —— 场景里放这个组件，就是为了"打开场景的人知道下一步做什么"。
//
// 【怎么用】打开 RevGMCommandDemo.unity → 点 Play → 菜单
//   Revolution.Tools / GM 指令面板（Ctrl+Shift+G）→ 试着执行"经济/加金币"等命令。
// ============================================================
using UnityEngine;

namespace Revolution.Demo.GM
{
    public sealed class RevGMSceneHint : MonoBehaviour
    {
        private void Start()
        {
            // 确保演示命令已注册（编辑器面板打开时也会收集；这里主动调一次保证 Play 模式下可查）
            RevGMCommandDemo.Register();
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 460, 190));
            GUILayout.Label("<b>RevGM 指令演示</b>", TitleStyle());
            GUILayout.Label("① 菜单：Revolution.Tools / GM 指令面板（Ctrl+Shift+G）", HintStyle());
            GUILayout.Label("② 试着输入：经济/加金币 1000　战斗/无敌 off　聊天/发消息 lobby 你好", HintStyle());
            GUILayout.Label($"③ 当前已注册命令：{RevGM.Count} 条（演示命令见 RevGMCommandDemo）", HintStyle());
            GUILayout.Label("命令可以带参数（面板会校验）、高危命令会二次确认、业务拒绝会显示原因。", HintStyle());
            GUILayout.EndArea();
        }

        // ---------- 简单样式 ----------
        private static GUIStyle _title, _hint;
        private static GUIStyle TitleStyle() => _title ??= new GUIStyle(GUI.skin.label) { richText = true, fontSize = 15 };
        private static GUIStyle HintStyle() => _hint ??= new GUIStyle(GUI.skin.label) { richText = true, fontSize = 12, wordWrap = true };
    }
}
