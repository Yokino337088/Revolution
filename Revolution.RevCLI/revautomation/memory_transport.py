"""Editor 本机 HTTP 内存请求：一次 POST，重载只重新连接 GET，绝不重发业务变更。"""
import json
from pathlib import Path
import time
import urllib.error
import urllib.parse
import urllib.request

from .errors import RevError, RevCancelled


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        raise RevError("Editor HTTP 不接受重定向")


def credentials(client):
    from .client import read_json_settled
    info = read_json_settled(client.root / "editor-http.json", attempts=3)
    url = urllib.parse.urlsplit(info.get("url", ""))
    if info.get("protocol") != 1 or Path(info.get("project", "")).resolve() != client.project or not info.get("token") \
            or url.scheme != "http" or url.hostname != "127.0.0.1" or url.path not in ("", "/") or url.query or url.fragment or url.username:
        raise RevError("Editor HTTP 凭据身份或本机地址无效")
    return info


def request(info, method, path, data=None):
    raw = json.dumps(data, ensure_ascii=False, allow_nan=False).encode("utf-8") if data is not None else None
    call = urllib.request.Request(info["url"] + path, raw, method=method, headers={
        "Authorization": "Bearer " + info["token"], "X-Rev-Session": info["sessionId"], "Content-Type": "application/json"})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())
    try:
        with opener.open(call, timeout=5) as response:
            body = response.read(24 * 1024 * 1024 + 1)
        if len(body) > 24 * 1024 * 1024:
            raise RevError("Editor HTTP 响应超过 24 MiB")
        return json.loads(body)
    except urllib.error.HTTPError as exc:
        raise RevError("Editor HTTP %s: %s" % (exc.code, exc.read(2048).decode("utf-8", errors="replace"))) from None


def call(client, payload, cancel=None):
    ident = payload["id"]
    info = credentials(client)
    if cancel is not None and cancel.is_set():
        raise RevCancelled("请求提交前已取消")
    # POST 出错时业务状态可能未知；不转文件桥、不再次 POST，以免动作执行两遍。
    try:
        request(info, "POST", "/call", payload)
    except OSError as exc:
        raise RevError(f"内存请求提交结果未知：{ident}；未切换通道或重发，请先检查日志。{exc}") from None
    deadline = time.monotonic() + client.timeout
    try:
        while True:
            if cancel is not None and cancel.is_set():
                raise RevCancelled("请求取消：" + ident)
            if time.monotonic() >= deadline:
                raise TimeoutError("内存工单超时：" + ident + "；未自动重放")
            try:
                current = credentials(client)
                if current["sessionId"] != info["sessionId"]:
                    raise RevError("Editor 已重新启动，原内存结果不可恢复；未自动重放：" + ident)
                info = current
                response = request(info, "GET", "/jobs/" + ident)
                if not response.get("pending"):
                    if response.get("id") != ident:
                        raise RevError("Editor HTTP 响应身份不匹配")
                    if response.get("error") == "ResultExpired":
                        raise RevError(response.get("message"))
                    if response.get("protocol") != 1:
                        raise RevError("Editor HTTP 响应协议不匹配")
                    if response.get("dataJson"):
                        response["data"] = json.loads(response.pop("dataJson"))
                    response["transport"] = "http-memory"
                    response["jobDirectory"] = str(client.root / "jobs" / ident) if (client.root / "jobs" / ident).is_dir() else None
                    try:
                        request(info, "POST", "/jobs/" + ident + "/ack", {})
                    except (OSError, RevError):
                        pass  # 已收到结果；确认失败由服务保留上限回收，不改变业务成功结果。
                    return response
            except OSError:
                # Unity 重载短暂关闭 listener：只等待同一个请求的 GET，不能重新提交。
                pass
            time.sleep(.02)
    except (RevCancelled, TimeoutError, KeyboardInterrupt):
        try:
            request(info, "DELETE", "/jobs/" + ident)
        except (OSError, RevError):
            pass
        raise
