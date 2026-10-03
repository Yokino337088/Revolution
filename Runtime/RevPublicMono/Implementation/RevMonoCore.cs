// ============================================================
// RevMonoCore.cs —— 监听列表内核（纯 C#，不引用 UnityEngine）
//
// 位置：Runtime\RevPublicMono\Implementation\
//
// 【三条规则（都是"修掉旧实现"来的）】
//   ① **单个监听者抛异常，不影响同一帧的其他监听者**：
//      旧实现是 `updateEvent?.Invoke()` —— 一个监听者抛异常，它后面的全部不执行，
//      异常还会冒到 Unity 的 Update 里，一帧刷一条错误、真正的问题被淹掉。
//   ② **派发期间改列表是安全的**：派发用"快照数组 + 脏标记"（脏了才重建，稳态零分配）。
//      语义很明确、可依赖：**本帧看到的是本帧开始时的名单** ——
//      回调里新增的监听者下一帧才跑；回调里移除的监听者本帧仍会被调到（下一帧起不再跑）。
//   ③ **去重 + 上限**：同一个委托加两次只生效一次（返回 false 告诉你没加进去）；
//      每个相位最多 256 个，超了报 Overflow 而不是静默超载。
//
// 【为什么不直接用 C# 的 event】
//   event 的 += / -= 在派发期间是安全的，但它**没有**去重、没有上限、
//   一个订阅者抛异常后面全停（上面第①条），也没法按 owner 批量清理。
// ============================================================
using System;
using System.Collections.Generic;

namespace Revolution
{
    /// <summary>公共 Mono 模块的内核（纯 C#；门面 <see cref="RevMono"/> 持有一个默认实例）。</summary>
    internal sealed class RevMonoCore
    {
        private readonly List<RevMonoListener>[] _lists = new List<RevMonoListener>[3];
        private readonly Action[][] _snapshots = new Action[3][];
        private readonly bool[] _dirty = { true, true, true };

        /// <summary>
        /// 首次用到监听列表时的回调 —— 由 Support 层装上（自动创建隐藏宿主）。
        /// ★ 内核不知道宿主是什么：纯 C# 部分只负责喊一声。
        /// </summary>
        internal static Action EnsureDriver;

        /// <summary>宿主是否就绪（Support 层创建成功后回填；没有宿主时仍然可以加监听者，只是暂时不派发）。</summary>
        internal static bool DriverReady;

        internal Action<RevMonoErrorReason, string> Failed;
        internal Action<Exception, string> OnException;

        internal RevMonoCore()
        {
            for (int i = 0; i < _lists.Length; i++) _lists[i] = new List<RevMonoListener>(16);
        }

        // ==================== 增 / 删 ====================

        /// <summary>加一个监听者。返回 false = 已经加过（去重）或已到上限（报 Overflow）。</summary>
        internal bool Add(RevMonoPhase phase, Action action, object owner)
        {
            if (action == null) return false;

            List<RevMonoListener> list = _lists[(int)phase];

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Action == action) return false;          // 已加过：静默去重（返回 false 就是信号）
            }

            if (list.Count >= RevMonoLimits.MaxListenersPerPhase)
            {
                string detail = $"相位 {phase} 的监听者已达上限 {RevMonoLimits.MaxListenersPerPhase}：本次新增被拒绝。" +
                                "常见原因：随对象销毁的监听者没用 owner / RemoveAllOf 清理。";
                Failed?.Invoke(RevMonoErrorReason.Overflow, detail);
                RevLog.Warn("[RevMono] " + detail, "Mono");   // 也进统一日志（业务没订阅 Failed 时也能看见）
                return false;
            }

            list.Add(new RevMonoListener(action, owner));
            _dirty[(int)phase] = true;

            // ★ 零配置：第一次用到就喊一声，让宿主适配层把隐藏驱动挂起来
            EnsureDriver?.Invoke();
            return true;
        }

        /// <summary>移除一个监听者（没加过也没事，返回 false）。</summary>
        internal bool Remove(RevMonoPhase phase, Action action)
        {
            if (action == null) return false;

            List<RevMonoListener> list = _lists[(int)phase];

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Action != action) continue;

                list.RemoveAt(i);
                _dirty[(int)phase] = true;
                return true;
            }

            return false;
        }

        /// <summary>移除某个 owner 的全部监听者（随对象销毁一行清理）。返回移除数量。</summary>
        internal int RemoveAllOf(object owner)
        {
            if (owner == null) return 0;                              // null 会被理解成"所有 owner" → 太危险，直接拒绝

            int removed = 0;
            for (int p = 0; p < _lists.Length; p++)
            {
                List<RevMonoListener> list = _lists[p];
                int removedInPhase = 0;                               // ★ 本相位自己的计数（脏标记只看它）

                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (!ReferenceEquals(list[i].Owner, owner)) continue;

                    list.RemoveAt(i);
                    removedInPhase++;
                }

                // ★ Bug 修复（2026-09-30）：脏标记必须按"本相位是否真的移除了"判断 ——
                //   原实现拿跨相位累计的 removed 判断：只要 Update 相位移除过 ≥1 个，
                //   一个都没动的 LateUpdate / FixedUpdate 也会被误标成脏，
                //   下一次 Tick 就白白重建那两个相位的快照数组（分配 + 整表复制），
                //   破坏了快照"脏了才重建、稳态零分配"的承诺（见 Snapshot 的注释）。
                if (removedInPhase > 0)
                {
                    removed += removedInPhase;
                    _dirty[p] = true;
                }
            }

            return removed;
        }

        /// <summary>清空全部监听者（进 Play / 切场景收尾用）。返回清掉的数量。</summary>
        internal int Clear()
        {
            int removed = 0;
            for (int p = 0; p < _lists.Length; p++)
            {
                removed += _lists[p].Count;
                _lists[p].Clear();
                _dirty[p] = true;
            }

            return removed;
        }

        internal int CountOf(RevMonoPhase phase) => _lists[(int)phase].Count;

        // ==================== 派发（宿主每帧调）====================

        internal void Tick(RevMonoPhase phase)
        {
            int index = (int)phase;
            Action[] snapshot = Snapshot(index);
            if (snapshot.Length == 0) return;

            for (int i = 0; i < snapshot.Length; i++)
            {
                Action action = snapshot[i];
                if (action == null) continue;

                try
                {
                    action();
                }
                catch (Exception e)
                {
                    // ★ 隔离：后面的监听者照常执行（旧实现是"一个抛异常，后面全不跑"）
                    Failed?.Invoke(RevMonoErrorReason.CallbackThrew, action.Method.DeclaringType?.Name + "." + action.Method.Name);
                    OnException?.Invoke(e,
                        $"[RevMono] {phase} 监听者 {action.Method.DeclaringType?.Name}.{action.Method.Name} 抛异常（已隔离，其余监听者照常）。" +
                        "要停止它请自己 Remove —— 框架不会因为一次异常就把它摘掉。");
                }
            }
        }

        /// <summary>取快照（脏了才重建：稳态零分配；派发期间列表被改动不影响本次快照）。</summary>
        private Action[] Snapshot(int index)
        {
            if (!_dirty[index] && _snapshots[index] != null) return _snapshots[index];

            List<RevMonoListener> list = _lists[index];
            var snapshot = new Action[list.Count];
            for (int i = 0; i < list.Count; i++) snapshot[i] = list[i].Action;

            _snapshots[index] = snapshot;
            _dirty[index] = false;
            return snapshot;
        }

        /// <summary>进 Play / 换工程时复位（Support 层的钩子调）。</summary>
        internal void ResetForNewSession()
        {
            Clear();

            // ★ Bug 修复（2026-09-30）：Failed 事件的订阅必须一并放掉。
            //   关闭 Domain Reload（项目常态）时静态字段跨局存活 —— 上一局订阅 Failed 的处理器
            //   （往往是引用已销毁 UI / 面板对象的闭包）在新一局仍会被调用：
            //   超上限 / 回调抛异常时报到死对象上（MissingReferenceException 或静默污染新逻辑），
            //   闭包引用还一直泄漏。这与上面 Clear() 防的是同一个"上一局残留"鬼故事，补上漏掉的口子。
            //   业务在运行期初始化（晚于 InstallInPlayer 的 SubsystemRegistration 时机）会重新订阅，不受影响。
            //   （OnException 是全局出口属性而非事件订阅，Install 的守卫本来就是"非空不覆盖"，保留不动。）
            Failed = null;

            DriverReady = false;
        }
    }
}
