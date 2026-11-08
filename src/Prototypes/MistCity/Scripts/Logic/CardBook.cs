#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DemoFactory.GugaFramework.Entities;

namespace DemoFactory.Prototypes.MistCity.Logic;

/// <summary>
/// 牌的自有字段 —— <b>不进实体层的载荷语义，由原型自己印进快照</b>（spec「牌是实体层的一个实例」）。
///
/// <para>实体层的快照段只印 id、序号与类别（<c>EntityCore.RenderSegment</c>），
/// 牌自己的三格由本模块印。载荷是实体层的黑盒（存而不读、不进快照），
/// 所以牌的数据住在本模块这一份表里，按 id 查。</para>
/// </summary>
/// <param name="Kind">牌种。</param>
/// <param name="IntervalSteps">产出间隔（步）。0 = 不产出。</param>
/// <param name="Produced">累计产出了几份食物（调试读数与「哪条产线在干活」的判据）。</param>
/// <param name="LivedIn">它原来住在哪张房屋牌上 —— <b>只在住过的那张被吃掉之后才有值</b>。
/// 那时这张牌留在桌面上、退回自由平面，而牌面上要还看得出它原本是这间房的住户
/// （画面上只看得出「一张牌不动了」，读数上查不出它从哪来）。
/// 玩家自己把住户牌拿下来不算住过 —— 那是它自己选择的下家，所以那一格是空的。</param>
public sealed record CardState(CardKind Kind, int IntervalSteps, double Produced, string? LivedIn);

/// <summary>
/// 牌册 —— <b>牌是实体层的一个实例</b>（id、序号、增删次序直接用实体层）。
///
/// <para><b>为什么一半用实体层一半自己存</b>：身份与次序是实体层那四条不变量的管辖范围，
/// 牌不另造一套；而牌的自有字段（类别、间隔、累计产出）不在实体层那条四格里，
/// 为它扩实体层就得让所有宿主理解「自有字段」这个概念（spec 的原话）。</para>
///
/// <para><b>事件容量构造时显式抬高</b>（spec「事件默认容量对不上牌的数量」）：
/// 默认 256 对一桌几十张牌 + 每 60 步一次结算的规模不够，
/// 给少了丢事件、而「日志里没有」不等于「没发生」。</para>
/// </summary>
public sealed class CardBook
{
    /// <summary>事件流容量。开局的牌加上后面合成出来的与批次销毁，按一桌的规模给。</summary>
    public const int EventCapacity = 2048;

    private readonly EntityCore _entities;
    private readonly Dictionary<string, CardState> _states = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _seqOf = new(StringComparer.Ordinal);
    private long _issued;

    /// <param name="tick">步号读数，事件行用。</param>
    public CardBook(Func<long> tick)
    {
        ArgumentNullException.ThrowIfNull(tick);
        _entities = new EntityCore(host: null, removal: null, tick: tick, eventCapacity: EventCapacity);
    }

    /// <summary>实体册（只读那一半：册、事件出口、查找）。写路径全部在本类那几个方法上。</summary>
    public EntityCore Entities => _entities;

    /// <summary>在册几张牌。</summary>
    public int Count => _entities.Count;

    /// <summary>在册的牌 id，按登记次序。</summary>
    public IReadOnlyList<string> Ids => _entities.Ids;

    /// <summary>册子一共发过多少个序号（序号永不复发，合成消耗掉的也占着）。</summary>
    public long Issued => _entities.Issued;

    /// <summary>下一张牌会拿到的那一格序号。</summary>
    public long NextSeq => _entities.NextSeq;

    /// <summary>一个牌 id 在不在册。</summary>
    public bool Contains(string id) => _entities.Contains(id);

    /// <summary>查一张牌的自有字段。<b>不在册回 null</b>，不抛 ——「查不到」是查询的正常答案。</summary>
    public CardState? Find(string id) => _states.GetValueOrDefault(id);

    /// <summary>
    /// 查一张牌的自有字段。<b>不在册当场抛</b> —— 写路径要的是 id（不是它的字段），
    /// 这一发是给「已经拿到字段还要再查一遍」那种场合用的。
    /// </summary>
    public CardState Require(string id)
        => _states.TryGetValue(id, out var state)
            ? state
            : throw new KeyNotFoundException(
                $"牌 {id} 不在册：这一发要它的字段，而册子里没有它（可用：{string.Join(" / ", Ids)}）");

    /// <summary>牌种的牌在不在册（按类别查，合成与结算要）。</summary>
    public IReadOnlyList<string> IdsOf(CardKind kind)
        => _entities.Ids.Where(id => _states[id].Kind == kind).ToArray();

    /// <summary>
    /// 放一张牌进世界 —— <b>本原型的第一条写路径</b>。
    /// 玩家点选与调试注入调的是这一个方法（spec「一个 seam：命令进，快照出」）。
    /// </summary>
    /// <param name="kind">牌种。不在牌种表里当场抛（不兜一个默认牌种）。</param>
    /// <returns>新牌的 id（形如 <c>c07</c>，序号那一格是实体层发的）。</returns>
    public string Place(CardKind kind)
    {
        var spec = CardCatalog.Of(kind);
        var seq = _entities.NextSeq;
        var id = IdOf(seq);

        // 实体层发序号（D8：单调递增、永不复发）；本模块只负责拼 id 那一串与记自有字段。
        var record = _entities.Spawn(id, KindTag, spec.Kind);
        _states[id] = new CardState(spec.Kind, spec.IntervalSteps, Produced: 0.0, LivedIn: null);
        _seqOf[id] = (int)record.Seq;
        _issued++;
        return id;
    }

    /// <summary>
    /// 摘掉一张牌 —— <b>走实体层的移除次序</b>（摘册 → 腾引用 → 发事件，spec「牌能进能出」）。
    ///
    /// <para><b>挂接不归这一发清</b>：本类不递 removal（牌不占寻路格），
    /// 绑定关系由世界持有，所以两条调用路径各自先腾引用再摘牌 ——
    /// <c>MistCityWorld.CombineInto</c> 与 <c>MistCityWorld.Discard</c>。
    /// 顺序反了会在挂接表里留下指向不存在牌的悬空端。</para>
    /// </summary>
    /// <returns>摘掉了 = true；本来就不在册 = false。</returns>
    public bool Remove(string id)
    {
        if (!_states.Remove(id)) return false;
        _seqOf.Remove(id);
        return _entities.Remove(id);
    }

    /// <summary>记一次产出（每步产出累加到牌自己那一格）。</summary>
    public void CreditProduction(string id, double amount)
    {
        if (amount <= 0.0) return;
        if (!_states.TryGetValue(id, out var state)) return;
        _states[id] = state with { Produced = state.Produced + amount };
    }

    /// <summary>
    /// 记下它原来住在哪张房屋牌上 —— <b>它那间房被合成吃掉时走这一发</b>。
    ///
    /// <para><b>只记一次，不覆盖</b>：它后来又住进别处，那一处由挂接表回答；
    /// 这一格记的是「它被腾下来过」这件事，腾过几次都指最早那间。</para>
    ///
    /// <para><b>牌不在册当场抛</b>（与别处同一口径）：写路径给的 id 必须是在册的那张。</para>
    /// </summary>
    /// <param name="cardId">住户牌。</param>
    /// <param name="houseId">它原来住在的那张房屋牌。</param>
    public void MarkResidentOf(string cardId, string houseId)
    {
        if (!_states.TryGetValue(cardId, out var state))
        {
            throw new InvalidOperationException(
                $"牌 {cardId} 不在桌上：它身上的挂接记了住处，牌却已经不在册了"
                + $"（可用：{string.Join(" / ", Ids)}）。腾引用必须早于摘牌。");
        }

        if (state.LivedIn is not null) return;
        _states[cardId] = state with { LivedIn = houseId };
    }

    /// <summary>发过多少张牌（含已消耗掉的）—— 「序号不因移除而复用」那一条的账。</summary>
    public long IssuedCount => _issued;

    /// <summary>一张牌在册时的序号（不在册回 0 —— 0 是实体层发号的第一格之前的占位）。</summary>
    public long SeqOf(string id) => _seqOf.GetValueOrDefault(id);

    /// <summary>
    /// 牌册进快照的那一段 —— <b>段首 <c>card</c></b>，摘要一行 + 每张一行。
    ///
    /// <para>实体层那一段（<c>entity count=…</c>）由 <see cref="EntityCore.DebugSnapshotSegment"/>
    /// 自己印，本段只搬 token、只补牌自己的四格 —— 一个数都不重算（spec「牌的自有字段要原型自己印」）。
    /// <c>livedin</c> 那一格是「被腾下来的住户牌原来住在谁身上」，
    /// 没人住过时印 <c>-</c>（与结算那一栏的 <c>who=-</c> 同一口径：不因为没有内容就不印）。</para>
    /// </summary>
    public string DebugSnapshotSegment()
    {
        var sb = new StringBuilder();
        sb.Append("card count=").Append(Num(Count))
            .Append(" issued=").Append(Num(_issued))
            .Append(" nextseq=").Append(Num(NextSeq))
            .Append(" dropped=").Append(Num(_entities.Events.Dropped))
            .Append('\n');

        foreach (var id in _entities.Ids)
        {
            var state = _states[id];
            sb.Append("C ").Append(id)
                .Append(" seq=").Append(Num(SeqOf(id)))
                .Append(" kind=").Append(state.Kind)
                .Append(" every=").Append(Num(state.IntervalSteps))
                .Append(" out=").Append(Num(state.Produced))
                .Append(" livedin=").Append(state.LivedIn ?? "-")
                .Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>牌在实体层那一栏的类别串（册子里那一个短字符串）。</summary>
    public const string KindTag = "card";

    /// <summary>id 的形状：前缀 + 序号两位。<b>格式一个字不许中途改</b>（进 Journal 与重放基线）。</summary>
    internal static string IdOf(long seq) => "c" + seq.ToString("D2", CultureInfo.InvariantCulture);

    /// <summary>数字的唯一格式化处（「double 与不变文化」）。</summary>
    internal static string Num(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>数字的唯一格式化处（「double 与不变文化」）。</summary>
    internal static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
