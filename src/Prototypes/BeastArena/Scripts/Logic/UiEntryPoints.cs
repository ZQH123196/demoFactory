#nullable enable
using System;
using System.Collections.Generic;

namespace DemoFactory.Prototypes.BeastArena.Logic;

/// <summary>一条主线动词在界面上的落点类别。</summary>
public enum UiEntryKind
{
    /// <summary>按钮 / 面板里的可点控件（<see cref="UiEntryPoint.Control"/> 是控件名或上屏文字）。</summary>
    Button = 0,

    /// <summary>世界内手势（右键点地、拖拽、蓝图条落座那一类）。</summary>
    Gesture = 1,

    /// <summary>由世界自己触发，不是玩家能按的动词（出界转向那一档）。</summary>
    Automatic = 2,
}

/// <summary>
/// 一条界面入口：哪个动词、落在哪一件界面、点的是哪个控件。
/// </summary>
/// <param name="Verb">词表里的动词名（与 <see cref="ArenaSetup.VerbNames"/> 逐字相同）。</param>
/// <param name="Kind">入口类别。</param>
/// <param name="Control">控件名 / 上屏文字 / 手势符号（必须是 <paramref name="Source"/> 里真有的那一串）。</param>
/// <param name="Source">相对 <c>Scripts/</c> 的源文件名（含子目录）。</param>
/// <param name="Note">一句人话，说明玩家怎么点到它。</param>
public readonly record struct UiEntryPoint(
    string Verb, UiEntryKind Kind, string Control, string Source, string Note);

/// <summary>
/// 主线动词的界面入口表 —— <b>测试拿它与 <see cref="ArenaSetup.VerbNames"/> 逐条比对</b>。
///
/// <para><b>为什么住在逻辑层而不是 UI</b>：这条表是纯数据，测试工程只链 <c>Scripts/Logic/*.cs</c>；
/// 放在 UI 里就进不了测试进程，「缺一条就红」也就钉不住。表里每条都带源文件名与控件名，
/// 测试会回读那个文件确认控件真的存在 —— 表本身因此不会变成一份没人核对的愿望清单。</para>
///
/// <para><b><see cref="UiEntryKind.Automatic"/> 只留给「世界自动触发、玩家按不到」的那一条</b>：
/// <c>SteerOutOfBounds</c> 是出界之后的自动转向，不是玩家动词。其余业务动词一律要有可点的落点。</para>
/// </summary>
public static class UiEntryPoints
{
    /// <summary>全部入口（按词表次序排；测试也按这个次序比对）。</summary>
    public static readonly IReadOnlyList<UiEntryPoint> All =
    [
        // 航行与城镇（7）
        new("StartRun", UiEntryKind.Button, "开始游戏", "Main.cs", "标题屏「开始游戏」"),
        new("Dock", UiEntryKind.Button, "Dock", "Ui/CommandBar.cs", "命令栏「停靠」→ 目的地弹层"),
        new("SailTo", UiEntryKind.Gesture, "RightClicked", "Ui/InputRouter.cs", "右键点大地图空地"),
        new("PullAnchor", UiEntryKind.Button, "PullAnchor", "Ui/CommandBar.cs", "命令栏「拔锚」"),
        new("StartMatch", UiEntryKind.Button, "StartMatch", "Ui/BuildingMenu.cs", "竞技场右键菜单「开赛」"),
        new("TradeAirship", UiEntryKind.Button, "Trade", "Ui/MarketPanel.cs", "经营 → 市场 →「成交」"),
        new("SteerOutOfBounds", UiEntryKind.Automatic, "SteerOutOfBounds", "Logic/BigMapRoute.cs",
            "出界自动转向（世界触发，玩家按不到）"),

        // 上帝之手与法术（8）
        new("GrabEntity", UiEntryKind.Gesture, "DragStarted", "Ui/InputRouter.cs", "按住镇民 / 尸体 / 散货拖动"),
        new("DropToBuilding", UiEntryKind.Gesture, "DropToBuilding", "Ui/DragController.cs", "拖到建筑上松手"),
        new("DropToDeck", UiEntryKind.Gesture, "DropToDeck", "Ui/DragController.cs", "拖到甲板空地松手"),
        new("LiftBuilding", UiEntryKind.Button, "Lift", "Ui/BuildingMenu.cs", "建筑右键菜单「拔起」"),
        new("DropBuilding", UiEntryKind.Gesture, "PlacementGhost", "Ui/PlacementGhost.cs", "拔起后点甲板格落位"),
        new("SummonHelper", UiEntryKind.Button, "Summon", "Ui/HelperPopover.cs", "经营 → 人员与帮工 →「召唤」"),
        new("DismissHelper", UiEntryKind.Button, "Dismiss", "Ui/HelperPopover.cs", "经营 → 人员与帮工 →「遣散」"),
        new("CastHeal", UiEntryKind.Button, "CastHeal", "Ui/SelectionPanel.cs", "选中面板「治疗 1 法力」"),

        // 建造与土壤（5）
        new("PlaceBuilding", UiEntryKind.Gesture, "BlueprintChosen", "Ui/BlueprintBar.cs", "蓝图条选一张 → 甲板落座"),
        new("RemoveBuilding", UiEntryKind.Button, "Remove", "Ui/BuildingMenu.cs", "建筑右键菜单「拆除」"),
        new("RepairBuilding", UiEntryKind.Button, "Repair", "Ui/BuildingMenu.cs", "建筑右键菜单「修理」"),
        new("ExcavateSoil", UiEntryKind.Button, "Dig", "Ui/SoilPanel.cs", "土壤面板「开挖」"),
        new("EmbedCrystal", UiEntryKind.Button, "Embed", "Ui/SoilPanel.cs", "土壤面板「嵌入魔晶」"),

        // 人口与经营（7）
        new("Recruit", UiEntryKind.Button, "Recruit", "Ui/BuildingMenu.cs", "镇政府右键菜单「招募」"),
        new("ConvertRole", UiEntryKind.Button, "ConvertRole", "Ui/SelectionPanel.cs", "选中面板「转角斗」"),
        new("GrantFreedom", UiEntryKind.Button, "GrantFreedom", "Ui/SelectionPanel.cs", "选中面板「给自由」"),
        new("SellStar", UiEntryKind.Button, "SellStar", "Ui/SelectionPanel.cs", "选中面板「卖明星」"),
        new("AssignPost", UiEntryKind.Button, "Assign", "Ui/SoilPanel.cs", "土壤面板「派到此格」"),
        new("SetNonCombatant", UiEntryKind.Button, "NonCombatant", "Ui/SelectionPanel.cs", "选中面板「不参战」"),
        new("ReviveCorpse", UiEntryKind.Button, "Rescue", "Ui/BuildingMenu.cs", "教堂右键菜单「救人」"),

        // 账目与远行（6）
        new("SetPricing", UiEntryKind.Button, "ApplyPricing", "Ui/LedgerPanel.cs", "经营 → 账本与工资 →「应用」"),
        new("BuySupply", UiEntryKind.Button, "Buy", "Ui/MarketPanel.cs", "经营 → 市场 →「采购」"),
        new("PayDebt", UiEntryKind.Button, "PayDebt", "Ui/LedgerPanel.cs", "经营 → 账本与工资 →「还清」"),
        new("SetDebtPolicy", UiEntryKind.Button, "Policy", "Ui/LedgerPanel.cs", "经营 → 账本与工资 → 策略三选一"),
        new("PayWages", UiEntryKind.Button, "PayWages", "Ui/LedgerPanel.cs", "经营 → 账本与工资 →「发薪」"),
        new("Teleport", UiEntryKind.Button, "Teleport", "Ui/BuildingMenu.cs", "传送阵右键菜单「传送」"),

        // 巨兽本体与生态（7）
        new("AdvanceTier", UiEntryKind.Button, "Advance", "Ui/BeastPanel.cs", "巨兽面板「进阶」"),
        new("SwitchForm", UiEntryKind.Button, "Form", "Ui/BeastPanel.cs", "巨兽面板形态三选一"),
        new("Feed", UiEntryKind.Button, "FeedButton", "Ui/BeastPanel.cs", "巨兽面板「喂 50 食物」"),
        new("NpcFeed", UiEntryKind.Button, "NpcFeedButton", "Ui/BeastPanel.cs", "巨兽面板「让工人喂 5 食物」"),
        new("EatTarget", UiEntryKind.Button, "Eat", "Ui/ForagePanel.cs", "命令栏「吞食」→ 目标行"),
        new("RecallForage", UiEntryKind.Button, "Recall", "Ui/ForagePanel.cs", "吞食面板「召回」"),
        new("UseCrystal", UiEntryKind.Button, "Use", "Ui/BeastPanel.cs", "巨兽面板魔晶用途组（补魔 / 治疗 / 修理）"),
        new("SellCrystal", UiEntryKind.Button, "SellCrystal", "Ui/MarketPanel.cs", "经营 → 市场 →「出售魔晶」"),

        // 地下战斗（2）
        new("ClearCave", UiEntryKind.Button, "Cave", "Ui/SoilPanel.cs", "土壤面板「清剿」（失败后可再点）"),
        new("RetreatCave", UiEntryKind.Button, "Cave", "Ui/SoilPanel.cs", "清剿中同一颗按钮变「撤退」"),

        // 终局（2）
        new("RetryFinalDefence", UiEntryKind.Button, "RetryFinal", "Ui/BattleReportPanel.cs", "最终防守失败战报「立即重试」"),
        new("ContinueBusiness", UiEntryKind.Button, "ContinueBusiness", "Ui/SettlementPanel.cs", "终局结算「继续经营」"),
    ];

    /// <summary>按动词查入口（查不到回 false）。</summary>
    public static bool TryGet(string verb, out UiEntryPoint entry)
    {
        foreach (var item in All)
        {
            if (item.Verb == verb)
            {
                entry = item;
                return true;
            }
        }

        entry = default;
        return false;
    }

    /// <summary>这条动词有没有界面入口（<see cref="UiEntryKind.Automatic"/> 也算「世界自己会触发」）。</summary>
    public static bool HasEntry(string verb) => TryGet(verb, out _);

    /// <summary>界面入口表里没覆盖到的动词（测试判红用）。</summary>
    public static IReadOnlyList<string> MissingFrom(IEnumerable<string> verbs)
    {
        ArgumentNullException.ThrowIfNull(verbs);

        var missing = new List<string>();
        foreach (var verb in verbs)
        {
            if (!HasEntry(verb)) missing.Add(verb);
        }

        return missing;
    }
}
