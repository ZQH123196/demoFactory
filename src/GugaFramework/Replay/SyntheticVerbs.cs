#nullable enable

namespace DemoFactory.GugaFramework.Replay;

/// <summary>
/// 合成输入那两条动词的名字与参数形状 —— 一份，所有原型共用（2026-10-05 用户裁决）。
///
/// <para><b>为什么能放在公共层，而「拾取谁」那类不能</b>：这两条记的是<b>玩家输入</b>，
/// 形状对每个原型都一样（一个 action 名或一组点击坐标），而重放端一律跳过它们
/// （机制见 <c>GugaFramework/DebugBridge/InputInjector.cs</c> 类注），所以原型之间
/// 没有任何差异要表达。相比之下「拾取谁」「落建成什么」是玩法语义，各原型不同，
/// 留在各自词表（「原型隔离」）。
/// 各原型要做的只是把这两个名字递进自己的 <c>VerbNames</c>，外加一句跳过实现。</para>
///
/// <para><b>为什么记进 Journal</b>：账上因此能读出「点了 (900,600)」与「因此建了一座酒馆」
/// 是相邻的两条 —— 玩家这一手是怎么打的，平时只有画面能回答它。
/// 它<b>只增可读性、不增断言能力</b>：重放端是纯 C#、没有引擎也没有光标，
/// 复现不了「引擎收到了一次点击」，真正的状态变更由紧随其后的逻辑写承担，那笔才是断言落点。</para>
///
/// <para><b>参数形状</b>：一律竖线分隔、位置固定（与 <see cref="ReplayArgs"/> 同一套口径）：
/// <code>名字|1或0</code>（action 按下/抬起）与 <code>x|y|按钮|1或0</code>（点击）。
/// 点位在入录时就定点化，区域设置改不了账上那一段的写法。</para>
///
/// <para><b>按下的两态都记</b>：漏一个会让账上出现「按下过但没抬起」的半个动作。</para>
/// </summary>
public static class SyntheticVerbs
{
    /// <summary>InputMap action 的按下与抬起（args = <c>名字|1或0</c>）。</summary>
    public const string Action = "SyntheticInputAction";

    /// <summary>鼠标键点击（args = <c>x|y|按钮|1或0</c>）。</summary>
    public const string Click = "SyntheticInputClick";

    /// <summary>action 动词要的段数。</summary>
    public const int ActionSegments = 2;

    /// <summary>点击动词要的段数。</summary>
    public const int ClickSegments = 4;

    /// <summary>按下态那一段的写法（与账上其余 token 同为不变文化整数）。</summary>
    public const string Pressed = "1";

    /// <summary>抬起态那一段的写法。</summary>
    public const string Released = "0";

    /// <summary>
    /// 词表全名 —— 各原型把它摊进自己的 <c>VerbNames</c>（照那一侧名单的既有写法）。
    /// </summary>
    public static string[] Names => [Action, Click];

    /// <summary>
    /// 这条动词在<b>重放端</b>的处理：<b>校验参数形状，规范化，然后返 true，不碰世界</b>。
    ///
    /// <para><b>为什么跳过</b>：重放端是纯 C#，没有引擎、没有光标、没有视口，
    /// <c>Input.ParseInputEvent</c> 那条玩家路径在重放世界里根本不存在。
    /// 硬要「复现」只会造一个假的世界：账上多了几笔推不动的动作，快照却没变 ——
    /// 那是把「验不了」伪装成「验过了」。⇒ 老老实实跳过，状态变更交给紧随其后的逻辑写。</para>
    ///
    /// <para><b>为什么还要校验参数</b>：词表里每一条都必须「收到坏 args 返 false 且不抛」
    /// （各原型的词表测试钉着）。而且坏参数进账本身就是账本损坏 —— 账是给人读的，
    /// 读到一段缺字段的输入行，没人会知道它缺了什么。</para>
    ///
    /// <para><b>实测坑</b>：词表测试用坏 args <c>"|"</c> 试动词，只查段数会漏判 ——
    /// <c>"|".Split('|')</c> 切出<b>两个空串</b>，段数正好等于 action 动词要的 2 段。
    /// ⇒ 段数之外逐段查空（2026-10-05 实测踩到，那条测试当场由红转绿）。</para>
    ///
    /// <para><b>规范化的是点位</b>：<c>x</c>/<c>y</c> 走定点文本（与 <c>ReplayArgs</c> 同一份
    /// 不变文化口径），免得区域设置把小数点写成逗号、账在两台机器上长得不一样。
    /// 其余各段本就是规范 token，原样。</para>
    /// </summary>
    /// <param name="args">入录时的参数串。</param>
    /// <param name="segments">该动词应有的段数。</param>
    /// <param name="canonicalArgs">规范化后的 args（Journal 记的就是它）。</param>
    /// <param name="reason">失败原因；成功时是空串。不进 Journal，给当次调用看。</param>
    public static bool Skip(
        string args, int segments, out string canonicalArgs, out string reason)
    {
        canonicalArgs = args;
        reason = string.Empty;
        if (!ReplayArgs.Split(args, segments, out var parts))
        {
            reason = $"合成输入参数要 {segments} 段，收到「{args}」";
            return false;
        }

        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part))
            {
                reason = $"合成输入有空参数段（收到「{args}」）";
                return false;
            }
        }

        // action 那一档（2 段）没有点位，直接规范化返回。
        if (segments != ClickSegments)
        {
            canonicalArgs = string.Join(ReplayArgs.Separator, parts);
            return true;
        }

        // 点击那一档：前两段是点位，解析不出就不是一次合法的注入（账上也不该有）。
        if (!ReplayArgs.TryNumber(parts[0], out var px) || !ReplayArgs.TryNumber(parts[1], out var py))
        {
            reason = $"合成点击的坐标解析不出（收到「{args}」）";
            return false;
        }

        canonicalArgs = string.Join(
            ReplayArgs.Separator,
            px.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
            py.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
            parts[2],
            parts[3]);
        return true;
    }
}
