#nullable enable
using System.Collections.Generic;

namespace DemoFactory.GugaFramework.Pathfinding.Stage;

/// <summary>
/// <b>示范注册壳</b>——「宿主侧只递一份」的可运行实例：格长、格数、阻挡布局、单位名册、步进档、
/// 预算档、容量档全在这一份里。
///
/// <para>它扮演的角色是<b>接层的那一侧</b>：换任何原型都照这份的形状写自己那一份，
/// 而 <see cref="NavStage"/> / <see cref="NavStageSpec"/> / 场景壳与叠加层一个字都不动。</para>
///
/// <para><b>它不是「缺省词表」</b>：所有数字都在这份壳里给出，而场子（<see cref="NavStage"/>）自己
/// 一个缺省都不带（口径同 <c>SkillStageSpec.cs:15-19</c> 那个教训）。现在场子能跑起来，
/// 靠的是本壳递进来，不是靠场子自己揣了一份默认值。</para>
///
/// <para><b>这份场地摆的是哪种形状</b>：16×16、1 米一格，中部一道 L 形墙留一个缺口 ——
/// 三件事一次看清：绕障（净空腐蚀掉的部分画得出来）、单位半径影响可行域、路径折线在拐点处的朝向。
/// 换一块场地（无墙的平地、一格宽的窄廊、封满的绝境）改的是本壳的
/// <see cref="CellsZ"/> / <see cref="BlockedCells"/> / <see cref="Units"/>，不是场子。</para>
/// </summary>
public static class NavStageSample
{
    /// <summary>空间 id。名字与任何原型都不同名 —— 公共层不认识原型的空间。</summary>
    public const string SpaceLabel = "navstage.field";

    /// <summary>一格多少米。本壳的场地条件；换原型各选各的（寻路层文档 1.9 那条待确认项）。</summary>
    public const double CellMeters = 1.0;

    /// <summary>网格 X 方向格数。</summary>
    public const int CellsX = 16;

    /// <summary>网格 Z 方向格数。</summary>
    public const int CellsZ = 16;

    /// <summary>跑的那一个（速度取 15 米每秒 ⇒ 每步 1.5 米 &gt; 一格，子步与隧穿那一档看得见）。</summary>
    public const string RunnerId = "stage-runner";

    /// <summary>慢慢挪的那一个（1.5 米每秒）：与跑的那一个同框，比「两档步长差多少」在画面上一眼可读。</summary>
    public const string CreeperId = "stage-creeper";

    /// <summary>
    /// 一份注册口：<b>L 形墙 + 两个单位 + 全套数字档</b>。
    /// 墙的缺口在 (7, 4) 一带，两个单位从左下角那一侧出发，目标是墙另一侧的右上角。
    /// </summary>
    public static NavStageSpec Spec() => new()
    {
        Space = new SpaceId(SpaceLabel),
        CellMeters = CellMeters,
        CellsX = CellsX,
        CellsZ = CellsZ,
        OriginX = 0.0,
        OriginZ = 0.0,
        BlockedCells = WallCells(),
        Roster = new NavStageRoster
        {
            Units = new List<NavStageUnit>
            {
                new()
                {
                    Id = RunnerId,
                    RadiusMeters = 0.4,
                    MoveSpeedMetersPerSecond = 15.0,
                    Start = new NavPoint(1.5, 1.5),
                    FacingDegrees = 0,
                    Group = 0,
                    OpeningOrder = NavCommand.GoTo(new NavPoint(14.0, 14.0), 0.4),
                },
                new()
                {
                    Id = CreeperId,
                    RadiusMeters = 0.5,
                    MoveSpeedMetersPerSecond = 1.5,
                    Start = new NavPoint(1.5, 2.5),
                    FacingDegrees = 0,
                    Group = 0,
                    OpeningOrder = NavCommand.GoTo(new NavPoint(14.0, 13.0), 0.4),
                },
            },
        },
        Tuning = new NavMoveTuning
        {
            StepSeconds = 0.1,   // 「时间基准」：一律 0.1 秒
            LookAheadMeters = 2.0,
            StepSolveExpansions = 4000,
        },
        SolveBudget = new NavSolveBudget { PerRequestExpansions = 4000 },
        RecomputeTuning = new NavRecomputeTuning
        {
            GoalMoveToleranceMeters = 0.5,
            NotArrivedStepCap = 20,
        },
        Capacity = new NavStageCapacity
        {
            PathCacheEntries = 128,
            SolveRecords = 64,
            TrackedRequests = 64,
            EventRecords = 256,
        },
        Separation = new NavSeparationTuning { MinNetStepMeters = 0.05 },
    };

    /// <summary>
    /// L 形墙那一份阻挡布局：横向 z = 8 的 x = 4..12，纵向 x = 12 的 z = 5..8（共用拐角那一格），
    /// 留两个缺口 —— x = 4..7 那一段与 z = 5..7 那一段之间的缺口。
    /// <b>用 <c>for</c> 推出来而不是手抄 17 个下标</b>：手抄的那一份与 <c>CellsX</c>/<c>CellsZ</c> 脱钩之后，
    /// 改格数会静默把墙画到网格外（读数上看不出异常，只是绕路忽然变简单了）。
    /// </summary>
    public static IReadOnlyList<NavCell> WallCells()
    {
        var cells = new List<NavCell>();
        for (var x = 4; x <= 12; x++)
        {
            cells.Add(new NavCell(x, 8));
        }

        for (var z = 5; z <= 7; z++)
        {
            cells.Add(new NavCell(12, z));
        }

        return cells;
    }
}
