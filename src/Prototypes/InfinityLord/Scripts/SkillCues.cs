#nullable enable
using System;
using DemoFactory.GugaFramework.Abilities;
using DemoFactory.Prototypes.InfinityLord.Logic;

namespace DemoFactory.Prototypes.InfinityLord;

/// <summary>
/// 技能 cue —— 表现层在技能层这一侧的<b>唯一</b>订阅者（<c>SkillCues</c>，原型侧三件里的第三件）。
///
/// <para>它只做一件事：把「谁出手了」翻成一个回调，绝不往逻辑层写一个字节
/// （「表现层只读」）。触发谁的动作、怎么演，是主节点那一侧的事。</para>
///
/// <para><b>两半都走这一条路</b>（Q12 收编的两半都在层里）：订技能层事件出口的 <see cref="DamageDealt"/>，
/// 出手方读<b>事件形状里的 <c>Source</c></b> —— 事件行的主体（<c>Subject</c>）是<b>挨打的那个</b>
/// （层里那一发是 <c>Emit(target.UnitId, new DamageDealt(final, sourceId, …))</c>），
/// 照主体演就把出手动作演在了被击单位身上。</para>
///
/// <para>单位↔建筑那一半后来也进了层（<c>InfinityLordAbilities.StrikeBuilding</c>），建筑在层那一册里
/// 同样有 Id ⇒ 砸楼也落在这一条路上。世界那一侧的 <c>CombatEvent</c> 仍是「这一发打出了什么」的战报出口
/// （别的消费者还在读它），但<b>它不再是 cue 的来源</b>：两处都演的话同一次砸楼会演两遍
/// （模型驱动器那条「一次攻击只演一遍」就守不住了）。</para>
///
/// <para><b>按逻辑步翻、不按渲染帧</b>：游标读的是层累计发出的条数（「时间基准」——
/// 报时间只认逻辑时钟）。「尾窗里没有」≠「没发生」：被环形挤掉的那几条由
/// <see cref="DroppedCount"/> 报出来，不静默。</para>
/// </summary>
public sealed class SkillCues
{
    private readonly Action<string> _onAttack;
    private LogicWorld? _world;

    /// <summary>上次读到第几条（累计条数；换一局要重新对齐，不然会把上一局的动作重演一遍）。</summary>
    private long _cursor;

    /// <param name="onAttack">「这个单位出手了」——表现层照它触发攻击动作。</param>
    public SkillCues(Action<string> onAttack)
    {
        ArgumentNullException.ThrowIfNull(onAttack);
        _onAttack = onAttack;
    }

    /// <summary>挂上这一个世界（换一局就重挂；游标记在当下，之前发过的事件不回放）。</summary>
    public void Wire(LogicWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        Unwire();
        _world = world;
        _cursor = world.Abilities.EventTotal;
    }

    /// <summary>交还订阅（「离树交还」：主动交还，不在调用点每次查有效性）。</summary>
    public void Unwire()
    {
        if (_world is null) return;
        _world = null;
    }

    /// <summary>被环形挤掉、没演出来的条数（读数口：画面跟不上时先看这一格）。</summary>
    public long DroppedCount => _world?.Abilities.EventsDropped ?? 0;

    /// <summary>
    /// 每逻辑步一次：把层这一步新发的事件读完，命中类的交给回调。
    /// 读到的是累计条数的增量，所以同一条事件不会被演第二遍。
    /// </summary>
    public void Mirror()
    {
        if (_world is not { } world) return;

        var abilities = world.Abilities;
        var pending = abilities.EventTotal - _cursor;
        if (pending > 0)
        {
            foreach (var ev in abilities.EventsTail((int)Math.Min(pending, int.MaxValue)))
            {
                // 演的是出手方：事件行的主体是挨打的那个，出手方在形状的 Source 里（见类型注释）。
                if (ev.Payload is DamageDealt damage) _onAttack(damage.Source);
            }

            _cursor = abilities.EventTotal;
        }
    }
}
