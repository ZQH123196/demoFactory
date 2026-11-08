#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DemoFactory.GugaFramework.FrontEnd;

/// <summary>菜单屏的五种身份。值即栈里的次序来源，不作他用。</summary>
public enum FrontEndScreen
{
    /// <summary>没有屏。栈空时栈顶就是它。</summary>
    None = 0,

    /// <summary>标题屏：游戏开始前那一屏。</summary>
    Title = 1,

    /// <summary>暂停屏：游戏中按 Esc 弹出那一屏。</summary>
    Pause = 2,

    /// <summary>设置屏：从标题屏或暂停屏进的那一屏。</summary>
    Settings = 3,

    /// <summary>
    /// 结算屏：一局结束那一屏。<b>它是暂停屏的替身，不是叠在暂停屏上面的一层</b>，
    /// 所以开它走 <see cref="PushExclusive"/>（栈里只剩它），不走 <see cref="Push"/>。
    /// </summary>
    Result = 4,
}

/// <summary>
/// 菜单栈 —— 前端层的骨架，纯 C#，离线可测。
///
/// <para><b>为什么栈在纯 C# 这一侧</b>：菜单的进出规则是这一层唯一有分支的东西
/// （从暂停屏进设置再退回收不收回暂停屏、重复开同一屏算不算叠一层），
/// 这些规则静态就能判；把它们钉在一个不含 Godot 引用的类里，五条测试线直接跑，
/// 不用起引擎。Godot 那一侧只按本类的结论摆放控件，不重写规则。</para>
///
/// <para><b>栈是 LIFO</b>：开设置叠在暂停屏上面，退一层回暂停屏，上一屏的控件不重建
/// （重建就丢状态：滑条拧到哪一格、焦点停在哪一个按钮都得重来一遍）。
/// 这与 Unreal CommonUI 的 <c>ActivatableWidgetStack</c> 同一形状：
/// push 时下层停用不销毁，pop 时上���停用、下层重新激活。</para>
///
/// <para><b>本类不认识任何原型</b>：它只管「现在是哪一屏、上面盖着谁」，
/// 至于「暂停了没有」是原型的世界管的事（见 <see cref="FrontEndLayer"/> 的类注）。</para>
/// </summary>
public sealed class MenuStack
{
    private readonly List<FrontEndScreen> _screens = [];

    /// <summary>栈顶那一屏。栈空时是 <see cref="FrontEndScreen.None"/>。</summary>
    public FrontEndScreen Top => _screens.Count > 0 ? _screens[^1] : FrontEndScreen.None;

    /// <summary>栈深（当前开着几屏）。</summary>
    public int Depth => _screens.Count;

    /// <summary>栈里有没有某一屏（不管在哪一层）。</summary>
    public bool Contains(FrontEndScreen screen) => _screens.Contains(screen);

    /// <summary>
    /// 叠一层新屏。返回是否真的叠上去了。
    ///
    /// <para><b>重复开同一屏不叠</b>：连按两下 Esc 不该得到两层暂停屏。
    /// 返回 <c>false</c> 表示「已经在最上面了，什么都没做」——
    /// 静默返回 true 会让调用方以为多叠了一层，回退时行为就与预期对不上。</para>
    /// </summary>
    /// <param name="screen">要开的屏。</param>
    public bool Push(FrontEndScreen screen)
    {
        if (screen == FrontEndScreen.None) return false;
        if (Top == screen) return false;

        _screens.Add(screen);
        return true;
    }

    /// <summary>
    /// 开一屏并把别的全退掉 —— <b>栈里只剩它</b>（结算屏走这一条）。
    ///
    /// <para><b>为什么结算屏不能走 <see cref="Push"/></b>：结算屏是暂停屏的<b>替身</b>，
    /// 不是叠在它上面的一层。按 <see cref="Push"/> 叠上去的栈形是「暂停屏 + 结算屏」，
    /// 而结算屏之上没有一层能退 —— 玩家退一格回到暂停屏，看到的是一局已经打完的世界，
    /// 那屏上写着「继续」，点下去什么也不会发生。</para>
    ///
    /// <para><b>「不清空就直接压栈」也不对</b>：栈深会越积越多，退一格先退掉的是更早那层，
    /// 顺序与玩家按 Esc 的预期相反。</para>
    /// </summary>
    /// <param name="screen">要开的屏。</param>
    /// <returns><see cref="FrontEndScreen.None"/> 回 false；其余一律开成功（栈必变成只剩它）。</returns>
    public bool PushExclusive(FrontEndScreen screen)
    {
        if (screen == FrontEndScreen.None) return false;

        _screens.Clear();
        _screens.Add(screen);
        return true;
    }

    /// <summary>
    /// 退一层。返回退掉之后栈顶是哪一屏。
    ///
    /// <para><b>退空了就归 <see cref="FrontEndScreen.None"/></b>——
    /// 最后一层退掉不是错误，是「菜单全关了」这个正常结局。</para>
    /// </summary>
    public FrontEndScreen Pop()
    {
        if (_screens.Count == 0) return FrontEndScreen.None;
        _screens.RemoveAt(_screens.Count - 1);
        return Top;
    }

    /// <summary>全关。返回退了几层。</summary>
    public int Clear()
    {
        var n = _screens.Count;
        _screens.Clear();
        return n;
    }

    /// <summary>
    /// 只留最上面那一屏（从设置屏直接换到标题屏那种「换根」用）。
    ///
    /// <para><b>与 <see cref="Clear"/> 的分别</b>：清空是「全关」，换根是「下面那几层不要了，
    /// 但最上面这层留着」。退格退回标题屏要的是后者——它俩写混的结果是退出游戏。</para>
    /// </summary>
    public void KeepTopOnly()
    {
        if (_screens.Count > 1) _screens.RemoveRange(0, _screens.Count - 1);
    }

    /// <summary>
    /// 读数 —— 调试桥的 <c>ui_read</c> 靠它把栈念出来。
    ///
    /// <para><b>栈自底向上念</b>：读数按「先开了什么、后开了什么」排，
    /// 与人复盘的顺序一致；反过来念的话，「设置盖在暂停上」这件事要靠脑内翻转才看得出来。</para>
    /// </summary>
    public string Readout()
    {
        if (_screens.Count == 0) return "stack=<空> depth=0 top=None";

        var sb = new StringBuilder("stack=");
        for (int i = 0; i < _screens.Count; i++)
        {
            if (i > 0) sb.Append(" > ");
            sb.Append(_screens[i].ToString());
        }

        return sb.Append(" depth=")
            .Append(_screens.Count.ToString(CultureInfo.InvariantCulture))
            .Append(" top=")
            .Append(Top.ToString())
            .ToString();
    }
}
