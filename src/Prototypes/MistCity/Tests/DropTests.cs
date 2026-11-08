#nullable enable
using DemoFactory.Prototypes.MistCity.Logic;
using Shouldly;
using Xunit;

namespace MistCity.Tests;

/// <summary>
/// 叠放落点判定（ticket 02）—— 一个手势里同时装着合成与派工。
///
/// <para><b>判据只有一条顺序</b>：先问配方，再问能不能接上去，两条都不通就什么都不发生。
/// 第一版那种「合不出配方就兜底成派工」的写法撑不住拖放 —— 拖过去可能只是随手试探，
/// 每一次都有反应的话试探永远不会落空，而落空正是发现的来源。</para>
///
/// <para><b>落空要能断言</b>：「什么都没发生」在画面上只是那张牌动了，
/// 所以落空那一档要说得出一句原因，且数据一个字都不动。</para>
///
/// <para>断言只从命令进、快照出，不断言内部结构与字段名。
/// 点与拖的分界（8 像素）与拖动手势住在表现层，离线断言不到，真机验。</para>
public class DropTests
{
    [Fact]
    public void 有配方的两张叠上去_合成一张_快照里两张旧牌都不在()
    {
        var world = NewWorld();

        var result = world.Drop("c01", "c07");

        var snap = world.DebugSnapshot();
        snap.ShouldNotContain("C c01", Case.Sensitive, "上面那张被消耗掉了");
        snap.ShouldNotContain("C c07", Case.Sensitive, "底下那张被消耗掉了");
        snap.ShouldContain("C c08 seq=8 kind=DewVial", Case.Sensitive,
            "柴 + 泉眼 出夜露瓶，新牌发新序号");
        snap.ShouldContain("card count=6", Case.Sensitive, "两张换一张，桌上少一张");
        snap.ShouldContain("bind count=0", Case.Sensitive, "合成不产生产线");
        result.Text.ShouldStartWith("combine ok", Case.Sensitive, "读数说得出发生了什么");
    }

    [Fact]
    public void 没配方但底下会产粮_叠上去变成产线()
    {
        var world = NewWorld();

        // 灰烬炉在配方表里跟谁都合不出来，而石会产粮 —— 这一对只能派工。
        var kiln = world.Place("Kiln");
        var result = world.Drop(kiln, "c05");

        var snap = world.DebugSnapshot();
        snap.ShouldContain("card count=8", Case.Sensitive, "派工不消耗材料，两张都还在");
        snap.ShouldContain($"B {kiln} onto=c05 kind=Production every=7 in=7", Case.Sensitive,
            "上面那张按自己的间隔产食物；挂接类型那一格也读得出");
        result.Text.ShouldStartWith("bind ok", Case.Sensitive, "读数说得出发生了什么");
    }

    [Fact]
    public void 判定顺序先问配方_同一对不会又合成又派工()
    {
        var world = NewWorld();

        // 柴 + 石 在配方表里（出泉眼），而两张也都能产粮 —— 派工那一档同样成立。
        // 结果是合成，说明配方排在前面。
        world.Drop("c01", "c05");

        var snap = world.DebugSnapshot();
        snap.ShouldNotContain("C c01", Case.Sensitive, "柴那张被合成消耗掉了");
        snap.ShouldNotContain("C c05", Case.Sensitive, "石那张被合成消耗掉了");
        snap.ShouldContain("bind count=0", Case.Sensitive,
            "没有派工 —— 有配方时先问配方，问到就合成，不问能不能接上去");
    }

    [Fact]
    public void 两条都不通时什么都不发生_快照逐字没变()
    {
        var world = NewWorld();
        var before = world.DebugSnapshot();

        var result = world.Drop("c05", "c05");

        world.DebugSnapshot().ShouldBe(before,
            "落空是「什么都不发生」：数据一个字都不动，也不抛");
        result.Text.ShouldStartWith("no-op", Case.Sensitive, "落空要说得出一句原因");
        result.Text.ShouldContain("same-card", Case.Sensitive, "原因说得清是哪一种");
    }

    [Fact]
    public void 上面那张已经在别处挂着_落空不抛_原来的产线还在()
    {
        var world = NewWorld();
        world.Bind("c01", "c06");

        // 灰烬炉跟柴没有配方，所以这一对进不到合成那一档；
        // 而 c01 已经压在 c06 上了 —— 一张牌只压一处，这一格落空。
        var kiln = world.Place("Kiln");
        var before = world.DebugSnapshot();
        var result = world.Drop("c01", kiln);

        world.DebugSnapshot().ShouldBe(before, "落空，数据不动");
        result.Text.ShouldContain("already-attached", Case.Sensitive,
            "一张牌只压一处，这一条要说得出来");
    }

    [Fact]
    public void 一张牌下面能叠多张_三张依次压上去三条挂接都在册()
    {
        var world = NewWorld();

        // 灰烬炉跟谁都合不出来，三张灰烬炉压同一张石 —— 只能一条条派工。
        var a = world.Place("Kiln");
        var b = world.Place("Kiln");
        var c = world.Place("Kiln");
        world.Drop(a, "c05");
        world.Drop(b, "c05");
        world.Drop(c, "c05");

        var snap = world.DebugSnapshot();
        snap.ShouldContain("bind count=3", Case.Sensitive, "一张牌下面叠了三张");
        snap.ShouldContain($"B {a} onto=c05", Case.Sensitive);
        snap.ShouldContain($"B {b} onto=c05", Case.Sensitive);
        snap.ShouldContain($"B {c} onto=c05", Case.Sensitive);
    }

    [Fact]
    public void 摘掉压在同一张上的其中一张_另外两条仍在册()
    {
        var world = NewWorld();
        var a = world.Place("Kiln");
        var b = world.Place("Kiln");
        var c = world.Place("Kiln");
        world.Drop(a, "c05");
        world.Drop(b, "c05");
        world.Drop(c, "c05");

        world.Unbind(b);

        var snap = world.DebugSnapshot();
        snap.ShouldContain("bind count=2", Case.Sensitive, "摘一条剩两条");
        snap.ShouldContain($"B {a} onto=c05", Case.Sensitive, "先压上去的那条还在");
        snap.ShouldContain($"B {c} onto=c05", Case.Sensitive, "后压上去的那条也在");
        snap.ShouldNotContain($"B {b} ", Case.Sensitive, "被摘掉的那条不在册里了");
    }

    [Fact]
    public void 合成消耗掉一张上面压着三张的牌_三条挂接一次腾干净()
    {
        var world = NewWorld();
        var a = world.Place("Kiln");
        var b = world.Place("Kiln");
        var c = world.Place("Kiln");
        world.Drop(a, "c05");
        world.Drop(b, "c05");
        world.Drop(c, "c05");

        // 石 + 石 出灰烬炉，c05 被消耗掉 —— 压在它上面的三条要一起腾干净。
        world.Drop("c05", "c06");

        var snap = world.DebugSnapshot();
        snap.ShouldContain("bind count=0", Case.Sensitive,
            "被消耗掉的那张身上挂着几条就清几条，不留悬空端");
        snap.ShouldNotContain("onto=c05", Case.Sensitive, "快照里查不到指向已消耗那张的引用");
    }

    [Fact]
    public void 牌不在册当场抛_那是命令写错不是落空()
    {
        var world = NewWorld();

        Should.Throw<System.InvalidOperationException>(() => world.Drop("c01", "c99"))
            .Message.ShouldContain("在桌上", Case.Sensitive);
    }

    private static MistCityWorld NewWorld() => new(new MistCityConfig());
}
