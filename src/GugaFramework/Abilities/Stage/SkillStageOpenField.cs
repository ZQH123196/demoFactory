#nullable enable
using System;
using System.Collections.Generic;
using DemoFactory.GugaFramework.Abilities;

namespace DemoFactory.GugaFramework.Abilities.Stage;

/// <summary>
/// <b>开阔多点场子</b>——空旷地 + 多个目标，量<b>扇形</b>、<b>范围</b>、<b>群体</b>三件事。
///
/// <b>它与第一个场子（<see cref="SkillStage"/>）差在哪</b>：只差一处——
/// <b>多目标同框，并且「打到了谁」是一份能念出来的名单</b>。
/// 阻挡、天气、经济、寻路、AI 决策、UI 那些干扰项两边一样（本来一个都没有），
/// 三个口（时间可控 / 状态可查 / 行为可注入）也全部继承，不重写一遍。
/// 场地尺寸是<b>注册口那份</b>的一格（<c>GridHalfExtentMeters</c>），
/// 本文件不替调用方挑「开阔」该开多大——那份地铺在注册壳里。
///
/// <b>两问是分开的，不是一问顺带判掉的</b>：
/// <list type="number">
/// <item><b>范围</b>（<see cref="TargetsInRadius"/>）：半径之内是谁，<b>不看角度</b>。</item>
/// <item><b>扇形</b>（<see cref="SkillStage.TargetsInSector"/>，继承来的）：半径之内<b>且</b>在正前全角内是谁。</item>
/// </list>
/// 判定本身在公共层那把尺里（<see cref="AbilityStepper.InsideSector"/> 与
/// <see cref="AbilityStepper.UnitsInRange"/>），场子只递自己那份摆位与朝向——
/// 所以「谁算的距离」「怎么算命中」都只有一份实现（「桥只转发」 同一句理由）。
///
/// <b>群体施加只走写路径那一条</b>：<see cref="ApplyEffectToGroupInRadius"/> 与
/// <see cref="ApplyEffectToGroupInSector"/> 都是「先算名单 → 名单落地成形状事件 → 逐个从
/// <see cref="SkillStage.Inject"/> 施加」。不新造动词、不另写一份施加实现，
/// 所以「注入的路径」与「玩家走的那条」不是两回事。
/// 返回的是<b>真的打上去了</b>那几位——比候选名单短就是有目标没打上（表里没这条效果、或当场被激活检查拒），
/// 调用方一眼看得出差在哪，不静默吞掉。
///
/// <b>本文件不认识任何一条具体技能、任何一条属性名、任何一条 tag</b>：名字与数值全由注册口
/// （<see cref="SkillStageSpec"/>）递进来，与第一个场子同一份注册口（场子之间不各写一份）。
/// </summary>
public sealed class SkillStageOpenField : SkillStage
{
    /// <summary>递同一份注册口起一局——签名与第一个场子逐字相同，种子照旧由调用点给（场子不替它挑）。</summary>
    public SkillStageOpenField(SkillStageSpec spec, int seed = 1)
        : base(spec, seed)
    {
    }

    /// <inheritdoc />
    protected override string HostLabel => nameof(SkillStageOpenField);

    /// <summary>
    /// <b>范围那一问</b>：<paramref name="radiusMeters"/> 米之内是谁，<b>不看角度</b>。
    /// 与扇形那一问（<see cref="SkillStage.TargetsInSector"/>）是分开的两问：
    /// 同样两个点，一个在界内一个在界外，分开它们的只有角度——两问各自的答案要能分别念出来，
    ///  否则「范围够不够」就永远被「角度够不够」顺带判掉了（场子自己的台账 L2 判的是同一件事）。
    ///
    /// 名单按注册口的摆位次序（遍历次序是确定性的一部分），判死的不进名单。
    /// </summary>
    public IReadOnlyList<string> TargetsInRadius(string casterId, double radiusMeters)
        => Stepper.UnitsInRange(casterId, radiusMeters);

    /// <summary>
    /// <b>群体施加 · 范围那一档</b>：半径之内每一个都挂上 <paramref name="effectId"/>。
    /// 全角那一格给 0，含义与 <see cref="AbilityStepper.InsideSector"/> 一致：<c>coneDegrees &lt;= 0</c>
    /// 就是「整个圆，不看角度」。返回真的打上去了那几位。
    /// </summary>
    public IReadOnlyList<string> ApplyEffectToGroupInRadius(string casterId, double radiusMeters,
        string effectId, long? durationSteps = null)
        => ApplyToGroup(casterId, TargetsInRadius(casterId, radiusMeters), radiusMeters, coneDegrees: 0,
            effectId, durationSteps);

    /// <summary>
    /// <b>群体施加 · 扇形那一档</b>：落在施法者那发扇形里的每一个都挂上 <paramref name="effectId"/>。
    /// 名单由 <see cref="SkillStage.TargetsInSector"/> 现算（公共层那把尺），返回真的打上去了那几位。
    /// </summary>
    public IReadOnlyList<string> ApplyEffectToGroupInSector(string casterId, double radiusMeters,
        double coneDegrees, string effectId, long? durationSteps = null)
        => ApplyToGroup(casterId, TargetsInSector(casterId, radiusMeters, coneDegrees), radiusMeters,
            coneDegrees, effectId, durationSteps);

    /// <summary>
    /// 群体施加的那一份实现：<b>先落名单、再逐个施加</b>，次序固定。
    /// 名单先落地成形状事件（公共层的补记口，名字与形状仍归公共层），于是「这一发打到了谁」
    /// 在事件流里是一行带名单的读数；打空时那一发照样发出去（打空要读得出为什么空）。
    /// 逐个施加走 <see cref="SkillStage.Inject"/> 那一条写路径，动词名与参数次序住在
    /// <see cref="AbilityVerbs"/>，本文件不写任何一个动词字面量。
    /// </summary>
    private IReadOnlyList<string> ApplyToGroup(string casterId, IReadOnlyList<string> group, double radius,
        double coneDegrees, string effectId, long? durationSteps)
    {
        Stepper.RecordShape(casterId, radius, coneDegrees, group);

        var landed = new List<string>(group.Count);
        foreach (var unitId in group)
        {
            var args = durationSteps.HasValue
                ? $"{unitId}|{effectId}|{durationSteps.Value}|{casterId}"
                : $"{unitId}|{effectId}|-|{casterId}";
            if (Inject(AbilityVerbs.ApplyEffect, args))
            {
                landed.Add(unitId);
            }
        }

        return landed;
    }
}
