#nullable enable
using DemoFactory.Prototypes.BattleArray.Logic;
using Godot;

namespace DemoFactory.Prototypes.BattleArray.AI;

/// <summary>
/// 出手（树的第一条支路）—— 够得着且冷却好了才归它管。
/// 规则住在 <see cref="BattleBrainPolicy"/>，本文件只声明「我管 Strike 这一档」。
/// </summary>
[GlobalClass]
public partial class BattleStrikeTask : BattleBrainTask
{
    protected override BattleBrainPolicy.Intent Owns => BattleBrainPolicy.Intent.Strike;
}
