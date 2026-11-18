# AGENTS.md · Revolution

框架开发遵循 [Revolution 官方编程架构规范](Revolution.Document/编程范式/Revolution官方编程架构规范.md)。涉及导入、分包或发布资源时，同时查阅 [资源加载与 AB 技能](Skills/revolution-framework/references/resource-and-ab.md)。

- 导入前按内容与导入用途查已有资源，能复用就沿用现有 GUID 和逻辑路径，只导入真正缺少的资源。
- 同一构建目标中，同一资源只归一个 AB 包。跨模块通过原包的依赖与映射复用，参与图集的 Sprite 只归一个生效图集。发布前核对显式归属、隐式重复依赖和目标平台产物。
