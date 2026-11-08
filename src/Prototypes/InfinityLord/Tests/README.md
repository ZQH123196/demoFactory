# InfinityLord.Tests — LogicWorld 确定性测试

> 本文不写约束句（「约束唯一出处」）；约束句一律写成 契约名 指针，此处只留事实、机制与操作步骤。

纯 C# 逻辑层的固定种子测试（2026-09-21 复盘提案 3 的落点，提案已全部闭环；分工见 `docs/AI能力手册.md`）。

## 运行

```bash
dotnet test src/Prototypes/InfinityLord/Tests
```

## 为什么这样搭

- **不引用主 csproj**：主项目是 `Godot.NET.Sdk`，会拖 GodotSharp 引擎引用进测试进程；
  改用 `<Compile Include>` 源码链接，只编译 `Scripts/Logic/*.cs` 与 `src/GugaFramework/**/*.cs`
  （两者均为纯 C#，零 Godot 类型）。
- **确定性**：`LogicWorld(randomSeed: 42)` + `Advance(n)`，断言不依赖 Godot 编辑器、
  不受窗口节流影响，毫秒级跑完。
- **选型**：xUnit v3 + Shouldly + Microsoft.NET.Test.Sdk。Shouldly 是断言层：xUnit 内置
  `Assert` 的 `Equal` / `Contains` / `Null` / `InRange` 都不带消息参数（实测 v3.2.2），
  而本项目每条断言都带一句中文判红理由（判红时给 agent 指路的那条信息，→ AGENTS.md「可调试性优先」），
  所以配 `x.ShouldBe(y, "说明")` 顶回原来 `Assert.That(x, Is.EqualTo(y), "说明")` 的形态。
- 与 MCP 实测闭环的分工 → AGENTS.md 「测试与实测分工」（本项目这一面是逻辑断言；命令与故障表在 `docs/验证操作手册.md`）。

## 边界

- `Scripts/Main.cs` 等 Godot 依赖文件不进本测试项目（→ AGENTS.md `src/Prototypes/_Template/Tests/README.md`；编译失败即边界警报）。
- 主 csproj 不引用本项目；测试项目独立构建，不进 Godot 导出。
