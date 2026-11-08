#nullable enable
namespace DemoFactory.GugaFramework.Vfx;

/// <summary>
/// 特效的四档 —— 一次触发在画面上以哪种方式存在。
///
/// <para><b>为什么不共用 <c>CueKind</c></b>：语义对齐、类型各一份。共用会把 Abilities 与 Vfx
/// 绑死 —— 在那边插一档，这边的逐字序列化跟着变，而这两个模块谁也不该知道对方长什么样。
/// 本仓先例是各模块自带 reason 枚举（<c>NavPathReason</c> / <c>PlacementReason</c>）。</para>
///
/// <para>档位与 <c>CueFeed</c> 的分类轴一一对齐（spec D5），不另起一套 —— 同一件事两个分类法，
/// 翻译时必然要查表对齐。</para>
/// </summary>
public enum VfxKind
{
    /// <summary>一次性：借节点演一下，演完自己结束，结束即归还池（spec D7）。</summary>
    OneShot = 0,

    /// <summary>持续：挂在目标身上一直亮着，每帧跟着目标走，撤下即硬切归还池（spec D8）。</summary>
    Sustained = 1,

    /// <summary>计数：画面跟着一个整数走（叠层数、tag 计数）。</summary>
    Counted = 2,

    /// <summary>带参数：画面由那几个标量决定（范围半径、扇形角度）。</summary>
    Descriptive = 3,
}
