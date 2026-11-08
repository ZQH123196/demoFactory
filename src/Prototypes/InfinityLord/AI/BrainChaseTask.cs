#nullable enable
using Godot;

namespace DemoFactory.Prototypes.InfinityLord.AI;

/// <summary>
/// 追击（最低优先级）—— GDScript 版 <c>brain_chase.gd</c> 的 C# 对照件，决策在
/// <see cref="HeroBrainPolicy.Chase"/>。与攻击共用 Engage 意图，区别只在行为流里记的决策名。
/// </summary>
[GlobalClass]
public partial class BrainChaseTask : HeroBrainTask
{
    protected override HeroBrainPolicy.Outcome Decide(HeroBrainPolicy.Senses senses) =>
        HeroBrainPolicy.Chase(senses);
}
