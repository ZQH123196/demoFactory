#nullable enable
using DemoFactory.GugaFramework.Pathfinding;

namespace DemoFactory.GugaFramework.Composition;

// 走位流水线要递进来的那份 —— 照 NavGridSpec 与 NavSolver 的纪律，公共层一个缺省都不给。
//
// 三份容量与两份档全部必递（required，少一列编译期就断）：它们各自有不成形状的下限
// （扩展上限至少 1、容量至少 1、步长秒数为正），让层里兜一个缺省等于替调用方挑数值，
// 而「这一档为什么是这个数」查不到出处。
//
// 后三样可空，因为它们是玩法决定不是数值档：有没有站位冲突、要不要发移动事件，
// 由原型的规则决定，公共层不该替它答。

/// <summary>
/// 建一条走位流水线要递进来的那份档。
///
/// <para><b>空间与网格是两列，不是一列</b>：网格自己报得出属于哪个空间（<c>NavSpace</c> 的构造会核对），
/// 两列摆在一起时那种「空间与网格对不上」在装配那一刻就报出来，而不等运行期一句「跨空间」。</para>
/// </summary>
public sealed record NavPipelineSpec
{
    /// <summary>这个相对坐标系的名字。必填。</summary>
    public required SpaceId Space { get; init; }

    /// <summary>落地形的那张网格。必填：<c>NavStepper</c> 从它所属的空间里取，builder 自己也要存一份给地形接线。</summary>
    public required NavGrid Grid { get; init; }

    /// <summary>求解预算档（每请求扩展上限）。必填。</summary>
    public required NavSolveBudget Budget { get; init; }

    /// <summary>重算判据那一档。必填：求解器与步进侧复用同一份，两处不会各有一套。</summary>
    public required NavRecomputeTuning Recompute { get; init; }

    /// <summary>路径缓存容量。必填。</summary>
    public required int CacheCapacity { get; init; }

    /// <summary>求解记录账本容量。必填。</summary>
    public required int RecordCapacity { get; init; }

    /// <summary>在跟踪的请求数上限。必填。</summary>
    public required int TrackedRequestCapacity { get; init; }

    /// <summary>步进档（一个逻辑步的秒数、前视距离、本步扩展上限）。必填。</summary>
    public required NavMoveTuning Move { get; init; }

    /// <summary>移动激活检查那一道裁决。空 = 全放行。</summary>
    public NavMoveCheck? Gate { get; init; }

    /// <summary>站位冲突的档。空 = 不开避让。</summary>
    public NavSeparationTuning? Separation { get; init; }

    /// <summary>移动事件出口。空 = 不发那几条事件。</summary>
    public NavMoveEvents? Events { get; init; }
}
