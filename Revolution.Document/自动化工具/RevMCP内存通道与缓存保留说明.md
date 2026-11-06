# RevMCP 内存通道与缓存保留

更新：2026-11-06。

新版 Unity Editor 启动本机 HTTP 服务，RevCLI 与 RevMCP 自动优先使用内存任务。普通查询、GM 与 UI 调用不再创建 jobs/<id>/request.json 和 response.json；旧工程没有内存端点时继续使用文件桥。提交后的请求不会因断线切换通道或重新 POST。

收到结果后客户端发送 Ack，释放大响应，保留身份用于去重。结果未确认时有数量、体积与时限上限；已过期的原始请求拒绝重新执行。脚本重载保留必要的恢复检查点，普通操作不重放，原有明确可恢复的等待动作继续恢复。Test Runner、scenario、构建等需要证据的任务仍可写少量文件。

截图保存为 PNG，响应只传路径、大小、mimeType。RevMCP 返回 image 时读取该图片，不把 base64 长期写入工单回执。报告仍保留必要截图及 JSON，按保留策略回收，不能称为完全零磁盘写入。

Unity RevMCP → 连接设置 → 缓存与证据保留，可以设置自动回收、成功/失败工单天数、工单/截图/报告容量。默认成功工单7天、失败14天、工单256MiB；截图7天/512MiB；报告30天/256MiB。超过容量从最老已完成缓存开始回收。保留设置在 Library/RevolutionAutomation/storage-policy.json，凭据在 editor-http.json，不提交或分享凭据。

CLI：storage.status 预览；storage.configure 用 configJson 修改；storage.prune 默认 dryRun=true，传 dryRun=false 才应用清理。未完成工单、活动/TestRunner工单、原请求期限未过、最近15分钟证据和 .keep 标记受到保护。目录含符号链接/junction 时不跟随清理。受保护文件超过预算会报告 overBudget，不强行删除。

本次验证：新增 Python 内存通道5/5、RevMCP回归36/36、Unity新增内存/清理测试5/5通过。实际Editor状态查询返回 http-memory、jobDirectory=null。已有CLI全量初次出现一项常驻服务大请求连接中断，单独复验32/32通过。完整MCP游戏流程、跨重载长任务、Player与设备的最终验收尚未完成，不以这些基础结果宣称全部通过。游戏工程需同步新版框架和Python工具后才能使用；历史游戏工单未在本次人工清理。
