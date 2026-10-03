// ============================================================
// RevPublicMonoDemo.cs —— 公共 Mono 模块开箱示例
//
// 位置：Assets\Revolution.Demo\RevPublicMono.Demo\
//
// 【它解决什么】
//   一个**纯 C# 类**没有 Update、也不能 StartCoroutine —— 本模块给它一个公共宿主：
//   每帧回调（Update / LateUpdate / FixedUpdate 三个相位）+ 协程能力，零配置自动建宿主。
//
// 【怎么用】打开配套场景 RevPublicMonoDemo.unity → 点 Play → 左侧按钮逐个点。
//
// 【本示例演示什么】
//   ① AddUpdate / AddLateUpdate / AddFixedUpdate 三个相位的区别与选择；
//   ② 去重：同一个委托加两次只生效一次（返回 false 告诉你）；
//   ③ 协程：纯 C# 场景之外的任何地方都能 StartCoroutine / StopCoroutine；
//   ④ 作用域：using 一块，出块自动全停（防泄漏的结构化写法）；
//   ⑤ owner：随对象销毁一行清干净；
//   ⑥ 异常隔离：一个监听者抛异常不影响同一帧的其他监听者（也不会自动摘除它）。
//
// 【三条铁律（详见模块文档）】
//   选对相位（相机跟随用 LateUpdate、物理用 FixedUpdate）；随对象销毁一行清干净；别在监听里做重活。
// ============================================================
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Revolution.Demo.PublicMono
{
    public sealed class RevPublicMonoDemo : MonoBehaviour
    {
        private readonly List<string> _ui = new List<string>();
        private Vector2 _scroll;

        private int _updateTicks;                       // Update 相位计数（演示"每帧一次"）
        private int _fixedTicks;                        // FixedUpdate 相位计数（一帧可能 0 次或多次）
        private RevMonoScope _scope;                    // 作用域（演示"出一块全停"）
        private Coroutine _loopCo;                      // 协程句柄（存下来才能单独停）
        private int _loopCount;

        private void Ui(string line)
        {
            _ui.Add(line);
            if (_ui.Count > 200) _ui.RemoveAt(0);
            _scroll.y = float.MaxValue;
        }

        private void Start()
        {
            Ui("公共 Mono 演示就绪。宿主会在第一次 AddXxx 时自动创建（RevMono.IsRunning 可查）。");
            Ui("提示：FixedUpdate 的回调次数由物理帧率决定 —— 一帧可能 0 次或多次，这正是选对相位的意义。");
        }

        // ---------- 相位回调（框架每帧推给我们；这里是它们干活的证据） ----------

        private void OnUpdateTick() => _updateTicks++;                                  // Update：每帧一次
        private void OnFixedTick() => _fixedTicks++;                                    // FixedUpdate：物理帧
        private void OnLateTick() { /* LateUpdate 演示按钮里单独加，避免刷屏 */ }

        // 一个故意抛异常的监听者：演示"异常被隔离，但监听者不会被自动摘掉"
        private int _boomCount;
        private void BoomEverySecond()
        {
            _boomCount++;
            throw new System.InvalidOperationException($"故意的异常（第 {_boomCount} 次）");
        }

        // 一段协程：每 0.5 秒报一次数，共 6 次
        private IEnumerator LoopCoroutine()
        {
            for (int i = 1; i <= 6; i++)
            {
                yield return new WaitForSeconds(0.5f);
                Ui($"协程节拍 {i}/6（new WaitForSeconds 也能用：宿主就是普通 MonoBehaviour）");
            }
            Ui("协程自然结束。");
        }

        // ==================== OnGUI ====================

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 430, Screen.height - 20));
            GUILayout.Label("<b>RevPublicMono 公共宿主演示</b>", TitleStyle());
            GUILayout.Label($"宿主就绪：{RevMono.IsRunning}　Update 计数：{_updateTicks}　Fixed 计数：{_fixedTicks}", HintStyle());

            // ① 加 / 减 Update 相位回调
            if (GUILayout.Button("① AddUpdate：每帧计数 +1"))
            {
                bool added = RevMono.AddUpdate(OnUpdateTick, owner: this);
                Ui(added ? "AddUpdate 成功（owner: this，销毁时一行清干净）。左侧 Update 计数开始增长。"
                         : "返回 false：已经加过了（去重）—— 同一个委托加两次只生效一次。");
            }
            if (GUILayout.Button("①b RemoveUpdate：移除计数回调"))
            {
                Ui(RevMono.RemoveUpdate(OnUpdateTick) ? "已移除：计数停止增长。" : "本来就没加（返回 false）。");
            }

            // ② 去重演示：连点两次，第二次返回 false
            if (GUILayout.Button("② 去重：AddLateUpdate 加两次"))
            {
                bool first = RevMono.AddLateUpdate(OnLateTick, owner: this);
                bool second = RevMono.AddLateUpdate(OnLateTick, owner: this);
                Ui($"第一次返回 {first}，第二次返回 {second}（重复加只生效一次 —— 旧实现会每帧跑两次且毫无提示）。");
                RevMono.RemoveLateUpdate(OnLateTick);
            }

            // ③ FixedUpdate：物理帧（演示次数差异）
            if (GUILayout.Button("③ AddFixedUpdate：物理帧计数"))
            {
                RevMono.AddFixedUpdate(OnFixedTick, owner: this);
                Ui("已加 FixedUpdate 回调：观察 Fixed 计数与 Update 计数的增速差异（物理帧 ≠ 渲染帧）。");
            }

            GUILayout.Space(6);

            // ④ 协程：纯 C# 类没有 StartCoroutine，本模块替它跑
            if (GUILayout.Button("④ StartCoroutine：跑一段协程"))
            {
                _loopCo = RevMono.StartCoroutine(LoopCoroutine());
                Ui(_loopCo != null ? "协程已启动（返回 Unity 的 Coroutine 句柄，可单独 Stop）。" : "启动失败（编辑器没 Play？Failed(NotPlaying) 会报）。");
            }
            if (GUILayout.Button("④b StopCoroutine：停掉它"))
            {
                if (_loopCo != null) { RevMono.StopCoroutine(_loopCo); Ui("协程已停（半路取消也要能停，这是协程管理的另一半）。"); }
                else Ui("还没有启动过协程。");
            }

            GUILayout.Space(6);

            // ⑤ 作用域：using 一块，出块自动全停（防泄漏的结构化写法）
            if (GUILayout.Button("⑤ OpenScope：开一块（内加监听 + 起协程）"))
            {
                _scope?.Dispose();                                   // 先收掉上一块（重复开不泄漏）
                _scope = RevMono.OpenScope();
                _scope.AddUpdate(() => { });                         // 这块里加的都归 scope 管
                _loopCo = _scope.StartCoroutine(LoopCoroutine());
                Ui("作用域已开：里面的监听与协程都归它管。点下面的按钮出块 → 全部自动消失。");
            }
            if (GUILayout.Button("⑤b Dispose 作用域（出块全停）"))
            {
                if (_scope != null) { _scope.Dispose(); Ui("作用域已释放：里面加的监听全摘、协程全停 —— 不用逐个记句柄。"); _scope = null; }
                else Ui("还没开过作用域。");
            }

            GUILayout.Space(6);

            // ⑥ 异常隔离：一个监听者抛异常，同一帧其他监听者照常跑
            if (GUILayout.Button("⑥ 异常隔离：加一个每秒抛异常的监听者"))
            {
                RevMono.AddUpdate(BoomEverySecond, owner: this);
                RevMono.AddUpdate(() => Ui("同帧的其他监听者照常执行 ✓"), owner: this);
                Ui("加了一个每帧抛异常的监听者：异常走 RevMono.Failed + OnException（默认进日志系统，tag=Mono）；" +
                   "其他监听者不受影响。注意：框架不会自动摘掉它 —— 要停请自己 Remove（这是刻意的：别让一次异常吞掉你的逻辑）。");
            }

            if (GUILayout.Button("⑦ RemoveAllOf(this)：按 owner 一行全清"))
            {
                int n = RevMono.RemoveAllOf(this);
                Ui($"按 owner 清掉了 {n} 项（监听 + 本示例加的全部）—— 这就是 OnDestroy 里该写的那一行。");
            }

            GUILayout.Space(6);
            if (GUILayout.Button("清空演示日志")) _ui.Clear();

            GUILayout.Label($"监听总数：{RevMono.Count}（U={RevMono.UpdateCount} / L={RevMono.LateUpdateCount} / F={RevMono.FixedUpdateCount}）", HintStyle());
            GUILayout.FlexibleSpace();
            GUILayout.EndArea();

            // ---------- 右侧：演示日志 ----------
            GUILayout.BeginArea(new Rect(Screen.width - 470, 10, 460, Screen.height - 20));
            GUILayout.Label("<b>演示步骤日志</b>", TitleStyle());
            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (string line in _ui) GUILayout.Label(line);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // 销毁示范：本示例以 this 为 owner 的监听全部带走（协程随宿主场景销毁自然结束）
        private void OnDestroy() => RevMono.RemoveAllOf(this);

        // ---------- 简单样式 ----------
        private static GUIStyle _title, _hint;
        private static GUIStyle TitleStyle() => _title ??= new GUIStyle(GUI.skin.label) { richText = true, fontSize = 15 };
        private static GUIStyle HintStyle() => _hint ??= new GUIStyle(GUI.skin.label) { richText = true, fontSize = 11, wordWrap = true };
    }
}
