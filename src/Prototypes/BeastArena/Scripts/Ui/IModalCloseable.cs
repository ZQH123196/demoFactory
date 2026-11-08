#nullable enable
namespace DemoFactory.Prototypes.BeastArena.Ui;

/// <summary>
/// 一个能被 Esc 逐层关掉的弹层 —— 面板自己知道「关掉」是收哪一块、摘哪一份模态。
///
/// <para><b>为什么不是让 <see cref="UiRoot"/> 猜一个 <c>Close()</c></b>：
/// 弹层的关闭动作不总叫 <c>Close</c>（命令栏那个目的地弹层是 <c>ClosePopup</c>，
/// 操作说明是 <c>SetOpen(false)</c>）。让每一件自己实现这一口，Esc 那一层只认接口。</para>
/// </summary>
public interface IModalCloseable
{
    /// <summary>关掉自己并摘掉自己那一份模态（幂等）。</summary>
    void Close();
}
