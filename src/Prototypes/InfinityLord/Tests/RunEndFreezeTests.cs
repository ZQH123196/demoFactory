#nullable enable
using DemoFactory.Prototypes.InfinityLord.Logic;
using Shouldly;
using Xunit;

namespace InfinityLord.Tests;

/// <summary>
/// 「局结束 = 冻住时间轴」这条统一口径，在本原型上的那一格。
///
/// <para><b>为什么这一格要单独立一条</b>：本世界的 <c>Advance</c> 重写了基类的推进口，
/// 直接调 <c>Assembly.Advance(steps, DrainConstructionEvents)</c>、不回基类 ——
/// 闸若开在基类那一处，这一条路就绕过去了。实测过：冻结之后
/// <c>Advance(10)</c> 仍回 true、步号照爬。闸因此改开在装配那一侧
/// （<c>WorldAssembly.FrozenProbe</c>），三条推进的路都从那里过。</para>
///
/// <para>另两个原型各有同族用例：<c>BeastArena/Tests/RunEndFreezeTests.cs</c>、
/// <c>BattleArray/Tests/RunEndFreezeTests.cs</c>。</para>
/// </summary>
public class RunEndFreezeTests : LogicWorldTestBase
{
    [Fact]
    public void 局结束之后推进那一格直接回假()
    {
        World.Advance(120);
        World.Advance(1).ShouldBeTrue("对照组：没冻结时推进照走");
        var before = World.Tick;

        World.FreezeTime();

        World.Advance(50).ShouldBeFalse("局结束 = 冻住时间轴");
        World.Tick.ShouldBe(before, "冻结中一步都不该走 —— 步号继续爬就是那个「判定之后还在第几秒」的坑");
        World.TimeFrozen.ShouldBeTrue();
    }

    [Fact]
    public void 局结束之后推到目标步那一格也推不动()
    {
        World.Advance(60);
        var before = World.Tick;
        var target = before + 300;

        World.FreezeTime();

        World.AdvanceTo(target);
        World.Tick.ShouldBe(before, "快进与重放共用的那一条路，冻结后同样一步都不走");
    }

    [Fact]
    public void 冻结判在装配那一侧_宿主重写推进口也拦得住()
    {
        // 本世界是三个里唯一重写 Advance 的。若闸只开在 GameLogicBase 上，这条会红。
        World.Advance(30).ShouldBeTrue();
        World.FreezeTime();
        World.Advance(10).ShouldBeFalse();
    }

    [Fact]
    public void 冻结不是判定_冻结不改写胜负真值()
    {
        World.Advance(120);
        var outcomeBefore = World.Outcome;

        World.FreezeTime();

        // 冻结是「时间轴停在这里」，不是「这一局判成某一边」。
        // 判据写成「冻结前后的胜负真值逐字相同」—— 若有人把冻结接到了判定那一格，这一格就会漂。
        World.Outcome.ShouldBe(outcomeBefore, "冻结只停推进，不改胜负那一格");
        World.Outcome.ShouldBe(GameOutcome.Running, "冻结不会把一局在跑的世界读成已分出胜负");
    }
}