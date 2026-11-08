#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DemoFactory.GugaFramework.Abilities;
using DemoFactory.GugaFramework.Scheduling;

namespace DemoFactory.GugaFramework.Abilities.Stage;

/// <summary>
/// <b>一次游标取数的结果</b>：这一段新增的事件（typed）、游标起止各停在哪，
/// 以及<b>游标之后发过、但已经不在环形里因而读不出来的那几条</b>。
///
/// 四个数都是读数，不是一句结论（契约「状态可查」「三条铁律」第三条：「日志里没有」≠「没发生」）：
/// <list type="bullet">
/// <item><see cref="Events"/>：这一段新增、<b>环形里还留得住</b>的那些，按发出顺序，
/// 已经过 <see cref="SkillStage.EventsSinceCursor{T}"/> 那个筛选。</item>
/// <item><see cref="FromTotal"/>：读之前游标停在哪（事件流累计条数那一格）。</item>
/// <item><see cref="TotalNow"/>：读之后游标停在哪（下一段从这儿起算）。</item>
/// <item><see cref="DroppedSinceCursor"/>：游标之后发过、却已被环形挤掉的条数。
/// 这一格是「没演的那几条读得出来」那个口径在出口上的位置。挤掉的那些连形状都没了，
/// 所以这个数只说<b>这一段丢了多少条</b>，<b>不声称它们是哪一类</b>——
/// 被筛选排除掉的那一类也计在这里（它们的形状已经没了，问不出来）。要知形状只能靠别处留的账。</item>
/// </list>
/// </summary>
/// <param name="Events">这一段新增且还留得住的事件（已按筛选挑过）。</param>
/// <param name="FromTotal">读之前游标停在哪（累计条数）。</param>
/// <param name="TotalNow">读之后游标停在哪（累计条数）。</param>
/// <param name="DroppedSinceCursor">游标之后发过、已被环形挤掉的条数。</param>
public readonly record struct EventDelta(
    IReadOnlyList<AbilityEvent> Events,
    long FromTotal,
    long TotalNow,
    long DroppedSinceCursor);

/// <summary>
/// <b>逻辑壳</b>——一块条件固定的干净空地，纯 C#、零引擎类型、可 <c>dotnet test</c> 秒级跑。
/// 场子的本体就是本文件：<b>它不认识任何一条具体技能、任何一条属性名、任何一条 tag</b>——
/// 名字与数值全由注册口（<see cref="SkillStageSpec"/>）递进来。加一条技能、换一个原型，本文件一个字都不动。
///
/// 场子自带时间可控、状态可查、行为可注入三个口，与每个逻辑世界同一份契约：
/// <list type="bullet">
/// <item><b>时间可控</b>：<see cref="AdvanceRealSeconds"/>（表现层每帧那一发）／<see cref="Advance"/>（批量推进）
/// ／<see cref="AdvanceStep"/>（单步）；<see cref="Paused"/> 只冻逻辑，<see cref="Speed"/> 倍率乘在流速上。
/// 步是唯一那把尺（1 步 = 0.1 秒，「时间基准」）。</item>
/// <item><b>状态可查</b>：<see cref="DebugSnapshot"/> 一发读出全量真值，显示即真相。</item>
/// <item><b>行为可注入</b>：<see cref="Inject"/> 是写路径的唯一分发口（「桥只转发」）。</item>
/// </list>
///
/// 三条边界，写明白不藏：
/// <list type="number">
/// <item><b>写路径只有一条</b>：放技能、挂效果、改属性、连挪位置都从 <see cref="Inject"/> 进，动词名与参数次序
/// 住在 <see cref="AbilityVerbs"/> 那一份表里（「桥只转发」）。场子不另写一套实现，
/// 所以「注入的路径」与「玩家走的那条」不是两回事。</item>
/// <item><b>随机源在宿主手里，种子也在宿主手里</b>：<see cref="RandomEnabled"/> 为假时
/// <see cref="NextRandom"/> 每次给同一个数，于是同条件重跑逐字一致（场子「固定」那一条口径）。</item>
/// <item><b>机位不在本文件里</b>：机位是表现层参数，不进检查点快照（「试验场随技能层」）。
/// 逻辑壳里没有任何一行与相机有关，所以那条不是靠自觉守住的，是结构上就没有。
/// <see cref="Paused"/> 与 <see cref="Speed"/> 同属这一类：<b>时间控制不进快照</b>，
/// 否则中途停一下再放两只会在逐字比对上报「第几步起分叉」，而逻辑一个字没变（同一句理由）。</item>
/// </list>
///
/// <b>场地条件兑到「理想」那一档</b>：摆位是二维的（X 与 Z，米）、朝向是开局的场地条件，
/// 距离按两点欧氏距离现算、扇形那一把尺用公共层 <see cref="AbilityStepper.InsideSector"/>——
/// 所以从贴脸到超出射程、二维站位、扇形界内界外都摆得出（「时间基准」 的步是唯一那把尺）。
/// <b>场子不替单位走位</b>：除了注册口那份开局摆位，位置只由 <see cref="Inject"/> 里的
/// <c>Move</c> 改，别的什么都不动它（这一条叫「钉住」，由 S15 那组用例钉）。
///
/// <b>步号只有一份</b>：时间推进落在层那本账上（<c>AbilityWorld.Tick</c>），
/// 场子不另持一个步号——不然重放分岔（<c>Branch</c>）带走的那一份与场子自己那份会漂。
/// <see cref="SpeedControl"/> 只借来做「真实秒 × 倍率 = 该推几步」的换算，暂停是场子自己那一格。
///
/// <b>坐标归场子、不归层</b>：摆位落在 <see cref="PointOf"/> 那份里，距离经
/// <see cref="AbilityStepper.DistanceOf"/> 递进层——层不持坐标，于是换一个三维场子只要换掉那一条谓词，
/// 本文件其余部分一个字不动。
///
/// <b>为什么本类不封死</b>：第二个场子（<see cref="SkillStageOpenField"/>，开阔多点）派生自本类，
/// 只往上加「群体名单」与「群体施加」两发，底下那份时间、状态、注入三个口一份都不重写。
/// 这与 spec 说的「不走子类那条」不是同一件事——那条禁的是<b>原型各自抄一遍壳</b>；
/// 这里的派生类是公共层自己给的完整场子，原型侧一行都不用抄，抄错那行的机会压根不存在。
/// </summary>
public class SkillStage : IAbilityHost, IAbilityVerbSink
{
    /// <summary>1 步 = 0.1 秒 ⇒ 每秒 10 步（「时间基准」）。<see cref="SpeedControl"/> 换算流速用的就是这个频率。</summary>
    public const double StepsPerSecond = 10.0;

    private readonly SkillStageSpec _spec;
    private readonly StageRoster _roster;
    private readonly AbilityWorld _world = new();
    private readonly AbilityStepper _stepper;
    private readonly Random _rng;
    private readonly SpeedControl _speed = new(StepsPerSecond);

    /// <summary>
    /// 摆位（米，地面上的一个点）：<b>场子自己那份</b>，不进技能层。
    /// 单位身上不再有坐标（那一格已从公共层拿掉），所以摆位与移动都落在这里，
    /// 距离则通过 <see cref="AbilityStepper.DistanceOf"/> 递进去——层只问不持。
    /// </summary>
    private readonly Dictionary<string, (double X, double Z)> _posByUnit = new(StringComparer.Ordinal);

    /// <summary>朝向（度）：开局的场地条件，只喂给扇形那一把尺（<see cref="AbilityStepper.InsideSector"/>）。</summary>
    private readonly Dictionary<string, double> _facingByUnit = new(StringComparer.Ordinal);

    /// <summary>帧内余量：真实秒换算出来的步数不够一步时先攒着（「时钟解耦」：攒够一个步长才推一步）。</summary>
    private double _carry;

    /// <summary>
    /// 事件出口的<b>游标</b>：键是 payload 类型，值是该类型读到「累计第几条」了。
    /// <b>一份类型一个游标</b>——共用一个的话，消费方按三种类型各问一次时，
    /// 只有头一次问的那一种拿得到事件，另外两种静默空手。
    /// </summary>
    private readonly Dictionary<Type, long> _eventCursors = new();

    /// <summary>给一个固定种子（默认 1）起一局。同种子两跑逐字一致。</summary>
    public SkillStage(SkillStageSpec spec, int seed = 1)
    {
        _spec = spec ?? throw new ArgumentNullException(nameof(spec),
            "起一局要一份注册口：技能表、纯函数、单位属性，场子自己一个都不替你挑");
        _roster = spec.Roster;
        _rng = new Random(seed);
        Seed = seed;

        foreach (var unit in _roster.Units)
        {
            var set = _world.AddUnit(unit.UnitId, unit.Attributes);
            _posByUnit[unit.UnitId] = (unit.X, unit.Z);
            _facingByUnit[unit.UnitId] = unit.FacingDegrees;
            foreach (var abilityId in unit.AbilityIds)
            {
                set.Abilities[abilityId] = new AbilityOnBody { Id = abilityId };
            }
        }

        _stepper = new AbilityStepper(_world, this, spec.Calcs, spec.Tables, _roster.Config);

        // 「注册谓词」那条路：场子把摆位换算成距离递进去，层里不存任何坐标。
        // 贴脸场那种距离是常数的情形不必注册——调用点直接递标量走另一条路。
        _stepper.DistanceOf = DistanceBetween;
    }

    // === 摆位与朝向（场子自己那份；坐标归场子、距离经谓词递进层）===

    /// <summary>某个单位此刻的摆位（米，场子自己那份；场子以外的东西从层里读不到它）。</summary>
    public (double X, double Z) PointOf(string unitId)
        => _posByUnit.GetValueOrDefault(unitId);

    /// <summary>摆位里那根 X（米）。一维那几处读它；判定请走 <see cref="PointOf"/> 那一对。</summary>
    public double XOf(string unitId) => PointOf(unitId).X;

    /// <summary>摆位里那根 Z（米）。</summary>
    public double ZOf(string unitId) => PointOf(unitId).Z;

    /// <summary>朝向（度）：0 = 朝 +X，90 = 朝 +Z。</summary>
    public double FacingDegreesOf(string unitId) => _facingByUnit.GetValueOrDefault(unitId);

    /// <summary>
    /// 两个单位此刻的距离（米）：两点欧氏距离，<b>摆位二维那一档也走这一份实现</b>。
    /// 射程激活检查、光环名单、扇形半径三处问的都是它，所以「谁算的距离」只有一个答案。
    /// 场地是平的、中间没有阻挡——这是「理想」那一条口径的另一半，场子不持任何阻挡表。
    /// </summary>
    public double DistanceBetween(string fromId, string toId)
    {
        var from = PointOf(fromId);
        var to = PointOf(toId);
        var dx = to.X - from.X;
        var dz = to.Z - from.Z;
        return Math.Sqrt((dx * dx) + (dz * dz));
    }

    /// <summary>换算成扇形那一把尺收的两个标量（度 → 单位向量；算术一律 double，「double 与不变文化」）。</summary>
    public (double FacingX, double FacingZ) FacingVectorOf(string unitId)
    {
        var radians = FacingDegreesOf(unitId) * Math.PI / 180.0;
        return (Math.Cos(radians), Math.Sin(radians));
    }

    /// <summary>
    /// 落在施法者那发扇形里的单位（按注册口的摆位次序，遍历次序是确定性的一部分）。
    /// <b>怎么算命中只有一份实现</b>：判定走公共层 <see cref="AbilityStepper.InsideSector"/>
    /// （两个点 + 朝向 + 半径 + 全角，全是标量），场子只递自己那份摆位与朝向。
    /// 名单落地成事件那一发是 <c>Stepper.RecordShape</c>，由调用方自己发（「桥只转发」：不许场子替他写账）。
    /// </summary>
    public IReadOnlyList<string> TargetsInSector(string casterId, double radiusMeters, double coneDegrees)
    {
        var from = PointOf(casterId);
        var (facingX, facingZ) = FacingVectorOf(casterId);
        var hits = new List<string>();
        foreach (var other in _world.UnitOrder)
        {
            if (other == casterId || !IsAlive(other))
            {
                continue;
            }

            var to = PointOf(other);
            if (AbilityStepper.InsideSector(from.X, from.Z, to.X, to.Z,
                    facingX, facingZ, radiusMeters, coneDegrees))
            {
                hits.Add(other);
            }
        }

        return hits;
    }

    /// <summary>本局的种子。读数里念出来：不然「这一局是哪一档」要回头翻代码。</summary>
    public int Seed { get; }

    /// <summary>当前第几步（步 ÷ 10 = 秒，「时间基准」）。这一格归层那本账，场子不另持一个。</summary>
    public long Tick => _world.Tick;

    /// <summary>接层那份配置。</summary>
    public AbilityLayerConfig Config => _roster.Config;

    /// <summary>步进器本体：原型侧要读写更细的东西时从这儿拿。</summary>
    public AbilityStepper Stepper => _stepper;

    /// <summary>
    /// 随机项总开关。<b>为假时 <see cref="NextRandom"/> 每次给同一个数</b>——
    /// 场子「固定」那一条口径的实现处（随机项一律能关掉，关掉后逐字一致）。
    /// </summary>
    public bool RandomEnabled { get; set; } = true;

    /// <summary>关掉随机时每次给的那个数。默认 0.5（区间中点，不偏向任何一端）。</summary>
    public double FixedRandomValue { get; set; } = 0.5;

    // === 时间可控 ===

    /// <summary>
    /// 逻辑暂停：<b>只冻逻辑</b>。冻结之后 <see cref="Advance"/> 与 <see cref="AdvanceRealSeconds"/> 一步都不走，
    /// 而 <see cref="AdvanceStep"/>（单步调试口）照走一步——「停下来一格格看」是暂停的全部用处。
    /// 这一格<b>不进快照</b>，理由与机位参数不进快照同源（「试验场随技能层」）：停一下不该让逐字比对报分叉。
    /// </summary>
    public bool Paused { get; set; }

    /// <summary>
    /// 流速倍率：1 = 现实时间（1 步 = 0.1 秒），0.25 = 慢放，20 = 快进。
    /// 换算走既有的 <see cref="SpeedControl"/>（真实秒 × 倍率 × 每秒步数），本文件不自己算那一乘。
    /// 必须 &gt; 0，缺了当场抛，不兜默认值（「桥只转发」 同一句理由）。
    /// </summary>
    public double Speed
    {
        get => _speed.Multiplier;
        set => _speed.SetMultiplier(value);
    }

    /// <summary>暂停：只冻逻辑，画面照跑。</summary>
    public void Pause() => Paused = true;

    /// <summary>解除逻辑暂停。</summary>
    public void Resume() => Paused = false;

    /// <summary>
    /// 表现层每帧那一发：按 <paramref name="realSeconds"/> 的现实时间推进。
    /// 倍率乘在流速上、攒够一个步长才推一步（「时钟解耦」）；暂停中返回 0 且什么都不做。
    /// 返回真正推了几步——表现层拿它判「这一帧要不要重画」。
    /// </summary>
    public long AdvanceRealSeconds(double realSeconds)
    {
        if (Paused || !(realSeconds > 0))
        {
            return 0;
        }

        _carry += _speed.TicksPerFrame(realSeconds);
        var steps = (long)Math.Floor(_carry);
        if (steps <= 0)
        {
            return 0;
        }

        _carry -= steps;
        for (var i = 0L; i < steps; i++)
        {
            _stepper.AdvanceStep();
        }

        return steps;
    }

    /// <summary>
    /// 推 <paramref name="steps"/> 步（时间流推进）。<b>暂停中一步都不走</b>，返回是否真的前进了。
    /// 要在暂停中跳步用 <see cref="AdvanceDebug"/>。
    /// </summary>
    public bool Advance(long steps)
    {
        if (Paused || steps < 1)
        {
            return false;
        }

        for (var i = 0L; i < steps; i++)
        {
            _stepper.AdvanceStep();
        }

        return true;
    }

    /// <summary>单步调试口：<b>暂停中也照走一步</b>（与最早那份宿主那个单步调试口同一形状）。</summary>
    public void AdvanceStep() => _stepper.AdvanceStep();

    /// <summary>
    /// 批量单步调试口：<b>暂停中也照走 n 步</b>，返回真走了几步。给「跳到第 k 步看那一格」用。
    /// </summary>
    public long AdvanceDebug(long steps)
    {
        var moved = 0L;
        for (var i = 0L; i < Math.Max(0, steps); i++)
        {
            _stepper.AdvanceStep();
            moved++;
        }

        return moved;
    }

    // === 状态可查 ===

    /// <summary>
    /// 一发读出全量真值（状态级，「断言比快照」：断言只比这份，不比事件流）。
    /// 逐行逐字段念出，不摘要：<c>tick</c> → 每个单位（摆位、朝向、属性）→ 层自己那本账 → 观测窗口读数。
    /// 属性名逐个念的是注册口递进来的那些，本文件不认识其中任何一个。
    ///
    /// <b>时间控制（<see cref="Paused"/> / <see cref="Speed"/>）与机位参数一样不进这一份</b>：
    /// 停一下、慢放一次都不改逻辑状态，进快照只会让逐字比对在没分叉的地方报分叉。
    /// 要看它们读属性（<c>IsPaused</c> / <c>Speed</c>），别在快照里找。
    /// </summary>
    public string DebugSnapshot()
    {
        var lines = new List<string>
        {
            $"tick={Tick} seconds={AbilityNumbers.Format(Tick / 10.0)} seed={Seed} " +
            $"random={(RandomEnabled ? 1 : 0)}",
        };

        foreach (var unitId in _world.UnitOrder)
        {
            var set = _world.Units[unitId];
            var attributes = string.Join(' ',
                set.Current.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                    .Select(kv => $"{kv.Key}={AbilityNumbers.Format(kv.Value)}"));
            var point = PointOf(unitId);
            lines.Add($"u {unitId} x={AbilityNumbers.Format(point.X)} z={AbilityNumbers.Format(point.Z)} " +
                      $"facing={AbilityNumbers.Format(FacingDegreesOf(unitId))} {attributes}");
        }

        foreach (var row in _stepper.TakeSnapshot().Split('\n'))
        {
            if (row.Length > 0)
            {
                lines.Add($"s {row}");
            }
        }

        lines.Add(Readout());
        return string.Join("\n", lines);
    }

    /// <summary>
    /// 观测窗口读数。<b>「日志里没有」≠「没发生」</b>（「三条铁律」 第三条）：
    /// <c>dropped</c> 那一格是环形缓冲挤掉多少条，读数不带它就等于把「丢了」说成「没有」。
    /// </summary>
    public string Readout()
    {
        var events = _world.Events;
        return $"events total={events.Total} kept={events.KeptCount} dropped={events.Dropped} " +
               $"capacity={events.Capacity}";
    }

    /// <summary>事件尾窗的行文本（人读；真值在 <see cref="DebugSnapshot"/> 里）。</summary>
    public IReadOnlyList<string> EventTailLines(int count)
        => _world.Events.Tail(count).Select(e => e.Line).ToArray();

    // === 结构化事件出口（typed 事件；上面那两个是同一扇窗的人读那一面）===

    /// <summary>
    /// 事件尾窗的 <b>typed 事件</b>：与 <see cref="EventTailLines"/> 同一扇窗、同一批事件，
    /// 递的是 <see cref="AbilityEvent"/> 而不是压成字符串的那一行。
    /// 消费方要答「第几步、谁、对谁、哪一种形状」时读这一发——从 <c>Line</c> 反解析等于把真值读成字面量。
    /// </summary>
    public IReadOnlyList<AbilityEvent> EventsTail(int count) => _world.Events.Tail(count);

    /// <summary>
    /// 按 payload 类型挑出<b>环形里还留着的</b>那一类，按发出顺序。
    ///
    /// <para><b>形状是「整个事件」而不是只给 payload</b>：只给 payload 的那个筛选
    /// （<c>AbilityEventLog.Where&lt;T&gt;()</c>）拿不到步号，表现层就没法答「这一下落在第几步」。
    /// 所以对外这一格是整个事件 + <paramref name="keep"/> 那个谓词，谓词也拿得到整个事件
    /// 与已经转型的那一份形状。</para>
    ///
    /// <para><paramref name="keep"/> 给 null 就是这一类的全部。走这一格拿的是<b>全量</b>；
    /// 每一步只想要新增的那些，走 <see cref="EventsSinceCursor{T}"/>。</para>
    /// </summary>
    public IReadOnlyList<AbilityEvent> EventsOf<T>(Func<AbilityEvent, T, bool>? keep = null) where T : EventPayload
        => KeptSince<T>(0, keep);

    /// <summary>
    /// <b>游标增量取数</b>：同一个筛选，只给<b>上一次读完之后新增</b>的那些，
    /// 并把游标推到当下。表现层每帧问一次就不用每帧翻全量，
    /// 同一条事件也不会被演第二遍。
    ///
    /// <para><b>游标停在哪、这一段有没有被环形挤掉，一起递出来</b>（<see cref="EventDelta"/>）：
    /// 挤掉的那些连形状都没了，返回的名单就比真实发生过的短——只递名单的话，
    /// 消费方会把「名单只有一条」当成「这一段只发生了一次」，
    /// 而真相是这一段发过若干条、其中若干条读不出来
    /// （契约「状态可查」「三条铁律」第三条：「日志里没有」≠「没发生」）。
    /// <see cref="EventDelta.DroppedSinceCursor"/> 就是那个数，非零即说明这一段有读不出来的。</para>
    ///
    /// <para><b>归零有三条路</b>：① <see cref="ResetEventCursor"/> 由调用方主动叫（换一局重挂）；
    /// ② 累计条数倒退了（分支或重建把整条流换过）自动从头读；
    /// ③ 清掉留着的那些（<c>ClearKept</c>）不需要特判——游标之后那些没读到的，
    /// 本来就被算进 <see cref="EventDelta.DroppedSinceCursor"/>，所以「被清掉了」照样读得出来。</para>
    /// </summary>
    public EventDelta EventsSinceCursor<T>(Func<AbilityEvent, T, bool>? keep = null) where T : EventPayload
    {
        var log = _world.Events;
        var totalNow = log.Total;

        // 从没读过这一类，或者累计条数倒退了（流被换过）：这一格从头起。
        if (!_eventCursors.TryGetValue(typeof(T), out var from) || from > totalNow)
        {
            from = 0;
        }

        // 挤掉的那几条 = 累计挤掉数 − 游标停的那一条：游标之前挤掉的早读过了，不算这一段。
        var dropped = Math.Max(0, log.Dropped - from);
        _eventCursors[typeof(T)] = totalNow;
        return new EventDelta(KeptSince<T>(from, keep), from, totalNow, dropped);
    }

    /// <summary>
    /// 这一类的游标读到累计第几条了（读数口：没读过就是 0）。
    /// 换一局重挂的表现层靠这一发清干净自己那份「读过的位置」。
    /// </summary>
    public long EventCursorOf<T>() where T : EventPayload
        => _eventCursors.TryGetValue(typeof(T), out var at) ? at : 0L;

    /// <summary>全部游标归零：下一次读从头再给一遍（换一局、换表现层那一份时叫）。</summary>
    public void ResetEventCursor() => _eventCursors.Clear();

    /// <summary>
    /// 累计第 <paramref name="from"/> 条之后、<b>环形里还留得住</b>的那几条，按发出顺序。
    /// 从头取就是尾窗（尾窗 = 最近 n 条，游标取数 = 某一格之后剩下的），
    /// 所以这两个出口永远对得上，不各算各的。
    /// </summary>
    private IReadOnlyList<AbilityEvent> KeptSince<T>(long from, Func<AbilityEvent, T, bool>? keep)
        where T : EventPayload
    {
        var log = _world.Events;
        var want = log.Total - from;               // 游标之后一共发过多少条
        if (want <= 0)
        {
            return Array.Empty<AbilityEvent>();
        }

        // 尾窗只给还留得住的那些：中途被挤掉的那些本来就不在里面（读数由 DroppedSinceCursor 念）。
        var window = log.Tail((int)Math.Min(want, int.MaxValue));
        if (keep is null)
        {
            return window.Where(e => e.Payload is T).ToArray();
        }

        return window.Where(e => e.Payload is T typed && keep(e, typed)).ToArray();
    }

    // === 行为可注入 ===

    /// <summary>
    /// 写路径的唯一分发口：注入、桥、录制与测试喂的都是这一发（「桥只转发」）。
    /// 动词名与参数次序住在 <see cref="AbilityVerbs"/>，本文件只是「这一侧接得住哪些」那份适配器。
    /// </summary>
    public bool Inject(string cmd, string args) => AbilityVerbs.Dispatch(this, cmd, args);

    // === IAbilityHost：世界向层供的两样（单位查询口 + 随机源）===

    /// <inheritdoc />
    public AbilitySet? Find(string unitId) => _world.Units.TryGetValue(unitId, out var set) ? set : null;

    /// <inheritdoc />
    public bool IsAlive(string unitId)
    {
        var set = Find(unitId);
        if (set is null)
        {
            return false;
        }

        return ReadCurrent(set, Config.HealthAttribute) > 0;
    }

    /// <inheritdoc />
    public bool Exists(string entityId) => _world.Units.ContainsKey(entityId);

    /// <inheritdoc />
    public IReadOnlyList<string> UnitIds => _world.UnitOrder;

    /// <inheritdoc />
    public double NextRandom() => RandomEnabled ? _rng.NextDouble() : FixedRandomValue;

    // === IAbilityVerbSink：这一侧接得住哪几条、从哪个口子打进去 ===

    string IAbilityVerbSink.HostLabel => HostLabel;

    /// <summary>
    /// 宿主身份：报错时念出来的那一句（哪个场子接不住哪条动词）。
    /// 派生场子改这一格，名字要念得出是哪个场子——「<see cref="SkillStageOpenField"/> 这一侧不接 X」
    /// 比「SkillStage 这一侧不接 X」好定位。
    /// </summary>
    protected virtual string HostLabel => nameof(SkillStage);

    private static readonly IReadOnlySet<string> SupportedVerbNames =
        new HashSet<string>(StringComparer.Ordinal)
        {
            AbilityVerbs.Cast, AbilityVerbs.Cancel, AbilityVerbs.ApplyEffect, AbilityVerbs.RemoveEffect,
            AbilityVerbs.SetAttribute, AbilityVerbs.SetBase, AbilityVerbs.ClearCooldown,
            AbilityVerbs.SetStepsRemaining, AbilityVerbs.Move,
        };

    IReadOnlySet<string> IAbilityVerbSink.SupportedVerbs => SupportedVerbNames;

    /// <summary>
    /// 本场<b>不接位移目的地</b>：站位是摆进来的输入常量，不是技能的输出。
    /// 带第 4 段的 <c>CastAbility</c> 打到这一侧当场抛，不静默丢掉那一格（「桥只转发」）。
    /// </summary>
    bool IAbilityVerbSink.AcceptsCastDestination => false;

    bool IAbilityVerbSink.Cast(string unitId, string abilityId, string? targetId, (double FromX, double ToX)? move)
        => _stepper.TryActivate(unitId, abilityId, targetId, move);

    bool IAbilityVerbSink.Cancel(string unitId, string abilityId) => _stepper.InjectCancel(unitId, abilityId);

    void IAbilityVerbSink.ApplyEffect(string targetId, string effectId, long? durationTicks, string? sourceId)
        => _stepper.InjectEffect(targetId, effectId, durationTicks, sourceId);

    void IAbilityVerbSink.RemoveEffect(string targetId, string effectId, int stacks)
        => _stepper.InjectRemoveEffect(targetId, effectId, stacks);

    void IAbilityVerbSink.SetAttribute(string targetId, string attribute, double value, string mode,
        string[]? requirementTags)
        => _stepper.InjectModifier(targetId, attribute, Enum.Parse<ModifierMode>(mode, ignoreCase: true), value,
            requirementTags: requirementTags);

    void IAbilityVerbSink.SetBase(string targetId, string attribute, double value)
        => _stepper.InjectSetBase(targetId, attribute, value);

    void IAbilityVerbSink.ClearCooldown(string unitId, string abilityId)
        => _stepper.InjectClearCooldown(unitId, abilityId);

    bool IAbilityVerbSink.SetStepsRemaining(string unitId, string abilityId, long steps)
        => _stepper.InjectSetStepsRemaining(unitId, abilityId, steps);

    /// <summary>
    /// 走 <paramref name="dx"/> 米：<b>激活检查问层、坐标写场子</b>——
    /// 激活检查（定身挡不挡）那份实现在层里，场子过了就把自己那份摆位挪过去。
    /// 动词那一格只带一个标量（<c>单位Id|走几米</c>，次序在 <see cref="AbilityVerbs"/>），所以走的是
    /// <b>X 那一根轴</b>、Z 不动；场子不替这条动词猜别的方向（「桥只转发」：缺了当场抛，不许兜）。
    /// </summary>
    bool IAbilityVerbSink.Move(string unitId, double dx)
    {
        if (!_stepper.TryMove(unitId, dx))
        {
            return false;
        }

        var point = PointOf(unitId);
        _posByUnit[unitId] = (point.X + dx, point.Z);
        return true;
    }

    private double ReadCurrent(AbilitySet unit, string name)
        => unit.Current.TryGetValue(name, out var value) ? value : 0;
}
