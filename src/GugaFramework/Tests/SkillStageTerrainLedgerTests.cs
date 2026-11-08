#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using DemoFactory.GugaFramework.Abilities;
using DemoFactory.GugaFramework.Abilities.Stage;
using Shouldly;
using Xunit;
using DemoFactory.GugaFramework.Tests;

namespace DemoFactory.Tests;

/// <summary>
/// <b>第一个场子自己的台账</b>：地形条件那几格（远近两侧、扇形界内外、钉住位置）逐档手算。
///
/// 与 <see cref="AbilityChecklistTests"/> 那 16 条的分工：清单是「功能面有没有漏」，
/// 这里是「场地条件摆得对不对」——16 条里没有一格问射程边界两侧、也没有一格问扇形，
/// 所以这两块各由本文件的一格判。断言只比状态（<see cref="SkillStage.DebugSnapshot"/> 与几个数值口，
/// 「断言比快照」），事件流逐字相等不做。
///
/// 摆位、射程、扇形角度这些数全部来自 <see cref="SkillStageSample"/>（示范注册壳）那份，
/// 场子一个都不许有（S8 钉住这一格）。每一格都报得出「差在哪一步」。
/// </summary>
public sealed class SkillStageTerrainLedgerTests
{
    /// <summary>起一局：摆位与种子都由调用点给，场子不替它挑（默认那一档是 2 米、种子 1）。</summary>
    private static SkillStage SampleStage(double gapMeters = SkillStageSample.DefaultGapMeters, int seed = 1)
        => new(SkillStageSample.Spec(gapMeters), seed);

    private static double Vitality(SkillStage stage, string unitId)
    {
        var set = stage.Find(unitId);
        set.ShouldNotBeNull($"场子里没有 {unitId}（注册口那一份没摆上？）");
        return set!.Current[SkillStageSample.Attr.Vitality];
    }

    /// <summary>把一轮台账逐行念到测试输出里（红的时候不用再回头翻源码就知道差在哪一格）。</summary>
    private static void Ledger(string line) => TestLog.Ledger($"地形台账｜{line}");

    // === 台账 L1：远近两侧（射程边界两侧各一格 + 从贴脸到超距逐档）===

    /// <summary>
    /// <b>L1 远近两侧</b>：以示范注册壳那 3 米射程为界，界内三档（0.5 / 2.99 / 3.0 米）放得出，
    /// 界外一档（3.01 米）当场拒且账上念得出 <c>NotInRange</c>。
    /// 距离是场子那份摆位现算的（<see cref="SkillStage.DistanceBetween"/>），所以「摆多远」与「判多远」同一份实现。
    /// 逐档报出距离读数：手算就是 <c>√(dx² + dz²)</c>，摆成直线那一档 dz = 0 ⇒ 距离 = 间距。
    /// </summary>
    [Fact]
    public void L1_射程边界两侧各判一次_距离按摆位现算()
    {
        var range = SkillStageSample.StrikeRangeMeters;
        Ledger($"L1 射程 = {range.ToString(CultureInfo.InvariantCulture)} 米（示范注册壳那一份）");

        foreach (var gap in new[] { 0.5, range - 0.01, range })
        {
            var stage = SampleStage(gapMeters: gap);
            var distance = stage.DistanceBetween(SkillStageSample.CasterId, SkillStageSample.DummyId);
            ((double)distance).ShouldBe(gap, 0.001, $"{gap} 米这一档：直线摆位（dz = 0）下距离读数应当等于间距，实到 {distance}");
            stage.Inject(AbilityVerbs.Cast,
                    $"{SkillStageSample.CasterId}|{SkillStageSample.Skill.Strike}|{SkillStageSample.DummyId}").ShouldBeTrue($"{gap} 米在 {range} 米射程之内（或正好等于），这一发该打得出");
            Ledger($"  界内 {gap} 米：距离 {distance:0.###}，放得出");
        }

        var outside = SampleStage(gapMeters: range + 0.01);
        ((double)outside.DistanceBetween(SkillStageSample.CasterId, SkillStageSample.DummyId)).ShouldBe(range + 0.01, 0.001, "界外那一档的距离读数");
        outside.Inject(AbilityVerbs.Cast,
                $"{SkillStageSample.CasterId}|{SkillStageSample.Skill.Strike}|{SkillStageSample.DummyId}").ShouldBeFalse($"{range + 0.01} 米超出射程，这一发打不出");
        outside.Find(SkillStageSample.CasterId)!.Abilities[SkillStageSample.Skill.Strike].RejectedReason.ShouldBe(AbilityRejectReason.NotInRange, "为什么没放有名字（不是一个光秃秃的 false）");
        Ledger($"  界外 {range + 0.01} 米：当场拒，账上 NotInRange");
    }

    /// <summary>
    /// <b>L1 附：清单注册壳那份的每一条带射程技能都判一次边界两侧</b>。
    /// 走的是 <see cref="StageChecklistShell.RangedSkills"/> 那一份，所以往注册壳里加一条带射程的技能，
    /// 这一格自动多一档——<b>台账与用例一个字都不用改</b>（这是「加一条技能只改注册壳」那条口径的回归护栏）。
    /// </summary>
    [Fact]
    public void L1b_清单注册壳每条带射程的技能_边界两侧各判一次()
    {
        foreach (var (abilityId, range) in StageChecklistShell.RangedSkills)
        {
            var inside = StageChecklistShell.NewStage(gapMeters: range - 0.01);
            inside.Inject(AbilityVerbs.Cast,
                    $"{StageChecklistShell.CasterId}|{abilityId}|{StageChecklistShell.TargetId}").ShouldBeTrue($"{abilityId}：{range - 0.01} 米在 {range} 米射程之内");

            var outside = StageChecklistShell.NewStage(gapMeters: range + 0.01);
            outside.Inject(AbilityVerbs.Cast,
                    $"{StageChecklistShell.CasterId}|{abilityId}|{StageChecklistShell.TargetId}").ShouldBeFalse($"{abilityId}：{range + 0.01} 米超出 {range} 米射程");
            outside.Find(StageChecklistShell.CasterId)!.Abilities[abilityId].RejectedReason.ShouldBe(AbilityRejectReason.NotInRange, $"{abilityId}：为什么没放有名字");
            Ledger($"  {abilityId}：界内 {range - 0.01} 放得出 / 界外 {range + 0.01} 当场拒");
        }
    }

    // === 台账 L2：扇形界内外（角度这一问与半径那一问分开判）===

    /// <summary>
    /// <b>L2 扇形界内外</b>：三个目标摆到出手那一个的斜前方，界内 30°、出角度 80°、出半径 3.01 米。
    /// 界内与出角度那两个<b>到出手那一个的距离逐字相等</b>（都是 3 米）——分开它们的只有角度，
    /// 所以这一格证明「半径够不够」与「角度够不够」是两问，不是一问顺带判掉的。
    /// 判定走公共层那把尺（<see cref="AbilityStepper.InsideSector"/>），场子只递自己那份摆位与朝向。
    /// </summary>
    [Fact]
    public void L2_扇形界内界外与半径外三档各摆一个点()
    {
        const double radius = 3.0;
        const double cone = 120;
        var inside = Polar(radius, 30);
        var outsideCone = Polar(radius, 80);
        var outsideRange = Polar(radius + 0.01, 0);
        Ledger($"L2 半径 = {radius} 米、全角 = {cone} 度；界内 30° / 出角度 80° / 出半径 {radius + 0.01} 米");

        var stage = new SkillStage(SpecWithTargets(inside, outsideCone, outsideRange));
        var hits = stage.TargetsInSector(SkillStageSample.CasterId, radius, cone);
        hits.ShouldBe(new[] { SkillStageSample.DummyId }, Case.Sensitive, "三档里只有「半径内 + 角度内」那一个该被打中");

        // 分开三档的分别是角度与半径：各自单问一次，答案与上面那份名单一致。
        AbilityStepper.InsideSector(0, 0, inside.x, inside.z, 1, 0, radius, cone).ShouldBeTrue("正前 30°、3 米：扇内（两问都过）");
        AbilityStepper.InsideSector(0, 0, outsideCone.x, outsideCone.z, 1, 0, radius, cone).ShouldBeFalse("同样 3 米、正前 80°：出扇形（角度这一问）");
        AbilityStepper.InsideSector(0, 0, outsideRange.x, outsideRange.z, 1, 0, radius, cone).ShouldBeFalse($"正前 0° 但 {radius + 0.01} 米：出半径（半径那一问）");

        var dInside = stage.DistanceBetween(SkillStageSample.CasterId, SkillStageSample.DummyId);
        var dOutsideCone = stage.DistanceBetween(SkillStageSample.CasterId, "post.turned");
        ((double)dInside).ShouldBe(radius, 0.001, "界内那一个正好 3 米");
        ((double)dOutsideCone).ShouldBe(dInside, 0.001, "界内与出角度那两个到出手那一个的距离逐字相等 ⇒ 分开它们的只有角度");
        Ledger($"  界内 3 米 / 出角度 3 米（距离逐字相等）/ 出半径 {radius + 0.01} 米");
    }

    /// <summary>极坐标（米 + 角度）→ 二维点（度）。</summary>
    private static (double x, double z) Polar(double meters, double degrees)
    {
        var rad = degrees * Math.PI / 180.0;
        return (meters * Math.Cos(rad), meters * Math.Sin(rad));
    }

    /// <summary>
    /// 递一份带三个目标的注册口：出手那个在原点朝 +X，
    /// <paramref name="inside"/> 摆成界内那一个、<paramref name="outsideCone"/> 出角度、<paramref name="outsideRange"/> 出半径。
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

    // === 台账 L3：钉住位置（场子不替单位走位）===

    /// <summary>
    /// <b>L3 钉住位置</b>：一整局 60 步（6 秒）里两个单位一步没挪、朝向也没被改；
    /// <b>反面也在同一格里</b>：注入 <c>Move</c> 那一发真的挪得动（否则「钉住」就分不清是钉住了还是根本改不动）。
    /// 逐档报出摆位：摆进去什么样，60 步之后还什么样。
    /// </summary>
    [Fact]
    public void L3_一整局跑完位置与朝向逐字未变_只有注入位移改得动()
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
        Ledger($"L3 60 步之后：出手 ({caster.X:0.###}, {caster.Z:0.###})、挨打 ({dummy.X:0.###}, {dummy.Z:0.###})、朝向 {facing:0.###}，逐字未变");

        // 反面：位置确实「可」改，只是只能从写路径改。
        stage.Inject(AbilityVerbs.Move, $"{SkillStageSample.DummyId}|1.5").ShouldBeTrue("注入 Move 这一发是位置唯一的写路径");
        stage.XOf(SkillStageSample.DummyId).ShouldBe(dummy.X + 1.5, "注入的那几米真的挪了 —— 所以上面那三条是真的钉住，不是根本改不动");
        stage.ZOf(SkillStageSample.DummyId).ShouldBe(dummy.Z, "Move 那一格只带一个标量，走的是 X 那根轴，Z 不动（次序在 AbilityVerbs）");
        Ledger($"  注入 Move 1.5 米之后：挨打 X = {stage.XOf(SkillStageSample.DummyId):0.###}、Z 仍 = {stage.ZOf(SkillStageSample.DummyId):0.###}");
    }

    // === 台账 L4：换场地条件就是换一份注册口重起一局（场子不替人挪位置）===

    /// <summary>
    /// <b>L4 换摆位就是换一份注册口</b>：同一个动作在两份注册口上跑出两份读数，
    /// 而场子那几份一个字都没改（注册口递什么摆位，场子就摆什么）。
    /// 这一格是「位置是输入的常量、不是场子的输出」那句话的机器口径。
    /// </summary>
    [Fact]
    public void L4_换一份注册口的摆位_场子跑出对应的距离与伤害()
    {
        // 界内 2 米：放得出，出手强度 25 一刀 ⇒ 掉 25。
        var near = SampleStage(gapMeters: 2);
        var nearBefore = Vitality(near, SkillStageSample.DummyId);
        near.Inject(AbilityVerbs.Cast,
                $"{SkillStageSample.CasterId}|{SkillStageSample.Skill.Strike}|{SkillStageSample.DummyId}").ShouldBeTrue("2 米那一局放得出");
        near.Advance(SkillStageSample.StrikePreSteps);
        ((double)Vitality(near, SkillStageSample.DummyId)).ShouldBe(nearBefore - 25, 0.001, "2 米那一局：出手强度 25 一刀，掉 25");

        // 界外 5 米：同一发打不出，血一滴不掉。
        var far = SampleStage(gapMeters: 5);
        var farBefore = Vitality(far, SkillStageSample.DummyId);
        far.Inject(AbilityVerbs.Cast,
                $"{SkillStageSample.CasterId}|{SkillStageSample.Skill.Strike}|{SkillStageSample.DummyId}").ShouldBeFalse("5 米那一局打不出");
        far.Advance(SkillStageSample.StrikePreSteps);
        Vitality(far, SkillStageSample.DummyId).ShouldBe(farBefore, "打不出就一滴不掉");
        Ledger("L4 界内 2 米：放得出、掉 25；界外 5 米：打不出、一滴不掉（换的是注册口那份摆位）");
    }

    // === 清单那份本体与注册壳的机器口径（ticket 05）===

    /// <summary>
    /// <b>S22 清单那份本体只有一份，且住在公共层</b>：16 条断言只写在
    /// <c>src/GugaFramework/Tests/AbilityChecklistTests.cs</c> 一处，四个注册壳（三个原型侧 + 场子侧）
    /// 只递名字与数字。判据是「清单那份文件的类名在本测试程序集里只出现一次定义」加上
    /// 「原型目录里已经没有了那份文件」——后者正是这一 ticket 要还的账（场子不隶属任何原型，
    /// 清单住在原型目录里就等于让公共层反过来认原型）。
    /// </summary>
    [Fact]
    public void S22_清单那份本体只有一份_且住在公共层()
    {
        var here = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "AbilityChecklistTests.cs"));
        File.Exists(here).ShouldBeTrue($"清单那份本体不在公共层测试目录里：{here}");
        File.ReadAllText(here).ShouldContain("class AbilityChecklistTests", Case.Sensitive, "读到的是清单那份本体（路径拼错了这条会空跑）");

        // 原型目录里不该再有那份文件（挪进来的那一步的回执）。
        // 扫全部原型目录，一个都不点名：点名某一个，那个原型改名或毕业之后
        // File.Exists 对不存在的路径返回 false，这条会静默变绿——守卫自己不守卫了，且没有症状。
        //
        // 层级：base = <根>/src/GugaFramework/Tests/bin/Debug/net8.0，
        // 上溯 5 级到 <根>/src（net8.0→Debug→bin→Tests→GugaFramework→src），再进 Prototypes。
        // 原先这里是 6 级，落到 <根>/Prototypes（不存在），File.Exists 返回 false，
        // ShouldBeFalse 于是恒绿——这条守卫自打写下那天起就没生效过（2026-10-12 修）。
        var prototypesRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Prototypes"));
        Directory.Exists(prototypesRoot).ShouldBeTrue($"原型目录不存在，这条守卫会空跑：{prototypesRoot}");
        var prototypeTestDirs = Directory.GetDirectories(prototypesRoot, "Tests", SearchOption.AllDirectories);
        prototypeTestDirs.Length.ShouldBeGreaterThan(0, "一个原型测试目录都没扫到 —— 这条是空跑，不是通过");
        foreach (var dir in prototypeTestDirs)
        {
            File.Exists(Path.Combine(dir, "AbilityChecklistTests.cs"))
                .ShouldBeFalse($"{dir} 里又有了一份清单本体 —— 16 条断言只准有一份，两份就是从此改一处要改两处");
        }

        // 本体里 16 条一个不少（少一条当场红，不许悄悄少一条）。
        var source = File.ReadAllText(here);
        foreach (var prefix in new[] { "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8" })
        {
            source.ShouldContain($"public void {prefix}_", Case.Sensitive, $"清单本体里 {prefix} 那一格不见了（16 条是固定的，少一条要先改这一句）");
        }

        foreach (var prefix in new[] { "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8" })
        {
            source.ShouldContain($"public void {prefix}_", Case.Sensitive, $"清单本体里 {prefix} 那一格不见了（16 条是固定的，少一条要先改这一句）");
        }
    }

    /// <summary>
    /// <b>S23 注册壳里没有断言、没有动词字面量</b>：壳一旦长出 <c>Assert.</c>，
    /// 就说明某一格的断言被抄进了注册壳侧，从此改一处要改两份（原型侧 A70 是同一条口径的另一半）。
    /// 同时扫动词名字面量：动词名单只准有公共层 <c>AbilityVerbs</c> 那一份，壳只许引用常量。
    /// </summary>
    [Fact]
    public void S23_注册壳里没有断言也没有动词字面量()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "AbilityChecklistStageHost.cs"));
        File.Exists(path).ShouldBeTrue($"读不到场子侧的注册壳：{path}");
        var source = File.ReadAllText(path);
        source.ShouldContain("class ChecklistStageHost", Case.Sensitive, "读到的是注册壳本体（读错文件或改名了，这条会空跑）");
        source.ShouldNotContain("Assert.", Case.Sensitive, "注册壳里长出了断言 —— 断言只能有清单本体那一份，壳只递名字与数字");
        source.ShouldNotContain("[Fact]", Case.Sensitive, "注册壳里长出了测试方法（同上：一处断言只准有一份）");

        foreach (var verb in AbilityVerbs.Names)
        {
            source.ShouldNotContain($"\"{verb}\"", Case.Sensitive,
                $"注册壳里写了动词名 {verb} 的字面量 —— 名单只准有公共层那一份");
        }
    }
}
