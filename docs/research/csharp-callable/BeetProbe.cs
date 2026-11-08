#nullable enable
using Godot;

namespace DemoFactory.Research;

/// <summary>
/// 一次性验证用节点：被 GDScript 按方法名调用，用来确定 C# 方法在 Godot 侧的真实注册名。
/// 结论记录在 docs/research/README.md，本文件不参与任何游戏逻辑。
/// </summary>
[GlobalClass]
public partial class BeetProbe : Node
{
    /// <summary>小写名字，直接照 Beehave 协议里的写法。</summary>
    public int tick(GodotObject? actor, GodotObject? blackboard) => 0;

    /// <summary>带下划线前缀的小写名字。</summary>
    public int _safe_tick(GodotObject? actor, GodotObject? blackboard) => 2;

    /// <summary>void 返回值的小写名字。</summary>
    public void interrupt(GodotObject? actor, GodotObject? blackboard)
    {
    }

    /// <summary>标准 PascalCase，作为对照。</summary>
    public int TickPascal(GodotObject? actor, GodotObject? blackboard) => 1;

    /// <summary>协议要求的带类型返回值。</summary>
    public Godot.Collections.Array<StringName> get_class_name() =>
        new Godot.Collections.Array<StringName> { "BeetProbe" };

    /// <summary>无参方法，作为对照。</summary>
    public int Ping() => 7;
}
