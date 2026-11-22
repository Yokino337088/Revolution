#!/usr/bin/env python3
"""把 Android 测试包的任务经 USB 转发给桌面 RevMCP。"""
from __future__ import annotations

import argparse
from dataclasses import asdict
import json
import math
from pathlib import Path
import sys
from xml.etree import ElementTree

from revandroid.device import Adb, AndroidError, apk_package, locate_adb, package_name, select_device
from revandroid.session import DeviceSession, Launch


# 源仓库与已打包插件都把三个工具目录并排放置。
TOOLS_ROOT = Path(__file__).resolve().parent.parent


def backends(tools_root: Path = TOOLS_ROOT):
    """加载同版本的 RevMCP 服务、计划执行器和 RevCLI 依赖。"""
    cli = tools_root.resolve() / "Revolution.RevCLI"
    mcp = tools_root.resolve() / "Revolution.RevMCP"
    if not (cli / "revautomation/client.py").is_file() or not (mcp / "revmcp/backends.py").is_file():
        raise AndroidError("请与同版本的 Revolution.RevCLI 和 Revolution.RevMCP 一起使用")
    sys.path.insert(0, str(cli))
    sys.path.insert(0, str(mcp))
    from revmcp.backends import Hub, RuntimeClient
    from revmcp.reporting import run_with_reports
    from revmcp.server import Server
    return cli, Hub, RuntimeClient, run_with_reports, Server


def unity_results(path: Path) -> dict:
    """读取 Unity Test Framework 的 NUnit XML，保留自己的测试结论与计数。"""
    try:
        with path.open("rb") as xml:
            body = xml.read(16 * 1024 * 1024 + 1)
        if len(body) > 16 * 1024 * 1024:
            raise AndroidError("Unity 测试 XML 超过 16 MiB")
        root = ElementTree.fromstring(body)
        if root.tag != "test-run":
            raise AndroidError("需要 NUnit 3 格式的 Unity test-run XML")
        counts = {name: int(root.get(name, "0")) for name in ("total", "passed", "failed", "skipped", "inconclusive")}
    except (OSError, ValueError, ElementTree.ParseError) as exc:
        raise AndroidError(f"Unity 测试结果读取失败（{type(exc).__name__}）") from None
    if any(value < 0 for value in counts.values()) or not root.get("result"):
        raise AndroidError("Unity 测试 XML 的结果或计数无效")
    return {
        "success": root.get("result") == "Passed" and counts["passed"] > 0 and counts["failed"] == 0,
        "result": root.get("result"), **counts,
    }


def write_result(result: dict, secret: str = "") -> None:
    """用 UTF-8 把业务结果交给终端或 Agent，防止输出本次连接令牌。"""
    payload = json.dumps(result, ensure_ascii=False, indent=2, allow_nan=False)
    if secret:
        payload = payload.replace(secret, "[已隐藏]")
    sys.stdout.buffer.write((payload + "\n").encode("utf-8"))
    sys.stdout.buffer.flush()


def launch_arguments(parser: argparse.ArgumentParser) -> None:
    """为 MCP 和单次计划配置同一套启动、身份和清理参数。"""
    parser.add_argument("--project", type=Path, help="可选的已打开 Unity 工程，同时提供 Editor 入口")
    parser.add_argument("--package", required=True, help="Android 应用包名")
    parser.add_argument("--apk", type=Path, help="可选，在启动前验包并安装测试 APK")
    parser.add_argument("--aapt", help="验包用的 Android SDK aapt，默认从 SDK 查找")
    parser.add_argument("--activity", help="可选的目标包 Activity，默认读取安装后的启动入口")
    parser.add_argument("--build-report", type=Path, help="可选，核对实际 Player 的 BuildReport buildId")
    parser.add_argument("--replace", action="store_true", help="明确允许覆盖安装同一包")
    parser.add_argument("--restart", action="store_true", help="明确允许先停止目标包的现有进程再开测试")
    parser.add_argument("--isolate", action="store_true", help="给支持的项目传入 -revTestSession，计划内继续核对")
    parser.add_argument("--keep-running", action="store_true", help="结束时保留测试应用，仍移除本次转发")
    parser.add_argument("--device-port", type=int, help="可选的设备监听端口，默认每次随机选择")
    parser.add_argument("--startup-timeout", type=float, default=90, help="等待 APK 就绪的秒数")
    parser.add_argument("--timeout", type=float, default=120, help="每项 RevMCP 动作的秒数")


def main() -> int:
    """处理设备发现、实时 MCP 服务及可重跑的单次测试计划。"""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", help="Android SDK 的 adb 路径")
    parser.add_argument("--serial", help="ADB 串号，多台设备时必选")
    parser.add_argument("--tools-root", type=Path, default=TOOLS_ROOT, help="可选，同版本 RevCLI 和 RevMCP 所在的目录")
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("devices", help="列出 ADB 设备与授权状态")
    inspect = sub.add_parser("inspect-apk", help="读取 APK 的实际应用包名，无需安装")
    inspect.add_argument("apk", type=Path)
    sample = sub.add_parser("device-sample", help="按需读取手机进程内存、热状态和电池温度")
    sample.add_argument("--package", required=True)
    mcp = sub.add_parser("mcp", help="经 USB 启动 RevMCP 标准输入输出服务")
    launch_arguments(mcp)
    mcp.add_argument("--compact-tools", action="store_true", help="只发现当前工程和手机的实际能力")
    run = sub.add_parser("run", help="运行 RevMCP 测试计划，把结构化结果返回给调用方")
    launch_arguments(run)
    run.add_argument("plan", help="计划 JSON 文件，- 表示从标准输入读取")
    run.add_argument("--device-samples", action="store_true", help="计划起止记 PSS、热状态和电池温度")
    run.add_argument("--unity-results", type=Path, help="附加独立生成的 Unity Test Framework XML")
    unity = sub.add_parser("unity-results", help="读取 Unity Test Framework XML 摘要")
    unity.add_argument("xml", type=Path)
    args = parser.parse_args()

    session = None
    try:
        if args.command == "unity-results":
            result = unity_results(args.xml)
            write_result(result)
            return 0 if result["success"] else 1
        base = Adb(locate_adb(args.adb))
        if args.command == "inspect-apk":
            apk = args.apk.resolve()
            if not apk.is_file() or apk.suffix.lower() != ".apk":
                raise AndroidError("请选择实际存在的 .apk 文件")
            write_result({"success": True, "applicationId": apk_package(apk, base.exe, None), "path": str(apk)})
            return 0
        entries = base.devices()
        if args.command == "devices":
            write_result({"success": True, "devices": [asdict(item) for item in entries]})
            return 0
        device = select_device(entries, args.serial)
        if args.command == "device-sample":
            package = package_name(args.package)
            observations = Adb(base.exe, device.serial).observations(package)
            write_result({"success": True, "device": asdict(device), "applicationId": package, "observations": observations})
            return 0
        if not math.isfinite(args.timeout) or not 0 < args.timeout <= 3600:
            raise AndroidError("--timeout 须在 0 到 3600 秒之间")
        if not math.isfinite(args.startup_timeout) or not 0 < args.startup_timeout <= 900:
            raise AndroidError("--startup-timeout 须在 0 到 900 秒之间")
        if args.project and not (args.project / "ProjectSettings/ProjectVersion.txt").is_file():
            raise AndroidError("--project 须指向 Unity 工程目录")
        plan = None
        unity = None
        if args.command == "run":
            plan = json.load(sys.stdin.buffer) if args.plan == "-" else json.loads(
                Path(args.plan).read_text(encoding="utf-8-sig"))
            if not isinstance(plan, dict):
                raise AndroidError("RevMCP 计划根节点须为 JSON 对象")
            if args.unity_results:
                unity = unity_results(args.unity_results)

        cli, hub_factory, client, run_plan, server = backends(args.tools_root)
        adb = Adb(base.exe, device.serial)
        launch = Launch(
            package=args.package, apk=args.apk, activity=args.activity, aapt=args.aapt,
            build_report=args.build_report, restart=args.restart, replace=args.replace,
            isolate=args.isolate, keep_running=args.keep_running,
            device_port=args.device_port, startup_timeout=args.startup_timeout,
        )
        session = DeviceSession(adb, launch, client)
        with session:
            hub = hub_factory(args.project, session.endpoint_file, cli_root=cli, timeout=args.timeout)
            if args.command == "mcp":
                # stdout 保留给 MCP 协议，设备与包身份提示走 stderr 且不含令牌。
                print(f"Android 就绪 {device.serial} / {launch.package} / Build {session.health['buildId']}",
                      file=sys.stderr)
                server(hub, compact_tools=args.compact_tools).serve()
            else:
                before = adb.observations(launch.package) if args.device_samples else None
                outcome = run_plan(hub, plan)
                after = adb.observations(launch.package) if args.device_samples else None
        if args.command == "mcp":
            for error in session.cleanup["errors"]:
                print(error, file=sys.stderr)
            return 1 if session.cleanup["errors"] else 0
        result = {
            "success": outcome.get("success") is True and (unity is None or unity["success"])
                       and not session.cleanup["errors"],
            "device": asdict(device),
            "runtime": {key: session.health[key] for key in ("pid", "product", "platform", "applicationId", "buildId")},
            "plan": outcome,
            "deviceBefore": before,
            "deviceAfter": after,
            "unityTests": unity,
            "cleanup": session.cleanup,
        }
        write_result(result, session.token)
        return 0 if result["success"] else 1
    except (OSError, ValueError, RuntimeError, TypeError, KeyboardInterrupt) as exc:
        result = {"success": False, "error": type(exc).__name__, "message": str(exc)}
        if session is not None:
            result["cleanup"] = session.cleanup
        secret = session.token if session is not None else ""
        if args.command == "mcp":
            print(json.dumps(result, ensure_ascii=False).replace(secret, "[已隐藏]") if secret
                  else json.dumps(result, ensure_ascii=False), file=sys.stderr)
        else:
            write_result(result, secret)
        return 130 if isinstance(exc, KeyboardInterrupt) else 1


if __name__ == "__main__":
    raise SystemExit(main())
