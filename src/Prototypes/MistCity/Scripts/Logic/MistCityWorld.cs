#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DemoFactory.GugaFramework.Base;

namespace DemoFactory.Prototypes.MistCity.Logic;

/// <summary>
/// 落点是哪一种 —— <b>一张牌 / 空白处 / 弃牌堆</b>。
///
/// <para>三种值共用同一个落点判定方法（<see cref="MistCityWorld.Drop(string, DropTarget)"/>）。
/// 分成三个判定方法的话，改一次规则要改三处，漏掉的那一处症状是
/// 「玩家叠牌没反应，但注入同样的牌有反应」。</para>
/// </summary>
public enum DropTargetKind
{
    /// <summary>底下有一张牌（合成 / 派工 / 住进去）。</summary>
    Card,

    /// <summary>底下是空白处（拿下来）。</summary>
    Blank,

    /// <summary>落在弃牌堆上（丢弃）。</summary>
    Discard,
}

/// <summary>
/// 落点 —— <b>一张牌 id / 空白处 / 弃牌堆，三种值里的一种</b>。
///
/// <para>构造只有三个入口（<see cref="Onto"/> / <see cref="Blank"/> / <see cref="Discard"/>），
/// 所以「底下那张是牌」与「那个 id 是空」不会拼在一起。</para>
/// </summary>
public sealed record DropTarget
{
    private DropTarget(DropTargetKind kind, string? cardId)
    {
        Kind = kind;
        CardId = cardId;
    }

    /// <summary>哪一种落点。</summary>
    public DropTargetKind Kind { get; }

    /// <summary>底下那张的 id（只有 <see cref="DropTargetKind.Card"/> 那一档有值）。</summary>
    public string? CardId { get; }

    /// <summary>空白处（拿下来）—— 那个字面量。</summary>
    public const string BlankToken = "blank";

    /// <summary>弃牌堆（丢弃）—— 那个字面量。</summary>
    public const string DiscardToken = "discard";

    /// <summary>落点是空白处：拿下来（有没有承载者由判定分两种）。</summary>
    public static DropTarget Blank { get; } = new(DropTargetKind.Blank, null);

    /// <summary>落点是弃牌堆：丢弃，不在册了。</summary>
    public static DropTarget Discard { get; } = new(DropTargetKind.Discard, null);

    /// <summary>落点是底下那张：合成 / 派工。</summary>
    /// <param name="cardId">底下那张的 id。</param>
    public static DropTarget Onto(string cardId)
        => string.IsNullOrEmpty(cardId)
            ? throw new ArgumentException(
                "落点是牌 id 那一档时 id 不能空：空白处传 blank，丢弃传 discard。", nameof(cardId))
            : new DropTarget(DropTargetKind.Card, cardId);

    /// <summary>
    /// 解析注入动词 <c>onto=</c> 的那个参数 —— <b>两个短词之外的一切都当牌 id</b>。
    /// </summary>
    /// <param name="token">牌 id、<c>blank</c> 或 <c>discard</c>。</param>
    public static DropTarget Parse(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return token switch
        {
            BlankToken => Blank,
            DiscardToken => Discard,
            _ => Onto(token),
        };
    }

    /// <summary>读数里这个落点怎么写（与 <c>onto=</c> 的参数写法一致）。</summary>
    public override string ToString()
        => Kind == DropTargetKind.Card ? CardId ?? "?" : Kind switch
        {
            DropTargetKind.Blank => BlankToken,
            _ => DiscardToken,
        };
}

/// <summary>
/// 把一张牌放到一个落点上会发生什么 —— <b>落点是牌时三档，落点是空白处或弃牌堆时两档</b>。
///
/// <para>「什么都没发生」那一档有五种情形，原因短词各不相同（见
/// <see cref="MistCityWorld.Drop(string, DropTarget)"/>）。玩家路径静默返回，
/// 注入路径返回那句话。</para>
/// </summary>
public enum DropAction
{
    /// <summary>合成出来一张牌：消耗配方要的那几张，多出来的那几张留在桌面上。</summary>
    Combine,

    /// <summary>上面那张派到底下那张上，开始产食物。</summary>
    Attach,

    /// <summary>住进房屋：挂接建起来了，产出物是人不是份食物。</summary>
    MoveIn,

    /// <summary>拿下来：落在空白处。产线拆了，累计产出留在牌上。</summary>
    TakeDown,

    /// <summary>丢弃：落在弃牌堆上，那张牌不在册了。</summary>
    Discard,

    /// <summary>什么都不发生（原因在 <see cref="DropResult.Reason"/> 那一格）。</summary>
    NoOp,
}

/// <summary>
/// 叠放的判定结果 —— <b>发生了什么，或者为什么什么都没发生</b>。
///
/// <para><b>两种结果要分得开</b>：「合成出一张牌」与「拖过去什么也没发生」在画面上
/// 都只是那张牌动了，读数上必须一句话说出原因，否则「落空」这件事本身没法断言。</para>
/// </summary>
/// <param name="Action">发生了哪一档。</param>
/// <param name="Reason">没发生时的原因（发生过了是空串）—— 一个能进断言的短词。</param>
/// <param name="Text">人读的那一句：发生过了就是那次写路径的原句，没发生就是一句说得出原因的话。</param>
public sealed record DropResult(DropAction Action, string Reason, string Text)
{
    /// <summary>真的发生了（<see cref="DropAction.NoOp"/> 之外都是）。</summary>
    public bool Happened => Action != DropAction.NoOp;

    /// <summary>合成出来的那张牌（新牌 id）—— 只有 <see cref="DropAction.Combine"/> 那一档有值。</summary>
    public string? ResultCard { get; init; }

    /// <summary>落点是空白处那一档：那张牌本来就没有承载者，是单纯移位（挂接表与产出读数都不动）。</summary>
    public bool MovedOnly { get; init; }
}

/// <summary>
/// 迷雾之都的逻辑世界 —— <b>一个接缝：命令进，快照出</b>（spec「一个 seam：命令进，快照出」）。
///
/// <para><b>形状照 BeastArenaWorld 那一族，删掉的是三层</b>：迷雾之都一处都不碰
/// 建造层、寻路层与技能层（spec「Out of Scope」），所以本类不组合持有那三件，
/// 也不走 <c>WorldAssembly</c> 的五步装配 —— 那一侧的推进守卫要求三件齐全，
/// 少一件当场抛，而这里本来就没有那三件。</para>
///
/// <para><b>所以 <see cref="Advance"/> 覆写成直接推时钟</b>：基类那份走装配，
/// 覆盖掉它就是「本原型不接三层」这一事实的唯一出处，不在调用点各判一次。
/// 暂停与冻结仍在本类判（基类那份判在装配里，而本类不走装配）。</para>
///
/// <para><b>每步的次序</b>（订阅次序就是执行次序）：
/// ① <see cref="OnPulse"/> 先跑：绑定到点的产出加到进食账本上，扣这一步的饭；
/// ② 到了结算步（<c>Tick % 60 == 0</c>）判进食、发死亡、住满的房屋生人。
/// 产出在每一步都在跑，<b>不攒到结算点</b> —— 数字一直在爬，玩家看得见。</para>
///
/// <para><b>纯 C#、零引擎依赖</b>（「逻辑层纯 C#」）：不起 Godot 就能跑完整局。</para>
/// </summary>
public sealed class MistCityWorld : GameLogicBase
{
    /// <summary>
    /// 造一局 —— <b>开局牌序固定，同一颗种子重跑逐字一致</b>（用户故事 22）。
    /// 三个人、三口饭、一小批够用但不够富余的牌（玩家故事 10）。
    /// </summary>
    /// <param name="config">这一局的配置。</param>
    public MistCityWorld(MistCityConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;

        Cards = new CardBook(() => Tick);
        Bindings = new Bindings();
        Food = new Rations(() => Tick);

        DealOpeningHand(config);
        foreach (var id in _residentIds) Food.AddResident(id);
        Food.SeedFood(config.OpeningFood);

        SubscribePulse(OnPulse);
    }

    private readonly MistCityConfig _config;
    private string[] _residentIds = [];

    /// <summary>逻辑种子（开局牌序由它派生；灰盒阶段没有随机玩法项）。</summary>
    public int Seed => _config.Seed;

    /// <summary>这一局的配置（只读那一半）。</summary>
    public MistCityConfig Config => _config;

    /// <summary>牌册（牌的册子：id、序号、增删次序走实体层，自有字段本原型自己印）。</summary>
    public CardBook Cards { get; }

    /// <summary>绑定关系模块（持有「一张牌压在另一张上」这份关系，自建不进建造层）。</summary>
    public Bindings Bindings { get; }

    /// <summary>进食账本（每 60 步判一次进食，供不上的人死）。</summary>
    public Rations Food { get; }

    /// <summary>
    /// 发开局那一手牌 —— <b>由种子派生，所以三个人拿到同一份</b>（验证者故事 16：
    /// 差异来自人不来自内容）。
    ///
    /// <para>发牌用固定次序轮转而不是随机洗：灰盒阶段要的是「同一颗种子两次跑逐字一样」，
    /// 而洗牌那种随机源一旦引入，「同种子是否同牌序」就成了每次都要重验的命题。</para>
    /// </summary>
    private void DealOpeningHand(MistCityConfig config)
    {
        foreach (var token in config.OpeningHand)
        {
            if (!CardCatalog.TryParse(token, out var kind))
            {
                throw new ArgumentException(
                    $"开局牌序里有一个解析不出的牌种「{token}」（词表：{CardCatalog.Names}）。"
                    + "开局牌序是配置，配置写错当场抛 —— 不挑一个能解析的凑合开局。");
            }

            Cards.Place(kind);
        }

        _residentIds = config.Residents.ToArray();
    }

    // === 推进：一步 = 0.1 秒（「时间基准」）===

    /// <summary>
    /// 推进若干步 —— <b>本原型只推时钟</b>（不接建造/寻路/技能三层，没有装配要过）。
    ///
    /// <para>暂停与冻结都判在这里，与 <c>WorldAssembly.Advance</c> 同一口径：
    /// 两者对推进的效果一样（一步都不走），但真值分放 —— 暂停可解冻，冻结是这一局的结局。</para>
    /// </summary>
    /// <param name="steps">推进几步。小于 1 什么都不做。</param>
    /// <returns>时间真的前进了返回 true；步数不足、暂停中或已冻结返回 false。</returns>
    public override bool Advance(int steps = 1)
    {
        if (steps < 1 || IsPaused || TimeFrozen) return false;
        return Clock.Advance(steps);
    }

    /// <summary>
    /// 每一步的三件事 —— <b>按步产出 + 按步扣饭</b>，结算步上多判一次进食、发死亡、生人。
    ///
    /// <para>次序：先加产出，再扣这一步的饭。所以同一秒里产出先于消耗 ——
    /// 「这一秒数字先涨后掉」就是这一段的形状。</para>
    ///
    /// <para><b>生人排在进食结算之后</b>：新生的人这一轮一份都没吃，
    /// 结算的缺口数与死者名单不该算他那 60 份。而顶栏「在世」与结算读数
    /// 在同一个读数里涨 —— 玩家不必跨两个时刻对表。</para>
    /// </summary>
    private void OnPulse(long step)
    {
        Food.CreditProduction(Bindings.Produce(step, Cards));
        Food.TickIntake();

        if (step % Rations.CycleSteps != 0) return;

        Food.Settle(step);
        BirthFromFullHouses(step);

        // 结算那一刻处置超出的那几张（次序：牌册登记次序，从最老的一张开始）。
        TrimToKeepLimit();
    }

    // === 每轮可保留的牌数上限 ===

    /// <summary>
    /// 基础上限 —— <b>一轮进食结算后可保留几张牌</b>，不是桌面的绝对上限。
    ///
    /// <para><b>这一条最容易写错的地方</b>：它压的是玩家的选择，不是玩家的手。
    /// 轮末之前桌上张数<b>可以</b>超过它，不被当场拦住；
    /// 到了结算那一刻超出的部分才被处置掉。所以牌位满了不拦，
    /// 拦的时机在 <see cref="OnPulse"/> 里结算之后那一格。</para>
    /// </summary>
    public const int BaseKeepLimit = 6;

    /// <summary>
    /// 这一轮实际能保留几张 —— <b>基础上限加上场上每张房屋牌的那一份（活数）</b>。
    /// 场上多一张棚就多 2 张，屋 5 张，仓 10 张；拆掉一张立刻掉回去。
    /// </summary>
    public int KeepLimit => BaseKeepLimit
        + Cards.Ids.Sum(id => CardCatalog.Of(Cards.Require(id).Kind).KeepBonus);

    /// <summary>超出了几张 —— 下一轮结算那一刻会被处置掉的那几张。</summary>
    public int CardsOverKeepLimit => Math.Max(0, Cards.Count - KeepLimit);

    /// <summary>
    /// 轮末处置 —— <b>结算那一刻把超出的那几张丢掉</b>，走
    /// <see cref="Discard"/> 那条写路径（玩家拖到弃牌堆上走的是同一条）。
    ///
    /// <para><b>次序判据：牌册登记次序，从最老的一张开始丢</b>。
    /// 与实体层那条「稳定遍历次序 = 登记次序」同一口径，
    /// 所以同一颗种子重跑时丢掉的永远是同样那几张，快照逐字一致 ——
    /// 处置次序没有出处的话，重放端会与录制端丢掉不同的牌。</para>
    ///
    /// <para><b>轮末之前这一发不调</b>：超过上限的那些牌在那一格之前一直好端端地在桌上。</para>
    /// </summary>
    /// <returns>丢掉了几张。</returns>
    private int TrimToKeepLimit()
    {
        var over = CardsOverKeepLimit;
        if (over <= 0) return 0;

        // 先把要丢的那几张抄出来：Discard 会改牌册，边走边摘会漏掉后头几张。
        foreach (var id in Cards.Ids.Take(over).ToArray()) Discard(id);
        return over;
    }

    // === 生人（住满之后） ===

    /// <summary>
    /// 下一次生人落在第几步 —— <b>房屋牌 id → 步号</b>。没住满的不在这张表里。
    ///
    /// <para><b>计时挂在住客数上，不挂在「上一次生人」上</b>。挂在后者的话，
    /// 第二次住满会接着上次没走完的步数继续走，而那条已被否掉（重新等一个完整周期）。</para>
    /// </summary>
    private readonly Dictionary<string, long> _birthAt = new(StringComparer.Ordinal);

    /// <summary>每张房屋牌已经生出几个人 —— 快照里房屋那一行的 <c>born</c> 读它。</summary>
    private readonly Dictionary<string, long> _born = new(StringComparer.Ordinal);

    /// <summary>
    /// 生人与清计时 —— <b>在结算步与每一次挂接变化之后跑一次</b>，只有三件事：
    ///
    /// <para><b>住满那一刻起等一个完整周期。</b>住满的判据是这间房身上的住户挂接数
    /// 达到牌种表里那一份住客上限（三种房屋都是 2 人）。
    /// 少了一个人就清计时，重新住满之后重新等一个完整周期。</para>
    ///
    /// <para><b>落点是结算步而不是「住满 + 60 步」。</b>住满那一刻可能是第 37 步，
    /// 从那儿数满 60 步落在第 97 步，而第 97 步没有结算那一格。
    /// 所以判据是「到点了」，落点是「下一个结算步」：第 60/120/180… 步。
    /// 与进食结算同刻这一条的好处是顶栏「在世」与结算读数在同一个读数里涨。</para>
    ///
    /// <para><b>一个人挂登记次序的队尾。</b>食物不够时按次序先到先得，
    /// 所以新人下一次饿死的先是他 —— 那与「缺口全压在最后一个人头上」是同一条规则，
    /// 这里不另写一遍。</para>
    /// </summary>
    /// <param name="nowStep">当前步号（结算步上就是这一次结算的那一步）。</param>
    private void BirthFromFullHouses(long nowStep)
    {
        foreach (var id in Cards.Ids)
        {
            if (!CardCatalog.IsHouse(Cards.Require(id).Kind)) continue;

            if (!IsFullHouse(id))
            {
                // 清计时：住处被撤走或那间房被合成吃掉之后，重新住满要重新等一个周期。
                _birthAt.Remove(id);
                continue;
            }

            if (!_birthAt.TryGetValue(id, out var at))
            {
                _birthAt[id] = nowStep + Rations.CycleSteps;
                continue;
            }

            if (nowStep < at) continue;

            // 生人上户口：走 Rations 那条现成的口，排在登记次序的队尾，id 永不复发。
            Food.AddResident(NextResidentId());
            _born[id] = _born.GetValueOrDefault(id) + 1;
            _birthAt[id] = nowStep + Rations.CycleSteps;
        }

        ForgetVanishedHouses();
    }

    /// <summary>
    /// 房屋牌不在桌上了，它那两格一并去掉 —— <b>不留指向已消耗承载者的悬挂计时</b>
    /// （与挂接那一套清理同一口径）。
    ///
    /// <para><b>已生出的个数跟着那张牌一起没了</b>：牌被合成掉之后新牌是另一间房，
    /// 出人数从零算。</para>
    /// </summary>
    private void ForgetVanishedHouses()
    {
        foreach (var id in _birthAt.Keys.Where(id => !Cards.Contains(id)).ToArray())
        {
            _birthAt.Remove(id);
            _born.Remove(id);
        }
    }

    /// <summary>
    /// 下一个人的 id —— <b>拿实体册下一格序号</b>，所以死掉的 h1 不会回来，
    /// 回来的是新编号的那个人（实体层那条「id 永不复发」）。
    /// </summary>
    private string NextResidentId()
        => "h" + Food.Entities.NextSeq.ToString(CultureInfo.InvariantCulture);

    // === 写路径（玩家点选与调试注入调的就是这一批方法本身） ===

    /// <summary>
    /// 放一张牌上桌 —— <b>第一条写路径</b>（用户故事 18）。
    /// 牌种不在牌种表里当场抛，不兜一个默认牌种。
    /// </summary>
    /// <param name="kindToken">牌种 token（枚举名或中文显示名）。</param>
    /// <returns>新牌的 id。</returns>
    public string Place(string kindToken)
    {
        if (!CardCatalog.TryParse(kindToken, out var kind))
        {
            throw new ArgumentException(
                $"牌种解析不出「{kindToken}」（词表：{CardCatalog.Names}）。"
                + "灰盒阶段没有需要宽容的输入来源，牌种给错当场抛。");
        }

        return Cards.Place(kind);
    }

    /// <summary>
    /// 派工：把一张牌绑到另一张上（用户故事 19）。
    /// 参数缺了当场抛：牌不在册、目标不在册、绑到自己、重复绑定、被绑的那张不产出
    /// —— 五种各自的消息里说得出是哪一端出的问题。
    /// 被压着的那张已经有人压不再抛：一张牌下面能叠多张。
    /// </summary>
    /// <param name="cardId">被绑的那张（产出的那一张）。</param>
    /// <param name="ontoId">被压着的那一张。</param>
    /// <returns>成立那条绑定的读数（两端、间隔、还差几步）。</returns>
    public string Bind(string cardId, string ontoId)
    {
        var b = Bindings.Bind(Cards, cardId, ontoId, Tick);
        return $"bind ok {b.CardId} onto={b.OntoId} every={Num(b.IntervalSteps)} in={Num(b.RemainingSteps(Tick))}";
    }

    /// <summary>
    /// 解绑一张牌 —— <b>不损失材料</b>（用户故事 6）。
    /// 解一张没绑过的当场抛（不静默当无事发生）。
    ///
    /// <para><b>挂接变了就要重算生人计时</b>：解掉的是住户牌时那间房可能掉到上限以下，
    /// 那一格计时当场清掉。</para>
    /// </summary>
    public string Unbind(string cardId)
    {
        var b = Bindings.Unbind(cardId);
        BirthFromFullHouses(Tick);
        return $"unbind ok {b.CardId} was-onto={b.OntoId} produced={Num(b.Produced)}";
    }

    /// <summary>
    /// 合成：两张牌换一张新牌（用户故事 8 / spec「合成与派工都是第一版就要能分辨的」）。
    ///
    /// <para><b>这一发是两个输入的那一个壳</b>。三条房屋配方全是三份输入，
    /// 所以真正的合成入口收任意份输入（下面那个重载），
    /// 两份那一档折过来，两条路共用同一段消耗次序。</para>
    /// </summary>
    /// <param name="aId">第一张。</param>
    /// <param name="bId">第二张。</param>
    /// <returns>新牌的 id。</returns>
    public string Combine(string aId, string bId) => Combine([aId, bId]);

    /// <summary>
    /// 合成 —— <b>任意份输入换一张新牌</b>。
    ///
    /// <para><b>输入按无序多重集判</b>（<see cref="RecipeBook"/>）：<c>石 + 柴 + 柴</c> 与
    /// <c>柴 + 柴 + 石</c> 是同一条配方。输入份数不够当场抛，
    /// 不从桌上再拿一张补上 —— 补上去的那一张玩家没看见。</para>
    ///
    /// <para><b>消耗型动作的三条</b>：① 每一张旧牌走实体层的移除次序，不在册了；
    /// ② 新牌发新序号，不复用被消耗掉的那几张的序号；
    /// ③ 旧牌身上还挂着的挂接一并腾干净，不留悬空端 ——
    /// 挂在房屋牌上的住户也在这一格里腾（住户退回自由平面，不被吃掉）。</para>
    ///
    /// <para><b>配方不合当场抛</b>：合成错了损失材料是玩法反馈，但「这些牌根本不是一条配方」
    /// 是命令写错了，那要在注入那一刻就炸出来，不能混进游戏反馈里。</para>
    /// </summary>
    /// <param name="cardIds">投入的牌，按牌册登记次序（次序不影响结果）。</param>
    /// <returns>新牌的 id。</returns>
    public string Combine(IReadOnlyList<string> cardIds) => CombineInto(cardIds, out _);

    /// <summary>
    /// 合成那一格，带出合成出来的新牌 id —— <b>叠放走完之后表现层要把新牌挪到落点</b>，
    /// 那里要的是 id，而 id 只有这一处算得出来。
    /// </summary>
    /// <param name="cardIds">投入的牌，按牌册登记次序。</param>
    /// <param name="newId">合成出来的那一张。</param>
    private string CombineInto(IReadOnlyList<string> cardIds, out string newId)
    {
        ArgumentNullException.ThrowIfNull(cardIds);

        if (cardIds.Count < 2)
        {
            throw new InvalidOperationException(
                $"合成至少要两张牌，这一发只有 {cardIds.Count.ToString(CultureInfo.InvariantCulture)} 张："
                + "几张换一张才是合成这一件事。");
        }

        // 同一张牌只能算一份输入：重复放进去会少消耗一张却多出一张产出。
        foreach (var id in cardIds)
        {
            if (cardIds.Count(other => string.Equals(other, id, StringComparison.Ordinal)) < 2) continue;

            throw new InvalidOperationException(
                $"{id} 只能算一张牌：合成要几张不同的牌，"
                + $"这一次把 {id} 放了两次，那不是合成，是凭空多出一张牌。");
        }

        // 全都在册之后才问配方：牌不在册是命令写错，比配方不合更早炸出来。
        var output = RecipeBook.OutputOf(KindsOf(cardIds));

        // ① 腾引用：先清挂接再摘牌，顺序反了会在挂接表里留下指向不存在牌的悬空端。
        //    挂在它身上的住户牌留在桌面上 —— 先记下它们原来住在谁身上，牌面上那一格
        //    「曾住在」与快照那一格 livedin 都从这一份记账读。
        foreach (var id in cardIds)
        {
            NoteEvictedResidents(id);
            Bindings.Detach(id);
        }

        // ② 摘册（走实体层的移除次序）。
        foreach (var id in cardIds) Cards.Remove(id);

        // ③ 发新牌 —— 新序号（实体层发的，序号永不复发）。
        newId = Cards.Place(output);
        BirthFromFullHouses(Tick);
        return $"combine ok {string.Join("+", cardIds)} => {newId} kind={output}";
    }

    /// <summary>
    /// 记下挂在它身上的住户牌原来住在它身上 —— <b>它离桌时走这一发</b>。
    ///
    /// <para><b>住户牌留在桌面上、退回自由平面、不再产出，停在房屋原来的位置</b>。
    /// 不记这一笔的话，画面上只剩一张普通的牌（「几步/次　已出0」），
    /// 玩家看不出这间房上原本住过人 —— 而那正是这一格要交代的事。</para>
    ///
    /// <para><b>住户自己也被这道配方消耗掉时无害</b>：那一格跟着那张牌一起没了。</para>
    /// </summary>
    /// <param name="houseId">要离桌的那一张（房屋牌）。</param>
    private void NoteEvictedResidents(string houseId)
    {
        foreach (var residentId in Bindings.ResidentsOf(houseId))
            Cards.MarkResidentOf(residentId, houseId);
    }

    /// <summary>
    /// 丢牌 —— <b>一条可复用的写路径</b>：拖到弃牌堆上走它，
    /// 轮末强制处置（超出保留上限的那几张被处置掉，ticket 05）也走它。
    ///
    /// <para><b>次序照 <see cref="CombineInto"/>：先腾引用、再摘册</b>。
    /// 挂在它身上的绑定先腾干净（<see cref="Bindings.Detach"/> 清它自己压着的那一条
    /// 加上压在它身上的若干条），再从牌册摘掉它。
    /// 顺序反了会在绑定表里留下指向不存在牌的悬空端 —— 那种故障在画面上
    /// 只看得见「牌不见了」，读数上查不出来。</para>
    ///
    /// <para><b>拿不回来</b>：这一发不登记任何回收路，所以拖走之后那张牌
    /// 在世界这一侧已经不存在了。牌不在册仍然当场抛（那是命令写错）。</para>
    /// </summary>
    /// <param name="cardId">丢掉的那张。</param>
    /// <returns>这一次丢弃的读数（腾掉了几条绑定，摘的是哪张）。</returns>
    public string Discard(string cardId)
    {
        RequireCard(cardId);

        // ① 腾引用：先清绑定再摘牌，顺序反了会在绑定表里留下指向不存在牌的悬空端。
        NoteEvictedResidents(cardId);
        var freed = Bindings.Detach(cardId);

        // ② 摘册（走实体层的移除次序）。
        Cards.Remove(cardId);
        BirthFromFullHouses(Tick);
        return $"discard ok {cardId} freed={Num(freed)}";
    }

    // === 落点判定（玩家拖放、点选第二步与 drag_to 注入调的都是这一个方法） ===

    /// <summary>
    /// 落点是底下那张牌 —— <b><see cref="Drop(string, DropTarget)"/> 是它的一个壳</b>。
    /// 合成之后表现层要把新牌挪到落点，合成之后的东西由那一个方法答。
    /// </summary>
    /// <param name="fromId">上面那张。</param>
    /// <param name="ontoId">底下那张。</param>
    /// <returns>判定结果。</returns>
    public DropResult Drop(string fromId, string ontoId) => Drop(fromId, DropTarget.Onto(ontoId));

    /// <summary>
    /// 把上面那张放到一个落点上 —— <b>三种落点共用这一个判定方法</b>。
    ///
    /// <para><b>落点是牌时：先问配方，再问能不能接上去，两条都不通就什么都不发生</b>。
    /// 配方那一格问的是<b>那一摞</b>：底下那张、压在它上面的每一张（住户也算进去），
    /// 再加上刚拖过来的那张。那一摞里能凑出一条配方就合成一张：
    /// 消耗的正好是配方要的那几张，多出来的那几张留在桌面上、停在原位。
    /// 兜底取消：第一版是「合不出配方就一定是派工」，那个写法的前提是
    /// 「两次点击一定是有意图的」。拖放没有这个前提 —— 玩家把牌拖过去可能只是随手试探，
    /// 每拖一次都有反应的话试探永远不会落空，而落空正是发现的来源。</para>
    ///
    /// <para><b>落点是空白处时是拿下来</b>：有承载者就拆那条产线（累计产出留在牌上），
    /// 没有承载者就是单纯移位 —— 位置变、挂接表不动、累计产出读数不动。
    /// 自由平面上落点本来就是空白是常事，所以这两种情形要分得开，
    /// 而 <see cref="Unbind"/> 对「解绑一件没绑过的牌」是当场抛的：
    /// 判定先答「有没有承载者」，有才走解绑那条路。</para>
    ///
    /// <para><b>落点是弃牌堆时是丢弃</b>：走 <see cref="Discard"/> 那条写路径，
    /// 牌不在册了，它身上的挂接一并腾干净。</para>
    ///
    /// <para><b>它答的是「牌种之间能不能发生什么」与「这张牌该去哪」</b>，那是玩法语义，
    /// 所以住在逻辑层。玩家拖放、点选与 <c>drag_to</c> 注入都调这一发，
    /// 表现层不另抄一份 —— 抄一份之后改一次规则要改两处，漏掉的那一处症状是
    /// 「玩家叠牌没反应，但注入同样的牌有反应」。</para>
    ///
    /// <para><b>落空时数据一个字都不动</b>，也不抛：落空不是命令写错，
    /// 那是玩家要试的那一下。牌不在册仍然当场抛（那是命令写错）。</para>
    ///
    /// <para><b>空白处是一个写明的值</b>，不是「那个 id 查不到」。
    /// 所以合成过程中的空白中间态（两张都已被消耗掉）不会被读成空白处：
    /// 它拿到的仍是一个已不在册的 id，那条路当场抛而不是静默当成拿下来。</para>
    /// </summary>
    /// <param name="fromId">上面那张（拿起来的那张）。</param>
    /// <param name="target">落点：一张牌 / 空白处 / 弃牌堆。</param>
    /// <returns>判定结果：发生了什么，或者为什么什么都没发生。</returns>
    public DropResult Drop(string fromId, DropTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var from = RequireCard(fromId);

        return target.Kind switch
        {
            DropTargetKind.Blank => TakeDown(fromId),
            DropTargetKind.Discard => DiscardFrom(fromId),
            _ => DropOnto(fromId, from, target.CardId!),
        };
    }

    /// <summary>
    /// 拿下来那一格 —— <b>先答有没有承载者，再决定走哪条路</b>。
    ///
    /// <para>有承载者：走 <see cref="Unbind"/> 那条已有的写路径，产线拆了、累计产出留在牌上。
    /// 没有承载者：单纯移位 —— 位置变、挂接表不动、累计产出读数不动，
    /// 逻辑层一个字都不改（位置是表现层数据）。</para>
    /// </summary>
    /// <param name="fromId">拿下来的那张。</param>
    private DropResult TakeDown(string fromId)
    {
        // 解绑一件没绑过的牌当场抛，而单纯移位是正常情形 ——
        // 所以先问有没有承载者，不拿那一发去接「没有承载者」。
        if (Bindings.OntoOf(fromId) is null)
        {
            return new DropResult(DropAction.TakeDown, "", $"move ok {fromId} not-bound")
            {
                MovedOnly = true,
            };
        }

        return new DropResult(DropAction.TakeDown, "", Unbind(fromId));
    }

    /// <summary>丢弃那一格 —— 折到 <see cref="Discard"/> 那条写路径上，读数是它那一句。</summary>
    /// <param name="fromId">丢掉的那张。</param>
    private DropResult DiscardFrom(string fromId) => new(DropAction.Discard, "", Discard(fromId));

    /// <summary>
    /// 落点是底下那张牌时的判定 —— <b>五种落空各有各的原因短词</b>：
    /// <c>same-card</c> / <c>already-attached</c> / <c>top-not-producer</c> /
    /// <c>bottom-full</c> / <c>bottom-not-producer</c>。
    ///
    /// <para><b>配方那一格数的是那一摞</b>：底下那张、压在它上面的每一张（住户也算进去），
    /// 加上刚拖过来的那张。那一摞里能凑出一条配方就合成一张 ——
    /// 消耗的正好是配方要的那几张，多出来的那几张留在桌面上、停在原位。
    /// 住户与其它牌一样参与配方，所以住户被算进去的那一次会被吃掉（用户裁决）；
    /// 而这一摞凑不出配方时住户仍然活着，承载者被合成之后它退回自由平面、停在原位。</para>
    /// </summary>
    /// <param name="fromId">上面那张。</param>
    /// <param name="from">上面那一张的自有字段（已查过在册）。</param>
    /// <param name="ontoId">底下那张。</param>
    private DropResult DropOnto(string fromId, CardState from, string ontoId)
    {
        var onto = RequireCard(ontoId);

        // ① 两张是同一张牌：拾取排除了正在被拖的那张，正常拖不到自己身上，
        //    但注入那两端可以是同一个 id，所以这一格按落空处理而不是抛。
        if (fromId == ontoId)
        {
            return NoOp("same-card",
                $"{fromId} 是同一张牌：叠放要两张不同的牌，自己压自己不成叠。");
        }

        // ② 先问配方。合成消耗材料、派工不消耗，所以拿不准的时候合成是重的那一边。
        //    配方按「那一摞里能凑出」判，不是整摞恰好等于：消耗的正好是配方要的那几张，
        //    摞上多出来的那几张留在桌面上、停在原位。
        //    多条都凑得出时取份数最多的那一条，份数相同取配方表里在前的那一条。
        var stack = StackInputs(ontoId, fromId);
        var recipe = RecipeBook.Match(KindsOf(stack));
        if (recipe is not null)
        {
            var used = CardsFor(stack, recipe);
            var left = stack.Count - used.Count;
            var text = CombineInto(used, out var made);
            return new DropResult(DropAction.Combine, "", Text(text, left)) { ResultCard = made };
        }

        // ③ 再问能不能接上去。一张牌只有一份力气，压在别处就不动 —— 一对一那一侧有玩法意义。
        if (Bindings.OntoOf(fromId) is not null)
        {
            return NoOp("already-attached",
                $"{fromId} 已经压在 {Bindings.OntoOf(fromId)} 身上：一张牌只压一处。");
        }

        // ④ 上面那张要自己会产出。间隔为 0 的牌压上去没有产出的语义，那一格不存在。
        //    房屋牌就落在这里：房屋只当基地不当工人，它压在产粮牌上没有产出的语义。
        //    这一格在 Bind 那条守卫之前接住，所以不抛 —— 房屋牌进来之后它开始命中。
        if (from.IntervalSteps < 1)
        {
            return NoOp("top-not-producer",
                $"{fromId}（{CardCatalog.Of(from.Kind).Label}）不产出："
                + "它只能当材料或当被叠的那张；房屋只当基地，不当工人。");
        }

        // ⑤ 底下是房屋牌但已经住满 —— 住客上限读牌种表那一份数据（两种房屋都是 2 人）。
        //    判据只有 IsFullHouse 那一发，落点判定这一格不重写上限。
        if (IsFullHouse(ontoId))
        {
            return NoOp("bottom-full",
                $"{ontoId}（{CardCatalog.Of(onto.Kind).Label}）住满了：得先空出一个位置才住得进去。");
        }

        // ⑥ 底下是房屋牌 → 住进去。产出物是人不是份食物，那是底下那张牌定的，
        //    不是这一格另立的一条规则。住满之后生人的计时挂在住客数上（BirthFromFullHouses）。
        if (CardCatalog.IsHouse(onto.Kind))
        {
            // 手势只有两个端：拖一张、压一张。手里那张身上压着的牌先腾干净再住进去。
            FreeRiders(fromId);
            return new DropResult(DropAction.MoveIn, "", MoveIn(fromId, ontoId));
        }

        // ⑦ 底下那张既不是房屋牌也不产粮。牌种表里现在只有「产粮的」与「房屋」两类，
        //    所以这一格暂时不命中 —— 牌种表加上第三类牌时它才命中，形状先立住。
        if (onto.IntervalSteps < 1)
        {
            return NoOp("bottom-not-producer",
                $"{ontoId}（{CardCatalog.Of(onto.Kind).Label}）不产粮、也不是房屋牌：叠上去没有产线可言。");
        }

        // ⑧ 两条都通 —— 上面那张派到底下那张上。手势只拖一张，
        //    身上压着的那几张先腾干净，它们退回自由平面、产线归零。
        FreeRiders(fromId);
        return new DropResult(DropAction.Attach, "", Bind(fromId, ontoId));
    }

    // === 那一摞（落点判定要数的那一摞） ===

    /// <summary>
    /// 那一摞的输入 —— <b>底下那张 + 压在它上面的每一张（含住户）+ 刚拖过来的那张</b>。
    ///
    /// <para><b>那一摞从摞底往两头收</b>：落在摞顶那张与落在摞底那张问的是同一摞牌，
    /// 两种落点数出来的输入必须一样。</para>
    ///
    /// <para><b>次序判据：牌册登记次序</b>。与实体层那条「稳定遍历次序 = 登记次序」
    /// 以及轮末处置那一处同一口径。按挂接成立的次序或按玩家点的次序排都不行 ——
    /// 那两种次序在同种子重跑时未必相同，合成读数与快照会跟着漂。</para>
    ///
    /// <para><b>住户数进输入</b>（用户裁决）：住户是摞上的一张牌，与其他牌一样参与配方。
    /// 不这样的话「屋 = 棚 + 2 石」永远凑不齐 ——
    /// 压到房屋上的石已经先成了住户。住户被算进配方并因此被消耗时，那张牌就合成掉了。</para>
    ///
    /// <para><b>一张牌只算一张</b>：手里那张本来就压在这一摞的某一张身上时，
    /// 原地放下不能把它算成两份 —— 算两份就能凭空凑出一条配方。</para>
    /// </summary>
    /// <param name="ontoId">落点那张。</param>
    /// <param name="fromId">刚拖过来的那张。</param>
    private List<string> StackInputs(string ontoId, string fromId)
    {
        var picked = new HashSet<string>(StringComparer.Ordinal) { fromId };
        foreach (var id in PileUnder(ChainUp(ontoId)[^1])) picked.Add(id);

        // 按牌册登记次序过滤（不是按 set 的枚举次序：那不是稳定次序）。
        return Cards.Ids.Where(picked.Contains).ToList();
    }

    /// <summary>
    /// 从那一摞里挑出这条配方要消耗的那几张 —— <b>同一种牌按牌册登记次序，最老的先消耗</b>。
    ///
    /// <para><b>为什么定这个次序</b>：摞上同一种牌可能有几张，消耗哪几张是一次选择。
    /// 不定出处的话，同种子重跑会消耗掉不同的那几张，读数与快照跟着漂，
    /// 而那种漂在画面上只看得出「少了一张牌」。按登记次序判，
    /// 重跑丢的永远是同样那几张，与实体层那条「稳定遍历次序 = 登记次序」同一口径。</para>
    ///
    /// <para><b>挑出来的那一串也是登记次序</b>，所以合成读数里输入怎么排列与改之前一致 ——
    /// 「摞里只有底下那张」那一格因此逐字不变。</para>
    ///
    /// <para><b>凑得出才挑得出来</b>：<see cref="RecipeBook.Match"/> 判过够份之后，
    /// 每一种输入都至少有一张可挑，所以这里挑不满就说明表与那一摞对不上 ——
    /// 那种错当场抛，不兜一张牌凑合。</para>
    /// </summary>
    /// <param name="stack">那一摞，按牌册登记次序。</param>
    /// <param name="recipe">要凑的那一条配方。</param>
    private List<string> CardsFor(IReadOnlyList<string> stack, Recipe recipe)
    {
        // 每一种输入还差几份。
        var need = new Dictionary<CardKind, int>();
        foreach (var input in recipe.Inputs) need[input] = need.GetValueOrDefault(input) + 1;

        // 按牌册登记次序走那一摞：同一个牌种先撞见的那张就是最老的那张，先消耗它。
        var used = new List<string>(recipe.Portions);
        foreach (var id in stack)
        {
            var kind = RequireCard(id).Kind;
            if (need.GetValueOrDefault(kind) < 1) continue;

            need[kind] -= 1;
            used.Add(id);
        }

        if (used.Count != recipe.Portions)
        {
            throw new InvalidOperationException(
                $"这一摞凑得出「{Describe(recipe)}」却挑不满 {recipe.Portions.ToString(CultureInfo.InvariantCulture)} 张："
                + "配方表与那一摞的牌种对不上。凑得出的判据与挑牌的那一格必须同一份口径。");
        }

        return used;
    }

    /// <summary>
    /// 叠放那一发的读数 —— <b>一摞没有被整摞吃掉时补一格「桌上还剩几张」</b>。
    ///
    /// <para>一摞全被吃掉时不补那一格：那种情形与「摞里只有底下那张」一模一样，
    /// 读数要逐字不变，不补才能验「没退化」那一条。</para>
    /// </summary>
    private static string Text(string combineText, int leftOnTable)
        => leftOnTable > 0 ? $"{combineText} left={Num(leftOnTable)}" : combineText;

    /// <summary>报一句里念得出这一条配方要什么（中文显示名）。</summary>
    private static string Describe(Recipe recipe)
        => string.Join(" + ", recipe.Inputs.Select(k => CardCatalog.Of(k).Label));

    /// <summary>
    /// 那一摞 —— <b>摞底那张与压在它上面的每一张</b>（住户也列在里面，它们与其他牌一样算一份）。
    ///
    /// <para><b>从摞底往两头收，不从落点往上走</b>：落在摞顶与落在摞底问的是同一摞牌，
    /// 两种落点答出来的输入必须一样。按落点往上走会漏掉摞底身上压着的其余几张 ——
    /// 那几张在画面上明明在那一摞里。</para>
    ///
    /// <para><b>张数上界只为坏数据不把进程挂住</b>：一张牌只有一份力气、只压一处，
    /// 而占用者要没有承载者才绑得上，所以这一摞走不到头就会断。
    /// 真走不到头说明挂接表里有环，那当场抛，不让它转圈。</para>
    /// </summary>
    private HashSet<string> PileUnder(string baseId)
    {
        var pile = new HashSet<string>(StringComparer.Ordinal);
        var frontier = new Queue<string>();
        frontier.Enqueue(baseId);

        while (frontier.Count > 0)
        {
            var id = frontier.Dequeue();
            if (!pile.Add(id)) continue;

            if (pile.Count > Cards.Count)
            {
                throw new InvalidOperationException(
                    $"{baseId} 这一摞数到 {pile.Count.ToString(CultureInfo.InvariantCulture)} 张还没数完："
                    + $"桌上只有 {Cards.Count.ToString(CultureInfo.InvariantCulture)} 张，挂接表里有环。"
                    + "环上有一张先离桌，环就断了。");
            }

            foreach (var covering in Bindings.CardOnto(id)) frontier.Enqueue(covering);
        }

        return pile;
    }

    /// <summary>
    /// 从一张牌沿「压在谁身上」一路走到摞底 —— <b>摞底是哪一张</b>。
    /// </summary>
    private List<string> ChainUp(string cardId)
    {
        var chain = new List<string>();
        for (var id = cardId; id is not null; id = Bindings.OntoOf(id))
        {
            if (chain.Count > Cards.Count)
            {
                throw new InvalidOperationException(
                    $"{cardId} 往上数了 {chain.Count.ToString(CultureInfo.InvariantCulture)} 张还没到摞底："
                    + "挂接表里有环，一摞牌走不出头。环上有一张先离桌，环就断了。");
            }

            chain.Add(id);
        }

        return chain;
    }

    /// <summary>
    /// 落点那一摞最底下那张 —— <b>整摞合成之后新牌落在它原来的位置</b>。
    ///
    /// <para><b>查询不抛</b>：牌不在册回 null（<c>CardStateOf</c> 同一口径）。
    /// 表现层在落手之前问这一发，因为合成之后摞底那张就不在画面上了。</para>
    /// </summary>
    /// <param name="cardId">落点那张。</param>
    public string? StackBaseOf(string cardId)
        => Cards.Contains(cardId) ? ChainUp(cardId)[^1] : null;

    /// <summary>
    /// 腾干净手里那张身上压着的牌 —— <b>手势只拖一张，不拖一摞</b>。
    ///
    /// <para>压在它身上的那些退回自由平面：产线归零、住户退房，它们停在原来的位置。
    /// 让它们留在原地而挂接表跟着改，画面上才与读数一致 ——
    /// 牌面那一格印着「压在谁身上」，压着却不叠在一起的那一种在读数上查得出来、
    /// 在画面上查不出来。</para>
    ///
    /// <para>手里那张自己那条挂接走的是 <c>Bindings.Detach</c> 里的一半：
    /// 它能落到落点上去，说明它没有承载者（<c>already-attached</c> 那一格先判过）。</para>
    ///
    /// <para><b>腾出来的住户牌腾掉的那间房可能因此掉到上限以下</b>，
    /// 所以腾完重算一次生人计时（清计时只有这一发，不留在调用点各判一次）。</para>
    /// </summary>
    private void FreeRiders(string cardId)
    {
        Bindings.Detach(cardId);
        BirthFromFullHouses(Tick);
    }

    /// <summary>一份输入的牌种，按给定的牌次序 —— 折键只有这一处，两条路共用。</summary>
    private CardKind[] KindsOf(IReadOnlyList<string> cardIds)
    {
        var kinds = new CardKind[cardIds.Count];
        for (var i = 0; i < cardIds.Count; i++) kinds[i] = RequireCard(cardIds[i]).Kind;
        return kinds;
    }

    /// <summary>
    /// 住进房屋 —— <b>叠到房屋牌上的那一格</b>，读数是挂接那一模块的那一句。
    ///
    /// <para><b>住满那一刻重算一次生人计时</b>：住客数推到上限的那一发，
    /// 计时从这一步起算一个完整周期（<b>不是从上一个结算步起算</b>）。
    /// 落点判定那一格先问过满没满，所以从这条路进来的一定是刚住满。</para>
    /// </summary>
    /// <param name="cardId">住进去的那一张。</param>
    /// <param name="ontoId">底下那张房屋牌。</param>
    public string MoveIn(string cardId, string ontoId)
    {
        var b = Bindings.MoveIn(Cards, cardId, ontoId, Tick);
        BirthFromFullHouses(Tick);
        return $"move-in ok {b.CardId} onto={b.OntoId} every={Num(b.IntervalSteps)}";
    }

    /// <summary>
    /// 底下那张是不是已经住满的房屋 —— <b>数住户挂接，不是数牌种</b>。
    ///
    /// <para>住户住在 <see cref="Bindings"/> 那一份表里（<see cref="BindingKind.Resident"/>
    /// 那一档），数 <see cref="Bindings.ResidentsOf"/> 里那几张即可，
    /// 本类不另记一份「每张房屋住了几个」。派工挂接不算住户 ——
    /// 玩家可以用 <c>bind</c> 造出「产粮牌压在房屋上」那一摞，它不占空位。</para>
    ///
    /// <para><b>上限读牌种表那一份数据</b>（<see cref="CardSpec.ResidentCap"/>），
    /// 三种房屋都是 2 人，生人数也一样。落点判定那一格不重写上限，
    /// 只问这一发。</para>
    ///
    /// <para>挂上去的住户牌可能被合成吃掉，所以同一摞也可能整摞被吃掉 ——
    /// 那时 <see cref="CombineInto"/> 的腾引用那一格已经把两类挂接一起腾干净了，
    /// 剩下的住户牌停在原处、不再产出。</para>
    /// </summary>
    /// <param name="houseId">底下那一张房屋牌。</param>
    private bool IsFullHouse(string houseId)
    {
        var state = Cards.Require(houseId);

        // 先问是不是房屋牌：产粮牌那一列住客上限是 0，而「0 >= 0」恒为真 ——
        // 不先判这一格的话，每一张产粮牌都会被答成住满。
        if (!CardCatalog.IsHouse(state.Kind)) return false;
        return Bindings.ResidentsOf(houseId).Count >= CardCatalog.Of(state.Kind).ResidentCap;
    }

    /// <summary>
    /// 这间房离下一次生人还差几步 —— <b>没住满回 null</b>（那时没有计时在跑）。
    ///
    /// <para><b>出处的唯一一处</b>：牌面上那条生人进度条与快照房屋那一行的
    /// <c>in</c> 读的是这一发。表现层不自己拿当前步号去减，
    /// 减出来的是另一个数（住满那一刻的起点不是这一格的起点）。</para>
    ///
    /// <para><b>量的不是「住满那刻起数的步数」</b>：生人落在进食结算步上，
    /// 所以这一格量的是离下一个结算步还差几步。</para>
    /// </summary>
    /// <param name="houseId">房屋牌 id。</param>
    /// <returns>还差几步。没住满、不是房屋牌或不在册都是 null。</returns>
    public long? BirthStepsLeftOf(string houseId)
        => _birthAt.TryGetValue(houseId, out var at) ? Math.Max(0L, at - Tick) : null;

    /// <summary>落空那一档的构造 —— 原因短词与那句人话一起给，不留半截。</summary>
    private static DropResult NoOp(string reason, string why)
        => new(DropAction.NoOp, reason, $"no-op {reason}：{why}");

    /// <summary>牌在不在册（查询不抛）。</summary>
    public bool HasCard(string id) => Cards.Contains(id);

    /// <summary>牌的自有字段（不在册回 null —— 查询的正常答案）。</summary>
    public CardState? CardStateOf(string id) => Cards.Find(id);

    /// <summary>牌的守卫：不在册当场抛，错误消息里列得出桌上有哪些牌。</summary>
    private CardState RequireCard(string id)
        => Cards.Find(id) ?? throw new InvalidOperationException(
            $"牌 {id} 不在桌上：在册的牌是 [{string.Join(" / ", Cards.Ids)}]。牌要先 place 上桌。");

    // === 读数 ===

    /// <summary>
    /// 全量状态读数 —— <b>显示即真相那一份</b>（用户故事 21）。
    ///
    /// <para><b>段首与分段口径照 BeastArenaWorld</b>：一行一段，段用 <c>|</c> 分。
    /// 本原型四段：牌册、绑定、进食、配方表。段首分别是
    /// <c>card</c> / <c>bind</c> / <c>ration</c> / <c>recipe</c> ——
    /// 断言三层里「段首身份漂移即红」，所以段首一个字都不许中途改。</para>
    /// </summary>
    public string DebugSnapshot()
        => $"tick={Num(Tick)} seed={Num(Seed)}"
           + $" | {CardSegment()}"
           + $" | {Fold(Bindings.DebugSnapshotSegment(Tick))}"
           + $" | {Fold(Food.DebugSnapshotSegment(Tick))}"
           + $" | {Fold(RecipeBook.DebugSnapshotSegment())}";

    /// <summary>
    /// 牌段 —— <b>牌册自己那一段 + 保留上限那两格 + 场上每一间房屋那一行</b>。
    ///
    /// <para><b>「一轮可保留几张」与「现在有几张」都进快照</b>，判据是
    /// 影响后续动作的数就得能被读到：轮末处置要依据它，
    /// 而依据不进快照就没法从录像倒推玩家为什么丢了牌。
    /// <c>over</c> 那一格是超出的张数 —— 下一轮结算那一刻会被处置掉的正是这些。</para>
    ///
    /// <para><b>房屋那一行印三格</b>：<c>live</c> 现在住了几个人、
    /// <c>born</c> 已经生了几个人、<c>in</c> 离下一次生人还差几步
    /// （没住满是 <c>-</c>，因为没有计时在跑 —— 那一格读 <see cref="BirthStepsLeftOf"/>，
    /// 与牌面上那条进度条同一份出处）。三格都是玩家下一步要算的数：
    /// 差几个才住满、几时多一个人、多一个人多要几份饭。
    /// 场上没有房屋牌时这一段一个字都不加 —— 不是印一行空行。</para>
    ///
    /// <para>牌册那一段的数一个都不在这里重算，这里只多接这几个数。</para>
    /// </summary>
    private string CardSegment()
    {
        var sb = new StringBuilder();
        sb.Append(Fold(Cards.DebugSnapshotSegment()))
            .Append(" keep=").Append(Num(KeepLimit))
            .Append(" over=").Append(Num(CardsOverKeepLimit));

        foreach (var id in Cards.Ids)
        {
            var state = Cards.Require(id);
            if (!CardCatalog.IsHouse(state.Kind)) continue;

            sb.Append(" home ").Append(id)
                .Append(" live=").Append(Num(Bindings.ResidentsOf(id).Count))
                .Append(" born=").Append(Num(_born.GetValueOrDefault(id)))
                .Append(" in=").Append(BirthStepsLeftOf(id) is { } stepsLeft ? Num(stepsLeft) : "-");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 推进到某一步再读快照 —— <b>只给断言用</b>，不在世界内部留第二份推进路径。
    /// </summary>
    /// <param name="step">推进到第几步。</param>
    public string DebugSnapshotAt(int step)
    {
        AdvanceTo(step);
        return DebugSnapshot();
    }

    /// <summary>
    /// 推进到某一步再读结算次数 —— 「第 59 步还没结算、第 60 步结算了」这类
    /// 边界断言写起来是一行，不必在测试里手算步号。
    /// </summary>
    /// <param name="step">推进到第几步。</param>
    public long FoodAt(int step)
    {
        AdvanceTo(step);
        return Food.SettlementIndex;
    }

    /// <summary>
    /// 把一段多行读数折成一行 —— 世界那份快照按「 | 」分段，模块给的是多行文本。
    /// 这里只搬 token，一个数都不重算（与 <c>LayerWiring.FoldSegment</c> 同一口径）。
    /// </summary>
    internal static string Fold(string segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        var sb = new StringBuilder();
        foreach (var line in segment.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(line.Trim());
        }

        return sb.ToString();
    }

    /// <summary>数字的唯一格式化处（「double 与不变文化」）。</summary>
    internal static string Num(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>数字的唯一格式化处（「double 与不变文化」）。</summary>
    internal static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
