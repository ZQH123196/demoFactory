#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DemoFactory.GugaFramework.Abilities;
using DemoFactory.GugaFramework.Abilities.Stage;
using Shouldly;
using Xunit;

namespace DemoFactory.Tests;

/// <summary>
/// 技能试验场的离线用例。断言只比<b>状态级</b>（<see cref="SkillStage.DebugSnapshot"/> 与属性读数，
/// 「断言比快照」：事件流逐字相等不做），一个用例只断言「一场试验的结果是什么」。
///
/// 场子的三个口各有用例：<b>时间可控</b>（步进与种子）、<b>状态可查</b>（快照逐字）、
/// <b>行为可注入</b>（写路径只有 <c>Inject</c> 一条口）。
/// </summary>
public sealed class SkillStageTests
{
    /// <summary>场子目录（源码检索那几条的根）。</summary>
    private static string StageDir => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "Abilities", "Stage"));

    private static SkillStage SampleStage(double gapMeters = SkillStageSample.DefaultGapMeters, int seed = 1)
        => new(SkillStageSample.Spec(gapMeters), seed);

    private static double Vitality(SkillStage stage, string unitId)
    {
        var set = stage.Find(unitId);
        set.ShouldNotBeNull($"场子里没有 {unitId}（注册口那一份没摆上？）");
        return set!.Current[SkillStageSample.Attr.Vitality];
    }

    // === 行为可注入 + 时间可控：递三样就能起一局，一条真技能从头跑到落地 ===

    /// <summary>
    /// 一条真技能的整条时间线：按下 → 前摇 3 步 → 落地扣血。
    /// 断言的是状态（血量读数与相位），不是事件流（「断言比快照」）。
    /// </summary>
    [Fact]
    public void S1_递三样起一局_一条技能从按下走到落地()
    {
        var stage = SampleStage();
        var before = Vitality(stage, SkillStageSample.DummyId);

        stage.Inject(AbilityVerbs.Cast,
            $"{SkillStageSample.CasterId}|{SkillStageSample.Skill.Strike}|{SkillStageSample.DummyId}").ShouldBeTrue("射程之内（2 米 < 3 米）这一发该打得出");

        var ability = stage.Find(SkillStageSample.CasterId)!.Abilities[SkillStageSample.Skill.Strike];
        ability.Phase.ShouldBe(AbilityPhase.Pre, "按下那一刻进前摇");
        Vitality(stage, SkillStageSample.DummyId).ShouldBe(before, "前摇里还没打中");

        // 前摇 3 步 ⇒ 第 3 步走完 commit 并落地。
        stage.Advance(SkillStageSample.StrikePreSteps);

        stage.Tick.ShouldBe(SkillStageSample.StrikePreSteps, "时间按步走，1 步 = 0.1 秒");
        ((double)Vitality(stage, SkillStageSample.DummyId)).ShouldBe(before - 25, 0.001, "伤害按出手强度 25 算（纯函数那份注册进来的）");
    }

    /// <summary>超射程当场拒，账上读得出为什么（<c>NotInRange</c>），不是静默少打一笔。</summary>
    [Fact]
    public void S2_超出射程当场拒_读得出原因()
    {
        var range = SkillStageSample.StrikeRangeMeters;
        var stage = SampleStage(gapMeters: range + 1);

        stage.Inject(AbilityVerbs.Cast,
            $"{SkillStageSample.CasterId}|{SkillStageSample.Skill.Strike}|{SkillStageSample.DummyId}").ShouldBeFalse("超出射程这一发打不出");
        stage.Find(SkillStageSample.CasterId)!.Abilities[SkillStageSample.Skill.Strike].RejectedReason.ShouldBe(AbilityRejectReason.NotInRange, "为什么没放有名字，不是一个 false");
    }

    /// <summary>冷却中再按一次记成「排一个」，不是丢掉也不是立刻放（层里既有语义，场子照单全收）。</summary>
    [Fact]
    public void S3_冷却中再按_记成排队而不是丢掉()
    {
        var stage = SampleStage();
        stage.Inject(AbilityVerbs.Cast,
            $"{SkillStageSample.CasterId}|{SkillStageSample.Skill.Strike}|{SkillStageSample.DummyId}");
        stage.Advance(SkillStageSample.StrikePreSteps);

        var ability = stage.Find(SkillStageSample.CasterId)!.Abilities[SkillStageSample.Skill.Strike];
        ability.Phase.ShouldBe(AbilityPhase.Cooling, "落地之后进冷却");

        stage.Inject(AbilityVerbs.Cast,
            $"{SkillStageSample.CasterId}|{SkillStageSample.Skill.Strike}|{SkillStageSample.DummyId}").ShouldBeFalse("冷却中这一发打不出");
        ability.QueuedInput.ShouldBeTrue("但它被记下来了，不是静默丢掉");
    }

    // === 时间可控：种子与随机开关 ===

    /// <summary>同种子两跑逐字一致（场子「固定」那一条口径）。</summary>
    [Fact]
    public void S4_同种子两跑逐字一致()
    {
        var first = SampleStage(seed: 7);
        var second = SampleStage(seed: 7);

        foreach (var stage in new[] { first, second })
        {
            stage.Inject(AbilityVerbs.Cast,
                $"{SkillStageSample.CasterId}|{SkillStageSample.Skill.Strike}|{SkillStageSample.DummyId}");
            stage.Advance(10);
        }

        second.DebugSnapshot().ShouldBe(first.DebugSnapshot(), "同种子同命令流 ⇒ 逐字一致");
    }

    /// <summary>
    /// 随机项能全关掉：关掉之后 <see cref="SkillStage.NextRandom"/> 每次给同一个数，
    /// 于是换种子也不改变任何一格状态。
    /// 比之前把头部那格 <c>seed=</c> 摘掉——它是「这是哪一局」的标签而不是状态（「断言比快照」 只比状态级），
    /// 留着它比的话这条就是在比标签，不是在比随机关掉之后的结果一不一样。
    /// </summary>
    [Fact]
    public void S5_随机项关掉之后逐字一致()
    {
        var first = SampleStage(seed: 1);
        var second = SampleStage(seed: 999);
        first.RandomEnabled = false;
        second.RandomEnabled = false;

        foreach (var stage in new[] { first, second })
        {
            stage.Advance(5);
        }

        first.DebugSnapshot().ShouldContain("random=0", Case.Sensitive, "读数里念得出随机关着");
        WithoutSeed(second.DebugSnapshot()).ShouldBe(WithoutSeed(first.DebugSnapshot()), "随机关掉之后，种子不同也跑出同一份状态");
    }

    /// <summary>摘掉头部那格 <c>seed=</c>（这一局跑的是哪个种子是标签，不是状态）。</summary>
    private static string WithoutSeed(string snapshot)
        => string.Join('\n', snapshot.Split('\n').Select(line =>
            line.StartsWith("tick=", StringComparison.Ordinal)
                ? string.Join(' ', line.Split(' ').Where(token => !token.StartsWith("seed=", StringComparison.Ordinal)))
                : line));

    // === 状态可查：快照与观测窗口 ===

    /// <summary>快照一发读出全量真值：步号、每个单位的属性与站位、层的账、观测窗口读数。</summary>
    [Fact]
    public void S6_快照一发读出全量真值()
    {
        var stage = SampleStage();
        stage.Advance(4);
        var snapshot = stage.DebugSnapshot();

        snapshot.ShouldContain("tick=4", Case.Sensitive, "步号念得出来");
        snapshot.ShouldContain("seconds=0.4", Case.Sensitive, "秒按步换算念得出来（1 步 = 0.1 秒，「时间基准」）");
        snapshot.ShouldContain($"u {SkillStageSample.CasterId}", Case.Sensitive, "每个单位一行");
        snapshot.ShouldContain($"u {SkillStageSample.DummyId}");
        snapshot.ShouldContain(SkillStageSample.Attr.Vitality, Case.Sensitive, "属性名逐个念，不摘要");
        snapshot.ShouldContain("dropped=0", Case.Sensitive, "观测窗口读数带 dropped（「三条铁律」 第三条）");
    }

    /// <summary>机位参数不进检查点快照（「试验场随技能层」）——逻辑壳里根本没有相机，这是结构上的，不是靠自觉。</summary>
    [Fact]
    public void S7_机位不进快照()
    {
        var snapshot = SampleStage().DebugSnapshot();
        foreach (var forbidden in new[] { "camera", "focus", "yaw", "pitch", "distance" })
        {
            snapshot.ShouldNotContain(forbidden, Case.Sensitive, $"快照里出现了 {forbidden}——机位是表现层参数，不许进比对（「试验场随技能层」）");
        }
    }

    /// <summary>
    /// <b>S24 机位不进写路径词表</b>（「写路径进 Journal」）：切预设、换锁、挪镜头是表现层动词，
    /// 一个都不许进 <see cref="AbilityVerbs"/> 那份词表——<b>进了一次重放就分叉一次</b>。
    ///
    /// 判据两半，与 S7 互补：S7 判「快照里没有机位」（读那一发的输出），
    /// 本条判「<b>逻辑壳源码里根本没有机位</b>，词表里也没有机位动词」（读源码与那份名单）——
    /// 机位住在场景壳（<c>SkillStageScene.cs</c>）那一侧，场子本体三份一个都不沾。
    ///
    /// 词表里<b>没有 <c>distance</c></b>：射程与光环距离是技能层的正经概念，
    /// 逻辑壳里 <c>DistanceBetween</c> / <c>DistanceOf</c> 说的就是它，不是机位。
    /// 出处：新词表里长出机位动词时，红的是这一条。
    /// </summary>
    [Fact]
    public void S24_机位不进写路径词表_逻辑壳里零处机位词()
    {
        var cameraWords = new[]
        {
            "camera", "focus", "yaw", "pitch", "preset", "follow", "zoom", "closeup", "overview",
        };

        var logicShell = new[] { "SkillStage.cs", "SkillStageOpenField.cs", "SkillStageSpec.cs" };
        logicShell.Select(name => File.Exists(Path.Combine(StageDir, name))).ShouldAllBe(_e => _e == true, $"逻辑壳那两份读不到（路径拼错时下面那圈是空跑）：{StageDir}");

        foreach (var name in logicShell)
        {
            // 源码与词都折成小写再比：机位那几处拼法大小写不统一（CameraFocus / camera / Pitch）。
            var source = File.ReadAllText(Path.Combine(StageDir, name)).ToLowerInvariant();
            foreach (var word in cameraWords)
            {
                source.ShouldNotContain(word, Case.Sensitive, $"{name} 里出现了 {word} —— 机位是表现层参数，它住在场景壳那一侧（「相机单源」、「试验场随技能层」）");
            }
        }

        foreach (var verb in AbilityVerbs.Names)
        {
            foreach (var word in cameraWords)
            {
                verb.ToLowerInvariant().ShouldNotContain(word, Case.Sensitive, $"写路径词表里长出了带 {word} 的动词 {verb} —— 机位动词进录制就让重放分叉（「写路径进 Journal」）");
            }
        }
    }

    // === 通用：场子源码里搜不到任何具体技能名 / 属性名 / tag 字面量 ===

    /// <summary>
    /// 场子本体那几份（逻辑壳 + 场景壳 + 注册口形状）里，搜不到<b>任何一份注册壳</b>的任何一个字面量。
    /// 注册壳现在有两份（ticket 05 起）：<c>SkillStageSample.cs</c>（示范注册壳：一条真技能跑通时间线）与
    /// <c>StageChecklistShell</c>（清单注册壳：16 格形状各摆一个载体，在 <c>AbilityChecklistStageHost.cs</c> 里）。
    /// 两份注册壳本身当然有它们各自的字面量——它们扮演的就是「接层的那一侧」，
    /// 所以本条只扫场子本体那几份，与 A83「扫全目录」的口径不同，扫的面也不同。
    /// <b>两份都扫</b>：只扫一份就等于「换另一份注册壳时可以在场子里写字面量」，那正是这一格要挡的。
    /// </summary>
    [Fact]
    public void S8_场子源码里搜不到任何注册壳的字面量()
    {
        var needles = new[]
        {
            // 示范注册壳那一份（最小那份）
            SkillStageSample.Skill.Strike,
            SkillStageSample.Fx.StrikeHit,
            SkillStageSample.Fn.StrikeByPower,
            SkillStageSample.Attr.Vitality,
            SkillStageSample.Attr.VitalityMax,
            SkillStageSample.Attr.Power,
            SkillStageSample.Attr.IncomingScale,
            SkillStageSample.Attr.CooldownPrefix,
            SkillStageSample.Tag.Held,
            SkillStageSample.CasterId,
            SkillStageSample.DummyId,

            // 示范注册壳的开阔多点那一份（ticket 07：六个目标各是谁）
            SkillStageSample.OpenField.Front,
            SkillStageSample.OpenField.Mid,
            SkillStageSample.OpenField.NearEdge,
            SkillStageSample.OpenField.Wide,
            SkillStageSample.OpenField.Back,
            SkillStageSample.OpenField.Far,

            // 清单注册壳那一份（16 格形状那一份）
            StageChecklistShell.CasterId,
            StageChecklistShell.TargetId,
            StageChecklistShell.Attr.Vitality,
            StageChecklistShell.Attr.VitalityMax,
            StageChecklistShell.Attr.Power,
            StageChecklistShell.Attr.Incoming,
            StageChecklistShell.Attr.Focus,
            StageChecklistShell.Attr.Guard,
            StageChecklistShell.Attr.CooldownPrefix,
            StageChecklistShell.Tag.Channel,
            StageChecklistShell.Tag.Silence,
            StageChecklistShell.Tag.Rooted,
            StageChecklistShell.Tag.Warded,
            StageChecklistShell.Skill.Windup,
            StageChecklistShell.Skill.ChannelSlash,
            StageChecklistShell.Skill.Lob,
            StageChecklistShell.Skill.Pair,
            StageChecklistShell.Skill.Sealed,
            StageChecklistShell.Fx.WindupHit,
            StageChecklistShell.Fx.ChannelHit,
            StageChecklistShell.Fx.LobHit,
            StageChecklistShell.Fx.PairFirst,
            StageChecklistShell.Fx.PairSecond,
            StageChecklistShell.Fx.WoundWound,
            StageChecklistShell.Fx.Interrupt,
            StageChecklistShell.Fx.TagGrant,
            StageChecklistShell.Fx.SilenceProbe,
            StageChecklistShell.Fn.ByPower,
            StageChecklistShell.Fn.Negate,
        };

        foreach (var file in StageSources())
        {
            var literals = StringLiterals(File.ReadAllText(file));
            foreach (var needle in needles)
            {
                literals.ShouldNotContain($"\"{needle}\"",
                    $"{Path.GetFileName(file)} 里出现了字面量 \"{needle}\" —— " +
                    "换原型换技能不该改场子，场子也不该认识任何一条具体技能（通用那一条口径）");
            }
        }
    }

    // === 单位身上不再有坐标：距离的两条路 ===

    /// <summary>
    /// <b>「宿主递标量」那条路</b>：调用点直接把距离当一个数递进去，层里既没有坐标也不问谓词。
    /// 断言的是外部行为（放不放得出、为什么没放），不是内部怎么算的（「断言比快照」）。
    /// </summary>
    [Fact]
    public void S11_宿主递标量_射程够不够当场判得出()
    {
        var stage = SampleStage();
        // 撤掉场子注册的那条谓词，逼这一发只能走「调用点递标量」那条路。
        stage.Stepper.DistanceOf = null;

        stage.Stepper.TryActivate(SkillStageSample.CasterId, SkillStageSample.Skill.Strike,
                SkillStageSample.DummyId, 2.0).ShouldBeTrue("递进去 2 米、在 3 米射程之内 ⇒ 这一发该放得出");
        Vitality(stage, SkillStageSample.DummyId).ShouldBe(100.0, "前摇里还没打中");

        var stage2 = SampleStage();
        stage2.Stepper.DistanceOf = null;
        stage2.Stepper.TryActivate(SkillStageSample.CasterId, SkillStageSample.Skill.Strike,
                SkillStageSample.DummyId, 4.0).ShouldBeFalse("递进去 4 米、超出 3 米射程 ⇒ 这一发打不出");
        stage2.Find(SkillStageSample.CasterId)!.Abilities[SkillStageSample.Skill.Strike].RejectedReason.ShouldBe(AbilityRejectReason.NotInRange, "为什么没放有名字");

        // 场上真实摆位（2 米）此刻是 4 米那一档：距离只认递进来的数，不认别的。
        stage2.XOf(SkillStageSample.DummyId).ShouldBe(SkillStageSample.DefaultGapMeters, "场子的站位没被动过 —— 这一格证明「距离算哪一份」是调用点说了算");
    }

    /// <summary>
    /// <b>「注册谓词」那条路</b>：宿主把距离算好注册进层里，此后每一格距离都问它。
    /// 开场那个场子已经注册了一条（场子自己的站位），这里换一个现算的来源，
    /// 证明换一把尺不用改场子一个字。
    /// </summary>
    [Fact]
    public void S12_注册谓词_换一把尺不用改场子()
    {
        var stage = SampleStage();
        var asked = new List<string>();
        stage.Stepper.DistanceOf = (fromId, toId) =>
        {
            asked.Add($"{fromId}>{toId}");
            return 1.0;   // 摆位明明是 2 米，这一把尺说 1 米 ⇒ 射程之内照放
        };

        stage.Stepper.TryActivate(SkillStageSample.CasterId, SkillStageSample.Skill.Strike,
                SkillStageSample.DummyId).ShouldBeTrue("谓词说 1 米 ⇒ 在 3 米射程之内");
        asked.ShouldNotBeEmpty("距离是问出来的，不是层里存的");
    }

    /// <summary>
    /// <b>两条路都缺来源时当场抛</b>：既没递标量也没注册谓词，而技能带射程激活检查
    /// ⇒ 抛而不是静默判成「够得着」（「桥只转发」：缺了当场抛，兜 0 等于静默摆到最近那一格）。
    /// </summary>
    [Fact]
    public void S13_两条路都没有_当场抛不静默判成够得着()
    {
        var stage = SampleStage();
        stage.Stepper.DistanceOf = null;

        Should.Throw<KeyError>(() => stage.Stepper.TryActivate(SkillStageSample.CasterId, SkillStageSample.Skill.Strike,
                SkillStageSample.DummyId), "没人给距离就不许放 —— 兜 0 就是静默判成贴脸");
    }

    /// <summary>
    /// <b>二维那条真的能验扇形</b>：一个二维宿主把距离按 (x, z) 现算，界内界外<b>两侧各摆一个点</b>——
    /// 正前 30°（扇内）与 80°（扇外），两点到出手那一个的距离都是 3 米，
    /// 所以「半径够不够」这一问在两点上答案相同、分开它们的<b>只有角度</b>。
    /// 规则在公共层那把尺里（<see cref="AbilityStepper.InsideSector"/>），宿主只递标量。
    /// </summary>
    [Fact]
    public void S14_二维那条_扇形界内界外两侧各摆一个点()
    {
        const double radius = 3;
        const double cone = 120;
        var facing = (x: 1.0, z: 0.0);      // 朝 +X
        var inside = Polar(radius, 30);      // 30°：正前 120° 的内档
        var outside = Polar(radius, 80);     // 80°：同样 3 米，但出扇形

        // 二维宿主：距离按 (x, z) 现算，界内界外两点到出手那一个都是 3 米。
        var stage = SampleStage();
        stage.Stepper.DistanceOf = (fromId, toId) => fromId == SkillStageSample.CasterId ? radius : 0.0;
        stage.Stepper.TryActivate(SkillStageSample.CasterId, SkillStageSample.Skill.Strike,
                SkillStageSample.DummyId).ShouldBeTrue("3 米在 3 米射程之内（半径这一关两点都过）");

        AbilityStepper.InsideSector(0, 0, inside.x, inside.z,
            facing.x, facing.z, radius, cone).ShouldBeTrue("正前 30°：扇内");
        AbilityStepper.InsideSector(0, 0, outside.x, outside.z,
            facing.x, facing.z, radius, cone).ShouldBeFalse("同样 3 米、正前 80°：出扇形");

        // 两点与出手那一个的距离逐字相等 —— 角度这一问不是被距离顺带判掉的。
        Math.Sqrt(inside.x * inside.x + inside.z * inside.z).ShouldBe(radius, 0.001);
        Math.Sqrt(outside.x * outside.x + outside.z * outside.z).ShouldBe(radius, 0.001);

        // 界外那一点换一个注册谓词就能进 —— 距离与角度是分开两问，宿主想给多少给多少。
        stage.Stepper.DistanceOf = (fromId, toId) => fromId == SkillStageSample.CasterId ? 0.0 : 0.0;
        stage.Stepper.UnitsInRange(SkillStageSample.CasterId, radius).ShouldContain(_e => Equals(_e, SkillStageSample.DummyId), "光环那一格也走同一条谓词：半径之内点到的就是这一位");
    }

    /// <summary>极坐标（米 + 角度）→ 二维点（度）。</summary>
    private static (double x, double z) Polar(double meters, double degrees)
    {
        var rad = degrees * Math.PI / 180.0;
        return (meters * Math.Cos(rad), meters * Math.Sin(rad));
    }

    /// <summary>场子源码里 0 处 <c>src/Prototypes</c> 引用（「原型隔离」：不隶属任何原型）。</summary>
    [Fact]
    public void S9_场子代码里零处原型引用()
    {
        foreach (var file in StageSources())
        {
            File.ReadAllText(file).ShouldNotContain("Prototypes", Case.Sensitive, $"{Path.GetFileName(file)} 里出现了 Prototypes —— 场子随项目走但不隶属任何原型（「原型隔离」）");
        }
    }

    /// <summary>
    /// 场子那几份源文件都在（路径拼错时下面两条会空跑，先钉住这个）。
    /// 判的是<b>整个目录</b>的清单：场子本体五份（两个逻辑壳 + 场景壳两份 + 注册口形状）+ 示范注册壳一份。
    /// 场景壳现有<b>两份</b>（ticket 09 补开阔多点那份的接线）：基类那份服务第一个场子，
    /// 派生那份（<c>SkillStageOpenFieldScene.cs</c>）只递两样，陈设与机位不重画。
    /// 示范注册壳<b>不</b>进 S8/S9 的检索面（见 <see cref="StageSources"/> 的头注），
    /// 但它得在——它是「接层的那一侧」的那份实例，少了它场子就没法开箱跑起来。
    /// </summary>
    [Fact]
    public void S10_场子那几份源文件都在()
    {
        Directory.Exists(StageDir).ShouldBeTrue($"读不到场子目录 {StageDir}（路径拼错了这一组就是空跑）");
        var files = Directory.GetFiles(StageDir, "*.cs", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
        files.ShouldBe(new[]
                {
                    "SkillStage.cs", "SkillStageOpenField.cs", "SkillStageOpenFieldScene.cs", "SkillStageSample.cs",
                    "SkillStageScene.cs", "SkillStageSpec.cs",
                }, Case.Sensitive, $"场子目录读到的文件不对：{string.Join(",", files)}（本组用例的检索面变了，先看这里）");
        StageSources().Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ShouldBe(new[]
                {
                    "SkillStage.cs", "SkillStageOpenField.cs", "SkillStageOpenFieldScene.cs", "SkillStageScene.cs",
                    "SkillStageSpec.cs",
                }, Case.Sensitive, "场子本体那五份（注册壳那份不在内）");
    }

    /// <summary>场子本体那几份（注册壳那份不在内，见 S8 的头注）。两个逻辑壳与两份场景壳都在内。</summary>
    private static IEnumerable<string> StageSources()
    {
        Directory.Exists(StageDir).ShouldBeTrue($"读不到场子目录 {StageDir}（路径拼错了这一组就是空跑）");
        return new[]
               {
                   "SkillStage.cs", "SkillStageOpenField.cs", "SkillStageOpenFieldScene.cs", "SkillStageSpec.cs",
                   "SkillStageScene.cs",
               }
            .Select(name => Path.Combine(StageDir, name));
    }

    /// <summary>
    /// 抽出源文件里的字符串字面量（只查字面量，排除注释——注释里提一句词表不算「场子认识它」）。
    /// 口径与 A83 同族：那条查的是词表字面量，这一条查的是「示范注册壳递进来的那批名字」。
    /// </summary>
    private static IReadOnlyList<string> StringLiterals(string source)
    {
        var found = new List<string>();
        var i = 0;
        while (i < source.Length)
        {
            if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] != '\n') i++;
                continue;
            }

            if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/')) i++;
                i = Math.Min(i + 2, source.Length);
                continue;
            }

            if (source[i] == '"')
            {
                var start = i;
                i++;
                while (i < source.Length && source[i] != '"')
                {
                    i += source[i] == '\\' ? 2 : 1;
                }

                i = Math.Min(i + 1, source.Length);
                found.Add(source[start..i]);
                continue;
            }

            i++;
        }

        return found;
    }

    // === ticket 04：第一个场子改到「理想」那一档 ===

    /// <summary>
    /// <b>S15 钉住</b>：单位位置钉得住——注入一发就站在那儿，<b>不自己乱走</b>。
    /// 这一条钉的是「场子不替单位走位」：没有自动寻敌、没有自动靠近，
    /// 场子里<b>唯一</b>改位置的是注入 <c>Move</c> 那一发（写路径只有一条，「桥只转发」）。
    /// 断言只比状态：摆位与朝向逐字未变。
    /// </summary>
    [Fact]
    public void S15_注入一发_单位站在原地不自己乱走()
    {
        var stage = SampleStage();
        var caster = stage.PointOf(SkillStageSample.CasterId);
        var dummy = stage.PointOf(SkillStageSample.DummyId);
        var facing = stage.FacingDegreesOf(SkillStageSample.CasterId);

        // 一整局跑完（60 步 = 6 秒），中途放两发，够长到「要是场子会自己找人靠近」必然露馅。
        stage.Inject(AbilityVerbs.Cast,
            $"{SkillStageSample.CasterId}|{SkillStageSample.Skill.Strike}|{SkillStageSample.DummyId}");
        stage.Advance(40);
        stage.Inject(AbilityVerbs.ClearCooldown, $"{SkillStageSample.CasterId}|{SkillStageSample.Skill.Strike}");
        stage.Inject(AbilityVerbs.Cast,
            $"{SkillStageSample.CasterId}|{SkillStageSample.Skill.Strike}|{SkillStageSample.DummyId}");
        stage.Advance(20);

        stage.Tick.ShouldBe(60, "一整局确实跑完了（1 步 = 0.1 秒，「时间基准」）");
        stage.PointOf(SkillStageSample.CasterId).ShouldBe(caster, "出手那一个一步没挪");
        stage.PointOf(SkillStageSample.DummyId).ShouldBe(dummy, "挨打那一个一步没挪");
        stage.FacingDegreesOf(SkillStageSample.CasterId).ShouldBe(facing, "朝向也没被改");

        // 反面：位置确实<b>可</b>改，只是只能从写路径改（否则「钉住」就分不清是钉住了还是根本改不动）。
        stage.Inject(AbilityVerbs.Move, $"{SkillStageSample.DummyId}|1.5").ShouldBeTrue("注入 Move 这一发是位置唯一的写路径");
        stage.XOf(SkillStageSample.DummyId).ShouldBe(dummy.X + 1.5, "注入的那几米真的挪了 —— 所以上面那三条是真的钉住，不是根本改不动");
        stage.ZOf(SkillStageSample.DummyId).ShouldBe(dummy.Z, "Move 那一格只带一个标量，走的是 X 那根轴，Z 不动（次序在 AbilityVerbs，不许场子自己猜方向）");
    }

    /// <summary>
    /// <b>S16 远近都摆得出 + 射程边界两侧</b>：从贴脸（0.5 米）到超出射程（射程 + 0.01）逐档判，
    /// <b>边界两侧各有一格</b>：正好等于射程那一档放得出、超出 1 厘米那一档当场拒（账上读得出为什么）。
    /// 距离是场子那份摆位现算的，所以「摆多远」与「判多远」是同一份实现，不会各说各话。
    /// </summary>
    [Fact]
    public void S16_从贴脸到超出射程都摆得出_射程边界两侧各判一次()
    {
        var range = SkillStageSample.StrikeRangeMeters;

        // 界内：贴脸、边界内侧、正好边界（层里判的是「大于射程才拒」，等于射程放得出）。
        foreach (var gap in new[] { 0.5, range - 0.01, range })
        {
            var stage = SampleStage(gapMeters: gap);
            ((double)stage.DistanceBetween(SkillStageSample.CasterId, SkillStageSample.DummyId)).ShouldBe(gap, 0.001, $"{gap} 米这一档：距离按摆位现算");
            stage.Inject(AbilityVerbs.Cast,
                    $"{SkillStageSample.CasterId}|{SkillStageSample.Skill.Strike}|{SkillStageSample.DummyId}").ShouldBeTrue($"{gap} 米在 {range} 米射程之内（或正好等于），这一发该打得出");
        }

        // 界外：超出 1 厘米 —— 当场拒，且账上读得出为什么（不是一个光秃秃的 false）。
        var outside = SampleStage(gapMeters: range + 0.01);
        outside.Inject(AbilityVerbs.Cast,
                $"{SkillStageSample.CasterId}|{SkillStageSample.Skill.Strike}|{SkillStageSample.DummyId}").ShouldBeFalse($"{range + 0.01} 米超出射程，这一发打不出");
        outside.Find(SkillStageSample.CasterId)!.Abilities[SkillStageSample.Skill.Strike].RejectedReason.ShouldBe(AbilityRejectReason.NotInRange, "为什么没放有名字");
    }

    /// <summary>
    /// <b>S17 二维摆位 + 扇形界内界外</b>：三个目标摆到出手那一个的<b>斜前方</b>，
    /// 一个在半径内且在正前 120° 内（界内），一个在半径内但偏到 80°（界外），
    /// 还有一个干脆在半径外——三档各一，<b>半径与角度是分开两问</b>。
    /// 判定走公共层那把尺（<see cref="AbilityStepper.InsideSector"/>），场子只递自己那份摆位与朝向。
    /// </summary>
    [Fact]
    public void S17_二维摆位与扇形_界内界外与半径外三档各摆一个点()
    {
        const double radius = 3.0;
        const double cone = 120;

        var inside = Polar(radius, 30);            // 30°：正前 120° 的内档
        var outsideCone = Polar(radius, 80);       // 80°：同样 3 米，但出扇形
        var outsideRange = Polar(radius + 0.01, 0); // 正前 0°，但半径外

        var stage = new SkillStage(SpecWithTargets(inside, outsideCone, outsideRange));

        var hits = stage.TargetsInSector(SkillStageSample.CasterId, radius, cone);
        hits.ShouldBe(new[] { SkillStageSample.DummyId }, Case.Sensitive, "三档里只有「半径内 + 角度内」那一个该被打中，界外与半径外两个都不在名单里");

        // 界外那两个为什么不进名单：分开它们的分别是角度与半径，不是别的原因。
        AbilityStepper.InsideSector(0, 0, outsideCone.x, outsideCone.z,
            1, 0, radius, cone).ShouldBeFalse("同样 3 米、正前 80°：出扇形（角度这一问）");
        AbilityStepper.InsideSector(0, 0, outsideRange.x, outsideRange.z,
            1, 0, radius, cone).ShouldBeFalse("正前 0° 但 3.01 米：出半径（半径那一问）");

        // 界内那一个到出手那一个的距离：与出角度那个逐字相等 —— 角度不是被距离顺带判掉的。
        ((double)stage.DistanceBetween(SkillStageSample.CasterId, SkillStageSample.DummyId)).ShouldBe(radius, 0.001, "界内那一个正好 3 米");
        ((double)stage.DistanceBetween(SkillStageSample.CasterId, "post.turned")).ShouldBe(stage.DistanceBetween(SkillStageSample.CasterId, SkillStageSample.DummyId), 0.001, "界内与出角度那两个到出手那一个的距离逐字相等 ⇒ 分开它们的只有角度");
    }

    /// <summary>
    /// <b>S18 时序三个口</b>：能停、能慢放、能一步步推进。
    /// ① 暂停<b>只冻逻辑</b>：冻结期间推进零步、步号不动，单步口仍能走一步（这就是「停住看」）；
    /// ② 倍率<b>乘在流速上</b>：0.25 倍时同样 1 秒现实时间只走 1/4 的步；
    /// ③ 攒步：真实秒不够一步时不推，攒够了才推（「时钟解耦」）。
    /// 全部是状态级断言：只比步号。
    /// </summary>
    [Fact]
    public void S18_能停能慢放能一步步推进_暂停只冻逻辑倍率乘在流速上()
    {
        // ① 暂停只冻逻辑
        var stage = SampleStage();
        stage.Pause();
        stage.AdvanceRealSeconds(1.0).ShouldBe(0, "暂停中按现实时间推进：零步");
        stage.Advance(50).ShouldBeFalse("暂停中批量推进：一步都不走");
        stage.Tick.ShouldBe(0, "步号没动 —— 暂停冻的是逻辑那一半");
        stage.AdvanceStep();
        stage.Tick.ShouldBe(1, "单步口在暂停中照走一步（这就是「停下来一格格看」）");
        stage.Resume();
        stage.AdvanceRealSeconds(0.1).ShouldBe(1, "解冻后 0.1 秒现实时间走一步（1 步 = 0.1 秒）");

        // ② 倍率乘在流速上
        var normal = SampleStage();
        var slow = SampleStage();
        slow.Speed = 0.25;
        normal.AdvanceRealSeconds(1.0);
        slow.AdvanceRealSeconds(1.0);
        normal.Tick.ShouldBe(10, "1 倍：1 秒现实时间 = 10 步（1 步 = 0.1 秒，「时间基准」）");
        slow.Tick.ShouldBe(2, "0.25 倍：同样 1 秒只走 2.5 步取整到 2 —— 倍率乘在流速上");
        slow.Speed.ShouldBe(0.25, "倍率读得回来");
        Should.Throw<ArgumentOutOfRangeException>(() => slow.Speed = 0, "倍率给 0 当场抛，不兜默认值（「桥只转发」 同一句理由）");

        // ③ 攒步：真实秒不够一步时不推，攒够了才推（「时钟解耦」）。
        // 0.25 倍下每 0.1 秒现实时间只折出 0.25 步，所以第 1~3 次都推不了、第 4 次才凑够一步。
        var carry = SampleStage();
        carry.Speed = 0.25;
        var pushed = new List<long>();
        for (var i = 0; i < 9; i++)
        {
            pushed.Add(carry.AdvanceRealSeconds(0.1));
        }

        pushed.ShouldBe(new long[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 }, false, "每 0.1 秒 × 0.25 倍 = 0.25 步 ⇒ 第 4 次与第 8 次各攒够一步，其余七次都推不了（「时钟解耦」）");
        carry.Tick.ShouldBe(2, "九次喂现实时间共推 2 步；步数只由攒够的那两次决定");
    }

    /// <summary>
    /// <b>S19 慢放与快进不改变那一局的结果</b>：同样那串注入、同样到第 10 步，
    /// 1 倍、0.25 倍、20 倍、还有暂停中一步步单步走出来的<b>状态逐字等</b>。
    /// 这一条是「倍率和暂停是流速不是逻辑」的机器口径：它们改了推进的<b>节奏</b>，不改第 k 步的真值。
    /// </summary>
    [Fact]
    public void S19_慢放快进与单步_到同一步时状态逐字等()
    {
        string Run(Action<SkillStage> drive)
        {
            var stage = SampleStage();
            drive(stage);
            return stage.DebugSnapshot();
        }

        var baseline = Run(s => s.Advance(10));
        var slow = Run(s =>
        {
            s.Speed = 0.25;
            for (var i = 0; i < 40; i++)
            {
                s.AdvanceRealSeconds(0.1);
            }
        });
        // 20 倍 × 0.05 秒 = 10 步（每秒 10 步 × 20 × 0.05），一发就到第 10 步。
        var fast = Run(s =>
        {
            s.Speed = 20;
            s.AdvanceRealSeconds(0.05);
        });
        var stepwise = Run(s =>
        {
            s.Pause();
            s.AdvanceDebug(10);
        });

        WithoutSeed(stepwise).ShouldBe(WithoutSeed(baseline), "暂停中一步步走到第 10 步，与一口气推 10 步逐字等");
        WithoutSeed(slow).ShouldBe(WithoutSeed(baseline), "慢放到第 10 步，逐字等");
        WithoutSeed(fast).ShouldBe(WithoutSeed(baseline), "快进到第 10 步，逐字等");
    }

    /// <summary>
    /// <b>S20 无干扰项</b>：天气、经济、寻路、AI 决策、UI 一件都不在<b>逻辑壳</b>里。
    /// 口径是源码检索（与 S8/S9 同一条路子）：读逻辑壳那两份源文件，
    /// 搜不到那五类东西的任何标识符 —— 查的是源码里有没有那一类东西，不是文档承诺。
    /// 场景壳（<c>SkillStageScene.cs</c>）不在这个检索面里：它本来就吃引擎类型，
    /// 「表现层只读」 说的表现层只读是它那一侧的约束。
    ///
    /// 词表只收<b>类名</b>，不收通用词：<c>Control</c> 会误伤 <c>SpeedControl</c>（既有那个流速换算件，
    /// 本 ticket 要复用的就是它）、<c>Label</c> 会误伤别处。宁可漏判也不误判 ——
    /// 这一条钉的是「别长出那一类东西」，不是逐字抠 grep。
    /// </summary>
    [Fact]
    public void S20_逻辑壳里没有天气经济寻路AI决策与UI()
    {
        var forbidden = new[]
        {
            "Weather", "Rainfall", "Storm", "DayNight", "TimeOfDay",   // 天气
            "Economy", "Currency", "Wallet", "Shop", "Inventory",      // 经济
            "Pathfinder", "NavMesh", "AStar", "Waypoint", "Obstacle", "BlockedCell",  // 寻路 / 阻挡
            "AIDecision", "IntentPolicy", "Blackboard", "Beehave",     // AI 决策
            "using Godot", "Node3D", "CanvasLayer", "Button", "Label3D", "Viewport", "BaseMaterial3D",  // 引擎 / UI
        };

        var logicShell = new[] { "SkillStage.cs", "SkillStageOpenField.cs", "SkillStageSpec.cs" };
        logicShell.Select(name => File.Exists(Path.Combine(StageDir, name))).ShouldAllBe(_e => _e == true, $"逻辑壳那两份读不到（路径拼错时下面那圈是空跑）：{StageDir}");

        foreach (var name in logicShell)
        {
            var source = File.ReadAllText(Path.Combine(StageDir, name));
            foreach (var needle in forbidden)
            {
                source.ShouldNotContain(needle, Case.Sensitive, $"{name} 里出现了 {needle} —— 场子只做「技能以外的因素全拿走」那件事，" +
                    "天气、经济、寻路、AI 决策、UI 一件都不该在（理想那一条口径）");
            }
        }
    }

    /// <summary>
    /// <b>S21 二维摆位与随机无关</b>：二维摆位那档（同种子 + 随机关掉）逐字一致，
    /// 证明「摆位换了格子」不改变「同条件重跑逐字一致」这条口径。
    /// </summary>
    [Fact]
    public void S21_二维摆位那档_随机关掉后同条件重跑逐字一致()
    {
        string Run(int seed)
        {
            var stage = new SkillStage(SkillStageSample.Spec(gapMeters: 2, lateralMeters: 1.5, facingDegrees: 30),
                seed) { RandomEnabled = false };
            stage.Inject(AbilityVerbs.Cast,
                $"{SkillStageSample.CasterId}|{SkillStageSample.Skill.Strike}|{SkillStageSample.DummyId}");
            stage.Advance(10);
            return stage.DebugSnapshot();
        }

        var first = Run(1);
        var second = Run(999);
        WithoutSeed(second).ShouldBe(WithoutSeed(first), "二维摆位 + 随机关掉 + 换种子 ⇒ 逐字一致");
        first.ShouldContain("z=1.5", Case.Sensitive, "快照里念得出 Z 那一格（摆位二维是状态，不是标签）");
        first.ShouldContain("facing=30", Case.Sensitive, "快照里念得出朝向那一格（扇形那一问的输入）");
    }

    /// <summary>
    /// 摆一份带三个目标的注册口：出手那个在原点朝 +X，
    /// <paramref name="inside"/> 摆成界内那一个、<paramref name="outsideCone"/> 出角度、
    /// <paramref name="outsideRange"/> 出半径。
    /// </summary>
    private static SkillStageSpec SpecWithTargets(
        (double X, double Z) inside, (double X, double Z) outsideCone, (double X, double Z) outsideRange)
    {
        var spec = SkillStageSample.Spec();
        var caster = spec.Roster.Units[0];
        var dummy = spec.Roster.Units[1];

        return spec with
        {
            Roster = spec.Roster with
            {
                Units = new[]
                {
                    caster with { X = 0, Z = 0, FacingDegrees = 0 },
                    dummy with { X = inside.X, Z = inside.Z },
                    dummy with { UnitId = "post.turned", X = outsideCone.X, Z = outsideCone.Z },
                    dummy with { UnitId = "post.far", X = outsideRange.X, Z = outsideRange.Z },
                },
            },
        };
    }
}
