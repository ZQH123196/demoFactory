#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DemoFactory.GugaFramework.Abilities;
using DemoFactory.GugaFramework.Composition;
using DemoFactory.GugaFramework.Construction;
using DemoFactory.GugaFramework.Pathfinding;
using DemoFactory.GugaFramework.Scheduling;

namespace DemoFactory.Prototypes.Template.Logic;

// 三层接线样板 —— 技能层 / 寻路层 / 建造层接进原型时，形状已经稳定下来的那几处。
//
// 这一份是脚手架，不是第四个原型：它不认识任何具体兵种、技能与建筑，也不带任何数值。
// 三层的**内容**（技能表、蓝图表、地形数据）全归原型，这里只留结构、次序与指路。
//
// 照最像自己那一份抄，四份现成实现都在 src/Prototypes/ 下：
//   · 只接寻路层、最小的一面 —— BeastArena/Scripts/Logic/BeastNavGrid.cs 与 BeastNavMove.cs；
//   · 三层都接、最全的一面 —— InfinityLord/Scripts/Logic/ 下 InfinityLordNavGrid.cs、
//     InfinityLordNavMove.cs、InfinityLordAbilities.cs、InfinityLordSkillTable.cs 四份，
//     表现层那一半在 InfinityLord/Scripts/SkillCues.cs；
//   · 建造层最小的一面 —— BattleArray/Scripts/Logic/BattleRoadblock.cs 与 BattleConstructionSink.cs；
//   · 建造层最全的一面 —— BeastArena/Scripts/Logic/ArenaBuildings.cs 与 ArenaConstructionSink.cs。
//
// 三层怎么被持有：世界**组合持有**三个层对象，每个各留一个只读读数口
// （现例 BeastArenaWorld 的字段 _navMove / _terrain / _construction / _abilities）：
//   · 寻路层 = 一张网格 + 一条走位流水线 + 一条地形接线；
//   · 建造层 = 一个模块（建筑册 + 事件出口 + 只读裁决）；
//   · 技能层 = 一张表（内容数据）+ 一件接线（宿主向层供的三个口 + 每步推进）。
//   原型自己的单位表、蓝图表与技能表都不进层：层只收标量与名字。
//
// 每步的次序（订阅次序就是执行次序，BeastArenaWorld 构造里那三段注释写得最清楚）：
//   ① 世界那一段先跑：决策链发令，并把每步标量递进层（移速、身体半径）；
//   ② 走位流水线接着跑：NavStepper 在自己的构造里订阅同一个时钟；
//   ③ 镜像同步最后跑：把层里的位置写回宿主那一列（开火读到的必须是走完之后的位置）。
//   地形接线不订阅时钟：宿主每步推进之前 Pump 一次（见 AdvanceSteps）。
//
// 三个口各自接在哪一处：
//   · 时间可控 —— 逻辑一侧只有 GameClock 一个时钟，三层都吃它（走位流水线订阅 Pulse，
//     技能层由宿主每步调 AdvanceStep，建造模块由宿主在时钟之后调 Advance）。
//     表现层那一半在 Main.cs：SpeedControl 攒满一步推一次。
//   · 状态可查 —— DebugSnapshot() 一份文本，每层各出自己那一段（见 FoldSegment 与类尾那三条出处）。
//   · 行为可注入 —— 一份按动词路由的词表（见 VerbNames 与 IsConstructionVerb）。
//     调试桥与录制包装走同一次调用（「桥只转发」）；表现层那一半在 Main.cs 的 SetupDebugBridge。

/// <summary>
/// 三层接线样板 —— 每步次序、场地注册口、登记口、地形接线、动词并集与快照折行这六处的形状。
/// 每个方法只摆形状（标量进来、层对象出去），名字与数值由原型自己递。
/// </summary>
/// <remarks>
/// <para><b>五步装配的唯一出处是公共层的世界装配</b>（走位流水线 → 建造模块 → 地形接线 → 推进）。
/// 新原型照 <see cref="WorldAssembly"/> 那份接，本类不再重复讲装配次序。</para>
/// <para><b><see cref="AdvanceSteps"/> 为什么不转发</b>：它收的是「已经建好的三件」，
/// 而世界装配把三件存在自己内部、不开「认领现成那三件」的入口 ——
/// 要让这一件变成转发，就得在那边开那个入口，那是那一件的归属范围。
/// 所以这里是本样板唯一一份仍在讲推进次序的方法，抄它的人应当直接抄世界装配那份。</para>
/// </remarks>
public static class LayerWiring
{
    // === 〇、五步装配：五步的次序与守卫全在公共层的 WorldAssembly 里 ===
    //
    // 本类只讲「递什么进去」；装配次序、缺层守卫、推进次序都是 WorldAssembly 的职责。
    // 新原型的世界走基类那条路时（继承 GameLogicBase），三件由基类建的装配自己持有 ——
    // 见 <see cref="Assemble"/>：那是照 WorldAssembly 的五步排的一遍，每一步只递参数。
    //
    // 五步：① 构造函数只认时钟（基类给的 Clock）→ ② 建走位流水线 → ③ 建建造模块
    //      → ④ 接地形接线 → ⑤ 推进（走 WorldAssembly.Advance，不在本类里另抄）。

    /// <summary>
    /// 照 <c>WorldAssembly</c> 的次序把三件装好 —— 形状样板，标量与名字由原型自己递。
    ///
    /// <para><b>次序不是建议，装配那侧有守卫</b>：④ 必须排在 ③ 之后（地形接线读建造模块的
    /// 事件出口），而 ④ 必须排在种子建筑放置之前（接上那一刻的游标记在当下，
    /// 之前发过的事件不回放 —— 排在后面接，种子建筑的阻挡永远落不进网格）。
    /// 三个原型都因为这条踩过，守卫与报错文案在 <c>WorldAssembly.AttachTerrain</c>。</para>
    ///
    /// <para><b>本方法只递三样，不替你排宿主特有的次序</b>：三个原型各有自己特有的次序依赖
    /// （决策段先订、地形接线早于种子建筑），那些不进公共层（出处见 <c>WorldAssembly</c> 类注
    /// 「本件不替宿主排次序」）。</para>
    ///
    /// <para><b>返回值</b>：三件装好之后交回宿主，供快照与投递读数用；
    /// 推进一律走 <c>WorldAssembly.Advance</c>（或基类的 <c>Advance</c>），不在本类里另开一条路。</para>
    /// </summary>
    /// <param name="assembly">世界装配（基类的 <c>Assembly</c>，世界走基类那条路时由基类持有）。</param>
    /// <param name="space">空间名。</param>
    /// <param name="grid">落地形的那张网格（由 <see cref="BuildSite"/> 建）。</param>
    /// <param name="move">步进档（一个逻辑步多少秒、前视距离、本步扩展上限）。</param>
    /// <param name="placeRule">放置规则（自由放置 / 网格放置）。</param>
    /// <param name="blueprints">蓝图表，一次递入。</param>
    /// <param name="mapCell">建造格 → 寻路格的换算（圆 → 格的光栅化归宿主，层里不认识圆形占地）。</param>
    /// <param name="navCellsOf">「这栋在寻路网格里占哪几格」的查法；不挪建筑的原型可省。</param>
    public static (NavStepper Stepper, ConstructionCore Construction, NavTerrainLink Terrain) Assemble(
        WorldAssembly assembly, SpaceId space, NavGrid grid, NavMoveTuning move,
        IPlacementRule placeRule, IEnumerable<Blueprint> blueprints,
        Func<GridCell, NavCell> mapCell, Func<string, IReadOnlyList<NavCell>>? navCellsOf = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(blueprints);

        // 第 ② 步：三份容量与两份档全部必递，公共层一个缺省都不给（层里兜一个缺省
        // 等于替调用方挑数值，而「这一档为什么是这个数」查不到出处）。
        var stepper = assembly.BuildNavPipeline(new NavPipelineSpec
        {
            Space = space,
            Grid = grid,
            Budget = new NavSolveBudget { PerRequestExpansions = 4000 },
            Recompute = new NavRecomputeTuning
            {
                GoalMoveToleranceMeters = 0.5,
                NotArrivedStepCap = 30,
            },
            CacheCapacity = 64,
            RecordCapacity = 256,
            TrackedRequestCapacity = 64,
            Move = move,
            // Gate / Separation / Events 留空 = 全放行 / 不开避让 / 不发事件。
            // 那是玩法决定不是数值档，由原型自己答（ADR 0009 的口径）。
        });

        // 第 ③ 步：建造模块，步号读数接在装配的时钟上。
        var construction = assembly.BuildConstruction(placeRule, blueprints);

        // 第 ④ 步：接地形接线（这一接必须早于宿主摆种子建筑）。
        var terrain = assembly.AttachTerrain(mapCell, navCellsOf);

        return (stepper, construction, terrain);
    }

    // === 一、每步的次序：先落地形，再推逻辑步 ===

    /// <summary>
    /// 推进 n 步 —— <b>每一步先落地形，再推这一步的逻辑步</b>（次序的出处是 <c>NavTerrainLink.Pump</c>）。
    /// 反过来的话，这一发砌的墙要到下一步才生效，绕障读数与建造那一步对不上。
    ///
    /// <para>建造模块的 Advance 排在时钟之后：这一步里到头的工期带这一步的步号。
    /// 没接建造层时递 null，那一段跳过。</para>
    ///
    /// <para>暂停中什么都不做，连地形都不读 —— 时间冻结对两边是同一件事。
    /// 一次 <c>Advance(n)</c> 与调 n 次 <c>Advance(1)</c> 逐字等价。</para>
    ///
    /// <para><b>新原型别抄这一份</b>：世界走基类那条路时，这三件是由世界装配自己持有的，
    /// 推进由 <c>WorldAssembly.Advance</c> 推 —— 逐字同形，只是那边是唯一出处。</para>
    /// </summary>
    /// <param name="clock">世界的逻辑时钟（三层共用同一个）。</param>
    /// <param name="terrain">建造 → 寻路的地形接线。</param>
    /// <param name="construction">建造模块；没接那一层时递 null。</param>
    /// <param name="steps">推进几步。</param>
    public static void AdvanceSteps(GameClock clock, NavTerrainLink terrain, ConstructionCore? construction,
        int steps)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(terrain);
        if (steps < 1 || clock.IsPaused)
        {
            return;
        }

        for (var i = 0; i < steps; i++)
        {
            terrain.Pump();             // 先落地形：这一发砌的墙在这一步就生效
            clock.Advance(1);           // 再推这一步的逻辑：三层的步进都挂在这一下上
            construction?.Advance(1);   // 建造过程排在时钟之后
        }
    }

    // === 二、场地注册口：宿主递哪几个标量 ===

    /// <summary>
    /// 建这张网格 —— 宿主递的就这几样：空间名、格长、两个方向的格数、静态阻挡表。
    /// 空阻挡表是合法声明：它说的是「这片场地没有地形阻挡」。
    /// 运行时砌的墙不走这里，走地形接线（见 <see cref="AttachTerrain"/>）。
    ///
    /// <para><b>构造之前先跑宿主侧那道格长校验</b>：格数 × 格长等于场地边长，漂了当场抛
    /// （「档与表钉死」）。现成实现见 <c>BeastNavGrid.RequirePinnedCellLength</c> 与
    /// <c>InfinityLordNavGrid.RequirePinnedCellLength</c>，两条都是四条一起校验。</para>
    ///
    /// <para>坐标换算的两份函数也在宿主侧（ADR 0009「换算归宿主」）：见 <see cref="ToGrid"/>
    /// 与 <see cref="ToWorld"/>。层里的格心落在下标 + 0.5 处，空间本身不带变换。</para>
    /// </summary>
    /// <param name="space">空间名 —— NavSpace 报身份、跨空间抛错时念的就是它。</param>
    /// <param name="cellMeters">格长（米）—— 与场地尺寸钉在一起的那一份数。</param>
    /// <param name="cellsX">沿 X 的格数。</param>
    /// <param name="cellsZ">沿 Z 的格数。</param>
    /// <param name="originX">网格第 (0,0) 格那一角的 X（米）—— 零基场地递 0。</param>
    /// <param name="originZ">网格第 (0,0) 格那一角的 Z（米）—— 零基场地递 0。</param>
    /// <param name="blockedCells">静态阻挡格（地形自带的那些）。</param>
    public static NavGrid BuildSite(SpaceId space, double cellMeters, int cellsX, int cellsZ,
        double originX, double originZ, IReadOnlyList<NavCell> blockedCells)
    {
        ArgumentNullException.ThrowIfNull(blockedCells);
        return new NavGrid(new NavGridSpec
        {
            Space = space,
            CellMeters = cellMeters,
            CellsX = cellsX,
            CellsZ = cellsZ,
            OriginX = originX,
            OriginZ = originZ,
            BlockedCells = blockedCells,
        });
    }

    /// <summary>世界坐标 → 空间局部米。两份换算成对写，只写一半时另一半会被就地重算一遍。</summary>
    /// <param name="worldX">世界坐标的 X。</param>
    /// <param name="worldZ">世界坐标的 Z。</param>
    /// <param name="originX">网格原点在世界里的 X。</param>
    /// <param name="originZ">网格原点在世界里的 Z。</param>
    public static NavPoint ToGrid(double worldX, double worldZ, double originX, double originZ)
        => new(worldX - originX, worldZ - originZ);

    /// <summary>空间局部米 → 世界坐标（<see cref="ToGrid"/> 的逆）。</summary>
    /// <param name="point">空间局部米。</param>
    /// <param name="originX">网格原点在世界里的 X。</param>
    /// <param name="originZ">网格原点在世界里的 Z。</param>
    public static (double X, double Z) ToWorld(NavPoint point, double originX, double originZ)
        => (point.X + originX, point.Z + originZ);

    // === 三、登记口：身体半径与移速都由宿主现读递进来 ===

    /// <summary>
    /// 登记一名单位 —— 层里没有词表，身体半径与每步移速都由宿主现读递进来。
    /// 位置的真值从此住在流水线这一份，宿主那一列是每步同步的镜像。
    /// 同组号只有站位冲突那一档读它，缺省 0 表示全员同组。
    /// </summary>
    /// <param name="stepper">走位流水线。</param>
    /// <param name="agentId">单位 id。</param>
    /// <param name="radiusMeters">身体半径（米）。</param>
    /// <param name="moveSpeedMetersPerSecond">移速（米每游戏秒）—— 不是「米每步」那一档。</param>
    /// <param name="start">起始位置（空间局部米）。</param>
    /// <param name="facingDegrees">起始朝向（度）。</param>
    /// <param name="group">同组号（站位冲突只推同组的）。</param>
    public static void RegisterAgent(NavStepper stepper, string agentId, double radiusMeters,
        double moveSpeedMetersPerSecond, NavPoint start, double facingDegrees, int group)
    {
        ArgumentNullException.ThrowIfNull(stepper);
        stepper.Register(new NavAgentSpec
        {
            Id = agentId,
            RadiusMeters = radiusMeters,
            MoveSpeedMetersPerSecond = moveSpeedMetersPerSecond,
            Start = start,
            FacingDegrees = facingDegrees,
            Group = group,
        });
    }

    // === 四、地形接线：建造占用 → 寻路网格 ===

    /// <summary>
    /// 造地形接线，并把建造模块的事件出口接上。
    ///
    /// <para>两个查法都归宿主（ADR 0009「换算归宿主」）：建造格 → 寻路格那一份必填，
    /// 「这栋占哪几格」那一份可省 —— 不挪建筑的原型用不上它。
    /// 网格放置的事件自带格名单，自由放置的事件自带空表、走寻路格那一份查法。</para>
    ///
    /// <para>圆 → 格的光栅化写在宿主侧（现例 <c>BeastNavGrid.CircleCells</c> 与
    /// <c>InfinityLordNavGrid.BuildingCells</c>）—— 层里不认识圆形占地，也不认识建造规则。</para>
    ///
    /// <para>接上那一刻的游标记在当下，之前发过的事件不回放。于是接线排在种子建筑之前
    /// （现例 BeastArenaWorld 构造：<c>_terrain.Attach</c> 在 <c>RegisterFixture</c> 之前）。</para>
    ///
    /// <para><b>读数口</b>：<c>StampedCount</c>（落了几枚印子）、<c>Missed</c>（有几条没落成地形）、
    /// <c>CellsOf</c>（这栋占哪几格）。<c>Missed</c> 不为 0 就说明有一类地形改动没进网格。</para>
    /// </summary>
    /// <param name="grid">落地形的那张网格。</param>
    /// <param name="construction">建造模块（事件出口从它身上取）。</param>
    /// <param name="mapCell">建造格 → 寻路格的换算。</param>
    /// <param name="navCellsOf">「这栋在寻路网格里占哪几格」的查法；不递时旧行为照旧。</param>
    public static NavTerrainLink AttachTerrain(NavGrid grid, ConstructionCore construction,
        Func<GridCell, NavCell> mapCell, Func<string, IReadOnlyList<NavCell>>? navCellsOf = null)
    {
        ArgumentNullException.ThrowIfNull(construction);
        var link = new NavTerrainLink(grid, mapCell, cellsOf: null, navCellsOf: navCellsOf);
        link.Attach(construction.Events);
        return link;
    }

    // === 五、动词并集：两三层各出自己那几条，本原型再补自己的 ===

    /// <summary>
    /// 词表并集 —— 形状照排兵布阵那一份（<c>DuelRiftSetup.VerbNames</c>）：
    /// 各层名单在前，本原型自己那几条在后。
    ///
    /// <para>只列写路径：call 类注入得动、不进录（建造层的 CheckPlacement 与寻路层那四条只读）。
    /// 层那一份表给的是动词名与参数次序，分发按动词路由到对应那一层（见 <see cref="IsConstructionVerb"/>）。</para>
    ///
    /// <para><b>一份名单、两处问</b>：这一份答「哪几条接得住」，各层的 Dispatch 答「参数怎么读」。
    /// 动词名一个字面量都别在这里另抄 —— 抄一份就是下一次漂移的起点。</para>
    /// </summary>
    public static readonly string[] VerbNames =
    [
        AbilityVerbs.Cast,          // 技能层：只列这一侧接得住的那几条（全名是 AbilityVerbs.Names）
        AbilityVerbs.ApplyEffect,
        ConstructionVerbs.Place,    // 建造层：那两条写路径（全名是 ConstructionVerbs.Names）
        ConstructionVerbs.Remove,
        // ← 本原型自己的写路径接在这里：每一条都在 Inject 的 switch 里转发既有直达方法
    ];

    /// <summary>这一条属于建造层那份词表吗（写路径两条 + 只读裁决那一条）。分发按它路由到各层的 Dispatch。</summary>
    /// <param name="cmd">动词名。</param>
    public static bool IsConstructionVerb(string cmd)
        => cmd is ConstructionVerbs.Place or ConstructionVerbs.Remove or ConstructionVerbs.Check;

    // === 六、状态可查：每层各出自己那一段，折进同一份快照 ===

    /// <summary>
    /// 把层给的一段多行读数折成一行 —— 世界那份快照按「 | 」分段，层给的是多行文本。
    /// 这里只搬 token，一个数都不重算（现例 LogicWorld 的 NavSegment 与 AbilitySegment）。
    ///
    /// <para>三段读数各自的出处：网格与走位那一段是 <c>NavGridSnapshot.Render</c> 加
    /// <c>NavStepper.ReplaySnapshotSegment</c>，建造那一段是 <c>ConstructionCore.DebugSnapshotSegment</c>，
    /// 技能那一段是 <c>AbilityStepper.TakeSnapshot</c>。</para>
    /// </summary>
    /// <param name="segment">层给的那一段读数。</param>
    public static string FoldSegment(string segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        return string.Join(" ", segment
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
    }
}
