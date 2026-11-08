#nullable enable

namespace DemoFactory.GugaFramework.Entities;

/// <summary>
/// 活的查询口 —— <b>整个宿主接口就这一个成员</b>（spec D6），形状照
/// <c>IConstructionHost</c>：不递 = 全活，测试与空场子不写假实现。
///
/// <para><b>「活着」不是层内的真值</b>。册子只提供查询口，真值问宿主：血量在建造层（建筑）、
/// 技能层（技能单位的 hp）、原型（单位）三处，抽一处要动两个已交付模块（D2、D3）。
/// 册子里出现一个 <c>IsAlive</c> 字段，就是那三个模块的边界各漏了一次。</para>
///
/// <para>册子里没有这一条 id 时也答 false —— 「不在册」与「在册但不活了」是两句不同的话，
/// 读数上分得开才算「显示即真相」。</para>
/// </summary>
public interface IEntityHost
{
    /// <summary>这个 id 还在不在。册子里没有它、或者它血量归零，都答 false。</summary>
    bool Alive(string id);
}

/// <summary>
/// 移除时册子要叫的两步（D11），各由各自的属主做 —— 寻路层那一步与宿主那一步不是同一双手。
///
/// <para><b>四步次序由 <see cref="EntityCore.Remove"/> 一处钉死</b>：摘册 → 寻路层定身与退注册 →
/// 放旗/腾引用 → 发事件。三家各写一份已经漂成三种（spec 2.4 第 2 条：退场没在寻路层定住，
/// 同一坑踩过三次，而「还在挪」这件事在读数上查不出来）。</para>
///
/// <para><b>「退注册」走哪两个既有 API</b>：寻路层<b>没有注销口</b>——<c>BeastNavMove.RetireAbsentResidents</c>
/// 的注释记着这一条（「层里没有注销口，所以这一步只是『停住 + 不再算进避让 + 从镜像面摘掉』」）。
/// 所以两步是 <c>Issue(id, NavCommand.Hold(当前位置))</c> 定身 + <c>SetCountedInSeparation(id, false)</c>
/// 退出站位冲突那一档。不给寻路层加能力（「寻路单入口」）。</para>
///
/// <para><b>两步拿的是那一条本身，不是 id</b>（<see cref="EntityRecord"/>）：
/// ①摘册已经先跑了，宿主这时回头按 id 查册子必然查不到 —— 拿到 null 就静默不做事，
/// 而「没做」正好就是退场还在挪的那一坑。册子手上还留着这一条，所以直接交出去。</para>
///
/// <para><b>不递这个接线 = 那两步没发生</b>，且读数里读得出来（<see cref="EntityCore.Readout"/>
/// 那一行「移除接线」）。静默跳过是最坏的一种：症状在好几步之后才冒出来。</para>
/// </summary>
public interface IEntityRemoval
{
    /// <summary>寻路层那一步：就地定住（换一条驻守命令），并退出站位冲突那一档。</summary>
    void DetainInNav(EntityRecord record);

    /// <summary>宿主那一步：放旗、腾引用、从宿主自己那份表里摘掉。</summary>
    void Release(EntityRecord record);
}
