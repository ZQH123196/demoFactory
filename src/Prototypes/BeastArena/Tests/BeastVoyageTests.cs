#nullable enable
using System.Linq;
using DemoFactory.GugaFramework.Pathfinding;
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 大地图上巨兽的航行：两城往返、脱队觅食回原断点、出界扣魂石与归零送回。
/// 纯 C# 逻辑直接推进，不起引擎。
/// </summary>
public class BeastVoyageTests
{
    private const int Seed = 7;

    /// <summary>240 米航程在 0.4 米每秒下要 600 现实秒 = 6000 步，多给一点余量。</summary>
    private static int LegSteps => 7000;

    [Fact]
    public void 巨兽登记在大地图上_甲板那一张没有它()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        world.MapStepper.AgentCount.ShouldBe(1, "大地图上只有巨兽一只。");
        world.MapStepper.HasAgent(BeastVoyage.BeastId).ShouldBeTrue("巨兽在大地图那张网格上。");
        world.MapStepper.AgentIds[0].ShouldBe(BeastVoyage.BeastId, "登记次序即步进次序。");

        // 甲板那张网格上只有镇民（合并后的世界开局就发 12 名），巨兽不在这张网格上。
        world.DeckStepper.AgentCount.ShouldBe(world.Roster.Units.Count, "甲板上登记的单位就是全镇民。");
        world.DeckStepper.HasAgent(BeastVoyage.BeastId).ShouldBeFalse("两个空间各是各的。");
    }

    [Fact]
    public void 巨兽从贫城西边的野地出发_开箱坐标与规格一致()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        world.Voyage.Position.ShouldBe(BigMapGrid.RunStart, "开局停在贫城西边的野地那一格（未停靠）。");
        world.Voyage.Phase.ShouldBe(VoyagePhase.Holding, "开局没有目标，停着。");
        world.Voyage.TargetCityId.ShouldBeNull("开局没有航行令。");
        world.IsDocked.ShouldBeFalse("开局未停靠：玩家点「停靠」才进港。");
        world.DockedCityId.ShouldBeEmpty("开局没有停靠城 id。");
        world.Voyage.Satiation.ShouldBe(ArenaTuning.StartingSatiation, "开局饱食度 80。");
        world.Voyage.SoulStones.ShouldBe(ArenaTuning.StartingSoulStones, "开局魂石 30。");

        // 删掉开局自动航行令之后，推进也不再自己开出去。
        world.Advance(100);
        world.Voyage.Phase.ShouldBe(VoyagePhase.Holding, "没人下令就停在野地上。");
        world.Voyage.Position.ShouldBe(BigMapGrid.RunStart, "坐标仍在开局那一格。");
        world.IsDocked.ShouldBeFalse("推进一步也不会自动进港。");
    }

    [Fact]
    public void 两城往返_走满一段航程就到对面那座城()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        // 这一条验的是航程与到点判定，不是生态：饱食度摆满，免得半路掉进觅食分支。
        world.Voyage.SetSatiation(ArenaTuning.SatiationMax);
        world.Voyage.SailToCity(BigMapGrid.RichCity.Id);
        world.Voyage.Phase.ShouldBe(VoyagePhase.Sailing, "下了航行令。");
        world.Voyage.TargetCityId.ShouldBe(BigMapGrid.RichCity.Id, "目标城记下来了。");

        // 走到位：让寻路层多走一段，用到位容差判。
        for (var i = 0; i < 200 && world.Voyage.Phase != VoyagePhase.Holding; i++)
        {
            world.Advance(100);
        }

        NavArrive.Reached(world.Voyage.Position, BigMapGrid.RichCity.StopPoint(BeastTier.Tier1), ArenaTuning.ArriveToleranceMeters)
            .ShouldBeTrue($"应当走到富城城门外，实际停在 {world.Voyage.Position}");
        world.Voyage.Phase.ShouldBe(VoyagePhase.Holding, "到点停住。");

        // 回程：换目标城再走一趟。
        world.Voyage.SetSatiation(ArenaTuning.SatiationMax);
        world.Voyage.SailToCity(BigMapGrid.PoorCity.Id);
        for (var i = 0; i < 200 && world.Voyage.Phase != VoyagePhase.Holding; i++)
        {
            world.Advance(100);
        }

        NavArrive.Reached(world.Voyage.Position, BigMapGrid.PoorCity.StopPoint(BeastTier.Tier1), ArenaTuning.ArriveToleranceMeters)
            .ShouldBeTrue($"应当走回贫城城门外，实际停在 {world.Voyage.Position}");
    }

    [Fact]
    public void TravelBetweenCitiesTakesTenGameHours()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        // 这一条量的是「两城之间那 240 米」，不是开局那一程：
        // 先站到贫城停靠点上（开局位置在它往西 40 米的野地），否则量到的是 280 米。
        world.MapStepper.ApplyMove(BeastVoyage.BeastId, BigMapGrid.PoorCity.StopPoint(BeastTier.Tier1), 0.0);
        world.Voyage.SailToCity(BigMapGrid.RichCity.Id);

        var elapsed = 0;
        while (elapsed < LegSteps && world.Voyage.Phase == VoyagePhase.Sailing)
        {
            world.Advance(1);
            elapsed++;
        }

        // 设计口径（spec §1.2）：两城相距 240 米，移速 0.4 米每现实秒
        // → 240 ÷ 0.4 = 600 现实秒 = 6000 步 = 600 游戏分钟（1× 档下 1 游戏分钟 = 1 现实秒）。
        var distance = BigMapGrid.PoorCity.Position.DistanceTo(BigMapGrid.RichCity.Position);
        var nominalMinutes = distance / ArenaTuning.BeastSpeedMetersPerSecond / ArenaTime.SecondsPerStep
            / ArenaTime.StepsPerGameMinute;
        nominalMinutes.ShouldBe(600, 1e-9,
            "设计口径：两城 240 米 ÷ 0.4 米每现实秒 = 600 游戏分钟（10 游戏小时）。");

        // 实测比设计口径早「到达容差那一段」：巨兽「到了」判在离城 1.0 米，
        // 那一米 = 25 步 = 2.5 游戏分钟 —— 实测 597.6，量到的是 597.5 那一档。
        var earlyMinutes = ArenaTuning.ArriveToleranceMeters
            / (ArenaTuning.BeastSpeedMetersPerSecond * ArenaTime.SecondsPerStep) / ArenaTime.StepsPerGameMinute;
        var gameMinutes = (double)elapsed / ArenaTime.StepsPerGameMinute;
        gameMinutes.ShouldBe(nominalMinutes - earlyMinutes, 1,
            "实测 = 设计口径 600 − 到达容差那一段 2.5（1.0 米 ÷ 0.04 米每步 = 25 步）。");
    }

    [Fact]
    public void BeastSpeedIsFortyCentimetersPerSecond()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        var before = world.Voyage.Position;

        world.Voyage.SailToCity(BigMapGrid.RichCity.Id);
        world.Advance(1);

        // 0.4 米每现实秒 × 0.1 现实秒每步 = 0.04 米每步。
        world.Voyage.Position.DistanceTo(before).ShouldBe(0.04, 1e-9,
            "一逻辑步走 0.04 米（0.4 米每现实秒 × 0.1 现实秒每步）。");
    }

    [Fact]
    public void 开局没有自动航行令_一个游戏小时之后仍在原地不动()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        var before = world.Voyage.Position;
        before.ShouldBe(BigMapGrid.RunStart, "开局登记在贫城西边的野地上。");

        world.Advance(ArenaTime.StepsPerGameHour);

        world.Voyage.Phase.ShouldBe(VoyagePhase.Holding, "没人下令就停在野地上。");
        world.Voyage.TargetCityId.ShouldBeNull("没有自动挑一座城。");
        world.Voyage.Position.ShouldBe(before,
            "撤掉开局航行令之后，推进一个游戏小时也不该挪动。");
        world.IsDocked.ShouldBeFalse("没进港，等玩家点「停靠」。");
    }

    [Fact]
    public void 玩家下过令之后_推进不会改掉目标城()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.Voyage.SailToCity(BigMapGrid.WildAnchor.Id);

        world.Advance(ArenaTime.StepsPerGameHour);

        world.Voyage.TargetCityId.ShouldBe(BigMapGrid.WildAnchor.Id,
            "目标城由玩家那一发令决定，没人会把它改回富城。");
    }

    [Fact]
    public void 出界那一发照常扣魂石_走到界外待满两个游戏分钟就扣()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        // 先开到富城再出界：出界目标是「圆心经兽当前位置向外」那个径向点，
        // 富城那一侧的径向点落在寻路网格之内（网格从 −58 米起 516 格，见 BigMapGrid）。
        world.Voyage.SailToCity(BigMapGrid.RichCity.Id);
        for (var i = 0; i < 200 && world.Voyage.Phase == VoyagePhase.Sailing; i++)
        {
            world.Advance(100);
        }
        world.Voyage.Phase.ShouldBe(VoyagePhase.Holding, "先停到富城城门外。");

        // 出界这一档验的是航行与扣魂石，不是饥饿：先把饱食度摆满，
        // 免得走到半路掉进低饱食减速那一档（基础衰减 4 点/小时后走不到目标）。
        world.Voyage.SetSatiation(ArenaTuning.SatiationMax);
        world.Voyage.SteerOutOfBounds();

        // **推进到「刚出界」之后再多走两个游戏分钟**：扣魂石算的是整游戏分钟那一档的差，
        // 跨过半径 256 米那一线的那一步还在同一个游戏分钟里（elapsed = 0），
        // 停在跨界那一格上读出来的是「还没扣」。魂石开局 30 点、界外待满 30 个游戏分钟
        // 才被送回内侧，两个游戏分钟离那条线还很远。
        var cap = ArenaTime.StepsPerGameHour * 12;
        for (var i = 0; i < cap && !BigMapGrid.IsOutOfBounds(world.Voyage.Position); i++)
        {
            world.Advance(1);
        }

        BigMapGrid.IsOutOfBounds(world.Voyage.Position).ShouldBeTrue(
            $"应当站到半径 256 米之外，实际停在 {world.Voyage.Position}");
        world.Advance(ArenaTime.StepsPerGameMinute * 2);

        world.Voyage.Phase.ShouldBe(VoyagePhase.Outbound,
            "出界的令还在，没有别的航行令把它顶掉。");
        world.Voyage.SoulStones.ShouldBeLessThan(ArenaTuning.StartingSoulStones,
            "出界期间魂石照扣 —— 没有自动航行把它顶回界内。");
    }

    [Theory]
    [InlineData(-56.0, 200.0)]   // 正西
    [InlineData(456.0, 200.0)]   // 正东
    [InlineData(200.0, -56.0)]   // 正南
    [InlineData(200.0, 456.0)]   // 正北
    public void 四个方向都出得去(double x, double z)
    {
        // 这一条是「寻路层加网格原点」那件事的验收：网格从 −58 米起 516 格，
        // 半径 257 那圈出界目标整圈落在网格外侧 —— 四个方向都出得去。
        // 网格只到 0..512 米那会儿，正西与正南的出界目标是落在网格外的那一半，
        // 求解器判「目标不可达」，兽出不去 —— 症状是「只有北/东能出界」。
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.MapStepper.ApplyMove(BeastVoyage.BeastId, new NavPoint(x, z), 0.0);

        // 出界这一档验的是航行与扣魂石，不是饥饿：先把饱食度摆满，
        // 免得走到半路掉进低饱食减速那一档（基础衰减 4 点/小时后走不到目标）。
        world.Voyage.SetSatiation(ArenaTuning.SatiationMax);
        world.Voyage.SteerOutOfBounds();

        var cap = ArenaTime.StepsPerGameHour * 12;
        for (var i = 0; i < cap && !BigMapGrid.IsOutOfBounds(world.Voyage.Position); i++)
        {
            world.Advance(1);
        }

        BigMapGrid.IsOutOfBounds(world.Voyage.Position).ShouldBeTrue(
            $"从 ({x}, {z}) 朝外那一发应当真的越过半径 256 米，实际停在 {world.Voyage.Position}");
        world.Voyage.Phase.ShouldBe(VoyagePhase.Outbound, "出界的令还在，兽没有自己掉头。");
    }

    [Fact]
    public void 饱食度按整游戏小时衰减四点_日结不再自动补()
    {
        var world = ArenaWorldBuilder.New(Seed).WithGranaryFood(500.0).Build();
        var before = world.Voyage.Satiation;

        world.Advance(ArenaTime.StepsPerGameHour);   // 一个游戏小时

        (before - world.Voyage.Satiation).ShouldBe(ArenaTuning.SatiationDecayPerGameHour, 1e-9,
            "一个游戏小时衰减 4 点。");
        world.Voyage.GranaryFood.ShouldBe(500.0, 1e-9, "日结自动喂食已删，粮仓不动。");
    }

    [Fact]
    public void 手动喂食五食物换一点饱食度()
    {
        var world = ArenaWorldBuilder.New(Seed).WithGranaryFood(100.0).Build();
        world.Voyage.SetSatiation(50.0);

        world.FeedBeast(25.0).ShouldBe(RejectReason.InvalidArgs, "喂进去了不算被拒。");

        world.Voyage.Satiation.ShouldBe(55.0, 1e-9, "25 食物换 5 点饱食度。");
        world.Voyage.GranaryFood.ShouldBe(75.0, "喂掉的从粮仓扣。");
        world.Town.Food.ShouldBe(475.0, 1e-9, "镇仓同样扣 25 份。");
    }

    [Fact]
    public void 饱食度低了移速打折_见底那一档是四成()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        world.Voyage.SetSatiation(80.0);
        world.Voyage.SpeedFactor().ShouldBe(1.0, "饱的不打折。");

        world.Voyage.SetSatiation(39.0);
        world.Voyage.SpeedFactor().ShouldBe(ArenaTuning.SlowSpeedFactor, "低于 40 乘 0.7。");

        world.Voyage.SetSatiation(9.0);
        world.Voyage.SpeedFactor().ShouldBe(ArenaTuning.StarvingSpeedFactor, "低于 10 乘 0.4。");

        ArenaTuning.StarvingSpeedFactor.ShouldBe(0.4, "保底 40% 移速，不饿死。");
    }

    [Fact]
    public void 脱队觅食_记原断点_吃到八十回原断点才恢复原目标城()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.Voyage.SailToCity(BigMapGrid.DesertCity.Id);
        world.Voyage.SetSatiation(20.0);   // 低于 30，且航线 30 米内有生态对象

        // 一路推进，记下它进脱队那一刻的原断点。
        NavPoint? waypoint = null;
        var sawDetour = false;
        var satiationAfterMeal = 0.0;
        // 预算按开局位置算：野地到食物点约 120 米，饿着走（0.7 倍速）要 4300 步，
        // 脱队之后再走到食物点、吃完返航还要 800 步上下。
        for (var i = 0; i < 800; i++)
        {
            var before = world.Voyage.Phase;
            world.Advance(10);
            if (world.Voyage.Phase == VoyagePhase.DetouringToFood && !sawDetour)
            {
                sawDetour = true;
                waypoint = world.Voyage.RouteWaypoint;
            }

            // 进食结束那一刻（Eating → Returning）读饱食度：那才是「吃完」的那一格。
            // 返航路上还在按小时饿，走完再读会读成 70 出头。
            if (before == VoyagePhase.Eating && world.Voyage.Phase == VoyagePhase.ReturningToWaypoint)
            {
                satiationAfterMeal = world.Voyage.Satiation;
            }

            if (sawDetour && world.Voyage.Phase == VoyagePhase.Sailing) break;
        }

        sawDetour.ShouldBeTrue("饿着且顺路，应当脱队觅食。");
        waypoint.ShouldNotBeNull("脱队那一刻要记下航线原断点。");
        satiationAfterMeal.ShouldBeGreaterThanOrEqualTo(ArenaTuning.SatiationAfterMeal,
            "吃到 80 才返航。");

        // 返航先回原断点：进入 Returning 那一刻的位置要贴着那个断点。
        var returned = false;
        for (var i = 0; i < 400 && !returned; i++)
        {
            world.Advance(10);
            if (world.Voyage.Phase == VoyagePhase.ReturningToWaypoint)
            {
                NavArrive.Reached(world.Voyage.Position, waypoint!.Value, ArenaTuning.ArriveToleranceMeters)
                    .ShouldBeTrue($"返航那一下得朝着航线原断点走，实际朝 {world.Voyage.Position}，断点 {waypoint}");
            }

            returned = world.Voyage.Phase == VoyagePhase.Sailing;
        }

        returned.ShouldBeTrue("吃完回到原断点之后应当恢复原目标城。");
        world.Voyage.TargetCityId.ShouldBe(BigMapGrid.DesertCity.Id, "恢复的是原目标城，不是新命令的城市。");
        world.Voyage.RouteWaypoint.ShouldBeNull("回到原断点之后断点这一格就交还了。");

        // 航线上那几件对象里至少吃掉了一件；吃完的那一件在再生窗口内不可吞。
        world.Ecology.Nodes.Count(n => !n.AvailableAt(world.Tick)).ShouldBeGreaterThan(0,
            "自主觅食确实把沿途对象吃掉了一件。");
    }

    [Fact]
    public void 饱腹时不脱队()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.Voyage.SailToCity(BigMapGrid.RichCity.Id);
        world.Voyage.SetSatiation(ArenaTuning.StartingSatiation);

        for (var i = 0; i < 60; i++)
        {
            world.Advance(10);
            world.Voyage.Phase.ShouldNotBe(VoyagePhase.DetouringToFood, "不饿就不脱队。");
        }
    }

    [Fact]
    public void 出界按游戏分钟扣魂石_扣到零送回圆盘内侧并回到停泊态()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        var before = world.Voyage.SoulStones;

        // 先停到富城（出界的径向目标要落在寻路网格内，见上面那条用例的注记）。
        world.Voyage.SailToCity(BigMapGrid.RichCity.Id);
        for (var i = 0; i < 200 && world.Voyage.Phase == VoyagePhase.Sailing; i++)
        {
            world.Advance(100);
        }

        // 出界这一档验的是航行与扣魂石，不是饥饿：先把饱食度摆满，
        // 免得走到半路掉进低饱食减速那一档（基础衰减 4 点/小时后走不到目标）。
        world.Voyage.SetSatiation(ArenaTuning.SatiationMax);
        world.Voyage.SteerOutOfBounds();
        world.Voyage.Phase.ShouldBe(VoyagePhase.Outbound, "下了出界令。");

        // 走到圆盘外就走不动了（魂石只扣、位置不回），所以分段推进盯着位置走。
        var wentOut = false;
        for (var i = 0; i < 400 && !wentOut; i++)
        {
            world.Advance(10);
            wentOut = BigMapGrid.IsOutOfBounds(world.Voyage.Position);
        }

        wentOut.ShouldBeTrue($"应当走到圆盘外，实际停在 {world.Voyage.Position}");
        (world.Voyage.SoulStones - before).ShouldBeLessThan(0.0, "出界期间魂石要掉。");

        // 一路推到魂石扣光。
        for (var i = 0; i < 400 && world.Voyage.SoulStones > 0.0; i++)
        {
            world.Advance(50);
        }

        world.Voyage.SoulStones.ShouldBe(0.0, "魂石扣到零。");
        world.Voyage.Phase.ShouldBe(VoyagePhase.Holding, "扣到零就送回圆盘内侧，回到停泊态。");
        world.Voyage.TargetCityId.ShouldBeNull("送回之后不再朝任何城走。");
        BigMapGrid.IsInsideEdge(world.Voyage.Position).ShouldBeTrue(
            $"送回点落在圆盘内侧，实际在 {world.Voyage.Position}");

        // 送回点是同一方向、半径 255.5 米那一点。
        RadiusOf(world.Voyage.Position).ShouldBe(BigMapGrid.SendBackRadiusMeters, 0.01,
            $"送回点落在半径 {BigMapGrid.SendBackRadiusMeters.ToString(System.Globalization.CultureInfo.InvariantCulture)} 米那一点。");

        // 送回之后不再继续扣（已经回到界内）。
        var settled = world.Voyage.SoulStones;
        world.Advance(500);
        world.Voyage.SoulStones.ShouldBe(settled, "送回之后不再扣魂石。");
    }

    [Fact]
    public void 送回之后还能再开出去_那一发不是终态()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        world.Voyage.SailToCity(BigMapGrid.RichCity.Id);
        for (var i = 0; i < 200 && world.Voyage.Phase == VoyagePhase.Sailing; i++)
        {
            world.Advance(100);
        }

        // 出界这一档验的是航行与扣魂石，不是饥饿：先把饱食度摆满，
        // 免得走到半路掉进低饱食减速那一档（基础衰减 4 点/小时后走不到目标）。
        world.Voyage.SetSatiation(ArenaTuning.SatiationMax);
        world.Voyage.SteerOutOfBounds();
        for (var i = 0; i < 400 && world.Voyage.SoulStones > 0.0; i++)
        {
            world.Advance(50);
        }

        world.Voyage.SoulStones.ShouldBe(0.0, "魂石扣到零并送回内侧。");
        world.Voyage.Phase.ShouldBe(VoyagePhase.Holding, "送回之后是停泊态。");

        // **送回不是终态**：玩家照样能再下航行令，兽照样能再开出去。
        // 这一条同样先把饱食度摆满：验的是「还能再开」，不是饿着会不会脱队。
        world.Voyage.SetSatiation(ArenaTuning.SatiationMax);
        world.Voyage.SailToCity(BigMapGrid.PoorCity.Id);
        world.Voyage.Phase.ShouldBe(VoyagePhase.Sailing, "送回之后照样能再开船。");
        world.Voyage.TargetCityId.ShouldBe(BigMapGrid.PoorCity.Id, "新的一发令记下来了。");

        for (var i = 0; i < 200 && world.Voyage.Phase != VoyagePhase.Holding; i++)
        {
            world.Advance(100);
        }

        world.Voyage.Phase.ShouldBe(VoyagePhase.Holding, "第二趟照样开得到贫城城门外。");
        NavArrive.Reached(world.Voyage.Position, BigMapGrid.PoorCity.StopPoint(BeastTier.Tier1), ArenaTuning.ArriveToleranceMeters)
            .ShouldBeTrue($"应当走回贫城城门外，实际停在 {world.Voyage.Position}");
    }

    [Fact]
    public void 扣魂石的口径是每游戏分钟两点()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        // 先停到富城：出界的径向目标要落在寻路网格内（见「出界那一发照常扣魂石」那条的注记）。
        world.Voyage.SailToCity(BigMapGrid.RichCity.Id);
        for (var i = 0; i < 200 && world.Voyage.Phase == VoyagePhase.Sailing; i++)
        {
            world.Advance(100);
        }

        // 出界这一档验的是航行与扣魂石，不是饥饿：先把饱食度摆满，
        // 免得走到半路掉进低饱食减速那一档（基础衰减 4 点/小时后走不到目标）。
        world.Voyage.SetSatiation(ArenaTuning.SatiationMax);
        world.Voyage.SteerOutOfBounds();

        // 分段推进到「已经在圆盘外」那一刻，从那儿起量，免得把走过去的路上那段算进来。
        var wentOut = false;
        for (var i = 0; i < 400 && !wentOut; i++)
        {
            world.Advance(10);
            wentOut = BigMapGrid.IsOutOfBounds(world.Voyage.Position);
        }

        wentOut.ShouldBeTrue("应当先走到圆盘外再开始扣。");

        var before = world.Voyage.SoulStones;
        var minuteMark = world.Tick / ArenaTime.StepsPerGameMinute;
        world.Advance(ArenaTime.StepsPerGameMinute * 5);   // 五个游戏分钟
        var minutes = (world.Tick / ArenaTime.StepsPerGameMinute) - minuteMark;

        (before - world.Voyage.SoulStones)
            .ShouldBe(ArenaTuning.SoulStoneDrainPerGameMinute * minutes, 1e-9,
                $"扣的量 = 每游戏分钟 {ArenaTuning.SoulStoneDrainPerGameMinute.ToString(System.Globalization.CultureInfo.InvariantCulture)} × 经过的 {minutes} 个游戏分钟。");
    }

    /// <summary>一点离圆心的距离（米）—— 圆盘判界用这一把尺子。</summary>
    private static double RadiusOf(NavPoint at)
        => System.Math.Sqrt(((at.X - BigMapGrid.DiskCenterX) * (at.X - BigMapGrid.DiskCenterX))
                             + ((at.Z - BigMapGrid.DiskCenterZ) * (at.Z - BigMapGrid.DiskCenterZ)));
}
