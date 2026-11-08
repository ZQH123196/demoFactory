#nullable enable
using System;
using System.Globalization;
using DemoFactory.GugaFramework.Persistence;

namespace DemoFactory.GugaFramework.FrontEnd;

/// <summary>
/// 窗口模式 —— 三档，与 Godot 的 <c>WindowMode</c> 逐档对应。
///
/// <para>档名是数据不是判断：<see cref="FrontEndSettings"/> 只按序号存取，
/// 显示名走 <c>Loc.T</c>（那一半在表现层，本类不碰 Godot）。</para>
/// </summary>
public enum FrontEndWindowMode
{
    /// <summary>窗口化，可拖动可缩放。</summary>
    Windowed = 0,

    /// <summary>无边框窗口，占满一块屏幕，不随分辨率缩放画面。</summary>
    Borderless = 1,

    /// <summary>独占全屏，分辨率跟着设置走。</summary>
    Fullscreen = 2,
}

/// <summary>
/// 分辨率 —— 两个整数。
///
/// <para><b>为什么不用 <c>Vector2I</c></b>：那是 Godot 的类型，本类要能被五条测试线链进去
/// （与 <c>SpeedControl</c> 同一档：不引引擎）。两个 <c>int</c> 换来一个能离线断言的值类型。</para>
/// </summary>
public readonly record struct FrontEndResolution(int Width, int Height)
{
    /// <summary>两个边长都大于零才算合法（零与负数在 <c>DisplayServer</c> 那一层的失败方式是静默不动）。</summary>
    public bool IsValid => Width > 0 && Height > 0;

    /// <inheritdoc />
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"{Width}×{Height}");
}

/// <summary>
/// 设置项的值 —— 语言、主音量、窗口模式、分辨率。
///
/// <para><b>这是纯 C#，不引 Godot</b>：取值、钳位、默认值、读数全在这里，离线可测；
/// 把值<b>落到引擎上</b>（<c>TranslationServer.SetLocale</c>、<c>AudioServer</c>、
/// <c>DisplayServer</c>）是表现层那一半的活，在 <c>FrontEndLayer</c>。
/// 两半分开的理由与 <c>SkillStage</c> / <c>SkillStageScene</c> 同一笔账：
/// 能离线判的判在离线，出引擎才看得见的排版与光标问题才排到出图。</para>
///
/// <para><b>越界一律抛，不静默钳</b>（照 <c>SpeedControl.SetMultiplier</c> 那一档的理由：挡笔误）。
/// 滑条自己会给出 0~1 之间的值，抛出来的那一次只可能是接错了线——</para>
/// <para><b>本类不认识任何原型</b>：四项都是引擎层的通用设置，与哪个原型无关。</para>
///
/// <para><b>四项进存档</b>（玩家按了 Esc 改了分辨率就关机，那是他要带回来的那一档）：
/// 存档只管这四个值，<b>控件不会跟着走</b> —— <see cref="FrontEndLayer.RefillFromSettings"/>
/// 读档那一发要自己往控件上写，见那一格的注释。</para>
/// </summary>
public sealed class FrontEndSettings : DemoFactory.GugaFramework.Persistence.ISaveable
{
    /// <summary>语言下拉里给的标签，顺序即显示顺序。取 <c>system</c> 表示跟操作系统走。</summary>
    public static readonly string[] LocaleTags = ["zh", "en", "ja"];

    /// <summary>分辨率下拉里给的档位。都是 16:9，省得同一份表里混进比例不同的行。</summary>
    public static readonly FrontEndResolution[] Resolutions =
    [
        new(1280, 720),
        new(1600, 900),
        new(1920, 1080),
        new(2560, 1440),
    ];

    /// <summary>主音量下界。</summary>
    public const double MinMasterVolume = 0.0;

    /// <summary>主音量上界。</summary>
    public const double MaxMasterVolume = 1.0;

    private string _locale = "zh";
    private double _masterVolume = 0.8;
    private FrontEndWindowMode _windowMode = FrontEndWindowMode.Windowed;
    private FrontEndResolution _resolution = new(1920, 1080);

    /// <summary>当前语言标签（<c>zh</c> / <c>en</c> / <c>ja</c>，或 <c>system</c>）。</summary>
    public string Locale => _locale;

    /// <summary>主音量，0（静音）到 1（满）。</summary>
    public double MasterVolume => _masterVolume;

    /// <summary>当前窗口模式。</summary>
    public FrontEndWindowMode WindowMode => _windowMode;

    /// <summary>当前分辨率。</summary>
    public FrontEndResolution Resolution => _resolution;

    /// <summary>恢复出厂值。四个字段一起回到初值，不留半改状态。</summary>
    public void Reset()
    {
        _locale = "zh";
        _masterVolume = 0.8;
        _windowMode = FrontEndWindowMode.Windowed;
        _resolution = new FrontEndResolution(1920, 1080);
    }

    /// <summary>
    /// 设语言。<paramref name="tag"/> 是语言标签，或 <c>system</c>（跟操作系统走）。
    ///
    /// <para><b>只查格式不查有没有译文</b>：这一格能不能翻由翻译表决定，不该在这里拦。
    /// 拦了就变成「填了译文却因为拼错标签被丢掉」，那比翻不出来更难查。</para>
    /// </summary>
    public void SetLocale(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            throw new ArgumentException("语言标签不能为空（要 zh / en / ja 之一，或 system 跟操作系统走）", nameof(tag));

        _locale = tag.Trim();
    }

    /// <summary>设主音量。<paramref name="volume"/> 必须在 0~1，越界当场抛。</summary>
    public void SetMasterVolume(double volume)
    {
        if (double.IsNaN(volume) || volume < MinMasterVolume || volume > MaxMasterVolume)
            throw new ArgumentOutOfRangeException(nameof(volume),
                $"主音量必须在 {MinMasterVolume}~{MaxMasterVolume} 之间，收到 {volume}");

        _masterVolume = volume;
    }

    /// <summary>设窗口模式。传入未定义的枚举值当场抛——那只能来自接错的线。</summary>
    public void SetWindowMode(FrontEndWindowMode mode)
    {
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode), $"未定义的窗口模式 {(int)mode}");

        _windowMode = mode;
    }

    /// <summary>
    /// 设分辨率。
    ///
    /// <para><b>不校验它在不在 <see cref="Resolutions"/> 里</b>：那张表是下拉里给的档位，
    /// 不是白名单。挡掉不在表里的值等于给「自定义分辨率」这条路提前判死，
    /// 而那条路本来就不该由本类决定。</para>
    /// </summary>
    public void SetResolution(FrontEndResolution resolution)
    {
        if (!resolution.IsValid)
            throw new ArgumentOutOfRangeException(nameof(resolution),
                $"分辨率两个边长都要大于零，收到 {resolution}");

        _resolution = resolution;
    }

    /// <summary>
    /// 读数 —— 调试桥的 <c>ui_read</c> 靠它把四个值一次性念出来。
    ///
    /// <para><b>用不变文化</b>（「double 与不变文化」）：小数点必须是点，
    /// 否则在改了区域设置的机器上读数变成逗号，逐字比对就假红了。</para>
    /// </summary>
    public string Readout()
        => string.Create(CultureInfo.InvariantCulture,
            $"locale={_locale} master={_masterVolume:F2} mode={_windowMode} resolution={_resolution}");

    /// <inheritdoc />
    /// <remarks>版本号不动就是「格式没变」。改字段时才 +1，并往存档模块的迁移表补一条。</remarks>
    public int SchemaVersion => 1;

    /// <inheritdoc />
    public void WriteState(SaveWriter w)
    {
        ArgumentNullException.ThrowIfNull(w);
        w.Str("locale", _locale);
        w.Num("master", _masterVolume);
        w.Str("windowMode", _windowMode.ToString());
        w.Int("width", _resolution.Width);
        w.Int("height", _resolution.Height);
    }

    /// <inheritdoc />
    public void ReadState(SaveReader r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var locale = r.Str("locale");
        var master = r.Num("master");
        var mode = r.Str("windowMode");
        var width = r.Int("width");
        var height = r.Int("height");

        // 走那四个 setter 而不是直接写字段：越界与未定义值当场抛，
        // 与玩家点控件那条路径同一套判据（档被手改过就在这里说清，不读成一个半合法的世界）。
        SetLocale(locale);
        SetMasterVolume(master);
        SetWindowMode(Enum.TryParse<FrontEndWindowMode>(mode, out var parsed)
            ? parsed
            : throw new FormatException($"windowMode：读到 {mode}，那不是一个窗口模式"));
        SetResolution(new FrontEndResolution(width, height));
    }
}
