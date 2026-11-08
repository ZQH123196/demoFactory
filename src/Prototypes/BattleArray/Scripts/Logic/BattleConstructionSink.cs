#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using DemoFactory.GugaFramework.Construction;

namespace DemoFactory.Prototypes.BattleArray.Logic;

// 营建动词的薄适配器 —— 公共建造模块那一份词表（「一份表、两处问」）在本原型的落地。
//
// 分工照 ConstructionVerbs 的先例：
//   · 动词名、参数次序、缺省值、竖线切分、什么时候该抛 —— 全在 ConstructionVerbs
//     （本文件一个动词名都不另抄，只引它的常量）；
//   · 本文件只回答两件事：①「这一侧接得住哪几条」（SupportedVerbs），
//     ②调用世界的那一次写路径。
//
// 路障没有载荷：模块存载荷、不读它、原样归还（D31），而这一条建筑不进经济也没有功能，
// 所以这一侧整段不挂载荷 —— 给了就是作者错误，当场抛。

/// <summary>
/// 营建动词的薄适配器（原型侧那一份）—— <see cref="IConstructionVerbSink"/> 的实现。
///
/// <para><b>只接最小集三条</b>：<see cref="ConstructionVerbs.Place"/>（布阵期摆一座路障）、
/// <see cref="ConstructionVerbs.Remove"/>（撤掉一座）、
/// <see cref="ConstructionVerbs.Check"/>（只读裁决，与落建同一条流水线）。
/// 名单外的动词名由 <see cref="ConstructionVerbs.Dispatch"/> 当场抛（口径 2：不适用要写明理由，
/// 不拿 false 表达「没这一格」）—— 编辑 / 暂停 / 血量 / 批量 / 布局都还没接线。</para>
///
/// <para><b>前置条件在世界那一侧</b>（<c>BattleWorld.CanPlaceRoadblock</c>）：建造模块不认识
/// 「布阵期」这个词，而「什么时候允许改阵面」是原型的规则。前置条件在这一层挡下时回
/// <see cref="PlacementReason.InvalidState"/> —— 拒绝是读数不是异常，它会进 Journal 的 <c>ok</c> 列。</para>
///
/// <para>词表转发与调试口走的是<b>同一个</b>世界入口（<c>BattleWorld.PlaceRoadblockCore</c>），
/// 不是两条路（「桥只转发」：调试桥只转发既有直达方法）。</para>
/// </summary>
public sealed class BattleConstructionSink : IConstructionVerbSink
{
    private static readonly HashSet<string> Verbs = new(StringComparer.Ordinal)
    {
        ConstructionVerbs.Place, ConstructionVerbs.Remove, ConstructionVerbs.Check,
    };

    private readonly BattleWorld _world;

    public BattleConstructionSink(BattleWorld world) => _world = world;

    public string HostLabel => "排兵布阵";

    public IReadOnlySet<string> SupportedVerbs => Verbs;

    /// <summary>
    /// 摆一座路障 —— 薄适配器只搬参数、只调世界那一次写路径；判定全在模块那一份裁决里。
    /// <b>前置条件先问</b>：不在布阵期就回 <see cref="PlacementReason.InvalidState"/>，一寸不动。
    /// </summary>
    public WriteResult PlaceBuilding(string blueprintId, double x, double y, int orientation,
        string? buildingId, long? buildDurationTicks, string? payloadText)
    {
        if (!_world.CanPlaceRoadblock(out _))
        {
            return WriteResult.Fail(PlacementReason.InvalidState, buildingId ?? string.Empty);
        }

        if (payloadText is not null and not "" and not "-")
        {
            throw new FormatException(
                $"路障不挂载荷，收到「{payloadText}」：它不进经济、也没有产出功能，"
                + "整段要给就写 -（兜一个空载荷等于让「这一座干什么」有两个答案）");
        }

        return _world.PlaceRoadblockCore(blueprintId, new Vector2((float)x, (float)y), buildingId,
            buildDurationTicks);
    }

    /// <summary>撤掉一座（<b>同一个前置条件</b>：开打之后阵面已经定下，撤也撤不动）。</summary>
    public WriteResult RemoveBuilding(string buildingId, RemovalCause cause)
        => _world.CanPlaceRoadblock(out _)
            ? _world.RemoveRoadblock(buildingId, cause)
            : WriteResult.Fail(PlacementReason.InvalidState, buildingId);

    /// <summary>放不放得下那一行的读数（只读，不入录）。与落建口<b>同一个前置条件</b>，所以
    /// 不会出现「预览说行、点下去被拒」那种两处各一个答案。</summary>
    public string CheckPlacement(string blueprintId, double x, double y, int orientation)
        => _world.CheckRoadblockPlacement(blueprintId, x, y, orientation);

    // ---- 这一轮不接的那几条：当场抛，并写明为什么（口径 2）------------------------

    public WriteResult MoveBuilding(string buildingId, double x, double y, int? orientation)
        => throw Unsupported("MoveBuilding");

    public WriteResult RotateBuilding(string buildingId, int orientation) => throw Unsupported("RotateBuilding");

    /// <summary>扣血 —— <b>这一侧一条都没有接</b>：路障没有攻击路径，谁都不打它（spec Q14）。</summary>
    public WriteResult DamageBuilding(string buildingId, double amount) => throw Unsupported("DamageBuilding");

    public WriteResult RepairBuilding(string buildingId, double amount) => throw Unsupported("RepairBuilding");

    public WriteResult SetHealthBuilding(string buildingId, double hp) => throw Unsupported("SetHealthBuilding");

    public WriteResult PauseBuild(string buildingId) => throw Unsupported("PauseBuild");

    public WriteResult ResumeBuild(string buildingId) => throw Unsupported("ResumeBuild");

    public WriteResult RegisterFixture(string blueprintId, double x, double y, int orientation,
        string buildingId, string? payloadText)
        => throw new NotSupportedException(
            "排兵布阵这一侧不接 RegisterFixture：本原型没有场地建筑 —— 路障全部由玩家手势摆下"
            + "（走 PlaceBuilding 那一格），世界构造期一座都不登记。");

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

    public string GizmoParams() => throw Unsupported("GizmoParams");

    private static NotSupportedException Unsupported(string verb)
        => new($"排兵布阵这一侧不接 {verb}：本原型只有路障的落建、拆除与只读裁决"
            + "（PlaceBuilding / RemoveBuilding / CheckPlacement），编辑 / 暂停 / 血量 / 批量 / 布局"
            + "与其余只读查询都还没接线。");
}
