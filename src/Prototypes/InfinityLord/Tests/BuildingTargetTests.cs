#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DemoFactory.GugaFramework.Abilities;
using DemoFactory.Prototypes.InfinityLord.Logic;
using Shouldly;
using Xunit;

namespace InfinityLord.Tests;

/// <summary>
/// <b>建筑进技能目标域</b>（Q12 后半）：单位↔建筑那一条也走层，既有直路退休。
///
/// <para>钉住三件事：① 建筑挨得了打（层里读得到它、进得了激活检查）；
/// ② 伤害写回<b>建造模块那一册</b>（走 <c>Damage</c> 不走 <c>SetHealth</c>：前者才发
/// <c>BuildingDamaged</c> 并在归零时走摧毁收口）；③ 目标中途被拆 —— 不抛、不误伤自己。</para>
///
/// <para>排布用<b>怪物砸宫殿</b>这一档（与 <c>PalaceEndgameTests</c> 同一手法）：宫殿在原点、
/// 8 秒前不会倒，而怪物只需站在墙下。英雄支开，免得战斗本能抢走这一步。</para>
/// </summary>
public class BuildingTargetTests : LogicWorldTestBase
{
    /// <summary>把英雄支开的那一处（离镇子、巢穴与波次入侵点都远）。</summary>
    private static readonly Vector2 FarAway = new(-80, -80);

    [Fact]
    public void 建筑挨打走技能层_伤害写回建造模块那一册()
    {
        var (mob, palace) = PlaceMonsterAtWall(BuildingKind.Palace);

        var moduleBefore = World.Construction.Find(palace.Id)!.Health;
        var layerBefore = World.Abilities.Find(palace.Id)!.Base[InfinityLordSkillTable.HealthAttribute];
        layerBefore.ShouldBe(moduleBefore, "排布前提：层里那一列血是模块那一册的镜像（进册即镜像）");

        Advance(10);

        // ① 层那一册认得这座楼，而且知道它是建筑（攻坚与范围技能要分辨目标时问这一格）
        World.Abilities.IsBuilding(palace.Id).ShouldBeTrue("层里读得出「这个 Id 是建筑」");
        World.Abilities.IsBuilding(mob.Id).ShouldBeFalse("单位不是建筑（身份不串）");

        // ② 打掉了：模块那一册与层那一册与句柄三处同一个数
        var moduleAfter = World.Construction.Find(palace.Id)!.Health;
        var layerAfter = World.Abilities.Find(palace.Id)!.Base[InfinityLordSkillTable.HealthAttribute];
        moduleAfter.ShouldBeLessThan(moduleBefore, "宫殿确实挨了打（既有直路退休之后，扣血由层算）");
        palace.Health.ShouldBe((int)moduleAfter, "句柄读的就是模块那一册（血量真值只有一处）");
        layerAfter.ShouldBe(moduleAfter, "层那一列跟着世界走（每步开头镜像）");

        // ③ 出手进了层的账：这一发是怪物打的，目标是这座楼
        var hits = World.Abilities.EventsWhere<DamageDealt>().Where(h => h.Source == mob.Id).ToList();
        hits.ShouldNotBeEmpty("层里留得下这一发（谁打的、打掉多少）");
        hits.Sum(h => h.Final).ShouldBe(moduleBefore - moduleAfter, 1e-6,
            "层里记的那几笔加起来 = 模块那一册掉的血");
        World.Abilities.EventsWhere<AbilityActivatedAt>()
            .ShouldContain(a => a.Target == palace.Id, "层里记着这一发瞄的是这座楼");

        // ④ 战报照旧发（别的消费者还在读它），目标那一列标着「不是单位」
        var combats = Log.Combats.Where(c => c.TargetId == palace.Id).ToList();
        combats.ShouldNotBeEmpty("单位↔建筑这一条的战报一字未改");
        combats.ShouldAllBe(c => !c.TargetIsUnit, "目标不是单位：表现层靠这一列分开两条演法");
    }

    [Fact]
    public void 写回走建造模块那一发_摧毁收口仍只此一处()
    {
        var (_, palace) = PlaceMonsterAtWall(BuildingKind.Palace);

        // 打空：摧毁收口（出册 + 腾占用 + Removed → 原型事件 + 终局判定）必须照旧发生。
        // 这条同时钉住「不走 SetHealth」：走那一发的话楼会停在 0 血而不出册。
        World.DebugDamageBuilding(palace.Id, LogicWorld.PalaceHealth).ShouldBeTrue();
        Advance(1);

        World.Outcome.ShouldBe(GameOutcome.Defeat, "宫殿被毁 = 失败（摧毁收口一字未改）");
        World.Buildings.Any(b => b.Id == palace.Id).ShouldBeFalse("句柄出列（模块出册那一条路照旧）");
        World.Construction.Find(palace.Id).ShouldBeNull("模块册里也不留占位");
        Log.Destroyed.Select(b => b.Id).ShouldContain(palace.Id, "表现层清模型靠这条事件");
    }

    [Fact]
    public void 目标中途被拆_不抛也不误伤自己()
    {
        var (mob, palace) = PlaceMonsterAtWall(BuildingKind.Palace);
        var mobHpBefore = mob.Health;
        var layerHpBefore = World.Abilities.Find(mob.Id)!.Base[InfinityLordSkillTable.HealthAttribute];

        // 把这座楼从世界上摘掉，而层那一册还留着它的 Id（层不摘册）
        World.DebugDamageBuilding(palace.Id, LogicWorld.PalaceHealth).ShouldBeTrue();
        World.Buildings.Any(b => b.Id == palace.Id).ShouldBeFalse("排布前提：这座楼已经出册");
        World.Abilities.Find(palace.Id).ShouldNotBeNull("层那一册不摘：快照的段数不随局势伸缩");

        // 拿一个已经不在场的目标开火：要有名字的拒绝，不抛、也不把这一发打回自己身上
        var fired = World.Abilities.Cast(mob.Id, InfinityLordSkillTable.AbilityOf(mob), palace.Id, null);
        fired.ShouldBeFalse("目标没了，这一发放不出去");

        Advance(1);
        mob.Health.ShouldBe(mobHpBefore, "不误伤自己：这一发不许打回施法者身上");
        World.Abilities.Find(mob.Id)!.Base[InfinityLordSkillTable.HealthAttribute]
            .ShouldBe(layerHpBefore, "层里施法者那一列血也一个字节没动");
    }

    [Fact]
    public void 建筑进册之后_层快照里读得到它()
    {
        var (_, palace) = PlaceMonsterAtWall(BuildingKind.Palace);

        World.Abilities.UnitOrder.ShouldContain(palace.Id, "建筑进了层那一册（目标域只有一册）");
        World.Abilities.Find(palace.Id).ShouldNotBeNull("层里查得到它的技能栏（血 / 血上限两列）");
        World.Abilities.BuildingCount.ShouldBeGreaterThanOrEqualTo(7, "种子那七座楼都进层");

        var snapshot = World.ReplaySnapshot();
        snapshot.ShouldContain($"u;{palace.Id}", Case.Sensitive, "层自己那一份全量文本里有它");
    }

    [Fact]
    public void 砸楼的出手步差与卡上的出手间隔逐字相同()
    {
        var (mob, palace) = PlaceMonsterAtWall(BuildingKind.Palace);

        // 表里那一格：与卡上那个数逐字相同（层是唯一真值）
        var abilityId = InfinityLordSkillTable.AbilityOf(mob);
        World.Abilities.Stepper.Tables.Ability(abilityId).CooldownTicks.ShouldBe(
            (long)InfinityLordSkillTable.CooldownTicksOf(mob.Side), "表里那一格冷却 = 卡上那个数");
        mob.AttackCooldownTicks.ShouldBe(InfinityLordSkillTable.CooldownTicksOf(mob.Side),
            "出生时写进单位的那一列 = 卡上那个数");

        // 靶子拉成打不死：这一条用例只读砸楼的步差（与单位那一条同一手法）
        mob.MaxHealth = 100_000;
        mob.Health = 100_000;

        // 实际步差：60 步里按卡上的间隔出手（怪物 8 步一次，宫殿 8000 血打不空）
        // 每一步都把英雄支开：行会 20 步补一名，新来的那几名会把这名怪从楼那边引走。
        var steps = new List<long>();
        var cursor = World.Abilities.EventTotal;
        for (var i = 0; i < 60; i++)
        {
            Advance(1);
            foreach (var hero in Heroes())
            {
                World.PlaceUnitForTest(hero.Id, FarAway);
            }

            // 每一步清一次「咬住不放」那一格：新补进来的英雄在视野里，不改它的话这名怪会转去打人。
            mob.TargetUnitId = null;

            var fresh = World.Abilities.EventTotal - cursor;
            if (fresh <= 0) continue;
            foreach (var ev in World.Abilities.EventsTail((int)fresh))
            {
                if (ev.Payload is DamageDealt hit && hit.Source == mob.Id) steps.Add(World.Tick);
            }

            cursor = World.Abilities.EventTotal;
        }

        steps.Count.ShouldBeGreaterThanOrEqualTo(7, "60 步里按 8 步一次，至少打得出 7 下");
        for (var i = 1; i < steps.Count; i++)
        {
            (steps[i] - steps[i - 1]).ShouldBe((long)InfinityLordSkillTable.MonsterCooldownTicks,
                "两次砸楼之间正好是卡上的出手间隔（建筑这一条与单位那一条同一把尺）");
        }

        // 热路径一次都不该撞上冷却激活检查（撞上就会进排队口，而这一侧技能行不带效果）
        World.Abilities.EventsWhere<AbilityRejectedAndQueued>()
            .ShouldNotContain(r => r.AbilityId == abilityId, "砸楼的每一步层里都已转好");
    }

    // === 排布小工具 ===

    /// <summary>
    /// 把首只怪摆到某一种楼<b>墙下</b>（2 米处），英雄全部支开。
    /// 返回摆好的怪与那座楼。
    /// </summary>
    private (Unit Mob, Building Building) PlaceMonsterAtWall(BuildingKind kind)
    {
        var building = World.Buildings.First(b => b.Kind == kind);
        Advance(LogicWorld.LairSpawnIntervalTicks + 1); // 首只怪落场（巢穴产出的第一批）
        foreach (var hero in Heroes())
        {
            World.PlaceUnitForTest(hero.Id, FarAway).ShouldBeTrue($"排布前提：{hero.Id} 已支开");
        }

        var mob = Monsters().First();
        World.PlaceUnitForTest(mob.Id, building.Position + new Vector2(2f, 0f))
            .ShouldBeTrue($"排布前提：{mob.Id} 已站到 {building.Id} 墙下");
        mob.TargetUnitId = null;   // 清掉上一条追击目标，逼它走「最近建筑」那一条
        return (mob, building);
    }
}
