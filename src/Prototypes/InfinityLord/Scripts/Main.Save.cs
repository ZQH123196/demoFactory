#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DemoFactory.GugaFramework.FrontEnd;
using DemoFactory.GugaFramework.I18n;
using DemoFactory.GugaFramework.Persistence;
using DemoFactory.Prototypes.InfinityLord.Logic;
using Godot;

namespace DemoFactory.Prototypes.InfinityLord;

/// <summary>
/// 无限领主接前端层与存档的那一半（<c>Main.Save.cs</c>）。
///
/// <para><b>前端层这一期一并接上</b>：这个原型原先没有 <c>FrontEndLayer</c> 节点，
/// 于是 spec 里「Esc 弹出设置界面」「回填 <c>OptionButton.Selected</c>」整段都还没有对应物。
/// 五行接法（与前端层类注那一段同一份）：建节点 → 答暂停 → 答退出 → 递存档区 → 注册词表。</para>
///
/// <para><b>换世界退订</b>：局内热替换时 <c>Main</c> 留在树上，<c>_ExitTree</c> 一条都不执行，
/// 那十几条 <c>+=</c> 一条都不会自动执行。所以订阅集中在这两件里 ——
/// <see cref="WireWorld"/> / <see cref="UnwireWorld"/> —— <b>不靠节点离树隐式退订</b>。
/// <c>EventBus.GamePaused</c> 是<b>进程级静态单例</b>，退订重订必须成对，否则叠两层。</para>
///
/// <para><b>读档那一发的次序</b>（spec 第 9 节）：退订 → 建新世界 → 重订 → 换录制器 →
/// 清表现层 → 重建 → 回焦点 → 回填设置屏。<b>不写差异比对</b>：读档不是高频操作，
/// 整体重建只有一个分支，查起来便宜。</para>
///
/// <para><b>不还原菜单栈</b>：读档后菜单全关回游戏画面。玩家点「读取」的意图是
/// 「回到那个存档去从头玩」，读完了还压着一个模态菜单得再按一次 Esc。</para>
/// </summary>
public partial class Main
{
    /// <summary>前端层（菜单栈、设置屏、存档区那一格）。</summary>
    private FrontEndLayer _front = null!;

    /// <summary>存档库（一叠槽位，目录由 <c>user://</c> 的真实路径给定）。</summary>
    private SaveStore _saves = null!;

    /// <summary>第一版单槽，接口要能枚举（多槽是增量）。</summary>
    private const int SaveSlotCount = 1;

    /// <summary>存档落点：<c>user://</c> 下那一份（真实路径由引擎解析，公共层不引引擎）。</summary>
    private const string SaveDir = "user://saves";

    // === 接线 ===

    private void SetupFrontEnd()
    {
        _saves = new SaveStore(ProjectSettings.GlobalizePath(SaveDir));

        _front = new FrontEndLayer();
        AddChild(_front);

        // 「表现层只读」：前端层只抛请求，冻不冻由这个世界答。
        _front.PauseRequested += paused => _world.SetPaused(paused);
        _front.QuitRequested += () => GetTree().Quit();

        // 标题屏条目由这一侧递。**没有「开始新局」**：这个原型没有重开入口
        // （换世界要连录制器与表现层一起重建，不是一颗按钮的事），不摆一条按了没反应的条目。
        _front.SetTitleEntries(
            (Loc.T("继续本局"), () => _front.CloseAll()),
            (Loc.T("读取存档"), () => LoadSlotNow(SaveSlotCount)));

        // 局开始与局结束各订阅一格（两个事件在这三个原型里都有订阅方）。
        _front.RunStarted += () => Log($"[InfinityLord] 局开始（Tick {_world.Tick}）。");
        _front.RunEnded += OnRunEnded;

        // 存档区：清单与两个动作由这一侧递进去，前端层只负责画。
        _front.BindSaveSlots(
            () => _saves.Enumerate(SaveSlotCount),
            SaveSlotNow,
            LoadSlotNow);

        // 词表（共享词表，与 InputInjector 同一档，不占标准七动词）。
        foreach (var (verb, handler) in FrontEndVerbs.Vocabulary(_front))
        {
            _bridge?.Register(verb, handler);
        }

        _front.NotifyRunStarted();
    }

    /// <summary>
    /// 局结束这一格：冻住时间轴（三个原型统一的口径，真值在基类那一处推进口）。
    /// 结算屏由前端层画，明细由 <see cref="OnGameOver"/> 递进去。
    /// </summary>
    private void OnRunEnded(RunOutcome outcome)
    {
        _world.FreezeTime();
        Log($"[InfinityLord] 局结束：{outcome.Title}（第 {outcome.Tick} 步）。");
    }

    /// <summary>世界订阅的<b>挂</b>那一半 —— 换世界之后走它重挂，不靠节点离树隐式退订。</summary>
    private void WireWorld()
    {
        _world.StepStarted += OnWorldStepStarted;
        _world.Stepped += OnWorldStepped;
        _world.UnitDied += OnUnitDied;
        _world.BuildingDestroyed += OnBuildingDestroyed;
        _world.BuildingRespawned += OnBuildingRespawned;
        _world.EconomyEvent += OnEconomyEvent;
        _world.WaveStarted += OnWaveStarted;
        _world.BossArrived += OnBossArrived;
        _world.GameOver += OnGameOver;
        _world.BountyEvent += OnBountyEvent;
        _world.BuildingCompleted += OnBuildingCompleted;
        _world.Construction.Hover.Changed += OnHoverVerdict;

        // 进程级静态单例：退订重订必须成对（多挂一层 = 横幅翻两次）。
        GugaFramework.EventBus.EventBus.GamePaused.Published += OnGamePaused;

        // 营建幽灵的 gizmo 参数取一次就够 —— 它是规则构造时定死的常量，不是每帧读数。
        _snapStep = _world.Construction.GizmoParams.GetValueOrDefault("positionStep");

        // 出手那一条订阅收在 SkillCues 里（它同时管「单位↔单位」与「单位↔建筑」两条来源）：
        // 逻辑层照旧发 CombatEvent（别的消费者还在读），只是表现层这一条不再直接订它。
        _cues.Wire(_world);
    }

    /// <summary>世界订阅的<b>退</b>那一半 —— 与 <see cref="WireWorld"/> 逐条成对。</summary>
    private void UnwireWorld()
    {
        _world.StepStarted -= OnWorldStepStarted;
        _world.Stepped -= OnWorldStepped;
        _cues.Unwire();
        _world.UnitDied -= OnUnitDied;
        _world.BuildingDestroyed -= OnBuildingDestroyed;
        _world.BuildingRespawned -= OnBuildingRespawned;
        _world.EconomyEvent -= OnEconomyEvent;
        _world.WaveStarted -= OnWaveStarted;
        _world.BossArrived -= OnBossArrived;
        _world.GameOver -= OnGameOver;
        _world.BountyEvent -= OnBountyEvent;
        _world.BuildingCompleted -= OnBuildingCompleted;
        _world.Construction.Hover.Changed -= OnHoverVerdict;
        GugaFramework.EventBus.EventBus.GamePaused.Published -= OnGamePaused;
    }

    // 那两条过去写成 lambda 的订阅：退订要按方法引用摘，lambda 摘不掉。
    private void OnWorldStepStarted(long tick) => _brainBridge?.EmitStepped();

    private void OnWorldStepped(long tick) => _cues.Mirror();

    // === 写档 ===

    /// <summary>
    /// 点「保存」：<b>冻 → 写 → 解冻</b>。
    ///
    /// <para>世界推进只走 <c>Advance()</c>，所以冻了写出来的就是玩家点按钮那一刻，
    /// 而<b>写档只在步末落</b>（同 tick 先快照后发命令那条契约照抄）—— 不在 tick 中间落，
    /// 否则同一个世界状态会因玩家点按钮的时机存出不同的档。</para>
    ///
    /// <para>失败（超上限、目录不可写）当场说出原因：屏幕上留着一份旧时刻，
    /// 玩家会以为刚才那一下存上了。</para>
    /// </summary>
    private void SaveSlotNow(int slot)
    {
        _world.SetPaused(true);
        try
        {
            var info = _saves.Write(slot, LogicWorld.WorldName, new SaveBundle(_world, _front.Settings), _world.Tick);
            ShowFeedback(Loc.Fmt("已保存 · 槽位 {0} · {1:F1} KB", info.Slot, info.Bytes / 1024.0));
            Log($"[InfinityLord] 写档：{info.Readout()}");
        }
        catch (Exception ex) when (ex is SaveFormatException or SaveTooLargeException
                                      or SaveWorldMismatchException or IOException)
        {
            ShowFeedback(Loc.Fmt("保存失败：{0}", ex.Message));
            GD.PushError($"[InfinityLord] 写档失败：{ex.Message}");
        }
        finally
        {
            _world.SetPaused(false);
        }
    }

    // === 读档 ===

    /// <summary>
    /// 点「读取」：按 spec 第 9 节的次序换整个世界。<b>一步不做多一步。</b>
    ///
    /// <para>第 4 步换录制器时<b>不接旧 Journal</b>：读档是「换世界」不是「改状态」，
    /// 而 <c>ReplayAssembly</c> 绑死在旧世界上，不换它的话取步号会取错。</para>
    /// </summary>
    private void LoadSlotNow(int slot)
    {
        // 第 2 步之前先把新世界造出来：档读不成时旧世界还好好地在那儿，
        // 这就是「在入口先校验一遍再动手」—— 恢复过程本身会抛，不包一层失败出来。
        var fresh = LogicWorld.EmptyForLoad(Seed);
        var bundle = new SaveBundle(fresh, _front.Settings);
        _saves.Read(slot, LogicWorld.WorldName, bundle);

        // 第 1 步：退订（换世界是对象的事，不是节点的事）。
        UnwireWorld();

        // 第 2 步：建新世界（空世界 + ReadState 灌进去，见 LogicWorld.Save.cs）。
        var old = _world;
        _world = fresh;
        // 交还的是**旧**那一座：OnDispose 会摘时钟的 Pulse 与寻路 Detach，
        // 对着刚灌好的新世界调，那座世界的单位从此走不动（实测：读档后 agents 全 速度 0）。
        old.Dispose();

        // 第 3 步：重订（含行为树桥 —— 它持有旧世界引用）。
        WireWorld();
        _brainAttached = false; // 接管表随世界一起换回来了；树桥要重新接（下一件模型建出来时）
        _brainBridge = null;

        // 第 4 步：换录制器（不接旧 Journal）。
        _recorder = ReplaySetup.Recorder(_world, Seed, new ReplaySetup.SaveHooks(SaveSlotNow, LoadSlotNow));

        // 第 5 步：清表现层。第 6 步：重建。
        ClearPresentation();
        RebuildPresentation();

        // 第 7 步：回焦点。
        RefocusCamera();

        // 第 8 步：回填设置屏并关菜单（读档后菜单全关，见类注）。
        _front.RefillFromSettings();
        _front.CloseAll();
        _front.RefreshSaveSlots();

        ShowFeedback(Loc.Fmt("已读取 · 槽位 {0} · 第 {1} 步", slot, _world.Tick));
        Log($"[InfinityLord] 读档：{_world.Tick} 步，单位 {_world.Units.Count}，建筑 {_world.Buildings.Count}");
    }

    /// <summary>
    /// 清表现层：<b>显式清那七本字典</b>再遍历节点。
    ///
    /// <para>这个原型<b>没有现成的「清空表现层」方法</b>：现有七处 <c>QueueFree</c> 全是
    /// 「按事件收单个」，没有遍历全表的先例。漏一本字典的后果是那个 id 在新世界里
    /// 重新出现时拿到旧节点 —— 症状是「读档之后两个模型叠在一处」。</para>
    /// </summary>
    private void ClearPresentation()
    {
        // 七本字典逐本清 + 各自遍历节点 —— 不绕什么「统一容器」：
        // 那四本装的是节点（UnitVisual / Node3D / Label3D），三本装的是纯值，
        // 一本通用遍历要两套分支，而分支漏掉哪一格就是「那个 id 拿到旧节点」。
        foreach (var node in _unitModels.Values.Concat(_buildingModels.Values)
                     .Concat(_flagModels.Values).OfType<Node>())
        {
            node.QueueFree();
        }

        foreach (var plate in _buildingPlates.Values)
        {
            plate.QueueFree();
        }

        _unitModels.Clear();
        _buildingModels.Clear();
        _buildingPlates.Clear();
        _flagModels.Clear();
        _unitVisuals.Clear();
        _unitPrevPos.Clear();
        _unitSpeedEma.Clear();

        // 悬停态一并丢掉：它记着上一世界的建筑 id，指针还压在同一块屏幕上。
        ExitPlacingMode(Loc.T("读档重摆"));
        _world.Construction.Hover.Clear();
    }

    /// <summary>按新状态重新生成所有实体节点。<c>QueueFree</c> 延到帧末，相机架仍可能持有待释放节点。</summary>
    private void RebuildPresentation()
    {
        foreach (var building in _world.Buildings)
        {
            _buildingModels[building.Id] = SpawnBuildingModel(building);
        }

        foreach (var unit in _world.Units)
        {
            _unitModels[unit.Id] = SpawnUnitModel(unit);
        }

        // 旗子模型走的是悬赏事件流；读档那一发不重放事件流，所以这里按当前旗全集直接摆一遍。
        RenderFlags();
    }

    /// <summary>相机回到新世界的焦点（读档后玩家看到的是新世界，不是刚才那一片）。</summary>
    private void RefocusCamera()
    {
        var anchor = _world.Buildings.FirstOrDefault(b => b.Id == "palace") ?? _world.Buildings.FirstOrDefault();
        if (anchor is null) return;

        _rig.SetPose(new Vector3(anchor.Position.X, 0f, anchor.Position.Y), _rig.Distance,
            _rig.YawDegrees, _rig.PitchDegrees);
        _rig.FollowTarget(null);
    }

    // === 调试读数与桥动词 ===

    /// <summary>存档区读数（调试桥的 <c>ui_read</c> 之外再给一发专念存档的）。</summary>
    public string SaveReadout() => _saves.Readout(SaveSlotCount);

    /// <summary>写档那一发（调试桥 <c>call</c>：不改逻辑状态，不进 Journal）。</summary>
    public string SaveWriteDebug(int slot)
    {
        SaveSlotNow(slot);
        return SaveReadout();
    }

    /// <summary>读档那一发（调试桥 <c>inject</c>：走词表，但<b>这一发不进 Journal</b>）。</summary>
    public string SaveLoadDebug(int slot)
    {
        LoadSlotNow(slot);
        return SaveReadout();
    }

    /// <summary>
    /// 存档那一束：<b>世界 + 前端设置</b>打包成一个 <see cref="ISaveable"/>。
    ///
    /// <para>为什么不分两个档：玩家按了 Esc 改了分辨率就关机，那一档是他要带回来的；
    /// 分成两件事就多一处「读回来了但设置没跟上」要查，而那种错在画面上才看得见。</para>
    /// </summary>
    private sealed class SaveBundle : ISaveable
    {
        private readonly LogicWorld _world;
        private readonly FrontEndSettings _settings;

        public SaveBundle(LogicWorld world, FrontEndSettings settings)
        {
            _world = world;
            _settings = settings;
        }

        public int SchemaVersion => 1;

        public void WriteState(SaveWriter w)
        {
            w.Obj("world", ow => _world.WriteState(ow));
            w.Obj("settings", os => _settings.WriteState(os));
        }

        public void ReadState(SaveReader r)
        {
            r.Obj("world", or => _world.ReadState(or));
            r.Obj("settings", os => _settings.ReadState(os));
        }
    }
}
