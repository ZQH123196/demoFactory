#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DemoFactory.GugaFramework.Abilities;
using DemoFactory.GugaFramework.Construction;
using DemoFactory.GugaFramework.Vfx;
using Shouldly;
using Xunit;

namespace DemoFactory.GugaFramework.Tests;

/// <summary>
/// 特效层的<b>纯 C# 那一半</b>（ticket 02）：轨名→效果路径那张表、池账与读数、四档枚举。
///
/// <para>这半份之所以能离线判红，是因为它一个字都不碰引擎：借与还、容量推导、读数格式
/// 全是纯函数与计数器，不需要起一局看画面（spec 第 3 节「离线可测」那一档）。
/// 演出机制（<c>restart()</c> / <c>speed_scale</c> / 借节点）留 04/05。</para>
///
/// <para>断言口径：读数逐字比（照 <c>CueFeed.Readout()</c> 与 <c>ModelAnimator.DebugReadout()</c>），
/// 因为「读数是给人指路的那一行」——它一旦漂了，判红理由本身就指错了方向。</para>
/// </summary>
public sealed class VfxLedgerTests
{
    // ============================ 表：轨名 → 效果路径 ============================

    [Fact]
    public void 登了的轨名查得到效果路径()
    {
        var spec = new VfxSpec(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["hit"] = "res://Shared/vfx/hit.tscn",
            ["slow"] = "res://Shared/vfx/slow.tscn",
        });

        spec.TryGetEffect("hit", out var path).ShouldBeTrue("表里登过的轨名要查得到");
        path.ShouldBe("res://Shared/vfx/hit.tscn", "查出来的就是原型登的那条 res:// 路径，原样交回");
        spec.Unregistered.ShouldBe(0, "查得到就不该计漏登");
    }

    [Fact]
    public void 没登的轨名回查得到false_不当场抛_并且计数()
    {
        var spec = new VfxSpec(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["hit"] = "res://Shared/vfx/hit.tscn",
        });

        // 表是原型侧的内容：漏登一条是作者的事，不该让整局崩在同一帧上。
        var found = spec.TryGetEffect("no_such_track", out var path);

        found.ShouldBeFalse("没登的轨名只能回「查不到」，不回一个错的效果");
        path.ShouldBeNull("查不到时没有路径可交回");
        spec.Unregistered.ShouldBe(1, "漏登必须读得出来：「日志里没有」≠「没发生」（三条铁律第三条）");

        spec.MissedTracks.ShouldBe(new[] { "no_such_track" }, "漏的是哪条轨也要念得出来，只有个数查不出该登哪儿");
        spec.TrackCount.ShouldBe(1, "在册的轨数不受查不到影响");
    }

    [Fact]
    public void 同一轨反复查不到_每次都计_有界尾巴留最近几条()
    {
        var spec = new VfxSpec(new Dictionary<string, string>(StringComparer.Ordinal));

        // 故意查超过尾巴上限的次数，否则「有界」那一格根本不会被走到（断言数刚好等于上限 = 没验到截断）。
        var asked = Enumerable.Range(0, VfxSpec.MissedTailKept + 2)
            .Select(i => $"t{i}")
            .ToArray();
        foreach (var track in asked)
        {
            spec.TryGetEffect(track, out _).ShouldBeFalse();
        }

        spec.Unregistered.ShouldBe(asked.Length, "每一次查不到就是一笔账：漏登被两帧各问一遍也是两回");
        spec.MissedTracks.Count.ShouldBe(VfxSpec.MissedTailKept,
            "尾巴有界（照 RollingLog / CueFeed.DroppedLines 那条），别跟着局长涨");
        spec.MissedTracks[^1].ShouldBe(asked[^1], "最新的那条在最后");
        spec.MissedTracks[0].ShouldBe("t2", "最旧的两条已被挤出尾巴（这是「有界」那一格的真证据）");
    }

    [Fact]
    public void 表的读数逐字()
    {
        var spec = new VfxSpec(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["hit"] = "res://Shared/vfx/hit.tscn",
        });
        spec.TryGetEffect("miss", out _);

        spec.Readout().ShouldBe("vfx 表 1 条｜漏登 1", "这一行是 HUD 与桥读的那一句，格式只此一处");
    }

    // ============================ 四档枚举 ============================

    [Fact]
    public void 四档从0起显式给值()
    {
        // 显式给值：往后中间插一档时，逐字序列化出来的旧读数不会整体挪位
        // （与 NavPathStatus / NavPathReason / PlacementReason 同一口径）。
        ((int)VfxKind.OneShot).ShouldBe(0);
        ((int)VfxKind.Sustained).ShouldBe(1);
        ((int)VfxKind.Counted).ShouldBe(2);
        ((int)VfxKind.Descriptive).ShouldBe(3);
    }

    [Fact]
    public void 四档不共用CueKind那个类型()
    {
        // 借用 CueKind 会把 Abilities 与 Vfx 绑死：动一处枚举，另一处的序列化跟着变。
        // 各模块自带 reason 枚举是本仓先例（NavPathReason / PlacementReason）。
        typeof(VfxKind).IsEnum.ShouldBeTrue("四档是本模块的分类轴");
        typeof(VfxKind).ShouldNotBe(typeof(CueKind));
        Enum.GetNames<VfxKind>().ShouldBe(Enum.GetNames<CueKind>(),
            "档位语义与 CueFeed 的分类轴一一对齐（spec D5），但类型各自一份");
    }

    // ============================ 容量推导 ============================

    [Fact]
    public void 容量从PendingPerTrack推()
    {
        new VfxLedger(new CueFeedConfig { PendingPerTrack = 2 }).Capacity.ShouldBe(2,
            "spec D11：容量从 CueFeedConfig.PendingPerTrack 推导，不另设参数");
        new VfxLedger(new CueFeedConfig { PendingPerTrack = 7 }).Capacity.ShouldBe(7);
        new VfxLedger().Capacity.ShouldBe(CueFeedConfig.Default.PendingPerTrack,
            "不给 config 时推的是那份缺省值");
    }

    [Fact]
    public void 显式覆盖容量时取覆盖值()
    {
        new VfxLedger(new CueFeedConfig { PendingPerTrack = 2 }, capacity: 9).Capacity.ShouldBe(9,
            "场景侧那一格 [Export] 接线走的就是这个 int：现场能调");
    }

    [Fact]
    public void 容量小于1当场抛_不兜0()
    {
        // 兜 0 的后果：一条都借不出，且账上念「在池 0/0」——看着像正常状态。
        Should.Throw<ArgumentException>(() => new VfxLedger(capacity: 0))
            .Message.ShouldContain("容量至少 1", Case.Sensitive, "判红要指向「为什么必须给一个正整数」");
        Should.Throw<ArgumentException>(() => new VfxLedger(new CueFeedConfig { PendingPerTrack = -1 }));
    }

    // ============================ 账：借出 / 借不出 / 归还 ============================

    [Fact]
    public void 借出一条_演账加1_池里减1_存活加1()
    {
        var ledger = new VfxLedger(new CueFeedConfig { PendingPerTrack = 2 });

        ledger.Borrow().ShouldBeTrue("池里还有一条，借得出");
        ledger.Played.ShouldBe(1, "借出成功就是演了一条");
        ledger.InPool.ShouldBe(1, "借走一条池里就少一条");
        ledger.Alive.ShouldBe(1, "当前存活 +1");
        ledger.BorrowFailed.ShouldBe(0, "借得出就不算借不出");
    }

    [Fact]
    public void 池空时借不出_演账不加_借不出加1()
    {
        var ledger = new VfxLedger(new CueFeedConfig { PendingPerTrack = 2 });
        ledger.Borrow();
        ledger.Borrow();

        ledger.Borrow().ShouldBeFalse("池空借不出");
        ledger.Played.ShouldBe(2, "借不出就没演，演账不许涨");
        ledger.BorrowFailed.ShouldBe(1, "没演这件事必须读得出来（D10：与 CueFeed 丢 cue 记 Dropped 同理）");
        ledger.InPool.ShouldBe(0);
        ledger.Alive.ShouldBe(2, "存活就是借出去没还的那两条");
    }

    [Fact]
    public void 归还后池内数回来()
    {
        var ledger = new VfxLedger(new CueFeedConfig { PendingPerTrack = 2 });
        ledger.Borrow();
        ledger.Borrow();

        ledger.Return();

        ledger.InPool.ShouldBe(1, "还一条回池");
        ledger.Alive.ShouldBe(1, "还一条就没那么活着");
        ledger.Played.ShouldBe(2, "归还不改演账");
        ledger.BorrowFailed.ShouldBe(0);
    }

    [Fact]
    public void 三个数对得上_演账等于存活加已归还()
    {
        var ledger = new VfxLedger(new CueFeedConfig { PendingPerTrack = 2 });
        ledger.Borrow();
        ledger.Borrow();
        ledger.Return();
        ledger.Borrow();

        ledger.Played.ShouldBe(ledger.Alive + ledger.Returned, "演出去的每一条此刻要么还活着、要么已归还");
        ledger.InPool.ShouldBe(ledger.Capacity - ledger.Alive, "池里 + 借出的 = 总量");
        ledger.Alive.ShouldBe(2);
        ledger.Returned.ShouldBe(1);
        ledger.BorrowFailed.ShouldBe(0);
    }

    [Fact]
    public void 池空借不出之后再归还_那一发就演成()
    {
        // 池空那一次不是排队等一会儿，是当场丢掉（spec D10：不选「临时新建」）。
        var ledger = new VfxLedger(new CueFeedConfig { PendingPerTrack = 1 });
        ledger.Borrow().ShouldBeTrue();
        ledger.Borrow().ShouldBeFalse();
        ledger.Return();

        ledger.Borrow().ShouldBeTrue("还回池里之后又借得出了");
        ledger.Played.ShouldBe(2);
        ledger.BorrowFailed.ShouldBe(1);
    }

    [Fact]
    public void 没借过就归还当场抛()
    {
        // 账三行对不上的最常见来路：归还的次数多过借出。静默忽略的话池里会凭空多条。
        var ledger = new VfxLedger(new CueFeedConfig { PendingPerTrack = 2 });
        Should.Throw<InvalidOperationException>(() => ledger.Return())
            .Message.ShouldContain("没借出过", Case.Sensitive, "判红要指向「多还了一次」而不是让人回去读池子");
        ledger.Returned.ShouldBe(0, "抛了就不该把这笔账记上");
    }

    // ============================ 读数逐字 ============================

    [Fact]
    public void 读数逐字_演借不出在池三段()
    {
        var ledger = new VfxLedger(new CueFeedConfig { PendingPerTrack = 2 });
        ledger.Borrow();
        ledger.Borrow();
        ledger.Borrow();      // 借不出

        ledger.Readout().ShouldBe("vfx 演 2｜借不出 1｜在池 0/2",
            "spec D10 念的就是那三段，逐字比：HUD 与桥读的是同一份格式化");
    }

    [Fact]
    public void 读数不出现存活粒子数_那一格本轮没有调用方()
    {
        var ledger = new VfxLedger(new CueFeedConfig { PendingPerTrack = 2 });
        ledger.Borrow();
        ledger.ReportAliveParticles(120);

        ledger.Readout().ShouldBe("vfx 演 1｜借不出 0｜在池 1/2",
            "ReportAliveParticles 递进来了 120，读数也不念它：那一格本轮没有调用方，恒为 0，"
            + "而恒为 0 会被读成「实测结论是零粒子」（「日志里没有」≠「没发生」）");
        ledger.Readout().ShouldNotContain("粒子", Case.Sensitive, "那一格整个不出现，不换个名字出现");
        ledger.AliveParticles.ShouldBe(120, "属性与那个方法都留着：接上真值直接用，不必再开一格进快照");
    }

    [Fact]
    public void 读数在别的区域设置下不变_只念不变文化()
    {
        // 「double 与不变文化」：读数文本在土耳其语等区域下也必须逐字一样。
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
            var ledger = new VfxLedger(new CueFeedConfig { PendingPerTrack = 3 });
            ledger.Borrow();

            ledger.Readout().ShouldBe("vfx 演 1｜借不出 0｜在池 2/3", "数字格式不许跟着区域设置走");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void 换一局把账归零()
    {
        var ledger = new VfxLedger(new CueFeedConfig { PendingPerTrack = 2 });
        ledger.Borrow();
        ledger.Borrow();
        ledger.Borrow();
        ledger.Return();

        ledger.Reset();

        ledger.Played.ShouldBe(0);
        ledger.BorrowFailed.ShouldBe(0);
        ledger.Returned.ShouldBe(0);
        ledger.InPool.ShouldBe(ledger.Capacity, "重开一局池是满的");
        ledger.Alive.ShouldBe(0);
    }

    // ============================ 不进真值 ============================

    [Fact]
    public void 本目录读数不出现在任何快照输出里()
    {
        var spec = new VfxSpec(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["hit"] = "res://Shared/vfx/hit.tscn",
        });
        var ledger = new VfxLedger(new CueFeedConfig { PendingPerTrack = 2 });
        ledger.Borrow();
        ledger.Borrow();
        ledger.Borrow();
        spec.TryGetEffect("miss", out _);

        // 先把读数弄成「有话可说」的状态：全 0 的一行字查不出任何东西。
        var vfxText = $"{ledger.Readout()}｜{spec.Readout()}";
        vfxText.ShouldContain("漏登 1", Case.Sensitive, "本条自己先钉住：读数确实有内容（否则下面那些断言全是假绿）");

        // 快照出口至少取两个不同的模块，两处都不许出现这一行。
        var snapshots = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Abilities"] = AbilitySnapshot.Save(new AbilityWorld()),
            ["Abilities.带单位"] = AbilitySnapshot.Save(WorldWithOneUnit()),
        };

        foreach (var (where, text) in snapshots)
        {
            // 「粒子」那一格不在这个清单里：读数已经不念它（见上面那条），留着只会变成一条
            // 永远匹配不到的空断言。清单里的每一段都必须真的出自读数，否则它自己就成了假绿。
            foreach (var piece in new[] { ledger.Readout(), spec.Readout(), "vfx 演", "在池", "借不出", "漏登" })
            {
                text.ShouldNotContain(piece, Case.Sensitive,
                    $"{where} 那份快照里出现了特效层的读数片段「{piece}」——粒子节点、存活数、池占用都在真值下游，"
                    + "进了快照就会让「没分叉的地方报分叉」（spec 第 3 节「不进真值」）");
            }
        }
    }

    [Fact]
    public void 本目录不声明任何快照出口()
    {
        // 结构那一半：上面那条钉的是「快照文本里没有」，这条钉的是「本模块压根没提供往快照里写的那一格」。
        // 两条都要有 —— 只有前者的话，把读数拼进快照的那一格可以是任意形状，判红只会指错方向。
        var offenders = new List<string>();
        foreach (var type in typeof(VfxLedger).Assembly.GetTypes()
                     .Where(t => t.Namespace == "DemoFactory.GugaFramework.Vfx"))
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
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
            "特效层不提供快照 / 序列化出口（spec 第 3 节「不进真值」）。真值要重建的是逻辑，"
            + "粒子节点与池占用是画面侧的临时物，重建它没有意义：\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void 判据抓得住_别的模块真有快照出口()
    {
        // 上一条是「扫到 0 个所以全过」，而一个从不判红的守卫与没有守卫长得一样。
        // 拿真模块反证这条判据今天真的匹配得到东西。
        var withSnapshot = typeof(ConstructionCore)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name.Contains("Snapshot", StringComparison.Ordinal))
            .ToList();

        withSnapshot.ShouldNotBeEmpty(
            "判据在真模块上也得匹配得到东西（否则上面那条是假绿）。若建造层哪天改名了，改的是这条断言的样本，不是判据");
    }

    private static AbilityWorld WorldWithOneUnit()
    {
        var world = new AbilityWorld();
        world.AddUnit("u1", new[] { ("training.power", 10.0) });
        return world;
    }
}
