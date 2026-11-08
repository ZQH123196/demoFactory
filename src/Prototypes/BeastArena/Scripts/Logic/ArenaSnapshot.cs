#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DemoFactory.GugaFramework.Construction;
namespace DemoFactory.Prototypes.BeastArena.Logic;

// 检查点快照那一件（格式只有这一处）。
//
// 分段清单（各段以 ` | ` 分隔）：
//   · 第 0 段段首是 `tick=<N>`，其余各段段首是 `<类型> <Id>` 双词身份（L2 语义档判红靠那两样）。
//   · 巨兽段一段 `BEAST beast tier=.. form=.. ritual=.. dwell=../.. fed=../..`
//     （位阶、形态、仪式剩余分钟与两个解锁进度），后面接生态三项
//     `sat=.. dig=.. ovf=.. crystal=<甲板>/<搬运中>/<仓库>`。
//   · 生态对象一物一段 `ECO <id> kind=.. pos=.. state=.. herd=.. regrow=..`
//     （state = ready / eaten；regrow 是剩余游戏分钟，矿石写 never）。
//   · 头部段末尾几格是跨段的汇总读数：`airship=<剩余分钟>`、`form_unlocked=desert:0/1,glutton:0/1`、
//     `flow=<段>`、`home=<归属城>`、`next=<下一站>`、`ms=glutton:<步号>,switch:<步号>,tier2:<步号>,final:<步号>`、
//     `held=<类别>:<id>`（手上正拿着哪一件）、`church=<尸体id:人id:游戏分钟,…>`、
//     `manual=<x>,<z>`（手动目标点，没有就写 `-`）、
//     `wages_due=<应发金币>`、`arrears_days=<欠薪天数>`、`debts=<势力:余额,…>`。
//   · 建筑段一栋一段；土壤段 8 格固定；活物段先 `entity count=.. issued=..` 再按登记序一段一个；
//     点位物段先 `item count=.. issued=..` 再按生成序一段一件。
//   · 留名账是在那几段之后另接一段 `STAR` / `SALE` ——
//     spec §3.4 与 Out of Scope 都要「成交后留名入账」「仅留账可查」，而这一本是世界实例上的账，
//     进检查点快照才谈得上「状态可查」（断言比的正是这份快照）。
//   · 招募额度在那之后另接一段 `RECRUIT left=.. used=.. per_dock=.. dock=..` ——
//     每停 3 人是这一停的余量，不写进快照就分不出「还能招」与「已招满」。
//   · 战报在最后一段 `R occasion=.. outcome=..`（没打过仗也占一行）。
//
// `ReplaySnapshot()` 在 `DebugSnapshot()` 之后追加甲板与大地图两个寻路空间的重放段（分号转空格）。

/// <summary>检查点快照那一件 —— 格式只有这里一处，两个入口共用。</summary>
public static class ArenaSnapshot
{
    /// <summary>调试快照（状态可查那一口）。</summary>
    public static string Debug(ArenaWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);

        var segments = new List<string> { HeaderSegment(world) };
        segments.Add(BeastSegment(world));
        segments.AddRange(world.Ecology.DebugSegments(world.Tick));
        segments.AddRange(BuildingSegments(world));
        segments.Add(SoilSegment(world));
        segments.Add(world.Roster.DebugSegments());
        segments.Add(world.Items.DebugSegments());
        segments.Add(StarSaleSegment(world));
        segments.Add(RecruitSegment(world));
        segments.Add(ReportSegment(world));
        return string.Join(" | ", segments);
    }

    /// <summary>重放快照：调试快照 + 两个寻路空间的重放段（分号转空格）。</summary>
    public static string Replay(ArenaWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);

        return Debug(world)
            + " | [甲板] " + world.DeckStepper.ReplaySnapshotSegment().Replace(";", " ")
            + " | [大地图] " + world.MapStepper.ReplaySnapshotSegment().Replace(";", " ");
    }

    // === 分段清单 ===

    /// <summary>
    /// 第 0 段：头部汇总。
    ///
    /// <para>末尾那一格 <c>airship=&lt;剩余游戏分钟&gt;</c> 是空艇成交窗口还剩多久
    /// （不在窗口内是 0）—— 与面板上那一行读的是同一口
    /// （<see cref="ArenaSetup.AirshipMinutesLeft"/>）。</para>
    ///
    /// <para>再往末尾那一格 <c>form_unlocked=desert:0/1,glutton:0/1</c> 是两个可解锁形态的旗标
    /// （1 = 已解锁）。它放在头部段而不是巨兽段：头部段只被「段首是 <c>tick=</c>」与几个
    /// <c>ShouldContain</c> 钉着，形态旗标加在这里不动其它段的逐字基线。</para>
    ///
    /// <para>再往末尾四格是一局流程的读数：<c>flow=&lt;段&gt;</c>（<see cref="RunStageText.TokenOf"/> 的 token）、
    /// <c>home=&lt;归属城 id&gt;</c>、<c>next=&lt;下一站 id&gt;</c>（没有就写 <c>-</c>）、
    /// <c>ms=glutton:..,switch:..,tier2:..,final:..</c>（四个里程碑步号，没报过写 <c>-1</c>）。
    /// <b>它们读的是流程自己的状态，不是从坐标推的</b>：段与资格只有 <see cref="RunFlow"/> 说了算。</para>
    ///
    /// <para>末尾再接着三格账本读数：<c>wages_due=&lt;应发金币&gt;</c>（发薪那一摊挂着多少）、
    /// <c>arrears_days=&lt;欠薪天数&gt;</c>（欠了几天）、<c>debts=&lt;势力:余额,…&gt;</c>
    /// （按债主分户；一笔债都没有就写空串）。</para>
    ///
    /// <para>再往末尾那一格 <c>church=&lt;尸体 id:人 id:游戏分钟,…&gt;</c> 是教堂在册尸体
    /// （一具都没有就写空串）—— 与 <c>debts=</c> 同一形状。</para>
    ///
    /// <para>它前面那一格 <c>held=&lt;类别&gt;:&lt;id&gt;</c> 是手上正拿着哪一件
    /// （类别是 <c>person</c> / <c>corpse</c> / <c>cargo</c>；手上没东西就写空串）。
    /// 抓着那一件时这一格非空，松手那一发清空。</para>
    ///
    /// <para>再往末尾那一格 <c>manual=<x>,<z></c> 是手动目标点（世界米）——
    /// 玩家右键点地图空地那一发落在这里。没有手动目标时写 <c>-</c>，与 <c>church=</c> 同一形状。
    /// <b>头部段仍以 <c>debts=</c> 结尾</b>（那一格是尾巴，<c>Tests/WagesTests.cs</c> 钉着）。</para>
    /// </summary>
    public static string HeaderSegment(ArenaWorld world)
    {
        var town = world.Town;
        var beast = world.Voyage.Position;
        var phases = new List<string>();
        if (world.Siege.Breached) phases.Add("breached");
        if (world.Siege.InStandstillAt(world.Tick)) phases.Add("standstill");
        if (world.IsDocked) phases.Add(world.DockedCityId);
        if (world.Wanted) phases.Add("wanted");
        var phase = phases.Count == 0 ? "sailing" : string.Join("+", phases);

        var stave = world.Battle.Occasion == BattleOccasion.None
            ? "none"
            : BattleSim.OccasionText(world.Battle.Occasion);

        return string.Format(CultureInfo.InvariantCulture,
            "tick={0} clock={1} phase={2} beast={3:0.##},{4:0.##}/{5}/hunger={6:0.##} terrain={17} resources={7} "
            + "debt={8:0.##}(policy={9}) rep=town:{10:0.##},star:{11:0.##} visitors={12}/{13} mana={14} "
            + "battle={15} conserved={16} airship={18} form_unlocked={19} flow={26} home={27} next={28} ms={29} "
            + "held={24} church={23} manual={25} "
            + "wages_due={20:0.##} arrears_days={21} debts={22}",
            world.Tick, ArenaTime.CompassAt(world.Tick), phase,
            beast.X, beast.Z, world.Voyage.Phase.ToString().ToLowerInvariant(), world.Voyage.Satiation,
            town.ResourcesText(),
            town.TotalDebt, town.Policy.ToString().ToLowerInvariant(),
            world.Visitors.TownReputation, HighestStarReputation(world),
            world.Visitors.InTownCount, world.Visitors.Cap,
            town.ManaText(),
            world.Battle.DebugText(),
            town.IsConserved(),
            BigMapTerrain.NameOf(BigMapTerrain.TerrainAt(beast)),
            ArenaSetup.AirshipMinutesLeft(world),
            FormUnlockedText(world),
            world.WagesDue, world.ArrearsDays, DebtsText(world),
            ChurchText(world),
            HeldText(world),
            world.Voyage.ManualTargetText(),
            RunStageText.TokenOf(world.Flow.Stage),
            CityOrDash(world.Flow.HomeCityId),
            CityOrDash(world.Flow.NextCityId),
            MilestoneText(world));
    }

    /// <summary>
    /// 手上正拿着哪一件：<c>held=&lt;类别&gt;:&lt;id&gt;</c>
    /// （类别是 <c>person</c> / <c>corpse</c> / <c>cargo</c>；手上没东西就写空串）。
    ///
    /// <para><b>两本册各管各的</b>：活物那一档读 <see cref="ArenaUnit.Held"/>，
    /// 点位物那一档读 <see cref="PointItem.Held"/> —— 「手上那一件」在谁身上就是谁的事，
    /// 这一格只是把两处读数并成一句。</para>
    ///
    /// <para><b>与 <c>church=</c> 同一形状</b>：键在、值空，读者一眼看得出「查过这一项、确实没有」。</para>
    /// </summary>
    private static string HeldText(ArenaWorld world)
    {
        foreach (var unit in world.Roster.Units)
        {
            if (unit.Held) return $"person:{unit.Id}";
        }

        foreach (var item in world.Items.Live)
        {
            if (item.Held) return $"{item.KindText}:{item.Id}";
        }

        return string.Empty;
    }

    /// <summary>
    /// 教堂在册尸体那一格：<c>church=&lt;尸体 id:人 id:游戏分钟,…&gt;</c>
    /// （按入册次序，逗号隔开；一具都没有就写空串）。
    ///
    /// <para><b>与 <c>debts=</c> 同一形状</b>：键在、值空，读者一眼看得出「查过这一项、确实没有」。</para>
    /// </summary>
    private static string ChurchText(ArenaWorld world)
    {
        var parts = new List<string>();
        foreach (var c in world.ChurchCorpses)
        {
            parts.Add(string.Format(CultureInfo.InvariantCulture, "{0}:{1}:{2}",
                c.CorpseId, c.Name, c.AgeGameMinutes));
        }

        return string.Join(",", parts);
    }

    /// <summary>
    /// 负债那一格：<c>debts=&lt;势力:余额,…&gt;</c>（按登记次序，逗号隔开；一笔债都没有就写空串）。
    ///
    /// <para><b>与 <c>form_unlocked=</c> 同一形状</b>：键名后面跟一串「名字:值」，
    /// 逗号隔开，没有就留空 —— 键在、值空，读者一眼看得出「查过这一项、确实没有」。</para>
    /// </summary>
    private static string DebtsText(ArenaWorld world)
    {
        var parts = new List<string>();
        foreach (var c in world.Town.Creditors)
        {
            if (c.Outstanding <= 0.0) continue;
            parts.Add($"{c.FactionId}:{c.Outstanding.ToString("0.##", CultureInfo.InvariantCulture)}");
        }

        return string.Join(",", parts);
    }

    /// <summary>城市 id 那一格：没有归属城 / 没有下一站就写 <c>-</c>（与 <c>RECRUIT dock=</c> 同一形状）。</summary>
    private static string CityOrDash(string cityId) => cityId.Length == 0 ? "-" : cityId;

    /// <summary>
    /// 四个里程碑步号：<c>glutton:&lt;步号&gt;,switch:&lt;步号&gt;,tier2:&lt;步号&gt;,final:&lt;步号&gt;</c>
    /// （没报过的是 <c>-1</c>）。
    ///
    /// <para><b>键在、值在</b>：四档都给，读者一眼看得出「查过这一项、确实没有」，
    /// 不必靠「少了一格」去推。</para>
    /// </summary>
    private static string MilestoneText(ArenaWorld world) => string.Format(
        CultureInfo.InvariantCulture, "glutton:{0},switch:{1},tier2:{2},final:{3}",
        world.Flow.GluttonUnlockedTick, world.Flow.FirstSwitchTick,
        world.Flow.Tier2Tick, world.Flow.FinalVictoryTick);

    /// <summary>
    /// 两个可解锁形态的旗标：<c>desert:0/1,glutton:0/1</c>（1 = 已解锁，原始态开局就是解锁的，不列）。
    /// </summary>
    private static string FormUnlockedText(ArenaWorld world)
        => string.Format(CultureInfo.InvariantCulture, "desert:{0}/1,glutton:{1}/1",
            Bool(world.Growth.FormUnlocked(BeastForm.Desert)),
            Bool(world.Growth.FormUnlocked(BeastForm.Glutton)));

    /// <summary>
    /// 巨兽段：位阶、形态、进阶仪式的剩余游戏分钟、两个解锁进度，
    /// 外加生态的三项读数（饱食度、消化进度 / 溢出、魔晶在甲板 / 搬运中 / 仓库三处）。
    ///
    /// <para><b>段首身份是 <c>BEAST beast</c> 两个 token</b>，不是 <c>tier=1</c> ——
    /// L2 语义档拿「段首两个 token」当身份，进阶只改第三段之后的值；
    /// 拿 <c>tier</c> 当身份的话，一进阶就在结构层判红。</para>
    /// </summary>
    public static string BeastSegment(ArenaWorld world)
    {
        var ritual = world.Growth.InRitualAt(world.Tick)
            ? ((world.Growth.RitualUntilTick - world.Tick) / ArenaTime.StepsPerGameMinute).ToString(CultureInfo.InvariantCulture)
            : "-";

        return string.Format(CultureInfo.InvariantCulture,
            "BEAST beast tier={0} form={1} ritual={2} dwell={3}/{4} fed={5:0.##}/{6} "
            + "sat={7:0.##} dig={8:0.##} ovf={9:0.##} crystal={10}/{11}/{12}",
            (int)world.Growth.Tier, BeastFormText.TokenOf(world.Growth.Form), ritual,
            world.Growth.DwellGameMinutes, ArenaTuning.DesertFormDwellGameMinutes,
            world.Growth.FedTotalFood, ArenaTuning.GluttonFormFedTotalFood,
            world.Ecology.Satiation, world.Ecology.DigestionProgress, world.Ecology.DigestionOverflow,
            world.CrystalOnDeck, world.CrystalCarrying, world.CrystalWarehouse);
    }

    /// <summary>建筑段：一栋一段（段首 <c>B &lt;建筑id&gt; &lt;蓝图id&gt;</c> 是身份，不改）。</summary>
    public static IEnumerable<string> BuildingSegments(ArenaWorld world)
    {
        foreach (var id in world.Construction.Ids)
        {
            var b = world.Construction.Find(id);
            if (b is null) continue;

            var posts = world.Roster.PostedCount(id);
            var maxPosts = world.Roster.PostCapacity(b.BlueprintId);
            var lifted = IsLifted(world, id);
            var breached = id == ArenaWorld.TownHallId && world.Siege.Breached;
            var col = b.Anchor?.Col ?? 0;
            var row = b.Anchor?.Row ?? 0;

            yield return string.Format(CultureInfo.InvariantCulture,
                "B {0} {1} payload={2:0.##}/{3:0.##},{4},posts={5}/{6},lifted={7},breached={8} pos={9},{10}",
                b.Id, b.BlueprintId, b.Health, b.MaxHealth, StateTextOf(b), posts, maxPosts,
                Bool(lifted), Bool(breached), col, row);
        }
    }

    /// <summary>土壤段：8 格固定输出（段首 <c>SOIL &lt;象限&gt;_&lt;层&gt;</c>）。</summary>
    public static string SoilSegment(ArenaWorld world) => world.Soil.DebugSegment();

    /// <summary>
    /// 留名账那一段：先 <c>STAR sales=&lt;留着的条数&gt; issued=&lt;一生成交笔数&gt;</c> 一行，
    /// 再按成交序一笔一段 <c>SALE &lt;人id&gt; tick=.. gold=.. rep=.. stats=..</c>。
    ///
    /// <para>那一对读数把「定长掉了几条」摆在明面上：<c>issued</c> 一直涨、
    /// <c>sales</c> 停在 <see cref="ArenaWorld.StarSaleLedgerSize"/>，
    /// 差值就是已经从账上掉出去的成交笔数，不靠翻源码才看得出来。</para>
    /// </summary>
    public static string StarSaleSegment(ArenaWorld world)
    {
        var lines = new List<string>(world.StarSales.Count + 1);
        lines.Add(string.Format(CultureInfo.InvariantCulture,
            "STAR sales={0} issued={1}", world.StarSaleCount, world.StarSaleIssued));
        foreach (var s in world.StarSales)
        {
            lines.Add(string.Format(CultureInfo.InvariantCulture,
                "SALE {0} tick={1} gold={2:0.##} rep={3:0.##} stats={4:0.##}",
                s.PersonId, s.Tick, s.Gold, s.PersonalReputation, s.StatTotal));
        }

        return string.Join(" | ", lines);
    }

    /// <summary>
    /// 招募额度那一段：<c>RECRUIT left=&lt;余量&gt; used=&lt;本停已招&gt; per_dock=&lt;每停上限&gt; dock=&lt;城市id&gt;</c>。
    ///
    /// <para>余量是「检查点要能判出这一停还能不能招」的那一格：没有它，
    /// 同一份快照在招满与没招满两种状态下逐字相同。</para>
    /// </summary>
    public static string RecruitSegment(ArenaWorld world)
        => string.Format(CultureInfo.InvariantCulture,
            "RECRUIT left={0} used={1} per_dock={2} dock={3}",
            world.RecruitLimitLeft, world.RecruitedThisDock, ArenaTuning.RecruitPerDock,
            world.IsDocked ? world.DockedCityId : "-");

    /// <summary>
    /// 战报那一段（<b>一行</b>）：
    /// <c>R occasion=&lt;场合&gt; outcome=&lt;胜负&gt; a=&lt;甲队上场&gt; b=&lt;乙队上场&gt;
    /// dead=&lt;死亡&gt; wounded=&lt;重伤&gt; looted=&lt;被抢件数&gt; ticket=&lt;门票收入&gt; tick=&lt;收场步号&gt;</c>。
    ///
    /// <para><b>只进确定性结算数字</b>：这是收场那一刻写下的那一份读数（<see cref="BattleSim.LastReport"/>），
    /// 不含特效 / cue / 动画等表现层读数。<b>没打过仗也占一行</b>（全 0、tick=-1）——
    /// 段数稳定，重放三层逐字与语义两档才对得上。</para>
    /// </summary>
    public static string ReportSegment(ArenaWorld world)
    {
        var report = world.Battle.LastReport;
        if (report is null) return "R occasion=none outcome=none a=0 b=0 dead=0 wounded=0 looted=0 ticket=0 tick=-1";

        return string.Format(CultureInfo.InvariantCulture,
            "R occasion={0} outcome={1} a={2} b={3} dead={4} wounded={5} looted={6} ticket={7:0.##} tick={8}",
            BattleSim.OccasionText(report.Occasion), BattleSim.OutcomeText(report.Outcome),
            report.SideACount, report.SideBCount, report.DeadCount, report.WoundedCount,
            report.LootedCount, report.GateIncome, report.EndedTick);
    }

    // === 小工具 ===
    /// <summary>这一栋是不是处在「法术停滞」悬空态。</summary>
    public static bool IsLifted(ArenaWorld world, string buildingId)
    {
        foreach (var id in world.Deck.Lifted)
        {
            if (id == buildingId) return true;
        }

        return false;
    }

    private static double HighestStarReputation(ArenaWorld world)
    {
        var top = 0.0;
        foreach (var u in world.Roster.Units)
        {
            if (u.Role != UnitRole.Gladiator) continue;
            if (u.PersonalReputation > top) top = u.PersonalReputation;
        }

        return top;
    }

    private static string StateTextOf(Building b)
    {
        if (b.Paused) return "paused";
        return b.Progress.StateText;
    }

    private static string Bool(bool value) => value ? "1" : "0";

    /// <summary>调试读数（不进快照）：一句话读完六本账、井口、游客与交战。</summary>
    public static string Readout(ArenaWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);

        var sb = new StringBuilder();
        sb.Append(world.CompassText())
            .Append(' ').Append(world.Visitors.DebugText())
            .Append(' ').Append(world.Elevator.DebugText())
            .Append(' ').Append("HAUL ").Append(world.Haul.DebugText())
            .Append(' ').Append("FIGHT ").Append(world.Battle.DebugText())
            .Append(' ').Append("MANA ").Append(world.Town.ManaText())
            .Append(' ').Append("DEBT ").Append(world.Town.TotalDebt.ToString("0.##", CultureInfo.InvariantCulture));
        return sb.ToString();
    }
}
