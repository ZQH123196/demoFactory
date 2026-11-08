#nullable enable
using DemoFactory.GugaFramework.Abilities;

namespace DemoFactory.GugaFramework.Abilities.Stage;

/// <summary>
/// <b>开阔多点的场景壳</b>——引擎侧那一半，只服务 <see cref="SkillStageOpenField"/> 那一个场子。
///
/// <b>它是派生，不是重写</b>（ticket 09 补 ticket 07 留下的那笔债）：地面网格、标尺、名牌与脚下色带、
/// 朝向短线、指向线、事件尾窗、机位三套预设与手动微调，全部住在
/// <see cref="SkillStageScene"/> 那一个基类里，本文件<b>一个字都不重画</b>——
/// 抄一遍的代价与 ticket 02 记的那三处逐行同构转发器是同一笔账。
/// 所以场子本体从一个类长成两个类，这一侧只花掉两行：
///
/// <list type="number">
/// <item><b>递哪一份注册口</b>（<see cref="CreateSpec"/>）：开阔多点那一份
/// （<see cref="SkillStageSample.SpecOpenField"/>，一个出手 + 六个目标，场地半边长 30 米）。</item>
/// <item><b>起哪一个逻辑壳</b>（<see cref="CreateStage"/>）：<see cref="SkillStageOpenField"/>，
/// 也就是带「群体名单」与「群体施加」那两发的那个。</item>
/// </list>
///
/// <b>本文件仍然不认识任何一条具体技能、属性名或 tag</b>：那六个目标各是谁、各摆在哪，
/// 全在示范注册壳（<see cref="SkillStageSample"/>）那一侧，本文件只问它要一份注册口。
///
/// <b>三个场子与三份场景壳的分工</b>：贴脸单挑用基类那份默认注册口（多单位同框那一档），
/// 开阔多点用本文件，长时段是第一个场子把三格参数按长时段拧一下——它连类都不用换。
/// 三套机位预设与手动微调对三者逐字相同，因为它们住在基类里。
public partial class SkillStageOpenFieldScene : SkillStageScene
{
    /// <inheritdoc />
    /// 开阔多点那一份：一个出手 + 六个目标散在 6~12 米的开阔地上，场地半边长 30 米。
    /// 摆位是注册壳的场地条件（「表现层只读」 表现层只读），所以「开阔」这一档在本文件里一个数都没有。
    protected override SkillStageSpec CreateSpec() => SkillStageSample.SpecOpenField();

    /// <inheritdoc />
    /// 起 <see cref="SkillStageOpenField"/> 而不是基类默认那一个：群体名单与群体施加
    /// 这两发在派生逻辑壳上，基类那份没有——出图要能注入群体施加，逻辑壳得是对的那一个。
    protected override SkillStage CreateStage(SkillStageSpec spec, int seed)
        => new SkillStageOpenField(spec, seed);
}
