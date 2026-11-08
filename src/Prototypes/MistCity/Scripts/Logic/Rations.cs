#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DemoFactory.GugaFramework.Entities;

namespace DemoFactory.Prototypes.MistCity.Logic;

/// <summary>一个人的进食账 —— <b>欠多少是这一份真值</b>（玩家故事 5：想看见自己欠着多少）。</summary>
/// <param name="Id">身份（形如 <c>h1</c>）。</param>
/// <param name="Owe">这一轮里想吃没吃到的份数（0 = 不欠）。</param>
/// <param name="Eaten">这一轮里真正吃掉的份数。</param>
public sealed record Resident(string Id, double Owe, double Eaten);

/// <summary>一次进食结算的结果 —— 快照与 HUD 都读它（玩家故事 12：死的是谁，死在哪次结算）。</summary>
/// <param name="Index">第几次结算（从 1 起）。</param>
/// <param name="Step">结算发生在第几步。</param>
/// <param name="Consumed">这一轮里真正吃掉的总份数。</param>
/// <param name="Short">这一轮里差了多少份（供不上就是正的）。</param>
/// <param name="DiedIds">这次结算死掉的人的 id，按结算内的次序。</param>
public sealed record Settlement(int Index, long Step, double Consumed, double Short, IReadOnlyList<string> DiedIds);

/// <summary>
/// 进食账本 —— <b>每 60 步结算一次进食，供不上的人死</b>。
///
/// <para><b>每人一份、按步扣除</b>（spec「产出与扣除都按步发生」）。一顿饭是一轮
/// <see cref="CycleSteps"/> 步，所以一个人一轮的额度是 <c>CycleSteps</c> 份；
/// 每一轮逐份从食物池里扣，扣到了算吃到，扣不到记成欠账。</para>
///
/// <para><b>结算点只判一件事：谁这一轮没吃够，谁死</b>。它不再扣一次饭 ——
/// 重复扣就是「同一件事两处代码」，账立刻对不上。</para>
///
/// <para><b>一份食物先到先得</b>：逐步扣除按登记次序发，所以食物不够时
/// 排在后面的人先饿。这是可读的规则（读数上逐人一行写着谁欠了多少），
/// 不是隐形的运气。</para>
///
/// <para><b>死亡走实体层的移除次序</b>（摘册 → 腾引用 → 发事件）。人是实体层的一个实例，
/// id、序号、增删次序都走实体层那四条不变量；欠账与额度那两格是本类自己印的。</para>
/// </summary>
public sealed class Rations
{
    /// <summary>结算周期（步）。结算点只判进食、发死亡，产出不在这里。</summary>
    public const int CycleSteps = 60;

    private readonly EntityCore _entities;
    private readonly Dictionary<string, double> _owe = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _eaten = new(StringComparer.Ordinal);
    private readonly List<string> _order = [];

    private long _settlementIndex;
    private long _lastSettlementStep;
    private double _eatenThisCycle;
    private Settlement? _last;
    private readonly List<Settlement> _history = [];

    /// <param name="tick">步号读数，事件行用。</param>
    /// <param name="eventCapacity">实体层事件容量（人的增删远少于牌，给一个够用的数）。</param>
    public Rations(Func<long> tick, int eventCapacity = 512)
    {
        ArgumentNullException.ThrowIfNull(tick);
        _entities = new EntityCore(host: null, removal: null, tick: tick, eventCapacity: eventCapacity);
    }

    /// <summary>实体册（人的册子，与牌同格式不同册 —— 两类东西的增删次序各自要稳定）。</summary>
    public EntityCore Entities => _entities;

    /// <summary>在场的人，按登记次序。</summary>
    public IReadOnlyList<Resident> Residents
        => _order.Where(id => _owe.ContainsKey(id))
            .Select(id => new Resident(id, _owe[id], _eaten[id]))
            .ToArray();

    /// <summary>活着的几个人。</summary>
    public int AliveCount => _order.Count(id => _owe.ContainsKey(id));

    /// <summary>桌上现有的食物（份）。产出按步加，扣除按步扣。</summary>
    public double Food { get; private set; }

    /// <summary>欠着多少（全场合计）。玩家故事 5 的那一格。</summary>
    public double TotalOwe => _owe.Values.Sum();

    /// <summary>第几次结算（还没结算过是 0）。</summary>
    public long SettlementIndex => _settlementIndex;

    /// <summary>上一次结算发生在第几步（还没结算过是 0）。</summary>
    public long LastSettlementStep => _lastSettlementStep;

    /// <summary>上一次结算的结果（还没结算过是 null）。</summary>
    public Settlement? LastSettlement => _last;

    /// <summary>历次结算（供「谁死在第几次结算」那一格读）。</summary>
    public IReadOnlyList<Settlement> History => _history;

    /// <summary>开局发一份食物（够开局，不够一整轮结算 —— 取舍从第一分钟就存在，玩家故事 10）。</summary>
    /// <param name="food">开局食物份数。负数当场抛（一份食物都不会有，不是一份也没有）。</param>
    public void SeedFood(double food)
    {
        if (food < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(food), food,
                "开局食物不能是负数（一份食物都不会有，不是一份也没有）。");
        }

        Food += food;
    }

    /// <summary>登记一个人上册。</summary>
    /// <param name="id">身份（形如 <c>h1</c>）。重复 id 由实体层当场抛（id 永不复发）。</param>
    public string AddResident(string id)
    {
        _entities.Spawn(id, KindTag);
        _owe[id] = 0.0;
        _eaten[id] = 0.0;
        _order.Add(id);
        return id;
    }

    /// <summary>一个人欠了多少（不在册回 0 —— 查询不抛）。</summary>
    public double OweOf(string id) => _owe.GetValueOrDefault(id);

    /// <summary>这个人还在不在。</summary>
    public bool IsAlive(string id) => _owe.ContainsKey(id);

    /// <summary>
    /// 这一步的产出到了几份 —— <b>不是结算</b>。
    /// 调用方（世界）在时钟推进一步之后调它，所以数字一直在爬。
    /// </summary>
    public void CreditProduction(double amount)
    {
        if (amount <= 0.0) return;
        Food += amount;
    }

    /// <summary>
    /// 扣掉一步的饭 —— <b>按步扣除</b>（spec「产出与扣除都按步发生」）。
    ///
    /// <para><b>每人每步一份，按登记次序先到先得</b>。桌上那份不够时，
    /// 排在后面的人这一步就记成欠账 —— 欠账每一步都在长，玩家看得见（玩家故事 5）。</para>
    ///
    /// <para><b>「只有欠着的人死」在这一发里有对象</b>：食物按步产出，
    /// 产出够三个人就三个人都吃饱、无人死；只够两个人，于是 h1 与 h2 每步都吃得到、
    /// h3 每步都欠 —— 结算那天死的只有 h3。要验到这一条，局面得是<b>按步的稳定产出</b>，
    /// 不是开局一次性囤一堆（囤够三个人的量就是三个人都活，囤不够就是三个人都死）。</para>
    ///
    /// <para><b>这一发是唯一的扣除口</b>：结算点不再扣一次 ——
    /// 「结算点只做一件事：判进食、发死亡」那一条的反面就是「结算点不重复扣」。</para>
    /// </summary>
    public void TickIntake()
    {
        var ids = _order.Where(id => _owe.ContainsKey(id)).ToArray();
        for (var i = 0; i < ids.Length; i++)
        {
            if (Food < 1.0)
            {
                // 扣不起：欠账 +1。这一格涨起来玩家看得见（快照每一步都印）。
                _owe[ids[i]] = _owe[ids[i]] + 1.0;
                continue;
            }

            Food -= 1.0;
            _eaten[ids[i]] = _eaten[ids[i]] + 1.0;
            _eatenThisCycle += 1.0;
        }
    }

    /// <summary>
    /// 结算 —— <b>每 <see cref="CycleSteps"/> 步判一次</b>。
    ///
    /// <para><b>判一件事：谁这一轮欠着，谁死</b>。饭已经逐步扣过了。
    /// 活下来的人的欠账与已吃额度都清零 —— 新的一轮从头算。</para>
    ///
    /// <para><b>「死者欠了多少」也进历史</b>：快照要能读出「谁死在第几次结算」，
    /// 不是只能读出「谁不在了」（ticket 06 的验收）。</para>
    /// </summary>
    /// <param name="nowStep">当前步号（结算发生的那一步）。</param>
    /// <returns>这次结算的结果（读数口）。</returns>
    public Settlement Settle(long nowStep)
    {
        _settlementIndex++;
        _lastSettlementStep = nowStep;

        var alive = _order.Where(id => _owe.ContainsKey(id)).ToArray();
        var eaten = _eatenThisCycle;
        _eatenThisCycle = 0.0;
        var shortBy = Math.Max(0.0, alive.Length * CycleSteps - eaten);

        // 欠着的人死。只有欠着的人死 —— 这一轮吃够的那些人这一次不受牵连。
        var died = new List<string>();
        for (var i = 0; i < alive.Length; i++)
        {
            var id = alive[i];
            var owed = _owe[id];
            _owe[id] = 0.0;
            _eaten[id] = 0.0;

            if (owed <= 0.0) continue;
            died.Add(id);
            _owe.Remove(id);
            _eaten.Remove(id);
            _entities.Remove(id);   // 死亡走实体层的移除次序
        }

        var result = new Settlement((int)_settlementIndex, nowStep, eaten, shortBy, died);
        _last = result;
        _history.Add(result);
        return result;
    }

    /// <summary>
    /// 进食账本进快照的那一段 —— 段首 <c>ration</c>。
    ///
    /// <para><b>一次读出四样</b>（ticket 06 的验收）：现在欠多少、离下个结算点还剩几步、
    /// 谁在第几次结算点死的、这一轮吃掉多少。</para>
    /// </summary>
    /// <param name="nowStep">当前步号。</param>
    public string DebugSnapshotSegment(long nowStep)
    {
        var sb = new StringBuilder();
        var alive = Residents;
        var toSettle = CycleSteps - (nowStep % CycleSteps);
        sb.Append("ration food=").Append(Num(Food))
            .Append(" owe=").Append(Num(TotalOwe))
            .Append(" alive=").Append(Num(alive.Count))
            .Append(" next=").Append(Num(toSettle))
            .Append(" settled=").Append(Num(_settlementIndex))
            .Append(" ate=").Append(Num(_eatenThisCycle))
            .Append(" dropped=").Append(Num(_entities.Events.Dropped))
            .Append('\n');

        foreach (var r in alive)
        {
            sb.Append("H ").Append(r.Id)
                .Append(" owe=").Append(Num(r.Owe))
                .Append(" ate=").Append(Num(r.Eaten))
                .Append('\n');
        }

        // 结算那一栏每次都印（无人死时 who=-）：玩家要能读出「上一次结算吃掉了多少」，
        // 那一格不能只在有人死的时候才出现在快照里（「日志里没有」≠「没发生」）。
        if (_last is { } last)
        {
            sb.Append("died at=").Append(Num(last.Index))
                .Append(" step=").Append(Num(last.Step))
                .Append(" ate=").Append(Num(last.Consumed))
                .Append(" short=").Append(Num(last.Short))
                .Append(" who=").Append(last.DiedIds.Count == 0 ? "-" : string.Join(",", last.DiedIds))
                .Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>人在实体层那一栏的类别串。</summary>
    public const string KindTag = "resident";

    /// <summary>数字的唯一格式化处（「double 与不变文化」）。</summary>
    private static string Num(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>数字的唯一格式化处（「double 与不变文化」）。</summary>
    private static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
