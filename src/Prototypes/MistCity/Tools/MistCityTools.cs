// 迷雾之都的开发期工具注册 —— 照 _Template/Tools/TemplateTools.cs 抄，标 ★ 的三处已改。
// 机制：src/Tools/DemoFactory.Tools.csproj 按 `Prototypes\*\Tools\*.cs` 通配源码链接本文件，
// src/Tools/Program.cs 反射发现 IPrototypeTools 的实现 —— 所以接新原型不需要改公共层一行。
//
// 造型线（gen / check / preview）本原型不做：灰盒阶段不要求画面（spec「Out of Scope」），
// RunVerb 因此对造型线那三条一律回 -1。

#nullable enable
using System;

namespace DemoFactory.Tools.MistCity;

public sealed class MistCityTools : IPrototypeTools
{
    // ★ 1：与 src/Prototypes/<目录名> 一致；命令行别名按规范化匹配（大小写与 '-' 无关）
    public string DirName => "MistCity";

    // ★ 2：体检单契约。Models 与 Clips 都空 = 目录内没有 .glb、没有带骨架的模型
    // （牌用什么呈现记在 docs/prototypes/mist-city/design.md 的待确认区，尚未定）。
    // ModelsDir 按约定落在 src/Prototypes/MistCity/Models。
    public ReviewContract Review() => new(
        ModelsDir: "src/Prototypes/MistCity/Models",
        Models: Array.Empty<string>(),          // 空 = 目录内全部 .glb
        Clips: Array.Empty<string>(),           // 空 = 每个模型自带的全部动作
        MaxFootprint: 0,                        // 占地上限（米），0 = 不判
        NeedClips: Array.Empty<string>(),       // 该有哪些动作，只管带骨架的模型
        LoopClips: Array.Empty<string>(),       // 哪些 clip 是姿势的循环（胶卷半开区间采样）
        AnchorName: "Anchor",
        ReportPath: "docs/prototypes/mist-city/model-review.md");

    // ★ 3：原型自有动词。造型线本轮不做，一律回 -1。
    public int RunVerb(string verb, string[] argv) => -1;
}
