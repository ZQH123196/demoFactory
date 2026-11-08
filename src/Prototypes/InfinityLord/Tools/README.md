# 造型源场景生成器（关节树 → .glb）

> 本文不写约束句（「约束唯一出处」）；约束句一律写成 契约名 指针，此处只留事实、机制与操作步骤。

`Tools/Rigs/` 下的 20 个 `.tscn`（17 份造型源场景 + 3 个预览舞台，共 914 节点）由本目录的 C# 工具产出
（2026-09-23 起：通用引擎层在 `src/Tools/`，本目录只剩薄 spec —— Node 脚本时代已结束，`*.mjs` 全部删除）。
源场景是**编译产物**，`Gen.cs` 才是源；而源场景本身也只是**中间产物**——
它用 `GugaFramework/Tools/glb/export_glb.gd` 编译成 `Models/<Name>.glb`（骨架 + 蒙皮 + 动作），
游戏只加载 .glb。`Scenes/` 下不再有造型，`Tools/Rigs/` 不进游戏运行时。

**本目录只有本原型的造型与契约，通用层在 `src/GugaFramework/Tools/`**（该目录另有 README，含新原型接入步骤）：

| 位置 | 内容 | 换原型时 |
|---|---|---|
| `src/Tools/Tscn.cs` | `.tscn` 文本序列化 + 单位图元表 + 形状 DSL（`Bx/Cy/Cn/Py/Disc/Limb/TipAt/CsgBox/...`）+ 写盘器。文件头记着 7 条 Godot 4 引擎事实 | 直接复用 |
| `src/Tools/PreviewStage.cs` | 预览舞台：地面 + 环境 + 平行光 + 相机取景，把任意一组源场景摆成一排 | 直接复用 |
| `src/Tools/TscnCheck.cs` | 离线校验器引擎：解析 `.tscn` → 沿父链累乘求世界 AABB → 格式与几何两组检查，契约由调用方传参 | 直接复用 |
| `GugaFramework/Tools/glb/export_glb.gd` | 关节树 → `.glb` 编译器（会动的导骨架+蒙皮+clip，不动的烘成无骨架单网格） | 不用改，只加本目录那份表 |
| `src/Tools/HumanoidRig.cs` | 人形骨架 `Humanoid.Rig()`（16 节 + u 基准画笔）与 `Humanoid.Anchor()` | 直接复用 |
| `GugaFramework/Tools/glb/inspect_model.gd` | 导出后校验器：占地 / clip 是否真让骨骼动 / 野节点 / 缺 clip，不合格 `quit(1)` | 改契约即用 |
| 本目录 `Gen.cs` | 配色表 `MAT`、15 个 `Build*()`（`BuildGoblin(tier)` 覆盖三阶，共 17 份源场景）、`Emit()` 薄封装 | 每原型一份 |
| 本目录 `ExportModels.gd` | 导出器读的三张表：`src_root()` / `out_root()` / `models()`（2026-09-26 自导出器内建表搬来） | 每原型一份 |
| 本目录 `Uids.cs` | 20 个源场景 uid 表（一次登记完） | 每原型一份 |
| 本目录 `GenPreview.cs` | 摆哪些件、机位在哪 | 每原型一份 |
| 本目录 `Check.cs` | 扫描目录 + 占地上限 12 + 锚点名 `Anchor` + 容差 | 每原型一份（十几行） |

新原型要做造型 kit，照本目录这几个薄文件抄一份即可（注册入口 `<原型>Tools.cs` 的写法见
`src/GugaFramework/Tools/README.md`），通用层不复制。

## 单向流水线

```
改 Gen.cs  →  跑生成  →  export_glb.gd 导 .glb  →  Godot filesystem scan  →  校验器/看效果
```

**源场景由脚本改完重跑；不在 Godot 编辑器里打开并保存 `Tools/Rigs/*.tscn`**（生成物被 autosave 重排这条 → AGENTS.md 《验证操作手册》「运行前一致性同步」）。
每个源场景根节点带 `metadata/_generated` 标记（Inspector 底部可见）。标记走 metadata 一条路：
`.tscn` 不支持 `#` 注释，首行与行内两种放法都实测报 `Parse Error`。

为什么这条要写死：编辑器存盘会顺手改写文件格式——头行补 `uid=`、每个节点注入 `unique_id`、
省略等于默认值的字段。一次 Ctrl+S 就能把 914 行搅成大 diff，下次重跑又被冲回去，
diff 里分不清"谁改了造型"和"谁只是打开看了一眼"。真在编辑器里改了，重跑生成器即可回滚。

**踩过的现实版本**：`project_run` 带 `autosave: true`，会把**编辑器当前打开的场景**顺手存盘。
所以用 MCP 跑预览场景 = 每次都会脏两个生成物。对策：跑之前先 `scene_open` 一个非生成场景
（如 `Main.tscn`），或跑完 `dotnet run --project src/Tools -- preview InfinityLord . && dotnet run --project src/Tools -- check InfinityLord .` 收尾。
`Check.cs` 认得出这种脏（节点带 `unique_id` 或头行无 `load_steps`），会直接点名。

`uid` 也不再交给引擎分配：20 个 uid 写死在 `Uids.cs`，生成器按表输出，磁盘上出现表外的
uid 会打 stderr 警告。这样"想补 uid 只能开编辑器存一次盘"这个死结就没了（引擎侧已实测：
`ResourceUID.get_id_path()` 能把表里的 uid 解析回正确路径）。改表里的字符串 = 换资源身份，
当前全部按 `res://` 路径引用（`Main.cs` 只引用 `Models/*.glb`），所以安全。

## 命令

在项目根目录执行（统一入口 `src/Tools/`，第一个参数是原型名）：

```bash
dotnet run --project src/Tools -- gen InfinityLord .      # 17 份源场景
dotnet run --project src/Tools -- check InfinityLord .    # 离线自检，改完必跑
dotnet run --project src/Tools -- preview InfinityLord .  # 3 个预览场景（可选动作名：idle/walk/...）
# 导出 .glb（--proto 必填，导出器据此读本目录 ExportModels.gd）：
Godot_v4.7.2-stable_mono_win64_console.exe --headless --path . --quit-after 900 \
  --script res://src/GugaFramework/Tools/glb/export_glb.gd -- --proto=InfinityLord
```

导出之后、校验器之前的那条 `--headless --import`（补 `.glb.import` 伴随文件）与完整六步顺序，
见 `src/GugaFramework/Tools/README.md` 与《AI 能力手册》「造型线六步」。

三个命令都**幂等**：产物浮点统一 4 位定点（`Tscn.cs` 的 `Num()`，`AwayFromZero` 复刻 JS `toFixed`
语义）、`-0` 归零、绝对值小于 5e-4 归零，
所以重跑不产生噪声 diff。2026-09-23 工具从 Node 迁到 C# 后实测：重跑 17 份 + 3 个预览场景
哈希不变——这条性质是"能放心重跑"的前提，改 `Num()` 时别破坏它。

`Check.cs` 只剩十几行契约，检查逻辑在 `src/Tools/TscnCheck.cs`。它查的是引擎
**不报错、或报错不进日志**的那类问题，分两组：

- 格式：`ext_resource` 排序、`Transform3D` 分量个数、父节点是否先于子节点出现、`load_steps` 计数、
  是否被编辑器存过盘。
- 几何（沿父链累乘 basis 求每件的世界 AABB）：建筑占地是否压进 `MinPlaceDistance`、
  `Anchor.y` 是否高过剪影（名牌不被屋顶吃掉）、部件是否连成一块（**揪悬空件**）。
  最后一项是这台机器的主要价值：造型手摆的错位在编辑器里只是"看着怪"，AABB 一算就现形。

退出码非 0 表示有问题，可直接据此定过不过。第三个参数给单个场景时转储逐件包围盒，用来定位
悬空件飞去哪了：`dotnet run --project src/Tools -- check InfinityLord . src/Prototypes/InfinityLord/Tools/Rigs/Units/Troll.tscn`

**它是校验器不是橡皮图章**——2026-09-22 抽引擎时做过反向验证，三类规则都会真的红：
整座建筑抬高 3 米 → 报 3 组悬空件；篡改 `load_steps` → 报计数不符 + `format!=3`；
把 `Palace` 的 `Anchor.y` 从 11.0 压到 2.0 → 报「名牌锚点 2.00 低于剪影高 10.8」。

## 四条非显然契约

| 约定 | 含义 | 消费方 |
|---|---|---|
| 关节节点名以 `j_` 开头 | 骨架契约。`export_glb.gd` 按它建骨（剥掉前缀即骨名），`clips_humanoid.gd` 的 clip 也按骨名写轨道；图元挂在哪个 `j_` 下就蒙到哪根骨上 | `glb/export_glb.gd` |
| 根下 `Marker3D` 名为 `Anchor` | `.y` = 名牌高度，`.x` = 头顶血条宽度。血条与名牌不硬编码尺寸，巨魔不该用人类尺寸的细血条。`.y` 高过该件全部部件的 AABB 顶（`Check.cs` 会查；`Anchor` 两个分量各装什么 → AGENTS.md 《模型与动画准则》「三条固定项」）；导出时 Anchor 留在产物根上 | `Scripts/Main.cs` |
| 手里的东西从 `TipAt()` 派生 | 武器/盾牌/棒槌的握把必须落在 `TipAt(手臂枢轴, 臂长, 手臂旋转)` 上，不能按世界坐标手摆 | 只有 `Gen.cs` 自己 |
| 源场景**前脸朝 +Z** | 导出器在根下垫一个转 180° 的 `Rig`，所以产物前脸朝 -Z（产物朝向这条约定 → AGENTS.md 《模型与动画准则》「三条固定项」）；游戏侧朝向表按这条给（`BuildingYaw` 因此写 180/90 而不是 0） | `Scripts/Main.cs` |

第三条是**这次踩出来的**：`prim` 绕图元中心旋转，所以"抬手"之后手的位置会离开原来的世界坐标。
战士的剑、巨魔的棒槌和左手掌、三只地精的刀/棒/斧、守卫的矛原先全是世界坐标手摆，手臂一转就全体脱手。
配套助手：`tipAt`（握点）、`axisAt`（沿轴取端点）、`cyEnd`/`cnEnd`（按轴线一端定位的旋转柱/锥）、
`disc`（按中心+厚度取圆盘）。**别再写回绝对坐标。**

改前两条名字不会报错，只会**静默退化**到 `Main.cs` 里的硬编码默认值——表现不对但查不出原因。

颜色、体型、装备全烘在 `.glb` 里：`Main.cs` 不给模型换色、不缩放几何（旧的职业色 `t_` 染色区与
`gearScale = 1 + 0.08 × 装备` 已删，体型差异该做进模型）。唯一的运行时叠加是升级金光，
走 `ModelAnimator.Flash()` 的 `MaterialOverlay`，0.6 秒后撤掉，模型自带材质全程只读。

## 占地缩放 fit

`emit(dir, rootName, build, fit)` 的第 4 个参数按轴缩放整个预制件。X/Z 压进
`LogicWorld.MinPlaceDistance = 12` 的街区契约（占地上限这类契约数字的口径 → AGENTS.md 《模型与动画准则》「看图这一环到底抓到过什么」），Y 单独给，保高度剪影。

这条契约**现在可以离线判**：任意两座中心距 ≥ 12，只要各自最大宽 ≤ 12，半宽和就 ≤ 12，
不可能互相穿插——所以 `Check.cs` 里那行"占地是否超 12"就是完备校验器，不必靠截图猜。
种子布局（`LogicWorld.SeedWorld`）最紧的一对是宫殿 (-8,0) ↔ 行会 (0,0)，间距 8，
实测半宽和 3.90+3.64 = 7.54 < 8，余量 0.47。**截图只看密度好不好看，不用来判重叠。**

2026-09-22 按 AABB 重测并回调过一轮，当前实测（世界单位，宽×深）：

| 建筑 | 占地 | fit X/Z |
|---|---|---|
| Palace | 7.79×6.79 | 0.50 |
| GuildHall | 7.28×5.15 | 0.46 |
| GuildWarrior | 7.33×5.16 | 0.50 |
| Market | 6.50×4.95 | 0.52 |
| Tavern | 8.15×8.15 | 0.60 |
| Apothecary | 6.32×6.22 | 0.55 |
| MonsterLair | 11.7×10.0 | 0.76 |
| BossLair | 11.9×11.6 | 0.77 / 0.74 |

两座巢穴原先 12.2 与 17.1 是**超契约**的（旧数在"部件被转飞"那坑修好前调的），已压回；
BossLair 的土台顺手从 19×17 收到 15.5×14。改 fit 后仍建议跑一次 `Main.tscn` 看视觉密度。

## 已踩过的引擎坑（都实测过，写码前先扫一眼）

1. **Godot 4 的 `.tscn` 里 `Transform3D` 是「9 个基向量（行主序）+ 3 个原点」**，不是 Godot 3 的基/原点交错式。
   按交错写不报错，但原点被读进基向量、解出 ±90° 旋转与负缩放，画面表现为"部件全被转飞"。
2. **CSG 树只采用根节点上的 `material_override`**，逐子形状的材质被忽略。对策：材质挂到 `CSGCombiner3D`
   （一棵树一种材质），异色部件留在树外用普通图元叠。
3. **`metallic` 在本项目渲染成纯黑**（无反射环境/光照探针）。灰盒阶段只写 `roughness`。
4. **`CylinderMesh(radial_steps=3)` 压扁后退化成一条线**（想当三角旗面用），引擎不报错但看不见。
   改用"主幅 + 两片斜挂角"拼燕尾。
5. **`.tscn` 的 `ext_resource` 必须整体排在 `sub_resource` 之前**，否则 `Parse Error: Unknown tag 'ext_resource'`；
   该错误**不进 MCP `logs_read` 的任何缓冲区**，只有 `scene_open` 表现为 `switched:false / settle:timeout`。
   真报错要用 headless 命令行拿：
   `Godot_v4.7.2-stable_mono_win64_console.exe --headless --path . --quit-after 2 <scene>`
6. **`prim` 绕图元中心旋转**：底面锚定的 `bx/cy/cn/py` 配 `rx/rz` 时，旋转发生在"加了一半高度"之后，
   杆子的末端会离你标的底面点而去。要"末端钉在某点"就用 `cyEnd/cnEnd`（按轴线一端定位），
   要圆盘就用 `disc`（按中心+厚度）。
7. **fit 是三轴独立的**，所以带 `rx:90` 的圆盘其"竖直半径"取自 `F.z` 不是 `F.y`。
   按世界坐标心算接缝会算错约 30%——非要用 `Check.cs` 的 AABB 输出对。

## 验收分工：两件事分开，别互相冒充

| 验什么 | 在哪验 |
|---|---|
| 造型 / 剪影 / 配色 / 部件对位 | `_PreviewUnits.tscn`、`_PreviewBuildings.tscn`（自带灯光+相机，`project_run(mode:"current")` 直接跑） |
| 动作是否真在动（clip 让骨骼离开绑定姿势）、产物占地、野节点 | `glb/inspect_model.gd` 校验器（无头跑，不合格退出码 1） |
| fit 落地、`Anchor` 血条名牌、朝向、蒙皮与动画播放 | **只能跑 `Main.tscn`**——预览场景是纯 instance，这几条代码路径永远不触发 |

预览场景是验收脚手架，不进游戏运行时。截图时注意：远景小图会漏判部件对位问题，
拉近到单个单位/建筑再下结论。
