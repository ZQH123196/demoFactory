#nullable enable
using System.Linq;
using System.Numerics;
using DemoFactory.Prototypes.InfinityLord.Logic;
using Shouldly;
using Xunit;

namespace InfinityLord.Tests;

/// <summary>
/// 战力感知那一条（切片 4「养英雄」的回报机制）—— <b>同一面旗、同一名英雄，只因战力涨了就改判</b>。
///
/// <para>公式住在一处：<c>LogicWorld.PerceivedThreat</c>（感知威胁 = 基础威胁 × 基准战力 / 自身战力，
/// 战力 = 攻击×2 + 血上限/5 + 装备×3）与 <c>LogicWorld.TryClaimFlag</c>（赏金 / 感知威胁 ≥ 阈值才接）。
/// 所以升级与买装备都会让同一面旗显得更轻。</para>
///
/// <para>这条命题与「职业分化」是两件事：那一条（<c>GuildUpgradeTests.同一面硬单</c>）把全场写回卡面基准、
/// 只留职业差；这一条职业不变、只动一名的战力。两条各有各的用例，别互相顶替。</para>
///
/// <para><b>场景只推到第 2 波大地精落场，且不建任何行会</b>：场上因此只有种子行会产的游侠
/// （同一职业、同一勇气阈值），全场写回卡面基准之后谁都不够勇；再把要观察的那一名养到 lv.6 ⇒ 只有它改判。
/// 养大那一档的数值不是编的：整局长跑里实测到过同样的形状（lv.6 游侠 150 血、战力 46）。</para>
/// </summary>
public class ThreatPerceptionTests : LogicWorldTestBase
{
    /// <summary>大地精的基础威胁 = 攻击 14×2 + 血上限 120/5 = 52（与波次表同源）。</summary>
    private const float OgreThreat = 52f;

    /// <summary>硬单定价：卡面基准游侠的阈值价 = 52 × 1.2 = 62.4 —— 55 在基准态不够，养大之后够。</summary>
    private const int HardPrice = 55;

    /// <summary>没有第三方插手的观察点（远离镇子、巢穴与波次入侵点）。</summary>
    private static readonly Vector2 QuietSpot = new(-15, 42);

    [Fact]
    public void 同一面旗同一名英雄_只因战力涨了就改判()
    {
        Advance(1801); // 第 2 波大地精落场

        Heroes().All(h => h.Class == HeroClass.Ranger).ShouldBeTrue(
            "排布前提：这一条不建行会 ⇒ 场上全是游侠（同一职业与同一勇气阈值）");

        var ogre = Monsters().First(m => m.Name == "大地精");
        foreach (var hero in Heroes())
        {
            PinToCardBaseline(hero);
        }

        var subject = Heroes().First();
        World.PlaceUnitForTest(subject.Id, QuietSpot).ShouldBeTrue("排布前提：观察对象挪到隔离点上");

        // 基准态：战力 28 = 基准战力 ⇒ 感知系数 1 ⇒ appeal = 55/52 = 1.06 < 1.2（不够勇）
        LogicWorld.PerceivedThreat(subject, OgreThreat).ShouldBe(OgreThreat, 0.01f,
            "卡面基准游侠（战力 28）感知系数 1 ⇒ 感知威胁就是基础威胁");
        World.PlaceBountyFlag(ogre.Id, HardPrice).ShouldBeTrue("排布前提：硬单挂出");
        Advance(1);

        subject.ClaimedFlagId.ShouldBeNull("第 1 步：基准态算不过来 → 拒");
        Heroes().Count(h => h.ClaimedFlagId is not null).ShouldBe(0, "全场同一档 ⇒ 谁都拒，旗仍待接");

        // 养大：只动这一名（lv.6 ⇒ 血上限 60 + 15×6 = 150，战力 16 + 30 = 46）。
        // 职业、那一面旗的价与目标一概不动 —— 改判只可能来自战力这一格。
        subject.Level = 6;
        subject.MaxHealth = 150;
        subject.Health = 150;

        LogicWorld.PerceivedThreat(subject, OgreThreat).ShouldBe(31.65f, 0.1f,
            "战力 46 ⇒ 感知系数 28/46 = 0.61 ⇒ 同一面旗在它眼里只剩 31.65");
        (HardPrice / (double)LogicWorld.PerceivedThreat(subject, OgreThreat)).ShouldBeGreaterThan(
            (double)LogicWorld.BraveryRatio, "appeal = 55/31.65 = 1.74 ≥ 1.2 ⇒ 这一发算得过来了");

        Advance(1);

        subject.ClaimedFlagId.ShouldNotBeNull(
            "同一面旗、同一名英雄：只因战力从 28 涨到 46，就由拒改判为接");
    }
}
