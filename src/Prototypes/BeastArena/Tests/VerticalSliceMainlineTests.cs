#nullable enable
using System;
using System.IO;
using System.Linq;
using DemoFactory.GugaFramework.Replay;
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// ticket 08「固定输入主线」（规格 T07）：一条固定输入从「未开始」的第 0 步跑到终局结算与继续经营，
/// 三城、双成长、三次防守、补救与结算全部走到，完整主线 L3 重放逐字一致（规格「测试与证据」「防守、终局与重试」）。
///
/// <para><b>每一发都走写路径唯一入口</b>（<see cref="ReplayAssembly.Inject"/> → <see cref="ArenaSetup.Inject"/>）：
/// 剧本不直接改任何字段 —— 位置靠 Dock / EatTarget，状态靠动词与时间自然演化；
/// 时间推进一律 <c>world.AdvanceTo</c>。重放端从同一种子的第 0 步重建世界，只喂 Journal 里的那些发。</para>
///
/// <para><b>检查点按「先快照后命令」落</b>：每个 <see cref="ReplayAssembly.Mark"/> 都排在同一步那条命令之前，
/// 落完顺手判一次八条守恒（L1，「断言三层」里不变量那一档由测试资产承担，不进脚本头部）。</para>
///
/// <para><b>剧本从默认开局态出发</b>（<see cref="ArenaWorldBuilder"/>.New(seed).Build()，与离线重放 CLI
/// 的 <c>ArenaConfig.Default(seed)</c> 同一份开局数），不是预推进状态 ——
/// 这样入库脚本就能被一条命令重跑：
/// <c>dotnet run --project src/Tools -- replay BeastArena src/Prototypes/BeastArena/Tests/Scripts/replay-mainline.json</c>。</para>
///
/// <para><b>两条用例都很慢</b>：完整主线约三万五千个逻辑步（五十多个游戏小时：三城、两段长航行、
/// 三次防守、一次洞穴清剿、进阶仪式与最终重试），第一条还要再整跑一遍 L3 重放。
/// 慢测试标签与原因按测试规范登记在这里。</para>
///
/// <para><b>脚本漂移的处理</b>：录制用例断言「新录脚本与入库脚本逐字相等」；
/// 逻辑改动让剧本合法地变了形时，判红消息给出明确重录方式（删掉入库件再跑录制用例，然后 review git diff）。</para>
/// </summary>
public class VerticalSliceMainlineTests
{
    /// <summary>固定种子（与其余主线用例同一档）。</summary>
    private const int Seed = 7;

    /// <summary>入库脚本文件名（Tests/Scripts/ 下，跟测试一起进版本库 = 回归资产）。</summary>
    private const string ScriptFileName = "replay-mainline.json";

    /// <summary>入库脚本的磁盘路径（从测试输出目录退回 Tests/，与 InfinityLord 那一份同一口径）。</summary>
    private static string ScriptPath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Scripts", ScriptFileName));

    [Fact]
    [Trait("测试类别", "慢测试")] // 慢的原因：录制整条主线约 3.5 万逻辑步，再对同一份脚本整跑一遍 L3 重放。
    public void 固定输入主线_从开局录到自由经营_入库脚本无漂移且L3重放逐字一致()
    {
        using var run = new MainlineRun(Seed);
        run.RecordMainline();
        var world = run.World;

        // ---- 主线终点（规格「流程状态」：Settlement → FreePlay） ----
        world.Flow.Stage.ShouldBe(RunStage.FreePlay, "末尾进入继续经营");
        world.Tick.ShouldBeLessThan(
            54_000, "完整主线要在合理步数上限内跑完（54 000 步 = 90 游戏小时；跑满上限说明有逻辑失速）");

        // ---- 剧本导出与入库件比对（「剧本里每一发命令都走写路径唯一入口」+ 漂移判据） ----
        var script = run.Rec.ToScript();
        script.Entries.Count.ShouldBeGreaterThan(40, "三城主线不止几十发写路径。");
        script.Checkpoints.Count.ShouldBeGreaterThan(10, "每个阶段都要留下一格可断言的检查点。");
        script.Entries.ShouldAllBe(e => e.Ok, "主线剧本的每一发都得真打得成（被拒的发不进这份剧本）。");

        var json = ReplayScriptJson.Write(script);
        if (!File.Exists(ScriptPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScriptPath)!);
            File.WriteAllText(ScriptPath, json);
        }
        else
        {
            File.ReadAllText(ScriptPath).ShouldBe(json,
                "主线剧本与入库脚本漂移了。若是逻辑改动让剧本合法变形：删掉 "
                + $"{ScriptFileName} 后重跑本用例（dotnet test src/Prototypes/BeastArena/Tests "
                + "--filter 固定输入主线），再 review git diff 后入库。");
        }

        // ---- L3 重放（内存里那份，逐字档） ----
        var session = Replay(script);
        session.Divergences.ShouldBeEmpty("完整主线 L3 重放要逐字一致：\n" + Report(session));
        session.Commands.ShouldAllBe(c => c.Equal, "每一发注入命令的 ok 都照录制复现。");
        session.Checkpoints.Count.ShouldBe(script.Checkpoints.Count);
        session.Checkpoints.ShouldAllBe(c => c.Equal, "每个检查点快照逐字相等。");
    }

    [Fact]
    [Trait("测试类别", "慢测试")] // 慢的原因：对入库脚本整跑一遍重放（约 3.5 万逻辑步）。
    public void 入库主线脚本_从磁盘读回重放_零分歧且命令ok全一致()
    {
        File.Exists(ScriptPath).ShouldBeTrue(
            $"入库主线脚本缺失 —— 先跑「固定输入主线_从开局录到自由经营」补录 {ScriptFileName}");

        var script = ReplayScriptJson.Read(File.ReadAllText(ScriptPath));
        script.Seed.ShouldBe(Seed);
        script.StartTick.ShouldBe(0, "主线从第 0 步的默认开局态录制。");

        var session = Replay(script);
        session.Divergences.ShouldBeEmpty("入库脚本必须能被一条命令重跑且零分歧：\n" + Report(session));
        session.Commands.ShouldAllBe(c => c.Equal, "读回来的每一发照录制打出同样的 ok。");
        session.Checkpoints.ShouldAllBe(c => c.Equal, "读回来的每个检查点逐字相等。");
        session.Finished.ShouldBeTrue("整份剧本喂完，不留半截。");
    }

    /// <summary>重放一份剧本：新建同种子世界，三钩子吃 <see cref="ArenaSetup.Hooks"/> 那一份装配。</summary>
    private static LiveReplaySession Replay(ReplayRunner.ReplayScript script)
    {
        var world = ArenaWorldBuilder.New(script.Seed).Build();
        var session = new LiveReplaySession(script, ArenaSetup.Hooks(world));
        session.RunAll();
        return session;
    }

    private static string Report(LiveReplaySession session) => string.Join("\n", session.Divergences);

    /// <summary>逐段比对「相同」那一档的回话（<see cref="FirstDiff"/> 用它作判绿的哨兵）。</summary>
    private const string Same = "逐段相同";

    /// <summary>
    /// 两份 L3 快照的第一处不同段。<b>只为判红时指路</b>：整串几千字，
    /// 直接把 Shouldly 的字符级 diff 摊在终端上没人看得下去，切成一格一段才读得出是哪一个读数没还原。
    /// </summary>
    private static string FirstDiff(string expected, string actual)
    {
        var a = expected.Split('|');
        var b = actual.Split('|');
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
            {
                return $"第 {i} 段不同：战前「{a[i].Trim()}」↔ 还原后「{b[i].Trim()}」"
                       + $"（段数 {a.Length} / {b.Length}）";
            }
        }

        return a.Length == b.Length ? Same : $"段数不同：{a.Length} / {b.Length}";
    }

    /// <summary>
    /// 录制端：一局主线从第 0 步跑到 FreePlay。
    /// 公开面只有 <see cref="RecordMainline"/> 与两个读数；所有写都走 <see cref="Command"/>，
    /// 所有等待都走 <see cref="WaitUntil"/>（逐格推进，条件成立即收，条件里就地发的那几发也是写路径）。
    /// </summary>
    private sealed class MainlineRun : IDisposable
    {
        private const int Sector = UndergroundSoil.CaveSector;          // 单处洞穴固定在象限 0
        private const int CaveLayer = UndergroundSoil.NativeCaveLayer;  // 洞穴固定在第 2 层
        private const double FedTarget = ArenaTuning.GluttonFormFedTotalFood; // 400

        private bool _gluttonSwitched;

        public MainlineRun(int seed)
        {
            World = ArenaWorldBuilder.New(seed).Build();
            Rec = ArenaSetup.Recorder(World);
            Rec.BeginSession();
        }

        public ArenaWorld World { get; }

        public ReplayAssembly Rec { get; }

        /// <summary>已发的命令数（判红消息里的序号）。</summary>
        public int Commands { get; private set; }

        public void Dispose() => Rec.Dispose();

        /// <summary>把整条主线录完（主线骨架按规格「分段」走；每一步的理由写在它那一节注释里）。</summary>
        public void RecordMainline()
        {
            OpeningVoyageAndPoorCity();
            PoorCityDefenceAndFirstVoyage();
            SandCityAndTier2();
            SandCityDefenceAndSecondVoyage();
            RichCityFinalDefenceAndSettlement();
        }

        // === 开局航程 + 贫城经营（建造 / 招募 / 交易 / 办赛 / 喂到暴食进度满） ===

        private void OpeningVoyageAndPoorCity()
        {
            Check("cp-开局");
            Command("StartRun", string.Empty);

            Check("cp-出发贫城");
            Command("Dock", BigMapGrid.PoorCity.Id);
            // 开局停在贫城西边 40 米的野地上：40 米 ÷ 0.04 米每步 = 1000 步，留一倍余量。
            WaitUntil(() => World.IsDocked && World.DockedCityId == BigMapGrid.PoorCity.Id,
                4_000, "驶入贫城并停稳");
            World.Flow.Stage.ShouldBe(RunStage.Poor, "到了贫城才进贫城段。");
            Check("cp-贫城停稳");

            // 停稳那一刻开空艇成交窗口（30 游戏分钟）：先卖光开局 10 件矿石。
            TradeAirshipSellOre();

            // 空船停靠区：把每一航段的空艇到访数从 0..2 抬到 1..3（沙城与富城的窗口靠它兜底）。
            Command("PlaceBuilding", "AirshipDock|-11|-11");
            World.HasBlueprint("AirshipDock").ShouldBeTrue();

            // 招募：本次停靠额度 3 人（城乡招，100 金一人）。
            for (var i = 0; i < ArenaTuning.RecruitPerDock; i++)
                Command("Recruit", RecruitSource.TownOrCountry.ToString());
            World.RecruitedThisDock.ShouldBe(ArenaTuning.RecruitPerDock);

            // 定价与发薪：门票倍率拉到上限 2.0，应发的工钱一次结清。
            Command("SetPricing", "1|1|2");
            Command("PayWages", string.Empty);

            // 办贫城那一场必打的比赛（菜单同一口挑人：现有角斗士交替分两队）。
            Check("cp-办赛前");
            Command("StartMatch", BuildMatchArgs());
            World.Battle.Occasion.ShouldBe(BattleOccasion.Match);
            WaitUntil(() => World.Battle.Occasion == BattleOccasion.None, 1_100, "首赛打满 1.5 游戏小时收场");
            World.Town.TicketIncome.ShouldBeGreaterThan(0.0, "门票要有进账。");
            Check("cp-首赛落幕");

            // 喂到暴食进度满：每 5 份食物换 1 点饱食度，衰减每小时 4 点 ——
            // 每格看一眼容量，够一口就喂；凑满 400 那一刻解锁只差「驶离贫城」那道门。
            WaitUntil(() => World.Growth.FedTotalFood >= FedTarget, 16_000,
                $"累计有效喂满 {FedTarget:0} 食物", FeedOnce);
            Check("cp-暴食进度满");

            // 开挖等过第一道回长日界（第 14 400 步）：界前挖开的格会在界上被回长重置，
            // 清剿状态跟着清零 —— 沙城嵌入赶不上就全白做。界前这段只是等待，不放写。
            World.AdvanceTo(14_460);
        }

        // === 贫城：开挖两层 → 洞穴清剿 → 拔锚防守胜利 → 第一段航行（吞食、魔晶、切暴食态） ===

        private void PoorCityDefenceAndFirstVoyage()
        {
            Check("cp-开挖前");
            Command("ExcavateSoil", $"{Sector}|1");
            WaitUntil(() => World.Soil.IsDug(Sector, 1), 700, "象限 0 第 1 层挖开（派满 3 人工时 1 游戏小时）");
            Command("ExcavateSoil", $"{Sector}|{CaveLayer}");
            WaitUntil(() => World.Soil.IsDug(Sector, CaveLayer), 700, "象限 0 第 2 层挖开（洞穴在那一层）");
            Check("cp-挖到第二层");

            // 岗位：3 名工人下洞层采矿岗（特殊材料的产地），1 名派进教堂当在岗牧师（战后抬伤员）。
            foreach (var miner in new[] { "w1", "w2", "w3" })
                Command("AssignPost", $"{miner}|soil:{Sector}_{CaveLayer}");
            Command("AssignPost", "w6|church");

            // 洞穴清剿：四只全灭才算清剿；保险起见留三次发起的机会（每次只涌出洞里剩下的那几只）。
            for (var attempt = 1; !World.Soil.IsCleared(Sector, CaveLayer); attempt++)
            {
                attempt.ShouldBeLessThanOrEqualTo(3, "三次清剿还没把四只全灭 —— 战斗模拟出了预期外的事。");
                Check($"cp-第{attempt}次清剿前");
                Command("ClearCave", $"{Sector}|{CaveLayer}");
                WaitUntil(() => World.Battle.Occasion == BattleOccasion.None, 1_100, "洞穴清剿收场");
                World.Battle.LastReport.ShouldNotBeNull();
            }
            World.Soil.IsCleared(Sector, CaveLayer).ShouldBeTrue("四只全灭才算清剿成功。");
            Check("cp-洞穴清剿胜");

            // 贫城防守：拔锚开 2 游戏小时的限时防守，到点且镇政府未破即胜（+10 魂石）。
            Check("cp-贫城拔锚前");
            Command("PullAnchor", string.Empty);
            World.Battle.Occasion.ShouldBe(BattleOccasion.Defence);
            WaitUntil(() => World.Battle.Occasion == BattleOccasion.None, 1_600, "贫城防守收场");
            World.TownHallBreached.ShouldBeFalse("这一场镇政府不该被攻破。");
            World.Flow.Stage.ShouldBe(RunStage.ToSand, "贫城防守胜利 → 第一段航行。");
            // 暴食解锁的两道门：累计喂满 400 食物（前面 WaitUntil 已保证）与「驶离贫城」。
            // 流程翻页排在防守收场那一步的末尾，而解锁判定在每一步的 DecideStep 之后重判 ——
            // 所以这一格还判不出解锁，等它落地要到航行开始后的下一步（就在那一段判）。
            World.Town.SoulStone.ShouldBeGreaterThanOrEqualTo(
                ArenaTuning.CostOf(BeastTier.Tier2).SoulStone, "开局 30 枚 + 胜利 10 枚：二阶的魂石在这一场之后就该齐。");
            RepairTownHallIfHit("贫城防守后");
            Check("cp-贫城防守胜");

            // 第一段航行：贫城 → 沙城的航线路肩上压着两块矿石。
            // 兽每格沿航线走，进了哪块矿石的 10 米圈就手动吞哪块（「航行途中吞食」走 EatTarget 写路径）；
            // 第一单位魔晶一产出，就手动切上暴食态（双成长的那道「至少切换过一次」）。
            Check("cp-首航出发前");
            Command("Dock", BigMapGrid.DesertCity.Id);
            // 解锁的「驶离贫城」那道门在拔锚那一刻才开，判定要等下一步（每一步 DecideStep 之后重判）：
            // 等一步让落地，里程碑才拿得到真实步号，而不是提前硬判。
            WaitUntil(() => World.Growth.GluttonFormUnlocked, 10, "驶离贫城后一步：暴食态解锁", SailLegOneStep);
            World.Flow.GluttonUnlockedTick.ShouldBeGreaterThan(0, "解锁那一刻要落进里程碑。");
            WaitUntil(() => World.IsDocked && World.DockedCityId == BigMapGrid.DesertCity.Id,
                9_000, "驶达沙城", SailLegOneStep);
            World.Ecology.CrystalsProduced.ShouldBeGreaterThanOrEqualTo(1,
                "两块矿石 120 点消化进度：抵沙城前必须拿到第一单位魔晶（规格「生态对象」）。");
            _gluttonSwitched.ShouldBeTrue("魔晶产出之后要手动切一次暴食态。");
            World.Flow.Stage.ShouldBe(RunStage.Sand);
            World.Flow.FirstSwitchTick.ShouldBeGreaterThan(0, "首次手动切换落进里程碑。");
            Check("cp-沙城停稳");
        }

        // === 沙城：魔晶入库 → 嵌入 → 二阶 → 拔锚防守胜利 → 第二段航行 ===

        private void SandCityAndTier2()
        {
            // 停稳那一刻的空艇窗口：卖掉航行与停靠期间采出的矿石（二阶的金币从这来）。
            TradeAirshipSellOre();

            // 航行中产出的那件魔晶还躺在甲板上：等工人把它搬进镇政府仓库（嵌入读的是仓库那本账）。
            WaitUntil(() => World.Town.Crystal >= 1.0, 6_000, "魔晶搬进镇政府仓库");
            World.CrystalOnDeck.ShouldBe(0, "入库之后甲板上不该再剩。");
            Check("cp-魔晶入库");

            // 嵌入已清剿的第二层：永久停止回长，特殊材料产出提速到每岗 2 游戏小时一件。
            World.EmbedBlockerOf(Sector, CaveLayer).ShouldBe(SoilEmbedBlocker.None,
                "停靠 + 已挖开 + 洞穴层 + 已清剿 + 未嵌入 + 仓里有货，六项都得齐。");
            Check("cp-嵌入前");
            Command("EmbedCrystal", $"{Sector}|{CaveLayer}");
            World.Soil.IsEmbedded(Sector, CaveLayer).ShouldBeTrue();
            World.Growth.Tier2Eligible.ShouldBeTrue("嵌入成功就把二阶进阶资格永久记下来。");
            World.Town.Crystal.ShouldBe(0.0, 1e-9, "嵌掉的就是仓里那 1 单位。");
            Check("cp-嵌入第二层");

            // 沙城的比赛自愿：再补一笔门票进账（也是双成长等待期里的一段经营）。
            // 挑不齐两队角斗士就跳过 —— 前两场战斗可能打残，这一场不是主线的承重项。
            Check("cp-沙城办赛前");
            if (BattleSim.TryBuildMatchArgs(World, out var sandMatch, out _))
            {
                Command("StartMatch", sandMatch);
                WaitUntil(() => World.Battle.Occasion == BattleOccasion.None, 1_100, "沙城比赛收场");
            }

            // 等三本账凑齐（魂石 40 / 特殊材料 10 / 金币 2000）：材料按小时从嵌过的格往外产，等待是游戏时间的事。
            WaitUntil(() => ArenaSetup.AdvanceBlockerOf(World) == AdvanceBlocker.None, 14_000,
                $"二阶门槛与三项代价齐（魂石 {World.Town.SoulStone:0.##}、材料 {World.Town.SpecialMaterial:0.##}、"
                + $"金 {World.Town.Coin:0.##}）");
            Check("cp-二阶可进阶");
            Command("AdvanceTier", string.Empty);
            World.InRitual.ShouldBeTrue("仪式开起来了（一游戏小时）。");
            WaitUntil(() => World.Tier == BeastTier.Tier2, 800, "进阶仪式走完");
            World.Flow.Tier2Tick.ShouldBeGreaterThan(0, "二阶完成落进里程碑。");
            World.Voyage.TierSpeedFactor.ShouldBe(
                ArenaTuning.StatsOf(BeastTier.Tier2).SpeedFactor, 1e-9, "二阶移速倍率从位阶表生效。");
            Check("cp-二阶完成");
        }

        private void SandCityDefenceAndSecondVoyage()
        {
            Check("cp-沙城拔锚前");
            Command("PullAnchor", string.Empty);
            World.Battle.Occasion.ShouldBe(BattleOccasion.Defence);
            WaitUntil(() => World.Battle.Occasion == BattleOccasion.None, 1_600, "沙城防守收场");
            World.TownHallBreached.ShouldBeFalse("沙城这一场镇政府不该被攻破。");
            World.Flow.Stage.ShouldBe(RunStage.ToRich, "沙城防守胜利 → 第二段航行。");
            RepairTownHallIfHit("沙城防守后");
            Check("cp-沙城防守胜");

            // 第二段航行：二阶 1.15 倍移速跑 138 米；沿途够得着富城草地的树与兽群，
            // 饿了会自己脱队吞（规格「分段」这一段允许继续吞食）。
            Check("cp-二航出发前");
            Command("Dock", BigMapGrid.RichCity.Id);
            WaitUntil(() => World.IsDocked && World.DockedCityId == BigMapGrid.RichCity.Id,
                18_000, "驶达富城");
            World.Flow.Stage.ShouldBe(RunStage.Rich);
            Check("cp-富城停稳");
        }

        // === 富城：建造 / 交易 → 最终防守（先败一次）→ 重试 → 胜利 → 结算 → 继续经营 ===

        private void RichCityFinalDefenceAndSettlement()
        {
            // 富城的交易：把攒下的矿石换成盖房的钱（空船停靠区把这一段的窗口数兜到 ≥1）。
            WaitUntil(() => World.InAirshipWindow, 60, "富城空艇成交窗口开");
            TradeAirshipSellOre();

            // 富城的建造：一座常规传送阵（拆掉贫城时期盖的饭店腾格 —— 拆除不留废墟，落位判据最干净）。
            // 格号 -11,-7 起那块 2×2 在整条主线上没人占：西北角 -11,-11 起那 3×3 已给了空船停靠区。
            Check("cp-富城建造前");
            if (World.Construction.Find("restaurant") is not null)
                Command("RemoveBuilding", "restaurant");
            Command("PlaceBuilding", "TeleportArray|-11|-7");
            World.HasBlueprint("TeleportArray").ShouldBeTrue();
            if (World.WagesDue > 0.0 && World.Town.Coin >= World.WagesDue)
                Command("PayWages", string.Empty);
            Check("cp-富城经营完");

            // 双成长三项齐：二阶 + 暴食解锁 + 手动切换过一次（规格「双成长完成」）。
            World.Flow.DoubleGrowthComplete.ShouldBeTrue();

            // 空城计 —— 第一次尝试要真输，得先让敌人「抢不着东西」。
            // 规格「普通防守」的敌人动作是「抢得到就抢一件、抢不到才打 1 点耐久」，而且每人抢满
            // <see cref="ArenaTuning.EnemyStealLimit"/> 件就走：镇仓几十份食物摆在那儿，
            // 24 个敌人各抢三件便全体退场，镇政府一点伤也受不着 —— 这一场就永远不破，
            // 「先失败一次」也没有失败可言。把存粮喂给兽之后，可抢的总数（金 12 件、材料 24、
            // 备件 1、食物零头）远够不上 24×3 = 72 件，漏网的敌人只能去啃镇政府。
            WaitUntil(() => World.Town.Food < 10.0, 9_000, "镇仓存粮喂到只剩零头", FeedAwayTownFood);

            // 最终防守第一次尝试：开战当格把全部在册镇民设成非战斗 —— 一个拦截的人都不留，
            // 与上面那口空城一起把「这一场真会输」做实（规格「普通防守」：没人拦的敌人一路啃到镇政府见底）。
            // 这几发排在「首次开战」之后、同一个 tick；重试按战前快照还原时把它们一并作废
            // （规格「最终重试」第 6 条），第二次尝试就是同一份状态配全员参战。
            Check("cp-最终开战");
            Command("PullAnchor", string.Empty);
            World.Flow.Stage.ShouldBe(RunStage.FinalDefence, "双成长完成且在富城拔锚 → 最终防守。");
            World.Battle.Occasion.ShouldBe(BattleOccasion.FinalDefence);
            World.FinalSnapshotTick.ShouldBe(World.Tick, "战前快照在初始化完、未推首个逻辑步时取。");
            var preBattle = World.DebugSnapshot();
            var preBattleTick = World.Tick;

            foreach (var unit in World.Townspersons)
                Command("SetNonCombatant", $"{unit.Id}|1");
            World.Townspersons.Count(u => u.NonCombatant).ShouldBe(World.Townspersons.Count);

            WaitUntil(() => World.Battle.Occasion == BattleOccasion.None, 1_500,
                "最终防守第一次尝试被攻破收场");
            World.Flow.Stage.ShouldBe(RunStage.FinalDefence, "失败留在最终防守等重试。");
            World.FinalRetryCount.ShouldBe(1, "每一次失败累计一次重试。");
            World.FinalAttemptIndex.ShouldBe(1, "首次尝试序号 1。");
            World.CanRetryFinalDefence.ShouldBeTrue();
            World.Battle.LastReport!.Occasion.ShouldBe(BattleOccasion.FinalDefence, "战报场合显示最终防守。");
            Check("cp-最终失败");

            // 立即重试：不重建世界、不重跑装配，按战前快照原样还原。
            // 「恢复后」那一格 L3 用 DebugSnapshot 逐字比（测试侧断言）：还原把步号拨回战前，
            // 检查点快照只能落在重试之后第一个「步号大于失败收场那一格」的时刻，那已由胜利结算
            // 那一格与整条重放时间线盖住。
            Command("RetryFinalDefence", string.Empty);
            FirstDiff(preBattle, World.DebugSnapshot()).ShouldBe(Same);
            World.Tick.ShouldBe(preBattleTick, "还原把步号也拨回战前（随机推进一起作废）。");
            World.FinalAttemptIndex.ShouldBe(2, "每次重试有独立尝试序号。");
            World.Flow.Stage.ShouldBe(RunStage.FinalDefence, "直接重开同一场。");

            // 第二次尝试：同一份战前状态配自然防守，到点取胜 → 结算。
            WaitUntil(() => World.Battle.Occasion == BattleOccasion.None, 1_500,
                "最终防守第二次尝试打满时限");
            World.Flow.Stage.ShouldBe(RunStage.Settlement, "最终防守赢了进结算。");
            var settlement = World.Settlement;
            settlement.ShouldNotBeNull("结算在胜利那一刻已经挂在世界上。");
            AssertSettlementFields(settlement!);
            Check("cp-终局结算");

            // 继续经营：目标改成自由经营，结算不再被改写。
            Command("ContinueBusiness", string.Empty);
            World.Flow.Stage.ShouldBe(RunStage.FreePlay);
            World.Flow.Goal.ShouldContain("自由经营", Case.Sensitive);
            Check("cp-自由经营");
        }

        private void AssertSettlementFields(RunSettlement settlement)
        {
            settlement.Tier.ShouldBe(BeastTier.Tier2, "位阶。");
            settlement.GluttonUnlocked.ShouldBeTrue("暴食态状态。");
            settlement.FinalRetryCount.ShouldBe(1, "结算钉住「重试过一次」的账。");
            settlement.DefenceWins.ShouldBeGreaterThanOrEqualTo(3, "贫城、沙城、最终各一胜。");
            settlement.DefenceLosses.ShouldBeGreaterThanOrEqualTo(1, "最终防守留得下失败那一笔。");
            settlement.TicketIncome.ShouldBeGreaterThan(0.0, "门票收入。");
            settlement.Coin.ShouldBe(World.Town.Coin, 1e-9, "当前金币。");
            settlement.Debt.ShouldBe(World.Town.TotalDebt, 1e-9, "负债。");
            settlement.CrystalProduced.ShouldBeGreaterThanOrEqualTo(1.0, "魔晶产出。");
            settlement.CrystalUsed.ShouldBeGreaterThanOrEqualTo(1, "嵌入记一次使用。");
            settlement.AliveResidents.ShouldBeGreaterThan(0, "居民存活数。");
            settlement.IntactBuildings.ShouldBeGreaterThan(0, "建筑完好数。");
            settlement.Segments.Count.ShouldBe(3, "三个体验段。");
            settlement.Segments[0].Key.ShouldBe("poor");
            settlement.Segments[1].Key.ShouldBe("sand");
            settlement.Segments[2].Key.ShouldBe("rich");
            settlement.Segments[0].DefenceWins.ShouldBeGreaterThanOrEqualTo(1, "贫城段的防守结果。");
            settlement.Segments[1].DefenceWins.ShouldBeGreaterThanOrEqualTo(1, "沙城段的防守结果。");
            settlement.Segments[2].Defences.Count.ShouldBeGreaterThanOrEqualTo(2, "富城段先败后胜两笔。");
            settlement.Milestones.Count.ShouldBe(4);
            settlement.GluttonUnlockedMilestone.Tick.ShouldBeGreaterThan(0);
            settlement.FirstFormSwitchMilestone.Tick.ShouldBeGreaterThan(0);
            settlement.Tier2Milestone.Tick.ShouldBeGreaterThan(0);
            settlement.FinalVictoryMilestone.Tick.ShouldBe(settlement.VictoryTick);
        }

        // === 逐格推进里就地发的条件命令（都是写路径） ===

        /// <summary>够一口就喂：容量 = 镇仓、兽粮、饱食余量里最小那份，够 50 份才发。</summary>
        private void FeedOnce()
        {
            if (ArenaSetup.FeedCapacity(World) >= 50.0)
                Command("Feed", "50");
        }

        /// <summary>
        /// 空城那一档的喂法：余量攒够十份就整口喂掉（一口一口喂 1 份会把剧本灌满碎发）。
        /// 只喂到剩个零头 —— 镇仓的整数份食物本来就是敌人的一口一件，清零才是目的。
        /// </summary>
        private void FeedAwayTownFood()
        {
            var chunk = (int)System.Math.Floor(ArenaSetup.FeedCapacity(World));
            if (chunk >= 10) Command("Feed", chunk.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>首航每一格：进矿石圈就手动吞；第一单位魔晶一产出就切暴食态。</summary>
        private void SailLegOneStep()
        {
            foreach (var oreId in new[] { "ore_poor_1", "ore_poor_2" })
            {
                var node = World.Ecology.Find(oreId);
                if (node is null || !node.AvailableAt(World.Tick)) continue;
                if (World.Voyage.Phase != VoyagePhase.Sailing) continue;
                if (World.Ecology.Satiation >= ArenaTuning.SatiationMax - 1e-9) continue;
                if (World.Voyage.Position.DistanceTo(node.Position) > ArenaTuning.ManualEatRangeMeters) continue;
                Command("EatTarget", oreId);
            }

            if (!_gluttonSwitched
                && World.Ecology.CrystalsProduced >= 1
                && World.Growth.FormUnlocked(BeastForm.Glutton)
                && World.Form != BeastForm.Glutton
                && World.Battle.Occasion == BattleOccasion.None)
            {
                Command("SwitchForm", "glutton");
                _gluttonSwitched = true;
            }
        }

        // === 小工具 ===

        /// <summary>把镇仓里的矿石整件卖光（空艇窗口内才发得出；窗口在停稳那一刻开）。</summary>
        private void TradeAirshipSellOre()
        {
            World.InAirshipWindow.ShouldBeTrue("停稳那一刻应开着空艇成交窗口。");
            var sellOre = (int)System.Math.Floor(World.Town.Ore);
            Command("TradeAirship", $"0|{sellOre}|0");
        }

        /// <summary>防守把镇政府打掉耐久就修一次（备件开局 3 件，够整条主线用）。</summary>
        private void RepairTownHallIfHit(string after)
        {
            var hall = World.Construction.Find(ArenaWorld.TownHallId)!;
            if (hall.Health >= hall.MaxHealth) return;
            Command("AssignPost", "w4|town_hall");
            Command("RepairBuilding", ArenaWorld.TownHallId);
            var repaired = World.Construction.Find(ArenaWorld.TownHallId)!;
            repaired.Health.ShouldBeGreaterThanOrEqualTo(
                hall.Health, $"{after}：那一发修理要么没需要（耐久满），要么把耐久往回抬。");
        }

        /// <summary>挑人开赛：与竞技场菜单同一口（在册角斗士按名单序交替分两队）。</summary>
        private string BuildMatchArgs()
        {
            BattleSim.TryBuildMatchArgs(World, out var args, out var reason).ShouldBeTrue(
                $"凑不出两队角斗士：{reason}");
            return args;
        }

        /// <summary>检查点：先落快照（「先快照后命令」），落完顺手判一次八条守恒（L1）。</summary>
        private void Check(string label)
        {
            Rec.Mark(label);
            World.Town.IsConserved().ShouldBeTrue(
                $"L1 不变量：检查点「{label}」@tick={World.Tick} 八条等式仍然要平：{World.Town.ConservedReport()}");
        }

        /// <summary>写路径唯一入口：每一发都进 Journal；主线剧本里每一发都必须打得成。</summary>
        private void Command(string verb, string args)
        {
            var ok = Rec.Inject(verb, args, out var reason);
            Commands++;
            ok.ShouldBeTrue($"第 {Commands} 发 {verb}|{args} 被拒（tick={World.Tick}）：{reason}");
        }

        /// <summary>
        /// 逐格推进到条件成立：每一格先跑 <paramref name="eachStep"/>（就地发条件命令），
        /// 再判条件，再走一格。上限步数就是这条用例「怕逻辑失速」的那道保险。
        /// </summary>
        private void WaitUntil(Func<bool> done, int maxSteps, string what, Action? eachStep = null)
        {
            for (var i = 0; i <= maxSteps; i++)
            {
                eachStep?.Invoke();
                if (done()) return;
                World.AdvanceTo(World.Tick + 1);
            }

            throw new InvalidOperationException(
                $"{maxSteps} 步仍未等到「{what}」：tick={World.Tick}、phase={World.Voyage.Phase}、"
                + $"occasion={World.Battle.Occasion}、stage={World.Flow.Stage}、satiation={World.Ecology.Satiation:0.#}");
        }

    }
}
