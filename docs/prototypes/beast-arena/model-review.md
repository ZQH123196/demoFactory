# 模型体检单 — BeastArena

> 由 `dotnet run --project src/Tools -- review BeastArena .` 生成，**勿手改**。
> 怎么算合格出自 `docs/《模型与动画准则》「验收清单」；PASS/FAIL 由引擎侧校验器算（一次计算两处用），
> 本文件只是它的可读投影。图不提交进版本库（`.cache/` 整条被 gitignore），这里只留可比对的数字。
› 规格：栏 1 十视图（前后左右 + 四角 45° + 正俯视 + 等距 45°，一律绑定姿势）；栏 2 胶卷格距 0.2 秒（= 逻辑 2 步）封顶 8 格；栏 3 比例尺同框分 10 组（组内一行、各组按声明网格）。不含时间戳，重跑逐字全等。

- 出图 640x480，窗口挪到 6000,6000（屏外），引擎 4.7.2-stable (official)
- 占地上限 ≤ 120 米；动作契约 idle / walk / run

## 汇总

88 个模型 · 88 PASS · 0 FAIL

## 逐模型

| 模型 | 骨 | 占地 X×Z | 高 | 底 y | 面 | 血条宽 @ 名牌高 | 最大骨偏移/米（每条 clip） | 结论 |
|---|---|---|---|---|---|---|---|---|
| `Airship` | 0 | 13.07×6.70 | 5.50 | 1.70 | 7 | 血条 6.00 米 @ 7.80 米 | — | PASS |
| `AirshipDock` | 0 | 5.20×5.28 | 4.34 | 0.00 | 5 | 血条 3.20 米 @ 5.20 米 | — | PASS |
| `AirshipDockDamaged` | 0 | 5.20×5.21 | 1.80 | 0.00 | 5 | 血条 3.20 米 @ 2.60 米 | — | PASS |
| `ArcheryRange` | 0 | 5.30×5.20 | 2.48 | 0.00 | 7 | 血条 2.80 米 @ 3.10 米 | — | PASS |
| `ArcheryRangeDamaged` | 0 | 5.20×5.20 | 1.19 | 0.00 | 8 | 血条 2.80 米 @ 2.40 米 | — | PASS |
| `Arena` | 0 | 7.20×7.20 | 3.56 | 0.00 | 7 | 血条 3.60 米 @ 4.40 米 | — | PASS |
| `ArenaDamaged` | 0 | 7.20×7.20 | 2.40 | 0.00 | 7 | 血条 3.60 米 @ 3.10 米 | — | PASS |
| `BackPoorA` | 0 | 11.00×9.20 | 8.70 | 0.00 | 4 | 血条 6.00 米 @ 9.60 米 | — | PASS |
| `BackPoorB` | 0 | 9.50×9.50 | 7.30 | 0.00 | 3 | 血条 5.00 米 @ 8.20 米 | — | PASS |
| `BackPoorC` | 0 | 8.00×7.22 | 11.40 | 0.00 | 5 | 血条 5.00 米 @ 12.00 米 | — | PASS |
| `BackPoorD` | 0 | 9.30×7.40 | 9.80 | 0.00 | 4 | 血条 5.00 米 @ 10.60 米 | — | PASS |
| `BackRichA` | 0 | 11.50×11.87 | 24.20 | 0.00 | 7 | 血条 6.00 米 @ 25.00 米 | — | PASS |
| `BackRichB` | 0 | 18.00×14.00 | 13.46 | 0.00 | 5 | 血条 9.00 米 @ 13.60 米 | — | PASS |
| `BackRichC` | 0 | 17.00×12.00 | 12.55 | 0.00 | 4 | 血条 9.00 米 @ 12.80 米 | — | PASS |
| `BackRichD` | 0 | 10.00×10.00 | 5.10 | 0.00 | 5 | 血条 6.00 米 @ 7.00 米 | — | PASS |
| `BackSandA` | 0 | 14.40×10.30 | 9.20 | 0.00 | 2 | 血条 7.00 米 @ 10.20 米 | — | PASS |
| `BackSandB` | 0 | 12.00×12.10 | 7.00 | 0.00 | 3 | 血条 6.00 米 @ 8.00 米 | — | PASS |
| `BackSandC` | 0 | 12.40×9.90 | 16.75 | 0.00 | 2 | 血条 5.00 米 @ 18.00 米 | — | PASS |
| `BackSandD` | 0 | 11.00×7.50 | 4.65 | 0.00 | 4 | 血条 7.00 米 @ 5.80 米 | — | PASS |
| `Beast` | 20 | 52.93×89.58 | 30.48 | -0.12 | 4 | 血条 8.00 米 @ 42.00 米 | advance 5.404 eat 0.787 form_shift 3.869 get_up 2.251 idle 0.159 lie_down 4.933 run 10.068 walk 5.304 | PASS |
| `BeastDesert` | 20 | 52.93×89.58 | 30.68 | -0.12 | 4 | 血条 8.00 米 @ 42.00 米 | advance 5.404 eat 0.787 form_shift 3.869 get_up 2.259 idle 0.159 lie_down 4.933 run 10.070 walk 5.272 | PASS |
| `BeastDesertTier2` | 20 | 53.00×89.58 | 32.52 | -0.12 | 5 | 血条 8.00 米 @ 42.00 米 | advance 5.404 eat 0.787 form_shift 3.869 get_up 2.259 idle 0.159 lie_down 4.933 run 10.070 walk 5.299 | PASS |
| `BeastGlutton` | 20 | 52.93×89.58 | 30.52 | -0.17 | 4 | 血条 8.00 米 @ 42.00 米 | advance 5.328 eat 0.755 form_shift 3.869 get_up 2.251 idle 0.153 lie_down 4.909 run 10.068 walk 5.070 | PASS |
| `BeastGluttonTier2` | 20 | 58.30×89.58 | 32.97 | -0.17 | 5 | 血条 8.00 米 @ 42.00 米 | advance 5.328 eat 0.755 form_shift 3.869 get_up 2.251 idle 0.153 lie_down 4.909 run 10.068 walk 5.099 | PASS |
| `BeastTier2` | 20 | 60.00×89.58 | 30.48 | -0.12 | 6 | 血条 8.00 米 @ 42.00 米 | advance 5.404 eat 0.787 form_shift 3.869 get_up 2.251 idle 0.159 lie_down 4.933 run 10.068 walk 5.332 | PASS |
| `Cargo` | 0 | 1.49×1.02 | 0.95 | 0.00 | 4 | 血条 0.80 米 @ 1.25 米 | — | PASS |
| `CaveMouth` | 0 | 10.00×8.90 | 4.20 | 0.00 | 4 | 血条 2.00 米 @ 5.20 米 | — | PASS |
| `CavernTile` | 0 | 12.00×12.00 | 3.00 | 0.00 | 5 | 血条 2.00 米 @ 3.80 米 | — | PASS |
| `Church` | 0 | 3.00×3.12 | 3.68 | 0.00 | 6 | 血条 1.80 米 @ 4.20 米 | — | PASS |
| `ChurchDamaged` | 0 | 3.04×3.44 | 2.14 | -0.06 | 6 | 血条 1.80 米 @ 2.70 米 | — | PASS |
| `Citizen` | 18 | 0.62×0.62 | 1.81 | -0.00 | 8 | 血条 0.93 米 @ 1.86 米 | attack_melee 0.394 carry 0.165 death 0.296 hit 0.160 idle 0.008 run 0.674 walk 0.307 work 0.641 | PASS |
| `CityNode` | 0 | 108.00×96.00 | 26.08 | 0.00 | 6 | 血条 30.00 米 @ 28.00 米 | — | PASS |
| `Corpse` | 0 | 1.12×1.47 | 0.22 | 0.00 | 7 | 血条 0.60 米 @ 0.50 米 | — | PASS |
| `Crystal` | 0 | 0.40×0.34 | 0.36 | 0.00 | 2 | 血条 0.45 米 @ 0.55 米 | — | PASS |
| `CrystalShard` | 0 | 2.20×2.20 | 3.05 | 0.00 | 2 | 血条 1.60 米 @ 3.60 米 | — | PASS |
| `CrystalSocket` | 0 | 4.00×4.00 | 2.60 | 0.00 | 4 | 血条 1.80 米 @ 3.40 米 | — | PASS |
| `DistantTower` | 0 | 9.00×9.00 | 42.50 | 0.00 | 8 | 血条 2.40 米 @ 44.50 米 | — | PASS |
| `Dormitory` | 0 | 3.20×3.03 | 3.14 | 0.00 | 5 | 血条 1.80 米 @ 3.90 米 | — | PASS |
| `DormitoryDamaged` | 0 | 3.20×3.20 | 2.42 | 0.00 | 6 | 血条 1.80 米 @ 3.90 米 | — | PASS |
| `FoodNode` | 0 | 36.00×32.00 | 11.40 | 0.00 | 8 | 血条 8.00 米 @ 12.00 米 | — | PASS |
| `Gladiator` | 18 | 0.95×0.71 | 1.83 | 0.00 | 6 | 血条 1.06 米 @ 1.99 米 | attack_melee 0.470 carry 0.179 death 0.317 hit 0.182 idle 0.010 run 0.727 walk 0.331 work 0.732 | PASS |
| `Granary` | 0 | 3.00×3.00 | 2.68 | 0.00 | 6 | 血条 1.80 米 @ 3.20 米 | — | PASS |
| `GranaryDamaged` | 0 | 3.25×3.56 | 3.03 | -0.79 | 7 | 血条 1.80 米 @ 2.90 米 | — | PASS |
| `GroundExit` | 0 | 2.60×2.60 | 6.50 | 0.00 | 4 | 血条 1.60 米 @ 7.20 米 | — | PASS |
| `GroundPoor` | 0 | 100.00×96.00 | 2.00 | -0.10 | 8 | 血条 20.00 米 @ 2.00 米 | — | PASS |
| `GroundRich` | 0 | 100.00×96.00 | 1.00 | 0.00 | 4 | 血条 20.00 米 @ 2.00 米 | — | PASS |
| `GroundSand` | 0 | 96.00×96.00 | 2.20 | -0.40 | 5 | 血条 20.00 米 @ 2.00 米 | — | PASS |
| `Helper` | 18 | 0.57×0.46 | 1.62 | 0.00 | 9 | 血条 0.87 米 @ 1.81 米 | attack_melee 0.373 carry 0.161 death 0.289 hit 0.154 idle 0.008 run 0.657 walk 0.299 work 0.606 | PASS |
| `HerdBeast` | 0 | 1.39×3.40 | 2.80 | 0.00 | 3 | 血条 1.60 米 @ 3.20 米 | — | PASS |
| `Hotel` | 0 | 5.20×4.90 | 3.69 | 0.00 | 7 | 血条 3.20 米 @ 4.40 米 | — | PASS |
| `HotelDamaged` | 0 | 5.20×5.39 | 3.32 | 0.00 | 7 | 血条 3.20 米 @ 3.70 米 | — | PASS |
| `LandmarkPoor` | 0 | 84.00×96.00 | 13.40 | 0.00 | 9 | 血条 10.00 米 @ 24.00 米 | — | PASS |
| `LandmarkRich` | 0 | 108.00×96.00 | 29.40 | 0.00 | 10 | 血条 10.00 米 @ 33.00 米 | — | PASS |
| `LandmarkSand` | 0 | 90.00×96.00 | 23.80 | 0.00 | 6 | 血条 10.00 米 @ 27.00 米 | — | PASS |
| `LevitationBase` | 0 | 13.20×13.20 | 3.90 | 0.00 | 3 | 血条 none 米 @ none 米 | — | PASS |
| `LevitationBaseTier2` | 0 | 15.40×15.40 | 3.90 | 0.00 | 3 | 血条 none 米 @ none 米 | — | PASS |
| `MiningFace` | 0 | 8.00×5.30 | 6.00 | 0.00 | 8 | 血条 2.40 米 @ 6.80 米 | — | PASS |
| `Monster` | 18 | 1.21×0.54 | 2.09 | 0.00 | 6 | 血条 1.32 米 @ 2.24 米 | attack_melee 0.598 death 0.367 emerge 1.202 hit 0.219 idle 0.013 run 0.845 walk 0.386 | PASS |
| `MonsterBurrower` | 18 | 0.97×0.68 | 1.51 | 0.00 | 6 | 血条 0.97 米 @ 1.62 米 | attack_melee 0.402 death 0.272 emerge 0.745 hit 0.148 idle 0.009 run 0.626 walk 0.286 | PASS |
| `OreChunk` | 0 | 0.60×0.48 | 0.47 | 0.00 | 4 | 血条 0.45 米 @ 0.50 米 | — | PASS |
| `OreNode` | 0 | 5.20×4.20 | 4.20 | 0.00 | 5 | 血条 2.40 米 @ 4.80 米 | — | PASS |
| `Pontoon` | 0 | 4.80×2.60 | 2.56 | 0.00 | 4 | 血条 2.80 米 @ 3.20 米 | — | PASS |
| `Restaurant` | 0 | 5.20×4.80 | 2.72 | 0.00 | 6 | 血条 3.00 米 @ 3.40 米 | — | PASS |
| `RestaurantDamaged` | 0 | 5.20×5.18 | 2.27 | 0.00 | 7 | 血条 3.00 米 @ 2.80 米 | — | PASS |
| `RuinLarge` | 0 | 7.20×7.20 | 2.28 | 0.00 | 7 | 血条 6.80 米 @ 4.20 米 | — | PASS |
| `RuinMedium` | 0 | 5.20×5.20 | 1.78 | 0.00 | 7 | 血条 4.80 米 @ 3.00 米 | — | PASS |
| `RuinSmall` | 0 | 3.20×3.20 | 1.38 | 0.00 | 7 | 血条 3.00 米 @ 2.20 米 | — | PASS |
| `ScaffoldLarge` | 0 | 7.38×7.51 | 6.65 | -0.43 | 2 | 血条 6.80 米 @ 6.80 米 | — | PASS |
| `ScaffoldMedium` | 0 | 5.38×5.39 | 5.07 | -0.43 | 2 | 血条 4.80 米 @ 5.20 米 | — | PASS |
| `ScaffoldSmall` | 0 | 3.38×3.36 | 3.68 | -0.44 | 2 | 血条 3.20 米 @ 3.80 米 | — | PASS |
| `SoilBlock` | 0 | 12.00×12.00 | 3.00 | 0.00 | 2 | 血条 2.00 米 @ 3.80 米 | — | PASS |
| `SparePart` | 0 | 0.48×0.59 | 0.24 | 0.01 | 2 | 血条 0.50 米 @ 0.45 米 | — | PASS |
| `SpecialMaterial` | 0 | 0.50×0.40 | 0.24 | 0.00 | 3 | 血条 0.50 米 @ 0.50 米 | — | PASS |
| `Stable` | 0 | 5.20×5.30 | 2.89 | 0.00 | 7 | 血条 3.00 米 @ 3.60 米 | — | PASS |
| `StableDamaged` | 0 | 5.20×5.20 | 2.41 | -0.36 | 6 | 血条 3.00 米 @ 2.90 米 | — | PASS |
| `TeleportArray` | 0 | 3.00×3.00 | 3.30 | 0.00 | 3 | 血条 1.80 米 @ 4.20 米 | — | PASS |
| `TeleportArrayDamaged` | 0 | 3.32×3.33 | 2.45 | 0.00 | 5 | 血条 1.80 米 @ 3.60 米 | — | PASS |
| `TownDeck` | 0 | 49.03×48.60 | 27.28 | 0.00 | 6 | 血条 none 米 @ none 米 | — | PASS |
| `TownDeckTier2` | 0 | 57.23×56.60 | 27.28 | 0.00 | 6 | 血条 none 米 @ none 米 | — | PASS |
| `TownHall` | 0 | 7.20×7.20 | 6.64 | 0.00 | 8 | 血条 3.80 米 @ 7.40 米 | — | PASS |
| `TownHallDamaged` | 0 | 7.20×7.20 | 4.45 | -0.44 | 10 | 血条 3.80 米 @ 4.20 米 | — | PASS |
| `TrainingGround` | 0 | 5.20×5.30 | 3.08 | 0.00 | 6 | 血条 3.00 米 @ 3.40 米 | — | PASS |
| `TrainingGroundDamaged` | 0 | 5.20×5.30 | 1.75 | 0.00 | 6 | 血条 3.00 米 @ 2.60 米 | — | PASS |
| `TreeBroadleaf` | 0 | 5.16×4.76 | 6.30 | 0.00 | 3 | 血条 2.00 米 @ 7.40 米 | — | PASS |
| `TreePine` | 0 | 5.20×5.20 | 8.20 | 0.00 | 3 | 血条 2.00 米 @ 9.20 米 | — | PASS |
| `Worker` | 18 | 0.81×0.65 | 1.68 | -0.00 | 9 | 血条 1.00 米 @ 1.88 米 | attack_melee 0.415 carry 0.167 death 0.299 hit 0.166 idle 0.009 run 0.683 walk 0.311 work 0.669 | PASS |
| `Workshop` | 0 | 5.20×4.80 | 4.18 | 0.00 | 10 | 血条 3.20 米 @ 5.60 米 | — | PASS |
| `WorkshopDamaged` | 0 | 5.29×5.24 | 2.66 | 0.00 | 8 | 血条 3.20 米 @ 3.40 米 | — | PASS |

胶卷机位：`Beast` / `BeastDesert` / `BeastGlutton` / `BeastTier2` / `BeastDesertTier2` / `BeastGluttonTier2` 方位 90° / 仰角 10°；其余模型走缺省 方位 135° / 仰角 45°。

## 胶卷采样时刻（秒；格距≠默认值即本条升过档，`once` = 引擎读到的这条动作不循环）

| 模型 | clip | 长度 | 格距 | 格数 | 循环 | 采样时刻 |
|---|---|---|---|---|---|---|
| `Beast` | advance | 2.60 | 0.40 | 8 | once | 0.00 0.40 0.80 1.20 1.60 2.00 2.40 2.60 |
| `Beast` | eat | 2.40 | 0.40 | 7 | once | 0.00 0.40 0.80 1.20 1.60 2.00 2.40 |
| `Beast` | form_shift | 1.80 | 0.40 | 6 | once | 0.00 0.40 0.80 1.20 1.60 1.80 |
| `Beast` | get_up | 2.20 | 0.40 | 7 | once | 0.00 0.40 0.80 1.20 1.60 2.00 2.20 |
| `Beast` | idle | 4.00 | 0.80 | 5 | loop | 0.00 0.80 1.60 2.40 3.20 |
| `Beast` | lie_down | 2.60 | 0.40 | 8 | once | 0.00 0.40 0.80 1.20 1.60 2.00 2.40 2.60 |
| `Beast` | run | 1.00 | 0.20 | 5 | loop | 0.00 0.20 0.40 0.60 0.80 |
| `Beast` | walk | 1.60 | 0.20 | 8 | loop | 0.00 0.20 0.40 0.60 0.80 1.00 1.20 1.40 |
| `BeastDesert` | advance | 2.60 | 0.40 | 8 | once | 0.00 0.40 0.80 1.20 1.60 2.00 2.40 2.60 |
| `BeastDesert` | eat | 2.40 | 0.40 | 7 | once | 0.00 0.40 0.80 1.20 1.60 2.00 2.40 |
| `BeastDesert` | form_shift | 1.80 | 0.40 | 6 | once | 0.00 0.40 0.80 1.20 1.60 1.80 |
| `BeastDesert` | get_up | 2.20 | 0.40 | 7 | once | 0.00 0.40 0.80 1.20 1.60 2.00 2.20 |
| `BeastDesert` | idle | 4.00 | 0.80 | 5 | loop | 0.00 0.80 1.60 2.40 3.20 |
| `BeastDesert` | lie_down | 2.60 | 0.40 | 8 | once | 0.00 0.40 0.80 1.20 1.60 2.00 2.40 2.60 |
| `BeastDesert` | run | 1.00 | 0.20 | 5 | loop | 0.00 0.20 0.40 0.60 0.80 |
| `BeastDesert` | walk | 1.60 | 0.20 | 8 | loop | 0.00 0.20 0.40 0.60 0.80 1.00 1.20 1.40 |
| `BeastDesertTier2` | advance | 2.60 | 0.40 | 8 | once | 0.00 0.40 0.80 1.20 1.60 2.00 2.40 2.60 |
| `BeastDesertTier2` | eat | 2.40 | 0.40 | 7 | once | 0.00 0.40 0.80 1.20 1.60 2.00 2.40 |
| `BeastDesertTier2` | form_shift | 1.80 | 0.40 | 6 | once | 0.00 0.40 0.80 1.20 1.60 1.80 |
| `BeastDesertTier2` | get_up | 2.20 | 0.40 | 7 | once | 0.00 0.40 0.80 1.20 1.60 2.00 2.20 |
| `BeastDesertTier2` | idle | 4.00 | 0.80 | 5 | loop | 0.00 0.80 1.60 2.40 3.20 |
| `BeastDesertTier2` | lie_down | 2.60 | 0.40 | 8 | once | 0.00 0.40 0.80 1.20 1.60 2.00 2.40 2.60 |
| `BeastDesertTier2` | run | 1.00 | 0.20 | 5 | loop | 0.00 0.20 0.40 0.60 0.80 |
| `BeastDesertTier2` | walk | 1.60 | 0.20 | 8 | loop | 0.00 0.20 0.40 0.60 0.80 1.00 1.20 1.40 |
| `BeastGlutton` | advance | 2.60 | 0.40 | 8 | once | 0.00 0.40 0.80 1.20 1.60 2.00 2.40 2.60 |
| `BeastGlutton` | eat | 2.40 | 0.40 | 7 | once | 0.00 0.40 0.80 1.20 1.60 2.00 2.40 |
| `BeastGlutton` | form_shift | 1.80 | 0.40 | 6 | once | 0.00 0.40 0.80 1.20 1.60 1.80 |
| `BeastGlutton` | get_up | 2.20 | 0.40 | 7 | once | 0.00 0.40 0.80 1.20 1.60 2.00 2.20 |
| `BeastGlutton` | idle | 4.00 | 0.80 | 5 | loop | 0.00 0.80 1.60 2.40 3.20 |
| `BeastGlutton` | lie_down | 2.60 | 0.40 | 8 | once | 0.00 0.40 0.80 1.20 1.60 2.00 2.40 2.60 |
| `BeastGlutton` | run | 1.00 | 0.20 | 5 | loop | 0.00 0.20 0.40 0.60 0.80 |
| `BeastGlutton` | walk | 1.60 | 0.20 | 8 | loop | 0.00 0.20 0.40 0.60 0.80 1.00 1.20 1.40 |
| `BeastGluttonTier2` | advance | 2.60 | 0.40 | 8 | once | 0.00 0.40 0.80 1.20 1.60 2.00 2.40 2.60 |
| `BeastGluttonTier2` | eat | 2.40 | 0.40 | 7 | once | 0.00 0.40 0.80 1.20 1.60 2.00 2.40 |
| `BeastGluttonTier2` | form_shift | 1.80 | 0.40 | 6 | once | 0.00 0.40 0.80 1.20 1.60 1.80 |
| `BeastGluttonTier2` | get_up | 2.20 | 0.40 | 7 | once | 0.00 0.40 0.80 1.20 1.60 2.00 2.20 |
| `BeastGluttonTier2` | idle | 4.00 | 0.80 | 5 | loop | 0.00 0.80 1.60 2.40 3.20 |
| `BeastGluttonTier2` | lie_down | 2.60 | 0.40 | 8 | once | 0.00 0.40 0.80 1.20 1.60 2.00 2.40 2.60 |
| `BeastGluttonTier2` | run | 1.00 | 0.20 | 5 | loop | 0.00 0.20 0.40 0.60 0.80 |
| `BeastGluttonTier2` | walk | 1.60 | 0.20 | 8 | loop | 0.00 0.20 0.40 0.60 0.80 1.00 1.20 1.40 |
| `BeastTier2` | advance | 2.60 | 0.40 | 8 | once | 0.00 0.40 0.80 1.20 1.60 2.00 2.40 2.60 |
| `BeastTier2` | eat | 2.40 | 0.40 | 7 | once | 0.00 0.40 0.80 1.20 1.60 2.00 2.40 |
| `BeastTier2` | form_shift | 1.80 | 0.40 | 6 | once | 0.00 0.40 0.80 1.20 1.60 1.80 |
| `BeastTier2` | get_up | 2.20 | 0.40 | 7 | once | 0.00 0.40 0.80 1.20 1.60 2.00 2.20 |
| `BeastTier2` | idle | 4.00 | 0.80 | 5 | loop | 0.00 0.80 1.60 2.40 3.20 |
| `BeastTier2` | lie_down | 2.60 | 0.40 | 8 | once | 0.00 0.40 0.80 1.20 1.60 2.00 2.40 2.60 |
| `BeastTier2` | run | 1.00 | 0.20 | 5 | loop | 0.00 0.20 0.40 0.60 0.80 |
| `BeastTier2` | walk | 1.60 | 0.20 | 8 | loop | 0.00 0.20 0.40 0.60 0.80 1.00 1.20 1.40 |
| `Citizen` | attack_melee | 1.00 | 0.20 | 6 | once | 0.00 0.20 0.40 0.60 0.80 1.00 |
| `Citizen` | carry | 1.20 | 0.20 | 6 | loop | 0.00 0.20 0.40 0.60 0.80 1.00 |
| `Citizen` | death | 1.50 | 0.40 | 5 | once | 0.00 0.40 0.80 1.20 1.50 |
| `Citizen` | hit | 0.45 | 0.20 | 4 | once | 0.00 0.20 0.40 0.45 |
| `Citizen` | idle | 3.20 | 0.40 | 8 | loop | 0.00 0.40 0.80 1.20 1.60 2.00 2.40 2.80 |
| `Citizen` | run | 0.62 | 0.20 | 4 | loop | 0.00 0.20 0.40 0.60 |
| `Citizen` | walk | 0.92 | 0.20 | 5 | loop | 0.00 0.20 0.40 0.60 0.80 |
| `Citizen` | work | 1.40 | 0.20 | 7 | loop | 0.00 0.20 0.40 0.60 0.80 1.00 1.20 |
| `Gladiator` | attack_melee | 1.00 | 0.20 | 6 | once | 0.00 0.20 0.40 0.60 0.80 1.00 |
| `Gladiator` | carry | 1.20 | 0.20 | 6 | loop | 0.00 0.20 0.40 0.60 0.80 1.00 |
| `Gladiator` | death | 1.50 | 0.40 | 5 | once | 0.00 0.40 0.80 1.20 1.50 |
| `Gladiator` | hit | 0.45 | 0.20 | 4 | once | 0.00 0.20 0.40 0.45 |
| `Gladiator` | idle | 3.20 | 0.40 | 8 | loop | 0.00 0.40 0.80 1.20 1.60 2.00 2.40 2.80 |
| `Gladiator` | run | 0.62 | 0.20 | 4 | loop | 0.00 0.20 0.40 0.60 |
| `Gladiator` | walk | 0.92 | 0.20 | 5 | loop | 0.00 0.20 0.40 0.60 0.80 |
| `Gladiator` | work | 1.40 | 0.20 | 7 | loop | 0.00 0.20 0.40 0.60 0.80 1.00 1.20 |
| `Helper` | attack_melee | 1.00 | 0.20 | 6 | once | 0.00 0.20 0.40 0.60 0.80 1.00 |
| `Helper` | carry | 1.20 | 0.20 | 6 | loop | 0.00 0.20 0.40 0.60 0.80 1.00 |
| `Helper` | death | 1.50 | 0.40 | 5 | once | 0.00 0.40 0.80 1.20 1.50 |
| `Helper` | hit | 0.45 | 0.20 | 4 | once | 0.00 0.20 0.40 0.45 |
| `Helper` | idle | 3.20 | 0.40 | 8 | loop | 0.00 0.40 0.80 1.20 1.60 2.00 2.40 2.80 |
| `Helper` | run | 0.62 | 0.20 | 4 | loop | 0.00 0.20 0.40 0.60 |
| `Helper` | walk | 0.92 | 0.20 | 5 | loop | 0.00 0.20 0.40 0.60 0.80 |
| `Helper` | work | 1.40 | 0.20 | 7 | loop | 0.00 0.20 0.40 0.60 0.80 1.00 1.20 |
| `Monster` | attack_melee | 1.00 | 0.20 | 6 | once | 0.00 0.20 0.40 0.60 0.80 1.00 |
| `Monster` | death | 1.50 | 0.40 | 5 | once | 0.00 0.40 0.80 1.20 1.50 |
| `Monster` | emerge | 1.80 | 0.40 | 6 | once | 0.00 0.40 0.80 1.20 1.60 1.80 |
| `Monster` | hit | 0.45 | 0.20 | 4 | once | 0.00 0.20 0.40 0.45 |
| `Monster` | idle | 3.20 | 0.40 | 8 | loop | 0.00 0.40 0.80 1.20 1.60 2.00 2.40 2.80 |
| `Monster` | run | 0.62 | 0.20 | 4 | loop | 0.00 0.20 0.40 0.60 |
| `Monster` | walk | 0.92 | 0.20 | 5 | loop | 0.00 0.20 0.40 0.60 0.80 |
| `MonsterBurrower` | attack_melee | 1.00 | 0.20 | 6 | once | 0.00 0.20 0.40 0.60 0.80 1.00 |
| `MonsterBurrower` | death | 1.50 | 0.40 | 5 | once | 0.00 0.40 0.80 1.20 1.50 |
| `MonsterBurrower` | emerge | 1.80 | 0.40 | 6 | once | 0.00 0.40 0.80 1.20 1.60 1.80 |
| `MonsterBurrower` | hit | 0.45 | 0.20 | 4 | once | 0.00 0.20 0.40 0.45 |
| `MonsterBurrower` | idle | 3.20 | 0.40 | 8 | loop | 0.00 0.40 0.80 1.20 1.60 2.00 2.40 2.80 |
| `MonsterBurrower` | run | 0.62 | 0.20 | 4 | loop | 0.00 0.20 0.40 0.60 |
| `MonsterBurrower` | walk | 0.92 | 0.20 | 5 | loop | 0.00 0.20 0.40 0.60 0.80 |
| `Worker` | attack_melee | 1.00 | 0.20 | 6 | once | 0.00 0.20 0.40 0.60 0.80 1.00 |
| `Worker` | carry | 1.20 | 0.20 | 6 | loop | 0.00 0.20 0.40 0.60 0.80 1.00 |
| `Worker` | death | 1.50 | 0.40 | 5 | once | 0.00 0.40 0.80 1.20 1.50 |
| `Worker` | hit | 0.45 | 0.20 | 4 | once | 0.00 0.20 0.40 0.45 |
| `Worker` | idle | 3.20 | 0.40 | 8 | loop | 0.00 0.40 0.80 1.20 1.60 2.00 2.40 2.80 |
| `Worker` | run | 0.62 | 0.20 | 4 | loop | 0.00 0.20 0.40 0.60 |
| `Worker` | walk | 0.92 | 0.20 | 5 | loop | 0.00 0.20 0.40 0.60 0.80 |
| `Worker` | work | 1.40 | 0.20 | 7 | loop | 0.00 0.20 0.40 0.60 0.80 1.00 1.20 |

## 产物清单（`.cache/Review/BeastArena/` 下，本机可读）

- **Airship**：`Airship/contact.png` `Airship/views/01_front.png` `Airship/views/02_right.png` `Airship/views/03_back.png` `Airship/views/04_left.png` `Airship/views/05_front_right.png` `Airship/views/06_back_right.png` `Airship/views/07_back_left.png` `Airship/views/08_front_left.png` `Airship/views/09_top.png` `Airship/views/10_iso.png` `Airship/overlay.png`
- **AirshipDock**：`AirshipDock/contact.png` `AirshipDock/views/01_front.png` `AirshipDock/views/02_right.png` `AirshipDock/views/03_back.png` `AirshipDock/views/04_left.png` `AirshipDock/views/05_front_right.png` `AirshipDock/views/06_back_right.png` `AirshipDock/views/07_back_left.png` `AirshipDock/views/08_front_left.png` `AirshipDock/views/09_top.png` `AirshipDock/views/10_iso.png` `AirshipDock/overlay.png`
- **AirshipDockDamaged**：`AirshipDockDamaged/contact.png` `AirshipDockDamaged/views/01_front.png` `AirshipDockDamaged/views/02_right.png` `AirshipDockDamaged/views/03_back.png` `AirshipDockDamaged/views/04_left.png` `AirshipDockDamaged/views/05_front_right.png` `AirshipDockDamaged/views/06_back_right.png` `AirshipDockDamaged/views/07_back_left.png` `AirshipDockDamaged/views/08_front_left.png` `AirshipDockDamaged/views/09_top.png` `AirshipDockDamaged/views/10_iso.png` `AirshipDockDamaged/overlay.png`
- **ArcheryRange**：`ArcheryRange/contact.png` `ArcheryRange/views/01_front.png` `ArcheryRange/views/02_right.png` `ArcheryRange/views/03_back.png` `ArcheryRange/views/04_left.png` `ArcheryRange/views/05_front_right.png` `ArcheryRange/views/06_back_right.png` `ArcheryRange/views/07_back_left.png` `ArcheryRange/views/08_front_left.png` `ArcheryRange/views/09_top.png` `ArcheryRange/views/10_iso.png` `ArcheryRange/overlay.png`
- **ArcheryRangeDamaged**：`ArcheryRangeDamaged/contact.png` `ArcheryRangeDamaged/views/01_front.png` `ArcheryRangeDamaged/views/02_right.png` `ArcheryRangeDamaged/views/03_back.png` `ArcheryRangeDamaged/views/04_left.png` `ArcheryRangeDamaged/views/05_front_right.png` `ArcheryRangeDamaged/views/06_back_right.png` `ArcheryRangeDamaged/views/07_back_left.png` `ArcheryRangeDamaged/views/08_front_left.png` `ArcheryRangeDamaged/views/09_top.png` `ArcheryRangeDamaged/views/10_iso.png` `ArcheryRangeDamaged/overlay.png`
- **Arena**：`Arena/contact.png` `Arena/views/01_front.png` `Arena/views/02_right.png` `Arena/views/03_back.png` `Arena/views/04_left.png` `Arena/views/05_front_right.png` `Arena/views/06_back_right.png` `Arena/views/07_back_left.png` `Arena/views/08_front_left.png` `Arena/views/09_top.png` `Arena/views/10_iso.png` `Arena/overlay.png`
- **ArenaDamaged**：`ArenaDamaged/contact.png` `ArenaDamaged/views/01_front.png` `ArenaDamaged/views/02_right.png` `ArenaDamaged/views/03_back.png` `ArenaDamaged/views/04_left.png` `ArenaDamaged/views/05_front_right.png` `ArenaDamaged/views/06_back_right.png` `ArenaDamaged/views/07_back_left.png` `ArenaDamaged/views/08_front_left.png` `ArenaDamaged/views/09_top.png` `ArenaDamaged/views/10_iso.png` `ArenaDamaged/overlay.png`
- **BackPoorA**：`BackPoorA/contact.png` `BackPoorA/views/01_front.png` `BackPoorA/views/02_right.png` `BackPoorA/views/03_back.png` `BackPoorA/views/04_left.png` `BackPoorA/views/05_front_right.png` `BackPoorA/views/06_back_right.png` `BackPoorA/views/07_back_left.png` `BackPoorA/views/08_front_left.png` `BackPoorA/views/09_top.png` `BackPoorA/views/10_iso.png` `BackPoorA/overlay.png`
- **BackPoorB**：`BackPoorB/contact.png` `BackPoorB/views/01_front.png` `BackPoorB/views/02_right.png` `BackPoorB/views/03_back.png` `BackPoorB/views/04_left.png` `BackPoorB/views/05_front_right.png` `BackPoorB/views/06_back_right.png` `BackPoorB/views/07_back_left.png` `BackPoorB/views/08_front_left.png` `BackPoorB/views/09_top.png` `BackPoorB/views/10_iso.png` `BackPoorB/overlay.png`
- **BackPoorC**：`BackPoorC/contact.png` `BackPoorC/views/01_front.png` `BackPoorC/views/02_right.png` `BackPoorC/views/03_back.png` `BackPoorC/views/04_left.png` `BackPoorC/views/05_front_right.png` `BackPoorC/views/06_back_right.png` `BackPoorC/views/07_back_left.png` `BackPoorC/views/08_front_left.png` `BackPoorC/views/09_top.png` `BackPoorC/views/10_iso.png` `BackPoorC/overlay.png`
- **BackPoorD**：`BackPoorD/contact.png` `BackPoorD/views/01_front.png` `BackPoorD/views/02_right.png` `BackPoorD/views/03_back.png` `BackPoorD/views/04_left.png` `BackPoorD/views/05_front_right.png` `BackPoorD/views/06_back_right.png` `BackPoorD/views/07_back_left.png` `BackPoorD/views/08_front_left.png` `BackPoorD/views/09_top.png` `BackPoorD/views/10_iso.png` `BackPoorD/overlay.png`
- **BackRichA**：`BackRichA/contact.png` `BackRichA/views/01_front.png` `BackRichA/views/02_right.png` `BackRichA/views/03_back.png` `BackRichA/views/04_left.png` `BackRichA/views/05_front_right.png` `BackRichA/views/06_back_right.png` `BackRichA/views/07_back_left.png` `BackRichA/views/08_front_left.png` `BackRichA/views/09_top.png` `BackRichA/views/10_iso.png` `BackRichA/overlay.png`
- **BackRichB**：`BackRichB/contact.png` `BackRichB/views/01_front.png` `BackRichB/views/02_right.png` `BackRichB/views/03_back.png` `BackRichB/views/04_left.png` `BackRichB/views/05_front_right.png` `BackRichB/views/06_back_right.png` `BackRichB/views/07_back_left.png` `BackRichB/views/08_front_left.png` `BackRichB/views/09_top.png` `BackRichB/views/10_iso.png` `BackRichB/overlay.png`
- **BackRichC**：`BackRichC/contact.png` `BackRichC/views/01_front.png` `BackRichC/views/02_right.png` `BackRichC/views/03_back.png` `BackRichC/views/04_left.png` `BackRichC/views/05_front_right.png` `BackRichC/views/06_back_right.png` `BackRichC/views/07_back_left.png` `BackRichC/views/08_front_left.png` `BackRichC/views/09_top.png` `BackRichC/views/10_iso.png` `BackRichC/overlay.png`
- **BackRichD**：`BackRichD/contact.png` `BackRichD/views/01_front.png` `BackRichD/views/02_right.png` `BackRichD/views/03_back.png` `BackRichD/views/04_left.png` `BackRichD/views/05_front_right.png` `BackRichD/views/06_back_right.png` `BackRichD/views/07_back_left.png` `BackRichD/views/08_front_left.png` `BackRichD/views/09_top.png` `BackRichD/views/10_iso.png` `BackRichD/overlay.png`
- **BackSandA**：`BackSandA/contact.png` `BackSandA/views/01_front.png` `BackSandA/views/02_right.png` `BackSandA/views/03_back.png` `BackSandA/views/04_left.png` `BackSandA/views/05_front_right.png` `BackSandA/views/06_back_right.png` `BackSandA/views/07_back_left.png` `BackSandA/views/08_front_left.png` `BackSandA/views/09_top.png` `BackSandA/views/10_iso.png` `BackSandA/overlay.png`
- **BackSandB**：`BackSandB/contact.png` `BackSandB/views/01_front.png` `BackSandB/views/02_right.png` `BackSandB/views/03_back.png` `BackSandB/views/04_left.png` `BackSandB/views/05_front_right.png` `BackSandB/views/06_back_right.png` `BackSandB/views/07_back_left.png` `BackSandB/views/08_front_left.png` `BackSandB/views/09_top.png` `BackSandB/views/10_iso.png` `BackSandB/overlay.png`
- **BackSandC**：`BackSandC/contact.png` `BackSandC/views/01_front.png` `BackSandC/views/02_right.png` `BackSandC/views/03_back.png` `BackSandC/views/04_left.png` `BackSandC/views/05_front_right.png` `BackSandC/views/06_back_right.png` `BackSandC/views/07_back_left.png` `BackSandC/views/08_front_left.png` `BackSandC/views/09_top.png` `BackSandC/views/10_iso.png` `BackSandC/overlay.png`
- **BackSandD**：`BackSandD/contact.png` `BackSandD/views/01_front.png` `BackSandD/views/02_right.png` `BackSandD/views/03_back.png` `BackSandD/views/04_left.png` `BackSandD/views/05_front_right.png` `BackSandD/views/06_back_right.png` `BackSandD/views/07_back_left.png` `BackSandD/views/08_front_left.png` `BackSandD/views/09_top.png` `BackSandD/views/10_iso.png` `BackSandD/overlay.png`
- **Beast**：`Beast/contact.png` `Beast/views/01_front.png` `Beast/views/02_right.png` `Beast/views/03_back.png` `Beast/views/04_left.png` `Beast/views/05_front_right.png` `Beast/views/06_back_right.png` `Beast/views/07_back_left.png` `Beast/views/08_front_left.png` `Beast/views/09_top.png` `Beast/views/10_iso.png` `Beast/film/advance.png` `Beast/film/eat.png` `Beast/film/form_shift.png` `Beast/film/get_up.png` `Beast/film/idle.png` `Beast/film/lie_down.png` `Beast/film/run.png` `Beast/film/walk.png` `Beast/overlay.png`
- **BeastDesert**：`BeastDesert/contact.png` `BeastDesert/views/01_front.png` `BeastDesert/views/02_right.png` `BeastDesert/views/03_back.png` `BeastDesert/views/04_left.png` `BeastDesert/views/05_front_right.png` `BeastDesert/views/06_back_right.png` `BeastDesert/views/07_back_left.png` `BeastDesert/views/08_front_left.png` `BeastDesert/views/09_top.png` `BeastDesert/views/10_iso.png` `BeastDesert/film/advance.png` `BeastDesert/film/eat.png` `BeastDesert/film/form_shift.png` `BeastDesert/film/get_up.png` `BeastDesert/film/idle.png` `BeastDesert/film/lie_down.png` `BeastDesert/film/run.png` `BeastDesert/film/walk.png` `BeastDesert/overlay.png`
- **BeastDesertTier2**：`BeastDesertTier2/contact.png` `BeastDesertTier2/views/01_front.png` `BeastDesertTier2/views/02_right.png` `BeastDesertTier2/views/03_back.png` `BeastDesertTier2/views/04_left.png` `BeastDesertTier2/views/05_front_right.png` `BeastDesertTier2/views/06_back_right.png` `BeastDesertTier2/views/07_back_left.png` `BeastDesertTier2/views/08_front_left.png` `BeastDesertTier2/views/09_top.png` `BeastDesertTier2/views/10_iso.png` `BeastDesertTier2/film/advance.png` `BeastDesertTier2/film/eat.png` `BeastDesertTier2/film/form_shift.png` `BeastDesertTier2/film/get_up.png` `BeastDesertTier2/film/idle.png` `BeastDesertTier2/film/lie_down.png` `BeastDesertTier2/film/run.png` `BeastDesertTier2/film/walk.png` `BeastDesertTier2/overlay.png`
- **BeastGlutton**：`BeastGlutton/contact.png` `BeastGlutton/views/01_front.png` `BeastGlutton/views/02_right.png` `BeastGlutton/views/03_back.png` `BeastGlutton/views/04_left.png` `BeastGlutton/views/05_front_right.png` `BeastGlutton/views/06_back_right.png` `BeastGlutton/views/07_back_left.png` `BeastGlutton/views/08_front_left.png` `BeastGlutton/views/09_top.png` `BeastGlutton/views/10_iso.png` `BeastGlutton/film/advance.png` `BeastGlutton/film/eat.png` `BeastGlutton/film/form_shift.png` `BeastGlutton/film/get_up.png` `BeastGlutton/film/idle.png` `BeastGlutton/film/lie_down.png` `BeastGlutton/film/run.png` `BeastGlutton/film/walk.png` `BeastGlutton/overlay.png`
- **BeastGluttonTier2**：`BeastGluttonTier2/contact.png` `BeastGluttonTier2/views/01_front.png` `BeastGluttonTier2/views/02_right.png` `BeastGluttonTier2/views/03_back.png` `BeastGluttonTier2/views/04_left.png` `BeastGluttonTier2/views/05_front_right.png` `BeastGluttonTier2/views/06_back_right.png` `BeastGluttonTier2/views/07_back_left.png` `BeastGluttonTier2/views/08_front_left.png` `BeastGluttonTier2/views/09_top.png` `BeastGluttonTier2/views/10_iso.png` `BeastGluttonTier2/film/advance.png` `BeastGluttonTier2/film/eat.png` `BeastGluttonTier2/film/form_shift.png` `BeastGluttonTier2/film/get_up.png` `BeastGluttonTier2/film/idle.png` `BeastGluttonTier2/film/lie_down.png` `BeastGluttonTier2/film/run.png` `BeastGluttonTier2/film/walk.png` `BeastGluttonTier2/overlay.png`
- **BeastTier2**：`BeastTier2/contact.png` `BeastTier2/views/01_front.png` `BeastTier2/views/02_right.png` `BeastTier2/views/03_back.png` `BeastTier2/views/04_left.png` `BeastTier2/views/05_front_right.png` `BeastTier2/views/06_back_right.png` `BeastTier2/views/07_back_left.png` `BeastTier2/views/08_front_left.png` `BeastTier2/views/09_top.png` `BeastTier2/views/10_iso.png` `BeastTier2/film/advance.png` `BeastTier2/film/eat.png` `BeastTier2/film/form_shift.png` `BeastTier2/film/get_up.png` `BeastTier2/film/idle.png` `BeastTier2/film/lie_down.png` `BeastTier2/film/run.png` `BeastTier2/film/walk.png` `BeastTier2/overlay.png`
- **Cargo**：`Cargo/contact.png` `Cargo/views/01_front.png` `Cargo/views/02_right.png` `Cargo/views/03_back.png` `Cargo/views/04_left.png` `Cargo/views/05_front_right.png` `Cargo/views/06_back_right.png` `Cargo/views/07_back_left.png` `Cargo/views/08_front_left.png` `Cargo/views/09_top.png` `Cargo/views/10_iso.png` `Cargo/overlay.png`
- **CaveMouth**：`CaveMouth/contact.png` `CaveMouth/views/01_front.png` `CaveMouth/views/02_right.png` `CaveMouth/views/03_back.png` `CaveMouth/views/04_left.png` `CaveMouth/views/05_front_right.png` `CaveMouth/views/06_back_right.png` `CaveMouth/views/07_back_left.png` `CaveMouth/views/08_front_left.png` `CaveMouth/views/09_top.png` `CaveMouth/views/10_iso.png` `CaveMouth/overlay.png`
- **CavernTile**：`CavernTile/contact.png` `CavernTile/views/01_front.png` `CavernTile/views/02_right.png` `CavernTile/views/03_back.png` `CavernTile/views/04_left.png` `CavernTile/views/05_front_right.png` `CavernTile/views/06_back_right.png` `CavernTile/views/07_back_left.png` `CavernTile/views/08_front_left.png` `CavernTile/views/09_top.png` `CavernTile/views/10_iso.png` `CavernTile/overlay.png`
- **Church**：`Church/contact.png` `Church/views/01_front.png` `Church/views/02_right.png` `Church/views/03_back.png` `Church/views/04_left.png` `Church/views/05_front_right.png` `Church/views/06_back_right.png` `Church/views/07_back_left.png` `Church/views/08_front_left.png` `Church/views/09_top.png` `Church/views/10_iso.png` `Church/overlay.png`
- **ChurchDamaged**：`ChurchDamaged/contact.png` `ChurchDamaged/views/01_front.png` `ChurchDamaged/views/02_right.png` `ChurchDamaged/views/03_back.png` `ChurchDamaged/views/04_left.png` `ChurchDamaged/views/05_front_right.png` `ChurchDamaged/views/06_back_right.png` `ChurchDamaged/views/07_back_left.png` `ChurchDamaged/views/08_front_left.png` `ChurchDamaged/views/09_top.png` `ChurchDamaged/views/10_iso.png` `ChurchDamaged/overlay.png`
- **Citizen**：`Citizen/contact.png` `Citizen/views/01_front.png` `Citizen/views/02_right.png` `Citizen/views/03_back.png` `Citizen/views/04_left.png` `Citizen/views/05_front_right.png` `Citizen/views/06_back_right.png` `Citizen/views/07_back_left.png` `Citizen/views/08_front_left.png` `Citizen/views/09_top.png` `Citizen/views/10_iso.png` `Citizen/film/attack_melee.png` `Citizen/film/carry.png` `Citizen/film/death.png` `Citizen/film/hit.png` `Citizen/film/idle.png` `Citizen/film/run.png` `Citizen/film/walk.png` `Citizen/film/work.png` `Citizen/overlay.png`
- **CityNode**：`CityNode/contact.png` `CityNode/views/01_front.png` `CityNode/views/02_right.png` `CityNode/views/03_back.png` `CityNode/views/04_left.png` `CityNode/views/05_front_right.png` `CityNode/views/06_back_right.png` `CityNode/views/07_back_left.png` `CityNode/views/08_front_left.png` `CityNode/views/09_top.png` `CityNode/views/10_iso.png` `CityNode/overlay.png`
- **Corpse**：`Corpse/contact.png` `Corpse/views/01_front.png` `Corpse/views/02_right.png` `Corpse/views/03_back.png` `Corpse/views/04_left.png` `Corpse/views/05_front_right.png` `Corpse/views/06_back_right.png` `Corpse/views/07_back_left.png` `Corpse/views/08_front_left.png` `Corpse/views/09_top.png` `Corpse/views/10_iso.png` `Corpse/overlay.png`
- **Crystal**：`Crystal/contact.png` `Crystal/views/01_front.png` `Crystal/views/02_right.png` `Crystal/views/03_back.png` `Crystal/views/04_left.png` `Crystal/views/05_front_right.png` `Crystal/views/06_back_right.png` `Crystal/views/07_back_left.png` `Crystal/views/08_front_left.png` `Crystal/views/09_top.png` `Crystal/views/10_iso.png` `Crystal/overlay.png`
- **CrystalShard**：`CrystalShard/contact.png` `CrystalShard/views/01_front.png` `CrystalShard/views/02_right.png` `CrystalShard/views/03_back.png` `CrystalShard/views/04_left.png` `CrystalShard/views/05_front_right.png` `CrystalShard/views/06_back_right.png` `CrystalShard/views/07_back_left.png` `CrystalShard/views/08_front_left.png` `CrystalShard/views/09_top.png` `CrystalShard/views/10_iso.png` `CrystalShard/overlay.png`
- **CrystalSocket**：`CrystalSocket/contact.png` `CrystalSocket/views/01_front.png` `CrystalSocket/views/02_right.png` `CrystalSocket/views/03_back.png` `CrystalSocket/views/04_left.png` `CrystalSocket/views/05_front_right.png` `CrystalSocket/views/06_back_right.png` `CrystalSocket/views/07_back_left.png` `CrystalSocket/views/08_front_left.png` `CrystalSocket/views/09_top.png` `CrystalSocket/views/10_iso.png` `CrystalSocket/overlay.png`
- **DistantTower**：`DistantTower/contact.png` `DistantTower/views/01_front.png` `DistantTower/views/02_right.png` `DistantTower/views/03_back.png` `DistantTower/views/04_left.png` `DistantTower/views/05_front_right.png` `DistantTower/views/06_back_right.png` `DistantTower/views/07_back_left.png` `DistantTower/views/08_front_left.png` `DistantTower/views/09_top.png` `DistantTower/views/10_iso.png` `DistantTower/overlay.png`
- **Dormitory**：`Dormitory/contact.png` `Dormitory/views/01_front.png` `Dormitory/views/02_right.png` `Dormitory/views/03_back.png` `Dormitory/views/04_left.png` `Dormitory/views/05_front_right.png` `Dormitory/views/06_back_right.png` `Dormitory/views/07_back_left.png` `Dormitory/views/08_front_left.png` `Dormitory/views/09_top.png` `Dormitory/views/10_iso.png` `Dormitory/overlay.png`
- **DormitoryDamaged**：`DormitoryDamaged/contact.png` `DormitoryDamaged/views/01_front.png` `DormitoryDamaged/views/02_right.png` `DormitoryDamaged/views/03_back.png` `DormitoryDamaged/views/04_left.png` `DormitoryDamaged/views/05_front_right.png` `DormitoryDamaged/views/06_back_right.png` `DormitoryDamaged/views/07_back_left.png` `DormitoryDamaged/views/08_front_left.png` `DormitoryDamaged/views/09_top.png` `DormitoryDamaged/views/10_iso.png` `DormitoryDamaged/overlay.png`
- **FoodNode**：`FoodNode/contact.png` `FoodNode/views/01_front.png` `FoodNode/views/02_right.png` `FoodNode/views/03_back.png` `FoodNode/views/04_left.png` `FoodNode/views/05_front_right.png` `FoodNode/views/06_back_right.png` `FoodNode/views/07_back_left.png` `FoodNode/views/08_front_left.png` `FoodNode/views/09_top.png` `FoodNode/views/10_iso.png` `FoodNode/overlay.png`
- **Gladiator**：`Gladiator/contact.png` `Gladiator/views/01_front.png` `Gladiator/views/02_right.png` `Gladiator/views/03_back.png` `Gladiator/views/04_left.png` `Gladiator/views/05_front_right.png` `Gladiator/views/06_back_right.png` `Gladiator/views/07_back_left.png` `Gladiator/views/08_front_left.png` `Gladiator/views/09_top.png` `Gladiator/views/10_iso.png` `Gladiator/film/attack_melee.png` `Gladiator/film/carry.png` `Gladiator/film/death.png` `Gladiator/film/hit.png` `Gladiator/film/idle.png` `Gladiator/film/run.png` `Gladiator/film/walk.png` `Gladiator/film/work.png` `Gladiator/overlay.png`
- **Granary**：`Granary/contact.png` `Granary/views/01_front.png` `Granary/views/02_right.png` `Granary/views/03_back.png` `Granary/views/04_left.png` `Granary/views/05_front_right.png` `Granary/views/06_back_right.png` `Granary/views/07_back_left.png` `Granary/views/08_front_left.png` `Granary/views/09_top.png` `Granary/views/10_iso.png` `Granary/overlay.png`
- **GranaryDamaged**：`GranaryDamaged/contact.png` `GranaryDamaged/views/01_front.png` `GranaryDamaged/views/02_right.png` `GranaryDamaged/views/03_back.png` `GranaryDamaged/views/04_left.png` `GranaryDamaged/views/05_front_right.png` `GranaryDamaged/views/06_back_right.png` `GranaryDamaged/views/07_back_left.png` `GranaryDamaged/views/08_front_left.png` `GranaryDamaged/views/09_top.png` `GranaryDamaged/views/10_iso.png` `GranaryDamaged/overlay.png`
- **GroundExit**：`GroundExit/contact.png` `GroundExit/views/01_front.png` `GroundExit/views/02_right.png` `GroundExit/views/03_back.png` `GroundExit/views/04_left.png` `GroundExit/views/05_front_right.png` `GroundExit/views/06_back_right.png` `GroundExit/views/07_back_left.png` `GroundExit/views/08_front_left.png` `GroundExit/views/09_top.png` `GroundExit/views/10_iso.png` `GroundExit/overlay.png`
- **GroundPoor**：`GroundPoor/contact.png` `GroundPoor/views/01_front.png` `GroundPoor/views/02_right.png` `GroundPoor/views/03_back.png` `GroundPoor/views/04_left.png` `GroundPoor/views/05_front_right.png` `GroundPoor/views/06_back_right.png` `GroundPoor/views/07_back_left.png` `GroundPoor/views/08_front_left.png` `GroundPoor/views/09_top.png` `GroundPoor/views/10_iso.png` `GroundPoor/overlay.png`
- **GroundRich**：`GroundRich/contact.png` `GroundRich/views/01_front.png` `GroundRich/views/02_right.png` `GroundRich/views/03_back.png` `GroundRich/views/04_left.png` `GroundRich/views/05_front_right.png` `GroundRich/views/06_back_right.png` `GroundRich/views/07_back_left.png` `GroundRich/views/08_front_left.png` `GroundRich/views/09_top.png` `GroundRich/views/10_iso.png` `GroundRich/overlay.png`
- **GroundSand**：`GroundSand/contact.png` `GroundSand/views/01_front.png` `GroundSand/views/02_right.png` `GroundSand/views/03_back.png` `GroundSand/views/04_left.png` `GroundSand/views/05_front_right.png` `GroundSand/views/06_back_right.png` `GroundSand/views/07_back_left.png` `GroundSand/views/08_front_left.png` `GroundSand/views/09_top.png` `GroundSand/views/10_iso.png` `GroundSand/overlay.png`
- **Helper**：`Helper/contact.png` `Helper/views/01_front.png` `Helper/views/02_right.png` `Helper/views/03_back.png` `Helper/views/04_left.png` `Helper/views/05_front_right.png` `Helper/views/06_back_right.png` `Helper/views/07_back_left.png` `Helper/views/08_front_left.png` `Helper/views/09_top.png` `Helper/views/10_iso.png` `Helper/film/attack_melee.png` `Helper/film/carry.png` `Helper/film/death.png` `Helper/film/hit.png` `Helper/film/idle.png` `Helper/film/run.png` `Helper/film/walk.png` `Helper/film/work.png` `Helper/overlay.png`
- **HerdBeast**：`HerdBeast/contact.png` `HerdBeast/views/01_front.png` `HerdBeast/views/02_right.png` `HerdBeast/views/03_back.png` `HerdBeast/views/04_left.png` `HerdBeast/views/05_front_right.png` `HerdBeast/views/06_back_right.png` `HerdBeast/views/07_back_left.png` `HerdBeast/views/08_front_left.png` `HerdBeast/views/09_top.png` `HerdBeast/views/10_iso.png` `HerdBeast/overlay.png`
- **Hotel**：`Hotel/contact.png` `Hotel/views/01_front.png` `Hotel/views/02_right.png` `Hotel/views/03_back.png` `Hotel/views/04_left.png` `Hotel/views/05_front_right.png` `Hotel/views/06_back_right.png` `Hotel/views/07_back_left.png` `Hotel/views/08_front_left.png` `Hotel/views/09_top.png` `Hotel/views/10_iso.png` `Hotel/overlay.png`
- **HotelDamaged**：`HotelDamaged/contact.png` `HotelDamaged/views/01_front.png` `HotelDamaged/views/02_right.png` `HotelDamaged/views/03_back.png` `HotelDamaged/views/04_left.png` `HotelDamaged/views/05_front_right.png` `HotelDamaged/views/06_back_right.png` `HotelDamaged/views/07_back_left.png` `HotelDamaged/views/08_front_left.png` `HotelDamaged/views/09_top.png` `HotelDamaged/views/10_iso.png` `HotelDamaged/overlay.png`
- **LandmarkPoor**：`LandmarkPoor/contact.png` `LandmarkPoor/views/01_front.png` `LandmarkPoor/views/02_right.png` `LandmarkPoor/views/03_back.png` `LandmarkPoor/views/04_left.png` `LandmarkPoor/views/05_front_right.png` `LandmarkPoor/views/06_back_right.png` `LandmarkPoor/views/07_back_left.png` `LandmarkPoor/views/08_front_left.png` `LandmarkPoor/views/09_top.png` `LandmarkPoor/views/10_iso.png` `LandmarkPoor/overlay.png`
- **LandmarkRich**：`LandmarkRich/contact.png` `LandmarkRich/views/01_front.png` `LandmarkRich/views/02_right.png` `LandmarkRich/views/03_back.png` `LandmarkRich/views/04_left.png` `LandmarkRich/views/05_front_right.png` `LandmarkRich/views/06_back_right.png` `LandmarkRich/views/07_back_left.png` `LandmarkRich/views/08_front_left.png` `LandmarkRich/views/09_top.png` `LandmarkRich/views/10_iso.png` `LandmarkRich/overlay.png`
- **LandmarkSand**：`LandmarkSand/contact.png` `LandmarkSand/views/01_front.png` `LandmarkSand/views/02_right.png` `LandmarkSand/views/03_back.png` `LandmarkSand/views/04_left.png` `LandmarkSand/views/05_front_right.png` `LandmarkSand/views/06_back_right.png` `LandmarkSand/views/07_back_left.png` `LandmarkSand/views/08_front_left.png` `LandmarkSand/views/09_top.png` `LandmarkSand/views/10_iso.png` `LandmarkSand/overlay.png`
- **LevitationBase**：`LevitationBase/contact.png` `LevitationBase/views/01_front.png` `LevitationBase/views/02_right.png` `LevitationBase/views/03_back.png` `LevitationBase/views/04_left.png` `LevitationBase/views/05_front_right.png` `LevitationBase/views/06_back_right.png` `LevitationBase/views/07_back_left.png` `LevitationBase/views/08_front_left.png` `LevitationBase/views/09_top.png` `LevitationBase/views/10_iso.png` `LevitationBase/overlay.png`
- **LevitationBaseTier2**：`LevitationBaseTier2/contact.png` `LevitationBaseTier2/views/01_front.png` `LevitationBaseTier2/views/02_right.png` `LevitationBaseTier2/views/03_back.png` `LevitationBaseTier2/views/04_left.png` `LevitationBaseTier2/views/05_front_right.png` `LevitationBaseTier2/views/06_back_right.png` `LevitationBaseTier2/views/07_back_left.png` `LevitationBaseTier2/views/08_front_left.png` `LevitationBaseTier2/views/09_top.png` `LevitationBaseTier2/views/10_iso.png` `LevitationBaseTier2/overlay.png`
- **MiningFace**：`MiningFace/contact.png` `MiningFace/views/01_front.png` `MiningFace/views/02_right.png` `MiningFace/views/03_back.png` `MiningFace/views/04_left.png` `MiningFace/views/05_front_right.png` `MiningFace/views/06_back_right.png` `MiningFace/views/07_back_left.png` `MiningFace/views/08_front_left.png` `MiningFace/views/09_top.png` `MiningFace/views/10_iso.png` `MiningFace/overlay.png`
- **Monster**：`Monster/contact.png` `Monster/views/01_front.png` `Monster/views/02_right.png` `Monster/views/03_back.png` `Monster/views/04_left.png` `Monster/views/05_front_right.png` `Monster/views/06_back_right.png` `Monster/views/07_back_left.png` `Monster/views/08_front_left.png` `Monster/views/09_top.png` `Monster/views/10_iso.png` `Monster/film/attack_melee.png` `Monster/film/death.png` `Monster/film/emerge.png` `Monster/film/hit.png` `Monster/film/idle.png` `Monster/film/run.png` `Monster/film/walk.png` `Monster/overlay.png`
- **MonsterBurrower**：`MonsterBurrower/contact.png` `MonsterBurrower/views/01_front.png` `MonsterBurrower/views/02_right.png` `MonsterBurrower/views/03_back.png` `MonsterBurrower/views/04_left.png` `MonsterBurrower/views/05_front_right.png` `MonsterBurrower/views/06_back_right.png` `MonsterBurrower/views/07_back_left.png` `MonsterBurrower/views/08_front_left.png` `MonsterBurrower/views/09_top.png` `MonsterBurrower/views/10_iso.png` `MonsterBurrower/film/attack_melee.png` `MonsterBurrower/film/death.png` `MonsterBurrower/film/emerge.png` `MonsterBurrower/film/hit.png` `MonsterBurrower/film/idle.png` `MonsterBurrower/film/run.png` `MonsterBurrower/film/walk.png` `MonsterBurrower/overlay.png`
- **OreChunk**：`OreChunk/contact.png` `OreChunk/views/01_front.png` `OreChunk/views/02_right.png` `OreChunk/views/03_back.png` `OreChunk/views/04_left.png` `OreChunk/views/05_front_right.png` `OreChunk/views/06_back_right.png` `OreChunk/views/07_back_left.png` `OreChunk/views/08_front_left.png` `OreChunk/views/09_top.png` `OreChunk/views/10_iso.png` `OreChunk/overlay.png`
- **OreNode**：`OreNode/contact.png` `OreNode/views/01_front.png` `OreNode/views/02_right.png` `OreNode/views/03_back.png` `OreNode/views/04_left.png` `OreNode/views/05_front_right.png` `OreNode/views/06_back_right.png` `OreNode/views/07_back_left.png` `OreNode/views/08_front_left.png` `OreNode/views/09_top.png` `OreNode/views/10_iso.png` `OreNode/overlay.png`
- **Pontoon**：`Pontoon/contact.png` `Pontoon/views/01_front.png` `Pontoon/views/02_right.png` `Pontoon/views/03_back.png` `Pontoon/views/04_left.png` `Pontoon/views/05_front_right.png` `Pontoon/views/06_back_right.png` `Pontoon/views/07_back_left.png` `Pontoon/views/08_front_left.png` `Pontoon/views/09_top.png` `Pontoon/views/10_iso.png` `Pontoon/overlay.png`
- **Restaurant**：`Restaurant/contact.png` `Restaurant/views/01_front.png` `Restaurant/views/02_right.png` `Restaurant/views/03_back.png` `Restaurant/views/04_left.png` `Restaurant/views/05_front_right.png` `Restaurant/views/06_back_right.png` `Restaurant/views/07_back_left.png` `Restaurant/views/08_front_left.png` `Restaurant/views/09_top.png` `Restaurant/views/10_iso.png` `Restaurant/overlay.png`
- **RestaurantDamaged**：`RestaurantDamaged/contact.png` `RestaurantDamaged/views/01_front.png` `RestaurantDamaged/views/02_right.png` `RestaurantDamaged/views/03_back.png` `RestaurantDamaged/views/04_left.png` `RestaurantDamaged/views/05_front_right.png` `RestaurantDamaged/views/06_back_right.png` `RestaurantDamaged/views/07_back_left.png` `RestaurantDamaged/views/08_front_left.png` `RestaurantDamaged/views/09_top.png` `RestaurantDamaged/views/10_iso.png` `RestaurantDamaged/overlay.png`
- **RuinLarge**：`RuinLarge/contact.png` `RuinLarge/views/01_front.png` `RuinLarge/views/02_right.png` `RuinLarge/views/03_back.png` `RuinLarge/views/04_left.png` `RuinLarge/views/05_front_right.png` `RuinLarge/views/06_back_right.png` `RuinLarge/views/07_back_left.png` `RuinLarge/views/08_front_left.png` `RuinLarge/views/09_top.png` `RuinLarge/views/10_iso.png` `RuinLarge/overlay.png`
- **RuinMedium**：`RuinMedium/contact.png` `RuinMedium/views/01_front.png` `RuinMedium/views/02_right.png` `RuinMedium/views/03_back.png` `RuinMedium/views/04_left.png` `RuinMedium/views/05_front_right.png` `RuinMedium/views/06_back_right.png` `RuinMedium/views/07_back_left.png` `RuinMedium/views/08_front_left.png` `RuinMedium/views/09_top.png` `RuinMedium/views/10_iso.png` `RuinMedium/overlay.png`
- **RuinSmall**：`RuinSmall/contact.png` `RuinSmall/views/01_front.png` `RuinSmall/views/02_right.png` `RuinSmall/views/03_back.png` `RuinSmall/views/04_left.png` `RuinSmall/views/05_front_right.png` `RuinSmall/views/06_back_right.png` `RuinSmall/views/07_back_left.png` `RuinSmall/views/08_front_left.png` `RuinSmall/views/09_top.png` `RuinSmall/views/10_iso.png` `RuinSmall/overlay.png`
- **ScaffoldLarge**：`ScaffoldLarge/contact.png` `ScaffoldLarge/views/01_front.png` `ScaffoldLarge/views/02_right.png` `ScaffoldLarge/views/03_back.png` `ScaffoldLarge/views/04_left.png` `ScaffoldLarge/views/05_front_right.png` `ScaffoldLarge/views/06_back_right.png` `ScaffoldLarge/views/07_back_left.png` `ScaffoldLarge/views/08_front_left.png` `ScaffoldLarge/views/09_top.png` `ScaffoldLarge/views/10_iso.png` `ScaffoldLarge/overlay.png`
- **ScaffoldMedium**：`ScaffoldMedium/contact.png` `ScaffoldMedium/views/01_front.png` `ScaffoldMedium/views/02_right.png` `ScaffoldMedium/views/03_back.png` `ScaffoldMedium/views/04_left.png` `ScaffoldMedium/views/05_front_right.png` `ScaffoldMedium/views/06_back_right.png` `ScaffoldMedium/views/07_back_left.png` `ScaffoldMedium/views/08_front_left.png` `ScaffoldMedium/views/09_top.png` `ScaffoldMedium/views/10_iso.png` `ScaffoldMedium/overlay.png`
- **ScaffoldSmall**：`ScaffoldSmall/contact.png` `ScaffoldSmall/views/01_front.png` `ScaffoldSmall/views/02_right.png` `ScaffoldSmall/views/03_back.png` `ScaffoldSmall/views/04_left.png` `ScaffoldSmall/views/05_front_right.png` `ScaffoldSmall/views/06_back_right.png` `ScaffoldSmall/views/07_back_left.png` `ScaffoldSmall/views/08_front_left.png` `ScaffoldSmall/views/09_top.png` `ScaffoldSmall/views/10_iso.png` `ScaffoldSmall/overlay.png`
- **SoilBlock**：`SoilBlock/contact.png` `SoilBlock/views/01_front.png` `SoilBlock/views/02_right.png` `SoilBlock/views/03_back.png` `SoilBlock/views/04_left.png` `SoilBlock/views/05_front_right.png` `SoilBlock/views/06_back_right.png` `SoilBlock/views/07_back_left.png` `SoilBlock/views/08_front_left.png` `SoilBlock/views/09_top.png` `SoilBlock/views/10_iso.png` `SoilBlock/overlay.png`
- **SparePart**：`SparePart/contact.png` `SparePart/views/01_front.png` `SparePart/views/02_right.png` `SparePart/views/03_back.png` `SparePart/views/04_left.png` `SparePart/views/05_front_right.png` `SparePart/views/06_back_right.png` `SparePart/views/07_back_left.png` `SparePart/views/08_front_left.png` `SparePart/views/09_top.png` `SparePart/views/10_iso.png` `SparePart/overlay.png`
- **SpecialMaterial**：`SpecialMaterial/contact.png` `SpecialMaterial/views/01_front.png` `SpecialMaterial/views/02_right.png` `SpecialMaterial/views/03_back.png` `SpecialMaterial/views/04_left.png` `SpecialMaterial/views/05_front_right.png` `SpecialMaterial/views/06_back_right.png` `SpecialMaterial/views/07_back_left.png` `SpecialMaterial/views/08_front_left.png` `SpecialMaterial/views/09_top.png` `SpecialMaterial/views/10_iso.png` `SpecialMaterial/overlay.png`
- **Stable**：`Stable/contact.png` `Stable/views/01_front.png` `Stable/views/02_right.png` `Stable/views/03_back.png` `Stable/views/04_left.png` `Stable/views/05_front_right.png` `Stable/views/06_back_right.png` `Stable/views/07_back_left.png` `Stable/views/08_front_left.png` `Stable/views/09_top.png` `Stable/views/10_iso.png` `Stable/overlay.png`
- **StableDamaged**：`StableDamaged/contact.png` `StableDamaged/views/01_front.png` `StableDamaged/views/02_right.png` `StableDamaged/views/03_back.png` `StableDamaged/views/04_left.png` `StableDamaged/views/05_front_right.png` `StableDamaged/views/06_back_right.png` `StableDamaged/views/07_back_left.png` `StableDamaged/views/08_front_left.png` `StableDamaged/views/09_top.png` `StableDamaged/views/10_iso.png` `StableDamaged/overlay.png`
- **TeleportArray**：`TeleportArray/contact.png` `TeleportArray/views/01_front.png` `TeleportArray/views/02_right.png` `TeleportArray/views/03_back.png` `TeleportArray/views/04_left.png` `TeleportArray/views/05_front_right.png` `TeleportArray/views/06_back_right.png` `TeleportArray/views/07_back_left.png` `TeleportArray/views/08_front_left.png` `TeleportArray/views/09_top.png` `TeleportArray/views/10_iso.png` `TeleportArray/overlay.png`
- **TeleportArrayDamaged**：`TeleportArrayDamaged/contact.png` `TeleportArrayDamaged/views/01_front.png` `TeleportArrayDamaged/views/02_right.png` `TeleportArrayDamaged/views/03_back.png` `TeleportArrayDamaged/views/04_left.png` `TeleportArrayDamaged/views/05_front_right.png` `TeleportArrayDamaged/views/06_back_right.png` `TeleportArrayDamaged/views/07_back_left.png` `TeleportArrayDamaged/views/08_front_left.png` `TeleportArrayDamaged/views/09_top.png` `TeleportArrayDamaged/views/10_iso.png` `TeleportArrayDamaged/overlay.png`
- **TownDeck**：`TownDeck/contact.png` `TownDeck/views/01_front.png` `TownDeck/views/02_right.png` `TownDeck/views/03_back.png` `TownDeck/views/04_left.png` `TownDeck/views/05_front_right.png` `TownDeck/views/06_back_right.png` `TownDeck/views/07_back_left.png` `TownDeck/views/08_front_left.png` `TownDeck/views/09_top.png` `TownDeck/views/10_iso.png` `TownDeck/overlay.png`
- **TownDeckTier2**：`TownDeckTier2/contact.png` `TownDeckTier2/views/01_front.png` `TownDeckTier2/views/02_right.png` `TownDeckTier2/views/03_back.png` `TownDeckTier2/views/04_left.png` `TownDeckTier2/views/05_front_right.png` `TownDeckTier2/views/06_back_right.png` `TownDeckTier2/views/07_back_left.png` `TownDeckTier2/views/08_front_left.png` `TownDeckTier2/views/09_top.png` `TownDeckTier2/views/10_iso.png` `TownDeckTier2/overlay.png`
- **TownHall**：`TownHall/contact.png` `TownHall/views/01_front.png` `TownHall/views/02_right.png` `TownHall/views/03_back.png` `TownHall/views/04_left.png` `TownHall/views/05_front_right.png` `TownHall/views/06_back_right.png` `TownHall/views/07_back_left.png` `TownHall/views/08_front_left.png` `TownHall/views/09_top.png` `TownHall/views/10_iso.png` `TownHall/overlay.png`
- **TownHallDamaged**：`TownHallDamaged/contact.png` `TownHallDamaged/views/01_front.png` `TownHallDamaged/views/02_right.png` `TownHallDamaged/views/03_back.png` `TownHallDamaged/views/04_left.png` `TownHallDamaged/views/05_front_right.png` `TownHallDamaged/views/06_back_right.png` `TownHallDamaged/views/07_back_left.png` `TownHallDamaged/views/08_front_left.png` `TownHallDamaged/views/09_top.png` `TownHallDamaged/views/10_iso.png` `TownHallDamaged/overlay.png`
- **TrainingGround**：`TrainingGround/contact.png` `TrainingGround/views/01_front.png` `TrainingGround/views/02_right.png` `TrainingGround/views/03_back.png` `TrainingGround/views/04_left.png` `TrainingGround/views/05_front_right.png` `TrainingGround/views/06_back_right.png` `TrainingGround/views/07_back_left.png` `TrainingGround/views/08_front_left.png` `TrainingGround/views/09_top.png` `TrainingGround/views/10_iso.png` `TrainingGround/overlay.png`
- **TrainingGroundDamaged**：`TrainingGroundDamaged/contact.png` `TrainingGroundDamaged/views/01_front.png` `TrainingGroundDamaged/views/02_right.png` `TrainingGroundDamaged/views/03_back.png` `TrainingGroundDamaged/views/04_left.png` `TrainingGroundDamaged/views/05_front_right.png` `TrainingGroundDamaged/views/06_back_right.png` `TrainingGroundDamaged/views/07_back_left.png` `TrainingGroundDamaged/views/08_front_left.png` `TrainingGroundDamaged/views/09_top.png` `TrainingGroundDamaged/views/10_iso.png` `TrainingGroundDamaged/overlay.png`
- **TreeBroadleaf**：`TreeBroadleaf/contact.png` `TreeBroadleaf/views/01_front.png` `TreeBroadleaf/views/02_right.png` `TreeBroadleaf/views/03_back.png` `TreeBroadleaf/views/04_left.png` `TreeBroadleaf/views/05_front_right.png` `TreeBroadleaf/views/06_back_right.png` `TreeBroadleaf/views/07_back_left.png` `TreeBroadleaf/views/08_front_left.png` `TreeBroadleaf/views/09_top.png` `TreeBroadleaf/views/10_iso.png` `TreeBroadleaf/overlay.png`
- **TreePine**：`TreePine/contact.png` `TreePine/views/01_front.png` `TreePine/views/02_right.png` `TreePine/views/03_back.png` `TreePine/views/04_left.png` `TreePine/views/05_front_right.png` `TreePine/views/06_back_right.png` `TreePine/views/07_back_left.png` `TreePine/views/08_front_left.png` `TreePine/views/09_top.png` `TreePine/views/10_iso.png` `TreePine/overlay.png`
- **Worker**：`Worker/contact.png` `Worker/views/01_front.png` `Worker/views/02_right.png` `Worker/views/03_back.png` `Worker/views/04_left.png` `Worker/views/05_front_right.png` `Worker/views/06_back_right.png` `Worker/views/07_back_left.png` `Worker/views/08_front_left.png` `Worker/views/09_top.png` `Worker/views/10_iso.png` `Worker/film/attack_melee.png` `Worker/film/carry.png` `Worker/film/death.png` `Worker/film/hit.png` `Worker/film/idle.png` `Worker/film/run.png` `Worker/film/walk.png` `Worker/film/work.png` `Worker/overlay.png`
- **Workshop**：`Workshop/contact.png` `Workshop/views/01_front.png` `Workshop/views/02_right.png` `Workshop/views/03_back.png` `Workshop/views/04_left.png` `Workshop/views/05_front_right.png` `Workshop/views/06_back_right.png` `Workshop/views/07_back_left.png` `Workshop/views/08_front_left.png` `Workshop/views/09_top.png` `Workshop/views/10_iso.png` `Workshop/overlay.png`
- **WorkshopDamaged**：`WorkshopDamaged/contact.png` `WorkshopDamaged/views/01_front.png` `WorkshopDamaged/views/02_right.png` `WorkshopDamaged/views/03_back.png` `WorkshopDamaged/views/04_left.png` `WorkshopDamaged/views/05_front_right.png` `WorkshopDamaged/views/06_back_right.png` `WorkshopDamaged/views/07_back_left.png` `WorkshopDamaged/views/08_front_left.png` `WorkshopDamaged/views/09_top.png` `WorkshopDamaged/views/10_iso.png` `WorkshopDamaged/overlay.png`
- **比例尺同框 · `small`**（0.5 米网格）：`scale_small_side.png` `scale_small_top.png`
- **比例尺同框 · `units`**（1 米网格）：`scale_units_side.png` `scale_units_top.png`
- **比例尺同框 · `town`**（1 米网格）：`scale_town_side.png` `scale_town_top.png`
- **比例尺同框 · `town_damaged`**（1 米网格）：`scale_town_damaged_side.png` `scale_town_damaged_top.png`
- **比例尺同框 · `town_build`**（1 米网格）：`scale_town_build_side.png` `scale_town_build_top.png`
- **比例尺同框 · `back`**（1 米网格）：`scale_back_side.png` `scale_back_top.png`
- **比例尺同框 · `outdoor`**（1 米网格）：`scale_outdoor_side.png` `scale_outdoor_top.png`
- **比例尺同框 · `world`**（1 米网格）：`scale_world_side.png` `scale_world_top.png`
- **比例尺同框 · `bases`**（10 米网格）：`scale_bases_side.png` `scale_bases_top.png`
- **比例尺同框 · `large`**（10 米网格）：`scale_large_side.png` `scale_large_top.png`
