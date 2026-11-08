#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using DemoFactory.GugaFramework.Construction;
using DemoFactory.GugaFramework.Pathfinding;
using DemoFactory.GugaFramework.Persistence;
using DemoFactory.GugaFramework.Scheduling;

namespace DemoFactory.Prototypes.InfinityLord.Logic;

/// <summary>
/// 无限领主的存档那一半 —— <see cref="ISaveable"/> 的实现（<c>LogicWorld.Save.cs</c>）。
///
/// <para><b>三分判据只管这一份</b>：权威状态存（金库 / 建筑全集 / 单位全集与当前位置 /
/// 悬赏旗 / 技能账 / 寻路登记表 / 待触发事件表 / 随机流已抽次数）；派生数据不存
/// （净空场、寻路路径缓存、血条百分比、单位坐标镜像——读档时重算）；引擎句柄绝不存
/// （模型、贴图、音频、<c>Node</c> 引用）。</para>
///
/// <para><b>位置为什么存两处的读法只留一处</b>：位置真值住在寻路流水线里，单位那一列是镜像。
/// 所以存档只存<b>登记表</b>那一份，读档末尾走一次 <c>SyncMirrors</c> 把镜像重算回来。
/// 两处都存的话，读档那一发镜像重算之前有一瞬间两份不一致，而那一刻正好是「读档后画面错位」
/// 这类故障最容易复现的窗口。</para>
///
/// <para><b>与调试快照不共用字段清单</b>（<c>docs/adr/0012</c>）：这一份是给机器读回去的，
/// 短、要版本承诺；<c>ReplaySnapshot</c> 是给人当场排查的，冗余、随代码变。
/// 权威状态被写两遍这件事这一版接受 —— 往返测试能抓到两份算出不同值的情形，
/// 而那本身是要修的 bug，不是让存档迁就重放。</para>
/// </summary>
public sealed partial class LogicWorld : ISaveable
{
    /// <summary>所属原型名（进文件头，读档先比对）。</summary>
    public const string WorldName = "InfinityLord";

    /// <summary>状态清单的版本。第一版只有 v1，迁移表在存档模块那一半（<see cref="SaveMigration"/>）。</summary>
    public const int SaveSchemaVersion = 1;

    // === 待触发事件的名字（存档读档靠它把闭包委托重排回来）===

    /// <summary>经济周期自续（<see cref="ScheduleNextCycle"/>）。</summary>
    private const string EventEconomy = "经济周期";

    /// <summary>BOSS 巢穴降临。</summary>
    private const string EventBoss = "BOSS降临";

    /// <summary>剧本波次（下标进 <c>WaveTable</c>，名字与那一行一一对应）。</summary>
    private static string EventWave(int index) => $"波次#{index.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>建筑产出链的自续一步。</summary>
    private static string EventSpawn(string buildingId) => $"产出#{buildingId}";

    /// <summary>「没有这一项」的占位符：与重放快照同一写法。id 非空，所以这个值不可能撞上真 id。</summary>
    private const string NoValue = "-";

    /// <summary>
    /// 读档那一发进行中。
    ///
    /// <para>它挡的是一件事：模块的 <c>Completed</c> 事件会起产出链，而产出链是往时钟里排一条
    /// 新的自续事件。读档要按<b>存档里那张表</b>重排，不该让「重建这一座楼」顺手多排一条 ——
    /// 多排的那一条不出现在表里，下一次存档两串就不相等了。</para>
    /// </summary>
    private bool _restoring;

    /// <inheritdoc />
    public int SchemaVersion => SaveSchemaVersion;

    /// <summary>
    /// 待触发事件表（名字 + 还差几步，按调度次序）—— 存档那一半写它、<see cref="Reschedule"/> 重排它，
    /// 调试桥与用例读它。<b>时钟自己存不出可识别的句柄</b>（排进去的是闭包委托），
    /// 这张表就是那一层的替代品。
    /// </summary>
    public IReadOnlyList<ScheduledEvent> PendingEvents() => _clock.Pending();

    /// <summary>
    /// 随机流已抽次数（存档那一半写它，调试桥与用例读它）。
    /// 读回来那一发要续上同一条流：同一个种子从头重抽到这一处。
    /// </summary>
    public long RandomDraws => _rngDraws;

    // === 写 ===

    /// <inheritdoc />
    public void WriteState(SaveWriter w)
    {
        ArgumentNullException.ThrowIfNull(w);
        if (_rngSeed is null)
            throw new InvalidOperationException(
                "跟系统共享随机流的世界存不回来（Random 的内部状态存不进去）："
                + "造世界时给一个固定种子（new LogicWorld(randomSeed: 42)）");

        w.Int("treasury", Treasury);
        w.Int("lastCycleNet", LastCycleNet);
        w.Str("outcome", Outcome.ToString());
        w.Int("moraleDecayTimer", _moraleDecayTimer);
        w.Int("nextUnitSeq", _nextUnitSeq);
        w.Int("nextFlagSeq", _nextFlagSeq);
        w.Int("nextLairSeq", _nextLairSeq);
        w.Int("rngSeed", _rngSeed.Value);
        w.Long("rngDraws", _rngDraws);

        // 集合的次序一律定死：这一份要能逐字比对，次序不稳就没有可比的两串。
        // 字典按枚举名序数排，集合按原次序（落位 / 出生 / 承接），哈希集合按序数排。
        w.Obj("placedCount", m =>
        {
            foreach (var (kind, count) in _placedCount.OrderBy(p => p.Key.ToString(), StringComparer.Ordinal))
                m.Int(kind.ToString(), count);
        });

        w.Arr("spawnChains", l =>
        {
            foreach (var id in _spawnChains.OrderBy(id => id, StringComparer.Ordinal)) l.Str(id);
        });

        // 建筑按落位次序（与模块册内那一行的顺序一致）；「场地建筑还是玩家营建」要存 ——
        // 场地建筑走 RegisterFixture（跳过落位校验），玩家营建走 Commit（校验间距）。
        // 用错那一条，种子布局里那两对挨得比 12 近的（行会↔宫殿 8）当场放不下。
        w.Arr("buildings", l =>
        {
            foreach (var b in _buildings) l.Obj(o => WriteBuilding(o, b));
        });

        // 单位按出生次序（决策链遍历的就是这个次序，次序本身就是确定性的一部分）。
        // 坐标不在这一段：位置真值在下面那段登记表里。
        w.Arr("units", l =>
        {
            foreach (var u in _units) l.Obj(o => WriteUnit(o, u, withPosition: false));
        });

        // 待落场的单位（产出链与波次排好队、下一步统一落场）。它们还没登记进流水线，
        // 坐标没有别处可抄，所以这一段自带坐标。
        w.Arr("spawnQueue", l =>
        {
            foreach (var u in _spawnQueue) l.Obj(o => WriteUnit(o, u, withPosition: true));
        });

        w.Arr("flags", l =>
        {
            foreach (var f in _flags)
                l.Obj(o =>
                {
                    o.Str("id", f.Id);
                    o.Str("target", f.TargetId);
                    o.Int("gold", f.Gold);
                    o.Str("claimedBy", f.ClaimedByHeroId ?? NoValue);
                    o.Bool("settled", f.Settled);
                });
        });

        // 英雄接管属权威状态（重放端自建接管表的唯一入口就是它）。
        w.Arr("brainIntents", l =>
        {
            foreach (var (hero, brain) in _brainIntents.OrderBy(p => p.Key, StringComparer.Ordinal))
                l.Obj(o =>
                {
                    o.Str("hero", hero);
                    o.Str("intent", brain.Intent.ToString());
                    o.Str("target", brain.TargetId ?? NoValue);
                });
        });

        w.Str("abilities", _abilities.Snapshot());

        // 登记表按登记次序整张存（含已退场的那些）。
        w.Arr("navRoster", l =>
        {
            foreach (var row in _nav.Roster())
                l.Obj(o =>
                {
                    o.Str("id", row.Id);
                    o.Num("x", row.X);
                    o.Num("z", row.Z);
                    o.Num("facing", row.FacingDegrees);
                    WriteNavCommand(o, row.Command);
                });
        });

        w.Long("navStep", _nav.Stepper.StepIndex);
        w.Long("tick", Tick);

        // 待触发事件表：名字 + 还差几步，按调度次序。读档按同一份次序、同一份名字重排。
        w.Arr("scheduled", l =>
        {
            foreach (var e in _clock.Pending())
                l.Obj(o =>
                {
                    o.Str("name", e.Id);
                    o.Int("remaining", e.RemainingTicks);
                });
        });
    }

    /// <summary>
    /// 一条移动命令在存档里的形态。
    ///
    /// <para><b>为什么命令也要存</b>：追实体那一档的目标坐标是<b>发令那一刻</b>的读数，
    /// 层里不缓存也不外推。读档后不把手上那条放回去，下一步决策链重发的就是带着新坐标的另一条，
    /// 走的路与存档那一局差几厘米，然后<b>永远差下去</b>。</para>
    ///
    /// <para>五档的字段全写（用不上的写零 / 空串 / 空串数组），键序固定 —— 读侧按键序读，
    /// 少写一个键就会在「读到别的键」那一格当场红。</para>
    /// </summary>
    private static void WriteNavCommand(SaveWriter o, NavCommand? command)
    {
        var target = command?.Target;
        o.Str("cmd", command is null ? "none" : target!.Kind.ToString());
        o.Num("tol", command?.ArriveToleranceMeters ?? 0.0);
        o.Num("px", target?.Point.X ?? 0.0);
        o.Num("pz", target?.Point.Z ?? 0.0);
        o.Num("ex", target?.EntityPosition.X ?? 0.0);
        o.Num("ez", target?.EntityPosition.Z ?? 0.0);
        o.Str("entity", target?.EntityId ?? string.Empty);
        o.Num("radius", target?.AreaRadiusMeters ?? 0.0);
        o.Arr("waypoints", l =>
        {
            foreach (var waypoint in target?.Waypoints ?? Array.Empty<NavPoint>())
                l.Obj(p =>
                {
                    p.Num("wx", waypoint.X);
                    p.Num("wz", waypoint.Z);
                });
        });
    }

    private static NavCommand? ReadNavCommand(SaveReader o)
    {
        var kindText = o.Str("cmd");
        var tolerance = o.Num("tol");
        var point = new NavPoint(o.Num("px"), o.Num("pz"));
        var entityPosition = new NavPoint(o.Num("ex"), o.Num("ez"));
        var entityId = o.Str("entity");
        var radius = o.Num("radius");
        var waypoints = new List<NavPoint>();
        o.Arr("waypoints", l =>
        {
            while (!l.Done) l.Obj(p => waypoints.Add(new NavPoint(p.Num("wx"), p.Num("wz"))));
        });

        if (kindText == "none") return null;

        var target = kindText switch
        {
            nameof(NavCommandKind.Hold) => NavTarget.Hold(point),
            nameof(NavCommandKind.GoTo) => NavTarget.GoTo(point),
            nameof(NavCommandKind.Chase) => NavTarget.Chase(entityId, entityPosition),
            nameof(NavCommandKind.AlongWaypoints) => NavTarget.Along(waypoints),
            nameof(NavCommandKind.GuardArea) => NavTarget.Guard(point, radius),
            _ => throw new FormatException($"寻路命令的档「{kindText}」不认识：存档与世界不同步"),
        };

        return new NavCommand { Target = target, ArriveToleranceMeters = tolerance };
    }

    private void WriteBuilding(SaveWriter o, Building b)
    {
        o.Str("id", b.Id);
        o.Str("kind", b.Kind.ToString());
        o.Str("owner", b.Owner.ToString());
        o.Int("spawnTicks", b.SpawnIntervalTicks);
        o.Int("level", b.Level);
        o.Bool("fixture", b.SpawnIntervalTicks >= 0 && IsFixture(b));
        o.Num("x", b.Position.X);
        o.Num("y", b.Position.Y);
        o.Int("hp", b.Health);
    }

    /// <summary>这一座是不是走 <see cref="AddFixture"/> 那条进来的（场地建筑）。</summary>
    private bool IsFixture(Building b)
    {
        // 场地建筑的坐标由关卡摆位给定、id 不带序号；玩家营建的 id 是「kind + 序号」。
        // 这里按「模块册内那一行有没有报过建成」判不出（RegisterFixture 那一档也发 Completed），
        // 所以改为记一份显式的来源标记在句柄上：见 EnsureBuildingOrigin。
        return _fixtureIds.Contains(b.Id);
    }

    private void WriteUnit(SaveWriter o, Unit u, bool withPosition)
    {
        o.Str("id", u.Id);
        o.Str("name", u.Name);
        o.Str("side", u.Side.ToString());
        o.Str("class", u.Class.ToString());
        o.Str("home", u.HomeGuildId ?? NoValue);
        o.Int("hp", u.Health);
        o.Int("maxHp", u.MaxHealth);
        o.Int("atk", u.Attack);
        o.Int("atkCdTicks", u.AttackCooldownTicks);
        o.Float("speed", u.Speed);
        o.Float("sight", u.SightRange);
        o.Float("range", u.AttackRange);
        o.Int("gold", u.Gold);
        o.Int("xp", u.Xp);
        o.Int("level", u.Level);
        o.Int("gear", u.Gear);
        o.Int("morale", u.Morale);
        o.Int("noIncome", u.NoIncomeTicks);
        o.Int("tavernStarve", u.TavernStarveTicks);
        o.Str("target", u.TargetUnitId ?? NoValue);
        o.Str("claim", u.ClaimedFlagId ?? NoValue);
        o.Int("lastGold", u.LastObservedGold);
        o.Int("cooldown", u.CooldownRemaining);
        o.Bool("hasMoveTarget", u.MoveTarget is not null);
        if (u.MoveTarget is { } target)
        {
            o.Num("moveX", target.X);
            o.Num("moveY", target.Y);
        }
        else
        {
            o.Num("moveX", 0.0);
            o.Num("moveY", 0.0);
        }

        if (withPosition)
        {
            o.Num("x", u.Position.X);
            o.Num("y", u.Position.Y);
        }
    }

    // === 读 ===

    /// <inheritdoc />
    public void ReadState(SaveReader r)
    {
        ArgumentNullException.ThrowIfNull(r);
        if (!_empty)
            throw new InvalidOperationException(
                "这一格只往空世界里灌（LogicWorld.EmptyForLoad(seed)）：往已经种过世界的实例上灌，"
                + "种子那七座建筑会被原地叠加，读数上看不出多了一座");

        Treasury = r.Int("treasury");
        LastCycleNet = r.Int("lastCycleNet");
        Outcome = ParseEnum<GameOutcome>(r.Str("outcome"), "outcome");
        _moraleDecayTimer = r.Int("moraleDecayTimer");
        _nextUnitSeq = r.Int("nextUnitSeq");
        _nextFlagSeq = r.Int("nextFlagSeq");
        _nextLairSeq = r.Int("nextLairSeq");
        var rngSeed = r.Int("rngSeed");
        var rngDraws = r.Long("rngDraws");

        r.Obj("placedCount", m =>
        {
            // 对象读完时 PeekKey 交出空串（不是 "}"）：分派器与 PeekKey 同一口径。
            while (m.PeekKey() != "")
            {
                var key = m.PeekKey();
                _placedCount[ParseEnum<BuildingKind>(key, "placedCount 的键")] = m.Int(key);
            }
        });

        r.Arr("spawnChains", l =>
        {
            while (!l.Done) _spawnChains.Add(l.Str());
        });

        // 建筑先于单位：模块册与技能层的建筑册都要先有这一座，单位才谈得上「打它」。
        _restoring = true;
        r.Arr("buildings", l =>
        {
            while (!l.Done) l.Obj(ReadBuilding);
        });
        _restoring = false;

        r.Arr("units", l =>
        {
            while (!l.Done) l.Obj(o => ReadUnit(o, addToWorld: true, inQueue: false));
        });

        r.Arr("spawnQueue", l =>
        {
            while (!l.Done) l.Obj(o => ReadUnit(o, addToWorld: false, inQueue: true));
        });

        r.Arr("flags", l =>
        {
            while (!l.Done)
                l.Obj(o =>
                {
                    var id = o.Str("id");
                    var target = o.Str("target");
                    _flags.Add(new BountyFlag
                    {
                        Id = id,
                        TargetId = target,
                        Gold = o.Int("gold"),
                        ClaimedByHeroId = OrNull(o.Str("claimedBy")),
                        Settled = o.Bool("settled"),
                    });
                });
        });

        r.Arr("brainIntents", l =>
        {
            while (!l.Done)
                l.Obj(o =>
                {
                    var hero = o.Str("hero");
                    var intent = ParseEnum<BrainIntent>(o.Str("intent"), "brainIntents 的意图码");
                    var target = OrNull(o.Str("target"));
                    _brainIntents[hero] = (intent, target);
                });
        });

        _abilities.RestoreState(r.Str("abilities"));

        // 登记表按原次序重建 —— 次序错了就是重放分叉，而症状是「读档之后逐步走的路线不一样」。
        var alive = _units.ToDictionary(u => u.Id, StringComparer.Ordinal);
        r.Arr("navRoster", l =>
        {
            while (!l.Done)
                l.Obj(o =>
                {
                    var id = o.Str("id");
                    var pose = new InfinityLordNavMove.NavAgentPose(id, o.Num("x"), o.Num("z"), o.Num("facing"))
                    {
                        Command = ReadNavCommand(o),
                    };
                    _nav.RegisterRestored(pose, countedInSeparation: alive.ContainsKey(id));
                });
        });

        _nav.Stepper.RestoreStepIndex(r.Long("navStep"));
        _clock.RestoreTick(r.Long("tick"));

        r.Arr("scheduled", l =>
        {
            while (!l.Done)
                l.Obj(o =>
                {
                    var name = o.Str("name");
                    var remaining = o.Int("remaining");
                    Reschedule(name, remaining);
                });
        });

        RestoreRandomStream(rngSeed, rngDraws);

        // 坐标镜像重算（派生档）：位置真值刚由登记表落定，镜像还没跟上。
        _nav.SyncMirrors(AliveUnits());
    }

    private void ReadBuilding(SaveReader o)
    {
        var id = o.Str("id");
        var kind = ParseEnum<BuildingKind>(o.Str("kind"), "building.kind");
        var owner = ParseEnum<Faction>(o.Str("owner"), "building.owner");
        var spawnTicks = o.Int("spawnTicks");
        var level = o.Int("level");
        var fixture = o.Bool("fixture");
        var x = (float)o.Num("x");
        var y = (float)o.Num("y");
        var hp = o.Int("hp");

        var building = new Building
        {
            Id = id,
            Kind = kind,
            Owner = owner,
            SpawnIntervalTicks = spawnTicks,
            Level = level,
        };

        if (fixture)
        {
            _fixtureIds.Add(id);
            _construction.RegisterFixture(BlueprintIdOf(kind), new Vector2(x, y), id, 0, building);
        }
        else
        {
            var verdict = _construction.Commit(BlueprintIdOf(kind), new Vector2(x, y), 0, id, null, building);
            if (!verdict.Ok)
                throw new InvalidOperationException(
                    $"{id} 读档时模块拒了落位（{verdict.Reason}）：坐标或蓝图对不上，"
                    + "去查建造模块那一册 —— 这一格是恢复过程本身抛，不是「读档失败」");
        }

        DrainConstructionEvents();

        var row = _construction.Find(id);
        if (row is null)
            throw new InvalidOperationException($"{id} 登记之后在模块册里查不到：册与句柄表分家了");

        if (row.State != BuildState.Completed)
            throw new InvalidOperationException(
                $"{id} 在存档里是在建状态（{row.State}）：这一版的存档不恢复在建工期，"
                + "碰上一份在建的档要当场说清，而不是读出一个进度错乱的建筑");

        _construction.SetHealth(id, hp);
    }

    private void ReadUnit(SaveReader o, bool addToWorld, bool inQueue)
    {
        var unit = new Unit
        {
            Id = o.Str("id"),
            Name = o.Str("name"),
            Side = ParseEnum<Faction>(o.Str("side"), "unit.side"),
            Class = ParseEnum<HeroClass>(o.Str("class"), "unit.class"),
            HomeGuildId = OrNull(o.Str("home")),
            Health = o.Int("hp"),
            MaxHealth = o.Int("maxHp"),
            Attack = o.Int("atk"),
            AttackCooldownTicks = o.Int("atkCdTicks"),
            Speed = o.Float("speed"),
            SightRange = o.Float("sight"),
            AttackRange = o.Float("range"),
            Gold = o.Int("gold"),
            Xp = o.Int("xp"),
            Level = o.Int("level"),
            Gear = o.Int("gear"),
            Morale = o.Int("morale"),
            NoIncomeTicks = o.Int("noIncome"),
            TavernStarveTicks = o.Int("tavernStarve"),
            TargetUnitId = OrNull(o.Str("target")),
            ClaimedFlagId = OrNull(o.Str("claim")),
            LastObservedGold = o.Int("lastGold"),
            CooldownRemaining = o.Int("cooldown"),
        };

        var hasMoveTarget = o.Bool("hasMoveTarget");
        var moveX = o.Num("moveX");
        var moveY = o.Num("moveY");
        unit.MoveTarget = hasMoveTarget ? new Vector2((float)moveX, (float)moveY) : null;

        if (inQueue)
        {
            unit.Position = new Vector2((float)o.Num("x"), (float)o.Num("y"));
            _spawnQueue.Enqueue(unit);
            return;
        }

        if (addToWorld)
        {
            _units.Add(unit);
            _abilities.Register(unit);
        }
    }

    /// <summary>
    /// 把一条待触发事件按名字重排回去 —— 时钟里挂的是<b>闭包委托</b>，反射拿不到可识别的句柄，
    /// 只有这一条路：不恢复的话脉搏回来了，但这批结算永远不发生，而且不报错。
    /// </summary>
    private void Reschedule(string name, int remaining)
    {
        if (remaining < 1)
            throw new InvalidOperationException($"待触发事件 {name} 还差 {remaining} 步：延迟至少 1 步");

        if (name == EventEconomy)
        {
            _clock.Schedule(name, remaining, EconomyCycle);
            return;
        }

        if (name == EventBoss)
        {
            _clock.Schedule(name, remaining, SpawnBossLair);
            return;
        }

        if (name.StartsWith("波次#", StringComparison.Ordinal))
        {
            var index = ParseIndex(name, "波次#");
            if (index < 0 || index >= WaveTable.Length)
                throw new InvalidOperationException($"待触发事件 {name} 指向的波次下标不在 WaveTable 里");

            var row = WaveTable[index];
            _clock.Schedule(name, remaining, () => RunWave(row));
            return;
        }

        if (name.StartsWith("产出#", StringComparison.Ordinal))
        {
            var buildingId = name["产出#".Length..];
            _clock.Schedule(name, remaining, () =>
            {
                var building = _buildings.FirstOrDefault(b => b.Id == buildingId);
                if (building is not null && building.Health > 0 && building.SpawnIntervalTicks > 0)
                    EnqueueSpawn(building);

                ScheduleSpawnStep(buildingId);
            });

            return;
        }

        throw new InvalidOperationException(
            $"不认的待触发事件名「{name}」：存档与世界不同步 —— 新加一条定时事件却没在 Reschedule 里登记那一档");
    }

    /// <summary>
    /// 续随机流：同一个种子从头重抽到<b>已抽次数</b>那一处，重抽出来的那一串与原局逐字相同。
    ///
    /// <para><b>为什么不是把随机状态存进去</b>：<c>Random</c> 的内部状态没有读出口。
    /// 世界只有 <c>RandomOffset</c> 一处随机源（每调一次抽两个数），所以「抽了几次」
    /// 就是完整的进度描述。</para>
    /// </summary>
    /// <param name="seed">存档里那一份种子。</param>
    /// <param name="draws">已抽次数。</param>
    public void RestoreRandomStream(int seed, long draws)
    {
        if (_rngSeed is null)
            throw new InvalidOperationException("这一局跟系统共享随机流，存不回来（读档那一格已经先拒了）");

        if (_rngSeed.Value != seed)
            throw new InvalidOperationException(
                $"存档的种子是 {seed}，这一局的种子是 {_rngSeed.Value}：两个种子续不出同一条随机流");

        if (draws < 0) throw new InvalidOperationException($"已抽次数不能是负数（收到 {draws}）");

        var rng = new Random(seed);
        for (long i = 0; i < draws; i++) rng.NextDouble();

        _rng = rng;
        _rngDraws = draws;
    }

    // === 小工具 ===

    private static string? OrNull(string value) => value == NoValue ? null : value;

    private static T ParseEnum<T>(string text, string what) where T : struct, Enum
        => Enum.TryParse<T>(text, out var value)
            ? value
            : throw new FormatException($"{what}：读到 {text}，那不是一个 {typeof(T).Name}");

    private static int ParseIndex(string name, string prefix)
        => int.TryParse(name[prefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
            ? index
            : throw new FormatException($"{name} 的下标不是一个整数");
}
