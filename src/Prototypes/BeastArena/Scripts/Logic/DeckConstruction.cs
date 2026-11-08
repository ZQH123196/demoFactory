#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using DemoFactory.GugaFramework.Construction;

namespace DemoFactory.Prototypes.BeastArena.Logic;

// 甲板建造那几个动作：落座、拔起、落位、施工 3 人启停、备件修房。
//
// 三条写路径的分工（照模块那一套）：
//   · 场地建筑 = RegisterFixture：跳校验、进度直接完工、不发 Completed、不走 Journal；
//   · 玩家建筑 = Commit：校验、占格、发 BuildingPlaced；
//   · 挪位     = Move：原子，占用检查排除自己，拒绝一寸不动。
// 坐标换算只有一条：格号经 DeckBlueprints.CandidateOf 落成 Vector2，再交模块吸一次。

/// <summary>法术停滞那一类占位消耗用掉的法力格。</summary>
public enum ManaOccupancy
{
    /// <summary>建筑拔起悬空（<c>LiftBuilding</c> 占住，<c>DropBuilding</c> 归还）。</summary>
    Stasis = 0,
}

/// <summary>
/// 甲板建造动作那一层 —— 模块之上、薄适配器之下的一层。
///
/// <para><b>它只做「什么时候发哪一发」这件事</b>：落座后立刻 <c>PauseBuild</c>，
/// 施工岗位派满 3 人改 <c>ResumeBuild</c>、不足 3 人再 <c>PauseBuild</c>；
/// 拔起与落位各占与还 1 个法力格。真正的放置、占格、进度倒数全在模块里。</para>
///
/// <para><b>修房那一口</b>：耗 1 备件加 1 名在岗工作人员，施工
/// <see cref="ArenaTuning.RepairGameMinutes"/> 个游戏分钟，完成后该建筑 +50 耐久（不超过上限）。
/// 备件与工人在这一层只做「够不够」的判定与记账，扣哪本账归账本那一侧。</para>
/// </summary>
public sealed partial class DeckConstruction
{
    private readonly ConstructionCore _core;
    private readonly Func<GridCell, bool> _deckCellInBounds;
    private readonly Dictionary<string, int> _workers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _lifted = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _repairDoneTick = new(StringComparer.Ordinal);
    private readonly List<(string BuildingId, long DoneTick)> _repairs = [];
    private readonly int[] _manaOccupied = new int[Enum.GetValues<ManaOccupancy>().Length];
    private long _nextBuildingNumber = 1;

    /// <summary>
    /// 接建造模块那本册（网格放置规则与十三件蓝图已由调用方递进去）。
    /// <paramref name="deckCellInBounds"/> 是当前位阶的建造边界判口 ——
    /// 门点探测要问活动边界，不问一阶那份静态缺省（二阶扩容后新圈上的建筑门才探得出）。
    /// </summary>
    public DeckConstruction(ConstructionCore core, Func<GridCell, bool>? deckCellInBounds = null)
    {
        ArgumentNullException.ThrowIfNull(core);
        _core = core;
        _deckCellInBounds = deckCellInBounds ?? DeckBlueprints.IsInTier1Bounds;
    }

    /// <summary>建造模块本体（读数与调试用）。</summary>
    public ConstructionCore Core => _core;

    /// <summary>
    /// 十三件蓝图 ↔ 开局固定建筑 id 的<b>唯一一张表</b>（蓝图 id → 固定 id）。
    /// 两个方向都从它派生：两张反向 switch 漂开之后，必然有一头把固定 id 喂给只认蓝图 id 的查找。
    /// 新建的 <c>bN</c> 没有固定 id，所以查这一类建筑要走 <see cref="FindByBlueprint"/>。
    /// </summary>
    private static readonly (string BlueprintId, string FixedId)[] FixedIdTable =
    [
        ("TownHall", ArenaWorld.TownHallId),
        ("Arena", ArenaWorld.ArenaId),
        ("Church", ArenaWorld.ChurchId),
        ("Granary", ArenaWorld.GranaryId),
        ("Restaurant", ArenaWorld.RestaurantId),
        ("Hotel", ArenaWorld.HotelId),
        ("Dormitory", ArenaWorld.DormitoryId),
        ("Workshop", ArenaWorld.WorkshopId),
        ("TeleportArray", ArenaWorld.TeleportArrayId),
        ("TrainingGround", "training_ground"),
        ("Stable", "stable"),
        ("ArcheryRange", "archery_range"),
        ("AirshipDock", "airship_dock"),
    ];

    private static readonly Dictionary<string, string> FixedIdByBlueprint = FixedIdTable
        .ToDictionary(pair => pair.BlueprintId, pair => pair.FixedId, StringComparer.Ordinal);

    private static readonly Dictionary<string, string> BlueprintByFixedId = FixedIdTable
        .ToDictionary(pair => pair.FixedId, pair => pair.BlueprintId, StringComparer.Ordinal);

    /// <summary>蓝图 id → 固定建筑 id（没有固定 id 的蓝图回空串）。</summary>
    public static string FixedIdOf(string blueprintId)
        => FixedIdByBlueprint.TryGetValue(blueprintId, out var id) ? id : string.Empty;

    /// <summary>固定建筑 id → 蓝图 id（与 <see cref="FixedIdOf"/> 是同一张表，不另写反向 switch）。</summary>
    public static string BlueprintOfFixedId(string buildingId)
        => BlueprintByFixedId.TryGetValue(buildingId, out var blueprintId) ? blueprintId : string.Empty;

    /// <summary>
    /// 按蓝图 id 查这一栋的建筑 id：<b>先找固定 id，找不到再按蓝图找</b>。
    /// 新建的 <c>bN</c> 因此可用；固定 id 那一栋被拆掉之后自动回退到同蓝图的新建那一栋。
    /// 不在册回空串。
    /// </summary>
    public string FindByBlueprint(string blueprintId)
    {
        if (blueprintId.Length == 0) return string.Empty;

        var fixedId = FixedIdOf(blueprintId);
        if (fixedId.Length > 0)
        {
            var fixedBuilding = _core.Find(fixedId);
            if (fixedBuilding is not null && fixedBuilding.BlueprintId == blueprintId) return fixedId;
        }

        foreach (var id in _core.Ids)
        {
            var building = _core.Find(id);
            if (building is not null && building.BlueprintId == blueprintId) return id;
        }

        return string.Empty;
    }

    /// <summary>
    /// 一个「建筑 id 或蓝图 id」→ 在册的建筑 id。
    /// 日常走位那一侧递进来的是固定 id（<c>dormitory</c>），而宿舍可能是新建的 <c>bN</c>；
    /// 先在册直接命中，再按蓝图找。
    /// </summary>
    public string ResolveBuildingId(string buildingIdOrBlueprintId)
    {
        if (buildingIdOrBlueprintId.Length == 0) return string.Empty;
        if (_core.Find(buildingIdOrBlueprintId) is not null) return buildingIdOrBlueprintId;

        // 递进来的可能是固定 id（dormitory）—— FindByBlueprint 只认蓝图 id（Dormitory），
        // 所以先过同一张表翻成蓝图 id；本来就是蓝图 id 的那一档原样用。
        var blueprintId = BlueprintOfFixedId(buildingIdOrBlueprintId);
        if (blueprintId.Length == 0) blueprintId = buildingIdOrBlueprintId;
        return FindByBlueprint(blueprintId);
    }

    /// <summary>这一栋的蓝图 id（不在册回空串）。转职清岗用它把「建筑 id」翻成「蓝图 id」。</summary>
    public string BlueprintIdOf(string buildingId) => _core.Find(buildingId)?.BlueprintId ?? string.Empty;

    /// <summary>备件存量（修房那一口的原料）。</summary>
    public int SpareParts { get; private set; } = ArenaTuning.StartingSpareParts;

    /// <summary>在建建筑的施工岗位上有几个人（按建筑 id）。</summary>
    public IReadOnlyDictionary<string, int> Workers => _workers;

    /// <summary>处于「法术停滞」悬空态的建筑（按建筑 id，无序读数用途）。</summary>
    public IReadOnlyCollection<string> Lifted => _lifted;

    /// <summary>法力总格数。</summary>
    public const int ManaCapacity = ArenaTuning.ManaCapacity;

    /// <summary>已被占住法力格的那几档，各占几格。</summary>
    public int ManaUsed(ManaOccupancy kind) => _manaOccupied[(int)kind];

    /// <summary>当前可用法力格 = 总数 - 占位那一档。</summary>
    public int ManaAvailable => ManaCapacity - _manaOccupied[(int)ManaOccupancy.Stasis];

    /// <summary>占住 1 个法力格；不够就返回 false（一个子儿不动）。</summary>
    public bool TryOccupyMana(ManaOccupancy kind)
    {
        if (ManaAvailable < 1) return false;
        _manaOccupied[(int)kind]++;
        return true;
    }

    /// <summary>归还 1 个法力格。已经归零的那一档不扣成负数。</summary>
    public void ReleaseMana(ManaOccupancy kind)
    {
        var slot = (int)kind;
        if (_manaOccupied[slot] > 0) _manaOccupied[slot]--;
    }

    /// <summary>
    /// 登记开局 6 栋默认建筑（完工态，不进 Journal）。调在接地形接线之后 ——
    /// 阻挡从建造事件流里落，接线之前砌的墙这一局永远不在网格里。
    /// </summary>
    public void RegisterStartingFixtures()
    {
        foreach (var (buildingId, blueprintId, col, row) in DeckBlueprints.StartingFixtures)
        {
            var at = DeckBlueprints.CandidateOf(new GridCell(col, row));
            var result = _core.RegisterFixture(blueprintId, at, buildingId);
            if (!result.Ok)
            {
                throw new InvalidOperationException(
                    $"开局建筑 {buildingId}（{blueprintId}）登记不成：{result.Reason}");
            }
        }
    }

    /// <summary>
    /// 把一座<b>已经出册</b>的建筑按原样请回册上（最终重试还原专用，不进 Journal）。
    ///
    /// <para>与开局那 6 栋同一口 <c>RegisterFixture</c>：完工态、跳过间距校验、
    /// 占回原来那几格（阻挡从建造事件流里落，与开局登记同一条路）。
    /// <b>在册的别叫这一发</b>—— 重名在核心那一侧是当场抛。</para>
    ///
    /// <para><paramref name="insertAt"/> 把它落回册上原来那一格：读数按册序逐栋印，
    /// 补到末尾会让「恢复后」与「首次开战」差一整截。</para>
    /// </summary>
    internal void ReRegisterFixture(string blueprintId, int col, int row, string buildingId, int insertAt)
    {
        var at = DeckBlueprints.CandidateOf(new GridCell(col, row));
        var result = _core.RegisterFixture(blueprintId, at, buildingId, insertAt: insertAt);
        if (!result.Ok)
        {
            throw new InvalidOperationException(
                $"还原建筑 {buildingId}（{blueprintId}）登记不成：{result.Reason}");
        }
    }

    /// <summary>
    /// 落座一座玩家建筑：校验 → 占格 → 落座后立刻 <c>PauseBuild</c>（等人派满岗位数再开工）。
    /// <b>0 岗建筑当场恢复施工</b>：它不靠岗位建，落座即暂停会让它永远卡在施工。
    /// </summary>
    /// <param name="blueprintId">蓝图 id。</param>
    /// <param name="col">锚点列号。</param>
    /// <param name="row">锚点行号。</param>
    /// <param name="buildingId">建筑 id；不给就按 b1、b2…… 自动发。</param>
    public WriteResult PlaceBuilding(string blueprintId, int col, int row, string? buildingId = null)
    {
        var anchor = new GridCell(col, row);
        var candidate = DeckBlueprints.CandidateOf(anchor);
        var verdict = _core.Check(blueprintId, candidate);
        if (!verdict.Ok)
        {
            return WriteResult.Fail(verdict.Reason, buildingId ?? "");
        }

        var id = buildingId ?? NextBuildingId();
        var placed = _core.Commit(blueprintId, candidate, orientation: 0, buildingId: id);
        if (!placed.Ok)
        {
            return WriteResult.Fail(placed.Reason, id);
        }

        // 落座即暂停：派不满岗位数就停在那儿（派满了由 SyncBuildProgress 改回来）；
        // 0 岗建筑在下一句里当场恢复 —— 它不靠岗位施工。
        _core.PauseBuild(id);
        _workers[id] = 0;
        SyncBuildProgress(id);
        return WriteResult.Done(id);
    }

    /// <summary>
    /// 法术停滞拔起：记成悬空态并占住 1 个法力格，<b>在建造模块里保持原位</b>
    /// （位置真值是模块册上那一行，拔起这一发不碰它）。
    /// </summary>
    public WriteResult LiftBuilding(string buildingId)
    {
        var building = _core.Find(buildingId);
        if (building is null) return WriteResult.Fail(PlacementReason.UnknownId, buildingId);
        if (_lifted.Contains(buildingId)) return WriteResult.Fail(PlacementReason.InvalidState, buildingId);
        if (!TryOccupyMana(ManaOccupancy.Stasis)) return WriteResult.Fail(PlacementReason.InvalidState, buildingId);

        _lifted.Add(buildingId);
        return WriteResult.Done(buildingId);
    }

    /// <summary>
    /// 落位目标是不是它自己现在这几格（是 = 放回原处，位置与占用一寸不动）。
    ///
    /// <para><b>为什么要有这一口</b>：拔起<b>不</b>释放格（位置真值是模块册上那一行），
    /// 所以「放回原处」这一发按普通的占用检查看是「这一格被占着」，而放回原处是一个空动作，
    /// 按非法落位拒掉是错的 —— 更糟的是那样一栋建筑就永远蹲在自己那几格上，谁也建不了。</para>
    ///
    /// <para><b>为什么这一口放不开「挪到别人头上」</b>：它只认<b>锚点相同</b>，
    /// 而锚点相同就意味着那几格只可能占着它自己（占用表一格一名）。锚点不同但压着它自己的格
    /// 那一档仍走下面那条 <c>Check</c>，照旧按占着拒。</para>
    /// </summary>
    public bool IsAtOwnAnchor(string buildingId, int col, int row)
    {
        var building = _core.Find(buildingId);
        return building?.Anchor == new GridCell(col, row);
    }

    /// <summary>
    /// 落位：迁往新锚点并归还 1 个法力格。格号先各自过 <c>Check</c>，OK 才发 <c>Move</c>；
    /// 模块那一发是原子的，失败一寸不动。
    ///
    /// <para><b>放回自己那几格</b>（<see cref="IsAtOwnAnchor"/>）走的是另一条路：
    /// 位置与占用都不动，<b>不发 <c>Move</c></b> —— 挪位事件流里没有「挪到自己原地」这一档 ——
    /// 只解悬空态并归还 1 个法力格。</para>
    /// </summary>
    public WriteResult DropBuilding(string buildingId, int col, int row)
    {
        var building = _core.Find(buildingId);
        if (building is null) return WriteResult.Fail(PlacementReason.UnknownId, buildingId);
        if (!_lifted.Contains(buildingId)) return WriteResult.Fail(PlacementReason.InvalidState, buildingId);

        var anchor = new GridCell(col, row);

        // 放回原处：位置与占用一寸不动，只解悬空并还法力格。
        if (building.Anchor == anchor)
        {
            _lifted.Remove(buildingId);
            ReleaseMana(ManaOccupancy.Stasis);
            return WriteResult.Done(buildingId);
        }

        var candidate = DeckBlueprints.CandidateOf(anchor);
        var verdict = _core.Check(building.BlueprintId, candidate);
        if (!verdict.Ok)
        {
            return WriteResult.Fail(verdict.Reason, buildingId);
        }

        var moved = _core.Move(buildingId, candidate, orientation: null);
        if (!moved.Ok)
        {
            return WriteResult.Fail(moved.Reason, buildingId);
        }

        _lifted.Remove(buildingId);
        ReleaseMana(ManaOccupancy.Stasis);
        return WriteResult.Done(buildingId);
    }

    /// <summary>
    /// 施工岗位人数变更 → 阈值 <c>min(3, max(1, 岗位数))</c>：派满阈值 <c>ResumeBuild</c>，不足再 <c>PauseBuild</c>。
    /// <b>0 岗建筑不靠岗位施工</b>：它永远算「派满」，不会卡在施工。
    /// 建造模块那一侧不回 ok 是正常的（已在该有的状态上再发一次就是 InvalidState），
    /// 所以这一口按「目标状态是什么」判，不按那一发的返回值判。
    /// </summary>
    public void SetBuildWorkers(string buildingId, int workers)
    {
        if (workers < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workers), workers, "施工岗位上的人数不能是负数");
        }

        _workers[buildingId] = workers;
        SyncBuildProgress(buildingId);
    }

    /// <summary>按当前登记的人数重新判一次启停。</summary>
    public void SyncBuildProgress(string buildingId)
    {
        var building = _core.Find(buildingId);
        if (building is null) return;

        var posts = DeckBlueprints.SpecOf(building.BlueprintId)?.Posts ?? 0;
        var workers = _workers.TryGetValue(buildingId, out var n) ? n : 0;
        var full = posts <= 0 || workers >= ArenaTuning.BuildWorkerThresholdFor(posts);

        if (full && building.Paused)
        {
            _core.ResumeBuild(buildingId);
            return;
        }

        if (!full && !building.Paused)
        {
            _core.PauseBuild(buildingId);
        }
    }

    /// <summary>
    /// 修房：耗 1 备件加 1 名在岗工作人员，施工 <see cref="ArenaTuning.RepairGameMinutes"/> 个游戏分钟，
    /// 完成后 +<see cref="ArenaTuning.RepairDurability"/> 耐久（钳到该建筑上限）。
    /// </summary>
    /// <returns>失败给读数（备件不够 / 人不在岗 / 已在修 / 满耐久）。</returns>
    public WriteResult RepairBuilding(string buildingId, long tick, bool workerOnDuty = true)
    {
        var building = _core.Find(buildingId);
        if (building is null) return WriteResult.Fail(PlacementReason.UnknownId, buildingId);
        if (building.Health >= building.MaxHealth) return WriteResult.Fail(PlacementReason.InvalidState, buildingId);
        if (SpareParts < ArenaTuning.RepairSpareParts) return WriteResult.Fail(PlacementReason.InvalidState, buildingId);
        if (!workerOnDuty) return WriteResult.Fail(PlacementReason.InvalidState, buildingId);
        if (_repairDoneTick.ContainsKey(buildingId)) return WriteResult.Fail(PlacementReason.InvalidState, buildingId);

        SpareParts -= ArenaTuning.RepairSpareParts;
        var done = tick + (ArenaTime.StepsPerGameMinute * ArenaTuning.RepairGameMinutes);
        _repairDoneTick[buildingId] = done;
        _repairs.Add((buildingId, done));
        return WriteResult.Done(buildingId);
    }

    /// <summary>修房完成结算：到点的那几座补耐久，一座最多同时修一次。</summary>
    public void AdvanceRepairs(long tick)
    {
        for (var i = _repairs.Count - 1; i >= 0; i--)
        {
            var (buildingId, done) = _repairs[i];
            if (tick < done) continue;

            _repairs.RemoveAt(i);
            _repairDoneTick.Remove(buildingId);
            _core.Repair(buildingId, ArenaTuning.RepairDurability);
        }
    }

    /// <summary>这一栋有没有一处在修的活。</summary>
    public bool IsRepairing(string buildingId) => _repairDoneTick.ContainsKey(buildingId);

    /// <summary>
    /// 魔晶修理：<b>立即完成</b>这一栋正在进行的修理（替代一次工坊修理）。
    /// 只接「正在修」的那一档 —— 备件与工时都不再走一遍，耐久补同一档。
    /// </summary>
    /// <returns>确实完成了一处修理返回 true；这一栋没在修返回 false。</returns>
    public bool FinishRepairNow(string buildingId)
    {
        if (!_repairDoneTick.Remove(buildingId)) return false;

        for (var i = _repairs.Count - 1; i >= 0; i--)
        {
            if (_repairs[i].BuildingId != buildingId) continue;
            _repairs.RemoveAt(i);
            break;
        }

        _core.Repair(buildingId, ArenaTuning.RepairDurability);
        return true;
    }

    /// <summary>这座建筑的门口点世界落点（南侧那一格，人从甲板南边走过来撞门）。</summary>
    public Vector2 DoorPointOf(string buildingId)
    {
        var building = _core.Find(buildingId);
        if (building is null || building.Anchor is not { } anchor)
        {
            throw new ArgumentException(
                $"{buildingId} 不在册或没有锚点格，算不出门口点", nameof(buildingId));
        }

        return DoorPointOf(building.BlueprintId, anchor);
    }

    /// <summary>蓝图与锚点 → 门口点世界落点（「这一格有没有别人占着」现问建造模块，边界问当前位阶那一份）。</summary>
    public Vector2 DoorPointOf(string blueprintId, GridCell anchor)
        => DeckBlueprints.DoorPointOf(blueprintId, anchor, cell =>
        {
            if (!_deckCellInBounds(cell)) return false;
            var at = DeckBlueprints.CandidateOf(cell);
            return _core.BuildingIdAt(at.X, at.Y) == "";
        });

    /// <summary>存档/读档用：把步号较后的那一档读数念出来（调试口，不进快照）。</summary>
    public string Readout()
    {
        var lifted = _lifted.Count == 0 ? "-" : string.Join(",", _lifted);
        return string.Format(CultureInfo.InvariantCulture,
            "DECK 在册={0} 备件={1} 悬空={2} 法力={3}/{4} 修房={5}",
            _core.Count, SpareParts, lifted, ManaAvailable, ManaCapacity, _repairs.Count);
    }

    private string NextBuildingId()
    {
        // 自动发号按 b1、b2…… 递增，跳过已占的那些（fixture 的 id 是关卡给定的，不在这个序列里）。
        while (true)
        {
            var id = "b" + _nextBuildingNumber.ToString(CultureInfo.InvariantCulture);
            _nextBuildingNumber++;
            if (_core.Find(id) is null) return id;
        }
    }
}
