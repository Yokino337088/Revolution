---
name: revgame-workflow
description: 在 RevGame 围棋史莱姆仓库中定位项目规范、资源检查、游戏验收和缺陷修复回归入口。用于 RevGame 的玩法、UI、资源与 Unity 自动化工作。
---

# RevGame 项目入口

编写或验收业务前，先查目标仓库的 `.codex/skills/revolution-framework/references/revgame-practice-map.md` 和命中的实际源码。用 [框架参考](https://github.com/Yokino337088/Revolution/blob/main/Skills/revolution-framework/references/revgame-practice-map.md) 找跨阶段、UI、内容、联机和交付先例，当前项目保持自己的规则和版本。自动交付在仓库同专题查看实际接口。

从工作目录向上找到同时包含 `AGENTS.md` 与 `RevGame_Client/UnityProject/RevGame.Unity` 的仓库根。读取根 `AGENTS.md` 与 `.codex/skills/revgame-development/SKILL.md`，沿后者只加载当前任务需要的专题。它们随 RevGame 版本更新。

编写、修改或审查代码时，先读 RevGame 根目录下的 `.codex/skills/revgame-development/references/code-and-comments.md`。新增或修改的类型、接口、字段、属性、方法和回调逐项写面向新手的中文注释。简短逻辑一句话，关键实现解释原因、顺序、边界和清理。交付前按 diff 和新文件逐项核对，补齐遗漏或过期的注释。

制作或排查 Shader、材质、纹理和渲染特效时，读 RevGame 根目录下的 `.codex/skills/revolution-ta/SKILL.md`，也可用已安装的 Revolution 插件中的 `revolution-ta`。RevGame 自有 Shader 在 `Assets/GameShader/<模块>`，材质在 `Assets/GameRes/Material/<模块>`。按现行采样、分包和实际目标画面验证。

Unity 目标是该仓库的 `RevGame_Client/UnityProject/RevGame.Unity`。用相邻的 `Revolution.RevCLI/rev.py` 做编辑器操作。实际游戏测试优先已绑定到这个工程的常驻 RevMCP。使用 Revolution 插件内置 MCP 时，先用 `rev_bind` 绑定此工程的绝对路径，再 `rev_discover`。工具调用、业务断言和清理分别核对。

功能完整性、缺陷定位和修复回归沿 `revgame-development` 的验收入口加载 `revolution-ai-automation`。覆盖本次需求对应的玩家路径、失败与重入，保留首轮实测、`code_refs`、修改建议、原断言复测和相关回归。原始证据按项目约定放 `.codex/work/`。

写代码或移动资产后，从 RevGame 根运行 `python tools/RevPolicy/check.py`。准备提交时用 `--staged` 检查索引。资源改动继续经过 Unity 的 `GameResourceUsage.Validate` 和目标 AB 验收。

导入前查 RevGame 中现有资源、逻辑路径、图集和 AB。大厅、战斗与 Gal 共用的资源沿用现有资产，只补真正缺少的项。对同一目标平台，同一资源只保留一份生产资产和一个实体 AB。其他包或模块通过依赖复用。图集和分包变更按项目资源专题核对重复导入、隐式重复依赖与实际包体积。

小范围 AB 更新加载 RevGame 项目的 Unity 与资源专题。先验证项目分包与图集，再通过 Revolution 的 `ab.build-incremental` → `ab.check-map` → `ab.publish-incremental` 串行执行，三步使用相同的显式 BuildTarget。读取回执确认实际替换数；Windows Editor 冷启动验证用 StandaloneWindows64。

构建成功且 `data.rebuildReason` 非空时，本次已经强制全量。记录原因，继续同平台映射检查和发布。遇到构建未完成、平台不符或校验不一致导致发布被拒时，按项目的增量 AB 配方检查工单并重建。发布回执用于确认实际替换数量。
