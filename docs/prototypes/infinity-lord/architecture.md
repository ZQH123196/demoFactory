# 无限领主 — 架构决策

> 约束句一律写成 契约名 指针（「约束唯一出处」）。选型基准见 `docs/prototypes/各原型设计初始基准.md` 表四。

## 1. 依赖的中台系统（初始基准·表四对账）

| 中台系统（表四） | 本 demo 的使用 |
|---|---|
| **全局时间/时钟** | `GameClock` 离散步进：Advance/Schedule/Pulse/Pause，防同一步内级联 |
| **标签/事件总线** | 暂停走 `EventBus.GamePaused`；跨原型信号也走它 |
| **指令排队/调度器** | 玩家指令=悬赏旗写入；英雄 AI 只产生意图 |
| **剥离状态的 AI** | 决策链=固定顺序纯代码，`StepHero` |
| **视角/相机** | `TopDownRig` 四自由度 |

依赖「原型隔离」：只依赖 `GugaFramework` 与 `Shared`。
### 经济系统（军饷删除后 — 冒险者自雇）

每 60 步（`EconomyCycle`）：村民税 +12 入金库，无系统性支出。需求流失托底人口：赏金饥饿（连续零收入）/ 酒馆饥饿（想喝喝不上），任一线满 300 步 → 冒险者离队（`ReleaseFlagsOf` 防孤儿化 → `UnitDied` 清尸）。悬赏是玩家写入口：挂赏扣金库 → 目标死结算发赏（无人承接则退款）→ 市场消费 → 40% 税收回流。

设计含义：金库是弹药库——下行压力只来自玩家的悬赏消费。
## 2. 三层结构

表现层 `Main.cs` + `Main.tscn`（无状态只读节点）：每帧累积 0.1 秒 → `Advance(1)`；模型字典镜像渲染；玩家输入调逻辑层公开悬赏 API。逻辑层 `LogicWorld : GameLogicBase`（纯 C#，零 Godot 依赖，坐标用 `System.Numerics`）。数据层 `WorldTypes.cs`：`Building / Unit / BountyFlag` 与事件报告。
## 3. 悬赏旗状态机（核心机制）

`PlaceBountyFlag(target, gold)` 扣金库进「待承接」→ 玩家 `RaiseBountyFlag` 加价 → 英雄 `TryClaimFlag` 通过勇气评估进「已承接」→ 目标死亡或建筑摧毁时 `SettleFlags` 单点结算，赏金+XP 发给承接者。承接者阵亡则旗子释放。
### 关键取舍

1. **单点结算**：所有赏金流动只发生在 `SettleFlags()`，杜绝账目分叉。
2. **Released 事件**：承接者阵亡必须释放旗子——否则旗子被死人永久占用、悬赏孤儿化。
3. **目标双态**：`TargetId` 先按怪物单位查找、再按怪物建筑查找；玩家方建筑不可挂赏。建筑威胁折算为 `appeal = gold / (health × BuildingThreatFactor)`，共用同一个勇气阈值。
## 4. 英雄 AI 决策链（每步固定顺序）

1. 已承接旗子 → 追击目标 / `EngageBuilding` 攻坚（为赏金玩命，不还手也分心）
2. 拾取新旗子 → `TryClaimFlag` 勇气评估（血量越低阈值越高 = 惜命）
3. 战斗本能 → 视野内最近敌人交战；钱包 ≥30 则去市场全花掉，40% 税收回流
4. 漫步 → 随机游走

设计含义：**承接中的英雄不会分心打怪**（第 1 步 return），会被巢穴口刷出的怪耗死——这是「消耗战」涌现的来源，是特性不是 bug。
## 5. 表现层视角与点选单位

视角走 `TopDownRig`（相机单源）：四自由度，俯角 −89.99°~89.99°（上下限缺省都是这个数，公共层 2026-10-15 那一轮把「下限必须为正」判成 bug 改掉了，同日用户又定到 89.99）。点选单位是屏幕最近邻匹配（`Camera3D.UnprojectPosition` + 阈值），**不用碰撞体**——造型是资产文件，加碰撞体等于再维护一套几何。点选点=模型根节点（地面中心），不是头顶 Label3D；单位优先于建筑。

- **阈值不是固定像素**：`48 × rig.PickToleranceScale`（钳在 20~160px）——48px 在不同机位下盖住的世界范围差好几倍。头顶文字同理：世界字号 × `Distance / ReferenceDistance`。
- 单位模型位置加了指数阻尼，否则拉近或锁定跟随时是 10 次/秒抖动。地面板要盖到最远机位（140）的取景边缘，俯角压低后铺一层同色雾把边缘化成远处的雾。
## 6. 表现层：真模型（.glb）+ ModelAnimator 喂状态

所有单位/建筑/道具都是按逻辑流实例化的**模型资产实例**，不是预摆节点。17 份造型在 `Models/*.glb`（Units 8 / Buildings 8 / Props 1）。

造型的源是 `Tools/Gen.cs` 生成的关节树源场景（`Tools/Rigs/`），`.glb` 是其编译产物（→《模型与动画准则》「标准管线」）。单向流水线与四条契约、离线几何校验器 `Check.cs` 写在 [`Tools/README.md`](../../../src/Prototypes/InfinityLord/Tools/README.md)——改造型前必读。

- 三张映射表（职业、怪物名、建筑 kind）在 `Main.cs` 的模型工厂，存 `res://.../Models/<Name>.glb`。**运行时不改造型**：不换色、不缩放几何、不缩旗杆；唯一叠加是升级金光，走 `ModelAnimator.Flash()`，0.6 秒后撤掉。
- 动画走 `ModelAnimator`（唯一翻译层 → 「动作在模型」）：原型只报「走多快 / 出手了 / 死了 / 升级了」。`GameSpeed` 由表现层写入 `AnimationPlayer.SpeedScale`。挑走/跑的口径是「米每游戏秒」：位移差商 ÷ 快进倍率后 ×10 换算（阈值 14），差商要低通一步（时间常数 0.15 秒）。
- 验收分工：造型/剪影/配色用 `_Preview*.tscn`；clip、占地、野节点由 `inspect_model.gd` 无头校验器判；`Anchor` 与动画播放**只能在真关卡里验**。
## 7. 可观测性设计（为 AI 验证闭环服务）

- 逻辑层只发事件（`UnitDied/CombatEvent/BountyEvent/EconomyEvent`），不写日志；表现层订阅后打 `GD.Print`，`logs_read(source:"game")` 可回放。
- HUD 直接读逻辑层公开快照（`Tick` / `Treasury` / `Units` / `Buildings` / `Flags`）每帧刷新。
- 固定种子保证开局完全可复现。
## 8. 已知技术债

- `TryClaimFlag` 是 O(旗×单位) 线性扫描，`FindNearestEnemy` 每步全量遍历——单位数上百后需空间划分（网格/四叉树）。
- 英雄自然增长快于村民税承受力（均衡兵力约 12 人）——调参课题。
- 预览场景仍不进运行时、不跑 `Main.tscn` 的染色/血条代码路径；这两条只能靠实跑验。工具侧已闭的账见 [`Tools/README.md`](../../../src/Prototypes/InfinityLord/Tools/README.md)。
