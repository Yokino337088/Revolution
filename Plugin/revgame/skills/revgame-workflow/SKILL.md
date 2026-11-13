---
name: revgame-workflow
description: 在 RevGame 围棋史莱姆仓库中定位项目规范、资源检查和游戏验收入口。用于 RevGame 的玩法、UI、资源与 Unity 自动化工作。
---

# RevGame 项目入口

从工作目录向上找到同时包含 `AGENTS.md` 与 `RevGame_Client/UnityProject/RevGame.Unity` 的仓库根。读取根 `AGENTS.md` 与 `.codex/skills/revgame-development/SKILL.md`，沿后者只加载当前任务需要的专题。它们随 RevGame 版本更新。

Unity 目标是该仓库的 `RevGame_Client/UnityProject/RevGame.Unity`。用相邻的 `Revolution.RevCLI/rev.py` 做编辑器操作。实际游戏测试优先已绑定到这个工程的常驻 RevMCP。使用 Revolution 插件内置 MCP 时，先用 `rev_bind` 绑定此工程的绝对路径，再 `rev_discover`。工具调用、业务断言和清理分别核对。

写代码或移动资产后，从 RevGame 根运行 `python tools/RevPolicy/check.py`。准备提交时用 `--staged` 检查索引。资源改动继续经过 Unity 的 `GameResourceUsage.Validate` 和目标 AB 验收。
