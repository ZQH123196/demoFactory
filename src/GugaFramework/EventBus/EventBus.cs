#nullable enable
using DemoFactory.GugaFramework.EventBus.Events;

namespace DemoFactory.GugaFramework.EventBus;

/// <summary>
/// 全局静态事件总线 — 所有事件通道的集中入口。
///
/// 设计思路：
/// - 纯静态类，不继承 Godot Node，纯 C# 层零 Godot 依赖
/// - 所有 EventChannel 声明为 static readonly，生命周期与进程同寿
/// - 事件通道集中声明，一目了然系统有哪些事件
/// - 原型间通信通过共享 EventChannel 完成，严禁直接引用其他原型
///
/// 使用方式：
///   发布: EventBus.GameStarted.Publish(new GameStarted());
///   订阅: EventBus.GameStarted.Published += e => ...;   退订: -= e => ...
///
/// 架构位置：中台通信层
/// </summary>
public static class EventBus
{
    // === 核心游戏事件 ===

    /// <summary>游戏启动事件。</summary>
    public static readonly EventChannel<GameStarted> GameStarted = new();

    /// <summary>游戏暂停/恢复事件。</summary>
    public static readonly EventChannel<GamePaused> GamePaused = new();

    // === 原型间通信通道 ===
    // 随原型实现在此添加，示例：
    // public static readonly EventChannel<PrototypeAMessage> PrototypeAMessage = new();
}
