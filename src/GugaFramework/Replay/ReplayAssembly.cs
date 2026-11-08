#nullable enable
using System;
using System.Collections.Generic;

namespace DemoFactory.GugaFramework.Replay;

/// <summary>
/// 一次命令解析 + 发到世界的结果。<b>返回值只有两种意思</b>：<c>true</c> = 这一发打成了；
/// <c>false</c> = 世界那儿真没打成。<c>reason</c> 带出失败原因（成功时是空串），给当次调用看。
/// </summary>
/// <param name="cmd">动词名。</param>
/// <param name="args">调用方原样传进来的参数串。</param>
/// <param name="canonicalArgs">
/// <b>规范化后的 args —— Journal 记的就是它</b>，不是 <paramref name="args"/>。
/// 有别名集时（枚举名 + 界面短名 + 旧名）把别名折成唯一那个 token；无别名集时原样返回。
/// </param>
/// <param name="reason">失败原因；成功时是空串。<b>不进 Journal</b>（见本类型上一层的说明）。</param>
public delegate bool ReplayInject(string cmd, string args, out string canonicalArgs, out string reason);

/// <summary>
/// 逻辑世界为重放暴露的那四个成员 —— <b>公共那半唯一需要的东西</b>。
///
/// 为什么只有四个：各原型世界的这四个成员结构上逐个对得上，而种子与「推进到目标步」是
/// 各自的真差异（有的世界公开 <c>Seed</c>、有的不公开，而推进到目标步要的
/// 是「暂停中也照走」这个能力，各世界各有一份不同实现）。把差异项塞进这个契约，
/// 等于按常态而不是按变化定尺寸。
///
/// <para><c>GameLogicBase</c> 不承载它 —— 那个基类只有 Dispose 与 OnDispose 两个成员，
/// 三个需要的成员一个都没声明。</para>
/// </summary>
public interface IReplayWorld
{
    /// <summary>当前走到第几步（重放比对与 Journal 排序的主键）。</summary>
    long Tick { get; }

    /// <summary>逻辑时钟是否冻着。</summary>
    bool IsPaused { get; }

    /// <summary>推进 <paramref name="steps"/> 步，返回是否真的推进了。</summary>
    bool Advance(int steps = 1);

    /// <summary>一发读出全量真值（检查点快照的取法，录制端与重放端同一份）。</summary>
    string ReplaySnapshot();
}

/// <summary>
/// 重放装配 —— <b>命令词表、三钩子、Journal 追加，检查点，脚本导出，全在一处</b>。
///
/// 收口前这五样在三个原型里各抄一份，已漂成三种答案（参数编码抽一份内联五处、检查点守卫三种、
/// Journal 有的存规范参数有的存原始串）。收口后各原型只留三样：<b>动词名单</b>、<b>每个动词那一次调用</b>、
/// <b>推进到目标步的循环</b>（能力不同，不共享）。
///
/// <para>写路径唯一入口是 <see cref="Inject(string, string)"/>：玩家自己下的那一手与调试口注入的那一笔
/// 都走它，于是「注入的路径」与「玩家的路径」不再是两回事（「桥只转发」）。
/// 录制端与重放端消费的是同一份委托（「写路径全录」 写路径必须全录，漏录就无处可藏）。</para>
///
/// <para>三条口径，都是实测换来的，别改回去：
/// <b>① 未知动词不是一次写，不入录</b>（它没走成任何写，「写路径进 Journal」），照样返回 <c>false</c>；
/// <b>② 已知动词被逻辑层拒绝要入录且 <c>ok=false</c></b>（拒绝是可观察行为，重放要比它）；
/// <b>③ 参数给成坏样子返回 <c>false</c>，不抛</b> —— <c>ok</c> 字段是权威，抛异常会让
/// 「录进去的东西」有一类重放不了。</para>
/// </summary>
public sealed class ReplayAssembly : IDisposable
{
    private readonly IReplayWorld _world;
    private readonly ReplayInject _inject;
    private readonly Action<long> _advanceTo;
    private readonly string[] _verbNames;
    private readonly Action? _onDispose;
    private int _seq; // 全局发生顺序（命令与检查点共用）—— 重放端 (步号, 录制序号) 排序的依据
    private bool _recording;
    private bool _disposed;

    /// <summary>
    /// 装一局。
    /// </summary>
    /// <param name="world">满足 <see cref="IReplayWorld"/> 的那个逻辑世界。</param>
    /// <param name="seed">种子，导出脚本时用。原型自备（三个世界的种子来源不同，不进契约）。</param>
    /// <param name="verbNames">词表全名。加动词改这一处，并列进 <paramref name="inject"/> 的 switch。</param>
    /// <param name="inject">每个动词那一次调用。规范化后的 args 与失败原因从它的 out 参数回来。</param>
    /// <param name="advanceTo">推进到目标步。原型自备 —— 「暂停也照走」这个能力各世界各有一份，
    /// 守卫规则也随之不同（有一份的僵局守卫是承重的：布阵期永远走不动，没有它就是死循环）。</param>
    /// <param name="onDispose">装配不拥有世界的生命周期（有的世界压根不是
    /// <see cref="IDisposable"/>，塞进来就得写类型判断，那是拿补丁盖住不对称）。
    /// 世界的交还在这儿递进来；不给就什么都不做。</param>
    /// <param name="recording">建好即开录，还是等 <see cref="BeginSession"/>。默认等。</param>
    public ReplayAssembly(
        IReplayWorld world,
        int seed,
        string[] verbNames,
        ReplayInject inject,
        Action<long> advanceTo,
        Action? onDispose = null,
        bool recording = false)
    {
        _world = world;
        Seed = seed;
        _verbNames = verbNames;
        _inject = inject;
        _advanceTo = advanceTo;
        _onDispose = onDispose;
        if (recording)
        {
            BeginSession();
        }
    }

    /// <summary>这一局的世界。读它那一侧的资源（血量、金币、结局）用。</summary>
    public IReplayWorld World => _world;

    /// <summary>种子（导出脚本时写进去，重放端按它建同一局）。</summary>
    public int Seed { get; }

    /// <summary>本次录制的起点步号（MCP 试玩录制 &gt; 0；测试内录制为 0）。</summary>
    public long StartTick { get; private set; }

    /// <summary>
    /// 开录那一刻的全量快照（重放端先推进到该步并逐字断言它）。
    /// 开录即抓，不按「第 0 步就免了」那套 —— 第 0 步也是一步。
    /// </summary>
    public string? StartSnapshot { get; private set; }

    public bool IsRecording => _recording;

    public List<ReplayRunner.JournalEntry> Journal { get; } = [];

    public List<ReplayRunner.Checkpoint> Checkpoints { get; } = [];

    /// <summary>这一串是不是词表里的动词（区分大小写，与 Journal 里的写法一致）。</summary>
    public bool IsVerb(string cmd) => Array.IndexOf(_verbNames, cmd) >= 0;

    /// <summary>词表全名，读数口（检查清单与契约测试读它）。</summary>
    public IReadOnlyList<string> VerbNames => _verbNames;

    /// <summary>开一段新录制：清空上一段，抓起点步号与起点快照。</summary>
    public void BeginSession()
    {
        Journal.Clear();
        Checkpoints.Clear();
        _seq = 0;
        StartTick = _world.Tick;
        StartSnapshot = _world.ReplaySnapshot();
        _recording = true;
    }

    public void EndSession() => _recording = false;

    /// <summary>
    /// 在<b>已经录着</b>的账上记一个正式起点：<c>StartTick</c> 与 <c>StartSnapshot</c> 取当下，
    /// <b>不清 Journal</b>、不重置序号。
    ///
    /// <para><b>为什么要有这一条</b>：录制端从第 0 步起就在录（比如原型一开局就把树写的意图记上账），
    /// 而「正式开录」发生在若干步之后。这时把前面的账清掉，重放端从第 0 步重建的世界就少了那一段 ——
    /// 实测的后果是随机流错位（同一粒种子、同一步差出 1.5 米）。留着账、只挪起点，
    /// 重放端就能「先推进到起点、断言起点快照、再按时间线喂」，
    /// 与 <see cref="BeginSession"/> 那条「开新的一段」是两件事。</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">没在录 —— 空账上记起点没有意义（先 BeginSession）。</exception>
    public void MarkStart()
    {
        if (!_recording)
        {
            throw new InvalidOperationException("MarkStart 要在录着的时候调：先 BeginSession（或构造时 recording: true）");
        }

        StartTick = _world.Tick;
        StartSnapshot = _world.ReplaySnapshot();
    }

    /// <summary>
    /// 往这一段录制里补一条<b>发生在开录之前</b>的写：步号记 <b>0</b>、序号占 <b>0</b>，
    /// 其余条目的序号各加一。
    ///
    /// <para><b>为什么步号记 0</b>：这一笔要落在「世界出生那一刻」，而重放端是<b>从第 0 步重建</b>
    /// 那个世界的 —— <see cref="MarkStart"/> 之后那一段（起点到开录）已经被
    /// <see cref="Inject(string, string)"/> 记在账上了，缺的只有起点之前那一笔。
    /// 记成起点步号会让它在同一格与别的条目抢先后，记 0 就是「比谁都早，但仍在世界出生之后」。</para>
    ///
    /// <para>与 <see cref="Inject(string, string)"/> 的分工：那个口录的是<b>此刻</b>发生的写，
    /// 这个口补的是起点之前<b>已经</b>发生的写。两者都只写 Journal，不碰世界（补的是账，不是动作）。</para>
    /// </summary>
    /// <param name="cmd">动词名；必须是词表里的名字（免得补进一条重放端不认的账）。</param>
    /// <param name="args">规范化后的参数串，与 Journal 里那一栏同一口径。</param>
    /// <exception cref="InvalidOperationException">
    /// 起点是第 0 步 —— 这一笔会落在世界出生之前（那是另一个世界，不是补账能解决的）。
    /// </exception>
    public void SeedPreStartWrite(string cmd, string args)
    {
        if (!IsVerb(cmd))
        {
            throw new ArgumentException($"补账只收词表里的动词，「{cmd}」不在册", nameof(cmd));
        }

        if (StartTick <= 0)
        {
            throw new InvalidOperationException(
                "补账是给「起点晚于第 0 步」那些录制用的：起点是第 0 步时它落在世界出生之前，补不进这一局");
        }

        // 全局发生顺序要跟着挪：这一笔占 0，其余各加一（步号都是 0，重放端按序号定先后）。
        for (var i = 0; i < Journal.Count; i++)
        {
            Journal[i] = Journal[i] with { Seq = Journal[i].Seq + 1 };
        }

        Journal.Insert(0, new ReplayRunner.JournalEntry(0, cmd, args, true, 0));
    }

    /// <summary>
    /// 写路径唯一入口 —— 走词表、按需入录、返回真实 ok。
    /// 入录的是<strong>规范化后</strong>的 args（<paramref name="ReplayInject.canonicalArgs"/>），
    /// 不是调用方原样传进来的串。
    ///
    /// <para>未开录时写路径照跑，只是不入录 —— 「未开录」不等于「未执行」。</para>
    /// </summary>
    public bool Inject(string cmd, string args) => Inject(cmd, args, out _);

    /// <summary>同上，另回传失败原因（成功时是空串）。原因<b>不进 Journal</b>，给当次调用看。</summary>
    public bool Inject(string cmd, string args, out string reason)
    {
        var isVerb = IsVerb(cmd);
        // 步号取「命令发出那一刻」：有的写会把时钟拨回去（最终重试按战前快照还原），
        // 执行后再读 _world.Tick 会录成还原前的旧步号，重放端按 (步号, 序号) 排时间线
        // 就把这一发提前到战斗进行中，同 tick 顺序契约（「先快照后命令」）随之失效。
        var tick = _world.Tick;
        var ok = _inject(cmd, args, out var canonical, out reason);
        if (_recording && isVerb)
        {
            Journal.Add(new ReplayRunner.JournalEntry(tick, cmd, canonical, ok, _seq++));
        }

        return ok;
    }

    /// <summary>
    /// 检查点 —— 在当前步抓一份全量快照（状态级，这是重放的断言单位）。
    /// 未开录不入记：没有重放方的检查点就是一笔查不了的账。
    /// </summary>
    public void Mark(string label)
    {
        if (!_recording)
        {
            return;
        }

        Checkpoints.Add(new ReplayRunner.Checkpoint(
            _world.Tick, label, _world.ReplaySnapshot(), _seq++));
    }

    /// <summary>
    /// 三个钩子一次填齐 —— 重放端与取证器唯一的接线点。
    ///
    /// <para>这里的 <c>Inject</c> 刻意<b>绕过入录</b>：它是重放端，走的是词表本身。
    /// 录制只发生在 <see cref="Inject(string, string)"/> 那一个口上（「写路径进 Journal」）。</para>
    /// </summary>
    public ReplayRunner.Hooks Hooks() => new()
    {
        Inject = (cmd, args) => _inject(cmd, args, out _, out _),
        Snapshot = _world.ReplaySnapshot,
        AdvanceTo = _advanceTo,
    };

    /// <summary>导出成公共层那份脚本形状（喂给 <see cref="LiveReplaySession"/>）。</summary>
    public ReplayRunner.ReplayScript ToScript(
        ReplayRunner.AssertLevel level = ReplayRunner.AssertLevel.Literal)
        => new(Seed, Journal, Checkpoints, StartTick, StartSnapshot, level);

    /// <summary>
    /// 停录，并把世界的交还回调（若给了）。装配不拥有世界的生命周期。
    ///
    /// <para><b>不清 Journal 与 Checkpoints。</b> 调用方常把这两个 List 的引用直接递进
    /// <see cref="ReplayScript"/>，清空它们等于把已经交出去的账本在对方手里抹掉
    /// （`ReplayScriptTests.RecordScript` 就是这个形状：<c>using</c> 一收，脚本当场变空脚本，
    /// 而「空脚本读回自己」照样自洽 —— 假绿）。要开新的一段用 <see cref="BeginSession"/>，
    /// 它才是「清空并重抓起点」的那一个口。</para>
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _recording = false;
        _onDispose?.Invoke();
    }
}
