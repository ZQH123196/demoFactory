#nullable enable
using System.Collections.Generic;

// 目录名与命名空间不同名是故意的：命名空间叫 Input 的话，
// 任何位于 DemoFactory.GugaFramework.* 下的文件写裸 Input 都会被它抢走
// （C# 先在 DemoFactory.GugaFramework 里找命名空间成员，命中就不再问 Godot.Input），
// 于是 Godot.Input.IsActionPressed 与 Input.MouseModeEnum 一起变成 CS0234 ——
// 那批文件不在这一张 ticket 的归属范围内，所以改的是这边，不动它们。

namespace DemoFactory.GugaFramework.InputLayer;

// 输入概念 —— 一个输入动作叫什么、按哪种读法去问。
//
// 公共层只定「这些概念存在、统一叫什么」；绑哪个键归原型（键位是内容决定）。
// 公共层另一件定的是「这个概念按哪种读法读」，而读法是**必填**的：
// 漏给读法编译不过，读法选错的后果是「按住不放重复触发」。

/// <summary>一个概念按哪种读法去问引擎。</summary>
public enum ReadMode
{
    /// <summary>刚按下：一次物理按下只成立一次。旋转钮、暂停、单步、变速走这一档。</summary>
    JustPressed,

    /// <summary>按住：只要还按着每帧都成立。平移、绕转、缩放走这一档。</summary>
    Held,

    /// <summary>模拟量：取动作强度（0~1），不取真假。</summary>
    Axis,
}

/// <summary>
/// 一个输入概念 —— 名字 + 读法。<paramref name="Read"/> 不是可选项：
/// 建一个概念就必须说清按哪种读法读，省掉它等于把「按住重复触发」那条坑重新打开一次。
/// </summary>
/// <param name="Name">action 名（公共名，绑定到哪个键由原型决定）。</param>
/// <param name="Read">读法。</param>
public sealed record InputConcept(string Name, ReadMode Read)
{
    /// <summary>暂停 / 恢复 —— 刚按下。三个原型共用这一个概念名，绑各自的键。</summary>
    public static readonly InputConcept TogglePause = new("input_toggle_pause", ReadMode.JustPressed);

    /// <summary>单步 —— 刚按下。</summary>
    public static readonly InputConcept StepOnce = new("input_step_once", ReadMode.JustPressed);

    /// <summary>提速一档 —— 刚按下。</summary>
    public static readonly InputConcept SpeedUp = new("input_speed_up", ReadMode.JustPressed);

    /// <summary>降速一档 —— 刚按下。</summary>
    public static readonly InputConcept SpeedDown = new("input_speed_down", ReadMode.JustPressed);

    /// <summary>镜头缩放（拉近）—— 按住：缩放是连续量，每帧都要读。</summary>
    public static readonly InputConcept CameraZoomIn = new("camera_zoom_in", ReadMode.Held);

    /// <summary>镜头缩放（拉远）—— 按住。</summary>
    public static readonly InputConcept CameraZoomOut = new("camera_zoom_out", ReadMode.Held);

    /// <summary>镜头左绕 —— 按住。</summary>
    public static readonly InputConcept CameraRotateLeft = new("camera_rotate_left", ReadMode.Held);

    /// <summary>镜头右绕 —— 按住。</summary>
    public static readonly InputConcept CameraRotateRight = new("camera_rotate_right", ReadMode.Held);

    /// <summary>镜头抬俯 —— 按住。</summary>
    public static readonly InputConcept CameraPitchUp = new("camera_pitch_up", ReadMode.Held);

    /// <summary>镜头压俯 —— 按住。</summary>
    public static readonly InputConcept CameraPitchDown = new("camera_pitch_down", ReadMode.Held);

    /// <summary>镜头前推 —— 按住。</summary>
    public static readonly InputConcept CameraPanForward = new("camera_pan_forward", ReadMode.Held);

    /// <summary>镜头后拉 —— 按住。</summary>
    public static readonly InputConcept CameraPanBack = new("camera_pan_back", ReadMode.Held);

    /// <summary>镜头左移 —— 按住。</summary>
    public static readonly InputConcept CameraPanLeft = new("camera_pan_left", ReadMode.Held);

    /// <summary>镜头右移 —— 按住。</summary>
    public static readonly InputConcept CameraPanRight = new("camera_pan_right", ReadMode.Held);

    /// <summary>跟随锁定切换 —— 刚按下：按住会来回翻。</summary>
    public static readonly InputConcept CameraFollowToggle = new("camera_follow_toggle", ReadMode.JustPressed);

    /// <summary>机位复位 —— 刚按下。</summary>
    public static readonly InputConcept CameraResetPose = new("camera_reset_pose", ReadMode.JustPressed);

    /// <summary>镜头那十条，按登记次序。</summary>
    public static readonly IReadOnlyList<InputConcept> CameraConcepts =
    [
        CameraZoomIn, CameraZoomOut, CameraRotateLeft, CameraRotateRight, CameraPitchUp, CameraPitchDown,
        CameraPanForward, CameraPanBack, CameraPanLeft, CameraPanRight,
        CameraFollowToggle, CameraResetPose,
    ];

    /// <summary>时间旋钮那四条，按登记次序。</summary>
    public static readonly IReadOnlyList<InputConcept> TimeKnobConcepts =
    [
        TogglePause, StepOnce, SpeedUp, SpeedDown,
    ];
}
