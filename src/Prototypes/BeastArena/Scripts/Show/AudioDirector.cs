#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using DemoFactory.Prototypes.BeastArena.Audio;
using Godot;

namespace DemoFactory.Prototypes.BeastArena.Show;

/// <summary>
/// 音效播放导演 —— 吃 <see cref="AudioCatalog"/> 的 cue 名，按 <see cref="AudioMixPolicy"/> 裁决，
/// 把裁决落成 <see cref="AudioStreamPlayer"/> 的播放与停止。
///
/// <para><b>为什么混音裁决不在这里重写</b>：80 毫秒同 cue 合并、警报 / 成长优先于普通反馈，
/// 那两条是纯函数、有离线测试；本类只把「此刻哪些声线还在响」递进去，照裁决结果执行。
/// 读数（发声 / 合并 / 让位 / 缺件）留在本类，HUD 与调试桥都读它。</para>
///
/// <para><b>声线池</b>：8 条一次性声线 + 3 条环境床。池满时按开始时间停掉最老的一条普通反馈；
/// 环境床一档只响一条（换床即停旧的），不走合并与抢占。</para>
/// </summary>
public partial class AudioDirector : Node
{
    private const int OneShotVoices = 8;
    private const int BedVoices = 3;

    private readonly AudioStreamPlayer[] _players = new AudioStreamPlayer[OneShotVoices];
    private readonly List<Active> _active = [];
    private readonly AudioStreamPlayer[] _beds = new AudioStreamPlayer[BedVoices];
    private string _bedCue = string.Empty;
    private int _nextPlayer;
    private int _nextBed;

    public float GameSpeed { get; set; } = 1f;

    public long Played { get; private set; }
    public long Merged { get; private set; }
    public long Blocked { get; private set; }
    public long Missing { get; private set; }

    private sealed record Active(AudioVoice Voice, AudioStreamPlayer Player);

    public override void _Ready()
    {
        for (var i = 0; i < _players.Length; i++)
        {
            _players[i] = new AudioStreamPlayer { Name = $"Cue{i}" };
            AddChild(_players[i]);
        }

        for (var i = 0; i < _beds.Length; i++)
        {
            _beds[i] = new AudioStreamPlayer { Name = $"Bed{i}", VolumeDb = -10f };
            AddChild(_beds[i]);
        }
    }

    /// <summary>
    /// 发一条 cue。返回 <c>true</c> = 这一下被受理（发声或被合并）；<c>false</c> = 让位或资产缺失。
    /// 未知 cue 当场抛（<see cref="AudioCatalog.Get"/> 的口径，不兜底）。
    /// </summary>
    public bool Cue(string cue)
    {
        var entry = AudioCatalog.Get(cue);
        if (_players[0] is null) return false;

        var now = (long)Time.GetTicksMsec();
        Prune(now);
        var voices = new List<AudioVoice>(_active.Count);
        foreach (var a in _active) voices.Add(a.Voice);

        var decision = AudioMixPolicy.Decide(now, cue, entry.Priority, voices);
        switch (decision.Outcome)
        {
            case MixOutcome.MergedSameCue:
                Merged++;
                return true;
            case MixOutcome.BlockedByHigherPriority:
                Blocked++;
                return false;
        }

        foreach (var stop in decision.ToStop)
        {
            for (var i = _active.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(_active[i].Voice, stop)) continue;
                _active[i].Player.Stop();
                _active.RemoveAt(i);
            }
        }

        var stream = GD.Load<AudioStream>(entry.ResPath);
        if (stream is null)
        {
            Missing++;
            GD.PushError($"[BeastArena] 声件读不到：{entry.ResPath}（cue={cue}）");
            return false;
        }

        var player = TakePlayer(now);
        player.Stream = stream;
        player.Play();
        _active.Add(new Active(
            new AudioVoice(cue, entry.Priority, now, (long)Math.Ceiling(stream.GetLength() * 1000.0)),
            player));
        Played++;
        return true;
    }

    /// <summary>
    /// 环境床：同一刻只响一条（<c>null</c> = 全停）。床不参与同 cue 合并与优先级抢占。
    /// </summary>
    public void Bed(string? cue)
    {
        var want = cue ?? string.Empty;
        if (want == _bedCue) return;
        _bedCue = want;
        foreach (var bed in _beds)
        {
            if (bed is not null) bed.Stop();
        }

        if (want.Length == 0 || _beds[0] is null) return;

        var entry = AudioCatalog.Get(want);
        if (!entry.Cue.StartsWith(AudioCatalog.AmbiencePrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException($"环境床只收 {AudioCatalog.AmbiencePrefix} 前缀的 cue：{want}", nameof(cue));
        }

        var stream = GD.Load<AudioStream>(entry.ResPath);
        if (stream is null)
        {
            Missing++;
            GD.PushError($"[BeastArena] 环境床读不到：{entry.ResPath}（cue={want}）");
            return;
        }

        if (stream is AudioStreamWav wav) wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        var player = _beds[_nextBed];
        _nextBed = (_nextBed + 1) % _beds.Length;
        player.Stream = stream;
        player.Play();
    }

    public string CurrentBed => _bedCue;

    public string Readout() =>
        $"audio_played={Played} audio_merged={Merged} audio_blocked={Blocked} audio_missing={Missing}"
        + $" audio_bed={(_bedCue.Length == 0 ? "-" : _bedCue)}";

    private void Prune(long now)
    {
        for (var i = _active.Count - 1; i >= 0; i--)
        {
            if (_active[i].Voice.IsPlayingAt(now)) continue;
            _active.RemoveAt(i);
        }
    }

    private AudioStreamPlayer TakePlayer(long now)
    {
        for (var offset = 0; offset < _players.Length; offset++)
        {
            var index = (_nextPlayer + offset) % _players.Length;
            if (_players[index].Playing) continue;
            _nextPlayer = (index + 1) % _players.Length;
            return _players[index];
        }

        // 全在响：停最老的一条普通反馈（警报 / 成长让位规则已经在那之前筛过一遍）。
        var oldest = 0;
        for (var i = 1; i < _active.Count; i++)
        {
            if (_active[i].Voice.StartMs < _active[oldest].Voice.StartMs) oldest = i;
        }

        var victim = _active[oldest].Player;
        victim.Stop();
        _active.RemoveAt(oldest);
        _nextPlayer = Array.IndexOf(_players, victim);
        _nextPlayer = (_nextPlayer + 1) % _players.Length;
        return victim;
    }
}
