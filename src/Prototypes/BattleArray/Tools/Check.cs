// 排兵布阵 造型源场景离线校验器 —— 契约写在这里，解析与几何检查引擎在 src/Tools/TscnCheck.cs。
// 退出码非 0 即不通过，可串进验收流程。
//
// 与无限领主那条的差别：那边判的是建筑（街区契约 12 米、匹配 /Buildings/），
// 这边判的是**兵种单位能不能站进自己的格**——格边长 2.0 米（design.md 5.1 节），
// 上限给到 3.0 米是因为唯一的逼近者是骑手的坐骑（身长约 2.4 米），留 0.6 米余量。
// 同一条 3.0 也写在 BattleArrayTools.cs 的体检单契约里，两处一起改。

#nullable enable
using System;
using DemoFactory.Tools;

namespace DemoFactory.Tools.BattleArray;

public static class Check
{
    public static int Run(string root, string? dumpFile)
    {
        if (string.IsNullOrEmpty(root))
            throw new ArgumentException("usage: dotnet run --project src/Tools -- check BattleArray <project-root> [单个 .tscn 转储逐件包围盒]");
        var r = TscnCheck.RunCheck(new TscnCheck.Options(
            Root: root,
            Dir: "src/Prototypes/BattleArray/Tools/Rigs",
            MaxFootprint: 3, // 校验器这条契约是整米（体检单契约那边是 double，两处同值 3.0 米）
            FootprintPattern: @"\/Units\/",
            // 预制件根下的 Marker3D：.x = 头顶血条宽度，.y = 名牌高度（表现层读这两个数，不再硬编码）
            AnchorName: "Anchor",
            Touch: 0.05, // 判定"贴在一起"的容差 —— 灰盒部件靠重叠拼接
            RegenHint: "跑 dotnet run --project src/Tools -- gen BattleArray . 回滚",
            DumpFile: dumpFile));
        return r.Bad > 0 ? 1 : 0;
    }
}
