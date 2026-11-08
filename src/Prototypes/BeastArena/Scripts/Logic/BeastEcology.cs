#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using DemoFactory.GugaFramework.Pathfinding;

namespace DemoFactory.Prototypes.BeastArena.Logic;

// 巨兽生态那一件：饱食度、消化进度、生态对象、自主觅食的靶子与冷却、魔晶产出、形态解锁计数。
//
// **为什么单独成件**：饱食度原来住在 BeastVoyage（航行那一摊），而「吃什么、吃了多少、
// 消化出几颗魔晶」与航线无关。切开之后 Voyage 只管怎么走，这一件只管吃与消化 ——
// 同一份饱食度只有这一处真值，Voyage 的读数转发到这里，世界不再存第二份。
//
// **时间量按逻辑步**：本件不读墙钟，所有「几游戏小时」都由调用方递 tick 换算
// （<see cref="ArenaTime.StepsPerGameHour"/> / <see cref="ArenaTime.StepsPerGameMinute"/>）。

/// <summary>生态对象的三类。</summary>
public enum EcoKind
{
    /// <summary>树木：饱食 +20、消化 +20，吞食后 12 游戏小时原位再生。</summary>
    Tree = 0,

    /// <summary>兽群：饱食 +40、消化 +40，吞食后 24 游戏小时原位再生（节点上三只兽）。</summary>
    Herd = 1,

    /// <summary>矿石：饱食 +10、消化 +60，不再生。</summary>
    Ore = 2,
}

/// <summary>
/// 「附近没有可吞对象」放弃觅食的封闭原因（规格「事件规则」：每个拒绝都带枚举，不留自由文本）。
/// </summary>
public enum ForageGiveUpReason
{
    /// <summary>感知半径（30 米）内没有可吞的生态对象。</summary>
    NoTargetInRange = 0,

    /// <summary>吃完一口后周围已经没有可吞的生态对象了。</summary>
    NoTargetLeft = 1,
}

/// <summary>放弃原因 → 横幅中文文案（唯一出处；事件载荷用枚举，文案只在这一处派生）。</summary>
public static class ForageGiveUpText
{
    /// <summary>这一档原因对玩家的中文说法。</summary>
    public static string TextOf(ForageGiveUpReason reason) => reason switch
    {
        ForageGiveUpReason.NoTargetInRange => "附近 30 米没有可吞的生态对象，返航（一游戏小时内不再自动尝试）",
        _ => "周围没有可吞的生态对象了，返航（一游戏小时内不再自动尝试）",
    };
}

/// <summary>大地图上摆一个生态对象用的规格（位置即世界米）。</summary>
/// <param name="Id">稳定 id（快照与动词都读它）。</param>
/// <param name="Kind">三类之一。</param>
/// <param name="Position">大地图逻辑坐标。</param>
public readonly record struct EcoNodeSpec(string Id, EcoKind Kind, NavPoint Position);

/// <summary>
/// 一次吞食的结果 —— <b>按实际吃进量记</b>（饱食度封顶时饱食那一半可能是 0）。
/// </summary>
/// <param name="NodeId">吃的是哪一个对象。</param>
/// <param name="Kind">对象类别。</param>
/// <param name="Auto">自主觅食吃的还是玩家指定吃的。</param>
/// <param name="SatiationGain">实际补进饱食度的点数。</param>
/// <param name="DigestionGain">记进消化进度的点数。</param>
/// <param name="CrystalsProduced">这一口跨过 100 的整倍数之后产出的魔晶数（溢出保留）。</param>
public readonly record struct EatResult(
    string NodeId,
    EcoKind Kind,
    bool Auto,
    double SatiationGain,
    double DigestionGain,
    int CrystalsProduced);

/// <summary>生态对象的那一份活状态（类别与位置只读，可吞状态按 tick 现算）。</summary>
public sealed class EcoNode
{
    internal EcoNode(EcoNodeSpec spec)
    {
        Id = spec.Id;
        Kind = spec.Kind;
        Position = spec.Position;
    }

    /// <summary>稳定 id。</summary>
    public string Id { get; }

    /// <summary>类别。</summary>
    public EcoKind Kind { get; }

    /// <summary>世界米坐标。</summary>
    public NavPoint Position { get; }

    /// <summary>哪一步起重新可吞（矿石不再生，吃掉之后置 <see cref="Consumed"/>）。</summary>
    public long AvailableAtTick { get; internal set; }

    /// <summary>矿石吃完就永久离场（树木与兽群靠 <see cref="AvailableAtTick"/> 再生）。</summary>
    public bool Consumed { get; internal set; }

    /// <summary>这一步可吞没有。</summary>
    public bool AvailableAt(long tick) => !Consumed && tick >= AvailableAtTick;

    /// <summary>
    /// 兽群节点上还剩几只 —— <b>逻辑侧只记这一档读数</b>，剪影怎么区分是资产那一票的事。
    /// 树木与矿石恒 0。
    /// </summary>
    public int HerdRemaining(long tick)
        => Kind == EcoKind.Herd && AvailableAt(tick) ? ArenaTuning.HerdSilhouetteCount : 0;

    /// <summary>类别的小写 token（快照用）。</summary>
    public string KindText => Kind switch
    {
        EcoKind.Tree => "tree",
        EcoKind.Herd => "herd",
        _ => "ore",
    };
}

/// <summary>
/// 巨兽生态状态机：饱食度、消化进度、生态对象与魔晶产出。
///
/// <para><b>能脱离世界单独构造与推进</b>：不持有 <see cref="ArenaWorld"/>，也不读墙钟。
/// 调用方递 tick、递「这一步在不在沙漠带」的形态结果，本件只负责状态与事件。</para>
///
/// <para><b>魔晶只在这里记账</b>：产出数（<see cref="CrystalsProduced"/>）与消化溢出
/// （<see cref="DigestionOverflow"/>）同源，世界里那一批点位物只是它的表现 ——
/// 落盘、搬运、入库、出售各有一本账，但「产出几颗」不另存第二份。</para>
/// </summary>
public sealed partial class BeastEcology
{
    private readonly List<EcoNode> _nodes = [];
    private long _lastDecayTick = -1;

    /// <summary>按一份摆位表建生态。</summary>
    public BeastEcology(IEnumerable<EcoNodeSpec> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        foreach (var spec in nodes) _nodes.Add(new EcoNode(spec));
    }

    /// <summary>所有生态对象（按摆位表的固定次序；快照与选最近都读这一份）。</summary>
    public IReadOnlyList<EcoNode> Nodes => _nodes;

    /// <summary>饱食度，0..100，开局 <see cref="ArenaTuning.StartingSatiation"/>。</summary>
    public double Satiation { get; private set; } = ArenaTuning.StartingSatiation;

    /// <summary>累计消化进度（点）。每满 100 产出一单位魔晶，溢出保留。</summary>
    public double DigestionProgress { get; private set; }

    /// <summary>已经产出过几单位魔晶（只增）。</summary>
    public int CrystalsProduced { get; private set; }

    /// <summary>消化进度里还没凑满一单位的溢出部分。</summary>
    public double DigestionOverflow => DigestionProgress - (CrystalsProduced * ArenaTuning.DigestionPerCrystal);

    /// <summary>饥饿衰减倍率（形态那一侧每一步现算递进来）。</summary>
    public double HungerDecayFactor { get; private set; } = 1.0;

    /// <summary>自主觅食的冷却到期步号（0 = 没在冷却）。</summary>
    public long ForageCooldownUntilTick { get; private set; }

    /// <summary>上一次「附近没有可吞对象」的原因（给横幅与快照读）。</summary>
    public string ForageGaveUpReason { get; private set; } = string.Empty;

    /// <summary>吞食完成时抛一次（自主与手动都算）。</summary>
    public event Action<EatResult>? EatCompleted;

    /// <summary>每产出 <c>count</c> 单位魔晶抛一次（世界据此在甲板落点位物）。</summary>
    public event Action<int>? CrystalsSpawned;

    /// <summary>附近没有可吞对象、进入一游戏小时冷却时抛一次。</summary>
    public event Action<ForageGiveUpReason>? ForageGaveUp;

    /// <summary>现在是不是在「无目标返航」的一游戏小时冷却里。</summary>
    public bool InForageCooldownAt(long tick) => tick < ForageCooldownUntilTick;

    /// <summary>写饥饿衰减倍率（形态那一侧每一步现算）。</summary>
    public void SetHungerDecayFactor(double factor)
    {
        if (factor <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(factor), factor, "饥饿衰减倍率不能是零或负数");
        }

        HungerDecayFactor = factor;
    }

    /// <summary>
    /// 每满一个游戏小时衰减 <see cref="ArenaTuning.SatiationDecayPerGameHour"/> × 倍率。
    /// <b>钳到 0，不饿死</b>：巨兽没有死亡态，饿到底只是移速打折。
    /// </summary>
    public void DecayHourly(long tick)
    {
        if (tick <= 0 || tick % ArenaTime.StepsPerGameHour != 0) return;
        if (tick == _lastDecayTick) return;

        _lastDecayTick = tick;
        Satiation = Math.Max(0.0, Satiation - (ArenaTuning.SatiationDecayPerGameHour * HungerDecayFactor));
    }

    /// <summary>
    /// 吃一个生态对象：同时结算饱食度与消化进度，并按 100 点一单位产出魔晶。
    ///
    /// <para><b>形态那一档只改收益，不改节点本身</b>：荒漠态吃矿石饱食 ×2、其他 ×0.5，
    /// 消化不变；暴食态饱食与消化都 ×1.5。</para>
    ///
    /// <para><b>不可吞就整发不受理</b>（返回 false）：调用方要先判 <see cref="EcoNode.AvailableAt"/>，
    /// 不要靠「吃了没效果」来当拒绝 —— 那样节点会被白标成已吃。</para>
    /// </summary>
    public bool TryEat(string nodeId, long tick, BeastForm form, bool auto, out EatResult result)
    {
        result = default;
        var node = Find(nodeId);
        if (node is null || !node.AvailableAt(tick)) return false;

        var (baseSatiation, baseDigestion) = BaseGainsOf(node.Kind);
        var satiationGain = baseSatiation * SatiationFactorOf(node.Kind, form);
        var digestionGain = baseDigestion * (form == BeastForm.Glutton ? ArenaTuning.GluttonFormEatFactor : 1.0);

        // 饱食度封顶：实际补进去的才是「有效恢复」，多出来的不记。
        var appliedSatiation = Math.Min(satiationGain, ArenaTuning.SatiationMax - Satiation);
        Satiation += appliedSatiation;
        DigestionProgress += digestionGain;

        // 每 100 点一单位，溢出保留。用「总进度算应得 − 已产出」而不是逐次相减，
        // 一次吃进 60 再吃 60 也不会因为浮点尾数少一颗。
        var shouldHave = (int)Math.Floor((DigestionProgress / ArenaTuning.DigestionPerCrystal) + 1e-9);
        var produced = Math.Max(0, shouldHave - CrystalsProduced);
        CrystalsProduced += produced;

        MarkEaten(node, tick);

        result = new EatResult(node.Id, node.Kind, auto, appliedSatiation, digestionGain, produced);
        EatCompleted?.Invoke(result);
        if (produced > 0) CrystalsSpawned?.Invoke(produced);
        return true;
    }

    /// <summary>
    /// 手动喂食：食物换饱食度（每 <see cref="ArenaTuning.FoodPerSatiation"/> 份换 1 点），封顶 100。
    /// </summary>
    /// <returns>实际补进饱食度的点数（0 = 一口也没吃进去）。</returns>
    public double Feed(double food)
    {
        if (food <= 0.0) return 0.0;

        var points = food / ArenaTuning.FoodPerSatiation;
        var applied = Math.Min(points, ArenaTuning.SatiationMax - Satiation);
        if (applied <= 0.0) return 0.0;

        Satiation += applied;
        return applied;
    }

    /// <summary>调试直写口：把饱食度摆到某个数（真跑要几十游戏小时，测试等不起）。</summary>
    public void SetSatiation(double value)
    {
        if (value < 0.0 || value > ArenaTuning.SatiationMax)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value,
                $"饱食度取 0..{ArenaTuning.SatiationMax.ToString(CultureInfo.InvariantCulture)}");
        }

        Satiation = value;
    }

    /// <summary>
    /// 这一口按当前形态会补多少饱食、记多少消化 —— <b>只读预览，不动世界</b>。
    /// 面板据此把「饱食收益 / 消化收益」写在目标行上，与 <see cref="TryEat"/> 走同一份表。
    /// </summary>
    public static (double Satiation, double Digestion) GainsOf(EcoKind kind, BeastForm form)
    {
        var (baseSatiation, baseDigestion) = BaseGainsOf(kind);
        return (baseSatiation * SatiationFactorOf(kind, form),
            baseDigestion * (form == BeastForm.Glutton ? ArenaTuning.GluttonFormEatFactor : 1.0));
    }

    /// <summary>按 id 找一个生态对象（不可吞也返回；查不到回 null）。</summary>
    public EcoNode? Find(string nodeId)
    {
        foreach (var node in _nodes)
        {
            if (node.Id == nodeId) return node;
        }

        return null;
    }

    /// <summary>
    /// 半径内最近的可吞对象（平手取摆位表里靠前的那个）。
    ///
    /// <para><b>遍历序即摆位次序</b>：重放要逐字可复现，不能靠「哪一个先被找到」这种运行期偶然。</para>
    /// </summary>
    public EcoNode? NearestEdible(NavPoint at, double radius, long tick)
    {
        EcoNode? best = null;
        var bestDistance = double.MaxValue;

        foreach (var node in _nodes)
        {
            if (!node.AvailableAt(tick)) continue;

            var distance = at.DistanceTo(node.Position);
            if (distance > radius) continue;
            if (distance >= bestDistance) continue;

            bestDistance = distance;
            best = node;
        }

        return best;
    }

    /// <summary>
    /// 无目标返航：记一游戏小时冷却，并给出读得懂的原因。
    /// <b>已在冷却里就不重复发事件</b>（每一步都喊一次会刷屏，也会把冷却往后推）。
    /// </summary>
    public void NoteForageGaveUp(long tick, ForageGiveUpReason reason)
    {
        if (InForageCooldownAt(tick)) return;

        ForageCooldownUntilTick = tick
            + (ArenaTime.StepsPerGameMinute * ArenaTuning.ForageCooldownGameMinutes);
        ForageGaveUpReason = ForageGiveUpText.TextOf(reason);
        ForageGaveUp?.Invoke(reason);
    }

    /// <summary>
    /// 玩家召回之后压住自动觅食一游戏小时：否则兽刚被叫回航线，下一步又因为还饿着
    /// 立刻脱队 —— 召回那一发等于没发。这一档不写「无目标」原因，也不发那条事件。
    /// </summary>
    public void SuppressForage(long tick)
    {
        if (InForageCooldownAt(tick)) return;

        ForageCooldownUntilTick = tick
            + (ArenaTime.StepsPerGameMinute * ArenaTuning.ForageCooldownGameMinutes);
    }

    /// <summary>清掉冷却与原因（换局 / 停靠时由调用方决定要不要清）。</summary>
    public void ClearForageCooldown()
    {
        ForageCooldownUntilTick = 0;
        ForageGaveUpReason = string.Empty;
    }

    /// <summary>状态可查那一口：每个生态对象一段（段首 <c>ECO &lt;id&gt;</c> 是身份）。</summary>
    public IReadOnlyList<string> DebugSegments(long tick)
    {
        var lines = new List<string>(_nodes.Count);
        foreach (var node in _nodes) lines.Add(DebugSegmentOf(node, tick));
        return lines;
    }

    /// <summary>单个生态对象那一段。</summary>
    public static string DebugSegmentOf(EcoNode node, long tick)
    {
        var state = node.AvailableAt(tick) ? "ready" : "eaten";
        var regrow = node.AvailableAt(tick)
            ? "-"
            : node.Kind == EcoKind.Ore
                ? "never"
                : ((node.AvailableAtTick - tick + ArenaTime.StepsPerGameMinute - 1)
                    / ArenaTime.StepsPerGameMinute).ToString(CultureInfo.InvariantCulture);

        return string.Format(CultureInfo.InvariantCulture,
            "ECO {0} kind={1} pos={2:0.0},{3:0.0} state={4} herd={5} regrow={6}",
            node.Id, node.KindText, node.Position.X, node.Position.Z, state,
            node.HerdRemaining(tick), regrow);
    }

    // === 私产 ===

    /// <summary>三类对象的基础收益（表只有这一处）。</summary>
    private static (double Satiation, double Digestion) BaseGainsOf(EcoKind kind) => kind switch
    {
        EcoKind.Tree => (ArenaTuning.TreeSatiationGain, ArenaTuning.TreeDigestionGain),
        EcoKind.Herd => (ArenaTuning.HerdSatiationGain, ArenaTuning.HerdDigestionGain),
        _ => (ArenaTuning.OreSatiationGain, ArenaTuning.OreDigestionGain),
    };

    /// <summary>
    /// 形态对饱食收益的偏向：荒漠态吃矿石 ×2、其他 ×0.5；暴食态全部 ×1.5。
    /// <b>消化进度不走这一口</b>（荒漠态不改消化，暴食态在 <see cref="TryEat"/> 里单独乘）。
    /// </summary>
    private static double SatiationFactorOf(EcoKind kind, BeastForm form) => form switch
    {
        BeastForm.Desert => kind == EcoKind.Ore
            ? ArenaTuning.DesertFormOreSatiationFactor
            : ArenaTuning.DesertFormOtherSatiationFactor,
        BeastForm.Glutton => ArenaTuning.GluttonFormEatFactor,
        _ => 1.0,
    };

    /// <summary>吞掉之后落再生时刻（矿石永久离场；树木/兽群按各自的小时数原位再生）。</summary>
    private static void MarkEaten(EcoNode node, long tick)
    {
        if (node.Kind == EcoKind.Ore)
        {
            node.Consumed = true;
            return;
        }

        var hours = node.Kind == EcoKind.Tree
            ? ArenaTuning.TreeRegrowGameHours
            : ArenaTuning.HerdRegrowGameHours;
        node.AvailableAtTick = tick + (ArenaTime.StepsPerGameHour * hours);
    }
}
