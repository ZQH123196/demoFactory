#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DemoFactory.Prototypes.InfinityLord.Logic;
using Shouldly;
using Xunit;
using DemoFactory.GugaFramework.Tests;

namespace InfinityLord.Tests;

/// <summary>
/// 接管表的**写作用域**测试 —— 扩到多名英雄听树之前必须先钉死的一层。
///
/// 要防的是交接账上那条「原计划风险 3」：真要全量换脑时，如果接管一名英雄会把**别人**的字段
/// 写脏，事后就从一堆红字段里归不出因。
///
/// 前四条把命题按它能成立的样子来验：<b>接管表这个数据结构只按 Id 记账</b>，让位/写入/撤下
/// 都碰不到别人 —— 零推进或只推 0.1 秒，故意不与世界的演化纠缠。
/// 第五条走另一个口径（跑两棵同种子世界、每 0.1 秒比一次快照），量出来的结论是反的：
/// 那条账在全量快照口径下**不成立**，而且不成立的原因不是脏写。细节见该条注释。
/// </summary>
public class BrainTakeoverIsolationTests : LogicWorldTestBase
{
    /// <summary>用到的英雄数（一名接管、一名并存、一名没挂桥作对照）。</summary>
    private const int HeroesNeeded = 3;

    /// <summary>推进到场上够 <see cref="HeroesNeeded"/> 名英雄，返回存活英雄（首名英雄在第 2.1 秒落场，之后陆续有）。</summary>
    private List<Unit> AdvanceUntilEnoughHeroes()
    {
        for (var guard = 0; Heroes().Count < HeroesNeeded && guard < 400; guard++) Advance(1);
        Heroes().Count.ShouldBeGreaterThanOrEqualTo(HeroesNeeded, $"推到 {(double)World.Tick / 10:F1} 秒仍凑不满 {HeroesNeeded} 名英雄");
        return Heroes();
    }

    /// <summary>快照按单位 Id 拆表（丢掉头行的步号/treasury —— 那是全局量，不属于任何一名英雄）。</summary>
    private static Dictionary<string, string> SnapshotByUnit(LogicWorld world)
    {
        var table = new Dictionary<string, string>();
        foreach (var line in world.DebugSnapshot().Split('\n').Skip(1))
        {
            table[line.Split(' ')[0]] = line;
        }

        return table;
    }

    /// <summary>除 <paramref name="exceptId"/> 外，两侧快照逐字相同的差异清单（空 = 一个字段都没写脏）。</summary>
    private static List<string> DirtyUnits(Dictionary<string, string> before, Dictionary<string, string> after, string exceptId)
        => after
            .Where(kv => kv.Key != exceptId && (!before.TryGetValue(kv.Key, out var old) || old != kv.Value))
            .Select(kv => $"{kv.Key}\n  前 {before.GetValueOrDefault(kv.Key, "(不在场)")}\n  后 {kv.Value}")
            .ToList();

    [Fact]
    public void 让位这个动作本身_只改这名英雄的意图字段_其余单位逐字不变()
    {
        var heroes = AdvanceUntilEnoughHeroes();
        var taken = heroes[0];
        var before = SnapshotByUnit(World);

        World.DelegateHeroToBrain(taken.Id);
        var after = SnapshotByUnit(World);

        var dirty = DirtyUnits(before, after, taken.Id);
        dirty.ShouldBeEmpty($"让位一名英雄就写脏了 {dirty.Count} 个单位：\n{string.Join("\n", dirty)}");
        // Is.EquivalentTo = 键集合相等（不看顺序）→ 两边排序后逐字比
        after.Keys.OrderBy(_k => _k).ShouldBe(before.Keys.OrderBy(_k => _k),
            "让位不该让谁从快照里消失或冒出来");
        after[taken.Id].ShouldContain("intent=树:", Case.Sensitive, "让位后快照要立刻显出「树接管」，否则观测口说谎");
    }

    [Fact]
    public void 意图口按Id收口_没让位的英雄和怪物和假Id_一律写不进去()
    {
        var heroes = AdvanceUntilEnoughHeroes();
        var taken = heroes[0];
        var bystander = heroes[1];
        var monster = Monsters().First();
        World.DelegateHeroToBrain(taken.Id);
        var before = SnapshotByUnit(World);

        World.ReportBrainIntent(bystander.Id, (int)BrainIntent.Engage, monster.Id);
        World.ReportBrainIntent(monster.Id, (int)BrainIntent.Wander, string.Empty);
        World.ReportBrainIntent("hero_不存在", (int)BrainIntent.Flee, monster.Id);

        World.BrainIntentOf(bystander.Id).ShouldBe(-1, "未登记的英雄被写入意图 = 接管表串号");
        World.BrainIntentOf(monster.Id).ShouldBe(-1, "怪物根本不该进得了接管表");
        World.BrainIntentOf("hero_不存在").ShouldBe(-1);

        var dirty = DirtyUnits(before, SnapshotByUnit(World), taken.Id);
        dirty.ShouldBeEmpty($"对未登记 Id 的写入不该动到任何人，实际动了：\n{string.Join("\n", dirty)}");
    }

    [Fact]
    public void 两座桥并存_各写各的意图_谁都不串到谁身上()
    {
        var heroes = AdvanceUntilEnoughHeroes();
        var first = heroes[0];
        var second = heroes[1];
        var bridgeless = heroes[2];
        var monster = Monsters().First();

        World.DelegateHeroToBrain(first.Id);
        World.DelegateHeroToBrain(second.Id);
        World.ReportBrainIntent(first.Id, (int)BrainIntent.Engage, monster.Id);
        World.ReportBrainIntent(second.Id, (int)BrainIntent.Hold, string.Empty);

        World.BrainIntentOf(first.Id).ShouldBe((int)BrainIntent.Engage);
        World.BrainIntentOf(second.Id).ShouldBe((int)BrainIntent.Hold, "后挂的桥把先写的意图覆盖了 = 没按人记账");
        World.BrainIntentOf(bridgeless.Id).ShouldBe(-1, "没挂桥的英雄不该出现在接管表里");

        Advance(1);

        var byUnit = SnapshotByUnit(World);
        byUnit[first.Id].ShouldContain("intent=树:交战");
        byUnit[second.Id].ShouldContain("intent=树:站着");
    }

    [Fact]
    public void 撤下一座桥_只交还那名英雄_另一座桥还在表上()
    {
        var heroes = AdvanceUntilEnoughHeroes();
        var leaving = heroes[0];
        var staying = heroes[1];
        World.DelegateHeroToBrain(leaving.Id);
        World.DelegateHeroToBrain(staying.Id);
        World.ReportBrainIntent(staying.Id, (int)BrainIntent.Wander, string.Empty);

        World.ReleaseHeroFromBrain(leaving.Id);
        Advance(1);

        var byUnit = SnapshotByUnit(World);
        World.BrainIntentOf(leaving.Id).ShouldBe(-1, "撤桥后 Id 该从接管表里出去");
        World.BrainIntentOf(staying.Id).ShouldBe((int)BrainIntent.Wander, "撤一座桥不该把别的桥一起摘掉");
        byUnit[staying.Id].ShouldContain("intent=树:游荡");
        byUnit[leaving.Id].ShouldNotContain("intent=树:", Case.Sensitive, "已交还的英雄要回到 Majesty 决策链的读数");
    }

    /// <summary>
    /// 「让位前后其余英雄字段逐字不变」这句账，实测**在世界层面不成立** —— 而且与接管表无关，
    /// 成因是全世界共用一条 <c>Random _rng</c>（LogicWorld 字段声明与构造处）：一名英雄的行为一变，
    /// 他抽走/省下的随机数就改了后面所有人的抽签。实测（种子 42，游戏时钟走到 4.0 秒时让位）：
    /// 0.1 秒后别人的<b>落点</b>就分叉；把 <c>pos=</c> 抹掉再比，
    /// 到 10.3 秒别人的<b>血量</b>也分叉 —— 伤害有浮动，打的怪也不是同一批。
    ///
    /// 所以这条钉的不是「没写脏」（那是上面四条写路径断言管的），而是钉住一句反面结论：
    /// <b>全量快照 diff 不能当「接管表有没有写脏」的归因工具</b> —— 它一上来就全红，红的东西里
    /// 绝大多数是正常的耦合。真要一个可比的窗口，得让随机数按单位分流水（设计决定，需用户裁决）。
    /// 报告全文落 <c>%TEMP%\brain-takeover-window.txt</c>。
    /// </summary>
    [Fact]
    public void 让位后其余英雄字段逐字对齐的窗口_量出来并钉住()
    {
        var measurement = MeasureHandoverWindow(seed: 42, steps: 1200, handoverAt: 40);
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "brain-takeover-window.txt"),
            measurement.Report, new UTF8Encoding(false));
        TestLog.Ledger(measurement.Report);

        measurement.HeroWindowWithPosition.ShouldBe(60, "含随机落点的比对：实测接管后 2.0 秒分叉（接管当步 = 40）。逐字重解时是接管当步就分叉；这一格量的是「落点还受不受接管影响」，成因是全世界共用一条 Random（见本用例上面的说明）—— 命令当步生效还是下一步生效都不改这个数（2026-10-04 改执行次序前后实测都是 60）");
        measurement.HeroWindowExceptPosition.ShouldBeGreaterThan(measurement.HandoverTick);
        measurement.HeroWindowExceptPosition.ShouldBeLessThan(measurement.Steps, "把落点抹掉，血量之类的字段也撑不了全程 —— 分叉照旧。要看具体数字与成因，读 %TEMP%\\brain-takeover-window.txt");
    }

    private readonly record struct WindowMeasurement(
        int HeroWindowWithPosition, int HeroWindowExceptPosition, int HandoverTick, int Steps, string Report);

    /// <summary>两颗同种子世界锁步推进（每 0.1 秒一步）；从 <paramref name="handoverAt"/> 步起只在接管侧把首名英雄交给树、之后每步写一次 Hold。</summary>
    private static WindowMeasurement MeasureHandoverWindow(int seed, int steps, int handoverAt)
    {
        using var control = new LogicWorld(randomSeed: seed);
        using var brain = new LogicWorld(randomSeed: seed);
        var report = new StringBuilder();
        var withPosition = steps;
        var exceptPosition = steps;
        string? firstPositionDiff = null;
        string? firstFieldDiff = null;
        var takenId = string.Empty;

        for (var tick = 1; tick <= steps; tick++)
        {
            if (tick == handoverAt)
            {
                takenId = TakeoverTarget(brain);
                brain.DelegateHeroToBrain(takenId);
            }

            if (takenId.Length > 0)
            {
                brain.ReportBrainIntent(takenId, (int)BrainIntent.Hold, string.Empty);
            }

            control.Advance(1);
            brain.Advance(1);

            // 只比英雄：怪物的血量差是「他站住不打架」的下游后果（实测第 8.1 秒 mob_1 少挨 8 点血），
            // 混进来会把「接管表有没有写脏别人」这个命题糊掉。
            var a = HeroLines(control);
            var b = HeroLines(brain);
            if (firstPositionDiff is null)
            {
                var diff = DirtyUnits(a, b, takenId).Concat(DirtyUnits(b, a, takenId)).ToList();
                if (diff.Count > 0)
                {
                    withPosition = tick - 1;
                    firstPositionDiff = string.Join("\n", diff);
                }
            }

            var strippedA = a.ToDictionary(kv => kv.Key, kv => WithoutPosition(kv.Value));
            var strippedB = b.ToDictionary(kv => kv.Key, kv => WithoutPosition(kv.Value));
            if (firstFieldDiff is null)
            {
                var diff = DirtyUnits(strippedA, strippedB, takenId).Concat(DirtyUnits(strippedB, strippedA, takenId)).ToList();
                if (diff.Count > 0)
                {
                    exceptPosition = tick - 1;
                    firstFieldDiff = string.Join("\n", diff);
                }
            }
        }

        report.AppendLine($"种子 {seed}，游戏时钟走到 {At(handoverAt)}把首名英雄 {takenId} 交给树" +
                          $"（之后每 0.1 秒只写一次 Hold），共推到 {At(steps)}，比对只看其余英雄：");
        report.AppendLine("· 全字段（含随机落点）逐字对齐到 " + At(withPosition) +
                          (firstPositionDiff is null ? "，全程无差异。" : $"，从 {At(withPosition + 1)}起分叉：\n{firstPositionDiff}"));
        report.AppendLine("· 去掉落点后其余英雄字段逐字对齐到 " + At(exceptPosition) +
                          (firstFieldDiff is null ? "，全程无差异 —— 接管表没往别人身上写过一个字。" : $"，从 {At(exceptPosition + 1)}起分叉：\n{firstFieldDiff}"));
        return new WindowMeasurement(withPosition, exceptPosition, handoverAt, steps, report.ToString());
    }

    /// <summary>游戏时钟口径：一步 = 0.1 秒，报数一律报秒。</summary>
    private static string At(int steps) => $"{steps / 10.0:F1} 秒";

    /// <summary>快照里只留英雄（谁的阵营算英雄，直接问世界，不在测试里猜 Id 前缀）。</summary>
    private static Dictionary<string, string> HeroLines(LogicWorld world)
    {
        var heroes = world.Units.Where(u => u.Side == Faction.Hero).Select(u => u.Id).ToHashSet();
        return SnapshotByUnit(world).Where(kv => heroes.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    /// <summary>抹掉随机落点口径（<c>pos=(x,y)</c>）—— 它由全世界共用的那条 RNG 决定，不属于「谁写脏了谁」。</summary>
    private static string WithoutPosition(string snapshotLine)
        => System.Text.RegularExpressions.Regex.Replace(snapshotLine, @"pos=\([^)]*\)", "pos=(略)");

    /// <summary>接管侧的首名存活英雄 Id；没人可接管返回空串。</summary>
    private static string TakeoverTarget(LogicWorld world)
        => world.Units.FirstOrDefault(u => u.Side == Faction.Hero && u.Health > 0)?.Id ?? string.Empty;
}
