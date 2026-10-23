// ============================================================
// RevResourceSystemDemo.cs —— 资源加载系统开箱示例（编辑器直读模式）
//
// 位置：Assets\Revolution.Demo\RevResourceSystem.Demo\
//
// 【它解决什么】
//   "从哪读、怎么缓存、何时卸载" 全部收进资源系统：业务只写逻辑路径（如 "Data/Hero"），
//   编辑器直读工程、真机走 AB，业务代码一行不改；引用计数保证"都释放了才真卸载"。
//
// 【怎么用】打开配套场景 RevResourceSystemDemo.unity → 点 Play → 左侧按钮逐个点。
//   本示例自带一个演示文本 demo_data.txt（就在本目录），用编辑器直读模式加载它。
//
// 【本示例演示什么】
//   ① 资源根目录：编辑器直读按 <资源根目录>/<逻辑路径> 找文件 —— 未配置时演示一键指向本目录
//      （本演示的 demo_data.txt 就在根目录下，所以"逻辑根目录"传空字符串 —— 见 DemoLogicalRoot）；
//   ② Load<T>：同步加载（编辑器直读 / 已缓存时有效；真机首次用 LoadAsync）；
//   ③ 缓存：同一资源只读一次盘（第二次 Load 直接命中缓存）；
//   ④ 引用计数：Release 归零后才真正卸载 —— "表/资源被提前卸掉"这类事故的解药；
//   ⑤ 句柄：RevResHandle 拿内容 / 查状态 / 查失败原因。
// ============================================================
using System.Collections.Generic;
using UnityEngine;

namespace Revolution.Demo.ResourceSystem
{
    public sealed class RevResourceSystemDemo : MonoBehaviour
    {
        private readonly List<string> _ui = new List<string>();
        private Vector2 _scroll;

        /// <summary>本演示的资源根目录（demo_data.txt 就在这里；用 '/' 结尾）。</summary>
        private const string DemoRoot = "Assets/Revolution.Demo/RevResourceSystem.Demo/";

        /// <summary>
        /// 加载用的**逻辑根目录** —— ★ 它是"相对资源根目录"的逻辑段，不是工程全路径。
        /// demo_data.txt 就躺在资源根目录下（没有子目录），所以逻辑根传空字符串。
        /// （编辑器直读按「资源根目录 + 逻辑路径」找文件；这里若写 "Assets/…" 会拼成 Assets/…/Assets/… 而找不到。）
        /// </summary>
        private const string DemoLogicalRoot = "";

        private void Ui(string line)
        {
            _ui.Add(line);
            if (_ui.Count > 200) _ui.RemoveAt(0);
            _scroll.y = float.MaxValue;
        }

        private void Start()
        {
            string root = Revolution.RevEditorResPolicy.ResRoot;
            Ui(root.Length == 0
                ? "资源根目录尚未配置。点下面第一个按钮，把根目录指到本演示目录（真实项目里在 RevAB 打包工具里设置）。"
                : $"资源根目录：{root}");
        }

        // ==================== OnGUI ====================

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 450, Screen.height - 20));
            GUILayout.Label("<b>RevResourceSystem 资源加载演示</b>", TitleStyle());

            string root = Revolution.RevEditorResPolicy.ResRoot;
            GUILayout.Label($"资源根目录：{(root.Length == 0 ? "（未配置）" : root)}", HintStyle());

            // ① 配置资源根目录（编辑器直读按 <根>/<逻辑路径> 找文件）
            if (GUILayout.Button("① 把资源根目录指向本演示目录"))
            {
                Revolution.RevEditorResPolicy.ResRoot = DemoRoot;
                Ui($"已设置 ResRoot = {DemoRoot}（真实项目在 RevAB 打包工具里设置，打包与真机读的是同一份配置）。");
            }

            GUILayout.Space(6);

            // ② Load<T>：同步加载文本资源
            if (GUILayout.Button("② Load：加载 demo_data.txt"))
            {
                if (root.Length == 0) { Ui("请先配置资源根目录（上面第一个按钮）。"); return; }

                var handle = Revolution.RevResManager.Load(DemoLogicalRoot, "demo_data", typeof(TextAsset));
                if (handle != null && handle.IsLoaded)
                {
                    string text = (handle.Content as TextAsset)?.text ?? "";
                    Ui($"加载成功 ✓ 内容前 60 字：{text.Substring(0, Mathf.Min(60, text.Length))}…");
                }
                else
                {
                    Ui($"加载失败：{(handle == null ? "null" : handle.ErrorReason.ToString())}（检查文件与根目录配置）");
                }
            }

            // ③ 缓存：同一资源第二次 Load 直接命中（不再读盘）
            if (GUILayout.Button("③ 再 Load 一次（命中缓存，引用计数 +1）"))
            {
                Revolution.RevResManager.Load(DemoLogicalRoot, "demo_data", typeof(TextAsset));
                int refs = Revolution.RevResManager.GetRefCount(DemoLogicalRoot, "demo_data");
                Ui($"命中缓存 ✓ 当前引用计数 = {refs}（每次 Load / AddRef +1）。全库缓存句柄 {RevResManager.CachedCount} 个。");
            }

            // ④ 引用计数：Release 归零才真正卸载
            if (GUILayout.Button("④ Release：释放一次（计数 -1）"))
            {
                int before = Revolution.RevResManager.GetRefCount(DemoLogicalRoot, "demo_data");
                Revolution.RevResManager.Release(DemoLogicalRoot, "demo_data");
                int after = Revolution.RevResManager.GetRefCount(DemoLogicalRoot, "demo_data");
                Ui(after > 0
                    ? $"引用计数 {before} → {after}：还有人在用，不会真卸载（这就是『不会被提前卸掉』的保证）。"
                    : $"引用计数归零：资源已真正卸载（下次 Load 会重新读盘）。");
            }

            // ⑤ 句柄：拿到内容与状态
            if (GUILayout.Button("⑤ Get：拿句柄查状态"))
            {
                var handle = Revolution.RevResManager.Get(DemoLogicalRoot, "demo_data");
                Ui(handle == null
                    ? "当前没有这个资源的句柄（已卸载或从未加载）。"
                    : $"句柄状态：IsLoaded={handle.IsLoaded}，类型={handle.Content?.GetType().Name ?? "null"}");
            }

            GUILayout.Space(6);
            if (GUILayout.Button("清空演示日志")) _ui.Clear();

            GUILayout.Label("原则：业务只写逻辑路径（如 Data/Hero）；从哪读 / 怎么缓存 / 何时卸载由资源系统与策略决定。", HintStyle());
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
