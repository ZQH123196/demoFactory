#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DemoFactory.GugaFramework.Abilities;
using DemoFactory.GugaFramework.Camera;
using DemoFactory.GugaFramework.I18n;
using DemoFactory.Prototypes.BattleArray.Logic;
using Godot;
// 逻辑层的方别与 Godot 自己的 Side 同名（CS0104）—— 同文件里两边都可能出现就必须起别名，
// 这是 BattleUnitViews 与 Main.cs 的同一口径，不是新发明。
using BattleSide = DemoFactory.Prototypes.BattleArray.Logic.Side;

namespace DemoFactory.Prototypes.BattleArray;

/// <summary>
/// 技能 cue 上屏 —— 表现层在技能层这一侧的<b>唯一</b>订阅者。
///
/// 它只做三件事，且一个字都不往逻辑层写（AGENTS 「表现层只读」「表现层只读」在这层的落点，A73 扫的就是这条）：
/// 1. 每<b>逻辑步</b>一次，把技能层新发的事件与状态读数交给 <see cref="SkillCueSpec"/> 翻成四类 cue；
/// 2. 把派发台 <see cref="CueFeed"/> 排出来那几条演成画面（飘字 / 色带 / 角标 / 指向线）；
/// 3. 把 <c>dropped=</c> 那行读数交给桥的只读动词 <c>cues</c>（2026-09-28 起 HUD 不再刷这行字）——
///    「为什么画面跟不上」不许靠人眼撞。
///
/// 三条设计后果，都写在代码里而不是只在文档里：
/// - <b>积压合法、丢失要有名字</b>：逻辑不阻塞，同一单位两个 clip 抢一条轨道（已删的设计档）。
///   挤爆的是最旧那条一次性 cue，计数与「丢了哪条」都在派发台那一份账里；
/// - <b>翻译按逻辑步、不按渲染帧</b>：同一状态每秒重推一遍会把「合并」计数刷成帧率的函数
///   （「时间基准」「报时间只认逻辑时钟」在这里的翻版）；
/// - <b>不进真值</b>：cue 与计数都不进快照、不进级 1 与级 2 的逐字基线 —— 它在真值下游。
///
/// 画面上的四类对应：
/// | 档 | 画面上是什么 | 载体（现成技能） |
/// |---|---|---|
/// | 一次性 | 目标头顶飘一刀的数字（命中闪白） | <c>warrior.rift</c> / <c>archer.pierce</c> / <c>rider.charge</c> |
/// | 持续 | 脚下那条色带，挂上就亮、撤下就灭 | <c>fx.guard_wall</c> / <c>fx.slam_slow</c> |
/// | 计数 | 名牌右上角那个 <c>×2</c> | <c>state.guard</c> 的技能段 + 效果段（A55 那一格的真世界版） |
/// | 带参数 | 身前一根指向线，长度＝半径，念出角度 | <c>tank.slam</c> 的形状查询 <c>slam_shape</c> |
/// </summary>
public partial class SkillCues : Node3D
{
    /// <summary>飘字从出现到散尽的<b>渲染</b>秒数（表现时长，不参与任何判定）。</summary>
    private const float FloatSeconds = 1.1f;

    /// <summary>屏幕上同时最多挂几条飘字（超了直接不再建节点 —— 派发台那边已经记过 dropped 了）。</summary>
    private const int MaxFloats = 24;

    private static readonly Color HitColor = new(1f, 0.92f, 0.4f);
    private static readonly Color RejectedColor = new(0.62f, 0.62f, 0.68f);
    private static readonly Color GuardColor = new(0.4f, 0.8f, 1f);
    private static readonly Color SlowColor = new(0.55f, 0.4f, 0.95f);
    private static readonly Color ShapeColor = new(1f, 0.55f, 0.3f);

    /// <summary>「在路上」那一格的颜色（橙是形状半径，青是飞行中，两种都读得出）。</summary>
    private static readonly Color FlightColor = new(0.35f, 0.9f, 0.85f);

    /// <summary>飞行指示那根线的定长（米）：它报的是「第几步落地」这个时间量，不是距离。</summary>
    private const float FlightBarMeters = 0.9f;

    private BattleWorld? _world;
    private TopDownRig? _rig;

    /// <summary>派发台：同轨怎么合并、挤爆了丢哪条、丢了怎么读得出来 —— 那份机制在公共层，这里只持一份。</summary>
    private readonly CueFeed _feed = new();

    /// <summary>这台原型的说明书：哪条事件算哪类、挂哪条轨、念成哪几个字（「按逻辑步翻一次」也归它判）。</summary>
    private readonly SkillCueSpec _spec = new();

    private readonly Dictionary<string, Node3D> _sustained = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Label3D> _counted = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Node3D> _shape = new(StringComparer.Ordinal);
    private readonly List<(Label3D Node, float Life)> _floats = new();

    /// <summary>形状指示在地上留多久（<b>渲染秒</b>，纯表现时长：它不供真值，逻辑早就不等它）。</summary>
    private const float ShapeSeconds = 1.4f;

    /// <summary>
    /// 带参数那一档的<b>下架时机</b>：派发台只在「参数变了」时发新值，而形状查询一辈子只发一次
    /// （<c>slam_shape</c> 之后不再有第二条），所以没有这份超时的话，第一次踏地之后那根橙线会
    /// <b>永久挂在地上</b> —— 第八轮出图抓到的（「在路上」那根线有 <c>settle</c> 会自己灭，这一档没有对应事件）。
    /// </summary>
    private readonly Dictionary<string, float> _shapeLife = new(StringComparer.Ordinal);

    /// <summary>挂上这一个世界的观测面（换一局就重挂，别指着已释放的世界 —— 「离树交还」）。</summary>
    public void Wire(BattleWorld world, TopDownRig rig)
    {
        Unwire();
        _world = world;
        _rig = rig;
        _spec.Reset();
        _feed.Reset();
    }

    /// <summary>换一局（重开直达口与演示档重跑用它）：清掉上一局的叠层与计数。</summary>
    public void Rebuild(BattleWorld world)
    {
        ClearVisuals();
        Wire(world, _rig ?? throw new InvalidOperationException("还没架相机，cue 无从换算屏幕口径"));
    }

    /// <summary>桥的 <c>cues</c> 动词用的读数（<c>cue 演 n｜合并 n｜dropped=n｜待播 n 轨</c> + 「丢了哪条」那段有界尾巴）。</summary>
    public string ReadoutFull() => _world?.Abilities is null ? "这一局没接技能层" : _feed.Readout();

    /// <summary>丢过几条一次性 cue（只有桥读这一个数，别在别处再算一遍）。</summary>
    public long DroppedCount => _feed.Dropped;

    /// <summary>
    /// 每渲染帧一次：先让说明书按<b>逻辑步</b>翻新账（一步只翻一次，那条规矩住在
    /// <see cref="SkillCueSpec.OnStep"/> 里，不在这里判），再把排出来的 cue 落成节点，最后推飘字的时长。
    /// <paramref name="speedMultiplier"/> 只用来缩「散尽」那 1.1 秒，让倍速下画面不糊成一坨。
    /// </summary>
    public void Mirror(float dt, double speedMultiplier)
    {
        if (_world is not { } world || dt <= 0f) return;
        _spec.OnStep(_feed, world);
        Play(_feed.Drain(), world);
        AdvanceFloats(dt, speedMultiplier);
        AgeShapes(dt, speedMultiplier);
    }

    private void Play(IReadOnlyList<CueRow> rows, BattleWorld world)
    {
        foreach (var row in rows)
        {
            if (world.Find(row.UnitId) is not { } unit) continue;   // 人都不在了，这一条不演
            var at = GroundOf(unit);
            switch (row.Kind)
            {
                case CueKind.OneShot:
                    SpawnFloat(at + new Vector3(0f, HeadHeight(unit) + 0.35f, 0f), row.Label,
                        row.Track == SkillCueSpec.RejectedTrack ? RejectedColor : HitColor);
                    break;
                case CueKind.Sustained:
                    SetSustained(row, at, unit);
                    break;
                case CueKind.Counted:
                    SetCounted(row, at, unit);
                    break;
                case CueKind.Descriptive:
                    SetShape(row, at, unit);
                    break;
            }
        }
    }

    // === 四档各自的节点 ===

    /// <summary>持续档：脚下一条色带。层里说「挂着」就亮，说「撤了」就灭 —— 不在这里自己判时长。</summary>
    private void SetSustained(CueRow row, Vector3 at, BattleUnit unit)
    {
        var key = row.UnitId + "/" + row.Track;
        if (row.Value <= 0)
        {
            if (_sustained.TryGetValue(key, out var off))
            {
                off.Visible = false;
                _sustained.Remove(key);
            }

            return;
        }

        if (!_sustained.TryGetValue(key, out var band))
        {
            var color = row.Track.Contains("guard", StringComparison.Ordinal) ? GuardColor : SlowColor;
            band = new MeshInstance3D
            {
                Name = $"Cue{key}",
                Mesh = new PlaneMesh { Size = new Vector2(1.15f, 0.22f) },
                MaterialOverride = Unshaded(color),
            };
            AddChild(band);
            _sustained[key] = band;
        }

        band.Visible = true;
        band.Position = at + new Vector3(0f, 0.04f + 0.09f * _sustained.Values.Count(v => v.Visible), 0f);
        band.Rotation = new Vector3(0f, yawOf(unit), 0f);
    }

    /// <summary>计数档：名牌右上角那个 <c>×n</c>。数字没变的那条压根不会排进来（同值合并），所以这里只管换字。</summary>
    private void SetCounted(CueRow row, Vector3 at, BattleUnit unit)
    {
        var key = row.UnitId + "/" + row.Track;
        if (row.Value <= 0)
        {
            if (_counted.TryGetValue(key, out var gone))
            {
                gone.Visible = false;
                _counted.Remove(key);
            }

            return;
        }

        if (!_counted.TryGetValue(key, out var mark))
        {
            mark = new Label3D
            {
                Name = $"Cue{key}",
                FontSize = 44,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Modulate = GuardColor,
                OutlineSize = 24,
                OutlineModulate = new Color(0f, 0f, 0f, 0.7f),
            };
            AddChild(mark);
            _counted[key] = mark;
        }

        mark.Text = Loc.T(row.Label);
        mark.Visible = true;
        mark.Position = at + new Vector3(0.75f, HeadHeight(unit) + 0.2f, 0f);
        mark.Scale = Vector3.One * PlateScale();
    }

    /// <summary>带参数档：身前一根指向线，长度＝形状半径（参数没变的那条不会排进来）。这是「打不着就别结算」那格在画面上的对应物。</summary>
    private void SetShape(CueRow row, Vector3 at, BattleUnit unit)
    {
        var key = row.UnitId + "/" + row.Track;
        if (row.Value <= 0)
        {
            if (_shape.TryGetValue(key, out var gone))
            {
                gone.Visible = false;
                _shape.Remove(key);
            }

            _shapeLife.Remove(key);
            return;
        }

        if (!_shape.TryGetValue(key, out var line))
        {
            // 两条轨同一种形状、两种含义：shape = 形状半径（米，线就画那么长），
            // flight = 「在路上」的定长指示（它的数字是「第几步落地」，不是距离 —— 拿它当米数会拉出 46 米一根线）
            var flight = row.Track == SkillCueSpec.FlightTrack;
            line = new Node3D { Name = $"Cue{key}" };
            line.AddChild(new MeshInstance3D
            {
                Name = "Bar",
                Mesh = new PlaneMesh { Size = new Vector2(0.18f, 1f) },
                MaterialOverride = Unshaded(flight ? FlightColor : ShapeColor),
            });
            line.AddChild(new Label3D
            {
                Name = "Text",
                FontSize = 34,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Modulate = flight ? FlightColor : ShapeColor,
                OutlineSize = 20,
                OutlineModulate = new Color(0f, 0f, 0f, 0.7f),
            });
            AddChild(line);
            _shape[key] = line;
        }

        var meters = row.Track == SkillCueSpec.FlightTrack ? FlightBarMeters : (float)row.Value;
        var bar = ((MeshInstance3D)line.GetChild(0)).Mesh as PlaneMesh;
        if (bar is not null) bar.Size = new Vector2(0.18f, Math.Max(0.2f, meters));
        ((MeshInstance3D)line.GetChild(0)).Position = new Vector3(0f, 0.05f, -meters * 0.5f);
        ((Label3D)line.GetChild(1)).Text = Loc.T(row.Label);
        ((Label3D)line.GetChild(1)).Position = new Vector3(0f, 0.5f, -meters);
        line.Visible = true;
        line.Position = at;
        line.Rotation = new Vector3(0f, yawOf(unit), 0f);
        _shapeLife[key] = ShapeSeconds;   // 每一发新参数重新给一次时长
    }

    /// <summary>到点就把形状指示收掉（没有第二条事件会来灭它，超时是唯一的天敌）。</summary>
    private void AgeShapes(float dt, double speedMultiplier)
    {
        if (_shapeLife.Count == 0) return;
        var rate = Math.Max(0.1f, (float)Math.Max(1.0, speedMultiplier));
        foreach (var (key, life) in _shapeLife.ToList())
        {
            var left = life - dt * rate;
            if (left > 0f)
            {
                _shapeLife[key] = left;
                continue;
            }

            _shapeLife.Remove(key);
            if (_shape.TryGetValue(key, out var line))
            {
                line.Visible = false;
                _shape.Remove(key);
            }
        }
    }

    /// <summary>一次性：头顶飘字，1.1 渲染秒（按倍率缩）内上浮并淡掉。挂到上限就不再造新节点。</summary>
    private void SpawnFloat(Vector3 at, string text, Color color)
    {
        if (_floats.Count >= MaxFloats) return;
        var label = new Label3D
        {
            Text = Loc.T(text),
            FontSize = 46,
            Position = at,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Modulate = color,
            OutlineSize = 26,
            OutlineModulate = new Color(0f, 0f, 0f, 0.8f),
        };
        AddChild(label);
        _floats.Add((label, FloatSeconds));
    }

    private void AdvanceFloats(float dt, double speedMultiplier)
    {
        var rate = Math.Max(0.1f, (float)Math.Max(1.0, speedMultiplier));
        for (var i = _floats.Count - 1; i >= 0; i--)
        {
            var (node, life) = _floats[i];
            life -= dt * rate;
            if (life <= 0f)
            {
                node.QueueFree();
                _floats.RemoveAt(i);
                continue;
            }

            node.Position += new Vector3(0f, dt * 0.7f, 0f);
            var alpha = Math.Clamp(life / FloatSeconds, 0f, 1f);
            node.Modulate = node.Modulate with { A = alpha };
            _floats[i] = (node, life);
        }
    }

    // === 小工具（尺寸口径照 BattleUnitViews：屏幕像素的东西跟着机位换算，「相机单源」） ===

    private static Vector3 GroundOf(BattleUnit unit) => new(unit.Position.X, 0f, unit.Position.Y);

    private static float HeadHeight(BattleUnit unit) => 1.95f;

    /// <summary>敌方在深度轴负侧、前脸朝 −Z ⇒ 我方朝 −Z 偏航 0、敌方转 180（与模型初始朝向同一口径）。</summary>
    private static float yawOf(BattleUnit unit) => unit.Side == BattleSide.Enemy ? Mathf.Pi : 0f;

    private float PlateScale() => _rig is null ? 1f : _rig.Distance / _rig.ReferenceDistance;

    private static StandardMaterial3D Unshaded(Color color) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        AlbedoColor = color,
    };

    private void ClearVisuals()
    {
        foreach (var node in _sustained.Values.Concat(_counted.Values.Cast<Node3D>()).Concat(_shape.Values))
        {
            node.QueueFree();
        }

        foreach (var (node, _) in _floats)
        {
            node.QueueFree();
        }

        _sustained.Clear();
        _counted.Clear();
        _shape.Clear();
        _shapeLife.Clear();
        _floats.Clear();
    }

    private void Unwire()
    {
        ClearVisuals();
        _world = null;
    }

    /// <summary>节点离开树时交还订阅与缓存（「离树交还」：别在调用点每次查 <c>IsInstanceValid</c>）。</summary>
    public override void _ExitTree() => Unwire();
}
