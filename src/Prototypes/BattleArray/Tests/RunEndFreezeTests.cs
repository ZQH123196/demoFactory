#nullable enable
using DemoFactory.Prototypes.BattleArray.Logic;
using Shouldly;
using Xunit;

namespace BattleArray.Tests;

/// <summary>
/// 局结束这一格 —— 判定之后时间轴冻住（三个原型统一的口径，真值在基类那一处推进口）。
///
/// <para><b>冻结不由世界自己做</b>：宿主（<c>Main</c>）收到局结束事件之后调
/// <c>FreezeTime()</c>。逻辑测试只驱动世界，所以这一格要显式调一次 ——
/// 那正是「冻不冻由宿主答」这条边界的形状。</para>
/// </summary>
public class RunEndFreezeTests
{
    [Fact]
    public void 判定之后冻住时间轴()
    {
        using var world = new BattleWorld(20260926, Formations.ArchersMidline, PlacementComparison.EnemyFormation);
        world.AdvanceUntilEnd().ShouldBeTrue("这一局该打出胜负");
        var atOutcome = world.Tick;

        // 对照组：世界自己不冻（判定是记账，不是暂停），所以先证这一步还推得动。
        world.Advance(1).ShouldBeTrue("没冻结时判定之后照走 —— 冻不冻由宿主答");

        world.FreezeTime();
        var atFreeze = world.Tick;
        world.Advance(10).ShouldBeFalse("局结束 = 冻住时间轴");
        world.Tick.ShouldBe(atFreeze, "冻结中一步都不该走");

        world.OutcomeTick.ShouldBe(atOutcome, "冻结不碰判定那一格");
        world.Outcome.ShouldNotBe(BattleOutcome.Running);
    }
}
