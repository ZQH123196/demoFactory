#nullable enable
using System;
using DemoFactory.Prototypes.MistCity.Logic;
using Shouldly;
using Xunit;

namespace MistCity.Tests;

/// <summary>
/// 拿下来（ticket 04）—— 拖到空白处把一张牌从承载者身上拿下来。
///
/// <para><b>这一格要挡住的是「解绑与移位混成一件事」</b>：解绑一件没绑过的牌当场抛，
/// 而一张本来就没有承载者的牌拿到空白处是单纯移位、不抛。自由平面上后者是常事
/// （第一版的牌位是网格，落点本来就是空白的情形不存在）。
/// 把两件事答成同一件的后果是「玩家把一张没绑过的牌挪个位置，游戏炸了」，
/// 而那一炸只在玩家那条路上出现，离线跑注入看不出来。</para>
///
/// <para><b>累计产出那一格不清零</b>：那是「这条产线干过多少活」的账，
/// 拿下来只是拆掉产线，不抹掉它干过的活。断言从命令进、快照出，不断言内部结构与字段名。
/// 位置是表现层数据，离线断言不到，真机出图验。</para>
public class TakeDownTests
{
    [Fact]
    public void 直接解绑一件没绑过的当场抛_拿同一张落到空白处不抛()
    {
        var world = NewWorld();

        // c06 是石，本轮没有压在谁身上。两条路问的是同一张牌，答案要分得开。
        var threw = Should.Throw<InvalidOperationException>(() => world.Unbind("c06"));
        threw.Message.ShouldContain("c06", Case.Sensitive,
            "消息念得出是哪一张出的问题");
        threw.Message.ShouldContain("没有压在任何牌身上", Case.Sensitive,
            "没绑过与不在册是两句不同的话，这一句说得出是没绑过");

        var result = world.Drop("c06", DropTarget.Blank);

        result.Happened.ShouldBeTrue("这一发是正常情形 —— 单纯移位，不抛");
        result.MovedOnly.ShouldBeTrue("读得出这一发是移位而不是解绑");
        result.Text.ShouldContain("not-bound", Case.Sensitive,
            "读数说得出这一张本来就没有承载者");
    }

    [Fact]
    public void 拿下来之后累计产出那一格留在牌上_不清零()
    {
        var world = NewWorld();
        world.Bind("c07", "c05");
        world.Advance(4); // c07 是泉眼，间隔 3：推进四步它已经干过一活
        var before = world.DebugSnapshot();
        before.ShouldContain("C c07 seq=7 kind=Spring every=3 out=1", Case.Sensitive,
            "前置：拿下来之前那张牌已经攒下一份产出 —— 断言要建在一个非零数上");

        world.Drop("c07", DropTarget.Blank);

        var snap = world.DebugSnapshot();
        snap.ShouldContain("C c07 seq=7 kind=Spring every=3 out=1", Case.Sensitive,
            "累计产出留在牌上：那是这条产线干过多少活的账，拆掉产线不抹掉它");
        snap.ShouldContain("bind count=0", Case.Sensitive, "产线拆了");
        snap.ShouldContain("card count=7", Case.Sensitive, "拿下来不损失牌，两张都还在");
        snap.ShouldContain("C c05 ", Case.Sensitive, "被压着的那张也在");
    }

    private static MistCityWorld NewWorld() => new(new MistCityConfig());
}
