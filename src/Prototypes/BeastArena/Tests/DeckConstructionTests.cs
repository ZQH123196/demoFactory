#nullable enable
using System.Linq;
using DemoFactory.GugaFramework.Construction;
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 甲板二十四格网格建造：十三件蓝图、开局 6 栋默认建筑、法术停滞拔起与落位、
/// 施工 3 人启停、备件修房。
/// </summary>
public class DeckConstructionTests
{
    private const int Seed = 7;

    [Fact]
    public void 建造规则是二十四格两米一格()
    {
        var rule = DeckBlueprints.BuildRule(ArenaTuning.DeckCellsTier1);

        rule.CellSize.ShouldBe(ArenaTuning.DeckCellMeters, "格长跟寻路那张网同一份。");
        rule.CellSize.ShouldBe(2.0, "2 米一格：人 1.7 米是锚，建筑不该比人矮。");
        rule.MinCol.ShouldBe(-12, "列下界 -12。");
        rule.MinRow.ShouldBe(-12, "行下界 -12。");
        rule.MaxCol.ShouldBe(11, "列上界 11。");
        rule.MaxRow.ShouldBe(11, "行上界 11。");
        rule.Kind.ShouldBe(FootprintKind.Cells, "网格放置吃格占地。");
        (rule.MaxCol - rule.MinCol + 1).ShouldBe(24, "24 格见方。");

        // 尺子钉死：格数没动，每格从 1 米变成 2 米，甲板占地从 24 米见方变成 48 米见方。
        ArenaTuning.DeckSpanMeters(ArenaTuning.DeckCellsTier1).ShouldBe(48.0, "一阶甲板边长 48 米。");
        DeckBlueprints.CellSize.ShouldBe(ArenaTuning.DeckCellMeters,
            "建造格长与寻路格长同源 —— 漂了就是「建造按 2 米、寻路按 1 米」的错位。");
    }

    [Fact]
    public void 十三件蓝图_占地造价耐久岗位与工期逐字对上()
    {
        var blueprints = DeckBlueprints.Build();

        blueprints.Count.ShouldBe(13, "十三件建筑。");
        blueprints.Select(b => b.Id).ShouldBe(DeckBlueprints.Specs.Select(s => s.Id), "蓝图 id 与规格表同序。");

        foreach (var s in DeckBlueprints.Specs)
        {
            var bp = blueprints.Single(b => b.Id == s.Id);

            // 占地：4×4 / 3×3 / 2×2 三档。
            var footprint = bp.Footprint.ShouldBeOfType<CellFootprint>();
            footprint.Width.ShouldBe(s.Width, $"{s.Id} 占 {s.Width} 格宽。");
            footprint.Height.ShouldBe(s.Height, $"{s.Id} 占 {s.Height} 格高。");
            footprint.Offsets.Count.ShouldBe(s.Width * s.Height, $"{s.Id} 占 {s.Width * s.Height} 格。");

            // 耐久：镇政府 200，其余 100。
            bp.MaxHealth.ShouldBe(s.MaxDurability, $"{s.Id} 耐久上限 {s.MaxDurability}。");

            // 工期是 1 个游戏日。
            bp.BuildDurationTicks.ShouldBe(ArenaTime.StepsPerGameDay, $"{s.Id} 工期 1 个游戏日。");

            DeckBlueprints.SpecOf(s.Id)!.Posts.ShouldBe(s.Posts, $"{s.Id} 岗位数 {s.Posts}。");
        }

        DeckBlueprints.SpecOf("TownHall")!.MaxDurability.ShouldBe(200.0, "镇政府耐久 200。");
        DeckBlueprints.Specs.Where(s => s.Id != "TownHall")
            .ShouldAllBe(s => s.MaxDurability == 100.0, "其余十二件耐久都是 100。");
    }

    [Fact]
    public void 造价按占地分三档_四四八百三三五百二二三百()
    {
        foreach (var s in DeckBlueprints.Specs)
        {
            var expected = (s.Width, s.Height) switch
            {
                (4, 4) => 800,
                (3, 3) => 500,
                _ => 300,
            };
            s.CoinCost.ShouldBe(expected, $"{s.Id} 是 {s.Width}×{s.Height}，造价 {expected} 金币。");
        }
    }

    [Fact]
    public void 开局六栋落在各自锚点上_其余七类留空()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        // playable-spec 定的开局六栋：镇政府、竞技场、粮仓、饭店、教堂、训练场。
        // 登记次序沿用旧表（锚点不重排），只把酒店、宿舍、传送阵那三行摘掉。
        var expected = new[]
        {
            ("town_hall", "TownHall"),
            ("arena", "Arena"),
            ("training_ground", "TrainingGround"),
            ("church", "Church"),
            ("restaurant", "Restaurant"),
            ("granary", "Granary"),
        };

        world.Construction.Count.ShouldBe(6, "开局 6 栋默认建筑。");
        world.Construction.Ids.ShouldBe(DeckBlueprints.StartingFixtures.Select(f => f.BuildingId),
            "登记次序就是那张锚点表的次序。");
        world.Construction.Ids.ShouldBe(expected.Select(e => e.Item1),
            "蓝图 id 与 playable-spec 的开局建筑清单一致。");

        foreach (var (buildingId, blueprintId, col, row) in DeckBlueprints.StartingFixtures)
        {
            var building = world.Construction.Find(buildingId);
            building.ShouldNotBeNull($"{buildingId} 要在册。");
            building!.BlueprintId.ShouldBe(blueprintId, $"{buildingId} 的蓝图。");
            building.Anchor.ShouldBe(new GridCell(col, row), $"{buildingId} 落在 ({col},{row})。");
            building.State.ShouldBe(BuildState.Completed, $"{buildingId} 是完工态登记的。");
        }

        // 其余七类开局留空，由局内 PlaceBuilding 造。
        foreach (var empty in new[]
                 {
                     "Stable", "ArcheryRange", "Workshop", "AirshipDock",
                     "Hotel", "Dormitory", "TeleportArray",
                 })
        {
            world.Construction.Blueprints.ContainsKey(empty).ShouldBeTrue($"{empty} 在蓝图表里。");
            world.Construction.Ids.ShouldNotContain(
                world.Construction.Ids.FirstOrDefault(i => i == empty) ?? empty,
                $"{empty} 开局不落座。");
        }
    }

    [Fact]
    public void 开局六栋_耐久与岗位从规格表同一份读()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        var hall = world.Construction.Find("town_hall")!;
        hall.MaxHealth.ShouldBe(200.0, "镇政府耐久上限 200。");
        hall.Cells.Count.ShouldBe(16, "镇政府占 4×4 = 16 格。");

        var restaurant = world.Construction.Find("restaurant")!;
        restaurant.Cells.Count.ShouldBe(9, "饭店占 3×3 = 9 格。");
        DeckBlueprints.SpecOf("Restaurant")!.Posts.ShouldBe(2, "饭店 2 个岗位。");

        var granary = world.Construction.Find("granary")!;
        granary.Cells.Count.ShouldBe(4, "粮仓占 2×2 = 4 格。");
    }

    [Fact]
    public void 十三件都能放_占地与造价逐字对上()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        // 全部摆在开局 6 栋没占的那几片空处：4×4 两件在西南角，3×3 七件在行 6，2×2 四件在行 9。
        var fourByFour = new[] { new GridCell(-12, -12), new GridCell(-8, -12) };
        var threeByThree = new[]
        {
            new GridCell(-12, 6), new GridCell(-9, 6), new GridCell(-6, 6), new GridCell(-3, 6),
            new GridCell(0, 6), new GridCell(3, 6), new GridCell(6, 6), new GridCell(9, 6),
        };
        var twoByTwo = new[]
        {
            new GridCell(-12, 9), new GridCell(-10, 9), new GridCell(-8, 9), new GridCell(-6, 9),
        };

        var three = 0;
        var two = 0;
        var placed = 0;

        foreach (var s in DeckBlueprints.Specs)
        {
            var anchor = (s.Width, s.Height) switch
            {
                (4, 4) => fourByFour[placed++],
                (3, 3) => threeByThree[three++],
                _ => twoByTwo[two++],
            };

            var result = world.Deck.PlaceBuilding(s.Id, anchor.Col, anchor.Row);
            result.Ok.ShouldBeTrue(
                $"{s.Id}（{s.Width}×{s.Height}）应当放得下在 ({anchor.Col},{anchor.Row})，实际 {result.Reason}。");

            var building = world.Construction.Find(result.BuildingId)!;
            building.Cells.Count.ShouldBe(s.Width * s.Height, $"{s.Id} 占 {s.Width * s.Height} 格。");
            building.MaxHealth.ShouldBe(s.MaxDurability, $"{s.Id} 耐久。");
            building.Anchor.ShouldBe(anchor, $"{s.Id} 落在指定锚点。");
        }

        placed.ShouldBe(2, "两件 4×4 都放过。");
        three.ShouldBe(7, "七件 3×3 都放过。");
        two.ShouldBe(4, "四件 2×2 都放过。");
        world.Construction.Count.ShouldBe(19, "开局 6 栋 + 新放 13 件 = 19 栋。");
    }

    [Fact]
    public void 落座即暂停_派满三个工人恢复_掉到两个再暂停()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        // 训练场 3 个岗位 → 阈值 3（阈值 = min(3, max(1, 岗位数))）。
        var placed = world.Deck.PlaceBuilding("TrainingGround", 6, 6);
        placed.Ok.ShouldBeTrue("训练场放得下。");
        var id = placed.BuildingId;

        var building = world.Construction.Find(id)!;
        building.Paused.ShouldBeTrue("落座后立刻 PauseBuild。");
        building.State.ShouldBe(BuildState.Building, "还在建。");

        world.Deck.SetBuildWorkers(id, 3);
        world.Construction.Find(id)!.Paused.ShouldBeFalse("派满 3 名工人就 ResumeBuild。");

        world.Deck.SetBuildWorkers(id, 2);
        world.Construction.Find(id)!.Paused.ShouldBeTrue("掉到 2 人就再 PauseBuild。");

        world.Deck.SetBuildWorkers(id, 3);
        world.Construction.Find(id)!.Paused.ShouldBeFalse("再派满又恢复。");

        world.Deck.Workers[id].ShouldBe(3, "岗位人数记着。");
    }

    [Fact]
    public void 施工满一个游戏日就完工()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        var id = world.Deck.PlaceBuilding("Granary", 6, 6).BuildingId;

        // 施工岗位人数归镇民岗位表管，世界每一步按岗位表刷一次 —— 所以派的是真人，不是旋钮。
        foreach (var personId in new[] { "w1", "w2", "w3" })
        {
            world.Roster.AssignPost(world.Roster.Find(personId)!, id);
        }

        world.Advance(ArenaTime.StepsPerGameHour);
        world.Construction.Find(id)!.State.ShouldBe(BuildState.Building, "一个游戏小时还没建完。");

        world.Advance(ArenaTime.StepsPerGameHour * 23);
        world.Construction.Find(id)!.State.ShouldBe(BuildState.Completed, "满一个游戏日就完工。");
    }

    [Fact]
    public void 酒店在两名工人到岗后能完工()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        // 酒店 2 个岗位 → 阈值 min(3, max(1, 2)) = 2。
        var id = world.Deck.PlaceBuilding("Hotel", 6, 6).BuildingId;

        world.Roster.AssignPost(world.Roster.Find("w1")!, id);
        world.Advance(1);
        world.Construction.Find(id)!.Paused.ShouldBeTrue("2 岗的楼只到 1 个人，不开工。");

        world.Roster.AssignPost(world.Roster.Find("w2")!, id);
        world.Advance(ArenaTime.StepsPerGameDay);

        world.Construction.Find(id)!.State.ShouldBe(BuildState.Completed,
            "2 名工人到岗，满一个游戏日就完工（阈值不是恒 3）。");
    }

    [Fact]
    public void 零岗建筑不会卡在施工()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        // 宿舍 0 个岗位：落座即开工，不靠派工。
        var id = world.Deck.PlaceBuilding("Dormitory", 6, 6).BuildingId;
        world.Construction.Find(id)!.Paused.ShouldBeFalse("0 岗建筑不靠岗位施工，落座即开工。");

        world.Advance(ArenaTime.StepsPerGameDay);
        world.Construction.Find(id)!.State.ShouldBe(BuildState.Completed, "一个游戏日后完工。");
    }

    [Fact]
    public void 按蓝图查找_先找固定id_拆掉后回退到新建的bN()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        world.FindByBlueprint("Restaurant").ShouldBe("restaurant", "固定 id 在册时优先它。");
        world.FindByBlueprint("Hotel").ShouldBeEmpty("开局六栋里没有酒店。");

        var hotelId = world.Deck.PlaceBuilding("Hotel", 6, 6).BuildingId;
        world.FindByBlueprint("Hotel").ShouldBe(hotelId, "没有固定 id 的蓝图按蓝图找到新建的 bN。");

        var restaurantId = world.Deck.PlaceBuilding("Restaurant", -12, 6).BuildingId;
        ArenaSetup.Inject(world, "RemoveBuilding", "restaurant", out _, out var why).ShouldBeTrue(why);

        world.FindByBlueprint("Restaurant").ShouldBe(restaurantId,
            "固定 id 那一栋拆掉之后回退到同蓝图的新建那一栋。");
    }

    [Fact]
    public void 修理缺备件与没人在岗各报一条原因()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DamageBuilding("church", 40.0);

        world.CanRepair("church").ShouldBeFalse("岗位上没人，修不了。");
        world.RepairBlockerOf("church").ShouldBe(RepairBlocker.NoWorker);

        ArenaSetup.Inject(world, "RepairBuilding", "church", out _, out var why).ShouldBeFalse();
        why.ShouldContain("没人在岗", customMessage: "横幅要写明是没人在岗。");
        world.Town.SparePart.ShouldBe(ArenaTuning.StartingSpareParts, "被拒不扣备件。");

        ArenaSetup.Inject(world, "AssignPost", "w1|church", out _, out var postWhy).ShouldBeTrue(postWhy);
        world.CanRepair("church").ShouldBeTrue("耐久未满、备件够、人在岗。");

        world.Town.TrySpendSparePart(world.Town.SparePart).ShouldBeTrue("把备件花光。");
        world.RepairBlockerOf("church").ShouldBe(RepairBlocker.NoSparePart);

        ArenaSetup.Inject(world, "RepairBuilding", "church", out _, out why).ShouldBeFalse();
        why.ShouldContain("缺备件", customMessage: "横幅要写明是缺备件。");
    }

    [Fact]
    public void 新建酒店能被游客选中为目的地_拆除后游客不再去()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        var hotelId = world.Deck.PlaceBuilding("Hotel", 6, 6).BuildingId;

        ArenaSetup.Inject(world, "Dock", BigMapGrid.PoorCity.Id, out _, out var why).ShouldBeTrue(why);
        for (var i = 0; i < 8000 && !world.IsDocked; i++) world.Advance(1);
        world.IsDocked.ShouldBeTrue("停到贫城上。");

        var targeted = false;
        for (var i = 0; i < 90 && !targeted; i++)
        {
            world.Advance(ArenaTime.StepsPerGameMinute);
            targeted = world.Visitors.InTown.Any(v => v.TargetBuildingId == hotelId);
        }

        targeted.ShouldBeTrue("游客会把新建的 bN 酒店选中为目的地。");

        ArenaSetup.Inject(world, "RemoveBuilding", hotelId, out _, out why).ShouldBeTrue(why);

        // 先让桌上那几位吃完（30 个游戏分钟的用餐）再验：此后一位也不许再指向那一栋。
        world.Advance(ArenaTime.StepsPerGameMinute * (ArenaTuning.VisitorEatGameMinutes + 10));

        var stale = false;
        for (var i = 0; i < 60; i++)
        {
            world.Advance(ArenaTime.StepsPerGameMinute);
            if (world.Visitors.InTown.Any(v => v.TargetBuildingId == hotelId)) stale = true;
        }

        stale.ShouldBeFalse("拆除后游客不再以那一栋为目的地。");
    }

    [Fact]
    public void 拔起占一个法力格_落位归还法力格并迁移建筑()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        var manaBefore = world.Deck.ManaAvailable;
        var restaurant = world.Construction.Find("restaurant")!;
        var anchorBefore = restaurant.Anchor!.Value;

        var lifted = world.Deck.LiftBuilding("restaurant");
        lifted.Ok.ShouldBeTrue("拔起应当成功。");

        world.Deck.ManaUsed(ManaOccupancy.Stasis).ShouldBe(1, "拔起占住 1 个法力格。");
        world.Deck.ManaAvailable.ShouldBe(manaBefore - 1, "可用法力少一格。");
        world.Construction.Find("restaurant")!.Anchor.ShouldBe(anchorBefore, "悬空态在建造模块里保持原位。");

        var dropped = world.Deck.DropBuilding("restaurant", 8, 6);
        dropped.Ok.ShouldBeTrue($"落位应当成功（{dropped.Reason}）。");

        world.Deck.ManaUsed(ManaOccupancy.Stasis).ShouldBe(0, "落位归还 1 个法力格。");
        world.Deck.ManaAvailable.ShouldBe(manaBefore, "可用法力回到原样。");
        world.Construction.Find("restaurant")!.Anchor.ShouldBe(new GridCell(8, 6), "建筑迁到新锚点。");
    }

    [Fact]
    public void 放回自己那几格_成空动作_还法力格且那几格腾得出来()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        var manaBefore = world.Deck.ManaAvailable;
        var anchorBefore = world.Construction.Find("restaurant")!.Anchor!.Value;
        var cellsBefore = world.Construction.Find("restaurant")!.Cells;

        world.Deck.LiftBuilding("restaurant").Ok.ShouldBeTrue("拔起应当成功。");
        world.Deck.ManaUsed(ManaOccupancy.Stasis).ShouldBe(1, "拔起占住 1 个法力格。");

        // 放回原来那个锚点：拔起没有释放那几格，所以这一发按普通占用检查看是「格被占着」。
        var dropped = world.Deck.DropBuilding("restaurant", anchorBefore.Col, anchorBefore.Row);

        dropped.Ok.ShouldBeTrue($"放回原处应当成功（{dropped.Reason}）。");
        world.Deck.ManaUsed(ManaOccupancy.Stasis).ShouldBe(0, "放回原处归还 1 个法力格。");
        world.Deck.ManaAvailable.ShouldBe(manaBefore, "可用法力回到原样。");
        world.Deck.Lifted.ShouldNotContain("restaurant", "不再悬空。");

        var restaurant = world.Construction.Find("restaurant");
        restaurant.ShouldNotBeNull("建筑还在册上。");
        restaurant!.Anchor.ShouldBe(anchorBefore, "锚点一寸没动。");
        restaurant.Cells.ShouldBe(cellsBefore, "占的格一寸没动。");

        // 那一格没有被子这一栋永久蹲住：等它挪走，同一片地方立刻能放别的建筑。
        world.Deck.LiftBuilding("restaurant").Ok.ShouldBeTrue("再拔一次。");
        world.Deck.DropBuilding("restaurant", 8, 6).Ok.ShouldBeTrue("挪到别处。");

        var other = world.Deck.PlaceBuilding("Granary", anchorBefore.Col, anchorBefore.Row);
        other.Ok.ShouldBeTrue($"原处应当放得下别的建筑（{other.Reason}）。");
        world.Construction.Find(other.BuildingId)!.Anchor.ShouldBe(anchorBefore, "新建筑落在同一锚点。");
        world.Construction.Find("restaurant")!.Anchor.ShouldBe(new GridCell(8, 6), "饭店已经挪走。");
    }

    [Fact]
    public void 落位压着别人的格_照旧拒_挪不动那一栋()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        var anchorBefore = world.Construction.Find("restaurant")!.Anchor!.Value;

        world.Deck.LiftBuilding("restaurant").Ok.ShouldBeTrue("拔起。");

        // 镇政府锚点 (-2,-2)、4×4 —— 那是另一栋占着的格，不是自己那几格。
        var blocked = world.Deck.DropBuilding("restaurant", -2, -2);

        blocked.Ok.ShouldBeFalse("挪到别人头上要拒。");
        blocked.Reason.ShouldBe(PlacementReason.Occupied, "拒因是那几格被别人占着。");
        world.Deck.ManaUsed(ManaOccupancy.Stasis).ShouldBe(1, "拒了这一发法力格还占着。");
        world.Construction.Find("restaurant")!.Anchor.ShouldBe(anchorBefore, "位置一寸不动。");
        world.Deck.Lifted.ShouldContain("restaurant", "还在悬空态。");
    }

    [Fact]
    public void 拔起两次_第二次读数是无效状态()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        world.Deck.LiftBuilding("granary").Ok.ShouldBeTrue("第一次拔起成功。");
        world.Deck.LiftBuilding("granary").Ok.ShouldBeFalse("已经在悬空态，不能再拔一次。");
        world.Deck.ManaUsed(ManaOccupancy.Stasis).ShouldBe(1, "重复拔起不重复占法力。");
    }

    [Fact]
    public void 修房耗一个备件加一个工人_十个游戏分钟恢复五十耐久()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        var id = "church";

        // 教堂上限 100：砸到 40 留出 50 的余量，才量得出「加 50」而不是「加到上限」。
        world.Construction.Damage(id, 60.0).Ok.ShouldBeTrue("先砸掉一点耐久。");
        world.Construction.Find(id)!.Health.ShouldBe(40.0, "100 减 60。");

        var partsBefore = world.Deck.SpareParts;
        var started = world.Deck.RepairBuilding(id, world.Tick);
        started.Ok.ShouldBeTrue("备件与人都在岗，修房应当发得出去。");

        world.Deck.SpareParts.ShouldBe(partsBefore - ArenaTuning.RepairSpareParts, "耗掉 1 个备件。");
        world.Deck.IsRepairing(id).ShouldBeTrue("工已经在册。");

        world.Advance(ArenaTime.StepsPerGameMinute * (ArenaTuning.RepairGameMinutes - 1));
        world.Construction.Find(id)!.Health.ShouldBe(40.0, "还没到 10 个游戏分钟，不加耐久。");

        world.Advance(ArenaTime.StepsPerGameMinute * 2);
        world.Construction.Find(id)!.Health.ShouldBe(90.0, "10 个游戏分钟后 +50 耐久（40 + 50）。");
        world.Deck.IsRepairing(id).ShouldBeFalse("工结掉了。");
    }

    [Fact]
    public void 修房钳到该建筑的上限()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        world.Construction.Damage("church", 10.0);   // 100 → 90
        world.Deck.RepairBuilding("church", world.Tick).Ok.ShouldBeTrue("发得出去。");
        world.Advance(ArenaTime.StepsPerGameMinute * (ArenaTuning.RepairGameMinutes + 1));

        world.Construction.Find("church")!.Health.ShouldBe(100.0,
            "90 + 50 超过上限 100，钳到上限。");
    }

    [Fact]
    public void 备件不够或者没人_修房发不出去且不扣备件()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.Construction.Damage("church", 40.0);

        var noWorker = world.Deck.RepairBuilding("church", world.Tick, workerOnDuty: false);
        noWorker.Ok.ShouldBeFalse("没人在岗就修不了。");
        world.Deck.SpareParts.ShouldBe(ArenaTuning.StartingSpareParts, "发不出去不扣备件。");

        // 把备件扣光。
        for (var i = 0; i < ArenaTuning.StartingSpareParts; i++)
        {
            world.Construction.Damage("church", 5.0);
            world.Deck.RepairBuilding("church", world.Tick).Ok.ShouldBeTrue("第一次还备件够。");
            world.Advance(ArenaTime.StepsPerGameMinute * (ArenaTuning.RepairGameMinutes + 1));
        }

        var noParts = world.Deck.RepairBuilding("church", world.Tick);
        noParts.Ok.ShouldBeFalse("备件扣光了修不了。");
    }

    [Fact]
    public void 满耐久的房子修不动()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        var full = world.Deck.RepairBuilding("granary", world.Tick);
        full.Ok.ShouldBeFalse("满耐久没东西可修。");
    }

    [Fact]
    public void 每栋建筑南侧那一格是门口点()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        // 镇政府锚点 (-2,-2)、4×4 → 南侧那一格是行 2，列落在占地正中（-2 + 4/2 = 0）。
        var door = world.Deck.DoorPointOf("town_hall");
        var expected = DeckBlueprints.CandidateOf(new GridCell(-2 + (4 / 2), -2 + 4));
        door.ShouldBe(expected, "南侧门口点在占地正下方一格。");

        // 「门口是哪一格」这条关系没变；变的是那一格折成几米：格心 = (格号 + 0.5) × 格长，
        // 格长从 1 米变成 2 米之后 (0, 2) 这一格的格心从 (0.5, 2.5) 挪到 (1, 5)。
        var doorCell = DeckBlueprints.DoorCellOf("TownHall", new GridCell(-2, -2));
        doorCell.Col.ShouldBe(0, "门口格仍在占地正中的那一列。");
        doorCell.Row.ShouldBe(2, "门口格仍在占地正下方那一行。");
        expected.ShouldBe(new System.Numerics.Vector2(1f, 5f), "格心坐标 (1, 5)（2 米一格）。");
    }

    [Fact]
    public void 门口点出界时顺次找最近的可通行邻格()
    {
        // 贴最东南放：2×2 占 (10..11, 10..11)，南侧那格是行 12 → 出界，要往旁边挪。
        var door = DeckBlueprints.DoorCellOf("Granary", new GridCell(10, 10), DeckBlueprints.IsInTier1Bounds);

        DeckBlueprints.IsInTier1Bounds(door).ShouldBeTrue("门口点落在甲板内。");
        door.Row.ShouldBe(11, "往回收到甲板内那一行。");
        door.Col.ShouldBeInRange(9, 11, "落在占地左右那一侧。");

        // 正常摆位时，南侧那一格就是门口点（列落在占地正中：-8 + 2/2 = -7）。
        var normal = DeckBlueprints.DoorCellOf("Granary", new GridCell(-8, 3));
        normal.ShouldBe(new GridCell(-7, 5), "锚点 (-8,3) 的 2×2 南侧那格是 (-7,5)。");
    }

    [Fact]
    public void 建筑阻挡落到甲板那张网格上()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.Advance(1);   // 推一步让地形接线 Pump 一次，把开局 6 栋的阻挡落进网格

        // 地形接线接的是第一个登记的空间（甲板），6 栋落座后阻挡应当已经落进甲板网格。
        world.Terrain.Missed.ShouldBe(0L, "地形接线一格都没落下就是接线错了空间。");
        world.Terrain.StampedCount.ShouldBeGreaterThan(0L, "开局 6 栋应当落过阻挡。");

        var church = world.Construction.Find("church")!;
        var cell = church.Cells[0];
        world.DeckGrid.IsBlocked(DeckBlueprints.ToNavCell(cell)).ShouldBeTrue(
            $"教堂占的格 {cell} 在甲板网格上应当是阻挡。");

        // 大地图那张网格上不该有这批阻挡（巨兽要能在上面走）。
        world.MapGrid.IsBlocked(DeckBlueprints.ToNavCell(cell)).ShouldBeFalse(
            "甲板的建筑阻挡不落在大地图那张网格上。");
    }

    [Fact]
    public void 挪动建筑_旧格撤阻挡新格封上()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.Deck.LiftBuilding("granary").Ok.ShouldBeTrue("拔起。");

        var oldCell = world.Construction.Find("granary")!.Cells[0];
        world.Deck.DropBuilding("granary", -6, 6).Ok.ShouldBeTrue("落位。");

        world.Terrain.Pump();
        world.Terrain.Missed.ShouldBe(0L, "挪动那一发要能撤旧格、封新格。");

        var newCell = world.Construction.Find("granary")!.Cells[0];
        newCell.ShouldNotBe(oldCell, "落到了新格。");
        world.DeckGrid.IsBlocked(DeckBlueprints.ToNavCell(oldCell)).ShouldBeFalse("旧格撤阻挡。");
        world.DeckGrid.IsBlocked(DeckBlueprints.ToNavCell(newCell)).ShouldBeTrue("新格封上。");
    }
}
