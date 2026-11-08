#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DemoFactory.Prototypes.MistCity.Logic;

/// <summary>
/// 挂接是哪一种 —— <b>住户与产线记在同一份表里，靠这一格区分</b>。
///
/// <para><b>拆成两份的代价大于省下的</b>：挂接的建立、拆除，
/// 以及承载者被合成消耗时的连带清理，这三件事对两种挂接是同一套规则。
/// 拆开维护两份，漏掉的那一份会留下一条指向已消耗承载者的悬挂挂接 ——
/// 而那种故障在画面上只看得出「牌不见了」。</para>
///
/// <para><b>数据形状一样，产出物不一样</b>：产线的产出物是份食物，
/// 住户的产出物是人。住客住满之后生不生人由后面那张 ticket定义，
/// 这一格现在只负责把两种挂接分开，不替它们编产出。</para>
/// </summary>
public enum BindingKind
{
    /// <summary>产线：占用者按自己的产出间隔产食物。</summary>
    Production,

    /// <summary>住户：占用者住进房屋，不产食物。</summary>
    Resident,
}

/// <summary>一条绑定的两端 —— 一张牌压在另一张上（spec「派工把牌绑到另一张上」）。</summary>
/// <param name="CardId">被绑的那张（产出的那一张，或住进去的那一张）。</param>
/// <param name="OntoId">被压着的那一张。</param>
/// <param name="Kind">这一条是产线还是住户（<see cref="BindingKind"/>）。</param>
/// <param name="IntervalSteps">产出间隔（以步计）—— 取的是 <paramref name="CardId"/> 自己那一份。住户那一档恒为 0。</param>
/// <param name="NextAtStep">下一次产出落在第几步。</param>
/// <param name="Produced">这条绑定累计产出了几份。</param>
public sealed record Binding(
    string CardId,
    string OntoId,
    BindingKind Kind,
    int IntervalSteps,
    long NextAtStep,
    double Produced)
{
    /// <summary>还差几步到下一次产出（玩家故事 3：想看见每张绑定上的剩余步数）。</summary>
    public long RemainingSteps(long nowStep) => Math.Max(0L, NextAtStep - nowStep);
}

/// <summary>
/// 绑定关系模块 —— <b>持有「一张牌压在另一张上」这份关系</b>。
///
/// <para><b>为什么自建而不进建造层</b>（ADR 0017）：建造层的 seam 切在放置规则上，
/// 只答「这个候选点能不能放、实际落在哪」，整套判据建立在占地与坐标上。
/// 绑定既不占地也没有坐标，逻辑上只是一条引用，塞进建造层要做四件事
/// （给不存在的坐标开入参、给「不占地」开一档规则、把引用塞进不透明载荷、
/// 让每个建造调用方都要理解新规则），代价大于收益。</para>
///
/// <para><b>收编判据</b>：产物进不进最终包。第二个宿主真要用同一形状时才搬进公共层，
/// 不因为这次用到了就搬。</para>
///
/// <para><b>参数缺了当场抛，一条例外都没有</b>（spec）：牌 id 不存在、绑定目标不存在、
/// 重复绑定、解绑一张没绑过的牌 —— 全部当场抛，不返回失败码、不兜 0。
/// 兜 0 在这里等于静默摆到第 0 排。</para>
///
/// <para><b>遍历次序 = 绑定成立的次序</b>：与实体层那条「稳定遍历次序 = 登记次序」同一口径，
/// 于是快照里绑定段的逐行次序在同种子重跑时逐字相同。</para>
///
/// <para><b>两个方向不是同一个形状</b>：承载者 → 占用者是多值（一张牌下面能叠多张），
/// 占用者 → 承载者是一对一（一张牌只有一份力气，压一处）。
/// 删掉「一牌只接一牌」那一版时只改了前者，后者有玩法意义，留着。</para>
///
/// <para><b>产线与住户记在同一份表里</b>，靠 <see cref="BindingKind"/> 那一格区分，
/// 而建立、拆除、连带清理这三件事两种挂接共用同一套规则
/// （见 <see cref="Remember"/> 与 <see cref="Detach"/>）。</para>
/// </summary>
public sealed class Bindings
{
    private readonly List<Binding> _order = [];
    private readonly Dictionary<string, Binding> _byCard = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _ontoOf = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _cardOnto = new(StringComparer.Ordinal);

    /// <summary>在册几条绑定。</summary>
    public int Count => _order.Count;

    /// <summary>全部绑定，按绑定成立的次序。</summary>
    public IReadOnlyList<Binding> All => _order;

    /// <summary>这张牌压在谁身上（没压着回 null —— 这是查询，不是写路径）。</summary>
    public string? OntoOf(string cardId) => _ontoOf.GetValueOrDefault(cardId);

    /// <summary>
    /// 谁压在这张牌身上 —— <b>一组，按压上去的次序</b>（没人压是一组空的）。
    ///
    /// <para>一张牌下面能叠多张，所以这里是多值。第一版那一档是单张牌，
    /// 那种形状在多张压同一张时只能记住其中一张，剩下的从册子里查不出来。</para>
    /// </summary>
    public IReadOnlyList<string> CardOnto(string ontoId)
        => _cardOnto.TryGetValue(ontoId, out var list) ? list : Array.Empty<string>();

    /// <summary>
    /// 挂在它身上的住户牌 —— <b>按住进去的次序</b>（没人住是一组空的）。
    ///
    /// <para><b>派工挂接不算数</b>：一张产粮牌压在房屋牌上是派工（玩家可以直接
    /// 用 <c>bind</c> 造出那一摞），不是住进去。所以数住客只能从这一档数，
    /// 别处不另抄一份「哪种挂接算住户」的判据。</para>
    /// </summary>
    /// <param name="ontoId">承载者 id。</param>
    public IReadOnlyList<string> ResidentsOf(string ontoId)
    {
        if (!_cardOnto.TryGetValue(ontoId, out var occupants)) return Array.Empty<string>();

        var found = new List<string>();
        foreach (var id in occupants)
        {
            if (_byCard.TryGetValue(id, out var b) && b.Kind == BindingKind.Resident) found.Add(id);
        }

        return found;
    }

    /// <summary>这张牌身上那条绑定（没有回 null）。</summary>
    public Binding? Find(string cardId) => _byCard.GetValueOrDefault(cardId);

    /// <summary>这张牌身上有没有绑定（点选时用来判要不要抛）。</summary>
    public bool Has(string cardId) => _byCard.ContainsKey(cardId);

    /// <summary>
    /// 派工：把一张牌绑到另一张上。
    ///
    /// <para><b>产出时刻从「绑成立那一步」起算</b>：下一次产出落在
    /// <c>nowStep + 间隔</c>。所以刚绑上那一瞬间不产出，推进恰好间隔那么多步才出第一份。</para>
    /// </summary>
    /// <param name="cards">牌册（两端在不在册由它答，错误消息里列得出可用牌）。</param>
    /// <param name="cardId">被绑的那张（产出的那一张）。</param>
    /// <param name="ontoId">被压着的那一张。</param>
    /// <param name="nowStep">当前步号（= 这一局已推的步数）。</param>
    /// <exception cref="InvalidOperationException">两个 id 有一个不在册（带可用牌清单）。</exception>
    /// <exception cref="InvalidOperationException">绑到自己、重复绑定、或被绑的那张不产出。</exception>
    public Binding Bind(CardBook cards, string cardId, string ontoId, long nowStep)
    {
        ArgumentNullException.ThrowIfNull(cards);

        // ① 两端都在册 —— 分两次判，错误消息里说得出是哪一端出的问题。
        var cardState = RequireCard(cards, cardId, "要绑的那张");
        var ontoState = RequireCard(cards, ontoId, "要压的那张");

        // ② 不能绑到自己。
        if (cardId == ontoId)
        {
            throw new InvalidOperationException(
                $"{cardId} 不能压在自己身上：一张牌压自己不成叠，产出会变成一条自己喂自己的空转。");
        }

        // ③ 被绑的那张必须自己会产出。间隔为 0 的牌只能当材料或当被压着的那张。
        if (cardState.IntervalSteps < 1)
        {
            throw new InvalidOperationException(
                $"{cardId}（{CardCatalog.Of(cardState.Kind).Label}）的产出间隔是 0，它不产出，派不了工："
                + "它只能当合成材料，或者当被压着的那一张。");
        }

        // ④ 重复绑定（同一张牌压两次）—— 一对一的那一格在占用者这一侧，一张牌只压一处。
        // 被压着的那张收几张不在这里判：第一版那条「一张牌只接一张」已经删掉了。
        if (_byCard.TryGetValue(cardId, out var existing))
        {
            throw new InvalidOperationException(
                $"{cardId} 已经压在 {existing.OntoId} 身上了：一张牌只能压一张。"
                + "要改产线先把上面那张拖到空白处拿下来，不损失材料。"
                + $"现在这条还在产出，累计 {Num(existing.Produced)} 份。");
        }

        var binding = new Binding(
            cardId, ontoId, BindingKind.Production, cardState.IntervalSteps, nowStep + cardState.IntervalSteps, 0.0);
        return Remember(binding);
    }

    /// <summary>
    /// 住进房屋 —— <b>叠到房屋牌上的那一档，产出物是人不是份食物</b>。
    ///
    /// <para><b>间隔恒为 0</b>，所以住户不产食物：这是「结果由底下那张牌决定」
    /// 那一条规则的结论，不是另立的一条规则。<see cref="Produce"/> 按
    /// <see cref="BindingKind"/> 跳过住户，所以它不会走进那份食物的账。</para>
    ///
    /// <para><b>住客上限住在牌种表那一份数据里</b>
    /// （<see cref="CardSpec.ResidentCap"/>，三种房屋都是 2），
    /// 满没满数的是本表 <see cref="ResidentsOf"/> 那一档。
    /// 所以这一发只负责把挂接建起来，落点判定那一格不必重写一遍住客上限。</para>
    /// </summary>
    /// <param name="cards">牌册（两端在不在册由它答，错误消息里列得出可用牌）。</param>
    /// <param name="cardId">住进去的那一张（占用者）。</param>
    /// <param name="ontoId">底下那张房屋牌（承载者）。</param>
    /// <param name="nowStep">当前步号（= 这一局已推的步数）。</param>
    /// <exception cref="InvalidOperationException">两端有一个不在册、绑到自己、
    /// 重复绑定，或底下那张不是房屋牌。</exception>
    public Binding MoveIn(CardBook cards, string cardId, string ontoId, long nowStep)
    {
        ArgumentNullException.ThrowIfNull(cards);

        var ontoState = RequireCard(cards, ontoId, "要住进去的那张房子");
        _ = RequireCard(cards, cardId, "要住进去的那张");

        if (cardId == ontoId)
        {
            throw new InvalidOperationException(
                $"{cardId} 不能压在自己身上：一张牌压自己不成叠，住户也不能住自己。");
        }

        // 底下必须是房屋牌。这条判据只有一处（牌种表那一列），落点判定那边也读它。
        if (!CardCatalog.IsHouse(ontoState.Kind))
        {
            throw new InvalidOperationException(
                $"{ontoId}（{CardCatalog.Of(ontoState.Kind).Label}）不是房屋牌："
                + "住进去要压在房屋上，压在产粮牌上是派工。");
        }

        if (_byCard.TryGetValue(cardId, out var existing))
        {
            throw new InvalidOperationException(
                $"{cardId} 已经压在 {existing.OntoId} 身上了：一张牌只压一处，"
                + "住在房屋上就一直住着，派到产粮牌上就一直产食物，两者互斥。");
        }

        // 间隔 0 = 不产出；时刻记当前步，住户不进那份食物的账（Produce 按 Kind 跳过）。
        return Remember(new Binding(cardId, ontoId, BindingKind.Resident, 0, nowStep, 0.0));
    }

    /// <summary>
    /// 把一条挂接登记进四本索引 —— <b>两种挂接共用这一发</b>。
    ///
    /// <para>承载者 → 占用者是多值：一张牌下面能叠多张，按压上去的次序往后加。
    /// 那一段对产线与住户是同一套规则，所以不各写一次。</para>
    /// </summary>
    private Binding Remember(Binding binding)
    {
        _order.Add(binding);
        _byCard[binding.CardId] = binding;
        _ontoOf[binding.CardId] = binding.OntoId;

        if (!_cardOnto.TryGetValue(binding.OntoId, out var occupants))
        {
            occupants = [];
            _cardOnto[binding.OntoId] = occupants;
        }

        occupants.Add(binding.CardId);
        return binding;
    }

    /// <summary>
    /// 解绑一张牌 —— <b>不损失任何材料</b>（玩家故事 6：改产线而不损失材料）。
    /// 累计产出那一格留在牌上，不清零：那是「这条产线干过多少活」的账。
    /// </summary>
    /// <param name="cardId">要解绑的那张（被绑的那一张）。</param>
    /// <exception cref="InvalidOperationException">它本来没绑过。</exception>
    public Binding Unbind(string cardId)
    {
        if (!_byCard.TryGetValue(cardId, out var binding))
        {
            // 「没绑过」与「不在册」是两句不同的话：前者是操作错，后者是牌已经不在桌上。
            // 不在册那一格由 RequireCard 判，这里不重复报。
            throw new InvalidOperationException(
                $"{cardId} 没有压在任何牌身上：解绑只对被绑的那一张成立，"
                + "它现在是一条空产线。要绑回去就再 bind 一次。");
        }

        Forget(cardId);
        return binding;
    }

    /// <summary>
    /// 腾干净 —— 一张牌要离开世界时，把它身上的绑定全部清掉。
    ///
    /// <para><b>这是消耗型动作最容易漏的一处</b>（ticket 05）：合成消耗两张旧牌，
    /// 旧牌身上还挂着绑定时不清，册子里就会留下一条指向不存在牌的悬空绑定端，
    /// 而画面上只看得见「牌不见了」，读数上查不出来。</para>
    ///
    /// <para><b>清多少条由那张牌自己身上挂了几条决定</b>：它自己压着一条（最多一条，
    /// 一对一那一侧），压在它身上的可以有若干条。第一版写死「最多两条」，
    /// 删掉「一牌只接一牌」之后那个上界不成立了。</para>
    /// </summary>
    /// <param name="cardId">要走的那张。</param>
    /// <returns>腾掉了几条绑定（0 是没人碰它，n 是自己压着一条加上 n-1 条压在它身上的）。</returns>
    public int Detach(string cardId)
    {
        var n = 0;
        if (_byCard.ContainsKey(cardId))
        {
            Forget(cardId);
            n++;
        }

        // 先把压在它身上的那几张抄出来再摘：Forget 会就地改那张表，边走边摘会漏掉后头几条。
        if (_cardOnto.TryGetValue(cardId, out var occupants))
        {
            var covering = occupants.ToArray();
            _cardOnto.Remove(cardId);
            foreach (var id in covering)
            {
                Forget(id);
                n++;
            }
        }

        return n;
    }

    /// <summary>
    /// 把这一步到点的那些各记一次产出，并把下一次产出时刻往前推一个间隔。
    /// 返回这一步一共产出了几份食物 —— <b>按步连续，不攒到结算点</b>（spec）。
    ///
    /// <para>每一步推进之后调一次。少推一步就少到一条，多推一步就多到一条，
    /// 边界由 <paramref name="nowStep"/> 那一格说了算。</para>
    /// </summary>
    public double Produce(long nowStep, CardBook cards)
    {
        ArgumentNullException.ThrowIfNull(cards);

        var food = 0.0;
        for (var i = 0; i < _order.Count; i++)
        {
            var b = _order[i];

            // 住户不产食物（spec「压上去之后产出什么由底下那张牌决定」）：
            // 间隔是 0，跳过它不是兜底 —— 不跳的话下一次产出时刻往前推的那个循环步长为 0。
            if (b.Kind != BindingKind.Production) continue;
            if (b.NextAtStep > nowStep) continue;

            var spec = CardCatalog.Of(cards.Require(b.CardId).Kind);
            food += spec.FoodPerCycle;
            cards.CreditProduction(b.CardId, spec.FoodPerCycle);

            var next = b.NextAtStep;
            while (next <= nowStep) next += b.IntervalSteps;
            _order[i] = b with { NextAtStep = next, Produced = b.Produced + spec.FoodPerCycle };
            _byCard[b.CardId] = _order[i];
        }

        return food;
    }

    /// <summary>绑定关系进快照的那一段 —— 段首 <c>bind</c>，摘要一行 + 每条一行。</summary>
    /// <remarks>
    /// 每条都印出 <c>kind=</c>：两种挂接记在同一份表里，
    /// 那一格是唯一能说得出「这一条是产线还是住户」的地方。
    /// </remarks>
    public string DebugSnapshotSegment(long nowStep)
    {
        var sb = new StringBuilder();
        sb.Append("bind count=").Append(Num(_order.Count)).Append('\n');
        foreach (var b in _order)
        {
            sb.Append("B ").Append(b.CardId)
                .Append(" onto=").Append(b.OntoId)
                .Append(" kind=").Append(b.Kind)
                .Append(" every=").Append(Num(b.IntervalSteps))
                .Append(" in=").Append(Num(b.RemainingSteps(nowStep)))
                .Append(" out=").Append(Num(b.Produced))
                .Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>
    /// 从四本索引里抹掉一条绑定（次序表也摘掉）。
    ///
    /// <para><b>只摘这一条占用者</b>：承载者那张表是多值的，整条 <c>Remove</c> 会把
    /// 压在它上面的那几条一起抹掉 —— 多张压同一张时摘掉其中一张，另外几张就凭空消失了，
    /// 而画面上只看得见「那张牌不动了」，读数上查不出来。</para>
    /// </summary>
    private void Forget(string cardId)
    {
        if (!_byCard.Remove(cardId, out var binding)) return;
        _order.Remove(binding);
        _ontoOf.Remove(cardId);

        if (!_cardOnto.TryGetValue(binding.OntoId, out var occupants)) return;
        occupants.Remove(cardId);
        if (occupants.Count == 0) _cardOnto.Remove(binding.OntoId);
    }

    /// <summary>两端在册的守卫 —— 「不在册」的错误消息里列得出桌上有哪些牌。</summary>
    private static CardState RequireCard(CardBook cards, string id, string role)
        => cards.Find(id) ?? throw new InvalidOperationException(
            $"{role} {id} 不在桌上：在册的牌是 [{string.Join(" / ", cards.Ids)}]。"
            + "牌要先 place 上桌才能派工。");

    /// <summary>数字的唯一格式化处（「double 与不变文化」）。</summary>
    private static string Num(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>数字的唯一格式化处（「double 与不变文化」）。</summary>
    private static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
