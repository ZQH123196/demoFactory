#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace DemoFactory.Prototypes.MistCity.Logic;

/// <summary>牌种。名字是玩法内容，进 Journal 与快照，所以一个词都不许中途改。</summary>
public enum CardKind
{
    /// <summary>柴：起点料，能派工也能当被叠的那张。</summary>
    Wood,

    /// <summary>石：起点料，比柴慢。</summary>
    Stone,

    /// <summary>泉眼：产粮的牌，短间隔（急）。</summary>
    Spring,

    /// <summary>田垄：产粮的牌，长间隔（稳）。</summary>
    Furrow,

    /// <summary>灰烬炉：合成出来的产粮牌，间隔居中。</summary>
    Kiln,

    /// <summary>夜露瓶：合成出来的产粮牌，最短间隔（最急）。</summary>
    DewVial,

    /// <summary>棚：房屋牌第一级。产出间隔为 0 —— 它自己不产食物，只当基地。</summary>
    Shed,

    /// <summary>屋：房屋牌第二级，棚升级上来。</summary>
    House,

    /// <summary>仓：房屋牌第三级，屋升级上来。</summary>
    Granary,
}

/// <summary>
/// 一个牌种的数据 —— <b>类别名 + 产出间隔 + 每轮多保留几张 + 住几个人</b>。
///
/// <para><b>间隔以步计，0 表示不产出</b>（「局内唯一时间单位是步」）。
/// 间隔一致就没有「取舍」这个词的对象（spec「按步连续，不攒到结算点」），
/// 所以牌与牌之间长短差异要大 —— 这一列就是「急」与「稳」的对象。</para>
///
/// <para><b>房屋牌是本牌集里第一批间隔为 0 的牌</b>：它占一个位置、每轮多保留几张，
/// 自己不产出，也不能派工，只能当合成材料或者当被叠的那一张。</para>
/// </summary>
/// <param name="Kind">牌种。</param>
/// <param name="Label">显示名（进 HUD 与日志的中文）。</param>
/// <param name="IntervalSteps">产出间隔（步）。0 = 不产出，只能当材料或被叠的那张。</param>
/// <param name="FoodPerCycle">每次产出多少食物。</param>
/// <param name="KeepBonus">每轮可多保留几张牌。<b>&gt; 0 的那一张是房屋牌</b>，
/// 产粮牌那一列恒为 0。房屋占一个位置，所以这个加成的净收益是它减 1。</param>
/// <param name="ResidentCap">这间房住几个人 —— <b>房屋牌三行都是 2，产粮牌那一列是 0</b>。
/// 住满之后每个进食结算周期出 1 人，三级房屋的出人数一样，
/// 三级之间的差别只在牌位加成那一列。</param>
public sealed record CardSpec(
    CardKind Kind,
    string Label,
    int IntervalSteps,
    double FoodPerCycle,
    int KeepBonus,
    int ResidentCap);

/// <summary>
/// 牌种表 —— <b>内容数据，全表只此一份</b>。
///
/// <para><b>牌集规模由试玩反推</b>（ticket 07 的判据：三个没玩过的人每人能合出三张
/// 事先不知道存在的牌）。这一版的条数是 ticket 02 定的最小集，
/// 后面那张 ticket按实测扩。</para>
///
/// <para><b>呈现上要留猜的余地</b>：显示名只说这东西是什么，不说它怎么合。
/// 合成规则不印在牌上（spec「符号暗示与渐进式披露」本轮推迟）。</para>
/// </summary>
public static class CardCatalog
{
    private static readonly CardSpec[] All =
    [
        new(CardKind.Wood, "柴", IntervalSteps: 4, FoodPerCycle: 1.0, KeepBonus: 0, ResidentCap: 0),
        new(CardKind.Stone, "石", IntervalSteps: 9, FoodPerCycle: 1.0, KeepBonus: 0, ResidentCap: 0),
        new(CardKind.Spring, "泉眼", IntervalSteps: 3, FoodPerCycle: 1.0, KeepBonus: 0, ResidentCap: 0),
        new(CardKind.Furrow, "田垄", IntervalSteps: 15, FoodPerCycle: 3.0, KeepBonus: 0, ResidentCap: 0),
        new(CardKind.Kiln, "灰烬炉", IntervalSteps: 7, FoodPerCycle: 2.0, KeepBonus: 0, ResidentCap: 0),
        new(CardKind.DewVial, "夜露瓶", IntervalSteps: 1, FoodPerCycle: 1.0, KeepBonus: 0, ResidentCap: 0),
        new(CardKind.Shed, "棚", IntervalSteps: 0, FoodPerCycle: 0.0, KeepBonus: 2, ResidentCap: 2),
        new(CardKind.House, "屋", IntervalSteps: 0, FoodPerCycle: 0.0, KeepBonus: 5, ResidentCap: 2),
        new(CardKind.Granary, "仓", IntervalSteps: 0, FoodPerCycle: 0.0, KeepBonus: 10, ResidentCap: 2),
    ];

    private static readonly Dictionary<CardKind, CardSpec> ByKind =
        All.ToDictionary(s => s.Kind);

    /// <summary>全部牌种，按表内次序。</summary>
    public static IReadOnlyList<CardSpec> Specs => All;

    /// <summary>查一个牌种的数据。牌种不在表里当场抛（不兜一个缺省数值）。</summary>
    public static CardSpec Of(CardKind kind)
        => ByKind.TryGetValue(kind, out var spec)
            ? spec
            : throw new ArgumentOutOfRangeException(nameof(kind), kind,
                $"牌种 {kind} 不在牌种表里（可用：{string.Join(" / ", All.Select(s => s.Kind))}）");

    /// <summary>
    /// 解析一个 token 成牌种 —— 吃枚举名与中文显示名两种写法。
    ///
    /// <para><b>有别名集时 Journal 记的是折过之后那一个 token</b>（枚举名），
    /// 不记人打的那个（那是同一个意思的另一个写法）。</para>
    /// </summary>
    public static bool TryParse(string token, out CardKind kind)
    {
        foreach (var spec in All)
        {
            if (string.Equals(spec.Kind.ToString(), token, StringComparison.OrdinalIgnoreCase)
                || string.Equals(spec.Label, token, StringComparison.Ordinal))
            {
                kind = spec.Kind;
                return true;
            }
        }

        kind = default;
        return false;
    }

    /// <summary>词表文案（报错与注入参数读数用）。</summary>
    public static string Names => string.Join(" / ", All.Select(s => s.Kind.ToString()));

    /// <summary>
    /// 这一个牌种是不是房屋牌 —— <b>判据只有这一处</b>，别处不另抄一份。
    ///
    /// <para><b>房屋牌是「每轮多保留几张」大于 0 的那一张</b>：它占一个位置、
    /// 自己不产出（间隔 0），叠上去的那张因此住进去而不是派工。
    /// 判据落在这张表上而不是散在落点判定里，是因为住客上限、住满了落空那几格
    /// 都要跟着「什么算房屋」一起长，加一次就够。</para>
    ///
    /// <para><b>间隔为 0 与房屋牌在本牌集里是同一批牌</b>，
    /// 但判据用这一列而不用间隔：间隔为 0 还可能出现别的牌种，
    /// 而「叠上去是住进去还是派工」问的是底下那张是不是房屋。</para>
    ///
    /// <para><b>住客上限是 <see cref="CardSpec.ResidentCap"/> 那一列</b>：
    /// 「住满 2 人」这个数写在牌种表里，落点判定、快照的房屋那一格与生人计时都从它读，
    /// 别处不另抄一份。</para>
    /// </summary>
    public static bool IsHouse(CardKind kind) => Of(kind).KeepBonus > 0;

    /// <summary>
    /// 解析一个 token 成牌种，解析不出当场抛 —— <b>给内部照抄牌序表那种场合用</b>
    /// （玩家与调试注入那条路走 <see cref="TryParse"/>，它要把失败原因报给调用者）。
    /// </summary>
    public static CardKind Parse(string token)
        => TryParse(token, out var kind)
            ? kind
            : throw new ArgumentException($"牌种解析不出「{token}」（词表：{Names}）", nameof(token));
}
