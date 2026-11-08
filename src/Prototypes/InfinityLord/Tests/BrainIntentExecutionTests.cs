#nullable enable
using System;
using System.Linq;
using System.Numerics;
using DemoFactory.GugaFramework.Pathfinding;
using DemoFactory.Prototypes.InfinityLord.Logic;
using Shouldly;
using Xunit;

namespace InfinityLord.Tests;

/// <summary>
/// 行为树意图在逻辑层的**执行**测试 —— 树只写意图码，怎么走/打不打由 <c>LogicWorld</c> 落地。
/// 这一层能被纯 C# 断言（不碰场景树、不碰 GDScript），所以「随机游荡」这类新动词的方向随机数
/// 是否真确定，在这里钉死，不用等 headless 跑一局。
/// </summary>
public class BrainIntentExecutionTests : LogicWorldTestBase
{
    /// <summary>取一名英雄并交给树（与表现层 AttachBrain 走的是同一个口）。</summary>
    private Unit HandOneHeroToBrain()
    {
        Advance(25); // 首个英雄在第 21 步（2.1 秒）落场
        var hero = Heroes().First();
        World.DelegateHeroToBrain(hero.Id);
        return hero;
    }

    [Fact]
    public void Wander意图_每步挪一步_且不超过本步速度()
    {
        var hero = HandOneHeroToBrain();
        var path = new System.Collections.Generic.List<(float X, float Y)> { (hero.Position.X, hero.Position.Y) };

        for (var i = 0; i < 5; i++)
        {
            World.ReportBrainIntent(hero.Id, (int)BrainIntent.Wander, string.Empty);
            Advance(1);
            path.Add((hero.Position.X, hero.Position.Y));
        }

        for (var i = 1; i < path.Count; i++)
        {
            var step = System.Numerics.Vector2.Distance(
                new System.Numerics.Vector2(path[i - 1].X, path[i - 1].Y),
                new System.Numerics.Vector2(path[i].X, path[i].Y));
            step.ShouldBeLessThanOrEqualTo(hero.Speed + 0.01f, $"第 {i} 步走了 {step:F2} 米，超过速度 {hero.Speed:F2}");
        }

        path.Distinct().Count().ShouldBeGreaterThan(1, "游荡五步原地不动 = 没执行");
    }

    [Fact]
    public void Wander意图_方向出自世界的带种子随机数_同种子两次轨迹逐步相同()
    {
        var first = WanderTrajectory(seed: 7);
        var second = WanderTrajectory(seed: 7);
        var other = WanderTrajectory(seed: 8);

        second.ShouldBe(first, "同一种子跑两次轨迹不同 = 游荡不可复现");
        other.ShouldNotBe(first, "换种子轨迹一模一样 = 随机数没生效");
    }

    [Fact]
    public void Wander意图_不设交战目标_游荡就是游荡()
    {
        var hero = HandOneHeroToBrain();

        World.ReportBrainIntent(hero.Id, (int)BrainIntent.Wander, string.Empty);
        Advance(1);

        hero.TargetUnitId.ShouldBeNull("游荡不该偷偷把目标挂上（那是 Engage 的语义）");
    }

    [Fact]
    public void 没给意图的步_原地发呆_不替树做决定()
    {
        var hero = HandOneHeroToBrain();

        // 交接之后树没写意图，逻辑层宁可发呆：每一步发一条原地不动（ApplyBrainIntent 那一档），
        // 所以它不会照上一条命令继续走 —— 命令当步生效，决策段排在走位那一次脉搏之前。
        Advance(1);
        var before = hero.Position;

        Advance(1);

        hero.Position.ShouldBe(before, "树没写意图时逻辑层宁可发呆 —— 对照组要的是「树说什么做什么」");
    }

    /// <summary>
    /// 树那一侧的口令表要覆盖每一个意图 —— 读源码点名（这一份住在吃 Godot 类型的任务节点里，
    /// 链不进本工程，所以只能这样钉）。
    ///
    /// <para><b>为什么值得一条专门的用例</b>：漏一支的后果不是「这一步不下令」，而是
    /// 「这名英雄原地不动一整局」—— 接管在册之后世界跳过它自己那条决策链，而接管表停在初始的
    /// <c>None</c> 上，每步发一条「原地不动」。实测踩过：<c>Wander</c> 漏了一支，
    /// hero_0 从第 1 步站到第 301 步，零收入被开除、阵亡；行为流里树这一步一步写得都对，
    /// 只读行为流看不出来。</para>
    /// </summary>
    [Fact]
    public void 树的意图口令表覆盖每一个意图()
    {
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "AI", "HeroBrainTask.cs"));
        System.IO.File.Exists(path).ShouldBeTrue($"读不到 {path}（路径拼错了这条就是空跑）");
        var text = System.IO.File.ReadAllText(path);

        foreach (var intent in Enum.GetValues<BrainIntent>())
        {
            text.Contains($"case BrainIntent.{intent}:", StringComparison.Ordinal).ShouldBeTrue(
                $"意图 {intent} 在 Dispatch 里要有自己那一支 —— 漏了它，被接管的英雄会被自己的树冻在原地");
        }
    }

    /// <summary>
    /// 逃跑那一步<b>真的挪了</b>，而且方向背离目标。
    ///
    /// <para>这一条钉的是「位置只走移动解那一口」：世界那一边直写 <c>unit.Position</c> 时，
    /// 这一步末尾的镜像同步会把那一笔抹掉 —— 读数看着像挪了，下一步它还在原处，
    /// 症状是「该跑的时候站着不动」。所以断言落在<b>两步之后的位置</b>上，
    /// 不是下令那一刻读到的镜像。</para>
    ///
    /// <para>推两步是既定次序：命令在决策那一段发出、<b>同一步就走</b>（决策段排在走位那一次脉搏之前），
    /// 第二步再走一次 —— 意图一直留在接管表上，直到树改写它。</para>
    /// </summary>
    [Fact]
    public void Flee意图_逃了一步之后位置真的变了_且方向背离目标()
    {
        var hero = HandOneHeroToBrain();
        var enemy = FirstMonster();
        World.PlaceUnitForTest(hero.Id, QuietSpot).ShouldBeTrue("排布前提：英雄已挪到隔离点");
        World.PlaceUnitForTest(enemy.Id, QuietSpot + new Vector2(2f, 0f)).ShouldBeTrue("排布前提：敌人摆在身边");
        Advance(1); // 排布落定（镜像在每步末尾照层里的落点抄回来）

        World.ReportBrainIntent(hero.Id, (int)BrainIntent.Flee, enemy.Id);
        Advance(1);
        var before = hero.Position;
        var enemyAt = enemy.Position;

        Advance(1);
        var after = hero.Position;

        after.ShouldNotBe(before, "逃跑那一步之后位置该真的变 —— 站着不动就是这一条要挡的回归");
        var moved = after - before;
        var away = before - enemyAt;
        Vector2.Dot(Vector2.Normalize(moved), Vector2.Normalize(away))
            .ShouldBeGreaterThan(0.9f, "挪的方向要背离目标");
    }

    /// <summary>
    /// 命令换过之后，<b>同一档、同一格号</b>的那一条命令不会被去重省掉。
    ///
    /// <para>这一条钉的是发令口那一份「手上那条命令」的指纹：驻守（树说交战而目标已死那一档）
    /// 改过命令却不更新指纹时，指纹还留在上一条追击那一档上 —— 下一步一条同格号的追击命令
    /// 被判成「与手上那条一样」而省掉，世界的读数是「发了令，单位站着不动」。</para>
    ///
    /// <para>两名敌人摆在同一个点上（格心），所以两条追击命令的目标格号逐字相同 ——
    /// 比的是格号，摆在格心上才不受这一两步的位移影响。</para>
    ///
    /// <para>每报一次意图推两步：意图在同一步的决策段被读走、同一步就走
    /// （决策那一段排在走位那一次脉搏之前），推两步是给「命令换过之后」留够落定的步数。</para>
    /// </summary>
    [Fact]
    public void 驻守换过命令之后_同格号的追击命令不会被去重省掉()
    {
        var hero = HandOneHeroToBrain();
        var (first, second) = TwoMonsters();
        World.PlaceUnitForTest(hero.Id, QuietSpot).ShouldBeTrue("排布前提：英雄已挪到隔离点");
        World.PlaceUnitForTest(first.Id, DuelSpot).ShouldBeTrue("排布前提：第一只怪摆好");
        World.PlaceUnitForTest(second.Id, DuelSpot).ShouldBeTrue("排布前提：第二只怪摆在同一点");
        Advance(1);

        // ① 追击：命令真发下去（射程 6 米、10 米开外，所以是追击不是出手）
        World.ReportBrainIntent(hero.Id, (int)BrainIntent.Engage, first.Id);
        Advance(2);
        World.Nav.SolutionOf(hero.Id).Command.ShouldBe(NavCommandKind.Chase, "前提：追击命令已发下去");

        // ② 目标死：下一步树还说交战、目标已不在场 → 那一档改成驻守（命令换了）
        World.DebugDamageUnit(first.Id, 10_000).ShouldBeTrue("排布前提：目标已打掉");
        Advance(2);
        World.Nav.SolutionOf(hero.Id).Command.ShouldBe(NavCommandKind.Hold, "前提：目标死后的那一步改成驻守");

        // ③ 新目标落在同一格：命令该真的换回追击
        World.ReportBrainIntent(hero.Id, (int)BrainIntent.Engage, second.Id);
        Advance(2);
        var solution = World.Nav.SolutionOf(hero.Id);
        solution.Command.ShouldBe(NavCommandKind.Chase,
            "同格号的追击命令被判「一样」而省掉了 = 世界发了令而单位站着不动");
        solution.Goal.DistanceTo(InfinityLordNavGrid.ToGrid(second.Position.X, second.Position.Y))
            .ShouldBeLessThan(1.0, "追击的目标点就是新目标此刻的位置");
    }

    /// <summary>隔离点（世界坐标）—— 远离镇子、巢穴与波次入侵点（与技能层那几条用例同一点）。</summary>
    private static readonly Vector2 QuietSpot = new(-15, 42);

    /// <summary>两名敌人共用的落点（世界坐标）—— 格心，格号 (15, 72)。</summary>
    private static readonly Vector2 DuelSpot = new(-4.5f, 42.5f);

    /// <summary>推到场上出现第一只怪物（巢穴按步产出，不写死步号）。</summary>
    private Unit FirstMonster()
    {
        for (var guard = 0; guard < 400 && Monsters().Count == 0; guard++) Advance(1);
        Monsters().Count.ShouldBeGreaterThan(0, $"推到第 {World.Tick} 步仍没有怪物");
        return Monsters().First();
    }

    /// <summary>推到场上凑够两只怪物。</summary>
    private (Unit First, Unit Second) TwoMonsters()
    {
        for (var guard = 0; guard < 400 && Monsters().Count < 2; guard++) Advance(1);
        Monsters().Count.ShouldBeGreaterThanOrEqualTo(2, $"推到第 {World.Tick} 步仍凑不够两只怪物");
        return (Monsters()[0], Monsters()[1]);
    }

    private static System.Collections.Generic.List<(float X, float Y)> WanderTrajectory(int seed)
    {
        using var world = new LogicWorld(randomSeed: seed);
        world.Advance(25); // 等首个英雄落场
        var hero = world.Units.First(u => u.Side == Faction.Hero && u.Health > 0);
        world.DelegateHeroToBrain(hero.Id);
        var path = new System.Collections.Generic.List<(float X, float Y)>();
        for (var i = 0; i < 8; i++)
        {
            world.ReportBrainIntent(hero.Id, (int)BrainIntent.Wander, string.Empty);
            world.Advance(1);
            path.Add((hero.Position.X, hero.Position.Y));
        }

        return path;
    }
}
