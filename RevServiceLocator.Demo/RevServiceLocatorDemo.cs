// ============================================================
// RevServiceLocatorDemo.cs —— 服务定位器开箱示例
//
// 位置：Assets\Revolution.Demo\RevServiceLocator.Demo\
//
// 【它解决什么】
//   "底层框架要调上层能力，但程序集依赖只能单向" —— 把依赖变成显式注册 + 显式获取：
//   装配只在一处（组合根）写完，Build 之后改不了；取不到在启动期就炸出来，不留到运行期空引用。
//
// 【怎么用】打开配套场景 RevServiceLocatorDemo.unity → 点 Play → 左侧按钮逐个点。
//
// 【本示例演示什么】
//   ① 装配三件套：AddSingleton（工厂 / 实例）+ AddScoped + Build；
//   ② 重复注册当场报错（框架刻意不做静默覆盖）；
//   ③ GetRequired：没注册就抛异常（把"忘了注册"提前到启动期）；
//   ④ Singleton 共享：根容器与作用域拿到的是同一个实例；
//   ⑤ Scoped：每个作用域一份（一局战斗 / 一个场景的边界）；
//   ⑥ 循环依赖检测：两个服务在 OnInit 里互相取 → 明确报错并打印依赖链；
//   ⑦ 释放：按逆创建序释放，容器释放后取服务会报错而不是给你一个已释放的对象。
//
// 【真实项目怎么组织】把下面的装配代码放进组合根（游戏入口），缓存到 static readonly 字段。
// ============================================================
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Revolution.Demo.ServiceLocator
{
    // ---------- 演示用的服务契约与实现（真实项目里契约放接口文件，实现放各自模块） ----------

    /// <summary>音效服务契约（业务只认接口）。</summary>
    public interface IDemoAudio { string Play(string name); }

    /// <summary>音效服务实现（Singleton：全局一份）。</summary>
    public sealed class DemoAudio : IDemoAudio
    {
        public string Play(string name) => $"播放音效：{name}";
    }

    /// <summary>一局战斗的上下文（Scoped：每个作用域一份 —— 血量这类"每局独立"的状态放这里）。</summary>
    public interface IDemoBattle
    {
        int Hp { get; set; }
        string TakeDamage(int dmg);
    }

    /// <summary>战斗上下文实现。实现 RevIServiceInit：创建后从容器里取依赖（构造函数里不要取）。</summary>
    public sealed class DemoBattle : Revolution.RevIServiceInit, IDemoBattle
    {
        public int Hp { get; set; }
        private IDemoAudio _audio;                            // 依赖在 OnInit 里取，而不是构造函数

        public void OnInit(Revolution.RevIServiceLocator services)
        {
            _audio = services.GetRequired<IDemoAudio>();      // ← 取依赖的正确位置
            Hp = 100;                                         // 每一局的初始状态
        }

        public string TakeDamage(int dmg)
        {
            Hp -= dmg;
            return $"{_audio.Play("hit") }，剩余血量 {Hp}";
        }
    }

    // ==================== 演示入口 ====================

    public sealed class RevServiceLocatorDemo : MonoBehaviour
    {
        private readonly List<string> _ui = new List<string>();
        private Vector2 _scroll;

        private Revolution.RevServiceLocator _root;           // 根容器（Build 一次，缓存复用）
        private Revolution.RevServiceLocator _battleScope;    // 当前作用域（一局）

        private void Ui(string line)
        {
            _ui.Add(line);
            if (_ui.Count > 200) _ui.RemoveAt(0);
            _scroll.y = float.MaxValue;
        }

        // ==================== OnGUI ====================

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 450, Screen.height - 20));
            GUILayout.Label("<b>RevServiceLocator 服务定位器演示</b>", TitleStyle());

            // ① 装配 + Build（组合根）
            if (GUILayout.Button("① 装配并 Build（创建根容器）"))
            {
                if (_root != null) { Ui("已经 Build 过了 —— 缓存起来复用，不要重复 Build（再 Build 会报错）。"); }
                else
                {
                    _root = Revolution.RevServiceLocator.Create()
                        .AddSingleton<IDemoAudio, DemoAudio>()                       // 类型对类型：第一次取用时 new
                        .AddScoped<IDemoBattle, DemoBattle>()                        // 每个作用域一份
                        .Build();                                                    // ← 冻结装配：之后改不了
                    Ui("根容器已创建（Singleton 1 个 + Scoped 1 个）。Build 之后装配只读 —— 这是纪律的类型化。");
                }
            }

            // ② 重复注册：当场报错，不静默覆盖
            if (GUILayout.Button("② 重复注册同一契约（演示报错）"))
            {
                try
                {
                    Revolution.RevServiceLocator.Create().AddSingleton<IDemoAudio, DemoAudio>()
                        .AddSingleton<IDemoAudio, DemoAudio>().Build();              // 第二次注册 → 抛异常
                }
                catch (InvalidOperationException e) { Ui("已拦下：" + e.Message.Split('\n')[0]); }
            }

            // ③ GetRequired：Singleton 全局共享
            if (GUILayout.Button("③ GetRequired<IDemoAudio>（Singleton 共享）"))
            {
                if (_root == null) { Ui("请先点 ①Build。"); return; }
                var a = _root.GetRequired<IDemoAudio>();
                var b = _root.GetRequired<IDemoAudio>();
                Ui($"两次取到的是同一个实例：{ReferenceEquals(a, b)}（{a.Play("ui_click")}）");
            }

            GUILayout.Space(6);

            // ④ Scoped：开一局，每个作用域独立
            if (GUILayout.Button("④ CreateScope：开一局（Scoped 服务独立）"))
            {
                if (_root == null) { Ui("请先点 ①Build。"); return; }
                _battleScope?.Dispose();
                _battleScope = _root.CreateScope();
                var battle = _battleScope.GetRequired<IDemoBattle>();                // 第一次取用时创建 + OnInit
                Ui($"新作用域已创建：本局血量 {battle.Hp}（每局独立；Singleton 依旧共享）。");
            }
            if (GUILayout.Button("④b 本局受伤 30（状态只属于这一局）"))
            {
                if (_battleScope == null) { Ui("请先点 ④CreateScope。"); return; }
                Ui(_battleScope.GetRequired<IDemoBattle>().TakeDamage(30));
            }
            if (GUILayout.Button("④c 再开一局（新旧两局互不影响）"))
            {
                if (_root == null) { Ui("请先点 ①Build。"); return; }
                using (var s2 = _root.CreateScope())
                {
                    var b2 = s2.GetRequired<IDemoBattle>();
                    Ui($"第二局血量 {b2.Hp}（与第一局无关 —— 退出 using 时这一局已被释放）。");
                }
            }

            GUILayout.Space(6);

            // ⑤ 循环依赖检测：两个服务在 OnInit 里互相取 → 明确报错并打印依赖链
            if (GUILayout.Button("⑤ 循环依赖演示（明确报错 + 依赖链）"))
            {
                try
                {
                    Revolution.RevServiceLocator.Create()
                        .AddSingleton<A_LOOP, A_LOOP>().AddSingleton<B_LOOP, B_LOOP>()
                        .Build().GetRequired<A_LOOP>();                              // 取 A → A.OnInit 取 B → B.OnInit 取 A → 环
                }
                catch (InvalidOperationException e) { Ui("已拦下：" + e.Message.Split('\n')[0]); }
            }

            // ⑥ 释放：释放后再取服务会明确报错（而不是给你一个已释放的对象）
            if (GUILayout.Button("⑥ 释放作用域后再取（演示报错）"))
            {
                if (_battleScope == null) { Ui("请先点 ④CreateScope。"); return; }
                _battleScope.Dispose();
                try { _battleScope.GetRequired<IDemoBattle>(); }
                catch (InvalidOperationException e) { Ui("已拦下：" + e.Message.Split('\n')[0]); }
                _battleScope = null;
            }

            GUILayout.Space(6);
            if (GUILayout.Button("清空演示日志")) _ui.Clear();
            if (_root != null) GUILayout.Label(_root.ToString(), HintStyle());

            GUILayout.FlexibleSpace();
            GUILayout.EndArea();

            // ---------- 右侧：演示日志 ----------
            GUILayout.BeginArea(new Rect(Screen.width - 480, 10, 470, Screen.height - 20));
            GUILayout.Label("<b>演示步骤日志</b>", TitleStyle());
            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (string line in _ui) GUILayout.Label(line);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ---------- 循环依赖演示用的两个"坏服务" ----------
        private sealed class A_LOOP : Revolution.RevIServiceInit
        {
            public void OnInit(Revolution.RevIServiceLocator s) => s.GetRequired<B_LOOP>();   // ← A 等 B
        }
        private sealed class B_LOOP : Revolution.RevIServiceInit
        {
            public void OnInit(Revolution.RevIServiceLocator s) => s.GetRequired<A_LOOP>();   // ← B 等 A（环）
        }

        // ---------- 简单样式 ----------
        private static GUIStyle _title, _hint;
        private static GUIStyle TitleStyle() => _title ??= new GUIStyle(GUI.skin.label) { richText = true, fontSize = 15 };
        private static GUIStyle HintStyle() => _hint ??= new GUIStyle(GUI.skin.label) { richText = true, fontSize = 11, wordWrap = true };
    }
}
