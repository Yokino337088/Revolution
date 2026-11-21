# WebGL 构建与交付

## 先定位故障所在的进程

| 证据 | 处理 |
| --- | --- |
| 浏览器报 `Build/*.data.br` 下载失败，页面地址为 `file://` | Unity 网页加载器在引擎启动前就被浏览器拦下。用项目的本地 HTTP 启动器、Unity Build And Run 或 HTTPS 静态站提供完整构建目录。项目模板可在加载器启动前提示正确入口 |
| HTTP 请求成功，仍报 Brotli、Wasm 解码或缺文件 | 查 Network 的实际响应。压缩文件带 `Content-Encoding: br`，`*.wasm.br` 带 `Content-Type: application/wasm`，`*.js.br` 带 JavaScript 类型。核对 404 响应体、跨域 CORS、重定向和缓存是否混用了两版产物 |
| Windows 的 Emscripten 报 `UnicodeDecodeError: 'gbk'`，随后 `llvm-nm` 相关 `IndexError` | 查 Unity 自带 Python、子进程环境和编译日志中的第一条异常。框架 `build.player` 对 Windows WebGL 构建设置 `PYTHONUTF8=1`，在 `finally` 恢复。直用其它构建入口时按进程传递，不改机器全局环境 |
| `build.target` 在内存工单报 `jobs/<id>/target-started` | 老版本把磁盘标记当成内存工单前提。同步用 `SessionState` 持有目标切换状态的版本，等重载后查同一工单和 `build.settings` |
| RevCLI 等待超时，Unity Editor 仍在构建 | 保留原工单。长构建用 `build player … --detach`，再以足够的全局 `--timeout` 调用 `job <id> --wait`。以 Unity 的最终 BuildReport、目标、输出和错误数为准 |
| Release 编译报 Editor、预览或 GM 专用方法缺失 | Player 需要的逻辑放入正式程序集和正式门面；工具注册与开发控制面保持宏保护。WebGL Release、Development 和 Editor 各用自己的编译参数 |

资料见 [Unity 2022.3 WebGL 部署](https://docs.unity3d.com/2022.3/Documentation/Manual/webgl-deploying.html)。

## 资源、下载和宿主隔离

- 用同一目标构建 Player 和 AB，核对 `StreamingAssets/WebGL/WebGL` 主包、ResMap、依赖及构建清单。Unity 会复制 StreamingAssets 的全部内容，发布目录需要明确保留该产品用到的平台目录与共享文件。
- 浏览器和小游戏使用异步 AB 加载。整个加载器 `ReleaseAll` 后，旧请求停止重试并在下一次帧检查时中止。已完成的旧包由原请求归还，单个消费者取消由其作用域清理。下载时限使用实时时钟，错误记录包名、传输结果和 HTTP 状态码。
- 可选 `RevHotUpdate` 沿相同的 WebGL 平台名与小游戏宏。网页清单、映射表和候选 AB 以真实帧等待，有取消、实时时限和 `Abort`，候选验证失败时保留生效版本。CDN 另核对 HTTPS、CORS、版本化 URL 与缓存策略。
- 使用 `UNITY_EDITOR` 区分运行宿主与活动构建目标。WebGL 与已接入小游戏的 Player 跳过后台文件日志与本机 `HttpListener`，在线桌面 Editor 仍可用 RevCLI。浏览器上的自动化需要项目支持的网页侧传输。异步用 RevTask，线程、Socket 和文件 I/O 按平台实际能力分支。见 [Unity 2022.3 WebGL 技术限制](https://docs.unity3d.com/2022.3/Documentation/Manual/webgl-technical-overview.html)。

## 输入、音频和持久化

- 键位先核对实际目标版本的 Unity 枚举。Unity 2022.3 的 `RevKey.PrintScreen` 映射到 `KeyCode.Print`，公开枚举及已有存档的值继续沿用。检查 canvas 获焦后键盘输入，以及文本框拥有焦点时的输入归属。
- 网页默认铺满视口。浏览器原生全屏要在真实用户手势内请求，处理拒绝和退出，并保留入口。音频也有手势门槛，首个点击同时核对解锁。WebGL 长 BGM 可用 `CompressedInMemory`，短音效按时序、内存和 AB 实际解码选导入方式。见 [Fullscreen 标准](https://fullscreen.spec.whatwg.org/#dom-element-requestfullscreen)、[Unity 2022.3 WebGL 音频](https://docs.unity3d.com/2022.3/Documentation/Manual/webgl-audio.html)。
- 浏览器 `Application.persistentDataPath` 由 IDBFS 支持。使用 `RevFileSave` 写主档与备份成功后，网页插件请求 IndexedDB 异步写回。记录存档时区分虚拟文件提交与浏览器事务完成。验证重开用同一 origin 和页面目录，存档即时刷新与配额拒绝另做针对性检查。见 [Unity persistentDataPath](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Application-persistentDataPath.html)、[Emscripten FS.syncfs](https://emscripten.org/docs/api_reference/Filesystem-API.html#FS.syncfs)。

## 交付记录

按任务范围分别记录 WebGL Player 构建、AB 清单、HTTP 预检、编译与浏览器玩法的证据。框架改动在源仓库通过对应流程导出后，以正式 `package` 的 SHA 同步项目，逐项核对快照。可选扩展以其独立导出为准。构建结束后恢复本次约定的开发目标。
