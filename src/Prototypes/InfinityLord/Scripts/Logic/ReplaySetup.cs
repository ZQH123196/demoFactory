#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using DemoFactory.GugaFramework.Construction;
using DemoFactory.GugaFramework.Abilities;
using DemoFactory.GugaFramework.Replay;

namespace DemoFactory.Prototypes.InfinityLord.Logic;

/// <summary>
/// 无限领主的重放装配 —— <b>只留这一侧该留的：动词名单、每个动词那一次调用、推进到目标步的循环。</b>
///
/// 收口前这份文件是「词表 + 三个钩子 + 参数编码 + 规范化」的整块拷贝，与 BeastArena 的
/// <c>ArenaSetup</c>、BattleArray 的 <c>DuelRiftSetup</c> 各一份。公共那半现在住在
/// <see cref="ReplayAssembly"/>：参数编码（<see cref="ReplayArgs"/>）、规范化、写路径守卫、
/// Journal 追加、检查点守卫、三钩子工厂、脚本导出，全在那边一份。
///
/// <para>这一侧留的判定（「重放装配单源」）：
/// <b>失败一律 return false，永不抛</b>。未知命令、字段数不符、数值解析失败、逻辑层拒绝，
/// 四种同款。理由是录制的 ok 字段是权威（<see cref="ReplayRunner.Hooks.Inject"/> 原文
/// 「返回实际 ok，与录制时必须一致」）—— 抛异常会让「录进去的东西」有一类重放不了。</para>
///
/// <para><b>args 一律规范化后再入录</b>（kind 记枚举名），同一逻辑建筑在 Journal 里只有一种
/// token。解析取并集：枚举名（忽略大小写）+ 界面短名 + 游侠公会旧名兼容别名。</para>
///
/// <para>推进到目标步留在这一侧，因为它要的能力别处没有：<see cref="LogicWorld"/> 没有
/// 「暂停也照走」那个口，所以只能守暂停（暂停时空转、步号不动，循环自然退出不死等）。</para>
/// </summary>
public static class ReplaySetup
{
    /// <summary>词表全名。加动词改这一处（并列进下面 <see cref="Inject"/> 的 switch）。</summary>
    public static readonly string[] VerbNames =
    [
        "PlaceBountyFlag",
        "RaiseBountyFlag",
        "UpgradeGuild",
        "HealBuilding",
        "DamageBuilding",
        "DebugSetTreasury",
        "PlaceBuilding",
        "PlaceUnit",

        // 技能层那两条（<see cref="AbilityVerbs"/> 的词表，本原型只接这两条）：
        // 「注入的路径与玩家的路径不许漂成两回事」—— 它们转发的是世界出手用的那同一个入口。
        "CastAbility",
        "ApplyEffect",

        // 行为树那两条：树住在表现层，测试世界没有它，
        // 所以「谁被树接管了」「这一步树说了什么」只有进了 Journal 才重放得出来
        // （→ 「写路径进 Journal」）。接管那一条是重放端自建接管表的唯一入口 ——
        // ReportBrainIntent 只在接管在册时才落笔（不在册 = 不是一次写，返 false）。
        "DelegateHeroToBrain",
        "ReportBrainIntent",

        // 存档那两条（写档 / 读档）：走同一份词表，但**两条都不进 Journal** ——
        // 写档不改逻辑状态，读档是「换世界」不是「改状态」（重放端把世界重建一遍
        // 与录制端换一次世界不是同一件事，录进去就分叉了）。
        "WriteSave",
        "LoadSave",

        // 合成输入那两条（2026-10-05 用户裁决进 Journal）：
        // 让账上读得出「玩家这一手是按哪个键按的、点了哪个坐标」。
        // **重放端一律跳过并返 true** —— 重放端是纯 C#、没有引擎也没有光标，
        // 复现不了「引擎收到了一次点击」；真正的状态变更由紧随其后的
        // PlaceBuilding / PlaceBountyFlag 那几笔承担，它们才是重放断言的落点。
        // 所以这两条只增账面可读性，不增断言能力（机制见 SyntheticVerbs.Skip）。
        .. SyntheticVerbs.Names,
    ];

    /// <summary>这一串是不是词表里的动词（区分大小写，与 Journal 里的写法一致）。</summary>
    public static bool IsVerb(string cmd) => Array.IndexOf(VerbNames, cmd) >= 0;

    /// <summary>
    /// <b>进 Journal 的那一份</b> —— 与 <c>NavVerbs.InjectNames</c> / <c>ConstructionVerbs.InjectNames</c>
    /// 同一形状的分档表。
    ///
    /// <para><b>为什么要分档</b>：往 <see cref="VerbNames"/> 加一条就自动进录
    /// （<c>ReplayAssembly.Inject</c> 只判 <c>IsVerb</c>），而存档那两条<b>不能</b>进 ——
    /// 写档不改逻辑状态，读档是换世界。两张表一分，「能不能录」这件事就在词表上写着，
    /// 而不是散在各个动词的注释里。</para>
    /// </summary>
    public static readonly string[] InjectNames =
    [
        .. VerbNames.Where(name => name is not ("WriteSave" or "LoadSave")),
    ];

    /// <summary>这一条进不进 Journal（分派那一份）。</summary>
    public static bool IsInject(string cmd) => Array.IndexOf(InjectNames, cmd) >= 0;

    // ---- 装配入口：把这一侧递进公共那半 ----

    /// <summary>存档那两条动词由表现层答（换世界要重建整个表现层，逻辑层碰不到）。</summary>
    /// <param name="OnWrite">写档（槽号 → 做完了）。</param>
    /// <param name="OnRead">读档（槽号 → 做完了）。</param>
    public readonly record struct SaveHooks(Action<int> OnWrite, Action<int> OnRead);

    /// <summary>
    /// 给一个已经建好的世界装上装配，<b>建好即开录</b>（起点快照当场抓）。
    /// 世界的交还走 <c>onDispose</c>：装配不拥有世界的生命周期。
    ///
    /// <para><paramref name="verbNames"/> 递的是<b>进 Journal 的那一份</b>
    /// （<see cref="InjectNames"/>）而不是全表 —— 入录闸门读的就是它。</para>
    /// </summary>
    public static ReplayAssembly Recorder(LogicWorld world, int seed, SaveHooks? saveHooks = null) => new(
        world,
        seed,
        InjectNames,
        InjectorFor(world, saveHooks),
        target => AdvanceTo(world, target),
        onDispose: world.Dispose,
        recording: true);

    /// <summary>测试内录制的入口：按种子建世界并从第 0 步起录。</summary>
    public static ReplayAssembly ForSeed(int seed) => Recorder(new LogicWorld(randomSeed: seed), seed);

    /// <summary>
    /// 三个钩子一次填齐 —— 重放端与取证器唯一的接线点。
    /// 不开录：重放端只吃词表，不产生新账（「写路径进 Journal」）。
    /// </summary>
    public static ReplayRunner.Hooks Hooks(LogicWorld world) =>
        new ReplayAssembly(world, 0, InjectNames, InjectorFor(world, null), target => AdvanceTo(world, target))
            .Hooks();

    /// <summary>把这一侧的词表递进公共那半 —— 录制端与重放端消费的是同一段代码（「写路径全录」）。</summary>
    private static ReplayInject InjectorFor(LogicWorld world, SaveHooks? saveHooks)
        => (string cmd, string args, out string canonical, out string reason)
            => Inject(world, cmd, args, out canonical, out reason, saveHooks);

    /// <summary>
    /// 推进到目标步 —— 转发给基类那一份（三个世界统一走它）。
    ///
    /// <para>原先这里是本侧一份手写循环（分块 + 守暂停），与世界装配上那份逐字同形。
    /// 收编之后只有一处实现：推进次序、分块大小、暂停空转都在那边。</para>
    ///
    /// <para><paramref name="chunkTicks"/> 是块大小，默认 250。确定性哨兵要「逐步 vs 大块」
    /// 对照（chunkTicks: 1），所以它不是一个可有可无的参数。</para>
    /// </summary>
    public static void AdvanceTo(LogicWorld world, long targetTick, int chunkTicks = 250)
        => world.AdvanceTo(targetTick, chunkTicks);

    // ---- 纯词表：不碰 Journal（重放端与离线 CLI 也走它） ----

    /// <summary>跑一条写路径（重放端用；不需要规范化 args 与原因时走这条）。</summary>
    public static bool Inject(LogicWorld world, string cmd, string args)
        => Inject(world, cmd, args, out _, out _);

    /// <summary>跑一条写路径，并回传<strong>规范化后的 args</strong>（Journal 记的就是它）。</summary>
    public static bool Inject(LogicWorld world, string cmd, string args, out string canonicalArgs)
        => Inject(world, cmd, args, out canonicalArgs, out _);

    /// <summary>跑一条写路径，回传规范化后的 args 与失败原因。
    ///
    /// <para><paramref name="reason"/> 只在这一侧自己判否的地方有值（参数段数不对、数值解析不出、
    /// 未知动词）；<b>逻辑层自己拒收的没有原因</b> —— 世界那几个方法只回一个裸 bool，
    /// 那一格要带原因得先让世界回话，不在本轮范围内。原因给当次调用看，<b>不进 Journal</b>。</para>
    /// </summary>
    public static bool Inject(
        LogicWorld world, string cmd, string args, out string canonicalArgs, out string reason)
        => Inject(world, cmd, args, out canonicalArgs, out reason, null);

    private static bool Inject(
        LogicWorld world, string cmd, string args, out string canonicalArgs, out string reason,
        SaveHooks? saveHooks)
    {
        canonicalArgs = args;
        reason = string.Empty;
        switch (cmd)
        {
            case "PlaceBountyFlag":
                if (!ReplayArgs.Split(args, 2, out var a1)
                    || !ReplayArgs.TryInt(a1[1], out var gold))
                {
                    return No(ref reason, $"{cmd} 参数要 2 段（目标 id|金数），收到「{args}」");
                }

                canonicalArgs = Args(a1[0], gold);
                return world.PlaceBountyFlag(a1[0], gold);

            case "RaiseBountyFlag":
                if (!ReplayArgs.Split(args, 2, out var a2)
                    || !ReplayArgs.TryInt(a2[1], out var extra))
                {
                    return No(ref reason, $"{cmd} 参数要 2 段（旗 id|追加金数），收到「{args}」");
                }

                canonicalArgs = Args(a2[0], extra);
                return world.RaiseBountyFlag(a2[0], extra);

            case "UpgradeGuild":
                // 裸 Id（无分隔）—— 与 Main 的 TryUpgradeGuild、UpgradeGuildDebug 同一口径
                return world.UpgradeGuild(args);

            case "HealBuilding":
                if (!ReplayArgs.Split(args, 2, out var a3)
                    || !ReplayArgs.TryInt(a3[1], out var amount))
                {
                    return No(ref reason, $"{cmd} 参数要 2 段（建筑 id|治疗量），收到「{args}」");
                }

                canonicalArgs = Args(a3[0], amount);
                return world.DebugHealBuilding(a3[0], amount);

            case "DamageBuilding":
                if (!ReplayArgs.Split(args, 2, out var a4)
                    || !ReplayArgs.TryInt(a4[1], out var damage))
                {
                    return No(ref reason, $"{cmd} 参数要 2 段（建筑 id|伤害），收到「{args}」");
                }

                canonicalArgs = Args(a4[0], damage);
                return world.DebugDamageBuilding(a4[0], damage);

            case "DebugSetTreasury":
                if (!ReplayArgs.TryInt(args, out var treasury))
                {
                    return No(ref reason, $"{cmd} 参数要一个整数，收到「{args}」");
                }

                canonicalArgs = Args(treasury);
                world.DebugSetTreasury(treasury);
                return true;

            case "PlaceBuilding":
                return PlaceBuilding(world, args, out canonicalArgs, out reason);

            case "PlaceUnit":
                // 排布口：把一个在场单位原样放到一个绝对落点（测试排布与调试直达共用）。
                // 进词表的理由与排兵布阵那张 ticket 同款：位置真值搬进移动解之后，
                // 「把它摆到这儿」也是一次写 —— 不进录的话重放端的排布会与录制端对不上账。
                if (!ReplayArgs.Split(args, 3, out var a5)
                    || !ReplayArgs.TryNumber(a5[1], out var px)
                    || !ReplayArgs.TryNumber(a5[2], out var pz))
                {
                    return No(ref reason, $"{cmd} 参数要 3 段（单位 id|x|z），收到「{args}」");
                }

                canonicalArgs = PlaceUnitArgs(a5[0], new Vector2((float)px, (float)pz));
                return world.PlaceUnitForTest(a5[0], new Vector2((float)px, (float)pz));

            case "CastAbility":
            case "ApplyEffect":
                return SkillVerb(world, cmd, args, out canonicalArgs, out reason);

            case "DelegateHeroToBrain":
                return DelegateHeroToBrain(world, args, out canonicalArgs, out reason);

            case "ReportBrainIntent":
                return ReportBrainIntent(world, args, out canonicalArgs, out reason);

            case "SyntheticInputAction":
                return SyntheticInput(args, SyntheticVerbs.ActionSegments, out canonicalArgs, out reason);

            case "SyntheticInputClick":
                return SyntheticInput(args, SyntheticVerbs.ClickSegments, out canonicalArgs, out reason);

            case "WriteSave":
            case "LoadSave":
                return SaveVerb(cmd, args, saveHooks, out canonicalArgs, out reason);

            default:
                // 未知命令 = 客户端发了一个不存在的动词，不是一次写：返 false 且不入录
                // （入录守卫在 ReplayAssembly.Inject，它对未知命令也照跑，所以这里是唯一
                //   能把它挡在 Journal 之外的地方 —— 见 ReplayVerbTableTests「未知命令不入录」）。
                return No(ref reason, $"命令「{cmd}」不在词表里（可用：{string.Join(" / ", VerbNames)}）");
        }
    }

    /// <summary>
    /// 技能层那两条动词：<b>参数次序、缺省值与解析全是 <see cref="AbilityVerbs"/> 那一份</b>
    /// （「重放装配单源」的同款口径：词表本体一份都不在这里另抄），本方法只做两件原型自己的事 ——
    /// ① 把这一侧那一份接收方（<see cref="InfinityLordAbilities"/>）递进去；
    /// ② 把模块那侧抛出来的作者错误统一接成 <c>false</c>（本类的第一条约定：永不抛）。
    ///
    /// <para><b>入录的 args 原样</b>：这两条动词的每一段都是 id 与数值，没有方言要规范化
    /// （营建那一档要规范化是因为第 1 段有界面短名与旧名两种写法）。</para>
    /// </summary>
    private static bool SkillVerb(
        LogicWorld world, string cmd, string args, out string canonicalArgs, out string reason)
    {
        canonicalArgs = args;
        reason = string.Empty;
        try
        {
            return AbilityVerbs.Dispatch(world.Abilities, cmd, args);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException
                                      or NotSupportedException or ArgumentException or KeyError
                                      or OverflowException)
        {
            return No(ref reason, $"{cmd} 注入失败（{ex.Message}）");
        }
    }

    // ---- 合成输入：账上可读，重放端跳过（跳过那份实现收在公共层 SyntheticVerbs） ----

    /// <summary>
    /// 合成输入那两条在重放端的处理 —— 一行转发公共那一份。
    ///
    /// <para><b>为什么只有转发、逻辑全在公共层</b>：跳过与参数校验的形状对每个原型都一样
    /// （见 <see cref="SyntheticVerbs.Skip"/> 的注释）。各原型各写一遍就是四份同形代码，
    /// 下次加一档输入形态要改四处 —— 正是「重放装配单源」要挡的漂移。</para>
    ///
    /// <para>本侧只负责递「这条动词要几段」：那是各自词表说了算的（照本文件各动词的既有写法）。</para>
    /// </summary>
    private static bool SyntheticInput(string args, int segments, out string canonicalArgs, out string reason)
        => SyntheticVerbs.Skip(args, segments, out canonicalArgs, out reason);

    /// <summary>
    /// 存档那两条：<b>参数是槽号一段</b>，实现由表现层递进来（换世界要重建整个表现层，逻辑层碰不到）。
    ///
    /// <para>它们<b>不在 <see cref="InjectNames"/> 里</b>，所以走了这一发也不进 Journal ——
    /// 写档不改逻辑状态，读档是换世界，两条都不是「一次写」。</para>
    /// </summary>
    private static bool SaveVerb(
        string cmd, string args, SaveHooks? hooks, out string canonicalArgs, out string reason)
    {
        canonicalArgs = args;
        reason = string.Empty;
        if (hooks is null)
        {
            return No(ref reason, $"{cmd} 没有接线：表现层没递存档钩子进来（重放端没有存档，这一格判否）");
        }

        if (!ReplayArgs.TryInt(args, out var slot) || slot < 1)
        {
            return No(ref reason, $"{cmd} 参数要一个槽号（1 起），收到「{args}」");
        }

        canonicalArgs = Args(slot);
        if (cmd == "WriteSave") hooks.Value.OnWrite(slot);
        else hooks.Value.OnRead(slot);
        return true;
    }

    // ---- 行为树：接管与意图 ----

    /// <summary>
    /// 接管那一条：<c>英雄id</c>（单段，无分隔符）—— 照 <c>UpgradeGuild</c> 那一档的写法。
    ///
    /// <para>它是重放端自建接管表的<b>唯一入口</b>：重放世界里没有树也没有桥，
    /// <c>HeroBrainBridge.Wire</c> 那条路不存在，接管只能从 Journal 里读回来。
    /// 录制的起点是运行中的世界时，录制端会把「开录那一刻已经接管了谁」补成起点那一笔。</para>
    ///
    /// <para>幂等：接管表里已经有他时重写一遍 <c>(None, null)</c> 是同一个值，不产生差别；
    /// 但**会清掉他上一条意图** —— 所以只在开始那一步写它，别在中间补。</para>
    /// </summary>
    private static bool DelegateHeroToBrain(
        LogicWorld world, string args, out string canonicalArgs, out string reason)
    {
        canonicalArgs = args;
        reason = string.Empty;
        if (!ReplayArgs.Split(args, 1, out var fields) || fields[0].Length == 0)
        {
            return No(ref reason, $"DelegateHeroToBrain 参数要 1 段（英雄 id），收到「{args}」");
        }

        world.DelegateHeroToBrain(fields[0]);
        return world.BrainIntentOf(fields[0]) >= 0;
    }

    /// <summary>
    /// 树写回意图那一条：<c>英雄id|意图名|目标id</c>（无目标写 <c>-</c>）。
    ///
    /// <para><b>为什么它也进词表</b>：树住在表现层（<c>HeroBrainBridge</c> + <c>ai/hero_brain.gd</c>），
    /// 测试世界不链那几件、也没有树。所以「这一步树说了什么」如果不进 Journal，
    /// 活局录的那一份在离线就重放不出来 —— 同一粒种子、同一个步号，一个世界有树在发令、
    /// 另一个世界没有，两边走的是两条决策路（实测第 28 步就差出 1.12 米）。</para>
    ///
    /// <para><b>意图记枚举名不记数字</b>：与营建的 kind 同款口径（别名折成唯一那个 token）。
    /// 读不出名字整条判否、不降级 —— 「意图没接管」这一档要由世界那条（<c>ok=false</c>）说，
    /// 不能由本层折成某个默认意图。</para>
    /// </summary>
    private static bool ReportBrainIntent(
        LogicWorld world, string args, out string canonicalArgs, out string reason)
    {
        canonicalArgs = args;
        reason = string.Empty;
        if (!ReplayArgs.Split(args, 3, out var fields))
        {
            return No(ref reason, $"ReportBrainIntent 参数要 3 段（英雄id|意图名|目标id），收到「{args}」");
        }

        if (fields[0].Length == 0)
        {
            return No(ref reason, $"ReportBrainIntent 第 1 段（英雄 id）是空串，收到「{args}」");
        }

        if (!Enum.TryParse<BrainIntent>(fields[1], ignoreCase: true, out var intent)
            || int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            return No(ref reason,
                $"ReportBrainIntent 第 2 段「{fields[1]}」不是意图名（{string.Join(" / ", Enum.GetNames<BrainIntent>())}）—— "
                + "不降级成某个默认意图（降级会把映射错误伪装成「世界没接管」）");
        }

        // 目标那一段：`-` 与空串都是「没给」（照 BuildingPayload 那一档的缺省写法）。
        var target = fields[2] is "-" ? string.Empty : fields[2];
        canonicalArgs = ReplayArgs.Join(fields[0], intent.ToString(), fields[2]);
        world.ReportBrainIntent(fields[0], (int)intent, target);

        // 世界那条只在「这名英雄在接管表里」时才落笔（不在册 = 不是一次写），
        // 所以 ok 读的是落笔后的真值，不自行假定成功。
        return world.BrainIntentOf(fields[0]) >= 0;
    }

    // ---- 营建：薄适配到公共建造模块那一份词表（D34「一份表、两处问」）----

    /// <summary>
    /// 营建这一发：<b>参数次序与缺省值全是 <see cref="ConstructionVerbs"/> 那一份</b>
    /// （蓝图 id | x | y | 方向 | 建筑 id | 工期 | 载荷），本方法只做两件原型自己的事：
    /// ① 第 1 段的<b>方言映射</b>（界面短名 / 游侠公会旧名 → 枚举名，与其余动词同一份规范化口径），
    /// 读不出就整条失败、不降级；② 载荷那一段由原型编码（模块存而不读，D31）。
    ///
    /// <para>词表本体一份都不在这里另抄：竖线切分、缺省段（<c>-</c> 与空串 = 没给）、数值解析、
    /// 载荷段位置全走模块那份 <see cref="ConstructionVerbs.Parse"/>。工期缺省 = 蓝图 0（本原型
    /// 全部营建都是即时建成），载荷缺省 = 照种类给（<see cref="BuildingPayload.DefaultFor"/>）。</para>
    ///
    /// <para><b>永不抛</b>：模块那侧把「作者错误」当异常抛（参数段读不出、动词不接），这一层
    /// 统一接成 <c>false</c>（上面那段约定第一条）—— 抛出去会让「录进去的东西」有一类重放不了。</para>
    /// </summary>
    private static bool PlaceBuilding(LogicWorld world, string args, out string canonicalArgs, out string reason)
    {
        canonicalArgs = args;
        reason = string.Empty;

        // 第 1 段是本原点的种类方言（其余各段由模块那份表解析）。切一次只为读第 1 段。
        var fields = args.Split(ReplayArgs.Separator);
        if (!TryParseBuildingKind(fields[0], out var kind))
        {
            return No(ref reason,
                $"PlaceBuilding 参数第 1 段「{fields[0]}」不是建筑种类（枚举名 / 界面短名 / 游侠公会旧名），" +
                "不降级成某个种类 —— 降级会把映射错误伪装成「逻辑层拒绝」");
        }

        if (fields.Length < 3
            || !ReplayArgs.TryNumber(fields[1], out var x)
            || !ReplayArgs.TryNumber(fields[2], out var y))
        {
            return No(ref reason, $"PlaceBuilding 参数至少要 3 段（种类|x|y），收到「{args}」");
        }

        // 规范化形态（入录的就是它）：第 1 段记枚举名，第 4~7 段按模块那份表补齐
        // （方向恒 0 / 建筑 id 不记 / 工期缺省 / 载荷照种类）。
        // 建筑 id 与工期不记进 Journal 的理由：id 照本类的编号规则在重放端逐字复现
        // （同一串命令 → 同一串序号），工期本轮全是蓝图 0。
        canonicalArgs = Args(kind, new Vector2((float)x, (float)y));

        // **注入用的是调用方给的那一份**（只把第 1 段换成枚举名），不是规范化形态 ——
        // 调用方多给的那几段照样要过模块那份表的解析，读不出就整条失败。拿规范化形态去注入
        // 等于把调用方写坏的那一段悄悄丢掉，那正是「静默折成」要防的病。
        var forwarded = new List<string>(fields) { [0] = LogicWorld.BlueprintIdOf(kind) };
        try
        {
            var outcome = ConstructionVerbs.Dispatch(new InfinityLordConstructionSink(world),
                ConstructionVerbs.Place, ReplayArgs.Join(forwarded.ToArray()));
            return outcome.Ok;
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException
                                      or NotSupportedException or ArgumentException or OverflowException)
        {
            return No(ref reason, $"PlaceBuilding 注入失败（{ex.Message}）");
        }
    }

    /// <summary>判否并记下原因。成功路径一律不经过它。</summary>
    private static bool No(ref string reason, string why)
    {
        reason = why;
        return false;
    }

    /// <summary>
    /// 建筑种类解析 —— 接受并集：枚举名（忽略大小写）+ 5 个界面短名 + 游侠公会旧名。
    ///
    /// `guild_ranger` 是游侠公会的旧名，枚举已改叫 <c>GuildHall</c>；存量脚本里还留着它，
    /// 所以作为显式别名继续认（不认就要重录一批脚本），但它现在写在词表里，
    /// 而不是藏在某个 Main 的 switch 分支中。
    ///
    /// 解析不出就返回 false 让整条命令失败 —— **不降级**。降级成某个不可放置的种类会让
    /// 拼错的 kind 伪装成「逻辑层拒绝」，把映射错误藏起来。
    /// </summary>
    public static bool TryParseBuildingKind(string kind, out BuildingKind parsed)
    {
        switch (kind.ToLowerInvariant())
        {
            case "market": parsed = BuildingKind.Market; return true;
            case "tavern": parsed = BuildingKind.Tavern; return true;
            case "apothecary": parsed = BuildingKind.Apothecary; return true;
            case "guild_warrior": parsed = BuildingKind.GuildWarrior; return true;
            case "guild_ranger": parsed = BuildingKind.GuildHall; return true;
            default:
                // Enum.TryParse 吃数字串（"3" 会解析成 Market）—— 那等于给「拼错」找了个
                // 还能落建的解释，正是本方法不许降级要防的伪装。数字串不在接受集里，判否。
                if (int.TryParse(kind, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                {
                    parsed = default;
                    return false;
                }

                return Enum.TryParse(kind, ignoreCase: true, out parsed);
        }
    }

    // ---- args 格式化（入录与调用方拼串共用一份，坐标口径只有这一处；编码规则见 ReplayArgs）----

    public static string Args(string id, int amount) => ReplayArgs.Join(id, ReplayArgs.Int(amount));

    /// <summary>
    /// 意图 args：<c>英雄id|意图名|目标id</c>（无目标写 <c>-</c>）。名字不叫 <c>Args</c>：
    /// 那两个重载已经是「id + 数」与「id + 坐标」，同签名重载会让调用点两边都吃（编译期二义）。
    /// 写出口只有这一处 —— 表现层的桥与录制端的补账都从它拼串。
    /// </summary>
    public static string BrainIntentArgs(string heroId, BrainIntent intent, string targetId)
        => ReplayArgs.Join(heroId, intent.ToString(), targetId.Length == 0 ? "-" : targetId);

    /// <summary>接管 args：单段英雄 id（照 <c>UpgradeGuild</c> 那一档的裸 Id 写法）。</summary>
    public static string DelegateArgs(string heroId) => heroId;

    /// <summary>排布 args：<c>单位id|x|z</c>（坐标口径与营建同一份 <see cref="ReplayArgs.Number"/>）。
    /// 名字不叫 <c>Args</c>：营建那一档已经是 <c>Args(string, Vector2)</c>，同签名重载会让调用点两边都吃（编译期二义）。</summary>
    public static string PlaceUnitArgs(string unitId, Vector2 position)
        => ReplayArgs.Join(unitId, ReplayArgs.Number(position.X), ReplayArgs.Number(position.Y));

    public static string Args(int value) => ReplayArgs.Int(value);

    /// <summary>
    /// 营建 args 的<b>规范形态</b> —— 照 <see cref="ConstructionVerbs"/> 那一份表的列序补齐：
    /// <c>蓝图id|x|y|方向|建筑id|工期|载荷</c>。
    ///
    /// <para>方向恒 0（本原型无旋转需求）、建筑 id 留 <c>-</c>（照世界的编号规则发，那套规则在
    /// 重放端逐字复现）、工期留 <c>-</c>（缺省 = 蓝图自带那份，本轮全 0 = 即时建成）、
    /// 载荷写成 <c>阵营/等级/产出周期</c>（段内分隔符是斜杠，竖线是 args 分隔符、全表一份）。</para>
    /// </summary>
    public static string Args(BuildingKind kind, Vector2 position)
        => ReplayArgs.Join(
            LogicWorld.BlueprintIdOf(kind),
            ReplayArgs.Number(position.X),
            ReplayArgs.Number(position.Y),
            ReplayArgs.Int(PlaceOrientation),
            "-",
            "-",
            BuildingPayload.DefaultFor(kind).Encode());

    /// <summary>营建方向段：恒 0（本原型没有旋转需求；给了别的值量到 15° 档也折回 0）。</summary>
    private const int PlaceOrientation = 0;

    /// <summary>
    /// 营建 args 的**待规范化**拼法 —— kind token 原样透传，规范化交给
    /// <see cref="PlaceBuilding"/>（eval 直达口传进来的是界面短名，不该由它自己决定记什么）。
    /// </summary>
    public static string Args(string kindToken, Vector2 position)
        => ReplayArgs.Join(kindToken, ReplayArgs.Number(position.X), ReplayArgs.Number(position.Y));
}
