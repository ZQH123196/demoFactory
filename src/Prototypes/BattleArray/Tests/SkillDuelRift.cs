#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DemoFactory.GugaFramework.Abilities;

namespace BattleArray.Tests;

/// <summary>
/// 单挑场的注册壳（H2 那一份）—— 「16 条清单」里<b>两批合起来那 14 条</b>的表与数值都住在这儿。
///
/// 形状照 <c>Scripts/Logic/DuelRiftSetup.cs</c>（H3 那份）：<b>装配 + 词表 + 快照格式三方共用</b>，
/// 场子（<see cref="SkillTestWorld"/>）不认识任何一条技能，G2 这条的机器口径就在这一对文件的分工上：
/// 加一条技能只改这个文件，<c>SkillTestWorld.cs</c> 一个字都不动。
///
/// 第一批的取法（「16 条清单」P2 那行，用户 2026-09-27 定「分两批各 8 条上」）：
/// <b>每兵种一主动一被动</b>——
/// 坦克 <c>tank.dread</c>（光环按距离 N-13）+ <c>tank.steady</c>（抵挡 issue 08 / 回充 N-15）、
/// 战士 <c>warrior.rift</c>（前摇+commit+Calc，L3 档排队 N-16）+ <c>warrior.rage</c>（事件触发 N-2）、
/// 弓手 <c>archer.pierce</c>（延迟结算 + 曲线 + 目标失效 + 多项 Calc 链 N-12）+ <c>archer.evasive</c>（Override 撞界）、
/// 骑手 <c>rider.trample</c>（定身挡 S5 位移 N-4）+ <c>rider.mounted</c>（modifier 自带条件 N-5）。
///
/// 第二批（2026-09-28 本轮，H2 这侧六条：<c>tank.guard</c> / <c>warrior.execute</c> / <c>warrior.bleed</c>
/// / <c>archer.rain</c> / <c>archer.mark</c> / <c>rider.joust</c>）<b>一律用既有列摆</b>——
/// 已删的设计档那句「形状已定完，第二批不再动列」在这里兑现，三个新格各指着一条台账断言：
/// 谓词走 Calc 注册口（<c>execute</c> 的血线）、落点上限走距离曲线（<c>rain</c> 那三发错峰 + 落空）、
/// tag 进计算走 <see cref="CalcInput.TagCount"/>（<c>mark</c> 的 ×1.2），冷却倍率走 attribute 口（N-7）。
///
/// 两条刻意没上的，写清楚不是漏：
/// - <c>rider.charge</c>（位移）：单挑场那页 11 节「不做清单」把位移类排除在本版之外，位置在这里仍是输入不是输出；
///   用户确认了它的台账落 <b>H3</b>（要动 <c>Pending.Move</c> 那一格，代价见已删的设计档那笔税）；
/// - <c>tank.slam</c>（正前 120°）：直线几何上扇形是退化的（夹角只有 0° 与 180°），
///   所以 N-14 那条在 <see cref="AbilityStepper.InsideSector"/> 那一层钉（界内界外两侧各一个点），
///   它自己的数值台账随第二批落 <b>H3</b>（二维站位在那侧）。
/// </summary>
public static class SkillDuelRift
{
    // === 单位与属性的名字（场子只认键，字面量全在这里）===

    public const string HeroId = "h01";
    public const string DummyId = "d01";

    public static class Attr
    {
        public const string Health = "hp";
        public const string MaxHealth = "hp_max";
        public const string Attack = "attack";
        public const string MoveSpeed = "move_speed";
        public const string DamageTaken = "damage_taken";
        public const string Rage = "rage";

        /// <summary>效果落地的事件订阅键（ticket 10 收进公共层配置的那两列）。</summary>
        public const string EffectApplied = "effect_applied";

        /// <summary>
        /// 冷却走 attribute 口（N-7，已删的设计档 <c>Ability</c> 行）：要把某条技能的冷却拉长，
        /// 就给它挂一条普通 Mult 修饰符。这一列得先有基础值 1.0，不然 <c>(0 + 0) × 1.3 = 0</c>
        /// 会把冷却折成 0 步（<see cref="AbilityStepper"/> 读的是派生值，不读缺省 1.0 那份兜底）。
        /// </summary>
        public const string CooldownGuard = "cooldown.tank.guard";

        /// <summary>
        /// 冷却属性的前缀：层里读的是 <c>前缀 + 技能Id</c>，所以 <see cref="CooldownGuard"/>
        /// 那一条正好是 <c>前缀 + "tank.guard"</c>。前缀归本原型，层里那五列没有缺省。
        /// </summary>
        public const string CooldownPrefix = "cooldown.";
    }

    public static class Tag
    {
        public const string Rooted = "status.rooted";
        public const string Mounted = "status.mounted";
        public const string BlockReady = "status.block_ready";
        public const string Guard = "state.guard";
        public const string Silence = "state.silence";
        public const string Marked = "status.marked";
    }

    public static class Skill
    {
        public const string Dread = "tank.dread";
        public const string Steady = "tank.steady";
        public const string Rift = "warrior.rift";
        public const string Rage = "warrior.rage";
        public const string Pierce = "archer.pierce";
        public const string Evasive = "archer.evasive";
        public const string Trample = "rider.trample";
        public const string Mounted = "rider.mounted";

        // 第二批这六条（「16 条清单」战士/坦克/弓手/骑手那六格）。
        public const string Guard = "tank.guard";
        public const string Execute = "warrior.execute";
        public const string Bleed = "warrior.bleed";
        public const string Rain = "archer.rain";
        public const string Mark = "archer.mark";
        public const string Joust = "rider.joust";
    }

    public static class Fx
    {
        public const string DreadAura = "fx.dread_aura";
        public const string SteadyCharges = "fx.steady_charges";
        public const string RiftSlash = "fx.rift_slash";
        public const string RageOnHit = "fx.rage_on_hit";
        public const string RageGain8 = "fx.rage_gain_8";
        public const string PierceArrow = "fx.pierce_arrow";
        public const string EasiveSpeed = "fx.evasive_speed";
        public const string TrampleRoot = "fx.trample_root";
        public const string MountedTag = "fx.mounted_tag";
        public const string MountedBonus = "fx.mounted_bonus";
        public const string BasicAttack = "fx.basic_attack";

        public const string GuardWall = "fx.guard_wall";
        public const string ExecuteGuillotine = "fx.execute_guillotine";
        public const string BleedWound = "fx.bleed_wound";
        public const string RainNear = "fx.rain_near";
        public const string RainMid = "fx.rain_mid";
        public const string RainFar = "fx.rain_far";
        public const string MarkSeal = "fx.mark_seal";
        public const string JoustFirst = "fx.joust_first";
        public const string JoustSecond = "fx.joust_second";

        /// <summary>激活检查探针：只用来给一次施法挂上 <c>state.silence</c>，不占 16 条技能的名额。</summary>
        public const string SilenceProbe = "fx.silence_probe";
    }

    /// <summary>
    /// <b>清单探针件</b>：16 条检查清单（<c>AbilityChecklistTests</c>）在 H2 这一侧要问、
    /// 而现成 16 条技能摆不出的那几格形状。<b>不占 16 条技能名额，只为摆出某一格形状</b>
    /// （先例是 <see cref="Fx.SilenceProbe"/> 那一句），也不进 A31~A63 那本数值台账、游戏里不装。
    /// 名字一律 <c>probe.</c> / <c>fx.probe_</c> 前缀，第一次读就知道它不是内容。
    /// </summary>
    public static class Probe
    {
        /// <summary>有前摇 + 有消耗 + 有冷却 + 立即结算：F1 那一格的载体。</summary>
        public const string WindupSlash = "probe.windup_slash";

        /// <summary>有前摇且在放中授予 <c>state.guard</c>：F2 / F3 / F5 / D5 那一格的载体。</summary>
        public const string ChannelSlash = "probe.channel_slash";

        /// <summary>探针技能那一刀的结算效果（伤害写死，不走 calc——清单只问时序与激活检查）。</summary>
        public const string WindupHit = "fx.probe_windup_hit";

        /// <summary>同上，蓄力那一刀。</summary>
        public const string ChannelHit = "fx.probe_channel_hit";

        /// <summary>带 <c>CancelMatchingTags</c> 而<b>不带延迟</b>的打断效果：F3 / D5 的显式打断。</summary>
        public const string Interrupt = "fx.probe_interrupt";

        /// <summary>只授予 <c>state.guard</c> 的效果段探针：F5 的「两段合到一张计数表」。</summary>
        public const string TagGrant = "fx.probe_tag_grant";

        /// <summary>
        /// 前摇 3 步（0.3 秒）、冷却 20 步（2 秒）、过 commit 扣怒气 30、那一刀写死 20 血。
        /// 现成 16 条没有一条同时带前摇与消耗：<c>warrior.rift</c> 有前摇无消耗、
        /// <c>warrior.execute</c> 有消耗无前摇（前摇 0 步），而 F1 问的正是
        /// 「过 commit 前后各取消一次，消耗与冷却差多少」——三列都得有值才问得出。
        /// </summary>
        public const long WindupPreSteps = 3;
        public const long WindupCooldownSteps = 20;
        public const double WindupCostRage = 30;
        public const double WindupDamage = 20;

        /// <summary>
        /// 前摇 2 步（0.2 秒）、那一刀写死 15 血。16 条里唯一「在放中授予 tag」的是
        /// <c>tank.guard</c>，而它前摇 0 步（按下即提交），摆不出 D5「取消与前摇走完撞同一步」
        /// 与 F3「interrupt 那一行念的是 committed=0」那两格——那两格必须有一条真在前摇里的技能。
        /// 授予的 tag 仍用 <c>state.guard</c>（与盾墙同一个「在放中」口径）。
        /// </summary>
        public const long ChannelPreSteps = 2;
        public const double ChannelDamage = 15;

        /// <summary>
        /// 打断探针在身上留 10 步（1 秒）：16 条里带 <c>CancelMatchingTags</c> 的只有
        /// <c>fx.joust_second</c>，而它带 7 步延迟（要走 S0 落地才进取消队列），会把那两格的时序整段推走。
        /// 留 10 步是为了 F3 读完那一行 committed=0 之后，还数得出这条效果在身上（撤技能 ≠ 撤效果）。
        /// </summary>
        public const long InterruptHoldSteps = 10;

        /// <summary>
        /// tag 探针留 80 步（8 秒）：16 条里授予 <c>state.guard</c> 的那条效果
        /// （<c>fx.guard_wall</c>）同时带两笔 modifier，会把承伤倍率一起改掉，不是「只授予 tag」那一格。
        /// 步数取 80 与猎印同档，比清单里那几发的前摇长出一大截，到期不会插进要量的那几步中间。
        /// </summary>
        public const long TagGrantHoldSteps = 80;
    }

    public static class Fn
    {
        public const string StageAttack = "calc.stage_attack";
        public const string Negate = "calc.negate";
        public const string RiftByMissingHp = "calc.rift_by_missing_hp";
        public const string PierceDraw = "calc.pierce_draw";
        public const string PierceFloor = "calc.pierce_floor";

        /// <summary>谓词注册口（已删的设计档 <c>Abilities.Calc</c> 那行：公式、条件、谓词同一个形状）。</summary>
        public const string ExecuteGate = "calc.execute_gate";
        public const string FlatPlusMissing = "calc.flat_plus_missing";
        public const string MarkBonus = "calc.mark_bonus";
    }

    // === 八条技能的数字（清单页那条纪律：全取整数、周期取 0.5 秒的整数倍，台账要能手算）===

    /// <summary>突斩：前摇 3 步（0.3 秒）、冷却 20 步（2 秒）、按施法者已损失生命最多放大 2.5 倍。</summary>
    public const long RiftPreSteps = 3;
    public const long RiftCooldownSteps = 20;
    public const double RiftMissingHpRatio = 1.5;

    /// <summary>穿刺箭：前摇 2 步、飞行 6 步（0.6 秒）、曲线 (0,1.0)(6,0.8)(12,0.5) 按落地那一步的距离折。</summary>
    public const long PiercePreSteps = 2;
    public const long PierceFlightSteps = 6;
    public const double PierceMinDamage = 12;

    /// <summary>威压光环半径 4 米、敌攻击 ×0.85（「16 条清单」那一格）。</summary>
    public const double DreadRadiusMeters = 4;
    public const double DreadAttackMult = 0.85;

    /// <summary>稳固：1 层抵挡、每 60 步（6 秒）回一层；游斗：×1.8 被界抬回 6.0；骑乘：×1.25。</summary>
    public const long SteadyRechargeSteps = 60;
    public const double EvasiveSpeedMult = 1.8;
    public const double EvasiveSpeedCap = 6.0;
    public const double MountedSpeedMult = 1.25;

    /// <summary>践踏：前摇 2 步、定身 20 步（2 秒）。</summary>
    public const long TramplePreSteps = 2;
    public const long TrampleRootSteps = 20;

    // === 第二批那六条的数字（同一条纪律：全取整数、周期取 0.5 秒的整数倍）===

    /// <summary>
    /// 盾墙：持续 50 步（5 秒）、同段两笔 modifier（承伤 <c>Override 0.5</c> + 移速 <c>Mult 0.7</c>）、
    /// 授予 <c>state.guard</c>、入口被 <c>state.silence</c> 挡住；后摇 8 步（给连刺第二段留一个可打断的窗口）、
    /// 冷却 20 步（2 秒，猎印那发 ×1.3 的倍率就乘在它身上）。
    /// </summary>
    public const long GuardDurationSteps = 50;
    public const long GuardPostSteps = 8;
    public const long GuardCooldownSteps = 20;
    public const double GuardDamageTakenOverride = 0.5;
    public const double GuardSpeedMult = 0.7;
    public const double MarkCooldownMult = 1.3;

    /// <summary>处决：怒气 30 一发（Cost 与来源属性快照那一格），血线谓词 0.2，底伤 30 加目标已损失生命。</summary>
    public const double ExecuteCostRage = 30;
    public const double ExecuteHpRatioGate = 0.2;
    public const double ExecuteFlatDamage = 30;

    /// <summary>撕裂：持续 60 步（6 秒）、每 5 步（0.5 秒）一跳 4 血、叠 3 层每层 +2 血、叠层不重置时长。</summary>
    public const long BleedDurationSteps = 60;
    public const long BleedPeriodSteps = 5;
    public const double BleedDamagePerTick = 4;
    public const int BleedMaxStacks = 3;
    public const double BleedDamagePerStack = 2;

    /// <summary>
    /// 箭雨：同一次激活登记三发（+4 / +6 / +8 步各落地），落点上限 8.0 / 8.0 / 7.0 米——
    /// 上限走<b>距离曲线</b>（既有列，第二批不动列），最后一发按落地当步的距离折到 0，那就是落空。
    /// </summary>
    public const long RainDelayA = 4;
    public const long RainDelayB = 6;
    public const long RainDelayC = 8;
    public const double RainReachNearMeters = 8;
    public const double RainReachFarMeters = 7;

    /// <summary>猎印：持续 80 步（8 秒）、授予 <c>status.marked</c>、marked 目标挨打 ×1.2、目标那条技能冷却 ×1.3。</summary>
    public const long MarkDurationSteps = 80;
    public const double MarkDamageMult = 1.2;

    /// <summary>连刺：两段各占一条 Pending（+3 / +7 步），第二段带 <c>CancelMatchingTags = state.guard</c>。</summary>
    public const long JoustDelayFirst = 3;
    public const long JoustDelaySecond = 7;
    public const double JoustSecondDamage = 12;

    /// <summary>
    /// 表：一条技能一张效果，全在这里挂号。<b>注册壳的注册函数</b>——场子只吃这张表。
    /// </summary>
    public static AbilityTables Tables()
    {
        var tables = new AbilityTables();

        // 木桩那一下平A：走效果，不走「自己写一句扣血」——抵挡与承伤倍率才问得到同一份真值。
        tables.Register(new EffectDef(Fx.BasicAttack, EffectKind.Instant,
            new[] { ChainToHealth() }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            new[] { Fn.StageAttack }, NoArgs(), 0, 0, Array.Empty<string>(), NoTriggers()));

        // 突斩
        tables.Register(new EffectDef(Fx.RiftSlash, EffectKind.Instant,
            new[] { ChainToHealth() }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            new[] { Fn.RiftByMissingHp }, Args(("ratio", RiftMissingHpRatio)), 0, 0,
            Array.Empty<string>(), NoTriggers()));
        tables.Register(new AbilityDef(Skill.Rift, RiftPreSteps, RiftCooldownSteps,
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            new[] { Fx.RiftSlash }, string.Empty, 0, RangeMeters: 3));

        // 威压（光环）：效果本身不带距离——距离由场子按 S5 那段数据算，跨边界发这两条动词（N-13）。
        // 主动那一发还是走 S4 入口（射程 = 光环半径），场子那条边界规则负责「之后每次越界怎么重应用」。
        tables.Register(EffectDef.OverTime(Fx.DreadAura, new[]
        {
            new ModifierSpec(Attr.Attack, ModifierMode.Mult, DreadAttackMult),
        }, durationTicks: 0, kind: EffectKind.Infinite));
        tables.Register(new AbilityDef(Skill.Dread, 0, 0, Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<string>(), new[] { Fx.DreadAura }, string.Empty, 0, RangeMeters: DreadRadiusMeters));

        // 稳固：1 层抵挡 + 周期回充。它就是 EffectDef.IsChargeCarrier 那一档形状（N-15）。
        tables.Register(EffectDef.OverTime(Fx.SteadyCharges, Array.Empty<ModifierSpec>(),
            durationTicks: 0, periodTicks: SteadyRechargeSteps,
            grantedTags: new[] { Tag.BlockReady }, stackKey: "block", maxStacks: 1,
            kind: EffectKind.Infinite));
        tables.Register(new AbilityDef(Skill.Steady, 0, 0, Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<string>(), new[] { Fx.SteadyCharges }, string.Empty, 0, RangeMeters: 0));

        // 怒意：受击 → 加怒 8（上限 100 是属性的界，不是触发器里的 if）
        tables.Register(EffectDef.OverTime(Fx.RageOnHit, Array.Empty<ModifierSpec>(), durationTicks: 0,
            kind: EffectKind.Infinite,
            triggeredEffects: new[] { ("damage_taken", Fx.RageGain8) }));
        tables.Register(EffectDef.Instant(Fx.RageGain8, new ModifierSpec(Attr.Rage, ModifierMode.Add, 8)));
        tables.Register(new AbilityDef(Skill.Rage, 0, 0, Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<string>(), new[] { Fx.RageOnHit }, string.Empty, 0, RangeMeters: 0));

        // 穿刺箭：延迟结算 + 曲线 + 三项 Calc 链（N-12 要钉的就在这条链上）
        tables.Register(new EffectDef(Fx.PierceArrow, EffectKind.Instant,
            new[] { ChainToHealth() }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            new[] { Fn.PierceDraw, Fn.PierceFloor, Fn.Negate },
            Args(("floor", PierceMinDamage)), 0, 0, Array.Empty<string>(), NoTriggers())
        {
            DelayTicks = PierceFlightSteps,
            CurveXY = new[] { 0.0, 1.0, 6.0, 0.8, 12.0, 0.5 },
            CurveAxis = "distance",
        });
        tables.Register(new AbilityDef(Skill.Pierce, PiercePreSteps, 10, Array.Empty<string>(),
            Array.Empty<string>(), Array.Empty<string>(), new[] { Fx.PierceArrow }, string.Empty, 0,
            RangeMeters: 30));

        // 游斗：×1.8 的移速被 6.0 那道界抬回（Override 先聚合、界在最后，已删的设计档）
        tables.Register(EffectDef.OverTime(Fx.EasiveSpeed, new[]
        {
            new ModifierSpec(Attr.MoveSpeed, ModifierMode.Mult, EvasiveSpeedMult),
        }, durationTicks: 0, kind: EffectKind.Infinite));
        tables.Register(new AbilityDef(Skill.Evasive, 0, 0, Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<string>(), new[] { Fx.EasiveSpeed }, string.Empty, 0, RangeMeters: 0));

        // 践踏：命中施加定身，定身挡住 S5 那条腿（N-4，激活检查的第三处问之一）
        tables.Register(EffectDef.OverTime(Fx.TrampleRoot, Array.Empty<ModifierSpec>(),
            durationTicks: TrampleRootSteps, grantedTags: new[] { Tag.Rooted }));
        tables.Register(new AbilityDef(Skill.Trample, TramplePreSteps, 0, Array.Empty<string>(),
            Array.Empty<string>(), Array.Empty<string>(), new[] { Fx.TrampleRoot }, string.Empty, 0,
            RangeMeters: 3));

        // 骑乘：加成那条 modifier 自带生效条件（N-5），下马不撤销、只跳过
        tables.Register(EffectDef.OverTime(Fx.MountedTag, Array.Empty<ModifierSpec>(), durationTicks: 0,
            grantedTags: new[] { Tag.Mounted }, kind: EffectKind.Infinite));
        tables.Register(EffectDef.OverTime(Fx.MountedBonus, new[]
        {
            new ModifierSpec(Attr.MoveSpeed, ModifierMode.Mult, MountedSpeedMult,
                RequirementTags: new[] { Tag.Mounted }),
        }, durationTicks: 0, kind: EffectKind.Infinite));
        tables.Register(new AbilityDef(Skill.Mounted, 0, 0, Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<string>(), new[] { Fx.MountedTag }, string.Empty, 0, RangeMeters: 0));

        // === 第二批（2026-09-28）：六条全用既有列摆，一格都不新加 ===

        // 盾墙：同一次施加挂两笔 modifier（排序键 = Seq 递增，两笔都归这条效果）、
        // 承伤那笔是 Override 且过属性界 [0.2, 1]（已删的设计档的先后：先聚合、界在最后）。
        tables.Register(EffectDef.OverTime(Fx.GuardWall, new[]
        {
            new ModifierSpec(Attr.DamageTaken, ModifierMode.Override, GuardDamageTakenOverride),
            new ModifierSpec(Attr.MoveSpeed, ModifierMode.Mult, GuardSpeedMult),
        }, durationTicks: GuardDurationSteps, grantedTags: new[] { Tag.Guard }));
        tables.Register(new AbilityDef(Skill.Guard, 0, GuardCooldownSteps,
            new[] { Tag.Guard }, Array.Empty<string>(), new[] { Tag.Silence },
            new[] { Fx.GuardWall }, string.Empty, 0, RangeMeters: 0, PostSteps: GuardPostSteps));

        // 处决：Cost 那一格给出「入口真拒绝」的读数（NoCost），血线谓词走 Calc 注册口——
        // 谓词挂在<b>链尾</b>当校验器：不成立时把已经算好的那一发伤害归零（0 伤害 = 这一发不打，
        // 而 Cost 在 commit 就扣掉了、不返还）。不加「表达式列」，N-10 的口径照旧。
        tables.Register(new EffectDef(Fx.ExecuteGuillotine, EffectKind.Instant,
            new[] { ChainToHealth() }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            new[] { Fn.FlatPlusMissing, Fn.Negate, Fn.ExecuteGate },
            Args(("threshold", ExecuteHpRatioGate), ("base", ExecuteFlatDamage)), 0, 0,
            Array.Empty<string>(), NoTriggers()));
        tables.Register(new AbilityDef(Skill.Execute, 0, 0, Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<string>(), new[] { Fx.ExecuteGuillotine }, Attr.Rage, ExecuteCostRage,
            RangeMeters: 3));

        // 撕裂：堆叠三列（StackKey / MaxStacks / 每层加值）+「叠层不重置时长」那一档策略。
        // 伤害符号在这里显式取负（Add hp 的负值 = 扣血，S0 那一处才认它是伤害），
        // 所以「每层 +2」这一格在数据上是 magnitudePerStack = -2。
        // 它带 modifier，所以 EffectDef.IsChargeCarrier 为假 —— 这一格与稳固的回充共用 S3 那一个入口，
        // 边界由 F4 / N6 与下面那条断言 A60 守着：流血那一跳打伤害，回充那一跳只加层数。
        tables.Register(EffectDef.OverTime(Fx.BleedWound, new[] { ChainToHealth() },
            durationTicks: BleedDurationSteps, periodTicks: BleedPeriodSteps,
            stackKey: "bleed", maxStacks: BleedMaxStacks, magnitudePerStack: -BleedDamagePerStack,
            stackPolicy: StackPolicy.AddStackKeepDuration, calcSeed: -BleedDamagePerTick));
        tables.Register(new AbilityDef(Skill.Bleed, 0, 0, Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<string>(), new[] { Fx.BleedWound }, string.Empty, 0, RangeMeters: 3));

        // 箭雨：同一次激活登记三条 Pending（AbilityDef.EffectIds 三行），互不顶掉、错峰到期。
        // 「第三发差 0.5 米落空」用距离曲线表达：折到 0 就是没打着，落地当步的距离现算。
        tables.Register(RainShot(Fx.RainNear, RainDelayA, RainReachNearMeters));
        tables.Register(RainShot(Fx.RainMid, RainDelayB, RainReachNearMeters));
        tables.Register(RainShot(Fx.RainFar, RainDelayC, RainReachFarMeters));
        tables.Register(new AbilityDef(Skill.Rain, 0, 0, Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<string>(), new[] { Fx.RainNear, Fx.RainMid, Fx.RainFar }, string.Empty, 0,
            RangeMeters: 30));

        // 猎印：授予 status.marked，并把目标那条技能的冷却乘上去——冷却走 attribute 口（N-7），
        // 所以这里就是一条普通 Mult 修饰符，目标属性名是 cooldown.<技能名>。
        tables.Register(EffectDef.OverTime(Fx.MarkSeal, new[]
        {
            new ModifierSpec(Attr.CooldownGuard, ModifierMode.Mult, MarkCooldownMult),
        }, durationTicks: MarkDurationSteps, grantedTags: new[] { Tag.Marked }));
        tables.Register(new AbilityDef(Skill.Mark, 0, 0, Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<string>(), new[] { Fx.MarkSeal }, string.Empty, 0, RangeMeters: 30));

        // 连刺：两段各一条 Pending；第二段带 CancelMatchingTags，打断的是目标「在放的那条」（S3 的取消队列）。
        tables.Register(new EffectDef(Fx.JoustFirst, EffectKind.Instant,
            new[] { ChainToHealth() }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            new[] { Fn.StageAttack }, NoArgs(), 0, 0, Array.Empty<string>(), NoTriggers())
        {
            DelayTicks = JoustDelayFirst,
        });
        tables.Register(new EffectDef(Fx.JoustSecond, EffectKind.Instant,
            new[] { ChainToHealth() }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<string>(), NoArgs(), -JoustSecondDamage, 0, new[] { Tag.Guard }, NoTriggers())
        {
            DelayTicks = JoustDelaySecond,
        });
        tables.Register(new AbilityDef(Skill.Joust, 0, 0, Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<string>(), new[] { Fx.JoustFirst, Fx.JoustSecond }, string.Empty, 0,
            RangeMeters: 10));

        // 激活检查探针：给一次施法挂上 state.silence（不占 16 条技能名额，只为读出 BlockedTags 那一格的拒绝原因）。
        tables.Register(EffectDef.OverTime(Fx.SilenceProbe, Array.Empty<ModifierSpec>(), durationTicks: 0,
            grantedTags: new[] { Tag.Silence }, kind: EffectKind.Infinite));

        // === 清单探针件（见 Probe 那一段的「为什么现成 16 条摆不出这三格」）===

        // 有前摇 + 有消耗 + 有冷却 + 立即结算：F1 那一格的载体。
        tables.Register(EffectDef.Instant(Probe.WindupHit,
            new ModifierSpec(Attr.Health, ModifierMode.Add, -Probe.WindupDamage)));
        tables.Register(new AbilityDef(Probe.WindupSlash, Probe.WindupPreSteps, Probe.WindupCooldownSteps,
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            new[] { Probe.WindupHit }, Attr.Rage, Probe.WindupCostRage, RangeMeters: 3));

        // 有前摇且在放中授予 state.guard：F2 的「管不住在放中」+ F3 / D5 的被打断方 + F5 的能力段。
        tables.Register(EffectDef.Instant(Probe.ChannelHit,
            new ModifierSpec(Attr.Health, ModifierMode.Add, -Probe.ChannelDamage)));
        tables.Register(new AbilityDef(Probe.ChannelSlash, Probe.ChannelPreSteps, 0,
            new[] { Tag.Guard }, Array.Empty<string>(), Array.Empty<string>(),
            new[] { Probe.ChannelHit }, string.Empty, 0, RangeMeters: 3));

        // 不带延迟的显式打断：自己不带 modifier，只留一条效果行 + 一次取消请求（S3 开头兑现）。
        tables.Register(EffectDef.OverTime(Probe.Interrupt, Array.Empty<ModifierSpec>(),
            durationTicks: Probe.InterruptHoldSteps, cancelMatchingTags: new[] { Tag.Guard }));

        // 只授予 tag 的效果段：与上面那条技能的「在放中」那一层合到 S2 同一张计数表（F5）。
        tables.Register(EffectDef.OverTime(Probe.TagGrant, Array.Empty<ModifierSpec>(),
            durationTicks: Probe.TagGrantHoldSteps, grantedTags: new[] { Tag.Guard }));

        return tables;
    }

    /// <summary>箭雨的一发：落点上限就是那条距离曲线（超过上限折到 0 = 落空）。</summary>
    private static EffectDef RainShot(string id, long delayTicks, double reachMeters)
        => new EffectDef(id, EffectKind.Instant,
            new[] { ChainToHealth() }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            new[] { Fn.MarkBonus, Fn.Negate }, Args(("mult", MarkDamageMult)),
            0, 0, Array.Empty<string>(), NoTriggers())
        {
            DelayTicks = delayTicks,
            CurveXY = new[] { 0.0, 1.0, reachMeters, 1.0, reachMeters + 0.5, 0.0 },
            CurveAxis = "distance",
        };

    public static CalcRegistry Calcs() => new CalcRegistry()
        // 平A：-(攻击 + 链上流过来的值)，链的起点是 0
        .Register(Fn.StageAttack, i => -(CalcInput.Attr(i.Source, Attr.Attack) + i.Working))
        // 突斩：按施法者自己已损失的生命放大（源属性是 commit 时抄的快照）
        .Register(Fn.RiftByMissingHp, i =>
        {
            var max = CalcInput.Attr(i.Source, Attr.MaxHealth);
            var hp = CalcInput.Attr(i.Source, Attr.Health);
            var attack = CalcInput.Attr(i.Source, Attr.Attack);
            var ratio = max > 0 ? 1 + (1 - hp / max) * i.Param("ratio") : 1.0;
            return -(attack * ratio + i.Working);
        })
        // 穿刺箭那三项链：第一项把攻击搬进 working，第二项设地板，第三项才翻成扣血。
        // N-12 要的就是这一条：链上哪一项把值吞了、顺序错了就读不出那个地板数。
        .Register(Fn.PierceDraw, i => CalcInput.Attr(i.Source, Attr.Attack) + i.Working)
        .Register(Fn.PierceFloor, i => Math.Max(i.Working, i.Param("floor")))
        .Register(Fn.Negate, i => -i.Working)
        // 处决的血线谓词（链尾校验）：目标血量比例没越过那道线，就把链上算好的伤害归零（这一发不打）。
        // 读的是落地/commit 那一格的目标属性，与 A43「目标按 Id 现解」同一口径。
        .Register(Fn.ExecuteGate, i =>
        {
            var max = CalcInput.Attr(i.Target, Attr.MaxHealth);
            var hp = CalcInput.Attr(i.Target, Attr.Health);
            return max > 0 && hp / max < i.Param("threshold") ? i.Working : 0;
        })
        // 按目标已损失生命计算：底伤 30 + 目标已损失的那一截。
        .Register(Fn.FlatPlusMissing, i => i.Param("base")
            + (CalcInput.Attr(i.Target, Attr.MaxHealth) - CalcInput.Attr(i.Target, Attr.Health))
            + i.Working)
        // tag 参与计算（猎印那一格）：读目标的 tag 计数，不给 tag 单开通道。
        .Register(Fn.MarkBonus, i => CalcInput.Attr(i.Source, Attr.Attack)
            * (i.TagCount.TryGetValue(Tag.Marked, out var count) && count > 0 ? i.Param("mult") : 1.0));

    /// <summary>
    /// 装一局单挑场。数值与档位都由调用点给（缺省是一局最好算的：英雄 100 血 20 攻、木桩 100 血 10 攻、隔 2 米）。
    /// <paramref name="passives"/> 里那些效果在<b>第 0 步经词表发出去</b>（走写路径、进账），
    /// 不是在装配里偷偷塞状态——被动什么时候挂上的，行为流里读得出来。
    /// </summary>
    public static SkillTestWorld Stage(
        SkillTestWorld.Tuning? tuning = null,
        int seed = 1,
        double heroHp = 100,
        double heroAttack = 20,
        double heroSpeed = 4,
        double dummyHp = 100,
        double dummyAttack = 10,
        IReadOnlyList<string>? heroAbilities = null,
        IReadOnlyList<string>? dummyAbilities = null,
        IReadOnlyList<string>? heroPassives = null,
        IReadOnlyList<string>? dummyPassives = null,
        bool aura = false,
        bool dummyUnkillable = false)
    {
        var chosen = tuning ?? new SkillTestWorld.Tuning();
        if (dummyUnkillable)
        {
            chosen = chosen with { EnemyUnkillable = true };
        }
        var spec = new SkillTestWorld.Spec
        {
            HeroId = HeroId,
            DummyId = DummyId,
            Tables = Tables(),
            Calcs = Calcs(),
            HealthAttribute = Attr.Health,
            MaxHealthAttribute = Attr.MaxHealth,
            AttackAttribute = Attr.Attack,
            Config = new AbilityLayerConfig
            {
                HealthAttribute = Attr.Health,
                DamageTakenAttribute = Attr.DamageTaken,
                CooldownAttributePrefix = Attr.CooldownPrefix,
                RootedTag = Tag.Rooted,
                DamageBlockTag = Tag.BlockReady,
                DamageTakenEvent = Attr.DamageTaken,
                EffectAppliedEvent = Attr.EffectApplied,
                EventCapacity = SkillTestWorld.StageEventCapacity,
            },
            HeroAttributes = new[]
            {
                (Attr.Health, heroHp), (Attr.MaxHealth, heroHp), (Attr.Attack, heroAttack),
                (Attr.MoveSpeed, heroSpeed), (Attr.DamageTaken, 1.0), (Attr.Rage, 0.0),
                (Attr.CooldownGuard, 1.0),
            },
            DummyAttributes = new[]
            {
                (Attr.Health, dummyHp), (Attr.MaxHealth, dummyHp), (Attr.Attack, dummyAttack),
                (Attr.MoveSpeed, 4.0), (Attr.DamageTaken, 1.0), (Attr.Rage, 0.0),
                (Attr.CooldownGuard, 1.0),
            },
            HeroAbilityIds = heroAbilities ?? Array.Empty<string>(),
            DummyAbilityIds = dummyAbilities ?? Array.Empty<string>(),
            AutoCastAbilityId = heroAbilities?.FirstOrDefault(),
            BasicAttackEffectId = Fx.BasicAttack,
            Auras = aura
                ? new[] { new SkillTestWorld.AuraRule(HeroId, Fx.DreadAura, DreadRadiusMeters) }
                : Array.Empty<SkillTestWorld.AuraRule>(),
        };

        var world = new SkillTestWorld(spec, chosen, seed);
        world.SetLimits(HeroId, Attr.Health, 0, heroHp);
        world.SetLimits(DummyId, Attr.Health, 0, dummyHp);
        world.SetLimits(HeroId, Attr.Rage, 0, 100);
        world.SetLimits(DummyId, Attr.Rage, 0, 100);
        world.SetLimits(HeroId, Attr.DamageTaken, 0.2, 1.0);
        world.SetLimits(DummyId, Attr.DamageTaken, 0.2, 1.0);
        world.SetLimits(HeroId, Attr.MoveSpeed, 0.2, EvasiveSpeedCap);
        world.SetLimits(DummyId, Attr.MoveSpeed, 0.2, 10.0);

        foreach (var (owner, effectId) in Passives(HeroId, heroPassives).Concat(Passives(DummyId, dummyPassives)))
        {
            AssertInjected(world.Inject(AbilityVerbs.ApplyEffect, $"{owner}|{effectId}"), effectId);
        }

        return world;
    }

    private static IEnumerable<(string Owner, string EffectId)> Passives(
        string owner, IReadOnlyList<string>? effectIds)
        => (effectIds ?? Array.Empty<string>()).Select(id => (owner, id));

    /// <summary>装配期那一发不许静默：摆不上就是壳写错了，当场抛。</summary>
    private static void AssertInjected(bool ok, string what)
    {
        if (!ok)
        {
            throw new InvalidOperationException($"装配阶段摆不上 {what}（词表返回 false）");
        }
    }

    private static ModifierSpec ChainToHealth() => new(Attr.Health, ModifierMode.Add, 0,
        CalcFromEffectValue: true);

    private static IReadOnlyDictionary<string, double> Args(params (string Key, double Value)[] pairs)
        => pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, double> NoArgs() =>
        new Dictionary<string, double>(StringComparer.Ordinal);

    private static IReadOnlyList<(string OnEvent, string EffectId)> NoTriggers()
        => Array.Empty<(string, string)>();
}
