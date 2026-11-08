#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DemoFactory.GugaFramework.Abilities;

/// <summary>
/// 快照读写：文本、可机读、行序固定。它同时是「状态可回跳」的口——
/// 存下来的一份文本能重建成同一个世界，分支对照与往返测试都靠它（已删的设计档、「16 条清单」D6/D7）。
///
/// 行形状：<c>类型;字段;字段</c>。派生行（<c>x</c> 当前值 / <c>t</c> tag 计数）不参与重建，
/// 重建后由 S2 现算，所以往返相等这件事顺带把 S2 的确定性也测了。
///
/// 值里出现空格就会把段格式的结构换散，所以这一份文本的取值只有四种：名字、枚举、数字、逗号表。
/// 宿主把一行并进重放段格式时靠「分号全换成空格」得到「段首两 token 是身份」的形状，
/// L2 语义档才兜得住尾列（账本 N-19，A53）。
/// </summary>
public static class AbilitySnapshot
{
    public const string Header = "ab1";

    public static string Format(double value) => AbilityNumbers.Format(value);

    /// <summary>
    /// 存。<paramref name="tables"/> 只用来做一件事：认出「这条效果是回充载体」，好把它的层数
    /// 念成 <c>charges=</c>（N-15 要的那一栏）。不给表也能存——那时那一格不念，其余一字不变。
    /// </summary>
    public static string Save(AbilityWorld world, AbilityTables? tables = null)
    {
        var sb = new StringBuilder();
        sb.Append(Header).Append(';').Append(world.Tick.ToString(CultureInfo.InvariantCulture))
            .Append(';').Append(world.NextSeq.ToString(CultureInfo.InvariantCulture));

        foreach (var unitId in world.UnitOrder)
        {
            var unit = world.Units[unitId];
            // 单位行只留身份：坐标那一格已从公共层拿掉（距离由宿主给），所以这行没有第二列。
            sb.Append('\n').Append("u;").Append(unitId);

            foreach (var name in unit.Base.Keys.OrderBy(n => n, StringComparer.Ordinal))
            {
                sb.Append('\n').Append("b;").Append(name).Append(';').Append(Format(unit.Base[name]));
            }

            foreach (var name in unit.Min.Keys.OrderBy(n => n, StringComparer.Ordinal))
            {
                if (unit.Min[name] == double.MinValue && unit.Max[name] == double.MaxValue)
                {
                    continue;
                }

                sb.Append('\n').Append("l;").Append(name).Append(';')
                    .Append(FormatBound(unit.Min[name])).Append(';').Append(FormatBound(unit.Max[name]));
            }

            foreach (var m in unit.Modifiers.OrderBy(m => m.AppliedAtTick).ThenBy(m => m.Seq))
            {
                sb.Append('\n').Append("m;").Append(m.Seq.ToString(CultureInfo.InvariantCulture))
                    .Append(';').Append(m.Target).Append(';').Append(m.Mode)
                    .Append(';').Append(Format(m.Magnitude))
                    .Append(';').Append(m.AppliedAtTick.ToString(CultureInfo.InvariantCulture))
                    .Append(';').Append(m.ExpiresAtTick?.ToString(CultureInfo.InvariantCulture) ?? "-")
                    .Append(';').Append(m.SourceId)
                    .Append(';').Append(m.SourceEffectId ?? "-")
                    .Append(';').Append(m.RequirementTags.Count == 0 ? "-" : string.Join(",", m.RequirementTags));
            }

            foreach (var e in unit.Effects.OrderBy(e => e.EffectId, StringComparer.Ordinal))
            {
                sb.Append('\n').Append("f;").Append(e.EffectId).Append(';').Append(e.Stacks)
                    .Append(';').Append(e.ExpiresAtTick?.ToString(CultureInfo.InvariantCulture) ?? "-")
                    .Append(';').Append(e.LastPeriodTick == long.MinValue ? "-"
                        : e.LastPeriodTick.ToString(CultureInfo.InvariantCulture));

                // 回充载体的那一格层数就是「还剩几层」——念成人读得出的 charges=（与 S1 读同一处）。
                if (tables is not null && tables.HasEffect(e.EffectId)
                    && tables.Effect(e.EffectId).IsChargeCarrier)
                {
                    sb.Append(";charges=").Append(e.Stacks.ToString(CultureInfo.InvariantCulture));
                }
            }

            foreach (var a in unit.Abilities.Values.OrderBy(a => a.Id, StringComparer.Ordinal))
            {
                sb.Append('\n').Append("a;").Append(a.Id).Append(';').Append(a.Phase)
                    .Append(';').Append(a.StepsRemaining.ToString(CultureInfo.InvariantCulture))
                    .Append(';').Append(a.CommitTick?.ToString(CultureInfo.InvariantCulture) ?? "-")
                    .Append(';').Append(a.CooldownEndsAtTick?.ToString(CultureInfo.InvariantCulture) ?? "-")
                    .Append(';').Append(a.TargetId ?? "-")
                    .Append(';').Append(a.RejectedReason)
                    .Append(';').Append(a.ChargesLeft.ToString(CultureInfo.InvariantCulture))
                    .Append(";q=").Append(a.QueuedInput ? 1 : 0);

                // 位移意图（rider.charge 那一格）：没有就不念，老快照逐字不变。
                // 形状与 p; 行那一位同源（从>到），读回走同一个 ReadMove。
                if (a.Move is { } intent)
                {
                    sb.Append(";move=").Append(Format(intent.FromX)).Append('>').Append(Format(intent.ToX));
                }
            }

            foreach (var p in unit.Pendings.OrderBy(p => p.Seq))
            {
                sb.Append('\n').Append("p;").Append(p.Seq.ToString(CultureInfo.InvariantCulture))
                    .Append(';').Append(p.ExecuteAtTick.ToString(CultureInfo.InvariantCulture))
                    .Append(';').Append(p.SourceId).Append(';').Append(p.EffectId)
                    .Append(';').Append(p.TargetIds.Count == 0 ? "-" : string.Join(",", p.TargetIds))
                    .Append(';').Append(Format(p.CalcValue))
                    .Append(';').Append(p.SourceAttrsSnapshot.Count == 0 ? "-"
                        : string.Join(",", p.SourceAttrsSnapshot
                            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                            .Select(kv => kv.Key + "=" + Format(kv.Value))));

                // 在路上的位移载荷：不写就没有「恢复后那一步走不到目的地」的静默分叉。
                if (p.Move is { } move)
                {
                    sb.Append(";move=").Append(Format(move.FromX)).Append('>').Append(Format(move.ToX));
                }
            }

            foreach (var c in unit.Counters.OrderBy(c => c.Key, StringComparer.Ordinal))
            {
                sb.Append('\n').Append("c;").Append(c.Key).Append(';').Append(Format(c.Value));
            }

            foreach (var name in unit.Current.Keys.OrderBy(n => n, StringComparer.Ordinal))
            {
                sb.Append('\n').Append("x;").Append(name).Append(';').Append(Format(unit.Current[name]));
            }

            foreach (var tag in unit.TagCounts.Keys.OrderBy(n => n, StringComparer.Ordinal))
            {
                sb.Append('\n').Append("t;").Append(tag).Append(';').Append(unit.TagCounts[tag]);
            }
        }

        return sb.ToString();
    }

    /// <summary>只重建原始数据；派生值交给新世界的 S2 现算（这就是往返测试要验的那件事）。</summary>
    public static AbilityWorld Load(string text)
    {
        var world = new AbilityWorld();
        AbilitySet? unit = null;
        var lines = text.Split('\n');

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var f = line.Split(';');
            switch (f[0])
            {
                case Header:
                    world.Tick = long.Parse(f[1], CultureInfo.InvariantCulture);
                    world.NextSeq = long.Parse(f[2], CultureInfo.InvariantCulture);
                    break;

                case "u":
                    unit = new AbilitySet { UnitId = f[1] };
                    world.Units[f[1]] = unit;
                    world.UnitOrder.Add(f[1]);
                    break;

                case "b":
                    Require(unit).Base[f[1]] = Parse(f[2]);
                    break;

                case "l":
                    Require(unit).Min[f[1]] = ParseBound(f[2]);
                    Require(unit).Max[f[1]] = ParseBound(f[3]);
                    break;

                case "m":
                    Require(unit).Modifiers.Add(new ModifierOnBody(
                        long.Parse(f[1], CultureInfo.InvariantCulture), f[2],
                        Enum.Parse<ModifierMode>(f[3]), Parse(f[4]),
                        long.Parse(f[5], CultureInfo.InvariantCulture),
                        f[6] == "-" ? null : long.Parse(f[6], CultureInfo.InvariantCulture),
                        f[7], f[8] == "-" ? null : f[8],
                        f[9] == "-" ? Array.Empty<string>() : f[9].Split(',')));
                    break;

                case "f":
                    Require(unit).Effects.Add(new EffectOnBody
                    {
                        EffectId = f[1],
                        Stacks = int.Parse(f[2], CultureInfo.InvariantCulture),
                        ExpiresAtTick = f[3] == "-" ? null : long.Parse(f[3], CultureInfo.InvariantCulture),
                        LastPeriodTick = f[4] == "-" ? long.MinValue
                            : long.Parse(f[4], CultureInfo.InvariantCulture),
                    });
                    break;

                case "a":
                    // 尾部两列（charges / q）是后加的：老快照没有它们也要读得出来，所以一律按长度取。
                    Require(unit).Abilities[f[1]] = new AbilityOnBody
                    {
                        Id = f[1],
                        Phase = Enum.Parse<AbilityPhase>(f[2]),
                        StepsRemaining = long.Parse(f[3], CultureInfo.InvariantCulture),
                        CommitTick = f[4] == "-" ? null : long.Parse(f[4], CultureInfo.InvariantCulture),
                        CooldownEndsAtTick = f[5] == "-" ? null : long.Parse(f[5], CultureInfo.InvariantCulture),
                        TargetId = f[6] == "-" ? null : f[6],
                        RejectedReason = Enum.Parse<AbilityRejectReason>(f[7]),
                        ChargesLeft = f.Length > 8 ? int.Parse(f[8], CultureInfo.InvariantCulture) : 0,
                        QueuedInput = f.Length > 9 && ReadFlag(f[9]),
                        Move = f.Length > 10 ? ReadMove(f[10]) : null,
                    };
                    break;

                case "p":
                    Require(unit).Pendings.Add(new PendingSettlement
                    {
                        Seq = long.Parse(f[1], CultureInfo.InvariantCulture),
                        ExecuteAtTick = long.Parse(f[2], CultureInfo.InvariantCulture),
                        SourceId = f[3],
                        EffectId = f[4],
                        TargetIds = f[5] == "-" ? Array.Empty<string>() : f[5].Split(','),
                        CalcValue = Parse(f[6]),
                        SourceAttrsSnapshot = ParsePairs(f[7]),
                        Move = f.Length > 8 ? ReadMove(f[8]) : null,
                    });
                    break;

                case "c":
                    Require(unit).Counters[f[1]] = Parse(f[2]);
                    break;

                default:
                    // x / t 是派生行，重建时忽略——它们由 S2 现算，回来必须一模一样。
                    break;
            }
        }

        return world;
    }

    public static double Parse(string text) => AbilityNumbers.Parse(text);

    /// <summary>读一个 0/1 旗标：<c>q=1</c> 与裸 <c>1</c> 都接得住，别的当场抛（不兜 0，理由同「桥只转发」）。</summary>
    private static bool ReadFlag(string text)
    {
        var at = text.IndexOf('=');
        var value = at < 0 ? text : text[(at + 1)..];
        return value switch
        {
            "1" => true,
            "0" => false,
            _ => throw new KeyError($"旗标读不懂 | {text}（只接 0 / 1）"),
        };
    }

    /// <summary>读一列位移载荷 <c>move=从&gt;到</c>；写成别的形状当场抛，不静默丢一笔在路上。</summary>
    private static (double FromX, double ToX)? ReadMove(string text)
    {
        var at = text.IndexOf('=');
        if (at < 0 || text[..at] != "move")
        {
            throw new KeyError($"位移列读不懂 | {text}（要的是 move=从>到）");
        }

        var parts = text[(at + 1)..].Split('>');
        if (parts.Length != 2)
        {
            throw new KeyError($"位移列读不懂 | {text}（要的是 move=从>到）");
        }

        return (double.Parse(parts[0], CultureInfo.InvariantCulture),
            double.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    private static string FormatBound(double value)
        => double.IsNegativeInfinity(value) ? "-inf" : double.IsPositiveInfinity(value) ? "+inf" : Format(value);

    private static double ParseBound(string text)
        => text == "-inf" ? double.MinValue : text == "+inf" ? double.MaxValue : Parse(text);

    private static IReadOnlyDictionary<string, double> ParsePairs(string text)
    {
        if (text == "-")
        {
            return new Dictionary<string, double>();
        }

        var dict = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var pair in text.Split(','))
        {
            var kv = pair.Split('=');
            dict[kv[0]] = Parse(kv[1]);
        }

        return dict;
    }

    private static AbilitySet Require(AbilitySet? unit)
        => unit ?? throw new KeyError("快照缺单位行 | 每个数据行前必须有一条 u; 行");
}
