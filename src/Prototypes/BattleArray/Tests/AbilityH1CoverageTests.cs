#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DemoFactory.GugaFramework.Abilities;
using Shouldly;
using Xunit;

namespace BattleArray.Tests;

/// <summary>
/// <b>GAS 功能面逐格在 H1 的那七格</b>（2026-09-28 P3 从 <see cref="AbilityChecklistTests"/> 里搬出来的）。
///
/// 为什么搬：那份文件原来同时装着两样东西——「16 条检查清单」与「覆盖矩阵里 H1 能承接的那几格」。
/// P3 把清单抽成宿主无关的一份之后，清单文件只剩 16 条；剩下这七格<b>不是清单条目</b>，
/// 是 16 条覆盖矩阵里 H1 能承接的那几行（矩阵口径见 <c>docs/research/技能系统/调研结论摘要.md</c> 5.3 节），
/// 已删的验证报告曾逐行指着它们当证据（曲线 / 免疫 / tag 层级方向 / 蓄力松手 / Cost 快照 / 充能 / 等条件每步重估）。
/// 它们仍然只在 H1 跑：载体是几行假数据，不需要世界。
/// </summary>
public class AbilityH1CoverageTests
{
    private sealed class FakeHost : IAbilityHost
    {
        private readonly AbilityWorld _world;

        public FakeHost(AbilityWorld world)
        {
            _world = world;
        }

        /// <summary>两个兵此刻隔多远（米）——<b>站位归这份假数据自己</b>，层里不再有坐标了。</summary>
        public double Gap { get; set; } = 5.0;

        /// <summary>目标往外走了 <paramref name="dx"/> 米：宿主改自己那份站位，层里的距离随之变化。</summary>
        public void WalkTarget(double dx) => Gap += dx;

        public AbilitySet? Find(string unitId) => _world.Units.TryGetValue(unitId, out var u) ? u : null;

        public bool IsAlive(string unitId) => Find(unitId) is { } u && u.Current["hp"] > 0;

        /// <summary>这一侧的目标册只装兵：在册就是在册（死活仍归 <see cref="IsAlive"/>）。</summary>
        public bool Exists(string entityId) => Find(entityId) is not null;

        public IReadOnlyList<string> UnitIds => _world.UnitOrder;

        public double NextRandom() => 0.5;
    }

    private static readonly string[] Empty = Array.Empty<string>();

    private static readonly ModifierSpec[] NoMods = Array.Empty<ModifierSpec>();

    private static readonly (string OnEvent, string EffectId)[] NoTriggers =
        Array.Empty<(string, string)>();

    private static readonly ModifierSpec HpByChain =
        new("hp", ModifierMode.Add, 0, CalcFromEffectValue: true);

    private static Dictionary<string, double> Args(params (string Key, double Value)[] pairs)
        => pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

    private static CalcRegistry Calcs()
        => new CalcRegistry()
            .Register("calc.attack_flat", i => -(CalcInput.Attr(i.Source, "attack") + i.Working))
            // 处决：按目标已损失生命加伤——读的是落地那一步的目标血。
            .Register("calc.flat_plus_missing", i =>
                -(CalcInput.Attr(i.Target, "hp_max") - CalcInput.Attr(i.Target, "hp")) - i.Working)
            // 血线谓词的数值版：目标血量比例低于阈值才放大，否则原样。
            .Register("calc.execute_gate", i =>
                CalcInput.Attr(i.Target, "hp") / CalcInput.Attr(i.Target, "hp_max") <= i.Param("below")
                    ? i.Working * i.Param("bonus")
                    : i.Working);

    private static (AbilityWorld World, AbilityStepper Step, AbilityTables Tables, FakeHost Host) NewArena()
    {
        var world = new AbilityWorld();
        var tables = new AbilityTables();

        tables
            .Register(new EffectDef("fx.slash", EffectKind.Instant, new[] { HpByChain },
                Empty, Empty, Empty, new[] { "calc.attack_flat" }, Args(), 0, 0, Empty, NoTriggers))
            // 穿刺箭：飞行 6 步、伤害按「落地那一格的距离」折曲线 (0,1.0)(6,0.8)(12,0.5)。
            .Register(new EffectDef("fx.arrow", EffectKind.Instant, new[] { HpByChain },
                Empty, Empty, Empty, new[] { "calc.attack_flat" }, Args(), 0, 0, Empty, NoTriggers)
            {
                DelayTicks = 6,
                CurveXY = new[] { 0.0, 1.0, 6.0, 0.8, 12.0, 0.5 },
                CurveAxis = "distance",
            })
            .Register(new EffectDef("fx.execute", EffectKind.Instant,
                new[] { new ModifierSpec("hp", ModifierMode.Add, -30, CalcFromEffectValue: true) },
                Empty, Empty, Empty, new[] { "calc.flat_plus_missing" }, Args(), 30, 0, Empty, NoTriggers))
            .Register(EffectDef.OverTime("fx.root", NoMods, durationTicks: 20,
                grantedTags: new[] { "cc.rooted" }))
            .Register(EffectDef.OverTime("fx.cursed", NoMods, durationTicks: 20,
                grantedTags: new[] { "cc.rooted" },
                immunityTags: new[] { "cc" }))
            .Register(EffectDef.OverTime("fx.mounted", NoMods, durationTicks: 0,
                grantedTags: new[] { "status.mounted" }, kind: EffectKind.Infinite))
            .Register(EffectDef.OverTime("fx.marked", NoMods, durationTicks: 80,
                grantedTags: new[] { "status.marked" }))
            .Register(EffectDef.OverTime("fx.cancel_channeling", NoMods, durationTicks: 10,
                cancelMatchingTags: new[] { "state.channeling" }));

        tables.Register(new AbilityDef("ab.arrow", PreSteps: 1, CooldownTicks: 10,
            GrantedTagsWhileActive: Empty, RequirementTags: Empty, BlockedTags: Empty,
            EffectIds: new[] { "fx.arrow" }, CostAttribute: string.Empty, CostAmount: 0,
            RangeMeters: 30, MaxCharges: 0));
        tables.Register(new AbilityDef("ab.charge", PreSteps: 6, CooldownTicks: 0,
            GrantedTagsWhileActive: new[] { "cc" }, RequirementTags: Empty, BlockedTags: Empty,
            EffectIds: Empty, CostAttribute: string.Empty, CostAmount: 0, RangeMeters: 30,
            MaxCharges: 0));
        tables.Register(new AbilityDef("ab.execute", PreSteps: 0, CooldownTicks: 0,
            GrantedTagsWhileActive: new[] { "state.channeling" }, RequirementTags: Empty,
            BlockedTags: Empty, EffectIds: new[] { "fx.execute" }, CostAttribute: "rage",
            CostAmount: 30, RangeMeters: 5, MaxCharges: 0));
        tables.Register(new AbilityDef("ab.two_shots", PreSteps: 0, CooldownTicks: 0,
            GrantedTagsWhileActive: Empty, RequirementTags: Empty, BlockedTags: Empty,
            EffectIds: new[] { "fx.slash" }, CostAttribute: string.Empty, CostAmount: 0,
            RangeMeters: 30, MaxCharges: 2));
        tables.Register(new AbilityDef("ab.need_tag", PreSteps: 0, CooldownTicks: 0,
            GrantedTagsWhileActive: Empty, RequirementTags: new[] { "status.marked" },
            BlockedTags: Empty, EffectIds: new[] { "fx.slash" }, CostAttribute: string.Empty,
            CostAmount: 0, RangeMeters: 30, MaxCharges: 0));

        var me = world.AddUnit("u1", new[]
        {
            ("hp", 100.0), ("hp_max", 100.0), ("attack", 10.0), ("rage", 100.0),
            ("move_speed", 4.0), ("damage_taken", 1.0),
        });
        var foe = world.AddUnit("u2", new[]
        {
            ("hp", 100.0), ("hp_max", 100.0), ("attack", 8.0), ("rage", 0.0),
            ("move_speed", 4.0), ("damage_taken", 1.0),
        });
        foreach (var id in new[] { "u1", "u2" })
        {
            world.SetLimits(id, "hp", 0, 100);
            world.SetLimits(id, "rage", 0, 100);
            world.SetLimits(id, "damage_taken", 0, 2);
        }

        var host = new FakeHost(world);
        var step = new AbilityStepper(world, host, Calcs(), tables, Config());

        // 距离那把尺：站位归这份假数据（层里不再有坐标），距离走宿主注册的那条谓词。
        step.DistanceOf = (_, _) => host.Gap;

        return (world, step, tables, host);
    }

    /// <summary>
    /// 接层那份配置。公共层那五列词表一个缺省都没有，所以词表归这份假数据宿主自己给
    /// （给的就是本文件一直在用的那五个名字，所以行为与旧缺省逐字相同）。
    /// </summary>
    private static AbilityLayerConfig Config() => new()
    {
        HealthAttribute = "hp",
        DamageTakenAttribute = "damage_taken",
        CooldownAttributePrefix = "cooldown.",
        RootedTag = "status.rooted",
        DamageBlockTag = "status.block_ready",
        DamageTakenEvent = "damage_taken",
        EffectAppliedEvent = "effect_applied",
        EventCapacity = 2000,
    };

    private static void Advance(AbilityStepper step, int steps)
    {
        for (var i = 0; i < steps; i++)
        {
            step.AdvanceStep();
        }
    }

    private static double Hp(AbilityWorld world, string unit) => world.Units[unit].Current["hp"];

    /// <summary>曲线那格：同一发箭，目标停在 5 米与跑到 12 米，落地伤害差一档（按当步距离现算）。</summary>
    [Fact]
    public void 曲线_按落地那一格的距离折()
    {
        var near = NewArena();
        near.Step.TryActivate("u1", "ab.arrow", "u2");
        Advance(near.Step, 7);
        Hp(near.World, "u2").ShouldBe(92, "5 米 → 折点插值 0.8333 → 10×0.8333 四舍五入 8 血");

        var far = NewArena();
        far.Step.TryActivate("u1", "ab.arrow", "u2");
        far.Host.WalkTarget(7);          // 目标往外走了 7 米：宿主改自己那份站位
        Advance(far.Step, 7);
        Hp(far.World, "u2").ShouldBe(95, "飞行途中目标走到 12 米 → 落地按当步距离折到 0.5 → 5 血");
    }

    /// <summary>免疫那格（Requirement/Blocked 之外的第三种形状）：授予父 tag 挡住一切子 tag 效果。</summary>
    [Fact]
    public void 免疫_授予父tag挡住子tag效果_单向不反噬()
    {
        var (world, step, _, _) = NewArena();
        step.TryActivate("u1", "ab.charge", "u2");
        Advance(step, 3);
        world.Units["u1"].TagCounts.GetValueOrDefault("cc").ShouldBe(1, "冲锋中授予 cc");

        step.InjectEffect("u1", "fx.cursed");
        world.Units["u1"].Effects.Any(e => e.EffectId == "fx.cursed").ShouldBeFalse("cc 挡住 cc.rooted 这类效果");
        step.World.Events.Count<EffectRefusedByTags>().ShouldBeGreaterThan(0, "被免疫要读得出来（形状 EffectRefusedByTags，旧文本 effect_immune）");

        step.InjectEffect("u1", "fx.root");
        world.Units["u1"].Effects.Any(e => e.EffectId == "fx.root").ShouldBeTrue("没声明免疫列的效果照旧落得上——免疫是逐条效果的属性，不是一刀切");

        // 层级方向只有父→子这一边算命中：身上挂着 cc.rooted 也对「免疫 cc」的效果免疫。
        step.InjectEffect("u2", "fx.root");
        step.InjectEffect("u2", "fx.cursed");
        world.Units["u2"].Effects.Any(e => e.EffectId == "fx.cursed").ShouldBeFalse("子 tag 也算命中父免疫");
    }

    /// <summary>
    /// tag 层级只有一个方向：身上授予子 tag 算命中父查询（挂着 <c>cc.rooted</c> 就是「被 cc 免疫」），
    /// 反过来「授予父、要求子」不成立——只挂着 <c>cc</c> 不等于被定身。这条是 N-17 那次改过来的。
    /// </summary>
    [Fact]
    public void tag_前缀匹配只有一个方向()
    {
        var (world, step, tables, _) = NewArena();
        tables.Register(EffectDef.OverTime("fx.needs_rooted_child", NoMods, durationTicks: 10,
            grantedTags: new[] { "status.zzz" }, requirementTags: new[] { "cc.rooted" }));
        step = Rebuild(world, tables);

        step.TryActivate("u1", "ab.charge", "u2");
        Advance(step, 2);
        world.Units["u1"].TagCounts.GetValueOrDefault("cc").ShouldBe(1, "授予父 tag");
        step.InjectEffect("u1", "fx.needs_rooted_child");
        world.Units["u1"].Effects.Any(e => e.EffectId == "fx.needs_rooted_child").ShouldBeFalse("挂着 cc 不算挂着 cc.rooted");

        step.InjectEffect("u1", "fx.root");
        step.InjectEffect("u1", "fx.needs_rooted_child");
        world.Units["u1"].Effects.Any(e => e.EffectId == "fx.needs_rooted_child").ShouldBeTrue("真挂了 cc.rooted 才满足要求");

        step.InjectEffect("u2", "fx.root");
        world.Units["u2"].TagCounts.GetValueOrDefault("cc.rooted").ShouldBe(1);
        step.InjectEffect("u2", "fx.needs_rooted_child");
        world.Units["u2"].Effects.Any(e => e.EffectId == "fx.needs_rooted_child").ShouldBeTrue("子 tag 满足子要求；而上面「只有父 tag」那一发被拒——方向只有一个");
    }

    /// <summary>换一张表重接一层。<b>距离那把尺要一并递</b>（层里不再有坐标，缺了就当场抛）。</summary>
    private static AbilityStepper Rebuild(AbilityWorld world, AbilityTables tables, double gapMeters = 5.0)
    {
        var step = new AbilityStepper(world, new FakeHost(world), Calcs(), tables, Config());
        step.DistanceOf = (_, _) => gapMeters;
        return step;
    }

    /// <summary>蓄力与松手：在放的能力剩余步数由外部注入改写，仍然只有步这一把尺。</summary>
    [Fact]
    public void 蓄力松手_外部改写剩余步数()
    {
        var (world, step, _, _) = NewArena();
        step.TryActivate("u1", "ab.charge", "u2");
        Advance(step, 1);
        world.Units["u1"].Abilities["ab.charge"].StepsRemaining.ShouldBe(5, "6 步蓄力走掉一步");

        step.InjectSetStepsRemaining("u1", "ab.charge", 1).ShouldBeTrue("松手");
        Advance(step, 1);
        world.Units["u1"].Abilities["ab.charge"].CommitTick.ShouldBe(world.Tick, "提前一步过 commit 点");
    }

    /// <summary>Cost 与来源属性快照：怒气在 commit 那一刻扣，目标血按落地那一步读。</summary>
    [Fact]
    public void Cost按commit扣_目标值按落地读()
    {
        var (world, step, _, _) = NewArena();
        step.InjectSetBase("u2", "hp", 40);
        step.TryActivate("u1", "ab.execute", "u2");
        Advance(step, 1);
        world.Units["u1"].Current["rage"].ShouldBe(70, "30 点怒气在 commit 扣");
        Hp(world, "u2").ShouldBe(0, "(100-40) + 30 = 90 伤害 → 打死");
    }

    /// <summary>充能那格：两发打完第三发被拒，且拒绝有名字。</summary>
    [Fact]
    public void 充能_打完两发第三发被拒()
    {
        var (world, step, _, _) = NewArena();
        step.TryActivate("u1", "ab.two_shots", "u2").ShouldBeTrue();
        Advance(step, 1);
        step.TryActivate("u1", "ab.two_shots", "u2").ShouldBeTrue();
        Advance(step, 1);
        step.TryActivate("u1", "ab.two_shots", "u2").ShouldBeFalse();
        world.Units["u1"].Abilities["ab.two_shots"].RejectedReason.ShouldBe(AbilityRejectReason.NoCharge);
    }

    /// <summary>
    /// Ability Task 的「等事件」那格：拉取模型每步重问，等待不需要实现——
    /// 条件出现的下一步，S4 自己就把技能放出去了。
    /// </summary>
    [Fact]
    public void 等条件不登记_每步重估自动放行()
    {
        var (world, step, _, _) = NewArena();
        step.IntentPolicy = unit => unit == "u1" ? ("ab.need_tag", "u2") : (null, null);
        Advance(step, 2);
        Hp(world, "u2").ShouldBe(100, "没标记 → 两步都被拒");
        world.Units["u1"].Abilities["ab.need_tag"].RejectedReason.ShouldBe(AbilityRejectReason.MissingTag);

        step.InjectEffect("u1", "fx.marked");
        Advance(step, 2);
        Hp(world, "u2").ShouldBe(90, "标记挂上后的下一步就放出去了");
    }
}
