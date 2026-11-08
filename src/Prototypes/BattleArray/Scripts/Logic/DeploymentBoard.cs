#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DemoFactory.Prototypes.BattleArray.Logic;

/// <summary>
/// 一局走到哪一步 —— 玩家的操作全部集中在<b>布阵期</b>，战斗期间只有一件事可以做：随时暂停
/// （design.md 1 节那句原话）。
///
/// 这一格为什么要在逻辑层而不是表现层：「现在还能不能摆子」「暂停读出来的是布阵的话还是战斗的话」
/// 都是真值，不是画面状态。它进 <see cref="BattleWorld.DebugSnapshot"/> 的第一行，
/// UI 与直达口读的都是同一句（显示即真相）。
/// </summary>
public enum BattlePhase
{
    /// <summary>布阵期：兵在格子上随便摆，世界一步都不演化。开局口把它推到 <see cref="Fighting"/>。</summary>
    Deploying,

    /// <summary>战斗已开打 —— 摆子口在这一档起效不了（要改阵得先重开一局）。</summary>
    Fighting,

    /// <summary>已判定（胜/败/同尽/超时判赢），结算面板读的就是这一档。</summary>
    Settled,
}

/// <summary>
/// 摆子 / 撤子 / 移位的回执 —— <b>拒绝必须带理由</b>，不许静默兜到别的格子或别的兵种
/// （「事件发射前置」那条契约：关键决策要有能被读出来的结果）。
/// </summary>
/// <param name="Ok">这一笔成没成。</param>
/// <param name="Reason">成了写「摆到哪、还剩多少钱」，没成写「为什么不行」—— 直接是给人看的那一句。</param>
/// <param name="Kind">涉及的兵种（撤子时是原来那个）。</param>
public readonly record struct PlacementOutcome(bool Ok, string Reason, TroopKind Kind, int Row, int Column);

/// <summary>
/// 布阵棋盘 —— 我方那 15 个格子现在站着谁，以及这笔名义额度还剩多少。
///
/// 它是<b>布阵期的唯一真值</b>：UI 的网格高亮、兵源面板的已摆数、开局时的入场名单，
/// 全部从这里读；写只有三条路（<see cref="Place"/> / <see cref="Remove"/> / <see cref="Move"/>），
/// 表现层的拖放和 <c>PlaceUnitDebug</c> 直达口走的是同一条 <see cref="Place"/>。
/// 摆完用 <see cref="ToFormation"/> 交给 <see cref="BattleWorld"/> —— 棋盘本身不打仗。
///
/// 三条已定规则落在这里（design.md 2 节 第 4、8 条与 5.1 节）：
/// - <b>格子数即出兵上限</b>：5×3 = 15 格，一兵一格，全兵种一律占 1 格；
/// - <b>钱够买满</b>：初始金 = 15 × 最贵兵种单价 ⇒ 任意组合都能摆满，钱退化成名义额度（「第 8 条的下游后果」）；
///   价差只展示、不做平衡，不参与任何结算。
/// - <b>兵种可重复购买</b>：同一格档摆多少个都行，只要格子和钱够。
///
/// 架构位置：Scripts/Logic —— 纯 C#，零 Godot 类型（进 dotnet test）。
/// </summary>
public sealed class DeploymentBoard
{
    /// <summary>格子总数（= 出兵上限，5.1 节：每方 5 列 × 3 行）。</summary>
    public const int CellCount = BattleContract.Rows * BattleContract.Columns;

    /// <summary>自定义摆法在直达口与报告里的 ASCII 键（<see cref="ToFormation"/> 给的默认值）。</summary>
    public const string CustomKey = "deployed";

    /// <summary>空格用 null 表示：一维数组按「行 × 列」铺开，遍历顺序就是出场顺序（行 0→2、列 0→4）。</summary>
    private readonly TroopKind?[] _cells = new TroopKind?[CellCount];

    /// <summary>
    /// 新开一块棋盘。<see cref="FillFromDefaultFormation"/> 为真时按「盾墙在前（常规线）」装满 15 格 ——
    /// M4/M5 那一局的开局阵面就是它，所以布阵期一进来看到的阵、与对照跑里那一档，是同一份数据。
    /// </summary>
    public DeploymentBoard(bool fillFromDefaultFormation = true)
    {
        StartingPurse = CellCount * BattleContract.Kinds.Max(BattleContract.PriceOf);
        Remaining = StartingPurse;
        if (fillFromDefaultFormation) FillFrom(Formations.ShieldWall);
    }

    /// <summary>这一关发下来的钱（= 15 × 最贵单价，代码算出来、不硬写数字：将来调价差也不会「买不满」）。</summary>
    public int StartingPurse { get; }

    /// <summary>还没花的钱。</summary>
    public int Remaining { get; private set; }

    /// <summary>已花出去 = 摆上场的兵各自的单价之和（撤子会退钱，所以它跟着棋盘变）。</summary>
    public int Spent => StartingPurse - Remaining;

    /// <summary>棋盘一变（摆/撤/移/清/重开）就喊一声 —— UI 只读它，别自己攒一份阵面。</summary>
    public event Action? Changed;

    /// <summary>某一格站着谁（越界查也当空格，不抛 —— 网格渲染要整片遍历）。</summary>
    public TroopKind? At(int row, int column)
        => row < 0 || row >= BattleContract.Rows || column < 0 || column >= BattleContract.Columns
            ? null
            : _cells[Index(row, column)];

    /// <summary>已摆的兵（按出场顺序：行 0→2、列 0→4）。</summary>
    public IReadOnlyList<UnitPlacement> Placements
    {
        get
        {
            var list = new List<UnitPlacement>(CellCount);
            for (var row = 0; row < BattleContract.Rows; row++)
            {
                for (var column = 0; column < BattleContract.Columns; column++)
                {
                    if (_cells[Index(row, column)] is { } kind) list.Add(new UnitPlacement(kind, row, column));
                }
            }
            return list;
        }
    }

    /// <summary>场上兵数（格子数就是出兵上限，这一格到 15 就该报「摆不下了」）。</summary>
    public int Filled => Placements.Count;

    /// <summary>该兵种已摆几个（兵源面板上「弓手 ×4」那一格）。</summary>
    public int CountOf(TroopKind kind) => Placements.Count(p => p.Kind == kind);

    /// <summary>摆一个兵到指定格。已占、钱不够、越界都被拒并给出人看得懂的理由。</summary>
    public PlacementOutcome Place(TroopKind kind, int row, int column)
    {
        if (!InBounds(row, column)) return Rejected(kind, row, column, OutOfBoundsReason(row, column));

        var price = BattleContract.PriceOf(kind);
        if (_cells[Index(row, column)] is { } occupant)
        {
            return Rejected(kind, row, column,
                $"({row},{column}) 已经站着{DisplayName(occupant)}（{BattleContract.KeyOf(occupant)}）—— 要换位先撤掉或直接拖过去，一格只站一个兵");
        }
        if (price > Remaining)
        {
            return Rejected(kind, row, column,
                $"钱不够：{Name(kind)}要 {price}，只剩 {Remaining}（撤掉别的兵可以退钱）");
        }

        _cells[Index(row, column)] = kind;
        Remaining -= price;
        Changed?.Invoke();
        return new PlacementOutcome(true,
            $"已把{Name(kind)}摆到 第{row + 1}排 第{column + 1}列（花 {price}，剩 {Remaining}；场上 {Filled}/{CellCount} 格）",
            kind, row, column);
    }

    /// <summary>撤掉某一格的兵（钱退回额度）。</summary>
    public PlacementOutcome Remove(int row, int column)
    {
        if (!InBounds(row, column)) return Rejected(TroopKind.Tank, row, column, OutOfBoundsReason(row, column));
        if (_cells[Index(row, column)] is not { } kind)
            return Rejected(TroopKind.Tank, row, column, $"({row},{column}) 本来就空着，没什么可撤");

        _cells[Index(row, column)] = null;
        Remaining += BattleContract.PriceOf(kind);
        Changed?.Invoke();
        return new PlacementOutcome(true, $"已撤掉{Name(kind)}，退回 {BattleContract.PriceOf(kind)}（剩 {Remaining}；场上 {Filled}/{CellCount} 格）",
            kind, row, column);
    }

    /// <summary>把一格的兵挪到另一格（挪动不动钱：兵已经买过了）。</summary>
    public PlacementOutcome Move(int fromRow, int fromColumn, int toRow, int toColumn)
    {
        if (!InBounds(fromRow, fromColumn)) return Rejected(TroopKind.Tank, fromRow, fromColumn, OutOfBoundsReason(fromRow, fromColumn));
        if (_cells[Index(fromRow, fromColumn)] is not { } kind)
            return Rejected(TroopKind.Tank, fromRow, fromColumn, $"({fromRow},{fromColumn}) 是空格，没有兵可挪");
        if (!InBounds(toRow, toColumn)) return Rejected(kind, toRow, toColumn, OutOfBoundsReason(toRow, toColumn));
        if (_cells[Index(toRow, toColumn)] is { } occupant)
            return Rejected(kind, toRow, toColumn,
                $"要挪去的 ({toRow},{toColumn}) 站着{Name(occupant)}—— 一格只站一个兵，先撤它或换个格");

        _cells[Index(fromRow, fromColumn)] = null;
        _cells[Index(toRow, toColumn)] = kind;
        Changed?.Invoke();
        return new PlacementOutcome(true, $"{Name(kind)} 已从 第{fromRow + 1}排 第{fromColumn + 1}列 挪到 第{toRow + 1}排 第{toColumn + 1}列（钱没动，剩 {Remaining}）",
            kind, toRow, toColumn);
    }

    /// <summary>清空棋盘并退全款（重开布阵用）。</summary>
    public void Clear()
    {
        for (var i = 0; i < _cells.Length; i++) _cells[i] = null;
        Remaining = StartingPurse;
        Changed?.Invoke();
    }

    /// <summary>照一份现成摆法把棋盘装满（M4 的三档摆法、M7 的配比都从这里进布阵期，不用手摆 15 次）。</summary>
    public void FillFrom(Formation formation)
    {
        Clear();
        foreach (var placement in formation.Placements)
        {
            // 装满走内部登记：预设摆法本身就是「买得起的阵」，但钱照样扣 —— 这样「买满要花多少」在面板上读得出来
            _cells[Index(placement.Row, placement.Column)] = placement.Kind;
            Remaining -= BattleContract.PriceOf(placement.Kind);
        }
        Changed?.Invoke();
    }

    /// <summary>这份棋盘对应的摆法 —— 开局时递给 <see cref="BattleWorld"/> 的就是它。</summary>
    public Formation ToFormation(string key = CustomKey, string displayName = "自己摆的阵")
        => new(key, $"{displayName}（{Filled}/{CellCount} 格）", Placements);

    /// <summary>棋盘读数：第一行是钱与格数，之后每格一行（UI、日志与直达口读的是同一份）。</summary>
    public string DebugSnapshot()
    {
        var lines = new List<string>
        {
            $"布阵棋盘 格数={Filled}/{CellCount} 钱={Remaining}/{StartingPurse}（已花 {Spent}） " +
            string.Join(' ', BattleContract.Kinds.Select(k => $"{BattleContract.KeyOf(k)}×{CountOf(k)}")),
        };
        for (var row = 0; row < BattleContract.Rows; row++)
        {
            for (var column = 0; column < BattleContract.Columns; column++)
            {
                if (_cells[Index(row, column)] is not { } kind) continue;
                lines.Add($"({row},{column}) {BattleContract.KeyOf(kind)}「{DisplayName(kind)}」 hp={StatsOf(kind).Health} rng={F(StatsOf(kind).Range)} " +
                          $"价={BattleContract.PriceOf(kind)} 格中心=({F(BattleContract.CellCenter(Side.Player, row, column).X)}," +
                          $"{F(BattleContract.CellCenter(Side.Player, row, column).Y)})");
            }
        }
        return string.Join("\n", lines);
    }

    private static int Index(int row, int column) => row * BattleContract.Columns + column;

    private static bool InBounds(int row, int column)
        => row >= 0 && row < BattleContract.Rows && column >= 0 && column < BattleContract.Columns;

    private static string OutOfBoundsReason(int row, int column)
        => $"格子号越界：行要 0~{BattleContract.Rows - 1}、列要 0~{BattleContract.Columns - 1}，收到 ({row},{column})";

    /// <summary>回执里怎么称呼一个兵种：中文叫法在前（玩家读这个），ASCII 键在后（直达口与日志认这个）。</summary>
    private static string Name(TroopKind kind) => $"{DisplayName(kind)}（{BattleContract.KeyOf(kind)}）";

    /// <summary>中文名取自对阵那一侧的兵种卡（布阵期摆的是我方阵 ⇒ 人族那一套叫法）。</summary>
    private static string DisplayName(TroopKind kind)
        => UnitCards.Of(UnitCards.RaceOf(Side.Player), kind).DisplayName;

    /// <summary>
    /// 棋盘上一格读得出的数值，取自<b>我方这一族实际参战</b>的那张卡（M8 起不再直接读基准表）——
    /// 面板上念出的血与射程，就是开局以后那个兵用的数（显示即真相）。
    /// </summary>
    private static TroopCard StatsOf(TroopKind kind)
        => UnitCards.Of(UnitCards.RaceOf(Side.Player), kind).Stats;

    private PlacementOutcome Rejected(TroopKind kind, int row, int column, string reason)
        => new(false, reason, kind, row, column);

    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>把「第几排」说成人话时用的排数（row 0 = 最接近敌人的一排，5.1 节）。</summary>
    public static string RowName(int row) => row switch
    {
        0 => "前排",
        1 => "中排",
        _ => "后排",
    };
}
