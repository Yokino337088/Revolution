package dev.revolution.companion;

import android.content.Context;
import android.text.TextUtils;
import android.text.format.DateFormat;
import android.util.AttributeSet;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.AbsListView;
import android.widget.BaseAdapter;
import android.widget.LinearLayout;
import android.widget.ListView;
import android.widget.TextView;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/** 展开时读取电脑操作记录，收起时取消 HTTPS 读取并归还本轮本地分页缓存。 */
public final class AgentLogView extends LinearLayout
{
    // 仅在可见且跟随最新记录时读取，单位毫秒。
    private static final int UPDATE_MS = 600;
    // 复用原生 ListView 的屏外行，后台一次只收一页。
    private final ListView rows;
    // 未连接或当前没有动作时显示的文字区域。
    private final TextView empty;
    // 从当前位置回到最新记录。
    private final View latestButton;
    // 当前已加载的记录，从新到旧保留，关闭时释放。
    private final List<OperationPage.Entry> entries = new ArrayList<>();
    // 通过稳定工单序号绑定回收的行。
    private final FeedAdapter adapter = new FeedAdapter();
    // 本次实际游戏会话，用相同 HTTPS 证书和会话凭据读取。
    private PhoneSession source;
    // 只有列表打开时创建读取线程。
    private ExecutorService reader;
    // 关闭时撤销单独的日志连接，游戏转发继续工作。
    private DesktopConnection.RequestLease subscription;
    // 可见性来自窗口聚合状态，覆盖悬浮窗折叠和弹窗关闭。
    private boolean visible;
    // 一次只读取一页，避免旧游标覆盖最新列表。
    private boolean fetching;
    // 用户触摸或 fling 时让出位置，停止跟随最新。
    private boolean scrolling;
    // 最新页的已知内容版本，-1 首次请求整页。
    private long revision = -1;
    // 电脑仍保留的最老序号。
    private long oldest = 1;
    // 点击以展开该条摘要，零表示全部折叠。
    private long expanded;

    /** 供测试台弹窗创建相同的日志列表。 */
    AgentLogView(Context context)
    {
        this(context, null);
    }

    /** 只构建视图，并为手动翻页和跳转最新安排触发点。 */
    public AgentLogView(Context context, AttributeSet attrs)
    {
        super(context, attrs);
        setOrientation(VERTICAL);
        LayoutInflater.from(context).inflate(R.layout.agent_timeline, this, true);
        rows = findViewById(R.id.agent_rows);
        empty = findViewById(R.id.agent_empty);
        latestButton = findViewById(R.id.agent_latest);
        rows.setEmptyView(empty);
        rows.setAdapter(adapter);
        rows.setOnItemClickListener((parent, view, position, id) -> {
            expanded = expanded == id ? 0 : id;
            adapter.notifyDataSetChanged();
        });
        latestButton.setOnClickListener(v -> {
            rows.setSelection(0);
            revision = -1;
            request(0, true);
        });
        rows.setOnScrollListener(new AbsListView.OnScrollListener()
        {
            /** 保持用户正在阅读的行，滑到最新位置才恢复跟随。 */
            @Override public void onScrollStateChanged(AbsListView view, int state)
            {
                scrolling = state != SCROLL_STATE_IDLE;
            }

            /** 接近底部才排下一页，避免在布局内直接重入适配器。 */
            @Override public void onScroll(AbsListView view, int first, int count, int total)
            {
                if (first > 0 && first + count >= total - 3)
                {
                    removeCallbacks(loadOlder);
                    post(loadOlder);
                }
            }
        });
    }

    /** 会话替换后旧页作废；视图隐藏时不会因此发起网络读取。 */
    void bind(PhoneSession session)
    {
        if (source != session)
        {
            stopReading();
            source = session;
            if (visible)
            {
                startReading();
            }
        }
    }

    /** 系统的聚合可见性包含父容器折叠，隐藏时直接撤销读取。 */
    @Override public void onVisibilityAggregated(boolean isVisible)
    {
        super.onVisibilityAggregated(isVisible);
        visible = isVisible;
        if (isVisible)
        {
            startReading();
        }
        else
        {
            stopReading();
        }
    }

    /** 从窗口移除时归还后台线程和未完成的读取。 */
    @Override protected void onDetachedFromWindow()
    {
        visible = false;
        stopReading();
        super.onDetachedFromWindow();
    }

    /** 确认正在查看此会话后，才启动一次受可见性拥有的读取。 */
    private void startReading()
    {
        if (subscription != null)
        {
            return;
        }
        if (source == null)
        {
            empty.setText(R.string.agent_connect_first);
            return;
        }
        subscription = new DesktopConnection.RequestLease();
        reader = Executors.newSingleThreadExecutor(runnable -> new Thread(runnable, "RevPhone-Activity"));
        latestButton.setEnabled(true);
        refreshTick.run();
    }

    /** 关闭列表直接清空分页状态，迟到回执通过订阅身份过滤。 */
    private void stopReading()
    {
        removeCallbacks(refreshTick);
        removeCallbacks(loadOlder);
        if (subscription != null)
        {
            subscription.close();
            subscription = null;
            reader.shutdownNow();
            reader = null;
        }
        fetching = false;
        scrolling = false;
        revision = -1;
        oldest = 1;
        expanded = 0;
        latestButton.setEnabled(false);
        if (!entries.isEmpty())
        {
            entries.clear();
            adapter.notifyDataSetChanged();
        }
    }

    // 列表打开且位于最新位置时合并刷新，没有变化的请求不返回记录。
    private final Runnable refreshTick = new Runnable()
    {
        /** 只安排当前会话的新读取，离开页面后不再继续。 */
        @Override public void run()
        {
            if (subscription != null)
            {
                if (following())
                {
                    request(0, false);
                }
                postDelayed(this, UPDATE_MS);
            }
        }
    };

    // 用户翻到底部时读更早一页。
    private final Runnable loadOlder = () -> {
        if (!entries.isEmpty())
        {
            long before = entries.get(entries.size() - 1).sequence;
            if (before > oldest)
            {
                request(before, false);
            }
        }
    };

    /** 读取历史时固定当前位置，不用新得的第一条挤走正在看的行。 */
    private boolean following()
    {
        return !scrolling && rows.getFirstVisiblePosition() == 0 &&
            (rows.getChildCount() == 0 || rows.getChildAt(0).getTop() >= rows.getPaddingTop());
    }

    /** 在活跃订阅内发一次 HTTPS 分页请求；解析与等待均离开主线程。 */
    private void request(long before, boolean forceLatest)
    {
        if (subscription == null || fetching)
        {
            return;
        }
        fetching = true;
        PhoneSession session = source;
        DesktopConnection.RequestLease owner = subscription;
        long known = revision;
        reader.execute(() -> {
            OperationPage result = null;
            try
            {
                result = session.readOperations(before, known, owner);
            }
            catch (Exception unavailable)
            {
                // 折叠或断线时都会终止这次读取，回到最新可手动重试。
            }
            OperationPage page = result;
            post(() -> accept(owner, before, forceLatest, page));
        });
    }

    /** 以订阅身份过滤旧响应，只有当前页的数据可以进入适配器。 */
    private void accept(DesktopConnection.RequestLease owner, long before, boolean forceLatest, OperationPage page)
    {
        if (subscription != owner)
        {
            return;
        }
        fetching = false;
        if (page != null)
        {
            empty.setText(R.string.agent_empty);
            oldest = page.oldest;
            if (!page.unchanged && before == 0 && (forceLatest || following()))
            {
                showLatest(page);
            }
            else if (before != 0 && !entries.isEmpty() && entries.get(entries.size() - 1).sequence == before)
            {
                entries.addAll(page.rows);
                trimExpired();
                adapter.notifyDataSetChanged();
            }
        }
        else
        {
            empty.setText(R.string.agent_read_error);
        }
        if (page != null && (before == 0 || !page.rows.isEmpty()) &&
            rows.getFirstVisiblePosition() > 0 && rows.getLastVisiblePosition() >= entries.size() - 3)
        {
            post(loadOlder);
        }
    }

    /** 最新变化时更换首屏；仅状态改动时更新可见行，保留已载入的历史范围。 */
    private void showLatest(OperationPage page)
    {
        revision = page.revision;
        boolean sameHead = !entries.isEmpty() && !page.rows.isEmpty() && entries.size() >= page.rows.size() &&
            entries.get(0).sequence == page.rows.get(0).sequence;
        if (sameHead)
        {
            for (int i = 0; i < page.rows.size(); i++)
            {
                entries.set(i, page.rows.get(i));
            }
            int before = entries.size();
            trimExpired();
            if (before != entries.size())
            {
                adapter.notifyDataSetChanged();
            }
            else
            {
                int first = rows.getFirstVisiblePosition();
                for (int i = 0; i < rows.getChildCount(); i++)
                {
                    adapter.bindRow(rows.getChildAt(i), first + i);
                }
            }
        }
        else
        {
            entries.clear();
            entries.addAll(page.rows);
            adapter.notifyDataSetChanged();
            if (!entries.isEmpty())
            {
                rows.setSelection(0);
            }
        }
    }

    /** 电脑环形记录淘汰了旧项，从列表末尾归还相应缓存。 */
    private void trimExpired()
    {
        while (!entries.isEmpty() && entries.get(entries.size() - 1).sequence < oldest)
        {
            entries.remove(entries.size() - 1);
        }
    }

    /** 通过稳定序号和回收视图呈现页，滚动时不会创建所有行。 */
    private final class FeedAdapter extends BaseAdapter
    {
        /** 返回当前已加载的范围。 */
        @Override public int getCount()
        {
            return entries.size();
        }

        /** 按当前位置读已解析过的摘要。 */
        @Override public OperationPage.Entry getItem(int position)
        {
            return entries.get(position);
        }

        /** 使用电脑工单序号识别原来的行。 */
        @Override public long getItemId(int position)
        {
            return getItem(position).sequence;
        }

        /** 附加更早页时原有行保持身份。 */
        @Override public boolean hasStableIds()
        {
            return true;
        }

        /** 只为可见范围生成视图，从回收池取得 TextView 后直接重绑。 */
        @Override public View getView(int position, View recycled, ViewGroup parent)
        {
            if (recycled == null)
            {
                recycled = LayoutInflater.from(getContext()).inflate(R.layout.agent_row, parent, false);
                recycled.setTag(new FeedRow(recycled));
            }
            bindRow(recycled, position);
            return recycled;
        }

        /** 将同一流中的状态变化写入可见行。 */
        void bindRow(View view, int position)
        {
            FeedRow row = (FeedRow)view.getTag();
            OperationPage.Entry entry = getItem(position);
            row.bind(entry, entry.sequence == expanded);
        }
    }

    /** 可复用文本行的显示缓存。 */
    private final class FeedRow
    {
        // 具体的注册动作或 GM 命令。
        private final TextView command;
        // 时间、真实执行状态及回执。
        private final TextView detail;
        // 当前绑定的不可变快照。
        private OperationPage.Entry shown;
        // 是否显示完整摘要。
        private boolean wasExpanded;

        /** 持有行上仅有的两处 TextView，复用时无需再查找。 */
        FeedRow(View view)
        {
            command = view.findViewById(R.id.agent_command);
            detail = view.findViewById(R.id.agent_detail);
        }

        /** 同工单的相同内容不反复设置文字，点击行可看较长的参数。 */
        void bind(OperationPage.Entry entry, boolean expand)
        {
            if (shown != null && shown.sequence == entry.sequence && shown.status.equals(entry.status) &&
                shown.message.equals(entry.message) && wasExpanded == expand)
            {
                return;
            }
            shown = entry;
            wasExpanded = expand;
            boolean gm = "gm.exec".equals(entry.action);
            String instruction = gm && !entry.arguments.isEmpty() ? entry.arguments : entry.action;
            if (!gm && !entry.arguments.isEmpty())
            {
                instruction += "\n" + entry.arguments;
            }
            command.setText(getContext().getString(gm ? R.string.agent_gm : R.string.agent_action, instruction));
            command.setMaxLines(expand ? Integer.MAX_VALUE : 3);
            command.setEllipsize(expand ? null : TextUtils.TruncateAt.END);
            String suffix = entry.message.isEmpty() ? "" : "  " + entry.message;
            if (!entry.tool.isEmpty())
            {
                suffix += "  ·  " + entry.tool;
            }
            detail.setText(getContext().getString(R.string.agent_result, DateFormat.format("HH:mm:ss", entry.timeMs),
                getContext().getString(caption(entry.status)), suffix));
            detail.setMaxLines(expand ? Integer.MAX_VALUE : 2);
            detail.setEllipsize(expand ? null : TextUtils.TruncateAt.END);
        }
    }

    /** 将电脑传来的状态映射成简短词语，并区分接单与真实终态。 */
    private static int caption(String state)
    {
        switch (state)
        {
            case "delivering": return R.string.agent_delivering;
            case "accepted": return R.string.agent_submitted;
            case "cancelling": return R.string.agent_cancelling;
            case "success": return R.string.agent_succeeded;
            case "failed": return R.string.agent_failed;
            case "cancelled": return R.string.agent_cancelled;
            default: return R.string.agent_unknown;
        }
    }
}
