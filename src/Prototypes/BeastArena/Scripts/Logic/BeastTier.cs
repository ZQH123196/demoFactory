#nullable enable
namespace DemoFactory.Prototypes.BeastArena.Logic;

// 巨兽位阶：三阶（1 / 2 / 3），开局一阶。
//
// 状态住在 BeastGrowth（与形态、两个解锁计数、进阶仪式同一件）——
// 进阶仪式是「世界停摆经营」那一类事，与停摆期同属世界级状态。
// 饱食度仍在 BeastVoyage，不挪，也不另开一份兽配置。

/// <summary>巨兽位阶（三阶：开局一阶）。</summary>
public enum BeastTier
{
    /// <summary>一阶（开局）。</summary>
    Tier1 = 1,

    /// <summary>二阶（任一象限挖到第 2 层后进阶）。</summary>
    Tier2 = 2,

    /// <summary>三阶（任一象限挖到第 3 层后进阶）。</summary>
    Tier3 = 3,
}

/// <summary>位阶的中文短名（横幅与快照读它）。</summary>
public static class BeastTierText
{
    /// <summary>位阶的中文短名（一阶 / 二阶 / 三阶）。</summary>
    public static string NameOf(BeastTier tier) => tier switch
    {
        BeastTier.Tier2 => "二阶",
        BeastTier.Tier3 => "三阶",
        _ => "一阶",
    };
}
