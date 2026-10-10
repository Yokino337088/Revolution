// ============================================================
// RevHotUpdateDemo.cs —— 资源热更新开箱示例（用本机"假 CDN"就能跑通全链路）
//
// 位置：Assets\Revolution.Demo\RevHotUpdate.Demo\
//
// 【它解决什么】
//   "AB 包从远端下载 + 版本比对 + 差量下载 + 校验 + 换版本 + 加载路径重定向"这一整套，
//   业务侧真正要写的只有一行：
//       await RevHotUpdate.InitializeAsync(config);
//   而**加载资源的代码一行都不用改**（还是 RevResManager.Load / LoadAsync）——
//   这是本包相对第三方方案最主要的便利点。
//
// 【怎么用】四步（全程不需要真的云账号）
//   ① 编辑器菜单：Revolution.Tools / 热更新 / Demo / ① 一键：打包 + 清单 + 装配本地 CDN
//      —— 它会把 Assets/GameRes/RevHotDemo/revhot_demo.txt 的"构建序号"+1，
//         然后打 AB、生成热更清单、按远端目录约定把产物装配到 {工程根}/LocalCDN。
//   ② 双击本目录的 起本地CDN.cmd（默认 http://127.0.0.1:8000）—— 把 LocalCDN 当远端服务起来。
//   ③ 打开本场景点 Play →（面板上的）① 检查更新 → ② 执行更新 → ③ 加载演示资源。
//      "构建序号"变大 = 热更真的生效了。
//   ④ 想再看一轮：再跑一次菜单 ①（序号再 +1）→ Play 里点 ② → ③。
//
// 【本示例演示什么】
//   ① 差量：第二轮只会下"内容变了的那一个包"（日志会打印下了几个文件、多少字节）；
//   ② 覆盖式语义：持久化目录优先、没有就回退 StreamingAssets（首包基线）——
//      所以"首装 / 清数据 / 临时关热更"都不会崩，这也是本包最关键的一条设计（见《架构解析》）；
//   ③ 大版本锚定：清单里的 @appVersion 必须等于客户端的 Application.version，否则拒绝更新（防串版本）；
//   ④ 进度与取消：热更的进度走 Progress 事件（真实项目就是拿它做更新界面）；取消后已下载内容保留。
//
// 【两个最容易踩的坑（本文件已在代码里处理）】
//   ① 编辑器默认是"AssetDatabase 直读工程资源"，热更下来的包根本不会被读到
//      → Start() 里自动打开 RevResBootstrap.UseABInEditor；
//   ② 资源系统有缓存 + 引用计数：不先 Release 再 Load 的话，你会一直看到上一次的内容
//      → ③ 按钮先 Release 再 LoadAsync（面板里 ⑤ 还能一键卸载全部，模拟重启）。
// ============================================================
using System;
using System.Collections.Generic;
using Revolution;
using Revolution.HotUpdate;
using UnityEngine;

namespace Revolution.Demo.HotUpdate
{
    /// <summary>热更演示面板（OnGUI：左侧按钮 + 右侧步骤日志）。</summary>
    public sealed class RevHotUpdateDemo : MonoBehaviour
    {
        // ---------- 演示资源：逻辑路径拆两段（分组 + 名字），与 ResMap.txt 的"逻辑名"对应 ----------
        //   ResMap.txt 里那一行是：  RevHotDemo/revhot_demo|revhotdemo|revhot_demo
        //                            └ 逻辑名（两段拼起来）  └ 包名     └ 包内资源名
        private const string DemoResGroup = "RevHotDemo";
        private const string DemoResName = "revhot_demo";

        private readonly List<string> _ui = new List<string>();
        private Vector2 _scroll;

        /// <summary>远端根：默认对准同目录 起本地CDN.cmd 打印的地址（换端口时这里也要改）。</summary>
        private string _remoteRoot = "http://127.0.0.1:8000";

        private RevHotCheckResult _check;                 // ① 的结果，② 要用它（先检查、再更新）
        private RevCancellationTokenSource _cts;          // 取消用（演示"下到一半不想下了"）
        private bool _busy;
        private float _progress;                          // 0~1，画进度条
        private string _progressLine = "";

        // ============================================================
        // 生命周期
        // ============================================================

        private void Start()
        {
#if UNITY_EDITOR
            // ★ 坑①：编辑器下默认只注册"编辑器直读"策略 —— 资源一律从工程文件读，
            //   热更下来的 AB 包永远不会被用到（表现为"更新成功了但内容没变"）。
            //   这个开关等价于菜单 Revolution.Tools / 资源 / AB 加载模式（编辑器）。
            if (RevResBootstrap.UseABInEditor == false)
            {
                RevResBootstrap.UseABInEditor = true;
                Ui("已自动开启『AB 加载模式（编辑器）』—— 否则加载走 AssetDatabase 直读，看不到热更效果。");
            }
#endif
            Ui("三步准备：① 菜单 Revolution.Tools / 热更新 / Demo / ① 一键：打包 + 清单 + 装配本地 CDN");
            Ui("　　　　　② 双击同目录 起本地CDN.cmd（默认 8000 端口）");
            Ui("　　　　　③ 本面板：① 检查更新 → ② 执行更新 → ③ 加载演示资源");
            Ui($"当前：大版本 {Application.version}　本地资源版本 {ShowVersion(RevHotUpdate.LocalResVersion)}　状态 {RevHotUpdate.State}");
            Ui("提示：每跑一次菜单 ①，演示资源的\"构建序号\"都会 +1，所以每次都能看到一轮真实的差量更新。");
        }

        private void OnEnable()
        {
            // 进度既能用回调（CheckAsync/UpdateAsync 的参数），也能订阅这个全局事件 —— demo 用事件画进度条。
            RevHotUpdate.Progress += OnProgress;
        }

        private void OnDisable()
        {
            // ★ 订阅了就必须退订：MonoBehaviour 被销毁后事件还握着它 = 空引用 / 内存泄漏的经典来源。
            RevHotUpdate.Progress -= OnProgress;
        }

        // ============================================================
        // 配置与进度
        // ============================================================

        /// <summary>构造一份"最小可用"配置（每个字段的真实项目取值见技术方案第四章）。</summary>
        private RevHotConfig BuildConfig()
        {
            return new RevHotConfig
            {
                RemoteRoot = _remoteRoot,

                // ★ 本机假 CDN 是 http://，必须显式允许；真实项目用 https 时**不要**打开它。
                AllowHttp = true,

                LogTag = "HotUpdateDemo",
                VerifyMode = RevHotVerifyMode.Hash,       // 校验 = 尺寸 + SHA-256（下载期，坏包绝不进版本目录）
                Concurrency = 3,                          // 手机上别超过 4
                TimeoutSeconds = 30,
                RetryCount = 3,                           // 指数退避：0.5s / 1s / 2s

                // 大版本不匹配时不会硬更，而是回调给业务决定（跳商店 / 公告 / 整包下载）
                OnForceUpdateRequired = info => Ui($"需要更新客户端：{info.Message}（{info.CurrentAppVersion} → {info.RequiredAppVersion}）"),
            };
        }

        private void OnProgress(RevHotProgress p)
        {
            _progress = Mathf.Clamp01((float)(p.Percent / 100.0));
            _progressLine = $"{p.State} {p.Percent:F0}%　{RevHotProgress.FormatBytes(p.DownloadedBytes)}/{RevHotProgress.FormatBytes(p.TotalBytes)}　{p.Text}";
        }

        // ============================================================
        // ① 检查更新（只拉清单 + 算差量，不下包）
        // ============================================================

        private async void ClickCheck()
        {
            if (GuardBusy()) return;

            Begin("① 检查更新（只拉清单 + 算差量，不下任何包）");
            try
            {
                _check = await RevHotUpdate.CheckAsync(BuildConfig(), OnProgress, _cts.Token);

                if (_check.Success == false)
                {
                    Ui($"检查失败：{_check.Error}");        // RevHotError 已经是"[错误码] 人话原因"的格式
                    Ui("排查顺序：CDN 起了吗 → RemoteRoot 对吗 → 菜单 ① 跑过吗（清单在 {平台}/{大版本}/RevHotManifest.txt）");
                }
                else if (_check.ForceUpdateRequired)
                {
                    Ui($"需要更新客户端：{_check.ForceUpdate.Message}（资源层保持出包基线可用，不会崩）");
                }
                else if (_check.HasUpdate == false)
                {
                    Ui($"已是最新：本地 {ShowVersion(_check.LocalResVersion)}　远端 {_check.RemoteResVersion}（没有要下载的内容）");
                }
                else
                {
                    Ui($"发现新版本 {_check.RemoteResVersion}：新增 {_check.AddedCount} 个包、变更 {_check.ChangedCount} 个包，合计 {_check.TotalBytesText}");
                    Ui("→ 接着点 ② 执行更新；只有这里列出的变化量才会被下载（这就是\"差量\"）。");
                }
            }
            catch (Exception e)
            {
                Ui($"检查过程出现异常：{e.Message}（热更的异常都会在 await 处抛出，不会静默）");
            }
            finally
            {
                End();
            }
        }

        // ============================================================
        // ② 执行更新（下载 → 校验 → 原子落盘 → 切版本 → 重装资源系统）
        // ============================================================

        private async void ClickUpdate()
        {
            if (GuardBusy()) return;

            if (_check == null || _check.Success == false)
            {
                Ui("② 需要 ① 的成功结果（先检查、再更新）—— 这也是 UpdateAsync 的硬要求。");
                return;
            }

            Begin("② 执行更新（下载 → 校验 → 原子落盘 → 切版本 → 重装资源系统）");
            try
            {
                RevHotUpdateResult result = await RevHotUpdate.UpdateAsync(_check, OnProgress, _cts.Token);
                ReportResult(result);

                if (result.Success)
                {
                    _check = null;                       // 这一轮的差量已经用掉了，下一轮重新 Check
                    Ui("→ 点 ③ 加载演示资源：里面的\"构建序号\"应该变大了（那就是热更生效）。");
                }
            }
            catch (Exception e)
            {
                Ui($"更新过程出现异常：{e.Message}");
            }
            finally
            {
                End();
            }
        }

        // ============================================================
        // ②' 一键 Initialize（检查 + 更新 + 重装，最省事的用法）
        // ============================================================

        private async void ClickInitialize()
        {
            if (GuardBusy()) return;

            Begin("②' 一键 Initialize（= 检查 + 更新 + 重装资源系统；启动流程里推荐就这一行）");
            try
            {
                RevHotUpdateResult result = await RevHotUpdate.InitializeAsync(BuildConfig(), OnProgress, _cts.Token);
                ReportResult(result);
            }
            catch (Exception e)
            {
                Ui($"初始化出现异常：{e.Message}");
            }
            finally
            {
                End();
            }
        }

        // ============================================================
        // ③ 加载演示资源（先 Release 再 Load —— 不然你看到的还是上一次的缓存）
        // ============================================================

        private void ClickLoadAsset()
        {
            // ★ 坑②：资源系统带缓存 + 引用计数。更新完不 Release 就 Load，会直接命中旧缓存。
            RevResManager.Release(DemoResGroup, DemoResName);
            Ui($"—— ③ 加载 {DemoResGroup}/{DemoResName}（已先 Release，确保这次真的从包里读）——");

            RevResManager.LoadAsync(DemoResGroup, DemoResName, typeof(TextAsset), OnAssetLoaded);
        }

        private void OnAssetLoaded(RevResHandle handle)
        {
            if (handle == null)
            {
                Ui("加载失败：句柄为 null（资源系统还没 Init？路径非法？）");
                return;
            }

            TextAsset text = handle.Content as TextAsset;
            if (handle.IsLoaded == false || text == null)
            {
                Ui($"加载失败：{handle.ErrorReason}");
                Ui($"排查：ResMap.txt 里有没有 {DemoResGroup}/{DemoResName} 这一行；AB 模式开了吗；本地包的 hash 与清单一致吗");
                return;
            }

            Ui($"加载成功 ✓ 包里的内容：{text.text.Replace("\r", "").Replace("\n", "　/　")}");
            Ui("↑ \"构建序号\"比上一次大 = 热更生效（这段文字来自 AB 包，不是工程里的那份原文件）。");
        }

        // ============================================================
        // ④ 状态与诊断 / ⑤ 卸载全部
        // ============================================================

        private void ClickPrintState()
        {
            Ui("—— ④ 状态与诊断 ——");
            Ui($"状态：{RevHotUpdate.State}　本地资源版本：{ShowVersion(RevHotUpdate.LocalResVersion)}");

            RevHotError error = RevHotUpdate.LastError;
            Ui($"最近错误：{(error == null || error.Code == RevHotErrorCode.None ? "（无）" : error.ToString())}");

            // Dump() 是给排查用的多行快照：状态 / 本地版本 / 最近错误 / 本地目录（含各版本目录）
            foreach (string line in RevHotUpdate.Dump().Split('\n'))
            {
                Ui(line.TrimEnd('\r'));
            }
        }

        private void ClickUnloadAll()
        {
            RevResManager.UnloadAll();
            Ui("已卸载全部资源与包（模拟重启；下次加载会重新读包）。");
        }

        // ============================================================
        // 小工具
        // ============================================================

        private void Begin(string title)
        {
            _busy = true;
            _progress = 0f;
            _cts = new RevCancellationTokenSource();
            Ui("————————————————————————");
            Ui(title);
        }

        private void End()
        {
            _busy = false;
            _cts = null;
            _progressLine = "";
            _progress = 0f;
        }

        private bool GuardBusy()
        {
            if (_busy)
            {
                Ui("上一轮还在跑 —— 稍等，或点\"取消\"。");
                return true;
            }
            return false;
        }

        private void ReportResult(RevHotUpdateResult result)
        {
            if (result.Success)
            {
                Ui($"成功 ✓ {result.Message}（耗时 {result.ElapsedSeconds:F1}s）");
                Ui($"当前生效资源版本：{ShowVersion(result.ResVersion)}");
            }
            else
            {
                Ui($"失败：{result.Error}");
                Ui("失败不一定等于\"坏了\"：网络失败会保留已完成的部分，下次点 ② 会从断点接着下。");
            }
        }

        private static string ShowVersion(string version)
        {
            return string.IsNullOrEmpty(version) ? "（无 = 还没热更过，用的是出包基线）" : version;
        }

        private void Ui(string line)
        {
            _ui.Add(line);
            if (_ui.Count > 400) _ui.RemoveAt(0);
            _scroll.y = float.MaxValue;      // 自动滚到最新
        }

        // ============================================================
        // 界面（左侧操作 + 右侧步骤日志）
        // ============================================================

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 520, Screen.height - 20));
            GUILayout.Label("<b>RevHotUpdate 资源热更演示</b>", TitleStyle());
            GUILayout.Label("远端根（对准 起本地CDN.cmd 打印的地址）：", HintStyle());
            _remoteRoot = GUILayout.TextField(_remoteRoot);

            GUILayout.Label($"状态 {RevHotUpdate.State}　本地资源版本 {ShowVersion(RevHotUpdate.LocalResVersion)}", HintStyle());
            if (_progressLine.Length > 0) GUILayout.Label(_progressLine, HintStyle());
            DrawProgressBar();

            bool oldEnabled = GUI.enabled;

            GUI.enabled = oldEnabled && _busy == false;
            if (GUILayout.Button("① 检查更新（拉清单 + 算差量，不下包）")) ClickCheck();
            if (GUILayout.Button("② 执行更新（下差量 → 校验 → 切版本）")) ClickUpdate();
            if (GUILayout.Button("②' 一键 Initialize（检查 + 更新，启动流程推荐）")) ClickInitialize();

            GUILayout.Space(4);
            if (GUILayout.Button("③ 加载演示资源（看\"构建序号\"有没有变大）")) ClickLoadAsset();

            GUILayout.Space(4);
            if (GUILayout.Button("④ 状态与诊断（State / LastError / Dump）")) ClickPrintState();
            if (GUILayout.Button("⑤ 卸载全部资源（模拟重启）")) ClickUnloadAll();

            GUI.enabled = oldEnabled;

            if (_busy)
            {
                GUI.enabled = oldEnabled && _cts != null;
                if (GUILayout.Button("取消当前这轮")) _cts.Cancel();
                GUI.enabled = oldEnabled;
            }

            if (GUILayout.Button("清空日志")) _ui.Clear();

            GUILayout.Space(6);
            GUILayout.Label("约定：加载代码不用改 —— 还是 RevResManager.LoadAsync(分组, 名字, 类型, 回调)。", HintStyle());
            GUILayout.Label("热更只改\"包从哪来\"：持久化版本目录优先，没有就回退 StreamingAssets 首包。", HintStyle());
            GUILayout.EndArea();

            // ---------- 右侧：步骤日志 ----------
            GUILayout.BeginArea(new Rect(Screen.width - 560, 10, 550, Screen.height - 20));
            GUILayout.Label("<b>演示步骤日志</b>", TitleStyle());
            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (string line in _ui) GUILayout.Label(line);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        /// <summary>自绘进度条（GUI.ProgressBar 是编辑器专用 API，运行时不能用）。</summary>
        private void DrawProgressBar()
        {
            Rect bar = GUILayoutUtility.GetRect(1f, 6f, GUILayout.ExpandWidth(true));
            GUI.color = new Color(1f, 1f, 1f, 0.15f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = new Color(0.25f, 0.8f, 0.45f, 1f);
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * _progress, bar.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        // ---------- 简单样式 ----------
        private static GUIStyle _title, _hint;
        private static GUIStyle TitleStyle() => _title ??= new GUIStyle(GUI.skin.label) { richText = true, fontSize = 15 };
        private static GUIStyle HintStyle() => _hint ??= new GUIStyle(GUI.skin.label) { richText = true, fontSize = 12, wordWrap = true };
    }
}
