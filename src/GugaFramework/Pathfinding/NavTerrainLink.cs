#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using DemoFactory.GugaFramework.Construction;

namespace DemoFactory.GugaFramework.Pathfinding;

// 建造占用 → 寻路网格的自动接线（spec「地形来源接线」第一条：<b>漏了这一类等于墙不存在</b>）。
//
// 分工照 ADR 0009 那一套：<b>换算归宿主</b>。这里收一个 `Func<GridCell, NavCell>`
//（建造格 → 寻路格），层里不持有建造规则、不知道蓝图的占地怎么算 ——
// 那两样是建造模块的私产，寻路层要是自己重算一遍占地，两处就会各有一份「这座占哪几格」。
// 自由放置那一侧没有建造格：层里也不认识圆形占地，所以圆 → 格的光栅化同样归宿主
//（`Func<string, IReadOnlyList<NavCell>>`，见 NavTerrainLink 的构造参数）。
//
// 写路径只有一条：落印子走 NavTerrain.Apply，与注入 StampTerrain 是同一次 NavGrid.SetBlocked
// （「桥只转发」：注入的路径与玩家的路径不许漂成两回事）。

/// <summary>一枚落进寻路网格的地形印子。<b>读数</b>：谁落下的、落哪一格、封了还是解封。</summary>
/// <param name="Source">哪一类事件落的（<c>placed</c> / <c>registered</c> / <c>completed</c> / <c>rotated</c> / <c>moved</c> / <c>removed</c>）。</param>
/// <param name="BuildingId">动的是哪一栋（<c>removed</c> 那一枚是撤掉它占的那几格）。</param>
/// <param name="Cell">寻路网格里的那一格。</param>
/// <param name="Blocked">封（true）还是解封（false）。</param>
public readonly record struct NavTerrainStamp(string Source, string BuildingId, NavCell Cell, bool Blocked)
{
    /// <summary>逐字可读的一行（invariant，「double 与不变文化」）。</summary>
    public override string ToString()
        => $"{Source} {BuildingId} cell={Cell} blocked={(Blocked ? 1 : 0)}";
}

/// <summary>
/// 订阅建造层的事件出口，把建成 / 移除自己落成地形印子。
///
/// <para><b>为什么是「拉」而不是「推」</b>：建造层那个出口是一本<b>有界环形账本</b>
/// （<see cref="ConstructionEventLog"/>），它没有回调、没有订阅口 —— 读出口只有
/// <c>Total</c> / <c>KeptCount</c> / <c>Tail(n)</c> 三个。所以接线是<b>每步推进之前调一次
/// <see cref="Pump"/></b>，次序写在类注释里那条上：先落地形再推逻辑步，
/// 否则「这一发砌的墙」要到下一步才生效，绕障的读数就与建造那一步对不上。</para>
///
/// <para><b>环形挤掉的条数是读数的一部分</b>（「三条铁律」 ③：「账本里没有」≠「没发生」）：
/// 两次 Pump 之间发的事件超过账本容量，最老的那几条就再也读不到了 ——
/// 它们计入 <see cref="Missed"/>，不静默少落几枚印子。</para>
///
/// <para><b>它记得自己落过哪几格</b>：<see cref="BuildingRemoved"/> 只带建筑 id、不带格名单，
/// 所以撤占用只能靠这份记忆（<see cref="CellsOf"/>）。挪动与转方向那两条不带<b>旧</b>格名单，
/// 于是挪动要宿主递一个「这栋现在占哪几格」的查法（<c>cellsOf</c>，可省）——
/// 不递的话挪动那一条计入 <see cref="Missed"/>，而不是留一堵撤不掉的墙。</para>
///
/// <para><b>自由放置的那三条自带空表</b>：圆形占地没有建造格，建造层给的是圆
/// （<see cref="PlacementVerdict.Circle"/>）。所以宿主还要递一个「这栋在寻路网格里占哪几格」的
/// 查法（<c>navCellsOf</c>，可省）：放下 / 登记 / 转方向这三条事件自带的 <c>Cells</c>
/// 是空表时走它。圆 → 格的光栅化写在宿主侧（ADR 0009「换算归宿主」），
/// 层里不认识圆形占地，也不认识建造规则。</para>
/// </summary>
public sealed class NavTerrainLink
{
    private readonly NavGrid _grid;
    private readonly Func<GridCell, NavCell> _mapCell;
    private readonly Func<string, IReadOnlyList<GridCell>>? _cellsOf;
    private readonly Func<string, IReadOnlyList<NavCell>>? _navCellsOf;
    private readonly Dictionary<string, List<NavCell>> _stamped = new(StringComparer.Ordinal);

    private ConstructionEventLog? _events;
    private long _cursor;
    private long _missed;
    private long _stamps;
    private NavTerrainStamp[] _last = [];

    /// <summary>
    /// 造一条接线。
    /// </summary>
    /// <param name="grid">落地形的那张网格。</param>
    /// <param name="mapCell">建造格 → 寻路格的换算（<b>由宿主递</b>，同 ADR 0009「换算归宿主」）。</param>
    /// <param name="cellsOf">「这栋现在占哪几格」的查法（建造格），挪动那一条事件要用（可省；不递则挪动计入 <see cref="Missed"/>）。</param>
    /// <param name="navCellsOf">「这栋在寻路网格里占哪几格」的查法（寻路格），自由放置那三条的格名单是空表时走它（可省；不递则旧行为照旧）。</param>
    public NavTerrainLink(NavGrid grid, Func<GridCell, NavCell> mapCell,
        Func<string, IReadOnlyList<GridCell>>? cellsOf = null,
        Func<string, IReadOnlyList<NavCell>>? navCellsOf = null)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(mapCell, "建造格 → 寻路格的换算必填：层里不持有建造规则，不知道这座占哪几格（ADR 0009 换算归宿主）。");
        _grid = grid;
        _mapCell = mapCell;
        _cellsOf = cellsOf;
        _navCellsOf = navCellsOf;
    }

    /// <summary>接上建造层那个事件出口。<b>接上那一刻的游标记在当下</b>：
    /// 之前发过的事件不回放一遍（接的时候世界已经在那儿了，回放一遍等于把已有的墙再砌一次）。</summary>
    public void Attach(ConstructionEventLog events)
    {
        ArgumentNullException.ThrowIfNull(events);
        _events = events;
        _cursor = events.Total;
    }

    /// <summary>不再读那个出口（换局 / 停机时调一次，「离树交还」：主动交还，不在调用点每次查）。</summary>
    public void Detach() => _events = null;

    /// <summary>读出上一次 <see cref="Pump"/> 落的印子（读数口：一次发了什么，宿主一发读出来）。</summary>
    public IReadOnlyList<NavTerrainStamp> LastPump => _last;

    /// <summary>累计落了多少枚印子（值没变时 <see cref="NavGrid.SetBlocked"/> 自己是空操作，版本号也不动）。</summary>
    public long StampedCount => _stamps;

    /// <summary>
    /// 有多少条建造事件<b>没能落成地形</b>：两次 Pump 之间被环形账本顶掉的、被
    /// <c>ClearKept</c> 清掉的、或落不成的那几条（挪动没递查法、建成那栋记忆表里没有）。
    /// 这一列不为 0 就说明「有一类地形改动没进网格」，而症状与「没砌墙」完全一样。
    /// </summary>
    public long Missed => _missed;

    /// <summary>这栋建筑在寻路侧占了哪几格（<b>读数</b>：查「那堵墙还在不在」）。没落过印子回空表。</summary>
    public IReadOnlyList<NavCell> CellsOf(string buildingId)
        => _stamped.TryGetValue(buildingId, out var cells) ? cells : (IReadOnlyList<NavCell>)Array.Empty<NavCell>();

    /// <summary>
    /// 读自上次之后新发的那几条建造事件，把能落的落成地形印子。返回这一趟落的印子。
    ///
    /// <para><b>次序是契约</b>：每一步都要在推进逻辑步<b>之前</b>调它 ——
    /// 反过来的话「这一发砌的墙」要到下一步才生效，绕障的读数与建造那一步对不上。</para>
    /// </summary>
    public IReadOnlyList<NavTerrainStamp> Pump()
    {
        var events = _events;
        if (events is null)
        {
            _last = [];
            return _last;
        }

        var total = events.Total;
        var pending = total - _cursor;
        _cursor = total;
        if (pending <= 0)
        {
            _last = [];
            return _last;
        }

        // 账本是有界的：pending 超过还留着的那几条时，多出来的那几条读不到了
        // （被环形顶掉，或被 ClearKept 清掉）—— 计入 Missed，不静默少落。
        var kept = events.KeptCount;
        if (pending > kept)
        {
            _missed += pending - kept;
            pending = kept;
        }

        var tail = events.Tail((int)pending);
        var stamps = new List<NavTerrainStamp>(tail.Count);
        for (var i = 0; i < tail.Count; i++)
        {
            Apply(tail[i], stamps);
        }

        _last = stamps.ToArray();
        return _last;
    }

    private void Apply(ConstructionEventPayload e, List<NavTerrainStamp> stamps)
    {
        switch (e)
        {
            case BuildingPlaced placed:
                StampOrLookup(placed.Subject, placed.Cells, "placed", stamps);
                break;
            case BuildingRegistered registered:
                StampOrLookup(registered.Subject, registered.Cells, "registered", stamps);
                break;
            case BuildingCompleted completed:
                // 「建成」这一发不带格名单，靠自己落印子时记下的那几格重落一遍。
                // 值没变时 SetBlocked 是空操作（静态版本不动），所以重落不产生版本抖动。
                // 记忆表里没有这一栋才计一笔：查法答过「不占格」的那一栋不算漏进。
                if (!Restamp(completed.Subject, "completed", stamps))
                {
                    _missed++;
                }

                break;
            case BuildingRotated rotated:
                Unstamp(rotated.Subject, "rotated", stamps);
                StampOrLookup(rotated.Subject, rotated.Cells, "rotated", stamps);
                break;
            case BuildingMoved moved:
                Unstamp(moved.Subject, "moved", stamps);
                if (_cellsOf is null || !StampLookup(moved.Subject, "moved", stamps))
                {
                    _missed++;
                }

                break;
            case BuildingRemoved removed:
                Unstamp(removed.Subject, "removed", stamps);
                break;
            default:
                // 其余建造事件（拒绝 / 进度 / 血量 / 暂停 / 汇总）不落地形：
                // 拦停与进度不占格，血量与暂停更不占 —— 把它们也当占位等于凭空多一堵墙。
                break;
        }
    }

    private void Stamp(string buildingId, IReadOnlyList<GridCell> cells, bool blocked, string source,
        List<NavTerrainStamp> stamps)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = _mapCell(cells[i]);
            NavTerrain.Apply(_grid, cell, NavTerrainLayer.Blocked, blocked ? 1.0 : 0.0);
            stamps.Add(new NavTerrainStamp(source, buildingId, cell, blocked));
            _stamps++;
        }

        var mine = Cells(buildingId);
        mine.Clear();
        for (var i = 0; i < cells.Count; i++)
        {
            mine.Add(_mapCell(cells[i]));
        }
    }

    /// <summary>「建成」那一条照记忆表重落一遍。返回假 = 这一条没进网格。</summary>
    /// <remarks>
    /// <para><b>记忆表把三种事实分开，这一条按三种事实分三档</b>：
    /// ① 表里有这一栋、格名单非空 —— 重落一遍，不计数；
    /// ② 表里有这一栋、格名单为空 —— 宿主的查法答过「这一栋不占格」，落不下也不计数；
    /// ③ 表里没有这一栋 —— 查法没递，或它从没落过印子，计一笔 <see cref="Missed"/>。</para>
    /// <para>②与③是两种事实：查法回空表是一句明确的答案，不是漏进。
    /// <see cref="Missed"/> 记的是「有一类地形改动没进网格」—— 空表这一档答过了，不进这一列。</para>
    /// </remarks>
    private bool Restamp(string buildingId, string source, List<NavTerrainStamp> stamps)
    {
        if (!_stamped.TryGetValue(buildingId, out var cells))
        {
            return false;
        }

        if (cells.Count == 0)
        {
            return true;
        }

        for (var i = 0; i < cells.Count; i++)
        {
            NavTerrain.Apply(_grid, cells[i], NavTerrainLayer.Blocked, 1.0);
            stamps.Add(new NavTerrainStamp(source, buildingId, cells[i], true));
            _stamps++;
        }

        return true;
    }

    private bool StampLookup(string buildingId, string source, List<NavTerrainStamp> stamps)
    {
        if (_cellsOf is null)
        {
            return false;
        }

        var cells = _cellsOf(buildingId);
        Stamp(buildingId, cells, blocked: true, source, stamps);
        return true;
    }

    /// <summary>事件自带格名单就按它落，空表改走宿主递的寻路格查法。</summary>
    /// <remarks>自由放置那三条（放下 / 登记 / 转方向）自带的格名单恒为空表。
    /// 查法没递时什么都不落 —— 旧行为照旧，<see cref="Missed"/> 也不多记一笔。</remarks>
    private void StampOrLookup(string buildingId, IReadOnlyList<GridCell> cells, string source,
        List<NavTerrainStamp> stamps)
    {
        if (cells.Count > 0)
        {
            Stamp(buildingId, cells, blocked: true, source, stamps);
            return;
        }

        StampNavLookup(buildingId, source, stamps);
    }

    /// <summary>按宿主递的寻路格名单落阻挡（自由放置那一侧）。</summary>
    /// <remarks>落下的那几格进记忆表，移除与建成两条照旧靠它。查法返回空表 = 这栋不占格。</remarks>
    private void StampNavLookup(string buildingId, string source, List<NavTerrainStamp> stamps)
    {
        if (_navCellsOf is null)
        {
            return;
        }

        var cells = _navCellsOf(buildingId);
        for (var i = 0; i < cells.Count; i++)
        {
            NavTerrain.Apply(_grid, cells[i], NavTerrainLayer.Blocked, 1.0);
            stamps.Add(new NavTerrainStamp(source, buildingId, cells[i], true));
            _stamps++;
        }

        var mine = Cells(buildingId);
        mine.Clear();
        mine.AddRange(cells);
    }

    private void Unstamp(string buildingId, string source, List<NavTerrainStamp> stamps)
    {
        if (!_stamped.Remove(buildingId, out var cells))
        {
            return;
        }

        for (var i = 0; i < cells.Count; i++)
        {
            NavTerrain.Apply(_grid, cells[i], NavTerrainLayer.Blocked, 0.0);
            stamps.Add(new NavTerrainStamp(source, buildingId, cells[i], false));
            _stamps++;
        }
    }

    private List<NavCell> Cells(string buildingId)
    {
        if (!_stamped.TryGetValue(buildingId, out var cells))
        {
            cells = [];
            _stamped.Add(buildingId, cells);
        }

        return cells;
    }

    /// <inheritdoc />
    public override string ToString()
        => $"NavTerrainLink(空间={_grid.Space} 已落={StampedCount.ToString(CultureInfo.InvariantCulture)} 落不下={Missed.ToString(CultureInfo.InvariantCulture)})";
}
