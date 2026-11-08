#nullable enable
using System;
using System.Globalization;
using Godot;

namespace DemoFactory.GugaFramework.I18n;

/// <summary>
/// 取字与格式化 —— <b>i18n 唯一入口</b>。
///
/// <para><b>键就是中文原文</b>（用户裁决 Q-I：中文当键）。代码里照旧直写中文，经这里透明查表替换：
/// 查不到就把中文本身当译文显示，所以「没有译文」和「没接 i18」在屏幕上长得一样，不会白屏、不会半截。
/// 把代码交给译者时，只要各原型的 <c>i18n/&lt;原型&gt;.csv</c>（首列即中文）就能翻，翻完填进目标列即可。
///
/// <para><b>为什么包 <see cref="TranslationServer"/> 而不是自建一套字典</b>：Godot 自带 CSV 导入、
/// 复数形式、运行中切语言与 <c>Control.auto_translate_mode</c> 的自动替换；复用它比自己手写查表 + 解析器更少代码。
/// 本类只是把「翻译 + 格式化」这两步合成一步、把不变文化钉死（「double 与不变文化」）。
///
/// <para><b>为什么在 <c>GugaFramework/I18n/</c> 而测试链不进来</b>：本类吃 Godot 类型（<see cref="TranslationServer"/>），
/// 与 Camera / DebugBridge 同一档，须加进 5 处测试 csproj 与 <c>src/Tools</c> 的排除面。真正需要它的只有表现层；
/// 判据测试（缺键 + 上屏插值）是纯 C#、读原始 CSV，不经过本类，所以排除不影响可测性。
/// </summary>
public static class Loc
{
    /// <summary>
    /// 取字。<paramref name="key"/> 就是中文原文；命中当前语言返回译文，没命中返回中文本身。
    /// </summary>
    public static string T(string key) => TranslationServer.Translate(key).ToString();

    /// <summary>
    /// 取字并填参。<paramref name="key"/> 是带 <c>{0}</c>／<c>{1:F1}</c> 的中文模板，<paramref name="args"/> 按序填。
    /// 格式串在模板里，译文必须保留同样的占位符——这是给译者的硬要求，写在 CSV 表头注释里。
    /// </summary>
    public static string Fmt(string key, params object?[] args)
        => string.Format(CultureInfo.InvariantCulture, TranslationServer.Translate(key).ToString(), args);
}
