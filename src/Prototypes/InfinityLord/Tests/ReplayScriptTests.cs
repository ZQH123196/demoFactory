#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using DemoFactory.GugaFramework.Replay;
using DemoFactory.Prototypes.InfinityLord.Logic;
using Shouldly;
using Xunit;

namespace InfinityLord.Tests;

/// <summary>
/// 重放脚本 — 「第几步做了什么」的记录 + 改代码后的批量回归
/// （2026-09-22 操控台设计 6 节 第 5 步；2026-09-23 落 json + 收编 GugaFramework）。
///
/// 三件事（本文件是原型的接线层，通用层在 src/GugaFramework/Replay/）：
/// 1. <see cref="ReplayAssembly"/> 装配 LogicWorld — 在**逻辑写路径**录 {tick, cmd, args, ok}，
///    不在 UI 输入录；相机/表现层严禁进重放脚本（红线）。
///    词表在 <see cref="ReplaySetup"/>（Scripts/Logic/，本原型唯一一份），录制与重放共用它。
///    脚本落成 json（<see cref="ReplayScriptJson"/>，录制/重放共用一份口径），存放 Tests/Scripts/。
/// 2. 录制测试：录制 → 落盘 → 读回 → 重放，全链路一遍跑通（兼当序列化回归）。
/// 3. 重放测试：**只读已落盘的 json** 执行 — 这就是「改代码后的批量回归」的日常形态；
///    新脚本由 AI 经 MCP 试玩录制（Main.cs 的 ReplayRecord 直达口）或现场手写补进来。
///
/// 断言强度（已确认）：只比检查点快照（状态级）；事件流逐字相等不做。
/// 与哨兵测试的关系：哨兵证明「同种子重跑 = 同结果」（重放可信的前提，DeterminismSentinelTests）；
/// 本文件把「重跑」换成数据驱动的脚本 — 画面无关、毫秒级、不出窗口。
/// </summary>
public class ReplayScriptTests
{
    /// <summary>脚本存放目录（json 落盘在这，跟测试一起进版本库 = 回归资产）。</summary>
    private static string ScriptDir => Path.Combine(AppContext.BaseDirectory,
        "..", "..", "..", "Scripts");

    /// <summary>当前脚本文件名 — 版本号随剧本演进，改剧本时改名（旧脚本保留 = 旧回归资产）。</summary>
    private const string ScriptFile = "replay-v1-sentinel.json";

    /// <summary>录制测试 — 录一条完整脚本、落盘、读回、重放全链路（兼当 json 序列化回归）。</summary>
    [Fact]
    public void 录制落盘读回重放_全链路逐字复现()
    {
        var script = RecordScript(seed: 42, finalTick: 1000);

        // 落盘 → 读回 → 断言口径一致（序列化互逆）
        Directory.CreateDirectory(ScriptDir);
        var file = Path.Combine(ScriptDir, ScriptFile);
        File.WriteAllText(file, ReplayScriptJson.Write(script));
        var loaded = ReplayScriptJson.Read(File.ReadAllText(file));

        loaded.Seed.ShouldBe(script.Seed);
        loaded.Entries.Count.ShouldBe(script.Entries.Count);
        loaded.Checkpoints.Count.ShouldBe(script.Checkpoints.Count);

        // 重放的是**读回的脚本** — 序列化往返后仍逐字复现，才是"落盘可信"
        Replay(loaded);
    }

    /// <summary>重放测试 — 只读已落盘的 json 执行（改代码后的批量回归入口）。</summary>
    [Fact]
    public void 已落盘脚本重放_检查点逐字复现()
    {
        var file = Path.Combine(ScriptDir, ScriptFile);
        File.Exists(file).ShouldBeTrue($"脚本 {ScriptFile} 不存在 — 先跑「录制落盘读回重放_全链路逐字复现」或经 MCP 试玩录制");
        Replay(ReplayScriptJson.Read(File.ReadAllText(file)));
    }

    /// <summary>
    /// 终局剧本重放（已落盘的 replay-fullgame.json）— 批量回归入口。
    /// </summary>
    [Fact]
    public void 终局剧本已落盘重放_快照逐字复现()
    {
        var file = Path.Combine(ScriptDir, "replay-fullgame.json");
        File.Exists(file).ShouldBeTrue("终局脚本不存在 — 先跑「终局剧本录制落盘读回重放」");
        Replay(ReplayScriptJson.Read(File.ReadAllText(file)));
    }

    /// <summary>
    /// MCP 试玩录制的冒烟脚本（Tests/Scripts/replay-mcp-smoke.json）— 快照级重放。
    /// 录制起点是运行中的世界（startTick&gt;0），重放端先对齐起点再注入：种子确定性 ⇒
    /// 同种子跑到同一步、状态逐字一致（哨兵已证），故检查点快照必须逐字相等 ——
    /// 这就是「MCP 试玩录制的脚本与测试内录制同级可用」的机器证明。
    /// </summary>
    [Fact]
    public void MCP试玩冒烟脚本_起点对齐后快照逐字复现()
    {
        var file = Path.Combine(ScriptDir, "replay-mcp-smoke.json");
        File.Exists(file).ShouldBeTrue("MCP 冒烟脚本不存在 — 经 MCP 试玩录制补录");
        var script = ReplayScriptJson.Read(File.ReadAllText(file));

        script.Seed.ShouldBe(42);
        script.Entries.Count.ShouldBeGreaterThan(0, "冒烟脚本应有命令记录");
        script.Checkpoints.Count.ShouldBeGreaterThan(0, "冒烟脚本应有检查点");
        script.StartTick.ShouldBeGreaterThan(0, "MCP 试玩录制从运行中世界开始，startTick 应 > 0");

        Replay(script);
    }

    /// <summary>
    /// 终局剧本 — **测试内录制整局**（拆冻结后重录，替代已删的 MCP 整局脚本）：
    /// 录制 → 落盘 replay-fullgame.json → 读回重放，全链路一遍（兼序列化回归）。
    /// 覆盖：起点排布（堡垒化）→ 玩家动词全表（挂赏/低赏/营建/升级）→ 快进到 BOSS 降临
    /// → 挂赏 BOSS → 推进到终局判定 → 判定后残余演化检查点（拆冻结新语义）。
    /// 「开录即玩整局」的生产正道是测试内录制：MCP 逐幕 eval 录制有幕间实时漂移
    /// （两次 eval 之间世界还在跑，时间轴不进录）+ 判负时机竞速（就绪握手期世界实时演化，
    /// 零干预判负 ~960 步（约 96 秒）就到）；MCP 真机录制只做顺手沉淀（smoke 脚本已机器证明可用）。
    /// </summary>
    [Fact]
    public void 终局剧本录制落盘读回重放_快照逐字复现()
    {
        var script = RecordEndgameScript(seed: 42);

        Directory.CreateDirectory(ScriptDir);
        var file = Path.Combine(ScriptDir, "replay-fullgame.json");
        File.WriteAllText(file, ReplayScriptJson.Write(script));
        var loaded = ReplayScriptJson.Read(File.ReadAllText(file));

        loaded.Seed.ShouldBe(script.Seed);
        loaded.Entries.Count.ShouldBe(script.Entries.Count);
        loaded.Checkpoints.Count.ShouldBe(script.Checkpoints.Count);

        Replay(loaded);
    }

    /// <summary>
    /// 终局剧本（录制端）— 走到终局判定与判定后的残余演化（拆冻结：判定后世界照常跑）。
    /// 写路径一律 <c>rec.Inject(词表名, args)</c>：与重放端同一份词表，所以
    /// 「录出来的脚本必然重放得动」是结构上的事实，不是靠人肉核对维持的。
    /// </summary>
    private static ReplayRunner.ReplayScript RecordEndgameScript(int seed)
    {
        var w = new LogicWorld(randomSeed: seed);
        using var rec = ReplaySetup.Recorder(w, seed);

        rec.Inject("HealBuilding", ReplaySetup.Args("palace", 200_000)); // 起点排布：堡垒化（进录的排布口）
        rec.Mark("t-start-fortified");

        ReplaySetup.AdvanceTo(w, 200);
        rec.Inject("PlaceBountyFlag", ReplaySetup.Args("lair1", 25));
        ReplaySetup.AdvanceTo(w, 226);
        rec.Mark("bounty-early");

        rec.Inject("DebugSetTreasury", ReplaySetup.Args(600));
        rec.Inject("PlaceBuilding", ReplaySetup.Args(BuildingKind.GuildWarrior, new Vector2(-45, 38)));
        ReplaySetup.AdvanceTo(w, 626);
        rec.Inject("HealBuilding", ReplaySetup.Args("palace", 50_000)); // 补一次堡垒化（波次 3:00 前提）
        var warriorGuildId = w.Buildings
            .Where(b => b.Kind == BuildingKind.GuildWarrior)
            .Select(b => b.Id).OrderBy(id => id, StringComparer.Ordinal).FirstOrDefault();
        if (warriorGuildId != null) rec.Inject("UpgradeGuild", warriorGuildId);
        rec.Mark("guild-up");

        ReplaySetup.AdvanceTo(w, 3620);
        rec.Mark("boss-arrived");

        var bossId = w.Buildings
            .Where(b => b.Kind == BuildingKind.BossLair)
            .Select(b => b.Id).OrderBy(id => id, StringComparer.Ordinal).FirstOrDefault();
        if (bossId != null) rec.Inject("PlaceBountyFlag", ReplaySetup.Args(bossId, 300));
        rec.Mark("boss-bounty");

        ReplaySetup.AdvanceTo(w, 4000);
        rec.Mark("t-final"); // 判定（若已发生）落进快照；拆冻结后残余演化继续可断言

        return new ReplayRunner.ReplayScript(seed, rec.Journal, rec.Checkpoints);
    }

    // ================= 录制端 =================

    /// <summary>
    /// 录一条完整脚本：世界 + <see cref="ReplayAssembly"/>（写路径记录）+ 检查点（状态级）。
    /// 剧本与哨兵同构（刻意覆盖挂赏/加价/营建/升级/拒单路径 + 暂停冻结），
    /// 保证第一条脚本就命中逻辑层每条主要写路径。
    /// </summary>
    private static ReplayRunner.ReplayScript RecordScript(int seed, int finalTick)
    {
        var w = new LogicWorld(randomSeed: seed);
        using var rec = ReplaySetup.Recorder(w, seed);

        // 剧本指令流（与 DeterminismSentinelTests.Script 相同的序列）
        ReplaySetup.AdvanceTo(w, 100);
        rec.Mark("t100");

        rec.Inject("PlaceBountyFlag", ReplaySetup.Args("lair1", 20));
        ReplaySetup.AdvanceTo(w, 150);
        rec.Mark("t150");

        // 挂赏第一只活怪（按 Id 序数取首 — 不依赖遍历顺序）
        var mobId = w.Units.Where(u => u.Side == Faction.Monster && u.Health > 0)
            .Select(u => u.Id).OrderBy(id => id, StringComparer.Ordinal).FirstOrDefault();
        if (mobId != null) rec.Inject("PlaceBountyFlag", ReplaySetup.Args(mobId, 30));

        ReplaySetup.AdvanceTo(w, 250);
        rec.Mark("t250");

        // 营建两座（远离种子建筑 ≥ MinPlaceDistance）+ 一条拒单路径（贴着行会放 → 过近拦截）
        rec.Inject("DebugSetTreasury", ReplaySetup.Args(600));
        rec.Inject("PlaceBuilding", ReplaySetup.Args(BuildingKind.Tavern, new Vector2(40, -40)));
        rec.Inject("PlaceBuilding", ReplaySetup.Args(BuildingKind.GuildWarrior, new Vector2(-30, 30)));
        // 拒单样本：贴着行会放市集 —— ok=false 也入录（拒绝也是可观察行为）
        rec.Inject("PlaceBuilding", ReplaySetup.Args(BuildingKind.Market, new Vector2(5, 5)));
        ReplaySetup.AdvanceTo(w, 350);
        rec.Mark("t350");

        // 升级刚建的战士公会 + 给第一面旗加价
        rec.Inject("DebugSetTreasury", ReplaySetup.Args(600));
        rec.Inject("UpgradeGuild", "guildwarrior1");
        var flagId = w.Flags.Select(f => f.Id).OrderBy(id => id, StringComparer.Ordinal).FirstOrDefault();
        if (flagId != null) rec.Inject("RaiseBountyFlag", ReplaySetup.Args(flagId, 50));

        // 暂停冻结段：冻结中推进 50 步必须空转，恢复后续跑
        w.SetPaused(true);
        ReplaySetup.AdvanceTo(w, w.Tick + 50);
        w.SetPaused(false);

        ReplaySetup.AdvanceTo(w, 700);
        rec.Mark("t700");
        ReplaySetup.AdvanceTo(w, finalTick);
        rec.Mark("t-final");

        return new ReplayRunner.ReplayScript(seed, rec.Journal, rec.Checkpoints);
    }

    // ================= 重放端（接线 GugaFramework 通用层）=================

    /// <summary>
    /// 重放一条脚本 — 通用执行骨架在 <see cref="ReplayRunner"/>（src/GugaFramework/Replay/），
    /// 原型侧三钩子（词表 / 快照 / 推进）在 <see cref="ReplaySetup"/> —— 本原型唯一一份。
    /// 录制与重放读的是同一个 <see cref="ReplaySetup.Hooks"/> 工厂，级 1 与级 2 天然同源。
    /// </summary>
    private static void Replay(ReplayRunner.ReplayScript script)
    {
        using var world = new LogicWorld(randomSeed: script.Seed);
        ReplayRunner.Replay(script, ReplaySetup.Hooks(world), msg =>
        {
            if (msg != null) Assert.Fail(msg);
        });
    }

    /// <summary>首处分歧定位 — 快照不等时给出首个差异字符的下标与两侧上下文（可观测性第一）。</summary>
    private static string DiffHint(string expected, string actual)
    {
        var n = Math.Min(expected.Length, actual.Length);
        var i = 0;
        while (i < n && expected[i] == actual[i]) i++;
        var from = Math.Max(0, i - 50);
        var take = 160;
        return $" 首处差异 @字符{i}（录制长{expected.Length}/重放长{actual.Length}）"
             + $" 录制侧…\"{expected.Substring(from, Math.Min(take, expected.Length - from))}\""
             + $" 重放侧…\"{actual.Substring(from, Math.Min(take, actual.Length - from))}\"";
    }
}
