// 预览舞台 —— 把一组预制件摆进自带地面/环境/灯光/相机的场景，供近景截图验收美术。
// （2026-09-23 自 preview_stage.mjs 移植；Node 原版已删，史实见 git log）
//
// 为什么值得单独一层：预制件的造型、剪影、配色、部件对位，在游戏运行时里很难看出来——
// 单位由逻辑流驱动、相机固定、还得先让单位活到出场时刻。反过来，占地是否越界、
// 运行时染色是否按实例生效、锚点驱动的血条尺寸，这些**只能**在真实关卡里验，
// 预览场景是纯 instance，那几条代码路径永不触发。两条通道各管一半，别互相替代。

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace DemoFactory.Tools;

public static class PreviewStage
{
    private static readonly (string Head, string Body)[] Subs =
    {
        ("type=\"PlaneMesh\" id=\"PlaneMesh_g\"", "size = Vector2(1, 1)"),
        ("type=\"StandardMaterial3D\" id=\"StandardMaterial3D_ground\"",
         "albedo_color = Color(0.2, 0.23, 0.2, 1)\nroughness = 0.95"),
        ("type=\"Environment\" id=\"Environment_1\"",
         "background_mode = 1\nbackground_color = Color(0.42, 0.48, 0.56, 1)\n"
         + "ambient_light_source = 3\nambient_light_color = Color(0.58, 0.6, 0.64, 1)\n"
         + "ambient_light_energy = 0.75\ntonemap_mode = 2"),
    };

    public sealed record Item(string Name, string Path, double X, double? Y = null, double? Z = null, double? Ry = null);

    public sealed record Cam(double[] Eye, double[] Tgt, double Fov);

    public sealed record PreviewInput(string File, string RootName, string? Uid, string GenNote,
        IReadOnlyList<Item> Items, Cam Cam, double[] SunEul, string? RootScript = null,
        IReadOnlyDictionary<string, string>? RootProps = null);

    public static Tscn.EmitResult PreviewScene(PreviewInput o)
    {
        var ext = new List<string>();
        if (o.RootScript != null)
            ext.Add($"[ext_resource type=\"Script\" path=\"res://{o.RootScript}\" id=\"0_script\"]");
        for (var i = 0; i < o.Items.Count; i++)
            ext.Add($"[ext_resource type=\"PackedScene\" path=\"res://{o.Items[i].Path}\" id=\"{i + 1}_p\"]");
        if (o.Uid == null) Console.Error.WriteLine($"⚠ UID 表缺 {o.RootName}，头行不带 uid");
        var lines = new List<string>
        {
            $"[gd_scene load_steps={Subs.Length + ext.Count + 1} format=3{(o.Uid != null ? $" uid=\"{o.Uid}\"" : "")}]",
            "",
        };
        // Godot 文本格式硬约束：ext_resource 必须整体排在 sub_resource 之前，否则报 Unknown tag
        lines.Add(string.Join("\n", ext));
        lines.Add("");
        foreach (var (head, body) in Subs)
        {
            lines.Add($"[sub_resource {head}]");
            lines.Add(body);
            lines.Add("");
        }
        lines.Add($"[node name=\"{o.RootName}\" type=\"Node3D\"]");
        if (o.RootScript != null) lines.Add("script = ExtResource(\"0_script\")");
        if (o.RootProps != null)
            foreach (var (k, v) in o.RootProps)
                lines.Add($"{k} = {v}");
        lines.Add($"metadata/_generated = {Tscn.JsonStr(o.GenNote)}");
        lines.Add("");
        lines.Add("[node name=\"Env\" type=\"WorldEnvironment\" parent=\".\"]");
        lines.Add("environment = SubResource(\"Environment_1\")");
        lines.Add("");
        var sun = o.SunEul;
        lines.Add("[node name=\"Sun\" type=\"DirectionalLight3D\" parent=\".\"]");
        lines.Add(Tscn.TfLine(Tscn.Rot(sun[0], sun[1], sun[2]), new[] { 0.0, 40.0, 0.0 }));
        lines.Add("shadow_enabled = true");
        lines.Add("directional_shadow_max_distance = 250.0");
        lines.Add("");
        lines.Add("[node name=\"Ground\" type=\"MeshInstance3D\" parent=\".\"]");
        lines.Add("transform = Transform3D(300, 0, 0, 0, 1, 0, 0, 0, 300, 0, -0.25, 0)");
        lines.Add("mesh = SubResource(\"PlaneMesh_g\")");
        lines.Add("cast_shadow = 0");
        lines.Add("surface_material_override/0 = SubResource(\"StandardMaterial3D_ground\")");
        lines.Add("");
        for (var i = 0; i < o.Items.Count; i++)
        {
            var it = o.Items[i];
            lines.Add($"[node name=\"{it.Name}\" parent=\".\" instance=ExtResource(\"{i + 1}_p\")]");
            lines.Add(Tscn.TfLine(Tscn.Rot(0, it.Ry ?? 0, 0), new[] { it.X, it.Y ?? 0, it.Z ?? 0 }));
            lines.Add("");
        }
        lines.Add("[node name=\"Camera3D\" type=\"Camera3D\" parent=\".\"]");
        lines.Add(Tscn.CamTr(o.Cam.Eye, o.Cam.Tgt));
        lines.Add($"fov = {Tscn.N(o.Cam.Fov)}");
        lines.Add("far = 800.0");
        lines.Add("current = true");
        lines.Add("");
        Tscn.WriteScene(o.File, lines);
        Console.WriteLine($"{Path.GetFileName(o.File)}  {o.Items.Count} instances");
        return new Tscn.EmitResult(o.RootName, o.Items.Count);
    }
}