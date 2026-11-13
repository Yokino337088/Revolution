# Revolution 与 RevGame Codex 插件

Revolution 插件包含框架、编辑器、AI 自动化、性能和 scenario 五个技能，以及 RevCLI、RevMCP。RevGame 插件从目标仓库读取当版项目规范，并在支持 hooks 的本地 Codex 会话启动或压缩后补充简短约定。RevGame 的项目规则和检查配置由 RevGame 仓库维护。

## 安装和使用

在 Codex 桌面端打开 Revolution 仓库并选择 **Revolution** 插件来源。安装 **Revolution**，在 RevGame 工作时再安装 **RevGame**。已安装的 Codex CLI 也可注册该市场并安装：

```powershell
codex plugin marketplace add .
codex plugin add revolution@revolution-tools
codex plugin add revgame@revolution-tools
```

插件使用 PATH 上的 Python 3.10+。在新会话里用 `rev_bind` 指定 Unity 工程的绝对路径。独立开发版 Player 可同时指定该实例的 `runtimeEndpoint`。一次绑定之后在同一连接里按需用 `rev_discover`、`rev_call`、`rev_run`。已有项目 RevMCP 的连接可以直接复用。项目安装的 Editor Bridge、Play 状态和业务注册表决定可用动作。

编辑器制作、导表和构建使用 `Revolution.RevCLI/rev.py`。优先用目标工程相邻工具，也可用 `rev_bind` 返回的 `bundledRevCli`。所有调用显式传 `--project`。RevMCP 通过现有 Hub 分发 Editor、开发态 GM 和测试计划，计划断言、取消与清理沿用 RevMCP。使用项目自己明确配置的外部扩展工具时，继续用项目的独立 RevMCP 启动参数。

在桌面端 **Review hooks** 中审核插件 hooks 后，Codex 才会执行会话提醒。用 Git 检查器和 CI 落实可判定的规则。具体宿主支持范围和审核方式见官方文档。 [插件打包](https://developers.openai.com/plugins/build/plugins) · [Codex Hooks](https://learn.chatgpt.com/docs/hooks)

## 在项目内执行规范检查

将 `Plugin/revolution/policy` 中的检查器源码随项目分发，配置放到项目根 `.codex/rev-policy.json`。项目自选资产目录、受查 C# 目录和启动入口。可直接查看 [RevGame 项目配置](https://github.com/Yokino337088/RevGame/blob/main/.codex/rev-policy.json)。

终端默认检查工作区，提交前用 `--staged`，CI 用 `--base <基线提交>`。输出 0 为通过，1 为违规，2 为检查无法完成。Unity 的资源验证、真机与业务验收继续走项目原入口。

资产规则检测目录与 `.meta`、Git 已识别的资产移动、变更 GUID 重复和框架快照锚点。C# 检查使用 .NET 8 SDK 内置 Roslyn 的语法树，在常用 Unity 符号组合中查新增问题。语义判断由 Unity 编译和代码审查负责。仓库管理员可把检查设为必需状态检查，以在合并时执行门禁。

## 维护和发布

从 Revolution 源码中的 `Skills/`、`Revolution.RevCLI/`、`Revolution.RevMCP/` 修改并重新导出。Codex 安装缓存只复制插件目录，导出的技能和工具随版本提交。

```powershell
python Plugin/package_plugins.py
python Plugin/package_plugins.py --check
python -m unittest discover -s Plugin/tests
```

工作区已安装旧版本时，刷新该来源并重新安装新版，打开新会话核对工具。Repo marketplace 可按 Git 提交固定版本。
