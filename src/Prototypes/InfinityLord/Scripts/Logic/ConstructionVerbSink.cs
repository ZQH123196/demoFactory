#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using DemoFactory.GugaFramework.Construction;

namespace DemoFactory.Prototypes.InfinityLord.Logic;

// 营建动词的薄适配器 —— 公共建造模块那一份词表（D34「一份表、两处问」）在本原型的落地。
//
// 分工照 AbilityVerbs / ConstructionVerbs 的先例：
//   · 动词名、参数次序、缺省值、竖线切分、什么时候该抛 —— 全在
//     <see cref="ConstructionVerbs"/>（本文件一个动词名都不另抄，只引它的常量）；
//   · 本文件只回答两件事：①「这一侧接得住哪几条」（<see cref="SupportedVerbs"/>），
//     ②载荷原文编成什么对象、调用世界的那一次写路径。
//
// 载荷原文的编解码留在原型（spec 3 节第 1 条的第四个插件槽）：模块存它、不读它、原样归还（D31），
// 所以「它是哪几列、怎么写成一段文本」是原型的私产，模块里一份都没有。

/// <summary>
/// 建筑载荷 —— 原型挂在 <c>Commit</c> 上的那一份数据：阵营 / 等级 / 产出周期（D31）。
///
/// <para><b>文本形状是 <c>阵营/等级/产出周期</c></b>，段内分隔符用斜杠而不是竖线：竖线是
/// 动词 args 的分隔符、全表一份、没有转义档（模块那侧的约定），载荷自己再带竖线就永远解不开。
/// 载荷里也不许写 <c>-</c>：那一档是「这一格没给」（「桥只转发」），所以缺省是<b>整段不给</b>
/// （<see cref="DefaultFor"/> 照种类给），不是「给一个减号」。</para>
///
/// <para>解码<b>不降级</b>：阵营读不出就判否，绝不把 <c>0</c> / <c>1</c> 这种数字串当成枚举序号
/// 悄悄折成 <c>Hero</c>（与 <c>ReplaySetup.TryParseBuildingKind</c> 同款理由：降级会把映射错误
/// 伪装成「逻辑层拒绝」）。</para>
/// </summary>
/// <param name="Owner">阵营。</param>
/// <param name="Level">行会等级（非行会恒为 1）。</param>
/// <param name="SpawnIntervalTicks">产出周期（步；0 = 不产单位）。</param>
public readonly record struct BuildingPayload(Faction Owner, int Level, int SpawnIntervalTicks)
{
    /// <summary>载荷段内的分隔符（不是竖线 —— 见类型注释）。</summary>
    public const char FieldSeparator = '/';

    /// <summary>照种类给缺省载荷：玩家营建全是本方、Lv.1，行会产兵 20 步/名、服务建筑不产单位。</summary>
    public static BuildingPayload DefaultFor(BuildingKind kind)
        => new(Faction.Hero, 1, kind is BuildingKind.GuildHall or BuildingKind.GuildWarrior ? 20 : 0);

    /// <summary>写成一段文本（进 Journal 的那一段）。</summary>
    public string Encode()
        => string.Join(FieldSeparator,
            Owner.ToString(),
            Level.ToString(CultureInfo.InvariantCulture),
            SpawnIntervalTicks.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// 读回载荷。<paramref name="text"/> 为 null / 空串 / <c>-</c>（整段没给）时回
    /// <paramref name="fallback"/>（缺省段机制，D15 的逐次覆盖同款）；读不出则判否 ——
    /// 作者错误要当场抛给调用方，由重放适配器那一层统一接成「返 false 不抛」。
    /// </summary>
    public static bool TryDecode(string? text, out BuildingPayload payload, BuildingPayload? fallback = null)
    {
        if (text is null or "" or "-")
        {
            payload = fallback ?? default;
            return true;
        }

        var fields = text.Split(FieldSeparator);
        if (fields.Length != 3
            || !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var level)
            || !int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var spawn)
            || level < 1
            || spawn < 0
            || !Enum.TryParse(fields[0], ignoreCase: true, out Faction owner))
        {
            payload = default;
            return false;
        }

        // 数字串不当枚举序号用（"0" 会解析成 Hero）—— 那是降级，不许。
        if (int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            payload = default;
            return false;
        }

        payload = new BuildingPayload(owner, level, spawn);
        return true;
    }
}

/// <summary>
/// 营建动词的薄适配器（原型侧那一份）—— <see cref="IConstructionVerbSink"/> 的实现。
///
/// <para><b>只接 <see cref="ConstructionVerbs.Place"/> 一条</b>：进 Journal 的是本原型自己词表
/// 那一份（<c>PlaceBuilding</c> / <c>HealBuilding</c> / <c>DamageBuilding</c> …），
/// 模块词表这一份只给动词名与参数次序（D34「一份表、两处问」）。名单外的动词名由
/// <see cref="ConstructionVerbs.Dispatch"/> 当场抛（口径 2：不适用要写明理由，
/// 不拿 false 表达「没这一格」）—— 编辑 / 暂停 / 批量 / 布局与只读查询都还没接线，
/// 所以这里给的不是读数而是「这一侧不接」。</para>
///
/// <para>玩家点鼠标落建与 eval / 重放注入走<b>同一个</b>入口（<c>LogicWorld.TryPlaceBuilding</c>），
/// 不是两条路（「桥只转发」：调试桥只转发既有直达方法）。</para>
/// </summary>
public sealed class InfinityLordConstructionSink : IConstructionVerbSink
{
    private static readonly HashSet<string> Verbs =
        new(StringComparer.Ordinal) { ConstructionVerbs.Place };

    private readonly LogicWorld _world;

    public InfinityLordConstructionSink(LogicWorld world) => _world = world;

    public string HostLabel => "无限领主";

    public IReadOnlySet<string> SupportedVerbs => Verbs;

    /// <summary>
    /// 放一座 —— 薄适配器只搬参数、只调世界那一次调用；判定（金库 / 间距 / 蓝图在不在册）全在世界里。
    /// 载荷原文在这一层解成 <see cref="BuildingPayload"/> 挂上去（模块不读它）。
    /// </summary>
    public WriteResult PlaceBuilding(string blueprintId, double x, double y, int orientation,
        string? buildingId, long? buildDurationTicks, string? payloadText)
    {
        if (!Enum.TryParse(blueprintId, ignoreCase: true, out BuildingKind kind))
        {
            return WriteResult.Fail(PlacementReason.UnknownBlueprint, buildingId ?? "");
        }

        if (!BuildingPayload.TryDecode(payloadText, out var payload, BuildingPayload.DefaultFor(kind)))
        {
            throw new FormatException(
                $"营建载荷读不懂「{payloadText}」（要的是 {nameof(BuildingPayload.Owner)}/" +
                $"{nameof(BuildingPayload.Level)}/{nameof(BuildingPayload.SpawnIntervalTicks)}，" +
                $"段内分隔符是 {BuildingPayload.FieldSeparator}，整段不许出现竖线）");
        }

        var outcome = _world.PlaceBuildingCore(kind, new System.Numerics.Vector2((float)x, (float)y),
            buildingId, buildDurationTicks, payload);
        return outcome.Ok
            ? WriteResult.Done(outcome.BuildingId)
            : WriteResult.Fail(outcome.CoreReason, outcome.BuildingId);
    }

    // ---- 这一轮不接的那几条：当场抛，并写明为什么（口径 2）------------------------

    public WriteResult RemoveBuilding(string buildingId, RemovalCause cause) => throw Unsupported("RemoveBuilding");

    public WriteResult MoveBuilding(string buildingId, double x, double y, int? orientation)
        => throw Unsupported("MoveBuilding");

    public WriteResult RotateBuilding(string buildingId, int orientation) => throw Unsupported("RotateBuilding");

    public WriteResult DamageBuilding(string buildingId, double amount) => throw Unsupported("DamageBuilding");

    public WriteResult RepairBuilding(string buildingId, double amount) => throw Unsupported("RepairBuilding");

    /// <summary>
    /// 血量绝对值写口 —— <b>模块那一格在了（用户裁决 2026-10-02），本原型的堡垒化排布已经走它</b>
    /// （<c>LogicWorld.DebugHealBuilding</c> → <c>ConstructionCore.SetHealth</c>，终局脚本那一发
    /// <c>HealBuilding palace|200000</c> 抬完是 208000、上限一并抬到 208000）。
    ///
    /// <para>而<b>这一侧仍然不接</b>：进 Journal 的是本原型自己词表里的
    /// <c>HealBuilding</c>，不是模块这一份的 <c>SetHealthBuilding</c>（D34「一份表、两处问」——
    /// 模块那一份给动词名与参数次序，本原型那一份给入录形态，两者各答自己那半）。
    /// 名单外由 <see cref="ConstructionVerbs.Dispatch"/> 当场抛（口径 2），
    /// 比这里假装接住再回读数好查。</para>
    /// </summary>
    public WriteResult SetHealthBuilding(string buildingId, double hp) => throw Unsupported("SetHealthBuilding");

    public WriteResult PauseBuild(string buildingId) => throw Unsupported("PauseBuild");

    public WriteResult ResumeBuild(string buildingId) => throw Unsupported("ResumeBuild");

    /// <summary>
    /// 场地建筑登记 —— <b>本原型这一侧的搬运已经接上了</b>：种子 / 终幕 / 巢穴重生那几座
    /// 走 <c>LogicWorld.AddFixture</c> → <c>ConstructionCore.RegisterFixture</c>（2026-10-02）。
    ///
    /// <para><b>它仍然不进 <see cref="SupportedVerbs"/></b>，理由与血量无关：这一格在
    /// <b>世界初始化期</b>调用（种子由两端各跑一次的 <c>SeedWorld()</c> 登记，巢穴重生与 BOSS 降临
    /// 由时钟排期确定地发生），录制端与重放端<b>两边都会发生同一发</b>，录一遍反而在重放端
    /// 撞上「重名」那道抛（理由钉在 <see cref="ConstructionVerbs.InjectNames"/> 上）。
    /// 在 Journal 里它的对应词是本原型自己的 <c>PlaceBuilding</c> —— 玩家的那一种。
    /// 名单外由 <see cref="ConstructionVerbs.Dispatch"/> 当场抛（口径 2）。</para>
    /// </summary>
    public WriteResult RegisterFixture(string blueprintId, double x, double y, int orientation,
        string buildingId, string? payloadText)
        => throw new NotSupportedException(
            "无限领主这一侧不接 RegisterFixture：场地建筑由世界初始化期的 SeedWorld 与时钟排期登记，" +
            "录制端与重放端两边都会发生同一发 —— 录进 Journal 会在重放端重复登记当场撞重名" +
            "（理由钉在 ConstructionVerbs.InjectNames 上）。玩家的那一种走 PlaceBuilding。");

    public BatchResult PlaceLine(string blueprintId, double x0, double y0, double x1, double y1, int orientation)
        => throw Unsupported("PlaceLine");

    public BatchResult PlaceRect(string blueprintId, double x0, double y0, double x1, double y1, int orientation)
        => throw Unsupported("PlaceRect");

    public BatchResult PlaceLayout(string layoutText, double x, double y, int orientation)
        => throw Unsupported("PlaceLayout");

    public RectRemovalResult RemoveRect(double x0, double y0, double x1, double y1) => throw Unsupported("RemoveRect");

    public string CheckLine(string blueprintId, double x0, double y0, double x1, double y1, int orientation)
        => throw Unsupported("CheckLine");

    public string CheckRect(string blueprintId, double x0, double y0, double x1, double y1, int orientation)
        => throw Unsupported("CheckRect");

    public string CheckLayout(string layoutText, double x, double y, int orientation)
        => throw Unsupported("CheckLayout");

    public string CaptureLayout(double x0, double y0, double x1, double y1) => throw Unsupported("CaptureLayout");

    public string BuildingAt(double x, double y) => throw Unsupported("BuildingAt");

    public string CheckPlacement(string blueprintId, double x, double y, int orientation)
        => throw Unsupported("CheckPlacement");

    public string GizmoParams() => throw Unsupported("GizmoParams");

    private static NotSupportedException Unsupported(string verb)
        => new($"无限领主这一侧不接 {verb}：进 Journal 的是本原型自己词表那一份"
            + "（PlaceBuilding / HealBuilding / DamageBuilding …），模块词表那一份只给动词名与参数次序"
            + "（D34「一份表、两处问」）。本原型的血量写口是 LogicWorld.DebugHealBuilding，"
            + "编辑 / 暂停 / 批量 / 布局与只读查询都还没接线。");
}
