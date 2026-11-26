package dev.revolution.companion;

import android.content.Intent;
import android.os.SystemClock;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import org.json.JSONObject;

/** 手机作为客户端发起 HTTPS 长轮询，在选定游戏的回环接口和电脑 Agent 之间往返传递同一工单。 */
final class PhoneSession implements AutoCloseable
{
    /** 复用已保存的电脑授权或完成首次确认后冷启动游戏，READY 才能执行游戏操作。 */
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
    // 提示授权来自哪里，或为什么要让用户重新确认，并随状态一起发布。
    volatile String authorizationMessage = "";
    // 电脑在配对成功后签发给本轮手机的短期凭据。
    private volatile String bearer;
    // 只能用于本包本轮冷启动的 Unity 回环连接。
    private GameEndpoint game;
    // 悬浮窗发起的单轮采样，取消时只使用本窗口持有的 runId。
    final PhonePerformance performance;
    // 唯一出站 HTTPS 目标。
    private DesktopConnection computer;
    // 仅记录本轮确实被游戏接受的退出工单编号。
    private String quitId;
    // 电脑已经领取游戏确认成功的退出工单，用来区分正常结束和进程意外失联。
    private boolean quitCompleted;

    /** 固定用户选择，证书探测、授权和系统端口分配在后台完成。 */
    PhoneSession(BridgeService service, String packageId, String address, boolean isolate)
    {
        this.service = service;
        this.packageId = packageId;
        this.address = address;
        this.isolate = isolate;
        performance = new PhonePerformance(() -> service.performanceChanged(this));
    }

    /** 先取电脑证书，完整指纹匹配才发送长期凭据，首次连接等待用户核对。 */
    void connect()
    {
        try
        {
            GameEndpoint endpoint = new GameEndpoint(packageId);
            DesktopConnection peer = new DesktopConnection(address);
            String saved = TrustedComputers.read(service, peer.fingerprint);
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
                phase = saved == null ? Phase.CONFIRM : Phase.PAIRING;
            }
            if (saved != null)
            {
                authenticate(saved, true);
            }
        }
        catch (Exception error)
        {
            end("无法连接电脑 " + address + "：" + error.getMessage());
        }
    }

    /** 操作者核对后提交八位短码，仅在首次授权阶段发送并请求保存凭据。 */
    synchronized void confirm(String code)
    {
        if (phase == Phase.CONFIRM)
        {
            phase = Phase.PAIRING;
            new Thread(() -> authenticate(code, false), "RevPhone-Pair").start();
        }
    }

    /** 认证成功后换领短期会话令牌，再开始转交请求；被撤销时回到可见的授权步骤。 */
    private void authenticate(String credential, boolean resumed)
    {
        try
        {
            JSONObject ticket = computer.control(resumed ? "/phone/resume" : "/phone/pair", credential, game.id,
                new JSONObject().put("applicationId", packageId).put("protocol", 1).put("remember", !resumed));
            synchronized (this)
            {
                if (phase == Phase.CLOSED)
                {
                    return;
                }
                String token = ticket.getString("token");
                if (!token.matches("[a-f0-9]{64}") || !game.id.equals(ticket.getString("sessionId")))
                {
                    throw new IOException("电脑配对回执无效");
                }
                bearer = token;
                String durable = ticket.optString("trustToken");
                if (resumed)
                {
                    authorizationMessage = "已使用保存的电脑授权。";
                }
                else if (durable.matches("[a-f0-9]{64}") && TrustedComputers.remember(service, fingerprint, durable))
                {
                    authorizationMessage = "已授权这台电脑，下次可直接连接。";
                }
                else
                {
                    authorizationMessage = "本次连接成功，未能保存下次连接的授权。";
                }
                phase = Phase.PAIRED;
            }
        }
        catch (DesktopConnection.Rejected rejected)
        {
            if (resumed && (rejected.status == 401 || rejected.status == 404))
            {
                awaitConfirmation("保存的授权已失效，请重新核对指纹并输入配对码。", true);
            }
            else if (!resumed && (rejected.status == 403 || rejected.status == 410))
            {
                awaitConfirmation(rejected.status == 403 ? "配对码不正确，请核对电脑窗口。" :
                    "此配对码已失效，请使用电脑窗口最新显示的配对码。", false);
            }
            else
            {
                end(rejected.status == 409 ? "电脑已有测试连接，或所选游戏与电脑指定的包名不同。" : rejected.getMessage());
            }
            return;
        }
        catch (Exception error)
        {
            end("电脑授权失败：" + error.getMessage());
            return;
        }
        exchange();
    }

    /** 拒绝的授权不留会话令牌，用户可在当前页面再次输入。 */
    private synchronized void awaitConfirmation(String message, boolean revoked)
    {
        if (phase == Phase.PAIRING)
        {
            if (revoked)
            {
                TrustedComputers.forget(service, fingerprint);
            }
            authorizationMessage = message;
            phase = Phase.CONFIRM;
        }
    }

    /** 持续从电脑接收指令，把游戏结果作为下一次 HTTPS 请求正文送回，空闲时阻塞等待。 */
    private void exchange()
    {
        try
        {
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

    /** 原样转交游戏工单。仅为正常退出回执保留必要的身份字段。 */
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
            if (next.path.equals("/call") && reply.status == 202 && next.body.length <= 4096)
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

    /** 只有打开记录列表的后台线程持有租约，才可从已配对电脑读取一个页面。 */
    OperationPage readOperations(long before, long revision, DesktopConnection.RequestLease lease) throws Exception
    {
        String token = bearer;
        if (phase != Phase.READY || token == null)
        {
            throw new IOException("游戏测试连接尚未就绪");
        }
        JSONObject page = computer.control("/phone/activity", token, game.id,
            new JSONObject().put("before", before).put("revision", revision), lease);
        return new OperationPage(page);
    }

    /** 只有验证过身份的前台游戏能从浮窗开始本地采样。 */
    synchronized void startPerformance()
    {
        if (phase == Phase.READY)
        {
            performance.start(game);
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
            service.gameStarted(this);
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
        performance.cancel();
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
