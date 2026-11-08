#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DemoFactory.GugaFramework.Construction;
using DemoFactory.GugaFramework.Replay;

namespace DemoFactory.Prototypes.BattleArray.Logic;

/// <summary>
/// 「我方一个剑士 打 敌方一个蛮牛 + 剑士带一条突斩」这一局的装配 —— H3 那一份宿主。
///
/// 三个消费者共用这一份，谁都不许抄第二份（抄了就会漂成两套真值）：
/// - 离线级 1 重放：<c>Tests/AbilityD8ReplayTests.cs</c> 与 <c>dotnet test</c>；
/// - 离线取证 CLI：<c>Tools/BattleArrayTools.cs</c> 的 <c>replay</c> 动词（D4）；
/// - 引擎里的活局级 2：<c>Scripts/Main.cs</c> 的桥动词（D8、D1、D3）。
///
/// 为什么这一份放在 <c>Scripts/Logic/</c> 而不是只放 Tests：级 2 必须在<b>跑着的那个进程</b>里装出
/// 同一个世界，测试目录不进游戏程序集；而它又不是玩法规则（是「把技能层接到这一局上」的装配），
/// 所以它既不碰结算也不发数值，只挑种子、挑摆法、给词表。
///
/// 台账口径沿用 <c>BattleMechanicsTests.近战一对一…</c>：抖动关、剑士 170 血 20 伤、蛮牛 260 血 13 伤，
/// 首刀第 42 步、第 162 步分胜负、剑士剩 53 血 —— 那是<b>全关档</b>的账，突斩进来后的账由
/// <see cref="NewWorld"/> 跑出来的实数回写，不靠纸面推测。
/// </summary>
public static class DuelRiftSetup
{
    /// <summary>与机制台账同一粒种子（1）——于是「带技能」与「不带技能」两局只差技能这一件事。</summary>
    public const int DefaultSeed = 1;

    /// <summary>带技能的那个兵：我方剑士。</summary>
    public const string CasterUnitId = "p01";

    /// <summary>挨打的那个兵：敌方蛮牛。</summary>
    public const string DummyUnitId = "e01";

    /// <summary>我方单骑摆法：一个剑士站前排中列。</summary>
    public static readonly Formation Ours = new("duel-warrior", "单挑·我方剑士",
        new[] { new UnitPlacement(TroopKind.Warrior, 0, 2) });

    /// <summary>敌方单骑摆法：一个蛮牛站前排中列（与我方同列 ⇒ 接近只发生在深度轴上）。</summary>
    public static readonly Formation Theirs = new("duel-tank", "单挑·敌方蛮牛",
        new[] { new UnitPlacement(TroopKind.Tank, 0, 2) });

    /// <summary>
    /// 装一局。<paramref name="withRift"/> = false 就是<b>全关档</b>：世界一个字节都不碰技能层，
    /// 既有台账逐字不动 —— 这条是「接了技能会不会碰坏已验证机制」的对照校验。
    /// </summary>
    public static BattleWorld NewWorld(bool withRift = true, int seed = DefaultSeed)
    {
        var world = new BattleWorld(seed, Ours, Theirs, jitter: false);
        if (withRift) BattleAbilities.AttachRift(world, CasterUnitId);
        return world;
    }

    /// <summary>演示档的血量倍数：一局从 142 步拉到 400 步上下，「谁放了什么」才够时间在画面上看。</summary>
    public const int ShowcaseHealthScale = 3;

    /// <summary>
    /// <b>演示档</b>（出图与录像专用，与 <see cref="NewWorld"/> 只差三件事）：
    /// 两边血量各抬 <see cref="ShowcaseHealthScale"/> 倍、敌方蛮牛带一条踏地、我方剑士再带一条盾墙 ——
    /// 于是一局里两边各出手十几回，而且 <b>四类 cue 在同屏都产得出来</b>：
    /// 一次性 = 突斩与踏地命中、持续 = 盾墙在身、计数 = <c>state.guard</c> 那层、带参数 = 踏地的形状查询半径。
    /// 两个兵同框、机位特写，人眼一眼分得清（满阵那一局 30 个兵，画面糊成一团，验收找帧太贵）。
    /// 默认档一字不动：级 1 与级 2 的逐字基线吃的是 <see cref="NewWorld"/>，这本账不往它们身上摊。
    /// </summary>
    public static BattleWorld NewShowcaseWorld(int seed = DefaultSeed)
    {
        var world = NewWorld(withRift: true, seed);
        foreach (var unit in world.Units)
        {
            unit.MaxHealth *= ShowcaseHealthScale;
            unit.Health = unit.MaxHealth;
        }

        world.Abilities!.Grant(DummyUnitId, BattleAbilities.SlamAbilityId);
        world.Abilities.Grant(CasterUnitId, BattleAbilities.GuardAbilityId);
        return world;
    }

    /// <summary>
    /// 这一侧接得住的词表全名 —— <b>三层的名单在这里合成一份并集</b>（「重放装配单源」：
    /// 只留自己的动词名单，公共那半的编码与守卫走 <see cref="ReplayAssembly"/>）：
    /// <list type="bullet">
    /// <item>技能层那八条（<see cref="WorldSink.SupportedNames"/>，<c>Move</c> 不接，理由见 <see cref="WorldSink"/>）；</item>
    /// <item>建造层那两条写路径（<see cref="ConstructionVerbs.Place"/> / <see cref="ConstructionVerbs.Remove"/>）
    /// —— 赛前路障的摆与撤（三层铺设 批③ ticket 06、spec Q14）；</item>
    /// <item>摆子口那三条（<see cref="DeploymentVerbs.Names"/>，spec Q7）——
    /// 布阵期的摆 / 撤 / 挪，落点是 <see cref="BattleWorld"/> 那个写入口。</item>
    /// </list>
    /// <b>一份名单、三处问</b>：名单在这里合成一次，分发按动词路由到对应那一层（见 <see cref="Inject"/>）；
    /// 三份名单都不在这一侧另抄，抄一份就是下一次漂移的起点。
    /// 顺序沿用各层自己的表序：技能层在前，建造层居中，摆子口在后。
    /// </summary>
    public static readonly string[] VerbNames =
    [
        .. WorldSink.SupportedNames,
        ConstructionVerbs.Place,
        ConstructionVerbs.Remove,
        .. DeploymentVerbs.Names,
        // 合成输入那两条（2026-10-05 用户裁决进 Journal）：让账上读得出
        // 「玩家这一手是按哪个键按的、点了哪个坐标」。名字与形状在公共层
        // GugaFramework/Replay/SyntheticVerbs.cs 一份（形状对各原型一样），
        // 重放端一律跳过（机制见 InputInjector 类注），状态变更由紧随的逻辑写承担。
        SyntheticVerbs.Action,
        SyntheticVerbs.Click,
    ];

    /// <summary>
    /// 建造层最小集里那一条只读查询（<see cref="ConstructionVerbs.Check"/>）—— <b>注入得动、不进录</b>。
    ///
    /// <para>call 类不进录是建造层那一份表自己的口径（它不在 <c>ConstructionVerbs.InjectNames</c> 里），
    /// 巨兽那一侧同款（批① ticket 03）。它照样走同一个 inject 口，只是不入 Journal ——
    /// 「写路径进 Journal」 录的是写，录一道什么都不写的查询只会让重放端多发一条空命令。</para>
    /// </summary>
    public const string CheckPlacementVerb = ConstructionVerbs.Check;

    /// <summary>
    /// 重放三钩子（注入 / 快照 / 推进）—— <see cref="ReplayRunner"/> 与
    /// <see cref="LiveReplaySession"/> 吃的就是这份，级 1 与级 2 递的是<b>同一段代码</b>。
    /// </summary>
    public static ReplayRunner.Hooks Hooks(BattleWorld world) =>
        new ReplayAssembly(world, DefaultSeed, VerbNames, InjectorFor(world),
            target => AdvanceTo(world, target)).Hooks();

    /// <summary>
    /// 给一局 H3 装上重放装配。<b>建好不开录</b>：录不录是 <c>record_start</c> 那个口说了算
    /// （试玩前那段时间的命令不该进录），所以起点步号与起点快照由 <c>BeginSession</c> 当场抓。
    /// </summary>
    public static ReplayAssembly Recorder(BattleWorld world) =>
        new(world, world.Seed, VerbNames, InjectorFor(world), target => AdvanceTo(world, target));

    /// <summary>把这一侧的词表递进公共那半 —— 录制端与重放端消费的是同一段代码（「写路径全录」）。</summary>
    private static ReplayInject InjectorFor(BattleWorld world)
        => (string cmd, string args, out string canonical, out string reason)
            => Inject(world, cmd, args, out canonical, out reason);

    /// <summary>推进到目标步 —— 分块推进，暂停时空转、步号不动（基类那一份，循环只此一处）。</summary>
    /// <remarks>
    /// <para><b>两处曾经不同的地方，改完记在这里，别让下一个人以为是行为回退</b>：</para>
    /// <para>① <b>暂停语义</b>：这一份原来走 <c>AdvanceDebugSteps</c>，暂停中也照走 ——
    /// 那是重放钩子那一侧的语义（活局冻着时靠调试直达口跳），玩家按暂停这个动作对它没有意义。
    /// 现在与另外两个世界统一为「守暂停」。要「暂停里也跳步」走 <c>BattleWorld.AdvanceDebugSteps</c>，
    /// 它没有消失，只是不再由这一钩子默认走它。</para>
    /// <para>② <b>推的是哪一件</b>：<c>AdvanceDebugSteps</c> 只推逻辑时钟；
    /// 现在这一份走世界装配的推进，落地形与建造进度与另外两个世界一样每步都走。
    /// 这一条是这次收编顺带抹平的历史分叉，不是本 ticket 要验的东西。</para>
    /// <para>③ <b>终局后不再停在结算那一格</b>：调试直达口在 <c>phase != Fighting</c> 时返回 0，
    /// 旧循环那句 <c>if (stepped &lt;= 0) break;</c> 于是让世界停在分胜负那一格。
    /// 现在这一份会真的推到目标步，两份入库的重放基线因此各改了检查点的头部步号
    /// （「打完再读一次」152 → 160、「开打之后阵面不动」250 → 900，
    /// 「分胜负那一格」本身没动）。实体与建造读数逐字未变，只有头部步号动了。
    /// 要「分胜负就停」的话那是 <see cref="BattleWorld.AdvanceUntilEnd"/> 那一件的事，不归这一钩子。</para>
    /// </remarks>
    public static void AdvanceTo(BattleWorld world, long targetTick, int chunkTicks = 250)
    {
        // 布阵期没有「下一步」可走（时间轴是冻着的）：要往后推就先开局 —— 开局只有
        // <see cref="BattleWorld.BeginBattle"/> 那一处，这里不另造一个。
        // 整局停在布阵期的剧本（目标步 = 0）走不到这一格，所以它一个字都不动。
        if (world.Phase == BattlePhase.Deploying && targetTick > world.Tick) world.BeginBattle();

        world.AdvanceTo(targetTick, chunkTicks);
    }

    /// <summary>跑一条写路径（重放端用；不需要规范化 args 与原因时走这条）。</summary>
    public static bool Inject(BattleWorld world, string cmd, string args)
        => Inject(world, cmd, args, out _, out _);

    /// <summary>
    /// 剧本词表 —— <b>H3 这一侧只是适配器</b>：动词名、参数次序、缺省值全在各层那一份表里
    /// （技能层 <see cref="GugaFramework.Abilities.AbilityVerbs"/>，与 H2 单挑场共用，机器核对 = A68；
    /// 建造层 <see cref="ConstructionVerbs"/>；摆子口 <see cref="DeploymentVerbs"/>）。
    /// 未知命令与「这一侧不接的词」当场抛（宁可在 CLI 上炸，也不要在重放里静默少打一笔）；
    /// 参数对但表里查无这条技能/效果返回 false，因为 false 本身会进账
    /// （录制 ok=false ⇒ 重放也必须是 false，这一比就是 D3 要的东西）。
    ///
    /// <para><b>规范化口径：原样即规范。</b>技能层那八条走
    /// <see cref="GugaFramework.Abilities.AbilityVerbs.Parse"/>，而它<b>没有别名表</b> ——
    /// token 原样取（只有 <c>-</c> 与空串映射成「这一格没给」、数字解析成 double/long），
    /// 所以调用方传进来的串就是唯一那一种 token，规范化对它是个恒等变换。
    /// 另两个原型有别名集（枚举名 / 界面短名 / 旧名），那里才需要折。
    /// 这条口径由 <c>src/GugaFramework/Tests/</c> 里的契约测试钉住，不靠这份注释维持。
    /// <b>摆子口那三条不走这条恒等口径</b>：它们的兵种键认大小写，`TANK` 与 `tank` 是同一个兵种，
    /// 所以那一层把键折成唯一那种写法，<paramref name="canonicalArgs"/> 由它回填。</para>
    ///
    /// <para><b>口径的另一半：技能层那一侧会抛。</b>未知动词、这一侧不接的词、必填参数缺失，
    /// 都由技能层那份表当场抛（它的口径 2 与「桥只转发」 那句「不兜 0」），本轮不动。
    /// 公共那半的「永不抛」说的是<b>它自己</b>判否的地方；
    /// 建造层与摆子口这两条路由把参数错误接成 <c>false</c> + 原因（照 <c>ReplayAssembly</c> 口径 ③）。</para>
    /// </summary>
    public static bool Inject(
        BattleWorld world, string cmd, string args, out string canonicalArgs, out string reason)
    {
        // 规范化口径：原样即规范（见上面那段口径）。两条路由的 token 都由各层那一份表自己解析，
        // 这一侧不发明第二种拼法 —— 摆子口那三条是例外里的例外：它那一份表会把兵种键折成
        // 唯一那种写法（见 DeploymentVerbs），所以它的 canonicalArgs 由那一层回填。
        canonicalArgs = args;
        reason = string.Empty;

        // 一份名单、三处问的第二处：分发按动词路由到对应那一层。
        // 技能层那八条走 AbilityVerbs（未知动词由它当场抛），建造层那三条走 ConstructionVerbs，
        // 摆子口那三条走 DeploymentVerbs（落点是世界那个写入口）。
        if (DeploymentVerbs.IsVerb(cmd))
        {
            return DeploymentVerbs.Dispatch(new DeploymentSink(world), cmd, args, out canonicalArgs, out reason);
        }

        if (IsConstructionVerb(cmd))
        {
            return ForwardToConstruction(world, cmd, args, ref reason);
        }

        // 合成输入那两条在技能层那个兜底写入口**之前**拦下：它们不在技能层词表里，
        // 落到最后那行会被技能层当作未知动词当场抛（抛出去 = 一类录进去的东西重放不了）。
        if (cmd == SyntheticVerbs.Action || cmd == SyntheticVerbs.Click)
        {
            var segments = cmd == SyntheticVerbs.Action
                ? SyntheticVerbs.ActionSegments
                : SyntheticVerbs.ClickSegments;
            if (SyntheticVerbs.Skip(args, segments, out var syntheticArgs, out reason))
            {
                canonicalArgs = syntheticArgs;
                return true;
            }

            return false;
        }

        var ok = GugaFramework.Abilities.AbilityVerbs.Dispatch(new WorldSink(world), cmd, args);
        reason = string.Empty; // 抛或不返两种：返 false 时表里查无这条技能/效果，细节去问技能层
        return ok;
    }

    /// <summary>
    /// 摆子口的落点：三条命令各转发到 <see cref="BattleWorld"/> 那个写入口
    /// （<see cref="BattleWorld.PlaceUnit"/> / <see cref="BattleWorld.RemoveUnit"/> /
    /// <see cref="BattleWorld.MoveUnit"/>）—— 只转发，不在这里另写一套摆子规则。
    /// </summary>
    private sealed class DeploymentSink : IDeploymentVerbSink
    {
        private readonly BattleWorld _world;

        public DeploymentSink(BattleWorld world)
        {
            _world = world;
        }

        public PlacementOutcome PlaceUnit(TroopKind kind, int row, int column)
            => _world.PlaceUnit(kind, row, column);

        public PlacementOutcome RemoveUnit(int row, int column) => _world.RemoveUnit(row, column);

        public PlacementOutcome MoveUnit(int fromRow, int fromColumn, int toRow, int toColumn)
            => _world.MoveUnit(fromRow, fromColumn, toRow, toColumn);
    }

    /// <summary>这一条属于建造层那一份词表吗（放置 / 移除两条写路径 + 一条只读裁决）。</summary>
    private static bool IsConstructionVerb(string cmd)
        => cmd is ConstructionVerbs.Place or ConstructionVerbs.Remove or CheckPlacementVerb;

    /// <summary>
    /// 薄转发：args 交给建造层那一份动词表（参数次序、缺省值、什么时候抛都只有那一处），
    /// 这一侧只把异常接成 <c>false</c> + 原因 —— 抛出去会让「录进去的东西」有一类重放不了。
    ///
    /// <para><b>失败要带得出原因</b>：参数写错与「世界不答应」是两类完全不同的错，
    /// 两者回同一个 <c>false</c> 时只能去读代码才知道是哪个（巨兽那一侧 2026-09-30 实测踩过）。
    /// 原因<b>不进 Journal</b>，给当次调用看（<c>ok</c> 那一列才是重放要比的东西）。</para>
    /// </summary>
    private static bool ForwardToConstruction(BattleWorld world, string verb, string args, ref string reason)
    {
        try
        {
            var outcome = ConstructionVerbs.Dispatch(new BattleConstructionSink(world), verb, args);
            if (outcome.Ok) return true;

            // 前置条件是原型自己加的那一条（建造模块不认识「布阵期」这个词）：
            // 拒在它上面时念那句人话，别让读的人去猜 InvalidState 是什么意思。
            if (!world.CanPlaceRoadblock(out var why))
            {
                reason = why;
                return false;
            }

            reason = $"{verb} 被建造模块拒了（{outcome.Text}）";
            return false;
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException
                                      or NotSupportedException or ArgumentException or OverflowException)
        {
            reason = $"{verb} 这一发没打成（{ex.Message}）";
            return false;
        }
    }

    /// <summary>
    /// H3 的落点：命令经 <see cref="BattleAbilities"/> 那几个口子打进技能层。
    /// <b>九条里这一侧接八条</b> —— <see cref="GugaFramework.Abilities.AbilityVerbs.Move"/> 不接：
    /// 这一侧单位的走位由世界自己的 S5 演化出来（谁在往哪打、分离推挤），位置不是调试口能塞进去的一笔；
    /// 要让位置成为<b>技能的输出</b>，走 <c>CastAbility</c> 的第 4 段目的地（A66 那本冲锋台账的形状）。
    /// 这一格不适用必须在检查清单里写明理由，不许静默少一条 —— 所以这一侧把它挡在<b>词表名单</b>外面，
    /// 而不是接了再空转。
    /// </summary>
    private sealed class WorldSink : GugaFramework.Abilities.IAbilityVerbSink
    {
        internal static readonly string[] SupportedNames =
        [
            GugaFramework.Abilities.AbilityVerbs.Cast,
            GugaFramework.Abilities.AbilityVerbs.Cancel,
            GugaFramework.Abilities.AbilityVerbs.ApplyEffect,
            GugaFramework.Abilities.AbilityVerbs.RemoveEffect,
            GugaFramework.Abilities.AbilityVerbs.SetAttribute,
            GugaFramework.Abilities.AbilityVerbs.SetBase,
            GugaFramework.Abilities.AbilityVerbs.ClearCooldown,
            GugaFramework.Abilities.AbilityVerbs.SetStepsRemaining,
        ];

        private static readonly IReadOnlySet<string> SupportedVerbSet =
            new HashSet<string>(SupportedNames, StringComparer.Ordinal);

        private readonly BattleWorld _world;

        public WorldSink(BattleWorld world)
        {
            _world = world;
        }

        public string HostLabel => "H3 排兵布阵真局";

        public IReadOnlySet<string> SupportedVerbs => SupportedVerbSet;

        /// <summary>接：位移的起点与目的地都是深度轴坐标（见 <see cref="BattleAbilities"/> 抬头边界 1）。</summary>
        public bool AcceptsCastDestination => true;

        public bool Cast(string unitId, string abilityId, string? targetId, (double FromX, double ToX)? move)
            => Abilities().TryActivate(unitId, abilityId, targetId, move);

        public bool Cancel(string unitId, string abilityId) => Abilities().TryCancel(unitId, abilityId);

        public void ApplyEffect(string targetId, string effectId, long? durationTicks, string? sourceId)
            => Abilities().ApplyEffect(targetId, effectId, durationTicks, sourceId);

        public void RemoveEffect(string targetId, string effectId, int stacks)
            => Abilities().RemoveEffect(targetId, effectId, stacks);

        public void SetAttribute(string targetId, string attribute, double value, string mode,
            string[]? requirementTags)
            => Abilities().SetAttribute(targetId, attribute, mode, value, requirementTags);

        public void SetBase(string targetId, string attribute, double value)
            => Abilities().SetBase(targetId, attribute, value);

        public void ClearCooldown(string unitId, string abilityId)
            => Abilities().ClearCooldown(unitId, abilityId);

        public bool SetStepsRemaining(string unitId, string abilityId, long steps)
            => Abilities().SetStepsRemaining(unitId, abilityId, steps);

        public bool Move(string unitId, double dx)
            => throw new InvalidOperationException(
                "H3 这一侧的走位归世界演化，词表不接 Move（要位移用 CastAbility 的第 4 段目的地）");

        /// <summary>全关档（没接技能层）时任何命令都无处可打 —— 这句话留在这一处，别抄进每个动词。</summary>
        private BattleAbilities Abilities()
            => _world.Abilities ?? throw new InvalidOperationException(
                "这一局没接技能层（全关档），命令无处可打");
    }

    /// <summary>这一发命令打完世界走到第几步（录制端与取证器都要读它）。</summary>
    public static long TickOf(BattleWorld world) => world.Tick;

    /// <summary>一发读出全量真值（录制端用；与重放端 <see cref="Hooks"/> 里的 Snapshot 同一份）。</summary>
    public static string SnapshotOf(BattleWorld world) => world.ReplaySnapshot();
}
