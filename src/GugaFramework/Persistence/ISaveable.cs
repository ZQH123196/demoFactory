#nullable enable

namespace DemoFactory.GugaFramework.Persistence;

/// <summary>
/// 存档接口 —— 原型那一半的边界。公共层只管文件、口径与上限，字段清单归原型自己写。
///
/// <para><b>边界手写，不用反射</b>。三个理由（缺一条都会静默分叉）：
/// <list type="number">
/// <item><c>GameClock</c> 排的事件挂的是<b>闭包委托</b>，反射拿不到可识别的句柄，
/// 必须有一张显式的待触发事件表由世界自己报告；</item>
/// <item>各逻辑世界的时钟是 <c>readonly</c> 字段初始化器，订阅全在构造函数里完成——
/// 读档要复现那个构造次序，反射只会再造一份空世界；</item>
/// <item>寻路步进器里的 agent 是私有类，外部反射扫不到它的字段。</item></list></para>
///
/// <para><b>写的那一半只写权威状态</b>：玩家做过什么、金币、职业、上限、单位当前位置。
/// 派生数据（净空场、路径缓存、血条百分比）不写，读档时重算；
/// 引擎句柄（模型、贴图、音频）绝不写，只写指向。</para>
/// </summary>
public interface ISaveable
{
    /// <summary>
    /// 这一份状态清单的版本号。存档文件头那个 <c>v</c> 取自它，
    /// 不等于 <see cref="SaveFile.CurrentVersion"/> 时走 <see cref="SaveMigration"/> 查表改到当前。
    /// </summary>
    int SchemaVersion { get; }

    /// <summary>
    /// 写权威状态。集合按调用方定死的次序排（一般按 Id 序数）——
    /// 往返测试要比的就是两串逐字相等，次序不稳就没有可比的两串。
    /// </summary>
    void WriteState(SaveWriter w);

    /// <summary>
    /// 读档：重建整个世界，含派生数据重算、寻路登记表按原次序重登记、待触发事件表重排。
    /// 途中遇到不一致当场抛，不返回可诊断的失败——恢复过程本身就会抛的时候，
    /// 把那一句抛留在恢复里，比包一层「读档失败」更好查。
    /// </summary>
    void ReadState(SaveReader r);
}
