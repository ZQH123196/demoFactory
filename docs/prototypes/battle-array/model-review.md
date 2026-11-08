# 模型体检单 — BattleArray

> 由 `dotnet run --project src/Tools -- review BattleArray .` 生成，**勿手改**。
> 怎么算合格出自 `docs/《模型与动画准则》「验收清单」；PASS/FAIL 由引擎侧校验器算（一次计算两处用），
> 本文件只是它的可读投影。图不提交进版本库（`.cache/` 整条被 gitignore），这里只留可比对的数字。
› 规格：栏 1 十视图（前后左右 + 四角 45° + 正俯视 + 等距 45°，一律绑定姿势）；栏 2 胶卷格距 0.2 秒（= 逻辑 2 步）封顶 8 格；栏 3 比例尺同框 + 1 米方格网。不含时间戳，重跑逐字全等。

- 出图 640x480，窗口挪到 6000,6000（屏外），引擎 4.7.2-stable (official)
- 占地上限 ≤ 3 米；动作契约 idle / walk / run / death

## 汇总

8 个模型 · 8 PASS · 0 FAIL

## 逐模型

| 模型 | 骨 | 占地 X×Z | 高 | 底 y | 面 | 血条宽 @ 名牌高 | 最大骨偏移/米（每条 clip） | 结论 |
|---|---|---|---|---|---|---|---|---|
| `HumanArcher` | 18 | 0.65×0.52 | 1.72 | -0.00 | 8 | 血条 1.04 米 @ 1.90 米 | attack_shoot 0.585 death 0.303 idle 0.009 run 0.690 walk 0.314 windup_pierce 0.675 | PASS |
| `HumanRider` | 38 | 1.04×2.48 | 2.70 | -0.01 | 10 | 血条 1.10 米 @ 2.87 米 | attack_melee 0.459 death 0.825 idle 0.010 run 0.637 walk 0.373 windup_trample 0.372 | PASS |
| `HumanTank` | 18 | 1.17×0.57 | 1.79 | 0.00 | 7 | 血条 1.31 米 @ 1.90 米 | attack_melee 0.444 death 0.306 idle 0.010 run 0.678 walk 0.310 windup_slam 0.182 | PASS |
| `HumanWarrior` | 18 | 1.03×0.82 | 1.81 | 0.00 | 7 | 血条 1.17 米 @ 1.94 米 | attack_melee 0.453 death 0.309 idle 0.010 run 0.709 walk 0.323 windup_slash 0.595 | PASS |
| `OrcArcher` | 18 | 0.90×0.44 | 1.76 | 0.00 | 9 | 血条 1.09 米 @ 1.94 米 | attack_shoot 0.587 death 0.307 idle 0.009 run 0.700 walk 0.319 windup_pierce 0.679 | PASS |
| `OrcRider` | 38 | 1.12×1.67 | 2.18 | -0.00 | 11 | 血条 1.13 米 @ 2.33 米 | attack_melee 0.481 death 0.547 idle 0.010 run 0.469 walk 0.247 windup_trample 0.392 | PASS |
| `OrcTank` | 18 | 1.25×0.63 | 1.88 | 0.00 | 9 | 血条 1.37 米 @ 2.02 米 | attack_melee 0.507 death 0.320 idle 0.011 run 0.733 walk 0.335 windup_slam 0.208 | PASS |
| `OrcWarrior` | 18 | 1.26×0.69 | 1.81 | -0.00 | 9 | 血条 1.23 米 @ 1.99 米 | attack_melee 0.467 death 0.315 idle 0.010 run 0.720 walk 0.328 windup_slash 0.611 | PASS |

## 胶卷采样时刻（秒；格距≠默认值即本条升过档，`once` = 引擎读到的这条动作不循环）

| 模型 | clip | 长度 | 格距 | 格数 | 循环 | 采样时刻 |
|---|---|---|---|---|---|---|
| `HumanArcher` | attack_shoot | 1.50 | 0.40 | 5 | once | 0.00 0.40 0.80 1.20 1.50 |
| `HumanArcher` | death | 1.50 | 0.40 | 5 | once | 0.00 0.40 0.80 1.20 1.50 |
| `HumanArcher` | idle | 3.20 | 0.40 | 8 | loop | 0.00 0.40 0.80 1.20 1.60 2.00 2.40 2.80 |
| `HumanArcher` | run | 0.62 | 0.20 | 4 | loop | 0.00 0.20 0.40 0.60 |
| `HumanArcher` | walk | 0.92 | 0.20 | 5 | loop | 0.00 0.20 0.40 0.60 0.80 |
| `HumanArcher` | windup_pierce | 0.60 | 0.20 | 4 | once | 0.00 0.20 0.40 0.60 |
| `HumanRider` | attack_melee | 1.00 | 0.20 | 6 | once | 0.00 0.20 0.40 0.60 0.80 1.00 |
| `HumanRider` | death | 2.20 | 0.40 | 7 | once | 0.00 0.40 0.80 1.20 1.60 2.00 2.20 |
| `HumanRider` | idle | 3.60 | 0.80 | 5 | loop | 0.00 0.80 1.60 2.40 3.20 |
| `HumanRider` | run | 0.66 | 0.20 | 4 | loop | 0.00 0.20 0.40 0.60 |
| `HumanRider` | walk | 1.15 | 0.20 | 6 | loop | 0.00 0.20 0.40 0.60 0.80 1.00 |
| `HumanRider` | windup_trample | 0.70 | 0.20 | 5 | once | 0.00 0.20 0.40 0.60 0.70 |
| `HumanTank` | attack_melee | 1.00 | 0.20 | 6 | once | 0.00 0.20 0.40 0.60 0.80 1.00 |
| `HumanTank` | death | 1.50 | 0.40 | 5 | once | 0.00 0.40 0.80 1.20 1.50 |
| `HumanTank` | idle | 3.20 | 0.40 | 8 | loop | 0.00 0.40 0.80 1.20 1.60 2.00 2.40 2.80 |
| `HumanTank` | run | 0.62 | 0.20 | 4 | loop | 0.00 0.20 0.40 0.60 |
| `HumanTank` | walk | 0.92 | 0.20 | 5 | loop | 0.00 0.20 0.40 0.60 0.80 |
| `HumanTank` | windup_slam | 0.60 | 0.20 | 4 | once | 0.00 0.20 0.40 0.60 |
| `HumanWarrior` | attack_melee | 1.00 | 0.20 | 6 | once | 0.00 0.20 0.40 0.60 0.80 1.00 |
| `HumanWarrior` | death | 1.50 | 0.40 | 5 | once | 0.00 0.40 0.80 1.20 1.50 |
| `HumanWarrior` | idle | 3.20 | 0.40 | 8 | loop | 0.00 0.40 0.80 1.20 1.60 2.00 2.40 2.80 |
| `HumanWarrior` | run | 0.62 | 0.20 | 4 | loop | 0.00 0.20 0.40 0.60 |
| `HumanWarrior` | walk | 0.92 | 0.20 | 5 | loop | 0.00 0.20 0.40 0.60 0.80 |
| `HumanWarrior` | windup_slash | 0.70 | 0.20 | 5 | once | 0.00 0.20 0.40 0.60 0.70 |
| `OrcArcher` | attack_shoot | 1.50 | 0.40 | 5 | once | 0.00 0.40 0.80 1.20 1.50 |
| `OrcArcher` | death | 1.50 | 0.40 | 5 | once | 0.00 0.40 0.80 1.20 1.50 |
| `OrcArcher` | idle | 3.20 | 0.40 | 8 | loop | 0.00 0.40 0.80 1.20 1.60 2.00 2.40 2.80 |
| `OrcArcher` | run | 0.62 | 0.20 | 4 | loop | 0.00 0.20 0.40 0.60 |
| `OrcArcher` | walk | 0.92 | 0.20 | 5 | loop | 0.00 0.20 0.40 0.60 0.80 |
| `OrcArcher` | windup_pierce | 0.60 | 0.20 | 4 | once | 0.00 0.20 0.40 0.60 |
| `OrcRider` | attack_melee | 1.00 | 0.20 | 6 | once | 0.00 0.20 0.40 0.60 0.80 1.00 |
| `OrcRider` | death | 2.20 | 0.40 | 7 | once | 0.00 0.40 0.80 1.20 1.60 2.00 2.20 |
| `OrcRider` | idle | 3.60 | 0.80 | 5 | loop | 0.00 0.80 1.60 2.40 3.20 |
| `OrcRider` | run | 0.66 | 0.20 | 4 | loop | 0.00 0.20 0.40 0.60 |
| `OrcRider` | walk | 1.15 | 0.20 | 6 | loop | 0.00 0.20 0.40 0.60 0.80 1.00 |
| `OrcRider` | windup_trample | 0.70 | 0.20 | 5 | once | 0.00 0.20 0.40 0.60 0.70 |
| `OrcTank` | attack_melee | 1.00 | 0.20 | 6 | once | 0.00 0.20 0.40 0.60 0.80 1.00 |
| `OrcTank` | death | 1.50 | 0.40 | 5 | once | 0.00 0.40 0.80 1.20 1.50 |
| `OrcTank` | idle | 3.20 | 0.40 | 8 | loop | 0.00 0.40 0.80 1.20 1.60 2.00 2.40 2.80 |
| `OrcTank` | run | 0.62 | 0.20 | 4 | loop | 0.00 0.20 0.40 0.60 |
| `OrcTank` | walk | 0.92 | 0.20 | 5 | loop | 0.00 0.20 0.40 0.60 0.80 |
| `OrcTank` | windup_slam | 0.60 | 0.20 | 4 | once | 0.00 0.20 0.40 0.60 |
| `OrcWarrior` | attack_melee | 1.00 | 0.20 | 6 | once | 0.00 0.20 0.40 0.60 0.80 1.00 |
| `OrcWarrior` | death | 1.50 | 0.40 | 5 | once | 0.00 0.40 0.80 1.20 1.50 |
| `OrcWarrior` | idle | 3.20 | 0.40 | 8 | loop | 0.00 0.40 0.80 1.20 1.60 2.00 2.40 2.80 |
| `OrcWarrior` | run | 0.62 | 0.20 | 4 | loop | 0.00 0.20 0.40 0.60 |
| `OrcWarrior` | walk | 0.92 | 0.20 | 5 | loop | 0.00 0.20 0.40 0.60 0.80 |
| `OrcWarrior` | windup_slash | 0.70 | 0.20 | 5 | once | 0.00 0.20 0.40 0.60 0.70 |

## 产物清单（`.cache/Review/BattleArray/` 下，本机可读）

- **HumanArcher**：`HumanArcher/contact.png` `HumanArcher/views/01_front.png` `HumanArcher/views/02_right.png` `HumanArcher/views/03_back.png` `HumanArcher/views/04_left.png` `HumanArcher/views/05_front_right.png` `HumanArcher/views/06_back_right.png` `HumanArcher/views/07_back_left.png` `HumanArcher/views/08_front_left.png` `HumanArcher/views/09_top.png` `HumanArcher/views/10_iso.png` `HumanArcher/film/attack_shoot.png` `HumanArcher/film/death.png` `HumanArcher/film/idle.png` `HumanArcher/film/run.png` `HumanArcher/film/walk.png` `HumanArcher/film/windup_pierce.png` `HumanArcher/overlay.png`
- **HumanRider**：`HumanRider/contact.png` `HumanRider/views/01_front.png` `HumanRider/views/02_right.png` `HumanRider/views/03_back.png` `HumanRider/views/04_left.png` `HumanRider/views/05_front_right.png` `HumanRider/views/06_back_right.png` `HumanRider/views/07_back_left.png` `HumanRider/views/08_front_left.png` `HumanRider/views/09_top.png` `HumanRider/views/10_iso.png` `HumanRider/film/attack_melee.png` `HumanRider/film/death.png` `HumanRider/film/idle.png` `HumanRider/film/run.png` `HumanRider/film/walk.png` `HumanRider/film/windup_trample.png` `HumanRider/overlay.png`
- **HumanTank**：`HumanTank/contact.png` `HumanTank/views/01_front.png` `HumanTank/views/02_right.png` `HumanTank/views/03_back.png` `HumanTank/views/04_left.png` `HumanTank/views/05_front_right.png` `HumanTank/views/06_back_right.png` `HumanTank/views/07_back_left.png` `HumanTank/views/08_front_left.png` `HumanTank/views/09_top.png` `HumanTank/views/10_iso.png` `HumanTank/film/attack_melee.png` `HumanTank/film/death.png` `HumanTank/film/idle.png` `HumanTank/film/run.png` `HumanTank/film/walk.png` `HumanTank/film/windup_slam.png` `HumanTank/overlay.png`
- **HumanWarrior**：`HumanWarrior/contact.png` `HumanWarrior/views/01_front.png` `HumanWarrior/views/02_right.png` `HumanWarrior/views/03_back.png` `HumanWarrior/views/04_left.png` `HumanWarrior/views/05_front_right.png` `HumanWarrior/views/06_back_right.png` `HumanWarrior/views/07_back_left.png` `HumanWarrior/views/08_front_left.png` `HumanWarrior/views/09_top.png` `HumanWarrior/views/10_iso.png` `HumanWarrior/film/attack_melee.png` `HumanWarrior/film/death.png` `HumanWarrior/film/idle.png` `HumanWarrior/film/run.png` `HumanWarrior/film/walk.png` `HumanWarrior/film/windup_slash.png` `HumanWarrior/overlay.png`
- **OrcArcher**：`OrcArcher/contact.png` `OrcArcher/views/01_front.png` `OrcArcher/views/02_right.png` `OrcArcher/views/03_back.png` `OrcArcher/views/04_left.png` `OrcArcher/views/05_front_right.png` `OrcArcher/views/06_back_right.png` `OrcArcher/views/07_back_left.png` `OrcArcher/views/08_front_left.png` `OrcArcher/views/09_top.png` `OrcArcher/views/10_iso.png` `OrcArcher/film/attack_shoot.png` `OrcArcher/film/death.png` `OrcArcher/film/idle.png` `OrcArcher/film/run.png` `OrcArcher/film/walk.png` `OrcArcher/film/windup_pierce.png` `OrcArcher/overlay.png`
- **OrcRider**：`OrcRider/contact.png` `OrcRider/views/01_front.png` `OrcRider/views/02_right.png` `OrcRider/views/03_back.png` `OrcRider/views/04_left.png` `OrcRider/views/05_front_right.png` `OrcRider/views/06_back_right.png` `OrcRider/views/07_back_left.png` `OrcRider/views/08_front_left.png` `OrcRider/views/09_top.png` `OrcRider/views/10_iso.png` `OrcRider/film/attack_melee.png` `OrcRider/film/death.png` `OrcRider/film/idle.png` `OrcRider/film/run.png` `OrcRider/film/walk.png` `OrcRider/film/windup_trample.png` `OrcRider/overlay.png`
- **OrcTank**：`OrcTank/contact.png` `OrcTank/views/01_front.png` `OrcTank/views/02_right.png` `OrcTank/views/03_back.png` `OrcTank/views/04_left.png` `OrcTank/views/05_front_right.png` `OrcTank/views/06_back_right.png` `OrcTank/views/07_back_left.png` `OrcTank/views/08_front_left.png` `OrcTank/views/09_top.png` `OrcTank/views/10_iso.png` `OrcTank/film/attack_melee.png` `OrcTank/film/death.png` `OrcTank/film/idle.png` `OrcTank/film/run.png` `OrcTank/film/walk.png` `OrcTank/film/windup_slam.png` `OrcTank/overlay.png`
- **OrcWarrior**：`OrcWarrior/contact.png` `OrcWarrior/views/01_front.png` `OrcWarrior/views/02_right.png` `OrcWarrior/views/03_back.png` `OrcWarrior/views/04_left.png` `OrcWarrior/views/05_front_right.png` `OrcWarrior/views/06_back_right.png` `OrcWarrior/views/07_back_left.png` `OrcWarrior/views/08_front_left.png` `OrcWarrior/views/09_top.png` `OrcWarrior/views/10_iso.png` `OrcWarrior/film/attack_melee.png` `OrcWarrior/film/death.png` `OrcWarrior/film/idle.png` `OrcWarrior/film/run.png` `OrcWarrior/film/walk.png` `OrcWarrior/film/windup_slash.png` `OrcWarrior/overlay.png`
- **比例尺同框**：`scale_side.png` `scale_top.png`
