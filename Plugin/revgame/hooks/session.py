"""识别 RevGame 后仅指向仓库规则入口。"""

import json
import sys
from pathlib import Path


def main():
    """根据项目入口和规范配置识别 RevGame，避免向其他仓库注入项目特例。"""
    event = json.load(sys.stdin.buffer)
    cwd = Path(event["cwd"]).resolve()
    for root in (cwd, *cwd.parents):
        if ((root / "AGENTS.md").is_file()
                and (root / ".codex/rev-policy.json").is_file()
                and (root / ".codex/skills/revgame-development/SKILL.md").is_file()):
            print(json.dumps({"hookSpecificOutput": {
                "hookEventName": "SessionStart",
                "additionalContext": "RevGame 项目规则见仓库根 AGENTS.md，专题按 revgame-development 路由。",
            }}))
            return


if __name__ == "__main__":
    main()
