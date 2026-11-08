#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DemoFactory.GugaFramework.Abilities;
using DemoFactory.Prototypes.BattleArray.Logic;
using Shouldly;
using Xunit;

namespace BattleArray.Tests;

/// <summary>
/// 技能层第二批落在 H3（排兵布阵真 1v1）的那两格 —— <c>tank.slam</c> 的正前 120° 与
/// <c>rider.charge</c> 的位移。用户确认：这两本的<b>数值台账在真世界那一侧</b>，
/// 因为单挑场只有一根直线（扇形退化）、也不让位置当技能的输出。
///
/// 两条边界跟着确认走，写在这儿免得下一位再问：
/// - <b>尺子在公共层</b>：扇形那把尺是 <see cref="AbilityStepper.InsideSector"/>（界内界外两侧 = A39），
///   H3 这侧只补台账数字与「谁被选中 / 为什么空」的读数，原型不另判一遍（N-14）。
/// - <b>坐标的写归宿主</b>：层只登记 <c>Pending.Move</c> 并写自己那根一维镜像（单挑场 12 节 待确认项 5 已确认），
///   世界的 <c>Position.Y</c> 由 <see cref="BattleAbilities"/> 在阶段 0 落笔。
///
/// 几何的取法：不靠纸面猜摆位走到第几步会在几米——先读世界自己的 <c>PositionFull</c>，
/// 期望值按公共层那条算式（距离、曲线）从读数推出来。这样每一步都手算得出来，
/// 红了也能一口说出差在哪一步、差在哪一笔（与 <see cref="AbilityH3DuelTests"/> 的残血那一刀同一口径）。
/// </summary>
public class AbilityH3SlamChargeTests
{
    private static readonly string Caster = DuelRiftSetup.CasterUnitId;   // p01
    private static readonly string Dummy = DuelRiftSetup.DummyUnitId;     // e01

    /// <summary>单挑摆法，可选兵种与列号（列号决定横向偏移：每格 2 米）。</summary>
    private static BattleWorld Duel(TroopKind ours, TroopKind theirs, int ourColumn = 2,
        int theirColumn = 2)
    {
        var world = new BattleWorld(DuelRiftSetup.DefaultSeed,
            new Formation("shape-ours", "形状·我方", new[] { new UnitPlacement(ours, 0, ourColumn) }),
            new Formation("shape-theirs", "形状·敌方", new[] { new UnitPlacement(theirs, 0, theirColumn) }),
            jitter: false);
        return world;
    }

    private static BattleUnit At(BattleWorld world, string id) => world.Units.Single(u => u.Id == id);

    private static double Distance(BattleWorld world)
        => (At(world, Caster).Position - At(world, Dummy).Position).Length();

    /// <summary>推到两人近身（距离进 3 米内）为止，最多走 200 步。</summary>
    private static void CloseIn(BattleWorld world, double withinMeters = 3)
        => AdvanceUntil(world, withinMeters, 0);

    /// <summary>
    /// 推到距离落进 <c>(至少, 至多]</c> 这一档为止——几何不靠纸面猜（两人同时走、还有分离推挤），
    /// 靠世界自己的读数把每一格停在哪儿说清楚。
    /// </summary>
    private static void AdvanceUntil(BattleWorld world, double atMost, double atLeast)
    {
        var guard = 0;
        while ((Distance(world) > atMost || Distance(world) <= atLeast) && guard++ < 400)
        {
            world.AdvanceDebugSteps(1);
        }

        guard.ShouldBeLessThan(400, $"四百步内没走到想要的这一档。{Where(world)}");
    }

    /// <summary>把两个兵的坐标原样念进失败消息（几何这件事要看得见）。</summary>
    private static string Where(BattleWorld world)
        => $"{Caster}={At(world, Caster).PositionFull} {Dummy}={At(world, Dummy).PositionFull} " +
           $"距离={Distance(world):0.###}";

    // === tank.slam：正前 120° / 3 米那把尺，在真世界里各跑越界与不越界一次 ===

    /// <summary>
    /// <b>A64（slam 的命中侧台账）</b>：同列对打（横向偏移 0）、距离走近身后（≤3 米）按下 →
    /// 前摇 2 步、第 3 步过 commit → 形状查询选中敌方那一个兵 → 它身上多一条 30 步的减速、
    /// 世界读到的移速倍率 = 0.6（<c>move_speed</c> 在 H3 是倍率口径）。
    /// 候选集只有 1v1 这一个人，「集合大小」那一格不在这里硬凑。
    /// </summary>
    [Fact]
    public void A64_踏地命中侧_近身那个兵被减速三十步()
    {
        var world = Duel(TroopKind.Tank, TroopKind.Tank);
        var abilities = BattleAbilities.AttachRift(world, Caster);
        abilities.Grant(Caster, BattleAbilities.SlamAbilityId);
        CloseIn(world);
        var at = Distance(world);
        at.ShouldBeLessThanOrEqualTo(BattleAbilities.SlamRadiusMeters, Where(world));

        DuelRiftSetup.Inject(world, "CastAbility", $"{Caster}|{BattleAbilities.SlamAbilityId}|{Dummy}").ShouldBeTrue("入口射程 6 米放行");
        world.AdvanceDebugSteps((int)BattleAbilities.SlamPreSteps + 1);   // 前摇走完 + 过 commit 那一格

        abilities.EventsWhere<ShapeHitLanded>().Any(s =>
                s.Radius == BattleAbilities.SlamRadiusMeters
                && s.ConeDegrees == BattleAbilities.SlamConeDegrees
                && s.HitUnitIds.Contains(Dummy)).ShouldBeTrue($"命中侧：形状 slam_shape（ShapeHitLanded）的字段念得出 r={BattleAbilities.SlamRadiusMeters} " +
            $"cone={BattleAbilities.SlamConeDegrees} 与命中名单（含 {Dummy}）。" + Where(world));
        abilities.Snapshot().ShouldContain($"f;{BattleAbilities.SlamSlowEffectId};1;", Case.Sensitive, "减速那条效果真落到了目标身上");
        abilities.SpeedScale(Dummy).ShouldBe(BattleAbilities.SlamSlowMult, "世界读到的移速倍率 = 0.6（清单页 slam 那一格）");
    }

    /// <summary>
    /// <b>A65（slam 的越界侧台账）</b>：错两列开局（横向偏移 4 米）⇒ 在还没走拢之前按下——
    /// 入口射程 6 米放行、形状半径 3 米拒收，于是 <c>hits=-</c>（空也读得出为什么空），
    /// 目标身上一条效果都没有、移速倍率照旧 1.0。
    /// 这一条与 A64 合起来就是覆盖矩阵那行「越界与不越界两侧各跑一次」；
    /// 角度那一关的两侧由公共层的 A39 钉着（直线几何上扇形退化，H2 也没给朝向开第六档参数）。
    /// </summary>
    [Fact]
    public void A65_踏地越界侧_入口放行形状拒收且念得出空()
    {
        var world = Duel(TroopKind.Tank, TroopKind.Tank, ourColumn: 2, theirColumn: 0);
        var abilities = BattleAbilities.AttachRift(world, Caster);
        abilities.Grant(Caster, BattleAbilities.SlamAbilityId);
        AdvanceUntil(world, atMost: 6, atLeast: BattleAbilities.SlamRadiusMeters);
        var at = Distance(world);
        at.ShouldBeGreaterThan(BattleAbilities.SlamRadiusMeters, "这一格要的就是越界侧。" + Where(world));
        at.ShouldBeLessThanOrEqualTo(6, "但仍在入口射程内（否则这一发压根放不出来）");

        DuelRiftSetup.Inject(world, "CastAbility", $"{Caster}|{BattleAbilities.SlamAbilityId}|{Dummy}").ShouldBeTrue();
        world.AdvanceDebugSteps((int)BattleAbilities.SlamPreSteps + 1);

        abilities.Count<ShapeHitMissed>().ShouldBeGreaterThan(0, "打空也要在账上：形状 slam_shape 的打空档自立成 ShapeHitMissed（旧文本 hits=- 那一档），空集也念得出。" + Where(world));
        abilities.Count<ShapeHitLanded>().ShouldBe(0, "越界侧不许同时留一条命中的形状（一处实现只准一个答案）");
        abilities.Snapshot().ShouldNotContain(BattleAbilities.SlamSlowEffectId, Case.Sensitive, "没选中就没有效果");
        abilities.SpeedScale(Dummy).ShouldBe(1.0);
    }

    // === rider.charge：位置第一次成为技能的输出 ===

    /// <summary>
    /// <b>A66（charge 的到位那一步 + 按已行走距离的曲线）</b>：第 k 步记下骑手的深度坐标 y，
    /// 按下冲锋并递进目的地 <c>y − 3</c>（往敌侧走 3 米）⇒ 第 3 步到位：
    /// 层那一根镜像坐标逐字等于目的地、世界的 <c>Position.Y</c> 也被宿主写到位移后的值、
    /// 伤害 = 攻击力 24 × 曲线(已行走 3 米 = 0.875) = 21（24 × 0.875 = 21，四舍五入到整数）。
    /// 「到位那一步」与「伤害落地」同一步——这就是 Pending 带位移载荷的意义。
    /// </summary>
    [Fact]
    public void A66_冲锋走满三步到位_伤害按已行走距离查曲线()
    {
        var world = Duel(TroopKind.Rider, TroopKind.Tank);
        var abilities = BattleAbilities.AttachRift(world, Caster);
        abilities.Grant(Caster, BattleAbilities.ChargeAbilityId);
        world.AdvanceDebugSteps(4);   // 上路了，但还没近身（位移是技能的输出，不必挨着才冲锋）

        var from = At(world, Caster).Position.Y;
        var to = from - 3.0;
        DuelRiftSetup.Inject(world, "CastAbility",
            $"{Caster}|{BattleAbilities.ChargeAbilityId}|{Dummy}|" +
            $"{from.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}>" +
            $"{to.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}").ShouldBeTrue("带位移（从>到）的按下走的是同一个入口");

        var before = At(world, Caster).Position.Y;

        // 先在<b>路上</b>那一格读：Pending 落地之后它就从快照里消失了，读不到这一笔。
        world.AdvanceDebugSteps(1);   // 过 commit 那一步（前摇 0 步也要占一步）
        var inFlight = abilities.Snapshot();
        inFlight.ShouldContain($"move={AbilitySnapshot.Format(from)}>{AbilitySnapshot.Format(to)}", Case.Sensitive, "在路上的那一条 Pending 念得出位移载荷（快照读不回来就是丢了一笔在路上）。" + Where(world));
        abilities.Find(Caster)!.Counters.GetValueOrDefault("damage_dealt").ShouldBe(0, "路上还不结算（到位那一步才落）");

        // 这一列要能被<b>读回来</b>（写得出只算半件事）：把快照交给 Load，位移载荷还在原主的那条 Pending 上。
        // 逐字往返那一格由 H2 的 A51 管（那里有步进器可以重建），这一格只管新列本身。
        var loaded = AbilitySnapshot.Load(inFlight);
        var moved = loaded.Units[Caster].Pendings.Single().Move;
        moved.ShouldNotBeNull("位移载荷读不回来 = 恢复之后那一步永远走不到位");
        moved!.Value.FromX.ShouldBe(from, 0.001);
        moved.Value.ToX.ShouldBe(to, 0.001);

        world.AdvanceDebugSteps((int)BattleAbilities.ChargeArriveSteps);   // 到位那一步 = commit + 3
        var travelled = Math.Abs(to - from);
        var expected = (int)Math.Round(At(world, Caster).Card.Attack
            * CurveAt(BattleAbilities.ChargeTravelledCurve, travelled), MidpointRounding.AwayFromZero);

        ((double)travelled).ShouldBe(3.0, 0.001, "按下到落地的意图就是走 3 米。" + Where(world));
        expected.ShouldBe(21, "24 × 曲线(3 米 = 0.875) = 21");
        abilities.Snapshot().ShouldNotContain("move=", Case.Sensitive, "到位之后这条待决就清掉了（不留一笔幽灵位移）");
        abilities.EventsTail(60).Any(e => e.Subject == Caster && e.Payload is UnitMoved).ShouldBeTrue("到位那一步发得出事件（形状 move / UnitMoved，主体是施法者）");
        abilities.Snapshot().ShouldContain($"c;damage_dealt;{expected}", Case.Sensitive, "层的账上就是这一撞（技能伤害不进平A那本账）");
        ((double)At(world, Caster).Position.Y).ShouldBe((float)to, 0.35, "世界坐标被宿主写到位移后的值（±同一步世界自己的走位）。" + Where(world));
        At(world, Caster).Position.Y.ShouldBeLessThan(before, "往敌侧走 ⇒ 深度变小");
    }

    /// <summary>
    /// <b>A67（曲线真的按已行走距离折，不是按目标距离）</b>：同一局、同一个骑手，只把目的地从 3 米改成 1 米 ⇒
    /// 伤害从 21 掉到 15（24 × 曲线(1 米 = 0.625) = 15）。两局唯一的差别就是那一格里走了多远。
    /// </summary>
    [Fact]
    public void A67_只改目的地距离_伤害照着曲线掉()
    {
        var far = ChargeBy(3.0, out var farDamage);
        var near = ChargeBy(1.0, out var nearDamage);

        farDamage.ShouldBe(21, $"走 3 米那一撞：期望 21 = 24 × 曲线(3 米 = 0.875)。{far}");
        nearDamage.ShouldBe(15, $"走 1 米那一撞：期望 15 = 24 × 曲线(1 米 = 0.625)。{near}");
    }

    /// <summary>把冲锋跑一遍并取出层账上的那一撞（<see cref="A67"/> 的两个用例共用）。</summary>
    private static string ChargeBy(double metres, out int damage)
    {
        var world = Duel(TroopKind.Rider, TroopKind.Tank);
        var abilities = BattleAbilities.AttachRift(world, Caster);
        abilities.Grant(Caster, BattleAbilities.ChargeAbilityId);
        world.AdvanceDebugSteps(4);

        var from = At(world, Caster).Position.Y;
        var to = from - metres;
        // 位移的起点与目的地都由宿主递（第 4 段「从>到」）——层里不再有坐标，算不出起点。
        DuelRiftSetup.Inject(world, "CastAbility", $"{Caster}|{BattleAbilities.ChargeAbilityId}|{Dummy}|" +
            $"{from.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}>" +
            $"{to.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}");
        world.AdvanceDebugSteps((int)BattleAbilities.ChargeArriveSteps + 1);   // commit 占一步，到位在那之后 3 步
        var bag = abilities.Find(Caster)!;
        damage = (int)bag.Counters.GetValueOrDefault("damage_dealt");
        return $"{Caster}={At(world, Caster).PositionFull} 按下前 y={from:0.###} 目的地={to:0.###} " +
               $"曲线={CurveAt(BattleAbilities.ChargeTravelledCurve, metres):0.###} 层账={damage}";
    }

    /// <summary>折线线性插值、端点外夹紧——与公共层那把尺同一口径（这里只为把手算式写在测试里）。</summary>
    private static double CurveAt(double[] xy, double x)
    {
        var count = xy.Length / 2;
        if (x <= xy[0]) return xy[1];
        if (x >= xy[(count - 1) * 2]) return xy[(count - 1) * 2 + 1];
        for (var i = 1; i < count; i++)
        {
            if (x > xy[i * 2]) continue;
            var (x0, y0) = (xy[(i - 1) * 2], xy[(i - 1) * 2 + 1]);
            var (x1, y1) = (xy[i * 2], xy[i * 2 + 1]);
            return y0 + (y1 - y0) * ((x - x0) / (x1 - x0));
        }

        return xy[(count - 1) * 2 + 1];
    }
}
