# 世界装配是一份 builder 加一个基类，不是「把层装配起来」这件事

`src/GugaFramework/` 收编三处原型的重复构造段，收编成一件 builder 加一个统一基类。
顺序敏感的那部分**留在原型**。

## Considered Options

- **收编整个装配过程**（公共层决定建哪几层、什么次序、怎么接事件）：不可行。
  实测三处构造各有原型特有的次序依赖，且注释里已写成契约——
  「决策段先订 → NavMove 构造内订走位段 → 镜像/后段最后订」三处都有；
  「Terrain Attach 必须早于种子建筑放置」晚了阻挡永不落（`LogicWorld.cs:429-431`）；
  无限领主还有「`Abilities` 必须先于 NavMove 的 gate 递查法」。
  把这些抽进公共层，抽到的是三个原型里最不一样的那部分。
- **只搬 `_Template/Scripts/Logic/LayerWiring.cs` 的六个静态方法进框架**：
  便宜，但那一层不带退订、不带继承关系。而巨兽竞技场现在**完全没有退订路径**
  （无 `_ExitTree`、无 `UnwireWorld`），光搬六个静态方法补不上那个洞。
- **builder + 统一基类，基类持有时钟、推进次序与退订成对**：本轮选这个。
  三处逐字重写的五步与三段收进 builder；基类接住退订成对，
  正好是给巨兽竞技场补上退订的最自然时机。

## Consequences

- 三个世界收编后都继承 `GameLogicBase`。收编前只有一个不继承：`BeastArenaWorld`；
  另两个早已继承——`LogicWorld`（`LogicWorld.cs:47`）与 `BattleWorld`（`BattleWorld.cs:117`，
  自带 `OnDispose`）。
- `GameLogicBase.cs`（现在 29 行、唯一能力是 `Dispose` → `OnDispose`）要补时钟持有、
  推进次序与退订成对。
- 基类**不代原型排那三类次序**，它只固定那五步。原型仍要自己排自己那份次序。
- 旧写法在三个原型全迁、验证跑通之后删，不长期并存（「先接后删」）。
- 机制与实测数字见 `docs/GugaFramework/世界装配.md`；三个原型各自的迁移动作与读数记在
  `docs/prototypes/<原型>/框架迁移记录.md`。