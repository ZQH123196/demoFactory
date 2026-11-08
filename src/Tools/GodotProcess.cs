// 引擎子进程的统一跑法 —— 排空输出与超时必须落在同一个等待里。
//
// 教训（2026-10-29 实锤）：先 ReadToEnd 再 WaitForExit(超时) 时，引擎一旦挂死，
// 管道永不关闭，流量读取永远不返回，超时那一行根本轮不到执行 —— 工位跟着一起挂住。
// 这里用异步读两路输出 + 带超时的 WaitForExit；超时就连子进程树一起杀。

#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace DemoFactory.Tools;

public static class GodotProcess
{
    /// <summary>跑一次引擎，返回退出码与末 30 行输出。超时抛异常（已杀进程树）。</summary>
    /// <param name="what">超时报错的主语，如「审阅运行」。</param>
    public static (int Code, string Tail) Run(string exe, List<string> args, int timeoutMs, string what)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Godot 进程没起来：" + exe);
        // 两路必须同时排空：只读一路时，另一路管道写满会把引擎卡住。
        var so = p.StandardOutput.ReadToEndAsync();
        var se = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(timeoutMs))
        {
            try { p.Kill(true); } catch { /* 已经自己退了 */ }
            p.WaitForExit(10_000);
            throw new InvalidOperationException($"{what}超过 {timeoutMs / 1000} 秒还没结束，已终止 Godot。");
        }
        var tail = (so.GetAwaiter().GetResult() + se.GetAwaiter().GetResult()).Trim();
        var lines = tail.Split('\n');
        return (p.ExitCode, lines.Length <= 30 ? tail : string.Join('\n', lines[(lines.Length - 30)..]));
    }
}
