#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 明星角斗士留名账与「招募没人应募」那一格的原因措辞。
///
/// <para><b>留名账归世界实例所有</b>：同一进程里造两台 <see cref="ArenaWorld"/>，
/// 两台的成交记录互不相干（进程全局的那一份静态表会让这一条无从验证），
/// 而且这一本要进检查点快照 —— 断言比的就是那份快照。</para>
///
/// <para><b>定长与掉条数</b>：账留最近 <see cref="ArenaWorld.StarSaleLedgerSize"/> 笔，
/// 掉的笔数由快照头部 <c>sales=</c> 与 <c>issued=</c> 之差摆在明面上。</para>
///
/// <para><b>招募没中不是期别不对</b>：spec §5.1 把 <see cref="RejectReason"/> 钉死在封闭枚举里
/// （2026-10-20 起 12 项），
/// 里面没有「这一轮没人应募」那一档，所以掷点没落进应募率时原因串不进
/// <c>rejected: &lt;RejectReason&gt;</c> 那一行。</para>
/// </summary>
public class StarSaleLedgerTests
{
    private const int Seed = 7;

    /// <summary>把个人名声抬过明星门槛那一档（那一栏是公开可写的那一口）。</summary>
    private static void MakeStar(ArenaWorld world, string personId)
    {
        var u = world.Roster.Find(personId);
        u.ShouldNotBeNull($"{personId} 该在册。");
        u!.Role.ShouldBe(UnitRole.Gladiator, $"{personId} 要先是在册角斗士。");
        u.PersonalReputation = ArenaTuning.StarSellReputationFloor + 10.0;
    }

    [Fact]
    public void 留名账长在世界上_两台世界各记各的()
    {
        var a = ArenaWorldBuilder.New(Seed).Build();
        var b = ArenaWorldBuilder.New(Seed).Build();

        MakeStar(a, "g3");
        var coinBefore = a.Town.Coin;
        ArenaSetup.Inject(a, "SellStar", "g3", out _, out var why).ShouldBeTrue(why);

        a.StarSales.Count.ShouldBe(1, "成交那一笔落在卖的那台世界上。");
        a.StarSaleIssued.ShouldBe(1);
        a.StarSales[0].PersonId.ShouldBe("g3");
        a.StarSales[0].Gold.ShouldBe(a.Town.Coin - coinBefore, 1e-9,
            "留名记的是成交价那一格，与金币账上多出来的那一截相等。");

        b.StarSales.ShouldBeEmpty("另一台世界没有参与这一笔，那本账在它身上是空的。");
        b.StarSaleIssued.ShouldBe(0, "累计笔数也归世界实例，不是进程全局的计数。");
        b.DebugSnapshot().ShouldContain("STAR sales=0 issued=0", Case.Sensitive,
            "没成交过的那台世界快照上那一段是零。");
    }

    [Fact]
    public void 成交那一笔进检查点快照_逐字读得出人名步号与成交价()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);
        var tickAtSale = world.Tick;

        MakeStar(world, "g3");
        ArenaSetup.Inject(world, "SellStar", "g3", out _, out var why).ShouldBeTrue(why);

        var segments = world.DebugSnapshot().Split(" | ");
        segments.ShouldContain("STAR sales=1 issued=1", "留名账那一段在快照里。");
        segments.ShouldContain(s => s.StartsWith(
            $"SALE g3 tick={tickAtSale.ToString(CultureInfo.InvariantCulture)} gold=",
            StringComparison.Ordinal));

        // 定长的那一对读数：留着的条数与累计笔数分开摆。
        world.StarSaleCount.ShouldBe(1);
        world.StarSaleIssued.ShouldBe(1);
        world.Town.IsConserved().ShouldBeTrue("卖角斗士那一格八条等式仍然要平：" + world.Town.ConservedReport());
    }

    [Fact]
    public void 留名账定长_成交超过定额时旧账先掉出去_掉了几条摆在那两个读数上()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        // 开局 3 名角斗士 + 1 名常驻自由人转身份 = 4 个可卖标的；定长账是 16 条，
        // 所以这一局从册子那一口另发 14 名角斗士，凑够顶穿定额的笔数。
        foreach (var u in world.Roster.Units.Where(u => u.Role == UnitRole.FreePerson).ToList())
        {
            ArenaSetup.Inject(world, "ConvertRole", $"{u.Id}|Gladiator", out _, out var why).ShouldBeTrue(why);
        }

        for (var i = 1; i <= 14; i++)
        {
            world.Roster.Add($"s{i}", UnitRole.Gladiator);
        }

        var stars = world.Roster.Units
            .Where(u => u.Role == UnitRole.Gladiator)
            .Select(u => u.Id)
            .ToList();
        stars.Count.ShouldBeGreaterThan(ArenaWorld.StarSaleLedgerSize,
            "要顶穿定额，卖的笔数得比定额多。");

        foreach (var id in stars)
        {
            MakeStar(world, id);
            ArenaSetup.Inject(world, "SellStar", id, out _, out var why).ShouldBeTrue(why);
        }

        world.StarSaleIssued.ShouldBe(stars.Count, "一生成交几笔全在累计那一栏。");
        world.StarSales.Count.ShouldBe(ArenaWorld.StarSaleLedgerSize, "留名账停在定额那一档。");
        world.StarSales[^1].PersonId.ShouldBe(stars[^1], "掉的是最老那几笔，最新那一笔还在。");

        var segments = world.DebugSnapshot().Split(" | ");
        segments.ShouldContain(
            $"STAR sales={ArenaWorld.StarSaleLedgerSize} issued={stars.Count}",
            "定长那一对读数把掉了几条摆在明面上。");
        segments.Count(s => s.StartsWith("SALE ", StringComparison.Ordinal))
            .ShouldBe(ArenaWorld.StarSaleLedgerSize, "快照里那一段也是定长的。");
        world.Town.IsConserved().ShouldBeTrue("连卖 18 笔之后八条等式仍然要平：" + world.Town.ConservedReport());
    }

    [Fact]
    public void InvalidPhase那一档仍只报期别不对_招人这一格没有被改写()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.IsDocked.ShouldBeFalse("开局未停靠。");

        var ok = ArenaSetup.Inject(world, "Recruit", "TownOrCountry", out _, out var why);

        ok.ShouldBeFalse("不在停靠期招不到人。");
        why.ShouldStartWith($"rejected: {RejectReason.InvalidPhase}", Case.Sensitive,
            "不在停靠期仍报 InvalidPhase —— 那一档没有被挪去报额度或掷点结果。");
    }
}
