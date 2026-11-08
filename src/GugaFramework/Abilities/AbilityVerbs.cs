#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace DemoFactory.GugaFramework.Abilities;

/// <summary>
/// 剧本词表 —— <b>一份表、两处问</b>（P3「一份清单三处跑」收的第一块）。
///
/// 在此之前，动词名与参数次序抄了两份：贴脸单挑场那份 <c>Inject</c>（十条，含现场扩的
/// <c>Move</c>）与开阔多点场那份 <c>Inject</c>（八条，<c>CastAbility</c> 的第 4 段
/// 位移目的地只有后者接了）。两份表各自漂的下场已经在仓里出现过一次：同一发命令在一侧接得住、
/// 在另一侧被<b>静默丢掉</b>（那格位移参数就是这一格），而静默丢掉的东西不会报错，只会让
/// 「注入的路径」与「玩家的路径」长成两回事（「桥只转发」 要防的正是这个）。
///
/// 现在的分工：
/// - <b>本文件</b>：动词名、参数次序、缺省值、怎么切竖线、什么时候该抛——只有一份；
/// - <b>各宿主的适配器</b>：只回答「这一侧哪个口子接得住」，一个动词名都不写（钉它的是 A68）。
///
/// 三条口径，都是实测换来的：
/// 1. <b>未知动词当场抛</b>，不在重放里静默少打一笔；
/// 2. <b>这一侧不接的动词也当场抛</b>（<see cref="IAbilityVerbSink.SupportedVerbs"/>），
///    不返回 <c>false</c>——<c>false</c> 是给「真打不成」留的读数（会被录进账，重放要比它），
///    拿它表达「这条词表没这一格」就把两件事搅成一锅；
/// 3. <b>参数给了却不生效 = 抛</b>：<c>CastAbility</c> 带第 4 段位移目的地，而这一侧声明不接
///    （<see cref="IAbilityVerbSink.AcceptsCastDestination"/>，H2 就在这一格）⇒ 当场抛，
///    不许演成「按下了、快照上也看不出少了一格」。不适用要在检查清单里写明理由，不许静默跳过。
///
/// 参数一律用竖线切，位置固定；缺的参数按位置留空，缺省值都写在这一份表里（见 <see cref="Parse"/>）。
/// </summary>
public interface IAbilityVerbSink
{
    /// <summary>报错时说清是哪一侧的宿主（「H2 单挑场」这类身份，不是技能语义）。</summary>
    string HostLabel { get; }

    /// <summary>
    /// 这一侧接得住哪几条动词。名单外的动词打过来 = 当场抛（口径 2）。
    /// 动词名只准用 <see cref="AbilityVerbs"/> 上那些常量，别在宿主侧写字面量（A68）。
    /// </summary>
    IReadOnlySet<string> SupportedVerbs { get; }

    /// <summary>
    /// 这一侧的 <c>CastAbility</c> 接不接第 4 段<b>位移（起点与目的地）</b>。
    /// 给了而这里为假 = 当场抛（口径 3）。H1 与 H3 接（层的 <c>Pending.Move</c> 有人落地），
    /// H2 不接——单挑场里位置是输入常量、不是技能的输出（那页 3 节与 12 节 待确认项 5 确认过）。
    /// </summary>
    bool AcceptsCastDestination { get; }

    /// <summary>放一条技能：剧本注入、桥动词、自动战斗都从这一发进，同一条路（「桥只转发」）。</summary>
    bool Cast(string unitId, string abilityId, string? targetId, (double FromX, double ToX)? move);

    /// <summary>显式取消在放的那条（commit 前后两种结果都从这一发走）。</summary>
    bool Cancel(string unitId, string abilityId);

    /// <summary>绕过技能直接挂效果；<paramref name="sourceId"/> = 「谁打的」，平A 与光环都要有名字。</summary>
    void ApplyEffect(string targetId, string effectId, long? durationTicks, string? sourceId);

    /// <summary>撤效果；<paramref name="stacks"/> = 0 表示整条撤，给了数就只消耗那几层。</summary>
    void RemoveEffect(string targetId, string effectId, int stacks);

    /// <summary>现场改一条属性（Override 与 clamp 的测试入口，不占技能名额）。</summary>
    void SetAttribute(string targetId, string attribute, double value, string mode, string[]? requirementTags);

    /// <summary>直接把某个基础值拧到某数（分支对照与剧本注入都要这一发）。</summary>
    void SetBase(string targetId, string attribute, double value);

    /// <summary>清冷却。</summary>
    void ClearCooldown(string unitId, string abilityId);

    /// <summary>蓄力档：外部改写剩余步数（清单页「松手」那一发）。</summary>
    bool SetStepsRemaining(string unitId, string abilityId, long steps);

    /// <summary>S5 位移；定身挂着走不动——激活检查第二处问的运行时出口（返回 false 并留一行 <c>move_blocked</c>）。</summary>
    bool Move(string unitId, double dx);
}

/// <summary>
/// 一次命令解析出来的样子。<b>参数次序与缺省值只有这一处定义</b>，宿主侧不再各写一套
/// （A69 腿 ① 读的就是这份解析结果）。
/// </summary>
/// <param name="Cmd">动词名。</param>
/// <param name="UnitId">第一个位置参数：施法者 / 被改的那个单位。</param>
/// <param name="AbilityId">第二条位置参数：技能 Id（<c>ApplyEffect</c> 那三条用它装效果 Id）。</param>
/// <param name="TargetId">被指的单位；<c>-</c> 与空 = 没有目标。</param>
/// <param name="Move">位移的起点与目的地（宿主世界里的坐标，层不持坐标也不替宿主算起点）；没给 = null。</param>
/// <param name="DurationTicks">效果持续几步；没给 = 照效果表自己那份。</param>
/// <param name="SourceId">「谁打的」；<c>-</c> 与空 = 没来源。</param>
/// <param name="Stacks">撤几层；0 = 整条撤。</param>
/// <param name="Attribute">属性名。</param>
/// <param name="Value">属性数值。</param>
/// <param name="Mode">修饰档：Add / Mult / Override。</param>
/// <param name="RequirementTags">修饰符自带生效条件（逗号表）；没给 = null。</param>
/// <param name="Steps">改写的前摇剩余步数。</param>
public sealed record AbilityVerbCall(
    string Cmd,
    string UnitId = "",
    string AbilityId = "",
    string? TargetId = null,
    (double FromX, double ToX)? Move = null,
    long? DurationTicks = null,
    string? SourceId = null,
    int Stacks = 0,
    string Attribute = "",
    double Value = 0,
    string Mode = "Add",
    string[]? RequirementTags = null,
    long Steps = 0);

/// <summary>
/// 词表本体：九个动词名 + <see cref="Parse"/> + <see cref="Dispatch"/>。
/// 新增一条动词只改这一个文件与两份适配器各一个方法——别在适配器里写 <c>case</c>（A68）。
/// </summary>
public static class AbilityVerbs
{
    public const string Cast = "CastAbility";

    public const string Cancel = "CancelAbility";

    public const string ApplyEffect = "ApplyEffect";

    public const string RemoveEffect = "RemoveEffect";

    public const string SetAttribute = "SetAttribute";

    public const string SetBase = "SetBase";

    public const string ClearCooldown = "ClearCooldown";

    public const string SetStepsRemaining = "SetStepsRemaining";

    public const string Move = "Move";

    /// <summary>九个动词名（录制包装、桥、CLI 与检查清单都从这里取，不各自抄一份名单）。</summary>
    public static readonly string[] Names =
    [
        Cast, Cancel, ApplyEffect, RemoveEffect, SetAttribute, SetBase, ClearCooldown,
        SetStepsRemaining, Move,
    ];

    /// <summary>
    /// 把一条命令按位置切成结构。<b>这条不发东西</b>，纯函数——所以「第 4 段到底是几」能单独判
    /// （A69），不用起任何一个世界。
    /// 必填参数缺了就当场抛，不兜 0（兜 0 = 静默摆到第 0 排，「桥只转发」 那句）。
    /// </summary>
    public static AbilityVerbCall Parse(string cmd, string args)
    {
        var f = args.Split('|');
        string At(int index, string fallback = "") => index < f.Length ? f[index] : fallback;
        string? Name(int index) => DashToNull(At(index));
        double Number(int index, string what) => ParseNumber(At(index), what, cmd);
        long Integer(int index, string what) => (long)Number(index, what);

        // 可省的那几格：空串与 "-" 都算「这一格没给」——两份适配器原先都是这么读的。
        double? OptionalNumber(int index, string what)
            => DashToNull(At(index)) is { } text
                ? double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture)
                : null;

        long? OptionalInteger(int index, string what) => (long?)OptionalNumber(index, what);

        switch (cmd)
        {
            case Cast:
                // 单位Id|技能Id|目标Id|位移(从>到)
                return new AbilityVerbCall(cmd,
                    UnitId: Required(At(0), "单位Id", cmd),
                    AbilityId: Required(At(1), "技能Id", cmd),
                    TargetId: Name(2),
                    Move: DashToNull(At(3)) is { } pair ? ReadMovePair(pair, cmd) : null);
            case Cancel:
                return new AbilityVerbCall(cmd,
                    UnitId: Required(At(0), "单位Id", cmd),
                    AbilityId: Required(At(1), "技能Id", cmd));
            case ApplyEffect:
                // 目标Id|效果Id|持续步|来源Id
                return new AbilityVerbCall(cmd,
                    UnitId: Required(At(0), "目标Id", cmd),
                    AbilityId: Required(At(1), "效果Id", cmd),
                    DurationTicks: OptionalInteger(2, "持续步数"),
                    SourceId: Name(3));
            case RemoveEffect:
                // 目标Id|效果Id|撤几层（不给或 0 = 整条撤）
                return new AbilityVerbCall(cmd,
                    UnitId: Required(At(0), "目标Id", cmd),
                    AbilityId: Required(At(1), "效果Id", cmd),
                    Stacks: (int)(OptionalInteger(2, "层数") ?? 0));
            case SetAttribute:
                {
                    // 目标Id|属性名|数值|档(缺省 Add)|生效条件 tag(逗号表)
                    var tags = Name(4);
                    return new AbilityVerbCall(cmd,
                        UnitId: Required(At(0), "目标Id", cmd),
                        Attribute: Required(At(1), "属性名", cmd),
                        Value: Number(2, "属性数值"),
                        Mode: At(3).Length == 0 ? "Add" : At(3),
                        RequirementTags: tags is null ? null : tags.Split(','));
                }

            case SetBase:
                // 目标Id|属性名|数值
                return new AbilityVerbCall(cmd,
                    UnitId: Required(At(0), "目标Id", cmd),
                    Attribute: Required(At(1), "属性名", cmd),
                    Value: Number(2, "属性数值"));
            case ClearCooldown:
                return new AbilityVerbCall(cmd,
                    UnitId: Required(At(0), "单位Id", cmd),
                    AbilityId: Required(At(1), "技能Id", cmd));
            case SetStepsRemaining:
                return new AbilityVerbCall(cmd,
                    UnitId: Required(At(0), "单位Id", cmd),
                    AbilityId: Required(At(1), "技能Id", cmd),
                    Steps: Integer(2, "剩余步数"));
            case Move:
                // 单位Id|走几米
                return new AbilityVerbCall(cmd,
                    UnitId: Required(At(0), "单位Id", cmd),
                    Value: Number(1, "位移米数"));
            default:
                throw new InvalidOperationException(
                    $"未知命令 {cmd}（词表由开发者现场扩——录制包装、桥、CLI 三边同加，改 {nameof(AbilityVerbs)} 那一份）");
        }
    }

    /// <summary>
    /// 解析 + 发到这一侧宿主。<b>返回值只有两种意思</b>：<c>true</c> = 这一发打成了，
    /// <c>false</c> = 宿主那儿真没打成（表里没这条技能/效果），这个 false 会被录进账、重放要比它。
    /// 「这一侧不接这条词」不走 false，走 <see cref="NotSupportedException"/>（口径 2）。
    /// </summary>
    public static bool Dispatch(IAbilityVerbSink sink, string cmd, string args)
    {
        var call = Parse(cmd, args);
        if (!sink.SupportedVerbs.Contains(cmd))
        {
            throw new NotSupportedException(
                $"{sink.HostLabel} 这一侧不接 {cmd}：不适用要在检查清单里写明理由，不许静默少一条");
        }

        if (cmd == Cast && call.Move is { } move && !sink.AcceptsCastDestination)
        {
            throw new NotSupportedException(
                $"{sink.HostLabel} 这一侧的 {Cast} 不接位移（给了 {move.FromX.ToString(CultureInfo.InvariantCulture)}" +
                $">{move.ToX.ToString(CultureInfo.InvariantCulture)} 却没地方落地）——位置在这侧不是技能的输出，" +
                "别演成「按下了但没走」");
        }

        return cmd switch
        {
            Cast => sink.Cast(call.UnitId, call.AbilityId, call.TargetId, call.Move),
            Cancel => sink.Cancel(call.UnitId, call.AbilityId),
            ApplyEffect => Run(() => sink.ApplyEffect(call.UnitId, call.AbilityId, call.DurationTicks,
                call.SourceId)),
            RemoveEffect => Run(() => sink.RemoveEffect(call.UnitId, call.AbilityId, call.Stacks)),
            SetAttribute => Run(() => sink.SetAttribute(call.UnitId, call.Attribute, call.Value, call.Mode,
                call.RequirementTags)),
            SetBase => Run(() => sink.SetBase(call.UnitId, call.Attribute, call.Value)),
            ClearCooldown => Run(() => sink.ClearCooldown(call.UnitId, call.AbilityId)),
            SetStepsRemaining => sink.SetStepsRemaining(call.UnitId, call.AbilityId, call.Steps),
            Move => sink.Move(call.UnitId, call.Value),
            _ => throw new InvalidOperationException($"未知命令 {cmd}"),
        };
    }

    /// <summary>表里查无这条技能/效果 = 这一发没打成，账上记 ok=false（与两份适配器原有口径一致）。</summary>
    private static bool Run(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (KeyError)
        {
            return false;
        }
    }

    private static string Required(string text, string what, string cmd)
    {
        if (text.Length == 0)
        {
            throw new FormatException($"{cmd} 缺{what}（必填参数不兜默认值）");
        }

        return text;
    }

    private static double ParseNumber(string text, string what, string cmd)
    {
        if (text.Length == 0)
        {
            throw new FormatException($"{cmd} 缺{what}（必填参数不兜默认值）");
        }

        return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>把 <paramref name="text"/> 当成数字读；空串与 "-" 都算「这一格没给」。</summary>
    private static string? DashToNull(string text) => text is "" or "-" ? null : text;

    /// <summary>
    /// 读第 4 段那一位位移载荷 <c>从&gt;到</c>。起点是宿主世界里的坐标，层不持坐标所以两个数都得给全；
    /// 只给一个数或写成别的形状当场抛，不拿 0 兜（「桥只转发」）。
    /// </summary>
    private static (double FromX, double ToX) ReadMovePair(string text, string cmd)
    {
        var parts = text.Split('>');
        if (parts.Length != 2)
        {
            throw new FormatException($"{cmd} 第 4 段读不懂 | {text}（要的是 从>到，两个数都得给）");
        }

        return (ParseNumber(parts[0], "位移起点", cmd), ParseNumber(parts[1], "位移目的地", cmd));
    }
}
