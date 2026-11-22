"""持有一次 ADB 转发、应用启动和本机 RevMCP 端点的生命周期。"""
from __future__ import annotations

from dataclasses import dataclass
import json
from pathlib import Path
import re
import secrets
import tempfile
import time

from .device import Adb, AndroidError, apk_package, component_name, package_name


@dataclass(frozen=True)
class Launch:
    """一次由调用方选定的包、启动选项和验收条件。"""

    # 将启动和清理限制在这一个 Android 包。
    package: str
    # 已安装时可以留空，传入后先核对实际 APK 包名。
    apk: Path | None = None
    # 空值时使用 Android 报告的启动 Activity。
    activity: str | None = None
    # 可覆盖从 SDK 自动发现的验包工具。
    aapt: str | None = None
    # 可选的构建身份证据，用 Player 的实际 buildId 校验。
    build_report: Path | None = None
    # 明确允许停止目标包已存在的进程。
    restart: bool = False
    # 明确允许覆盖已安装的同名包。
    replace: bool = False
    # 传入由业务项目负责解释的临时存档开关。
    isolate: bool = False
    # 保留本次测试应用，退出时仍移除自己持有的转发。
    keep_running: bool = False
    # 手机上的监听端口需在 1024 到 65535，空值时随机选择。
    device_port: int | None = None
    # Activity 启动和 HTTP 就绪共用的最长等待，单位秒。
    startup_timeout: float = 90


def read_build_identity(path: Path | None) -> str | None:
    """可选地使用 Unity 已成功的安卓 BuildReport 锚定实际安装版本。"""
    if path is None:
        return None
    try:
        data = json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, ValueError) as exc:
        raise AndroidError(f"无法读安卓 BuildReport（{type(exc).__name__}）") from None
    if not isinstance(data, dict) or data.get("target") != "Android" or data.get("result") != "Succeeded" \
            or data.get("errors") != 0 or not (data.get("development") or data.get("automation")):
        raise AndroidError("需要构建成功的 Android 开发或自动化包报告")
    identity = data.get("buildId", "")
    if not isinstance(identity, str) or not re.fullmatch(r"[A-Fa-f0-9]{32}", identity):
        raise AndroidError("BuildReport 缺少有效 buildId")
    return identity.lower()


class DeviceSession:
    """启动并认领一个隔离的测试连接，退出时清理本次进程和转发。"""

    def __init__(self, adb: Adb, launch: Launch, client_factory):
        """生成一次性凭据，并准备与 RevMCP 共用的客户端类型。"""
        # 已绑定实际设备，后续的安装、进程与转发都从这里发出。
        self.adb = adb
        # 整轮共用的启动与清理配置。
        self.launch = launch
        # 用同一个运行时客户端实现令牌、轮询与结果确认。
        self.client_factory = client_factory
        # 校验后的包名也是整个会话的操作范围。
        self.package = package_name(launch.package)
        # 手机把启动标识同时当会话标识，便于电脑认领。
        self.launch_id = secrets.token_hex(16)
        # 每轮重新生成，用于授权本次的回环请求。
        self.token = secrets.token_hex(32)
        # 手机监听端口与电脑 ADB 端口独立分配。
        self.device_port = launch.device_port if launch.device_port is not None else 18000 + secrets.randbelow(12000)
        # ADB 返回端口后才产生所有权，失败清理时据此归还。
        self.host_port = None
        # 保存通过核验的真实运行时身份，清理时用它确认原进程。
        self.health = None
        # 就绪探测和退出共用点对点 HTTP 客户端。
        self.client = None
        # 按会话持有端点文件及凭据，作用域结束时删除。
        self.temp = None
        # 保证重复关闭不会再次删除转发或向应用发命令。
        self.closed = False
        # 记录启动请求已发送，就绪失败时仍要检查是否留下应用。
        self.started = False
        # 向调用者交代进程、转发和清理错误。
        self.cleanup = {"errors": [], "forward": "none", "player": "not-started"}

    @property
    def endpoint_file(self) -> Path:
        """在本次宿主进程的私有临时目录中供 RevMCP 读取。"""
        return Path(self.temp.name) / "runtime.json"

    def __enter__(self):
        """建连失败也归还本次已获得的资源。"""
        try:
            self.open()
        except (Exception, KeyboardInterrupt):
            self.close()
            raise
        return self

    def __exit__(self, *_):
        """Agent 完成、计划失败或交互中断后收尾。"""
        self.close()

    def open(self) -> None:
        """先校验安装内容，再用相同串号启动并验证实际手机进程。"""
        expected_build = read_build_identity(self.launch.build_report)
        apk = self.launch.apk.resolve() if self.launch.apk else None
        if not self.adb.serial:
            raise AndroidError("请先选定一台已授权的 ADB 设备")
        if not 1024 <= self.device_port <= 65535:
            raise AndroidError("手机端口须在 1024 到 65535 之间")
        if apk is not None:
            if not apk.is_file() or apk.suffix.lower() != ".apk":
                raise AndroidError("请传可直接安装的 .apk 文件")
            if apk_package(apk, self.adb.exe, self.launch.aapt) != self.package:
                raise AndroidError("APK 的实际包名与 --package 不同，安装未执行")
        component = component_name(self.package, self.launch.activity) if self.launch.activity else None

        # 安装升级也可能结束正在运行的应用，先检查是否明确允许重开。
        running = self.adb.pids(self.package)
        if running and not self.launch.restart:
            raise AndroidError("目标应用仍在运行，先退出或明确使用 --restart")
        if apk is not None:
            _, installed = self.adb.run("shell", "pm", "path", self.package, check=False)
            if installed.startswith("package:") and not self.launch.replace:
                raise AndroidError("目标包已安装，使用 --replace 明确允许替换后再安装")
            flags = ["-r"] if self.launch.replace else []
            self.adb.run("install", *flags, str(apk), timeout=180)
        component = component or self.adb.launcher(self.package)
        if self.adb.pids(self.package):
            if not self.launch.restart:
                raise AndroidError("目标应用在准备期间启动，请确认后再重试")
            self.adb.run("shell", "am", "force-stop", self.package)
            if self.adb.pids(self.package):
                raise AndroidError("旧进程仍存在，暂不发起测试启动")

        self.host_port = self.adb.forward(self.device_port)
        self.temp = tempfile.TemporaryDirectory(prefix="rev-android-")
        # 手机无需写出凭据。先构造已知会话，以令牌和启动 ID 访问原有 /health。
        self.endpoint_file.write_text(json.dumps({
            "protocol": 1, "url": f"http://127.0.0.1:{self.host_port}",
            "token": self.token, "sessionId": self.launch_id, "launchId": self.launch_id,
            "memoryJobs": True, "relativeDeadlines": True,
        }), encoding="utf-8")
        self.client = self.client_factory(self.endpoint_file, 4)
        arguments = [
            "shell", "am", "start", "-W", "-n", component,
            "--ez", "revAutomation", "true",
            "--ei", "revAutomationPort", str(self.device_port),
            "--es", "revAutomationToken", self.token,
            "--es", "revAutomationLaunchId", self.launch_id,
        ]
        if self.launch.isolate:
            arguments.extend(("--es", "unity", "-revTestSession"))
        deadline = time.monotonic() + self.launch.startup_timeout
        self.started = True
        _, response = self.adb.run(*arguments, timeout=self.launch.startup_timeout, secrets=(self.token,))
        if "Error:" in response or not re.search(r"(?im)^Status:\s*ok\s*$", response):
            raise AndroidError(f"安卓 Activity 未确认启动，{response[:600]}")
        reply = self.wait_health(deadline)
        if expected_build and str(reply.get("buildId", "")).lower() != expected_build:
            raise AndroidError("手机运行的 buildId 与指定 Android BuildReport 不同")

        # 把核对过的设备身份保存到本机临时端点，RevMCP 用的仍是原协议。
        data = json.loads(self.endpoint_file.read_text(encoding="utf-8"))
        data.update({key: reply[key] for key in ("pid", "product", "buildId", "platform", "applicationId")})
        self.endpoint_file.write_text(json.dumps(data), encoding="utf-8")

    def wait_health(self, deadline: float) -> dict:
        """等待真实 Unity 启动并认领匹配的应用，端口通了才开始验证身份。"""
        last_error = "尚未接到回复"
        while time.monotonic() < deadline:
            try:
                reply = self.client.request("GET", "/health")
            except (OSError, RuntimeError, ValueError) as exc:
                last_error = str(exc).replace(self.token, "[已隐藏]")[:350]
                time.sleep(min(0.25, max(0, deadline - time.monotonic())))
                continue
            self.check_identity(reply)
            self.health = reply
            return reply
        raise AndroidError(f"等待 Android /health 超时，{last_error}。请核对测试构建宏、网络权限和启动 Activity")

    def check_identity(self, reply: dict) -> None:
        """同时校验 Android 包、会话、启动标识及从 ADB 查到的实际进程。"""
        if not isinstance(reply, dict) or reply.get("protocol") != 1 or reply.get("platform") != "Android" \
                or reply.get("applicationId") != self.package or reply.get("sessionId") != self.launch_id \
                or reply.get("launchId") != self.launch_id or reply.get("memoryJobs") is not True:
            raise AndroidError("手机返回的运行时身份与本次测试启动不符")
        pid = reply.get("pid")
        if type(pid) is not int or pid <= 0 or pid not in self.adb.pids(self.package):
            raise AndroidError("手机运行时进程与 ADB 实测进程不符")
        if not isinstance(reply.get("product"), str) or not isinstance(reply.get("buildId"), str):
            raise AndroidError("运行时未提供构建身份")

    def stop_owned_player(self) -> None:
        """结束已核验且由本次启动的进程，应用自行重开后保留新进程。"""
        if not self.started:
            return
        if self.launch.keep_running:
            self.cleanup["player"] = "kept"
            return
        if self.health is None:
            if self.adb.pids(self.package):
                self.cleanup["player"] = "unverified"
                self.cleanup["errors"].append("未核验到启动进程身份，保留应用进程")
            else:
                self.cleanup["player"] = "ended"
            return
        pid = self.health["pid"]
        if pid not in self.adb.pids(self.package):
            self.cleanup["player"] = "ended"
            return

        reply = self.client.request("GET", "/health")
        self.check_identity(reply)
        if reply["pid"] != pid:
            self.cleanup["player"] = "original-ended"
            return
        try:
            self.client.call("runtime.quit")
        except (OSError, RuntimeError, TimeoutError):
            # Unity 可能先退出，导致动作回执在 HTTP 连接关闭前来不及取回。
            pass
        deadline = time.monotonic() + 5
        while time.monotonic() < deadline:
            if pid not in self.adb.pids(self.package):
                self.cleanup["player"] = "stopped"
                return
            time.sleep(0.25)

        # 回退出场前再认领一次，避免把用户已经新开的进程停掉。
        reply = self.client.request("GET", "/health")
        self.check_identity(reply)
        if reply["pid"] != pid:
            self.cleanup["player"] = "original-ended"
            return
        self.adb.run("shell", "am", "force-stop", self.package)
        if pid in self.adb.pids(self.package):
            raise AndroidError("工具启动的测试进程仍在运行")
        self.cleanup["player"] = "stopped"

    def close(self) -> None:
        """先结束己方应用，再检查并移除己方转发，最后删除本机凭据。"""
        if self.closed:
            return
        self.closed = True
        try:
            self.stop_owned_player()
        except (OSError, RuntimeError, ValueError, KeyboardInterrupt) as exc:
            self.cleanup["errors"].append("关闭测试应用：" + str(exc).replace(self.token, "[已隐藏]"))
        try:
            if self.host_port is not None:
                self.adb.remove_forward(self.host_port, self.device_port)
                self.cleanup["forward"] = "removed"
        except (OSError, RuntimeError) as exc:
            self.cleanup["errors"].append("移除 ADB 转发：" + str(exc).replace(self.token, "[已隐藏]"))
        finally:
            if self.temp is not None:
                self.temp.cleanup()
