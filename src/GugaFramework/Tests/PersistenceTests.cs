#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DemoFactory.GugaFramework.Construction;
using DemoFactory.GugaFramework.Persistence;
using Shouldly;
using Xunit;

namespace DemoFactory.GugaFramework.Tests;

/// <summary>
/// 存档公共层的判据 —— 形状、往返、上限、迁移分派、出口守卫。
///
/// <para>往返那一格是这一层的主力判据：<c>WriteState → ReadState → WriteState</c>
/// 两串逐字相等。它跑不绿只有两种病——有派生数据被存进去了，或漏了字段——
/// 一个测试顶两件事，所以它是这一整层最先要绿的那一条。</para>
/// </summary>
public class PersistenceTests
{
    // === 往返 ===

    [Fact]
    public void 往返两串逐字相等()
    {
        var world = FakeWorld.Rich();
        var first = world.Compose();
        var second = world.Reload().Compose();
        second.ShouldBe(first, "WriteState → ReadState → WriteState 两串必须逐字相等（少一个字段或多写一份派生数据都跑不绿）");
    }

    [Fact]
    public void 往返守住浮点全精度()
    {
        // 少一个 "R" 往返格式，0.1 就会写成 0.10000000000000001 —— 往返照样「相等」，
        // 所以这条要在源头掐：写出来的字面量本身就得是最短往返那一份。
        var text = FakeWorld.Rich().Compose();
        text.ShouldContain("\"ratio\":0.1,", Case.Sensitive, "浮点走 \"R\" 往返格式：0.1 写出来就该是 0.1");
        text.ShouldNotContain("0.10000000000000001", Case.Sensitive, "浮点没走 \"R\" 往返格式，逐字基线当场分叉");
    }

    [Fact]
    public void 集合次序由写侧定死()
    {
        // 通用件不替集合排序：排了就等于把「集合按什么序」的决定搬进公共层。
        var a = new FakeWorld { Ids = ["b", "a", "c"] };
        var b = new FakeWorld { Ids = ["a", "b", "c"] };
        a.Compose().ShouldNotBe(b.Compose(), "同一批值、两种次序，是两份不同的存档");
        a.Reload().Ids.ShouldBe(a.Ids, "读回来必须是写进去的那一份次序");
    }

    [Fact]
    public void 字符串转义能还原()
    {
        var world = new FakeWorld { Text = "引号\" 反斜杠\\ 换行\n 制表\t 控制 全部" };
        world.Reload().Text.ShouldBe(world.Text, "字符串转义必须逐字还原（能力层那份快照是多行文本，不转义就读不回来）");
    }

    [Fact]
    public void 非有限数当场拒写()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => FakeWorld.Rich().Compose(useNaN: true))
            .Message.ShouldContain("非有限数", Case.Sensitive, "NaN 写进存档就是一个读不回来的档，缺口当场判否");
    }

    [Fact]
    public void 同一个键写两遍当场抛()
    {
        var w = new SaveWriter();
        w.Int("hp", 1);
        Should.Throw<InvalidOperationException>(() => w.Int("hp", 2))
            .Message.ShouldContain("hp", Case.Sensitive, "一份存档只能有一份权威字段清单");
    }

    [Fact]
    public void 读到别的键当场念出两个名字()
    {
        // 「读档之后某个字段没读回来」这一类故障最省钱的查法：
        // 游标当场说出「此处该是 hp，档里写的是 gold」，而不是丢一句「格式错误」。
        var w = new SaveWriter();
        w.Int("hp", 10);
        w.Int("gold", 3);

        var reader = ReaderOf(w.Result);
        var e = Should.Throw<FormatException>(() => reader.Int("gold"));
        e.Message.ShouldContain("gold", Case.Sensitive, "判红要指出该读的键");
        e.Message.ShouldContain("hp", Case.Sensitive, "判红要指出档里写的是什么键");
    }

    [Fact]
    public void 少一个字段当场抛()
    {
        var w = new SaveWriter();
        w.Int("hp", 10);

        var reader = ReaderOf(w.Result);
        var e = Should.Throw<FormatException>(() => { reader.Int("hp"); reader.Int("mana"); });
        e.Message.ShouldContain("少了一个字段", Case.Sensitive, "缺字段是「这份档不是这份代码写的」，不能兜默认值");
    }

    // === 文件头 ===

    [Fact]
    public void 文件头只有那四个键()
    {
        var text = SaveFile.Compose(1, "FakeWorld", 4000, "{\"a\":1}");
        text.ShouldStartWith("{\"v\":1,\"world\":\"FakeWorld\",\"tick\":4000,\"state\":", Case.Sensitive,
            "文件头只有 v / world / state / tick 四个键");
        text.ShouldEndWith("}\n", Case.Sensitive, "落盘件末尾带一个换行（否则每次存档都把工作区弄脏一次）");
        text.ShouldNotContain("seed", Case.Sensitive, "存档是全量状态，读档后世界已经是那一刻的样子，seed 在存档里没有作用");
        text.ShouldNotContain("bytes", Case.Sensitive, "bytes 是派生数据（text.Length 当场就有），留着就多一份能漂的读数");
    }

    [Fact]
    public void 文件头拆回来逐字相等()
    {
        var world = FakeWorld.Rich();
        var text = SaveFile.Compose(world.SchemaVersion, "FakeWorld", 4000, world.Compose());
        var header = SaveFile.Split(text);
        header.Version.ShouldBe(1);
        header.World.ShouldBe("FakeWorld");
        header.Tick.ShouldBe(4000);
        header.State.ShouldBe(world.Compose());
    }

    [Fact]
    public void 不是一份本模块写的存档当场报错()
    {
        Should.Throw<SaveFormatException>(() => SaveFile.Split("not json"))
            .Message.ShouldContain("存档文件头不对", Case.Sensitive, "读不成就要说清是文件头那一层读的");
    }

    // === 迁移链 ===

    [Fact]
    public void 当前版本原样交出()
    {
        var header = new SaveHeader(1, "W", 10, "{}");
        SaveMigration.BringForward(header).ShouldBe(header, "已是当前版本时原样交出");
    }

    [Fact]
    public void 档比当前新当场报错()
    {
        Should.Throw<SaveVersionTooNewException>(() =>
                SaveMigration.BringForward(new SaveHeader(99, "W", 0, "{}")))
            .Message.ShouldContain("v99", Case.Sensitive, "拿旧代码读新档，那份档的字段这一版根本没见过");
    }

    [Fact]
    public void 迁移表先在()
    {
        // 第一版只有 v1，表却是先在的：改第一个字段时没有插入点就晚了。
        SaveMigration.Readout().ShouldContain("迁移表空（当前 v1）", Case.Sensitive,
            "迁移表要在代码里先占好位置（spec 第 7 节），空表也要有读数能证明它在");
    }

    // === 槽位与落盘 ===

    [Fact]
    public void 写档落盘读档回来两串相等()
    {
        using var tmp = new TempDirectory();
        var store = new SaveStore(tmp.Path);
        var world = FakeWorld.Rich();
        store.Write(1, "FakeWorld", world, 4000);

        var target = new FakeWorld();
        var header = store.Read(1, "FakeWorld", target);
        header.Tick.ShouldBe(4000);
        target.Compose().ShouldBe(world.Compose(), "落盘那一圈不能改字");
    }

    [Fact]
    public void 原型名不符当场报错()
    {
        using var tmp = new TempDirectory();
        var store = new SaveStore(tmp.Path);
        store.Write(1, "FakeWorld", FakeWorld.Rich(), 1);

        Should.Throw<SaveWorldMismatchException>(() => store.Read(1, "OtherWorld", new FakeWorld()))
            .Message.ShouldContain("OtherWorld", Case.Sensitive, "读档先比对原型名，不对当场报错");
    }

    [Fact]
    public void 空槽位念出来是空()
    {
        using var tmp = new TempDirectory();
        var store = new SaveStore(tmp.Path);
        var info = store.Probe(1);
        info.Exists.ShouldBeFalse();
        info.WhenText.ShouldBe("空", "没档时设置屏那一格显示「空」");
        store.Readout().ShouldContain("槽位 1 空", Case.Sensitive);
    }

    [Fact]
    public void 槽位清单能枚举()
    {
        using var tmp = new TempDirectory();
        var store = new SaveStore(tmp.Path);
        store.Write(2, "FakeWorld", FakeWorld.Rich(), 7);

        var slots = store.Enumerate(3);
        slots.Count.ShouldBe(3, "接口要能枚举：第一版单槽，多槽是增量");
        slots.Select(s => s.Exists).ShouldBe(new[] { false, true, false });
        slots[1].World.ShouldBe("FakeWorld");
        slots[1].Tick.ShouldBe(7);
        (slots[1].Bytes > 0).ShouldBeTrue("文件字节数要念出来（设置屏那一格显示它）");
    }

    [Fact]
    public void 写盘走原子不留临时件()
    {
        using var tmp = new TempDirectory();
        var store = new SaveStore(tmp.Path);
        store.Write(1, "FakeWorld", FakeWorld.Rich(), 1);
        File.Exists(store.PathOf(1)).ShouldBeTrue();
        File.Exists(store.PathOf(1) + ".tmp").ShouldBeFalse("先写 .tmp 再改名：半份档比没有档更难查");
    }

    [Fact]
    public void 超上限当场拒写并说出哪一段最大()
    {
        using var tmp = new TempDirectory();
        var store = new SaveStore(tmp.Path);
        var huge = new FakeWorld { Blob = new string('x', 11 * 1024 * 1024) };

        var e = Should.Throw<SaveTooLargeException>(() => store.Write(1, "FakeWorld", huge, 1));
        e.Message.ShouldContain("当场拒写", Case.Sensitive);
        e.Message.ShouldContain("blob", Case.Sensitive,
            "报错要带哪一段最大：光说「11MB」等于让读的人自己去猜");
        store.HasSlot(1).ShouldBeFalse("超限当场拒写，不落半份档");
    }

    // === 出口守卫 ===

    [Fact]
    public void 本命名空间只有两个写出口()
    {
        // 扫法必须带命名空间过滤：漏掉那个 Where，那几个世界类上的 ReplaySnapshot /
        // DebugSnapshot、NavStepper.DebugSnapshotSegment 等五个现有方法会一起进 offenders。
        var offenders = new List<string>();
        foreach (var type in typeof(SaveStore).Assembly.GetTypes()
                     .Where(t => t.Namespace == "DemoFactory.GugaFramework.Persistence"))
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                   | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (method.Name.Contains("Snapshot", StringComparison.Ordinal)
                    || method.Name.Contains("Serialize", StringComparison.Ordinal)
                    || method.Name.Contains("Save", StringComparison.Ordinal))
                {
                    offenders.Add($"{type.Name}.{method.Name}");
                }
            }
        }

        offenders.ShouldBeEmpty(
            "存档层的写出口只有 WriteState / ReadState 两个。长出第二套序列化会让「哪一份是权威字段清单」"
            + "重新变成三份：\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void 守卫判据抓得住别的模块真有快照出口()
    {
        // 上一条是「扫到 0 个所以全过」，而一个从不判红的守卫与没有守卫长得一样。
        var withSnapshot = typeof(ConstructionCore)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name.Contains("Snapshot", StringComparison.Ordinal))
            .ToList();

        withSnapshot.ShouldNotBeEmpty(
            "判据在真模块上也得匹配得到东西（否则上面那条是假绿）。若建造层哪天改名了，改的是这条断言的样本，不是判据");
    }

    // === 样本 ===

    /// <summary>把一段状态文本摆成读侧游标（内部型在测试程序集里直接可见：测试项目源码链接了这一层）。</summary>
    private static SaveReader ReaderOf(string stateText)
    {
        var cursor = new SaveCursor(stateText);
        cursor.EnterObject();
        return new SaveReader(cursor);
    }

    /// <summary>测试样本世界 —— 形状上够全的一份清单：标量、嵌套对象、数组、浮点、字符串。</summary>
    private sealed class FakeWorld : ISaveable
    {
        public int Hp { get; set; } = 10;
        public int Gold { get; set; } = 3;
        public double Ratio { get; set; } = 0.1;
        public float Speed { get; set; } = 2.5f;
        public long Tick { get; set; } = 4000;
        public bool Alive { get; set; } = true;
        public string Text { get; set; } = "屋顶";
        public List<string> Ids { get; set; } = ["a", "b"];
        public List<(string Id, double Value)> Rows { get; set; } = [("r1", 1.5), ("r2", 2.5)];
        public string Blob { get; set; } = "";

        /// <summary>灌一份非有限数进去（只为触发拒写）。</summary>
        private bool _useNaN;

        public int SchemaVersion => 1;

        public void WriteState(SaveWriter w)
        {
            if (_useNaN) w.Num("ratio", double.NaN);
            w.Int("hp", Hp);
            w.Int("gold", Gold);
            w.Num("ratio", Ratio);
            w.Float("speed", Speed);
            w.Long("tick", Tick);
            w.Bool("alive", Alive);
            w.Str("text", Text);
            if (Blob.Length > 0) w.Str("blob", Blob);
            w.Arr("ids", l => { foreach (var id in Ids) l.Str(id); });
            w.Arr("rows", l =>
            {
                foreach (var (id, value) in Rows)
                    l.Obj(o =>
                    {
                        o.Str("id", id);
                        o.Num("value", value);
                    });
            });
            w.Obj("meta", m => { m.Int("v", 1); m.Str("tag", "主角"); });
        }

        public void ReadState(SaveReader r)
        {
            _useNaN = false;
            Hp = r.Int("hp");
            Gold = r.Int("gold");
            Ratio = r.Num("ratio");
            Speed = r.Float("speed");
            Tick = r.Long("tick");
            Alive = r.Bool("alive");
            Text = r.Str("text");
            Blob = r.PeekKey() == "blob" ? r.Str("blob") : string.Empty;
            Ids = [];
            r.Arr("ids", l =>
            {
                while (!l.Done) Ids.Add(l.Str());
            });
            Rows = [];
            r.Arr("rows", l =>
            {
                while (!l.Done)
                    l.Obj(o => Rows.Add((o.Str("id"), o.Num("value"))));
            });
            r.Obj("meta", m => { m.Int("v"); m.Str("tag"); });
        }

        /// <summary>这一份的状态文本。</summary>
        public string Compose(bool useNaN = false)
        {
            _useNaN = useNaN;
            var w = new SaveWriter();
            WriteState(w);
            return w.Result;
        }

        /// <summary>读一份新的回来（往返那一格用）。</summary>
        public FakeWorld Reload()
        {
            var target = new FakeWorld();
            target.ReadState(ReaderOf(Compose()));
            return target;
        }

        public static FakeWorld Rich() => new();
    }

    /// <summary>临时目录（用完即删，落在系统临时区，不进仓库）。</summary>
    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "demofactory-save-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // 临时目录删不掉不是本轮的判据，留着让系统清。
            }
        }
    }
}