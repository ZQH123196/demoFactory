#nullable enable

namespace DemoFactory.GugaFramework.EventBus.Events;

/// <summary>
/// 占位事件类型，用于基础设施验证。
/// 随着原型实现，替换为实际游戏事件。
/// 
/// 设计思路：
/// - 使用 readonly record struct 实现值类型，零堆分配
/// - 不可变语义，线程安全
/// - 支持模式匹配，灵活处理事件
/// </summary>
public readonly record struct GameStarted();

public readonly record struct GamePaused(bool IsPaused);
