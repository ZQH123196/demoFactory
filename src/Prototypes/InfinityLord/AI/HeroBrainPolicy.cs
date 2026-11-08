#nullable enable
using System.Globalization;
using DemoFactory.Prototypes.InfinityLord.Logic;

namespace DemoFactory.Prototypes.InfinityLord.AI;

/// <summary>
/// 行为树五条支路的决策与行为流文本 —— 纯 C#、零 Godot 依赖，所以进得了 <c>dotnet test</c>。
/// 任务节点（<c>Brain*Task.cs</c>）只做参数搬运：读桥上的标量 → 调这里 → 发意图 + 写日志。
///
/// 前三条（逃跑/攻击/追击）从 GDScript 的 <c>brain_{flee,attack,chase}.gd</c> 逐行搬来，
/// 阈值与日志文本一字不改 —— 那是行为树改造第 3 步「C# 版与 GDScript 版逐步全等」的根据。
/// 两条游荡支路是之后补的（用户确认「安全之后就随机游荡」）。
/// 算术一律用 double：GDScript 的 float 就是双精度，桥传过去的 C# float 在那边被无损加宽，
/// 用 float 算会在末位分叉、行为流文本就对不上。
///
/// 优先级靠 <c>SelectorReactive</c> 每步从顶端重估（<c>Selector</c> 不重估，见 `docs/GugaFramework/行为树与跨语言共存.md`），
/// 所以每条支路的返回值就是它的表态：RUNNING=这件事归我、FAILURE=不归我、往下走。
/// </summary>
public static class HeroBrainPolicy
{
    /// <summary>一步里能从桥上读到的全部观测（标量进标量出，跨语言边界只走这些）。</summary>
    public readonly record struct Senses(
        string HeroId,
        int Health,
        int MaxHealth,
        string NearestEnemyId,
        float DistanceToEnemy,
        float AttackRange);

    /// <summary>
    /// 决策结果：交回树的状态 + 写给逻辑层的意图 + 打进 <c>HeroBrain.log_decision</c> 的一行。
    /// 意图为 <see cref="BrainIntent.None"/> 表示这一步不下令；<see cref="LogWhat"/> 为 null 表示不写行为流
    /// （三条支路的失败都是「不归我」，不该占一行）。
    /// </summary>
    public readonly record struct Outcome(
        LeafStatus Status,
        BrainIntent Intent,
        string TargetId,
        string? LogWhat,
        string? LogDetail);

    /// <summary>
    /// 逃跑（最高优先级）：血低于阈值、且敌人还在安全距离之内才归这条管。
    /// **拉开到安全距离就不再是逃跑的事**（返回 FAILURE 让位给下面的支路）—— 早先这里返回
    /// SUCCESS 并下令站着，结果 <c>SelectorReactive</c> 每步重估时这条永远赢、把攻击与追击一直压着，
    /// 他就站在 15 血不动了。
    /// </summary>
    public static Outcome Flee(Senses senses, double ratioBelow, double safeDistance)
    {
        if (!IsHurt(senses, ratioBelow))
        {
            return No();
        }

        if (senses.NearestEnemyId.Length == 0)
        {
            return No();
        }

        double distance = senses.DistanceToEnemy;
        if (distance > safeDistance)
        {
            return No();
        }

        return new Outcome(
            LeafStatus.Running,
            BrainIntent.Flee,
            senses.NearestEnemyId,
            "逃跑",
            Text("还在 {0:0.0} 米内，背向 {1} 挪一步", distance, senses.NearestEnemyId));
    }

    /// <summary>
    /// 伤后游荡：血还低（敌人已经不在安全距离内，否则上面的逃跑先接走）—— 不接战，先走动。
    /// 为什么单列一条：<c>Attack</c>/<c>Chase</c> 不带血量校验，少了这一条，受伤的英雄拉开距离后
    /// 会立刻被「视野里有敌人」重新拽回去打。
    /// </summary>
    public static Outcome HurtWander(Senses senses, double ratioBelow)
    {
        if (!IsHurt(senses, ratioBelow))
        {
            return No();
        }

        return WanderOutcome(senses, "先不接战，边退边看");
    }

    /// <summary>攻击：敌人进了射程就下令交战。逻辑层的 Engage 是「射程内冷却好了就打」，
    /// 所以这里返回 SUCCESS 表示「这一步的决断完成了」，下一步由 SelectorReactive 重新评估。</summary>
    public static Outcome Attack(Senses senses)
    {
        if (senses.NearestEnemyId.Length == 0)
        {
            return No();
        }

        double distance = senses.DistanceToEnemy;
        double range = senses.AttackRange;
        if (distance > range)
        {
            return No();
        }

        return new Outcome(
            LeafStatus.Success,
            BrainIntent.Engage,
            senses.NearestEnemyId,
            "攻击",
            Text("{0} 在 {1:0.0} 米射程内（实测 {2:0.00}）", senses.NearestEnemyId, range, distance));
    }

    /// <summary>追击：视野里有敌人但够不着，下令交战 —— 逻辑层的 Engage 自带
    /// 「射程外先走位接近」，所以追击与攻击共用同一个意图，区别只在树上记下的决策名。</summary>
    public static Outcome Chase(Senses senses)
    {
        if (senses.NearestEnemyId.Length == 0)
        {
            return No();
        }

        return new Outcome(
            LeafStatus.Running,
            BrainIntent.Engage,
            senses.NearestEnemyId,
            "追击",
            Text(
                "朝 {0} 接近，还差 {1:0.00} 米进射程",
                senses.NearestEnemyId,
                (double)senses.DistanceToEnemy - senses.AttackRange));
    }

    /// <summary>
    /// 无事游荡（兜底，最低优先级）：前面四条都不归管 —— 视野里没有敌人、血也还行，那就随便走走。
    /// 与 <see cref="HurtWander"/> 同一个意图，差别只在行为流里说清「为什么在逛」。
    /// </summary>
    public static Outcome IdleWander(Senses senses) => WanderOutcome(senses, "眼前没有敌人，随便走走");

    private static Outcome WanderOutcome(Senses senses, string why) =>
        new(
            LeafStatus.Running,
            BrainIntent.Wander,
            string.Empty,
            "游荡",
            Text("血量 {0}/{1}，{2}", senses.Health, senses.MaxHealth, why));

    /// <summary>血低到阈值以下（上限取不到时当作没受伤 —— 别拿零做除数）。</summary>
    private static bool IsHurt(Senses senses, double ratioBelow) =>
        senses.MaxHealth > 0 && (double)senses.Health / senses.MaxHealth < ratioBelow;

    private static Outcome No() =>
        new(LeafStatus.Failure, BrainIntent.None, string.Empty, null, null);

    /// <summary>行为流文本一律用不变文化：跟着系统区域设置走会让同一局在别的机器上录出别的样子。</summary>
    private static string Text(string format, params object[] args) =>
        string.Format(CultureInfo.InvariantCulture, format, args);
}

/// <summary>
/// 交回树的状态。数值逐字对齐 Beehave 的 <c>enum { SUCCESS, FAILURE, RUNNING }</c>
/// （<c>beehave_node.gd</c>）与 <c>BeehaveTask</c> 上的同名常量 —— 那边吃 Godot、这边不能吃，
/// 所以两处各写一份，改一处必须改另一处。
/// </summary>
public enum LeafStatus
{
    Success = 0,
    Failure = 1,
    Running = 2,
}
