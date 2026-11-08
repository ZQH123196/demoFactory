#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DemoFactory.GugaFramework.DebugBridge;
using DemoFactory.GugaFramework.Replay;
using DemoFactory.Prototypes.BattleArray.Logic;
using Godot;

namespace DemoFactory.Prototypes.BattleArray;

/// <summary>
/// <see cref="Main"/> 的另一半：<b>运行时那一侧的重放与留痕通道</b>（D8 / D3 / D1 的承接处）。
///
/// 分三件事，都只做转发，不在这里写第二套逻辑（AGENTS 「桥只转发」）：
/// 1. <b>开一局 H3 单挑</b>（<see cref="StartDuelRiftDebug"/>）：世界由 <see cref="DuelRiftSetup"/> 装，
///    技能层由它一并接上 —— 与离线测试、CLI 取证吃的是同一段装配代码。
/// 2. <b>级 2 引擎重放</b>（<c>replay_*</c> 一组动词）：把 <see cref="LiveReplaySession"/> 挂在这局活世界上，
///    一发喂一条条目，画面跟着走；<c>replay_dump</c> 打的逐格指纹与级 1（<c>src/Tools -- replay … --dump</c>）
///    同格式，两边直接 diff 就是 D8 要的东西。
/// 3. <b>Journal 留痕</b>（<c>record_start</c> / <c>mark</c> / <c>record_stop</c>）：写操作一律经
///    <c>inject</c> 词表分发器，发出即录（「写路径全录」），停下就落一份 json 给上面那条通道吃。
///
/// 一条要紧的现场纪律：<b>重放期间时间轴归剧本，不归渲染</b>。<see cref="StartDuelRiftDebug"/> 一开就暂停，
/// 于是 <see cref="Main._Process"/> 那个「攒够 0.1 秒推一步」的循环一步都不会自己走，
/// 只有 <see cref="LiveReplaySession"/> 通过 <see cref="BattleWorld.AdvanceDebugSteps"/> 推进它 ——
/// 暂停冻的是「自己走」，不是「被喂着走」，这正是 M6 那条随时暂停手感能拿来跑级 2 的原因。
///
/// 第三道对照（2026-09-27 补）：这一局<b>挂行为树</b>。级 2 头一轮答的是「物理帧时间轴与离线时间轴对不对得齐」，
/// 挂树答的是下一个问题——「注入的路径与玩家的路径是不是同一条路」（AGENTS 「桥只转发」 那句话的实证版本）：
/// 级 1 侧 <see cref="BattleWorld.Brain"/> 为空 ⇒ 决策走纯 C# 的 <see cref="BattleBrainPolicy"/>；
/// 级 2 侧每个兵听一棵 GDScript 组合树（叶子 <c>Battle*Task.cs</c> 只搬参数，问的还是同一个政策函数）。
/// 两边逐格指纹全等，就是「规则只有一份」的实证；实跑数字见
/// <c>docs/prototypes/battle-array/技能层验证记录.md</c>（那一轮的实跑报告已并入
/// <c>docs/research/技能系统/调研结论摘要.md</c>，原文在 git 历史）。
/// </summary>
public partial class Main
{
    /// <summary>
    /// 剧本落盘与读取的目录（与离线测试、CLI 用的是同一个位置，一份 json 三方吃）。
    /// 走 <c>res://</c> 换算而不是靠进程当前目录 —— 编辑器里 F5 与 headless 直跑的 cwd 不是一回事。
    /// </summary>
    private static string DuelScriptDir => ProjectSettings.GlobalizePath("res://") + "src/Prototypes/BattleArray/Tests/Scripts";

    /// <summary>级 2 那个会话：null = 这一局没在跑剧本。</summary>
    private LiveReplaySession? _session;

    /// <summary>会话绑的那个世界：中途换局（重开 / 摆子）即视为会话作废，绝不拿旧钩子打新世界。</summary>
    private BattleWorld? _sessionWorld;

    /// <summary>录制会话（null = 没在录）。公共那份 <see cref="ReplayAssembly"/>，与另两个原型同一件。</summary>
    private ReplayAssembly? _recorder;

    // === 开一局 H3 单挑 ===

    /// <summary>
    /// 调试直达 —— 开一局「我方剑士带一条突斩 打 敌方蛮牛」：与离线级 1 同一段装配代码，一开就暂停。
    /// 桥动词 <c>duel</c>；返回开局读数（步号 / 相位 / 两个兵的血）。
    /// <paramref name="showcase"/> = true 走<b>演示档</b>（两边血量各抬三倍、蛮牛也带一条踏地）——
    /// 出图与录像吃这一档，级 1 与级 2 的逐字基线仍吃默认档那一局。
    /// </summary>
    public string StartDuelRiftDebug(int seed, bool showcase = false)
    {
        _world.BattleEnded -= OnBattleEnded;
        _world.Dispose();
        _world = showcase ? DuelRiftSetup.NewShowcaseWorld(seed) : DuelRiftSetup.NewWorld(seed: seed);
        _world.BattleEnded += OnBattleEnded;
        _world.SetPaused(true);       // 时间轴交给剧本：重放期间渲染一歩都不自己推
        _tickAccumulator = 0f;
        RebuildOverlays(_world);      // 模型 / cue / 树一起重挂（第三道对照「树真在管事」靠它，级 2 挂树那格）
        _front.CloseAll();
        _readoutUnitId = DuelRiftSetup.CasterUnitId;
        _session = null;
        _sessionWorld = null;
        _recorder?.Dispose(); // 换局即弃旧账：它攥着上一局那个世界
        _recorder = null;
        RefreshHudNow();
        var caster = _world.Find(DuelRiftSetup.CasterUnitId)!;
        return $"单挑局已开（{(showcase ? $"演示档·血 ×{DuelRiftSetup.ShowcaseHealthScale}·蛮牛带踏地" : "默认档")}，seed={seed}，" +
               $"突斩挂在 {caster.Id} 身上，暂停中等剧本）：" +
               $"{caster.Id} 血 {caster.Health}/{caster.MaxHealth} vs " +
               $"{DuelRiftSetup.DummyUnitId} 血 {_world.Find(DuelRiftSetup.DummyUnitId)!.Health}｜" +
               $"技能层已接（快照末尾多出 [技能层] 那一栏）｜树已挂 {_bridges.Count}/{_world.Units.Count}" +
               "（要细看树判了什么：brain / brain_log）";
    }

    /// <summary>技能层那一半的读数（桥动词 <c>ability_snapshot</c>）；没接技能就照实说。</summary>
    public string AbilitySnapshotDebug()
        => _world.Abilities is { } abilities
            ? abilities.Snapshot()
            : "这一局没接技能层（要开带技能的那局用 duel）";

    /// <summary>
    /// 技能行为流尾部（桥动词 <c>ability_log</c>）：第几步放了什么、为什么被拒。
    /// 返回与标准 <c>logs</c> 动词同形的三键字典（dropped_count / total / lines）——
    /// 尾窗是有界的，「lines 里没有」≠「没发生」，先看 dropped_count。
    /// </summary>
    public Dictionary<string, object?> AbilityLogDebug(int count)
    {
        if (_world.Abilities is not { } abilities)
        {
            return new Dictionary<string, object?>
            {
                ["dropped_count"] = 0L,
                ["total"] = 0L,
                ["lines"] = new List<string> { "这一局没接技能层（要读行为流先用 duel 开一局单挑）" },
            };
        }

        return new Dictionary<string, object?>
        {
            ["dropped_count"] = abilities.EventsDropped,
            ["total"] = abilities.EventTotal,
            ["lines"] = abilities.EventTailLines(count),
        };
    }

    /// <summary>
    /// cue 派发台的只读读数（P4 的验收口径：<b>丢弃计数读得出来</b>，不许靠人眼撞画面）。
    /// 它是真值<b>下游</b>的一份账 —— 不进快照、不进级 1 与级 2 的逐字基线，所以这一发随时读都不脏。
    /// 计数只在逻辑步上涨（<see cref="SkillCues"/> 里那条「按步翻译」），倍率与帧率都不刷它。
    /// </summary>
    public string CueReadoutDebug() => _cues.ReadoutFull();

    /// <summary>
    /// 一次性动作台账（桥动词 <c>windups</c>，N-30 那一格）：这一局起手动作与平A 各演了几次、
    /// 发生在第几步、被吞几次、模型里有没有那条动作。
    /// 与 <see cref="CueReadoutDebug"/> 同一档：真值<b>下游</b>的一份账，不进快照、不进逐字基线，
    /// 所以「突斩那 0.7 秒到底演没演」现在是<b>一发只读调用</b>的事，不用盯画面也不用考古。
    /// </summary>
    public string WindupReadoutDebug() => _units.Windups.Readout();

    /// <summary>
    /// 出图复核那一档的起路口（原先写在 <c>_Ready</c> 里那一段，实现搬到这里只留一份）：
    /// 起一局 H3 单挑、让技能层自己把技能放出去、相机框好、立刻解冻时间轴。
    /// 环境变量 <c>BATTLE_ARRAY_DUEL</c> / <c>--duel</c> 那一档与下面的 GDScript 发入口走的都是这一发，
    /// 所以「画面里看到的那一局」与「启动档自动开的那一局」不会是两回事。
    /// </summary>
    public string AutoStartDuelRiftDebug(bool showcase)
    {
        // 演示档只比默认档多两件事：两边血量各抬三倍、蛮牛也带一条踏地（装配在 Logic 侧
        // DuelRiftSetup.NewShowcaseWorld，级 1 与级 2 的逐字基线仍吃默认档那一局）。
        var readout = showcase
            ? $"H3 单挑局·演示档：两边血 ×{DuelRiftSetup.ShowcaseHealthScale}，剑士突斩 打 蛮牛踏地（{ShowcaseEnvVar}=1 或 --showcase）"
            : "H3 单挑局：剑士带一条突斩，由技能层自己放（" + DuelRiftEnvVar + "=1 或 --duel）";

        StartDuelRiftDebug(DefaultSeed, showcase);
        _world.Abilities!.AutoCast = true;
        _autoFrameDuel = true;
        FrameDuelCamera();
        _world.SetPaused(false);
        Say(readout);
        return readout;
    }

    // ---- GDScript 侧看得见的转发入口（例外形态，条款 → AGENTS.md 「小写转发入口」）------
    // 为什么要有这一格：MCP 的 editor_manage(op: "game_eval") 跑的是 GDScript 子集，而 C# 方法在
    // GDScript 的调用路径上看不见 PascalCase 名字。2026-09-29 现场验的形状是：
    // has_method("StartDuelRiftDebug") 回真、call("StartDuelRiftDebug", 7) 报 Nonexistent function，
    // snake_case 的 start_duel_rift_debug 也回假。有了下面这几发，出图复核在编辑器那条通道上就是
    // 两发的事（起局 + 读数），不必另起进程。每发只一行、只做转发，业务实现仍在上面那些方法里；
    // 它们与桥动词调的是同一个方法，所以「注入的路径」与「玩家的路径」不会漂成两回事（→ 「桥只转发」）。

    /// <summary>转发 <see cref="AutoStartDuelRiftDebug"/>。</summary>
    public string auto_start_duel_rift_debug(bool showcase) => AutoStartDuelRiftDebug(showcase);

    /// <summary>转发 <see cref="CueReadoutDebug"/>。</summary>
    public string cue_readout_debug() => CueReadoutDebug();

    /// <summary>转发 <see cref="WindupReadoutDebug"/>。</summary>
    public string windup_readout_debug() => WindupReadoutDebug();

    /// <summary>转发 <see cref="AbilitySnapshotDebug"/>。</summary>
    public string ability_snapshot_debug() => AbilitySnapshotDebug();

    /// <summary>转发 <see cref="Main.DebugSnapshot"/>（全量真值那一发，显示即真相）。</summary>
    public string debug_snapshot() => DebugSnapshot();

    /// <summary>
    /// 时间轴直达（桥动词 <c>advance</c>，D1 的那一半）：<b>暂停中也照走 n 步</b>，
    /// 走完把读数报回去。它与「一步一跳到同一格」必须逐字同 —— 离线那半边由测试钉，
    /// 这一发是运行时那半边的口子。
    /// </summary>
    public string AdvanceDebug(int steps)
    {
        if (steps < 1) throw new ArgumentException($"参数 --steps 要 >= 1，收到 {steps}");
        var stepped = _world.AdvanceDebugSteps(steps);
        return $"走了 {stepped} 步（要 {steps}）tick={_world.Tick} paused={_world.IsPaused} phase={_world.Phase}";
    }

    // === 写路径与留痕（D3：每发进 Journal、重放复现） ===

    /// <summary>
    /// 桥动词 <c>inject</c> 的实现：词表与级 1、CLI 共用 <see cref="DuelRiftSetup.Inject"/> 那一份。
    /// <b>发出即录</b>（录制会话存活时）——「注入的路径」与「玩家的路径」在这里就是同一条。
    /// </summary>
    public string InjectDebug(string cmd, string args) => WriteDebug(cmd, args);

    /// <summary>
    /// <b>写路径的唯一入口</b>（本文件里只此一处）：桥动词 <c>inject</c> 与摆子口那三个桥动词
    /// （<c>place</c> / <c>remove</c> / <c>move</c>）、鼠标拖放与点格，全走它。
    ///
    /// <para>在录就走 <see cref="ReplayAssembly.Inject"/>（它自己就是那条写路径，记不记由它管），
    /// 没在录就直接打词表。<b>不是「打一次再记一次」</b> —— 那样一开会把这一发打两遍。
    /// 于是「注入的路径」与「玩家的路径」是同一条（「桥只转发」、「写路径进 Journal」）。</para>
    ///
    /// <para>拒的理由跟着回执走（巨兽那一侧 2026-09-30 实测的那条）：参数写错与「世界不答应」
    /// 是两类不同的错，只回一个 ok=false 时读的人分不出是哪个。原因不进 Journal。</para>
    /// </summary>
    private string WriteDebug(string cmd, string args)
    {
        var ok = _recorder is not null
            ? _recorder.Inject(cmd, args, out var why)
            : DuelRiftSetup.Inject(_world, cmd, args, out _, out why);
        return $"{cmd}({args}) ok={ok} tick={_world.Tick}" + (ok ? string.Empty : $" reason={why}");
    }

    /// <summary>
    /// 合成输入的入录回调（2026-10-05 用户裁决）—— 与本原型其余写路径同一条口。
    ///
    /// <para><b>为什么走 <see cref="WriteDebug"/> 而不是自己摸 <c>_recorder</c></b>：
    /// 它已经在处理「没在录就直接打词表」这一档（<c>_recorder</c> 为 null 的那种局面）。
    /// 绕过它自己摸 _recorder，那一档就会漏——而漏录一次就让重放分叉一次（「写路径全录」）。</para>
    /// </summary>
    /// <remarks>
    /// 动词名与形状在公共层 <c>GugaFramework/Replay/SyntheticVerbs</c> 一份，
    /// 重放端跳过那份实现也在那里；本方法只负责把 <c>kind</c> 翻成名字。
    /// </remarks>
    private void RecordSyntheticInput(InputInjector.InputKind kind, string args)
    {
        var cmd = kind == InputInjector.InputKind.Action
            ? SyntheticVerbs.Action
            : SyntheticVerbs.Click;
        WriteDebug(cmd, args);
    }

    /// <summary>开录（桥动词 <c>record_start</c>）：从当前这一刻开始录，起点快照当场抓。</summary>
    public string RecordStartDebug()
    {
        _recorder?.Dispose();
        _recorder = DuelRiftSetup.Recorder(_world);
        _recorder.BeginSession();
        return $"已开始录（起点第 {_recorder.StartTick} 步，种子 {_recorder.Seed}）";
    }

    /// <summary>打一个检查点（桥动词 <c>mark</c>）：抓的是重放那份段格式，与级 1 逐字可比。</summary>
    public string MarkDebug(string label)
    {
        if (_recorder is null) return "没在录（先 record_start）—— 检查点不进账就没有重放方";
        _recorder.Mark(label);
        return $"检查点「{label}」已记在第 {_recorder.World.Tick} 步";
    }

    /// <summary>
    /// 停录并落盘（桥动词 <c>record_stop</c>）：写出一份 json，路径回给你 ——
    /// 同一份文件级 1（<c>src/Tools -- replay</c>）与级 2（这里的 replay_start）都能吃。
    /// </summary>
    public string RecordStopDebug(string name)
    {
        if (_recorder is null) return "没在录";
        var safe = string.Concat(name.Select(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));
        var path = Path.Combine(DuelScriptDir, $"{(safe.Length == 0 ? "duel-bridge" : safe)}.json");
        Directory.CreateDirectory(DuelScriptDir);
        File.WriteAllText(path, ReplayScriptJson.Write(_recorder.ToScript()));
        var summary =
            $"已落盘：{path}（命令 {_recorder.Journal.Count} 条 / 检查点 {_recorder.Checkpoints.Count} 格）";
        _recorder.Dispose();
        _recorder = null;
        return summary;
    }

    // === 级 2 重放（D8 的运行时半边） ===

    /// <summary>读一份剧本挂到这局上（桥动词 <c>replay_start</c>），并把起点那一格对齐好。</summary>
    public string ReplayStartDebug(string path)
    {
        if (!File.Exists(path)) throw new ArgumentException($"剧本不存在：{path}（相对项目根，或先 record_stop 落一份）");
        if (_world.Abilities is null)
            throw new InvalidOperationException("这一局没接技能层 —— 先 duel 开一局单挑，再喂剧本");

        var script = ReplayScriptJson.Read(File.ReadAllText(path));
        _sessionWorld = _world;
        _session = new LiveReplaySession(script, DuelRiftSetup.Hooks(_world));
        var start = _session.PrepareStart();
        return $"剧本已挂上：{path}（种子 {script.Seed}，条目 {_session.ItemsTotal} 条）｜{start}";
    }

    /// <summary>喂 n 条（桥动词 <c>replay_step</c>，缺省 1）：每喂一条，画面跟着走到那一格。</summary>
    public string ReplayStepDebug(int items)
    {
        var session = RequireSession();
        return $"{session.Step(items)}｜画面此刻 tick={_world.Tick}";
    }

    /// <summary>一口气喂完（桥动词 <c>replay_run</c>）：报分歧条数，逐格指纹用 replay_dump 取。</summary>
    public string ReplayRunDebug()
    {
        var session = RequireSession();
        var tail = session.RunAll();
        // 级 2 的断言不许静默：有分歧就把第一条当场念出来，省得再发一发 replay_dump 才知道红了
        var first = session.Divergences.FirstOrDefault();
        return $"{tail}\n{session.DumpSequence()}" + (first is null ? "" : $"\n第一条分歧：{first}");
    }

    /// <summary>打逐格指纹（桥动词 <c>replay_dump</c>）——与级 1 那份同格式，两边 diff 就是 D8。</summary>
    public string ReplayDumpDebug() => RequireSession().DumpSequence();

    /// <summary>取某一格的实跑全文（桥动词 <c>replay_detail</c>，按录制序号取）。</summary>
    public string ReplayDetailDebug(int seq) => RequireSession().SnapshotOf(seq);

    private LiveReplaySession RequireSession()
    {
        if (_session is null)
            throw new InvalidOperationException("还没挂剧本（先 replay_start <json 路径>）");
        if (!ReferenceEquals(_sessionWorld, _world))
            throw new InvalidOperationException("中途换过局了：剧本会话作废，重开一局再 replay_start（旧钩子不许打新世界）");
        return _session;
    }
}
