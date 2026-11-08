#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DemoFactory.GugaFramework.Entities;
using DemoFactory.GugaFramework.Persistence;
using Shouldly;
using Xunit;

namespace DemoFactory.GugaFramework.Tests;

/// <summary>
/// 实体层的判红线 —— 逐行落 spec 5 节那张验收表。
///
/// <para><b>为什么这一份能离线判红</b>：册子是一个字都不碰引擎的纯 C# 件 ——
/// 发号、登记、摘册、次序、事件、快照、存档、读数八项全是纯函数与计数器。
/// 表现层绑定（模型、悬停、输入）不在这一份里，那一档走真机实测（「测试与实测分工」）。</para>
///
/// <para>用例名里带 spec 5 节那一行的原词，是为了让「这一条判据还有没有钉着」
/// 变成一件 grep 就能答的事，而不是一次考古。</para>
/// </summary>
public sealed class EntityLedgerTests
{
    // === 判据本身：「在场的事全在一本册子里」 ===

    [Fact]
    public void 判据本身_册子里一条只有四格_血量阵营位置都不在册内()
    {
        var cells = typeof(EntityRecord)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Csv(cells).ShouldBe("Id,Kind,Payload,Seq",
            "四格就是四格：血量、阵营、位置一格都不许出现（D2、D3、D4）——"
            + "硬抽血量到一处要动建造层与技能层两个已交付模块，seam 会漏");
    }

    [Fact]
    public void 判据本身_层内八项在册子内()
    {
        var ledger = new EntityCore();
        ledger.Spawn("u1", "unit");
        ledger.Spawn("u2", "unit");
        ledger.Remove("u1");

        // ① 发号 ② 登记 ③ 摘册 ④ 次序 ⑤ 事件 ⑥ 快照 ⑦ 存档 ⑧ 读数，八项各那一发都在。
        ledger.Issued.ShouldBe(2, "①发号：两次 Spawn 发了两个序号");
        ledger.Contains("u2").ShouldBeTrue("②登记：查得到");
        ledger.Contains("u1").ShouldBeFalse("③摘册：摘掉的那一条不在册");
        Csv(ledger.Ids).ShouldBe("u2", "④次序：摘册不挪别人的位");
        ledger.Events.Total.ShouldBe(3, "⑤事件：生成两条 + 摘册一条");
        ledger.DebugSnapshotSegment().ShouldStartWith("entity ", Case.Sensitive, "⑥快照：段首是 entity（D15）");
        Write(ledger).ShouldContain("\"rows\"", Case.Sensitive, "⑦存档：写得出那一段");
        ledger.Readout().ShouldContain("在册 1 条", Case.Sensitive, "⑧读数：答得出在场几条");
    }

    // === id 子约束：任一 id 全局只发一次 ===

    [Fact]
    public void id子约束_同一局内两次Spawn不撞号_序号单调()
    {
        var ledger = new EntityCore();
        var a = ledger.Spawn("p01", "unit");
        var b = ledger.Spawn("p02", "unit");
        var c = ledger.Spawn("p03", "unit");

        string.Join(",", new[] { a.Seq, b.Seq, c.Seq }).ShouldBe("1,2,3",
            "册子发的那一格单调递增（D8）");
        ledger.NextSeq.ShouldBe(4, "下一个序号接得上");
    }

    [Fact]
    public void id子约束_发过的id再Spawn当场抛()
    {
        var ledger = new EntityCore();
        ledger.Spawn("p01", "unit");
        ledger.Remove("p01");

        var again = Should.Throw<InvalidOperationException>(() => ledger.Spawn("p01", "unit"));
        again.Message.ShouldContain("永不复发", Case.Sensitive, "1 节子约束：id 唯一且永不复发，摘了册也不许再发");
    }

    [Fact]
    public void id子约束_读档恢复后序号接得上()
    {
        var source = new EntityCore();
        source.Spawn("u1", "unit");
        source.Spawn("u2", "unit");
        source.Spawn("u3", "unit");
        source.Remove("u2");

        var loaded = Load(Write(source));
        loaded.NextSeq.ShouldBe(4, "读档之后下一个序号接在最大那个后面，不从 1 重来");
        loaded.Spawn("u4", "unit").Seq.ShouldBe(4, "新发的那一格接得上");
    }

    [Fact]
    public void id子约束_读档恢复后发过的id不能发第二次()
    {
        var source = new EntityCore();
        source.Spawn("u1", "unit");
        source.Spawn("u2", "unit");
        source.Remove("u2");   // u2 发过、已摘册 —— 它的 id 仍然占着

        var loaded = Load(Write(source));
        Should.Throw<InvalidOperationException>(() => loaded.Spawn("u2", "unit"));
        loaded.IssuedIds.ShouldBe(2, "「发过」这件事要活过读档：1 节子约束那句「含读档恢复」就管这一格");
    }

    [Fact]
    public void id子约束_复登走Register不发新序号()
    {
        var ledger = new EntityCore();
        ledger.Spawn("u1", "unit");
        ledger.Spawn("u2", "unit");
        ledger.Remove("u1");
        var again = ledger.Register("u1", "unit");

        again.Seq.ShouldBe(1, "复登不发新序号（D9）：它那一格序号是首次登记时发的");
        ledger.Issued.ShouldBe(2, "发过的序号还是两个，复登不占第三个");
    }

    [Fact]
    public void id子约束_没发过的id不能复登()
    {
        var ledger = new EntityCore();
        Should.Throw<InvalidOperationException>(() => ledger.Register("u1", "unit"))
            .Message.ShouldContain("Spawn", Case.Sensitive, "第一次上册走 Spawn（那一条才发序号）");
    }

    [Fact]
    public void id子约束_还在册的不能复登()
    {
        var ledger = new EntityCore();
        ledger.Spawn("u1", "unit");
        Should.Throw<InvalidOperationException>(() => ledger.Register("u1", "unit"))
            .Message.ShouldContain("重复登记", Case.Sensitive, "一条在场的东西不需要再上一次册");
    }

    [Fact]
    public void id子约束_空白id与空白kind当场抛()
    {
        var ledger = new EntityCore();
        Should.Throw<ArgumentException>(() => ledger.Spawn("", "unit"));
        Should.Throw<ArgumentException>(() => ledger.Spawn("u1", ""));
    }

    // === 一次生成 ===

    [Fact]
    public void 一次生成_一发Spawn之后册子里有一条且事件已发()
    {
        var ledger = new EntityCore();
        var payload = new object();
        var record = ledger.Spawn("p01", "unit", payload);

        ledger.Count.ShouldBe(1, "册子里有一条");
        ledger.Find("p01").ShouldBeSameAs(record, "查得到同一条");
        ReferenceEquals(record.Payload, payload).ShouldBeTrue("载荷原样存着（D7：存而不读）");
        Csv(ledger.Ids).ShouldBe("p01", "次序就是登记次序");
        ledger.Events.Count<EntitySpawned>().ShouldBe(1, "事件已发");
        ledger.Events.All[0].Line.ShouldBe("0 spawned p01 seq=1 kind=unit", 
            "事件行逐字可读：步号、形状、id、序号、kind");
    }

    [Fact]
    public void 次序_复登回原位这条规矩跨读档也成立()
    {
        var source = new EntityCore();
        source.Spawn("u1", "unit");
        source.Spawn("u2", "unit");
        source.Spawn("u3", "unit");
        source.Remove("u2");   // u2 发过、已摘册：它在档里是死槽

        var loaded = Load(Write(source));
        loaded.Register("u2", "unit");

        Csv(loaded.Ids).ShouldBe("u1,u2,u3",
            "1 节子约束那句「含读档恢复」管的就是这一格：死槽带着 id 与序号一起进档，"
            + "所以复登回得来它首次登记的那一格（D9、D14）");
        loaded.Find("u2")!.Seq.ShouldBe(2, "复登不发新序号：那仍是它首次登记发的那一格");
        loaded.Issued.ShouldBe(3, "读档之后发过的序号还是三个");
        loaded.Spawn("u4", "unit").Seq.ShouldBe(4, "下一个序号接得上");
    }

    [Fact]
    public void 存档_读档把留着的旧事件清掉总数不清()
    {
        var source = new EntityCore();
        source.Spawn("u1", "unit");
        source.Remove("u1");

        var loaded = Load(Write(source));
        loaded.Events.KeptCount.ShouldBe(0,
            "留着的话事件流里那些 spawned 念的是上一局的东西，读数上分不出哪一条属于这一局");
        loaded.Events.Total.ShouldBe(0, "一个干净的册子读档之后也该是干净的事件流");
    }

    // === 一次移除：四项缺一即红，次序照 D11 ===

    [Fact]
    public void 一次移除_四项各一次且次序照D11()
    {
        var ledger = new EntityCore(tick: () => 12);
        var exit = new RecordingRemoval(ledger);
        var wired = new EntityCore(host: null, removal: exit, tick: () => 12);
        var payload = new object();
        exit.Expected = payload;
        wired.Spawn("p01", "unit", payload);
        wired.Spawn("p02", "unit");

        var removed = wired.Remove("p01");

        removed.ShouldBeTrue("摘掉了");
        Csv(exit.Calls).ShouldBe("定身,腾引用", "②③ 两步各一次，且②在③前");
        Csv(exit.InLedgerWhenCalled.Select(v => v ? "在册" : "不在册")).ShouldBe("不在册,不在册",
            "①摘册在②之前：定身那一刻册子里已经查不到它了，否则退场还留一条在册的");
        Csv(exit.Handed).ShouldBe("p01:拿到载荷,p01:拿到载荷",
            "②③ 拿到的是那一条本身（含载荷），不是 id：它们跑在①之后，"
            + "回头按 id 查册子必然是 null，而 null 就是「静默不做事」——退场还在挪那一坑的形状");
        wired.Contains("p01").ShouldBeFalse("①摘册：册子里没有");
        wired.Find("p01").ShouldBeNull("查不到");
        wired.Events.Count<EntityRemoved>().ShouldBe(1, "④事件已发");
        wired.Events.All[^1].Line.ShouldBe("12 removed p01 seq=1 kind=unit",
            "④发事件放最后：订阅者看到这一条时三边都已落定");

        _ = ledger;   // 上面那个没接线的册子只是把「不接线」那一档也留在这一份里
    }

    [Fact]
    public void 一次移除_不递接线时那两步没发生且读数说得出()
    {
        var ledger = new EntityCore();
        ledger.Spawn("p01", "unit");
        ledger.Remove("p01");

        ledger.Readout().ShouldContain("移除接线 不接", Case.Sensitive,
            "静默跳过是最坏的一种：症状在好几步之后才冒出来，读数里读不出是这里漏了（D11）");
    }

    [Fact]
    public void 一次移除_摘一个不在册的不算错误()
    {
        var ledger = new EntityCore();
        ledger.Spawn("p01", "unit");
        ledger.Remove("p01").ShouldBeTrue();
        ledger.Remove("p01").ShouldBeFalse("摘一个不存在的东西没有话说，不抛");
        ledger.Events.Count<EntityRemoved>().ShouldBe(1, "第二次不发事件：没有东西被摘");
    }

    [Fact]
    public void 次序_摘册不挪次序且复登回原位()
    {
        var ledger = new EntityCore();
        ledger.Spawn("u1", "unit");
        ledger.Spawn("u2", "unit");
        ledger.Spawn("u3", "unit");
        ledger.Remove("u2");
        ledger.Remove("u1");

        Csv(ledger.Ids).ShouldBe("u3", "只剩一条");

        ledger.Register("u1", "unit");
        Csv(ledger.Ids).ShouldBe("u1,u3", "复登回它首次登记的那一格，不排在末尾（D14）");
        ledger.Register("u2", "unit");
        Csv(ledger.Ids).ShouldBe("u1,u2,u3",
            "两个都复登，次序回到登记时的样子：这是居民环路重建那一档（D9、D14）");
    }

    [Fact]
    public void 复登_事件是ReRegistered而不是Spawned()
    {
        var ledger = new EntityCore();
        ledger.Spawn("u1", "unit");
        ledger.Remove("u1");
        ledger.Register("u1", "unit");

        ledger.Events.Count<EntitySpawned>().ShouldBe(1, "复登不是生成");
        ledger.Events.Count<EntityReRegistered>().ShouldBe(1, "复登发的是那一个");
        ledger.Events.All[^1].Line.ShouldBe("0 reregistered u1 seq=1 kind=unit", 
            "明细里 seq=1 而不是 2：与 Spawned 的差别就在这一格上");
    }

    // === 「活着」是查询口不是真值 ===

    [Fact]
    public void 活的查询口_宿主答得对()
    {
        var world = new Dictionary<string, bool> { ["u1"] = true, ["u2"] = false };
        var ledger = new EntityCore(new MapHost(world));
        ledger.Spawn("u1", "unit");
        ledger.Spawn("u2", "unit");

        ledger.Alive("u1").ShouldBeTrue("宿主说活");
        ledger.Alive("u2").ShouldBeFalse("宿主说不活 —— 册子自己不持有血量（D2）");
        ledger.Alive("没登记过").ShouldBeFalse("不在册答 false：不在册与不活是两句不同的话");
    }

    [Fact]
    public void 活的查询口_不递宿主时全活()
    {
        var ledger = new EntityCore();
        ledger.Spawn("u1", "unit");
        ledger.Spawn("u2", "unit");

        ledger.Alive("u1").ShouldBeTrue("不递宿主 = 全活（照 IConstructionHost 的形状）");
        ledger.Alive("u2").ShouldBeTrue();
        ledger.Readout().ShouldContain("宿主 不接（全活）", Case.Sensitive, "接线状态读得出来");
    }

    // === 存档 ===

    [Fact]
    public void 存档_往返后Ids逐字相同且顺序相同()
    {
        var source = new EntityCore();
        source.Spawn("p01", "unit");
        source.Spawn("p02", "unit");
        source.Spawn("p03", "unit");
        source.Remove("p02");

        var loaded = Load(Write(source));
        Csv(loaded.Ids).ShouldBe(Csv(source.Ids), "在册 id 逐字相同、顺序相同（5 节存档行）");
        Csv(loaded.Records.Select(r => $"{r.Id}:{r.Seq}:{r.Kind}")).ShouldBe("p01:1:unit,p03:3:unit",
            "序号与 kind 也逐字对上");
    }

    [Fact]
    public void 存档_载荷原样还回()
    {
        var source = new EntityCore();
        var first = new object();
        var second = new object();
        source.Spawn("u1", "unit", first);
        source.Spawn("u2", "unit", second);

        // 载荷不进档（D7）：读档时按 id 从原型自己那份对回去。
        var byId = new Dictionary<string, object?> { ["u1"] = first, ["u2"] = second };
        var loaded = Load(Write(source), id => byId.TryGetValue(id, out var v) ? v : null);

        ReferenceEquals(loaded.Find("u1")!.Payload, first).ShouldBeTrue("载荷原样还回");
        ReferenceEquals(loaded.Find("u2")!.Payload, second).ShouldBeTrue();
    }

    [Fact]
    public void 存档_两串逐字相等()
    {
        var source = new EntityCore();
        source.Spawn("u1", "unit");
        source.Spawn("u2", "building");
        source.Remove("u1");

        var first = Write(source);
        var second = Write(Load(first));

        second.ShouldBe(first, 
            "WriteState → ReadState → WriteState 两串必须逐字相等："
            + "少一个字段立刻不等，多写一个派生数据读回来再写一次也不等");
    }

    [Fact]
    public void 存档_读不出时读数那一行还在()
    {
        var source = new EntityCore();
        source.Spawn("u1", "unit");
        source.Spawn("u2", "unit");
        source.Spawn("u3", "unit");
        source.Remove("u3");

        var loaded = Load(Write(source));
        loaded.IssuedIds.ShouldBe(3, "死槽那一行也在档里");
        loaded.Issued.ShouldBe(3, "最大序号就是发过的最后一个");
    }

    [Fact]
    public void 存档_档里同一个id出现两次当场抛()
    {
        var broken = "{\"rows\":[" +
                     "{\"id\":\"u1\",\"seq\":1,\"kind\":\"unit\",\"live\":true}," +
                     "{\"id\":\"u1\",\"seq\":2,\"kind\":\"unit\",\"live\":true}]}";

        Should.Throw<FormatException>(() => Load(broken))
            .Message.ShouldContain("两次", Case.Sensitive, "「id 永不复发」：档里出现两次就是这份档坏了");
    }

    [Fact]
    public void 存档_档里序号倒退当场抛()
    {
        var broken = "{\"rows\":[" +
                     "{\"id\":\"u1\",\"seq\":5,\"kind\":\"unit\",\"live\":true}," +
                     "{\"id\":\"u2\",\"seq\":2,\"kind\":\"unit\",\"live\":true}]}";

        Should.Throw<FormatException>(() => Load(broken))
            .Message.ShouldContain("严格递增", Case.Sensitive, "槽次序与序号必须一起严格递增（倒退与两行同号都不放过）");
    }

    // === 全量快照还原 ===

    [Fact]
    public void 全量还原_截回快照那一格之后前缀与序号都还原()
    {
        // 只给「全量快照原样还原」那一档：重试是同一局回到过去，战斗里新召的人
        // 必须连「这个 id 发过」一起作废，否则重开那一局发的是下一个号，逐字比对当场漂。
        var ledger = new EntityCore();
        ledger.Spawn("h11", "unit");
        ledger.Spawn("h12", "unit");
        ledger.Spawn("h13", "unit"); // 快照那一刻：三格、序号三
        ledger.Spawn("h14", "unit"); // 战斗里新召的
        ledger.Remove("h12");        // 快照里它已经死了，槽还在

        ledger.TruncateTo(3, 3);

        ledger.IssuedIds.ShouldBe(3, "h14 那一格连「发过」一起作废");
        ledger.Issued.ShouldBe(3, "序号拨回快照那一刻，重开才再发 h14 而不是 h15");
        ledger.NextSeq.ShouldBe(4);
        ledger.Ids.ShouldBe(new[] { "h11", "h13" }, "在册表按前缀重建，死槽仍是死的");
        ledger.Contains("h14").ShouldBeFalse();
        ledger.Records.Select(r => r.Id).ShouldBe(new[] { "h11", "h13" }, "在册的四格也按同一顺序重建");

        Should.Throw<ArgumentOutOfRangeException>(() => ledger.TruncateTo(4, 3));
        Should.Throw<ArgumentOutOfRangeException>(() => ledger.TruncateTo(3, 4));
    }

    // === 重放快照 ===

    [Fact]
    public void 重放_段首是entity()
    {
        var ledger = new EntityCore();
        ledger.Spawn("u1", "unit");
        ledger.Spawn("b1", "building");

        ledger.ReplaySnapshotSegment().ShouldBe(
            "entity count=2 issued=2\nu1 seq=1 kind=unit\nb1 seq=2 kind=building\n",
            "段首是 entity，与各世界自己的段首（大写 U / 小写 u）无关（D15）");
    }

    [Fact]
    public void 重放_调试段与重放段逐字相同()
    {
        var ledger = new EntityCore();
        ledger.Spawn("u1", "unit");
        ledger.Remove("u1");
        ledger.Spawn("u2", "unit");

        ledger.ReplaySnapshotSegment().ShouldBe(ledger.DebugSnapshotSegment(), 
            "两个入口两个名字，格式只有一处 —— 排两遍就会漂");
    }

    [Fact]
    public void 重放_同种子两跑逐字相同()
    {
        string Run()
        {
            var ledger = new EntityCore();
            ledger.Spawn("p01", "unit");
            ledger.Spawn("p02", "unit");
            ledger.Spawn("p03", "unit");
            ledger.Remove("p02");
            return ledger.ReplaySnapshotSegment();
        }

        Run().ShouldBe(Run(), "册子自己那份快照逐字可重放");
    }

    // === 事件 ===

    [Fact]
    public void 事件_三个形状各发一次()
    {
        var ledger = new EntityCore();
        ledger.Spawn("u1", "unit");
        ledger.Remove("u1");
        ledger.Register("u1", "unit");

        Csv(ledger.Events.All.Select(e => e.Wire)).ShouldBe("spawned,removed,reregistered", 
            "D12 就这三个：没有「序号推进」与「册子重置」两类事件");
    }

    [Fact]
    public void 事件_读档不发事件()
    {
        var source = new EntityCore();
        source.Spawn("u1", "unit");
        source.Remove("u1");

        var loaded = Load(Write(source));
        loaded.Events.Total.ShouldBe(0, "D12：读档是一次调用，不是「有一条上了册」");
    }

    [Fact]
    public void 事件_被环形挤出去的条数读得出来()
    {
        var ledger = new EntityCore(eventCapacity: 2);
        ledger.Spawn("u1", "unit");
        ledger.Spawn("u2", "unit");
        ledger.Spawn("u3", "unit");

        ledger.Events.KeptCount.ShouldBe(2, "环形只留最近两条");
        ledger.Events.Dropped.ShouldBe(1, "被挤出去的那一条要读得出来：「日志里没有」≠「没发生」");
        ledger.Events.TailText(5).ShouldContain("dropped=1", Case.Sensitive, "尾窗带读数行");
        ledger.Readout().ShouldContain("事件漏登 1 条", Case.Sensitive, "一行读数里也带这一格");
    }

    [Fact]
    public void 事件_容量非正数当场抛()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new EntityCore(eventCapacity: 0));
    }

    // === 读数 ===

    [Fact]
    public void 读数_一发行把在场那些数答完()
    {
        var ledger = new EntityCore();
        ledger.Spawn("u1", "unit");
        ledger.Spawn("u2", "unit");
        ledger.Spawn("u3", "unit");
        ledger.Remove("u3");

        ledger.Readout().ShouldBe(
            "实体层 在册 2 条｜发过 3 个 id｜下一个序号 4｜事件漏登 0 条｜宿主 不接（全活）｜移除接线 不接（那两步没发生）",
            "「这一场有哪些 kind」是调试第一个要问的问题，一行读数要答得出来（D4）");
    }

    // === 夹具 ===

    private static string Csv(IEnumerable<string> xs) => string.Join(",", xs);

    private static string Write(EntityCore ledger)
    {
        var w = new SaveWriter();
        ledger.WriteState(w);
        return w.Result;
    }

    private static EntityCore Load(string state, Func<string, object?>? payloadOf = null)
    {
        var loaded = new EntityCore();
        var cursor = new SaveCursor(state);
        cursor.EnterObject();
        loaded.ReadState(new SaveReader(cursor), payloadOf);
        cursor.LeaveObject();
        return loaded;
    }

    /// <summary>只答一件事的假宿主（IEntityHost 就这一个成员，D6）。</summary>
    private sealed class MapHost : IEntityHost
    {
        private readonly Dictionary<string, bool> _alive;

        public MapHost(Dictionary<string, bool> alive) => _alive = alive;

        public bool Alive(string id) => _alive.TryGetValue(id, out var v) && v;
    }

    /// <summary>
    /// 记调用次序的假接线。除了记下「叫了哪两步、什么次序」，
    /// 还在每次被叫的那一刻读一次册子 —— 那样「①摘册在②之前」才判得出来。
    ///
    /// <para><b>还记下每一步拿到的那一条的载荷</b>：②③ 跑在①之后，那时册子里已经查不到它了，
    /// 所以接线若回头按 id 查册子会拿到 null 而静默不做事 —— 那正是「退场还在挪」那一坑。
    /// 这一格把「那两步拿得到载荷」钉住（宿主侧踩过的那个坑，见那边那份退场定身接线）。</para>
    /// </summary>
    private sealed class RecordingRemoval : IEntityRemoval
    {
        private readonly EntityCore _ledger;

        public RecordingRemoval(EntityCore ledger) => _ledger = ledger;

        public List<string> Calls { get; } = new();

        public List<bool> InLedgerWhenCalled { get; } = new();

        /// <summary>每一步拿到的那一条：id 之后跟载荷是不是原来那个。</summary>
        public List<string> Handed { get; } = new();

        public void DetainInNav(EntityRecord record)
        {
            Calls.Add("定身");
            InLedgerWhenCalled.Add(_ledger.Contains(record.Id));
            Handed.Add($"{record.Id}:{(ReferenceEquals(record.Payload, Expected) ? "拿到载荷" : "载荷没了")}");
        }

        public void Release(EntityRecord record)
        {
            Calls.Add("腾引用");
            InLedgerWhenCalled.Add(_ledger.Contains(record.Id));
            Handed.Add($"{record.Id}:{(ReferenceEquals(record.Payload, Expected) ? "拿到载荷" : "载荷没了")}");
        }

        /// <summary>预期交出去的那个载荷对象（用来判「还是原来那一个」）。</summary>
        public object? Expected { get; set; }
    }
}
