# 模型体检单 — InfinityLord

> 由 `dotnet run --project src/Tools -- review InfinityLord .` 生成，**勿手改**。
> 怎么算合格出自 `docs/《模型与动画准则》「验收清单」；PASS/FAIL 由引擎侧校验器算（一次计算两处用），
> 本文件只是它的可读投影。图不提交进版本库（`.cache/` 整条被 gitignore），这里只留可比对的数字。
› 规格：栏 1 十视图（前后左右 + 四角 45° + 正俯视 + 等距 45°，一律绑定姿势）；栏 2 胶卷格距 0.2 秒（= 逻辑 2 步）封顶 8 格；栏 3 比例尺同框 + 1 米方格网。不含时间戳，重跑逐字全等。

- 出图 640x480，窗口挪到 6000,6000（屏外），引擎 4.7.2-stable (official)
- 占地上限 ≤ 12 米；动作契约 idle / walk / run / death

## 汇总

17 个模型 · 17 PASS · 0 FAIL

## 逐模型

| 模型 | 骨 | 占地 X×Z | 高 | 底 y | 面 | 血条宽 @ 名牌高 | 最大骨偏移/米（每条 clip） | 结论 |
|---|---|---|---|---|---|---|---|---|
| `Apothecary` | 0 | 6.33×6.23 | 7.16 | -0.03 | 8 | 血条 0.00 米 @ 7.31 米 | — | PASS |
| `BossGuard` | 18 | 1.11×0.85 | 2.12 | -0.00 | 7 | 血条 1.29 米 @ 2.35 米 | attack_melee 0.502 death 0.327 idle 0.011 run 0.751 walk 0.343 | PASS |
| `BossLair` | 0 | 11.94×11.60 | 16.19 | 0.00 | 6 | 血条 0.00 米 @ 16.66 米 | — | PASS |
| `BountyFlag` | 0 | 2.72×1.82 | 5.04 | 0.00 | 5 | 血条 0.00 米 @ 5.90 米 | — | PASS |
| `Goblin` | 18 | 0.47×0.47 | 1.02 | -0.00 | 5 | 血条 0.66 米 @ 1.21 米 | attack_melee 0.339 death 0.203 idle 0.007 run 0.512 walk 0.217 | PASS |
| `GoblinBrute` | 18 | 1.16×0.78 | 1.61 | -0.01 | 7 | 血条 1.39 米 @ 1.92 米 | attack_melee 0.647 death 0.300 idle 0.015 run 0.912 walk 0.392 | PASS |
| `GoblinElite` | 18 | 0.66×0.52 | 1.36 | -0.00 | 6 | 血条 0.80 米 @ 1.51 米 | attack_melee 0.422 death 0.232 idle 0.009 run 0.628 walk 0.268 | PASS |
| `GuildHall` | 0 | 7.28×5.17 | 6.04 | 0.00 | 9 | 血条 0.00 米 @ 6.08 米 | — | PASS |
| `GuildWarrior` | 0 | 7.33×5.16 | 5.70 | 0.00 | 8 | 血条 0.00 米 @ 5.95 米 | — | PASS |
| `Knight` | 18 | 1.49×0.71 | 2.50 | -0.00 | 6 | 血条 1.46 米 @ 2.65 米 | attack_melee 0.560 death 0.356 idle 0.012 run 0.822 walk 0.375 | PASS |
| `Market` | 0 | 6.50×4.95 | 6.82 | 0.00 | 10 | 血条 0.00 米 @ 7.22 米 | — | PASS |
| `MonsterLair` | 0 | 11.65×9.50 | 5.59 | -0.47 | 6 | 血条 0.00 米 @ 5.76 米 | — | PASS |
| `Palace` | 0 | 7.79×6.79 | 10.76 | 0.00 | 8 | 血条 0.00 米 @ 11.02 米 | — | PASS |
| `Ranger` | 18 | 0.64×0.81 | 1.87 | -0.00 | 6 | 血条 1.10 米 @ 1.91 米 | attack_shoot 0.569 death 0.304 idle 0.009 run 0.693 walk 0.316 | PASS |
| `Tavern` | 0 | 8.21×8.21 | 11.69 | 0.00 | 10 | 血条 0.00 米 @ 12.07 米 | — | PASS |
| `Troll` | 18 | 3.27×2.36 | 3.50 | -0.03 | 6 | 血条 3.20 米 @ 3.70 米 | attack_melee 1.088 death 0.588 idle 0.025 run 1.400 walk 0.613 | PASS |
| `Warrior` | 18 | 1.75×0.56 | 1.93 | -0.00 | 8 | 血条 1.18 米 @ 2.05 米 | attack_melee 0.478 death 0.313 idle 0.010 run 0.719 walk 0.328 | PASS |

## 胶卷采样时刻（秒；格距≠默认值即本条升过档，`once` = 引擎读到的这条动作不循环）

| 模型 | clip | 长度 | 格距 | 格数 | 循环 | 采样时刻 |
|---|---|---|---|---|---|---|
| `BossGuard` | attack_melee | 1.00 | 0.20 | 6 | once | 0.00 0.20 0.40 0.60 0.80 1.00 |
| `BossGuard` | death | 1.50 | 0.40 | 5 | once | 0.00 0.40 0.80 1.20 1.50 |
| `BossGuard` | idle | 3.20 | 0.40 | 8 | loop | 0.00 0.40 0.80 1.20 1.60 2.00 2.40 2.80 |
| `BossGuard` | run | 0.62 | 0.20 | 4 | loop | 0.00 0.20 0.40 0.60 |
| `BossGuard` | walk | 0.92 | 0.20 | 5 | loop | 0.00 0.20 0.40 0.60 0.80 |
| `Goblin` | attack_melee | 1.00 | 0.20 | 6 | once | 0.00 0.20 0.40 0.60 0.80 1.00 |
| `Goblin` | death | 1.50 | 0.40 | 5 | once | 0.00 0.40 0.80 1.20 1.50 |
| `Goblin` | idle | 3.20 | 0.40 | 8 | loop | 0.00 0.40 0.80 1.20 1.60 2.00 2.40 2.80 |
| `Goblin` | run | 0.62 | 0.20 | 4 | loop | 0.00 0.20 0.40 0.60 |
| `Goblin` | walk | 0.92 | 0.20 | 5 | loop | 0.00 0.20 0.40 0.60 0.80 |
| `GoblinBrute` | attack_melee | 1.00 | 0.20 | 6 | once | 0.00 0.20 0.40 0.60 0.80 1.00 |
| `GoblinBrute` | death | 1.50 | 0.40 | 5 | once | 0.00 0.40 0.80 1.20 1.50 |
| `GoblinBrute` | idle | 3.20 | 0.40 | 8 | loop | 0.00 0.40 0.80 1.20 1.60 2.00 2.40 2.80 |
| `GoblinBrute` | run | 0.62 | 0.20 | 4 | loop | 0.00 0.20 0.40 0.60 |
| `GoblinBrute` | walk | 0.92 | 0.20 | 5 | loop | 0.00 0.20 0.40 0.60 0.80 |
| `GoblinElite` | attack_melee | 1.00 | 0.20 | 6 | once | 0.00 0.20 0.40 0.60 0.80 1.00 |
| `GoblinElite` | death | 1.50 | 0.40 | 5 | once | 0.00 0.40 0.80 1.20 1.50 |
| `GoblinElite` | idle | 3.20 | 0.40 | 8 | loop | 0.00 0.40 0.80 1.20 1.60 2.00 2.40 2.80 |
| `GoblinElite` | run | 0.62 | 0.20 | 4 | loop | 0.00 0.20 0.40 0.60 |
| `GoblinElite` | walk | 0.92 | 0.20 | 5 | loop | 0.00 0.20 0.40 0.60 0.80 |
| `Knight` | attack_melee | 1.00 | 0.20 | 6 | once | 0.00 0.20 0.40 0.60 0.80 1.00 |
| `Knight` | death | 1.50 | 0.40 | 5 | once | 0.00 0.40 0.80 1.20 1.50 |
| `Knight` | idle | 3.20 | 0.40 | 8 | loop | 0.00 0.40 0.80 1.20 1.60 2.00 2.40 2.80 |
| `Knight` | run | 0.62 | 0.20 | 4 | loop | 0.00 0.20 0.40 0.60 |
| `Knight` | walk | 0.92 | 0.20 | 5 | loop | 0.00 0.20 0.40 0.60 0.80 |
| `Ranger` | attack_shoot | 1.50 | 0.40 | 5 | once | 0.00 0.40 0.80 1.20 1.50 |
| `Ranger` | death | 1.50 | 0.40 | 5 | once | 0.00 0.40 0.80 1.20 1.50 |
| `Ranger` | idle | 3.20 | 0.40 | 8 | loop | 0.00 0.40 0.80 1.20 1.60 2.00 2.40 2.80 |
| `Ranger` | run | 0.62 | 0.20 | 4 | loop | 0.00 0.20 0.40 0.60 |
| `Ranger` | walk | 0.92 | 0.20 | 5 | loop | 0.00 0.20 0.40 0.60 0.80 |
| `Troll` | attack_melee | 1.00 | 0.20 | 6 | once | 0.00 0.20 0.40 0.60 0.80 1.00 |
| `Troll` | death | 1.50 | 0.40 | 5 | once | 0.00 0.40 0.80 1.20 1.50 |
| `Troll` | idle | 3.20 | 0.40 | 8 | loop | 0.00 0.40 0.80 1.20 1.60 2.00 2.40 2.80 |
| `Troll` | run | 0.62 | 0.20 | 4 | loop | 0.00 0.20 0.40 0.60 |
| `Troll` | walk | 0.92 | 0.20 | 5 | loop | 0.00 0.20 0.40 0.60 0.80 |
| `Warrior` | attack_melee | 1.00 | 0.20 | 6 | once | 0.00 0.20 0.40 0.60 0.80 1.00 |
| `Warrior` | death | 1.50 | 0.40 | 5 | once | 0.00 0.40 0.80 1.20 1.50 |
| `Warrior` | idle | 3.20 | 0.40 | 8 | loop | 0.00 0.40 0.80 1.20 1.60 2.00 2.40 2.80 |
| `Warrior` | run | 0.62 | 0.20 | 4 | loop | 0.00 0.20 0.40 0.60 |
| `Warrior` | walk | 0.92 | 0.20 | 5 | loop | 0.00 0.20 0.40 0.60 0.80 |

## 产物清单（`.cache/Review/InfinityLord/` 下，本机可读）

- **Apothecary**：`Apothecary/contact.png` `Apothecary/views/01_front.png` `Apothecary/views/02_right.png` `Apothecary/views/03_back.png` `Apothecary/views/04_left.png` `Apothecary/views/05_front_right.png` `Apothecary/views/06_back_right.png` `Apothecary/views/07_back_left.png` `Apothecary/views/08_front_left.png` `Apothecary/views/09_top.png` `Apothecary/views/10_iso.png` `Apothecary/overlay.png`
- **BossGuard**：`BossGuard/contact.png` `BossGuard/views/01_front.png` `BossGuard/views/02_right.png` `BossGuard/views/03_back.png` `BossGuard/views/04_left.png` `BossGuard/views/05_front_right.png` `BossGuard/views/06_back_right.png` `BossGuard/views/07_back_left.png` `BossGuard/views/08_front_left.png` `BossGuard/views/09_top.png` `BossGuard/views/10_iso.png` `BossGuard/film/attack_melee.png` `BossGuard/film/death.png` `BossGuard/film/idle.png` `BossGuard/film/run.png` `BossGuard/film/walk.png` `BossGuard/overlay.png`
- **BossLair**：`BossLair/contact.png` `BossLair/views/01_front.png` `BossLair/views/02_right.png` `BossLair/views/03_back.png` `BossLair/views/04_left.png` `BossLair/views/05_front_right.png` `BossLair/views/06_back_right.png` `BossLair/views/07_back_left.png` `BossLair/views/08_front_left.png` `BossLair/views/09_top.png` `BossLair/views/10_iso.png` `BossLair/overlay.png`
- **BountyFlag**：`BountyFlag/contact.png` `BountyFlag/views/01_front.png` `BountyFlag/views/02_right.png` `BountyFlag/views/03_back.png` `BountyFlag/views/04_left.png` `BountyFlag/views/05_front_right.png` `BountyFlag/views/06_back_right.png` `BountyFlag/views/07_back_left.png` `BountyFlag/views/08_front_left.png` `BountyFlag/views/09_top.png` `BountyFlag/views/10_iso.png` `BountyFlag/overlay.png`
- **Goblin**：`Goblin/contact.png` `Goblin/views/01_front.png` `Goblin/views/02_right.png` `Goblin/views/03_back.png` `Goblin/views/04_left.png` `Goblin/views/05_front_right.png` `Goblin/views/06_back_right.png` `Goblin/views/07_back_left.png` `Goblin/views/08_front_left.png` `Goblin/views/09_top.png` `Goblin/views/10_iso.png` `Goblin/film/attack_melee.png` `Goblin/film/death.png` `Goblin/film/idle.png` `Goblin/film/run.png` `Goblin/film/walk.png` `Goblin/overlay.png`
- **GoblinBrute**：`GoblinBrute/contact.png` `GoblinBrute/views/01_front.png` `GoblinBrute/views/02_right.png` `GoblinBrute/views/03_back.png` `GoblinBrute/views/04_left.png` `GoblinBrute/views/05_front_right.png` `GoblinBrute/views/06_back_right.png` `GoblinBrute/views/07_back_left.png` `GoblinBrute/views/08_front_left.png` `GoblinBrute/views/09_top.png` `GoblinBrute/views/10_iso.png` `GoblinBrute/film/attack_melee.png` `GoblinBrute/film/death.png` `GoblinBrute/film/idle.png` `GoblinBrute/film/run.png` `GoblinBrute/film/walk.png` `GoblinBrute/overlay.png`
- **GoblinElite**：`GoblinElite/contact.png` `GoblinElite/views/01_front.png` `GoblinElite/views/02_right.png` `GoblinElite/views/03_back.png` `GoblinElite/views/04_left.png` `GoblinElite/views/05_front_right.png` `GoblinElite/views/06_back_right.png` `GoblinElite/views/07_back_left.png` `GoblinElite/views/08_front_left.png` `GoblinElite/views/09_top.png` `GoblinElite/views/10_iso.png` `GoblinElite/film/attack_melee.png` `GoblinElite/film/death.png` `GoblinElite/film/idle.png` `GoblinElite/film/run.png` `GoblinElite/film/walk.png` `GoblinElite/overlay.png`
- **GuildHall**：`GuildHall/contact.png` `GuildHall/views/01_front.png` `GuildHall/views/02_right.png` `GuildHall/views/03_back.png` `GuildHall/views/04_left.png` `GuildHall/views/05_front_right.png` `GuildHall/views/06_back_right.png` `GuildHall/views/07_back_left.png` `GuildHall/views/08_front_left.png` `GuildHall/views/09_top.png` `GuildHall/views/10_iso.png` `GuildHall/overlay.png`
- **GuildWarrior**：`GuildWarrior/contact.png` `GuildWarrior/views/01_front.png` `GuildWarrior/views/02_right.png` `GuildWarrior/views/03_back.png` `GuildWarrior/views/04_left.png` `GuildWarrior/views/05_front_right.png` `GuildWarrior/views/06_back_right.png` `GuildWarrior/views/07_back_left.png` `GuildWarrior/views/08_front_left.png` `GuildWarrior/views/09_top.png` `GuildWarrior/views/10_iso.png` `GuildWarrior/overlay.png`
- **Knight**：`Knight/contact.png` `Knight/views/01_front.png` `Knight/views/02_right.png` `Knight/views/03_back.png` `Knight/views/04_left.png` `Knight/views/05_front_right.png` `Knight/views/06_back_right.png` `Knight/views/07_back_left.png` `Knight/views/08_front_left.png` `Knight/views/09_top.png` `Knight/views/10_iso.png` `Knight/film/attack_melee.png` `Knight/film/death.png` `Knight/film/idle.png` `Knight/film/run.png` `Knight/film/walk.png` `Knight/overlay.png`
- **Market**：`Market/contact.png` `Market/views/01_front.png` `Market/views/02_right.png` `Market/views/03_back.png` `Market/views/04_left.png` `Market/views/05_front_right.png` `Market/views/06_back_right.png` `Market/views/07_back_left.png` `Market/views/08_front_left.png` `Market/views/09_top.png` `Market/views/10_iso.png` `Market/overlay.png`
- **MonsterLair**：`MonsterLair/contact.png` `MonsterLair/views/01_front.png` `MonsterLair/views/02_right.png` `MonsterLair/views/03_back.png` `MonsterLair/views/04_left.png` `MonsterLair/views/05_front_right.png` `MonsterLair/views/06_back_right.png` `MonsterLair/views/07_back_left.png` `MonsterLair/views/08_front_left.png` `MonsterLair/views/09_top.png` `MonsterLair/views/10_iso.png` `MonsterLair/overlay.png`
- **Palace**：`Palace/contact.png` `Palace/views/01_front.png` `Palace/views/02_right.png` `Palace/views/03_back.png` `Palace/views/04_left.png` `Palace/views/05_front_right.png` `Palace/views/06_back_right.png` `Palace/views/07_back_left.png` `Palace/views/08_front_left.png` `Palace/views/09_top.png` `Palace/views/10_iso.png` `Palace/overlay.png`
- **Ranger**：`Ranger/contact.png` `Ranger/views/01_front.png` `Ranger/views/02_right.png` `Ranger/views/03_back.png` `Ranger/views/04_left.png` `Ranger/views/05_front_right.png` `Ranger/views/06_back_right.png` `Ranger/views/07_back_left.png` `Ranger/views/08_front_left.png` `Ranger/views/09_top.png` `Ranger/views/10_iso.png` `Ranger/film/attack_shoot.png` `Ranger/film/death.png` `Ranger/film/idle.png` `Ranger/film/run.png` `Ranger/film/walk.png` `Ranger/overlay.png`
- **Tavern**：`Tavern/contact.png` `Tavern/views/01_front.png` `Tavern/views/02_right.png` `Tavern/views/03_back.png` `Tavern/views/04_left.png` `Tavern/views/05_front_right.png` `Tavern/views/06_back_right.png` `Tavern/views/07_back_left.png` `Tavern/views/08_front_left.png` `Tavern/views/09_top.png` `Tavern/views/10_iso.png` `Tavern/overlay.png`
- **Troll**：`Troll/contact.png` `Troll/views/01_front.png` `Troll/views/02_right.png` `Troll/views/03_back.png` `Troll/views/04_left.png` `Troll/views/05_front_right.png` `Troll/views/06_back_right.png` `Troll/views/07_back_left.png` `Troll/views/08_front_left.png` `Troll/views/09_top.png` `Troll/views/10_iso.png` `Troll/film/attack_melee.png` `Troll/film/death.png` `Troll/film/idle.png` `Troll/film/run.png` `Troll/film/walk.png` `Troll/overlay.png`
- **Warrior**：`Warrior/contact.png` `Warrior/views/01_front.png` `Warrior/views/02_right.png` `Warrior/views/03_back.png` `Warrior/views/04_left.png` `Warrior/views/05_front_right.png` `Warrior/views/06_back_right.png` `Warrior/views/07_back_left.png` `Warrior/views/08_front_left.png` `Warrior/views/09_top.png` `Warrior/views/10_iso.png` `Warrior/film/attack_melee.png` `Warrior/film/death.png` `Warrior/film/idle.png` `Warrior/film/run.png` `Warrior/film/walk.png` `Warrior/overlay.png`
- **比例尺同框**：`scale_side.png` `scale_top.png`
