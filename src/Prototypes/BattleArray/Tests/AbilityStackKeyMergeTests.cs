#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DemoFactory.GugaFramework.Abilities;
using Shouldly;
using Xunit;

namespace BattleArray.Tests;

/// <summary>
/// A79~A82 —— 技能层公共层那三列的口径测试。
///
/// 立这几条的因由（架构走读 2026-09-29 那一轮，候选 C4）：
/// <list type="bullet">
/// <item><description><c>AbilityData.cs:28</c> 那句「同一 <c>StackKey</c> 的效果再次施加时怎么办（N-6）」
/// 在实现里没兑现——找「身上已有那条」的两个分支都按 <c>EffectId</c> 比。设计文档 :184/:203/:301
/// 三处写的都是「同 StackKey」，不是「同 EffectId」。</description></item>
/// <item><description><c>MagnitudeFor</c> 里那个看 <c>ModifierSpec.RequirementTags</c> 的 <c>if</c> 两臂同 return，
/// 而「条件不满足则跳过、不撤销」在 :304 的 S2 段真生效（N-5）。文档里查不到「条件不满足时数值另算」这一档。</description></item>
/// <item><description><c>ConsumeTriggers</c> 外层那个 <c>!ReferenceEquals(...)</c> 比的是字符串引用，几乎恒真；
/// N-2 的原文只要求「事件回调只登记、不写真值」，没有同源排除这一说。真正起作用的是内层那行。</description></item>
/// </list>
///
/// 既有断言为什么没抓住：H1 的 <c>N6_叠层四格</c> 与单挑场的 <c>warrior.bleed</c> 都只反复施加<b>同一条</b>
/// 效果，「两条不同定义共用一个 StackKey」这一格全仓没有用例——按 Id 归与按键归并在现有内容上看不出差别，
/// 缺口一直在。所以 A79 是本轮唯一一条先红后绿的断言，其余三条是把已经成立的口径钉住。
/// </summary>
public class AbilityStackKeyMergeTests
{
    private const string SharedKey = "shared";

    private static readonly ModifierSpec[] NoModifiers = Array.Empty<ModifierSpec>();

    /// <summary>公共层要的外界口（单位查询 / 存活 / 名单 / 随机源）：这里只当假数据镜像，读法与 H1 的宿主一致。</summary>
    private sealed class ProbeHost : IAbilityHost
    {
        private readonly AbilityWorld _world;

        public ProbeHost(AbilityWorld world) => _world = world;

        public AbilitySet? Find(string unitId) => _world.Units.TryGetValue(unitId, out var u) ? u : null;

        public bool IsAlive(string unitId) => Find(unitId) is { } u && u.Current["hp"] > 0;

        /// <summary>这一侧的目标册只装兵：在册就是在册（死活仍归 <see cref="IsAlive"/>）。</summary>
        public bool Exists(string entityId) => Find(entityId) is not null;

        public IReadOnlyList<string> UnitIds => _world.UnitOrder;

        public double NextRandom() => 0.5;
    }

    private static CalcRegistry Calcs() => new CalcRegistry();

    /// <summary>
    /// 一对共用 <c>StackKey="shared"</c> 的定义：<c>fx.carrier</c> 是回充载体（无 modifier，S1 那一跳只加层数），
    /// <c>fx.pain</c> 带扣血 modifier。另有 <c>fx.solo</c>（独占一键）、<c>fx.loop</c>（自己触发自己）、
    /// <c>fx.mount</c> 与 <c>fx.slow_if_mounted</c>（条件列那格）。
    /// </summary>
    private static (AbilityWorld World, AbilityTables Tables, AbilityStepper Step) NewPair()
    {
        var world = new AbilityWorld();
        var tables = new AbilityTables();

        tables.Register(EffectDef.OverTime("fx.carrier", NoModifiers, durationTicks: 0,
            periodTicks: 5, stackKey: SharedKey, maxStacks: 3, kind: EffectKind.Infinite));
        tables.Register(EffectDef.OverTime("fx.pain", new[]
        {
            new ModifierSpec("hp", ModifierMode.Add, -2),
        }, durationTicks: 60, periodTicks: 5, stackKey: SharedKey, maxStacks: 3));
        tables.Register(EffectDef.OverTime("fx.solo", NoModifiers, durationTicks: 30,
            stackKey: "solo", maxStacks: 3));
        tables.Register(EffectDef.OverTime("fx.loop", NoModifiers, durationTicks: 0,
            kind: EffectKind.Infinite,
            triggeredEffects: new[] { ("effect_applied", "fx.loop") }));
        tables.Register(EffectDef.OverTime("fx.mount", NoModifiers, durationTicks: 0,
            grantedTags: new[] { "status.mounted" }, kind: EffectKind.Infinite));
        tables.Register(EffectDef.OverTime("fx.slow_if_mounted", new[]
        {
            new ModifierSpec("move_speed", ModifierMode.Mult, 0.5,
                RequirementTags: new[] { "status.mounted" }),
        }, durationTicks: 50));

        world.AddUnit("u1", new[] { ("hp", 100.0), ("move_speed", 4.0) });
        world.AddUnit("u2", new[] { ("hp", 100.0), ("move_speed", 4.0) });
        world.SetLimits("u1", "hp", 0, 100);
        world.SetLimits("u2", "hp", 0, 100);

        var step = new AbilityStepper(world, new ProbeHost(world), Calcs(), tables, Config());

        // 距离那把尺：站位归这份假数据（层里不再有坐标），两个兵固定隔 2 米。
        step.DistanceOf = (_, _) => 2.0;

        return (world, tables, step);
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

    /// <summary>身上那两条同键的定义（归并之后应当只剩一条）。</summary>
    private static List<EffectOnBody> SharedKeyOnes(AbilityWorld world)
        => world.Units["u2"].Effects
            .Where(e => e.EffectId == "fx.carrier" || e.EffectId == "fx.pain")
            .ToList();

    // ---- A79 N-6 的归并键 ------------------------------------------------------

    /// <summary>
    /// A79：两条<b>不同定义</b>共用一个 StackKey，第二次施加时应归并成身上那一条并叠层
    /// （N-6 原文口径是「同 StackKey」，不是「同 EffectId」）。
    /// </summary>
    [Fact]
    public void A79_两条定义共用一个StackKey_第二次施加归并成一条并叠层()
    {
        var (world, _, step) = NewPair();

        step.InjectEffect("u2", "fx.carrier");
        step.InjectEffect("u2", "fx.pain");
        Advance(step, 1);

        var merged = SharedKeyOnes(world);
        merged.Count.ShouldBe(1, "共用一个 StackKey 的两条应当归并成一条；按 EffectId 找会留下两条实例、各带 1 层");
        merged.Single().Stacks.ShouldBe(2, "第二次施加是加层，不是再挂一条");
    }

    /// <summary>各占一个键时不许被牵连：补归并键不能把这条也吃掉。</summary>
    [Fact]
    public void A79_各带唯一StackKey时行为一字不变_保住现有基线()
    {
        var (world, _, step) = NewPair();

        step.InjectEffect("u2", "fx.carrier");
        step.InjectEffect("u2", "fx.solo");
        Advance(step, 1);

        world.Units["u2"].Effects.Count.ShouldBe(2, "两条各占一个键，不该合并");
        world.Units["u2"].Effects.Select(e => e.Stacks).ShouldAllBe(_e => _e == 1);
    }

    // ---- A80 跨条归并时快照那一格跟谁 ------------------------------------------

    /// <summary>
    /// A80：共用键时「谁先挂上谁当家」——身上那条实例记第一次那个 Id，
    /// 所以快照的 <c>charges=</c> 只跟着<b>当家那条</b>读（N-15 与 A60 的那条边界）。
    /// </summary>
    [Fact]
    public void A80_跨条归并后那行charges只念一次_且跟着当家的定义()
    {
        var (world, tables, step) = NewPair();
        step.InjectEffect("u2", "fx.carrier");
        step.InjectEffect("u2", "fx.pain");
        Advance(step, 1);

        var carrierLines = EffectLines(AbilitySnapshot.Save(world, tables)).ToList();
        carrierLines.Count.ShouldBe(1, "归并成一条，快照就该只有一行 f;");
        carrierLines.Single().ShouldStartWith("f;fx.carrier;2", Case.Sensitive, "当家的是第一次挂上的 fx.carrier，层数记在它身上");
        carrierLines.Single().ShouldContain(";charges=2", Case.Sensitive, "载体那格跟着当家定义念一次，不因第二条同键定义而多念");

        var (world2, tables2, step2) = NewPair();
        step2.InjectEffect("u2", "fx.pain");
        step2.InjectEffect("u2", "fx.carrier");
        Advance(step2, 1);

        var painLines = EffectLines(AbilitySnapshot.Save(world2, tables2)).ToList();
        painLines.Count.ShouldBe(1);
        painLines.Single().ShouldStartWith("f;fx.pain;2");
        painLines.Single().ShouldNotContain("charges=", Case.Sensitive, "当家的不是载体就不念 charges=——归并不改「谁在念这一格」");
    }

    // ---- A81 触发链上的同源那一跳 ----------------------------------------------

    /// <summary>
    /// A81：自己触发自己不成环。实现只有一处——<c>ConsumeTriggers</c> 里「被触发的正好是造成事件那条 → 跳过」。
    /// 外层那个比字符串引用的条件删掉之后这一条仍成立：它本来就恒真，没在管事。
    /// </summary>
    [Fact]
    public void A81_效果自己触发自己不成环_层数与登记都不涨()
    {
        var (world, _, step) = NewPair();

        step.InjectEffect("u2", "fx.loop");
        world.Units["u2"].Pendings.Count.ShouldBe(0, "自触发那一跳应当被跳过；登记成待决结算就成环了");

        Advance(step, 2);

        var unit = world.Units["u2"];
        unit.Effects.Count.ShouldBe(1, "自环不该把同一条效果再挂一遍");
        unit.Effects.Single().Stacks.ShouldBe(1, "层数不因自触发而涨");
        step.World.Events.Count<EffectApplied>().ShouldBe(1, "一笔施加只发一条落地事件——发第二笔就是环在走");
    }

    // ---- A82 条件列不参与数值段 --------------------------------------------------

    /// <summary>
    /// A82：modifier 的条件列只在 S2 决定「本步生效不生效」，<b>不参与数值</b>
    /// （N-5「不满足则跳过、不撤销」）。这条是钉给 <c>MagnitudeFor</c> 的：那里曾留着一个看条件列却
    /// 两臂同 return 的空 if——谁想把「不满足时按几折」写进去，这条就该红。
    /// </summary>
    [Fact]
    public void A82_条件列只管跳过不管数值_不满足时读原值且记录还在()
    {
        var (world, _, step) = NewPair();

        step.InjectEffect("u2", "fx.slow_if_mounted");
        Advance(step, 1);

        var stored = world.Units["u2"].Modifiers.Single(m => m.SourceEffectId == "fx.slow_if_mounted");
        stored.Magnitude.ShouldBe(0.5, "数值按表上写的那份存，不因条件满不满足而缩放");
        world.Units["u2"].Current["move_speed"].ShouldBe(4.0, "没挂载 → 本步跳过（跳过不等于撤销：记录还在身上）");

        step.InjectEffect("u2", "fx.mount");
        Advance(step, 1);

        world.Units["u2"].Current["move_speed"].ShouldBe(2.0, "挂上 tag 的下一步就生效，无需重挂");
        world.Units["u2"].Modifiers.Count.ShouldBe(1, "生效那一步不该再攒出第二份同来源修饰符");
    }

    // ---- 读快照的小口 -----------------------------------------------------------

    private static IEnumerable<string> EffectLines(string snapshot)
        => snapshot.Split('\n').Where(l => l.StartsWith("f;", StringComparison.Ordinal));
}
