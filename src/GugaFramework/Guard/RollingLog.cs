#nullable enable
using System;
using System.Collections.Generic;

namespace DemoFactory.GugaFramework.Guard;

/// <summary>
/// 滚动日志缓冲（GugaFramework 通用件）— agent 读日志永远读有界缓冲，不面对失控文件。
///
/// 背景（2026-09-23 OOM 事故防线 #3，红线见 AGENTS.md「进程与运行时安全」）：进程内拦不到
/// 引擎核心 ERROR（Godot 文件日志对核心 ERROR 无字节上限——洪水主体），但**我们自己的
/// 日志可以全程有界**：原型 GD.Print 调用点换 Log() 包装，固定条数环形缓冲；
/// DebugBridge 的 logs 动词读尾部 — agent 拿到的日志口径有上界。
///
/// 纯 C# 零引擎依赖（逻辑层同规）— 测试项目可直接链（防线组件允许进测试）。
/// </summary>
public static class RollingLog
{
    /// <summary>缓冲条数上限（正常局事件日志 ~26K 行/19 分钟 — 4096 条足够回看；洪水时丢旧保新）。</summary>
    public const int Capacity = 4096;

    private static readonly object Gate = new();
    private static readonly Queue<string> Lines = new(Capacity);
    private static long _dropped;
    private static long _seq;

    /// <summary>记一条（线程安全 — bridge 后台线程/主线程都可能写）。</summary>
    public static void Write(string line)
    {
        lock (Gate)
        {
            _seq++;
            if (Lines.Count >= Capacity)
            {
                Lines.Dequeue();
                _dropped++;
            }
            Lines.Enqueue($"[{_seq}] {line}");
        }
    }

    /// <summary>读尾部 n 条（默认 200）；返回带累计丢弃数的报告（logs_read 必看 dropped_count）。</summary>
    public static (IReadOnlyList<string> Lines, long Dropped, long Total) Tail(int count = 200)
    {
        lock (Gate)
        {
            var take = Math.Min(count, Lines.Count);
            var result = new string[take];
            var start = Lines.Count - take;
            var i = 0;
            foreach (var line in Lines)
            {
                if (i >= start) result[i - start] = line;
                i++;
            }
            return (result, _dropped, _seq);
        }
    }
}
