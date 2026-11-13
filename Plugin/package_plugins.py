"""从 Revolution 主仓库导出 Codex 插件的技能和运行时工具。"""

import argparse
import re
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
PLUGIN = ROOT / "Plugin/revolution"
SKILLS = (
    "revolution-framework", "revolution-unity-editor", "revolution-ai-automation",
    "revolution-performance", "revolution-automation-tester",
)
# 源仓库相对文档链接在安装缓存里没有源目录。分发时转换为官方仓库链接。
DOC_LINK = re.compile(rb"(?<=\()(?:(?:\.\./)+)(Revolution\.Document/[^)\s]+)")
DOC_BASE = b"https://github.com/Yokino337088/Revolution/blob/main/"


def sources():
    """按固定清单收集技能和 Python 运行文件，排除测试、工单和本机配置。"""
    for name in SKILLS:
        source = ROOT / "Skills" / name
        for path in sorted(source.rglob("*")):
            relative = path.relative_to(source)
            if path.is_file() and not any(part.startswith(".") or part == "__pycache__" for part in relative.parts):
                yield path, PLUGIN / "skills" / name / relative
    for name, entry, package in (
        ("Revolution.RevCLI", "rev.py", "revautomation"),
        ("Revolution.RevMCP", "rev_mcp.py", "revmcp"),
    ):
        source = ROOT / name
        target = PLUGIN / "tools" / name
        for path in [source / entry, *sorted((source / package).glob("*.py"))]:
            yield path, target / path.relative_to(source)
    yield ROOT / "Revolution.RevCLI/commands.json", PLUGIN / "tools/Revolution.RevCLI/commands.json"
    yield ROOT / "LICENSE", PLUGIN / "LICENSE"
    yield ROOT / "LICENSE", ROOT / "Plugin/revgame/LICENSE"


def main():
    """导出时更新差异，CI 用 --check 确认缓存副本与源文件一致。"""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="只校验导出副本，不写文件。")
    args = parser.parse_args()
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    different = []
    expected = set()
    for source, target in sources():
        expected.add(target)
        content = source.read_bytes()
        if source.suffix == ".md":
            content = DOC_LINK.sub(lambda match: DOC_BASE + match[1], content)
        current = target.read_bytes() if target.is_file() else None
        if current is not None and current.replace(b"\r\n", b"\n") == content.replace(b"\r\n", b"\n"):
            continue
        different.append(target.relative_to(ROOT).as_posix())
        if not args.check:
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(content)

    # skills 和 tools 只保存导出文件。过时文件需从插件删除，避免旧模块继续被发现。
    for directory in (PLUGIN / "skills", PLUGIN / "tools"):
        for path in sorted(directory.rglob("*")):
            if path.is_file() and "__pycache__" not in path.parts and path not in expected:
                different.append(path.relative_to(ROOT).as_posix())
                if not args.check:
                    path.unlink()
    if args.check and different:
        print("插件导出已过期，请运行 python Plugin/package_plugins.py")
        print("\n".join(different))
        return 1
    print(f"插件导出文件 {len(expected)} 个，{'检查通过' if args.check else f'更新 {len(different)} 项'}。")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
