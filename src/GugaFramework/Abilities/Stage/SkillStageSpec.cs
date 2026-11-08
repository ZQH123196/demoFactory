#nullable enable
using System;
using System.Collections.Generic;
using DemoFactory.GugaFramework.Abilities;

namespace DemoFactory.GugaFramework.Abilities.Stage;

/// <summary>
/// <b>注册口</b>——原型侧递进来的三样东西，场子只认这一份形状：
/// ① 技能表（<see cref="AbilityTables"/>）② 纯函数（<see cref="CalcRegistry"/>）③ 单位属性（<see cref="StageRoster"/>）。
///
/// 形状是「一份注册 + 两个壳」里的第一份（spec「形态」一节）：场子不认原型，也不认任何一条具体技能，
/// 换原型换技能时本文件与 <c>SkillStage.cs</c> 一个字都不动。
///
/// 三份必填（<see langword="required"/>，少一列编译期就断）：公共层不替调用方挑词表、挑容量、挑种子。
/// </summary>
public sealed record SkillStageSpec
{
    /// <summary>① 技能表：一条技能一张效果，由原型侧挂号（公共层不认识任何一条具体技能）。</summary>
    public required AbilityTables Tables { get; init; }

    /// <summary>② 纯函数：按名注册的公式 / 条件 / 谓词（<c>name → function</c>）。</summary>
    public required CalcRegistry Calcs { get; init; }

    /// <summary>③ 单位属性：词表、单位属性值、摆位与场地尺寸。全在这个一份里。</summary>
    public required StageRoster Roster { get; init; }
}

/// <summary>
/// 单位属性那一份：<b>属性名词表 + 每个单位的属性值 + 摆位 + 场地尺寸</b>。
///
/// 词表（<see cref="Config"/> 那几格）跟着单位属性一起递，理由是它们本来就是属性名与 tag 名：
/// 血量属性名、承伤属性名、冷却前缀、定身 tag、抵挡 tag——少一列编译期就断，场子一个缺省都不给
/// （场子自带一份默认词表，「换原型不改场子」就又是假的）。
///
/// <b>摆位是二维的</b>（<see cref="StageUnit.X"/> + <see cref="StageUnit.Z"/>，米），
/// 朝向是开局的场地条件（<see cref="StageUnit.FacingDegrees"/>，度，0 = 朝 +X）。
/// 坐标归场子、不归技能层：单位身上没有坐标那一格（ticket 03 定稿），
/// 距离由场子换算成米数经 <c>DistanceOf</c> 递进层里，层只问不持。
/// </summary>
public sealed record StageRoster
{
    /// <summary>接层那份配置：五列词表 + 事件流观测窗口。全部必填，公共层不替调用方选。</summary>
    public required AbilityLayerConfig Config { get; init; }

    /// <summary>场上全部单位，按摆位顺序。遍历次序是确定性的一部分，所以是 List 不是 Dictionary。</summary>
    public required IReadOnlyList<StageUnit> Units { get; init; }

    /// <summary>
    /// 血量上限的属性名：<b>只给表现层念血条用</b>（<see cref="Config"/> 那一列是逻辑层判死读的那一份，
    /// 两者可以是同一个名字，不必是同一个格子）。
    /// </summary>
    public required string MaxHealthAttribute { get; init; }

    /// <summary>
    /// 地面网格的半边长（米）。默认 20 ⇒ 一块 40 米见方的地，1 米一格。
    /// 场地尺寸是场子自己的参数，不是原型的——它不认任何具体技能。
    /// </summary>
    public double GridHalfExtentMeters { get; init; } = 20;

    /// <summary>地面网格一格多少米。默认 1。改这一格只改画面，不改逻辑（单位坐标的单位是米）。</summary>
    public double GridCellMeters { get; init; } = 1;

    /// <summary>标尺上每隔多少米立一根带数字的刻度。默认 5。</summary>
    public double RulerLabelEveryMeters { get; init; } = 5;
}

/// <summary>
/// 场上一个单位：<b>属性值 + 技能栏 + 摆位与朝向</b>。
///
/// 摆位与朝向都是<b>开局的场地条件</b>，不是技能的输出：场子不替单位走位（规则：写路径只有
/// <c>Inject</c> 一条，位置只由摆位与位移那两条写路径改）。想验「谁在射程边界哪一侧」，
/// 改这一份摆位重起一局，而不是等场子自己走过去。
/// </summary>
public sealed record StageUnit
{
    /// <summary>逻辑层的单位标识。场子里所有东西都按它认，不按显示名认。</summary>
    public required string UnitId { get; init; }

    /// <summary>属性名 → 基础值。名字归原型，场子只当键用。</summary>
    public required IReadOnlyList<(string Name, double Value)> Attributes { get; init; }

    /// <summary>开局就装好的技能栏（相位与冷却都从 Ready 起）。</summary>
    public IReadOnlyList<string> AbilityIds { get; init; } = Array.Empty<string>();

    /// <summary>摆位的那一根轴（米）。默认 0。X 与 Z 合起来是一个地面点；只给 X 是一维那一档。</summary>
    public double X { get; init; }

    /// <summary>摆位的那一根轴（米）。默认 0。</summary>
    public double Z { get; init; }

    /// <summary>
    /// 朝向（度）：0 = 朝 +X，90 = 朝 +Z，逆时针为正。只喂给扇形那一把尺
    /// （公共层 <c>InsideSector</c> 收的是两个标量，场子按这一格换算过去）。
    /// 默认 0。
    /// </summary>
    public double FacingDegrees { get; init; }
}
