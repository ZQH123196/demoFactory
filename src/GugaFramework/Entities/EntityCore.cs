#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using DemoFactory.GugaFramework.Persistence;

namespace DemoFactory.GugaFramework.Entities;

/// <summary>
/// 实体册 —— 「在场的事全在一本册子里」（spec 1 节的判据）。
///
/// <para><b>册子答什么</b>：谁在场、id 是什么、第几个、什么次序。
/// <b>册子不答什么</b>：血多少、属于谁、走哪、有什么属性（D2、D3、D4）——
/// 「活着」只是一个查询口（<see cref="IEntityHost"/>），真值问宿主。</para>
///
/// <para><b>四条不变量</b>，各有测试钉着：
/// <list type="bullet">
/// <item><b>id 唯一且永不复发</b>：任何 id 一经发出全局不再用第二次，含读档恢复
/// （1 节子约束）。撞号当场抛——作者错误，不兜。</item>
/// <item><b>序号单调、永不复发</b>：册子发，原型拼 id 字符串（D8）。</item>
/// <item><b>稳定遍历次序 = 登记次序</b>（D10）：序号那一格本身就是次序，无第二份真值。</item>
/// <item><b>摘册不挪次序</b>（D14）：每条占一个永久槽，摘册只把那一格标成不在册。
/// 复用同一个 id 复登（<see cref="Register"/>）回到它首次登记的那一格（D9）。</item></list></para>
///
/// <para><b>为什么是「永久槽」而不是一份活 id 的表</b>：有宿主在环路重建时复用同一个 id
/// 对象（居民环路那类：拆掉又按同样的身份装回去）是刻意的，复用不新增登记、于是次序不动。
/// 用一份活表就表达不出「复登回原位」，而那正是 D14 的判据。</para>
/// </summary>
public sealed class EntityCore
{
    /// <summary>事件流缺省容量。给少了丢事件、给 0 是没接上，所以不兜（与建造层同口径）。</summary>
    public const int DefaultEventCapacity = 256;

    /// <summary>
    /// 永久槽。<b>摘册只置 <see cref="Live"/> 为 null</b>，槽本身与 id、序号、kind 都留着
    /// —— 「这个 id 发过」这件事必须活到读档之后（id 永不复发），载荷不活（放旗/腾引用，D11）。
    /// </summary>
    private sealed class Slot
    {
        public Slot(string id, long seq, string kind, EntityRecord? live)
        {
            Id = id;
            Seq = seq;
            Kind = kind;
            Live = live;
        }

        public string Id { get; }

        public long Seq { get; }

        /// <summary>摘册后仍留着（短字符串，不是一个对象图）；复登时可以换。</summary>
        public string Kind { get; set; }

        /// <summary>不在册时是 null。</summary>
        public EntityRecord? Live { get; set; }
    }

    private readonly List<Slot> _slots = [];
    private readonly List<int> _live = [];                                  // 在册槽的序数，恒升序
    private readonly Dictionary<string, int> _slotOf = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EntityRecord> _byId = new(StringComparer.Ordinal);
    private readonly IEntityHost? _host;
    private readonly IEntityRemoval? _removal;
    private readonly Func<long> _tick;
    private long _seq;

    /// <param name="host">活的查询口。<b>不递 = 全活</b>（D6，照 <c>IConstructionHost</c> 的形状）。</param>
    /// <param name="removal">移除时那两步的接线。<b>不递 = 那两步没发生</b>，读数里读得出来。</param>
    /// <param name="tick">步号读数，事件行用。不递 = 0（空场子与离线测试不用造一个时钟）。</param>
    /// <param name="eventCapacity">事件流容量。</param>
    public EntityCore(
        IEntityHost? host = null,
        IEntityRemoval? removal = null,
        Func<long>? tick = null,
        int eventCapacity = DefaultEventCapacity)
    {
        _host = host;
        _removal = removal;
        _tick = tick ?? (static () => 0L);
        Events = new EntityEventLog(eventCapacity);
    }

    /// <summary>事件出口（D12：三个，别更多）。</summary>
    public EntityEventLog Events { get; }

    /// <summary>在册几条。</summary>
    public int Count => _live.Count;

    /// <summary>册子一共发过多少个序号（<see cref="Spawn"/> 那一发各发一个）。</summary>
    public long Issued => _seq;

    /// <summary>下一个 <see cref="Spawn"/> 会拿到的那一格。存档与读数用。</summary>
    public long NextSeq => _seq + 1;

    /// <summary>发过多少个 id（含已摘册的）—— 「id 永不复发」那一条的账。</summary>
    public int IssuedIds => _slots.Count;

    /// <summary>在册 id，按登记次序（D10）。</summary>
    public IReadOnlyList<string> Ids
    {
        get
        {
            var ids = new string[_live.Count];
            for (var i = 0; i < ids.Length; i++) ids[i] = _slots[_live[i]].Id;
            return ids;
        }
    }

    /// <summary>在册的四格，按登记次序（D10）。</summary>
    public IReadOnlyList<EntityRecord> Records
    {
        get
        {
            var list = new EntityRecord[_live.Count];
            for (var i = 0; i < list.Length; i++) list[i] = _slots[_live[i]].Live!;
            return list;
        }
    }

    /// <summary>这一条在不在册。</summary>
    public bool Contains(string id) => _byId.ContainsKey(id);

    /// <summary>查一条。<b>不在册回 null</b>，不抛 —— 「查不到」是查询的正常答案。</summary>
    public EntityRecord? Find(string id) => _byId.GetValueOrDefault(id);

    /// <summary>
    /// 活的查询口（D6）。不在册 = false；在册 = 问宿主；<b>不递宿主 = 全活</b>。
    /// 册子里没有这一格真值（D2），所以这一发永远是转发。
    /// </summary>
    public bool Alive(string id) => _byId.TryGetValue(id, out _) && (_host?.Alive(id) ?? true);

    /// <summary>
    /// 生成一条上册 —— <b>发一个新序号</b>（D8）。
    ///
    /// <para><b>id 字符串由原型自己拼</b>：册子只管那一格序号不重发。像 <c>p01</c>
    /// 那种固定字符串进了 Journal 与重放基线，格式一个字不动；「前缀 + 册子那一格」
    /// 那一种也一样，格式归原型。</para>
    /// </summary>
    /// <param name="id">原型拼好的身份。</param>
    /// <param name="kind">短字符串：这一条是哪一类。</param>
    /// <param name="payload">不透明载荷（D7）：册子存它、还它、不读它。</param>
    /// <exception cref="ArgumentException">id 或 kind 是空白。</exception>
    /// <exception cref="InvalidOperationException">这个 id 发过了（1 节子约束）。</exception>
    public EntityRecord Spawn(string id, string kind, object? payload = null)
    {
        RequireId(id);
        RequireKind(kind);
        if (_slotOf.TryGetValue(id, out var taken))
        {
            // 报序号（册子发的那一格），不报槽序数 —— 后者是内部实现细节，
            // 作者拿它对不上任何一处读数，而序号在快照与事件行里都能查到（D8）。
            throw new InvalidOperationException(
                $"{id} 这个 id 发过了，它那一格序号是 {_slots[taken].Seq}："
                + "「id 唯一且永不复发」是实体层的硬账。"
                + "同一个 id 再上一次册走 Register（不复活登记口），那一条不发新序号");
        }

        var seq = ++_seq;
        var record = new EntityRecord(id, seq, kind, payload);
        var slot = new Slot(id, seq, kind, record);
        _slotOf[id] = _slots.Count;
        _slots.Add(slot);
        _byId[id] = record;
        InsertLive(_slots.Count - 1);
        Emit(new EntitySpawned(seq, kind) { Subject = id });
        return record;
    }

    /// <summary>
    /// 复用一个既有 id 上册（D9 的不复活登记口）—— <b>不发新序号</b>。
    ///
    /// <para>现役用法是居民环路那类：重建时复用同一个 id 对象是刻意的，
    /// 复用不新增登记，
    /// 于是<b>次序仍是它首次登记的那一格</b>（D14）——这是 D10 的延伸，不是它的例外。</para>
    ///
    /// <para>与 <see cref="Spawn"/> 的差别就两处：序号不涨、事件是
    /// <see cref="EntityReRegistered"/>。走错那一条的后果不是报错，是次序悄悄挪了一格、
    /// 同种子重跑不再逐字相同。</para>
    /// </summary>
    /// <param name="id">要复用的那个 id（必须发过）。</param>
    /// <param name="kind">短字符串：这一条是哪一类。</param>
    /// <param name="payload">不透明载荷（D7）。</param>
    /// <exception cref="InvalidOperationException">
    /// 这个 id 没发过（那是 <see cref="Spawn"/> 的活），或者它还在册（那不是复登，是重复登记）。
    /// </exception>
    public EntityRecord Register(string id, string kind, object? payload = null)
    {
        RequireId(id);
        RequireKind(kind);
        if (!_slotOf.TryGetValue(id, out var slotIndex))
        {
            throw new InvalidOperationException(
                $"{id} 这个 id 没发过：复登走的是「拿回一个发过的 id」，"
                + "第一次上册走 Spawn（那一条才发序号）");
        }

        var slot = _slots[slotIndex];
        if (slot.Live is not null)
        {
            throw new InvalidOperationException(
                $"{id} 还在册上（第 {slotIndex} 号槽），这一发不是复登是重复登记："
                + "一条在场的东西不需要再上一次册");
        }

        var record = new EntityRecord(id, slot.Seq, kind, payload);
        slot.Kind = kind;
        slot.Live = record;
        _byId[id] = record;
        InsertLive(slotIndex);
        Emit(new EntityReRegistered(slot.Seq, kind) { Subject = id });
        return record;
    }

    /// <summary>
    /// 把册子截回快照里的那一格（<b>只给「全量快照原样还原」用</b>）：
    /// 丢掉 <paramref name="slotCount"/> 之后新发的槽，并把 <see cref="Issued"/> 拨回原数。
    ///
    /// <para><b>为什么需要它</b>：读档是换一局，序号只会往前；重试是同一局回到过去，
    /// 战斗里新召的人必须连「这个 id 发过」这件事一起作废，否则重开那一局会发 <c>h14</c>
    /// 而不是原来的 <c>h13</c>，逐字比对当场漂。</para>
    ///
    /// <para>槽次序不变（只截尾），<see cref="Ids"/> 与在册表按前缀重建。</para>
    /// </summary>
    /// <param name="slotCount">快照那一刻的 <see cref="IssuedIds"/>。</param>
    /// <param name="issued">快照那一刻的 <see cref="Issued"/>。</param>
    /// <exception cref="ArgumentOutOfRangeException">槽数或序号越界。</exception>
    public void TruncateTo(int slotCount, long issued)
    {
        if (slotCount < 0 || slotCount > _slots.Count)
            throw new ArgumentOutOfRangeException(nameof(slotCount), slotCount, "槽数超出册子现有范围");
        if (issued < 0 || issued > _seq)
            throw new ArgumentOutOfRangeException(nameof(issued), issued, "序号超出册子现有范围");

        for (var i = _slots.Count - 1; i >= slotCount; i--)
        {
            var id = _slots[i].Id;
            _slots.RemoveAt(i);
            _byId.Remove(id);
            _slotOf.Remove(id);
        }

        _seq = issued;
        _live.Clear();
        _byId.Clear();
        for (var i = 0; i < _slots.Count; i++)
        {
            var live = _slots[i].Live;
            if (live is null) continue;
            _live.Add(i);
            _byId[live.Id] = live;
        }
    }

    /// <summary>
    /// 摘册 —— <b>内部次序固定，四步一步不缺</b>（D11）：
    /// ① 摘册 → ② 寻路层定身与退注册 → ③ 放旗/腾引用 → ④ 发事件。
    ///
    /// <para>②③ 各由 <see cref="IEntityRemoval"/> 那两个成员做，缺哪个是接线漏了，
    /// 读数里说得出来（<see cref="Readout"/> 那一行「移除接线」）。</para>
    ///
    /// <para>② 为什么必须有：「退场必须先在寻路层定住，否则还在挪」——
    /// 位置真值住在寻路层的流水线里，不换命令它会照着手上那条命令继续走，
    /// 而一个已经不在世界里的单位还在挪，<b>读数上查不出来</b>。这条坑三个原型各踩过一次
    /// （各宿主退场那一发各写了一份，其中一份还挂在阶段结算那一档里），
    /// 收编之后只踩一次。</para>
    /// </summary>
    /// <param name="id">摘哪一条。</param>
    /// <returns>摘掉了 = true；本来就不在册 = false（不是错误：摘一个不存在的东西没有话说）。</returns>
    /// <remarks>
    /// ②③ 那两步拿到的是<b>那一条本身</b>（<see cref="EntityRecord"/>）而不是 id：
    /// 它们跑在①之后，那时册子里已经查不到它了，而它们要的正是它的载荷。
    /// </remarks>
    public bool Remove(string id)
    {
        if (!_byId.Remove(id, out var record)) return false;

        // ① 摘册。槽留着（id 永不复发 + 复登回原位，D9/D14），载荷在这一发里丢掉（腾引用）。
        var slotIndex = _slotOf[id];
        _slots[slotIndex].Live = null;
        _live.RemoveAt(FindLiveIndexOf(slotIndex));

        // ②③ 寻路层那一步与宿主那一步，次序不许反：先定身（别再挪），再腾引用（别再被谁拿着）。
        // 递的是那一条本身：① 已经把它从册子里摘了，宿主这时回头按 id 查册子必然查不到，
        // 拿到 null 就静默不做事 —— 而那正是「退场还在挪」这一坑的形状。
        _removal?.DetainInNav(record);
        _removal?.Release(record);

        // ④ 事件放最后：订阅者看到这一条时，册子、寻路层、宿主引用三边都已经落定。
        Emit(new EntityRemoved(record.Seq, record.Kind) { Subject = id });
        return true;
    }

    /// <summary>检查点快照段（状态级）。段首是 <c>entity</c>（D15）。</summary>
    public string DebugSnapshotSegment() => RenderSegment();

    /// <summary>
    /// 重放快照段。内容与调试段同一份格式化 —— 两个入口两个名字，格式只有一处
    /// （照 <c>ConstructionCore</c> 与 <c>NavStepper</c> 同一口径）。
    ///
    /// <para><b>段首是 <c>entity</c>，与各世界自己的段首无关</b>（D15）：有的宿主用大写 <c>U</c>、
    /// 有的用小写 <c>u</c>，那两处都是各自既有的重放基线，碰它就要重录基线，
    /// 而且当场破「同种子重跑逐字相同」。世界把本段嵌进自己那份，中间那几段照旧。</para>
    /// </summary>
    public string ReplaySnapshotSegment() => RenderSegment();

    /// <summary>一行读数：调试第一个要问的那几个数（spec D4 的理由）。</summary>
    public string Readout()
        => $"实体层 在册 {EntityNumbers.Format(Count)} 条｜发过 {EntityNumbers.Format(IssuedIds)} 个 id｜" +
           $"下一个序号 {EntityNumbers.Format(NextSeq)}｜事件漏登 {EntityNumbers.Format(Events.Dropped)} 条｜" +
           $"宿主 {(_host is null ? "不接（全活）" : "接上")}｜移除接线 {(_removal is null ? "不接（那两步没发生）" : "接上")}";

    /// <summary>
    /// 写档（存档层是原型那一半的边界，字段清单归原型自己写；这里只写册子那三格与在册标志）。
    ///
    /// <para><b>写全部槽，不只写在册的</b>：已摘册的那些槽带着 id 与序号一起进档，
    /// 「id 唯一且永不复发」那一条要活过读档（1 节子约束）——
    /// 只写在册的，读回来就有一段「发过的 id」不见了，于是又能发第二次。
    /// 死槽的载荷不留：那一份已经腾掉了。</para>
    ///
    /// <para><b>载荷不进档</b>（D7）：它是原型的数据，读档时按 id 从原型自己那份对回去
    /// （<see cref="ReadState"/> 的 <c>payloadOf</c>）。</para>
    /// </summary>
    public void WriteState(SaveWriter w)
    {
        ArgumentNullException.ThrowIfNull(w);
        w.Arr("rows", l =>
        {
            foreach (var slot in _slots)
            {
                l.Obj(o =>
                {
                    o.Str("id", slot.Id);
                    o.Long("seq", slot.Seq);
                    o.Str("kind", slot.Kind);
                    o.Bool("live", slot.Live is not null);
                });
            }
        });
    }

    /// <summary>
    /// 读档：把册子整个重建（槽、序号、在册表全清，再按档里的次序装回去）。
    ///
    /// <para><b>这一发不发事件</b>（D12）：读档是一次调用，不是「有一条上了册」。
    /// 订阅者要的是业务事件，恢复过程不是业务。</para>
    ///
    /// <para><b>载荷由 <paramref name="payloadOf"/> 按 id 还回来</b>：册子不读载荷、
    /// 也不知道它是什么（D7）。不递 = 全是 null（纯册子的往返测试与空场子够用）。</para>
    /// </summary>
    /// <param name="r">读侧游标。键序照 <see cref="WriteState"/> 那一串，不按键查找。</param>
    /// <param name="payloadOf">按 id 交还原型自己那份数据。</param>
    /// <exception cref="FormatException">档里两行同一个 id，或序号不是单调递增。</exception>
    public void ReadState(SaveReader r, Func<string, object?>? payloadOf = null)
    {
        ArgumentNullException.ThrowIfNull(r);
        _slots.Clear();
        _live.Clear();
        _slotOf.Clear();
        _byId.Clear();
        Events.ClearKept();
        // 从 −1 起，于是第一行的检查与后面几行走同一句「必须严格递增」——
        // 档里两行共用一个序号同样是坏档（每次发序号都开一格，两行同号不可能由本层写出）。
        _seq = -1;

        r.Arr("rows", l =>
        {
            var lastSeq = 0L;
            while (!l.Done)
            {
                string id = "";
                string kind = "";
                var live = false;
                l.Obj(o =>
                {
                    // 读键序必须与 WriteState 写键序逐字一致：读侧是「按键顺序读，不按键查找」
                    // （SaveReader 的口径），对不上当场抛并把两个键名都念出来。
                    id = o.Str("id");
                    lastSeq = o.Long("seq");
                    kind = o.Str("kind");
                    live = o.Bool("live");
                });

                RequireId(id);
                RequireKind(kind);
                if (lastSeq <= _seq)
                {
                    throw new FormatException(
                        $"{id} 的序号是 {EntityNumbers.Format(lastSeq)}，没有比档里前一行大："
                        + "槽次序与序号必须一起严格递增（写档照槽次序写，读档照同一份次序读）");
                }

                if (!_slotOf.TryAdd(id, _slots.Count))
                {
                    throw new FormatException(
                        $"{id} 在档里出现了两次：「id 唯一且永不复发」，档里出现两次就是这份档坏了");
                }

                _seq = lastSeq;
                var record = live ? new EntityRecord(id, lastSeq, kind, payloadOf?.Invoke(id)) : null;
                _slots.Add(new Slot(id, lastSeq, kind, record));
                if (record is not null)
                {
                    _byId[id] = record;
                    _live.Add(_slots.Count - 1);
                }
            }
        });
    }

    // ---- 内部 ----

    /// <summary>
    /// 快照段：<b>一行摘要 + 每条一行</b>。格式化只有这一处 ——
    /// 原型把这段嵌进自己的 DebugSnapshot / ReplaySnapshot，列要是两处各拼一次就会漂。
    /// 只印册子自己的三格；不透明载荷是原型的私产，印进检查点快照等于让册子替它保管会不会变（D7）。
    /// </summary>
    private string RenderSegment()
    {
        var sb = new StringBuilder();
        sb.Append("entity count=").Append(EntityNumbers.Format(Count))
            .Append(" issued=").Append(EntityNumbers.Format(_seq))
            .Append('\n');

        foreach (var index in _live)
        {
            var r = _slots[index].Live!;
            sb.Append(r.Id)
                .Append(" seq=").Append(EntityNumbers.Format(r.Seq))
                .Append(" kind=").Append(r.Kind)
                .Append('\n');
        }

        return sb.ToString();
    }

    private void Emit(EntityEventPayload e)
    {
        e = e with { Tick = _tick() };
        Events.Add(e);
    }

    private void InsertLive(int slotIndex)
    {
        // 二分插进恒升序那一份：遍历走的是它，于是遍历代价是在册条数，不是历史条数。
        var lo = 0;
        var hi = _live.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (_live[mid] < slotIndex) lo = mid + 1;
            else hi = mid;
        }

        _live.Insert(lo, slotIndex);
    }

    private int FindLiveIndexOf(int slotIndex)
    {
        var lo = 0;
        var hi = _live.Count - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            var v = _live[mid];
            if (v == slotIndex) return mid;
            if (v < slotIndex) lo = mid + 1;
            else hi = mid - 1;
        }

        throw new InvalidOperationException($"槽 {slotIndex} 不在在册表里：册与槽分家了（作者错误）");
    }

    private static void RequireId(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            throw new ArgumentException("id 不能是空白：册子靠它认人，空 id 进册之后没有任何一处能查它", nameof(id));
        }
    }

    private static void RequireKind(string kind)
    {
        if (string.IsNullOrEmpty(kind))
        {
            throw new ArgumentException(
                "kind 不能是空白：「这一场有哪些 kind」是调试第一个要问的问题，空 kind 答不了这一问", nameof(kind));
        }
    }

    public override string ToString() => $"entity 在册 {EntityNumbers.Format(Count)} 条";
}
