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

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import connect


class CompanionTests(unittest.TestCase):
    """使用真实回环 HTTPS 代表一台主动连入的手机，核对本机 Agent 获取的最终数据。"""

    def test_phone_initiates_https_and_returns_exact_result(self):
        """电脑先监听，配对后接受手机长轮询，原封不动交回结果，结束后拒绝旧凭据。"""
        channel = connect.Channel("dev.test")
        sid = "a" * 32
        info = dict(protocol=1, sessionId=sid, launchId=sid, memoryJobs=True, applicationId="dev.test",
                    platform="Android", pid=17, buildId="b" * 32)
        with tempfile.TemporaryDirectory() as directory, connect.ExitStack() as stack:
            tls, pin = connect.server_tls(Path(directory), "127.0.0.1")
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
                self.assertEqual(device("/phone/pair", payload, "incorrect")[0], 403)
                code = channel.code
                result = device("/phone/pair", payload, code)
                self.assertEqual(result[0], 200)
                token = json.loads(result[2])["token"]
                self.assertEqual(device("/phone/pair", payload, code)[0], 409)
                self.assertEqual(device("/phone/ready", json.dumps(dict(info, launchId="old")), token)[0], 400)
                self.assertEqual(device("/phone/ready", json.dumps(info), token)[0], 200)
                poll = executor.submit(device, "/phone/exchange", b"", token)

                def agent():
                    """本机一侧走原版协议提交单个请求，而后等待手机回传。"""
                    client = HTTPConnection("127.0.0.1", local, timeout=8)
                    try:
                        client.request("POST", "/call", b'{"action":"runtime.performance.state"}',
                                       {"Authorization": "Bearer " + channel.local_token, "X-Rev-Session": sid})
                        response = client.getresponse()
                        return response.status, response.read()
                    finally:
                        client.close()

                waiting = executor.submit(agent)
                status, headers, body = poll.result()
                self.assertEqual((status, headers["X-Rev-Method"], headers["X-Rev-Path"]), (200, "POST", "/call"))
                self.assertEqual(json.loads(body)["action"], "runtime.performance.state")
                evidence = b'{"fps":59.7,"frames":1791}'
                followup = executor.submit(device, "/phone/exchange", evidence, token,
                                           {"X-Rev-Command": headers["X-Rev-Command"], "X-Rev-Status": "202"})
                self.assertEqual(waiting.result(), (202, evidence))
                self.assertEqual(device("/phone/close", b"{}", token)[0], 200)
                self.assertEqual(followup.result()[0], 410)
                self.assertEqual(device("/phone/ready", json.dumps(info), token)[0], 401)

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
        self.assertEqual(channel.pair(("Bearer " + key).encode(), "a" * 32, {})[0], 409)
        adb = Mock()
        adb.run.return_value = (0, "usb tcp:18767 tcp:1234")
        connect.undo_reverse(adb, 4321)
        self.assertEqual(adb.run.call_count, 1)
        connect.undo_reverse(adb, 1234)
        adb.run.assert_called_with("reverse", "--remove", "tcp:18767")

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
