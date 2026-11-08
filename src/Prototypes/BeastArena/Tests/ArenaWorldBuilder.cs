#nullable enable
using System;
using DemoFactory.Prototypes.BeastArena.Logic;

namespace BeastArena.Tests;

/// <summary>
/// 测试用的一份世界起点。
///
/// <para><b>只做安排，不做断言</b>：它按测试的用法把 <see cref="ArenaWorld"/> 造出来，
/// 再把「已停靠 / 挖到第 N 层 / 三本账备足」这些高频起点收成具名方法。
/// 断言仍留在测试里；一次性长链（防守、债、尸首）也留在测试里。</para>
///
/// <para><b>只用公开写路径</b>：不碰生产代码，不反射，不把 setup 塞进词表。
/// 要验证某条动词路径的测试仍显式调 <see cref="ArenaSetup.Inject"/>。</para>
///
/// <para><b>Build() 之后还能继续用</b>：builder 持有那一个世界；测试中途补状态时
/// 用世界上的测试扩展方法（<c>DockedAt</c> / <c>DugTo</c> / <c>Stocked</c>）。
/// 世界建出来之后再调 With...，会当场抛。</para>
/// </summary>
internal sealed class ArenaWorldBuilder
{
    internal const int DefaultSeed = 7;
    internal const double DefaultGranaryFood = 500.0;

    private readonly int _seed;
    private double _granaryFood = DefaultGranaryFood;
    private double _satiation = ArenaTuning.StartingSatiation;
    private int _spareParts = ArenaTuning.StartingSpareParts;
    private ArenaWorld? _world;

    private ArenaWorldBuilder(int seed) => _seed = seed;

    internal static ArenaWorldBuilder New(int seed = DefaultSeed) => new(seed);

    internal ArenaWorldBuilder WithGranaryFood(double value)
    {
        RequireNotBuilt();
        _granaryFood = value;
        return this;
    }

    internal ArenaWorldBuilder WithSatiation(double value)
    {
        RequireNotBuilt();
        _satiation = value;
        return this;
    }

    internal ArenaWorldBuilder WithSpareParts(int value)
    {
        RequireNotBuilt();
        _spareParts = value;
        return this;
    }

    internal ArenaWorld Build()
    {
        _world ??= new ArenaWorld(new ArenaConfig
        {
            Seed = _seed,
            GranaryFood = _granaryFood,
            Satiation = _satiation,
            SpareParts = _spareParts,
        });
        return _world;
    }

    private void RequireNotBuilt()
    {
        if (_world is not null)
        {
            throw new InvalidOperationException("世界已经建出来了；With... 必须在任何 setup 之前调用。");
        }
    }
}

/// <summary>
/// 世界建出来之后那几口 test-only setup。放在 world 上，不另造一层 wrapper：
/// 测试读的仍然是 <see cref="ArenaWorld"/> 本身。
/// </summary>
internal static class ArenaWorldTestSetup
{
    // 够付二阶与三阶两道代价（50/10/2000 与 150/30/5000）。
    private const double StockSoulStones = 1000.0;
    private const double StockSpecialMaterial = 200.0;
    private const double StockCoin = 50000.0;

    /// <summary>
    /// 真实航程：下令去那座城，再逐步推进到停稳。多数测试要的是这一档。
    ///
    /// <para>本口只判航程与停稳，不判 <c>CanDockTo</c> 的通缉 / 债拒停；
    /// 验拒停的用例走 <see cref="ArenaSetup.Inject"/> 的 <c>Dock</c> 动词。</para>
    /// </summary>
    internal static ArenaWorld DockedAt(
        this ArenaWorld world, string cityId, int maxSteps = ArenaTime.StepsPerGameDay)
    {
        ArgumentNullException.ThrowIfNull(world);

        if (world.IsDocked)
        {
            throw new InvalidOperationException("世界已经停靠了；DockedAt 只用于启航前。");
        }

        world.OrderSailTo(cityId);
        for (var i = 0; i < maxSteps && !world.IsDocked; i++)
        {
            world.Advance(1);
        }

        if (!world.IsDocked)
        {
            throw new InvalidOperationException(
                $"{cityId} 在 {maxSteps} 步内没有停稳；航行状态是 {world.Voyage.Phase}。");
        }

        if (world.DockedCityId != cityId)
        {
            throw new InvalidOperationException(
                $"要去 {cityId}，实际停到了 {world.DockedCityId}。");
        }

        return world;
    }

    /// <summary>
    /// 把二阶门槛三项一次摆齐：挖到第 2 层 → 洞穴清剿成功 → 仓库有 1 单位魔晶并嵌入。
    /// <b>只给与门槛无关的用例当起点</b>；验门槛本身的用例仍逐项走真实写路径。
    /// </summary>
    internal static ArenaWorld Tier2Ready(this ArenaWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);

        world.DugTo(UndergroundSoil.CaveSector, 1);
        world.DugTo(UndergroundSoil.CaveSector, UndergroundSoil.NativeCaveLayer);

        if (!world.Soil.MarkCaveCleared(UndergroundSoil.CaveSector, UndergroundSoil.NativeCaveLayer))
        {
            throw new InvalidOperationException("洞穴那一格没清成，测试起点没达到条件。");
        }

        // 魔晶只有镇账那一本；这里直接走产出写口摆「仓里有 1 单位」（真跑由生态消化产出）。
        world.Town.ProduceCrystal(1.0);

        var rejected = world.EmbedCrystal(UndergroundSoil.CaveSector, UndergroundSoil.NativeCaveLayer);
        if (rejected != RejectReason.InvalidArgs)
        {
            throw new InvalidOperationException($"嵌入那一发没走成：{rejected}（世界是否已停靠？）");
        }

        return world;
    }

    /// <summary>直跳停靠：只给确实要跳过航程的测试用。</summary>
    internal static ArenaWorld DockedDirect(this ArenaWorld world, MapPlace place)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(place);
        world.OnDocked(place);
        return world;
    }

    /// <summary>
    /// 把局流程推到「已驶离贫城」（暴食态解锁的第二道门）：开局 → 抵贫城 → 拔锚防守赢了 → 第一段航行。
    ///
    /// <para><b>只动流程，不动世界自己的停靠与航程读数</b>：四发都是 <see cref="RunFlow"/> 对外的
    /// 通知口，与 <c>BattleSim</c> / <c>OnDocked</c> 在生产里走的是同一条路。要验防守胜负本身的用例
    /// 仍自己打那一仗。</para>
    /// </summary>
    internal static ArenaWorld LeftPoorCity(this ArenaWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);

        if (world.DepartedPoorCity) return world;

        world.Flow.Start();
        world.Flow.NoteDocked(BigMapGrid.PoorCity.Id);
        world.Flow.NoteAnchorPulled(BigMapGrid.PoorCity.Id);
        world.Flow.NoteDefenceSettled(won: true);
        return world;
    }

    internal static ArenaWorld DugTo(this ArenaWorld world, int sector, int layer)
    {
        ArgumentNullException.ThrowIfNull(world);

        for (var l = 1; l <= layer; l++)
        {
            if (world.Soil.IsDug(sector, l)) continue;

            if (!world.Soil.BeginDigging(sector, l, ArenaTuning.DigWorkerThreshold, world.Tick))
            {
                throw new InvalidOperationException(
                    $"象限 {sector} 第 {l} 层开不了工；测试起点没有达到挖掘条件。");
            }

            world.Advance(ArenaTime.StepsPerGameHour);

            if (!world.Soil.IsDug(sector, l))
            {
                throw new InvalidOperationException(
                    $"象限 {sector} 第 {l} 层推了一游戏小时还没挖开。");
            }
        }

        return world;
    }

    internal static ArenaWorld Stocked(this ArenaWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        world.Town.ProduceSoulStone(StockSoulStones);
        world.Town.ProduceSpecialMaterial(StockSpecialMaterial);
        world.Town.ProduceCoin(StockCoin);
        return world;
    }
}
