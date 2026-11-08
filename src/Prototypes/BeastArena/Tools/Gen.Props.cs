#nullable enable
// 巨兽竞技场 造型源场景生成器 —— 静态场景件与点位物域（浮桥 / 飞艇 / 尸体 / 货物 / 城 / 食物点）。
//
// 本文件是 Gen 的部分实现（partial），按域拆分见 Gen.cs 文件头那一段。
// 约定同老件：最低点落在本地 y=0（表现层自己决定把它摆到多高）。

using DemoFactory.Tools;

namespace DemoFactory.Tools.BeastArena;

public static partial class Gen
{
    //  浮码头（4 只浮筒撑起的一片木台）：占地 2.40 × 1.30，最高约 1.28 米。
    private static Tscn.EmitResult BuildPontoon(string root)
    {
        return Emit(PathDef.Props(root), "Pontoon", r =>
        {
            var p = r;
            foreach (var sx in new[] { -0.72, 0.72 })
                foreach (var sz in new[] { -0.42, 0.42 })
                    Tscn.Cy(p, "float", "woodD", new[] { sx, 0.0, sz }, 0.44, 0.34);
            Tscn.Bx(p, "deck", "deckWood", new[] { 0.0, 0.34, 0.0 }, new[] { 2.40, 0.12, 1.30 });
            for (var i = 0; i < 5; i++)
                Tscn.Bx(p, "plank", "woodL", new[] { -0.96 + i * 0.48, 0.44, 0.0 }, new[] { 0.40, 0.04, 1.24 });
            foreach (var sx in new[] { -1.05, 1.05 })
                Tscn.Cy(p, "bollard", "woodD", new[] { sx, 0.42, 0.50 }, 0.16, 0.34);
            Tscn.Cy(p, "lampPost", "woodD", new[] { 0.0, 0.42, -0.50 }, 0.10, 0.70);
            Tscn.Sp(p, "lamp", "glowWarm", new[] { 0.0, 1.16, -0.50 }, new[] { 0.20, 0.24, 0.20 });

            Humanoid.Anchor(p, 1.60, 1.40);
        });
    }

    //  飞艇（气囊 + 尾翼 + 吊舱 + 两副螺旋桨）：占地 6.60 × 3.90。
    //  悬空是这个造型的全部意思（它挂在天上），所以 Check.cs 的 IntentionalGaps 登了它这一条。
    private static Tscn.EmitResult BuildAirship(string root)
    {
        return Emit(PathDef.Props(root), "Airship", r =>
        {
            var p = r;
            Tscn.Sp(p, "envelope", "canvas", new[] { 0.0, 2.30, 0.0 }, new[] { 6.60, 2.60, 2.60 });
            Tscn.Cn(p, "nose", "roofRed", new[] { 0.0, 2.30, 1.10 }, 1.30, 1.30, Humanoid.O(rx: 90));
            Tscn.Cbx(p, "finV", "clothBlue", new[] { 0.0, 2.30, -1.15 }, new[] { 0.08, 1.60, 0.70 });
            Tscn.Cbx(p, "finL", "clothBlue", new[] { -1.05, 2.30, -1.15 }, new[] { 1.20, 0.08, 0.70 });
            Tscn.Cbx(p, "finR", "clothBlue", new[] { 1.05, 2.30, -1.15 }, new[] { 1.20, 0.08, 0.70 });

            Tscn.Bx(p, "gondola", "woodL", new[] { 0.0, 1.30, 0.0 }, new[] { 1.10, 0.60, 3.20 });
            Tscn.Bx(p, "keel", "woodD", new[] { 0.0, 0.85, 0.0 }, new[] { 0.14, 0.50, 2.60 });
            foreach (var sx in new[] { -0.55, 0.55 })
                foreach (var sz in new[] { -1.00, -0.40, 0.20, 0.80 })
                    Tscn.Cbx(p, "window", "glowWarm", new[] { sx, 1.55, sz }, new[] { 0.08, 0.24, 0.30 });
            foreach (var sx in new[] { -0.72, 0.72 })
            {
                Tscn.Cbx(p, "propStub", "iron", new[] { sx, 1.55, 1.40 }, new[] { 0.40, 0.08, 0.08 });
                Tscn.Disc(p, "propBlade", "woodL", new[] { sx, 1.55, 1.42 }, 0.60, 0.05);
            }

            Humanoid.Anchor(p, 3.90, 3.00);
        });
    }

    //  尸体：一具摊在血泊上的活物（血泊垫底，其余几件都压在它上面）。
    private static Tscn.EmitResult BuildCorpse(string root)
    {
        return Emit(PathDef.Props(root), "Corpse", r =>
        {
            var p = r;
            Tscn.Bx(p, "pool", "roofRed", new[] { 0.0, 0.00, 0.10 }, new[] { 1.00, 0.03, 0.70 });
            Tscn.Bx(p, "torso", "cloth", new[] { 0.0, 0.00, 0.00 }, new[] { 0.36, 0.22, 0.60 });
            Tscn.Bx(p, "head", "skin", new[] { 0.0, 0.00, 0.44 }, new[] { 0.22, 0.20, 0.22 });
            Tscn.Bx(p, "armL", "cloth", new[] { -0.26, 0.00, 0.12 }, new[] { 0.12, 0.12, 0.50 });
            Tscn.Bx(p, "armR", "cloth", new[] { 0.26, 0.00, 0.06 }, new[] { 0.12, 0.12, 0.50 });
            Tscn.Bx(p, "legL", "cloth", new[] { -0.09, 0.00, -0.52 }, new[] { 0.15, 0.13, 0.50 });
            Tscn.Bx(p, "legR", "cloth", new[] { 0.09, 0.00, -0.50 }, new[] { 0.15, 0.13, 0.50 });
            Tscn.Bx(p, "bootL", "leatherD", new[] { -0.09, 0.00, -0.82 }, new[] { 0.17, 0.12, 0.16 });
            Tscn.Sp(p, "helm", "iron", new[] { -0.46, 0.09, 0.44 }, new[] { 0.26, 0.18, 0.26 });
            Tscn.Cbx(p, "sword", "steel", new[] { 0.50, 0.03, 0.10 }, new[] { 0.05, 0.05, 0.70 });
            Tscn.Cbx(p, "hilt", "woodD", new[] { 0.50, 0.03, -0.32 }, new[] { 0.06, 0.06, 0.20 });

            Humanoid.Anchor(p, 0.50, 0.60);
        });
    }

    //  货物：一只木箱带两道箍，压一只小箱，旁边一只桶与一只粮袋。
    private static Tscn.EmitResult BuildCargo(string root)
    {
        return Emit(PathDef.Props(root), "Cargo", r =>
        {
            var p = r;
            Tscn.Bx(p, "crate", "woodL", new[] { 0.0, 0.00, 0.0 }, new[] { 0.66, 0.58, 0.66 });
            Tscn.Bx(p, "bandA", "woodD", new[] { 0.0, 0.25, 0.0 }, new[] { 0.70, 0.07, 0.70 });
            Tscn.Bx(p, "bandB", "woodD", new[] { 0.0, 0.00, 0.0 }, new[] { 0.70, 0.07, 0.70 });
            Tscn.Bx(p, "crateTop", "woodD", new[] { -0.04, 0.55, 0.02 }, new[] { 0.50, 0.40, 0.50 });
            Tscn.Cy(p, "barrel", "woodD", new[] { -0.50, 0.00, 0.44 }, 0.42, 0.54);
            Tscn.Cy(p, "hoop", "iron", new[] { -0.50, 0.36, 0.44 }, 0.46, 0.06);
            Tscn.Sp(p, "sack", "crop", new[] { 0.56, 0.19, 0.44 }, new[] { 0.40, 0.38, 0.40 });

            Humanoid.Anchor(p, 1.25, 0.80);
        });
    }

    //  城池节点（大地图地标）：三层石台 + 石塔 + 顶上信标 + 一面旗。
    //
    //  2026-10-15 放大一档。放大前占地 1.80×1.60 米、高 3.26 米：位置是逻辑坐标 ×20 落到
    //  400 米见方的大地图上，而模型本身不缩放（不缩几何是造型的事），于是三座城在画面上是小点，
    //  读不出「城镇」。现在横 ×20、纵 ×8：占地 36.00×32.00 米、高 26.08 米 ——
    //  与 45 米长的巨兽同一量级，而竖向压得比横向浅，读起来是「一片聚落」而不是一根塔。
    private static Tscn.EmitResult BuildCityNode(string root)
    {
        return Emit(PathDef.Props(root), "CityNode", r =>
        {
            var p = r;
            Tscn.Bx(p, "rockA", "stoneD", new[] { 0.0, 0.00, 0.0 }, new[] { 36.00, 4.00, 32.00 });
            Tscn.Bx(p, "rockB", "stoneM", new[] { 0.0, 4.00, 0.0 }, new[] { 26.00, 2.72, 22.00 });
            Tscn.Bx(p, "tower", "stoneL", new[] { 0.0, 6.72, 0.0 }, new[] { 14.40, 12.00, 14.40 });
            foreach (var sx in new[] { -4.80, 4.80 })
                foreach (var sz in new[] { -4.80, 4.80 })
                    Tscn.Bx(p, "merlon", "stoneM", new[] { sx, 18.72, sz }, new[] { 3.20, 1.76, 3.20 });
            Tscn.Cy(p, "beacon", "glowGold", new[] { 0.0, 18.72, 0.0 }, 6.00, 2.08);
            Tscn.Cy(p, "pole", "woodD", new[] { 0.0, 20.00, 0.0 }, 1.40, 6.08);
            Tscn.Cbx(p, "banner", "roofRed", new[] { 4.40, 24.80, 0.0 }, new[] { 8.80, 2.40, 1.00 });
            Tscn.Cbx(p, "door", "woodD", new[] { 0.0, 8.96, 6.80 }, new[] { 6.00, 4.48, 2.00 });

            Humanoid.Anchor(p, 28.00, 10.00);
        });
    }

    //  食物节点（大地图地标）：一座小粮囤 + 一口带顶棚的井 + 三垄麦。
    //
    //  2026-10-15 与城池那一件同批放大：横 ×10、纵 ×5，占地 18.00×16.00 米、高 5.70 米。
    //  城池的两倍小 —— 寻路时认得出「这一处比另一处要紧」，又不会把两个食物点读成第二座城。
    private static Tscn.EmitResult BuildFoodNode(string root)
    {
        return Emit(PathDef.Props(root), "FoodNode", r =>
        {
            var p = r;
            Tscn.Bx(p, "ground", "dirt", new[] { 0.0, 0.00, 0.0 }, new[] { 18.00, 0.60, 16.00 });
            Tscn.Cy(p, "silo", "woodL", new[] { -4.80, 0.60, -3.00 }, 7.00, 3.50);
            Tscn.Cn(p, "siloCap", "thatch", new[] { -4.80, 4.10, -3.00 }, 8.20, 1.60);

            // 井整组摆到右前角：组节点带位移，井里那几件的坐标就都是井心的局部系
            var w = Tscn.GrpT("Well", p, new[] { 4.60, 0.60, 3.40 }, null);
            Tscn.Cy(w, "wellRim", "stoneL", new[] { 0.0, 0.0, 0.0 }, 6.20, 1.10);
            Tscn.Cy(w, "wellWater", "water", new[] { 0.0, 0.0, 0.0 }, 4.60, 0.20);
            Tscn.Cy(w, "bucket", "woodL", new[] { 0.0, 0.0, 0.0 }, 1.60, 0.90);
            Tscn.Bx(w, "postA", "woodD", new[] { -2.40, 0.0, -2.40 }, new[] { 0.90, 2.50, 0.90 });
            Tscn.Bx(w, "postB", "woodD", new[] { 2.40, 0.0, 2.40 }, new[] { 0.90, 2.50, 0.90 });
            Gable(w, "roofBlue", 8.40, 8.40, 2.50, 0.80, 0.60);

            for (var i = 0; i < 3; i++)
                Tscn.Bx(p, "wheatRow", "crop", new[] { -6.60 + i * 2.40, 0.60, 5.00 }, new[] { 1.60, 0.70, 5.20 });

            Humanoid.Anchor(p, 6.00, 4.00);
        });
    }
}
