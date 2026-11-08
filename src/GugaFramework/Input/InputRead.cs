#nullable enable
using System;

namespace DemoFactory.GugaFramework.InputLayer;

// 读法分派 —— 读一个概念时走这里，按概念自己声明的读法去问引擎。
//
// 原型不再直接调引擎的按下/按住读法去读自己的 action：那一层现在只剩三件东西
//   · 概念名（InputConcept）—— 公共的
//   · 读法（ReadMode）—— 概念自己带的，必填
//   · 引擎那三个读数口 —— 由宿主在构造时递进来（这三个委托是本件唯一的引擎依赖面）
// 换引擎或换自动化注入方式时，改宿主那一行构造即可，读法分派这一段不动。

/// <summary>读法分派 —— 按概念声明的读法去问源。</summary>
public sealed class InputRead
{
    private readonly Func<string, bool> _justPressed;
    private readonly Func<string, bool> _held;
    private readonly Func<string, float> _axis;

    /// <summary>建一份读法分派。三个委托是引擎那一侧的三个读数口。</summary>
    /// <param name="justPressed">刚按下：一次物理按下只 true 一次。</param>
    /// <param name="held">按住：还按着就 true。</param>
    /// <param name="axis">模拟量：取动作强度。</param>
    /// <remarks>
    /// 引擎那一侧传进来的通常是 lambda 而不是方法组：引擎读法收的是 <c>StringName</c>，
    /// 而方法组转换不吃用户定义的隐式转换（<c>string</c> → <c>StringName</c> 是用户定义的），
    /// 直接写 <c>Input.IsActionJustPressed</c> 会报 CS1503。
    /// </remarks>
    public InputRead(Func<string, bool> justPressed, Func<string, bool> held, Func<string, float> axis)
    {
        ArgumentNullException.ThrowIfNull(justPressed);
        ArgumentNullException.ThrowIfNull(held);
        ArgumentNullException.ThrowIfNull(axis);
        _justPressed = justPressed;
        _held = held;
        _axis = axis;
    }

    /// <summary>当帧读一个概念 —— 按它自己声明的读法问源。</summary>
    /// <param name="concept">概念。</param>
    /// <returns>刚按下与按住两档回真假；模拟量那一档回真假是错的，抛。</returns>
    /// <exception cref="InvalidOperationException">这个概念声明的是模拟量。</exception>
    public bool Read(InputConcept concept)
    {
        ArgumentNullException.ThrowIfNull(concept);
        return concept.Read switch
        {
            ReadMode.JustPressed => _justPressed(concept.Name),
            ReadMode.Held => _held(concept.Name),
            _ => throw new InvalidOperationException(
                $"概念 {concept.Name} 是模拟量，要用 {nameof(ReadAxis)} 读："
                + "把它当真假读会把「按住多强」塌成「按没按」。"),
        };
    }

    /// <summary>读一个概念的模拟量强度。</summary>
    /// <param name="concept">概念。</param>
    /// <returns>强度（引擎给什么就回什么）。</returns>
    /// <exception cref="InvalidOperationException">这个概念声明的是刚按下。</exception>
    public double ReadAxis(InputConcept concept)
    {
        ArgumentNullException.ThrowIfNull(concept);
        if (concept.Read == ReadMode.JustPressed)
        {
            throw new InvalidOperationException(
                $"概念 {concept.Name} 是刚按下，没有强度可读：一次按下只有「有」与「没有」，"
                + "把它当模拟量读会得到一个每帧恒定的值。");
        }

        return _axis(concept.Name);
    }

    /// <summary>这一帧有没有接住 <paramref name="concept"/> 的一发刚按下。</summary>
    public bool JustPressed(InputConcept concept)
    {
        ArgumentNullException.ThrowIfNull(concept);
        if (concept.Read != ReadMode.JustPressed)
        {
            throw new InvalidOperationException(
                $"概念 {concept.Name} 的读法是 {concept.Read}，问「有没有刚按下」是问错了口径。");
        }

        return _justPressed(concept.Name);
    }

    /// <summary>
    /// 触发型读取：<b>当帧成立就用当帧的，当帧没成立才去窗口里找那一发</b>。
    ///
    /// <para>窗口只补「这一发本来会作废」的输入，不改任何已生效命令的时机：
    /// 当帧成立的那条路径根本不看缓冲，返回的 <paramref name="firedAtTick"/> 就是当前步。</para>
    /// </summary>
    /// <param name="concept">概念。只对刚按下那一档成立。</param>
    /// <param name="buffer">缓冲。</param>
    /// <param name="currentTick">当前步号。</param>
    /// <param name="firedAtTick">回传触发那一步的步号（当帧成立时就是当前步）。</param>
    /// <returns>这一发该生效返回 true。</returns>
    public bool ReadBuffered(InputConcept concept, InputBuffer buffer, long currentTick, out long firedAtTick)
    {
        ArgumentNullException.ThrowIfNull(concept);
        ArgumentNullException.ThrowIfNull(buffer);
        RequireJustPressed(concept);

        if (_justPressed(concept.Name))
        {
            firedAtTick = currentTick;
            return true;
        }

        if (buffer.TryConsume(concept, currentTick, out firedAtTick)) return true;

        firedAtTick = 0L;
        return false;
    }

    /// <summary>
    /// 看见一发刚按下、而这一帧来不及用 —— 记进缓冲等窗口里补。
    /// 返回 false 表示这一帧根本没有刚按下（那么什么都不记）。
    /// </summary>
    /// <param name="concept">概念。只对刚按下那一档成立。</param>
    /// <param name="buffer">缓冲。</param>
    /// <param name="currentTick">当前步号。</param>
    public bool Notice(InputConcept concept, InputBuffer buffer, long currentTick)
    {
        ArgumentNullException.ThrowIfNull(concept);
        ArgumentNullException.ThrowIfNull(buffer);
        RequireJustPressed(concept);

        if (!_justPressed(concept.Name)) return false;

        buffer.Record(concept, currentTick);
        return true;
    }

    /// <summary>
    /// 当帧消费，<b>不进缓冲也不查缓冲</b> —— 参数依赖步号的命令走这一条。
    ///
    /// <para>理由：那条命令的内容要读当前步的世界真值（例如「现在悬赏定价是多少」），
    /// 把它延到窗口里再执行，读到的就是另一个步号的数 —— 命令内容会跟着窗口变，
    /// 那就不是「补触发时刻」而是「改命令」了。所以这一条漏掉就是漏掉，不补。</para>
    /// </summary>
    /// <param name="concept">概念。只对刚按下那一档成立。</param>
    /// <returns>这一帧成立返回 true。</returns>
    public bool ReadSameFrameOnly(InputConcept concept)
    {
        ArgumentNullException.ThrowIfNull(concept);
        RequireJustPressed(concept);
        return _justPressed(concept.Name);
    }

    private static void RequireJustPressed(InputConcept concept)
    {
        if (!InputBuffer.Bufferable(concept))
        {
            throw new InvalidOperationException(
                $"概念 {concept.Name} 的读法是 {concept.Read}，不是触发型："
                + "缓冲与同帧消费这两条路都只对刚按下那一档成立。");
        }
    }
}
