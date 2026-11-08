#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DemoFactory.GugaFramework.Abilities;
using DemoFactory.GugaFramework.Replay;
using DemoFactory.Prototypes.BattleArray.Logic;
using Shouldly;
using Xunit;

namespace BattleArray.Tests;

/// <summary>
/// 技能层 H3（排兵布阵真 1v1 接一条突斩）与级 1 重放 —— D1 的直达那一半、
/// D3 的「每发都进账」在这一层先立住，D8 的级 2 对照用的就是这里录下来的那份剧本。
///
/// 台账全是手算的（抖动关、单挑一列、两个兵），红了要能一口说出差在哪一步：
/// - 全关档：<c>42 首刀 / 162 分胜负 / 剑士剩 53 血</c>，与 <see cref="BattleMechanicsTests"/> 同一本账；
/// - 突斩在第 43 步落 20 血（满血时那一刀不放大）⇒ 蛮牛只需 12 刀平A ⇒ 第 152 步分胜负，
///   蛮牛来得及出手的是 45/59/…/143 共 8 刀 = 104 伤 ⇒ 剑士剩 66 血。
/// </summary>
public class AbilityH3DuelTests
{
    private static readonly string ScriptDir =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Scripts");

    private static string ScriptPath(string name) => Path.GetFullPath(Path.Combine(ScriptDir, name));

    // === 全关档：接了技能层之后，世界那一半还得逐字是原来那局 ===

    [Fact]
    public void 全关档_既有台账一字不动()
    {
        using var world = DuelRiftSetup.NewWorld(withRift: false);
        world.Advance(200);

        world.OutcomeTick.ShouldBe(162, "13 刀 × 10 步间隔 ＋ 第 42 步首刀");
        var warrior = world.Units.Single(u => u.Side == Side.Player);
        warrior.Health.ShouldBe(53);
        warrior.AttacksMade.ShouldBe(13);
        world.DebugSnapshot().ShouldNotContain("[技能层]", Case.Sensitive, "全关档连那一栏都不念");
    }

    [Fact]
    public void 突斩改得动这一局_伤害按施法者已损失生命放大()
    {
        using var world = DuelRiftSetup.NewWorld();
        // 第 40 步按下（前摇 3 步 ⇒ 第 43 步过 commit 落那一刀）；此时剑士满血，放大系数为 1。
        world.Advance(39);
        DuelRiftSetup.Inject(world, "CastAbility", "p01|warrior.rift|e01").ShouldBeTrue();
        world.AdvanceUntilEnd();

        world.OutcomeTick.ShouldBe(152, "蛮牛 260−20＝240 血，12 刀平A，末刀在第 152 步");
        var warrior = world.Units.Single(u => u.Side == Side.Player);
        warrior.Health.ShouldBe(66, "只挨了 45/59/…/143 这 8 刀，每刀 13");
        warrior.AttacksMade.ShouldBe(12, "平A 那本账不数技能那一刀");

        var abilities = world.Abilities!;
        abilities.Snapshot().ShouldContain("c;damage_dealt;20", Case.Sensitive, "技能造成的伤害记在层自己的账上");
        abilities.EventsWhere<DamageDealt>().Any(d => d.Source == "p01").ShouldBeTrue("那一刀是谁打的，形状 damage（DamageDealt）的 Source 字段读得出（旧文本 from=p01）");
    }

    [Fact]
    public void 残血那一刀更狠_放大系数走施法者已损失生命()
    {
        using var world = DuelRiftSetup.NewWorld();
        // 先让两边各打两轮（剑士 42/52，蛮牛 45/59）⇒ 第 59 步剑士 144 血 ⇒ 此刻按下、第 62/63 步落刀时
        // 已损失 26/170 ⇒ 那一刀 = 20 × (1 + 26/170 × 1.5) = 24.588… → 25（比满血那一刀的 20 狠）。
        world.Advance(59);
        var caster = world.Find("p01")!;
        var tank = world.Find("e01")!;
        var hurt = caster.Health;
        DuelRiftSetup.Inject(world, "CastAbility", "p01|warrior.rift|e01").ShouldBeTrue();
        world.Advance(6);

        var expected = (int)Math.Round(20 * (1 + (1 - hurt / 170.0) * 1.5), MidpointRounding.AwayFromZero);
        var melee = caster.AttacksMade * caster.Card.Attack;   // 平A 那本账数得清，剩下的一格才是技能那一刀
        tank.Health.ShouldBe(260 - melee - expected, "残血那一刀比满血那一刀狠");
        world.Abilities!.Snapshot().ShouldContain($"c;damage_dealt;{expected}", Case.Sensitive, "层自己的账上就是这一发");
    }

    [Fact]
    public void 冷却中再按被拒_拒绝有名字()
    {
        using var world = DuelRiftSetup.NewWorld();
        world.Advance(40);
        DuelRiftSetup.Inject(world, "CastAbility", "p01|warrior.rift|e01").ShouldBeTrue();
        // 冷却 20 步（2 秒）：第 43 步过 commit ⇒ 冷却到第 63 步。第 50 步再按必被拒，
        // 而「为什么没放」念得出来（Busy 与 Cooldown 是两种拒绝，同一步内连按只能得到前者）。
        world.Advance(10);
        DuelRiftSetup.Inject(world, "CastAbility", "p01|warrior.rift|e01").ShouldBeFalse();
        world.Abilities!.Snapshot().ShouldContain("Cooldown", Case.Sensitive, "拒的理由要有名字，进快照");
        world.Abilities!.EventsWhere<AbilityRejectedAndQueued>()
                .Any(r => r.Reason == AbilityRejectReason.Cooldown).ShouldBeTrue("拒绝的原因写在形状 reject 的 Reason 字段里（旧文本 reason=Cooldown 的 queued=1 那一档 = AbilityRejectedAndQueued）");
        world.DebugSnapshot().ShouldContain("[技能层]", Case.Sensitive, "接了技能这一栏就念得出（与全关档的唯一差别）");
    }

    /// <summary>
    /// N-16 在 H3 的真演化账（本轮新挂的那一格消费者）：冷却中那一发按下不丢，第 63 步补放、
    /// 第 66 步落第二刀 —— 于是这一局<b>早 10 步</b>结束。手算：
    /// 第 40 步按下 → 第 43 步落 20（满血不放大）、冷却到第 63 步；第 50 步再按 → 拒 + 排队；
    /// 第 63 步补放 → 第 66 步落 25（那时剑士 144 血，20×(1+26/170×1.5)=24.588…）；
    /// 蛮牛剩 260−45=215 血 → 剑士第 11 次平A（第 142 步）打死；蛮牛只来得及出手 45/59/…/129 共 7 刀 = 91
    /// ⇒ 剑士剩 79 血。
    /// </summary>
    [Fact]
    public void 排队补放在真演化里改得动胜负()
    {
        using var world = DuelRiftSetup.NewWorld();
        world.Advance(39);
        DuelRiftSetup.Inject(world, "CastAbility", "p01|warrior.rift|e01").ShouldBeTrue();
        world.Advance(10);
        DuelRiftSetup.Inject(world, "CastAbility", "p01|warrior.rift|e01").ShouldBeFalse("冷却中必被拒");
        world.Abilities!.Snapshot().ShouldContain("q=1", Case.Sensitive, "排上了这件事在快照里读得出");
        world.AdvanceUntilEnd();

        world.OutcomeTick.ShouldBe(142, "两刀 45 血 ⇒ 第 11 次平A 结束这一局（全关档是 162）");
        var warrior = world.Units.Single(u => u.Side == Side.Player);
        warrior.Health.ShouldBe(79, "只挨了 7 刀，每刀 13");
        warrior.AttacksMade.ShouldBe(11, "平A 那本账照样只数平A");
        world.Abilities!.Snapshot().ShouldContain("c;damage_dealt;45", Case.Sensitive, "两刀都在层的账上（20 + 25）");
    }

    // === D1 直达那一半：一步一跳到第 k 步，与一口气跳到第 k 步，读数逐字相同 ===

    [Fact]
    public void 直达k步与逐步推到k步_快照逐字等()
    {
        const int target = 97;   // 一个既过了首刀、又没打完的步号，读到的东西才够全
        using var jumped = DuelRiftSetup.NewWorld();
        jumped.AdvanceDebugSteps(target);

        using var stepped = DuelRiftSetup.NewWorld();
        for (var i = 0; i < target; i++) stepped.AdvanceDebugSteps(1);

        stepped.Tick.ShouldBe(jumped.Tick);
        stepped.DebugSnapshot().ShouldBe(jumped.DebugSnapshot(), "直达与单步是同一份演化");
        stepped.ReplaySnapshot().ShouldBe(jumped.ReplaySnapshot());
    }

    [Fact]
    public void 暂停中单步与直达照走_读数一样()
    {
        using var world = DuelRiftSetup.NewWorld();
        world.SetPaused(true);
        world.Advance(5).ShouldBeFalse("暂停冻时间轴：普通 Advance 该空转（这是正确行为）");
        world.AdvanceDebugSteps(5).ShouldBe(5, "调试直达口在暂停中照走（暂停只冻「自己走」）");
        world.IsPaused.ShouldBeTrue("直达口不许偷偷把暂停解掉");
    }

    // === 级 1 重放：录一份剧本 → 落盘 → 重放逐字等 → 指纹两跑逐字等 ===

    [Fact]
    public void 录一份突斩剧本_级1重放检查点逐字等()
    {
        var script = RecordRiftScript();

        using var replayWorld = DuelRiftSetup.NewWorld(seed: script.Seed);
        var session = new LiveReplaySession(script, DuelRiftSetup.Hooks(replayWorld));
        session.RunAll();

        session.Divergences.ShouldBeEmpty("级 1 重放必须逐字复现录制时的每一格：\n" + string.Join("\n", session.Divergences));
        session.Checkpoints.Count.ShouldBe(script.Checkpoints.Count);
        session.Commands.Count.ShouldBe(script.Entries.Count);
        session.Commands.ShouldAllBe(_e => _e.Equal == true, "每一发注入命令的 ok 都照录制复现（D3 要的就是这个形状）");

        // 剧本落盘给 CLI 与级 2 用：三份消费者吃同一份 json，才对得上「逐字相等」这句话
        var path = ScriptPath("duel-rift-h3.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, ReplayScriptJson.Write(script));
        File.ReadAllText(path).ShouldContain("CastAbility");
    }

    [Fact]
    public void 级1指纹两跑逐字等_并落一份给级2对照()
    {
        var first = DumpOf(out var sequence);
        var second = DumpOf(out _);
        second.ShouldBe(first, "同一份剧本两跑，逐格指纹必须逐字相同");

        var path = ScriptPath("duel-rift-h3.level1.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // 行尾写死 "\n"，不用 Environment.NewLine：后者在 Windows 上每次跑完都给这份基线留一条
        // 纯 CRLF 的假 `M`（内容逐字相同、git status 却说改过），每轮都要人工辨一次（用户确认：改）。
        // 代价照实记一次：这份文件的行尾整体翻了一遍，内容一个字没动。
        File.WriteAllText(path, first + "\n" + sequence);
        Console.WriteLine($"级 1 指纹（{first.Split('\n').Length} 格）已落盘：{path}");
    }

    /// <summary>跑一遍剧本并取出级 1 的逐格指纹与检查点全文（D8 对照的左半边）。</summary>
    private static string DumpOf(out string detail)
    {
        var script = RecordRiftScript();
        using var world = DuelRiftSetup.NewWorld(seed: script.Seed);
        var session = new LiveReplaySession(script, DuelRiftSetup.Hooks(world));
        session.RunAll();
        session.Divergences.ShouldBeEmpty(string.Join("\n", session.Divergences));
        detail = string.Join("\n", session.Checkpoints.Select(c =>
            $"--- {c.Label} @tick={c.Tick} ---\n{c.Actual}"));
        return session.DumpSequence();
    }

    /// <summary>
    /// 录制端（时序按「先快照后命令」：到那一步 → <b>先抓快照 → 后发命令</b>，命令发出即录）。
    /// 剧本里那几步是照着上面那本手算台账挑的：按下前 / 落刀那一格 / 冷却中被拒 / 分胜负 / 打完再读一次。
    /// </summary>
    private static ReplayRunner.ReplayScript RecordRiftScript()
    {
        var entries = new List<ReplayRunner.JournalEntry>();
        var marks = new List<ReplayRunner.Checkpoint>();
        var seq = 0;

        using var world = DuelRiftSetup.NewWorld();

        void Mark(long atTick, string label)
        {
            DuelRiftSetup.AdvanceTo(world, atTick);
            marks.Add(new ReplayRunner.Checkpoint(atTick, label, DuelRiftSetup.SnapshotOf(world), seq++));
        }

        void Command(long atTick, string cmd, string args)
        {
            DuelRiftSetup.AdvanceTo(world, atTick);
            var ok = DuelRiftSetup.Inject(world, cmd, args);
            entries.Add(new ReplayRunner.JournalEntry(atTick, cmd, args, ok, seq++));
        }

        Mark(0, "开局");
        Mark(40, "按下突斩之前");
        Command(40, "CastAbility", "p01|warrior.rift|e01");
        Mark(43, "落刀那一格");
        Command(50, "CastAbility", "p01|warrior.rift|e01");   // 冷却中：这一发必须是 ok=false，重放也得是
        Mark(60, "冷却里再按被拒之后");
        Mark(152, "分胜负那一格");
        Mark(160, "打完再读一次");

        return new ReplayRunner.ReplayScript(
            DuelRiftSetup.DefaultSeed, entries, marks, StartTick: 0, StartSnapshot: null);
    }
}
