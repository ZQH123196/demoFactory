#nullable enable
using DemoFactory.Prototypes.BattleArray.Logic;
using Godot;

namespace DemoFactory.Prototypes.BattleArray.AI;

/// <summary>
/// 站着（树的兜底支路，选择器的最后一条）—— 对面没人，或在射程内等冷却。
/// 规则同样住在 <see cref="BattleBrainPolicy"/>；它排在最后，前面两条都不归管时才轮得到。
/// </summary>
[GlobalClass]
public partial class BattleHoldTask : BattleBrainTask
{
    protected override BattleBrainPolicy.Intent Owns => BattleBrainPolicy.Intent.Hold;
}
