#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;

namespace DemoFactory.GugaFramework.Construction;

// 候选点输入口 + 悬停裁决的推送流（D27~D29、ADR 0006）。
//
// 形状就一句话：<b>表现层推手势描述，模块吸附去重，裁决变了才推出去</b>。没有「每帧拉一次
// Check」那种形状 —— 那是迁移前的表现层镜像，靠注释维持同源、
// 会漂，本模块是它的反面教材。
//
// 三条硬契约（D28），都在这一层落地：
//   1. 悬停态不进 Journal（这个口是 call 类，「写路径进 Journal」）；
//   2. 悬停态不进检查点快照 —— 所以这一族一个字都不进 ConstructionCore 的快照段，
//      连当前手势都不进（视图侧状态在确定性面上零存在）；
//   3. Commit 显式自带候选点参数，不消费悬停值（Journal 录参数，重放自包含）。
//
// 去重是两层，各挡一类抖动：
//   第一层「手势描述字面没变」→ 零计算（鼠标没动就别跑校验）；
//   第二层「吸附之后裁决没变」→ 零事件（鼠标在格内 / 量化档内微动不刷事件）。
// 第二层才是那条真正值钱的不变量：表现层每帧推坐标，事件流不会每帧长一条。
//
// 批量形状（ticket 04，D25）走的是同一个口、同一份去重：推一条画线手势收一条带成员表的裁决，
// 逐成员的红绿从 <see cref="HoverVerdict.Members"/> 读（D29）—— 悬停这一侧没有第二个
// 「批量版查询」，切形状与切工具都只经 Push 这一个入口。

/// <summary>
/// 一次悬停裁决 —— 推送出去的形状（typed record 带步号，D27）。
///
/// 字段与 <see cref="PlacementVerdict"/> 同源：表现层显示「能不能放 / 落在哪 / 占哪些格 /
/// 什么角度」只准读这一条，禁止第二次计算（D29）。
/// </summary>
/// <param name="Tick">第几步（构造时递进来的那个时钟读数）。</param>
/// <param name="BlueprintId">这次推的蓝图 id。</param>
/// <param name="Ok">放不放得下。</param>
/// <param name="Reason">拒绝原因（D6 封闭枚举）。</param>
/// <param name="Position">吸附后的落点。</param>
/// <param name="Orientation">方向整数（网格 = D4 八值，自由 = 量化角）。</param>
/// <param name="Cells">受影响格名单（网格侧；自由侧是空表）。</param>
/// <param name="Circle">圆形占地半径（自由侧；网格侧是 null）。中心就是 <paramref name="Position"/>。</param>
/// <param name="Recomputes">这是第几次重算。给「零计算」留一个能断言的读数，不然那条契约只能靠感觉。</param>
/// <param name="Shape">这次推的是哪个形状（D25）。表现层靠它分「画一格」与「画一片」的幽灵，
/// 不必拿成员表的长度自己猜（1 格的画线与单件长得一样）。</param>
/// <param name="Members">批量形状的逐成员裁决表；单件是 null。<b>与查询路径读的是同一份</b>，
/// 所以红格不用表现层再算一遍（D29）。</param>
public sealed record HoverVerdict(
    long Tick,
    string BlueprintId,
    bool Ok,
    PlacementReason Reason,
    Vector2 Position,
    int Orientation,
    IReadOnlyList<GridCell> Cells,
    RadiusFootprint? Circle,
    long Recomputes,
    GestureShape Shape = GestureShape.Single,
    IReadOnlyList<PlacementVerdict>? Members = null)
{
    /// <summary>给人读与抽查的那一行字。</summary>
    public string Line
        => $"{ConstructionNumbers.Format(Tick)} hover {(Ok ? "ok" : "no")} {BlueprintId} reason={Reason} " +
            $"pos={ConstructionNumbers.Point(Position)} dir={Orientation} cells={ConstructionNumbers.Cells(Cells)}" +
            (Members is null ? string.Empty : $" members={Members.Count}");

    /// <summary>
    /// 裁决「变了没有」只看裁决本身：<b>步号与重算序号不算</b>。
    /// 它们是这次推送的元数据，不是裁决内容 —— 拿它们比，重算一次就多一条事件，
    /// 去重就白做了。
    ///
    /// <para><b>形状与成员表都算</b>：同一处蓝图标成「画线 5 格」与「画矩形 5 格」时
    /// 落点、方向、格名单全一样，只看那几个字段的话切形状时幽灵不重画（看着是同一片红绿，
    /// 实际枚举的格不同）。</para>
    /// </summary>
    public bool SameVerdictAs(HoverVerdict? other)
    {
        if (other is null)
        {
            return false;
        }

        return Ok == other.Ok
            && Reason == other.Reason
            && BlueprintId == other.BlueprintId
            && Position == other.Position
            && Orientation == other.Orientation
            && Shape == other.Shape
            && RadiusEquals(Circle, other.Circle)
            && SameCells(Cells, other.Cells)
            && SameMembers(Members, other.Members);
    }

    private static bool RadiusEquals(RadiusFootprint? a, RadiusFootprint? b)
        => a is null ? b is null : b is not null && a.Radius.Equals(b.Radius);

    private static bool SameCells(IReadOnlyList<GridCell> a, IReadOnlyList<GridCell> b)
    {
        for (var i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>逐成员表逐条比。判据用每一条自己的裁决（<see cref="PlacementVerdict"/> 是 record，
    /// 逐字段相等），不比集合与不去重 —— 成员次序是契约（D24 的顺序单一真值）。</summary>
    private static bool SameMembers(IReadOnlyList<PlacementVerdict>? a, IReadOnlyList<PlacementVerdict>? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// 悬停裁决流 —— 候选点输入口在这一侧（D27）。
///
/// 用法是订阅 <see cref="Changed"/> 再推手势：推一个 → 收一条；推一个吸附后落到同一处的
/// → 什么都不收。<b>取消防幽灵</b>：推 null（或调 <see cref="Clear"/>）会推一条 null 出去，
/// 表示「这一轮没有候选点了」，表现层据此把幽灵收起来。
/// </summary>
public sealed class HoverVerdicts
{
    private readonly Func<GestureSpec?, PlacementVerdict?> _check;
    private readonly Func<long> _tick;

    public HoverVerdicts(Func<GestureSpec?, PlacementVerdict?> check, Func<long> tick)
    {
        _check = check ?? throw new ArgumentNullException(nameof(check));
        _tick = tick ?? throw new ArgumentNullException(nameof(tick));
    }

    /// <summary>裁决变了才推。<b>参数是 null 表示「没有候选点了」</b>（推 null 与 Clear 同一个出口）。</summary>
    public event Action<HoverVerdict?>? Changed;

    /// <summary>当前手势（悬停态，只活在这儿，不进任何快照）。</summary>
    public GestureSpec? Current { get; private set; }

    /// <summary>最近一次推出去的裁决；取消防幽灵之后是 null。</summary>
    public HoverVerdict? Last { get; private set; }

    /// <summary>真正推出去的条数（含取消防幽灵那一条）。</summary>
    public long Pushes { get; private set; }

    /// <summary>跑过完整校验的次数。「零计算」这条契约的读数就靠它。</summary>
    public long Recomputes { get; private set; }

    /// <summary>
    /// 候选点输入口：推手势描述。四个形状都接（ticket 04，D25）。
    ///
    /// <para><b>自检在存进 Current 之前</b>（<see cref="GestureSpec.Validate"/>）：先存后抛的话，
    /// 当前手势就成了一颗雷 —— 之后每次占用变化触发的重算都会跟着再抛一次，而抛的那个手势
    /// 谁也不记得是谁推的。</para>
    ///
    /// <para>非单件形状不再当场抛了：<b>推线 / 推矩形 / 推布局都能算</b>，红绿从成员表里读（D29）。
    /// 缺件（画线没给终点那类）才抛，抛的是作者错误，不是形状。</para>
    /// </summary>
    public void Push(GestureSpec? gesture)
    {
        gesture?.Validate();

        // 第一层去重：手势描述字面没变 ⇒ 连校验都不跑。
        if (gesture == Current)
        {
            return;
        }

        Current = gesture;
        Recompute();
    }

    /// <summary>手势撤走（取消防幽灵）。推一条 null 出去，别让表现层自己猜。</summary>
    public void Clear() => Push(null);

    /// <summary>
    /// 占用变化后重算（提交 / 移除 / 摧毁的固定点上由写路径自动调）。
    /// 裁决变了才推 —— 变的是「这一格从空变成被占」，幽灵就该从绿变红。
    /// </summary>
    public void Recompute()
    {
        var gesture = Current;
        var verdict = _check(gesture);
        if (gesture is null || verdict is null)
        {
            // 没有候选点（或查询递回来一个空）：推一条 null = 取消防幽灵。
            if (Current is null && Last is null)
            {
                return;
            }

            Last = null;
            Pushes++;
            Changed?.Invoke(null);
            return;
        }

        Recomputes++;
        var next = new HoverVerdict(
            _tick(),
            gesture.BlueprintId,
            verdict.Ok,
            verdict.Reason,
            verdict.SnappedWorld,
            verdict.Orientation,
            verdict.Cells,
            verdict.Circle,
            Recomputes,
            gesture.Shape,
            verdict.Members);

        if (Last is { } previous && previous.SameVerdictAs(next))
        {
            return;
        }

        Last = next;
        Pushes++;
        Changed?.Invoke(next);
    }

    /// <summary>最近一条裁决的那行字（给「当前幽灵是什么」一句读数）。</summary>
    public string? Line => Last?.Line;
}
