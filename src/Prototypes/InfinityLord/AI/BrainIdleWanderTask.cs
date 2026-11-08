#nullable enable
using Godot;

namespace DemoFactory.Prototypes.InfinityLord.AI;

/// <summary>
/// 无事游荡（兜底，树的最下一条支路）—— 前面四条都不归管时随便走走。
/// 决策在 <see cref="HeroBrainPolicy.IdleWander"/>。
/// </summary>
[GlobalClass]
public partial class BrainIdleWanderTask : HeroBrainTask
{
    protected override HeroBrainPolicy.Outcome Decide(HeroBrainPolicy.Senses senses) =>
        HeroBrainPolicy.IdleWander(senses);
}
