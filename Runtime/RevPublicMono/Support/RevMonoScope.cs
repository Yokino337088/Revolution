// ============================================================
// RevMonoScope.cs —— 作用域（using 一块，退出把这块加的监听全摘掉、协程全停）
//
// 位置：Runtime\RevPublicMono\Support\
//
// 【它解决什么】"忘记移除监听"是这类模块唯一的泄漏方式（旧实现连去重都没有，
//   同一个方法加两次就是每帧跑两次，而你看不出来）。作用域把"收尾"变成结构性的：
//
//   <code>
//   using (var scope = RevMono.OpenScope())
//   {
//       scope.AddUpdate(OnTick);              // 这块里加的监听者都归 scope 管
//       scope.StartCoroutine(RunSequence());  // 这块里起的协程也归 scope 管
//   }   // ← 出块：监听全摘、协程全停；不用记委托、不用逐个 Remove
//   </code>
//
// 【什么时候用它】进入一个界面/一段玩法/一个关卡时 —— 凡是"这段时间内才有意义"的每帧逻辑。
//
// 【和 RemoveAllOf 的关系】作用域内部就是用自己当 owner：
//   scope.AddUpdate(f) ≡ RevMono.AddUpdate(f, owner: scope)，
//   所以 RevMono.RemoveAllOf(scope) 与 scope.Dispose() 效果一致（想手动停也行）。
// ============================================================
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Revolution
{
    /// <summary>公共 Mono 模块的作用域：`using` 一块，退出时摘掉这块加的监听者、停掉这块起的协程。</summary>
    public sealed class RevMonoScope : IDisposable
    {
        private readonly List<Coroutine> _routines = new List<Coroutine>(4);
        private bool _disposed;

        internal RevMonoScope()
        {
        }

        /// <summary>这块里加一个每帧回调。</summary>
        public bool AddUpdate(Action action) => RevMono.AddUpdate(action, this);

        /// <summary>这块里加一个每帧最后的回调。</summary>
        public bool AddLateUpdate(Action action) => RevMono.AddLateUpdate(action, this);

        /// <summary>这块里加一个物理帧回调。</summary>
        public bool AddFixedUpdate(Action action) => RevMono.AddFixedUpdate(action, this);

        /// <summary>这块里起一条协程（退出作用域时会自动停掉它）。</summary>
        public Coroutine StartCoroutine(IEnumerator routine)
        {
            Coroutine routineHandle = RevMono.StartCoroutine(routine);
            if (routineHandle != null) _routines.Add(routineHandle);
            return routineHandle;
        }

        /// <summary>手动摘掉这块的全部监听者并停掉协程（不 Dispose 也能用；重复调用安全）。</summary>
        public int Close()
        {
            int removed = RevMono.RemoveAllOf(this);

            for (int i = 0; i < _routines.Count; i++) RevMono.StopCoroutine(_routines[i]);
            _routines.Clear();

            return removed;
        }

        /// <summary>退出作用域（重复 Dispose 安全）。</summary>
        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            Close();
        }
    }
}
