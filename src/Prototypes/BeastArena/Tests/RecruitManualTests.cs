#nullable enable
using DemoFactory.Prototypes.BeastArena.Logic;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 招募那一格：<b>纯手动、付钱必得</b>，每停靠一次最多
/// <see cref="ArenaTuning.RecruitPerDock"/> 人，停靠抵达那一刻清零。
///
/// <para>钉的是三件事：① 同一颗种子连招两次都成（不再掷应募率，也不再走「no applicants」
/// 那一句）；② 三条拒绝各是各的原因（额度用完 / 岗位已满 / 金币不足），
/// 按这个顺序判；③ 重新停靠之后额度回满。</para>
/// </summary>
public class RecruitManualTests
{
    private const int Seed = 7;

    /// <summary>一个航段的步数上限（与 <c>LogicSubsystemTests</c> 同一口径，留两成余量）。</summary>
    private const int VoyageSteps = ArenaTime.StepsPerGameHour * 12;

    private static void Fire(ArenaWorld world, string verb, string args)
    {
        ArenaSetup.Inject(world, verb, args, out _, out var why)
            .ShouldBeTrue($"{verb}({args}) 该走成写路径，被拒了：{why}");
    }

    private static string Reject(ArenaWorld world, string verb, string args)
    {
        ArenaSetup.Inject(world, verb, args, out _, out var why).ShouldBeFalse($"{verb}({args}) 该判否。");
        return why;
    }

    [Fact]
    public void 手动招募付钱必得()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);

        var coinBefore = world.Town.Coin;
        var peopleBefore = world.Roster.Units.Count;

        Fire(world, "Recruit", "TownOrCountry");
        Fire(world, "Recruit", "TownOrCountry");

        world.Roster.Units.Count.ShouldBe(peopleBefore + 2, "同一颗种子连招两次都该招到人。");
        world.Town.Coin.ShouldBe(coinBefore - 200.0, 1e-9, "两发城乡招各扣 100 金。");
        world.RecruitLimitLeft.ShouldBe(ArenaTuning.RecruitPerDock - 2, "两次成功各占一格额度。");
        world.DebugSnapshot().ShouldContain(
            $"RECRUIT left={ArenaTuning.RecruitPerDock - 2} used=2 per_dock={ArenaTuning.RecruitPerDock}",
            Case.Sensitive, "余量要进检查点快照。");
        world.Town.IsConserved().ShouldBeTrue(world.Town.ConservedReport());
    }

    [Fact]
    public void 招募超额度_岗位满_钱不够各返回对应拒绝()
    {
        // ① 额度用完：免费来路连招三次，第四次按额度那一档拒。
        var overQuota = ArenaWorldBuilder.New(Seed).Build();
        overQuota.DockedAt(BigMapGrid.PoorCity.Id);
        for (var i = 0; i < ArenaTuning.RecruitPerDock; i++) Fire(overQuota, "Recruit", "CatchVagrant");

        overQuota.RecruitLimitLeft.ShouldBe(0, "三个人用完这一停的额度。");
        var quotaReject = Reject(overQuota, "Recruit", "CatchVagrant");
        quotaReject.ShouldContain(
            "本次停靠已招满", Case.Sensitive, "额度那一档要报「本次停靠已招满」。");
        quotaReject.ShouldContain(
            $"要 {ArenaTuning.RecruitPerDock} 个，有 0 个，缺 {ArenaTuning.RecruitPerDock} 个",
            Case.Sensitive, "额度那一档要报上限、余量与缺口三个数。");

        // ② 岗位已满：把工人补到上限，额度还剩着也招不下。
        var postFull = ArenaWorldBuilder.New(Seed).Build();
        postFull.DockedAt(BigMapGrid.PoorCity.Id);
        var workers = postFull.Roster.WorkerCount;
        for (var i = workers; i < ArenaTuning.WorkerPostCap; i++)
            postFull.Roster.Add($"cap{i}", UnitRole.Worker);
        postFull.Roster.WorkerCount.ShouldBe(ArenaTuning.WorkerPostCap);

        Reject(postFull, "Recruit", "CatchVagrant").ShouldContain(
            $"岗位已满：工作人员岗位要 {ArenaTuning.WorkerPostCap} 个，有 0 个，缺 {ArenaTuning.WorkerPostCap} 个",
            Case.Sensitive, "岗位那一档要报岗位上限与缺口。");

        // ③ 金币不足：把金币花光，额度与岗位都还够。
        var broke = ArenaWorldBuilder.New(Seed).Build();
        broke.DockedAt(BigMapGrid.PoorCity.Id);
        broke.Town.TrySpendCoin(broke.Town.Coin).ShouldBeTrue("把开局的金钱花光。");

        var townCost = ArenaSetup.RecruitCost(RecruitSource.TownOrCountry);
        Reject(broke, "Recruit", "TownOrCountry").ShouldContain(
            $"要 {townCost} 金，有 0 金，缺 {townCost} 金",
            Case.Sensitive, "金币那一档要报要多少钱、有多少、缺多少。");
        broke.Town.Coin.ShouldBe(0.0, 1e-9, "被拒那一发不扣钱。");
    }

    [Fact]
    public void 重新停靠后招募额度恢复()
    {
        var world = ArenaWorldBuilder.New(Seed).Build();
        world.DockedAt(BigMapGrid.PoorCity.Id);

        for (var i = 0; i < ArenaTuning.RecruitPerDock; i++) Fire(world, "Recruit", "CatchVagrant");
        world.RecruitLimitLeft.ShouldBe(0);

        Fire(world, "PullAnchor", string.Empty);
        world.IsDocked.ShouldBeFalse("拔锚之后离开这一座城。");

        world.DockedAt(BigMapGrid.PoorCity.Id);
        world.RecruitLimitLeft.ShouldBe(ArenaTuning.RecruitPerDock, "重新停靠抵达那一刻额度清零。");

        Fire(world, "Recruit", "CatchVagrant");
        world.RecruitLimitLeft.ShouldBe(ArenaTuning.RecruitPerDock - 1, "恢复之后第一发照常招得到。");
    }
}
