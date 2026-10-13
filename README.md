# Revolution.Demo —— 框架模块示例工程

每个运行时模块一个 `Rev<模块>.Demo` 目录：**一个带详尽注释的演示脚本 + 一个可直接打开点 Play 的场景**。
打开场景 → 点 Play → 按界面左侧按钮逐个体验；右侧是步骤日志（讲清楚每一步发生了什么）。

## 场景索引（18 个，全部可运行）

| 场景（目录内同名 .unity） | 演示模块 | 亮点 |
|---|---|---|
| RevLogDemo.unity | 日志系统 | 五级别成本契约、重复抑制、MinLevel、环形缓冲 Dump、tag 静音 |
| RevTimerDemo.unity | 计时器系统 | After / Every / 句柄暂停重启 / 全局暂停 / Server 域与 At / owner 防泄漏 / 秒表 |
| RevPublicMonoDemo.unity | 公共 Mono | 三相位回调、去重、纯 C# 协程、作用域、异常隔离 |
| RevObjectPoolDemo.unity | 对象池 | GameObject 池复用 / 引用池生命周期 / 池统计 / 一键清空 |
| RevEventSystemDemo.unity | 事件系统 | 字符串事件解耦、带参派发、优先级、返回值发现"没人听"、异常隔离 |
| RevServiceLocatorDemo.unity | 服务定位器 | 装配 Build、重复注册报错、Scoped 每局独立、循环依赖检测、释放后取服务报错 |
| RevTaskDemo.unity | 异步任务 | async RevTask 顺序流程、Yield/Delay/WhenAll、异常传播、CompletionSource 桥接旧接口 |
| RevSceneDemo.unity | 场景系统 | ReloadAsync 完整加载链（0→100% 平滑进度）、四事件、人话报错 |
| RevInputDemo.unity | 输入系统 | 动作轮询与事件驱动、轴、连发、屏蔽（轴一并归零）、手势、改键存档、冲突检测 |
| RevDataLoadDemo.unity | 配置表装载 | 生成的容器等价物手写版、LoadText 容错解析、主键查询、与 DataTableManager 的关系 |
| RevSoundSystemDemo.unity | 音效系统 | 目录注册、Play/PlayAt/PlayBgm、音量体系、静音、失败可见（接真实音频只差一步） |
| RevStateMachineDemo.unity | 状态机 | 轻量状态机三件套、自动流转（巡逻→追击→攻击）、StateChanged、重复切换拦截 |
| RevResourceSystemDemo.unity | 资源加载 | 资源根目录、Load 缓存、引用计数（归零才真卸载）、句柄与失败原因 |
| RevSingletonDemo.unity | 单例 | 纯 C# 惰性单例 / 自动创建的组件单例（跨场景常驻）、适用纪律 |
| RevUISystemDemo.unity | UI 系统 | 覆盖《UI 使用说明》全部章节：三种事件接法 · 8 个生命周期回调 · 全部 API（异步/预加载/查询/关层/关组/Back/DumpStats）· 带数据面板 · 六层与遮罩/返回栈 · 互斥组 · 三 Canvas · 动画（预设+自定义转场+控件动效）· Part 两种挂法（首次需一键生成演示预制体 → 生成到资源根目录 `Assets/GameRes/RevUIDemo/`） |
| RevActionSequenceDemo.unity | 行动序列 | 按键驱动的多段序列播放（既有示例） |
| RevGMCommandDemo.unity | GM 指令 | 场景内提示 + 命令注册演示；指令面板在编辑器菜单（Ctrl+Shift+G） |
| RevHotUpdateDemo.unity | 资源热更（扩展包） | 本机假 CDN 跑通全链路：一键打包+清单+装配 → 起本地CDN.cmd → 检查/执行更新 → 加载到新内容（需装 `Revolution.HotUpdate` 包；见该目录 README） |

> ★ 第 18 个（`RevHotUpdate.Demo`）属于**扩展包**演示：它有自己的程序集（`Revolution.Demo.HotUpdate`，引用 `Revolution.HotUpdate`）。
> 没装热更包时只有这一个目录报"找不到程序集引用"，其余 17 个场景不受影响。

## 结构约定

```
Rev<模块>.Demo/
  Rev<模块>Demo.cs      演示脚本（MonoBehaviour，OnGUI 面板：按钮 + 步骤日志，注释讲透每个 API）
  Rev<模块>Demo.unity   可运行场景（Main Camera + Light + 挂演示脚本的 RevDemoRoot）
Revolution.Demo.asmdef  独立程序集（只引用 Revolution.Runtime —— demo 永远是"使用方"视角）
RevDemoSceneList.txt    场景索引纯文本版
```

## 发布

push 到 main 后由 `.github/workflows/sync-demo-branch.yml` 自动把本目录发布到 **demo 分支**（与 package 分支同机制）：

```
git clone -b package https://github.com/Yokino337088/Revolution.git Assets/Revolution
git clone -b demo    https://github.com/Yokino337088/Revolution.git Assets/Revolution.Demo
```
