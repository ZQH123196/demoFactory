#nullable enable
using System;

namespace DemoFactory.GugaFramework.Construction;

// 建造过程这一族的词汇与纯算术：状态、进度（D16、D17、D18）。
//
// 单独一个文件，是因为这一族<b>一个副作用都没有</b>：「推进 steps 步之后是什么样」是纯函数，
// 离线就能单测。发事件、腾占用、出册那些动作全在 ConstructionCore 一处，理由写在那儿。
// 这么分的另一个好处：「只有两档状态」这条不变量有一个能一眼看完的地方，而它恰恰是最容易
// 被后来的人加出第三档的地方 —— 状态一旦能加档，「这一格占着谁」和「册上有几座」就会给两个答案。

/// <summary>
/// 建造状态 —— <b>只有两档</b>（D16）。
///
/// 为什么不是三档：摧毁不是一个状态，是<b>去向</b>。一座建筑要么还在册（在建 / 完工），
/// 要么已经不在册（Removed 事件带着去向：拆除 / 取消 / 摧毁）。把摧毁做成第三档状态，册里就会
/// 留下一座「已摧毁」的建筑：占用已经腾了、血量是 0，可它还在册上、还在快照段里印一行。
/// 查这一格占着谁回空串、查册却数得到它 —— 显示即真相当场破掉。所以销毁是「从册上除掉」
/// 这一个动作，不是状态迁移。
/// </summary>
public enum BuildState
{
    /// <summary>在建：还在数剩余步（D17 的整数倒数）。</summary>
    Building = 0,

    /// <summary>完工。功能由原型挂 <see cref="BuildingCompleted"/> 自己接 —— 模块不认识建筑功能（D3）。</summary>
    Completed = 1,
}

/// <summary>
/// 一座建筑的建造进度：整数倒数 + 暂停标志 + 状态（D17、D18）。
///
/// 三个字段合在一份里而不是散成三个可写字段，是因为它们必须一起变：暂停标志一动，倒数就跟着
/// 不走；剩步到 0，状态就跟着翻面。散开就多出「状态是完工但剩步还有 7」这种组合，而它在快照段
/// 里查不出来 —— 每个字段单看都合法，只有把三个放一起看才发现不对。
///
/// <b>没有定时器对象</b>（D17）：时间由原型在它自己的 Advance 固定点推进模块的
/// <see cref="ConstructionCore.Advance"/>，模块这边只有一个整数。顺序即契约：原型先抽检查点
/// 快照、再推这一步，事件才带得上正确的步号（「先快照后命令」）。
/// </summary>
/// <param name="RemainingTicks">还要几步才完工（逻辑每步 0.1 秒，「时间基准」；报时长别按帧算）。</param>
/// <param name="Paused">暂停中：<see cref="Step"/> 跳过它，<b>模块不问为什么</b>（D18）。</param>
/// <param name="State">两档状态之一（D16）。</param>
public readonly record struct BuildProgress(long RemainingTicks, bool Paused, BuildState State)
{
    /// <summary>
    /// 刚落座时的那一份。<b>工期 0 也从「在建」起步</b> —— 完工是 <see cref="Step"/> 判出来的，
    /// 这样工期 0 与工期 n 走的是同一条流水线，中间没有一处「工期是不是 0」的分支（D16）。
    ///
    /// 这里不校验负数：工期的两个入口（蓝图构造、Commit 逐次覆盖）各自当场抛，
    /// 再加一道只是让人以为这个类型封得住 —— 它的位置参数构造器本来也封不住。
    /// </summary>
    public static BuildProgress Start(long buildDurationTicks)
        => new(buildDurationTicks, false, BuildState.Building);

    /// <summary>
    /// 「它已经在那儿」的那一份 —— <b>场地建筑</b>登记进来时就是它（不是第三档状态，是完工那一档）。
    ///
    /// <para>为什么不走「<see cref="Start"/> 工期 0 之后再 <see cref="Step"/> 一步翻面」那条路：
    /// 那条路会发一条 <c>Completed</c>，而场地建筑并没有「建成」这一发 —— 它是关卡摆位，不是玩家手势。
    /// 订阅者（起产出链、表现层建模型）为了不为它各写一个「这一座是场地建筑」的例外分支，
    /// 就得在每一处都先问一句「这是登记来的还是放下来的」。给这一档一个自己的工厂，
    /// 模块这一侧就不必发那条不存在的建成事件（D16：两档状态不变）。</para>
    /// </summary>
    public static BuildProgress Finished => new(0, false, BuildState.Completed);

    /// <summary>
    /// 推进 <paramref name="steps"/> 步之后的那一份（纯函数，<b>无副作用</b>）。
    ///
    /// 两条原样返回：已完工的（没东西可数了）与暂停中的（暂停跳过倒数，D18）。剩步推到 0 或
    /// 以下就翻成完工，剩步钉在 0 —— 不留负数，让「剩步 &gt; 0 ⇒ 在建」这条在快照段里一眼能验。
    /// </summary>
    public BuildProgress Step(long steps)
    {
        if (steps < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(steps), steps,
                "步数不能是负的：进度是整数倒数，不是双向的（模块没有倒着走的时间）");
        }

        if (State == BuildState.Completed || Paused)
        {
            return this;
        }

        var left = RemainingTicks - steps;
        return left > 0
            ? this with { RemainingTicks = left }
            : new BuildProgress(0, Paused, BuildState.Completed);
    }

    /// <summary>快照与事件里印的读法（小写，与事件行里的 wire 名对得上）。</summary>
    public string StateText => State switch
    {
        BuildState.Building => "building",
        BuildState.Completed => "completed",
        _ => throw new ArgumentOutOfRangeException(nameof(State), State,
            "建造状态只有在建与完工两档（D16：摧毁是去向，不是状态）"),
    };
}
