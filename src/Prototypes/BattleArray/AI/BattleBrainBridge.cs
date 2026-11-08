#nullable enable
using DemoFactory.Prototypes.BattleArray.Logic;
using Godot;

namespace DemoFactory.Prototypes.BattleArray.AI;

/// <summary>
/// 行为树 ↔ 逻辑世界之间的桥 —— 一个兵一座，挂在该兵的表现节点下。
///
/// 为什么要有这座桥：树是 GDScript 节点（Beehave 的组合件），逻辑层是纯 C#（零引擎依赖才能进
/// <c>dotnet test</c>），两边不能互相引用类型 ⇒ 只走**标量进、标量出**的动词。
/// GDScript 侧按名调用要写小写 <c>bridge.call("SenseHealth")</c>（大写 <c>Call</c> 会被当成成员访问）。
///
/// 与无限领主那条线的**一处不同**（刻意）：那边是「树写意图 → 世界下一步执行」，隔一步；
/// 这边世界在同一步里**同步问树**（<see cref="DecideFor"/> 里调一次 `tick()`、拿回决策、当场执行）。
/// 理由：M4 的判绿建立在「离线跑与运行时逐字同一局」上，同步问 ⇒ 决策顺序仍是出场顺序、
/// 没有「哪一步的意图被哪一步执行」这类二次账；将来若真要引入一步延迟或多支路并发，
/// 改的是 <see cref="BattleWorld.Brain"/> 这一个口子。
/// </summary>
public partial class BattleBrainBridge : Node
{
    private BattleWorld? _world;
    private Node? _host;
    private string _unitId = string.Empty;
    private BattleBrainPolicy.Senses _senses;
    private BattleBrainPolicy.Decision? _result;
    private bool _fallbackWarned;

    /// <summary>这座树实际交出过多少次决策 —— <c>Main.BrainReadout()</c> 用它证明「树真在管事」，不是挂了个摆设。</summary>
    public int DecisionsTaken { get; private set; }

    /// <summary>由表现层在挂树时注入：哪个兵、哪个世界、哪座宿主（树在宿主下面）。</summary>
    public void Wire(BattleWorld world, string unitId, Node host)
    {
        _world = world;
        _unitId = unitId;
        _host = host;
    }

    /// <summary>
    /// 世界走到这一步时问一次：把观测交给树、驱动它 `tick()` 一遍、取回决策。
    /// 树没给出决策（叶子没挂上、宿主被释放）时退回纯 C# 同一条规则并告警一次 ——
    /// 退路必须有，但绝不能安静：安静地退回就等于「树其实没在管事」。
    /// </summary>
    public BattleBrainPolicy.Decision DecideFor(BattleBrainPolicy.Senses senses)
    {
        _senses = senses;
        _result = null;
        _host?.Call("tick_for_bridge");
        if (_result is { } decided)
        {
            DecisionsTaken++;
            return decided;
        }

        if (!_fallbackWarned)
        {
            _fallbackWarned = true;
            GD.PushWarning($"[BattleArray] 兵 {_unitId} 的树没交出决策，退回纯 C# 规则 —— 树没在管事（叶子没挂上？宿主已释放？）");
        }
        return BattleBrainPolicy.Decide(senses);
    }

    /// <summary>叶子把决策交回这里（一个兵一步只该有一个叶子认账；后到的覆盖前到的，且被记进告警）。</summary>
    public void Record(BattleBrainPolicy.Decision decision)
    {
        if (_result is not null && _result.Value.Intent != decision.Intent)
            GD.PushWarning($"[BattleArray] 兵 {_unitId} 一步出现两条支路：{_result.Value.Intent} 与 {decision.Intent}");
        _result = decision;
    }

    // === 观测口（叶子与 GDScript 宿主都从这里读，别在别处另算一套） ===

    public string SenseUnitId() => _unitId;

    /// <summary>这人还在不在场。宿主用它决定要不要停手 —— 死人继续被 `tick()` 会把行为流写成活人。</summary>
    public bool SenseAlive() => _world?.Find(_unitId)?.Alive ?? false;

    public int SenseHealth() => _world?.Find(_unitId)?.Health ?? 0;

    public int SenseMaxHealth() => _world?.Find(_unitId)?.MaxHealth ?? 0;

    /// <summary>兵种中文名（名牌与行为流读的是同一张卡）。</summary>
    public string SenseDisplayName() => _world?.Find(_unitId)?.Look.DisplayName ?? "未知兵种";

    public bool SenseHasTarget() => _senses.HasTarget;

    public string SenseTargetId() => _world?.Find(_unitId)?.TargetId ?? string.Empty;

    /// <summary>世界算好的这一步观测：距离 / 兵种卡口径的够得着距离 / 冷却。</summary>
    public float SenseDistance() => _senses.Distance;

    public float SenseCardReach() => _senses.Reach;

    public int SenseCooldown() => _senses.CooldownRemaining;

    /// <summary>兵种卡上的射程（米）—— 叶子的 <c>range_override</c> 与它对照，不覆盖时以它为准。</summary>
    public float SenseCardRange() => _world?.Find(_unitId)?.Card.Range ?? 0f;

    /// <summary>自己的身体半径（拥挤度）—— 换算「够得着」时与目标的半径相加。</summary>
    public float SenseSelfRadius() => _world?.Find(_unitId)?.Card.BodyRadius ?? 0f;

    /// <summary>目标的身体半径。</summary>
    public float SenseTargetRadius()
    {
        var targetId = _world?.Find(_unitId)?.TargetId;
        return targetId is null ? 0f : _world?.Find(targetId)?.Card.BodyRadius ?? 0f;
    }

    /// <summary>本步世界实际执行的决策（行为流与快照读的是同一句）。</summary>
    public string SenseDecisionWhy() => _result?.Why ?? BattleBrainPolicy.Decide(_senses).Why;

    /// <summary>本步世界实际执行的意图名（Strike / CloseIn / Hold）。</summary>
    public string SenseDecisionIntent() => _result?.Intent.ToString() ?? "未判";

    /// <summary>桥离场时交还：世界侧不需要额外登记，但引用要断掉，别拖住已释放的节点。</summary>
    public override void _ExitTree()
    {
        _world = null;
        _host = null;
    }
}
