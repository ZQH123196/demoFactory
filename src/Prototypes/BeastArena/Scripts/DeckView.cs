#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DemoFactory.GugaFramework.Animation;
using DemoFactory.GugaFramework.Construction;
using DemoFactory.GugaFramework.I18n;
using DemoFactory.GugaFramework.Pathfinding;
using DemoFactory.Prototypes.BeastArena.Logic;
using DemoFactory.Prototypes.BeastArena.Show;
using DemoFactory.Prototypes.BeastArena.Ui;
using Godot;

namespace DemoFactory.Prototypes.BeastArena;

/// <summary>
/// 甲板的表现层 —— **只读**：从 <see cref="ArenaWorld"/> 读状态，把场景节点摆到位，绝不往世界里写东西
/// （「表现层只读」）。
///
/// <para><b>场景树</b>（spec 6 节「场景树布局与『先接后删』清理」）：
/// <code>
/// World                     大地图空间（逻辑坐标即世界坐标）
/// ├── Terrain               地形圆盘（512 米直径，程序化颜色 / 法线 / 粗糙度，y=0）
/// ├── Starfield              星空面（1600×1600 米，y=−2，固定种子生成的 2048×2048 星图）
/// ├── SideWall               圆盘边缘那 2 米高的侧壁（圆柱侧面，半径 256 米）
/// ├── Landmarks             CityNode ×4 + 生态对象占位
/// ├── Deck                  巨兽的承载者，位姿每个渲染帧连续
/// │   ├── BeastAnchor       巨兽锚点：三形态三件模型都挂在它下面（同一时刻只显示一件）
/// │   │   ├── Beast         原始态
/// │   │   ├── BeastDesert   荒漠态
/// │   │   ├── BeastGlutton  暴食态
/// │   │   └── LevitationBase 磁悬浮底座（静态件，骑在兽背上，local y=0）
/// │   └── Levitation        挂在**不参与动画**的 Deck 上，顶面恒定
/// │       ├── TownDeck      镇地面（静态件，local y=0）
/// │       ├── 十三件建筑     按建造册上的锚点摆，1:1 局部坐标
/// │       ├── 五类人形活物   位置读活物册，动作交给 ModelAnimator
/// │       ├── 三类点位物     尸体 / 地面散货 / 魔晶
/// │       ├── Pontoon        只在停靠期显示（甲板南侧边缘）
/// │       └── Airship        只在空艇成交窗口内显示（空船停靠区旁或甲板东侧上空）
/// └── （相机 rig 与太阳灯由 Main 建在这一层）
/// </code>
/// 关键在悬浮层那一层的挂法：它不是巨兽的子节点，所以巨兽的步态起伏传不到小镇；
/// 而小镇脚下恒定是镇地面那块平板，「像平地一样平稳」由此成立。
/// 兽背上那半（LevitationBase）是巨兽锚点的子节点，它与悬浮层之间那道 9.00 米的空就是磁悬浮场 ——
/// 镇地面那一件如今是一座**倒三角浮山**（顶面是平台，山体往下收到锥尖），
/// 场量的是「锥尖发光核 ↔ 底座顶面」那一段。</para>
///
/// <para><b>大地图那一侧一把尺子</b>：大地图逻辑坐标就是真实米（圆盘跨世界 −56..456 米），
/// 逻辑坐标直接当 3D 坐标用，没有第二把尺子。
/// 两城之间那 240 米与 <c>Beast.glb</c> 那 45 米同量级 —— 巨兽与地图的比例是对的。</para>
///
/// <para><b>位姿为什么每渲染帧算</b>：逻辑位姿只在步上更新（10 步/秒），渲染却是 30~60 帧/秒，
/// 只在 <see cref="Sync"/>（步上）刷位姿，画面上就是 30 帧里动 3 帧的台阶。
/// 现在 <see cref="SyncPose"/> 每个渲染帧都调，位姿于是连续。</para>
///
/// <para><b>地下那八格不挂 3D 网格</b>：照「灰盒全部走报数」，
/// 地下不画体量 —— 画了会从巨兽背部穿出来。原来的 HUD 第五行已改成「人口与岗位」，
/// 土壤那 8 格改由右侧土壤面板承担。</para>
/// </summary>
public partial class DeckView : Node
{
    /// <summary>
    /// 磁悬浮底座挂在巨兽身上的高度（米，相对巨兽模型原点）—— 那一格是**底面**。
    ///
    /// <para>**这一格是照着实测背脊定的，不是估的**（2026-10-15，用户：「让磁悬浮底座正确的贴合
    /// 巨兽的背部」）。逐件包围盒（离线校验器转储 <c>Beast.tscn</c>）量到的三段背脊：
    /// 桶身 <c>barrel</c> 顶面 <b>23.84</b> 米（占 z = −28.44..27.21）、臀肌 <c>haunch</c> <b>24.96</b> 米
    /// （z ≤ −15.44）、臀背 <c>rump</c> <b>27.20</b> 米（z ≤ −25.74）。
    ///
    /// <para>底座摆在 z=0、跨 z = −6.60..6.60，那一段**整个落在桶身上，背脊是平的 23.84 米** ——
    /// 于是一块平底就够贴合，不必为了高出的两段做台阶。取 <b>23.24</b>：底面沉进背脊 0.60 米。
    ///
    /// <para>原来那一格是 28.50 —— 盘底悬在背脊上方 <b>4.66 米</b>，画面上就是「一块饼搁在兽背上」。
    /// 沉 0.60 而不是贴着 0.00，是为了给巨兽走路那点起伏留余量：底座挂在兽节点上不跟骨架动，
    /// 背脊跟着脊柱骨转，那一层余量吃掉那份错动，差几厘米也不至于在盘沿露缝。</para>
    /// </summary>
    public const float LevitationBaseY = 23.24f;

    /// <summary>
    /// 悬浮场的高度（米）—— 底座顶面与浮山锥尖之间那段空。
    ///
    /// <para>取 <b>9.00</b>（原来 7.00）：浮山底下多了一座 26 米深的山，锥尖是那一头最尖的一点点，
    /// 场太窄时从侧视只看得见尖顶着底座，读不出「整座山浮在上面」。</para>
    /// </summary>
    public const float FieldGapMeters = 9.00f;

    /// <summary>
    /// 镇地面（浮山）自身高度（米）—— 源场景 <c>Tools/Rigs/Town/TownDeck.tscn</c> 从**锥尖底面**量到
    /// **镇面顶板**：五层岩体 24.00 + 锥尖 1.60 + 平台 1.20（离线校验器读数见下面注记那一行）。
    /// 改了造型忘了回填这一格，浮山要么埋进底座、要么整座飘高，间隙那一格立刻读成负数。
    /// </summary>
    public const float TownDeckHeight = 27.20f;

    /// <summary>
    /// 镇地面在**悬浮层局部系**里的高度（米）—— 那就是镇面：平台上那 48×48 米的顶板。
    /// 建筑、镇民、点位物、空艇一律摆在这一面上（见 <c>SyncBuildings</c> 与 <c>SyncUnits</c>）。
    /// </summary>
    public const float TownY = TownDeckHeight;

    /// <summary>
    /// 镇面在**甲板局部系**里的离地高度（米）。
    ///
    /// <para><b>由另外三格推出来，不单独给数</b>：底座顶面（<see cref="LevitationBaseY"/> +
    /// <see cref="LevitationBaseHeight"/>）＋ 悬浮场（<see cref="FieldGapMeters"/>）＋
    /// 浮山自身高度（<see cref="TownDeckHeight"/>）＝ 27.14 + 9.00 + 27.20 ＝ <b>63.34</b>。
    /// 原来那一格是手写的 40.00，造型一改就与底下那一段对不上（出图复核抓得到）。</para>
    /// </summary>
    public const float DeckY = LevitationBaseY + LevitationBaseHeight + FieldGapMeters + TownDeckHeight;

    /// <summary>
    /// 悬浮层根节点的离地高度（米）—— 浮山模型本地 y=0（锥尖发光核的底面）就在这一层。
    /// 它比 <see cref="DeckY"/> 低一个 <see cref="TownDeckHeight"/>。
    /// </summary>
    public const float LevitationY = DeckY - TownY;

    /// <summary>
    /// 磁悬浮底座自身高度（米）—— 源场景 <c>Tools/Rigs/Town/LevitationBase.tscn</c> 量出来的顶面高度
    /// （鞍座 1.20 + 第二级台阶 1.00 + 能量圈 0.85 + 内毂 0.85）。
    /// 改了造型忘了回填这一格，悬浮场那一段的宽度就与注释里那几个数对不上（出图复核抓得到）。
    /// </summary>
    public const float LevitationBaseHeight = 3.90f;

    /// <summary>
    /// 镇面在**甲板局部系**里的离地高度（米）—— 相机焦点高度那一格读它。
    /// 主体离地这么高，焦点还压平在 y=0 就等于镜头盯着它脚下（「TopDownRig.FocusHeightMeters」那一格）。
    /// </summary>
    public const float TownWorldY = DeckY;

    /// <summary>
    /// 地面顶面高度（米）。**这一格就是「兽与城同处一个平面」那句话里的那个平面**：
    /// 巨兽、城镇地标、食物点与地形拼图四样全部按 y=0 落地，模型自身底面 y=0 那一档正好落在这条面上。
    ///
    /// <para>不是造型，是<b>场地陈设</b> —— 与公共层技能试验场那块
    /// <c>SkillStageScene</c> 脚下的地网格同一族，所以不走 <c>Models/</c> 那一套
    /// （rig 源场景 → glb → 体检单）。判据是「这一块代表什么长什么样」：
    /// 地面与地形色块不代表任何东西，兽与城的造型仍然各自是真模型。</para>
    /// </summary>
    public const float GroundY = 0.0f;

    /// <summary>
    /// 地面中心的世界 X（米）—— 圆盘圆心。
    ///
    /// <para><b>不在这里另写一份</b>：圆心是逻辑层那一个数（<see cref="BigMapGrid.DiskCenterX"/>），
    /// 地形圆盘、星空面与侧壁三样都从它摆位 —— 逻辑层改了圆心而表现层还守着自己那份，
    /// 画面上的圆盘就与判界那一圈错开了。</para>
    /// </summary>
    public static float GroundCenterX => (float)BigMapGrid.DiskCenterX;

    /// <summary>地面中心的世界 Z（米）—— 同 <see cref="GroundCenterX"/>，读逻辑层那一个数。</summary>
    public static float GroundCenterZ => (float)BigMapGrid.DiskCenterZ;

    /// <summary>
    /// 星空面的边长（米）—— 1600 × 1600，铺在 y = −2。
    ///
    /// <para><b>为什么这么大</b>：它要盖住相机边界（−120..520）加上远端那一圈雾层 ——
    /// 星空面比视野小的话，画面里会出现一道「世界的边」，那比没有地面更假。</para>
    /// </summary>
    public const float StarfieldSpanMeters = 1600.0f;

    /// <summary>
    /// 星空面的离地高度（米）：<b>−2</b>，与侧壁的下沿齐平（侧壁从 y=0 落到 y=−2）。
    /// </summary>
    public const float StarfieldY = -2.0f;

    /// <summary>
    /// 侧壁高度（米）：从 y=0 落到 y=−2，圆盘边缘那 2 米高的一圈挡墙。
    /// </summary>
    public const float SideWallHeightMeters = 2.0f;

    /// <summary>
    /// 星空贴图的边长（像素）：2048 × 2048。
    /// </summary>
    public const int StarTexturePixels = 2048;

    /// <summary>
    /// 星空生成的固定种子 —— 同一颗种子 → 同一片星空。
    /// 贴图是运行时生成的，不落盘，所以「逐字一致」靠的是这颗种子不变。
    /// </summary>
    public const ulong StarfieldSeed = 20261020UL;

    /// <summary>星空里的星点数量（个）。</summary>
    public const int StarfieldStarCount = 6000;

    /// <summary>被抓起（<c>GrabEntity</c>）或法术停滞（<c>LiftBuilding</c>）那一档抬起的米数。</summary>
    public const float LiftHeight = 6.0f;

    /// <summary>
    /// 空艇停在甲板东侧上空时的高度（米，相对镇地面）。空艇实测高 7.20 米，
    /// 这一格得高过它才不是插进镇子里。
    /// </summary>
    public const float AirshipHoverHeight = 12.0f;

    /// <summary>
    /// 一个逻辑步折多少现实秒 —— 位姿外推那一格要拿它把「不足一步的补偿量」换算成米。
    /// 「时间基准」：这一步的换算全仓只有 <see cref="ArenaTime"/> 一处，这里只引用不重推。
    /// </summary>
    private const double SecondsPerStep = ArenaTime.SecondsPerStep;

    /// <summary>新单位模型每趟最多建几个（配合 <see cref="BindBudgetMs"/> 分帧）。</summary>
    private const int BindMaxPerCall = 4;

    /// <summary>新单位模型每趟最多花多少毫秒 —— 接不完的下一趟再来。</summary>
    private const double BindBudgetMs = 6.0;

    private Node3D _world = null!;
    private Node3D _landmarks = null!;
    private Node3D _deckRoot = null!;
    private Node3D _levitation = null!;

    /// <summary>巨兽锚点 —— 三形态三件模型都挂在它下面，同一时刻只显示一件。</summary>
    private Node3D _beastAnchor = null!;

    /// <summary>形态 × 位阶 → 那件模型的节点（六套，同一时刻只显示一件）。</summary>
    private readonly Dictionary<(BeastForm Form, BeastTier Tier), Node3D> _beastViews = new();

    /// <summary>形态 × 位阶 → 那件模型的动画器（六件各一个，构造即绑定）。</summary>
    private readonly Dictionary<(BeastForm Form, BeastTier Tier), ModelAnimator> _beastAnims = new();

    /// <summary>两阶的磁悬浮底座与浮山（位阶换模时切显隐，不重建小镇）。</summary>
    private Node3D? _levitationBase1;
    private Node3D? _levitationBase2;
    private Node3D? _townDeck1;
    private Node3D? _townDeck2;
    private BeastTier _beastTier = BeastTier.Tier1;

    /// <summary>表现导演：九类事件的三样（特效 / 音效 / 结果标记）都从它出去。</summary>
    private ShowDirector _show = null!;

    /// <summary>上一帧的建筑读数（判完工 / 受损 / 修好 / 拆除四种变化）。</summary>
    private sealed record BuildSeen(
        string BlueprintId, double Health, double MaxHealth, BuildState State,
        Vector3 At, int Width, int Height);

    /// <summary>上一帧的活物读数（判掉血 / 阵亡两种变化）。</summary>
    private sealed record UnitSeen(double Hp, UnitState State);

    private readonly Dictionary<string, BuildSeen> _buildSeen = new(StringComparer.Ordinal);

    /// <summary>上一帧的活物读数（判掉血 / 阵亡两种变化）。</summary>
    private readonly Dictionary<string, UnitSeen> _unitSeen = new(StringComparer.Ordinal);

    /// <summary>阵亡动画的到点时刻（现实毫秒）：到点再收走尸体模型。</summary>
    private readonly Dictionary<string, long> _deadAt = new(StringComparer.Ordinal);

    /// <summary>洞穴涌怪那一小段：新出现的原生怪补一条 <c>emerge</c>。</summary>
    private long _emergeUntilTick = -1;

    /// <summary>巨兽已经伏下（拔锚 / 防守开场）；重新开步时补一条 <c>get_up</c>。</summary>
    private bool _lying;

    /// <summary>当前形态的动画器 —— 换形态 / 换位阶时改指向，不重建任何东西。</summary>
    private ModelAnimator? _beastAnim;

    /// <summary>当前形态（默认原始态；<see cref="SetBeastForm"/> 改它）。</summary>
    private BeastForm _beastForm = BeastForm.Original;

    private Node3D? _pontoon;
    private Node3D? _airship;

    // ---- T14 地下与船位（造一次、只切显隐与摆位，不在每帧重建）----
    private Node3D? _touristShip;
    private Node3D? _soilBlockL1;
    private Node3D? _soilBlockL2;
    private Node3D? _cavernL1;
    private Node3D? _cavernL2;
    private Node3D? _caveMouth;
    private Node3D? _miningFace;
    private Node3D? _crystalSocket;
    private Node3D? _crystalEmbedded;

    private readonly Dictionary<string, Node3D> _buildingViews = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _buildingModels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Node3D> _ruinViews = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (Node3D View, ModelAnimator Anim)> _unitViews = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Node3D> _itemViews = new(StringComparer.Ordinal);
    private readonly HashSet<string> _lifted = new(StringComparer.Ordinal);

    private Label? _clockRow;
    private Label? _goalRow;
    private Label? _stationRow;
    private Label? _accountRow;
    private Label? _beastRow;
    private Label? _growthRow;
    private Label? _standingRow;
    private Label? _populationRow;

    // === 世界内标记（spec「世界内标记」四条；读数格按对接面清单「世界标记」表） ===

    /// <summary>一块名牌：节点 + 上一次上屏的文字（没变就不重写，Label3D 重设文字会重排版一次）。</summary>
    private sealed record Plate(Label3D Label, string Last);

    /// <summary>活物名牌（id → 牌）：职业与当前行为，挂在各自模型头顶。</summary>
    private readonly Dictionary<string, Plate> _unitPlates = new(StringComparer.Ordinal);

    /// <summary>点位物名牌：只给魔晶那一类挂（甲板上的魔晶要能在地面散货里读出来）。</summary>
    private readonly Dictionary<string, Plate> _itemPlates = new(StringComparer.Ordinal);

    /// <summary>生态对象名牌（id → 牌）：类别与可吞状态，挂在占位模型头顶。</summary>
    private readonly Dictionary<string, Plate> _ecoPlates = new(StringComparer.Ordinal);

    /// <summary>洞穴名牌：挂在镇政府门口那一格（<see cref="DeckGeometry.CaveSpawn"/>）上空。</summary>
    private Plate? _cavePlate;

    /// <summary>仓库名牌：挂在镇政府上空，报仓库里的魔晶数（「魔晶三种位置」的仓库那一档）。</summary>
    private Plate? _warehousePlate;

    /// <summary>最近一次 <see cref="Sync"/> 的那个世界 —— 查询口（格上的建筑）读它。</summary>
    private ArenaWorld? _syncedWorld;

    /// <summary>上一帧的位姿读数，给 <see cref="LevitationReadout"/> 与抖动排查用。</summary>
    private string _poseReadout = "(none)";

    /// <summary>巨兽上一帧的大地图逻辑坐标 —— 朝向由两次读数的差推出来。</summary>
    private NavPoint? _lastBeastAt;

    /// <summary>巨兽当前的朝向（3D 向量，Y 分量恒 0）。</summary>
    private Vector3 _beastHeading = Vector3.Forward;

    /// <summary>甲板当前的偏航（弧度）—— 目标偏航按一个上限转过去，转进车位是转出来的。</summary>
    private float _yaw;

    /// <summary>偏航定过没有。第一帧直接摆到目标偏航，不从零转起。</summary>
    private bool _yawKnown;

    /// <summary>偏航每秒最多转多少度 —— 兽转进车位那一档是转出来的，不是瞬移。</summary>
    private const float YawRateDegreesPerSecond = 90.0f;

    /// <summary>巨兽模型出厂朝向与「-Z 为前」差的那个角（度）。出图复核标定的，别只动数字。</summary>
    private const float BeastFacingYawDegrees = 0f;

    /// <summary>相机锁的那个节点（整块甲板，不是巨兽本体 —— 两者同一处，甲板才是取景主体）。</summary>
    public Node3D BeastNode => _deckRoot;

    public DeckView(Node3D world)
    {
        Name = "DeckView";
        _world = world;
        world.AddChild(this);

        // 地面先摆：它一整块铺在大地图底下，兽与城随后各自按 y=0 落地 ——
        // 「三者同处一个平面」那一格是靠这一块定的，没有它，兽与城都是悬空漂着的。
        BuildGround();

        // 甲板根与巨兽先摆：地标的摆位要按巨兽的实测占地方推（见 LandmarkWorldPosition），
        // 所以量尺寸的那一件得排在摆地标之前 —— 顺序反了就成了「按写死的数避开写死的数」。
        _deckRoot = new Node3D { Name = "Deck" };
        AddChild(_deckRoot);

        // 巨兽锚点：三形态 × 两阶共六件模型都挂在它下面，同一时刻只显示一件
        // （切形态 / 换位阶都只切显隐，见 SetBeastForm / SetBeastTier）。
        _beastAnchor = new Node3D { Name = "BeastAnchor" };
        _deckRoot.AddChild(_beastAnchor);

        // 表现导演（T17）：九类事件的特效 / 音效 / 结果标记都从它出去。
        _show = new ShowDirector(this, _deckRoot, seed: 20261027);

        foreach (var (form, tier, model) in BeastFormModels)
        {
            var view = LoadModel(model, _beastAnchor) ?? new Node3D { Name = model };
            view.Visible = false;
            _beastViews[(form, tier)] = view;
            // 六件各建一个动画器：**构造函数就是绑定口**（ModelAnimator 只在构造时找 AnimationPlayer，
            // 没有换绑口），所以不能拿锚点建一个、也不能一个动画器管六件。
            _beastAnims[(form, tier)] = new ModelAnimator(view);
        }
        _beastAnim = _beastAnims[(BeastForm.Original, BeastTier.Tier1)];
        ApplyBeastForm(BeastForm.Original);

        // 磁悬浮底座与浮山各两套（一阶 48 米甲板 / 二阶 56 米甲板）：位阶一换切显隐，
        // 既有布局与小镇节点都不重建（「位阶改变主轮廓与体量」）。
        _levitationBase1 = LoadModel("LevitationBase", _beastAnchor, "LevitationBase1");
        if (_levitationBase1 is not null) _levitationBase1.Position = new Vector3(0f, LevitationBaseY, 0f);
        _levitationBase2 = LoadModel("LevitationBaseTier2", _beastAnchor, "LevitationBase2");
        if (_levitationBase2 is not null)
        {
            _levitationBase2.Position = new Vector3(0f, LevitationBaseY, 0f);
            _levitationBase2.Visible = false;
        }

        // 悬浮层：镇地面（倒三角浮山）＋整座小镇，挂在**不参与动画**的甲板根上（不是挂在巨兽下）——
        // 挂在巨兽下的话镇地面会跟着步态起伏，镇上的人一张一合，那样等于没改。
        // 底座与浮山锥尖之间那道空就是磁悬浮场，它随兽的起伏变宽变窄，把那份起伏吃掉。
        _levitation = new Node3D { Name = "Levitation", Position = new Vector3(0f, LevitationY, 0f) };
        _deckRoot.AddChild(_levitation);

        // 浮山本体的原点（local y=0）是锥尖发光核的底面，镇面在 TownY。两阶各一件，切显隐。
        _townDeck1 = LoadModel("TownDeck", _levitation, "TownDeck1");
        _townDeck2 = LoadModel("TownDeckTier2", _levitation, "TownDeck2");
        if (_townDeck2 is not null) _townDeck2.Visible = false;

        // 两层地下（T14）：象限 0 的两格挂在浮山腰侧 —— 第 2 层（洞穴层）露在岩体边缘外，
        // 第 1 层压进岩体里（挖进山体的断面），地面出口三节井筒串回镇地板底。
        // 模块按 UndergroundSoil 的读数切显隐（开挖 / 清剿 / 嵌入），表现层只读不改。
        BuildUnderground();

        // 洞穴名牌（spec「洞穴、回长与嵌入状态」）：摆在镇政府门口那一格上空 ——
        // 涌怪落点（<see cref="DeckGeometry.CaveSpawn"/>）就是洞口在甲板上的那一格，
        // 地下八格不画体量（见类注），洞穴那一格的状态借门口这一格上屏。
        var caveSpot = DeckGeometry.CaveSpawn();
        _cavePlate = new Plate(
            MakeWorldPlate(_levitation, "CavePlate",
                new Vector3((float)caveSpot.X, TownY + 4.5f, (float)caveSpot.Z)),
            string.Empty);

        // 大地图地标：CityNode ×4 + 生态对象 + 远方高塔 + 远景与游客空艇。
        // 摆位与推进无关（点位是静态的），只在构造这一次排一遍。
        //
        // **摆位读逻辑层那一个数，不在这里另算** —— 城模型中心 = 城门沿外法线反推半个城深
        // （`MapPlace.CityCenter`），生态对象直接用 `BigMapGrid.EcoNodeSpecs` 的坐标。
        // 表现层只摆位，尺寸那一档由 `VerifyLandmarkSizes` 去验（验不上就当场报错）。
        _landmarks = new Node3D { Name = "Landmarks" };
        AddChild(_landmarks);

        foreach (var place in BigMapGrid.Places)
        {
            // 城门要朝着航线：模型的门在 **+Z 面**，而贫城、富城、野外锚地的外法线朝南（-Z）——
            // 那三座要绕 Y 转 180 度，门才朝着路（沙城的法线朝北 +Z，不用转）。
            var facing = place.OutwardNormal.Z < 0.0 ? new Vector3(0f, MathF.PI, 0f) : Vector3.Zero;
            if (CitySets.TryGetValue(place.Id, out var set))
            {
                var city = new Node3D
                {
                    Name = place.Id,
                    Position = ToWorld(place.CityCenter),
                    Rotation = facing,
                };
                _landmarks.AddChild(city);
                LoadModel(set.Landmark, city);
                LoadModel(set.Ground, city);
                foreach (var back in set.Backs)
                {
                    if (LoadModel(back.Model, city, back.Model) is not { } v) continue;
                    v.Position = new Vector3(back.X, back.Y, back.Z);
                    v.Rotation = new Vector3(0f, Mathf.DegToRad(back.RyDeg), 0f);
                }
                _landmarkViews.Add((city, place.Id, IsCity: true));
                continue;
            }

            if (LoadModel("CityNode", _landmarks, place.Id) is not { } c) continue;
            c.Rotation = facing;
            c.Position = ToWorld(place.CityCenter);
            _landmarkViews.Add((c, place.Id, IsCity: true));
        }

        // 生态剪影（T14）：树两种按清单序号轮替、矿石一件、兽群一件模型摆三只
        // （`ArenaTuning.HerdSilhouetteCount` 那一档在这里落地）。
        // 早先那一版三类共用 FoodNode 占位，占位期注记里写的「剪影区分是造型票的事」就是这一轮。
        var treeAlt = 0;
        foreach (var node in BigMapGrid.EcoNodeSpecs)
        {
            var container = new Node3D { Name = "eco_" + node.Id };
            container.Position = new Vector3((float)node.Position.X, GroundY, (float)node.Position.Z);
            _landmarks.AddChild(container);

            switch (node.Kind)
            {
                case EcoKind.Tree:
                    {
                        var model = treeAlt % 2 == 0 ? "TreePine" : "TreeBroadleaf";
                        treeAlt++;
                        if (LoadModel(model, container) is { } tree)
                            tree.Rotation = new Vector3(0f, (treeAlt % 3) * 1.1f, 0f);
                        break;
                    }
                case EcoKind.Herd:
                    {
                        foreach (var (dx, dz, yaw) in HerdLayout)
                        {
                            if (LoadModel("HerdBeast", container) is not { } beast) continue;
                            beast.Position = new Vector3(dx, 0f, dz);
                            beast.Rotation = new Vector3(0f, yaw, 0f);
                        }
                        break;
                    }
                default:
                    LoadModel("OreNode", container);
                    break;
            }

            _landmarkViews.Add((container, node.Id, IsCity: false));

            // 生态名牌（spec「生态对象与可吞状态」）：造一次挂头顶，可吞态每个逻辑步刷文字。
            // 高度按类别给（Tree 9.5 / Herd 3.4 / Ore 5.2 米），压在各自剪影之上读得动。
            _ecoPlates[node.Id] = new Plate(
                MakeWorldPlate(container, "EcoPlate", new Vector3(0f, EcoPlateHeight(node.Kind), 0f)),
                string.Empty);
        }

        // 远方高塔 ×3（T14）：摆在地平那一圈、离圆心 200~240 米的草地上 ——
        // 不压道路（z 180..199）、不压湖泊与河流，出界视角里读得出「陆地的边」。
        foreach (var (x, z) in DistantTowerSpots)
        {
            if (LoadModel("DistantTower", _landmarks, $"tower_{x}_{z}") is not { } tower) continue;
            tower.Position = new Vector3(x, GroundY, z);
        }

        // 远景飞艇 ×2（T14，复用 Airship 一件）：挂在 85~110 米高空，纯陈设，不参与任何成交窗口。
        foreach (var (x, y, z) in FarAirshipSpots)
        {
            if (LoadModel("Airship", _landmarks, $"far_airship_{x}_{z}") is not { } ship) continue;
            ship.Position = new Vector3(x, y, z);
            ship.Visible = false;   // 远景件默认收着，「出图复核」里按需要点亮（陈设不挡核心链路）
        }

        // 游客船（T14，复用 Airship 一件）：停靠期出现在当前城的门外上空，拔锚即收。
        _touristShip = LoadModel("Airship", _landmarks, "TouristShip");
        if (_touristShip is not null) _touristShip.Visible = false;

        // 产出建筑、防御设施与地标**都不在这儿摆死** —— 十三件建筑按建造册摆，
        // 见 SyncBuildings。早先旧版把采石场/工坊/农庄/市集与箭塔/陷阱无条件写死摆位，
        // 于是「不经营」那一档画面上照样有四栋楼，而逻辑层一座都没建：
        // 显示的不是真相（「表现层只读」 要防的那一类）。
    }

    /// <summary>
    /// 形态 × 位阶 → 模型文件名（<c>Models/&lt;Name&gt;.glb</c>，按 res:// 引用）。
    ///
    /// <para>六件是同一副四足骨架的六套剪影：同一套动作名、同一档包围盒，
    /// 差别做在模型本身里（配色、体量与局部器官）—— **不在表现层换皮或缩放几何**（「造型进模型」）。</para>
    /// </summary>
    private static readonly (BeastForm Form, BeastTier Tier, string Model)[] BeastFormModels =
    [
        (BeastForm.Original, BeastTier.Tier1, "Beast"),
        (BeastForm.Desert, BeastTier.Tier1, "BeastDesert"),
        (BeastForm.Glutton, BeastTier.Tier1, "BeastGlutton"),
        (BeastForm.Original, BeastTier.Tier2, "BeastTier2"),
        (BeastForm.Desert, BeastTier.Tier2, "BeastDesertTier2"),
        (BeastForm.Glutton, BeastTier.Tier2, "BeastGluttonTier2"),
    ];

    /// <summary>
    /// 切形态 —— <b>只切显隐与动画器指向</b>，不重建世界、不走 <c>StartWorld</c>。
    ///
    /// <para>六件模型在构造时就都挂好了（见构造函数），所以换形态是「亮一件、灭五件、
    /// 把动画器指向亮的那一件」，**不重建任何节点**。重建一次就得重挂磁悬浮底座与整座小镇，
    /// 而小镇浮在「不参与动画」的悬浮层上，重建它等于把小镇的相位又推倒一次。</para>
    ///
    /// <para>形态本身是逻辑层的真值（<c>ArenaWorld.Form</c>），本方法只把那一格
    /// 摆到画面上（表现层只读）。</para>
    /// </summary>
    public void SetBeastForm(BeastForm form)
    {
        // 同一形态直接返回：**只在形态真变那一刻切**。
        // 表现层每个逻辑步都跟着世界同步一次，这一格若每步都重摆，
        // 就会把六件模型的显隐反复写一遍（白费），也让「变了没有」这件事读不出来。
        if (_beastForm == form && _beastViews.Count > 0) return;
        ApplyBeastForm(form);
    }

    /// <summary>把某一档形态 × 位阶摆到画面上（显隐 + 动画器指向）—— 无条件执行那一次。</summary>
    private void ApplyBeastForm(BeastForm form)
    {
        _beastForm = form;
        foreach (var ((keyForm, keyTier), view) in _beastViews)
            view.Visible = keyForm == form && keyTier == _beastTier;
        _beastAnim = _beastAnims.GetValueOrDefault((form, _beastTier)) ?? _beastAnim;
    }

    /// <summary>
    /// 换位阶：六件里换一档、底座与浮山换一套。三阶不在本切片内，落到二阶那一档。
    /// </summary>
    public void SetBeastTier(BeastTier tier)
    {
        var effective = tier == BeastTier.Tier3 ? BeastTier.Tier2 : tier;
        if (_beastTier == effective) return;
        _beastTier = effective;
        ApplyBeastForm(_beastForm);
        if (_levitationBase1 is not null) _levitationBase1.Visible = effective == BeastTier.Tier1;
        if (_levitationBase2 is not null) _levitationBase2.Visible = effective == BeastTier.Tier2;
        if (_townDeck1 is not null) _townDeck1.Visible = effective == BeastTier.Tier1;
        if (_townDeck2 is not null) _townDeck2.Visible = effective == BeastTier.Tier2;
    }

    /// <summary>当前形态（画面上摆着的那一件）。</summary>
    public BeastForm CurrentBeastForm => _beastForm;

    /// <summary>当前位阶（画面上摆着的那一件）。</summary>
    public BeastTier CurrentBeastTier => _beastTier;

    /// <summary>当前形态那件模型的节点（读数与量尺寸都按它来）。</summary>
    private Node3D CurrentBeastView => _beastViews.GetValueOrDefault((_beastForm, _beastTier)) ?? _levitation;

    /// <summary>
    /// 地面那一组 —— <b>场地陈设，不是造型</b>：三样东西 ——
    /// 一张按 <see cref="BigMapTerrain"/> 生成的地形圆盘（程序化颜色 / 法线 / 粗糙度）、
    /// 一张圆外的星空面、一圈 2 米高的侧壁（圆盘边缘那堵挡墙）。
    ///
    /// <para><b>为什么之前要摆那块大平面</b>：早先场景里只有一盏方向光、没有任何承托面，
    /// 于是城与食物点整只悬在黑色虚空里（2026-10-15 出图实测），
    /// 而巨兽在「走」这件事上没有任何参照 —— 画面上分不清它是在地面上走还是漂着走。</para>
    ///
    /// <para><b>为什么用运行时图元而不是 <c>Models/</c> 里的真模型</b>：
    /// 判据是「这一块代表什么长什么样」。地面与地形色块不代表任何东西，
    /// 兽、城镇、食物点那三样造型仍然各自走 rig → glb → 体检单那一套。
    /// 与公共层 <c>SkillStageScene</c> 脚下的地网格、<c>src/Tools/PreviewStage.cs</c>
    /// 预览舞台那块地面是同一族做法。</para>
    ///
    /// <para><b>为什么改成不透明圆盘</b>：旧版是一张方形面靠 alpha 抠圆，材质因此必须透明，
    /// 地形吃不到方向光的阴影，城镇、树与巨兽看起来全像贴在色板上。圆盘只铺陆地那一块，
    /// 材质不透明，阴影落得上去；圆边仍与侧壁同半径，圆外那一圈照旧是星空。</para>
    ///
    /// <para><b>三样的高低关系</b>：地形圆盘在 y=0（兽、城、食物点都按 y=0 落地，与它同面）；
    /// 侧壁从 y=0 落到 y=−2；星空面在 y=−2。
    /// <b>彼此不再需要错开</b>：旧版那块大平面压在 y=−0.05 是为了消 z-fighting，
    /// 现在同高的面只剩地形圆盘一张，星空面在它下面 2 米，侧壁在圆边上竖着 ——
    /// 三者没有共面，于是那一档偏移整个删掉。</para>
    /// </summary>
    private void BuildGround()
    {
        BuildTerrain();
        BuildStarfield();
        BuildSideWall();
    }

    /// <summary>
    /// 地形圆盘 —— 半径 256 米、贴在 y=0；颜色、法线、粗糙度三张图都由
    /// <see cref="TerrainArt"/> 按 <see cref="BigMapTerrain"/> 程序化生成。
    ///
    /// <para><b>为什么从方形面改成圆盘</b>：方形面要盖住圆盘外的星空，只能靠 alpha 抠圆；
    /// 透明材质吃不到方向光阴影，城镇与树因此像贴在地上。圆盘只铺陆地，材质不透明，
    /// 阴影落得上去；UV 仍按世界米线性映射，第 0 列在最西、第 0 行在最北。</para>
    /// </summary>
    private void BuildTerrain()
    {
        var (albedo, normal, roughness, disc) = TerrainArt.Build();
        var terrain = new MeshInstance3D
        {
            Name = "Terrain",
            Mesh = disc,
            // 地面不投影：它一投影，105 米那一档机位上兽的影子会被拉成一大片糊在地上的黑斑。
            // 不透明圆盘仍然受影，城镇、树与巨兽的影子由它们自己投下来。
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = new Vector3(GroundCenterX, GroundY, GroundCenterZ),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoTexture = ImageTexture.CreateFromImage(albedo),
                NormalEnabled = true,
                NormalTexture = ImageTexture.CreateFromImage(normal),
                NormalScale = 0.8f,
                RoughnessTexture = ImageTexture.CreateFromImage(roughness),
                RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Red,
                Roughness = 1.0f,
                Metallic = 0.0f,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            },
        };
        AddChild(terrain);

        GD.Print($"[BeastArena] 地形圆盘 半径 {BigMapGrid.LandRadiusMeters.ToString(CultureInfo.InvariantCulture)} 米，"
                 + $"颜色贴图 {TerrainArt.AlbedoPixels.ToString(CultureInfo.InvariantCulture)}×"
                 + $"{TerrainArt.AlbedoPixels.ToString(CultureInfo.InvariantCulture)} 像素"
                 + $"（{TerrainArt.AlbedoPixelsPerMeter.ToString(CultureInfo.InvariantCulture)} 像素 = 1 米），"
                 + $"法线 / 粗糙度 {TerrainArt.DetailPixels.ToString(CultureInfo.InvariantCulture)}×"
                 + $"{TerrainArt.DetailPixels.ToString(CultureInfo.InvariantCulture)} 像素");
    }

    /// <summary>
    /// 星空面 —— 1600 × 1600 米，贴在 y = −2（圆盘外那一圈地平）。
    ///
    /// <para><b>星星是程序生成的，种子固定</b>（<see cref="StarfieldSeed"/>）：同一颗种子 → 同一片星空，
    /// 逐次重跑画面一致（重放与出图复核都要这个）。</para>
    /// </summary>
    private void BuildStarfield()
    {
        var image = StarfieldTexture();
        var texture = ImageTexture.CreateFromImage(image);

        var plane = new MeshInstance3D
        {
            Name = "Starfield",
            Mesh = new PlaneMesh { Size = new Vector2(StarfieldSpanMeters, StarfieldSpanMeters) },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = new Vector3(GroundCenterX, StarfieldY, GroundCenterZ),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoTexture = texture,
                // 不受光：星星自己就是亮的，挨一盏方向光只会把整片夜空压暗。
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Roughness = 1.0f,
                Metallic = 0.0f,
            },
        };
        AddChild(plane);

        GD.Print($"[BeastArena] 星空面 {StarfieldSpanMeters.ToString(CultureInfo.InvariantCulture)}×"
                 + $"{StarfieldSpanMeters.ToString(CultureInfo.InvariantCulture)} 米，"
                 + $"y = {StarfieldY.ToString(CultureInfo.InvariantCulture)}，"
                 + $"贴图 {StarTexturePixels.ToString(CultureInfo.InvariantCulture)}×"
                 + $"{StarTexturePixels.ToString(CultureInfo.InvariantCulture)} 像素，"
                 + $"种子 {StarfieldSeed.ToString(CultureInfo.InvariantCulture)}");
    }

    /// <summary>星空贴图 —— 固定种子程序生成的一批星点（黑底 + 若干亮点）。</summary>
    private static Image StarfieldTexture()
    {
        var size = StarTexturePixels;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        image.Fill(new Color(0.02f, 0.02f, 0.05f, 1f));

        var rng = new RandomNumberGenerator { Seed = StarfieldSeed };
        for (var i = 0; i < StarfieldStarCount; i++)
        {
            var x = rng.RandiRange(0, size - 1);
            var y = rng.RandiRange(0, size - 1);
            var brightness = rng.RandfRange(0.35f, 1.0f);
            var tint = rng.RandfRange(0.85f, 1.0f);
            image.SetPixel(x, y, new Color(brightness, brightness, brightness * tint, 1f));
        }

        return image;
    }

    /// <summary>
    /// 侧壁 —— 圆盘边缘那 2 米高的挡墙：圆柱侧面，半径 <see cref="BigMapGrid.LandRadiusMeters"/> 米，
    /// 从 y=0 落到 y=−2，单色中灰，不投影。
    ///
    /// <para><b>上下两个盖都关掉</b>：开着的话顶盖是一张半径 256 米的圆盘，正好盖在
    /// 地形圆盘上（z-fighting），底盖又正好压在星空面上（还是 z-fighting）。关掉之后只剩那一圈立面。</para>
    ///
    /// <para><b>为什么是中灰而不是深灰</b>：墙只有 2 米高，出图时它就是一条窄带。
    /// 深灰（0.16）在成图里与圆外的星空底色几乎同色，那条带子直接读不出来
    /// （2026-10-21 出图实测）。中灰加上一点自发光，换来的是「圆盘边缘立着一堵墙」
    /// 这件事在图上看得见 —— 出图复核是这一格唯一的证据手段（「落地配出图」）。</para>
    /// </summary>
    private void BuildSideWall()
    {
        var radius = (float)BigMapGrid.LandRadiusMeters;
        var wall = new MeshInstance3D
        {
            Name = "SideWall",
            Mesh = new CylinderMesh
            {
                TopRadius = radius,
                BottomRadius = radius,
                Height = SideWallHeightMeters,
                RadialSegments = TerrainArt.DiscSegments,
                Rings = 1,
                CapTop = false,
                CapBottom = false,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = new Vector3(
                GroundCenterX,
                GroundY - (SideWallHeightMeters * 0.5f),
                GroundCenterZ),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.34f, 0.34f, 0.38f),
                // 圆柱有一半朝着光的背面，不给一点自发光，背光那半圈会掉进纯黑。
                EmissionEnabled = true,
                Emission = new Color(0.10f, 0.10f, 0.12f),
                Roughness = 1.0f,
                Metallic = 0.0f,
            },
        };
        AddChild(wall);
    }

    /// <summary>
    /// 地标与其 id 的对应表 —— 摆位在构造里就摆好了（位置读逻辑层那一个数），
    /// 这里只留着模型与 id，好把实测尺寸跟逻辑层那两格对一遍。
    /// </summary>
    private readonly List<(Node3D View, string Id, bool IsCity)> _landmarkViews = new();

    // ---- T13 三城地标组（表现层摆位表：地标 / 地表 / 四件背景建筑的本地偏移）----
    // 背景建筑的 Y 就是各地标台面的顶高（贫 2.0 / 沙 1.6 / 富 1.8 米，与 Gen.Cities.cs 各件同源）；
    // 偏移取的是各件在自家台面上的空角，改地标要同时改这张表（对照读两处的注释）。

    private readonly record struct BackSlot(string Model, float X, float Y, float Z, float RyDeg);

    private sealed record CitySet(string Landmark, string Ground, BackSlot[] Backs);

    private static readonly Dictionary<string, CitySet> CitySets = new()
    {
        ["poor_city"] = new("LandmarkPoor", "GroundPoor",
        [
            new("BackPoorA", -34f, 2f, 26f, 0f),
            new("BackPoorB", 34f, 2f, 24f, 90f),
            new("BackPoorC", -32f, 2f, -30f, 0f),
            new("BackPoorD", 30f, 2f, -2f, 0f),
        ]),
        ["desert_city"] = new("LandmarkSand", "GroundSand",
        [
            new("BackSandA", -30f, 1.6f, 18f, 0f),
            new("BackSandB", 2f, 1.6f, -34f, 0f),
            new("BackSandC", -32f, 1.6f, -30f, 0f),
            new("BackSandD", 24f, 1.6f, 30f, 0f),
        ]),
        ["rich_city"] = new("LandmarkRich", "GroundRich",
        [
            new("BackRichA", -30f, 1.8f, 22f, 0f),
            new("BackRichB", 30f, 1.8f, 22f, 0f),
            new("BackRichC", -28f, 1.8f, 12f, 90f),
            new("BackRichD", 28f, 1.8f, 12f, 90f),
        ]),
    };


    /// <summary>尺寸验过没有。验过之后不再重量（地标是静态的，反复重量只会让读数抖）。</summary>
    private bool _sizesVerified;

    /// <summary>
    /// 实测尺寸与逻辑层那几格对一遍 —— <b>对不上当场喊出来</b>。
    ///
    /// <para><b>为什么是「验」而不是「算」</b>：停靠点（<see cref="ArenaTuning.BeastHalfWidthFor"/>，
    /// 按位阶取那一档）与城模型中心（<see cref="ArenaTuning.CityHalfDepthMeters"/>）都由逻辑层那几格算出，
    /// 表现层只摆位。改了模型却忘了回填那几格，画面上兽就骑在城身上，
    /// 而读数还在报「净空 20 米」—— 量错的那一格和验它的那一格读同一个数，错与对一起错。
    /// 这一格就是把那两处的差摆到台面上，不静默兜底。</para>
    ///
    /// <para><b>六套组合逐件验</b>：三形态 × 两位阶的每件模型各量一次，
    /// 期望半宽按它自己那一档位阶取 —— 一阶三件都是 26.45，二阶的余量是最大档 30.00。</para>
    ///
    /// <para><b>量不到就下一帧再验</b>：刚 <c>LoadModel</c> 出来的那一帧，
    /// <c>MeshInstance3D.GetAabb()</c> 读到的是全零 —— 同一批件在下一帧量就对。</para>
    /// </summary>
    private void VerifyLandmarkSizes()
    {
        if (_sizesVerified) return;

        foreach (var (key, view) in _beastViews)
        {
            var measured = HalfWidthOf(view);
            if (measured <= 0f) return;   // 还没量出来 —— 下一帧再验
            CheckBeastHalfWidth(ArenaTuning.BeastHalfWidthFor(key.Tier), measured, key);
        }

        foreach (var (view, id, isCity) in _landmarkViews)
        {
            if (!isCity) continue;
            var measured = HalfDepthOf(view);
            if (measured <= 0f) return;
            CheckMeasured(ArenaTuning.CityHalfDepthMeters, measured, $"城模型的进深（{id}）");
            break;   // 四座城是同一件模型，量一座就够
        }

        _sizesVerified = true;
    }

    /// <summary>
    /// 兽的体宽验档 —— <b>只验「实测超出本位阶余量」这一向</b>。
    ///
    /// <para><b>为什么不双向卡</b>：停靠点是按本位阶<b>最宽</b>那一件模型留的余量
    /// （二阶三件实测 30.000 / 26.500 / 29.152 米，逻辑层取最大档 30.00）。
    /// 比余量窄的形态在城门外多留一道缝，兽身不压城，合法；
    /// 实测<b>超过</b>余量才是真压城，那才是这一格要拦的事。
    /// 双向 ±0.05 会把同位阶里较窄的两件模型天天报错，报错的不是 bug。</para>
    ///
    /// <para><b>容差仍是 0.05 米</b>：逻辑层写的是体检单取整过的数（一阶 52.9 → 26.45），
    /// 实测 26.465，差 0.015 米。超出的方向越过容差就是造型改了、该回填位阶半宽表了。</para>
    /// </summary>
    private static void CheckBeastHalfWidth(
        double expected, double measured, (BeastForm Form, BeastTier Tier) key)
    {
        if (measured <= expected + SizeToleranceMeters) return;

        GD.PushError(
            $"[BeastArena] 兽的体宽（{key} 形态）实测 {measured.ToString("0.000", CultureInfo.InvariantCulture)} 米，"
            + $"逻辑层那一档余量是 {expected.ToString("0.000", CultureInfo.InvariantCulture)} 米 —— "
            + $"超出 {(measured - expected).ToString("0.000", CultureInfo.InvariantCulture)} 米，"
            + $"越过容差 {SizeToleranceMeters.ToString("0.00", CultureInfo.InvariantCulture)} 米。"
            + "改了模型要回填 ArenaTuning 的位阶半宽表，否则兽会压城。");
    }

    /// <summary>
    /// 实测值与逻辑层那一个数对一遍：差得超过容差就当场报错（<b>不静默兜底</b>）。
    /// </summary>
    private static void CheckMeasured(double expected, double measured, string what)
    {
        var diff = Math.Abs(measured - expected);
        if (diff <= SizeToleranceMeters) return;

        GD.PushError(
            $"[BeastArena] {what}实测 {measured.ToString("0.000", CultureInfo.InvariantCulture)} 米，"
            + $"逻辑层那一个数是 {expected.ToString("0.000", CultureInfo.InvariantCulture)} 米 —— "
            + $"差 {diff.ToString("0.000", CultureInfo.InvariantCulture)} 米，"
            + $"超出容差 {SizeToleranceMeters.ToString("0.00", CultureInfo.InvariantCulture)} 米。"
            + "改了模型要回填 ArenaTuning 里那两格，否则兽会压城。");
    }

    /// <summary>实测尺寸与逻辑层那两格之间允许的差（米）—— 差 0.05 米以上就是造型改了。</summary>
    private const double SizeToleranceMeters = 0.05;

    /// <summary>
    /// 航线那一行的世界 Z（米）—— 兽在贫城与富城之间往返走的那一行。
    /// 取贫城的<b>停靠点</b>那一行（城门再外推一个兽半宽），不是城门那一行；
    /// 兽半宽按画面当前位阶取 —— 二阶那一档外扩到 30.00 米，这一行跟着挪 3.55 米。
    /// </summary>
    private float RouteLineWorldZ => (float)BigMapGrid.PoorCity.StopPoint(_beastTier).Z;

    /// <summary>一个模型在水平面上沿 Z 那一半的占地（米）—— 按 AABB 实测，不按造型文档里的数写死。</summary>
    private static float HalfDepthOf(Node3D view) => FootprintOf(view).Depth * 0.5f;

    /// <summary>
    /// 一个模型在水平面上沿 X 那一半的占地（米）—— 按 AABB 实测。
    ///
    /// <para><b>巨兽用的是这一档</b>：巨兽模型里那条 X 向的短边（实测 52.93 米）是它的**体宽**，
    /// 沿法线那一个半宽 = 52.93 ÷ 2 = <b>26.465</b>，停靠点用的就是它（写死的是 26.45，
    /// 差 0.015 米，见 <see cref="CheckMeasured"/>）。
    /// （它模型里的 Z 向是兽身的长边，89.58 米那一档是兽的长度，不是宽。）</para>
    /// </summary>
    private static float HalfWidthOf(Node3D view) => FootprintOf(view).Width * 0.5f;

    /// <summary>
    /// 一个模型的水平占地（米）。逐层找第一个 <see cref="VisualInstance3D"/>，量它的 AABB。
    ///
    /// <para><b>为什么带名字而不是回 <see cref="Vector3"/></b>：早先那一版把两个尺寸塞进
    /// <c>Vector3</c> 的 X 与 Y，于是「半深」取 <c>.Z</c> 恒得 0 ——
    /// 地标全被摆在离航线 <c>兽半径 + 净空</c> 那儿，正好压在巨兽身上，
    /// 而净空读数因为量的是同一个 0，报出来还是正的。
    /// 量错的那一格和验它的那一格读同一个数，错与对一起错。</para>
    /// </summary>
    private static (float Width, float Depth) FootprintOf(Node3D view)
    {
        foreach (var node in EnumerateSelf(view))
        {
            if (node is VisualInstance3D vi)
            {
                var a = vi.GetAabb();
                return (a.Size.X, a.Size.Z);
            }
        }
        return (0f, 0f);
    }

    private static IEnumerable<Node> EnumerateSelf(Node root)
    {
        yield return root;
        foreach (var child in root.GetChildren())
        {
            foreach (var n in EnumerateSelf(child))
                yield return n;
        }
    }

    /// <summary>
    /// 挂 HUD（<c>UILayer</c> 下要有一个 <c>Control</c> 子节点，「落地配出图」）。
    /// 常驻五组 + 三行辅助读数（spec「信息结构」与对接面清单「HUD」表），
    /// 行名与 <see cref="UpdateHud"/> 里那一处同名；组名那一行是静态文字，不在这里取。
    /// </summary>
    public void AttachHud(Control hudRoot)
    {
        _clockRow = hudRoot.GetNodeOrNull<Label>("Clock");
        _goalRow = hudRoot.GetNodeOrNull<Label>("Goal");
        _stationRow = hudRoot.GetNodeOrNull<Label>("Station");
        _accountRow = hudRoot.GetNodeOrNull<Label>("Accounts");
        _beastRow = hudRoot.GetNodeOrNull<Label>("Beast");
        _growthRow = hudRoot.GetNodeOrNull<Label>("Growth");
        _standingRow = hudRoot.GetNodeOrNull<Label>("Standing");
        _populationRow = hudRoot.GetNodeOrNull<Label>("Population");
    }

    /// <summary>甲板上的悬浮层根节点 —— 屏幕点换算局部坐标要从它过（它带着巨兽的偏航）。</summary>
    public Node3D LevitationNode => _levitation;

    /// <summary>
    /// 按 id 取当前挂着的场景节点：建筑、活物、点位物三本册依次查。
    /// 抓取与高亮要能拿到那一件本体（不是再建一份副本）。
    /// </summary>
    public bool TryNodeOf(string id, out Node3D node)
    {
        if (_buildingViews.TryGetValue(id, out var building) && GodotObject.IsInstanceValid(building))
        {
            node = building;
            return true;
        }

        if (_unitViews.TryGetValue(id, out var unit) && GodotObject.IsInstanceValid(unit.View))
        {
            node = unit.View;
            return true;
        }

        if (_itemViews.TryGetValue(id, out var item) && GodotObject.IsInstanceValid(item))
        {
            node = item;
            return true;
        }

        node = null!;
        return false;
    }

    /// <summary>
    /// 这一格上站着哪一栋建筑（没占着回 false）。格号先经
    /// <see cref="DeckBlueprints.CandidateOf"/> 翻成甲板坐标 —— 建造模块收的是米，不是格号。
    /// </summary>
    public bool BuildingIdAtCell(int col, int row, out string id)
    {
        id = string.Empty;
        if (_syncedWorld is null) return false;

        var center = DeckBlueprints.CandidateOf(new GridCell(col, row));
        id = _syncedWorld.Construction.BuildingIdAt(center.X, center.Y);
        return id.Length > 0;
    }

    /// <summary>
    /// 把世界当前的样子摆到场景上。<b>每个逻辑步调一次</b>（内容与状态的更新都在这里）。
    /// 位姿不在这里定 —— 那一格在 <see cref="SyncPose"/>，因为它每个渲染帧都要跑。
    /// </summary>
    public void Sync(ArenaWorld world, float gameSpeed = 1f)
    {
        if (!ReferenceEquals(_syncedWorld, world)) BindWorld(world);

        _show.GameSpeed(gameSpeed);
        SetBeastTier(world.Tier);
        SetBeastForm(world.Form);
        SyncShowBed(world);

        // 动画时钟跟着逻辑流速走：不接这一根，8 倍快进下巨兽位移 8 倍而腿摆仍是 1 倍，
        // 画面上就是滑着走（`ModelAnimator.GameSpeed` 的注记里写的就是这件事）。
        if (_beastAnim is not null) _beastAnim.GameSpeed = gameSpeed;
        SyncBuildings(world);
        SyncUnits(world, gameSpeed);
        SyncItems(world);
        SyncDockedProps(world);
        SyncUnderground(world);
        SyncTouristShip(world);
        SyncWorldMarkers(world);
        UpdateHud(world);

        // 位姿在步上也刷一次（0 = 不外推），这样单步直达、注入口这些不经过 _Process 的路径
        // 摆出来的东西仍然是对的；渲染循环里紧接着还会用真正的不足一步量再刷一遍。
        SyncPose(world, 0.0);
    }

    /// <summary>
    /// 换局时接事件、清上一局的表现残留。事件只订世界转发口（对接面清单 1.7），
    /// 九类各接一条；建筑完工与受损没有专门事件，走读数变化（<see cref="SyncBuildings"/>）。
    /// </summary>
    private void BindWorld(ArenaWorld world)
    {
        ResetPresentation();
        _syncedWorld = world;

        world.EatStarted += OnEatStartedShow;
        world.CrystalsProduced += OnCrystalsProducedShow;
        world.FormSwitched += OnFormSwitchedShow;
        world.TierAdvanced += OnTierAdvancedShow;
        world.CaveClearStarted += OnCaveClearStartedShow;
        world.AnchorPulled += OnAnchorPulledShow;
        world.DefenceStarted += OnDefenceStartedShow;
        world.Battle.DefenceSettled += OnDefenceSettledShow;
        world.FinalRetryStarted += OnFinalRetryShow;
    }

    /// <summary>清掉上一局的建筑 / 活物 / 点位物 / 尸体 / 废墟与标记（生态与城是静态件，不重建）。</summary>
    private void ResetPresentation()
    {
        foreach (var view in _buildingViews.Values) Free(view);
        foreach (var view in _ruinViews.Values) Free(view);
        foreach (var entry in _unitViews.Values) Free(entry.View);
        foreach (var view in _itemViews.Values) Free(view);
        _buildingViews.Clear();
        _buildingModels.Clear();
        _ruinViews.Clear();
        _unitViews.Clear();
        _itemViews.Clear();
        _buildSeen.Clear();
        _unitSeen.Clear();
        _deadAt.Clear();
        _unitPlates.Clear();
        _itemPlates.Clear();
        _warehousePlate = null;
        _emergeUntilTick = -1;
        _lying = false;
        _show.ClearMarks();
        SetBeastTier(BeastTier.Tier1);
    }

    /// <summary>生态对象在 <c>Landmarks</c> 下的那件锚（吃的时候特效与标记落它头顶）。</summary>
    private Node3D? EcoAnchorOf(string id)
    {
        foreach (var (view, landmarkId, _) in _landmarkViews)
        {
            if (string.Equals(landmarkId, id, StringComparison.Ordinal)) return view;
        }

        return null;
    }

    /// <summary>一条吞食开场：巨兽播 eat，生态对象头顶给一条结果标记。</summary>
    private void OnEatStartedShow(EcoNode node, bool auto)
    {
        _beastAnim?.PlayOneShot(ShowCues.Of(ShowCategory.Eat).BeastClip);
        if (EcoAnchorOf(node.Id) is not { } anchor) return;

        _show.Fire(ShowCategory.Eat, anchor,
            auto ? Loc.T("自主吞食") : Loc.T("指定吞食"),
            EcoPlateHeight(node.Kind) - 2.0f);
    }

    /// <summary>消化产出：甲板落魔晶，镇政府上空报数（仓库名牌之外再给一条即时标记）。</summary>
    private void OnCrystalsProducedShow(int count)
    {
        var anchor = _buildingViews.GetValueOrDefault(ArenaWorld.TownHallId) ?? _levitation;
        _show.Fire(ShowCategory.Crystal, anchor,
            Loc.Fmt("+{0} 魔晶", count.ToString(CultureInfo.InvariantCulture)), 13f);
    }

    /// <summary>形态切换：先切模型，再播 <c>form_shift</c> 与金光。</summary>
    private void OnFormSwitchedShow(BeastForm form)
    {
        SetBeastForm(form);
        _beastAnim?.PlayOneShot(ShowCues.Of(ShowCategory.FormShift).BeastClip);
        _beastAnim?.Flash();
        _show.Fire(ShowCategory.FormShift, _levitation,
            Loc.T(BeastFormText.NameOf(form)), 14f, vfxY: DeckY + 2f);
    }

    /// <summary>进阶完成：换六件里的那一档、换底座与浮山，播 <c>advance</c> 与金光。</summary>
    private void OnTierAdvancedShow(BeastTier tier)
    {
        SetBeastTier(tier);
        _beastAnim?.PlayOneShot(ShowCues.Of(ShowCategory.TierAdvance).BeastClip);
        _beastAnim?.Flash();
        _show.Fire(ShowCategory.TierAdvance, _levitation,
            Loc.T(BeastTierText.NameOf(tier)), 14f, vfxY: DeckY + 2f);
    }

    /// <summary>洞穴涌怪：新出现的原生怪在下一两个逻辑步里补 <c>emerge</c>，并给一条洞口标记。</summary>
    private void OnCaveClearStartedShow(int sector, int layer)
    {
        _emergeUntilTick = (_syncedWorld?.Tick ?? 0) + 2;
        var anchor = _cavePlate is { } plate && GodotObject.IsInstanceValid(plate.Label)
            ? (Node3D)plate.Label
            : _beastAnchor;
        _show.Fire(ShowCategory.CaveEmerge, anchor,
            Loc.Fmt("洞穴涌怪 · 象限{0} 第{1}层", sector.ToString(CultureInfo.InvariantCulture),
                layer.ToString(CultureInfo.InvariantCulture)), 6f);
    }

    /// <summary>拔锚：巨兽伏下。</summary>
    private void OnAnchorPulledShow(string cityId)
    {
        _lying = true;
        _beastAnim?.PlayOneShot("lie_down");
        _show.Marker(_levitation, Loc.T("拔锚"), 14f);
        _show.Audio.Cue("alarm_warning");
    }

    /// <summary>防守开场：巨兽伏下，警报按普通 / 最终两档分。</summary>
    private void OnDefenceStartedShow(BattleOccasion occasion)
    {
        if (!_lying)
        {
            _lying = true;
            _beastAnim?.PlayOneShot("lie_down");
        }

        var final = occasion == BattleOccasion.FinalDefence;
        if (final)
        {
            _show.Fire(ShowCategory.FinalDefence, _levitation,
                Loc.T("最终防守开始"), 16f, vfxY: DeckY + 2f);
            return;
        }

        _show.Audio.Cue("alarm_warning");
        _show.Vfx.Play("mark", _beastAnchor.GlobalPosition.X, _beastAnchor.GlobalPosition.Z, y: DeckY + 2f);
        _show.Marker(_levitation, Loc.T("防守开始"), 14f);
    }

    /// <summary>防守收场：胜负两个音效各一条，结果标记留在兽背上空。</summary>
    private void OnDefenceSettledShow(bool won)
    {
        _show.Audio.Cue(won ? "result_victory" : "result_defeat");
        _show.Marker(_levitation, Loc.T(won ? "防守胜利" : "防守失败"), 16f);
        _show.Vfx.Play("ring", _beastAnchor.GlobalPosition.X, _beastAnchor.GlobalPosition.Z, y: DeckY + 2f);
    }

    /// <summary>最终防守重试：每一次尝试单独一条标记。</summary>
    private void OnFinalRetryShow(int attemptIndex)
    {
        _show.Audio.Cue("alarm_critical");
        _show.Marker(_levitation,
            Loc.Fmt("最终防守重试 · 第{0}次", attemptIndex.ToString(CultureInfo.InvariantCulture)), 40f);
    }

    /// <summary>环境床：防守中 > 停靠 > 航行；其余（停摆 / 仪式 / 拔锚等待）沿用停靠床。</summary>
    private void SyncShowBed(ArenaWorld world)
    {
        if (world.Battle.Occasion is BattleOccasion.Defence or BattleOccasion.FinalDefence or BattleOccasion.CaveClearing)
        {
            _show.Bed("amb_defense");
            return;
        }

        if (world.IsDocked)
        {
            _show.Bed("amb_dock");
            return;
        }

        _show.Bed(world.Voyage.Phase == VoyagePhase.Sailing ? "amb_sail" : "amb_dock");
    }

    /// <summary>调试桥读数：九类演出记了多少条、还挂着几块标记、音效合并 / 让位各几次。</summary>
    public string ShowReadout() => _show.Readout();

    /// <summary>
    /// 粒子那一份读数（池账 + 正在演的节点数 + 申报粒子数）。
    /// <b>「申请粒子」不是存活粒子数</b>：GPU 粒子在显卡上算，引擎不给出存活读数；
    /// 峰值验收按申请量记，两倍压力也按它翻。
    /// </summary>
    public string VfxReadout() =>
        $"{_show.Vfx.Readout()}｜发射器 {_show.Vfx.ActiveEmitters.ToString(CultureInfo.InvariantCulture)}"
        + $"｜申请粒子 {_show.Vfx.RequestedParticles.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>正在演的效果节点数（性能采样的峰值从这一格取）。</summary>
    public int VfxEmitters => _show.Vfx.ActiveEmitters;

    /// <summary>正在演的发射器申报的粒子数（不是存活粒子数，见 <see cref="VfxReadout"/>）。</summary>
    public long VfxParticles => _show.Vfx.RequestedParticles;

    /// <summary>
    /// 两倍峰值压力：在兽背挂一个持续发射器，粒子数按 <paramref name="particles"/> 给。
    /// 只给验收脚本用，正常播放仍走池。
    /// </summary>
    public string StressVfxParticles(long particles)
    {
        var ok = _show.Vfx.StressSustain("impact", (int)Math.Clamp(particles, 16, 4096),
            _beastAnchor.GlobalPosition.X, DeckY + 2f, _beastAnchor.GlobalPosition.Z);
        return $"vfx_stress={(ok ? "on" : "failed")}"
            + $" emitters={VfxEmitters.ToString(CultureInfo.InvariantCulture)}"
            + $" particles={VfxParticles.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>撤掉压力测试的发射器。</summary>
    public string ClearVfxStress() => _show.Vfx.ClearStress();

    /// <summary>
    /// 巨兽在大地图上的位姿 —— <b>每个渲染帧调一次</b>，这是「小镇不抖」的那一格。
    ///
    /// <para>逻辑层给的位置是 <see cref="BeastVoyage.Position"/>（大地图逻辑坐标，米），
    /// 那一格就是 3D 坐标（同一把尺子）；渲染 30~60 帧而逻辑 10 步/秒，
    /// 只在步上刷位姿就是三帧一动的台阶。这里把累积器里那不足一步的部分
    /// （<paramref name="stepFraction"/>，0~1）按 <see cref="SecondsPerStep"/> 折成米、按剩余航程比例补进去，
    /// 于是位姿在渲染帧率上连续 —— 逻辑层一个数都没改，改的只是取样点。</para>
    ///
    /// <para>停泊、进食、停摆那几档地图不再推进（<c>Voyage.Phase</c> 不是 <c>Sailing</c>），
    /// 巨兽站住；这时不外推，否则会在原地漂一小段再被拉回来。暂停同理。</para>
    /// </summary>
    public void SyncPose(ArenaWorld world, double stepFraction)
    {
        VerifyLandmarkSizes();

        var at = world.Voyage.Position;
        var walking = world.Voyage.Phase == VoyagePhase.Sailing && !world.IsPaused && !world.TimeFrozen;

        // 朝向由两次读数的差推出来：寻路层没有公开的朝向读数口，而表现层自己补算航向
        // 不构成第二份真值 —— 它只摆朝向，不回写任何坐标。
        if (_lastBeastAt is { } prev && (Math.Abs(at.X - prev.X) > 1e-9 || Math.Abs(at.Z - prev.Z) > 1e-9))
        {
            _beastHeading = new Vector3((float)(at.X - prev.X), 0f, (float)(at.Z - prev.Z)).Normalized();
        }

        _lastBeastAt = at;

        // 停靠时兽身横过来（长边与城门法线垂直）：伸进城里的于是兽的**半宽**（按位阶取那一档，一阶 26.45 米），
        // 停靠点那一档偏移正好抵住城体边缘（净空 0.0 米）。见 `MapPlace.ParkAxis`。
        // 不在城门外就照旧走航向那一档。
        var heading = ParkingHeading(world) ?? _beastHeading;

        _deckRoot.Position = ToWorld(at);
        _deckRoot.Rotation = new Vector3(
            0f,
            TurnedYaw(heading) + Mathf.DegToRad(BeastFacingYawDegrees),
            0f);

        // 动作在模型里、状态在逻辑层里（「动作在模型」）：这里只报「走多快」，不自己摆腿。
        // 停泊那一档报 0，动作层自己落到 idle。
        //
        // **×10 那个换算原来漏了**（2026-10-15 铺地面那一轮照出）：直接报 0.02 米每现实秒，
        // 而 <see cref="ModelAnimator.Travel"/> 收的是「米每游戏秒」、idle/walk 分界在 0.05 ——
        // 0.02 ≤ 0.05，于是巨兽永远落在 idle 上。桥上读数逐字为 `want=idle playing=idle`，
        // 而同一时刻 `walking=true`：兽在滑着走、腿一下不动。换成 0.2 就过界进 walk。
        _beastAnim?.Travel(walking ? CruiseSpeedMetersPerSecond * GameSecondsPerRealSecond : 0f);

        // 伏下之后重新开步：补一条起身（A-B6）。只在真开步那一步发，停泊期不空演。
        if (walking && _lying)
        {
            _beastAnim?.PlayOneShot("get_up");
            _lying = false;
        }

        // 悬浮层只跟着甲板根的偏航走，自身不摆姿态：镇地面因此恒定水平，
        // 巨兽过弯时小镇跟着转、但不会被甩出水平（这格要是也 LookAt 一次就会跟着俯仰）。
        _levitation.Rotation = Vector3.Zero;

        _poseReadout =
            $"map=({at.X.ToString("0.00", CultureInfo.InvariantCulture)},{at.Z.ToString("0.00", CultureInfo.InvariantCulture)}) " +
            $"frac={stepFraction.ToString("0.00", CultureInfo.InvariantCulture)} " +
            $"world=({_deckRoot.Position.X.ToString("0.0", CultureInfo.InvariantCulture)}," +
            $"{_deckRoot.Position.Z.ToString("0.0", CultureInfo.InvariantCulture)}) " +
            $"phase={world.Voyage.Phase.ToString().ToLowerInvariant()} " +
            $"walking={walking.ToString().ToLowerInvariant()} " +
            $"levitationY={_levitation.Position.Y.ToString("0.00", CultureInfo.InvariantCulture)} " +
            $"townY={TownY.ToString("0.00", CultureInfo.InvariantCulture)} " +
            ClearancesText();
    }

    /// <summary>
    /// 停靠时该摆的那一档朝向（<b>兽身横过来</b>：长边与城门法线垂直）；
    /// 不在城门外回 null（那一档照旧走航向）。
    ///
    /// <para><b>为什么停靠时要横过来</b>：停靠点离城门一个当前位阶的兽半宽（一阶 26.45 米），
    /// 那一档的前提是「兽身的宽正对城门」。巨兽节点跟着航向转（模型的长边 89.58 米顺着航向），
    /// 沿支路（南北向）到的沙城与野外锚地，兽停下来是<b>头朝城</b>的 ——
    /// 伸进城里的是兽的长（44.79 米），兽身压城 18.35 米。
    /// 停靠时把兽身摆到与外法线垂直的那条轴上，伸进城里又回到半宽，净空归零。</para>
    ///
    /// <para><b>符号跟当前航向</b>：兽从哪边来就朝哪边停，不为了对齐而掉头。</para>
    /// </summary>
    private Vector3? ParkingHeading(ArenaWorld world)
    {
        var place = world.Voyage.ParkedPlace;
        if (place is null) return null;

        var axis = place.ParkAxis;
        var along = (_beastHeading.X * (float)axis.X) + (_beastHeading.Z * (float)axis.Z);
        var sign = along < 0.0 ? -1.0 : 1.0;
        return new Vector3((float)axis.X * (float)sign, 0f, (float)axis.Z * (float)sign);
    }

    /// <summary>
    /// 甲板摆位用的偏航（弧度）—— 目标偏航按 <see cref="YawRateDegreesPerSecond"/> 转过去。
    ///
    /// <para><b>为什么要限速</b>：停靠那一档要把兽身转 90 度（头朝城 → 横过来），
    /// 一步到位就是瞬移 —— 兽背上那座小镇跟着瞬移 90 度，画面上一帧就跳过去。
    /// 限速转过去，转的是兽与镇一起。</para>
    /// </summary>
    private float TurnedYaw(Vector3 heading)
    {
        // 甲板的 -Z 轴指着航向（Godot 的 look_at 口径），所以偏航 = atan2(−hx, −hz)。
        var target = MathF.Atan2(-heading.X, -heading.Z);
        if (!_yawKnown)
        {
            _yawKnown = true;
            _yaw = target;
            return _yaw;
        }

        var delta = GetProcessDeltaTime();
        if (delta <= 0.0) delta = 1.0 / 60.0;
        var step = Mathf.DegToRad(YawRateDegreesPerSecond) * (float)delta;

        var diff = target - _yaw;
        diff = MathF.Atan2(MathF.Sin(diff), MathF.Cos(diff));   // 归到 (-π, π]
        _yaw = MathF.Abs(diff) <= step ? target : _yaw + (MathF.Sign(diff) * step);
        return _yaw;
    }

    /// <summary>
    /// 三道净空的读数 —— 「兽与城不重叠」「兽与镇不贴合」那两条的唯一验收入口。
    ///
    /// <para>都是<b>由实测尺寸减出来的余量</b>，不是写死的数：负数就是穿模，正数是还剩多宽的一道缝。
    /// 「读数说它在那儿」与「画面上看得见」是两回事，所以每一格都另配出图复核
    /// （<see cref="LevitationReadout"/> 的注记）。</para>
    ///
    /// <para><b>城与兽那两格报的是「实测」</b>：锚点用的就是这两个数 ——
    /// 城沿法线那一个半深 <c>cityHalfZ</c>（实测 48.0，模型不转，Z 向那一条就是法线方向），
    /// 兽沿法线那一个半宽 <c>beastHalfX</c>（实测 26.5，兽身停靠时横过来，
    /// 模型里 X 向那条短边 52.93 米正对法线）。锚点那两格
    /// （<c>ArenaTuning.CityHalfDepthMeters</c> / <c>ArenaTuning.BeastHalfWidthFor</c>，半宽按位阶取）
    /// 是逻辑层那一份；实测与它对不上时 `VerifyLandmarkSizes` 当场报错。</para>
    /// </summary>
    private string ClearancesText()
    {
        var beastTop = GroundY + BeastTopY();

        // 悬浮场：盘底与塔尖之间那道空。负数就是盘压进塔里，正数是还剩多宽的一道场。
        // 这一格量的是**底座顶面与镇地面底面之间**的空 —— 底座骑在兽背上，镇地面浮在它上方，
        // 中间隔着的那一段就是磁悬浮场。与更早那版量的「整件底座浮在兽背上方」不是同一件事。
        var fieldGap = LevitationY - (LevitationBaseY + LevitationBaseHeight);

        // 兽 ↔ 地标：逐件量两个 AABB 的分离量，取最小的一件。
        //
        // **按各自在世界轴上的半宽算**：巨兽节点每帧转向航向，它模型里那两条边在世界上是斜着的
        // （停靠那一档把兽身摆到横过来，见 `ParkingHeading`，于是模型那条 52.93 米的短边正对城门法线）。
        // 按模型自己的轴算会漏掉偏航 —— 那一格算出来的净空与画面对不上。
        //
        // 每一轴的分离量取大者就是真实的最近距离：任一轴为负且另一个也为负才是真重叠。
        var beastHalf = WorldHalvesOf(CurrentBeastView);
        var beastSize = FootprintOf(CurrentBeastView);
        var worst = float.MaxValue;
        var worstId = "-";
        var cityHalfZ = 0f;
        foreach (var child in _landmarks.GetChildren())
        {
            if (child is not Node3D node) continue;
            var half = WorldHalvesOf(node);
            if (node.Name.ToString() == BigMapGrid.PoorCity.Id) cityHalfZ = half.Z;
            var gap = Separation(node.Position, half, _deckRoot.Position, beastHalf);
            if (gap >= worst) continue;
            worst = gap;
            worstId = node.Name.ToString();
        }
        if (worstId == "-") worst = 0f;

        string F(float v) => v.ToString("0.0", CultureInfo.InvariantCulture);
        return $"beastHalfX={F(beastSize.Width * 0.5f)} beastHalfZ={F(beastSize.Depth * 0.5f)} " +
               $"cityHalfZ={F(cityHalfZ)} beastTopY={F(beastTop)} " +
               $"baseY={F(LevitationBaseY)} baseTop={F(LevitationBaseY + LevitationBaseHeight)} " +
               $"fieldGap={F(fieldGap)} landmarkGap={F(worst)}({worstId}) " +
               $"routeZ={F(RouteLineWorldZ)}";
    }

    /// <summary>
    /// 一个模型在世界 X / Z 两轴上各占的半宽（米）—— 按 AABB 实测，
    /// 再按它所在节点的偏航转到世界轴上（巨兽节点每帧转向航向，模型那两条边在世界上是斜着的）。
    /// </summary>
    private static (float X, float Z) WorldHalvesOf(Node3D view)
    {
        var size = FootprintOf(view);
        var hx = size.Width * 0.5f;
        var hz = size.Depth * 0.5f;
        var yaw = view.GlobalRotation.Y;
        var cos = MathF.Abs(MathF.Cos(yaw));
        var sin = MathF.Abs(MathF.Sin(yaw));
        return ((cos * hx) + (sin * hz), (sin * hx) + (cos * hz));
    }

    /// <summary>
    /// 两个水平 AABB 之间的分离量（米）—— 负数即重叠。
    /// 沿每一轴各按自己的半宽算一次，取大者：那一轴上两者已经分开了，就不必再看另一轴。
    /// </summary>
    private static float Separation(Vector3 a, (float X, float Z) aHalf, Vector3 b, (float X, float Z) bHalf)
    {
        var dx = MathF.Abs(a.X - b.X) - (aHalf.X + bHalf.X);
        var dz = MathF.Abs(a.Z - b.Z) - (aHalf.Z + bHalf.Z);
        return Math.Max(dx, dz);
    }

    /// <summary>巨兽顶点离地高度（米）—— 按**当前形态那件**模型的 AABB 实测，不按造型文档里的数写死。</summary>
    private float BeastTopY()
    {
        foreach (var node in EnumerateSelf(CurrentBeastView))
        {
            if (node is VisualInstance3D vi)
            {
                var a = vi.GetAabb();
                var g = vi.GlobalTransform;
                return g.Origin.Y + a.Position.Y + a.Size.Y;
            }
        }
        return 0f;
    }

    /// <summary>巨兽巡航速度（米每现实秒）。动画那一口收的是「米每游戏秒」，差一个 10 倍。</summary>
    private const float CruiseSpeedMetersPerSecond = (float)ArenaTuning.BeastSpeedMetersPerSecond;

    /// <summary>
    /// 一个现实秒折多少游戏秒 —— 「米每现实秒」换成「米每游戏秒」乘它。
    ///
    /// <para>口径只在这里定一次：「时间基准」说一个逻辑步是 0.1 秒，而一步也正好是 1 游戏秒 ——
    /// 所以 1 现实秒 = 10 游戏秒 ×（倍率）。倍率归 <see cref="ModelAnimator.GameSpeed"/> 管
    /// （它乘在 AnimationPlayer 的播放速度上），不进这一格。
    /// 场上另一处同样的换算是 <see cref="SyncUnits"/> 里 <c>SpeedOf(u.Role) × 10</c>，
    /// 两处同源：改一处要改两处。</para>
    /// </summary>
    private const float GameSecondsPerRealSecond = 10.0f;

    /// <summary>
    /// 悬浮层 / 位姿读数 —— 「小镇脚下是不是平的」「巨兽在大地图哪一格」这两格的依据。
    ///
    /// <para>抖不抖是**时间上**的事，快照看不出来（任何一帧小镇都稳稳落在盘上），
    /// 只有连续采样才判得准；这一格把「位姿在渲染帧率上连续」这件事的两个必要条件显式报出来 ——
    /// 不足一步的补偿量（<c>frac</c>）与是否在推进（<c>walking</c>）。两者任一为 0，台阶就回来。</para>
    /// </summary>
    public string LevitationReadout() => _poseReadout;

    /// <summary>巨兽动画读数（转发 <see cref="ModelAnimator.DebugReadout"/>）。</summary>
    public string BeastAnimReadout() => _beastAnim?.DebugReadout() ?? "no-animator";

    // === 三类摆位 ===

    /// <summary>
    /// 十三件建筑：<b>一册一栋</b>，种类读蓝图 id，落点读册上那个锚点 ——
    /// 本层不读任何槽位表，也不自己定坐标（「表现层只读」）。
    /// 蓝图 id 与 <c>Models/*.glb</c> 的文件名一一对应（「按 res:// 引用」）。
    /// 处在「法术停滞」悬空态的那几栋局部 <c>Y</c> 抬 <see cref="LiftHeight"/> 米。
    /// </summary>
    private void SyncBuildings(ArenaWorld world)
    {
        _lifted.Clear();
        foreach (var id in world.Deck.Lifted) _lifted.Add(id);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in world.Construction.Ids)
        {
            var b = world.Construction.Find(id);
            if (b is null) continue;

            seen.Add(id);
            var spec = DeckBlueprints.SpecOf(b.BlueprintId);
            var width = spec?.Width ?? 1;
            var height = spec?.Height ?? 1;
            var anchor = b.Anchor;
            // 锚点是占地里最小行最小列那一格；模型摆在地盘中心（格心 col+0.5 起算，故落点 col + 宽/2）。
            // **格号要乘格长才是米** —— 甲板那张网格 2026-10-15 从 1 米一格放大到 2 米一格（格数不变），
            // 拿格号直接当米用会让每一栋楼都摆在实际位置的四分之一处、互相压穿。
            var cell = DeckBlueprints.CellSize;
            var x = ((anchor?.Col ?? 0) + (width / 2.0)) * cell;
            var z = ((anchor?.Row ?? 0) + (height / 2.0)) * cell;
            var at = new Vector3((float)x, _lifted.Contains(id) ? TownY + LiftHeight : TownY, (float)z);
            var current = new BuildSeen(b.BlueprintId, b.Health, b.MaxHealth, b.State, at, width, height);

            var first = !_buildSeen.TryGetValue(id, out var prev);
            var model = ModelNameOf(current);
            if (!_buildingViews.TryGetValue(id, out var view) || _buildingModels.GetValueOrDefault(id) != model)
            {
                if (view is not null) Free(view);
                view = LoadModel(model, _levitation, id) ?? LoadModel(b.BlueprintId, _levitation, id);
                if (view is null) continue;
                _buildingViews[id] = view;
                _buildingModels[id] = model;
            }

            view.Position = at;

            if (!first && prev is not null)
            {
                var name = spec?.Name ?? b.BlueprintId;
                if (prev.State == BuildState.Building && current.State == BuildState.Completed)
                {
                    _show.Fire(ShowCategory.Build, view, Loc.Fmt("建成 · {0}", Loc.T(name)), 8f);
                }
                else if (current.Health < prev.Health - 1e-9)
                {
                    var percent = current.MaxHealth <= 0
                        ? 0
                        : (int)Math.Round(current.Health / current.MaxHealth * 100.0);
                    _show.Fire(ShowCategory.Damage, view,
                        Loc.Fmt("受损 · {0} {1}%", Loc.T(name), percent.ToString(CultureInfo.InvariantCulture)), 8f);
                }
                else if (current.Health > prev.Health + 1e-9 && current.Health >= current.MaxHealth - 1e-9)
                {
                    _show.Fire(ShowCategory.Build, view, Loc.Fmt("修好 · {0}", Loc.T(name)), 8f);
                }
            }

            _buildSeen[id] = current;

            // 仓库名牌（「魔晶三种位置」的仓库那一档）：挂在镇政府上空，数字跟 CrystalWarehouse 走。
            if (id == ArenaWorld.TownHallId
                && (_warehousePlate is null || !GodotObject.IsInstanceValid(_warehousePlate.Label)))
            {
                _warehousePlate = new Plate(
                    MakeWorldPlate(view, "WarehousePlate", new Vector3(0f, 11f, 0f)),
                    string.Empty);
            }
        }

        foreach (var stale in _buildingViews.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            // 拆掉的建筑留一件废墟（只在它曾经完工过时留；施工中途取消不留残骸）。
            if (_buildSeen.TryGetValue(stale, out var last) && last.State == BuildState.Completed
                && !_ruinViews.ContainsKey(stale))
            {
                var ruin = LoadModel(RuinNameOf(last.Width, last.Height), _levitation, "ruin_" + stale);
                if (ruin is not null)
                {
                    ruin.Position = last.At;
                    _ruinViews[stale] = ruin;
                    var spec = DeckBlueprints.SpecOf(last.BlueprintId);
                    _show.Fire(ShowCategory.Damage, ruin,
                        Loc.Fmt("损毁 · {0}", Loc.T(spec?.Name ?? last.BlueprintId)), 8f);
                }
            }

            Free(_buildingViews[stale]);
            _buildingViews.Remove(stale);
            _buildingModels.Remove(stale);
            _buildSeen.Remove(stale);
        }
    }

    /// <summary>建筑这一刻该用哪件模型：施工中 → 脚手架；受损 → 受损态；健康 → 完成态。</summary>
    private static string ModelNameOf(BuildSeen seen)
    {
        if (seen.State == BuildState.Building) return ScaffoldNameOf(seen.Width, seen.Height);
        if (seen.Health < seen.MaxHealth - 1e-9) return seen.BlueprintId + "Damaged";
        return seen.BlueprintId;
    }

    /// <summary>占地三档 → 脚手架 / 废墟三档（规格「资产范围」：按占地分档，不为十三种建筑各做一套）。</summary>
    private static string ScaffoldNameOf(int width, int height) => (width * height) switch
    {
        <= 4 => "ScaffoldSmall",
        <= 9 => "ScaffoldMedium",
        _ => "ScaffoldLarge",
    };

    private static string RuinNameOf(int width, int height) => (width * height) switch
    {
        <= 4 => "RuinSmall",
        <= 9 => "RuinMedium",
        _ => "RuinLarge",
    };

    /// <summary>
    /// 在场活物：种类读 <see cref="UnitRole"/>，位置读活物册那两列，动作交给
    /// <see cref="ModelAnimator"/> 翻译（「动作在模型」：原型不自己写四肢摆动）。
    /// 被 <c>GrabEntity</c> 抓起的那个局部 <c>Y</c> 抬 <see cref="LiftHeight"/> 米。
    ///
    /// <para><b>阵亡不立刻收模型</b>：逻辑层一判死就有尸体点位物了，但表现层要让 <c>death</c>
    /// 演完再收（A-H6 / A-M6）；收早了「死了」这件事只剩一具静态尸体，动作一条都看不到。</para>
    /// </summary>
    private void SyncUnits(ArenaWorld world, float gameSpeed)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // 新模型分帧接：一次接满五十个会把主线程冻住十几秒（实测 14.5 秒）。
        // 每一趟只给一小段预算，接不完的下一步再来 —— 单位与人口规模成正比的那点开销必须让出主线程。
        var bindStartUsec = Time.GetTicksUsec();
        var bound = 0;

        foreach (var u in world.Units)
        {
            if (u.State == UnitState.Dead)
            {
                if (!_unitViews.TryGetValue(u.Id, out var dead))
                {
                    _unitSeen.Remove(u.Id);
                    continue;
                }

                seen.Add(u.Id);
                if (!_deadAt.TryGetValue(u.Id, out var at))
                {
                    var seconds = dead.Anim.Die();
                    _deadAt[u.Id] = (long)Time.GetTicksMsec()
                        + (long)(Math.Max(seconds, 0.8f) / Math.Max(gameSpeed, 0.01f) * 1000f);
                    _show.Marker(dead.View, Loc.T("阵亡"), 2.6f);
                }
                else if ((long)Time.GetTicksMsec() >= at)
                {
                    Free(dead.View);
                    _unitViews.Remove(u.Id);
                    _unitPlates.Remove(u.Id);
                    _unitSeen.Remove(u.Id);
                    _deadAt.Remove(u.Id);
                }

                continue;
            }

            // 离镇的人（游客到点走、饥饿脱队）在逻辑名单里还留着，画面上不能留：
            // 节点只在「人还在镇」这一段存在。不留这一步，每日一批游客按新 ID 累积，
            // 节点只增不减 —— 75 分钟长跑的节点增幅就是从这里来的。
            if (u.LeftTown)
            {
                if (_unitViews.TryGetValue(u.Id, out var gone))
                {
                    Free(gone.View);
                    _unitViews.Remove(u.Id);
                }
                _unitPlates.Remove(u.Id);
                _unitSeen.Remove(u.Id);
                _deadAt.Remove(u.Id);
                continue;
            }

            seen.Add(u.Id);
            if (!_unitViews.TryGetValue(u.Id, out var entry))
            {
                var spentMs = (Time.GetTicksUsec() - bindStartUsec) / 1000.0;
                if (bound >= BindMaxPerCall || spentMs >= BindBudgetMs)
                {
                    continue;   // 下一趟再接（分帧），这一趟不建模型
                }

                bound++;
                var model = LoadModel(ModelOf(u.Role), _levitation, u.Id);
                if (model is null) continue;
                entry = (model, new ModelAnimator(model));
                _unitViews[u.Id] = entry;

                // 洞穴涌怪：清剿开场那一两个逻辑步里新建的那几只补一条出土动作。
                if (_emergeUntilTick >= world.Tick && u.Role == UnitRole.Monster)
                    entry.Anim.PlayOneShot("emerge");
            }

            entry.Anim.GameSpeed = gameSpeed;
            entry.View.Position = new Vector3(
                (float)u.DeckX, u.Held ? TownY + LiftHeight : TownY, (float)u.DeckZ);

            // 活物名牌（spec「人物职业、岗位、当前行为」）：牌挂在模型头顶跟着走，
            // 文字每个逻辑步现读现刷（「表现层只读」：只读世界，一个字不往世界里写）。
            if (!_unitPlates.TryGetValue(u.Id, out var plate))
            {
                plate = new Plate(
                    MakeWorldPlate(entry.View, "Plate", new Vector3(0f, 2.6f, 0f)),
                    string.Empty);
            }

            _unitPlates[u.Id] = Apply(plate, UnitPlateText(world, u));

            // 掉血：受击那一条 clip + 命中特效（音效按 80 毫秒同 cue 合并，不刷屏）。
            if (_unitSeen.TryGetValue(u.Id, out var before) && u.Hp < before.Hp - 1e-9)
            {
                entry.Anim.PlayOneShot("hit");
                _show.Spark(ShowCategory.Battle, entry.View);
            }

            _unitSeen[u.Id] = new UnitSeen(u.Hp, u.State);

            // 速度按「米每游戏秒」报给翻译层：逻辑层那一格是米每现实秒，×10 换算（1 步 = 0.1 秒）。
            // **站着报 0** —— 重伤倒地的报 0，站在原地没动的也报 0，那一条是 `u.Moving`
            // （真值在寻路层那一侧的表现：本步有没有真走出去）。少了这一条，
            // 每个站着的人都在原地播走路动作，画面上就是五十个人齐步走 ——
            // 「显示不是真相」（「表现层只读」要防的那一类）。
            entry.Anim.Travel(u.State == UnitState.Wounded || !u.Moving
                ? 0f
                : (float)(PeopleRoster.SpeedOf(u.Role) * GameSecondsPerRealSecond));
        }

        // 出手：战斗结算这一格只有读数没有逐次事件（G-13），本步打出伤害的那几个攻方各补一条平A。
        foreach (var pair in world.Battle.Pairs)
        {
            if (pair.LastDamage <= 0) continue;
            foreach (var attacker in pair.Attackers)
            {
                if (_unitViews.TryGetValue(attacker.Id, out var view)) view.Anim.TriggerAttack();
            }
        }

        foreach (var id in _unitViews.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            Free(_unitViews[id].View);
            _unitViews.Remove(id);
            _unitPlates.Remove(id);
            _unitSeen.Remove(id);
            _deadAt.Remove(id);
        }
    }

    /// <summary>
    /// 三类点位物（尸体 / 地面散货 / 魔晶）：种类读 <see cref="PointItemKind"/>，落点读那本册。
    /// 已搬走的（<c>Removed</c>）模型跟着收走。
    /// 魔晶那一类多挂一块名牌（spec「魔晶在甲板」一档）—— 它与地面散货共用 <c>Cargo.glb</c>，
    /// 不挂牌在画面上分不出来；搬运中那一档写在搬运人的名牌上，仓库那一档写在镇政府的仓库牌上。
    /// </summary>
    private void SyncItems(ArenaWorld world)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in world.Items.Live)
        {
            seen.Add(item.Id);
            if (!_itemViews.TryGetValue(item.Id, out var view))
            {
                view = LoadModel(ModelOf(item.Kind), _levitation, item.Id);
                if (view is null) continue;
                _itemViews[item.Id] = view;
            }

            view.Position = new Vector3((float)item.DeckX, TownY, (float)item.DeckZ);

            if (item.Kind != PointItemKind.Crystal) continue;
            if (!_itemPlates.TryGetValue(item.Id, out var plate))
            {
                plate = new Plate(
                    MakeWorldPlate(view, "Plate", new Vector3(0f, 1.4f, 0f)),
                    string.Empty);
            }

            _itemPlates[item.Id] = Apply(plate, Loc.T("魔晶 · 甲板"));
        }

        foreach (var stale in _itemViews.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            Free(_itemViews[stale]);
            _itemViews.Remove(stale);
            _itemPlates.Remove(stale);
        }
    }

    /// <summary>
    /// 停靠期与空艇窗口那两件场景道具：<c>Pontoon</c> 只在停靠期显示于甲板南侧边缘，
    /// <c>Airship</c> 只在空艇成交窗口内显示于空船停靠区旁（没有那一栋就落到甲板东侧上空）。
    /// 两件都是**造一次、只切可见性**，不按帧重建。
    /// </summary>
    private void SyncDockedProps(ArenaWorld world)
    {
        _pontoon ??= LoadModel("Pontoon", _levitation, "Pontoon");
        if (_pontoon is not null)
        {
            _pontoon.Visible = world.IsDocked;
            var pontoon = DeckGeometry.Pontoon();
            _pontoon.Position = new Vector3((float)pontoon.X, TownY, (float)pontoon.Z);
        }

        _airship ??= LoadModel("Airship", _levitation, "Airship");
        if (_airship is not null)
        {
            _airship.Visible = world.InAirshipWindow;
            _airship.Position = AirshipSpot(world);
        }
    }

    /// <summary>空艇落点：有空船停靠区就停它东侧，没有就停在甲板东侧上空（米，甲板局部系）。</summary>
    private Vector3 AirshipSpot(ArenaWorld world)
    {
        var dock = world.Construction.Find("airship_dock");
        if (dock?.Anchor is { } anchor)
        {
            var spec = DeckBlueprints.SpecOf(dock.BlueprintId);
            var cell = DeckBlueprints.CellSize;
            // 格号 × 格长 = 米，与 SyncBuildings 那一处同一把尺子。
            var x = (anchor.Col + (spec?.Width ?? 3) + 2.0) * cell;
            var z = (anchor.Row + ((spec?.Height ?? 3) / 2.0)) * cell;
            return new Vector3((float)x, TownY + AirshipHoverHeight, (float)z);
        }

        // 甲板边那一档（格 -12..11、格长 2 米 ⇒ 甲板逻辑坐标 -23..23）之内侧一点。
        return new Vector3(19.0f, TownY + AirshipHoverHeight, 0f);
    }

    // === T14：地下两层、游客船与世界陈设 ===

    /// <summary>生态名牌高度（米，按类别压在各自剪影之上）：树 9.5 / 兽群 3.4 / 矿石 5.2。</summary>
    private static float EcoPlateHeight(EcoKind kind) => kind switch
    {
        EcoKind.Tree => 9.5f,
        EcoKind.Herd => 3.4f,
        _ => 5.2f,
    };

    /// <summary>兽群单点的三具摆布（甲板外的世界摆位，米，相对点位原点；偏航度）。</summary>
    private static readonly (float X, float Z, float Yaw)[] HerdLayout =
    [
        (-2.2f, 0.0f, 0.6f),
        (2.1f, 1.5f, 3.0f),
        (0.6f, -2.3f, 4.4f),
    ];

    /// <summary>远方高塔的三处摆位（世界米）：全在圆盘内、道路与水系之外。</summary>
    private static readonly (float X, float Z)[] DistantTowerSpots =
    [
        (200f, 435f),
        (30f, 330f),
        (370f, 300f),
    ];

    /// <summary>远景飞艇的两处摆位（世界米 + 悬浮高度，米）：复用 Airship 一件。</summary>
    private static readonly (float X, float Y, float Z)[] FarAirshipSpots =
    [
        (80f, 85f, 380f),
        (330f, 110f, 330f),
    ];

    /// <summary>
    /// 地下两层模块在浮山腰侧的断面（甲板局部系，米）。
    ///
    /// <para><b>为什么摆在这里</b>：地下住在甲板象限 0 的两层里（`UndergroundSoil`：2 层 × 4 象限、
    /// 单层 3 米）；浮山是倒三角，山腰往下越来越窄，于是把第 2 层（洞穴层）露在岩体边缘外、
    /// 第 1 层压进岩体里 —— 低机位从下往上一眼看得出「山里挖了两层」，
    /// 三节井筒（GroundExit 一节 6.5 米）从第 1 层接回镇地板底，那就是「地面出口」。</para>
    ///
    /// <para><b>状态只切显隐</b>：地块 ↔ 空腔按 Dug 切换；洞口与工作面挖开才亮；
    /// 嵌入态 = 魔晶件摆进嵌入构件的环心。八格状态的真值都在 `UndergroundSoil`，这里一个都不改。</para>
    /// </summary>
    private void BuildUnderground()
    {
        var depth = (float)ArenaTuning.SoilLayerDepthMeters;
        Node3D? Place(string model, string nodeName, float x, float y, float z, float yawDegrees = 0f)
        {
            var view = LoadModel(model, _levitation, nodeName);
            if (view is null) return null;
            view.Position = new Vector3(x, y, z);
            if (yawDegrees != 0f) view.Rotation = new Vector3(0f, yawDegrees * (MathF.PI / 180f), 0f);
            return view;
        }

        // 两层地块：第 2 层（洞穴层）落在岩体边缘外，第 1 层压进山腰（同一格的两层上下相叠）。
        _soilBlockL2 = Place("SoilBlock", "UndergroundSoilL2", UndergroundX, 0f, 0f);
        _cavernL2 = Place("CavernTile", "UndergroundCavernL2", UndergroundX, 0f, 0f);
        _soilBlockL1 = Place("SoilBlock", "UndergroundSoilL1", UndergroundX, depth, 0f);
        _cavernL1 = Place("CavernTile", "UndergroundCavernL1", UndergroundX, depth, 0f);
        if (_cavernL2 is not null) _cavernL2.Visible = false;
        if (_cavernL1 is not null) _cavernL1.Visible = false;

        // 洞口朝 +Z（通道方向）摆第 2 层格前缘；工作面摆第 1 层格后缘，支护与碎石堆探进格内。
        _caveMouth = Place("CaveMouth", "UndergroundCaveMouth", UndergroundX, 0f, 6.5f);
        _miningFace = Place("MiningFace", "UndergroundMiningFace", UndergroundX, depth, -7.2f);
        _crystalSocket = Place("CrystalSocket", "UndergroundCrystalSocket", UndergroundX, 0.5f, 0f);
        _crystalEmbedded = Place("CrystalShard", "UndergroundCrystal", UndergroundX, 1.8f, 0f);
        if (_caveMouth is not null) _caveMouth.Visible = false;
        if (_miningFace is not null) _miningFace.Visible = false;
        if (_crystalSocket is not null) _crystalSocket.Visible = false;
        if (_crystalEmbedded is not null) _crystalEmbedded.Visible = false;

        // 地面出口：三节井筒首尾相接，从第 1 层顶（6 米）一直接到镇地板底（26 米）那一侧。
        for (var i = 0; i < 3; i++)
            Place("GroundExit", $"UndergroundShaft{i}", UndergroundX + 4.2f, 6f + i * 6.5f, 0f);
    }

    /// <summary>地下断面横摆的 X（甲板局部，米）：第 2 层整格露在岩体边缘外，第 1 层一半埋进山腰。</summary>
    private const float UndergroundX = 10f;

    /// <summary>按 `UndergroundSoil` 象限 0 的读数切地下断面的显隐（开挖 / 清剿 / 嵌入三态；表现层只读）。</summary>
    private void SyncUnderground(ArenaWorld world)
    {
        var layer1 = world.Soil.CellAt(0, 1);
        var layer2 = world.Soil.CellAt(0, 2);

        if (_soilBlockL1 is { } s1) s1.Visible = !layer1.Dug;
        if (_cavernL1 is { } c1) c1.Visible = layer1.Dug;
        if (_soilBlockL2 is { } s2) s2.Visible = !layer2.Dug;
        if (_cavernL2 is { } c2) c2.Visible = layer2.Dug;
        if (_caveMouth is { } mouth) mouth.Visible = layer2.Dug;
        if (_miningFace is { } face) face.Visible = layer1.Dug;
        if (_crystalSocket is { } socket) socket.Visible = layer2.Dug;
        if (_crystalEmbedded is { } crystal) crystal.Visible = layer2.Embedded;
    }

    /// <summary>游客船：停靠期停到当前城门外的上空（沿外法线再退 15 米、高 40 米），拔锚即收。</summary>
    private void SyncTouristShip(ArenaWorld world)
    {
        if (_touristShip is not { } ship) return;

        if (!world.IsDocked || world.DockedPlace is not { } place)
        {
            ship.Visible = false;
            return;
        }

        ship.Visible = true;
        ship.Position = new Vector3(
            (float)(place.Position.X + place.OutwardNormal.X * 15.0),
            40f,
            (float)(place.Position.Z + place.OutwardNormal.Z * 15.0));
    }

    // === 世界内标记（spec「信息结构」四条；读数格按对接面清单「世界标记」表） ===

    /// <summary>一块头顶名牌：面向相机的字，不遮挡模型也不投影 —— 陈设，不是造型（「真模型」的判据不碰它）。</summary>
    /// <remarks>PixelSize 0.012 是出图复核量出来的两档之间取的一格：0.005（默认）在 140 米战略档
    /// 完全读不出，0.02 在 60 米战术档一个字糊掉半条街 —— 0.012 下战术档一行名牌约一人高、
    /// 战略档勉强可辨（T15 实测）。</remarks>
    private static Label3D MakeWorldPlate(Node3D parent, string name, Vector3 at)
    {
        var label = new Label3D
        {
            Name = name,
            Position = at,
            PixelSize = 0.012f,
            FontSize = 40,
            OutlineSize = 8,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            RenderPriority = 6,
            Modulate = new Color(0.92f, 0.95f, 0.98f),
        };
        parent.AddChild(label);
        return label;
    }

    /// <summary>文字变了才重写那一块牌（Label3D 重设文字会重排版一次，名牌每个逻辑步刷一遍、多数步该无事发生）。</summary>
    private static Plate Apply(Plate plate, string text)
    {
        if (plate.Last != text)
        {
            plate.Label.Text = text;
            return plate with { Last = text };
        }

        return plate;
    }

    /// <summary>
    /// 世界名牌每步一刷：生态可吞状态、洞穴与回长与嵌入、仓库魔晶 ——
    /// 全部只读世界（「表现层只读」），活物与点位物的名牌在 <see cref="SyncUnits"/> 与
    /// <see cref="SyncItems"/> 里跟着摆位一起刷。
    /// </summary>
    private void SyncWorldMarkers(ArenaWorld world)
    {
        foreach (var node in world.Ecology.Nodes)
        {
            if (!_ecoPlates.TryGetValue(node.Id, out var plate)) continue;
            _ecoPlates[node.Id] = Apply(plate, EcoPlateText(world, node));
        }

        if (_cavePlate is { } cave)
        {
            _cavePlate = Apply(cave, CavePlateText(world));
        }

        if (_warehousePlate is { } house && GodotObject.IsInstanceValid(house.Label))
        {
            _warehousePlate = Apply(house, Loc.Fmt("魔晶 · 仓库 {0}",
                world.CrystalWarehouse.ToString(CultureInfo.InvariantCulture)));
        }
    }

    /// <summary>生态名牌：类别 + 可吞 / 再生剩余游戏分钟 / 已耗尽（口径与 <see cref="BeastEcology"/> 的 state 栏同源）。</summary>
    private static string EcoPlateText(ArenaWorld world, EcoNode node)
    {
        var kind = node.Kind switch
        {
            EcoKind.Tree => Loc.T("树木"),
            EcoKind.Herd => Loc.T("兽群"),
            _ => Loc.T("矿石"),
        };

        if (node.AvailableAt(world.Tick)) return kind + " · " + Loc.T("可吞");
        if (node.Consumed) return kind + " · " + Loc.T("已耗尽");

        var minutes = (long)Math.Ceiling(
            (node.AvailableAtTick - world.Tick) / (double)ArenaTime.StepsPerGameMinute);
        return kind + " · " + Loc.Fmt("再生剩 {0} 游戏分钟", minutes);
    }

    /// <summary>洞穴名牌：未挖 / 挖开中 / 剩几只怪 / 已清剿 / 已嵌入 + 回长进度（地下八格不画体量，洞口借门口那一格上屏）。</summary>
    private static string CavePlateText(ArenaWorld world)
    {
        var cell = world.Soil.CellAt(UndergroundSoil.CaveSector, UndergroundSoil.NativeCaveLayer);
        if (!cell.Dug)
        {
            return cell.Digging ? Loc.T("洞穴 · 挖开中") : Loc.T("洞穴 · 未挖开");
        }

        if (cell.Embedded) return Loc.T("洞穴 · 已嵌入 · 不回长");

        var status = cell.Cleared
            ? Loc.T("已清剿")
            : Loc.Fmt("剩 {0} 怪", cell.CaveMonsters.ToString(CultureInfo.InvariantCulture));
        return Loc.Fmt("洞穴 · {0} · 回长 {1}/1 日", status,
            cell.RegrowthProgress.ToString("0.00", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// 活物名牌：职业 + 当前行为（重伤 / 搬运 / 行走 / 在岗带岗位名 / 停立）。
    /// 职业名复用 <see cref="SelectionPanel.RoleNameOf"/>（同一份词，不另写一套）。
    /// </summary>
    private static string UnitPlateText(ArenaWorld world, ArenaUnit u)
    {
        var role = SelectionPanel.RoleNameOf(u.Role);

        if (u.State == UnitState.Wounded) return role + " · " + Loc.T("重伤倒地");

        var haul = world.CarryingOf(u.Id);
        if (haul is not null)
        {
            var crystal = world.Items.Find(haul.ItemId)?.Kind == PointItemKind.Crystal;
            return role + " · " + (crystal ? Loc.T("搬运魔晶") : Loc.T("搬运中"));
        }

        if (u.Moving) return role + " · " + Loc.T("行走中");
        if (u.Post.Length > 0) return role + " · " + Loc.Fmt("在岗 · {0}", PostPlateName(world, u.Post));
        return role + " · " + Loc.T("停立");
    }

    /// <summary>岗位那一格的中文名：建筑 id 先经建造册翻成蓝图名，翻不出来就原样印 id（与选中面板同一口径）。</summary>
    private static string PostPlateName(ArenaWorld world, string post)
    {
        if (post.StartsWith("soil:", StringComparison.Ordinal)) return Loc.T("土壤");

        var blueprintId = world.Construction.Find(post)?.BlueprintId ?? post;
        return Loc.T(DeckBlueprints.SpecOf(blueprintId)?.Name ?? post);
    }

    // === HUD（常驻五组 + 三行辅助读数，VBoxContainer 一组一个组名 + 一行 Label） ===

    /// <summary>
    /// HUD 读数 —— 常驻五组（spec「信息结构」）：当前目标、下一站、关键资源、双成长进度、饱食 / 消化 / 形态；
    /// 另有罗盘时钟、立场（名声与负债）、人口三行辅助读数。读数格与对接面清单「HUD」表逐行对齐。
    ///
    /// <para>账本数字与法力那一列直接转发 <see cref="TownAccounts.ResourcesText"/> 与
    /// <see cref="TownAccounts.ManaText"/>（真值只有逻辑层那一处），这里不另算一遍；
    /// 时钟行那一格是把几个标记（破镇 / 停摆 / 停靠点 / 通缉）拼成一句给人读的话。</para>
    ///
    /// <para><b>「在镇游客与战斗摘要」一行按清单 HUD 表去掉</b>：战斗摘要归命令栏的防守倒计时
    /// 与战报面板，游客数归调试读数（<c>ArenaSnapshot.Readout</c>）。</para>
    /// </summary>
    private void UpdateHud(ArenaWorld world)
    {
        if (_clockRow is null || _accountRow is null || _standingRow is null) return;

        _clockRow.Text = Loc.Fmt("{0} · {1} · 巨兽 {2}",
            world.CompassText(), PhaseText(world), world.Voyage.Phase.ToString().ToLowerInvariant());

        if (_goalRow is not null)
        {
            _goalRow.Text = Loc.Fmt("{0} · 阶段 {1}", world.Flow.Goal, world.Flow.PhaseLabel);
        }

        if (_stationRow is not null)
        {
            _stationRow.Text = Loc.Fmt("下一站 {0}", NextStationText(world));
        }

        // 四通货（花钱与卖的那些）与三类货（仓里那些 + 法力格）分两段摆。
        // 魔晶单列一格：它在仓库 / 甲板 / 搬运中三处各有一本账，HUD 只报仓库那一本，细分归巨兽面板。
        var town = world.Town;
        _accountRow.Text = Loc.Fmt(
            "四通货 金币 {0} 魂石 {1} 矿石 {2} 特殊材料 {3} 魔晶 {4} · 三类货 食物 {5} 备件 {6} 法力 {7}",
            Num(town.Coin), Num(town.SoulStone), Num(town.Ore), Num(town.SpecialMaterial),
            world.CrystalWarehouse.ToString(CultureInfo.InvariantCulture),
            Num(town.Food), town.SparePart.ToString(CultureInfo.InvariantCulture), town.ManaText());

        if (_beastRow is not null)
        {
            _beastRow.Text = Loc.Fmt("位阶 {0} · 形态 {1} · 饱食 {2}/{3} · 消化 {4}/{5}（已产魔晶 {6}）",
                Loc.T(BeastTierText.NameOf(world.Tier)),
                Loc.T(BeastFormText.NameOf(world.Form)),
                Num(world.Ecology.Satiation),
                Num(ArenaTuning.SatiationMax),
                Num(world.Ecology.DigestionOverflow),
                Num(ArenaTuning.DigestionPerCrystal),
                world.Ecology.CrystalsProduced.ToString(CultureInfo.InvariantCulture));
        }

        if (_growthRow is not null)
        {
            _growthRow.Text = Loc.Fmt(
                "双成长 {0}（二阶 {1}/2 · 暴食态 {2} · 首切 {3}） · 形态进度 荒漠 {4}/{5} 分钟 · 暴食 {6}/{7} 食物",
                world.Flow.DoubleGrowthComplete ? Loc.T("已达成") : Loc.T("未达成"),
                ((int)world.Growth.Tier).ToString(CultureInfo.InvariantCulture),
                world.Growth.GluttonFormUnlocked ? Loc.T("已解锁") : Loc.T("未解锁"),
                world.Flow.FirstSwitchTick >= 0 ? Loc.T("已切过") : Loc.T("未切过"),
                world.Growth.DwellGameMinutes.ToString(CultureInfo.InvariantCulture),
                ArenaTuning.DesertFormDwellGameMinutes.ToString(CultureInfo.InvariantCulture),
                Num(world.Growth.FedTotalFood),
                ArenaTuning.GluttonFormFedTotalFood.ToString(CultureInfo.InvariantCulture));
        }

        _standingRow.Text = Loc.Fmt("名声 镇 {0} / 个人 {1} · 负债 {2} · 策略 {3}",
            world.Visitors.TownReputation.ToString("0", CultureInfo.InvariantCulture),
            TopStarReputation(world),
            world.Town.TotalDebt.ToString("0", CultureInfo.InvariantCulture),
            world.Town.Policy.ToString().ToLowerInvariant());

        if (_populationRow is not null)
        {
            _populationRow.Text = Loc.Fmt("人口 工人 {0} · 角斗士 {1} · 自由人 {2} · 帮工 {3} · 在岗 {4}",
                world.Roster.WorkerCount,
                world.Roster.GladiatorCount,
                world.Roster.FreePersonCount,
                world.Roster.HelperCount,
                PostedCount(world));
        }
    }

    /// <summary>下一站的城市名（富城是终点，回「无」）。</summary>
    private static string NextStationText(ArenaWorld world)
    {
        var id = world.Flow.NextCityId;
        if (id.Length == 0) return Loc.T("无（富城是终点）");

        foreach (var place in BigMapGrid.Places)
        {
            if (place.Id == id) return Loc.T(place.Name);
        }

        return id;
    }

    /// <summary>在岗人数：有岗位、还在场、没离镇的那几类镇民。</summary>
    private static int PostedCount(ArenaWorld world)
    {
        var posted = 0;
        foreach (var unit in world.Units)
        {
            if (unit.LeftTown || unit.State == UnitState.Dead) continue;
            if (unit.Role is not (UnitRole.Worker or UnitRole.Gladiator or UnitRole.FreePerson)) continue;
            if (unit.Post.Length > 0) posted++;
        }

        return posted;
    }

    /// <summary>一行里的账本数字：去掉小数点尾巴（不变文化，「double 与不变文化」）。</summary>
    private static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>阶段那一列：四个标记拼一句，顺序固定（破镇 / 停摆 / 停靠点 / 通缉）。</summary>
    private static string PhaseText(ArenaWorld world)
    {
        if (world.TownHallBreached) return Loc.T("镇政府已攻破");
        if (world.InStandstill) return Loc.T("停摆等城卫队解救");
        if (world.Wanted) return Loc.T("通缉中");
        if (world.IsDocked)
        {
            var place = world.DockedPlace;
            return place is null ? Loc.T("停靠中") : Loc.Fmt("停靠 {0}", place.Name);
        }

        return Loc.T("航行");
    }

    /// <summary>在册角斗士里个人名声最高的那一档（0 起步）。</summary>
    private static string TopStarReputation(ArenaWorld world)
    {
        var top = 0.0;
        foreach (var u in world.Units)
        {
            if (u.Role != UnitRole.Gladiator) continue;
            if (u.PersonalReputation > top) top = u.PersonalReputation;
        }

        return top.ToString("0", CultureInfo.InvariantCulture);
    }

    // === 小工具 ===

    /// <summary>大地图逻辑坐标 → 3D 世界坐标 —— 同一把尺子，逻辑坐标就是世界米。</summary>
    private static Vector3 ToWorld(NavPoint at)
        => new((float)at.X, 0f, (float)at.Z);

    /// <summary>
    /// 活物类别 → 模型名。外来活物（来敌与洞穴原生怪）共用怪物那一件 ——
    /// 差异做进模型本身，不在表现层换皮（「造型进模型」）。
    /// </summary>
    private static string ModelOf(UnitRole role) => role switch
    {
        UnitRole.Worker => "Worker",
        UnitRole.Gladiator => "Gladiator",
        UnitRole.FreePerson => "Citizen",
        UnitRole.Helper => "Helper",
        _ => "Monster",
    };

    /// <summary>
    /// 点位物类别 → 模型名。魔晶搬运小件走 T11 的专属件 <c>Crystal</c>（小件尺度，
    /// 配 1.4 米的头顶名牌）；地里的嵌入大晶是另一件 <c>CrystalShard</c>，只进地下嵌入构件。
    /// </summary>
    private static string ModelOf(PointItemKind kind) => kind switch
    {
        PointItemKind.Corpse => "Corpse",
        PointItemKind.Crystal => "Crystal",
        _ => "Cargo",
    };

    private static void Free(Node node)
    {
        if (GodotObject.IsInstanceValid(node)) node.QueueFree();
    }

    private static Node3D? LoadModel(string name, Node3D parent, string? nodeName = null)
    {
        var scene = GD.Load<PackedScene>($"res://src/Prototypes/BeastArena/Models/{name}.glb");
        if (scene is null) return null;
        var inst = scene.Instantiate<Node3D>();
        parent.AddChild(inst);
        if (nodeName is not null) inst.Name = nodeName;
        return inst;
    }
}

internal static class NodeExtensions
{
    /// <summary>拿到就用，不用判空 —— 这一层的调用点都是「有就摆、没有就跳过」。</summary>
    public static void Let(this Node3D node, Action<Node3D> action) => action(node);
}
