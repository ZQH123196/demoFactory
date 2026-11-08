#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using DemoFactory.GugaFramework.Guard;
using Godot;

namespace DemoFactory.GugaFramework.DebugBridge;

/// <summary>
/// 操控台接线的<b>唯一一处</b> —— 每个原型都要写、写的都一样的那 15 行，收编在这里
/// （AGENTS「沉淀方向」「脚手架趋零」：反复出现的样板要提炼回去，让下一个原型生下来就带上时间可控、状态可查、行为可注入这三个口）。
///
/// 收编了什么：建宿主、<c>Expose("main", 主节点)</c>、强类型 eval 求值器、
/// 七个标准动词（ping / verbs / targets / call / eval / evaldiag / logs）。
/// 没收编的：<b>原型自己的动词</b> —— 每个原型的写入口不一样（重放词表 inject、
/// 摆子/开局各有各的那一套），那些必须留在原型侧，用 <c>verbs</c> 参数递进来。
///
/// 调用方要用对这次接线，需要知道的四件事：
/// ① <paramref name="globals"/> 要是原型自己的 public 嵌套类、字段类型要具体 —— Roslyn 脚本
///    编译要可访问性，字段是 <c>object</c> 就得靠 dynamic；脚本要额外导入哪些命名空间，
///    让这个类实现 <see cref="IEvalImports"/>。
/// ② 与标准动词<b>同名</b>的原型动词<b>覆盖</b>标准那一条（注册表是 verb → 处理器一个键），
///    读 <c>verbs</c> 时看不出谁覆盖了谁 —— 想改名别撞这七个。
/// ③ 返回的宿主一直留着：<b>重开一局换 globals 走的通路是</b>
///    <c>host.SetEvaluator(DebugBridgeEval.CreateEvaluator(newGlobals))</c>（重建树会换掉世界引用）。
/// ④ 动词处理器里读参数只有一个口：<see cref="BridgeArgs"/> 的 <c>Num</c>/<c>Int</c>/<c>Str</c>。
///
/// 动词处理器的形状本身受两条纪律约束（本文件不复述措辞，指针在此）：
/// AGENTS.md 「桥只转发」（只转发既有 eval 直达方法）、「写路径进 Journal」（写操作走 inject 进 Journal）。
///
/// 为什么留「默认关」这条：端口没配置时宿主根本不监听（见 <see cref="DebugBridgeHost"/>），
/// 接了桥的出货版本零开销、零暴露面。
/// </summary>
public static class DebugConsole
{
    /// <summary>
    /// 挂上调试桥并注册标准动词，返回宿主（原型侧要留着它继续 <c>Register</c> 自己的动词，
    /// 或在重开一局时换 eval 的 globals）。
    /// </summary>
    /// <param name="parent">主场景节点（桥宿主挂在它下面，随场景一起释放）。</param>
    /// <param name="globals">
    /// eval 脚本的强类型 globals —— 原型侧自带一个 public 嵌套类、里面放 <c>Main</c> 等具体类型引用，
    /// 这样脚本里 <c>Main.DebugSnapshot()</c> 是全量 C# 而不用 dynamic。
    /// </param>
    /// <param name="verbs">原型自己的动词表（写入口、直达读数）；与标准动词同名时后注册的覆盖前一个。</param>
    public static DebugBridgeHost Attach<TGlobals>(Node parent, TGlobals globals,
        params (string Verb, Func<JsonElement, object?> Handler)[] verbs)
    {
        var host = new DebugBridgeHost { Name = "DebugBridge" };
        parent.AddChild(host);
        host.Expose("main", parent);
        host.SetEvaluator(DebugBridgeEval.CreateEvaluator(globals));

        host.Register("ping", _ => host.PingVerb(default));
        host.Register("verbs", _ => host.VerbsVerb(default));
        host.Register("targets", _ => host.TargetsVerb(default));
        host.Register("call", a => host.CallVerb(a));
        host.Register("eval", a => host.EvalVerb(a));
        host.Register("evaldiag", _ => DebugBridgeEval.Diagnose());

        // 日志读口（OOM 防线之一：agent 读有界缓冲，必看 dropped_count —— 「日志里没有」不等于「没发生」）
        host.Register("logs", a =>
        {
            var (lines, dropped, total) = RollingLog.Tail(a.Int("count", 200));
            return new Dictionary<string, object?>
            {
                ["dropped_count"] = dropped,
                ["total"] = total,
                ["lines"] = lines,
            };
        });

        foreach (var (verb, handler) in verbs) host.Register(verb, handler);
        return host;
    }
}
