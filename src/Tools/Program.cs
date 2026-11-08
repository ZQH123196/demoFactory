// 开发期工具 CLI —— 造型生成 / 离线校验器 / 预览舞台 / 模型体检单 / 重放取证 / DebugBridge 的统一入口。
// 独立 csproj：不进游戏、不被游戏 csproj 编译（与 Tests 项目同一排除模式）。
//
// 这个文件里不许出现任何原型名：原型靠 IPrototypeTools 自报家门（见 IPrototypeTools.cs），
// csproj 通配 src/Prototypes/*/Tools/*.cs，所以加原型不改编排层。

#nullable enable
using System;
using System.Linq;

namespace DemoFactory.Tools;

public static class Program
{
    public static int Main(string[] args)
    {
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; }
        catch { /* 输出重定向下的宿主差异，忽略 */ }

        try
        {
            // 与原型无关的全局工具
            if (args.Length >= 2 && args[0] == "trx") return Trx.Run(args);
            if (args.Length >= 2 && args[0] == "debug") return Debug.Run(args);
            if (args.Length >= 2 && args[0] == "game") return Game.Run(args);
            if (args.Length >= 2 && args[0] == "uitest") return UiTest.Run(args);
            if (args.Length >= 1 && args[0] == "register-translations") return RegisterTranslations.Run(args);

            if (args.Length >= 2 && Prototypes.Find(args[1]) is { } proto)
            {
                // review / skill 是公共层动词：实现了相应契约的原型自带体检单
                if (args[0] == "review" && args.Length >= 3) return Review.Run(args[2], proto, args[3..]);
                if (args[0] == "skill")
                {
                    // skill 的位置参数只有「项目根」那一个，其余全是 -- 开头的开关
                    var rest = args.Length >= 3 ? args[2..] : Array.Empty<string>();
                    var root = rest.FirstOrDefault(a => !a.StartsWith("--")) ?? ".";
                    return Skill.Run(root, proto, rest.Where(a => a != root).ToArray());
                }
                if (proto.RunVerb(args[0], args) is var r && r >= 0) return r;
            }
            return Usage();
        }
        catch (Exception e)
        {
            // 工具进程的异常打全量（含调用栈）：这里的一行 Message 曾把「体检单没出报告」变成一次考古
            Console.Error.WriteLine(e.ToString());
            return 1;
        }
    }

    private static int Usage()
    {
        var aliases = Prototypes.All.Count > 0
            ? string.Join(" | ", Prototypes.All.Select(p => p.DirName))
            : "（没有已注册的原型：在 src/Prototypes/<原型>/Tools/ 下实现 IPrototypeTools）";
        Console.Error.WriteLine(
            $"""
            用法（项目根执行；<原型> = {aliases}）：
              dotnet run --project src/Tools -- debug <verb> [args] [--port n]   # DebugBridge（动词见 debug 无参输出）
              dotnet run --project src/Tools -- game list                    # 现在有几局在跑、分别是谁
              dotnet run --project src/Tools -- game start <res://场景> [--port n] [--window]
                                                                             # 起一局（起前必探端口，有人应答就拒绝起第二局）
              dotnet run --project src/Tools -- game stop --port n | --pid n | all
              dotnet run --project src/Tools -- uitest <脚本.json> [--port n] [--godot=<exe>] [--scene=res://场景]
                                                                             # 无头起一局、按脚本点控件发命令断言读数（无头不出图；全过退 0，有失败退 1）
              dotnet run --project src/Tools -- trx <file.trx> [--all]          # 读测试结果（失败名+断言消息+文件行号）
              dotnet run --project src/Tools -- replay <原型> <script.json> [--report|--bisect]  # 离线重放取证
              dotnet run --project src/Tools -- gen <原型> .                    # 造型源场景
              dotnet run --project src/Tools -- check <原型> . [dump]           # 离线校验器（退出码非 0 即不通过）
              dotnet run --project src/Tools -- preview <原型> . [clip]         # 预览舞台
              dotnet run --project src/Tools -- review <原型> . [模型|--only=模型] [--godot=<exe>]
                                                                                # 模型体检单：三栏图 + PASS/FAIL 报告（要开窗口出图）
              dotnet run --project src/Tools -- skill <原型> . [--godot=<exe>]  # 技能体检单：三栏 + 前摇对表校验（无头读时长即可）
              dotnet run --project src/Tools -- skill <原型> --selfcheck        # 只验校验器自己判得对不对，不起引擎、不读技能表
              dotnet run --project src/Tools -- audiogen BeastArena . [verify]   # 音效合成：全部 WAV 与四段技术样本从零重跑（逐字节确定；verify=重跑并与盘上文件比对）
              dotnet run --project src/Tools -- register-translations [--force]  # i18n：把各原型 i18n/*.csv 生成 .translation 并注册进 project.godot
            """);
        return 2;
    }
}
