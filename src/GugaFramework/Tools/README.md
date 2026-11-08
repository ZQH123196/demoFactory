# GugaFramework/Tools — 开发期 GDScript 工具

> 本文不写约束句（「约束唯一出处」）；约束句一律写成 契约名 指针，此处只留事实、机制与操作步骤。

**这里的东西不进游戏、不被 csproj 编译**，所以和 `GugaFramework/` 根下的运行时 C#
（`Base`/`EventBus`/`Scheduling`）分两层。这条线见 AGENTS.md「沉淀方向」：
「产物进不进最终包」。

> **2026-09-23 变更**：`.tscn` 序列化 / 预览舞台 / 离线校验器三件通用引擎层已从本目录
> 迁到 `src/Tools/`（C# 独立工具项目，`dotnet run --project src/Tools`），Node 脚本
> 时代结束、`*.mjs` 全部删除。本目录只剩模型流水线的 GDScript 侧
> （导出器 / 动作库 / 校验器 / 轮播，必须在 Godot 引擎里跑，无法 C# 化）。

## 模块

| 文件 | 管什么 | 换原型时 |
|---|---|---|
| ~~`tscn.mjs` / `preview_stage.mjs` / `tscn_check.mjs` / `imgdiff.mjs`~~ | **2026-09-23 迁走/删除** — 前三件现居 `src/Tools/`（Tscn.cs / PreviewStage.cs / TscnCheck.cs，原型薄 spec 在各原型 `Tools/*.cs`）；imgdiff 是画面回归线，用户确认裁撤 | 见 `src/Tools/` |
| `glb/export_glb.gd` | **模型导出器**：把关节树源场景（`j_` 前缀）编译成真模型资产 `.glb` —— 骨架 + 刚性蒙皮 + 动作，全烘进一个文件。表在原型侧，本文件只剩机制 | 不用改，只加原型侧 `ExportModels.gd` |
| `glb/clips_humanoid.gd` | 人形动作库：idle / walk / run / attack_shoot / attack_melee / death，按骨名给关键帧 | 直接复用 |
| `glb/clips_mount.gd` | **骑乘动作库**（2026-09-26 加）：坐骑四肢自己写、骑手轨道从人形库挑着搬（只留躯干/头/手臂）。登记表里 `lib = "mount"` 才走这座库 | 直接复用 |
| `glb/inspect_model.gd` | **导出后校验器**：读回 `.glb` 查四类事——占地上限、每条 clip 是否真让骨骼离开绑定姿势、野节点、缺 clip；任一不合格 `quit(1)`。模型目录由 `--models-dir=` 给（写死目录 = 锁在一个原型上） | 改契约即用 |
| `glb/model_metrics.gd` | **读模型出数字**的唯一一份实现（共用一次计算、不各写一遍 → AGENTS.md 《模型与动画准则》「实现分层与聚合」）：占地 AABB、骨数、每条 clip 的最大骨偏移、野节点、锚点。校验器与体检单都调它 | 直接复用 |
| `review/review_capture.gd` + `review_stage.tscn` | **模型体检单**：窗口模式（屏外）一次出十视图 / 每条 clip 的动画胶卷 / 叠层标记 / 比例尺同框 + `facts.txt`，并自带产物自检四条 | 直接复用 |
| `glb/preview_autoplay.gd` | 验收台的轮播开关（开发期脚手架，不进游戏） | 直接复用 |

## 模型流水线（真资产，不是运行时拼的图元）

```
Gen.cs 造型（j_ 关节树 .tscn，源）
   → export_glb.gd 编译 → Models/<Name>.glb（骨架+蒙皮+动作）
   → Godot 自动导入 → 游戏 GD.Load 这个 .glb，用 ModelAnimator 喂状态
```

游戏侧**不认识造型**：不在运行时决定谁长什么样、不给模型换色、不缩放几何。
命令（项目根）：

```bash
dotnet run --project src/Tools -- gen <原型> .            # 关节树源场景
Godot..._console.exe --headless --path . --quit-after 300 \
  --script res://src/GugaFramework/Tools/glb/export_glb.gd -- --proto=<原型>
Godot..._console.exe --headless --path . --import                # ★ 触发导入，生成 .glb.import 伴随文件
Godot..._console.exe --headless --path . --quit-after 60 \
  --script res://src/GugaFramework/Tools/glb/inspect_model.gd --     --models-dir=res://src/Prototypes/<原型>/Models/ --all --max-footprint=12
dotnet run --project src/Tools -- check <原型> .          # 离线校验器（.tscn 几何）
dotnet run --project src/Tools -- preview <原型> . walk   # 验收台（指定要看的动作）
dotnet run --project src/Tools -- review <原型> .         # 模型体检单（窗口模式）
```

`--proto=<目录名>`（**必填**）让导出器读本原型 `Tools/ExportModels.gd` 那三张表
（源目录 / 产物目录 / 模型清单）。没传或表加载失败 → `push_error` + `quit(1)`，不会静默用别人的表。
公共层因此不留任何原型的数据：最早内建的那份表早已搬进各原型自己的
`src/Prototypes/<原型>/Tools/ExportModels.gd`，搬完重导全部模型
**`.glb` md5 逐字节全等**（体检单报告 `diff` 也为空），机制与参数见 `docs/模型与动画准则.md`。

`review` 要 Godot 可执行文件，按 `--godot=<路径>` > 环境变量 `GODOT_BIN` > PATH 上的 `godot` 依次找。
**这一环不能 `--headless`**（dummy renderer 不出图），是整条流水线里唯一走窗口模式的一环；
其余（导出、校验器）照旧无头。产物：图落 `.cache/Review/<原型>/`（不提交进版本库），
体检单落 `docs/prototypes/<原型>/model-review.md`（提交进版本库），规格见 `docs/《模型与动画准则》「审阅工位」起。

导出器另有 `-- --list`（打表里的模型名）；校验器按契约逐项判：`--models-dir=` 必传（模型目录），
`-- --model=Ranger` 只查一个，
`--clip=walk` 指定要验的 clip，`--need-clips=idle,walk` 断言动作齐不齐。
**占地校验跑在导出后的模型上**（不是源场景）—— 源场景过拟合不等于 .glb 合用，
CSG 烘进顶点后尺寸才是最终尺寸。任一不合格退出码 1。

### 导出与蒙皮的引擎事实（4.7 实测，写码前先读）

1. **`skin` 挂在 `MeshInstance3D` 上**，不在 `Mesh`/`ArrayMesh` 上（4.7 已搬走；
   `Mesh` 类现在只剩 `lightmap_size_hint` 一个属性）。`ArrayMesh.skin = ...` 直接报
   "Invalid assignment"，连读都读不到。
2. **顶点必须烘进模型空间**（从场景根累乘下来），不能烘进各自关节的局部系。
   后者不报错，画面表现为「整个人塌成一堆、部件缩到自己关节的原点」——实测踩过。
   绑定矩阵（`create_skin_from_rest_transforms()`）负责「模型空间 → 骨骼空间」这一跳。
3. **骨骼动画轨道路径必须写成 `"Skeleton3D:骨名"`（冒号）**。写成 `"Skeleton3D/骨名"`
   不报错、播不动，只在导出时留一句 `Cannot get node for animated track`，动画静默丢失。
4. **`--script` 跑的无头脚本里 `quit()` 在 `_initialize` 阶段不可靠**，且全局变换要等真实帧
   才算好 —— 建树与导出必须分帧（用 `_process` 的帧计数，最后 `return true` 收尾）。
   另外 `AnimationPlayer.add_animation()` 在 4.7 已移到 `AnimationMixer` 的库接口：
   `add_animation_library("", lib)` + `lib.add_animation(name, anim)`。
5. **CSG 的合并网格要等它算完**：源场景入树后要轮询 `bake_static_mesh()` 直到非空（就绪快慢随机器与帧率变，
   旧实测约 40 个渲染帧 ≈ 0.7 秒，这个数不当等待时长写进代码——口径 → 《模型与动画准则》「三条固定项」），
   所以导出前逐帧轮询 `_csg_settled()`；而且**只有最顶层**的 CSG 节点产得出网格
   （子刷形状是父节点的输入，永远 null）。`_update_shape` / `bake_static_mesh` 带 EDITOR
   方法标记，但无头 `--script` 里照样能调。
6. **CSG 烘出来的是非索引、无 UV 的网格**：`surface_get_arrays()` 里 `ARRAY_INDEX` /
   `ARRAY_TEX_UV` 是 null，赋给带类型的局部变量直接 `Invalid access to property or key ... on 'Nil'`
   —— 按可缺省处理。
7. **`_PhysicalBoneSimulator3D` 不是源树里的节点，是 GLTFDocument 导出时自己塞进文件的**：
   实测只有一根骨的裸 Skeleton3D，源树查不到该节点，写出的 .glb 里却多一个（名字带 `_N` 去重后缀）。
   摘不掉也不必摘 —— 校验器对它单独开白名单，其余野节点照旧判失败。
8. **源场景约定面朝 +Z，产物前脸朝 -Z**（产物朝向这条约定 → AGENTS.md 《模型与动画准则》「三条固定项」）：导出器在根下垫一个转了 180° 的 `Rig` 节点。
   游戏侧的朝向表按这条给（宿主侧的建筑朝向表因此全是 180/90 而不是 0）。
9. **glTF 里没有「循环」这个字段**：导出器给 clip 设的 `LOOP_LINEAR` 到不了 `.glb`
   （实测文件 JSON 块 `loop` 字样 0 次），读回来一律 `LOOP_NONE`。
   ⇒ 谁消费谁定：游戏侧 `ModelAnimator.PlayBase()` 播基动作前置 `Linear`；
   审阅侧由原型契约 `LoopClips` 声明。也别拿「首末键相等」反推 —— 出手动作回起手式，同样相等。

## 新原型怎么接

**注册机制**：`src/Tools/DemoFactory.Tools.csproj` 按 `Prototypes\*\Tools\*.cs` 通配源码链接，
`src/Tools/Program.cs` 反射发现 `IPrototypeTools` 的实现。已实测：`_Template` 只写一份契约文件，
`review` 整条线就通了。

抄 `_Template/Tools/TemplateTools.cs` 起步（三处标 ★），再抄原型侧的薄文件
（参考 `src/Prototypes/<原型>/Tools/`），通用层一行都不要复制：

- `<原型>Tools.cs` —— 注册入口：`DirName` + `ReviewContract`（体检单契约）+ 自有动词分发
- `Gen.cs` —— 配色表、各 `Build*()` 造型、`Emit()` 薄封装（把 `palette`/`genNote`/`uid` 传进 `EmitScene`）
- `GenPreview.cs` —— 摆哪些件、机位在哪
- `Check.cs` —— 只传契约：扫描目录、占地上限及其路径匹配式、锚点节点名、贴合容差、重生成提示
- `Uids.cs` —— 源场景与预览舞台的 uid 表（一次登记完，别每加一个模型改一次）
- `ExportModels.gd` —— 导出器读的三张表：`src_root()` / `out_root()` / `models()`（模型名 → 源场景 + 要烘的 clip；
  骑马这类双骨架模型再加一个 `lib = "mount"` 选骑乘动作库，缺省走人形库）

**人形单位不要复制骨架代码**：`src/Tools/HumanoidRig.cs` 是公共层的
`Humanoid.Rig(...)`（16 节人形骨架 + u 基准画笔）与 `Humanoid.Anchor(...)`，
`Build*()` 里只写装备与配色。已接进来的原型都调它 ——
本文件以前说「最早那份 private 实现是历史件、迁移另立账」，那条账已结掉：
那份原型侧的 `Tools/Gen.cs` 删掉 private `RigHumanoid()` / `Rig` / `HumanoidBrush` / `Anchor()`
（−169 行），改调公共层，全部源场景与 `.glb` 重跑**逐字节全等**。

**四足坐骑也不要复制骨架代码**：`src/Tools/QuadrupedRig.cs`（建骑手兵种时提炼）
给 `Quadruped.Rig(...)` —— 17 节马/狼骨架 ＋ 躯干四肢肉体，鞍具与护甲由原型侧挂在返回的关节上。
两条约定（骨架共存那条 → AGENTS.md 《模型与动画准则》「一个模型挂两副骨架」）：**骨名带 `m_` 前缀**（骑手的人形骨与坐骑骨挂在同一副骨架上，
导出器按「骨名 → 序号」寻骨，重名会静默覆盖）；**腿的段长按角度自动解**
（给定关节离地高度与蹄高，解出两段的垂直落差之和），所以怎么调姿势四蹄都踩在 y=0。
骑手挂在 `J["saddle"]` 下面 ⇒ 坐骑背线一颠骑手跟着颠。
动作侧配套 `glb/clips_mount.gd`，登记表里 `lib = "mount"` 选用。

现成的最薄参考：**`_Template`**（`src/Prototypes/_Template/Tools/`，只写一份契约文件）——
接新原型照它起步，再按上面那份清单逐件补。

通用层（`src/Tools/Tscn.cs` 等）以源码链接进 `src/Tools/DemoFactory.Tools.csproj`，
原型 spec 物理上留在原型 `Tools/` 里、链接进同一工具项目；引擎**故意不给任何契约默认值**
（连锚点名都要显式传），因为契约属于原型，不属于公共设施。

## 两条使用约定

- **幂等**：产物浮点统一 4 位定点、`-0` 归零、绝对值小于 5e-4 归零（`src/Tools/Tscn.cs` 的 `Num()`）。
  改它之前先确认重跑生成器后 `git diff` 仍为空——这是"能放心重跑"的前提。
- **`EmitScene` 一次只生成一个场景**（静态场景状态），且 `build()` 抛异常时会在 `finally`
  里复位，不会把脏 fit/调色板留给下一个场景。别并发调用它。
