#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using DemoFactory.GugaFramework.Construction;
using DemoFactory.GugaFramework.Pathfinding;
using DemoFactory.GugaFramework.Scheduling;

namespace DemoFactory.GugaFramework.Composition;

// 世界装配 —— 把「一个逻辑世界要装哪几件、按什么次序装、每步按什么次序推」收在一处。
//
// 收编的是什么：走位流水线、建造模块、地形接线这三段构造与「每步先落地形、再推时钟、再推建造」
// 这三件事，在三处宿主里各抄一份，逐字相同的那三段已经漂成三种写法。
//
// 本件存在的理由是「不静默建半套」：层数对不齐的时候当场抛，并逐条说出缺哪一层。
// 静默建半套的症状是世界演化结果不对，要等结果不对才反查得到装配。

/// <summary>
/// 一个逻辑世界的装配 —— 走位流水线、建造模块、地形接线三件，收在一条五步的次序上。
///
/// <para><b>五步的次序</b>：① 构造函数（只认时钟）→ ② 建走位流水线 → ③ 建建造模块 → ④ 接地形接线 → ⑤ 推进。</para>
///
/// <para><b>每步推进的次序是契约</b>：先 <see cref="NavTerrainLink.Pump"/> 落地形，再 <c>Clock.Advance(1)</c>
/// 推逻辑步，再 <c>Construction.Advance(1)</c> 推建造进度，最后是宿主自己的收尾。
/// 反过来的话「这一发砌的墙」要到下一步才生效，绕障的读数与建造那一步对不上。
/// 建造进度排在时钟之后：这一步里到头的工期带的是这一步的步号。
/// 暂停那一格<b>只判一次</b>（<see cref="Advance"/> 开头那一次）—— 时钟在暂停中回 false 且不动，
/// 模块跟着不动，时间冻结对两边是同一件事。</para>
///
/// <para><b>网格为什么由本件存一份</b>：<see cref="NavStepper"/> 不公开网格属性（只有
/// <c>NavSolver.Space.Grid</c>），而 <see cref="AttachTerrain"/> 不收网格参数。
/// 所以那张网格只能从 <see cref="NavPipelineSpec.Grid"/> 存下来 —— 这是最容易踩空的一处。</para>
///
/// <para><b>第 ② 步按空间登记，不是一个世界一条</b>：<see cref="BuildNavPipeline"/> 以
/// <c>spec.Space</c> 为键，一个世界同时要盯两套相对坐标系（甲板与大地图）时各建一条。
/// <see cref="Grid"/> 与 <see cref="Stepper"/> 两个属性仍给<b>第一个</b>登记进来的空间 ——
/// 地形接线那一侧读的是它们，第几条注册不会把那两个属性的指向换掉。
/// 另两个查询口 <see cref="StepperOf"/> / <see cref="GridOf"/> 按空间名回本尊。</para>
///
/// <para><b>多出来的空间不用本件推</b>：<see cref="NavStepper"/> 在构造那一刻就订上同一个
/// <see cref="GameClock"/> 的脉搏，而 <see cref="Advance"/> 推的就是那个时钟 ——
/// 登记了几条就有几条跟着走，<see cref="Advance"/> 里没有逐条那一段。</para>
///
/// <para><b>本件不替宿主排次序</b>：三处宿主各有自己特有的次序依赖（决策段先订、地形接线必须早于
/// 种子建筑放置、某些层必须先于走位那一档），那些不进这一层。</para>
/// </summary>
public sealed class WorldAssembly
{
    private readonly GameClock _clock;

    /// <summary>已登记的走位流水线，按 <c>spec.Space</c> 查（第一个登记的另存一份给旧那两个属性）。</summary>
    private readonly Dictionary<SpaceId, SpacePipeline> _pipelines = new();

    private SpacePipeline? _first;
    private ConstructionCore? _construction;
    private NavTerrainLink? _terrain;
    private bool _terrainDetached;

    /// <summary>成功推过至少一步 —— 守卫用。回 false 与抛异常的那三种都<b>不算</b>「之后」。</summary>
    private bool _advanced;

    /// <summary>第 ① 步：只认时钟并存下来，不造任何层。</summary>
    public WorldAssembly(GameClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
    }

    /// <summary>这个装配挂着的那个逻辑时钟。</summary>
    public GameClock Clock => _clock;

    /// <summary>
    /// 时间轴是否已被冻住（局结束）。<b>缺省 = 永不冻。</b>
    ///
    /// <para><b>冻结判在这一层，不在宿主那一层</b>：三处宿主推进都从 <see cref="Advance"/> 过
    /// （<c>AdvanceTo</c> 也是调它），把闸开在这里就不用逐个原型各判一次 ——
    /// 有的世界会重写 <c>Advance</c> 并直接调本件，那条路若在宿主层判就绕过去了。
    /// 真值本身住在 <c>GameLogicBase.TimeFrozen</c>，本件只读那个谓词。</para>
    ///
    /// <para><b>冻结与暂停是两件事</b>：暂停可解冻（玩家按一下继续），冻结是这一局的结局。
    /// 两者对推进的效果一样（一步都不走），所以在本件里并排判，但真值分开放。</para>
    /// </summary>
    public Func<bool>? FrozenProbe { get; set; }

    /// <summary>第 ② 步建好的<b>第一个</b>空间那张网格（还没建就是空；后面登记的空间走 <see cref="GridOf"/>）。</summary>
    public NavGrid? Grid => _first?.Grid;

    /// <summary>第 ② 步建好的<b>第一个</b>空间那条走位流水线（还没建就是空；后面登记的空间走 <see cref="StepperOf"/>）。</summary>
    public NavStepper? Stepper => _first?.Stepper;

    /// <summary>第 ③ 步建好的建造模块（还没建就是空）。</summary>
    public ConstructionCore? Construction => _construction;

    /// <summary>第 ④ 步接好的地形接线（还没接就是空）。</summary>
    public NavTerrainLink? Terrain => _terrain;

    /// <summary>
    /// 第 ② 步：建走位流水线（求解器 + 步进器），以 <paramref name="spec"/> 的空间名为键登记，
    /// 并把那张网格存下来给地形接线用。<b>第一个登记的空间</b>那条同时赋给 <see cref="Grid"/> 与
    /// <see cref="Stepper"/>；别的空间走 <see cref="StepperOf"/> / <see cref="GridOf"/>。
    ///
    /// <para><b>登记几条就都跟着时钟走</b>：步进器在构造那一刻就订上本装配那个时钟的脉搏，
    /// 而 <see cref="Advance"/> 推的就是那个时钟，所以第二个空间不用在这里再接一次。</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// 已经推过步（晚了），或 <paramref name="spec"/> 那个空间已经登记过一条。
    /// </exception>
    public NavStepper BuildNavPipeline(NavPipelineSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (_advanced)
        {
            throw new InvalidOperationException(
                $"走位流水线不能晚建：已经推到第 {_clock.Tick.ToString(CultureInfo.InvariantCulture)} 步了，"
                + "位置与朝向那些读数这一局都对不上 —— 造它的时候世界已经走远了。");
        }

        if (_pipelines.ContainsKey(spec.Space))
        {
            throw new InvalidOperationException(
                $"一个世界只有一条走位流水线，是每一个空间各一条：{spec.Space} 这一个已经建过 —— "
                + "位置与朝向的真值只住流水线这一份，同一个空间建两条就是两份真值，"
                + "而查询口与网格都只认登记表里那一条。");
        }

        var solver = new NavSolver(
            new NavSpace(spec.Space, spec.Grid),
            spec.Budget,
            spec.Recompute,
            spec.CacheCapacity,
            spec.RecordCapacity,
            spec.TrackedRequestCapacity);
        var stepper = new NavStepper(solver, spec.Move, _clock, spec.Gate, spec.Separation, spec.Events);
        var pipeline = new SpacePipeline(spec.Grid, stepper);
        _pipelines.Add(spec.Space, pipeline);
        _first ??= pipeline;
        return stepper;
    }

    /// <summary>取 <paramref name="space"/> 那个空间上登记的那条走位流水线。</summary>
    /// <exception cref="KeyNotFoundException">那个空间上没有登记过流水线（消息列出已登记的那几个）。</exception>
    public NavStepper StepperOf(SpaceId space) => PipelineOf(space).Stepper;

    /// <summary>取 <paramref name="space"/> 那个空间上登记的那张网格。</summary>
    /// <exception cref="KeyNotFoundException">那个空间上没有登记过流水线（消息列出已登记的那几个）。</exception>
    public NavGrid GridOf(SpaceId space) => PipelineOf(space).Grid;

    private SpacePipeline PipelineOf(SpaceId space)
    {
        if (!_pipelines.TryGetValue(space, out var found))
        {
            throw new KeyNotFoundException(
                $"{space} 这个空间上没有登记过走位流水线：已登记的是 {Registered()} —— "
                + "查一个没建过的空间，拿到的那条要么是空、要么是另一个坐标系里的走位。");
        }

        return found;
    }

    /// <summary>已登记的空间名单，供报错文案当场点名（字典枚举序，顺序本身无意义）。</summary>
    private string Registered()
        => _pipelines.Count == 0 ? "（一个都没有）" : string.Join("、", _pipelines.Keys);

    /// <summary>
    /// 第 ③ 步：建建造模块，步号读数接在这个装配的时钟上。
    ///
    /// <para><b>归零去向那一个槽原样递给模块</b>（<paramref name="removePolicy"/>）：
    /// 这一件是装配，不是规则的第二份，所以它不替原型答，只递过去。没递 = 模块那一档
    /// （归零即出册），与没带这个参数进来之前逐字相同。</para>
    /// </summary>
    /// <param name="rule">放置规则。</param>
    /// <param name="blueprints">蓝图表，一次递入。</param>
    /// <param name="removePolicy">归零之后留不留册；<b>没递 = 归零即出册</b>。</param>
    /// <exception cref="InvalidOperationException">已经建过一个。</exception>
    public ConstructionCore BuildConstruction(
        IPlacementRule rule,
        IEnumerable<Blueprint> blueprints,
        Func<Building, RemovalCause, bool>? removePolicy = null)
    {
        if (_construction is not null)
        {
            throw new InvalidOperationException(
                "建造模块已经建过一个：两个模块就是两本册，一座建筑在两本册上都查得到，而移除只走其中一本。");
        }

        _construction = new ConstructionCore(rule, blueprints, () => _clock.Tick,
            removePolicy: removePolicy);
        return _construction;
    }

    /// <summary>
    /// 第 ④ 步：造地形接线并接在建造模块那个事件出口上。圆形占地那一侧要递
    /// <paramref name="navCellsOf"/>（圆 → 格的光栅化归宿主，层里不认识圆形占地）。
    ///
    /// <para><b>「这栋现在占哪几格」的查法缺省接在建造模块上</b>：<paramref name="cellsOf"/> 没递时
    /// 绑 <c>id =&gt; Find(id)?.Cells ?? []</c>，于是 <see cref="ConstructionCore.Move"/> 发的那一发
    /// <see cref="BuildingMoved"/> 会把旧格解封、把新格封上 —— 那一条事件只带建筑 id，
    /// 新占地得回建造模块那一本册上查。显式递进来的查法照样说了算（自由放置那一侧递自己的）。
    /// 自由放置的建筑在建造模块上没有格（<c>Cells</c> 恒为空表），那一种世界的迁阻挡要靠
    /// <paramref name="navCellsOf"/> 那一侧的查法，见 <see cref="NavTerrainLink"/> 类注。</para>
    ///
    /// <para><b>接在第一个空间那张网格上</b>：本件只造一条接线，落哪张网格由
    /// <see cref="Grid"/> 那一对属性定 —— 别的空间那张网格要落地形，宿主自己接一条
    /// <see cref="NavTerrainLink"/>。</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">已经推过步（晚了）、已经接过一次、没有建造模块，或还没有网格。</exception>
    public NavTerrainLink AttachTerrain(
        Func<GridCell, NavCell> mapCell,
        Func<string, IReadOnlyList<NavCell>>? navCellsOf = null,
        Func<string, IReadOnlyList<GridCell>>? cellsOf = null)
    {
        ArgumentNullException.ThrowIfNull(mapCell);
        if (_advanced)
        {
            throw new InvalidOperationException(
                $"晚了阻挡永不落：地形接线接在第 {_clock.Tick.ToString(CultureInfo.InvariantCulture)} 步之后，"
                + "游标从当下开始，之前发过的建造事件不回放一遍 —— 这一局那几堵墙就永远不在网格里。");
        }

        if (_terrain is not null)
        {
            throw new InvalidOperationException(
                "地形接线已经接过一次：再接一条就是两本记忆表，撤占用只走其中一本，于是留下一堵撤不掉的墙。");
        }

        if (_construction is null)
        {
            throw new InvalidOperationException(
                "地形接线接在建造模块的事件出口上，建造模块还没建 —— 没有那个出口就没有可读的那本账。");
        }

        if (_first is null)
        {
            throw new InvalidOperationException(
                "地形接线要落在走位流水线建的那张网格上 —— 还没建走位流水线，就还没有那张网格。");
        }

        // 建造模块这一本册就是「这栋现在占哪几格」的答案：挪动那一发只带建筑 id。
        var ledger = _construction;
        var link = new NavTerrainLink(
            _first.Grid,
            mapCell,
            cellsOf: cellsOf ?? (id => ledger.Find(id)?.Cells ?? []),
            navCellsOf: navCellsOf);
        link.Attach(ledger.Events);
        _terrain = link;
        return link;
    }

    /// <summary>
    /// 第 ⑤ 步：推进 <paramref name="steps"/> 步，每步的次序是「落地形 → 推时钟 → 推建造 → 宿主收尾」。
    /// </summary>
    /// <param name="steps">要推几步。小于 1 什么都不做。</param>
    /// <param name="afterEachStep">每步推完之后宿主的收尾（排事件之类）。缺省 = 没有。</param>
    /// <returns>时间真的前进了返回 true；步数不足或时钟暂停中返回 false（连地形都不读）。</returns>
    /// <exception cref="InvalidOperationException">②③④ 差任一层。</exception>
    /// <remarks>
    /// 缺层这条守卫<b>排在步数与暂停那两条之前</b>：先判守卫再判暂停，
    /// 否则暂停中调 Advance 会把「装配少一层」这条真 bug 盖成一句「暂停中，推不动」。
    /// </remarks>
    public bool Advance(int steps = 1, Action? afterEachStep = null)
    {
        if (_first is null || _construction is null || _terrain is null)
        {
            throw new InvalidOperationException("推进前五步没走齐，缺：" + string.Join("、", Missing()) + "。");
        }

        if (steps < 1) return false;
        if (_clock.IsPaused) return false;

        // 冻结排在步数与暂停之后、循环之前 —— 与暂停并排判，两者对推进的效果一样。
        // 判在这里而不是判在宿主那一层：有的世界重写 Advance 并直接调本件。
        if (FrozenProbe?.Invoke() == true) return false;

        for (var i = 0; i < steps; i++)
        {
            _terrain.Pump();
            _clock.Advance(1);
            _construction.Advance(1);
            afterEachStep?.Invoke();
        }

        _advanced = true;
        return true;
    }

    /// <summary>
    /// 分块推进到 <paramref name="targetTick"/> 为止。暂停中推不动就退出（步号不动）。
    /// </summary>
    /// <param name="targetTick">目标步号。已经越过它就什么都不做。</param>
    /// <param name="chunkTicks">一块推几步。必须至少 1。</param>
    /// <exception cref="ArgumentOutOfRangeException">分块步数小于 1。</exception>
    public void AdvanceTo(long targetTick, int chunkTicks = 250)
    {
        if (chunkTicks < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chunkTicks), chunkTicks, "分块步数必须 >= 1 —— 分成 0 步的循环一步也不走。");
        }

        while (_clock.Tick < targetTick)
        {
            var remaining = targetTick - _clock.Tick;
            var chunk = remaining < chunkTicks ? (int)remaining : chunkTicks;
            if (!Advance(chunk))
            {
                return;   // 暂停中：步号不动，退出
            }
        }
    }

    /// <summary>
    /// 交还本件挂上去的那几处订阅（重复调返回 0）。
    /// </summary>
    /// <returns>这次交还了几处订阅。<b>读数口径是「几处订阅」，不是「几条时钟脉搏」</b>：
    /// 每个已登记空间一条走位流水线那一处，地形接线那一处订的是建造模块的事件出口。</returns>
    public int Detach()
    {
        var handed = 0;
        foreach (var pipeline in _pipelines.Values)
        {
            if (pipeline.Detached) continue;
            pipeline.Stepper.Detach();
            pipeline.Detached = true;
            handed++;
        }

        if (_terrain is not null && !_terrainDetached)
        {
            _terrain.Detach();
            _terrainDetached = true;
            handed++;
        }

        return handed;
    }

    /// <summary>还没建成的层，按「走位流水线 / 建造模块 / 地形接线」这个次序列出来。</summary>
    private List<string> Missing()
    {
        var missing = new List<string>(3);
        if (_first is null) missing.Add("走位流水线");
        if (_construction is null) missing.Add("建造模块");
        if (_terrain is null) missing.Add("地形接线");
        return missing;
    }

    /// <summary>一个空间上登记的那一条：网格与步进器，加一个「交还没有」的本件侧读数。</summary>
    private sealed class SpacePipeline
    {
        public SpacePipeline(NavGrid grid, NavStepper stepper)
        {
            Grid = grid;
            Stepper = stepper;
        }

        public NavGrid Grid { get; }

        public NavStepper Stepper { get; }

        /// <summary>本件已把它那一处时钟脉搏交还 —— <see cref="Detach"/> 的计数读这一格，
        /// 不读 <see cref="NavStepper"/> 内部那一格（那边交还的是引擎事件，不是一处订阅的计数）。</summary>
        public bool Detached { get; set; }
    }
}
