#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace DemoFactory.GugaFramework.Vfx;

/// <summary>
/// 轨名 → 效果 <c>res://</c> 路径那张表 —— <b>原型侧递进来的内容，公共层不认识任何具体效果名</b>（spec D6）。
///
/// <para>照 <c>SkillCueSpec</c> 的分工：机制在公共层（怎么查、查不到怎么办、怎么记账），
/// 内容在原型侧（哪条轨对应哪个效果文件）。本类只做前者。</para>
///
/// <para><b>查不到不回错、不抛</b>：这张表是原型侧递的内容，漏登一条是作者的事，
/// 让整局崩在同一帧上没有好处。它回「查不到」并<b>计数</b>——
/// 「日志里没有」≠「没发生」，没演这件事必须读得出来（与 <c>CueFeed</c> 丢 cue 记 <c>Dropped</c> 同一口气）。</para>
///
/// <para>按 <c>res://</c> 引用、不按 uid（spec D3）。</para>
/// </summary>
public sealed class VfxSpec
{
    /// <summary>「查不到哪条轨」那段有界尾巴留几条（最新在后）。照 <c>RollingLog</c> / <c>CueFeed.DroppedLines</c> 那条有界缓冲的做法。</summary>
    public const int MissedTailKept = 8;

    private readonly Dictionary<string, string> _effects;

    private readonly List<string> _missedTail = [];

    /// <param name="effects">轨名 → 效果 <c>res://</c> 路径。给 <c>null</c> 等于一张空表（全查不到），不抛。</param>
    public VfxSpec(IReadOnlyDictionary<string, string>? effects = null)
    {
        _effects = new Dictionary<string, string>(StringComparer.Ordinal);
        if (effects is null) return;

        foreach (var (track, path) in effects)
        {
            _effects[track] = path;
        }
    }

    /// <summary>在册的轨有几条。</summary>
    public int TrackCount => _effects.Count;

    /// <summary>查不到过几回（同一条轨被两帧各问一遍就是两笔账：问几次是几回）。</summary>
    public long Unregistered { get; private set; }

    /// <summary>「查不到哪条轨」那段有界尾巴（最新在后）。空 = 一条都没查不到过。</summary>
    public IReadOnlyList<string> MissedTracks => _missedTail;

    /// <summary>
    /// 按轨名查效果路径。返回 <c>false</c> = 查不到（并已计入 <see cref="Unregistered"/>）；
    /// 此时 <paramref name="path"/> 交回 <c>null</c>。
    /// </summary>
    public bool TryGetEffect(string track, out string? path)
    {
        if (_effects.TryGetValue(track, out var found))
        {
            path = found;
            return true;
        }

        Unregistered++;
        _missedTail.Add(track);
        while (_missedTail.Count > MissedTailKept)
        {
            _missedTail.RemoveAt(0);
        }

        path = null;
        return false;
    }

    /// <summary>
    /// 一行读数：<c>vfx 表 N 条｜漏登 N</c>（短版，给 HUD）。
    /// 「漏登哪条」那段尾巴不进这一行 —— 一行念成三行会把底部那一片字全压住
    /// （与 <c>CueFeed</c> 短版 / 长版分家同一口径）。
    /// </summary>
    public string Readout()
        => $"vfx 表 {TrackCount.ToString(CultureInfo.InvariantCulture)} 条"
           + $"｜漏登 {Unregistered.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>换一局：漏登计数与那段尾巴一起归零。</summary>
    public void Reset()
    {
        Unregistered = 0;
        _missedTail.Clear();
    }
}
