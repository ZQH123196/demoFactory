#nullable enable
using System.Collections.Generic;
using System.Linq;
using DemoFactory.Prototypes.MistCity.Logic;
using Shouldly;
using Xunit;

namespace MistCity.Tests;

/// <summary>
/// 牌集规模（ticket 07）—— 这一片不改机制，只加内容。
///
/// <para><b>规模判据</b>：三个没玩过的人，无指引试玩，结束前每人能合出三张
/// 事先不知道存在的牌。达不到就扩条数，扩到够为止。
/// <b>这一条要真人试玩才判得了</b>，逻辑层只能钉住「够不够试」的三个下限：
/// ① 开局那一手牌能试出至少三条配方；② 产出的牌也能当材料（有一条产线接产线）；
/// ③ 不同产线的快慢差距足够大，「取舍」这个词有对象。</para>
///
/// <para><b>配方按无序多重集判</b>（ticket 05 起）：输入按牌种计数，不按次序。
/// 所以「手上有没有这张牌」那一格换成「手上够几份」——
/// 够份的那些配方才是玩家试得出来的，够种的算不出真实的可试范围。</para>
/// </summary>
public class CardSetTests
{
    [Fact]
    public void 全部牌种与配方条数能从快照一次读出_条数与设计时定的相等()
    {
        var world = new MistCityWorld(new MistCityConfig());

        var snap = world.DebugSnapshot();
        snap.ShouldContain($"recipe count={RecipeBook.Recipes.Count}", Case.Sensitive,
            "配方表条数从快照读得出来，不用回代码数");
    }

    [Fact]
    public void 产出的牌也能当材料_至少一条配方两个输入都会产出()
    {
        // 「产出的牌也能当材料」是玩家最该自己发现的那一层；
        // 没有它，产线就到顶了，玩家的发现空间少一整块。
        var withTwoProducers = RecipeBook.Recipes
            .Where(r => r.Output != CardKind.Wood && r.Output != CardKind.Stone)
            .ToArray();

        // 「两个输入都会产出」按份数判：一份产出的牌加另一份产出的牌。
        var any = RecipeBook.Recipes.Any(r =>
            (r.Output != CardKind.Wood && r.Output != CardKind.Stone)
            && r.Inputs.Count(i => i != CardKind.Wood && i != CardKind.Stone) >= 2);

        any.ShouldBeTrue("表里至少要有一条「产出的牌 + 产出的牌 → 更强的牌」，产线才能接产线");
        withTwoProducers.Length.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void 开局那一手牌能试出至少三条配方()
    {
        var world = new MistCityWorld(new MistCityConfig());
        var hand = HandOf(world);

        var discoverable = RecipeBook.Recipes.Count(r => Enough(hand, r));

        discoverable.ShouldBeGreaterThanOrEqualTo(3,
            "开局那一手牌至少能试出三条配方 —— 够玩家连着发现三次；不够就扩牌序");
    }

    [Fact]
    public void 不同产线的快慢差距足够大_取舍这个词有对象()
    {
        // 「间隔一致就没有『取舍』这个词的对象」。这里钉住：最快的与最慢的差出五倍以上，
        // 且至少有两个不同的间隔档。产粮牌才进这一格 —— 房屋牌间隔为 0，不参与快慢比较。
        var producers = CardCatalog.Specs
            .Where(s => s.IntervalSteps > 0)
            .Select(s => s.IntervalSteps)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        producers.Length.ShouldBeGreaterThanOrEqualTo(2, "至少要有两个不同的间隔档");
        producers.Max().ShouldBeGreaterThanOrEqualTo(producers.Min() * 5,
            $"最快的 {producers.Min()} 步与最慢的 {producers.Max()} 步差不到五倍，「急」与「稳」之间就没有真实的差别");
    }

    [Fact]
    public void 每条配方都进快照_录像能倒推玩家试到了哪一步()
    {
        var world = new MistCityWorld(new MistCityConfig());
        var snap = world.DebugSnapshot();

        foreach (var r in RecipeBook.Recipes)
        {
            snap.ShouldContain(RecipeLine(r), Case.Sensitive,
                "每条配方都要能进快照，否则 08 之后没法从录像倒推玩家试到了哪一步");
        }
    }

    [Fact]
    public void 配方表无歧义_同一份输入只对应一个产出()
    {
        // 有歧义的话玩家试出来的反馈不稳定，同一份输入两次给出不同答案，
        // 「合出来的东西是确定的」这个前提就没了。键是无序多重集，
        // 所以两份输入的退化情形（柴 + 柴）与三份输入的房屋配方走同一把尺子。
        var keys = new HashSet<string>();
        foreach (var r in RecipeBook.Recipes)
        {
            keys.Add(r.Key).ShouldBeTrue(
                $"配方 {RecipeLine(r)} 与另一条重了：同一份输入不能有两个产出");
        }
    }

    [Fact]
    public void 每条配方的产出都真的在牌种表里()
    {
        foreach (var r in RecipeBook.Recipes)
        {
            var spec = CardCatalog.Of(r.Output);
            spec.Kind.ShouldBe(r.Output, $"配方产出 {r.Output} 要在牌种表里有数据（间隔与产出量）");
        }
    }

    [Fact]
    public void 牌种都有显示名_牌面上有字能认()
    {
        foreach (var spec in CardCatalog.Specs)
        {
            spec.Label.ShouldNotBeNullOrWhiteSpace($"牌种 {spec.Kind} 没有显示名 —— 牌面上得有字能认");
        }
    }

    [Fact]
    public void 从开局那一手牌能试出至少三种新牌()
    {
        // 生死线第一条的机械化下限：三个陌生人每人结束前能合出三张事先不知道存在的牌。
        // 这里用一张搜索表从开局牌序出发，算出「能到达的新牌种」有几个 ——
        /// <b>够不够由真人试玩判，这条只钉住「牌集本身够不够」这一半。</b>
        var hand = MistCityConfig.DefaultOpeningHand;
        var reachable = ReachableNewKinds(hand);

        reachable.Count.ShouldBeGreaterThanOrEqualTo(3,
            $"从开局牌序能试出的新牌只有 {reachable.Count} 种（{string.Join(" / ", reachable)}）："
            + "每人要能合出三张事先不知道存在的牌，牌集得先够得到");

        // 新牌本身也得能当材料继续往上叠 —— 否则发现到第三张之后就停了。
        var stackable = reachable.Count(k => reachable.Any(o =>
            RecipeBook.Recipes.Any(r => r.Inputs.Contains(k) && reachable.Contains(r.Output))));
        stackable.ShouldBeGreaterThan(0, "发现出来的新牌要能接着当材料，否则产线到顶就没有下一步了");
    }

    /// <summary>
    /// 从开局牌序出发能到达的新牌种（走 BFS：每一轮拿手上已有的牌试配方）。
    /// 只算「配方表里有、且手上份数够」的那一步，所以它就是玩家能试出来的路径。
    ///
    /// <para><b>存量按份数记，不按有几种记</b>：两种柴只能出一次田垄，
    /// 够不够是两次输入的事，用「有没有柴」判会凭空多出两张牌。</para>
    /// </summary>
    private static IReadOnlyList<CardKind> ReachableNewKinds(IReadOnlyList<string> hand)
    {
        var have = new Dictionary<CardKind, int>();
        foreach (var token in hand) Add(have, CardCatalog.Parse(token));

        var seen = new HashSet<CardKind>(have.Keys);
        var found = new List<CardKind>();

        // 玩家手上永远只有那么多张，所以按「手里有什么」逐轮放宽，直到不再有新发现。
        for (var round = 0; round < 3; round++)
        {
            var produced = new List<CardKind>();
            foreach (var r in RecipeBook.Recipes)
            {
                if (!Enough(have, r)) continue;
                if (!seen.Add(r.Output)) continue;

                // 这一轮真的把这几份花掉：下一轮手上少的就是真的少了。
                foreach (var input in r.Inputs) have[input] -= 1;
                have[r.Output] = have.GetValueOrDefault(r.Output) + 1;
                produced.Add(r.Output);
            }

            if (produced.Count == 0) break;
            found.AddRange(produced);
        }

        return found;
    }

    [Fact]
    public void 扩牌集没有碰坏任何已有机制_产粮牌的间隔为正_间隔为零的牌只当材料或当被叠的那张()
    {
        // 房屋牌进来之后这一条改了判据。
        // 旧版断言的是「每种牌的间隔都大于 0」，那是房屋牌进来之前的事实：
        // 一张间隔为 0 的牌会立刻撞上「不产出的牌派不了工」那条守卫，
        // 于是开局那一手牌里混进一张就只能当材料。为了让旧断言变绿而给房屋
        // 配一个非零间隔，等于把设计做反 —— 房屋必须不产出。
        //
        // 新判据：产粮牌的间隔为正、每次产出为正；
        // 间隔为 0 的那几张只当材料（是某条配方的输入）或当被叠的那张
        // （一张产粮牌叠上去挂接建得起来），两条都不许去派工。
        var world = new MistCityWorld(new MistCityConfig());

        foreach (var spec in CardCatalog.Specs)
        {
            if (spec.IntervalSteps > 0)
            {
                spec.FoodPerCycle.ShouldBeGreaterThan(0, $"产粮牌 {spec.Kind} 每次产出必须是正数");
                continue;
            }

            // 间隔为 0 ⇒ 它不产食物。
            spec.FoodPerCycle.ShouldBe(0, $"牌种 {spec.Kind} 的间隔是 0，它就不该产出食物");

            var asMaterial = RecipeBook.Recipes.Any(r => r.Inputs.Contains(spec.Kind));
            var asHost = HostsACard(spec.Kind);
            (asMaterial || asHost).ShouldBeTrue(
                $"牌种 {spec.Kind} 间隔为 0，它要么是某条配方的输入（当材料），"
                + "要么能接住叠上去的牌（当被叠的那张）—— 两条都不是的话它就是一张死牌");
        }

        world.DebugSnapshot().ShouldContain("card count=7", Case.Sensitive,
            "前置：这几格在开局那一手牌上判，桌上仍是七张");
    }

    /// <summary>间隔为零的牌只当材料或当被叠的那张 —— <b>它自己派不了工</b>。</summary>
    [Fact]
    public void 间隔为零的牌只当材料或当被叠的那张_它自己压在产粮牌上落空()
    {
        // 「只能当材料或当被叠的那张」那半句的反面：它自己压到产粮牌上不派工。
        // 那一格是落空（房屋只当基地不当工人），不是抛 —— 玩家路径静默返回。
        foreach (var spec in CardCatalog.Specs.Where(s => s.IntervalSteps < 1))
        {
            var world = new MistCityWorld(new MistCityConfig());
            var house = world.Place(spec.Kind.ToString());
            var onto = world.Place("Stone");

            var result = world.Drop(house, onto);

            result.Action.ShouldBe(DropAction.NoOp, $"{spec.Kind} 压在产粮牌上是落空，不是派工");
            result.Reason.ShouldBe("top-not-producer", "落空要说得清是哪一种");
            world.DebugSnapshot().ShouldNotContain($"B {house} ", Case.Sensitive,
                "落空就是什么都不发生：挂接表里没有它");
            world.HasCard(house).ShouldBeTrue("落空不消耗牌");
            world.Drop(world.Place("DewVial"), house).Action.ShouldBe(DropAction.MoveIn,
                "反过来它能当被叠的那张：产粮牌叠上去是住进去");
        }
    }

    // === 判据的共用件 ===

    /// <summary>一条配方在快照里的那一行 —— 与配方表自己印的那一行同一份口径。</summary>
    private static string RecipeLine(Recipe r)
    {
        var kinds = r.Inputs.OrderBy(k => k).Select(k => k.ToString());
        return $"recipe {RecipeBook.Recipes.ToList().IndexOf(r)} {r.Portions} "
            + $"{string.Join(" ", kinds)} => {r.Output}";
    }

    /// <summary>手上那几份牌（按牌种计数）。</summary>
    private static Dictionary<CardKind, int> HandOf(MistCityWorld world)
    {
        var have = new Dictionary<CardKind, int>();
        foreach (var id in world.Cards.Ids) Add(have, world.CardStateOf(id)!.Kind);
        return have;
    }

    /// <summary>这份配方要的每一份输入手上够不够 —— <b>通用计数判，不特判同种两输入</b>。</summary>
    private static bool Enough(Dictionary<CardKind, int> have, Recipe r)
    {
        var pool = new Dictionary<CardKind, int>(have);
        foreach (var input in r.Inputs)
        {
            if (pool.GetValueOrDefault(input) < 1) return false;
            pool[input] -= 1;
        }

        return true;
    }

    /// <summary>这一个牌种能不能当被叠的那张 —— 放一张产粮牌上去，挂接建得起来就算。</summary>
    private static bool HostsACard(CardKind kind)
    {
        var world = new MistCityWorld(new MistCityConfig());
        var host = world.Place(kind.ToString());
        return world.Drop(world.Place("DewVial"), host).Action == DropAction.MoveIn;
    }

    private static void Add(Dictionary<CardKind, int> counts, CardKind kind)
        => counts[kind] = counts.GetValueOrDefault(kind) + 1;
}
