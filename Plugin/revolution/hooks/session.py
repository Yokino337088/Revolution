"""在 Revolution 工程的会话启动和压缩后补充短指引。"""

import json
import sys
from pathlib import Path


def uses_revolution(root):
    """查找源码、内置包或已声明的 UPM 依赖，普通 Unity 工程不注入。"""
    projects = [root, root / "RevolutionFrameWork_Unity"]
    # 项目规范与检查器共用 unityProject 入口，仓库根也能找到嵌套工程。
    policy = root / ".codex/rev-policy.json"
    if policy.is_file():
        try:
            project = (root / json.loads(policy.read_text(encoding="utf-8-sig"))["unityProject"]).resolve()
            if project == root or root in project.parents:
                projects.append(project)
        except (KeyError, OSError, TypeError, ValueError):
            pass
    for project in projects:
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
    roots = (cwd, *cwd.parents)
    # RevGame 由仓库规则和项目插件负责。即使当前目录在嵌套 Unity 工程中，也不重复注入。
    if any((root / "AGENTS.md").is_file()
           and (root / ".codex/rev-policy.json").is_file()
           and (root / ".codex/skills/revgame-development/SKILL.md").is_file()
           for root in roots):
        return
    if not any(uses_revolution(path) for path in roots):
        return
    print(json.dumps({"hookSpecificOutput": {
        "hookEventName": "SessionStart",
        "additionalContext": (
            "Revolution 工程：写业务前查当前安装的公开门面和同类实现。"
            "写代码按 revolution-framework 的新手注释规范。新增或修改的类型、接口、字段和方法逐项写中文注释。"
            "简单行为一句话，关键实现旁解释原因、顺序、边界与清理。交付前逐项核对 diff 并补齐。"
            "FSM 管阶段，RevScope 管生命周期，业务异步用 RevTask。"
            "编写或修改战斗、逐帧更新、密集 UI 和特效等热路径时先读 revolution-performance，核对 CPU、GPU、内存与缓存，按同负载验收。"
            "Unity 操作先确认绝对工程路径与真实注册表，编辑器验证和运行期验收各用对应证据。"
            "Unity 功能最后一次脚本改动后，先退出 Play，在目标编辑器主动完整重编并核对本次编译无错误，再进 Play 或交付。"
            "功能验收或修 Bug 读 revolution-ai-automation，按需求覆盖和源码证据定位，授权修复后复跑原断言与相关回归。"
            "自动化默认在对话汇报。用户明确要求才出报告，优先用指定路径，省略时查项目文档目录。临时工单消费后立即永久删除。"
        ),
    }}))


if __name__ == "__main__":
    main()
