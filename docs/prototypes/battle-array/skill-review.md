# 技能体检单 — BattleArray

> 由 `dotnet run --project src/Tools -- skill BattleArray .` 生成，**勿手改**。
> 数字不抄第二份：这一页是技能表的**可读投影**，投影与表对不上由 `dotnet test` 那条对表检查判红。
> 检查清单出处 `docs/research/技能系统/调研结论摘要.md`「设计骨架」节。
> 阈值 0.05 秒（半个步长）归公共层；判红是**单向**的 —— 只咬「动画比前摇短」，动画比前摇长只把超出量印出来
> （演多久归表现层，见 AGENTS.md「游戏开发的各阶段」）。报告不含时间戳与绝对路径，重跑逐字全等。

## 校验器

- **前摇对表**（参与退出码）：16 条技能 —— 0 红 · 4 条动画比前摇长（不判红）· 12 条**没有动画载体**（表里没挂号，校验器不判、这里念得出）。<br>真值只有技能表那一份（不许运行时问播放器），clip 名义时长是另一本账。**短**的那一侧判红（逻辑还在蓄力、画面已经演完，读数在骗人）；**长**的那一侧只入账（演到 commit 之后是合法表现层选择）。这一栏钉的是「同一个数写两遍然后漂」。
- **出手节奏对表**（**不**参与退出码）：8 行里 2 行动画比节奏长 —— 这一栏读的是平A，动作库是人共有的一份（改它要重导两个原型的模型），「看起来像不像一回事」属活人感阶段（AGENTS.md「游戏开发的各阶段」），所以只入账、不判红。
- clip 名义时长来自 8 个模型的 `.glb`（无头读回，与模型体检单用的同一份实现）。

## 栏 1 · 数值台账（每条一行，全按 0.1 秒一步换算）

数字取自这些表（报告只投影、不另存一份）：H2 单挑场注册壳那张表（<c>SkillDuelRift</c>） · H3 宿主侧那张表（<c>BattleAbilities</c>）。

| 技能 | 兵种 | 前摇 | commit | 结算落在 | 冷却 | 射程 | 消耗 | 充能 | 效果（时长/周期/延迟/层） |
|---|---|---|---|---|---|---|---|---|---|
| `tank.guard` | 坦克 | 无前摇 | 第 0 步 | commit 当步 | 20 步｜2.0 秒 | 0 米 | 无 | 不设激活检查 | fx.guard_wall Duration 时长 50 步｜5.0 秒 ｜ damage_taken Override 0.5 move_speed Mult 0.7 ｜ 授予 state.guard |
| `tank.dread` | 坦克 | 无前摇 | 第 0 步 | commit 当步 | 没有冷却 | 4 米 | 无 | 不设激活检查 | fx.dread_aura Infinite ｜ attack Mult 0.85 |
| `tank.slam` | 坦克 | 2 步｜0.2 秒 | 第 2 步 | commit 当步 | 20 步｜2.0 秒 | 6 米 | 无 | 不设激活检查 | （技能自己不带效果） |
| `tank.steady` | 坦克 | 无前摇 | 第 0 步 | commit 当步 | 没有冷却 | 0 米 | 无 | 不设激活检查 | fx.steady_charges Infinite 每 60 步 叠层 1（每层 0） ｜ 授予 status.block_ready |
| `warrior.rift` | 战士 | 3 步｜0.3 秒 | 第 3 步 | commit 当步 | 20 步｜2.0 秒 | 3 米 | 无 | 不设激活检查 | fx.rift_slash Instant ｜ hp Add（走效果的 Calc） |
| `warrior.execute` | 战士 | 无前摇 | 第 0 步 | commit 当步 | 没有冷却 | 3 米 | rage 30 | 不设激活检查 | fx.execute_guillotine Instant ｜ hp Add（走效果的 Calc） |
| `warrior.bleed` | 战士 | 无前摇 | 第 0 步 | commit 当步 | 没有冷却 | 3 米 | 无 | 不设激活检查 | fx.bleed_wound Duration 时长 60 步｜6.0 秒 每 5 步 叠层 3（每层 -2） ｜ hp Add（走效果的 Calc） |
| `warrior.rage` | 战士 | 无前摇 | 第 0 步 | commit 当步 | 没有冷却 | 0 米 | 无 | 不设激活检查 | fx.rage_on_hit Infinite ｜ 触发 damage_taken→fx.rage_gain_8 |
| `archer.pierce` | 弓手 | 2 步｜0.2 秒 | 第 2 步 | +6 步 | 10 步｜1.0 秒 | 30 米 | 无 | 不设激活检查 | fx.pierce_arrow Instant 延迟 6 步 曲线 distance ｜ hp Add（走效果的 Calc） |
| `archer.rain` | 弓手 | 无前摇 | 第 0 步 | +4 步 / +6 步 / +8 步 | 没有冷却 | 30 米 | 无 | 不设激活检查 | fx.rain_near Instant 延迟 4 步 曲线 distance ｜ hp Add（走效果的 Calc）<br>fx.rain_mid Instant 延迟 6 步 曲线 distance ｜ hp Add（走效果的 Calc）<br>fx.rain_far Instant 延迟 8 步 曲线 distance ｜ hp Add（走效果的 Calc） |
| `archer.mark` | 弓手 | 无前摇 | 第 0 步 | commit 当步 | 没有冷却 | 30 米 | 无 | 不设激活检查 | fx.mark_seal Duration 时长 80 步｜8.0 秒 ｜ cooldown.tank.guard Mult 1.3 ｜ 授予 status.marked |
| `archer.evasive` | 弓手 | 无前摇 | 第 0 步 | commit 当步 | 没有冷却 | 0 米 | 无 | 不设激活检查 | fx.evasive_speed Infinite ｜ move_speed Mult 1.8 |
| `rider.charge` | 骑手 | 无前摇 | 第 0 步 | +3 步 | 20 步｜2.0 秒 | 38 米 | 无 | 不设激活检查 | fx.charge_impact Instant 延迟 3 步 曲线 travelled ｜ hp Add（走效果的 Calc） |
| `rider.trample` | 骑手 | 2 步｜0.2 秒 | 第 2 步 | commit 当步 | 没有冷却 | 3 米 | 无 | 不设激活检查 | fx.trample_root Duration 时长 20 步｜2.0 秒 ｜ 授予 status.rooted |
| `rider.joust` | 骑手 | 无前摇 | 第 0 步 | +3 步 / +7 步 | 没有冷却 | 10 米 | 无 | 不设激活检查 | fx.joust_first Instant 延迟 3 步 ｜ hp Add（走效果的 Calc）<br>fx.joust_second Instant 延迟 7 步 ｜ hp Add（走效果的 Calc） ｜ 打断 state.guard |
| `rider.mounted` | 骑手 | 无前摇 | 第 0 步 | commit 当步 | 没有冷却 | 0 米 | 无 | 不设激活检查 | fx.mounted_tag Infinite ｜ 授予 status.mounted |

## 栏 2 · 时序胶卷（按逻辑步，不按渲染帧；每格 1 步 = 0.1 秒）

| 技能 | 前摇（步｜秒） | 载体 clip | clip 名义（秒） | 差（clip − 前摇，秒） | 判 |
|---|---|---|---|---|---|
| `tank.guard` | 0｜0.0 | （没有动画载体） | — | — | 不适用：没有动画载体 |
| `tank.dread` | 0｜0.0 | （没有动画载体） | — | — | 不适用：没有动画载体 |
| `tank.slam` | 2｜0.2 | HumanTank·windup_slam | 0.6 | +0.4 | 动画比前摇长（演到 commit 之后）· 不判红 |
| `tank.steady` | 0｜0.0 | （没有动画载体） | — | — | 不适用：没有动画载体 |
| `warrior.rift` | 3｜0.3 | HumanWarrior·windup_slash | 0.7 | +0.4 | 动画比前摇长（演到 commit 之后）· 不判红 |
| `warrior.execute` | 0｜0.0 | （没有动画载体） | — | — | 不适用：没有动画载体 |
| `warrior.bleed` | 0｜0.0 | （没有动画载体） | — | — | 不适用：没有动画载体 |
| `warrior.rage` | 0｜0.0 | （没有动画载体） | — | — | 不适用：没有动画载体 |
| `archer.pierce` | 2｜0.2 | HumanArcher·windup_pierce | 0.6 | +0.4 | 动画比前摇长（演到 commit 之后）· 不判红 |
| `archer.rain` | 0｜0.0 | （没有动画载体） | — | — | 不适用：没有动画载体 |
| `archer.mark` | 0｜0.0 | （没有动画载体） | — | — | 不适用：没有动画载体 |
| `archer.evasive` | 0｜0.0 | （没有动画载体） | — | — | 不适用：没有动画载体 |
| `rider.charge` | 0｜0.0 | （没有动画载体） | — | — | 不适用：没有动画载体 |
| `rider.trample` | 2｜0.2 | HumanRider·windup_trample | 0.7 | +0.5 | 动画比前摇长（演到 commit 之后）· 不判红 |
| `rider.joust` | 0｜0.0 | （没有动画载体） | — | — | 不适用：没有动画载体 |
| `rider.mounted` | 0｜0.0 | （没有动画载体） | — | — | 不适用：没有动画载体 |

### 每条技能的里程碑与刻度尺

**`archer.evasive`**

```
按下(0) → 前摇 0 步 → commit(0) → 结算(0)
+-----------
  a=按下 c=commit h=结算 x=效果到期 r=冷却解除（末格 12 步封顶）
```

**`archer.mark`**

```
按下(0) → 前摇 0 步 → commit(0) → 结算(0) → fx.mark_seal 到期(80)
+-----------------------------------------------------------
  a=按下 c=commit h=结算 x=效果到期 r=冷却解除（末格 60 步封顶）
```

**`archer.pierce`**

```
按下(0) → 前摇 2 步 → commit(2) → 结算(8) → 冷却解除(12)
a-c-----h---
  a=按下 c=commit h=结算 x=效果到期 r=冷却解除（末格 12 步封顶）
```

**`archer.rain`**

```
按下(0) → 前摇 0 步 → commit(0) → 结算(8)
+-------h---
  a=按下 c=commit h=结算 x=效果到期 r=冷却解除（末格 12 步封顶）
```

**`rider.charge`**

```
按下(0) → 前摇 0 步 → commit(0) → 结算(3) → 冷却解除(20)
+--h----------------
  a=按下 c=commit h=结算 x=效果到期 r=冷却解除（末格 20 步封顶）
```

**`rider.joust`**

```
按下(0) → 前摇 0 步 → commit(0) → 结算(7)
+------h----
  a=按下 c=commit h=结算 x=效果到期 r=冷却解除（末格 12 步封顶）
```

**`rider.mounted`**

```
按下(0) → 前摇 0 步 → commit(0) → 结算(0)
+-----------
  a=按下 c=commit h=结算 x=效果到期 r=冷却解除（末格 12 步封顶）
```

**`rider.trample`**

```
按下(0) → 前摇 2 步 → commit(2) → 结算(2) → fx.trample_root 到期(22)
a-+-------------------
  a=按下 c=commit h=结算 x=效果到期 r=冷却解除（末格 22 步封顶）
```

**`tank.dread`**

```
按下(0) → 前摇 0 步 → commit(0) → 结算(0)
+-----------
  a=按下 c=commit h=结算 x=效果到期 r=冷却解除（末格 12 步封顶）
```

**`tank.guard`**

```
按下(0) → 前摇 0 步 → commit(0) → 结算(0) → fx.guard_wall 到期(50) → 冷却解除(20)
+-------------------r-----------------------------
  a=按下 c=commit h=结算 x=效果到期 r=冷却解除（末格 50 步封顶）
```

**`tank.slam`**

```
按下(0) → 前摇 2 步 → commit(2) → 结算(2) → 冷却解除(22)
a-+-------------------
  a=按下 c=commit h=结算 x=效果到期 r=冷却解除（末格 22 步封顶）
```

**`tank.steady`**

```
按下(0) → 前摇 0 步 → commit(0) → 结算(0)
+-----------
  a=按下 c=commit h=结算 x=效果到期 r=冷却解除（末格 12 步封顶）
```

**`warrior.bleed`**

```
按下(0) → 前摇 0 步 → commit(0) → 结算(0) → fx.bleed_wound 到期(60)
+-----------------------------------------------------------
  a=按下 c=commit h=结算 x=效果到期 r=冷却解除（末格 60 步封顶）
```

**`warrior.execute`**

```
按下(0) → 前摇 0 步 → commit(0) → 结算(0)
+-----------
  a=按下 c=commit h=结算 x=效果到期 r=冷却解除（末格 12 步封顶）
```

**`warrior.rage`**

```
按下(0) → 前摇 0 步 → commit(0) → 结算(0)
+-----------
  a=按下 c=commit h=结算 x=效果到期 r=冷却解除（末格 12 步封顶）
```

**`warrior.rift`**

```
按下(0) → 前摇 3 步 → commit(3) → 结算(3) → 冷却解除(23)
a--+-------------------
  a=按下 c=commit h=结算 x=效果到期 r=冷却解除（末格 23 步封顶）
```

### 出手节奏（平A 的间隔 vs 动画长度）

| 档位 | 模型 | clip | 间隔（步｜秒） | 动画名义（秒） | 超出（秒） | 读数 |
|---|---|---|---|---|---|---|
| Human·盾卫 | HumanTank | attack_melee | 14｜1.4 | 1.0 | 0 | 演得完 |
| Human·剑士 | HumanWarrior | attack_melee | 10｜1.0 | 1.0 | 0 | 演得完 |
| Human·弓手 | HumanArcher | attack_shoot | 12｜1.2 | 1.5 | 0.3 | 动画盖不住（观感账） |
| Human·骑兵 | HumanRider | attack_melee | 11｜1.1 | 1.0 | 0 | 演得完 |
| Orc·蛮牛 | OrcTank | attack_melee | 14｜1.4 | 1.0 | 0 | 演得完 |
| Orc·劫掠者 | OrcWarrior | attack_melee | 10｜1.0 | 1.0 | 0 | 演得完 |
| Orc·投斧手 | OrcArcher | attack_shoot | 12｜1.2 | 1.5 | 0.3 | 动画盖不住（观感账） |
| Orc·狼骑 | OrcRider | attack_melee | 11｜1.1 | 1.0 | 0 | 演得完 |

## 栏 3 · 对照矩阵（这一条顶住哪一格、谁钉着它）

| 技能 | 跑在哪个宿主 | 顶住的 GAS 功能面行 | 钉它的断言 | 前摇载体 | 内容侧写明 |
|---|---|---|---|---|---|
| `tank.guard` | H2 + H3（四类 cue 上屏 A71~A75） | 同段两笔 modifier（排序键）·Override 撞界·Duration·tag 授予·入口激活检查 | A55 A56 A57 | 未挂号 | — |
| `tank.dread` | H2 | 光环按距离反复重新应用与撤销 | A37 A38 | 未挂号 | — |
| `tank.slam` | H3（形状查询在真二维站位上） | 显式打断·形状查询不用物理（距离 + 角度）·减速类 modifier | A39 A64 A65 | windup_slam | 技能自己不带效果：命中那一发由宿主在 commit 那一刻按二维站位发 <c>ApplyEffect</c>（与 N-13 同一种形状），栏 1 里那条 30 步减速就是它｜起手动作两族各一件（HumanTank 与 OrcTank，同库同时长），这一格报第一件作代表 |
| `tank.steady` | H2 | tag 计数被消耗（一次性抵挡）·计数型资源·周期回充 | A31 A32 A60 | 未挂号 | — |
| `warrior.rift` | H2 + H3（级 1 与级 2 逐格全等） | CommitAbility 提交点·Calc 链·冷却与返还口径·排队释放 | A34 A35 A36 A41 | windup_slash | 起手动作两族各一件（HumanWarrior 与 OrcWarrior，同库同时长），这一格报第一件作代表 |
| `warrior.execute` | H2 | Cost 与来源属性快照·谓词激活检查·按目标已损失生命计算 | A58 | 未挂号 | — |
| `warrior.bleed` | H2 | GE 堆叠策略三列·period 频率·Duration ≠ period 的正证 | A59 A60 | 未挂号 | — |
| `warrior.rage` | H2 | GameplayEvent 触发效果·资源型属性与上限的界 | A40 | 未挂号 | — |
| `archer.pierce` | H2 + H3（延迟结算那条指示线） | 投射物＝延迟结算·目标按 Id 落地现解·距离曲线·多项 Calc 链 | A42 A43 | windup_pierce | 起手动作两族各一件（HumanArcher 与 OrcArcher，同库同时长），这一格报第一件作代表 |
| `archer.rain` | H2 | 同一次激活多条 Pending 互不顶掉·错峰到期·打空也读得出为什么 | A61 | 未挂号 | — |
| `archer.mark` | H2 | tag 计数进计算·冷却时长被外部修改（attribute 口 N-7） | A62 | 未挂号 | — |
| `archer.evasive` | H2 | 被动常驻 modifier·Override 与 Min/Max 的先后（Meta 口径） | A44 | 未挂号 | — |
| `rider.charge` | H3（位置第一次成为技能的输出） | 位移载荷 Pending.Move·已行走距离曲线·到位与伤害同一步 | A66 A67 | 未挂号 | — |
| `rider.trample` | H2 | 控制链·激活检查的第二处检查（S5 走位前再问一次） | A45 | windup_trample | 起手动作两族各一件（HumanRider 与 OrcRider，同库同时长），这一格报第一件作代表 |
| `rider.joust` | H2 | 多段结算·后段带 CancelMatchingTags 的打断（延迟落地也走同一队列） | A63 | 未挂号 | — |
| `rider.mounted` | H2 | modifier 自带条件列（RequirementTags，不满足 ≠ 撤销） | A46 | 未挂号 | — |

## 怎么读这一页

- 「没有动画载体」不是通过，也不是失败：它说的是这条技能的前摇在模型里没有对应的那段动作，画面要靠 cue 交代。谁给这条技能做起手动作，校验器当场开始咬人 —— **只咬一侧**：动作比前摇短（差超过 0.05 秒）就红；动作比前摇长不判红，超出量带正号印在「差」那一列。
- 「结算落在」= commit 之后再过效果的延迟步数；飞行物、位移都在这格里，与快照里 `p;` 那行的到期步同一个口径。
- 「钉它的断言」指测试方法名里的编号，报告里出现的每个编号都必须真找得到（对表检查的第二条腿）。
