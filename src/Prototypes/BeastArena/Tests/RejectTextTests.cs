#nullable enable
using System.Globalization;
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 拒绝文案那一张 ticket：<b>前缀只一层</b>、<b>细节与缺口数字都上屏</b>、<b>每类拒绝有固定句式</b>。
///
/// <para><b>断言落在逻辑层那一串原因串上</b>：表现层（<c>Scripts/Ui/</c>）不进测试工程
/// （测试项目只编 <c>Scripts/Logic/</c>），横幅那一句由逻辑层拆好的两半拼出来 ——
/// 所以这里验的是「拆出来的那两半」：枚举那一段与细节那一段。
/// 上屏效果由 uitest 那一档验（横幅真文本在引擎里）。</para>
///
/// <para><b>句式固定</b>：做不到 + 原因 + 缺口数字。缺口那一段一律带
/// 「要多少 / 有多少 / 缺多少」三个数 —— 只写「差 20」玩家不知道要 50 份还是 200 份。</para>
/// </summary>
public class RejectTextTests
{
    private const int Seed = 7;

    /// <summary>细节那一段（括号里那一截）—— 横幅上接在短句后面的就是它。</summary>
    private static string DetailOf(string reason) => ArenaSetup.SplitRejectText(reason).Detail;

    [Fact]
    [Trait("测试类别", "快速测试")]
    public void 任取一条拒绝_前缀只有一层_细节带缺口数字()
    {
        // Arrange：造一条带缺口的拒绝（镇仓只剩 30 份，要喂 50 份）。
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.Town.TrySpendFood(world.Town.Food - 30.0).ShouldBeTrue("镇仓只剩 30 份。");

        // Act
        ArenaSetup.Inject(world, "Feed", "50", out _, out var why).ShouldBeFalse();

        // Assert：前缀只有一层（双前缀那一格不许再出现）。
        why.ShouldStartWith($"rejected: {RejectReason.InsufficientFood}", Case.Sensitive,
            "前缀一层，枚举名紧跟其后。");
        why.ShouldNotContain("rejected: rejected:", Case.Sensitive, "不许有两层前缀。");

        // 拆出来的那两半：枚举那一段与细节那一段（横幅上屏的就是这两半）。
        var (parsed, detail) = ArenaSetup.SplitRejectText(why);
        parsed.ShouldBe(RejectReason.InsufficientFood, "枚举那一段。");
        detail.ShouldNotContain("rejected:", Case.Sensitive, "细节里没有前缀。");
        detail.ShouldContain("要 50 份", Case.Sensitive, "细节带「要多少」。");
        detail.ShouldContain("有 30 份", Case.Sensitive, "细节带「有多少」。");
        detail.ShouldContain("缺 20 份", Case.Sensitive, "细节带「缺多少」。");
    }

    [Fact]
    [Trait("测试类别", "快速测试")]
    public void 拆串那一步_前缀剥到没有为止_双前缀也认得出枚举()
    {
        // Arrange：手拼一条双前缀（那一格是修掉之前的形状）。
        const string doubled = "rejected: rejected: FullCapacity（岗位已满：church 岗位要 1 个，有 0 个，缺 1 个）";

        // Act
        var (parsed, detail) = ArenaSetup.SplitRejectText(doubled);

        // Assert：两段前缀都剥掉，枚举与细节照旧分出来。
        parsed.ShouldBe(RejectReason.FullCapacity, "两段前缀都剥掉之后认得出枚举。");
        detail.ShouldBe("岗位已满：church 岗位要 1 个，有 0 个，缺 1 个", "细节那一段原样取出。");
    }

    [Fact]
    [Trait("测试类别", "快速测试")]
    public void 进阶_魂石不够_写清要多少有多少缺多少()
    {
        // Arrange：门槛备齐，只留魂石这一档。
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedDirect(BigMapGrid.PoorCity);
        world.Tier2Ready();
        world.Town.ProduceSpecialMaterial(200.0);
        world.Town.ProduceCoin(50000.0);
        world.Town.TrySpendSoulStone(world.Town.SoulStone - 30.0).ShouldBeTrue("魂石只剩 30 枚。");

        // Act
        ArenaSetup.Inject(world, "AdvanceTier", string.Empty, out _, out var why).ShouldBeFalse();

        // Assert
        why.ShouldStartWith($"rejected: {RejectReason.InsufficientSoulStone}", Case.Sensitive, "魂石不够那一档。");
        var detail = DetailOf(why);
        detail.ShouldContain("要 40 枚", Case.Sensitive, "要多少。");
        detail.ShouldContain("有 30 枚", Case.Sensitive, "有多少。");
        detail.ShouldContain("缺 10 枚", Case.Sensitive, "缺多少。");
    }

    [Fact]
    [Trait("测试类别", "快速测试")]
    public void 进阶_特殊材料不够_写清要多少有多少缺多少()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedDirect(BigMapGrid.PoorCity);
        world.Tier2Ready();
        world.Town.ProduceSoulStone(1000.0);
        world.Town.ProduceCoin(50000.0);
        world.Town.ProduceSpecialMaterial(100.0);
        world.Town.TrySpendSpecialMaterial(world.Town.SpecialMaterial - 4.0)
            .ShouldBeTrue("特殊材料只剩 4 份。");

        ArenaSetup.Inject(world, "AdvanceTier", string.Empty, out _, out var why).ShouldBeFalse();

        why.ShouldStartWith($"rejected: {RejectReason.InsufficientMaterial}", Case.Sensitive, "材料不够那一档。");
        var detail = DetailOf(why);
        detail.ShouldContain("要 10 份", Case.Sensitive, "要多少。");
        detail.ShouldContain("有 4 份", Case.Sensitive, "有多少。");
        detail.ShouldContain("缺 6 份", Case.Sensitive, "缺多少。");
    }

    [Fact]
    [Trait("测试类别", "快速测试")]
    public void 进阶_金币不够_写清要多少有多少缺多少()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedDirect(BigMapGrid.PoorCity);
        world.Tier2Ready();
        world.Town.ProduceSoulStone(1000.0);
        world.Town.ProduceSpecialMaterial(200.0);
        world.Town.TrySpendCoin(world.Town.Coin - 1500.0).ShouldBeTrue("金币只剩 1500 金。");

        ArenaSetup.Inject(world, "AdvanceTier", string.Empty, out _, out var why).ShouldBeFalse();

        why.ShouldStartWith($"rejected: {RejectReason.InsufficientCoin}", Case.Sensitive, "金币不够那一档。");
        var detail = DetailOf(why);
        detail.ShouldContain("要 2000 金", Case.Sensitive, "要多少。");
        detail.ShouldContain("有 1500 金", Case.Sensitive, "有多少。");
        detail.ShouldContain("缺 500 金", Case.Sensitive, "缺多少。");
    }

    [Fact]
    [Trait("测试类别", "快速测试")]
    public void 喂食_食物不够_写清差几份()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.Town.TrySpendFood(world.Town.Food - 30.0).ShouldBeTrue("镇仓只剩 30 份。");

        ArenaSetup.Inject(world, "Feed", "50", out _, out var why).ShouldBeFalse();

        why.ShouldStartWith($"rejected: {RejectReason.InsufficientFood}", Case.Sensitive, "食物不够那一档。");
        var detail = DetailOf(why);
        detail.ShouldContain("要 50 份", Case.Sensitive, "要多少。");
        detail.ShouldContain("有 30 份", Case.Sensitive, "有多少。");
        detail.ShouldContain("缺 20 份", Case.Sensitive, "缺多少。");
    }

    [Fact]
    [Trait("测试类别", "快速测试")]
    public void 法力_法力格不够_写清差几格()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        var man = world.Roster.Find("w1")!;
        man.State = UnitState.Wounded;
        for (var i = 0; i < TownAccounts.ManaCapacity; i++)
        {
            world.Town.TryHoldMana(TownAccounts.ManaHold.Stasis).ShouldBeTrue("法力格占住一格。");
        }

        ArenaSetup.Inject(world, "CastHeal", "w1", out _, out var why).ShouldBeFalse();

        why.ShouldStartWith($"rejected: {RejectReason.InsufficientMana}", Case.Sensitive, "法力不够那一档。");
        var detail = DetailOf(why);
        detail.ShouldContain("要 1 格", Case.Sensitive, "要多少。");
        detail.ShouldContain("有 0 格", Case.Sensitive, "有多少。");
        detail.ShouldContain("缺 1 格", Case.Sensitive, "缺多少。");
    }

    [Fact]
    [Trait("测试类别", "快速测试")]
    public void 派岗_岗位已满_写清差几个岗()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        ArenaSetup.Inject(world, "AssignPost", "w1|church", out _, out var why1).ShouldBeTrue(why1);
        ArenaSetup.Inject(world, "AssignPost", "w2|church", out _, out var why2).ShouldBeTrue(why2);
        world.Roster.PostedCount(ArenaWorld.ChurchId).ShouldBe(2, "教堂两个岗位先占满。");

        ArenaSetup.Inject(world, "AssignPost", "w3|church", out _, out var why).ShouldBeFalse();

        why.ShouldStartWith($"rejected: {RejectReason.FullCapacity}", Case.Sensitive, "岗位满那一档。");
        var detail = DetailOf(why);
        detail.ShouldContain("岗位已满", Case.Sensitive, "写清是岗位满。");
        detail.ShouldContain("要 1 个", Case.Sensitive, "要多少。");
        detail.ShouldContain("有 0 个", Case.Sensitive, "有多少。");
        detail.ShouldContain("缺 1 个", Case.Sensitive, "缺多少。");
    }

    [Fact]
    [Trait("测试类别", "快速测试")]
    public void 进阶_门槛未到_写清差哪几个象限差几层()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedDirect(BigMapGrid.PoorCity);
        world.Town.ProduceSoulStone(1000.0);
        world.Town.ProduceSpecialMaterial(200.0);
        world.Town.ProduceCoin(50000.0);
        world.Soil.IsDug(0, ArenaTuning.Tier2GateLayer).ShouldBeFalse("还没挖到第二层。");

        ArenaSetup.Inject(world, "AdvanceTier", string.Empty, out _, out var why).ShouldBeFalse();

        why.ShouldStartWith($"rejected: {RejectReason.GateNotMet}", Case.Sensitive, "门槛没到那一档。");
        var detail = DetailOf(why);
        detail.ShouldContain("象限 0 挖到第 2 层", Case.Sensitive, "写清哪一处洞穴层没到。");
    }

    [Fact]
    [Trait("测试类别", "快速测试")]
    public void 停靠_正被通缉_写清谁在通缉欠多少()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.OnDocked(BigMapGrid.PoorCity);
        world.Town.AccrueDebt(ArenaWorld.PoorFactionId, ArenaTuning.WantedDebtThreshold + 100.0);
        world.AdvanceTo(((world.Tick / ArenaTime.StepsPerGameDay) + 1) * ArenaTime.StepsPerGameDay);
        world.Wanted.ShouldBeTrue("欠债过阈值即通缉。");

        ArenaSetup.Inject(world, "Dock", BigMapGrid.PoorCity.Id, out _, out var why).ShouldBeFalse();

        why.ShouldStartWith($"rejected: {RejectReason.WantedRefused}", Case.Sensitive, "通缉拒停那一档。");
        var detail = DetailOf(why);
        detail.ShouldContain("贫城行会", Case.Sensitive, "写清谁在通缉。");
        detail.ShouldContain("欠 3100 金", Case.Sensitive, "写清欠多少。");
    }

    [Fact]
    [Trait("测试类别", "快速测试")]
    public void 空艇_不在成交窗口内_写清这一段还剩几艘()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();

        ArenaSetup.Inject(world, "TradeAirship", "0|0|0", out _, out var why).ShouldBeFalse();

        why.ShouldStartWith($"rejected: {RejectReason.InvalidPhase}", Case.Sensitive, "窗口外那一档。");
        var detail = DetailOf(why);
        detail.ShouldContain("空艇", Case.Sensitive, "写清是空艇那一档。");
        detail.ShouldContain("还剩 0 艘", Case.Sensitive, "写清这一段还剩几艘。");
    }

    [Fact]
    [Trait("测试类别", "快速测试")]
    public void 复活_尸体过期_写清在册几小时与过期几小时()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.RegisterChurchCorpse("corpse1", "w1", world.Tick);
        world.AdvanceTo(world.Tick + (ArenaTime.StepsPerGameHour * (ArenaTuning.ReviveWindowGameHours + 2)));

        ArenaSetup.Inject(world, "ReviveCorpse", "corpse1", out _, out var why).ShouldBeFalse();

        why.ShouldStartWith($"rejected: {RejectReason.InvalidTarget}", Case.Sensitive, "过期那一档。");
        var detail = DetailOf(why);
        detail.ShouldContain("上限 24 游戏小时", Case.Sensitive, "写清窗口多长。");
        detail.ShouldContain("过期 2 小时", Case.Sensitive, "写清过期了几小时。");
    }

    [Fact]
    [Trait("测试类别", "快速测试")]
    public void 发薪超市价_忠心每天加两点_低于市价那一支当前走不到()
    {
        // Arrange：发清今天那一份工钱（开局的默认镇：8 工作人员 + 3 角斗士 + 1 自由人，
        // 人均实发 26/12 ≈ 2.17 金）。
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedDirect(BigMapGrid.PoorCity);
        var worker = world.Roster.Find("w1")!;
        var gladiator = world.Roster.Find("g1")!;
        var free = world.Roster.Find("f1")!;
        worker.Loyalty = 50.0;
        gladiator.Loyalty = 50.0;
        free.Loyalty = 50.0;
        var workerBefore = worker.Loyalty;
        var gladiatorBefore = gladiator.Loyalty;
        var freeBefore = free.Loyalty;

        ArenaSetup.Inject(world, "PayWages", string.Empty, out _, out var why).ShouldBeTrue(why);

        // Act：走到下一个整游戏日 —— 那一格日结按实发算忠心。
        world.AdvanceTo(((world.Tick / ArenaTime.StepsPerGameDay) + 1) * ArenaTime.StepsPerGameDay);

        // Assert：市价按角色取该角色的日薪（工作人员 2 / 自由人 1 / 角斗士 3）。
        worker.Loyalty.ShouldBe(workerBefore + ArenaTuning.LoyaltyDailyAbove, 1e-9,
            "工作人员市价 2，人均实发高于它 → 忠心 +2/日。");
        free.Loyalty.ShouldBe(freeBefore + ArenaTuning.LoyaltyDailyAbove, 1e-9,
            "自由人市价 1，人均实发高于它 → 忠心 +2/日。");
        gladiator.Loyalty.ShouldBe(gladiatorBefore, 1e-9,
            "角斗士市价 3，人均实发低于它，但差额没到 −3 那一档 → 不动（那一档是留着的休眠规则）。");
    }

    [Fact]
    [Trait("测试类别", "快速测试")]
    public void 市价按角色取日薪_三档都在数值表里()
    {
        ArenaTuning.WorkerDailyWage.ShouldBe(2.0, 1e-9, "工作人员日薪 2 金。");
        ArenaTuning.GladiatorDailyWage.ShouldBe(3.0, 1e-9, "角斗士日薪 3 金。");
        ArenaTuning.FreePersonDailyWage.ShouldBe(1.0, 1e-9, "自由人日薪 1 金。");
    }

    [Fact]
    [Trait("测试类别", "快速测试")]
    public void 快照头部带手动目标点_没有手动目标时写横杠()
    {
        // Arrange：开一局手动航行（玩家右键点地图空地那一发）。
        var world = ArenaWorldBuilder.New(Seed).Build();
        ArenaSetup.Inject(world, "SailTo", "210.5|300.25", out _, out var why).ShouldBeTrue(why);

        // Act
        var header = world.DebugSnapshot().Split(" | ")[0];

        // Assert：手动目标点进快照头部（调试桥读得出兽要去哪）。
        header.ShouldContain("manual=210.5,300.3", Case.Sensitive, "手动目标点带着世界米坐标。");
        header.ShouldEndWith("debts=", Case.Sensitive, "头部段的尾巴仍是 debts= 那一格。");
    }

    [Fact]
    [Trait("测试类别", "快速测试")]
    public void 快照头部没有手动目标时写横杠()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        var header = world.DebugSnapshot().Split(" | ")[0];

        header.ShouldContain("manual=-", Case.Sensitive, "没有手动目标就写横杠（键在、值空）。");
    }

    [Fact]
    [Trait("测试类别", "快速测试")]
    public void 喂食容量_三处里最少的那一份_饱食度满了就是零()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        ArenaSetup.FeedCapacity(world).ShouldBe(100.0, 1e-9,
            "开局镇仓 500、兽粮 500、饱食余量 100 → 容量是 100。");

        world.Voyage.SetSatiation(ArenaTuning.SatiationMax);
        ArenaSetup.FeedCapacity(world).ShouldBe(0.0, 1e-9, "饱食度满了还能吃 0 份。");
    }

    [Fact]
    [Trait("测试类别", "快速测试")]
    public void 喂食_饱食度满了也喂不进去_一格不扣()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.Voyage.SetSatiation(ArenaTuning.SatiationMax);
        var foodBefore = world.Town.Food;
        var granaryBefore = world.Voyage.GranaryFood;

        ArenaSetup.Inject(world, "Feed", "50", out _, out var why).ShouldBeFalse();

        why.ShouldStartWith($"rejected: {RejectReason.InsufficientFood}", Case.Sensitive, "喂不进去那一档。");
        world.Town.Food.ShouldBe(foodBefore, 1e-9, "镇仓一格不扣。");
        world.Voyage.GranaryFood.ShouldBe(granaryBefore, 1e-9, "兽粮一格不扣。");
        world.Voyage.Satiation.ShouldBe(ArenaTuning.SatiationMax, 1e-9, "饱食度不动。");
    }

    [Fact]
    [Trait("测试类别", "快速测试")]
    public void 拒绝原因枚举仍是十四项_没有因为文案加档()
    {
        System.Enum.GetValues<RejectReason>().Length.ShouldBe(14, "拒绝原因就 14 项（文案不新建错误码）。");
    }
}
