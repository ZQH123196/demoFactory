# Models/ — 本原型的真模型资产

> 本文不写约束句（「约束唯一出处」）；约束句一律写成 契约名 指针，此处只留事实、机制与操作步骤。

这里放的是**模型文件**（`.glb`），不是场景、不是图元拼贴——真模型这条跨原型约束 → AGENTS.md 「真模型」、「模型四件套」。
准则的机制与实测记录见 `docs/模型与动画准则.md` 1~3 节。

- 一个会动的单位 = 一个 `<Name>.glb`，里面带骨架（`Skeleton3D`）、蒙皮、动作 clip。
- 静态道具/建筑同样导出成 `.glb`（不需要骨架）。
- 游戏侧只 `GD.Load` 这里的文件，用 `GugaFramework/Animation/ModelAnimator` 喂状态；
  运行时改造型（拼身体 / 换材质染色 / 缩放几何）这几类做法 → AGENTS.md 「造型进模型」 与《模型与动画准则》「允许 / 不允许」。
- 造型的"源"在本原型 `Tools/` 的关节树场景里（`j_` 前缀 = 关节契约），
  用 `src/GugaFramework/Tools/glb/export_glb.gd` 导出到这里；源场景不进游戏运行时（源/产物这条 → 《模型与动画准则》「标准管线」）。

自检（跑完导出后一条命令看骨架与动作）：

```bash
Godot..._console.exe --headless --path . --quit-after 60 \
  --script res://src/GugaFramework/Tools/glb/inspect_model.gd -- --clip=walk
```
