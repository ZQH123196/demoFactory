#nullable enable
using System;
using DemoFactory.Prototypes.MistCity.Logic;
using Shouldly;
using Xunit;

namespace MistCity.Tests;

/// <summary>
/// 配方按「那一摞里能凑出」判 —— <b>拖一张到一摞上，凑得出就合成一张</b>。
///
/// <para><b>用户裁决的两条在这一格里</b>：
/// ① 棚的配方是「柴 + 田垄」，不是「2 柴 + 1 石」；
/// ② 住户与其他牌一样参与配方匹配，所以它被凑进配方时会被消耗掉。
/// 不这样的话「屋 = 棚 + 2 石」永远凑不齐 —— 压到房屋上的石已经先成了住户。</para>
///
/// <para><b>第三条例外不在裁决里</b>：「那一摞里能凑出就合，多出来的牌留在桌面上」
/// 是为了 spec 里「房屋被合成吃掉时住户留在桌面上」那一条还够得着才定的。
/// 消耗的正好是配方要的那几张；多条都凑得出时取份数最多的那一条，
/// 份数相同取配方表里在前的那一条；同一种牌消耗哪几张按牌册登记次序。</para>
///
/// <para><b>要钉住的是六样容易漏的</b>：
/// ① 输入的次序判据是牌册登记次序（同种子重跑逐字一致）；
/// ② 住户数进输入，被凑进配方时那张牌合成掉了；
/// ③ 多出来的那几张留在桌上、停在原位、挂接清干净；
/// ④ 多条都能凑出时取份数最多的那一条；
/// ⑤ 挂在被消耗那张身上的挂接一次腾干净（不留悬挂引用）；
/// ⑥ 底下那张没有别人压时行为与改之前逐字一致（没退化）。</para>
///
/// <para><b>局面有一部分是用写路径搭出来的</b>（<c>bind</c> 绕过落点判定）：
/// 房屋配方内部的每一对本身都是一条配方，玩家手势攒不出那样的摞 ——
/// 这件事记在报告里，断言不假装它能从手势来。
/// 但「玩家手势那条链」（柴柴出田垄、田垄加柴出棚、压石出屋）在这里另有一条从手势走的。</para>
///
/// <para>断言只从命令进、快照出，不断言内部结构与字段名。
/// 合成之后那张新牌落在摞底那张原来的位置，那是表现层数据，离线断言不到，真机验。</para>
public class StackCombineTests
{
    // === 玩家手势那条升级链（这三条玩家做得到）===

    [Fact]
    public void 升级链第一级_两张柴叠上去出田垄_再压一张柴出棚()
    {
        var world = NewWorld();

        // 第一步：拖一张柴压到另一张柴上 —— 那一摞恰好凑出「柴 + 柴」。
        var furrow = world.Drop("c02", "c01");
        furrow.Action.ShouldBe(DropAction.Combine, "柴 + 柴 凑得出田垄");
        furrow.Text.ShouldBe("combine ok c01+c02 => c08 kind=Furrow",
            "整摞按牌册登记次序列进读数");

        // 第二步：拖第三张柴压到田垄上 —— 那一摞凑出「柴 + 田垄」出棚。
        var shed = world.Drop("c03", furrow.ResultCard!);
        shed.Action.ShouldBe(DropAction.Combine, "柴 + 田垄 凑得出棚");
        shed.Text.ShouldBe("combine ok c03+c08 => c09 kind=Shed",
            "棚是升级链第一级，它换掉的是一张田垄与一张柴");
        world.DebugSnapshot().ShouldContain("keep=8", Case.Sensitive,
            "棚那一档 +2，保留上限从 6 涨到 8");
    }

    [Fact]
    public void 升级链第二级_压石到住着人的棚上_合出屋()
    {
        var world = NewWorld();
        var shed = ShedOf(world);

        // 第一张石压到棚上：凑不出配方，于是住进去。
        var first = world.Place("Stone");
        world.Drop(first, shed).Action.ShouldBe(DropAction.MoveIn,
            "一张石凑不出屋，得两张 —— 这一下落空不到，住进去");
        world.DebugSnapshot().ShouldContain($"B {first} onto={shed} kind=Resident", Case.Sensitive,
            "住户与产线记在同一份表里，那一格说得出这是住户");

        // 第二张石压到住户身上：住户与其他牌一样参与配方，三份凑齐出屋。
        var second = world.Place("Stone");
        var result = world.Drop(second, first);

        result.Action.ShouldBe(DropAction.Combine, "住户参与配方，三份凑齐就整摞合成");
        result.Text.ShouldBe($"combine ok {shed}+{first}+{second} => c12 kind=House",
            "棚 + 2 石 出屋；住户那张被算进配方，因此合成掉了");

        var snap = world.DebugSnapshot();
        snap.ShouldNotContain($"C {first} ", Case.Sensitive, "住户被凑进配方，那张牌合成掉了");
        snap.ShouldContain("C c12 seq=12 kind=House", Case.Sensitive);
        snap.ShouldContain("keep=11", Case.Sensitive, "屋那一档 +5，上限从 8 涨到 11");
        snap.ShouldContain("bind count=0", Case.Sensitive, "腾干净之后不留悬挂挂接");
    }

    [Fact]
    public void 升级链第三级_屋加两张泉眼_整摞合成出一张仓()
    {
        var world = NewWorld();
        var shed = ShedOf(world);
        var first = world.Place("Stone");
        world.Drop(first, shed);
        var house = world.Drop(world.Place("Stone"), first).ResultCard!;

        // 两张泉眼：一张是柴 + 石合出来的，另一张现放 ——
        // 开局只剩一柴两石，凑不出第二张泉眼，所以这一级离线才走得完（真机那一条见报告）。
        var made = NewIdFrom(world.Combine(["c04", "c05"]));
        var second = world.Place("Spring");
        world.Bind(second, house);
        var result = world.Drop(made, second);

        result.Action.ShouldBe(DropAction.Combine, "屋 + 2 泉眼 出仓");
        result.Text.ShouldBe($"combine ok {house}+{made}+{second} => c15 kind=Granary",
            "仓是升级链第三级，它换掉的是屋与两张泉眼");
        world.DebugSnapshot().ShouldContain("keep=16", Case.Sensitive, "仓那一档 +10");
    }

    // === 多出来的牌留在桌面上 ===

    [Fact]
    public void 摞上多出来的牌_合完仍在桌上_挂在被消耗那张身上的那张挂接清干净()
    {
        // 那一摞：柴(底下) ← 石 ← 柴。手上还有第四张石。
        // 凑得出的是「柴 + 石」出泉眼（两份），而「柴 + 柴」出田垄（两份）同样凑得出 ——
        // 份数相同，取配方表里在前的那一条，所以出泉眼。
        var world = NewWorld();
        var rider = world.Place("Wood");        // c08 压在 c05 上
        world.Bind("c05", "c01");
        world.Bind(rider, "c05");
        var dragged = world.Place("Stone");     // c09 刚拖过来的

        var result = world.Drop(dragged, "c05");

        result.Action.ShouldBe(DropAction.Combine, "那一摞能凑出泉眼");
        result.Text.ShouldBe("combine ok c01+c05 => c10 kind=Spring left=2",
            "消耗的正好是配方要的那两张；多出来的两张报出来");

        var snap = world.DebugSnapshot();
        snap.ShouldContain("C c10 seq=10 kind=Spring", Case.Sensitive);
        snap.ShouldContain($"C {rider} ", Case.Sensitive, "多出来的那一张仍在桌上，停在原位");
        snap.ShouldContain($"C {dragged} ", Case.Sensitive, "刚拖过来的那张没被消耗掉");
        snap.ShouldNotContain($"B {rider} ", Case.Sensitive,
            "它的承载者被合成吃掉了，挂接一并腾干净 —— 它退回自由平面、不再产出");
        snap.ShouldNotContain("onto=c05", Case.Sensitive, "快照里查不到指向已消耗承载者的悬挂挂接");
        snap.ShouldContain("bind count=0", Case.Sensitive);
        snap.ShouldContain("card count=8", Case.Sensitive, "桌上九张减去被消耗的两张加上新的一张");
    }

    // === 多条都能凑出时的判据 ===

    [Fact]
    public void 多条都凑得出时_取份数最多的那一条()
    {
        // 棚 + 三张石：「棚 + 2 石」出屋（三份）与「石 + 石」出灰烬炉（两份）同时凑得出。
        var world = NewWorld();
        var shed = world.Place("Shed");
        var s1 = world.Place("Stone");
        var s2 = world.Place("Stone");
        world.Bind(s2, s1);
        world.Bind(s1, shed);
        var extra = world.Place("Stone");

        var result = world.Drop(extra, s2);

        result.Text.ShouldBe("combine ok c08+c09+c10 => c12 kind=House left=1",
            "取份数最多的那一条：三份的屋压过两份的灰烬炉");
        world.DebugSnapshot().ShouldContain("kind=House", Case.Sensitive);
    }

    [Fact]
    public void 份数相同时_取配方表里在前的那一条()
    {
        // 柴 + 石 出泉眼、柴 + 柴 出田垄，两条都是两份，柴 + 石 + 柴 这一摞两条都凑得出。
        var world = NewWorld();
        var rider = world.Place("Stone");     // c08 压在 c01 上
        world.Bind(rider, "c01");
        var dragged = world.Place("Wood");    // c09

        var result = world.Drop(dragged, "c01");

        result.Text.ShouldBe("combine ok c01+c08 => c10 kind=Spring left=1",
            "份数相同取表里在前的那一条：柴 + 石 排在 柴 + 柴 前面");
    }

    [Fact]
    public void 同一个牌种消耗哪几张_按牌册登记次序最老的先消耗()
    {
        // 两张石压在棚上，底下那张先登记 —— 消耗它，不消耗后登记的那张。
        var world = NewWorld();
        var shed = world.Place("Shed");
        var older = world.Place("Stone");
        var newer = world.Place("Stone");
        world.Bind(newer, older);
        world.Bind(older, shed);
        var dragged = world.Place("Stone");

        var result = world.Drop(dragged, newer);

        result.Text.ShouldBe($"combine ok {shed}+{older}+{newer} => c12 kind=House left=1",
            "同一种牌消耗最老的那几张：先登记的那两张进配方，后登记的那一张留在桌上");
        world.DebugSnapshot().ShouldContain($"C {dragged} ", Case.Sensitive,
            "留下的是最年轻的那一张 —— 判据是登记次序，不看摞里的高低");
    }

    // === 输入次序判据 ===

    [Fact]
    public void 整摞的输入按牌册登记次序_不按摞里的高低次序()
    {
        var world = NewWorld();
        var shed = ShedOf(world);
        var first = world.Place("Stone");
        world.Drop(first, shed);
        var house = world.Drop(world.Place("Stone"), first).ResultCard!;

        // 那一摞的高低次序是「屋 ← 泉(c14) ← 泉(c13)」，
        // 牌册登记次序是「屋(c12) → 泉(c13) → 泉(c14)」。这两条不是同一条。
        var springA = NewIdFrom(world.Combine(["c04", "c05"]));
        var springB = world.Place("Spring");
        world.Bind(springB, house);

        var result = world.Drop(springA, springB);

        result.Text.ShouldBe($"combine ok {house}+{springA}+{springB} => c15 kind=Granary",
            "合成读数按牌册登记次序列输入，不按摞里的高低次序，也不按点击次序");
        springA.CompareTo(springB).ShouldBeLessThan(0,
            "前置：摞顶那张在登记次序上比摞里那张晚 —— 所以按高低排与按登记排答得不一样");
    }

    [Fact]
    public void 同一颗种子两跑_凑得出的读数与快照逐字一致()
    {
        var a = WholeStackCombine();
        var b = WholeStackCombine();

        a.ShouldBe(b, "挑哪几张与读数怎么排列都没有出处的话，重跑会漂");
    }

    // === 拖起来的那张本身压着别的牌（先腾再叠） ===

    [Fact]
    public void 拖的那张本身是房屋且住着人_住户不在那一摞里_它退回自由平面()
    {
        var world = NewWorld();
        var shed = ShedOf(world);
        var resident = world.Place("Stone");
        world.Drop(resident, shed);                     // 住进去
        var rider = world.Place("Stone");
        world.Bind(rider, "c06");                       // 石压石：摞里那一张

        var result = world.Drop(shed, rider);

        result.Action.ShouldBe(DropAction.Combine, "石 + 石 + 棚 凑齐一条配方，合成一张屋");
        result.Text.ShouldBe($"combine ok c06+{shed}+{rider} => c12 kind=House",
            "被拖的那张也在那一摞里，按牌册登记次序列");

        // 住户不在那一摞里（那一摞是从落点那张往两头收的，不是从被拖的那张），
        // 所以它没有被消耗，只是跟着被吃掉的房屋退回自由平面。
        var snap = world.DebugSnapshot();
        snap.ShouldContain($"C {resident} ", Case.Sensitive, "住户没有被吃掉，仍在桌上");
        snap.ShouldNotContain($"B {resident} ", Case.Sensitive, "它退回自由平面，不再挂在房屋上");
        snap.ShouldNotContain($"onto={shed}", Case.Sensitive, "不留指向已消耗承载者的悬挂挂接");
        snap.ShouldContain("bind count=0", Case.Sensitive);
    }

    [Fact]
    public void 拖的那张身上压着牌_没配方时那些牌先腾干净_它自己派工()
    {
        var world = NewWorld();
        var dragged = world.Place("Kiln");
        var rider = world.Place("Kiln");
        world.Bind(rider, dragged);

        var result = world.Drop(dragged, "c01");

        result.Action.ShouldBe(DropAction.Attach, "柴 + 灰烬炉 凑不出配方，走派工那一档");
        var snap = world.DebugSnapshot();
        snap.ShouldContain($"B {dragged} onto=c01", Case.Sensitive, "手里那张派到底下那张上");
        snap.ShouldNotContain($"B {rider} ", Case.Sensitive, "压在它身上的那张先腾干净了");
        snap.ShouldContain($"C {rider} ", Case.Sensitive, "腾干净不等于吃掉，那张仍在桌上");
        snap.ShouldContain("bind count=1", Case.Sensitive);
    }

    [Fact]
    public void 拖的那张身上压着牌_这一下落空时那些牌一条都不动()
    {
        var world = NewWorld();
        var shed = world.Place("Shed");
        var resident = world.Place("Stone");
        world.Drop(resident, shed);
        var before = world.DebugSnapshot();

        var result = world.Drop(shed, "c05");

        world.DebugSnapshot().ShouldBe(before,
            "落空是「什么都不发生」：腾挂接只发生在这一下真的会发生的路上");
        result.Reason.ShouldBe("top-not-producer", "房屋只当基地不当工人");
    }

    // === 没退化与凑不齐 ===

    [Fact]
    public void 底下那张没有别人压_输入就是两张_读数与改之前逐字一致()
    {
        var world = NewWorld();

        var result = world.Drop("c01", "c07");

        result.Text.ShouldBe("combine ok c01+c07 => c08 kind=DewVial",
            "摞里只有一张时，行为与改之前一个字都不差：读数不补「多出来几张」那一格");
        world.DebugSnapshot().ShouldContain("card count=6", Case.Sensitive, "两张换一张");
    }

    [Fact]
    public void 整摞凑不出配方_走原来的那一档_一张都不消耗()
    {
        var world = NewWorld();
        var a = world.Place("Kiln");
        var b = world.Place("Kiln");
        world.Drop(a, "c05");
        world.Drop(b, "c05");

        var c = world.Place("Kiln");
        var result = world.Drop(c, b);

        result.Action.ShouldBe(DropAction.Attach, "凑不出就走派工那一档，与改之前一样");
        var snap = world.DebugSnapshot();
        snap.ShouldContain($"B {c} onto={b}", Case.Sensitive, "派到底下那张上（摞顶那张）");
        snap.ShouldContain("card count=10", Case.Sensitive, "派工不消耗牌，那一摞三张都还在");
        snap.ShouldContain("bind count=3", Case.Sensitive);
        snap.ShouldContain("keep=6", Case.Sensitive, "上限那一格与合成之前一样");
    }

    // === 注入与玩家走同一条路 ===

    [Fact]
    public void 两种落点写法合成之后_读数与快照逐字一致()
    {
        var byToken = NewWorld();
        byToken.Bind("c05", "c01");
        var first = byToken.Drop(byToken.Place("Stone"), "c01");

        var byTarget = NewWorld();
        byTarget.Bind("c05", "c01");
        var second = byTarget.Drop(byTarget.Place("Stone"), DropTarget.Onto("c01"));

        first.Text.ShouldBe(second.Text, "落点是牌 id 与落点对象走同一个判定方法");
        byToken.DebugSnapshot().ShouldBe(byTarget.DebugSnapshot(), "合成之后快照也逐字一致");
    }

    // === 共用件 ===

    private static MistCityWorld NewWorld() => new(new MistCityConfig());

    /// <summary>
    /// 用写路径合出一张棚 —— <b>玩家手势那条链的前两步</b>
    /// （柴 + 柴 出田垄、田垄 + 柴 出棚）。
    /// 离线这一步走合成动词而不是落点判定，是为了让后面几条断言的局面一眼看得懂。
    /// </summary>
    private static string ShedOf(MistCityWorld world)
    {
        var furrow = NewIdFrom(world.Combine(["c01", "c02"]));
        return NewIdFrom(world.Combine([furrow, "c03"]));
    }

    /// <summary>
    /// 跑一次凑得出的合成 —— <b>返回那一发读数与快照拼起来的一句话</b>，
    /// 两跑比它就是同种子重跑逐字一致那一条。
    /// </summary>
    private static string WholeStackCombine()
    {
        var world = NewWorld();
        var rider = world.Place("Stone");
        world.Bind("c05", "c01");
        world.Bind(rider, "c05");
        var result = world.Drop(world.Place("Stone"), "c05");
        return $"{result.Text} || {world.DebugSnapshot()}";
    }

    /// <summary>跑一发合成，返回新发的那张牌 id（新牌最后进册）。</summary>
    private static string NewIdFrom(string combineText)
    {
        var parts = combineText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts[^2];
    }
}