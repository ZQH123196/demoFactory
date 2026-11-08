// 无限领主 — 开发期工具注册入口。
//
// 这个类只做两件事：报出自己的目录名，把动词转给本原型 Tools/ 下那几个薄 spec
// （Gen / Check / GenPreview 造型线，Replay 取证线），以及给出一份体检单契约。
// 通用编排在 src/Tools/（Program.cs / Review.cs / Tscn.cs / PreviewStage.cs），这里一行都不复制。

#nullable enable
using System;

namespace DemoFactory.Tools.InfinityLord;

public sealed class InfinityLordTools : IPrototypeTools
{
    public string DirName => "InfinityLord";

    /// <summary>
    /// 模型体检单契约（《模型与动画准则》「实现分层与聚合」 第 4 条：清单与契约属于原型，介质参数属于公共层）。
    /// 占地 12 米沿用 Check.cs 的街区契约（LogicWorld.MinPlaceDistance）；
    /// 动作齐不齐只判带骨架的模型（建筑/道具无骨架是准则允许的）。
    /// </summary>
    public ReviewContract Review() => new(
        ModelsDir: "src/Prototypes/InfinityLord/Models",
        Models: Array.Empty<string>(),
        Clips: Array.Empty<string>(),
        MaxFootprint: 12,
        NeedClips: new[] { "idle", "walk", "run", "death" },
        // 循环的是哪几条：与动作库 clips_humanoid.gd 里 "loop": true 的那三条一致。
        // 游戏侧不靠这张表（ModelAnimator 按「基动作」定循环），体检单靠它决定半开区间。
        LoopClips: new[] { "idle", "walk", "run" },
        AnchorName: "Anchor",
        ReportPath: "docs/prototypes/infinity-lord/model-review.md");

    public int RunVerb(string verb, string[] argv)
    {
        var root = argv.Length > 2 ? argv[2] : "";
        var extra = argv.Length > 3 ? argv[3] : null;
        return verb switch
        {
            "gen" when argv.Length >= 3 => Gen.Run(root),
            "check" when argv.Length >= 3 => Check.Run(root, extra),
            "preview" when argv.Length >= 3 => GenPreview.Run(root, extra),
            "replay" => Replay.Run(argv),
            _ => -1,
        };
    }
}
