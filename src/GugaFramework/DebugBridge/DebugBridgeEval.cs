#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Scripting.Hosting;

namespace DemoFactory.GugaFramework.DebugBridge;

/// <summary>
/// eval 脚本的<b>引用面契约</b> —— 原型在自己的 globals 类上声明脚本要额外导入哪些命名空间。
///
/// 开这条接口的原因：公共层曾把某一个原型的逻辑层命名空间写死在
/// <see cref="DebugBridgeEval"/> 的导入清单里（GugaFramework 认识某个原型的命名空间）。后来请回
/// 原型侧：谁要脚本里少写一段全名，谁在自己的 globals 上声明。
/// 不实现本接口也合法 —— 那时脚本用得上固定那几条导入，其余写全名。
/// </summary>
public interface IEvalImports
{
    /// <summary>追加进 <c>ScriptOptions.AddImports</c> 的命名空间清单。</summary>
    IReadOnlyList<string> EvalImports { get; }
}

/// <summary>
/// DebugBridge 的 eval 求值器 — Roslyn C# Scripting 进程内求值任意 C# 表达式。
///
/// 设计（计划 Phase 1 动态层，用户确认：动态求值不可割舍，不设砍档）：
/// - 语言从 GDScript 子集换成全量 C#：lambda/elif/`str()` 包 bool/`%` 路径等
///   eval 故障表条目整体蒸发；可达面更宽（GugaFramework 与 Logic 类型直接在手）。
/// - globals：原型侧自带一个 public 嵌套类，脚本里 <c>Main</c>（原型主节点）与它其余的
///   字段都是全量强类型 C#，零 dynamic。可访问性与具体类型两条都满足才用得上真方法。
/// - 引用面：游戏主程序集（含各原型的 Logic）+ GodotSharp + 运行库，
///   命名空间导入清单 = 公共层那几条 + 原型经 <see cref="IEvalImports"/> 声明的那几条。
/// - 每发独立求值（无跨发脚本状态）：一发一编译，语义干净、失败即报不残留。
/// - 本类是表现层组件的一部分（同 DebugBridgeHost），测试项目已显式 Exclude。
///
/// 信任边界：与本机开发期的 MCP eval 同级（任意代码执行），仅 localhost、
/// 默认关、不出货 — 不是新增暴露面，是换语言。
/// </summary>
public static class DebugBridgeEval
{
    /// <summary>
    /// 建强类型求值器：globals 由原型侧自带（含具体的 Main / World 字段），
    /// 想要额外的命名空间导入就让 globals 类实现 <see cref="IEvalImports"/>。
    /// 引用面在首求值时构建；每次求值独立编译执行。
    /// 关键：globals 所在程序集（游戏主程序集）与 GodotSharp 必须 RegisterDependency 进
    /// Roslyn 的 InteractiveAssemblyLoader — 否则 Roslyn 会把 DemoFactory.dll 再加载一遍，
    /// globals 双份类型 → InvalidCastException（headless 实测）。
    /// </summary>
    public static Func<string, object?> CreateEvaluator<TGlobals>(TGlobals globals)
    {
        var extraImports = globals is IEvalImports declared ? declared.EvalImports : [];
        var loader = new InteractiveAssemblyLoader();
        loader.RegisterDependency(typeof(TGlobals).Assembly); // globals 类型宿主（游戏主程序集）
        loader.RegisterDependency(typeof(Godot.Node).Assembly);    // GodotSharp（脚本常触达）
        ScriptOptions? options = null;
        var globalsType = typeof(TGlobals);
        return code =>
        {
            options ??= BuildOptions(extraImports); // 惰性：首求值时再构建引用面
            var script = CSharpScript.Create(code, options, globalsType, loader);
            var state = script.RunAsync(globals).GetAwaiter().GetResult();
            return state.ReturnValue;
        };
    }

    /// <summary>
    /// 引用面：显式列举关键程序集。**游戏主程序集的 Location 在 Godot 自定义加载上下文里
    /// 为空**（headless 实测：Assembly.Location=""，MetadataReference 拒收）— 从
    /// AppContext.BaseDirectory 按程序集名补回文件路径。基元运行库靠 Scripting 包隐式引用。
    /// </summary>
    private static ScriptOptions BuildOptions(IReadOnlyList<string> extraImports)
    {
        var refs = new[]
        {
            typeof(DebugBridgeEval).Assembly,          // DemoFactory（游戏主程序集，含 Logic/Main）
            typeof(Godot.Node).Assembly,               // GodotSharp
            typeof(System.Numerics.Vector2).Assembly,  // System.Runtime
            typeof(System.Linq.Enumerable).Assembly,   // System.Linq
        };
        var baseDir = AppContext.BaseDirectory;
        var metadata = refs
            .Where(a => !a.IsDynamic)
            .Select(a => new
            {
                Assembly = a,
                Path = string.IsNullOrEmpty(a.Location)
                    ? Path.Combine(baseDir, a.GetName().Name + ".dll") // Godot 加载上下文：Location 空，按名补
                    : a.Location,
            })
            .Where(x => File.Exists(x.Path))
            .Select(x => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(x.Path));
        var imports = new List<string>
        {
            "System",
            "System.Collections.Generic",
            "System.Linq",
            "System.Numerics",
            "Godot",
            "DemoFactory.GugaFramework.Replay",
            "DemoFactory.GugaFramework.DebugBridge",
        };
        imports.AddRange(extraImports); // 原型侧那几条从这里进，本文件不再出现任何原型名
        return ScriptOptions.Default
            .AddReferences(metadata)
            .AddImports(imports);
    }

    /// <summary>eval 诊断：列出引用面程序集名（排查 CS0246 用，验收后可留作 verbs 的一部分）。</summary>
    public static object? Diagnose()
    {
        var refs = new[]
        {
            typeof(DebugBridgeEval).Assembly,
            typeof(Godot.Node).Assembly,
            typeof(System.Numerics.Vector2).Assembly,
            typeof(System.Linq.Enumerable).Assembly,
        };
        var baseDir = AppContext.BaseDirectory;
        return new
        {
            base_directory = baseDir,
            assemblies = refs.Select(a => new
            {
                name = a.GetName().Name,
                location = a.Location,
                resolved = string.IsNullOrEmpty(a.Location)
                    ? Path.Combine(baseDir, a.GetName().Name + ".dll")
                    : a.Location,
            }).ToArray(),
        };
    }
}
