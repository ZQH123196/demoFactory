#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DemoFactory.GugaFramework.Abilities;
using DemoFactory.GugaFramework.Camera;
using DemoFactory.GugaFramework.DebugBridge;
using DemoFactory.GugaFramework.Vfx;
using Godot;

namespace DemoFactory.GugaFramework.Abilities.Stage;

/// <summary>
/// <b>场景壳</b>——引擎侧那一半：地面网格、标尺、机位、把事件画出来。纯表现，不碰真值。
///
/// 分工（spec「形态」一节的「两个壳」）：<b>逻辑壳 <c>SkillStage.cs</c> 是纯 C#、零引擎</b>，
/// 本文件只做「把那份真值画出来」。表现层只读（「表现层只读」）：本文件一个字都不往世界里写，
/// 推进时间走 <see cref="SkillStage.AdvanceRealSeconds"/>（<b>倍率与攒步那笔账在逻辑壳里</b>，
/// 这里不再自己算一遍——两处算就会漂），改状态一律走 <see cref="SkillStage.Inject"/> 那一条口。
///
/// 画面上五样，够看清就够（<b>先不带单位模型</b>——场地陈设与造型是两件事，「真模型」 管的是后者）：
/// <list type="number">
/// <item><b>地面网格</b>：一格一格画在地面上，兼作「这一下为什么是这个结果」的距离参照。</item>
/// <item><b>标尺</b>：每隔几米一根带数字的刻度——读距离靠它，不靠心算。</item>
/// <item><b>名牌 + 脚下色带 + 一条指向线</b>：每个单位三件。名牌念属性读数，
/// 脚下色带给一个可辨的落点，指向线画「谁在打谁」。</item>
/// <item><b>朝向那一小段</b>：每格从脚下朝自己朝向画 2 米——扇形界内界外那几格肉眼要能复核，
/// 靠的就是它（角度靠心算是不作数的）。</item>
/// <item><b>事件尾窗</b>：右上角逐行念事件，外加观测窗口读数里的 <c>dropped</c> 那一格
/// ——「日志里没有」≠「没发生」（「三条铁律」 第三条）。</item>
/// </list>
///
/// <b>机位三套预设</b>（「相机单源」：相机一律走既有的 <see cref="TopDownRig"/>，本文件只给参数）：
/// <list type="bullet">
/// <item><b>全景</b>（<see cref="StageView.Overview"/>）：整块场地进画，看范围与群体。<b>距离按场地尺寸推算</b>
/// （<see cref="FitDistance"/>），不再是一个量出来的常数。</item>
/// <item><b>跟随</b>（<see cref="StageView.Follow"/>）：锁定一个单位看它的动作。</item>
/// <item><b>特写</b>（<see cref="StageView.Closeup"/>）：贴到最近，看前摇那一小段动作。</item>
/// </list>
/// 「点中了谁、锁定跟随谁」是玩法语义，留在场子侧：<see cref="PickUnitAt"/>（中键单击）、
/// <see cref="FollowUnit"/> / <see cref="CycleLockUnit"/>（Tab 轮换）三条入口都只换「锁谁」，
/// 换完仍旧用 <see cref="TopDownRig"/> 那台相机去看。
/// <b>手动微调兜场</b>：预设对不上场合时用 <see cref="NudgePose"/> 或相机自己那套输入
/// （滚轮 / WASD / Q E / ↑↓ / 中键拖拽）——预设只定起点，不锁死。
///
/// <b>机位是表现层动词，不进录制</b>（「写路径进 Journal」）：切预设、锁谁、挪镜头走本文件这几个方法
/// 或 <see cref="TopDownRig"/> 自己的输入 action，<b>永远不走 <see cref="SkillStage.Inject"/></b>
/// ——所以挪镜头不会让重放误报分岔。机位参数也不进检查点快照（「试验场随技能层」）：
/// <see cref="SkillStage.DebugSnapshot"/> 是逻辑壳出的，那边根本没有相机。
///
/// <b>本类是基类，派生场子只递两样</b>（ticket 09）：<see cref="CreateSpec"/>（递哪一份注册口）
/// 与 <see cref="CreateStage"/>（起哪一个逻辑壳）。现役派生一份：
/// <see cref="SkillStageOpenFieldScene"/>（开阔多点）。陈设与机位三套预设全部住在本类，
/// 派生那一份一个字都不重画——抄一遍的代价与「三处逐行同构的转发器」是同一笔账。
/// </summary>
public partial class SkillStageScene : Node3D
{
    // === 机位参数（「相机单源」：场子只给焦点 / 距离 / 俯仰范围 / 边界这几个）===

    /// <summary>
    /// 机位偏航（度）：三套预设共用这一个「场子怎么摆」的方向。缺省 0。
    /// 距离与俯角<b>不在这里给</b>——那两格归三套预设，距离按场地尺寸推算。
    /// </summary>
    [Export] public float CameraYawDegrees { get; set; } = 0f;

    // === 三套预设的那几个数（全部有出处，见每一条的注释）===

    /// <summary>
    /// 全景俯角（度）：<b>近俯视</b>。地面透视压缩最小的一档——俯角越斜，远端被压得越扁
    /// （ticket 02 出图实测：70 度时 40 米见方的场地呈梯形，远端只剩一条线）。
    /// 顶到组件自己的上限 80 度是「正俯视」，那时绕转与剪影都失效，所以差两度收手。
    /// </summary>
    private const double OverviewPitchDegrees = 78;

    /// <summary>
    /// 跟随俯角（度）：斜到能看出前后层次（谁在前谁在后），又不把地面压平。
    /// ticket 02 那 70 度对「看整场」太斜，对「看一个单位的动作」又太陡。
    /// </summary>
    private const double FollowPitchDegrees = 58;

    /// <summary>
    /// 特写俯角（度）：三套里最低的一档，贴脸看动作。低到能看清单位前后谁挡谁，
    /// 再低就接近平视、地面射线与前后关系两头都退化了。
    /// </summary>
    private const double CloseupPitchDegrees = 42;

    /// <summary>
    /// 跟随那一套：画面里至少装下这么宽的一块地（米，见方）。
    /// 这个数是「看得见锁定那个单位 + 它旁边那个 + 指向线」的量级，
    /// 14 米那档两个贴 3 米的单位同框且名牌读得开。
    /// </summary>
    private const double FollowSpanMeters = 14;

    /// <summary>
    /// 特写那一套：画面里至少装下这么宽的一块地（米，见方）。
    /// 5 米那档里脚下色带占画面宽约四成、朝向那 2 米短线与指向线都在画内
    /// （出图实测：2 米那档只剩一块色带，参照物全出了画）。
    /// </summary>
    private const double CloseupSpanMeters = 5;

    /// <summary>
    /// 装下之后留几成空白：<b>0.9 = 让目标那一块占满九成画面</b>。
    /// 取 0.9 而不是 1.0，是因为边缘那一圈网格与标尺要留在画内（读距离靠它们）。
    /// </summary>
    private const double FrameFill = 0.9;

    /// <summary>
    /// 名牌的参考距离（米）：<b>等于全景那一套的距离</b>，所以全景那档名牌字高约 22 像素
    /// （出图实测）。离得越近按比例放大——名牌是读数，字高不跟着距离变才读得开。
    /// </summary>
    private const double PlateReferenceDistanceMeters = 28;

    /// <summary>名牌在参考距离上的字高系数（<c>字的世界高 = 字号 × 这一格</c>）。</summary>
    private const float PlatePixelSize = 0.026f;

    /// <summary>标尺刻度在参考距离上的字高系数（比名牌小一档：它是背景读数，不是主角）。</summary>
    private const float RulerPixelSize = 0.03f;

    /// <summary>
    /// 视场角（度，竖直方向）：<b>从场景里那台相机读</b>，读不到才按 75 走
    /// （场景没给相机时的兜底，也是 Godot 的缺省值）。
    /// 视场角<b>是相机的参数、不由本文件改</b>——场景把它改成别的值时距离推算照实际值重算，
    /// 而且读数行会把它念出来，所以「这个距离是按什么算出来的」在图上就看得见。
    /// </summary>
    private double _fovDegrees = 75;

    /// <summary>画面比例（宽 ÷ 高）：起局时从视口读一次，之后窗体改尺寸也不重读（三套预设同一份）。</summary>
    private float _aspect = 16.0f / 9.0f;

    /// <summary>
    /// 名牌离地多高（米）：<b>0.9 是三套预设都能看见的那个值</b>。
    /// 名牌高过相机时它在画面上会跑到上沿之外——特写那档相机高只有 1.6 米
    /// （2.4 米 × sin(42 度)），出图实测 1.6 米高的名牌整个出了画、画内一块字都没有；
    /// 0.9 米那档落在画面中线上方约半屏高，三套都在画内。
    /// </summary>
    private const double PlateHeightMeters = 0.9;

    /// <summary>名牌逐个错开多少（米）：<b>竖向错位在近俯视那一档几乎看不见</b>（见下面那处注释）。</summary>
    private const double PlateStaggerMeters = 1.0;

    /// <summary>
    /// 名牌错开几层时，一层折多少米 Z（<b>沿 Z 排开，不沿高度</b>）。
    ///
    /// <b>为什么改成沿 Z（ticket 09 出图复核第二次量出来的）</b>：第一版按「离中心远近」分层，
    /// 开阔多点那一份七个目标里有六个落在 3~12 米半径内，于是六块名牌挤在两层里仍互相压叠
    /// （2026-10-01 出图实测：七行字糊成两坨，只认得出颜色）。
    /// 根因是<b>竖向错位在近俯视那一档被 cos(俯角) 压没</b>：全景俯角 78 度，
    /// 沿高度错 1 米在画面上只剩约 6 像素，而字本身约 22 像素高——高度这一维根本不够分。
    /// 沿 Z 排开则不同：俯视投影下 Z 差异<b>不衰减</b>，米数直接换成画面上的上下位移。
    ///
    /// <b>跟随 / 特写那两档也成立</b>：它们俯角 58 / 42 度，Z 差异在画面上同样不减，
    /// 而名牌跟着各自单位走（层号只影响高度，位置仍钉在脚下），所以锁定谁就读谁那一块。
    /// <b>这一格是表现层陈设</b>，一个字都不往世界里写（「表现层只读」），
    /// 名牌高度与层号都不进检查点快照（「试验场随技能层」）。
    /// </summary>
    private const double PlateLayerZStepMeters = 2.4;

    /// <summary>中键单击认人时，屏幕上允许多近算点中（像素，参考距离上的值；近处按比例放宽）。</summary>
    private const float PickRadiusPixels = 48f;

    // === 三套预设的 action 名（「输入走 InputMap」：合成事件进裸键状态表就会让这一格不可实测）===

    public const string ActionViewOverview = "stage_view_overview";
    public const string ActionViewFollow = "stage_view_follow";
    public const string ActionViewCloseup = "stage_view_closeup";

    /// <summary>
    /// <b>起局就在哪一套机位</b>（<c>overview</c> / <c>follow</c> / <c>closeup</c>，缺省全景）。
    /// 场景作者的那一格：把场子摆成跟随或特写再打开就行，不必先进全景再按一下键。
    /// 存在的另一个理由是<b>出图复核要可复现</b>：三套预设各出一张图时，
    /// 靠这一格把起点钉死，比「跑一局再按一下 2/3」少一个手抖的变量。
    /// 它是场景配置而不是动词：不在 <see cref="AbilityVerbs"/> 词表里，不进 Journal（「写路径进 Journal」）。
    /// </summary>
    [Export] public string StartView { get; set; } = "overview";

    // === 时间可控（表现层这一侧只管「本帧该推几步」，那笔账在逻辑壳里）===

    /// <summary>
    /// 倍率：乘在逻辑流速上。1 = 现实时间（1 步 = 0.1 秒，「时间基准」）。转给逻辑壳那一份，
    /// 本文件不自己算「真实秒 × 倍率 × 每秒步数」——那是两处算就会漂的那笔账。
    /// </summary>
    [Export] public double Speed { get; set; } = 1.0;

    /// <summary>暂停：只冻逻辑，画面照跑。转给逻辑壳那一份。</summary>
    [Export] public bool Paused { get; set; }

    // === 读数口 ===

    /// <summary>步进器：<c>Spec</c> 没递时用随场子走的那份示范注册（见 <see cref="CreateSpec"/>）。</summary>
    public SkillStage? Stage { get; private set; }

    /// <summary>
    /// 接新原型时递自己那一份进来（<b>三样：技能表、纯函数、单位属性</b>）。
    /// 为 null 时本场用 <see cref="SkillStageSample"/>——那是「场子自带一个能跑起来的样子」，不是缺省词表。
    /// </summary>
    public SkillStageSpec? Spec { get; set; }

    /// <summary>本局种子。同种子两跑逐字一致（场子「固定」那一条口径）。</summary>
    [Export] public int Seed { get; set; } = 1;

    /// <summary>
    /// 池容量：转给特效层那一份（spec D11）。<b>0 = 从 <c>CueFeedConfig.PendingPerTrack</c> 推导</b>，
    /// 所以缺省（场景不给值）就是推导。
    ///
    /// <para><b>这一格钉在这里的原因</b>：<c>SkillStageVfxCues</c> 与 <c>VfxPlayer</c> 两处
    /// <c>[Export]</c> 都在 <c>BuildVfx</c> / <see cref="SkillStageVfxCues.Wire"/> 里用代码 <c>new</c> 出来，
    /// 场景给它们发不到值（恒为 0，永远走推导）—— 现场调的那一格要有个能设的地方，就在这里。
    /// 本类那几个 <c>[Export]</c>（<see cref="StartView"/> / <see cref="Speed"/> / <see cref="Paused"/> /
    /// <see cref="Seed"/>）也是同一个形状：脚本挂在场景根节点上，值从 <c>.tscn</c> 来。</para>
    ///
    /// <para>调完拧回 0（压测必恢复）。</para>
    /// </summary>
    [Export] public int PoolCapacity { get; set; }

    private TopDownRig? _rig;
    private readonly List<UnitMarker> _markers = new();
    private readonly List<Label3D> _rulerLabels = new();
    private ImmediateMesh? _aimMesh;
    private Label? _eventLabel;
    private StandardMaterial3D? _lineMaterial;

    /// <summary>
    /// 特效消费者：把技能层的新事件演成粒子（住在 <c>Vfx/</c> 那一侧，cue 归特效层那条路）。
    /// <b>没建出来时读数那一格念「vfx 未挂上」</b>，不静默当零粒子——恒为 0 会被读成实测结论。
    /// </summary>
    private SkillStageVfxCues? _vfx;

    /// <summary>调试桥宿主（没配 <c>DEBUG_BRIDGE_PORT</c> / <c>--debug-bridge-port</c> 时内部不监听，零开销）。</summary>
    private DebugBridgeHost? _bridge;

    /// <summary>当前那一套预设。刚起局是 <see cref="StageView.Overview"/>。</summary>
    private StageView _view = StageView.Overview;

    /// <summary>锁定跟随的是场上第几个单位（按注册口那份摆位次序）。-1 = 一个都没锁。</summary>
    private int _lockIndex = -1;

    /// <summary>场地半边长（米）：全景那一套的距离按它推算（ticket 04 留的那笔账）。</summary>
    private double _fieldHalfExtentMeters = 20;

    /// <summary>一个单位在画面上的两样东西：脚下色带 + 名牌。指向线画在所有人共用的一根 <see cref="ImmediateMesh"/> 上。</summary>
    private sealed record UnitMarker(string UnitId, MeshInstance3D Band, Label3D Plate, Color Color);

    public override void _Ready()
    {
        var spec = Spec ?? CreateSpec();
        Stage = CreateStage(spec, Seed);
        Stage.Speed = Speed;
        Stage.Paused = Paused;

        var roster = spec.Roster;
        var extent = (float)roster.GridHalfExtentMeters;
        var cell = (float)roster.GridCellMeters;
        _fieldHalfExtentMeters = roster.GridHalfExtentMeters;

        BuildGrid(extent, cell);
        BuildRuler(extent, roster.RulerLabelEveryMeters);
        BuildAimLine();
        BuildMarkers(roster);
        BuildFacings(roster);
        BuildEventReadout();
        BuildCamera(extent);
        BuildVfx();

        // 锁定先落在名单头一个：跟随 / 特写那两套要一个「锁着谁」才成立，
        // 而「按 2 就跟住第一个」比「先 Tab 一下再按 2」少一个步骤。
        _lockIndex = _markers.Count > 0 ? 0 : -1;
        ApplyViewByName(StartView);
        SetupBridge();
        Refresh();
    }

    /// <summary>
    /// 建特效消费者并挂上逻辑壳。<b>先入树、后接线</b>：离树的那一半一律回
    /// 「没演」（<c>VfxPlayer</c> 里的 <c>IsInsideTree</c> 那一格），所以顺序反了会得到一片安静的画面
    /// 而读数照样念得出来——那种故障在画面上看着像「效果太小」，最难查。
    ///
    /// <para>池容量那一格在这里从 <see cref="PoolCapacity"/> 转下去 —— 那一份是代码里 <c>new</c> 出来的，
    /// 场景发不到值，所以场景侧能设的那一格只在本类这一个地方。</para>
    /// </summary>
    private void BuildVfx()
    {
        if (Stage is null)
        {
            return;
        }

        var cues = new SkillStageVfxCues { Name = "VfxCues", PoolCapacity = PoolCapacity };
        AddChild(cues);
        cues.Wire(Stage);
        _vfx = cues;
    }

    /// <summary>
    /// 没递注册口时用的那一份：<b>示范注册壳</b>（<see cref="SkillStageSample"/>），一份能跑起来的样子。
    /// 场子自带它是为了「开箱能跑 + 出图能看」，不是为了给词表兜缺省——
    /// 词表五列仍然全在注册口那边必填，场子一个缺省都不给。
    /// 走的是「多单位同框」那一份（六个人），因为「点谁跟谁」与「全景看群体」这两格
    /// 要有五六个人同框才验得出来（注册壳的摆位是它的场地条件，场子一个字不改）。
    /// </summary>
    protected virtual SkillStageSpec CreateSpec() => SkillStageSample.SpecMultiUnit();

    /// <summary>
    /// 起哪一个逻辑壳（ticket 09 补 ticket 07 那笔债时开的派生点）。
    /// <b>为什么要有这一格</b>：场景壳要能被派生场子复用（开阔多点那份，ticket 07 只有逻辑壳没有场景壳），
    /// 而各场子的差别有一半在逻辑壳的类上——<see cref="SkillStageOpenField"/> 比本基类多
    /// 「群体名单」与「群体施加」两发，出图要能注入群体施加，起的就必须是对的那一个。
    /// <b>不开放这一格的话，派生场子只能重画一遍整个场景壳</b>（几百行陈设 + 三套机位），
    /// 抄一遍就多一份会漂的实现——与 spec「不走子类那条」禁的是同一件事，只是这里抄的是表现层。
    /// <b>本文件一个具体的场子都不认</b>：默认起 <see cref="SkillStage"/>，派生场子自己换。
    /// </summary>
    protected virtual SkillStage CreateStage(SkillStageSpec spec, int seed) => new SkillStage(spec, seed);

    /// <summary>
    /// 本帧推进：把真实秒与倍率交给逻辑壳那一份（<see cref="SkillStage.AdvanceRealSeconds"/>），
    /// 攒步那一格也在那边——表现层只问「推了几步」来决定要不要重画（「时钟解耦」）。
    /// </summary>
    public override void _Process(double delta)
    {
        if (Stage is null)
        {
            return;
        }

        Stage.Speed = Speed;
        Stage.Paused = Paused;
        if (Stage.AdvanceRealSeconds(delta) > 0)
        {
            Refresh();
        }

        // 特效消费者每帧走一次它的三拍（翻账 → 取走并演 → 推倍率）。
        // 不挂在上面那个 if 里：三拍与「本帧推了几步」无关（推 0 步的那几帧也要推倍率，
        // 也要把上一帧排出来的那几条演掉），挂在里面就等于倍率只在推进的那些帧上更新。
        _vfx?.Mirror((float)delta, Speed);

        // 切预设走 action（「输入走 InputMap」）：与「本帧推了几步」无关，所以不挂在那个 if 里。
        ReadViewInput();
    }

    /// <summary>把逻辑那份真值重画一遍。只读，不写。</summary>
    public void Refresh()
    {
        if (Stage is null)
        {
            return;
        }

        var health = Stage.Config.HealthAttribute;
        var maxHealth = Spec?.Roster.MaxHealthAttribute ?? health;

        // 名牌与标尺刻度的字高都按机位距离缩放：三套预设从 28.3 米到 2.4 米，
        // 不缩放的话特写那一档的名牌会宽到出画（出图实测：2.4 米那档字高约 210 像素、
        // 横向出画），全景那档反而只有 22 像素；标尺刻度反过来，贴近时糊住半个画面。
        // 缩放之后三套都是同一档字号 —— 读数不跟着距离变才读得开。
        var plateScale = (float)(Math.Max(_rig?.Distance ?? PlateReferenceDistanceMeters, 0.5)
                               / PlateReferenceDistanceMeters);

        foreach (var ruler in _rulerLabels)
        {
            ruler.PixelSize = RulerPixelSize * plateScale;
        }

        foreach (var marker in _markers)
        {
            var set = Stage.Find(marker.UnitId);
            if (set is null)
            {
                continue;
            }

            var current = set.Current.TryGetValue(health, out var hp) ? hp : 0;
            var max = set.Current.TryGetValue(maxHealth, out var ceiling) ? ceiling : current;
            marker.Plate.Text = $"{marker.UnitId}  {AbilityNumbers.Format(current)}" +
                                (max > 0 ? $"/{AbilityNumbers.Format(max)}" : string.Empty);
            marker.Plate.PixelSize = PlatePixelSize * plateScale;
            marker.Band.Position = Ground(Stage.PointOf(marker.UnitId), 0.02f);
        }

        RedrawAimLine();
        if (_eventLabel is not null)
        {
            _eventLabel.Text = ReadoutText();
        }
    }

    /// <summary>右上角那块读数：机位读数 + 时间控制 + 尾窗逐行 + 观测窗口读数（带 <c>dropped</c>）。</summary>
    public string ReadoutText()
    {
        if (Stage is null)
        {
            return string.Empty;
        }

        // 上屏的字一律 ASCII：Godot 缺省字体没有汉字字形，中文那行出图实测是<b>整行空白</b>
        // （2026-10-01）。口径照本仓既有上屏文字（`step N | ...` 那套），步仍是唯一那把尺。
        // 第二行念时间控制（暂停 / 倍率）、第三行念机位：这两样都<b>不进快照</b>
        // （停一下、挪一下镜头都不该让逐字比对报分叉），所以想看它们只能看这几行。
        var lines = new List<string>
        {
            $"step {Stage.Tick} | {AbilityNumbers.Format(Stage.Tick / 10.0)} s",
            $"speed {AbilityNumbers.Format(Stage.Speed)} | {(Stage.Paused ? "paused" : "running")}",
            ViewReadout(),
            Stage.Readout(),
            _vfx?.AsciiReadout() ?? "vfx not wired",
            string.Empty,
        };
        lines.AddRange(Stage.EventTailLines(14).Reverse());
        return string.Join("\n", lines);
    }

    // === eval 直达（游戏侧读状态 / 注入的入口；逻辑仍在逻辑壳那一份）===

    /// <summary>
    /// 一发读出全量真值——游戏侧的读状态入口，值就是 <see cref="SkillStage.DebugSnapshot"/> 那一份，
    /// 这里不做任何加工。存在的理由是 GDScript 够不着 <see cref="Stage"/> 那个可空 C# 类型
    /// （实测报 <c>Invalid access</c>），所以在 C# 侧开一个返回 <c>string</c> 的口子。
    /// </summary>
    public string Snapshot() => Stage?.DebugSnapshot() ?? "stage=none";

    /// <summary>观测窗口读数（含 <c>dropped</c>）。</summary>
    public string Readout() => Stage?.Readout() ?? "stage=none";

    /// <summary>
    /// 事件尾窗拼成一段字（最新那条在最后一行）。返回 <c>string</c> 而不是列表：
    /// GDScript 够不着 <c>IReadOnlyList&lt;string&gt;</c>（实测报 <c>Nonexistent function</c>），
    /// 所以在 C# 侧先拼好。给谁读：右上角那块画的就是它，agent 出图后读的是同一份。
    /// </summary>
    public string EventTailText(int count)
        => Stage is null ? "stage=none" : string.Join("\n", Stage.EventTailLines(count));

    /// <summary>
    /// 推进 <paramref name="steps"/> 步并重画。注入与出图时把逻辑推到某一步靠它，
    /// 不用等现实时间——「前摇落在第几步」这类问题要能一步一跳地问。
    /// 走单步那一发（<see cref="SkillStage.AdvanceDebug"/>），所以<b>暂停中这一步也照走</b>：
    /// 「停住看第 k 步」是这套调试口最常见的用法。
    /// 画完顺手 <see cref="Refresh"/>，所以调完这一发画面就是那一步的样子。
    /// </summary>
    public void AdvanceSteps(long steps)
    {
        Stage?.AdvanceDebug(steps);
        Refresh();
    }

    /// <summary>
    /// 走写路径唯一那条口注入一发（「桥只转发」）。<b>词表由调用方给</b>——
    /// 本方法不认识任何一条具体技能，所以原型与 agent 都能用它而不必改场子。
    /// 画完顺手 <see cref="Refresh"/>：位置被改过就要立刻画到脚下色带上，
    /// 否则锁着那个单位时相机会跟着一块没动的色带走（表现层只读，这一发仍走写路径）。
    /// </summary>
    public bool InjectVerb(string cmd, string args)
    {
        var ok = Stage is not null && Stage.Inject(cmd, args);
        Refresh();
        return ok;
    }

    /// <summary>
    /// <b>特效直达</b>：立刻演一次指定效果，不经逻辑触发。
    /// 粒子演出窗口极短（那份资源 0.3~0.6 秒），等逻辑层真走到那一步再截图必然错过，
    /// 而「这一下有没有演」正是这一格要答的（先例：某一版宿主里的特效直达口，它当初就是为这件事写的）。
    ///
    /// <para><b>一个真值都不写</b>：走的是 <c>VfxPlayer</c> 那几发演出口，不经
    /// <see cref="SkillStage.Inject"/>，所以它不进录制（「表现层只读」）。
    /// 场子里没有那个单位、或没有那条轨，当场抛并指名——不静默摆在场地正中、不兜一个缺省档。</para>
    /// </summary>
    /// <param name="unitId">演在谁身上。</param>
    /// <param name="track">轨名（本场那张薄表里的名字）。</param>
    /// <param name="value">各档里它不是同一个意思：一次性 = 大小倍数，持续 = 在身(1)/撤下(0)，计数与带参数 = 那个数。</param>
    public string PlayFxDebug(string unitId, string track, double value)
    {
        var detail = _vfx?.PlayDirect(unitId, track, value)
                     ?? throw new InvalidOperationException("特效消费者还没建出来（起局时 Stage 是空的？）");
        Refresh();
        return detail;
    }

    /// <summary>
    /// 特效与派发台的完整读数（含汉字那几段，含池那一行与「丢了哪条」那段有界尾巴）。
    /// 调试桥的 <c>vfx_read</c> 读它；<b>它不上屏</b>（上屏那一格是 ASCII 的 <see cref="ReadoutText"/>）。
    /// </summary>
    public string VfxReadoutText() => _vfx?.ReadoutFull() ?? "vfx=未挂上";

    /// <summary>
    /// 接调试桥。接法照公共层那个 UI 试验场（<c>FrontEndStageScene</c>）逐字同形：
    /// <see cref="DebugConsole.Attach"/> 建宿主、暴露 <c>main</c>、递一份强类型 globals，
    /// 然后只注册<b>本场自己那份动词表</b>与<b>共享词表</b>（<c>screenshot</c>）。
    /// <b>标准七动词不在这一层重写</b>（契约「标准七动词」：那七个只由 <c>DebugConsole</c> 注册）。
    ///
    /// <para><b>词表为什么只有这十发</b>：动词只转发<b>既有</b>的 eval 直达方法（契约「桥只转发」），
    /// 桥那一层另写一套实现的后果是「注入的路径」与「玩家走的路径」漂成两回事。
    /// 场子预留的那二十个 public 方法大半也走 <c>call</c> / <c>eval</c> 够得着
    /// （<c>main</c> 就是本节点），所以只把<b>出图复核要反复打的那几条</b>提成动词：
    /// 推进定点（粒子窗口短，必须能推到落地那一步再截图）、注入、出图、三套机位、换锁、
    /// 特效直达与两发读数。
    /// 数字参数一律 <c>required</c>（契约「桥只转发」：兜 0 就是静默摆到第 0 排）。</para>
    /// </summary>
    private void SetupBridge()
    {
        _bridge = DebugConsole.Attach(this, new Globals { Stage = this, Vfx = _vfx },
            // 出图复核那一格：粒子演没演、三套机位下看不看得清，只有出图看得见（共享词表）。
            ("screenshot", a => ViewportCapture.SavePng(this, a.Str("path") ?? "user://skill-stage.png")),
            ("stage_read", _ => ReadoutText()),
            ("stage_snapshot", _ => Snapshot()),
            ("stage_inject", a => InjectVerb(a.Str("cmd", required: true), a.Str("args", required: true))),
            ("stage_advance", a => { AdvanceSteps((long)a.Num("steps", required: true)); return $"tick={Stage?.Tick}"; }
        ),
            ("stage_view", a => { ApplyViewByName(a.Str("name", required: true)); return CurrentViewName(); }
        ),
            ("stage_lock", a => { FollowUnit(a.Str("unit", required: true)); return LockedUnitId() ?? "none"; }
        ),
            ("stage_lock_next", _ => { CycleLockUnit(); return LockedUnitId() ?? "none"; }
        ),
            ("vfx_play", a => PlayFxDebug(a.Str("unit", required: true), a.Str("track", required: true),
                a.Num("value", required: true))),
            ("vfx_read", _ => VfxReadoutText()));

        GD.Print("[SkillStage] 技能试验场就绪：1/2/3 切机位、Tab 换锁、中键点人；"
                 + "调试桥 ping / verbs / stage_read / stage_advance / stage_inject / stage_view / "
                 + "stage_lock / stage_lock_next / vfx_play / vfx_read / screenshot");
    }

    /// <summary>
    /// eval 脚本的强类型 globals：<c>Stage.ReadoutText()</c> / <c>Vfx.PlayDirect(…)</c>
    /// 走全量 C#，不用 dynamic。
    /// </summary>
    public sealed class Globals
    {
        /// <summary>场子那一个（<c>main</c> 暴露的也是它）。</summary>
        public SkillStageScene Stage { get; init; } = null!;

        /// <summary>特效消费者那一侧（起局时建出来才有值，所以是可空的）。</summary>
        public SkillStageVfxCues? Vfx { get; init; }
    }

    // === 机位：三套预设 + 点谁跟谁 + 手动微调（表现层动词，不进 Journal，「写路径进 Journal」）===

    /// <summary>
    /// 机位读数（截图只能证明「画面变了」，读数才能证明「变到了该去的地方」）：
    /// 现在是哪一套、锁着谁、相机自己那几个数。
    /// 右上角那行画的就是它，agent 出图后读的是同一份。
    /// </summary>
    public string ViewReadout()
    {
        var rig = _rig?.DebugReadout() ?? "rig=none";
        return $"view {ViewName(_view)} lock={LockedUnitId() ?? "none"} {rig} {FramingReadout()}";
    }

    /// <summary>机位读数的旧名（只读同一份，不另算）。</summary>
    public string CameraReadout() => ViewReadout();

    /// <summary>当前那一套预设。给 eval 与读数用；切换走 <see cref="ApplyViewByName"/>。</summary>
    public string CurrentViewName() => ViewName(_view);

    /// <summary>锁着谁（没人锁时是 null）。</summary>
    public string? LockedUnitId()
        => _lockIndex >= 0 && _lockIndex < _markers.Count ? _markers[_lockIndex].UnitId : null;

    /// <summary>
    /// 切到某一套预设（<b>立刻生效，不过阻尼</b>——预设是给定点出图用的，缓动会让「第 k 步长什么样」不可复现）。
    /// 三套各自给的是：焦点（场地中心 / 锁着的那个单位）、距离、俯角、跟不跟随。
    /// <b>全程只挪相机</b>：一个字都不往世界里写（「表现层只读」），也永远不走
    /// <see cref="SkillStage.Inject"/>（「写路径进 Journal」：表现层动词不进录制）。
    /// 切完把这一套记成归位点，于是相机的 Home 一键回到当前这套（手动微调完的兜场路径）。
    /// </summary>
    public void ApplyView(StageView view)
    {
        if (_rig is null)
        {
            return;
        }

        var locked = _lockIndex >= 0 && _lockIndex < _markers.Count ? _markers[_lockIndex] : null;
        var follow = view != StageView.Overview && locked is not null;

        var distance = view switch
        {
            StageView.Overview => FitDistance(_fieldHalfExtentMeters * 2.0, OverviewPitchDegrees),
            StageView.Follow => FitDistance(FollowSpanMeters, FollowPitchDegrees),
            StageView.Closeup => FitDistance(CloseupSpanMeters, CloseupPitchDegrees),
            _ => throw new ArgumentOutOfRangeException(nameof(view), view, "只有三套预设"),
        };

        var pitch = view switch
        {
            StageView.Overview => OverviewPitchDegrees,
            StageView.Follow => FollowPitchDegrees,
            StageView.Closeup => CloseupPitchDegrees,
            _ => throw new ArgumentOutOfRangeException(nameof(view), view, "只有三套预设"),
        };

        // 跟随时焦点钉在锁定那个单位的脚下（场上没有锁着谁就退回全景那一套：无处可跟）。
        var focus = follow
            ? Ground(Stage?.PointOf(locked!.UnitId) ?? (0, 0), 0f)
            : new Vector3(0f, 0f, 0f);

        _view = view;
        _rig.FollowTarget(follow ? locked!.Band : null);
        _rig.SetPose(focus, (float)distance, CameraYawDegrees, (float)pitch);
        _rig.SaveHome();
        Refresh();
    }

    /// <summary>
    /// 按名字切预设（给 eval / agent 那一侧的口）：<c>overview</c> / <c>follow</c> / <c>closeup</c>。
    /// 名字不认识当场抛，不静默当全景（「桥只转发」 同一句理由：兜一个缺省值 =
    /// 「看起来成功了」，最难查）。上屏与读数用的小写名也从这一份出。
    /// </summary>
    public void ApplyViewByName(string name)
    {
        var view = name?.Trim().ToLowerInvariant() switch
        {
            "overview" => StageView.Overview,
            "follow" => StageView.Follow,
            "closeup" => StageView.Closeup,
            _ => throw new ArgumentException(
                $"没有叫 {name} 的机位预设，只有 overview / follow / closeup", nameof(name)),
        };

        ApplyView(view);
    }

    /// <summary>
    /// 锁定跟随某个单位：换锁 + 立刻把当前那套重摆到它身上。
    /// 名字不认识当场抛（不知道场上有没有这个单位，就不许静默锁一个错的）。
    /// </summary>
    public void FollowUnit(string unitId)
    {
        var index = _markers.FindIndex(marker => string.Equals(marker.UnitId, unitId, StringComparison.Ordinal));
        if (index < 0)
        {
            throw new ArgumentException($"场上没有叫 {unitId} 的单位", nameof(unitId));
        }

        _lockIndex = index;
        ApplyView(_view);
    }

    /// <summary>
    /// 换锁到下一个单位（按注册口那份摆位次序绕一圈）——「五六个单位同框时能切」的入口。
    /// 切完若当前是跟随 / 特写，相机立刻跟过去；若当前是全景，只换锁不挪镜头
    /// （全景那一档的作用就是看整场，不该因为按了一下 Tab 就跳走）。
    /// </summary>
    public void CycleLockUnit()
    {
        if (_markers.Count == 0)
        {
            return;
        }

        _lockIndex = _lockIndex < 0 ? 0 : (_lockIndex + 1) % _markers.Count;
        if (_view != StageView.Overview)
        {
            ApplyView(_view);
        }
        else
        {
            Refresh();
        }
    }

    /// <summary>
    /// 中键单击认人：<paramref name="screenPos"/> 是屏幕坐标（像素，左上原点），
    /// 找离它最近的那块脚下色带，容差按相机距离放宽（<see cref="TopDownRig.PickToleranceScale"/>
    /// 就是为这件事留的那一格）。认到就换锁，没认到就<b>不动</b>（空地点击不改变镜头）。
    /// 返回认到的单位名，没认到返回空串。
    /// </summary>
    public string PickUnitAt(double screenX, double screenY)
    {
        var camera = _rig?.Camera;
        if (camera is null || Stage is null || _markers.Count == 0)
        {
            return string.Empty;
        }

        var click = new Vector2((float)screenX, (float)screenY);
        var tolerance = PickRadiusPixels * _rig!.PickToleranceScale;
        var bestIndex = -1;
        var bestDistance = float.MaxValue;

        for (var i = 0; i < _markers.Count; i++)
        {
            var marker = _markers[i];
            var screen = camera.UnprojectPosition(Ground(Stage.PointOf(marker.UnitId), 0.02f));
            var distance = screen.DistanceTo(click);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        if (bestIndex < 0 || bestDistance > tolerance)
        {
            return string.Empty;
        }

        _lockIndex = bestIndex;
        if (_view != StageView.Overview)
        {
            ApplyView(_view);
        }
        else
        {
            Refresh();
        }

        return _markers[bestIndex].UnitId;
    }

    /// <summary>
    /// 手动微调（兜场用：预设对不上场合时改几个数，不必重起一局）。
    /// 五个增量：距离（米，正数拉远）、偏航（度）、俯仰（度）、平移（世界米 X / Z）。
    /// <b>跟随时给平移 = 不跟了</b>：跟随的焦点每帧被目标改写，平移会被无声吃掉，
    /// 那就明确地解除锁定再挪（「桥只转发」 同一句理由：不许静默无效）。
    /// 其余三格相机自己钳在它的范围内。全程只挪相机，不碰世界。
    /// </summary>
    public void NudgePose(double distanceMeters, double yawDegrees, double pitchDegrees, double panX, double panZ)
    {
        if (_rig is null)
        {
            return;
        }

        if (Math.Abs(panX) > 0.0 || Math.Abs(panZ) > 0.0)
        {
            _rig.FollowTarget(null);
            _view = StageView.Overview;
        }

        if (Math.Abs(distanceMeters) > 0.0)
        {
            _rig.ZoomBy((float)(distanceMeters / Math.Max(_rig.Distance, 0.01)));
        }

        if (Math.Abs(yawDegrees) > 0.0)
        {
            _rig.RotateBy((float)yawDegrees);
        }

        if (Math.Abs(pitchDegrees) > 0.0)
        {
            _rig.TiltBy((float)pitchDegrees);
        }

        if (Math.Abs(panX) > 0.0 || Math.Abs(panZ) > 0.0)
        {
            _rig.PanBy((float)panX, (float)panZ);
        }

        Refresh();
    }

    /// <summary>
    /// 「装下 <paramref name="spanMeters"/> 见方的一块地」需要的那个距离（米）。
    /// 三条约束取最远的那一条：横向要装下 <c>span</c>，纵深要装下 <c>span</c>、
    /// 而地面纵深在画面上被 <c>sin(俯角)</c> 压着（俯角越接近正俯视压得越少）。
    /// 两条都再除以 <see cref="FrameFill"/> 留那一圈空白。
    /// 用的是本场那台相机的视场角与本场的画面比例（读数行把这两个数念出来）。
    ///
    /// <b>为什么要有这一发</b>：ticket 04 留下的缺省距离是照 40 米见方的场地<b>量</b>出来的，
    /// 换一块场地就错。现在缺省值是<b>按场地尺寸算</b>的，换多大都对。
    /// 算术一律 double（「double 与不变文化」）。
    /// </summary>
    public double FitDistance(double spanMeters, double pitchDegrees)
        => FitDistanceFor(spanMeters, pitchDegrees, _fovDegrees, _aspect);

    /// <summary>
    /// 上面那一发的纯算术部分（视场角与画面比例由调用方给，所以它自己什么都不读）。
    /// 单独拆出来是为了「这个距离是按什么算出来的」在注释里能算给人看：
    /// 40 米见方 / 俯角 78 度 / 视场角 75 度 / 16:9 ⇒ 横向 16.3 米、纵深 28.3 米，取 28.3。
    /// </summary>
    public static double FitDistanceFor(double spanMeters, double pitchDegrees, double fovDegrees, double aspect)
    {
        var halfViewHeight = Math.Tan(fovDegrees * Math.PI / 360.0);
        var across = spanMeters / (2.0 * halfViewHeight * aspect * FrameFill);
        var along = (spanMeters * Math.Sin(pitchDegrees * Math.PI / 180.0)) / (2.0 * halfViewHeight * FrameFill);
        return Math.Max(across, along);
    }

    /// <summary>视场角与画面比例（读数行念出来：距离是按这两个数算的）。</summary>
    public string FramingReadout() => $"fov={_fovDegrees:0.#} aspect={_aspect:0.###} fill={FrameFill:0.##}";

    /// <summary>画面比例（宽 ÷ 高）：读视口。视口尺寸读不出来时按 16:9 走——
    /// 那是本仓窗体的缺省形状，差一档也只是距离差一点，不会错到看不清。
    /// </summary>
    private float ReadAspect()
    {
        var size = GetViewport()?.GetVisibleRect().Size ?? Vector2.Zero;
        return size.Y > 1f && size.X > 1f ? size.X / size.Y : 16.0f / 9.0f;
    }

    /// <summary>三套预设的小写名（读数与 eval 用的那一份，ASCII：Godot 缺省字体没有汉字字形）。</summary>
    private static string ViewName(StageView view) => view switch
    {
        StageView.Overview => "overview",
        StageView.Follow => "follow",
        StageView.Closeup => "closeup",
        _ => "none",
    };

    // === 场地陈设 ===

    private void BuildGrid(float extent, float cell)
    {
        if (cell <= 0f)
        {
            cell = 1f;
        }

        var mesh = new ImmediateMesh();
        var material = BuildLineMaterial();
        _lineMaterial = material;
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, material);

        // 每格一根线，落在地面上抬高一点点，免得与地面 z-fighting。
        for (var x = -extent; x <= extent + 0.001f; x += cell)
        {
            mesh.SurfaceSetColor(new Color(0.36f, 0.40f, 0.46f));
            mesh.SurfaceAddVertex(new Vector3(x, 0.01f, -extent));
            mesh.SurfaceAddVertex(new Vector3(x, 0.01f, extent));
        }

        for (var z = -extent; z <= extent + 0.001f; z += cell)
        {
            mesh.SurfaceSetColor(new Color(0.36f, 0.40f, 0.46f));
            mesh.SurfaceAddVertex(new Vector3(-extent, 0.01f, z));
            mesh.SurfaceAddVertex(new Vector3(extent, 0.01f, z));
        }

        // 中轴两根粗一档的：X = 0 与 Z = 0 两根，给「零点在哪儿」一个可读的参照
        // （二维摆位那档里 X 与 Z 都读得着，一维那档退化成一根）。
        mesh.SurfaceSetColor(new Color(0.62f, 0.68f, 0.76f));
        mesh.SurfaceAddVertex(new Vector3(0f, 0.02f, -extent));
        mesh.SurfaceAddVertex(new Vector3(0f, 0.02f, extent));
        mesh.SurfaceSetColor(new Color(0.55f, 0.62f, 0.72f));
        mesh.SurfaceAddVertex(new Vector3(-extent, 0.02f, 0f));
        mesh.SurfaceAddVertex(new Vector3(extent, 0.02f, 0f));
        mesh.SurfaceEnd();

        var node = new MeshInstance3D { Name = "GroundGrid", Mesh = mesh };
        AddChild(node);
    }

    /// <summary>
    /// 标尺：每隔几米一根带数字的刻度，摆在<b>远端那条边</b>上。
    /// 摆远端不摆近端：机位在近端那一侧，摆近端的话刻度会落到画面下沿之外（出图实测过，看不见）。
    /// </summary>
    private void BuildRuler(float extent, double labelEvery)
    {
        var holder = new Node3D { Name = "Ruler" };
        AddChild(holder);

        // 算术一律 double（「double 与不变文化」）：整段用 double 走，落进 Vector3 时再收窄。
        var step = labelEvery > 0 ? labelEvery : 5.0;
        for (double x = -extent; x <= extent + 0.001; x += step)
        {
            var label = new Label3D
            {
                Name = $"Tick{x:0}",
                Text = $"{Math.Round(x).ToString(CultureInfo.InvariantCulture)} m",
                Position = new Vector3((float)x, 1.6f, -extent - 2f),
                PixelSize = RulerPixelSize,
                FontSize = 40,
                Modulate = new Color(0.78f, 0.82f, 0.88f),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = true,
                RenderPriority = 4,
            };
            holder.AddChild(label);
            _rulerLabels.Add(label);
        }
    }

    private void BuildMarkers(StageRoster roster)
    {
        var holder = new Node3D { Name = "Units" };
        AddChild(holder);

        for (var i = 0; i < roster.Units.Count; i++)
        {
            var unit = roster.Units[i];
            var color = MarkerColor(i);
            var spot = (unit.X, unit.Z);

            // 脚下色带：一块贴地的圆盘，给一个可辨的落点（场地陈设，不是造型）。
            var band = new MeshInstance3D
            {
                Name = $"Band_{unit.UnitId}",
                Mesh = new CylinderMesh { TopRadius = 0.9f, BottomRadius = 0.9f, Height = 0.04f, RadialSegments = 24 },
                Position = Ground(spot, 0.02f),
                MaterialOverride = Unshaded(color),
            };

            // 名牌错开高度：两个单位贴脸摆时同高的名牌会叠成一团读不出来
            // （2026-10-01 出图实测：两行字糊成一坨）。
            //
            // 错开量从 ticket 02 的「5 米一档」收到「1 米一档」，是出图复核量出来的：
            // 竖向错位在画面上被 cos(俯角) 压着——全景那档俯角 78 度，1 米只剩约 6 像素
            // （5 米那档约 30 像素，而字本身就有约 22 像素高，本来也只是勉强错开）；
            // 反过来 5 米一档在六个人同框时最高那块名牌会飞到 27 米高、离相机只剩 7 米，
            // 字被放大 4 倍还出了画。1 米一档下六块名牌最高 6.6 米、三套预设都在画内。
            // 贴脸那一档（俯角 42 度、距离 2.4 米）1 米 ≈ 130 像素，够错开一行。
            //
            // **层号从「序号」改成「沿 Z 错开」**（ticket 09 出图复核量出来的，见 PlateLayerZStepMeters）：
            // 开阔多点那一份七个目标散在 6~12 米，按序号或按远近分层都会让六块名牌挤在两层里压叠，
            // 而全景那一档恰恰是「看六个目标的角度分布」唯一说得清的一档。
            // 沿 Z 错开时名牌仍钉在各自单位脚下那一列（Z 只做小幅偏移，不脱离自己的落点）。
            // 层号是表现层陈设，读数不依赖它（「表现层只读」 表现层只读：它一个字都不往世界里写）。
            var layer = (int)Math.Round(unit.Z / PlateLayerZStepMeters);
            var plate = new Label3D
            {
                Name = $"Plate_{unit.UnitId}",
                Position = new Vector3((float)unit.X, (float)(PlateHeightMeters + (layer * PlateStaggerMeters)),
                    (float)(unit.Z + (layer * PlateLayerZStepMeters * 0.35))),
                PixelSize = PlatePixelSize,
                FontSize = 56,
                Modulate = color,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = true,
                RenderPriority = 6,
            };

            holder.AddChild(band);
            holder.AddChild(plate);
            _markers.Add(new UnitMarker(unit.UnitId, band, plate, color));
        }
    }

    /// <summary>
    /// 朝向那一小段：每格从脚下朝自己朝向画 2 米。<b>只看画面分不出「正前 30°」与「正前 80°」</b>
    /// ——扇形界内界外那几格要肉眼能复核，就得有这一段（场地陈设，不是造型）。
    /// 方向由 <see cref="StageUnit.FacingDegrees"/> 换算（度 → 单位向量，「double 与不变文化」）。
    /// </summary>
    private void BuildFacings(StageRoster roster)
    {
        var mesh = new ImmediateMesh();
        var material = BuildLineMaterial();
        _lineMaterial = material;
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, material);

        for (var i = 0; i < roster.Units.Count; i++)
        {
            var unit = roster.Units[i];
            var radians = unit.FacingDegrees * Math.PI / 180.0;
            var (facingX, facingZ) = (Math.Cos(radians), Math.Sin(radians));
            mesh.SurfaceSetColor(MarkerColor(i));
            mesh.SurfaceAddVertex(Ground((unit.X, unit.Z), 0.03f));
            mesh.SurfaceAddVertex(Ground((unit.X + (facingX * FacingStubMeters), unit.Z + (facingZ * FacingStubMeters)), 0.03f));
        }

        mesh.SurfaceEnd();
        AddChild(new MeshInstance3D { Name = "Facings", Mesh = mesh });
    }

    /// <summary>朝向那一小段画多长（米）。够看出方向，又不至于压住脚下色带。</summary>
    private const double FacingStubMeters = 2.0;

    private void BuildAimLine()
    {
        _aimMesh = new ImmediateMesh();
        var node = new MeshInstance3D { Name = "AimLines", Mesh = _aimMesh };
        AddChild(node);
    }

    /// <summary>指向线：每个「正在打谁」的单位画一根，从它脚下连到目标脚下。</summary>
    private void RedrawAimLine()
    {
        var stage = Stage;
        if (_aimMesh is null || stage is null)
        {
            return;
        }

        // 先把要画的线收齐再开面：ImmediateMesh 一个顶点都不给就 SurfaceEnd 会逐帧报错
        // （实测一次空面刷出 196 条 error）。此刻没人放技能 ⇒ 场面上就该一根线都没有。
        var segments = _markers
            .Select(marker => (Marker: marker, Target: AimTarget(stage, marker)))
            .Where(pair => pair.Target is not null)
            .ToList();
        if (segments.Count == 0)
        {
            return;
        }

        _aimMesh.ClearSurfaces();
        _aimMesh.SurfaceBegin(Mesh.PrimitiveType.Lines, _lineMaterial ?? BuildLineMaterial());
        foreach (var (marker, targetId) in segments)
        {
            if (targetId is null || stage.Find(marker.UnitId) is null || stage.Find(targetId) is null)
            {
                continue;
            }

            _aimMesh.SurfaceSetColor(marker.Color);
            _aimMesh.SurfaceAddVertex(Ground(stage.PointOf(marker.UnitId), 0.6f));
            _aimMesh.SurfaceAddVertex(Ground(stage.PointOf(targetId), 0.6f));
        }

        _aimMesh.SurfaceEnd();
    }

    /// <summary>场子那份摆位（米）落成引擎坐标：X → x、Z → z（算术一律 double，「double 与不变文化」）。</summary>
    private static Vector3 Ground((double X, double Z) point, double height)
        => new((float)point.X, (float)height, (float)point.Z);

    /// <summary>这一格在打谁：技能栏里相位在前摇/出手/后摇、且记着目标的那一条；没在打就 null。</summary>
    private static string? AimTarget(SkillStage stage, UnitMarker marker)
    {
        var set = stage.Find(marker.UnitId);
        return set is null ? null : CastTarget(set);
    }

    /// <summary>这一格在打谁：技能栏里相位在前摇/出手/后摇、且记着目标的那一条。</summary>
    private static string? CastTarget(AbilitySet set)
        => set.Abilities.Values
            .Where(a => a.TargetId is not null
                && a.Phase is AbilityPhase.Pre or AbilityPhase.Execute or AbilityPhase.Post)
            .OrderBy(a => a.Id, StringComparer.Ordinal)
            .Select(a => a.TargetId)
            .FirstOrDefault();

    private void BuildEventReadout()
    {
        var layer = new CanvasLayer { Name = "Readout" };
        var label = new Label
        {
            Name = "EventTail",
            VerticalAlignment = VerticalAlignment.Top,
            Modulate = new Color(0.88f, 0.91f, 0.95f),
        };
        layer.AddChild(label);
        AddChild(layer);

        // 铺满视口再收边，不直接摆 Position / Size。Label 是 Control：入树前给 Position 没有父矩形
        // 可依，出图实测首行被推到画面外（2026-10-01，只剩第二行 `events ...` 在画内）。
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        label.OffsetLeft = 16;
        label.OffsetTop = 12;
        label.OffsetRight = -16;
        label.OffsetBottom = -12;
        _eventLabel = label;
    }

    private void BuildCamera(float extent)
    {
        var camera = FindChildCamera();

        // 距离推算要用的两个数从相机与视口当场读，读数行会把它们念出来。
        _fovDegrees = camera?.Fov ?? 75;
        _aspect = ReadAspect();

        _rig = new TopDownRig
        {
            Name = "TopDownRig",
            BoundsMinXZ = new Vector2(-extent, -extent),
            BoundsMaxXZ = new Vector2(extent, extent),
            // 距离范围也按场地尺寸给：近端要够特写那一套（约 2.4 米），
            MinDistance = 1.5f,
        };
        AddChild(_rig);
        if (camera is not null)
        {
            _rig.AttachCamera(camera);
        }

        // 远端留三倍全景距离（退到整块场地还占画面九成高度的三倍远）。
        _rig.MaxDistance = (float)Math.Max(140.0, FitDistance(extent * 2.0, OverviewPitchDegrees) * 3.0);

        // 「点中了谁」「换锁」两件事的判定在场子侧（「相机单源」：相机只管被看的那台机器）。
        _rig.ViewClicked += OnViewClicked;
        _rig.FollowToggleRequested += CycleLockUnit;

        EnsureViewInputMap();
        ReadViewInput();
    }

    /// <summary>
    /// 中键单击（相机抛的屏幕坐标）→ 认人换锁。相机自己不管「点中了谁」。
    /// </summary>
    private void OnViewClicked(Vector2 screenPos) => PickUnitAt(screenPos.X, screenPos.Y);

    /// <summary>
    /// 切预设的三个 action（「输入走 InputMap」：一律走 InputMap action，合成事件才注得进来——
    /// 裸键轮询那一路 MCP 进不去，等于这一格不可实测）。
    /// 键位分工：1 全景 / 2 跟随 / 3 特写；换锁用相机的 Tab（<c>camera_follow_toggle</c>），
    /// 手动微调用相机自己那套（滚轮 / WASD / Q E / ↑↓ / 中键拖拽）。
    /// </summary>
    private static void EnsureViewInputMap()
    {
        AddKeyAction(ActionViewOverview, Key.Key1, Key.Kp1);
        AddKeyAction(ActionViewFollow, Key.Key2, Key.Kp2);
        AddKeyAction(ActionViewCloseup, Key.Key3, Key.Kp3);
    }

    private static void AddKeyAction(StringName action, params Godot.Key[] keys)
    {
        if (InputMap.HasAction(action))
        {
            return;
        }

        InputMap.AddAction(action);
        foreach (var key in keys)
        {
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
        }
    }

    /// <summary>每帧读一次三个 action：切预设。相机自己那套输入由相机自己读。</summary>
    private void ReadViewInput()
    {
        if (Input.IsActionJustPressed(ActionViewOverview))
        {
            ApplyView(StageView.Overview);
        }

        if (Input.IsActionJustPressed(ActionViewFollow))
        {
            ApplyView(StageView.Follow);
        }

        if (Input.IsActionJustPressed(ActionViewCloseup))
        {
            ApplyView(StageView.Closeup);
        }
    }

    private Camera3D? FindChildCamera()
    {
        foreach (var child in GetChildren())
        {
            if (child is Camera3D camera)
            {
                return camera;
            }
        }

        return null;
    }

    public override void _ExitTree()
    {
        // 交还托管引用（「离树交还」）：节点离树时表意愿，引用自己断。
        if (_rig is not null)
        {
            _rig.ViewClicked -= OnViewClicked;
            _rig.FollowToggleRequested -= CycleLockUnit;
        }

        _vfx?.Unwire();   // 特效那一份：逻辑壳与池节点一并交还，别指着已离树的那些
        _vfx = null;
        _bridge = null;   // 宿主是本节点的子节点，随本节点一起回收；这里只断引用

        _markers.Clear();
        _rulerLabels.Clear();
        _aimMesh = null;
        _eventLabel = null;
        _lineMaterial = null;
        _rig = null;
    }

    private static StandardMaterial3D BuildLineMaterial() => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        VertexColorUseAsAlbedo = true,
        Transparency = BaseMaterial3D.TransparencyEnum.Disabled,
    };

    private static StandardMaterial3D Unshaded(Color color) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        AlbedoColor = color,
    };

    /// <summary>脚下色带的颜色：按单位序号取一档，彼此分得开。不随属性变（那属造型决定，「造型进模型」）。</summary>
    private static Color MarkerColor(int index)
    {
        Color[] palette =
        {
            new(0.98f, 0.78f, 0.36f),
            new(0.55f, 0.80f, 0.98f),
            new(0.75f, 0.92f, 0.58f),
            new(0.98f, 0.62f, 0.72f),
            new(0.82f, 0.72f, 0.98f),
            new(0.62f, 0.94f, 0.90f),
        };
        return palette[index % palette.Length];
    }
}

/// <summary>
/// 场子的三套机位预设：<b>全景 / 跟随 / 特写</b>。
/// 三套都只给焦点、距离、俯角这三样，相机本身一律是公共层那台
/// （<see cref="TopDownRig"/>，「相机单源」）；「锁谁」是场子那边的玩法语义，留在场子侧。
/// </summary>
public enum StageView
{
    /// <summary>整块场地进画：看范围与群体。距离按场地尺寸推算，跟随解除。</summary>
    Overview,

    /// <summary>锁定一个单位看它的动作：焦点钉在它脚下，相机跟着它。</summary>
    Follow,

    /// <summary>贴脸：最近的一档，看前摇那一小段动作。</summary>
    Closeup,
}
