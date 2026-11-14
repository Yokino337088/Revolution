---
name: revgame-workflow
description: 在 RevGame 围棋史莱姆仓库中定位项目规范、资源检查、游戏验收和缺陷修复回归入口。用于 RevGame 的玩法、UI、资源与 Unity 自动化工作。
---

# RevGame 项目入口

从工作目录向上找到同时包含 `AGENTS.md` 与 `RevGame_Client/UnityProject/RevGame.Unity` 的仓库根。读取根 `AGENTS.md` 与 `.codex/skills/revgame-development/SKILL.md`，沿后者只加载当前任务需要的专题。它们随 RevGame 版本更新。

Unity 目标是该仓库的 `RevGame_Client/UnityProject/RevGame.Unity`。用相邻的 `Revolution.RevCLI/rev.py` 做编辑器操作。实际游戏测试优先已绑定到这个工程的常驻 RevMCP。使用 Revolution 插件内置 MCP 时，先用 `rev_bind` 绑定此工程的绝对路径，再 `rev_discover`。工具调用、业务断言和清理分别核对。

功能完整性、缺陷定位和修复回归沿 `revgame-development` 的验收入口加载 `revolution-ai-automation`。覆盖本次需求对应的玩家路径、失败与重入，保留首轮实测、`code_refs`、修改建议、原断言复测和相关回归。原始证据按项目约定放 `.codex/work/`。

写代码或移动资产后，从 RevGame 根运行 `python tools/RevPolicy/check.py`。准备提交时用 `--staged` 检查索引。资源改动继续经过 Unity 的 `GameResourceUsage.Validate` 和目标 AB 验收。

小范围 AB 更新加载 RevGame 项目的 Unity 与资源专题。先验证项目分包与图集，再通过 Revolution 的 `ab.build-incremental` → `ab.check-map` → `ab.publish-incremental` 串行执行，三步使用相同的显式 BuildTarget。读取回执确认实际替换数；Windows Editor 冷启动验证用 StandaloneWindows64。
