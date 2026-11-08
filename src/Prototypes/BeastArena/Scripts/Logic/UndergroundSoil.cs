#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace DemoFactory.Prototypes.BeastArena.Logic;

// 地下那一层：2 层 × 4 象限共 8 个土壤格，同象限自上而下开挖、自下而上回长。
//
// 地下不另建寻路网格 —— 土壤格是格状态表加岗位槽，不在甲板或任何 SpaceId 上开网格。
// 洞穴清剿的怪从镇政府电梯口涌上甲板再按同一套交战（交战那一摊不在本文件里）。
//
// 8 格的编法（sector 0..3 各对应甲板 12×12 米象限，layer 1..2 各深 3 米，1 号最靠巨兽）：
//   · 单处原生洞穴长在象限 0 的第 2 层，挖开后可以发起清剿，洞里四只原生怪；
//   · 其余 7 格挖开不遇洞；挖开本身不即得货物；
//   · 每个挖开的格开 3 个采矿岗位，每人在岗每 2 游戏小时产 1 矿石；
//   · 清剿成功的洞穴格转成高危生产区，每人每 4 游戏小时额外产 1 特殊材料；
//   · 嵌入魔晶后那一格永久停止回长，特殊材料提速到每 2 游戏小时一件；
//   · 未嵌入的已挖格按层速回长（第 2 层 2 层/游戏日、第 1 层 1 层/游戏日），回长填满引发塌方。

/// <summary>地下一个土壤格的读数快照（状态可查那一口，一格一行）。</summary>
/// <param name="Sector">象限号 0..3。</param>
/// <param name="Layer">层号 1..2（1 号最靠巨兽）。</param>
/// <param name="Dug">挖开了没有。</param>
/// <param name="Cleared">洞穴清剿成功没有（高危生产区与特殊材料产出的唯一判据）。</param>
/// <param name="Embedded">嵌入魔晶没有（嵌入后永久停止回长，特殊材料产出提速）。</param>
/// <param name="CaveMonsters">洞穴里还剩几只原生怪（不是洞穴格 = 0）。</param>
/// <param name="RegrowthProgress">这一格回长的进度（游戏日，够一条就填满一格）。</param>
/// <param name="Miners">在岗采矿人数（0..3）。</param>
/// <param name="Digging">正在挖（派满 3 人满 1 游戏小时挖开）。</param>
public readonly record struct SoilCellReadout(
    int Sector,
    int Layer,
    bool Dug,
    bool Cleared,
    bool Embedded,
    int CaveMonsters,
    double RegrowthProgress,
    int Miners,
    bool Digging);

/// <summary>一次塌方读了什么（塌方那一格发生的可查事件）。</summary>
/// <param name="Sector">塌的是哪一象限。</param>
/// <param name="Layer">塌的是哪一层。</param>
/// <param name="Tick">塌在第几步。</param>
/// <param name="VisitorsFled">吓跑的在镇游客数。</param>
/// <param name="MoodDrop">全镇情绪落差。</param>
public readonly record struct SoilCollapse(int Sector, int Layer, long Tick, int VisitorsFled, double MoodDrop);

/// <summary>
/// 嵌入魔晶那一发的拦路（<b>逻辑层唯一判据</b>，UI 只负责把它翻成一句中文）。
/// </summary>
public enum SoilEmbedBlocker
{
    /// <summary>可以嵌入。</summary>
    None = 0,

    /// <summary>不在停靠期（嵌入只限停靠期）。</summary>
    NotDocked = 1,

    /// <summary>象限或层越界。</summary>
    OutOfRange = 2,

    /// <summary>那一格还没挖开。</summary>
    NotDug = 3,

    /// <summary>不是洞穴层（第二层以外没有可嵌入的格）。</summary>
    NotCaveLayer = 4,

    /// <summary>洞穴还没清剿成功。</summary>
    NotCleared = 5,

    /// <summary>同格已经嵌入过（同格不可重复嵌入）。</summary>
    AlreadyEmbedded = 6,

    /// <summary>仓库里没有 1 单位魔晶。</summary>
    NoCrystal = 7,

    /// <summary>正在打仗 / 停摆 / 仪式，这一发发不出去。</summary>
    Busy = 8,
}

/// <summary>
/// 「派到此格」发不出去的原因（<b>逻辑层唯一判据</b>，UI 只负责把它翻成一句中文）。
/// </summary>
public enum SoilAssignBlocker
{
    /// <summary>可以派。</summary>
    None = 0,

    /// <summary>人不在册（或已经离镇）。</summary>
    NotInRoster = 1,

    /// <summary>人已经死了。</summary>
    Dead = 2,

    /// <summary>采矿岗只收工作人员。</summary>
    NotWorker = 3,

    /// <summary>象限或层越界。</summary>
    OutOfRange = 4,

    /// <summary>那一格还没挖开，没有采矿岗。</summary>
    NotDug = 5,
}

/// <summary>
/// 地下土壤的格状态表与岗位槽 —— <b>没有寻路网格</b>，不在任何 SpaceId 上开格。
///
/// <para><b>开挖与回长都是整游戏小时/整游戏日挂在步号整除上</b>，不按「每步累加一个浮点增量」——
/// 后者一天下来是 0.9999999999999999 那一档，而回长进度与塌方都要能被测试逐字读出来。</para>
///
/// <para><b>回长自下而上</b>：同一象限里第 2 层先长、长满了再长第 1 层。
/// 长满一格若那一格正被挖着（未嵌入且未回长满），就地引发塌方。</para>
///
/// <para><b>嵌入取代固化</b>：固化那一套（占法力格、开/关冻结回长）整件删掉；
/// 现在只有「嵌入魔晶」这一条永久改变回长与产出的写口。</para>
/// </summary>
public sealed partial class UndergroundSoil
{
    private readonly Dictionary<int, Cell> _cells = [];
    private readonly List<SoilCollapse> _collapses = [];

    /// <summary>一个土壤格的状态。</summary>
    private sealed class Cell
    {
        public required int Sector { get; init; }

        public required int Layer { get; init; }

        public bool Dug { get; set; }

        public bool Cleared { get; set; }

        public bool Embedded { get; set; }

        public double RegrowthProgress { get; set; }

        public int Miners { get; set; }

        public int DigWorkers { get; set; }

        public long DigDoneTick { get; set; }

        public bool Digging { get; set; }

        public double OreAccumulated { get; set; }

        public double MaterialAccumulated { get; set; }

        /// <summary>洞穴里还剩几只原生怪（只对洞穴格有意义）。</summary>
        public int CaveMonsters { get; set; }
    }

    /// <summary>象限数（0..3）。</summary>
    public const int Sectors = ArenaTuning.SoilSectors;

    /// <summary>层数（1..2）。</summary>
    public const int Layers = ArenaTuning.SoilLayers;

    /// <summary>土壤格总数：2 层 × 4 象限。</summary>
    public const int CellCount = Sectors * Layers;

    /// <summary>单处原生洞穴所在的象限。</summary>
    public const int CaveSector = ArenaTuning.NativeCaveSector;

    /// <summary>原生洞穴固定长在第 2 层。</summary>
    public const int NativeCaveLayer = ArenaTuning.NativeCaveLayer;

    /// <summary>那一格是不是那处唯一的原生洞穴。</summary>
    public static bool IsCaveCell(int sector, int layer)
        => sector == CaveSector && layer == NativeCaveLayer;

    /// <summary>按（象限，层）取一格的状态读数。</summary>
    public SoilCellReadout CellAt(int sector, int layer)
    {
        var cell = Require(sector, layer);
        return new SoilCellReadout(sector, layer, cell.Dug, cell.Cleared, cell.Embedded,
            CaveMonstersAt(sector, layer), cell.RegrowthProgress, cell.Miners, cell.Digging);
    }

    /// <summary>这一格挖开了没有。</summary>
    public bool IsDug(int sector, int layer) => Require(sector, layer).Dug;

    /// <summary>这一格洞穴清剿成功没有（非洞穴格恒 false）。</summary>
    public bool IsCleared(int sector, int layer) => Require(sector, layer).Cleared;

    /// <summary>这一格嵌入魔晶没有。</summary>
    public bool IsEmbedded(int sector, int layer) => Require(sector, layer).Embedded;

    /// <summary>洞穴里还剩几只原生怪（不是洞穴格 = 0；没挖开也照实报洞里那几只）。</summary>
    public int CaveMonstersAt(int sector, int layer)
        => IsCaveCell(sector, layer) ? Require(sector, layer).CaveMonsters : 0;

    /// <summary>这一格在岗采矿人数（0..3）。</summary>
    public int MinersAt(int sector, int layer) => Require(sector, layer).Miners;

    /// <summary>这一格的回长进度（游戏日）。</summary>
    public double RegrowthProgressAt(int sector, int layer) => Require(sector, layer).RegrowthProgress;

    /// <summary>这一格累计采出的矿石（够 1 就该运上镇政府仓库）。</summary>
    public double OreAccumulatedAt(int sector, int layer) => Require(sector, layer).OreAccumulated;

    /// <summary>这一格累计采出的特殊材料。</summary>
    public double MaterialAccumulatedAt(int sector, int layer) => Require(sector, layer).MaterialAccumulated;

    /// <summary>这一格是不是正在挖。</summary>
    public bool IsDigging(int sector, int layer) => Require(sector, layer).Digging;

    /// <summary>已发生的塌方（按发生次序）。</summary>
    public IReadOnlyList<SoilCollapse> Collapses => _collapses;

    /// <summary>挖开洞穴格那一刻抛一次（象限 + 层；洞里四只原生怪已就位）。</summary>
    public event Action<int, int>? CaveOpened;

    /// <summary>洞穴格回长填满、洞穴重置那一刻抛一次（象限 + 层；清剿状态清零、重新住满四只）。</summary>
    public event Action<int, int>? CaveReset;

    /// <summary>
    /// 派工开挖一格：需派满 3 名工作人员挖 1 游戏小时。
    /// 同象限自上而下：挖深一层要求上一层已经挖开（否则挖出来悬空）。
    /// </summary>
    /// <param name="sector">象限号 0..3。</param>
    /// <param name="layer">层号 1..2（1 号最靠巨兽）。</param>
    /// <param name="workers">派到这一格的人数（派满 3 人才开工）。</param>
    /// <param name="tick">当前步号（工期的到期步号是拿它加出来的绝对值，
    /// 不是相对步数 —— 拿相对值与绝对步号比，开局之后发的工会在下一步就到期）。</param>
    public bool BeginDigging(int sector, int layer, int workers, long tick)
    {
        var cell = Require(sector, layer);
        if (cell.Dug) return false;

        // 自上而下：上一层没开，这一层就开不了。
        if (layer > 1 && !IsDug(sector, layer - 1)) return false;

        cell.DigWorkers = workers;
        if (workers >= ArenaTuning.DigWorkerThreshold)
        {
            cell.Digging = true;
            cell.DigDoneTick = tick + ArenaTime.StepsPerGameHour;
            return true;
        }

        cell.Digging = false;
        cell.DigDoneTick = 0;
        return true;
    }

    /// <summary>撤回开挖派工（人撤了，挖停）。</summary>
    public void StopDigging(int sector, int layer)
    {
        var cell = Require(sector, layer);
        cell.Digging = false;
        cell.DigWorkers = 0;
    }

    /// <summary>
    /// 派采矿工到某一格（挖开的格开 3 个岗位）。返回实际派上的人数
    /// （没挖开 = 0，那一格没有岗位槽）。
    /// </summary>
    public int AssignMiners(int sector, int layer, int miners)
    {
        var cell = Require(sector, layer);
        if (!cell.Dug) return 0;

        var seated = Math.Clamp(miners, 0, ArenaTuning.MinePostsPerCell);
        cell.Miners = seated;
        return seated;
    }

    /// <summary>
    /// 清剿成功：那一格转成高危生产区（特殊材料的唯一产地）。
    /// <b>写口只该由 <see cref="BattleSim"/> 在那四只全灭收场时调</b> ——
    /// 清剿失败那一档走 <see cref="MarkCaveFailed"/>，一个字都不改。
    /// </summary>
    public bool MarkCaveCleared(int sector, int layer)
    {
        if (!IsCaveCell(sector, layer)) return false;

        var cell = Require(sector, layer);
        if (!cell.Dug) return false;

        cell.Cleared = true;
        cell.CaveMonsters = 0;
        return true;
    }

    /// <summary>
    /// 清剿失败（撤退或超时）：<paramref name="remaining"/> 只怪留在洞里，
    /// 那一格不算清剿成功、不产特殊材料；下次发起时只涌出剩下的这几只。
    /// </summary>
    public bool MarkCaveFailed(int sector, int layer, int remaining)
    {
        if (!IsCaveCell(sector, layer)) return false;

        var cell = Require(sector, layer);
        if (!cell.Dug) return false;

        cell.Cleared = false;
        cell.CaveMonsters = Math.Clamp(remaining, 0, ArenaTuning.NativeCaveMonsters);
        return true;
    }

    /// <summary>
    /// 嵌入魔晶：<b>永久</b>停止回长，特殊材料产出从每岗 4 游戏小时一件提速到 2 小时一件。
    /// <b>扣魔晶在 <see cref="ArenaWorld.EmbedCrystal"/> 那一头</b>，本口只管格状态 ——
    /// 仓库那一本是别人的账，土壤册不碰。
    /// </summary>
    public bool MarkEmbedded(int sector, int layer)
    {
        if (!IsCaveCell(sector, layer)) return false;

        var cell = Require(sector, layer);
        if (!cell.Dug || !cell.Cleared || cell.Embedded) return false;

        cell.Embedded = true;
        return true;
    }

    /// <summary>
    /// 推进一整步：开挖到点即挖开、采矿岗位按整游戏小时结算、回长按整游戏日推进、
    /// 回长填满已挖格引发塌方。
    ///
    /// <para><b><paramref name="visitorsInTown"/> 递的是塌方那一刻的在镇游客数</b> ——
    /// 土壤自己不知道城里站了几个人，那一栏由记账那一侧递进来算，
    /// 不写死 0（「可调试性优先」：写着 0 的读数与「真的没人跑」在快照上分不开）。</para>
    ///
    /// <para><b><paramref name="miningPaused"/> 只停采矿</b>：开挖到点与回长照常跑。
    /// 进阶仪式期间由世界递 true —— 采矿累积与「开挖完成」是两件事，
    /// 仪式停的是前者。</para>
    ///
    /// <para><b><paramref name="oreFactor"/> / <paramref name="materialFactor"/> 是位阶那一档的倍率</b>：
    /// 每一步现算，不缓存。</para>
    /// </summary>
    public void AdvanceStep(
        long tick,
        int visitorsInTown = 0,
        bool miningPaused = false,
        double oreFactor = 1.0,
        double materialFactor = 1.0)
    {
        CompleteDigging(tick);
        ProduceFromPosts(tick, miningPaused, oreFactor, materialFactor);
        Regrow(tick, visitorsInTown);
    }

    /// <summary>状态可查那一口：8 格固定输出，一格一段（按象限再按层，段首是身份）。</summary>
    public string DebugSegment()
    {
        var lines = new List<string>(CellCount);
        for (var sector = 0; sector < Sectors; sector++)
        {
            for (var layer = 1; layer <= Layers; layer++)
            {
                var c = CellAt(sector, layer);
                lines.Add(string.Format(CultureInfo.InvariantCulture,
                    "SOIL {0}_{1} dug={2} cleared={3} embedded={4} cave={5} regrowth={6:0.00} miners={7}/{8}",
                    sector, layer, Bool(c.Dug), Bool(c.Cleared), Bool(c.Embedded), c.CaveMonsters,
                    c.RegrowthProgress, c.Miners, ArenaTuning.MinePostsPerCell));
            }
        }

        return string.Join(" | ", lines);
    }

    /// <summary>塌方后清掉那一格的采矿岗位（在格人员已经重伤或死亡，槽位得腾出来）。</summary>
    public void ClearMinersOnCollapse(int sector, int layer) => Require(sector, layer).Miners = 0;

    private static string Bool(bool value) => value ? "1" : "0";

    private Cell Require(int sector, int layer)
    {
        if (sector < 0 || sector >= Sectors)
        {
            throw new ArgumentOutOfRangeException(nameof(sector), sector,
                $"象限号取 0..{Sectors - 1}");
        }

        if (layer < 1 || layer > Layers)
        {
            throw new ArgumentOutOfRangeException(nameof(layer), layer,
                $"层号取 1..{Layers}");
        }

        if (!_cells.TryGetValue(Key(sector, layer), out var cell))
        {
            cell = new Cell
            {
                Sector = sector,
                Layer = layer,
                // 洞穴格一开局洞里就住着四只；挖开只是把它们放上甲板。
                CaveMonsters = IsCaveCell(sector, layer) ? ArenaTuning.NativeCaveMonsters : 0,
            };
            _cells[Key(sector, layer)] = cell;
        }

        return cell;
    }

    private static int Key(int sector, int layer) => (sector * (Layers + 1)) + layer;

    private void CompleteDigging(long tick)
    {
        for (var sector = 0; sector < Sectors; sector++)
        {
            for (var layer = 1; layer <= Layers; layer++)
            {
                var cell = Require(sector, layer);
                if (!cell.Digging || tick < cell.DigDoneTick) continue;

                cell.Digging = false;
                cell.DigWorkers = 0;
                cell.Dug = true;
                cell.RegrowthProgress = 0.0;

                // 重新挖开一处洞穴 = 洞里重新住满四只（上一次清剿留下的残留已经被回长重置过）。
                if (IsCaveCell(sector, layer))
                {
                    cell.Cleared = false;
                    cell.CaveMonsters = ArenaTuning.NativeCaveMonsters;
                    CaveOpened?.Invoke(sector, layer);
                }
            }
        }
    }

    private void ProduceFromPosts(long tick, bool miningPaused, double oreFactor, double materialFactor)
    {
        // 每人在岗每 2 游戏小时产 1 矿石；清剿成功的格再额外产特殊材料，
        // 未嵌入每 4 小时一件、已嵌入每 2 小时一件。
        // 挂在整游戏小时上：几格各有几个矿工，几点就结算几格（次序固定 = 象限再层）。
        if (tick <= 0 || tick % ArenaTime.StepsPerGameHour != 0) return;
        if (miningPaused) return;

        for (var sector = 0; sector < Sectors; sector++)
        {
            for (var layer = 1; layer <= Layers; layer++)
            {
                var cell = Require(sector, layer);
                if (!cell.Dug || cell.Miners <= 0) continue;

                cell.OreAccumulated += cell.Miners * (1.0 / ArenaTuning.OreGameHoursPerMiner) * oreFactor;
                if (cell.Cleared)
                {
                    var hours = cell.Embedded
                        ? ArenaTuning.EmbeddedMaterialGameHoursPerMiner
                        : ArenaTuning.MaterialGameHoursPerMiner;
                    cell.MaterialAccumulated += cell.Miners * (1.0 / hours) * materialFactor;
                }
            }
        }
    }

    private void Regrow(long tick, int visitorsInTown)
    {
        if (tick <= 0 || tick % ArenaTime.StepsPerGameDay != 0) return;

        // 自下而上：第 2 层先长（每游戏日 2 层速），长满了再第 1 层（每游戏日 1 层速）。
        // 只有「挖开且未嵌入」的格才回长；下面一层还悬着空腔时上面那一层不回长
        // —— 土壤自下而上长回来，下面没长满就先长上面等于凭空多出体积。
        for (var layer = Layers; layer >= 1; layer--)
        {
            var perDay = layer == Layers
                ? ArenaTuning.RegrowthLayersPerGameDayDeep
                : ArenaTuning.RegrowthLayersPerGameDayOuter;

            for (var sector = 0; sector < Sectors; sector++)
            {
                var cell = Require(sector, layer);
                if (cell.Embedded) continue;      // 嵌入魔晶后永久停止回长
                if (!cell.Dug) continue;          // 没挖开就没有可回长的空腔

                // 下面一层还开着空腔，这一层先不长（layer 1 是最上层，没有下面一层）。
                if (layer < Layers && Require(sector, layer + 1).Dug) continue;

                cell.RegrowthProgress += perDay;
                if (cell.RegrowthProgress >= 1.0)
                {
                    cell.RegrowthProgress = 0.0;
                    cell.Dug = false;
                    cell.Miners = 0;
                    cell.Digging = false;
                    cell.DigWorkers = 0;

                    // 洞穴格长回去 = 洞穴重置：清剿状态清零、洞里重新住满四只。
                    // 没嵌入的格都走这一条；嵌入过的格在上面就 continue 了，永远到不了这里。
                    if (IsCaveCell(sector, layer))
                    {
                        cell.Cleared = false;
                        cell.CaveMonsters = ArenaTuning.NativeCaveMonsters;
                        CaveReset?.Invoke(sector, layer);
                    }

                    // 回长填满已挖格 → 塌方（在格人员重伤或死亡、游客吓跑 20%、全镇情绪 -10）。
                    // 吓跑人数按递进来的在镇游客数算（<see cref="RecordCollapse"/> 是塌方那一栏的
                    // 唯一写口 —— 写死 0 的读数在快照上与「真的没人跑」分不开）。
                    RecordCollapse(sector, layer, tick, visitorsInTown);
                }
            }
        }
    }

    /// <summary>把某一次塌方的「在镇游客吓跑 20%」那一栏按在镇游客数算出来（记账那一侧递数进来）。</summary>
    public int VisitorsFledBy(int visitorsInTown)
    {
        if (visitorsInTown <= 0) return 0;
        return (int)Math.Floor(visitorsInTown * ArenaTuning.CollapseVisitorFleeShare);
    }

    /// <summary>给某次塌方补上按在镇游客数算出来的吓跑人数（塌方那一格按序号取人，不掷点）。</summary>
    public void RecordCollapse(int sector, int layer, long tick, int visitorsInTown)
    {
        _collapses.Add(new SoilCollapse(sector, layer, tick,
            VisitorsFledBy(visitorsInTown), ArenaTuning.CollapseTownMoodDrop));
    }
}
