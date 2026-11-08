#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DemoFactory.Prototypes.BattleArray.Logic;
using Shouldly;
using Xunit;
using DemoFactory.GugaFramework.Tests;

namespace BattleArray.Tests;

/// <summary>
/// 兵种数据面 —— M5 那一条「兵种差异只在数据」＋ M8 那一条「种族规则只落在数据」的机器可执行版本。
///
/// 压的五件事：
/// 1. 八张卡齐（2 种族 × 4 兵种），叫法与 design.md 2.2 节那张命名表一字不差；
/// 2. 基准表（5.2 节）两族共用一张，两族的差只从 <c>RaceRules</c> 那两条乘数长出来 ——
///    规则写在明处、条数钉死（偷偷往数值里塞第三条 ⇒ 这条红）；
/// 3. 表里登记的八件模型文件真在 `Models/` 下，一件不多、一件不少（加了兵种忘了建模 ⇒ 上屏必炸，先在这儿红）；
/// 4. 阵面对阵按「我方人族 / 敌方兽人」配种族 ⇒ 一局八件全上屏；
/// 5. 归因地基换了口径但没塌：<b>同族</b>镜像同摆法必判同尽（机制不偏袒先后手），
///    <b>异族</b>镜像同摆法必须分得出胜负（种族规则真有因果力，不是摆设）。
/// </summary>
public class UnitCardDataTests
{
    /// <summary>2.2 节命名表：种族 × 兵种槽位 → 中文名与件名。</summary>
    private static readonly (Race, TroopKind, string, string)[] Naming =
    [
        (Race.Human, TroopKind.Tank, "盾卫", "HumanTank"),
        (Race.Human, TroopKind.Warrior, "剑士", "HumanWarrior"),
        (Race.Human, TroopKind.Archer, "弓手", "HumanArcher"),
        (Race.Human, TroopKind.Rider, "骑兵", "HumanRider"),
        (Race.Orc, TroopKind.Tank, "蛮牛", "OrcTank"),
        (Race.Orc, TroopKind.Warrior, "劫掠者", "OrcWarrior"),
        (Race.Orc, TroopKind.Archer, "投斧手", "OrcArcher"),
        (Race.Orc, TroopKind.Rider, "狼骑", "OrcRider"),
    ];

    [Fact]
    public void 八张卡齐_叫法与件名照设计表_一格不多一格不少()
    {
        UnitCards.All.Count.ShouldBe(8, "M5 就是八件模型全上屏，八张卡少了谁都不算过");
        UnitCards.All.Select(c => (c.Race, c.Kind, c.DisplayName, c.ModelName)).ShouldBe(Naming, true, "兵种表与 design.md 2.2 节的命名对不上（改了哪一边？）");
        UnitCards.All.Select(c => c.ModelName).Distinct().Count().ShouldBe(8, "两个兵种穿同一件模型 ⇒ 阵面上分不出是谁");
    }

    /// <summary>M8 落的两条种族规则（用户确认：两侧各一条）—— 这条测试与数据表各写一份，谁漂谁红。</summary>
    private static readonly (Race, TroopKind, int Attack, float Range)[] BattleStats =
    [
        //        族      兵种      攻  射程（米）  ← 攻：兽人 ×1.1；射程：人族 ×1.1（5.2 节基准表 12/20/16/24 与 1.6/1.8/9.0/2.0）
        (Race.Human, TroopKind.Tank, 12, 1.76f),
        (Race.Human, TroopKind.Warrior, 20, 1.98f),
        (Race.Human, TroopKind.Archer, 16, 9.9f),
        (Race.Human, TroopKind.Rider, 24, 2.2f),
        (Race.Orc, TroopKind.Tank, 13, 1.6f),
        (Race.Orc, TroopKind.Warrior, 22, 1.8f),
        (Race.Orc, TroopKind.Archer, 18, 9.0f),
        (Race.Orc, TroopKind.Rider, 26, 2.0f),
    ];

    [Fact]
    public void 八张卡参战数值全等在登记的那八行()
    {
        foreach (var (race, kind, attack, range) in BattleStats)
        {
            var card = UnitCards.Of(race, kind).Stats;
            card.Attack.ShouldBe(attack, $"{race}/{kind} 的攻击漂了（种族规则以外不许动数值）");
            card.Range.ShouldBe(range, 1e-4, $"{race}/{kind} 的射程漂了");
        }
    }

    [Fact]
    public void 两族只差那两条规则_其余数值逐位相同()
    {
        // M8 之前的口径是「两族数值逐位相同」；M8 之后两族各带一条规则，口径换成这一条：
        // <b>差只许差在攻击与射程上，且必须正好等于那两条乘数</b>——血、攻击间隔、移速、身体半径两族该一模一样。
        // 有人往别的格子里塞第三条规则（或偷偷改了基准表），这里当场红。
        foreach (var kind in new[] { TroopKind.Tank, TroopKind.Warrior, TroopKind.Archer, TroopKind.Rider })
        {
            var human = UnitCards.Of(Race.Human, kind);
            var orc = UnitCards.Of(Race.Orc, kind);

            // 基准表两族共用一张：过规则<b>之前</b>那几个数必须完全相同
            orc.BaseStats.ShouldBe(human.BaseStats, $"{kind}：两族吃的基准表不是同一张（5.2 节那张表被分家了？）");

            orc.Stats.Health.ShouldBe(human.Stats.Health, $"{kind}：血不该被种族规则动");
            orc.Stats.AttackIntervalTicks.ShouldBe(human.Stats.AttackIntervalTicks, $"{kind}：攻击间隔不该被种族规则动");
            orc.Stats.Speed.ShouldBe(human.Stats.Speed, $"{kind}：移速不该被种族规则动");
            orc.Stats.BodyRadius.ShouldBe(human.Stats.BodyRadius, $"{kind}：身体半径不该被种族规则动");
            orc.AttackClip.ShouldBe(human.AttackClip, $"{kind}：出手动作两族该一致");
            orc.Gallops.ShouldBe(human.Gallops, $"{kind}：步态档两族该一致");

            // 两条规则各管一格，且只在它那一族生效
            human.Stats.Range.ShouldBe(RaceRules.Apply(Race.Human, human.BaseStats).Range, 1e-4, $"{kind}：人族射程没走 RaceRules 那条口子");
            human.Stats.Attack.ShouldBe(human.BaseStats.Attack, $"人族那条只管射程，{kind} 的攻击被人动了");
            orc.Stats.Range.ShouldBe(orc.BaseStats.Range, $"兽人那条只管攻击，{kind} 的射程被人动了");
            orc.Stats.Attack.ShouldBe(RaceRules.Apply(Race.Orc, orc.BaseStats).Attack, $"{kind}：兽人攻击没走 RaceRules 那条口子（双重加成？）");
        }
    }

    [Fact]
    public void 兵种表登记的八件模型真在Models下_一件不多一件不少()
    {
        var modelsDir = Path.Combine(RepoRoot(), "src", "Prototypes", "BattleArray", "Models");
        Directory.Exists(modelsDir).ShouldBeTrue($"找不到模型目录：{modelsDir}");

        var onDisk = Directory.GetFiles(modelsDir)
            .Where(f => string.Equals(Path.GetExtension(f), ".glb", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileNameWithoutExtension)
            .ToList();

        onDisk.OrderBy(n => n).ShouldBe(UnitCards.All.Select(c => c.ModelName).OrderBy(n => n), "兵种表与 Models/ 对不上：加了兵种没建模、或建模没登记 —— 上屏那天必然加载失败");
    }

    [Fact]
    public void 我方穿人族敌方穿兽人_一局八件全上屏()
    {
        UnitCards.RaceOf(Side.Player).ShouldBe(Race.Human);
        UnitCards.RaceOf(Side.Enemy).ShouldBe(Race.Orc);

        using var world = new BattleWorld(1001, Formations.ShieldWall, PlacementComparison.EnemyFormation);
        var worn = world.Units.Select(u => u.Look.ModelName).ToList();

        worn.Count.ShouldBe(Formations.FullRosterSize * 2, "满编 15 对 15 该有 30 件");
        worn.Distinct().Count().ShouldBe(8, "一局里八件模型没全露面（对照报告的剪影对比就少一半证据）");
        world.Units.Where(u => u.Side == Side.Player).All(u => u.Race == Race.Human).ShouldBeTrue();
        world.Units.Where(u => u.Side == Side.Enemy).All(u => u.Race == Race.Orc).ShouldBeTrue();
    }

    [Fact]
    public void 同族镜像同摆法必判同尽_机制不偏袒先后手()
    {
        // M4 立的那条归因地基，M8 之后要这样问才成立：把两侧配成<b>同一族</b>（摆法一个都不动）。
        // 它量的一直是「机制偏不偏袒先出手的那一方」，不是「两族对不对得称」——
        // 种族各带一条规则之后，不同族的镜像本来就该分出胜负（见下一条），拿它当对称尺会把尺用错。
        foreach (var formation in Formations.ComparisonSet)
        {
            var ours = formation.Wearing(Race.Human);
            using var world = new BattleWorld(1001, ours, ours, jitter: false);
            world.AdvanceUntilEnd().ShouldBeTrue($"{formation.Key} 该在 120 秒上限内打完");
            world.Outcome.ShouldBe(BattleOutcome.Draw, $"{formation.Key} 同族镜像出现胜负 ⇒ 这套规则有先后手偏袒，对照测试的归因不成立");

            var orcMirror = formation.Wearing(Race.Orc);
            using var orcWorld = new BattleWorld(1001, orcMirror, orcMirror, jitter: false);
            orcWorld.AdvanceUntilEnd().ShouldBeTrue($"{formation.Key} 兽人镜像该打完");
            orcWorld.Outcome.ShouldBe(BattleOutcome.Draw, $"{formation.Key} 兽人镜像出现胜负 ⇒ 偏袒与穿谁家的皮有关，不该如此");
        }
    }

    [Fact]
    public void 异族镜像同摆法必分胜负_种族规则确有因果力()
    {
        // 这一条是 M8 的验收面之一：阵面照默认对阵（我方人族 vs 敌方兽人）、兵与摆法都不动 ——
        // 如果结果还是同归于尽，那两条乘数就是纸面上的规则，没进结算。
        foreach (var formation in Formations.ComparisonSet)
        {
            using var world = new BattleWorld(1001, formation, formation, jitter: false);
            world.AdvanceUntilEnd().ShouldBeTrue($"{formation.Key} 异族镜像该打完");
            world.Outcome.ShouldNotBe(BattleOutcome.Draw, $"{formation.Key} 异族镜像仍判同尽 ⇒ 种族规则没参与结算（M8 的验收不成立）");
            world.Units.Where(u => u.Side == Side.Player).All(u => u.Race == Race.Human).ShouldBeTrue();
            world.Units.Where(u => u.Side == Side.Enemy).All(u => u.Race == Race.Orc).ShouldBeTrue();
        }
    }

    [Fact]
    public void 每个兵参战的数值卡就是他那张兵种卡_不存在第二份账()
    {
        // 世界的 Card 与兵种卡的 Stats 必须同源：M8 把「谁吃哪条规则」收在 UnitCard.Stats 一处，
        // 世界这边只是读它。哪天有人绕开卡另算一份数值（或规则被乘两次），这条先红。
        using var world = new BattleWorld(1001, Formations.ShieldWall, PlacementComparison.EnemyFormation);
        foreach (var unit in world.Units)
        {
            unit.Card.ShouldBe(unit.Look.Stats, $"{unit.Id} 参战的数值与兵种卡不同源");
            unit.MaxHealth.ShouldBe(unit.Look.Stats.Health, $"{unit.Id} 的血不是卡上的血");
        }
    }

    [Fact]
    public void 快照读得出叫法_观测口与兵种表同一份数据()
    {
        // 显示即真相的另一半：快照里那两个字与兵种表同源，不是表现层另写的一套名字。
        using var world = new BattleWorld(1001, Formations.ShieldWall, PlacementComparison.EnemyFormation);
        var snapshot = world.DebugSnapshot();

        foreach (var card in UnitCards.All)
        {
            snapshot.ShouldContain(card.DisplayName, Case.Sensitive, $"快照里读不到「{card.DisplayName}」");
        }

        // M8 补的两格：这一局带着哪几条种族规则、每个兵实际够得着几米，都要从快照直接读出来，
        // 不用回去翻代码（否则「规则进没进结算」只能靠读源码回答）。
        var header = snapshot.Split('\n')[0];
        header.ShouldContain(RaceRules.Describe(Race.Human), Case.Sensitive);
        header.ShouldContain(RaceRules.Describe(Race.Orc), Case.Sensitive, $"快照表头念不出这局吃的是哪两条种族规则：{header}");
        foreach (var unit in world.Units)
        {
            snapshot.ShouldContain($"atk={unit.Card.Attack} rng={unit.Card.Range.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} ", Case.Sensitive, $"{unit.Id} 的快照没把他实际参战的射程念出来");
        }
    }

    /// <summary>
    /// M8 那一条「逻辑层 diff ＝ 0 行结构性改动」的机器可执行版本 —— 扫全原型的代码面（不含测试与本表），
    /// 除兵种卡那份数据以外**不许有任何一处按种族分支**：既没有 <c>Race.Human/Race.Orc</c> 的判等，
    /// 也没有 <c>switch (race)</c>。规则只住在 <c>RaceRules.Apply</c> 一处，别处只读兵种卡。
    ///
    /// 为什么用扫源码这种笨办法：这条命题的主语是「代码结构」，不是「某个数」——
    /// 行为断言证不了「没有分支」（没走到那一支也是全绿）。M5 的射程校验同样是离线逐字扫文本，同一个思路。
    /// </summary>
    [Fact]
    public void 逻辑层与表现层零种族分支_规则只住在兵种卡里()
    {
        var root = RepoRoot();
        var dir = Path.Combine(root, "src", "Prototypes", "BattleArray");

        // 允许出现种族字面量的三处，全是「数据」不是「分支」：
        //   UnitCards.cs —— 兵种卡登记表与 RaceRules 本体（规则就该长在这）
        //   BattleContract.cs —— Race 枚举的声明处
        //   PlacementComparison.cs —— 对照跑钉哪一族当控制档（一个常量，不参与结算）
        var allowListed = new[] { "UnitCards.cs", "BattleContract.cs", "PlacementComparison.cs" };
        var branch = new Regex(@"Race\.(Human|Orc)|(?:^|[^\w.])race\s*(?:==|!=)|switch\s*\(\s*\w*[Rr]ace",
            RegexOptions.Multiline | RegexOptions.Compiled);

        var offenders = new List<string>();
        var sources = new[] { "Scripts", "AI", "UI", "Tools" }
            .SelectMany(sub => Directory.GetFiles(Path.Combine(dir, sub), "*", SearchOption.AllDirectories))
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".gd", StringComparison.Ordinal));
        foreach (var file in sources.OrderBy(f => f, StringComparer.Ordinal))
        {
            if (allowListed.Contains(Path.GetFileName(file), StringComparer.Ordinal)) continue;
            foreach (Match match in branch.Matches(File.ReadAllText(file, Encoding.UTF8)))
            {
                offenders.Add($"{Path.GetRelativePath(root, file)}：{match.Value.Trim()}");
            }
        }

        offenders.ShouldBeEmpty("出现了兵种卡以外的种族分支 —— M8 的验收是「换皮 + 一条规则、逻辑层零结构改动」，" +
            "规则要加就往 RaceRules/兵种卡加数据，别在结算与表现层开 if：\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// 种族规则的<b>量级</b> —— 摆法与兵都不动，只把「对面穿哪族」换掉，看胜率挪多少。
    ///
    /// 门槛（跑之前登记，不看完数字再挑）：与 M7 的「有差」线同口径 —— 胜率挪 ≥ <b>15 个百分点</b>
    /// 才算这条规则有因果力；挪不到就把 M8 的验收如实记成「规则进了结算、但量级看不出」。
    /// 之所以比同一种子、同一摆法：只有这样「族」才是唯一变量（与摆位/配比对照同一个设计）。
    /// </summary>
    [Fact]
    public void 只换对面那一族_镜像局胜率就该挪出十五个百分点()
    {
        const double MattersWinRateShift = 0.15;

        var ours = PlacementComparison.Controlled(Formations.ShieldWall);   // 我方：人族（对照档那一族）
        var rival = ours.Wearing(Race.Orc);                                 // 对面改穿兽人 ⇒ 摆法一格没动

        var baseline = WinRate(ours, ours, out var baseOurDeaths, out var baseEnemyDeaths);
        var withRule = WinRate(ours, rival, out var ruleOurDeaths, out var ruleEnemyDeaths);
        var shift = Math.Abs(withRule - baseline);

        // 零点检查（不是「该等于五成」）：同族镜像全靠抖动抽序拆开双方（先出场的那一侧天然占便宜，
        // M7 记过这一笔：均衡 vs 均衡 60%）。这里只要求这一比**有得比** —— 20 局全一个结果就是尺子坏了。
        baseline.ShouldBeGreaterThan(0.0);
        baseline.ShouldBeLessThan(1.0, $"同族镜像 20 局全是同一个结果（{baseline:P0}）⇒ 抖动没把对称局拆开，这一比没有零点");
        shift.ShouldBeGreaterThanOrEqualTo(MattersWinRateShift, $"只换对面那一族，胜率只挪了 {shift:P0}（同族 {baseline:P0} → 异族 {withRule:P0}）" +
            "—— 规则进了结算但量级看不出，M8 的验收不成立");
        TestLog.Ledger($"种族规则量级（同一组 20 粒种子、同一份摆法，只换对面那一族）：\n" +
                                  $"  同族（人族 vs 人族）胜率 {baseline:P0}｜我方均阵亡 {baseOurDeaths:F2}｜敌方均阵亡 {baseEnemyDeaths:F2}\n" +
                                  $"  异族（人族 vs 兽人）胜率 {withRule:P0}｜我方均阵亡 {ruleOurDeaths:F2}｜敌方均阵亡 {ruleEnemyDeaths:F2}\n" +
                                  $"  ⇒ 胜率挪 {shift:P0}");
    }

    /// <summary>两组摆法跑一遍 M4 那 20 粒固定种子（抖动开着），返回我方胜率与双方平均阵亡。</summary>
    private static double WinRate(Formation player, Formation enemy, out double ourDeaths, out double enemyDeaths)
    {
        var matches = PlacementComparison.Seeds
            .Select(seed => PlacementComparison.RunMatch(seed, player, enemy))
            .ToList();
        ourDeaths = matches.Average(m => (double)m.OurDeaths);
        enemyDeaths = matches.Average(m => (double)m.EnemyDeaths);
        return matches.Count(m => m.Outcome == BattleOutcome.PlayerVictory) / (double)matches.Count;
    }

    /// <summary>从测试产物目录往上找到仓库根（认 <c>project.godot</c>），用来定位 <c>Models/</c> 与源码扫描面。</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "project.godot")))
        {
            dir = dir.Parent;
        }
        dir.ShouldNotBeNull("从测试目录往上找不着 project.godot —— 这条测试的相对路径口径失效了");
        return dir!.FullName;
    }
}
