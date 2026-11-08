#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DemoFactory.GugaFramework.Replay;
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// MVP 那一侧的重放三层（<b>断言三层</b>：L1 不变量 / L2 语义 / L3 逐字）。
///
/// <para><b>每一发命令都走 <c>inject</c> 动词</b>（「写路径全录」），每个检查点都
/// <c>Mark</c> 在同一步的命令<b>之前</b>（「先快照后命令」）—— 断言的是检查点快照
/// （「断言比快照」），不比事件流。</para>
///
/// <para><b>三档分工照抄契约</b>：L1 不变量不进脚本头部，由测试资产承担 —— 这里是每个检查点
/// 抓完快照顺手判一次 <see cref="TownAccounts.IsConserved"/>；L2 结构一致（头部步号 /
/// 实体段数 / 每段段首身份），键集与同键值漂移容忍；L3 逐字。</para>
///
/// <para><b>更要紧的是判得出</b>：下面每一格都拿一份改坏的剧本去证「这一档该不该红」——
/// 一条永远绿的断言和不存在的断言是一回事。改坏的五种手法全是纯文本改动：只动检查点快照里的
/// 字符串，不动世界、不动时间线、不动序号。</para>
/// </summary>
public class ReplayTests
{
    private const int Seed = 7;
    private const string SegSep = " | ";

    /// <summary>
    /// 录一局：停靠 → 空艇交易 → 办赛 → 三个检查点。每条命令都走词表写路径。
    /// L1 就在这一格里判：每个检查点抓完快照顺手判一次八条守恒等式。
    /// </summary>
    private static ReplayAssembly RecordRun(out ArenaWorld world)
    {
        world = ArenaWorldBuilder.New(Seed).Build();
        var rec = ArenaSetup.Recorder(world);
        rec.BeginSession();

        MarkAndCheckL1(rec, world, "开局");

        world.AdvanceTo(200);
        MarkAndCheckL1(rec, world, "航行中");
        rec.Inject("Dock", BigMapGrid.PoorCity.Id);

        // 开局在贫城西边 40 米的野地上：40 米 = 1000 步，留余量推到 1500 步才停稳。
        const long DockedTick = 1500;
        world.AdvanceTo(DockedTick);
        world.IsDocked.ShouldBeTrue("停到贫城上，剧本里才有一笔可断言的成交。");

        MarkAndCheckL1(rec, world, "停稳");
        rec.Inject("TradeAirship", "30|1|0");
        rec.Inject("StartMatch", "g1,g2|g3");
        world.AdvanceTo(DockedTick + 1000);   // 办赛 1.5 游戏小时 = 900 步，留 100 步余量

        MarkAndCheckL1(rec, world, "赛毕");
        world.AdvanceTo(DockedTick + 2000);
        MarkAndCheckL1(rec, world, "两小时后");
        return rec;
    }

    private static void MarkAndCheckL1(ReplayAssembly rec, ArenaWorld world, string label)
    {
        rec.Mark(label);
        world.Town.IsConserved().ShouldBeTrue(
            $"L1 不变量：检查点「{label}」那一格八条等式仍然要平：" + world.Town.ConservedReport());
    }

    private static LiveReplaySession Replay(ReplayRunner.ReplayScript script)
    {
        var world = ArenaWorldBuilder.New(script.Seed).Build();
        var session = new LiveReplaySession(script, ArenaSetup.Hooks(world));
        session.RunAll();
        return session;
    }

    private static string Report(LiveReplaySession session) => string.Join("\n", session.Divergences);

    [Fact]
    public void 三档断言_L3逐字L2语义全绿且命令ok全对()
    {
        var rec = RecordRun(out _);
        var script = rec.ToScript();
        script.Entries.Count.ShouldBe(3, "剧本里那三发都是动词写路径（Dock / TradeAirship / StartMatch）。");
        script.Checkpoints.Count.ShouldBe(5);

        var literal = Replay(script);
        literal.Divergences.ShouldBeEmpty("L3 逐字要复现录制时的每一格：\n" + Report(literal));
        literal.Commands.ShouldAllBe(c => c.Equal,
            "每一发注入命令的 ok 都照录制复现（ok 翻转不走快照路径，任何档位都红）");
        literal.Checkpoints.Count.ShouldBe(script.Checkpoints.Count);
        literal.Checkpoints.ShouldAllBe(c => c.Equal, "检查点快照逐字相等");

        var semantic = Replay(rec.ToScript(ReplayRunner.AssertLevel.Semantic));
        semantic.Divergences.ShouldBeEmpty("L2 语义全绿：\n" + Report(semantic));
    }

    [Fact]
    public void L2判红_头部步号漂移段数漂移段首身份漂移()
    {
        foreach (var drift in new (string Label, Func<ReplayRunner.ReplayScript, ReplayRunner.ReplayScript> Doctor)[]
                 {
                     ("头部步号", Doctored.HeadTick),
                     ("实体段数", Doctored.SegmentCount),
                     ("段首身份", Doctored.SegmentHead),
                 })
        {
            var rec = RecordRun(out _);
            Replay(rec.ToScript(ReplayRunner.AssertLevel.Semantic)).Divergences.ShouldBeEmpty(
                $"原剧本在 L2 档全绿 —— 红的来自「{drift.Label}漂移」那一处改坏。");

            var doctored = drift.Doctor(rec.ToScript(ReplayRunner.AssertLevel.Semantic));
            Doctored.Snapshot(doctored, "赛毕").ShouldNotBe(Doctored.Snapshot(rec.ToScript(), "赛毕"),
                $"改坏「{drift.Label}漂移」这一处本身要生效，否则下面那条红是假的。");

            var session = Replay(doctored);
            session.Divergences.ShouldNotBeEmpty($"L2 判「{drift.Label}漂移」为红（结构变化）：\n" + Report(session));
        }
    }

    [Fact]
    public void L2容忍_同键值漂移与键集漂移_但L3判红()
    {
        foreach (var drift in new (string Label, Func<ReplayRunner.ReplayScript, ReplayRunner.ReplayScript> Doctor)[]
                 {
                     ("同键值漂移", Doctored.Value),
                     ("键集漂移", Doctored.KeySet),
                 })
        {
            var rec = RecordRun(out _);

            var semantic = Replay(drift.Doctor(rec.ToScript(ReplayRunner.AssertLevel.Semantic)));
            semantic.Divergences.ShouldBeEmpty(
                $"L2 容忍「{drift.Label}」（良性改动不逼全脚本全红）：\n" + Report(semantic));

            var literal = Replay(drift.Doctor(rec.ToScript(ReplayRunner.AssertLevel.Literal)));
            literal.Divergences.ShouldNotBeEmpty($"L3 逐字档对「{drift.Label}」判红。");
        }
    }

    [Fact]
    public void 命令ok翻转_三档都判红()
    {
        foreach (var level in new[]
                 {
                     ReplayRunner.AssertLevel.Literal,
                     ReplayRunner.AssertLevel.Semantic,
                     ReplayRunner.AssertLevel.Invariants,
                 })
        {
            var rec = RecordRun(out _);
            var script = rec.ToScript(level);
            var first = script.Entries[0];
            var flipped = first with { Ok = !first.Ok };
            var doctored = script with
            {
                Entries = script.Entries.Select((e, i) => i == 0 ? flipped : e).ToList(),
            };

            var session = Replay(doctored);
            session.Divergences.ShouldNotBeEmpty($"{level} 档对命令 ok 翻转判红（不走快照路径，任何档位都红）。");
            session.Commands[0].Equal.ShouldBeFalse("被改的那一发 ok 对不上。");
            session.Commands.Skip(1).ShouldAllBe(c => c.Equal, "其余各发照旧。");
        }
    }

    [Fact]
    public void 漏录一笔写路径_重放立刻分叉()
    {
        // 「写路径全录」的反面验收：剧本里掐掉办赛那一笔，重放端就没有那一场与那一份门票。
        var world = ArenaWorldBuilder.New(Seed).Build();
        var rec = ArenaSetup.Recorder(world);
        rec.BeginSession();
        world.AdvanceTo(200);
        rec.Inject("Dock", BigMapGrid.PoorCity.Id);
        world.AdvanceTo(1500);   // 开局在野地上：40 米 = 1000 步
        rec.Inject("StartMatch", "g1,g2|g3");
        world.AdvanceTo(2500);   // 办赛 900 步 + 余量
        rec.Mark("赛毕");

        var script = rec.ToScript();
        var skipped = script with
        {
            Entries = script.Entries.Where(e => e.Cmd != "StartMatch").ToList(),
        };

        var session = Replay(skipped);
        session.Divergences.ShouldNotBeEmpty("漏录一笔写路径，重放端与录制端就该分叉：\n" + Report(session));
        session.Checkpoints.ShouldAllBe(c => !c.Equal,
            "终点那一格对不上 —— 否则测的是「没差别」，那条红是假的。");
    }

    [Fact]
    public void 剧本落盘能被公共层读回来_重放仍逐字相等()
    {
        var rec = RecordRun(out _);
        var json = ReplayScriptJson.Write(rec.ToScript());
        var back = ReplayScriptJson.Read(json);

        back.Seed.ShouldBe(rec.ToScript().Seed);
        back.Entries.Count.ShouldBe(rec.ToScript().Entries.Count);
        back.Checkpoints.Count.ShouldBe(rec.ToScript().Checkpoints.Count);
        var session = Replay(back);
        session.Divergences.ShouldBeEmpty("落盘再读回来重放仍应逐字相等：\n" + Report(session));
    }

    /// <summary>
    /// 改坏一份剧本的五种手法。全是纯文本改动：只动检查点快照里的字符串，
    /// 世界、时间线与序号一格不动。
    /// </summary>
    private static class Doctored
    {
        /// <summary>取某个检查点的快照全文（标签取不到就抛 —— 取不到说明剧本没录上）。</summary>
        public static string Snapshot(ReplayRunner.ReplayScript s, string label)
            => s.Checkpoints.First(c => c.Label == label).Snapshot;

        /// <summary>头部步号漂移：第 0 段段首 <c>tick=N</c> 加一。</summary>
        public static ReplayRunner.ReplayScript HeadTick(ReplayRunner.ReplayScript s)
            => MapFirst(s, snap =>
            {
                var segs = Segs(snap);
                var tokens = Tokens(segs[0]).ToList();
                var at = tokens[0].IndexOf("tick=", StringComparison.Ordinal);
                var tick = long.Parse(tokens[0][(at + 5)..], CultureInfo.InvariantCulture);
                tokens[0] = "tick=" + (tick + 1).ToString(CultureInfo.InvariantCulture);
                segs[0] = string.Join(' ', tokens);
                return Join(segs);
            });

        /// <summary>实体段数漂移：掐掉建筑段里的一整段。</summary>
        public static ReplayRunner.ReplayScript SegmentCount(ReplayRunner.ReplayScript s)
            => MapFirst(s, snap =>
            {
                var segs = Segs(snap);
                segs.RemoveAt(BuildingSegmentIndex(snap));
                return Join(segs);
            });

        /// <summary>段首身份漂移：建筑段的第 2 个 token（建筑 id）改掉。</summary>
        public static ReplayRunner.ReplayScript SegmentHead(ReplayRunner.ReplayScript s)
            => MapFirst(s, snap =>
            {
                var segs = Segs(snap);
                var i = BuildingSegmentIndex(snap);
                var tokens = Tokens(segs[i]).ToList();
                tokens[1] = tokens[1] + "_doctored";
                segs[i] = string.Join(' ', tokens);
                return Join(segs);
            });

        /// <summary>同键值漂移：建筑段第 3 个 token 的值改掉（段首身份与段数都不变）。</summary>
        public static ReplayRunner.ReplayScript Value(ReplayRunner.ReplayScript s)
            => MapFirst(s, snap =>
            {
                var segs = Segs(snap);
                var i = BuildingSegmentIndex(snap);
                var tokens = Tokens(segs[i]).ToList();
                tokens[2] = "doctored=" + tokens[2].Split('=').Last();
                segs[i] = string.Join(' ', tokens);
                return Join(segs);
            });

        /// <summary>键集漂移：给建筑段末尾加一个新字段（段首身份与段数都不变）。</summary>
        public static ReplayRunner.ReplayScript KeySet(ReplayRunner.ReplayScript s)
            => MapFirst(s, snap =>
            {
                var segs = Segs(snap);
                var i = BuildingSegmentIndex(snap);
                segs[i] = segs[i] + " doctoredField=1";
                return Join(segs);
            });

        private static ReplayRunner.ReplayScript MapFirst(
            ReplayRunner.ReplayScript s, Func<string, string> patch)
            => s with { Checkpoints = s.Checkpoints.Select(c => c with { Snapshot = patch(c.Snapshot) }).ToList() };

        private static int BuildingSegmentIndex(string snapshot)
        {
            var segs = Segs(snapshot);
            for (var i = 1; i < segs.Count; i++)
            {
                if (Tokens(segs[i]).FirstOrDefault() == "B") return i;
            }

            throw new InvalidOperationException("这份快照里没有建筑段，改坏手法用不了");
        }

        private static List<string> Segs(string snapshot)
            => snapshot.Split(SegSep, StringSplitOptions.None).ToList();

        private static string Join(IEnumerable<string> segs) => string.Join(SegSep, segs);

        private static IEnumerable<string> Tokens(string seg)
            => seg.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
    }
}
