// ============================================================
// RevInputBlock.cs —— "屏蔽输入"的句柄（纯 C#，不引用 UnityEngine）
//
// 位置：Runtime\RevInput\Core\
//
// 【要解决的问题】
//   弹窗打开时要挡掉世界输入，但**不能**把 ESC、返回键一起挡掉；
//   教程遮罩要挡住某根手指，但不能影响另一根在拖镜头的手 ——
//   这些都需要"精确到范围"的屏蔽，而不是一个全局开关。
//
// 【三条铁律】
//   ① **可精确到指针**：<see cref="RevInputBlockKind.World"/> 挡世界、<see cref="RevInputBlockKind.Pointer"/> 挡单指、
//      <see cref="RevInputBlockKind.All"/> 全挡（指针位置也读不到）。
//   ② **句柄可失效**：<see cref="IsEmpty"/> 为真时所有操作都是安全空操作（重复关闭不会出错）。
//   ③ **带 owner**：`RevInput.UnblockAllOf(owner)` 一行清掉某个对象申请的全部屏蔽（
//      和框架里 计时器 / 音效 / 公共 Mono 的作用域写法一致）。
// ============================================================

namespace Revolution
{
    /// <summary>
    /// 一次屏蔽申请的句柄。<b>不要自己 new</b>：用 <c>RevInput.Block(...)</c> 拿，
    /// 或 <c>using (var scope = RevInput.OpenScope()) { scope.Block(...) }</c>。
    /// </summary>
    public readonly struct RevInputBlock
    {
        /// <summary>无效句柄（所有操作都是空操作）。</summary>
        public static readonly RevInputBlock Empty = new RevInputBlock(0);

        internal readonly int Id;

        internal RevInputBlock(int id)
        {
            Id = id;
        }

        /// <summary>是否是无效句柄。</summary>
        public bool IsEmpty => Id <= 0;

        /// <summary>屏蔽种类（无效句柄返回 <see cref="RevInputBlockKind.None"/>）。</summary>
        public RevInputBlockKind Kind => Id <= 0 ? RevInputBlockKind.None : RevInput.GetBlockKind(Id);

        /// <summary>解除这次屏蔽（重复调用安全）。</summary>
        public bool Close() => Id > 0 && RevInput.Unblock(this);

        public override string ToString() => IsEmpty ? "RevInputBlock(Empty)" : "RevInputBlock#" + Id + "(" + Kind + ")";
    }
}
