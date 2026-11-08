#nullable enable
using System;
using DemoFactory.Prototypes.MistCity.Logic;
using Shouldly;
using Xunit;

namespace MistCity.Tests;

/// <summary>
/// 派工与绑定关系模块（ticket 03）—— 本原型风险最集中的一片：
/// 绑错了、重复绑、解了有残留，这三样在画面上都长一个样，全靠当场抛与快照两端把它挡住。
/// </summary>
public class BindingTests
{
    [Fact]
    public void bind_快照读出这条绑定的两端与产出间隔()
    {
        var world = NewWorld();

        world.Bind("c07", "c05");

        var snap = world.DebugSnapshot();
        snap.ShouldContain("bind count=1", Case.Sensitive, "桌上一条绑定");
        snap.ShouldContain("B c07 onto=c05 kind=Production every=3", Case.Sensitive,
            "两端、挂接类型与产出间隔都能从快照读出 —— 一出问题第一眼能看出断在哪一端");
        snap.ShouldContain("in=3", Case.Sensitive, "刚绑上那一刻还差一个间隔才产出");
    }

    [Fact]
    public void unbind_这条绑定从快照消失_两张牌都还在()
    {
        var world = NewWorld();
        world.Bind("c07", "c05");

        world.Unbind("c07");

        var snap = world.DebugSnapshot();
        snap.ShouldContain("bind count=0", Case.Sensitive, "绑定从快照消失");
        snap.ShouldContain("C c07 ", Case.Sensitive, "被绑那张还在（解绑不损失材料，玩家故事 6）");
        snap.ShouldContain("C c05 ", Case.Sensitive, "被压那张还在");
    }

    [Fact]
    public void bind_牌不在册当场抛_消息里说得出是哪一端()
    {
        var world = NewWorld();

        var missingWorker = Should.Throw<InvalidOperationException>(() => world.Bind("c99", "c05"));
        missingWorker.Message.ShouldContain("要绑的那张", Case.Sensitive, "是哪一端出的问题");
        missingWorker.Message.ShouldContain("c99", Case.Sensitive);
        missingWorker.Message.ShouldContain("c07", Case.Sensitive, "错误消息里列得出桌上有哪些牌");

        var missingOnto = Should.Throw<InvalidOperationException>(() => world.Bind("c07", "c98"));
        missingOnto.Message.ShouldContain("要压的那张", Case.Sensitive, "两端分开判，两句消息不同");
    }

    [Fact]
    public void bind_重复绑定当场抛_消息里念得出它压在谁身上()
    {
        var world = NewWorld();
        world.Bind("c07", "c05");

        var again = Should.Throw<InvalidOperationException>(() => world.Bind("c07", "c04"));
        again.Message.ShouldContain("已经压在 c05", Case.Sensitive,
            "改产线要先解绑，不损失材料 —— 消息要告诉玩家走哪条路");
    }

    [Fact]
    public void bind_一张牌下面可以叠多张_第二张第三张都不抛()
    {
        var world = NewWorld();

        // c07 是泉眼（间隔 3，会产出），c05 与 c06 都是石（间隔 9，会产出）。
        // 三张各压各的，所以三张都能产 —— 一张牌收几张不是产出上限的问题。
        world.Bind("c07", "c05");
        world.Bind("c06", "c05");
        world.Bind("c04", "c05");

        var snap = world.DebugSnapshot();
        snap.ShouldContain("bind count=3", Case.Sensitive, "一张牌下面叠了三张");
        snap.ShouldContain("B c07 onto=c05", Case.Sensitive);
        snap.ShouldContain("B c06 onto=c05", Case.Sensitive);
        snap.ShouldContain("B c04 onto=c05", Case.Sensitive);
    }

    [Fact]
    public void unbind_摘掉压在同一张上的其中一张_另外两条仍在册()
    {
        var world = NewWorld();
        world.Bind("c07", "c05");
        world.Bind("c06", "c05");
        world.Bind("c04", "c05");

        world.Unbind("c06");

        // 这一格是第一版那个 bug 的形状：多值索引上整条 Remove 会把另外两条一起抹掉，
        // 而画面上只看得见「被摘的那张不动了」。
        var snap = world.DebugSnapshot();
        snap.ShouldContain("bind count=2", Case.Sensitive, "摘一条剩两条");
        snap.ShouldContain("B c07 onto=c05", Case.Sensitive, "先压上去的那条还在");
        snap.ShouldContain("B c04 onto=c05", Case.Sensitive, "后压上去的那条也在");
        snap.ShouldNotContain("B c06", Case.Sensitive, "被摘掉的那条不在册里了");
    }

    [Fact]
    public void bind_绑到自己当场抛()
    {
        var world = NewWorld();

        var self = Should.Throw<InvalidOperationException>(() => world.Bind("c07", "c07"));
        self.Message.ShouldContain("不能压在自己身上", Case.Sensitive);
    }

    [Fact]
    public void bind_不产出的牌派不了工当场抛()
    {
        // 房屋牌进来之前牌种表里每张都产粮，这条守卫一直没有对象，
        // 断言只能反过来钉「牌种表里没有间隔 0 的牌」。
        // 房屋牌进来之后守卫有对象了，所以这一条改成正面验它守得住。
        var world = NewWorld();
        var shed = world.Place("Shed");

        var threw = Should.Throw<InvalidOperationException>(() => world.Bind(shed, "c05"));

        threw.Message.ShouldContain("派不了工", Case.Sensitive,
            "房屋只当基地不当工人：它压在产粮牌上不派工");
        world.DebugSnapshot().ShouldContain("bind count=0", Case.Sensitive,
            "抛出来的那一发没有建成任何挂接");
    }

    [Fact]
    public void unbind_解一张没绑过的当场抛()
    {
        var world = NewWorld();

        var threw = Should.Throw<InvalidOperationException>(() => world.Unbind("c07"));
        threw.Message.ShouldContain("没有压在任何牌身上", Case.Sensitive,
            "解绑一件没发生过的事要炸出来，不能静默当无事发生");
    }

    [Fact]
    public void unbind_解绑之后可以绑到另一张_产线改了材料不损失()
    {
        var world = NewWorld();
        world.Bind("c07", "c05");

        world.Unbind("c07");
        world.Bind("c07", "c04");

        var snap = world.DebugSnapshot();
        snap.ShouldContain("bind count=1", Case.Sensitive, "还是一条绑定");
        snap.ShouldContain("B c07 onto=c04", Case.Sensitive, "换了一张被压着的那张");
        snap.ShouldContain("C c05 ", Case.Sensitive, "原来那张还在桌面上");
    }

    [Fact]
    public void 建造层一行未改_绑定不进建造层()
    {
        // 判据是形状而不是文件内容：牌的关系不进实体层的册（实体层只印 id/序号/kind），
        // 也不进建造模块的册 —— 所以牌册那一段之外，快照里没有第二份「谁压在谁」的册。
        var world = NewWorld();
        world.Bind("c07", "c05");

        var snap = world.DebugSnapshot();
        var cardSegment = snap.Split("bind count=")[0];
        cardSegment.ShouldNotContain("onto=", Case.Sensitive,
            "牌册那一段不印绑定两端 —— 两端住在绑定模块自己的段里，全快照只有那一份");
    }

    private static MistCityWorld NewWorld() => new(new MistCityConfig());
}
