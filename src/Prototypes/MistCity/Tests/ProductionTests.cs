#nullable enable
using DemoFactory.Prototypes.MistCity.Logic;
using Shouldly;
using Xunit;

namespace MistCity.Tests;

/// <summary>
/// 按步产出（ticket 04）—— 产出在每一步都在跑，不攒到结算点。
/// 边界那一步单独有断言：少推一步少一次，多推一步多一次。
///
/// <para><b>账怎么算</b>：开局 5 份；每一步扣「在场人数」份（三个��各 1 份 = 3 份）；
/// 扣不起的那几份记成欠账。所以推进 n 步的账是
/// <c>开局 5 + 这 n 步产出 − (3 × n) + 欠账 = 现在的食物</c>。</para>
/// </summary>
public class ProductionTests
{
    [Fact]
    public void 推进恰好间隔那么多步_这条产线产出一次()
    {
        var world = NewWorld();
        world.Bind("c07", "c05");   // 泉眼压石：间隔 3，每次 1 份

        world.Advance(3);

        world.Cards.Require("c07").Produced.ShouldBe(1,
            "推进恰好间隔 3 步 → 产出一次（产出落在第 3 步）");
    }

    [Fact]
    public void 少推一步不产出_边界那一步单独断言()
    {
        var world = NewWorld();
        world.Bind("c07", "c05");
        var before = world.Cards.Require("c07").Produced;

        world.Advance(2);   // 间隔 3，只推了 2 步

        world.Cards.Require("c07").Produced.ShouldBe(before,
            "少推一步就还没到产出时刻 —— 边界就在间隔那一格上");

        world.Advance(1);   // 补上那一步

        world.Cards.Require("c07").Produced.ShouldBe(before + 1, "补上那一步就产出了");
    }

    [Fact]
    public void 多推一步就多产出一次_六步出两次()
    {
        var world = NewWorld();
        world.Bind("c07", "c05");

        world.Advance(6);   // 间隔 3，正好两轮

        world.Cards.Require("c07").Produced.ShouldBe(2, "两步一个间隔，6 步出两次");
    }

    [Fact]
    public void 推进n步_快照读出的产出与按间隔手算的结果逐个相等()
    {
        var world = NewWorld();
        world.Bind("c07", "c05");   // 间隔 3

        world.Advance(10);

        // 手算：产出落在第 3、6、9 步，第 10 步不到点。
        world.Cards.Require("c07").Produced.ShouldBe(3, "10 步 ÷ 间隔 3 = 3 次");
        var line = CardLine(world.DebugSnapshot(), "c07");
        line.ShouldContain("out=3", Case.Sensitive, "快照那一格与手算逐字相等");
        line.ShouldContain("in=2", Case.Sensitive, "第 10 步时离下一次（第 12 步）还差 2 步");
    }

    [Fact]
    public void 食物按步扣_扣不起的那几份记成欠账而不是当场死人()
    {
        var world = NewWorld();   // 开局 5 份、三个人
        world.Bind("c07", "c05"); // 间隔 3，每 3 步产出 1 份

        world.Advance(3);

        // 逐人各扣一份，开局 5 份、第 3 步才产出 1 份：第 1 步三个人都吃到（剩 2），
        // 第 2 步只够两个人（h3 欠 1），第 3 步那 1 份只够 h1（h2、h3 各欠 1）。
        // 于是账上归零，缺口 3 份全记在欠账那一格。
        world.Food.Food.ShouldBe(0, "账上归零：5 + 1 − 3 = 3，剩下的 3 份被按步扣掉");
        world.Food.TotalOwe.ShouldBe(3, "第 2 步 h3 欠 1，第 3 步 h2 与 h3 各欠 1");
        world.Food.AliveCount.ShouldBe(3, "死亡不是立刻判定：欠账不长到结算点就不死人");

        // 再推一步：这一步没有产出（间隔 3，下一次在第 6 步），三个人各扣不到，各欠一份。
        world.Advance(1);
        world.Food.TotalOwe.ShouldBe(6, "扣不起的那几份记成欠账 —— 玩家看得见缺口（玩家故事 5）");
        world.Food.AliveCount.ShouldBe(3, "第 4 步还没到结算点，一个都没死");
    }

    [Fact]
    public void 拆掉绑定后推进同样步数_这条产线不再产出()
    {
        var world = NewWorld();
        world.Bind("c07", "c05");
        world.Advance(3);   // 先产出一份，产线是活的

        world.Unbind("c07");
        var producedAtUnbind = world.Cards.Require("c07").Produced;
        var oweAtUnbind = world.Food.TotalOwe;

        world.Advance(9);   // 推三倍间隔那么多步

        world.Cards.Require("c07").Produced.ShouldBe(producedAtUnbind, "解绑之后这条产线不再产出");
        world.Food.TotalOwe.ShouldBeGreaterThan(oweAtUnbind,
            "只被按步扣，没有产出补回来 —— 缺口只会越长");
    }

    [Fact]
    public void 两条产线并存_各自按自己的间隔产出()
    {
        var world = NewWorld();
        world.Bind("c07", "c05");   // 泉眼：间隔 3，每次 1
        world.Bind("c06", "c04");   // 石：间隔 9，每次 1

        world.Advance(9);

        world.Cards.Require("c07").Produced.ShouldBe(3, "间隔 3，9 步出三次");
        world.Cards.Require("c06").Produced.ShouldBe(1, "间隔 9，9 步出一次");
    }

    [Fact]
    public void 产出连续发生_不攒到结算点()
    {
        var world = NewWorld();
        world.Bind("c07", "c05");   // 间隔 3

        // 逐步读那条产线的累计产出：它在第 3、6、9… 步各加一次，
        // 而不是攒到第 60 步结算那一格一次加完。
        var afterEachStep = new System.Collections.Generic.List<double>();
        for (var i = 0; i < 12; i++)
        {
            world.Advance(1);
            afterEachStep.Add(world.Cards.Require("c07").Produced);
        }

        afterEachStep[2].ShouldBe(1, "第 3 步就产出一次");
        afterEachStep[5].ShouldBe(2, "第 6 步再一次");
        afterEachStep[11].ShouldBe(4, "到第 12 步累计 4 次 —— 分四次加出来的，不是第 12 步一次加 4");
    }

    [Fact]
    public void 产出间隔以步计_局内不引入夜与天()
    {
        var world = NewWorld();
        world.Bind("c07", "c05");

        world.Advance(3);

        var snap = world.DebugSnapshot();
        snap.ShouldNotContain("夜", Case.Sensitive, "局内唯一时间单位是步，不引入「夜」「天」这类名目");
        snap.ShouldNotContain("天", Case.Sensitive);
        snap.ShouldContain("every=3", Case.Sensitive, "间隔那一格直接以步计");
    }

    private static MistCityWorld NewWorld() => new(new MistCityConfig());

    private static string CardLine(string snapshot, string id)
    {
        var tokens = snapshot.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < tokens.Length - 1; i++)
        {
            if (tokens[i] != "C" || tokens[i + 1] != id) continue;
            var parts = new System.Collections.Generic.List<string> { id };
            for (var j = i + 2; j < tokens.Length && tokens[j] != "C"; j++) parts.Add(tokens[j]);
            return string.Join(" ", parts);
        }

        throw new ShouldAssertException($"快照里没有牌 {id} 那一行：{snapshot}");
    }
}
