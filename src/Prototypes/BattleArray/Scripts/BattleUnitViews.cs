#nullable enable
using System;
using System.Collections.Generic;
using DemoFactory.GugaFramework.Abilities;
using DemoFactory.GugaFramework.Animation;
using DemoFactory.GugaFramework.Camera;
using DemoFactory.GugaFramework.I18n;
using DemoFactory.Prototypes.BattleArray.Logic;
using Godot;
// 逻辑层用 System.Numerics（纯 C#，零 Godot 类型），表现层用 Godot 自己的向量类型 ——
// 两边都叫 Vector2 / Side，同文件里同时引用必须起别名，别让编译器替人猜是哪个（CS0104/CS0019）。
using NumVec2 = System.Numerics.Vector2;
using BattleSide = DemoFactory.Prototypes.BattleArray.Logic.Side;

namespace DemoFactory.Prototypes.BattleArray;

/// <summary>
/// 阵面镜像 —— 把逻辑世界里每个兵的样子摆上屏：模型、朝向、血条、名牌、走/跑/出手/阵亡四件事。
///
/// 三层架构里的「无状态表现层」：本节点只读 <see cref="BattleWorld"/> 的状态与事件流，
/// 一个字都不往逻辑层写（模型不许决定谁打谁）。
/// 兵种差异全部取自 <see cref="UnitCard"/> 那张表 —— 这里不许出现 <c>if (kind == Archer)</c> 这种分支，
/// 新加一个兵种应当只动数据（M5 的结论就是「兵种差异只在数据」）。
///
/// 架构位置：Scripts —— 表现层节点（吃 Godot 类型，因此不进 dotnet test）。
/// </summary>
public partial class BattleUnitViews : Node3D
{
    /// <summary>
    /// 走路档与奔跑档喂给 <see cref="ModelAnimator.Travel"/> 的口径值。
    /// ModelAnimator 的走/跑校验器定在 14「米每游戏秒」，那是无限领主那批单位（9~16）的速度口径；
    /// 本原型的逻辑移速是 1.2~3.2 米每秒（design.md「兵种数据表 v1」），照实喂会把八件全判成走路。
    /// 所以这里不喂实测速度、只喂档位：**该不该跑由兵种卡的 <see cref="UnitCard.Gallops"/> 说**
    /// （骑手奔跑、其余行走），实测位移只用来判「这一帧有没有在动」。
    /// 逻辑真值（谁走了多远）仍然只在逻辑层，这里改的只是腿。
    /// </summary>
    private const float WalkFeed = 8f;

    /// <summary>奔跑档（&gt;= ModelAnimator 的 14 米每游戏秒校验器）。</summary>
    private const float GallopFeed = 20f;

    /// <summary>判定「这一帧在动」的位移下限（米）—— 平滑阻尼的残差不能算走路。</summary>
    private const float MovingEpsilon = 0.0015f;

    /// <summary>造型资产根（.glb：骨架 + 蒙皮 + 动作全在文件里，运行时只加载，不换色、不缩放几何、不拼图元）。</summary>
    private const string ModelRoot = "res://src/Prototypes/BattleArray/Models/";

    private BattleWorld? _world;
    private TopDownRig? _rig;
    private readonly Dictionary<string, PackedScene?> _sceneCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UnitView> _views = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NumVec2> _lastLogicPos = new(StringComparer.Ordinal);

    /// <summary>
    /// 这一局的<b>一次性动作台账</b>（N-30 那一格）：起手动作与平A 各演了几次、发生在第几步、
    /// 被吞了几次、模型里有没有那条动作。桥 <c>windups</c> 读的就是它。
    /// 它在真值下游 —— 不进快照、不进级 1/级 2 的逐字基线（与 cue 与 <c>dropped=</c> 同一档）。
    /// </summary>
    public OneShotLedger Windups { get; } = new();

    /// <summary>挂上这一个世界的观测面：模型按出场顺序补齐，之后每帧镜像、每个事件推一次动画。</summary>
    public void Wire(BattleWorld world, TopDownRig rig)
    {
        Unwire();
        _world = world;
        _rig = rig;
        Windups.Reset();        // 换一局归零：别让下一局读到上一局的演出
        _eventCursor = 0;       // 事件游标跟着新世界重零（新一局的行为流从 0 长起）
        world.Attacked += OnAttacked;
        world.Died += OnDied;
        BuildAll(world);
    }

    /// <summary>换一局（重开直达口用）：清掉上一局的模型与订阅，再挂新世界。</summary>
    public void Rebuild(BattleWorld world)
    {
        Wire(world, _rig ?? throw new InvalidOperationException("阵面还没接相机架，PlateScale 无从换算"));
    }

    private void Unwire()
    {
        if (_world is null) return;
        _world.Attacked -= OnAttacked;
        _world.Died -= OnDied;
        _world = null;
    }

    private void BuildAll(BattleWorld world)
    {
        foreach (var child in GetChildren())
        {
            child.QueueFree();
        }
        _views.Clear();
        _lastLogicPos.Clear();

        foreach (var unit in world.Units)
        {
            Spawn(world, unit);
        }
    }

    /// <summary>每渲染帧一次：位置阻尼、腿的档位、朝向、血条与名牌。数据全部来自逻辑层，这里只读。</summary>
    public void MirrorAll(float dt, double speedMultiplier)
    {
        if (_world is null || dt <= 0f) return;

        var gameSpeed = Math.Max(1.0, speedMultiplier);
        var smooth = 1f - Mathf.Exp(-SmoothStiffness * (float)gameSpeed * dt);
        var plate = _rig is null ? 1f : _rig.Distance / _rig.ReferenceDistance;
        DrainWindups(_world);   // 起手动作按事件流演（一次喂多步也不漏），下面那一圈只管每帧的镜像

        foreach (var unit in _world.Units)
        {
            if (!_views.TryGetValue(unit.Id, out var view)) continue;

            var target = new Vector3(unit.Position.X, 0f, unit.Position.Y);
            var logicMoved = _lastLogicPos.TryGetValue(unit.Id, out var last)
                && (unit.Position - last).LengthSquared() > MovingEpsilon * MovingEpsilon;
            _lastLogicPos[unit.Id] = unit.Position;

            var before = view.Root.Position;
            view.Root.Position = before.Lerp(target, smooth);
            var step = view.Root.Position - before;

            if (view.Model is null || view.Dead) continue;   // 尸体冻在死亡那一帧，不再改腿与朝向

            view.Anim.GameSpeed = (float)gameSpeed;
            view.Anim.Travel(step.LengthSquared() > MovingEpsilon * MovingEpsilon || logicMoved
                ? (view.Card.Gallops ? GallopFeed : WalkFeed)
                : 0f);
            if (step.LengthSquared() > 1e-8f)
            {
                // 产物前脸朝 -Z（导出器垫的那 180°），所以这里用 -X/-Z 求偏航；只在真动的时候改，
                // 一直转的话原地站着的会抖成陀螺。
                view.Model.Rotation = new Vector3(view.Model.Rotation.X, Mathf.Atan2(-step.X, -step.Z), view.Model.Rotation.Z);
            }

            var ratio = unit.MaxHealth > 0 ? Mathf.Clamp(unit.Health / (float)unit.MaxHealth, 0f, 1f) : 0f;
            view.Bar.Scale = new Vector3(ratio, 1f, 1f);
            var material = (StandardMaterial3D)view.Bar.MaterialOverride!;
            material.AlbedoColor = ratio > 0.35f ? view.BarColor : HurtColor;   // 掉到三分之一以下转红：血条颜色只报「快不行了」

            // 施法读条：前摇那几步亮着，按「已经走了几步 / 前摇总步数」填。数据只问层，不在这里判相位。
            var casting = _world.Abilities?.CastingOf(unit.Id);
            view.CastBar.Visible = casting is not null;
            view.CastBack.Visible = casting is not null;
            if (casting is { } cast)
            {
                var progress = Mathf.Clamp(1f - cast.StepsRemaining / (float)cast.TotalSteps, 0f, 1f);
                view.CastBar.Scale = new Vector3(Math.Max(0.04f, progress), 1f, 1f);
            }

            // 起手动作不在这里演 —— 它由技能层的 activate 事件驱动（见 DrainWindups），
            // 按渲染帧采样状态那次实测演了 0 次：一次喂多步（桥 advance / 高倍快进）会把三五步的前摇整段跨过去。
            view.Label.Scale = Vector3.One * plate;
        }
    }

    /// <summary>上一次取到事件流的第几条（与 <see cref="SkillCueSpec"/> 同一个口径：按逻辑步取增量，不按帧）。</summary>
    private long _eventCursor;

    /// <summary>
    /// 技能层的 activate 形状事件 → 起手动作：**按事件流驱动，不按渲染帧采样状态**。
    ///
    /// 为什么走事件：起手那一段只有 2~3 个逻辑步（0.2~0.3 秒），而本方法是每渲染帧跑一次 ——
    /// 2026-09-28 实测，桥一发 <c>advance --steps 300</c>（一次喂 30 秒）之后台账念「windup_slash 演 0 次」，
    /// 因为那 3 步落在两个渲染帧之间，采样什么都看不见。事件流一条不丢，所以倍率与喂步方式都演得出来
    /// （「事件发射前置」那条契约说的就是这件事：关键决策必须有事件发射，表现层才有钩子可订阅）。
    /// </summary>
    private void DrainWindups(BattleWorld world)
    {
        var layer = world.Abilities;
        if (layer is null || layer.EventTotal <= _eventCursor) return;
        foreach (var ev in layer.EventsTail((int)(layer.EventTotal - _eventCursor)))
        {
            // activate 有两种形状（打自己 / 打目标），技能 ID 都读字段，不再抠 detail。
            var abilityId = ev.Payload switch
            {
                AbilityActivated activated => activated.AbilityId,
                AbilityActivatedAt activatedAt => activatedAt.AbilityId,
                _ => null,
            };
            if (abilityId is null) continue;
            if (SkillWindups.ClipFor(abilityId) is not { } clip) continue;   // 那 12 条按下即提交，没有起手段
            if (!_views.TryGetValue(ev.Subject, out var view)) continue;
            Record(view.Anim.PlayOneShot(clip), clip);
        }
        _eventCursor = layer.EventTotal;
    }

    /// <summary>位置阻尼刚度：逻辑 0.1 秒跳一格，不补这层就是 10 次/秒的明晃晃跳动。</summary>
    private const float SmoothStiffness = 18f;

    private static readonly Color HurtColor = new(1f, 0.2f, 0.15f);
    private static readonly Color OurBar = new(0.3f, 0.75f, 1f);
    private static readonly Color EnemyBar = new(1f, 0.45f, 0.25f);
    private static readonly Color OurPlate = new(0.75f, 0.88f, 1f);
    private static readonly Color EnemyPlate = new(1f, 0.8f, 0.7f);
    private static readonly Color CastBar = new(0.95f, 0.8f, 0.25f);

    // === 事件流 → 动画（逻辑层发什么，这里就演什么；不发就不演） ===

    /// <summary>
    /// 把一次「演不演得成」落进台账：演出去的记步号，被吞的记次数，模型里没这条动作的单独记一类
    /// （那一类不归 N-27 那笔观感账，归「登记表与 glb 漂了」——技能体检单的「挂号了却读不到」那一红就是它）。
    /// 阵亡与没有播放器两种不记：那是正常状态，记进去会把读数搅浑。
    /// </summary>
    private void Record(OneShotOutcome outcome, string clip)
    {
        switch (outcome)
        {
            case OneShotOutcome.Played:
                Windups.RecordPlayed(clip, _world?.Tick ?? 0L);
                break;
            case OneShotOutcome.Busy:
                Windups.RecordDropped(clip);
                break;
            case OneShotOutcome.ClipAbsent:
                Windups.RecordAbsent(clip);
                break;
        }
    }

    private void OnAttacked(AttackRow row)
    {
        if (!_views.TryGetValue(row.AttackerId, out var view)) return;
        Record(view.Anim.TriggerAttack(), view.Card.AttackClip);   // 吞没吞，这一格从此读得出来（N-27 的运行时半边）
        if (view.Dead) return;
        // 出手这一刻转身对着目标：弓手在射程外放箭，不转就是背对靶子射击。
        // （朝向着陆在表现层，不写回逻辑层 —— 谁打谁仍然是逻辑层那一条「打最近」决定的。）
        if (_world?.Find(row.TargetId) is not { } target || view.Model is not { } model) return;
        var delta = new Vector3(target.Position.X - view.Root.Position.X, 0f, target.Position.Y - view.Root.Position.Z);
        if (delta.LengthSquared() > 1e-6f)
            model.Rotation = new Vector3(model.Rotation.X, Mathf.Atan2(-delta.X, -delta.Z), model.Rotation.Z);
    }

    private void OnDied(DeathRow row)
    {
        if (!_views.TryGetValue(row.UnitId, out var view)) return;
        view.Dead = true;
        view.Anim.Die();
        view.Bar.Visible = false;
        view.BarBack.Visible = false;
        view.CastBar.Visible = false;
        view.CastBack.Visible = false;
        view.Label.Visible = false;
    }

    // === 单个兵的模型 ===

    private void Spawn(BattleWorld world, BattleUnit unit)
    {
        var card = unit.Look;
        var root = new Node3D
        {
            Name = $"U{unit.Id}",
            // 出生即落位：位移平滑从这一帧起才生效，否则新模型会从原点滑过来
            Position = new Vector3(unit.Position.X, 0f, unit.Position.Y),
        };
        var model = InstantiateModel(card.ModelName);
        if (model is not null)
        {
            // 初始朝向：产物前脸朝 -Z ⇒ 我方（+Z 侧）偏航 0 就对着敌人，敌方转 180 对着我方
            model.Rotation = new Vector3(0f, unit.Side == BattleSide.Player ? 0f : Mathf.Pi, 0f);
            root.AddChild(model);
        }

        // 头顶血条与名牌的尺寸取自模型自带的 Anchor 标记（.x＝血条宽，.y＝名牌高）——
        // 骑兵骑在两米七的马上，不该跟弓手共用一条细血条。
        var anchor = model?.GetNodeOrNull<Marker3D>("Anchor");
        var barWidth = anchor?.Position.X ?? 1.1f;
        var barY = (anchor?.Position.Y ?? 1.7f) - 0.25f;
        var ours = unit.Side == BattleSide.Player;
        var barColor = ours ? OurBar : EnemyBar;
        var labelColor = ours ? OurPlate : EnemyPlate;

        var barBack = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(barWidth, 0.14f, 0.02f) },
            MaterialOverride = Unshaded(new Color(0.05f, 0.05f, 0.05f)),
            Position = new Vector3(0, barY, 0),
        };
        root.AddChild(barBack);

        var bar = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(barWidth - 0.1f, 0.09f, 0.02f) },
            MaterialOverride = Unshaded(barColor),
            Position = new Vector3(0, barY, 0.01f),
        };
        root.AddChild(bar);

        // 施法读条：血条上面那一条，只在「前摇中」亮着（进度来自层里的相位，表现层不自判）。
        // 没有它的时候，一局里最要紧的那 0.2~0.3 秒在画面上什么都没有 ⇒ 用户原话「完全看不出技能的释放」。
        var castY = barY + 0.22f;
        var castBack = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(barWidth, 0.09f, 0.02f) },
            MaterialOverride = Unshaded(new Color(0.08f, 0.07f, 0.03f)),
            Position = new Vector3(0, castY, 0),
            Visible = false,
        };
        root.AddChild(castBack);

        var castBar = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(barWidth - 0.06f, 0.06f, 0.02f) },
            MaterialOverride = Unshaded(CastBar),
            Position = new Vector3(0, castY, 0.01f),
            Visible = false,
        };
        root.AddChild(castBar);

        var label = new Label3D
        {
            Text = Loc.T(card.DisplayName),
            FontSize = 40,
            Position = new Vector3(0, barY + 0.45f, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Modulate = labelColor,
        };
        root.AddChild(label);

        AddChild(root);
        _views[unit.Id] = new UnitView(root, model, new ModelAnimator(model), bar, barBack, castBar, castBack,
            label, card, barColor);
        _lastLogicPos[unit.Id] = unit.Position;
    }

    private static StandardMaterial3D Unshaded(Color color) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        AlbedoColor = color,
    };

    private Node3D? InstantiateModel(string modelName)
    {
        var path = ModelRoot + modelName + ".glb";
        if (!_sceneCache.TryGetValue(path, out var packed))
        {
            packed = GD.Load<PackedScene>(path);
            if (packed is null) GD.PushError($"[BattleArray] 模型加载失败：{path}（兵种表里有这一档，Models/ 下却没有件？）");
            _sceneCache[path] = packed;
        }
        if (packed is null) return null;
        if (packed.Instantiate() is not Node3D model)
        {
            GD.PushError($"[BattleArray] 模型根节点不是 Node3D：{path}");
            return null;
        }
        model.Name = "Model";
        return model;
    }

    /// <summary>一个兵在屏上的那一套节点。逻辑层的状态一个都不存 —— 每次镜像现读（Card 是数据不是状态）。</summary>
    private sealed record UnitView(
        Node3D Root,
        Node3D? Model,
        ModelAnimator Anim,
        MeshInstance3D Bar,
        MeshInstance3D BarBack,
        MeshInstance3D CastBar,
        MeshInstance3D CastBack,
        Label3D Label,
        UnitCard Card,
        Color BarColor)
    {
        public bool Dead { get; set; }
    }
}
