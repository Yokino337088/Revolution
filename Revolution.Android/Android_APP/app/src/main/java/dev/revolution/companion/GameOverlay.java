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
import org.json.JSONObject;

/** 在一次游戏测试期间持有小范围悬浮窗。所有视图操作均由前台服务在主线程完成。 */
final class GameOverlay
{
    /** 展开时只展示其中一页，小屏幕不会叠加日志列表和采样面板。 */
    private enum Page { SUMMARY, TIMELINE, PERFORMANCE }
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
    /** 展开游戏在前台时使用的性能控制与摘要。 */
    private final View performance;
    /** 在性能页和连接摘要之间切换。 */
    private final Button performanceToggle;
    /** 读取或更新最近一轮运行状态。 */
    private final TextView performanceState;
    /** 游戏验证通过且本窗口空闲时允许开始。 */
    private final Button startPerformance;
    /** 只在本窗口正在采样时显示。 */
    private final Button cancelPerformance;
    /** 当前展开页，手柄收起后归到连接摘要。 */
    private Page page = Page.SUMMARY;
    /** 两个控件只操作当前身份已验证的会话。 */
    private PhoneSession session;
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
        performance = content.findViewById(R.id.overlay_performance);
        performanceToggle = content.findViewById(R.id.overlay_perf_toggle);
        performanceState = content.findViewById(R.id.overlay_perf_state);
        startPerformance = content.findViewById(R.id.overlay_perf_start);
        cancelPerformance = content.findViewById(R.id.overlay_perf_cancel);
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
        timelineToggle.setOnClickListener(view -> selectPage(page == Page.TIMELINE ? Page.SUMMARY : Page.TIMELINE));
        performanceToggle.setOnClickListener(view -> selectPage(page == Page.PERFORMANCE ? Page.SUMMARY : Page.PERFORMANCE));
        startPerformance.setOnClickListener(view -> {
            if (session != null)
            {
                session.startPerformance();
            }
        });
        cancelPerformance.setOnClickListener(view -> {
            if (session != null)
            {
                session.performance.cancel();
            }
        });
        content.findViewById(R.id.overlay_open).setOnClickListener(view ->
            service.startActivity(new Intent(service, MainActivity.class)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_SINGLE_TOP)));
        content.findViewById(R.id.overlay_disconnect).setOnClickListener(view -> service.disconnectByUser());
    }

    /** 发布实际启动状态后挂载。用户在系统撤销权限时，本轮仍可在通知或测试台操作。 */
    void show(PhoneSession session)
    {
        this.session = session;
        refreshPerformance(session);
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
        session = null;
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
            selectPage(Page.SUMMARY);
        }
    }

    /** 只让被选中的页持有可见控件，折叠日志列表时停止读取电脑活动。 */
    private void selectPage(Page selected)
    {
        page = selected;
        summary.setVisibility(page == Page.SUMMARY ? View.VISIBLE : View.GONE);
        if (page == Page.TIMELINE)
        {
            fitTimeline();
        }
        timeline.setVisibility(page == Page.TIMELINE ? View.VISIBLE : View.GONE);
        performance.setVisibility(page == Page.PERFORMANCE ? View.VISIBLE : View.GONE);
        timelineToggle.setText(page == Page.TIMELINE ? R.string.overlay_summary : R.string.agent_history);
        performanceToggle.setText(page == Page.PERFORMANCE ? R.string.overlay_summary : R.string.perf_title);
    }

    /** 主线程刷新采样页，旧会话的完成回调不能覆盖新会话。 */
    void refreshPerformance(PhoneSession owner)
    {
        if (session != owner)
        {
            return;
        }
        boolean ready = owner.phase == PhoneSession.Phase.READY;
        PhonePerformance.Display value = owner.performance.display();
        startPerformance.setEnabled(ready && !value.active);
        cancelPerformance.setVisibility(value.active ? View.VISIBLE : View.GONE);
        cancelPerformance.setEnabled(!value.cancelling);
        if (value.active)
        {
            int progress = value.cancelling ? R.string.perf_cancelling :
                value.accepted ? R.string.perf_running : R.string.perf_connecting;
            performanceState.setText(progress);
        }
        else if (value.error != null)
        {
            performanceState.setText(content.getContext().getString(R.string.perf_failed, value.error));
        }
        else if (value.report != null)
        {
            showReport(value.report);
        }
        else
        {
            performanceState.setText(ready ? R.string.perf_idle : R.string.overlay_starting);
        }
    }

    /** 展示与 Agent 共享的真实帧统计。取消、失焦和运行错误各有自己的状态。 */
    private void showReport(JSONObject data)
    {
        Context context = content.getContext();
        if (data.optBoolean("cancelled"))
        {
            performanceState.setText(R.string.perf_cancelled);
            return;
        }
        if (!data.optBoolean("success"))
        {
            performanceState.setText(context.getString(R.string.perf_failed, data.optString("error")));
            return;
        }
        String summaryText = context.getString(R.string.perf_result, data.optDouble("seconds"), data.optDouble("fps"),
            data.optDouble("p95Ms"), data.optDouble("p99Ms"));
        if (data.optInt("unfocusedFrames") > 0)
        {
            summaryText += "\n" + context.getString(R.string.perf_unfocused, data.optInt("unfocusedFrames"));
        }
        JSONObject device = data.optJSONObject("androidAfter");
        if (device != null && !device.isNull("processPssKiB"))
        {
            summaryText += "\n" + context.getString(R.string.perf_pss, device.optDouble("processPssKiB") / 1024);
        }
        String runId = data.optString("runId");
        if (runId.length() >= 8)
        {
            summaryText += "\n" + context.getString(R.string.perf_run, runId.substring(0, 8));
        }
        performanceState.setText(summaryText);
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
