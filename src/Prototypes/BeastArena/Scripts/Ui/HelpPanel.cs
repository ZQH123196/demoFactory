#nullable enable
using System;
using DemoFactory.GugaFramework.I18n;
using Godot;

namespace DemoFactory.Prototypes.BeastArena.Ui;

/// <summary>
/// 操作说明 —— 标题屏的「操作说明」与游戏内 F1 都开这一页。
///
/// <para>挂在 <see cref="UiRoot.PopupLayer"/> 上（层号 110）：从标题屏打开时它压在标题屏之上，
/// 关掉之后标题屏还在原处。开着的时候置模态，底下的世界不收拾取。</para>
/// </summary>
public partial class HelpPanel : Control, IModalCloseable
{
    private readonly UiRoot _ui;

    /// <summary>现在开着没有。</summary>
    public bool IsOpen => Visible;

    public HelpPanel(UiRoot ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        _ui = ui;

        Name = "HelpPanel";
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;

        // 居中：四边都锚在正中，偏移给出固定的宽高。
        AnchorLeft = 0.5f;
        AnchorRight = 0.5f;
        AnchorTop = 0.5f;
        AnchorBottom = 0.5f;
        OffsetLeft = -280f;
        OffsetRight = 280f;
        OffsetTop = -220f;
        OffsetBottom = 220f;

        var panel = new PanelContainer { Name = "Box" };
        panel.SetAnchorsPreset(LayoutPreset.FullRect);
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.12f, 0.13f, 0.17f, 0.98f),
            BorderColor = new Color(0.45f, 0.50f, 0.62f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            ContentMarginLeft = 22f,
            ContentMarginRight = 22f,
            ContentMarginTop = 18f,
            ContentMarginBottom = 18f,
        });
        AddChild(panel);

        var box = new VBoxContainer { Name = "Rows" };
        panel.AddChild(box);

        box.AddChild(new Label
        {
            Text = Loc.T("操作说明"),
            MouseFilter = MouseFilterEnum.Ignore,
        });

        box.AddChild(new Label
        {
            Name = "Lines",
            Text = Body(),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        });

        var close = new Button { Name = "Close", Text = Loc.T("关闭") };
        close.Pressed += () => SetOpen(false);
        box.AddChild(close);

        ui.AddPopup(this);
    }

    /// <summary>开合一次（F1 与标题屏那一条都走它）。</summary>
    public void Toggle() => SetOpen(!Visible);

    /// <summary>开到指定状态。</summary>
    public void SetOpen(bool open)
    {
        Visible = open;
        if (open) _ui.EnterModal(this);
        else _ui.ExitModal(this);
    }

    /// <summary>Esc 逐层关闭时的收口（与「关闭」那一颗同一件事）。</summary>
    public void Close() => SetOpen(false);

    /// <summary>说明正文 —— 一行一条，动词用术语表里的叫法，<b>按实现写</b>（点建筑不改选中，菜单走右键）。</summary>
    private static string Body() => string.Join("\n",
        Loc.T("左键点人：选中；点空地 / 甲板空格：取消选中。"),
        Loc.T("左键点建筑：不选中（建筑菜单走右键）。"),
        Loc.T("左键按住拖动：把人抓起来，拖到建筑上或甲板空地松手。"),
        Loc.T("右键点建筑：打开建筑菜单（招募、开赛、修理、拆除）。"),
        Loc.T("右键点地图空地：巨兽驶向该点。"),
        Loc.T("右键拖动：转视角；滚轮：推拉远近。"),
        Loc.T("W A S D 平移，Q E 绕转，↑ ↓ 俯仰，Home 归位。"),
        Loc.T("空格暂停，句点单步，[ ] 减速与加速。"),
        Loc.T("F1 开合本页，Esc 逐层关掉弹层，最后打开暂停菜单。"));
}
