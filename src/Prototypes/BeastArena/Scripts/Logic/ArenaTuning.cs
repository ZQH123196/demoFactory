#nullable enable
using System;
using System.Globalization;

namespace DemoFactory.Prototypes.BeastArena.Logic;

// 大地图、甲板建造与地下土壤这三块的新版数值集中在这一个文件里。
// 与 Tuning.cs 并存：那份是切片 1/2 的数值表（沿固定折线边走边防那套），这份是
// 「大地图双城往返 + 甲板二十四格 + 地下八格」那套，两套的场地与账本都不是同一件事。

/// <summary>
/// 游戏时间换算与 24 小时罗盘时钟。<b>全仓只有这一处推导</b>。
///
/// <para><b>换算的根是一条</b>：一个逻辑步 0.1 现实秒，1× 档下 1 游戏分钟 = 1 现实秒。
/// 于是 1 游戏分钟 = 10 步、1 游戏小时 = 600 步、1 游戏日 = 14400 步。
/// 倍率只加快现实时间流逝，不改游戏时间内的速率 —— 同一个游戏分钟内巨兽仍走 0.04 米。</para>
///
/// <para><b><see cref="GameClock"/> 只有步号这一列</b>，没有小时也没有分钟，
/// 所以罗盘时钟（开局 <c>Day 1 · 06:00</c>，随游戏分钟连续推进）在这一处从步号推出来。</para>
/// </summary>
public static class ArenaTime
{
    /// <summary>一个逻辑步折多少现实秒（「时间基准」：0.1 秒）。</summary>
    public const double SecondsPerStep = 0.1;

    /// <summary>一个游戏分钟折多少逻辑步。</summary>
    public const int StepsPerGameMinute = 10;

    /// <summary>一个游戏小时折多少逻辑步。</summary>
    public const int StepsPerGameHour = 600;

    /// <summary>一个游戏日折多少逻辑步。</summary>
    public const int StepsPerGameDay = 14400;

    /// <summary>一个游戏日有多少个游戏分钟。</summary>
    public const int GameMinutesPerDay = 1440;

    /// <summary>开局钟面时刻（分钟）：06:00。</summary>
    public const int StartMinuteOfDay = 360;

    /// <summary>走到 <paramref name="tick"/> 那一刻已经过了多少个游戏分钟。</summary>
    public static long GameMinutesAt(long tick) => tick / StepsPerGameMinute;

    /// <summary>走到 <paramref name="tick"/> 那一刻已经过了多少个游戏小时。</summary>
    public static long GameHoursAt(long tick) => tick / StepsPerGameHour;

    /// <summary>走到 <paramref name="tick"/> 那一刻已经过了多少个游戏日。</summary>
    public static long GameDaysAt(long tick) => tick / StepsPerGameDay;

    /// <summary>钟面上的第几天（开局是第 1 天）。</summary>
    public static int DayAt(long tick)
        => (int)(1 + ((GameMinutesAt(tick) + StartMinuteOfDay) / GameMinutesPerDay));

    /// <summary>钟面上当天已过的分钟数（0..1439）。</summary>
    public static int MinuteOfDayAt(long tick)
        => (int)((GameMinutesAt(tick) + StartMinuteOfDay) % GameMinutesPerDay);

    /// <summary>罗盘时钟那一行文本（invariant，「double 与不变文化」）。</summary>
    public static string CompassAt(long tick)
    {
        var minute = MinuteOfDayAt(tick);
        return string.Format(CultureInfo.InvariantCulture, "Day {0} · {1:00}:{2:00}",
            DayAt(tick), minute / 60, minute % 60);
    }
}

/// <summary>大地图、甲板建造与地下土壤的新版数值（距离米、速度米每现实秒、时间按游戏单位）。</summary>
public static class ArenaTuning
{
    // ---- 大地图 ----

    /// <summary>巨兽在大地图上的寻路半径（米）。0 合法：它是那只唯一在场的单位。</summary>
    public const double BeastRadiusMeters = 0.0;

    /// <summary>
    /// 巨兽基础移速（米每现实秒）。原样填进移速口，<b>不换算成米每游戏秒</b>。
    ///
    /// <para><b>0.4 这个数怎么来的</b>：两城相距 240 米，设计的口径是「基础移速 = 两城距离 ÷ 10 游戏小时」，
    /// 于是 240 米 ÷ 10 游戏小时 = 0.4 米每现实秒（1× 档下 1 游戏小时 = 600 现实秒）。
    /// 落到一步：1 逻辑步 0.1 现实秒，每步走 0.04 米。</para>
    /// </summary>
    public const double BeastSpeedMetersPerSecond = 0.4;

    /// <summary>
    /// 到达容差（米）。格长 1.0 米，取 1.0 米 = 一格。
    ///
    /// <para><b>为什么不能更小</b>：1× 档下每逻辑步走 0.04 米，容差要大于一步，
    /// 否则巨兽在到达点两侧来回迈步却永远判不到点。新值 1.0 米是新格长的 1 倍、每步位移的 25 倍。</para>
    /// </summary>
    public const double ArriveToleranceMeters = 1.0;

    /// <summary>
    /// 出界那一发的到达容差（米）。比 <see cref="ArriveToleranceMeters"/> 紧一档。
    ///
    /// <para><b>为什么这一发要单独一档</b>：出界目标是圆盘外那个径向点
    /// （<see cref="BigMapGrid.OutboundTargetRadiusMeters"/> 米），巨兽在目标前就停；
    /// 用 1.0 米那一档容差，它会停在界内那 0.5 米上 ——
    /// 它永远不算出界，魂石也就永远不扣。出界这一发要的就是「真的跨出去」。</para>
    /// </summary>
    public const double OutboundArriveToleranceMeters = 0.05;

    /// <summary>
    /// 一阶巨兽的<b>体宽的一半</b>（米）—— 停靠时兽身横过来，正对城门的正是这一档。
    ///
    /// <para><b>实测值</b>：巨兽模型 <c>Beast.glb</c> 占地 <b>52.93（X）× 89.58（Z）</b> 米 ——
    /// X 向那条短边是它的体宽，Z 向那条长边是它的长度（含吻含尾）。
    /// 体宽的一半 = 52.93 ÷ 2 = <b>26.465</b>，这一格写体检单上取整过的 <b>26.45</b>
    /// （52.9 ÷ 2），差 0.015 米。表现层每帧按 AABB 量同一个数（读数见 <c>levitation</c>
    /// 那一行的 <c>beastHalfX</c>），量到的比这一档宽出容差就当场报错。</para>
    /// </summary>
    public const double BeastHalfWidthTier1Meters = 26.45;

    /// <summary>
    /// 二阶巨兽的<b>体宽的一半</b>（米）。
    ///
    /// <para><b>为什么取三套模型的最大档</b>：二阶三套形态的 X 向实测半宽各不相同 ——
    /// Original 30.000 / Desert 26.500 / Glutton 29.152 米（实测出处
    /// <c>docs/prototypes/beast-arena/model-review.md</c>）。停靠点由这一个数外推，
    /// 取最大档保证进阶后无论切到哪一套形态兽身都不压城；
    /// 比余量窄的形态只是在城门外多留一道缝。</para>
    /// </summary>
    public const double BeastHalfWidthTier2Meters = 30.00;

    /// <summary>
    /// 位阶 → 兽的体宽一半（唯一推导口）—— 停靠点、航线目标、到达判定与攻城距离都读这一档
    /// （见 <see cref="MapPlace.StopPoint(BeastTier)"/>）。
    /// 三阶模型还没上体检单：问到三阶的半宽当场抛，不兜一个没定过的数。
    /// </summary>
    public static double BeastHalfWidthFor(BeastTier tier) => tier switch
    {
        BeastTier.Tier1 => BeastHalfWidthTier1Meters,
        BeastTier.Tier2 => BeastHalfWidthTier2Meters,
        _ => throw new NotSupportedException(
            $"位阶 {tier} 没有定过兽半宽：三阶模型还没上体检单，档位表只到二阶"),
    };

    /// <summary>
    /// 城模型沿航线法线那一个方向的半深（米）。
    ///
    /// <para><b>实测值</b>：城模型 <c>CityNode.glb</c> 占地 <b>108.00（X）× 96.00（Z）</b> 米，
    /// 法线方向（Z 向）那一个半深 = 96 ÷ 2 = <b>48</b>。
    /// 表现层每帧按 AABB 量同一个数（读数见 <c>levitation</c> 那一行的 <c>cityHalfZ</c>），
    /// 量到的与这一格对不上超过 0.05 米就当场报错。</para>
    ///
    /// <para><b>它管什么</b>：城模型中心 = 城门沿外法线反推这么远（<see cref="MapPlace.CityCenter"/>），
    /// 于是城体整个在城门后面，城门落在模型朝航线那一侧的边缘上。</para>
    /// </summary>
    public const double CityHalfDepthMeters = 48.0;

    // ---- 饱食度 ----

    /// <summary>饱食度开局值（取值 0..100）。</summary>
    public const double StartingSatiation = 80.0;

    /// <summary>饱食度上限。</summary>
    public const double SatiationMax = 100.0;

    /// <summary>每游戏小时的自然衰减（-4）。</summary>
    public const double SatiationDecayPerGameHour = 4.0;

    /// <summary>喂食的换算（手动与 NPC 同一口径）：这么多食物换 1 点饱食度。</summary>
    public const double FoodPerSatiation = 5.0;

    /// <summary>NPC 喂食一次递的份数（工人或帮工每次 5 食物）。</summary>
    public const double NpcFeedFoodAmount = 5.0;

    /// <summary>自主觅食吃到这个饱食度就返航。</summary>
    public const double SatiationAfterMeal = 80.0;

    /// <summary>手动指定吞食目标的最大距离（米）：站在对象 10 米内才够得着。</summary>
    public const double ManualEatRangeMeters = 10.0;

    /// <summary>无目标返航后多久不再自动尝试（游戏分钟）。</summary>
    public const int ForageCooldownGameMinutes = 60;

    // ---- 生态对象 ----

    /// <summary>树木：饱食度收益。</summary>
    public const double TreeSatiationGain = 20.0;

    /// <summary>树木：消化进度收益。</summary>
    public const double TreeDigestionGain = 20.0;

    /// <summary>树木：吞食后原位再生的小时数。</summary>
    public const int TreeRegrowGameHours = 12;

    /// <summary>兽群：饱食度收益。</summary>
    public const double HerdSatiationGain = 40.0;

    /// <summary>兽群：消化进度收益。</summary>
    public const double HerdDigestionGain = 40.0;

    /// <summary>兽群：吞食后原位再生的小时数。</summary>
    public const int HerdRegrowGameHours = 24;

    /// <summary>兽群节点上摆几只兽（逻辑侧只记这一档读数）。</summary>
    public const int HerdSilhouetteCount = 3;

    /// <summary>矿石：饱食度收益。</summary>
    public const double OreSatiationGain = 10.0;

    /// <summary>矿石：消化进度收益。</summary>
    public const double OreDigestionGain = 60.0;

    /// <summary>多少点消化进度产出一单位魔晶。</summary>
    public const double DigestionPerCrystal = 100.0;

    // ---- 魔晶 ----

    /// <summary>魔晶基准售价（金币，再乘城市或空艇价格倍率）。</summary>
    public const double CrystalBaseValue = 500.0;

    /// <summary>一单位魔晶补几格可用法力。</summary>
    public const int CrystalManaRestore = 5;

    /// <summary>用魔晶补魔至少要缺几格（缺得少就不值得花一颗）。</summary>
    public const int CrystalManaMinGap = 5;

    /// <summary>魔晶落甲的指定位置（甲板逻辑坐标；世界落件时再吸到最近可站格）。</summary>
    public const double CrystalDropDeckX = 0.0;

    public const double CrystalDropDeckZ = 8.0;

    /// <summary>低于这个饱食度移速乘 <see cref="SlowSpeedFactor"/>。</summary>
    public const double SlowSatiationThreshold = 40.0;

    /// <summary>低于这个饱食度移速乘 <see cref="StarvingSpeedFactor"/>（保底，不饿死）。</summary>
    public const double StarvingSatiationThreshold = 10.0;

    /// <summary>饱食度偏低那一档的移速倍率。</summary>
    public const double SlowSpeedFactor = 0.7;

    /// <summary>饱食度见底那一档的移速倍率（40% 是地板）。</summary>
    public const double StarvingSpeedFactor = 0.4;

    // ---- 脱队觅食 ----

    /// <summary>低于这个饱食度才考虑脱队觅食。</summary>
    public const double DetourSatiationThreshold = 30.0;

    /// <summary>
    /// 离最近的可吞生态对象不超过这个距离（米）就脱队。设计口径是「航线 30 米内」。
    ///
    /// <para><b>30 这个数怎么来的</b>：整张图从 20 米见方放到 400 米见方，放大 20 倍；
    /// 觅食半径按同一倍数走（1.5 米 × 20 = 30 米），新格长 1 米，所以是 30 个新格。</para>
    /// </summary>
    public const double FoodSenseMeters = 30.0;

    /// <summary>原地进食耗时（游戏分钟）。</summary>
    public const int EatGameMinutes = 5;

    // ---- 出界 ----

    /// <summary>魂石开局值。</summary>
    public const double StartingSoulStones = 30.0;

    /// <summary>坐标落在世界边缘外侧期间，每满 1 游戏分钟扣掉的魂石。</summary>
    public const double SoulStoneDrainPerGameMinute = 2.0;

    // ---- 甲板建造 ----

    /// <summary>
    /// 甲板网格格长（米）。<b>2.0 米一格</b>：以人 1.7 米为锚，1 米一格时建筑比人还矮。
    /// 建造格与寻路格两处的格长都读它（<see cref="DeckBlueprints.CellSize"/>），不许各写一个。
    /// </summary>
    public const double DeckCellMeters = 2.0;

    /// <summary>一阶甲板格数（沿两轴各一份，正方形）：24 格 × 2 米 = 48 × 48 米。</summary>
    public const int DeckCellsTier1 = 24;

    /// <summary>
    /// 二阶甲板格数（沿两轴各一份，正方形）：28 格 × 2 米 = 56 × 56 米。
    /// <b>进阶完成那一刻生效，永久</b>（spec「二阶进阶」表「甲板」那一行）。
    /// </summary>
    public const int DeckCellsTier2 = 28;

    /// <summary>
    /// 位阶 → 甲板格数（唯一推导口）。三阶不在切片内：它的门槛恒假，
    /// 甲板档位也就没有第三档 —— 问到三阶当场抛，不兜一个没定过的数。
    /// </summary>
    public static int DeckCellsFor(BeastTier tier) => tier switch
    {
        BeastTier.Tier1 => DeckCellsTier1,
        BeastTier.Tier2 => DeckCellsTier2,
        _ => throw new NotSupportedException(
            $"位阶 {tier} 没有定过甲板格数：三阶不在本切片内，档位表只到二阶"),
    };

    /// <summary>甲板边长（米，沿两轴各一份）：格数 × 格长。</summary>
    public static double DeckSpanMeters(int cells) => cells * DeckCellMeters;

    /// <summary>一阶甲板边长（米）。地下土壤固定在开局那一档甲板之下，读这一格。</summary>
    public const double DeckSpanTier1Meters = DeckCellsTier1 * DeckCellMeters;

    /// <summary>建造格下界（列与行各一份，格数为偶数的那一档）。</summary>
    public static int BuildMinCol(int cells) => -cells / 2;

    /// <summary>建造格上界（含）。cells 格 = 上界 - 下界 + 1。</summary>
    public static int BuildMaxCol(int cells) => cells / 2 - 1;

    /// <summary>新开工期（游戏日）。</summary>
    public const double BuildDurationGameDays = 1.0;

    /// <summary>施工岗位的阈值上限（三名；0 岗建筑不靠岗位施工）。</summary>
    public const int BuildWorkerCap = 3;

    /// <summary>
    /// 这一栋要派满几名工人才恢复施工 —— <c>min(3, max(1, 岗位数))</c>。
    ///
    /// <para><b>为什么不是恒 3</b>：酒店只有 2 个岗位，恒 3 的话它永远派不满、永远卡在施工。
    /// 0 岗建筑（宿舍）不靠岗位施工，阈值给 1 只是为了「不要出现 0 人即开工」的第二套分支；
    /// 调用方对 0 岗单独判成「随时可施工」（见 <c>DeckConstruction.SyncBuildProgress</c>）。</para>
    /// </summary>
    public static int BuildWorkerThresholdFor(int posts)
        => Math.Min(BuildWorkerCap, Math.Max(1, posts));

    /// <summary>修房耗时（游戏分钟）。</summary>
    public const int RepairGameMinutes = 10;

    /// <summary>修房恢复的耐久。</summary>
    public const double RepairDurability = 50.0;

    /// <summary>修房耗的备件数。</summary>
    public const int RepairSpareParts = 1;

    /// <summary>法力总格数。<b>每游戏小时恢复 1 格那一档归动词表那一张工单，本文件只管占位。</b></summary>
    public const int ManaCapacity = 10;

    /// <summary>备件开局值。</summary>
    public const int StartingSpareParts = 3;

    // ---- 地下土壤 ----

    /// <summary>
    /// 土层数（layer = 1..2，每层深 3 米）。
    /// <b>切片只要两层</b>：第二层就是洞穴层，也是二阶门槛那一层 ——
    /// 再往下没有内容，留着第三层只会让门槛与洞穴各多一处永远走不到的分支。
    /// </summary>
    public const int SoilLayers = 2;

    /// <summary>象限数（sector = 0..3，2×2 排在甲板底下，每格对应甲板四分之一）。</summary>
    public const int SoilSectors = 4;

    /// <summary>单层深度（米）。</summary>
    public const double SoilLayerDepthMeters = 3.0;

    /// <summary>
    /// 单个象限边长（米）：一阶甲板边长 ÷ 2（2×2 象限）。
    /// <b>跟着一阶甲板边长走</b> —— 地下八格铺在开局那块 48 米见方的甲板底下；
    /// 二阶扩容只把甲板向四周展出一圈新格，土壤不跟着长（spec 未定义那圈底下的土层）。
    /// </summary>
    public const double SoilSectorSpanMeters = DeckSpanTier1Meters / 2.0;

    /// <summary>开挖单格要派满几名工人。</summary>
    public const int DigWorkerThreshold = 3;

    /// <summary>挖开单格的工时（游戏小时），派满 <see cref="DigWorkerThreshold"/> 人时的墙上时间。</summary>
    public const double DigGameHours = 1.0;

    /// <summary>每个挖开的格开几个采矿岗位。</summary>
    public const int MinePostsPerCell = 3;

    /// <summary>每人在岗产 1 矿石要多少游戏小时。</summary>
    public const double OreGameHoursPerMiner = 2.0;

    /// <summary>高危生产区里每人在岗额外产 1 特殊材料要多少游戏小时。</summary>
    public const double MaterialGameHoursPerMiner = 4.0;

    /// <summary>
    /// 已嵌入魔晶的格每人在岗产 1 特殊材料要多少游戏小时。
    /// <b>嵌入的收益就在这一档</b>：4 小时一件 → 2 小时一件（产出翻倍），
    /// 同时那一格永久停止回长。
    /// </summary>
    public const double EmbeddedMaterialGameHoursPerMiner = 2.0;

    /// <summary>原生洞穴固定长在第几层。</summary>
    public const int NativeCaveLayer = 2;

    /// <summary>
    /// 原生洞穴固定长在哪个象限。
    /// <b>单处洞穴</b>：切片只要一处，放在第一象限 —— 主线第一段就在那里开挖，
    /// 洞穴、采矿与嵌入三件事因此集中在同一格上，玩家不用在四个象限之间找洞。
    /// </summary>
    public const int NativeCaveSector = 0;

    /// <summary>单处洞穴里的原生怪只数（四只全灭才算清剿成功）。</summary>
    public const int NativeCaveMonsters = 4;

    /// <summary>洞穴清剿的时限（游戏小时）—— 只当保险，四只全灭会提前收场。</summary>
    public const double CaveGameHours = 1.5;

    /// <summary>第 1 层每游戏日回长的层数。</summary>
    public const double RegrowthLayersPerGameDayOuter = 1.0;

    /// <summary>第 2 层（最深那一层）每游戏日回长的层数 —— 自下而上，它先长。</summary>
    public const double RegrowthLayersPerGameDayDeep = 2.0;

    /// <summary>塌方时在镇游客吓跑的比例。</summary>
    public const double CollapseVisitorFleeShare = 0.20;

    /// <summary>塌方时全镇情绪的落差。</summary>
    public const double CollapseTownMoodDrop = 10.0;

    // ---- 巨兽位阶 ----
    //
    // 四张表都按**位阶号**索引（一阶 = 下标 1，二阶 = 2，三阶 = 3）。
    // 下标 0 是哨兵：填 NaN 而不是 1.0 —— 写错下标时会算出一个 NaN，
    // 而不是悄悄按一阶的数算过去。

    /// <summary>位阶最高档（三阶）。</summary>
    public const int BeastTierMax = 3;

    /// <summary>
    /// 二阶门槛要的挖掘层数。二阶还要「该象限洞穴已清剿」与「该格已嵌入魔晶」两项，
    /// 那两项住在 <see cref="ArenaWorld.GateMet"/> 的判定里，不在这个常数里。
    /// </summary>
    public const int Tier2GateLayer = 2;

    /// <summary>
    /// 三阶门槛的层数 —— <b>切片里永远到不了</b>：地下只有两层，第三层不存在。
    /// 三阶的代价表与位阶收益表都还留着（见 <see cref="TierCostByTarget"/>），
    /// 但通向它的门是关的：<see cref="ArenaWorld.GateMet"/> 对三阶恒回 false。
    /// 这样切片外的规则不会以「删一半」的形状留在代码里。
    /// </summary>
    public const int Tier3GateLayer = 3;

    /// <summary>进阶仪式时长（游戏小时）。</summary>
    public const int TierRitualGameHours = 1;

    /// <summary>
    /// 一个位阶的运行数值 —— 住在<strong>当前位阶</strong>那一档。
    ///
    /// <para><b>为什么打包成一档</b>：移速倍率与采矿倍率同属「人在第几阶」，
    /// 拆成两张按下标对齐的表，改一个位阶要两处一起改，
    /// 漏一处就是「二阶的采矿倍率按三阶算」那种错。</para>
    /// </summary>
    /// <param name="SpeedFactor">移速倍率。</param>
    /// <param name="MiningFactor">采矿速率倍率（矿石与特殊材料同档）。</param>
    public readonly record struct TierStats(double SpeedFactor, double MiningFactor);

    /// <summary>
    /// 进阶到某一位阶要付的三笔代价 —— 住在<strong>目标位阶</strong>那一档。
    ///
    /// <para><b>下标含义与 <see cref="TierStats"/> 不同</b>：那张表回答「人在第几阶」，
    /// 这张表回答「升到第几阶要付什么」。两件事都按位阶号下标，
    /// 混成一张表时同一个下标会同时管两件事，读代码的人要停下来想。</para>
    /// </summary>
    /// <param name="SoulStone">要花的魂石。</param>
    /// <param name="SpecialMaterial">要花的特殊材料。</param>
    /// <param name="Coin">要花的金币。</param>
    public readonly record struct TierCost(double SoulStone, double SpecialMaterial, double Coin);

    /// <summary>
    /// 位阶运行数值表（一位阶一档；下标 = 位阶号）。
    /// 0 位那档是空的（位阶号从 1 起），代价填 <see cref="double.NaN"/>。
    /// </summary>
    public static readonly TierStats[] TierStatsByTier =
    [
        new(double.NaN, double.NaN),
        new(1.00, 1.00),
        new(1.15, 1.25),
        new(1.30, 1.50),
    ];

    /// <summary>
    /// 进阶代价表（一位阶一档；下标 = 目标位阶号）。
    /// 一阶那一档是空的 —— 「进阶到一阶」这件事不存在，代价填 <see cref="double.NaN"/>。
    /// </summary>
    public static readonly TierCost[] TierCostByTarget =
    [
        new(double.NaN, double.NaN, double.NaN),
        new(double.NaN, double.NaN, double.NaN),
        new(40.0, 10.0, 2000.0),
        new(150.0, 30.0, 5000.0),
    ];

    /// <summary>当前位阶那一档的运行数值。</summary>
    public static TierStats StatsOf(BeastTier tier) => TierStatsByTier[(int)tier];

    /// <summary>进阶到目标位阶要付的三笔代价。</summary>
    public static TierCost CostOf(BeastTier target) => TierCostByTarget[(int)target];

    // ---- 形态 ----

    /// <summary>
    /// 荒漠态解锁所需的<b>累计</b>沙漠游戏分钟：240 分钟 = 4 游戏小时。
    /// <b>累计不因离开沙漠而清零</b>（旧口径是「连续」，切片改成累计）。
    /// </summary>
    public const long DesertFormDwellGameMinutes = 240;

    /// <summary>暴食态解锁所需的累计有效喂食（食物）：400。</summary>
    public const double GluttonFormFedTotalFood = 400.0;

    /// <summary>荒漠态在沙漠带里的饥饿衰减倍率（每小时 4 → 2）。</summary>
    public const double DesertFormSatiationDecayFactor = 0.5;

    /// <summary>荒漠态吃矿石的饱食收益倍率。</summary>
    public const double DesertFormOreSatiationFactor = 2.0;

    /// <summary>荒漠态吃树木 / 兽群的饱食收益倍率。</summary>
    public const double DesertFormOtherSatiationFactor = 0.5;

    /// <summary>暴食态吞食的饱食与消化倍率。</summary>
    public const double GluttonFormEatFactor = 1.5;

    /// <summary>暴食态的饥饿衰减倍率。</summary>
    public const double GluttonFormHungerDecayFactor = 1.5;

    // ---- 人口（三类镇民） ----

    /// <summary>开局工作人员数。</summary>
    public const int StartingWorkers = 8;

    /// <summary>开局角斗士数。</summary>
    public const int StartingGladiators = 3;

    /// <summary>开局常驻自由人数（无薪水、住宿舍）。</summary>
    public const int StartingFreePersons = 1;

    /// <summary>每人每日耗的食物：三类人一样，全镇口粮 = 在册人数 × 1。</summary>
    public const double FoodPerPersonPerGameDay = 1.0;

    /// <summary>
    /// 三个日薪（金币，每游戏日）：工作人员 2 / 角斗士 3 / 自由人 1。
    ///
    /// <para><b>数从哪儿来</b>：spec §「账本面板 · 发薪」那一行 ——
    /// 「工人 2 / 角斗士 3 / 自由人 1 金每游戏日」；这一行同时把旧值 3 / 5 顶掉。
    /// 这三个数是工资表的<b>唯一出处</b>：日结累计应发（<see cref="ArenaWorld.WageBill"/>）、
    /// 账本面板上那两行读数与测试都取它，别处不再写一份。</para>
    /// </summary>
    public const double WorkerDailyWage = 2.0;

    /// <summary>角斗士日薪（金币，每游戏日）。</summary>
    public const double GladiatorDailyWage = 3.0;

    /// <summary>自由人日薪（金币，每游戏日）。自由人也领工钱，只是比工作人员少。</summary>
    public const double FreePersonDailyWage = 1.0;

    /// <summary>工作人员移速（米每现实秒）。</summary>
    public const double WorkerSpeedMetersPerSecond = 1.3;

    /// <summary>角斗士移速（米每现实秒）。</summary>
    public const double GladiatorSpeedMetersPerSecond = 1.6;

    /// <summary>常驻自由人移速（米每现实秒）。</summary>
    public const double FreePersonSpeedMetersPerSecond = 1.3;

    /// <summary>召唤帮工的移速（同工作人员）。</summary>
    public const double HelperSpeedMetersPerSecond = 1.3;

    /// <summary>游客移速（米每现实秒）。</summary>
    public const double VisitorSpeedMetersPerSecond = 1.2;

    /// <summary>敌人移速（米每现实秒）。</summary>
    public const double EnemySpeedMetersPerSecond = 1.5;

    /// <summary>开工时三项本事的初值。</summary>
    public const double StartingStat = 20.0;

    /// <summary>三项本事各自的上限。</summary>
    public const double StatMax = 100.0;

    /// <summary>训练场每一游戏小时加的近战。</summary>
    public const double MeleeTrainingPerGameHour = 2.0;

    /// <summary>靶场每一游戏小时加的远程。</summary>
    public const double RangedTrainingPerGameHour = 2.0;

    /// <summary>马场每一游戏小时加的骑术。</summary>
    public const double RidingTrainingPerGameHour = 2.0;

    /// <summary>宿舍床位（只管常驻自由人住哪，不是招募闸门）。</summary>
    public const int DormitoryBeds = 20;

    /// <summary>工坊每产 1 件备件耗的矿石。</summary>
    public const double WorkshopOrePerSparePart = 5.0;

    /// <summary>工坊每产 1 件备件耗的金币。</summary>
    public const double WorkshopCoinPerSparePart = 20.0;

    /// <summary>工坊每产 1 件备件要的工时（游戏小时）。</summary>
    public const double WorkshopGameHoursPerSparePart = 8.0;

    /// <summary>酒店基础消费（金币每次）。</summary>
    public const double HotelBaseSpend = 10.0;

    /// <summary>饭店基础消费（金币每次）。</summary>
    public const double RestaurantBaseSpend = 5.0;

    // ---- 游客 ----

    /// <summary>入店消费一次占多少游戏分钟（饭店与酒店同一档）。</summary>
    public const int VisitorEatGameMinutes = 30;

    /// <summary>看一场比赛占多少游戏分钟（1.5 游戏小时）。</summary>
    public const int VisitorWatchGameMinutes = 90;

    /// <summary>每名游客的开局预算（金币）。</summary>
    public const double VisitorBudgetGold = 15.0;

    /// <summary>停留时长下界（游戏小时）。</summary>
    public const int VisitorStayMinGameHours = 2;

    /// <summary>停留时长上界（游戏小时）。</summary>
    public const int VisitorStayMaxGameHours = 4;

    /// <summary>游客池每游戏日的回速。</summary>
    public const int VisitorRegenPerGameDay = 25;

    /// <summary>
    /// 每 1 个游戏分钟最多放几个新客上甲板（池里有多少都不在这一格放完）。
    ///
    /// <para><b>为什么是 0.5 而不是 1（2026-10-15 实测）</b>：游客池是<b>每一趟停靠的一份预算</b> ——
    /// 停稳时回满到城档上限（贫城 80、富城 150），本趟不再补；每游戏日那 25 那一档回速只在这趟之后才起作用。
    /// 游客停留 2..4 游戏小时，于是这一档真正决定的是「那份预算摊在多长的停留里」：
    /// 取 1 时贫城那 80 个名额 80 个游戏分钟就放完，一趟 3 游戏小时（180 个游戏分钟）的停靠里
    /// 后 100 分钟街上一个人没有 —— 办赛没观众、轮结评价读到的只会是「在镇 0 人」那一档差评；
    /// 取 4 更糟：名额 20 个游戏分钟就放光，实测拔锚那一刻在镇 0 人。
    /// 取 0.5 时 80 个名额摊在 160 个游戏分钟上，与停留时长同一量级，一趟停靠从头到尾街上都有人。
    /// <b>城档上限与每游戏日回速都不受这一档影响</b> —— 它只管那份预算放得多快。</para>
    /// </summary>
    public const double VisitorEnterPerGameMinute = 0.5;

    /// <summary>游客进店的到达容差（米）。</summary>
    public const double VisitorArriveToleranceMeters = 0.05;

    /// <summary><c>SetPricing</c> 三个倍率的下界。</summary>
    public const double PricingMinMultiplier = 0.5;

    /// <summary><c>SetPricing</c> 三个倍率的上界。</summary>
    public const double PricingMaxMultiplier = 2.0;

    /// <summary>游客从浮桥上甲板的落点（甲板逻辑坐标，米）。</summary>
    public const double PontoonX = 0.0;

    public const double PontoonZ = 11.0;

    // ---- 搬运队列与魔法电梯 ----

    /// <summary>搬运队列的三级优先级，数字越小越优先（尸体 &gt; 备件 &gt; 地面散货）。</summary>
    public const int HaulLevelCount = 3;

    /// <summary>魔法电梯每多少游戏分钟结算一次。</summary>
    public const int ElevatorGameMinutes = 10;

    /// <summary>魔法电梯每次结算几名（人先货后）。</summary>
    public const int ElevatorSlotsPerSettlement = 4;

    /// <summary>搬运一件物的到达容差（米）。</summary>
    public const double HaulArriveToleranceMeters = 0.05;

    /// <summary>一具尸体在场上摆多久算「没搬走」（用于尸体负面情绪圈；真值是它在不在点位物表里）。</summary>
    public const double CorpseAuraRadiusMeters = 2.0;

    // ---- 日常走位 ----

    /// <summary>
    /// 日常走位每多少个游戏分钟换一次去处（镇民在镇上的例行往返那一档）。
    ///
    /// <para><b>挂在整游戏小时上</b>（60 个游戏分钟 = 1 个游戏小时）：与情绪回升、采矿、
    /// 日结那几摊同一批刻度，镇民换去处这件事在钟面上读得出是「换班」，
    /// 而不是一个说不清什么时候发生的漂移。</para>
    ///
    /// <para><b>排在走位之前那一段的最前头</b>（<c>ArenaWorld.OnBeforeStep</c>）：
    /// 那一发发的是移动令，寻路层要到本步那次脉冲才真的挪人，
    /// 所以同一格上排在后面的读数（情绪回升那一档读的是上一部同步下来的位置）不受这一发影响。</para>
    /// </summary>
    public const int DailyRoundGameMinutes = 60;

    /// <summary>
    /// 日常换去处那一档按名单序号错开的步数（<b>不让全镇在同一步换</b>）。
    ///
    /// <para><b>为什么必须错开</b>：改去处就是重新求解一次甲板那张网，而求解预算
    /// <c>StepSolveExpansions</c> 是<b>那张网逐步共享</b>的。全镇 12 个人在同一步各换一次去处，
    /// 排在预算后面的那几位直接拿到「目标不可达」——症状是那些人卡在原地、
    /// 读数是「地形无路」，而甲板上其实一格阻挡都没有（实测：576 格全通、净空全够）。</para>
    ///
    /// <para>错开量 × 开局 12 名镇民仍塞得进一个游戏小时（12 × 12 = 144 步），
    /// 所以「每小时每人换一次去处」这一档没变，变的只是同一分钟内谁先谁后。</para>
    /// </summary>
    public const int DailyRoundStaggerSteps = 12;

    /// <summary>没有岗位的镇民在「去吃饭」那一档里占的比例（0..1，其余那一档是去休息）。</summary>
    public const double DailyMealShare = 0.5;

    /// <summary>日常走位的到达容差（米）。与搬运那一档同一档：走到门口那一格就停。</summary>
    public const double DailyArriveToleranceMeters = 0.05;

    // ---- 情绪 ----

    /// <summary>情绪下界。</summary>
    public const double MoodMin = -100.0;

    /// <summary>情绪上界。</summary>
    public const double MoodMax = 100.0;

    /// <summary>尸体负面情绪圈里每人每游戏小时的落差。</summary>
    public const double MoodCorpseAuraPerGameHour = -20.0;

    /// <summary>
    /// 欠薪每人每游戏日掉的情绪。
    ///
    /// <para><b>与 <see cref="LoyaltyDailyUnpaid"/> 同一个数</b>（−5）：spec §「账本面板 · 发薪」
    /// 那一行把「欠薪惩罚」定成 −5，issue 里写的是「替换 <c>ArenaTuning</c> 现值 3 / 5 与欠薪 −15；
    /// 改为 2 / 3 / 1 与 −5」—— 两档欠薪数一起换，欠一天就是 5 点。
    /// 情绪那 5 点抵不过每游戏小时 +2 的自然回升（一天 +48），所以它不压过忠心那一档。</para>
    /// </summary>
    public const double MoodUnpaidWagePerGameDay = -5.0;

    /// <summary>情绪每游戏小时的自然回升。</summary>
    public const double MoodRecoveryPerGameHour = 2.0;

    /// <summary>产出与招人待客的打折分母（情绪 -100 即半折）。</summary>
    public const double MoodFactorDivisor = 200.0;

    // ---- 忠心 ----

    /// <summary>忠心上界。</summary>
    public const double LoyaltyMax = 100.0;

    /// <summary>
    /// 日结时实发高于该角色市价，忠诚按这一档涨（+2/日）。
    ///
    /// <para><b>市价按角色取该角色的日薪</b>（角斗士 3 / 工作人员 2 / 自由人 1），
    /// 差额口径是「人均实发 − 该角色市价」：差额为正就走这一档。</para>
    /// </summary>
    public const double LoyaltyDailyAbove = 2.0;

    /// <summary>日结时持平记的增减。</summary>
    public const double LoyaltyDailyLevel = 0.0;

    /// <summary>
    /// 日结时实发低于该角色市价这一档：差额不到 −3 金（也就是低于市价 3 金以上），
    /// 忠诚按这一档掉（−3/日）。
    ///
    /// <para><b>当前数值组合下走不到（留着的休眠规则）</b>：实发是人均实发（≥ 0），
    /// 市价最高是角斗士日薪 3 金，所以差额 ≥ −3，永远到不了 −3 以下。
    /// 规则与判据都留着 —— 数值改了它自己会活过来，不静默删。</para>
    /// </summary>
    public const double LoyaltyDailyBelow = -3.0;

    /// <summary>
    /// 欠薪一天记的忠诚落差。
    ///
    /// <para><b>数从哪儿来</b>：spec §「账本面板 · 发薪」那一行 ——
    /// 「欠一天忠心 −5」（旧值 −15 顶掉）。欠薪天数每多一天就按这一档扣一次，
    /// 扣到 0 为止（<see cref="ArenaTuning.LoyaltyMax"/> 是上界，0 是下界）。</para>
    /// </summary>
    public const double LoyaltyDailyUnpaid = -5.0;

    /// <summary>低于这个忠诚值就按日掷跑。</summary>
    public const double LoyaltyFleeThreshold = 30.0;

    /// <summary>低于阈值时每人每游戏日逃跑概率（由逻辑种子派生，重放逐字可复现）。</summary>
    public const double LoyaltyFleeChancePerGameDay = 0.05;

    /// <summary>忠诚落在这个区间（不含端点之外那一侧按区间内算）战力乘这一档。</summary>
    public const double LoyaltyDiscountLow = 30.0;

    public const double LoyaltyDiscountHigh = 60.0;

    /// <summary>忠诚打折那一档的战力倍率（七折）。</summary>
    public const double LoyaltyPowerFactor = 0.7;

    // ---- 名声 · 收购 · 招募 ----

    /// <summary>名声下界。</summary>
    public const double ReputationMin = 0.0;

    /// <summary>名声上界。</summary>
    public const double ReputationMax = 1000.0;

    /// <summary>镇名声开局值。</summary>
    public const double StartingTownReputation = 100.0;

    /// <summary>个人名声开局值。</summary>
    public const double StartingPersonalReputation = 0.0;

    /// <summary>办赛门票收入乘 <c>1 + 镇名声 ÷ 这一档</c>。</summary>
    public const double TicketReputationDivisor = 500.0;

    /// <summary>好评 / 差评给镇名声的增减。</summary>
    public const double ReviewReputationDelta = 2.0;

    /// <summary>办赛一场给贡献读数最高的角斗士的个人名声。</summary>
    public const double MatchPersonalReputationGain = 5.0;

    /// <summary>收购报价的固定项。</summary>
    public const double StarOfferBase = 500.0;

    /// <summary>收购报价里个人名声的系数。</summary>
    public const double StarOfferReputationFactor = 10.0;

    /// <summary>收购报价里三项本事总和的系数。</summary>
    public const double StarOfferStatFactor = 5.0;

    /// <summary>卖明星角斗士要的个人名声门槛。</summary>
    public const double StarSellReputationFloor = 100.0;

    /// <summary>工作人员岗位上限（录取受它约束，满员按 FullCapacity 拒）。</summary>
    public const int WorkerPostCap = 30;

    /// <summary>角斗士岗位上限。</summary>
    public const int GladiatorPostCap = 20;

    /// <summary>每停靠一次最多招几个人（停靠抵达那一刻清零）。</summary>
    public const int RecruitPerDock = 3;

    // ---- 按游戏分钟自动战斗 ----

    /// <summary>办赛时长（游戏小时）。</summary>
    public const double MatchGameHours = 1.5;

    /// <summary>拔锚防守时长（游戏小时）。</summary>
    public const double DefenceGameHours = 2.0;

    /// <summary>竞技场西侧入场点相对大门点的偏移（甲板逻辑坐标，米）。</summary>
    public const double ArenaWestOffsetX = -2.5;

    public const double ArenaWestOffsetZ = 1.0;

    /// <summary>竞技场东侧入场点相对大门点的偏移。</summary>
    public const double ArenaEastOffsetX = 0.5;

    public const double ArenaEastOffsetZ = 1.0;

    /// <summary>在岗牧师驻的场边点相对竞技场大门点的偏移。</summary>
    public const double PriestEdgeOffsetX = -1.0;

    public const double PriestEdgeOffsetZ = 3.0;

    /// <summary>在岗牧师最多几名。</summary>
    public const int PriestMaxOnDuty = 2;

    /// <summary>牧师光环半径（米）：圈内挨的伤害乘 <see cref="PriestAuraFactor"/>。</summary>
    public const double PriestAuraRadiusMeters = 2.0;

    /// <summary>牧师光环那一档减伤倍率。</summary>
    public const double PriestAuraFactor = 0.5;

    /// <summary>拔锚防守敌人数基数。</summary>
    public const int DefenceEnemyBase = 20;

    /// <summary>拔锚防守每个向心环加的敌人。</summary>
    public const int DefenceEnemyPerRing = 4;

    /// <summary>普通 / 最终防守守住的固定魂石奖励（击杀魂石另算、照常保留）。</summary>
    public const double DefenceWinSoulStones = 10.0;

    /// <summary>敌人目标分段：每 10 人切一段（前 5 打镇政府、次 3 冲粮仓、末 2 打就近产出）。</summary>
    public const int DefenceTargetSegment = 10;

    /// <summary>拔锚防守的拦截半径（米）：只把在这个圈内的敌人纳入配对池。</summary>
    public const double InterceptionRadiusMeters = 4.0;

    /// <summary>最多几个围攻一个。</summary>
    public const int MaxSwarmAgainstOne = 2;

    /// <summary>交手结算的距离（米）。</summary>
    public const double HitRangeMeters = 2.0;

    /// <summary>伤害基数：<c>基数 × (攻方 ÷ 攻守之和) × 围攻人数</c>。</summary>
    public const double DamageBase = 10.0;

    /// <summary>对人战力的三项权重（近战 / 远程 / 骑术）。</summary>
    public const double HumanMeleeWeight = 0.5;

    public const double HumanRangedWeight = 0.3;
    public const double HumanRidingWeight = 0.2;

    /// <summary>对兽战力的三项权重（骑术 / 近战 / 远程）。</summary>
    public const double BeastRidingWeight = 0.5;

    public const double BeastMeleeWeight = 0.3;
    public const double BeastRangedWeight = 0.2;

    /// <summary>原生怪三项本事统一这个数（按对兽权重算战力）。</summary>
    public const double MonsterStat = 30.0;

    /// <summary>全员生命上限（生命值只住在单位自己身上）。</summary>
    public const double UnitMaxHp = 100.0;

    /// <summary>血量落到这个数或更低当场死（更轻的落法是重伤）。</summary>
    public const double DeathHpThreshold = -100.0;

    /// <summary>重伤者有在岗牧师时多久起身（游戏分钟）。</summary>
    public const int WoundRiseGameMinutes = 5;

    /// <summary>敌人进门口圈后每游戏分钟打掉的耐久。</summary>
    public const double EnemyDurabilityPerGameMinute = 1.0;

    /// <summary>敌人每抢一件先扣的金币。</summary>
    public const double EnemyStealGold = 10.0;

    /// <summary>抢满几件就退场。</summary>
    public const int EnemyStealLimit = 3;

    /// <summary>刷怪落点抖动（米）：按名单序号轮转，不掷点。</summary>
    public const double SpawnJitterMeters = 0.5;

    // ---- 攻破 · 战损 · 解救 ----

    /// <summary>攻破后按造价从高到低毁掉几栋普通建筑。</summary>
    public const int WarLossBuildings = 3;

    /// <summary>攻破后按名单序号掳走几人。</summary>
    public const int WarLossTakenPeople = 7;

    /// <summary>每名被掳者折多少金币（不留尸不出魂石）。</summary>
    public const double WarLossPersonValue = 100.0;

    /// <summary>仓库矿石与特殊材料各扣这一份（向下取整）。</summary>
    public const double WarLossGoodsShare = 0.30;

    /// <summary>矿石折价（金币每件）。</summary>
    public const double OreValue = 20.0;

    /// <summary>特殊材料折价（金币每件）。</summary>
    public const double MaterialValue = 100.0;

    /// <summary>债额下界。</summary>
    public const double DebtFloor = 500.0;

    /// <summary>债额折算倍数。</summary>
    public const double DebtFactor = 1.5;

    /// <summary>
    /// 旧停摆期的天数（ticket 06 起城破改为立即解救，不再走这段停摆）。
    /// 常量留着给读旧档 / 旧用例的读法用；运行期没有一处再把它排进时间轴。
    /// </summary>
    public const int StandstillGameDays = 3;

    /// <summary>城卫队解救后镇政府恢复到的耐久。</summary>
    public const double RescueTownHallDurability = 100.0;

    // ---- 账目 · 法力 · 通缉 ----

    /// <summary>开局金币。</summary>
    public const double OpeningCoin = 2000.0;

    /// <summary>开局食物。</summary>
    public const double OpeningFood = 500.0;

    /// <summary>开局矿石。</summary>
    public const double OpeningOre = 10.0;

    /// <summary>开局特殊材料。</summary>
    public const double OpeningSpecialMaterial = 2.0;

    /// <summary>法力每游戏小时恢复几格。</summary>
    public const int ManaRegainPerGameHour = 1;

    /// <summary>空艇成交价相对市价的系数（<c>两城物价系数均值 × 这一档</c>）。</summary>
    public const double AirshipPriceFactor = 1.2;

    /// <summary>一艘空艇的成交窗口（游戏分钟）。</summary>
    public const int AirshipWindowGameMinutes = 30;

    /// <summary>通缉阈值：总负债超过这个数就置通缉。</summary>
    public const double WantedDebtThreshold = 3000.0;

    /// <summary>攻破后第几个游戏日期满仍未还清就置通缉。</summary>
    public const int WantedGameDaysAfterBreach = 7;

    /// <summary>讨债队来一趟的间隔（游戏日）。</summary>
    public const int DebtCollectionIntervalGameDays = 7;

    /// <summary>传送阵单人价（金币）。</summary>
    public const double TeleportCoinPerPerson = 50.0;

    /// <summary>传送阵小货单价（金币每件）。</summary>
    public const double TeleportCoinPerSmallGood = 10.0;

    /// <summary>传送阵单次最多几件小货。</summary>
    public const int TeleportMaxSmallGoods = 5;

    /// <summary>教堂复活一具尸体耗的魂石。</summary>
    public const double ReviveSoulStones = 10.0;

    /// <summary>教堂复活要等的工时（游戏小时）。</summary>
    public const int ReviveGameHours = 1;

    /// <summary>教堂复活的是这个游戏小时内死的尸体。</summary>
    public const int ReviveWindowGameHours = 24;

    /// <summary>灵魂石入场加成：一具尸体自动入账这么多。</summary>
    public const double SoulStonesPerCorpse = 3.0;
}
