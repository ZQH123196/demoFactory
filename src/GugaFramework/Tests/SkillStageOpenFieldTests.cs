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
/// <b>另两个场子的用例</b>（ticket 07）：开阔多点和长时段各一组，断言只比<b>状态级</b>
/// （属性读数、名单、快照，「断言比快照」：事件流逐字相等不做）。
///
/// <b>两个场子与第一个场子共用同一套注册口</b>（<see cref="SkillStageSpec"/>，S30 判这一条）：
/// 开阔多点是派生出来的那一个（<see cref="SkillStageOpenField"/>，只加「群体名单」与「群体施加」两发），
/// 长时段并进第一个场子的<b>参数</b>（步数 / 观测窗口容量 / 账本上限，全在注册壳那一侧，
/// 场子与技能层一个字没改）——那笔裁的依据原话记在 `docs/GugaFramework/技能试验场.md`「另两个场子」一节。
///
/// <b>数字口径</b>：1 步 = 0.1 秒，报「第 k 步」（「时间基准」）。全部手算数字都在
/// <see cref="SkillStageSample"/>（开阔那一份）与 <see cref="StageChecklistShell"/>（长时段那三个数）里，
/// 场子一个都不许有（S8 钉住这一格）。
/// </summary>
public sealed class SkillStageOpenFieldTests
{
    private const double StrikeDamage = 25.0;   // 示范注册壳那份的出手强度（training.power）

    // === 开阔多点：场地条件与两问 ===

    private static SkillStageOpenField OpenField(int seed = 1)
        => new(SkillStageSample.SpecOpenField(), seed);

    private static double Vitality(SkillStage stage, string unitId, string attribute)
    {
        var set = stage.Find(unitId);
        set.ShouldNotBeNull($"场子里没有 {unitId}（注册口那一份没摆上？）");
        return set!.Current[attribute];
    }

    /// <summary>效果几层（没有这条效果返回 -1）。读的是层那一个口。</summary>
    private static int Stacks(SkillStage stage, string unitId, string effectId)
        => stage.Stepper.World.Units[unitId].Effects.FirstOrDefault(e => e.EffectId == effectId)?.Stacks ?? -1;

    /// <summary>把一轮台账逐行念到测试输出里（红的时候不用回头翻源码就知道差在哪一格）。</summary>
    private static void Ledger(string line) => TestLog.Ledger($"场子台账｜{line}");

    /// <summary>
    /// <b>S25 开阔多点摆得出多目标与扇形群体</b>：六个目标散在 6~12 米的开阔地上，
    /// 范围那一问与扇形那一问<b>各给一份名单</b>，两份的差就是「角度那一问」与「距离那一问」各自排掉了谁。
    ///
    /// 手算（布局全在 <see cref="SkillStageSample.OpenField"/>）：
    /// 半径 8 米之内 5 个（front 8 / mid 6 / near_edge 6 / wide 6 / back 6；far 12 出半径）；
    /// 全角 120 度（正前 ±60）之内 3 个（front 0° / mid 40° / near_edge 55°；
    /// wide 80° 与 back 180° 出角度）。<b>mid 与 wide 的距离逐字相等</b>——分开它们的只有角度。
    /// </summary>
    [Fact]
    public void S25_开阔多点_范围与扇形两问各念一份名单()
    {
        var stage = OpenField();
        var caster = SkillStageSample.CasterId;
        var radius = SkillStageSample.OpenField.RadiusMeters;
        var cone = SkillStageSample.OpenField.ConeDegrees;

        stage.UnitIds.Count.ShouldBe(7, "一个出手 + 六个目标（多目标同框那一档）");
        ((double)stage.DistanceBetween(caster, SkillStageSample.OpenField.Front)).ShouldBe(8.0, 0.001, "front 摆的是 8 米 —— 摆位与判位是同一份实现");
        stage.FacingDegreesOf(caster).ShouldBe(0.0, "出手那一个朝 +X（0 度）");

        var inRadius = stage.TargetsInRadius(caster, radius);
        var inSector = stage.TargetsInSector(caster, radius, cone);
        inRadius.ShouldBe(new[]
            {
                SkillStageSample.OpenField.Front, SkillStageSample.OpenField.Mid, SkillStageSample.OpenField.NearEdge,
                SkillStageSample.OpenField.Wide, SkillStageSample.OpenField.Back,
            }, Case.Sensitive, "半径 8 米之内 5 个：只有 12 米那一个出半径");
        inSector.ShouldBe(new[]
            {
                SkillStageSample.OpenField.Front, SkillStageSample.OpenField.Mid, SkillStageSample.OpenField.NearEdge,
            }, Case.Sensitive, "正前 ±60 度之内 3 个：80 度与 180 度那两个出角度");
        Ledger($"S25 范围 8 米：{string.Join(",", inRadius)}（5 个）；扇形 8 米 / 120 度：{string.Join(",", inSector)}（3 个）");

        // 分开两问的分别是距离与角度：逐字读出来，不靠嘴说。
        ((double)stage.DistanceBetween(caster, SkillStageSample.OpenField.Mid)).ShouldBe(stage.DistanceBetween(caster, SkillStageSample.OpenField.Wide), 0.001, "mid 与 wide 到出手那一个的距离逐字相等（都是 6 米）⇒ 分开它们的只有角度");
        ((double)stage.DistanceBetween(caster, SkillStageSample.OpenField.Far)).ShouldBe(SkillStageSample.OpenField.RadiusMeters + 4, 0.001, "far 摆在半径外 4 米那一档");

        // 全角给 0 的含义与公共层那把尺一致（coneDegrees <= 0 = 不限角度 = 整个圆）：
        // 于是「扇形那一问 + 不限角度」与「范围那一问」必须给出同一份名单。
        stage.TargetsInSector(caster, radius, coneDegrees: 0).ShouldBe(inRadius, "全角 0 = 整个圆 ⇒ 名单与范围那一问逐字相同（两问分开的只有角度那一格）");
    }

    /// <summary>
    /// <b>S26 群体施加</b>：一份名单逐个施加，<b>走的是 <c>Inject</c> 那一条写路径</b>（「桥只转发」），
    /// 名单先落地成形状事件，于是「这一发打到了谁」在事件流里是一行带名单的读数。
    ///
    /// 手算：出手强度 25 ⇒ 名单里每一位掉 25（效果表里那一刀按施法者的出手强度算）。
    /// 扇形那档 3 个 ⇒ 共 75；范围那档 5 个 ⇒ 共 125。没进名单的一滴不掉。
    /// </summary>
    [Fact]
    public void S26_群体施加_逐个走写路径且打到了谁念得出来()
    {
        var caster = SkillStageSample.CasterId;
        var vit = SkillStageSample.Attr.Vitality;
        var radius = SkillStageSample.OpenField.RadiusMeters;
        var cone = SkillStageSample.OpenField.ConeDegrees;
        var inSector = new[]
        {
            SkillStageSample.OpenField.Front, SkillStageSample.OpenField.Mid, SkillStageSample.OpenField.NearEdge,
        };

        // 扇形那一档
        var sector = OpenField();
        var landed = sector.ApplyEffectToGroupInSector(caster, radius, cone, SkillStageSample.Fx.StrikeHit);
        landed.ShouldBe(inSector, "扇形里那 3 个都真的打上去了（返回的是打上去了那几位）");
        foreach (var unitId in SkillStageSample.OpenFieldTargets.Select(t => t.UnitId))
        {
            var expected = inSector.Contains(unitId) ? 100.0 - StrikeDamage : 100.0;
            ((double)Vitality(sector, unitId, vit)).ShouldBe(expected, 0.001, $"{unitId}：进了名单掉 {StrikeDamage.ToString("0.####", CultureInfo.InvariantCulture)}，没进的没掉");
        }

        var tail = string.Join('\n', sector.EventTailLines(8));
        tail.ShouldContain($"hits={string.Join(",", inSector)}", Case.Sensitive, "名单落地成形状事件那一行：打到了谁念得出来（打空也有那一行，不静默）");
        Ledger($"S26 扇形群体：打上 {landed.Count} 个，共掉 " +
               $"{(landed.Count * StrikeDamage).ToString("0.####", CultureInfo.InvariantCulture)}");

        // 范围那一档：名单更大，掉的也更多 —— 同一发施加，两问各给各的名单。
        var circle = OpenField();
        var landedInCircle = circle.ApplyEffectToGroupInRadius(caster, radius, SkillStageSample.Fx.StrikeHit);
        landedInCircle.Count.ShouldBe(5, "半径 8 米之内 5 个都打上去了");
        landedInCircle.ShouldBe(landed.Concat(new[]
            {
                SkillStageSample.OpenField.Wide, SkillStageSample.OpenField.Back,
            }), "范围那档比扇形那档多出的正是那 2 个出角度的");
        ((double)Vitality(circle, SkillStageSample.OpenField.Far, vit)).ShouldBe(100.0, 0.001, "12 米那一个出半径：一滴不掉");

        // 「一次激活打多个」在写路径上喂不进去（能力段只收一个目标）——这一格记在常驻文档的缺口里。
        circle.Inject(AbilityVerbs.ApplyEffect,
                $"{SkillStageSample.OpenField.Far}|{SkillStageSample.Fx.StrikeHit}|-|{caster}").ShouldBeTrue("宿主要对名单外那一位单独施加时，走的还是那一条动词（逐个发，不新造动词）");
    }

    /// <summary>
    /// <b>S27 开阔多点：同条件重跑逐字一致</b>（场子「固定」那一条口径，ticket 07 验收第 3 条之一）。
    /// 同样种子、同样那份注册口、同样那串注入 ⇒ 两发快照逐字等。
    /// </summary>
    [Fact]
    public void S27_开阔多点_同条件重跑逐字一致()
    {
        string Run()
        {
            var stage = OpenField(seed: 7);
            stage.ApplyEffectToGroupInSector(SkillStageSample.CasterId, SkillStageSample.OpenField.RadiusMeters,
                SkillStageSample.OpenField.ConeDegrees, SkillStageSample.Fx.StrikeHit);
            stage.Advance(12);
            return stage.DebugSnapshot();
        }

        var first = Run();
        var second = Run();
        second.ShouldBe(first, "同种子 + 同样注册口 + 同样命令流 ⇒ 逐字一致");
        first.ShouldContain(SkillStageSample.OpenField.Far, Case.Sensitive, "快照把六个目标逐个念出来（多目标同框是状态，不是标签）");
    }

    // === 长时段：并进第一个场子的参数 ===

    /// <summary>
    /// <b>S28 长时段</b>：<b>第一个场子</b>，把三格参数按长时段拧一下（步数 1200、观测窗口 20000、账本上限 10000），
    /// 叠层、周期、持续、冷却四样跨 20 轮之后仍按位。
    ///
    /// <summary>手算（一轮 60 步，第 r 轮起点是第 60r 步）：</summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><b>叠层封顶</b>：挂 4 次，第 4 次被封在 3 层 ⇒ 每跳伤害 = 4（种子）+ 2 ×（3 − 1）= <b>8</b>；
    /// 身上另挂着一条每层 −2 的修饰符（2 × 2 = <b>4</b>），它随效果到期一起撤。</item>
    /// <b>周期按位</b>：周期 5 步，一轮里跳在 +5…+55 共 <b>11</b> 跳 ⇒ 一轮 11 × 8 = 88。</item>
    /// <item><b>持续到期即走</b>：时长 60 步 ⇒ 第 +60 步那条就没了（下一轮重新挂）。</item>
    /// <item><b>冷却</b>：前摇 3 步、冷却 20 步 ⇒ 放出的那一步在 +3 过 commit、<b>+23</b> 转好；
    /// 冷却中按下的那发记成排队，在 +23 同一个入口补放（+26 过 commit）。两刀各 20。</item>
    /// </list>
    /// 一轮合计 88 + 40 = <b>128</b>：第 60(r+1) 步的账读数应当是 <c>上限 − 128 × (r + 1)</c>；
    /// 轮中（第 +28 步）那条修饰符还挂着，所以再多减 4。
    /// </remarks>
    [Fact]
    public void S28_长时段并进第一个场子的参数_叠层周期持续冷却跨1200步仍按位()
    {
        const long roundSteps = 60;
        const long rounds = 20;
        const int perRoundDamage = 128;   // 11 跳 × 8 + 两刀 × 20
        const int standingModifier = 4;   // 封顶 3 层时身上挂着的每层 −2 那条（2 × 2），到期随效果撤
        const int midRoundDamage = 80;    // 已跳的 5 跳 × 8 + 两刀 × 20

        var stage = StageChecklistShell.NewLongRunStage();
        var caster = StageChecklistShell.CasterId;
        var target = StageChecklistShell.TargetId;
        var wound = StageChecklistShell.Fx.WoundWound;
        var windup = StageChecklistShell.Skill.Windup;
        var vit = StageChecklistShell.Attr.Vitality;

        stage.ShouldBeOfType<SkillStage>("长时段用的就是第一个场子（不是另一个类）");

        for (var round = 0L; round < rounds; round++)
        {
            var start = round * roundSteps;
            stage.Tick.ShouldBe(start, $"第 {start} 步起手（1 步 = 0.1 秒，「时间基准」）");

            // 叠层：挂四次，前三次叠到 3 层，第四次封顶（不刷时长，所以它到点就走）。
            for (var i = 0; i < 4; i++)
            {
                stage.Inject(AbilityVerbs.ApplyEffect, $"{target}|{wound}").ShouldBeTrue("挂持续效果没打成");
            }

            Stacks(stage, target, wound).ShouldBe(3, $"第 {start} 步挂了 4 次仍是 3 层 —— 封顶 3 层（每层 +2，第 4 次不叠第 4 层）");

            // 冷却：放一发，前摇 3 步后过 commit，冷却到 +23。
            stage.Inject(AbilityVerbs.Cast, $"{caster}|{windup}|{target}").ShouldBeTrue("冷却好了这一发该放得出");
            stage.Advance(StageChecklistShell.WindupPreSteps);
            var ability = stage.Stepper.World.Units[caster].Abilities[windup];
            ability.Phase.ShouldBe(AbilityPhase.Cooling, $"第 {start + 3} 步过 commit，进冷却");
            ability.CooldownEndsAtTick.ShouldBe(start + 3 + StageChecklistShell.WindupCooldownSteps, $"冷却到期步 = commit 步 {start + 3} + 冷却 {StageChecklistShell.WindupCooldownSteps} 步");

            // 冷却中再按：当场拒且记成排队，不丢掉。
            stage.Inject(AbilityVerbs.Cast, $"{caster}|{windup}|{target}").ShouldBeFalse("冷却中这一发打不出");
            ability.QueuedInput.ShouldBeTrue("但它被记下来了（排一个）");

            // 推到冷却转好的那一步：排的那发在同一个 S4 入口补放。
            stage.Advance(StageChecklistShell.WindupCooldownSteps);
            stage.Tick.ShouldBe(start + 3 + StageChecklistShell.WindupCooldownSteps, "推到转好那一步");
            ability.Phase.ShouldBe(AbilityPhase.Pre, "转好那一步排的那发就补放了（前摇 3 步重新起）");
            ability.StepsRemaining.ShouldBe(StageChecklistShell.WindupPreSteps, "补放的前摇从头起算");

            // 轮中检查点（第 +28 步）：5 跳 × 8 + 两刀 × 20 = 80，身上的每层 −2 那条修饰符还挂着（−4）。
            stage.Advance(5);
            ((double)Vitality(stage, target, vit)).ShouldBe(StageChecklistShell.LongRunCeiling - (perRoundDamage * round) - midRoundDamage
                    - standingModifier, 0.001, $"第 {stage.Tick} 步：已过去 {round} 轮整 + 本轮 5 跳 + 两刀，账读数逐格对得上（周期没漂）");

            // 轮尾（第 +60 步）：本轮 11 跳全跳完，那条持续效果到点就走。
            stage.Advance(roundSteps - 28);
            Stacks(stage, target, wound).ShouldBe(-1, $"第 {start + 60} 步：时长 60 步到期，那条持续效果走了（没被续命）");
            ((double)Vitality(stage, target, vit)).ShouldBe(StageChecklistShell.LongRunCeiling - (perRoundDamage * (round + 1)), 0.001, $"第 {stage.Tick} 步：一整轮 11 跳 + 两刀 = {perRoundDamage} 血，{round + 1} 轮下来逐格对得上" +
                $"（那条每层 −2 的修饰符随效果一起撤了，所以这里不再多减 {standingModifier}）");
            ability.Phase.ShouldBe(AbilityPhase.Ready, "轮尾冷却早转好了（等着下一轮再放）");
        }

        stage.Tick.ShouldBe(rounds * roundSteps, $"一共跑了 {rounds * roundSteps} 步");
        stage.Tick.ShouldBe(StageChecklistShell.LongRunSteps, "就是长时段那一份配的步数");
        stage.Readout().ShouldContain("dropped=0", Case.Sensitive, $"1200 步一局，观测窗口按长时段配到 {StageChecklistShell.LongRunEventCapacity} 条，一条都没被挤掉（「三条铁律」 第三条）");
        Ledger($"S28 {rounds} 轮 × {roundSteps} 步 = {rounds * roundSteps} 步：每轮 {perRoundDamage} 血（11 跳 × 8 + 两刀 × 20），末读数 {Vitality(stage, target, vit)}");
    }

    /// <summary>
    /// <b>S29 长时段：同条件重跑逐字一致</b>（ticket 07 验收第 3 条之二）。
    /// 同样种子、同样那三个参数、同样那串注入与推进 ⇒ 两发快照逐字等。
    /// </summary>
    [Fact]
    public void S29_长时段_同条件重跑逐字一致()
    {
        static string Run()
        {
            var stage = StageChecklistShell.NewLongRunStage();
            for (var round = 0; round < 20; round++)
            {
                stage.Inject(AbilityVerbs.ApplyEffect, $"{StageChecklistShell.TargetId}|{StageChecklistShell.Fx.WoundWound}");
                stage.Inject(AbilityVerbs.Cast,
                    $"{StageChecklistShell.CasterId}|{StageChecklistShell.Skill.Windup}|{StageChecklistShell.TargetId}");
                stage.Advance(60);
            }

            return stage.DebugSnapshot();
        }

        var first = Run();
        var second = Run();
        second.ShouldBe(first, "1200 步一局同条件重跑 ⇒ 逐字一致");
        first.ShouldContain($"tick={StageChecklistShell.LongRunSteps}", Case.Sensitive, "两份都跑满了长时段那一份的步数");
    }

    // === 场子之间的关系与那条缺口 ===

    /// <summary>
    /// <b>S30 两个场子共用同一套注册口</b>（ticket 07 验收第 5 条）：
    /// 同一份 <see cref="SkillStageSpec"/> 实例，<b>两个场子都能起</b>，
    /// 而且跑出来的那份读数逐字相同——注册口是唯一那一份，场子之间不各写一份。
    /// 长时段那一档更狠：它连类都还是第一个场子（<see cref="SkillStage"/>），只是参数拧了三格。
    /// </summary>
    [Fact]
    public void S30_两个场子共用同一套注册口()
    {
        var spec = SkillStageSample.SpecOpenField();

        var close = new SkillStage(spec);
        var open = new SkillStageOpenField(spec);
        close.Inject(AbilityVerbs.ApplyEffect,
            $"{SkillStageSample.OpenField.Front}|{SkillStageSample.Fx.StrikeHit}|-|{SkillStageSample.CasterId}");
        open.Inject(AbilityVerbs.ApplyEffect,
            $"{SkillStageSample.OpenField.Front}|{SkillStageSample.Fx.StrikeHit}|-|{SkillStageSample.CasterId}");
        close.Advance(5);
        open.Advance(5);

        open.DebugSnapshot().ShouldBe(close.DebugSnapshot(), "同一份注册口 + 同一条写路径 ⇒ 两个场子跑出同一份状态（注册口只有一份）");
        StageChecklistShell.NewLongRunStage().ShouldBeOfType<SkillStage>("长时段并进第一个场子：连类都不用换，只换三格参数");
    }

    /// <summary>
    /// <b>S31 能力覆盖缺口在常驻文档里写明了</b>（ticket 07 验收第 4 条）。
    /// 另两个场子不跑全清单，缺的那几格只有各自的用例兜着——这件事写在
    /// <c>docs/GugaFramework/技能试验场.md</c> 里，钉成一条用例就是「别让它悄悄消失」：
    /// 有人日后把那一节删掉或改名，红的是这一条。
    /// </summary>
    [Fact]
    public void S31_另两个场子不跑全清单_缺口在常驻文档里写明了()
    {
        var doc = FindRepoFile(Path.Combine("docs", "GugaFramework", "技能试验场.md"));
        var text = File.ReadAllText(doc);

        // 用 Contains 判而不是 Does.Contain：这一份文档很长，红的时候把整篇打出来没有用。
        foreach (var (what, needle) in new[]
                 {
                     ("那一节的标题", "## 另两个场子"),
                     ("缺口的标题", "能力覆盖缺口"),
                     ("缺口的成因", "16 条清单只在第一个场子跑全"),
                 })
        {
            text.Contains(needle, StringComparison.Ordinal).ShouldBeTrue(
                $"常驻文档 {doc} 里没有{what}（找 \"{needle}\"）—— ticket 07 验收第 4 条要的就是它，别让它悄悄消失");
        }
    }

    /// <summary>从测试输出目录往上找仓库里的那一份文件（不写死层数：测试输出目录的深度会变）。</summary>
    private static string FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"从 {AppContext.BaseDirectory} 往上找不到 {relative}");
    }
}
