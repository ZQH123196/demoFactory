#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DemoFactory.GugaFramework.Abilities;

namespace DemoFactory.Prototypes.InfinityLord.Logic;

/// <summary>
/// 无限领主接技能层的<b>接线那一件</b>（<c>SkillHost</c>）—— 世界向层供的口、层的每步推进、
/// 以及伤害路径的结算（Q12）。
///
/// <para><b>只有一个写入点</b>：伤害、激活检查与台账全部由层算，世界只做两件事 ——
/// 出手之前把这一对的三个数抄进层（血 / 血上限 / 攻击，真值在世界），出手之后把层算出来的那一笔
/// 写回 <b>血的真值那一侧</b>：单位写回 <see cref="Unit.Health"/>，建筑写回建造模块
/// （<see cref="StrikeBuilding"/> 走 <c>ConstructionCore.Damage</c>，不走 <c>SetHealth</c>）。</para>
/// <para>别处一个字节都不往层里写。</para>
///
/// <para><b>单位与建筑同册</b>（Q12 后半）：层里只有一册目标（<c>AbilityWorld.Units</c>），
/// 建筑以<b>宿主声明的身份</b>进这一册 —— 层不认识「建筑」这个词，它只知道「这一个目标在不在册」。
/// 身份住在这一侧（<see cref="_buildingIds"/>）：攻坚与范围技能要分辨目标是不是楼，问
/// <see cref="IsBuilding"/>，不问公共层。公共层为此只多要了一格
/// <see cref="IAbilityHost.Exists"/>（「这个 Id 你认不认识」）。</para>
///
/// <para><b>建筑不还手</b>：它进册只装四列（血 / 血上限 / 承伤倍率 / 移速倍率），
/// 技能栏是空的 —— 层每步逐技能那一遍对它空转，但它没有「出手」这件事。</para>
///
/// <para><b>占位主动技 0 条</b>（Q13）：表里只有每类单位那一条普攻，没有任何主动技；
/// 词表这一侧只接 <c>CastAbility</c> 与 <c>ApplyEffect</c> 两条 —— 够调试直达、够重放录制。</para>
///
/// <para><b>状态可查</b>：<see cref="Snapshot"/> 是层自己那一份全量文本（技能栏 / 修饰符 / 效果 /
/// 冷却 / 计数），世界快照把它整段搬进去，一个数都不重算。</para>
/// </summary>
public sealed class InfinityLordAbilities : IAbilityHost, IAbilityVerbSink
{
    /// <summary>事件流容量（公共层不替你定这一格，缺了构造即抛；仓里各调用点一律 2000）。</summary>
    public const int EventCapacity = 2000;

    /// <summary>
    /// 建筑那几列不进层的属性：建筑<b>没有</b>攻击与移速，攻坚射程由调用点判（与既有直路同一条尺）。
    /// 记在这里是为了让「建筑装哪几列」只有一处写得出。
    /// </summary>
    private static readonly string[] BuildingAttributes =
        [InfinityLordSkillTable.HealthAttribute, "hp_max", InfinityLordSkillTable.DamageTakenAttribute];

    /// <summary>接层那份配置 —— 公共层那七列一个缺省都没有，词表全在技能表那一份里。</summary>
    private static readonly AbilityLayerConfig LayerConfig = new()
    {
        HealthAttribute = InfinityLordSkillTable.HealthAttribute,
        DamageTakenAttribute = InfinityLordSkillTable.DamageTakenAttribute,
        CooldownAttributePrefix = InfinityLordSkillTable.CooldownAttributePrefix,
        RootedTag = InfinityLordSkillTable.RootedTag,
        DamageBlockTag = InfinityLordSkillTable.DamageBlockTag,
        DamageTakenEvent = "damage_taken",
        EffectAppliedEvent = "effect_applied",
        EventCapacity = EventCapacity,
        // 这一侧把「卡上的出手间隔」当唯一真值：零前摇技能在按下那一步过 commit，
        // 于是技能行里的冷却就是卡上那个数，世界按卡上的间隔出手（缺省那一档差一步，
        // 排兵布阵按自己的用例钉着那一档）。
        InstantCommitOnPressStep = true,
    };

    private AbilityWorld _sets = new();
    private AbilityStepper _stepper;

    /// <summary>层的属性名 ↔ 世界那个单位；顺序 = 世界的出场顺序（同一步遍历次序是确定性的一部分）。</summary>
    private readonly List<Unit> _owned = [];
    private readonly Dictionary<string, Unit> _byId = new(StringComparer.Ordinal);

    /// <summary>层的属性名 ↔ 世界那一座建筑；顺序 = 世界的落位顺序（层里排在全部单位之后）。</summary>
    private readonly List<Building> _ownedBuildings = [];
    private readonly Dictionary<string, Building> _buildingsById = new(StringComparer.Ordinal);

    /// <summary>
    /// 这一步还活着的建筑名单（每步开头照世界重填一个）。
    /// <b>不摘册</b>：层里那一册是「目标册」，摘了的话快照会按不同的段数列印，
    /// 而「这一座什么时候没的」世界那一侧已经记着（摧毁事件）。
    /// </summary>
    private readonly List<Building> _liveBuildings = [];

    public InfinityLordAbilities(Action<Building, double> writeBuildingDamageBack)
    {
        ArgumentNullException.ThrowIfNull(writeBuildingDamageBack);
        WriteBuildingDamageBack = writeBuildingDamageBack;
        _stepper = new AbilityStepper(_sets, this, InfinityLordSkillTable.BuildCalcs(),
            InfinityLordSkillTable.BuildTables(), LayerConfig);
    }

    /// <summary>层本体（调试与用例读它的冷却、相位与事件流）。</summary>
    public AbilityStepper Stepper => _stepper;

    /// <summary>
    /// 存档读档那一发：把层换成 <paramref name="snapshot"/> 那一份状态。
    ///
    /// <para><b>换的是整个层实例</b>：层的还原入口是静态工厂（吃一份
    /// <c>AbilityWorld</c> 重建一个步进器），没有「灌进现成那个」的写法。
    /// 换实例时 <c>_owned</c> / <c>_byId</c> / <c>_ownedBuildings</c> 那三张映射表
    /// <b>不能换</b> —— 它们连着世界的单位与建筑对象，而快照里存的是属性名。</para>
    ///
    /// <para><b>调用次序由这一格钉死</b>：先 <see cref="Register"/> / <see cref="RegisterBuilding"/>
    /// 把这一局的单位与建筑按落场次序登记一遍，再调它。反过来（先还原、后登记）的话
    /// 那三张映射表指向的对象与层里的属性名对不上，而症状是「一出手就说目标不在册」。</para>
    /// </summary>
    /// <param name="snapshot"><see cref="Snapshot"/> 那一份文本。</param>
    public void RestoreState(string snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var restored = AbilityStepper.Restore(new AbilityWorld(), this, InfinityLordSkillTable.BuildCalcs(),
            InfinityLordSkillTable.BuildTables(), snapshot, LayerConfig);

        // 层那一侧的还原入口吃的是「壳」参数，但它内部是从快照 Load 出另一个世界重建的 ——
        // 递进去的壳不进那个步进器。所以这里必须把这一份字段指到步进器真正持有的那一个世界，
        // 否则 Register 之后新登记的单位进的是另一个世界，出手那一步才抛「不在册」。
        _sets = restored.World;
        _stepper = restored;
    }

    // === 登记 ===

    /// <summary>
    /// 一个单位落场：给它一个技能栏，装上它那一条普攻。
    /// <b>属性只有这五列</b>（血 / 血上限 / 攻击 / 承伤倍率 / 移速倍率），别的一概不进层。
    /// </summary>
    public void Register(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (_byId.ContainsKey(unit.Id)) return;

        var set = _sets.AddUnit(unit.Id,
        [
            (InfinityLordSkillTable.HealthAttribute, unit.Health),
            ("hp_max", unit.MaxHealth),
            (InfinityLordSkillTable.AttackAttribute, unit.Attack),
            (InfinityLordSkillTable.DamageTakenAttribute, 1.0),
            (InfinityLordSkillTable.MoveSpeedAttribute, 1.0),
        ]);

        // 界：承伤倍率挡在 [0.2, 1]（与排兵布阵那一档同口径），移速倍率不封顶。
        _sets.SetLimits(unit.Id, InfinityLordSkillTable.DamageTakenAttribute, 0.2, 1.0);
        _sets.SetLimits(unit.Id, InfinityLordSkillTable.MoveSpeedAttribute, 0.2, 10.0);

        var abilityId = InfinityLordSkillTable.AbilityOf(unit);
        set.Base[InfinityLordSkillTable.CooldownAttributePrefix + abilityId] = 1.0;
        _sets.SetLimits(unit.Id, InfinityLordSkillTable.CooldownAttributePrefix + abilityId, 0.0, 10.0);
        set.Abilities[abilityId] = new AbilityOnBody { Id = abilityId };

        _owned.Add(unit);
        _byId[unit.Id] = unit;
    }

    /// <summary>登记过几个单位。</summary>
    public int UnitCount => _owned.Count;

    /// <summary>登记过几座建筑（含已被拆的：层不摘册，见 <see cref="_liveBuildings"/>）。</summary>
    public int BuildingCount => _ownedBuildings.Count;

    /// <summary>登记次序那一列（层的遍历次序就是它）。</summary>
    public IReadOnlyList<string> UnitOrder => _sets.UnitOrder;

    // === 建筑（Q12 后半：单位↔建筑那一条也进层） ===

    /// <summary>
    /// 一座建筑进层：给它一个技能栏，只装<b>四列</b>（血 / 血上限 / 承伤倍率 / 移速倍率）。
    /// <b>不装技能</b>：建筑不还手，层里就没有它那一条出手。
    ///
    /// <para>它排在这一册的<b>末尾</b>（在册单位之后）：层每步按这一册的次序遍历，
    /// 建筑排后面 ⇒ 单位那一段的次序与建筑进册之前逐字相同。</para>
    /// </summary>
    public void RegisterBuilding(Building building)
    {
        ArgumentNullException.ThrowIfNull(building);
        if (_buildingsById.ContainsKey(building.Id)) return;

        _sets.AddUnit(building.Id,
        [
            (InfinityLordSkillTable.HealthAttribute, (double)building.Health),
            ("hp_max", (double)building.MaxHealth),
            (InfinityLordSkillTable.DamageTakenAttribute, 1.0),
        ]);

        // 界与单位那一档同口径：承伤倍率挡在 [0.2, 1]。
        _sets.SetLimits(building.Id, InfinityLordSkillTable.DamageTakenAttribute, 0.2, 1.0);

        _ownedBuildings.Add(building);
        _buildingsById[building.Id] = building;
    }

    /// <summary>
    /// 一座建筑出册（摧毁 / 拆除）：把它从这一步的活名单里摘掉。
    /// <b>层那一册不摘</b>（<see cref="_liveBuildings"/>）—— 摘了的话层快照的段数会随局势伸缩，
    /// 重放基线再也对不上；「它已经没了」由世界的 <see cref="Exists"/> 一处回答。
    /// </summary>
    public void RetireBuilding(string buildingId)
    {
        var i = _liveBuildings.FindIndex(b => string.Equals(b.Id, buildingId, StringComparison.Ordinal));
        if (i >= 0) _liveBuildings.RemoveAt(i);
    }

    /// <summary>把一个建筑那两列抄进层（血 / 血上限）—— 血的真值在建造模块那一册。
    /// 返回抄进去的那一列血（调用点拿它当「出手之前」那个数，别去读模块那一行：那一行还没动）。</summary>
    public double MirrorBuilding(Building building)
    {
        ArgumentNullException.ThrowIfNull(building);
        if (!_sets.Units.TryGetValue(building.Id, out var set)) return building.Health;
        set.Base[InfinityLordSkillTable.HealthAttribute] = building.Health;
        set.Base["hp_max"] = building.MaxHealth;
        return building.Health;
    }

    /// <summary>
    /// 这个 Id 在层里是不是<b>建筑</b>。攻坚类技能与范围伤害要分辨目标时问它
    /// （公共层不认识「建筑」这个词，身份只住在这一侧）。
    /// </summary>
    public bool IsBuilding(string entityId) => _buildingsById.ContainsKey(entityId);

    /// <summary>
    /// <b>一次对建筑的出手</b>：与 <see cref="Strike"/> 同一条路 —— 层判激活检查、层算这一笔伤害，
    /// 差只差在写回哪一侧。
    ///
    /// <para><b>写回只走 <c>ConstructionCore.Damage</c></b>：那一发才发 <c>BuildingDamaged</c>
    /// 并在归零时走摧毁收口（出册 + 腾占用 + <c>Removed</c>）。走 <c>SetHealth</c> 写绝对值等于
    /// 绕开摧毁收口 —— 楼会停在 0 血而不出册，而「它已经没了」这件事谁也读不到。</para>
    ///
    /// <para><b>写回的是层算出来的差量</b>（层里那一列血的前后之差），不是调用点自己算的一笔：
    /// 伤害的算式只有层那一份，调用点不另算一遍（「决策落纯 C#」）。</para>
    /// </summary>
    /// <param name="attacker">出手的那一个。</param>
    /// <param name="target">挨打的那一座楼。</param>
    /// <param name="distanceMeters">两者此刻的距离（米）—— 由世界现算递进来，层里没有坐标。</param>
    public bool StrikeBuilding(Unit attacker, Building target, double distanceMeters)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(target);

        Mirror(attacker);
        LastDamage = 0;

        var abilityId = InfinityLordSkillTable.AbilityOf(attacker);
        if (!IsReadyToFire(attacker.Id, abilityId))
        {
            LastRejectReason = AbilityRejectReason.Cooldown;
            return false;
        }

        // 差量读<b>层里那一列</b>：房主那一列血的真值在建造模块，层这一列是每步同步的镜像。
        // 效果改的是层那一列，模块那一行在写回之前一个字节都没动 ——
        // 拿模块那一行的前后之差去算，永远算出 0（这一个坑本 ticket 实测踩过：楼一点血都不掉）。
        var before = MirrorBuilding(target);
        var fired = _stepper.TryActivate(attacker.Id, abilityId, target.Id, out var reason,
            move: null, distanceMeters: distanceMeters);
        LastRejectReason = reason;
        if (!fired) return false;

        _stepper.InjectEffect(target.Id, InfinityLordSkillTable.HitEffectOf(attacker), sourceId: attacker.Id);

        // 写回的是<b>层算出来的差量</b>，不是调用点自己算的一笔：伤害的算式只有层那一份，
        // 调用点不另算一遍（「决策落纯 C#」）。
        var dealt = before - MirrorDerivedHealth(target);
        if (dealt > 0)
        {
            WriteBuildingDamageBack(target, dealt);
        }

        LastDamage = dealt;
        return true;
    }

    // === 每步 ===

    /// <summary>
    /// 世界每步开头的第一件事：<b>把这一列血抄进层，再让层自己跑完六段</b>。
    ///
    /// <para><b>为什么抄在层之前</b>：世界是血的真值，层每步按它重算 tag 与派生值
    /// （快照上那列 <c>hp=</c> 读的就是这一份）。抄的次序排在推进之前，层这一步读到的才是当下的世界。</para>
    ///
    /// <para><b>为什么排在一步的开头</b>：层里的冷却从 commit 那一步起算，而零前摇这一档
    /// 在按下那一步就过 commit（<c>AbilityStepper.TryActivate</c>）。排在开头，世界按卡上的间隔
    /// 出手时层里正好转好 —— 两把尺的对齐见 <see cref="InfinityLordSkillTable.CooldownTicksOf"/>，
    /// 由用例钉住。</para>
    /// </summary>
    public void AdvanceStep()
    {
        foreach (var unit in _owned)
        {
            if (unit.Health > 0) Mirror(unit);
        }

        // 建筑那一段同款：先把世界那一列血抄进层，再让层跑（血的真值在建造模块那一册）。
        // 每步重填「还活着的那一份」：拆掉的那几座不再抄 —— 抄的话会把一座不在册的楼
        // 又写成满血，而层那一步还会把它当活目标（层不认识「建筑被拆了」这件事）。
        _liveBuildings.Clear();
        foreach (var building in _ownedBuildings)
        {
            if (building.Health <= 0) continue;
            _liveBuildings.Add(building);
            MirrorBuilding(building);
        }

        _stepper.AdvanceStep();
    }

    /// <summary>把一个单位那三列抄进层（血 / 血上限 / 攻击）。</summary>
    public void Mirror(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (!_sets.Units.TryGetValue(unit.Id, out var set)) return;
        set.Base[InfinityLordSkillTable.HealthAttribute] = unit.Health;
        set.Base["hp_max"] = unit.MaxHealth;
        set.Base[InfinityLordSkillTable.AttackAttribute] = unit.Attack;
    }

    // === 双方出手（单位↔单位、单位↔建筑：Q12 两条都收进层） ===

    /// <summary>最近一次出手被拒的理由（没被拒时是 <c>None</c>）。给调试与用例读，不进快照。</summary>
    public AbilityRejectReason LastRejectReason { get; private set; } = AbilityRejectReason.None;

    /// <summary>
    /// 最近一次出手打进对方身上的伤害（层算出来的那一笔）。
    /// 用 <c>double</c>：建筑那一册的血是 <c>double</c>（建造模块的账），取整会把那一笔写歪。
    /// </summary>
    public double LastDamage { get; private set; }

    /// <summary>
    /// <b>一次出手</b>：层里判激活检查（冷却 / 射程 / tag），过了就由层结算这一笔伤害，世界的血跟着写回。
    ///
    /// <para>三步全在层里：① <c>TryActivate</c> 判激活检查并把这一发记进台账（被拒也有名字）；
    /// ② <c>InjectEffect</c> 把命中效果交给层，伤害按那条共用的纯函数算；
    /// ③ 层的血抄回世界。世界的决策链只管「够不够得着、这一步要不要出手」。</para>
    ///
    /// <para><b>被拒时不静默</b>：<see cref="LastRejectReason"/> 与层的事件流各留一份
    /// （「事件发射前置」：拒绝要有名字）。</para>
    /// </summary>
    /// <param name="attacker">出手的那一个。</param>
    /// <param name="target">挨打的那一个。</param>
    /// <param name="distanceMeters">两者此刻的距离（米）—— 由世界现算递进来，层里没有坐标。</param>
    public bool Strike(Unit attacker, Unit target, double distanceMeters)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(target);

        Mirror(attacker);
        Mirror(target);
        LastDamage = 0;

        var abilityId = InfinityLordSkillTable.AbilityOf(attacker);

        // 热路径不许进排队口：层没转好这一步就不按（读的是层的相位，不是另判一遍激活检查）。
        //
        // 为什么必须挡：冷却中按下去，TryActivate 会把它记成「排一个」，而下一步 S1 转好后
        // DrainQueuedInputs 抢在决策之前把这一发补放掉。这一侧的技能行不带效果
        // （伤害由宿主在按下那一刻施加），补放等于空放 —— 世界那一步再按就读到 Busy，
        // 冷却照转，出手从此再也不落地（实测症状：第一次多按之后这一名再也不掉血）。
        // 世界那把尺与层同起点（两条都写卡上那个数），所以这一步被挡只是「还没轮到」。
        if (!IsReadyToFire(attacker.Id, abilityId))
        {
            LastRejectReason = AbilityRejectReason.Cooldown;
            return false;
        }

        var fired = _stepper.TryActivate(attacker.Id, abilityId, target.Id, out var reason,
            move: null, distanceMeters: distanceMeters);
        LastRejectReason = reason;
        if (!fired) return false;

        var before = target.Health;
        _stepper.InjectEffect(target.Id, InfinityLordSkillTable.HitEffectOf(attacker), sourceId: attacker.Id);
        target.Health = BaseHealthOf(target.Id);
        LastDamage = before - target.Health;
        return true;
    }

    /// <summary>
    /// 这条技能此刻是不是 <c>Ready</c> —— 直读层里那一格相位，不另判一次激活检查。
    /// 这处判定仍然只有 <c>TryActivate</c> 一处；这一问只为「别在层没转好时按下」，
    /// 免得那一发进排队口（见 <see cref="Strike"/>）。
    /// </summary>
    private bool IsReadyToFire(string unitId, string abilityId)
        => Find(unitId) is { } set
            && set.Abilities.TryGetValue(abilityId, out var ability)
            && ability.Phase == AbilityPhase.Ready;

    /// <summary>
    /// 调试直达那一发（建筑）：给一座楼扣一笔<b>指名数额</b>的血。
    /// 伤害那一笔走层的属性算术（与 <see cref="DebugHit"/> 同款：一条本步生效、下一步就撤的修饰符），
    /// 写回仍走唯一那个口 —— 层不认识建造模块，写回那一发是构造时递进来的
    /// <see cref="WriteBuildingDamageBack"/>。
    /// </summary>
    public bool DebugHitBuilding(Building building, double damage)
    {
        ArgumentNullException.ThrowIfNull(building);
        if (damage <= 0) return false;
        MirrorBuilding(building);

        // 到期步给当下：下一步的 S1 就把它撤掉（不撤的话它会每一步都减一次 —— 那是静默的第二份真值）。
        _stepper.InjectModifier(building.Id, InfinityLordSkillTable.HealthAttribute, ModifierMode.Add, -damage,
            expiresAtTick: _sets.Tick);

        // 修饰符改的是派生值那一列（Current 由层在 S2 重算），所以差量照它算。
        var after = MirrorDerivedHealth(building);
        if (after < building.Health)
        {
            WriteBuildingDamageBack(building, building.Health - after);
        }

        return true;
    }

    /// <summary>
    /// 把层里那一列<b>派生</b>血抄回世界那一册（<c>Current</c> 由层在 S2 重算，
    /// 所以修饰符那条路读它）。返回抄回去之后的数。
    /// </summary>
    private double MirrorDerivedHealth(Building building)
        => _sets.Units.TryGetValue(building.Id, out var set)
            && set.Current.TryGetValue(InfinityLordSkillTable.HealthAttribute, out var hp)
                ? hp
                : building.Health;

    /// <summary>
    /// <b>建筑的唯一写回口</b>：把层算出来的那一笔落到血的真值那一侧（建造模块）。
    /// 由世界在构造时递进来 —— 层这一件不认识建造模块，也不许直接改世界那一列血
    /// （那会绕开 <c>ConstructionCore.Damage</c> 的摧毁收口）。
    /// </summary>
    private readonly Action<Building, double> WriteBuildingDamageBack;

    /// <summary>
    /// 调试直达那一发（单位）：给一个单位扣一笔<b>指名数额</b>的血（数额由调用方给，技能表里没有这一条）。
    /// 走层的属性算术（一条本步生效、下一步就撤的修饰符），不走技能入口 —— 它问的不是「这个技能放不放得出」。
    /// </summary>
    public bool DebugHit(Unit unit, int damage)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (damage <= 0) return false;
        Mirror(unit);

        // 到期步给当下：下一步的 S1 就把它撤掉（不撤的话它会每一步都减一次 —— 那是静默的第二份真值）。
        _stepper.InjectModifier(unit.Id, InfinityLordSkillTable.HealthAttribute, ModifierMode.Add, -damage,
            expiresAtTick: _sets.Tick);
        unit.Health = CurrentHealthOf(unit.Id);
        return true;
    }

    /// <summary>层里那一列基础血（即时伤害落的是它）。查无此人按世界的数回 0。</summary>
    private int BaseHealthOf(string unitId)
        => _sets.Units.TryGetValue(unitId, out var set)
            && set.Base.TryGetValue(InfinityLordSkillTable.HealthAttribute, out var hp)
                ? (int)Math.Round(hp, MidpointRounding.AwayFromZero)
                : 0;

    /// <summary>层里那一列派生血（修饰符路径读它）。</summary>
    private int CurrentHealthOf(string unitId)
        => _sets.Units.TryGetValue(unitId, out var set)
            && set.Current.TryGetValue(InfinityLordSkillTable.HealthAttribute, out var hp)
                ? (int)Math.Round(hp, MidpointRounding.AwayFromZero)
                : 0;

    // === 世界每步要问的两个口子 ===

    /// <summary>
    /// 移动激活检查 —— 定身挂着就走不动。<b>转发公共层那一份 <c>AbilityStepper.CanMove</c></b>，
    /// 原型不另判一次「有没有定身」（判两遍就会漂，而漂了之后症状是「有时定身没挡住」）。
    /// 层里没有这个单位时放行：那说明它没挂着任何东西 —— 这一格在登记齐全时走不到。
    /// </summary>
    public bool CanMove(string unitId)
        => !_sets.Units.ContainsKey(unitId) || _stepper.CanMove(unitId);

    /// <summary>
    /// 这个单位这一步的<b>移速倍率</b>（世界拿它乘卡上的速度）。没挂技能栏的一律按 1.0 ——
    /// 本版一个减速效果都没授予，所以这个数此刻恒为 1.0，形状先铺上（Q4 保留那一口）。
    /// </summary>
    public double SpeedScale(string unitId)
        => _sets.Units.TryGetValue(unitId, out var set)
            && set.Current.TryGetValue(InfinityLordSkillTable.MoveSpeedAttribute, out var scale)
                ? scale
                : 1.0;

    /// <summary>层那一半的全量快照（世界快照整段搬它，一个数都不重算）。</summary>
    public string Snapshot() => _stepper.TakeSnapshot();

    // === 行为流读数口（全部转发公共层的 AbilityEventLog，原型不另存一份账） ===

    /// <summary>行为流里某一类事件留着几条。</summary>
    public int Count<T>() where T : EventPayload => _sets.Events.Count<T>();

    /// <summary>行为流里某一类事件的全部留窗条目。</summary>
    public IEnumerable<T> EventsWhere<T>() where T : EventPayload => _sets.Events.Where<T>();

    /// <summary>行为流累计发出的条数（表现件游标照它走）。</summary>
    public long EventTotal => _sets.Events.Total;

    /// <summary>被环形挤掉的条数：「尾窗里没有」≠「没发生」，读数先看这一格。</summary>
    public long EventsDropped => _sets.Events.Dropped;

    /// <summary>最近 n 条事件。</summary>
    public IReadOnlyList<AbilityEvent> EventsTail(int n) => _sets.Events.Tail(n);

    /// <summary>最近 n 条的行文本（桥与报告读它）。</summary>
    public IReadOnlyList<string> EventTailLines(int n) => _sets.Events.Tail(n).Select(e => e.Line).ToList();

    /// <summary>
    /// 行为流尾窗的可读出口（公共层那一份排版：头一行带 <c>dropped=</c> / <c>total=</c> 读数，
    /// 正文逐条一行）。<b>原型不自己排版</b> —— 「事件行」那一份只此一处。
    /// </summary>
    public string EventTailText(int n) => _sets.Events.TailText(n);

    // === IAbilityHost（世界向层供的三样：单位查询口 / 存活判定 / 随机源） ===

    public AbilitySet? Find(string unitId) => _sets.Units.TryGetValue(unitId, out var set) ? set : null;

    /// <summary>
    /// 还活着没 —— 判的是<b>世界那一列血</b>（单位读 <see cref="Unit.Health"/>，
    /// 建筑读建造模块册内那一行；两边世界都是血的真值，层那列是每步同步的镜像）。
    /// </summary>
    public bool IsAlive(string unitId)
        => _byId.TryGetValue(unitId, out var unit) ? unit.Health > 0
            : _buildingsById.TryGetValue(unitId, out var building) && building.Health > 0;

    /// <summary>
    /// 层认不认识这个 Id —— 这一格答的是「它是不是<b>当下有效</b>的目标」，
    /// 所以由 <see cref="IsAlive"/> 一处回答：世界那两册里已经没有的、血量已经归零的，
    /// 一律算「不认识」。
    ///
    /// <para><b>为什么必须读世界那一册而不是层那一册</b>：层那一册<b>不摘册</b>
    /// （摘了的话层快照的段数会随局势伸缩，重放基线再也对不上）。于是「这一座被拆了」
    /// 在层里读不出来 —— 拿一个已经不在场的 Id 进 <c>TryActivate</c>，激活检查会判它「打不着」
    /// 而不是「这一位没了」，而 <c>UnitsInRange</c> 那种点名册也会把一堆废墟报成目标。</para>
    ///
    /// <para>代价是每一步每目标一次字典查询，换来的是「有效目标」只有一个答案。</para>
    /// </summary>
    public bool Exists(string entityId) => IsAlive(entityId);

    public IReadOnlyList<string> UnitIds => _sets.UnitOrder;

    /// <summary>
    /// 层的随机源。表里四条技能都是确定值算式，一个随机数都不抽 ——
    /// 这个口只把 <see cref="IAbilityHost"/> 填满，抽它等于把一个隐藏随机源引进逻辑层。
    /// </summary>
    public double NextRandom()
        => throw new InvalidOperationException(
            "无限领主的技能表里没有一条会抽随机数的技能；走到这里说明有人给表加了随机算法，那要先说清种子从哪来");

    // === IAbilityVerbSink（薄适配：只回答「这一侧哪个口子接得住」） ===

    public string HostLabel => "无限领主";

    /// <summary>这一侧接得住的两条：出手与直接施加效果（定身与减速那两条调试路径靠它）。</summary>
    public IReadOnlySet<string> SupportedVerbs { get; } =
        new HashSet<string>(StringComparer.Ordinal) { AbilityVerbs.Cast, AbilityVerbs.ApplyEffect };

    /// <summary>普攻没有位移载荷 —— 给了第 4 段当场抛（口径 3），不演成「按下了但没走」。</summary>
    public bool AcceptsCastDestination => false;

    /// <summary>
    /// 放一条技能（调试直达与重放录制走它，与世界的出手路径同一条）。
    /// 技能 Id 原样按调用方给的判激活检查：这一发要能回答「这条技能在这个单位身上会怎样」。
    /// </summary>
    public bool Cast(string unitId, string abilityId, string? targetId, (double FromX, double ToX)? move)
    {
        _ = move;   // 这一侧声明不接位移（AcceptsCastDestination=false），走到这里时它必为 null
        if (!_byId.TryGetValue(unitId, out var attacker)) return false;
        if (targetId is null || !_byId.TryGetValue(targetId, out var target)) return false;

        var distance = Vector2.Distance(attacker.Position, target.Position);
        Mirror(attacker);
        Mirror(target);
        LastDamage = 0;

        var fired = _stepper.TryActivate(unitId, abilityId, targetId, out var reason,
            move: null, distanceMeters: distance);
        LastRejectReason = reason;
        if (!fired) return false;

        var before = target.Health;
        _stepper.InjectEffect(targetId, InfinityLordSkillTable.HitEffectOf(attacker), sourceId: unitId);
        target.Health = BaseHealthOf(targetId);
        LastDamage = before - target.Health;
        return true;
    }

    /// <summary>绕过技能直接挂效果（调试直达：定身与减速两条路径都走它）。</summary>
    public void ApplyEffect(string targetId, string effectId, long? durationTicks, string? sourceId)
    {
        if (!_byId.TryGetValue(targetId, out var target)) return;
        Mirror(target);
        _stepper.InjectEffect(targetId, effectId, durationTicks, sourceId);

        // 即时效果（伤害那一类）落的是基础血：层的血要抄回世界，不然这一步的扣血只活在层里。
        if (_stepper.Tables.Effect(effectId).Kind == EffectKind.Instant)
        {
            target.Health = BaseHealthOf(targetId);
        }
    }

    public bool Cancel(string unitId, string abilityId)
        => throw NotTaken(AbilityVerbs.Cancel, "普攻没有前摇，没有可取消的在放段");

    public void RemoveEffect(string targetId, string effectId, int stacks)
        => throw NotTaken(AbilityVerbs.RemoveEffect, "这一侧撤效果走施加时的到期步，不给撤动词");

    public void SetAttribute(string targetId, string attribute, double value, string mode, string[]? requirementTags)
        => throw NotTaken(AbilityVerbs.SetAttribute, "属性的现场改写走 InjectModifier 那一发，不给词表动词");

    public void SetBase(string targetId, string attribute, double value)
        => throw NotTaken(AbilityVerbs.SetBase, "现场改基础值这一发这一侧没接（要接先想清它与世界那一列的关系）");

    public void ClearCooldown(string unitId, string abilityId)
        => throw NotTaken(AbilityVerbs.ClearCooldown, "清冷却这一发这一侧没接");

    public bool SetStepsRemaining(string unitId, string abilityId, long steps)
        => throw NotTaken(AbilityVerbs.SetStepsRemaining, "普攻没有前摇，没有剩余步数可改写");

    public bool Move(string unitId, double dx)
        => throw NotTaken(AbilityVerbs.Move, "位移归寻路层那一份移动解（「寻路单入口」），技能层不落坐标");

    /// <summary>这一侧不接的动词：名字与理由说清，不返回 false（那是「真打不成」的读数，两件事不许搅成一锅）。</summary>
    private NotSupportedException NotTaken(string verb, string why)
        => new($"{HostLabel} 这一侧不接 {verb}：{why}");
}
