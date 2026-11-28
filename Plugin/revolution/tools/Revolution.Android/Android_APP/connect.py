#!/usr/bin/env python3
"""电脑监听手机的出站 HTTPS 请求，并给正式 RevMCP 提供本机回环端点。"""
from __future__ import annotations

import argparse
from collections import deque
from contextlib import ExitStack, contextmanager, suppress
from dataclasses import dataclass, field
import hashlib
import hmac
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import ipaddress
import json
import os
from pathlib import Path
import re
import secrets
import socket
import sqlite3
import ssl
import sys
import tempfile
import threading
import time
import uuid

# USB 模式复用既有的设备选择及 SDK 定位；局域网模式不调用 ADB。
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from revandroid.device import Adb, AndroidError, locate_adb, select_device
from revandroid.session import read_build_identity
from host_trust import HostTrust, default_directory
from operation_log import Console, OperationLog, mcp_context

# 手机必须输入电脑界面显示的端口，USB 的设备侧使用这个固定值。
PORT = 18767
# 与 Unity 对单个 /call 正文的限制相同。
MAX_REQUEST = 4 * 1024 * 1024
# 与 RevMCP 截图及 JSON 回执上限相同。
MAX_RESULT = 24 * 1024 * 1024
# 首次授权窗口为三分钟，已有授权的手机不依赖短码时钟。
PAIR_WINDOW = 180


def note(message: str) -> None:
    """本地终端显示接线进度，stdout 只发布可绑定的端点路径。"""
    print(message, file=sys.stderr, flush=True)


def check_identity(info: dict, session: str, package: str, build: str | None, pid: int | None = None):
    """认领前及保活时核对真实包、启动号、构建及进程，拒绝替换后的游戏。"""
    if (info.get("protocol") != 1 or info.get("platform") != "Android" or info.get("memoryJobs") is not True or
            info.get("applicationId") != package or info.get("sessionId") != session or info.get("launchId") != session or
            type(info.get("pid")) is not int or info["pid"] <= 0 or (pid is not None and pid != info["pid"]) or
            not re.fullmatch(r"[0-9a-fA-F]{32}", str(info.get("buildId", ""))) or
            (build is not None and info["buildId"].lower() != build.lower())):
        raise AndroidError("游戏的包、构建、启动号或进程身份不符")


def route(method: str, path: str) -> bool:
    """局域网只传输现有运行时的健康、提交、轮询、确认和取消协议。"""
    return ((method, path) in {("GET", "/health"), ("POST", "/call")} or
            (method in {"GET", "DELETE"} and re.fullmatch(r"/jobs/[a-f0-9]{32}", path) is not None) or
            (method == "POST" and re.fullmatch(r"/jobs/[a-f0-9]{32}/ack", path) is not None))


@dataclass
class Command:
    """电脑端的一次 HTTP 往返，传输编号与 Unity 业务工单分别维护。"""

    # 原始 RuntimeClient 发出的 HTTP 动词。
    method: str
    # 已经过许可的游戏路径。
    path: str
    # 不解析或修改游戏的 JSON 参数。
    body: bytes
    # 每次转交使用独立编号，手机结果仅能完成匹配的请求。
    id: str = field(default_factory=lambda: uuid.uuid4().hex)
    # 交付给手机后即使网络抖动也不会二次派发。
    delivered: bool = False
    # 唤醒正在等此结果的电脑端线程。
    done: threading.Event = field(default_factory=threading.Event)
    # 默认归还断线错误，仅在收到匹配的回执后替换。
    result: tuple[int, bytes] = (503, b'{"error":"Device disconnected"}')


class Channel:
    """一个电脑监听实例接受一台手机，条件变量把 Agent 的指令即时送到手机挂起的请求。"""

    def __init__(self, expected_package=None, expected_build=None, trust=None, console=None, console_log="mcp"):
        """准备三分钟配对窗口和两组彼此独立的认证凭据。"""
        # 电脑端显示的临时口令。
        self.code = f"{secrets.randbelow(100000000):08d}"
        # 手机通过配对才能拿到的传输令牌。
        self.phone_token = secrets.token_hex(32)
        # 本机 RevMCP 私有端点的令牌。
        self.local_token = secrets.token_hex(32)
        # 可选的电脑端包名约束。
        self.expected_package = expected_package
        # 由成功的 Android BuildReport 提取的 Build ID。
        self.expected_build = expected_build
        # 电脑持久授权库与本轮临时令牌分开。
        self.trust = trust
        # 工单历史只在电脑，按实际收到的请求与回执更新。
        self.operations = OperationLog(console, console_log)
        # 以下状态和挂起请求都在同一条件变量下发布。
        self.changed = threading.Condition()
        # 配对成功后绑定到所选游戏的唯一启动号。
        self.session = ""
        # 手机选定的 Android 安装包。
        self.package = ""
        # 只有手机和电脑核对同一份健康回执后才对 Agent 发布。
        self.identity = None
        # 待派发及已派发的请求按编号查找，数量有界。
        self.pending = {}
        # 每条指令仅由一个长轮询领取一次。
        self.queue = deque()
        # 避免同一会话的两个读取者同时领取变更指令。
        self.polling = False
        # 结束原因仅用于本机界面和等待者。
        self.closed = ""
        # 用户主动断开与线路中断分别向终端报告。
        self.normal_close = False
        # 初次配对时限由单调时钟计量。
        self.started = time.monotonic()
        # 手机每次长轮询或状态提交都会续租。
        self.last_seen = self.started
        # 五次失败或配对超时后废弃本轮口令。
        self.attempts = 0

    def next_code(self, now):
        """等待首次授权时轮换短码，过期和超过试错次数的码不能再次提交。"""
        with self.changed:
            if self.session or self.closed or now - self.started < PAIR_WINDOW:
                return None
            self.code = f"{secrets.randbelow(100000000):08d}"
            self.attempts = 0
            self.started = now
            return self.code

    def pair(self, authorization, session, data, resume=False):
        """用短码首次授权或用保存的手机凭据重连，两条路径都签发本轮临时令牌。"""
        with self.changed:
            if self.closed or self.session:
                return 409, {}
            if resume:
                # 长期凭据只能进入重连接口，不能调用测试或接管已经开始的游戏会话。
                token = authorization.removeprefix(b"Bearer ")
                if (not authorization.startswith(b"Bearer ") or not re.fullmatch(rb"[0-9a-f]{64}", token)
                        or self.trust is None or not self.trust.recognizes(token)):
                    return 401, {}
            elif not self.code or time.monotonic() - self.started >= PAIR_WINDOW:
                return 410, {}
            elif not hmac.compare_digest(authorization, ("Bearer " + self.code).encode()):
                self.attempts += 1
                if self.attempts >= 5:
                    self.code = ""
                return 403, {}
            package = data.get("applicationId")
            if (data.get("protocol") != 1 or not re.fullmatch(r"[a-f0-9]{32}", session) or
                    not isinstance(package, str) or not re.fullmatch(r"[A-Za-z][\w]*(?:\.[A-Za-z][\w]*)+", package) or
                    (self.expected_package and self.expected_package != package)):
                return 409, {}
            ticket = {"sessionId": session, "token": self.phone_token}
            if not resume and self.trust is not None and data.get("remember") is True:
                ticket["trustToken"] = self.trust.remember()
            self.package, self.session = package, session
            self.code = ""
            self.last_seen = time.monotonic()
            self.changed.notify_all()
            self.operations.emit("安卓", f"{package} · {'已授权直连' if resume else '首次授权成功'}")
            return 200, ticket

    def authorized(self, headers, local=False):
        """分别校验手机与本地 Agent 的令牌，已撤销会话立即拒绝。"""
        with self.changed:
            token = self.local_token if local else self.phone_token
            return (bool(self.session) and not self.closed and
                    hmac.compare_digest(headers.get("Authorization", "").encode(), ("Bearer " + token).encode()) and
                    hmac.compare_digest(headers.get("X-Rev-Session", "").encode(), self.session.encode()))

    def ready(self, info):
        """手机验证成功后额外核对电脑指定的构建，可重复提交同一进程身份。"""
        with self.changed:
            check_identity(info, self.session, self.package, self.expected_build,
                           self.identity["pid"] if self.identity else None)
            self.identity = info
            self.last_seen = time.monotonic()
            self.changed.notify_all()

    def call(self, method, path, body, timeout=5):
        """在有界队列中派发一次，超时只移除本次等待，不重新执行游戏操作。"""
        job = Command(method, path, body)
        with self.changed:
            if self.closed or self.identity is None or len(self.pending) >= 12:
                return 503, b'{"error":"Device unavailable or busy"}'
            self.pending[job.id] = job
            self.queue.append(job.id)
            self.changed.notify_all()
        try:
            if not job.done.wait(timeout):
                return 504, b'{"error":"Phone response timed out; request was not retried"}'
            return job.result
        finally:
            with self.changed:
                self.pending.pop(job.id, None)
                with suppress(ValueError):
                    self.queue.remove(job.id)

    def exchange(self, result_id, status, body, idle=8):
        """先交还上一条结果，再长等下一条；超时晚到的结果只丢弃，不重派其副作用。"""
        with self.changed:
            if self.polling:
                return 409, None
            self.last_seen = time.monotonic()
            if result_id:
                previous = self.pending.get(result_id)
                if previous and previous.delivered and not previous.done.is_set():
                    previous.result = status, body
                    previous.done.set()
            self.polling = True
            try:
                self.changed.wait_for(lambda: bool(self.queue) or bool(self.closed), timeout=idle)
                if self.closed:
                    return 410, None
                if not self.queue:
                    return 204, None
                job = self.pending[self.queue.popleft()]
                job.delivered = True
                return 200, job
            finally:
                self.polling = False

    def close(self, reason, normal=False):
        """唤醒手机和所有电脑调用，未完成的动作报告不确定结果。"""
        with self.changed:
            if not self.closed:
                self.closed, self.normal_close = reason, normal
                self.queue.clear()
                for job in self.pending.values():
                    job.done.set()
                self.changed.notify_all()


class Handler(BaseHTTPRequestHandler):
    """电脑的手机 HTTPS 和本机 RevMCP 使用不同监听器，固定路由分别鉴权。"""

    # 允许手机复用已经配对的 TLS 连接，避免每次轮询都重新握手。
    protocol_version = "HTTP/1.1"

    def log_message(self, *_):
        """配对和业务请求均不记录原始头和参数。"""

    def reply(self, code, body=b"", headers=None):
        """用定长响应交付游戏数据，手机空闲轮询以 204 返回。"""
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        if code >= 400 or self.path == "/phone/close":
            # 拒绝路径可能尚未读完请求体，关闭此连接防止正文污染后续请求。
            self.send_header("Connection", "close")
            self.close_connection = True
        for key, value in (headers or {}).items():
            self.send_header(key, value)
        self.end_headers()
        self.wfile.write(body)

    def read_body(self, limit):
        """只接收有界定长 JSON；断开或含糊的输入不会递交到游戏。"""
        size = self.headers.get("Content-Length", "0")
        if (self.headers.get("Transfer-Encoding") or len(self.headers.get_all("Content-Length", [])) > 1 or
                not re.fullmatch(r"[0-9]{1,8}", size) or int(size) > limit or
                (self.command != "POST" and int(size) != 0)):
            raise ValueError("Invalid HTTP length")
        body = self.rfile.read(int(size))
        if len(body) != int(size):
            raise ValueError("Incomplete body")
        return body

    def dispatch(self):
        """首先分开两侧权限，再交给只处理本会话的状态机。"""
        try:
            if self.headers.get("Origin") is not None:
                return self.reply(403)
            channel = self.server.channel
            if self.server.local:
                if not channel.authorized(self.headers, local=True):
                    return self.reply(401)
                if self.command == "GET" and self.path == "/mcp/activity":
                    return self.reply(200, json.dumps(channel.operations.counts()).encode())
                if self.command == "POST" and self.path == "/mcp/activity":
                    channel.operations.plan_finished(self.json_body())
                    return self.reply(200, b"{}")
                if not route(self.command, self.path):
                    return self.reply(404)
                body = self.read_body(MAX_REQUEST)
                submitted = None
                if self.command == "POST" and self.path == "/call":
                    with suppress(Exception):
                        context = mcp_context(self.headers.get("X-Rev-MCP", ""), self.headers.get("X-Rev-Intent", ""))
                        submitted = channel.operations.submit(body, context)
                status, response = channel.call(self.command, self.path, body)
                # 记录错误不应影响已发出的操作，也不能导致 Agent 重试。
                with suppress(Exception):
                    channel.operations.response(self.command, self.path, submitted, status, response, channel.session)
                return self.reply(status, response)
            if self.command != "POST":
                return self.reply(405)
            if self.path in {"/phone/pair", "/phone/resume"}:
                code, info = channel.pair(self.headers.get("Authorization", "").encode(),
                    self.headers.get("X-Rev-Session", ""), self.json_body(), resume=self.path == "/phone/resume")
                return self.reply(code, json.dumps(info).encode())
            if not channel.authorized(self.headers):
                return self.reply(401)
            if self.path == "/phone/activity":
                data = channel.operations.page(self.json_body())
                return self.reply(200, json.dumps(data, ensure_ascii=False, separators=(",", ":")).encode("utf-8"))
            if self.path == "/phone/ready":
                channel.ready(self.json_body())
                return self.reply(200, b"{}")
            if self.path == "/phone/close":
                reason = self.json_body()
                failed = reason.get("failed") is True
                channel.close("手机测试会话异常结束" if failed else "手机已断开连接", normal=not failed)
                return self.reply(200, b"{}")
            if self.path != "/phone/exchange":
                return self.reply(404)
            result_id = self.headers.get("X-Rev-Command")
            status = self.headers.get("X-Rev-Status", "200")
            if (result_id is not None and not re.fullmatch(r"[a-f0-9]{32}", result_id)) or not re.fullmatch(r"[2-5][0-9]{2}", status):
                return self.reply(400)
            code, job = channel.exchange(result_id, int(status), self.read_body(MAX_RESULT if result_id else 0))
            if job is None:
                return self.reply(code)
            self.reply(code, job.body, {"X-Rev-Command": job.id, "X-Rev-Method": job.method, "X-Rev-Path": job.path})
        except (ValueError, AndroidError):
            self.reply(400, b'{"error":"Invalid request or device identity"}')
        except sqlite3.Error:
            self.reply(503, b'{"error":"Computer authorization store unavailable"}')

    def json_body(self):
        """配对和就绪只需要小型 JSON 对象，不分配截图大小的控制缓冲。"""
        data = json.loads(self.read_body(16384))
        if not isinstance(data, dict):
            raise ValueError("Expected JSON object")
        return data

    # GET 转交只读运行时请求。
    do_GET = dispatch
    # POST 接受手机长轮询和 Agent 工单。
    do_POST = dispatch
    # DELETE 允许本机 Agent 取消指定游戏工单。
    do_DELETE = dispatch


class Listener(ThreadingHTTPServer):
    """电脑持有一个有界外部 TLS 监听和一个只在回环开放的 RevMCP 入口。"""

    def __init__(self, address, channel, tls=None):
        """两侧通过相同的内存会话交接请求，仅手机侧需要 TLS。"""
        # 这对监听实例共享的配对及工单状态。
        self.channel = channel
        # 回环端不接受手机控制接口。
        self.local = tls is None
        # 限制未认证的慢连接数量，给本机调用留独立配额。
        self.slots = threading.BoundedSemaphore(20)
        super().__init__(address, Handler)
        if tls:
            self.socket = tls.wrap_socket(self.socket, server_side=True, do_handshake_on_connect=False)

    def process_request(self, request, address):
        """在工作线程握手，单个尚未完成 TLS 的手机不能卡住主监听。"""
        if not self.slots.acquire(blocking=False):
            self.shutdown_request(request)
            return
        request.settimeout(20)
        try:
            super().process_request(request, address)
        except BaseException:
            self.slots.release()
            raise

    def process_request_thread(self, request, address):
        """处理完或断开后归还并发名额。"""
        try:
            super().process_request_thread(request, address)
        finally:
            self.slots.release()

    def handle_error(self, request, client_address):
        """提前离线的客户端只结束该请求，不打印可能包含私有参数的堆栈。"""


def server_tls(folder, trust):
    """加载电脑的固定证书供 TLS 握手，载入后删除本次生成的临时 PEM 副本。"""
    from cryptography import x509
    from cryptography.hazmat.primitives import serialization

    identity = trust.identity()
    cert = x509.load_pem_x509_certificate(identity)
    ctx = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    ctx.minimum_version = ssl.TLSVersion.TLSv1_2
    with tempfile.NamedTemporaryFile(dir=folder, delete=False) as pem:
        path = Path(pem.name)
        try:
            pem.write(identity)
            pem.close()
            ctx.load_cert_chain(str(path))
        finally:
            pem.close()
            path.unlink(missing_ok=True)
    return ctx, hashlib.sha256(cert.public_bytes(serialization.Encoding.DER)).hexdigest()


@contextmanager
def endpoint_file(path, data):
    """先在私有文件完成凭据，再独占发布；收尾时只删除本进程发布的同一文件。"""
    with tempfile.NamedTemporaryFile(dir=path.parent, prefix=".rev-phone-", delete=False) as temp:
        staging = Path(temp.name)
    try:
        staging.write_text(json.dumps(data, ensure_ascii=False), encoding="utf-8")
        os.link(staging, path)
        try:
            yield path
        finally:
            if path.exists() and os.path.samefile(staging, path):
                path.unlink()
    finally:
        staging.unlink(missing_ok=True)


def lan_address() -> str:
    """询问操作系统的默认 IPv4 出口，只做本地路由选择，不发送应用数据。"""
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as probe:
        probe.connect(("192.0.2.1", 9))
        return probe.getsockname()[0]


def reverse(adb, host_port):
    """独占手机回环上的端口；已有其它工具的映射时由 --no-rebind 拒绝。"""
    adb.run("reverse", "--no-rebind", f"tcp:{PORT}", f"tcp:{host_port}")


def undo_reverse(adb, host_port):
    """仅移除同一设备上仍映射到本轮电脑端口的反向转发。"""
    code, raw = adb.run("reverse", "--list", check=False)
    if code != 0:
        raise AndroidError("设备已离线，无法确认本轮 ADB reverse 的清理状态")
    if any(line.split()[-2:] == [f"tcp:{PORT}", f"tcp:{host_port}"] for line in raw.splitlines()):
        adb.run("reverse", "--remove", f"tcp:{PORT}")


def serve_on(stack, address, channel, tls=None):
    """启动监听线程，退出时先停接入再归还端口。"""
    server = stack.enter_context(Listener(address, channel, tls))
    threading.Thread(target=server.serve_forever, daemon=True).start()
    stack.callback(server.shutdown)
    return server.server_port


def serve_session(args, path, address, tls, pin, channel):
    """承接一个手机会话并独占发布本轮端点，关闭后释放监听和已有映射。"""
    with ExitStack() as stack:
        usb = args.mode == "usb"
        host_port = serve_on(stack, (address, 0 if usb else args.port), channel, tls)
        if usb:
            base = Adb(locate_adb(args.adb))
            device = select_device(base.devices(), args.serial)
            adb = Adb(base.exe, device.serial)
            reverse(adb, host_port)
            stack.callback(undo_reverse, adb, host_port)
        stack.callback(channel.close, "电脑已结束会话")
        note(f"电脑监听 IP {address}  端口 {host_port}")
        note(f"手机连接电脑 {address}:{PORT if usb else host_port}")
        note("电脑证书指纹 " + pin[:16].upper())
        note("首次连接需要授权，已授权的手机可直接连接。")
        note("一次性配对码 " + channel.code)
        with channel.changed:
            while not channel.identity and not channel.closed:
                now = time.monotonic()
                if code := channel.next_code(now):
                    note("一次性配对码 " + code)
                if channel.session and now - channel.last_seen > 40:
                    raise AndroidError("手机已离线，未收到游戏身份")
                channel.changed.wait(1)
        if channel.closed:
            if channel.normal_close:
                return
            raise AndroidError(channel.closed)
        info = channel.identity
        port = serve_on(stack, ("127.0.0.1", 0), channel)
        data = dict(info, url=f"http://127.0.0.1:{port}", token=channel.local_token, relativeDeadlines=True,
                    companionActivity=True)
        stack.enter_context(endpoint_file(path, data))
        note(f"已连接 {channel.package} / PID {info['pid']} / Build {info['buildId']}")
        note("把下面路径作为 rev_bind 的 runtimeEndpoint 或 RevMCP 的 --runtime-endpoint。按 Ctrl+C 断开。")
        print(path, flush=True)
        # 维持服务时只观察手机的长轮询心跳，避免额外 JNI 或逐帧探测干扰压力测量。
        with channel.changed:
            while not channel.closed:
                if time.monotonic() - channel.last_seen > 40:
                    raise AndroidError("手机长轮询已断开，电脑端点已撤销")
                channel.changed.wait(1)
        note(channel.closed)
        if not channel.normal_close:
            raise AndroidError(channel.closed)


def run(args):
    """开启电脑接线器，常驻模式创建新会话并沿用电脑授权与控制台显示模式。"""
    trust = HostTrust(args.trust_dir)
    build = read_build_identity(args.build_report)
    with ExitStack() as resources:
        private_dir = resources.enter_context(tempfile.TemporaryDirectory(prefix="rev-phone-"))
        console = Console(args.color)
        resources.callback(console.close)
        folder = Path(private_dir)
        path = args.endpoint.expanduser().resolve() if args.endpoint else folder / "runtime.json"
        if path.name != "runtime.json" or path.exists() or not path.parent.is_dir():
            raise AndroidError("--endpoint 需要现有私有目录中尚未使用的 runtime.json")
        address = "127.0.0.1" if args.mode == "usb" else str(ipaddress.IPv4Address(args.listen or lan_address()))
        if address == "0.0.0.0":
            raise AndroidError("请指定手机可直达的电脑网卡 IPv4 地址")
        tls, pin = server_tls(folder, trust)
        while True:
            channel = Channel(args.package, build, trust, console, args.console_log)
            try:
                serve_session(args, path, address, tls, pin, channel)
            except AndroidError as error:
                if not getattr(args, "stay_open", False) or not channel.session:
                    raise
                note("本轮已结束：" + str(error))
            if not getattr(args, "stay_open", False):
                return
            note("等待手机开始下一轮测试。")


def main(argv=None):
    """解析接线、控制台显示模式或撤销手机授权的启动参数。"""
    parser = argparse.ArgumentParser(description=__doc__)
    modes = parser.add_subparsers(dest="mode", required=True)
    lan = modes.add_parser("lan", help="监听电脑局域网网卡，手机主动接入")
    lan.add_argument("--listen", help="电脑指定网卡的 IPv4，默认按系统路由选择")
    lan.add_argument("--port", type=int, default=PORT, help="电脑 HTTPS 端口")
    lan.add_argument("--stay-open", action="store_true", help="一轮结束后继续等待下一台手机")
    usb = modes.add_parser("usb", help="通过 ADB reverse 使用同一套手机客户端协议")
    usb.add_argument("--adb", help="SDK adb 路径")
    usb.add_argument("--serial", help="多台设备时指定串号")
    revoke = modes.add_parser("revoke", help="撤销所有手机已保存的重连授权")
    for mode in (usb, lan, revoke):
        mode.add_argument("--trust-dir", type=Path, default=default_directory(), help="电脑私有授权目录")
    for mode in (usb, lan):
        mode.add_argument("--color", choices=("auto", "always", "never"), default="auto",
                          help="控制台高亮，默认根据终端能力决定")
        mode.add_argument("--console-log", choices=("mcp", "gm"), default="mcp",
                          help="默认显示 MCP 工单与计划摘要，gm 仅显示 RevGM")
        mode.add_argument("--endpoint", type=Path, help="可选的私有 runtime.json 绝对路径")
        mode.add_argument("--package", help="可选，仅接受此游戏包名")
        mode.add_argument("--build-report", type=Path, help="可选，核对 Android rev-build.json")
    try:
        args = parser.parse_args(argv)
        if args.mode == "revoke":
            count = HostTrust(args.trust_dir).revoke()
            note(f"已撤销 {count} 份手机重连授权，新的连接需要首次授权。")
            return 0
        if args.mode == "lan" and not 1 <= args.port <= 65535:
            raise AndroidError("电脑监听端口应在 1 到 65535 之间")
        run(args)
        return 0
    except KeyboardInterrupt:
        note("电脑已结束测试连接。")
        return 0
    except ModuleNotFoundError as error:
        if error.name != "cryptography":
            raise
        note("请先运行 python -m pip install -r requirements.txt 安装证书依赖")
        return 1
    except (OSError, ValueError, AndroidError, sqlite3.Error) as error:
        note("接线失败：" + str(error))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
