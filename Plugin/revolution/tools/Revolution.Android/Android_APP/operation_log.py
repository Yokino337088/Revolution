"""接线器的旁路运行记录。只在电脑保存短摘要，手机可按需分页读取。"""
from dataclasses import dataclass
from datetime import datetime
import json
import os
from pathlib import Path
import queue
import re
import sys
import threading
import time
from urllib.parse import unquote

# 仓库和官方插件将 Android 工具和 RevMCP 并排放置，共用脱敏规则。
sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "Revolution.RevMCP"))
from revmcp.activity import safe_text, safe_value

# 一轮会话的有界历史与每次下发的最大页长。
CAPACITY = 4096
PAGE_SIZE = 40
# 优先保留明确标识的工单和结果头，大图片不会整体反序列化。
READ_LIMIT = 64 * 1024
# 仅输出可信标签着色，游戏返回的控制字符没有终端控制权。
CONTROL_CHARS = re.compile(r"[\x00-\x1f\x7f-\x9f\u202a-\u202e\u2066-\u2069]")
# 游戏返回的请求编号固定为 32 位十六进制。
JOB_ID = re.compile(r"[0-9a-f]{32}")
# 控制台只输出工单终态。等待中的状态仍用于手机历史。
OUTCOMES = {"success": "成功", "failed": "失败", "cancelled": "取消或超时", "unknown": "未获回执"}


def line(text, limit=384):
    """统一裁剪与移除控制字符，复用 RevMCP 的自由文本脱敏。"""
    return CONTROL_CHARS.sub(" ", safe_text(text, limit))


def json_preview(data, limit=900):
    """对有限深度和长度的回执做脱敏预览，原始回执仍交给 RevMCP。"""
    return line(json.dumps(safe_value(data), ensure_ascii=False, separators=(",", ":")), limit)


def envelope(body, heavy_field):
    """小对象正常反序列化。大回执只解析位于 payload/data 之前的标识和状态。"""
    try:
        if len(body) > READ_LIMIT:
            prefix = body[:READ_LIMIT]
            match = re.search(rb',\s*"' + heavy_field + rb'"\s*:', prefix)
            if not match:
                return {}
            body = prefix[:match.start()] + b"}"
        result = json.loads(body)
        return result if isinstance(result, dict) else {}
    except (ValueError, RecursionError):
        return {}


def colored_console(mode):
    """输出管道支持显式开启颜色，交互 Windows 终端使用系统的 VT 支持。"""
    if mode == "never" or (mode == "auto" and ("NO_COLOR" in os.environ or not sys.stderr.isatty())):
        return False
    if os.name != "nt" or not sys.stderr.isatty():
        return True
    import ctypes
    from ctypes import wintypes
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.GetStdHandle.argtypes = (wintypes.DWORD,)
    kernel.GetStdHandle.restype = wintypes.HANDLE
    kernel.GetConsoleMode.argtypes = (wintypes.HANDLE, ctypes.POINTER(wintypes.DWORD))
    kernel.SetConsoleMode.argtypes = (wintypes.HANDLE, wintypes.DWORD)
    terminal = kernel.GetStdHandle(wintypes.DWORD(-12))
    flags = wintypes.DWORD()
    return bool(kernel.GetConsoleMode(terminal, ctypes.byref(flags)) and
                kernel.SetConsoleMode(terminal, flags.value | 4)) or mode == "always"


class Console:
    """后台写入彩色控制台；终端慢时最多缓存 512 行，不拖慢游戏工单。"""

    # 颜色只分配给接线器自己的短标签。
    COLORS = {"RevGM": "95", "MCP": "94", "安卓": "36"}

    def __init__(self, color):
        """启动一个轻量终端写线程，游戏的网络线程只投递短行。"""
        # 当前 stderr 是否支持 ANSI 转义。
        self.color = colored_console(color)
        # 终端缓存的行数上限。
        self.lines = queue.Queue(maxsize=512)
        # 输出滞后时用于提示省略行数。
        self.dropped = 0
        # 仅保护省略计数，终端等待不占用该锁。
        self.lock = threading.Lock()
        # 此进程所有会话共用的后台写线程。
        self.worker = threading.Thread(target=self._write, name="RevPhone-Console", daemon=True)
        self.worker.start()

    def emit(self, tag, message):
        """投递已限长的运行信息，队列已满时不等待。"""
        try:
            self.lines.put_nowait((datetime.now().strftime("%H:%M:%S"), tag, line(message, 1300)))
        except queue.Full:
            with self.lock:
                self.dropped += 1

    def _write(self):
        """串行写一整行并重置颜色，文件重定向默认保留纯文本。"""
        try:
            while True:
                item = self.lines.get()
                if item is None:
                    break
                at, tag, message = item
                label = f"[{tag}]"
                if self.color:
                    label = f"\x1b[1;{self.COLORS.get(tag, '37')}m{label}\x1b[0m"
                sys.stderr.write(f"{at} {label} {message}\n")
                with self.lock:
                    dropped, self.dropped = self.dropped, 0
                if dropped:
                    sys.stderr.write(f"[警告] 控制台繁忙，略过 {dropped} 行输出。\n")
                sys.stderr.flush()
        except (OSError, ValueError):
            # 输出窗口被关闭不影响已经提交的测试操作。
            return

    def close(self):
        """退出时给已接收的少量输出收尾，保留有界等待。"""
        try:
            self.lines.put(None, timeout=.25)
            self.worker.join(.8)
        except queue.Full:
            pass


@dataclass
class Event:
    """一张 Unity 工单对应一条记录，接单及最终结果都更新同一序号。"""

    # 此手机会话的递增序号，用于向更早的记录分页。
    seq: int
    # 记录电脑首次转交工单的本地墙钟毫秒。
    time_ms: int
    # 计算实际端到端工单耗时的单调时钟。
    started: float
    # 真实请求中的 Unity 动作名。
    action: str
    # 限长的请求预览，RevGM 直接显示具体命令行。
    arguments: str
    # 接单时关联的本地 MCP 工具名，可为空。
    tool: str
    # 计划中声明的步骤编号及阶段，仅用于定位控制台中的调用。
    step: str
    # 已识别的阶段为 steps、finally，其他值按普通调用处理。
    phase: str
    # Agent 为本次 GM 声明的意图。旧客户端没有这个字段时留空。
    intent: str
    # 转交、接单和终态字面值由手机列表映射。
    state: str = "delivering"
    # Unity 返回的结果摘要。
    message: str = ""

    def row(self):
        """向手机输出界面所需的最小数据。"""
        return dict(seq=self.seq, timeMs=self.time_ms, action=self.action, arguments=self.arguments,
                    tool=self.tool, state=self.state, message=self.message)


class OperationLog:
    """一台手机一轮游戏，留存经过实际 Android 通道的命令和回执。"""

    def __init__(self, console=None, console_log="mcp"):
        """初始化旁路历史，只有展开消息列表的手机才会来查询。"""
        # 电脑控制台可以关闭，记录与游戏派发仍然正常。
        self.console = console
        # mcp 显示收到的工具调用，gm 只保留 RevGM。两个模式共用手机历史。
        self.console_log = console_log
        # 只持锁读写少数记录，任何网络或终端操作在锁外进行。
        self.lock = threading.Lock()
        # 本轮最多保留 CAPACITY 项，由序号快速定位分页。
        self.events = {}
        # 通过 Unity 请求编号更新原有行。
        self.jobs = {}
        # 活动记录的最大序号，和传输层工单编号独立。
        self.latest = 0
        # 首屏相同修订号时返回空增量。
        self.revision = 0
        # 每次手机实际打开列表后的 HTTPS 读取，供电脑查看本轮流量状态。
        self.reads = 0

    def emit(self, tag, value):
        """允许离线回归不创建终端线程。"""
        if self.console:
            self.console.emit(tag, value)

    def submit(self, body, context):
        """记录已提交的工单和实际工具来源，控制台等待工单终态。"""
        request = envelope(body, b"payload")
        job, action = request.get("id"), request.get("action")
        if not isinstance(job, str) or not JOB_ID.fullmatch(job) or not isinstance(action, str) or not action:
            return None
        payload = request.get("payload")
        if action == "gm.exec" and payload is None and len(body) > READ_LIMIT:
            # 电脑接线器已限制请求体大小。长 GM 仍需取到合法命令名，长结果继续只看外层。
            try:
                payload = json.loads(body).get("payload")
            except (ValueError, RecursionError):
                payload = None
        arguments = ""
        if isinstance(payload, dict) and payload:
            arguments = line(payload.get("command")) if action == "gm.exec" else json_preview(payload, 384)
        tool = line(context.get("tool"), 128)
        step = line(context.get("step"), 64)
        phase = context.get("phase", "")
        intent = line(context.get("intent"), 256) if action == "gm.exec" else ""
        with self.lock:
            if job in self.jobs:
                return job
            self.latest += 1
            self.revision += 1
            self.events[self.latest] = (job, Event(self.latest, time.time_ns() // 1_000_000,
                                                    time.monotonic(), line(action), arguments, tool, step, phase, intent))
            self.jobs[job] = self.events[self.latest][1]
            expired = self.events.pop(self.latest - CAPACITY, None)
            if expired:
                self.jobs.pop(expired[0], None)
        return job

    def response(self, method, path, submitted, status, body, session):
        """核对实际工单终态。HTTP 202、轮询和确认只更新原记录。"""
        if submitted:
            if status == 202:
                self._update(submitted, "accepted")
            else:
                if status in (502, 503, 504):
                    self._update(submitted, "unknown", f"传输 HTTP {status}")
                else:
                    self._update(submitted, "failed", f"提交 HTTP {status}")
            return
        if method not in ("GET", "DELETE") or not path.startswith("/jobs/") or not JOB_ID.fullmatch(path[6:]):
            return
        job = path[6:]
        with self.lock:
            if job not in self.jobs:
                return
        if status != 200:
            self._update(job, "unknown", f"查询 HTTP {status}")
            return
        reply = envelope(body, b"data")
        if reply.get("id") != job:
            return
        if reply.get("pending") is True:
            state = "cancelling" if reply.get("state") == "CancellationRequested" else "accepted"
            self._update(job, state)
            return
        if reply.get("sessionId") != session or type(reply.get("success")) is not bool:
            return
        state = "success" if reply["success"] else "failed"
        error = reply.get("error")
        if error == "CancelledOrTimeout":
            state = "cancelled"
        elif error == "ResultExpired":
            state = "unknown"
        self._update(job, state, reply.get("message") or error or "", reply.get("data"))

    def _update(self, job, state, message="", data=None):
        """按工单更新历史并输出一次终态。连接查询和内部工具发现不刷控制台。"""
        with self.lock:
            item = self.jobs.get(job)
            if item is None or item.state == state or item.state in OUTCOMES:
                return
            message = line(message)
            if item.action == "gm.exec" and isinstance(data, dict):
                if state == "success" and data.get("success") is False:
                    state = "failed"
                if data.get("message"):
                    message = line(data["message"])
            item.state = state
            item.message = "" if state == "success" and message == "完成" else message
            self.revision += 1
        outcome = OUTCOMES.get(state)
        if not outcome:
            return
        waited = f"等待 {round((time.monotonic() - item.started) * 1000)} ms"
        if item.action == "gm.exec" and item.arguments:
            # 普通参数保留原命令行。很长的计划正文只显示命令名，避免每条 GM 再刷屏。
            command = item.arguments
            if len(command) > 120:
                command = command.split(maxsplit=1)[0] + " [长参数略]"
            # 有游戏回执时优先读 Unity 的 GM 耗时。无该字段时标明电脑实际等待时间。
            elapsed = data.get("elapsedMs") if isinstance(data, dict) else None
            if type(elapsed) in (int, float) and 0 <= elapsed < float("inf"):
                duration = f"{elapsed:.2f} ms"
            else:
                duration = waited
            self.emit("RevGM", f"{command} · 意图 {item.intent or '未提供'} · {outcome} · {duration}")
        elif self.console_log == "mcp":
            # rev_call 和 rev_run 会先读能力以校验输入，只显示 Agent 显式发起的发现。
            if item.action == "runtime.capabilities" and item.tool not in ("rev_discover", "discover"):
                return
            tool = item.tool or "运行时"
            if item.step and item.phase in ("steps", "finally"):
                phase_label = "收尾" if item.phase == "finally" else "步骤"
                tool += f" {phase_label} {item.step}"
            detail = f" · {line(item.message, 160)}" if state != "success" and item.message else ""
            self.emit("MCP", f"{tool} · {item.action} · {outcome} · {waited}{detail}")

    def page(self, data):
        """首次读取或回到顶部取最新一页，向下滚动按 before 取更早的记录。"""
        before, revision = data.get("before", 0), data.get("revision", -1)
        if type(before) is not int or type(revision) is not int or before < 0 or revision < -1:
            raise ValueError("Invalid activity cursor")
        with self.lock:
            self.reads += 1
            oldest = max(1, self.latest - CAPACITY + 1)
            unchanged = before == 0 and revision == self.revision
            highest = min(before - 1, self.latest) if before else self.latest
            limit = max(oldest - 1, highest - PAGE_SIZE)
            events = [] if unchanged else [self.events[index][1].row() for index in range(highest, limit, -1)]
            return dict(oldest=oldest, latest=self.latest, revision=self.revision, unchanged=unchanged, events=events)

    def counts(self):
        """电脑回环端点查询本轮的操作总数和手机记录请求数，便于排查暂停或断线。"""
        with self.lock:
            return {"operations": self.latest, "activityReads": self.reads}

    def plan_finished(self, data):
        """显示 RevMCP 汇总的断言和收尾结论，游戏动作成功不代表计划通过。"""
        if type(data.get("success")) is not bool:
            raise ValueError("Invalid MCP plan summary")
        steps, cleanup = data.get("steps", 0), data.get("cleanup", 0)
        if type(steps) is not int or type(cleanup) is not int or min(steps, cleanup) < 0:
            raise ValueError("Invalid MCP plan counts")
        if self.console_log == "mcp":
            tool = "run" if data.get("tool") == "run" else "rev_run"
            name = line(data.get("name"), 160) or "未命名计划"
            outcome = "通过" if data["success"] else "未通过"
            failure = line(data.get("failure"), 160)
            detail = f" · {failure}" if not data["success"] and failure else ""
            self.emit("MCP", f"{tool} · {name} · {outcome} · 业务步骤 {steps}，收尾步骤 {cleanup}{detail}")


def mcp_context(header, intent_header=""):
    """读取电脑 RevMCP 发来的有界记录头。缺失或无效头不影响游戏请求。"""
    result = {}
    if len(header) <= 2048:
        try:
            context = json.loads(unquote(header))
            if isinstance(context, dict):
                result.update({key: context[key] for key in ("tool", "phase", "step") if isinstance(context.get(key), str)})
        except (ValueError, RecursionError):
            pass
    if 0 < len(intent_header) <= 4096:
        result["intent"] = unquote(intent_header)
    return result
