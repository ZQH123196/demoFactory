#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DemoFactory.GugaFramework.Replay;
using DemoFactory.GugaFramework.Tests;
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 「进阶成功也进 Journal 并重放一致」那一格 —— ticket 05 留下的缺口。
///
/// <para><b>缺口是什么</b>：位阶那套（ticket 04）的测试全是<b>直写账本</b>攒资源
/// （<c>world.Town.ProduceSoulStone / ProduceSpecialMaterial / ProduceCoin</c>），
/// 那些写口不进 Journal。于是「进阶成功」这一发从来没进过任何一份 Journal，
/// 重放端也复现不出来（重放端凑不出同样的数，命令 ok 会翻转 → 任何档位都红）。</para>
///
/// <para><b>这一格怎么补</b>：整轮操作只走<b>词表动词</b>（<c>rec.Inject</c>），
/// 二阶那三笔代价全部由世界自己产出：魂石靠拔锚防守里倒下的尸体自动入账、
/// 特殊材料靠高危格采矿产出经电梯进镇仓、金币靠空艇成交与游客消费。
/// 账本那一侧<b>一个直写口都不碰</b>（那是本测试要补的洞本身）。</para>
///
/// <para><b>形态解锁那一格是夹具，不是写路径</b>：荒漠态要累计 240 游戏分钟、
/// 暴食态要 400 累计喂食且已驶离贫城，这两样在现役经济里都跑不满
/// （荒漠要 4 个游戏小时；暴食那 400 食物里至少 200 件得靠买食凑）。
/// 所以这两处用 <c>BeastVoyage</c> 上的调试直写口摆到阈值，<b>录制端与重放端用同一份夹具</b>
/// （<see cref="ApplyFormFixture"/>），谁都不多不少 —— 解锁标记在世界那侧照常翻，
/// 切形态那一发照常进 Journal、照常逐字复现。</para>
///
/// <para><b>时间一律读游戏时间</b>（<see cref="ArenaTime"/>），不写死「走 X 米用 Y 秒」
/// 这类旧距离口径：两城之间的航程在本轮改过尺子，而游戏时间账一字未动。</para>
///
/// <para><b>判据要能自证</b>：末尾拿一份改坏头部步号的剧本去证「这一档真会红」——
/// 一条永远绿的断言和一条不存在的断言是一回事。</para>
/// </summary>
public class BeastTierJournalTests
{
    private const int Seed = 20261015;
    private const string SegSep = " | ";

    /// <summary>停靠那一段等游客上街的时长（游戏小时）。</summary>
    private const int DwellGameHours = 4;

    /// <summary>一场仗打完后多等一会儿的余量（游戏小时）—— 仗自己收场，余量只防边界。</summary>
    private const int FightMarginGameHours = 1;

    /// <summary>采矿那一段的时长（游戏小时）：三矿工 × 每 2 游戏小时 1 矿石、每 4 游戏小时 1 特殊材料。</summary>
    private const int MineGameHours = 24;

    /// <summary>沙城那一段让世界自己记的累计计时（游戏分钟）。</summary>
    private const int DesertDwellGameMinutes = 600;

    /// <summary>
    /// 世界夹具：形态解锁两处直写口 + 1 单位魔晶，<b>录制端与重放端用同一份</b>。
    ///
    /// <para>解锁标记在世界那侧照常翻（<c>CheckFormUnlocks</c> 每一步判一次），
    /// 所以切形态那一发是真的走成了，不是塞进账本的假动作。</para>
    ///
    /// <para><b>魔晶为什么也放在夹具里</b>：本票只验「嵌入这一发进 Journal 并逐字复现」，
    /// 所以两边各走产出写口摆 1 单位在仓里；嵌入本身仍走 <c>EmbedCrystal</c> 动词，不绕过写路径。</para>
    /// </summary>
    private static void ApplyFormFixture(ArenaWorld world)
    {
        // 暴食态的第二道门是「已驶离贫城」：夹具把局流程推到第一段航行（贫城防守赢了），
        // 再立刻停回贫城（后面那一轮要从贫城开局）。录制端与重放端用同一份。
        world.LeftPoorCity();
        world.OnDocked(BigMapGrid.PoorCity);

        world.Voyage.SetDwellGameMinutes(ArenaTuning.DesertFormDwellGameMinutes);
        world.Voyage.AddFedTotalFood(ArenaTuning.GluttonFormFedTotalFood);
        world.Town.ProduceCrystal(1.0);
    }

    /// <summary>走词表写路径发一发，失败就把原因原样带出来。</summary>
    private static void Fire(ReplayAssembly rec, string cmd, string args)
    {
        var ok = rec.Inject(cmd, args, out var reason);
        ok.ShouldBeTrue($"「{cmd}|{args}」这一发要走成（{reason}）");
    }

    /// <summary>账本一行读数（判红时带出来，省得再跑一遍探针）。</summary>
    private static string Ledger(ArenaWorld world)
    {
        var t = world.Town;
        return $"tick={world.Tick} coin={t.Coin:0.##} food={t.Food:0.##} soul={t.SoulStone:0.##} " +
               $"ore={t.Ore:0.##} mat={t.SpecialMaterial:0.##} debt={t.TotalDebt:0.##} " +
               $"visitors={world.Visitors.InTownCount} units={world.Roster.Units.Count} " +
               $"battle={world.Battle.DebugText()}";
    }

    /// <summary>推进若干个游戏小时（时间口径走 <see cref="ArenaTime"/>，不写死步数）。</summary>
    private static void AdvanceGameHours(ArenaWorld world, int hours)
        => world.Advance(ArenaTime.StepsPerGameHour * hours);

    /// <summary>抓一个检查点，顺手判一次 L1 不变量（九条守恒等式）。</summary>
    private static void Mark(ReplayAssembly rec, ArenaWorld world, string label)
    {
        rec.Mark(label);
        world.Town.IsConserved().ShouldBeTrue(
            $"L1 不变量：检查点「{label}」那一格九条等式仍然要平：" + world.Town.ConservedReport());
    }

    /// <summary>
    /// 等巨兽停稳在指定城市（停靠期那一格是 <c>DockedCityId</c>，不是航程）。
    ///
    /// <para>上限用游戏小时记（一个航段十来个游戏小时），不写死「多少米」——
    /// 两城之间的尺子改过，游戏时间账没改。</para>
    /// </summary>
    private static void SailTo(ArenaWorld world, string cityId)
    {
        var cap = ArenaTime.StepsPerGameHour * 24;
        for (var i = 0; i < cap; i++)
        {
            world.Advance(1);
            if (world.DockedCityId == cityId) return;
        }

        world.DockedCityId.ShouldBe(cityId, $"巨兽没停稳在 {cityId}（还在 {world.DockedCityId}）。");
    }

    /// <summary>推进到这一场仗收场（场合回到 <see cref="BattleOccasion.None"/>）。</summary>
    private static void FightToTheEnd(ArenaWorld world)
    {
        var cap = ArenaTime.StepsPerGameHour * 8;
        for (var i = 0; i < cap; i++)
        {
            world.Advance(1);
            if (world.Battle.Occasion == BattleOccasion.None) return;
        }

        world.Battle.Occasion.ShouldBe(BattleOccasion.None, "这一场仗没收场。");
    }

    /// <summary>
    /// 录一整轮：停靠 → 攒钱 → 两场防守凑魂石 → 挖开第二层 → 采矿攒特殊材料 →
    /// 进阶仪式 → 两次切形态。每一发写路径都走词表，账本一个直写口都不碰。
    /// </summary>
    private static ReplayAssembly RecordRun(out ArenaWorld world)
    {
        world = ArenaWorldBuilder.New(Seed).Build();
        ApplyFormFixture(world);
        var rec = ArenaSetup.Recorder(world);
        rec.BeginSession();

        Mark(rec, world, "开局");

        // ① 停靠贫城（写路径一：Dock）。
        Fire(rec, "Dock", BigMapGrid.PoorCity.Id);
        SailTo(world, BigMapGrid.PoorCity.Id);
        world.DockedCityId.ShouldBe(BigMapGrid.PoorCity.Id, "停稳在贫城上。");
        Mark(rec, world, "停稳贫城");

        // ② 空艇成交：把开局的矿石换成金币（材料留着付进阶代价，那 2 件不卖）。
        Fire(rec, "TradeAirship", "0|10|0");

        // ③ 定价抬到两倍：游客在饭店酒店的消费按两倍算。
        Fire(rec, "SetPricing", "2|2|2");
        AdvanceGameHours(world, DwellGameHours);
        Mark(rec, world, "游客消费毕");

        // ④ 办赛一场：门票进账（观众 = 在镇游客）。
        BattleSim.TryBuildMatchArgs(world, out var matchArgs, out var matchWhy)
            .ShouldBeTrue($"要凑得出两队才开得赛：{matchWhy}");
        Fire(rec, "StartMatch", matchArgs);
        AdvanceGameHours(world, (int)ArenaTuning.MatchGameHours + FightMarginGameHours);
        Mark(rec, world, "办赛毕");

        // ⑤ 挖第二层：洞穴露出来，清剿是独立一发。
        Fire(rec, "ExcavateSoil", "0|1");
        AdvanceGameHours(world, 1);
        world.Soil.IsDug(0, 1).ShouldBeTrue("第 1 层挖开了。" + Ledger(world));

        Fire(rec, "ExcavateSoil", "0|2");
        AdvanceGameHours(world, 1);
        world.Soil.IsDug(0, 2).ShouldBeTrue("第 2 层挖开了。" + Ledger(world));

        // ⑥ 清剿：四只全灭那一格才转高危。
        Fire(rec, "ClearCave", $"0|{UndergroundSoil.NativeCaveLayer}");
        FightToTheEnd(world);
        world.Soil.IsCleared(0, UndergroundSoil.NativeCaveLayer).ShouldBeTrue(
            "四只全灭，那一格转成高危生产区（特殊材料的产地）。" + Ledger(world));
        Mark(rec, world, "挖开第二层");

        // ⑦ 嵌入魔晶：停靠期、已清剿、第二层、同格未嵌入 —— 嵌完永久停长、材料提速，
        //    二阶资格也一并落下。
        Fire(rec, "EmbedCrystal", $"0|{UndergroundSoil.NativeCaveLayer}");
        world.Soil.IsEmbedded(0, UndergroundSoil.NativeCaveLayer).ShouldBeTrue(
            "魔晶嵌进去了。" + Ledger(world));
        world.Growth.Tier2Eligible.ShouldBeTrue("嵌入完成即永久记下二阶资格。" + Ledger(world));

        // ⑧ 拔锚防守（贫城）：场上倒下的每条命自动入账魂石。
        Fire(rec, "PullAnchor", string.Empty);
        AdvanceGameHours(world, (int)ArenaTuning.DefenceGameHours + FightMarginGameHours);
        Mark(rec, world, "防守毕（贫城）");

        // ⑨ 富城再守一场：两场加起来才够二阶的魂石。
        Fire(rec, "Dock", BigMapGrid.RichCity.Id);
        SailTo(world, BigMapGrid.RichCity.Id);
        AdvanceGameHours(world, DwellGameHours);
        Fire(rec, "PullAnchor", string.Empty);
        AdvanceGameHours(world, (int)ArenaTuning.DefenceGameHours + FightMarginGameHours);
        world.Town.SoulStone.ShouldBeGreaterThanOrEqualTo(
            ArenaTuning.CostOf(BeastTier.Tier2).SoulStone,
            "两场防守倒下的命够付二阶的魂石。" + Ledger(world));
        Mark(rec, world, "防守毕（富城）");

        // ⑩ 派三名矿工到高危格（嵌入后每人每 2 游戏小时 1 件特殊材料）。
        foreach (var worker in new[] { "w1", "w2", "w3" })
        {
            Fire(rec, "AssignPost", $"{worker}|soil:0_{UndergroundSoil.NativeCaveLayer}");
        }

        // ⑪ 等采矿产出经电梯进镇仓。
        AdvanceGameHours(world, MineGameHours);
        world.Town.SpecialMaterial.ShouldBeGreaterThanOrEqualTo(
            ArenaTuning.CostOf(BeastTier.Tier2).SpecialMaterial,
            "采矿产出经电梯进了镇仓，特殊材料够付二阶。" + Ledger(world));
        Mark(rec, world, "特殊材料入库");

        // ⑫ 回贫城把矿石与多出来的特殊材料换成金币（二阶要 2000 金）。
        //     贫城这一站有空艇到访（富城那一站没有），成交窗口在停稳那一刻开。
        Fire(rec, "Dock", BigMapGrid.PoorCity.Id);
        SailTo(world, BigMapGrid.PoorCity.Id);
        Fire(rec, "TradeAirship", "0|20|8");
        world.Town.Coin.ShouldBeGreaterThanOrEqualTo(
            ArenaTuning.CostOf(BeastTier.Tier2).Coin,
            "空艇成交 + 游客消费 + 门票攒出来的金币够付二阶。" + Ledger(world));

        // ⑬ 进阶仪式：门槛、代价、时长都对得上。
        Fire(rec, "AdvanceTier", string.Empty);
        world.InRitual.ShouldBeTrue("仪式开起来了。");
        AdvanceGameHours(world, (int)ArenaTuning.TierRitualGameHours);
        world.Tier.ShouldBe(BeastTier.Tier2, "满一个游戏小时后位阶 +1。");
        world.InRitual.ShouldBeFalse("仪式结束。");
        Mark(rec, world, "进阶毕");

        // ⑭ 沙城停靠：荒漠态的累计计时要靠世界自己跑（脚下那一带是沙漠才计）。
        Fire(rec, "Dock", BigMapGrid.DesertCity.Id);
        SailTo(world, BigMapGrid.DesertCity.Id);
        world.Advance(ArenaTime.StepsPerGameMinute * DesertDwellGameMinutes);
        world.Voyage.DwellGameMinutes.ShouldBeGreaterThanOrEqualTo(DesertDwellGameMinutes,
            "沙城脚下是沙漠带，累计计时是世界自己记的。" + Ledger(world));
        Mark(rec, world, "沙城计时");

        // ⑮ 两次切形态：解锁标记在夹具里已经翻上（见类注释），这两发照常进 Journal。
        Fire(rec, "SwitchForm", "desert");
        world.Form.ShouldBe(BeastForm.Desert, "荒漠态切成了。");

        Fire(rec, "SwitchForm", "glutton");
        world.Form.ShouldBe(BeastForm.Glutton, "暴食态切成了。");
        Mark(rec, world, "两次切形态毕");

        return rec;
    }

    private static LiveReplaySession Replay(ReplayRunner.ReplayScript script)
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        ApplyFormFixture(world);
        var session = new LiveReplaySession(script, ArenaSetup.Hooks(world));
        session.RunAll();
        return session;
    }

    private static string Report(LiveReplaySession session) => string.Join("\n", session.Divergences);

    [Fact]
    public void 进阶成功与两次切形态_同一轮录进Journal_重放三档全绿()
    {
        var rec = RecordRun(out var world);

        // ---- 账上那几发：一发都不许缺、也不许是被拒的那一发 ----
        var script = rec.ToScript();
        script.Entries.ShouldContain(e => e.Cmd == "AdvanceTier" && e.Ok,
            "进阶成功那一发在 Journal 里，而且是真的走成了。");
        script.Entries.Count(e => e.Cmd == "SwitchForm" && e.Ok).ShouldBe(2,
            "两次切形态都走成了。");
        script.Entries.ShouldNotContain(e => e.Cmd == "SwitchForm" && !e.Ok,
            "两次切形态没有一发是被拒的（解锁条件满足才发得出去）。");
        script.Entries.ShouldNotContain(e => !e.Ok,
            "这一轮里没有一发被拒的 —— 被拒的那一发在重放端会翻 ok，任何档位都红。");
        script.Checkpoints.Count.ShouldBeGreaterThan(0, "检查点抓到了，重放才有得比。");

        // ---- 三档重放 ----
        var literal = Replay(script);
        literal.Divergences.ShouldBeEmpty("L3 逐字要复现录制时的每一格：\n" + Report(literal));
        literal.Commands.ShouldAllBe(c => c.Equal,
            "每一发注入命令的 ok 都照录制复现（ok 翻转不走快照路径，任何档位都红）");
        literal.Checkpoints.Count.ShouldBe(script.Checkpoints.Count);
        literal.Checkpoints.ShouldAllBe(c => c.Equal, "检查点快照逐字相等");

        var semantic = Replay(rec.ToScript(ReplayRunner.AssertLevel.Semantic));
        semantic.Divergences.ShouldBeEmpty("L2 语义全绿：\n" + Report(semantic));
        semantic.Commands.ShouldAllBe(c => c.Equal, "命令 ok 一样对得上。");

        var invariants = Replay(rec.ToScript(ReplayRunner.AssertLevel.Invariants));
        invariants.Divergences.ShouldBeEmpty("L1 那一档也要全绿：\n" + Report(invariants));

        // ---- 改坏一处就红：这一档不是「跑完不红」，而是「坏了一定红」 ----
        var doctored = DoctorHeadTick(script);
        DoctoredSnapshot(doctored).ShouldNotBe(DoctoredSnapshot(script),
            "改坏头部步号这一处本身要生效，否则下面那条红是假的。");
        var doctoredWorld = ArenaWorldBuilder.New(Seed).Build();
        ApplyFormFixture(doctoredWorld);
        var doctoredSession = new LiveReplaySession(doctored, ArenaSetup.Hooks(doctoredWorld));
        doctoredSession.ExecuteNext();
        doctoredSession.Divergences.ShouldNotBeEmpty(
            "头部步号漂移是结构变化，L2 与 L3 都该判红。");

        // ---- 顺带把「重放端的世界」也守一遍恒 ----
        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());

        TestLog.Ledger($"进阶那一轮：{world.Tick} 步、命令 {script.Entries.Count} 发、"
            + $"检查点 {script.Checkpoints.Count} 个、位阶 {world.Tier}、形态 {world.Form}。");
    }

    /// <summary>取某个检查点的快照全文（标签取不到就抛 —— 取不到说明剧本没录上）。</summary>
    private static string DoctoredSnapshot(ReplayRunner.ReplayScript s)
        => s.Checkpoints.First(c => c.Label == "挖开第二层").Snapshot;

    /// <summary>
    /// 改坏一处：把每一格快照头部那一个 <c>tick=N</c> 加一（其余一格不动）。
    /// 这一改只动文本，不动世界、不动时间线、不动序号。
    /// </summary>
    private static ReplayRunner.ReplayScript DoctorHeadTick(ReplayRunner.ReplayScript s)
        => s with
        {
            Checkpoints = s.Checkpoints.Select(c => c with { Snapshot = BumpHeadTick(c.Snapshot) }).ToList(),
        };

    private static string BumpHeadTick(string snapshot)
    {
        var segs = snapshot.Split(SegSep, StringSplitOptions.None).ToList();
        var tokens = segs[0].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        var at = tokens[0].IndexOf("tick=", StringComparison.Ordinal);
        var tick = long.Parse(tokens[0][(at + 5)..], CultureInfo.InvariantCulture);
        tokens[0] = "tick=" + (tick + 1).ToString(CultureInfo.InvariantCulture);
        segs[0] = string.Join(' ', tokens);
        return string.Join(SegSep, segs);
    }
}
