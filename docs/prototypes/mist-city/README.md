# 迷雾之都（MistCity）

一句话定位：**无叙事的卡牌堆叠原型**——把牌叠在一起，按步产出，喂给下面的人，验这个循环在没有叙事包裹时成不成立。

- 代码：`src/Prototypes/MistCity/`（逻辑层 `Scripts/Logic/`，表现层 `Scripts/`）
- 状态：逻辑层完成（147 条断言全绿），点选走真机已验；**生死线待三个真人试玩**
- 定位来源：`docs/prototypes/各原型设计初始基准.md` 的迷雾之都那一行，偏离点见 [`design.md`](design.md)

| 文档 | 内容 |
|---|---|
| [`design.md`](design.md) | 核心循环、验证命题、生死线、与基准表的偏离、框架缺口、阶段进度、实测数字、待确认区 |
| [`试玩手册.md`](试玩手册.md) | 生死线试玩的操作步骤与判读表 |

## 起一局

```bash
dotnet test src/Prototypes/MistCity/Tests                    # 逻辑层（固定种子，~140ms）
dotnet build                                                    # 主程序集
$env:GODOT_BIN="<Godot 4.7.2 mono 可执行文件>"
dotnet run --project src/Tools -- game start res://src/Prototypes/MistCity/Scenes/Main.tscn --window --port 7777
dotnet run --project src/Tools -- debug ping --port 7777
dotnet run --project src/Tools -- game stop --port 7777        # 收尾：按端口停
```
