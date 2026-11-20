"""识别 RevGame 后恢复项目入口和写代码时的注释约定。"""

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
                "additionalContext": (
                    "RevGame 项目规则见仓库根 AGENTS.md，专题按 revgame-development 路由。"
                    "先按 RevGame 工程实践索引读本工程真实路径、业务状态与收尾。切场和数据交付沿业务自动交付契约。"
                    "写代码前读项目的新手注释守则。新增或修改的类型、接口、字段和方法逐项写中文注释。"
                    "短行为一句话，关键实现旁解释原因、顺序、边界与清理。交付前逐项核对 diff 并补齐。"
                ),
            }}))
            return


if __name__ == "__main__":
    main()
