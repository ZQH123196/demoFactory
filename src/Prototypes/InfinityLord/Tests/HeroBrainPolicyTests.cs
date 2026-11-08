#nullable enable
using DemoFactory.Prototypes.InfinityLord.AI;
using DemoFactory.Prototypes.InfinityLord.Logic;
using Shouldly;
using Xunit;

namespace InfinityLord.Tests;

/// <summary>
/// 行为树五条支路的决策测试 —— 被测件是 <see cref="HeroBrainPolicy"/>（纯 C#，零 Godot 依赖，
/// 所以进得了这里；节点 <c>Brain*Task</c> 只搬参数，属表现层，留给 MCP 实测）。
/// 支路之间的**优先级**不在这里断言 —— 那是 <c>SelectorReactive</c> 每步从顶端重估的事，
/// 口径在树上的节点顺序；这里只钉每条「什么情况归我、我说什么」。
///
/// 用例数字取自真游戏录下的行为流（固定种子，见 docs/prototypes/infinity-lord/design.md）：
/// 断言里那些中文文本就是行为流的逐步比对契约 —— 改一个字，两次跑的记录就对不上了。
/// </summary>
public class HeroBrainPolicyTests
{
    private const double RatioBelow = 0.3;
    private const double SafeDistance = 14.0;

    private static HeroBrainPolicy.Senses Senses(
        int health = 60,
        int maxHealth = 60,
        string enemyId = "mob_1",
        float distance = 10f,
        float range = 3f) =>
        new("hero_0", health, maxHealth, enemyId, distance, range);

    // ================= 逃跑（最高优先级） =================

    [Fact]
    public void Flee_血量在阈值之上_不进这条支路()
    {
        HeroBrainPolicy.Outcome outcome = HeroBrainPolicy.Flee(Senses(health: 30), RatioBelow, SafeDistance);

        outcome.Status.ShouldBe(LeafStatus.Failure);
        outcome.Intent.ShouldBe(BrainIntent.None);
        outcome.LogWhat.ShouldBeNull("没决策就不该往行为流里写一行");
    }

    [Fact]
    public void Flee_血低但敌人已拉到安全距离之外_不归这条管了()
    {
        // 曾经这里返回 SUCCESS + Hold（站着），结果 SelectorReactive 每步重估时这条永远赢，
        // 把攻击与追击一直压着 —— 英雄就站在 15 血不动了。现在交给下面的游荡支路。
        HeroBrainPolicy.Outcome outcome =
            HeroBrainPolicy.Flee(Senses(health: 15, distance: 14.5f), RatioBelow, SafeDistance);

        outcome.Status.ShouldBe(LeafStatus.Failure);
        outcome.Intent.ShouldBe(BrainIntent.None);
        outcome.LogWhat.ShouldBeNull();
    }

    [Fact]
    public void Flee_血低且敌人还在身边_继续跑并保持进行中()
    {
        HeroBrainPolicy.Outcome outcome =
            HeroBrainPolicy.Flee(Senses(health: 15, distance: 13.2f), RatioBelow, SafeDistance);

        outcome.Status.ShouldBe(LeafStatus.Running);
        outcome.Intent.ShouldBe(BrainIntent.Flee);
        outcome.TargetId.ShouldBe("mob_1");
        outcome.LogDetail.ShouldBe("还在 13.2 米内，背向 mob_1 挪一步");
    }

    [Fact]
    public void Flee_视野里没有敌人_逃也无从逃起()
    {
        HeroBrainPolicy.Outcome outcome =
            HeroBrainPolicy.Flee(Senses(health: 15, enemyId: "", distance: -1f), RatioBelow, SafeDistance);

        outcome.Status.ShouldBe(LeafStatus.Failure);
        outcome.Intent.ShouldBe(BrainIntent.None);
    }

    [Fact]
    public void Flee_血量上限为零_不做除法()
    {
        HeroBrainPolicy.Outcome outcome =
            HeroBrainPolicy.Flee(Senses(health: 0, maxHealth: 0), RatioBelow, SafeDistance);

        outcome.Status.ShouldBe(LeafStatus.Failure);
    }

    // ================= 游荡（用户确认：安全之后就随机游荡） =================

    [Fact]
    public void HurtWander_血还低但逃跑已经不管了_不接战地走动()
    {
        // 这条支路存在的理由：Attack/Chase 不带血量校验，少了它，受伤的英雄拉开距离后
        // 会被「视野里有敌人」立刻拽回去打。
        HeroBrainPolicy.Outcome outcome = HeroBrainPolicy.HurtWander(Senses(health: 15), RatioBelow);

        outcome.Status.ShouldBe(LeafStatus.Running);
        outcome.Intent.ShouldBe(BrainIntent.Wander);
        outcome.LogWhat.ShouldBe("游荡");
        outcome.LogDetail.ShouldBe("血量 15/60，先不接战，边退边看");
    }

    [Fact]
    public void HurtWander_血不低_让位给攻击与追击()
    {
        HeroBrainPolicy.Outcome outcome = HeroBrainPolicy.HurtWander(Senses(health: 60), RatioBelow);

        outcome.Status.ShouldBe(LeafStatus.Failure);
        outcome.Intent.ShouldBe(BrainIntent.None);
        outcome.LogWhat.ShouldBeNull();
    }

    [Fact]
    public void IdleWander_兜底_没有敌人时也有一行可说()
    {
        HeroBrainPolicy.Outcome outcome = HeroBrainPolicy.IdleWander(Senses(enemyId: "", distance: -1f));

        outcome.Status.ShouldBe(LeafStatus.Running);
        outcome.Intent.ShouldBe(BrainIntent.Wander);
        outcome.LogDetail.ShouldBe("血量 60/60，眼前没有敌人，随便走走");
    }

    // ================= 攻击 =================

    [Fact]
    public void Attack_敌人进射程_下令交战并结束这一步的决断()
    {
        HeroBrainPolicy.Outcome outcome = HeroBrainPolicy.Attack(Senses(distance: 2.5f, range: 3f));

        outcome.Status.ShouldBe(LeafStatus.Success);
        outcome.Intent.ShouldBe(BrainIntent.Engage);
        outcome.TargetId.ShouldBe("mob_1");
        outcome.LogWhat.ShouldBe("攻击");
        outcome.LogDetail.ShouldBe("mob_1 在 3.0 米射程内（实测 2.50）");
    }

    [Fact]
    public void Attack_敌人还在射程外_让位给追击()
    {
        HeroBrainPolicy.Outcome outcome = HeroBrainPolicy.Attack(Senses(distance: 10f, range: 3f));

        outcome.Status.ShouldBe(LeafStatus.Failure);
        outcome.Intent.ShouldBe(BrainIntent.None);
        outcome.LogWhat.ShouldBeNull();
    }

    [Fact]
    public void Attack_射程读英雄本人_不在决策里抄常数()
    {
        HeroBrainPolicy.Outcome outcome = HeroBrainPolicy.Attack(Senses(distance: 7f, range: 8f));

        outcome.Status.ShouldBe(LeafStatus.Success, "远程职业的 8 米射程该打得着 7 米");
        outcome.LogDetail.ShouldBe("mob_1 在 8.0 米射程内（实测 7.00）");
    }

    // ================= 追击（最低优先级） =================

    [Fact]
    public void Chase_够不着但看得见_接近并保持进行中()
    {
        HeroBrainPolicy.Outcome outcome = HeroBrainPolicy.Chase(Senses(distance: 13f, range: 3f));

        outcome.Status.ShouldBe(LeafStatus.Running);
        outcome.Intent.ShouldBe(BrainIntent.Engage, "追击与攻击共用同一个意图");
        outcome.LogWhat.ShouldBe("追击");
        outcome.LogDetail.ShouldBe("朝 mob_1 接近，还差 10.00 米进射程");
    }

    [Fact]
    public void Chase_差多少米按双精度算_与GDScript版末位不分叉()
    {
        // GDScript 的 float 是双精度，桥传过去的 C# float 在那边被无损加宽后再相减；
        // 决策里同样先加宽再减，否则末位会差一格、行为流文本就对不上了。
        HeroBrainPolicy.Outcome outcome = HeroBrainPolicy.Chase(Senses(distance: 10.31f, range: 3f));

        outcome.LogDetail.ShouldBe("朝 mob_1 接近，还差 7.31 米进射程");
    }

    [Fact]
    public void Chase_视野里没有敌人_失败()
    {
        HeroBrainPolicy.Outcome outcome = HeroBrainPolicy.Chase(Senses(enemyId: "", distance: -1f));

        outcome.Status.ShouldBe(LeafStatus.Failure);
        outcome.Intent.ShouldBe(BrainIntent.None);
    }

    // ================= 状态码与树的约定 =================

    [Fact]
    public void 状态码逐字对齐Beehave()
    {
        // BeehaveTask 吃 Godot 进不了测试项目，所以这里钉住数值本身：
        // nodes/beehave_node.gd 的 enum { SUCCESS, FAILURE, RUNNING }。
        ((int)LeafStatus.Success).ShouldBe(0);
        ((int)LeafStatus.Failure).ShouldBe(1);
        ((int)LeafStatus.Running).ShouldBe(2);
    }
}
