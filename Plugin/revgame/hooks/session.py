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
                    "功能验收或修 Bug 按项目缺陷闭环保留实测与源码位置，修复后复跑原用例及相关回归。"
                    "交付前运行 python tools/RevPolicy/check.py。"
                    "最后一次 Unity 脚本改动后，在编辑态完整重编并核对本次编译工单无错误，再进入 Play 或交付。"
                ),
            }}))
            return


if __name__ == "__main__":
    main()
