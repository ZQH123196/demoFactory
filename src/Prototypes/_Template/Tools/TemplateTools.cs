// 新原型的开发期工具注册骨架 —— 抄这个文件，改标 ★ 的三处，工具链就整条接上了：
//   gen / check / preview（造型线，本骨架还没有）+ review（模型体检单，公共层自带）。
//
// 机制：src/Tools/DemoFactory.Tools.csproj 按 `Prototypes\*\Tools\*.cs` 通配源码链接本文件，
// src/Tools/Program.cs 反射发现 IPrototypeTools 的实现 —— 所以接新原型不需要改公共层一行。

#nullable enable
using System;

namespace DemoFactory.Tools.Template;

public sealed class TemplateTools : IPrototypeTools
{
    // ★ 1：与 src/Prototypes/<目录名> 一致；命令行别名按规范化匹配（大小写与 '-' 无关）
    public string DirName => "_Template";

    // ★ 2：体检单契约。审哪些、怎么算合格属于原型，公共层一个默认值都不给 —— 留 0 / 空数组就是「不判」
    public ReviewContract Review() => new(
        ModelsDir: "src/Prototypes/_Template/Models",
        Models: Array.Empty<string>(),          // 空 = 目录内全部 .glb
        Clips: Array.Empty<string>(),           // 空 = 每个模型自带的全部动作
        MaxFootprint: 0,                        // 占地上限（米），0 = 不判
        NeedClips: Array.Empty<string>(),       // 该有哪些动作，只管带骨架的模型
        LoopClips: Array.Empty<string>(),       // 哪些 clip 是姿势的循环（胶卷半开区间采样）
        AnchorName: "Anchor",
        ReportPath: "docs/prototypes/_template/model-review.md");

    // ★ 3：原型自有动词。造型线（Gen/Check/GenPreview）从上一个原型抄；不做的动词返回 -1
    public int RunVerb(string verb, string[] argv) => -1;
}
