#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DemoFactory.GugaFramework.Abilities;

namespace DemoFactory.Prototypes.BattleArray.Logic;

/// <summary>
/// H3 宿主 —— 把 <c>src/GugaFramework/Abilities/</c> 那套技能层贴到排兵布阵的真世界上（只贴一条技能）。
///
/// 这一层存在的唯一命题是清单页 P4 / 单挑场那页 H3 那两行：
/// <b>技能进了真演化，改不改得动胜负</b>；以及<b>级 1（离线）与级 2（引擎里）跑同一条剧本时，
/// 快照序列逐字相等</b>（D8）。所以这里刻意做得薄：一张表、一条技能、三个口子。
///
/// 三个口子（谁写谁读，只有一份真值）：
/// - <b>血</b>：世界是唯一出处。每步先把世界的血抄进技能栏，让层自己结算，
///   再把层结算出来的血差分写回世界 —— 写回只发生在 <see cref="AdvanceStep"/> 这一处。
/// - <b>位置</b>：<b>不进层</b>（单位身上那根坐标已从公共层拿掉）。层要距离时问
///   <c>DistanceOf</c>，那条谓词现算世界的深度轴差；位移落位则从 <c>UnitMoved</c> 事件读目的地。
/// - <b>倍率</b>：层算完 S2 后派生出的 <c>damage_taken</c> / <c>move_speed</c> 由世界在结算平A与位移时来读
///   （<see cref="DamageTakenScale"/> / <see cref="SpeedScale"/>）；没挂技能栏的兵一律按 1.0。
/// - <b>技能入口</b>：<see cref="TryActivate"/> 直通 <c>AbilityStepper.TryActivate</c>，
///   剧本注入与（将来）自动战斗走同一个入口（AGENTS 「桥只转发」）。
///
/// 三条已知的边界，都写在这儿而不是藏在代码里：
/// 1. 距离取的是世界的深度轴 Y（<c>|ΔY|</c>）—— 单挑这一局两个兵同列（X 都是 0），
///    接近只发生在 Y 上，所以它就是直线距离。若哪天让两个兵错列对打，这条要重新算。
/// 2. <c>move_speed</c> 在这里是<b>倍率</b>口径（世界拿它乘 <see cref="BattleUnit.Speed"/>），
///    与清单页里「米/秒」那条不是一回事。本版挂的这条技能不改移速，所以不冲突；
///    减速/位移类技能进 H3 时这一格要跟 S5 归属一起重新确认（单挑场那页 12 节待确认项 5）。
/// 3. 技能伤害<b>不进</b>平A那本账（<c>dealt=</c> / <c>AttacksMade</c> 只数平A）——
///    两件事分开记，既有台账才谈得上「全关档逐字等」。技能造成的伤害在层自己的 <c>c;damage_dealt;</c> 行里。
///
/// 全关档：<see cref="BattleWorld.Abilities"/> 留 null，世界一步一息都不碰这一层，
/// 快照与演化一个字节都不变。这是回归校验档，不是偷懒——「接了技能会不会把已验证的机制碰坏」
/// 就靠这一档来答。
/// </summary>
public sealed class BattleAbilities : IAbilityHost
{
    /// <summary>清单页那条突斩的 Id（本层只挂它，名字与清单页严格一致）。</summary>
    public const string RiftAbilityId = "warrior.rift";

    /// <summary>突斩那一刀的效果：伤害走 Calc 链，链的产物是负数＝扣血。</summary>
    public const string RiftEffectId = "fx.rift_slash";

    /// <summary>按施法者已损失生命放大伤害的那条纯函数（注册在原型侧，公共层不认识它）。</summary>
    public const string RiftCalcName = "calc.rift_by_missing_hp";

    /// <summary>突斩的前摇（步）＝ 0.3 秒。清单页给的是「前摇 3 步 → commit → 立即结算」。</summary>
    public const long RiftPreSteps = 3;

    /// <summary>突斩冷却（步）＝ 2 秒（清单页「冷却 2 秒」按 0.1 秒一步换算）。</summary>

    public const long RiftCooldownSteps = 20;

    /// <summary>已损失生命的放大系数：残血时这一刀最多打到 1＋1.5 ＝ 2.5 倍。</summary>
    public const double RiftMissingHpRatio = 1.5;

    // === 第二批落在 H3 的这两格（用户确认：slam 与 charge 的台账在真世界那一侧）===

    /// <summary>踏地：前摇 2 步 → commit → 「距离 ≤ 3 米且在正前 120°」那一发形状查询（规则在公共层）。</summary>
    public const string SlamAbilityId = "tank.slam";

    /// <summary>踏地命中的落点效果：3 秒（30 步）把目标移速倍率乘成 0.6，并带打断 <c>state.guard</c> 的取消列。</summary>
    public const string SlamSlowEffectId = "fx.slam_slow";
    public const long SlamPreSteps = 2;
    public const long SlamSlowSteps = 30;
    public const double SlamSlowMult = 0.6;
    public const double SlamRadiusMeters = 3;
    public const double SlamConeDegrees = 120;

    /// <summary>冲锋：登记 <c>Pending.Move{From, To}</c>，第 3 步到位、到位那一步一并结算伤害（N-3 的正主）。</summary>
    public const string ChargeAbilityId = "rider.charge";

    public const string ChargeEffectId = "fx.charge_impact";

    /// <summary>冲锋那一撞的打底纯函数：只取施法者攻击力，曲线那一折由公共层在 commit 乘上去。</summary>
    public const string ChargeCalcName = "calc.charge_rams";

    public const long ChargeArriveSteps = 3;

    /// <summary>冲锋的伤害按<b>已行走距离</b>查曲线：0 米 ×0.5、4 米 ×1.0、8 米以上封顶 ×1.0。</summary>
    public static readonly double[] ChargeTravelledCurve = { 0.0, 0.5, 4.0, 1.0, 8.0, 1.0 };

    // === P4 后半（第八轮）「每兵种一条」补的那两条：坦克的盾墙、弓手的穿刺箭 ===
    // 都不进默认突斩局（谁要谁 Grant，同 slam/charge 那一档口径），所以级 1 与级 2 的逐字基线一个字不动。

    /// <summary>盾墙：5 秒（50 步）在身上，两笔 modifier（承伤 Override 0.5、移速 Mult 0.7）+ 授予 <see cref="GuardTagId"/>。</summary>
    public const string GuardAbilityId = "tank.guard";

    /// <summary>盾墙那条持续效果（画面上的「持续 cue」载体：挂上就亮、撤下就灭）。</summary>
    public const string GuardWallEffectId = "fx.guard_wall";
    public const long GuardSteps = 50;
    public const double GuardDamageTakenOverride = 0.5;
    public const double GuardSpeedMult = 0.7;

    /// <summary>盾墙的入口激活检查（清单页那条 <c>BlockedTags = status.silence</c>；本原型没人授予沉默，这一格摆的是形状）。</summary>
    public const string SilenceGateTag = "status.silence";

    /// <summary>穿刺箭：前摇 2 步 → commit → 登记「6 步后落地」的待决 —— 箭在路上这段时间就是画面上那条待决。</summary>
    public const string PierceAbilityId = "archer.pierce";

    public const string PierceEffectId = "fx.pierce_arrow";
    public const long PiercePreSteps = 2;
    public const long PierceFlightSteps = 6;
    public const long PierceCooldownSteps = 25;

    /// <summary>穿刺箭的衰减曲线按<b>落地那一格的距离</b>折（16 条清单：0 米 ×1.0、6 米 ×0.8、12 米 ×0.5）。</summary>
    public static readonly double[] PierceDistanceCurve = { 0.0, 1.0, 6.0, 0.8, 12.0, 0.5 };
    public const string PierceCalcName = "calc.pierce_draw";

    /// <summary>
    /// 「在放中」这一档的 tag 名（清单页 <c>tank.guard</c> 授予的那条，<see cref="SlamSlowEffectId"/>
    /// 的 <c>CancelMatchingTags</c> 点的就是它）。名字只留这一处：表里用它、检查清单的壳也用它。
    /// </summary>
    public const string GuardTagId = "state.guard";

    /// <summary>
    /// <b>检查清单（P3「一份清单三处跑」）H3 那一块壳要摆的形状所用的探针件</b>。
    ///
    /// 这一批<b>不占清单页那 16 条技能名额</b>，也不是一份内容：它们只为把清单里现成三条摆不出的
    /// 那几格<b>形状</b>摆出来——带消耗的施法（F1）、在放中授予 tag 的能力段（F2/F3/F5/D5）、
    /// 一次激活登记两条待决（F7）、入口被 tag 挡住（F2/D2）、时长≠周期且可叠层的持续伤害（F4）、
    /// 只授予某个 tag 的效果（F5）、只授予沉默 tag 的效果（F2）。
    /// 摆得出的形状一律复用现成件：延迟结算读源属性那一格用 <see cref="ChargeAbilityId"/>、
    /// 显式打断那一格用 <see cref="SlamSlowEffectId"/>（它自带 <c>CancelMatchingTags = state.guard</c>）、
    /// 两笔 Override 撞界那一格用 <c>damage_taken</c> 现成的界 [0.2, 1]。
    ///
    /// <b>默认突斩局与全关档一个字节都不许多出 <c>a;</c> 行</b>：构造时不给任何兵装它们，
    /// 只有 <c>Tests/AbilityChecklistH3Host.cs</c> 那个壳在自己的世界里 <see cref="Grant"/>；
    /// 数字与 <see cref="AbilityDef"/> 的字段同处一地，是为了让壳那边手算的台账漂了就当场红。
    /// </summary>
    public static class ChecklistProbe
    {
        /// <summary>F1/D3 的载体：有前摇 + 有消耗 + 有冷却 + 立即结算（现成三条都没有消耗，这一格非摆不可）。</summary>
        public const string WindupAbilityId = "ck.windup";

        /// <summary>F2/F3/F5/D5 的被打断方：前摇 2 步、在放中授予 <see cref="GuardTagId"/>。</summary>
        public const string ChanneledAbilityId = "ck.channeled";

        /// <summary>F7 的载体：一次激活登记两条待决、各走各的到期步。</summary>
        public const string TwoPendingAbilityId = "ck.two_pending";

        /// <summary>F2/D2 的「入口被 tag 挡住」那一格：<c>BlockedTags = ck.silence</c>。</summary>
        public const string SilencedAbilityId = "ck.silenced";

        public const string WindupHitEffectId = "ck.windup_hit";

        public const string ChanneledHitEffectId = "ck.channeled_hit";

        public const string PendingFirstEffectId = "ck.pending_first";

        public const string PendingSecondEffectId = "ck.pending_second";

        public const string SilencedHitEffectId = "ck.silenced_hit";

        /// <summary>F4/D2 第三问的持续伤害：时长 60 步、周期 5 步一跳 4 血、可叠三层每层多 2 血。</summary>
        public const string WoundEffectId = "ck.wound";

        /// <summary>F5 的「效果段那一层」：只授予 <see cref="GuardTagId"/>，与在放中的能力段合到同一张计数表。</summary>
        public const string TagGrantEffectId = "ck.tag_grant";

        /// <summary>F2/D2 用它摆出「被挡住」：只授予 <see cref="SilenceTag"/>，常驻（Infinite）。</summary>
        public const string SilenceProbeEffectId = "ck.silence_probe";

        /// <summary>探针沉默 tag（与清单页 <c>status.silence</c> 同格，名字另起是为了不冒充内容）。</summary>
        public const string SilenceTag = "ck.silence";

        /// <summary>探针资源属性：<see cref="WindupAbilityId"/> 扣它。层里本来没有这一条，由壳经词表 <c>SetBase</c> 拧出来。</summary>
        public const string RageAttribute = "ck.rage";

        /// <summary>壳开局把那笔资源拧成几（F1 读它）。</summary>
        public const double RageStart = 100;

        public const long WindupPreSteps = 3;

        public const long WindupCooldownSteps = 20;

        public const double WindupCost = 30;

        public const double WindupDamage = 20;

        public const long ChanneledPreSteps = 2;

        public const double ChanneledDamage = 15;

        public const long PendingFirstDelaySteps = 3;

        public const double PendingFirstDamage = 3;

        public const long PendingSecondDelaySteps = 7;

        public const double PendingSecondDamage = 5;

        public const double SilencedDamage = 6;

        public const long WoundDurationSteps = 60;

        public const long WoundPeriodSteps = 5;

        public const double WoundDamagePerTick = 4;

        public const int WoundMaxStacks = 3;

        /// <summary>叠一层的加值（负数＝更狠）：与清单页 <c>warrior.bleed</c> 的「每层 +2 血」同口径。</summary>
        public const double WoundDamagePerStack = -2;

        /// <summary><see cref="TagGrantEffectId"/> 挂几步：要盖过清单里 tag 计数那一格的全部步数。</summary>
        public const long TagGrantDurationSteps = 80;
    }

    /// <summary>
    /// 探针件的射程：给满战场宽度（与 <see cref="RiftAbilityId"/>、<see cref="ChargeAbilityId"/> 同一表达式）。
    /// 清单问的是形状，不是够不够得着；而这一局为了不让平A 插手，把两个兵沿深度轴拉到了最后排
    /// （<c>|ΔY| = 22 米</c>，见 <c>Tests/AbilityChecklistH3Host.cs</c> 那句几何），短射程会让这一格问的变成「放不出来」。
    /// </summary>
    private const double ChecklistRangeMeters =
        BattleContract.ArenaHalfDepth * 2 + BattleContract.FrontRowDepth * 2;

    /// <summary>层的属性名 ↔ 世界的那个兵；顺序 = 世界的出场顺序（同一步遍历次序是确定性的一部分）。</summary>
    private readonly List<BattleUnit> _owned = [];

    /// <summary>这一局被给了技能的那几个兵（其余兵只有空技能栏 —— 见下面构造那句）。</summary>
    private readonly List<string> _casters = [];
    private readonly Dictionary<string, AbilitySet> _setByUnitId = new(StringComparer.Ordinal);
    private readonly BattleWorld _world;
    private readonly AbilityWorld _sets = new();
    private readonly AbilityStepper _stepper;

    /// <summary>那张表自己也留一份：头顶施法读条要问「这条技能的前摇总共几步」，只问表、不另写常量。</summary>
    private readonly AbilityTables _tables;
    private readonly Random _rng;

    /// <param name="casterUnitIds">被给技能的那些兵。<b>全场每个兵都会分到一个空技能栏</b>：
    /// 目标身上得有栏位，激活检查与结算才查得到人 —— H3 实测撞出来的第一条，
    /// 已记进本轮的新账（技能栏的准入面）。</param>
    public BattleAbilities(BattleWorld world, IEnumerable<string> casterUnitIds, int seed)
    {
        _world = world;
        _rng = new Random(seed);   // 层的随机源与世界的入场抖动各走各的流：谁都不许动对方的抽取次数
        _casters.AddRange(casterUnitIds);
        var tables = BuildTables();
        var calcs = BuildCalcs();
        _tables = tables;

        foreach (var unit in world.Units)
        {
            _owned.Add(unit);
            var set = _sets.AddUnit(unit.Id, AttributesOf(unit));
            _setByUnitId[unit.Id] = set;
            // 上下界：承伤倍率挡在 [0.2, 1]（盾墙那条 clamp 的口径），移速倍率不封顶。
            _sets.SetLimits(unit.Id, "damage_taken", 0.2, 1.0);
            _sets.SetLimits(unit.Id, "move_speed", 0.2, 10.0);
            if (!_casters.Contains(unit.Id)) continue;

            // 冷却走属性口（N-7 已定）：每条技能一条「cooldown.技能Id」，倍率默认 1.0。
            set.Base[CooldownAttribute(RiftAbilityId)] = 1.0;
            _sets.SetLimits(unit.Id, CooldownAttribute(RiftAbilityId), 0.0, 10.0);

            // 充能那格不给（MaxCharges=0）：突斩是冷却技能，不是「一局一发」。
            // 这一格曾经被 record 的缺省值 1 默默成了一次性技能，P2 的排队补放才把它撞出来。
            set.Abilities[RiftAbilityId] = new AbilityOnBody { Id = RiftAbilityId };
        }

        // 事件流容量由原型给，公共层不替你定这一格（AbilityEventLog 没配容量直接抛）。
        _stepper = new AbilityStepper(_sets, this, calcs, tables, LayerConfig);

        // 距离由宿主给：层里不再有坐标（那一格已从公共层拿掉），这一条把世界的深度轴差递进去。
        // 与抬头边界 1 同一件事——层不问引擎、只收标量，问法只有这一条。
        _stepper.DistanceOf = (fromId, toId) => Math.Abs(DepthOf(fromId) - DepthOf(toId));
    }

    /// <summary>
    /// 最常用的那一步：给指定那个兵挂上突斩，并把这一层接进世界。
    /// 三个消费者（离线测试、CLI 取证、引擎里的活局）都走这一发——省得长出三份装配。
    /// </summary>
    public static BattleAbilities AttachRift(BattleWorld world, string unitId)
    {
        if (world.Find(unitId) is null)
            throw new ArgumentException($"查无此兵：{unitId}（兵号形如 p01/e07）");
        var layer = new BattleAbilities(world, new[] { unitId }, seed: world.Seed + 7);
        world.Abilities = layer;
        return layer;
    }

    /// <summary>
    /// <b>每兵种一条</b>（P4 那行「排兵布阵 1v1 当 H3 接上，四兵种各一条技能」）：
    /// 场上每个兵按它的兵种槽位领一条，谁都不许多领。
    ///
    /// 走的是 <see cref="Grant"/> 而不是构造参数：构造那一条装的是突斩，而级 1 与级 2 的逐字基线
    /// 吃的就是「只有一只突斩栏位」那一局（多出一行 <c>a;</c> 就要重录一次基线，已删的设计档那笔税）。
    /// 这一发只在<b>显式开了技能档</b>的真玩法局里用（<c>BATTLE_ARRAY_ABILITIES</c>），
    /// 默认关着 ⇒ 全关档旧账与摆位/配比对照 60 场一个字都不必重跑。
    /// </summary>
    public static BattleAbilities AttachPerTroop(BattleWorld world, int? seed = null)
    {
        var layer = new BattleAbilities(world, Array.Empty<string>(), seed ?? world.Seed + 7);
        foreach (var unit in world.Units)
        {
            layer.Grant(unit.Id, AbilityOfTroopKind(unit.Kind));
        }

        world.Abilities = layer;
        return layer;
    }

    /// <summary>
    /// 兵种槽位 → 那一条技能。<b>这是一张表，不是一串 if</b>：加第五个兵种只动这张表
    /// （与 M5 那条「兵种差异只在数据」同口径，表现层搜不到 <c>if (kind == Archer)</c>）。
    /// </summary>
    public static string AbilityOfTroopKind(TroopKind kind) => kind switch
    {
        TroopKind.Tank => GuardAbilityId,
        TroopKind.Warrior => RiftAbilityId,
        TroopKind.Archer => PierceAbilityId,
        TroopKind.Rider => ChargeAbilityId,
        _ => throw new ArgumentException($"兵种 {kind} 还没领到它那一条技能（表里没这一档）"),
    };

    /// <summary>突斩那条冷却属性的名字（N-7：冷却时长住在属性表里，不给技能开特例列）。</summary>
    public static string CooldownAttribute(string abilityId) => CooldownAttributePrefix + abilityId;

    /// <summary>接层那份配置的属性名与 tag 名（本原型的词表，公共层那七列没有缺省）。</summary>
    public const string CooldownAttributePrefix = "cooldown.";

    /// <summary>
    /// 接层那份配置用到的词表，全在本类里——公共层那七列一个缺省都没有，词表归原型。
    /// 这七列与本文件其余各处的写法逐字相同，所以接不接这份配置，行为一字不变。
    /// </summary>
    private static readonly AbilityLayerConfig LayerConfig = new()
    {
        HealthAttribute = "hp",
        DamageTakenAttribute = "damage_taken",
        CooldownAttributePrefix = CooldownAttributePrefix,
        RootedTag = "status.rooted",
        DamageBlockTag = "status.block_ready",
        DamageTakenEvent = DamageTakenEvent,
        EffectAppliedEvent = EffectAppliedEvent,
        EventCapacity = 2000,
    };

    /// <summary>
    /// 事件触发的两把订阅键（ticket 10 收进公共层配置的那两列）。本原型的战斗世界目前没有
    /// 事件触发型被动，这两格只把公共层那七列填满；它与承伤属性名同字面量纯属本原型选了同一个词，
    /// 两件事各自给各自的。
    /// </summary>
    public const string DamageTakenEvent = "damage_taken";

    public const string EffectAppliedEvent = "effect_applied";

    // === 世界每步要问的三个口子 ===

    /// <summary>
    /// 每步一次：<b>抄进来 → 层自己演化 → 差分写回</b>。世界在自己那五段之前调它（阶段 0），
    /// 于是技能造成的死亡在同一步的阵亡播报里就说得清。
    /// </summary>
    public void AdvanceStep()
    {
        foreach (var unit in _owned)
        {
            var set = _setByUnitId[unit.Id];
            set.Base["hp"] = unit.Health;                     // 血的真值在世界，读进来
            set.Base["hp_max"] = unit.MaxHealth;
            // 位置不进层了（那一格已从公共层拿掉）：层要距离时问 DistanceOf，那一条现算世界的深度轴。
        }

        var totalBefore = _sets.Events.Total;
        _stepper.AdvanceStep();

        // 「谁打的」读层自己发的 DamageDealt 形状（一处真值，世界不再算第二遍、也不再解析文本）。
        // 同一目标本步挨好几种来源时记第一个，与世界的平A口径一致（「死于谁手」只认头一位）。
        var dealer = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var ev in _sets.Events.Tail((int)(_sets.Events.Total - totalBefore)))
        {
            // 踏地 commit 那一刻：形状查询由宿主在这一发补记（规则仍在公共层那把尺里）。
            if (ev.Payload is AbilityCommitted commit && commit.AbilityId == SlamAbilityId)
            {
                ApplySlamShape(ev.Subject);
                continue;
            }

            // 位移落位（rider.charge）：层只把宿主按下那一刻递进来的两个端点发成事件，
            // 世界坐标的写在这里（单挑场 12 节 待确认项 5 已确认）。写的是深度轴。
            // 走的是寻路层那个落位移的写口（工单 08 裁决一口）：位置真值住在流水线，
            // 世界这一列只是每步同步一次的镜像 —— 直接改它，下一步的同步就把这一笔抹掉。
            if (ev.Payload is UnitMoved moved && _setByUnitId.ContainsKey(ev.Subject))
            {
                var body = _world.Find(ev.Subject);
                if (body is not null) _world.PlaceDepthAxis(body, moved.ToX);
                continue;
            }

            if (ev.Payload is DamageDealt damage && !dealer.ContainsKey(ev.Subject))
                dealer[ev.Subject] = damage.Source;
        }

        foreach (var unit in _owned)
        {
            var after = (int)Math.Max(0, Math.Round(_setByUnitId[unit.Id].Base["hp"], MidpointRounding.AwayFromZero));
            var absorbed = Math.Min(unit.Health - after, unit.Health);
            if (absorbed <= 0) continue;
            unit.Health -= absorbed;
            if (unit.Health <= 0 && unit.KilledById is null)
                unit.KilledById = dealer.TryGetValue(unit.Id, out var by) ? by : "技能";
            _world.RaiseAbilityAttack(unit.Id, dealer.TryGetValue(unit.Id, out var source) ? source : "-", absorbed);
        }
    }

    /// <summary>平A 打在这个兵身上要乘的承伤倍率（没挂技能栏的兵 = 1.0，界已由 S2 守过）。</summary>
    public double DamageTakenScale(string unitId)
        => _setByUnitId.TryGetValue(unitId, out var set) && set.Current.TryGetValue("damage_taken", out var scale)
            ? scale
            : 1.0;

    /// <summary>
    /// 这个兵此刻的深度轴坐标（<c>Position.Y</c>）：<b>距离那把尺的原料</b>。
    /// 层里不再有坐标，所以 <c>DistanceOf</c> 现算时读这一格；查无此兵按 0 算（那一格不会走到）。
    /// </summary>
    private double DepthOf(string unitId) => _world.Find(unitId)?.Position.Y ?? 0.0;

    /// <summary>这个兵这一步的移速倍率（同上；本版挂的技能不改它，1.0 时世界的位移算术一字不变）。</summary>
    public double SpeedScale(string unitId)
        => _setByUnitId.TryGetValue(unitId, out var set) && set.Current.TryGetValue("move_speed", out var scale)
            ? scale
            : 1.0;

    /// <summary>
    /// 移动激活检查 —— 定身挂着就走不动（<b>寻路层流水线上那唯一一道位移裁决</b>，工单 08 接上）。
    ///
    /// <para>转发公共层那一份 <c>AbilityStepper.CanMove</c>（<c>AbilityStepper.cs:751</c>），
    /// 原型不另判一次「有没有定身」：判两遍就会漂，而漂了之后症状是
    /// 「有时定身没挡住」，那是查不出出处的一档。</para>
    ///
    /// <para><b>调用顺序是契约</b>（ADR 0008、「桥只转发」）：激活检查排在寻路求解<b>之前</b>，
    /// 所以它看到的是<b>标称步长</b>（移速 × 0.1 秒），不是「沿路径还剩多远」——
    /// 那要等求解之后才知道，而排在求解之前正是契约。
    /// 「先问允不允许动，再算站哪」这一条由 <c>BattleNavMove.BuildGate</c> 递进 <c>NavStepper</c>。</para>
    ///
    /// <para>激活检查拒了不是静默跳过：移动解交出 <c>HeldByActivationCheck</c> 与那句说法，
    /// 「它为什么不动」在快照上读得出原因（「事件发射前置」的静默拒绝教训）。</para>
    /// </summary>
    public bool CanMove(string unitId) => _stepper.CanMove(unitId);

    /// <summary>放一条技能：剧本注入、桥动词、（将来）自动战斗都从这一发进，同一条路。</summary>
    public bool TryActivate(string unitId, string abilityId, string? targetId)
        => _stepper.TryActivate(unitId, abilityId, TargetOf(abilityId, targetId));

    /// <summary>带位移意图的那一发（<c>rider.charge</c>）：起点与目的地都是<b>深度轴坐标</b>，宿主自己算好递进来。</summary>
    public bool TryActivate(string unitId, string abilityId, string? targetId, (double FromX, double ToX)? move)
        => _stepper.TryActivate(unitId, abilityId, TargetOf(abilityId, targetId), move);

    /// <summary>
    /// 自增益那一格的目标口径<b>收在这一发</b>：这条技能挂谁身上由表说了算，不由调用方说了算。
    ///
    /// 为什么要在这里挡：本轮第一次装配就撞出来了 ——「<c>CastAbility|p01|tank.guard|e01</c>」
    /// 递进去之后，盾墙那两笔 modifier 落到了<b>敌人</b>身上（公共层的 <c>TargetFor</c> 照目标施加）。
    /// 自动战斗与剧本注入两条路各判各的「这条是不是给自己上的」，就是 N-24 那一族——同一份数据
    /// 两条通路给出两种结果，只是这次漂在靶子那一格。所以：<b>带了目标而这条是自增益 = 当场抛</b>，
    /// 与词表「给了目的地却没地方落地就抛」同一条口径；静默改成自己才是最难查的那种「看起来成功了」。
    /// </summary>
    private static string? TargetOf(string abilityId, string? targetId)
    {
        if (!IsSelfTargeted(abilityId)) return targetId;
        if (targetId is not null)
        {
            throw new ArgumentException(
                $"{abilityId} 是自增益（效果挂在按下的那个兵自己身上），却带了目标 {targetId} —— " +
                "要它给自己上就别填第 3 段；这里不静默改靶，改了就长成「两条路挂到两个人身上」");
        }

        return null;
    }

    /// <summary>显式取消在放的那条（F1  commit 前后两种结果都从这一发走）。</summary>
    public bool TryCancel(string unitId, string abilityId) => _stepper.InjectCancel(unitId, abilityId);

    /// <summary>清冷却（词表里那条 <c>ClearCooldown</c>）。</summary>
    public void ClearCooldown(string unitId, string abilityId) => _stepper.InjectClearCooldown(unitId, abilityId);

    /// <summary>
    /// 绕过技能直接挂效果（控制链与打断最省事的摆法，词表 <c>ApplyEffect</c>）。
    /// <paramref name="sourceId"/> = 「谁打的」，光环与平A 那两条要名字才摆得出台账（H2 同一条口）。
    /// </summary>
    public void ApplyEffect(string targetId, string effectId, long? durationTicks = null, string? sourceId = null)
        => _stepper.InjectEffect(targetId, effectId, durationTicks, sourceId);

    /// <summary>撤效果（词表 <c>RemoveEffect</c>；stacks=0 表示整条撤）。</summary>
    public void RemoveEffect(string targetId, string effectId, int stacks = 0)
        => _stepper.InjectRemoveEffect(targetId, effectId, stacks);

    /// <summary>
    /// 现场改一条属性（词表 <c>SetAttribute</c>：Override 与 clamp 的测试入口，不占技能名额）。
    /// <paramref name="requirementTags"/> = 修饰符自带生效条件（N-5 那一格），不给 = 无条件生效。
    /// </summary>
    public void SetAttribute(string targetId, string attribute, string mode, double magnitude,
        string[]? requirementTags = null)
        => _stepper.InjectModifier(targetId, attribute, Enum.Parse<ModifierMode>(mode, ignoreCase: true),
            magnitude, requirementTags: requirementTags);

    /// <summary>直接把某个基础值拧到某数（分支对照与剧本注入都要这一发）。</summary>
    public void SetBase(string targetId, string attribute, double value) => _stepper.InjectSetBase(targetId, attribute, value);

    /// <summary>把冷却直接倒到第 n 步好（D7 分支对照用）。</summary>
    public void SetCooldown(string unitId, string abilityId, long endsAtTick)
        => _stepper.InjectSetCooldown(unitId, abilityId, endsAtTick);

    /// <summary>蓄力档：外部改写剩余步数（清单页「松手」那一发）。</summary>
    public bool SetStepsRemaining(string unitId, string abilityId, long steps)
        => _stepper.InjectSetStepsRemaining(unitId, abilityId, steps);

    /// <summary>
    /// 给某个兵装上一条技能的栏位（冷却/相位都从 Ready 起）。
    /// <b>为什么要有这一发</b>：构造时装的那一条是突斩，H3 已验证的基线（含级 1 逐字指纹）就是那一只栏位；
    /// 把 slam / charge 也在构造时装上，等于让每一局都多出两行 <c>a;</c> 快照——白交一次「重录基线」的税。
    /// 谁要用谁 grant，全关档与突斩局一字不动。
    /// </summary>
    public void Grant(string unitId, string abilityId)
    {
        var set = _setByUnitId.TryGetValue(unitId, out var s)
            ? s
            : throw new ArgumentException($"这个兵没装技能栏：{unitId}（H3 全场每兵都该有一个，见 N-18）");
        set.Abilities[abilityId] = new AbilityOnBody { Id = abilityId };
    }

    /// <summary>
    /// 踏地的形状查询（N-14 的落法）：<b>规则在公共层那把尺里</b>（<see cref="AbilityStepper.InsideSector"/>），
    /// 原型不另判一遍；二维的坐标与朝向全由宿主递进去（层里一个坐标都不持）。
    /// </summary>
    public List<string> TargetsInSector(string casterId, double radiusMeters, double coneDegrees)
    {
        var caster = _world.Find(casterId) ?? throw new ArgumentException($"查无此兵：{casterId}");
        var facing = FacingOf(caster);
        var hits = new List<string>();
        foreach (var other in _world.Units)
        {
            if (other.Id == casterId || !other.Alive) continue;
            if (AbilityStepper.InsideSector(caster.Position.X, caster.Position.Y,
                    other.Position.X, other.Position.Y, facing.X, facing.Y, radiusMeters, coneDegrees))
            {
                hits.Add(other.Id);
            }
        }

        return hits.OrderBy(id => id, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// commit 那一刻由宿主发的形状查询 + 施加（与光环 N-13 同一种形状：判定在层、跨边界由宿主发动词）。
    /// 补记走层的 <c>RecordShape</c>：事件名与形状（ShapeHitLanded / ShapeHitMissed）都归公共层，
    /// 宿主只递半径、全角与命中名单这几个标量；空集也念得出——「打空要读得出为什么空」。
    /// </summary>
    private void ApplySlamShape(string casterId)
    {
        var hits = TargetsInSector(casterId, SlamRadiusMeters, SlamConeDegrees);
        foreach (var targetId in hits)
        {
            _stepper.InjectEffect(targetId, SlamSlowEffectId, sourceId: casterId);
        }

        _stepper.RecordShape(casterId, SlamRadiusMeters, SlamConeDegrees, hits);
    }

    /// <summary>朝向由方别定：我方在深度轴正侧、面向敌方（−Y），敌方反之。这一局没有「转身」这件事。</summary>
    private static (double X, double Y) FacingOf(BattleUnit unit)
        => unit.Side == Side.Player ? (0, -1) : (0, 1);

    /// <summary>
    /// 自动战斗的意图口：会自己放这一条的兵，每步 S4 问一发（与手动注入同一条 <see cref="TryActivate"/>）。
    /// 缺省关——H3 的台账靠剧本喂，「玩家按下」与「自己冒出来」两条路都要有口，但不许同时开。
    /// </summary>
    public bool AutoCast
    {
        get => _autoCast;
        set
        {
            _autoCast = value;
            _stepper.IntentPolicy = value ? AskAutoCast : (Func<string, (string? AbilityId, string? TargetId)>?)null;
        }
    }

    private bool _autoCast;

    private (string? AbilityId, string? TargetId) AskAutoCast(string unitId)
    {
        if (_world.Find(unitId) is not { } unit) return (null, null);
        var target = _world.Units.FirstOrDefault(u => u.Side != unit.Side && u.Alive);
        if (target is null || Find(unitId) is not { } set) return (null, null);

        // 先问哪一条 = 按栏位名的序取第一条「不在前摇也不在冷却」的（遍历次序是确定性的一部分）。
        // 这里只回答「先问哪条」，不放行 —— 射程 / tag / 消耗那三处激活检查仍在 S4 判，实现只有一份。
        // 身上只装突斩的那一局（级 1 与级 2 的基线局）挑出来还是突斩，一字不差。
        foreach (var abilityId in set.Abilities.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (set.Abilities[abilityId].Phase != AbilityPhase.Ready) continue;
            // 自增益那条不带目标（带了就把盾墙挂到敌人身上），其余照旧打最近的活人。
            return (abilityId, IsSelfTargeted(abilityId) ? null : target.Id);
        }

        return (null, null);
    }

    /// <summary>层的全量状态（技能那一半快照：技能栏 / 修饰符 / 效果 / 技能档 / 待决结算 / 历史计数）。</summary>
    public string Snapshot() => _stepper.TakeSnapshot();

    // === 行为流读数口（全部转发公共层的 AbilityEventLog，原型不另存一份账、不再按字符串匹配） ===
    // 有界账的形状跟着事件形状走：按形状计数 / 过滤用泛型转发，尾窗按累计条数配 Tail 增量取。

    /// <summary>行为流里某一类事件留着几条（形状即名单，没有 kind 字符串与文本匹配那两样）。</summary>
    public int Count<T>() where T : EventPayload => _sets.Events.Count<T>();

    /// <summary>行为流里某一类事件的全部留窗条目（只读；检查清单问「这一格留了几行」就取它）。</summary>
    public IEnumerable<T> EventsWhere<T>() where T : EventPayload => _sets.Events.Where<T>();

    /// <summary>行为流累计发出的条数（含被环形挤掉的；表现件游标照它走，换一局面归零、游标跟着重取）。</summary>
    public long EventTotal => _sets.Events.Total;

    /// <summary>被环形挤掉的条数：「尾窗里没有」≠「没发生」，读数先看这一格。</summary>
    public long EventsDropped => _sets.Events.Dropped;

    /// <summary>最近 n 条事件（只读；表现件每步拿 EventTotal 的增量取新行，不重演旧行）。</summary>
    public IReadOnlyList<AbilityEvent> EventsTail(int n) => _sets.Events.Tail(n);

    /// <summary>最近 n 条的行文本（AbilityEvent.Line；桥动词 ability_log 的 lines 用它）。</summary>
    public IReadOnlyList<string> EventTailLines(int n) => _sets.Events.Tail(n).Select(e => e.Line).ToList();

    /// <summary>行为流尾窗的可读出口（带头一行 dropped= / total= 读数，正文逐条 Line）。</summary>
    public string EventTailText(int n) => _sets.Events.TailText(n);

    // === IAbilityHost（世界向层供的三样：单位查询口 / 存活判定 / 随机源） ===

    public AbilitySet? Find(string unitId) => _sets.Units.TryGetValue(unitId, out var set) ? set : null;

    /// <summary>
    /// 这个兵此刻的 <b>tag 计数</b>（S2 那份派生值，读它不重算）——
    /// cue 里「动态/计数」那一档的读数源。<b>只读</b>：表现件拿到的是同一份派生值，不许再算一遍。
    /// </summary>
    public IReadOnlyDictionary<string, int> TagsOf(string unitId)
        => Find(unitId)?.TagCounts ?? EmptyTags;

    /// <summary>
    /// 这个兵身上每条<b>效果</b>此刻的层数（同 Id 多实例时求和 —— 画面上「挂上了没」问的是这个）。
    /// 没挂号的效果照样交出去，挂不挂由 <see cref="SkillCueSpec"/> 那张说明书判，宿主不判。
    /// </summary>
    public IReadOnlyDictionary<string, int> EffectStacksOf(string unitId)
    {
        if (Find(unitId) is not { } set) return EmptyStacks;
        var stacks = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var effect in set.Effects)
        {
            stacks[effect.EffectId] = stacks.TryGetValue(effect.EffectId, out var n)
                ? n + effect.Stacks
                : effect.Stacks;
        }

        return stacks;
    }

    private static readonly IReadOnlyDictionary<string, int> EmptyTags =
        new Dictionary<string, int>(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, int> EmptyStacks =
        new Dictionary<string, int>(StringComparer.Ordinal);

    public bool IsAlive(string unitId)
    {
        var unit = _world.Find(unitId);
        if (unit is null) return false;
        return unit.Alive && _sets.Units.TryGetValue(unitId, out var set) && set.Current["hp"] > 0;
    }

    /// <summary>这一侧的目标册只装兵：在册就是在册（死活仍归 <see cref="IsAlive"/>）。</summary>
    public bool Exists(string entityId) => _sets.Units.ContainsKey(entityId);

    public IReadOnlyList<string> UnitIds => _owned.Select(u => u.Id).ToList();

    /// <summary>层的随机源：与世界的入场抖动分开两条流，谁都不改对方的抽取次数（否则台账会整体漂）。</summary>
    public double NextRandom() => _rng.NextDouble();

    /// <summary>
    /// 这个兵<b>正在放</b>的那一条（前摇中）—— 头顶施法读条只读这一发，表现层不自判相位。
    /// 返回 null = 没在读（Ready / Cooling / 已 commit）。前摇总步数取自技能表那一份，不在表现层另写。
    /// </summary>
    public (string AbilityId, long StepsRemaining, long TotalSteps)? CastingOf(string unitId)
    {
        if (Find(unitId) is not { } set) return null;
        foreach (var abilityId in set.Abilities.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var body = set.Abilities[abilityId];
            if (body.Phase != AbilityPhase.Pre) continue;
            return (abilityId, body.StepsRemaining, Math.Max(1, _tables.Ability(abilityId).PreSteps));
        }

        return null;
    }

    // === 那张表：只有清单页的 warrior.rift ===

    private static IEnumerable<(string Name, double Value)> AttributesOf(BattleUnit unit) => new (string, double)[]
    {
        ("hp", unit.Health),
        ("hp_max", unit.MaxHealth),
        ("attack", unit.Card.Attack),
        ("damage_taken", 1.0),
        ("move_speed", 1.0),
    };

    /// <summary>
    /// 技能体检单（P5）读真表的那张口：把这张表原样交出去，<b>只读、不起世界、不装配</b>。
    /// 为什么要开这个口子而不是让工具自己写一份数字：报告是表的**可读投影**，
    /// 抄第二份数字就有了「报告说 3 步、表里住 2 步」这种没人当场发现的漂 ——
    /// A76（Tests/AbilitySkillReviewTests.cs）拿这份投影逐格比回来。
    /// </summary>
    public static AbilityTables TablesForReview() => BuildTables();

    private static AbilityTables BuildTables()
    {
        var tables = new AbilityTables();
        tables.Register(new EffectDef(
            RiftEffectId,
            EffectKind.Instant,
            new[] { new ModifierSpec("hp", ModifierMode.Add, 0, CalcFromEffectValue: true) },
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            new[] { RiftCalcName },
            new Dictionary<string, double> { ["ratio"] = RiftMissingHpRatio },
            CalcSeed: 0,
            PeriodTicks: 0,
            Array.Empty<string>(),
            Array.Empty<(string OnEvent, string EffectId)>()));
        tables.Register(new AbilityDef(
            RiftAbilityId,
            RiftPreSteps,
            RiftCooldownSteps,
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            new[] { RiftEffectId },
            string.Empty,
            0,
            // 射程给满战场宽度：单挑这一局两个人从头到尾都在彼此够得着的距离内，
            // 射程那一格要验的是「够不着就拒」，那是 H1 与单挑场的题，不在 H3 重复。
            RangeMeters: BattleContract.ArenaHalfDepth * 2 + BattleContract.FrontRowDepth * 2));

        // 踏地：技能自己不带效果（候选集要二维判定，由宿主在 commit 那一刻发 ApplyEffect，
        // 与光环 N-13 同一种形状）。入口射程给 6 米＞形状半径 3 米，为的是让「打得着人但打不着这一发」
        // 那一侧读得出来：入口放行、形状拒收，两把尺各问各的。
        tables.Register(EffectDef.OverTime(SlamSlowEffectId, new[]
        {
            new ModifierSpec("move_speed", ModifierMode.Mult, SlamSlowMult),
        }, durationTicks: SlamSlowSteps, cancelMatchingTags: new[] { GuardTagId }));
        tables.Register(new AbilityDef(SlamAbilityId, SlamPreSteps, RiftCooldownSteps,
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<string>(), string.Empty, 0, RangeMeters: 6));

        // 冲锋：位移载荷 + 按已行走距离的曲线（CurveAxis="travelled"，在 commit 折好，落地那一步不再折）。
        tables.Register(new EffectDef(ChargeEffectId, EffectKind.Instant,
            new[] { new ModifierSpec("hp", ModifierMode.Add, 0, CalcFromEffectValue: true) },
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            new[] { ChargeCalcName }, new Dictionary<string, double>(),
            CalcSeed: 0, PeriodTicks: 0, Array.Empty<string>(),
            Array.Empty<(string OnEvent, string EffectId)>())
        {
            DelayTicks = ChargeArriveSteps,
            CurveXY = ChargeTravelledCurve,
            CurveAxis = "travelled",
        });
        tables.Register(new AbilityDef(ChargeAbilityId, 0, RiftCooldownSteps,
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            new[] { ChargeEffectId }, string.Empty, 0,
            RangeMeters: BattleContract.ArenaHalfDepth * 2 + BattleContract.FrontRowDepth * 2));

        // === P4 后半「每兵种一条」补的两条（第八轮）：坦克的盾墙、弓手的穿刺箭 ===
        // 两格都是「16 条清单」原样的形状，不改 record：
        //   tank.guard  = Duration 50 步 + 同段两笔 modifier（Override 撞界、Mult）+ 授予 state.guard（技能段与
        //                 效果段各一层 ⇒ 计数念得出 2，那是 A55 在 H2 摆过的那一格，这里给它一个真世界的载体）；
        //   archer.pierce = 前摇 2 步 → commit → 登记 6 步后落地的待决，伤害按<b>落地那一格的距离</b>查曲线。
        tables.Register(EffectDef.OverTime(GuardWallEffectId, new[]
        {
            new ModifierSpec("damage_taken", ModifierMode.Override, GuardDamageTakenOverride),
            new ModifierSpec("move_speed", ModifierMode.Mult, GuardSpeedMult),
        }, durationTicks: GuardSteps, grantedTags: new[] { GuardTagId },
            blockedTags: new[] { SilenceGateTag }));
        tables.Register(new AbilityDef(GuardAbilityId, 0, 60,
            new[] { GuardTagId }, Array.Empty<string>(), new[] { SilenceGateTag },
            new[] { GuardWallEffectId }, string.Empty, 0,
            RangeMeters: BattleContract.ArenaHalfDepth * 2 + BattleContract.FrontRowDepth * 2));

        tables.Register(new EffectDef(PierceEffectId, EffectKind.Instant,
            new[] { new ModifierSpec("hp", ModifierMode.Add, 0, CalcFromEffectValue: true) },
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            new[] { PierceCalcName }, new Dictionary<string, double>(),
            CalcSeed: 0, PeriodTicks: 0, Array.Empty<string>(),
            Array.Empty<(string OnEvent, string EffectId)>())
        {
            DelayTicks = PierceFlightSteps,
            CurveXY = PierceDistanceCurve,
            CurveAxis = "distance",
        });
        tables.Register(new AbilityDef(PierceAbilityId, PiercePreSteps, PierceCooldownSteps,
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            new[] { PierceEffectId }, string.Empty, 0, RangeMeters: 12));

        // === 检查清单（P3）H3 那一块壳的探针件：不占清单页那 16 条技能名额，只为摆出某一格形状 ===
        // 谁都不许在默认装配里给兵装它们（见 ChecklistProbe 抬头那句「一个字节都不许多出 a; 行」）：
        // 只有 Tests/AbilityChecklistH3Host.cs 那个壳在自己的世界里 Grant。
        var noTags = Array.Empty<string>();
        var noModifiers = Array.Empty<ModifierSpec>();

        // 四刀都是固定值（不走 Calc）：清单手算出来的数是「目标血量差 == 这个数」，
        // 而放大系数那一本账在 AbilityH3DuelTests（残血那一刀）与 A66/A67（曲线），不在这里重复。
        tables.Register(EffectDef.Instant(ChecklistProbe.WindupHitEffectId,
            new ModifierSpec("hp", ModifierMode.Add, -ChecklistProbe.WindupDamage)));
        tables.Register(EffectDef.Instant(ChecklistProbe.ChanneledHitEffectId,
            new ModifierSpec("hp", ModifierMode.Add, -ChecklistProbe.ChanneledDamage)));
        tables.Register(EffectDef.Instant(ChecklistProbe.SilencedHitEffectId,
            new ModifierSpec("hp", ModifierMode.Add, -ChecklistProbe.SilencedDamage)));

        // 一次激活两条待决、各走各的到期步（F7「互不顶掉」；取消技能也不撤已登记的结算那一半同格兑现）
        tables.Register(EffectDef.Delayed(ChecklistProbe.PendingFirstEffectId,
            new ModifierSpec("hp", ModifierMode.Add, -ChecklistProbe.PendingFirstDamage),
            ChecklistProbe.PendingFirstDelaySteps));
        tables.Register(EffectDef.Delayed(ChecklistProbe.PendingSecondEffectId,
            new ModifierSpec("hp", ModifierMode.Add, -ChecklistProbe.PendingSecondDamage),
            ChecklistProbe.PendingSecondDelaySteps));

        // 持续伤害：时长与周期两列各自存在（F4「Duration ≠ period」的正证），另带叠层三档
        tables.Register(EffectDef.OverTime(ChecklistProbe.WoundEffectId, new[]
        {
            new ModifierSpec("hp", ModifierMode.Add, -ChecklistProbe.WoundDamagePerTick),
        }, durationTicks: ChecklistProbe.WoundDurationSteps, periodTicks: ChecklistProbe.WoundPeriodSteps,
            stackKey: ChecklistProbe.WoundEffectId, maxStacks: ChecklistProbe.WoundMaxStacks,
            magnitudePerStack: ChecklistProbe.WoundDamagePerStack));

        // 两条只授予 tag 的探针效果：F5 的「效果段那一层」与 F2/D2 的「被挡住」
        tables.Register(EffectDef.OverTime(ChecklistProbe.TagGrantEffectId, noModifiers,
            durationTicks: ChecklistProbe.TagGrantDurationSteps, grantedTags: new[] { GuardTagId }));
        tables.Register(EffectDef.OverTime(ChecklistProbe.SilenceProbeEffectId, noModifiers,
            durationTicks: 0, kind: EffectKind.Infinite, grantedTags: new[] { ChecklistProbe.SilenceTag }));

        tables.Register(new AbilityDef(ChecklistProbe.WindupAbilityId,
            ChecklistProbe.WindupPreSteps, ChecklistProbe.WindupCooldownSteps,
            noTags, noTags, noTags, new[] { ChecklistProbe.WindupHitEffectId },
            ChecklistProbe.RageAttribute, ChecklistProbe.WindupCost, RangeMeters: ChecklistRangeMeters));
        tables.Register(new AbilityDef(ChecklistProbe.ChanneledAbilityId,
            ChecklistProbe.ChanneledPreSteps, 0,
            new[] { GuardTagId }, noTags, noTags, new[] { ChecklistProbe.ChanneledHitEffectId },
            string.Empty, 0, RangeMeters: ChecklistRangeMeters));
        tables.Register(new AbilityDef(ChecklistProbe.TwoPendingAbilityId, 0, 0,
            noTags, noTags, noTags,
            new[] { ChecklistProbe.PendingFirstEffectId, ChecklistProbe.PendingSecondEffectId },
            string.Empty, 0, RangeMeters: ChecklistRangeMeters));
        tables.Register(new AbilityDef(ChecklistProbe.SilencedAbilityId, 0, 0,
            noTags, noTags, new[] { ChecklistProbe.SilenceTag },
            new[] { ChecklistProbe.SilencedHitEffectId }, string.Empty, 0,
            RangeMeters: ChecklistRangeMeters));

        return tables;
    }

    private static CalcRegistry BuildCalcs() => new CalcRegistry()
        .Register(RiftCalcName, i =>
        {
            var max = CalcInput.Attr(i.Source, "hp_max");
            var hp = CalcInput.Attr(i.Source, "hp");
            var attack = CalcInput.Attr(i.Source, "attack");
            var missingRatio = max > 0 ? 1 + (1 - hp / max) * i.Param("ratio") : 1.0;
            return -(attack * missingRatio + i.Working);   // 负数＝扣血，链的种子是 0，所以这一发就是那一刀的总量
        })
        // 冲锋那一撞：按<b>施法者自己的攻击力</b>打底，曲线那一折（已行走距离）由公共层在 commit 乘上去。
        .Register(ChargeCalcName, i => -(CalcInput.Attr(i.Source, "attack") + i.Working))
        // 穿刺箭那一发：同样只取攻击力打底，折衰减那一折由公共层按<b>落地那一格的距离</b>乘上去
        // （两条技能各用各的注册名 —— 清单页的表行里 Calc 名就是各住各的一格，合用一个名会让台账读不出是谁）
        .Register(PierceCalcName, i => -(CalcInput.Attr(i.Source, "attack") + i.Working));

    /// <summary>
    /// 自增益那一档：<b>目标就是自己</b>（层的 <c>TargetFor</c> 在目标为空时回落到施法者）。
    /// 自动战斗挑技能时要问这一发，不然盾墙会把两笔 modifier 挂到敌人身上去。
    /// 名字走表不走分支，第五个自增益技能只往这个集合里加一行。
    /// </summary>
    private static readonly HashSet<string> SelfTargeted = new(StringComparer.Ordinal)
    {
        GuardAbilityId,
    };

    public static bool IsSelfTargeted(string abilityId) => SelfTargeted.Contains(abilityId);

    /// <summary>把这一层从世界上摘掉（「全关」对照档用它：摘完世界就该逐字回到 M4 那本账）。</summary>
    public void Detach() => _world.Abilities = null;
}
