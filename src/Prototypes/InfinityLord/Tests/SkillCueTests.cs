#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DemoFactory.GugaFramework.Abilities;
using DemoFactory.Prototypes.InfinityLord;
using DemoFactory.Prototypes.InfinityLord.Logic;
using Shouldly;
using Xunit;

namespace InfinityLord.Tests;

/// <summary>
/// 出手 cue 的分派测试 —— <see cref="SkillCues"/> 把「谁出手了」翻成一个回调，这一层纯 C#，
/// 所以「演在谁身上、演了几遍」能在 <c>dotnet test</c> 里钉死，不必等出图。
///
/// <para>两半（单位↔单位、单位↔建筑）都由技能层那一条路演，两处易错各钉一条：
/// <list type="bullet">
/// <item>出手方在事件<b>形状</b>的 <c>Source</c> 里，事件行的主体是挨打的那个 ——
/// 照主体演就把动作演在了被击单位身上；</item>
/// <item>世界那一份战报（<c>CombatEvent</c>）不再是 cue 的来源：它还在发（别的消费者读它），
/// 谁要是把它也接上，同一次出手就演两遍。</item>
/// </list></para>
///
/// <para>排布与技能层那几条用例同一点（<c>(-15, 42)</c>）：离镇子、巢穴与波次入侵点都远。</para>
/// </summary>
public class SkillCueTests : LogicWorldTestBase
{
    /// <summary>隔离点（世界坐标）。</summary>
    private static readonly Vector2 QuietSpot = new(-15, 42);

    /// <summary>一场单挑跑完之后手上的三样读数。</summary>
    private readonly record struct Duel(
        Unit Hero,
        Unit Mob,
        long SinceTotal,
        long DiedAt,
        List<(long Step, string Id)> Played);

    /// <summary>
    /// 推一场单挑（英雄打死一只怪），返回这段窗口里每一次「出手」的回调。
    /// <b>窗口从挂上 cue 那一刻起算</b>：挂上之前的出手不回放。
    /// </summary>
    private Duel RunDuel()
    {
        AdvanceUntil(heroes: 1, monsters: 1);
        var hero = Heroes().First(h => h.Class == HeroClass.Ranger);
        var mob = Monsters().First();
        World.PlaceUnitForTest(mob.Id, QuietSpot).ShouldBeTrue("排布前提：怪物已挪到隔离点");
        World.PlaceUnitForTest(hero.Id, QuietSpot + new Vector2(5f, 0f)).ShouldBeTrue("排布前提：英雄摆在射程里");

        var played = new List<(long Step, string Id)>();
        var cues = new SkillCues(id => played.Add((World.Tick, id)));
        var since = World.Abilities.EventTotal;
        cues.Wire(World);
        World.Stepped += _ => cues.Mirror();   // 真局里这一发由 Main 挂在 Stepped 上

        var diedAt = -1L;
        World.UnitDied += report =>
        {
            if (report.UnitId == mob.Id) diedAt = World.Tick;
        };

        for (var i = 0; i < 80 && mob.Health > 0; i++) Advance(1);

        mob.Health.ShouldBeLessThanOrEqualTo(0, "前提：这段窗口里真把它打死了");
        cues.DroppedCount.ShouldBe(0, "前提：这些事件没被环形挤掉（「尾窗里没有」不等于「没发生」）");
        return new Duel(hero, mob, since, diedAt, played);
    }

    [Fact]
    public void 每一次出手只演一遍_且演在出手方身上()
    {
        var duel = RunDuel();

        // 真值：这一段窗口里层发出的每一次出手，出手方读事件形状的 Source（事件行的主体是挨打的那个）
        var attackers = new List<string>();
        foreach (var ev in World.Abilities.EventsTail((int)(World.Abilities.EventTotal - duel.SinceTotal)))
        {
            if (ev.Payload is DamageDealt damage) attackers.Add(damage.Source);
        }

        attackers.ShouldNotBeEmpty("前提：这段窗口里真出过手");
        duel.Played.Select(p => p.Id).ShouldBe(attackers,
            "每一次出手演一遍，且演的是出手方 —— 演成挨打的那个、或同一次出手演两遍都算错");
    }

    [Fact]
    public void 击杀那一发只演一遍_且演的是出手方()
    {
        var duel = RunDuel();

        duel.DiedAt.ShouldBeGreaterThan(0, "前提：读到了那一只阵亡的那一步");
        var atKillStep = duel.Played.Where(p => p.Step == duel.DiedAt).ToList();
        atKillStep.Count.ShouldBe(1, "击杀那一发只演一遍（按血量查「目标还在不在场」会把它读成建筑，于是演两遍）");
        atKillStep[0].Id.ShouldBe(duel.Hero.Id, "演的是出手的那一名英雄，不是被击杀的那一只");
    }

    /// <summary>
    /// 砸楼那一发也只演一遍 —— 建筑进层之后两半都走层那一条路，
    /// 世界那份战报（<c>CombatEvent</c>）再被接上就会演两遍。
    /// </summary>
    [Fact]
    public void 砸楼那一发只演一遍_且演在出手方身上()
    {
        Advance(LogicWorld.LairSpawnIntervalTicks + 1);   // 首只怪落场
        var mob = Monsters().First();
        var palace = World.Buildings.First(b => b.Kind == BuildingKind.Palace);

        // 英雄全支开（怪挑最近的目标，留在城里它就先去扑人，这一条测的是砸楼那一半）
        foreach (var hero in Heroes())
        {
            World.PlaceUnitForTest(hero.Id, FarAway).ShouldBeTrue($"排布前提：{hero.Id} 已支开");
        }

        World.PlaceUnitForTest(mob.Id, palace.Position + new Vector2(2f, 0f))
            .ShouldBeTrue("排布前提：怪已站到宫殿墙下");

        var played = new List<(long Step, string Id)>();
        var cues = new SkillCues(id => played.Add((World.Tick, id)));
        var since = World.Abilities.EventTotal;
        cues.Wire(World);
        World.Stepped += _ => cues.Mirror();   // 真局里这一发由 Main 挂在 Stepped 上

        var hits = 0;
        for (var i = 0; i < 60 && hits == 0; i++)
        {
            Advance(1);
            hits = HitsBy(mob.Id, since, buildingsOnly: true);
        }

        hits.ShouldBeGreaterThan(0, "前提：这段窗口里它真砸到了楼");

        played.Count(p => p.Id == mob.Id).ShouldBe(HitsBy(mob.Id, since, buildingsOnly: false),
            "砸楼这一发也只演一遍（世界那份战报再被接上就演两遍）");
    }

    /// <summary>英雄支开用的界外点（与建筑那几条用例同一点）。</summary>
    private static readonly Vector2 FarAway = new(-80, -80);

    /// <summary>这一段窗口里「这一位出手」的层事件条数；<paramref name="buildingsOnly"/> 只要打在楼上的那些。</summary>
    private int HitsBy(string attackerId, long since, bool buildingsOnly)
    {
        var count = 0;
        foreach (var ev in World.Abilities.EventsTail((int)(World.Abilities.EventTotal - since)))
        {
            if (ev.Payload is not DamageDealt damage || damage.Source != attackerId) continue;
            if (buildingsOnly && !World.Abilities.IsBuilding(ev.Subject)) continue;
            count++;
        }

        return count;
    }

    /// <summary>推到场上凑够要用的单位（行会 20 步一名、巢穴按 LairSpawnIntervalTicks 一只 —— 数够就停，不写死步号）。</summary>
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
