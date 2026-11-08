#nullable enable
using System;
using DemoFactory.GugaFramework.Pathfinding;

namespace DemoFactory.Prototypes.BeastArena.Logic;

// 三级搬运单的四个产地 —— 生产代码里唯一会调 `HaulQueue.Enqueue` 的那一头。
//
// **为什么单独一件**：派单与交单都在 `ArenaWorld` 里（那一头是派单那一格与交单那一格），
// 而「什么时候该有一张单」是另一件事 —— 它读的是世界里的几处真值（场上还没搬走的尸体、
// 巨兽饱食度、建筑耐久、土壤册与电梯那一本「已运走」的账）。把这四条判定与入队收在一件里，
// 每一档各占一格，改一档不牵动另外三档。
//
// 四条口径：
//   · **尸体 → 教堂**：`BattleSim.Die` 那一发立刻排一张（负面情绪圈从那一刻起在清）。
//   · **备件 → 修房**：某一栋耐久没满、不在修、账上有备件、场上没有第二张备件单在途。
//   · **魔晶 → 镇政府仓库**：生态产出的魔晶落在甲板指定位置，逐件排一张入库单。
//   · **地面散货 → 镇政府仓库**：电梯结算那一格把「累计 − 已运走」还留在地下的整件
//     落成地面上一件货并排单 —— 电梯名额只有 4 个，排不到的那些格靠人搬。
//
// **防单海的四条读数**（都在排单之前判，不在派单那一格裁）：
//   ① 同一件货不排第二张；② 同一档载荷在途只留一张（同一类需求一张单）；
//   ③ 货不在册不排；④ 取货点或投递那一栋不在册不排。
//
// **每一张单都走玩家那一发的同一条投递写路径**（<c>ArenaSetup.DeliverItem</c>），
// 这一件只管「该不该有这张单」，不自己写投递（「桥只转发」）。

/// <summary>三级搬运单的四个产地。</summary>
public sealed class HaulOrders
{
    /// <summary>散货那一档的载荷标识符（<see cref="PointItem.Payload"/> 上那几个词）。</summary>
    private const string SparePartPayload = "sparepart";

    private const string CrystalPayload = "crystal";

    private const string OrePayload = "ore";

    private const string MaterialPayload = "material";

    /// <summary>每一单搬多少（一件 ore / 一份 food / 一个 sparepart）。</summary>
    private const double AmountPerOrder = 1.0;

    private readonly ArenaWorld _world;

    /// <summary>接世界本体（点位物册、账本、活物册、建造册与电梯都在那一头）。</summary>
    public HaulOrders(ArenaWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
    }

    /// <summary>
    /// 尸体那一发（<c>BattleSim.Die</c>）调它 —— 尸体一生成就有一张单，
    /// 甲板上那一圈负面情绪从那一刻起有人在清。
    /// </summary>
    public void RaiseCorpseOrder(PointItem corpse)
    {
        ArgumentNullException.ThrowIfNull(corpse);

        if (corpse.Removed) return;                                  // ③ 货不在册不排

        // ④ 投递那一栋不在册不排：教堂可能是玩家新建的 bN，先按蓝图找固定 id 兜底。
        var churchId = _world.FindByBlueprint("Church");
        if (churchId.Length == 0) return;
        if (OutstandingItem(HaulLevel.CorpseToChurch, corpse.Id)) return;        // ① 同一具不排第二张

        _world.Haul.Enqueue(NewOrder(HaulLevel.CorpseToChurch, corpse.Id,
            PickupAt(corpse.AtDeck), churchId));
    }

    /// <summary>
    /// 每一逻辑步调它 —— 备件修房那个需求，缺了就补一张，
    /// 在途的那一张还在就不再补（②）。
    ///
    /// <para><b>魔晶入库单排在这一格的最前</b>：它是已经落在甲板上的货，搬运照常那一档
    /// 管的就是这类「已经在场」的单 —— 停摆期也照排。</para>
    ///
    /// <para><b>停摆期这一格不排新单</b>：停摆期冻的是「经营」，已经排进队列的那些单
    /// 照常派、照常走、照常投（「搬运照常」那一档盯的正是队列里已有的单）；
    /// 而「这一栋还缺耐久」这类新需求要变成新的一单，是新开一笔活 ——
    /// 停摆的巨兽上不新开工。这条只管「要不要开新单」，管不了已经在途的那些。</para>
    /// </summary>
    public void RaiseStandingOrders()
    {
        RaiseCrystalOrder();
        if (_world.InStandstill) return;

        RaiseRepairOrder();
    }

    /// <summary>
    /// 电梯结算那一格调它 —— 把电梯这一轮名额排不到、仍留在地下的整件落成地面散货并排单，
    /// 顺带把「本航段还欠多少矿石 / 特殊材料」那一对读数刷新。
    ///
    /// <para><b>次序是契约</b>：先搬料再刷读数 —— 读数要报搬完之后剩下的那一份，
    /// 加上甲板上还没入库的那几件（已搬上甲板但没人搬走的那一批也算「还欠」）。</para>
    /// </summary>
    public void RaiseGroundCargoOrders()
    {
        LiftGroundCargo(OrePayload);
        LiftGroundCargo(MaterialPayload);

        _world.RecordPendingGroundCargo(
            _world.Elevator.OreBacklog(_world.Soil) + LiveCargoCount(OrePayload),
            _world.Elevator.MaterialBacklog(_world.Soil) + LiveCargoCount(MaterialPayload));
    }

    // ---- 备件 → 修房 ----

    /// <summary>
    /// 备件修房那一档：<b>某一栋耐久没满、不在修、镇政府仓库里还有备件</b> 就排一张。
    ///
    /// <para><b>取货点钉在镇政府门口</b> —— 备件那一本账是镇政府的账（<c>Town.SparePart</c>），
    /// 投进目标那一栋时扣的也是它（<c>ArenaSetup.DeliverItem</c> 那一档）。</para>
    /// </summary>
    private void RaiseRepairOrder()
    {
        if (_world.Town.SparePart < AmountPerOrder) return;
        if (_world.Construction.Find(ArenaWorld.TownHallId) is null) return;
        if (OutstandingPayload(HaulLevel.SparePartToRepair, SparePartPayload)) return;

        var target = FirstBuildingNeedingRepair();
        if (target.Length == 0) return;

        var hallDoor = _world.DoorDeckOf(ArenaWorld.TownHallId);
        var item = _world.Items.Add(PointItemKind.Cargo, hallDoor, SparePartPayload);
        _world.Haul.Enqueue(NewOrder(HaulLevel.SparePartToRepair, item.Id,
            PickupAt(item.AtDeck), target));
    }

    /// <summary>按建造册次序取第一栋「耐久没满且没在修」的（在册次序固定，重放逐字可复现）。</summary>
    private string FirstBuildingNeedingRepair()
    {
        foreach (var id in _world.Construction.Ids)
        {
            var building = _world.Construction.Find(id);
            if (building is null) continue;
            if (building.Health >= building.MaxHealth) continue;
            if (_world.Deck.IsRepairing(id)) continue;
            return id;
        }

        return string.Empty;
    }

    // ---- 魔晶 → 镇政府仓库 ----

    /// <summary>
    /// 甲板上有魔晶就排一张入库单（<b>逐件</b>）：同档在途已有魔晶单就不再补，
    /// 一张单搬一件 —— 工人与帮工都领这一类单，上帝之手抓走的那一件不在这里排。
    /// </summary>
    private void RaiseCrystalOrder()
    {
        if (_world.Construction.Find(ArenaWorld.TownHallId) is null) return;
        if (OutstandingPayload(HaulLevel.GroundCargoToWarehouse, CrystalPayload)) return;

        foreach (var item in _world.Items.Live)
        {
            if (item.Kind != PointItemKind.Crystal) continue;

            _world.Haul.Enqueue(NewOrder(HaulLevel.GroundCargoToWarehouse, item.Id,
                PickupAt(item.AtDeck), ArenaWorld.TownHallId));
            return;
        }
    }

    // ---- 地面散货 → 镇政府仓库 ----

    /// <summary>
    /// 把电梯名额排不到、仍留在地下的第一件整料搬上甲板并排一张单。
    ///
    /// <para><b>「第一件」按电梯那一头同一份固定次序取</b>（象限 0..3 再层 1..3）——
    /// 两条路取料次序一致，账才对得上；取走即记进电梯那本「已运走」的账，
    /// 电梯下一轮不会再运它（不重不漏）。</para>
    /// </summary>
    private void LiftGroundCargo(string payload)
    {
        if (_world.Construction.Find(ArenaWorld.TownHallId) is null) return;
        if (OutstandingPayload(HaulLevel.GroundCargoToWarehouse, payload)) return;

        // 地面散货的取货点钉在镇政府电梯口 —— 矿从地下上来先落在那儿，等电梯或等人搬。
        var stash = DeckGeometry.CaveSpawn();
        foreach (var (sector, layer) in MagicElevator.CellOrder())
        {
            if (!_world.Soil.IsDug(sector, layer)) continue;

            var taken = payload == OrePayload
                ? _world.Elevator.TakeOreOffCell(_world.Soil, sector, layer, AmountPerOrder)
                : _world.Elevator.TakeMaterialOffCell(_world.Soil, sector, layer, AmountPerOrder);
            if (taken < AmountPerOrder) continue;

            var item = _world.Items.Add(PointItemKind.Cargo, stash, payload);
            _world.Haul.Enqueue(NewOrder(HaulLevel.GroundCargoToWarehouse, item.Id,
                PickupAt(item.AtDeck), ArenaWorld.TownHallId));
            return;
        }
    }

    // === 防单海那几条要读的三样东西 ===

    /// <summary>某一档里有没有一张单指的是这一件货（排队或在途都算「在途」）。</summary>
    private bool OutstandingItem(HaulLevel level, string itemId)
    {
        foreach (var order in _world.Haul.Pending)
        {
            if (order.Level == level && order.ItemId == itemId) return true;
        }

        foreach (var u in _world.Roster.Units)
        {
            var inHand = _world.CarryingOf(u.Id);
            if (inHand is not null && inHand.Level == level && inHand.ItemId == itemId) return true;
        }

        return false;
    }

    /// <summary>某一档里有没有一张单装的是这一种载荷（在途一张 = 那类需求已经有着落）。</summary>
    private bool OutstandingPayload(HaulLevel level, string payload)
    {
        foreach (var order in _world.Haul.Pending)
        {
            if (order.Level == level && PayloadOf(order.ItemId) == payload) return true;
        }

        foreach (var u in _world.Roster.Units)
        {
            var inHand = _world.CarryingOf(u.Id);
            if (inHand is not null && inHand.Level == level && PayloadOf(inHand.ItemId) == payload) return true;
        }

        return false;
    }

    private string PayloadOf(string itemId)
    {
        var item = _world.Items.Find(itemId);
        return item is null ? string.Empty : item.Payload;
    }

    private double LiveCargoCount(string payload)
    {
        var n = 0.0;
        foreach (var item in _world.Items.Live)
        {
            if (item.Kind != PointItemKind.Cargo || item.Payload != payload) continue;
            n += AmountPerOrder;
        }

        return n;
    }

    private HaulOrder NewOrder(HaulLevel level, string itemId, NavPoint origin, string target)
        => ArenaWorld.Order(level, itemId, origin, target, AmountPerOrder, _world.Tick);

    /// <summary>
    /// 取货点落到<b>那一格的格心</b>上（<see cref="ArenaWorld.NearestFreeDeckCell"/>）。
    ///
    /// <para><b>为什么落到格心</b>：甲板寻路网格是 1 米一格，走位解出来的落点是格心，
    /// 而搬运那一档的到达容差只有 <see cref="ArenaTuning.HaulArriveToleranceMeters"/> 米。
    /// 取货点钉在格角上（洞穴口 <c>(0, 2)</c> 就是格角）时，走位把人停在离取货点
    /// 0.1 米开外 —— 比容差大，于是那一单永远等不到「到了」，人被钉在原地、单再也投不出去。
    /// 落点这一格只挪走位目标，<b>不动点位物自己那一格真实位置</b>：
    /// 尸体的位置仍记着人倒下的地方，负面情绪圈也仍按真实位置算。</para>
    /// </summary>
    private NavPoint PickupAt(NavPoint itemAt)
    {
        var cell = _world.NearestFreeDeckCell(itemAt.X, itemAt.Z);
        return new NavPoint(cell.X, cell.Z);
    }
}
