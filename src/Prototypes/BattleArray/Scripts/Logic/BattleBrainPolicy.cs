#nullable enable
namespace DemoFactory.Prototypes.BattleArray.Logic;

/// <summary>
/// 一个兵每一步的决策 —— 「打最近」这条唯一规则的可拆出来的那一半。
///
/// 为什么单独立一个文件：M5 要把单位挂上行为树（Beehave），而 AGENTS 的分工写死了
/// **决策与规则必须是零 Godot 依赖的纯 C#**（否则进不了 <c>dotnet test</c>，
/// M4 那条判绿就不再可复现）。树只负责「什么时候问哪条支路、参数从哪个场景档取」，
/// 规则本身在这里 —— 逻辑世界离线跑、运行时挂树，两边调用的是同一个函数。
///
/// 规则没变（design.md 2 节 第 3 条 + 5.2 节）：只打最近的活人、只平A、够不着就赶路。
/// 本文件是 <c>BattleWorld.Step</c> 决策阶段的原样搬出，不是第二套实现：
/// 搬完之后 <c>DebugSnapshot()</c> 的 <c>why=</c> 那句也改读这里，
/// 「显示即真相」要求只有一份实现。
/// </summary>
public static class BattleBrainPolicy
{
    /// <summary>这一步要世界去做的事。目标选择（打最近）不在这里 —— 那是世界的账，M7 要改再搬。</summary>
    public enum Intent
    {
        /// <summary>站着不动（对面没人，或冷却还没好）。</summary>
        Hold,

        /// <summary>朝目标赶路。</summary>
        CloseIn,

        /// <summary>出手。</summary>
        Strike,
    }

    /// <summary>
    /// 决策的输入 —— 全是标量，由调用方（世界或桥）算好递进来。
    /// <paramref name="Reach"/> ＝ 射程 ＋ 双方身体半径（够得着的判距，见 <see cref="TroopCard.ReachAgainst"/>）。
    /// </summary>
    public readonly record struct Senses(
        bool HasTarget,
        float Distance,
        float Reach,
        int CooldownRemaining);

    /// <summary>
    /// 决策的输出。<paramref name="Why"/> 是给「谁为什么不动」看的那句话，
    /// 快照的 <c>why=</c> 与行为流的行为档共用它 —— 一份实现，两处读。
    /// </summary>
    public readonly record struct Decision(Intent Intent, string Why);

    /// <summary>本步可出手（冷却已清零）时给出的出手决策。</summary>
    public static Decision Strike() => new(Intent.Strike, "在射程内、本步出手");

    /// <summary>
    /// 走一步决策。四档规则与 M4 的结算逐字一致：
    /// 对面没人 → 站着；够不着 → 赶路；够得着但冷却没好 → 站着等；否则 → 出手。
    /// </summary>
    public static Decision Decide(Senses senses)
    {
        if (!senses.HasTarget) return new Decision(Intent.Hold, "对面已无人");
        var gap = senses.Distance - senses.Reach;
        if (gap > 0f) return new Decision(Intent.CloseIn, $"赶路（还差 {F2(gap)} 米）");
        if (senses.CooldownRemaining > 0)
            return new Decision(Intent.Hold, $"在射程内、冷却剩 {senses.CooldownRemaining} 步");
        return Strike();
    }

    private static string F2(float v) => v.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
}
