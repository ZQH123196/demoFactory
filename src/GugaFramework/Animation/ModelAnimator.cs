#nullable enable
using System;
using System.Collections.Generic;
using Godot;

namespace DemoFactory.GugaFramework.Animation;

/// <summary>
/// 模型动画驱动器 — 把「逻辑状态」翻译成模型自带的一条动作。
///
/// 原型侧只报四件事：走多快（<see cref="Travel"/>）、出手了（<see cref="TriggerAttack"/>）、
/// 死了（<see cref="Die"/>）、升级了（<see cref="Flash"/>）。骨架长什么样、有哪些动作、每段多少秒，
/// 全部在模型的 AnimationPlayer 里（.glb 自带），这里不认识任何具体造型，也不认识任何具体游戏。
///
/// 模型没有 AnimationPlayer（建筑与道具本来就不动）时动画类方法静默不做事 ——
/// 同一段代码两种模型共用，不需要在表现层开分支。
///
/// 为什么直驱 AnimationPlayer 而不是 AnimationTree：clip 之间 0.15 秒交叉淡入
/// 已经看不出硬切，而 AnimationTree 要按单位数量付状态机实例的钱。
/// 真需要「速度连续混合」时换成 AnimationTree，对外这几个方法（<see cref="Travel"/> /
/// <see cref="TriggerAttack"/> / <see cref="PlayOneShot"/> / <see cref="Die"/>）不变。
/// </summary>
/// <summary>
/// 一次「演一条一次性动作」的请求落成了哪一格 —— 调用方要能把「没演」分成四类，
/// 否则读数只会念「什么都没发生」，而这四类的处置完全不同（演太多 = 观感、被吞 = N-27 那笔账、
/// 模型没这条 = 登记表与 glb 漂了、已阵亡 = 正常）。
/// </summary>
public enum OneShotOutcome
{
    /// <summary>演出去了。</summary>
    Played,

    /// <summary>正有一条一次性动作没演完，而这条请求不许抢占（平A 撞平A）。</summary>
    Busy,

    /// <summary>模型里没有登记这条 clip（登记表漂了，或这件模型本来就没做这条动作）。</summary>
    ClipAbsent,

    /// <summary>这个兵已经阵亡，尸体停在死亡那一帧。</summary>
    Dead,

    /// <summary>这件模型压根没有 AnimationPlayer（建筑与道具那一类）。</summary>
    NoPlayer,
}

public sealed class ModelAnimator
{
    /// <summary>
    /// clip 之间的交叉淡入时长（秒）。**短 clip 按比例缩**：见 <see cref="BlendFor"/> ——
    /// 0.15 秒的淡入套在 0.2 秒的动作上，整条有四分之三时间在插值里爬，姿势根本落不到位。
    /// </summary>
    private const float BlendTime = 0.15f;

    /// <summary>短 clip 的淡入上限比例：淡入最多占这条动作的 25%，其余留给姿势本体。</summary>
    private const float BlendFraction = 0.25f;

    /// <summary>
    /// 走 / 跑的分档阈值，口径是「米每游戏秒」。逻辑层的 Speed 是「每 0.1 秒走的米数」，
    /// ×10 才换算到这个口径：游侠 1.6→16、骑士 1.3→13、战士 1.2→12、波次怪 1.1~1.2→11~12、
    /// BOSS 护卫 1.1→11、巨魔 0.9→9。取 14 = 只有全程风筝走位的游侠算跑，其余一律走路。
    /// </summary>
    private const float RunThreshold = 14f;

    private const string IdleName = "idle";
    private const string WalkName = "walk";
    private const string RunName = "run";
    private const string DeathName = "death";
    private const string AttackPrefix = "attack_";

    private readonly Node? _model;
    private readonly AnimationPlayer? _player;
    private readonly string? _attack;
    private readonly HashSet<string> _clips = new();
    private string _base = string.Empty;
    private bool _oneShot;
    private bool _dead;
    private float _gameSpeed = 1f;

    public ModelAnimator(Node? modelRoot)
    {
        _model = modelRoot;
        if (modelRoot?.FindChild("AnimationPlayer", true, false) is not AnimationPlayer player)
            return;
        _player = player;
        foreach (var name in player.GetAnimationList())
            _clips.Add(name.ToString());
        foreach (var name in _clips)
        {
            if (name.StartsWith(AttackPrefix, System.StringComparison.Ordinal)) { _attack = name; break; }
        }
        player.AnimationFinished += OnAnimationFinished;
    }

    /// <summary>
    /// 游戏时间倍率（快进 = 每个渲染帧里推进多少逻辑步）。写进 AnimationPlayer.SpeedScale，
    /// 让演出跟着时间轴一起快进 —— 否则高倍快进下动作还按实时长度演，尸体就滑着步满场跑。
    /// </summary>
    public float GameSpeed
    {
        get => _gameSpeed;
        set
        {
            _gameSpeed = value;
            if (_player is not null && !Mathf.IsEqualApprox(_player.SpeedScale, value)) _player.SpeedScale = value;
        }
    }

    /// <summary>
    /// 按位移速度选基动作：站着 / 走 / 跑。速度口径是「米每游戏秒」（快进倍率由调用方除掉）。
    /// 一次性动作（出手、阵亡）演完之前不被打断。
    /// </summary>
    public void Travel(float speed)
    {
        if (_player is null || _dead || _oneShot) return;
        PlayBase(speed <= 0.05f ? IdleName : speed < RunThreshold ? WalkName : RunName);
    }

    /// <summary>
    /// 诊断读数：现在**想要**哪条基动作、**实际在播**哪条、以及要的这条在不在 clip 表里。
    ///
    /// 存在的理由：<see cref="Travel"/> 挑了 <c>walk</c> 不等于真的在演 <c>walk</c> ——
    /// clip 表里没有这条时 <see cref="PlayBase"/> 什么都不做，模型就静站着，**而画面上完全看不出区别**
    /// （静站姿可能就是某帧恰好对上）。「巨兽在走」这句话要有依据就靠这三栏对得上，
    /// 不能靠看图猜。「状态可查」那一格在动画上的落点。
    /// </summary>
    public string DebugReadout()
    {
        var playing = _player is not null && _player.IsPlaying()
            ? _player.CurrentAnimation.ToString()
            : "(none)";

        var have = _clips.Contains(_base) ? "yes" : "NO";
        return $"want={(_base.Length == 0 ? "(none)" : _base)} playing={playing} clipInTable={have}"
            + $" speed={_player?.SpeedScale ?? 0f:0.##}x oneShot={_oneShot} dead={_dead}";
    }

    /// <summary>出手：播一次攻击动作。返回值说清「演了没演、没演是哪一类」，调用方据此记账。</summary>
    public OneShotOutcome TriggerAttack() => _attack is null
        ? OneShotOutcome.ClipAbsent
        : TryPlay(_attack, mayInterrupt: false);

    /// <summary>
    /// 按名字演一条**一次性**动作 —— 技能前摇的起手动作走这一发（哪条技能挂哪条 clip 由宿主侧那份表说，
    /// 这里只认名字，不认技能，见「动作在模型」）。
    /// 模型里没登记那条 clip 时返回 <see cref="OneShotOutcome.ClipAbsent"/>，什么都不发生
    /// （与 <see cref="TriggerAttack"/> 同口径）。
    ///
    /// 与 <see cref="TriggerAttack"/> 的**唯一**区别是抢占方向：起手动作可以顶掉正在演的平A，
    /// 反过来不行。理由是这两件事的信息量不对等 —— 起手那一段交代的是「谁在蓄力、蓄的是哪一招」，
    /// 而平A 的挥砍少演一次没人看不出来（那一笔账记在 N-27，本轮不动它的守卫，只把它<b>数出来</b>）。
    /// </summary>
    public OneShotOutcome PlayOneShot(string clip) => TryPlay(clip, mayInterrupt: true);

    private OneShotOutcome TryPlay(string clip, bool mayInterrupt)
    {
        if (_player is null) return OneShotOutcome.NoPlayer;
        if (clip.Length == 0 || !_clips.Contains(clip)) return OneShotOutcome.ClipAbsent;
        if (_dead) return OneShotOutcome.Dead;
        // 一次性动作演完之前：平A 之间互不抢（旧的那条演完），起手动作允许顶掉它。
        if (_oneShot && !mayInterrupt) return OneShotOutcome.Busy;
        _oneShot = true;
        _player.Play(clip, BlendFor(clip));
        return OneShotOutcome.Played;
    }

    /// <summary>
    /// 这条 clip 该用多长的交叉淡入：取「定死 0.15 秒」与「这条动作的 25%」里小的那个。
    /// 后者专为短动作兜底 —— 0.15 秒的淡入套在一条 0.2 秒的动作上，四分之三的时间都在往姿势爬，
    /// 姿势还没落到位就要淡出，画面上等于没演（实测：0.3 秒前摇若挂 0.2 秒的 clip 就是这个样子）。
    /// </summary>
    private float BlendFor(string clip)
    {
        if (_player is null || !_clips.Contains(clip)) return BlendTime;
        var length = _player.GetAnimation(clip)?.Length ?? 1.0;
        return (float)Math.Min(BlendTime, length * BlendFraction);
    }

    /// <summary>
    /// 阵亡：播死亡动作并停在最后一帧，返回它在**现实时间**里占的秒数（调用方据此安排收尸）。
    /// 快进中 clip 被 SpeedScale 加速，所以现实秒数 = 动作长度 ÷ GameSpeed。
    /// 没有 death 动作时返回 0 —— 表现层就当场的老路子处理。
    /// </summary>
    public float Die()
    {
        if (_player is null || !_clips.Contains(DeathName)) return 0f;
        _dead = true;
        _oneShot = false;
        var seconds = (float)_player.GetAnimation(DeathName).Length / Math.Max(_gameSpeed, 1e-3f);
        _player.Play(DeathName, 0.1);
        return seconds;
    }

    /// <summary>
    /// 升级金光：整身叠一层加色金色，透明度 0→0.9→0（约 0.6 秒），演完撤掉叠加。
    /// 走 <see cref="GeometryInstance3D.MaterialOverlay"/> —— 模型自带材质是资产，运行时只读不改。
    /// 叠加层用引擎自带的 StandardMaterial3D：实测自写 Shader 当 overlay 在 4.7 上不了屏
    /// （同一网格换 StandardMaterial3D 立刻可见），别再试第二条路。
    /// tween 挂在模型节点上：模型释放，演出跟着停。
    /// </summary>
    public void Flash()
    {
        if (_model is null) return;
        var meshes = CollectMeshes(_model);
        if (meshes.Count == 0) return;

        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            VertexColorUseAsAlbedo = false,
        };
        foreach (var mesh in meshes) mesh.MaterialOverlay = material;
        var gold = new Color(1f, 0.85f, 0.2f);
        void SetAlpha(float a) => material.AlbedoColor = new Color(gold.R, gold.G, gold.B, a);

        var tween = _model.CreateTween();
        tween.TweenMethod(Callable.From<float>(f => SetAlpha(0.9f * f)), 0f, 1f, 0.2);
        tween.TweenMethod(Callable.From<float>(f => SetAlpha(0.9f * (1f - f))), 0f, 1f, 0.4);
        tween.Finished += () =>
        {
            foreach (var mesh in meshes)
                if (GodotObject.IsInstanceValid(mesh)) mesh.MaterialOverlay = null;
        };
    }

    /// <summary>
    /// 复位成「刚构造完」的样子 —— <b>给节点复用准备的那一发</b>
    /// （表现层拿 <c>ObjectPool&lt;T&gt;</c> 借还模型时，归还那一路接它）。
    ///
    /// <para><b>为什么必须有</b>：<see cref="_dead"/> 没有任何一条路把它写回 <c>false</c>
    /// （<see cref="Die"/> 置 true，<see cref="OnAnimationFinished"/> 的 death 分支不清它），
    /// 所以复用过一个「死过」的模型时，它借出来<b>一帧都不动</b> —— 而画面上完全看不出区别
    /// （静站姿可能就是某帧恰好对上）。<see cref="_oneShot"/> 与 <see cref="_base"/> 同理：
    /// 一条没演完的一次性动作会把后面若干次 <see cref="Travel"/> 全部挡掉。
    /// 这三档都不清，「池子复用」这件事在真机上就是静默失灵。</para>
    ///
    /// <para><b>不碰 AnimationPlayer 本身</b>：停播交给池子的 <c>actionOnRelease</c>
    /// （那一格才是藏节点/停处理的地方），本件只清自己那三档私有状态 ——
    /// 池子不认识它，它也不认识池子（照「职责单一」，池不该带一个「认识 ModelAnimator」的口）。</para>
    public void Recycle()
    {
        _dead = false;
        _oneShot = false;
        _base = string.Empty;
    }

    private static List<MeshInstance3D> CollectMeshes(Node parent)
    {
        var found = new List<MeshInstance3D>();
        foreach (var child in parent.GetChildren())
        {
            if (child is MeshInstance3D mesh) found.Add(mesh);
            found.AddRange(CollectMeshes(child));
        }
        return found;
    }

    private void PlayBase(string clip)
    {
        if (!_clips.Contains(clip) || _base == clip) return;
        _base = clip;
        // 基动作是「姿势的循环」，一次性动作（出手 / 阵亡）才演完就停。
        // 循不循环只能在这儿定：glTF 规范里没有这个字段，.glb 的 JSON 块压根不存，
        // 导回来一律是 LOOP_NONE —— 于是走满 0.92 秒就定格在迈步帧上滑行（2026-09-25 实锤）。
        var anim = _player!.GetAnimation(clip);
        if (anim is not null && anim.LoopMode != Godot.Animation.LoopModeEnum.Linear)
            anim.LoopMode = Godot.Animation.LoopModeEnum.Linear;
        _player.Play(clip, BlendTime);
    }

    private void OnAnimationFinished(StringName name)
    {
        var clip = name.ToString();
        if (clip == DeathName)
        {
            _oneShot = false;
            return;     // 尸体停在最后一帧，不再回到基动作
        }
        if (!_oneShot || clip != _attack) return;
        _oneShot = false;
        _base = string.Empty;   // 让下一帧 Travel 重新决定基动作
    }
}
