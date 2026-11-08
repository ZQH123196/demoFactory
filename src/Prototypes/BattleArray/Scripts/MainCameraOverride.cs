#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace DemoFactory.Prototypes.BattleArray;

/// <summary>
/// 机位覆盖口 —— 让「调视角」在离线出图那条道上也是一发命令的事。
///
/// 为什么要它：调一档机位要看的命题是「画面读不读得清」，出图走 <c>--write-movie</c>，
/// 那条道上没有 eval 通道（引擎自己渲染，没人能中途注代码），于是每换一档都要改常量重编。
/// 有了这一发，扫档 = 同一次构建跑 N 条命令。运行时那一侧不重复造口：
/// <see cref="CameraSetPose"/> 已经能把相机摆到任意机位，两条路汇到同一个 <c>TopDownRig.SetPose</c>。
///
/// 用法（环境变量 <c>BATTLE_ARRAY_CAM</c> 或用户参数 <c>--cam=…</c>，键值用 <c>;</c> 分隔）：
/// <code>
/// BATTLE_ARRAY_CAM="fov=32;pitch=30;yaw=45;dist=26;focusz=2.5"
/// </code>
/// 认的键：<c>fov</c>（垂直视场角，度）、<c>pitch</c>（整场俯角）、<c>yaw</c>（整场偏航）、
/// <c>dist</c>（整场距离）、<c>focusz</c>（整场焦点深度）、<c>ref</c>（名牌与点选容差的参考机位）、
/// <c>min</c>/<c>max</c>（距离上下限）、
/// <c>duelpitch</c>/<c>dueldist</c>（单挑取景的俯角与距离，给了 dueldist 就不再按间距倒推）。
/// 没给的键一律用 <see cref="Main"/> 里那组缺省值 ⇒ 不开这个口时行为与原来逐字相同。
///
/// 这一发只碰表现层：机位是表现层状态，不进 <c>BattleWorld</c>，确定性测试与重放基线不受影响。
/// </summary>
public partial class Main
{
    /// <summary>机位覆盖的环境变量名（与 <see cref="ShowcaseEnvVar"/> 同一形状）。</summary>
    public const string CameraEnvVar = "BATTLE_ARRAY_CAM";

    /// <summary>解析出来的覆盖档；null = 没覆盖，全部走缺省常量。</summary>
    private Dictionary<string, float>? _camOverride;

    /// <summary>取一个覆盖值，没给就用缺省（缺省值仍是 <see cref="Main"/> 里那组常量，不在这儿另立一套）。</summary>
    private float CamVal(string key, float fallback)
        => _camOverride is not null && _camOverride.TryGetValue(key, out var v) ? v : fallback;

    /// <summary>
    /// 读环境变量或 <c>--cam=…</c> 用户参数，解析成键值档。
    /// 解错的键打一行警告并忽略那一条（调参时写错键名是常事，别让整个原型起不来）。
    /// </summary>
    private void ReadCameraOverride()
    {
        var raw = OS.GetEnvironment(CameraEnvVar);
        if (string.IsNullOrWhiteSpace(raw))
        {
            foreach (var arg in OS.GetCmdlineUserArgs())
            {
                if (arg.StartsWith("--cam=", StringComparison.Ordinal))
                {
                    raw = arg.Substring("--cam=".Length);
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(raw)) return;
        var table = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var pair in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0)
            {
                GD.PushWarning($"[BattleArray] {CameraEnvVar} 里这一条不是 key=value，已忽略：{pair}");
                continue;
            }

            var key = pair.Substring(0, eq).Trim().ToLowerInvariant();
            if (!float.TryParse(pair.Substring(eq + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                GD.PushWarning($"[BattleArray] {CameraEnvVar} 的 {key} 不是数字，已忽略：{pair}");
                continue;
            }

            table[key] = value;
        }

        _camOverride = table;
        GD.Print($"[BattleArray] 机位覆盖已生效：{raw}");
    }
}
