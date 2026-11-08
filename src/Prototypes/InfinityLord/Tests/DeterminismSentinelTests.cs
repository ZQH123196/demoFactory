#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using DemoFactory.Prototypes.InfinityLord.Logic;
using Shouldly;
using Xunit;

namespace InfinityLord.Tests;

/// <summary>
/// 确定性哨兵测试 — 重放脚本可信的前提（2026-09-22 操控台设计 6 节 第 1 步）。
///
/// 哨兵语义：同种子 + 同一操作剧本跑两遍，状态快照与事件流必须**逐字相等**。
/// 这三个测试守住的边界：
/// 1. 同种子两遍 — 逻辑层没有隐藏的随机源 / 时间源 / 迭代顺序依赖；
/// 2. 逐步 vs 大块快进 — 推进分块方式（= 调试时的速度档/黑屏快进）不影响结果，
///    即"变速只是推进节奏，时间轴分毫不差"的机器可执行版本；
/// 3. 暂停冻结段 — 冻结期间 Advance 空转、恢复后续跑与"从没暂停过"逐字一致。
///
/// 红线（本文件就是它的报警器）：逻辑层 Dictionary/HashSet 迭代顺序、任何
/// DateTime/Random.Shared/环境依赖，一旦引入，这里先红。
/// 快照取全精度浮点（"R" 往返格式）— 坐标精度丢在这里就是假绿。
/// </summary>
public class DeterminismSentinelTests
{
    private const int Seed = 42;

    /// <summary>剧本终局步号（覆盖三波剧本波次的第 1 波 + 暂停段，一局 6 分钟的 1/6）。</summary>
    private const int FinalTick = 1000;

    [Fact]
    public void 同种子跑两遍_状态与事件流逐字相等()
    {
        var (cpsA, eventsA) = SentinelRun.Run(Seed, chunkTicks: 250, pauseFreeze: true);
        var (cpsB, eventsB) = SentinelRun.Run(Seed, chunkTicks: 250, pauseFreeze: true);

        SentinelRun.AssertSame(cpsA, cpsB, "状态快照（同种子两遍）");
        SentinelRun.AssertSame(eventsA, eventsB, "事件流（同种子两遍）");
    }

    [Fact]
    public void 逐步推进与大块快进_结果逐字相等()
    {
        // 1x（调试慢挡 — 一步一步走）vs 大块（黑屏快进 — 一发跳 250 步）
        var (cpsStep, eventsStep) = SentinelRun.Run(Seed, chunkTicks: 1, pauseFreeze: true);
        var (cpsBulk, eventsBulk) = SentinelRun.Run(Seed, chunkTicks: 250, pauseFreeze: true);

        SentinelRun.AssertSame(cpsStep, cpsBulk, "状态快照（逐步 vs 大块）");
        SentinelRun.AssertSame(eventsStep, eventsBulk, "事件流（逐步 vs 大块）");
    }

    [Fact]
    public void 暂停冻结不改写世界_恢复后与从没暂停过逐字相等()
    {
        // 同一剧本，唯一差异：中段冻结 50 步（5 秒）再解除 — 终局必须与"从没暂停过"一致
        var (cpsPlain, eventsPlain) = SentinelRun.Run(Seed, chunkTicks: 250, pauseFreeze: false);
        var (cpsFrozen, eventsFrozen) = SentinelRun.Run(Seed, chunkTicks: 250, pauseFreeze: true);

        SentinelRun.AssertSame(cpsPlain, SentinelRun.WithoutFreezeProbe(cpsFrozen), "状态快照（无暂停 vs 暂停过）");
        SentinelRun.AssertSame(eventsPlain, eventsFrozen, "事件流（没暂停 vs 暂停过）");
    }

    /// <summary>
    /// 哨兵剧本 runner — 一次完整运行（世界 + 事件收集 + 快照序列）。
    /// 剧本刻意覆盖全部玩家动词（挂赏/加价/营建/升级）+ 拒单路径 + 暂停冻结，
    /// 命中逻辑层每条主要写路径；金额先排满金库，避免与造价常量耦合。
    /// </summary>
    private static class SentinelRun
    {
        public static (List<string> Checkpoints, List<string> Events) Run(
            int seed, int chunkTicks, bool pauseFreeze)
        {
            using var world = new LogicWorld(randomSeed: seed);
            var rec = new Recorder(world);
            Script(world, rec, chunkTicks, pauseFreeze);
            return (rec.Checkpoints, rec.Events);
        }

        /// <summary>哨兵剧本 — 两条运行必须走完全相同的指令序列。</summary>
        private static void Script(LogicWorld world, Recorder rec, int chunkTicks, bool pauseFreeze)
        {
            // t=100：三条产出链（行会 20 步/名、两巢穴按 LairSpawnIntervalTicks 一只）都已起步
            rec.AdvanceTo(world, 100, chunkTicks);
            rec.Checkpoint(world, "t100");

            rec.Cmd("PlaceBountyFlag(lair1,20)", world.PlaceBountyFlag("lair1", 20));
            rec.AdvanceTo(world, 150, chunkTicks);
            rec.Checkpoint(world, "t150");

            // 挂赏第一只活怪（按 Id 序数排序取首 — 不依赖任何遍历顺序）
            var mobId = world.Units.Where(u => u.Side == Faction.Monster && u.Health > 0)
                .Select(u => u.Id).OrderBy(id => id, StringComparer.Ordinal).FirstOrDefault();
            rec.Cmd($"PlaceBountyFlag({mobId},30)", mobId is null ? false : world.PlaceBountyFlag(mobId, 30));

            rec.AdvanceTo(world, 250, chunkTicks);
            rec.Checkpoint(world, "t250");

            // 营建两座（远离种子建筑 ≥ MinPlaceDistance）+ 一条拒单路径（贴着行会放 → 过近拦截）
            world.DebugSetTreasury(600);
            rec.Cmd("PlaceBuilding(Tavern,(40,-40))", world.PlaceBuilding(BuildingKind.Tavern, new Vector2(40, -40)));
            rec.Cmd("PlaceBuilding(GuildWarrior,(-30,30))", world.PlaceBuilding(BuildingKind.GuildWarrior, new Vector2(-30, 30)));
            rec.Cmd("PlaceBuilding(Market,(5,5))", world.PlaceBuilding(BuildingKind.Market, new Vector2(5, 5)));

            rec.AdvanceTo(world, 350, chunkTicks);
            rec.Checkpoint(world, "t350");

            // 升级刚建的战士公会（Id 规则：玩家行会从 1 号起）+ 给第一面旗加价
            world.DebugSetTreasury(600);
            rec.Cmd("UpgradeGuild(guildwarrior1)", world.UpgradeGuild("guildwarrior1"));
            var flagId = world.Flags.Select(f => f.Id).OrderBy(id => id, StringComparer.Ordinal).FirstOrDefault();
            rec.Cmd($"RaiseBountyFlag({flagId},50)", flagId is null ? false : world.RaiseBountyFlag(flagId, 50));

            // 暂停冻结段：冻结中推进 50 步必须空转（步号不动），恢复后续跑
            if (pauseFreeze)
            {
                world.SetPaused(true);
                rec.AdvanceTo(world, 400, chunkTicks);
                world.SetPaused(false);
                rec.Checkpoint(world, "t-freeze-probe", tag: FreezeTag);
            }

            rec.AdvanceTo(world, 700, chunkTicks);
            rec.Checkpoint(world, "t700");
            rec.AdvanceTo(world, FinalTick, chunkTicks);
            rec.Checkpoint(world, "t1000-final");
        }

        private const string FreezeTag = "freeze-probe";

        /// <summary>剔除冻结探针快照 — 暂停不变性测试对比时用（其余快照两 run 一致）。</summary>
        public static List<string> WithoutFreezeProbe(List<string> checkpoints)
            => checkpoints.Where(cp => !cp.Contains(FreezeTag)).ToList();

        /// <summary>逐行断言相等；不等时报出首处差异的下标与两行内容。</summary>
        public static void AssertSame(List<string> expected, List<string> actual, string what)
        {
            for (var i = 0; i < Math.Max(expected.Count, actual.Count); i++)
            {
                var a = i < expected.Count ? expected[i] : "<越界>";
                var b = i < actual.Count ? actual[i] : "<越界>";
                b.ShouldBe(a, $"{what} 第 {i} 行开始分歧（此前 {Math.Min(i, expected.Count)} 行逐字一致）");
            }
        }

        /// <summary>
        /// 事件与快照收集器 — 全部事件单流按发生顺序记一行；快照全精度序列化。
        /// </summary>
        private sealed class Recorder
        {
            public List<string> Checkpoints { get; } = [];
            public List<string> Events { get; } = [];

            public Recorder(LogicWorld world)
            {
                world.UnitDied += r => Events.Add($"UnitDied {r}");
                world.CombatEvent += r => Events.Add($"Combat {r}");
                world.BountyEvent += r => Events.Add($"Bounty {r}");
                world.EconomyEvent += r => Events.Add($"Economy {r}");
                world.BuildingDestroyed += b => Events.Add($"BuildingDestroyed {Describe(b)}");
                world.BuildingRespawned += b => Events.Add($"BuildingRespawned {Describe(b)}");
                world.BuildingPlaced += b => Events.Add($"BuildingPlaced {Describe(b)}");
                world.GameOver += o => Events.Add($"GameOver {o}");
                world.WaveStarted += w => Events.Add($"Wave {w}");
                world.BossArrived += b => Events.Add($"BossArrived {Describe(b)}");
            }

            /// <summary>推进到目标步 —— 转发原型唯一那份推进器（chunkTicks 供「逐步 vs 大块」对照）。</summary>
            public void AdvanceTo(LogicWorld world, long targetTick, int chunkTicks)
                => ReplaySetup.AdvanceTo(world, targetTick, chunkTicks);

            /// <summary>命令结果也入流 — 拒单/成功都是可观察行为，两 run 必须一致。</summary>
            public void Cmd(string label, bool ok) => Events.Add($"Cmd {label} -> {ok}");

            public void Checkpoint(LogicWorld world, string label, string? tag = null)
                => Checkpoints.Add($"{label}|{tag ?? ""}|{Capture(world)}");

            private static string Describe(Building b)
                => $"{b.Id} {b.Kind} {b.Owner} lv={b.Level} hp={b.Health} pos=({F(b.Position.X)},{F(b.Position.Y)})";

            /// <summary>全量状态一行 — 排序保证顺序确定，浮点 "R" 全精度防往返漂移。</summary>
            private static string Capture(LogicWorld w)
            {
                var sb = new StringBuilder();
                sb.Append($"tick={w.Tick} treasury={w.Treasury} net={w.LastCycleNet} outcome={w.Outcome}");
                foreach (var b in w.Buildings.OrderBy(b => b.Id, StringComparer.Ordinal))
                {
                    sb.Append($" | B {Describe(b)}");
                }
                foreach (var u in w.Units.OrderBy(u => u.Id, StringComparer.Ordinal))
                {
                    var move = u.MoveTarget is { } m ? $"{F(m.X)},{F(m.Y)}" : "-";
                    sb.Append(
                        $" | U {u.Id} \"{u.Name}\" {u.Side} {u.Class} home={u.HomeGuildId ?? "-"}" +
                        $" hp={u.Health}/{u.MaxHealth} atk={u.Attack} cd={u.CooldownRemaining}" +
                        $" pos=({F(u.Position.X)},{F(u.Position.Y)}) move=({move})" +
                        $" gold={u.Gold} xp={u.Xp} lv={u.Level} gear={u.Gear} morale={u.Morale}" +
                        $" noIncome={u.NoIncomeTicks} tavernStarve={u.TavernStarveTicks}" +
                        $" target={u.TargetUnitId ?? "-"} claim={u.ClaimedFlagId ?? "-"} lastGold={u.LastObservedGold}");
                }
                foreach (var f in w.Flags.OrderBy(f => f.Id, StringComparer.Ordinal))
                {
                    sb.Append($" | F {f.Id} -> {f.TargetId} gold={f.Gold} claim={f.ClaimedByHeroId ?? "-"} settled={f.Settled}");
                }
                return sb.ToString();
            }

            private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
