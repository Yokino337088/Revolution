// ============================================================
// NpcCloseupSequence.cs —— 场景四：NPC 特写镜头（演出 + 取消必须恢复现场）
//
// 位置：Assets\Revolution.Demo\RevActionSequence.Demo\
//
// 【王者原体系怎么做的】
//   Scene\DimensionNpcCamera.cs:44-143（InteractionObjectCamera 同理 49-123）
//     StartCloseup(world, onUpdate, onComplete)：用 LeanTween 推近 FOV + 位移，
//     同时隐藏 Player / Doll / HUDUI；StopCloseup 恢复。
//   原体系这套"推近 → 等演出 → 恢复"是靠外部回调 onUpdate/onComplete 串起来的，
//   一旦中途切场景/掉线，恢复逻辑就得靠调用方记得调 StopCloseup。
//
// 【本框架怎么表达同一件事，以及关键差别】
//   ① "等演出播完"用 WaitUntil + **超时保护** —— 原体系 MoveActorTo 的 MoveCompleted
//      永不置真导致序列永久卡死（05 文档 113 行记录过这类事故），这里有兜底；
//   ② "恢复现场"写在 .Finally 里 —— 正常跑完、被取消、步骤出错都会执行（框架契约，
//      不是"靠节点自觉 OnDisable"），只写一遍，永远不会留下"镜头还推着、HUD 还藏着"的状态。
// ============================================================
namespace Revolution.Demo.ActionSequence
{
    /// <summary>NPC 特写演出（对应 DimensionNpcCamera 的 StartCloseup / StopCloseup）</summary>
    public static class NpcCloseupDemoSequences
    {
        /// <summary>
        /// 特写演出：隐藏 HUD/玩家 → 推近 → 等对白 → 恢复。
        /// <para><b>同源拒绝</b>：同一个玩家重复点 NPC，不会叠两套特写。</para>
        /// </summary>
        public static readonly RevSequenceDefinition Closeup =
            RevSequence.Create("次元\\NPC 特写", RevSequenceConcurrency.RejectPerSource)
                .Do("隐藏 HUD", ctx => Scene(ctx)?.SetHudVisible(false))
                .Do("隐藏玩家模型", ctx => Scene(ctx)?.SetActorVisible(ctx.Source, false))
                .Do("推近镜头", ctx => Camera(ctx)?.PushIn(target: ctx.Source, fov: 25f, duration: 0.35f))
                .Wait(0.35f)                                        // 等镜头推到位
                .Do("播放对白音效", ctx => Sound(ctx)?.PostEvent("Play_CiYuan_Npc_Voice_Intro"))
                .WaitUntil("等玩家关闭对白（15s 超时保护）",
                           ctx => ctx.Get<IDemoInputService>()?.ConsumeClick() == true,
                           timeoutSeconds: 15f)
                // ★ 跑完也好、被取消（切场景 / 掉线 / 被打断）也好，都要把现场恢复回来 —— 写一遍就够
                .Finally(cleanup => cleanup
                    .Do("恢复镜头", ctx => Camera(ctx)?.Restore())
                    .Do("恢复 HUD 与玩家模型", ctx =>
                    {
                        Scene(ctx)?.SetHudVisible(true);
                        Scene(ctx)?.SetActorVisible(ctx.Source, true);
                    }))
                .OnCompleted(run => UnityEngine.Debug.Log($"[特写] 正常结束（耗时 {run.Elapsed:F2}s）"))
                .OnCancelled(run => UnityEngine.Debug.Log($"[特写] 被取消（耗时 {run.Elapsed:F2}s）→ 已强制恢复现场"))
                .Build();

        private static IDemoCameraService Camera(RevSequenceContext ctx) => ctx.Get<IDemoCameraService>();

        private static IDemoSoundService Sound(RevSequenceContext ctx) => ctx.Get<IDemoSoundService>();

        private static IDemoSceneService Scene(RevSequenceContext ctx) => ctx.Get<IDemoSceneService>();
    }
}
