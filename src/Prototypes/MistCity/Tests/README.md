# Tests/ — 新原型测试项目模板

> 本文不写约束句（「约束唯一出处」）；约束句一律写成 契约名 指针，此处只留事实、机制与操作步骤。

纯 C# 逻辑层的固定种子测试住这里。**接新原型 = 把本目录整个复制到
`src/Prototypes/<原型名>/Tests/`，改四处**，不必再去照抄无限领主那份。

| ★ | 改什么 | 在哪 |
|---|---|---|
| 1 | 目录与文件名 `_Template.Tests.csproj` → `<原型名>.Tests.csproj` | 文件名 |
| 2 | `namespace _Template.Tests;` → `<原型名>.Tests;` | `WiringSmokeTests.cs` 顶部那行 `namespace`（复制过来的每个测试文件同理） |
| 3 | 第二列 `<Compile Include>`：`..\Scripts\Logic\*.cs` 一般不用动（约定就在这层）；有单位 AI 且写了纯 C# 决策类时，加一行 `..\AI\<决策类>.cs LinkBase="AI"` | csproj 尾部 |
| 4 | 本 README 与 `WiringSmokeTests.cs` 里的「_Template」字样改成原型名（仅注释与日志，不影响编译） | 两个文件 |

## 跑起来

```bash
dotnet test src/Prototypes/<原型名>/Tests        # 独立构建，不进 .sln、不进 Godot 导出
```

## 排除面就是边界（这条排除面是哪条约束 → AGENTS.md 「逻辑层纯 C#」）

`Exclude` 那一行点名排除 20 处，全是吃引擎的表现层件（吃 `Node3D` / `AnimationPlayer` /
`TcpListener` / `Godot.Node`），链进测试进程直接编译失败。**这条排除面就是「逻辑层零感知引擎」
的机器可执行版本** —— 删掉其中一条就等于宣布该表现层组件可以被逻辑层引用。
`WiringSmokeTests` 是这条边界的报警器。

| 排除什么 | 为什么 |
|---|---|
| `Camera/**`、`Animation/**` | 相机架与动画驱动器，吃 `Node3D` / `AnimationPlayer` |
| `I18n/**` | 取字入口与字体安装，吃引擎字体与翻译接口 |
| `DebugBridge` 按文件点名 5 件 | 宿主（`TcpListener`）、求值器（Roslyn）、`Attach` 接线、视口出图、输入合成 |
| `FrontEnd` 三件、`Vfx` 两件 | 前端层与特效层吃 `CanvasLayer` / 粒子节点 |
| `Abilities/Stage` 两件、`Pathfinding/Stage` 两件 | 三个试验场的场子脚本，要真跑得起来才验得出 |
| `beehave/**` | 包内 C# 件吃 `Godot.Node`；决策另放纯 C# 类（见上面 AI 那条） |
| `Guard/LogRateWatchdog.cs` | 吃引擎日志接口 |
| `GugaFramework/Tests/**` | 公共层自己的测试，另在它自己的项目里跑 |

同目录的 `BridgeArgs.cs` 是纯 C# 取参口径，链得进来、也就断得了 ——
排除面写精确，免得哪天往 `DebugBridge/` 加纯 C# 件时被通配符顺手挡掉。

**加新排除的判据**：那份件吃引擎就点名排除，不吃就留着。
2026-10-13 核过一遍：`GugaFramework` 下带 `using Godot;` 的文件正好 20 处，与这 20 项一一对应。

## 为什么不用 `ProjectReference` 引主 csproj

主项目是 `Godot.NET.Sdk`，引用它会把 GodotSharp 与引擎依赖整个拖进测试进程；
所以走 `<Compile Include>` 源码链接，只编纯 C# 的那部分。选型（xUnit v3 + Shouldly）记在
`docs/GugaFramework/确定性与重放.md`；逻辑面与 MCP 实测面的分工 → AGENTS.md 「测试与实测分工」。
