# AGENTS.md · Revolution

框架开发遵循 [Revolution 官方编程架构规范](Revolution.Document/编程范式/Revolution官方编程架构规范.md)。涉及导入、分包或发布资源时，同时查阅 [资源加载与 AB 技能](Skills/revolution-framework/references/resource-and-ab.md)。

- 新增或修改手写代码时，按 [新手注释规范](Skills/revolution-framework/references/code-style.md#3-面向新手的中文注释是交付门禁) 写中文注释。类型和接口、字段和属性、方法和回调逐项覆盖，方法内关键步骤写清原因、顺序和收尾。简单行为一句话，复杂契约分段说明。交付前按实际改动逐项补齐，方法体变更也要检查声明的注释。
- 导入前按内容与导入用途查已有资源，能复用就沿用现有 GUID 和逻辑路径，只导入真正缺少的资源。
- 同一构建目标中，同一资源只归一个 AB 包。跨模块通过原包的依赖与映射复用，参与图集的 Sprite 只归一个生效图集。发布前核对显式归属、隐式重复依赖和目标平台产物。
