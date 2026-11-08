#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using DemoFactory.GugaFramework.Guard;
using DemoFactory.GugaFramework.Tests;
using Shouldly;
using Xunit;

namespace BeastArena.Tests;

/// <summary>
/// 共享骨架的自动化检查 —— 读回 <c>.glb</c> 的 JSON 段，证明「同一 clip 能套到两个模型上」。
///
/// <para><b>怕什么</b>（「模型四件套」与规格「六套巨兽共用骨架动作组」）：居民四件、原生怪两件与巨兽三件
/// 各共用一副骨架（居民与原生怪是同一副人形骨架、两种动作组，T12 拆组）；哪天某件的骨名漂了一个字，动作库的轨道就在那件上静默落空
/// （导出器只对不存在的骨名 push_error，整轮照样退 0 —— 见 <see cref="AssetSourceGuardTests"/> 那一族坑）。</para>
///
/// <para><b>判据</b>：同组模型的蒙皮关节名数组**逐字相同**（骨名即蒙皮绑定，接口清单「骨架事实」）；
/// 每条 clip 的轨道目标骨都落在组内**每一个**模型的关节名集合里 —— 这才是「clip 可跨模型复用」的机器说法。
/// clip 的时长与最大骨偏移数字走引擎工位（本文件不起引擎），留痕在
/// <c>.cache/beast-arena-vs/T09/action-reuse.md</c>。</para>
///
/// <para>标签：快速测试（纯文件解析，不起引擎、不连数据库）。</para>
/// </summary>
public class SharedSkeletonTests
{
    // T12 起拆成两组：居民四件带八条（idle/walk/run/attack_melee/hit/death/work/carry），
    // 原生怪两件带七条（idle/walk/run/attack_melee/hit/death/emerge）—— clip 名单按组内一致判，
    // 骨架是同一副 Humanoid.Rig，关节名七件逐字相同照旧全体一起判。
    private static readonly string[] ResidentGroup = ["Citizen", "Gladiator", "Helper", "Worker"];
    private static readonly string[] MonsterGroup = ["Monster", "MonsterBurrower"];
    private static readonly string[] BeastGroup = ["Beast", "BeastDesert", "BeastGlutton"];
    private static string[] HumanoidGroup => [.. ResidentGroup, .. MonsterGroup];

    private static string ModelPath(string name) =>
        Path.Combine(TestPaths.RepoRoot, "src", "Prototypes", "BeastArena", "Models", name + ".glb");

    /// <summary>glb 的 JSON 段：12 字节文件头 + 8 字节块头（块长在 [12:16]、块型在 [16:20]）+ JSON。</summary>
    private static JsonDocument ReadGlbJson(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Encoding.ASCII.GetString(bytes, 0, 4).ShouldBe("glTF", $"{path} 不是 glb");
        var jsonLen = BitConverter.ToUInt32(bytes, 12);
        Encoding.ASCII.GetString(bytes, 16, 4).ShouldBe("JSON", $"{path} 第一块不是 JSON");
        return JsonDocument.Parse(Encoding.UTF8.GetString(bytes, 20, (int)jsonLen));
    }

    /// <summary>蒙皮关节名数组（按 joints 序）。取带 mesh 且带 skin 的那个节点，与 model_metrics 认 Body 同口径。</summary>
    private static string[] JointsOf(JsonDocument doc, string model)
    {
        var root = doc.RootElement;
        var nodes = root.GetProperty("nodes");
        var skins = root.GetProperty("skins");
        var skinIdx = -1;
        foreach (var n in nodes.EnumerateArray())
            if (n.TryGetProperty("mesh", out _) && n.TryGetProperty("skin", out var s))
                skinIdx = s.GetInt32();
        skinIdx.ShouldBeGreaterThan(-1, $"{model} 里没有带蒙皮的节点 —— 四样齐备缺了蒙皮");
        var joints = skins[skinIdx].GetProperty("joints");
        return joints.EnumerateArray().Select(i => nodes[i.GetInt32()].GetProperty("name").GetString()!).ToArray();
    }

    /// <summary>每条 clip（animation）的轨道目标骨名集合；clip 名取 animation.name。</summary>
    private static Dictionary<string, HashSet<string>> ClipTargetsOf(JsonDocument doc)
    {
        var root = doc.RootElement;
        var nodes = root.GetProperty("nodes");
        var outMap = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var anim in root.GetProperty("animations").EnumerateArray())
        {
            var name = anim.TryGetProperty("name", out var nm) ? nm.GetString()! : "(无名)";
            var targets = anim.GetProperty("channels").EnumerateArray()
                .Select(ch => nodes[ch.GetProperty("target").GetProperty("node").GetInt32()]
                    .GetProperty("name").GetString()!)
                .ToHashSet(StringComparer.Ordinal);
            outMap[name] = targets;
        }
        return outMap;
    }

    private static (string[] Joints, Dictionary<string, HashSet<string>> Clips) Read(string model)
    {
        using var doc = ReadGlbJson(ModelPath(model));
        return (JointsOf(doc, model), ClipTargetsOf(doc));
    }

    [Fact]
    public void 人形七件居民与原生怪共用一副骨架_关节名逐字相同()
    {
        var baseline = Read(HumanoidGroup[0]);
        for (var i = 1; i < HumanoidGroup.Length; i++)
        {
            var other = Read(HumanoidGroup[i]);
            string.Join(",", other.Joints)
                .ShouldBe(string.Join(",", baseline.Joints),
                    $"{HumanoidGroup[i]} 与 {HumanoidGroup[0]} 的人形骨名不一致 —— 动作轨道会在那件上静默落空");
        }
        TestLog.Ledger($"人形组关节名（{baseline.Joints.Length} 根，七件一致）：{string.Join(" ", baseline.Joints)}");
    }

    [Fact]
    public void 巨兽三件共用一副四足骨架_关节名逐字相同()
    {
        var baseline = Read(BeastGroup[0]);
        for (var i = 1; i < BeastGroup.Length; i++)
        {
            var other = Read(BeastGroup[i]);
            string.Join(",", other.Joints)
                .ShouldBe(string.Join(",", baseline.Joints),
                    $"{BeastGroup[i]} 与 {BeastGroup[0]} 的四足骨名不一致 —— clips_beast.gd 的轨道会在那件上静默落空");
        }
        baseline.Joints.ShouldAllBe(b => b.StartsWith("m_", StringComparison.Ordinal),
            "四足骨名应全部带 m_ 前缀（与骑手人形骨防撞名，见 clips_beast.gd 文件头）");
        TestLog.Ledger($"巨兽组关节名（{baseline.Joints.Length} 根，三件一致）：{string.Join(" ", baseline.Joints)}");
    }

    [Fact]
    public void 同组每条clip的目标骨都落在组内每个模型的骨架里()
    {
        foreach (var group in new[] { ResidentGroup, MonsterGroup, BeastGroup })
        {
            var reads = group.Select(Read).ToArray();
            var clipNames = reads.SelectMany(r => r.Clips.Keys).Distinct().OrderBy(x => x, StringComparer.Ordinal);
            foreach (var clip in clipNames)
            {
                var carrier = reads.First(r => r.Clips.ContainsKey(clip));
                foreach (var (model, read) in group.Zip(reads))
                {
                    if (!read.Clips.ContainsKey(clip)) continue; // 「那件没带这条 clip」由体检单 need_clips 判，这里只管套得上去
                    var orphans = carrier.Clips[clip].Where(b => !read.Joints.Contains(b)).ToArray();
                    orphans.Length.ShouldBe(0,
                        $"{clip} 的轨道骨 {string.Join(",", orphans)} 不在 {model} 的骨架里 —— 同一 clip 套不上去");
                }
            }
            // 组内 clip 名单也要一致，否则「共用动作组」名存实亡
            static string KeyList((string[] Joints, Dictionary<string, HashSet<string>> Clips) r) =>
                string.Join(",", r.Clips.Keys.OrderBy(x => x, StringComparer.Ordinal));
            var names0 = KeyList(reads[0]);
            for (var i = 1; i < reads.Length; i++)
                KeyList(reads[i]).ShouldBe(names0,
                    $"{group[i]} 的 clip 名单与 {group[0]} 不一致");
        }
    }

    [Fact]
    public void 两副骨架的骨名互不重叠()
    {
        // m_ 前缀防撞名的机器判据：人形组与巨兽组的骨名交集必须为空，
        // 否则「骑乘双骨架」那一族串轨坑（《模型与动画准则》18 节）会回来。
        var human = Read(HumanoidGroup[0]).Joints;
        var beast = Read(BeastGroup[0]).Joints;
        human.Intersect(beast, StringComparer.Ordinal).ShouldBeEmpty();
    }
}
