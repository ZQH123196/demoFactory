// 引擎可执行文件的定位 —— 三级找法，全仓只此一份。
//
// 口径原样保留（Review / Skill 两处原本各抄了一份，2026-10-06 收编到一处）：
// 命令行 --godot= > 环境变量 GODOT_BIN > PATH 上的 godot。
// 机器级路径既不写进代码也不写进契约（契约提交进版本库，机器路径不提交进版本库）。
// 各调用点自己递尾注（各自的报错上下文不同：模型体检单要窗口、技能体检单可无头）。

#nullable enable
using System;
using System.IO;

namespace DemoFactory.Tools;

public static class GodotBin
{
    /// <summary>按三级找法定位引擎；找不到就抛，尾注由调用点给。</summary>
    public static string Resolve(string flag, string tailHint)
    {
        if (!string.IsNullOrEmpty(flag)) return flag;
        var env = Environment.GetEnvironmentVariable("GODOT_BIN");
        if (!string.IsNullOrEmpty(env)) return env;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (dir.Length == 0) continue;
            var cand = Path.Join(dir, OperatingSystem.IsWindows() ? "godot.exe" : "godot");
            if (File.Exists(cand)) return cand;
        }
        throw new InvalidOperationException(
            "找不到 Godot 可执行文件。三选一：--godot=<路径> ／ 设环境变量 GODOT_BIN ／ 把 godot 放进 PATH。\n"
            + tailHint);
    }
}