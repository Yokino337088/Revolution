package dev.revolution.companion;

import android.content.Intent;
import android.os.SystemClock;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.InetAddress;
import java.net.Proxy;
import java.net.ServerSocket;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.SecureRandom;
import org.json.JSONException;
import org.json.JSONObject;

/** 单次启动的 Unity 回环端点，电脑凭据与游戏凭据分开，只访问选定游戏的固定端口。 */
final class GameEndpoint
{
    // 加密随机源为每次启动生成独立凭据。
    static final SecureRandom RANDOM = new SecureRandom();
    // 用户从手机选定的包名，贯穿整个会话。
    final String packageId;
    // 送入 Android Intent 的启动标识，同时也是运行时会话号。
    final String id = randomHex(16);
    // 仅由手机持有，随所选包的显式 Intent 交给游戏。
    private final String token = randomHex(32);
    // 监听端口由系统挑选，探测必须核对身份以处理关闭预留后的端口争用。
    private final int port;

    /** 先向系统申请可用的回环端口，在游戏冷启动之前归还预留套接字。 */
    GameEndpoint(String packageId) throws IOException
    {
        this.packageId = packageId;
        try (ServerSocket reservation = new ServerSocket(0, 1, InetAddress.getByName("127.0.0.1")))
        {
            port = reservation.getLocalPort();
        }
    }

    /** 生成框架 Android Intent 接口要求的十六进制随机串。 */
    static String randomHex(int size)
    {
        byte[] bytes = new byte[size];
        RANDOM.nextBytes(bytes);
        return hex(bytes);
    }

    /** 统一令牌和 SHA-256 指纹的小写文本格式。 */
    static String hex(byte[] bytes)
    {
        StringBuilder result = new StringBuilder(bytes.length * 2);
        for (byte value : bytes)
        {
            result.append(Character.forDigit((value & 255) >>> 4, 16));
            result.append(Character.forDigit(value & 15, 16));
        }
        return result.toString();
    }

    /** 鉴权值只在内存中比较，空头直接拒绝。 */
    static boolean equal(String expected, String actual)
    {
        return actual != null && MessageDigest.isEqual(expected.getBytes(StandardCharsets.UTF_8),
            actual.getBytes(StandardCharsets.UTF_8));
    }

    /** 复用框架原有的 Android 启动参数，隔离选项交给业务项目解释。 */
    void configure(Intent intent, boolean isolate)
    {
        intent.putExtra("revAutomation", true);
        intent.putExtra("revAutomationPort", port);
        intent.putExtra("revAutomationToken", token);
        intent.putExtra("revAutomationLaunchId", id);
        if (isolate)
        {
            intent.putExtra("unity", "-revTestSession");
        }
    }

    /** 只暴露运行时实际使用的健康、提交、轮询、确认和取消路径。 */
    static boolean allows(String method, String path)
    {
        return (method.equals("GET") && path.equals("/health")) ||
            (method.equals("POST") && path.equals("/call")) ||
            ((method.equals("GET") || method.equals("DELETE")) && path.matches("/jobs/[a-f0-9]{32}")) ||
            (method.equals("POST") && path.matches("/jobs/[a-f0-9]{32}/ack"));
    }

    /** 发往固定回环端口，关闭重定向；定长输出阻止底层自动重新提交 POST。 */
    Wire.Reply request(String method, String path, byte[] body) throws IOException
    {
        HttpURLConnection connection = (HttpURLConnection)new URL("http://127.0.0.1:" + port + path)
            .openConnection(Proxy.NO_PROXY);
        try
        {
            connection.setInstanceFollowRedirects(false);
            connection.setConnectTimeout(1500);
            connection.setReadTimeout(4500);
            connection.setRequestMethod(method);
            connection.setRequestProperty("X-Rev-Session", id);
            connection.setRequestProperty("Authorization", "Bearer " + token);
            connection.setRequestProperty("Accept-Encoding", "identity");
            if (method.equals("POST"))
            {
                connection.setDoOutput(true);
                connection.setRequestProperty("Content-Type", "application/json");
                connection.setFixedLengthStreamingMode(body.length);
                try (OutputStream out = connection.getOutputStream())
                {
                    out.write(body);
                }
            }
            int status = connection.getResponseCode();
            try (InputStream in = status >= 400 ? connection.getErrorStream() : connection.getInputStream())
            {
                return new Wire.Reply(status, in == null ? new byte[0] : Wire.read(in, Wire.OUTPUT_LIMIT));
            }
        }
        finally
        {
            connection.disconnect();
        }
    }

    /** 由悬浮窗提交一次本机性能动作，等待并确认同一工单。只允许三个固定入口。 */
    JSONObject performance(String action, JSONObject arguments) throws Exception
    {
        if (!action.equals("begin") && !action.equals("state") && !action.equals("cancel"))
        {
            throw new IllegalArgumentException("未知的性能采样动作");
        }
        String jobId = randomHex(16);
        String path = "/jobs/" + jobId;
        byte[] requestBody = new JSONObject().put("id", jobId).put("action", "runtime.performance." + action)
            .put("payload", arguments).put("confirm", false).put("timeoutSeconds", 8)
            .toString().getBytes(StandardCharsets.UTF_8);
        boolean consumed = false;
        try
        {
            // 接单和结果分开，丢回执时取消原工单；开始采样的 POST 永远只执行一次。
            Wire.Reply accepted = request("POST", "/call", requestBody);
            JSONObject intake = new JSONObject(new String(accepted.body, StandardCharsets.UTF_8));
            if (accepted.status != 202 || !jobId.equals(intake.optString("id")) || !id.equals(intake.optString("sessionId")))
            {
                throw new IOException(intake.optString("message", "游戏未接受性能工单（HTTP " + accepted.status + "）"));
            }
            long deadline = SystemClock.elapsedRealtime() + 9000;
            while (SystemClock.elapsedRealtime() < deadline)
            {
                Wire.Reply response = request("GET", path, new byte[0]);
                JSONObject result = new JSONObject(new String(response.body, StandardCharsets.UTF_8));
                if (response.status != 200 || !jobId.equals(result.optString("id")))
                {
                    throw new IOException(result.optString("message", "性能工单已失效（HTTP " + response.status + "）"));
                }
                if (result.optBoolean("pending"))
                {
                    SystemClock.sleep(80);
                    continue;
                }
                if (!id.equals(result.optString("sessionId")))
                {
                    throw new IOException("性能结果属于另一游戏会话");
                }
                consumed = true;
                try
                {
                    request("POST", path + "/ack", "{}".getBytes(StandardCharsets.UTF_8));
                }
                catch (IOException disconnected)
                {
                    // 终态已经收到，运行时的有界回收负责释放无法确认的旧回执。
                }
                if (!result.optBoolean("success"))
                {
                    throw new IOException(result.optString("message", "游戏拒绝性能动作"));
                }
                return result.getJSONObject("data");
            }
            throw new IOException("等待游戏返回性能工单超时");
        }
        finally
        {
            if (!consumed)
            {
                try
                {
                    request("DELETE", path, new byte[0]);
                }
                catch (IOException disconnected)
                {
                    // 游戏失联时由工单自身期限回收。
                }
            }
        }
    }

    /** 认领本轮生成的启动号和包身份，后续探测必须仍是同一进程及构建。 */
    JSONObject health(JSONObject previous) throws IOException, JSONException
    {
        Wire.Reply reply = request("GET", "/health", new byte[0]);
        if (reply.status != 200)
        {
            throw new IOException("游戏拒绝健康查询（HTTP " + reply.status + "）");
        }
        JSONObject info = new JSONObject(new String(reply.body, StandardCharsets.UTF_8));
        if (info.optInt("protocol") != 1 || !"Android".equals(info.optString("platform")) ||
            !packageId.equals(info.optString("applicationId")) || !id.equals(info.optString("sessionId")) ||
            !id.equals(info.optString("launchId")) || !info.optBoolean("memoryJobs") ||
            info.optInt("pid") <= 0 || !info.optString("buildId").matches("(?i)[0-9a-f]{32}") ||
            (previous != null && (previous.optInt("pid") != info.optInt("pid") ||
                !previous.optString("buildId").equals(info.optString("buildId")))))
        {
            throw new IOException("游戏包、进程、构建或会话身份与本次启动不符");
        }
        return info;
    }
}
