// trx 读取动词（遗留账 1，2026-09-24）— 「读失败输出」的一等公民命令。
//
// 目的（遗留账"目的"栏）：让 agent 读 dotnet test 失败输出这一步变便宜——
// 控制台失败输出经管道会碎裂（ANSI 切割 + 堆栈交错，drill A 实测白跑一发预算），
// 惯例是 --logger trx 落盘再读；但 trx 是带命名空间的 XML，每次会话现场手搓
// PowerShell 解析器 ~3 min 且踩命名空间坑（Phase 4 复测自报卡点）。本动词一发吐出
// 「测试名 + 断言消息（含期望/实值）」表格，agent 零解析成本。
//
// 纯离线零游戏知识：只认 trx 的标准结构（TestRun/UnitTestResult/Message），
// 与原型、测试框架版本无关。返回码：0 = 无失败；1 = 有失败；2 = 用法/文件错误。

#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace DemoFactory.Tools;

public static class Trx
{
    /// <summary>
    /// trx &lt;file.trx&gt; [--all]
    /// 缺省 = 只列失败（测试名 + 断言消息前 4 行 + 堆栈首行）；--all = 追加全量结果清单。
    /// 返回码：0 无失败 / 1 有失败 / 2 用法或文件错误。
    /// </summary>
    public static int Run(string[] args)
    {
        var file = args.FirstOrDefault(a => a.EndsWith(".trx", StringComparison.OrdinalIgnoreCase));
        if (file == null || !File.Exists(file))
        {
            Console.Error.WriteLine("用法：dotnet run --project src/Tools -- trx <file.trx> [--all]");
            return 2;
        }
        var all = Array.Exists(args, a => a == "--all");

        var doc = XDocument.Load(file);
        // trx 默认命名空间（2010 TeamTest）——按命名空间取元素，不猜前缀
        //（agent 手搓解析器的坑点：PowerShell XML API 报「Namespace prefix not defined」）
        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
        var results = doc.Descendants(ns + "UnitTestResult").ToList();
        var passed = results.Count(r => (string?)r.Attribute("outcome") == "Passed");
        var skipped = results.Count(r => (string?)r.Attribute("outcome") == "NotExecuted");
        var failed = results.Where(r => (string?)r.Attribute("outcome") == "Failed").ToList();

        if (failed.Count == 0)
        {
            Console.WriteLine($"无失败（通过 {passed} / 跳过 {skipped} / 总计 {results.Count}）");
            return 0;
        }

        Console.WriteLine($"失败 {failed.Count} / 总 {results.Count}（通过 {passed} / 跳过 {skipped}）：");
        foreach (var r in failed)
        {
            var name = (string?)r.Attribute("testName") ?? "(未名)";
            Console.WriteLine($"--- {name}");
            var msg = r.Element(ns + "Message")?.Value ?? "(无消息)";
            foreach (var line in msg.Replace("\r\n", "\n").Split('\n')
                     .Select(l => l.TrimEnd()).Where(l => l.Length > 0).Take(4))
                Console.WriteLine($"    {line}");
            // 堆栈只给测试方法所在首行（文件:行号 是排障第一入口）
            var stack = r.Element(ns + "StackTrace")?.Value;
            var frame = stack?.Replace("\r\n", "\n").Split('\n')
                .Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("at "));
            if (frame != null) Console.WriteLine($"    @ {frame}");
        }

        if (all)
        {
            foreach (var r in results.Where(r => (string?)r.Attribute("outcome") != "Failed"))
                Console.WriteLine($"    [{r.Attribute("outcome")?.Value}] {r.Attribute("testName")?.Value}");
        }
        return 1;
    }
}