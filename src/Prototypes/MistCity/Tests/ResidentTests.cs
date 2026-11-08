#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DemoFactory.GugaFramework.Entities;
using DemoFactory.Prototypes.MistCity.Logic;
using Shouldly;
using Xunit;

namespace MistCity.Tests;

/// <summary>
/// 住户与生人（ticket 06 的生人那一半）—— 牌叠到房屋牌上就是住进去，
/// 住满 2 人之后每个进食结算周期出 1 个人。
///
/// <para><b>三格最容易写反，分开钉</b>：
/// ① 生人落在<b>对齐的结算步</b>（第 60/120/180… 步），不是住满那一刻起算的第 60 步；
/// ② 生人排在<b>进食结算之后</b>，所以那一发的缺口数与死者名单只算出生前就在册的人；
/// ③ 住处掉到 2 人以下时<b>计时清零</b>，重新住满要重新等一个完整周期。
/// 第 ① 与第 ③ 是一条规则的两面，所以「拿掉一个又在同一个周期里住回去」那种局面
/// 单独有断言：接着上次没走完的步数继续走的话，它会在第 60 步就生出来。</para>
///
/// <para><b>为什么用灰烬炉当住户</b>：它与三种房屋都凑不出配方，
/// 所以第二张压上去是「住进去」而不是「整摞合成」。用石当住户的话，
/// 第二张石压上去会凑齐「棚 + 2 石」直接出一间屋 —— 住户参与配方匹配是用户裁决过的，
/// 断言不假装能绕开它。</para>
///
/// <para>断言只从命令进、快照出，不断言内部结构与字段名（spec「Testing Decisions」）。
/// 「牌面上不印累计出生数」那一格住在表现层，出图复核，不在这里假装验。</para>
public class ResidentTests
{
    /// <summary>一轮 = <see cref="Rations.CycleSteps"/> 步。</summary>
    private const int Cycle = Rations.CycleSteps;

    // === 住户不产食物 ===

    [Fact]
    public void 牌叠到房屋牌上之后_那条挂接是住户_它不产食物()
    {
        var world = NewWorld();
        var shed = world.Place("Shed");
        var resident = world.Place("Kiln");   // 灰烬炉：当产线是每 7 步出 2 份

        world.Drop(resident, shed);

        world.DebugSnapshot()
            .ShouldContain($"B {resident} onto={shed} kind=Resident every=0", Case.Sensitive,
                "挂接从快照读得出它是住户：类型那一格与间隔 0 都在");

        world.Advance(14);   // 灰烬瓶当产线早出货两轮了

        var line = CardLine(world.DebugSnapshot(), resident);
        line.ShouldContain("out=0", Case.Sensitive,
            "住户不产食物 —— 产出物是人不是份，由底下那张牌决定");
        line.ShouldContain("livedin=-", Case.Sensitive, "它没被腾下来过");
    }

    // === 住满之前不出人 ===

    [Fact]
    public void 只住一个人_过两个结算点也不出生()
    {
        var world = NewWorld();
        var shed = world.Place("Shed");
        world.Drop(world.Place("Kiln"), shed);

        var snap = world.DebugSnapshotAt(2 * Cycle);

        snap.ShouldContain($"home {shed} live=1 born=0 in=-", Case.Sensitive,
            "少了一个人就没有计时在跑 —— 那一格印的是「-」，不是 0");
        snap.ShouldNotContain("H h4 ", Case.Sensitive, "住满 2 人之前不出人");
    }

    // === 生人落在对齐的结算步 ===

    [Fact]
    public void 住满两人_第59步还没生_第60步生一个()
    {
        var (world, house) = Fed();
        TwoResidents(world, house);

        var at59 = world.DebugSnapshotAt(Cycle - 1);
        at59.ShouldContain($"home {house} live=2 born=0 in=1", Case.Sensitive,
            "第 59 步时离下一次生人还差 1 步");
        at59.ShouldNotContain("H h4 ", Case.Sensitive, "第 59 步还没生");

        var at60 = world.DebugSnapshotAt(Cycle);
        at60.ShouldContain($"home {house} live=2 born=1 in=60", Case.Sensitive,
            "第 60 步生一个，计时重开一个完整周期");
        at60.ShouldContain("alive=4", Case.Sensitive, "顶栏在世涨 1");
        at60.ShouldContain("H h4 owe=0 ate=0", Case.Sensitive, "新人这一轮一份都没吃");
    }

    [Fact]
    public void 生人排在进食结算之后_那一发的缺口数与死者名单只算出生前就在册的人()
    {
        // 开局 5 份、三个人、没有产线：这一轮三个人都欠，全部饿死。
        // 生人发生在同一发，所以这一发的读数里仍然是三个人。
        var world = NewWorld();
        var shed = world.Place("Shed");
        TwoResidents(world, shed);

        var snap = world.DebugSnapshotAt(Cycle);

        // 缺口数按三个人算：3 × 60 − 吃到 5 份 = 175。
        // 新生的人若被算进去会是 4 × 60 − 5 = 235。
        snap.ShouldContain("died at=1 step=60 ate=5 short=175 who=h1,h2,h3", Case.Sensitive,
            "结算那一刻在册的是三个人：缺口数与死者名单都不含新人");
        snap.ShouldContain("alive=1", Case.Sensitive, "三个人饿死之后，只剩生出来的这一个");
        snap.ShouldContain("H h4 owe=0 ate=0", Case.Sensitive, "新人从零开始");
        snap.ShouldContain("dropped=0", Case.Sensitive, "读事件流必看 dropped_count");
    }

    [Fact]
    public void 生完那一刻新人一份都没吃_从下一步起每步吃一份()
    {
        var world = NewWorld();
        var shed = world.Place("Shed");
        TwoResidents(world, shed);

        world.Advance(Cycle);   // 生在结算那一刻
        world.Advance(1);       // 再推一步

        world.DebugSnapshot().ShouldContain("H h4 owe=1 ate=0", Case.Sensitive,
            "从下一步起每步吃一份 —— 所以下一个周期的需求加 1");
    }

    // === 新的人挂在登记次序队尾 ===

    [Fact]
    public void 新的人挂在登记次序队尾_缺口先压在他身上()
    {
        // 三条产线每步各产 1 份，供三个人正好相等。第 60 步生一个之后，
        // 每步 3 份的供与 4 个人的需差 1 份，而先到先得按登记次序发。
        var (world, house) = Fed();
        TwoResidents(world, house);

        var at119 = world.DebugSnapshotAt(2 * Cycle - 1);

        string.Join(",", ResidentIds(at119)).ShouldBe("h1,h2,h3,h4",
            "快照逐人一行按登记次序印，新生的人排在最后");
        at119.ShouldContain("H h4 owe=59 ate=0", Case.Sensitive, "他挂在队尾，每一步都吃不到");
        at119.ShouldContain("H h1 owe=0 ate=59", Case.Sensitive, "先登的三个人这一轮仍吃够了");
        at119.ShouldContain($"home {house} live=2 born=1", Case.Sensitive);

        var at120 = world.DebugSnapshotAt(2 * Cycle);
        at120.ShouldContain("died at=2 step=120 ate=180 short=60 who=h4", Case.Sensitive,
            "缺口全压在最后一个人头上：这一轮 4 人在册、吃到 180 份，差 60 份全在他身上");
        at120.ShouldContain("alive=4", Case.Sensitive, "结算走完又生一个，在世回到 4");
        at120.ShouldContain($"home {house} live=2 born=2", Case.Sensitive, "第 120 步再生一个");
    }

    // === 住户牌能被拿下来 ===

    [Fact]
    public void 玩家能把住户牌拿下来_这间房的空位数减一()
    {
        var (world, house) = Fed();
        var residents = TwoResidents(world, house);

        var result = world.Drop(residents[1], DropTarget.Blank);

        result.Action.ShouldBe(DropAction.TakeDown, "住户退房走拿下来那条路，不损失材料");
        var snap = world.DebugSnapshot();
        snap.ShouldContain($"home {house} live=1 born=0 in=-", Case.Sensitive,
            "少一个人就清计时：那一格从「差几步」变回「-」");
        snap.ShouldContain($"C {residents[1]} ", Case.Sensitive, "拿下来的那张退回自由平面，仍在桌上");
        snap.ShouldNotContain($"B {residents[1]} ", Case.Sensitive, "它不再挂在房屋上");
        snap.ShouldContain("bind count=4", Case.Sensitive,
            "三条产线加上还住着的那一个 —— 被拿下来的那一条不在里面");

        // 空出来的位置能再住进去一个，住满之后重新等一个完整周期。
        world.Drop(world.Place("Kiln"), house).Action.ShouldBe(DropAction.MoveIn);
        world.DebugSnapshot().ShouldContain($"home {house} live=2 born=0 in=60", Case.Sensitive,
            "重新住满：计时从这一刻起算一个完整周期");
    }

    [Fact]
    public void 住客被拿掉之后计时清零_重新住满要再等一个完整周期()
    {
        var (world, house) = Fed();
        var residents = TwoResidents(world, house);

        world.Advance(30);
        world.Drop(residents[1], DropTarget.Blank);   // 第 30 步拿掉一个
        world.Drop(residents[1], house);              // 第 30 步又住回去

        // 接着上次没走完的步数继续走的话，第 60 步就生出来了 —— 那一格要红。
        var at60 = world.DebugSnapshotAt(Cycle);
        at60.ShouldContain($"home {house} live=2 born=0", Case.Sensitive,
            "清零之后重新等一个完整周期：第 60 步不生");

        var at119 = world.DebugSnapshotAt(2 * Cycle - 1);
        at119.ShouldContain($"home {house} live=2 born=0", Case.Sensitive, "第 119 步还没到");

        var at120 = world.DebugSnapshotAt(2 * Cycle);
        at120.ShouldContain($"home {house} live=2 born=1 in=60", Case.Sensitive,
            "第 120 步生一个 —— 与刚住满那一步对齐");
    }

    [Fact]
    public void 住满之后第三张牌落空_说得出原因()
    {
        var (world, house) = Fed();
        TwoResidents(world, house);
        var third = world.Place("Kiln");
        var before = world.DebugSnapshot();

        var result = world.Drop(third, house);

        result.Action.ShouldBe(DropAction.NoOp, "住满了没有第三个空位");
        result.Reason.ShouldBe("bottom-full", "原因短词说得出是哪一种落空");
        world.DebugSnapshot().ShouldBe(before, "落空是「什么都不发生」");
    }

    // === 承载者被合成吃掉时住户怎么腾 ===

    [Fact]
    public void 房屋被合成吃掉_住户牌留在桌上不再产出_快照里查不到指向它的挂接()
    {
        var world = NewWorld();
        var shed = world.Place("Shed");
        var resident = world.Place("DewVial");
        world.Drop(resident, shed);
        var stone = world.Place("Stone");
        world.Drop(stone, shed);                              // 一张石凑不出屋，住进去

        var result = world.Drop(world.Place("Stone"), stone); // 棚 + 2 石 出屋，那间棚被吃掉

        result.Action.ShouldBe(DropAction.Combine, "住户参与配方，承载者被整摞合成掉");

        var snap = world.DebugSnapshot();
        snap.ShouldContain($"C {resident} ", Case.Sensitive, "住户牌留在桌面上，停在房屋原来的位置");
        snap.ShouldContain($"livedin={shed}", Case.Sensitive, "牌面上还看得出它原来住在哪间房");
        snap.ShouldNotContain($"B {resident} ", Case.Sensitive, "它退回自由平面，不再挂在任何牌身上");
        snap.ShouldNotContain($"onto={shed}", Case.Sensitive, "快照里查不到指向已消失房屋的挂接");
        snap.ShouldContain("bind count=0", Case.Sensitive);
        snap.ShouldContain($"home {result.ResultCard} live=0 born=0 in=-", Case.Sensitive,
            "新牌是另一间房：没人住、没生过");

        world.Advance(5);   // 夜露瓶的间隔是 1 步，当产线的话早就出货了

        world.DebugSnapshot().ShouldContain($"out=0 livedin={shed}", Case.Sensitive,
            "被腾下来的住户牌不再产出");
    }

    [Fact]
    public void 玩家自己把住户牌拿下来_那一格不记曾住在()
    {
        var (world, house) = Fed();
        var residents = TwoResidents(world, house);

        world.Drop(residents[1], DropTarget.Blank);

        // 玩家自己选的下家不算「被腾下来」—— 否则它改挂产线时牌面还挂着旧住户的账。
        CardLine(world.DebugSnapshot(), residents[1]).ShouldContain("livedin=-", Case.Sensitive);
    }

    // === 事件与确定性 ===

    [Fact]
    public void 生人有事件可订阅_新人上户口拿新编号()
    {
        var world = NewWorld();
        var shed = world.Place("Shed");
        TwoResidents(world, shed);
        world.Food.Entities.Events.Count<EntitySpawned>().ShouldBe(3, "开局三个人各登一次");

        world.Advance(Cycle);

        var spawned = world.Food.Entities.Events.All.OfType<EntitySpawned>().ToList();
        spawned.Count.ShouldBe(4, "生出来的人上了一次册");
        spawned[^1].Subject.ShouldBe("h4", "新的人拿新编号 —— id 永不复发");
    }

    [Fact]
    public void 同一颗种子两跑_生人与房屋读数逐字一致()
    {
        Run().ShouldBe(Run(), "生人落在哪一步、缺多少份、谁排在队尾都没有出处的话，重跑会漂");

        static string Run()
        {
            var (world, house) = Fed();
            TwoResidents(world, house);
            world.Advance(2 * Cycle);
            return $"{world.DebugSnapshot()}";
        }
    }

    // === 共用件 ===

    private static MistCityWorld NewWorld() => new(new MistCityConfig());

    /// <summary>
    /// 一间住满人的房子 + 三条每步各产 1 份的产线 ——
    /// <b>供给与三个人正好相等，所以推进到结算点无人饿死</b>，读数里只剩生人那几格。
    ///
    /// <para>开局只发一张屋：桌上九张对上限十一，轮末不处置任何一张，
    /// 处置那一格不混进这几条断言。</para>
    /// </summary>
    private static (MistCityWorld World, string House) Fed()
    {
        var world = new MistCityWorld(new MistCityConfig
        {
            OpeningHand = ["House"],
            OpeningFood = 0.0,
        });

        for (var i = 0; i < 3; i++)
        {
            var vial = world.Place("DewVial");
            world.Bind(vial, world.Place("Stone"));
        }

        return (world, "c01");
    }

    /// <summary>
    /// 给那间房住满两个人 —— <b>住户用灰烬炉</b>：它与三种房屋都凑不出配方，
    /// 所以第二张压上去是住进去而不是整摞合成。
    /// </summary>
    private static string[] TwoResidents(MistCityWorld world, string house)
    {
        var first = world.Place("Kiln");
        world.Drop(first, house).Action.ShouldBe(DropAction.MoveIn, "一张灰烬炉凑不出配方，住进去");

        var second = world.Place("Kiln");
        world.Drop(second, house).Action.ShouldBe(DropAction.MoveIn, "第二张也住进去");

        return [first, second];
    }

    /// <summary>快照里逐人那几行的人 id，按印出来的次序（折行后是 <c>H h1 owe=… ate=…</c>）。</summary>
    private static string[] ResidentIds(string snapshot)
    {
        var tokens = snapshot.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var ids = new List<string>();
        for (var i = 0; i < tokens.Length - 2; i++)
        {
            if (tokens[i] != "H") continue;
            if (!tokens[i + 2].StartsWith("owe=", StringComparison.Ordinal)) continue;
            ids.Add(tokens[i + 1]);
        }

        return ids.ToArray();
    }

    /// <summary>从快照里取出某张牌那一行（折行后是 <c>C &lt;id&gt; seq=… </c> 这么一串 token）。</summary>
    private static string CardLine(string snapshot, string id)
    {
        var tokens = snapshot.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < tokens.Length - 1; i++)
        {
            if (tokens[i] != "C" || tokens[i + 1] != id) continue;
            var parts = new List<string> { id };
            for (var j = i + 2; j < tokens.Length && tokens[j] != "C"; j++) parts.Add(tokens[j]);
            return string.Join(" ", parts);
        }

        throw new ShouldAssertException($"快照里没有牌 {id} 那一行：{snapshot}");
    }
}