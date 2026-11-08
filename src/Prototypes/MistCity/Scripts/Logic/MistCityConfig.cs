#nullable enable
using System;
using System.Collections.Generic;

namespace DemoFactory.Prototypes.MistCity.Logic;

/// <summary>
/// 一局的配置 —— <b>开局发什么牌、有几个人、几口饭</b>，内容是玩法决定，归原型。
///
/// <para>牌种表与配方表是全局静态数据（<c>CardCatalog</c> / <c>RecipeBook</c>），
/// 「这一局发哪一手」才是配置。三个人拿同一份开局牌序（验证者故事 16：
/// 差异来自人不来自内容），所以牌序由种子派生而不是每次随机。</para>
/// </summary>
public sealed class MistCityConfig
{
    /// <summary>默认种子。写死一格，三次试玩拿同一份开局（验证者故事 16）。</summary>
    public const int DefaultSeed = 20260930;

    /// <summary>默认三个人（生死线那条判据是「三人中至少两人」）。</summary>
    public static readonly IReadOnlyList<string> DefaultResidents = ["h1", "h2", "h3"];

    /// <summary>
    /// 开局食物 —— <b>够撑一轮结算，不够两轮</b>。
    /// 一轮结算三人各吃一份；给 5 份，于是玩家必须在这一轮里把产线接上，
    /// 否则第二轮结算时死一个人（玩家故事 10：取舍从第一分钟就存在）。
    /// </summary>
    public const double DefaultOpeningFood = 5.0;

    /// <summary>
    /// 开局那一手牌 —— <b>够试出至少三条配方，不够把牌集全摊开</b>。
    ///
    /// <para>四张柴 + 两张石 + 一张泉眼：柴+石 出泉眼、柴+柴 出田垄、石+石 出灰烬炉，
    /// 三条配方都能在开局就试出来；泉眼那张是「已经能产出」的起点，
    /// 玩家得自己发现「产出的牌也可以当材料」那条路（泉眼+柴 → 夜露瓶）。</para>
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultOpeningHand =
    [
        "Wood", "Wood", "Wood", "Wood", "Stone", "Stone", "Spring",
    ];

    /// <summary>逻辑种子（开局牌序由它派生）。</summary>
    public int Seed { get; init; } = DefaultSeed;

    /// <summary>在场的人（三人，生死线要求三人中至少两人）。</summary>
    public IReadOnlyList<string> Residents { get; init; } = DefaultResidents;

    /// <summary>开局食物份数。</summary>
    public double OpeningFood { get; init; } = DefaultOpeningFood;

    /// <summary>开局那一手牌，按发放次序。</summary>
    public IReadOnlyList<string> OpeningHand { get; init; } = DefaultOpeningHand;
}
