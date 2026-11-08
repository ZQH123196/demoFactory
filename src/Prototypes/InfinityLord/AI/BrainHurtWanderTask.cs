#nullable enable
using Godot;

namespace DemoFactory.Prototypes.InfinityLord.AI;

/// <summary>
/// 伤后游荡 —— 血还低、但敌人已经不在安全距离内（否则上面的逃跑先接走），于是不接战、先走动。
/// 决策在 <see cref="HeroBrainPolicy.HurtWander"/>。
/// </summary>
[GlobalClass]
public partial class BrainHurtWanderTask : HeroBrainTask
{
    /// <summary>
    /// 与 <see cref="BrainFleeTask.RatioBelow"/> 同值同义：这两条支路合起来才是
    /// 「血低就逃、拉开就逛」，改一处必须改另一处，否则中间会裂开一条谁都不管的缝。
    /// </summary>
    [Export]
    public double RatioBelow { get; set; } = 0.3;

    protected override HeroBrainPolicy.Outcome Decide(HeroBrainPolicy.Senses senses) =>
        HeroBrainPolicy.HurtWander(senses, RatioBelow);
}
