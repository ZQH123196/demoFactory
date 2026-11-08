#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DemoFactory.GugaFramework.Abilities;
using DemoFactory.GugaFramework.Pathfinding;
using DemoFactory.Prototypes.InfinityLord.Logic;
using Shouldly;
using Xunit;

namespace InfinityLord.Tests;

/// <summary>
/// 技能层接入的五条用例（ticket 05 点名的五样）：<b>普攻走层 / 击杀账不变 / 拒绝理由读得出 /
/// 激活检查拒了不动 / 移速倍率影响走位</b>，另加一条把「出手的步差 = 卡上的出手间隔」钉死。
///
/// <para>全部排布走 <c>World.PlaceUnitForTest</c>：位置真值住在寻路流水线里，
/// 直接写 <c>unit.Position</c> 会被下一步的镜像同步抹掉（上一张 ticket 实测）。</para>
///
/// <para>隔离点取 <c>(-15, 42)</c>：种子建筑全在西侧镇子（z ≤ 24）、巢穴在东侧（x ≥ 60），
/// 漫步半径 ±8 米 —— 这个点上除了自己放的两个单位，没有第三方插手。</para>
/// </summary>
public class AbilityLayerTests : LogicWorldTestBase
{
    /// <summary>隔离点（世界坐标）—— 远离镇子、巢穴与波次入侵点。</summary>
    private static readonly Vector2 QuietSpot = new(-15, 42);

    [Fact]
    public void 普攻走层_伤害由层结算且世界的血跟着走()
    {
        var (hero, mob) = PlaceDuelPair(gap: 5f);
        var layer = World.Abilities;
        var hpBefore = mob.Health;

        Advance(1);

        // 世界那一列血掉的是层算出来的那一笔（同一步、同一个数）
        mob.Health.ShouldBe(hpBefore - hero.Attack, "普攻的伤害由技能层结算，世界只是把层那一列血抄回来");
        hero.CooldownRemaining.ShouldBe(hero.AttackCooldownTicks, "出手间隔仍是卡上那个数（世界那一列没被搬走）");

        // 层里那一笔账在：谁打的（Source）、经哪条效果（Via）、打掉多少（Final）
        var hits = HitsBy(layer, hero.Id);
        hits.Count.ShouldBe(1, "这一步只该有这一发（游侠射程 6 米、冷却 0）");
        var hit = hits[0];
        hit.Event.Subject.ShouldBe(mob.Id, "挨打的是那一只");
        hit.Payload.Source.ShouldBe(hero.Id, "账上记着是谁打的");
        hit.Payload.Final.ShouldBe(hero.Attack, "打掉的数与施法者的攻击力逐字相同");

        // 世界的战报照旧发（别的消费者还在读它），数值与层那一笔同源
        Log.Combats.Count.ShouldBe(1, "这一步发一条战报");
        Log.Combats[0].Damage.ShouldBe(hero.Attack);
        Log.Combats[0].TargetDied.ShouldBeFalse();
    }

    [Fact]
    public void 击杀账与经验金币不变_英雄击杀仍得固定经验与战利品()
    {
        var (hero, mob) = PlaceDuelPair(gap: 5f);
        var xpBefore = hero.Xp;
        var goldBefore = hero.Gold;
        var lootBefore = Log.Economy.Count(e => e.Kind == EconomyEventKind.KillLoot);

        // 游侠攻击力 8、地精 30 血 ⇒ 四下（出手间隔 6 步）—— 推到它倒下再多走几步
        Advance(40);

        mob.Health.ShouldBeLessThanOrEqualTo(0, "四下打完这只地精");
        World.Units.Any(u => u.Id == mob.Id).ShouldBeFalse("阵亡清理把尸体挪出了世界");

        hero.Xp.ShouldBe(xpBefore + LogicWorld.KillXp, "击杀经验仍是那个固定数");
        Log.Economy.Count(e => e.Kind == EconomyEventKind.KillLoot).ShouldBe(lootBefore + 1, "击杀战利品发一条");
        Log.Economy.Last(e => e.Kind == EconomyEventKind.KillLoot).Amount.ShouldBe(LogicWorld.KillLootGold);
        hero.Gold.ShouldBeGreaterThanOrEqualTo(goldBefore + LogicWorld.KillLootGold, "战利品直接进英雄钱包");

        var last = Log.Combats.Last(c => c.TargetId == mob.Id);
        last.TargetDied.ShouldBeTrue("最后那一条战报标着「这一下打死了」");
        last.Damage.ShouldBe(hero.Attack);
    }

    [Fact]
    public void 拒绝理由读得出_射程外与冷却中各有一条有名字的理由()
    {
        var (hero, mob) = PlaceDuelPair(gap: 0f, placeAdjacent: false);
        var layer = World.Abilities;
        var abilityId = InfinityLordSkillTable.AbilityOf(hero);

        // ① 射程外：Debug 直达口走的是世界出手用的同一个入口（「桥只转发」）
        layer.Cast(hero.Id, abilityId, mob.Id, null).ShouldBeFalse("游侠射程 6 米，隔这么远打不着");
        layer.LastRejectReason.ShouldBe(AbilityRejectReason.NotInRange, "打不着要有名字，不许只回一个 false");

        // ② 贴上去出手：世界那一步打出第一发
        World.PlaceUnitForTest(hero.Id, mob.Position + new Vector2(1f, 0f)).ShouldBeTrue();
        Advance(1);
        HitsBy(layer, hero.Id).Count.ShouldBe(1, "排布前提：第一发已打出");

        // ③ 同一步里再按一次：零前摇这一档按下那一步就过 commit ⇒ 冷却已起算，读到的理由是 Cooldown。
        // 这里早先读到的是 Busy（「还在前摇里」）—— 那是「按下之后一步才 commit」留下的窗口；
        // 本原型四条普攻全是零前摇，那个窗口在这里不存在了（前摇非零的技能仍会经过它）。
        layer.Cast(hero.Id, abilityId, mob.Id, null).ShouldBeFalse();
        layer.LastRejectReason.ShouldBe(AbilityRejectReason.Cooldown, "冷却中再按一次是「冷却」，账上读得出");

        // ④ 再走一步，冷却仍在转：理由不变（读数稳定，不是「碰巧这一步」）
        Advance(1);
        layer.Cast(hero.Id, abilityId, mob.Id, null).ShouldBeFalse();
        layer.LastRejectReason.ShouldBe(AbilityRejectReason.Cooldown, "冷却中再按一次是「冷却」，账上读得出");

        // 拒绝都进了层的事件流（拒绝不静默）
        layer.EventsWhere<AbilityRejected>().ShouldNotBeEmpty("打不着的那一发留一行");
        layer.EventsWhere<AbilityRejectedAndQueued>().ShouldNotBeEmpty("冷却中那一发记成「排一个」（层的排队语义）");
    }

    [Fact]
    public void 激活检查拒了不动_定身挂着这一步一步都不挪()
    {
        AdvanceUntil(heroes: 2, monsters: 0);
        var heroes = Heroes();
        heroes.Count.ShouldBeGreaterThanOrEqualTo(2, "排布前提：行会已产出两名游侠");

        var rooted = heroes[0];
        var free = heroes[1];
        World.PlaceUnitForTest(rooted.Id, QuietSpot).ShouldBeTrue();
        World.PlaceUnitForTest(free.Id, QuietSpot + new Vector2(0f, 5f)).ShouldBeTrue();

        // 给两名各指一个远处的漫步点：排布口只落位移、不清「上一条命令」的去重缓存，
        // 所以挪过之后要把漫步点换成新的一处，它们下一步才会真发一条新命令（走位的题前提）。
        // 点全落在网格里（Z ≤ 50）：网格外的目标求解报「地形无路」，那一名会站着不动。
        rooted.MoveTarget = QuietSpot + new Vector2(10f, 0f);
        free.MoveTarget = QuietSpot + new Vector2(0f, 5f);

        // 定身挂上（探针效果只授予定身 tag，不改别的数）
        World.Abilities.ApplyEffect(rooted.Id, InfinityLordSkillTable.Probe.RootedEffectId, null, null);
        var at = rooted.Position;

        Advance(30);

        rooted.Position.ShouldBe(at, "定身挂着一步都不许挪（先问允不允许动、再算站哪）");
        var held = World.Nav.SolutionOf(rooted.Id);
        held.Status.ShouldBe(NavMoveStatus.HeldByActivationCheck, "移动解交出「被激活检查挡住」这一档，不是静默跳过");
        held.CheckReason.ShouldBe("定身", "为什么不动要读得出来");
        held.MovedMeters.ShouldBe(0);

        // 同一步里另一名没被定住的英雄照常走位 —— 停下来的原因是激活检查，不是别的东西
        Vector2.Distance(free.Position, QuietSpot + new Vector2(0f, 5f)).ShouldBeGreaterThan(0.5f,
            "对照：没挂定身的那一名照常漫步");
    }

    [Fact]
    public void 移速倍率影响走位_减速挂着这一名的每步位移减半()
    {
        AdvanceUntil(heroes: 2, monsters: 0);
        var heroes = Heroes();
        heroes.Count.ShouldBeGreaterThanOrEqualTo(2, "排布前提：行会已产出两名游侠");

        var slowed = heroes[0];
        var control = heroes[1];
        World.PlaceUnitForTest(slowed.Id, QuietSpot).ShouldBeTrue();
        World.PlaceUnitForTest(control.Id, QuietSpot + new Vector2(0f, 5f)).ShouldBeTrue();

        // 同上激活检查那一条：挪过之后漫步点要换新的，下一步才发得出新命令。
        slowed.MoveTarget = QuietSpot + new Vector2(10f, 0f);
        control.MoveTarget = QuietSpot + new Vector2(10f, 5f);
        World.Abilities.ApplyEffect(slowed.Id, InfinityLordSkillTable.Probe.SlowEffectId, null, null);

        var slowedTop = 0.0;
        var controlTop = 0.0;
        for (var i = 0; i < 40; i++)
        {
            Advance(1);
            slowedTop = Math.Max(slowedTop, World.Nav.SolutionOf(slowed.Id).SpeedMetersPerSecond);
            controlTop = Math.Max(controlTop, World.Nav.SolutionOf(control.Id).SpeedMetersPerSecond);
        }

        controlTop.ShouldBeGreaterThan(0.0, "对照：没减速的那一名确实在走（卡上 1.6 米每秒）");
        slowedTop.ShouldBe(controlTop * InfinityLordSkillTable.Probe.SlowSpeedMult, 1e-6,
            "每步移速标量乘的是技能层派生出来的那一列倍率");
    }

    [Fact]
    public void 出手的步差与卡上的出手间隔逐字相同()
    {
        var (hero, mob) = PlaceDuelPair(gap: 5f);

        // 靶子拉成打不死：这一条用例只读出手的步差，不读击杀
        mob.MaxHealth = 100_000;
        mob.Health = 100_000;

        var steps = new List<long>();
        var cursor = World.Abilities.EventTotal;
        for (var i = 0; i < 60; i++)
        {
            Advance(1);
            var total = World.Abilities.EventTotal;
            var fresh = total - cursor;
            if (fresh > 0)
            {
                foreach (var ev in World.Abilities.EventsTail((int)fresh))
                {
                    if (ev.Payload is DamageDealt hit && hit.Source == hero.Id) steps.Add(World.Tick);
                }

                cursor = total;
            }
        }

        steps.Count.ShouldBeGreaterThanOrEqualTo(9, "60 步里按 6 步一次出手，至少打得出 9 下");
        for (var i = 1; i < steps.Count; i++)
        {
            (steps[i] - steps[i - 1]).ShouldBe(
                (long)InfinityLordSkillTable.HeroCooldownTicks,
                "两次出手之间正好是卡上的出手间隔（层里那把尺与卡上那个数逐字相同，见下一条用例）");
        }

        // 对齐的哨兵：世界那把尺与层同起点 ⇒ 热路径一次都不该撞上冷却激活检查。
        // 撞上就会进排队口，而这一侧的补放是空放（技能行不带效果）—— 见 InfinityLordAbilities.Strike 那段。
        World.Abilities.EventsWhere<AbilityRejectedAndQueued>().ShouldBeEmpty(
            "出手的每一步层里都已转好：热路径不许进排队口");
    }

    [Fact]
    public void 表里那条冷却与卡上的出手间隔逐字相同()
    {
        AdvanceUntil(heroes: 1, monsters: 1);

        // 三处逐字相等：技能表那一格、卡上那个常量、出生时写进单位的那一列。
        // 表里那格早先写的是「卡上数减一」（拿算术去补 commit 晚一步的缝）；这条用例钉它不许再减 ——
        // 而「减了也看不出来」正是这一处难查的地方：上面那条出手步差的用例照样绿。
        foreach (var unit in new[] { Heroes().First(), Monsters().First() })
        {
            var abilityId = InfinityLordSkillTable.AbilityOf(unit);
            World.Abilities.Stepper.Tables.Ability(abilityId).CooldownTicks.ShouldBe(
                (long)InfinityLordSkillTable.CooldownTicksOf(unit.Side),
                $"{abilityId} 表里那一格冷却 = 卡上那个数");
            unit.AttackCooldownTicks.ShouldBe(
                InfinityLordSkillTable.CooldownTicksOf(unit.Side),
                $"{unit.Id} 出生时写进单位的那一列 = 卡上那个数");
        }
    }

    // === 排布与读数的小工具 ===

    /// <summary>
    /// 排一名游侠与一只地精：两者都放到隔离点上（<paramref name="placeAdjacent"/> 为假时只放怪物，
    /// 留出「射程外」那一步要的距离）。
    /// </summary>
    private (Unit Hero, Unit Mob) PlaceDuelPair(float gap, bool placeAdjacent = true)
    {
        AdvanceUntil(heroes: 1, monsters: 1);
        var hero = Heroes().First(h => h.Class == HeroClass.Ranger);
        var mob = Monsters().First();

        // 先把怪物挪到隔离点，再按 gap 摆英雄（gap = 0 时不动英雄，留着它原来的距离）
        World.PlaceUnitForTest(mob.Id, QuietSpot).ShouldBeTrue("排布前提：怪物已挪到隔离点");
        if (placeAdjacent)
        {
            World.PlaceUnitForTest(hero.Id, QuietSpot + new Vector2(gap, 0f)).ShouldBeTrue("排布前提：英雄已摆好");
        }

        return (hero, mob);
    }

    /// <summary>层里某个单位打出的全部命中（事件行的主体是挨打的那个，来源在载荷里）。</summary>
    private static List<(AbilityEvent Event, DamageDealt Payload)> HitsBy(
        InfinityLordAbilities layer, string attackerId)
    {
        var hits = new List<(AbilityEvent, DamageDealt)>();
        foreach (var ev in layer.EventsTail(200))
        {
            if (ev.Payload is DamageDealt hit && hit.Source == attackerId) hits.Add((ev, hit));
        }

        return hits;
    }

    /// <summary>
    /// 推到场上凑够要用的单位（行会 20 步一名、巢穴按 LairSpawnIntervalTicks 一只 —— 数够就停，不写死步号）。
    /// 上限 400 步：凑不齐当场判否，不静默用一个不在场的单位往下走。
    /// </summary>
    private void AdvanceUntil(int heroes, int monsters)
    {
        for (var guard = 0; guard < 400; guard++)
        {
            if (Heroes().Count >= heroes && Monsters().Count >= monsters) return;
            Advance(1);
        }

        Heroes().Count.ShouldBeGreaterThanOrEqualTo(heroes, $"推到第 {World.Tick} 步仍凑不够 {heroes} 名英雄");
        Monsters().Count.ShouldBeGreaterThanOrEqualTo(monsters, $"推到第 {World.Tick} 步仍凑不够 {monsters} 只怪物");
    }
}
