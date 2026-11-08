#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DemoFactory.GugaFramework.Abilities;
using DemoFactory.GugaFramework.Vfx;

namespace DemoFactory.GugaFramework.Tests;

/// <summary>
/// <c>VfxPlayer</c>（吃引擎类型的那一半）在纯 C# 这边<b>逐句转写的一份</b> —— <b>这不是真跑引擎</b>。
///
/// <para>吃引擎类型的那几件（<c>PackedScene.Instantiate</c>、<c>Node3D.GlobalPosition</c>、
/// <c>Emitting</c>、<c>Restart</c>、<c>speed_scale</c>）离线跑不了，所以这里换成同形的记号：
/// 节点是 <c>Node</c>，位置是几个 <c>double</c>，借还照 <see cref="VfxLedger"/> 的真件走。
/// <b>转写与引擎那半对着看才有意义</b>：每一步的先后次序逐句照抄，所以「先后错了」这类问题
/// 离线能断，「引擎真的这么演」不能断。</para>
///
/// <para>三档的次序都在这儿，一份就够（不要按档各抄一份，抄一份漂一处就有一档的槽位收不回来）：
/// <list type="bullet">
/// <item>一次性档（<c>Play</c> / <c>Describe</c> 共用那一份尾巴）：
/// 「查表 → 借池位 → 翻空闲栈或建节点 → 摆位定向定大小染色 → 记借出名册 → 接 finished →
/// 钉种子 → 重演」；<c>finished</c> 抛出来是「<b>断那一条自己那个 finished 回调</b> → 摘名册 →
/// 停演 → 推空闲栈 → 还账」。接与断成对（见 <see cref="VfxFinishedSignalTests"/>）。</item>
/// <item>持续档挂上：<c>Attach</c>；撤下：<c>Detach</c>（硬切，见
/// <see cref="VfxSustainedAttachTests"/>）。</item>
/// <item>倍率：<c>GameSpeed</c> 的 setter 是「写字段 → 逐个已在池里的节点写 <c>speed_scale</c>」；
/// 建/借节点那一步另照字段补一次。</item>
/// <item>持有上限（ticket 08）：每帧「<c>_holdClock</c> 加帧长 → 倒扫一次性那份名册 →
/// 到点走同一个 <c>GiveBack</c>」。上限 = <c>lifetime ÷ 倍率 + 8 帧 × 帧长</b>，
/// <b>倍率 ≤ 1 时不设上限</b>（倍率 0 落在这一格，暂停不会把池清空）。</item>
/// </list></para>
///
/// <para>三处转写只保证「次序与分支」对得上引擎那半，<b>数值语义不断</b>：
/// 引擎那格 <c>speed_scale</c> 收的是 <c>float</c>、去抖阈值是 Godot 自己的，这份用
/// <see cref="IsEqualApprox"/> 保证「同一个值不重复写」那一支，两边在阈值附近的分歧要真机看。</para>
/// </summary>
internal sealed class VfxPlayerTranscript
{
    /// <summary>一个池节点的替身。属性名照引擎那半的节点属性名，值是「那一格此刻是什么」。</summary>
    internal sealed class Node
    {
        internal Node(string track, bool oneShot)
        {
            Track = track;
            OneShot = oneShot;
        }

        internal string Track { get; }

        internal bool OneShot { get; }

        internal bool Emitting { get; set; }

        /// <summary>清空了几回（清空由 <c>Restart</c> 带出来）。</summary>
        internal int Restarts { get; private set; }

        /// <summary>此刻活着的粒子数。真实引擎里这个数由 GPU 侧算，离线只记一个数出来。</summary>
        internal int LiveParticles { get; set; }

        internal double X { get; set; }

        internal double Y { get; set; }

        internal double Z { get; set; }

        internal double Facing { get; set; }

        internal double Scale { get; set; } = 1.0;

        /// <summary>节点的速度倍率（引擎那半的 <c>speed_scale</c>，缺省 1）。</summary>
        internal double SpeedScale { get; set; } = 1.0;

        /// <summary>往引擎写过几回速度倍率（去抖那一格是否真的在干活，就看这个数涨不涨）。</summary>
        internal int SpeedScaleWrites { get; private set; }

        /// <summary>钉过的种子（spec D12：种子钉在节点上，不按 cue 派生）。<c>null</c> = 没钉过。</summary>
        internal uint? Seed { get; set; }

        /// <summary>用固定种子播不播（spec D12 那一格）。</summary>
        internal bool UseFixedSeed { get; set; }

        /// <summary>染色落在那份副本上的颜色（<c>null</c> = 没落上去）。</summary>
        internal string? Tint { get; set; }

        /// <summary>
        /// 那份效果自己的 <c>lifetime</c>（秒）。持有上限那一格从它算（引擎那半 = 节点上的
        /// <c>Lifetime</c>，同一个来源：效果资源那份，不是本层另配的数）。
        /// 场子里按引擎那份资源的实际值给（<c>FxHit</c> 0.4、<c>FxStackMark</c> 0.3、
        /// <c>FxShapeRing</c> 0.6、<c>FxGuardWall</c> 1.2）。
        /// </summary>
        internal double Lifetime { get; set; } = 0.4;

        /// <summary>引擎那半的 <c>Restart</c>：清空还活着的粒子，并把 <c>emitting</c> 置真。</summary>
        internal void Restart()
        {
            Restarts++;
            LiveParticles = 0;
            Emitting = true;
        }

        /// <summary>引擎那半的写 <c>speed_scale</c>：值真的变了才进引擎（去抖那一格）。</summary>
        internal void WriteSpeedScale(double value)
        {
            if (!IsEqualApprox(SpeedScale, value))
            {
                SpeedScale = value;
                SpeedScaleWrites++;
            }
        }

        /// <summary>
        /// 引擎那半的 <c>finished</c> 信号：<b>此刻连在它上面的回调个数</b>。
        /// 「同一个节点演过 N 次之后这个数是多少」就是那一格的读数：接一次断一次恒为 0 或 1，
        /// 只接不断的话它跟着演的次数一路涨 —— 而那一格<b>画面上完全看不出区别</b>
        /// （<see cref="GiveBack"/> 里那道护栏照样把多出来的回调挡住），只有池账之外读不出来。
        /// </summary>
        internal int FinishedHandlers => _finished.Count;

        /// <summary>引擎那半的 <c>Finished += handler</c>。</summary>
        internal void ConnectFinished(Action handler) => _finished.Add(handler);

        /// <summary>引擎那半的 <c>Finished -= handler</c>。</summary>
        internal void DisconnectFinished(Action handler) => _finished.Remove(handler);

        /// <summary>
        /// 引擎那半抛 <c>finished</c>：信号上连着的回调<b>全走一遍</b>。
        /// 走的是抛那一刻的一份拷贝 —— 某个回调在里头把自己摘掉也不掐断这一轮（与真跑引擎同形）。
        /// </summary>
        internal void EmitFinished()
        {
            foreach (var handler in _finished.ToArray())
            {
                handler();
            }
        }

        /// <summary>引擎那半那个委托表的替身。</summary>
        private readonly List<Action> _finished = [];
    }

    /// <summary>
    /// 借出去还没还的那一条（引擎那半的 <c>_borrowed</c> 一条记录）。
    /// <b><c>OnFinished</c> 是这一条自己那个 <c>finished</c> 回调</b>：借出那格接、归还那格断。
    /// <b><c>HeldSince</c></b> 是持有上限那一格的起点（<c>_holdClock</c> 上的秒数）。
    /// </summary>
    private readonly record struct Borrowed(Node Node, string Track, Action OnFinished, double HeldSince);

    /// <summary>一份效果在引擎那半读出来的那两格：<c>one_shot</c> 与 <c>lifetime</c>（秒）。</summary>
    private readonly record struct Effect(bool OneShot, double Lifetime);

    private readonly Dictionary<string, Stack<Node>> _idle = new(StringComparer.Ordinal);

    private readonly Dictionary<string, Node> _attached = new(StringComparer.Ordinal);

    private readonly List<Borrowed> _borrowed = [];

    /// <summary>建过的每一个节点（引擎那半的 <c>_nodes</c>，只给倍率那格推用）。</summary>
    private readonly HashSet<Node> _nodes = [];

    /// <summary>效果路径 → 那一档与它自己的 <c>lifetime</c>（引擎那半 = 读 <c>PackedScene</c> 读出来的
    /// <c>one_shot</c> 与 <c>Lifetime</c>）。</summary>
    private readonly Dictionary<string, Effect> _effectsByPath = new(StringComparer.Ordinal);

    private readonly Dictionary<string, string> _registered = new(StringComparer.Ordinal);

    private VfxSpec _spec = new();

    private double _hostX = -1.2;

    private double _hostZ;

    private double _hostFacing;

    /// <summary>
    /// 引擎那半的 <c>Readout()</c>：池账那一行（<see cref="VfxLedger.Readout"/>，纯 C# 那半用真件）
    /// 外加本层自己那「超时归还」一段。
    /// </summary>
    internal string Readout() => $"{Ledger.Readout()}｜超时归还 {ExpiredReturns.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>宿主那边还有没有这个单位。</summary>
    private bool _hostHasUnit = true;

    /// <summary>宿主有没有递查询（没递是接线漏了，与「宿主说没这个单位」不是一回事）。</summary>
    private bool _queryGiven = true;

    /// <summary>本节点在不在树里（没进树就没有屏幕可演，两档都在这一格先答）。</summary>
    private bool _inTree = true;

    private float _gameSpeed = 1f;

    /// <summary>持有时长那一格的时间基准（渲染秒的单调累加），逐句照引擎那半的 <c>_holdClock</c>。</summary>
    private double _holdClock;

    /// <summary>
    /// 引擎那半留给引擎滞后的帧数。实测那段滞后 5 帧（240 fps 下画面第 97 帧散尽、
    /// <c>finished</c> 第 102 帧），多留 3 帧给量化与机型差异；乘帧长折成秒，
    /// 低帧率下自动变长（宁可多占一会儿，不切在粒子散尽之前）。
    /// </summary>
    private const double HoldLagFrames = 8.0;

    internal VfxPlayerTranscript(CueFeedConfig? config = null, int? capacity = null)
    {
        Ledger = new VfxLedger(config, capacity);
        RegisterEffect("guard", "res://vfx/FxGuardWall.tscn", oneShot: false);
    }

    internal VfxLedger Ledger { get; }

    /// <summary>轨名 → 效果路径那张表。轨名没登进去时演不上，漏登由它计（与引擎那半同一份类）。</summary>
    internal VfxSpec Spec => _spec;

    /// <summary>建过的节点数（引擎那半 = <c>GetChildCount()</c>，只有引擎数得出来）。</summary>
    internal int Instances { get; private set; }

    /// <summary>当前挂着几条。</summary>
    internal int Attached => _attached.Count;

    /// <summary>「宿主说这个单位没了」的次数。</summary>
    internal long LostUnits { get; private set; }

    /// <summary>
    /// 走<b>持有上限</b>那条路归还的条数（其余归还都走 <c>finished</c>）。
    /// 1× 下恒为 0：倍率 ≤ 1 时不设上限（见 <see cref="HoldCapSeconds"/>）。
    /// </summary>
    internal long ExpiredReturns { get; private set; }

    /// <summary>在场子里那些节点（引擎那半遍历 <c>_nodes</c> 用；一个都没有说明场子空了）。</summary>
    internal IReadOnlyCollection<Node> PooledNodes => _nodes;

    /// <summary>
    /// 场子里那条轨的那个节点（引擎那半 = 按轨名找那个发射器节点）。
    /// 这一栏只有一条节点时才拿得出来 —— 那正是池容量那几条断言的形状。
    /// </summary>
    internal Node PooledAt(string track)
    {
        var found = _nodes.Where(n => string.Equals(n.Track, track, StringComparison.Ordinal)).ToList();
        if (found.Count != 1)
        {
            throw new InvalidOperationException(
                $"轨「{track}」在场子里有 {found.Count} 个节点（要 1 个）：这个取法只在容量那几条断言的形状下成立");
        }

        return found[0];
    }

    /// <summary>
    /// 登一条轨（原型侧递那张表的那一格）。表是不可变的，所以每登一条重建一份。
    /// </summary>
    /// <param name="track">轨名。</param>
    /// <param name="path">效果路径。</param>
    /// <param name="oneShot">那一档（<c>true</c> = 一次性档）。</param>
    /// <param name="lifetime">
    /// 那份效果自己的 <c>lifetime</c>（秒）。持有上限从它算，所以要照
    /// <c>src/Shared/vfx/*.tscn</c> 里的实际值给：<c>FxHit</c> 0.4、<c>FxStackMark</c> 0.3、
    /// <c>FxShapeRing</c> 0.6、<c>FxGuardWall</c> 1.2。不给按 0.4（<c>FxHit</c> 那一份）。
    /// </param>
    internal void RegisterEffect(string track, string path, bool oneShot, double lifetime = 0.4)
    {
        _effectsByPath[path] = new Effect(oneShot, lifetime);
        _registered[track] = path;
        _spec = new VfxSpec(_registered);
    }

    /// <summary>宿主把单位挪到新位置（宿主改自己的真值；VFX 层不参与，只在 <see cref="Sync"/> 时读一次）。</summary>
    internal void MoveHost(double x, double z, double facingDegrees)
    {
        _hostX = x;
        _hostZ = z;
        _hostFacing = facingDegrees;
    }

    /// <summary>宿主那边没有这个单位了。</summary>
    internal void ForgetHostUnit() => _hostHasUnit = false;

    /// <summary>宿主把那个查询撤了（接线漏了）。</summary>
    internal void GiveUpQuery() => _queryGiven = false;

    /// <summary>
    /// 本节点离树（引擎那半的 <c>_ExitTree</c>）：借出的那批还账、挂着的那批还账，
    /// 然后四份名册一起清空。<b>节点由引擎随本节点回收，本层只把引用交还</b>。
    /// </summary>
    internal void LeaveTree()
    {
        foreach (var _ in _borrowed)
        {
            Ledger.Return();
        }

        foreach (var _ in _attached)
        {
            Ledger.Return();
        }

        _attached.Clear();
        _borrowed.Clear();
        _idle.Clear();
        _nodes.Clear();
        _inTree = false;
    }

    /// <summary>本节点重新进树。</summary>
    internal void EnterTree() => _inTree = true;

    // ============================ 倍率 ============================

    /// <summary>
    /// 引擎那半的 <c>GameSpeed</c>（它收 <c>float</c>，与 <c>ModelAnimator.GameSpeed</c> 同一形状）：
    /// <b>写字段 → 逐个已在池里的节点写 <c>speed_scale</c></b>。
    /// 一次性档与持续档的节点都在 <see cref="_nodes"/> 里，所以一处覆盖两档。
    /// </summary>
    internal float GameSpeed
    {
        get => _gameSpeed;
        set
        {
            _gameSpeed = value;
            foreach (var node in _nodes)
            {
                node.WriteSpeedScale(value);
            }
        }
    }

    // ============================ 一次性档 ============================

    /// <summary>引擎那半的 <c>Play</c>。</summary>
    internal bool Play(string track, double x, double z, double? facingDegrees = null, string? color = null, double? scale = null)
    {
        if (!_inTree)
        {
            return false;   // 没进树就没有屏幕可演。这一条不进池账
        }

        return PlayOneShot(track, x, z, facingDegrees ?? 0.0, color, scale ?? 1.0);
    }

    /// <summary>引擎那半的带参数档：走一次性档那一条（<c>one_shot = true</c>，演一遍靠 finished 收槽位）。</summary>
    internal bool Describe(CueRow cue)
    {
        if (!_inTree)
        {
            return false;  // 没进树就没有屏幕可演，不进池账
        }

        if (!_queryGiven)
        {
            WarnNoQuery();
            return false;  // 接线漏了：不计入 LostUnits
        }

        var pose = PoseOf(cue.UnitId);
        if (pose is null)
        {
            LostUnits++;
            return false;  // 宿主那边没有这个单位：没地方画，也不占池位
        }

        return PlayOneShot(cue.Track, pose.Value.X, pose.Value.Z, pose.Value.FacingDegrees, color: null, scale: cue.Value);
    }

    /// <summary>一次性档那一条的公共尾巴（<c>Play</c> 与 <c>Describe</c> 共用这一份）。</summary>
    private bool PlayOneShot(string track, double x, double z, double facingDegrees, string? color, double scale)
    {
        if (!Spec.TryGetEffect(track, out var path) || path is null)
        {
            return false;   // 漏登：VfxSpec 已经计了 Unregistered
        }

        if (!Ledger.Borrow())
        {
            return false;   // 池空：当场丢掉，VfxLedger 已经计了 BorrowFailed（spec D10）
        }

        var node = Take(track, path, expectOneShot: true);
        if (node is null)
        {
            Ledger.Return();   // 借出来了却拿不到节点：当场还回去，账不悬着
            return false;
        }

        node.X = x;
        node.Y = 0.0;
        node.Z = z;
        node.Facing = facingDegrees;
        node.Scale = scale;
        node.Tint = color ?? "白";

        // 记借出名册 → 接 finished（接一次）。归还那格按同一个委托断（见 GiveBack）。
        var onFinished = () => GiveBack(node);
        _borrowed.Add(new Borrowed(node, track, onFinished, _holdClock));
        node.ConnectFinished(onFinished);
        node.UseFixedSeed = true;
        node.Seed = 404u;
        node.Restart();
        node.LiveParticles = 24;   // 一次性档：此刻这一簇活着的粒子
        return true;
    }

    /// <summary>
    /// 引擎那半的 <c>_Process(delta)</c>：<b>持有上限那一格</b> + <b>摆一次</b>，次序照引擎那半。
    /// 用它推帧（<c>Process(1.0 / 240.0)</c> 推 240 fps 的一帧）。
    /// </summary>
    internal void Process(double delta)
    {
        ExpireOneShotHolds(delta);
        Sync();
    }

    /// <summary>
    /// 持有上限那一格（引擎那半同名那一份）：<b>一次性的那几条按倍率缩的持有时长上限，到点自己归还</b>。
    /// <b>只扫一次性档那份名册</b>：持续档挂着的那几条槽位由宿主显式撤下收，不靠时长。
    /// 幂等在 <see cref="GiveBack"/> 里那道护栏（摘不到名册就返回），这一格不另判。
    /// </summary>
    private void ExpireOneShotHolds(double delta)
    {
        if (delta <= 0)
        {
            return;   // 帧长非正时不动持有时长
        }

        _holdClock += delta;

        if (_borrowed.Count == 0)
        {
            return;
        }

        var frameSeconds = HoldLagFrames * delta;   // 引擎那几帧滞后的秒数，随帧长走
        for (var i = _borrowed.Count - 1; i >= 0; i--)
        {
            var held = _borrowed[i];
            var heldSeconds = _holdClock - held.HeldSince;
            if (heldSeconds < HoldCapSeconds(held.Node, frameSeconds))
            {
                continue;
            }

            ExpiredReturns++;
            GiveBack(held.Node);
        }
    }

    /// <summary>
    /// 这一条还能占着槽位多久（秒）：<b><c>lifetime ÷ 倍率</c> 加一段按帧折的滞后</b>。
    /// <b>倍率 ≤ 1 时不设上限</b>：上限是为抵消「<c>speed_scale</c> 把池压紧」而存在的，
    /// 倍率不压紧时无事可抵；倍率 0（暂停粒子）落在这一格，上限不缩，暂停不会把池清空。
    /// 滞后那一段按帧折而不写死秒数：低帧率下它自动变长，宁可多占一会儿也不切在粒子散尽之前。
    /// </summary>
    private double HoldCapSeconds(Node node, double lagSeconds)
        => _gameSpeed > 1f
            ? node.Lifetime / _gameSpeed + lagSeconds
            : double.PositiveInfinity;

    /// <summary>
    /// 引擎那半的 <c>finished</c> 抛出来：代它触发 <see cref="GiveBack"/>。
    /// <paramref name="times"/> 给 2 就是「引擎对同一个节点多抛一次」那一格。
    /// </summary>
    internal bool AdvanceOneShot(string track, int times = 1)
    {
        for (var i = _borrowed.Count - 1; i >= 0; i--)
        {
            if (!string.Equals(_borrowed[i].Track, track, StringComparison.Ordinal))
            {
                continue;
            }

            var node = _borrowed[i].Node;
            for (var t = 0; t < times; t++)
            {
                node.EmitFinished();
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// 引擎那半的 <c>GiveBack</c>：<b>断这一条自己那个回调 → 摘名册 → 停演 → 推空闲栈 → 还账</b>。
    /// 摘不到名册就返回（一次借出只还一次账）；<see cref="VfxLedger.Return"/> 自己不幂等，
    /// 多还一次在池数就推高了。
    /// </summary>
    private void GiveBack(Node node)
    {
        for (var i = _borrowed.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(_borrowed[i].Node, node))
            {
                continue;
            }

            var borrowed = _borrowed[i];
            _borrowed.RemoveAt(i);
            node.DisconnectFinished(borrowed.OnFinished);
            node.Emitting = false;
            Idle(node.Track).Push(node);
            Ledger.Return();
            return;
        }
    }

    // ============================ 持续档 ============================

    internal bool Sustain(CueRow cue)
        => cue.Value != 0 ? Attach(cue.UnitId, cue.Track) : Detach(cue.UnitId, cue.Track);

    internal bool Attach(string unitId, string track)
    {
        var key = Key(unitId, track);
        if (_attached.ContainsKey(key))
        {
            return true;   // 已经挂着：不重复建节点
        }

        if (!_inTree)
        {
            return false;  // 没进树就没有屏幕可演：不进池账
        }

        if (!_queryGiven)
        {
            WarnNoQuery();
            return false;  // 接线漏了：不计入 LostUnits
        }

        var pose = PoseOf(unitId);
        if (pose is null)
        {
            LostUnits++;
            return false;  // 宿主那边没有这个单位：没地方摆，也不占池位
        }

        if (!Spec.TryGetEffect(track, out var path) || path is null)
        {
            return false;
        }

        if (!Ledger.Borrow())
        {
            return false;
        }

        var node = Take(track, path, expectOneShot: false);
        if (node is null)
        {
            Ledger.Return();
            return false;
        }

        node.X = pose.Value.X;
        node.Y = 0.0;
        node.Z = pose.Value.Z;
        node.Facing = pose.Value.FacingDegrees;
        _attached[key] = node;
        node.Emitting = true;
        node.Restart();
        node.LiveParticles = 8;   // 循环发射：接下来每帧都有活着的粒子
        return true;
    }

    internal bool Detach(string unitId, string track)
    {
        if (!_attached.Remove(Key(unitId, track), out var node))
        {
            return false;
        }

        node.Restart();
        node.Emitting = false;
        Idle(node.Track).Push(node);
        Ledger.Return();
        return true;
    }

    internal void Sync()
    {
        if (!_queryGiven)
        {
            return;
        }

        // 边走边删会抛（Dictionary 在枚举中被改），所以先收键，走完再撤。
        List<string>? gone = null;
        foreach (var (key, node) in _attached)
        {
            var pose = PoseOf(Split(key).UnitId);
            if (pose is not null)
            {
                node.X = pose.Value.X;
                node.Y = 0.0;
                node.Z = pose.Value.Z;
                node.Facing = pose.Value.FacingDegrees;
                continue;
            }

            (gone ??= []).Add(key);
        }

        if (gone is null)
        {
            return;
        }

        foreach (var key in gone)
        {
            var (unitId, track) = Split(key);
            LostUnits++;
            Detach(unitId, track);
        }
    }

    /// <summary>宿主那个「某个单位此刻在哪」的查询（引擎那半 = 宿主递进来的那个 delegate）。
    /// 场子里只站着 <c>HostUnit</c> 那个单位，而容量那几条断言要往几个单位身上挂，
    /// 所以这里对谁问都答同一个位置 —— 宿主侧答什么与本层怎么用它是两件事。</summary>
    private (double X, double Z, double FacingDegrees)? PoseOf(string unitId)
        => _hostHasUnit ? (_hostX, _hostZ, _hostFacing) : null;

    private Node? Take(string track, string path, bool expectOneShot)
    {
        if (_idle.TryGetValue(track, out var idle) && idle.TryPop(out var reused))
        {
            return StampSpeed(reused);
        }

        if (!_effectsByPath.TryGetValue(path, out var effect))
        {
            return null;   // 读不到那张效果
        }

        if (effect.OneShot != expectOneShot)
        {
            return null;   // 档位对不上 = 误用，当场拒
        }

        Instances++;
        return StampSpeed(new Node(track, effect.OneShot) { Lifetime = effect.Lifetime });
    }

    /// <summary>登记进 <c>_nodes</c> 并按当前倍率补一次 <c>speed_scale</c>（新节点缺省是 1 倍）。</summary>
    private Node StampSpeed(Node node)
    {
        _nodes.Add(node);
        node.WriteSpeedScale(_gameSpeed);
        return node;
    }

    private Stack<Node> Idle(string track)
    {
        if (!_idle.TryGetValue(track, out var idle))
        {
            idle = new Stack<Node>();
            _idle[track] = idle;
        }

        return idle;
    }

    /// <summary>「宿主没递查询」那行警告（引擎那半只推一次，离线只记有没有推过）。</summary>
    private void WarnNoQuery() => WarnedNoQuery = true;

    internal bool WarnedNoQuery { get; private set; }

    internal Node NodeOf(string unitId, string track)
        => _attached[Key(unitId, track)];

    /// <summary>那条轨的空闲栈里有几个节点（引擎那半 = 换回来之前那条轨的空闲栈深度）。</summary>
    internal int NodeIdleOf(string track) => _idle.TryGetValue(track, out var idle) ? idle.Count : 0;

    private static string Key(string unitId, string track) => $"{unitId}/{track}";

    private static (string UnitId, string Track) Split(string key)
    {
        var at = key.IndexOf('/', StringComparison.Ordinal);
        return (key[..at], key[(at + 1)..]);
    }

    /// <summary>
    /// 转写引擎那半的 <c>Mathf.IsEqualApprox</c>：<b>只保证「同一个值不重复写」那一支</b>。
    /// 引擎那个阈值是 Godot 自己的、收的是 <c>float</c>，所以阈值附近哪一侧算「约等」
    /// 这份不定；那一格真机看。
    /// </summary>
    private static bool IsEqualApprox(double a, double b) => Math.Abs(a - b) < 1e-5;
}
