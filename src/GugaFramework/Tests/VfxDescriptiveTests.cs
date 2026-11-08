#nullable enable
using DemoFactory.GugaFramework.Abilities;
using Shouldly;
using Xunit;

namespace DemoFactory.GugaFramework.Tests;

/// <summary>
/// 带参数档（<c>CueKind.Descriptive</c>）<b>走一次性档那条路</b>的离线那半（ticket 06）。
///
/// <para><b>为什么改道</b>：ticket 05 撞到的那个冲突是真冲突 —— 唯一一份 Descriptive 资源
/// <c>FxShapeRing</c> 是 <c>one_shot = true</c>，而当时 <c>Describe</c> 走的是持续档那条路
/// （要 <c>one_shot = false</c>），所以那张资源当场被拒，真机那半没验成。
/// 用户裁决 Descriptive 改走一次性档：画面由半径、角度、进度那几个标量决定的东西是
/// <b>画一个范围</b>，那一次显现就该演一遍，不像护盾那样挂上就在。</para>
///
/// <para><b>这里测的是顺序，不是引擎</b>：<see cref="VfxPlayerTranscript"/> 把引擎那半
/// <c>Describe</c> 走的次序逐句转写了一份（查表 → 借池位 → 翻栈或建节点 → 摆位定向定大小 →
/// 记借出名册 → 接 <c>finished</c> → 钉种子 → 重演）。
/// <b>这不是真跑引擎</b>：<c>PackedScene.Instantiate</c> / <c>GpuParticles3D</c> 那些
/// 离线进程里一件都跑不了。「画出来的那一圈有多大」「真的看得见吗」要真机看。</para>
///
/// <para>断言口径：借与还用 <see cref="VfxLedger"/> 的真件断言，节点那一半是转写出来的
/// （见 <see cref="VfxPlayerTranscript"/> 的头注）。</para>
/// </summary>
public sealed class VfxDescriptiveTests
{
    private const string Unit = "u1";

    /// <summary>带参数那一档登的那条轨（对应 <c>res://vfx/FxShapeRing.tscn</c>，<c>one_shot = true</c>）。</summary>
    private const string Ring = "ring";

    /// <summary>持续档那条轨（<c>res://vfx/FxGuardWall.tscn</c>，<c>one_shot = false</c>）。</summary>
    private const string Guard = "guard";

    // ============================ 1. 走一次性档那条路 ============================

    [Fact]
    public void 带参数档走一次性档_演一遍_靠finished把池位还回来()
    {
        var fx = NewStage(capacity: 2);

        fx.Describe(Param(2.0)).ShouldBeTrue("一次性档的资源正是这一档要的那张");
        fx.Ledger.Played.ShouldBe(1, "演了一条 = 借出一个池位");
        fx.Ledger.InPool.ShouldBe(1, "借走一条");
        fx.Ledger.Alive.ShouldBe(1, "正在演");
        fx.Instances.ShouldBe(1, "建了一个节点");
        fx.NodeIdleOf(Ring).ShouldBe(0, "还没演完，不在空闲栈里");

        // 引擎那半由 finished 信号触发归还，离线由 AdvanceOneShot 代它触发。
        fx.AdvanceOneShot(Ring).ShouldBeTrue("演完了：抛 finished");
        fx.Ledger.Alive.ShouldBe(0, "槽位收回来了");
        fx.Ledger.Returned.ShouldBe(1);
        fx.Ledger.InPool.ShouldBe(2, "池回到满的那一份 —— 收不回槽位的话后面全都借不出");
        fx.Ledger.Played.ShouldBe(fx.Ledger.Alive + fx.Ledger.Returned, "演账三行仍然对得上");
        fx.NodeIdleOf(Ring).ShouldBe(1, "演完那个节点回空闲栈");
    }

    // ============================ 2. 档位对不上当场拒 ============================

    [Fact]
    public void 带参数档拿持续档的资源_当场拒_不占池位()
    {
        // 这就是ticket 05 撞到的那一条，方向反过来了：当时拒的是 FxShapeRing（one_shot = true），
        // 现在拒的是护盾那张（one_shot = false）—— 循环发射的资源交给一次性档，
        // 演一遍就没了，finished 也不抛，槽位收不回来。
        var fx = NewStage(capacity: 4);

        fx.Describe(Param(2.0, track: Guard)).ShouldBeFalse("档位对不上，当场拒");
        fx.Ledger.BorrowFailed.ShouldBe(0, "拒不是借不出：借出去了却摆不下，当场还回去，账不悬着");
        fx.Ledger.InPool.ShouldBe(4, "池位还回来了");
        fx.Ledger.Alive.ShouldBe(0);
        fx.Instances.ShouldBe(0, "没借出就没建节点");
    }

    // ============================ 3. 连着描述两次 ============================

    [Fact]
    public void 带参数档连着描述两次_第二次借得出_节点实例数不涨()
    {
        // 判据从「不重建节点」改成「借了又还、实例数不涨」：一次性档的节点演完就归还池，
        // 所以同一个轨连着描述两次是两次借还，不是「同一个节点改参数」。
        var fx = NewStage(capacity: 1);

        fx.Describe(Param(1.0)).ShouldBeTrue();
        fx.AdvanceOneShot(Ring).ShouldBeTrue();

        fx.Describe(Param(4.0)).ShouldBeTrue("上一条已经演完还回来了，所以借得出（容量 1 就够）");
        fx.Instances.ShouldBe(1, "复用的是同一个节点：来回一次实例数不涨");
        fx.Ledger.Played.ShouldBe(2, "两条各记一笔");
        fx.Ledger.Returned.ShouldBe(1);
        fx.Ledger.BorrowFailed.ShouldBe(0);

        var node = fx.PooledAt(Ring);
        node.Scale.ShouldBe(4.0, "后一条的参数落在后一次演出的那个节点上");
        node.Restarts.ShouldBe(2, "两次演各开一个周期 —— 这正是一次性档该有的样子");
    }

    // ============================ 4. 位置与大小 ============================

    [Fact]
    public void 带参数档的位置朝向照宿主那份读数_大小是那一个标量()
    {
        // 本层不自己算位置：形状要画在那个单位脚下，答案由宿主那份读数给（与挂上那一档同口径）。
        var fx = NewStage(capacity: 2);
        fx.MoveHost(2.5, -1.5, 40.0);

        fx.Describe(Param(3.5)).ShouldBeTrue();

        var node = fx.PooledAt(Ring);
        node.X.ShouldBe(2.5, "X 照宿主那份读数");
        node.Y.ShouldBe(0.0, "摆位是二维的");
        node.Z.ShouldBe(-1.5);
        node.Facing.ShouldBe(40.0, "朝向也照那份读数");
        node.Scale.ShouldBe(3.5, "那一个标量当倍数落在发射器节点上：弹道连同散布半径一起放大");
        node.UseFixedSeed.ShouldBeTrue("spec D12：种子钉在节点上");
        node.Seed.ShouldNotBeNull("钉过的种子交得出来");
    }

    // ============================ 5. 不占持续档的名额 ============================

    [Fact]
    public void 带参数档不占持续档的名额_每帧同步摆的不是它()
    {
        // 改道最实在的一处差别：以前这一档是「挂上」，占着 <c>单位/轨</c> 那个键、
        // 每帧跟着走；现在它是「画一遍」，不挂上也不跟。
        var fx = NewStage(capacity: 4);

        fx.Describe(Param(2.0)).ShouldBeTrue();
        fx.Attached.ShouldBe(0, "画一遍不等于挂上：不进持续档的名册");
        fx.Ledger.Alive.ShouldBe(1, "占的是一次性档那个池位");

        // 宿主那边把单位挪开了：挂着的那几条会跟，画过的那一下不会（它本来就是那一刻的样子）。
        fx.MoveHost(9.0, 9.0, 0.0);
        fx.Sync();

        fx.PooledAt(Ring).X.ShouldBe(-1.2, "Sync 摆的是挂着的那几条，这一格不在里面");
        fx.Ledger.BorrowFailed.ShouldBe(0, "同步不碰池账");

        // 真的挂一条持续档上去，同步才动得了它 —— 反证 Sync 那一格不是坏了。
        fx.Sustain(Cue(1)).ShouldBeTrue();
        fx.Attached.ShouldBe(1);
        fx.Sync();
        fx.NodeOf(Unit, Guard).X.ShouldBe(9.0, "挂着的那条跟着走");
    }

    // ============================ 宿主那边没这个单位 ============================

    [Fact]
    public void 带参数档在宿主说没这个单位时_画不上_也不占池位()
    {
        var fx = NewStage(capacity: 4);

        fx.ForgetHostUnit();
        fx.Describe(Param(2.0)).ShouldBeFalse("没地方画");
        fx.LostUnits.ShouldBe(1, "这一回要读得出来");
        fx.Ledger.InPool.ShouldBe(4, "不占池位");
        fx.Instances.ShouldBe(0);
    }

    [Fact]
    public void 带参数档在宿主压根没递查询时_画不上_且不计入单位没了()
    {
        // 「宿主说这个单位没了」与「宿主没递查询」是两件事：后者是接线漏了。
        var fx = NewStage(capacity: 4);

        fx.GiveUpQuery();
        fx.Describe(Param(2.0)).ShouldBeFalse("没有位置可读");
        fx.LostUnits.ShouldBe(0, "那不是「宿主说这个单位没了」");
        fx.Ledger.InPool.ShouldBe(4, "没借没还");
        fx.Instances.ShouldBe(0);
    }

    private static VfxPlayerTranscript NewStage(int capacity)
    {
        var fx = new VfxPlayerTranscript(capacity: capacity);
        fx.RegisterEffect(Ring, "res://vfx/FxShapeRing.tscn", oneShot: true);
        return fx;
    }

    private static CueRow Param(double value, string track = Ring)
        => new(1, CueKind.Descriptive, Unit, track, "半径", value);

    private static CueRow Cue(double value, string track = Guard)
        => new(1, CueKind.Sustained, Unit, track, "护盾", value);
}