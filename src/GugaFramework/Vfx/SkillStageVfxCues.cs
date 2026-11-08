#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using DemoFactory.GugaFramework.Abilities;
using DemoFactory.GugaFramework.Abilities.Stage;
using Godot;

namespace DemoFactory.GugaFramework.Vfx;

/// <summary>
/// <b>技能试验场的 cue 消费者</b>——场子这一侧把派发台（<see cref="CueFeed"/>）排出来的那几条
/// 演成粒子。
///
/// <para><b>它只做三件事，一个字都不往世界里写</b>（契约「表现层只读」）：
/// <list type="number">
/// <item><b>按逻辑步翻新账</b>：<see cref="SkillStageVfxSpec.OnStep"/> 把技能层新增的事件
/// 翻成四类 cue 排进派发台（游标增量读，同一条事件只翻一次）。</item>
/// <item><b>取走并演</b>：<see cref="CueFeed.Drain"/> 取出确定序那几条，转调 VFX 层四档方法
/// （<see cref="VfxPlayer.Play"/> / <see cref="VfxPlayer.Sustain"/> / <see cref="VfxPlayer.Describe"/>）。</item>
/// <item><b>推倍率</b>：把场子那份时间倍率写进 <see cref="VfxPlayer.GameSpeed"/>，
/// 也就是每个粒子的 <c>speed_scale</c>（spec D14）。</item>
/// </list></para>
///
/// <para><b>三拍骨架与最早那份宿主接线同形</b>（那份的 <c>Mirror</c>：
/// 翻账 → <c>Drain</c> 并演 → 收表现时长）。<b>第三拍在两边不是同一件事</b>：
/// 那边收的是飘字与形状指示的「表现时长」（倍率用来缩它），而<b>本层没有那一格</b>——
/// 粒子的时长由效果资源自己的 <c>lifetime</c> 与 <c>finished</c> 收，倍率走的是
/// <c>speed_scale</c>，所以第三拍换成「推倍率」。<paramref name="dt"/> 因此在本层没有用处：
/// 签名与那份 <c>Mirror</c> 保持同形，<b>此件这一轮没有要缩时长的东西</b>，
/// 那一格收进来不参与本层的任何计算。</para>
///
/// <para><b>四档分派为什么落到三个方法</b>（ticket 06 之后的样子）：VFX 层对外是
/// <see cref="VfxPlayer.Play"/>（一次性）、<see cref="VfxPlayer.Sustain"/>（持续）、
/// <see cref="VfxPlayer.Describe"/>（带参数）三个口，而 <c>CueKind</c> 是四档。
/// <b>带参数档已经改走一次性档那条路</b>（<c>Describe</c> 内部与 <c>Play</c> 共用同一份池账），
/// 所以分派是：一次性 → <c>Play</c>；持续 → <c>Sustain</c>；计数与带参数 → <c>Describe</c>。
/// <b>计数与带参数共用一个口</b>：两档在 VFX 层是同一种东西——「画面跟着一个标量画一遍」，
/// <c>CueRow.Value</c> 落成发射器节点的大小倍数，分开写只会多一份逐字相同的转发。</para>
///
/// <para><b>位置只有一份答案</b>：四档摆位都从 <see cref="PoseOf"/> 那一个查询来，
/// <see cref="VfxPlayer.UnitPoseOf"/> 拿到的也是它。于是「四档画在哪」不会档档不同
/// （档档不同的话，「这一下打在谁身上」就得逐档猜）。
/// <b>这一层不自己算位置</b>：摆位与朝向都由逻辑壳答（<c>PointOf</c> / <c>FacingDegreesOf</c>），
/// 坐标归场子、距离经谓词递进技能层（那是场子既有的一条口径）。</para>
///
/// <para><b>朝向换算只在这一处</b>：逻辑壳那份是「度，0 = +X」，而引擎里节点的正脸是 −Z，
/// 于是偏航 = −朝向 − 90（朝向 0 度 ⇒ 偏航 −90，正脸指向 +X，对得上）。
/// 换算写一份就够：写两处的话，两处会漂，而漂的那一处画面上看着还像那么回事
/// （护盾圈转了 90 度，不盯着看发现不了）。</para>
///
/// <para><b>上屏那一格一律 ASCII</b>：Godot 缺省字体没有汉字字形，中文上屏出图实测是整行空白
/// （记在 <c>SkillStageScene.ReadoutText()</c> 那一处）。所以本件给 HUD 的那一行是
/// <see cref="AsciiReadout"/>（自己排版，读的是派发台与 VFX 层的公开计数）；
/// 含汉字的那两行在 <see cref="ReadoutFull"/> 里，只给调试桥念——桥的输出不上屏。</para>
///
/// <para><b>不进真值</b>：cue、粒子节点、存活数、池占用都在真值下游，不进检查点快照、
/// 不进逐字基线。</para>
/// </summary>
public sealed partial class SkillStageVfxCues : Node3D
{
    /// <summary>
    /// 池容量。<b>0 = 从 <c>CueFeedConfig.PendingPerTrack</c> 推导</b>（spec D11，本层不另设参数）；
    /// 给正数是现场调的那一格（压测必恢复：调完要拧回去）。
    /// <b>这一格的值由场子那一份 <c>[Export]</c> 递进来</b>（<c>SkillStageScene.PoolCapacity</c>）：
    /// 本件也是代码里 <c>new</c> 出来的，场景本身发不到它，所以现场调的那一格钉在场景根节点上，
    /// 由 <see cref="Wire"/> 转给 <see cref="VfxPlayer.PoolCapacity"/>。
    /// </summary>
    [Export] public int PoolCapacity { get; set; }

    /// <summary>派发台：同轨怎么合并、挤爆丢哪条、丢了怎么读得出来——那份机制在公共层，这里只持一份。</summary>
    private readonly CueFeed _feed = new();

    /// <summary>本场这一侧的说明书：哪条事件算哪一档、挂在哪条轨，外加那张轨名 → 效果路径的薄表。</summary>
    private readonly SkillStageVfxSpec _spec = new();

    /// <summary>吃引擎的那一半（池与节点）。换一局重挂，别指着已离树的那一个（契约「离树交还」）。</summary>
    private VfxPlayer? _player;

    /// <summary>逻辑壳那一侧。坐标、朝向与事件都从它答。</summary>
    private SkillStage? _stage;

    /// <summary>
    /// 「派发台排出来、而场子里没有那个单位」的条数。
    /// 不静默丢：<c>PointOf</c> 对不存在的单位交回原点，于是不判这一格就是
    /// 「粒子演在场地正中」，看着像一次成功的演出，而它打在了一个不存在的人身上。
    /// </summary>
    private long _skippedRows;

    /// <summary>VFX 层那三样读数口（挂着几条 / 单位没了几回 / 一行读数）。没挂上时交回 <c>null</c>。</summary>
    public VfxPlayer? Player => _player;

    /// <summary>
    /// 挂上这一个逻辑壳，并建出吃引擎的那一半。<b>调一次</b>：换一局先 <see cref="Unwire"/>。
    /// 薄表与逻辑种子在这一刻装上（<see cref="VfxPlayer.Spec"/> / <c>LogicSeed</c>）——
    /// 建节点那一刻种子就定死，所以要早于第一次演。
    /// </summary>
    public void Wire(SkillStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        Unwire();

        _stage = stage;
        _skippedRows = 0;
        _spec.Reset();

        var player = new VfxPlayer
        {
            Name = "VfxPlayer",
            PoolCapacity = PoolCapacity,
            LogicSeed = stage.Seed,
            Spec = new VfxSpec(SkillStageVfxSpec.Effects),
            UnitPoseOf = PoseOf,
        };
        AddChild(player);   // 进树之后才演得出来（VfxPlayer 离树时一律回 false）
        _player = player;
    }

    /// <summary>交还逻辑壳与池节点（换一局与离树都走这里，契约「离树交还」）。</summary>
    public void Unwire()
    {
        _stage = null;
        if (_player is null)
        {
            return;
        }

        _player.UnitPoseOf = null;
        _player.QueueFree();
        _player = null;
    }

    /// <summary>
    /// 每渲染帧一次：<b>翻账 → 取走并演 → 推倍率</b>。
    /// 三拍的次序照最早那份宿主接线（<c>Mirror</c>）；第三拍在本层是「推倍率」，
    /// 原因见类注。没挂上逻辑壳时三拍都跳过（不是静默演到别的地方去）。
    /// </summary>
    /// <param name="dt">
    /// 渲染秒。<b>与那份 <c>Mirror</c> 同形</b>，而本层这一轮没有要缩时长的东西
    /// （粒子的时长由效果资源自己的 <c>lifetime</c> 与 <c>finished</c> 收），
    /// 所以它收进来不参与本层的任何计算（类注那一段说清了为什么留着同形）。
    /// </param>
    /// <param name="speedMultiplier">场子那份时间倍率，落进每个粒子的 <c>speed_scale</c>（spec D14）。</param>
    public void Mirror(float dt, double speedMultiplier)
    {
        if (_stage is not { } stage || _player is not { } player)
        {
            return;
        }

        _spec.OnStep(_feed, stage);
        Play(_feed.Drain());
        player.GameSpeed = (float)speedMultiplier;
    }

    /// <summary>
    /// 派发台排出来的那几条 → VFX 层四档方法。<b>取走即演</b>（<c>Drain</c> 已清空），
    /// 没演的那几条由 VFX 层各自的账记（漏登记 <c>Unregistered</c>、池空记 <c>BorrowFailed</c>），
    /// 本层不重记一份。
    /// </summary>
    private void Play(IReadOnlyList<CueRow> rows)
    {
        foreach (var row in rows)
        {
            Dispatch(row, oneShotScale: 1.0);
        }
    }

    /// <summary>
    /// <b>四档分派那一份</b>：<c>Drain</c> 出来的那几条与直达演的那一条共用它，
    /// 所以「直达演的那一下」与「逻辑真触发的那一下」落在同一个口上——分派写两份就多一处会漂的地方。
    /// </summary>
    /// <param name="row">一条 cue。</param>
    /// <param name="oneShotScale">
    /// 一次性档的大小倍数。<b>为什么它不进 <see cref="CueRow.Value"/></b>：派发台那一档的值
    /// 在喂进来时是没意义的（<c>PushOneShot</c> 缺省 0），而 0 当大小就是「演了但看不见」，
    /// 所以由调用点直接给。
    /// </param>
    private void Dispatch(CueRow row, double oneShotScale)
    {
        if (_player is not { } player) return;

        var pose = PoseOf(row.UnitId);
        if (pose is null)
        {
            _skippedRows++;   // 场上没有这个单位：不演，且计数（不静默演在原点）
            return;
        }

        switch (row.Kind)
        {
            case CueKind.OneShot:
                player.Play(row.Track, pose.Value.X, pose.Value.Z, pose.Value.FacingDegrees,
                    color: null, scale: oneShotScale);
                break;
            case CueKind.Sustained:
                player.Sustain(row);   // 值非 0 = 挂上，0 = 撤下（值那一格是派发台那份语义，原样递过去）
                break;
            case CueKind.Counted:
            case CueKind.Descriptive:
                player.Describe(row);   // 值当倍数落在发射器节点的大小上
                break;
        }
    }

    /// <summary>
    /// <b>特效直达</b>：立刻演一次指定效果，不经逻辑触发（不走 <see cref="SkillStage.Inject"/>，
    /// 一个真值都不写）。
    ///
    /// <para>它为什么是这一件而不是「等逻辑层真触发再截图」：粒子演出窗口极短（那份资源 0.3~0.6 秒），
    /// 等逻辑层走到那一步再截图必然错过，而「这一下有没有演」正是这一格要答的。
    /// 先例是某一版宿主里的特效直达口，它当初就是为这件事写的。</para>
    ///
    /// <para><b>走的是四档分派那一份</b>（<see cref="Dispatch"/>），所以能直演持续档（挂上 / 撤下），
    /// 不只是「画一下就没了」那一档。</para>
    /// </summary>
    /// <param name="unitId">演在谁身上。场上没有这个单位当场抛——不静默演在场地正中。</param>
    /// <param name="track">轨名。本表没有这条轨当场抛，并把这张表的轨名逐条念出来。</param>
    /// <param name="value">
    /// 各档里它不是同一个意思：一次性 = 大小倍数；持续 = 在身(1) / 撤下(0)；
    /// 计数与带参数 = 画面跟着走的那个数。<b>调用点不给它</b>（兜 0 就是「持续档悄悄撤下」）。
    /// </param>
    /// <returns>人读的判定串：演没演、哪一档、落在哪。</returns>
    public string PlayDirect(string unitId, string track, double value)
    {
        ArgumentNullException.ThrowIfNull(unitId);
        ArgumentNullException.ThrowIfNull(track);

        if (_stage is not { } stage)
        {
            throw new InvalidOperationException("特效直达要一个挂着的逻辑壳（先 Wire）——没挂上就没有「演在谁身上」可答");
        }

        if (stage.Find(unitId) is null)
        {
            throw new ArgumentException(
                $"场上没有叫 {unitId} 的单位：这一发演不到任何人身上（不静默摆在场地正中）", nameof(unitId));
        }

        if (!SkillStageVfxSpec.TryGetKind(track, out var kind))
        {
            throw new ArgumentException(
                $"没有叫 {track} 的轨；在册的是 {string.Join(" / ", TrackNames())}", nameof(track));
        }

        if (_player is not { } player)
        {
            throw new InvalidOperationException("特效直达要一个建出来的 VFX 层（先 Wire）");
        }

        var before = player.Readout();
        var row = new CueRow(stage.Tick, kind, unitId, track, track, value);
        Dispatch(row, value);

        var at = PoseOf(unitId);
        return $"track={track} kind={kind} unit={unitId} value={Format(value)} " +
               $"x={Format(at?.X)} z={Format(at?.Z)} yaw={Format(at?.FacingDegrees)} | {before} -> {player.Readout()}";
    }

    /// <summary>
    /// 演出来之后 VFX 层那三样读数（挂着几条 / 单位没了几回 / 池那一行）。
    /// 「单位没了」那一格不为 0 就说明逻辑层退场了但没发撤下，或位置表与单位表对不上。
    /// </summary>
    public string VfxReadout() => _player?.Readout() ?? "vfx=未挂上";

    /// <summary>派发台那一行（含 <c>dropped=</c> 与「丢了哪条」那段有界尾巴）。</summary>
    public string CueReadout() => _feed.Readout();

    /// <summary>
    /// <b>给 HUD 那一行</b>：ASCII、横向一行、自己排版。
    ///
    /// <para>为什么不直接用 <see cref="VfxLedger.Readout"/>：那一行含汉字与全角竖线，
    /// 上屏就是空白（Godot 缺省字体没有汉字字形，出图实测）。所以本行读的是两边<b>公开的数</b>，
    /// 自己拼；含汉字的那一份在 <see cref="ReadoutFull"/>，只给调试桥念。</para>
    /// </summary>
    public string AsciiReadout()
    {
        var attached = _player?.AttachedCount ?? 0;
        var lost = _player?.LostUnits ?? 0;
        return $"cue emitted={Format(_feed.Emitted)} merged={Format(_feed.Merged)} " +
               $"dropped={Format(_feed.Dropped)} pending={Format(_feed.PendingTracks)} " +
               $"unreadable={Format(_spec.UnreadableEvents)} | " +
               $"vfx attached={Format(attached)} lost={Format(lost)} " +
               $"skipped={Format(_skippedRows)} tracks={Format(SkillStageVfxSpec.TrackCount)}";
    }

    /// <summary>
    /// 调试桥那一发的完整读数：含汉字那两行（池那一行与派发台那一行，带「丢了哪条」那段尾巴）
    /// 外加本层那几格。<b>桥的输出不上屏</b>，所以这里可以有汉字。
    /// </summary>
    public string ReadoutFull()
        => $"{CueReadout()} | {VfxReadout()} | 挂着 {_player?.AttachedCount ?? 0} 条，" +
           $"单位没了 {_player?.LostUnits ?? 0} 回，读不出来的事件 {_spec.UnreadableEvents.ToString(CultureInfo.InvariantCulture)} 条，" +
           $"名单里有而场上没有 {_skippedRows.ToString(CultureInfo.InvariantCulture)} 条，" +
           $"轨 {SkillStageVfxSpec.TrackCount.ToString(CultureInfo.InvariantCulture)} 条、" +
           $"漏登 {_player?.Spec?.Unregistered.ToString(CultureInfo.InvariantCulture) ?? "?"} 回";

    /// <summary>这张薄表里在册的轨名（抛错时逐条念出来，省一次回头查表）。</summary>
    private static IReadOnlyList<string> TrackNames()
    {
        var names = new List<string>(SkillStageVfxSpec.Effects.Count);
        foreach (var name in SkillStageVfxSpec.Effects.Keys) names.Add(name);
        names.Sort(StringComparer.Ordinal);
        return names;
    }

    /// <summary>
    /// 「某个单位此刻在哪」那一份：摆位与朝向都由逻辑壳答，这一层不自己算
    /// （<b>坐标归场子、距离经谓词递进技能层</b>，那是场子既有的一条口径）。
    /// 场上没有这个单位交回 <c>null</c>，于是 VFX 层按「宿主说这个单位没了」那一路处理。
    /// </summary>
    private VfxPlayer.UnitPose? PoseOf(string unitId)
    {
        if (_stage is not { } stage || stage.Find(unitId) is null)
        {
            return null;
        }

        var point = stage.PointOf(unitId);
        return new VfxPlayer.UnitPose(point.X, point.Z, YawDegrees(stage.FacingDegreesOf(unitId)));
    }

    /// <summary>
    /// 逻辑壳那份朝向（度，0 度 = +X）→ 引擎的 Y 轴偏航（度）。
    /// 引擎里节点的正脸是 −Z，转 θ 之后指向 (−sin θ, −cos θ)；令它等于 (cos φ, sin φ) 解得 θ = −φ − 90。
    /// 算术一律 double（契约「double 与不变文化」），读数一律不变文化。
    /// </summary>
    private static double YawDegrees(double facingDegrees) => -facingDegrees - 90.0;

    /// <summary>读数用的数字排版（不变文化，契约「double 与不变文化」）。</summary>
    private static string Format(double value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>读数用的数字排版（同上，可空那一档）。</summary>
    private static string Format(double? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "-";

    /// <inheritdoc />
    public override void _ExitTree() => Unwire();
}
