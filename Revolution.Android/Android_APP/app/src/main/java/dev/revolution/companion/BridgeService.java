package dev.revolution.companion;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.content.res.Configuration;
import android.os.Handler;
import android.os.IBinder;
import android.os.Looper;
import android.provider.Settings;

/** 在游戏占据前台时继续接线，并在用户允许时展示可收起的测试悬浮窗。 */
public final class BridgeService extends Service
{
    // 手机进程内唯一会话，Activity 重新创建后仍能显示当前阶段。
    static volatile PhoneSession current;
    // 会话结束后供返回测试台的人查看，不含任何认证凭据。
    static volatile String lastMessage = "尚未连接";
    // 主线程上当前服务的引用，供测试台进入或离开前台时更新悬浮窗。
    private static BridgeService running;
    // 回到测试台时隐藏系统悬浮窗，避免遮挡应用自己的按钮。
    private static boolean dashboardVisible;
    // 仅存悬浮窗显示偏好，授权由 Android 系统管理。
    private static final String DISPLAY_PREFS = "session-display";
    // 通知渠道在应用私有范围内复用。
    private static final String CHANNEL = "active-session";
    // 使用同一个编号更新或移除本轮常驻通知。
    private static final int NOTICE = 17;
    // 生命周期变更必须回到服务主线程处理。
    private final Handler main = new Handler(Looper.getMainLooper());
    // 只有本轮服务能挂载和归还悬浮窗。
    private GameOverlay overlay;

    /** 发布本服务的生命周期，Activity 可在会话中通知其视图状态。 */
    @Override public void onCreate()
    {
        super.onCreate();
        running = this;
    }

    /** 前台通知由用户进入的测试台启动，可在通知中随时回到界面或断开。 */
    @Override public int onStartCommand(Intent intent, int flags, int startId)
    {
        if (intent == null || "stop".equals(intent.getAction()))
        {
            disconnectByUser();
            return START_NOT_STICKY;
        }
        if (current != null)
        {
            return START_NOT_STICKY;
        }
        getSystemService(NotificationManager.class).createNotificationChannel(
            new NotificationChannel(CHANNEL, "测试连接", NotificationManager.IMPORTANCE_LOW));
        startForeground(NOTICE, notification("等待电脑与所选游戏连接"));
        PhoneSession session = new PhoneSession(this, intent.getStringExtra("package"),
            intent.getStringExtra("address"), intent.getBooleanExtra("isolate", true));
        current = session;
        new Thread(session::connect, "RevPhone-Connect").start();
        return START_NOT_STICKY;
    }

    /** 返回用户保存的显示偏好；展示之前还需实时核对安卓系统许可。 */
    static boolean overlayEnabled(Context context)
    {
        return context.getSharedPreferences(DISPLAY_PREFS, MODE_PRIVATE).getBoolean("overlay", false);
    }

    /** 保存显示偏好并更新当前窗口，关闭后本轮立即收起。 */
    static void setOverlayEnabled(Context context, boolean enabled)
    {
        context.getSharedPreferences(DISPLAY_PREFS, MODE_PRIVATE).edit().putBoolean("overlay", enabled).apply();
        if (running != null)
        {
            running.refreshOverlay();
        }
    }

    /** 测试台在前台时让出屏幕，离开后恢复此次测试游戏的悬浮控件。 */
    static void reportDashboard(boolean visible)
    {
        dashboardVisible = visible;
        if (running != null)
        {
            running.refreshOverlay();
        }
    }

    /** 游戏启动后显示等待状态，直到同一个会话验证成功。 */
    void gameStarted(PhoneSession owner)
    {
        main.post(() -> {
            if (current == owner)
            {
                refreshOverlay();
            }
        });
    }

    /** 只在用户离开测试台且本轮游戏已启动时显示，权限撤回后归还窗口。 */
    private void refreshOverlay()
    {
        PhoneSession owner = current;
        if (!dashboardVisible && owner != null &&
            (owner.phase == PhoneSession.Phase.STARTING || owner.phase == PhoneSession.Phase.READY) &&
            overlayEnabled(this) && Settings.canDrawOverlays(this))
        {
            if (overlay == null)
            {
                overlay = new GameOverlay(this);
            }
            overlay.show(owner);
        }
        else if (overlay != null)
        {
            overlay.hide();
        }
    }

    /** 屏幕方向变化后重新按可见面积布置当前测试浮窗。 */
    @Override public void onConfigurationChanged(Configuration configuration)
    {
        super.onConfigurationChanged(configuration);
        refreshOverlay();
    }

    /** 由通知或浮窗显式断开，停止服务后归还当前连接和窗口。 */
    void disconnectByUser()
    {
        lastMessage = "已断开连接";
        stopSelf();
    }

    /** 通知只显示公开状态，配对码始终留在前台界面。 */
    private Notification notification(String text)
    {
        int flags = PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT;
        Intent open = new Intent(this, MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP);
        PendingIntent show = PendingIntent.getActivity(this, 0, open, flags);
        PendingIntent stop = PendingIntent.getService(this, 1, new Intent(this, BridgeService.class).setAction("stop"), flags);
        return new Notification.Builder(this, CHANNEL).setSmallIcon(R.drawable.ic_bridge)
            .setContentTitle("Revolution 测试连接").setContentText(text).setContentIntent(show)
            .setOngoing(true).setOnlyAlertOnce(true)
            .addAction(new Notification.Action.Builder(null, "断开连接", stop).build()).build();
    }

    /** 只更新当前会话的通知，旧探测结果不能覆盖用户的新连接。 */
    void notifyReady(PhoneSession owner)
    {
        main.post(() -> {
            if (current == owner && owner.phase == PhoneSession.Phase.READY)
            {
                GamePicker.remember(this, owner.packageId);
                refreshOverlay();
                getSystemService(NotificationManager.class).notify(NOTICE, notification("已验证游戏 · " + owner.packageId));
            }
        });
    }

    /** 将后台性能回执交给当前会话的悬浮窗，隐藏期间不挂载新窗口。 */
    void performanceChanged(PhoneSession owner)
    {
        main.post(() -> {
            if (current == owner && overlay != null)
            {
                overlay.refreshPerformance(owner);
            }
        });
    }

    /** 仅接受本轮退出回调，保留原因并停止前台服务。 */
    void finished(PhoneSession owner, String message)
    {
        main.post(() -> {
            if (current == owner)
            {
                lastMessage = message;
                stopSelf();
            }
        });
    }

    /** 同时归还窗口、会话和通知，短期令牌随本轮结束释放。 */
    @Override public void onDestroy()
    {
        PhoneSession owner = current;
        if (owner != null)
        {
            owner.close();
        }
        current = null;
        refreshOverlay();
        overlay = null;
        running = null;
        stopForeground(STOP_FOREGROUND_REMOVE);
        super.onDestroy();
    }

    /** 接线仅由私有启动 Intent 管理，不对其它应用提供 Binder。 */
    @Override public IBinder onBind(Intent intent)
    {
        return null;
    }
}
