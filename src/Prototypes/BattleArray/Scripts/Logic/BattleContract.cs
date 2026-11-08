#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;

namespace DemoFactory.Prototypes.BattleArray.Logic;

/// <summary>
/// 排兵布阵 M4 契约层 —— 场地/网格、兵种数据表 v1、三种摆法、对照跑的结果结构。
///
/// 数字全部照抄 `docs/prototypes/battle-array/design.md` 5.1~5.2 节（M0 已定稿，本文件不重新发明）；
/// 造型侧的同一批契约在 `Tools/BattleArrayTools.cs`，两边各自对 design.md 负责，不互相引用。
///
/// 架构位置：Scripts/Logic —— 纯 C#，零 Godot 类型（进 dotnet test）。
/// </summary>
public static class BattleContract
{
    /// <summary>逻辑步长：每推进一步 = 0.1 秒（报时间只说秒，换算口径：步数 ÷ 10 = 秒）。</summary>
    public const float StepSeconds = 0.1f;

    /// <summary>每秒步数 —— 契约里的「秒」换算成实现里的「步」只走这一个口子。</summary>
    public const int StepsPerSecond = 10;

    /// <summary>
    /// 一局战斗时长上限：**120 秒**（design.md 「已定」第 5 条，2026-09-26 用户改判「只说时间、不说步数」）。
    /// 到点还没一方全灭 ⇒ 按 「已定」第 5 条判我方赢（<see cref="BattleWorld.DecidedByTimeout"/> 记下这一笔）。
    /// 它不是目标时长而是兜底：满编 15v15 实测 20~31 秒打完，对照跑 60 局一次都没碰过这条线
    /// （有测试守着，碰了就是红的 —— 见 <c>对照跑没有一局走到时长上限</c>）。
    /// </summary>
    public const int MaxBattleSeconds = 120;

    /// <summary>上限换算成步（120 秒 × 10）—— 实现用它，对外说话用上面那个秒。</summary>
    public const int MaxBattleTicks = MaxBattleSeconds * StepsPerSecond;

    /// <summary>
    /// 开局记账点：**12 秒**（design.md 2 节 第 6 条）。
    /// 原来取 30 秒，那是按「一局 10 分钟」定的；短局改成定案后 30 秒那一格几乎等于终局那一格，
    /// 曲线塌成一个点。取 12 秒的理由是实测事实：三种摆法的伤亡曲线**前 12 秒重叠、18 秒才分岔**
    /// （数字见 `docs/prototypes/battle-array/灰盒阶段结论.md`「M4 已完成」）—— 这一格量的正是「站位的好处还没显形时谁吃亏」。
    /// </summary>
    public const int OpeningCheckpointSeconds = 12;

    /// <summary>开局记账点换算成步（120 步）。</summary>
    public const int OpeningCheckpointTicks = OpeningCheckpointSeconds * StepsPerSecond;

    /// <summary>格边长 2.0 米 —— 布阵期占格口径；战斗期是连续空间，格子不再是硬约束。</summary>
    public const float CellSize = 2.0f;

    /// <summary>布阵区：每方 5 列 × 3 行 = 15 格（10 米宽 × 6 米深）；格子数即出兵上限。</summary>
    public const int Columns = 5;
    public const int Rows = 3;

    /// <summary>战场 24 米宽 × 24 米深：两军布阵区各 6 米深，中间 12 米接敌带。</summary>
    public const float ArenaHalfWidth = 12.0f;
    public const float ArenaHalfDepth = 12.0f;

    /// <summary>最前一排的格中心离战场中线 7 米 ⇒ 两军前排相距 14 米。</summary>
    public const float FrontRowDepth = 7.0f;

    /// <summary>
    /// 格中心坐标（row 0 = 最接近敌人的一排；敌方沿中线镜像）。
    /// 列 0~4 → X = −4,−2,0,2,4；行 0~2 → 我方 Y = 7,9,11，敌方 Y = −7,−9,−11。
    /// </summary>
    public static Vector2 CellCenter(Side side, int row, int column)
    {
        if (row < 0 || row >= Rows) throw new ArgumentOutOfRangeException(nameof(row), $"行号必须 0~{Rows - 1}");
        if (column < 0 || column >= Columns) throw new ArgumentOutOfRangeException(nameof(column), $"列号必须 0~{Columns - 1}");
        var depth = FrontRowDepth + CellSize * row;
        return new Vector2(-4.0f + CellSize * column, side == Side.Player ? depth : -depth);
    }

    /// <summary>
    /// 把世界坐标（逻辑口径：X 与 Y，Y 就是世界的 Z 深度）换算成布阵格。
    /// 落点在布阵区外（含中央 12 米接敌带与场外）时返回 false —— 点空地不该有反应，也不兜到最近的格。
    /// 口径与 <see cref="CellCenter"/> 严格互逆：格 r 覆盖深度 [6,8+2r) 那一档（前排格中心 7 米 ±1 米，
    /// 三行连起来是 [6,12)），格 c 覆盖 X 的 [−5,5) 五等分。
    /// </summary>
    /// <param name="x">横向坐标（米）。</param>
    /// <param name="y">深度坐标（米）—— 正数是我方侧，负数是敌方侧。</param>
    /// <param name="side">落点属于哪一侧（敌方布阵区同样算得出来：回放棋盘与摆位对照都用得上）。</param>
    public static bool TryCellFromWorldPoint(float x, float y, out Side side, out int row, out int column)
    {
        side = y >= 0f ? Side.Player : Side.Enemy;
        row = -1;
        column = -1;

        var depth = MathF.Abs(y);
        var nearEdge = FrontRowDepth - CellSize * 0.5f;          // 6 米：最前一排格的靠中线那一边
        var farEdge = FrontRowDepth + CellSize * (Rows - 1) + CellSize * 0.5f; // 12 米：最后一排的远边
        var halfWidth = CellSize * Columns * 0.5f;               // 5 米：五列的半宽
        if (depth < nearEdge || depth >= farEdge) return false;
        if (x < -halfWidth || x >= halfWidth) return false;

        row = (int)MathF.Floor((depth - nearEdge) / CellSize);
        column = (int)MathF.Floor((x + halfWidth) / CellSize);
        return row >= 0 && row < Rows && column >= 0 && column < Columns;
    }

    /// <summary>把坐标夹回战场矩形内（单位不该走出 24×24 的场地）。</summary>
    public static Vector2 ClampToArena(Vector2 position)
        => new(Math.Clamp(position.X, -ArenaHalfWidth, ArenaHalfWidth),
               Math.Clamp(position.Y, -ArenaHalfDepth, ArenaHalfDepth));

    /// <summary>
    /// 兵种单价（design.md 「已定」第 8 条与「第 8 条的下游后果」）—— 钱在 2026-09-26 的确认里已经退化成<b>名义额度</b>：
    /// 每关发一笔够买满格子的钱，价差只在兵源面板上展示，<b>不参与任何结算</b>，也不做平衡。
    /// 留它的理由写在 design.md 2.1 节：核心循环那句「买 → 摆 → 放 → 结算」里「买」这一步还在。
    /// </summary>
    public static int PriceOf(TroopKind kind) => kind switch
    {
        TroopKind.Tank => 120,
        TroopKind.Warrior => 100,
        TroopKind.Archer => 80,
        TroopKind.Rider => 150,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), $"未知兵种 {kind}"),
    };

    /// <summary>四个槽位（布阵区与兵源面板都按这个序遍历，别按字典序）。</summary>
    public static readonly IReadOnlyList<TroopKind> Kinds =
        [TroopKind.Tank, TroopKind.Warrior, TroopKind.Archer, TroopKind.Rider];

    /// <summary>直达口与报告用的 ASCII 键（eval 里传中文名太脆，同 <see cref="Formation.Key"/> 的理由）。</summary>
    public static string KeyOf(TroopKind kind) => kind switch
    {
        TroopKind.Tank => "tank",
        TroopKind.Warrior => "warrior",
        TroopKind.Archer => "archer",
        TroopKind.Rider => "rider",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), $"未知兵种 {kind}"),
    };

    /// <summary>按 ASCII 键取兵种；不认识时返回 null（调用方要把可选清单打回去，不许静默兜一个）。</summary>
    public static TroopKind? KindFromKey(string? key)
    {
        foreach (var kind in Kinds)
        {
            if (string.Equals(KeyOf(kind), key, StringComparison.OrdinalIgnoreCase)) return kind;
        }
        return null;
    }
}

/// <summary>对阵双方 —— 玩家方与敌方（M4 只有一种架：我方摆法 vs 敌方固定摆法）。</summary>
public enum Side
{
    Player,
    Enemy,
}

/// <summary>兵种槽位 —— 2 种族 × 4 兵种里的那 4 个槽（design.md 2.2 节；种族规则见 <c>UnitCards.cs</c> 的 <c>RaceRules</c>，M8 已落）。</summary>
public enum TroopKind
{
    /// <summary>坦克（盾卫 / 蛮牛）：血最厚、出手最慢、天生该站最前。</summary>
    Tank,

    /// <summary>战士（剑士 / 劫掠者）：均衡输出。</summary>
    Warrior,

    /// <summary>弓手（弓手 / 投斧手）：射程 9 米的脆皮 —— 摆位对照测的就是它站哪。</summary>
    Archer,

    /// <summary>骑手（骑兵 / 狼骑）：最快最狠，身体半径也最大（最挤）。</summary>
    Rider,
}

/// <summary>
/// 兵种数据卡 —— design.md「兵种数据表 v1」那张表，一个数都不改。
/// 伤害公式最简：每间隔扣一次「攻击」点血，无暴击、无护甲、无技能（2 节 第 2 条：只平A）。
/// </summary>
public readonly record struct TroopCard(
    TroopKind Kind,
    int Health,
    int Attack,
    int AttackIntervalTicks,
    float Range,
    float Speed,
    float BodyRadius,
    string AttackClip)
{
    /// <summary>出手判定距离 = 射程 + 双方身体半径（近战不需要额外「贴脸」容差）。</summary>
    public float ReachAgainst(TroopCard target) => Range + BodyRadius + target.BodyRadius;
}

/// <summary>
/// 四张数值卡 —— design.md「兵种数据表 v1」那张表，一个数都不改。
/// 这是**兵种槽位**的表，不分种族：两族吃同一张（5.2 节「兽人同表换比例与配色」），
/// 「谁叫什么、穿哪件模型」在 <c>UnitCards.cs</c> 那八张卡里。
/// 伤害公式最简：每间隔扣一次「攻击」点血，无暴击、无护甲、无技能（2 节 第 2 条：只平A）。
/// </summary>
public static class TroopTable
{
    public static readonly TroopCard Tank =
        new(TroopKind.Tank, Health: 260, Attack: 12, AttackIntervalTicks: 14, Range: 1.6f, Speed: 1.2f, BodyRadius: 0.55f, "attack_melee");

    public static readonly TroopCard Warrior =
        new(TroopKind.Warrior, Health: 170, Attack: 20, AttackIntervalTicks: 10, Range: 1.8f, Speed: 1.5f, BodyRadius: 0.45f, "attack_melee");

    public static readonly TroopCard Archer =
        new(TroopKind.Archer, Health: 90, Attack: 16, AttackIntervalTicks: 12, Range: 9.0f, Speed: 1.4f, BodyRadius: 0.40f, "attack_shoot");

    public static readonly TroopCard Rider =
        new(TroopKind.Rider, Health: 150, Attack: 24, AttackIntervalTicks: 11, Range: 2.0f, Speed: 3.2f, BodyRadius: 0.75f, "attack_melee");

    public static TroopCard Of(TroopKind kind) => kind switch
    {
        TroopKind.Tank => Tank,
        TroopKind.Warrior => Warrior,
        TroopKind.Archer => Archer,
        TroopKind.Rider => Rider,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), $"未知兵种 {kind}"),
    };
}

/// <summary>一个兵种在布阵区的一个格子里（一兵一格，全兵种一律占 1 格）。</summary>
public readonly record struct UnitPlacement(TroopKind Kind, int Row, int Column);

/// <summary>
/// 一种摆法 —— 15 个格子怎么填。阵容内容三种摆法完全相同（4 坦 4 战 4 弓 3 骑），
/// 只有「谁在哪一格」不同：这正是对照测试要的，兵不动、只换位。
///
/// <paramref name="race"/> 是 M8 加的一格<b>数据</b>：这一方穿哪族（null ＝ 照对阵口径
/// <see cref="UnitCards.RaceOf"/>）。它存在的理由只有一条 ——「镜像必判同尽」那条报警器量的是
/// <b>机制不偏袒先后手</b>，而 M8 之后两族各带一条规则，两侧不同族就证不了对称 ⇒
/// 归因测试用 <see cref="Wearing"/> 把两侧调成同族。它不开任何新机制：结算读的还是兵种卡。
/// </summary>
public sealed class Formation
{
    public Formation(string key, string displayName, IReadOnlyList<UnitPlacement> placements, Race? race = null)
    {
        Key = key;
        DisplayName = displayName;
        Placements = placements;
        Race = race;
    }

    /// <summary>报告与直达口用的 ASCII 键（eval 里传中文名太脆）。</summary>
    public string Key { get; }

    /// <summary>中文名 —— 报告里的那一行。</summary>
    public string DisplayName { get; }

    public IReadOnlyList<UnitPlacement> Placements { get; }

    /// <summary>这一方穿哪族；null ＝ 由方别定（我方人族、敌方兽人）。</summary>
    public Race? Race { get; }

    /// <summary>该方兵种数（对照测试用它核对「三种摆法阵容一模一样」）。</summary>
    public int Count => Placements.Count;

    /// <summary>换一层皮、格子一个不动（归因测试用：两侧同族才谈得上「机制偏不偏袒先后手」）。</summary>
    public Formation Wearing(Race race) => new(Key, DisplayName, Placements, race);

    public override string ToString() => $"{Key}（{DisplayName}，{Placements.Count} 兵）";
}

/// <summary>
/// 三种摆法 + 敌方固定摆法。
///
/// 三条摆沿同一根轴拉开：**四个弓手离接敌线多远**。
/// 敌方一律用 <see cref="ShieldWall"/>（与我方镜像同阵容）⇒ 阵容差异被消掉，
/// 三种摆法之间任何胜率/时长/伤亡差都只能来自摆位本身（对照测试的归因前提）。
/// </summary>
public static class Formations
{
    /// <summary>满编阵容：4 坦 + 4 战 + 4 弓 + 3 骑 = 15 兵（格子数即出兵上限）。</summary>
    public const int FullRosterSize = 15;

    /// <summary>「盾墙在前」（常规线）：前排坦克＋骑手、中排战士＋骑手、后排弓手＋骑手 —— 弓手离前线两排。</summary>
    public static readonly Formation ShieldWall = Build(
        "shield-wall", "盾墙在前（常规线）",
        front: [TroopKind.Tank, TroopKind.Tank, TroopKind.Rider, TroopKind.Tank, TroopKind.Tank],
        mid: [TroopKind.Warrior, TroopKind.Warrior, TroopKind.Rider, TroopKind.Warrior, TroopKind.Warrior],
        back: [TroopKind.Archer, TroopKind.Archer, TroopKind.Rider, TroopKind.Archer, TroopKind.Archer]);

    /// <summary>「弓手顶前排」：四个弓手全压最前一排，坦克缩到最后 —— 弓手离前线零排。</summary>
    public static readonly Formation ArchersForward = Build(
        "archers-forward", "弓手顶前排",
        front: [TroopKind.Archer, TroopKind.Archer, TroopKind.Tank, TroopKind.Archer, TroopKind.Archer],
        mid: [TroopKind.Warrior, TroopKind.Warrior, TroopKind.Rider, TroopKind.Warrior, TroopKind.Warrior],
        back: [TroopKind.Tank, TroopKind.Rider, TroopKind.Tank, TroopKind.Tank, TroopKind.Rider]);

    /// <summary>「弓手夹中间」：前排全坦克、弓手在第二排、战士掉到最后 —— 弓手离前线一排（无纵深层次）。</summary>
    public static readonly Formation ArchersMidline = Build(
        "archers-midline", "弓手夹中间",
        front: [TroopKind.Tank, TroopKind.Tank, TroopKind.Warrior, TroopKind.Tank, TroopKind.Tank],
        mid: [TroopKind.Archer, TroopKind.Archer, TroopKind.Archer, TroopKind.Archer, TroopKind.Rider],
        back: [TroopKind.Warrior, TroopKind.Warrior, TroopKind.Rider, TroopKind.Rider, TroopKind.Warrior]);

    /// <summary>对照测试的三档（顺序即报告顺序）。</summary>
    public static readonly IReadOnlyList<Formation> ComparisonSet = [ShieldWall, ArchersForward, ArchersMidline];

    /// <summary>按 ASCII 键取摆法（调试直达口用）；不认识时返回 null。</summary>
    public static Formation? Find(string key)
    {
        foreach (var f in ComparisonSet)
        {
            if (string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase)) return f;
        }
        return null;
    }

    /// <summary>三行五列 → 15 个格子；每行给五个兵种，row 0 = 最前一排。</summary>
    private static Formation Build(string key, string displayName, TroopKind[] front, TroopKind[] mid, TroopKind[] back)
    {
        if (front.Length != BattleContract.Columns || mid.Length != BattleContract.Columns || back.Length != BattleContract.Columns)
        {
            throw new ArgumentException($"摆法每行必须 {BattleContract.Columns} 列");
        }
        var placements = new List<UnitPlacement>(FullRosterSize);
        var rows = new[] { front, mid, back };
        for (var row = 0; row < rows.Length; row++)
        {
            for (var column = 0; column < rows[row].Length; column++)
            {
                placements.Add(new UnitPlacement(rows[row][column], row, column));
            }
        }
        return new Formation(key, displayName, placements);
    }
}

/// <summary>战斗判定 —— 非 Running 即已判定，判定后世界不再演化（「打多久」才有唯一起点）。</summary>
public enum BattleOutcome
{
    /// <summary>还没打完。</summary>
    Running,

    /// <summary>
    /// 敌方全灭 ⇒ 我方胜。**打满 120 秒双方都还有人也算这一档**（design.md 「已定」第 5 条：超时判赢），
    /// 两者的区别记在 <see cref="BattleWorld.DecidedByTimeout"/>，不额外开一个枚举值 ——
    /// 统计口径上它就是一场胜。
    /// </summary>
    PlayerVictory,

    /// <summary>我方全灭 —— 我方败。</summary>
    PlayerDefeat,

    /// <summary>两军同时全灭（同一步互杀到底）。</summary>
    Draw,
}
