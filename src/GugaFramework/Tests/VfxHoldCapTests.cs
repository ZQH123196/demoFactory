#nullable enable
using DemoFactory.GugaFramework.Abilities;
using Shouldly;
using Xunit;

namespace DemoFactory.GugaFramework.Tests;

/// <summary>
/// 一次性档「<b>按倍率缩的持有时长上限</b>」的离线那半（ticket 08）。
///
/// <para><b>这一格回答什么</b>：<c>speed_scale</c> 只改粒子的演算速度，
/// <b>不改引擎抛 <c>finished</c> 的时刻</b>。实测（240 fps 定步长，<c>FxHit</c>）：
/// 画面上散尽在第 97 帧（1×）与第 10 帧（20×），而 <c>finished</c> 两路都在第 102 帧。
/// 于是高倍快进下等效池容量被压紧约 20 倍（容量默认 2），画面上早就散尽的那一簇还占着槽位，
/// 读数念「在池 0/2」而画面上什么都没有 —— 这一格验的是<b>槽位跟着粒子一起回来</b>。</para>
///
/// <para><b>为什么那假账特别难查</b>：<c>play</c> 看着演完了、槽位却还占着，
/// agent 会得出「池太小，该加容量」的错误结论，于是去调 <c>PoolCapacity</c> ——
/// 而病根在归还时机。而它与「效果没演」在画面上完全一样。</para>
///
/// <para><b>这里测的是机制，不是引擎</b>：<c>VfxPlayer</c> 吃引擎类型，而
/// <c>GpuParticles3D.Lifetime</c> 与 <c>_Process(delta)</c> 的帧长离线跑不了，
/// 所以 <see cref="VfxPlayerTranscript"/> 把那一段逐句转写了一份：
/// <c>Process(delta)</c> 就是引擎那半的 <c>_Process</c>，
/// 上限那一格就是 <c>lifetime ÷ 倍率 + 8 帧 × 帧长</c>。
/// <b>「引擎的滞后到底有几帧、几帧率下成立」离线不定，要真机看</b>
/// （真机那格：起技能试验场，1× 与 20× 各连演，比「借不出」那个数）。</para>
///
/// <para><b>断言口径</b>：账用 <see cref="VfxLedger"/> 的真件；
/// 「走上限归还了几条」读 <c>ExpiredReturns</c> 与读数行那一段。</para>
/// </summary>
public sealed class VfxHoldCapTests
{
    private const string Unit = "u1";

    /// <summary>一次性档那条轨（<c>res://vfx/FxHit.tscn</c>，<c>one_shot = true</c>，<c>lifetime = 0.4</c>）。</summary>
    private const string Hit = "hit";

    /// <summary>持续档那条轨（<c>res://vfx/FxGuardWall.tscn</c>，<c>one_shot = false</c>，<c>lifetime = 1.2</c>）。</summary>
    private const string Guard = "guard";

    /// <summary>240 fps 的一帧（秒）—— ticket 06 那轮实测用的就是这一档帧长。</summary>
    private const double Frame240 = 1.0 / 240.0;

    /// <summary><c>FxHit</c> 的 <c>lifetime</c>（秒），与 <c>src/Shared/vfx/FxHit.tscn</c> 逐字同值。</summary>
    private const double HitLifetime = 0.4;

    // ============================ 1. 1× 不变（不许回归） ============================

    [Fact]
    public void 倍率1_上限全程不生效_槽位照finished回来()
    {
        // 这一格是「不许回归」的那一半：1× 下归还的仍然只有 finished 一条路。
        // 如果上限在 1× 也生效，它就要与 finished 抢，抢赢了就是「粒子还在天上飞槽位先回来」。
        var fx = NewStage(capacity: 2);
        fx.Play(Hit, 0.0, 0.0).ShouldBeTrue();

        for (var frame = 0; frame < 240; frame++)   // 一整秒，远超 lifetime 0.4
        {
            fx.Process(Frame240);
        }

        fx.ExpiredReturns.ShouldBe(0, "1× 下没有上限：整整一秒（240 帧）走完，一条都不是靠上限还的");
        fx.Ledger.Alive.ShouldBe(1, "那条还占着槽位 —— 等 finished");
        fx.Ledger.InPool.ShouldBe(1);

        fx.AdvanceOneShot(Hit).ShouldBeTrue("演完了：抛 finished");
        fx.Ledger.Alive.ShouldBe(0);
        fx.Ledger.InPool.ShouldBe(2, "池回到满");
        fx.ExpiredReturns.ShouldBe(0, "走的是 finished 那一路，超时归还那个数不该动");
        fx.Ledger.Returned.ShouldBe(1);
    }

    [Fact]
    public void 倍率1_上限不生效_不是碰巧等到finished_是上限那格本身不存在()
    {
        // 上一条在 1 秒（远超 lifetime 0.4）之后才抛 finished，够长但还不够长到把
        // 「上限在 1× 也生效、只是比 finished 晚」这件事排除掉吗？排除了：
        // 上限在 1× 恒为无穷（倍率 ≤ 1 那一格），所以无论推多久都不会自己还。
        // 单独钉这一格是为了在「上限那个公式改错了」时立刻红 ——
        // 上面那条要推够久才红得出来。
        var fx = NewStage(capacity: 1);
        fx.Play(Hit, 0.0, 0.0).ShouldBeTrue();

        for (var frame = 0; frame < 2400; frame++)   // 10 秒，是 lifetime 的 25 倍
        {
            fx.Process(Frame240);
        }

        fx.Ledger.Alive.ShouldBe(1, "10 秒过去还没还：1× 下没有上限那条路");
        fx.Ledger.InPool.ShouldBe(0, "池里那一个被这条占着");
        fx.ExpiredReturns.ShouldBe(0);
    }

    // ============================ 2. 20× 上限兜住 ============================

    [Fact]
    public void 倍率20_粒子早散尽但上限先到_槽位已归还_在池回到满()
    {
        // 本 ticket的正题。20× 下画面第 10 帧就散尽，而 finished 第 102 帧才抛；
        // 不加上限的话这一条要占着槽位将近 0.43 秒，等效池容量被压紧约 20 倍。
        var fx = NewStage(capacity: 2);
        fx.GameSpeed = 20.0f;
        fx.Play(Hit, 0.0, 0.0).ShouldBeTrue();
        fx.Ledger.InPool.ShouldBe(1, "刚借出：池里少一个");

        // 推到画面散尽那一刻（第 10 帧 ≈ 0.042 秒）：粒子还在天上飞，槽位不能还。
        for (var frame = 0; frame < 10; frame++)
        {
            fx.Process(Frame240);
        }

        fx.Ledger.Alive.ShouldBe(1, "第 10 帧画面上刚散尽，槽位还占着 —— 上限不能卡得比真实散尽还早");
        fx.ExpiredReturns.ShouldBe(0, "还没到上限");

        // 再推到上限（0.4 / 20 + 8 帧 ≈ 0.02 + 0.033 = 0.053 秒 ≈ 第 13 帧）。
        for (var frame = 0; frame < 4; frame++)
        {
            fx.Process(Frame240);
        }

        fx.Ledger.Alive.ShouldBe(0, "上限到了：槽位自己还回来了");
        fx.Ledger.InPool.ShouldBe(2, "「在池」回到满 —— 读数不再念 0/2 而画面上什么都没有");
        fx.ExpiredReturns.ShouldBe(1, "走的是上限那一路");
        fx.Ledger.Returned.ShouldBe(1);
        fx.NodeIdleOf(Hit).ShouldBe(1, "节点回空闲栈了，能被下一次借出复用");
    }

    [Fact]
    public void 倍率20_连演若干条_在池一直回到满_不压紧()
    {
        // 「20× 下不该比 1× 更容易借不出」那一格的离线形状：
        // 容量 2，每 20 帧（0.083 秒）演一下。20× 下每 13 帧就还回来，两个池位够连着演；
        // 没有上限时每条要占 102 帧 ≈ 0.43 秒，第二下之后一路借不出。
        var fx = NewStage(capacity: 2);
        fx.GameSpeed = 20.0f;

        for (var shot = 0; shot < 10; shot++)
        {
            for (var frame = 0; frame < 20; frame++)
            {
                fx.Process(Frame240);
            }

            fx.Play(Hit, 0.0, 0.0).ShouldBeTrue($"第 {shot + 1} 下借得出：前面那几条早还回来了");
            fx.Ledger.InPool.ShouldBe(1, $"第 {shot + 1} 下刚借出，池里剩一个");
        }

        fx.Ledger.BorrowFailed.ShouldBe(0, "20× 下没有一条借不出");
        fx.Ledger.Alive.ShouldBe(1, "最后那一演还没到上限");
        fx.ExpiredReturns.ShouldBe(9, "前九条都走上限还的");
    }

    // ============================ 3. 两条路撞上只还一次 ============================

    [Fact]
    public void 上限先到之后引擎又抛finished_只还一次账()
    {
        // 幂等那一格。上限先还了槽位，引擎那边照旧会抛 finished（它不认我们的上限），
        // 那条回调在归还时已经被断开，所以多出来的那一抛没有回调可走。
        // 万一断漏了，GiveBack 里那道「摘不到名册就返回」的护栏兜着 ——
        // 账被还多一次的话在池数会被推高，借不出与在池两个数从此对不上。
        var fx = NewStage(capacity: 2);
        fx.GameSpeed = 20.0f;
        fx.Play(Hit, 0.0, 0.0).ShouldBeTrue();

        for (var frame = 0; frame < 13; frame++)
        {
            fx.Process(Frame240);
        }

        fx.Ledger.Alive.ShouldBe(0, "上限先到，已经还了");

        // 引擎那边照旧抛：这条路上节点已经回空闲栈，名册里没有它了。
        fx.AdvanceOneShot(Hit, times: 2).ShouldBeFalse("名册里已经没有那一条，没有回调可走");
        fx.PooledAt(Hit).FinishedHandlers.ShouldBe(0, "归还那格断干净了");

        fx.Ledger.Returned.ShouldBe(1, "只还一次");
        fx.Ledger.InPool.ShouldBe(2, "在池数没有被多还一次推高");
        fx.Ledger.Alive.ShouldBe(0);
        fx.NodeIdleOf(Hit).ShouldBe(1, "节点只回空闲栈一次");
        fx.ExpiredReturns.ShouldBe(1, "超时归还那个数没有被重复记");
    }

    [Fact]
    public void finished先到之后上限再扫_不重复记一次超时归还()
    {
        // 反过来的次序：正常路径先赢。上限那一格扫的是名册，而名册里已经没有那一条，
        // 所以它不会把同一次归还数两遍。
        var fx = NewStage(capacity: 2);
        fx.GameSpeed = 20.0f;
        fx.Play(Hit, 0.0, 0.0).ShouldBeTrue();

        fx.AdvanceOneShot(Hit).ShouldBeTrue("正常路径先到");
        fx.Ledger.Alive.ShouldBe(0);

        for (var frame = 0; frame < 240; frame++)
        {
            fx.Process(Frame240);
        }

        fx.ExpiredReturns.ShouldBe(0, "上限扫的是名册，那一条已经不在里面了");
        fx.Ledger.Returned.ShouldBe(1);
        fx.Ledger.InPool.ShouldBe(2);
    }

    [Fact]
    public void 同一帧内上限与finished同时到_仍然只还一次()
    {
        // 「同时到」那一格：先扫上限、再让引擎抛（真实次序是引擎内部决定的，两条路都能先）。
        // 两次都走一遍 GiveBack，账不许动第二次。
        var fx = NewStage(capacity: 2);
        fx.GameSpeed = 20.0f;
        fx.Play(Hit, 0.0, 0.0).ShouldBeTrue();

        // 推到正好越过上限的那一帧（上限 ≈ 第 13 帧）。
        for (var frame = 0; frame < 13; frame++)
        {
            fx.Process(Frame240);
        }

        fx.Ledger.Alive.ShouldBe(0, "上限已经还了");
        var returnedOnce = fx.Ledger.Returned;

        fx.AdvanceOneShot(Hit).ShouldBeFalse("引擎这一抛没有回调可走");
        for (var frame = 0; frame < 30; frame++)
        {
            fx.Process(Frame240);
        }

        fx.Ledger.Returned.ShouldBe(returnedOnce, "账没被动第二次");
        fx.Ledger.InPool.ShouldBe(2, "在池数没被推高");
        fx.ExpiredReturns.ShouldBe(1);
    }

    // ============================ 4. 倍率 0 ============================

    [Fact]
    public void 倍率0_上限不缩_暂停不会把池清空()
    {
        // 场子口径「暂停只冻逻辑、画面照跑」（契约「时间基准」）。
        // 如果上限跟着倍率缩到 0，暂停期间每一条都当场归还，池被清空，
        // 恢复之后第一条也要重借 —— 而画面上那簇粒子还冻在天上。
        var fx = NewStage(capacity: 2);
        fx.GameSpeed = 0.0f;
        fx.Play(Hit, 0.0, 0.0).ShouldBeTrue();
        fx.Play(Hit, 1.0, 0.0).ShouldBeTrue("第二个槽位也借得出");

        for (var frame = 0; frame < 2400; frame++)   // 暂停 10 秒
        {
            fx.Process(Frame240);
        }

        fx.ExpiredReturns.ShouldBe(0, "倍率 0 那格不缩上限：一条都没靠上限还");
        fx.Ledger.Alive.ShouldBe(2, "两条都还占着槽位，池没被清空");
        fx.Ledger.InPool.ShouldBe(0);
    }

    [Fact]
    public void 倍率0拧回正数_上限按拧回来的倍率算_不沿用暂停期间那几秒()
    {
        // 「上限每次现算、不借出那一刻定死」那一格：暂停期间没累计任何持有时长
        // （累计的那张表只在扫到那条时才读，而它压根没被扫到），
        // 所以拧回 20× 之后上限从「拧回来那一刻」起算，不是从暂停之前算。
        var fx = NewStage(capacity: 2);
        fx.GameSpeed = 20.0f;
        fx.Play(Hit, 0.0, 0.0).ShouldBeTrue();

        for (var frame = 0; frame < 100; frame++)   // 先在 20× 下推 0.42 秒（早已越过上限）
        {
            fx.Process(Frame240);
        }

        fx.Ledger.Alive.ShouldBe(0, "20× 下那条早该被上限还掉了");
        fx.ExpiredReturns.ShouldBe(1);
    }

    // ============================ 5. 持续档不归这一格 ============================

    [Fact]
    public void 持续档挂着的那几条_上限不碰它_槽位由显式撤下收()
    {
        // 持续档不抛 finished，槽位由宿主显式撤下收（spec D8/D9）。
        // 拿持有时长去收它等于替宿主猜「该撤了」—— 而护盾该挂多久是玩法语义。
        // 这一格钉的是「上限只扫一次性那份名册」。
        var fx = NewStage(capacity: 2);
        fx.GameSpeed = 20.0f;
        fx.Sustain(Cue(1)).ShouldBeTrue();

        for (var frame = 0; frame < 2400; frame++)   // 10 秒 ≈ lifetime 1.2 秒的 8 倍
        {
            fx.Process(Frame240);
        }

        fx.ExpiredReturns.ShouldBe(0, "持续档不走上限那一路");
        fx.Ledger.Alive.ShouldBe(1, "挂着的那一条还占着槽位");
        fx.Attached.ShouldBe(1);

        fx.Sustain(Cue(0)).ShouldBeTrue("撤下");
        fx.Ledger.Alive.ShouldBe(0, "撤下之后才还");
        fx.Ledger.InPool.ShouldBe(2);
    }

    // ============================ 6. 读数 ============================

    [Fact]
    public void 超时归还了几条_读数行里读得出来()
    {
        // spec D15 那一格：没有读数就等于没这回事。
        var fx = NewStage(capacity: 4);
        fx.GameSpeed = 20.0f;

        fx.Play(Hit, 0.0, 0.0).ShouldBeTrue();
        for (var frame = 0; frame < 13; frame++)
        {
            fx.Process(Frame240);
        }

        fx.ExpiredReturns.ShouldBe(1, "走上限还了一条");
        fx.Readout().ShouldBe("vfx 演 1｜借不出 0｜在池 4/4｜超时归还 1",
            "读数行多出「超时归还」那一格，数字用不变文化排版");

        fx.ExpiredReturns.ShouldBe(1);
        fx.Readout().ShouldBe("vfx 演 1｜借不出 0｜在池 4/4｜超时归还 1");
    }

    [Fact]
    public void 一次都没靠上限还_那一格念0_不是不出现()
    {
        // 反过来那一格：1× 下那一格恒为 0，而 0 要念出来 ——
        // 少了它就分不出「还没发生」与「没接上」（同「三条铁律」第三条那一口气）。
        var fx = NewStage(capacity: 2);
        fx.Play(Hit, 0.0, 0.0).ShouldBeTrue();

        fx.Readout().ShouldBe("vfx 演 1｜借不出 0｜在池 1/2｜超时归还 0");

        fx.AdvanceOneShot(Hit).ShouldBeTrue();
        fx.Readout().ShouldBe("vfx 演 1｜借不出 0｜在池 2/2｜超时归还 0");
    }

    [Fact]
    public void 上限公式用的是那份效果自己的lifetime_不是本层配的数()
    {
        // 短的那一份（FxStackMark，lifetime 0.3）与长的那一份（FxShapeRing，lifetime 0.6）
        // 在同一倍率下上限不同。这一格钉的是「上限从效果资源读 lifetime」：
        // 两条长度不同的轨，归还时刻要不同 —— 若上限写死成一个数，这一格就红。
        var fx = new VfxPlayerTranscript(capacity: 4);
        fx.RegisterEffect("short", "res://vfx/FxStackMark.tscn", oneShot: true, lifetime: 0.3);
        fx.RegisterEffect("long", "res://vfx/FxShapeRing.tscn", oneShot: true, lifetime: 0.6);
        fx.GameSpeed = 20.0f;
        fx.Play("short", 0.0, 0.0).ShouldBeTrue();
        fx.Play("long", 1.0, 0.0).ShouldBeTrue();

        for (var frame = 0; frame < 11; frame++)
        {
            fx.Process(Frame240);
        }

        fx.ExpiredReturns.ShouldBe(0, "第 11 帧（0.046 秒）两条都还没到上限（短的那份 0.048 秒）");
        fx.Ledger.Alive.ShouldBe(2);

        // 再推一帧到 0.050 秒：短的那一份（上限 0.048 秒）过了，长的那一份（0.063 秒）没过。
        fx.Process(Frame240);

        fx.ExpiredReturns.ShouldBe(1, "短的那一份先到上限（0.3/20 + 8 帧 ≈ 0.048 秒 < 长那一份的 0.063 秒）");
        fx.Ledger.Alive.ShouldBe(1, "还占着槽位的是长的那一条");

        // 再推 4 帧到 0.067 秒：长那一份的上限（0.063 秒）也过了。
        for (var frame = 0; frame < 4; frame++)
        {
            fx.Process(Frame240);
        }

        fx.ExpiredReturns.ShouldBe(2, "长那一份也到上限了");
        fx.Ledger.InPool.ShouldBe(4, "两条都还回来了");
    }

    // ============================ 7. 帧长 ============================

    [Fact]
    public void 低帧率_上限按帧长变长_不会切在粒子散尽之前()
    {
        // 「滞后那一段按帧折、不写死秒数」那一格：同一个倍率下，帧率低的那一路
        // 滞后占的秒数更长，于是上限更靠后。若上限里那几帧被写成秒数常量，
        // 30 fps 那一路就会切在画面散尽之前 —— 粒子还在天上飞槽位先回来了。
        var fx = NewStage(capacity: 4);
        fx.GameSpeed = 20.0f;
        fx.Play(Hit, 0.0, 0.0).ShouldBeTrue();

        // 20× 下画面第 10 帧散尽（0.4/20 = 0.02 秒，按 30 fps 算是第 0.6 帧，
        // 真实散尽要等引擎那几帧）。上限 = 0.02 + 8/30 = 0.287 秒 ≈ 第 8.6 帧。
        // 推 6 帧（0.2 秒）：远过画面散尽，但还没到上限。
        for (var frame = 0; frame < 6; frame++)
        {
            fx.Process(1.0 / 30.0);
        }

        fx.Ledger.Alive.ShouldBe(1, "0.2 秒：上限还没到");
        fx.ExpiredReturns.ShouldBe(0);

        for (var frame = 0; frame < 4; frame++)
        {
            fx.Process(1.0 / 30.0);
        }

        fx.ExpiredReturns.ShouldBe(1, "0.33 秒：上限到了");
        fx.Ledger.InPool.ShouldBe(4);
    }

    [Fact]
    public void 帧长非正_持有时长不动_不会凭空推快()
    {
        // 引擎给 0 帧长（首帧或某种节流）时，那一格什么都不做：
        // 上限是「最多占多久」，被 0 帧长推快就成了「比上限更早还」。
        var fx = NewStage(capacity: 2);
        fx.GameSpeed = 20.0f;
        fx.Play(Hit, 0.0, 0.0).ShouldBeTrue();

        for (var frame = 0; frame < 100; frame++)
        {
            fx.Process(0.0);
        }

        fx.Ledger.Alive.ShouldBe(1, "100 个 0 帧之后仍在演 —— 持有时长一秒都没走");
        fx.ExpiredReturns.ShouldBe(0);
    }

    private static VfxPlayerTranscript NewStage(int capacity)
    {
        var fx = new VfxPlayerTranscript(capacity: capacity);
        fx.RegisterEffect(Hit, "res://vfx/FxHit.tscn", oneShot: true, lifetime: HitLifetime);
        return fx;
    }

    private static CueRow Cue(double value)
        => new(1, CueKind.Sustained, Unit, Guard, "护盾", value);
}