#nullable enable
using System;
using DemoFactory.GugaFramework.Camera;
using DemoFactory.GugaFramework.Construction;
using DemoFactory.Prototypes.BeastArena.Logic;
using Godot;

namespace DemoFactory.Prototypes.BeastArena.Ui;

/// <summary>一次世界拾取落在哪一类目标上。</summary>
public enum PickTarget
{
    /// <summary>空地：视线没落到甲板上（或根本没有地面交点）。</summary>
    Empty = 0,

    /// <summary>人（容差内的屏幕最近邻）。</summary>
    Person = 1,

    /// <summary>建筑：那一格被一栋建筑占着。</summary>
    Building = 2,

    /// <summary>甲板格：落在甲板范围内、但那一格上没有建筑。</summary>
    DeckCell = 3,

    /// <summary>巨兽：点在那只兽的屏幕包围盒里。</summary>
    Beast = 4,

    /// <summary>尸体：容差内的屏幕最近邻（尸体那一类点位物；只由拖拽那一口报）。</summary>
    Corpse = 5,

    /// <summary>散货：容差内的屏幕最近邻（散货那一类点位物；只由拖拽那一口报）。</summary>
    Cargo = 6,
}

/// <summary>
/// 一次拾取的完整结果 —— <b>人 / 尸体 / 散货 / 建筑 / 巨兽 / 甲板格 / 空地</b>七档一次说清。
///
/// <para>面板与后续 ticket（建造、右键菜单、抓放）都读这一份，不再各自调三次换算：
/// 三次换算之间相机位置会变，各自算出来的答案可能不是同一帧的。</para>
/// </summary>
/// <param name="Target">落在哪一类目标上。</param>
/// <param name="Person">人那一档的活物（其余档为 null）。</param>
/// <param name="Item">尸体与散货那两档的点位物（其余档为 null）。</param>
/// <param name="BuildingId">建筑那一档的建筑 id（其余档为空串）。</param>
/// <param name="Cell">甲板格那一档的格号（其余档为 <c>default</c>）。</param>
public readonly record struct PickResult(
    PickTarget Target, ArenaUnit? Person, PointItem? Item, string BuildingId, GridCell Cell)
{
    /// <summary>空地那一档。</summary>
    public static PickResult None => new(PickTarget.Empty, null, null, string.Empty, default);
}

/// <summary>
/// 屏幕点 → 世界目标的那一层换算 —— 地面点、甲板格、最近的人、最近的点位物、格上的建筑、兽的屏幕包围盒。
///
/// <para><b>换算链只有一条，后续 ticket都走这里</b>：
/// 屏幕点 → 相机射线与 <c>y = DeckView.TownWorldY</c> 的平面求交 → <see cref="DeckView"/> 的悬浮层
/// <c>ToLocal</c> → 局部点除以 <see cref="DeckBlueprints.CellSize"/> 后 <c>floor</c> → 钳在 −12..11。</para>
///
/// <para><b>为什么局部点必须过 <c>ToLocal</c></b>：甲板根节点带着偏航（<c>DeckView.SyncPose</c> 里的
/// <c>LookAt</c>），手工减原点只在没转过时才对。转过之后格号会整体错一个角度，
/// 而那种错在画面上不显形 —— 读数说「点在格上」，落座却落在别处。</para>
///
/// <para><b>表现层只读</b>：这里只读世界与相机，不算格心、不占格、不写任何字段。
/// 格心换算交给 <see cref="DeckBlueprints.CandidateOf"/>，占格查询交给建造模块。</para>
/// </summary>
public sealed class Picker
{
    /// <summary>找人时的基准容差（像素）。实际值乘上相机的 <see cref="TopDownRig.PickToleranceScale"/>。</summary>
    public const float PickRadiusPixels = 36f;

    /// <summary>容差下限（像素）：贴到最近一档时也不能窄到点不中。</summary>
    public const float MinPickRadiusPixels = 20f;

    /// <summary>容差上限（像素）：拉到最远一档时也不能宽到点谁都是它。</summary>
    public const float MaxPickRadiusPixels = 160f;

    /// <summary>
    /// 兽那一块屏幕包围盒的边长下限（像素）。
    /// 战略档离得远，兽在画面上只有几个像素 —— 照那个尺寸点等于点不中，所以按中心撑到这一档。
    /// </summary>
    public const float MinBeastPickPixels = 28f;

    /// <summary>兽那一块屏幕包围盒外面再留一圈余量（像素）。</summary>
    public const float BeastPickPaddingPixels = 6f;

    private readonly Camera3D _camera;
    private readonly TopDownRig _rig;
    private readonly DeckView _deck;
    private readonly Func<ArenaWorld?> _worldOf;

    public Picker(Camera3D camera, TopDownRig rig, DeckView deck, Func<ArenaWorld?> worldOf)
    {
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _rig = rig ?? throw new ArgumentNullException(nameof(rig));
        _deck = deck ?? throw new ArgumentNullException(nameof(deck));
        _worldOf = worldOf ?? throw new ArgumentNullException(nameof(worldOf));
    }

    /// <summary>屏幕点 → 镇面那个平面上的世界点。</summary>
    public bool TryGroundPoint(Vector2 screen, out Vector3 worldPoint)
    {
        worldPoint = Vector3.Zero;

        var origin = _camera.ProjectRayOrigin(screen);
        var direction = _camera.ProjectRayNormal(screen);
        // 视线与镇面平行时没有交点；镜头上仰到地平线以上也是这一档。
        if (Math.Abs(direction.Y) < 1e-6f) return false;

        var distance = (DeckView.TownWorldY - origin.Y) / direction.Y;
        if (distance <= 0f) return false;

        worldPoint = origin + (direction * distance);
        return true;
    }

    /// <summary>屏幕点 → 甲板格（建造格号 −12..11）。</summary>
    public bool TryDeckCell(Vector2 screen, out GridCell cell)
    {
        cell = default;
        if (!TryGroundPoint(screen, out var worldPoint)) return false;

        // 甲板根节点带偏航，必须问它自己的局部系；手工减原点在转过之后会错位。
        var local = _deck.LevitationNode.ToLocal(worldPoint);
        cell = CellOfLocal(local);
        return true;
    }

    /// <summary>
    /// 屏幕点 → 一次拾取结果（人 / 建筑 / 巨兽 / 甲板格 / 空地）。
    ///
    /// <para><b>次序是「人 → 建筑 → 兽包围盒 → 甲板格」</b>：人站在建筑门口时点的是人，不是那一栋 ——
    /// 人的容差只有几十像素，先把这一档判掉，点选才不会被脚下的建筑吃掉；
    /// 建筑挡在兽前面时点的是那栋楼，不是兽；
    /// 剩下的一档才是兽（它的屏幕包围盒），最后才是甲板格。</para>
    ///
    /// <para><b>这一口不认尸体与散货</b>：点选与右键那两条路问的是「点到了哪一栋、哪一个人」，
    /// 地上躺着的东西不该把它们抢走 —— 地上那一具挨着某栋楼时，右键该开的是那栋楼的菜单。
    /// 拖拽要认那两类，走的是另一口（<see cref="PickDraggable"/>）。</para>
    /// </summary>
    public PickResult Pick(Vector2 screen)
    {
        if (TryPerson(screen, out var person))
            return new PickResult(PickTarget.Person, person, null, string.Empty, default);

        // 甲板格要先算出来：建筑那一档是「那一格上有楼」，兽那一档是 3D 模型的屏幕包围盒。
        var onDeck = TryDeckCellInBounds(screen, out var cell);

        if (onDeck)
        {
            var building = BuildingIdAt(cell);
            if (building.Length > 0)
                return new PickResult(PickTarget.Building, null, null, building, cell);
        }

        if (TryBeast(screen))
            return new PickResult(PickTarget.Beast, null, null, string.Empty, default);

        // 甲板外的地面（钳之前的格号越界）一律算空地，不把屏幕边缘的钳位格当目标。
        return onDeck
            ? new PickResult(PickTarget.DeckCell, null, null, string.Empty, cell)
            : PickResult.None;
    }

    /// <summary>
    /// 屏幕点 → 抓得住的那一类（人 / 尸体 / 散货）；抓不住就是空档。
    ///
    /// <para><b>为什么单独一口</b>：拖拽要认地上躺着的东西（尸体与散货），点选与右键不要 ——
    /// 地上那一具挨着某栋楼时，右键该开的是那栋楼的菜单，不是尸体那一具。
    /// 两个次序问的不是同一件事：<see cref="Pick"/> 问「点到了什么」，
    /// 这一口问「能抓什么」。</para>
    ///
    /// <para><b>次序是「人 → 尸体 → 散货」</b>：人排在最前（镇民挡在前面时抓的是镇民）；
    /// 尸体与散货各按屏幕最近邻算，谁离光标近算谁。</para>
    /// </summary>
    public PickResult PickDraggable(Vector2 screen)
    {
        if (TryPerson(screen, out var person))
            return new PickResult(PickTarget.Person, person, null, string.Empty, default);

        if (TryCorpse(screen, out var corpse))
            return new PickResult(PickTarget.Corpse, null, corpse, string.Empty, default);

        if (TryCargo(screen, out var cargo))
            return new PickResult(PickTarget.Cargo, null, cargo, string.Empty, default);

        return PickResult.None;
    }

    /// <summary>
    /// 屏幕点 → 是不是点在巨兽身上（那一件的屏幕包围盒里）。
    ///
    /// <para><b>为什么用屏幕包围盒而不是一个点</b>：兽是 3D 模型，投影到屏幕上是一块
    /// （模型量出来 52.9 × 89.6 米），按点算只能点到它身上某几根线上。
    /// 做法是把模型那件 <see cref="VisualInstance3D"/> 的 AABB 八个角投到屏幕上，
    /// 取外接矩形 —— 兽在画面上占多大，这块就有多大。</para>
    ///
    /// <para><b>为什么要留余量</b>：战略档离得远，兽在屏幕上只有几个像素宽，
    /// 照那个尺寸点等于点不中。所以这一块有下限（<see cref="MinBeastPickPixels"/>），
    /// 太小就按中心撑开；外面还留一圈余量（<see cref="BeastPickPaddingPixels"/>）。</para>
    /// </summary>
    public bool TryBeast(Vector2 screen)
    {
        if (!TryBeastScreenRect(out var rect)) return false;
        return rect.HasPoint(screen);
    }

    /// <summary>巨兽那一件在屏幕上的包围矩形（量不出来回 false）。</summary>
    private bool TryBeastScreenRect(out Rect2 rect)
    {
        rect = default;

        var visual = BeastVisual();
        if (visual is null) return false;

        var aabb = visual.GetAabb();
        if (aabb.Size.X <= 0f || aabb.Size.Y <= 0f || aabb.Size.Z <= 0f) return false;

        var transform = visual.GlobalTransform;
        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        var corners = 0;

        for (var i = 0; i < 8; i++)
        {
            var corner = transform * new Vector3(
                (i & 1) == 0 ? aabb.Position.X : aabb.Position.X + aabb.Size.X,
                (i & 2) == 0 ? aabb.Position.Y : aabb.Position.Y + aabb.Size.Y,
                (i & 4) == 0 ? aabb.Position.Z : aabb.Position.Z + aabb.Size.Z);

            // 角点在相机背后时投影没有意义（会投到屏幕另一头去），那一角不算。
            if (_camera.IsPositionBehind(corner)) continue;

            var onScreen = _camera.UnprojectPosition(corner);
            min = min.Min(onScreen);
            max = max.Max(onScreen);
            corners++;
        }

        if (corners == 0) return false;

        // 太小的块撑到下限：战略档上兽只有几个像素，照那个尺寸点不中。
        var size = max - min;
        if (size.X < MinBeastPickPixels)
        {
            var grow = (MinBeastPickPixels - size.X) * 0.5f;
            min.X -= grow;
            max.X += grow;
        }

        if (size.Y < MinBeastPickPixels)
        {
            var grow = (MinBeastPickPixels - size.Y) * 0.5f;
            min.Y -= grow;
            max.Y += grow;
        }

        rect = new Rect2(min, max - min).Grow(BeastPickPaddingPixels);
        return true;
    }

    /// <summary>
    /// 当前形态那件模型上的 <see cref="VisualInstance3D"/>（量不出来回 null）。
    ///
    /// <para><b>怎么找到那一件</b>：兽的三件模型挂在锚点 <c>BeastAnchor</c> 下，
    /// 同一时刻只显示一件（切形态只切显隐，见 <c>DeckView.SetBeastForm</c>），
    /// 所以取锚点下那个可见的子节点，再往里找第一件能画的东西。</para>
    ///
    /// <para><b>锚点下还有磁悬浮底座</b>（它也是可见的）：底座量不出东西或者量成零格时
    /// 那一档跳过 —— 那一块是兽背上的盘子，不是兽本身。</para>
    /// </summary>
    private VisualInstance3D? BeastVisual()
    {
        var anchor = FindByName(_deck.BeastNode, "BeastAnchor");
        if (anchor is null) return null;

        foreach (var child in anchor.GetChildren())
        {
            if (child is not Node3D node || !node.Visible) continue;

            var visual = FirstVisualOf(node);
            if (visual is null) continue;

            var aabb = visual.GetAabb();
            if (aabb.Size.X <= 0f || aabb.Size.Y <= 0f || aabb.Size.Z <= 0f) continue;

            return visual;
        }

        return null;
    }

    /// <summary>往下找第一件能画的东西（模型的 AABB 就量它）。</summary>
    private static VisualInstance3D? FirstVisualOf(Node root)
    {
        if (root is VisualInstance3D visual) return visual;

        foreach (var child in root.GetChildren())
        {
            var found = FirstVisualOf(child);
            if (found is not null) return found;
        }

        return null;
    }

    /// <summary>按名字往下找（找不到回 null）。</summary>
    private static Node? FindByName(Node root, string name)
    {
        if (root.Name == name) return root;

        foreach (var child in root.GetChildren())
        {
            var found = FindByName(child, name);
            if (found is not null) return found;
        }

        return null;
    }

    /// <summary>屏幕点 → 甲板格，<b>但越界回 false</b>（钳位之前判）。</summary>
    private bool TryDeckCellInBounds(Vector2 screen, out GridCell cell)
    {
        cell = default;
        if (!TryDeckLocal(screen, out var local)) return false;

        cell = CellOfLocal(local);
        return true;
    }

    /// <summary>
    /// 屏幕点 → <b>甲板局部点</b>（悬浮层的局部系）；落在甲板范围外回 false。
    ///
    /// <para>抓放跟手与落点判定都走这一口：跟手要的是连续的局部点（不是格号），
    /// 落点要的是「这一下到底在不在甲板上」。<b>不钳位</b> —— 屏幕边缘外的格号
    /// 钳进来之后会得到一个假目标，拖丢的人会落在甲板边缘而不是原地。</para>
    /// </summary>
    public bool TryDeckLocal(Vector2 screen, out Vector3 local)
    {
        local = Vector3.Zero;
        if (!TryGroundPoint(screen, out var worldPoint)) return false;

        // 甲板根节点带偏航，必须问它自己的局部系；手工减原点在转过之后会错位。
        local = _deck.LevitationNode.ToLocal(worldPoint);
        return IsCellInLiveBounds(CellOfLocalUnclamped(local));
    }

    /// <summary>格号在不在<b>当前位阶</b>的甲板边界内（世界没建好时按一阶档判）。</summary>
    private bool IsCellInLiveBounds(GridCell cell)
    {
        var world = _worldOf();
        return world is null ? DeckBlueprints.IsInTier1Bounds(cell) : world.IsDeckCellInBounds(cell);
    }

    /// <summary>屏幕点 → 最近的人（屏幕距离最近邻，容差按相机距离换算）。</summary>
    public bool TryPerson(Vector2 screen, out ArenaUnit person)
    {
        person = null!;
        var world = _worldOf();
        if (world is null) return false;

        var tolerance = Mathf.Clamp(PickRadiusPixels * _rig.PickToleranceScale,
            MinPickRadiusPixels, MaxPickRadiusPixels);

        var best = tolerance;
        foreach (var unit in world.Units)
        {
            // 死者已换成尸体点位物；离镇的还在册但不在画面上。
            if (unit.State == UnitState.Dead || unit.LeftTown) continue;

            var at = _deck.LevitationNode.ToGlobal(
                new Vector3((float)unit.DeckX, DeckView.TownY, (float)unit.DeckZ));
            var onScreen = _camera.UnprojectPosition(at);
            var distance = onScreen.DistanceTo(screen);
            if (distance > best) continue;

            best = distance;
            person = unit;
        }

        return person is not null;
    }

    /// <summary>屏幕点 → 最近的一具尸体（屏幕距离最近邻，容差按相机距离换算）。</summary>
    public bool TryCorpse(Vector2 screen, out PointItem corpse)
        => TryItem(screen, PointItemKind.Corpse, out corpse);

    /// <summary>屏幕点 → 最近的一件散货（同上，容差与尸体那一档同形）。</summary>
    public bool TryCargo(Vector2 screen, out PointItem cargo)
        => TryItem(screen, PointItemKind.Cargo, out cargo);

    /// <summary>
    /// 屏幕点 → 最近的一类点位物（屏幕距离最近邻，容差按相机距离换算）。
    ///
    /// <para><b>与人那一档同形</b>：都是「屏幕最近邻 + 同一份容差」。
    /// 两档分开写只是因为要分开报告类别 —— 判据（多近算点中）只有这一份。</para>
    ///
    /// <para><b>只看不摘走的那几件</b>：离场（<c>Removed</c>）的不在画面上。</para>
    /// </summary>
    private bool TryItem(Vector2 screen, PointItemKind kind, out PointItem item)
    {
        item = null!;
        var world = _worldOf();
        if (world is null) return false;

        var tolerance = Mathf.Clamp(PickRadiusPixels * _rig.PickToleranceScale,
            MinPickRadiusPixels, MaxPickRadiusPixels);

        var best = tolerance;
        foreach (var candidate in world.Items.Live)
        {
            if (candidate.Kind != kind) continue;

            var at = _deck.LevitationNode.ToGlobal(
                new Vector3((float)candidate.DeckX, DeckView.TownY, (float)candidate.DeckZ));
            var onScreen = _camera.UnprojectPosition(at);
            var distance = onScreen.DistanceTo(screen);
            if (distance > best) continue;

            best = distance;
            item = candidate;
        }

        return item is not null;
    }

    /// <summary>
    /// 格 → 这一格上的建筑 id（没占着回空串）。
    /// <b>规则只有一份</b>：本口直接读 <see cref="DeckView.BuildingIdAtCell"/>，
    /// 不在这里再写一遍「格心 → 建造模块」的换算。
    /// </summary>
    public string BuildingIdAt(GridCell cell)
        => _deck.BuildingIdAtCell(cell.Col, cell.Row, out var id) ? id : string.Empty;

    /// <summary>屏幕点 → 建筑 id（没点着建筑回空串）。</summary>
    public string BuildingIdAtScreen(Vector2 screen)
        => TryDeckCell(screen, out var cell) ? BuildingIdAt(cell) : string.Empty;

    /// <summary>甲板局部点 → 建造格号（floor 后钳进<b>当前位阶</b>的边界；二阶扩容后新圈才点得到）。</summary>
    public GridCell CellOfLocal(Vector3 local)
    {
        var cell = CellOfLocalUnclamped(local);
        if (cell.Col >= DeckBlueprints.MinCol && cell.Col <= DeckBlueprints.MaxCol
            && cell.Row >= DeckBlueprints.MinRow && cell.Row <= DeckBlueprints.MaxRow)
        {
            return cell;
        }

        var (minCol, minRow, maxCol, maxRow) = _worldOf()?.DeckCellBounds
            ?? (DeckBlueprints.MinCol, DeckBlueprints.MinRow, DeckBlueprints.MaxCol, DeckBlueprints.MaxRow);
        return new GridCell(Math.Clamp(cell.Col, minCol, maxCol), Math.Clamp(cell.Row, minRow, maxRow));
    }

    /// <summary>甲板局部点 → 建造格号（<b>不钳位</b>，越界回越界值，交给调用方判）。</summary>
    private static GridCell CellOfLocalUnclamped(Vector3 local)
        => new(
            Mathf.FloorToInt((float)(local.X / DeckBlueprints.CellSize)),
            Mathf.FloorToInt((float)(local.Z / DeckBlueprints.CellSize)));
}
