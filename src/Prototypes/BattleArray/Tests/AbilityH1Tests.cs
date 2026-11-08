#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DemoFactory.GugaFramework.Abilities;
using Shouldly;
using Xunit;

namespace BattleArray.Tests;

/// <summary>
/// 技能层 H1 假数据宿主 —— 「三切片设计与确认记录」的 H1。
/// 几行数据模拟两个单位，没有世界、不起引擎：公理 A2（状态即数据）的全部风险在这暴露。
/// 断言编号对着单挑场那页第 7 节的功能组 F1~F8，新增列缺口 N-2~N-10 各自配一条。
/// </summary>
public class AbilityH1Tests
{
    // ---- 假数据宿主 ----------------------------------------------------------

    private sealed class FakeHost : IAbilityHost
    {
        private readonly AbilityWorld _world;

        public FakeHost(AbilityWorld world)
        {
            _world = world;
        }

        public AbilitySet? Find(string unitId) => _world.Units.TryGetValue(unitId, out var u) ? u : null;

        public bool IsAlive(string unitId) => Find(unitId) is { } u && u.Current["hp"] > 0;

        /// <summary>这一侧的目标册只装兵：在册就是在册（死活仍归 <see cref="IsAlive"/>）。</summary>
        public bool Exists(string entityId) => Find(entityId) is not null;

        public IReadOnlyList<string> UnitIds => _world.UnitOrder;

        public double NextRandom() => 0.5;
    }

    private static readonly string[] Empty = Array.Empty<string>();

    private static readonly ModifierSpec[] NoMods = Array.Empty<ModifierSpec>();

    private static EffectDef Instant(string id, ModifierSpec spec)
        => EffectDef.Instant(id, spec);

    private static Dictionary<string, double> Args(params (string Key, double Value)[] pairs)
        => pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

    private static CalcRegistry Calcs()
        => new CalcRegistry()
            .Register("calc.attack_flat", i => -(CalcInput.Attr(i.Source, "attack") + i.Working))
            .Register("calc.bleed_tick", i => -i.Working)
            .Register("calc.scale_by_missing_hp", i =>
                CalcInput.Attr(i.Target, "hp_max") > 0
                    ? i.Working * (1 + (1 - CalcInput.Attr(i.Target, "hp") / CalcInput.Attr(i.Target, "hp_max"))
                        * i.Param("ratio"))
                    : i.Working)
            // tag 参与计算走同一个 Calc 口，不给 tag 单开一条通道（archer.mark 的形状）。
            .Register("calc.tag_multiplier", i =>
                i.TagCount.TryGetValue("status.marked", out var c) && c > 0
                    ? i.Working * i.Param("multiplier")
                    : i.Working);

    private static (AbilityWorld World, AbilityStepper Step, AbilityTables Tables) NewDuel(
        bool enemyMarked = false)
    {
        var world = new AbilityWorld();
        var tables = new AbilityTables();

        tables
            // 立即结算的一刀：伤害走 Calc 链，链的输入是 commit 时抄下来的源属性快照。
            .Register(Instant("fx.slash", new ModifierSpec("hp", ModifierMode.Add, 0, true)) with
            {
                CalcNames = new[] { "calc.attack_flat" },
            })
            .Register(Instant("fx.hit20", new ModifierSpec("hp", ModifierMode.Add, -20)))
            .Register(EffectDef.Delayed("fx.arrow", new ModifierSpec("hp", ModifierMode.Add, 0, true), 6) with
            {
                CalcNames = new[] { "calc.attack_flat" },
            })
            .Register(EffectDef.Delayed("fx.part1", new ModifierSpec("hp", ModifierMode.Add, -3), 3))
            .Register(EffectDef.Delayed("fx.part2", new ModifierSpec("hp", ModifierMode.Add, -5), 7))
            .Register(Instant("fx.rage8", new ModifierSpec("rage", ModifierMode.Add, 8)))
            .Register(Instant("fx.block", new ModifierSpec("damage_taken", ModifierMode.Override, 0)))
            // 持续效果：Modifiers 挂身上；PeriodTicks 决定它是「每 N 步再打一次」而不是「一直改着」。
            .Register(EffectDef.OverTime("fx.slow", new[]
            {
                new ModifierSpec("move_speed", ModifierMode.Mult, 0.7),
            }, durationTicks: 50))
            .Register(EffectDef.OverTime("fx.slow_if_mounted", new[]
            {
                new ModifierSpec("move_speed", ModifierMode.Mult, 0.5,
                    RequirementTags: new[] { "status.mounted" }),
            }, durationTicks: 50))
            .Register(EffectDef.OverTime("fx.bleed", new[]
            {
                new ModifierSpec("hp", ModifierMode.Add, 0, CalcFromEffectValue: true),
            }, durationTicks: 60, periodTicks: 5, stackKey: "bleed", maxStacks: 3,
                magnitudePerStack: -2, calcNames: new[] { "calc.bleed_tick" }, calcSeed: 4))
            .Register(EffectDef.OverTime("fx.guard", new[]
            {
                new ModifierSpec("damage_taken", ModifierMode.Override, 0.5),
            }, durationTicks: 50, grantedTags: new[] { "state.guard" },
                cancelMatchingTags: new[] { "state.channeling" }))
            .Register(EffectDef.OverTime("fx.root", NoMods, durationTicks: 20,
                grantedTags: new[] { "status.rooted" }))
            .Register(EffectDef.OverTime("fx.mounted", NoMods, durationTicks: 0,
                grantedTags: new[] { "status.mounted" }, kind: EffectKind.Infinite))
            .Register(EffectDef.OverTime("fx.marked", NoMods, durationTicks: 80,
                grantedTags: new[] { "status.marked" }))
            .Register(EffectDef.OverTime("fx.silence", NoMods, durationTicks: 40,
                grantedTags: new[] { "status.silence" }))
            .Register(EffectDef.OverTime("fx.rage_on_hit", NoMods, durationTicks: 0,
                kind: EffectKind.Infinite,
                triggeredEffects: new[] { ("damage_taken", "fx.rage8") }));

        tables.Register(new AbilityDef("ab.rift", PreSteps: 3, CooldownTicks: 20,
            GrantedTagsWhileActive: Empty, RequirementTags: Empty,
            BlockedTags: new[] { "status.silence" }, EffectIds: new[] { "fx.slash" },
            CostAttribute: "rage", CostAmount: 30, RangeMeters: 3, MaxCharges: 0));
        tables.Register(new AbilityDef("ab.channel", PreSteps: 5, CooldownTicks: 0,
            GrantedTagsWhileActive: new[] { "state.channeling" }, RequirementTags: Empty,
            BlockedTags: Empty, EffectIds: new[] { "fx.slash" },
            CostAttribute: string.Empty, CostAmount: 0, RangeMeters: 3, MaxCharges: 0));
        tables.Register(new AbilityDef("ab.arrow", PreSteps: 1, CooldownTicks: 10,
            GrantedTagsWhileActive: Empty, RequirementTags: Empty, BlockedTags: Empty,
            EffectIds: new[] { "fx.arrow" }, CostAttribute: string.Empty, CostAmount: 0,
            RangeMeters: 10, MaxCharges: 0));
        tables.Register(new AbilityDef("ab.two_part", PreSteps: 0, CooldownTicks: 0,
            GrantedTagsWhileActive: Empty, RequirementTags: Empty, BlockedTags: Empty,
            EffectIds: new[] { "fx.part1", "fx.part2" }, CostAttribute: string.Empty, CostAmount: 0,
            RangeMeters: 10, MaxCharges: 0));
        tables.Register(new AbilityDef("ab.silence", PreSteps: 0, CooldownTicks: 0,
            GrantedTagsWhileActive: Empty, RequirementTags: Empty, BlockedTags: Empty,
            EffectIds: new[] { "fx.silence" }, CostAttribute: string.Empty, CostAmount: 0,
            RangeMeters: 10, MaxCharges: 0));

        var me = world.AddUnit("u1", new[]
        {
            ("hp", 100.0), ("attack", 10.0), ("rage", 40.0), ("move_speed", 4.0),
            ("damage_taken", 1.0), ("cooldown.ab.rift", 1.0),
        });
        var foe = world.AddUnit("u2", new[]
        {
            ("hp", 100.0), ("hp_max", 100.0), ("attack", 8.0), ("rage", 0.0),
            ("move_speed", 4.0), ("damage_taken", 1.0), ("cooldown.ab.rift", 1.0),
        });
        world.SetLimits("u1", "hp", 0, 100);
        world.SetLimits("u2", "hp", 0, 100);
        world.SetLimits("u1", "rage", 0, 100);
        world.SetLimits("u2", "rage", 0, 100);
        world.SetLimits("u1", "damage_taken", 0.2, 1);
        world.SetLimits("u2", "damage_taken", 0.2, 1);
        if (enemyMarked)
        {
            me.TagCounts["status.marked"] = 1;
        }

        var step = new AbilityStepper(world, new FakeHost(world), Calcs(), tables, Config());

        // 距离那把尺：站位归这份假数据（层里不再有坐标），两个兵固定隔 2 米。
        step.DistanceOf = (_, _) => 2.0;

        return (world, step, tables);
    }

    /// <summary>
    /// 接层那份配置。公共层那五列词表一个缺省都没有，所以词表归这份假数据宿主自己给
    /// （给的就是本文件一直在用的那五个名字，所以行为与旧缺省逐字相同）。
    /// </summary>
    private static AbilityLayerConfig Config(int eventCapacity = 2000) => new()
    {
        HealthAttribute = "hp",
        DamageTakenAttribute = "damage_taken",
        CooldownAttributePrefix = "cooldown.",
        RootedTag = "status.rooted",
        DamageBlockTag = "status.block_ready",
        DamageTakenEvent = "damage_taken",
        EffectAppliedEvent = "effect_applied",
        EventCapacity = eventCapacity,
    };

    private static void Advance(AbilityStepper step, int steps)
    {
        for (var i = 0; i < steps; i++)
        {
            step.AdvanceStep();
        }
    }

    private static double Cur(AbilityWorld world, string unit, string attr) => world.Units[unit].Current[attr];

    private static string Line(string snapshot, string prefix)
        => snapshot.Split('\n').FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal)) ?? "-";

    // ---- 4.3 Override 与上下界的先后 -----------------------------------------

    /// <summary>Override 先把承伤顶成 0.5，界在 0.2~1 之内 → 原样出。</summary>
    [Fact]
    public void Clamp_在_Override_之后_界内原样出()
    {
        var (world, step, _) = NewDuel();
        step.InjectModifier("u2", "damage_taken", ModifierMode.Override, 0.5, expiresAtTick: 99);
        Cur(world, "u2", "damage_taken").ShouldBe(0.5);
    }

    /// <summary>同一条界在 Override 顶出界时把它抬回 0.2 —— 界归属性、不归求值器。</summary>
    [Fact]
    public void Clamp_在_Override_之后_界外被抬回()
    {
        var (world, step, _) = NewDuel();
        step.InjectModifier("u2", "damage_taken", ModifierMode.Override, 0.1, expiresAtTick: 99);
        Cur(world, "u2", "damage_taken").ShouldBe(0.2);
    }

    // ---- F8 modifier 排序键 ---------------------------------------------------

    /// <summary>同一步两条 Override：结果由 (AppliedAtTick, Seq) 决定，不是由字典顺序决定。</summary>
    [Fact]
    public void 同一步两条修饰符_排序键定出唯一结果()
    {
        var (world, step, _) = NewDuel();
        step.InjectModifier("u2", "attack", ModifierMode.Override, 3);
        step.InjectModifier("u2", "attack", ModifierMode.Override, 7);
        Cur(world, "u2", "attack").ShouldBe(7, "后施加的那条 Seq 更大，赢");

        var again = NewDuel();
        again.Step.InjectModifier("u2", "attack", ModifierMode.Override, 3);
        again.Step.InjectModifier("u2", "attack", ModifierMode.Override, 7);
        again.Step.TakeSnapshot().ShouldBe(step.TakeSnapshot(), "两跑逐字全等");
    }

    // ---- N-5 修饰符自带条件 ----------------------------------------------------

    /// <summary>条件不满足 = 这条不生效，但记录还在、界面上念得出它此刻不生效（不满足 ≠ 撤销）。</summary>
    [Fact]
    public void N5_条件修饰符_不满足时跳过不撤销()
    {
        var (world, step, _) = NewDuel();
        step.InjectEffect("u2", "fx.slow_if_mounted");
        Cur(world, "u2", "move_speed").ShouldBe(4, "没骑乘 → ×0.5 不生效");
        world.Units["u2"].Modifiers.Count.ShouldBe(1, "修饰符记录照旧挂着");

        step.InjectEffect("u2", "fx.mounted");
        Cur(world, "u2", "move_speed").ShouldBe(2, "骑上 → 同一条立刻生效");
        world.Units["u2"].Effects.Any(e => e.EffectId == "fx.slow_if_mounted").ShouldBeTrue("效果没被撤过");
    }

    // ---- F1 commit 点前后取消的返还差 -----------------------------------------

    /// <summary>前摇中取消：Cost 一分没扣、冷却也不给。</summary>
    [Fact]
    public void F1_commit_前取消_不扣Cost不给冷却()
    {
        var (world, step, _) = NewDuel();
        step.TryActivate("u1", "ab.rift", "u2");
        Advance(step, 1);
        step.InjectCancel("u1", "ab.rift").ShouldBeTrue();

        Cur(world, "u1", "rage").ShouldBe(40, "怒气未动");
        world.Units["u1"].Abilities["ab.rift"].CooldownEndsAtTick.ShouldBeNull();
        world.Units["u1"].Abilities["ab.rift"].Phase.ShouldBe(AbilityPhase.Ready);
        Line(step.TakeSnapshot(), "a;ab.rift").ShouldContain("Interrupted");
    }

    /// <summary>过了 commit 被打断：Cost 照扣、冷却照给（钉的就是 warrior.rift 这一格）。</summary>
    [Fact]
    public void F1_commit_后取消_扣Cost留冷却()
    {
        var (world, step, _) = NewDuel();
        step.TryActivate("u1", "ab.rift", "u2");
        Advance(step, 3);
        Cur(world, "u1", "rage").ShouldBe(10, "过点那一刻扣掉 30");
        var cooldown = world.Units["u1"].Abilities["ab.rift"].CooldownEndsAtTick;
        cooldown.ShouldNotBeNull();

        step.InjectCancel("u1", "ab.rift");
        world.Units["u1"].Abilities["ab.rift"].CooldownEndsAtTick.ShouldBe(cooldown, "已提交的冷却不还");
    }

    // ---- F2 BlockedTags 只管入口 ----------------------------------------------

    /// <summary>在放的能力对中途新增的 tag 无感：激活检查只管入口，打断另有其口（S3 的取消段）。</summary>
    [Fact]
    public void F2_BlockedTags_只管入口_管不住在放中的能力()
    {
        var (world, step, _) = NewDuel();
        step.TryActivate("u1", "ab.channel", "u2");
        Advance(step, 1);
        step.InjectEffect("u1", "fx.silence");

        world.Units["u1"].TagCounts.ContainsKey("status.silence").ShouldBeTrue();
        Advance(step, 4);
        Cur(world, "u2", "hp").ShouldBe(90, "前摇照走完、结算照样落地");
    }

    /// <summary>入口那一发问得到 tag：被沉默就是放不下，并且「为什么没放」有名字。</summary>
    [Fact]
    public void F2_入口被挡_拒绝有名字()
    {
        var (world, step, _) = NewDuel();
        step.InjectEffect("u1", "fx.silence");
        step.TryActivate("u1", "ab.rift", "u2").ShouldBeFalse();
        world.Units["u1"].Abilities["ab.rift"].RejectedReason.ShouldBe(AbilityRejectReason.BlockedByTag);
        Line(step.TakeSnapshot(), "a;ab.rift").ShouldContain("BlockedByTag");
    }

    // ---- F3 显式打断 + N-4 位移激活检查 -------------------------------------------

    /// <summary>挂着 state.channeling 的能力被带 CancelMatchingTags 的效果打断，且行为流留一行。</summary>
    [Fact]
    public void F3_CancelMatchingTags_显式打断在放的能力()
    {
        var (_, step, _) = NewDuel();
        step.TryActivate("u1", "ab.channel", "u2");
        Advance(step, 1);

        // 效果挂在放技能的那个人身上：谁被取消，看的是谁挂着那条 tag（GAS 的 CancelAbilitiesWithTag 同构）。
        step.InjectEffect("u1", "fx.guard");
        Advance(step, 1);

        var eventWires = step.World.Events.All.Select(e => e.Payload.Wire).ToList();
        eventWires.ShouldContain("interrupt", "取消是显式动作，不打折不留痕就等于没发生（形状 AbilityInterrupted）");
        step.World.Events.Where<AbilityInterrupted>((_, p) => !p.Committed).Any().ShouldBeTrue("还没过 commit 点 → 形状 AbilityInterrupted 的 Committed=false（旧文本 committed=0），Cost 与冷却都不消耗");
    }

    /// <summary>N-4：定身 tag 挡不住 S4 的入口，但挡住 S5 的腿 —— 两处问、一份实现。</summary>
    [Fact]
    public void N4_定身挡住位移_S5_问的是同一条()
    {
        var (world, step, _) = NewDuel();
        step.InjectEffect("u1", "fx.root");
        step.CanMove("u1").ShouldBeFalse();
        step.TryMove("u1", 1).ShouldBeFalse("定身挂着走不动 —— 这一格只剩激活检查：坐标归宿主，层里没有可写的格子");
        step.World.Events.Count<MoveBlocked>().ShouldBeGreaterThan(0, "为什么没走要读得出来（形状 MoveBlocked，旧文本 move_blocked）");
        world.Units.ContainsKey("u1").ShouldBeTrue("被挡住也不影响技能栏本身");
    }

    // ---- F4 Duration ≠ period --------------------------------------------------

    /// <summary>只改周期那一列，时长纹丝不动；每跳伤害按层数加。</summary>
    [Fact]
    public void F4_时长与周期两列各自存在()
    {
        var (world, step, _) = NewDuel();
        step.InjectEffect("u2", "fx.bleed");
        var bleed = world.Units["u2"].Effects.Single(e => e.EffectId == "fx.bleed");
        bleed.ExpiresAtTick.ShouldBe(world.Tick + 60, "时长 6 秒");
        _tables(step).Effect("fx.bleed").PeriodTicks.ShouldBe(5, "周期 0.5 秒");

        var before = Cur(world, "u2", "hp");
        Advance(step, 5);
        Cur(world, "u2", "hp").ShouldBe(before - 4, "到一跳掉一跳的血");
        world.Units["u2"].Effects.Single(e => e.EffectId == "fx.bleed").ExpiresAtTick.ShouldBe(bleed.ExpiresAtTick, "改周期不碰时长");
    }

    // ---- F5 tag 两段汇总 --------------------------------------------------------

    /// <summary>施法期授予的 tag 来自能力段，持续效果的 tag 来自效果段，S2 一处收口。</summary>
    [Fact]
    public void F5_tag_两段汇总_能力段与效果段都念得出()
    {
        var (world, step, _) = NewDuel();
        step.TryActivate("u1", "ab.channel", "u2");
        Advance(step, 1);
        world.Units["u1"].TagCounts.GetValueOrDefault("state.channeling").ShouldBe(1, "能力段");

        step.InjectEffect("u1", "fx.guard");
        world.Units["u1"].TagCounts.GetValueOrDefault("state.guard").ShouldBe(1, "效果段");
        world.Units["u1"].TagCounts.GetValueOrDefault("state.channeling").ShouldBe(1, "两段合在一张计数表上");
    }

    // ---- F6 Calc 走注册函数（含源属性快照冻结）---------------------------------

    /// <summary>伤害按 commit 时抄下的攻击算：施法者之后被削攻，已在路上的那一发不受影响。</summary>
    [Fact]
    public void F6_Calc走注册函数_源属性是快照不是活值()
    {
        var (world, step, _) = NewDuel();
        step.TryActivate("u1", "ab.arrow", "u2");
        Advance(step, 1);
        world.Units["u1"].Pendings.Count.ShouldBe(1, "箭在路上");

        step.InjectModifier("u1", "attack", ModifierMode.Override, 1);
        Advance(step, 6);
        Cur(world, "u2", "hp").ShouldBe(90, "落地用的还是抄下来的攻击 10");
    }

    // ---- F7 + N-9 Pending 登记即入快照、多条互不顶掉 ----------------------------

    /// <summary>取消技能不取消已登记的结算：两段 Pending 各占一条、错峰各自落地。</summary>
    [Fact]
    public void F7_N9_两条结算互不顶掉_取消技能也不撤已登记的()
    {
        var (world, step, _) = NewDuel();
        step.TryActivate("u1", "ab.two_part", "u2");
        Advance(step, 1);
        world.Units["u1"].Pendings.Count.ShouldBe(2, "同一次激活登记两条");
        step.TakeSnapshot().ShouldContain("p;", Case.Sensitive, "登记即入快照");

        step.InjectCancel("u1", "ab.two_part");
        var hp = Cur(world, "u2", "hp");
        Advance(step, 3);
        Cur(world, "u2", "hp").ShouldBe(hp - 3, "第一段照结");
        Advance(step, 4);
        Cur(world, "u2", "hp").ShouldBe(hp - 8, "第二段错峰落地");
    }

    /// <summary>目标按 Id 在落地那一步现解：中途被打死，结算作废并念得出为什么作废。</summary>
    [Fact]
    public void 目标阵亡时结算作废_作废有计数有事件()
    {
        var (world, step, _) = NewDuel();
        step.TryActivate("u1", "ab.arrow", "u2");
        Advance(step, 1);
        step.InjectEffect("u1", "fx.hit20"); // 挨打的是挂着被动的那个单位
        step.InjectSetBase("u2", "hp", 0);
        RecalcVia(step, world);

        Advance(step, 6);
        world.Units["u1"].Counters.GetValueOrDefault("void_count").ShouldBe(1);
        step.World.Events.Count<EffectMissed>().ShouldBeGreaterThan(0, "作废要发得出事件（形状 EffectMissed，旧文本 settle_void）");
    }

    private static void RecalcVia(AbilityStepper step, AbilityWorld world)
    {
        // 直接改基值后读一次快照，逼 S2 重算一遍（注入口的正常路径）。
        step.InjectModifier("u1", "attack", ModifierMode.Add, 0);
    }

    // ---- N-6 叠层 --------------------------------------------------------------

    /// <summary>叠到封顶、每层加伤，且不重置时长（warrior.bleed 的口径）。</summary>
    [Fact]
    public void N6_叠层四格_封顶_每层加伤_不刷时长()
    {
        var (world, step, _) = NewDuel();
        step.InjectEffect("u2", "fx.bleed");
        var expires = world.Units["u2"].Effects.Single(e => e.EffectId == "fx.bleed").ExpiresAtTick;
        step.InjectEffect("u2", "fx.bleed");
        step.InjectEffect("u2", "fx.bleed");
        step.InjectEffect("u2", "fx.bleed");

        var bleed = world.Units["u2"].Effects.Single(e => e.EffectId == "fx.bleed");
        bleed.Stacks.ShouldBe(3, "MaxStacks 封顶");
        bleed.ExpiresAtTick.ShouldBe(expires, "AddStackKeepDuration 不刷时长");

        var before = Cur(world, "u2", "hp");
        Advance(step, 5);
        Cur(world, "u2", "hp").ShouldBe(before - 8, "每层加 2 点伤 → 三层一跳 8 血");
    }

    /// <summary>tag 计数被消耗：一次性抵挡靠 InjectRemoveEffect 的单层版（归属表里「谁能写」那一行）。</summary>
    [Fact]
    public void N6_层数可以单独消耗一层()
    {
        var (world, step, _) = NewDuel();
        step.InjectEffect("u2", "fx.bleed");
        step.InjectEffect("u2", "fx.bleed");
        step.InjectRemoveEffect("u2", "fx.bleed", stacks: 1);
        world.Units["u2"].Effects.Single(e => e.EffectId == "fx.bleed").Stacks.ShouldBe(1);
    }

    // ---- N-7 冷却走 attribute 口 -----------------------------------------------

    /// <summary>外部改冷却时长 = 一条普通 Mult modifier 打在 cooldown.&lt;技能Id&gt; 上，不给技能开特例列。</summary>
    [Fact]
    public void N7_冷却走属性口_倍率只影响之后的冷却()
    {
        var (world, step, _) = NewDuel();
        step.TryActivate("u1", "ab.rift", "u2");
        Advance(step, 3);
        var plain = world.Units["u1"].Abilities["ab.rift"].CooldownEndsAtTick!.Value;
        plain.ShouldBe(world.Tick + 20, "名义 20 步");

        step.InjectSetBase("u1", "rage", 100); // 第二次释放的怒气另给
        step.InjectSetCooldown("u1", "ab.rift", 0);
        Advance(step, 20);
        step.InjectModifier("u1", "cooldown.ab.rift", ModifierMode.Mult, 1.3);
        step.TryActivate("u1", "ab.rift", "u2");
        Advance(step, 3);
        world.Units["u1"].Abilities["ab.rift"].CooldownEndsAtTick.ShouldBe(world.Tick + 26, "×1.3 → 26 步（20 × 1.3 四舍五入）");
    }

    // ---- N-10 激活检查表达式 --------------------------------------------------------

    /// <summary>RequirementTags 全满足才放行、BlockedTags 任一命中即拒。</summary>
    [Fact]
    public void N10_激活检查表达式_全满足与任一命中()
    {
        var (world, step, tables) = NewDuel();
        tables.Register(new AbilityDef("ab.need_both", PreSteps: 0, CooldownTicks: 0,
            GrantedTagsWhileActive: Empty, RequirementTags: new[] { "status.mounted", "status.marked" },
            BlockedTags: Empty, EffectIds: new[] { "fx.slash" }, CostAttribute: string.Empty,
            CostAmount: 0, RangeMeters: 5, MaxCharges: 0));
        step = Rebuild(world, tables);

        step.TryActivate("u1", "ab.need_both", "u2").ShouldBeFalse("一条满足都不算");
        step.InjectEffect("u1", "fx.mounted");
        step.TryActivate("u1", "ab.need_both", "u2").ShouldBeFalse("两条都要满足");
        step.InjectEffect("u1", "fx.marked");
        step.TryActivate("u1", "ab.need_both", "u2").ShouldBeTrue();
        world.Units["u1"].Abilities["ab.need_both"].Phase.ShouldBe(AbilityPhase.Pre, "两条都满足才放行；放行后进 Pre（0 步前摇的技能也要等下一步 S3 才过 commit 点）");
    }

    /// <summary>换一张表重接一层。<b>距离那把尺要一并递</b>（层里不再有坐标，缺了就当场抛）。</summary>
    private static AbilityStepper Rebuild(AbilityWorld world, AbilityTables tables)
    {
        var step = new AbilityStepper(world, new FakeHost(world), Calcs(), tables, Config());
        step.DistanceOf = (_, _) => 2.0;
        return step;
    }

    // ---- N-2 事件触发效果 --------------------------------------------------------

    /// <summary>受击 → 加怒：登记表走 TriggeredEffects，回调里不写真值，登记成 Pending 下一步结。</summary>
    [Fact]
    public void N2_事件触发效果_登记成待决结算()
    {
        var (world, step, _) = NewDuel();
        step.InjectEffect("u1", "fx.rage_on_hit");
        step.InjectEffect("u1", "fx.hit20"); // 挨打的是挂着被动的那个单位
        world.Units["u1"].Pendings.Count.ShouldBe(1, "触发登记成 Pending，不当场改数值");
        Cur(world, "u1", "rage").ShouldBe(40, "本步怒气未动（因果不倒）");

        Advance(step, 1);
        Cur(world, "u1", "rage").ShouldBe(48, "下一步 S0 结算 +8");
        step.World.Events.Count<TriggerFired>().ShouldBeGreaterThan(0, "触发链点火要发得出事件（形状 TriggerFired，旧文本 trigger）");
    }

    /// <summary>怒气上限 100 是属性的界，不是触发器里的 if。</summary>
    [Fact]
    public void 资源型属性靠界封顶()
    {
        var (world, step, _) = NewDuel();
        step.InjectSetBase("u1", "rage", 96);
        step.InjectEffect("u1", "fx.rage_on_hit");
        for (var i = 0; i < 3; i++)
        {
            step.InjectEffect("u1", "fx.hit20"); // 挨打的是挂着被动的那个单位
            Advance(step, 1);
        }

        Cur(world, "u1", "rage").ShouldBe(100, "96+8 被界抬回 100 以下再收 8 → 顶到 100");
    }

    // ---- D6 往返测试（状态可回跳）-----------------------------------------------

    /// <summary>快照 → 重建 → 再快照，逐字相等；不等就是 A2 破了，而差在哪一行指得出来。</summary>
    [Fact]
    public void D6_快照往返_逐字相等()
    {
        var (world, step, _) = NewDuel();
        step.TryActivate("u1", "ab.arrow", "u2");
        step.InjectEffect("u2", "fx.bleed");
        step.InjectEffect("u2", "fx.guard");
        Advance(step, 4);
        var first = step.TakeSnapshot();

        var rebuilt = step.Branch(first);
        var second = rebuilt.TakeSnapshot();
        second.ShouldBe(first, "差在哪一行可直接 diff：\n" + FirstDiff(first, second));
    }

    /// <summary>D7 分支对照：从检查点起跳、改一笔输入，起跳成本是 O(1)。</summary>
    [Fact]
    public void D7_从检查点分叉_改一笔输入()
    {
        var (_, step, _) = NewDuel();
        step.TryActivate("u1", "ab.rift", "u2");
        Advance(step, 2);
        var checkpoint = step.TakeSnapshot();

        var a = step.Branch(checkpoint);
        Advance(a, 3);
        var b = step.Branch(checkpoint);
        b.InjectModifier("u1", "attack", ModifierMode.Override, 2);
        Advance(b, 3);

        a.World.Units["u2"].Current["hp"].ShouldBe(90);
        b.World.Units["u2"].Current["hp"].ShouldBe(98, "同一检查点分叉，只差那一笔");
    }

    /// <summary>
    /// 分叉不带配置时<b>沿用父层那一份</b>（用户确认）：分叉出来的是同一条时间线的另一支，
    /// 配置属于层而不属于调用点。以前缺省 null 会让事件流容量归零当场抛，逼调用点各递一份一模一样的。
    /// 真要换一档跑对照，显式递进来仍然接得住。
    /// </summary>
    [Fact]
    public void D7b_分叉缺省沿用父层配置()
    {
        var (_, step, _) = NewDuel();
        var checkpoint = step.TakeSnapshot();

        var inherited = step.Branch(checkpoint);
        inherited.World.Events.Capacity.ShouldBe(step.World.Events.Capacity, "不带 config 的分叉，事件流容量应与父层同格");

        var overridden = step.Branch(checkpoint, Config(eventCapacity: 7));
        overridden.World.Events.Capacity.ShouldBe(7, "显式那一档仍要接得住");
    }

    /// <summary>
    /// 注入口挂上的修饰符与效果路径<b>念同一种事件行</b>（用户确认补的那一格）：
    /// 以前注入口发的是不带 `til=` 的第三种写法，到期步在快照里有、在事件里没有。
    /// </summary>
    [Fact]
    public void 注入口挂的修饰符与效果路径念同一种事件行()
    {
        var (_, step, _) = NewDuel();

        step.InjectModifier("u1", "attack", ModifierMode.Add, 3, expiresAtTick: 42);
        step.InjectModifier("u2", "attack", ModifierMode.Add, 5);

        step.World.Events.Count<ModifierAppliedUntil>().ShouldBe(1, "给了到期步就该与效果路径同形，til= 那一格不许丢");
        step.World.Events.Count<ModifierAppliedOpenEnded>().ShouldBe(1, "没给到期步念 til=-，不另发明第三种写法");
        step.World.Events.Tail(2)[0].Line.ShouldContain("til=42", Case.Sensitive, "事件行念得出到期步，读数与快照同源");
    }

    private static string FirstDiff(string a, string b)
    {
        var la = a.Split('\n');
        var lb = b.Split('\n');
        for (var i = 0; i < Math.Min(la.Length, lb.Length); i++)
        {
            if (la[i] != lb[i])
            {
                return $"第 {i + 1} 行\n  存: {la[i]}\n  回: {lb[i]}";
            }
        }

        return la.Length == lb.Length ? "(无差异)" : $"行数 {la.Length} vs {lb.Length}";
    }

    private static AbilityTables _tables(AbilityStepper step) => step.Tables;
}
