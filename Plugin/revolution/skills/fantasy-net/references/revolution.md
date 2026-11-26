# Fantasy 与 Revolution 联动

联动源码与样例安装说明位于 fork 的 [Integrations/Revolution](https://github.com/Yokino337088/Fantasy_JiYeon/tree/main/Integrations/Revolution)。先确认当前项目使用的 Fantasy.Net、Fantasy.Unity 和历史 OpCode.Cache 都来自同一套协议契约。选定可信 profile 后才生成工具清单，RevCLI、RevMCP 和 Codex 复用这份清单。

## 协议与配置

1. 把外网与内网协议放在同一协议根的 `Outer/`、`Inner/`。修改原始 `.proto`，继续保留共用的 `OpCode.Cache`。
2. 用 `fantasy.config.check` 检查本地拓扑中的进程、机器、World、Scene 和端口引用。Control Center 远端拓扑到实际宿主发现状态中核对。
3. 用 `fantasy.protocol.check` 在临时目录运行真实生成器。输出或共享操作码缓存存在漂移时，确认预期后再调用 `fantasy.protocol.export`。
4. 从同一配置生成两端产物，执行 `fantasy.server.build` 和目标 Unity 的真实编译。导出成功与编译通过各有回执，协议兼容要另外核对。

## 客户端、服务器与自动化

- Unity 联动是单独安装的 `com.jiyeon.revolution.fantasy`。工程仍由其 Fantasy 宿主管理正常连接，开发探针使用自己的 Scene 与 Session。用 `fantasy.editor.status` 核对当前编辑器安装，再通过 `rev_discover` 查询 Play 中实际可用的 `fantasy.client.*`。
- 网络生命周期与 RPC 继续使用 `FTask`。RevMCP 的连接和发送动作返回接单结果，计划等待 `fantasy.client.state` 的真实连接与 RPC 结果。在 open、echo 和 finally 的 close 传同一个 UUID `scope`，最后核对回到 idle。
- 本机服务端样例使用 `Entry.Start`、编译期注册的真实 Handler 和已导出的回显消息。配置固定回环地址，流程编译、启动独立进程、断言成功和业务拒绝后用 `Entry.Close` 收尾。用 `fantasy.verify` 依次检查配置、漂移和服务器回归。
- 项目可通过显式 profile 填写 `unityProject` 与 `revMcp`，在已开启 Play 的隔离工程用 `fantasy.unity.smoke` 启动同一服务端并运行 Unity RPC 计划。
- 业务示例可参考 RevGame 的 [GoRelay 协议](https://github.com/Yokino337088/RevGame/tree/main/RevGame_Server/RevGame.Server/Tools/NetworkProtocol) 和 [Fantasy 网络边界](https://github.com/Yokino337088/RevGame/tree/main/RevGame_Client/UnityProject/RevGame.Unity/Assets/GameScripts/Services/Pvp/Networking/Fantasy)。按目标项目自己的协议和生命周期扩展调试动作。
