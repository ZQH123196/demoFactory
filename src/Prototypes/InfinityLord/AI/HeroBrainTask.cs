#nullable enable
using System;
using DemoFactory.GugaFramework.Beehave;
using DemoFactory.Prototypes.InfinityLord.Logic;
using Godot;

namespace DemoFactory.Prototypes.InfinityLord.AI;

/// <summary>
/// 无限领主 C# 任务的共同搬运层：从宿主上取桥、读一步观测、把意图写回桥、
/// 把决策打进 <c>HeroBrain.log_decision</c>（行为流的唯一出口，GDScript 版与 C# 版共用，
/// 否则两份记录不可比）。叶子只实现 <see cref="Decide"/>。
/// </summary>
public abstract partial class HeroBrainTask : BeehaveTask
{
    protected sealed override int Tick(Node actor, Node blackboard)
    {
        if (Bridge(actor) is not { } bridge)
        {
            return Failure;
        }

        HeroBrainPolicy.Outcome outcome = Decide(Sense(bridge));
        Dispatch(bridge, outcome);
        if (outcome.LogWhat is not null && outcome.LogDetail is not null)
        {
            // C# 调 GDScript 用大写 Call（小写 call 会被当成成员访问，实测报 Nonexistent function）。
            actor.Call("log_decision", outcome.LogWhat, outcome.LogDetail);
        }

        return (int)outcome.Status;
    }

    /// <summary>叶子只回答这一件事：拿到这一步的观测，决策是什么。</summary>
    protected abstract HeroBrainPolicy.Outcome Decide(HeroBrainPolicy.Senses senses);

    /// <summary>
    /// 桥挂在宿主（<c>AI/hero_brain.gd</c>）上，由表现层挂树时 <c>Set("bridge", ...)</c> 注入。
    /// 刻意不用 [Export] 节点引用：手写 .tscn 里的节点引用取回来是 null（实测，根因未查）。
    /// </summary>
    private static HeroBrainBridge? Bridge(Node actor) => actor.Get("bridge").As<HeroBrainBridge>();

    private static HeroBrainPolicy.Senses Sense(HeroBrainBridge bridge)
    {
        string enemyId = bridge.SenseNearestEnemyId();
        return new HeroBrainPolicy.Senses(
            bridge.SenseHeroId(),
            bridge.SenseHealth(),
            bridge.SenseMaxHealth(),
            enemyId,
            enemyId.Length == 0 ? -1f : bridge.SenseDistanceTo(enemyId),
            bridge.SenseAttackRange());
    }

    /// <summary>意图在<b>同一步</b>被 LogicWorld 执行（树在决策那一段的开头 tick，写完就被读走）。</summary>
    private static void Dispatch(HeroBrainBridge bridge, HeroBrainPolicy.Outcome outcome)
    {
        switch (outcome.Intent)
        {
            case BrainIntent.None:
                // 这一步不下令：接管表保持上一条，不撤令（策略侧用它表示「这一步不发表意见」）。
                break;
            case BrainIntent.Engage:
                bridge.CommandEngage(outcome.TargetId);
                break;
            case BrainIntent.Flee:
                bridge.CommandFlee(outcome.TargetId);
                break;
            case BrainIntent.Hold:
                bridge.CommandHold();
                break;
            case BrainIntent.Wander:
                bridge.CommandWander();
                break;
            default:
                // 漏一支的后果不是「这一步不下令」而是「这名英雄原地不动一整局」：
                // 接管在册之后世界跳过它自己那条决策链，而接管表停在初始的 None 上 ⇒ 每步发「原地不动」。
                // 实测踩过：Wander 漏了一支，hero_0 从第 1 步站到第 301 步，零收入被开除、阵亡。
                // 所以这里当场抛，不静默。
                throw new InvalidOperationException($"英雄树意图没有对应的桥口令 | {outcome.Intent}");
        }
    }
}
