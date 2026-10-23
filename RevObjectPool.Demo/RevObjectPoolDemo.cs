// ============================================================
// RevObjectPoolDemo.cs —— 对象池开箱示例（GameObject 池 + 纯 C# 引用池）
//
// 位置：Assets\Revolution.Demo\RevObjectPool.Demo\
//
// 【它解决什么】
//   子弹 / 特效 / 伤害数字这类"短命对象"如果 Instantiate / Destroy，会产生大量 GC 与卡顿。
//   对象池 = 取出来复用、用完还回去。本框架有两个池：
//     · RevPool    —— GameObject 池（场景对象：取出来就是场景里的实例）；
//     · RevRefPool —— 纯 C# 引用池（伤害数字 / 寻路结果这类 class 对象，实现 IRevPoolable）。
//
// 【怎么用】打开配套场景 RevObjectPoolDemo.unity → 点 Play → 左侧按钮逐个点。
//   本示例的"预制体"是运行时用代码构造的一个立方体模板（真实项目里是拖进来的 prefab）。
//
// 【本示例演示什么】
//   ① GameObject 池：Get / Return、同一批实例被反复复用（看实例名与 ID 不变）；
//   ② 池统计：GetStats / DumpStats（活跃 / 空闲一眼可见）；
//   ③ 引用池：IRevPoolable 的 OnPoolGet / OnPoolReturn 生命周期回调；
//   ④ 一键清空：ClearAll（切场景时的兜底 —— RevScene.AutoClearPool 默认也会做这件事）。
// ============================================================
using System.Collections.Generic;
using UnityEngine;

namespace Revolution.Demo.ObjectPool
{
    public sealed class RevObjectPoolDemo : MonoBehaviour
    {
        /// <summary>
        /// 纯 C# 池化对象示例：一"条"伤害数字。实现 IRevPoolable，
        /// 在 OnPoolReturn 里把字段清干净（否则下次取出来会带着上一次的数据 —— 池化最经典的坑）。
        /// </summary>
        private sealed class DamageNumber : IRevPoolable
        {
            public int Value;
            public string Target;

            public void OnPoolGet() { }                              // 取出时调用（本示例在取出后手动赋值）
            public void OnPoolReturn() { Value = 0; Target = null; } // ★ 归还时清状态
        }

        private readonly List<string> _ui = new List<string>();
        private Vector2 _scroll;

        private GameObject _prefab;            // "预制体"模板（运行时构造；真实项目是拖进来的 prefab 资产）
        private readonly List<GameObject> _live = new List<GameObject>();   // 已取出的实例
        private int _rentCount;

        private void Ui(string line)
        {
            _ui.Add(line);
            if (_ui.Count > 200) _ui.RemoveAt(0);
            _scroll.y = float.MaxValue;
        }

        private void Start()
        {
            // 运行时构造一个"预制体模板"：普通立方体，默认不激活（池的取用会自己激活它）。
            // 真实项目里这一步不存在 —— 直接把做好的 prefab 拖到 Inspector 字段上。
            _prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _prefab.name = "DemoCubePrefab";
            _prefab.transform.position = new Vector3(0, -50, 0);    // 挪出视野：它是模板，不该出现在场景里
            _prefab.SetActive(false);

            Ui("对象池演示就绪（模板立方体已构造）。点「取出 5 个」开始。");
        }

        // ==================== OnGUI ====================

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 430, Screen.height - 20));
            GUILayout.Label("<b>RevObjectPool 对象池演示</b>", TitleStyle());

            // ① GameObject 池：取出 5 个实例（每次 Get 都会复用池里还回来的）
            if (GUILayout.Button("① 取出 5 个立方体（RevPool.Get）"))
            {
                for (int i = 0; i < 5; i++)
                {
                    // Get(GameObject prefab) —— 拿一个实例（池空才 Instantiate，否则复用）
                    GameObject go = RevPool.Get(_prefab, null);
                    go.name = $"DemoCube_{++_rentCount}";
                    go.transform.position = new Vector3(-4f + _live.Count * 1.2f, 1f, 0f);
                    _live.Add(go);
                }
                Ui($"已取出，现场共 {_live.Count} 个实例。再点几次【取出 → 归还】，观察实例名与 ID 不变（是复用，不是新建）。");
            }

            // ② 归还：一次还 3 个（Return 回池，对象不销毁）
            if (GUILayout.Button("② 归还 3 个（RevPool.Return）"))
            {
                int returned = 0;
                for (int i = _live.Count - 1; i >= 0 && returned < 3; i--)
                {
                    if (RevPool.Return(_live[i])) { _live.RemoveAt(i); returned++; }
                }
                Ui($"归还了 {returned} 个（对象没销毁，躺回池里等下次复用）。");
            }

            // ③ 池统计：活跃 / 空闲 / 取用次数一眼可见
            if (GUILayout.Button("③ 查看池统计（DumpStats）"))
            {
                Debug.Log("[RevObjectPoolDemo] 全局统计：\n" + RevPool.DumpStats());
                var s = RevPool.GetGlobalStats();
                Ui($"全局：活跃 {s.ActiveCount} / 空闲 {s.IdleCount} / 累计取用 {s.GetCount}（其中复用命中 {s.Hit}；详情在 Console）。");
            }

            GUILayout.Space(6);

            // ④ 引用池（纯 C#）：伤害数字这类"数据对象"的复用
            if (GUILayout.Button("④ 引用池：取 3 个 DamageNumber"))
            {
                for (int i = 0; i < 3; i++)
                {
                    DamageNumber dn = RevRefPool.Get<DamageNumber>();       // 池空 new，否则复用（触发 OnPoolGet）
                    dn.Value = 100 + i;
                    dn.Target = "哥布林 #" + i;
                    Ui($"取出 DamageNumber：对 {dn.Target} 造成 {dn.Value} 点伤害。");
                }
                Ui($"引用池数量：{RevRefPool.PoolCount}。取完记得 Return —— 见下一个按钮。");
            }
            if (GUILayout.Button("④b 归还 3 个 DamageNumber（触发 OnPoolReturn 清状态）"))
            {
                // 演示用：这里没有存句柄，实际项目里"谁取谁还"。
                // 为让示例可重复点，这里清空整个引用池（等价于把演示对象全部归还）。
                RevRefPool.ClearAll();
                Ui("已清空引用池（真实项目用 RevRefPool.Return(item) 逐个还，OnPoolReturn 里清字段）。");
            }

            GUILayout.Space(6);

            // ⑤ 一键清空：切场景兜底（RevScene.AutoClearPool 默认也会帮你做这件事）
            if (GUILayout.Button("⑤ ClearAll：清空全部池（切场景兜底）"))
            {
                int cleared = RevPool.ClearAll();
                _live.Clear();
                Ui($"清空了 {cleared} 个池的空闲实例（现场对象不受影响）。旧场景的池化实例不该跟着新场景走 —— 这步 RevScene 默认替你做。");
            }

            GUILayout.Space(6);
            if (GUILayout.Button("清空演示日志")) _ui.Clear();

            GUILayout.Label($"现场实例：{_live.Count} 个。原则：谁取谁还；Return 会把对象 SetActive(false) 并回收。", HintStyle());
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

        // ---------- 简单样式 ----------
        private static GUIStyle _title, _hint;
        private static GUIStyle TitleStyle() => _title ??= new GUIStyle(GUI.skin.label) { richText = true, fontSize = 15 };
        private static GUIStyle HintStyle() => _hint ??= new GUIStyle(GUI.skin.label) { richText = true, fontSize = 11, wordWrap = true };
    }
}
