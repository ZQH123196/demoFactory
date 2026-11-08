#nullable enable
using DemoFactory.Prototypes.MistCity.Logic;
using Shouldly;
using Xunit;

namespace MistCity.Tests;

/// <summary>
/// 暂停时照样能拖（ticket 08）—— <b>暂停只冻逻辑时钟，手势不跟着冻</b>。
///
/// <para><b>最容易错的那一格是结算点</b>：把暂停当成「跳过」的话，
/// 结算会跟着往后挪，玩家每按一次空格就偷走一轮的压力。
/// 所以这里逐字钉住「停一百步之后，结算仍在第六十步」。</para>
///
/// <para><b>空格键那一格断言不到</b>：键位读法、连按不连触发、
/// 菜单吃掉空格这三格住在表现层与引擎输入里，离线跑不到，真机验。</para>
///
/// <para>断言从命令进、快照出，不断言内部结构与字段名。</para>
public class PauseTests
{
    [Fact]
    public void 停一百步再继续_结算仍在第六十步()
    {
        var world = NewWorld();
        world.Advance(55);

        world.Pause();
        world.Advance(100).ShouldBeFalse("暂停中推进是空转");
        world.Tick.ShouldBe(55, "停住的时间一步都不走");
        world.Food.LastSettlementStep.ShouldBe(0, "还没到第六十步，没有结算过");

        world.Resume();
        world.Advance(5);

        world.Tick.ShouldBe(60);
        world.Food.LastSettlementStep.ShouldBe(60, "结算点没有被暂停挪走");
        world.Food.SettlementIndex.ShouldBe(1, "这一次结算只在第六十步发生一次");
    }

    [Fact]
    public void 暂停中推进空转_快照逐字不变()
    {
        var world = NewWorld();
        world.Advance(7);
        world.Pause();
        var before = world.DebugSnapshot();

        world.Advance(1000);

        world.DebugSnapshot().ShouldBe(before,
            "暂停中一步都不走，快照逐字不变：tick 冻结，牌与账也不动");
    }

    [Fact]
    public void 暂停中叠牌照样合成()
    {
        var world = NewWorld();
        world.Pause();

        var result = world.Drop("c01", "c07");

        var snap = world.DebugSnapshot();
        result.Text.ShouldStartWith("combine ok", Case.Sensitive, "柴 + 泉眼 出夜露瓶");
        snap.ShouldContain("C c08 seq=8 kind=DewVial", Case.Sensitive);
        snap.ShouldContain("card count=6", Case.Sensitive, "两张换一张");
        snap.ShouldContain("tick=0", Case.Sensitive, "叠牌不推时间");
    }

    [Fact]
    public void 暂停中拿下来照样拆产线()
    {
        var world = NewWorld();
        world.Bind("c07", "c05");
        world.Pause();

        var result = world.Drop("c07", DropTarget.Blank);

        var snap = world.DebugSnapshot();
        result.Text.ShouldStartWith("unbind ok", Case.Sensitive, "拿下来照常拆产线");
        snap.ShouldContain("bind count=0", Case.Sensitive);
        snap.ShouldContain("card count=7", Case.Sensitive, "拿下来不损失牌");
    }

    [Fact]
    public void 暂停中拖到弃牌堆照样丢()
    {
        var world = NewWorld();
        var wood = world.Place("Wood");
        world.Pause();

        var result = world.Drop(wood, DropTarget.Discard);

        var snap = world.DebugSnapshot();
        result.Text.ShouldStartWith("discard ok", Case.Sensitive, "丢弃照常生效");
        snap.ShouldNotContain($"C {wood} ", Case.Sensitive, "丢掉的牌不在册里了");
        snap.ShouldContain("card count=7", Case.Sensitive, "桌上少一张");
    }

    [Fact]
    public void 同一对牌在暂停与运行两局判定逐字一致()
    {
        var running = NewWorld();
        var paused = NewWorld();
        paused.Pause();

        var a = running.Drop("c01", "c07");
        var b = paused.Drop("c01", "c07");

        b.Text.ShouldBe(a.Text, "暂停只冻时钟，落点判定走同一个方法");
        paused.DebugSnapshot().ShouldBe(running.DebugSnapshot(),
            "暂停中叠牌之后的世界与运行中逐字一致");
    }

    private static MistCityWorld NewWorld() => new(new MistCityConfig());
}