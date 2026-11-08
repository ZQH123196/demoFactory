#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace DemoFactory.Prototypes.BattleArray.Logic;

/// <summary>
/// 种族 —— 用户确认：就两个人形种族，人族与兽人（design.md 2.2 节）。
///
/// M8 起两族**各带一条规则**、不再同值（<see cref="RaceRules"/>：人族手长、兽人力大），
/// 但规则只落在兵种卡的数值上 —— 结算与决策代码里没有任何一处按种族分支。
/// </summary>
public enum Race
{
    /// <summary>人族：盾卫 / 剑士 / 弓手 / 骑兵。规则：射程 ×1.1（手长）。</summary>
    Human,

    /// <summary>兽人：蛮牛 / 劫掠者 / 投斧手 / 狼骑。规则：攻击 ×1.1（力大）。</summary>
    Orc,
}

/// <summary>
/// M8 的种族规则 —— <b>两族各一条，全原型只有这一处让种族参与结算</b>
/// （用户确认：两侧各一条；M8 只验「兵种差异驱不驱得动数据」，不替「买兵要不要先看对面」那条待确认作数）。
///
/// 规则的<b>形式</b>是刻意的：一条乘数、作用在 <see cref="TroopCard"/> 的两个字段上，
/// 由 <see cref="UnitCard.Stats"/> 在取数值时施加一次 —— 八张卡的登记表照旧指向 5.2 节那张基准表，
/// 所以「谁穿哪族、这族多这一条」与「基准数值」两件事各自只有一处真值。
///
/// 它<b>不是</b>克制环：两条规则都是全族一视同仁的乘数，族内四档的相对关系一格没动，
/// 也没有「A 吃 B、B 吃 C、C 吃 A」的非传递性。要造那玩意儿是 `docs/prototypes/battle-array/待确认区.md` 那条 C，仍等用户确认。
/// </summary>
public static class RaceRules
{
    /// <summary>人族「手长」：全兵种射程 ×1.1 → 坦克 1.6→1.76、战士 1.8→1.98、弓手 9.0→9.9、骑手 2.0→2.2（米）。</summary>
    public const float HumanRangeMultiplier = 1.1f;

    /// <summary>兽人「力大」：全兵种攻击 ×1.1 后四舍五入取整 → 12→13、20→22、16→18、24→26。</summary>
    public const float OrcAttackMultiplier = 1.1f;

    /// <summary>
    /// 把这一族的规则施加到基准数值卡上。<b>唯一调用点</b>是 <see cref="UnitCard.Stats"/> ——
    /// 别处再调一次就是双重加成，那条口径有测试钉着。
    /// </summary>
    public static TroopCard Apply(Race race, TroopCard card) => race switch
    {
        // 射程取两位小数（四舍五入）：参数档（Scenes/Brains/*.tscn）写的是同一个十进制字面量，两边要能逐字对上
        Race.Human => card with { Range = MathF.Round(card.Range * HumanRangeMultiplier, 2, MidpointRounding.AwayFromZero) },
        Race.Orc => card with { Attack = (int)Math.Round(card.Attack * OrcAttackMultiplier, MidpointRounding.AwayFromZero) },
        _ => throw new ArgumentOutOfRangeException(nameof(race), $"未知种族 {race}"),
    };

    /// <summary>种族的中文名（快照、行为流、参数档里 <c>brain_label</c> 用的都是这三个字）。</summary>
    public static string Name(Race race) => race switch
    {
        Race.Human => "人族",
        Race.Orc => "兽人",
        _ => throw new ArgumentOutOfRangeException(nameof(race), $"未知种族 {race}"),
    };

    /// <summary>这一族带着哪一条 —— 快照与行为流里念得出的是这一句（显示即真相：读的就是上面那两个乘数）。</summary>
    public static string Describe(Race race) => race switch
    {
        Race.Human => $"{Name(race)} 手长（射程 ×{HumanRangeMultiplier}）",
        Race.Orc => $"{Name(race)} 力大（攻击 ×{OrcAttackMultiplier}）",
        _ => throw new ArgumentOutOfRangeException(nameof(race), $"未知种族 {race}"),
    };
}

/// <summary>
/// 一张兵种卡 = 种族 × 兵种槽位，八张（2×4）。
///
/// 这张表存在的理由是 M5 的结论：**兵种差异只落在数据**。
/// 基准数值（血/攻/间隔/射程/移速/半径）走 <see cref="BaseStats"/>，那是 design.md「兵种数据表 v1」那张表；
/// M8 起这一族还带一条规则，所以**结算取的数值是 <see cref="Stats"**（＝基准表过一遍
/// <see cref="RaceRules.Apply"/>，全原型只有这一个施加口子）。
/// 长什么样、叫什么、移动时算走还是算跑，全写在这里的字段里 —— 表现层不许为某个兵种开 if 分支。
/// 加第五个兵种 = 这里加一行 + 5.2 节加一行基准数值，别处不改（有测试逐件核对模型文件在不在）。
/// </summary>
/// <param name="Race">种族。</param>
/// <param name="Kind">兵种槽位（坦克/战士/弓手/骑手）。</param>
/// <param name="DisplayName">中文名 —— 头顶名牌与快照里读到的那两个字（design.md 2.2 节）。</param>
/// <param name="ModelName">模型名 = <c>Models/&lt;ModelName&gt;.glb</c> 的文件名，与体检单里的件名一字不差。</param>
/// <param name="BaseStats">基准数值卡（design.md「兵种数据表 v1」那张表的一行，<b>没</b>过种族规则）。</param>
/// <param name="Gallops">移动时动画算「跑」还是算「走」。逻辑层的移速差异已经在机制里，
/// 这一格只管腿怎么动：骑手该看得出在奔跑，其余四件走路。它是口径不是机制 —— 不参与任何结算。</param>
public sealed record UnitCard(
    Race Race,
    TroopKind Kind,
    string DisplayName,
    string ModelName,
    TroopCard BaseStats,
    bool Gallops)
{
    /// <summary>
    /// 这个兵种<b>实际参战</b>的数值 = 基准表 ×本族规则 —— 结算、快照、参数档校验读的都是这一格。
    /// 两族的差异只从这里长出来（M8 的验收面）：基准表两族共用一张，规则一处施加、只乘一次。
    /// </summary>
    public TroopCard Stats => RaceRules.Apply(Race, BaseStats);

    /// <summary>出手动作 clip 名（`attack_melee` / `attack_shoot`）—— 就在数值卡里，这里转一道手给表现层。</summary>
    public string AttackClip => BaseStats.AttackClip;
}

/// <summary>
/// 八张兵种卡（唯一的一份「谁是谁」登记表）。
///
/// 阵面对阵口径（2026-09-26 定）：**我方穿人族、敌方穿兽人** ⇒ 八件模型一局全上屏。
///
/// M8 之后这一行不再只是穿皮：<see cref="RaceRules"/> 给了两族各一条规则，敌我数值不再逐位相同。
/// 对照测试的归因前提因此换成两条，都还有测试钉着 ——
/// ① 种族由<b>方别</b>定（<see cref="RaceOf"/>）、与摆法无关 ⇒ 三档摆法/五档配比吃的是同一个恒定偏移，
///    档位之间的差仍然只能来自「谁站哪一格、买谁」；
/// ② 「镜像必判同尽」那条量的是<b>机制不偏袒先后手</b>，不是种族对称 ⇒ 它的口径改成两侧同族
///    （<see cref="Formation.Wearing"/>），异族镜像则另有一条测试专门断言它**必须**分得出胜负。
/// </summary>
public static class UnitCards
{
    /// <summary>人族四件（design.md 2.2 节那一行；数值＝5.2 节基准表 × 人族那条「手长」）。</summary>
    private static readonly UnitCard[] HumanCards =
    [
        new(Race.Human, TroopKind.Tank, "盾卫", "HumanTank", TroopTable.Tank, Gallops: false),
        new(Race.Human, TroopKind.Warrior, "剑士", "HumanWarrior", TroopTable.Warrior, Gallops: false),
        new(Race.Human, TroopKind.Archer, "弓手", "HumanArcher", TroopTable.Archer, Gallops: false),
        new(Race.Human, TroopKind.Rider, "骑兵", "HumanRider", TroopTable.Rider, Gallops: true),
    ];

    /// <summary>兽人四件（M3 的换皮：件名与叫法各自一套；M8 起数值＝5.2 节基准表 × 兽人那条「力大」）。</summary>
    private static readonly UnitCard[] OrcCards =
    [
        new(Race.Orc, TroopKind.Tank, "蛮牛", "OrcTank", TroopTable.Tank, Gallops: false),
        new(Race.Orc, TroopKind.Warrior, "劫掠者", "OrcWarrior", TroopTable.Warrior, Gallops: false),
        new(Race.Orc, TroopKind.Archer, "投斧手", "OrcArcher", TroopTable.Archer, Gallops: false),
        new(Race.Orc, TroopKind.Rider, "狼骑", "OrcRider", TroopTable.Rider, Gallops: true),
    ];

    /// <summary>全部八张（顺序＝种族 × 兵种槽位，报告与体检单都按这个序遍历）。</summary>
    public static IReadOnlyList<UnitCard> All { get; } = [.. HumanCards, .. OrcCards];

    /// <summary>按种族取那四张。</summary>
    public static IReadOnlyList<UnitCard> OfRace(Race race)
        => race == Race.Human ? HumanCards : OrcCards;

    /// <summary>
    /// 同一档兵种在登记表上的<b>第一件</b> —— 八张卡的顺序是人族四件在前（见 <see cref="All"/>），
    /// 所以这一发取到的是人族那件。开发期那台技能体检单只需要一个件名作代表（两族同骨架、同一份动作库、
    /// 起手 clip 时长相同），拿它当代表。
    ///
    /// 这一发<b>不读种族、不乘数值</b>：M8 那条「规则只住在兵种卡里、别处零种族分支」扫的就是
    /// <c>Race.X</c> 这种字样，这里按登记表顺序取，不出现分支。
    /// </summary>
    public static UnitCard FirstOfKind(TroopKind kind)
        => All.FirstOrDefault(c => c.Kind == kind)
           ?? throw new ArgumentOutOfRangeException(nameof(kind), $"兵种表里没有 {kind} 这一档");

    /// <summary>这一档兵种在两族各有哪几件（体检单在内容侧写明「两族各一件」时用）。</summary>
    public static IReadOnlyList<string> ModelsOfKind(TroopKind kind)
        => All.Where(c => c.Kind == kind).Select(c => c.ModelName).ToList();

    /// <summary>取一张卡；组合不存在时抛出去（宁可红在启动，不要悄悄兜一个别的兵种上屏）。</summary>
    public static UnitCard Of(Race race, TroopKind kind)
        => OfRace(race).FirstOrDefault(c => c.Kind == kind)
           ?? throw new ArgumentOutOfRangeException(nameof(kind), $"{race} 族没有 {kind} 这一档");

    /// <summary>按模型名反查（体检单、预览舞台那边只有件名）。</summary>
    public static UnitCard ByModel(string modelName)
        => All.FirstOrDefault(c => string.Equals(c.ModelName, modelName, StringComparison.Ordinal))
           ?? throw new ArgumentOutOfRangeException(nameof(modelName), $"兵种表里没有名为 {modelName} 的模型");

    /// <summary>
    /// 对阵双方的种族：<b>我方＝人族，敌方＝兽人</b>。
    /// 全原型只有这一处决定「谁默认穿哪族」；要改成两侧同族（比如只验人族四件），只改这一行。
    /// 单局要临时换皮不改这里：给那一份摆法挂上 <see cref="Formation.Wearing"/> 就行
    /// （M8 的归因测试靠它把两侧配成同族，用来证「机制不偏袒先后手」）。
    /// </summary>
    public static Race RaceOf(Side side) => side == Side.Player ? Race.Human : Race.Orc;
}
