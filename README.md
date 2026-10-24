# Revolution RevMCP

把 Revolution 的 Editor、实际注册 GM、运行时业务、UGUI、状态/截图，
以及显式登记的 CLI/Python 接口提供给 AI Agent。Python 3.10+，仅用标准库。

本分支根目录包含两个目录，缺一不可：

| 目录 | 作用 |
|---|---|
| `Revolution.RevMCP/` | MCP 服务与 CLI（`rev_mcp.py`），入口在这里 |
| `Revolution.Automation/` | 自动化客户端，`rev_mcp.py` 启动时要导入它 |

## 接入

```text
git clone -b revmcp --depth 1 https://github.com/<owner>/Revolution.git tools
```

克隆后目录关系与开发仓库一致，`rev_mcp.py` 的默认依赖路径不用改：

```powershell
python tools/Revolution.RevMCP/rev_mcp.py --project <Unity工程绝对路径> discover
```

Agent 客户端启动命令：

```text
python <tools>/Revolution.RevMCP/rev_mcp.py --project <Unity工程绝对路径> serve
```

配置示例见 `Revolution.RevMCP/examples/mcp-config.example.json`，
其中的路径必须换成本机绝对路径。

## 前提

- 目标 Unity 工程已安装框架与自动化桥接代码，并等待编译完成；
- 工具只能调用工程里**实际注册**的 Editor / GM / 业务能力，没有登记就测不到；
- 独立 Player 需显式开启运行时接口，正式发行包默认不开放；
- 部分删除类与高风险 GM 动作要求 `confirm`，但它是接口参数，不是完整权限系统。

详细说明见 `Revolution.RevMCP/README.md`。

> 本分支由 CI 从 `main` 的 `Revolution.RevMCP/` 与 `Revolution.Automation/` 整份重建，
> 不保留历史，也不接受直接提交；请改主分支上的源目录。
