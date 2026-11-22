# AGENTS.md · Revolution

框架开发遵循 [Revolution 官方编程架构规范](Revolution.Document/编程范式/Revolution官方编程架构规范.md)。涉及导入、分包或发布资源时，同时查阅 [资源加载与 AB 技能](Skills/revolution-framework/references/resource-and-ab.md)。

- 写业务、维护框架门面或验收时，优先用 [RevGame 工程实践索引](Skills/revolution-framework/references/revgame-practice-map.md) 找职责相近的真实入口和回归证据，再结合目标项目已安装版本、平台、规则和资产归属适配。
- 涉及开页、切场、初始化、异步命令和持久化时，优先采用 [业务自动交付契约](Skills/revolution-framework/references/automatic-delivery.md)。业务声明许可、依赖、就绪和提交点。接单、取消、失败回收和诊断由对应框架入口承担。
- 新增工具动作时尽量在 Unity 注册处声明 DTO。发布前通过 RevCLI `contract` 校验清单，有在线工程时加 `--live` 核对实际注册与编译结果。
- 新增或修改手写代码时，按 [新手注释规范](Skills/revolution-framework/references/code-style.md#3-面向新手的中文注释是交付门禁) 写中文注释。类型和接口、字段和属性、方法和回调逐项覆盖，方法内关键步骤写清原因、顺序和收尾。简单行为一句话，复杂契约分段说明。交付前按实际改动逐项补齐，方法体变更也要检查声明的注释。
- 自动化优先用内存传递计划和响应。必须创建临时工单时，由创建者在最后一次读取后立即永久删除。仅在用户明确要求时生成自动化测试报告，优先用用户指定的路径，省略时查项目现有文档目录。`Revolution.RevMCP/results/` 禁止写入运行产物。详见 [报告规范](Skills/revolution-ai-automation/references/report.md)。
- 制作或调整 Shader、材质和纹理时读 [Revolution-TA](Skills/revolution-ta/SKILL.md)，按实际管线和目标平台核对采样、依赖、画面与资源回收。
- 编写、修改或审查战斗、逐帧更新、密集 UI/特效及其它性能敏感代码时，读 [Revolution 性能 Skill](Skills/revolution-performance/SKILL.md)。在本次路径内分别核对 CPU 逻辑与提交、GPU、内存与 GC、缓存失效与所有权，用相同负载核对受影响的规则、画面和收益。
- 攻坚和高技术含量任务交付时，新建或实质更新对应的项目 Wiki 案例。用 [Revolution Wiki Skill](Skills/revolution-wiki/SKILL.md) 记录问题、取舍、实测证据和复用边界，按任务已有授权发布到对应项目。
- 导入前按内容与导入用途查已有资源，能复用就沿用现有 GUID 和逻辑路径，只导入真正缺少的资源。
- 同一构建目标中，同一资源只归一个 AB 包。跨模块通过原包的依赖与映射复用，参与图集的 Sprite 只归一个生效图集。发布前核对显式归属、隐式重复依赖和目标平台产物。
