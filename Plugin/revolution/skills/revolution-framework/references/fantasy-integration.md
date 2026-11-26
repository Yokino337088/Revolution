# 可选 Fantasy 联动

使用 [Fantasy fork 的联动包与样例](https://github.com/Yokino337088/Fantasy_JiYeon/tree/main/Integrations/Revolution)，并按 [fantasy-net](../../fantasy-net/SKILL.md) 实现 Scene、Session、FTask、协议与 Handler。Revolution 提供命令发现、状态断言、跨帧计划与收尾。宿主声明何时启用探针，独立 UPM 包才会为 Unity 加入 Fantasy 引用。

## 外部导出与服务器

1. 在 Fantasy 联动目录选择本机可信 profile，用工具生成外部清单。清单用绝对路径登记固定程序与工程，AI 输入沿用 JSON 校验。
2. RevCLI 用 `external --manifest <清单> discover` 查看能力。RevMCP 启动时传 `--external-manifest <清单>`。Codex 插件可在 `rev_bind` 显式给出 `externalManifest`，随后搜索 `target=cli` 和 `query=fantasy.`。
3. 从 `fantasy.config.check`、`fantasy.protocol.check` 与 `fantasy.verify` 开始。正式生成和服务器测试使用清单声明的确认字段。只读预演通过实际生成器判断漂移，业务测试再检查 RPC 数据与清理。

## Unity 接线

独立包持有探针 Scene 和 Session。启动开发态 Play 后核对 `fantasy.editor.status` 与运行期 `fantasy.client.state`，再提交连接和回显请求，等待状态变化。每个计划为连接、发送和退出共用一个 UUID `scope`，退出释放该探针的 Scene，按 owner 注销能力。宿主可沿同样的节奏给真实业务入口添加能力。

[RevGame 联机实践](revgame-online.md#九路围棋) 展示了已有项目怎样隔离 Fantasy 宿主调用、协议实例和游戏状态。
