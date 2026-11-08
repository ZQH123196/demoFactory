// UiTestDriver.cs —— uitest 通道的引擎侧助手：改根视口尺寸、按名字或文字找控件并按下。
//
// 为什么这两件事留在引擎侧：驱动器（src/Tools/UiTest.cs）是纯 .NET 那一半，
// 全程只走调试桥（TCP JSON 行协议），不引 GodotSharp。而「根视口现在多大」
// 「那个按钮在哪、怎么才算真的按下」只有引擎知道 —— 所以本体在这里，
// 引擎侧只认两个动词名：ui_viewport / ui_click。
//
// 两条实测经验（写在《验证操作手册》「uitest 通道」那一节）：
// ① 无头根视口默认 64×64，--resolution 透传无效 —— 视口必须显式设，设完要回读确认。
// ② 无头没有渲染器，screenshot 回 shot=empty-frame —— 出图另起一次窗口模式。

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;

namespace DemoFactory.Prototypes.BeastArena.Ui;

/// <summary>
/// uitest 通道的引擎侧助手。
///
/// <para><b>只做两件事</b>：① 把根视口设成给定尺寸并回读真实值；
/// ② 按名字精确匹配控件并真的按下它（发 pressed 信号，不是移光标）。
/// 别的什么都不做 —— 脚本怎么编排、断言怎么判，都在驱动器那一侧。</para>
///
/// <para><b>找控件的口径</b>：先按 <c>Name</c> 精确匹配；名字没匹配上才回退按
/// <b>可见文字</b>匹配，且只在<b>唯一</b>匹配时才算数 —— 标题屏那三颗按钮
/// （开始游戏 / 操作说明 / 退出）没有名字，只有文字。零匹配或多匹配都判红。</para>
///
/// <para><b>按下是真按下</b>：<c>EmitSignal(pressed)</c> 让按钮挂着的处理器跑起来。
/// 移光标那一路只改指针位置，按钮不会响应 —— 那种「点过了」是假的。</para>
/// </summary>
public partial class UiTestDriver : Node
{
    /// <summary>根视口的目标宽（像素）。无头默认是 64×64，不设的话界面坐标全错。</summary>
    public const int TargetViewportWidth = 1920;

    /// <summary>根视口的目标高（像素）。</summary>
    public const int TargetViewportHeight = 1080;

    /// <summary>
    /// 把根视口设成给定尺寸，并回读真实尺寸。
    ///
    /// <para><b>回读的是视口的可见矩形，不是窗口尺寸</b>：<c>ui_rects</c> 首行
    /// <c>viewport=(W,H)</c> 报的就是它，断言得跟它对齐，不能拿窗口尺寸凑。</para>
    ///
    /// <para>返回 <c>viewport=(W, H) requested=(W, H) match=true|false</c>：
    /// 驱动器只看 <c>match</c>，为假就整轮判红（坐标全错的断言没有意义）。</para>
    /// </summary>
    public string SetViewportSize(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), $"视口尺寸必须为正数，收到 {width}×{height}");

        var root = GetTree().Root;
        root.Size = new Vector2I(width, height);

        var actual = root.GetVisibleRect().Size;
        var match = actual.X == width && actual.Y == height;
        return string.Format(CultureInfo.InvariantCulture,
            "viewport=({0}, {1}) requested=({2}, {3}) match={4}",
            actual.X, actual.Y, width, height, match.ToString().ToLowerInvariant());
    }

    /// <summary>
    /// 按名字精确匹配控件并按下它。
    ///
    /// <para>匹配不到、有歧义、控件不可见或禁用，都<b>当场抛</b> ——
    /// 异常消息就是判红理由（驱动器那一侧原样打出来）。</para>
    /// </summary>
    public string ClickControl(string query)
    {
        if (string.IsNullOrEmpty(query))
            throw new ArgumentException("ui_click 缺 --name（要按什么找控件？）");

        var controls = CollectControls();
        var byName = controls.FindAll(c => c.Name.ToString() == query);
        Control target;
        if (byName.Count == 1)
        {
            target = byName[0];
        }
        else if (query.Contains('/', StringComparison.Ordinal))
        {
            // 带路径的查询（如 TownOrCountry/Pick）：按名字后缀逐级匹配，
            // 用来点那些同名成排的控件（每行一颗 Pick）。
            var parts = query.Split('/');
            var byPath = controls.FindAll(c => PathEndsWith(c, parts));
            if (byPath.Count == 1)
            {
                target = byPath[0];
            }
            else if (byPath.Count > 1)
            {
                throw new InvalidOperationException(
                    $"控件路径「{query}」匹配到 {byPath.Count} 个 —— 不确定按哪一个");
            }
            else
            {
                throw new InvalidOperationException($"没有控件路径「{query}」");
            }
        }
        else if (byName.Count > 1)
        {
            throw new InvalidOperationException(
                $"控件名「{query}」匹配到 {byName.Count} 个（{string.Join("、", byName.Select(c => c.Name.ToString()))}）"
                + "—— 不确定按哪一个");
        }
        else
        {
            // 名字没匹配上：回退按可见文字（标题屏那三颗只有文字）。
            var byText = controls.FindAll(c => c.IsVisibleInTree() && TextOf(c) == query);
            if (byText.Count == 1)
            {
                target = byText[0];
            }
            else if (byText.Count > 1)
            {
                throw new InvalidOperationException(
                    $"文字「{query}」匹配到 {byText.Count} 个可见控件 —— 不确定按哪一个");
            }
            else
            {
                throw new InvalidOperationException(
                    $"没有控件叫「{query}」（按名字没匹配上，按可见文字也没有唯一匹配）");
            }
        }

        if (!target.IsVisibleInTree())
            throw new InvalidOperationException($"控件「{target.Name}」当前不可见，点不动");
        if (target is not BaseButton button)
            throw new InvalidOperationException($"控件「{target.Name}」不是按钮，没有可触发的按下动作");
        if (button.Disabled)
            throw new InvalidOperationException($"控件「{target.Name}」当前是禁用态，点不动");

        // 真的按下：发 pressed 信号，按钮挂着的处理器就此触发。
        button.EmitSignal(BaseButton.SignalName.Pressed);

        return string.Format(CultureInfo.InvariantCulture, "clicked name={0} type={1} text={2}",
            target.Name.ToString(), target.GetType().Name, TextOf(target));
    }

    /// <summary>
    /// 给数量框填一个值（<c>SpinBox</c>）或给输入框填一段文字（<c>LineEdit</c>）。
    ///
    /// <para>只改控件状态，不改世界 —— 动词仍由随后那一按进入。
    /// 调试桥没有文字输入合成，数量框这一档由这里补上。</para>
    /// </summary>
    public string SetControlValue(string query, string value)
    {
        if (string.IsNullOrEmpty(query))
            throw new ArgumentException("ui_set 缺控件名");

        var controls = CollectControls();
        var byName = controls.FindAll(c => c.Name.ToString() == query);
        if (byName.Count != 1)
            throw new InvalidOperationException($"控件名「{query}」匹配到 {byName.Count} 个 —— 填值要唯一");

        var target = byName[0];
        if (!target.IsVisibleInTree())
            throw new InvalidOperationException($"控件「{query}」当前不可见，填不了值");

        switch (target)
        {
            case SpinBox spin:
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    throw new InvalidOperationException($"「{query}」要一个数，收到「{value}」");
                spin.Value = number;
                return string.Format(CultureInfo.InvariantCulture, "set name={0} value={1}", query, spin.Value);

            case LineEdit edit:
                edit.Text = value;
                return $"set name={query} text={value}";

            default:
                throw new InvalidOperationException($"控件「{query}」是 {target.GetType().Name}，不是可填值的控件");
        }
    }

    /// <summary>场景树里所有控件（按树序）。前端层那几颗按钮不在 UI 三层里，一并收进来。</summary>
    private List<Control> CollectControls()
    {
        var list = new List<Control>();
        var scene = GetTree().CurrentScene;
        if (scene is null) return list;
        Walk(scene, list);
        return list;
    }

    private static void Walk(Node node, List<Control> into)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is Control control) into.Add(control);
            Walk(child, into);
        }
    }

    /// <summary>控件的名字链是否以给定序列结尾（<c>TownOrCountry/Pick</c> 这一类路径查询）。</summary>
    private static bool PathEndsWith(Node node, string[] parts)
    {
        var current = node;
        for (var i = parts.Length - 1; i >= 0; i--)
        {
            if (current is null || current.Name.ToString() != parts[i]) return false;
            current = current.GetParent();
        }
        return true;
    }

    /// <summary>控件上的文字（与 <c>ui_rects</c> 那一列同一口径：Label 与 Button 才有）。</summary>
    private static string TextOf(Control control) => control switch
    {
        Label label => label.Text,
        Button button => button.Text,
        _ => "",
    };
}
