// ============================================================
// RevInputCore.cs —— 输入内核（纯 C#：这里没有一行 UnityEngine）
//
// 位置：Runtime\RevInput\Implementation\
//
// 【要解决的问题】
//   一帧的输入要经过：采集 → 判相位 → 算轴 → 识别手势 → 派发事件 → 供业务查询。
//   这些都在这里完成，而且**一帧只做一次**（业务查询全是读结果，不再重算）。
//
// 【三条铁律】
//   ① **纯 C#**：时间以参数进来、采集 / 日志 / 宿主一律以委托注入 ——
//      所以这个文件能被链接进普通 .NET 工程跑断言（见模块 README 的验收一节）。
//   ② **零分配**：每帧只有位运算、数组写入与 for 循环；没有 LINQ、没有装箱、没有字符串拼接。
//   ③ **失败不静默也不炸**：配置类错误记 <see cref="RevInputErrorReason"/> 并走 `Failed` 事件；
//      业务监听者抛的异常被 <see cref="RevInputLog.Guard"/> 隔离，不影响同帧其它监听者。
//
// 【和 Support 的分工】
//   这里不建 GameObject、不读 Input、不碰 EventSystem —— 那些都在 `Support\` 里，
//   通过 <see cref="EnsureDriver"/> 与 <see cref="Device"/> 两个口子接进来。
// ============================================================

using System;
using System.Collections.Generic;

namespace Revolution
{
    /// <summary>输入内核：持有绑定表、快照、手势识别器、屏蔽栈与事件订阅。</summary>
    internal sealed class RevInputCore
    {
        /// <summary>宿主钩子：内核不知道该由谁每帧驱动，只负责喊一声（Support 层把它接到隐藏宿主上）。</summary>
        internal static Action EnsureDriver;

        // ── 组成 ────────────────────────────────────────────

        /// <summary>本帧快照（复用同一实例，零分配）。</summary>
        internal readonly RevInputSnapshot Snapshot = new RevInputSnapshot();

        /// <summary>绑定表 + 全部动作的每帧状态。</summary>
        internal readonly RevInputActionTable Actions = new RevInputActionTable();

        /// <summary>手势识别器。</summary>
        internal readonly RevGestureRecognizer Gestures = new RevGestureRecognizer();

        /// <summary>
        /// 采集口：由适配层（Support）接上引擎设备，或由测试脚本直接写入快照。
        /// 内核**不认识任何引擎类型**，只认这个委托 —— 这就是"内核纯 C#"的接缝。
        /// </summary>
        internal Action<RevInputSnapshot> Poll;

        /// <summary>采集源名字（只用于自检输出）。</summary>
        internal string SourceName = "无";

        /// <summary>命名轴提供者（Support 接到 <c>Input.GetAxis</c>；纯 C# 环境下为 null = 不支持命名轴）。</summary>
        internal Func<string, float> AxisProvider;

        /// <summary>失败事件（原因码 + 说明）。</summary>
        internal Action<RevInputErrorReason, string> Failed;

        /// <summary>业务强制指定的设备类型（<see cref="RevInputDeviceKind.Unknown"/> = 自动识别）。</summary>
        internal RevInputDeviceKind ForcedDeviceKind = RevInputDeviceKind.Unknown;

        /// <summary>手动驱动中（宿主让位，业务自己调 <c>Tick</c>）。</summary>
        internal bool ManualDriven;

        // ── 本帧信息（供查询与诊断）──────────────────────────
        internal double Realtime;
        internal int Frame;
        internal float DeltaTime;

        // ── 统计（只在 <c>Dump</c> 里读）─────────────────────
        internal int TickCount;
        internal int FramesWithInput;
        internal int GestureTotal;
        internal int BlockedFrames;
        internal int FailedCount;

        // ── 屏蔽栈 ──────────────────────────────────────────
        private struct BlockEntry
        {
            public int Id;
            public RevInputBlockKind Kind;
            public int PointerId;
            public object Owner;
        }

        private readonly List<BlockEntry> _blocks = new List<BlockEntry>(8);
        private int _nextBlockId = 1;

        // ── 事件订阅（列表 + owner，退订按引用相等）──────────
        private struct ActionEntry
        {
            public string Action;
            public Action Handler;
            public object Owner;
        }

        private struct GestureEntry
        {
            public RevGestureKind Kind;
            public Action<RevGestureEvent> Handler;
            public object Owner;
        }

        private readonly List<ActionEntry> _pressed = new List<ActionEntry>(16);
        private readonly List<ActionEntry> _released = new List<ActionEntry>(16);
        private readonly List<GestureEntry> _gestureHandlers = new List<GestureEntry>(8);

        // ── 事件驱动接入：监听者 / 轴 / 连发 ──────────────────
        private struct ListenerEntry
        {
            public RevInputListener Listener;
            public object Owner;
        }

        private struct AxisEntry
        {
            public string Axis;
            public Action<float> Handler;
            public object Owner;
        }

        private struct RepeatEntry
        {
            public string Action;
            public Action Handler;
            public object Owner;
        }

        /// <summary>轴变化判定阈值（小于它不算变化，避免摇杆抖动刷屏）。</summary>
        private const float AxisChangeEpsilon = 0.001f;

        private readonly List<ListenerEntry> _listeners = new List<ListenerEntry>(8);
        private readonly List<AxisEntry> _axisHandlers = new List<AxisEntry>(8);
        private readonly List<RepeatEntry> _repeatHandlers = new List<RepeatEntry>(8);

        /// <summary>每个轴上次投递的值（只在变化时回调；换会话时清空）。</summary>
        private readonly Dictionary<string, float> _axisLast = new Dictionary<string, float>(32);

        // ── 每帧推进 ────────────────────────────────────────

        /// <summary>
        /// 推进一帧。**由驱动调用**（隐藏宿主或 <c>RevInput.Tick</c>）。
        /// </summary>
        internal void Tick(float deltaTime, float unscaledDeltaTime, double realtime, int frame)
        {
            TickCount++;
            DeltaTime = deltaTime;
            Realtime = realtime;
            Frame = frame;

            // ① 采集：设备把原始输入写进快照
            Snapshot.Reset();
            Snapshot.DeltaTime = deltaTime;
            Snapshot.UnscaledDeltaTime = unscaledDeltaTime;
            Snapshot.Realtime = realtime;
            Snapshot.Frame = frame;

            Action<RevInputSnapshot> poll = Poll;
            if (poll != null)
            {
                try
                {
                    poll(Snapshot);
                }
                catch (Exception e)
                {
                    // 采集坏了不能拖垮整帧输入：隔离 + 继续（本帧按"无输入"处理）
                    RevInputLog.OnException?.Invoke(e, "输入采集(" + SourceName + ")");
                }
            }
            else
            {
                RevInputLog.V("[RevInput] 还没接采集口：Support 未装载时正常（纯 C# 跑断言时也如此）");
            }

            if (ForcedDeviceKind != RevInputDeviceKind.Unknown)
                Snapshot.Device = ForcedDeviceKind;

            bool allBlocked = IsBlocked(RevInputBlockKind.All);
            bool worldBlocked = allBlocked || IsBlocked(RevInputBlockKind.World);
            if (worldBlocked) BlockedFrames++;

            // ② 判相位 / 算轴：一次遍历算完所有动作
            Actions.Update(Snapshot, realtime, worldBlocked, AxisProvider);

            // ③ 手势识别（全部屏蔽时连识别都不做：指针位置也读不到）
            Gestures.BeginFrame();
            Gestures.Update(Snapshot, realtime, worldBlocked || allBlocked);

            // ④ 派发动作事件
            DispatchActionEvents(_pressed, pressed: true);
            DispatchActionEvents(_released, pressed: false);

            // ⑤ 派发手势事件
            if (Gestures.Count > 0)
            {
                GestureTotal += Gestures.Count;
                for (int i = 0; i < Gestures.Count; i++)
                {
                    RevGestureEvent e = Gestures.At(i);
                    for (int h = 0; h < _gestureHandlers.Count; h++)
                    {
                        GestureEntry entry = _gestureHandlers[h];
                        if (entry.Kind != RevGestureKind.None && entry.Kind != e.Kind) continue;

                        Action<RevGestureEvent> handler = entry.Handler;
                        RevGestureEvent evt = e;
                        try
                        {
                            handler(evt);            // ★ 同上：热路径不用 lambda，就地 try/catch
                        }
                        catch (Exception ex)
                        {
                            RevInputLog.OnException?.Invoke(ex, "OnGesture");
                        }
                    }

                    NotifyGestureToListeners(e);          // 同一条手势也推给监听者
                }
            }

            // ⑥ 派发连发事件（配了连发的动作；不用你自己在 Update 里查 Repeat）
            DispatchRepeatEvents();

            // ⑦ 派发轴事件（只在数值变化时回调；没有订阅者时零开销）
            DispatchAxisEvents();

            // ⑧ 投给监听者（事件驱动接入面：按下 / 抬起 / 连发；没有监听者时零开销）
            DispatchToListeners();

            if (Snapshot.KeyHeld.Any || Snapshot.PointerCount > 0) FramesWithInput++;
        }

        private void DispatchActionEvents(List<ActionEntry> list, bool pressed)
        {
            if (list.Count == 0) return;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                ActionEntry entry = list[i];
                if (string.IsNullOrEmpty(entry.Action) || entry.Handler == null)
                {
                    list.RemoveAt(i);
                    continue;
                }

                RevInputActionState s = Actions.State(entry.Action);
                bool fire = pressed ? s.Down : s.Up;
                if (!fire) continue;

                Action handler = entry.Handler;
                try
                {
                    // ★ 不用 RevInputLog.Guard：它要传 lambda（每次派发分配闭包）+ 拼字符串。
                    //   派发是逐帧热路径，就地 try/catch；上下文直接用动作名（本来就是现成字符串）。
                    handler();
                }
                catch (Exception e)
                {
                    RevInputLog.OnException?.Invoke(e, entry.Action);
                }
            }
        }

        // ── 事件驱动投递（监听者 / 轴 / 连发）───────────────

        private enum NotifyKind
        {
            Pressed = 0,
            Released = 1,
            Repeat = 2,
        }

        /// <summary>把本帧的动作事件推给监听者。**不依赖有没有人订阅委托**（只注册监听者也能收到）。</summary>
        private void DispatchToListeners()
        {
            if (_listeners.Count == 0) return;
            for (int i = 0; i < Actions.Count; i++)
            {
                RevInputBinding b = Actions.At(i);
                if (b == null) continue;
                RevInputActionState s = Actions.State(b.Action);
                if (s.Down) NotifyActionToListeners(b.Action, NotifyKind.Pressed);
                if (s.Up) NotifyActionToListeners(b.Action, NotifyKind.Released);
                if (s.Repeat) NotifyActionToListeners(b.Action, NotifyKind.Repeat);
            }
        }

        private void NotifyActionToListeners(string action, NotifyKind kind)
        {
            // ★ Bug 修复（2026-09-30）：不能用循环外缓存的 Count（旧代码 `for (int i = 0, n = ...; i < n; i++)`）——
            //   监听者回调里完全可能注销自己（如 OnInputPressed 里 RevInput.OffAllOf(this)，这是文档推荐的清理方式），
            //   列表变短后下一轮 `_listeners[i]` 越界，而这个取元素在 try **之外**，
            //   异常会直接冲出 Tick 炸到驱动的 Update 里。
            //   改为倒序 + 实时 Count：回调里注销自己时，已通知的（尾部）不受影响、未通知的（头部）位置不变 ——
            //   不越界、也不跳过任何人，与上面 DispatchActionEvents 委托版的倒序遍历行为一致。
            for (int i = _listeners.Count - 1; i >= 0; i--)
            {
                RevInputListener listener = _listeners[i].Listener;
                if (listener == null) continue;
                try
                {
                    switch (kind)
                    {
                        case NotifyKind.Pressed:
                            listener.OnInputPressed(action);
                            break;
                        case NotifyKind.Released:
                            listener.OnInputReleased(action);
                            break;
                        default:
                            listener.OnInputRepeat(action);
                            break;
                    }
                }
                catch (Exception e)
                {
                    RevInputLog.OnException?.Invoke(e, kind + "(" + action + ")");
                }
            }
        }

        private void NotifyGestureToListeners(in RevGestureEvent gesture)
        {
            if (_listeners.Count == 0) return;
            // ★ Bug 修复（2026-09-30）：同 NotifyActionToListeners —— 倒序 + 实时 Count，
            //   防止"回调里注销自己"导致的越界（旧代码缓存 Count 且取元素在 try 外）。
            for (int i = _listeners.Count - 1; i >= 0; i--)
            {
                RevInputListener listener = _listeners[i].Listener;
                if (listener == null) continue;
                RevGestureEvent evt = gesture;
                try
                {
                    listener.OnInputGesture(evt);
                }
                catch (Exception e)
                {
                    RevInputLog.OnException?.Invoke(e, "OnInputGesture(" + evt.Kind + ")");
                }
            }
        }

        /// <summary>连发事件：投给 <c>OnRepeat</c> 的订阅者。</summary>
        private void DispatchRepeatEvents()
        {
            if (_repeatHandlers.Count == 0) return;
            for (int i = 0; i < Actions.Count; i++)
            {
                RevInputBinding b = Actions.At(i);
                if (b == null || !Actions.State(b.Action).Repeat) continue;

                for (int h = _repeatHandlers.Count - 1; h >= 0; h--)
                {
                    RepeatEntry entry = _repeatHandlers[h];
                    if (entry.Action != b.Action || entry.Handler == null) continue;
                    Action handler = entry.Handler;
                    try
                    {
                        handler();
                    }
                    catch (Exception e)
                    {
                        RevInputLog.OnException?.Invoke(e, "OnRepeat(" + b.Action + ")");
                    }
                }
            }
        }

        /// <summary>
        /// 轴事件：投给 <c>OnAxis</c> 订阅者与监听者。
        /// **只在数值变化时回调**（订阅后第一次会给出当前值，方便初始化移动 / UI）。
        /// </summary>
        private void DispatchAxisEvents()
        {
            if (_axisHandlers.Count == 0 && _listeners.Count == 0) return;

            for (int i = 0; i < Actions.Count; i++)
            {
                RevInputBinding b = Actions.At(i);
                if (b == null) continue;

                float value = Actions.State(b.Action).Axis;
                float last;
                bool known = _axisLast.TryGetValue(b.Action, out last);
                if (known && Math.Abs(value - last) < AxisChangeEpsilon) continue;
                _axisLast[b.Action] = value;
                if (!known && value == 0f) continue;      // 一直没动过的轴不打扰业务

                for (int h = _axisHandlers.Count - 1; h >= 0; h--)
                {
                    AxisEntry entry = _axisHandlers[h];
                    if (entry.Axis != b.Action || entry.Handler == null) continue;
                    Action<float> handler = entry.Handler;
                    float v = value;
                    try
                    {
                        handler(v);
                    }
                    catch (Exception e)
                    {
                        RevInputLog.OnException?.Invoke(e, "OnAxis(" + b.Action + ")");
                    }
                }

                // ★ Bug 修复（2026-09-30）：同 NotifyActionToListeners —— 倒序 + 实时 Count，
                //   防止"轴回调里注销自己"导致的越界（旧代码缓存 Count 且取元素在 try 外）。
                for (int l = _listeners.Count - 1; l >= 0; l--)
                {
                    RevInputListener listener = _listeners[l].Listener;
                    if (listener == null) continue;
                    string axis = b.Action;
                    float v = value;
                    try
                    {
                        listener.OnInputAxis(axis, v);
                    }
                    catch (Exception e)
                    {
                        RevInputLog.OnException?.Invoke(e, "OnInputAxis(" + axis + ")");
                    }
                }
            }
        }

        internal void AddListener(RevInputListener listener, object owner)
        {
            if (listener == null) return;
            for (int i = 0; i < _listeners.Count; i++)
                if (ReferenceEquals(_listeners[i].Listener, listener)) return;   // 重复登记忽略
            _listeners.Add(new ListenerEntry { Listener = listener, Owner = owner });
        }

        internal bool RemoveListener(RevInputListener listener)
        {
            for (int i = _listeners.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(_listeners[i].Listener, listener)) continue;
                _listeners.RemoveAt(i);
                return true;
            }
            return false;
        }

        internal int ListenerCount => _listeners.Count;

        internal bool AddAxisHandler(string axis, Action<float> handler, object owner)
        {
            if (!RevInputActionTable.IsValidName(axis) || handler == null) return false;
            _axisHandlers.Add(new AxisEntry { Axis = axis, Handler = handler, Owner = owner });
            return true;
        }

        internal bool RemoveAxisHandler(string axis, Action<float> handler)
        {
            for (int i = _axisHandlers.Count - 1; i >= 0; i--)
            {
                if (_axisHandlers[i].Axis != axis) continue;
                if (handler != null && !ReferenceEquals(_axisHandlers[i].Handler, handler)) continue;
                _axisHandlers.RemoveAt(i);
                return true;
            }
            return false;
        }

        internal bool AddRepeatHandler(string action, Action handler, object owner)
        {
            if (!RevInputActionTable.IsValidName(action) || handler == null) return false;
            _repeatHandlers.Add(new RepeatEntry { Action = action, Handler = handler, Owner = owner });
            return true;
        }

        internal bool RemoveRepeatHandler(string action, Action handler)
        {
            for (int i = _repeatHandlers.Count - 1; i >= 0; i--)
            {
                if (_repeatHandlers[i].Action != action) continue;
                if (handler != null && !ReferenceEquals(_repeatHandlers[i].Handler, handler)) continue;
                _repeatHandlers.RemoveAt(i);
                return true;
            }
            return false;
        }

        // ── 复位 ────────────────────────────────────────────

        /// <summary>新会话复位（进 Play）：清掉上一局的一切（静态字段不会自己清）。</summary>
        internal void ResetForNewSession()
        {
            Actions.Clear();
            Gestures.ResetAll();
            Snapshot.Reset();
            _blocks.Clear();
            _pressed.Clear();
            _released.Clear();
            _gestureHandlers.Clear();
            _listeners.Clear();
            _axisHandlers.Clear();
            _repeatHandlers.Clear();
            _axisLast.Clear();
            _nextBlockId = 1;

            // ★ Bug 修复（2026-09-30）：Failed 事件的订阅必须一并放掉 —— 上面清了全部六类
            //   事件/监听订阅，唯独漏了它。关闭 Domain Reload（项目常态）时本实例跨局存活，
            //   上一局订阅 RevInput.Failed 的处理器（常是引用已销毁 UI 的闭包）在新一局
            //   还会被调用（键位配错等失败报到死对象上）且闭包引用泄漏 ——
            //   与 RevMono.ResetForNewSession 补 Failed 是同一个鬼故事的同一个口子。
            //   业务在运行期初始化（晚于 InstallInPlayer）会重新订阅，不受影响。
            Failed = null;

            ManualDriven = false;
            ForcedDeviceKind = RevInputDeviceKind.Unknown;
            TickCount = FramesWithInput = GestureTotal = BlockedFrames = FailedCount = 0;
        }

        /// <summary>
        /// 局部复位（失焦 / 切后台 / 换场景）：**清按键与进行中的手势，保留绑定与屏蔽**。
        /// 这是"切后台回来按键卡住"的正解 —— 抬起事件丢了，就把状态当成"全部松手"。
        /// </summary>
        internal void ResetAll(string reason)
        {
            Snapshot.FlushKeys();
            Actions.Update(Snapshot, Realtime, false, AxisProvider);   // 让动作状态立刻归一（Held 变 false）
            Gestures.ResetAll();
            Snapshot.Reset();
            RevInputLog.V("[RevInput] 复位：" + (reason ?? "未说明") + "（按键与手势已清空，绑定与屏蔽保留）");
        }

        // ── 屏蔽 ────────────────────────────────────────────

        internal RevInputBlock Block(RevInputBlockKind kind, int pointerId, object owner)
        {
            if (kind == RevInputBlockKind.None) return RevInputBlock.Empty;
            if (kind == RevInputBlockKind.Pointer && pointerId < 0)
            {
                Fail(RevInputErrorReason.PointerOutOfRange, "Pointer 屏蔽必须给 pointerId（鼠标用 -1 表示不适用）");
                return RevInputBlock.Empty;
            }

            var entry = new BlockEntry
            {
                Id = _nextBlockId++,
                Kind = kind,
                PointerId = pointerId,
                Owner = owner,
            };
            _blocks.Add(entry);
            RevInputLog.V("[RevInput] 屏蔽 " + kind + (kind == RevInputBlockKind.Pointer ? "#" + pointerId : string.Empty)
                          + "（当前共 " + _blocks.Count + " 条）");
            return new RevInputBlock(entry.Id);
        }

        internal bool Unblock(in RevInputBlock handle)
        {
            if (handle.IsEmpty) return false;
            for (int i = 0; i < _blocks.Count; i++)
            {
                if (_blocks[i].Id != handle.Id) continue;
                _blocks.RemoveAt(i);
                return true;
            }
            return false;
        }

        internal int UnblockAllOf(object owner)
        {
            if (owner == null) return 0;      // 拒绝"清掉所有人"：那是 ResetAll 的活
            int removed = 0;
            for (int i = _blocks.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_blocks[i].Owner, owner))
                {
                    _blocks.RemoveAt(i);
                    removed++;
                }
            }
            return removed;
        }

        internal bool IsBlocked(RevInputBlockKind kind)
        {
            for (int i = 0; i < _blocks.Count; i++)
                if (_blocks[i].Kind == kind) return true;
            return false;
        }

        internal bool IsPointerBlocked(int pointerId)
        {
            for (int i = 0; i < _blocks.Count; i++)
            {
                if (_blocks[i].Kind == RevInputBlockKind.All) return true;
                if (_blocks[i].Kind == RevInputBlockKind.Pointer && _blocks[i].PointerId == pointerId) return true;
            }
            return false;
        }

        internal bool WorldBlocked => IsBlocked(RevInputBlockKind.All) || IsBlocked(RevInputBlockKind.World);

        internal RevInputBlockKind GetBlockKind(int id)
        {
            for (int i = 0; i < _blocks.Count; i++)
                if (_blocks[i].Id == id) return _blocks[i].Kind;
            return RevInputBlockKind.None;
        }

        internal int BlockCount => _blocks.Count;

        // ── 读取（把屏蔽语义集中在这里：业务读到的就是"该不该给"）────

        /// <summary>有效指针数（全部屏蔽时为 0；被单独屏蔽的手指不计数）。</summary>
        internal int PointerCount
        {
            get
            {
                if (IsBlocked(RevInputBlockKind.All)) return 0;
                int count = 0;
                for (int i = 0; i < Snapshot.PointerCount; i++)
                    if (!IsPointerBlocked(Snapshot.Pointer(i).Id)) count++;
                return count;
            }
        }

        /// <summary>
        /// 读第 <paramref name="index"/> 根<b>有效</b>指针（被屏蔽的不占下标，保证与 <see cref="PointerCount"/> 成对）。
        /// </summary>
        internal RevPointerSample Pointer(int index)
        {
            if (IsBlocked(RevInputBlockKind.All) || index < 0) return default;
            int seen = 0;
            for (int i = 0; i < Snapshot.PointerCount; i++)
            {
                RevPointerSample sample = Snapshot.Pointer(i);
                if (IsPointerBlocked(sample.Id)) continue;
                if (seen == index) return sample;
                seen++;
            }
            return default;
        }

        internal float MouseX => IsBlocked(RevInputBlockKind.All) ? 0f : Snapshot.MouseX;
        internal float MouseY => IsBlocked(RevInputBlockKind.All) ? 0f : Snapshot.MouseY;
        internal float MouseDeltaX => IsBlocked(RevInputBlockKind.All) ? 0f : Snapshot.MouseDeltaX;
        internal float MouseDeltaY => IsBlocked(RevInputBlockKind.All) ? 0f : Snapshot.MouseDeltaY;
        internal float ScrollX => IsBlocked(RevInputBlockKind.All) ? 0f : Snapshot.ScrollX;
        internal float ScrollY => IsBlocked(RevInputBlockKind.All) ? 0f : Snapshot.ScrollY;

        internal bool MouseButton(RevMouseButton button, RevInputPhase phase)
        {
            if (IsBlocked(RevInputBlockKind.All)) return false;
            if (IsPointerBlocked(RevInputSnapshot.MousePointerId)) return false;
            return Snapshot.MouseButton(button, phase);
        }

        // ── 事件订阅 ────────────────────────────────────────

        internal bool AddActionHandler(string action, Action handler, object owner, bool pressed, out RevInputErrorReason reason)
        {
            reason = RevInputErrorReason.None;
            if (!RevInputActionTable.IsValidName(action))
            {
                reason = RevInputErrorReason.InvalidActionName;
                return false;
            }
            if (handler == null) return false;

            var list = pressed ? _pressed : _released;
            var entry = new ActionEntry { Action = action, Handler = handler, Owner = owner };
            list.Add(entry);
            return true;
        }

        internal bool RemoveActionHandler(string action, Action handler, bool pressed)
        {
            var list = pressed ? _pressed : _released;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].Action != action) continue;
                if (handler != null && !ReferenceEquals(list[i].Handler, handler)) continue;
                list.RemoveAt(i);
                return true;
            }
            return false;
        }

        internal void AddGestureHandler(RevGestureKind kind, Action<RevGestureEvent> handler, object owner)
        {
            if (handler == null) return;
            _gestureHandlers.Add(new GestureEntry { Kind = kind, Handler = handler, Owner = owner });
        }

        internal int RemoveAllOf(object owner)
        {
            if (owner == null) return 0;
            int removed = 0;
            for (int i = _pressed.Count - 1; i >= 0; i--)
                if (ReferenceEquals(_pressed[i].Owner, owner)) { _pressed.RemoveAt(i); removed++; }
            for (int i = _released.Count - 1; i >= 0; i--)
                if (ReferenceEquals(_released[i].Owner, owner)) { _released.RemoveAt(i); removed++; }
            for (int i = _gestureHandlers.Count - 1; i >= 0; i--)
                if (ReferenceEquals(_gestureHandlers[i].Owner, owner)) { _gestureHandlers.RemoveAt(i); removed++; }
            for (int i = _listeners.Count - 1; i >= 0; i--)                 // 事件驱动接入面
                if (ReferenceEquals(_listeners[i].Owner, owner)) { _listeners.RemoveAt(i); removed++; }
            for (int i = _axisHandlers.Count - 1; i >= 0; i--)
                if (ReferenceEquals(_axisHandlers[i].Owner, owner)) { _axisHandlers.RemoveAt(i); removed++; }
            for (int i = _repeatHandlers.Count - 1; i >= 0; i--)
                if (ReferenceEquals(_repeatHandlers[i].Owner, owner)) { _repeatHandlers.RemoveAt(i); removed++; }
            removed += UnblockAllOf(owner);
            return removed;
        }

        internal int HandlerCount => _pressed.Count + _released.Count + _gestureHandlers.Count
                                     + _listeners.Count + _axisHandlers.Count + _repeatHandlers.Count;

        // ── 失败上报 ────────────────────────────────────────

        internal void Fail(RevInputErrorReason reason, string detail)
        {
            FailedCount++;
            Action<RevInputErrorReason, string> handler = Failed;
            if (handler != null) RevInputLog.Guard("Failed(" + reason + ")", () => handler(reason, detail));

            if (RevInputLog.IsVerbose)
                RevInputLog.V("[RevInput] 失败 " + reason + "：" + detail);
        }

        // ── 自检 ────────────────────────────────────────────

        /// <summary>一行诊断（业务打到控制台用；只在调用时分配字符串）。</summary>
        internal string Dump()
        {
            var sb = new System.Text.StringBuilder(256);
            sb.Append("RevInput 采集=").Append(SourceName)
              .Append(" 帧=").Append(Frame)
              .Append(" 动作=").Append(Actions.Count)
              .Append(" 屏蔽=").Append(_blocks.Count)
              .Append(" 监听=").Append(HandlerCount)
              .Append(" 指针=").Append(Snapshot.PointerCount)
              .Append('\n').Append("累计：帧=").Append(TickCount)
              .Append(" 有输入帧=").Append(FramesWithInput)
              .Append(" 手势=").Append(GestureTotal)
              .Append(" 被屏蔽帧=").Append(BlockedFrames)
              .Append(" 失败=").Append(FailedCount);
            return sb.ToString();
        }
    }
}
