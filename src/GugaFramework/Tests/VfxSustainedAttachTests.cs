#nullable enable
using System;
using System.Collections.Generic;
using DemoFactory.GugaFramework.Abilities;
using DemoFactory.GugaFramework.Vfx;
using Shouldly;
using Xunit;

namespace DemoFactory.GugaFramework.Tests;

/// <summary>
/// 持续档「挂上 / 跟着走 / 撤下」那几条命题的<b>离线那半</b>（ticket 05）。
///
/// <para><b>这里测的是顺序，不是引擎</b>：<c>VfxPlayer</c> 吃引擎类型（建节点、摆位、开发射、
/// 清粒子），离线进程里一件都跑不了（契约「测试与实测分工」）。所以引擎那半每次挂上/撤下走的
/// <b>借与还顺序在 <see cref="VfxPlayerTranscript"/> 里逐句转写一份</b>：
/// 借 → 翻空闲栈或建节点 → 摆位 → 开发射；撤下 → 清空 → 停发射 → 推空闲栈 → 还账。
/// <b>转写与引擎那半对着看才有意义</b>：顺序两边不一样就有一处漂，所以改动要两边一起动。
/// 「节点实例数」「画面上有没有残留」那两半离线断不了 —— 那真机验。</para>
///
/// <para><b>那份转写三档共用一份</b>（<c>VfxPlayerTranscript.cs</c>）：一次性档
/// （带参数档那一档也走它，ticket 06）、持续档、倍率都在同一个类里。
/// 按档各抄一份的话，抄一份漂一处就有一档的槽位收不回来。</para>
///
/// <para><b>账与查表用真件</b>：<see cref="VfxLedger"/> 与 <see cref="VfxSpec"/> 都是纯 C#，
/// 链进本测试项目，所以「借不借得出」「漏登计不计」断言的是真实现；
/// 只有「拿哪个节点、节点建了几个、参数落在谁身上」是转写出来的。</para>
///
/// <para>断言口径：借不借得出只判布尔与计数，不重抄 <c>Readout()</c> 的逐字格式
/// （那份逐字由 <c>VfxLedgerTests</c> 钉着，重复一遍只会两处一起漂）。</para>
/// </summary>
public sealed class VfxSustainedAttachTests
{
    private const string Unit = "u1";

    private const string Unit2 = "u2";

    private const string Unit3 = "u3";

    private const string Track = "guard";

    // ============================ 1. 挂上 → 撤下 → 再挂上 ============================

    [Fact]
    public void 同一轨挂上撤下再挂上_借出归还再借出_在池数来回一致()
    {
        // 池容量 1：这一条只有「还回来再借得出」才走得通，所以它是持续档那条循环的最小闭环。
        var fx = new VfxPlayerTranscript(capacity: 1);

        fx.Sustain(Cue(1)).ShouldBeTrue("挂上：借得出");
        fx.Instances.ShouldBe(1, "第一次挂上建一个节点");
        fx.Ledger.InPool.ShouldBe(0, "借走那一条，池里空了");
        fx.Ledger.Alive.ShouldBe(1, "挂着一条");

        fx.Sustain(Cue(0)).ShouldBeTrue("撤下：本来挂着");
        fx.Instances.ShouldBe(1, "撤下不建新节点");
        fx.Ledger.InPool.ShouldBe(1, "还回来了，池回到满的那一份");
        fx.Ledger.Alive.ShouldBe(0, "没有一条还悬在外面");

        fx.Sustain(Cue(1)).ShouldBeTrue("再挂上：借得出（上一条已经还回来了）");
        fx.Instances.ShouldBe(1, "复用的是同一个节点：来回一次实例数不涨");
        fx.Ledger.InPool.ShouldBe(0);
        fx.Ledger.Alive.ShouldBe(1);

        fx.Ledger.Played.ShouldBe(2, "挂上两次 = 借出两次");
        fx.Ledger.Returned.ShouldBe(1, "中间那一次撤下还了账");
        fx.Ledger.BorrowFailed.ShouldBe(0, "容量 1 就够：这一条是借了又还，不是同时占着两条");
    }

    // ============================ 2. 同一条挂上两次 ============================

    [Fact]
    public void 同一条挂上两次而中间没撤下_第二次不重复建节点()
    {
        // 不重复建节点这一条不是为了省节点，是为了画面上正确：一个单位身上挂两个护盾，
        // 读起来是「这个单位有两个护盾」，而它其实只有一个。
        var fx = new VfxPlayerTranscript(capacity: 4);
        fx.RegisterEffect("aura", "res://vfx/FxGuardWall.tscn", oneShot: false);

        fx.Sustain(Cue(1)).ShouldBeTrue();
        var first = fx.NodeOf(Unit, Track);

        fx.Sustain(Cue(1)).ShouldBeTrue("第二次挂上：已经挂着就答挂上了");
        fx.Instances.ShouldBe(1, "一个键一个节点：不新建");
        fx.NodeOf(Unit, Track).ShouldBeSameAs(first, "第二次挂的还是同一个节点");
        fx.Ledger.Alive.ShouldBe(1, "占着一个池位，不是两个");
        fx.Ledger.Played.ShouldBe(1, "没多借一次");

        // 换一条轨就是另一个键：那是两件事，该建第二个节点。
        fx.Sustain(Cue(1, track: "aura")).ShouldBeTrue("换一条轨是另一个键");
        fx.Instances.ShouldBe(2, "单位/轨 那个键认两个值：同轨同键、异轨异键");
        fx.Ledger.Alive.ShouldBe(2);
    }

    // ============================ 3. 容量推导 ============================

    [Fact]
    public void 挂到池上限再挂一条_借不出加一_不新建节点()
    {
        // 容量从 CueFeedConfig.PendingPerTrack 推导（spec D11），这里给 2 便于数。
        // 三条挂在同一条轨上：空闲栈是按轨分的，跨轨复用在引擎那半不存在，所以同轨才量得到「复用」。
        var fx = new VfxPlayerTranscript(new CueFeedConfig { PendingPerTrack = 2 });

        fx.Sustain(Cue(1, unit: Unit)).ShouldBeTrue();
        fx.Sustain(Cue(1, unit: Unit2)).ShouldBeTrue();
        fx.Ledger.InPool.ShouldBe(0, "两条都借走了");
        fx.Instances.ShouldBe(2);

        fx.Sustain(Cue(1, unit: Unit3)).ShouldBeFalse("池空了：当场丢，不排队（spec D10）");
        fx.Ledger.BorrowFailed.ShouldBe(1, "丢的那一条要在账上");
        fx.Instances.ShouldBe(2, "借不出就不新建节点 —— 临时新建会让峰值节点数失控");
        fx.Attached.ShouldBe(2, "u3 没挂上");
        fx.Ledger.Alive.ShouldBe(2, "账没悬着");

        // 空出来之后仍然借得出来，而且复用的是还回来的那一个节点。
        fx.Sustain(Cue(0, unit: Unit)).ShouldBeTrue();
        fx.Sustain(Cue(1, unit: Unit3)).ShouldBeTrue("还回来一条就借得出了");
        fx.Ledger.BorrowFailed.ShouldBe(1, "借不出那一回不会被后来的成功抹掉");
        fx.Instances.ShouldBe(2, "复用的是还回来的那一个节点");
        fx.Attached.ShouldBe(2);
    }

    // ============================ 每帧同步位置 ============================

    [Fact]
    public void 每帧同步_位置与朝向跟着宿主那份读数走()
    {
        var fx = new VfxPlayerTranscript(capacity: 4);
        fx.Sustain(Cue(1)).ShouldBeTrue();
        var node = fx.NodeOf(Unit, Track);
        node.X.ShouldBe(-1.2, "挂上那一刻按宿主那份读数摆位");
        node.Z.ShouldBe(0.0);
        node.Facing.ShouldBe(0.0);

        fx.MoveHost(1.4, 1.1, 35.0);   // 宿主那边挪了单位
        fx.Sync();                    // 表现层每帧做的事
        node.X.ShouldBe(1.4, "跟着挪");
        node.Z.ShouldBe(1.1);
        node.Facing.ShouldBe(35.0, "朝向也一起同步");
        node.Y.ShouldBe(0.0, "摆位是二维的：护盾那类效果都落在脚下");
        fx.Ledger.Played.ShouldBe(1, "跟着走不借池位：还是那一条");
        fx.Instances.ShouldBe(1);
    }

    [Fact]
    public void 宿主没挪_同步一遍位置不动_池账也不动()
    {
        var fx = new VfxPlayerTranscript(capacity: 4);
        fx.Sustain(Cue(1)).ShouldBeTrue();
        var node = fx.NodeOf(Unit, Track);
        var after = fx.Ledger.Readout();

        fx.Sync();
        fx.Sync();
        fx.Sync();
        node.X.ShouldBe(-1.2, "读数没变，位置就不变");
        fx.Ledger.Readout().ShouldBe(after, "每帧同步不碰池账：反复调不累积");
    }

    // ============================ 撤下即硬切 ============================

    [Fact]
    public void 撤下即硬切_停发射且当场清掉还活着的粒子()
    {
        var fx = new VfxPlayerTranscript(capacity: 4);
        fx.Sustain(Cue(1)).ShouldBeTrue();
        var node = fx.NodeOf(Unit, Track);
        node.Emitting.ShouldBeTrue("挂上就开着循环发射");
        node.LiveParticles.ShouldBe(8, "循环发射：此刻有活着的粒子");

        fx.Sustain(Cue(0)).ShouldBeTrue();
        node.Emitting.ShouldBeFalse("硬切第一格：停发射");
        node.LiveParticles.ShouldBe(0, "硬切第二格：还活着的粒子当场清掉，不让它们再活满一个 lifetime");
        node.Restarts.ShouldBe(2, "撤下走一次 Restart 清空（它会把 emitting 置真，所以紧跟着要写 Emitting = false）");
    }

    [Fact]
    public void 撤下没挂着的那条_答没撤_不动账()
    {
        var fx = new VfxPlayerTranscript(capacity: 4);

        fx.Sustain(Cue(0)).ShouldBeFalse("本来就没挂着，没什么可撤");
        fx.Sustain(Cue(1)).ShouldBeTrue();
        fx.Sustain(Cue(0)).ShouldBeTrue();
        fx.Sustain(Cue(0)).ShouldBeFalse("撤两次：第二次没得撤");
        fx.Ledger.Returned.ShouldBe(1, "只还了一次账");
        fx.Ledger.InPool.ShouldBe(4, "池回到满的那一份");
    }

    // ============================ 宿主那边没这个单位 ============================

    [Fact]
    public void 宿主说没这个单位_挂着的那条当场撤下_池位还回来()
    {
        // 不撤的话池位永远悬着，最后表现成「借不出」那个数莫名往上涨。
        var fx = new VfxPlayerTranscript(capacity: 1);
        fx.Sustain(Cue(1)).ShouldBeTrue();
        fx.Ledger.InPool.ShouldBe(0);

        fx.ForgetHostUnit();
        fx.Sync();
        fx.Attached.ShouldBe(0, "宿主那边没这个单位 = 撤下");
        fx.NodeIdleOf(Track).ShouldBe(1, "硬切之后那个节点回空闲栈");
        fx.Ledger.InPool.ShouldBe(1, "池位还回来了");
        fx.Ledger.Alive.ShouldBe(0);
        fx.LostUnits.ShouldBe(1, "这一回要读得出来");
        fx.Instances.ShouldBe(1, "硬切不是新建");
    }

    [Fact]
    public void 宿主压根没递查询_挂不上也不同步_不把所有挂上的一次清空()
    {
        // 「宿主说这个单位没了」与「宿主没递查询」是两回事：后者是接线漏了，
        // 按前者处理会把所有挂上一次性清空，而画面上看着像「效果自己结束了」。
        var fx = new VfxPlayerTranscript(capacity: 4);
        fx.Sustain(Cue(1)).ShouldBeTrue();
        fx.Ledger.InPool.ShouldBe(3);

        fx.GiveUpQuery();
        fx.Sync();
        fx.Attached.ShouldBe(1, "没递查询时不同步，更不撤");
        fx.LostUnits.ShouldBe(0, "那不是「宿主说这个单位没了」");
        fx.Ledger.Alive.ShouldBe(1, "账没动");

        fx.Sustain(Cue(1, unit: Unit2)).ShouldBeFalse("没递查询就没有位置可摆");
        fx.Ledger.BorrowFailed.ShouldBe(0, "挂不上是接线漏了，不进池账（账只记取舍）");
        fx.Ledger.InPool.ShouldBe(3, "没借没还");
    }

    // ============================ 档位对不上就是误用 ============================

    [Fact]
    public void 一次性资源拿去挂持续档_当场拒_不占池位()
    {
        // FxHit / FxShapeRing 那种 one_shot = true 的资源：挂上去演一遍就没了，等于什么都没有。
        var fx = new VfxPlayerTranscript(capacity: 4);
        fx.RegisterEffect("oneshot", "res://vfx/FxHit.tscn", oneShot: true);

        fx.Sustain(Cue(1, track: "oneshot")).ShouldBeFalse("档位对不上，当场拒");
        fx.Ledger.BorrowFailed.ShouldBe(0, "拒不是借不出：借出去了却摆不下，当场还回去，账不悬着");
        fx.Ledger.InPool.ShouldBe(4, "池位还回来了");
        fx.Attached.ShouldBe(0);
        fx.Instances.ShouldBe(0, "没借出就没建节点");
    }

    [Fact]
    public void 轨名没登_挂不上_漏登在表上读得出来()
    {
        var fx = new VfxPlayerTranscript(capacity: 4);

        fx.Sustain(Cue(1, track: "没登过")).ShouldBeFalse("查不到轨名");
        fx.Spec.Unregistered.ShouldBe(1, "漏登要读得出来：日志里没有不等于没发生");
        fx.Ledger.BorrowFailed.ShouldBe(0, "查表在借池位之前，所以没借也没丢");
        fx.Ledger.InPool.ShouldBe(4);
    }

    private static CueRow Cue(double value, string unit = Unit, string track = Track)
        => new(1, CueKind.Sustained, unit, track, "护盾", value);
}
