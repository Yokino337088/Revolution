package dev.revolution.companion;

import android.annotation.SuppressLint;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.graphics.Insets;
import android.graphics.PixelFormat;
import android.graphics.Rect;
import android.hardware.display.DisplayManager;
import android.os.Build;
import android.view.ContextThemeWrapper;
import android.view.Display;
import android.view.Gravity;
import android.view.LayoutInflater;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewConfiguration;
import android.view.ViewGroup;
import android.view.WindowInsets;
import android.view.WindowManager;
import android.view.WindowMetrics;
import android.widget.TextView;
import android.widget.Button;

/** 在一次游戏测试期间持有小范围悬浮窗。所有视图操作均由前台服务在主线程完成。 */
final class GameOverlay
{
    // 游戏及系统所在屏幕的窗口管理器，用于挂载和归还本窗。
    private final WindowManager windows;
    // 只占用浮窗实际大小并保持游戏焦点的窗口参数。
    private final WindowManager.LayoutParams position;
    // 可折叠面板和拖动入口的根视图。
    private final View content;
    // 拖动手柄可点击展开，也支持无障碍动作。
    private final TextView handle;
    // 展开后显示游戏、连接和会话操作的区域。
    private final View panel;
    // 面板摘要与记录独立收放，使用列表时留出更多游戏画面。
    private final View summary;
    // 展开实时记录和返回会话摘要共用的按钮。
    private final Button timelineToggle;
    // 开启时只读取当前会话已收到的工单。
    private final AgentLogView timeline;
    // 当前客户端连接阶段。
    private final TextView state;
    // 系统安装包的显示名称。
    private final TextView game;
    // 电脑地址，同一会话保持不变。
    private final TextView address;
    // 区分拖动和点击的系统触摸阈值，单位像素。
    private final int dragThreshold;
    // 是否已由窗口管理器接管视图，避免重复挂载和移除。
    private boolean attached;
    // 每次触摸开始的屏幕横坐标。
    private float downX;
    // 每次触摸开始的屏幕纵坐标。
    private float downY;
    // 触摸开始时窗口横向偏移。
    private int startX;
    // 触摸开始时窗口纵向偏移。
    private int startY;
    // 开始拖动后本次手势不再视为点击。
    private boolean dragging;

    /** 构建系统悬浮窗，窗口参数由 WindowManager 读取。拖动使用物理坐标，点击交回 performClick。 */
    @SuppressLint({"InflateParams", "ClickableViewAccessibility", "RtlHardcoded"})
    GameOverlay(BridgeService service)
    {
        Context windowContext = service;
        if (Build.VERSION.SDK_INT >= 30)
        {
            Display display = service.getSystemService(DisplayManager.class).getDisplay(Display.DEFAULT_DISPLAY);
            windowContext = service.createDisplayContext(display)
                .createWindowContext(WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY, null);
        }
        windows = windowContext.getSystemService(WindowManager.class);
        Context themed = new ContextThemeWrapper(windowContext, R.style.AppTheme);
        content = LayoutInflater.from(themed).inflate(R.layout.game_overlay, null);
        handle = content.findViewById(R.id.overlay_handle);
        panel = content.findViewById(R.id.overlay_panel);
        summary = content.findViewById(R.id.overlay_summary);
        timeline = content.findViewById(R.id.overlay_timeline);
        timelineToggle = content.findViewById(R.id.overlay_log_toggle);
        state = content.findViewById(R.id.overlay_state);
        game = content.findViewById(R.id.overlay_game);
        address = content.findViewById(R.id.overlay_address);
        dragThreshold = ViewConfiguration.get(themed).getScaledTouchSlop();
        position = new WindowManager.LayoutParams(WindowManager.LayoutParams.WRAP_CONTENT,
            WindowManager.LayoutParams.WRAP_CONTENT, WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY,
            WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE | WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL,
            PixelFormat.TRANSLUCENT);
        position.gravity = Gravity.TOP | Gravity.LEFT;
        position.setTitle("RevolutionGameOverlay");

        // 首次靠右显示。展开或横竖屏切换后都按新尺寸调整位置。
        position.x = Math.max(0, available().width() - dp(100));
        position.y = dp(72);
        content.addOnLayoutChangeListener((view, left, top, right, bottom, oldLeft, oldTop, oldRight, oldBottom) ->
            move(position.x, position.y));
        handle.setOnClickListener(view -> expand(panel.getVisibility() != View.VISIBLE));
        handle.setOnTouchListener((view, motion) -> drag(view, motion));
        timelineToggle.setOnClickListener(view -> showTimeline(timeline.getVisibility() != View.VISIBLE));
        content.findViewById(R.id.overlay_open).setOnClickListener(view ->
            service.startActivity(new Intent(service, MainActivity.class)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_SINGLE_TOP)));
        content.findViewById(R.id.overlay_disconnect).setOnClickListener(view -> service.disconnectByUser());
    }

    /** 发布实际启动状态后挂载。用户在系统撤销权限时，本轮仍可在通知或测试台操作。 */
    void show(PhoneSession session)
    {
        state.setText(session.phase == PhoneSession.Phase.READY ? R.string.overlay_ready : R.string.overlay_starting);
        address.setText(session.address);
        timeline.bind(session);
        if (timeline.getVisibility() == View.VISIBLE)
        {
            fitTimeline();
        }
        try
        {
            PackageManager pm = content.getContext().getPackageManager();
            game.setText(pm.getApplicationInfo(session.packageId, 0).loadLabel(pm));
        }
        catch (PackageManager.NameNotFoundException unavailable)
        {
            game.setText(session.packageId);
        }
        if (!attached)
        {
            try
            {
                windows.addView(content, position);
                attached = true;
            }
            catch (WindowManager.BadTokenException | SecurityException unavailable)
            {
                // 权限可能在检查之后被撤销，测试会话本身仍可运行。
            }
        }
    }

    /** 回到测试台或关闭会话时归还窗口，再次显示默认保持为小手柄。 */
    void hide()
    {
        if (attached)
        {
            attached = false;
            try
            {
                windows.removeViewImmediate(content);
            }
            catch (IllegalArgumentException removedBySystem)
            {
                // 系统可能已在权限或窗口变化时移除窗口。
            }
            expand(false);
        }
    }

    /** 展开时更新触摸范围和无障碍名称，收起后露出原有游戏区域。 */
    private void expand(boolean expanded)
    {
        panel.setVisibility(expanded ? View.VISIBLE : View.GONE);
        handle.setContentDescription(content.getContext().getString(expanded ? R.string.overlay_collapse : R.string.overlay_expand));
        handle.setText(expanded ? R.string.overlay_collapse : R.string.overlay_bubble);
        if (!expanded)
        {
            showTimeline(false);
        }
    }

    /** 显示记录时隐藏会话摘要，折叠后让列表停止本地刷新。 */
    private void showTimeline(boolean show)
    {
        summary.setVisibility(show ? View.GONE : View.VISIBLE);
        if (show)
        {
            fitTimeline();
        }
        timeline.setVisibility(show ? View.VISIBLE : View.GONE);
        timelineToggle.setText(show ? R.string.overlay_summary : R.string.agent_history);
    }

    /** 给短屏幕和横屏保留手柄和返回按钮，再把余下高度交给可滚动列表。 */
    private void fitTimeline()
    {
        ViewGroup.LayoutParams size = timeline.getLayoutParams();
        int height = Math.max(dp(112), Math.min(dp(224), available().height() - dp(122)));
        if (size.height != height)
        {
            size.height = height;
            timeline.setLayoutParams(size);
        }
    }

    /** 点击手柄展开或收起，超过系统移动阈值后更新屏幕位置。 */
    private boolean drag(View view, MotionEvent event)
    {
        switch (event.getActionMasked())
        {
            case MotionEvent.ACTION_DOWN:
                downX = event.getRawX();
                downY = event.getRawY();
                startX = position.x;
                startY = position.y;
                dragging = false;
                return true;
            case MotionEvent.ACTION_MOVE:
                float deltaX = event.getRawX() - downX;
                float deltaY = event.getRawY() - downY;
                dragging = dragging || Math.abs(deltaX) > dragThreshold || Math.abs(deltaY) > dragThreshold;
                if (dragging)
                {
                    move(startX + Math.round(deltaX), startY + Math.round(deltaY));
                }
                return true;
            case MotionEvent.ACTION_UP:
                if (!dragging)
                {
                    view.performClick();
                }
                return true;
            case MotionEvent.ACTION_CANCEL:
                return true;
            default:
                return false;
        }
    }

    /** 返回扣除系统栏及刘海后可用于摆放浮窗的大小，旧系统使用显示资源尺寸。 */
    private Rect available()
    {
        if (Build.VERSION.SDK_INT >= 30)
        {
            WindowMetrics metrics = windows.getCurrentWindowMetrics();
            Rect bounds = metrics.getBounds();
            Insets insets = metrics.getWindowInsets().getInsetsIgnoringVisibility(
                WindowInsets.Type.systemBars() | WindowInsets.Type.displayCutout());
            return new Rect(0, 0, bounds.width() - insets.left - insets.right,
                bounds.height() - insets.top - insets.bottom);
        }
        return new Rect(0, 0, content.getResources().getDisplayMetrics().widthPixels,
            content.getResources().getDisplayMetrics().heightPixels);
    }

    /** 将浮窗留在屏幕内，位置没有变化时跳过系统窗口更新。 */
    private void move(int x, int y)
    {
        Rect bounds = available();
        int left = Math.max(0, Math.min(x, bounds.width() - content.getWidth() - dp(8)));
        int top = Math.max(0, Math.min(y, bounds.height() - content.getHeight() - dp(8)));
        if (position.x == left && position.y == top)
        {
            return;
        }
        position.x = left;
        position.y = top;
        if (attached)
        {
            try
            {
                windows.updateViewLayout(content, position);
            }
            catch (IllegalArgumentException | SecurityException unavailable)
            {
                hide();
            }
        }
    }

    /** 将布局 dp 换成窗口管理器所需的屏幕像素。 */
    private int dp(int value)
    {
        return Math.round(value * content.getResources().getDisplayMetrics().density);
    }
}
