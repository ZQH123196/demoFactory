# Models/ — 巨兽竞技场的真模型资产

> 本文不写约束句（「约束唯一出处」）；约束句一律写成 契约名 指针，此处只留事实、机制与操作步骤。

这里放的是**模型文件**（`.glb`），不是场景、不是图元拼贴——真模型这条跨原型约束 → 「真模型」、「模型四件套」。
准则的机制与实测记录见 `docs/模型与动画准则.md`；造型线六步的命令原文在那一篇 15 节。

- 一个会动的单位 = 一个 `<Name>.glb`，里面带骨架（`Skeleton3D`）、蒙皮、动作 clip。
- 静态道具/建筑同样导出成 `.glb`（不需要骨架）。
- 游戏侧只 `GD.Load` 这里的文件，用 `GugaFramework/Animation/ModelAnimator` 喂状态；
  运行时改造型（拼身体 / 换材质染色 / 缩放几何）这几类做法 → 「造型进模型」 与《模型与动画准则》「允许 / 不允许」。
- 造型的"源"在本原型 `Tools/Rigs/` 的关节树场景里（`j_` 前缀 = 关节契约），
  用 `src/GugaFramework/Tools/glb/export_glb.gd` 导出到这里；源场景不进游戏运行时（源/产物这条 → 规则 《模型与动画准则》「标准管线」）。

## 本原型的造型分工

- 巨兽走四足骨架（`clips_beast.gd`）；地面人形六件（员工 / 角斗士 / 镇民 / 帮手 + 怪物 / 钻地怪）
  走同一副人形骨架（`clips_humanoid.gd`）：居民四件带八条
  （idle / walk / run / attack_melee / hit / death / work / carry），
  原生怪两件带七条（idle / walk / run / attack_melee / hit / death / emerge）。
- 巨兽有三形态三件模型（`Beast` / `BeastDesert` / `BeastGlutton`）：**同一副四足骨架、
  同一套动作名、同一档包围盒**（全长 89.6 × 体宽 52.9 × 总高约 30.4 米，三件彼此差 ≤ 0.5 米），
  差别做在配色与体量上。造型参数在 `Tools/Gen.cs` 的形态表里。
  T10 起另有二阶三件（`BeastTier2` / `BeastDesertTier2` / `BeastGluttonTier2`）：同骨架同动作组，
  八条 clip（idle / walk / run / eat / lie_down / get_up / form_shift / advance）六件一字不差；
  位阶差在剪影（bulk 上一档、背峰更高或多一峰、背上一块鞍板），包围盒**刻意**比一阶大一档。
  一阶甲板 48 米见方，二阶甲板 56 米见方（28×28 格），底座各一阶 / 二阶一套，四层关系两阶同形。
- 身份差异做进模型本身（比例、材质、随身那一套装备），**不在表现层换皮或缩放几何**
  （「造型进模型」）。早先那版是四种身份共用 `Worker.glb` 一个文件，2026-10-14 拆成五件。
- 其余各件（十三件建筑、七件静态场景件与点位物、磁悬浮底座）是静态资产：不挂骨架、不带动作。
- **背上小镇不烘进巨兽那个模型文件**：占地与「比例尺同框」那两栏会失真。
- 竞技场与工坊 2026-10-14 缩聚重生成过（7.2 → 3.6 米 / 7.2 → 2.6 米）：旧件是按 2.0 米
  格长做的，新网格格长 1.0 米。十三件建筑的底座上限按档位给死：4×4 ≤ 3.8 / 3×3 ≤ 2.8 /
  2×2 ≤ 1.8 米（清单出自 `spec「二十七个真模型清单」`）。实测占地读体检单那张表。
- 过渡期那六件不进 MVP 清单的旧件（`ArrowTower` / `SlowTrap` / `Breach` /
  `Quarry` / `Farm` / `Market`）在 2026-10-15 随切片 1/2 的旧码一起退休：`.glb`、`.glb.import`、
  `Tools/Rigs/Town/*.tscn` 与四张工具表里的条目一并删掉；`Dung` 随后也退了。
  这个目录现在恰好是体检单那七十件（含 2026-10-20 加的巨兽另两形态 `BeastDesert` / `BeastGlutton`、
  T12 加的第二种原生怪 `MonsterBurrower`、T11 加的受损态 / 脚手架 / 废墟 / 搬运小件 23 件、
  T10 加的二阶三形态与二阶底座甲板 5 件、T14 加的世界与地下静态件 12 件）。
  逐件清单以 `Tools/BeastArenaTools.cs` 的 `Review().Models` 为准）。

## 本原型的资产管线

材质库（六类「材质名 → 用途」表）、共享骨架与复用判据、四样齐备的导入检查、
一条命令体检 `verify`、新增模型的操作步骤——全部在 `docs/prototypes/beast-arena/asset-pipeline.md`。

## 出完模型后

一条命令看结论（离线几何校验 + 引擎回读校验）：

```bash
dotnet run --project src/Tools -- verify BeastArena .
```

体检单（本原型固定那一格审阅工位，十视图 + 动画胶卷 + 比例尺同框）走 `Tools/BeastArenaTools.cs`
里那份 `ReviewContract`，命令原文见 `docs/《模型与动画准则》「造型线六步」 与 `docs/prototypes/beast-arena/asset-pipeline.md`。
占地上限按巨兽实际尺寸填在 `ReviewContract.MaxFootprint`。
