#nullable enable
using Godot;

namespace DemoFactory.GugaFramework.I18n;

/// <summary>
/// 项目字体 —— 让**上屏中文真的有字形**。
///
/// <para><b>为什么需要它</b>：Godot 缺省字体不含汉字字形，中文上屏实测是**整行空白**
/// （2026-10-01 记在 <c>Abilities/Stage/SkillStageScene.cs</c> 类注）。本层把 HUD 改回中文
/// 上屏之后，这一格就是「读得出代码、看不见字」的典型 —— 画面上那行是空的，agent 只能
/// 读代码猜它渲染成什么。「落地配出图」要求出图看得见，这一格不接就等于没接。</para>
///
/// <para><b>为什么用 SystemFont 而不是塞字体文件</b>：<c>SystemFont</c> 按名字问操作系统要
/// 字体，零资产、零仓库体积、干净机器上照样有。代价是<b>跨平台渲染结果不保证一致</b> ——
/// Windows 上问 <c>Microsoft YaHei</c>，macOS 上没有这个名字，落到 fallback 链的下一档。
/// 这个代价的处置见「字体覆盖」那份口径：机制原型阶段只要「看得见汉字」，像素级一致要等
/// 垂直切片（那时的做法是塞一份随包字体，代价是仓库多 10~20 MB）。
/// 现在就把这个取舍写在明处，不是留到出问题时才发现。</para>
///
/// <para><b>fallback 链为什么这么排</b>：中文名 → 英文名 → 泛 CJK。中文名在中文 Windows 上
/// 命中最快；英文名（YaHei）在英文 Windows 上命中；Noto Sans SC 两条都覆盖不到时兜底
/// （Linux 与容器里常见）。链尾还有 Godot 缺省字体，缺字体时仍渲染拉丁字母而不是全白。</para>
///
/// <para><b>装在哪</b>：装成项目默认主题（<c>ProjectSettings</c> 的 <c>gui/theme/custom</c>）
/// 指向一份 .tres，一处生效、各原型共用。挨个 Label 调 <c>AddThemeFontOverride</c> 会漏，
/// 而且新增控件默认就又是缺省字体 —— 那种做法要靠人记得每次都调，正是「可调试性优先」要挡的。</para>
/// </summary>
public static class FontSetup
{
    /// <summary>项目主题资源：把缺省字体换成带 fallback 链的 SystemFont。</summary>
    public const string ThemeResourcePath = "res://src/Shared/ui/ProjectTheme.tres";

    /// <summary>
    /// 装一次：把项目默认主题指向 <see cref="ThemeResourcePath"/>。
    ///
    /// <para><b>幂等</b>：重复调用无副作用，值相同就跳过 —— 四个原型各调一次是正常的。</para>
    ///
    /// <para><b>为什么不在 <c>_Ready</c> 里直接改主题资源</b>：那要求主题已加载才能改，
    /// 而主题是懒加载的；写 ProjectSettings 再由引擎在启动时读，是唯一不会与加载顺序打架的
    /// 做法。各原型的接法与实测读数见 <c>docs/GugaFramework/i18n.md</c>。</para>
    /// </summary>
    public static void Install()
    {
        if (ProjectSettings.GetSetting("gui/theme/custom").AsString() == ThemeResourcePath)
        {
            return;
        }

        ProjectSettings.SetSetting("gui/theme/custom", ThemeResourcePath);
        // 关键：ProjectSettings 那一改，要显式让主题库重载才生效。
        // 少了这一句，实测是「设置写进去了、读口能念出路径，但上屏仍是缺省字体」——
        // 主题库在启动早期就把 default_theme 读完了，运行期改这个设置它不再回头看。
        // 记在这里是因为它没有报错：不加这一句的失败形态是「上屏空白」，
        // 而「上屏空白」的第一反应总是去查字体文件在不在（查不到问题，因为文件在）。
        ThemeDB.Singleton.FallbackFont = ResourceLoader
            .Load<Theme>(ThemeResourcePath)?.DefaultFont;
    }

    /// <summary>
    /// 当前项目主题的默认字体链（读出来给调试桥回报，不改任何状态）。
    ///
    /// <para><b>为什么要有这个读口</b>：「上屏是空白」这类故障的判据是<b>出图</b>，但
    /// 「字体链装上没有、链上有哪些名字」是<b>状态</b>。合成输入把光标挪过去之后画面变了，
    /// 变的那行到底用的哪个字体只有这里读得到 —— 与悬停拾取那条读出口同一理由
    /// （见各原型 <c>Main.cs</c> 的「3D 拾取读出口」类注）。</para>
    /// </summary>
    public static string FontChainReadout()
    {
        // 读 ThemeDB 的 FallbackFont 只能读到「主题库当前那一份」，
        // 读不到主题资源里 default_font 的 SystemFont 名字（实测：Fallbacks 链长=0，
        // 因为 SystemFont 的名字在 font_names 上，fallbacks 数组是另一回事）。
        // 名字那一半直接读主题资源 —— 判「装上没有、装的是哪条链」要看的就是它。
        var names = "（主题没装上）";
        if (ResourceLoader.Exists(ThemeResourcePath))
        {
            var theme = ResourceLoader.Load<Theme>(ThemeResourcePath);
            names = theme?.DefaultFont is SystemFont sf
                ? $"[{string.Join(" | ", sf.FontNames)}] allow_system_fallback={sf.AllowSystemFallback}"
                : $"（主题里的 default_font 不是 SystemFont：{theme?.DefaultFont?.GetType().Name ?? "null"}）";
        }

        var live = ThemeDB.Singleton.FallbackFont;
        return $"主题={ThemeResourcePath} default_font={names} | 运行期 FallbackFont={live?.GetType().Name ?? "null"}";
    }
}
