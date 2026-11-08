// 排兵布阵（BattleArray）的开发期工具 spec —— 注册入口。
//
// 机制：src/Tools/DemoFactory.Tools.csproj 按 `Prototypes\*\Tools\*.cs` 通配源码链接本文件，
// src/Tools/Program.cs 反射发现 IPrototypeTools 的实现 —— 所以接新原型不需要改公共层一行 C#。
//
// 本原型这条线还有一格在 GDScript 侧：模型导出器 src/GugaFramework/Tools/glb/export_glb.gd
// 要在引擎里跑（CSG 合并网格、GLTFDocument 导出都在引擎内），C# 反射够不到它。
// 该脚本按 `--proto=BattleArray` 去 load 本原型 Tools/ExportModels.gd 那张登记表
// （模型名 → 源场景 + 要烘的动作 clip），新原型只加那一个 .gd 文件。

#nullable enable
using System;

namespace DemoFactory.Tools.BattleArray;

public sealed class BattleArrayTools : IPrototypeTools
{
    // 与 src/Prototypes/<目录名> 一致；命令行别名按规范化匹配（大小写与 '-' 无关）
    public string DirName => "BattleArray";

    /// <summary>
    /// 模型体检单契约（《模型与动画准则》「实现分层与聚合」第 4 条：清单与契约属于原型，介质参数属于公共层）。
    ///
    /// 占地 3.0 米的来路与 Check.cs 同源（design.md「造型契约」）：步战单位水平最大边 0.6~0.9 米，
    /// 唯一的逼近者是骑手的坐骑（身长约 2.4 米），留 0.6 米余量。
    /// NeedClips 只判四条：出手动作按兵种二选一（近战 attack_melee / 弓手 attack_shoot），
    /// 要求一个模型同时带两条攻击动作没有意义。
    /// Models 留空 ＝ 审目录内全部 .glb —— M2/M3 每加一个模型不必回来改这里。
    /// </summary>
    public ReviewContract Review() => new(
        ModelsDir: "src/Prototypes/BattleArray/Models",
        Models: Array.Empty<string>(),
        Clips: Array.Empty<string>(),
        MaxFootprint: 3.0,
        NeedClips: new[] { "idle", "walk", "run", "death" },
        // 循环的是哪几条：与动作库 clips_humanoid.gd 里 "loop": true 的那三条一致。
        // 游戏侧不靠这张表（ModelAnimator 按「基动作」定循环），体检单靠它决定胶卷的半开区间。
        LoopClips: new[] { "idle", "walk", "run" },
        AnchorName: "Anchor",
        ReportPath: "docs/prototypes/battle-array/model-review.md");

    /// <summary>
    /// 技能体检单契约（P5，2026-09-28）：16 条技能报什么、平A 节奏报什么、数字从哪两张真表现取。
    /// 内容全在 <c>BattleArraySkills.cs</c>，公共层那侧（<c>src/Tools/Skill.cs</c>）搜不到一个技能名。
    /// </summary>
    public SkillContract? Skills() => BattleArraySkills.Build();

    public int RunVerb(string verb, string[] argv)
    {
        var root = argv.Length > 2 ? argv[2] : "";
        var extra = argv.Length > 3 ? argv[3] : null;
        return verb switch
        {
            "gen" when argv.Length >= 3 => Gen.Run(root),
            "check" when argv.Length >= 3 => Check.Run(root, extra),
            "preview" when argv.Length >= 3 => GenPreview.Run(root, extra),
            // H3 离线重放取证（D4/D8 的 CLI 形状）；三钩子吃 Scripts/Logic/DuelRiftSetup 那份装配
            "replay" when argv.Length >= 3 => DuelReplay.Run(argv),
            _ => -1,
        };
    }
}
