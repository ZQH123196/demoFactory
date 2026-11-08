#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DemoFactory.Prototypes.MistCity.Logic;
using Shouldly;
using Xunit;

namespace MistCity.Tests;

/// <summary>
/// 开局牌序的账 —— <b>试玩前先量出来的那一组数</b>。
///
/// <para><b>三个人每步吃 3 份。</b>开局牌序（4 柴 2 石 1 泉眼）经穷举能到 35 个牌集，
/// 稳态产出的上限是 <b>2.361 份/步</b>（1 柴 + 1 石 + 2 夜露瓶），三个人吃不饱、
/// 两个人吃得饱。第一轮结算在第 60 步（6 秒），那时至少死一个人，
/// 死几个取决于玩家在 6 秒里能把产出凑到多高。</para>
///
/// <para><b>这里钉的是实测值，不是设计意图</b>。改牌集、改结算周期或改人数之后这四条会红，
/// 那时连同 <c>docs/prototypes/mist-city/</c> 里的结论一起改（用户故事 24：
/// 改配方表之后立刻知道哪些断言变红）。</para>
/// </summary>
public class BalanceTests
{
    /// <summary>一轮 = <see cref="Rations.CycleSteps"/> 步。</summary>
    private const int Cycle = Rations.CycleSteps;

    [Fact]
    public void 开局牌序的稳态产出上限_三个人每步吃三份()
    {
        var mouths = NewWorld().Food.AliveCount;
        var (rate, best, reachable) = MaxSteadySupply();

        rate.ShouldBeLessThan((double)mouths,
            $"开局 {mouths} 个人，每步吃 {mouths} 份；而开局牌序穷举 {reachable} 个可达牌集之后，"
            + $"稳态产出上限只有 {rate:0.###} 份/步（{best}）—— 喂不饱，"
            + "于是每一轮结算都必然死人，玩家再怎么派工也换不来稳态。");

        // 实测值钉在这里，不钉意图。改了牌集、改了配方输入的计数判、改了结算周期之后
        // 数字会变，那时连同 docs/prototypes/mist-city/ 里的结论一起改。
        //
        // 这一档打法：1 柴 + 1 石 + 2 夜露瓶 —— 夜露瓶是合出来的（间隔 1 步、每步 1 份），
        // 两瓶各一张，另留一张柴一张石走产线。所以是「两张最快的那一种撑住底盘，
        // 剩下的位置继续派工」这一档。
        //
        // 棚的配方从「2 柴 + 1 石」换成「柴 + 田垄」之后重跑过一次：
        // 可达牌集从 26 个变成 35 个，稳态产出上限仍是 2.361111111111111 份/步，
        // 最优牌集一个字都没变。房屋牌间隔为 0、不产食物，
        // 换一条配方只会把几张产粮牌换成一张不产出的牌，所以它给不出更高的数 ——
        // 变的只是牌序够得着的那些牌集。
        rate.ShouldBe(2.361111111111111, $"稳态产出上限实测值变了：现在是 {rate:0.######} 份/步，最优牌集「{best}」，穷举了 {reachable} 个可达牌集");
    }

    [Fact]
    public void 第一轮结算_什么都不派三个人全死()
    {
        var world = NewWorld();

        world.Advance(Cycle);

        world.Food.LastSettlement!.DiedIds.ShouldBe(new[] { "h1", "h2", "h3" }, "开局牌序不派工，第一轮三个人全死");
        world.Food.LastSettlement.Short.ShouldBe(175, "一轮要 180 份，只有开局的 5 份顶上了，缺口 175");
    }

    [Fact]
    public void 第一轮结算_七张全派成环缺口八十五()
    {
        var world = NewWorld();
        // 连成环：七张牌各压一张，每张都在产。
        // 一张牌下面能叠多张，但多张压同一张时只有最上面那张派得上工，
        // 所以开局这七张摆出的最大产出仍然是连成一个环。
        foreach (var (card, onto) in new[]
                 {
                     ("c01", "c02"), ("c02", "c03"), ("c03", "c04"), ("c04", "c05"),
                     ("c05", "c06"), ("c06", "c07"), ("c07", "c01"),
                 })
        {
            world.Bind(card, onto);
        }

        world.Advance(Cycle);

        world.Food.LastSettlement!.DiedIds.ShouldBe(new[] { "h1", "h2", "h3" },
            "七张全派（开局能摆出的最大产出）仍然供不上，第一轮三个人照样全死");
        world.Food.LastSettlement.Consumed.ShouldBe(95, "七张全派这一轮实际吃进去 95 份");
        world.Food.LastSettlement.Short.ShouldBe(85, "一轮要 180 份，缺口 85");
    }

    [Fact]
    public void 第一轮结算_凑到穷举上限只死最后一个()
    {
        var world = NewWorld();
        // 穷举给出的那张最优牌集：泉+柴 → 露瓶，柴+柴 → 垄，垄+石 → 露瓶，
        // 剩下 1 柴 + 1 石 + 2 露瓶连成环（2.361 份/步，开局能达到的上限）。
        world.Combine("c07", "c01");
        var dewA = Newest(world);
        world.Combine("c02", "c03");
        var furrow = Newest(world);
        world.Combine(furrow, "c05");
        var dewB = Newest(world);

        foreach (var (card, onto) in new[]
                 {
                     ("c04", "c06"), ("c06", dewA), (dewA, dewB), (dewB, "c04"),
                 })
        {
            world.Bind(card, onto);
        }

        world.Advance(Cycle);

        world.Food.LastSettlement!.DiedIds.ShouldBe(new[] { "h3" },
            "凑到开局能达到的产出上限（2.361 份/步）时三个人吃不饱，但只有排在最后的那个人死 —— "
            + "先到先得，h1 与 h2 这一轮吃够了。于是两个人活下来，稳态产出 2.361 > 两人吃的 2.0，城站住了");
        // 这一轮的实际产出是整数：柴 15 次 + 石 6 次 + 两瓶各 60 次 = 141，加开局 5 = 146。
        world.Food.LastSettlement.Consumed.ShouldBe(146, "这一轮吃进 146 份（开局 5 + 产出 141）");
        world.Food.LastSettlement.Short.ShouldBe(34, "一轮要 180 份，缺口 34 —— 全部压在最后那个人头上");
    }

    // === 穷举：开局牌序能凑出的最大稳态产出 ===

    /// <summary>
    /// 穷举所有合成可达的牌集，取稳态产出率最高的那一个。
    ///
    /// <para><b>稳态产出率 = 各牌「每次份数 ÷ 间隔步数」之和</b>：一张牌各产各的，
    /// 压在哪张牌底下不影响它自己的产出率，所以这个和就是上限（人肉派的线不会更高）。
    /// 间隔为 0 的那几张（房屋牌）在这一格里贡献 0：它不产食物，
    /// 合出一条房屋牌只会把几张产粮牌换成一张不产出的牌，所以它给不出更高的数。</para>
    ///
    /// <para><b>存量按份数扣，不特判同种两输入</b>：对配方的每一种输入各扣一份，
    /// 扣不够就跳过那一发。柴 + 柴 是同一种输入扣两份，
    /// 「同一对牌只查一次」那种写法会凭空造出一张牌。</para>
    /// </summary>
    private static (double Rate, string Best, int Reachable) MaxSteadySupply()
    {
        var start = new Dictionary<CardKind, int>();
        foreach (var spec in CardCatalog.Specs)
        {
            start[spec.Kind] = 0;
        }

        // 存量按牌种累加：开局四柴两石一泉眼，四个「柴」要叠成 4，不是 1。
        // 写成 start[kind] = count 的话后一个 token 盖掉前一个，穷举就从
        // 「每种牌各一张」出发，算出来的上限不是开局牌序的上限。
        foreach (var (kind, count) in Opening())
        {
            start[kind] = start.GetValueOrDefault(kind) + count;
        }

        var seen = new HashSet<string> { Key(start) };
        var queue = new Queue<Dictionary<CardKind, int>>();
        queue.Enqueue(start);

        var best = (rate: Rate(start), set: Show(start));
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            var rate = Rate(cur);
            if (rate > best.rate) best = (rate, Show(cur));

            foreach (var r in RecipeBook.Recipes)
            {
                if (!Spends(cur, r, out var next)) continue;
                if (!seen.Add(Key(next))) continue;
                queue.Enqueue(next);
            }
        }

        // 可达牌集数在穷举走完之后才报：写在更新 best 那一格里的话，
        // 报的是刚找到最优牌集那一刻的半截数，读起来像「只穷举了这么几个」。
        return (best.rate, best.set, seen.Count);
    }

    /// <summary>
    /// 这一发配方扣不扣得出来 —— <b>通用计数判</b>。
    /// 扣不够回 false（那一发跳过），扣够就把扣完之后的存量给出去。
    /// </summary>
    private static bool Spends(Dictionary<CardKind, int> have, Recipe r, out Dictionary<CardKind, int> next)
    {
        next = new Dictionary<CardKind, int>(have);

        // 每一种输入各扣一份：同一张牌要几份就扣几次。
        foreach (var input in r.Inputs)
        {
            if (next.GetValueOrDefault(input) < 1) return false;
            next[input] -= 1;
        }

        next[r.Output] = next.GetValueOrDefault(r.Output) + 1;
        return true;
    }

    /// <summary>开局牌序（直接读配置里那一份，不在这里另抄一份字面量）。</summary>
    private static IEnumerable<(CardKind Kind, int Count)> Opening()
    {
        foreach (var token in new MistCityConfig().OpeningHand)
        {
            yield return (CardCatalog.Parse(token), 1);
        }
    }

    private static double Rate(Dictionary<CardKind, int> set)
    {
        var rate = 0.0;
        foreach (var (kind, count) in set)
        {
            if (count <= 0) continue;
            var spec = CardCatalog.Of(kind);
            if (spec.IntervalSteps <= 0) continue;
            rate += count * (double)spec.FoodPerCycle / spec.IntervalSteps;
        }

        return rate;
    }

    private static string Key(Dictionary<CardKind, int> set)
        => string.Join(",", CardCatalog.Specs.Select(s => $"{s.Kind}={set.GetValueOrDefault(s.Kind)}"));

    private static string Show(Dictionary<CardKind, int> set)
        => string.Join(" ", CardCatalog.Specs
            .Where(s => set.GetValueOrDefault(s.Kind) > 0)
            .Select(s => $"{set.GetValueOrDefault(s.Kind)}×{s.Label}({s.IntervalSteps}步{s.FoodPerCycle:0.##}份)"));

    private static MistCityWorld NewWorld() => new(new MistCityConfig());

    /// <summary>刚合成出来的那一张 —— 新发的牌最后进册。</summary>
    private static string Newest(MistCityWorld world) => world.Cards.Ids[^1];
}
