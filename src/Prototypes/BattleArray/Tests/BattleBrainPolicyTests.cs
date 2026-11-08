#nullable enable
using System.Collections.Generic;
using System.Linq;
using DemoFactory.Prototypes.BattleArray.Logic;
using Shouldly;
using Xunit;

namespace BattleArray.Tests;

/// <summary>
/// 决策规则本体 —— M5-c 把 <c>BattleWorld.Step</c> 的决策阶段搬进
/// <see cref="BattleBrainPolicy"/> 之后，这条线必须能**脱离引擎单独问**
/// （AGENTS 分工表：决策与规则＝零 Godot 依赖的纯 C#，否则进不了 dotnet test）。
///
/// 压的是四档规则本身，以及「搬出来没改行为」——
/// 后者由既有的机制台账兜着（近战 1v1 首刀第 43 步、13 刀第 163 步那两条），这里不重复算账。
/// </summary>
public class BattleBrainPolicyTests
{
    private static BattleBrainPolicy.Senses At(float distance, float reach, int cooldown = 0)
        => new(true, distance, reach, cooldown);

    [Fact]
    public void 对面没人_站着()
    {
        var decision = BattleBrainPolicy.Decide(new BattleBrainPolicy.Senses(false, 0f, 0f, 0));
        decision.Intent.ShouldBe(BattleBrainPolicy.Intent.Hold);
        decision.Why.ShouldBe("对面已无人");
    }

    [Fact]
    public void 够不着_赶路_并报出还差几米()
    {
        var decision = BattleBrainPolicy.Decide(At(distance: 12.5f, reach: 2.8f));
        decision.Intent.ShouldBe(BattleBrainPolicy.Intent.CloseIn);
        decision.Why.ShouldBe("赶路（还差 9.70 米）", "快照的 why= 与行为流读的是同一句话");
    }

    [Fact]
    public void 刚好够得着_不許再赶路()
    {
        // 判距含双方身体半径（design.md「兵种数据表 v1」）：距离 == 射程 + 两半径 ⇒ 已经够得着。
        // 这一格压的是「<=」还是「<」——写反了近战就会原地踏步，M4 的时长账全变。
        BattleBrainPolicy.Decide(At(2.8f, 2.8f)).Intent.ShouldBe(BattleBrainPolicy.Intent.Strike);
        BattleBrainPolicy.Decide(At(2.81f, 2.8f)).Intent.ShouldBe(BattleBrainPolicy.Intent.CloseIn);
    }

    [Fact]
    public void 在射程内但冷却没好_站着等_并报出剩几步()
    {
        var decision = BattleBrainPolicy.Decide(At(2.0f, 2.8f, cooldown: 3));
        decision.Intent.ShouldBe(BattleBrainPolicy.Intent.Hold);
        decision.Why.ShouldBe("在射程内、冷却剩 3 步");
    }

    [Fact]
    public void 四档之外没有第五档_每条支路都有人管()
    {
        // 行为树的选择器按意图分派三条支路（Strike/CloseIn/Hold）；
        // 规则若哪天冒出第四种意图，树没挂对应叶子就会静默什么都不做 —— 这条测试先把「只有三种」钉住。
        var intents = new HashSet<BattleBrainPolicy.Intent>();
        for (var distance = 0f; distance <= 12f; distance += 0.25f)
        {
            for (var cooldown = 0; cooldown <= 3; cooldown++)
            {
                intents.Add(BattleBrainPolicy.Decide(At(distance, 2.8f, cooldown)).Intent);
                intents.Add(BattleBrainPolicy.Decide(new BattleBrainPolicy.Senses(false, distance, 2.8f, cooldown)).Intent);
            }
        }

        intents.OrderBy(i => i.ToString()).ShouldBe(new[] { BattleBrainPolicy.Intent.CloseIn, BattleBrainPolicy.Intent.Hold, BattleBrainPolicy.Intent.Strike }
                .OrderBy(i => i.ToString()), false, "决策意图集合变了：要么加了三条支路（记得给树挂叶子），要么少了一条（有支路永不认账）");
    }

    [Fact]
    public void 世界走的确实是这一份规则_快照的why与决策同句()
    {
        // 「显示即真相只有一份实现」：快照里那句 why= 不许另算一套。
        // 压法：跑一局，把每个活人的 why= 与「用同一份观测问一次规则」的结果对齐。
        using var world = new BattleWorld(1001, Formations.ShieldWall, PlacementComparison.EnemyFormation);
        world.Advance(150);

        // 快照体 = 头部一行 + 每个兵一行（出场次序）+ 各层自己那几段（带 [层名] 抬头）。
        // 实体层那一段是 2026-10-07 迁册子加进来的（D13），所以「后头那些段」现在不再为空 ——
        // 逐兵那几行仍必须紧跟头部、仍按出场次序、仍一个不少，层段往后排。
        var body = world.DebugSnapshot().Split('\n').Skip(1).ToList();
        var unitRows = body.Take(world.Units.Count).ToList();
        unitRows.Count.ShouldBe(world.Units.Count, "逐兵那几行一个都不能少");
        body.Skip(world.Units.Count).First().ShouldStartWith("[", Case.Sensitive,
            "逐兵那几行之后紧接的必须是某一层的抬头（[层名]）—— 否则有一行既不是兵也不是层，形状混了");
        for (var i = 0; i < world.Units.Count; i++)
        {
            var unit = world.Units[i];
            var line = unitRows[i];
            line.ShouldStartWith(unit.Id + " ", Case.Sensitive, $"第 {i} 行念的兵号与出场次序对不上");
            var why = line[(line.IndexOf("why=", System.StringComparison.Ordinal) + 4)..];
            if (!unit.Alive)
            {
                why.ShouldStartWith("阵亡", Case.Sensitive, $"{unit.Id} 死了却报不出死因");
                continue;
            }

            var expected = BattleBrainPolicy.Decide(world.SensesOf(unit, world.Find(unit.TargetId))).Why;
            why.ShouldBe(expected, $"{unit.Id} 的 why= 与规则给出的不是同一句");
        }
    }
}
