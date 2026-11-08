#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DemoFactory.GugaFramework.Replay;

namespace DemoFactory.Prototypes.BattleArray.Logic;

/// <summary>
/// 摆子口的接缝 —— 三条写命令落到哪一侧。<see cref="BattleWorld"/> 就是这一侧的实现
/// （它把命令转进挂着的那个 <see cref="DeploymentBoard"/>，写路径只有那个写入口）。
/// </summary>
public interface IDeploymentVerbSink
{
    /// <summary>摆一个兵到指定格。</summary>
    PlacementOutcome PlaceUnit(TroopKind kind, int row, int column);

    /// <summary>撤掉某一格的兵（钱退回额度）。</summary>
    PlacementOutcome RemoveUnit(int row, int column);

    /// <summary>把一格的兵挪到另一格（钱不动）。</summary>
    PlacementOutcome MoveUnit(int fromRow, int fromColumn, int toRow, int toColumn);
}

/// <summary>
/// 一次解析出来的样子。<b>参数次序只有 <see cref="DeploymentVerbs.Parse"/> 一处定义。</b>
/// </summary>
/// <param name="Cmd">动词名。</param>
/// <param name="Kind">兵种；只有 <c>place</c> 有它，别的动词这一格是 null。</param>
/// <param name="Row">行；撤子是那一格、挪位是起点。</param>
/// <param name="Column">列；同上。</param>
/// <param name="ToRow">挪位的终点行；别的动词不看它。</param>
/// <param name="ToColumn">挪位的终点列；别的动词不看它。</param>
/// <param name="CanonicalArgs">
/// 折过的那一种 token —— <b>Journal 记的就是它</b>。兵种键在这里折成
/// <see cref="BattleContract.KeyOf"/> 那一种写法，数字走 <see cref="ReplayArgs"/> 那份格式。
/// </param>
public sealed record DeploymentVerbCall(
    string Cmd, TroopKind? Kind, int Row, int Column, int ToRow, int ToColumn, string CanonicalArgs);

/// <summary>
/// 摆子三动词的词表 —— <c>place</c> / <c>remove</c> / <c>move</c>。
///
/// <para>这一份是「重放装配单源」里原型侧那半：动词名、参数次序、怎么切段、什么时候判否，
/// 只有这一处；参数编码与规范化走 <see cref="ReplayArgs"/>；Journal 的追加、检查点与
/// 「要不要记」走 <see cref="ReplayAssembly"/>。</para>
///
/// <para><b>规范化折的是兵种键的大小写</b>：<see cref="BattleContract.KindFromKey"/> 认大小写，
/// 于是 <c>TANK</c> 与 <c>tank</c> 是同一个兵种，而账上只留后一种写法 ——
/// 同一件事实在 Journal 里只有一种 token（数字那一半由 <see cref="ReplayArgs"/> 保证）。</para>
///
/// <para>三条口径照 <c>ConstructionVerbs</c> 与 <c>AbilityVerbs</c>：
/// ① 未知动词当场抛，不在重放里静默少打一笔；
/// ② 参数坏样子判否、不抛 —— <c>ok</c> 那一列才是权威，抛出去会让录进去的东西有一类重放不了；
/// ③ 世界不答应时回读数（含那句人话理由）并判否，这个 false 照样进账、重放端要比它。</para>
/// </summary>
public static class DeploymentVerbs
{
    /// <summary>摆一个兵（args：<c>兵种|行|列</c>）。</summary>
    public const string Place = "place";

    /// <summary>撤掉某一格的兵（args：<c>行|列</c>）。</summary>
    public const string Remove = "remove";

    /// <summary>挪位（args：<c>从行|从列|到行|到列</c>）。</summary>
    public const string Move = "move";

    /// <summary>词表全名（<c>DuelRiftSetup.VerbNames</c> 从这里取，不另抄一份）。</summary>
    public static readonly string[] Names = [Place, Remove, Move];

    /// <summary>这一串是不是摆子口的动词（区分大小写，与 Journal 里的写法一致）。</summary>
    public static bool IsVerb(string cmd) => Array.IndexOf(Names, cmd) >= 0;

    /// <summary>可选的兵种键清单 —— 键认不出时打回给调用方，不静默兜一个（「桥只转发」）。</summary>
    public static string KindKeys => string.Join(" / ", BattleContract.Kinds.Select(BattleContract.KeyOf));

    /// <summary><c>place</c> 那一条的 args 编码：<c>兵种|行|列</c>。</summary>
    public static string PlaceArgs(string kindKey, int row, int column)
        => ReplayArgs.Join(kindKey, ReplayArgs.Int(row), ReplayArgs.Int(column));

    /// <summary><c>remove</c> 那一条的 args 编码：<c>行|列</c>。</summary>
    public static string RemoveArgs(int row, int column)
        => ReplayArgs.Join(ReplayArgs.Int(row), ReplayArgs.Int(column));

    /// <summary><c>move</c> 那一条的 args 编码：<c>从行|从列|到行|到列</c>。</summary>
    public static string MoveArgs(int fromRow, int fromColumn, int toRow, int toColumn)
        => ReplayArgs.Join(ReplayArgs.Int(fromRow), ReplayArgs.Int(fromColumn),
            ReplayArgs.Int(toRow), ReplayArgs.Int(toColumn));

    /// <summary>
    /// 按位置切段并折成规范 token。<b>这条不发东西</b>，纯函数 —— 所以「哪几段、什么形状」
    /// 能单独判，不用起一个世界。读不懂时回 null 并给出理由，不抛（口径 ②）。
    /// </summary>
    public static DeploymentVerbCall? Parse(string cmd, string args, out string reason)
    {
        reason = string.Empty;
        args ??= string.Empty;
        switch (cmd)
        {
            case Place:
                {
                    if (!ReplayArgs.Split(args, 3, out var three))
                    {
                        reason = $"{Place} 参数要 3 段（兵种|行|列），收到 {Segments(args)} 段";
                        return null;
                    }

                    if (BattleContract.KindFromKey(three[0]) is not { } kind)
                    {
                        reason = $"兵种键读不懂「{three[0]}」（可用：{KindKeys}）";
                        return null;
                    }

                    if (!int.TryParse(three[1], out var row) || !int.TryParse(three[2], out var column))
                    {
                        reason = $"{Place} 的行列读不懂（行「{three[1]}」列「{three[2]}」，要整数）";
                        return null;
                    }

                    return new DeploymentVerbCall(cmd, kind, row, column, 0, 0,
                        PlaceArgs(BattleContract.KeyOf(kind), row, column));
                }

            case Remove:
                {
                    if (!ReplayArgs.Split(args, 2, out var two))
                    {
                        reason = $"{Remove} 参数要 2 段（行|列），收到 {Segments(args)} 段";
                        return null;
                    }

                    if (!int.TryParse(two[0], out var row) || !int.TryParse(two[1], out var column))
                    {
                        reason = $"{Remove} 的行列读不懂（行「{two[0]}」列「{two[1]}」，要整数）";
                        return null;
                    }

                    return new DeploymentVerbCall(cmd, null, row, column, 0, 0, RemoveArgs(row, column));
                }

            case Move:
                {
                    if (!ReplayArgs.Split(args, 4, out var four))
                    {
                        reason = $"{Move} 参数要 4 段（从行|从列|到行|到列），收到 {Segments(args)} 段";
                        return null;
                    }

                    if (!int.TryParse(four[0], out var fromRow) || !int.TryParse(four[1], out var fromColumn)
                        || !int.TryParse(four[2], out var toRow) || !int.TryParse(four[3], out var toColumn))
                    {
                        reason = $"{Move} 的格号读不懂（{string.Join('|', four)}，要整数）";
                        return null;
                    }

                    return new DeploymentVerbCall(cmd, null, fromRow, fromColumn, toRow, toColumn,
                        MoveArgs(fromRow, fromColumn, toRow, toColumn));
                }

            default:
                throw new InvalidOperationException(
                    $"未知命令 {cmd}（词表见 {nameof(DeploymentVerbs)}）：不适用要在检查清单里写明理由，不许静默少一条");
        }
    }

    /// <summary>
    /// 解析 + 折 token + 发到这一侧那个写入口。返回值两种意思：<c>true</c> = 这一发打成了；
    /// <c>false</c> = 世界那儿真没打成（格被占 / 越界 / 空格 / 前置条件），这个 false 会被录进账。
    ///
    /// <para>参数坏样子时原样入录（它照样是一条 <c>ok=false</c> 的账，重放端要比它）；
    /// 解析得动时入录的是折过的那一种 token。</para>
    /// </summary>
    public static bool Dispatch(IDeploymentVerbSink sink, string cmd, string args,
        out string canonicalArgs, out string reason)
    {
        ArgumentNullException.ThrowIfNull(sink);
        var text = args ?? string.Empty;
        canonicalArgs = text;

        if (Parse(cmd, text, out reason) is not { } call)
        {
            return false;
        }

        canonicalArgs = call.CanonicalArgs;
        return Report(Forward(sink, call), out reason);
    }

    /// <summary>把解析好的那一发打进这一侧（动词名到方法只有这一处对应）。</summary>
    private static PlacementOutcome Forward(IDeploymentVerbSink sink, DeploymentVerbCall call) => call.Cmd switch
    {
        Place => sink.PlaceUnit(call.Kind!.Value, call.Row, call.Column),
        Remove => sink.RemoveUnit(call.Row, call.Column),
        Move => sink.MoveUnit(call.Row, call.Column, call.ToRow, call.ToColumn),
        _ => throw new InvalidOperationException($"未知命令 {call.Cmd}"),
    };

    /// <summary>回执变读数：成了就不念理由，没成把那一句人话带出去（重放比的是 <c>ok</c> 那一列）。</summary>
    private static bool Report(PlacementOutcome outcome, out string reason)
    {
        reason = outcome.Ok ? string.Empty : outcome.Reason;
        return outcome.Ok;
    }

    /// <summary>给理由文案用的实收段数（<see cref="ReplayArgs.Split"/> 判否时不带 token 出来）。</summary>
    private static int Segments(string args) => (args ?? string.Empty).Split(ReplayArgs.Separator).Length;
}
