#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DemoFactory.GugaFramework.Abilities;
using DemoFactory.Prototypes.BattleArray.Logic;
using DemoFactory.Tests;
using Shouldly;
using Xunit;

namespace BattleArray.Tests;

/// <summary>
/// <b>H3 真 1v1 宿主递进清单的那一层壳</b>（P3「一份清单三处跑」的三个注册壳之一，另两份是
/// <see cref="ChecklistH1Host"/> 与 <c>AbilityChecklistOnH2</c>）。
///
/// 壳的分工照 <see cref="ChecklistH1Host"/> 与 <c>SkillDuelRift</c>：<b>只回答「哪一个兵、哪一条技能、
/// 哪一个数字」，一个字都不带断言</b>——断言本体只有 <see cref="AbilityChecklistTests"/> 那一份，
/// 那份文件不认得原型（钉它的是 A54），技能名 / tag 名 / 属性名全在这里（A70 钉的就是这一对分工）。
/// 世界与层都是现成的：<see cref="BattleWorld"/> 那一局真 1v1 ＋ <see cref="BattleAbilities"/> 那层皮，
/// 装配与写路径都走 <see cref="DuelRiftSetup"/>——<b>壳里不新写第三条词表，也不另开第二条写路径</b>
/// （「桥只转发」）。三个口在 H3 各有现成承接处：时间 = <see cref="BattleWorld.AdvanceDebugSteps"/>、
/// 状态 = <see cref="BattleWorld.ReplaySnapshot"/> 与 <see cref="BattleAbilities.Snapshot"/>、
/// 注入 = <see cref="DuelRiftSetup.Inject"/>。
///
/// <b>这一壳最难的一格，先处理它再谈别的</b>：H3 的两个兵会自己走向对方、自己打平A，
/// 而清单要撞的是「目标血量差 == 这条技能的手算伤害」。世界插手一次，红字就说不清是谁打的。
/// 两条对策缺一条都不成立：
/// 1. <b>摆开</b>：两个兵各站本方<b>最后排</b>（深度轴 |ΔY| = 11 − (−11) = 22 米）、又<b>错开四列</b>
///    （横向 4 − (−4) = 8 米）⇒ 直线距离 √(22² + 8²) ≈ 23.4 米；平A 够得着的距离只有
///    射程 + 两个身体半径 ≈ 3 米（剑士 1.98 + 0.45 + 0.55），两边相对速度 (1.5 + 1.2) 米/秒
///    = 每步 0.27 米 ⇒ <b>要 76 步才走拢</b>，而清单最长的一格（<c>RunScript</c> 那 20 格）离这条线还远。
///    代价写在 <see cref="BattleAbilities.ChecklistProbe"/> 那一句里：探针件的射程一律给满战场宽度，
///    现成 <c>tank.slam</c> 那种 6 米短射程当不了载体——它会把整格变成「放不出来」。
/// 2. <b>自查</b>：见 <see cref="EnsureWorldStaysOutOfTheJudgement"/>——不按下任何技能先推 20 步，
///    两个兵的血一字不动才交得出宿主；不干净就在构造函数里抛，绝不把污染过的格交给本体。
///
/// 每一格摆成什么样（能复用现成三条就复用；摆不出的形状才加探针件，数字都取整数，红了能一口说出差在哪一步）：
/// | 清单要问的形状 | 这里的载体 | 手算 |
/// |---|---|---|
/// | 有前摇 + 有消耗 + 有冷却 | <c>ck.windup</c>（探针） | 前摇 3 步、过 commit 扣 <c>ck.rage</c> 30、冷却 20 步、固定 20 血 |
/// | 在放中授予 tag | <c>ck.channeled</c>（探针） | 前摇 2 步、授予 <c>state.guard</c>（现成 tag）、那一刀 15 血 |
/// | 延迟结算 + 读源属性 | <c>rider.charge</c>（现成） | 不带目的地按下 → 延迟 3 步、伤害 = commit 抄下的攻击 20 × 曲线(已行走 0 米 = 0.5) = 10 |
/// | 一次激活两条待决 | <c>ck.two_pending</c>（探针） | +3 步落 3 血、+7 步落 5 血，互不顶掉 |
/// | 入口被 tag 挡住 | <c>ck.silenced</c>（探针，<c>BlockedTags = ck.silence</c>） | 挂了沉默探针就放不下，且念得出 BlockedByTag |
/// | 时长 ≠ 周期 + 可叠层 | <c>ck.wound</c>（探针） | 时长 60 步、周期 5 步一跳 4 血、封顶 3 层每层多 2 血 |
/// | 显式打断 | <c>fx.slam_slow</c>（现成，自带 <c>CancelMatchingTags = state.guard</c>） | 命中「在放中」那条、留一行 committed=0；减速那笔照在身上 |
/// | 效果段授予同一个 tag | <c>ck.tag_grant</c>（探针） | 与在放中的能力段合到一张计数表 ⇒ <c>t;state.guard;2</c> |
/// | 只授予沉默 tag | <c>ck.silence_probe</c>（探针） | 常驻（Infinite），挂了才谈得上「被挡住」 |
/// | 同段两笔 Override 撞界 | <c>damage_taken</c>（现成的界 [0.2, 1]） | 0.5 → 0.1 → 界抬回 0.2 |
/// | 带消耗的资源 | <c>ck.rage</c>（探针属性） | 层的属性表里本来没有它，开局经词表 <c>SetBase</c> 拧成 100（不开第二条写路径） |
/// </summary>
internal sealed class ChecklistH3Host : AbilityChecklistTests.IChecklistHost
{
    // === 属性名（层里那一份登记在 BattleAbilities.AttributesOf / SetLimits，壳只回答「清单问的是哪一条」）===

    private const string Hp = "hp";

    private const string Attack = "attack";

    private const string DamageTaken = "damage_taken";

    // === 这一局的摆法：把平A 排除在这一格之外（几何的账在抬头那段，取法是先读世界自己的坐标再算）===

    /// <summary>两个兵各站本方最后排（row 2 ⇒ 深度轴 ±11 米）。</summary>
    private const int QuietRow = 2;

    private const int CasterColumn = 0;

    private const int TargetColumn = 4;

    /// <summary>自查推几步：与清单最长的那一格（<c>RunScript</c> 20 格）同长，多一步都不必。</summary>
    private const int QuietSteps = 20;

    /// <summary>带技能的那个兵 = 我方剑士（沿用 <see cref="DuelRiftSetup"/> 那对兵号，壳不自编第三套身份）。</summary>
    private const string Caster = DuelRiftSetup.CasterUnitId;

    /// <summary>挨打的那个兵 = 敌方蛮牛（260 血：兵种卡 5.2 节那张表，兽人那条「力大」只乘攻击、不动血）。</summary>
    private const string Target = DuelRiftSetup.DummyUnitId;

    private static readonly Formation Ours = new("ck-duel-ours", "清单·我方剑士靠后排错开列",
        new[] { new UnitPlacement(TroopKind.Warrior, QuietRow, CasterColumn) });

    private static readonly Formation Theirs = new("ck-duel-theirs", "清单·敌方蛮牛靠后排错开列",
        new[] { new UnitPlacement(TroopKind.Tank, QuietRow, TargetColumn) });

    private readonly BattleWorld _world;

    private readonly BattleAbilities _abilities;

    private ChecklistH3Host(BattleWorld world, BattleAbilities abilities)
    {
        // 自查排在交付之前：世界插手了这一局，交出去的每一格红字都会说成「技能的数不对」，
        // 而真相是平A 也打了那一笔——所以这里宁可直接抛，也不交一份污染的格。
        EnsureWorldStaysOutOfTheJudgement();
        _world = world;
        _abilities = abilities;
        Shapes = new AbilityChecklistTests.Shapes
        {
            Caster = Caster,
            Target = Target,

            // F1 / D3 的载体：探针件 ck.windup。现成的 warrior.rift 形状几乎一样（前摇 3 步、冷却 20 步、
            // 满血那一刀 20 血，那本账在 AbilityH3DuelTests），但它<b>没有消耗</b>，而 F1 两半读的都是
            // CostAttribute ⇒ 这一格只能摆探针件，数字照 rift 那本台账对齐（20 血、冷却 20 步）。
            Windup = BattleAbilities.ChecklistProbe.WindupAbilityId,

            // F2 / F3 / F5 / D5 的被打断方：前摇里授予 state.guard——那个 tag 是现成的（清单页 tank.guard
            // 授予的那条，fx.slam_slow 的取消列点的就是它），所以「打断方」那一格不用新件。
            Channeled = BattleAbilities.ChecklistProbe.ChanneledAbilityId,

            // F6 / D2 第一问的载体：复用现成三条里的 rider.charge——它是 H3 唯一「延迟登记 + 真的读源属性」
            // 的一件（fx.charge_impact 走 calc.charge_rams，读的是施法者的 attack，在 commit 抄成快照）。
            // 这里<b>不带目的地</b>按下（位移那一格的台账是 A66/A67，不在清单里重复），于是已行走距离 = 0 米
            // ⇒ 曲线交回 0.5 ⇒ 伤害 = 抄下来的攻击 × 0.5。
            Flying = BattleAbilities.ChargeAbilityId,

            TwoPending = BattleAbilities.ChecklistProbe.TwoPendingAbilityId,
            Silenced = BattleAbilities.ChecklistProbe.SilencedAbilityId,
            Wound = BattleAbilities.ChecklistProbe.WoundEffectId,

            // F3 / D5 的打断方就用现成的踏地减速：它自带 CancelMatchingTags = state.guard，
            // 而「身上那条效果不随技能撤销」这件事在这一格正好读得出来（减速照在、层数照在）。
            Interrupter = BattleAbilities.SlamSlowEffectId,

            TagGrant = BattleAbilities.ChecklistProbe.TagGrantEffectId,
            SilenceProbe = BattleAbilities.ChecklistProbe.SilenceProbeEffectId,
            ChanneledTag = BattleAbilities.GuardTagId,
            SilenceTag = BattleAbilities.ChecklistProbe.SilenceTag,
            HealthAttribute = Hp,
            AttackAttribute = Attack,

            // F1 读的那笔消耗：层的属性表里没有「怒气」这一条（加进 AttributesOf 会让默认突斩局的
            // b; 行多出一行 ⇒ 逐字基线要重录），所以由壳经词表 SetBase 现场拧出来（见 Fresh 那一句）。
            CostAttribute = BattleAbilities.ChecklistProbe.RageAttribute,

            // F8 撞界那一格用现成的 damage_taken：它的界 [0.2, 1] 是盾墙那条 clamp 的口径，
            // BattleAbilities 构造时就给全场每个兵设好了。
            ProbeAttribute = DamageTaken,

            // === 手算的数字（TargetHealth 与 FlyingDamage 直接从层里那份真值推，不抄第二份）===
            TargetHealth = Value(Target, Hp),
            WindupPreSteps = BattleAbilities.ChecklistProbe.WindupPreSteps,
            WindupCost = BattleAbilities.ChecklistProbe.WindupCost,
            WindupDamage = BattleAbilities.ChecklistProbe.WindupDamage,
            ChanneledPreSteps = BattleAbilities.ChecklistProbe.ChanneledPreSteps,
            FlyingDelaySteps = BattleAbilities.ChargeArriveSteps,

            // 20 × 0.5 = 10：攻击读的是 <c>Current[attack]</c>（S2 出口那份），曲线首折点 = 已行走 0 米那一格
            FlyingDamage = Value(Caster, Attack) * BattleAbilities.ChargeTravelledCurve[1],
            PendingFirstDelay = BattleAbilities.ChecklistProbe.PendingFirstDelaySteps,
            PendingSecondDelay = BattleAbilities.ChecklistProbe.PendingSecondDelaySteps,
            PendingFirstDamage = BattleAbilities.ChecklistProbe.PendingFirstDamage,
            PendingSecondDamage = BattleAbilities.ChecklistProbe.PendingSecondDamage,
            WoundDurationSteps = BattleAbilities.ChecklistProbe.WoundDurationSteps,
            WoundPeriodSteps = BattleAbilities.ChecklistProbe.WoundPeriodSteps,
            WoundDamagePerTick = BattleAbilities.ChecklistProbe.WoundDamagePerTick,
            ProbeFirstMagnitude = 0.5,
            ProbeSecondMagnitude = 0.1,

            // 0.1 出了下界 ⇒ S2 聚合完再套界，抬回 0.2（界在最后那一格，已删的设计档）
            ProbeClampedTo = 0.2,
        };
    }

    /// <summary>从零起一场（D1/D4 两跑、F1/F2/F8 的对照支、逐条下落那一条的第二次读数都要新的一场）。</summary>
    public static ChecklistH3Host Fresh()
    {
        var (world, abilities) = NewRig();
        return new ChecklistH3Host(world, abilities);
    }

    /// <summary>
    /// 装配一局：<see cref="DuelRiftSetup"/> 那一套真 1v1（同种子、抖动关），把清单那五个技能槽装上栏位，
    /// 并把 F1 要的那笔消耗经词表 <c>SetBase</c> 拧出来。
    /// <b>探针件只在这一发里 <see cref="BattleAbilities.Grant"/>：</b>默认突斩局与全关档的快照因此
    /// 一个字节都不许多出 <c>a;</c> 行（那两份逐字基线照旧，那条断言在 <see cref="AbilityH3DuelTests"/>）。
    /// </summary>
    private static (BattleWorld World, BattleAbilities Abilities) NewRig()
    {
        var world = new BattleWorld(DuelRiftSetup.DefaultSeed, Ours, Theirs, jitter: false);
        var abilities = BattleAbilities.AttachRift(world, Caster);
        foreach (var probe in new[]
                 {
                     BattleAbilities.ChecklistProbe.WindupAbilityId,
                     BattleAbilities.ChecklistProbe.ChanneledAbilityId,
                     BattleAbilities.ChecklistProbe.TwoPendingAbilityId,
                     BattleAbilities.ChecklistProbe.SilencedAbilityId,

                     // 延迟结算那一格用现成的冲锋（不新摆一件）
                     BattleAbilities.ChargeAbilityId,
                 })
        {
            abilities.Grant(Caster, probe);
        }

        // F1 那一半要一条「有消耗」的技能，而那笔资源不在层的属性表里（加进 AttributesOf 会让默认突斩局的
        // b; 行多出一行 ⇒ 逐字基线要重录）——写只走词表那一份口（「桥只转发」），不在层上开第二条写路径。
        // 参数串里的数字用 InvariantCulture（「double 与不变文化」）。
        DuelRiftSetup.Inject(world, AbilityVerbs.SetBase,
            $"{Caster}|{BattleAbilities.ChecklistProbe.RageAttribute}|" +
            BattleAbilities.ChecklistProbe.RageStart.ToString(CultureInfo.InvariantCulture));
        return (world, abilities);
    }

    /// <summary>
    /// <b>平A 污染自查</b>：一个技能都不按、先推 <see cref="QuietSteps"/> 步，两个兵的血必须一字不动。
    /// 动了（或世界在这 20 步里把局判掉了）就抛——抛不算断言：它让那一条测试红，并把「哪一格掉了几血、
    /// 两人各站在哪、离够得着的距离还剩几步」念进消息里，下一位读者不必重新考古。
    ///
    /// 查的是<b>同一装配法开出的孪生局</b>，不是交付出去的那一只实例：同种子同摆法的演化逐字相同
    /// （本体 D1/D4 那两跑逐字等吃的就是这个前提），所以查完不必把交付局也先推走 20 步、
    /// 让壳里每一格的手算步号都白加 20。
    ///
    /// 反证一次（2026-09-28 本轮）：把 <see cref="QuietSteps"/> 临时拧到 80 步 ⇒ 这一发当场抛，
    /// 消息念得出「e01 血 260 → 240、两人隔着 2.529 米而够得着的距离是 2.98 米」——
    /// 这道校验咬得住，不是摆设；按抬头那句几何约 76 步才走拢，20 步这一档留了 56 步余量。
    /// </summary>
    private static void EnsureWorldStaysOutOfTheJudgement()
    {
        var (world, abilities) = NewRig();
        try
        {
            var casterBefore = abilities.Find(Caster)!.Current[Hp];
            var targetBefore = abilities.Find(Target)!.Current[Hp];
            var stepped = world.AdvanceDebugSteps(QuietSteps);
            var casterAfter = abilities.Find(Caster)!.Current[Hp];
            var targetAfter = abilities.Find(Target)!.Current[Hp];

            if (stepped == QuietSteps && casterAfter == casterBefore && targetAfter == targetBefore
                && world.Outcome == BattleOutcome.Running)
            {
                return;
            }

            var (casterUnit, targetUnit) = (world.Find(Caster)!, world.Find(Target)!);
            var reach = casterUnit.Card.ReachAgainst(targetUnit.Card);
            var gap = (casterUnit.Position - targetUnit.Position).Length();
            throw new InvalidOperationException(
                $"清单 H3 壳的自查没过：不按任何技能、空推 {QuietSteps} 步之后世界自己动了手" +
                $"（{Caster} 血 {casterBefore} → {casterAfter}，{Target} 血 {targetBefore} → {targetAfter}，" +
                $"实际走了 {stepped} 步，判定 = {world.Outcome}）。" +
                $"此刻 {casterUnit.PositionFull} 与 {targetUnit.PositionFull} 隔着 {gap:0.###} 米，" +
                $"够得着的距离是 {reach:0.###} 米——这一格要看的是「目标血量差 == 这条技能的手算伤害」，" +
                "世界插手一次就分不清是谁打的了：把两个兵沿深度轴与列号再拉开，或者换掉兵种。");
        }
        finally
        {
            world.Dispose();
        }
    }

    public string Name => "H3 真 1v1";

    public AbilityChecklistTests.Shapes Shapes { get; }

    public bool Supports(AbilityChecklistTests.Capability capability) => capability switch
    {
        // H3 的走位不是调试口能塞的一笔：位置由世界的 S5 演化出来（谁在往哪打、同方分离推挤）。
        // 词表名单里也没有 Move（DuelRiftSetup.WorldSink 那一侧挡在门外），所以 D3 读到的必须是「当场抛」。
        AbilityChecklistTests.Capability.Move => false,

        // 层的 Branch/Restore 出来的步进器仍绑着父宿主（仓里记着的账 N-21）：分支里读存活会读到父局，
        // 而 H3 读存活读的就是那只真世界（BattleAbilities.IsAlive）——D6/D7 在这一侧跑等于自证。
        AbilityChecklistTests.Capability.Fork => false,

        // 级 2 要起引擎与桥，dotnet test 这一层起不来（实测数字在已删的验证报告）。
        AbilityChecklistTests.Capability.LiveReplay => false,
        _ => false,
    };

    public string ReasonWithout(AbilityChecklistTests.Capability capability) => capability switch
    {
        AbilityChecklistTests.Capability.Move =>
            "H3 的走位由世界自己的 S5 演化出来（谁在往哪打、同方分离推挤），位置不是调试口能塞进去的一笔；" +
            "要让位置当技能的输出，走的是 CastAbility 的第 4 段目的地——现成的 rider.charge 与台账 A66 就是那一格",

        AbilityChecklistTests.Capability.Fork =>
            "层的 Branch/Restore 出来的步进器仍绑着父宿主（仓里记着的账 N-21），" +
            "而 H3 读存活读的就是那只父世界——分支里读存活会读到父局。D6/D7 在这一侧要等宿主工厂换掉才可信",

        AbilityChecklistTests.Capability.LiveReplay =>
            "级 2 要在跑着的引擎进程里按条喂同一份剧本（桥动词 replay_start / replay_run 那一轮），" +
            "dotnet test 这一层起不了引擎；同一份剧本在引擎里的实测数字在已删的验证报告",
        _ => string.Empty,
    };

    /// <summary>
    /// 16 条清单条目在这一侧都摆得出载体：能复用现成就复用（<c>fx.slam_slow</c> 自带打断列顶 F3、
    /// <c>rider.charge</c> 顶 F6、<c>damage_taken</c> 现成的界顶 F8），摆不出的用 <c>ck.*</c> 探针件补。
    /// 只有 D6/D7 两格走「不适用」——那两格挡在 <see cref="AbilityChecklistTests.Capability.Fork"/>
    /// 那一条路径上（N-21：分支出来的步进器仍绑父宿主），不在这一行。
    /// </summary>
    public string? ReasonWithout(AbilityChecklistTests.Item item) => null;

    public AbilityChecklistTests.IChecklistHost Restart() => Fresh();

    /// <summary>
    /// 时间可控那一格在 H3 的刻度：世界的调试直达口（暂停中也照走、与逐单步推到同一格逐字等，
    /// 那条断言见 <see cref="AbilityH3DuelTests"/>）。走不到那么多步就是这一局已经判掉了——当场说清，
    /// 别让本体拿一串冻住的读数去比。
    /// </summary>
    public void Advance(int steps)
    {
        if (_world.Outcome != BattleOutcome.Running)
        {
            throw new InvalidOperationException(
                $"第 {_world.Tick} 步这一局已判定（{_world.Outcome}），世界不再演化——清单里那条断言" +
                "「目标血量差 == 这条技能的手算伤害」在这一格已经不成立（摆开两个兵那句几何见本文件抬头）");
        }

        _world.AdvanceDebugSteps(steps);
    }

    /// <summary>写只走 <see cref="DuelRiftSetup.Inject"/> 那一份词表口（H3 的适配器），不许绕过它直接调层。</summary>
    public bool Inject(string cmd, string args) => DuelRiftSetup.Inject(_world, cmd, args);

    /// <summary>层那半的全量文本（D2 三问、F5 的 <c>t;…;2</c>、F7 的两条 <c>p;</c> 都读它）。</summary>
    public string Snapshot => _abilities.Snapshot();

    /// <summary>
    /// 逐字比的那份读数 = 世界的重放段格式（头部以 <c>tick=N</c> 起，接了技能层时后面并进层那几行）——
    /// 级 1 重放与 L2 语义档吃的就是同一份排版，清单不许另起第三种读数。
    /// </summary>
    public string Frame => _world.ReplaySnapshot();

    /// <summary>读层里那份 S2 派生值（判定与显示同一份，不在调用点现算）。</summary>
    public double Value(string unit, string attribute) => Set(unit).Current[attribute];

    public int TagCount(string unit, string tag) => Set(unit).TagCounts.GetValueOrDefault(tag);

    public int Stacks(string unit, string effect)
        => Set(unit).Effects.FirstOrDefault(e => e.EffectId == effect)?.Stacks ?? -1;

    /// <summary>按形状计数（层自己那份账，只读口 <see cref="BattleAbilities.Count{T}"/> 与 <see cref="BattleAbilities.EventsWhere{T}"/>，不再按文本匹配）。</summary>
    public int Count<T>(Func<T, bool>? keep = null) where T : EventPayload
        => keep is null ? _abilities.Count<T>() : _abilities.EventsWhere<T>().Count(keep);

    public bool HasEvent<T>(Func<T, bool>? keep = null) where T : EventPayload => Count(keep) > 0;

    /// <summary>这一侧不接回跳：抛出去，理由与 <see cref="Supports"/> 那一句同一处（本体先问 Supports 再调）。</summary>
    public AbilityChecklistTests.IChecklistHost Fork(string snapshot)
        => throw new NotSupportedException(ReasonWithout(AbilityChecklistTests.Capability.Fork));

    private AbilitySet Set(string unitId)
        => _abilities.Find(unitId)
           ?? throw new ArgumentException($"这个兵没装技能栏：{unitId}（H3 全场每兵都该有一个，见 N-18）");
}

/// <summary>
/// <b>清单在 H3 上跑的那一份 fixture</b>：断言全在基类（<see cref="AbilityChecklistTests"/>），
/// 这里只把壳递进去——<b>这 16 条在仓里只准有这一份</b>（A70）。
/// 三个 fixture（H1 / H2 / H3）跑的是同一批方法名，所以 <c>dotnet test</c> 的输出里
/// 每一条都带着宿主名重复出现三次，跑不了的带着理由标成不适用（H3 这一侧：D6/D7/D8 不适用）。
/// </summary>
public class AbilityChecklistOnH3 : AbilityChecklistTests
{
    protected override IChecklistHost NewHost() => ChecklistH3Host.Fresh();
}
