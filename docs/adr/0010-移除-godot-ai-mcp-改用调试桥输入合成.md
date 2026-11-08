# 移除 godot-ai MCP，真机验证改走 Godot 可执行文件 + 调试桥

用户确认。`addons/godot_ai/`、`addons/.godot_ai_update/`、仓库根 `mcpServers.json`
连同 `project.godot` 里的 editor plugin 项与 `_mcp_game_helper` autoload 一并删除；`docs/AI能力手册.md`
的 47 个工具与 13 个只读资源清单、两条「MCP 直连」专节、契约五条同批改掉。真机输入链的替代件
`src/GugaFramework/DebugBridge/InputInjector.cs` 按「先接后删」在删除之前落地并实测通过。

## 触发它的四条事实

1. **端口是机器级的，按项目钉不住。** 插件的 `http_port = 8000` 写在
   `%APPDATA%\Godot\editor_settings-4.7.tres` 上，一台机一份，所有 Godot 项目共用同一个后端，
   项目之间靠 `session_id` 区分而不是靠端口。调试桥的端口每次启动自己指定
   （`DEBUG_BRIDGE_PORT` 或 `--debug-bridge-port=`）——**端口即寻址，连的就是自己起的那个进程**。
   同机开多个 worktree 各挑一个端口即可并行，这一条 MCP 结构上做不到。
2. **要占着编辑器。** MCP 通道的前提是编辑器开着、插件就绪。本项目其余验证全部落在
   `dotnet test` 与 Godot 可执行文件上，唯独这一条要求人守着一个窗口。
3. **`project_run` 默认 `autosave:true`，会把生成物搅脏。** 实测：预览场景跑一次
   `_PreviewBuildings.tscn` 脏 70 行。对由脚本生成的 `.tscn` 危害更大——
   一次 `project_run` 能把几百行搅成大 diff。
4. **账里实际只用过四个动作**，散在四轮里：
   `editor_state`（身份校验）、`project_run`（起局）、`get_scene_tree`（读运行时树）、
   `game_eval`（注入）。前三个各有替代；第四个文档早已把它降级成「桥未接线时的兜底」。
   那轮 OOM 事故里它是 1 发探针，事故成因是快速 stop→run 重启循环，与它无关。

## 给掉了什么

47 个工具里，本项目用不上的有四组：

- **13 个表现层作者工具**（`ui_manage` / `theme_manage` / `animation_manage` / `material_manage` /
  `particle_manage` / `camera_manage` / `audio_manage` / `tilemap_manage` / `tileset_manage` /
  `gridmap_manage` / `navigation_manage` / `csg_manage` / `animation_create`）。
  场景与造型在本仓由 `src/Tools` 的 `Gen.cs` 与 `.tscn` 文件产出，磁盘是唯一真值。
  其中四个与既有契约相顶，用了会把实现分叉：`animation_manage` 撞「动作在模型」、
  `camera_manage` 撞「相机单源」、`navigation_manage` 撞「寻路单入口」（寻路层是纯 C#，
  且有 `NavSourceGuard` 守卫禁止源码出现 `NavigationServer` / `NavigationAgent` / `AStar*`）、
  `csg_manage` 撞造型线（CSG 烘网格的时机在导出器内逐帧轮询）。
- **9 个场景与节点**、**9 个脚本 / 资源 / 文件系统 / 测试**。其中 `test_run` 在本仓没有目标对象
  （仓库唯一的 `tests` 目录是 C# 的 `src/GugaFramework.Tests`，没有 `res://tests/`）。
- **13 个只读资源**。全部有桥读数或本地替代。

编辑器侧的场景 / 脚本 / 节点 / 图块集作者能力，本项目此后**没有自动化入口**了。人做设计改动
仍然开编辑器，但那是人自己的事，不是 agent 的通道。

## 替代件：InputInjector

`src/GugaFramework/DebugBridge/InputInjector.cs`，公共层零游戏知识件，各世界各注册一次。
四条动词 `input_action` / `input_click` / `input_hover` / `input_read`。
`BridgeArgs` 加了 `Bool`（整条桥读参数的唯一出口），配 3 条测试。

**接之前先测了五条引擎事实**（headless 与窗口模式实测，不是记忆）：

1. 合成**滞后一帧**：同一次调用内读 `IsActionJustPressed` 读到的是注入**之前**的状态
   （12 发全 false；把合成与读放进同一帧回调里照样 false，所以不是求值延迟造成的）。
2. 原始事件**能到达** `Node._UnhandledInput`：合成 `InputEventKey`（Escape）与
   `InputEventMouseButton`（右键）都让一个世界的营建模式从有变成无；对照发 `Key.F13` 状态不变。
   相机架只吃滚轮与中键，不吞这两类。
3. 合成 `InputEventMouseMotion` **改不了** `GetViewport().GetMousePosition()`
   （无头恒 (0,0)、窗口恒真实光标位）。3D 拾取悬停走不了这条路。
4. `Input.WarpMouse` **能改它**（窗口模式三个目标都跟上且隔帧不弹回），但**无头读数恒零**，
   且它动的是真光标——窗口挪在屏幕外时会把光标带出可视区。
5. 合成 action 的**按下态消费后即清**，桥上读到的永远是 0。

**验出来一个设计缺陷**：第一版 `input_read` 打算报每个 action 的按下态。真机一跑全是 0，
而 `eval` 读私有字段显示世界确实进了营建模式（`placing=Market`）。⇒ 改掉：读数只报视口鼠标位置
与可注入的 action 名单，**注入有没有生效一律问世界自己**。这就是「先接后删」要拦的东西——
验晚了就会带着一个假读数去删 MCP，而那个假读数会一路留在代码里骗下一个 agent。

## Considered Options

- **只停用、不删文件**：功能一样拿掉，磁盘上留 2.1 MB。删文件要裁一条「外部插件禁改」的豁免，
  那是另一件事的代价。**用户裁决：删。** 契约「外部插件禁改」的判据已按裁决加了唯一一条例外
  （`addons/godot_ai/` 不在保护范围内，也不要装回来）。`addons/.godot_ai_update/backup/4.1.0/`
  那份插件副本一并删掉。
- **先删后接**：先停用 MCP 立刻受益，`InputInjector` 之后补。**用户否决**——「对于替换移除来说，
  先移除都是大忌」。本 ADR 记的正是执行这条否决的过程：替代件先落地、真机验通、再动删除。
  代价是删除那一刻没有 MCP 可用，收益是删除那一刻输入链已是验过的状态。
- **把输入合成也删掉不补**：那样真机输入链就完全没有自动化通道。**否决**——它本来就是零覆盖的
  （账里 4 轮真机的输入动作一次都不是靠 MCP 打的，都走 `inject` 动词），删 MCP 不产生回归，
  但补上它有收益。
- **保留 MCP 只为了编辑器侧的作者能力**：那 13 个工具里本仓用得上的一个没有，四个与契约相顶。
  保留的成本是每个 worktree 各开一个编辑器。**否决**。

## Consequences

- **验证不再需要开编辑器**。四条通道全在 Godot 可执行文件上：`dotnet test`（逻辑）、
  无头/窗口起进程加调试桥（运行时与注入）、`--write-movie`（逐帧画面）、
  `dotnet run --project src/Tools`（造型线与体检单）。实测：`--write-movie` 与
  `DEBUG_BRIDGE_PORT` 可以同时给，有画面与能注入合到一条通道上。
- **「先验身份」那条契约整条删除**。它防的是「端口上挂的是别的项目」，而那个风险源是机器级的
  8000；端口改成每次启动自己指定之后，这个风险在结构上不存在。留一条改写版等于给一个不会发生的
  风险留一句约束。探活由「三条铁律」① 管。
- **「先接后删」从「迁造型」拓宽到「任何能力」**。判据在迁造型那一步写的是「中间会出现没画面的窗口」，
  这一例证出来的代价是「唯一一条未被自动化覆盖的验证路径消失」——不是画面问题。
- **枚举档参数走不进 `BridgeArgs`**。那份是 `DebugBridge/` 里唯一的纯 C# 件（零 Godot 类型，
  所以能被测试项目链进去），而 `MouseButton` 是 Godot 枚举。实测口径：与 Godot 无关的标量走
  `BridgeArgs`（`Num` / `Int` / `Str` / `Bool`），枚举档留在认识那个枚举的代码旁边。
- **真机输入链有端到端断言**。端到端断言把「玩家按键 → 逻辑层改状态」整条链接起来验了：`input_action` 进营建 →
  `input_click` 点一次 → 建筑 7→8、金库 198→98、`market2` 进检查点快照，无头模式八条判据全通。
  3D 拾取那一格补了各世界的 `hover_pick` 读出口（窗口模式实测 `pick=none` → `pick=hero_0`）——
  没有这个读出口时，移了光标也读不到拾取结果，那条链只能靠出图人眼看。
  序列与判据见《验证操作手册》「端到端断言」与「3D 拾取读出口」两节。
- **合成输入进 Journal —— 用户裁决**。判据：玩家路径的写经 `Main.Write` 汇到 `_recorder.Inject`，
  **点击本身就是写路径的一部分**。真机录的 `Tests/Scripts/replay-mcp-smoke.json`（304 条）作证：
  真机上点出来的那座酒馆就在账里（`PlaceBuilding args=Tavern|-50|-50|… ok=True`），点选挂赏、落建、升级同理。
  据此把**输入事件那一层也录进去**：词表加 `SyntheticInputAction` / `SyntheticInputClick`
  （名字与形状收在公共层 `GugaFramework/Replay/SyntheticVerbs.cs`，入录口是宿主侧递进
  `InputInjector.Vocabulary` 的委托——公共件不认宿主词表）。
  **重放端一律跳过**：重放端是纯 C#，没有引擎也没有光标，复现不了「引擎收到了一次点击」，
  硬造就是拿假的世界骗自己（账上多几笔推不动的动作，快照却没变）。
  ⇒ 这一层**只增账面可读性、不增断言能力**，状态变更由紧随的逻辑写承担，那才是断言落点。
  真机实测账上那一串：
  `SyntheticInputAction place_market|1` → `…|0` → `SyntheticInputClick 900|600|left|1`
  → `PlaceBuilding Market|202.10547|159.18054`。
  接入时有个会静默红掉的坑：重放端**必须显式接住这两条**，漏接会落到各自的 `default`
  被判「不在词表」返 `false`，而录制端记 `ok=true` ⇒ `ok` 翻转。
- **一路上的两处环境事实**（都不是代码问题，但每次撞到都要重跑一遍处置）：
  ① `dotnet test` 一启动 `testhost` 就 `Win32Exception (5) 拒绝访问`——工作区根被压了
  `Mandatory Label\Low` 且带 `(OI)(CI)` 继承，`testhost` 以 Low 起、拿不到 Medium 父进程的句柄。
  处置 `icacls . /setintegritylevel '(OI)(CI)M' /T /C`。**这个标签已经长回来过一次**
  （这一点此前已被预判到），判据不变：`icacls <根>` 见 Low
  就是它，重跑同一条命令（这条陷阱现收在 `docs/debug 指南.md`）。② 工作区完整性标签修好后 `dotnet format` 立刻暴露出一处既有的空白违规
  （`DeploymentVerbs.cs` 48 行纯缩进），顺手修了——不修的话 format 检查会永久红着。
- **历史留档不抹。** 那 9 处提 MCP 的记录恰恰是将来解释「为什么整节消失了」最有力的证据；
  删文档不等于删历史，git 记录仍能逐字追回。
