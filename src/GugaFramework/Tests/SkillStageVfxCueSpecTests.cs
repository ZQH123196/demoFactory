#nullable enable
using System;
using System.IO;
using System.Linq;
using DemoFactory.GugaFramework.Abilities;
using DemoFactory.GugaFramework.Abilities.Stage;
using DemoFactory.GugaFramework.Guard;
using DemoFactory.GugaFramework.Vfx;
using Shouldly;
using Xunit;

namespace DemoFactory.GugaFramework.Tests;

/// <summary>
/// 技能试验场那一侧的 cue 说明书（<c>SkillStageVfxSpec</c>，纯 C# 那一半）的离线用例。
///
/// <para><b>这里测的是翻译，不是画面</b>：「哪条事件算哪一档、挂在哪条轨、挂在谁身上」是纯 C#，
/// 离线能断；而「粒子演没演出来、落在对不对的位置」要靠真机出图
/// （分工照「测试与实测分工」）。</para>
///
/// <para><b>为什么这一件值得有离线用例</b>：四档分派与那张薄表是这一层全部的决定，
/// 而它们错了的形态在画面上极难认——轨名拼错 ⇒ 查不到效果 ⇒ <b>什么都不演</b>，
/// 而读数里「演 0」与「这一下没触发」完全一样。</para>
/// </summary>
public sealed class SkillStageVfxCueSpecTests
{
    private static (SkillStage Stage, CueFeed Feed, SkillStageVfxSpec Spec) NewStage(
        SkillStageSpec? spec = null, int seed = 1)
    {
        var stage = new SkillStage(spec ?? SkillStageSample.Spec(), seed);
        return (stage, new CueFeed(), new SkillStageVfxSpec());
    }

    private static string CastVerb =>
        $"{SkillStageSample.CasterId}|{SkillStageSample.Skill.Strike}|{SkillStageSample.DummyId}";

    /// <summary>排出来那几条的逐条念法（判红时一眼看排出的是什么）。</summary>
    private static string Describe(System.Collections.Generic.IReadOnlyList<CueRow> rows)
        => string.Join(" ; ", rows.Select(r => $"{r.Kind}/{r.UnitId}/{r.Track}={r.Value}"));

    // ============================ 一发技能 → 一条一次性 cue ============================

    /// <summary>
    /// 一条技能从按下走到落地，派发台排出<b>一条</b>一次性 cue，
    /// <b>挂在挨打那个身上</b>（层里那一发是 <c>Emit(target.UnitId, …)</c>，主体是挨打的）。
    ///
    /// <para><b>为什么主体这一格要钉</b>：照出手方演就会把「这一刀落在谁身上」演错，
    /// 而画面上两处只隔 3 米，出图不盯着看分不出来；派发台那条 <c>UnitId</c> 一钉就断。</para>
    /// </summary>
    [Fact]
    public void 命中那一刀排一条一次性_挂在挨打那个身上_不是出手那个()
    {
        var (stage, feed, spec) = NewStage();
        stage.Inject(AbilityVerbs.Cast, CastVerb).ShouldBeTrue("射程之内这一发该打得出");
        stage.Advance(SkillStageSample.StrikePreSteps);

        spec.OnStep(feed, stage);
        var rows = feed.Drain();

        var hit = rows.ShouldHaveSingleItem("这一发只该排出一条 cue");
        hit.Kind.ShouldBe(CueKind.OneShot, "命中那一刀是一次性档");
        hit.Track.ShouldBe(SkillStageVfxSpec.HitTrack);
        hit.UnitId.ShouldBe(SkillStageSample.DummyId, "事件行的主体是挨打那个 —— 画面要落在被打中的单位脚下");
        hit.UnitId.ShouldNotBe(SkillStageSample.CasterId);
        hit.Tick.ShouldBe(SkillStageSample.StrikePreSteps, "落在落地那一步（1 步 = 0.1 秒，「时间基准」）");
    }

    /// <summary>没放成那一发也排一条，理由照事件形状里的字段念（不从文本反解析）。</summary>
    [Fact]
    public void 超出射程当场拒_排一条一次性_挂在出手那个身上_理由照形状念()
    {
        var (stage, feed, spec) = NewStage(SkillStageSample.Spec(SkillStageSample.StrikeRangeMeters + 1));
        stage.Inject(AbilityVerbs.Cast, CastVerb).ShouldBeFalse("超出射程这一发打不出");

        spec.OnStep(feed, stage);
        var row = feed.Drain().ShouldHaveSingleItem("没放成也要读得出原因，不静默少演一笔");

        row.Track.ShouldBe(SkillStageVfxSpec.RejectedTrack);
        row.UnitId.ShouldBe(SkillStageSample.CasterId, "没放成的是按下的那个");
        row.Label.ShouldBe(nameof(AbilityRejectReason.NotInRange), "理由照事件形状里的字段念");
    }

    /// <summary>
    /// <b>同一个游标连着翻两次，第二次一条都不排</b>（游标增量的那一格）。
    /// 这一格钉的是「按渲染帧调 <c>OnStep</c> 也不会把同一次出手演第二遍」——
    /// 而这一件是<b>按帧调</b>的，所以这一格就是它成立的前提。
    /// </summary>
    [Fact]
    public void 同一个游标连着翻两次_第二条一条都不排()
    {
        var (stage, feed, spec) = NewStage();
        stage.Inject(AbilityVerbs.Cast, CastVerb);
        stage.Advance(SkillStageSample.StrikePreSteps);

        spec.OnStep(feed, stage);
        feed.Drain().ShouldHaveSingleItem("第一遍排出那一条");

        spec.OnStep(feed, stage);
        feed.Drain().ShouldBeEmpty("同一批事件不排第二遍（游标已经推到当下）");
        spec.UnreadableEvents.ShouldBe(0, "正常路径没有「发过却读不出来」的那些");
    }

    /// <summary>什么都没发生时翻账不排任何 cue（每帧都要调，空转一拍不许造出一行来）。</summary>
    [Fact]
    public void 什么都没发生_翻账排零条()
    {
        var (stage, feed, spec) = NewStage();
        stage.Advance(10);

        spec.OnStep(feed, stage);
        feed.Drain().ShouldBeEmpty("没有事件就一条都不该排");
        feed.Emitted.ShouldBe(0);
    }

    // ============================ 四档：每一档在事件那一路上都有一格载体 ============================

    /// <summary>
    /// <b>一发群体施加同时喂到两档</b>：开阔多点那份先落名单（<c>RecordShape</c>
    /// → <c>ShapeHitLanded</c>，带参数档拿到那把半径），而示范注册壳那条效果是
    /// <c>EffectKind.Instant</c> 的扣血，所以名单上每一个各发一次 <c>DamageDealt</c>
    /// （一次性档拿到三个命中点）。<b>一记扇形打中几个，就该有几个命中点</b>——
    /// 这一格钉的是「一次触发有几种演法」，而画面上看到三个点才对得上那份名单。
    /// </summary>
    [Fact]
    public void 一记群体扇形_带参数档拿半径_一次性档每个被打中的各一个命中点()
    {
        var stage = new SkillStageOpenField(SkillStageSample.SpecOpenField());
        var feed = new CueFeed();
        var spec = new SkillStageVfxSpec();
        var landed = stage.ApplyEffectToGroupInSector(SkillStageSample.CasterId,
            SkillStageSample.OpenField.RadiusMeters, SkillStageSample.OpenField.ConeDegrees,
            SkillStageSample.Fx.StrikeHit);

        spec.OnStep(feed, stage);
        var rows = feed.Drain();
        var hits = rows.Where(r => r.Kind == CueKind.OneShot).ToArray();
        var shapes = rows.Where(r => r.Kind == CueKind.Descriptive).ToArray();

        shapes.ShouldHaveSingleItem($"一记扇形只落一条名单事件，实际排了 [{Describe(rows)}]");
        shapes[0].Track.ShouldBe(SkillStageVfxSpec.ShapeTrack);
        shapes[0].UnitId.ShouldBe(SkillStageSample.CasterId, "形状事件的主体是施法者 —— 范围圈画在他脚下");
        shapes[0].Value.ShouldBe(SkillStageSample.OpenField.RadiusMeters, 0.001, "值就是那把半径（米）");

        hits.Length.ShouldBe(landed.Count, $"打中几个就有几个命中点（实际排了 [{Describe(rows)}]）");
        foreach (var hit in hits)
        {
            hit.Track.ShouldBe(SkillStageVfxSpec.HitTrack);
            landed.ShouldContain(hit.UnitId, "命中点只落在真被打中的那几个身上（名单里没有的不许演）");
        }
    }

    /// <summary>
    /// <b>持续与计数两档在本场的事件那一路上没有载体</b>——这是场地条件，写出来当事实钉住，
    /// 别哪天当 bug 修掉：示范注册壳那条唯一的效果是 <c>EffectKind.Instant</c>
    /// （落 <c>EffectAppliedInstantly</c>，不是 <c>EffectApplied</c>），也没有叠层型效果，
    /// 所以那两档要由<b>特效直达</b>那一发验（真机那趟），事件这一路上验不到。
    /// </summary>
    [Fact]
    public void 示范注册壳没有持续与计数两档的载体_一次性效果落的是Instantly那一发()
    {
        var stage = new SkillStage(SkillStageSample.Spec());
        stage.Inject(AbilityVerbs.Cast, CastVerb);
        stage.Advance(SkillStageSample.StrikePreSteps);

        stage.EventsOf<EffectApplied>().ShouldBeEmpty("一次性效果不落 EffectApplied");
        stage.EventsOf<EffectAppliedInstantly>().ShouldNotBeEmpty("它落的是 Instantly 那一发");
        stage.EventsOf<EffectStacksChanged>().ShouldBeEmpty("示范注册壳没有叠层型效果");
    }

    // ============================ 那张薄表 ============================

    /// <summary>四档各一条轨（一次性两条），档位逐条对得上——两表必须逐字同形。</summary>
    [Fact]
    public void 四档各一条轨_档位逐条对得上_两表键集逐字相同()
    {
        SkillStageVfxSpec.TryGetKind(SkillStageVfxSpec.HitTrack, out var hit).ShouldBeTrue();
        hit.ShouldBe(CueKind.OneShot);
        SkillStageVfxSpec.TryGetKind(SkillStageVfxSpec.RejectedTrack, out var rejected).ShouldBeTrue();
        rejected.ShouldBe(CueKind.OneShot);
        SkillStageVfxSpec.TryGetKind(SkillStageVfxSpec.GuardTrack, out var guard).ShouldBeTrue();
        guard.ShouldBe(CueKind.Sustained);
        SkillStageVfxSpec.TryGetKind(SkillStageVfxSpec.StacksTrack, out var stacks).ShouldBeTrue();
        stacks.ShouldBe(CueKind.Counted);
        SkillStageVfxSpec.TryGetKind(SkillStageVfxSpec.ShapeTrack, out var shape).ShouldBeTrue();
        shape.ShouldBe(CueKind.Descriptive);

        var tracks = new[]
        {
            SkillStageVfxSpec.HitTrack, SkillStageVfxSpec.RejectedTrack, SkillStageVfxSpec.GuardTrack,
            SkillStageVfxSpec.StacksTrack, SkillStageVfxSpec.ShapeTrack,
        };
        foreach (var track in tracks)
        {
            SkillStageVfxSpec.Effects.Keys.ShouldContain(track,
                $"轨「{track}」查得到档位却查不到效果路径 —— 那张 VfxSpec 表查不到它，于是什么都不演");
        }
    }

    /// <summary>
    /// <b>每条轨都指向一个真实存在的效果文件</b>（按 <c>res://</c> 引用，不按 uid）。
    ///
    /// <para>这一格抓的是<b>路径拼错</b>。拼错了在真机上的形态是「粒子不演」，
    /// 而读数里「演 0」与「这一下没触发」完全一样 —— 只有把路径在离线拿文件系统对一遍才断得掉。</para>
    /// </summary>
    [Fact]
    public void 每条轨都指向一个真实存在的效果文件()
    {
        SkillStageVfxSpec.Effects.ShouldNotBeEmpty("这张表空了就是一条轨都演不上");
        foreach (var (track, path) in SkillStageVfxSpec.Effects)
        {
            path.ShouldStartWith("res://", Case.Sensitive,
                $"轨「{track}」按 uid 或按绝对路径引用了效果 —— 一律按 res://（spec D3）");
            var onDisk = Path.GetFullPath(
                Path.Combine(TestPaths.RepoRoot, path["res://".Length..].Replace('/', Path.DirectorySeparatorChar)));
            File.Exists(onDisk).ShouldBeTrue($"轨「{track}」那张效果读不到：{path}（落到磁盘上 {onDisk}）");
        }
    }

    /// <summary>轨名不认识当场回 <c>false</c>，不兜某一档（兜一档就是「拼错轨名当成别的轨演了一遍」）。</summary>
    [Fact]
    public void 轨名不认识_当场回false_不兜某一档()
    {
        SkillStageVfxSpec.TryGetKind("no-such-track", out var kind).ShouldBeFalse();
        kind.ShouldBe(default(CueKind), "回的是缺省那一档，调用点必须自己判 false（不静默当别的轨演）");
    }

    // ============================ 不进真值 ============================

    /// <summary>
    /// cue 这一层不进真值：快照里不出现效果资源文件名，也不出现派发台与 VFX 层的读数前缀
    /// （它俩都在真值下游，契约「表现层只读」）。
    /// </summary>
    [Fact]
    public void cue不进真值_快照里看不到效果资源与那两层的读数()
    {
        var (stage, _, spec) = NewStage();
        stage.Inject(AbilityVerbs.Cast, CastVerb);
        stage.Advance(SkillStageSample.StrikePreSteps);
        spec.OnStep(new CueFeed(), stage);

        var snapshot = stage.DebugSnapshot();
        snapshot.ShouldNotContain("FxHit.tscn", Case.Sensitive, "效果资源名进了快照 —— cue 在真值下游");
        snapshot.ShouldNotContain("cue emitted=", Case.Sensitive);
        snapshot.ShouldNotContain("vfx ", Case.Sensitive, "VFX 层那几格读数进了快照");
        snapshot.ShouldContain("dropped=0", Case.Sensitive, "观测窗口读数照旧带 dropped（「三条铁律」第三条）");
    }
}