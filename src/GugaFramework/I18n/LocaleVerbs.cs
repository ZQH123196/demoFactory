#nullable enable
using System;
using System.Text.Json;
using DemoFactory.GugaFramework.DebugBridge;
using Godot;

namespace DemoFactory.GugaFramework.I18n;

/// <summary>
/// 调试桥的 i18n 共享词表 —— <b>切语言与查表变可验动作</b>。
///
/// <para><b>为什么要有它</b>：翻译表存在不等于翻译能用。「键在表里」是静态事实，
/// 「切到目标语言后那行字真的变了」是运行时事实，只靠读代码永远证明不了。这一层把它变成
/// 一发可注入、可断言的动作 —— 即「行为可注入」在 i18n 上的那一格。切完再配一次
/// <c>debug screenshot</c>，出图复核就能直接读出目标语言那行长什么样（「落地配出图」
/// 要求出图看得见，这条链就是给它准备的）。</para>
///
/// <para><b>为什么只两发</b>：<c>set_locale</c> 切、<c>locale_probe</c> 读当前语言与
/// 若干键的取字结果。再多就是「翻译表编辑器」，那是开发期工位的事，不该占用桥的运行时
/// 词表（桥的词表越短越好读，「行为可注入」要的是可注入，不是功能全）。</para>
///
/// <para><b>数字参数缺了当场抛</b>：照「桥只转发」——数字参数缺了不兜 0。错参数当场回错，
/// 拿到一个假读数比拿不到读数更糟。</para>
///
/// <para><b>接法</b>：各原型在 <c>DebugConsole.Attach</c> 之后按行展开注册（与
/// <c>InputInjector.Vocabulary</c> 同一形状，理由见 <c>Main.cs</c> 那处注释 ——
/// <c>params</c> 的 <c>..</c> 展开只在集合初始化里合法，参数列表里不认）。</para>
/// </summary>
public static class LocaleVerbs
{
    /// <summary>
    /// 词表：<c>set_locale</c>（切语言）+ <c>locale_probe</c>（读当前语言、字体链、若干键的取字结果）。
    /// </summary>
    public static (string Verb, Func<JsonElement, object?> Handler)[] Vocabulary()
        =>
        [
            ("set_locale", SetLocale),
            ("locale_probe", a => LocaleProbe(a.Str("key"))),
        ];

    /// <summary>
    /// 切当前语言。
    ///
    /// <para><paramref name="tag"/> 是语言标签（<c>zh</c> / <c>en</c> / <c>ja</c> …），
    /// 或 <c>system</c> 取操作系统语言。切换会发 Godot 的翻译变更通知，
    /// <c>Control</c> 节点自动重取字（<c>auto_translate_mode</c> 默认开）；
    /// <c>Label3D</c> 没有这层自动化，<c>SkillCues</c> 那一侧靠每帧重设文本，
    /// 见各原型那处注释。</para>
    ///
    /// <para><b>为什么当月抛而不是回读数</b>：切语言不改变任何游戏状态，失败只可能是
    /// 参数错，那属于调用方的错，当场打回去比安静记一笔好。</para>
    /// </summary>
    private static object? SetLocale(JsonElement args)
    {
        var tag = args.Str("locale", required: true)
                   ?? throw new ArgumentException("set_locale 要 locale=<语言标签>（zh / en / ja …）或 system");

        if (tag == "system")
        {
            // OS.GetLocaleLanguage 在 C# 侧是 OS.Singleton.GetLocaleLanguage（实例方法，不是静态）。
            tag = OS.Singleton.GetLocaleLanguage();
        }

        TranslationServer.SetLocale(tag);
        return $"locale={TranslationServer.GetLocale()}（请求 {tag}）";
    }

    /// <summary>
    /// 读口：当前语言、字体链、给定键的取字结果。
    ///
    /// <para><b>为什么带 <c>key</c> 参数</b>：不传就只回语言与字体链；传了就多回那几行
    /// 「这个键在当前语言下渲染成什么」。后者是判定「译文到底填没填对」的唯一读数 ——
    /// 表里填了不等于引擎读到了，也不等于读到的对。</para>
    ///
    /// <para><b>缺键时怎么表现</b>：<c>Loc.T</c> 查不到就返回键本身，所以这里回出来的是
    /// 中文原文。它<b>不是</b>故障，是「这一格没译文」的可读证据 —— 缺键由判据测试在离线侧
    /// 判红，运行时这一发只负责把实际渲染值念出来。</para>
    /// </summary>
    private static string LocaleProbe(string? key)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("locale=").Append(TranslationServer.GetLocale());
        sb.Append(" | ").Append(FontSetup.FontChainReadout());
        var loaded = TranslationServer.GetLoadedLocales();
        sb.Append(" | 已注册译文语言=")
            .Append(loaded.Length > 0 ? string.Join(",", loaded) : "（无）");
        if (!string.IsNullOrEmpty(key))
        {
            sb.Append(" | 取字[").Append(key).Append("]=「").Append(Loc.T(key)).Append("」");
        }

        return sb.ToString();
    }
}
