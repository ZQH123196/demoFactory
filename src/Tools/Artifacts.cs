#nullable enable
using System;
using System.IO;
using System.Linq;

namespace DemoFactory.Tools;

/// <summary>
/// 开发期产物的根 —— 全仓唯一出处（工位与原型只准经由这里拿路径，别在各自文件里重复写字面量）。
///
/// 位置：<c>&lt;仓库根&gt;/.cache/</c>。选它有两个理由：
/// ① 这一树全是**可再生产物**（「资产可重跑」 那条口径：拿到仓库 + 同一个 Godot 就能重跑），
///   占在根目录上会让人误以为是产出物，也白占文件树的位置；
/// ② <c>.gitignore</c> 里 <c>**/.cache/</c> 一条本来就在（研究探针的 obj/bin 走的是同一条），
///   产物根并进去之后不必再单开一条忽略规则。
///
/// 目录形状不变，只是换了根：<c>.cache/Review/&lt;原型&gt;/&lt;模型&gt;/</c>（「目录一层到底」，《模型与动画准则》「实现分层与聚合」节）、
/// <c>.cache/Skill/&lt;原型&gt;/</c>、<c>.cache/Movie/</c>（<c>--write-movie</c> 出帧）。
///
/// <c>.gdignore</c> 那枚标记照旧顺手创建（《模型与动画准则》「产物与留痕」节）。如实记一句：Godot 的导入器本来就跳过以
/// <c>.</c> 开头的目录，所以搬进 <c>.cache/</c> 之后它已经不咬人了 —— 留着是防有人把产物根
/// 指到别处（显式给一个非点开头的目录）时又被当资产导入。
/// </summary>
public static class Artifacts
{
    /// <summary>产物根相对仓库根的那一段。</summary>
    public const string Root = ".cache";

    /// <summary>拼一个产物目录：<c>Dir(repoRoot, "Review", "&lt;原型&gt;")</c> → <c>&lt;根&gt;/.cache/Review/&lt;原型&gt;</c>。</summary>
    public static string Dir(string repoRoot, params string[] parts)
        => Path.Combine(new[] { repoRoot, Root }.Concat(parts).ToArray());

    /// <summary>建目录（含父级）并在产物根补一枚 <c>.gdignore</c>；返回建好的目录。</summary>
    public static string Ensure(string repoRoot, params string[] parts)
    {
        var dir = Dir(repoRoot, parts);
        Directory.CreateDirectory(dir);
        var marker = Path.Combine(repoRoot, Root, ".gdignore");
        if (!File.Exists(marker)) File.WriteAllText(marker, "");
        return dir;
    }

    /// <summary>
    /// 给人看的那一句相对路径（控制台与报告里用它，别各处手拼字符串）。
    /// 一律正斜杠：报告是要提交进版本库比对的，不能让 Windows 的反斜杠混进去（Path.Combine 在 Windows 上给的是 `\`）。
    /// </summary>
    public static string Show(params string[] parts)
        => Path.Combine(new[] { Root }.Concat(parts).ToArray()).Replace('\\', '/');
}
