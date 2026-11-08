#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace DemoFactory.GugaFramework.Pathfinding;

/// <summary>
/// 命令空间的五档（spec「命令空间」逐字那五条）加一个「还没发令」。
///
/// <para><b>五档是归一后的样子，不是五种走法</b>：五条命令最终都落成
/// 「<b>目标描述 + 到达容差</b>」这一个形状，于是
/// <see cref="NavMoveSolution"/> 那条流水线只跑一遍、到达只判一次 ——
/// 每个命令各判一次到达，<c>StepToward</c> 就会在每个原型复活一次（「寻路单入口」）。</para>
///
/// <para><b>不寻路的移动也在这五条里</b>：原地不动、沿固定点走、守一片区域
/// 都不需要一条路，把它们放到模块外面，「寻路单入口」 反而会逼出一个「寻路的归寻路、
/// 不寻路的各写一份」的双入口。</para>
///
/// <para>取值从 1 起，0 留给 <see cref="Unset"/>：读数里「这个单位还没收到命令」
/// 与「命令是原地不动」是两件事，合成一档就把「没人管它」与「有人让它站着」抹成同一件事。</para>
/// </summary>
public enum NavCommandKind
{
    /// <summary>还没发令：注册之后、第一条命令之前。这一档不产位移，但也不是「原地不动」。</summary>
    Unset = 0,

    /// <summary>原地不动（驻守）：目标描述就是「站住的那个点」。</summary>
    Hold = 1,

    /// <summary>走到点：目标描述是一个点。</summary>
    GoTo = 2,

    /// <summary>追实体：目标每步在动，<b>目标坐标每步由宿主重读</b>后递进来（层里没有单位表）。</summary>
    Chase = 3,

    /// <summary>沿固定点走：一串航点。走到一个、到位、游标进下一个 —— 判定与到位处理同一条。</summary>
    AlongWaypoints = 4,

    /// <summary>守一片区域：目标描述是区域中心加半径。半径<b>只作读数交回</b>，不参与到达判定。</summary>
    GuardArea = 5,
}

/// <summary>
/// 目标描述 —— 命令空间归一后的那<b>一个</b>形状。五个命令各有一份工厂，
/// 缺件与不成形状的在那五个入口当场抛（「桥只转发」），不靠使用方自觉填齐。
///
/// <para><b>归一之后每个命令只剩「本步该朝哪个点走」这一个答案</b>
/// （<see cref="GoalFor"/>）：原地不动的目标是驻守点、追实体的目标是目标这一轮的坐标、
/// 沿固定点走的目标是游标指的那一个航点。到达判定因此只有一处。</para>
/// </summary>
public sealed record NavTarget
{
    /// <summary>这一档是五个命令里的哪一个。</summary>
    public required NavCommandKind Kind { get; init; }

    /// <summary>
    /// 点目标：走到点的落点 / 守区域区域的中心 / 原地不动的驻守点。
    /// 其余三档是 <see cref="NavPoint.Zero"/> —— 那一列对它们没有意义，
    /// 放着不走是因为「五档各一个类型」会把归一拆成五份。
    /// </summary>
    public NavPoint Point { get; init; }

    /// <summary>追实体那一档的目标 id（<b>只作读数</b>：层里没有单位表，不解析这个 id）。</summary>
    public string? EntityId { get; init; }

    /// <summary>追实体那一档的目标坐标。<b>每步由宿主重读后重发命令</b>，层不缓存、不外推。</summary>
    public NavPoint EntityPosition { get; init; }

    /// <summary>沿固定点走那一档的航点串（首尾照收，游标从 0 起）。其余四档是空表。</summary>
    public IReadOnlyList<NavPoint> Waypoints { get; init; } = Array.Empty<NavPoint>();

    /// <summary>守区域那一档的区域半径（米）。<b>只作读数</b>：到达判定看中心，不看半径。</summary>
    public double AreaRadiusMeters { get; init; }

    /// <summary>原地不动（驻守）：目标描述就是那个驻守点。</summary>
    /// <param name="standAt">站住的地方（米）。</param>
    public static NavTarget Hold(NavPoint standAt)
    {
        NavCheck.Finite(standAt.X, "驻守点的 X");
        NavCheck.Finite(standAt.Z, "驻守点的 Z");
        return new NavTarget { Kind = NavCommandKind.Hold, Point = standAt };
    }

    /// <summary>走到点。</summary>
    /// <param name="point">落点（米）。</param>
    public static NavTarget GoTo(NavPoint point)
    {
        NavCheck.Finite(point.X, "落点的 X");
        NavCheck.Finite(point.Z, "落点的 Z");
        return new NavTarget { Kind = NavCommandKind.GoTo, Point = point };
    }

    /// <summary>追实体。<b>目标坐标是这一轮的读数</b>：它每步在动，宿主每步重读一次递回来。</summary>
    /// <param name="entityId">目标 id（只作读数交回）。</param>
    /// <param name="position">目标这一轮的位置（米）。</param>
    public static NavTarget Chase(string entityId, NavPoint position)
    {
        NavCheck.Text(entityId, nameof(entityId));
        NavCheck.Finite(position.X, "目标坐标的 X");
        NavCheck.Finite(position.Z, "目标坐标的 Z");
        return new NavTarget
        {
            Kind = NavCommandKind.Chase,
            EntityId = entityId,
            EntityPosition = position,
        };
    }

    /// <summary>沿固定点走。一串航点，非空。</summary>
    /// <param name="waypoints">航点串（米，顺序即走法）。</param>
    public static NavTarget Along(IReadOnlyList<NavPoint> waypoints)
    {
        ArgumentNullException.ThrowIfNull(waypoints, "航点串必填：空串是一条「哪也不去」的命令，那一档是原地不动。");
        if (waypoints.Count == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(waypoints),
                waypoints.Count,
                "航点串至少一个点 —— 空串读作「哪也不去」，那一档叫原地不动；把它当成沿固定点走会让「它为什么不动」有两套读法。");
        }

        for (var i = 0; i < waypoints.Count; i++)
        {
            NavCheck.Finite(waypoints[i].X, $"航点串第 {i.ToString(CultureInfo.InvariantCulture)} 个点的 X");
            NavCheck.Finite(waypoints[i].Z, $"航点串第 {i.ToString(CultureInfo.InvariantCulture)} 个点的 Z");
        }

        return new NavTarget { Kind = NavCommandKind.AlongWaypoints, Waypoints = waypoints };
    }

    /// <summary>守一片区域。<b>第一档是走到区域中心</b>，到了之后区域内怎么待归上层；
    /// 半径只作读数交回（宿主要判「还在不在区域内」时有这一列可读）。</summary>
    /// <param name="center">区域中心（米）。</param>
    /// <param name="radiusMeters">区域半径（米，必须为正）。</param>
    public static NavTarget Guard(NavPoint center, double radiusMeters)
    {
        NavCheck.Finite(center.X, "区域中心的 X");
        NavCheck.Finite(center.Z, "区域中心的 Z");
        NavCheck.Positive(radiusMeters, nameof(radiusMeters),
            "守区域的半径必须为正 —— 半径 0 的「守一个点」就是走到点那一档，两条命令形状一样却读不出是哪一条。");
        return new NavTarget
        {
            Kind = NavCommandKind.GuardArea,
            Point = center,
            AreaRadiusMeters = radiusMeters,
        };
    }

    /// <summary>
    /// 本步该朝哪个点走 —— <b>五档归一后的那一个答案</b>。
    /// 到达判定只对着这一个点判（<see cref="NavArrive"/>），于是「原地不动为什么也算到达」
    /// 与「追实体为什么不逐帧盯目标」都不需要各写一遍。
    /// </summary>
    /// <param name="waypointCursor">沿固定点走那一档的游标；其余四档不读它。</param>
    public NavPoint GoalFor(int waypointCursor)
    {
        if (Kind != NavCommandKind.AlongWaypoints)
        {
            return Kind == NavCommandKind.Chase ? EntityPosition : Point;
        }

        if (waypointCursor < 0 || waypointCursor >= Waypoints.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(waypointCursor),
                waypointCursor,
                $"游标 {waypointCursor.ToString(CultureInfo.InvariantCulture)} 落在 {Waypoints.Count.ToString(CultureInfo.InvariantCulture)} 个航点之外 —— 游标由模块自己推，"
                + "越界说明「到位就进下一个」那一处漏了一次。");
        }

        return Waypoints[waypointCursor];
    }

    /// <summary>逐字可读的一行（invariant，「double 与不变文化」）。</summary>
    public string Describe() => Kind switch
    {
        NavCommandKind.Unset => "未下令",
        NavCommandKind.Hold => $"原地不动 驻守点 {Point}",
        NavCommandKind.GoTo => $"走到点 {Point}",
        NavCommandKind.Chase => $"追实体 {EntityId} 目标 {EntityPosition}",
        NavCommandKind.AlongWaypoints => $"沿固定点走 {Waypoints.Count.ToString(CultureInfo.InvariantCulture)} 个航点 当前 {GoalFor(0)}",
        _ => $"守一片区域 中心 {Point} 半径 {AreaRadiusMeters.ToString(CultureInfo.InvariantCulture)}",
    };
}

/// <summary>
/// 一条命令 —— <b>目标描述 + 到达容差</b>。spec 归一后的那一个形状，五档命令全用它。
///
/// <para><b>到达容差是命令自己那份</b>，不是层的缺省：多宽算到了是这一局的尺度问题
/// （「接近到 2 米内就出手」的单位与「贴着才算到了」的单位差一个数量级），
/// 层里藏一个缺省会变成「为什么这个单位老是提前停」查不到出处的第二类原因。</para>
/// </summary>
public sealed record NavCommand
{
    /// <summary>目标描述（五档各有一份工厂）。</summary>
    public required NavTarget Target { get; init; }

    /// <summary>到达容差（米，非负）：距离 ≤ 容差即算到达。<b>0 是合法值</b>（原地不动那一档的缺省形状）。</summary>
    public required double ArriveToleranceMeters { get; init; }

    /// <summary>原地不动（驻守）。容差 0：被推开几厘米也要走回驻守点。</summary>
    public static NavCommand Hold(NavPoint standAt)
        => new() { Target = NavTarget.Hold(standAt), ArriveToleranceMeters = 0.0 };

    /// <summary>走到点。</summary>
    public static NavCommand GoTo(NavPoint point, double toleranceMeters)
        => new() { Target = NavTarget.GoTo(point), ArriveToleranceMeters = NavCheck.Tolerance(toleranceMeters) };

    /// <summary>追实体。目标坐标每步由宿主重读后重发（层里没有单位表，不缓存也不外推）。</summary>
    public static NavCommand Chase(string entityId, NavPoint position, double toleranceMeters)
        => new()
        {
            Target = NavTarget.Chase(entityId, position),
            ArriveToleranceMeters = NavCheck.Tolerance(toleranceMeters),
        };

    /// <summary>沿固定点走。</summary>
    public static NavCommand Along(IReadOnlyList<NavPoint> waypoints, double toleranceMeters)
        => new() { Target = NavTarget.Along(waypoints), ArriveToleranceMeters = NavCheck.Tolerance(toleranceMeters) };

    /// <summary>守一片区域。到达只看中心，半径交回作读数。</summary>
    public static NavCommand Guard(NavPoint center, double radiusMeters, double toleranceMeters)
        => new()
        {
            Target = NavTarget.Guard(center, radiusMeters),
            ArriveToleranceMeters = NavCheck.Tolerance(toleranceMeters),
        };

    /// <summary>这一条是五档里的哪一档；还没发令时是 <see cref="NavCommandKind.Unset"/>。</summary>
    public NavCommandKind Kind => Target.Kind;

    /// <summary>本步该朝哪个点走（走位的前视目标与到达判定都对着它）。</summary>
    public NavPoint GoalFor(int waypointCursor) => Target.GoalFor(waypointCursor);
}

/// <summary>
/// 到达判定 —— <b>整个模块只有这一处</b>。
///
/// <para>五个命令各自判一次到达，<c>StepToward</c> 就会在每个原型复活一次（「寻路单入口」）；
/// 而「到达」这个词必须是同一个意思，否则「它为什么停了」在跨命令比较时读不出来。</para>
///
/// <para>判据是「距离 ≤ 容差」，<b>含等号</b>：容差线上算到了，口径钉死是为了重放逐字
/// （「断言三层」 的 L3 那一档）。</para>
/// </summary>
public static class NavArrive
{
    /// <summary>站在 <paramref name="at"/> 算不算到了 <paramref name="goal"/>。</summary>
    /// <param name="at">当前位置（米）。</param>
    /// <param name="goal">本步的目标点（米）。</param>
    /// <param name="toleranceMeters">到达容差（米，非负有限数）。</param>
    public static bool Reached(NavPoint at, NavPoint goal, double toleranceMeters)
    {
        NavCheck.Tolerance(toleranceMeters);
        return at.DistanceTo(goal) <= toleranceMeters;
    }
}

/// <summary>命令空间这一档的入参校验，集中在这一处（口径同 <c>NavGridSpecCheck</c> / <c>NavSolveCheck</c>）。</summary>
internal static class NavCheck
{
    /// <summary>到达容差：非负、为有限数。0 是合法值（原地不动那一档）。</summary>
    public static double Tolerance(double value)
    {
        if (value < 0 || double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"到达容差必须是非负有限数，收到 {value.ToString(CultureInfo.InvariantCulture)} —— 容差由命令递进来，层里没有缺省；"
                + "兜 0 会让「原地不动」与「走到点」读出同一种站位（「桥只转发」）。");
        }

        return value;
    }

    /// <summary>半径 / 区域半径：正有限数。</summary>
    public static void Positive(double value, string name, string because)
    {
        if (value <= 0 || double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                name,
                value,
                $"收到 {value.ToString(CultureInfo.InvariantCulture)} —— {because}（「桥只转发」）。");
        }
    }

    /// <summary>移速那类「非负有限」的标量。0 是合法值（站桩单位：每步算「走多远」都得到 0）。</summary>
    public static void NonNegative(double value, string name, string what)
    {
        if (value < 0 || double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                name,
                value,
                $"{what}必须是非负有限数，收到 {value.ToString(CultureInfo.InvariantCulture)} —— "
                + "它由宿主递进这个模块，负数让「往哪儿走多远」没有读数（「桥只转发」）。");
        }
    }

    /// <summary>坐标：有限数。NaN 与无穷会让「距离 ≤ 容差」这一句恒为假或恒为真，两种都读不出真相。</summary>
    public static void Finite(double value, string name)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                name,
                value,
                $"坐标必须是有限数，收到 {value.ToString(CultureInfo.InvariantCulture)} —— 非有限坐标会让每一处距离判定都退化成一条恒值读数（「桥只转发」）。");
        }
    }

    /// <summary>名字 / id：非空。</summary>
    public static void Text(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentOutOfRangeException(
                name,
                value,
                "名字 / id 必填且不能是空白 —— 空 id 落进快照就是一行没有主体的记录，事后读不出是哪个单位（「桥只转发」）。");
        }
    }
}
