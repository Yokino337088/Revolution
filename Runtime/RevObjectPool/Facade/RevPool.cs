// ============================================================
// RevPool.cs —— 对象池门面（业务唯一入口 · Unity 对象）
//
// 位置：Runtime\ObjectPool\Facade\
//
// 【一行取出、一行归还】
//     GameObject bullet = RevPool.Get("Battle/Bullet/Blue", firePoint);          // 同步（池里有 / 已加载）
//     Bullet b = RevPool.Get<Bullet>("Battle/Bullet/Blue", firePoint);           // 直接拿组件
//     RevPool.Return(bullet);                                                    // 归还
//     RevPool.Return(bullet, delayFrames: 5);                                    // 等 5 帧再回收（特效播完）
//
//     // 真机首次 / WebGL：用异步（和资源系统一致，同步加载在 WebGL 上不可用）
//     RevPool.GetAsync("Battle/Bullet/Blue", go => { ... }, firePoint, RevResGroup.Battle);
//
// 【它是怎么"基于资源加载系统"的】
//   · 取对象的路径就是资源系统的逻辑路径（可以用生成的 RevResPath 常量拼，有编译期保护）；
//   · prefab 通过 RevResManager 加载，池**端着那份 RevResHandle 引用** → 池在，prefab 就不会被卸载；
//   · 池销毁时还掉引用，prefab 才有机会卸载；
//   · 传 RevResGroup 后，prefab 归属该分组，切场景时可以整组清账（见下面 ClearGroup）。
//
// 【对象池自己不管"实例状态重置"，那是你的组件的事】
//   预制体上的脚本实现 IRevPoolable，取出/归还时会自动回调：
//     OnPoolGet()     → 重置动画、重新播放粒子、计时归零
//     OnPoolReturn()  → 停止音效、清空数据、解绑事件
//   不需要为了池化去改预制体结构，也不会在业务代码里到处写"取出后记得初始化"。
//
// 【C# 引用对象用另一个门面】RevRefPool（配置 DTO、消息体这类不需要 Unity 对象的东西）。
//
// 【线程约定】只在主线程使用（和 Unity 一致）。
// ============================================================
using System;
using UnityEngine;

namespace Revolution
{
    /// <summary>Unity 对象池门面（GameObject / 组件）。一行取出、一行归还。</summary>
    public static class RevPool
    {
        private static int _maxIdlePerPool = 64;
        private static int _delayRecycleFrames;

        // ==================== 配置 ====================

        /// <summary>
        /// 每条池的空闲上限（默认 64，0 = 不限）。
        /// 设置会立即对所有已有池生效（超出部分销毁），以后新建的池也用它。
        /// ★ 同时作用于 GameObject 池和 <see cref="RevRefPool"/> 引用池 —— 一个开关管两类，省得记两处。
        /// </summary>
        public static int MaxIdlePerPool
        {
            get => _maxIdlePerPool;
            set
            {
                _maxIdlePerPool = value < 0 ? 0 : value;
                RevGameObjectPools.ApplyCapacityToAll(_maxIdlePerPool);
                RevRefPools.ApplyCapacityToAll(_maxIdlePerPool);
            }
        }

        /// <summary>归还时默认延迟多少帧才真正进池（默认 0 = 立即）。</summary>
        public static int DelayRecycleFrames
        {
            get => _delayRecycleFrames;
            set => _delayRecycleFrames = value < 0 ? 0 : value;
        }

        /// <summary>日志出口（默认在 Unity 下接 Debug.LogWarning）。</summary>
        public static Action<string> Log
        {
            get => RevPoolLog.Sink;
            set => RevPoolLog.Sink = value;
        }

        // ==================== 取（同步）====================

        /// <summary>
        /// 取一个 GameObject。
        /// 池里有 → 直接复用（一行代码、零加载）；池里没有 → 通过资源系统加载 prefab 再实例化。
        /// ★ 同步加载在 WebGL / 小游戏上不可用，且真机首次加载会卡帧 —— 那两种情况用 <see cref="GetAsync"/>。
        /// </summary>
        /// <param name="rootPath">资源所在的根目录（用生成的 RevResPath 常量，如 "Battle/Bullet/"）</param>
        /// <param name="resName">资源名（不带扩展名，如 "Blue"）</param>
        /// <param name="parent">取出来挂到哪个节点下（可空）</param>
        /// <param name="group">资源分组（决定归属，便于整组清理）</param>
        public static GameObject Get(string rootPath, string resName, Transform parent = null, RevResGroup group = RevResGroup.Unknown)
            => RevGameObjectPools.Get(rootPath, resName, parent, group);

        /// <summary>取一个 GameObject 并直接拿它身上的组件（省掉 GetComponent）。</summary>
        public static T Get<T>(string rootPath, string resName, Transform parent = null, RevResGroup group = RevResGroup.Unknown)
            where T : Component
        {
            GameObject item = RevGameObjectPools.Get(rootPath, resName, parent, group);
            return ExtractComponent<T>(item, RevResPathUtil.Join(rootPath, resName));
        }

        /// <summary>
        /// 用已经持有的 prefab 引用取对象（省掉路径哈希，热路径更快）。
        /// 注意：这类池不持有资源引用，prefab 的生命周期由调用方保证。
        /// </summary>
        public static GameObject Get(GameObject prefab, Transform parent = null)
            => RevGameObjectPools.Get(prefab, parent);

        /// <summary>用 prefab 引用取对象并直接拿组件。</summary>
        public static T Get<T>(GameObject prefab, Transform parent = null) where T : Component
        {
            GameObject item = RevGameObjectPools.Get(prefab, parent);
            return ExtractComponent<T>(item, prefab != null ? prefab.name : "?");
        }

        // ==================== 取（异步）====================

        /// <summary>
        /// 异步取一个 GameObject（真机首次加载 / WebGL 必须走这条）。
        /// 加载失败时回调收到 <c>null</c>（失败原因看 <c>RevResManager.Get(rootPath, resName).ErrorReason</c>）。
        /// 返回 prefab 的资源句柄，用法与资源系统的 <c>LoadAsync</c> 一致。
        /// </summary>
        public static RevResHandle GetAsync(string rootPath, string resName, Action<GameObject> onFinished,
            Transform parent = null, RevResGroup group = RevResGroup.Unknown)
            => RevGameObjectPools.GetAsync(rootPath, resName, onFinished, parent, group);

        /// <summary>异步取组件（预制体上没有该组件时报错并回调 null，同时把对象还回池里）。</summary>
        public static RevResHandle GetAsync<T>(string rootPath, string resName, Action<T> onFinished,
            Transform parent = null, RevResGroup group = RevResGroup.Unknown) where T : Component
        {
            return RevGameObjectPools.GetAsync(rootPath, resName, item =>
            {
                if (item == null)
                {
                    onFinished?.Invoke(null);
                    return;
                }

                if (item.GetComponent<T>() is T component)
                {
                    onFinished?.Invoke(component);
                    return;
                }

                RevPoolLog.Error($"「{RevResPathUtil.Join(rootPath, resName)}」的预制体上没有 {typeof(T).Name} 组件，已把对象还回池里。");
                RevGameObjectPools.Return(item, 0);
                onFinished?.Invoke(null);
            }, parent, group);
        }

        // ==================== 归还 ====================

        /// <summary>
        /// 归还一个 GameObject。
        /// <paramref name="delayFrames"/> 传 -1（默认）表示用 <see cref="DelayRecycleFrames"/> 的全局设置。
        /// </summary>
        public static bool Return(GameObject item, int delayFrames = -1)
            => RevGameObjectPools.Return(item, ResolveDelay(delayFrames));

        /// <summary>归还一个组件（等价于归还它所在的 GameObject）。</summary>
        public static bool Return(Component item, int delayFrames = -1)
            => RevGameObjectPools.Return(item, ResolveDelay(delayFrames));

        // ==================== 清理 ====================

        /// <summary>清空某条池的空闲实例（池保留，之后还能继续用）。返回销毁数量。</summary>
        public static int Clear(string rootPath, string resName) => RevGameObjectPools.Clear(rootPath, resName);

        /// <summary>清空所有池的空闲实例（池保留）。返回销毁总数。</summary>
        public static int ClearAll() => RevGameObjectPools.ClearAll();

        /// <summary>
        /// 清空某个资源分组下所有池的空闲实例。
        /// ★ 和 <c>RevResBootstrap.Instance.Shutdown(group)</c> 配对调用：资源那边清账，
        ///   池这边也要把实例放掉，否则会留下"实例还在、贴图没了"的怪状态。
        /// </summary>
        public static int ClearGroup(RevResGroup group) => RevGameObjectPools.ClearGroup(group);

        /// <summary>销毁整条池（空闲实例销毁 + 还掉 prefab 引用）。按"根目录 + 资源名"找。</summary>
        public static bool DestroyPool(string rootPath, string resName) => RevGameObjectPools.DestroyPool(rootPath, resName);

        /// <summary>销毁整条池。按 prefab 引用找。</summary>
        public static bool DestroyPool(GameObject prefab) => RevGameObjectPools.DestroyPool(prefab);

        /// <summary>销毁全部对象池（GameObject 池 + 引用池）。回登录 / 退出游戏时用。</summary>
        public static void DestroyAll()
        {
            RevGameObjectPools.DestroyAll();
            RevRefPools.DestroyAll();
        }

        /// <summary>统一设置所有池的空闲上限（等价于设置 <see cref="MaxIdlePerPool"/>）。</summary>
        public static void ApplyCapacityToAll(int capacity) => MaxIdlePerPool = capacity;

        /// <summary>每条池最多留 <paramref name="keepCount"/> 个空闲实例，其余销毁。返回销毁总数。</summary>
        public static int TrimAll(int keepCount) => RevGameObjectPools.TrimAll(keepCount);

        // ==================== 驱动 / 诊断 ====================

        /// <summary>
        /// 推进延迟回收。<b>由 <see cref="RevPoolDriver"/> 每帧自动调用</b>，
        /// 业务不用管（工程外测试可以手动调它来验证延迟回收）。
        /// </summary>
        public static void Tick()
        {
            RevRefPools.Tick();
            RevGameObjectPools.Tick();
        }

        /// <summary>某条池的统计快照。</summary>
        public static RevPoolStats GetStats(string rootPath, string resName) => RevGameObjectPools.GetStats(rootPath, resName);

        /// <summary>把某条池的请求计数清零。</summary>
        public static void ResetStats(string rootPath, string resName) => RevGameObjectPools.ResetStats(rootPath, resName);

        /// <summary>把所有池（含引用对象池）的请求计数清零。</summary>
        public static void ResetStatsAll() => RevGameObjectPools.ResetStatsAll();

        /// <summary>所有池（含引用池）的合计统计。</summary>
        public static RevPoolStats GetGlobalStats()
        {
            RevPoolStats go = RevGameObjectPools.GetGlobalStats();
            RevPoolStats reference = RevRefPools.GetGlobalStats();

            go.Created += reference.Created;
            go.Destroyed += reference.Destroyed;
            go.Lost += reference.Lost;
            go.GetCount += reference.GetCount;
            go.Hit += reference.Hit;
            go.Miss += reference.Miss;
            go.ReturnCount += reference.ReturnCount;
            go.IdleCount += reference.IdleCount;
            go.ActiveCount += reference.ActiveCount;
            go.Recycling += reference.Recycling;

            return go;
        }

        /// <summary>把所有池的统计打成一坨文本（排查 / 汇报用）。</summary>
        public static string DumpStats() => RevGameObjectPools.DumpStats();

        // ==================== 内部 ====================

        private static int ResolveDelay(int delayFrames) => delayFrames < 0 ? _delayRecycleFrames : delayFrames;

        private static T ExtractComponent<T>(GameObject item, string source) where T : Component
        {
            if (item == null) return null;

            if (item.GetComponent<T>() is T component) return component;

            RevPoolLog.Error($"\"{source}\" 的预制体上没有 {typeof(T).Name} 组件，已把对象还回池里。");
            RevGameObjectPools.Return(item, 0);
            return null;
        }
    }
}
