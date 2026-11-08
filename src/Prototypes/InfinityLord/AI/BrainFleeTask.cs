#nullable enable
using Godot;

namespace DemoFactory.Prototypes.InfinityLord.AI;

/// <summary>
/// 逃跑（最高优先级）—— GDScript 版 <c>brain_flee.gd</c> 的 C# 对照件，决策在
/// <see cref="HeroBrainPolicy.Flee"/>，这里只搬参数与两个可调阈值。
/// </summary>
[GlobalClass]
public partial class BrainFleeTask : HeroBrainTask
{
    /// <summary>血量比例低于这个值才进逃跑支路。</summary>
    [Export]
    public double RatioBelow { get; set; } = 0.3;

    /// <summary>拉开到这么远就算脱险（米）。</summary>
    [Export]
    public double SafeDistance { get; set; } = 14.0;

    protected override HeroBrainPolicy.Outcome Decide(HeroBrainPolicy.Senses senses) =>
        HeroBrainPolicy.Flee(senses, RatioBelow, SafeDistance);
}
