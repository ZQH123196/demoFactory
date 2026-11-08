#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using DemoFactory.GugaFramework.Vfx;
using Godot;

namespace DemoFactory.Prototypes.BeastArena.Show;

/// <summary>
/// 表现导演 —— 一次事件的三样（特效、音效、结果标记）从这一个口出去，
/// 第四样「状态变化」由模型本身与逻辑读数承担（形状 / 受损 / 尸体 / 名牌）。
///
/// <para><b>九类只认 <see cref="ShowCategory"/></b>：事件 → 类别由 DeckView 那一侧翻，
/// 本类不认具体游戏事件，也不另造事件名（对接面清单冻结口径）。</para>
///
/// <para><b>结果标记不跟着粒子走</b>：粒子演完就散，标记在锚点上方留 4 秒，并进一份有界账
/// （<see cref="Readout"/> 最近 16 条）。「效果消失后关键结果仍可追查」靠这两条落地。</para>
/// </summary>
public partial class ShowDirector : Node
{
    private const int MarkKept = 24;
    private const int LogKept = 16;
    private const double MarkSeconds = 4.0;
    private const float MarkLaneHeight = 1.2f;

    private readonly VfxPlayer _vfx;
    private readonly AudioDirector _audio = new() { Name = "AudioDirector" };
    private readonly List<Mark> _marks = [];
    private readonly List<string> _log = [];
    private int _cueCount;

    private sealed class Mark
    {
        public Label3D Label = null!;
        public double Remaining;
    }

    public ShowDirector(Node owner, Node3D vfxHost, int seed)
    {
        Name = "ShowDirector";
        owner.AddChild(this);
        AddChild(_audio);

        _vfx = new VfxPlayer
        {
            Name = "Vfx",
            LogicSeed = seed,
            Spec = new VfxSpec(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["impact"] = ShowCues.VfxTracks["impact"],
                ["ring"] = ShowCues.VfxTracks["ring"],
                ["mark"] = ShowCues.VfxTracks["mark"],
            }),
        };
        vfxHost.AddChild(_vfx);
    }

    public VfxPlayer Vfx => _vfx;

    public AudioDirector Audio => _audio;

    public void GameSpeed(float speed)
    {
        _vfx.GameSpeed = speed;
        _audio.GameSpeed = speed;
    }

    public void Bed(string? cue) => _audio.Bed(cue);

    /// <summary>
    /// 发一类事件的三样：特效落在锚点包围盒中心上方、音效走目录、屏幕标记挂在锚点上。
    /// </summary>
    /// <param name="category">九类之一。</param>
    /// <param name="anchor">落点锚（甲板上的单位 / 建筑 / 生态对象节点都行）。</param>
    /// <param name="text">结果标记文字（由调用方按那一刻的读数拼）。</param>
    /// <param name="heightOffset">标记相对锚点再抬高多少米。</param>
    /// <param name="vfxY">特效落点的世界 Y（米）。不给 = 锚点当前位置 + 1 米；
    /// 锚点是巨兽这类贴地的节点时，甲板上的事件必须显式给甲板高度。</param>
    public void Fire(ShowCategory category, Node3D anchor, string text, float heightOffset = 3.0f, float? vfxY = null)
    {
        var cue = ShowCues.Of(category);
        var at = anchor.GlobalPosition;
        _vfx.Play(cue.VfxTrack, at.X, at.Z, y: vfxY ?? at.Y + 1.0);
        _audio.Cue(cue.AudioCue);
        Marker(anchor, text, heightOffset);
        _cueCount++;
    }

    /// <summary>只发特效与音效，不挂标记（用于每步都有的战斗接触，避免标记刷屏）。</summary>
    public void Spark(ShowCategory category, Node3D anchor)
    {
        var cue = ShowCues.Of(category);
        var at = anchor.GlobalPosition;
        _vfx.Play(cue.VfxTrack, at.X, at.Z, y: at.Y + 1.0);
        _audio.Cue(cue.AudioCue);
    }

    /// <summary>只挂一条结果标记（不推特效与音效）。锚点一般是单位或建筑节点。</summary>
    public void Marker(Node3D anchor, string text, float heightOffset = 3.0f)
    {
        var live = 0;
        foreach (var mark in _marks)
        {
            if (GodotObject.IsInstanceValid(mark.Label) && ReferenceEquals(mark.Label.GetParent(), anchor)) live++;
        }

        var label = new Label3D
        {
            Text = text,
            Position = new Vector3(0f, heightOffset + live * MarkLaneHeight, 0f),
            PixelSize = 0.016f,
            FontSize = 48,
            OutlineSize = 7,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            RenderPriority = 8,
            Modulate = new Color(1f, 0.85f, 0.3f),
        };
        anchor.AddChild(label);
        _marks.Add(new Mark { Label = label, Remaining = MarkSeconds });
        _log.Add(text);
        while (_log.Count > LogKept) _log.RemoveAt(0);
        while (_marks.Count > MarkKept) Expire(0);
    }

    public override void _Process(double delta)
    {
        for (var i = _marks.Count - 1; i >= 0; i--)
        {
            var mark = _marks[i];
            mark.Remaining -= delta;
            if (!GodotObject.IsInstanceValid(mark.Label))
            {
                _marks.RemoveAt(i);
                continue;
            }

            if (mark.Remaining <= 0.0)
            {
                mark.Label.QueueFree();
                _marks.RemoveAt(i);
                continue;
            }

            if (mark.Remaining < 0.6)
            {
                var color = mark.Label.Modulate;
                color.A = (float)(mark.Remaining / 0.6);
                mark.Label.Modulate = color;
            }
        }
    }

    private void Expire(int index)
    {
        var mark = _marks[index];
        if (GodotObject.IsInstanceValid(mark.Label)) mark.Label.QueueFree();
        _marks.RemoveAt(index);
    }

    public int LiveMarks => _marks.Count;

    /// <summary>换局：把还挂着的标记全收掉（旧局的事件不该留在新局屏上）。</summary>
    public void ClearMarks()
    {
        foreach (var mark in _marks)
        {
            if (GodotObject.IsInstanceValid(mark.Label)) mark.Label.QueueFree();
        }

        _marks.Clear();
        _log.Clear();
        _cueCount = 0;
    }

    public string Readout()
    {
        var last = _log.Count == 0 ? "-" : _log[^1];
        return $"show_cue={_cueCount.ToString(CultureInfo.InvariantCulture)}"
            + $" show_marks={_marks.Count.ToString(CultureInfo.InvariantCulture)}"
            + $" show_last={last.Replace(' ', '_')}"
            + $" · {_audio.Readout()}"
            + $" · {_vfx.Spec?.Readout() ?? "vfx 表 0 条"}";
    }
}
