#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace DemoFactory.GugaFramework.Abilities;

/// <summary>
/// 属性名与 tag 名由原型侧填进来——公共层一个业务字面量都不出现（已删的设计档「公共层零字面量」）。
/// </summary>
/// <remarks>
/// 下面八格<b>全部没有缺省</b>：七列词表加事件容量，接层时必须逐格显式给。
/// 少了任何一格要么编译不过（词表七列是 <see langword="required"/>），要么当场抛（容量为 0）——
/// 公共层不替原型挑词表，替它挑了就等于「换原型不改公共层」这句话是假的。
/// </remarks>
public sealed class AbilityLayerConfig
{
    /// <summary>生命值的属性名（扣血与判死读它）。</summary>
    public required string HealthAttribute { get; init; }

    /// <summary>承伤倍率的属性名；单位没有这条属性时按 1.0 处理。</summary>
    public required string DamageTakenAttribute { get; init; }

    /// <summary>冷却时长的属性名前缀：<c>前缀 + 技能Id</c> 上的派生值就是倍率（N-7）。</summary>
    public required string CooldownAttributePrefix { get; init; }

    /// <summary>定身 tag：挂着它，S5 的位移问一次就挡住（N-4，激活检查的第二处）。</summary>
    public required string RootedTag { get; init; }

    /// <summary>
    /// 抵挡 tag：挂着它（还剩层数），S0 扣血落地<b>前</b>问一次就吃掉这一次伤害并消耗一层
    /// （issue 08 取候选 1，已删的设计档——激活检查的第三处问，问的就是上面两处同一份实现）。
    /// </summary>
    public required string DamageBlockTag { get; init; }

    /// <summary>
    /// 事件触发的订阅键之一：承伤那一发（含被倍率归零那一档）。效果表
    /// <c>TriggeredEffects</c> 按它订阅，层里不写死任何一个字面量（ticket 10）。
    /// </summary>
    public required string DamageTakenEvent { get; init; }

    /// <summary>
    /// 事件触发的订阅键之二：一条效果落地（<c>ConsumeTriggers</c> 的第二个调用点）。
    /// 同一机制的第二把键，与上面那把一样由宿主给。
    /// </summary>
    public required string EffectAppliedEvent { get; init; }

    /// <summary>
    /// 事件流环形容量（条数）。<b>公共层不给默认值</b>——缺了这一格，接层时
    /// <see cref="AbilityEventLog.Capacity"/> 当场抛，不兜底：容量是宿主按观测窗口定的，
    /// 替它兜一个数等于替它选了观测口径。
    /// </summary>
    public int EventCapacity { get; init; }

    /// <summary>
    /// 零前摇技能的 commit 落点 —— <b>宿主的计时口径，由宿主钉住自己那一档</b>。
    /// 缺省 <c>false</c>：按下之后等下一步的 S3 才过 commit 点；设 <c>true</c>：按下那一步就过。
    ///
    /// <para><b>为什么这一步要由宿主选</b>：层的冷却从 commit 起算，而宿主那把尺从按下起算
    /// （世界的出手间隔 = 这一次按下到下一次按下）。两档只差一步，但这一步决定
    /// 「技能行里那个数是不是卡上那个数」：设 <c>true</c> 时两把尺同起点，技能行的冷却直接写卡上那个数；
    /// 缺省时两把尺差一步，想让世界按卡上的间隔出手，技能行的冷却就得写「卡上数减一」。</para>
    ///
    /// <para>这一档由各宿主自己定，两种口径都成立：卡上的间隔 == 真实出手间隔（commit 与按下同一步），
    /// 或到期步 = commit 步 + 时长（commit 在下一步）。
    /// 前摇非零的技能两档都不受影响 —— 它们本来就要等前摇。</para>
    /// </summary>
    public bool InstantCommitOnPressStep { get; init; }
}

/// <summary>技能表与效果表，由原型侧提供（公共层不认识任何一条具体技能）。</summary>
public sealed class AbilityTables
{
    private readonly Dictionary<string, AbilityDef> _abilities = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EffectDef> _effects = new(StringComparer.Ordinal);

    public AbilityTables Register(AbilityDef ability)
    {
        if (!_abilities.TryAdd(ability.Id, ability))
        {
            throw new KeyError($"技能重名 | {ability.Id}");
        }

        return this;
    }

    public AbilityTables Register(EffectDef effect)
    {
        if (!_effects.TryAdd(effect.Id, effect))
        {
            throw new KeyError($"效果重名 | {effect.Id}");
        }

        return this;
    }

    public AbilityDef Ability(string id) => _abilities.TryGetValue(id, out var a)
        ? a
        : throw new KeyError($"技能未注册 | {id} | 已注册 [{string.Join(",", _abilities.Keys)}]");

    public EffectDef Effect(string id) => _effects.TryGetValue(id, out var e)
        ? e
        : throw new KeyError($"效果未注册 | {id} | 已注册 [{string.Join(",", _effects.Keys)}]");

    public bool HasEffect(string id) => _effects.ContainsKey(id);
}

/// <summary>
/// 每步执行序 S0~S5 的编排器。它不产生时间、不决定何时推进——那是宿主的时钟的事；
/// 它只负责「把这一层的账每步重算一遍」，所以暂停、单步、倍率、重放、分支一起成立。
/// </summary>
public sealed class AbilityStepper
{
    private readonly AbilityWorld _world;
    private readonly IAbilityHost _host;
    private readonly CalcRegistry _calc;
    private readonly AbilityTables _tables;
    private readonly AbilityLayerConfig _config;

    /// <summary>取消请求排在下一步 S3 开头处理：同一步里取消与超时竞争，段序定胜负（3 节约束 3）。</summary>
    private readonly List<(string UnitId, IReadOnlyList<string> Tags, string Cause)> _cancelQueue = new();

    /// <summary>本次激活登记的目标，只在同一步内有效（S3 与 S4 之间传递）。</summary>
    private IReadOnlyList<string> _currentTargets = Array.Empty<string>();

    public AbilityStepper(AbilityWorld world, IAbilityHost host, CalcRegistry calc,
        AbilityTables tables, AbilityLayerConfig config)
    {
        _world = world;
        _host = host;
        _calc = calc;
        _tables = tables;
        _config = config ?? throw new ArgumentNullException(nameof(config),
            "接层必须递一份 AbilityLayerConfig：五列词表公共层一个都不替你挑，缺了当场抛，不兜默认值");
        _world.Events.Capacity = _config.EventCapacity;
        foreach (var unitId in _world.UnitOrder)
        {
            PrimeAttributes(_world.Units[unitId]);
        }

        Recalc();
    }

    public AbilityWorld World => _world;

    public AbilityLayerConfig Config => _config;

    public CalcRegistry Calc => _calc;

    public AbilityTables Tables => _tables;

    /// <summary>自动战斗的意图口：每步 S4 问一发，同步返回、不带跨步状态（对齐「树同步返回」）。</summary>
    public Func<string, (string? AbilityId, string? TargetId)>? IntentPolicy { get; set; }

    /// <summary>
    /// <b>距离的第二条路：宿主注册的谓词</b>（单位身上不再有坐标，公共层自己算不出距离）。
    /// 递 <c>(fromId, toId) → 米</c>；层在射程激活检查、光环名单与距离曲线三处问它。
    /// 没注册（null）时，只有<b>调用点显式递了标量</b>的那几条路走得通，其余当场抛
    /// <see cref="KeyError"/>——不兜 0（「桥只转发」：缺了当场抛，兜 0 等于静默判成贴脸）。
    /// 开阔场那种「距离要现算」的宿主注册这一条；贴脸场距离是常数的那种，调用点递标量就够。
    /// </summary>
    public Func<string, string, double>? DistanceOf
    {
        get => _distanceOf;
        set => _distanceOf = value;
    }

    private Func<string, string, double>? _distanceOf;

    /// <summary>问一次距离：先看宿主注册的那条谓词，没注册就当场抛（不兜 0，理由同 <see cref="DistanceOf"/>）。</summary>
    public double Distance(string fromId, string toId)
        => DistanceOf is { } of
            ? of(fromId, toId)
            : throw new KeyError(
                $"问距离但宿主没给来源 | {fromId} -> {toId}（注册 AbilityStepper.DistanceOf，或在调用点递标量）");

    /// <summary>推进一步：六段按固定顺序跑完，段序就是契约。</summary>
    public void AdvanceStep()
    {
        _world.Tick++;
        SettleDuePending();
        TickTimers();
        Recalc();
        ProgressAbilities();
        RunDecisionPhase();
        Recalc();
    }

    /// <summary>把某单位的全部属性快照念一遍给 S2 —— 新建的技能栏还没有 Current，先补一次派生。</summary>
    private void PrimeAttributes(AbilitySet unit)
    {
        foreach (var name in unit.Base.Keys.ToList())
        {
            unit.Current[name] = Math.Round(unit.Base[name], 2, MidpointRounding.AwayFromZero);
        }
    }

    // ---- S0 到期结算 ----------------------------------------------------------

    private void SettleDuePending()
    {
        foreach (var unitId in _world.UnitOrder)
        {
            var unit = _world.Units[unitId];
            var due = unit.Pendings.Where(p => p.ExecuteAtTick <= _world.Tick)
                .OrderBy(p => p.Seq)
                .ToList();
            foreach (var pending in due)
            {
                unit.Pendings.Remove(pending);
                Settle(pending);
            }
        }
    }

    private void Settle(PendingSettlement pending)
    {
        var def = _tables.Effect(pending.EffectId);
        var source = _world.Units[pending.SourceId];
        var targets = pending.TargetIds.Count == 0 ? new[] { pending.SourceId } : pending.TargetIds;

        foreach (var targetId in targets)
        {
            var target = _world.Units.TryGetValue(targetId, out var t) ? t : null;

            // 目标失效在这一刻现解：落地那一步人不在了就作废，并把为什么作废念出来。
            if (target is null || !IsAlive(target))
            {
                AddCounter(source, "void_count", 1);
                Emit(pending.SourceId, new EffectMissed(pending.EffectId, targetId));
                continue;
            }

            // 曲线按「落地这一格的距离」折，不按登记那一格的——目标会走开，衰减也跟着变。
            var landed = ApplyCurve(def, pending.CalcValue, source, target);
            ApplyModifiersAsOneShot(target, pending.SourceId, def, landed, stacks: 1);
            // 取消请求「一份实现、两处问」：即时施加那一条路（ApplyEffect）与延迟落地这同一条路
            // 都得把 CancelMatchingTags 交给同一个取消队列。第二批实跑查出来的缺口：
            // 只写在 ApplyEffect 上时，带 CancelMatchingTags 的延迟那一发（rider.joust 第二段）
            // 会静默不打断——技能层两条路对同一条数据给出两种结果，正是 A1 要防的那种漂移。
            if (def.CancelMatchingTags.Count > 0)
            {
                QueueCancels(target, def);
            }
            Emit(pending.SourceId, new EffectLanded(pending.EffectId, targetId, pending.Seq, landed));

            if (pending.Move is { } move)
            {
                // 位移落的是<b>走路那一个兵</b>（冲锋是我方扑出去），不是被打的那个。
                // 两个端点都是宿主按下那一刻递进来的标量，层只把它们发成事件；
                // <b>世界坐标的写归宿主</b>（单挑场 12 节 待确认项 5 已确认），层不落任何一笔坐标。
                Emit(pending.SourceId, new UnitMoved(move.ToX, move.FromX));
            }

            RefreshCurrent();
        }
    }

    // ---- S1 计时 --------------------------------------------------------------

    private void TickTimers()
    {
        foreach (var unitId in _world.UnitOrder)
        {
            var unit = _world.Units[unitId];

            foreach (var effect in unit.Effects
                         .Where(e => e.ExpiresAtTick.HasValue && e.ExpiresAtTick.Value <= _world.Tick)
                         .OrderBy(e => e.EffectId, StringComparer.Ordinal)
                         .ToList())
            {
                unit.Effects.Remove(effect);
                DetachModifiers(unit, effect.EffectId);
                Emit(unitId, new EffectExpired(effect.EffectId));
            }

            foreach (var loose in unit.Modifiers
                         .Where(m => m.ExpiresAtTick.HasValue && m.ExpiresAtTick.Value <= _world.Tick
                             && m.SourceEffectId is null)
                         .OrderBy(m => m.Seq)
                         .ToList())
            {
                unit.Modifiers.Remove(loose);
                Emit(unitId, new ModifierExpiredOnBody(loose.Target, loose.Mode));
            }

            // 冷却不比递减、比到期步：转完就在 S1 回 Ready，S4 这一步才问得到（3 节约束 5）。
            foreach (var ability in unit.Abilities.Values.OrderBy(a => a.Id, StringComparer.Ordinal))
            {
                if (ability.Phase == AbilityPhase.Cooling
                    && ability.CooldownEndsAtTick.HasValue
                    && ability.CooldownEndsAtTick.Value <= _world.Tick)
                {
                    ability.Phase = AbilityPhase.Ready;
                    ability.CooldownEndsAtTick = null;
                    ability.RejectedReason = AbilityRejectReason.None;
                    Emit(unitId, new CooldownReady(ability.Id));
                }
            }
        }
    }

    // ---- S2 重算：tag 两段汇总 + modifier 求值 + 上下界 ------------------------

    /// <summary>
    /// tag 计数与 <see cref="AbilitySet.Current"/> 的唯一写入处。判定与显示读同一份值，
    /// 同一步多单位遍历时全部单位读的是这一步结束时的同一份（3 节约束 4）。
    /// </summary>
    private void Recalc()
    {
        RecalcTags();
        RefreshCurrent();
    }

    private void RecalcTags()
    {
        foreach (var unitId in _world.UnitOrder)
        {
            var unit = _world.Units[unitId];
            unit.TagCounts.Clear();

            foreach (var ability in unit.Abilities.Values.OrderBy(a => a.Id, StringComparer.Ordinal))
            {
                if (ability.Phase is AbilityPhase.Pre or AbilityPhase.Execute or AbilityPhase.Post)
                {
                    foreach (var tag in _tables.Ability(ability.Id).GrantedTagsWhileActive)
                    {
                        AddTag(unit, tag, 1);
                    }
                }
            }

            foreach (var effect in unit.Effects.OrderBy(e => e.EffectId, StringComparer.Ordinal))
            {
                foreach (var tag in _tables.Effect(effect.EffectId).GrantedTags)
                {
                    AddTag(unit, tag, effect.Stacks);
                }
            }
        }
    }

    private void RefreshCurrent()
    {
        foreach (var unitId in _world.UnitOrder)
        {
            var unit = _world.Units[unitId];
            unit.Current.Clear();

            var names = unit.Base.Keys
                .Concat(unit.Modifiers.Select(m => m.Target))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            foreach (var name in names)
            {
                var applicable = unit.Modifiers
                    .Where(m => m.Target == name && TagsSatisfied(unit, m.RequirementTags))
                    .OrderBy(m => m.AppliedAtTick)
                    .ThenBy(m => m.Seq)
                    .ToList();

                var addSum = applicable.Where(m => m.Mode == ModifierMode.Add).Sum(m => m.Magnitude);
                var multProduct = applicable.Where(m => m.Mode == ModifierMode.Mult)
                    .Aggregate(1.0, (acc, m) => acc * m.Magnitude);
                var lastOverride = applicable.LastOrDefault(m => m.Mode == ModifierMode.Override);

                var baseValue = unit.Base.TryGetValue(name, out var b) ? b : 0;

                // 顺序定死（4.3）：三种 mode 先汇成一个值，界在最后——Override 顶不开 clamp。
                var raw = lastOverride is not null
                    ? lastOverride.Magnitude
                    : (baseValue + addSum) * multProduct;

                var min = unit.Min.TryGetValue(name, out var lo) ? lo : double.MinValue;
                var max = unit.Max.TryGetValue(name, out var hi) ? hi : double.MaxValue;
                unit.Current[name] = Math.Round(Math.Min(Math.Max(raw, min), max), 2,
                    MidpointRounding.AwayFromZero);
            }
        }
    }

    // ---- S3 技能推进 ----------------------------------------------------------

    private void ProgressAbilities()
    {
        ProcessCancelQueue();

        foreach (var unitId in _world.UnitOrder)
        {
            var unit = _world.Units[unitId];

            foreach (var effect in unit.Effects
                         .Where(e => _tables.Effect(e.EffectId).PeriodTicks > 0)
                         .OrderBy(e => e.EffectId, StringComparer.Ordinal)
                         .ToList())
            {
                var def = _tables.Effect(effect.EffectId);
                if (_world.Tick - effect.LastPeriodTick >= def.PeriodTicks)
                {
                    effect.LastPeriodTick += def.PeriodTicks;

                    // 回充那一跳（N-15，已删的设计档：不加列）。只授予 tag 的常驻周期效果，它这一跳做的是
                    // 「把层数加回去、封顶 MaxStacks」，不是再打一跳——与快照那格 charges= 读的是同一处。
                    if (def.IsChargeCarrier)
                    {
                        if (effect.Stacks < Math.Max(1, def.MaxStacks))
                        {
                            effect.Stacks++;
                            AddCounter(unit, "charge_recovered", 1);
                            Emit(unitId, new ChargeRefilled(effect.EffectId, effect.Stacks));
                        }
                        else
                        {
                            AddCounter(unit, "charge_recharge_capped", 1);
                        }

                        Recalc();
                        continue;
                    }

                    var value = EvaluateChain(unit.Current, unit, def);
                    ApplyModifiersAsOneShot(unit, unitId, def, value, effect.Stacks);
                    Emit(unitId, new PeriodTicked(effect.EffectId, effect.Stacks));
                    RefreshCurrent();
                }
            }

            foreach (var ability in unit.Abilities.Values.OrderBy(a => a.Id, StringComparer.Ordinal).ToList())
            {
                if (ability.Phase is AbilityPhase.Ready or AbilityPhase.Cooling)
                {
                    continue;
                }

                var def = _tables.Ability(ability.Id);
                if (ability.StepsRemaining > 0)
                {
                    ability.StepsRemaining--;
                }

                if (ability.Phase == AbilityPhase.Pre)
                {
                    if (ability.StepsRemaining == 0)
                    {
                        Commit(unit, ability, def);
                    }

                    continue;
                }

                if (ability.StepsRemaining == 0)
                {
                    if (ability.CooldownEndsAtTick.HasValue && ability.CooldownEndsAtTick.Value > _world.Tick)
                    {
                        ability.Phase = AbilityPhase.Cooling;
                    }
                    else
                    {
                        ability.Phase = AbilityPhase.Ready;
                        ability.RejectedReason = AbilityRejectReason.None;
                        Emit(unitId, new AbilityCompleted(ability.Id));
                    }
                }
            }
        }
    }

    private void ProcessCancelQueue()
    {
        foreach (var (unitId, tags, cause) in _cancelQueue)
        {
            if (!_world.Units.TryGetValue(unitId, out var unit))
            {
                continue;
            }

            foreach (var ability in unit.Abilities.Values.OrderBy(a => a.Id, StringComparer.Ordinal))
            {
                if (ability.Phase is AbilityPhase.Ready or AbilityPhase.Cooling)
                {
                    continue;
                }

                var def = _tables.Ability(ability.Id);
                if (!def.GrantedTagsWhileActive.Any(used => tags.Any(t => TagMatches(used, t))))
                {
                    continue;
                }

                var committed = ability.CommitTick.HasValue;
                ability.Phase = AbilityPhase.Ready;
                ability.StepsRemaining = 0;
                ability.RejectedReason = AbilityRejectReason.Interrupted;

                // 未到 commit 即撤 = 一分不扣；已过 commit = 照扣、冷却也不还（warrior.rift 就这么算）。
                Emit(unitId, new AbilityInterrupted(ability.Id, cause, committed));
            }
        }

        _cancelQueue.Clear();
    }

    private void Commit(AbilitySet unit, AbilityOnBody ability, AbilityDef def)
    {
        ability.CommitTick = _world.Tick;

        if (def.CostAttribute.Length > 0)
        {
            var have = ReadCurrent(unit, def.CostAttribute);
            unit.Base[def.CostAttribute] = have - def.CostAmount;
            Emit(unit.UnitId, new CostPaid(def.CostAttribute, def.CostAmount));
        }

        if (def.CooldownTicks > 0)
        {
            // N-7：冷却时长是一条普通 attribute 的派生值，commit 那一步算定到期步，之后只比步数。
            var multiplier = ReadOptionalCurrent(unit, _config.CooldownAttributePrefix + def.Id, 1.0);
            var scaled = (long)Math.Round(def.CooldownTicks * multiplier, MidpointRounding.AwayFromZero);
            ability.CooldownEndsAtTick = _world.Tick + Math.Max(1, scaled);
            ability.Phase = AbilityPhase.Cooling;
        }

        // Cost 与来源属性快照：抄的是这一刻的派生值，之后施法者怎么变都不影响已在路上的那一发。
        var sourceSnapshot = unit.Current.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        var targetIds = ability.TargetId is null ? _currentTargets : new[] { ability.TargetId };
        var target = TargetFor(ability, unit);

        foreach (var effectId in def.EffectIds)
        {
            var effectDef = _tables.Effect(effectId);
            var value = EvaluateChain(sourceSnapshot, target ?? unit, effectDef);

            if (effectDef.DelayTicks > 0)
            {
                // 位移那一格（rider.charge）：起点与目的地都是按下那一刻宿主递进来的意图，
                // 抄在 Ability.Move 里，到这一步才长成 Pending.Move{FromX, ToX}；
                // 已行走距离是那两个标量之差、在此刻就定了，所以 CurveAxis="travelled" 的曲线
                // 在 commit 折好，落地那一步不再折第二次。
                var move = ability.Move;
                var travelled = move is { } m ? Math.Abs(m.ToX - m.FromX) : 0;
                var carried = effectDef.CurveAxis == "travelled" && effectDef.HasCurve
                    ? value * SampleCurve(effectDef.CurveXY!, travelled)
                    : value;
                unit.Pendings.Add(new PendingSettlement
                {
                    Seq = _world.NextSeq++,
                    ExecuteAtTick = _world.Tick + effectDef.DelayTicks,
                    SourceId = unit.UnitId,
                    EffectId = effectId,
                    TargetIds = targetIds,
                    SourceAttrsSnapshot = sourceSnapshot,
                    // 距离轴（飞行物）不在这里折：按「落地那一步的距离」结算，此刻抄下的只有链的裸值。
                    // 唯一的例外是上面那条 travelled 轴——位移的已行走距离在按下那一刻就是定值。
                    CalcValue = carried,
                    Move = move,
                });
                var landingTick = _world.Tick + effectDef.DelayTicks;
                if (move is { } landed)
                {
                    Emit(unit.UnitId, new SettlementRegisteredWithMove(
                        effectId, landingTick, carried, landed.FromX, landed.ToX));
                }
                else
                {
                    Emit(unit.UnitId, new SettlementRegistered(effectId, landingTick, carried));
                }
                continue;
            }

            if (target is not null)
            {
                ApplyEffect(target, unit.UnitId, effectDef, ApplyCurve(effectDef, value, unit, target));
            }
        }

        // 位移的意图是一次性的：登记完就清掉，下一次按下不带上一发的目的地。
        ability.Move = null;

        if (ability.Phase != AbilityPhase.Cooling)
        {
            ability.Phase = def.PostSteps > 0 ? AbilityPhase.Post : AbilityPhase.Ready;
            ability.StepsRemaining = def.PostSteps > 0 ? def.PostSteps : 0;
        }
        else
        {
            ability.Phase = def.PostSteps > 0 ? AbilityPhase.Post : AbilityPhase.Cooling;
            ability.StepsRemaining = def.PostSteps;
        }

        Emit(unit.UnitId, new AbilityCommitted(def.Id));
        Recalc();
    }

    // ---- S4 决策与入口 --------------------------------------------------------

    private void RunDecisionPhase()
    {
        DrainQueuedInputs();

        if (IntentPolicy is null)
        {
            return;
        }

        foreach (var unitId in _world.UnitOrder)
        {
            if (!IsAlive(_world.Units[unitId]))
            {
                continue;
            }

            var (abilityId, targetId) = IntentPolicy(unitId);
            if (abilityId is not null)
            {
                TryActivate(unitId, abilityId, targetId ?? FirstEnemy(unitId));
            }
        }
    }

    /// <summary>
    /// 排队补放（N-16）：冷却转好的那一步，S1 已把 <c>Phase</c> 放回 <c>Ready</c>，这一发排队的输入
    /// 在<b>同一个 S4 入口</b>（<see cref="TryActivate"/>）兑现——不开第二条接受路径，
    /// 否则「玩家按的」与「补放的」会漂成两套激活检查。排在问政策之前，所以补放那一发起算前摇的步号
    /// 与同一步手按的一模一样。
    /// <b>技能没转好之前队一直留着</b>（<c>Phase != Ready</c> 就跳过）：不然每一步都会「补放→被冷却拒→
    /// 重新排队」，行为流被同一句话灌满，而「排着一个」这件事也变成了「每步重试」。
    /// 真补放那一次读一次就清零：又被拒（目标走远了）也认栽，不留着重试。
    /// </summary>
    private void DrainQueuedInputs()
    {
        foreach (var unitId in _world.UnitOrder)
        {
            var unit = _world.Units[unitId];
            foreach (var ability in unit.Abilities.Values
                         .Where(a => a.QueuedInput && a.Phase == AbilityPhase.Ready)
                         .OrderBy(a => a.Id, StringComparer.Ordinal)
                         .ToList())
            {
                ability.QueuedInput = false;
                var targetId = ability.TargetId ?? FirstEnemy(unitId);
                if (targetId is not null)
                {
                    Emit(unitId, new QueuedCastTakenAt(ability.Id, targetId));
                }
                else
                {
                    Emit(unitId, new QueuedCastTaken(ability.Id));
                }
                // 排队时抄下的目的地跟着补放走（同一条 S4 入口，不另开第二条路）
                TryActivate(unitId, ability.Id, targetId, move: ability.Move);
            }
        }
    }

    private string? FirstEnemy(string unitId)
        => _world.UnitOrder.FirstOrDefault(id => id != unitId && IsAlive(_world.Units[id]));

    /// <summary>玩家按下与自动战斗走同一个入口（「桥只转发」：注入路径与玩家路径不许漂）。</summary>
    public bool TryActivate(string unitId, string abilityId, string? targetId)
        => TryActivate(unitId, abilityId, targetId, out _);

    /// <summary>
    /// 带<b>位移意图</b>的那一发（<c>rider.charge</c>）：起点与目的地同属「这一步要做什么」，
    /// 所以从同一个入口进来（「桥只转发」：注入的路径与玩家的路径不许漂成两回事）。
    /// </summary>
    public bool TryActivate(string unitId, string abilityId, string? targetId, (double FromX, double ToX)? move)
        => TryActivate(unitId, abilityId, targetId, out _, move);

    /// <summary>
    /// 「<b>宿主递标量</b>」那条路：距离由调用点直接给一个数，不问 <see cref="DistanceOf"/>。
    /// 贴脸场那种「距离是常数」的场子走这一发——<paramref name="distanceMeters"/> 就是那把常数尺。
    /// </summary>
    public bool TryActivate(string unitId, string abilityId, string? targetId, double distanceMeters)
        => TryActivate(unitId, abilityId, targetId, out _, move: null, distanceMeters);

    public bool TryActivate(string unitId, string abilityId, string? targetId,
        out AbilityRejectReason reason, (double FromX, double ToX)? move = null, double? distanceMeters = null)
    {
        var unit = _world.Units[unitId];
        var ability = GetOrCreateAbility(unit, abilityId);
        var def = _tables.Ability(abilityId);
        reason = AbilityRejectReason.None;

        if (!IsAlive(unit))
        {
            reason = AbilityRejectReason.Dead;
        }
        else if (ability.Phase != AbilityPhase.Ready)
        {
            reason = ability.Phase == AbilityPhase.Cooling ? AbilityRejectReason.Cooldown
                : AbilityRejectReason.Busy;
        }
        else if (targetId is not null && !_host.Exists(targetId))
        {
            // 目标在激活检查这一刻就不在册了（建筑被拆、单位退场）：这是「这一位不存在」，
            // 不是「打不着」——两件事的理由分开。排在射程之前：目标都不在时那一问没有意义。
            reason = AbilityRejectReason.TargetGone;
        }
        else if (!TagsSatisfied(unit, def.RequirementTags))
        {
            reason = AbilityRejectReason.MissingTag;
        }
        else if (def.BlockedTags.Any(tag => HasTag(unit, tag)))
        {
            reason = AbilityRejectReason.BlockedByTag;
        }
        else if (targetId is not null && def.RangeMeters > 0
                 && DistanceTo(unitId, targetId, distanceMeters) > def.RangeMeters)
        {
            reason = AbilityRejectReason.NotInRange;
        }
        else if (def.CostAttribute.Length > 0 && def.CostAmount > 0
                 && ReadCurrent(unit, def.CostAttribute) < def.CostAmount)
        {
            reason = AbilityRejectReason.NoCost;
        }
        else if (def.MaxCharges > 0 && ability.ChargesLeft <= 0)
        {
            reason = AbilityRejectReason.NoCharge;
        }

        if (reason != AbilityRejectReason.None)
        {
            ability.RejectedReason = reason;

            // 排队释放（N-16）：冷却中按下的这一发不丢，记成「排一个」（上限 1），
            // 要打的谁抄在既有的 TargetId 那一列里——不为这一格再开一列。
            var queued = false;
            if (reason == AbilityRejectReason.Cooldown)
            {
                ability.QueuedInput = true;
                ability.TargetId = targetId ?? ability.TargetId;
                ability.Move = move ?? ability.Move;   // 排队也带上这一步的位移意图，补放时不丢
                queued = true;
            }

            Emit(unitId, queued
                ? new AbilityRejectedAndQueued(abilityId, reason)
                : new AbilityRejected(abilityId, reason));
            return false;
        }

        _currentTargets = targetId is null ? Array.Empty<string>() : new[] { targetId };
        ability.Phase = AbilityPhase.Pre;
        ability.StepsRemaining = def.PreSteps;
        ability.TargetId = targetId;
        ability.Move = move;
        ability.CommitTick = null;
        ability.RejectedReason = AbilityRejectReason.None;
        if (def.MaxCharges > 0)
        {
            ability.ChargesLeft--;
        }

        Emit(unitId, targetId is not null
            ? new AbilityActivatedAt(abilityId, targetId)
            : new AbilityActivated(abilityId));
        Recalc();

        // 零前摇那一档：宿主选了「同一步过 commit」时，按下这一步就过，不等下一步的 S3。
        //
        // 为什么要把这一步压掉：层里的冷却从 commit 起算，而宿主那把尺从按下起算
        // （世界的出手间隔就是「这一次按下到下一次按下」）。commit 晚一步 ⇒ 两把尺差一步 ⇒
        // 技能行的冷却要么写「卡上数减一」（表里的数因此与卡上不同），要么整局慢一步。
        // 压掉之后两把尺同起点，技能行的冷却直接写卡上那个数。
        // 这一档由 AbilityLayerConfig.InstantCommitOnPressStep 选，缺省不压（有一侧按
        // 「到期步 = commit 步 + 时长」写的用例钉着旧口径）。
        // 前摇非零的技能不走这里：它们本来就要等前摇，行为一字不变。
        //
        // 段序没有动：Commit 仍是唯一那一处写 CommitTick 的代码，只是零前摇这一档在同一步兑现。
        // 它末尾自带一次 Recalc，所以本调用排在上面那一句之后 —— 免得同一步把派生值算两遍。
        if (def.PreSteps == 0 && _config.InstantCommitOnPressStep)
        {
            Commit(unit, ability, def);
        }

        return true;
    }

    private AbilityOnBody GetOrCreateAbility(AbilitySet unit, string abilityId)
    {
        if (unit.Abilities.TryGetValue(abilityId, out var existing))
        {
            return existing;
        }

        var def = _tables.Ability(abilityId);
        var created = new AbilityOnBody
        {
            Id = abilityId,
            ChargesLeft = def.MaxCharges > 0 ? def.MaxCharges : 0,
        };
        unit.Abilities[abilityId] = created;
        return created;
    }

    // ---- S5 移动与选目标：激活检查的第二处（N-4）---------------------------------

    /// <summary>定身挂着就走不动。S4 入口与 S5 位移问的是同一件事、同一份实现。</summary>
    public bool CanMove(string unitId) => !HasTag(_world.Units[unitId], _config.RootedTag);

    /// <summary>
    /// 位移那一格只剩<b>激活检查</b>：定身挂着就问一次「走不走得成」，成了由宿主把坐标写进自己那份世界。
    /// 层不再替谁挪坐标——单位身上那根坐标已经从公共层拿掉，这里也就没有可写的格子。
    /// 返回 true = 激活检查放行（宿主接着写自己那份坐标）；false = 被定身挡住，账上留一行 <c>move_blocked</c>。
    /// </summary>
    public bool TryMove(string unitId, double dx)
    {
        if (!CanMove(unitId))
        {
            Emit(unitId, new MoveBlocked(dx));
            return false;
        }

        return true;
    }

    /// <summary>
    /// 距离那把尺：<b>调用点递的标量优先，没递才问宿主注册的那条谓词</b>，两条都没有就当场抛。
    /// 两处问的是同一份实现（射程激活检查、光环名单、距离曲线都走这一发），所以「谁算的距离」只有一个答案。
    /// </summary>
    private double DistanceTo(string fromId, string toId, double? givenMeters)
        => givenMeters ?? Distance(fromId, toId);

    // ---- S5 的数据算：距离与角度这两把尺（N-13 / N-14）------------------------

    /// <summary>
    /// 光环那一格的数据算：<b>谁在 <paramref name="rangeMeters"/> 之内</b>。只算、不写——
    /// 跨边界时由宿主发既有的 <see cref="InjectEffect"/> / <see cref="InjectRemoveEffect"/> 重应用，
    /// 公共层不给 <c>Modifier</c> 加「距离条件」这一列，S2 全程不问活世界（已删的设计档的 N-13）。
    /// 距离从宿主来（<see cref="DistanceOf"/>，或逐个候选递标量的那一发），层自己不持坐标；
    /// 二维的扇形判定走 <see cref="InsideSector"/>，输入全是标量。
    /// </summary>
    public List<string> UnitsInRange(string unitId, double rangeMeters)
        => UnitsInRange(unitId, rangeMeters, distanceMeters: null);

    /// <summary>
    /// 「<b>宿主递标量</b>」那一发：<paramref name="distanceMeters"/> 给了就用它当「每个候选的距离」，
    /// 不问 <see cref="DistanceOf"/>。贴脸场那种距离是常数的情形走这一发。
    /// </summary>
    public List<string> UnitsInRange(string unitId, double rangeMeters, double? distanceMeters)
    {
        var limit = Math.Round(rangeMeters, 2, MidpointRounding.AwayFromZero);
        var hits = new List<string>();
        foreach (var other in _world.UnitOrder)
        {
            if (other == unitId || !IsAlive(_world.Units[other]))
            {
                continue;
            }

            if (Math.Round(DistanceTo(unitId, other, distanceMeters), 2, MidpointRounding.AwayFromZero) <= limit)
            {
                hits.Add(other);
            }
        }

        return hits;
    }

    /// <summary>
    /// 一发扇形判定（<c>tank.slam</c> 的「距离 ≤ 3 米且在正前 120° 内」）：<b>怎么算命中在这里，原型不另判一遍</b>
    /// （N-14，与射程同一格、一份实现）。输入全是宿主交进来的标量：两个点、一支朝向向量、
    /// 半径与全角——公共层不持活对象、不查引擎（A1）。
    /// <c>rangeMeters &lt;= 0</c> = 不限距离；<c>coneDegrees &lt;= 0</c> = 不限角度（那就是个圆）；
    /// 朝向是零向量 = 这一发不看角度（宁可放行，不静默判负）。
    /// 舍入只在界上这一次（3 节约束 2）；角度比的是 <c>cos²</c>，不再开第二次方。
    /// </summary>
    public static bool InsideSector(double fromX, double fromZ, double toX, double toZ,
        double facingX, double facingZ, double rangeMeters, double coneDegrees)
    {
        var dx = toX - fromX;
        var dz = toZ - fromZ;
        var d2 = dx * dx + dz * dz;

        if (rangeMeters > 0
            && Math.Round(Math.Sqrt(d2), 2, MidpointRounding.AwayFromZero)
               > Math.Round(rangeMeters, 2, MidpointRounding.AwayFromZero))
        {
            return false;
        }

        if (coneDegrees <= 0 || d2 == 0)
        {
            return true;
        }

        var facingLength = Math.Sqrt(facingX * facingX + facingZ * facingZ);
        if (facingLength == 0)
        {
            return true;
        }

        var half = Math.Min(360.0, coneDegrees) / 2.0;
        var cosHalf = Math.Cos(half * Math.PI / 180.0);
        var along = (dx * facingX + dz * facingZ) / facingLength;   // = 距离 × cos(夹角)
        return along >= 0 && along * along >= cosHalf * cosHalf * d2;
    }

    /// <summary>
    /// 宿主补记口：一发扇形的命中名单落地成事件（N-13 / N-14「判定在层、跨边界由宿主发动词」的出口那一半）。
    /// 为什么开这一格：怎么算命中（<see cref="InsideSector"/>）与事件的名字和形状都归公共层，
    /// 二维坐标与活对象枚举归宿主——宿主只递标量与 Id 名单，不再往自己技能栏的 <c>Events</c> 里手写事件行。
    /// 名单非空发 <see cref="ShapeHitLanded"/>，空头发 <see cref="ShapeHitMissed"/>（打空要读得出为什么空）。
    /// <b>这里只发事件、不写任何真值</b>（公理 A1/A4：回调与补记口不许写真值，真值仍走注入动词那几条写路径）。
    /// </summary>
    public void RecordShape(string casterId, double radius, double coneDegrees,
        IReadOnlyList<string> hitUnitIds)
    {
        if (hitUnitIds.Count > 0)
        {
            Emit(casterId, new ShapeHitLanded(radius, coneDegrees, hitUnitIds));
            return;
        }

        Emit(casterId, new ShapeHitMissed(radius, coneDegrees));
    }

    // ---- 施加效果：写路径只有这一条 ------------------------------------------

    private AbilitySet? TargetFor(AbilityOnBody ability, AbilitySet caster)
    {
        // 目标是跨步状态：在前摇里瞄着谁，必须从快照里的 Ability.TargetId 读，
        // 不能读「本步记下的那一份」——否则分支重放与单步恢复会把目标丢掉。
        var id = ability.TargetId ?? (_currentTargets.Count > 0 ? _currentTargets[0] : null);
        if (id is null)
        {
            return caster;
        }

        // 目标不在册：激活检查那一问（IAbilityHost.Exists）已经把它挡在 TargetGone 上了，走不到这里。
        // 走到这里说明有人绕开了那个入口 —— 当场抛，不静默回退成施法者
        // （回退的后果是这一发打在施法者自己身上，而账上谁也看不出打错了人）。
        return _world.Units.TryGetValue(id, out var t)
            ? t
            : throw new KeyError(
                $"目标不在层里 | {id}（施法者 {caster.UnitId}）| 激活检查那一问是 IAbilityHost.Exists，别绕开它");
    }

    /// <summary>
    /// 把一条效果带来的 <c>CancelMatchingTags</c> 交给取消队列（S3 开头兑现，见 <see cref="ProcessCancelQueue"/>）。
    /// 即时施加与延迟落地两条路都调这一个，不许各写一份。
    /// </summary>
    private void QueueCancels(AbilitySet target, EffectDef def)
        => _cancelQueue.Add((target.UnitId, def.CancelMatchingTags, def.Id));

    /// <summary>挂效果：免疫、激活检查、叠层、上下架 modifier、登记取消请求与触发都在这一步说清。</summary>
    private void ApplyEffect(AbilitySet target, string sourceId, EffectDef def, double chainValue)
    {
        // 免疫排在激活检查之前：GAS 的 ImmunityTags 就是「即使对方要求挂上，我这里也不落」。
        // 看的是单向的 tag 层级——授予 `cc` 就挡住一切 `cc.*`，反过来不成立。
        if (def.ImmunityTagsOrEmpty.Any(immune => HasTag(target, immune)))
        {
            Emit(target.UnitId, new EffectRefusedByTags(def.Id, def.ImmunityTagsOrEmpty));
            return;
        }

        if (!TagsSatisfied(target, def.RequirementTags)
            || def.BlockedTags.Any(tag => HasTag(target, tag)))
        {
            Emit(target.UnitId, new EffectSkipped(def.Id));
            return;
        }

        if (def.CancelMatchingTags.Count > 0)
        {
            QueueCancels(target, def);
        }

        // N-6：归并键是 `StackKey`，没给键的定义才退回按自己的 Id 找。跨条定义共用一键时，身上那条
        // 实例记的是「第一次挂上去那条」的 Id——谁先挂谁当家，层数与到期都记在它身上，快照那格
        // `charges=` 也跟着照当家那条来读（A80 钉这一格；A60 那条「流血一跳 / 回充一跳」的分界不被归并改写）。
        var existing = target.Effects.FirstOrDefault(e => MergesInto(e, def));

        if (def.Kind == EffectKind.Instant)
        {
            ApplyModifiersAsOneShot(target, sourceId, def, chainValue, stacks: 1);
            Emit(target.UnitId, new EffectAppliedInstantly(def.Id));
            return;
        }

        if (existing is not null)
        {
            var capacity = Math.Max(1, def.MaxStacks);
            if (def.StackPolicy == StackPolicy.Replace)
            {
                target.Effects.Remove(existing);
                // 跨条归并时当家的是先前那条，换掉它就得把它挂的修饰符一起撤（同 Id 时这句与下一句同键，撤两遍无副作用）。
                DetachModifiers(target, existing.EffectId);
                DetachModifiers(target, def.Id);
            }
            else if (existing.Stacks < capacity)
            {
                existing.Stacks++;
                if (def.StackPolicy == StackPolicy.AddStackRefreshDuration && def.DurationTicks > 0)
                {
                    existing.ExpiresAtTick = _world.Tick + def.DurationTicks;
                }

                Emit(target.UnitId, new EffectStacksChanged(def.Id, existing.Stacks));
                AttachModifiers(target, def, sourceId, existing.Stacks, existing.ExpiresAtTick);
                return;
            }
            else
            {
                Emit(target.UnitId, new EffectStackCapped(def.Id, capacity));
                return;
            }
        }

        var instance = new EffectOnBody
        {
            EffectId = def.Id,
            AppliedAtTick = _world.Tick,
            Stacks = 1,
            ExpiresAtTick = def.Kind == EffectKind.Duration && def.DurationTicks > 0
                ? _world.Tick + def.DurationTicks
                : null,
            LastPeriodTick = def.PeriodTicks > 0 ? _world.Tick : long.MinValue,
        };
        target.Effects.Add(instance);
        Emit(target.UnitId, new EffectApplied(def.Id));
        AttachModifiers(target, def, sourceId, instance.Stacks, instance.ExpiresAtTick);
    }

    /// <summary>
    /// 身上这条实例与正要施加的这条定义是不是<b>同一把键</b>（N-6）：给了 <c>StackKey</c> 就按键归并，
    /// 没给键的定义只认自己的 Id。实现只在这里一份——S3 归并、封顶、换档都读它，别在调用点各写一遍。
    /// </summary>
    private bool MergesInto(EffectOnBody instance, EffectDef def)
    {
        if (def.StackKey is null)
        {
            return instance.EffectId == def.Id;
        }

        return instance.EffectId == def.Id
            || (_tables.HasEffect(instance.EffectId) && _tables.Effect(instance.EffectId).StackKey == def.StackKey);
    }

    /// <summary>持续效果的修饰符挂身上：重挂之前先撤旧的，免得同来源攒出两份真值。</summary>
    private void AttachModifiers(AbilitySet target, EffectDef def, string sourceId, int stacks,
        long? expiresAtTick)
    {
        DetachModifiers(target, def.Id);
        foreach (var spec in def.Modifiers)
        {
            var magnitude = MagnitudeFor(spec, def, 0, stacks);
            target.Modifiers.Add(new ModifierOnBody(
                _world.NextSeq++, spec.Target, spec.Mode, magnitude, _world.Tick, expiresAtTick,
                sourceId, def.Id, spec.RequirementTags ?? Array.Empty<string>()));
            Emit(target.UnitId, expiresAtTick.HasValue
                ? new ModifierAppliedUntil(spec.Target, spec.Mode, magnitude, expiresAtTick.Value)
                : new ModifierAppliedOpenEnded(spec.Target, spec.Mode, magnitude));
        }
    }

    private void DetachModifiers(AbilitySet target, string effectId)
        => target.Modifiers.RemoveAll(m => m.SourceEffectId == effectId);

    /// <summary>一次性落地：伤害（含承伤倍率）与即时加值。周期伤害、延迟结算、Instant 效果都走这里。</summary>
    private void ApplyModifiersAsOneShot(AbilitySet target, string sourceId, EffectDef def,
        double chainValue, int stacks)
    {
        foreach (var spec in def.Modifiers)
        {
            var magnitude = MagnitudeFor(spec, def, chainValue, stacks);

            if (spec.Mode == ModifierMode.Add && spec.Target == _config.HealthAttribute && magnitude < 0)
            {
                DealDamage(target, sourceId, def, -magnitude);
                continue;
            }

            if (spec.Mode == ModifierMode.Add)
            {
                target.Base[spec.Target] = ReadBase(target, spec.Target) + magnitude;
                Emit(target.UnitId, new BaseRaisedBy(spec.Target, magnitude));
                continue;
            }

            // 非 Add 的一次性数值没有归属处：挂一条本步生效、下一步 S1 就撤的修饰符。
            target.Modifiers.Add(new ModifierOnBody(_world.NextSeq++, spec.Target, spec.Mode, magnitude,
                _world.Tick, _world.Tick + 1, sourceId, null,
                spec.RequirementTags ?? Array.Empty<string>()));
            Emit(target.UnitId, new ModifierFlewBy(spec.Target, spec.Mode));
        }

        ConsumeTriggers(target, def, _config.EffectAppliedEvent, 0);
    }

    private double MagnitudeFor(ModifierSpec spec, EffectDef def, double chainValue, int stacks)
    {
        var magnitude = spec.CalcFromEffectValue ? chainValue : spec.Magnitude;
        if (def.MagnitudePerStack != 0 && stacks > 1)
        {
            magnitude += def.MagnitudePerStack * (stacks - 1);
        }

        // 条件列不在这里管事（N-5）：它只在 S2 决定「本步生效不生效」，不参与数值——那一份实现是
        // S2 里 `TagsSatisfied(unit, m.RequirementTags)` 那一处。数值段再读一次条件就是第二套真值，
        // 而「不满足则跳过、不撤销」与「不满足按几折」是两回事（A82 钉这条：谁把后者写进来就该红）。
        return magnitude;
    }

    private void DealDamage(AbilitySet target, string sourceId, EffectDef def, double rawDamage)
    {
        // 激活检查的第三处问（issue 08 取候选 1，已删的设计档）：伤害落地**前**问一次「他挡不挡得住这一发」。
        // 它与 S4 入口、S5 位移是同一段实现（HasTag / TagMatches），只是问的地方多了一处。
        // 挡住的那一发不发承伤事件（<c>DamageTakenEvent</c>）——它没有落地，事件触发型被动不该跟着加怒。
        if (TryBlockDamage(target, sourceId, def, rawDamage))
        {
            return;
        }

        // 承伤倍率读目标那条宿主给的承伤属性（<c>DamageTakenAttribute</c>）；界在 S2 已经守过，这里只防负数把伤害加回去。
        var taken = Math.Max(0, ReadOptionalCurrent(target, _config.DamageTakenAttribute, 1.0));
        var final = (long)Math.Round(rawDamage * taken, MidpointRounding.AwayFromZero);
        var source = _world.Units[sourceId];

        if (final <= 0)
        {
            AddCounter(target, "damage_nullified", 1);
            Emit(target.UnitId, new DamageRefused(def.Id, rawDamage));
            ConsumeTriggers(target, def, _config.DamageTakenEvent, 0);
            return;
        }

        target.Base[_config.HealthAttribute] = ReadBase(target, _config.HealthAttribute) - final;
        AddCounter(source, "damage_dealt", final);
        AddCounter(target, "damage_taken_total", final);
        Emit(target.UnitId, new DamageDealt(final, sourceId, def.Id));
        ConsumeTriggers(target, def, _config.DamageTakenEvent, final);
    }

    /// <summary>
    /// 抵挡这一次伤害（issue 08）：挂着 <c>DamageBlockTag</c> 且那层数还有货，就吃掉这一发并消耗一层。
    /// 消耗走归属表「`Effect` 的层数 → 撤销者：移除动词的单层版」那一行已写好的形状，不新增列。
    /// </summary>
    private bool TryBlockDamage(AbilitySet target, string sourceId, EffectDef cause, double rawDamage)
    {
        if (!HasTag(target, _config.DamageBlockTag))
        {
            return false;
        }

        var holder = target.Effects
            .Where(e => e.Stacks > 0 && _tables.Effect(e.EffectId).GrantedTags
                .Any(granted => TagMatches(granted, _config.DamageBlockTag)))
            .OrderBy(e => e.EffectId, StringComparer.Ordinal)
            .FirstOrDefault();
        if (holder is null)
        {
            return false;   // 那个 tag 不是层数载体给的（没有可消耗的东西）——宁可这一发照扣，也不静默吃掉
        }

        ConsumeOneStack(target, holder);
        AddCounter(target, "damage_blocked", 1);
        Emit(target.UnitId, new DamageBlockedByEffect(holder.EffectId, rawDamage, sourceId, cause.Id));
        return true;
    }

    /// <summary>
    /// 单层消耗：允许减到 <b>0 层而实例留着</b>。0 层念得出「抵挡打光了、等在回充」，
    /// 而撤掉实例就没有东西能回充了（N-15 那一跳靠这条效果自己带周期）。
    /// 整条撤销是另一个动词（<see cref="InjectRemoveEffect"/> 的 stacks=0）。
    /// </summary>
    private void ConsumeOneStack(AbilitySet target, EffectOnBody instance)
    {
        instance.Stacks = Math.Max(0, instance.Stacks - 1);
        Emit(target.UnitId, new EffectStackConsumed(instance.EffectId, instance.Stacks));
        Recalc();
    }

    /// <summary>
    /// 事件回调只做一件事：把命中 <c>TriggeredEffects</c> 的条目登记成 Pending（N-2）——
    /// 真值仍在数据上，回调里不写状态（A1）。
    /// </summary>
    private void ConsumeTriggers(AbilitySet target, EffectDef causeDef, string eventName, double amount)
    {
        // 遍历全部在身的效果，谁带触发条目谁说话——同源那一跳的把关只有一处，就是下面
        // 「被触发的正好是造成事件那条 → 跳过」。这里曾多挂过一个 `!ReferenceEquals(字符串)` 的外层过滤：
        // 它比的是引用不是内容，几乎恒真（等于没筛），而 N-2 的原文只要求「回调只登记、不写真值」，
        // 没有第二道同源排除（A81 钉住内层那一道就够）。
        foreach (var holder in target.Effects
                     .OrderBy(e => e.EffectId, StringComparer.Ordinal)
                     .ToList())
        {
            var def = _tables.Effect(holder.EffectId);
            foreach (var (onEvent, effectId) in def.TriggeredEffects)
            {
                // 同源排除只这一道（A81 钉它）：造成这一跳的效果不许被自己再登记一遍，否则一步之内成环。
                if (onEvent != eventName || effectId == causeDef.Id)
                {
                    continue;
                }

                var fired = _tables.Effect(effectId);
                var snapshot = target.Current.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
                var value = fired.CalcNames.Count > 0
                    ? EvaluateChain(snapshot, target, fired)
                    : amount;
                target.Pendings.Add(new PendingSettlement
                {
                    Seq = _world.NextSeq++,
                    ExecuteAtTick = _world.Tick,
                    SourceId = target.UnitId,
                    EffectId = effectId,
                    TargetIds = new[] { target.UnitId },
                    SourceAttrsSnapshot = snapshot,
                    CalcValue = value,
                });
                Emit(target.UnitId, new TriggerFired(eventName, effectId));
            }
        }
    }

    // ---- 注入动词（写路径的唯一入口，与玩家走的是同一段代码）------------------

    /// <summary>
    /// 绕过技能直接施加效果：控制链与打断最省事的摆法。
    /// <paramref name="sourceId"/> 给了就把「谁打的」与 Calc 的源属性一起换过去——
    /// 木桩平A、外部打一笔这类「不走技能入口的伤害」也要过 S0 那三处激活检查问（承伤倍率、抵挡、事件触发），
    /// 所以这里留一个有名字的来源，而不是让调用点自己去改血。
    /// </summary>
    public void InjectEffect(string targetId, string effectId, long? durationTicks = null, string? sourceId = null)
    {
        var target = _world.Units[targetId];
        var source = sourceId is null ? target : _world.Units[sourceId];
        var def = _tables.Effect(effectId);
        if (durationTicks.HasValue && def.Kind != EffectKind.Instant)
        {
            def = def with { DurationTicks = durationTicks.Value };
            if (!_tables.HasEffect(def.Id))
            {
                _tables.Register(def);
            }
        }

        ApplyEffect(target, source.UnitId, def, EvaluateChain(source.Current, target, def));
        Recalc();
    }

    /// <summary>撤效果的层数：stacks 给 0 表示整条撤（tank.steady 消耗一层走这一发）。</summary>
    public void InjectRemoveEffect(string targetId, string effectId, int stacks = 0)
    {
        var target = _world.Units[targetId];
        var instance = target.Effects.FirstOrDefault(e => e.EffectId == effectId);
        if (instance is null)
        {
            return;
        }

        if (stacks > 0 && instance.Stacks > stacks)
        {
            instance.Stacks -= stacks;
            Emit(targetId, new EffectStackConsumed(effectId, instance.Stacks));
            Recalc();
            return;
        }

        target.Effects.Remove(instance);
        DetachModifiers(target, effectId);
        Emit(targetId, new EffectRemoved(effectId));
        Recalc();
    }

    /// <summary>给属性现场加一条修饰符（Override 与上下界的测试入口，不占技能名额）。</summary>
    public void InjectModifier(string targetId, string attribute, ModifierMode mode, double magnitude,
        long? expiresAtTick = null, IReadOnlyList<string>? requirementTags = null)
    {
        var target = _world.Units[targetId];
        target.Modifiers.Add(new ModifierOnBody(_world.NextSeq++, attribute, mode, magnitude,
            _world.Tick, expiresAtTick, targetId, null,
            requirementTags ?? Array.Empty<string>()));
        // 注入口与效果路径共用同一对形状：同一份数据（挂在身上的修饰符、有没有到期步）
        // 只念一种事件行。以前这里发的是不带 `til=` 的第三种写法，等于「注入的路径」与
        // 「效果的路径」对同一条真值各念一套（用户确认补齐，改的是文案不是真值）。
        Emit(targetId, expiresAtTick.HasValue
            ? new ModifierAppliedUntil(attribute, mode, magnitude, expiresAtTick.Value)
            : new ModifierAppliedOpenEnded(attribute, mode, magnitude));
        Recalc();
    }

    public void InjectSetBase(string targetId, string attribute, double value)
    {
        var target = _world.Units[targetId];
        target.Base[attribute] = value;
        Emit(targetId, new BaseSetTo(attribute, value));
        Recalc();
    }

    public void InjectClearCooldown(string unitId, string abilityId)
    {
        var ability = _world.Units[unitId].Abilities[abilityId];
        ability.CooldownEndsAtTick = null;
        ability.Phase = AbilityPhase.Ready;
        ability.StepsRemaining = 0;
        ability.RejectedReason = AbilityRejectReason.None;
        var def = _tables.Ability(abilityId);
        if (def.MaxCharges > 0)
        {
            ability.ChargesLeft = def.MaxCharges;
        }

        Emit(unitId, new CooldownCleared(abilityId));
    }

    /// <summary>显式取消在放的能力：未到 commit 全额返还，已过 commit 照扣（F1 的载体）。</summary>
    public bool InjectCancel(string unitId, string abilityId)
    {
        if (!_world.Units[unitId].Abilities.TryGetValue(abilityId, out var ability))
        {
            return false;
        }

        if (ability.Phase is AbilityPhase.Ready or AbilityPhase.Cooling)
        {
            return false;
        }

        var def = _tables.Ability(abilityId);
        if (!ability.CommitTick.HasValue && def.CostAttribute.Length > 0)
        {
            Emit(unitId, new CancelRefunded(abilityId));
        }

        ability.Phase = AbilityPhase.Ready;
        ability.StepsRemaining = 0;
        ability.RejectedReason = AbilityRejectReason.Interrupted;
        Emit(unitId, new AbilityCanceled(abilityId));
        Recalc();
        return true;
    }

    /// <summary>把某个技能的冷却直接倒到第 n 步好（分支对照与剧本注入都要这一发）。</summary>
    public void InjectSetCooldown(string unitId, string abilityId, long endsAtTick)
    {
        var ability = _world.Units[unitId].Abilities[abilityId];
        ability.CooldownEndsAtTick = endsAtTick;
        ability.Phase = AbilityPhase.Cooling;
        Emit(unitId, new CooldownStarted(abilityId, endsAtTick));
    }

    // ---- 共用小件 -------------------------------------------------------------

    private double EvaluateChain(IReadOnlyDictionary<string, double> source, AbilitySet targetUnit,
        EffectDef def)
    {
        if (def.CalcNames.Count == 0)
        {
            return def.CalcSeed;
        }

        var input = new CalcInput(source, targetUnit.Current, targetUnit.TagCounts, def.CalcSeed,
            def.CalcParams, _world.Tick);
        return _calc.RunChain(def.CalcNames, def.CalcParams, def.CalcSeed, input);
    }

    private static double ReadBase(AbilitySet unit, string name)
        => unit.Base.TryGetValue(name, out var v) ? v : 0;

    private double ReadCurrent(AbilitySet unit, string name)
        => unit.Current.TryGetValue(name, out var v) ? v : ReadBase(unit, name);

    private double ReadOptionalCurrent(AbilitySet unit, string name, double fallback)
        => unit.Current.TryGetValue(name, out var v) ? v
            : unit.Base.TryGetValue(name, out var b) ? b : fallback;

    private bool IsAlive(AbilitySet unit)
        => ReadCurrent(unit, _config.HealthAttribute) > 0;

    private bool HasTag(AbilitySet unit, string tag) => unit.TagCounts.Any(kv => kv.Value > 0
        && TagMatches(kv.Key, tag));

    private bool TagsSatisfied(AbilitySet unit, IReadOnlyList<string>? tags)
        => tags is null || tags.All(tag => HasTag(unit, tag));

    /// <summary>
    /// tag 层级靠前缀匹配、不建树——但方向只有一个：<b>授予父 tag 算命中子查询</b>（授予 `cc`、
    /// 查 `cc.rooted` 为真），反过来「授予子、查父」不算。写成双向会让「免疫一类 tag」用
    /// 一条前缀就把自己所有子 tag 也认成命中，那是错的（H1 实测把这条逼出来的，见验证报告）。
    /// </summary>
    private static bool TagMatches(string granted, string queried)
        => granted == queried
            || granted.StartsWith(queried + ".", StringComparison.Ordinal);

    private static void AddTag(AbilitySet unit, string tag, int delta)
        => unit.TagCounts[tag] = unit.TagCounts.TryGetValue(tag, out var c) ? c + delta : delta;

    private static void AddCounter(AbilitySet unit, string name, double delta)
        => unit.Counters[name] = unit.Counters.TryGetValue(name, out var c) ? c + delta : delta;

    /// <summary>
    /// 曲线是数据（折点数组平铺 <c>x0,y0,x1,y1,...</c>），采样归公共层：折线线性插值、端点外夹紧。
    /// 现在只有一个轴 —— <c>"distance"</c>：按施法者与目标当这一步的距离折。距离从宿主来
    /// （<see cref="DistanceOf"/>），与射程激活检查同一把尺。
    /// </summary>
    private double ApplyCurve(EffectDef def, double value, AbilitySet source, AbilitySet target)
    {
        if (!def.HasCurve || def.CurveAxis != "distance")
        {
            return value;
        }

        return value * SampleCurve(def.CurveXY!, Distance(source.UnitId, target.UnitId));
    }

    internal static double SampleCurve(IReadOnlyList<double> xy, double x)
    {
        if (xy.Count < 4 || xy.Count % 2 != 0)
        {
            throw new KeyError($"曲线折点不成对 | {xy.Count} 个数");
        }

        var count = xy.Count / 2;
        if (x <= xy[0])
        {
            return xy[1];
        }

        if (x >= xy[(count - 1) * 2])
        {
            return xy[(count - 1) * 2 + 1];
        }

        for (var i = 1; i < count; i++)
        {
            if (!(x <= xy[i * 2]))
            {
                continue;
            }

            var (x0, y0) = (xy[(i - 1) * 2], xy[(i - 1) * 2 + 1]);
            var (x1, y1) = (xy[i * 2], xy[i * 2 + 1]);
            return x1 == x0 ? y1 : y0 + (y1 - y0) * ((x - x0) / (x1 - x0));
        }

        return xy[(count - 1) * 2 + 1];
    }

    /// <summary>
    /// 蓄力与松手：技能还在 <c>Pre</c> 里时，外部把剩余步数改写掉——这是「等条件」之外唯一
    /// 需要动时序的一档，改的仍是那一列整数（A3），所以暂停、单步、重放三件一起成立。
    /// </summary>
    public bool InjectSetStepsRemaining(string unitId, string abilityId, long steps)
    {
        if (!_world.Units[unitId].Abilities.TryGetValue(abilityId, out var ability)
            || ability.Phase != AbilityPhase.Pre)
        {
            return false;
        }

        ability.StepsRemaining = Math.Max(0, steps);
        Emit(unitId, new StepsReset(abilityId, steps));
        return true;
    }

    /// <summary>私有出口：一类事件一个形状，字段进 <see cref="EventPayload"/>，
    /// 事件行的名字与文本都由形状自己给（名单见 AbilityEvents.cs）。</summary>
    private void Emit(string subject, EventPayload payload)
        => _world.Events.Add(new AbilityEvent(_world.Tick, subject, payload));

    // ---- 往返与分支（D6/D7 的机器口）-----------------------------------------

    /// <summary>存：派生值先现算一遍再写，所以读回来的世界重算必须逐字相同。
    /// 把表一起交给它，只为那一格 <c>charges=</c>（与周期回充读的是同一处）。</summary>
    public string TakeSnapshot() => AbilitySnapshot.Save(_world, _tables);

    /// <summary>
    /// 跳到快照那一格：重建状态 → 重新派生 → 与存下来那份逐字比。
    /// 不等就是 A2（状态即数据）破了，而差在哪一行指得出来。
    /// </summary>
    public static AbilityStepper Restore(AbilityWorld shell, IAbilityHost host, CalcRegistry calc,
        AbilityTables tables, string snapshot, AbilityLayerConfig config)
    {
        var restored = AbilitySnapshot.Load(snapshot);
        return new AbilityStepper(restored, host, calc, tables, config);
    }

    /// <summary>
    /// 从检查点分叉：回到那一格再改一笔输入，起跳成本是 O(1) 而不是 O(k)。
    /// <paramref name="config"/> 缺省<b>沿用本层那一份</b>——分叉出来的是同一条时间线的另一支，
    /// 配置属于层而不属于调用点（以前缺省 null 会让事件流容量归零当场抛，逼 8 个调用点各递一份
    /// 一模一样的 config，那是 interface 外溢；用户确认改掉）。要真换一档跑对照，显式递进来。
    /// <b>距离那把尺也跟过来</b>（<see cref="DistanceOf"/>）：它属于「这一支的世界」而不属于某一格状态，
    /// 快照里没有它，不跟过来就等于分叉出来的那支一放带射程的技能就抛。
    /// </summary>
    public AbilityStepper Branch(string snapshot, AbilityLayerConfig? config = null)
    {
        var branch = Restore(new AbilityWorld(), _host, _calc, _tables, snapshot, config ?? _config);
        branch.DistanceOf = _distanceOf;
        return branch;
    }
}
