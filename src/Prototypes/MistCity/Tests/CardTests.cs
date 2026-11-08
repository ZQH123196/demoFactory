#nullable enable
using System;
using System.Linq;
using DemoFactory.Prototypes.MistCity.Logic;
using Shouldly;
using Xunit;

namespace MistCity.Tests;

/// <summary>
/// 牌能进能出（ticket 02）—— 只测外部行为：命令进、快照出。
/// 不断言内部字段名，不断言实现结构（spec「Testing Decisions」）。
/// </summary>
public class CardTests
{
    [Fact]
    public void place_快照一次读出全部牌_类别与序号都在()
    {
        var world = new MistCityWorld(new MistCityConfig());

        var snap = world.DebugSnapshot();
        snap.ShouldContain("card count=7", Case.Sensitive, "开局发七张牌（够试三条配方，不够把牌集摊开）");

        // 每张牌一行，行首是牌 id，后面是序号、类别、自有字段。
        var cardLine = CardLine(snap, "c01");
        cardLine.ShouldContain("seq=1", Case.Sensitive, "实体层发的那一格序号");
        cardLine.ShouldContain("kind=Wood", Case.Sensitive, "牌的类别是原型自有字段，由原型自己印");
        cardLine.ShouldContain("every=4", Case.Sensitive, "柴的产出间隔是 4 步（间隔以步计，牌与牌长短不一）");
    }

    [Fact]
    public void place_牌种解析不出当场抛_不兜一个默认牌种()
    {
        var world = new MistCityWorld(new MistCityConfig());

        var threw = Should.Throw<ArgumentException>(() => world.Place("不存在的牌"));
        threw.Message.ShouldContain("不存在的牌", Case.Sensitive, "错误消息要念回收到的那一串");
        threw.Message.ShouldContain("Wood", Case.Sensitive, "错误消息里要念得出词表，注入的人才能改对");
    }

    [Fact]
    public void place_牌种解析吃枚举名与中文显示名两种写法_解析不出当场抛()
    {
        var world = new MistCityWorld(new MistCityConfig());

        world.Place("Wood").ShouldBe("c08");
        world.Place("柴").ShouldBe("c09", "同一个意思的另一种写法也对");

        // 显示名对不上（泉眼不叫「春」）就是解析不出，当场抛而不是挑一个近的。
        Should.Throw<ArgumentException>(() => world.Place("春"));
    }

    [Fact]
    public void 序号不因移除而复用_新牌拿到更大的序号()
    {
        var world = new MistCityWorld(new MistCityConfig());
        var before = world.Cards.NextSeq;

        // 消耗掉一张（柴+柴 → 田垄），再放一张新的：序号必须比刚消耗掉的那两张大。
        world.Combine("c01", "c02");
        var placed = world.Cards.SeqOf(world.Place("Wood"));

        placed.ShouldBeGreaterThan(before - 1, "实体层发序号，永不复用");
        world.Cards.IssuedCount.ShouldBe(9, "发过九张牌了（七张开局 + 一次合成的产物 + 刚放的）");
    }

    [Fact]
    public void 牌被消耗掉_快照立刻读不到它()
    {
        var world = new MistCityWorld(new MistCityConfig());

        world.Combine("c01", "c02");

        var snap = world.DebugSnapshot();
        snap.ShouldNotContain("C c01", Case.Sensitive);
        snap.ShouldNotContain("C c02", Case.Sensitive);
        snap.ShouldContain("card count=6", Case.Sensitive, "两张旧的走了，一张新的来了，在册从 7 变 6");
    }

    [Fact]
    public void 事件流容量显式抬高_大批量增删不丢事件()
    {
        var world = new MistCityWorld(new MistCityConfig());

        for (var i = 0; i < 400; i++) world.Place("Wood");

        // 容量 2048：407 次 place 与后面那几次消耗的事件都还在。
        world.Cards.Entities.Events.Dropped.ShouldBe(0,
            "实体层事件默认容量 256 对不上牌的数量，构造时必须显式抬高 —— 丢了事件读数上就查不出来");
        world.Cards.Count.ShouldBe(407);
    }

    [Fact]
    public void 每张在册牌的间隔都能从快照读到()
    {
        var world = new MistCityWorld(new MistCityConfig());

        var snap = world.DebugSnapshot();
        CardLine(snap, "c07").ShouldContain("every=3", Case.Sensitive, "泉眼间隔 3 步（那一手牌里唯一会产出的那张）");
        CardLine(snap, "c06").ShouldContain("every=9", Case.Sensitive, "石间隔 9 步");
    }

    /// <summary>从快照里取出某张牌那一行（折行后是 <c>C &lt;id&gt; seq=… </c> 这么一串 token）。</summary>
    private static string CardLine(string snapshot, string id)
    {
        var tokens = snapshot.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < tokens.Length - 1; i++)
        {
            if (tokens[i] != "C" || tokens[i + 1] != id) continue;

            var parts = new System.Collections.Generic.List<string> { id };
            for (var j = i + 2; j < tokens.Length && tokens[j] != "C"; j++) parts.Add(tokens[j]);
            return string.Join(" ", parts);
        }

        throw new ShouldAssertException($"快照里没有牌 {id} 那一行：{snapshot}");
    }
}
