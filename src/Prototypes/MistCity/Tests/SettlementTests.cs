#nullable enable
using System.Linq;
using DemoFactory.Prototypes.MistCity.Logic;
using Shouldly;
using Xunit;

namespace MistCity.Tests;

/// <summary>
/// 进食结算与死亡（ticket 06）—— 让时间第一次有牙齿。
///
/// <para>死亡是跨步事件，日志里扫不出来（当时那几行早翻过去了），画面上只有一张牌不见了。
/// 快照一次读完三样：欠多少、谁死的、死在第几次结算。</para>
///
/// <para><b>账怎么算</b>：每一步每个人扣一份（先到先得），扣不到记成欠账。
/// 一轮 60 步，所以三个人吃满一轮要 180 份。<b>「只有欠着的人死」要靠按步的稳定产出
/// 才验得到</b>：一次性囤 180 份是三个人都活、囤 5 份是三个人都死，
/// 稳态每步产出 2 份才是「h1、h2 吃饱、h3 饿死」。</para>
/// </summary>
public class SettlementTests
{
    /// <summary>一轮 = <see cref="Rations.CycleSteps"/> 步。</summary>
    private const int Cycle = Rations.CycleSteps;

    /// <summary>三个人吃满一轮要多少份。</summary>
    private const double FullCycleForAll = 3 * Cycle;

    [Fact]
    public void 推进到第60步那一刻发生一次结算_快照读得出这次吃掉多少()
    {
        var world = Stocked(FullCycleForAll);

        world.Advance(Cycle);

        var snap = world.DebugSnapshot();
        snap.ShouldContain("settled=1", Case.Sensitive, "推进到第 60 步发生第一次结算");
        snap.ShouldContain("died at=1 step=60", Case.Sensitive, "结算那一栏在：第几次、第几步、吃掉多少都读得出");
        snap.ShouldContain("who=-", Case.Sensitive, "无人死时名字那一格是「-」，不是那一行整个消失");
    }

    [Fact]
    public void 结算发生在60步整上_59步还没结算_60步结算了_61步没有第二次()
    {
        NewWorld().Food.SettlementIndex.ShouldBe(0, "开局还没到结算点");
        NewWorld().FoodAt(Cycle - 1).ShouldBe(0, "第 59 步还没结算");
        NewWorld().FoodAt(Cycle).ShouldBe(1, "第 60 步发生第一次结算");
        NewWorld().FoodAt(Cycle + 1).ShouldBe(1, "第 61 步没有第二次结算（下一个在第 120 步）");
    }

    [Fact]
    public void 供得上时这次结算无人死亡()
    {
        var world = Stocked(FullCycleForAll);

        world.Advance(Cycle);

        world.Food.LastSettlement!.DiedIds.ShouldBeEmpty("供得上时无人死亡 —— 只有欠着的人死");
        world.Food.LastSettlement.Consumed.ShouldBe(FullCycleForAll, "这一轮真吃掉了这么多");
        world.Food.LastSettlement.Short.ShouldBe(0, "缺口为 0");
        world.Food.AliveCount.ShouldBe(3);
    }

    [Fact]
    public void 供不上时欠着的人死掉_且只有欠着的人死()
    {
        // 稳态每步产出 2 份：h1 与 h2 每步都吃得到，h3 每步都欠。
        // 这一局是「只有欠着的人死」唯一真的能发生的样子 ——
        // 一次性囤 120 份是三个人都活、囤 5 份是三个人都死。
        var world = NewWorld();
        BindTwoFastLines(world);

        world.Advance(Cycle);

        world.Food.LastSettlement!.DiedIds.ShouldBe(new[] { "h3" },
            "只有欠着的那个死：先到先得，h1 与 h2 这一轮吃够了");
        world.Food.AliveCount.ShouldBe(2, "两个人活下来");
        world.Food.IsAlive("h1").ShouldBeTrue();
        world.Food.IsAlive("h2").ShouldBeTrue();
        world.Food.IsAlive("h3").ShouldBeFalse();
    }

    [Fact]
    public void 一个人都供不上时_三个人全死()
    {
        var world = NewWorld();   // 开局只有 5 份，没有任何产线

        world.Advance(Cycle);

        world.Food.LastSettlement!.DiedIds.ShouldBe(new[] { "h1", "h2", "h3" },
            "三个都欠着，所以三个都死");
        world.Food.AliveCount.ShouldBe(0);
    }

    [Fact]
    public void 快照一次读出四样_欠多少_离结算还剩几步_谁死的_吃掉多少()
    {
        var world = Stocked(FullCycleForAll);
        world.Advance(Cycle);

        var snap = world.DebugSnapshot();
        snap.ShouldContain("ration food=", Case.Sensitive, "① 现在还剩多少食物");
        snap.ShouldContain("owe=", Case.Sensitive, "② 现在欠多少（玩家故事 5）");
        snap.ShouldContain("next=", Case.Sensitive, "③ 离下个结算点还剩几步（玩家故事 4）");
        snap.ShouldContain("died at=", Case.Sensitive, "④ 谁在第几次结算点死的（玩家故事 12）");
        snap.ShouldContain("ate=", Case.Sensitive, "这一轮吃掉多少");
        snap.ShouldContain("short=", Case.Sensitive, "这一轮差了多少");
    }

    [Fact]
    public void 离下个结算点还剩几步_59步时是1步_60步时是60步()
    {
        NewWorld().DebugSnapshotAt(59).ShouldContain("next=1", Case.Sensitive, "第 59 步时离结算点 1 步");
        NewWorld().DebugSnapshotAt(60).ShouldContain("next=60", Case.Sensitive, "刚结算完，离下一个整 60 步");
    }

    [Fact]
    public void 死亡不是立刻判定_欠账先长结算点才判死()
    {
        var world = NewWorld();

        world.Advance(30);   // 还没到结算点

        world.Food.TotalOwe.ShouldBeGreaterThan(0, "按步扣除 ⇒ 欠账每一步都在长");
        world.Food.AliveCount.ShouldBe(3, "但没人死：死亡在结算点那一格判（spec「死亡不是立刻判定」）");

        world.Advance(30);   // 推到第 60 步

        world.Food.AliveCount.ShouldBe(0, "第 60 步结算，欠着的人死掉");
    }

    [Fact]
    public void 欠账逐人一行_玩家看得见是谁欠着()
    {
        var world = NewWorld();

        world.Advance(20);

        var snap = world.DebugSnapshot();
        snap.ShouldContain("H h1 owe=", Case.Sensitive, "欠账逐人一行，不是一个总数了事");
        snap.ShouldContain("H h3 owe=", Case.Sensitive);
    }

    [Fact]
    public void 死亡走实体层的移除次序_快照里读得到摘册事件()
    {
        var world = NewWorld();
        world.Advance(Cycle);

        world.Food.Entities.Events
            .Count<DemoFactory.GugaFramework.Entities.EntityRemoved>().ShouldBe(3,
            "三个人都死了，实体层那三个 removed 事件都在 —— 死亡走的是实体层的移除次序");
        world.Food.Entities.Events.TailText(5).ShouldContain("dropped=0", Case.Sensitive,
            "「日志里没有」≠「没发生」，读事件流必看 dropped_count");
    }

    [Fact]
    public void 死掉的人下一轮不再吃也不欠()
    {
        var world = NewWorld();
        BindTwoFastLines(world);   // h3 第一轮饿死
        world.Advance(Cycle);
        world.Food.AliveCount.ShouldBe(2);
        world.Food.IsAlive("h3").ShouldBeFalse();

        // 第二轮这两条产线仍够两个人吃 —— 死掉的人不在册上，不参与这一轮。
        world.Advance(Cycle);

        world.Food.LastSettlement!.DiedIds.ShouldBeEmpty("第二轮两个活人都吃够了，没人死");
        world.Food.AliveCount.ShouldBe(2);
        world.Food.LastSettlement.Consumed.ShouldBe(2 * Cycle,
            "这一轮只有两个人在吃，所以额度按在场人数算，不是按开局三人算");
    }

    private static MistCityWorld NewWorld() => new(new MistCityConfig());

    /// <summary>开局之外再补一份存量（供得起的那几局用）。</summary>
    private static MistCityWorld Stocked(double extra)
    {
        var world = NewWorld();
        world.Food.CreditProduction(extra);
        return world;
    }

    /// <summary>
    /// 接两条稳态产线：每步各产 1 份，合起来每步 2 份。
    ///
    /// <para>走的是真产线（夜露瓶的间隔是 1 步、每步 1 份），不是往世界里塞一个
    /// 「测试专用开关」—— 那类开关留在生产代码里，验的就不是玩家会走的那条路了。</para>
    /// </summary>
    private static void BindTwoFastLines(MistCityWorld world)
    {
        var first = world.Place("DewVial");
        var firstOnto = world.Place("Stone");
        world.Bind(first, firstOnto);

        var second = world.Place("DewVial");
        var secondOnto = world.Place("Stone");
        world.Bind(second, secondOnto);
    }
}
