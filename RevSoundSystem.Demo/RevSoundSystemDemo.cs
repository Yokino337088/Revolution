// ============================================================
// RevSoundSystemDemo.cs —— 音效系统开箱示例
//
// 位置：Assets\Revolution.Demo\RevSoundSystem.Demo\
//
// 【怎么用】打开配套场景 RevSoundSystemDemo.unity → 点 Play → 左侧按钮逐个点。
//
// 【本示例演示什么】
//   ① 目录注册：声音先按"名字 → 资源路径 + 分类"注册进目录，之后全程按名字播放；
//   ② 播放入口：Play（2D）/ PlayAt（3D 定位）/ PlayBgm（背景乐，带淡入 / 自动切歌）；
//   ③ 音量体系：主音量 + 分类音量（Sfx / Bgm 分开调）—— 设置界面里的音量滑条就是这两行；
//   ④ 静音与停止：Mute 一键静音（恢复后无缝继续）；StopAll 全停带淡出；
//   ⑤ 失败可见：播未注册 / 加载失败的名字会走 Failed 事件（带原因码），框架不静默。
//
// 【真实项目怎么"出声"（本示例演示注册与控制；出声只差一步）】
//   ① 在「Revolution.Tools/资源/RevAB 打包工具」里配置音效 / BGM 目录（写进团队共享配置，
//      并自动生成 Runtime 内部的路径常量 RevSoundPath.cs —— 目录写错编译不过）；
//   ② 启动期把声音注册进目录：RevSound.Register("sfx_hit", "sfx_hit.wav", RevSoundKind.Sfx)；
//   ③ 之后任何地方 RevSound.Play("sfx_hit") —— 音频由资源系统异步加载（缓存常驻，Unload 才还引用）。
// ============================================================
using System.Collections.Generic;
using UnityEngine;

namespace Revolution.Demo.Sound
{
    public sealed class RevSoundSystemDemo : MonoBehaviour
    {
        private readonly List<string> _ui = new List<string>();
        private Vector2 _scroll;

        private bool _bgmOn;
        private RevSoundHandle _lastHandle;       // 单句柄停止演示用

        private void Ui(string line)
        {
            _ui.Add(line);
            if (_ui.Count > 200) _ui.RemoveAt(0);
            _scroll.y = float.MaxValue;
        }

        private void Start()
        {
            // ---------- ① 目录注册（演示用假路径；真实项目注册到工程里的真实音频路径） ----------
            // Register(name, path, kind)：path 只做目录记录（播放走资源系统按目录常量 + 名字异步加载）
            RevSound.Register("sfx_high", "demo/路径仅演示/sfx_high.wav", RevSoundKind.Sfx);
            RevSound.Register("sfx_low", "demo/路径仅演示/sfx_low.wav", RevSoundKind.Sfx);
            RevSound.Register("bgm_demo", "demo/路径仅演示/bgm_demo.wav", RevSoundKind.Bgm);

            // ---------- 失败可见：播不出来的声音会走 Failed（原因码），订阅它就不会"为什么没声音"干瞪眼 ----------
            RevSound.Failed += (name, reason) => Ui($"[Failed] {name}：{reason}");

            Ui("音效演示就绪：已注册 3 个声音（演示路径，未接真实音频 → 播放会走 Failed 可见）。");
            Ui("本演示重点是目录制、音量体系与失败可见 —— 出声只差『RevAB 里配好音效目录 + 注册真实路径』一步。");
        }

        // ==================== OnGUI ====================

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 430, Screen.height - 20));
            GUILayout.Label("<b>RevSound 音效系统演示</b>", TitleStyle());

            // ① 按名字播放（2D）：不需要知道 clip 在哪、不需要挂 AudioSource
            if (GUILayout.Button("① Play（按名字，Sfx 分类）"))
            {
                _lastHandle = RevSound.Play("sfx_high");
                Ui("Play(sfx_high)：目录里有 → 框架异步加载音频（首次播放晚一点出声是设计内）；本示例路径是假的 → 看 Failed。");
            }

            // ② 3D 定位播放：声音从指定位置传来（距离衰减由 Set3DRange 控制）
            if (GUILayout.Button("② PlayAt：3D 定位播放"))
            {
                _lastHandle = RevSound.PlayAt("sfx_low", new Vector3(8f, 0f, 0f));
                Ui("PlayAt：声音从右侧传来（戴上耳机能分辨方位）。");
            }

            GUILayout.Space(6);

            // ③ 背景乐：PlayBgm / StopBgm（带淡入淡出；重复调用自动切歌）
            if (GUILayout.Button(_bgmOn ? "③b StopBgm（淡出停止背景乐）" : "③ PlayBgm（淡入背景乐）"))
            {
                if (_bgmOn) { RevSound.StopBgm(0.5f); Ui("背景乐淡出停止。"); }
                else { RevSound.PlayBgm("bgm_demo", 0.5f); Ui("PlayBgm：Bgm 与 Sfx 音量分开控制；再次调用自动切歌。"); }
                _bgmOn = !_bgmOn;
            }

            // 单句柄停止：只想停"这一个"声音时
            if (GUILayout.Button("③c Stop(句柄)：只停刚播的那个"))
            {
                if (!_lastHandle.IsEmpty) { RevSound.Stop(_lastHandle); Ui("已停掉句柄对应的声音。"); }
                else Ui("还没有播放过（句柄指向的播放结束后自动失效）。");
            }

            GUILayout.Space(6);

            // ④ 音量体系：主音量 + 分类音量
            RevSound.MasterVolume = GUILayout.HorizontalSlider(RevSound.MasterVolume, 0f, 1f);
            GUILayout.Label($"主音量：{RevSound.MasterVolume:F2}", HintStyle());
            float sfx = GUILayout.HorizontalSlider(RevSound.GetVolume(RevSoundKind.Sfx), 0f, 1f);
            if (!Mathf.Approximately(sfx, RevSound.GetVolume(RevSoundKind.Sfx))) RevSound.SetVolume(RevSoundKind.Sfx, sfx);
            GUILayout.Label($"Sfx 分类音量：{sfx:F2}（Bgm 同理）", HintStyle());

            if (GUILayout.Button(RevSound.Mute ? "④b 取消静音" : "④b 一键静音"))
            {
                RevSound.Mute = !RevSound.Mute;
                Ui(RevSound.Mute ? "已静音（播放照常走，只是不发声 —— 恢复后无缝继续）。" : "已取消静音。");
            }

            GUILayout.Space(6);

            // ⑤ 停止：全停（带淡出）—— 切场景时的标准动作
            if (GUILayout.Button("⑤ StopAll（全停，淡出 0.3 秒）"))
            {
                RevSound.StopAll(0.3f);
                _bgmOn = false;
                Ui("全部停止（切场景 / 回大厅时用）。");
            }

            // ⑥ 失败可见：播一个没注册过的名字
            if (GUILayout.Button("⑥ 播未注册的名字（演示 Failed 可见）"))
            {
                RevSound.Play("sfx_not_exist");
                Ui("已播一个不存在的名字：右侧会出现 [Failed]（UnknownName）—— 框架不静默。");
            }

            GUILayout.Space(6);
            if (GUILayout.Button("清空演示日志")) _ui.Clear();

            GUILayout.Label("原则：先注册后播放（目录制）；播放入口只有名字 —— 路径、AudioSource、生命周期框架全管。", HintStyle());
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
