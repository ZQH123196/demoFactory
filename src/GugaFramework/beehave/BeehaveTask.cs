#nullable enable
using Godot;

namespace DemoFactory.GugaFramework.Beehave;

/// <summary>
/// 行为树任务的 C# 基类 —— 就放在本包里。这个包（Beehave 2.9.3，原样是 GDScript）的改造方向
/// 就是 C#，新写的 C# 件与还没改写的 <c>.gd</c> 组合节点同目录共存。
///
/// 曾另定过一个「<c>GugaFramework/Beehave/</c>（大写）与 <c>GugaFramework/beehave/</c> 分大小写两个目录」
/// 的摆法，在这台机器上做不到：Windows 大小写不敏感 + <c>core.ignorecase=true</c>，两个名字指向
/// 同一个目录。何况要改造的对象就是这个包，本就不该避开它。
///
/// 为什么靠方法名而不是靠继承：Beehave 的容器不看类型、只按名字喊
/// （<c>composites/selector_reactive.gd</c> 里 <c>var children = get_children()</c> 是无类型的，
/// 随后 <c>child.before_run(...)</c>、<c>child._safe_tick(...)</c>），所以 C# 任务不必继承
/// <c>BeehaveNode</c>，字面上有这几个方法就够。代价是 vendored 侧要放宽 12 处把
/// <c>BeehaveNode</c> 当类型的地方（改成本仓库的改动清单见 docs/GugaFramework/行为树与跨语言共存.md）。
///
/// 下面六个小写方法是**跨语言转发入口**（AGENTS.md 命名规范的唯一例外）：引擎不做
/// PascalCase→snake_case 转换，GDScript 的直接成员调用要求对面真有个字面同名的方法
/// （实测喊 <c>tick_pascal</c> 找不到 <c>TickPascal</c>）。每个只准一行、只做转发，
/// 业务实现写在 PascalCase 虚方法上。
///
/// 约定：**任务的业务逻辑落在纯 C# 类里，节点只做参数搬运** —— 否则决策进不了 <c>dotnet test</c>。
/// 现例见 <c>src/Prototypes/InfinityLord/AI/</c>（HeroBrainPolicy 决策 + Brain*Task 搬运）。
/// </summary>
public abstract partial class BeehaveTask : Node
{
    // Beehave 的状态码，逐字对齐 beehave_node.gd 的 enum { SUCCESS, FAILURE, RUNNING }。
    public const int Success = 0;
    public const int Failure = 1;
    public const int Running = 2;

    public int tick(Node actor, Node blackboard) => Tick(actor, blackboard);

    public void before_run(Node actor, Node blackboard) => BeforeRun(actor, blackboard);

    public void after_run(Node actor, Node blackboard) => AfterRun(actor, blackboard);

    public void interrupt(Node actor, Node blackboard) => Interrupt(actor, blackboard);

    public int _safe_tick(Node actor, Node blackboard) => SafeTick(actor, blackboard);

    public Godot.Collections.Array<StringName> get_class_name() => GetClassName();

    /// <summary>一步决策，返回 <see cref="Success"/> / <see cref="Failure"/> / <see cref="Running"/>。</summary>
    protected virtual int Tick(Node actor, Node blackboard) => Success;

    /// <summary>父节点第一次 tick 本节点之前调一次。</summary>
    protected virtual void BeforeRun(Node actor, Node blackboard)
    {
    }

    /// <summary>本节点交出 SUCCESS 或 FAILURE 之后调一次。</summary>
    protected virtual void AfterRun(Node actor, Node blackboard)
    {
    }

    /// <summary>被父节点打断（树换了支路）时调一次。</summary>
    protected virtual void Interrupt(Node actor, Node blackboard)
    {
    }

    /// <summary>
    /// 组合节点真正喊的是这个。Beehave 原版在这里兜「tick 没返回 int」的底，
    /// C# 侧返回类型已经是 int，所以直接转给 <see cref="Tick"/>。
    /// </summary>
    protected virtual int SafeTick(Node actor, Node blackboard) => Tick(actor, blackboard);

    /// <summary>类名链。不走编辑器插件形态时其实没人读它（调试数据在树侧就过滤掉了）。</summary>
    protected virtual Godot.Collections.Array<StringName> GetClassName() =>
        new() { nameof(BeehaveTask), GetType().Name };
}
