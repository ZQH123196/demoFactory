// 原型注册表 —— 开发期工具的「原型无关」边界。
//
// 为什么要有这一层：动词分发曾经是按一个原型名硬编码的一句 `if`（史实见 git log），
// 那等于把「第二个原型接不上」写进了公共层。现在的规矩是：
//   · 新原型在自己的 src/Prototypes/<原型>/Tools/ 下放一个实现 IPrototypeTools 的类；
//   · csproj 按 `Prototypes\*\Tools\*.cs` 通配源码链接，编译期自动带进来；
//   · 公共层（Program / Review / Tscn / PreviewStage …）一行都不许出现原型名。
//
// 契约一律由原型侧显式给值，公共层不提供默认（同 GugaFramework/Tools/README.md 末节的口径）。

#nullable enable
using System;
using System.Collections.Generic;

namespace DemoFactory.Tools;

/// <summary>一个原型对开发期工具的全部暴露面。实现类需要无参构造。</summary>
public interface IPrototypeTools
{
    /// <summary>src/Prototypes/ 下的目录名。命令行别名按规范化（去 '-'、忽略大小写）匹配它。</summary>
    string DirName { get; }

    /// <summary>模型体检单的契约：审哪些模型、采哪些动作、占地上限与动作清单、报告落哪。</summary>
    ReviewContract Review();

    /// <summary>
    /// 技能体检单的契约（P5）：报哪些技能、每条的前摇载体是谁、出手节奏那几行、报告落哪。
    /// <b>缺省不实现</b> —— 没有技能层的原型不该被校验拖进来，
    /// 而接了技能层的原型只在自己的 <c>Tools/</c> 下多一份契约文件，公共层照旧不改。
    /// </summary>
    SkillContract? Skills() => null;

    /// <summary>
    /// 原型自有动词（gen / check / preview / replay 这类造型与取证线）。
    /// argv 是原始命令行（argv[0]=动词、argv[1]=原型名、argv[2..]=参数），原样交给原型自己解析。
    /// 返回 -1 表示「这个动词不归我管」，由 Program 统一打用法。
    /// </summary>
    int RunVerb(string verb, string[] argv);
}

public static class Prototypes
{
    /// <summary>本程序集里所有已注册的原型（csproj 通配源码链接进来的都算）。</summary>
    public static IReadOnlyList<IPrototypeTools> All { get; } = Discover();

    public static IPrototypeTools? Find(string alias)
    {
        var key = Normalize(alias);
        foreach (var p in All)
            if (Normalize(p.DirName) == key) return p;
        return null;
    }

    /// <summary>三种写法认作同一个键：去掉分隔符、忽略大小写（PascalCase / kebab-case / 连写小写）。</summary>
    public static string Normalize(string alias)
    {
        var chars = new char[alias.Length];
        var n = 0;
        foreach (var ch in alias)
            if (char.IsLetterOrDigit(ch)) chars[n++] = char.ToLowerInvariant(ch);
        return new string(chars, 0, n);
    }

    private static IReadOnlyList<IPrototypeTools> Discover()
    {
        var found = new List<IPrototypeTools>();
        foreach (var t in typeof(Prototypes).Assembly.GetTypes())
        {
            if (t.IsAbstract || t.IsInterface || !typeof(IPrototypeTools).IsAssignableFrom(t)) continue;
            if (t.GetConstructor(Type.EmptyTypes) == null)
            {
                Console.Error.WriteLine($"⚠ {t.FullName} 实现了 IPrototypeTools 但没有无参构造，已跳过");
                continue;
            }
            found.Add((IPrototypeTools)Activator.CreateInstance(t)!);
        }
        found.Sort((a, b) => string.CompareOrdinal(a.DirName, b.DirName));
        return found;
    }
}
