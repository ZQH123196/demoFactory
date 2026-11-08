#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DemoFactory.Prototypes.InfinityLord.Logic;
using Shouldly;
using Xunit;

namespace InfinityLord.Tests;

/// <summary>
/// LogicWorld 确定性测试基类 — 固定种子世界 + 时间轴快进的最小脚手架。
/// 全部断言基于纯 C# 状态，与 Godot 编辑器完全解耦。
///
/// <para>迁移前是 NUnit 的 <c>[SetUp]</c> / <c>[TearDown]</c>。xUnit 没有这两个属性，
/// 它的正统形态是「构造函数即 setup、IDisposable 即 teardown」—— 而且 xUnit 本来就是
/// <b>每个测试方法新建一个实例</b>，与这里「每个用例一份全新世界」的原意天然吻合，
/// 语义没有变松：World / Log 仍是逐例独立的。</para>
/// </summary>
public abstract class LogicWorldTestBase : IDisposable
{
    protected LogicWorld World = null!;

    /// <summary>世界演化过程中收集的事件（断言事件流用）。</summary>
    protected EventLog Log = null!;

    /// <summary>每条用例都拿一份全新世界（种子固定 42，所以可重复）。</summary>
    protected LogicWorldTestBase()
    {
        World = new LogicWorld(randomSeed: 42);
        Log = new EventLog();
        World.UnitDied += Log.OnUnitDied;
        World.CombatEvent += Log.OnCombat;
        World.BountyEvent += Log.OnBounty;
        World.EconomyEvent += Log.OnEconomy;
        World.BuildingDestroyed += Log.OnBuildingDestroyed;
        World.BuildingRespawned += Log.OnBuildingRespawned;
        World.BuildingPlaced += Log.OnBuildingPlaced;
        World.GameOver += Log.OnGameOver;
        World.WaveStarted += Log.OnWave;
        World.BossArrived += Log.OnBossArrived;
    }

    /// <summary>迁移前的 <c>[TearDown]</c>：交还世界持有的资源。</summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>给派生类留的释放钩子（无派生需求时不必动）。</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            World.Dispose();
        }
    }

    /// <summary>推进 n 步（测试里的「快进旋钮」）。</summary>
    protected void Advance(int steps) => World.Advance(steps);

    /// <summary>坦克化：临时放大血上限，隔离窗口内战斗不掉线（观察需求线专用排布）。</summary>
    protected static void Tankify(Unit hero)
    {
        hero.MaxHealth += 100_000;
        hero.Health = hero.MaxHealth;
    }

    /// <summary>
    /// 把一名英雄写回卡面基准：职业那三个数（<c>docs/prototypes/infinity-lord/design.md</c>「英雄职业」那一行 —
    /// 游侠 60/8、战士 100/10、骑士 180/16，与 <c>LogicWorld.GuildClassTable</c> 同源）
    /// + 零等级零装备（<c>Xp</c> 一并清零：它与 <c>Level</c> 耦合，留着旧 XP 下一次击杀就把等级跳回去）
    /// + 满血满士气。
    ///
    /// <para><b>为什么要定住</b>：勇气评估读「感知威胁」，而感知威胁读英雄自己的
    /// <c>Attack × 2 + MaxHealth / 5 + Gear × 3</c>（<c>LogicWorld.PerceivedThreat</c>）——
    /// 等级抬血上限、装备抬攻击，两者都随整局历史涨。同一面旗因此在第 20 步与第 1801 步得到两个答案，
    /// 而走这条工具的两个用例各自只证一个因子（职业分化 / 战力感知）。</para>
    ///
    /// <para>承接判定不看距离，先遍历到谁谁先叫牌（<c>LogicWorld.StepHero</c> 按场上次序走），
    /// 所以定前提通常要把<b>全场</b>英雄都写一遍：只定要观察的那一名，次序在它前面的同名职业
    /// 照样会把旗先拿走（实测踩到过）。</para>
    /// </summary>
    protected static void PinToCardBaseline(Unit hero)
    {
        var (health, attack) = hero.Class switch
        {
            HeroClass.Knight => (180, 16),
            HeroClass.Warrior => (100, 10),
            _ => (60, 8),
        };

        hero.Level = 0;
        hero.Xp = 0;
        hero.Gear = 0;
        hero.Attack = attack;
        hero.MaxHealth = health;
        hero.Health = health;
        hero.Morale = 100;
    }

    /// <summary>
    /// 堡垒化：把宫殿血量抬到近乎无限 — 观察后段波次/终幕时排除「宫殿提前被毁 → 判负」的干扰
    /// （判负不可改写；拆冻结后判负不再停时间轴，堡垒化只为单看某一层）。
    ///
    /// <para><b>走模块那一发增量写口，不直接写血量</b>：血量账归模块（D2），2026-10-02 起
    /// 连场地建筑也在册内，写 <c>palace.Health = …</c> 会当场抛。差额算出来是
    /// <c>目标 − 当前</c>，落定的血量与迁前逐字同一个 <c>int.MaxValue / 2</c>；
    /// 而它会<b>把血量上限一并抬到那个数</b>（<c>SetHealth</c>）—— 不抬的话之后任何一次
    /// <c>Repair</c> 都会把血量钳回 8000，那正是这条写口要防的语义坑。
    /// 已经高于目标时那一发回 false、什么都不做（幂等）。</para>
    /// </summary>
    protected void FortifyPalace()
    {
        const int target = int.MaxValue / 2;
        var palace = World.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.Palace);
        if (palace is null) return;

        var gap = target - palace.Health;
        if (gap > 0) World.DebugHealBuilding(palace.Id, gap);
    }

    /// <summary>存活英雄列表。</summary>
    protected System.Collections.Generic.List<Unit> Heroes()
        => [.. World.Units.Where(u => u.Side == Faction.Hero && u.Health > 0)];

    /// <summary>存活怪物列表。</summary>
    protected System.Collections.Generic.List<Unit> Monsters()
        => [.. World.Units.Where(u => u.Side == Faction.Monster && u.Health > 0)];

    /// <summary>
    /// 金币守恒不变量断言 — 金库 + 全体英雄钱包 + 在途悬赏 = 初始金库 + Σ注水 + Σ出生盘缠 − Σ焚烧。
    ///
    /// 进出清单（单点维护在这里，各长跑/逐步断言共用 — 新切片引入任何金库异动若不入账必爆断言）：
    /// - 注水：村民税 VillagerTax、击杀战利品 KillLoot、英雄出生盘缠
    /// - 焚烧：消费未回流沉淀（60%）、阵亡英雄随身金币、建造款 BuildingBuilt、行会升级款 GuildUpgraded
    /// </summary>
    protected void AssertConservation(string context = "")
    {
        var injected = Log.Economy
            .Where(e => e.Kind is EconomyEventKind.VillagerTax or EconomyEventKind.KillLoot)
            .Sum(e => (long)e.Amount);
        var burned = Log.Economy
                .Where(e => e.Kind == EconomyEventKind.HeroSpent)
                .Sum(e => (long)e.Amount - (int)(e.Amount * LogicWorld.TaxRate))
            + Log.Deaths.Where(d => d.Side == Faction.Hero).Sum(d => (long)d.Gold)
            + Log.Economy
                .Where(e => e.Kind is EconomyEventKind.BuildingBuilt or EconomyEventKind.GuildUpgraded)
                .Sum(e => (long)e.Amount);
        var birthGold = HeroBirthGold * (Heroes().Count
            + Log.Deaths.Count(d => d.Side == Faction.Hero));
        var total = World.Treasury
            + World.Units.Sum(u => (long)u.Gold)
            + World.Flags.Where(f => !f.Settled).Sum(f => (long)f.Gold);

        total.ShouldBe(InitialTreasury + injected + birthGold - burned, $"守恒：总量 = {InitialTreasury} + Σ注水 + Σ盘缠 − Σ焚烧 {context}");
    }

    /// <summary>初始金库（与 LogicWorld 构造函数同值 — 守恒公式基准）。</summary>
    protected const int InitialTreasury = 150;

    /// <summary>英雄出生盘缠注水口（与 LogicWorld.EnqueueSpawn 的 Gold=10 同值）。</summary>
    protected const int HeroBirthGold = 10;

    /// <summary>
    /// 事件日志 — 逻辑层事件流的内存收集器（对应 MCP 验证里的 logs_read，但零丢失）。
    /// </summary>
    protected sealed class EventLog
    {
        public System.Collections.Generic.List<UnitDiedReport> Deaths { get; } = [];
        public System.Collections.Generic.List<CombatEventReport> Combats { get; } = [];
        public System.Collections.Generic.List<BountyEventReport> Bounties { get; } = [];
        public System.Collections.Generic.List<EconomyEventReport> Economy { get; } = [];
        public System.Collections.Generic.List<Building> Destroyed { get; } = [];
        public System.Collections.Generic.List<Building> Respawned { get; } = [];
        public System.Collections.Generic.List<Building> BuildingPlaced { get; } = [];

        /// <summary>终局判定流（切片 10：胜/负各一次 — 断言「恰好发一次」用）。</summary>
        public System.Collections.Generic.List<GameOutcome> Outcomes { get; } = [];

        /// <summary>波次播报流（切片 10：剧本加压层的可观测点）。</summary>
        public System.Collections.Generic.List<WaveRow> Waves { get; } = [];

        /// <summary>BOSS 巢穴降临流（切片 10：终幕层的可观测点）。</summary>
        public System.Collections.Generic.List<Building> BossArrivals { get; } = [];

        public void OnUnitDied(UnitDiedReport r) => Deaths.Add(r);
        public void OnCombat(CombatEventReport r) => Combats.Add(r);
        public void OnBounty(BountyEventReport r) => Bounties.Add(r);
        public void OnEconomy(EconomyEventReport r) => Economy.Add(r);
        public void OnBuildingDestroyed(Building b) => Destroyed.Add(b);
        public void OnBuildingRespawned(Building b) => Respawned.Add(b);
        public void OnBuildingPlaced(Building b) => BuildingPlaced.Add(b);
        public void OnGameOver(GameOutcome o) => Outcomes.Add(o);
        public void OnWave(WaveRow w) => Waves.Add(w);
        public void OnBossArrived(Building b) => BossArrivals.Add(b);

        public int Count(EconomyEventKind kind) => Economy.Count(e => e.Kind == kind);
    }
}
