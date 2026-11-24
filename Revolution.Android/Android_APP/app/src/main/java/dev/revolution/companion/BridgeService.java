package dev.revolution.companion;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.Intent;
import android.os.Handler;
import android.os.IBinder;
import android.os.Looper;

/** 在游戏占据前台时继续接线，系统回收服务后必须重新由用户配对。 */
public final class BridgeService extends Service
{
    // 手机进程内唯一会话，Activity 重新创建后仍能显示当前阶段。
    static volatile PhoneSession current;
    // 会话结束后供返回测试台的人查看，不含任何认证凭据。
    static volatile String lastMessage = "尚未连接";
    // 通知渠道在应用私有范围内复用。
    private static final String CHANNEL = "active-session";
    // 使用同一个编号更新或移除本轮常驻通知。
    private static final int NOTICE = 17;
    // 生命周期变更必须回到服务主线程处理。
    private final Handler main = new Handler(Looper.getMainLooper());

    /** 前台通知由用户进入的测试台启动，可在通知中随时回到界面或断开。 */
    @Override public int onStartCommand(Intent intent, int flags, int startId)
    {
        if (intent == null || "stop".equals(intent.getAction()))
        {
            lastMessage = "已断开连接";
            stopSelf();
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
            intent.getStringExtra("address"), intent.getStringExtra("code"), intent.getBooleanExtra("isolate", true));
        current = session;
        new Thread(session::connect, "RevPhone-Connect").start();
        return START_NOT_STICKY;
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
                getSystemService(NotificationManager.class).notify(NOTICE, notification("已验证游戏 · " + owner.packageId));
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

    /** 释放当前会话和通知，不在系统重开进程后恢复旧凭据。 */
    @Override public void onDestroy()
    {
        PhoneSession owner = current;
        if (owner != null)
        {
            owner.close();
        }
        current = null;
        stopForeground(STOP_FOREGROUND_REMOVE);
        super.onDestroy();
    }

    /** 接线仅由私有启动 Intent 管理，不对其它应用提供 Binder。 */
    @Override public IBinder onBind(Intent intent)
    {
        return null;
    }
}
