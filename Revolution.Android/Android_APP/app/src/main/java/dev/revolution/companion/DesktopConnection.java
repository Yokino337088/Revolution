package dev.revolution.companion;

import android.annotation.SuppressLint;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.net.Proxy;
import java.net.Socket;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.KeyStore;
import java.security.cert.CertificateException;
import java.security.cert.X509Certificate;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;
import javax.net.ssl.HttpsURLConnection;
import javax.net.ssl.SSLContext;
import javax.net.ssl.SSLHandshakeException;
import javax.net.ssl.SSLSocket;
import javax.net.ssl.TrustManager;
import javax.net.ssl.TrustManagerFactory;
import javax.net.ssl.X509TrustManager;
import org.json.JSONObject;

/** 手机主动连接用户填写的电脑；首次握手只读证书，确认指纹后才发送配对码和数据。 */
final class DesktopConnection implements AutoCloseable
{
    // 电脑的固定 HTTPS 源，URL 只由用户选择的 IPv4 和端口组成。
    private final URL origin;
    // 电脑自签证书的完整 SHA-256，每次传输都重新由系统 TLS 栈核对。
    final String fingerprint;
    // 已固定证书的客户端 TLS 配置，整个会话复用连接池。
    private final SSLContext pinned;
    // 关闭会话时唤醒还在等下一条指令的 HTTP 长轮询。
    private final Set<HttpsURLConnection> active = ConcurrentHashMap.newKeySet();
    // 用户断开后禁止新网络请求。
    private volatile boolean closed;

    /** 首次握手只留存叶证书并主动拒绝；所有携带数据的请求另用 Android 的正式证书验证器。 */
    @SuppressLint("CustomX509TrustManager")
    private static SSLContext probe(X509Certificate[] observed) throws Exception
    {
        X509TrustManager trust = new X509TrustManager()
        {
            /** 此 TLS 配置只用于客户端，不接受客户端证书。 */
            @Override public void checkClientTrusted(X509Certificate[] chain, String auth) throws CertificateException
            {
                throw new CertificateException("仅用于连接电脑");
            }

            /** 取证书后终止首次 TLS 握手，此连接永远不能发送应用数据。 */
            @Override public void checkServerTrusted(X509Certificate[] chain, String auth) throws CertificateException
            {
                if (chain == null || chain.length == 0)
                {
                    throw new CertificateException("电脑未提供证书");
                }
                chain[0].checkValidity();
                observed[0] = chain[0];
                throw new CertificateException("已读取待用户核对的电脑证书");
            }

            /** 电脑证书由现场配对确认，没有预设 CA。 */
            @Override public X509Certificate[] getAcceptedIssuers()
            {
                return new X509Certificate[0];
            }
        };
        SSLContext tls = SSLContext.getInstance("TLS");
        tls.init(null, new TrustManager[]{trust}, null);
        return tls;
    }

    /** 返回证书 DER 编码的 SHA-256，指纹无法计算时终止握手。 */
    private static String hash(X509Certificate cert) throws CertificateException
    {
        try
        {
            return GameEndpoint.hex(MessageDigest.getInstance("SHA-256").digest(cert.getEncoded()));
        }
        catch (Exception failure)
        {
            throw new CertificateException(failure);
        }
    }

    /** 在后台探测电脑证书，不相信网络提供的主机名、重定向或代理。 */
    DesktopConnection(String address) throws Exception
    {
        if (!address.matches("(?:[0-9]{1,3}\\.){3}[0-9]{1,3}(?::[0-9]{1,5})?"))
        {
            throw new IOException("请输入电脑 IPv4 地址，可附端口");
        }
        String[] parts = address.split(":");
        for (String part : parts[0].split("\\."))
        {
            if (Integer.parseInt(part) > 255)
            {
                throw new IOException("电脑 IPv4 地址无效");
            }
        }
        int port = parts.length == 1 ? 18767 : Integer.parseInt(parts[1]);
        if (port <= 0 || port > 65535)
        {
            throw new IOException("电脑端口无效");
        }
        origin = new URL("https", parts[0], port, "/");
        X509Certificate[] observed = new X509Certificate[1];
        try (Socket tcp = new Socket(Proxy.NO_PROXY))
        {
            tcp.connect(new InetSocketAddress(origin.getHost(), port), 5000);
            try (SSLSocket socket = (SSLSocket)probe(observed).getSocketFactory()
                .createSocket(tcp, origin.getHost(), port, true))
            {
                socket.setSoTimeout(5000);
                socket.startHandshake();
            }
            catch (SSLHandshakeException expected)
            {
                if (observed[0] == null)
                {
                    throw expected;
                }
            }
        }
        if (observed[0] == null)
        {
            throw new IOException("未收到电脑证书");
        }
        fingerprint = hash(observed[0]);
        // 只把本轮候选证书装入内存信任库；UI 确认指纹后才会发出配对请求。
        KeyStore trusted = KeyStore.getInstance(KeyStore.getDefaultType());
        trusted.load(null, null);
        trusted.setCertificateEntry("desktop", observed[0]);
        TrustManagerFactory managers = TrustManagerFactory.getInstance(TrustManagerFactory.getDefaultAlgorithm());
        managers.init(trusted);
        pinned = SSLContext.getInstance("TLS");
        pinned.init(null, managers.getTrustManagers(), null);
    }

    /** 电脑长轮询响应中的指令头与原始正文，空闲响应没有工单号。 */
    static final class Message
    {
        // 电脑传输层的命令编号，和 Unity 内部业务工单独立。
        String id;
        // 转交给游戏的 HTTP 动词。
        String method;
        // 转交给游戏的固定运行时路径。
        String path;
        // 电脑的 HTTP 回执码，204 表示这一轮没有工作。
        int status;
        // JSON 请求体或控制响应。
        byte[] body;
    }

    /** 通过固定证书 POST 结果并等待下一条指令；不重试状态不明的业务操作。 */
    Message post(String path, String bearer, String session, String resultId, Wire.Reply value) throws Exception
    {
        HttpsURLConnection conn = (HttpsURLConnection)new URL(origin, path).openConnection(Proxy.NO_PROXY);
        boolean drained = false;
        active.add(conn);
        try
        {
            if (closed)
            {
                throw new IOException("连接已关闭");
            }
            conn.setSSLSocketFactory(pinned.getSocketFactory());
            // 本轮信任固定证书而非 DNS。校验同一叶证书也覆盖恢复会话和连接复用。
            conn.setHostnameVerifier((host, sslSession) -> {
                try
                {
                    return host.equals(origin.getHost()) && GameEndpoint.equal(fingerprint,
                        hash((X509Certificate)sslSession.getPeerCertificates()[0]));
                }
                catch (Exception invalid)
                {
                    return false;
                }
            });
            conn.setInstanceFollowRedirects(false);
            conn.setConnectTimeout(5000);
            conn.setReadTimeout(18000);
            conn.setRequestMethod("POST");
            conn.setRequestProperty("Content-Type", "application/json");
            conn.setRequestProperty("Accept-Encoding", "identity");
            conn.setRequestProperty("Authorization", "Bearer " + bearer);
            conn.setRequestProperty("X-Rev-Session", session);
            if (resultId != null)
            {
                conn.setRequestProperty("X-Rev-Command", resultId);
                conn.setRequestProperty("X-Rev-Status", Integer.toString(value.status));
            }
            conn.setDoOutput(true);
            conn.setFixedLengthStreamingMode(value.body.length);
            try (OutputStream out = conn.getOutputStream())
            {
                out.write(value.body);
            }
            Message message = new Message();
            message.status = conn.getResponseCode();
            message.id = conn.getHeaderField("X-Rev-Command");
            message.method = conn.getHeaderField("X-Rev-Method");
            message.path = conn.getHeaderField("X-Rev-Path");
            try (InputStream in = message.status < 400 ? conn.getInputStream() : conn.getErrorStream())
            {
                message.body = in == null ? new byte[0] : Wire.read(in, Wire.INPUT_LIMIT);
            }
            drained = true;
            return message;
        }
        finally
        {
            active.remove(conn);
            // 读完的定长响应已交回系统连接池，半途失败的连接必须关闭。
            if (!drained)
            {
                conn.disconnect();
            }
        }
    }

    /** 给配对、就绪和断开控制请求编码 JSON，并校验电脑确认的状态。 */
    JSONObject control(String path, String bearer, String session, JSONObject data) throws Exception
    {
        Message response = post(path, bearer, session, null, new Wire.Reply(200, data.toString().getBytes(StandardCharsets.UTF_8)));
        if (response.status != 200)
        {
            throw new IOException("电脑拒绝请求（HTTP " + response.status + "）");
        }
        return new JSONObject(new String(response.body, StandardCharsets.UTF_8));
    }

    /** 断开本会话所有 HTTP 长轮询及并行的控制请求。 */
    @Override public void close()
    {
        closed = true;
        for (HttpsURLConnection connection : active)
        {
            connection.disconnect();
        }
    }
}
