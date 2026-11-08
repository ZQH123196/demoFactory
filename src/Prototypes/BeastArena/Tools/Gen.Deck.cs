#nullable enable
// 巨兽竞技场 造型源场景生成器 —— 底座与甲板域（磁悬浮底座 / 镇地面）。
//
// 本文件是 Gen 的部分实现（partial），按域拆分见 Gen.cs 文件头那一段。
// 这两件按成品米给尺寸，不走 Fits 的造型期缩放（原因见下面那一段注释）。

using DemoFactory.Tools;

namespace DemoFactory.Tools.BeastArena;

public static partial class Gen
{
    // ============================================================
    //  磁悬浮底座下半（LevitationBase）与镇地面（TownDeck）——**一件东西的两半**（2026-10-15）
    //
    //  为什么有这一层：原来小镇直接坐在 Beast.glb 自带的木质驮台上，而那个驮台是**跟着走路动作
    //  起伏**的 —— 小镇钉在固定高度，于是小镇与脚下的台面一张一合地对不上，巨兽一走就抖。
    //  这一层是小镇与巨兽之间唯一的中间件，它把那份起伏吃掉。
    //
    //  用户原话（2026-10-15，两句连着说的，合成一件事）：
    //  「磁悬浮底座 其实就是 驮台，这是一个玩意，准确的说是这个 驮台 具有带有磁悬浮功能的底座。
    //    所以，这个东西应该位于巨兽的背上，而不是与巨兽脱离。」
    //  「把这个底座改成一个圆形，而且要比小镇要小，大概是小镇的四分之一的大小吧，
    //    就像是真正的磁悬浮底座。」（＋「不需要能量塔，换成一个圆的发光能量圈就行了。发射头不需要。」）
    //
    //  第一句定了「在哪」：下半骑在兽背上，镇浮在它上方。
    //  第二句定了「什么形状、多大」：小镇地面 48 米见方，底座直径取 12 米（边长的四分之一）。
    //  早先那一件是 62 米方形鞍座 + 四角能量塔 + 四个发射头，比镇还宽 ——
    //  读出来是「一块大板压在另一块大板上」，磁悬浮那层意思一点也出不来（2026-10-15 出图实测）。
    //
    //  拆成两个模型是因为两个半边要挂在**两个不同的东西**上：
    //  下半（鞍座 + 发光能量圈）**骑在兽背上**，跟着兽走；
    //  上半（镇地面）**保持水平**，镇建在它上面 —— 镇上的人站着不许一张一合。
    //  两半之间那道空就是磁悬浮场，它随兽的起伏变宽变窄，把那份起伏吃掉。
    //
    //  拆开还有一个白得的好处：两件各自底面都在本地 y=0，离线校验器判不出悬空件，
    //  `Tools/Check.cs` 里那条「故意空着的缝」登记因此可以删掉 ——
    //  缝从「一件造型内部靠登记豁免」变成「两件模型之间本来就空着的空间」。
    //
    //  再一轮（2026-10-15，用户一次提三件，第三件见 `TopDownRig.MinPitchDegrees`）：
    //  ① 底座改成两级台阶的鞍座、整体沉进背脊（原来悬在背脊上方 4.66 米）；
    //  ② 镇地面长成**倒三角浮山**（顶面那块 48×48 木板仍是平面，底下多一座 26 米深的山）；
    //  ③ 相机放开俯角下限，于是「从下往上看那座山」这件事才拍得出来。
    //  ①②两件的高度关系全由 `DeckView` 那一组常量算出来（`DeckY` 由另外三格推出来），
    //  改造型只回填那三格，位置不会散。
    // ============================================================
    private static Tscn.EmitResult BuildLevitationBase(string root)
    {
        return Emit(PathDef.Town(root), "LevitationBase", r =>
        {
            var p = r;
            // 高度全按**成品米**给死，不走 Fits 的造型期缩放：这一件的每一格都要对着兽背读，
            // 乘一个放大倍数就再也读不出「它有没有沉进背脊里」。
            //
            // **这一件改成两级台阶的鞍座（2026-10-15 用户：「让磁悬浮底座正确的贴合巨兽的背部」）**：
            // 原来是一个直径 12 米的平圆盘，盘底 28.50 米，而它正下方那块脊背（桶身 `barrel`）
            // 顶面只有 **23.84** 米 —— 盘底悬在背脊上方 **4.66 米**，画面上读成「一块饼搁在兽背上」。
            // 修法两笔：① 整件下沉 5.26 米（<c>DeckView.LevitationBaseY</c> 那一格），底面沉进背脊 0.60 米；
            // ② 盘底那一圈加宽到 13.20 米并收成两级台阶 —— 宽底座从背脊里长出来，
            // 加上「底面比背脊低」这一条，画面上就没有那道悬空的边了。
            const double SaddleD = 13.20;   // 鞍座外径（贴背那一圈，比镇地面 48 米的四分之一略宽）
            const double SaddleH = 1.20;
            const double StepD = 11.80;     // 第二级台阶：往上收一圈，收出「长在背上」的斜势
            const double StepH = 1.00;
            const double RingD = 11.20;     // 发光能量圈外径（沿用上一版，四分之一的边长口径不变）
            const double RingH = 0.85;
            const double HubD = 5.20;       // 内毂直径 —— 外径减内毂 6.00 ⇒ 带宽 3.00
            const double HubH = 0.85;

            // 鞍座：外径 13.20 米的矮圆柱，**底面沉在背脊里 0.60 米**。
            // 沉这么深不是为了好看，是给巨兽走路那一点起伏留的余量 ——
            // 底座挂在兽节点上不跟骨架动，背脊跟着脊柱骨转，盘底与背面那一层余量吃掉那份错动，
            // 差几厘米也不至于在盘沿露缝。读数走 `debug levitation` 的 `baseY` / `fieldGap`。
            Tscn.Cy(p, "saddle", "stoneM", new[] { 0.0, 0.0, 0.0 }, SaddleD, SaddleH);

            // 第二级台阶：直径收到 11.80 米。收出来的这一圈斜势就是「底座嵌在背上」那层意思 ——
            // 早先那版是一块等径的饼，边缘与背脊之间是一条齐平的硬边，怎么沉都读成「搁上去的」。
            Tscn.Cy(p, "step", "stoneD", new[] { 0.0, SaddleH, 0.0 }, StepD, StepH);

            // 发光能量圈：外径 11.20 米的环带，坐在第二级台阶顶面上。
            // 内毂比它小 6.00 米、正好压在上沿，于是从 3/4 侧俯看过去那圈光整圈露出来 ——
            // 一眼是「发光的圈」，不是「一块板」。四座能量塔与塔顶发射头那一格就是这一圈的全部意思。
            //
            // **带宽 1.40 → 3.00**（2026-10-15 出图实测）：1.40 米那圈在机位上只剩两个像素，
            // 加上过曝成白色，读出来是「白垫圈」而不是「发光的圈」。加宽到 3.00 米才读得出那一圈。
            Tscn.Cy(p, "ring", "glowAmber", new[] { 0.0, SaddleH + StepH, 0.0 }, RingD, RingH);
            Tscn.Cy(p, "hub", "stoneM", new[] { 0.0, SaddleH + StepH + RingH, 0.0 }, HubD, HubH);
        });
    }

    /// <summary>
    /// 浮山五层岩体（自上而下）：底面 y、X/Z 偏心、尺寸、绕 Y 的偏航。
    ///
    /// <para>**为什么写成一张表而不是五段 <c>Bx</c> 调用**：出图要调形状时（层数、哪层收得快、
    /// 哪层歪一点）只改这一张表，读一遍就知道整座山长什么样；散成五段调用就只能一处处试。
    /// 每层顶面都比下一层的底面高 0.30 米 —— 那 0.30 是「贴在一起」的余量，
    /// 离线校验器靠它判「不是悬空件」（容差 0.05）。</para>
    ///
    /// <para>配色在 <see cref="BuildTownDeck"/> 里按层号交替（深浅一层隔一层），
    /// 那道一层深一层浅的横带就是岩层断面，与参考图里那种层层叠起的岩体同一族读法。</para>
    /// </summary>
    private static readonly (double Y, double W, double D, double H, double X, double Z, double Ry)[] Mountain =
    [
        (21.40, 46.0, 45.0, 4.90, 0.0, 0.0, 4.0),
        (16.60, 39.0, 38.0, 5.10, 0.6, -0.5, -3.0),
        (11.80, 31.5, 32.0, 5.10, -0.7, 0.6, 3.5),
        (7.00, 23.5, 24.0, 5.10, 0.5, -0.6, -4.0),
        (2.40, 15.0, 15.5, 4.90, -0.4, 0.3, 5.0),
    ];

    /// <summary>
    /// 镇地面上的木板细节（2026-10-28 审查修复：一块素板读不出「木甲板」）：
    /// 顶面等距板缝（暗色薄条，顶面只高出 0.01 米防同面闪烁）+ 四周边梁（压在板缘内侧，
    /// 顶面高出镇地面 0.08 米，平面尺寸不外挑——48 / 56 米口径不变）。
    /// </summary>
    private static void DeckPlankDetail(string p, double deckSize, double surfaceY)
    {
        const double SeamW = 0.10;      // 板缝宽
        const double SeamStep = 4.0;    // 每 4 米一道
        var half = deckSize / 2.0;
        var inset = 4.0;                // 板缝不贴着边梁走
        var n = (int)((deckSize - 2 * inset) / SeamStep);
        for (var i = 0; i <= n; i++)
            Tscn.Cbx(p, "plankSeam", "woodD",
                new[] { -n * SeamStep / 2.0 + i * SeamStep, surfaceY - 0.03, 0.0 },
                new[] { SeamW, 0.08, deckSize - 2 * inset });
        foreach (var s in new[] { -1.0, 1.0 })
        {
            Tscn.Bx(p, "edgeBeam", "woodD", new[] { 0.0, surfaceY - 0.22, s * (half - 0.30) },
                new[] { deckSize, 0.30, 0.60 });
            Tscn.Bx(p, "edgeBeam", "woodD", new[] { s * (half - 0.30), surfaceY - 0.22, 0.0 },
                new[] { 0.60, 0.30, deckSize - 1.20 });
        }
    }

    private static Tscn.EmitResult BuildTownDeck(string root)
    {
        return Emit(PathDef.Town(root), "TownDeck", r =>
        {
            var p = r;
            const double DeckSize = 48.00;      // 小镇地面：逻辑层 24×24 格、格长 2 米
            const double DeckH = 1.20;
            const double TrimOut = 0.12;        // 亮边比板边再探出多少
            const double TrimW = 0.36;
            const double SurfaceY = 27.20;      // **镇地面顶面 = 整件模型的最高点**

            // 整件模型的本地 y=0 是**锥尖发光核的底面**，镇地面在 SurfaceY。
            // 换句话说：这一件从下往上是「尖 → 岩体 → 平台」，镇子坐在最高那一面。
            // 本地 y=0 那一格是离线校验器判「落地」的依据（底面 ≤ 0.05），也是
            // `DeckView.LevitationY` 摆这一件时用的原点。
            //
            // 倒三角这件事（2026-10-15 用户：「让小镇变成倒三角，就像是一个倒着悬浮的山，
            // 当然最上面的小镇还是平面的」）—— 顶面那块 48×48 的木板仍是**平面**，
            // 十三件建筑与镇民照旧摆在上面，逻辑层一个坐标没动；变的只是平台底下长出一座山。

            // 镇地面：整座小镇建在它顶面上。板底在 SurfaceY - DeckH。
            //
            // （`Tscn.Bx` 的 y 是**底面**不是中心，所以板给 SurfaceY - DeckH、亮边给板厚的一半。）
            Tscn.Bx(p, "deck", "deckWood", new[] { 0.0, SurfaceY - DeckH, 0.0 },
                new[] { DeckSize, DeckH, DeckSize });

            // 板沿那圈暗光：比板边探出 0.30 米，从侧视看是浮在半空那块大板底下的一条亮线。
            // 用暗一档的金（glowDim）：它在方向光下不会被曝成白板，而过曝了这一层意思就没了。
            //
            // **亮边必须探出板边**：埋进去（早先那两版就是埋进去的）就一点都看不见。
            foreach (var s in new[] { -1, 1 })
            {
                Tscn.Bx(p, "deckTrim", "glowDim",
                    new[] { 0.0, SurfaceY - DeckH + DeckH / 2 - 0.13, s * (DeckSize / 2 + TrimOut) },
                    new[] { DeckSize + 0.60, 0.26, TrimW });
                Tscn.Bx(p, "deckTrim", "glowDim",
                    new[] { s * (DeckSize / 2 + TrimOut), SurfaceY - DeckH + DeckH / 2 - 0.13, 0.0 },
                    new[] { TrimW, 0.26, DeckSize + 0.60 });
            }

            // 顶面木板细节：板缝 + 四周边梁（见 DeckPlankDetail）。
            DeckPlankDetail(p, DeckSize, SurfaceY);

            // 五层岩体：一层比一层窄、往下收到 15 米见方，最后由锥尖收到一点。
            // 每层都往旁边错开一点并转一个小角度 —— 齐着心的五层方块读出来是婚礼蛋糕，
            // 错开加旋转才是「山」。
            //
            // **配色一层深一层浅**（2026-10-15 出图实测）：那道横带就是岩层断面，
            // 与参考图里层层叠起的岩体同一族读法；全用同一个色那道台阶就平掉了。
            //
            // **深色档从 `stoneD` 提到 `stoneM`**：这座山的整片山体朝下，太阳照不到，
            // 收得到的只有天光 —— 早先那版 `stoneM`/`stoneD` 交替在真机画面上整块读成黑剪影，
            // 层与层的断面全糊掉（一格深一格浅那层意思也没了）。提一档之后
            // 俯视与仰视两张都读得出「一层收一圈」。
            for (var i = 0; i < Mountain.Length; i++)
            {
                var (y, w, d, h, x, z, ry) = Mountain[i];
                Tscn.Bx(p, $"cliff{i}", i % 2 == 0 ? "stoneL" : "stoneM", new[] { x, y, z },
                    new[] { w, h, d }, Humanoid.O(ry: ry));
            }

            // **不要再往上加「斜插出来的岩脊板」那一类装饰件**（2026-10-15 出图实测踩到）：
            // 板斜插在崖体里、露出半个身位，从仰视看就是一块**切断深色崖面**的薄板 ——
            // 画面上读成「穿模」。而这一件本来就是干净的方块阶梯，阶梯不该有那种东西。
            // 打破对称这件事，层与层的错位（X/Z 偏心）加各自的偏航已经够了，插板是多余的。

            // 锥尖：底面在 2.90 米处、直径 10.50 米，收到 1.30 米高处的一个点。
            // `ConeEnd` 按「轴线一端」定位，rx=180 把轴翻成朝下，于是递进去的那个 `end`
            // 落在**锥体底面那一端**、尖端在它下面 h 米处（锥尖朝下正是要的）。
            Tscn.ConeEnd(p, "tip", "stoneM", new[] { 0.0, 2.90, 0.0 }, 10.50, 1.60,
                Humanoid.O(rx: 180));

            // 锥尖底下那枚发光核：从锥尖里探出来 1.30 米，是这件造型唯一的发光点。
            // 「浮着」这件事全靠它 —— 从下往上看，锥尖那一头亮着，
            // 而亮的那一头与下面底座顶上那圈光之间是空的，一眼是「悬着的」。
            // 它的底面就是整件模型的 y=0。
            Tscn.Cy(p, "tipGlow", "glowAmber", new[] { 0.0, 0.00, 0.0 }, 3.60, 1.60);
        });
    }

    // ---- T10：二阶底座与二阶甲板（各自新起一个 Build，一阶那两件的代码一个字不动）----
    //
    //  为什么另起一件而不是给 TownDeck 加参数：「现有几何不动」是这一波的硬口径 ——
    //  一阶那两件要逐字节重跑得出盘上现档，改它们的生成代码就得重新验字节，白费一道工。
    //
    //  尺寸来路（四层关系照旧：巨兽、紧凑底座、悬浮空隙、倒悬山镇）：
    //  逻辑层二阶甲板 28×28 格 × 格长 2.0 米 = 56.0 米（格数与格长的一阶口径见上，
    //  规格口径「边长 48 米到 56 米」；接线在 ticket 27，本件只出资产）。
    //  平面从 48 到 56 是 7/6 档：底座各径与岩体各层、锥尖、发光核全按这一档放平面；
    //  **竖向一格都不放** —— 悬浮空隙、镇地面顶面 27.20 米与兽背的对位关系全在竖向那一列上，
    //  动了竖向四层就散架（与一阶「高度不放大」是同一条判据）。
    private static Tscn.EmitResult BuildLevitationBaseTier2(string root)
    {
        return Emit(PathDef.Town(root), "LevitationBaseTier2", r =>
        {
            var p = r;
            // 底座视觉直径跟着镇地面走四分之一口径：13.20 × 7/6 = 15.40，往下各圈同档。
            const double SaddleD = 15.40;   // 鞍座外径（贴背那一圈）
            const double SaddleH = 1.20;
            const double StepD = 13.80;     // 第二级台阶
            const double StepH = 1.00;
            const double RingD = 13.10;     // 发光能量圈外径
            const double RingH = 0.85;
            const double HubD = 6.05;       // 内毂直径（外径减内毂 6.05 ⇒ 带宽 ≈ 3.50，随平面同档放宽）
            const double HubH = 0.85;

            Tscn.Cy(p, "saddle", "stoneM", new[] { 0.0, 0.0, 0.0 }, SaddleD, SaddleH);
            Tscn.Cy(p, "step", "stoneD", new[] { 0.0, SaddleH, 0.0 }, StepD, StepH);
            Tscn.Cy(p, "ring", "glowAmber", new[] { 0.0, SaddleH + StepH, 0.0 }, RingD, RingH);
            Tscn.Cy(p, "hub", "stoneM", new[] { 0.0, SaddleH + StepH + RingH, 0.0 }, HubD, HubH);
        });
    }

    /// <summary>
    /// 二阶浮山五层岩体（形状口径同一阶 <see cref="Mountain"/>：层数、Y、错位、偏航照抄，
    /// 平面各径 × 7/6 —— 56/48 那一档；竖向一格不放，理由见 <see cref="BuildTownDeckTier2"/> 那一段）。
    /// </summary>
    private static readonly (double Y, double W, double D, double H, double X, double Z, double Ry)[] MountainTier2 =
    [
        (21.40, 53.70, 52.50, 4.90, 0.0, 0.0, 4.0),
        (16.60, 45.50, 44.30, 5.10, 0.6, -0.5, -3.0),
        (11.80, 36.75, 37.30, 5.10, -0.7, 0.6, 3.5),
        (7.00, 27.40, 28.00, 5.10, 0.5, -0.6, -4.0),
        (2.40, 17.50, 18.10, 4.90, -0.4, 0.3, 5.0),
    ];

    private static Tscn.EmitResult BuildTownDeckTier2(string root)
    {
        return Emit(PathDef.Town(root), "TownDeckTier2", r =>
        {
            var p = r;
            const double DeckSize = 56.00;      // 小镇地面：二阶 28×28 格、格长 2.0 米（接线在 ticket 27）
            const double DeckH = 1.20;
            const double TrimOut = 0.12;        // 亮边比板边再探出多少
            const double TrimW = 0.36;
            const double SurfaceY = 27.20;      // 镇地面顶面 —— 与一阶同高：四层关系里竖向那一列一格没动

            // 镇地面（同口径：`Tscn.Bx` 的 y 是底面）。
            Tscn.Bx(p, "deck", "deckWood", new[] { 0.0, SurfaceY - DeckH, 0.0 },
                new[] { DeckSize, DeckH, DeckSize });

            // 板沿那圈暗光（与一阶同一判据：必须探出板边才看得见）。
            foreach (var s in new[] { -1, 1 })
            {
                Tscn.Bx(p, "deckTrim", "glowDim",
                    new[] { 0.0, SurfaceY - DeckH + DeckH / 2 - 0.13, s * (DeckSize / 2 + TrimOut) },
                    new[] { DeckSize + 0.60, 0.26, TrimW });
                Tscn.Bx(p, "deckTrim", "glowDim",
                    new[] { s * (DeckSize / 2 + TrimOut), SurfaceY - DeckH + DeckH / 2 - 0.13, 0.0 },
                    new[] { TrimW, 0.26, DeckSize + 0.60 });
            }

            // 顶面木板细节：板缝 + 四周边梁（与一阶同一助手，只按 56 米排缝）。
            DeckPlankDetail(p, DeckSize, SurfaceY);

            // 五层岩体：平面 × 7/6、每层的 Y / 错位 / 偏航照一阶 —— 山形不变，只是整座大一圈。
            // 各层顶面仍比下一层底面高 0.30 米（悬空件余量，与一阶同一口径）。
            for (var i = 0; i < MountainTier2.Length; i++)
            {
                var (y, w, d, h, x, z, ry) = MountainTier2[i];
                Tscn.Bx(p, $"cliff{i}", i % 2 == 0 ? "stoneL" : "stoneM", new[] { x, y, z },
                    new[] { w, h, d }, Humanoid.O(ry: ry));
            }

            // 锥尖与发光核：平面同档放大（10.50 / 3.60 → 12.25 / 4.20），竖向照旧。
            Tscn.ConeEnd(p, "tip", "stoneM", new[] { 0.0, 2.90, 0.0 }, 12.25, 1.60,
                Humanoid.O(rx: 180));
            Tscn.Cy(p, "tipGlow", "glowAmber", new[] { 0.0, 0.00, 0.0 }, 4.20, 1.60);
        });
    }
}
