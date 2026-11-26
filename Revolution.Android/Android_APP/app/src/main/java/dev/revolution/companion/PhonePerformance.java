package dev.revolution.companion;

import android.os.SystemClock;
import java.io.IOException;
import org.json.JSONObject;

/** 管理悬浮窗发起的一轮采样，和电脑 Agent 复用游戏内的同一采样器及 runId。 */
final class PhonePerformance
{
    /** UI 只读最近一轮摘要；游戏仍向电脑提供同一轮的完整 JSON。 */
    static final class Display
    {
        /** 有采样正在接单、进行或收尾。 */
        final boolean active;
        /** 已收到游戏确认，界面可展示预热与采样阶段。 */
        final boolean accepted;
        /** 用户已请求取消，按钮需等待本轮收尾。 */
        final boolean cancelling;
        /** 系统或游戏拒绝时可供用户看到的原因。 */
        final String error;
        /** 最近一次收到的已验证终态，只有本会话存续时保留。 */
        final JSONObject report;

        /** 一次提交替换整份状态，避免 UI 把上一轮结果画到新一轮上。 */
        Display(boolean active, boolean accepted, boolean cancelling, String error, JSONObject report)
        {
            this.active = active;
            this.accepted = accepted;
            this.cancelling = cancelling;
            this.error = error;
            this.report = report;
        }
    }

    /** 手机默认采集时长。其它时长可由电脑 Agent 提交。 */
    private static final int SECONDS = 30;
    /** 收起悬浮窗及稳定游戏的预热时间。 */
    private static final int WARMUP = 3;
    /** 服务将状态变化转回主线程，隐藏窗口无需轮询。 */
    private final Runnable changed;
    /** 后台线程发布与主线程读取共享同一份不可变摘要。 */
    private volatile Display display = new Display(false, false, false, null, null);

    /** 生命周期与拥有它的 PhoneSession 一致。 */
    PhonePerformance(Runnable changed)
    {
        this.changed = changed;
    }

    /** 主线程需要刷新控件时读取当前状态。 */
    Display display()
    {
        return display;
    }

    /** 只允许本窗口同时有一轮，事先选定 ID 用于失败或断线后的定向清理。 */
    synchronized void start(GameEndpoint game)
    {
        if (display.active)
        {
            return;
        }
        publish(new Display(true, false, false, null, null));
        String runId = GameEndpoint.randomHex(16);
        new Thread(() -> collect(game, runId), "RevPhone-Performance").start();
    }

    /** 请求停止本窗口发起的采样，唤醒后台等待。重复点击不会发第二份取消。 */
    synchronized void cancel()
    {
        if (display.active && !display.cancelling)
        {
            publish(new Display(true, display.accepted, true, null, null));
            notifyAll();
        }
    }

    /** 预热和计时期间不插入网络查询，计时窗结束后读取同一轮；失联时尽力取消。 */
    private void collect(GameEndpoint game, String runId)
    {
        JSONObject result = null;
        String error = null;
        boolean submitted = false;
        try
        {
            if (!display.cancelling)
            {
                JSONObject start = new JSONObject().put("runId", runId).put("seconds", SECONDS).put("warmup", WARMUP)
                    .put("androidDevice", true).put("releaseOnFinish", true);
                submitted = true;
                requireRun(game.performance("begin", start), runId);
                accept();

                // 从接到回执开始等待整个测量窗，避免手机读状态干扰正式采样。
                delay((SECONDS + WARMUP) * 1000L);
                long deadline = SystemClock.elapsedRealtime() + 15000;
                while (!display.cancelling && SystemClock.elapsedRealtime() < deadline)
                {
                    JSONObject state = requireRun(game.performance("state", new JSONObject().put("runId", runId)), runId);
                    if (state.optBoolean("finished"))
                    {
                        result = state;
                        break;
                    }
                    delay(1000);
                }
                if (result == null && !display.cancelling)
                {
                    error = "游戏未在测量窗结束后返回结果，请确认它仍在前台";
                }
            }
        }
        catch (Exception failure)
        {
            error = failure.getMessage() == null ? "读取游戏采样失败" : failure.getMessage();
        }
        finally
        {
            if (submitted && result == null)
            {
                try
                {
                    JSONObject stopped = requireRun(game.performance("cancel", new JSONObject().put("runId", runId)), runId);
                    if (stopped.optBoolean("finished"))
                    {
                        result = stopped;
                    }
                }
                catch (Exception cleanup)
                {
                    if (error == null)
                    {
                        error = cleanup.getMessage() == null ? "暂未确认游戏采样停止" : cleanup.getMessage();
                    }
                }
            }
            publish(new Display(false, false, false, error, result));
        }
    }

    /** 只更新尚未被取消的采样，保留用户可能在接单期间发出的停止请求。 */
    private synchronized void accept()
    {
        if (!display.cancelling)
        {
            publish(new Display(true, true, false, null, null));
        }
    }

    /** 等待单调时钟期限，用户取消或断开会立即唤醒等待。 */
    private synchronized void delay(long duration) throws InterruptedException
    {
        long deadline = SystemClock.elapsedRealtime() + duration;
        long remaining = duration;
        while (!display.cancelling && remaining > 0)
        {
            wait(remaining);
            remaining = deadline - SystemClock.elapsedRealtime();
        }
    }

    /** 旧包或另一调用者的回执必须被拒绝。 */
    private static JSONObject requireRun(JSONObject data, String runId) throws IOException
    {
        if (!runId.equals(data.optString("runId")))
        {
            throw new IOException("游戏返回了不同的采样 runId");
        }
        return data;
    }

    /** 发布整轮 UI 状态，通知仅为可见控件刷新。 */
    private void publish(Display state)
    {
        display = state;
        changed.run();
    }
}
