package dev.revolution.companion;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import org.json.JSONObject;

/** 电脑和游戏之间传递原始 HTTP 状态与 JSON 字节，截图也沿用原始回执。 */
final class Wire
{
    // Unity 单次提交最多 4 MiB。
    static final int INPUT_LIMIT = 4 * 1024 * 1024;
    // RevMCP 单次游戏回执最多 24 MiB。
    static final int OUTPUT_LIMIT = 24 * 1024 * 1024;

    /** 每次网络动作的结果，202 表示接单，最终业务状态由 RevMCP 查工单。 */
    static final class Reply
    {
        // HTTP 状态码原样交回发起命令的电脑。
        final int status;
        // 原始 UTF-8 JSON 正文。
        final byte[] body;

        /** 包装一次实际回执，不改写游戏业务状态。 */
        Reply(int status, byte[] body)
        {
            this.status = status;
            this.body = body;
        }

        /** 编码手机拒绝转交或无法连接所选游戏的原因。 */
        static Reply error(int status, String message)
        {
            return new Reply(status, ("{\"success\":false,\"error\":" + JSONObject.quote(message) +
                "}").getBytes(StandardCharsets.UTF_8));
        }
    }

    /** 有上限地读取 JSON 或截图，超过协议预算立即关闭该次连接。 */
    static byte[] read(InputStream in, int limit) throws IOException
    {
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        byte[] block = new byte[16384];
        int count;
        while ((count = in.read(block)) != -1)
        {
            if (out.size() + count > limit)
            {
                throw new IOException("响应超出大小限制");
            }
            out.write(block, 0, count);
        }
        return out.toByteArray();
    }
}
