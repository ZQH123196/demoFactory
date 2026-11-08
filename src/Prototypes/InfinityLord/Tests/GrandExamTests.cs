#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using DemoFactory.Prototypes.InfinityLord.Logic;
using Shouldly;
using Xunit;
using DemoFactory.GugaFramework.Tests;

namespace InfinityLord.Tests;

/// <summary>
/// 切片 10「终局」第 5 步 — 生死线大考实验（design.md 4 节命题的正式回答）。
///
/// 命题：「仅靠发悬赏给行为模式可预期的 AI，能否策略通关」——切片 10 之前这句没有操作定义
/// （没有可输的判定、没有可通的目标）。外环落地后，考题变成可跑的：
/// **固定种子下，代理玩家按策略跑完整局，宫殿存活 + 摧毁 BOSS 巢穴 = 通关。**
///
/// 三条对照（同一张剧本曲线、同一份守恒不变量，只差玩家动词）：
/// - 策略 1 全动词：挂赏 + 营建 + 行会升级（大考口径 = 最接近真实玩家的全部手段）
/// - 策略 2 只挂赏：内环单动词（切片 10 前的世界只有这一个动词 — 现在拿它考外环）
/// - 策略 3 零干预：对照组（design.md 3 节a 已证：英雄灭绝 + 威胁无界）
/// 用户确认：**输与赢都是有效结论**，但都必须「可读」——
/// 所以每条都出报告：终局判定 + 时刻 + 宫殿血量轨迹 + 金库 + 英雄构成 + 赏金支出。
/// 时长对玩家口径一律秒/分钟；内部步数（1 步 = 0.1 秒）只在代码层。
/// </summary>
public class GrandExamTests : LogicWorldTestBase
{
    /// <summary>一局时长：8 分钟（4800 步）— 用户确认「没必要弄太长」。</summary>
    private const int RunTicks = 4800;

    /// <summary>采样窗口：每 1 分钟一行。</summary>
    private const int SampleEvery = 600;

    /// <summary>代理玩家决策节奏：每 3 秒一次。</summary>
    private const int DecideEvery = 30;

    /// <summary>
    /// 「近身威胁」半径（世界单位）：进入玩家建筑群周边的怪优先处理。
    ///
    /// <para>25 是快世界那一版定的。修正移速之后怪在城下聚得久、决策每 3 秒一次，
    /// 25 米这一圈要挂的旗远远多于金库的收入 —— 实测 4:00 已花掉 460、金库只剩 42，
    /// 终幕那一面旗永远开不出来。收到 14 米：仍在「城下」这一档内，钱留给终幕。</para>
    /// </summary>
    private const float DefenseRadius = 14f;

    /// <summary>
    /// 攻坚前必须留在金库里的守城余量（$）。
    ///
    /// <para>150 是快世界那一版定的（那时怪几步就到城下，余量要够）——修正移速之后怪走 60 米要 500 步，
    /// 同一个余量把 BOSS 那一面旗的门槛顶到 <c>108 + 150 = 258</c>，而实测金库峰值只有 202~242
    /// ⇒ <b>攻坚队从没出发，BOSS 一滴血没掉</b>。守城余量按「够挂一只城下地精」定：20 ——
    /// 实测这一档下全动词的城下照样守得住（宫殿全程 8000），而终幕旗在 3:00~3:30 就开得出来。</para>
    /// </summary>
    private const int DefenseReserve = 20;

    /// <summary>
    /// 营建候选点（确定性排布 — 距既有建筑 ≥12 的第一个空位）。
    ///
    /// <para><b>全部落在战场网格内</b>（<c>InfinityLordNavGrid</c>：X∈[-20,90]、Z∈[-30,50]）。
    /// 早先那一版写的是 (-50,-50) / (-50,-25) / (-25,-50) 这类<b>界外坐标</b>，
    /// 而营建口不校边界 ⇒ 战士公会真被建在 (-50,-50)：出来的英雄生成在界外 (-35,-25) 再也走不回来，
    /// 连带 140 只追它们的怪报「地形无路」（实测 279 个在场单位里 153 个卡住）。
    /// 新候选点定在镇子外围一圈，与种子建筑都留得下 12 米间距。</para>
    /// </summary>
    private static readonly Vector2[] BuildCandidates =
    [
        new(-5, -25), new(-5, 30), new(25, -20), new(25, 20), new(45, 0),
        new(15, 35), new(15, -28), new(60, 0), new(0, 40), new(0, -28),
    ];

    [Fact]
    public void 大考1_全动词策略通关()
    {
        var (outcome, tick) = RunExam("grand-exam-full-verb-report.md", "策略 1 全动词（挂赏 + 营建 + 行会升级）", FullVerbPolicy);

        // 判据不变（设计命题的「能赢」半边）。测量版本：修正移速 + 本夹具修完两处闸之后的那一版
        // —— 波次时刻 / 供给 / 终幕都按新移速重新定过档（见 LogicWorld 那几条常量上的注释）。
        // 这一版的形状读数（`grand-exam-full-verb-report.md`）：宫殿全程 8000 满血、同屏怪 21~44、
        // 英雄 7~8 名（游侠 4 + 骑士 3~4）、BOSS 600 血被逐步磨掉，7:09 通关。
        outcome.ShouldBe(GameOutcome.Victory, $"生死线命题的「能赢」半边：全动词策略必须在一局内打掉 BOSS（实测 {FormatTime(tick)} 通关）");
    }

    [Fact]
    public void 大考2_只挂赏的结局入档()
    {
        var (outcome, tick) = RunExam("grand-exam-bounty-only-report.md", "策略 2 只挂赏（内环单动词）", BountyOnlyPolicy);

        // 断言的是**实测结论**而非设计目标：这一版测量下内环单动词也能通关（7:45，宫殿全程满血，
        // 4 名游侠、无营建无升级）—— 与「全动词」（7:09、8 名英雄、有骑士）只差 36 秒，
        // 也就是说营建与升级这条纵向线现在只买到 36 秒的余量：外环还没有陡到逼出新动词。
        // 若后续把外环调陡使本断言翻红，那正是「外环开始逼出新动词」的信号 —— 见 design.md「当前调参基准」。
        outcome.ShouldBe(GameOutcome.Victory, $"当前曲线下的测量结论：只挂赏也能通关（{FormatTime(tick)}）");
        outcome.ShouldNotBe(GameOutcome.Running, "不得悬而未决");
    }

    [Fact]
    public void 大考3_零干预判负且原因可读()
    {
        var (outcome, tick) = RunExam("grand-exam-neglect-report.md", "策略 3 零干预（对照组）", () => { });

        // 迁移前是 NUnit 的聚合式 Multiple：一个 lambda 里多条断言一起跑，一次报全。
        // xUnit 的 Multiple 收的是「多个委托、最后统一跑」，形态与语义都不同。
        // 这里是纯读断言、无先后依赖，顺序执行与聚合执行等价（只是首条红就停，不再一次报全）。
        // 测量版本同大考1：这一版的形状读数是宫殿 6:00 掉到 6505、6:36 被推平，同屏怪 15~29。

        outcome.ShouldBe(GameOutcome.Defeat, "零干预必败 — 失败条件真实可达");
        tick.ShouldBeLessThan(RunTicks, "且在一局上限之内");
        World.Buildings.Any(b => b.Kind == BuildingKind.Palace).ShouldBeFalse("判负原因可读：宫殿被推平（不是超时、不是断言崩溃）");    }

    // === 实验脚手架 ===

    /// <summary>
    /// 跑一条大考曲线：每 3 秒决策一次、每 1 分钟采样一行、每个采样点断言金币守恒，
    /// 报告落 Tests 输出目录。返回终局判定与判定时刻（判定后时间轴继续跑满 — 拆冻结 2026-09-23）。
    /// </summary>
    private (GameOutcome Outcome, long Tick) RunExam(string fileName, string label, Action decide)
    {
        var report = new StringBuilder();
        report.AppendLine($"# 生死线大考 — {label}（seed 42，一局 {RunTicks / 600} 分钟上限）");
        report.AppendLine();
        report.AppendLine("| 时刻 | 判定 | 金库 | 英雄(游/战/骑) | 怪 | 宫殿血 | 玩家建筑 | 波次 | BOSS血 | 赏金Σ | 击杀Σ | 阵亡Σ |");
        report.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");

        var endedAtTick = -1;
        for (var block = 0; block < RunTicks / SampleEvery && endedAtTick < 0; block++)
        {
            for (var d = 0; d < SampleEvery / DecideEvery; d++)
            {
                decide();
                // 判定即记账（2026-09-23 拆冻结）：Outcome 非 Running 后停决策循环，
                // 时间轴继续跑满（或跑满一局上限）— 采样照常，曲线能看到判负后的残余演化
                if (World.Outcome != GameOutcome.Running && endedAtTick < 0)
                    endedAtTick = (int)World.Tick;
                World.Advance(DecideEvery);
                if (World.Outcome != GameOutcome.Running && endedAtTick < 0)
                    endedAtTick = (int)World.Tick;
            }

            var heroes = World.Units.Where(u => u.Side == Faction.Hero && u.Health > 0).ToList();
            var mobs = World.Units.Count(u => u.Side == Faction.Monster && u.Health > 0);
            var palace = World.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.Palace);
            var boss = World.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.BossLair);
            var paid = Log.Bounties.Where(b => b.Kind == BountyEventKind.Paid).Sum(b => (long)b.Gold);
            var kills = Log.Combats.Count(c => c.TargetDied && c.TargetId.StartsWith("mob_"));
            var deaths = Log.Deaths.Count(d => d.Side == Faction.Hero);

            report.AppendLine(
                $"| {FormatTime(World.Tick)} | {World.Outcome} | {World.Treasury} " +
                $"| {heroes.Count}({heroes.Count(h => h.Class == HeroClass.Ranger)}/{heroes.Count(h => h.Class == HeroClass.Warrior)}/{heroes.Count(h => h.Class == HeroClass.Knight)}) " +
                $"| {mobs} | {palace?.Health ?? 0} | {World.Buildings.Count(b => b.Owner == Faction.Hero)} " +
                $"| {Log.Waves.Count} | {boss?.Health ?? 0} | {paid} | {kills} | {deaths} |");

            AssertConservation($"（第 {World.Tick} 步 — 大考 {label} 中途）");
        }

        report.AppendLine();
        report.AppendLine("## 终局");
        report.AppendLine($"- 判定：**{World.Outcome}**（判定时刻 {FormatTime(endedAtTick)} / 一局上限 {FormatTime(RunTicks)}；拆冻结后时间轴跑满全程）");
        report.AppendLine($"- 宫殿：{(World.Buildings.Any(b => b.Kind == BuildingKind.Palace) ? "存活 " + World.Buildings.First(b => b.Kind == BuildingKind.Palace).Health + " 血" : "被毁 = 判负")}");
        var bossAtEnd = World.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.BossLair);
        report.AppendLine($"- BOSS 巢穴：{(bossAtEnd is null ? (Log.BossArrivals.Count > 0 ? "已被摧毁 = 判胜" : "未降临") : "在场 " + bossAtEnd.Health + " 血")}");
        report.AppendLine($"- 波次到达 {Log.Waves.Count}/{LogicWorld.WaveTable.Length}；金库 {World.Treasury}；" +
            $"赏金支出 Σ{Log.Bounties.Where(b => b.Kind == BountyEventKind.Paid).Sum(b => b.Gold)}；" +
            $"退款 Σ{Log.Bounties.Where(b => b.Kind == BountyEventKind.Refunded).Sum(b => b.Gold)}");
        report.AppendLine($"- 英雄阵亡/离队 {Log.Deaths.Count(d => d.Side == Faction.Hero)} 名；营建 {Log.BuildingPlaced.Count} 座；行会升级 {Log.Count(EconomyEventKind.GuildUpgraded)} 次");
        report.AppendLine();
        report.AppendLine("## 终局快照");
        report.AppendLine("```");
        foreach (var line in World.DebugSnapshot().Split('\n')) report.AppendLine(line);
        report.AppendLine("```");

        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        File.WriteAllText(path, report.ToString());
        TestLog.Ledger($"大考报告已写入: {path}");
        TestLog.Ledger($"RESULT {label}: outcome={World.Outcome} tick={World.Tick} treasury={World.Treasury}");

        return (World.Outcome, World.Tick);
    }

    /// <summary>内部步数 → 玩家口径的 mm:ss（1 步 = 0.1 秒）。</summary>
    private static string FormatTime(long tick)
    {
        var seconds = tick / 10;
        return $"{seconds / 60}:{seconds % 60:00}";
    }

    // === 代理玩家策略（确定性，无随机） ===

    /// <summary>
    /// 策略 1 · 全动词：终幕前走「防守 + 基建」两条线；终幕一开始钱全部留给攻坚（停止营建与清剿巢穴）。
    /// 定价一律取「建议价」（= 恰好让满血基准英雄过勇气阈值），因为代理玩家没有行情直觉。
    /// </summary>
    private void FullVerbPolicy()
    {
        var bossPhase = World.Buildings.Any(b => b.Kind == BuildingKind.BossLair);
        HuntAndDefend(bossPhase);
        if (!bossPhase) BuildAndUpgrade(); // 终幕 = 机会成本决策的时刻：基建投资要让位于大赏
    }

    /// <summary>策略 2 · 只挂赏：与策略 1 同一条打怪防守线，但不碰营建与升级（内环单动词）。</summary>
    private void BountyOnlyPolicy() => HuntAndDefend(false);

    /// <summary>防守/清剿/终幕的挂赏线（三种策略共用 — 唯一动词 = 出价）。</summary>
    private void HuntAndDefend(bool bossPhase)
    {
        var playerBuildings = World.Buildings.Where(b => b.Owner == Faction.Hero).ToList();
        if (playerBuildings.Count == 0) return;

        // 1) 终幕攻坚：一面旗只有一个承接者 → 凑三面旗拉三名攻坚手上去（终幕是决胜局，
        //    真实玩家会把能派的都派上去，不是只派两名）；
        //    但攻坚不梭哈 —— 金库必须留出守城余量（大考实测：钱打空 = 护卫潮无人处理 = 宫殿陷落）
        var boss = World.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.BossLair);
        if (boss is not null)
        {
            var price = BossFlagPrice(boss);
            var active = World.Flags.Count(f => !f.Settled && f.TargetId == boss.Id);
            if (price > 0 && active < 3 && World.Treasury >= price + DefenseReserve)
            {
                World.PlaceBountyFlag(boss.Id, price);
                return;
            }
        }

        // 2) 近身威胁：离玩家建筑最近的一只，按建议价挂
        var nearest = World.Units
            .Where(u => u.Side == Faction.Monster && u.Health > 0)
            .Select(m => (Mob: m, Dist: playerBuildings.Min(b => Vector2.Distance(m.Position, b.Position))))
            .Where(x => x.Dist < DefenseRadius)
            .OrderBy(x => x.Dist)
            .FirstOrDefault();
        if (nearest.Mob is not null && !HasActiveFlag(nearest.Mob.Id))
        {
            var price = World.SuggestedPrice(nearest.Mob.Id);
            if (price > 0 && World.Treasury >= price)
            {
                World.PlaceBountyFlag(nearest.Mob.Id, price);
                return;
            }
        }

        // 3) 有余力才压源头：**城下干净**才分兵（原先只看金库 ≥150）
        //    —— 实测这一条是「自己把城抽空」的元凶：1:00 起就分兵去打巢穴，
        //    同屏怪物从 15 涨到 116，宫殿在 6:00 被推平。代理玩家至少要有「城下有人就不出门」的常识。
        var townClear = !World.Units.Any(u => u.Side == Faction.Monster && u.Health > 0
            && playerBuildings.Min(b => Vector2.Distance(u.Position, b.Position)) < DefenseRadius);
        if (!bossPhase && townClear && World.Treasury >= 150)
        {
            var lair = World.Buildings
                .Where(b => b.Kind == BuildingKind.MonsterLair && b.Health > 0)
                .OrderBy(b => Vector2.Distance(b.Position, playerBuildings[0].Position))
                .FirstOrDefault();
            if (lair is not null && !HasActiveFlag(lair.Id))
            {
                var price = World.SuggestedPrice(lair.Id);
                if (price > 0 && World.Treasury >= price) World.PlaceBountyFlag(lair.Id, price);
            }
        }
    }

    /// <summary>
    /// 终幕那一面旗的定价：按「手上最好请的那名英雄刚好算得过来」开价，不按建议价。
    ///
    /// <para><b>为什么不按建议价</b>：<c>World.SuggestedPrice</c> 给的是「满血基准英雄」的价
    /// （BOSS 威胁固定档 90 ⇒ $108）。而终幕时英雄早有等级与装备，感知威胁随战力下降
    /// （战力 46 的游侠眼里 BOSS 只剩 54.8）—— 照建议价开等于按新兵价付钱，
    /// 一面旗 108 加守城余量把攻坚队锁死在城里（实测金库峰值 202~242 < 258，BOSS 一滴血没掉）。</para>
    ///
    /// <para>算式与 <c>LogicWorld.TryClaimFlag</c> 同源：够不够勇看
    /// <c>赏金 ÷ 感知威胁 ÷ 士气系数 ≥ 勇气比 × 职业系数 ÷ 血量比</c>，
    /// 所以开价 = 感知威胁 × 士气系数 × 阈值。挑「感知威胁最低」的那一名 = 最好请的那一名。</para>
    /// </summary>
    private int BossFlagPrice(Building boss)
    {
        if (World.EstimateThreat(boss.Id) is not { } baseThreat) return 0;

        var best = World.Units
            .Where(u => u.Side == Faction.Hero && u.Health > 0)
            .OrderBy(u => LogicWorld.PerceivedThreat(u, baseThreat))
            .FirstOrDefault();
        if (best is null) return 0;

        var perceived = LogicWorld.PerceivedThreat(best, baseThreat);
        var hpFactor = best.Health / (float)best.MaxHealth;
        var moraleFactor = best.Morale < LogicWorld.MoraleDrinkThreshold
            ? 1f + LogicWorld.MoralePenaltyMax * (LogicWorld.MoraleDrinkThreshold - best.Morale)
                / LogicWorld.MoraleDrinkThreshold
            : 1f;
        var threshold = LogicWorld.BraveryRatio * LogicWorld.ClassBraveryFactor(best.Class) / hpFactor;
        return (int)MathF.Ceiling(perceived * threshold * moraleFactor);
    }

    /// <summary>
    /// 基建线：先建一座战士公会（它是出骑士的唯一入口），再把它升一格出骑士（精英线，应对波次曲线的硬单）。
    /// 门槛留余量：升级要 1.2 倍价款、建会要 1.5 倍价款 —— 代理玩家也不梭哈。
    ///
    /// <para><b>建会门槛按「有没有这座行会 + 钱够不够」判，不按活着的英雄数</b>：行会名额封顶 4/会，
    /// 满编时活人也只有 4 名，而种子行会一直在补员 ⇒ 拿活人数当门槛等于永不建会。
    /// 实测后果：1:00 起场上就有 7~8 名英雄（含骑士线未开时的游侠），大考1 与「只挂赏」因此跑成了
    /// 同一条策略，两组对照的强度是假的。</para>
    /// </summary>
    private void BuildAndUpgrade()
    {
        var upgradeable = World.Buildings.FirstOrDefault(b =>
            b.Kind == BuildingKind.GuildWarrior && b.Level == 1);
        if (upgradeable is not null && World.Treasury >= LogicWorld.GuildUpgradeCost * 6 / 5)
        {
            if (World.UpgradeGuild(upgradeable.Id)) return;
        }

        var warriorGuilds = World.Buildings.Count(b => b.Kind == BuildingKind.GuildWarrior && b.Health > 0);
        var cost = World.NextPlaceCost(BuildingKind.GuildWarrior);
        if (warriorGuilds == 0 && cost > 0 && World.Treasury >= cost * 3 / 2 + DefenseReserve)
        {
            var spot = BuildCandidates.FirstOrDefault(p => SpotIsFree(p));
            if (spot != default) World.PlaceBuilding(BuildingKind.GuildWarrior, spot);
        }
    }

    /// <summary>候选点是否满足营建间距（与逻辑层 PlaceBuilding 校验同源 — 只为挑位置，不判金库）。</summary>
    private bool SpotIsFree(Vector2 position)
        => World.Buildings.All(b => b.Health <= 0
            || Vector2.Distance(b.Position, position) >= LogicWorld.MinPlaceDistance);

    private bool HasActiveFlag(string targetId)
        => World.Flags.Any(f => !f.Settled && f.TargetId == targetId);
}
