#nullable enable
using System;
using System.Linq;
using DemoFactory.Prototypes.MistCity.Logic;
using Shouldly;
using Xunit;

namespace MistCity.Tests;

/// <summary>
/// 房屋三级与保留上限（ticket 05）—— 本轮最大的一张逻辑 ticket，三件事一起进来：
/// ① 三种房屋牌组成升级链；② 配方输入从两个位改成任意份；③ 保留上限从
/// 「桌上几张」改成「一轮进食结算后可保留几张」。
///
/// <para><b>断言只从命令进、快照出</b>（spec「Testing Decisions」）：
/// 不断言内部结构，不断言字段名。所以保留上限与超出的张数都进快照 ——
/// 轮末处置要依据它，而依据不进快照就没法从录像倒推玩家为什么丢了牌。</para>
///
/// <para><b>「轮末之前不拦、结算那一刻处置」这一格最容易写反</b>：
/// 上限压的是玩家的选择，不是玩家的手。所以两条断言分开钉：
/// 推到第 59 步时张数仍然超着，推到第 60 步那一刻才被处置掉。</para>
/// </summary>
public class HouseTests
{
    /// <summary>一轮 = <see cref="Rations.CycleSteps"/> 步。</summary>
    private const int Cycle = Rations.CycleSteps;

    // === 升级链：棚 → 屋 → 仓 ===

    [Fact]
    public void 三条房屋配方都能合出来_升级链是连着的()
    {
        var world = NewWorld();

        // 棚 = 一份柴 + 一份田垄（开局那一手就有：四柴两石一泉）。
        // 用户裁决换掉了原定的「2 柴 + 1 石」：那一摞里任意两张本身都是配方，
        // 玩家拖上去当场合成掉，摞长不到 3 张，棚这一级根本造不出来。
        var furrow = NewestAfter(world.Combine(["c01", "c02"]));
        var shed = NewestAfter(world.Combine([furrow, "c03"]));
        world.CardStateOf(shed)!.Kind.ShouldBe(CardKind.Shed, "柴 + 田垄 出棚");

        // 屋 = 一份棚 + 两份石（场上还有两张石没动）。
        var house = NewestAfter(world.Combine([shed, "c05", "c06"]));
        world.CardStateOf(house)!.Kind.ShouldBe(CardKind.House, "棚 + 2 石 出屋");

        // 仓 = 一份屋 + 两份泉眼。泉眼是柴 + 石 合出来的，所以先各造出一张。
        var springA = NewestAfter(world.Combine(["c04", world.Place("Stone")]));
        var springB = world.Place("Spring");
        var granary = NewestAfter(world.Combine([house, springA, springB]));
        world.CardStateOf(granary)!.Kind.ShouldBe(CardKind.Granary, "屋 + 2 泉眼 出仓");

        // 链是连着的：仓吃的是屋，不是棚 —— 三张互不相干的配方不是升级链。
        RecipeBook.Recipes.ShouldContain(r =>
            r.Output == CardKind.Granary && r.Inputs.Contains(CardKind.House),
            "仓的输入里要有屋，「先凑小的、后面换大的」这件事才成立");
    }

    [Fact]
    public void 配方按无序多重集判_次序不同合成同一张棚()
    {
        // 田垄 + 柴 与 柴 + 田垄 是同一条配方：玩家摆放的顺序不该改变结果。
        var a = NewWorld();
        var b = NewWorld();

        var first = NewestAfter(a.Combine([NewestAfter(a.Combine(["c01", "c02"])), "c03"]));
        var second = NewestAfter(b.Combine(["c03", NewestAfter(b.Combine(["c01", "c02"]))]));

        world0Kind(a, first).ShouldBe(CardKind.Shed);
        world0Kind(b, second).ShouldBe(CardKind.Shed, "两种次序合成同一张棚");

        // 快照也逐字一致：两份同样输入的世界，除了牌 id 一样，读数没有任何差别。
        a.DebugSnapshot().ShouldBe(b.DebugSnapshot(), "同一种子重跑逐字一致");

        static CardKind world0Kind(MistCityWorld w, string id) => w.CardStateOf(id)!.Kind;
    }

    [Fact]
    public void 同种两输入是退化情形_柴加柴仍然出田垄()
    {
        // 「同种两输入」不再单独特判，它只是多重集里牌种相同的那一种。
        var world = NewWorld();

        var furrow = NewestAfter(world.Combine(["c01", "c02"]));

        world.CardStateOf(furrow)!.Kind.ShouldBe(CardKind.Furrow,
            "同种两份柴按多重集折出来的键与别的配方不重，仍然是田垄");
    }

    // === 房屋牌本身不产食物 ===

    [Fact]
    public void 房屋牌本身不产食物_推到结算点也不出()
    {
        var world = NewWorld();
        var shed = world.Place("Shed");
        world.Drop(world.Place("DewVial"), shed);   // 一张产粮牌住进去

        world.Advance(Cycle);

        // 住户不进那份食物的账：间隔为 0 的牌「每次产���多少」是 0。
        world.Cards.Require(shed).Produced.ShouldBe(0, "房屋牌自己从不产出");
        CardLine(world.DebugSnapshot(), shed).ShouldContain("every=0", Case.Sensitive,
            "间隔为 0 就是不产出");
    }

    // === 房屋牌作为承载者 ===

    [Fact]
    public void 房屋牌能接住叠上去的牌_挂接建起来且从快照读得到()
    {
        var world = NewWorld();
        var shed = world.Place("Shed");
        var occupant = world.Place("DewVial");

        var result = world.Drop(occupant, shed);

        result.Action.ShouldBe(DropAction.MoveIn, "叠到房屋上不是派工，是住进去");
        result.Happened.ShouldBeTrue("不落空 —— 房屋牌是这一轮才开始有对象的承载者");

        var snap = world.DebugSnapshot();
        snap.ShouldContain("bind count=1", Case.Sensitive);
        snap.ShouldContain($"B {occupant} onto={shed} kind=Resident every=0", Case.Sensitive,
            "挂接从快照读得到：两端、类型、间隔都印出来");
        snap.ShouldContain($"C {shed} ", Case.Sensitive, "承载者那张也在");
        snap.ShouldContain("card count=9", Case.Sensitive, "住进去不消耗牌");
    }

    [Fact]
    public void 住进去的那张不产食物_它不像派工那样出货()
    {
        var world = NewWorld();
        var shed = world.Place("Shed");
        var occupant = world.Place("DewVial");
        world.Drop(occupant, shed);

        world.Advance(4);   // 夜露瓶的间隔是 1 步，推进四步它早该产四份

        world.Cards.Require(occupant).Produced.ShouldBe(0,
            "住户不产食物 —— 产出物是人不是份，由底下那张牌决定");
        CardLine(world.DebugSnapshot(), occupant).ShouldContain("out=0", Case.Sensitive);
    }

    [Fact]
    public void 住进去的那张_与它压在别处互斥()
    {
        // 一张牌只有一份力气：压在产粮牌上就一直产食物，压在房屋上就一直当住户。
        var world = NewWorld();
        var shed = world.Place("Shed");
        var occupant = world.Place("DewVial");
        world.Drop(occupant, shed);

        var again = world.Drop(occupant, world.Place("Stone"));

        again.Action.ShouldBe(DropAction.NoOp, "它已经住进去了，压到产粮牌上不动");
        again.Reason.ShouldBe("already-attached", "一张牌只压一处");
        world.Bindings.CardOnto(shed).ShouldHaveSingleItem("它仍然只挂在房屋那一张上");

        // 拿下来之后两件事才互斥：住过的拆掉，再压产粮牌才是派工。
        world.Drop(occupant, DropTarget.Blank).Action.ShouldBe(DropAction.TakeDown,
            "住户退房走的是拿下来那条路，挂接表清空");
        world.Drop(occupant, "c05").Action.ShouldBe(DropAction.Attach,
            "换一张承载者，它就变成派工了");
    }

    // === 每轮可保留的牌数上限 ===

    [Fact]
    public void 基础上限是6_场上每张房屋牌往上加()
    {
        var world = NewWorld();

        world.DebugSnapshot().ShouldContain("keep=6", Case.Sensitive,
            "基础上限 6，开局没有房屋牌");

        world.Place("Shed");
        world.DebugSnapshot().ShouldContain("keep=8", Case.Sensitive, "一张棚 +2");
        world.Place("House");
        world.DebugSnapshot().ShouldContain("keep=13", Case.Sensitive, "再加一张屋 +5");
        world.Place("Granary");
        world.DebugSnapshot().ShouldContain("keep=23", Case.Sensitive, "再加一张仓 +10");

        // 加成是活数：丢掉一张立刻掉回去。
        world.Drop(CardOf(world, CardKind.Granary), DropTarget.Discard);
        world.DebugSnapshot().ShouldContain("keep=13", Case.Sensitive, "仓不再算那一档");
    }

    [Fact]
    public void 保留上限与超出的张数都能从快照读出_每一步都报()
    {
        var world = NewWorld();

        // 开局七张对上限六，所以开局就超一张 —— 这是刻意留的：
        // 第一轮就把这条规则教给玩家。
        var snap = world.DebugSnapshot();
        snap.ShouldContain("card count=7", Case.Sensitive);
        snap.ShouldContain("keep=6", Case.Sensitive);
        snap.ShouldContain("over=1", Case.Sensitive, "超出的张数是下一轮会被处置掉的那几张");
    }

    [Fact]
    public void 轮末之前张数可以超过上限_不被当场拦住()
    {
        var world = NewWorld();

        world.Advance(Cycle - 1);

        // 第 59 步：七张仍在桌上，扣饭照跑，一个字都没动过手。
        var snap = world.DebugSnapshotAt(Cycle - 1);
        snap.ShouldContain("card count=7", Case.Sensitive,
            "上限压的是玩家的选择，不是玩家的手 —— 轮末之前不拦");
        snap.ShouldContain("over=1", Case.Sensitive, "超出的张数一直在报");
        snap.ShouldContain("settled=0", Case.Sensitive, "还没到结算点");
    }

    [Fact]
    public void 结算那一刻超出的部分被处置掉_走的是弃牌堆那条路()
    {
        var world = NewWorld();
        world.Advance(Cycle - 1);

        world.Advance(1);   // 推到第 60 步，结算那一刻

        var snap = world.DebugSnapshot();
        snap.ShouldContain("card count=6", Case.Sensitive, "超出的那一张在结算那一刻被处置");
        snap.ShouldContain("over=0", Case.Sensitive, "处置之后不超了");
        snap.ShouldContain("settled=1", Case.Sensitive);
        snap.ShouldNotContain("C c01 ", Case.Sensitive,
            "处置走牌册登记次序，从最老的那一张开始丢");
        snap.ShouldContain("C c02 ", Case.Sensitive, "只丢一张，其余不动");
    }

    [Fact]
    public void 处置次序是牌册登记次序_同种子重跑丢的是同样那几张()
    {
        // 处置次序没有出处的话，重放端会与录制端丢掉不同的牌。
        var a = Trimmed();
        var b = Trimmed();

        a.ShouldBe(b, "同一颗种子重跑，被丢掉的那几张逐字相同");

        static string Trimmed()
        {
            var world = NewWorld();
            world.Advance(Cycle);
            return world.DebugSnapshot();
        }
    }

    [Fact]
    public void 场上房屋牌抬高上限_轮末因此不必处置()
    {
        var world = NewWorld();

        // 丢掉一张柴、放一张棚：桌上仍是七张，但上限从 6 抬到 8。
        world.Drop("c01", DropTarget.Discard);
        world.Place("Shed");
        world.DebugSnapshot().ShouldContain("over=0", Case.Sensitive, "棚那一档把缺口补上了");

        world.Advance(Cycle);

        world.DebugSnapshot().ShouldContain("card count=7", Case.Sensitive,
            "结算那一刻不处置任何一张：桌上没超出抬起来的上限");
    }

    [Fact]
    public void 轮末处置走同一条写路径_被丢的那张身上的挂接一起腾干净()
    {
        var world = NewWorld();

        // c01 压 c05 派工，而 c01 是牌册上最老的那张 —— 轮末先丢它。
        world.Bind("c01", "c05");
        world.Advance(Cycle);

        var snap = world.DebugSnapshot();
        snap.ShouldNotContain("C c01 ", Case.Sensitive, "最老的那一张被处置");
        snap.ShouldNotContain("onto=c05", Case.Sensitive,
            "丢弃走的是拖到弃牌堆那条路：它身上那条挂接一起腾干净");
        snap.ShouldContain("C c05 ", Case.Sensitive, "被压着的那张还在");
        snap.ShouldContain("bind count=0", Case.Sensitive);
    }

    // === 共用件 ===

    private static MistCityWorld NewWorld() => new(new MistCityConfig());

    /// <summary>场上某个牌种的那一张牌 id（没有那种牌当场抛 —— 那是断言写错了）。</summary>
    private static string CardOf(MistCityWorld world, CardKind kind)
    {
        foreach (var id in world.Cards.Ids)
        {
            if (world.CardStateOf(id)!.Kind == kind) return id;
        }

        throw new ShouldAssertException($"桌上没有 {kind} 那张牌");
    }

    /// <summary>跑一发合成，返回新发的那张牌 id（新牌最后进册）。</summary>
    private static string NewestAfter(string combineText)
    {
        var parts = combineText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts[^2];
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
