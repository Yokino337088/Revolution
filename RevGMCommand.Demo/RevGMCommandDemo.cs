// RevGMCommandDemo.cs —— GM 指令开箱示例（一行注册一条命令）
//
// 【怎么用】打开面板：菜单 Revolution.Tools/GM 指令面板（Ctrl+Shift+G）→ 试着打 "加金" / "缩放" / "频道"。
// 【说明】这里操作的是"演示用假数据"；真项目里把 `_gold += ...` 换成你自己的系统调用即可。
//         本文件只用框架 API（不碰 UnityEngine），所以它本身也能在工程外跑测试。

using System.Collections.Generic;
using Revolution;

namespace Revolution.Demo.GM
{
    /// <summary>GM 指令示例：经济 / 战斗 / 聊天 / 工具 四类命令。</summary>
    public static class RevGMCommandDemo
    {
        private static int _gold;
        private static float _scale = 1f;
        private static bool _godMode;
        private static readonly List<string> Sent = new List<string>();

        /// <summary>
        /// 注册入口：面板在编辑期会调用它（只做注册，不要在这里初始化业务）。
        /// </summary>
        [RevGMEntry("演示：经济 / 战斗 / 聊天 / 工具")]
        public static void Register()
        {
            // ① 一行注册：最简单形态（无参数）
            RevGM.Register("经济/清空金币", "把金币清零", args => { _gold = 0; });

            // ② 一行注册：带参数说明 + 默认值（面板显示 <数量|整数(默认 1000)>，执行前自动校验）
            //    注意 args.Int(0, 1000) 的第二个参数就是"不传时用多少"，要与 RevGMArg.Int("数量", 1000) 对齐
            RevGM.Register("经济/加金币", "给当前玩家加金币",
                           args => { _gold += args.Int(0, 1000); return $"金币 = {_gold}"; },
                           RevGMArg.Int("数量", 1000));

            // ③ 小数参数（不传 = 1）：★ 默认值是两处 —— 面板显示"(默认 1)"靠 RevGMArg，
            //    "不传时实际用多少"靠 args.Float(0, 1f) 的第二个参数；写成 args.Float(0)（= 0）就会
            //    "面板说 1、实际变 0"。（框架目前只有 IntRequired / Str("名字") 两种必填写法）
            RevGM.Register("战斗/缩放主角", "把主控英雄缩放成指定倍数",
                           args => { _scale = args.Float(0, 1f); return $"缩放 = {_scale}"; },
                           RevGMArg.Float("倍率", 1f));

            // ④ 开关参数（true/false、1/0、是/否、开/关 都认）：不传 = 声明里的默认值 true；关掉就打 "战斗/无敌 false"
            RevGM.Register("战斗/无敌", "开关无敌",
                           args => { _godMode = args.Bool(0, true); return _godMode ? "无敌：开" : "无敌：关"; },
                           RevGMArg.Bool("开启", true));

            // ⑤ 枚举参数（面板会列出候选值，点一下自动填进输入框）
            RevGM.Register("聊天/发消息", "往指定频道发一条测试消息",
                           args =>
                           {
                               string text = args.Str(1, string.Empty);
                               Sent.Add(args.Str(0) + ":" + text);
                               return $"已发送到 {args.Str(0)}：{(text.Length == 0 ? "（空）" : text)}";
                           },
                           RevGMArg.Enum("频道", "lobby", "guild", "customteam"), RevGMArg.Str("内容", ""));

            // ⑥ 高危命令：面板执行前会二次确认（Ctrl+回车可跳过）
            RevGM.Register("战斗/清空全场敌人", "把所有敌人血量清零（演示高危标记）",
                           args => "已清空全场敌人", RevGMFlags.HighRisk);

            // ⑦ 业务拒绝：抛 RevGMUsageException —— 这句话会原样显示在面板上（而不是静默没反应）
            RevGM.Register("工具/领取每日奖励", "演示「业务说不行」该怎么表达",
                           args => throw new RevGMUsageException("今天已经领过了（业务拒绝的例子）"));

            // ⑧ 返回一段信息，用于排障（面板会把返回值显示出来）
            RevGM.Register("工具/打印GM概览", "打印当前演示状态",
                           args => $"金币={_gold}　缩放={_scale}　无敌={_godMode}　命令总数={RevGM.Count}　已发消息={Sent.Count}");

            // ⑨ 隐藏命令：不进联想列表，但直接输入完整名仍可执行（给自己留的临时/废弃命令）
            RevGM.Register("工具/内部复位", "把演示状态全部复位（不进联想）",
                           args => { _gold = 0; _scale = 1f; _godMode = false; Sent.Clear(); return "已复位"; },
                           RevGMFlags.Hidden);
        }
    }
}
