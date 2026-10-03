// ============================================================
// RevSingletonDemo.cs —— 单例开箱示例
//
// 位置：Assets\Revolution.Demo\RevSingleton.Demo\
//
// 【两种单例，按"要不要 MonoBehaviour"选】
//   ① RevSingleton<T>      —— 纯 C# 单例（配置 / 数据 / 纯逻辑管理器）：惰性创建、线程安全。
//   ② RevSingletonAutoMono<T> —— 自动创建的组件单例（要跑协程 / 要 Update 的管理器）：
//      首次访问 Instance 自动创建隐藏宿主（DontDestroyOnLoad），全游戏常驻，场景里不用摆。
//
// 【怎么用】打开配套场景 RevSingletonDemo.unity → 点 Play → 左侧按钮逐个点。
//
// 【本示例演示什么】
//   ① 纯 C# 单例：首次访问 Instance 时才创建（惰性）；多次访问是同一个实例；
//   ② 组件单例（AutoMono）：不摆场景、不手动 new，首次访问自动创建宿主并跨场景常驻；
//   ③ 惰性创建的收益：没被用到的管理器永远不会被创建（不会"一启动装了一堆系统"）。
//
// 【纪律提醒】单例是"全局可变状态"：新代码优先考虑依赖注入 / 服务定位器；
//   确实是"全局唯一才合理"的东西（音频总监 / 配置中心）再用单例，并且只读优先。
// ============================================================
using System.Collections.Generic;
using UnityEngine;

namespace Revolution.Demo.Singleton
{
    // ---------- ① 纯 C# 单例：全局配置中心 ----------
    public sealed class DemoGameConfig : Revolution.RevSingleton<DemoGameConfig>
    {
        // RevSingleton 的构造函数是 protected：只能通过 Instance 创建，外界 new 不出来
        public string Language = "zh-CN";
        public float MusicVolume = 0.8f;
        public int DailyResetHour = 5;

        /// <summary>演示方法：读一份配置摘要（业务直接 DemoGameConfig.Instance.Xxx）。</summary>
        public string Summary() => $"语言={Language}　音乐音量={MusicVolume:F2}　每日刷新={DailyResetHour}:00";
    }

    // ---------- ② 组件单例：全局音频总监（要跑协程 / 要每帧做混音策略） ----------
    public sealed class DemoAudioDirector : Revolution.RevSingletonAutoMono<DemoAudioDirector>
    {
        private int _played;
        protected override void OnInit() { /* 只初始化一次：宿主创建时调用 */ }

        public string PlayBeep()
        {
            _played++;
            return $"播了一声（累计 {_played} 次）";
        }
    }

    // ==================== 演示入口 ====================

    public sealed class RevSingletonDemo : MonoBehaviour
    {
        private readonly List<string> _ui = new List<string>();
        private Vector2 _scroll;

        private void Ui(string line)
        {
            _ui.Add(line);
            if (_ui.Count > 200) _ui.RemoveAt(0);
            _scroll.y = float.MaxValue;
        }

        private void Start() => Ui("单例演示就绪。左侧按钮逐个点 —— 注意【首次访问才创建】的时机。");

        // ==================== OnGUI ====================

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 430, Screen.height - 20));
            GUILayout.Label("<b>RevSingleton 单例演示</b>", TitleStyle());

            // ① 纯 C# 单例：惰性创建
            if (GUILayout.Button("① 第一次访问 DemoGameConfig.Instance"))
            {
                Ui($"拿到实例：{DemoGameConfig.Instance.Summary()}");
                Ui("首次访问才创建（惰性）—— 在此之前这个管理器在内存里根本不存在。");
            }
            if (GUILayout.Button("①b 再访问一次（应该是同一个实例）"))
            {
                bool same = ReferenceEquals(DemoGameConfig.Instance, DemoGameConfig.Instance);
                Ui($"两次 ReferenceEquals = {same}（全局只有这一份，改了它全工程可见）。");
            }

            GUILayout.Space(6);

            // ② 组件单例（AutoMono）：自动创建宿主、跨场景常驻
            if (GUILayout.Button("② 访问 DemoAudioDirector.Instance（自动创建宿主）"))
            {
                Ui(DemoAudioDirector.Instance.PlayBeep());
                var go = DemoAudioDirector.Instance.gameObject;
                Ui($"宿主：{go.name}（DontDestroyOnLoad：切场景不销毁；场景里从头到尾都不用摆它）。");
            }
            if (GUILayout.Button("②b 再播一声（同一个宿主）"))
            {
                Ui(DemoAudioDirector.Instance.PlayBeep());
                Ui("与上一次是同一个组件 —— 宿主只建一次，状态跨场景保留。");
            }

            GUILayout.Space(6);

            // ③ 使用建议
            if (GUILayout.Button("③ 什么时候该用单例？（提示）"))
            {
                Ui("✔ 适合：全局唯一才合理的东西（音频总监 / 配置中心 / 时间系统）；");
                Ui("✘ 不适合：'方便全局访问'的业务对象 —— 那会用 RevServiceLocator / 参数传递（单例是全局可变状态）。");
            }

            GUILayout.Space(6);
            if (GUILayout.Button("清空演示日志")) _ui.Clear();

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
