# docs/research — 一次性验证代码

> 本文不写约束句（「约束唯一出处」）；此处只留事实、机制与操作步骤。

这里放为了回答「某个能力边界到底在哪」而写的验证代码，不参与游戏运行：不进游戏程序集（`DemoFactory.csproj` 里有 `<Compile Remove="docs\research\**\*.cs" />`），产物统一丢进项目根 `.cache/`。

---

## csharp-callable —— GDScript 能不能按小写方法名调到 C# 方法

**结论：能，而且不需要任何标注。**

### 为什么验这条

要让 C# 写行为树任务，前提是 GDScript 那侧 `child.tick(actor, blackboard)` 这种**小写名字**能打到 C# 方法上。Beehave 的整棵树一律靠方法名动态喊话。

### 实测矩阵（原文见 `.cache/probe_result.txt`）

| C# 里怎么写 | GDScript 侧看到什么 |
|---|---|
| `public int tick(GodotObject, GodotObject)` | `has_method("tick")=true`，调通，返回 `0` |
| `public int _safe_tick(...)` / `public void interrupt(...)` | 调通，返回 `2` / `null` |
| `public int TickPascal(...)` / `public int Ping()` | 按声明名喊得通；改成 `tick_pascal` / `ping` 则 `false` |

### 四条实测结论

1. **注册名 = C# 声明名逐字**，引擎不做 PascalCase→snake_case 转换（→ 「小写转发入口」）。参数用 `GodotObject` 就能吃下 GDScript 传来的任意 Node。
2. **4.7.2 的 C# 里没有 `[Callable]` 这个特性**——`Callable` 是结构体，写成特性报 CS0616。public 方法本身就对脚本可见。

### 图标这件事（只查到接口，没实测画出来）

`EditorPlugin.add_custom_type(...)` 在 4.7.2 存在，Beehave 自己就靠这条注册图标。**没有实测**图标真的画出来。`EditorInterface` 上**没有**它。

### 复跑方法

四个文件拷进 `src/` 下 → `dotnet build` → 按场景跑 `res://…/Probe.tscn` → 读 `.cache/probe_result.txt`。

### 两个写探针时踩到的 GDScript 坑

`untyped` 和 `class_name` 是 GDScript 2.0 的**保留字**，当局部变量名会让整个脚本报 `Parser Error`。

---

## 技能系统 —— 对标 GAS 之后定下来的取舍

**结账摘要在 [`技能系统/调研结论摘要.md`](技能系统/调研结论摘要.md)**；过程稿从 git 历史取。

---

## 存档：LimboAI 与 C#（该插件已从本项目移除）

留三条防止重复劳动：GDExtension 装法下 C# 编译期看不到 LimboAI 的类型而运行时不受影响；写自定义任务得换 module 版；GDScript 与 C# 不能互相继承。另：**MSBuild 遇到多个假设只汇总后一个文件的报错**。
