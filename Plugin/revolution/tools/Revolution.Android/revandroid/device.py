"""用系统 ADB 管理一台明确的 Android 设备。"""
from __future__ import annotations

import os
from pathlib import Path
import re
import shlex
import shutil
import subprocess
from dataclasses import dataclass


class AndroidError(RuntimeError):
    """工具可直接报告的错误，内容不含启动令牌或完整启动命令。"""


@dataclass(frozen=True)
class Device:
    """ADB 清单中的设备身份与当前授权状态。"""

    # 串号是后续命令唯一使用的设备地址。
    serial: str
    # 只有 device 状态可进入已授权的测试会话。
    state: str
    # ADB 不一定会报告型号或 USB 总线信息。
    model: str | None
    # Linux 等环境可报告 USB 拓扑，缺省时留空。
    usb: str | None


def parse_devices(output: str) -> list[Device]:
    """把 adb devices -l 输出解析成设备列表，忽略表头及 daemon 消息。"""
    entries = []
    for line in output.splitlines():
        cells = line.split()
        if len(cells) < 2 or cells[0] in ("List", "*", "adb:"):
            continue
        fields = dict(cell.split(":", 1) for cell in cells[2:] if ":" in cell)
        entries.append(Device(cells[0], cells[1], fields.get("model"), fields.get("usb")))
    return entries


def locate_adb(explicit: str | None) -> str:
    """优先用显式 SDK 工具，其次使用当前终端的 ADB 或配置的 SDK。"""
    if explicit:
        path = Path(explicit).expanduser()
        app = shutil.which(explicit)
        if app or path.is_file():
            return str(Path(app or path).resolve())
        raise AndroidError("指定的 adb 不存在")
    app = shutil.which("adb")
    if app:
        return app
    name = "adb.exe" if os.name == "nt" else "adb"
    for key in ("ANDROID_SDK_ROOT", "ANDROID_HOME"):
        sdk = os.environ.get(key)
        if sdk:
            path = Path(sdk) / "platform-tools" / name
            if path.is_file():
                return str(path)
    raise AndroidError("找不到 adb，请用 --adb 指定 Android SDK platform-tools 下的工具")


def select_device(entries: list[Device], serial: str | None) -> Device:
    """自动选择只适用于清单中恰有一台设备，授权与连接状态必须就绪。"""
    candidates = [device for device in entries if serial is None or device.serial == serial]
    if len(candidates) != 1:
        states = ", ".join(f"{item.serial}={item.state}" for item in entries) or "空"
        raise AndroidError(f"请用 --serial 选择一台设备。当前 ADB 清单 {states}")
    device = candidates[0]
    if device.state != "device":
        raise AndroidError(f"设备 {device.serial} 状态为 {device.state}，请检查 USB 连接和调试授权")
    if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_.:-]*", device.serial):
        raise AndroidError("ADB 设备串号格式无效")
    return device


class Adb:
    """每个设备动作都绑定同一串号，所有子进程和返回值都有明确的等待边界。"""

    def __init__(self, exe: str, serial: str | None = None):
        """保留 ADB 程序路径与已选中的设备串号。"""
        # 同一会话始终使用配置的 ADB 可执行文件。
        self.exe = exe
        # 尚未选设备时只用于枚举，设备操作绑定明确串号。
        self.serial = serial

    def run(self, *args: str, timeout: float = 12, check: bool = True, secrets: tuple[str, ...] = ()):
        """执行单条 ADB 命令，错误和输出清除本会话秘密。"""
        prefix = [self.exe, "-s", self.serial] if self.serial else [self.exe]
        try:
            # adb shell 的远端是 Android sh，逐参数引号可保留嵌套 Activity 的 $ 及启动参数。
            command = ("shell", shlex.join(args[1:])) if args[0] == "shell" else args
            completed = subprocess.run([*prefix, *command], capture_output=True, timeout=timeout)
        except (OSError, subprocess.TimeoutExpired) as exc:
            raise AndroidError(f"ADB {args[0]} 无法完成（{type(exc).__name__}）") from None
        output = completed.stdout.decode("utf-8", errors="replace")
        error = completed.stderr.decode("utf-8", errors="replace")
        for value in secrets:
            output = output.replace(value, "[已隐藏]")
            error = error.replace(value, "[已隐藏]")
        if check and completed.returncode != 0:
            raise AndroidError(f"ADB {args[0]} 退出 {completed.returncode}，{(error or output).strip()[:1500]}")
        return completed.returncode, output.strip()

    def devices(self) -> list[Device]:
        """用 ADB 清单发现串号和授权状态。"""
        return parse_devices(self.run("devices", "-l")[1])

    def pids(self, package: str) -> set[int]:
        """读取包的活动进程号，设备断开时不能伪装成应用已退出。"""
        code, result = self.run("shell", "pidof", package, check=False)
        if not result and code in (0, 1):
            if self.run("get-state")[1] == "device":
                return set()
        if code == 0 and result and all(value.isascii() and value.isdecimal() for value in result.split()):
            return {int(value) for value in result.split()}
        raise AndroidError("读取安卓应用进程身份失败")

    def forward(self, remote: int) -> int:
        """请 ADB 分配空闲的主机端口，避免覆盖其他调试会话。"""
        value = self.run("forward", "tcp:0", f"tcp:{remote}")[1]
        if not value.isascii() or not value.isdecimal() or not 1 <= int(value) <= 65535:
            raise AndroidError("ADB 未返回有效转发端口，请使用支持 tcp:0 的 platform-tools")
        return int(value)

    def remove_forward(self, local: int, remote: int) -> None:
        """仅删除仍属于本串号与目标端口的转发，其他已存在的映射保留。"""
        lines = self.run("forward", "--list")[1].splitlines()
        expected = [self.serial, f"tcp:{local}", f"tcp:{remote}"]
        if any(line.split() == expected for line in lines):
            self.run("forward", "--remove", f"tcp:{local}")

    def launcher(self, package: str) -> str:
        """从已安装 APK 的 Android 启动入口解析 Activity。"""
        reply = self.run("shell", "cmd", "package", "resolve-activity", "--brief", "-a",
                         "android.intent.action.MAIN", "-c", "android.intent.category.LAUNCHER", package)[1]
        return component_name(package, reply.splitlines()[-1] if reply else "")

    def observations(self, package: str) -> dict:
        """取 Android 系统的边界快照，无法读到的字段保留 null。"""
        def shell(*args: str) -> str:
            """按采样边界读取单个系统服务的文本，服务无数据时留空。"""
            try:
                code, value = self.run("shell", *args, timeout=9, check=False)
            except AndroidError:
                return ""
            return value if code == 0 else ""

        meminfo = shell("dumpsys", "meminfo", package)
        thermal = shell("dumpsys", "thermalservice")
        battery = shell("dumpsys", "battery")
        pss = find_number(r"\bTOTAL PSS:\s*([\d,]+)", meminfo)
        if pss is None:
            pss = find_number(r"^\s*TOTAL\s+([\d,]+)\s", meminfo)
        battery_tenths = find_number(r"^\s*temperature:\s*(-?\d+)", battery)
        return {
            "totalPssKiB": pss,
            "thermalStatus": find_number(r"^\s*Thermal Status:\s*([0-6])\s*$", thermal),
            "batteryTemperatureC": battery_tenths / 10 if battery_tenths is not None else None,
        }


def find_number(pattern: str, value: str) -> int | None:
    """读取明确命名的整数计数器，兼容千位分隔。"""
    match = re.search(pattern, value, re.MULTILINE | re.IGNORECASE)
    return int(match[1].replace(",", "")) if match else None


def package_name(name: str) -> str:
    """校验用于 Android shell 的应用标识，只接收合法包名。"""
    if not re.fullmatch(r"[A-Za-z][A-Za-z0-9_]*(?:\.[A-Za-z][A-Za-z0-9_]*)+", name):
        raise AndroidError("Android 包名无效")
    return name


def component_name(package: str, value: str) -> str:
    """限定启动 Activity 属于调用者给出的包，拒绝其它应用和 shell 参数。"""
    owner, sep, activity = value.partition("/")
    if not sep:
        owner, activity = package, value
    if owner != package or not re.fullmatch(r"\.?[A-Za-z_$][\w$]*(?:\.[A-Za-z_$][\w$]*)*", activity, flags=re.ASCII):
        raise AndroidError("没有找到目标包的可启动 Activity，请检查包名或指定 --activity")
    return f"{owner}/{activity}"


def apk_package(apk: Path, adb: str, explicit_aapt: str | None) -> str:
    """安装前用 SDK aapt 校验 APK 实际包名，避免把错误文件装到设备。"""
    suffix = ".exe" if os.name == "nt" else ""
    tool = explicit_aapt
    if not tool:
        roots = [Path(adb).resolve().parent.parent]
        roots.extend(Path(os.environ[key]) for key in ("ANDROID_SDK_ROOT", "ANDROID_HOME") if os.environ.get(key))
        choices = [entry for root in roots for entry in (root / "build-tools").glob(f"*/aapt{suffix}")]
        if choices:
            tool = str(max(choices, key=lambda entry: tuple(int(part) for part in re.findall(r"\d+", entry.parent.name))))
    if not tool:
        raise AndroidError("安装前需用 aapt 校验 APK 包名，请指定 --aapt 或配置 Android Build Tools")
    try:
        result = subprocess.run([tool, "dump", "badging", str(apk)], capture_output=True, timeout=20)
    except (OSError, subprocess.TimeoutExpired) as exc:
        raise AndroidError(f"aapt 无法读取 APK（{type(exc).__name__}）") from None
    text = result.stdout.decode("utf-8", errors="replace")
    found = re.search(r"(?m)^package:\s+name='([^']+)'", text)
    if result.returncode or not found:
        raise AndroidError("aapt 无法取得 APK 的实际包名")
    return package_name(found[1])
