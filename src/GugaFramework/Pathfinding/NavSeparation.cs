#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DemoFactory.GugaFramework.Pathfinding;

/// <summary>
/// <b>agent 投影</b> —— 站位冲突这一档看得见的全部东西：id、一个标量组号、位置、身体半径、
/// 外加一个 <b>「这一趟算不算它」的标记</b>。
///
/// <para>投影就这五列，<b>没有别的</b>：除 id 与位置之外的一切单位属性（那份词表见寻路层文档 4.2）
/// 在寻路层里一个都不该出现 —— 不是「暂时没用到」，是这一层不持有那类知识（「寻路单入口」 后半句）。
/// 身体半径是<b>宿主递进来的标量</b>，层里没有单位表 —— 所以「两个单位该离多远」这个数只有宿主知道从哪来。</para>
///
/// <para>组号是<b>请求递进来的标量</b>，不是层的概念：只推同组的那几对（照
/// 退休前那份世界「同方两兵各推开一半」的形状），不同组之间不推 ——
/// 「谁和谁是一伙的」是宿主的知识，不是寻路网格的。</para>
///
/// <para><b>最后那一列为什么是「算不算它」而不是「它那一组是几」</b>：
/// 组号是登记时定死的（<see cref="NavAgentSpec.Group"/> 是 <c>init</c>），
/// 而宿主每一步都可能要让某个单位<b>不参与</b>这一趟 —— 退休前那处
/// <c>if (!a.Alive || !b.Alive) continue;</c> 就是这一类。
/// 让宿主去改组号只能绕（把它挪到一个只有它自己那一组，于是它谁也不推），
/// 而那一绕在读数上完全看不出来：组号是 1 还是 7 都说得通，
/// 于是「它为什么没被推开」在快照上答不出来。改成<b>一个名字说清了的标记</b>之后，
/// 这一列要么是「算」，要么是「不算」，没有第三种读法。</para>
///
/// <para>标记名里不许出现宿主的词（「阵亡」「被控」那一类）：这一列只说
/// <b>这一趟避让算不算它</b>，为什么不算由宿主自己知道（「禁自造术语」、「寻路单入口」 后半句）。</para>
/// </summary>
public sealed record NavAgentProjection
{
    /// <summary>单位 id（宿主递进来，原样交回；事件与账本里就是它）。</summary>
    public required string Id { get; init; }

    /// <summary>同组标识（标量）。<b>只推同组的</b>。缺省 0 = 全员同组。</summary>
    public required int Group { get; init; }

    /// <summary>这一步开始时的位置（米）。</summary>
    public required NavPoint Position { get; init; }

    /// <summary>身体半径（米，非负有限数）。0 是合法值（不占地方的探针单位）。</summary>
    public required double RadiusMeters { get; init; }

    /// <summary>
    /// 这一趟站位冲突<b>算不算它</b>（宿主每步递进来的标记）。
    /// 记成 false 的那一个：既不被别人推开，也不推别人 —— 两头都不参与，
    /// 顺序固定成<b>先看它算不算，再看组号</b>（判据 2 节那六条）。
    /// </summary>
    public required bool CountedInSeparation { get; init; }
}

/// <summary>
/// 站位冲突的档 —— 一个<b>阈值</b>，宿主递进来。
///
/// <para>「每步净位移低于多少算被推挤住了」是这一局的规模问题（跑档一步 1.5 米、爬档一步 0.12 米，
/// 同一个绝对值在两档上的意思完全不同），所以公共层一个缺省都不给（口径同 <see cref="NavSolveBudget"/>）。</para>
/// </summary>
public sealed record NavSeparationTuning
{
    /// <summary>
    /// 拥挤推挤那一档的阈值（米）：<b>避让叠加之后</b>的净位移低于它就算「被推挤住了」。
    /// 0 是合法值（净位移不到一步就判挤住 —— 那是「撞上去停住」那一局的读法）。
    /// </summary>
    public required double MinNetStepMeters { get; init; }
}

/// <summary>推开过的那一对 —— 站位冲突的读数按<b>配对</b>给，不按单位给。</summary>
/// <param name="FirstIndex">列表里靠前的那个下标（配对次序是 <c>i &lt; j</c> 走一遍列表，按列表序）。</param>
/// <param name="SecondIndex">列表里靠后的那个下标。</param>
/// <param name="FirstId">前者的 id。</param>
/// <param name="SecondId">后者的 id。</param>
/// <param name="OverlapMeters">重叠了多少米（= 最小间距 − 当前距离，各推开一半后正好清零）。</param>
/// <param name="Coincident">距离近乎零、沿第一根轴按序号一前一后拆开（不走方向）。</param>
/// <param name="HeldByBoundary">这一对里有一侧的候选落点出界，于是<b>那一半没挪</b>（边界即墙）。</param>
public readonly record struct NavSeparationPair(
    int FirstIndex,
    int SecondIndex,
    string FirstId,
    string SecondId,
    double OverlapMeters,
    bool Coincident,
    bool HeldByBoundary)
{
    /// <summary>逐字可读的一行（invariant，「double 与不变文化」）。</summary>
    public string Describe()
        => $"{FirstId}×{SecondId}"
           + $" 重叠 {OverlapMeters.ToString(CultureInfo.InvariantCulture)}"
           + (Coincident ? " 重合拆开" : " 沿连线各退一半")
           + (HeldByBoundary ? " 一侧撞界没挪" : string.Empty);
}

/// <summary>
/// 站位冲突这一趟的结果 —— <b>叠加修正之后</b>的位置、按配对序的读数、每个单位被谁推的。
///
/// <para><b>寻路网格一个格都没动</b>（spec「现有六种阻挡的归类」：网格是采样场，
/// 成对相对查询是解析量，把两个单位互相推开翻译成网格是错位）。守卫 ④
/// （<see cref="NavSourceGuard.AgentInGridRule"/>）钉的就是这一条，
/// 用例另有「推完之后整张网格逐格读数不变」那一条运行时对读。</para>
/// </summary>
public sealed record NavSeparationResult
{
    /// <summary>推开之后每个单位的位置，<b>按请求的列表序</b>（下标与请求一一对应）。</summary>
    public required IReadOnlyList<NavPoint> Positions { get; init; }

    /// <summary>推开过的那几对，<b>按配对序</b>（<c>i &lt; j</c> 走一遍列表）。结果逐字固定靠的就是这个次序。</summary>
    public required IReadOnlyList<NavSeparationPair> Pairs { get; init; }

    /// <summary>
    /// 每个单位<b>这一步被谁推的</b>（没被推是 null）。取<b>配对序里第一对</b>推它的那个 ——
    /// 「卡在谁身上」要的正是那一个 id，而取哪一对钉死成「第一对」读数才可重放。
    /// </summary>
    public required IReadOnlyList<string?> PushedBy { get; init; }

    /// <summary>推开了几对。</summary>
    public int PairCount => Pairs.Count;

    /// <summary>逐字可读（invariant，「double 与不变文化」）。一行一对。</summary>
    public string Describe()
    {
        var sb = new StringBuilder();
        sb.Append("站位冲突 对 ")
            .Append(PairCount.ToString(CultureInfo.InvariantCulture))
            .Append(" 位置 ").Append(Positions.Count.ToString(CultureInfo.InvariantCulture))
            .Append(" 个\n");
        for (var i = 0; i < Pairs.Count; i++)
        {
            sb.Append("  #").Append((i + 1).ToString(CultureInfo.InvariantCulture))
                .Append(' ').Append(Pairs[i].Describe())
                .Append('\n');
        }

        for (var i = 0; i < Positions.Count; i++)
        {
            sb.Append("  ").Append(i.ToString(CultureInfo.InvariantCulture))
                .Append(" (").Append(Positions[i].X.ToString(CultureInfo.InvariantCulture))
                .Append(", ").Append(Positions[i].Z.ToString(CultureInfo.InvariantCulture))
                .Append(") 被 ").Append(PushedBy[i] ?? "无")
                .Append('\n');
        }

        return sb.ToString();
    }
}

/// <summary>
/// 站位冲突 —— <b>叠加修正</b>，叠在前视式跟随之上，<b>不进网格</b>。
///
/// <para><b>为什么是叠加而不是地形</b>（spec「现有六种阻挡的归类」）：网格是<b>采样场</b>，
/// 成对相对查询是<b>解析量</b>。把「两个单位互相推开」翻译成网格，等于把一件随局面变的相对量
/// 冻成一张每帧都要重建的静态表；而静态表一动就 +1 静态版本，于是「手在抖一下」与
/// 「有人在砌墙」在缓存作废那条路上长得一模一样 —— 那是查不出来源的版本号抖动。
/// 所以这一档只在<b>位置</b>上做修正，网格逐格读数一个都不变。</para>
///
/// <para><b>次序即语义</b>（照退休前那套，逐字照搬）：
///
/// <list type="number">
/// <item>配对是 <c>i &lt; j</c> 走一遍列表，<b>按列表序</b>；</item>
/// <item><b>先看这一趟算不算它</b>（<see cref="NavAgentProjection.CountedInSeparation"/>）：
/// 有一侧记成不算，这一对直接跳过（<b>两头都不参与</b>）；</item>
/// <item>再看<b>同组</b>（今天那一档写的是同方 —— 层里不许出现那个词，组号由请求递进来）；</item>
/// <item>最小间距 = 两者身体半径之和；</item>
/// <item>重叠量<b>一人一半</b>：<c>push = (最小间距 − 距离) × 0.5</c>；</item>
/// <item>方向 = <c>delta / 距离</c>；</item>
/// <item><b>一对推完立刻写回</b>，后面的配对看到的是已推过的位置（不是先算完再统一落）——
/// 于是同一份输入逐字可重放，而「第 3 对看到的是原始位置还是前两对推完的位置」这件事
/// 在读数上分得开。</item>
/// </list>
///
/// <para><b>落不成那一半不挪</b>：候选落点站不住就不挪，判据是
/// <see cref="NavGrid.CanStandAt"/>（净空保守下界，界外一律 false）。
/// <b>越界与落进阻挡格都算站不住</b> —— 只判越界的话推挤会把人推进建筑里，
/// 而人在阻挡格里移动解永远找不到出路，于是原地卡死到局末。
/// 最终口径取「落不成」（用户确认）：层里<b>不另写一份矩形夹取</b>，能不能站只认网格这一个真值。
/// 宿主侧那份竞技场矩形（<c>BattleContract.ClampToArena</c>）<b>没有退休</b> ——
/// 它现在只在摆兵入场那一处的抖动里用着。</para>
///
/// <para><b>纯函数</b>：进去一份投影列表，出来一份新位置与读数，<b>不改调用方的对象</b>
/// （写回是流水线的活，因为位置那一份真值住在流水线上）。</para>
/// </summary>
public static class NavSeparation
{
    /// <summary>
    /// 距离低到多少算「完全重合」：<b>沿第一根轴按序号一前一后拆开，不走方向</b>
    /// （照退休前那份同侧推开里的 <c>1e-6</c>）。
    ///
    /// <para>它是<b>写死的常量，不是入参</b>，理由与 <see cref="NavSubstepper.CellFraction"/> 同款：
    /// 这一支存在的理由是「delta 归一化会除以 0」，1e-6 是浮点噪声那一档的门限，
    /// 递进来就变成一条「多小的距离算重合」的调项，而它与这一局的规模无关。</para>
    /// </summary>
    public const double CoincidentEpsilonMeters = 0.000001;

    /// <summary>
    /// 推一遍。<b>一个单位表也没有</b>：身体半径与组号都在请求里（「寻路单入口」 后半句）。
    /// </summary>
    /// <param name="grid">这一片寻路网格。<b>只读它</b>：这一档不写任何一格（判红由守卫 ④ 与那条逐格对读钉）。</param>
    /// <param name="agents">agent 投影，<b>列表序就是配对序</b>。</param>
    /// <exception cref="ArgumentOutOfRangeException">id 空白、半径为负或非有限、坐标非有限。</exception>
    public static NavSeparationResult Resolve(NavGrid grid, IReadOnlyList<NavAgentProjection> agents)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(agents);

        var count = agents.Count;
        var positions = new NavPoint[count];
        for (var i = 0; i < count; i++)
        {
            var a = agents[i];
            ArgumentNullException.ThrowIfNull(a);
            NavCheck.Text(a.Id, nameof(agents));
            NavSolveCheck.Radius(a.RadiusMeters, nameof(agents));
            NavCheck.Finite(a.Position.X, "agent 投影的 X");
            NavCheck.Finite(a.Position.Z, "agent 投影的 Z");
            positions[i] = a.Position;
        }

        var pairs = new List<NavSeparationPair>();
        var pushedBy = new string?[count];

        for (var i = 0; i < count; i++)
        {
            for (var j = i + 1; j < count; j++)
            {
                if (!agents[i].CountedInSeparation || !agents[j].CountedInSeparation)
                {
                    continue;   // 记成「这一趟不算它」的那一侧两头都不参与：不被推，也不推
                }

                if (agents[i].Group != agents[j].Group)
                {
                    continue;   // 只推同组的：不同组之间不推（组号是宿主递进来的知识）
                }

                var minDistance = agents[i].RadiusMeters + agents[j].RadiusMeters;
                var dx = positions[j].X - positions[i].X;
                var dz = positions[j].Z - positions[i].Z;
                var distance = Math.Sqrt((dx * dx) + (dz * dz));
                if (distance >= minDistance)
                {
                    continue;   // 不重叠就一个数都不动 —— 叠加修正在没重叠时必须是零读数
                }

                var overlap = minDistance - distance;
                var coincident = distance < CoincidentEpsilonMeters;
                NavPoint nextFirst;
                NavPoint nextSecond;
                if (coincident)
                {
                    // 完全重合：沿第一根轴（X）按序号一前一后拆开，不走方向 —— 方向此刻是 0/0。
                    var half = minDistance * 0.5;
                    nextFirst = new NavPoint(positions[i].X - half, positions[i].Z);
                    nextSecond = new NavPoint(positions[j].X + half, positions[j].Z);
                }
                else
                {
                    var push = overlap * 0.5;
                    var ux = dx / distance;
                    var uz = dz / distance;
                    nextFirst = new NavPoint(positions[i].X - (ux * push), positions[i].Z - (uz * push));
                    nextSecond = new NavPoint(positions[j].X + (ux * push), positions[j].Z + (uz * push));
                }

                // 一对推完立刻写回：后面的配对看到的是已推过的位置。
                //
                // **判据是「那个落点站不站得住」，不是「那个落点有没有出网格」**：
                // 越界与落进阻挡格一律不挪，层里不另写一份矩形夹取。
                //
                // <para><b>为什么不能只判 Contains</b>（2026-10-15 修的真 bug）：
                // <c>Contains</c> 只答「点有没有出网格边界」，<b>建筑内部那一格也是 Contains 的</b>。
                // 于是几十个单位朝同一个落点挤时，推挤会把人<b>推进墙里</b>；
                // 人一旦站在阻挡格里，移动解找不到任何出得去的路，寻路层从此
                // 每一步都回 <c>NoTerrainRoute</c> —— 单位在原地卡死到局末，
                // 宿主读到的却是「搬货那一档走了 12 小时没到」。
                // 现场（BeastArena 甲板）：一名搬运工被挤进宿舍那两格，
                // <c>Stuck=NoTerrainRoute/7462</c>，那一档的入库单永远交不出去。</para>
                //
                // <para><b>跟移动解同一口径</b>：<see cref="NavStepper"/> 判一步能不能走
                // 走的是同一个 <see cref="NavGrid.CanStandAt"/>（净空保守下界）。
                // 两处口径必须一致 —— 移动解说「这里能站」而推挤说「这里不能」的话，
                // 下一个单位会被推到一个移动解自己也走不出去的地方。</para>
                var held = false;
                if (grid.CanStandAt(nextFirst, agents[i].RadiusMeters))
                {
                    positions[i] = nextFirst;
                }
                else
                {
                    held = true;
                }

                if (grid.CanStandAt(nextSecond, agents[j].RadiusMeters))
                {
                    positions[j] = nextSecond;
                }
                else
                {
                    held = true;
                }

                if (pushedBy[i] is null)
                {
                    pushedBy[i] = agents[j].Id;
                }

                if (pushedBy[j] is null)
                {
                    pushedBy[j] = agents[i].Id;
                }

                pairs.Add(new NavSeparationPair(i, j, agents[i].Id, agents[j].Id, overlap, coincident, held));
            }
        }

        return new NavSeparationResult
        {
            Positions = positions,
            Pairs = pairs,
            PushedBy = pushedBy,
        };
    }
}
