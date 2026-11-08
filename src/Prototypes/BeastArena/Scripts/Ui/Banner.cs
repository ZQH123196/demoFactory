#nullable enable
using System;
using System.Globalization;
using DemoFactory.GugaFramework.I18n;
using DemoFactory.Prototypes.BeastArena.Logic;
using Godot;

namespace DemoFactory.Prototypes.BeastArena.Ui;

/// <summary>
/// 居中横幅 —— 操作成功给一句中性提示，失败给一句红色提示并写明原因。
///
/// <para><b>两秒淡出</b>：整条停留两秒，最后 <see cref="FadeSeconds"/> 秒把透明度收到 0。
/// 淡出用 <c>Modulate.A</c>，所以底色与文字一起退，不会留下一条空框。</para>
///
/// <para><b>失败必须写原因，而且不显示枚举原文</b>：<see cref="RejectReason"/> 是十三项封闭枚举，
/// 玩家读不懂 <c>FullCapacity</c> 这个词。十三项各译一句短中文，<b>逐项 switch</b> ——
/// 不给缺省兜底把枚举名漏上屏。原因串从注入口回来时带着 <c>rejected:</c> 前缀与括号里的细节，
/// 这里剥掉前缀、翻成短中文，<b>括号里那一段（原因与缺口数字）原样接在后面</b>。</para>
/// </summary>
public partial class Banner : Control
{
    /// <summary>整条停留时长（秒）。</summary>
    public const double DurationSeconds = 2.0;

    /// <summary>最后这一段做淡出（秒）。</summary>
    public const double FadeSeconds = 0.6;

    private readonly PanelContainer _panel;
    private readonly Label _label;
    private double _remaining;

    public Banner(UiRoot ui)
    {
        ArgumentNullException.ThrowIfNull(ui);

        Name = "Banner";
        MouseFilter = MouseFilterEnum.Ignore;

        // 居中：水平锚在正中，竖直锚在底边上；偏移把整条抬到蓝图条之上。
        AnchorLeft = 0.5f;
        AnchorRight = 0.5f;
        AnchorTop = 1f;
        AnchorBottom = 1f;
        OffsetLeft = -UiRoot.BannerWidth / 2f;
        OffsetRight = UiRoot.BannerWidth / 2f;
        OffsetTop = -UiRoot.BannerBottomOffset - UiRoot.BannerHeight;
        OffsetBottom = -UiRoot.BannerBottomOffset;

        _panel = new PanelContainer { Name = "Box", MouseFilter = MouseFilterEnum.Ignore };
        _panel.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_panel);

        _label = new Label
        {
            Name = "Text",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _panel.AddChild(_label);

        Visible = false;
        ui.AddPanel(this);
    }

    /// <summary>给一句中性提示（成功、中性状态）。</summary>
    public void Show(string message) => Show(message, success: true);

    /// <summary>给一句提示；<paramref name="success"/> 为假时用红色底与红字。</summary>
    public void Show(string message, bool success)
    {
        _label.Text = message;
        _label.AddThemeColorOverride("font_color",
            success ? new Color(0.94f, 0.95f, 0.97f) : new Color(1f, 0.72f, 0.70f));

        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = success ? new Color(0.16f, 0.18f, 0.22f, 0.92f) : new Color(0.30f, 0.10f, 0.10f, 0.94f),
            BorderColor = success ? new Color(0.45f, 0.50f, 0.60f) : new Color(0.85f, 0.30f, 0.28f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
            ContentMarginLeft = 14f,
            ContentMarginRight = 14f,
            ContentMarginTop = 6f,
            ContentMarginBottom = 6f,
        });

        // 重置整条的不透明度：上一条可能正在淡出，淡到 0.2 时再来一条会接着那条的透明度。
        Modulate = Colors.White;
        _remaining = DurationSeconds;
        Visible = true;
    }

    /// <summary>立刻收起（换局复位用；不等淡出）。</summary>
    public void Clear()
    {
        Visible = false;
        _remaining = 0.0;
        Modulate = Colors.White;
    }

    /// <summary>
    /// 失败横幅：原因给注入口回传的那一串（带前缀与括号里的细节）。
    ///
    /// <para><b>细节与数字都上屏</b>：枚举名那一段翻成短中文，括号里那一段（原因与缺口数字）
    /// 原样接在后面 —— 玩家要看到的是「差几个」「差多少」，不是枚举名。
    /// 拆串那一步在 <see cref="ArenaSetup.SplitRejectText"/>（格式是逻辑层那一处定的）。
    /// 认不出枚举就给一句不暴露枚举原文的话，细节照样接上。</para>
    /// </summary>
    public void ShowReject(string reason)
    {
        var (parsed, detail) = ArenaSetup.SplitRejectText(reason);

        if (parsed is { } value)
        {
            ShowReject(value, detail);
            return;
        }

        Show(Loc.T("这一下没做成") + Suffix(detail), success: false);
    }

    /// <summary>失败横幅：原因给枚举，译文走 <see cref="TextOf"/>（没有细节就只上短句）。</summary>
    public void ShowReject(RejectReason reason) => ShowReject(reason, string.Empty);

    /// <summary>失败横幅：原因给枚举，译文走 <see cref="TextOf"/>；细节（括号里那一段）接在后面。</summary>
    public void ShowReject(RejectReason reason, string detail)
        => Show(Loc.T("做不到：") + TextOf(reason) + Suffix(detail), success: false);

    /// <summary>细节那一段（空串就是什么都没有）。</summary>
    private static string Suffix(string detail)
        => detail.Length == 0 ? string.Empty : $"（{detail}）";

    /// <summary>
    /// 十三项拒绝原因各译一句短中文（逐项 switch，不留缺省把枚举名漏上屏）。
    /// 末尾那一档只兜「枚举被塞进未定义值」—— 给的仍是一句中文，不是枚举原文。
    /// </summary>
    public static string TextOf(RejectReason reason) => reason switch
    {
        RejectReason.InvalidArgs => Loc.T("参数不对"),
        RejectReason.InvalidPhase => Loc.T("当前阶段不允许"),
        RejectReason.InsufficientCoin => Loc.T("金币不够"),
        RejectReason.InsufficientFood => Loc.T("食物不够"),
        RejectReason.InsufficientSoulStone => Loc.T("魂石不够"),
        RejectReason.InsufficientMana => Loc.T("法力格不够"),
        RejectReason.InsufficientMaterial => Loc.T("矿石或特殊材料不够"),
        RejectReason.FullCapacity => Loc.T("岗位或额度已满"),
        RejectReason.InvalidTarget => Loc.T("这个目标不可用"),
        RejectReason.WantedRefused => Loc.T("正被通缉，这座城拒停"),
        RejectReason.GateNotMet => Loc.T("进阶条件未满足"),
        RejectReason.TierAtMax => Loc.T("已到三阶"),
        RejectReason.FormLocked => Loc.T("这个形态还没解锁"),
        RejectReason.NoIdleWorker => Loc.T("没有无岗的工人或帮工"),
        _ => Loc.T("这一下没做成"),
    };

    public override void _Process(double delta)
    {
        if (!Visible) return;

        _remaining -= delta;
        var alpha = _remaining >= FadeSeconds
            ? 1f
            : (float)Math.Clamp(_remaining / FadeSeconds, 0.0, 1.0);
        Modulate = new Color(1f, 1f, 1f, alpha);

        if (_remaining > 0) return;
        Visible = false;
        Modulate = Colors.White;
    }

    /// <summary>读数（调试桥用）：现在有没有一条、剩多久、上面写着什么。</summary>
    public string DebugReadout()
        => $"visible={Visible.ToString().ToLowerInvariant()} " +
           $"left={_remaining.ToString("0.00", CultureInfo.InvariantCulture)}s " +
           $"text={_label.Text}";
}
