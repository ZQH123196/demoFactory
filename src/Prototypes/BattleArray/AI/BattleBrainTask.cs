#nullable enable
using DemoFactory.Prototypes.BattleArray.Logic;
using Godot;

namespace DemoFactory.Prototypes.BattleArray.AI;

/// <summary>
/// 排兵布阵 C# 任务叶子的共同搬运层 —— 从宿主取桥、读一步观测、问一次纯 C# 规则、
/// 归自己管的那条支路就认账（交回桥并返回 SUCCESS），不归自己管就 FAILURE 让选择器往下走。
///
/// 两条口径写死在这里：
/// 1. **规则不在叶子里**。出手/赶路/站着只有一份实现：<see cref="BattleBrainPolicy"/>。
///    叶子若自己写一遍 <c>if (distance &lt;= range)</c>，离线测试与运行时就会长成两套真值
///    （AGENTS「显示即真相只有一份实现」）。
/// 2. **参数从场景档来**。射程可以按兵种覆盖（宿主上的 <c>range_override</c>），
///    没覆盖（-1）就用兵种卡的射程 —— 「同一棵树、不同 .tscn 参数」那条路走的是这个口子，
///    有测试逐字核对场景里的数与 <c>UnitCards</c> 不许漂（漂了就是两处真值）。
///
/// 架构位置：AI —— 行为树任务节点（吃 Godot 类型，不进 dotnet test；决策在 Logic 那边进）。
/// </summary>
public abstract partial class BattleBrainTask : GugaFramework.Beehave.BeehaveTask
{
    /// <summary>本条支路管的意图（选择器按这个决定谁认账）。</summary>
    protected abstract BattleBrainPolicy.Intent Owns { get; }

    protected sealed override int Tick(Node actor, Node blackboard)
    {
        // 桥挂在宿主（AI/battle_brain.gd）上，由表现层挂树时 Set("bridge", ...) 注入。
        // 刻意不用 [Export] 节点引用：手写 .tscn 里的节点引用取回来是 null（实测，
        // 记在 docs/GugaFramework/行为树与跨语言共存.md第 5 条）。
        if (actor.Get("bridge").As<BattleBrainBridge>() is not { } bridge) return Failure;

        var decision = BattleBrainPolicy.Decide(SensesFor(bridge, actor));
        if (decision.Intent != Owns) return Failure;

        bridge.Record(decision);
        return Success;
    }

    /// <summary>把宿主这一档的射程参数并进观测里（其余标量是世界算好的原样）。</summary>
    private static BattleBrainPolicy.Senses SensesFor(BattleBrainBridge bridge, Node actor)
    {
        var senses = new BattleBrainPolicy.Senses(
            bridge.SenseHasTarget(),
            bridge.SenseDistance(),
            bridge.SenseCardReach(),
            bridge.SenseCooldown());

        var rangeOverride = actor.Get("range_override").AsSingle();
        if (rangeOverride < 0f || !senses.HasTarget) return senses;

        // 覆盖的是「射程」这一格，身体半径照旧相加 —— 与 TroopCard.ReachAgainst 同一个算法。
        var reach = rangeOverride + bridge.SenseSelfRadius() + bridge.SenseTargetRadius();
        return senses with { Reach = reach };
    }
}
