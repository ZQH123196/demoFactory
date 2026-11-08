#nullable enable
using System;
using System.Linq;
using DemoFactory.GugaFramework.Abilities;
using Shouldly;
using Xunit;

namespace DemoFactory.GugaFramework.Tests;

/// <summary>
/// <c>VfxPlayer.GameSpeed</c> 的<b>离线那半</b>（ticket 06，spec D14）：倍率写进每个粒子的
/// <c>speed_scale</c>，与 <c>ModelAnimator.GameSpeed</c> 同一口径。
///
/// <para><b>这里测的是顺序，不是引擎</b>：<c>VfxPlayer</c> 吃引擎类型，而
/// <c>GpuParticles3D.SpeedScale</c> 离线进程里写不了，所以引擎那半的次序在
/// <see cref="VfxPlayerTranscript"/> 里逐句转写了一份：
/// 「写字段 → 逐个已在池里的节点写 <c>speed_scale</c>（同一个值不重复写）」，
/// 建/借节点那一步另照字段补一次。
/// <b>这不是真跑引擎</b>：去抖那个阈值是 Godot 自己的，这份只保证「同一个值不重复写」那一支。</para>
///
/// <para><b>为什么这一格值得单独立一张 ticket</b>：快进下粒子按实时演完，表现层与逻辑就错位了 ——
/// 20 倍时逻辑里那一下已经过去 10 秒，画面上的火花还在原地慢慢散。
/// 那种故障在 1 倍下<b>永远看不见</b>，只在真机按住快进时冒出来，而那正是「一局跑完看结果」
/// 时最常按的键。</para>
///
/// <para><b>与另一格不重复算</b>：<c>CueFeed</c> / <c>SkillCues</c> 那侧按倍率缩「表现时长」
/// （飘字、形状那几样），本层只管粒子自身的 <c>speed_scale</c>。两处各管一格，不互相抄。</para>
///
/// <para><b>暂停不在这一格</b>：场子口径是「暂停只冻逻辑，画面照跑」（契约「时间基准」），
/// 所以粒子在暂停期间照常演完。本层不读暂停状态、也不改节点的材质 ——
/// 「暂停时粒子要不要一起停」是另一条决定，真要有再单开一张。</para>
/// </summary>
public sealed class VfxGameSpeedTests
{
    private const string Unit = "u1";

    /// <summary>一次性档那条轨（<c>res://vfx/FxHit.tscn</c>，<c>one_shot = true</c>）。</summary>
    private const string Hit = "hit";

    /// <summary>持续档那条轨（<c>res://vfx/FxGuardWall.tscn</c>，<c>one_shot = false</c>）。</summary>
    private const string Guard = "guard";

    // ============================ 1. 写一次，两档都跟 ============================

    [Fact]
    public void 写一次倍率_借出中的一次性档与挂着的持续档节点都跟着变()
    {
        // 两档在同一份名册里，所以一处 setter 覆盖两档 —— 分两段遍历的话，
        // 漏掉哪一档都只在快进中才看得出来（1 倍下两边都一样）。
        var fx = NewStage(capacity: 4);
        fx.Play(Hit, 0.0, 0.0).ShouldBeTrue();
        fx.Sustain(Cue(1)).ShouldBeTrue();

        fx.GameSpeed = 8.0f;

        fx.PooledAt(Hit).SpeedScale.ShouldBe(8.0, "一次性档正在演的那一个");
        fx.PooledAt(Guard).SpeedScale.ShouldBe(8.0, "持续档挂着的那一个");
        fx.PooledNodes.Count.ShouldBe(2, "场子里就这两个节点，一个都没漏");
    }

    // ============================ 2. 倍率 0 ============================

    [Fact]
    public void 倍率写0_节点的速度倍率就写0_不兜底也不夹到1()
    {
        // 引擎语义：speed_scale 为 0 即暂停粒子。本层照数写下去 ——
        // 兜底或夹到 1 都会让「宿主把倍率拧到 0」这件事读不出来。
        var fx = NewStage(capacity: 2);
        fx.Play(Hit, 0.0, 0.0).ShouldBeTrue();

        fx.GameSpeed = 0.0f;

        fx.PooledAt(Hit).SpeedScale.ShouldBe(0.0, "写下去就是 0：暂停粒子是引擎那一格的语义");
        fx.GameSpeed.ShouldBe(0f, "字段也记着 0，所以下一个新建的节点按 0 起步");
    }

    [Fact]
    public void 倍率从0拧回正数_节点跟着走_不用重新借出()
    {
        var fx = NewStage(capacity: 2);
        fx.GameSpeed = 0.0f;
        fx.Play(Hit, 0.0, 0.0).ShouldBeTrue();
        fx.PooledAt(Hit).SpeedScale.ShouldBe(0.0);

        fx.GameSpeed = 4.0f;

        fx.PooledAt(Hit).SpeedScale.ShouldBe(4.0, "拧回去就恢复，不靠重新借出");
    }

    // ============================ 3. 快进中途改倍率 ============================

    [Fact]
    public void 快进中途改倍率_正在演的那几个当场变速_不等下次借出()
    {
        // 这是本 ticket最要紧的一格：只在借出时设的话，正在演的那几条恰恰是最晚回收的 ——
        // 整段快进里它们都按旧速演完，而账上「演 N」看着一切正常。
        var fx = NewStage(capacity: 4);
        fx.Play(Hit, 0.0, 0.0).ShouldBeTrue();
        fx.Play(Hit, 1.0, 0.0).ShouldBeTrue();
        fx.Sustain(Cue(1)).ShouldBeTrue();
        fx.GameSpeed = 20.0f;

        // 中途把倍率从 20 拧到 2：三条都在演（含持续档那条）。
        fx.GameSpeed = 2.0f;

        fx.PooledNodes.ShouldAllBe(n => n.SpeedScale == 2.0, "三条当场全变，一条都不等下次借出");
        fx.Ledger.Played.ShouldBe(3, "改倍率不碰池账：演出去的不退账、借出的不还池");
        fx.Ledger.Alive.ShouldBe(3);
        fx.Instances.ShouldBe(3, "改倍率不建节点：两条同时在演的一次性档各占一个池位，挂着的护盾占一个");
    }

    // ============================ 4. 借出之前先拧倍率 ============================

    [Fact]
    public void 借出之前先拧倍率_新建的节点按拧好的值起步_不是缺省的1()
    {
        // 建/借节点那一步要照字段补一次：新节点的 speed_scale 缺省是 1，
        // 而本层的倍率可能早就不是 1 了（开局前就拧到 20 倍，然后才开演）。
        var fx = NewStage(capacity: 2);

        fx.GameSpeed = 20.0f;
        fx.Play(Hit, 0.0, 0.0).ShouldBeTrue();

        fx.PooledAt(Hit).SpeedScale.ShouldBe(20.0, "建出来那一刻就按拧好的倍率起步");
        fx.PooledAt(Hit).SpeedScaleWrites.ShouldBe(1, "补了一次，就一次（不是先写 1 再写 20）");
    }

    [Fact]
    public void 还回池的节点_下次借出再补一次_仍然按当前倍率()
    {
        var fx = NewStage(capacity: 1);
        fx.Play(Hit, 0.0, 0.0).ShouldBeTrue();
        fx.AdvanceOneShot(Hit).ShouldBeTrue("演完还池");
        var node = fx.PooledAt(Hit);

        fx.GameSpeed = 6.0f;   // 还在池里（空闲栈）也照推
        fx.Play(Hit, 0.0, 0.0).ShouldBeTrue();

        node.SpeedScale.ShouldBe(6.0);
        node.SpeedScaleWrites.ShouldBe(1, "写进引擎只有拧到 6 那一次：建出来时补的那次值就是缺省的 1，"
            + "借出再补的那次值已经是被拧过的 6，两次都被去抖挡掉");
    }

    // ============================ 5. 去抖 ============================

    [Fact]
    public void 重复设同一个倍率_不重复写引擎()
    {
        // 宿主每帧都拧一次（照 ModelAnimator.GameSpeed 的用法），所以「设了没变」那一格
        // 真的要挡掉重复的引擎写，否则每帧白走一趟。
        var fx = NewStage(capacity: 2);
        fx.Play(Hit, 0.0, 0.0).ShouldBeTrue();
        var node = fx.PooledAt(Hit);
        var afterBorrow = node.SpeedScaleWrites;

        for (var i = 0; i < 30; i++)
        {
            fx.GameSpeed = 3.0f;
        }

        node.SpeedScaleWrites.ShouldBe(afterBorrow + 1, "30 次同一个值，只多写进引擎 1 次");
        node.SpeedScale.ShouldBe(3.0, "第一次就把值写对了");
    }

    [Fact]
    public void 设一个约等于现值的新倍率_同样不写引擎()
    {
        var fx = NewStage(capacity: 2);
        fx.Play(Hit, 0.0, 0.0).ShouldBeTrue();
        fx.GameSpeed = 3.0f;
        var node = fx.PooledAt(Hit);
        var before = node.SpeedScaleWrites;

        fx.GameSpeed = 3f + 1e-9f;   // 约等，不算新的倍率

        node.SpeedScaleWrites.ShouldBe(before, "去抖那一格用的是约等，不是严格相等（与 ModelAnimator 同一口径）");
    }

    // ============================ 离树 ============================

    [Fact]
    public void 离树清空节点名册_不留失效引用()
    {
        var fx = NewStage(capacity: 2);
        fx.Play(Hit, 0.0, 0.0).ShouldBeTrue();
        fx.PooledNodes.ShouldNotBeEmpty();

        fx.LeaveTree();

        // 引擎那半 _ExitTree 清的是借出名册、挂着那一批账与空闲栈；节点随本节点一起回收。
        fx.PooledNodes.ShouldBeEmpty("名册上留着就是一批已经回收掉的引用");
        fx.Play(Hit, 0.0, 0.0).ShouldBeFalse("离树之后没有屏幕可演（这一格不进池账）");
    }

    private static VfxPlayerTranscript NewStage(int capacity)
    {
        var fx = new VfxPlayerTranscript(capacity: capacity);
        fx.RegisterEffect(Hit, "res://vfx/FxHit.tscn", oneShot: true);
        return fx;
    }

    private static CueRow Cue(double value, string track = Guard)
        => new(1, CueKind.Sustained, Unit, track, "护盾", value);
}