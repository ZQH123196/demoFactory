#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DemoFactory.GugaFramework.Abilities;
using DemoFactory.Prototypes.BattleArray.Logic;
using Shouldly;
using Xunit;

namespace BattleArray.Tests;

/// <summary>
/// cue 这一侧要钉的（P4 后半，第八轮）—— 四类 cue 各有形状、<b>丢弃计数读得出来</b>、
/// 机制只有一份而内容在原型侧、表现层只订观测面、每兵种一条铺得下去。
///
/// 为什么这四条能离线跑：cue 的<b>台子与说明书</b>都是纯 C#（<c>GugaFramework/Abilities/AbilityCues.cs</c>
/// 与 <c>Scripts/Logic/SkillCueSpec.cs</c>），Godot 那半边只负责把排出来的行变成节点。
/// 「四类在同屏各看到一次」仍要出图用人眼验收（AGENTS 「落地配出图」），但<b>四类真的各产过一条</b>
/// 这件事从本轮起不必等人眼 —— A75 在真世界上跑一局就能判。
/// </summary>
public class AbilityCueTests
{
    /// <summary>派发台的默认档：同轨攒两条待播，第三条起就丢最旧的。</summary>
    private static CueFeedConfig Cap(int perTrack) => new() { PendingPerTrack = perTrack };

    // === A71：四类各自的背压策略，以及「丢了哪条」读不读得出来 ===

    /// <summary>
    /// <b>A71（积压怎么化、丢了要念得出）</b>：已删的设计档说「表现层必然积压，
    /// 允许丢弃与合并（只播最新状态），但丢了哪条要读得出来」。这一发就把那三句逐条钉住：
    /// ① 一次性同轨挤爆 ⇒ 丢最旧、计数与尾巴都念得出，最新那条照演；
    /// ② 持续/计数/带参数三档 ⇒ 同值重复推 = 合并（不发第二遍），换了值 = 新值顶旧值（也只发一条）；
    /// ③ <see cref="CueFeed.Readout"/> 那一行真有 <c>dropped=</c>，且数字与丢的条数一致。
    /// ④ Drain 的序是<b>确定序</b>（同一步按 单位/轨 的序数比较），否则同种子重跑做不到逐字等。
    /// </summary>
    [Fact]
    public void A71_四类各自化法_丢了哪条念得出来()
    {
        // ① 一次性：一条轨 5 发，默认档攒 2 ⇒ 丢最旧 3 条，留下的第 4 发在前
        var feed = new CueFeed();
        for (var i = 1; i <= 5; i++)
        {
            feed.PushOneShot(10, "p01", "hit", $"-{i}");
        }

        var rows = feed.Drain();
        rows.Select(r => r.Label).ShouldBe(new[] { "-4", "-5" }, Case.Sensitive, "挤爆了丢的是最旧那几条，留在画面上的必须是最新的（只播最新状态）");
        feed.Dropped.ShouldBe(3);
        feed.DroppedLines.Count.ShouldBe(3, "丢了哪条要念得出，不许只报一个总数");
        feed.DroppedLines[0].ShouldContain("10 p01/hit -1", Case.Sensitive, "尾巴里带步号、单位、轨与内容");
        feed.Readout().ShouldContain("dropped=3", Case.Sensitive, "读数那一行就是 dropped= 那一行");
        feed.Readout().ShouldContain("演 2", Case.Sensitive, "演出去的两条另算，不混进丢弃数");

        // ② 持续档：挂上发一条，重复挂不重发，撤下再发一条
        var sustained = new CueFeed();
        sustained.PushSustained(1, "e01", "effect:fx.guard_wall", "盾墙", true).ShouldBeTrue();
        sustained.PushSustained(2, "e01", "effect:fx.guard_wall", "盾墙", true).ShouldBeFalse("同一个身相重复推 = 合并掉，画面上不该闪第二遍");
        sustained.PushSustained(3, "e01", "effect:fx.guard_wall", "盾墙", false).ShouldBeTrue();
        var bands = sustained.Drain();
        bands.Count.ShouldBe(1, "同轨只留最新状态");
        bands[0].Value.ShouldBe(0, "留的是「撤了」那一条");
        sustained.Merged.ShouldBe(2, "三次推只演一条 ⇒ 合并两条（同值那条 + 被新值顶掉那条），这本账要能对上");
        sustained.Dropped.ShouldBe(0, "合并不是丢 —— 两个数各管一件事");
        // 记忆必须活过 Drain：同值再推还是不发第二遍，否则「演」就成了每步重报的虚数
        sustained.PushSustained(4, "e01", "effect:fx.guard_wall", "盾墙", false).ShouldBeFalse("已经发出去的同一个身相，下一步再推一次 = 合并（这条挡的是「演 815｜合并 0」那种假账）");
        sustained.Drain().ShouldBeEmpty("重报那一步画面上不该有新东西");
        sustained.Merged.ShouldBe(3);

        // ②' 计数档：数没变不发，变了只发新值（叠层与 tag 计数那一档）
        var counted = new CueFeed();
        counted.PushCounted(1, "p01", "tags", "守卫×1", 1);
        counted.PushCounted(2, "p01", "tags", "守卫×1", 1);
        counted.PushCounted(3, "p01", "tags", "守卫×2", 2);
        var marks = counted.Drain();
        marks.Select(m => m.Label).ShouldBe(new[] { "守卫×2" }, Case.Sensitive, "计数只播最新那一档，中间没变的重复不算新账");

        // ②'' 带参数档：半径没变不发第二遍
        var described = new CueFeed();
        described.PushDescriptive(1, "e01", "shape", "范围 120°", 3.0).ShouldBeTrue();
        described.PushDescriptive(2, "e01", "shape", "范围 120°", 3.0).ShouldBeFalse();
        described.PushDescriptive(3, "e01", "shape", "范围 120°", 4.5).ShouldBeTrue();
        described.Drain().Single().Value.ShouldBe(4.5, "地上那根线跟着最新参数走");

        // ③ 一次性那条数与读数一致（反证摘掉计数时，这里就是第一个红）
        feed.Dropped.ShouldBe(3);

        // ④ 确定序：乱序推三个兵与两条轨，Drain 出来的序只认 步号→单位→轨
        var ordered = new CueFeed(Cap(8));
        ordered.PushOneShot(5, "e02", "hit", "b");
        ordered.PushOneShot(5, "e02", "rejected", "a");
        ordered.PushOneShot(5, "e01", "hit", "c");
        ordered.PushSustained(5, "p01", "effect:x", "盾墙", true);
        var keys = ordered.Drain().Select(r => $"{r.Tick}/{r.UnitId}/{r.Track}").ToList();
        keys.ShouldBe(new[] { "5/e01/hit", "5/e02/hit", "5/e02/rejected", "5/p01/effect:x" }, Case.Sensitive, "字典序会跟着哈希种子漂，同种子重跑做不到逐字全等");

        // ⑤ 换一局归零（重开直达口与演示档重跑靠它，别让上一局的欠账挂着）
        ordered.Reset();
        ordered.Dropped.ShouldBe(0);
        ordered.Readout().ShouldContain("dropped=0");

        // ⑥ HUD 那一行只准一行长：尾巴留给桥（第八轮出图抓到「最近丢弃」8 条把底部三行字全压住）
        feed.ReadoutBrief().ShouldNotContain("最近丢弃", Case.Sensitive, "短版读数不许带尾巴 —— 它是 HUD 那一行");
        feed.Readout().ShouldContain("最近丢弃", Case.Sensitive, "长版（桥的 cues 动词）才带丢了哪条");
        feed.ReadoutBrief().ShouldContain("待播", Case.Sensitive, "那个数还没播出去，念成「挂着」就是假读数（显示即真相）");
    }

    // === A72：机制只有一份（公共层不认得技能），内容只有一份（说明书在原型侧） ===

    /// <summary>
    /// <b>A72（cue 的机制在公共层、内容在原型侧，两边都不许长出对方那份）</b>：
    /// 上一轮刚实测过同一种病：分发口抄两张表，一侧接得住、另一侧静默丢掉（N-24）。
    /// cue 这里同样是「一份机制 + 一份内容」，所以两条腿各扫一份源码：
    /// ① 公共层那份 <c>AbilityCues.cs</c> 里搜不到任何技能名 / tag 名 / 效果名的字面量
    ///    （它只认「哪条轨」这个字符串键）；② 原型侧说明书真引用了 <c>BattleAbilities</c> 的常量
    ///    （自己另抄一份技能名就是第二份表）；③ 两份文件都真读到了（防路径拼错成空跑，A54 同一手法）。
    /// </summary>
    [Fact]
    public void A72_机制一份内容一份_公共层搜不到技能名()
    {
        var feedSource = ReadSource("../../../GugaFramework/Abilities/AbilityCues.cs");
        feedSource.ShouldContain("class CueFeed", Case.Sensitive, "读到的是派发台本体");
        feedSource.ShouldContain("dropped=", Case.Sensitive, "读数那一行就在台子这一份里");

        // 公共层零字面量：技能名、效果名、tag 名一个都不许出现（这些名字全住原型的表里）
        foreach (var literal in new[] { "warrior.", "tank.", "archer.", "rider.", "fx.", "state.", "status.", "ck." })
        {
            feedSource.ShouldNotContain($"\"{literal}\"", Case.Sensitive,
                $"公共层里出现了 {literal} 开头的名字字面量 —— 接第二个原型时就得改它，那是抽象漏了");
        }

        var specSource = ReadSource("../Scripts/Logic/SkillCueSpec.cs");
        specSource.ShouldContain("class SkillCueSpec", Case.Sensitive, "读到的是说明书本体");
        specSource.ShouldContain("BattleAbilities.", Case.Sensitive, "原型侧要引用表里那些常量，而不是自己抄一份技能名");
    }

    // === A73：表现层只订观测面（「表现层只读」 在这一层的机器边界） ===

    /// <summary>
    /// <b>A73（cue 这一侧一个真值都不写）</b>：已删的设计档「三个口互不替换」——
    /// 表现层只能订观测面，不许碰注入面。这条历来只在人眼里（「画面有没有偷偷改逻辑」），
    /// 这里扫两份源码把它机器化：① 表现件与说明书里都搜不到注入动词与写入口；
    /// ② 两份都真读到了；③ 这条缝钉的是形状（2026-09-29 换）：原型侧不自己记事件账——
    /// <c>Scripts</c> 下搜不到 <c>.Detail</c>、搜不到 <c>Events.Add(</c>、搜不到 <c>Emit("</c>。
    /// </summary>
    [Fact]
    public void A73_表现侧搜不到写口_只订观测面()
    {
        var writes = new[]
        {
            "Inject", "TryActivate", "ApplyEffect(", "RemoveEffect(", "SetBase(", "SetAttribute(",
            "ClearCooldown(", "Grant(", "Advance(", "Dispose(", "world.Abilities =",
        };

        var files = new[] { "../Scripts/SkillCues.cs", "../Scripts/Logic/SkillCueSpec.cs" };
        foreach (var file in files)
        {
            var source = ReadSource(file);
            source.ShouldContain("class ", Case.Sensitive, $"{file} 读到的是源码本体");
            foreach (var write in writes)
            {
                source.ShouldNotContain(write, Case.Sensitive, $"{file} 里出现了写入口 {write} —— 表现层只读（AGENTS 「表现层只读」），" +
                    "「注入的路径」与「玩家的路径」不许在这条上混起来");
            }
        }

        AssertScriptsCarryNoSecondEventLedger();
    }

    /// <summary>
    /// 这条缝钉的是形状：事件的<b>名字与形状归公共层</b>（<c>AbilityEvents.cs</c> 那份名单），
    /// 原型侧只读字段。所以 <c>src/Prototypes/BattleArray/Scripts</c> 下三份禁用形状一个都不许出现——
    /// <c>.Detail</c>（把事件行文本再当依据抠一遍 = 第二份实现）、<c>Events.Add(</c>
    /// （场子往层的事件流里自己记账）、<c>Emit(\"</c>（把事件名当字符串字面量写死在原型侧）。
    /// 读源码找禁用形状，比钉一句旧代码稳：旧代码改了字面、这条不跟着漂。
    /// </summary>
    private static void AssertScriptsCarryNoSecondEventLedger()
    {
        var dir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "Scripts"));
        Directory.Exists(dir).ShouldBeTrue($"读不到 {dir}（路径拼错了这条就是空跑）");
        var files = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories);
        files.Length.ShouldBeGreaterThan(0, $"{dir} 下一份 .cs 都没读到（空跑）");
        foreach (var banned in new[] { ".Detail", "Events.Add(", "Emit(\"" })
        {
            foreach (var file in files)
            {
                File.ReadAllText(file).ShouldNotContain(banned, Case.Sensitive, $"{Path.GetFileName(file)} 里出现了禁用形状 {banned} —— 事件行只准由公共层的形状自己渲染，" +
                    "原型侧只读字段，不自己记第二本账");
            }
        }
    }

    // === A74：每兵种一条（P4「四兵种各一条」那张映射）+ 自增益不挂到敌人身上 ===

    /// <summary>
    /// <b>A74（每兵种一条，且默认关着）</b>：四兵种各领一条（坦克盾墙 / 战士突斩 / 弓手穿刺箭 / 骑手冲锋），
    /// 铺法走 <see cref="BattleAbilities.AttachPerTroop"/>。三条腿：
    /// ① 映射是<b>数据</b>不是分支：四个槽位各指一条、且四条互不相同；
    /// ② 装完之后每个兵身上<b>正好一条</b>，就是它兵种那一条，四条技能都在表里（打一发不留「未知」那种哑账）；
    /// ③ 盾墙是自增益：过 commit 之后 <c>fx.guard_wall</c> 挂在<b>施法者自己</b>身上、敌人身上没有
    ///    （自动战斗那条路把目标交给 <c>null</c>，公共层的 <c>TargetFor</c> 才回落到自身 —— 这一处回落只有一份实现）；
    /// ④ <b>目标口径也只有一份</b>：剧本注入那条路给自增益带了靶子同样当场抛，不静默改靶
    ///    （装配第一次就撞出「盾墙挂到敌人身上」，与 N-24 同一族，只是这次漂在靶子那一格）。
    /// 全关档不在此列：它由 <see cref="AbilityH3DuelTests"/> 那条旧账钉，本轮一字未动。
    /// </summary>
    [Fact]
    public void A74_每兵种一条_盾墙挂在自己身上()
    {
        // ① 四兵种各一条，且不重样
        var mapped = BattleContract.Kinds.Select(BattleAbilities.AbilityOfTroopKind).ToList();
        mapped.Distinct().Count().ShouldBe(mapped.Count, $"两条兵种领到了同一条技能：{string.Join(" / ", mapped)}");
        // 迁移前是 NUnit 的 Is.SupersetOf(expected)。Shouldly 4.3.0 只有反方向的
        // ShouldBeSubsetOf（没有 ShouldBeSupersetOf），而「actual 含 expected 全部元素」
        // 等价于「expected ⊆ actual」——把 expected 提成变量后走子集那一支。
        var requiredAbilities = new[]
        {
            BattleAbilities.GuardAbilityId, BattleAbilities.RiftAbilityId,
            BattleAbilities.PierceAbilityId, BattleAbilities.ChargeAbilityId,
        };
        requiredAbilities.ShouldBeSubsetOf(mapped, "每个兵种都领到了它那条技能（守 / 位移 / 穿透 / 冲阵四件套一个都不能少）");

        // ② 真铺一局满阵：每个兵身上正好一条
        using var world = new BattleWorld(DuelRiftSetup.DefaultSeed, Formations.ShieldWall,
            PlacementComparison.EnemyFormation, jitter: false);
        var layer = BattleAbilities.AttachPerTroop(world);
        foreach (var unit in world.Units)
        {
            var set = layer.Find(unit.Id) ?? throw new InvalidOperationException($"{unit.Id} 没装技能栏（N-18 那条边界破了）");
            set.Abilities.Keys.ShouldBe(new[] { BattleAbilities.AbilityOfTroopKind(unit.Kind) }, false, $"{unit.Id}（{unit.Kind}）身上那条不是它兵种的那一条");
        }

        // 表里真有这四条：打一发，行为流里念得出它（查无此技能会抛 KeyError，不是静默 false）
        var ours = world.Units.First(u => u.Side == Side.Player);
        var theirs = world.Units.First(u => u.Side == Side.Enemy);
        var oursAbility = BattleAbilities.AbilityOfTroopKind(ours.Kind);
        Should.NotThrow(() => layer.TryActivate(ours.Id, oursAbility,
            BattleAbilities.IsSelfTargeted(oursAbility) ? null : theirs.Id));
        (layer.EventsWhere<AbilityActivated>().Any(a => a.AbilityId == oursAbility)
            || layer.EventsWhere<AbilityActivatedAt>().Any(a => a.AbilityId == oursAbility)
            || layer.EventsWhere<AbilityRejected>().Any(r => r.AbilityId == oursAbility)
            || layer.EventsWhere<AbilityRejectedAndQueued>().Any(r => r.AbilityId == oursAbility))
            .ShouldBeTrue("打了一发，行为流里 activate / reject 两族形状都念不出这条技能 = 表里压根没有它");

        // ③ 盾墙是自增益：挂在自己身上
        var tank = world.Units.First(u => u.Kind == TroopKind.Tank && u.Side == Side.Player);
        var foe = world.Units.First(u => u.Side == Side.Enemy && u.Id != tank.Id);
        BattleAbilities.IsSelfTargeted(BattleAbilities.GuardAbilityId).ShouldBeTrue("盾墙没声明成自增益 ⇒ 自动战斗会把两笔 modifier 挂到敌人身上去");
        // 上面 ② 那一发可能正是打在这只兵身上的（满阵里第一个我方兵就是盾卫），冷却得先清掉再判
        world.Advance(1);
        layer.ClearCooldown(tank.Id, BattleAbilities.GuardAbilityId);
        layer.TryActivate(tank.Id, BattleAbilities.GuardAbilityId, null).ShouldBeTrue($"盾墙这一发没打进去，行为流尾窗：{layer.EventTailText(3)}");
        world.Advance(2);   // 前摇 0 步：按下即过 commit
        layer.EffectStacksOf(tank.Id).Keys.ShouldContain(_e => Equals(_e, BattleAbilities.GuardWallEffectId), "盾墙那 5 秒没挂在自己身上");
        layer.EffectStacksOf(foe.Id).Keys.ShouldNotContain(BattleAbilities.GuardWallEffectId, "自增益挂到敌人身上去了 —— 这一格红了就是「谁的目标」被判成了两份");
        layer.TagsOf(tank.Id).Keys.ShouldContain(_e => Equals(_e, BattleAbilities.GuardTagId), "tag 计数那一格（画面上念 ×2 的那个）要有来源");

        // ④ 目标口径只有一份：剧本那条路带了靶子也一样当场抛（与词表第三条同口径，A69 的靶子版）
        Should.Throw<ArgumentException>(() => DuelRiftSetup.Inject(world, AbilityVerbs.Cast,
            $"{tank.Id}|{BattleAbilities.GuardAbilityId}|{foe.Id}"), "自增益却被人塞了个目标：静默丢掉那一格就长成「自动战斗挂自己、剧本注入挂敌人」两套真值");
    }

    // === A75：四类 cue 在真世界上各产过一条（出图之前先钉这一小步） ===

    /// <summary>
    /// <b>A75（四类各真产过一条，用的是「每兵种一条」那一局）</b>：「在同屏各看到一次」得用人眼
    /// （「落地配出图」），但<b>四类真的都被产出来过</b>这件事不必等人眼 —— 满阵那一局按兵种铺上四条技能
    /// （坦克盾墙 / 战士突斩 / 弓手穿刺箭 / 骑手冲锋）自动开打，跑完整局逐逻辑步翻译，四档各至少排出一条：
    /// 一次性 = 命中那一刀、持续 = 盾墙在身、计数 = <c>state.guard</c> 两层、
    /// 带参数 = 弓手那发箭登记的待决结算（<c>pending</c>，画面上是「在路上」那根线）。
    /// 哪一档没产出来，红的那一条直接指名是哪格断了 —— 不用回头翻画面找帧。
    /// </summary>
    [Fact]
    public void A75_真世界跑一局_四类cue各产过一条()
    {
        using var world = new BattleWorld(DuelRiftSetup.DefaultSeed, Formations.ShieldWall,
            PlacementComparison.EnemyFormation, jitter: false);
        var layer = BattleAbilities.AttachPerTroop(world);
        layer.AutoCast = true;

        var feed = new CueFeed();
        var spec = new SkillCueSpec();
        var seen = new HashSet<CueKind>();
        var topCount = 0.0;
        for (var step = 0; step < 1300 && world.OutcomeTick is null; step++)
        {
            world.Advance(1);
            spec.OnStep(feed, world);
            foreach (var row in feed.Drain())
            {
                seen.Add(row.Kind);
                if (row.Kind == CueKind.Counted) topCount = Math.Max(topCount, row.Value);
            }
        }

        seen.ShouldContain(_e => Equals(_e, CueKind.OneShot), "一次性（命中）没产出来：伤害事件那一路断了");
        seen.ShouldContain(_e => Equals(_e, CueKind.Sustained), "持续（身相）没产出来：效果挂号或层数读数那一路断了");
        seen.ShouldContain(_e => Equals(_e, CueKind.Counted), "计数没产出来：tag 计数那一路断了");
        seen.ShouldContain(_e => Equals(_e, CueKind.Descriptive), "带参数没产出来：待决结算（在路上）与形状查询那两路都断了");
        topCount.ShouldBeGreaterThanOrEqualTo(1, "计数档念不出层数：tag 计数那一路是空的");
        // ×2 那一档（技能段 + 效果段两层叠在一起）在 H2 由 A55 钉；H3 这局盾墙前摇 0 步 ⇒ 只有效果段一层，
        // 画面上那个角标念得出 ×1、念不出 ×2。要在 H3 也念出 ×2，得有一条「前摇中授予 tag」的真技能，
        // 而清单页那 16 条里带这一格的是探针件（只在检查清单的壳里 Grant）—— 这条边界记进报告，不硬凑。
        feed.Emitted.ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// 「在路上」那根线必须<b>登记时亮、落地或作废时灭</b>：它是画面上唯一表达「延迟结算还在账上」的东西，
    /// 灭不掉就成了永久假指示（比没有更坏）。两个到期出口都判：<c>settle</c>（打中了）与
    /// <c>settle_void</c>（目标半路死了，作废也要读得出为什么作废）。
    /// </summary>
    [Fact]
    public void 在路上那根线_登记亮落地灭()
    {
        var feed = new CueFeed();
        var spec = new SkillCueSpec();
        var none = Array.Empty<SkillCueSpec.BodyRead>();

        spec.Step(feed, 10, new[]
        {
            new AbilityEvent(10, "p03", new SettlementRegistered("fx.pierce_arrow", 16, -8)),
        }, none);
        var landed = feed.Drain().Single();
        landed.Track.ShouldBe(SkillCueSpec.FlightTrack);
        landed.Value.ShouldBe(16, "值 = 落地那一步（第 1.6 秒），读的是 SettlementRegistered.LandingTick 字段");
        landed.Label.ShouldContain("16", Case.Sensitive, "念得出第几步落地（延迟结算在画面上要有交代）");

        spec.Step(feed, 16, new[]
        {
            new AbilityEvent(16, "p03", new EffectLanded("fx.pierce_arrow", "e02", 3, -8)),
        }, none);
        feed.Drain().Single().Value.ShouldBe(0, "落地之后线还亮着 = 画面上永久的假指示");

        spec.Step(feed, 30, new[]
        {
            new AbilityEvent(30, "p03", new SettlementRegistered("fx.pierce_arrow", 36, -8)),
        }, none);
        feed.Drain();
        spec.Step(feed, 36, new[]
        {
            new AbilityEvent(36, "p03", new EffectMissed("fx.pierce_arrow", "e02")),
        }, none);
        feed.Drain().Single().Value.ShouldBe(0, "作废那一发也得灭线");
    }

    // === 按步翻译那份规矩只有一份实现（表现件不许再判一遍） ===

    /// <summary>
    /// 同一局里把 <see cref="SkillCueSpec.OnStep"/> 连发三次而不推进世界：
    /// <b>第二次第三次一条都不许多产</b>（没新账就退）。这条规矩如果散在表现件里，
    /// <c>dropped=</c> 与「合并」就会刷成帧率的函数 —— 那行读数也就没法信了。
    /// </summary>
    [Fact]
    public void 同一逻辑步不重翻_读数不是帧率的函数()
    {
        using var world = DuelRiftSetup.NewWorld();
        world.Advance(39);
        DuelRiftSetup.Inject(world, AbilityVerbs.Cast,
            $"{DuelRiftSetup.CasterUnitId}|{BattleAbilities.RiftAbilityId}|{DuelRiftSetup.DummyUnitId}").ShouldBeTrue();
        world.Advance(5);   // 前摇 3 步 ⇒ 第 43 步落那一刀，行为流里多出一行 damage

        var feed = new CueFeed();
        var spec = new SkillCueSpec();
        spec.OnStep(feed, world);
        feed.Drain().Count.ShouldBeGreaterThan(0, "这一发连新账都没翻出来 —— 下面那条「不重翻」就成了空跑");

        spec.OnStep(feed, world);
        spec.OnStep(feed, world);
        feed.Drain().Count.ShouldBe(0, "同一步重复推 = 帧率决定演几条，画面与 dropped= 那行读数都不可信");

        // 换了账才有新东西：冷却中再按一次，层发的是 reject（画面上那条灰色被拒飘字的来源）
        DuelRiftSetup.Inject(world, AbilityVerbs.Cast,
            $"{DuelRiftSetup.CasterUnitId}|{BattleAbilities.RiftAbilityId}|{DuelRiftSetup.DummyUnitId}").ShouldBeFalse();
        spec.OnStep(feed, world);
        var rows = feed.Drain();
        rows.Count.ShouldBeGreaterThan(0, "有新账却不翻 = 上面那条红了也说不清是空跑还是漏翻");
        rows.Any(r => r.Track == SkillCueSpec.RejectedTrack).ShouldBeTrue("「为什么没放」要能在画面上念出来（D2 那三问的画面版）");
    }

    // === 读源码（与 A54 / A68 / A70 同一手法；读不到就抛，不空跑） ===

    /// <summary><paramref name="relative"/> 相对 <c>Tests/</c> 目录 —— <c>..</c> 的层数照那份拼法算。</summary>
    private static string ReadSource(string relative)
    {
        var baseDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..");
        var full = Path.GetFullPath(Path.Combine(baseDir, relative));
        if (!File.Exists(full))
            throw new FileNotFoundException($"读不到源码 {relative}（路径拼错了这条就是空跑）", full);
        return File.ReadAllText(full);
    }
}
