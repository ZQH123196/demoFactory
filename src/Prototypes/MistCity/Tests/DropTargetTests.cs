#nullable enable
using System;
using System.Linq;
using DemoFactory.Prototypes.MistCity.Logic;
using Shouldly;
using Xunit;

namespace MistCity.Tests;

/// <summary>
/// 落点判定收三种值（ticket 03）—— 一张牌 / 空白处 / 弃牌堆共用同一个判定方法。
///
/// <para><b>这一张 ticket 要挡住的是「三种手势三份实现」</b>：三种落点如果写成三个判定方法，
/// 改一次规则要改三处，而漏掉的那一处症状是「玩家叠牌没反应，但注入同样的牌有反应」——
/// 那是最难查的一类偏差，因为现象只在玩家那条路上出现。所以断言只问一件事：
/// 三种值走的那个方法答得对不对。</para>
///
/// <para><b>落空要能断言</b>：「什么都没发生」在画面上只是那张牌动了，
/// 所以落空那一档要说得出一句原因，返回串里没有 ok，数据一个字都不动。</para>
///
/// <para>断言只从命令进、快照出，不断言内部结构与字段名。
/// 三种落点在玩家那边怎么解析成坐标（扫空白处、认弃牌堆那块）住在表现层，离线断言不到，真机验。</para>
public class DropTargetTests
{
    [Fact]
    public void 落点解析_牌id_blank_discard各解析成一种()
    {
        DropTarget.Parse("c05").Kind.ShouldBe(DropTargetKind.Card, "牌 id 解析成底下有一张牌");
        DropTarget.Parse("c05").CardId.ShouldBe("c05");
        DropTarget.Parse("blank").Kind.ShouldBe(DropTargetKind.Blank, "blank 解析成空白处");
        DropTarget.Parse("discard").Kind.ShouldBe(DropTargetKind.Discard, "discard 解析成弃牌堆");
    }

    [Fact]
    public void 落到空白处_有承载者的走解绑那条路_挂接断开两张牌都还在()
    {
        var world = NewWorld();
        world.Bind("c07", "c05");

        var result = world.Drop("c07", DropTarget.Blank);

        var snap = world.DebugSnapshot();
        snap.ShouldContain("bind count=0", Case.Sensitive, "产线拆了 —— 空白处走的是解绑那条路");
        snap.ShouldNotContain("B c07", Case.Sensitive, "那条挂接不在册里了");
        snap.ShouldContain("C c07 ", Case.Sensitive, "拿下来不损失牌（玩家故事 6）");
        snap.ShouldContain("C c05 ", Case.Sensitive, "被压着的那张也在");
        snap.ShouldContain("card count=7", Case.Sensitive, "两张都还在，桌上张数没变");
        result.Action.ShouldBe(DropAction.TakeDown, "读数说得出这一档是拿下来");
        result.MovedOnly.ShouldBeFalse("它本来压在 c05 身上，不是单纯移位");
    }

    [Fact]
    public void 落到空白处_本来就没有承载者的_是单纯移位_挂接表与累计产出都不动()
    {
        var world = NewWorld();

        // 先造一条别的产线，让「挂接表不动」这一格有东西可比。
        world.Bind("c07", "c05");
        world.Advance(4); // 推进几步让累计产出动起来，移位不该把它抹掉
        var before = world.DebugSnapshot();

        // c06 是石，本轮没有压在谁身上 —— 落到空白处是单纯移位。
        var result = world.Drop("c06", DropTarget.Blank);

        world.DebugSnapshot().ShouldBe(before,
            "单纯移位：挂接表与累计产出读数一个字都不动（位置是表现层数据，不进逻辑层快照）");
        result.Action.ShouldBe(DropAction.TakeDown, "仍然答「拿下来」那一档");
        result.MovedOnly.ShouldBeTrue("这一发读得出它是移位而不是解绑");
    }

    [Fact]
    public void 丢弃_走实体层的移除次序_牌从册子上消失()
    {
        var world = NewWorld();

        world.Drop("c06", DropTarget.Discard);

        var snap = world.DebugSnapshot();
        snap.ShouldNotContain("C c06", Case.Sensitive, "丢掉的那张不在册了");
        snap.ShouldContain("card count=6", Case.Sensitive, "桌上少一张");
        snap.ShouldContain("nextseq=8", Case.Sensitive, "发过的序号不回收");
    }

    [Fact]
    public void 丢弃_身上挂着三条挂接的_三条一次腾干净_快照里查不到悬挂引用()
    {
        var world = NewWorld();
        var a = world.Place("Kiln");
        var b = world.Place("Kiln");
        var c = world.Place("Kiln");
        world.Drop(a, "c05");
        world.Drop(b, "c05");
        world.Drop(c, "c05");

        // 丢掉承载者 c05：它自己没压着谁，身上压着三条。
        world.Drop("c05", DropTarget.Discard);

        var snap = world.DebugSnapshot();
        snap.ShouldContain("bind count=0", Case.Sensitive, "它身上挂着几条就清几条");
        snap.ShouldNotContain("onto=c05", Case.Sensitive,
            "快照里查不到指向已丢弃那张的引用 —— 悬挂挂接在画面上只看得出「牌不见了」");
    }

    [Fact]
    public void 丢弃_自己压着别的那张_自己那条挂接也一起清掉()
    {
        var world = NewWorld();
        world.Bind("c07", "c05");

        // 丢掉的是占用者（自己压着 c05），不是承载者。
        world.Drop("c07", DropTarget.Discard);

        var snap = world.DebugSnapshot();
        snap.ShouldContain("bind count=0", Case.Sensitive, "自己那条挂接一起清掉");
        snap.ShouldNotContain("B c07", Case.Sensitive);
    }

    [Fact]
    public void 丢弃之后_sequence不复用_丢掉的牌拿不回来()
    {
        var world = NewWorld();
        world.Drop("c06", DropTarget.Discard);

        var text = world.Place("Stone");

        // 丢掉的那张 c06 不回来，而 c06 这个序号也不会再发给别的牌。
        text.ShouldNotBe("c06", "序号不因移除而复用");
        world.HasCard("c06").ShouldBeFalse("丢掉的那张拿不回来");
    }

    [Fact]
    public void 两端是同一张牌_按落空处理_不合不派_快照逐字没变()
    {
        var world = NewWorld();
        world.Bind("c07", "c05");
        var before = world.DebugSnapshot();

        var result = world.Drop("c03", DropTarget.Onto("c03"));

        world.DebugSnapshot().ShouldBe(before, "落空：牌册与挂接表逐字不变");
        result.Action.ShouldBe(DropAction.NoOp, "同一张牌不合成、不派工");
        result.Reason.ShouldBe("same-card", "原因短词说得出是哪一种落空");
        result.Text.ShouldNotContain("ok", Case.Sensitive, "落空的返回串里没有 ok");
    }

    [Fact]
    public void 上面那张已经在别处挂着_落空_原来的产线还在()
    {
        var world = NewWorld();
        // 灰烬炉在配方表里跟谁都合不出来，所以这一对进不到合成那一档。
        var kiln = world.Place("Kiln");
        world.Bind(kiln, "c05");
        var before = world.DebugSnapshot();

        var result = world.Drop(kiln, DropTarget.Onto("c04"));

        world.DebugSnapshot().ShouldBe(before, "落空，数据不动");
        result.Reason.ShouldBe("already-attached", "一张牌只压一处，这一条要说得出来");
        result.Text.ShouldNotContain("ok", Case.Sensitive, "落空的返回串里没有 ok");
    }

    [Fact]
    public void 落空的那一句_以落空两字起头_说得出为什么什么都没发生()
    {
        var world = NewWorld();
        var kiln = world.Place("Kiln");
        world.Bind(kiln, "c05");

        var result = world.Drop(kiln, DropTarget.Onto("c04"));

        result.Text.ShouldStartWith("no-op already-attached", Case.Sensitive,
            "落空要说得出一句原因，且那一句以 no-op 起头");
    }

    [Fact]
    public void 合成过程中的空白中间态_不会被读成空白处()
    {
        var world = NewWorld();

        // 柴 + 泉眼 出夜露瓶：中间会短暂没有第二张牌（底下那张被消耗掉了）。
        var result = world.Drop("c01", DropTarget.Onto("c07"));

        var snap = world.DebugSnapshot();
        snap.ShouldNotContain("C c01", Case.Sensitive, "上面那张被消耗掉了");
        snap.ShouldNotContain("C c07", Case.Sensitive, "底下那张被消耗掉了");
        snap.ShouldContain("card count=6", Case.Sensitive, "两张换一张，桌上少一张");
        snap.ShouldContain("bind count=0", Case.Sensitive,
            "挂接表没有多出也没有少掉一条 —— 中间态没有被当成拿下来拆掉什么");
        result.Action.ShouldBe(DropAction.Combine, "这一发的答案是合成，不是拿下来");
    }

    [Fact]
    public void 合成之后_拿被消耗掉的那张去空白处_当场抛而不是静默当成移位()
    {
        var world = NewWorld();
        world.Drop("c01", DropTarget.Onto("c07"));

        // 空白处是一个写明的值，不是「那个 id 查不到」。
        // 所以拿一个已不在册的 id 走空白处那一支是命令写错，当场抛。
        var threw = Should.Throw<InvalidOperationException>(() => world.Drop("c07", DropTarget.Blank));
        threw.Message.ShouldContain("不在桌上", Case.Sensitive,
            "合成中间态留下的那个 id 不是空白处 —— 命令写错要炸出来，不静默当成移位");
    }

    [Fact]
    public void 上面那张是房屋牌_落空_它只当基地不当工人()
    {
        // 房屋牌是牌集里第一张间隔为 0 的牌，这一格从「形状先立住」变成真的会命中。
        // 落空而不是抛：玩家路径静默返回，注入路径返回一句说得出原因的话。
        var world = NewWorld();
        var shed = world.Place("Shed");
        var before = world.DebugSnapshot();

        var result = world.Drop(shed, "c05");

        world.DebugSnapshot().ShouldBe(before, "落空：牌册与挂接表逐字不变");
        result.Action.ShouldBe(DropAction.NoOp);
        result.Reason.ShouldBe("top-not-producer", "原因短词说得出是哪一种落空");
    }

    [Fact]
    public void 底下那张既不是房屋牌也不产粮_本牌集里还没有对象_形状先立住()
    {
        // 底下是房屋牌但住满（bottom-full）那一格要住客上限，由后面那张 ticket填，
        // 所以现在不命中。而「既不产粮又不是房屋」的那种牌种牌集里还没有，
        // 底部不产粮那一格同样不命中。断言的是这个事实本身 ——
        // 牌种表加了第三类牌时这条断言就红，那时它自己就能验了。
        var kinds = CardCatalog.Specs.Select(s => s.Kind).ToList();

        kinds.Any(k => !CardCatalog.IsHouse(k) && CardCatalog.Of(k).IntervalSteps < 1).ShouldBeFalse(
            "牌种表里出现了既不产粮又不是房屋的牌种：叠上去落到 bottom-not-producer 那一格了");

        var world = NewWorld();

        // 每一对底牌都答得出一条原因或一件事，没有哪一对掉进「派不了工」那条守卫去抛。
        // 灰烬炉在配方表里跟谁都合不出来，所以这一对要么派工要么落空。
        var kiln = world.Place("Kiln");
        var result = world.Drop(kiln, DropTarget.Onto("c05"));
        result.Happened.ShouldBeTrue("灰烬炉压到石上：要么派工要么落空，不抛");
    }

    [Fact]
    public void 牌不在册当场抛_那是命令写错不是落空()
    {
        var world = NewWorld();

        Should.Throw<InvalidOperationException>(() => world.Drop("c01", DropTarget.Onto("c99")))
            .Message.ShouldContain("在桌上", Case.Sensitive, "底下那张不在册：命令写错");

        Should.Throw<InvalidOperationException>(() => world.Drop("c99", DropTarget.Blank))
            .Message.ShouldContain("在桌上", Case.Sensitive, "上面那张不在册：命令写错");
    }

    [Fact]
    public void 牌id落点空的那个字_当场抛_不兜一个空白处()
    {
        var world = NewWorld();

        Should.Throw<ArgumentException>(() => DropTarget.Onto(string.Empty))
            .Message.ShouldContain("blank", Case.Sensitive,
                "牌 id 那一档不给 id 要炸出来 —— 静默兜成空白处就是拿下来");
    }

    [Fact]
    public void 三种落点走的是同一个判定方法_同一对牌两条路读数逐字一致()
    {
        // 「三种值共用同一个判定方法」这一格从外部验：玩家那条路与注入那条路
        // 折出来的落点不同（坐标是表现层数据），但答出的那句话逐字相同。
        var byToken = NewWorld();
        var byTarget = NewWorld();
        var a = byToken.Drop("c01", "c07");
        var b = byTarget.Drop("c01", DropTarget.Onto("c07"));

        a.Text.ShouldBe(b.Text, "落点是牌 id 时，两种写法答同一句话");
        byToken.DebugSnapshot().ShouldBe(byTarget.DebugSnapshot(), "快照也逐字一致");
    }

    private static MistCityWorld NewWorld() => new(new MistCityConfig());
}