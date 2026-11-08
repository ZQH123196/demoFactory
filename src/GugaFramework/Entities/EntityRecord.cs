#nullable enable

namespace DemoFactory.GugaFramework.Entities;

/// <summary>
/// 实体册里的一条 —— <b>四格，一格不多</b>（spec D3）。
///
/// <para>血量、阵营、位置、属性一律不在这里。血量真值分散在好几处（单位侧各宿主各有一份，字段名各是各的；
/// 技能层用 hp 属性，建造层另有 <c>Building.Health</c>），
/// 硬抽到一处要动建造层与技能层两个已交付模块，seam 会漏（D2）。册子因此不持有任何血量字段。
/// 阵营是玩法语义（有的宿主两值、有的三值、有的只有一种），进册子就要在册子里判阵营，
/// 同样会漏（D4）。</para>
///
/// <para><b>四格里只有 <see cref="Kind"/> 是册子要答的</b>：「这一场有哪些 kind」是调试第一个要问的问题，
/// 现在要翻三个地方答。其余三格各由别人答：<see cref="Id"/> 是身份（原型自己拼的那串字符串）、
/// <see cref="Seq"/> 是册子发的那一格、<see cref="Payload"/> 是原型自己的数据对象（D7）。</para>
/// </summary>
/// <param name="Id">身份 —— 原型拼的那串字符串（<c>p01</c> / <c>hero_3</c> / <c>m1</c> 各是各的格式）。</param>
/// <param name="Seq">序号 —— 册子发的，单调递增、永不复发（D8、D9）。</param>
/// <param name="Kind">短字符串：这一条是哪一类（<c>unit</c> / <c>building</c> / <c>resident</c> 各是各的词表）。</param>
/// <param name="Payload">
/// 不透明载荷：<b>册子存它、还它、不读它</b>（照建造层 <c>payload</c> 那一格）。
/// 读档时按 id 对回原型自己那份数据，对不上是原型那一半的事。
/// </param>
public sealed record EntityRecord(string Id, long Seq, string Kind, object? Payload)
{
    public override string ToString() => $"{Id} seq={EntityNumbers.Format(Seq)} kind={Kind}";
}
