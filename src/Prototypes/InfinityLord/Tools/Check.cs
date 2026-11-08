// 无限领主 预制件离线校验器 —— 契约写在这里，解析与几何检查引擎在
// src/Tools/TscnCheck.cs。退出码非 0 即不通过，可串进验收流程。
// （2026-09-23 自 check.mjs 移植；Node 原版已删，史实见 git log）

#nullable enable
using System;
using DemoFactory.Tools;

namespace DemoFactory.Tools.InfinityLord;

public static class Check
{
    public static int Run(string root, string? dumpFile)
    {
        if (string.IsNullOrEmpty(root))
            throw new ArgumentException("usage: dotnet run --project src/Tools -- check InfinityLord <project-root> [单个 .tscn 转储逐件包围盒]");
        var r = TscnCheck.RunCheck(new TscnCheck.Options(
            Root: root,
            Dir: "src/Prototypes/InfinityLord/Tools/Rigs",
            // 街区契约：LogicWorld.MinPlaceDistance 既是建筑中心间距，也是单座建筑的占地上限；
            // 各自 ≤12 ⇒ 任意两座不穿插，所以这条能离线判，不必跑关卡截图猜
            MaxFootprint: 12,
            FootprintPattern: @"\/Buildings\/",
            // 预制件根下的 Marker3D：.x = 头顶血条宽度，.y = 名牌高度（Main.cs 读这两个数，不再硬编码）
            AnchorName: "Anchor",
            Touch: 0.05, // 判定"贴在一起"的容差 —— 灰盒部件靠重叠拼接
            RegenHint: "跑 dotnet run --project src/Tools -- gen InfinityLord . 回滚",
            DumpFile: dumpFile));
        return r.Bad > 0 ? 1 : 0;
    }
}