#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DemoFactory.Prototypes.MistCity.Logic;

/// <summary>
/// 一条配方 —— <b>任意份输入合成出什么</b>。
///
/// <para><b>输入按无序多重集处理</b>：输入按牌种计数，不按次序。
/// 所以 <c>石 + 柴 + 柴</c> 与 <c>柴 + 柴 + 石</c> 折出同一个键，是同一条配方；
/// 玩家摆放的顺序不该改变结果（那只会让「我先放了哪个」变成一个隐形规则）。
/// <c>柴 + 柴</c> 是「同种两输入」那一种退化情形，不单独特判。</para>
///
/// <para><b>份数这一格有对象</b>：屋与仓这两条都是三份输入，而柴、石、泉眼、田垄
/// 那几条都是两份，所以「这一条要几份」是表里的一等事实，不是从输入列表长度临时推的。</para>
/// </summary>
/// <param name="Inputs">输入牌种，按牌种计数（次序无意义）。</param>
/// <param name="Output">产出的牌种。</param>
public sealed record Recipe(IReadOnlyList<CardKind> Inputs, CardKind Output)
{
    /// <summary>这一条配方要几份输入 —— 两条或三条（屋与仓那两条是三份）。</summary>
    public int Portions => Inputs.Count;

    /// <summary>
    /// 这一条配方的键 —— <b>无序多重集</b>（牌种按名排，同种的份数写在一起）。
    ///
    /// <para>同一条配方不管输入按什么次序摆进来，折出来的键逐字相同，
    /// 所以它能当表的键用（表一装载就按它建索引）。</para>
    /// </summary>
    public string Key => FoldKey(Inputs);

    /// <summary>
    /// 把一份输入折成无序多重集的键 —— <b>折法只有这一处</b>，
    /// 配方的键与查表时用的键都走它，两边不许各折一次。
    /// </summary>
    /// <param name="inputs">输入牌种，次序无意义。</param>
    public static string FoldKey(IEnumerable<CardKind> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        return string.Join("+", inputs
            .GroupBy(k => k)
            .OrderBy(g => g.Key)
            .Select(g => string.Concat(g.Key.ToString(), "=",
                g.Count().ToString(CultureInfo.InvariantCulture))));
    }
}

/// <summary>
/// 配方表 —— <b>内容数据，全表只此一份</b>。
///
/// <para><b>这一表不进建造层</b>（ADR 0017）：配方是「几个对象合成另一个对象」，
/// 既不占地也没有坐标，建造层那套判据一条都不适用。</para>
///
/// <para><b>条数与开局可发现的牌数是生死线的输入</b>（ticket 07 的判据：
/// 三个没玩过的人每人能合出三张事先不知道存在的牌）。表里每一行都要能从快照读出，
/// 否则 08 之后没法从录像倒推玩家试到了哪一步。</para>
///
/// <para><b>表在装载时建索引，重了就当场抛</b>：两条配方折出同一个键就是表写错了，
/// 那种错留在代码里要到玩家合成出一张不该有的牌时才暴露，而快照上查不出来。</para>
/// </summary>
public static class RecipeBook
{
    private static readonly Recipe[] All =
    [
        new([CardKind.Wood, CardKind.Stone], CardKind.Spring),
        new([CardKind.Wood, CardKind.Wood], CardKind.Furrow),
        new([CardKind.Stone, CardKind.Stone], CardKind.Kiln),
        new([CardKind.Spring, CardKind.Wood], CardKind.DewVial),
        new([CardKind.Spring, CardKind.Stone], CardKind.Kiln),
        new([CardKind.Furrow, CardKind.Stone], CardKind.DewVial),

        // 三级房屋走升级链而不是三个互不相干的配方：
        // 先凑一个小的、后面换大的这件事因此成立，
        // 而且后两级吃的是产粮牌，合仓直接压低当下供给 —— 取舍从配方里长出来。
        //
        // 棚的配方是「柴 + 田垄」，不是「2 柴 + 1 石」（用户裁决）：
        // 原定那一摞里任意两张本身都是配方（柴 + 石 出泉眼、柴 + 柴 出田垄），
        // 玩家拖上去当场合成掉，摞长不到 3 张，棚这一级根本造不出来。
        // 「柴 + 田垄」这一对本身不是配方，玩家能把它叠上去，整条链才走得通。
        new([CardKind.Wood, CardKind.Furrow], CardKind.Shed),
        new([CardKind.Shed, CardKind.Stone, CardKind.Stone], CardKind.House),
        new([CardKind.House, CardKind.Spring, CardKind.Spring], CardKind.Granary),
    ];

    private static readonly Dictionary<string, CardKind> ByKey = All.ToDictionary(r => r.Key, r => r.Output);

    /// <summary>全部配方，按表内次序。</summary>
    public static IReadOnlyList<Recipe> Recipes => All;

    /// <summary>
    /// 这一份输入合成出什么 —— <b>不在表里当场抛</b>，不返回 null、不兜一个默认牌。
    ///
    /// <para>抛是刻意的：spec「参数缺了当场抛，一条例外都没有」，
    /// 而「这些牌合不出来」正是玩家要试的那一下，合成失败损失材料是他要的反馈，
    /// 不是要被静默吞掉的输入错误。</para>
    ///
    /// <para><b>输入份数不够也走这一条抛</b>，不从桌上再拿一张补上：
    /// 补上去的那一张玩家没看见，他只知道自己的牌不见了 ——
    /// 而画面上只看得见「合成失败」，读数上查不出多吞了谁。</para>
    /// </summary>
    /// <param name="inputs">这一份输入的牌种，次序无意义。</param>
    public static CardKind OutputOf(IReadOnlyList<CardKind> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (ByKey.TryGetValue(Recipe.FoldKey(inputs), out var output)) return output;

        throw new InvalidOperationException(
            $"{Describe(inputs)} 不在配方表里：这些牌合不出东西（表里共 {All.Length} 条）。"
            + "输入份数不够时也是这一条 —— 当场抛，不从桌上再拿一张补上："
            + "补上去的那一张玩家没看见，他只看见自己的牌不见了。");
    }

    /// <summary>
    /// 这一摞能凑出哪一条配方 —— <b>能凑出就合，不是整摞恰好等于</b>。
    ///
    /// <para><b>判据是子多重集</b>：这一摞里够这一条要的每一份就算凑得出。
    /// 凑得出时消耗的正好是配方要的那几张，摞上多出来的那几张留在桌面上、
    /// 停在原位（spec「叠放只有一条规则」）。</para>
    ///
    /// <para><b>多条都凑得出时取份数最多的那一条</b>，份数相同取配方表里在前的那一条
    /// （下面那一发按表内次序扫，份数严格更多才换人，所以平手时留下的就是先扫到的那条）。
    /// 不这么判的话，一次拖放的结果就取决于表内次序，而不是取决于玩家叠了什么。</para>
    ///
    /// <para>一条都凑不出回 null —— 那不是错，是「再叠一张也许就凑出了」。</para>
    /// </summary>
    /// <param name="available">这一摞的牌种，次序无意义。</param>
    /// <returns>凑得出的那一条；一条都凑不出是 null。</returns>
    public static Recipe? Match(IReadOnlyList<CardKind> available)
    {
        ArgumentNullException.ThrowIfNull(available);

        Recipe? best = null;
        for (var i = 0; i < All.Length; i++)
        {
            var r = All[i];
            if (!Fits(available, r)) continue;
            if (best is null || r.Portions > best.Portions) best = r;
        }

        return best;
    }

    /// <summary>
    /// 这一摞里够不够这一条配方要的每一份 —— <b>按牌种计数，不看次序</b>。
    /// 同一张牌只能算一份：扣一份就减一个，扣不够当场回 false。
    /// </summary>
    private static bool Fits(IReadOnlyList<CardKind> have, Recipe recipe)
    {
        var pool = new Dictionary<CardKind, int>();
        foreach (var kind in have) pool[kind] = pool.GetValueOrDefault(kind) + 1;

        foreach (var input in recipe.Inputs)
        {
            if (pool.GetValueOrDefault(input) < 1) return false;
            pool[input] -= 1;
        }

        return true;
    }

    /// <summary>配方表进快照的那一段（条数 + 每条一行）—— 供录像倒推玩家试到了哪一步。</summary>
    /// <remarks>
    /// 每行印「要几份 + 按牌种排开的输入」，所以三份输入的屋与仓
    /// 与两条输入的那些在快照上一眼分得开。
    /// </remarks>
    public static string DebugSnapshotSegment()
    {
        var sb = new StringBuilder();
        sb.Append("recipe count=").Append(Num(All.Length)).Append('\n');
        for (var i = 0; i < All.Length; i++)
        {
            var r = All[i];
            sb.Append("recipe ").Append(Num(i))
                .Append(' ').Append(Num(r.Portions));
            foreach (var kind in r.Inputs.OrderBy(k => k))
                sb.Append(' ').Append(kind);

            sb.Append(" => ").Append(r.Output).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>报��里念得出这一份输入是什么（中文显示名，次序不重排 —— 照玩家摆的次序念）。</summary>
    private static string Describe(IReadOnlyList<CardKind> inputs)
        => inputs.Count == 0
            ? "（一份输入也没有）"
            : string.Join(" + ", inputs.Select(k => CardCatalog.Of(k).Label));

    /// <summary>数字的唯一格式化处（「double 与不变文化」）。</summary>
    private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);
}
