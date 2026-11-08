#nullable enable
using System;
using System.Globalization;
using DemoFactory.GugaFramework.Pathfinding;

namespace DemoFactory.Prototypes.BeastArena.Logic;

/// <summary>
/// 大地图地形四种（逻辑层只记类别；表现层在色块上加程序化细节，垂直切片 2026-10-28）。
/// </summary>
public enum TerrainKind
{
    /// <summary>草地。两座老城与野外锚地落在草地上。</summary>
    Grass = 0,

    /// <summary>沙漠。南侧一整带；沙城落在沙漠上。</summary>
    Desert = 1,

    /// <summary>道路。城镇之间的航线走廊，铺在草地与沙漠上。</summary>
    Road = 2,

    /// <summary>水。湖泊与河流同一类，表现层都按水画。</summary>
    Water = 3,
}

/// <summary>
/// 大地图那一张 512 × 512 格的地形图 —— <b>逻辑层数据，表现层只读</b>：
/// 地形表现（<c>TerrainArt.Build</c>，由 <c>DeckView.BuildTerrain</c> 挂上去）与读数
/// （<c>BeastVoyage.DebugSegment</c> 的 terrain 那一列）都从这一张表取，不各写一份。
///
/// <para><b>格即米</b>：格长 1 米，格号就是米号 —— 第 40 格覆盖 [40,41) 米。
/// 地形边界那一组数因此全按米写，读的时候不再要「格 → 米」换算。</para>
///
/// <para><b>格号原点 −56</b>：第一格是 −56 号格，末格是 455 号格（<see cref="GridOrigin"/> / <see cref="GridCells"/>）。
/// 圆盘跨的是世界 −56..456 米，所以这张表比寻路那张（零基 0..512）往西南多出一段。
/// <b>偏移只在这一处加</b>：数组下标由 <see cref="IndexOf"/> 算，别处不再各加一次。</para>
///
/// <para><b>布局</b>：格网里默认草地；
/// 南侧 z 40..159 是沙漠带；西北 x 100..159 × z 220..259 是湖泊；东侧 x 280..319 × z 40..179 是河流；
/// 道路三条——主干 z 180..199 一条带（两城之间，城在它北侧），南支 x 200..219 带 z 100..179（通沙城），
/// 北支 x 200..219 带 z 200..279（通野外锚地）。铺色次序即优先级：草 → 沙漠 → 水 → 道路。</para>
///
/// <para><b>细节只铺在旧方图那一段</b>（x 40..359）：那些是原 400 米方图里的地形，一格不动；
/// 新扩出来的环带一律草地（这一轮不打磨地图）。</para>
///
/// <para><b>圆外没有地形</b>：<see cref="TerrainAtCell"/> 对圆盘外与格网外一律回 null ——
/// 圆外那一圈是星空出界区，不属四种地形之一。</para>
///
/// <para><b>为什么地形在逻辑层而不是画在表现层</b>：后续「沙漠连续待 N 天」那类条件
/// 要读「巨兽现在在哪块地上」，那是逻辑判断；先落数据，条件后接。</para>
/// </summary>
public static class BigMapTerrain
{
    /// <summary>
    /// 地形格网的格号原点（米）：第一格是 −56 号格。
    /// <b>偏移只在这一处加</b>（<see cref="IndexOf"/>），别处不再各加一次。
    /// </summary>
    public const int GridOrigin = -56;

    /// <summary>地形格网边长（格）：512 格 × 1 米 = 512 米。</summary>
    public const int GridCells = 512;

    /// <summary>格网最西那一格的格号（含）= <see cref="GridOrigin"/>。</summary>
    public const int MinCell = GridOrigin;

    /// <summary>格网最东那一格的格号（含）。</summary>
    public const int MaxCell = GridOrigin + GridCells - 1;

    /// <summary>
    /// 细节区（沙漠带与三条路）的横向范围：格 40..359 —— 与旧 400 米方图那一段一格不差。
    /// 范围之外（圆盘新扩出的环带）一律草地。
    /// </summary>
    private const int DetailMinX = 40;

    /// <summary>细节区（沙漠带与三条路）的横向范围上界（格，含）。</summary>
    private const int DetailMaxX = 359;

    /// <summary>
    /// 停靠点离最近道路格矩形边缘的允许距离（米）。
    /// 四个停靠点算出来都正好 10.0 米 —— 主干道路宽 20 米，城市摆在路肩外一站。
    /// </summary>
    public const double StopRoadGapMeters = 10.0;

    /// <summary>沙漠带南界（格，含）。</summary>
    private const int DesertMinZ = 40;

    /// <summary>沙漠带北界（格，含）。</summary>
    private const int DesertMaxZ = 159;

    /// <summary>湖泊西界（格，含）。</summary>
    private const int LakeMinX = 100;

    /// <summary>湖泊东界（格，含）。</summary>
    private const int LakeMaxX = 159;

    /// <summary>湖南界（格，含）—— 在航线那一带（z 180..199）北侧，巨兽走航线时不穿湖。</summary>
    private const int LakeMinZ = 220;

    /// <summary>湖北界（格，含）—— 到城模型那一带之前停，湖面不被城压住。</summary>
    private const int LakeMaxZ = 259;

    /// <summary>河流西界（格，含）。</summary>
    private const int RiverMinX = 280;

    /// <summary>河流东界（格，含）。</summary>
    private const int RiverMaxX = 319;

    /// <summary>河流的北界（格，含）—— 到主干道路之前停，不横切道路。</summary>
    private const int RiverMaxZ = 179;

    /// <summary>主干道路南界（格，含）。两城在它北侧（z=210）。</summary>
    private const int RoadMinZ = 180;

    /// <summary>主干道路北界（格，含）。这条路带宽 20 米。</summary>
    private const int RoadMaxZ = 199;

    /// <summary>两条支路共用的那一带（西界，格，含）。这条带宽 20 米。</summary>
    private const int BranchMinX = 200;

    /// <summary>两条支路共用的那一带（东界，格，含）。</summary>
    private const int BranchMaxX = 219;

    /// <summary>南支（通沙城）的南界（格，含）—— 到沙城那一行的北侧停，沙城格留沙漠。</summary>
    private const int SouthBranchMinZ = 100;

    /// <summary>南支的北界（格，含）—— 与主干道路相接。</summary>
    private const int SouthBranchMaxZ = 179;

    /// <summary>北支（通野外锚地）南界（格，含）—— 与主干那一行接上，不断。</summary>
    private const int NorthBranchMinZ = 200;

    /// <summary>北支北界（格，含）—— 到锚地那一行的南侧停，锚地格留草地。</summary>
    private const int NorthBranchMaxZ = 279;

    /// <summary>整张地形表（512×512，行主序：下标 = (z − 原点) × 512 + (x − 原点)）。静态构造一次，之后只读。</summary>
    private static readonly TerrainKind[] Layout = Build();

    /// <summary>道路格下标清单（从 <see cref="Layout"/> 扫出来，静态构造一次，之后只读）。</summary>
    private static readonly int[] RoadCellIndices = CollectRoadCells();

    /// <summary>
    /// 格号 → 数组下标（行主序）。<b>+56 那一档偏移只在这一处加</b> ——
    /// 别处再各加一次，改格网尺寸时就会有一处漏掉。
    /// </summary>
    private static int IndexOf(int x, int z) => ((z - GridOrigin) * GridCells) + (x - GridOrigin);

    /// <summary>
    /// 按格下标读地形。圆盘外与格网外回 <c>null</c>（星空不属四种地形）。
    ///
    /// <para><b>半径判据按格心那一点算</b>：格号就是米号，于是「这一格在不在圆内」
    /// 与 <see cref="BigMapGrid.IsInsideEdge"/> 用的是同一把尺子、同一处判据（不另写一份半径）。
    /// 用格心而不是格角：格角判据会让陆地探出侧壁最多 √2 ≈ 1.41 米（对角那一格），
    /// 看着就像地形纸穿出了圆盘。</para>
    ///
    /// <para><b>残余</b>：按格心判之后，格心在墙内的那些格，格角仍可能探出墙外
    /// 最多 √2 ÷ 2 ≈ <b>0.71 米</b>（对角那一格）—— 不足一米，认了。
    /// 原方图那一段（x 40..359）最远角离圆心 226 米，整块在墙内，一格不变。</para>
    /// </summary>
    public static TerrainKind? TerrainAtCell(int x, int z)
    {
        if (x < MinCell || x > MaxCell || z < MinCell || z > MaxCell) return null;
        if (!BigMapGrid.IsInsideEdge(new NavPoint(x + 0.5, z + 0.5))) return null;
        return Layout[IndexOf(x, z)];
    }

    /// <summary>
    /// 按逻辑坐标读地形（米）。坐标落在格外（界外）回 <c>null</c>。
    /// </summary>
    public static TerrainKind? TerrainAt(NavPoint point)
    {
        var x = (int)Math.Floor(point.X);
        var z = (int)Math.Floor(point.Z);
        return TerrainAtCell(x, z);
    }

    /// <summary>
    /// 这一格在不在<b>沙漠带</b>里 —— 判据是位置，不是那一格填的是什么。
    ///
    /// <para><b>为什么不是「那一格是沙子」</b>：南支路从沙漠带里穿过去，那一段路格填的是路。
    /// 城模型改成锚点之后，兽停在沙城城门外那一点，脚下正是穿过沙漠带的那一段路 ——
    /// 按「脚下那一格填的是沙子」判，兽在沙城停一整天也攒不出一个累计计时，
    /// 荒漠态就永远解不开锁。判据改成「在不在沙漠带里」：路穿过沙漠，路上也在沙漠里。</para>
    /// </summary>
    public static bool InDesertBand(int x, int z)
        => x >= DetailMinX && x <= DetailMaxX && z >= DesertMinZ && z <= DesertMaxZ;

    /// <summary>那一点在不在沙漠带里（米）。</summary>
    public static bool InDesertBand(NavPoint point)
        => InDesertBand((int)Math.Floor(point.X), (int)Math.Floor(point.Z));

    /// <summary>地形名的读数写法（小写英文，进 DebugSegment 与快照）。</summary>
    public static string NameOf(TerrainKind? kind) => kind switch
    {
        TerrainKind.Grass => "grass",
        TerrainKind.Desert => "desert",
        TerrainKind.Road => "road",
        TerrainKind.Water => "water",
        _ => "-",
    };

    /// <summary>
    /// 这张表是不是「道路格连成一片、每个停靠点都挨着一条道路」——
    /// 建造期自检用（测试也走这一口，判据只有一份）。
    ///
    /// <para><b>「挨着」按米判</b>：停靠点到最近道路格<b>矩形边缘</b>（不取格心）的距离 ≤
    /// <see cref="StopRoadGapMeters"/>。取边缘不取格心，是因为「挨着道路」说的是站在路肩上，
    /// 不是站在路心。</para>
    /// </summary>
    public static bool RoadGraphReaches(params NavPoint[] spots)
    {
        // 从主干道路西端出发走四邻 BFS，看能不能走遍所有道路格；
        // 再逐点看每个停靠点是不是挨着一条道路。
        var seen = new bool[GridCells * GridCells];
        var queue = new System.Collections.Generic.List<int>();

        var start = IndexOf(DetailMinX, RoadMinZ);
        if (Layout[start] != TerrainKind.Road) return false;
        seen[start] = true;
        queue.Add(start);

        for (var i = 0; i < queue.Count; i++)
        {
            var at = queue[i];
            var x = (at % GridCells) + GridOrigin;
            var z = (at / GridCells) + GridOrigin;
            TryPush(x + 1, z);
            TryPush(x - 1, z);
            TryPush(x, z + 1);
            TryPush(x, z - 1);

            void TryPush(int nx, int nz)
            {
                if (nx < MinCell || nx > MaxCell || nz < MinCell || nz > MaxCell) return;
                var index = IndexOf(nx, nz);
                if (seen[index] || Layout[index] != TerrainKind.Road) return;
                seen[index] = true;
                queue.Add(index);
            }
        }

        for (var i = 0; i < Layout.Length; i++)
        {
            if (Layout[i] == TerrainKind.Road && !seen[i]) return false;
        }

        foreach (var spot in spots)
        {
            if (DistanceToNearestRoadCell(spot.X, spot.Z) > StopRoadGapMeters) return false;
        }

        return true;
    }

    /// <summary>
    /// 一点到最近那一个道路格矩形边缘的距离（米）。
    ///
    /// <para><b>取矩形不取格心</b>：道路格第 cx 格覆盖 [cx, cx+1) 米这一段，
    /// 点落在这一段里时距离是 0，在段外时是到那一段的轴向距离。
    /// 四个停靠点算出来都正好 10.0 米（整数），不另给浮点容差。</para>
    /// </summary>
    public static double DistanceToNearestRoadCell(double x, double z)
    {
        var best = double.MaxValue;

        foreach (var index in RoadCellIndices)
        {
            var cx = (index % GridCells) + GridOrigin;
            var cz = (index / GridCells) + GridOrigin;

            var dx = Math.Max(cx - x, x - (cx + 1.0));
            if (dx < 0.0) dx = 0.0;

            var dz = Math.Max(cz - z, z - (cz + 1.0));
            if (dz < 0.0) dz = 0.0;

            var d = Math.Sqrt((dx * dx) + (dz * dz));
            if (d < best) best = d;
        }

        return best;
    }

    /// <summary>从地形表里扫出道路格的下标清单（静态构造一次，之后只读）。</summary>
    private static int[] CollectRoadCells()
    {
        var list = new System.Collections.Generic.List<int>();
        for (var i = 0; i < Layout.Length; i++)
        {
            if (Layout[i] == TerrainKind.Road) list.Add(i);
        }

        return list.ToArray();
    }

    /// <summary>按布局规则生成整张表。<b>顺序即优先级</b>：草 → 沙漠 → 水 → 道路（道路最后，压过沙漠）。</summary>
    private static TerrainKind[] Build()
    {
        var layout = new TerrainKind[GridCells * GridCells];

        // 草地铺满整张格网：圆外那圈由 TerrainAtCell 的半径判据回 null，不靠这一层区分。
        FillRect(layout, TerrainKind.Grass, MinCell, MaxCell, MinCell, MaxCell);

        // 沙漠带：南侧一整行带（横向只铺旧方图那一段，与旧表一格不差）。
        FillRect(layout, TerrainKind.Desert, DetailMinX, DetailMaxX, DesertMinZ, DesertMaxZ);

        // 湖泊：西北一块。
        FillRect(layout, TerrainKind.Water, LakeMinX, LakeMaxX, LakeMinZ, LakeMaxZ);

        // 河流：东侧一竖条，穿沙漠东缘，到主干道路前停。
        FillRect(layout, TerrainKind.Water, RiverMinX, RiverMaxX, DesertMinZ, RiverMaxZ);

        // 主干道路：横贯的一条带（20 米宽）。
        FillRect(layout, TerrainKind.Road, DetailMinX, DetailMaxX, RoadMinZ, RoadMaxZ);

        // 南支：通沙城。
        FillRect(layout, TerrainKind.Road, BranchMinX, BranchMaxX, SouthBranchMinZ, SouthBranchMaxZ);

        // 北支：通野外锚地。
        FillRect(layout, TerrainKind.Road, BranchMinX, BranchMaxX, NorthBranchMinZ, NorthBranchMaxZ);

        return layout;
    }

    /// <summary>
    /// 填一块矩形（行列都含端点）。
    /// 六块地形带各写一遍双层循环的话，改一次边界要动六处。
    /// </summary>
    private static void FillRect(
        TerrainKind[] layout, TerrainKind kind, int x0, int x1, int z0, int z1)
    {
        for (var z = z0; z <= z1; z++)
        {
            for (var x = x0; x <= x1; x++)
            {
                layout[IndexOf(x, z)] = kind;
            }
        }
    }
}
