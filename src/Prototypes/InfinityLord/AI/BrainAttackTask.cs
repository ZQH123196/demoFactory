#nullable enable
using Godot;

namespace DemoFactory.Prototypes.InfinityLord.AI;

/// <summary>
/// 攻击 —— GDScript 版 <c>brain_attack.gd</c> 的 C# 对照件，决策在
/// <see cref="HeroBrainPolicy.Attack"/>。射程读英雄本人的 AttackRange，不在这里抄常数。
/// </summary>
[GlobalClass]
public partial class BrainAttackTask : HeroBrainTask
{
    protected override HeroBrainPolicy.Outcome Decide(HeroBrainPolicy.Senses senses) =>
        HeroBrainPolicy.Attack(senses);
}
