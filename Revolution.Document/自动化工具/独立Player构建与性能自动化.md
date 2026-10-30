# 独立Player构建与性能自动化

2026-10-31。适用于Unity 2022.3、Windows x64；CLI/MCP负责构建与调度，游戏内面板只消费同一份运行时采样。本文的手机/WebGL部分是边界说明，不代表已经支持或验收。

## 1. 两种测试包

开发诊断包：IL2CPP + development=true，关闭Script Debugging、Deep Profiling与自动连接Profiler，用于完整指标和功能回归。

性能验收包：IL2CPP + development=false + automation=true + frameTiming=true。本次构建通过extraScriptingDefines加入REV_AUTOMATION，不修改项目永久宏。它是受控内部测试包，不是公开发行包。项目自己的GM、隔离接线、调试面板也必须允许REV_AUTOMATION；只给框架加宏不会让项目的UNITY_EDITOR代码进入Player。

正式发行：automation=false、development=false，且项目永久宏不含REV_AUTOMATION。发布前核对实际能力不存在，不能只靠面板隐藏。

## 2. AI/CLI打包到指定路径

使用Python 3.10+。已有编辑器使用live，不启动第二个Unity争用工程。先确认目标平台支持、Windows IL2CPP模块、Visual C++工具链、启用场景与真实AB资源均就绪。

```powershell
py -3.13 Revolution.RevCLI/rev.py --project D:/Game --mode live --timeout 3600 build player --target StandaloneWindows64 --path D:/Builds/Game/20261031-01/Game.exe --backend il2cpp --automation --frame-timing
```

开发诊断包再加 --development。backend支持current/mono/il2cpp。target须与活动平台一致，否则先调用build.target并等待重载完成。build.player不负责生成项目AB，项目构建管线须在Player之前完成依赖校验和AB构建。

输出必须是专用空目录下的完整路径。允许工程外目录，拒绝覆盖旧包、目录链接及工程Assets/Packages/ProjectSettings/Library/Temp等目录。不要为重试删除用户旧包，另选新目录。

返回真实BuildReport：result、errors、warnings、seconds、bytes、backend、buildId。仅Succeeded且errors=0为成功；输出旁写rev-build.json，包含构建身份、不含凭据。backend、Frame Timing Stats、IL2CPP编译配置在finally恢复；编辑器崩溃时不承诺finally执行，应核对项目设置后继续。

MCP调用：

```json
{"target":"editor","action":"build.player","arguments":{"target":"StandaloneWindows64","path":"D:/Builds/Game/20261031-01/Game.exe","backend":"il2cpp","development":false,"automation":true,"frameTiming":true},"intent":"构建独立IL2CPP性能测试包"}
```

构建耗时可能数分钟，客户端使用足够timeout。超时不代表构建没有发生，应读取同一工单和BuildReport，不盲目重复构建。

## 3. 启动、连接、退出

```powershell
py -3.13 Revolution.RevCLI/rev.py player launch --executable D:/Builds/Game/20261031-01/Game.exe --directory D:/Evidence/Game/session-01 --port 18766
py -3.13 Revolution.RevMCP/rev_mcp.py --runtime-endpoint D:/Evidence/Game/session-01/runtime.json --reports D:/Evidence/Game/mcp serve
py -3.13 Revolution.RevCLI/rev.py player status --directory D:/Evidence/Game/session-01
py -3.13 Revolution.RevCLI/rev.py player stop --directory D:/Evidence/Game/session-01
```

launcher首版只支持本机Windows exe。directory每次用新的空目录。可通过--arguments传JSON字符串数组，例如窗口宽高；不允许覆盖保留的自动化、日志参数或使用batchmode/nographics测渲染。

launch返回endpointPath、pid、sessionId、launchId、buildId等身份，不返回token。runtime.json含本机凭据，只放忽略目录，不入库、不粘贴到报告。启动失败只终止本次创建的子进程；stop通过校验过的HTTP会话调用runtime.quit，不接收任意PID强杀。stopped表示已接受退出且运行时桥已清除本次endpoint；进程最终退出仍需由宿主观察，崩溃不能当正常收尾。

MCP也可直接rev_call target=cli，action=player.launch/player.status/player.stop。launch参数为executable、directory、可选port、arguments、timeout；后两项只需directory。启动后用返回endpoint建立独立Player的MCP会话，不把Editor会话误当Player。多个实例用不同端口和会话目录。

## 4. 必须展示真实调用过程

每次连接先rev_discover，确认运行实例、能力和GM。然后rev_call查询runtime.identity、业务状态与隔离状态。使用rev_run执行带断言和finally的计划。向用户展示实际工具名、关键参数、目的和结果；未执行的步骤标未执行，不把直接CLI/Hub调用冒充MCP工具。

GM仍使用完整注册名称和discover返回的参数schema。运行时桥调用玩家同一业务入口；接受请求不是完成。编译/域重载或切换Player会话后重新发现。Editor的build.*、scene.*、game.perf.*等Editor专用接口不会自动出现在Player中。

## 5. 性能采样API

RevRuntimeAutomation.EnablePerformance启动轻量采样；PerformanceSnapshot返回最近最多2048帧的窗口快照；DisablePerformance释放Recorder。面板建议每0.5秒读取一次，关闭时停止文字更新；正式测量期间隐藏面板。采样器不会修改VSync、帧率上限、分辨率或游戏难度。

runtime.performance.enable / snapshot / disable 对应上述功能。

runtime.performance.begin接收seconds（1～600）和warmup（0～60，默认0），返回唯一runId。runtime.performance.state是只读查询，可用于有界until；runtime.performance.cancel协作取消并保留cancelled状态。begin只接受作业，最终必须核对同一runId、status=completed、success=true、frames>0、seconds达到请求、errors=0、cancelled=false，再另做业务负载和资源回收断言。

```json
{
  "name":"独立Player十秒采样",
  "steps":[
    {"target":"game","action":"runtime.identity","arguments":{},"expect":[{"path":"/data/editor","equals":false},{"path":"/data/backend","equals":"IL2CPP"}]},
    {"target":"game","action":"runtime.performance.begin","arguments":{"seconds":10,"warmup":2}},
    {"target":"game","action":"runtime.performance.state","arguments":{},"until":{"path":"/data/status","equals":"completed","timeoutSeconds":30,"intervalSeconds":1},"expect":[{"path":"/data/success","equals":true},{"path":"/data/errors","equals":0}]}
  ],
  "finally":[
    {"target":"game","action":"runtime.performance.cancel","arguments":{}},
    {"target":"game","action":"runtime.performance.disable","arguments":{}}
  ]
}
```

这是采样链路示例，不包含游戏造局或业务清理；正式计划必须在此前接项目隔离与固定负载，在finally接项目回首页/复位。截图与GIF在计时窗口外采集。

## 6. 指标口径

FPS=窗口帧数/墙钟秒，保留P50/P95/P99/max与超过12.5/16.67/33.33ms的比例。CPU、渲染线程、GPU并行，不能相加，也不从整帧扣EditorLoop计算所谓真实FPS。

Draw Calls/Total Batches/SetPass/三角形/顶点使用ProfilerRecorder。动态/静态合批/Instancing详细计数在Release中可能不可用。每项有available、value、unit；缺样=-1，不是0。主/渲染线程从纳秒转换为毫秒；内存字段为字节，非进程全部内存。GC Allocated In Frame的0值不能在没有有效性探针时用来承诺全局零GC。

GPU只接收新时间戳且正有效值的FrameTiming，记录gpuSamples；无样本保留-1。Frame Timing Stats在非开发包须启用。窗口焦点、分辨率、设备/API、构建ID、VSync和帧率上限随结果记录；unfocusedFrames非0应单独分析，不把后台样本与前台样本混成优化收益。

固定测量每秒预留1000帧容量，超过容量明确失败，不截断。采样循环不排序、不写CSV/JSON、不录制Profiler；结果只在窗口外汇总。查询/面板本身有开销，需独立做开启与关闭对照。

## 7. 接入职责与平台边界

框架提供构建、运行时桥、进程身份、通用计数器与有界测量。项目负责存档隔离、真实业务造局、规则断言、UI/资源预算和正式流程清理。不要把项目GameFlow或81棋夹具搬进框架。

Player HTTP使用127.0.0.1、随机token与session；业务在主线程执行。Windows IL2CPP必须实包验证裁剪、中文参数、JSON、截图与跨场景注册，不把Mono/Editor成功当IL2CPP通过。Android需另验HTTP支持、安装启动和端口转发；WebGL没有此HttpListener通道。

真机验收必须使用真实图形Player，固定输入、画质与资源，分别报告开发包/非开发包、冷启动/稳态/热态、规则通过/性能预算。报告保留全部轮次、取消和失败原因。RevGame不生成ZIP、不导出Profiler录制或内存快照，机器证据放.codex/work。
