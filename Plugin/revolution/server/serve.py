"""把 Codex 的常驻连接绑定到明确指定的 Unity 工程，实际动作由 RevMCP 执行。"""

import json
import sys
from pathlib import Path

# 从插件缓存加载同批导出的 RevMCP 与 RevCLI。绑定工程时优先读项目的动作清单，
# Python 实现始终来自已安装的插件，切换工程不执行工作区里的 Python 代码。
ROOT = Path(__file__).resolve().parents[1]
CLI = ROOT / "tools/Revolution.RevCLI"
MCP = ROOT / "tools/Revolution.RevMCP"
sys.path[:0] = [str(CLI), str(MCP)]

from revmcp.backends import Hub
from revmcp.schema import object_schema, validate
from revmcp.server import Server


# 服务器切换工程需要完整路径，避免在多个打开的工程之间自动猜测。
BIND = object_schema({
    "project": {"type": "string", "minLength": 1},
    "runtimeEndpoint": {"type": "string", "minLength": 1,
                        "description": "可选的开发版 Player runtime.json 绝对路径。"},
}, ("project",))


class ProjectServer(Server):
    """增加工程绑定，沿用 RevMCP 的 stdio、工具校验、证据和串行任务队列。"""

    def __init__(self):
        """初次握手不打开 Unity，也不读取当前目录下的任意配置。"""
        super().__init__(None, compact_tools=True)
        self.binding = None
        self.hub = None

    def tools(self, cancel=None):
        """固定工具数量。注册动作从当前工程用 rev_discover 查询。"""
        result = super().tools(cancel)
        result["tools"].insert(0, {
            "name": "rev_bind",
            "description": "把本条连接绑定到明确指定的 Unity 工程绝对路径。调用后先 rev_discover。",
            "inputSchema": BIND,
        })
        return result

    def call(self, params, token):
        """绑定之外的调用交给 RevMCP，包括断言、取消和 finally 清理。"""
        if params.get("name") == "rev_bind":
            arguments = params.get("arguments", {})
            try:
                validate(arguments, BIND)
                data = self.bind(arguments["project"], arguments.get("runtimeEndpoint"))
            except (OSError, ValueError, RuntimeError) as exc:
                data = {"success": False, "error": type(exc).__name__, "message": str(exc)}
        elif self.hub:
            return super().call(params, token)
        else:
            data = {"success": False, "message": "请先用 rev_bind 指定 Unity 工程绝对路径。"}
        return {
            "content": [{"type": "text", "text": json.dumps(data, ensure_ascii=False)}],
            "structuredContent": data,
            "isError": not data["success"],
        }

    def bind(self, project_arg, endpoint_arg):
        """先核对项目标识和目录，再替换连接。绑定失败时原工程仍可用。"""
        requested = Path(project_arg)
        if not requested.is_absolute():
            raise ValueError("Unity 工程必须是绝对路径。")
        project = requested.resolve(strict=True)
        if not (project / "Assets").is_dir() or not (project / "ProjectSettings/ProjectSettings.asset").is_file():
            raise ValueError(f"未找到 Unity 工程结构：{project}")
        endpoint = None
        if endpoint_arg:
            requested_endpoint = Path(endpoint_arg)
            if not requested_endpoint.is_absolute():
                raise ValueError("Player 连接文件必须是绝对路径。")
            endpoint = requested_endpoint.resolve(strict=True)
            if endpoint.name != "runtime.json" or not endpoint.is_file():
                raise ValueError("请选择开发版 Player 写出的 runtime.json。")

        # 同版本的项目工具优先。MCP 协议仍使用插件自带的 RevMCP，
        # Hub 读取项目 RevCLI 动作清单，在线注册表才决定动作是否可用。
        project_cli = project.parent / "Revolution.RevCLI"
        has_cli = (project_cli / "rev.py").is_file() and (project_cli / "commands.json").is_file()
        cli = project_cli if has_cli else CLI
        reports = project / "Library/RevolutionAutomation/codex"
        for parent in (project, *project.parents):
            if (parent / ".codex/rev-policy.json").is_file() and (parent / ".git").exists():
                reports = parent / ".codex/work/revolution-plugin"
                break

        binding = (project, endpoint)
        if binding != self.binding:
            self.hub = Hub(project=project, runtime_endpoint=endpoint, cli_root=cli, reports=reports)
            self.binding = binding
        return {"success": True, "project": str(project), "commandCatalog": str(cli / "commands.json"),
                "bundledRevCli": str(CLI / "rev.py"),
                "player": endpoint is not None, "revMcp": str(MCP), "reports": str(reports)}


if __name__ == "__main__":
    ProjectServer().serve()
