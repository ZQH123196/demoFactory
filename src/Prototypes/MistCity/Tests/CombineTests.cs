#nullable enable
using System;
using System.Linq;
using DemoFactory.Prototypes.MistCity.Logic;
using Shouldly;
using Xunit;

namespace MistCity.Tests;

/// <summary>
/// 合成（ticket 05）—— 消耗型动作最怕两样：旧牌还在册，或者序号被复用。
/// 这两样在画面上都看不见，只有快照读得出册子。
/// </summary>
public class CombineTests
{
    [Fact]
    public void 合成之后_两张旧牌不在快照里_新牌在册且序号比两张旧牌都大()
    {
        var world = new MistCityWorld(new MistCityConfig());
        var oldA = world.Cards.SeqOf("c01");
        var oldB = world.Cards.SeqOf("c02");

        var newId = NewIdFrom(world.Combine("c01", "c02"));
        var snap = world.DebugSnapshot();

        snap.ShouldNotContain("C c01", Case.Sensitive, "消耗掉的那张不在册了");
        snap.ShouldNotContain("C c02", Case.Sensitive, "两张都不在册了");
        snap.ShouldContain("C " + newId, Case.Sensitive, "新牌在册");

        var newSeq = world.Cards.SeqOf(newId);
        newSeq.ShouldBeGreaterThan(oldA, "新序号比第一张旧牌大");
        newSeq.ShouldBeGreaterThan(oldB, "新序号比第二张旧牌大 —— 序号不复用");
    }

    [Fact]
    public void 合成_产出的是配方表说的那个牌种()
    {
        var world = new MistCityWorld(new MistCityConfig());

        // 柴 + 柴 → 田垄
        var id = NewIdFrom(world.Combine("c01", "c02"));
        world.CardStateOf(id).ShouldNotBeNull();
        world.CardStateOf(id)!.Kind.ShouldBe(CardKind.Furrow);
    }

    [Fact]
    public void 合成_输入次序不影响结果()
    {
        var a = new MistCityWorld(new MistCityConfig());
        var b = new MistCityWorld(new MistCityConfig());

        // 柴+石 与 石+柴 是同一条配方：玩家点选的顺序不该改变结果。
        var first = a.Combine("c01", "c05");
        var second = b.Combine("c05", "c01");

        NewIdFrom(first).ShouldBe(NewIdFrom(second), "同一对牌，正反次序合出同一张");
    }

    [Fact]
    public void 旧牌身上还挂着绑定_合成之后残留为零()
    {
        var world = new MistCityWorld(new MistCityConfig());
        world.Bind("c07", "c05");   // c07 压 c05

        // 消耗掉 c05（它身上有人压）与 c07（它自己压着别人）—— 一条绑定的两端都要腾干净。
        world.Combine("c07", "c05");

        var snap = world.DebugSnapshot();
        snap.ShouldContain("bind count=0", Case.Sensitive,
            "快照里读不到悬空的绑定端 —— 这是这张片最容易漏的一处");
        snap.ShouldNotContain("onto=c05", Case.Sensitive);
        snap.ShouldNotContain("B c07", Case.Sensitive);
    }

    [Fact]
    public void 合成_牌不在册当场抛()
    {
        var world = new MistCityWorld(new MistCityConfig());

        var threw = Should.Throw<InvalidOperationException>(() => world.Combine("c01", "c99"));
        threw.Message.ShouldContain("c99", Case.Sensitive, "消息里念得出缺的是哪一张");
        threw.Message.ShouldContain("在桌上", Case.Sensitive);
    }

    [Fact]
    public void 合成_两张不是同一条当场抛_消息里念得出那一对叫什么()
    {
        var world = new MistCityWorld(new MistCityConfig());

        // 泉眼 + 石 在表里（→ 灰烬炉），所以换一对表里没有的：泉眼 + 田垄。
        world.Combine("c01", "c02");   // 先造出田垄
        var furrow = FindCardOfKind(world, CardKind.Furrow);

        var threw = Should.Throw<InvalidOperationException>(() => world.Combine("c07", furrow));
        threw.Message.ShouldContain("不在配方表里", Case.Sensitive,
            "命令写错要在注入那一刻炸出来，不能混进「合成失败只损失材料」那种游戏反馈里");
    }

    [Fact]
    public void 合成_拿一张牌合成自己当场抛()
    {
        var world = new MistCityWorld(new MistCityConfig());

        var threw = Should.Throw<InvalidOperationException>(() => world.Combine("c01", "c01"));
        threw.Message.ShouldContain("只能算一张牌", Case.Sensitive);
    }

    [Fact]
    public void 合成_输入份数不够当场抛_不静默从桌上再拿一张补上()
    {
        // 三条房屋配方都是三份输入。少给一份时当场抛，
        // 而不是从桌上抓一张凑上去 —— 补上去的那一张玩家没看见。
        var world = new MistCityWorld(new MistCityConfig());

        // 柴 + 石 是两条输入，出泉眼；柴 + 石 + 石 这三张在表里没有。
        // 所以只能靠输入不够来触发：同一条配方少一份的那一档。
        var shed = world.Place("Shed");
        var before = world.DebugSnapshot();

        var threw = Should.Throw<InvalidOperationException>(() => world.Combine("c05", shed));

        threw.Message.ShouldContain("不在配方表里", Case.Sensitive,
            "这些牌合不出东西 —— 当场抛，不静默当合成失败");
        world.DebugSnapshot().ShouldBe(before,
            "抛出来的那一发一个字都不改：牌没被消耗，也没从桌上再拿一张补上");
        world.Cards.Count.ShouldBe(8, "桌上仍是放进去的八张（开局七张 + 刚放的那张棚）");
    }

    [Fact]
    public void 合成_同一个id放了两次当场抛_不凭空多出一张牌()
    {
        var world = new MistCityWorld(new MistCityConfig());

        // 三份输入凑一条棚：柴 + 柴 + 石。把同一张柴放了两次就是「一张牌算两份」。
        var threw = Should.Throw<InvalidOperationException>(
            () => world.Combine(["c01", "c01", "c05"]));

        threw.Message.ShouldContain("只能算一张牌", Case.Sensitive);
        world.Cards.Count.ShouldBe(7, "抛出来的那一发不消耗牌");
    }

    [Fact]
    public void 合成_配方表每条都能从快照读出()
    {
        var world = new MistCityWorld(new MistCityConfig());

        var snap = world.DebugSnapshot();
        snap.ShouldContain($"recipe count={RecipeBook.Recipes.Count}", Case.Sensitive,
            "配方表条数与设计时定的相等（ticket 07 的验收之一）");
        foreach (var r in RecipeBook.Recipes)
        {
            snap.ShouldContain(LineOf(r), Case.Sensitive,
                "每条配方都要能进快照，否则 08 之后没法从录像倒推玩家试到了哪一步");
        }
    }

    /// <summary>一条配方在快照里的那一行 —— 输入按牌种排开、份数写在前面。</summary>
    private static string LineOf(Recipe r)
    {
        var kinds = r.Inputs.OrderBy(k => k).Select(k => k.ToString());
        return $"recipe {RecipeBook.Recipes.ToList().IndexOf(r)} {r.Portions} "
            + $"{string.Join(" ", kinds)} => {r.Output}";
    }

    private static string NewIdFrom(string combineResult)
    {
        // combine 的返回串是「combine ok c01+c02 => c07 kind=Furrow」，新牌是倒数第二段。
        var parts = combineResult.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts[^2];
    }

    private static string FindCardOfKind(MistCityWorld world, CardKind kind)
    {
        foreach (var id in world.Cards.Ids)
        {
            if (world.CardStateOf(id)!.Kind == kind) return id;
        }

        throw new ShouldAssertException($"桌上没有 {kind} 那张牌");
    }
}
