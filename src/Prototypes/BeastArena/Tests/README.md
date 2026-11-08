# Tests/ — 巨兽竞技场

> 本文不写约束句（「约束唯一出处」）；约束句一律写成 契约名 指针，此处只留事实、机制与操作步骤。

纯 C# 逻辑层的固定种子测试住这里。骨架从 `src/Prototypes/_Template/Tests/` 整份复制而来，
结构与边界的完整理由见那份 README 与 `src/Prototypes/InfinityLord/Tests/README.md`。

```bash
dotnet test src/Prototypes/BeastArena/Tests                 # 全跑
```

「对照实验」那个 fixture **当前不在这个测试工程里**。160 场版曾恢复并形成历史结论，
后来随旧切片退役。下方保留 3 场阶段的历史决策，不是当前入口；按名字过滤匹配不到任何用例。

读失败输出一律走 trx（「失败读 trx」）：

```bash
dotnet test src/Prototypes/BeastArena/Tests --logger trx
```

## 世界起点：ArenaWorldBuilder（测试专用）

测试不再各写 `NewWorld` / `DockAt` / `DigThrough` / `StockUp`。起点统一从
`ArenaWorldBuilder` 来：`New(seed)` 给基线，`WithGranaryFood` / `WithSatiation` /
`WithSpareParts` 覆盖开局数，`Build()` 交回真的 `ArenaWorld`。

建出来之后，高频起点是 `ArenaWorld` 上的测试扩展方法：

- `world.DockedAt(cityId)`：真实航程（`OrderSailTo` + 推进到停稳）。
- `world.DockedDirect(place)`：直跳停靠；只给确实要跳过航程的用例。
- `world.DugTo(sector, layer)`：逐层开挖，每层推一游戏小时。
- `world.Stocked()`：三本账备到够付二阶与三阶。

`With...` 只在建世界之前有效；建出来之后再调会当场抛。builder 不碰生产代码、不反射、
不走 `ArenaSetup.Inject`；要验动词路径的用例仍显式调 `ArenaSetup.Inject`。
断言留在测试里，一次性长链（防守、债、尸首）也留在测试里。

## uitest 脚本：无头跑界面那一路（2026-10-21 ticket 10，2026-10-24 补齐四条）

`Scripts/` 下脚本，走调试桥在**无头**实例里点真控件。跑法：

```bash
dotnet run --project src/Tools -- uitest src/Prototypes/BeastArena/Tests/Scripts/uitest-<名>.json --port 78xx --godot=<exe> --scene=res://src/Prototypes/BeastArena/Scenes/Main.tscn
```

`uitest-mainline-live.json` 与 `uitest-terrain.json` 要加 `--window`：两者负责出图，无头没有渲染器。其余脚本按上面的无头跑法即可。

| 脚本 | 步数 | 测什么 |
|---|---|---|
| `uitest-start.json` | 6 | 标题屏开局那一按（`StartRun`）+ 推进 + 段与未停靠 |
| `uitest-smoke.json` | 6 | 注入通路 + 视口 |
| `uitest-market.json` | 13 | 市场：停靠、空艇成交、补给采购 |
| `uitest-beast.json` | 21 | 巨兽：喂食、切形态、停靠、拔锚、防守、复活、手动开船 |
| `uitest-ledger.json` | 14 | 账本：定价、策略、发薪 |
| `uitest-people.json` | 24 | 人事与法术：招募、转岗、给自由、不参战、帮工、传送、开赛 |
| `uitest-drag.json` | 29 | 拖拽与建造：抓起、投楼、放回、挖土、派岗、拔起、落位、建造、拆除 |
| `uitest-battle.json` | 25 | 战斗：挖开第二层、发起清剿、撤退算失败、再清剿、嵌入魔晶、治疗伤员 |
| `uitest-reject.json` | 22 | 拒绝文案：法力用光那一发的前缀与横幅 |
| `uitest-coverage.json` | 39 | 补齐四条：修理、卖明星、还债、进阶；条件用 `eval` 摆，四个动作都点界面 |
| `uitest-mainline-live.json` | 258 | 整条主线实机复放（seed 7，窗口出图）：T18–T20 三段截图、最终防守失败→立即重试→胜利→结算→继续经营→再次拔锚 |
| `uitest-terrain.json` | 28 | 地形精修出图：战略、湖面、湖面近景、沙漠、道路、俯视六张（窗口模式） |

### 44 条业务动词落在哪一步

产品动词 44 条。`SteerOutOfBounds` 由世界自己触发（出界转向），玩家按不到；其余 43 条各有
一个界面入口（ticket 07 起，市场 / 账本与工资 / 人员与帮工并成命令栏「经营」一个入口，内部分栏）。
`EatTarget` / `RecallForage` / `UseCrystal` / `SellCrystal`
的脚本步还没补（下表记 `——`），所以十一份脚本实际点到 39 条；**每条动词的界面入口由
`UiEntryCoverageTests` 对着 `UiEntryPoints` 逐条钉住**（缺一条、重复一条、或标成自动触发都判红）。

`click` 走界面那一路，`inject` 走调试桥的写路径 —— 两者触发的是同一条动词。
`eval` 只摆前置条件（耐久、名声、负债、账本余量），不改被验的那一发。
步号是驱动器自己数的（`[第 N 步]`）。

| 动词 | 落在哪份脚本第几步 | 界面入口（点哪个控件） |
|---|---|---|
| StartRun | start 2 | 标题屏「开始游戏」 |
| Dock | market 4–5 / beast 10–11 / ledger 4–5 / people 4–5 / drag 4–5 / battle 4–5 | 命令栏「停靠」→ 目的地弹层「贫城」 |
| SailTo | beast 20 | 右键点大地图空地 |
| PullAnchor | beast 14 | 命令栏「拔锚」 |
| StartMatch | people 23 | 竞技场菜单「开赛」 |
| TradeAirship | market 9 | 经营 → 市场 →「成交」（空艇那一行） |
| GrabEntity | drag 8、13 | 拖拽（上帝之手）按住镇民 / 尸体 / 散货 |
| DropToBuilding | drag 10 | 拖到某栋建筑上松手 |
| DropToDeck | drag 15 | 拖到甲板空地松手 |
| LiftBuilding | drag 22 | 建筑菜单「拔起」 |
| DropBuilding | drag 24 | 落位模式下点甲板格（建筑菜单「落位」） |
| SummonHelper | people 15 / reject 9 | 经营 → 人员与帮工 →「召唤」 |
| DismissHelper | people 17 | 经营 → 人员与帮工 →「遣散」 |
| CastHeal | battle 24 | 选中面板「治疗 1 法力」 |
| PlaceBuilding | drag 26 / people 19 | 蓝图条槽位 → 甲板落座 |
| RemoveBuilding | drag 28 | 建筑菜单「拆除」 |
| ExcavateSoil | drag 17 / battle 8 | 土壤面板「开挖」 |
| EmbedCrystal | battle 22 | 土壤面板「嵌入魔晶」 |
| ClearCave | battle 14 | 土壤面板「清剿」（洞穴那一行） |
| RetreatCave | battle 16 | 土壤面板「撤退」（同一颗按钮，清剿中变脸） |
| Recruit | people 8 | 镇政府菜单「招募」 |
| ConvertRole | people 10 | 选中面板「转角斗」 |
| GrantFreedom | people 12 | 选中面板「给自由」 |
| AssignPost | drag 20 | 土壤面板「派到此格」；另一入口是拖人进楼 |
| SetNonCombatant | people 14 | 选中面板「不参战」 |
| ReviveCorpse | beast 18 | 教堂建筑菜单「救人：<名字>（10 魂石）」 |
| SetPricing | ledger 9 | 经营 → 账本与工资 →「应用」 |
| BuySupply | market 10 | 经营 → 市场 →「采购」 |
| SetDebtPolicy | ledger 10 / smoke 5 | 经营 → 账本与工资 → 策略三选一 |
| PayWages | ledger 11 | 经营 → 账本与工资 →「发薪」 |
| Teleport | people 21 | 传送阵建筑菜单「传送」 |
| SwitchForm | beast 7 | 巨兽面板「荒漠态 / 暴食态」 |
| Feed | beast 4 | 巨兽面板「喂 50 食物」 |
| RepairBuilding | coverage 13 | 建筑菜单「修理」 |
| SellStar | coverage 19 | 选中面板「卖明星」 |
| PayDebt | coverage 25 | 经营 → 账本与工资 →「还清」 |
| AdvanceTier | coverage 36 | 巨兽面板「进阶」 |
| EatTarget | —— | 命令栏「吞食」→ 吞食面板的目标行 |
| RecallForage | —— | 吞食面板「召回觅食」（觅食途中可点） |
| UseCrystal | —— | 巨兽面板魔晶用途组（补魔 / 治疗 / 修理） |
| SellCrystal | —— | 经营 → 市场 →「出售魔晶」 |
| RetryFinalDefence | mainline-live 196 | 最终防守失败战报上的「立即重试」 |
| ContinueBusiness | mainline-live 223 | 终局结算「继续经营」 |

### 四条条件怎么摆（2026-10-24 补齐）

这四条原先在 ticket 10 的收尾里没有覆盖到。条件由 `eval` 摆好，动作仍点界面：

| 动词 | 摆什么条件（`uitest-coverage.json`） | 实测读数 |
|---|---|---|
| RepairBuilding | 打掉饭馆 20 点耐久；把 w1 派进饭馆 | `B restaurant Restaurant payload=80/100`、`post=restaurant` |
| SellStar | 把 g1 的个人名声抬到 100 | `STAR sales=1` |
| PayDebt | 记一笔 100 金的债给 `poor_town` | 点「还清」后头部段 `debts=` 清空 |
| AdvanceTier | 备齐 40 魂石 / 10 特殊材料 / 2000 金币；挖开 0 号象限第 2 层、清剿洞穴、嵌入 1 单位魔晶 | 仪式走完后 `BEAST beast tier=2` |

## 主命题的历史验证口径

历史命题是「背上小镇的经营产出能不能改变防守战的胜负」。当时的四类断言分别盯它的四个侧面：

| 断言 | 盯什么 | 假绿的形状 |
|---|---|---|
| 对照实验（历史） | 固定怪物波次与种子、只换经营配置，结果有没有变 | 只有「多建就赢」的单调滑条 —— 一定绿，但答不了命题 |
| 确定性 | 同种子两跑逐字相等 | 随机泄漏会让分布好看，实验本身却不可信 |
| 守恒 | 建材与金币的产出 = 消耗 + 结余 | 凭空漏出或生出资源，经营配置就不是那条因果链上的真变量 |
| 边界 | 限时到点、地标被摧毁、涌入口无防御三种收尾各自可达 | 收尾不可达 = 有一种结局从没被覆盖 |

## 历史决策：对照实验的规模被砍到 3 场（2026-09-30 用户定）

spec 原稿写的是 4 配置 × 2 波次 × 每格 20 场 = 160 场，靠**分布**答命题。
用户当时明确不跑那一套：「只准跑三场，数值不重要，差不多就得了」。

那一阶段跑的是三场：重建材配藤潮、重金币配石潮、不经营配藤潮。断的是
「配装确实随经营配置而变」与「三种收尾都跑得到」，**不是**「胜负分布翻转」。

**3 场阶段没有验出命题。** 3 场量不出分布，也就答不了「经营有没有因果力」。
160 场版后来恢复并形成主命题的历史结论；当前 fixture 已随旧切片退役，不能从本工程复跑。

## 四条排除就是边界（这条排除面是哪条约束 → AGENTS.md 「逻辑层纯 C#」）

`Camera` / `Animation` / `DebugBridge` 三件 / `beehave` / `Guard/LogRateWatchdog.cs` 被显式排除：
它们吃 `Node3D` / `AnimationPlayer` / `TcpListener` / `Godot.Node`，链进测试进程直接编译失败。
同目录的 `BridgeArgs.cs` 是纯 C# 取参口径，链得进来，也就断得了。
`WiringSmokeTests` 是这条边界的报警器。

## 为什么不引用主 csproj

主项目是 `Godot.NET.Sdk`，引用它会把 GodotSharp 与引擎依赖整个拖进测试进程；
所以走 `<Compile Include>` 源码链接，只编纯 C# 的那部分。逻辑面与 MCP 实测面的分工 → 「测试与实测分工」
（表现层绑定一律走 MCP 实测，不用测试去断言）。
