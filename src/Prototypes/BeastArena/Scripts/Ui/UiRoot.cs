#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using DemoFactory.Prototypes.BeastArena.Logic;
using Godot;

namespace DemoFactory.Prototypes.BeastArena.Ui;

/// <summary>
/// 表现层那棵 <see cref="Control"/> 树 —— 三层的父节点、弹层容器与模态标记。
///
/// <para><b>锚点一律给 <see cref="Control"/>，不给 <see cref="CanvasLayer"/></b>（「落地配出图」）：
/// <c>CanvasLayer</c> 不是 <c>Control</c>，锚点预设落在它身上等于没设。
/// 所以每一层都是一块铺满视口的 <c>Control</c>，子控件的锚点相对它算。</para>
///
/// <para><b>三层各管一摊，后续 ticket只往里挂、不再改这个文件</b>：
/// <list type="bullet">
/// <item><see cref="WorldLayer"/>：HUD 那一层（左上五组 HUD、右上命令栏）。</item>
/// <item><see cref="PanelLayer"/>：常驻面板的父节点 —— 左下选中面板、右侧土壤、底部蓝图条、居中横幅。</item>
/// <item><see cref="PopupLayer"/>：模态弹层的父节点 —— 右键菜单、招募框、战报、操作说明。
/// 它挂在自己那层 <c>CanvasLayer</c> 上、层号 110，高于前端层 100（标题屏与暂停屏盖不住弹层）。</item>
/// </list></para>
///
/// <para><b>模态持有者</b>：弹层打开时由弹出它的那一件调 <see cref="EnterModal"/>、关上时调
/// <see cref="ExitModal"/>。<b>按持有者记账而不是一个全局 bool</b>：两个弹层同时开着时，
/// 先关掉的那一个不能把另一个的模态一起清掉。
/// <c>InputRouter</c> 读 <see cref="IsModal"/> 决定世界还要不要接收拾取。</para>
///
/// <para><b>锚点常量集中在这里</b>：8 格土壤位与 13 个蓝图槽位的宽高、间距、边距只写一份，
/// 后续 ticket 按 <see cref="BlueprintSlotRect"/> 取值，不各算一遍。
/// 各面板自己的锚点仍由各面板设置（它们知道自己要多大）。</para>
/// </summary>
public partial class UiRoot : Node
{
    // === HUD 与右栏（屏幕布局那张图上的两列） ===

    /// <summary>HUD 五行那一块的固定宽度（像素）。原先 760，spec 定为 560 并开自动折行。</summary>
    public const float HudWidth = 560f;

    /// <summary>HUD 左内边距（像素）。</summary>
    public const float HudLeftMargin = 16f;

    /// <summary>HUD 上内边距（像素）。</summary>
    public const float HudTopMargin = 12f;

    /// <summary>
    /// 右上命令栏的最小宽度（像素）—— 与 HUD 560 并排在 1152 宽下不重叠。
    /// 八颗按钮那一行（锁巨兽 / 停靠 / 拔锚 / 吞食 / 召回 / 经营 / 结算 / ▶继续）最小宽 480：
    /// 320 装不下就按最小尺寸往外顶 —— 顶出屏幕右边那一颗就点不到了。
    /// </summary>
    public const float CommandBarMinWidth = 480f;

    /// <summary>屏幕四周的统一边距（像素）。</summary>
    public const float ScreenMargin = 16f;

    // === 右侧土壤面板：8 格，按 4 个扇区 × 2 层分组 ===

    /// <summary>土壤格数（4 扇区 × 2 层）—— 跟着逻辑层那本册走，不另写一个 8。</summary>
    public const int SoilCellCount = UndergroundSoil.CellCount;

    /// <summary>一个格位的宽度（像素）。</summary>
    public const float SoilCellWidth = 104f;

    /// <summary>一个格位的高度（像素）。</summary>
    public const float SoilCellHeight = 26f;

    /// <summary>相邻格位的竖向间距（像素）。</summary>
    public const float SoilCellGap = 3f;

    /// <summary>土壤面板距屏幕右缘的距离（像素）。</summary>
    public const float SoilPanelRightMargin = ScreenMargin;

    /// <summary>土壤面板距屏幕下缘的留空（像素）—— 让出底部蓝图条。</summary>
    public const float SoilPanelBottomOffset = 104f;

    /// <summary>土壤面板与命令栏之间的竖向间距（像素）。</summary>
    public const float SoilPanelTopGap = 8f;

    /// <summary>
    /// 土壤面板上缘（像素）—— 贴在右上命令栏下缘，同一条右栏。
    /// 命令栏高度那一档按它自己的常量（<see cref="CommandBar.ButtonHeight"/> 与内边距）算，不另写一份高度。
    /// </summary>
    public const float SoilPanelTopOffset =
        HudTopMargin + CommandBar.ButtonHeight + (CommandBar.PanelPadding * 2f) + SoilPanelTopGap;

    // === 底部蓝图条：13 个固定槽位 ===

    /// <summary>蓝图槽位数（与十三件建筑表同长）。</summary>
    public static int BlueprintSlotCount => DeckBlueprints.Specs.Count;

    /// <summary>一个蓝图槽位的宽度（像素）。</summary>
    public const float BlueprintSlotWidth = 76f;

    /// <summary>一个蓝图槽位的高度（像素）。</summary>
    public const float BlueprintSlotHeight = 72f;

    /// <summary>相邻蓝图槽位的间距（像素）。</summary>
    public const float BlueprintSlotGap = 6f;

    /// <summary>蓝图条距屏幕左右缘的距离（像素）。</summary>
    public const float BlueprintBarSideMargin = ScreenMargin;

    /// <summary>蓝图条距屏幕下缘的距离（像素）。</summary>
    public const float BlueprintBarBottomMargin = 12f;

    // === 左下选中面板与居中横幅（后续 ticket挂上去时读这一组） ===

    /// <summary>选中面板距屏幕左缘的距离（像素）。</summary>
    public const float SelectionPanelLeftMargin = ScreenMargin;

    /// <summary>选中面板的宽度（像素）。</summary>
    public const float SelectionPanelWidth = 280f;

    /// <summary>
    /// 选中面板的高度（像素）。
    /// 五行（标题 / 三行读数 / 钉住那一行 / 三颗按钮那行）加内边距约 150，
    /// 128 是四行那一版的宽 —— 加了钉住那一行就放不下了，放不下时最后一行被挤出面板。
    /// </summary>
    public const float SelectionPanelHeight = 168f;

    /// <summary>选中面板距屏幕下缘的留空（像素）。</summary>
    public const float SelectionPanelBottomOffset = 104f;

    /// <summary>居中横幅的宽度（像素）。</summary>
    public const float BannerWidth = 640f;

    /// <summary>居中横幅的高度（像素）。</summary>
    public const float BannerHeight = 44f;

    /// <summary>居中横幅距屏幕下缘的留空（像素）。</summary>
    public const float BannerBottomOffset = 180f;

    /// <summary>HUD 那一层（左上五组 HUD、右上命令栏）。</summary>
    public Control WorldLayer { get; }

    /// <summary>常驻面板那一层（左下选中面板、右侧土壤、底部蓝图条、居中横幅）。</summary>
    public Control PanelLayer { get; }

    /// <summary>弹层那一层（右键菜单、招募框、战报、操作说明）。</summary>
    public Control PopupLayer { get; }

    /// <summary>
    /// 当前压着世界的那些弹层（<b>持有者集合</b>，不是全局 bool）。
    /// 关掉操作说明不能把同时开着的招募框 / 战报的模态一起清掉。
    /// </summary>
    private readonly HashSet<object> _modalOwners = [];

    /// <summary>弹层的打开次序（后开的在上）—— Esc 逐层关闭按这个次序从栈顶收。</summary>
    private readonly List<object> _modalOrder = [];

    /// <summary>有没有任一个弹层压着世界（<c>InputRouter</c> 据此决定要不要收拾取）。</summary>
    public bool IsModal => _modalOwners.Count > 0;

    public UiRoot(CanvasLayer uiLayer)
    {
        ArgumentNullException.ThrowIfNull(uiLayer);

        Name = "UiRoot";
        uiLayer.AddChild(this);

        WorldLayer = MakeLayer("WorldLayer", this);
        PanelLayer = MakeLayer("PanelLayer", this);

        // 弹层单独一层画布：110 高于前端层 100，所以标题屏与暂停屏盖不住它
        // （spec：右键菜单、招募框、战报、操作说明都挂这一层）。
        var popupCanvas = new CanvasLayer { Name = "PopupCanvas", Layer = 110 };
        AddChild(popupCanvas);
        PopupLayer = MakeLayer("PopupLayer", popupCanvas);
    }

    /// <summary>某个弹层开始压住世界（幂等：同一个持有者重复调用不叠层）。</summary>
    public void EnterModal(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (_modalOwners.Add(owner)) _modalOrder.Add(owner);
    }

    /// <summary>某个弹层不再压住世界（只摘掉自己那一份，不动别人的）。</summary>
    public void ExitModal(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _modalOwners.Remove(owner);
        _modalOrder.Remove(owner);
    }

    /// <summary>
    /// Esc 的第一层：把最上面那个弹层关掉。返回有没有收掉一层。
    ///
    /// <para><b>只认 <see cref="IModalCloseable"/></b>：弹层自己知道怎么关（收哪一块、摘哪份模态）。
    /// 没实现接口的持有者按「兜底摘模态」处理 —— 不把它留在栈上让 Esc 永远关不掉。</para>
    ///
    /// <para>顺序是<b>后开的先关</b>：战报压着巨兽面板时，Esc 先收战报。</para>
    /// </summary>
    public bool TryCloseTopModal()
    {
        if (_modalOrder.Count == 0) return false;

        var top = _modalOrder[^1];
        if (top is IModalCloseable closable)
        {
            closable.Close();
            return true;
        }

        ExitModal(top);
        return true;
    }

    /// <summary>把一件常驻面板挂到 <see cref="PanelLayer"/>。重复挂同一件不报错。</summary>
    public void AddPanel(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (control.GetParent() is null) PanelLayer.AddChild(control);
    }

    /// <summary>把一件弹层挂到 <see cref="PopupLayer"/>。重复挂同一件不报错。</summary>
    public void AddPopup(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (control.GetParent() is null) PopupLayer.AddChild(control);
    }

    /// <summary>
    /// 注册一层额外的控件源（<c>ui_rects</c> 也会遍历它）。
    ///
    /// <para>用得上的是**不在三层之内**的那些层：前端层（标题屏 / 暂停屏 / 设置 / 结算）
    /// 挂在 <c>UILayer</c> 之外、自成一张 <c>CanvasLayer</c>，它的按钮不在
    /// <see cref="WorldLayer"/> / <see cref="PanelLayer"/> / <see cref="PopupLayer"/> 里。
    /// 不注册它，「开始游戏」那一颗就取不到矩形 —— 而真机验收第一下点的正是它。</para>
    /// </summary>
    public void RegisterLayer(string name, Node root)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(root);
        _extraLayers.Add((name, root));
    }

    /// <summary>
    /// 把三层 UI 上每一件控件摊平成一行行文字，供调试桥的 <c>ui_rects</c> 读。
    ///
    /// <para><b>为什么要有这一口</b>：真机验收要点按钮，而「按钮在哪」只有 UI 树知道 ——
    /// 文字在画面上看着的位置与 Button 的真实矩形有偏移（内边距、最小宽、自动折行都会让两者分开），
    /// 拿截图目测坐标去点，点空是常态。所以坐标只从这一口取，不从画面上估。</para>
    ///
    /// <para>每行：<c>层级 名字 全局矩形 可见 禁用 文本</c>。全局矩形已经折算到视口坐标，
    /// 与 <c>input_click</c> 的 x/y 同一坐标系，取行中心点直接点即可。
    /// 不可见或禁用的控件也照列 —— 验证「该置灰的置灰了」就靠这两列。</para>
    /// </summary>
    public string DescribeControls()
    {
        var lines = new List<string> { $"viewport={GetViewport().GetVisibleRect().Size}" };
        Describe(WORLD, WorldLayer, 0, lines);
        Describe(PANEL, PanelLayer, 0, lines);
        Describe(POPUP, PopupLayer, 0, lines);
        foreach (var (name, root) in _extraLayers) Describe(name, root, 0, lines);
        return string.Join("\n", lines);
    }

    private readonly List<(string Name, Node Root)> _extraLayers = new();

    private const string WORLD = "world";
    private const string PANEL = "panel";
    private const string POPUP = "popup";

    private static void Describe(string layer, Node node, int depth, List<string> lines)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is not Control control)
            {
                Describe(layer, child, depth, lines);
                continue;
            }

            var rect = control.GetGlobalRect();
            var text = control switch
            {
                Label label => label.Text,
                Button button => button.Text,
                _ => "",
            };
            // 禁用态只有 BaseButton 有（Control 上没有这一项）；不是按钮的控件这一列写 -。
            var disabled = control is BaseButton baseButton && baseButton.Disabled;
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"{layer} d{depth} {control.Name} rect=({rect.Position.X:0.#},{rect.Position.Y:0.#} {rect.Size.X:0.#}x{rect.Size.Y:0.#}) " +
                $"vis={control.Visible} dis={(control is BaseButton ? disabled.ToString() : "-")} txt={text}"));

            Describe(layer, control, depth + 1, lines);
        }
    }

    /// <summary>第 <paramref name="index"/> 个蓝图槽位在屏幕上的矩形（0 在最左）。</summary>
    public static Rect2 BlueprintSlotRect(int index, Vector2 viewportSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        var x = BlueprintBarSideMargin + (index * (BlueprintSlotWidth + BlueprintSlotGap));
        var y = viewportSize.Y - BlueprintBarBottomMargin - BlueprintSlotHeight;
        return new Rect2(x, y, BlueprintSlotWidth, BlueprintSlotHeight);
    }

    /// <summary>铺满视口的一层 <see cref="Control"/>；本身不吃鼠标，子控件照常收。</summary>
    private static Control MakeLayer(string name, Node parent)
    {
        var layer = new Control
        {
            Name = name,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        layer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        parent.AddChild(layer);
        return layer;
    }
}
