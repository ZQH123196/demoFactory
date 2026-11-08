#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DemoFactory.GugaFramework.Abilities;
using DemoFactory.GugaFramework.ObjectPool;
using Godot;

namespace DemoFactory.GugaFramework.Vfx;

/// <summary>
/// 特效节点池 —— 吃 Godot 类型的那一半（一次性档 ticket 04、持续档 ticket 05）。
///
/// <para><b>为什么判断与计数全在纯 C# 那半</b>：本类只回答三件事 —— 这条轨现在有哪个节点可用、
/// 怎么把它摆到位置上、怎么让它重演。演没演出去、借不借得出、在池几条，都在
/// <see cref="VfxLedger"/>；轨名查不查得到效果路径，在 <see cref="VfxSpec"/>。
/// 表现层再记一份账，两份数迟早分叉，而分叉的那一处两边都读不出来。
/// 照本仓既有的分工口径：<c>CueFeed</c> 纯 C#、<c>SkillCues</c> 吃引擎，本类属后者。</para>
///
/// <para><b>节点怎么复用</b>：一条轨一个空闲栈。借出 → 摆位定向 → <c>Restart()</c> 重播；
/// <c>finished</c> 抛出来 → 停演 → 归还。<b>「演一遍再演一遍」用的是同一个节点</b>，
/// 所以节点实例数不跟着演的次数涨。</para>
///
/// <para><b>借出时必须 <c>Restart()</c>，不能只写 <c>Emitting = true</c></b>（spec D7）：
/// 一次性档下后者不重启周期，GPU 侧另有延迟。踩错的表现是「第二次触发没反应」，
/// 而画面上与「没触发」完全一样。引擎文档点名这一条（<c>GpuParticles3D.Finished</c> 的注）。</para>
///
/// <para><b>确定性钉在节点级</b>（spec D12 / D13）：每次借出都把 <c>use_fixed_seed</c> 打开、
/// 把 <see cref="LogicSeed"/> 与轨名派生出来的同一个种子重新装回那个节点，于是重播走的是同一串随机数。
/// 种子<b>不</b>按 cue 派生：那个派生函数本身会在表现层造出第二份随机真值。</para>
///
/// <para><b>实测边界（2026-10-06，4.7.2-stable / D3D12 / Movie Maker 30 帧）</b>：
/// 同一个节点<b>第一次</b>演出来的那一簇，与它被 <c>Restart()</c> 重播出来的那一簇<b>不是逐字相同</b> ——
/// 约 3% 的像素有差，差异范围就落在火花群那一块。改成 <c>Restart(keepSeed: true)</c>、
/// 或把种子从「建节点时钉」挪到「每次借出重钉」，两种做法量到的差都一样。
/// 第四轮起与第五轮起逐字相同。<b>能逐字复现的是「同一份输入重跑一整局」</b>：
/// 同一场景跑两遍，115 帧逐字全等。<b>所以「画面可复现」按重跑一整局那一档理解，
/// 不按「同一个池节点反复重播逐字相同」那一档。</b></para>
///
/// <para><b>池空则丢弃并计数</b>（spec D10）：丢弃由 <see cref="VfxLedger"/> 记，本类只回
/// <c>false</c>，不排队、不临时新建。</para>
///
/// <para><b>档位对不对，当场推警告并拒绝</b>：<c>Play</c> 与 <see cref="Describe"/> 只收
/// <c>one_shot = true</c> 的资源，挂上（<see cref="Sustain"/>）只收 <c>one_shot = false</c> 的资源
/// （那一档靠循环发射，不是演一遍就完）。把一张当另一档用就是误用，两头都拒：
/// 持续档资源借进一次性池，它不抛 <c>finished</c>，槽位收不回来；一次性档资源拿去循环发射，
/// 演一遍就没了，挂上去等于什么都没有。</para>
///
/// <para><b>持续档是「挂上 / 跟着走 / 撤下」</b>（ticket 05，spec D8）。<b>一个键一个节点</b>，
/// 键是 <c>单位/轨</c>（<see cref="CueRow.UnitId"/> 与 <see cref="CueRow.Track"/>）——
/// 同一条再挂一次不新建节点，否则一个单位身上会挂两个护盾。位置<b>每帧</b>从
/// <see cref="UnitPoseOf"/> 那个查询读：<b>本层不自己算位置</b>。本层不知道寻路层怎么走、
/// 不知道单位有没有被控、也不知道坐标系怎么摆，那三件事的答案都在宿主那里；
/// 在本层再算一遍就成了第二份位置真值，而两份一定会漂，漂了画面上看着还像那么回事
/// （护盾留在原地很容易被读成「保护范围就那么大」）。宿主递进来的形状与
/// <c>SkillStage.PointOf</c> / <c>FacingDegreesOf</c> 那一对是同一个口径：
/// 「某个单位此刻在哪」由宿主答，本层只照着摆。</para>
///
/// <para><b>撤下即硬切</b>（spec D9）：<c>Restart()</c> 清空 + <c>Emitting = false</c> 停发射，
/// 然后当场归还池。只停发射的话，还活着的粒子要再活满一个 <c>lifetime</c> 才消失，
/// 而那一段时间里画面上留着半个护盾，比不撤还难读。刻意不淡出：淡出会让占用时间变长，
/// 池容量推导要另算，而淡出是纯观感问题。</para>
///
/// <para><b>带参数档（<see cref="Describe"/>）走一次性档那条路</b>：画面由半径、角度、进度
/// 那几个标量决定的东西，是<b>画一遍</b>——画一个范围本就一次性显现，不像护盾那样挂上就在。
/// 所以它要 <c>one_shot = true</c> 的资源、演一遍靠 <c>finished</c> 收槽位，
/// 与 <see cref="Play"/> 同一条路（<see cref="PlayOneShot"/> 那一份公共尾巴）。
/// <see cref="CueRow.Value"/> 那一个标量当<b>倍数</b>落在发射器节点的 <c>Scale</c> 上。
///
/// <para>「参数没变就不重画」那份记忆<b>不在本层</b>：它在 <c>CueFeed</c> 合并同值那一层，
/// 本层每收到一条就演一遍。演过就还池（<c>finished</c>），所以同一个轨连着描述两次
/// 是借了又还，节点实例数不涨 —— 与 <see cref="Play"/> 的复用口径一样。</para>
///
/// <para><b>倍率写进每个粒子的 <c>speed_scale</c></b>（<see cref="GameSpeed"/>）：
/// 快进下粒子按实时演完，表现层与逻辑就错位了 —— 20 倍时逻辑里那一下已经过去 10 秒，
/// 画面上的火花还在原地慢慢散。那种故障在 1 倍下永远看不见，只在真机按住快进时冒出来。
/// 倍率变化时<b>当场推给已在池里的每一个节点</b>，不等下次借出才设：否则高倍快进中
/// 正在演的那些仍按旧速演完，而它们的槽位恰恰是最久才回收的。
/// 这一格只管粒子自身，<c>CueFeed</c> / <c>SkillCues</c> 那侧按倍率缩表现时长，各管一格。</para>
///
/// <para><b>暂停不归这一格</b>：<see cref="GameSpeed"/> 停 <c>speed_scale</c> 是宿主主动拧的倍率，
/// 与场子的「暂停」不是一回事。暂停只冻逻辑、画面照跑（契约「时间基准」），
/// 所以粒子在暂停期间照常演完 —— 本类不读暂停状态、也不改节点的 <c>process_material</c>。</para>
///
/// <para><b>一次性档的槽位有一条按倍率缩的持有上限</b>（ticket 08）。病根在
/// <c>speed_scale</c> <b>只改粒子的演算速度、不改引擎抛 <c>finished</c> 的时刻</b>：
/// 2026-10-06 实测（240 fps 定步长，<c>res://vfx/FxHit.tscn</c>，<c>lifetime = 0.4</c>）
/// 画面上散尽在第 97 帧（1×）与第 10 帧（20×），而 <c>finished</c> 两路都在第 102 帧。
/// 于是高倍快进下等效池容量被压紧约 20 倍（容量从 <c>CueFeedConfig.PendingPerTrack</c> 推，默认 2），
/// 画面上早就散尽的那一簇还占着槽位，「借不出」那个数在快进下会莫名往上涨 ——
/// 而读数与画面都看不出原因，只能靠一轮「起局 → 快进连演 → 看读数」才发现。</para>
///
/// <para><b>上限的口径</b>：<c>上限 = lifetime ÷ 倍率 + 一段按帧折的引擎滞后</c>。
/// <b>倍率 ≤ 1 时没有上限</b>（<see cref="HoldCapSeconds"/>）：上限是为抵消「倍率把池压紧」
/// 这一件事而存在的，倍率不压紧时它无事可抵；而那一格还担着「<c>finished</c> 之外不许有第二条
/// 归还路径」这件事 —— 引擎从 <c>Restart()</c> 到画面散尽、从散尽到抛 <c>finished</c>
/// 各有几帧滞后（240 fps 下各约 5 帧），那段滞后只在一个帧率上量过，
/// 拿它当常数写进上限就成了「上限比真实散尽还早」的隐患（粒子还在天上飞槽位先回来）。
/// 倍率 0 落在同一格里：上限不缩，槽位不会因为暂停被清空。</para>
///
/// <para><b>滞后那一段按帧算、不写死秒数</b>：它随帧长变（240 fps 下 5 帧 = 0.02 秒，
/// 60 fps 下同样是 5 帧 = 0.083 秒），所以由每一帧的 <c>delta</c> 折出来，
/// 而不是取一个秒数常量 —— 取秒数常量在低帧率下就会切早。取的方向是<b>宁可多占一会儿</b>：
/// 帧长突然变大（一次卡顿）只会把上限推得更远，退回「照 <c>finished</c> 走」那条老路，
/// 不会反过来切在粒子散尽之前。</para>
///
/// <para><b>上限只是兜底，两条路撞上只还一次</b>：<c>finished</c> 仍是正常路径；
/// 上限先到时走的是同一个 <see cref="GiveBack"/>，那道「摘不到名册就返回」的护栏兜着幂等
/// （<see cref="VfxLedger.Return"/> 自己不幂等，多还一次会把在池数推高）。
/// 归还了几条走的是上限那一路，念在 <see cref="ExpiredReturns"/>。</para>
///
/// <para><b>持续档不归这一格</b>：持续档的槽位由宿主显式撤下收，不靠 <c>finished</c>、
/// 也不靠持有时长；挂着的那几条根本不在这份名册里。</para>
///
/// <para><b>宿主说这个单位没了，等价于撤下</b>：<see cref="UnitPoseOf"/> 交回 <c>null</c> 时
/// 当场硬切归还（并计 <see cref="LostUnits"/>）。不撤的话池位会永远悬着 ——
/// 单位退了场，护盾还占着一个槽位，最后表现为「借不出」那个数莫名往上涨。
/// 「宿主那边没这个单位」与「宿主压根没递查询」是两回事：后者不撤（那是接线漏了，
/// 推一行警告说明），否则一个没接线的宿主会把所有挂上一次性全清空。</para>
///
/// <para><b>不进真值</b>：粒子节点、存活数、池占用都在真值下游，不进检查点快照、
/// 不进级 1 与级 2 的逐字基线。</para>
/// </summary>
public sealed partial class VfxPlayer : Node3D
{
    /// <summary>
    /// 池容量。<b>0 = 从 <c>CueFeedConfig.PendingPerTrack</c> 推导</b>（spec D11，不另设参数）；
    /// 给正数是给场景侧那一格 <c>[Export]</c> 现场调（压测必恢复）。
    /// <b>这一格的值由 <c>SkillStageVfxCues</c> 那一份递进来</b>：本件也是代码里 <c>new</c> 出来的，
    /// 没有任何 <c>.tscn</c> 给它发过值，所以不走技能试验场时它恒为 0、走推导。
    /// </summary>
    [Export] public int PoolCapacity { get; set; }

    /// <summary>
    /// 逻辑种子。节点种子从它派生（spec D12）—— 表现层不自己摇随机源。
    /// </summary>
    [Export] public int LogicSeed { get; set; } = 1;

    /// <summary>
    /// 轨名 → 效果 <c>res://</c> 路径那张表（原型侧递，公共层不认识任何具体效果名）。
    /// 不给 = 空表，于是每次 <see cref="Play"/> 都查不到，漏登由 <see cref="VfxSpec.Unregistered"/> 计，
    /// 「没给表」这件事照样读得出来。
    /// </summary>
    public VfxSpec? Spec { get; set; }

    /// <summary>
    /// 游戏时间倍率（快进 = 每个渲染帧里推进多少逻辑步）。写进每个粒子的
    /// <c>speed_scale</c>，让演出跟着一起快进（spec D14）——
    /// 否则高倍快进下粒子按实时演完，逻辑里那一下已经过去了、画面上还在原地慢慢散。
    /// 形状与 <c>ModelAnimator.GameSpeed</c> 同一口径：私有字段 + setter 里写字段、写引擎、
    /// <c>Mathf.IsEqualApprox</c> 去抖。
    ///
    /// <para><b>推给已在池里的每一个节点</b>，不只是下次借出时才设：那几条正在演的节点
    /// 恰恰是最晚回收的，只在借出时设等于让它们在整段快进里按 1 倍演完。
    /// 一次性档与持续档的节点都跟（它们在同一份名册 <see cref="_nodes"/> 里）。
    /// 借出时也照写一遍，那是给<b>新建</b>的那个节点用的（新建节点那份默认是 1）。
    /// 宿主每帧重复设同一个值不会累积成本：去抖那格挡掉了重复的引擎写。</para>
    ///
    /// <para><b>0 就是暂停粒子</b>（引擎语义），本层照数写下去、不兜底也不夹到 1 倍。
    /// 它与场子的「暂停」不是一回事：暂停只冻逻辑、画面照跑（契约「时间基准」），
    /// 所以暂停期间这一格不动，粒子照演完。</para>
    /// </summary>
    public float GameSpeed
    {
        get => _gameSpeed;
        set
        {
            _gameSpeed = value;
            foreach (var node in _nodes)
            {
                SetSpeedScale(node, value);
            }
        }
    }

    /// <summary>
    /// 宿主递进来的「某个单位此刻在哪」——挂着的效果每帧从它读位置（spec D8）。
    /// 交回 <c>null</c> = 宿主那边没有这个单位，挂着的那几条当场硬切（见 <see cref="LostUnits"/>）。
    /// 不给 = 宿主没递查询：挂不上，也不同步，<see cref="Sync"/> 什么也不动。
    ///
    /// <para>形状与 <c>SkillStage.PointOf</c> / <c>FacingDegreesOf</c> 同一口径：
    /// 「某个单位在哪」由宿主答。原型侧通常就是一个 lambda 转发到场子那两个读数。</para>
    /// </summary>
    public Func<string, UnitPose?>? UnitPoseOf { get; set; }

    /// <summary>当前挂着几条（一个键一个节点，所以这个数就是占着池位的持续档条数）。</summary>
    public int AttachedCount => _attached.Count;

    /// <summary>
    /// 正在演的效果节点数（一次性档借出的 + 持续档挂着的 + 压力测试临时建的）。
    /// 一次性的借出与归还都逐条改动 <see cref="_borrowed"/>，所以这一格按帧读总是最新。
    /// </summary>
    public int ActiveEmitters => _borrowed.Count + _attached.Count + _stress.Count;

    /// <summary>
    /// 正在演的那些发射器申报的粒子总数（各节点 <c>Amount</c> 之和）。
    /// <b>不是存活粒子数</b>：GPU 粒子在显卡上算，Godot 不提供逐粒子的存活读数；
    /// 这一格是「申请量」，验收按它记峰值并做两倍压力。
    /// </summary>
    public long RequestedParticles
    {
        get
        {
            long total = 0;
            foreach (var held in _borrowed) total += held.Node.Amount;
            foreach (var attached in _attached.Values) total += attached.Node.Amount;
            foreach (var node in _stress) total += node.Amount;
            return total;
        }
    }

    /// <summary>
    /// 压力测试专用：按轨建<b>一个持续发射</b>的节点，粒子数由 <paramref name="amount"/> 指定，
    /// 不过池、不占池位。只服务验收的「两倍峰值」：正常路径的并发仍由池容量封顶，
    /// 而峰值压测要的是同一个发射器的粒子数翻倍，不是把节点数堆上去（D3D12 的描述符堆会先撑爆）。
    /// </summary>
    /// <returns>true = 建起来并开始发射。</returns>
    public bool StressSustain(string track, int amount, double x, double y, double z)
    {
        if (!IsInsideTree() || amount <= 0) return false;
        var spec = Spec ?? _emptySpec;
        if (!spec.TryGetEffect(track, out var path) || path is null) return false;

        if (!_scenes.TryGetValue(track, out var packed))
        {
            packed = ResourceLoader.Load<PackedScene>(path);
            _scenes[track] = packed;
        }
        if (packed?.Instantiate() is not GpuParticles3D node)
        {
            return false;
        }

        node.Name = $"VfxStress_{track}";
        node.OneShot = false;    // 持续档：整段采样窗都按这个粒子数在算
        node.Amount = amount;
        node.Emitting = true;
        AddChild(node);
        // 先入树再写全局位置：不在树上时读全局变换会报 !is_inside_tree()。
        node.GlobalPosition = new Vector3((float)x, (float)y, (float)z);
        _stress.Add(node);
        node.Restart();
        return true;
    }

    /// <summary>撤掉压力测试的发射器（正常播放不受影响）。</summary>
    public string ClearStress()
    {
        var n = _stress.Count;
        foreach (var node in _stress)
        {
            if (IsInstanceValid(node)) node.QueueFree();
        }
        _stress.Clear();
        return $"vfx_stress_cleared={n.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// 走<b>持有上限</b>那条路归还的条数（其余归还都走 <c>finished</c>）。
    ///
    /// <para>不为 0 说明高倍快进下确实有几条是等上限等回来的 —— 那一格是「快进中槽位跟着粒子
    /// 一起回来」的证据，没有它就看不出机制在不在干活。
    /// <b>1× 下恒为 0</b>：那一格没有上限（见 <see cref="HoldCapSeconds"/>），
    /// 全场归还都走 <c>finished</c>。</para>
    ///
    /// <para>与 <see cref="LostUnits"/> 同一口径：<b>不进 <see cref="VfxLedger"/></b> ——
    /// 那份账记的是「节点借与还」，这一格记的是「归还走的是哪条路」。</para>
    /// </summary>
    public long ExpiredReturns { get; private set; }

    /// <summary>
    /// 「宿主说这个单位没了」的次数 —— 挂着的那几条因此被硬切的条数。
    /// 挂上有一条而撤下没有来，池位就一直悬着，账上「借不出」会莫名往上涨；
    /// 这个数不为 0 就说明逻辑层退场了但没发撤下，或宿主的位置表与单位表对不上。
    ///
    /// <para>不并进 <see cref="VfxLedger"/>：那份账记的是「节点借与还」，
    /// 「宿主那边没这个单位」是宿主侧的错位，是两件事（<c>VfxLedger</c> 归 ticket 02，不在本 ticket 改动面内）。</para>
    /// </summary>
    public long LostUnits { get; private set; }

    /// <summary>
    /// 「某个单位此刻在哪」的那三个标量（米与度）。摆位是二维的（X 与 Z），
    /// 与场子那份 <c>PointOf</c> 同一个口径；Y 固定 0，护盾/光带那类效果都落在脚下。
    /// </summary>
    /// <param name="X">X（米）。</param>
    /// <param name="Z">Z（米）。</param>
    /// <param name="FacingDegrees">朝向（度，0 = +Z）。</param>
    public readonly record struct UnitPose(double X, double Z, double FacingDegrees);

    /// <summary>
    /// 一轨一个空闲栈。键是轨名（与 <see cref="Spec"/> 那张表同一个口径）。
    ///
    /// <para><b>内核交给通用池</b>：这一件是 <c>ObjectPool&lt;GpuParticles3D&gt;</c>，
    /// 一轨一个实例（按轨分池见对象池文档「两种分法」——池子不认识「轨」是什么，
    /// 那是 Vfx 的词，所以分池由这一层做，池子只管一条栈）。
    /// <b>建节点、停演、重装种子</b>分别走 <c>createFunc</c> / <c>actionOnRelease</c> /
    /// <c>actionOnGet</c>；<b>满池销毁</b>走 <c>actionOnDestroy</c>（<c>QueueFree</c>）。
    /// 容量按轨给（<c>Ledger.Capacity</c>），与 <see cref="VfxLedger"/> 那份账同一个数 ——
    /// 两个数必须同源，否则「在池」与「容量」会差一格。</para>
    /// </summary>
    private readonly Dictionary<string, ObjectPool<GpuParticles3D>> _pools = new(StringComparer.Ordinal);

    /// <summary>轨名 → 那条轨的效果场景（第一次用时按 <c>res://</c> 路径读一次，之后不再读盘）。</summary>
    private readonly Dictionary<string, PackedScene?> _scenes = new(StringComparer.Ordinal);

    /// <summary>借出去还没还的那些（<c>finished</c> 的回调按它摘账，顺带给借出中与空闲中一份名册）。</summary>
    private readonly List<Borrowed> _borrowed = [];

    /// <summary>
    /// 挂着的那几条：键是 <c>单位/轨</c>，值是那条节点。<b>一个键一个节点</b>——
    /// 同一条再挂一次不新建（否则一个单位身上挂两个护盾），撤下才从这儿摘。
    /// 与 <see cref="_borrowed"/> 分开记：一次性档的节点归 <c>finished</c> 管，
    /// 持续档的节点归宿主显式撤下管，两者的收尾条件不同，混在一份名册里迟早有人摘错。
    /// </summary>
    private readonly Dictionary<string, Attached> _attached = new(StringComparer.Ordinal);

    /// <summary>
    /// 建过的每一个池节点（一档一条轨的空闲栈、<see cref="_borrowed"/>、<see cref="_attached"/>
    /// 三处的并集）。<b>只给 <see cref="GameSpeed"/> 推倍率用</b>，不参与借与还 ——
    /// 那三处各有各的收尾条件，借还照它们自己那份名册走。
    ///
    /// <para>为什么留着一份全量：倍率是「写一次、影响全部已在演的那些」的那类属性，
    /// 按档分三份名册推就得三段遍历，而按全量推只有一段，且新增节点那一格
    /// （<see cref="Take"/> 里照写一遍）就自动覆盖到了。节点数有界（借出才建、借出数不超过
    /// 池容量），这一份名册跟着池容量封顶，不跟着演的次数涨。</para>
    /// </summary>
    private readonly HashSet<GpuParticles3D> _nodes = [];

    /// <summary>压力测试临时建的发射器（不过池；演完自释放）。只服务验收的「两倍峰值」。</summary>
    private readonly List<GpuParticles3D> _stress = [];

    private readonly VfxSpec _emptySpec = new();

    private VfxLedger? _ledger;

    /// <summary>宿主递进来的时间倍率（<see cref="GameSpeed"/> 那一份）。建出来的节点按它起步。</summary>
    private float _gameSpeed = 1f;

    /// <summary>
    /// 持有时长那一格的时间基准：<b>渲染秒的单调累加</b>（不是逻辑步，也不是引擎时间缩放）。
    /// 借出那格记 <see cref="_holdClock"/> 的当前值，<see cref="ExpireOneShotHolds"/> 每帧拿它算已持有多久。
    ///
    /// <para>为什么不用逻辑步：逻辑步长是 0.1 秒，而这份上限量级是零点几秒
    /// （<c>lifetime</c> 0.3~0.6 秒），按步算会把上限的分辨率粗成两三个档。
    /// 为什么不用引擎时间缩放：场子的暂停只冻逻辑、画面照跑（契约「时间基准」），
    /// 本层不读暂停状态，用它推会引出「暂停期间上限还走不走」那个没人答过的问题。</para>
    /// </summary>
    private double _holdClock;

    /// <summary>「宿主没递 <see cref="UnitPoseOf"/>」那行警告只推一次，免得每帧刷屏。</summary>
    private bool _warnedNoQuery;

    /// <summary>
    /// 持有上限留给引擎滞后的帧数：<b>8 帧</b>。实测那段滞后是 5 帧
    /// （240 fps 下 <c>FxHit</c> 画面第 97 帧散尽、<c>finished</c> 第 102 帧，
    /// <c>lifetime = 0.4</c> 秒 ≈ 96 帧），多留 3 帧给量化与机型差异。
    /// 这几帧乘以帧长就是秒数（见 <see cref="ExpireOneShotHolds"/>），
    /// 所以低帧率下它自动变长 —— 那正是要的：宁可多占一会儿，不切在粒子散尽之前。
    /// </summary>
    private const double HoldLagFrames = 8.0;

    /// <summary>
    /// 借出去还没还的那一条。<b><c>OnFinished</c> 是这一条自己那个 <c>finished</c> 回调</b>：
    /// 借出那格 <c>+=</c> 它（<see cref="PlayOneShot"/>），归还那格 <c>-=</c> 它（<see cref="GiveBack"/>），
    /// 记在这里就是为了断得掉。
    ///
    /// <para><b><c>HeldSince</c> 是持有时长那一格的起点</b>（<see cref="_holdClock"/> 上的秒数）：
    /// 借出那一刻记下，<see cref="ExpireOneShotHolds"/> 每帧拿它算「已经持有多久」。
    /// <b>上限每次现算</b>，不借出那一刻定死 —— 倍率中途拧大时上限当场跟着缩，
    /// 与 <see cref="GameSpeed"/> 把倍率推给正在演的节点是同一件事的两个面。</para>
    /// </summary>
    private readonly record struct Borrowed(GpuParticles3D Node, string Track, Action OnFinished, double HeldSince);

    private readonly record struct Attached(GpuParticles3D Node, string UnitId, string Track);

    /// <summary>
    /// 池账。第一次用到时才建 —— 这样 <c>[Export]</c> 那一格已经接线完了，
    /// 不用在 <c>_Ready</c> 里抢时序（谁先来谁先建，两条路都拿到同一份导出值）。
    /// </summary>
    private VfxLedger Ledger => _ledger ??= PoolCapacity > 0
        ? new VfxLedger(capacity: PoolCapacity)
        : new VfxLedger();

    /// <summary>
    /// 演一次这条轨。落点与朝向可省，省了就取默认值（落点缺省在原点、朝向缺省 0 度）；
    /// 颜色与大小可省，省了就是白与 1 倍。
    /// </summary>
    /// <param name="track">轨名。查表在 <see cref="VfxSpec"/>，公共层不认识任何具体效果名。</param>
    /// <param name="x">落点 X（游戏米，<c>double</c>）。</param>
    /// <param name="z">落点 Z（游戏米，<c>double</c>）。</param>
    /// <param name="facingDegrees">朝向（度，0 度 = +Z）。<c>null</c> = 0 度。</param>
    /// <param name="color">整体染色。<c>null</c> = 白。走节点自己的 <c>Modulate</c>，不去改效果资源里那份材质。</param>
    /// <param name="scale">整体大小倍数。<c>null</c> = 1。作用于发射器节点：一次性档的弹道在发射那一刻就定下了，这个倍数改的是画出来的大小与之后发射的散布半径。</param>
    /// <param name="y">落点 Y（游戏米，<c>double</c>）。<c>null</c> = 0。甲板离地几十米的宿主必须给这一格，
    /// 否则粒子全落在地面高度、从甲板视角看就是「没演」。</param>
    /// <returns>
    /// <c>true</c> = 演出去了。<c>false</c> = 没演，且没占池位 —— 轨名没登（已计 <c>Unregistered</c>）、
    /// 效果资源读不到、那不是一次性档、池空丢弃（已计 <c>BorrowFailed</c>），或本节点不在树里。
    /// </returns>
    public bool Play(string track, double x, double z, double? facingDegrees = null, Color? color = null, double? scale = null, double? y = null)
    {
        if (!IsInsideTree())
        {
            // 没进树就没有屏幕可演。这一条不进池账：账只记取舍，不记环境没就绪。
            return false;
        }

        return PlayOneShot(track, x, y ?? 0.0, z, facingDegrees ?? 0.0, color, scale ?? 1.0);
    }

    /// <summary>
    /// 一次性档那一条的公共尾巴：<b>查表 → 借池位 → 翻空闲栈或建节点 → 摆位定向定大小染色
    /// → 记借出名册 → 接 <c>finished</c> → 钉种子 → 重演</b>。
    ///
    /// <para><see cref="Play"/> 与 <see cref="Describe"/> 走同一份，所以那九步只有一处：
    /// 各写一遍的话，漂一步就有一档的槽位收不回来，而那一档表面上看着还在演
    /// （收不回槽位 → 「在池」那个数只减不增 → 后面全都借不出）。</para>
    ///
    /// <para>次序上有两处要点：查表在<b>借池位之前</b>（漏登不是借不出，两件事不混进同一笔账）；
    /// <c>Restart()</c> 排在接 <c>finished</c> 之后（spec D7）。倍率不在这一步写 ——
    /// <see cref="Take"/> 里照 <see cref="_gameSpeed"/> 补过一次，新节点与复用节点都过那一格。</para>
    /// </summary>
    /// <returns>
    /// <c>true</c> = 演出去了。<c>false</c> = 没演，且没占着池位 —— 借出来了却拿不到节点那一路
    /// 当场把账还了回去，所以调用方不欠账。
    /// </returns>
    private bool PlayOneShot(string track, double x, double y, double z, double facingDegrees, Color? color, double scale)
    {
        var spec = Spec ?? _emptySpec;
        if (!spec.TryGetEffect(track, out var path) || path is null)
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

        node.GlobalPosition = new Vector3((float)x, (float)y, (float)z);
        node.RotationDegrees = new Vector3(0f, (float)facingDegrees, 0f);
        node.Scale = Vector3.One * (float)scale;
        if (node.MaterialOverride is StandardMaterial3D tint)
        {
            tint.AlbedoColor = color ?? Colors.White;   // 染色只落在本节点自己那份副本上
        }

        // 记借出名册 → 接 finished。接与断成对：断在 GiveBack 那头。
        // 闭包只捕获 node（不捕获这条记录本身），那一条自己挂的回调随那一条一起摘，所以断得掉。
        // 只接不断的话，同一个节点复用 N 次之后信号上就积着 N 个回调，每演完全走一遍 N 回。
        var onFinished = () => GiveBack(node);
        _borrowed.Add(new Borrowed(node, track, onFinished, _holdClock));
        node.Finished += onFinished;
        node.UseFixedSeed = true;   // spec D12
        node.Seed = DeriveSeed(track);
        node.Restart();   // spec D7：借出必须 Restart，不能只写 Emitting = true
        return true;
    }

    /// <summary>
    /// 持续档那一条：<see cref="CueRow.Value"/> 非 0 = 挂上，0 = 撤下。
    /// 挂上之后位置由 <see cref="Sync"/> 每帧从 <see cref="UnitPoseOf"/> 读，不靠本方法再递一次坐标。
    /// </summary>
    /// <param name="cue">
    /// 键取自 <see cref="CueRow.UnitId"/> 与 <see cref="CueRow.Track"/>（一个键一个节点）；
    /// 轨名查表在 <see cref="VfxSpec"/>。
    /// </param>
    /// <returns>
    /// 挂上：<c>true</c> = 画面上现在挂着（<b>已经挂着也算挂上了</b>）。
    /// 撤下：<c>true</c> = 当场撤掉了，<c>false</c> = 本来就没挂着。
    /// <c>false</c>（挂上那一侧）= 没挂上且没占池位 —— 本节点不在树里、宿主没递
    /// <see cref="UnitPoseOf"/>（那是接线漏了，不计 <see cref="LostUnits"/>）、
    /// 宿主那边没有这个单位（已计 <see cref="LostUnits"/>）、轨名没登（已计 <c>Unregistered</c>）、
    /// 效果资源读不到、那不是持续档的资源、池空丢弃（已计 <c>BorrowFailed</c>）。
    /// </returns>
    public bool Sustain(CueRow cue)
    {
        ArgumentNullException.ThrowIfNull(cue);
        return cue.Value != 0
            ? Attach(cue.UnitId, cue.Track)
            : Detach(cue.UnitId, cue.Track);
    }

    /// <summary>
    /// 挂上这条轨到某个单位身上：借节点 → 按 <see cref="UnitPoseOf"/> 摆位 → 开循环发射。
    /// 已经在身上就直接答挂上了，<b>不重复建节点</b>。
    ///
    /// <para>要 <c>one_shot = false</c> 的资源（循环发射那一档）。拿一次性档资源来挂，
    /// 演一遍就没了，挂上去等于什么都没有 —— 那种误用当场推警告并拒绝。</para>
    /// </summary>
    public bool Attach(string unitId, string track)
    {
        ArgumentNullException.ThrowIfNull(unitId);
        ArgumentNullException.ThrowIfNull(track);

        var key = Key(unitId, track);
        if (_attached.ContainsKey(key))
        {
            return true;   // 已经挂着：再挂一次不新建，否则一个单位身上挂两个护盾
        }

        if (!IsInsideTree())
        {
            return false;  // 没进树就没有屏幕可演，同 Play 那一格：不进池账
        }

        var query = UnitPoseOf;
        if (query is null)
        {
            WarnNoQuery();
            return false;  // 接线漏了：不算「宿主说这个单位没了」，两件事后果不同
        }

        var pose = query(unitId);
        if (pose is null)
        {
            LostUnits++;
            return false;  // 宿主那边没有这个单位：没地方摆，也不占池位
        }

        var spec = Spec ?? _emptySpec;
        if (!spec.TryGetEffect(track, out var path) || path is null)
        {
            return false;   // 漏登：VfxSpec 已经计了 Unregistered
        }

        if (!Ledger.Borrow())
        {
            return false;   // 池空：当场丢掉，VfxLedger 已经计了 BorrowFailed（spec D10）
        }

        var node = Take(track, path, expectOneShot: false);
        if (node is null)
        {
            Ledger.Return();   // 借出来了却拿不到节点：当场还回去，账不悬着
            return false;
        }

        node.GlobalPosition = new Vector3((float)pose.Value.X, 0f, (float)pose.Value.Z);
        node.RotationDegrees = new Vector3(0f, (float)pose.Value.FacingDegrees, 0f);
        _attached[key] = new Attached(node, unitId, track);

        // 种子从逻辑种子与轨名派生，钉在节点上（spec D12）—— 不按 cue 派生。
        node.UseFixedSeed = true;
        node.Seed = DeriveSeed(track);
        node.Emitting = true;
        node.Restart();   // spec D7：光写 Emitting 不重启周期。Restart 同时清掉上一次剩的粒子
        return true;
    }

    /// <summary>撤下：<b>即硬切</b>（<c>Restart()</c> 清空 + <c>Emitting = false</c> 停发射）后归还池（spec D9）。</summary>
    /// <returns><c>true</c> = 当场撤掉了。<c>false</c> = 本来就没挂着这条（没什么可撤）。</returns>
    public bool Detach(string unitId, string track)
    {
        ArgumentNullException.ThrowIfNull(unitId);
        ArgumentNullException.ThrowIfNull(track);

        if (!_attached.Remove(Key(unitId, track), out var attached))
        {
            return false;
        }

        // 硬切：Restart 清空还活着的粒子（引擎文档点明它 clear existing particles）。
        // GpuParticles3D 没有单独一个「清空」的方法可用（C# 绑定里没有 clear_particles），
        // 而只写 Emitting = false 的话，剩的粒子要再活满一个 lifetime 才消失 ——
        // 那半秒多里画面上留着半个护盾，比不撤还难读。
        attached.Node.Restart();
        attached.Node.Emitting = false;   // Restart 会把 emitting 置真，所以关掉要写在它后头
        PoolFor(attached.Track).Return(attached.Node);
        Ledger.Return();
        return true;
    }

    /// <summary>
    /// 带参数档：把 <see cref="CueRow.Value"/> 那一个标量当<b>倍数</b>演一遍。
    ///
    /// <para>走<b>一次性档</b>那条路（与 <see cref="Play"/> 同一份 <see cref="PlayOneShot"/>）：
    /// 画面由半径、角度、进度那几个标量决定的东西，是<b>画一个范围</b>——那一次显现就该演一遍，
    /// 不像护盾那样挂上就在。所以它要 <c>one_shot = true</c> 的资源、演一遍靠 <c>finished</c>
    /// 收槽位；拿 <c>one_shot = false</c> 的资源来，当场被拒（那一档循环发射，靠显式撤下收）。</para>
    ///
    /// <para><b>位置从宿主那份读数来</b>：<c>CueRow</c> 给的是单位 id，形状要画在那个单位脚下。
    /// 本层不自己算位置，<see cref="UnitPoseOf"/> 那个查询答，与挂上那一档同一个口径。
    /// <b>画完不跟着走</b> —— 一次性的东西本来就是那一刻的样子，
    /// 所以 <see cref="Sync"/> 摆的是挂着的那几条，这一格不在里面。</para>
    ///
    /// <para>那一个标量是「倍数」还是「米」由原型侧知道，本层不猜：它只当倍数用，
    /// 落在发射器节点的 <c>Scale</c> 上，所以弹道连同散布半径一起放大。
    /// 「参数没变就不重画」那份记忆不在本层，它在 <c>CueFeed</c> 合并同值那一层。</para>
    /// </summary>
    /// <returns>
    /// <c>true</c> = 画出去了。<c>false</c> = 没画，且没占池位 —— 本节点不在树里、
    /// 宿主没递 <see cref="UnitPoseOf"/>（那是接线漏了，不计 <see cref="LostUnits"/>）、
    /// 宿主那边没有这个单位（已计 <see cref="LostUnits"/>）、轨名没登（已计 <c>Unregistered</c>）、
    /// 效果资源读不到、那不是一次性档的资源、池空丢弃（已计 <c>BorrowFailed</c>）。
    /// </returns>
    public bool Describe(CueRow cue)
    {
        ArgumentNullException.ThrowIfNull(cue);

        if (!IsInsideTree())
        {
            return false;  // 没进树就没有屏幕可演，同 Play 那一格：不进池账
        }

        var query = UnitPoseOf;
        if (query is null)
        {
            WarnNoQuery();
            return false;  // 接线漏了：不算「宿主说这个单位没了」，两件事后果不同
        }

        var pose = query(cue.UnitId);
        if (pose is null)
        {
            LostUnits++;
            return false;  // 宿主那边没有这个单位：没地方画，也不占池位
        }

        // 位置与朝向照宿主那份读数走；染色不给（带参数那一档的表里没有颜色那一格，null = 白）。
        return PlayOneShot(cue.Track, pose.Value.X, 0.0, pose.Value.Z, pose.Value.FacingDegrees, color: null, scale: cue.Value);
    }

    /// <summary>
    /// 每帧把挂着的那几条按 <see cref="UnitPoseOf"/> 的读数摆一遍（spec D8）。
    /// <see cref="UnitPoseOf"/> 交回 <c>null</c>（宿主那边没这个单位）时当场硬切并计
    /// <see cref="LostUnits"/>；<see cref="UnitPoseOf"/> 压根没给则什么也不动（只推一次警告）。
    ///
    /// <para><see cref="_Process"/> 每帧调它。宿主也可以自己调 —— 逻辑刚推进完立刻摆一次，
    /// 那一帧就不用等下一个渲染帧。两条路摆的是同一份读数，重复调不累积（只写变换）。</para>
    /// </summary>
    public void Sync()
    {
        if (UnitPoseOf is null)
        {
            WarnNoQuery();
            return;
        }

        // 边走边删会抛（Dictionary 在枚举中被改），所以先收键，走完再撤。
        List<string>? gone = null;
        foreach (var (key, attached) in _attached)
        {
            var pose = UnitPoseOf(attached.UnitId);
            if (pose is not null)
            {
                attached.Node.GlobalPosition = new Vector3((float)pose.Value.X, 0f, (float)pose.Value.Z);
                attached.Node.RotationDegrees = new Vector3(0f, (float)pose.Value.FacingDegrees, 0f);
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
            var attached = _attached[key];
            LostUnits++;
            Detach(attached.UnitId, attached.Track);
        }
    }

    /// <summary>
    /// 每帧两件事：<b>持有上限那一格</b>（<see cref="ExpireOneShotHolds"/>）与
    /// <b>摆一次</b>（spec D8 的「每帧同步位置」）。
    ///
    /// <para>两件事的次序无关紧要，写在一起是因为它们都是「每帧走一遍」的那类，
    /// 分成两个 override 只会让人以为能只调一个。</para>
    /// </summary>
    public override void _Process(double delta)
    {
        ExpireOneShotHolds(delta);
        Sync();
    }

    /// <summary>
    /// 一行读数（<c>vfx 演 N｜借不出 M｜在池 K/T｜超时归还 R</c>）。
    /// 格式只有 <see cref="VfxLedger.Readout"/> 那一处加本件这一段。
    ///
    /// <para><b>后一段「新建 / 复用」是通用池接进来才有的</b>：<see cref="VfxLedger"/> 记的是
    /// 「槽位借与还」（它判峰值丢弃），池子记的是「节点造与复用」（它管的是节点）。
    /// 两个数看的是同一局的两个面，缺任一个都会漏故障 ——
    /// <b>「新建」涨而「演」不涨就是池在空转</b>（每演一条都新造一个，槽位白借）。</para>
    /// </summary>
    public string Readout()
        => $"{Ledger.Readout()}｜超时归还 {Num(ExpiredReturns)}｜节点新建 {Num(PoolCreatedTotal)}｜池轨数 {Num(_pools.Count)}";

    /// <summary>
    /// 各轨池累计造过的节点总数 —— <b>复用有没有发生的证据</b>：这一格不涨，
    /// 就是每演一条都新造一个，池子在空转。
    /// </summary>
    private long PoolCreatedTotal
    {
        get
        {
            long total = 0;
            foreach (var pool in _pools.Values) total += pool.CreatedCount;
            return total;
        }
    }

    private static string Num(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// 持有上限那一格：<b>一次性的那几条按倍率缩的持有时长上限，到点自己归还</b>。
    ///
    /// <para><b>只扫一次性档那份名册</b>（<see cref="_borrowed"/>）：持续档挂着的那几条
    /// 槽位由宿主显式撤下收，不靠时长（那一档压根不抛 <c>finished</c>，
    /// 拿上限去收它等于替宿主猜「该撤了」）。</para>
    ///
    /// <para><b>倒着扫</b>：<see cref="GiveBack"/> 会从名册里摘掉那一条，正着扫会跳格
    /// （倒扫安全：一个节点在 <see cref="_borrowed"/> 里就不在空闲栈里，
    /// 摘的必然是刚看的那一条）。</para>
    ///
    /// <para><b>幂等不在这一格判，在 <see cref="GiveBack"/> 里</b>：上限与 <c>finished</c>
    /// 撞上时哪条路先到算哪条，后到那条摘不到名册就返回
    /// （<see cref="VfxLedger.Return"/> 自己不幂等，多还一次会把在池数推高）。
    /// 两边各判一次就多出一条判据要维护，而护栏已经在那儿了。</para>
    /// </summary>
    private void ExpireOneShotHolds(double delta)
    {
        if (delta <= 0)
        {
            return;   // 帧长非正（首帧或引擎给 0）时不动持有时长：宁可晚还，不凭空推快
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
    /// 这一条还能占着槽位多久（秒，<c>double</c>）：<b><c>lifetime ÷ 倍率</c> 加一段按帧折的滞后</b>。
    ///
    /// <para><b>倍率 ≤ 1 时不设上限</b>（交 <c>double.PositiveInfinity</c>）：上限是为抵消
    /// 「<c>speed_scale</c> 把池压紧」而存在的，倍率不压紧时它无事可抵。留着它反而多一条
    /// 归还路径要与 <c>finished</c> 抢 —— 而那一格上「上限比真实散尽还早」的余量只有几帧，
    /// 抢输了就是粒子还在天上飞槽位先回来了。<b>倍率 0（暂停粒子）落在这一格</b>：
    /// 上限不缩，暂停不会把池清空（类注那一段）。</para>
    ///
    /// <para><b>滞后那一段按帧折、不写死秒数</b>：引擎从 <c>Restart()</c> 到画面散尽、
    /// 从散尽到抛 <c>finished</c> 各有几帧滞后（240 fps 实测 5 帧，
    /// <c>FxHit</c> 1× 画面第 97 帧、信号第 102 帧）。滞后随帧长变，
    /// 写死成秒数在低帧率下就变成「卡得比真实散尽还早」。取的方向是<b>宁可多占一会儿</b>：
    /// 帧长变大只会把上限推得更远，退回「照 <c>finished</c> 走」那条老路，不会反过来切早。</para>
    /// </summary>
    /// <param name="node">那条池节点（<c>lifetime</c> 从效果资源那份读，不另设参数）。</param>
    /// <param name="lagSeconds">这一帧折出来的滞后秒数（<see cref="HoldLagFrames"/> × 帧长）。</param>
    private double HoldCapSeconds(GpuParticles3D node, double lagSeconds)
        => _gameSpeed > 1f
            ? node.Lifetime / _gameSpeed + lagSeconds
            : double.PositiveInfinity;

    /// <summary>
    /// 离树即交还：借出去的那些先把账还平（挂着的那些与 <c>finished</c> 还没抛的那些各是一批），
    /// 空闲栈里那些包装引用也一并丢掉（那些节点由引擎随本节点一起回收）。
    /// 离树之后那个包装对象只是 <c>Dispose</c>，再碰它抛 <c>ObjectDisposedException</c>。
    ///
    /// <para>本节点不搬树：搬走时子节点跟着走，而本类已经把引用交还了，池账与节点数从此对不上。
    /// 要搬先让 <see cref="Play"/> / <see cref="Attach"/> 那一批完结（没借出的时候搬是安全的）。</para>
    /// </summary>
    public override void _ExitTree()
    {
        foreach (var borrowed in _borrowed)
        {
            Ledger.Return();
        }

        // 挂着的那几条：只还账，不 ClearParticles —— 节点马上随本节点一起回收，
        // 多清一次是白干；账要还平是另一回事（不还的话重启一局池账从头开始就少了几条）。
        foreach (var attached in _attached.Values)
        {
            Ledger.Return();
        }

        _attached.Clear();
        _borrowed.Clear();
        // 池子那份全清空：节点随本节点一起回收了，池里再留着就是一批失效引用
        // （下一次借出弹一个已经释放的节点，那一格当场就崩在池里，不是慢慢烂）。
        foreach (var pool in _pools.Values) pool.Clear();
        _pools.Clear();
        _nodes.Clear();   // 名册上同样不能留着（留着就是一批失效引用）
    }

    /// <summary>
    /// 拿一个能演这条轨的节点：先问这条轨的池（复用），池空才按 <c>res://</c> 路径建一个新的。
    /// 返回 <c>null</c> 的三种情形都推一行警告 —— 效果资源是原型侧递进来的，读不到就是作者那一侧的事，
    /// 静默跳过只会变成「画面上少了一个效果，谁也不知道少了哪个」。
    ///
    /// <para><b>建节点那一格是 <c>PoolOf</c> 的 <c>createFunc</c></b>：池空时由它造，
    /// 造不出来（资源读不到 / 类型不对 / 档位不对）就交回 <c>null</c>，池子据此记一次「借不出」
    /// 并返回 <c>null</c>。峰值丢弃不在这一格 —— 那是 <see cref="VfxLedger"/> 的决定，
    /// 账本先判「池空就丢」，压根不进这里。</para>
    /// </summary>
    /// <param name="track">轨名（也是节点名与种子派生那一份的输入）。</param>
    /// <param name="path">效果 <c>res://</c> 路径。</param>
    /// <param name="expectOneShot">
    /// 这一条路要哪种资源：<c>true</c> = 一次性档（<c>one_shot = true</c>，演一遍靠
    /// <c>finished</c> 收槽位）；<c>false</c> = 持续档（<c>one_shot = false</c>，循环发射，
    /// 槽位由宿主显式撤下时收）。<b>对不上就是误用</b>，当场推警告并拒绝。
    /// </param>
    private GpuParticles3D? Take(string track, string path, bool expectOneShot)
    {
        var node = PoolOf(track, path, expectOneShot).Get();
        return node is null
            ? null
            : RegisterBorrowed(node);
    }

    /// <summary>
    /// 这条轨的池（第一次用时开一个）。
    ///
    /// <para><b>四个委托各归一件事</b>，与三个引擎的池子同一口径：
    /// <c>createFunc</c> 建节点（含档位校验与那三行警告）、<c>actionOnGet</c> 补一次
    /// <c>speed_scale</c>、<c>actionOnRelease</c> 停演、<c>actionOnDestroy</c> 释放节点。
    /// <b>不递归还回调里的种子重装</b>：种子由 <see cref="PlayOneShot"/> 与 <see cref="Attach"/>
    /// 在摆好位之后写，重装在那两格（与 spec D12/D13 一致，且幂等）。</para>
    ///
    /// <para><b>容量按轨给</b>，与 <see cref="VfxLedger"/> 那份账同一个数（<see cref="Ledger.Capacity"/>）：
    /// 两个数必须同源，分开给就会出现「账说还有空位、池说满了」这种对不上的读数。
    /// <b><c>collectionCheck</c> 关掉</b>：归还那两路（<c>finished</c> 与显式撤下）
    /// 都走 <see cref="GiveBack"/> 那道「摘不到名册就返回」的幂等护栏，
    /// 已经挡住多还一次，池子自己再抛一次是把同一个作者错误报两遍。</para>
    /// </summary>
    private ObjectPool<GpuParticles3D> PoolOf(string track, string path, bool expectOneShot)
    {
        if (!_pools.TryGetValue(track, out var pool))
        {
            pool = new ObjectPool<GpuParticles3D>(
                createFunc: () => BuildNode(track, path, expectOneShot),
                actionOnGet: node => SetSpeedScale(node, _gameSpeed),
                actionOnRelease: node => node.Emitting = false,
                // 满池销毁：从全量名册摘掉并让引擎释放。这一格不写就成了泄漏——
                // 池满了但节点既不进池也不释放，画面上只剩「节点数慢慢涨」。
                actionOnDestroy: node =>
                {
                    _nodes.Remove(node);
                    node.QueueFree();
                },
                collectionCheck: false,
                maxSize: Ledger.Capacity);
            _pools[track] = pool;
        }

        return pool;
    }

    /// <summary>
    /// 按 <c>res://</c> 路径建一个粒子节点：读资源 → 查根节点类型 → 查档位 → 改名 → 取材质副本 → 进树。
    ///
    /// <para>这是 <c>PoolOf</c> 的 <c>createFunc</c>。<b>任何一步没过都交回 <c>null</c></b>
    /// （池子据此记「借不出」），并推一行警告说明是哪一头不对 —— 效果资源是原型侧递进来的，
    /// 读不到就是作者那一侧的事，静默跳过只会变成「画面上少了一个效果，谁也不知道少了哪个」。</para>
    /// </summary>
    private GpuParticles3D? BuildNode(string track, string path, bool expectOneShot)
    {
        if (!_scenes.TryGetValue(track, out var packed))
        {
            packed = ResourceLoader.Load<PackedScene>(path);
            _scenes[track] = packed;
        }

        if (packed is null)
        {
            GD.PushWarning($"[vfx] 轨「{track}」那张效果没读到：{path} —— 按 res:// 路径加载失败，这条不演");
            return null;
        }

        var instance = packed.Instantiate();
        if (instance is not GpuParticles3D particles)
        {
            GD.PushWarning($"[vfx] 轨「{track}」那张效果的根节点是 {instance.GetType().Name}，不是 GPUParticles3D —— 池摆不下它，这条不演");
            instance.QueueFree();
            return null;
        }

        if (particles.OneShot != expectOneShot)
        {
            // 两个方向都是误用，各有各的表现，所以两句话都写清楚是哪一头不对。
            GD.PushWarning(expectOneShot
                ? $"[vfx] 轨「{track}」那张效果 one_shot = false —— 那是持续档的资源（循环发射、不抛 finished，槽位收不回来），走挂上/撤下那条路（Sustain / Attach）"
                : $"[vfx] 轨「{track}」那张效果 one_shot = true —— 持续档要 one_shot = false 的资源（循环发射），这张演一遍就没了，挂上去等于什么都没有");
            instance.QueueFree();
            return null;
        }

        particles.Name = $"Vfx_{track}";
        particles.MaterialOverride = DuplicateTintSource(particles, track);
        AddChild(particles);
        return particles;
    }

    /// <summary>
    /// 借出那一刻把节点登记进 <see cref="_nodes"/>（<c>GameSpeed</c> 推倍率要遍历它）。
    ///
    /// <para><b>为什么复用节点也要过这一格</b>：<c>_nodes</c> 是全量名册，
    /// 而池子那条 <c>actionOnGet</c> 只管 <c>speed_scale</c>。登记放在借出这一格，
    /// 新建与复用走同一行，两条路不必分。<c>HashSet</c> 自己去重，
    /// 同一个节点复用 N 次也只留一条。</para>
    /// </summary>
    private GpuParticles3D RegisterBorrowed(GpuParticles3D node)
    {
        _nodes.Add(node);
        return node;
    }

    /// <summary>写一个节点的 <c>speed_scale</c>，去抖（同一个值不重复写引擎）。</summary>
    private static void SetSpeedScale(GpuParticles3D node, float value)
    {
        if (!Mathf.IsEqualApprox(node.SpeedScale, value)) node.SpeedScale = value;
    }

    /// <summary>
    /// 取一份可单独染色的材质副本（从效果资源那份复制，副本归本节点）。
    /// 效果资源是共享资源，在它身上改颜色会一次染上所有同轨实例。
    ///
    /// <para>副本<b>一个字都不重设</b>：<c>vertex_color_use_as_albedo</c>、加色混合、渐变末端淡到黑
    /// 这几项效果资源里已经调对了，复制过来就是对的；在这里另搭一份材质会踩「看着对、实际没生效」
    /// 那一类坑（4.7 的 <c>vertex_color_use_as_albedo</c> 默认关，粒子颜色会整个不进 shader）。</para>
    ///
    /// <para>读到的那份不是 <c>StandardMaterial3D</c> 就交回 <c>null</c> 并推一行警告：
    /// 那种材质不认 <c>AlbedoColor</c>，<see cref="Play"/> 递进来的颜色会被无声丢掉。</para>
    /// </summary>
    private static StandardMaterial3D? DuplicateTintSource(GpuParticles3D particles, string track)
    {
        var source = particles.DrawPass1 switch
        {
            PrimitiveMesh primitive => primitive.Material,
            { } mesh when mesh.GetSurfaceCount() > 0 => mesh.SurfaceGetMaterial(0),
            _ => null,
        };

        if (source is StandardMaterial3D standard)
        {
            return (StandardMaterial3D)standard.Duplicate();
        }

        GD.PushWarning($"[vfx] 轨「{track}」那份效果的材质不是 StandardMaterial3D，<c>Play</c> 递进来的颜色不生效（粒子照演）");
        return null;
    }

    /// <summary>
    /// 演完了：<b>断开这一条自己那个 <c>finished</c> 回调 → 摘名册 → 停演 → 还回这条轨的池 → 账上归还</b>。
    ///
    /// <para><b>断开是这一格的头一步</b>，与 <see cref="PlayOneShot"/> 里的 <c>+=</c> 成对：
    /// 只接不断的话，同一个节点复用 N 次之后信号上就积着 N 个回调，每演完全走一遍 N 回 ——
    /// 那一格<b>画面上完全看不出区别</b>（下面那道护栏照样把它挡住，池账也是对的），
    /// 只有节点复用次数够多才显形，而那时读数读不出来。</para>
    ///
    /// <para><b>摘不到名册就直接返回</b>：那是「一次借出只还一次账」的护栏 ——
    /// <see cref="VfxLedger.Return"/> 自己不幂等，多还一次会把在池数推高，
    /// 借不出与在池那两个数从此对不上。断开之后，正常路径已经走不到这里（回调摘掉了）；
    /// 留着是因为还账那一格只该发生一次，而这一次比较的开销是一个整数比，
    /// 换掉的是「账被还多一次」那种读数再也读不平的故障。<b>它也顺带替池子挡住了多还一次</b>
    /// （池子那件 <c>collectionCheck</c> 关着，见 <see cref="PoolOf"/>）。</para>
    /// </summary>
    private void GiveBack(GpuParticles3D node)
    {
        // 倒着找那一条：一个节点在借出名册里至多有一条（它在 _borrowed 里就不在池的空闲栈里）。
        var index = _borrowed.Count - 1;
        for (; index >= 0; index--)
        {
            if (ReferenceEquals(_borrowed[index].Node, node))
            {
                break;
            }
        }

        if (index < 0)
        {
            return;   // 摘不到：这一次借出的账已经还过了
        }

        var borrowed = _borrowed[index];
        _borrowed.RemoveAt(index);
        borrowed.Node.Finished -= borrowed.OnFinished;   // 接一次断一次，成对
        borrowed.Node.Emitting = false;   // 先停演，否则这条还可能再抛一次 finished
        PoolFor(borrowed.Track).Return(borrowed.Node);
        Ledger.Return();
    }

    /// <summary>
    /// 这条轨的池（按轨名取，不递建池那些参数）—— 归还那一路用它。
    ///
    /// <para>取不到就是作者错误（没借出过的节点不会有轨名），当场抛而不是兜一个空池：
    /// 兜空池的话，归还那一发就退化成「还进一个没人读的池」，节点既不演也不再被借，
    /// 而画面上只是「这个效果没再演过」—— 那是读不出来的那种失灵。</para>
    /// </summary>
    private ObjectPool<GpuParticles3D> PoolFor(string track)
        => _pools.TryGetValue(track, out var pool)
            ? pool
            : throw new InvalidOperationException(
                $"轨「{track}」还没有池，却要往里归还：这一条没走过借出那一路（或借出时那个池没建成）");

    /// <summary>挂着的那些的键：<c>单位/轨</c>（与 <c>CueFeed</c> 那份同一个口径，两边对得上才排得出同一轨）。</summary>
    private static string Key(string unitId, string track) => $"{unitId}/{track}";

    /// <summary>「宿主没递 <see cref="UnitPoseOf"/>」那行警告只推一次：每帧推一次会把日志刷满（见「长跑看门狗」）。</summary>
    private void WarnNoQuery()
    {
        if (_warnedNoQuery)
        {
            return;
        }

        _warnedNoQuery = true;
        GD.PushWarning("[vfx] 宿主没递 UnitPoseOf（某个单位此刻在哪那个查询）—— 持续档挂不上也不同步。挂上的效果要跟着单位走，那份位置只能由宿主给");
    }

    /// <summary>
    /// 节点种子：从 <see cref="LogicSeed"/> 与轨名派生（spec D12 / D13）。
    /// <b>借出时重装</b>（<see cref="PlayOneShot"/> 与 <see cref="Attach"/> 各一处），
    /// 不是建节点那一刻定死、之后不再动 —— <see cref="Take"/> 建节点与复用节点是同一条路，
    /// 本层没有「建节点那一刻」那一格可供区别。
    /// <b>重装是幂等的</b>：这个函数只吃 <see cref="LogicSeed"/> 与轨名，同一轨同一个
    /// <see cref="LogicSeed"/> 算出来是同一个数，所以节点上那个种子前后一样，画面上也没有差别。
    /// 类注那份实测边界（第一次演与 <c>Restart()</c> 重播不逐字相同）说的是另一件事，与这里重不重装无关。
    /// FNV-1a 64 位混完取低 31 位 —— <c>GpuParticles3D.Seed</c> 是 <c>uint</c>，留一位符号位不用。
    /// </summary>
    private uint DeriveSeed(string track)
    {
        var hash = 14695981039346656037UL;
        foreach (var b in Encoding.UTF8.GetBytes(track))
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }

        hash ^= unchecked((ulong)(long)LogicSeed);
        hash *= 1099511628211UL;
        return (uint)(hash & 0x7FFFFFFFUL);
    }
}
