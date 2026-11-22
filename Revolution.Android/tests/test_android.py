"""无设备 CI 回归。ADB 被替身接管，HTTP 与 RevMCP 使用真实本机连接。"""
from __future__ import annotations

from contextlib import contextmanager
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
from pathlib import Path
import sys
import tempfile
import threading
import unittest
from unittest.mock import Mock, patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from rev_android import backends, unity_results
from revandroid.device import Adb, AndroidError, parse_devices, select_device
from revandroid.session import DeviceSession, Launch


class FakeDevice:
    """为回归提供一台带进程身份与端口状态的 ADB 替身。"""

    # 固定输入串号，用于识别同一测试设备。
    serial = "test-usb"
    # 测试不启真 ADB，只保留调用方使用的程序名。
    exe = "adb"
    # 跟独立 Android 冒烟包使用相同的测试标识。
    package = "com.revolution.androidsmoke"

    def __init__(self, port):
        """建好初始状态，测试时由 DeviceSession 完成启动和端点接管。"""
        # 测试 HTTP 服务分配的真实主机回环端口。
        self.port = port
        # 模拟测试进程仅在收到起停命令时变更状态。
        self.alive = False
        # 当前转发的主机和手机端口，用于回收断言。
        self.forwarding = None
        # 记录启动时收到的一次性 HTTP 令牌，供测试服务器校验。
        self.token = None
        # 与手机回执一起检查的本轮启动标识。
        self.launch_id = None
        # 默认回报正确身份，失败用例可以模拟认领失败。
        self.bad_identity = False
        # 记录 ADB 动作顺序，不保存包含凭据的参数。
        self.commands = []
        # HTTP 服务器实际收到的业务动作和期限。
        self.called = []
        # 记录已确认消费的工单编号。
        self.acked = set()
        # 供 HTTP 轮询读取的测试工单结果。
        self.jobs = {}

    def run(self, *args, **options):
        """记录 ADB 启动所用的参数，模拟安装后的 Activity 和进程。"""
        self.commands.append(args[:3])
        if args[:3] == ("shell", "am", "start"):
            self.token = args[args.index("revAutomationToken") + 1]
            self.launch_id = args[args.index("revAutomationLaunchId") + 1]
            self.alive = True
            return 0, "Status: ok"
        if args[:3] == ("shell", "am", "force-stop"):
            self.alive = False
        return 0, ""

    def pids(self, package):
        """只返回这个测试包的进程。"""
        assert package == self.package
        return {2101} if self.alive else set()

    def launcher(self, package):
        """提供可供 am start 使用的复合 Activity 名称。"""
        return package + "/com.unity3d.player.UnityPlayerActivity"

    def forward(self, remote):
        """用本机 HTTP 测试服务替代 ADB 转发的终点。"""
        self.forwarding = (self.port, remote)
        return self.port

    def remove_forward(self, local, remote):
        """验证实际被移除的转发正是本次建立的映射。"""
        assert (local, remote) == self.forwarding
        self.forwarding = None


@contextmanager
def runtime():
    """搭建与 Unity 端点协议一致的本机服务，测试真实请求、轮询和 ACK。"""
    class Handler(BaseHTTPRequestHandler):
        """只允许夹具当前启动的令牌和会话。"""

        def log_message(self, *_):
            """静默 HTTP 访问日志，令牌与请求正文不出测试进程。"""

        def reply(self, status, data):
            """把测试回执从实际 Socket 发给客户端。"""
            body = json.dumps(data).encode("utf-8")
            self.send_response(status)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def authorized(self):
            """所有请求携带同一启动会话、令牌和本机路径。"""
            if self.headers.get("Authorization") != "Bearer " + device.token:
                self.reply(401, {"error": "Unauthorized"})
                return False
            if self.headers.get("X-Rev-Session") != device.launch_id:
                self.reply(409, {"error": "SessionChanged"})
                return False
            return True

        def do_GET(self):
            """返回当前设备身份或同一工单的最终回执。"""
            if not self.authorized():
                return
            if self.path == "/health":
                self.reply(200, {
                    "protocol": 1, "applicationId": "bad.app" if device.bad_identity else device.package,
                    "sessionId": device.launch_id, "launchId": device.launch_id,
                    "pid": 2101, "product": "Python mock Android", "buildId": "a" * 32,
                    "platform": "Android", "memoryJobs": True,
                })
            elif self.path.startswith("/jobs/"):
                request = device.jobs[self.path.split("/")[-1]]
                action = request["action"]
                if action == "runtime.capabilities":
                    data = {"actions": [{"id": "fixture.state", "readOnly": True,
                                          "inputSchema": {"type": "object", "additionalProperties": False}}],
                            "commands": [], "gmEnabled": False}
                else:
                    data = {"ready": True}
                self.reply(200, {"id": request["id"], "sessionId": device.launch_id, "success": True, "data": data})
                if action == "runtime.quit":
                    device.alive = False
            else:
                self.reply(404, {"success": False})

        def do_POST(self):
            """记录被接单的 JSON 和结果确认，保留请求参数供断言。"""
            if not self.authorized():
                return
            data = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
            if self.path == "/call":
                device.called.append(data)
                device.jobs[data["id"]] = data
                self.reply(202, {"success": True, "id": data["id"]})
            elif self.path.endswith("/ack"):
                device.acked.add(self.path.split("/")[-2])
                self.reply(200, {"success": True})
            else:
                self.reply(404, {"success": False})

    server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    device = FakeDevice(server.server_port)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    try:
        yield device
    finally:
        server.shutdown()
        server.server_close()
        thread.join()


class AndroidTests(unittest.TestCase):
    """覆盖设备选择、USB 生命周期、真实协议回执与失败边界。"""

    def test_devices(self):
        """多台设备不能自动猜测，未经授权的设备不能开始。"""
        rows = parse_devices("List of devices attached\nA device usb:1-3 model:Pixel\nB unauthorized\n")
        self.assertEqual(select_device(rows, "A").usb, "1-3")
        with self.assertRaises(AndroidError):
            select_device(rows, None)
        with self.assertRaises(AndroidError):
            select_device(rows, "B")

    def test_release_only_owned_forward_and_shell_quotes(self):
        """保留不属于本会话的 ADB 转发，Activity 的 $ 原样送到远端。"""
        runner = Adb("adb", "test-usb")
        with patch("revandroid.device.subprocess.run") as call:
            call.return_value = Mock(returncode=0, stdout=b"", stderr=b"")
            runner.run("shell", "am", "start", "-n", "com.demo.app/com.demo.Outer$Nested")
            self.assertEqual(call.call_args.args[0][-1], "am start -n 'com.demo.app/com.demo.Outer$Nested'")
        with patch.object(runner, "run", return_value=(0, "other tcp:14221 tcp:19991")) as call:
            runner.remove_forward(14221, 19991)
            call.assert_called_once_with("forward", "--list")

    def test_routed_agent_plan(self):
        """实际 RevMCP 运行一份带断言的计划，收到结论后归还 USB 进程和端口。"""
        cli, Hub, RuntimeClient, run_plan, _ = backends()
        from revautomation.game import GameClient

        with runtime() as adb:
            session = DeviceSession(adb, Launch(adb.package, isolate=True), RuntimeClient)
            with session:
                endpoint = session.endpoint_file
                self.assertTrue(endpoint.exists())
                hub = Hub(runtime_endpoint=endpoint, cli_root=cli, timeout=6)
                result = run_plan(hub, {"steps": [{"target": "game", "action": "fixture.state",
                                                  "expect": [{"path": "/data/ready", "equals": True}]}]})
                self.assertTrue(result["success"], result)
                self.assertTrue(GameClient(endpoint, 6).call("fixture.state")["data"]["ready"])
            self.assertFalse(endpoint.exists())
            self.assertIsNone(adb.forwarding)
            self.assertFalse(adb.alive)
            self.assertEqual(session.cleanup, {"errors": [], "forward": "removed", "player": "stopped"})
            self.assertTrue(adb.called)
            self.assertTrue(all("deadlineUtc" not in args for args in adb.called))
            self.assertEqual(adb.acked, {args["id"] for args in adb.called})

    def test_wrong_package_identity_preserves_process(self):
        """错误身份既不能执行计划，也不能关闭未认领进程。"""
        _, _, client, _, _ = backends()
        with runtime() as adb:
            adb.bad_identity = True
            session = DeviceSession(adb, Launch(adb.package), client)
            with self.assertRaisesRegex(AndroidError, "运行时身份"):
                with session:
                    self.fail("错误身份不应进入测试")
            self.assertEqual(adb.called, [])
            self.assertTrue(adb.alive)
            self.assertIsNone(adb.forwarding)
            self.assertFalse(session.endpoint_file.exists())

    def test_unapproved_restart(self):
        """目标应用仍在运行时，普通命令不擅自停止或覆盖它。"""
        _, _, client, _, _ = backends()
        with runtime() as adb:
            adb.alive = True
            with self.assertRaisesRegex(AndroidError, "仍在运行"):
                with DeviceSession(adb, Launch(adb.package), client):
                    self.fail("不应接管正在运行的应用")
            self.assertEqual(adb.commands, [])

    def test_official_unity_xml(self):
        """官方 Test Runner 的 XML 有独立结果，空运行不会写成测试通过。"""
        with tempfile.TemporaryDirectory() as directory:
            xml = Path(directory) / "tests.xml"
            xml.write_text('<test-run total="2" passed="2" failed="0" result="Passed"/>', encoding="utf-8")
            self.assertTrue(unity_results(xml)["success"])
            xml.write_text('<test-run total="0" passed="0" failed="0" result="Passed"/>', encoding="utf-8")
            self.assertFalse(unity_results(xml)["success"])

    def test_example_conditions(self):
        """示例计划沿用 RevMCP 的真实 JSON Pointer 断言格式。"""
        backends()
        from revautomation.pipeline import validate_condition
        path = Path(__file__).resolve().parents[1] / "examples/revgame-perf.json"
        plan = json.loads(path.read_text(encoding="utf-8"))
        for step in plan["steps"] + plan["finally"]:
            for condition in step.get("expect", []):
                validate_condition(condition)
            if "until" in step:
                validate_condition({key: value for key, value in step["until"].items()
                                    if key not in ("timeoutSeconds", "intervalSeconds")})


if __name__ == "__main__":
    unittest.main()
