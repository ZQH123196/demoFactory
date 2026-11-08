// 引擎侧 facts 文件的唯一读取件 —— 「一份数字，两个工位共用」（《模型与动画准则》「实现分层与聚合」节）。
//
// 为什么单独一层：`K=V` 行式的 facts 原本是模型体检单（Review.cs）自己解析的。技能体检单要读的是
// **同一批数字**（每条 clip 的名义时长就躺在同一份文件里），再抄一个解析器就成了两套读法各自漂移 ——
// 与「校验器与报告共用一次计算」是同一条理由。所以现在两边都调这里的 `Facts.Read`。
//
// 文件形状（由引擎侧写）：`MODEL=<模型名>` 起一段，段内一行一个 `K=V`；
// 段前的行算头部（SIZE / ENGINE / WINDOW / SCALE / SCALE_TOP）。
// 重复键里 `TIMES` 按行累积（一个模型多条 clip），其余后进的覆盖先到的。
//
// 确定性：读进来就按模型名排序 —— 报告要「同输入重跑逐字全等」，输入顺序不该有影响。

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace DemoFactory.Tools;

public sealed class FactsModel
{
    public string Name = "";
    public readonly Dictionary<string, string> Kv = new(StringComparer.Ordinal);

    /// <summary>一个模型有多条 clip 就有多行 TIMES —— 重复键必须累积，用字典存会只剩最后一条。</summary>
    public readonly List<string> Times = new();

    public string Verdict = "PASS";
    public List<string> ViewFiles => KvList("VIEWS");
    public List<string> FilmFiles => KvList("FILMS");

    /// <summary>每条 clip 让骨骼离开绑定姿势的最大距离（米）。</summary>
    public Dictionary<string, double> Motion => Facts.Pairs(Kv.GetValueOrDefault("MOTION", ""));

    /// <summary>每条 clip 的<b>名义时长（秒）</b> —— 技能体检单的前摇对表就读这一行。</summary>
    public Dictionary<string, double> Lengths => Facts.Pairs(Kv.GetValueOrDefault("LENGTHS", ""));

    public List<string> KvList(string key) => Facts.Split(Kv.GetValueOrDefault(key, ""));
}

public sealed class FactsReport
{
    public readonly Dictionary<string, string> Header = new(StringComparer.Ordinal);
    public readonly List<FactsModel> Models = new();

    public FactsModel? Model(string name) => Models.FirstOrDefault(m => m.Name == name);
}

public static class Facts
{
    public static FactsReport Read(string file)
    {
        var r = new FactsReport();
        FactsModel? cur = null;
        foreach (var raw in File.ReadAllLines(file))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            var i = line.IndexOf('=');
            if (i < 0) continue;
            var k = line[..i];
            var v = line[(i + 1)..];
            if (k == "MODEL")
            {
                cur = new FactsModel { Name = v };
                r.Models.Add(cur);
                continue;
            }
            if (cur == null) r.Header[k] = v;
            else if (k == "TIMES") cur.Times.Add(v);
            else cur.Kv[k] = v;
        }
        foreach (var m in r.Models) m.Verdict = m.Kv.GetValueOrDefault("VERDICT", "PASS");
        r.Models.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return r;
    }

    public static List<string> Split(string s)
    {
        var list = s.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim())
            .Where(p => p.Length > 0).ToList();
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    public static Dictionary<string, double> Pairs(string s)
    {
        var d = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var p in s.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = p.IndexOf(':');
            if (i > 0 && double.TryParse(p[(i + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                d[p[..i]] = v;
        }
        return d;
    }
}
