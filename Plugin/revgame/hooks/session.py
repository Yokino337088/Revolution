"""只向 RevGame 会话恢复简短约定，供新会话和上下文压缩后使用。"""

import json
import sys
from pathlib import Path


def main():
    """根据项目入口和规范配置识别 RevGame，避免向其他仓库注入项目特例。"""
    event = json.load(sys.stdin.buffer)
    cwd = Path(event["cwd"]).resolve()
    for root in (cwd, *cwd.parents):
        if (root / ".codex/rev-policy.json").is_file() and (root / ".codex/skills/revgame-development/SKILL.md").is_file():
            print(json.dumps({"hookSpecificOutput": {
                "hookEventName": "SessionStart",
                "additionalContext": (
                    f"RevGame 仓库 {root}。以根 AGENTS.md 和仓库内 revgame-development 为准。"
                    "框架改动在 Revolution 源仓库进行。项目材质归 GameRes/Material/模块，移动资产带原 .meta。"
                    "编辑器用 RevCLI；游戏验收优先项目 RevMCP，发现后调用并核对断言和清理。"
                    "交付前运行 python tools/RevPolicy/check.py。"
                ),
            }}))
            return


if __name__ == "__main__":
    main()
