#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using DemoFactory.GugaFramework.Construction;
using DemoFactory.GugaFramework.Pathfinding;
using DemoFactory.GugaFramework.Replay;

namespace DemoFactory.Prototypes.BeastArena.Logic;

// 四十五个业务 `inject` 动词 + 重放装配 —— 只留这一侧该留的：动词名单、每个动词那一次调用、
// 推进到目标步的委托。公共那半（参数编码与规范化、写路径守卫、Journal 追加、检查点守卫、
// 三钩子工厂、脚本导出）全在 `ReplayAssembly` 一份。
//
// 与切片 1/2 那份 `ArenaSetup` 并存：那份的词表是旧设计（固定航线 + 箭塔/减速陷阱），
// 这一份是新版的这四十多个动词。ticket 13 做清理时把两份合成一份。
//
// **拒绝走 `rejected: <RejectReason>` 那一行，不抛异常**：那 14 项是封闭枚举（`RejectReason`），
// 参数缺失、段数不对、取值越界都归 `InvalidArgs`。原因不进 Journal（`ok` 才是权威），
// 而成功侧的 `canonicalArgs` 走 `ReplayArgs.Join` / `Number` / `Int`（不变文化）——
// Journal 记的是规范化后的那一串，同一逻辑事实在账上只有一种 token。

/// <summary>招人来路的五个档（标识符与中文名一一对应）。</summary>
public enum RecruitSource
{
    /// <summary>城乡招（忠诚初值 60，100 金币）。</summary>
    TownOrCountry = 0,

    /// <summary>买奴隶（忠诚初值 30，200 金币）。</summary>
    BuySlave = 1,

    /// <summary>捉游民（忠诚初值 40，0 金币）。</summary>
    CatchVagrant = 2,

    /// <summary>俘强盗（忠诚初值 20，0 金币）。</summary>
    CaptureBandit = 3,

    /// <summary>捕野兽（忠诚初值 10，150 金币）。</summary>
    CatchBeast = 4,
}

/// <summary>一条卖角斗士的留名记录（成交之后该角色在本巨兽上不再出现）。</summary>
/// <param name="PersonId">角色 id。</param>
/// <param name="Tick">成交那一刻的步号。</param>
/// <param name="Gold">成交价（金币）。</param>
/// <param name="PersonalReputation">成交时的个人名声。</param>
/// <param name="StatTotal">成交时的三项本事总和。</param>
public sealed record StarSaleRecord(
    string PersonId,
    long Tick,
    double Gold,
    double PersonalReputation,
    double StatTotal);

/// <summary>
/// 进阶按钮那一档的拦路 —— <b>十档，与 <see cref="ArenaWorld.AdvanceTier"/> 的判定顺序一一对应</b>。
///
/// <para>为什么不直接回 <see cref="RejectReason"/>：那 13 项里「不在停靠 / 正在打仗 / 停摆 / 仪式中」
/// 四档都是 <see cref="RejectReason.InvalidPhase"/>，而界面要分头写原因（玩家看到的是四句不同的话），
/// 所以界面这一侧另有一份更细的判据；两者的对应关系由
/// <see cref="ArenaSetup.RejectReasonOf"/> 收在一处。</para>
///
/// <para><b>权威那一处在 <see cref="ArenaWorld.AdvanceTier"/></b>：本枚举是它的只读镜像，
/// 供面板在不动世界的前提下问「现在按下去会被拒在哪一档」。两处判定顺序必须一致
/// （<c>Tests/BeastPanelTests.cs</c> 逐档对照）。</para>
/// </summary>
public enum AdvanceBlocker
{
    /// <summary>没拦：可以进阶。</summary>
    None = 0,

    /// <summary>已经到最高位阶（三阶）。</summary>
    TierAtMax = 1,

    /// <summary>不在停靠期。</summary>
    NotDocked = 2,

    /// <summary>正在打仗（办赛或防守）。</summary>
    InBattle = 3,

    /// <summary>停摆期（镇政府被攻破之后那几天）。</summary>
    InStandstill = 4,

    /// <summary>进阶仪式期间。</summary>
    InRitual = 5,

    /// <summary>门槛没过（二阶：第二层 + 洞穴清剿 + 魔晶嵌入，三项缺一不可）。</summary>
    GateNotMet = 6,

    /// <summary>魂石不够。</summary>
    NotEnoughSoulStone = 7,

    /// <summary>特殊材料不够。</summary>
    NotEnoughMaterial = 8,

    /// <summary>金币不够。</summary>
    NotEnoughCoin = 9,
}

/// <summary>
/// 巨兽竞技场 MVP 那一侧的词表与重放装配。
///
/// <para><b>失败带原因返回 false，绝不抛</b>：数字参数解析不出就让整条命令失败，
/// <b>不降级、不兜 0</b>（「桥只转发」）。</para>
///
/// <para><b>扣钱先判「能不能成」再扣</b>；扣完发现落不成，原路退回
/// （<c>Refund*</c> 记进产出、不记消耗）—— 八条守恒等式才不会破。</para>
/// </summary>
public static class ArenaSetup
{
    /// <summary>词表全名（44 条业务动词 + 合成输入那两条）。加动词改这一处并加 <see cref="Inject"/> 的 case。</summary>
    public static readonly string[] VerbNames =
    [
        // 航行与城镇（7）
        "StartRun",
        "Dock",
        "SailTo",
        "PullAnchor",
        "StartMatch",
        "TradeAirship",
        "SteerOutOfBounds",

        // 上帝之手与法术（8）
        "GrabEntity",
        "DropToBuilding",
        "DropToDeck",
        "LiftBuilding",
        "DropBuilding",
        "SummonHelper",
        "DismissHelper",
        "CastHeal",

        // 建造与土壤（5）
        "PlaceBuilding",
        "RemoveBuilding",
        "RepairBuilding",
        "ExcavateSoil",
        "EmbedCrystal",

        // 人口与经营（7）
        "Recruit",
        "ConvertRole",
        "GrantFreedom",
        "SellStar",
        "AssignPost",
        "SetNonCombatant",
        "ReviveCorpse",

        // 账目与远行（6）
        "SetPricing",
        "BuySupply",
        "PayDebt",
        "SetDebtPolicy",
        "PayWages",
        "Teleport",

        // 巨兽本体与生态（8）
        "AdvanceTier",
        "SwitchForm",
        "Feed",
        "NpcFeed",
        "EatTarget",
        "RecallForage",
        "UseCrystal",
        "SellCrystal",

        // 地下战斗（2）：单处洞穴的发起与撤退
        "ClearCave",
        "RetreatCave",

        // 终局（2）：最终防守失败后的立即重试、结算后的继续经营
        "RetryFinalDefence",
        "ContinueBusiness",

        // 合成输入那两条：名字与形状在公共层 SyntheticVerbs 一份，重放端整条跳过。
        // 这里显式接住 —— 落到 default 会被判「不在词表」而返 false，而录制端记的是
        // ok=true ⇒ 重放端 ok 翻转（「断言三层」里任何档位都红）。
        .. SyntheticVerbs.Names,
    ];

    /// <summary>词表判定。</summary>
    public static bool IsVerb(string cmd) => Array.IndexOf(VerbNames, cmd) >= 0;

    /// <summary>给一局装上重放装配（建好不开录）。</summary>
    public static ReplayAssembly Recorder(ArenaWorld world)
        => new(world, world.Seed, VerbNames, InjectorFor(world), target => world.AdvanceTo(target));

    /// <summary>三个钩子一次填齐（重放端与取证器唯一的接线点）。</summary>
    public static ReplayRunner.Hooks Hooks(ArenaWorld world)
        => new ReplayAssembly(world, world.Seed, VerbNames, InjectorFor(world),
            target => world.AdvanceTo(target)).Hooks();

    private static ReplayInject InjectorFor(ArenaWorld world)
        => (string cmd, string args, out string canonical, out string reason)
            => Inject(world, cmd, args, out canonical, out reason);

    // === 词表上的小工具（测试与调试桥共用） ===

    /// <summary>拒绝文案：<c>rejected: &lt;RejectReason&gt;</c>。</summary>
    public static string RejectedText(RejectReason why) => $"{RejectPrefix} {why}";

    /// <summary>拒绝前缀（<c>rejected:</c>）—— 加它与剥它都在本文件这一处。</summary>
    public const string RejectPrefix = "rejected:";

    /// <summary>
    /// 原因串拆成两半：<b>枚举那一段</b>与<b>括号里那一段（细节与缺口数字）</b>。
    ///
    /// <para><b>前缀剥到没有为止</b>：<c>rejected:</c> 那一层由 <see cref="Bad"/> 加，
    /// 哪一层加的都剥掉（剥一遍就够，剥到没有为止更耐改）。</para>
    ///
    /// <para><b>拆的是同一份约定</b>：加前缀与加括号都在本文件这一处，拆也在这里 ——
    /// 表现层只管把拆出来的两半摆上屏，不自己再拼一遍格式。</para>
    /// </summary>
    /// <param name="reason">注入口回传的那一串（那一串带前缀与括号里的细节）。</param>
    /// <returns>枚举那一段（认不出就是 null）与细节那一段。</returns>
    public static (RejectReason? Reason, string Detail) SplitRejectText(string reason)
    {
        var token = (reason ?? string.Empty).Trim();
        while (token.StartsWith(RejectPrefix, StringComparison.Ordinal))
            token = token[RejectPrefix.Length..].Trim();

        var detail = string.Empty;
        var open = token.IndexOfAny(['（', '(']);
        if (open >= 0)
        {
            var close = token.LastIndexOfAny(['）', ')']);
            detail = close > open ? token[(open + 1)..close].Trim() : token[(open + 1)..].Trim();
            token = token[..open].Trim();
        }

        return (Enum.TryParse<RejectReason>(token, out var parsed) ? parsed : null, detail);
    }

    /// <summary>收购报价那一式：<c>500 + 个人名声 × 10 + 三本事总和 × 5</c>（不另设系数）。</summary>
    public static double StarOffer(ArenaUnit unit)
        => ArenaTuning.StarOfferBase
            + (unit.PersonalReputation * ArenaTuning.StarOfferReputationFactor)
            + (unit.StatTotal * ArenaTuning.StarOfferStatFactor);

    /// <summary>招募成本（五个来路各一档）。</summary>
    public static double RecruitCost(RecruitSource source) => source switch
    {
        RecruitSource.TownOrCountry => 100.0,
        RecruitSource.BuySlave => 200.0,
        RecruitSource.CatchBeast => 150.0,
        _ => 0.0,
    };

    /// <summary>这一来路招来的是工作人员还是打手（买奴隶与捕野兽来的是打手）。</summary>
    public static UnitRole RoleOf(RecruitSource source)
        => source is RecruitSource.BuySlave or RecruitSource.CatchBeast
            ? UnitRole.Gladiator
            : UnitRole.Worker;

    /// <summary>一份食物的基准价（金币）—— 城内价与空艇价都乘它。</summary>
    public const double FoodBasePrice = 10.0;

    /// <summary>
    /// 按某个物价系数算一份食物的价（金币）。
    /// 城内价 = 这一档 × <see cref="ArenaWorld.CityPriceFactor"/>；
    /// 空艇价 = 这一档 × <see cref="ArenaWorld.AirshipPriceFactor"/>。
    /// </summary>
    public static double FoodPriceOf(double priceFactor) => FoodBasePrice * priceFactor;

    /// <summary>
    /// 空艇卖一份食物的价（金币）—— <b>三城均价 × 空艇加价</b>。
    ///
    /// <para>数从哪儿来：贫城 0.8 + 富城 1.2 + 沙城 0.9 = 2.9，均值 0.9666…，
    /// × 空艇加价 <see cref="ArenaTuning.AirshipPriceFactor"/>（1.2）= 1.16，
    /// × 基准价 <see cref="FoodBasePrice"/>（10 金）= <b>11.6 金一份</b>。
    /// <b>野外锚地不是城</b>，不进这个均价（判据在
    /// <see cref="ArenaWorld.AirshipPriceFactor"/> 那一处）。</para>
    ///
    /// <para><b>这一份是面板上那个「空艇价」的唯一出处</b>：成交那一发按它扣钱，
    /// 面板按它显示 —— 两边不许各算一遍。</para>
    /// </summary>
    public static double AirshipFoodPrice => FoodPriceOf(ArenaWorld.AirshipPriceFactor);

    /// <summary>
    /// 空艇成交窗口还剩几个游戏分钟（不在窗口内是 0）。
    /// 面板那一行「剩余 N 游戏分钟」与快照里的 <c>airship=</c> 读的是同一口。
    /// </summary>
    public static int AirshipMinutesLeft(ArenaWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!world.InAirshipWindow) return 0;

        var left = world.AirshipWindowUntilTick - world.Tick;
        return left <= 0 ? 0 : (int)(left / ArenaTime.StepsPerGameMinute);
    }

    /// <summary>忠诚初值（五个来路各一档）。</summary>
    public static double LoyaltyOf(RecruitSource source) => source switch
    {
        RecruitSource.TownOrCountry => 60.0,
        RecruitSource.CatchVagrant => 40.0,
        RecruitSource.BuySlave => 30.0,
        RecruitSource.CaptureBandit => 20.0,
        _ => 10.0,
    };

    /// <summary>来路解析（认标识符也认中文名；认不出回 null，不降级）。</summary>
    public static RecruitSource? ParseRecruitSource(string token)
    {
        foreach (RecruitSource s in Enum.GetValues<RecruitSource>())
        {
            if (string.Equals(s.ToString(), token, StringComparison.Ordinal)) return s;
        }

        return token switch
        {
            "城乡招" => RecruitSource.TownOrCountry,
            "买奴隶" => RecruitSource.BuySlave,
            "捉游民" => RecruitSource.CatchVagrant,
            "俘强盗" => RecruitSource.CaptureBandit,
            "捕野兽" => RecruitSource.CatchBeast,
            _ => null,
        };
    }

    /// <summary>卖明星角斗士的留名记录归世界实例所有，读数口在 <see cref="ArenaWorld.StarSales"/>。</summary>
    // === 分发 ===

    /// <summary>跑一条写路径（不带原因与规范化串那一档）。</summary>
    public static bool Inject(ArenaWorld world, string cmd, string args)
        => Inject(world, cmd, args, out _, out _);

    /// <summary>跑一条写路径，并回传规范化后的 args（Journal 记的就是它）。</summary>
    public static bool Inject(ArenaWorld world, string cmd, string args, out string canonicalArgs)
        => Inject(world, cmd, args, out canonicalArgs, out _);

    /// <summary>跑一条写路径，回传规范化后的 args 与失败原因（成功时是空串）。</summary>
    public static bool Inject(
        ArenaWorld world, string cmd, string args, out string canonicalArgs, out string reason)
    {
        ArgumentNullException.ThrowIfNull(world);

        canonicalArgs = args;
        reason = string.Empty;

        switch (cmd)
        {
            // ---- 航行与城镇（7） ----

            case "StartRun":
                return StartRun(world, ref canonicalArgs, ref reason);

            case "Dock":
                return Dock(world, args, ref canonicalArgs, ref reason);

            case "SailTo":
                return SailTo(world, args, ref canonicalArgs, ref reason);

            case "PullAnchor":
                return PullAnchor(world, ref canonicalArgs, ref reason);

            case "StartMatch":
                return StartMatch(world, args, ref canonicalArgs, ref reason);

            case "TradeAirship":
                return TradeAirship(world, args, ref canonicalArgs, ref reason);

            case "SteerOutOfBounds":
                return SteerOutOfBounds(world, ref canonicalArgs, ref reason);

            // ---- 上帝之手与法术（8） ----

            case "GrabEntity":
                return GrabEntity(world, args, ref canonicalArgs, ref reason);

            case "DropToBuilding":
                return DropToBuilding(world, args, ref canonicalArgs, ref reason);

            case "DropToDeck":
                return DropToDeck(world, args, ref canonicalArgs, ref reason);

            case "LiftBuilding":
                return LiftBuilding(world, args, ref canonicalArgs, ref reason);

            case "DropBuilding":
                return DropBuilding(world, args, ref canonicalArgs, ref reason);

            case "SummonHelper":
                return SummonHelper(world, ref canonicalArgs, ref reason);

            case "DismissHelper":
                return DismissHelper(world, args, ref canonicalArgs, ref reason);

            case "CastHeal":
                return CastHeal(world, args, ref canonicalArgs, ref reason);

            // ---- 建造与土壤（5） ----

            case "PlaceBuilding":
                return PlaceBuilding(world, args, ref canonicalArgs, ref reason);

            case "RemoveBuilding":
                return RemoveBuilding(world, args, ref canonicalArgs, ref reason);

            case "RepairBuilding":
                return RepairBuilding(world, args, ref canonicalArgs, ref reason);

            case "ExcavateSoil":
                return ExcavateSoil(world, args, ref canonicalArgs, ref reason);

            case "EmbedCrystal":
                return EmbedCrystal(world, args, ref canonicalArgs, ref reason);

            // ---- 人口与经营（7） ----

            case "Recruit":
                return Recruit(world, args, ref canonicalArgs, ref reason);

            case "ConvertRole":
                return ConvertRole(world, args, ref canonicalArgs, ref reason);

            case "GrantFreedom":
                return GrantFreedom(world, args, ref canonicalArgs, ref reason);

            case "SellStar":
                return SellStar(world, args, ref canonicalArgs, ref reason);

            case "AssignPost":
                return AssignPost(world, args, ref canonicalArgs, ref reason);

            case "SetNonCombatant":
                return SetNonCombatant(world, args, ref canonicalArgs, ref reason);

            case "ReviveCorpse":
                return ReviveCorpse(world, args, ref canonicalArgs, ref reason);

            // ---- 账目与远行（6） ----

            case "SetPricing":
                return SetPricing(world, args, ref canonicalArgs, ref reason);

            case "BuySupply":
                return BuySupply(world, args, ref canonicalArgs, ref reason);

            case "PayDebt":
                return PayDebt(world, args, ref canonicalArgs, ref reason);

            case "SetDebtPolicy":
                return SetDebtPolicy(world, args, ref canonicalArgs, ref reason);

            case "PayWages":
                return PayWages(world, ref canonicalArgs, ref reason);

            case "Teleport":
                return Teleport(world, args, ref canonicalArgs, ref reason);

            // ---- 巨兽本体与生态（7） ----

            case "AdvanceTier":
                return AdvanceTier(world, ref canonicalArgs, ref reason);

            case "SwitchForm":
                return SwitchForm(world, args, ref canonicalArgs, ref reason);

            case "Feed":
                return Feed(world, args, ref canonicalArgs, ref reason);

            case "NpcFeed":
                return NpcFeed(world, ref canonicalArgs, ref reason);

            case "EatTarget":
                return EatTarget(world, args, ref canonicalArgs, ref reason);

            case "RecallForage":
                return RecallForage(world, ref canonicalArgs, ref reason);

            case "UseCrystal":
                return UseCrystal(world, args, ref canonicalArgs, ref reason);

            case "SellCrystal":
                return SellCrystal(world, args, ref canonicalArgs, ref reason);

            // ---- 地下战斗（2） ----

            case "ClearCave":
                return ClearCave(world, args, ref canonicalArgs, ref reason);

            case "RetreatCave":
                return RetreatCave(world, ref canonicalArgs, ref reason);

            // ---- 终局（2） ----

            case "RetryFinalDefence":
                return RetryFinalDefence(world, ref canonicalArgs, ref reason);

            case "ContinueBusiness":
                return ContinueBusiness(world, ref canonicalArgs, ref reason);

            // 合成输入那两条：校验参数后跳过、返 true（机制见 SyntheticVerbs.Skip）。
            case SyntheticVerbs.Action:
                return SkipSynthetic(args, SyntheticVerbs.ActionSegments, ref canonicalArgs, ref reason);

            case SyntheticVerbs.Click:
                return SkipSynthetic(args, SyntheticVerbs.ClickSegments, ref canonicalArgs, ref reason);

            default:
                return Bad(ref reason, RejectReason.InvalidArgs, $"命令「{cmd}」不在词表里");
        }
    }

    // === 航行与城镇（7） ===

    /// <summary>
    /// 开始游戏：零段动词。标题屏那一按走的是这一条写路径 ——
    /// 不走旁路，重放账里才有开局那一发。
    /// </summary>
    private static bool StartRun(ArenaWorld world, ref string canonicalArgs, ref string reason)
    {
        canonicalArgs = string.Empty;

        var rejected = world.StartRun();
        return rejected == RejectReason.InvalidArgs
            ? true
            : Bad(ref reason, rejected, "这一局已经开始了");
    }

    private static bool Dock(
        ArenaWorld world, string args, ref string canonicalArgs, ref string reason)
    {
        if (!ReplayArgs.Split(args, 1, out var one))
        {
            return Bad(ref reason, RejectReason.InvalidArgs, $"Dock 要 1 段（城市 id），收到 {Count(args)} 段");
        }

        var cityId = one[0];
        canonicalArgs = ReplayArgs.Join(cityId);
        var place = BigMapGrid.PlaceById(cityId);
        if (place is null)
        {
            return Bad(ref reason, RejectReason.InvalidArgs, $"{cityId} 不是大地图上的停靠点");
        }

        // 通缉只拒债主势力名下城镇的停靠（判定收在 ArenaWorld.CanDockTo，命令栏置灰读同一处）。
        if (!world.CanDockTo(cityId))
        {
            return Bad(ref reason, RejectReason.WantedRefused, WantedShortText(world, cityId, place.Name));
        }

        if (BlockedByRitual(world, ref reason, "进阶仪式期间巨兽匍匐不动"))
        {
            return false;
        }

        world.OrderSailTo(cityId);
        return true;
    }

    /// <summary>
    /// 手动航行：朝地图上一点走（玩家右键点地图空地那一发）。两段是世界米。
    ///
    /// <para><b>四道闸的次序</b>：仪式中 → 停摆期 → 防守中 → 停靠期。
    /// 停靠期排在最后，是因为<b>仪式期间巨兽是停靠着的</b>（进阶仪式要求停靠才开得起来）——
    /// 停靠那一格先判，仪式那一格就永远轮不到，提示会串。</para>
    /// </summary>
    private static bool SailTo(
        ArenaWorld world, string args, ref string canonicalArgs, ref string reason)
    {
        if (!ReplayArgs.Split(args, 2, out var two))
        {
            return Bad(ref reason, RejectReason.InvalidArgs,
                $"SailTo 要 2 段（x|z，世界米），收到 {Count(args)} 段");
        }

        // 解析不出就整条命令失败，**不兜 0**（「桥只转发」记的就是那句「兜 0 就是静默摆到第 0 排」）。
        if (!ReplayArgs.TryNumber(two[0], out var x) || !ReplayArgs.TryNumber(two[1], out var z))
        {
            return Bad(ref reason, RejectReason.InvalidArgs, "两段都得是数字（世界米）");
        }

        canonicalArgs = ReplayArgs.Join(ReplayArgs.Number(x), ReplayArgs.Number(z));

        if (BlockedByRitual(world, ref reason, "仪式中"))
        {
            return false;
        }

        if (world.InStandstill)
        {
            return Bad(ref reason, RejectReason.InvalidPhase, "等解救");
        }

        if (world.Battle.IsDefence)
        {
            return Bad(ref reason, RejectReason.InvalidPhase, "打完了再走");
        }

        if (world.IsDocked)
        {
            return Bad(ref reason, RejectReason.InvalidPhase, "先拔锚");
        }

        world.Voyage.SailTo(x, z);
        return true;
    }

    private static bool PullAnchor(ArenaWorld world, ref string canonicalArgs, ref string reason)
    {
        canonicalArgs = string.Empty;
        if (!world.IsDocked)
        {
            return Bad(ref reason, RejectReason.InvalidPhase, "不在停靠期，拔不了锚");
        }

        if (world.InStandstill)
        {
            return Bad(ref reason, RejectReason.InvalidPhase, "停摆期巨兽 Hold 在大地图上");
        }

        if (BlockedByRitual(world, ref reason, "进阶仪式期间巨兽匍匐不动"))
        {
            return false;
        }

        if (world.Battle.Occasion != BattleOccasion.None)
        {
            return Bad(ref reason, RejectReason.InvalidPhase, "已经在打仗了");
        }

        var rings = world.DockedPlace?.InwardRings ?? 0;

        // 轮结评价在清空在镇游客之前算（好评差评读的就是在镇游客的平均情绪）。
        world.Visitors.ReviewAtPullAnchor();
        world.OnPulled(rings);

        var rejected = world.Battle.PullAnchor(rings);
        if (rejected != RejectReason.InvalidArgs)
        {
            return Bad(ref reason, rejected, "拔锚防守开不起来");
        }

        return true;
    }

    private static bool StartMatch(
        ArenaWorld world, string args, ref string canonicalArgs, ref string reason)
    {
        if (!ReplayArgs.Split(args, 2, out var two))
        {
            return Bad(ref reason, RejectReason.InvalidArgs,
                $"StartMatch 要 2 段（甲队 id 列表|乙队 id 列表），收到 {Count(args)} 段");
        }

        canonicalArgs = ReplayArgs.Join(two[0], two[1]);
        if (world.Battle.Occasion != BattleOccasion.None)
        {
            return Bad(ref reason, RejectReason.InvalidPhase, "已经在打仗了");
        }

        if (BlockedByRitual(world, ref reason, "进阶仪式期间办不了赛"))
        {
            return false;
        }

        var sideA = SplitIds(two[0]);
        var sideB = SplitIds(two[1]);
        if (sideA.Count == 0 || sideB.Count == 0)
        {
            return Bad(ref reason, RejectReason.InvalidArgs, "两队都得有人");
        }

        var rejected = world.Battle.StartMatch(sideA, sideB);
        return rejected == RejectReason.InvalidArgs
            ? true
            : Bad(ref reason, rejected, "办赛开不起来");
    }

    /// <summary>
    /// 空艇成交（<c>TradeAirship|买食物|卖矿石|卖材料</c>）。
    ///
    /// <para><b>买食物是「买」</b>：付金币，食物进镇仓与巨兽粮仓 ——
    /// 与 <see cref="BuySupply"/> 那一路同一形状。早先那一版是「既扣镇上的食物、
    /// 又扣金币、还不给货」：玩家的食物与金币一起没了，换来一件东西都没有。
    /// 那一版按 spec 改成真买。</para>
    ///
    /// <para><b>卖货那一半没动</b>：卖矿与卖材料照旧从镇仓扣走，按
    /// <see cref="ArenaTuning.OreValue"/> 与 <see cref="ArenaTuning.MaterialValue"/> 折成金币进账。</para>
    ///
    /// <para><b>不够就一子儿不动</b>：先扣后判，扣不动的原路退回
    /// （<c>ProduceOre</c> / <c>ProduceSpecialMaterial</c> 记进产出，账仍然平）。</para>
    /// </summary>
    private static bool TradeAirship(
        ArenaWorld world, string args, ref string canonicalArgs, ref string reason)
    {
        if (!ReplayArgs.Split(args, 3, out var three))
        {
            return Bad(ref reason, RejectReason.InvalidArgs,
                $"TradeAirship 要 3 段（买食物|卖矿石|卖材料），收到 {Count(args)} 段");
        }

        if (!ReplayArgs.TryInt(three[0], out var buyFood)
            || !ReplayArgs.TryInt(three[1], out var sellOre)
            || !ReplayArgs.TryInt(three[2], out var sellMaterial))
        {
            return Bad(ref reason, RejectReason.InvalidArgs, "三段都得是整数（数量不按小数计）");
        }

        canonicalArgs = ReplayArgs.Join(ReplayArgs.Int(buyFood), ReplayArgs.Int(sellOre),
            ReplayArgs.Int(sellMaterial));

        if (!world.InAirshipWindow)
        {
            return Bad(ref reason, RejectReason.InvalidPhase, AirshipWindowShortText(world));
        }

        if (buyFood < 0 || sellOre < 0 || sellMaterial < 0)
        {
            return Bad(ref reason, RejectReason.InvalidArgs, "数量不能是负数");
        }

        // 卖货：先扣，扣不动就退回去（拒绝一寸不动）。
        if (sellOre > 0 && !world.Town.TrySpendOre(sellOre))
        {
            return Bad(ref reason, RejectReason.InsufficientMaterial,
                Gap("矿石不够", "件", sellOre, world.Town.Ore));
        }

        if (sellMaterial > 0 && !world.Town.TrySpendSpecialMaterial(sellMaterial))
        {
            if (sellOre > 0) world.Town.ProduceOre(sellOre);
            return Bad(ref reason, RejectReason.InsufficientMaterial,
                Gap("特殊材料不够", "份", sellMaterial, world.Town.SpecialMaterial));
        }

        // 买食物：按空艇价扣钱。扣不动就把刚才扣掉的货退回原处 —— 账一格不动。
        var foodCost = buyFood * AirshipFoodPrice;
        if (foodCost > 0.0 && !world.Town.TrySpendCoin(foodCost))
        {
            if (sellOre > 0) world.Town.ProduceOre(sellOre);
            if (sellMaterial > 0) world.Town.ProduceSpecialMaterial(sellMaterial);
            return Bad(ref reason, RejectReason.InsufficientCoin,
                Gap("金", foodCost, world.Town.Coin));
        }

        // 卖货的钱进账（自动还债按策略走，策略是「不还」时这一口不动）。
        var gold = (sellOre * ArenaTuning.OreValue) + (sellMaterial * ArenaTuning.MaterialValue);
        if (gold > 0.0)
        {
            world.Town.ProduceCoin(gold);
            world.Town.SettleAutoRepayment(gold);
        }

        // 买来的食物进镇仓，粮仓同步 —— 与 BuySupply 同一形状。
        if (buyFood > 0)
        {
            world.Town.ProduceFood(buyFood);
            world.Voyage.AddGranaryFood(buyFood);
        }

        world.RecordAirshipTrade();
        return true;
    }

    private static bool SteerOutOfBounds(ArenaWorld world, ref string canonicalArgs, ref string reason)
    {
        canonicalArgs = string.Empty;
        if (world.InStandstill)
        {
            return Bad(ref reason, RejectReason.InvalidPhase, "停摆期巨兽 Hold 在大地图上");
        }

        if (BlockedByRitual(world, ref reason, "进阶仪式期间巨兽匍匐不动"))
        {
            return false;
        }

        world.Voyage.SteerOutOfBounds();
        return true;
    }

    // === 巨兽本体（1） ===

    /// <summary>
    /// 发起进阶仪式（无参数）。判定顺序在 <see cref="ArenaWorld.AdvanceTier"/> 那一处，
    /// 本方法只负责把拒绝原因翻成注入口那一口约定的形状，并把明细写清楚
    /// （门槛那一档要带「第 N 层」，横幅那一侧的短句收不到这个数）。
    /// </summary>
    private static bool AdvanceTier(ArenaWorld world, ref string canonicalArgs, ref string reason)
    {
        canonicalArgs = string.Empty;
        var rejected = world.AdvanceTier();
        if (rejected == RejectReason.InvalidArgs) return true;

        var target = (BeastTier)((int)world.Tier + 1);
        var detail = rejected switch
        {
            RejectReason.TierAtMax => "已经到三阶了",
            RejectReason.GateNotMet => GateShortText(world, target),
            RejectReason.InsufficientSoulStone =>
                Gap("枚", ArenaTuning.CostOf(target).SoulStone, world.Town.SoulStone),
            RejectReason.InsufficientMaterial =>
                Gap("特殊材料不够", "份", ArenaTuning.CostOf(target).SpecialMaterial, world.Town.SpecialMaterial),
            RejectReason.InsufficientCoin =>
                Gap("金", ArenaTuning.CostOf(target).Coin, world.Town.Coin),
            RejectReason.InvalidPhase => "不在停靠期、正在打仗、停摆或仪式中，这一下发不出去",
            _ => "进阶仪式开不起来",
        };

        return Bad(ref reason, rejected, detail);
    }

    /// <summary>
    /// 切形态（<c>SwitchForm|original|desert|glutton</c>）。
    ///
    /// <para><b>判定顺序两档</b>：形态名不认识 → <see cref="RejectReason.InvalidArgs"/>；
    /// 没解锁 → <see cref="RejectReason.FormLocked"/>；切到当前形态是幂等成功。
    /// 幂等那一发也进 Journal（<c>ok=true</c>），重放端照录复现。</para>
    ///
    /// <para>入录的参数是规范化的 token（小写英文），不是调用方写的那一串 ——
    /// 同一逻辑事实在账上只有一种 token。</para>
    /// </summary>
    private static bool SwitchForm(
        ArenaWorld world, string args, ref string canonicalArgs, ref string reason)
    {
        if (!ReplayArgs.Split(args, 1, out var one))
        {
            return Bad(ref reason, RejectReason.InvalidArgs,
                "切形态要一个形态名（original / desert / glutton）");
        }

        var form = BeastFormText.Parse(one[0]);
        if (form is null)
        {
            var detail = one[0].Length == 0
                ? "切形态要一个形态名（original / desert / glutton）"
                : $"不认识「{one[0]}」这个形态";
            return Bad(ref reason, RejectReason.InvalidArgs, detail);
        }

        canonicalArgs = BeastFormText.TokenOf(form.Value);

        var rejected = world.SwitchForm(form.Value);
        if (rejected == RejectReason.InvalidArgs) return true;

        return Bad(ref reason, rejected, "这个形态还没解锁");
    }

    /// <summary>
    /// 喂食（<c>Feed|份数</c>）：食物换饱食度，每 <see cref="ArenaTuning.FoodPerSatiation"/> 份换 1 点，
    /// 封顶 <see cref="ArenaTuning.SatiationMax"/>。
    ///
    /// <para><b>要一段（正整数）</b>：缺段、不是数、不是正数一律回
    /// <see cref="RejectReason.InvalidArgs"/>,<b>不兜 0</b> —— 兜 0 就是「点了没反应」，
    /// 玩家分不清是自己写错了还是按钮坏了（「桥只转发」记的就是那句「兜 0 就是静默摆到第 0 排」）。</para>
    ///
    /// <para><b>喂不满就当场拒，一口都不喂</b>：<see cref="ArenaWorld.FeedBeast"/> 那一档是
    /// 「能吃多少吃多少、吃不完的退回镇仓」—— 玩家点了 50、兽粮只够 30，那一格会静悄悄地只喂进 30
    /// （还退 20 份回镇仓），画面上什么也没写。本口先判 <see cref="FeedCapacity"/>：
    /// 喂不满要的那一份就不喂，回 <see cref="RejectReason.InsufficientFood"/>，
    /// 镇仓与兽粮一格不动。</para>
    ///
    /// <para><b>饱食度满了也喂不进去</b>：余量算在 <see cref="FeedCapacity"/> 里
    /// （<see cref="ArenaWorld.SatiationRoomFood"/>）—— 兽吃不下那一口，
    /// 扣掉的食物是有去无回的。</para>
    ///
    /// <para><b>随时可喂</b>：喂食不受停靠期限制（只有进阶仪式要停靠才开得起来）。</para>
    /// </summary>
    private static bool Feed(
        ArenaWorld world, string args, ref string canonicalArgs, ref string reason)
    {
        if (!ReplayArgs.Split(args, 1, out var one))
        {
            return Bad(ref reason, RejectReason.InvalidArgs,
                $"Feed 要 1 段（食物份数），收到 {Count(args)} 段");
        }

        if (!ReplayArgs.TryInt(one[0], out var count))
        {
            return Bad(ref reason, RejectReason.InvalidArgs, "食物份数要整数");
        }

        if (count <= 0)
        {
            return Bad(ref reason, RejectReason.InvalidArgs, "食物份数要正整数");
        }

        canonicalArgs = ReplayArgs.Join(ReplayArgs.Int(count));

        // 喂不满就当场拒：宁可这一发发不出去，也不让玩家点了 50 只喂进 30 还不知道。
        var capacity = FeedCapacity(world);
        if (capacity < count)
        {
            return Bad(ref reason, RejectReason.InsufficientFood, FeedShortText(world, count));
        }

        var rejected = world.FeedBeast(count);
        if (rejected == RejectReason.InvalidArgs) return true;

        return Bad(ref reason, rejected, FeedShortText(world, count));
    }

    /// <summary>
    /// NPC 喂食（<c>NpcFeed</c>，无参数）：停靠期由一名无岗工人或帮工喂 5 食物。
    /// <b>判定全在 <see cref="ArenaWorld.NpcFeedBlocker"/></b>，这里只把原因翻成注入口那一口约定。
    /// </summary>
    private static bool NpcFeed(ArenaWorld world, ref string canonicalArgs, ref string reason)
    {
        canonicalArgs = string.Empty;
        var rejected = world.NpcFeed();
        if (rejected == RejectReason.InvalidArgs) return true;

        var detail = rejected switch
        {
            RejectReason.NoIdleWorker => "没有无岗的工人或帮工可以派去喂食",
            RejectReason.InvalidPhase => "NPC 喂食只在停靠期、且不在战斗 / 仪式 / 停摆时",
            RejectReason.InsufficientFood => world.Ecology.Satiation >= ArenaTuning.SatiationMax
                ? "饱食已满，喂不进去"
                : "镇仓与兽粮合起来不够 5 份",
            _ => "NPC 喂食发不出去",
        };
        return Bad(ref reason, rejected, detail);
    }

    /// <summary>
    /// 指定吞食（<c>EatTarget|对象 id</c>）：10 米内、饱食未满、非停靠 / 战斗 / 仪式时发得出去。
    /// </summary>
    private static bool EatTarget(
        ArenaWorld world, string args, ref string canonicalArgs, ref string reason)
    {
        if (!ReplayArgs.Split(args, 1, out var one) || one[0].Length == 0)
        {
            return Bad(ref reason, RejectReason.InvalidArgs, "EatTarget 要 1 段（生态对象 id）");
        }

        var id = one[0];
        canonicalArgs = ReplayArgs.Join(id);

        var rejected = world.EatTarget(id);
        if (rejected == RejectReason.InvalidArgs) return true;

        return Bad(ref reason, rejected, EatTargetShortText(world, id, rejected));
    }

    /// <summary>吞食被拒那一句人话（每一档分开写，玩家看得出差在哪）。</summary>
    private static string EatTargetShortText(ArenaWorld world, string id, RejectReason rejected)
    {
        if (rejected == RejectReason.FullCapacity)
        {
            return $"已经吃饱了（{Num(world.Ecology.Satiation)}/{Num(ArenaTuning.SatiationMax)}）";
        }

        if (rejected == RejectReason.InvalidPhase)
        {
            return world.Battle.Occasion != BattleOccasion.None
                ? "正在打仗，吞不下去"
                : world.IsDocked ? "停靠期间不进食"
                : world.InRitual ? "进阶仪式期间巨兽匍匐不动"
                : "停摆期巨兽 Hold 在大地图上";
        }

        var node = world.Ecology.Find(id);
        if (node is null) return $"没有「{id}」这个生态对象";

        if (!node.AvailableAt(world.Tick))
        {
            return node.Kind == EcoKind.Ore
                ? $"{id} 这块矿石已经被吃掉了（矿石不再生）"
                : $"{id} 还在再生（{ArenaTuning.TreeRegrowGameHours} / {ArenaTuning.HerdRegrowGameHours} 游戏小时那一档）";
        }

        var distance = world.Voyage.Position.DistanceTo(node.Position);
        return string.Format(CultureInfo.InvariantCulture, "离 {0} 有 {1:0.0} 米，超出 {2:0.0} 米",
            id, distance, ArenaTuning.ManualEatRangeMeters);
    }

    /// <summary>召回（<c>RecallForage</c>，无参数）：只在觅食途中有效。</summary>
    private static bool RecallForage(ArenaWorld world, ref string canonicalArgs, ref string reason)
    {
        canonicalArgs = string.Empty;
        var rejected = world.RecallForage();
        if (rejected == RejectReason.InvalidArgs) return true;

        return Bad(ref reason, rejected, "不在觅食途中（停靠 / 战斗 / 仪式期间也发不出去）");
    }

    /// <summary>
    /// 魔晶用途（<c>UseCrystal|mana</c> / <c>UseCrystal|heal|人id</c> / <c>UseCrystal|repair|建筑id</c>）。
    /// 三条都从仓库扣，且都可在战斗中使用。
    /// </summary>
    private static bool UseCrystal(
        ArenaWorld world, string args, ref string canonicalArgs, ref string reason)
    {
        var parts = args.Split(ReplayArgs.Separator, StringSplitOptions.None);
        if (parts.Length is < 1 or > 2)
        {
            return Bad(ref reason, RejectReason.InvalidArgs,
                "UseCrystal 要 1 或 2 段（mana / heal|人id / repair|建筑id）");
        }

        var kindText = parts[0].ToLowerInvariant();
        var use = kindText switch
        {
            "mana" => CrystalUseKind.Mana,
            "heal" => CrystalUseKind.Heal,
            "repair" => CrystalUseKind.Repair,
            _ => (CrystalUseKind?)null,
        };

        if (use is null)
        {
            return Bad(ref reason, RejectReason.InvalidArgs, $"不认识的魔晶用途「{parts[0]}」");
        }

        var target = parts.Length == 2 ? parts[1] : string.Empty;
        if (use != CrystalUseKind.Mana && target.Length == 0)
        {
            return Bad(ref reason, RejectReason.InvalidArgs, $"{kindText} 要给一个目标 id");
        }

        canonicalArgs = parts.Length == 2
            ? ReplayArgs.Join(kindText, target)
            : kindText;

        var rejected = world.UseCrystal(use.Value, target);
        if (rejected == RejectReason.InvalidArgs) return true;

        return Bad(ref reason, rejected, CrystalShortText(world, use.Value, target, rejected));
    }

    /// <summary>魔晶用途被拒那一句人话。</summary>
    private static string CrystalShortText(
        ArenaWorld world, CrystalUseKind use, string target, RejectReason rejected)
    {
        if (rejected == RejectReason.InsufficientMaterial)
        {
            return world.CrystalWarehouse <= 0
                ? "仓库里没有魔晶"
                : $"仓库里只有 {world.CrystalWarehouse.ToString(CultureInfo.InvariantCulture)} 单位魔晶";
        }

        return use switch
        {
            CrystalUseKind.Mana => string.Format(CultureInfo.InvariantCulture,
                "可用法力还差 {0} 格才到 {1} 格（至少要缺 {2} 格才值得用一颗）",
                ArenaTuning.ManaCapacity - world.Town.AvailableMana,
                ArenaTuning.ManaCapacity, ArenaTuning.CrystalManaMinGap),
            CrystalUseKind.Heal => world.Roster.Find(target) is null
                ? $"没有「{target}」这个人"
                : "这个人没受重伤",
            _ => world.Construction.Find(target) is null
                ? $"没有「{target}」这栋建筑"
                : "这一栋现在没有在修",
        };
    }

    /// <summary>出售魔晶（<c>SellCrystal|数量</c>）：停靠期或空艇成交窗口内才卖得出去。</summary>
    private static bool SellCrystal(
        ArenaWorld world, string args, ref string canonicalArgs, ref string reason)
    {
        if (!ReplayArgs.Split(args, 1, out var one) || !ReplayArgs.TryInt(one[0], out var count))
        {
            return Bad(ref reason, RejectReason.InvalidArgs, "SellCrystal 要 1 段（正整数）");
        }

        if (count <= 0) return Bad(ref reason, RejectReason.InvalidArgs, "数量要正整数");

        canonicalArgs = ReplayArgs.Join(ReplayArgs.Int(count));
        var rejected = world.SellCrystal(count);
        if (rejected == RejectReason.InvalidArgs) return true;

        var detail = rejected == RejectReason.InvalidPhase
            ? "停靠期或空艇成交窗口内才卖得出去"
            : $"仓库里只有 {world.CrystalWarehouse.ToString(CultureInfo.InvariantCulture)} 单位魔晶";
        return Bad(ref reason, rejected, detail);
    }


    /// <summary>
    /// 喂食一次能吃下的份数上限 —— <b>镇仓食物、兽粮与饱食度余量里最少的那一份</b>。
    ///
    /// <para>为什么是三处里的最小值：<see cref="ArenaWorld.FeedBeast"/> 先从镇仓扣食物，
    /// 再从兽粮里取（<see cref="BeastVoyage.TakeGranaryFood"/> 取 <c>min(请求, 兽粮)</c>），
    /// 还得吃得下（饱食度封顶那一档的余量）。三处都得够，这一发才喂得饱；
    /// 差在哪一处由调用方写清（面板按同一口径置灰）。</para>
    /// </summary>
    public static double FeedCapacity(ArenaWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return Math.Min(Math.Min(world.Town.Food, world.Voyage.GranaryFood), world.SatiationRoomFood);
    }

    /// <summary>
    /// 进阶按钮那一档的拦路（<b>只读，不动世界</b>）。
    ///
    /// <para>判定顺序与 <see cref="ArenaWorld.AdvanceTier"/> 同一档一档：
    /// 最高位阶 → 停靠 → 打仗 → 停摆 → 仪式 → 门槛 → 魂石 → 材料 → 金币。</para>
    ///
    /// <para><b>为什么要有这一口</b>：面板要在按下去之前就把那颗按钮置灰并写清差多少，
    /// 而 <see cref="ArenaWorld.AdvanceTier"/> 是会真的动手的那一档（成了就开仪式、扣三本账）。
    /// 这一口是它的只读镜像 —— 两处顺序必须一样，测试逐档对照
    /// （<c>Tests/BeastPanelTests.cs</c>）。</para>
    /// </summary>
    public static AdvanceBlocker AdvanceBlockerOf(ArenaWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);

        if (world.Tier == BeastTier.Tier3) return AdvanceBlocker.TierAtMax;
        if (!world.IsDocked) return AdvanceBlocker.NotDocked;
        if (world.Battle.Occasion != BattleOccasion.None) return AdvanceBlocker.InBattle;
        if (world.InStandstill) return AdvanceBlocker.InStandstill;
        if (world.InRitual) return AdvanceBlocker.InRitual;

        var target = (BeastTier)((int)world.Tier + 1);
        if (!world.GateMet(target)) return AdvanceBlocker.GateNotMet;

        var cost = ArenaTuning.CostOf(target);
        if (world.Town.SoulStone < cost.SoulStone) return AdvanceBlocker.NotEnoughSoulStone;
        if (world.Town.SpecialMaterial < cost.SpecialMaterial) return AdvanceBlocker.NotEnoughMaterial;
        if (world.Town.Coin < cost.Coin) return AdvanceBlocker.NotEnoughCoin;

        return AdvanceBlocker.None;
    }

    /// <summary>
    /// 拦路那一档 → 词表用的拒绝码。
    ///
    /// <para><b><see cref="RejectReason"/> 仍是那 13 项封闭枚举，这里不加新的</b>：
    /// 界面那一侧要分头写原因（四档阶段问题各写一句），所以另有一份更细的
    /// <see cref="AdvanceBlocker"/>；两者的对应关系只收在这一处。</para>
    /// </summary>
    public static RejectReason RejectReasonOf(AdvanceBlocker blocker) => blocker switch
    {
        AdvanceBlocker.None => RejectReason.InvalidArgs,
        AdvanceBlocker.TierAtMax => RejectReason.TierAtMax,
        AdvanceBlocker.GateNotMet => RejectReason.GateNotMet,
        AdvanceBlocker.NotEnoughSoulStone => RejectReason.InsufficientSoulStone,
        AdvanceBlocker.NotEnoughMaterial => RejectReason.InsufficientMaterial,
        AdvanceBlocker.NotEnoughCoin => RejectReason.InsufficientCoin,
        _ => RejectReason.InvalidPhase,
    };

    // === 上帝之手与法术（8） ===

    private static bool GrabEntity(
        ArenaWorld world, string args, ref string canonicalArgs, ref string reason)
    {
        if (!ReplayArgs.Split(args, 1, out var one))
        {
            return Bad(ref reason, RejectReason.InvalidArgs, $"GrabEntity 要 1 段（id），收到 {Count(args)} 段");
        }

        var id = one[0];
        if (id.Length == 0)
        {
            return Bad(ref reason, RejectReason.InvalidArgs, "id 那���不能是空串");
        }

        canonicalArgs = ReplayArgs.Join(id);

        var unit = world.Roster.Find(id);
        if (unit is not null)
        {
            // 只有镇民抓得起来：游客、敌人、帮工按下去不响应（spec §5 / 逻辑层 12）。
            if (!unit.IsTownsperson)
            {
                return Bad(ref reason, RejectReason.InvalidTarget, $"只有镇民抓得起来（{id}）");
            }

            // 在手物退回原位并重新入队（回到本级队尾 —— 同级 FIFO 的那一半）。
            var carried = world.CarryingOf(id);
            if (carried is not null)
            {
                world.DropHaulOf(id);
                world.Haul.Requeue(carried with { QueuedTick = world.Tick });
            }

            world.Roster.ClearPost(unit);
            unit.Held = true;
            HoldAt(world, unit, unit.DeckX, unit.DeckZ);
            return true;
        }

        // 尸体与散货那一档：抓在手就停在原地，等 DropToBuilding / DropToDeck 处置。
        var item = world.Items.Find(id);
        if (item is null)
        {
            return Bad(ref reason, RejectReason.InvalidTarget, $"甲板上没有 {id} 这一件");
        }

        // 拖拽范围：镇民、尸体、散货与魔晶。点位物都抓得起来，不按类别另开黑名单。
        // 在手状态记在本体上：快照那一格 held=<类别>:<id> 读的就是这一位。
        item.Hold();
        return true;
    }

    private static bool DropToBuilding(
        ArenaWorld world, string args, ref string canonicalArgs, ref string reason)
    {
        if (!ReplayArgs.Split(args, 2, out var two))
        {
            return Bad(ref reason, RejectReason.InvalidArgs,
                $"DropToBuilding 要 2 段（id|建筑 id），收到 {Count(args)} 段");
        }

        canonicalArgs = ReplayArgs.Join(two[0], two[1]);
        var id = two[0];
        var buildingId = two[1];
        if (id.Length == 0 || buildingId.Length == 0)
        {
            return Bad(ref reason, RejectReason.InvalidArgs, "两段都不能是空串");
        }

        var building = world.Construction.Find(buildingId);
        if (building is null)
        {
            return Bad(ref reason, RejectReason.InvalidTarget, $"{buildingId} 不在册");
        }

        var unit = world.Roster.Find(id);
        if (unit is not null)
        {
            if (unit.IsBeast || unit.Role == UnitRole.Visitor)
            {
                return Bad(ref reason, RejectReason.InvalidTarget, $"敌人与游客扔不进建筑（{id}）");
            }

            // 自由人扔进工作 / 训练建筑自动转身份；宿舍不是岗位（它是住处，0 岗）。
            // **先只读校验、后转职**：岗位满等拒绝路径上不能留下「职业改了、忠心重置了」的副作用。
            var roleAfterDrop = unit.Role;
            if (unit.Role == UnitRole.FreePerson
                && building.BlueprintId != "Dormitory"
                && PeopleRoster.RoleFitsPost(UnitRole.Worker, building.BlueprintId))
            {
                roleAfterDrop = UnitRole.Worker;
            }

            // 与 AssignPost 动词同一套校验（角色相符、岗位未满）—— 判据只写一份。
            if (!CanPostToBuilding(world, unit, buildingId, building.BlueprintId, roleAfterDrop, ref reason))
            {
                // 被拒也要把人从手上放下来：留着 Held 会一直停在空中等下一发。
                unit.Held = false;
                HoldAt(world, unit, unit.DeckX, unit.DeckZ);
                return false;
            }

            // 校验通过了才转职，再把岗位写上 —— 拒绝路径上一寸不动。
            if (roleAfterDrop != unit.Role) world.Roster.ConvertRole(unit, roleAfterDrop);
            world.Roster.AssignPost(unit, buildingId);

            unit.Held = false;
            HoldAt(world, unit, world.DoorDeckOf(buildingId).X, world.DoorDeckOf(buildingId).Z);
            return true;
        }

        var item = world.Items.Find(id);
        if (item is null)
        {
            return Bad(ref reason, RejectReason.InvalidTarget, $"甲板上没有 {id} 这一件");
        }

        // 成功与被拒都算松手：留着 Held 会让这一件一直被当成「在手」。
        var delivered = DeliverItem(world, item, buildingId, ref reason);
        item.Release();
        return delivered;
    }

    /// <summary>
    /// 把一件点位物投进某栋建筑 —— <b>投递的唯一写口</b>。
    ///
    /// <para><b>两个调用方共用这一份</b>：上帝之手 <c>DropToBuilding</c>（玩家那一发），
    /// 与搬运队列走完第二程时自动交单（<c>ArenaWorld</c> 那一口）。
    /// 搬运那一摊**不另写一套投递实现** —— 两套实现漂开之后，
    /// 「玩家把备件扔进教堂」与「帮工把备件送进教堂」会算出两个不同的结果，
    /// 而那种分叉在画面上只看得出「某个数字不一样」（「桥只转发」）。</para>
    ///
    /// <para><b>被拒不结单</b>：回 <c>false</c> 时这一件仍在册（本方法不摘它），
    /// 账上该退的已退，八条守恒等式不动（「约束唯一出处」不靠调用方补救）。</para>
    ///
    /// <para><b>尸体那一档进教堂即入册</b>：教堂在册表（<c>ArenaWorld.RegisterChurchCorpse</c>）
    /// 记下「是谁倒下的、什么时候进的」—— 复活那一发就是从这本册子里把人找回来的。
    /// 入册之后这一件离场，场上不再有这具尸体，负面情绪那一圈随之消掉。</para>
    /// </summary>
    public static bool DeliverItem(
        ArenaWorld world, PointItem item, string buildingId, ref string reason)
    {
        if (item.Kind == PointItemKind.Corpse)
        {
            if (buildingId != world.FindByBlueprint("Church"))
            {
                return Bad(ref reason, RejectReason.InvalidTarget, "尸体只进教堂的待救槽");
            }

            // 入册：教堂那一本在册表记下「这一具是谁倒下的、什么时候进的」——
            // 只有记了这一条，这一具才救得回来（直接摘掉就再也认不出是谁）。
            world.RegisterChurchCorpse(item.Id, item.Payload, world.Tick);

            // 入册之后这一件离场：场上不再有这具尸体，那一圈负面情绪随之消掉。
            item.Retire();
            return true;
        }

        switch (item.Payload)
        {
            case "sparepart":
                if (!world.Deck.IsRepairing(buildingId))
                {
                    var spent = world.Town.TrySpendSparePart(1.0);
                    var fixedIt = world.Deck.RepairBuilding(buildingId, world.Tick);
                    if (!spent && !fixedIt.Ok)
                    {
                        if (spent) world.Town.ProduceSparePart(1.0);
                        return Bad(ref reason, RejectReason.InsufficientMaterial,
                            spent
                                ? "备件够了（要 1 个，有 "
                                    + $"{Num(world.Town.SparePart)} 个），但那栋已经满耐久 / 已在修"
                                : Gap("缺备件", "个", 1.0, world.Town.SparePart));
                    }

                    // 备件在这一格真扣掉了（镇账或甲板占位账任一边）—— 修理消耗那一刻发事件。
                    world.NoteRepairSpent(buildingId, 1.0);
                }

                item.Retire();
                return true;

            case "food":
                {
                    var rejected = world.FeedBeast(1.0);
                    if (rejected != RejectReason.InvalidArgs)
                    {
                        return Bad(ref reason, rejected, FeedShortText(world, 1.0));
                    }

                    item.Retire();
                    return true;
                }

            case "ore":
                if (buildingId != ArenaWorld.TownHallId)
                {
                    return Bad(ref reason, RejectReason.InvalidTarget, "矿石只进镇政府仓库");
                }

                world.Town.ProduceOre(1.0);
                item.Retire();
                return true;

            case "material":
                if (buildingId != ArenaWorld.TownHallId)
                {
                    return Bad(ref reason, RejectReason.InvalidTarget, "特殊材料只进镇政府仓库");
                }

                world.Town.ProduceSpecialMaterial(1.0);
                item.Retire();
                return true;

            case "crystal":
                if (buildingId != ArenaWorld.TownHallId)
                {
                    return Bad(ref reason, RejectReason.InvalidTarget, "魔晶只进镇政府仓库");
                }

                world.Town.ProduceCrystal(1.0);
                world.NoteCrystalWarehoused(item.Id);
                item.Retire();
                return true;

            default:
                return Bad(ref reason, RejectReason.InvalidTarget, $"这一件不是可投递的货（{item.Kind}）");
        }
    }

    private static bool DropToDeck(
        ArenaWorld world, string args, ref string canonicalArgs, ref string reason)
    {
        if (!ReplayArgs.Split(args, 3, out var three))
        {
            return Bad(ref reason, RejectReason.InvalidArgs,
                $"DropToDeck 要 3 段（id|x|z），收到 {Count(args)} 段");
        }

        if (!ReplayArgs.TryNumber(three[1], out var x) || !ReplayArgs.TryNumber(three[2], out var z))
        {
            return Bad(ref reason, RejectReason.InvalidArgs, "x / z 解析不出");
        }

        canonicalArgs = ReplayArgs.Join(three[0], ReplayArgs.Number(x), ReplayArgs.Number(z));

        var unit = world.Roster.Find(three[0]);
        if (unit is not null)
        {
            if (unit.IsBeast || unit.Role == UnitRole.Visitor)
            {
                return Bad(ref reason, RejectReason.InvalidTarget, $"敌人与游客挪不进自家甲板（{three[0]}）");
            }

            var at = world.NearestFreeDeckCell(x, z);
            unit.Held = false;
            world.Roster.ClearPost(unit);
            PlaceOnDeck(world, unit, at);
            return true;
        }

        var item = world.Items.Find(three[0]);
        if (item is null)
        {
            return Bad(ref reason, RejectReason.InvalidTarget, $"甲板上没有 {three[0]} 这一件");
        }

        // 尸体只进教堂：放甲板这一档不接它（被拒也要把手松开 —— 留着 Held 会一直算「在手」）。
        if (item.Kind == PointItemKind.Corpse)
        {
            item.Release();

            // 放回原处不算挪动 —— 取消那一档（拖到界面控件上、模态挡住、甲板外）走的就是这一条，
            // 那一发不该弹一条拒绝。挪到别处才拒。
            if (SameSpot(x, z, item.DeckX, item.DeckZ)) return true;

            return Bad(ref reason, RejectReason.InvalidTarget, "尸体只进教堂，不放甲板");
        }

        // 散货放甲板：只改落点，库存与在册状态一格不动。
        item.MoveTo(new NavPoint(x, z));
        item.Release();
        return true;
    }

    /// <summary>
    /// 两处落点算不算同一处 —— 「放回原处」那一档的判据。
    ///
    /// <para><b>容差 1 厘米</b>：落点按两位小数上发（<c>ReplayArgs.Number</c> 同形），
    /// 两位小数最多带来 5 毫米偏差 —— 1 厘米容得下那点偏差，
    /// 又窄到「真的挪过了」不算同一处。</para>
    /// </summary>
    private static bool SameSpot(double x, double z, double otherX, double otherZ)
        => Math.Abs(x - otherX) <= 0.01 && Math.Abs(z - otherZ) <= 0.01;

    private static bool LiftBuilding(
        ArenaWorld world, string args, ref string canonicalArgs, ref string reason)
    {
        if (!ReplayArgs.Split(args, 1, out var one))
        {
            return Bad(ref reason, RejectReason.InvalidArgs, $"LiftBuilding 要 1 段（建筑 id），收到 {Count(args)} 段");
        }

        if (one[0].Length == 0)
        {
            return Bad(ref reason, RejectReason.InvalidArgs, "id 那一段不能是空串");
        }

        canonicalArgs = ReplayArgs.Join(one[0]);
        if (world.Construction.Find(one[0]) is null)
        {
            return Bad(ref reason, RejectReason.InvalidTarget, $"{one[0]} 不在册");
        }

        if (!world.Town.TryHoldMana(TownAccounts.ManaHold.Stasis))
        {
            return Bad(ref reason, RejectReason.InsufficientMana, Gap("格", 1.0, world.Town.AvailableMana));
        }

        var lifted = world.Deck.LiftBuilding(one[0]);
        if (!lifted.Ok)
        {
            world.Town.ReleaseMana(TownAccounts.ManaHold.Stasis);
            return Bad(ref reason, RejectReason.InvalidPhase, $"拔不起 {one[0]}（已悬空或不在册）");
        }

        return true;
    }

    private static bool DropBuilding(
        ArenaWorld world, string args, ref string canonicalArgs, ref string reason)
    {
        if (!ReplayArgs.Split(args, 3, out var three))
        {
            return Bad(ref reason, RejectReason.InvalidArgs,
                $"DropBuilding 要 3 段（建筑 id|列|行），收到 {Count(args)} 段");
        }

        if (!ReplayArgs.TryInt(three[1], out var col) || !ReplayArgs.TryInt(three[2], out var row))
        {
            return Bad(ref reason, RejectReason.InvalidArgs, "列 / 行解析不出");
        }

        canonicalArgs = ReplayArgs.Join(three[0], ReplayArgs.Int(col), ReplayArgs.Int(row));

        // 先问「这一格能不能落」，再问「那一格有没有别人占着」——
        // 反过来的话会先报法力或状态不对，而真因是位置被占。
        var building = world.Construction.Find(three[0]);
        if (building is null)
        {
            return Bad(ref reason, RejectReason.InvalidTarget, $"{three[0]} 不在册");
        }

        // 放回自己那几格是空动作，不按「格被占着」拒 —— 那几格占着的就是它自己。
        // 别的建筑占着的那几格照旧走下面这条 Check 拒（挪到别人头上这条路没有开口）。
        if (!world.Deck.IsAtOwnAnchor(three[0], col, row))
        {
            var verdict = world.Construction.Check(
                building.BlueprintId, DeckBlueprints.CandidateOf(new GridCell(col, row)));
            if (!verdict.Ok)
            {
                return Bad(ref reason, RejectReason.InvalidTarget, $"落位不成：{verdict.Reason}");
            }
        }

        var dropped = world.Deck.DropBuilding(three[0], col, row);
        if (!dropped.Ok)
        {
            return Bad(ref reason, RejectReason.InvalidPhase, $"落不下 {three[0]}（没悬空）");
        }

        world.Town.ReleaseMana(TownAccounts.ManaHold.Stasis);
        world.OpenWalkableBuildings();
        return true;
    }

    private static bool SummonHelper(ArenaWorld world, ref string canonicalArgs, ref string reason)
    {
        canonicalArgs = string.Empty;
        if (!CanSummonHelper(world))
        {
            return Bad(ref reason, RejectReason.InvalidPhase, "停靠期或交战期才召帮工");
        }

        if (world.SummonHelper() is null)
        {
            return Bad(ref reason, RejectReason.InsufficientMana, Gap("格", 1.0, world.Town.AvailableMana));
        }

        return true;
    }

    /// <summary>
    /// 现在召得下帮工没有 —— <b>判据只有这一处</b>（停靠期或交战期）。
    ///
    /// <para>动词 <c>SummonHelper</c> 与帮工弹层上那颗「召唤」按钮读的是同一个口：
    /// 两处各判一遍的话，按钮能点而动词拒、或者按钮灰着而动词收得到。</para>
    /// </summary>
    public static bool CanSummonHelper(ArenaWorld? world)
        => world is not null && (world.Battle.Occasion != BattleOccasion.None || world.IsDocked);

    private static bool DismissHelper(
        ArenaWorld world, string args, ref string canonicalArgs, ref string reason)
    {
        if (!ReplayArgs.Split(args, 1, out var one))
        {
            return Bad(ref reason, RejectReason.InvalidArgs, $"DismissHelper 要 1 段（帮工 id），收到 {Count(args)} 段");
        }

        if (one[0].Length == 0)
        {
            return Bad(ref reason, RejectReason.InvalidArgs, "id 那一段不能是空串");
        }

        canonicalArgs = ReplayArgs.Join(one[0]);
        if (!world.DismissHelper(one[0]))
        {
            return Bad(ref reason, RejectReason.InvalidTarget, $"{one[0]} 不是在册帮工");
        }

        return true;
    }

    private static bool CastHeal(
        ArenaWorld world, string args, ref string canonicalArgs, ref string reason)
    {
        if (!ReplayArgs.Split(args, 1, out var one))
        {
            return Bad(ref reason, RejectReason.InvalidArgs, $"CastHeal 要 1 段（人 id），收到 {Count(args)} 段");
        }

        if (one[0].Length == 0)
        {
            return Bad(ref reason, RejectReason.InvalidArgs, "id 那一段不能是空串");
        }

        canonicalArgs = ReplayArgs.Join(one[0]);
        var unit = world.Roster.Find(one[0]);
        if (unit is null || unit.LeftTown)
        {
            return Bad(ref reason, RejectReason.InvalidTarget, $"{one[0]} 不在册");
        }

        if (unit.State != UnitState.Wounded)
        {
            return Bad(ref reason, RejectReason.InvalidPhase, $"{one[0]} 没伤，治不了");
        }

        // 法力即时消耗：花掉即扣，不占格。
        if (!world.Town.TrySpendInstantMana())
        {
            return Bad(ref reason, RejectReason.InsufficientMana, Gap("格", 1.0, world.Town.AvailableMana));
        }

        unit.State = UnitState.Standing;
        unit.Hp = ArenaTuning.UnitMaxHp;

        // 法力已扣、人已治好 —— 治疗消耗那一刻发事件（走魔晶的治疗另有 E-09，两条不重叠）。
        world.NoteHealingSpent(unit.Id, 1.0);
        return true;
    }

    // === 建造与土壤（5） ===

    private static bool PlaceBuilding(
        ArenaWorld world, string args, ref string canonicalArgs, ref string reason)
    {
        if (!ReplayArgs.Split(args, 3, out var three))
        {
            return Bad(ref reason, RejectReason.InvalidArgs,
                $"PlaceBuilding 要 3 段（蓝图 id|列|行），收到 {Count(args)} 段");
        }

        if (!ReplayArgs.TryInt(three[1], out var col) || !ReplayArgs.TryInt(three[2], out var row))
        {
            return Bad(ref reason, RejectReason.InvalidArgs, "列 / 行解析不出");
        }

        canonicalArgs = ReplayArgs.Join(three[0], ReplayArgs.Int(col), ReplayArgs.Int(row));

        var spec = DeckBlueprints.SpecOf(three[0]);
        if (spec is null)
        {
            return Bad(ref reason, RejectReason.InvalidArgs, $"{three[0]} 不在十三件建筑表里");
        }

        // 先问「这一格能不能建」，再问「买不买得起」——
        // 反过来的话钱不够时会先报钱不够，而真因是那一格建过了。
        var verdict = world.Construction.Check(three[0], DeckBlueprints.CandidateOf(new GridCell(col, row)));
        if (!verdict.Ok)
        {
            return Bad(ref reason, RejectReason.InvalidTarget, $"建不了：{verdict.Reason}");
        }

        if (!world.Town.TrySpendCoin(spec.CoinCost))
        {
            return Bad(ref reason, RejectReason.InsufficientCoin,
                Gap("金", spec.CoinCost, world.Town.Coin));
        }

        var placed = world.Deck.PlaceBuilding(three[0], col, row);
        if (!placed.Ok)
        {
            world.Town.RefundCoin(spec.CoinCost);   // 拒绝一寸不动
            return Bad(ref reason, RejectReason.InvalidTarget, $"落座不成：{placed.Reason}");
        }

        return true;
    }

    private static bool RemoveBuilding(
        ArenaWorld world, string args, ref string canonicalArgs, ref string reason)
    {
        if (!ReplayArgs.Split(args, 1, out var one))
        {
            return Bad(ref reason, RejectReason.InvalidArgs, $"RemoveBuilding 要 1 段（建筑 id），收到 {Count(args)} 段");
        }

        if (one[0].Length == 0)
        {
            return Bad(ref reason, RejectReason.InvalidArgs, "id 那一段不能是空串");
        }

        canonicalArgs = ReplayArgs.Join(one[0]);
        if (one[0] == ArenaWorld.TownHallId)
        {
            return Bad(ref reason, RejectReason.InvalidTarget, "镇政府不可拆除");
        }

        if (world.Construction.Find(one[0]) is null)
        {
            return Bad(ref reason, RejectReason.InvalidTarget, $"{one[0]} 不在册");
        }

        world.Roster.ClearPostsOf(one[0]);
        world.Construction.Remove(one[0], RemovalCause.Dismantle);
        world.OpenWalkableBuildings();
        return true;
    }

    private static bool RepairBuilding(
        ArenaWorld world, string args, ref string canonicalArgs, ref string reason)
    {
        if (!ReplayArgs.Split(args, 1, out var one))
        {
            return Bad(ref reason, RejectReason.InvalidArgs, $"RepairBuilding 要 1 段（建筑 id），收到 {Count(args)} 段");
        }

        if (one[0].Length == 0)
        {
            return Bad(ref reason, RejectReason.InvalidArgs, "id 那一段不能是空串");
        }

        canonicalArgs = ReplayArgs.Join(one[0]);

        // 先整条判完再动账：**缺备件与没人在岗是两条原因**，横幅要写得出是哪一条
        // （两样都归同一个 InvalidPhase 的话，UI 只能从一句笼统的拒绝里猜）。
        switch (world.RepairBlockerOf(one[0]))
        {
            case RepairBlocker.UnknownBuilding:
                return Bad(ref reason, RejectReason.InvalidTarget, $"{one[0]} 不在册");
            case RepairBlocker.FullHealth:
                return Bad(ref reason, RejectReason.InvalidPhase, $"{one[0]} 耐久已满，没什么可修的");
            case RepairBlocker.NoSparePart:
                return Bad(ref reason, RejectReason.InsufficientMaterial, Gap("缺备件", "个", ArenaTuning.RepairSpareParts, world.Town.SparePart));
            case RepairBlocker.NoWorker:
                return Bad(ref reason, RejectReason.InvalidPhase, "没人在岗");
            case RepairBlocker.AlreadyRepairing:
                return Bad(ref reason, RejectReason.InvalidPhase, $"{one[0]} 已经在修了");
        }

        // 备件的真值在 Town 那一本（八条守恒第 6 式读它）；Deck 那一份是 ticket 10 的占位账，
        // 动词两边同步动 —— 托不起时原路退回去。
        if (!world.Town.TrySpendSparePart(ArenaTuning.RepairSpareParts))
        {
            return Bad(ref reason, RejectReason.InsufficientMaterial, Gap("缺备件", "个", ArenaTuning.RepairSpareParts, world.Town.SparePart));
        }

        // 上面那一道 RepairBlockerOf 已经把「有没有人在岗」算过了（None 蕴含在岗 ≥ 1），
        // 这里不再重复读一次岗位表 —— 同一件事只算一处。
        var repaired = world.Deck.RepairBuilding(one[0], world.Tick);
        if (!repaired.Ok)
        {
            world.Town.ProduceSparePart(ArenaTuning.RepairSpareParts);
            return Bad(ref reason, RejectReason.InvalidPhase, $"修不了 {one[0]}（满耐久 / 已在修）");
        }

        // 备件已扣、修理已受理 —— 修理消耗那一刻发事件（走魔晶的立即修好另有 E-09，两条不重叠）。
        world.NoteRepairSpent(one[0], ArenaTuning.RepairSpareParts);
        return true;
    }

    private static bool ExcavateSoil(
        ArenaWorld world, string args, ref string canonicalArgs, ref string reason)
    {
        if (!ReplayArgs.Split(args, 2, out var two))
        {
            return Bad(ref reason, RejectReason.InvalidArgs,
                $"ExcavateSoil 要 2 段（象限|层），收到 {Count(args)} 段");
        }

        if (!ReplayArgs.TryInt(two[0], out var sector) || !ReplayArgs.TryInt(two[1], out var layer))
        {
            return Bad(ref reason, RejectReason.InvalidArgs, "象限 / 层解析不出");
        }

        canonicalArgs = ReplayArgs.Join(ReplayArgs.Int(sector), ReplayArgs.Int(layer));

        if (sector < 0 || sector >= UndergroundSoil.Sectors
            || layer < 1 || layer > UndergroundSoil.Layers)
        {
            return Bad(ref reason, RejectReason.InvalidArgs, $"象限取 0..{UndergroundSoil.Sectors - 1}、层取 1..{UndergroundSoil.Layers}");
        }

        if (world.Soil.IsDug(sector, layer))
        {
            return Bad(ref reason, RejectReason.InvalidPhase, "那一格已经挖开了");
        }

        if (!world.Soil.BeginDigging(sector, layer, ArenaTuning.DigWorkerThreshold, world.Tick))
        {
            return Bad(ref reason, RejectReason.InvalidPhase, "挖不了（同象限自上而下，上一层还没开）");
        }

        // 挖开洞穴格不再自动开打：清剿是一条独立动词（ClearCave），
        // 「挖到第二层」与「下去清剿」是两步，玩家要准备才发起。
        return true;
    }

    /// <summary>
    /// 嵌入魔晶（<c>sector|layer</c>）：停靠期、第二层、已清剿、同格未嵌入、仓库有 1 单位。
    /// 判定顺序与 <see cref="ArenaWorld.EmbedBlockerOf"/> 一一对应，本方法只把拦路翻成中文。
    /// </summary>
    private static bool EmbedCrystal(
        ArenaWorld world, string args, ref string canonicalArgs, ref string reason)
    {
        if (!ReplayArgs.Split(args, 2, out var two))
        {
            return Bad(ref reason, RejectReason.InvalidArgs,
                $"EmbedCrystal 要 2 段（象限|层），收到 {Count(args)} 段");
        }

        if (!ReplayArgs.TryInt(two[0], out var sector) || !ReplayArgs.TryInt(two[1], out var layer))
        {
            return Bad(ref reason, RejectReason.InvalidArgs, "象限 / 层解析不出");
        }

        canonicalArgs = ReplayArgs.Join(ReplayArgs.Int(sector), ReplayArgs.Int(layer));

        var blocker = world.EmbedBlockerOf(sector, layer);
        if (blocker != SoilEmbedBlocker.None)
        {
            return Bad(ref reason, ArenaWorld.RejectReasonOf(blocker), EmbedBlockerText(world, blocker, sector, layer));
        }

        var rejected = world.EmbedCrystal(sector, layer);
        if (rejected == RejectReason.InvalidArgs) return true;

        return Bad(ref reason, rejected, "魔晶嵌不进去");
    }

    /// <summary>嵌入那一档的中文明细 —— 缺什么写什么（缺魔晶那一档要写出仓库余量）。</summary>
    private static string EmbedBlockerText(
        ArenaWorld world, SoilEmbedBlocker blocker, int sector, int layer)
    {
        return blocker switch
        {
            SoilEmbedBlocker.NotDocked => "嵌入只限停靠期",
            SoilEmbedBlocker.OutOfRange =>
                $"象限取 0..{UndergroundSoil.Sectors - 1}、层取 1..{UndergroundSoil.Layers}",
            SoilEmbedBlocker.NotDug => $"{sector}_{layer} 还没挖开",
            SoilEmbedBlocker.NotCaveLayer => "只有第二层的洞穴格能嵌入",
            SoilEmbedBlocker.NotCleared => "洞穴还没清剿成功",
            SoilEmbedBlocker.AlreadyEmbedded => "这一格已经嵌入过魔晶（同格不可重复嵌入）",
            SoilEmbedBlocker.NoCrystal => Gap("仓库里没有魔晶", "单位", 1.0, world.Town.Crystal),
            SoilEmbedBlocker.Busy => "打仗、停摆或仪式期间嵌入发不出去",
            _ => "这一格嵌不了",
        };
    }

    // === 地下战斗（2） ===

    /// <summary>发起洞穴清剿（<c>sector|layer</c>）：只认那处唯一的洞穴，且必须已挖开、未清剿。</summary>
    /// <summary>
    /// 立即重试最终防守：零段动词。按战前快照还原同一个世界，直接重开同一场。
    /// <b>重试次数不在这里清零</b>（失败那一格已累计），只把尝试序号 +1。
    /// </summary>
    /// <summary>结算后的继续经营：零段动词。</summary>
    private static bool ContinueBusiness(ArenaWorld world, ref string canonicalArgs, ref string reason)
    {
        canonicalArgs = string.Empty;
        return world.ContinueBusiness()
            ? true
            : Bad(ref reason, RejectReason.InvalidPhase, "不在结算那一档，继续经营发不出去");
    }

    private static bool RetryFinalDefence(ArenaWorld world, ref string canonicalArgs, ref string reason)
    {
        canonicalArgs = string.Empty;
        var rejected = world.RetryFinalDefence();
        return rejected == RejectReason.InvalidArgs
            ? true
            : Bad(ref reason, rejected, "不在最终防守的失败收场，重试发不出去");
    }

    private static bool ClearCave(
        ArenaWorld world, string args, ref string canonicalArgs, ref string reason)
    {
        if (!ReplayArgs.Split(args, 2, out var two))
        {
            return Bad(ref reason, RejectReason.InvalidArgs,
                $"ClearCave 要 2 段（象限|层），收到 {Count(args)} 段");
        }

        if (!ReplayArgs.TryInt(two[0], out var sector) || !ReplayArgs.TryInt(two[1], out var layer))
        {
            return Bad(ref reason, RejectReason.InvalidArgs, "象限 / 层解析不出");
        }

        canonicalArgs = ReplayArgs.Join(ReplayArgs.Int(sector), ReplayArgs.Int(layer));

        var rejected = world.Battle.ClearCave(sector, layer);
        if (rejected == RejectReason.InvalidArgs) return true;

        var detail = rejected switch
        {
            RejectReason.InvalidTarget => $"只有 {UndergroundSoil.CaveSector}_{UndergroundSoil.NativeCaveLayer} 那一处洞穴",
            RejectReason.InvalidPhase => world.Soil.IsCleared(sector, layer)
                ? "那一格已经清剿过了"
                : "那一格还没挖开，或者正在打另一场",
            _ => "清剿开不起来",
        };

        return Bad(ref reason, rejected, detail);
    }

    /// <summary>撤退（无参数）：只对洞穴清剿有效，算失败，伤亡与洞里剩下的怪都保留。</summary>
    private static bool RetreatCave(ArenaWorld world, ref string canonicalArgs, ref string reason)
    {
        canonicalArgs = string.Empty;
        var rejected = world.Battle.RetreatCave();
        if (rejected == RejectReason.InvalidArgs) return true;

        return Bad(ref reason, rejected, "现在没有洞穴清剿可撤");
    }

    // === 人口与经营（7） ===

    private static bool Recruit(
        ArenaWorld world, string args, ref string canonicalArgs, ref string reason)
    {
        if (!ReplayArgs.Split(args, 1, out var one))
        {
            return Bad(ref reason, RejectReason.InvalidArgs, $"Recruit 要 1 段（来路），收到 {Count(args)} 段");
        }

        var source = ParseRecruitSource(one[0]);
        if (source is null)
        {
            return Bad(ref reason, RejectReason.InvalidArgs,
                $"来路解析不出「{one[0]}」（TownOrCountry / BuySlave / CatchVagrant / CaptureBandit / CatchBeast）");
        }

        canonicalArgs = ReplayArgs.Join(source.Value.ToString());

        // 判定顺序只有一份，在 ArenaWorld.CanRecruit 上：期别 → 本次停靠额度 → 岗位 → 金币。
        // 招募框置灰读的也是它，UI 与拒绝不各写一遍。
        var blocker = world.CanRecruit(source.Value);
        if (blocker == RecruitBlocker.NotDocked)
        {
            return Bad(ref reason, RejectReason.InvalidPhase, "停靠期才招人");
        }

        if (blocker == RecruitBlocker.LimitReached)
        {
            return Bad(ref reason, RejectReason.FullCapacity,
                Gap("本次停靠已招满", "个", ArenaTuning.RecruitPerDock, world.RecruitLimitLeft));
        }

        var role = RoleOf(source.Value);
        if (blocker == RecruitBlocker.PostFull)
        {
            var cap = role == UnitRole.Worker ? ArenaTuning.WorkerPostCap : ArenaTuning.GladiatorPostCap;
            var inTown = role == UnitRole.Worker
                ? world.Roster.WorkerCount
                : world.Roster.GladiatorCount;
            var label = role == UnitRole.Worker ? "工作人员岗位" : "角斗士岗位";
            return Bad(ref reason, RejectReason.FullCapacity,
                $"岗位已满：{label}{Gap("个", cap, cap - inTown)}");
        }

        var cost = RecruitCost(source.Value);
        if (blocker == RecruitBlocker.NoCoin)
        {
            return Bad(ref reason, RejectReason.InsufficientCoin,
                Gap("金", cost, world.Town.Coin));
        }

        if (cost > 0.0 && !world.Town.TrySpendCoin(cost))
        {
            return Bad(ref reason, RejectReason.InsufficientCoin,
                Gap("金", cost, world.Town.Coin));
        }

        var id = source.Value switch
        {
            RecruitSource.TownOrCountry => $"r{world.Roster.Units.Count + 1}",
            RecruitSource.BuySlave => $"sl{world.Roster.Units.Count + 1}",
            RecruitSource.CatchVagrant => $"vg{world.Roster.Units.Count + 1}",
            RecruitSource.CaptureBandit => $"bd{world.Roster.Units.Count + 1}",
            _ => $"be{world.Roster.Units.Count + 1}",
        };

        var unit = world.Roster.Add(id, role);
        unit.Loyalty = LoyaltyOf(source.Value);
        world.NoteRecruited();
        return true;
    }

    private static bool ConvertRole(
        ArenaWorld world, string args, ref string canonicalArgs, ref string reason)
    {
        if (!ReplayArgs.Split(args, 2, out var two))
        {
            return Bad(ref reason, RejectReason.InvalidArgs,
                $"ConvertRole 要 2 段（人 id|目标类别），收到 {Count(args)} 段");
        }

        if (!Enum.TryParse<UnitRole>(two[1], out var target))
        {
            return Bad(ref reason, RejectReason.InvalidArgs,
                $"类别解析不出「{two[1]}」（Worker / Gladiator / FreePerson）");
        }

        canonicalArgs = ReplayArgs.Join(two[0], target.ToString());
        var unit = world.Roster.Find(two[0]);
        if (unit is null || unit.LeftTown)
        {
            return Bad(ref reason, RejectReason.InvalidTarget, $"{two[0]} 不在册");
        }

        if (!world.Roster.ConvertRole(unit, target))
        {
            return Bad(ref reason, RejectReason.InvalidTarget, $"{two[0]} 转不了那一类");
        }

        return true;
    }

    private static bool GrantFreedom(
        ArenaWorld world, string args, ref string canonicalArgs, ref string reason)
    {
        if (!ReplayArgs.Split(args, 1, out var one))
        {
            return Bad(ref reason, RejectReason.InvalidArgs, $"GrantFreedom 要 1 段（人 id），收到 {Count(args)} 段");
        }

        if (one[0].Length == 0)
        {
            return Bad(ref reason, RejectReason.InvalidArgs, "id 那一段不能是空串");
        }

        canonicalArgs = ReplayArgs.Join(one[0]);
        var unit = world.Roster.Find(one[0]);
        if (unit is null || unit.LeftTown)
        {
            return Bad(ref reason, RejectReason.InvalidTarget, $"{one[0]} 不在册");
        }

        if (!world.Roster.GrantFreedom(unit))
        {
            return Bad(ref reason, RejectReason.InvalidTarget, $"{one[0]} 放不了（外来活物改不了身份）");
        }

        return true;
    }

    private static bool SellStar(
        ArenaWorld world, string args, ref string canonicalArgs, ref string reason)
    {
        if (!ReplayArgs.Split(args, 1, out var one))
        {
            return Bad(ref reason, RejectReason.InvalidArgs, $"SellStar 要 1 段（人 id），收到 {Count(args)} 段");
        }

        if (one[0].Length == 0)
        {
            return Bad(ref reason, RejectReason.InvalidArgs, "id 那一段不能是空串");
        }

        canonicalArgs = ReplayArgs.Join(one[0]);
        var unit = world.Roster.Find(one[0]);

        // spec §3.4 把那一道闸写成一个条件式：「只卖个人名声 ≥ 100 的明星角斗士」。
        // 两处回的是同一档 InvalidTarget：被指名的那个人此刻不是可卖的标的，
        // 括号里那句话分说的是「压根不是角斗士」还是「是角斗士但还没挣到明星那一档」。
        if (unit is null || unit.LeftTown || unit.Role != UnitRole.Gladiator)
        {
            return Bad(ref reason, RejectReason.InvalidTarget, $"{one[0]} 不是在册角斗士");
        }

        if (unit.PersonalReputation < ArenaTuning.StarSellReputationFloor)
        {
            return Bad(ref reason, RejectReason.InvalidTarget,
                $"个人名声 {unit.PersonalReputation:0.##} 不到 {ArenaTuning.StarSellReputationFloor:0.##}，不是明星");
        }

        var price = StarOffer(unit);
        world.Town.ProduceCoin(price);
        world.Town.SettleAutoRepayment(price);

        world.NoteStarSale(unit, price);
        world.Roster.LeaveTown(unit);
        return true;
    }

    private static bool AssignPost(
        ArenaWorld world, string args, ref string canonicalArgs, ref string reason)
    {
        if (!ReplayArgs.Split(args, 2, out var two))
        {
            return Bad(ref reason, RejectReason.InvalidArgs,
                $"AssignPost 要 2 段（人 id|目标 id），收到 {Count(args)} 段");
        }

        if (two[0].Length == 0 || two[1].Length == 0)
        {
            return Bad(ref reason, RejectReason.InvalidArgs, "两段都不能是空串");
        }

        canonicalArgs = ReplayArgs.Join(two[0], two[1]);
        var unit = world.Roster.Find(two[0]);
        if (unit is null || unit.LeftTown)
        {
            return Bad(ref reason, RejectReason.InvalidTarget, $"{two[0]} 不在册");
        }

        var target = two[1];

        // 土壤那一档：<c>soil:&lt;象限&gt;_&lt;层&gt;</c>。
        if (target.StartsWith("soil:", StringComparison.Ordinal))
        {
            var parts = target["soil:".Length..].Split('_');
            if (parts.Length != 2
                || !ReplayArgs.TryInt(parts[0], out var sector)
                || !ReplayArgs.TryInt(parts[1], out var layer))
            {
                return Bad(ref reason, RejectReason.InvalidArgs, "土壤岗位那一档写作 soil:象限_层");
            }

            // 判据只读逻辑层那一口（在册 / 活着 / Worker / 已挖开）；UI 不再另写一份。
            switch (world.SoilAssignBlockerOf(unit.Id, sector, layer))
            {
                case SoilAssignBlocker.OutOfRange:
                    return Bad(ref reason, RejectReason.InvalidArgs, "象限或层越界");
                case SoilAssignBlocker.NotInRoster:
                    return Bad(ref reason, RejectReason.InvalidTarget, $"{unit.Id} 不在册");
                case SoilAssignBlocker.Dead:
                    return Bad(ref reason, RejectReason.InvalidTarget, $"{unit.Id} 已经死了");
                case SoilAssignBlocker.NotWorker:
                    return Bad(ref reason, RejectReason.InvalidTarget, "采矿岗只收工作人员");
                case SoilAssignBlocker.NotDug:
                    return Bad(ref reason, RejectReason.InvalidPhase, "那一格还没挖开，没有采矿岗");
            }

            world.Roster.AssignPost(unit, target);
            world.Soil.AssignMiners(sector, layer, world.Roster.PostedCount(target));
            return true;
        }

        var building = world.Construction.Find(target);
        if (building is null)
        {
            return Bad(ref reason, RejectReason.InvalidTarget, $"{target} 不在册");
        }

        if (!TryPostToBuilding(world, unit, target, building.BlueprintId, ref reason)) return false;

        PlaceOnDeck(world, unit, world.DoorDeckOf(target));
        return true;
    }

    /// <summary>
    /// 把一个镇民派到某栋建筑的岗位上 —— <b>岗位校验与写口的唯一出处</b>。
    ///
    /// <para><b>两个调用方共用这一份</b>：<c>AssignPost</c> 动词（土壤面板 / 调试桥那一发）
    /// 与上帝之手的 <c>DropToBuilding</c>（拖人进楼那一发）。两套判据漂开之后，
    /// 同一栋楼会从两条路得出不同的答案（「约束唯一出处」要防的就是这个）。</para>
    ///
    /// <para>顺序固定：<b>先看有没有岗位 → 再看职业相不相符 → 最后看满没满</b>。
    /// 顺序反过来时，一栋不设岗的楼会先报「满了」，玩家按那条提示怎么腾都腾不出来。</para>
    /// </summary>
    private static bool TryPostToBuilding(
        ArenaWorld world, ArenaUnit unit, string postId, string blueprintId, ref string reason)
    {
        if (!CanPostToBuilding(world, unit, postId, blueprintId, unit.Role, ref reason)) return false;

        world.Roster.AssignPost(unit, postId);
        return true;
    }

    /// <summary>
    /// 派岗的<b>只读校验</b>（不写任何字段）：给 <c>DropToBuilding</c> 在转职之前先问一遍，
    /// 拒绝路径因此没有副作用。<paramref name="role"/> 是「落下去之后会是什么职业」。
    /// </summary>
    private static bool CanPostToBuilding(
        ArenaWorld world, ArenaUnit unit, string postId, string blueprintId, UnitRole role,
        ref string reason)
    {
        var capacity = world.Roster.PostCapacity(blueprintId);
        if (capacity <= 0)
        {
            return Bad(ref reason, RejectReason.InvalidTarget, $"{blueprintId} 没有岗位（宿舍床位不是岗位）");
        }

        if (!PeopleRoster.RoleFitsPost(role, blueprintId))
        {
            return Bad(ref reason, RejectReason.InvalidTarget, $"{role} 上不了 {blueprintId}");
        }

        // 已经站在这栋楼上的人换回同一栋不算「再塞一个人」。
        if (unit.Post != postId && world.Roster.PostedCount(postId) >= capacity)
        {
            return Bad(ref reason, RejectReason.FullCapacity,
                $"岗位已满：{postId} 岗位{Gap("个", 1.0, capacity - world.Roster.PostedCount(postId))}");
        }

        return true;
    }

    private static bool SetNonCombatant(
        ArenaWorld world, string args, ref string canonicalArgs, ref string reason)
    {
        if (!ReplayArgs.Split(args, 2, out var two))
        {
            return Bad(ref reason, RejectReason.InvalidArgs,
                $"SetNonCombatant 要 2 段（人 id|0 或 1），收到 {Count(args)} 段");
        }

        if (!ReplayArgs.TryInt(two[1], out var flag) || (flag != 0 && flag != 1))
        {
            return Bad(ref reason, RejectReason.InvalidArgs, "第二段只认 0 或 1");
        }

        if (two[0].Length == 0)
        {
            return Bad(ref reason, RejectReason.InvalidArgs, "id 那一段不能是空串");
        }

        canonicalArgs = ReplayArgs.Join(two[0], ReplayArgs.Int(flag));
        var unit = world.Roster.Find(two[0]);
        if (unit is null || unit.LeftTown || !unit.IsTownsperson)
        {
            return Bad(ref reason, RejectReason.InvalidTarget, $"{two[0]} 不是在册镇民");
        }

        unit.NonCombatant = flag == 1;
        if (unit.NonCombatant)
        {
            // 立刻移出参战名单并回原建筑槽（那栋没了就落到最近可通行格）。
            var at = unit.Post.Length > 0 && world.Construction.Find(unit.Post) is not null
                ? world.DoorDeckOf(unit.Post)
                : world.NearestFreeDeckCell(unit.DeckX, unit.DeckZ);
            HoldAt(world, unit, at.X, at.Z);
        }

        return true;
    }

    /// <summary>
    /// 复活一具尸体 —— <b>教堂在册表是那一份真值</b>。
    ///
    /// <para><b>两处来路</b>：① 教堂在册表里那一条（拖进教堂登记过的，教堂菜单点「救人」走这一条）；
    /// ② 还在场上没搬走的尸体点位物（沿用既有那一档规则）。两处都先问「这一具记着是谁倒下」，
    /// 再问魂石够不够（<see cref="ArenaTuning.ReviveSoulStones"/>），最后才动手。</para>
    ///
    /// <para><b>过期的那一条救不回来</b>：在册超过 <see cref="ArenaTuning.ReviveWindowGameHours"/>
    /// 个游戏小时就过期，这一发按 <see cref="RejectReason.InvalidTarget"/> 拒，账一格不动。</para>
    /// </summary>
    private static bool ReviveCorpse(
        ArenaWorld world, string args, ref string canonicalArgs, ref string reason)
    {
        if (!ReplayArgs.Split(args, 1, out var one))
        {
            return Bad(ref reason, RejectReason.InvalidArgs, $"ReviveCorpse 要 1 段（尸体 id），收到 {Count(args)} 段");
        }

        if (one[0].Length == 0)
        {
            return Bad(ref reason, RejectReason.InvalidArgs, "id 那一段不能是空串");
        }

        canonicalArgs = ReplayArgs.Join(one[0]);

        if (world.FindByBlueprint("Church").Length == 0)
        {
            return Bad(ref reason, RejectReason.InvalidTarget, "教堂不在册");
        }

        // 先查教堂在册表（那一本是真值）；查不到再查场上的尸体点位物。
        var registered = world.FindChurchCorpse(one[0]);
        string corpseId;
        string personId;

        if (registered is not null)
        {
            if (world.IsChurchCorpseExpired(registered))
            {
                var ageHours = registered.AgeGameMinutes
                    / (double)(ArenaTime.StepsPerGameHour / ArenaTime.StepsPerGameMinute);
                return Bad(ref reason, RejectReason.InvalidTarget,
                    $"{one[0]} 在册 {Num(ageHours)} 游戏小时，上限 {ArenaTuning.ReviveWindowGameHours} 游戏小时，"
                    + $"过期 {Num(ageHours - ArenaTuning.ReviveWindowGameHours)} 小时");
            }

            corpseId = registered.CorpseId;
            personId = registered.Name;
        }
        else
        {
            var item = world.Items.Find(one[0]);
            if (item is null || item.Kind != PointItemKind.Corpse)
            {
                return Bad(ref reason, RejectReason.InvalidTarget,
                    $"{one[0]} 不是在场尸体，也不是教堂在册的尸体");
            }

            corpseId = item.Id;
            personId = item.Payload;
        }

        // 复用尸体的名：尸体位物件的载荷记着是谁倒下的。
        if (personId.Length == 0)
        {
            return Bad(ref reason, RejectReason.InvalidTarget, $"{one[0]} 没记着是谁倒下，复活不了");
        }

        var unit = world.Roster.Find(personId);
        if (unit is null)
        {
            return Bad(ref reason, RejectReason.InvalidTarget, $"{one[0]} 没记着是谁倒下，复活不了");
        }

        if (!world.BeginRevive(corpseId, personId, world.Tick))
        {
            return Bad(ref reason, RejectReason.InsufficientSoulStone,
                Gap("枚", ArenaTuning.ReviveSoulStones, world.Town.SoulStone));
        }

        // 复活走成了：在册那一条摘掉（不在册的那一档没这一条可摘）。
        world.RemoveChurchCorpse(corpseId);
        return true;
    }

    // === 账目与远行（5） ===

    private static bool SetPricing(
        ArenaWorld world, string args, ref string canonicalArgs, ref string reason)
    {
        if (!ReplayArgs.Split(args, 3, out var three))
        {
            return Bad(ref reason, RejectReason.InvalidArgs,
                $"SetPricing 要 3 段（酒店|饭店|门票），收到 {Count(args)} 段");
        }

        if (!ReplayArgs.TryNumber(three[0], out var hotel)
            || !ReplayArgs.TryNumber(three[1], out var restaurant)
            || !ReplayArgs.TryNumber(three[2], out var ticket))
        {
            return Bad(ref reason, RejectReason.InvalidArgs, "三段都得是数字");
        }

        canonicalArgs = ReplayArgs.Join(
            ReplayArgs.Number(hotel), ReplayArgs.Number(restaurant), ReplayArgs.Number(ticket));

        if (!TownAccounts.InPricingRange(hotel)
            || !TownAccounts.InPricingRange(restaurant)
            || !TownAccounts.InPricingRange(ticket))
        {
            return Bad(ref reason, RejectReason.InvalidArgs,
                $"三个倍率各取 {ArenaTuning.PricingMinMultiplier}..{ArenaTuning.PricingMaxMultiplier}");
        }

        world.Town.TrySetPricing(hotel, restaurant, ticket);
        return true;
    }

    private static bool BuySupply(
        ArenaWorld world, string args, ref string canonicalArgs, ref string reason)
    {
        if (!ReplayArgs.Split(args, 1, out var one))
        {
            return Bad(ref reason, RejectReason.InvalidArgs, $"BuySupply 要 1 段（食物数），收到 {Count(args)} 段");
        }

        if (!ReplayArgs.TryInt(one[0], out var food) || food <= 0)
        {
            return Bad(ref reason, RejectReason.InvalidArgs, "食物数要正整数");
        }

        canonicalArgs = ReplayArgs.Join(ReplayArgs.Int(food));
        if (!world.IsDocked)
        {
            return Bad(ref reason, RejectReason.InvalidPhase, "停靠期才买得到食物");
        }

        // 城内价 = 基准价 × 所在城物价系数（与空艇价同一份基准，判定见 ArenaSetup.FoodPriceOf）。
        var price = food * FoodPriceOf(world.CityPriceFactor);
        if (!world.Town.TrySpendCoin(price))
        {
            return Bad(ref reason, RejectReason.InsufficientCoin, Gap("金", price, world.Town.Coin));
        }

        world.Town.ProduceFood(food);
        world.Voyage.AddGranaryFood(food);
        return true;
    }

    private static bool PayDebt(
        ArenaWorld world, string args, ref string canonicalArgs, ref string reason)
    {
        if (!ReplayArgs.Split(args, 2, out var two))
        {
            return Bad(ref reason, RejectReason.InvalidArgs,
                $"PayDebt 要 2 段（势力 id|金额），收到 {Count(args)} 段");
        }

        if (!ReplayArgs.TryNumber(two[1], out var amount) || amount <= 0.0)
        {
            return Bad(ref reason, RejectReason.InvalidArgs, "金额要正数");
        }

        canonicalArgs = ReplayArgs.Join(two[0], ReplayArgs.Number(amount));
        if (!world.IsDocked)
        {
            return Bad(ref reason, RejectReason.InvalidPhase, "停靠期才划得转款");
        }

        if (world.Town.CreditorOf(two[0]) is null)
        {
            return Bad(ref reason, RejectReason.InvalidTarget, $"{two[0]} 不是已登记的债主");
        }

        if (world.Town.Coin < amount)
        {
            return Bad(ref reason, RejectReason.InsufficientCoin, Gap("金", amount, world.Town.Coin));
        }

        if (world.Town.PayDebt(two[0], amount) <= 0.0)
        {
            return Bad(ref reason, RejectReason.InvalidTarget, $"{two[0]} 那一家没账可还");
        }

        return true;
    }

    private static bool SetDebtPolicy(
        ArenaWorld world, string args, ref string canonicalArgs, ref string reason)
    {
        if (!ReplayArgs.Split(args, 2, out var two))
        {
            return Bad(ref reason, RejectReason.InvalidArgs,
                $"SetDebtPolicy 要 2 段（策略|优先债主），收到 {Count(args)} 段");
        }

        if (!Enum.TryParse<DebtPolicy>(two[0], out var policy))
        {
            return Bad(ref reason, RejectReason.InvalidArgs,
                $"策略解析不出「{two[0]}」（None / AutoOrder / AutoTarget）");
        }

        canonicalArgs = ReplayArgs.Join(policy.ToString(), two[1]);

        var priority = two[1] == "-" ? string.Empty : two[1];
        if (policy == DebtPolicy.AutoTarget && priority.Length > 0 && world.Town.CreditorOf(priority) is null)
        {
            return Bad(ref reason, RejectReason.InvalidTarget, $"{priority} 不是已登记的债主");
        }

        world.Town.SetDebtPolicy(policy, priority);
        return true;
    }

    /// <summary>
    /// 发薪：把应发的工钱一次结清（<b>只结应发的那一份</b>，不多给也不少给）。
    ///
    /// <para><b>停靠期才发得出薪</b>（spec §「账本面板 · 发薪」）：在海上付不了薪。
    /// 钱那一头走 <see cref="ArenaWorld.TryPayWages"/> —— 那是世界自己的钱路
    /// （金币账那一条），动词不另写一遍扣钱，八条守恒等式才照样平。</para>
    /// </summary>
    private static bool PayWages(ArenaWorld world, ref string canonicalArgs, ref string reason)
    {
        canonicalArgs = string.Empty;

        if (!world.IsDocked)
        {
            return Bad(ref reason, RejectReason.InvalidPhase, "停靠期才发得出薪");
        }

        if (world.WagesDue <= 0.0)
        {
            return Bad(ref reason, RejectReason.InvalidArgs, "没有应发的工钱");
        }

        if (world.Town.Coin < world.WagesDue)
        {
            return Bad(ref reason, RejectReason.InsufficientCoin,
                Gap("金", world.WagesDue, world.Town.Coin));
        }

        if (!world.TryPayWages())
        {
            return Bad(ref reason, RejectReason.InsufficientCoin,
                Gap("金", world.WagesDue, world.Town.Coin));
        }

        return true;
    }

    private static bool Teleport(
        ArenaWorld world, string args, ref string canonicalArgs, ref string reason)
    {
        if (!ReplayArgs.Split(args, 3, out var three))
        {
            return Bad(ref reason, RejectReason.InvalidArgs,
                $"Teleport 要 3 段（人 id 列表|矿石|材料），收到 {Count(args)} 段");
        }

        if (!ReplayArgs.TryInt(three[1], out var ore) || !ReplayArgs.TryInt(three[2], out var material))
        {
            return Bad(ref reason, RejectReason.InvalidArgs, "矿石 / 材料那两段要整数");
        }

        if (three[0].Length == 0)
        {
            return Bad(ref reason, RejectReason.InvalidArgs, "人 id 列表那一段不能是空串");
        }

        canonicalArgs = ReplayArgs.Join(three[0], ReplayArgs.Int(ore), ReplayArgs.Int(material));

        if (ore < 0 || material < 0)
        {
            return Bad(ref reason, RejectReason.InvalidArgs, "数量不能是负数");
        }

        if (ore + material > ArenaTuning.TeleportMaxSmallGoods)
        {
            return Bad(ref reason, RejectReason.InvalidArgs,
                $"单次最多 {ArenaTuning.TeleportMaxSmallGoods} 件小货");
        }

        if (!world.IsDocked)
        {
            return Bad(ref reason, RejectReason.InvalidPhase, "停靠期才传得过去");
        }

        if (world.FindByBlueprint("TeleportArray").Length == 0)
        {
            return Bad(ref reason, RejectReason.InvalidTarget, "传送阵不在册");
        }

        var ids = SplitIds(three[0]);
        foreach (var id in ids)
        {
            var u = world.Roster.Find(id);
            if (u is null || u.LeftTown)
            {
                return Bad(ref reason, RejectReason.InvalidTarget, $"{id} 不在册");
            }

            // 只收镇民：帮工、游客、敌人传不过去（判据在 ArenaWorld.IsTownsperson 一处）。
            if (!world.IsTownsperson(id))
            {
                return Bad(ref reason, RejectReason.InvalidTarget, $"{id} 不是镇民，传不过去");
            }
        }

        if (ore > 0 && !world.Town.TrySpendOre(ore))
        {
            return Bad(ref reason, RejectReason.InsufficientMaterial, Gap("矿石不够", "件", ore, world.Town.Ore));
        }

        if (material > 0 && !world.Town.TrySpendSpecialMaterial(material))
        {
            if (ore > 0) world.Town.ProduceOre(ore);
            return Bad(ref reason, RejectReason.InsufficientMaterial, Gap("特殊材料不够", "份", material, world.Town.SpecialMaterial));
        }

        var price = (ids.Count * ArenaTuning.TeleportCoinPerPerson)
            + ((ore + material) * ArenaTuning.TeleportCoinPerSmallGood);
        if (price > 0.0 && !world.Town.TrySpendCoin(price))
        {
            if (ore > 0) world.Town.ProduceOre(ore);
            if (material > 0) world.Town.ProduceSpecialMaterial(material);
            return Bad(ref reason, RejectReason.InsufficientCoin, Gap("金", price, world.Town.Coin));
        }

        // 单向：人走、货走，本镇这一侧相应减员减货。
        foreach (var id in ids) world.Roster.LeaveTown(world.Roster.Find(id)!);
        return true;
    }

    // === 共用小工具 ===

    /// <summary>
    /// 缺口那一句话的固定句式：<b>要多少 + 有多少 + 缺多少</b>。
    ///
    /// <para><b>三个数都要</b>：只写「差 20」玩家不知道要 50 份还是 200 份；
    /// 只写「有 30」玩家不知道差在哪儿。</para>
    /// </summary>
    private static string Gap(string unit, double want, double have)
        => $"要 {Num(want)} {unit}，有 {Num(have)} {unit}，缺 {Num(want - have)} {unit}";

    /// <summary>
    /// 带原因短句的那一档（短句与缺口数字之间用冒号隔开）——
    /// 短句与横幅上那一句重复时用它（那一档的短句太笼统，细节要分说）。
    /// </summary>
    private static string Gap(string reason, string unit, double want, double have)
        => $"{reason}：{Gap(unit, want, have)}";

    /// <summary>读数上屏的数（不变文化，两位小数收尾）。</summary>
    private static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>
    /// 通缉拒停那一句话（<b>谁在通缉 + 欠多少</b>）。
    /// </summary>
    private static string WantedShortText(ArenaWorld world, string cityId, string placeName)
    {
        foreach (var c in world.Town.Creditors)
        {
            if (c.Outstanding <= 0.0) continue;

            foreach (var t in c.TownIds)
            {
                if (t != cityId) continue;
                return $"债主 {c.DisplayName} 通缉，{placeName} 拒停，欠 {Num(c.Outstanding)} 金";
            }
        }

        return $"正被通缉，{placeName} 拒停";
    }

    /// <summary>
    /// 空艇成交窗口那一句话（<b>这一段还剩几艘 / 窗口还剩几分钟</b>）。
    /// </summary>
    private static string AirshipWindowShortText(ArenaWorld world)
    {
        if (world.AirshipVisitsLeft <= 0)
        {
            return $"不在空艇成交窗口内：这一段的空艇都成交完了（还剩 0 艘）";
        }

        return "不在空艇成交窗口内：成交窗口 "
            + $"{ArenaTuning.AirshipWindowGameMinutes} 游戏分钟已过完（还剩 0 游戏分钟）";
    }

    /// <summary>
    /// 喂食缺口那一句话（固定句式：<b>要多少 + 有多少 + 缺多少</b>，并点名差在哪一处）。
    ///
    /// <para>三处都得够：镇仓食物、兽粮、饱食度余量 —— 差哪一处就写哪一处。</para>
    /// </summary>
    private static string FeedShortText(ArenaWorld world, double count)
    {
        var room = world.SatiationRoomFood;
        var granary = world.Voyage.GranaryFood;
        var town = world.Town.Food;

        if (room <= granary && room <= town)
        {
            return $"饱食度 {Num(world.Voyage.Satiation)}/{Num(ArenaTuning.SatiationMax)}，"
                + $"还能吃 {Num(room)} 份，要喂 {Num(count)} 份，缺 {Num(count - room)} 份";
        }

        if (granary <= town)
        {
            return Gap("兽粮不够", "份", count, granary);
        }

        return Gap("镇仓食物不够", "份", count, town);
    }

    /// <summary>
    /// 门槛那一档的中文明细：二阶要三项同时满足（第二层 + 洞穴已清剿 + 已嵌入魔晶），
    /// 缺哪一项就写哪一项 —— 一句话说不清「进不去」到底卡在哪。
    /// </summary>
    private static string GateShortText(ArenaWorld world, BeastTier target)
    {
        if (target != BeastTier.Tier2)
        {
            // 三阶的规则还在表里，但地下只有两层 —— 通向它的门是关的。
            return $"三阶不在本切片内：地下只有 {UndergroundSoil.Layers} 层，第 {ArenaTuning.Tier3GateLayer} 层挖不到";
        }

        var sector = UndergroundSoil.CaveSector;
        var layer = UndergroundSoil.NativeCaveLayer;

        if (!world.Soil.IsDug(sector, layer))
        {
            return $"要先把象限 {sector} 挖到第 {layer} 层（洞穴就在那一层）";
        }

        if (!world.Soil.IsCleared(sector, layer))
        {
            return "那处洞穴还没清剿成功（四只全灭才算）";
        }

        return "洞穴那一格还没嵌入魔晶";
    }

    private static bool Bad(ref string reason, RejectReason why, string detail)
    {
        reason = $"{RejectedText(why)}（{detail}）";
        return false;
    }

    /// <summary>
    /// 进阶仪式期间的那道闸 —— 仪式期间这几个动词一律拒。
    /// 四个动词各写一遍的话，改一次口径要动四处。
    /// </summary>
    /// <returns>被拒时为 true（调用方直接 <c>return false</c>）。</returns>
    private static bool BlockedByRitual(ArenaWorld world, ref string reason, string detail)
    {
        if (!world.InRitual) return false;
        Bad(ref reason, RejectReason.InvalidPhase, detail);
        return true;
    }

    private static bool SkipSynthetic(
        string args, int segments, ref string canonicalArgs, ref string reason)
    {
        if (SyntheticVerbs.Skip(args, segments, out var canonical, out var why))
        {
            canonicalArgs = canonical;
            reason = string.Empty;
            return true;
        }

        reason = $"{RejectedText(RejectReason.InvalidArgs)}（{why}）";
        return false;
    }

    private static IReadOnlyList<string> SplitIds(string token)
    {
        if (token.Length == 0 || token == "-") return [];
        return token.Split(',', StringSplitOptions.RemoveEmptyEntries);
    }

    private static void HoldAt(ArenaWorld world, ArenaUnit? unit, double deckX, double deckZ)
    {
        if (unit is null) return;

        var stepper = world.Roster.DeckStepper;
        stepper.SetCountedInSeparation(unit.Id, false);
        stepper.Issue(unit.Id, NavCommand.Hold(DeckGeometry.Nav(deckX, deckZ)));
    }

    /// <summary>落位：<c>ApplyMove</c> 到门口点、<c>Hold</c>、关闭避让（入槽那一档的同一口）。</summary>
    private static void PlaceOnDeck(ArenaWorld world, ArenaUnit unit, NavPoint atDeck)
    {
        var stepper = world.Roster.DeckStepper;
        var nav = DeckGeometry.Nav(atDeck.X, atDeck.Z);
        stepper.ApplyMove(unit.Id, nav, 0.0);
        stepper.SetCountedInSeparation(unit.Id, false);
        stepper.Issue(unit.Id, NavCommand.Hold(nav));
    }

    private static int Count(string args) => args.Split(ReplayArgs.Separator).Length;
}
