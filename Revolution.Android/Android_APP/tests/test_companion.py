"""无设备回归电脑监听、单次派发和撤销；Android Player 的真实帧率通过性能计划验收。"""
from concurrent.futures import ThreadPoolExecutor
from http.client import HTTPConnection, HTTPSConnection
import hashlib
import json
from pathlib import Path
import ssl
import sys
import tempfile
import unittest
from unittest.mock import Mock
from urllib.parse import quote

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import connect
import operation_log


class CompanionTests(unittest.TestCase):
    """使用真实回环 HTTPS 代表一台主动连入的手机，核对本机 Agent 获取的最终数据。"""

    def test_phone_initiates_https_and_returns_exact_result(self):
        """电脑先监听，配对后接受手机长轮询，原封不动交回结果，结束后拒绝旧凭据。"""
        channel = connect.Channel("dev.test")
        sid = "a" * 32
        info = dict(protocol=1, sessionId=sid, launchId=sid, memoryJobs=True, applicationId="dev.test",
                    platform="Android", pid=17, buildId="b" * 32)
        with tempfile.TemporaryDirectory() as directory, connect.ExitStack() as stack:
            folder = Path(directory)
            tls, pin = connect.server_tls(folder, connect.HostTrust(folder / "trust"))
            port = connect.serve_on(stack, ("127.0.0.1", 0), channel, tls)
            local = connect.serve_on(stack, ("127.0.0.1", 0), channel)
            stack.callback(channel.close, "测试结束")
            with ThreadPoolExecutor(2) as executor:
                def device(path, data, authorization, extra=None):
                    """测试手机按独立取得的完整指纹验证电脑，然后向其提交消息。"""
                    ctx = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
                    ctx.check_hostname = False
                    ctx.verify_mode = ssl.CERT_NONE
                    connection = HTTPSConnection("127.0.0.1", port, context=ctx, timeout=12)
                    try:
                        connection.connect()
                        self.assertEqual(hashlib.sha256(connection.sock.getpeercert(True)).hexdigest(), pin)
                        headers = {"Authorization": "Bearer " + authorization, "X-Rev-Session": sid}
                        connection.request("POST", path, data, dict(headers, **(extra or {})))
                        response = connection.getresponse()
                        return response.status, dict(response.getheaders()), response.read()
                    finally:
                        connection.close()

                payload = b'{"protocol":1,"applicationId":"dev.test"}'
                self.assertEqual(device("/phone/activity", b"{}", "incorrect")[0], 401)
                self.assertEqual(device("/phone/pair", payload, "incorrect")[0], 403)
                code = channel.code
                result = device("/phone/pair", payload, code)
                self.assertEqual(result[0], 200)
                token = json.loads(result[2])["token"]
                self.assertEqual(device("/phone/pair", payload, code)[0], 409)
                self.assertEqual(device("/phone/ready", json.dumps(dict(info, launchId="old")), token)[0], 400)
                self.assertEqual(device("/phone/ready", json.dumps(info), token)[0], 200)
                activity = device("/phone/activity", b'{"before":0,"revision":-1}', token)
                self.assertEqual(activity[0], 200)
                self.assertEqual(json.loads(activity[2])["events"], [])
                poll = executor.submit(device, "/phone/exchange", b"", token)

                def agent():
                    """本机一侧走原版协议提交单个请求，而后等待手机回传。"""
                    client = HTTPConnection("127.0.0.1", local, timeout=8)
                    try:
                        client.request("GET", "/jobs/" + "c" * 32, headers=
                                       {"Authorization": "Bearer " + channel.local_token, "X-Rev-Session": sid})
                        response = client.getresponse()
                        return response.status, response.read()
                    finally:
                        client.close()

                waiting = executor.submit(agent)
                status, headers, body = poll.result()
                self.assertEqual((status, headers["X-Rev-Method"], headers["X-Rev-Path"]), (200, "GET", "/jobs/" + "c" * 32))
                self.assertEqual(body, b"")
                # 模拟同一游戏会话的已完成采样，传输层须保持原始数组、数值单位和 ID。
                evidence = json.dumps(dict(id="c" * 32, sessionId=sid, success=True, data=dict(
                    runId="d" * 32, finished=True, fps=59.7, frames=1791, p95Ms=18.5,
                    androidBefore={"pid":17,"processPssKiB":89000,"batteryCelsius":34.5},
                    androidAfter={"pid":17,"processPssKiB":92000,"thermalStatus":1}))).encode()
                followup = executor.submit(device, "/phone/exchange", evidence, token,
                                           {"X-Rev-Command": headers["X-Rev-Command"], "X-Rev-Status": "200"})
                self.assertEqual(waiting.result(), (200, evidence))
                self.assertEqual(device("/phone/close", b"{}", token)[0], 200)
                self.assertEqual(followup.result()[0], 410)
                self.assertEqual(device("/phone/ready", json.dumps(info), token)[0], 401)

    def test_activity_pages_keep_real_mcp_context_and_ack_lifecycle(self):
        """真实 GM 的接单和终态更新同一行，分页与性能预览保留实际数据。"""
        console = Mock()
        history = operation_log.OperationLog(console)
        session = "a" * 32
        for index in range(47):
            job = f"{index:032x}"
            body = json.dumps(dict(id=job, action="gm.exec", payload={"command": f"fixture.set {index}"})).encode()
            self.assertEqual(history.submit(body, {"tool": "rev_run", "plan": "安卓校验", "phase": "steps"}), job)
            history.response("POST", "/call", job, 202, b"{}", session)
            if index == 46:
                self.assertEqual(history.page({})["events"][0]["state"], "accepted")
            result = json.dumps(dict(id=job, sessionId=session, success=True, message="完成",
                                     data={"message": f"value={index}"})).encode()
            history.response("GET", "/jobs/" + job, None, 200, result, session)
        head = history.page({})
        self.assertEqual(len(head["events"]), 40)
        self.assertEqual(head["events"][0]["tool"], "rev_run")
        self.assertEqual(head["events"][0]["arguments"], "fixture.set 46")
        self.assertEqual(head["events"][0]["state"], "success")
        self.assertEqual(head["events"][0]["message"], "value=46")
        older = history.page({"before": head["events"][-1]["seq"]})
        self.assertEqual([event["seq"] for event in older["events"]], [7, 6, 5, 4, 3, 2, 1])
        self.assertTrue(history.page({"revision": head["revision"]})["unchanged"])
        self.assertEqual(console.emit.call_count, 47)

    def test_console_prints_only_final_gm_with_declared_intent(self):
        """gm 模式保留单行终态，切换显示范围不影响已经记录的普通工单。"""
        console = Mock()
        history = operation_log.OperationLog(console, console_log="gm")
        session = "a" * 32
        gm = "b" * 32
        wire = json.dumps(dict(id=gm, action="gm.exec", payload={"command": 'RevGame/选奖励 "0"'})).encode()
        context = operation_log.mcp_context(quote('{"tool":"rev_call"}'), quote("核对领奖覆盖"))
        self.assertEqual(history.submit(wire, context), gm)
        history.response("POST", "/call", gm, 202, b"{}", session)
        history.response("GET", "/jobs/" + gm, None, 200, json.dumps(dict(id=gm, pending=True)).encode(), session)
        for number, action in enumerate(("runtime.capabilities", "story.state", "runtime.performance.state"), 1):
            other = f"{number:032x}"
            history.submit(json.dumps(dict(id=other, action=action, payload={})).encode(), context)
            result = json.dumps(dict(id=other, sessionId=session, success=True, data={"details": "机读回执"})).encode()
            history.response("GET", "/jobs/" + other, None, 200, result, session)
        history.page({})
        history.plan_finished(dict(success=False, steps=1, cleanup=2))
        console.emit.assert_not_called()
        self.assertEqual(len(history.page({})["events"]), 4)
        final = json.dumps(dict(id=gm, sessionId=session, success=True,
                                data={"success": True, "elapsedMs": 1.25, "message": "详细数据"})).encode()
        history.response("GET", "/jobs/" + gm, None, 200, final, session)
        history.response("GET", "/jobs/" + gm, None, 200, final, session)
        history.response("POST", "/jobs/" + gm + "/ack", None, 200, b"{}", session)
        console.emit.assert_called_once_with("RevGM", 'RevGame/选奖励 "0" · 意图 核对领奖覆盖 · 成功 · 1.25 ms')
        gm_row = next(row for row in history.page({})["events"] if row["action"] == "gm.exec")
        self.assertEqual((gm_row["tool"], gm_row["message"]), ("rev_call", "详细数据"))

    def test_long_command_and_missing_receipt_still_get_one_short_line(self):
        """长参数省略正文，拿不到回执要明确展示状态和等待时间。"""
        console = Mock()
        history = operation_log.OperationLog(console)
        job = "c" * 32
        command = 'RevGame/自动化开始 "' + "Z" * 80000 + '"'
        body = json.dumps(dict(id=job, action="gm.exec", payload={"command": command})).encode()
        self.assertEqual(history.submit(body, {}), job)
        history.response("POST", "/call", job, 504, b"{}", "a" * 32)
        console.emit.assert_called_once()
        self.assertRegex(console.emit.call_args.args[1], r"^RevGame/自动化开始 \[长参数略\] · 意图 未提供 · 未获回执 · 等待 \d+ ms$")

    def test_console_prints_mcp_results_once_and_preserves_plan_outcome(self):
        """显式发现、业务失败与计划失败各有一行，内部能力查询和传输轮询不输出。"""
        console = Mock()
        history = operation_log.OperationLog(console)
        session = "a" * 32
        cases = (("rev_call", "runtime.capabilities"), ("rev_discover", "runtime.capabilities"),
                 ("rev_run", "runtime.performance.state"))
        for number, (tool, action) in enumerate(cases, 1):
            job = f"{number:032x}"
            metadata = {"tool": tool}
            if tool == "rev_run":
                metadata.update(phase="finally", step="sample")
            ctx = operation_log.mcp_context(quote(json.dumps(metadata)))
            body = json.dumps(dict(id=job, action=action, payload={"token": "test-private-value"})).encode()
            history.submit(body, ctx)
            history.response("POST", "/call", job, 202, b"{}", session)
            history.response("GET", "/jobs/" + job, None, 200, json.dumps(dict(id=job, pending=True)).encode(), session)
            finished = dict(id=job, sessionId=session, success=number != 3, error="NotReady" if number == 3 else "")
            reply = json.dumps(finished).encode()
            history.response("GET", "/jobs/" + job, None, 200, reply, session)
            history.response("GET", "/jobs/" + job, None, 200, reply, session)
            history.response("POST", "/jobs/" + job + "/ack", None, 200, b"{}", session)
        history.plan_finished(dict(name="性能采样", success=False, steps=2, cleanup=1, failure="AssertionFailed"))

        rows = [call.args for call in console.emit.call_args_list]
        self.assertEqual([tag for tag, _ in rows], ["MCP", "MCP", "MCP"])
        self.assertIn("rev_discover · runtime.capabilities · 成功", rows[0][1])
        self.assertIn("rev_run 收尾 sample · runtime.performance.state · 失败", rows[1][1])
        self.assertIn("NotReady", rows[1][1])
        self.assertIn("rev_run · 性能采样 · 未通过 · 业务步骤 2，收尾步骤 1 · AssertionFailed", rows[2][1])
        self.assertNotIn("test-private-value", str(rows))

    def test_activity_never_decodes_large_game_artifact(self):
        """含截图等大字段时只取接单头部，ANSI 控制符不会跟随消息输出。"""
        raw = b'{"success":true,"id":"abc","data":{"base64":"' + b'x' * 90000 + b'"}}'
        self.assertEqual(operation_log.envelope(raw, b"data"), {"success": True, "id": "abc"})
        self.assertNotIn("\x1b", operation_log.line("cmd\x1b[31m\nline"))

    def test_timeout_never_redelivers_mutation(self):
        """丢失变更回执时只放弃等待，不能把已经领取的 POST 再发给游戏。"""
        channel = connect.Channel()
        channel.identity = {"pid": 1}
        with ThreadPoolExecutor(1) as pool:
            sent = pool.submit(channel.call, "POST", "/call", b"{}", .15)
            status, job = channel.exchange(None, 200, b"", idle=1)
            self.assertEqual(status, 200)
            self.assertEqual(sent.result()[0], 504)
            self.assertEqual(channel.exchange(job.id, 200, b"late", idle=0), (204, None))
        self.assertFalse(channel.pending)

    def test_pair_limit_and_forward_ownership(self):
        """超过配对次数后旧口令失效，USB 收尾只撤销仍属于当前电脑端口的映射。"""
        channel = connect.Channel()
        key = channel.code
        for _ in range(5):
            self.assertEqual(channel.pair(b"wrong", "a" * 32, {})[0], 403)
        self.assertEqual(channel.pair(("Bearer " + key).encode(), "a" * 32, {})[0], 410)
        adb = Mock()
        adb.run.return_value = (0, "usb tcp:18767 tcp:1234")
        connect.undo_reverse(adb, 4321)
        self.assertEqual(adb.run.call_count, 1)
        connect.undo_reverse(adb, 1234)
        adb.run.assert_called_with("reverse", "--remove", "tcp:18767")

    def test_trust_survives_listener_restart_and_can_be_revoked(self):
        """同一电脑重启后可接受已授权手机，即使短码已过期；长期凭据不能直接下工单。"""
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)
            initial = connect.HostTrust(folder / "trust")
            _, first_pin = connect.server_tls(folder, initial)
            first = connect.Channel("dev.test", trust=initial)
            payload = {"applicationId": "dev.test", "protocol": 1, "remember": True}
            status, enrolled = first.pair(("Bearer " + first.code).encode(), "a" * 32, payload)
            self.assertEqual(status, 200)
            secret = enrolled["trustToken"]
            self.assertNotIn(secret.encode(), initial.path.read_bytes())

            reopened = connect.HostTrust(folder / "trust")
            self.assertEqual(connect.server_tls(folder, reopened)[1], first_pin)
            second = connect.Channel("dev.test", trust=reopened)
            second.started -= connect.PAIR_WINDOW + 1
            self.assertEqual(second.pair(("Bearer " + second.code).encode(), "b" * 32, payload)[0], 410)
            status, resumed = second.pair(("Bearer " + secret).encode(), "b" * 32, payload, resume=True)
            self.assertEqual(status, 200)
            self.assertEqual(resumed["sessionId"], "b" * 32)
            self.assertNotEqual(resumed["token"], enrolled["token"])
            self.assertNotIn("trustToken", resumed)
            self.assertFalse(second.authorized({"Authorization": "Bearer " + secret, "X-Rev-Session": "b" * 32}))
            self.assertEqual(reopened.revoke(), 1)
            third = connect.Channel("dev.test", trust=reopened)
            self.assertEqual(third.pair(("Bearer " + secret).encode(), "c" * 32, payload, resume=True)[0], 401)

    def test_private_endpoint_does_not_clobber(self):
        """私有端点中途被替换时保留新文件，并发启动不可覆盖其它会话。"""
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "runtime.json"
            with connect.endpoint_file(path, {"session": "original"}):
                self.assertEqual(json.loads(path.read_text())["session"], "original")
                with self.assertRaises(FileExistsError), connect.endpoint_file(path, {}):
                    self.fail("并发覆盖")
                path.unlink()
                path.write_text("other")
            self.assertEqual(list(Path(directory).iterdir()), [path])
            self.assertEqual(path.read_text(), "other")


if __name__ == "__main__":
    unittest.main()
