# RevMCP · 架构与扩展方式

## 2026-10-29：活动账本与目的说明

Python Hub/Server 记录 Agent 请求、外部进程和计划步骤；Unity 文件桥、GM 适配与 HTTP 主线程记录实际执行。每层用唯一 id 合并开始和完成事件，跨层记录各自显示，避免把接单、执行完成和业务断言混为一谈。活动文件为 UTF-8 JSONL，位于 Library/RevolutionAutomation/activity；进程使用独立流，每流最多保留四个约 2MiB 分段。RevMCPActivityReader 每 250ms 按偏移读取完整行，最多读取最近 64 个文件并保留最近 500 项；半行、重载、乱序完成和清空视图按明确规则处理。

日志是旁路观察，不改变调用、取消、回执或重放规则；写入失败记录诊断，不重复执行业务。参数/结果摘要递归隐藏常见凭据，省略图像 base64 并限制大小；editor.activity 查询结果只记摘要，避免账本递归复制。自由文本不保证覆盖所有业务隐私，报告/复制前由项目决定分享范围。

RevMCPActivityDescription 根据内置契约或实时注册 Description 解释操作用途。intent 由 Agent 明确提供，最多 512 字符，仅为显示元数据，不是业务参数、指令或授权。rev_call/动态工具外层、计划与步骤支持 intent；步骤覆盖计划。窗口默认显示活动页，连接设置单列；editor.activity 只读查询日志与人话 summaries，editor.activity-window 负责可调用的页签和筛选。

RevResPath 输出位置由 ABBuildConfig.resPathCodePath 决定，空值回退原默认。生成器与窗口/CLI共用解析与迁移入口；仅移动有本生成器标记的旧资产并保留 GUID，不覆盖手写代码、不产生两份同名类。RevSoundPath 不改动。使用步骤与边界见 [新版使用说明](RevMCP活动日志与生成路径使用说明.html)。

更新：2026-10-22。配套 [使用说明](RevMCP使用说明.html) 和 [实测记录](验证记录/2026-10-22-RevMCP.html)。

## 1. 第三层负责稳定调用契约

参考本次提供的设计文档与 F:/system 中 Unity MCP、SGameMCPServer、ScriptRunnerRoute 的分层思路，Revolution 采用 Agent → RevMCP → 受控接口 → 真实游戏的链路。借鉴的是“把接口做清楚、让 Agent 能观察与验证”的设计，没有把来源工程的专有业务或脚本执行权限移植进框架。

Editor、Player、CLI 的实现差异留在适配器中。Agent 只需处理 target/action/arguments、结构化结果和测试计划。业务动作调用现有服务；GM、按钮与AI入口共享同一个业务方法，避免为测试实现第二套玩法。

## 2. Unity 插件与外部 MCP 的职责

| 组件 | 负责 | 不在此组件实现 |
|---|---|---|
| RevMCPWindow | 当前工程/Play/GM、HTTP启停、配置复制、证据入口 | 模型推理和Python解释器 |
| RevAutomationBridge/Catalog | Editor实际能力、工单、环境/参数、重载与完成回执 | Player网络与游戏规则 |
| RevRuntimeAutomation | 真实业务/GM注册表、Schema、主线程调用与owner清理 | 任意C#方法反射执行 |
| RevRuntimeAutomationHost | Player/Play本机HTTP、认证、session、队列、逐帧操作 | 网络线程直接碰Unity对象 |
| Python Hub | Editor/Runtime/外部进程统一发现和调度 | 凭空生成业务结果 |
| Python Server | MCP stdio握手、工具列表/调用、取消、image与报告 | 场景内业务状态 |
| Skill | 从一句话选择真实接口、断言、清理、诊断与报告 | 绕过参数/环境边界 |

采用外部 Python 的原因是可复用 Editor 与 Player，避免在 Unity 中维护 Python 环境和 MCP 协议生命周期。Editor 文件桥已经存在，因此复用它，不新增一套竞争 Editor 操作的协议服务器。HTTP 用于游戏运行时内部传输，外部 MCP 仍是 stdio。

## 3. 发现、校验、执行、验收

1. 发现：Editor从实际已加载特性扫描能力，运行时从真实业务注册表与RevGM.Commands读取；外部工具仅从显式manifest读取。
2. 校验：Python预检计划，Unity再校验参数/环境/confirm；未知字段、类型错误、范围错误明确失败。
3. 执行：每个实例的变更串行推进；Unity操作在主线程。跨帧动作每帧Tick，不能阻塞渲染或主线程忙等。
4. 验收：最终response、业务后置断言、日志、截图与finally都纳入结果；接单202、进度和命令正常返回不代表玩法通过。

离线commands.json服务帮助与参数类型；实时Catalog才证明目标项目安装了接口。当前菜单覆盖表使用精确菜单路径，不以目录前缀把未来按钮自动标成已接线。

## 4. 生命周期与不重放

Editor工单在Library按唯一id存储，跨域重载保存当前请求。普通变更重载后失败收尾；只有明确ResumeAfterReload的等待型动作恢复。Test Runner用持久启动标记避免重跑，并在取消等待后保持引擎变更门禁到真正结束。

HTTP后台线程只认证、解析、入队和读已发布结果；Update串行执行业务。session随服务启动变化，id在同session内去重，同id不同参数拒绝。最多16个排队动作、1024个保留工单；到容量后显式拒绝，新一轮重新启动session。不能删除去重记录后重放旧变更。

Editor文件桥与HTTP桥共用忙状态；排队HTTP变更时Editor拒绝另一笔变更，保留状态/Console和停止/恢复所需的控制入口。取消信号不能强行抢占同步业务函数，也不承诺回滚已经发生的效果；复位属于显式业务契约。

注册表在SubsystemRegistration清空，兼容关闭Domain Reload的Enter Play设置；按owner注销避免场景退出后静态委托持有旧对象。日志环保留最近128条，本局错误累计数独立保留，避免日志出环后丢失失败依据。

## 5. 扩展时选哪一层

| 目标 | 入口 |
|---|---|
| 新的编辑器工具按钮/资源规则 | 复用窗口背后服务，增加RevAutomationAction、离线清单和菜单覆盖映射 |
| 玩家动作、移动、战斗、业务场景切换 | 在业务组合根Register，转调现有Service/Logic/RevScene |
| 业务查询与异步完成状态 | Register并标readOnly；返回稳定结构化字段 |
| 需要多帧的单个动作 | RevRuntimeOperation.Tick/Cancel/Dispose |
| 已有GM指令 | 按现有RevGM契约注册，动态发现无需为每条手写MCP工具 |
| 已有Python/CLI | 显式manifest、固定命令数组、JSON stdin/stdout包装 |
| 手机/特殊引擎传输 | 单独验证平台支持后扩展传输，保留统一能力与工单语义 |

业务输入用Schema子集描述，输出用DTO/字典。readOnly必须真实无业务副作用，confirm表达接口要求；Hidden/工具注解不是安全边界。正式包默认裁掉测试接口，显式启用与本机凭据仍需由项目管理。

## 6. 测试覆盖与边界

Python回归覆盖协议/进程、引用类型、参数拒绝、断言、轮询与清理；Unity EditMode覆盖真实序列化/Undo/Prefab/参数内核；Editor与独立Windows Player用同一教学计划证明调用真正穿过MCP到Unity。

这是可扩展的接口与验收基建。UGUI中心点击不等于完整手指/拖拽模拟；截图不是逻辑断言；Windows开发版成功不代表小游戏/设备平台；菜单映射完整不代表所有动作与第三方插件逐项测试完成。扩展必须继续提供对应环境的真实证据。
