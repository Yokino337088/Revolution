package dev.revolution.companion;

import java.util.ArrayList;
import java.util.List;
import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

/** 电脑仅向已经展开列表的手机返回这些可显示的行，测试传输不携带日志。 */
final class OperationPage
{
    // 与接线器一致，每次最多交付四十条。
    static final int SIZE = 40;

    /** 已裁剪的单行摘要，在后台从本次 HTTPS 回执读取。 */
    static final class Entry
    {
        // 电脑为原工单分配的稳定序号。
        final long sequence;
        // 电脑首次收到工单的毫秒时间。
        final long timeMs;
        // Unity 注册标识或 gm.exec。
        final String action;
        // 真实 RevGM 命令或业务参数预览。
        final String arguments;
        // 可选的实际 RevMCP 工具名。
        final String tool;
        // 游戏接单或最终状态，为小写协议值。
        final String status;
        // 游戏回执的可见文字摘要。
        final String message;

        /** 把记录转换为界面需要的字段，格式异常由本次查询统一处理。 */
        Entry(JSONObject value) throws JSONException
        {
            sequence = value.getLong("seq");
            timeMs = value.getLong("timeMs");
            action = value.getString("action");
            arguments = value.optString("arguments");
            tool = value.optString("tool");
            status = value.getString("state");
            message = value.optString("message");
        }
    }

    // 电脑当前仍保留的最旧工单序号。
    final long oldest;
    // 下一次只读最新记录时使用的修订号。
    final long revision;
    // 最新记录未改变时可直接保留既有行。
    final boolean unchanged;
    // 仅保留收到的这一页，从新到旧排序。
    final List<Entry> rows;

    /** 每页按长度和顺序检查，不把新回执误追加到旧连接。 */
    OperationPage(JSONObject value) throws JSONException
    {
        oldest = value.getLong("oldest");
        revision = value.getLong("revision");
        unchanged = value.optBoolean("unchanged");
        JSONArray entries = value.getJSONArray("events");
        if (entries.length() > SIZE)
        {
            throw new JSONException("接线器回执超出单页范围");
        }
        rows = new ArrayList<>(entries.length());
        long previous = Long.MAX_VALUE;
        for (int i = 0; i < entries.length(); i++)
        {
            Entry row = new Entry(entries.getJSONObject(i));
            if (row.sequence >= previous || row.sequence < oldest)
            {
                throw new JSONException("接线器回执顺序无效");
            }
            rows.add(row);
            previous = row.sequence;
        }
    }
}
