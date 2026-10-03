// RevServiceLocator.cs —— ★ 小白从这里开始（3 分钟上手）
//
// ┌─ 三步走 ───────────────────────────────────────────────────────────────────────┐
// │ 1) 启动期装配（只写一次，缓存到 static readonly）                                │
// │    static readonly RevServiceLocator Services = RevServiceLocator.Create()      │
// │        .AddSingleton<ISoundService, SoundService>()        // 全局一份          │
// │        .AddSingleton<IConfigService>(Config.Load())        // 已有实例          │
// │        .AddScoped<ICombatContext, CombatContext>()         // 每局一份          │
// │        .Build();                                                                │
// │                                                                                 │
// │ 2) 运行期取用（一行）                                                             │
// │    var sound = Services.GetRequired<ISoundService>();                            │
// │    Services.Get<IDebugService>()?.Draw();                  // 可选能力：没有就 null │
// │                                                                                 │
// │ 3) 一局/一个场景的边界（作用域：同类型多实例、退出即释放）                          │
// │    using (var battle = Services.CreateScope())                                   │
// │    {                                                                             │
// │        var ctx = battle.GetRequired<ICombatContext>();    // 每局不同的一份       │
// │    }   // ← 离开 using：这一局的服务按"逆创建序"释放                              │
// └─────────────────────────────────────────────────────────────────────────────────┘
//
// 【接下来看哪】（只有前两个是你必须看的）
//   ① 本文件                         取服务 + 作用域 + 释放（唯一入口）
//   ② RevServiceBuilder.cs           注册：AddSingleton / AddScoped / Build
//   ③ Implementation\RevServiceRegistry.cs   引擎内部：实例表 + 逆序释放 + Tick 列表（不用读）
//   ④ Interfaces\                        可选钩子：RevIServiceInit（创建后取依赖）、RevITickable（每帧做事）
//
// 【三条铁律】
//   ① 装配只在一处（组合根）写完 —— Build 之后改不了，也别用全局静态字段到处赋值
//   ② 取服务优先用 GetRequired（"忘了注册"在启动期就炸出来）；只有可选能力才用 Get
//   ③ 谁创建谁负责释放：容器释放"工厂创建的服务"，不碰"你自己传进来的实例"
//
// 【怎么脱离 Unity 用】本模块是纯 C#（不引用 UnityEngine、不引用 RevTask），可直接放普通 .NET 工程里单元测试。

using System;
using System.Collections.Generic;
using System.Text;

namespace Revolution
{
    /// <summary>
    /// 服务定位器：把"底层框架要调上层能力"这件事，从"写死依赖 / 全局静态"变成"显式注册 + 显式获取"。
    /// <para>只读运行期：注册在 <see cref="RevServiceBuilder"/> 里做完，这里只负责取服务、建作用域、释放。</para>
    /// <para>线程模型：单线程（Unity 主线程）—— 不支持多线程同时解析同一个容器。</para>
    /// </summary>
    public sealed class RevServiceLocator : RevIServiceLocator, IDisposable
    {
        private readonly Dictionary<Type, RevServiceDescriptor> _descriptors;   // 装配表（只读；根与所有作用域共用同一份）
        private readonly RevServiceRegistry _own;                              // 本容器持有的实例（根 = Singleton 表；作用域 = Scoped 表）
        private readonly RevServiceLocator _root;                             // 根容器（自己就是根时 == this）
        private readonly RevServiceLocator _parent;                           // 作用域的父（根为 null）
        private readonly List<RevServiceLocator> _scopes;                     // 只有根用：跟踪子作用域，Dispose 时一并释放
        private readonly List<Type> _creating;                                // 循环依赖检测（整个容器树共用，挂在根上）
        private bool _disposed;

        // ============================================================
        // 入口
        // ============================================================

        /// <summary>
        /// 开始装配：返回注册表，链式 <c>AddSingleton / AddScoped</c> 之后调 <c>Build()</c> 拿到容器。
        /// <para>建议在启动期（游戏入口 / 场景 Boot）调用一次，结果缓存到 <c>static readonly</c> 字段。</para>
        /// </summary>
        /// <param name="expectedServiceCount">预估服务数量（只是字典初始容量，不影响功能）</param>
        public static RevServiceBuilder Create(int expectedServiceCount = 16)
            => new RevServiceBuilder(expectedServiceCount);

        /// <summary>根容器（由 <see cref="RevServiceBuilder.Build"/> 调用）</summary>
        internal RevServiceLocator(Dictionary<Type, RevServiceDescriptor> descriptors)
        {
            _descriptors = descriptors;
            _root = this;
            _parent = null;
            _own = new RevServiceRegistry(descriptors.Count);
            _scopes = new List<RevServiceLocator>(4);
            _creating = new List<Type>(4);
        }

        /// <summary>作用域容器（由 <see cref="CreateScope"/> 调用）</summary>
        private RevServiceLocator(Dictionary<Type, RevServiceDescriptor> descriptors, RevServiceLocator root)
        {
            _descriptors = descriptors;
            _root = root;
            _parent = root;
            _own = new RevServiceRegistry(8);       // 作用域里的服务数通常比全局少
            _scopes = null;                         // 只有根跟踪子作用域（作用域不再嵌套，见 CreateScope）
            _creating = null;                       // 用根的那份
        }

        // ============================================================
        // 取服务
        // ============================================================

        /// <summary>
        /// 取服务（必须有）：没注册就抛异常，错误信息里会列出"当前已注册的服务"。
        /// <para>用在"没有它就跑不起来"的依赖上 —— 让装配漏项在启动期暴露，而不是运行期某处空引用。</para>
        /// </summary>
        public T GetRequired<T>() where T : class
        {
            object instance = Resolve(typeof(T), required: true);
            return (T)instance;
        }

        /// <summary>
        /// 取服务（可能有）：没注册返回 <c>null</c>（用在可选能力上）。
        /// <para>注意：若 T 是 <b>Scoped</b> 服务却在根容器上取，会<b>抛异常</b> —— 那是用法错误（不是"没有"），
        /// 应该在作用域里取。</para>
        /// </summary>
        public T Get<T>() where T : class
        {
            object instance = Resolve(typeof(T), required: false);
            return instance as T;
        }

        /// <summary>尝试取服务：拿到返回 true，否则 false 且 <paramref name="service"/> 为 null。</summary>
        public bool TryGet<T>(out T service) where T : class
        {
            object instance = Resolve(typeof(T), required: false);
            service = instance as T;
            return service != null;
        }

        // ============================================================
        // 作用域（一局战斗 / 一个场景 / 一个房间）
        // ============================================================

        /// <summary>
        /// 开一个作用域：<b>共享</b>根容器的 Singleton，<b>各持一份</b> Scoped 服务；释放作用域即释放它持有的服务。
        /// <para>典型用法：<c>using (var battle = Services.CreateScope()) { ... }</c></para>
        /// <para>★ 作用域<b>不再嵌套</b>（一层就够）：并发多轮请建多个平行作用域，而不是作用域里再套作用域 ——
        /// 对应王者"生命周期越短的依赖越应该跟着作用域走，而不是挂全局"的原则，但刻意去掉了它无限制嵌套的空间。</para>
        /// </summary>
        public RevServiceLocator CreateScope()
        {
            EnsureUsable();

            if (_parent != null)
                throw new InvalidOperationException(
                    "作用域不再嵌套：请从根容器 CreateScope()（需要多轮并发就建多个平行作用域，不要作用域里再套作用域）");

            RevServiceLocator scope = new RevServiceLocator(_descriptors, this);
            _scopes.Add(scope);
            return scope;
        }

        // ============================================================
        // 驱动 / 释放
        // ============================================================

        /// <summary>
        /// 每帧驱动：调用本容器<b>自己创建的</b>、实现了 <see cref="RevITickable"/> 的服务。
        /// <para>根容器驱动它的 Singleton，作用域驱动它的 Scoped —— 同一个服务不会被驱动两次（宿主自己决定 Tick 谁）。</para>
        /// </summary>
        public void Tick(float deltaTime)
        {
            EnsureUsable();
            _own.Tick(deltaTime);
        }

        /// <summary>
        /// 释放：逆着创建顺序释放本容器持有的实例（只释放"容器创建的"，业务自己传进来的实例不动）。
        /// <para>根容器会先释放所有作用域，再释放自己的 Singleton；重复调用安全（幂等）。</para>
        /// <para>释放之后再取服务会抛异常（而不是默默给你一个已释放的对象）。</para>
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_parent == null && _scopes != null)
            {
                // ★ Bug 修复（2026-09-30）：先把列表整个摘下来再逐个释放。
                //   子作用域的 Dispose 会把自己从本列表移除（见下面"Bug 修复"注释）——
                //   如果边倒序遍历边 Remove，列表左移会导致每隔一个漏放一个；先夺走列表则两边都安全。
                List<RevServiceLocator> scopes = new List<RevServiceLocator>(_scopes);
                _scopes.Clear();
                for (int i = scopes.Count - 1; i >= 0; i--) scopes[i].Dispose();
            }

            // ★ Bug 修复（2026-09-30）：把自己从父（根）容器的跟踪列表里摘掉。
            //   原实现只加不减：每局 using (CreateScope()) 结束后，根的 _scopes 就永久多留一个
            //   已释放的作用域对象（连同它的实例表空壳）—— 长跑游戏每局泄漏一份，越积越多；
            //   根容器最后 Dispose 时还要白白遍历这一串死对象。
            //   （根容器释放路径在上面已先把列表整个摘空，此处 Remove 找不到目标是安全的空操作。）
            _parent?._scopes?.Remove(this);

            _own.DisposeAll();
        }

        // ============================================================
        // 解析（内部）
        // ============================================================

        /// <summary>解析一个类型：先看本容器 → 再看 Singleton 表 → 都没有就按注册信息创建。</summary>
        private object Resolve(Type type, bool required)
        {
            EnsureUsable();

            // ⓪ 循环依赖：这个类型"正在创建 / 初始化中"，却又被别人取用 → 报错并打印依赖链
            //    （王者靠"先入表再 Init"把这个环放过去了，代价是别人拿到一个半初始化的实例；这里明确拦下）
            List<Type> creating = _root._creating;
            if (creating.Count > 0 && creating.Contains(type))
                throw new InvalidOperationException(
                    $"检测到循环依赖：{BuildDependencyChain(creating, type)} —— 服务之间不能互相等对方先创建。" +
                    "做法：构造函数里只初始化自己（不要取别的服务），需要依赖就在 OnInit 里取，并且不要互相取用。");

            // ① 本容器已创建过（根 = Singleton；作用域 = 它自己的 Scoped）
            if (_own.TryGet(type, out object instance)) return instance;

            // ② 没注册过
            if (!_descriptors.TryGetValue(type, out RevServiceDescriptor descriptor))
            {
                if (required) throw new InvalidOperationException(BuildNotFoundMessage(type));
                return null;
            }

            // ③ Scoped：只能在作用域里取（根没有"一轮"的语义 —— 这条守卫对应王者"World 级服务被挂成全局单例"的经典事故）
            if (descriptor.Lifetime == RevServiceLifetime.Scoped)
            {
                if (_parent == null)
                    throw new InvalidOperationException(
                        $"{type.Name} 是 Scoped（每个作用域一份）服务，不能在根容器上取。" +
                        "请在它所属的边界里开作用域：using (var scope = Services.CreateScope()) { scope.GetRequired<" + type.Name + ">(); }");

                return CreateInto(_own, descriptor, owner: this);
            }

            // ④ Singleton：整个应用一份，存在根容器里
            if (_root._own.TryGet(type, out instance)) return instance;

            return CreateInto(_root._own, descriptor, owner: _root);
        }

        /// <summary>
        /// 创建并登记一个实例：<b>先入表、再 OnInit</b>（王者这个不变量是为了避免 Init 内部又去创建而打转），
        /// 但我们补上了它缺的一半：<b>创建或 OnInit 失败就回滚</b>，绝不把半初始化的实例留在容器里。
        /// </summary>
        private object CreateInto(RevServiceRegistry registry, RevServiceDescriptor descriptor, RevServiceLocator owner)
        {
            Type type = descriptor.ServiceType;
            List<Type> creating = _root._creating;

            creating.Add(type);     // 标记"正在创建 / 初始化"：期间被别的服务取用即判为循环依赖（由 Resolve 拦下）

            try
            {
                object instance = descriptor.Factory(owner);

                if (instance == null)
                    throw new InvalidOperationException($"创建 {type.Name} 时工厂返回了 null（{descriptor.Origin}）");

                registry.Add(type, instance, descriptor.OwnsInstance);

                try
                {
                    if (instance is RevIServiceInit init) init.OnInit(owner);
                }
                catch
                {
                    registry.Remove(type, instance);        // 回滚：不留半初始化的坏实例

                    // ★ Bug 修复（2026-09-30）：回滚时把实例本身也释放掉。
                    //   原实现只把它从表里摘掉 —— 但工厂已经把对象造出来了，如果它实现了 IDisposable
                    //   （文件句柄 / 网络连接 / 原生资源）， OnInit 一失败就再也没人管它 = 资源泄漏。
                    //   只有"容器负责释放"（OwnsInstance）的才释放；业务自己传进来的实例仍归业务。
                    //   Dispose 的异常要吞掉：回滚路径不能掩盖上面正在传播的 OnInit 原始异常。
                    if (descriptor.OwnsInstance && instance is IDisposable rollback)
                    {
                        try { rollback.Dispose(); }
                        catch { /* 回滚清理失败不能盖住 OnInit 的原始异常；容器已摘除该实例，不影响其余服务 */ }
                    }

                    throw;
                }

                return instance;
            }
            finally
            {
                creating.Remove(type);
            }
        }

        // ============================================================
        // 错误信息 / 调试显示
        // ============================================================

        private void EnsureUsable()
        {
            if (_disposed)
                throw new InvalidOperationException("这个 RevServiceLocator 已经 Dispose 了 —— 容器释放后不能再取服务（如果是场景卸载，请在那之前把需要的东西取好）");
        }

        private string BuildNotFoundMessage(Type type)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("取不到服务 ").Append(type.Name).Append("（没有注册过）。");
            builder.Append("\n· 已注册的服务：").Append(DescribeRegistered());
            builder.Append("\n· 本容器已创建的服务：").Append(_own.DescribeHeldTypes());

            if (!type.IsInterface)
                builder.Append("\n· 提醒：注册时通常写的是<接口>（AddSingleton<IXxx, Xxx>()），取的时候也要用接口（GetRequired<IXxx>()）");

            builder.Append("\n· 其它常见原因：① 忘了在组合根注册；② Scoped 服务要在 CreateScope() 出来的作用域里取；③ 容器已经 Dispose");
            return builder.ToString();
        }

        private string DescribeRegistered()
        {
            if (_descriptors.Count == 0) return "（一个都没有 —— 是不是忘了 Build 之前的 Add？）";

            StringBuilder builder = new StringBuilder();
            foreach (KeyValuePair<Type, RevServiceDescriptor> pair in _descriptors)
            {
                if (builder.Length > 0) builder.Append("、");
                builder.Append(pair.Key.Name);
            }

            return builder.ToString();
        }

        private static string BuildDependencyChain(List<Type> creating, Type repeated)
        {
            StringBuilder builder = new StringBuilder();

            for (int i = 0; i < creating.Count; i++)
            {
                if (builder.Length > 0) builder.Append(" → ");
                builder.Append(creating[i].Name);
            }

            builder.Append(" → ").Append(repeated.Name);
            return builder.ToString();
        }

        /// <summary>调试显示：例如 <c>RevServiceLocator(根容器，已注册 3 个，已创建 2 个)</c></summary>
        public override string ToString()
            => $"RevServiceLocator({(_parent == null ? "根容器" : "作用域")}，已注册 {_descriptors.Count} 个，已创建 {_own.Count} 个{(_disposed ? "，已释放" : string.Empty)})";
    }
}
