#nullable enable
using System.Numerics;

namespace DemoFactory.GugaFramework.Construction;

/// <summary>
/// 可建性查询的一个入参：格与坐标都在里面，规则只读自己那一个（D8）。
///
/// <b>为什么是两个字段而不是两个成员</b>：D8 把宿主接口定成「一个成员」，而网格按格问、
/// 自由按坐标问是两种问法。用一个值对象把两个问法装起来，宿主就仍然只实现一个成员，
/// 也不必为不需要的那一半写一个永远返回 true 的空实现。
/// </summary>
/// <param name="Cell">格（网格放置读它；自由放置忽略）。</param>
/// <param name="Point">世界坐标（自由放置读它；网格放置忽略）。</param>
public readonly record struct BuildSite(GridCell Cell, Vector2 Point)
{
    /// <summary>网格放置的问法。</summary>
    public static BuildSite At(GridCell cell) => new(cell, Vector2.Zero);

    /// <summary>自由放置的问法。</summary>
    public static BuildSite At(Vector2 point) => new(default, point);
}

/// <summary>
/// 原型递进来的可建性查询（D8）——<b>整个宿主接口就这一个成员</b>。
///
/// 问的是「这一处能不能建」，不是「这一处有什么」：地形、建造者、科技激活检查这些都在原型那侧，
/// 模块只拿到一个是非（spec 4 节「分工与留待未来」）。模块不认识地形内容（D3）。
/// <b>不递宿主 = 全可建</b>：构造参数留 null 就走这条，测试与空地场子不用写假实现。
/// </summary>
public interface IConstructionHost
{
    /// <summary>这一处能不能建。网格侧给格、自由侧给已经量化过的坐标（判的那点与落的那点是同一个数）。</summary>
    bool IsBuildable(BuildSite site);
}
