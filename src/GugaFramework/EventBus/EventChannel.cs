#nullable enable
using System;

namespace DemoFactory.GugaFramework.EventBus;

/// <summary>
/// 类型安全的事件通道，纯 C# 多播事件，零第三方依赖。
///
/// 设计思路：
/// - 底层就是标准 C# <c>event</c>，一个事件类型对应一个 EventChannel 实例
/// - 订阅/退订用 <c>+= / -=</c>，与原型侧既有写法一致（<c>_clock.Pulse += Step</c> 同款）
/// - 通道与进程同寿，没有 dispose 概念：谁也掐不死它，退订只发生在订阅方离场时
/// - 原型间通信只走 EventBus 上声明的通道，严禁直接引用其他原型
/// </summary>
/// <typeparam name="T">事件消息类型，推荐使用 readonly record struct 实现零分配</typeparam>
public sealed class EventChannel<T>
{
    /// <summary>
    /// 消息发出时触发。订阅 <c>channel.Published += handler</c>，
    /// 退订 <c>channel.Published -= handler</c>。纯 C# 逻辑层与表现层都能直接用，
    /// 不需要任何节点引用。
    /// </summary>
    public event Action<T>? Published;

    /// <summary>
    /// 发布事件到所有订阅者。没有订阅者时是空操作。
    /// </summary>
    public void Publish(T message) => Published?.Invoke(message);
}
