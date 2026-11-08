# VFX 特效资产是一份独立 `.tscn`，确定性钉在节点级

`src/GugaFramework/Vfx/` 这一层的形态：每个特效是 `src/Shared/vfx/`（公共批）或
`src/Prototypes/<原型>/Vfx/`（原型专属批）下一份独立 `.tscn`，按 `res://` 引用；
粒子开 `use_fixed_seed`，`seed` 从逻辑种子派生，钉在**节点级**而非 cue 级。
这一层与 `ModelAnimator`（骨骼动作）平级，原型只报事实，翻译层决定怎么演。

## Considered Options

- **效果资源做成 `.tres`**：`.tres` 装得下 `ParticleProcessMaterial` 一样，但一个粒子特效是四样——
  发射器节点（`amount` / `lifetime` / `one_shot` / `draw_pass_1`）、过程材质、绘制网格、绘制材质。
  节点那几项存不成 `.tres`，落到「过程材质走资源、其余写死在 C#」的半路，两头都不完整。
- **全部代码里配**（`new ParticleProcessMaterial()`）：改 `amount` / `lifetime` 要改代码重编，
  与本仓「场景由 `src/Tools` 生成、磁盘是唯一真值」的既有做法相拧。
- **效果资源做成 `.tscn`**：Godot 没有单文件特效资源，`.tscn` 就是它对 prefab / Niagara System
  那一类业内既有形状的对应位置。全仓已有一份先例可照样式：`src/Shared/ui/ProjectTheme.tres`
  （按 `res://` 引用，见 `I18n/FontSetup.cs:32`）。
- **确定性钉在 cue 级**（`seed = f(逻辑种子, cue 的 Tick, 轨名)`，每次触发派生新 seed）：
  既可复现又每次不同，但那个派生函数本身就是一份新逻辑，等于在表现层造了一份「随机」的真值，
  与「表现层只读」相拧。
- **确定性钉在节点级**（开 `use_fixed_seed`，`seed` 取自逻辑种子）：同一次运行里同一效果每次触发
  长得一样，画面可复现，派生只做一层。表现层不持有随机源。

Godot 官方文档点明 `use_fixed_seed` 的用途正是「重播时视觉结果一致，例如 Movie Maker 模式」
（`GPUParticles3D.use_fixed_seed`），所以这是顺引擎的用法，不是自造机制。

## Consequences

- 一次齐射几十条粒子不建几十个节点：一次性走 `finished` 信号 + 对象池，借出时 `restart()`
  （`one_shot` 时 `emitting = true` 不会重启周期，GPU 侧另有延迟，官方文档点名要调 `restart()`）。
- 持续档（护盾、治疗光）走另一条路：挂上时借节点、每帧同步位置、撤下即硬切。池容量按
  `CueFeedConfig.PendingPerTrack` 推导，不另设参数。撤下不淡出是刻意的——淡出会让占用时间变长，
  容量推导要另算，而淡出是纯观感问题，垂直切片阶段再补不迟。
- 池空时丢弃并计数（读数念「演 N｜借不出 M｜在池 K/T」），与 `CueFeed` 丢掉 cue 时记 `Dropped`
  是同一个道理（`Abilities/AbilityCues.cs:123-129`）：没演这件事必须读得出来。
- `GameSpeed` 写进每个粒子的 `speed_scale`，与 `ModelAnimator.GameSpeed` 同口径
  （`Animation/ModelAnimator.cs:95-106`）。否则高倍快进下粒子按实时演完，画面与逻辑脱节。
- 效果资源是 `.tscn`，它引用的 `.tres`（过程材质、绘制材质）会被 Godot 导入器生成 `.import`
  伴随文件，这些必须入库，否则干净机器上场景加载失败（契约「伴随文件入库」）。
- 本轮**不做** LOD、**不做**混合模式白名单。业界把 overdraw 列为 VFX 头号敌人，但本仓明写
  「可调试性第一，功能性第二，性能第三」，功能成立之前不上优化。先只加一条存活粒子数读数，
  等垂直切片真跑起来看到数字再谈（`SkillCues.cs:46` 的 `MaxFloats = 24` 是硬编码在代码里的先例，
  粒子这类更容易雪崩的效果不宜照抄；池容量改走 `[Export]`，现场能调也是「压测必恢复」的前提）。