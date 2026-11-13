"""在 Revolution 工程的会话启动和压缩后补充短指引。"""

import json
import sys
from pathlib import Path


def uses_revolution(root):
    """查找源码、内置包或已声明的 UPM 依赖，普通 Unity 工程不注入。"""
    for project in (root, root / "RevolutionFrameWork_Unity"):
        if (project / "Assets/Revolution/package.json").is_file():
            return True
        manifest = project / "Packages/manifest.json"
        if manifest.is_file():
            try:
                if "com.yokino.revolution" in json.loads(manifest.read_text(encoding="utf-8-sig")).get("dependencies", {}):
                    return True
            except (OSError, ValueError, AttributeError, TypeError):
                continue
    return False


def main():
    """只用会话工作目录定位工程，输出 Codex SessionStart 支持的上下文。"""
    event = json.load(sys.stdin.buffer)
    cwd = Path(event["cwd"]).resolve()
    if not any(uses_revolution(path) for path in (cwd, *cwd.parents)):
        return
    print(json.dumps({"hookSpecificOutput": {
        "hookEventName": "SessionStart",
        "additionalContext": (
            "Revolution 工程：写业务前查当前安装的公开门面和同类实现。"
            "FSM 管阶段，RevScope 管生命周期，业务异步用 RevTask。"
            "Unity 操作先确认绝对工程路径与真实注册表，编辑器验证和运行期验收各用对应证据。"
        ),
    }}))


if __name__ == "__main__":
    main()
