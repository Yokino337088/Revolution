package dev.revolution.companion;

import android.content.Intent;
import android.os.SystemClock;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import org.json.JSONObject;

/** 手机作为客户端发起 HTTPS 长轮询，在选定游戏的回环接口和电脑 Agent 之间往返传递同一工单。 */
final class PhoneSession implements AutoCloseable
{
    /** 用户先确认电脑指纹，再冷启动游戏；只有 READY 阶段可执行游戏操作。 */
    enum Phase { CONNECTING, CONFIRM, PAIRING, PAIRED, STARTING, READY, CLOSED }
    // 唯一持有本轮通知和生命周期的前台服务。
    private final BridgeService service;
    // 用户明确选择的游戏包。
    final String packageId;
    // 用户选择的启动存档选项。
    final boolean isolate;
    // 用户填写的电脑地址，USB 使用 adb reverse 提供的回环地址。
    final String address;
    // 发布阶段前先准备相关数据，UI 读阶段后即可访问。
    volatile Phase phase = Phase.CONNECTING;
    // 只在游戏自己的健康检查通过后发布实际身份。
    volatile JSONObject identity;
    // 首次无凭据握手取得的电脑公钥证书摘要，供用户与电脑屏幕核对。
    volatile String fingerprint = "";
    // 用户输入的一次性配对码，配对完成或会话终止后释放引用。
    private String code;
    // 电脑在配对成功后签发给本轮手机的短期凭据。
    private volatile String bearer;
    // 只能用于本包本轮冷启动的 Unity 回环连接。
    private GameEndpoint game;
    // 唯一出站 HTTPS 目标。
    private DesktopConnection computer;
    // 仅记录本轮确实被游戏接受的退出工单编号。
    private String quitId;
    // 电脑已经领取游戏确认成功的退出工单，用来区分正常结束和进程意外失联。
    private boolean quitCompleted;

    /** 固定用户选择，网络和系统端口分配在后台完成。 */
    PhoneSession(BridgeService service, String packageId, String address, String code, boolean isolate)
    {
        this.service = service;
        this.packageId = packageId;
        this.address = address;
        this.code = code;
        this.isolate = isolate;
    }

    /** 首次连接只取电脑证书，等待手机操作者核对指纹才可发送配对码。 */
    void connect()
    {
        try
        {
            GameEndpoint endpoint = new GameEndpoint(packageId);
            DesktopConnection peer = new DesktopConnection(address);
            synchronized (this)
            {
                if (phase == Phase.CLOSED)
                {
                    peer.close();
                    return;
                }
                game = endpoint;
                computer = peer;
                fingerprint = peer.fingerprint;
                phase = Phase.CONFIRM;
            }
        }
        catch (Exception error)
        {
            end("无法连接电脑 " + address + "：" + error.getMessage());
        }
    }

    /** 经手机用户明确核对后开始认证，防止向错误的局域网电脑提交配对码。 */
    synchronized void confirm()
    {
        if (phase == Phase.CONFIRM)
        {
            phase = Phase.PAIRING;
            new Thread(this::exchange, "RevPhone-Exchange").start();
        }
    }

    /** 持续从电脑接收指令，把游戏结果作为下一次 HTTPS 请求正文送回，空闲时阻塞等待。 */
    private void exchange()
    {
        try
        {
            JSONObject ticket = computer.control("/phone/pair", code, game.id,
                new JSONObject().put("applicationId", packageId).put("protocol", 1));
            synchronized (this)
            {
                if (phase == Phase.CLOSED)
                {
                    return;
                }
                code = null;
                String token = ticket.getString("token");
                if (!token.matches("[a-f0-9]{64}") || !game.id.equals(ticket.getString("sessionId")))
                {
                    throw new IOException("电脑配对回执无效");
                }
                bearer = token;
                phase = Phase.PAIRED;
            }
            String resultId = null;
            Wire.Reply reply = new Wire.Reply(200, new byte[0]);
            int missedHealth = 0;
            while (phase != Phase.CLOSED)
            {
                DesktopConnection.Message next = computer.post("/phone/exchange", bearer, game.id, resultId, reply);
                resultId = null;
                reply = new Wire.Reply(200, new byte[0]);
                if (next.status == 204)
                {
                    // 只有电脑连续空闲一个长轮询窗时才探测游戏退出，不增加每帧或每工单的采样负担。
                    if (phase == Phase.READY)
                    {
                        try
                        {
                            game.health(identity);
                            missedHealth = 0;
                        }
                        catch (Exception missing)
                        {
                            if (++missedHealth >= 2)
                            {
                                if (quitCompleted)
                                {
                                    finish("测试游戏已正常退出", false);
                                }
                                else
                                {
                                    end("所选测试游戏已退出或无法访问");
                                }
                                return;
                            }
                        }
                    }
                    continue;
                }
                if (next.status != 200 || next.id == null || !next.id.matches("[a-f0-9]{32}"))
                {
                    throw new IOException("电脑会话已结束或指令无效（HTTP " + next.status + "）");
                }
                resultId = next.id;
                reply = execute(next);
            }
        }
        catch (Exception error)
        {
            end("与电脑的连接已结束：" + error.getMessage());
        }
    }

    /** 同一条已领取的命令仅执行一次，断线后的不确定结果不自动重试。 */
    private Wire.Reply execute(DesktopConnection.Message next)
    {
        if (phase != Phase.READY || next.method == null || next.path == null ||
            !GameEndpoint.allows(next.method, next.path) || (!next.method.equals("POST") && next.body.length != 0))
        {
            return Wire.Reply.error(409, "当前阶段或游戏路径不可用");
        }
        try
        {
            if (next.path.equals("/health"))
            {
                return new Wire.Reply(200, game.health(identity).toString().getBytes(StandardCharsets.UTF_8));
            }
            Wire.Reply reply = game.request(next.method, next.path, next.body);
            if (next.path.equals("/call") && reply.status == 202)
            {
                JSONObject call = new JSONObject(new String(next.body, StandardCharsets.UTF_8));
                if ("runtime.quit".equals(call.optString("action")))
                {
                    quitId = call.optString("id");
                }
            }
            else if (quitId != null && next.method.equals("GET") && next.path.equals("/jobs/" + quitId) && reply.status == 200)
            {
                JSONObject result = new JSONObject(new String(reply.body, StandardCharsets.UTF_8));
                quitCompleted = !result.optBoolean("pending") && result.optBoolean("success") &&
                    quitId.equals(result.optString("id")) && game.id.equals(result.optString("sessionId"));
            }
            return reply;
        }
        catch (Exception unavailable)
        {
            return Wire.Reply.error(502, "所选游戏未返回有效回执，已提交的动作不会重发");
        }
    }

    /** 从可见 Activity 启动选定游戏，前台服务留在后台转交请求。 */
    synchronized void launch(MainActivity activity)
    {
        if (phase != Phase.PAIRED)
        {
            return;
        }
        Intent intent = activity.getPackageManager().getLaunchIntentForPackage(packageId);
        if (intent == null || intent.getComponent() == null || !packageId.equals(intent.getComponent().getPackageName()))
        {
            end("游戏已卸载或启动入口不可用");
            return;
        }
        game.configure(intent, isolate);
        phase = Phase.STARTING;
        try
        {
            activity.startActivity(intent);
            new Thread(this::verify, "RevGame-Health").start();
        }
        catch (RuntimeException failed)
        {
            end("系统拒绝启动游戏：" + failed.getMessage());
        }
    }

    /** 先验证游戏，再向电脑登记真实包名、启动号、PID 和 Build ID。 */
    private void verify()
    {
        long deadline = SystemClock.elapsedRealtime() + 90000;
        while (phase == Phase.STARTING && SystemClock.elapsedRealtime() < deadline)
        {
            JSONObject info;
            try
            {
                info = game.health(null);
            }
            catch (Exception starting)
            {
                SystemClock.sleep(300);
                continue;
            }
            synchronized (this)
            {
                if (phase != Phase.STARTING)
                {
                    return;
                }
                // 在电脑看到 ready 前即可处理被立即唤醒的长轮询。
                identity = info;
                phase = Phase.READY;
            }
            try
            {
                computer.control("/phone/ready", bearer, game.id, info);
                service.notifyReady(this);
            }
            catch (Exception rejected)
            {
                end("电脑未接纳游戏身份：" + rejected.getMessage());
            }
            return;
        }
        if (phase == Phase.STARTING)
        {
            end("90 秒内未验证游戏。请退出旧进程，确认测试包支持 Revolution 自动化后重试");
        }
    }

    /** 只公布本次退出原因，已关闭的线程不得覆盖下一会话。 */
    private void end(String message)
    {
        finish(message, true);
    }

    /** 归还连接并公布终态，实际收到退出结果的测试可以正常结束。 */
    private synchronized void finish(String message, boolean failed)
    {
        if (phase != Phase.CLOSED)
        {
            disconnect(failed);
            service.finished(this, message);
        }
    }

    /** 停止派发，通知电脑撤销端点，再关闭长轮询；网络操作不占用 Android 主线程。 */
    @Override public synchronized void close()
    {
        disconnect(false);
    }

    /** 结束原因归入电脑回执，在 UI 主线程只撤销本地派发许可。 */
    private synchronized void disconnect(boolean failed)
    {
        if (phase == Phase.CLOSED)
        {
            return;
        }
        phase = Phase.CLOSED;
        code = null;
        DesktopConnection peer = computer;
        String token = bearer;
        bearer = null;
        if (peer != null)
        {
            new Thread(() -> {
                try
                {
                    if (token != null)
                    {
                        peer.control("/phone/close", token, game.id, new JSONObject().put("failed", failed));
                    }
                }
                catch (Exception disconnected)
                {
                    // 断网时电脑会按心跳租约撤销私有端点。
                }
                finally
                {
                    peer.close();
                }
            }, "RevPhone-Close").start();
        }
    }
}
