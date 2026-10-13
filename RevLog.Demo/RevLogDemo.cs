// ============================================================
// RevLogDemo.cs —— 日志系统开箱示例
//
// 位置：Assets\Revolution.Demo\RevLog.Demo\
//
// 【怎么用】打开配套场景 RevLogDemo.unity → 点 Play → 屏幕左侧逐个点按钮，
//          右下角的滚动区会显示每一步的结果（本示例自带一个最小 OnGUI 日志区）。
//
// 【本示例演示什么】
//   ① 级别就是成本契约：Debug 正式包里被编译期删除（本工程没定义 REVLOG_DEBUG，所以点它没有输出）；
//   ② 五个级别的用法与去向（Info/Warn 便宜；Error 默认采堆栈；Exception 带异常对象）；
//   ③ 连续重复抑制：同一句连点多次只会留下"首条 + 重复 N 次"；
//   ④ 最低输出级别 MinLevel：调到 Warn 后 Info 就不再输出；
//   ⑤ 环形缓冲：RevLog.Dump(n) 随时拿"出事之前的现场"；
//   ⑥ tag 用法：按模块打标，配合 MuteTag 按模块静音。
//
// 【到哪里看输出】
//   Unity Console（由框架自动装好的控制台通道输出，tag = 你传的 tag）；
//   想落盘成文件：一行 RevLog.EnableFileLog(Application.persistentDataPath + "/revlog")。
// ============================================================
using UnityEngine;

namespace Revolution.Demo.Log
{
    public sealed class RevLogDemo : MonoBehaviour
    {
        // ---------- 演示状态 ----------
        private Vector2 _scroll;            // OnGUI 日志区的滚动位置
        private readonly System.Collections.Generic.List<string> _ui =
            new System.Collections.Generic.List<string>();   // 本示例自己的界面日志（与 RevLog 输出互不干扰）

        /// <summary>往界面日志区追加一行（Play 模式下 OnGUI 专用；不是 RevLog 的用法）。</summary>
        private void Ui(string line)
        {
            _ui.Add($"[{Time.frameCount}] {line}");
            if (_ui.Count > 200) _ui.RemoveAt(0);          // 界面日志也有上限，防止越积越多
            _scroll.y = float.MaxValue;                     // 自动滚到最底
        }

        private void Start()
        {
            Ui("日志系统演示就绪。左侧按钮逐个点，输出看 Unity Console（tag = Demo）。");
            Ui($"当前最低输出级别 MinLevel = {RevLog.MinLevel}；环形缓冲 {RevLog.Count} 条。");
        }

        // ==================== OnGUI：按钮 + 日志区 ====================

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 380, Screen.height - 20));

            GUILayout.Label("<b>RevLog 日志系统演示</b>", TitleStyle());

            // ① 五个级别：注意 Debug 在正式包（没定义 REVLOG_DEBUG 宏）里连参数求值都不会发生
            if (GUILayout.Button("① Info —— 普通信息（正式包保留，不采堆栈）"))
            {
                RevLog.Info("登录成功（Info 示例）", "Demo");
                Ui("已打 Info：去 Console 看 tag=Demo 那条。");
            }

            if (GUILayout.Button("② Warn —— 可疑但能跑"))
            {
                RevLog.Warn("配置缺失，已用默认值（Warn 示例）", "Demo");
                Ui("已打 Warn：Console 里是黄色。");
            }

            if (GUILayout.Button("③ Error —— 必须修（默认采堆栈）"))
            {
                RevLog.Error("数据库连不上（Error 示例，自带堆栈）", "Demo");
                Ui("已打 Error：Console 里是红色，消息里带堆栈（只有 Error 及以上才付堆栈的成本）。");
            }

            if (GUILayout.Button("④ Exception —— 带异常对象（可触发上报）"))
            {
                // 第二个参数是给"人"看的补充说明；异常对象会原样交给 Console（可点击跳转）与 OnReport
                RevLog.Exception(new System.InvalidOperationException("演示用异常"), "战斗开始失败（Exception 示例）", "Demo");
                Ui("已打 Exception：Console 里可点击跳转；接了 RevLog.OnReport 就会上报（默认不上报）。");
            }

            if (GUILayout.Button("⑤ Debug —— 正式包里不存在（编译期删除）"))
            {
                // 下面这行在正式包里被 [Conditional] 整行删除：连 "x=" 的字符串拼接都不会发生。
                // 想在编辑器之外看到它：给工程加 REVLOG_DEBUG 宏。
                int x = 42;
                RevLog.Debug($"x={x}（Debug 示例：编辑器可见，正式包零成本）", "Demo");
                Ui("已打 Debug（编辑器可见）。注意：它不经过 MinLevel 过滤，由编译宏决定生死。");
            }

            GUILayout.Space(6);

            // ② 连续重复抑制：同一句连打多次，只留首条；换话时补一条"重复 N 次"
            if (GUILayout.Button("⑥ 重复抑制：连打 5 次同一句 Info"))
            {
                for (int i = 0; i < 5; i++) RevLog.Info("这条消息重复刷屏（重复抑制示例）", "Demo");
                Ui("连打了 5 条相同 Info：Console 只会出现首条；下一条不同日志出现时会补『重复 4 次』。");
            }

            // ③ MinLevel：运行期调阈值（Debug 例外，它由编译宏管）
            if (GUILayout.Button($"⑦ 切换 MinLevel（当前 {RevLog.MinLevel}）"))
            {
                RevLog.MinLevel = RevLog.MinLevel == RevLogLevel.Info ? RevLogLevel.Warn : RevLogLevel.Info;
                Ui($"MinLevel 已切到 {RevLog.MinLevel}：再点上面的 Info 按钮，低于阈值就不会输出（IsEnabled 也变 false）。");
            }

            GUILayout.Space(6);

            // ④ 环形缓冲：出事后拿"之前的现场"，不用提前开文件
            if (GUILayout.Button("⑧ Dump 最近 10 条（复制现场）"))
            {
                string dump = RevLog.Dump(10);
                Debug.Log("[Demo] RevLog.Dump(10)：\n" + dump);
                Ui($"已把最近 {RevLog.Count} 条里最近的 10 条打到 Console（缓冲共 {RevLog.Count} 条，硬顶 2048）。");
            }

            // ⑤ tag 静音：按模块关日志
            if (GUILayout.Button("⑨ MuteTag(\"Demo\") 静音 / 解除"))
            {
                bool muted = !RevLog.IsTagMuted("Demo");
                RevLog.MuteTag("Demo", muted);
                RevLog.Info(muted ? "这条你看不到（tag 已静音）" : "tag 已解除静音", "Demo");
                Ui(muted ? "tag=Demo 已静音：刚那条 Info 被筛掉了（筛掉的日志不进缓冲、不进通道）。"
                         : "tag=Demo 已解除：日志恢复正常输出。");
            }

            if (GUILayout.Button("⑩ 清空环形缓冲"))
            {
                RevLog.Clear();
                Ui("RevLog.Clear()：历史清空（输出通道不动）。");
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label($"Ring={RevLog.Count}  SinkErrors={RevLog.SinkErrors}（>0 说明某个输出通道在丢日志）", HintStyle());
            GUILayout.EndArea();

            // ---------- 右侧：本示例自己的界面日志 ----------
            GUILayout.BeginArea(new Rect(Screen.width - 460, 10, 450, Screen.height - 20));
            GUILayout.Label("<b>演示步骤日志（仅本示例的界面显示）</b>", TitleStyle());
            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (string line in _ui) GUILayout.Label(line);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ---------- 简单样式（本示例自用） ----------
        private static GUIStyle _title;
        private static GUIStyle _hint;
        private static GUIStyle TitleStyle()
        {
            if (_title == null) { _title = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 15 }; }
            return _title;
        }
        private static GUIStyle HintStyle()
        {
            if (_hint == null) { _hint = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 11, wordWrap = true }; }
            return _hint;
        }
    }
}
