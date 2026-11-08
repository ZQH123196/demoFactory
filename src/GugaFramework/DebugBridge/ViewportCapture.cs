#nullable enable
using System;
using System.IO;
using Godot;

namespace DemoFactory.GugaFramework.DebugBridge;

/// <summary>
/// 视口截图（GugaFramework 通用件）—— 「落地配出图」「HUD / 表现层落地必须配一次出图复核」的执行手段。
///
/// <para><b>为什么上收</b>：出图复核是全原型的硬要求，而
/// 「把当前视口存成 PNG」这段与任何原型的游戏逻辑都无关。留在原型侧的结果是下个原型
/// 再抄一份、抄的那份还少了健壮性处理（目录不存在、扩展名漏写）。</para>
///
/// <para><b>主线程纪律</b>：必须经 <c>DebugBridgeHost</c> 的 <c>CallDeferred</c> 泵回主线程调用
/// （那条泵见 <c>DebugBridgeHost</c> 的类注）—— <see cref="Viewport.GetTexture"/> 是 Godot API，
/// 在 socket 收行线程上直接摸就是跨线程访问（「C# 文件基线」）。</para>
///
/// <para><b>返回的是人读的判定串，不是异常</b>：出图复核是「画面上看得见才算」的一类断言，
/// 失败原因必须从返回值里一眼读出来（目录？扩展名？空图？），不能只抛个 IO 异常让人再猜一遍。</para>
/// </summary>
public static class ViewportCapture
{
    /// <summary>
    /// 把某个节点所在视口的当前帧存成 PNG。<paramref name="path"/> 缺扩展名时补 <c>.png</c>，
    /// 目录不存在时建出来。
    /// </summary>
    /// <returns>形如 <c>shot=ok path=… size=WxH</c> 的判定串；失败时各前缀见失败原因。</returns>
    public static string SavePng(Node from, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "shot=no-path";

        // 少写扩展名是实测里真会发生的（人敲路径敲到一半就回车），而 Godot 那边的失败是
        // 泛泛的 err=14，够不着「其实是路径没后缀」这一层。自己补上。
        if (Path.GetExtension(path).Length == 0) path += ".png";

        var viewport = from.GetViewport();
        if (viewport is null) return $"shot=no-viewport path={path}";

        // GetImage() 在刚启动、还没画出任何一帧时会给空图 —— 那不是 bug，是「还没到时候」。
        // 分开报，别和保存失败混成一条。
        var image = viewport.GetTexture()?.GetImage();
        if (image is null || image.IsEmpty())
            return $"shot=empty-frame path={path}（视口还没画出过一帧）";

        // 目录不存在在 Godot 那边也只报泛泛的错；出图目录常是现造的（.cache/Shots），
        // 让人自己记得先建目录是白挨一次。
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return $"shot=no-dir path={path} err={e.GetType().Name}";
        }

        var error = image.SavePng(path);
        return error == Error.Ok
            ? $"shot=ok path={path} size={image.GetWidth()}x{image.GetHeight()}"
            : $"shot=failed err={error} path={path}";
    }
}
