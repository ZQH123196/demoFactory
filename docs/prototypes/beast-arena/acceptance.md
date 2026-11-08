# 垂直切片验收配置与报告

本文是垂直切片的固定证据配置与实测报告。它登记四件事：怎么重跑、量什么、量到了什么、哪些口径与规格不一致。判据原文在 `AGENTS.md` 契约索引与规格「12. 性能」「13. 测试与证据」「15. 已知口径冲突」，本文只引用，不新立。

## 1. 验收环境

| 项 | 规格要求 | 实测机器 | 结论 |
| --- | --- | --- | --- |
| CPU | 四核 | Intel i5-12400F（6 核 12 线程） | 高于规格；CPU 侧结论偏保守 |
| GPU | GTX 750 2GB | NVIDIA GeForce GTX 750（2GB，D3D12） | 与规格一致 |
| 内存 | 8GB | 32GB | 高于规格；内存压力结论偏保守 |
| 分辨率 | 1920×1080 | 1920×1080（无头默认 64×64，脚本设根视口） | 一致 |
| 引擎 | Godot 4.7.2-stable mono | 同 | 一致 |
| 系统 | — | Windows 10.0.26300 | 一致 |

超过规格的 CPU 与内存不冒充「低端机达标」：本文的性能数字只对 GTX 750 那一档负责。渲染后端固定 D3D12，不切换。

## 2. 固定种子与固定输入

所有验收共用种子 **7**。同一条脚本重跑，逻辑步与快照逐字一致。

| 用途 | 脚本 | 步数 | 说明 |
| --- | --- | ---: | --- |
| 键鼠主线（标题屏 → 结算 → 继续经营） | `Tests/Scripts/uitest-mainline-ui.json` | 303 | 全程点界面控件；写路径只走 `click` / `set`；`eval` 只摆前置条件与读数 |
| 逻辑主线（调试桥动词） | `Tests/Scripts/uitest-mainline-live.json` | 258 | 与重放脚本同一条流程，用 `inject` 驱动 |
| L3 重放 | `Tests/Scripts/replay-mainline.json` | — | 逐字断言，`assertLevel=3` |
| 性能：三档负载与 8 倍速 | `Tests/Scripts/uitest-perf-loads.json` | 19 | 30 / 100 / 8× 三个采样窗 |
| 性能：最终防守与粒子压力 | `Tests/Scripts/uitest-perf-final.json` | 93 | 富城最终防守 74 人、粒子峰值与两倍压力 |
| 长跑：75 分钟静置 | `Tests/Scripts/uitest-longrun.json` | 41 | 100 人聚集负载，每 5 分钟一条读数 |
| 工人喂食 | `Tests/Scripts/uitest-npcfeed.json` | 17 | 停靠期点「让工人喂 5 食物」，扣 5 食物、饱食 +2 |
| 暂停屏 | `Tests/Scripts/uitest-pause.json` | 20 | 操作说明 / 重开一局 / 回标题各点一次 |
| 召回 | `Tests/Scripts/uitest-forage.json` | 18 | 饥饿脱队后点「召回觅食」 |
| 魔晶三用途与出售 | `Tests/Scripts/uitest-crystal.json` | 42 | 补魔 / 治疗 / 修理 / 出售各走界面按钮 |
| 其余动词分件 | `uitest-start` / `smoke` / `drag` / `coverage` / `ledger` / `market` / `people` / `reject` / `show` / `battle` / `beast` | — | 见第 7 节的逐动词表 |

一键重跑（窗口模式，出图与真实计时都要窗口）：

```bash
dotnet build DemoFactory.csproj
dotnet run --project src/Tools -- uitest src/Prototypes/BeastArena/Tests/Scripts/uitest-mainline-ui.json --window --wait=180 --godot="C:/Users/ZQH12/Desktop/software/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64.exe"
```

口径：引擎侧的点击是真的按下（`BaseButton.EmitSignal(Pressed)`），不是调用动词；数字参数用 `set` 填界面数量框；`eval` 只摆前置条件（伤害、存量、饥饿），被验的动词仍从按钮进。

## 3. 机位

| 档 | 焦点 | 距离 | 偏航 | 俯角 | 出处 |
| --- | --- | ---: | ---: | ---: | --- |
| 战略档（跟兽） | 巨兽 | 105 米 | 205° | 22° | `Main.LockBeast` / `StrategicDistanceMeters` |
| 战术档（钉人） | 钉住的人 | 26 米 | 205° | 22° | `Main.PinUnit` / `TacticalDistanceMeters` |
| 甲板拾取（摆建筑、点兽） | 巨兽 | 105 米 | 205° | 80° | 脚本 `CameraSetPose(...,105,205,80)` |
| 甲板近景（点格） | 巨兽 | 60 米 | 0° | 60° | 脚本 `CameraSetPose(...,60,0,60)` |

固定点：`StrategicDistanceMeters = 105f`、`TacticalDistanceMeters = 26f`；两档 FOV：战略 75°、战术 53°（`Main.StrategicFovDegrees` / `TacticalFovDegrees`）。俯角是相机层 `PitchDegrees`（水平面以上抬起的角度），两档都是 22°；锁定兽与钉人只改距离、焦点与 FOV，不改俯角。

规格「10.1 画面基准」写「战略档 105 米 / 75°；战术档 26 米 / 53°」，指的是距离 / FOV，与实现一致。俯角 22° 不在规格里约束；脚本出图档的 80° / 60° 是截图机位，不是玩法机位。

## 4. 三档负载

| 档 | 构造 | 活跃人形单位 | 其他 |
| --- | --- | ---: | --- |
| 平时 | 开局 12 人 + 加到 30 名自由人 | 30 | 停靠贫城，不战斗 |
| 聚集峰值 | 加到 100 名自由人 | 100 | 停靠贫城，不战斗 |
| 最终防守 | 富城最终防守（50 名镇民与帮工 + 24 名敌人） | 74 | 二阶甲板、富城环境、粒子峰值 |

8 倍速档在 100 人聚集负载上跑：`SetSpeed(8)`，每现实秒 80 次逻辑推进。

## 5. 采样与统计口径

| 读数 | 定义 | 判据 |
| --- | --- | --- |
| `frame_ms` | 相邻渲染帧的墙钟间隔 | 平均值换 FPS（≥30）；单帧 >100 毫秒记一次卡顿 |
| `step_ms` | 一帧内「推进逻辑步 + 同步甲板」那一段的主线程耗时 | p95 ≤20 毫秒，p99 ≤33.3 毫秒 |
| `process_ms` | `Performance.Monitor.TimeProcess`，含 D3D12 提交与呈现等待 | 只作诊断，不作判据 |
| `steps_per_s` | 每现实秒推进的逻辑步数 | 1 倍速 10，8 倍速 80；按整秒分桶，`low_rate_s` 记低于「期望 −1」的秒数，应为 0 |
| `stutter` | `frame_ms > 100` 的帧数 | 只记录，含加载帧的窗口要单列 |
| `nodes` / `resources` / `mem_mb` | 场景节点数、引擎资源数、进程工作集 | 长跑预热后增幅 ≤5% |
| `vfx_peak_emitters` / `vfx_peak_particles` | 采样窗内同时在场的发射器与申请粒子数峰值 | 两倍峰值压力窗必须真的到两倍 |

采样窗：三档负载各 30 秒（8 倍速 20 秒）；最终防守 20 秒；粒子压力 20 秒。开始采样前先 `perf_reset`；每次加载（放人、拔锚、开压测发射器）都不在采样窗内。每帧只计一次逻辑耗时，累加器一次推多步时按一次计。

口径说明：规格写「p95 物理帧时间」，本作没有刚体，`Performance.Monitor.TimePhysicsProcess` 只反映主循环的抖动。判定分两套：主判用 `step_ms`（一帧内逻辑推进加甲板同步），副判用 `frame_ms`（渲染帧间隔）；两套套同一门槛（p95 ≤20 毫秒、p99 ≤33.3 毫秒）。最终构建两套都过。

## 6. 性能实测（种子 7，窗口模式）

命令：

```bash
dotnet run --project src/Tools -- uitest src/Prototypes/BeastArena/Tests/Scripts/uitest-perf-loads.json --window
dotnet run --project src/Tools -- uitest src/Prototypes/BeastArena/Tests/Scripts/uitest-perf-final.json --window
```

下表是 2026-10-27 地形精修前的最终构建读数；地形精修后的复测见本节末段。

| 窗口 | 平均 FPS | frame p95 / p99 | step p95 / p99 | 卡顿 | steps/s | 节点 | 内存 | 结论 |
| --- | ---: | --- | --- | ---: | --- | ---: | ---: | --- |
| 30 人 30 秒 | 59.8 | 16.67 / 16.67 | 1.02 / 1.43 | 1（117ms，加载帧） | 10.0 | 1181 | 184.4MB | 过 |
| 100 人 30 秒 | 60.0 | 16.67 / 16.67 | 2.20 / 3.13 | 0 | 10.0 | 1811 | 196.8MB | 过 |
| 100 人 8 倍速 20 秒 | 59.9 | 16.67 / 18.05 | 6.25 / 14.90 | 0 | 80.0（最小 80，低速率 0 秒） | 1811 | 195.8MB | 过 |
| 最终防守 74 人 20 秒 | 59.7 | 16.67 / 16.67 | 2.39 / 5.00 | 1（142ms） | 10.0 | 1600 | 203.3MB | 过 |
| 粒子两倍峰值 20 秒 | 59.9 | 16.67 / 16.67 | 2.32 / 4.20 | 0 | 10.0 | 1561 | 202.6MB | 过 |

补充读数：

- 粒子自然峰值：2 个发射器 / 48 个申请粒子。两倍压力：3 个发射器 / 144 个申请粒子（96 个压力发射器 + 48 个自然），确实到两倍以上。
- 最终防守前的加载窗（加 50 人、拔锚、开战，20.2 秒）单列：57.7 FPS，7 次卡顿，单帧最大 148 毫秒。属加载，不入判据。
- 8 倍速达标不靠暂停渲染：窗口内 59.9 FPS，`steps_per_s` 每秒都是 80，`low_rate_s=0`。
- 内存 184.4 → 203.3MB 是不同负载之间的差，不是长跑增长；长跑的增长判据见第 10 节。
- 五个窗都在最终提交的构建上重跑。离镇游客的模型节点回收之后，最终防守与粒子窗的节点数从 3387 级降到 1600 级，平均 FPS 从 55.5 升到 59.7。外部 GPU 占用（用户在同机开游戏）会压低读数；第 6 节这一批在无外部占用时测得。

地形精修后复测（2026-10-28，窗口模式，种子 7，最终代码，日志 `.cache/terrain-polish/perf-loads-rerun.log` 与 `perf-final-rerun.log`）：

| 窗口 | 平均 FPS | frame p95 / p99 | step p95 / p99 | 卡顿 | steps/s | 节点 | 内存 | 结论 |
| --- | ---: | --- | --- | ---: | --- | ---: | ---: | --- |
| 30 人 30 秒 | 59.8 | 16.67 / 16.67 | 1.39 / 1.67 | 1（100ms，加载帧） | 10.0 | 1181 | 184.3MB | 过 |
| 100 人 30 秒 | 59.8 | 16.67 / 18.06 | 2.53 / 3.84 | 0 | 10.0 | 1811 | 196.6MB | 过 |
| 100 人 8 倍速 20 秒 | 59.8 | 16.67 / 18.06 | 6.44 / 16.54 | 0 | 80.0（低速率 0 秒） | 1811 | 195.9MB | 过 |
| 最终防守 74 人 20 秒 | 55.3 | 18.18 / 20.83 | 1.57 / 3.86 | 9（单帧最大 150ms） | 10.0 | 1588 | 199.4MB | 过 |
| 粒子自然 20 秒 | 57.9 | 18.06 / 25.71 | 1.78 / 3.76 | 1 | 10.0 | 1600 | 199.8MB | 过 |
| 粒子两倍峰值 20 秒 | 59.8 | 16.67 / 16.67 | 2.06 / 3.70 | 0 | 10.0 | 1570 | 199.2MB | 过 |

- 最终防守窗帧 p99 20.83、step p99 3.86，都在门槛内；`stutter` 按旧口径只记录。同一窗的 `max_process_ms=8950` 是采样窗边缘的着色器 / 纹理上传，属诊断读数，不参与判据。
- 最终防守窗与粒子自然窗的平均 FPS 比地形精修前低：最终防守 59.7 → 55.3（低 4.4），粒子自然 59.7 → 57.9（低 1.8），仍高于 30 FPS 门槛。地形贴图常驻显存是候选原因，未单独归因。
- 粒子自然峰值仍是 2 个发射器 / 48 个申请粒子；两倍压力到 3 个发射器 / 144 个粒子。
- 节点、资源与内存与旧窗同档：节点 1181 / 1811 / 1588 / 1600 / 1570，资源 464–509，内存 184.3–199.8MB。地形三张图在启动期一次性生成，不进逻辑，也不进逐帧分配。
- 长跑 75 分钟未重跑：地形是静态贴图，不参与逻辑与逐帧分配；旧长跑的节点 / 内存增长判据仍成立，绝对内存基线见上表。

## 7. 主线动词覆盖矩阵

界面入口表是 `Scripts/Logic/UiEntryPoints.cs`；下表逐条给出实测脚本与步号。「自动」一条由世界触发，没有玩家入口。

| 动词 | 界面入口 | 证据（脚本:步） |
| --- | --- | --- |
| StartRun | 标题屏「开始游戏」 | mainline-ui:2 |
| Dock | 命令栏「停靠」→ 目的地 | mainline-ui:9/150/236；start:4 |
| SailTo | 右键点地图空地 | mainline-ui:4（真实右键事件，`manual=` 读回） |
| PullAnchor | 命令栏「拔锚」 | mainline-ui:107/232/272；show:12；beast:14 |
| StartMatch | 竞技场「开赛」 | mainline-ui:78/209；people:25 |
| TradeAirship | 经营 → 市场 →「成交」 | mainline-ui:23/177/249；market:10 |
| SteerOutOfBounds | 自动（出界转向） | 无玩家入口；逻辑测试覆盖 |
| GrabEntity | 按住镇民 / 尸体 / 散货拖动 | drag:8/13 |
| DropToBuilding | 拖到建筑上松手 | drag:10 |
| DropToDeck | 拖到甲板空地松手 | drag:15 |
| LiftBuilding | 建筑菜单「拔起」 | drag:22 |
| DropBuilding | 拔起后点甲板格落位 | drag:24 |
| SummonHelper | 人员与帮工 →「召唤」 | reject:11–22；people:15 |
| DismissHelper | 人员与帮工 →「遣散」 | people:19 |
| CastHeal | 选中面板「治疗 1 法力」 | battle:27 |
| PlaceBuilding | 蓝图条 → 甲板落座 | mainline-ui:32；drag:26；people:21 |
| RemoveBuilding | 建筑菜单「拆除」 | mainline-ui:260；drag:28 |
| RepairBuilding | 建筑菜单「修理」 | coverage:13；crystal:25 |
| ExcavateSoil | 土壤面板「开挖」 | mainline-ui:83/86；battle:8/11 |
| EmbedCrystal | 土壤面板「嵌入魔晶」 | mainline-ui:186；battle:23 |
| Recruit | 镇政府「招募」 | mainline-ui:45；people:8 |
| ConvertRole | 选中面板「转角斗」 | people:10 |
| GrantFreedom | 选中面板「给自由」 | people:12 |
| SellStar | 选中面板「卖明星」 | coverage:19 |
| AssignPost | 土壤面板「派到此格」 | mainline-ui:91/95/99；drag:20 |
| SetNonCombatant | 选中面板「不参战」 | people:14 |
| ReviveCorpse | 教堂「救人」 | beast:18 |
| SetPricing | 账本「应用」 | mainline-ui:66；ledger:11 |
| BuySupply | 市场「采购」 | mainline-ui:27；market:11 |
| PayDebt | 账本「还清」 | coverage:26 |
| SetDebtPolicy | 账本策略三选一 | ledger:12；smoke:4 |
| PayWages | 账本「发薪」 | mainline-ui:69/268；ledger:13 |
| Teleport | 传送阵「传送」 | people:23 |
| AdvanceTier | 巨兽面板「进阶」 | mainline-ui:219；coverage:37 |
| SwitchForm | 巨兽面板形态三选一 | mainline-ui:196；beast:7 |
| Feed | 巨兽面板「喂 50 食物」 | mainline-ui:130–144；beast:4 |
| NpcFeed | 巨兽面板「让工人喂 5 食物」 | npcfeed:13 |
| EatTarget | 命令栏「吞食」→ 目标行 | mainline-ui:154–155/162–163；forage:13（开面板） |
| RecallForage | 吞食面板「召回觅食」 | forage:15 |
| UseCrystal | 巨兽面板「补魔 / 治疗 / 修理」 | crystal:13/18/32 |
| SellCrystal | 市场「出售魔晶」 | crystal:39 |
| ClearCave | 土壤面板「清剿」 | mainline-ui:102；battle:14 |
| RetreatCave | 同颗按钮变「撤退」 | battle:16 |
| RetryFinalDefence | 战报「立即重试」 | mainline-live:196 |
| ContinueBusiness | 结算「继续经营」 | mainline-ui:295；mainline-live:223 |

## 8. 六项手感：可判定证据

| 手感项 | 判定证据 | 证据位置 |
| --- | --- | --- |
| 建筑搬得到位 | 蓝图条选槽 → 甲板格落座 → 在册断言 + 出图 | mainline-ui:32（AirshipDock 在册）；`T23/ui-poor-dock.png`、`T23/ui-tier2.png` |
| 说得出巨兽为什么跑 | 吞食面板顶部状态行给出饥饿 / 停靠 / 战斗 / 冷却的原因；召回把航程打回返航 | forage:13–17（`MAP phase=返航`）；面板文案 `ForagePanel.StatusText` |
| 说得出当前位置与下一站 | 头部 `beast=x,z`、`phase=`；航海段 `MAP target= waypoint= manual=`；下一站与里程碑 `next=` | mainline-ui:6（`manual=`）；每次 `WorldReadout` |
| 开挖、回长与嵌入读得懂 | 土壤段 `SOIL` 三档；嵌入后 `embedded=1`；回长进度在土壤面板 | mainline-ui:83/86/186；`T18/cave-cleared.png`、`T19/embedded.png` |
| 人物职业与行为读得出 | 活物段 `U <id> role= state= post= pos= moving=`；选中面板与名牌 | mainline-ui:91–99（派岗断言 `miners=3/3`）；`T18/poor-dock.png` |
| 拔锚与倒计时读得出 | 命令栏防守倒计时文本；战报；最终重试 | mainline-ui:107/232/272；`T18/poor-defence-start.png`、`T20/final-defence-failed.png` |

## 9. 键鼠主线（T23）

`uitest-mainline-ui.json` 在地形精修前的构建上连跑两次，每次 303 步全过（T23/T24，`.cache/beast-arena-vs/T23/mainline-ui.log` 与 `T24/mainline-ui-rerun.log`）。地形精修后的最终构建上重跑原始脚本 3 次：2 次在第 183 步（沙城「仓库里有 1 单位魔晶」）失败，1 次 303 步全过（`.cache/terrain-polish/mainline-rerun.log`、`mainline-rerun2.log`、`mainline-rerun3.log`）。带诊断读数的同流程另跑 5 次，全部走完 303 / 304 步（`.cache/terrain-polish/diag-*.log`）；能走完的运行在等待窗开头读到 `free=6 posted=5 wounded=0 dead=0`，失败运行没有留下同一份读数，因此不能断定是缺工人还是搬运卡住。失败运行都正常停进程、端口 7777 复认无监听，引擎日志 `SCRIPT ERROR` / `ERROR` 均为 0。窗口模式；脚本里的 `wait` 按逻辑步推进，不是 1× 实时。

流程：标题屏 → 贫城停靠（市场成交 / 采购 / 摆 AirshipDock / 招募 / 定价 / 发薪）→ 竞技场 → 挖两层土 / 三人派岗 / 清剿洞穴 / 贫城防守 → 喂食 8 次 / 航海吞食两处矿 → 回港交易 → 嵌入魔晶 → 暴食形态 / 二阶 / 沙地开赛 / 贫城防守 → 富城交易 / 拆除餐馆 / 发薪 → 最终防守守住 → 结算 → 命令栏「结算」回看 → 继续经营（`flow=freeplay`）。

截图（8 张，`.cache/beast-arena-vs/T23/`）：`ui-poor-dock.png`、`ui-sand-glutton.png`、`ui-tier2.png`、`ui-rich-deck.png`、`ui-final-defence.png`、`ui-settlement.png`、`ui-continued.png`、`ui-pause.png`。

本轮最终防守直接守住，没有触发战报「立即重试」；重试路径由 `uitest-mainline-live.json` 第 196 步的界面点击覆盖。暂停屏三条（操作说明 / 重开一局 / 回标题）由 `uitest-pause.json` 20 步单独覆盖（日志 `.cache/beast-arena-vs/T23/pause.log`，0 错误）。全程点击与数量框输入都走界面控件，`eval` 只摆前置条件（推进到目标 tick、填伤害与存量）。

## 10. 长跑（75 分钟）

本节是 2026-10-27 地形精修前那一次长跑；地形精修后未重跑（静态贴图，不进逐帧分配），理由与新的性能窗见第 6 节末段。

`uitest-longrun.json` 41 步全过：开局 → 停靠贫城 → 加 100 名自由人 → 每 5 分钟一条 `PerfReadout` → 自停进程并复认端口无监听。日志 `.cache/beast-arena-vs/T22/longrun.log`；引擎 `^ERROR` / `SCRIPT ERROR` 计 0。

| 分钟 | FPS | 卡顿（累计） | 低速率秒 | 节点 | 资源 | 内存 | 在镇单位 |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 5 | 59.1 | 1 | 0 | 1946 | 481 | 200.1MB | 115 |
| 10 | 59.5 | 1 | 0 | 1811 | 465 | 197.8MB | 100 |
| 15 | 59.6 | 2 | 0 | 1811 | 465 | 198.0MB | 100 |
| 20 | 59.3 | 2 | 0 | 1811 | 465 | 197.9MB | 100 |
| 25 | 55.7 | 5 | 0 | 1955 | 481 | 199.6MB | 116 |
| 30 | 51.1 | 6 | 0 | 1811 | 467 | 197.8MB | 100 |
| 35 | 47.8 | 8 | 0 | 1811 | 465 | 197.8MB | 100 |
| 40 | 45.6 | 9 | 0 | 1811 | 465 | 198.0MB | 100 |
| 45 | 45.2 | 10 | 0 | 1811 | 465 | 198.8MB | 100 |
| 50 | 46.5 | 11 | 0 | 1919 | 481 | 200.2MB | 112 |
| 55 | 47.5 | 12 | 0 | 1811 | 465 | 198.8MB | 100 |
| 60 | 48.6 | 12 | 0 | 1811 | 465 | 198.8MB | 100 |
| 65 | 49.4 | 12 | 0 | 1811 | 465 | 198.9MB | 100 |
| 70 | 50.0 | 13 | 0 | 1811 | 465 | 198.8MB | 100 |
| 75 | 50.7 | 13 | 0 | 1820 | 481 | 199.1MB | 101 |

结论：

- **节点与内存不随时间长**。以 10 分钟稳定态为基线（100 人、无游客）：节点 1811 → 1820（+0.5%），内存 197.8 → 199.1MB（+0.66%），都低于 5%。
- 每日游客波把节点抬到 1919–1955、内存抬到 199.6–200.2MB；5 分钟内回落到 1811 / 198.8MB。末刻正好落在第 4 天游客波上（101 人），所以节点 1820 而不是 1811。
- 逻辑无失速：15 个窗口都是 `steps_per_s=10.0`、`low_rate_s=0`、`max_steps_per_frame` 1–2。
- FPS 从 25 分钟起降到 45–50：同一台机器上另有一个游戏进程占用约 50% GPU（17:14 启动）。前 20 分钟无外部占用时 59.1–59.6 FPS。这一段只用于内存、节点、稳定性判据；FPS 判据取第 6 节的独立窗口。
- 13 次卡顿里，1 次是 5 分钟窗的加载帧，其余集中在外来 GPU 占用之后；全程无崩溃、无未处理错误。

## 11. 重跑一致性

地形精修前（2026-10-27）的 T23/T24 两次完整主线都落盘，两次都是 **303 步全过**、末读数一致：`Day 3 06:31`、`结算=285`。地形精修后（2026-10-28）在最终构建上重跑，结果不一致：

- 原始脚本 3 次：2 次在第 183 步（沙城魔晶入库）失败，1 次 303 步全过、末读数仍是 `Day 3 06:31`、`结算=285`。
- 带诊断读数的同流程 5 次全部走完；通过时等待窗开头读到 `free=6 posted=5`，失败态未取到。
- 失败与通过的差别落在帧序：第 176 步的 tick 只差 1–3 步，第 183 步的仓库读数就分成 1 与 0。地形改动不碰逻辑；这条主线按真实帧推进，不能用逐帧假设。
- 逻辑层的逐字一致性仍由 L3 重放单独负责：`replay --report` 输出「重放无分歧」（`.cache/beast-arena-vs/T24/replay-baseline.log`），该基线不看表现层帧序。
- 通过时的诊断读数里 `posted=5`；本脚本只派过 `soil:0_2`，而该格只有 3 个采矿岗。`AssignPost` 对土壤岗没有满岗拒派，`Soil.Miners` 只把产量截到 3，多出的 2 人既不算采矿也不能领搬运单。这条与第 183 步失败可能相关，未在本次改动里处理。
- 键鼠主线这一道门因此**未稳定通过**；根因待定，不把它算进地形精修的完成判据。

## 12. 已知口径冲突核对

规格「15. 已知口径冲突」四条逐条核对：

| 冲突 | 现状 | 结论 |
| --- | --- | --- |
| 粪便链：旧决策写「吞食产生粪便、粪便改良土壤」，后续规则删除 | 代码里已无粪便点位物，只剩一条「粪便已删」的防回退测试；消化产出魔晶、魔晶嵌入土壤 | 施工按新规则，冲突 1 已闭合 |
| `design.md` 旧章节六处旧口径不追改 | 旧章节保留；垂直切片以「10.2 垂直切片」为准 | 按规格保留，冲突 2 成立且已声明 |
| 现役代码仍有旧土壤层数 / 旧洞穴怪数 / 三日停摆 / 旧形态系数 / 粪便物 | 两层土壤、单洞穴四只原生怪、城破当格解救记债（`StandstillUntilTick` 恒 0）、暴食 ×1.5、粪便已删 | 冲突 3 描述的旧实现已被 T02–T05 取代；这一条现在只剩历史意义 |
| 结算的「现实耗时」不进逻辑确定性状态 | `RunTelemetry` 在 `Scripts/Ui/`，只订阅流程事件；`FinalSnapshot` 不含现实秒；L3 不比较该字段 | 冲突 4 成立，实现与之一致 |
| 规格「10.1」机位 75° / 53° | 指的是 FOV：实现战略 75°、战术 53°，与规格一致。俯角 22° 是相机层参数，规格未约束 | 已核一致，无需裁决 |
| 规格「性能」写「p95 物理帧时间 ≤ 20 毫秒」 | 本作没有刚体，`TimePhysicsProcess` 不反映逻辑；报告以 `step_ms` 主判、`frame_ms` 副判。最终构建两套判法的 p95 / p99 都在线内（frame p95 16.67、p99 ≤18.05） | 已核一致，无需裁决 |

## 13. 重跑全部证据

```bash
# 1. 构建与测试
dotnet build DemoFactory.csproj
dotnet test src/Prototypes/BeastArena/Tests --logger trx
dotnet test src/GugaFramework/Tests --logger trx

# 2. L3 重放（离线，逐字）
dotnet run --project src/Tools -- replay BeastArena src/Prototypes/BeastArena/Tests/Scripts/replay-mainline.json --report

# 3. 键鼠主线与分件（窗口）
for f in uitest-mainline-ui uitest-mainline-live uitest-npcfeed uitest-pause uitest-forage uitest-crystal \
         uitest-start uitest-smoke uitest-drag uitest-coverage uitest-ledger uitest-market \
         uitest-people uitest-reject uitest-show uitest-battle uitest-beast; do
  dotnet run --project src/Tools -- uitest src/Prototypes/BeastArena/Tests/Scripts/$f.json --window
done

# 4. 性能与长跑（窗口）
dotnet run --project src/Tools -- uitest src/Prototypes/BeastArena/Tests/Scripts/uitest-perf-loads.json --window
dotnet run --project src/Tools -- uitest src/Prototypes/BeastArena/Tests/Scripts/uitest-perf-final.json --window
dotnet run --project src/Tools -- uitest src/Prototypes/BeastArena/Tests/Scripts/uitest-longrun.json --window

# 5. 音效资产（逐字节）
dotnet run --project src/Tools -- audiogen BeastArena . verify
```

证据落 `.cache/beast-arena-vs/`（不入库）：T17 两档截图、T18–T20 三段体验截图、T22 性能与长跑日志、T23 键鼠主线日志与截图、T24 回归日志与 trx、T28 工人喂食记录。音效技术样本与响度报告见 `docs/prototypes/beast-arena/audio-review.md`。

## 14. 证据索引与移交

### 14.1 按体验段

| 段 | 测试与重放 | 截图 | 操作记录 |
| --- | --- | --- | --- |
| T17 两档机位与九类事件 | `ShowCueTests` 13 条；`uitest-show` 32 步全过 | `T17/strategic-dock`、`strategic-defence`、`strategic-combat`、`tactical-defence`、`tactical-combat`（两档已确认不同） | `T17/show.log` |
| T18 贫城、首航与暴食态 | `mainline-live`；`replay-mainline` L3 | `T18/poor-dock`、`cave-cleared`、`glutton-form`、`poor-defence-start`、`poor-defence-won` | `mainline-ui:9–157`（同一流程的键鼠版） |
| T19 沙城、二阶与次航 | `mainline-live`；`replay-mainline` L3 | `T19/sand-dock`、`tier2`、`embedded`、`cave-recleared` | `mainline-ui:150–238` |
| T20 富城、最终防守与结算 | `mainline-live`；`replay-mainline` L3 | `T20/rich-dock`、`final-defence-pressure`、`final-defence-failed`、`final-retry`、`final-victory`、`settlement`、`business-continued`、`settlement-revisit`、`normal-defence-after-continue` | `mainline-ui:236–303`；`mainline-live:196`（重试点击） |
| T23 键鼠主线 | `uitest-mainline-ui` 303 步 ×2（地形精修前） | `T23/ui-poor-dock`、`ui-sand-glutton`、`ui-tier2`、`ui-rich-deck`、`ui-final-defence`、`ui-settlement`、`ui-continued`、`ui-pause` | `T23/mainline-ui.log`、`T24/mainline-ui-rerun.log` |
| T28 工人喂食 | `NpcFeedTests`；`uitest-npcfeed` 17 步全过 | — | `T28/npcfeed-main.log` |

### 14.2 按验收门

| 门 | 结果 | 证据 |
| --- | --- | --- |
| 构建 | `dotnet build DemoFactory.csproj` 0 错 0 警 | 控制台输出 |
| 全量测试 | 六个测试工程 1898 过 3 跳过 0 红（BeastArena 530）；地形精修后重跑 BeastArena 530 / GugaFramework 736+1 | `T24/trx/`、第 13 节命令 |
| L3 重放 | 「重放无分歧」 | `T24/replay-baseline.log`；`replay-mainline.json` |
| 性能 | 第 6 节五窗全过（30 / 100 / 8× / 74 / 粒子两倍）；地形精修后复测见第 6 节末段 | `T22/perf-loads.log`、`T22/perf-final.log`、`.cache/terrain-polish/` |
| 长跑 | 第 10 节；地形精修后未重跑（静态贴图，不进逐帧分配） | `T22/longrun.log` |
| 键鼠主线 | 第 9 节；覆盖矩阵第 7 节 45/45；地形精修前两次 303 步全过；地形精修后原始脚本 3 次里 2 次在第 183 步失败、1 次全过；诊断版 5 次走完。**门未稳定通过** | `T23/mainline-ui.log`、`T24/mainline-ui-rerun.log`、`.cache/terrain-polish/mainline-rerun*.log` |
| 六项手感 | 第 8 节六条都有可判定读数 | 同上 |
| 暂停屏 | 继续 / 操作说明 / 设置 / 重开一局 / 回标题 | `T23/pause.log`、`T23/ui-pause.png`、`FrontEndTests` |
| 音效 | 四段技术样本与响度 / 峰值报告 | `audio-review.md`「四段固定技术样本」与响度表 |
| 资产 | 88 个模型 88 PASS 0 FAIL | `model-review.md`；管线 `asset-pipeline.md` |
| 地形 | 程序化颜色 / 法线 / 粗糙度 + 不透明圆盘；28 步出图脚本全过；运行日志 0 SCRIPT ERROR / 0 ERROR | 第 15 节；`.cache/terrain-polish/` |
| 已知口径冲突 | 四条逐条结论；机位与 p95 两条已核实一致 | 第 12 节 |

### 14.3 移交

- 重跑全部证据：按第 13 节五组命令；第 1、2 组离线可跑，第 3、4 组要窗口模式，第 4 组的长跑要 75 分钟，开跑前查一次日志速率，结束后按端口属主 PID 停进程并复认无监听。地形出图另跑 `uitest-terrain.json`（窗口），命令见第 15 节。
- 证据目录 `.cache/beast-arena-vs/` 与 `.cache/terrain-polish/` 不入库；本报告与 `design.md`、`audio-review.md`、`asset-pipeline.md`、`model-review.md` 入库。
- 未裁决事项：键鼠主线在最终构建上的帧序相关偶发失败（第 9、11 节）。地形精修见第 15 节。规格「已知口径冲突」四条逐条核对见第 12 节；机位与 p95 两条已核实一致，早前记的「俯角 22° vs 75° / 53°」是把 FOV 读成俯角，已更正。

## 15. 地形精修（2026-10-28）

灰盒期四类纯色贴图换成程序化颜色、法线、粗糙度三张图；方形透明面换成不透明圆盘。逻辑地形表、寻路、挡路与移速未改。

| 项 | 结果 |
| --- | --- |
| 颜色贴图 | 1024 × 1024（2 像素 = 1 米）；草地深浅斑块与草点、沙漠沙丘带、道路车辙与碎石边、水波纹与岸线 |
| 法线 / 粗糙度 | 512 × 512（1 像素 = 1 米）；水 roughness 0.14–0.20，其余 0.82–0.97 |
| 圆盘 | 128 段三角扇，半径 256 米，与侧壁同半径；不透明材质，吃方向光阴影 |
| 确定性 | 噪声固定种子 20261028；同条件重跑逐像素一致 |
| 出图 | `.cache/terrain-polish/after-strategic.png`、`after-lake.png`、`after-lake-close.png`、`after-desert.png`、`after-road.png`、`after-topdown.png` |
| 运行 | `uitest-terrain.json` 28 步全过；`.cache/Runtime/run-7777.log` 的 `SCRIPT ERROR` / `ERROR` 均为 0；进程已停、端口 7777 无监听 |
| 测试 | `dotnet build` 0 警告 0 错误；BeastArena 530 过 0 红；GugaFramework 736 过 1 跳过 0 红；L3 重放无分歧 |
| 性能 | 第 6 节末段六窗全过；最终防守 55.3 FPS、step p99 3.86ms；粒子两倍 3 发射器 / 144 粒子 |

重跑命令（窗口模式；脚本已入库）：

```bash
dotnet run --project src/Tools -- uitest src/Prototypes/BeastArena/Tests/Scripts/uitest-terrain.json --window --wait=180 --godot="C:/Users/ZQH12/Desktop/software/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64.exe"
```

机位：战略 105 米 / 湖面 105 米 / 湖面近景 60 米 / 沙漠 105 米 / 道路 105 米 / 俯视 180 米；俯角依次 22° / 22° / 35° / 22° / 22° / 70°。
