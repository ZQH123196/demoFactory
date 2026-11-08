# BattleArray.Tests — 逻辑层确定性测试

```bash
dotnet test src/Prototypes/BattleArray/Tests
```

搭法：2026-09-26 自 `src/Prototypes/_Template/Tests/` 整体复制，改了 csproj 文件名、
`WiringSmokeTests.cs` 的 namespace（★1、★2、★4）；`Scripts/Logic/*.cs` 那条链接原样保留
（现在是空目录，接上逻辑世界后自动生效）。

**接线规则、四条排除为什么在、边界含义**都在模板那份 README 里，此处不重复：
`src/Prototypes/_Template/Tests/README.md`。

当前内容：只有 `WiringSmokeTests`（2 条，证明源码链接与排除面完好）。
逻辑世界（阵型 / 移动 / 攻击结算）落地后，这里开始长真断言。
