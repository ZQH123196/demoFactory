#nullable enable
using System;
using DemoFactory.Prototypes.InfinityLord.Logic;
using Godot;

namespace DemoFactory.Prototypes.InfinityLord;

/// <summary>
/// 行为树 ↔ 逻辑层之间的桥 — 一个英雄一座，挂在单位的表现节点下。
///
/// 存在理由：行为树是 GDScript 节点，逻辑层是纯 C#（<see cref="LogicWorld"/> 里零引擎依赖，
/// 才能被 dotnet test 复现）。两边不能互相引用类型，这座桥只暴露**标量进标量出**的动词，
/// GDScript 侧用 <c>bridge.call("SenseHealth")</c> 这样的按名调用 —— 小写 <c>call</c>，
/// 写成 C# 风格的 <c>Call</c> 会被当成成员访问，报 Nonexistent function 'Call'（实测踩过）。
/// 另外 Godot 4.7.2 的 C# 没有 [Callable] 特性，
/// public 方法本身就对脚本可见；注册名逐字等于声明名，所以 C# 侧照旧写 PascalCase）。
///
/// 驱动：每推进一步逻辑帧时间，表现层调一次 <see cref="EmitStepped"/> —— 它挂在
/// <c>LogicWorld.StepStarted</c> 上（决策那一段的开头），所以树收到信号后 `tick()` 一次、
/// 写回的意图紧接着被<b>同一步</b>的决策链读走（同一步生效，方向确定，重跑可复现）。
/// </summary>
public partial class HeroBrainBridge : Node
{
    /// <summary>逻辑帧时间又推进了一步 — 树据此 `tick()` 一次。</summary>
    [Signal]
    public delegate void SteppedEventHandler();

    private LogicWorld? _world;
    private string _heroId = string.Empty;

    /// <summary>
    /// 意图的写路口 —— 由表现层在挂树时注入（原型侧唯一那条写路径 <c>Main.Write</c>）。
    /// 返回类型与那条写路径一致（<c>true</c> = 这一发打成了），桥这一侧不看它：
    /// 树只负责说「现在该干什么」，打不打得成由世界那条说了算。
    ///
    /// <para><b>为什么不直调 <see cref="LogicWorld.ReportBrainIntent"/></b>：那是一条写进逻辑世界的
    /// 路，而树住在表现层、测试世界没有树 —— 直调的话「这一步树说了什么」进不了 Journal，
    /// 活局录的脚本在离线就重放不出来（同一粒种子、同一个步号，一个世界有树发令、另一个没有）。
    /// 走这一路之后，树的意图与玩家的那一手是<b>同一条写路径</b>（→ 「桥只转发」 的同款口径）。</para>
    /// </summary>
    private Func<string, string, bool>? _write;

    /// <summary>由表现层在挂树时注入：哪名英雄、哪个世界、意图往哪个写路口走。</summary>
    public void Wire(LogicWorld world, string heroId, Func<string, string, bool> write)
    {
        _world = world;
        _heroId = heroId;
        _write = write;
        world.DelegateHeroToBrain(heroId);
    }

    /// <summary>表现层每步发一次（暂停时不发，树自然跟着时间轴一起停）。</summary>
    public void EmitStepped() => EmitSignal(SignalName.Stepped);

    // === 观测口（GDScript 按名调用） ===

    public string SenseHeroId() => _heroId;

    /// <summary>
    /// 这名英雄还在不在场（阵亡/被行会开除/离场都算不在）。
    /// 存在的理由：宿主得知道什么时候该停手 —— 人不在了还继续被 `tick()`，行为流就会写出
    /// 「血量 0/0，随便走走」这种把死人写成活人的行（实测 2026-09-25 抓到过 150 步）。
    /// </summary>
    public bool SenseAlive() => _world?.FindAliveUnit(_heroId) is not null;

    public int SenseHealth() => _world?.FindAliveUnit(_heroId)?.Health ?? 0;

    public int SenseMaxHealth() => _world?.FindAliveUnit(_heroId)?.MaxHealth ?? 0;

    /// <summary>视野内最近敌方单位的 Id；没有返回空串。</summary>
    public string SenseNearestEnemyId() => _world?.SenseNearestEnemy(_heroId).Id ?? string.Empty;

    /// <summary>到那名敌人的距离；没有敌人返回 -1。</summary>
    public float SenseNearestEnemyDistance() => _world?.SenseNearestEnemy(_heroId).Distance ?? -1f;

    /// <summary>到指定单位的距离；不在场返回 -1。</summary>
    public float SenseDistanceTo(string unitId) => _world?.SenseDistance(_heroId, unitId) ?? -1f;

    /// <summary>这名英雄的真实攻击距离 —— 树的射程校验读它，不在叶子里手抄一份常数（抄了就会与职业表漂移）。</summary>
    public float SenseAttackRange() => _world?.FindAliveUnit(_heroId)?.AttackRange ?? 0f;

    // === 意图口 ===

    public void CommandEngage(string targetId) => Report((int)BrainIntent.Engage, targetId);

    public void CommandFlee(string targetId) => Report((int)BrainIntent.Flee, targetId);

    public void CommandHold() => Report((int)BrainIntent.Hold, string.Empty);

    /// <summary>随机游荡：方向由世界的带种子随机数给，同一颗种子跑两次轨迹逐步相同。</summary>
    public void CommandWander() => Report((int)BrainIntent.Wander, string.Empty);

    /// <summary>实验剧本用：给自己扣血（真游戏里英雄清完敌人就不再挨打，血线自然跌不到逃跑阈值）。</summary>
    public void DebugHurt(int amount) => _world?.DebugDamageUnit(_heroId, amount);

    /// <summary>
    /// 意图的写出口 —— 走表现层注入的那条写路径（<c>Main.Write</c> → 词表 → Journal）。
    /// 没注入写路口时退回直调世界：那种接线只可能出现在「没接录制」的场合，
    /// 直调至少让树还能驱动世界，不至于静默失效。
    /// </summary>
    private void Report(int intent, string targetId)
    {
        var intentName = ((BrainIntent)intent).ToString();
        var target = string.IsNullOrEmpty(targetId) ? "-" : targetId;
        if (_write is not null)
        {
            _write("ReportBrainIntent", $"{_heroId}|{intentName}|{target}");
            return;
        }

        _world?.ReportBrainIntent(_heroId, intent, targetId);
    }

    /// <summary>桥被撤下时交还英雄，否则 Id 会永远占着接管表。</summary>
    public override void _ExitTree()
    {
        _world?.ReleaseHeroFromBrain(_heroId);
    }
}
