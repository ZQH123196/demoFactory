#nullable enable
namespace DemoFactory.Prototypes.BeastArena.Logic;

// 巨兽形态：三种（原始态 / 荒漠态 / 暴食态），开局原始态，没有出身差别。
//
// 当前形态与「哪些形态已解锁」两个标记住在 BeastGrowth（与 Tier 同一件）——
// 形态是「世界级」的那一份：解锁是一次性的事件，不是每次现算的派生值。
// 饱食度与两个解锁计数由 BeastVoyage 递增量给 BeastGrowth，不另存一份。

/// <summary>巨兽形态（三种：开局原始态）。</summary>
public enum BeastForm
{
    /// <summary>原始态（开局）。食性与土壤都是基线。</summary>
    Original = 0,

    /// <summary>荒漠态。所在格是沙漠时饱食衰减减半，矿石产出多两成半。</summary>
    Desert = 1,

    /// <summary>暴食态。日结耗粮多五成（200 → 300 食物），高危区特殊材料多两成半。</summary>
    Glutton = 2,
}

/// <summary>形态的读数：中文短名（横幅用）与词表 token（Journal 与快照用）。</summary>
public static class BeastFormText
{
    /// <summary>中文短名（一格一句，只给人看）。</summary>
    public static string NameOf(BeastForm form) => form switch
    {
        BeastForm.Desert => "荒漠态",
        BeastForm.Glutton => "暴食态",
        _ => "原始态",
    };

    /// <summary>
    /// 词表 token（小写英文）：<c>original</c> / <c>desert</c> / <c>glutton</c>。
    /// 它进 Journal 的参数串与快照的 <c>form=</c> 那一栏，<b>是契约</b>，别随手改。
    /// </summary>
    public static string TokenOf(BeastForm form) => form switch
    {
        BeastForm.Desert => "desert",
        BeastForm.Glutton => "glutton",
        _ => "original",
    };

    /// <summary>认 token（认不出回 null，不降级成原始态）。</summary>
    public static BeastForm? Parse(string token) => token switch
    {
        "original" => BeastForm.Original,
        "desert" => BeastForm.Desert,
        "glutton" => BeastForm.Glutton,
        _ => null,
    };
}
