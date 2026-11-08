#nullable enable
using DemoFactory.Prototypes.BattleArray.Logic;
using Godot;

namespace DemoFactory.Prototypes.BattleArray.AI;

/// <summary>
/// 赶路（树的第二条支路）—— 有目标但够不着。
/// 「往哪儿走、走多远」仍归世界算（那是位移结算，不是决策），这里只交回 CloseIn 这个意图。
/// </summary>
[GlobalClass]
public partial class BattleCloseInTask : BattleBrainTask
{
    protected override BattleBrainPolicy.Intent Owns => BattleBrainPolicy.Intent.CloseIn;
}
