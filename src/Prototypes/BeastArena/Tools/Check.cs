// 巨兽竞技场 造型源场景离线校验器 —— 契约写在这里，解析与几何检查引擎在 src/Tools/TscnCheck.cs。
// 退出码非 0 即不通过，可串进验收流程。
//
// 这里是这批造型里**唯一**的一条尺寸契约，数值与 Tools/BeastArenaTools.cs 的体检单契约同源同值：
//   · 巨兽（BEAST_WALK）是这批造型里水平最大的一件。2026-09-30 放大一档：实测占地 26.5 × 45.0 米、
//     剪影高 23.0 米、肩离地 12 米。放大前是 6.03 × 19.65 米 / 高 10.64 米 —— 那个尺度的驮台
//     只有 5.60 × 9.60 米，装不下逻辑层 9 米半径的三条航道（见 Gen.cs 的 BuildBeast 段）。
//   · 其余各件都在 7 米量级，于是**一条上限套得住全部**，不必像无限领主（街区 12 米）与
//     排兵布阵（兵种 3 米）那样分目录给两套契约。
//   · 校验器这条契约是整米（体检单契约那边是 double），两处同值这一个数：48。
//
// 十三件建筑另有一层更严的档位口径（4×4 ≤ 3.8 / 3×3 ≤ 2.8 / 2×2 ≤ 1.8 米，清单见
// spec 6 节「二十七个真模型清单」第 1 条）。**那一层本文件不判** —— 引擎那边只有一条上限，
// 多档位要改的是公共层 src/Tools/TscnCheck.cs，不在本原型的地盘内；实际占地读下面输出的
// 「占地 W×D」那一栏对着档位核，建造尺寸由 Gen.cs 各 Build 自己守住。
//
// 认哪几个文件：FootprintPattern 只匹配 Units/ 与 Town/ 与 Props/ 三个子目录 ——
// Rigs/ 根下的 _Preview*.tscn 是预览舞台（带一块 300×300 的地面），不进占地判定。

#nullable enable
using System;
using DemoFactory.Tools;

namespace DemoFactory.Tools.BeastArena;

public static class Check
{
    /// <summary>
    /// 巨兽（含吻；尾是短尾桩）的水平最大边（米）。体量定档 2026-10-15 整体放大一倍后实测 89.58（放大前 45.0）。
    /// </summary>
    private const double BeastWalk = 90.0;

    /// <summary>占地上限（米）。体检单契约那边同一个数，见 BeastArenaTools.Review() 的 MaxFootprint。</summary>
    private const int MaxFootprint = 120;

    public static int Run(string root, string? dumpFile)
    {
        if (string.IsNullOrEmpty(root))
            throw new ArgumentException("usage: dotnet run --project src/Tools -- check BeastArena <project-root> [单个 .tscn 转储逐件包围盒]");
        Console.WriteLine($"占地上限 {MaxFootprint} 米（巨兽实测 {BeastWalk:0.0} 米，留 {MaxFootprint - BeastWalk:0.0} 米余量）");
        var r = TscnCheck.RunCheck(new TscnCheck.Options(
            Root: root,
            Dir: "src/Prototypes/BeastArena/Tools/Rigs",
            MaxFootprint: MaxFootprint,
            // 只匹配这三个子目录 —— Rigs/ 根下的 _Preview*.tscn 是预览舞台（带一块 300×300 的地面），
            // 不进占地判定。Props/ 是「静态场景件与点位物」，同样要占地口径。
            FootprintPattern: @"\/(Units|Town|Props)\/",
            // 预制件根下的 Marker3D：.x = 头顶血条宽度，.y = 名牌高度（表现层读这两个数，不再硬编码）
            AnchorName: "Anchor",
            Touch: 0.05, // 判定"贴在一起"的容差 —— 灰盒部件靠重叠拼接
            RegenHint: "跑 dotnet run --project src/Tools -- gen BeastArena . 回滚",
            DumpFile: dumpFile,
            IntentionalGaps:
            [
                // 磁悬浮那道缝**不再需要登记**（2026-10-15 拆成驮台与悬浮盘之后）：
                // 缝从「一件造型内部靠登记豁免的空」变成「两件模型之间本来就空着的空间」——
                // 驮台下半底面 y=0、盘底面也是 y=0，两件各自都是落地件。
                // 原来那一条登记的是 LevitationPad.tscn 内部发射头（4.20）与盘底（9.20）之间那 1.30 米，
                // 盘是浮在自家裙座上的。拆开后盘不再自带裙座，也就不存在自家悬空件。
                // 飞艇（2026-10-14 造，2026-10-15 体量放大跟着 ×2）仍然是整条件挂在天上、
                // 由表现层摆到多高，悬空是这件造型全部的意思，与磁悬浮同一族，所以仍然登记。
                // 这两个数与 Gen.cs 的 BuildAirship 里 gondola/keel 那一段同源，
                // 并且跟 `Gen.Fits` 里空艇那一档（×2）同源 —— 改放大倍数要改这一处。
                (@"Airship\.tscn$", 0.00, 2.80),
            ]));
        return r.Bad > 0 ? 1 : 0;
    }
}
