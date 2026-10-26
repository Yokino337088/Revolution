// ============================================================
// RevEventSystemDemo.cs —— 事件系统开箱示例
//
// 位置：Assets\Revolution.Demo\RevEventSystem.Demo\
//
// 【它解决什么】
//   "A 发生了一件 B 关心的事，但 A 不该认识 B" —— 用字符串事件名解耦：
//   发送方只管 Dispatch，接收方只管订阅（owner 一行防泄漏）。
//
// 【怎么用】打开配套场景 RevEventSystemDemo.unity → 点 Play → 左侧按钮逐个点。
//
// 【本示例演示什么】
//   ① 无参事件 / 带参事件（最多 4 个参数）：把"发生了什么"连数据一起带过去；
//   ② 优先级：同一个事件上 priority **数值大的先执行**（同优先级按注册顺序）；
//   ③ 返回值 = 收到事件的监听者数量（发出去没人听也能发现）；
//   ④ owner：随对象销毁一行清干净（与计时器 / 输入模块同一套纪律）；
//   ⑤ 隔离：某个监听者抛异常不影响其他监听者，也不传染给派发方。
//
// 【★ 一条硬约定：同一个事件名只能有一种签名】
//   所以本示例把"带参的击杀事件"和"无参的收尾事件"拆成两个事件名。
//   如果把它们挂在同一个事件名上，派发时框架会逐个报"签名不匹配"并跳过 ——
//   不静默（比旧框架"什么都不发生"好），但那些监听者确实一个都不会被执行，白注册。
// ============================================================
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Revolution.Demo.EventSystem
{
    public sealed class RevEventSystemDemo : MonoBehaviour
    {
        // ---------- 事件名：业务里请集中放到自己的常量类（拼错编译不过）----------
        private const string BossDead = "demo/boss_dead";             // 带参：谁死了、掉了多少金币
        private const string BossDeadDone = "demo/boss_dead_done";    // 无参：击杀流程收尾
        private const string Boom = "demo/boom";                      // 异常隔离演示
        private const string NobodyListens = "demo/nobody_listens";   // 无人监听演示

        private readonly List<string> _ui = new List<string>();
        private Vector2 _scroll;

        private int _killedCount;                       // 演示数据：击杀计数
        private Action<string, int> _onDrop;            // 缓存委托：RemoveEventListener 按"目标+方法"匹配，存下来才能精确退订
        private Action _onQuestDone;

        private void Ui(string line)
        {
            _ui.Add(line);
            if (_ui.Count > 200) _ui.RemoveAt(0);
            _scroll.y = float.MaxValue;
        }

        private void Start()
        {
            // ---------- 订阅（一般在 OnEnable / OnBindView 里做，退订在 OnDisable / OnClose）----------
            _onDrop = OnDrop;
            RevEvent.AddEventListener(BossDead, _onDrop, owner: this);                       // 掉落系统
            RevEvent.AddEventListener<string, int>(BossDead, OnStatistics, priority: 10, owner: this);   // 统计：priority 更大 → 先执行

            _onQuestDone = OnQuestDone;
            RevEvent.AddEventListener(BossDeadDone, _onQuestDone, owner: this);              // 任务系统
            RevEvent.AddEventListener(BossDeadDone, OnRefreshUi, priority: -10, owner: this); // 刷新：priority 更小 → 后执行

            Ui($"已订阅 2 个事件（各 2 个监听者，全部带 owner: this）：");
            Ui($"  · {BossDead}：带参 Action<string,int> —— 统计 priority=10 先跑，掉落 priority=0 后跑");
            Ui($"  · {BossDeadDone}：无参 Action —— 任务 priority=0 先跑，界面 priority=-10 后跑");
            Ui("点「① 击杀 Boss」派发这两个事件。");
        }

        // 带参监听者：参数随事件一起带过来（这里用 string + int，最多支持 4 个）
        private void OnDrop(string name, int gold) => Ui($"[掉落系统] {name} 死亡 → 掉落 {gold} 金币");

        private void OnStatistics(string name, int gold) => Ui($"[战斗统计 priority=10] 先收到 → {name} 计入击杀榜，本局经济 +{gold}");

        // 无参监听者：只有"发生了"，没有数据
        private void OnQuestDone() => Ui("[任务系统] 收到收尾事件 → 击杀任务完成 +1");

        private void OnRefreshUi() => Ui("[界面 priority=-10] 最后收到 → 结算界面刷新（拿到的是已经算好的数据）");

        // ==================== OnGUI ====================

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 450, Screen.height - 20));
            GUILayout.Label("<b>RevEvent 事件系统演示</b>", TitleStyle());
            GUILayout.Label($"监听者：{BossDead} = {RevEvent.GetListenerCount(BossDead)}　{BossDeadDone} = {RevEvent.GetListenerCount(BossDeadDone)}　历史击杀：{_killedCount}", HintStyle());

            // ① 派发两个事件：返回值 = 收到事件的监听者数
            if (GUILayout.Button("① 击杀 Boss（带参事件 + 无参事件各派发一次）"))
            {
                _killedCount++;
                int dropReceived = RevEvent.DispatchEvent(BossDead, "暗影领主", 500 + _killedCount * 10);
                int doneReceived = RevEvent.DispatchEvent(BossDeadDone);
                Ui($"派发完成：带参事件 {dropReceived} 个监听者、无参事件 {doneReceived} 个监听者收到" +
                   $"（返回值让你发现『发出去没人听 / 名字拼错了』）。");
            }

            // ② 派发一个没有任何监听者的事件：LogNoListener 打开后会有提示
            if (GUILayout.Button("② 派发没人听的事件"))
            {
                RevEvent.LogNoListener = true;                       // 打开后：派发无人监听的事件会打一条日志
                int received = RevEvent.DispatchEvent(NobodyListens);
                Ui($"{received} 个监听者收到（LogNoListener=true 时 Console 会提示你事件名可能拼错了）。");
            }

            // ③ 异常隔离：某个监听者抛异常，其他监听者照常、派发方不被传染
            if (GUILayout.Button("③ 异常隔离演示"))
            {
                // 退订按"委托引用"匹配 —— 要退订就把委托存下来（这也是所有事件系统的通用纪律）
                Action boom = () => throw new InvalidOperationException("监听者故意的");
                RevEvent.AddEventListener(Boom, boom, owner: this);
                RevEvent.AddEventListener(Boom, () => Ui("抛异常的监听者后面这个，照常执行 ✓"), owner: this);

                int received = RevEvent.DispatchEvent(Boom);
                Ui($"派发完成：{received} 个监听者被调用（抛异常那个已被隔离，不影响后面）。");
                RevEvent.RemoveEventListener(Boom, boom);     // 引用相同 → 退订成功
            }

            // ④ owner 清理：退掉本对象注册的全部监听（含上面演示加的）
            if (GUILayout.Button("④ RemoveAllByOwner(this)：一行清干净"))
            {
                int removed = RevEvent.RemoveAllByOwner(this);
                Ui($"按 owner 清掉了 {removed} 个监听 —— 这就是 OnDestroy 里该写的那一行。");
            }

            if (GUILayout.Button("⑤ 重新订阅（清完再玩一轮）"))
            {
                RevEvent.AddEventListener(BossDead, _onDrop, owner: this);
                RevEvent.AddEventListener<string, int>(BossDead, OnStatistics, priority: 10, owner: this);
                RevEvent.AddEventListener(BossDeadDone, _onQuestDone, owner: this);
                RevEvent.AddEventListener(BossDeadDone, OnRefreshUi, priority: -10, owner: this);
                Ui($"重新订阅完成（{BossDead} = {RevEvent.GetListenerCount(BossDead)}，{BossDeadDone} = {RevEvent.GetListenerCount(BossDeadDone)} 个监听者）。");
            }

            GUILayout.Space(6);
            if (GUILayout.Button("清空演示日志")) _ui.Clear();

            GUILayout.Label("★ 同一个事件名只能有一种签名（本示例拆成两个事件名）；事件名建议集中在常量类里定义，避免拼错。", HintStyle());
            GUILayout.Label("★ priority 数值大的先执行（不是从前到后）。", HintStyle());
            GUILayout.FlexibleSpace();
            GUILayout.EndArea();

            // ---------- 右侧：演示日志 ----------
            GUILayout.BeginArea(new Rect(Screen.width - 490, 10, 480, Screen.height - 20));
            GUILayout.Label("<b>演示步骤日志</b>", TitleStyle());
            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (string line in _ui) GUILayout.Label(line);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // 防泄漏示范：本示例以 this 为 owner 的监听全部带走
        private void OnDestroy() => RevEvent.RemoveAllByOwner(this);

        // ---------- 简单样式 ----------
        private static GUIStyle _title, _hint;
        private static GUIStyle TitleStyle() => _title ??= new GUIStyle(GUI.skin.label) { richText = true, fontSize = 15 };
        private static GUIStyle HintStyle() => _hint ??= new GUIStyle(GUI.skin.label) { richText = true, fontSize = 11, wordWrap = true };
    }
}
