// RevAutomationDemo —— 最小可运行教学场景，演示“一句话 → Agent → MCP → 玩家入口 → 业务断言”。
//
// 把此脚本挂到空物体再 Play 即可。按钮和 GM 都调用 Add，同一份计数是真实业务状态，
// Agent 应先点击按钮，再查询 demo.state 断言 count 增加，而不能只看 click 返回 dispatched。
// 这是传输/接线示例，不代表你的游戏业务；接入正式玩法时把 Add 换成已有 Service/Logic 方法。
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Revolution.Demo
{
    public sealed class RevAutomationDemo : MonoBehaviour
    {
        private int _count;
        private Text _label;
        [System.Serializable] private sealed class State { public int count; public string source = "shared-player-entry"; }
        [System.Serializable] private sealed class AddArgs { public int amount; }

        private void Awake()
        {
            // UI 只是业务的入口。测试查询直接读业务状态，不从屏幕文字倒推结果。
            var canvasObject = new GameObject("AutomationCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var buttonObject = new GameObject("Increment", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(canvasObject.transform, false);
            var rect = buttonObject.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(360, 100);
            buttonObject.GetComponent<Image>().color = new Color(.12f, .4f, .7f);
            var textObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(buttonObject.transform, false);
            var textRect = textObject.GetComponent<RectTransform>(); textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one; textRect.sizeDelta = Vector2.zero;
            _label = textObject.GetComponent<Text>(); _label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _label.alignment = TextAnchor.MiddleCenter; _label.fontSize = 24; _label.raycastTarget = false;
            buttonObject.GetComponent<Button>().onClick.AddListener(() => Add(1));
            if (UnityEngine.EventSystems.EventSystem.current == null)
            {
                var events = new GameObject("AutomationEventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(StandaloneInputModule));
                events.transform.SetParent(transform, false);
            }
            ResetCounter();
#if UNITY_EDITOR || DEVELOPMENT_BUILD || REV_AUTOMATION
            // 在组合根集中注册。owner=this 使退出场景时能对称清理自己的能力。
            RevRuntimeAutomation.Register("demo.state", "查询按钮和 GM 共用的真实计数", "{\"type\":\"object\",\"additionalProperties\":false}", _ => new State { count = _count }, readOnly: true, owner: this);
            RevRuntimeAutomation.Register("demo.add", "调用与玩家按钮相同的 Add 入口", "{\"type\":\"object\",\"properties\":{\"amount\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":100}},\"required\":[\"amount\"],\"additionalProperties\":false}", json => { Add(JsonUtility.FromJson<AddArgs>(json).amount); return new State { count = _count }; }, owner: this);
            RevRuntimeAutomation.Register("demo.reset", "复位本示例，不触碰存档", "{\"type\":\"object\",\"additionalProperties\":false}", _ => { ResetCounter(); return new State { count = _count }; }, owner: this);
            RevGM.Register("自动化演示/增加", "与玩家按钮相同的计数入口", args => { Add(args.Int(0)); return _count.ToString(); }, RevGMArg.Int("数量", 1));
            RevGM.Register("自动化演示/查询", "读取真实计数", _ => _count.ToString());
            RevGM.Register("自动化演示/重置", "清理本轮示例状态", _ => { ResetCounter(); return RevGM.Done; });
            RevGM.Register("自动化演示/受控重置", "验证高危指令的确认保护", _ => { ResetCounter(); return RevGM.Done; }, RevGMFlags.HighRisk);
#endif
        }
        // 所有入口共享参数边界；GM 无法绕开业务校验。
        private void Add(int amount)
        {
            if (amount < 1 || amount > 100) { throw new RevGMUsageException("数量必须为 1..100"); }
            _count += amount; _label.text = "Count: " + _count + " (click +1)";
        }
        private void ResetCounter() { _count = 0; if (_label != null) { _label.text = "Count: 0 (click +1)"; } }
        private void OnDestroy()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || REV_AUTOMATION
            RevRuntimeAutomation.UnregisterAllOf(this);
            RevGM.Unregister("自动化演示/增加"); RevGM.Unregister("自动化演示/查询");
            RevGM.Unregister("自动化演示/重置"); RevGM.Unregister("自动化演示/受控重置");
#endif
        }
    }
}
