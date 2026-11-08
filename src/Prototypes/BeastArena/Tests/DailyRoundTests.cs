#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DemoFactory.GugaFramework.Pathfinding;
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 镇民的日常走位 —— 空甲板上他们在镇上的例行去处（上工 / 吃饭 / 训练 / 休息那一档），
/// 以及 <see cref="ArenaUnit.Moving"/> 那一列读数。纯 C# 逻辑直接推进，不起引擎。
///
/// <para><b>这一族要钉的是三件事</b>：① 人真的走出去了（坐标变了，不只是发了令）；
/// ② 择点落在某一栋的门口上（去的是「饭店」不是「随便一个格子」）；
/// ③ 同种子两跑逐字相同（「择点不掷点」那一档，重放要的就是这个）。</para>
///
/// <para><b>为什么挑一个没有战斗 / 搬运 / 游客的世界来验</b>：那三处在场时日常走位整发不发
/// （移动权归它们自己），在一个正打着仗的世界上看到「人没动」读不出是哪一档在管。
/// 所以这里只造「空甲板」这一种局面，让这一格只量日常走位自己。</para>
/// </summary>
public class DailyRoundTests
{
    private const int Seed = 7;

    /// <summary>
    /// 三档去处各自落在哪一栋门前的那一小片空地上（无岗位那一档的两选一 + 角斗士那一档）。
    ///
    /// <para><b>量的是「门前那片空地」不是「门口那一格」</b>：门口格只站得下一个人，
    /// 日常走位把全镇民发往四栋建筑，抢同一格的结果是一个也到不了
    /// （那正是本族要防的那一种空转）。一栋楼门前本来就有十几格可站，
    /// 落点按名单序号在 5×5 邻域里错开（<c>ArenaWorld.DoorSpreadDeckOf</c>），
    /// 这一格要验的「去处是某一栋」仍然成立 —— 它与「去的是随便一个格子」分得开。</para>
    /// </summary>
    private static List<NavPoint> DoorsOf(ArenaWorld world)
    {
        var all = new List<NavPoint>();
        foreach (var id in new[]
                 {
                     ArenaWorld.RestaurantId, ArenaWorld.DormitoryId, ArenaWorld.ArenaId,
                 })
        {
            // 固定 id 只说明「哪一张蓝图」；宿舍 / 竞技场可能是玩家新建的 bN，走真实解析。
            var resolved = world.ResolveBuildingId(id);
            if (resolved.Length == 0) continue;

            for (var spread = 0; spread < 25; spread++)
            {
                var at = world.DoorSpreadDeckOf(resolved, spread);
                if (!all.Contains(at)) all.Add(at);
            }
        }

        return all;
    }

    [Fact]
    public void 开局的镇民站在落点表那一格上_一步没走()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        // 这一格是「改之前」的样子：造好那一刻全镇民都站在落点表给的那一格上，
        // 走过去要等第一个日常档（整游戏小时那一格）。
        world.Roster.Townspeople.ShouldAllBe(u =>
            new NavPoint(u.DeckX, u.DeckZ).DistanceTo(
                world.DoorDeckOf(ArenaWorld.RestaurantId)) > 1.0,
            "造好那一刻还没发过任何日常令，人站在自己的落点上。");
    }

    [Fact]
    public void 空甲板上镇民自己走起来_一个半游戏小时之后大半站在某一栋门口()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        // 宿舍不在开局六栋里，而「去休息」那一档以它为去处 —— 这一局先把宿舍摆上，
        // 让日常走位这一格只量走位本身（否则一半人根本没有去处，量到的是缺建筑）。
        // 不递固定 id：玩家新建的是 bN，解析必须走「固定 id → 蓝图 id → 在册建筑」那一口。
        world.Deck.PlaceBuilding("Dormitory", -8, 3).Ok.ShouldBeTrue("宿舍在旧锚点上放得下。");

        var before = world.Units.ToDictionary(u => u.Id, u => new NavPoint(u.DeckX, u.DeckZ));

        // 因开局人口 50→12：错开量 12 让发令时刻集中在一个游戏小时的末段，
        // 所以多给半游戏小时，才量得到「走到门口」而不是「刚发令」。
        world.Advance(ArenaTime.StepsPerGameHour + (ArenaTime.StepsPerGameHour / 2));

        var town = world.Roster.Townspeople.ToList();
        town.ShouldNotBeEmpty("开局 12 名镇民在册。");

        var moved = town.Count(u =>
            new NavPoint(u.DeckX, u.DeckZ).DistanceTo(before[u.Id]) > 0.5);

        // 分成两句量：**开局不在门口的真的走出去了**，**走完之后大半在某一栋门口**。
        var doors = DoorsOf(world);
        var walkers = town.Count(u =>
            Nearest(before[u.Id], doors) > ArenaTuning.DailyArriveToleranceMeters);
        moved.ShouldBeGreaterThan(0,
            $"开局不在门口的那几位得真的走出去 —— 实际走了 {moved}/{town.Count} 个，"
            + $"开局不在门口的 {walkers} 个。只发令不挪人，那一格验的是假走位。");
        moved.ShouldBeGreaterThanOrEqualTo(walkers,
            $"开局不在门口的人应当全部动起来 —— 实际走了 {moved} 个，不在门口的 {walkers} 个。");

        // 去处是某一栋门前，不是随便一格：三档各落在饭店 / 宿舍 / 竞技场三处门前之一。
        var atDoor = town.Count(u => Nearest(u, doors) <= ArenaTuning.DailyArriveToleranceMeters);
        atDoor.ShouldBeGreaterThan(town.Count / 2,
            $"走完之后大半镇民得聚在某一栋门前 —— 实际 {atDoor}/{town.Count} 个。"
            + "择点那一档读的是「去哪一栋」，不是「往哪走」。");
    }

    [Fact]
    public void 择点不掷点_同种子两跑逐字相同()
    {
        var first = ArenaWorldBuilder.New(Seed).Build();
        var second = ArenaWorldBuilder.New(Seed).Build();

        first.Advance(ArenaTime.StepsPerGameHour);
        second.Advance(ArenaTime.StepsPerGameHour);

        // 活物段那一栏含每个人的坐标与走动标志：逐字相等 = 去处与走位都逐字相同。
        first.Roster.DebugSegments().ShouldBe(second.Roster.DebugSegments(),
            "同种子两跑跑出了不同的日常走位 —— 择点那一档漏了不掷点的派生，重放会分叉。");

        first.DeckStepper.ReplaySnapshotSegment().ShouldBe(second.DeckStepper.ReplaySnapshotSegment(),
            "甲板上那一段寻位重放段也要逐字相同。");
    }

    [Fact]
    public void 换个种子_日常走位跑出的是另一处()
    {
        var a = ArenaWorldBuilder.New(Seed).Build();
        var b = ArenaWorldBuilder.New(Seed + 1).Build();

        a.Advance(ArenaTime.StepsPerGameHour);
        b.Advance(ArenaTime.StepsPerGameHour);

        // 反面哨兵：不换种子也相等的话，上面那条可能只是「所有人去同一个地方」。
        // 换一颗种子要换出「谁去吃饭、谁去歇」这一层差别。
        a.Roster.DebugSegments().ShouldNotBe(b.Roster.DebugSegments(),
            "换种子没有换出差别 —— 「同种子两跑相等」就可能只是「压根没被种子牵着走」。");
    }

    [Fact]
    public void 走动标志_走着的那一步为真_站定了就落回假()
    {
        // 因开局人口 50→12：错开量 12 让第一位在第 600 步发令、最后一位在第 732 步发令；
        // 这一格只要读到「刚发令那一批正在走」，所以推进到一个游戏小时再加 2 个游戏分钟。
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.Deck.PlaceBuilding("Dormitory", -8, 3).Ok.ShouldBeTrue("宿舍在旧锚点上放得下。");

        world.Advance(ArenaTime.StepsPerGameHour + (ArenaTime.StepsPerGameMinute * 2));
        var walking = world.Roster.Townspeople.Count(u => u.Moving);
        walking.ShouldBeGreaterThan(0,
            "发完令那一批人正在走过去 —— 站着的人不该被报成在走动（动作层据此播不播走路）。");

        // 走到位之后：到达容差内寻路层不再产位移，那一列就落回假。
        //
        // **判据是「谁到了谁就不动」，不是「所有人都不动」**：全镇 12 个人分去四栋建筑门前，
        // 出发时刻按名单序号错开，路程也有远有近，到达本来就有先后 ——
        // 「全镇此刻恰好一个都不在走」量的是时刻的巧合，不是这一列的语义。
        // 这一格要钉的那一句是：**已经站在落点上的人，不再被报成在走动**。
        world.Advance((ArenaTime.StepsPerGameHour * 3) + (ArenaTime.StepsPerGameHour / 2));

        var doors = DoorsOf(world);
        var town = world.Roster.Townspeople.ToList();
        var settled = town.Where(u => Nearest(u, doors) <= ArenaTuning.DailyArriveToleranceMeters).ToList();
        settled.Count.ShouldBeGreaterThan(town.Count / 2,
            $"走了三个游戏小时的半程之后，大半镇民该已经站在某一栋门前 —— 实际 {settled.Count}/{town.Count} 个。");
        settled.ShouldAllBe(u => !u.Moving,
            "已经站在门前落点上的人不再被报成在走动 —— "
            + "「在不在走动」量的是本步有没有真走出去，站在原地的人不产位移。");
    }

    [Fact]
    public void 走动标志_在读数里读得出()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.Advance(30);

        var segment = world.Roster.Find("w1")!.DebugSegment();
        segment.Contains(" moving=", StringComparison.Ordinal)
            .ShouldBeTrue("活物那一段要带上走动标志（表现层动作那一档靠它）：" + segment);
    }

    [Fact]
    public void 拆掉固定宿舍后新建bN宿舍_自由人日常去处解析到它而不是回落()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.Construction.Find(ArenaWorld.DormitoryId).ShouldBeNull("开局六栋里没有宿舍。");

        // 玩家新建的宿舍拿的是 bN，不是固定 id —— 日常走位递进来的仍是固定 id「dormitory」，
        // 所以解析必须先翻成蓝图 id「Dormitory」再按蓝图找（A3 那一条）。
        var placed = world.Deck.PlaceBuilding("Dormitory", -8, 3);
        placed.Ok.ShouldBeTrue($"宿舍放得下：{placed.Reason}");
        var dormId = placed.BuildingId;
        dormId.ShouldNotBe(ArenaWorld.DormitoryId, "新建的是 bN，不是固定 id。");

        world.ResolveBuildingId(ArenaWorld.DormitoryId).ShouldBe(dormId,
            "固定 id 要先翻成蓝图 id，再找到新建的那一栋 bN。");

        // 挑一个本档抽到「回宿舍」的自由人（draw ≥ 吃饭份额那一档），把发令步号对准他。
        var free = world.Roster.Units.First(u =>
            u.Role == UnitRole.FreePerson
            && PeopleRoster.DailyDraw(world.Seed, u.RosterIndex, 0) >= ArenaTuning.DailyMealShare);
        var tick = (ArenaTime.StepsPerGameMinute * ArenaTuning.DailyRoundGameMinutes)
            - (free.RosterIndex * ArenaTuning.DailyRoundStaggerSteps);

        // 递的是真实世界里那一口（固定 id → 解析 → 门前空地），不是测试自己复刻的规则。
        var at = world.DoorSpreadDeckOrNull(ArenaWorld.DormitoryId, free.RosterIndex);
        at.ShouldNotBeNull("固定 id 解析到 bN 宿舍之后，门前落点必须拿得到；拿不到就会走兜底。");

        var usedFallback = false;
        world.Roster.StepDailyRound(tick, deckBusy: false,
            world.DoorSpreadDeckOrNull,
            u =>
            {
                usedFallback = true;
                return world.NearestFreeDeckCell(u.DeckX, u.DeckZ);
            });

        usedFallback.ShouldBeFalse("选了回宿舍的人不该落到「最近可通行格」兜底上 —— 那正是 A3 的 bug。");
    }

    private static double Nearest(ArenaUnit u, IReadOnlyList<NavPoint> doors)
        => Nearest(new NavPoint(u.DeckX, u.DeckZ), doors);

    private static double Nearest(NavPoint at, IReadOnlyList<NavPoint> doors)
    {
        var best = double.MaxValue;
        foreach (var d in doors)
        {
            var distance = at.DistanceTo(d);
            if (distance < best) best = distance;
        }

        return best;
    }
}
