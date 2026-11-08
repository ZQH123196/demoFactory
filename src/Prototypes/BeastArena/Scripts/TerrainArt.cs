#nullable enable
using System;
using System.Globalization;
using DemoFactory.Prototypes.BeastArena.Logic;
using Godot;

namespace DemoFactory.Prototypes.BeastArena;

/// <summary>
/// 大地图地形的表现层 —— 程序化贴图 + 不透明圆盘。
///
/// <para><b>为什么不是模型</b>：地面与地形色块不代表任何东西；「真模型」那一组判据管的是
/// 兽、城、道具这类有具体造型的东西。这里与 <see cref="DeckView"/> 的场地陈设、
/// 技能试验场脚下的地网格同一族。</para>
///
/// <para><b>为什么加程序化细节</b>：灰盒期只有四块纯色。垂直切片要求主线沿途不留灰盒，
/// 所以按 <see cref="BigMapTerrain"/> 的四类地形生成颜色、法线、粗糙度三张图：
/// 草地有深浅斑块与草点，沙漠有沙丘带，道路有车辙与碎石边，水有波纹与岸线。
/// 噪声全部由固定种子的整数哈希生成，同条件重跑逐像素一致。</para>
///
/// <para><b>圆盘替代方形透明面</b>：方形面靠 alpha 抠圆，材质必须透明，地形因此吃不到
/// 方向光的阴影；改成 <see cref="DiscSegments"/> 段三角扇的圆盘后材质不透明，城镇、树与
/// 巨兽的影子落得上去。圆盘半径仍是 <see cref="BigMapGrid.LandRadiusMeters"/>，与侧壁同半径；
/// UV 按世界米线性映射，第 0 列在最西、第 0 行在最北（与旧贴图同一套换算）。</para>
/// </summary>
internal static class TerrainArt
{
    /// <summary>颜色贴图的横向分辨率（像素/米）。</summary>
    public const int AlbedoPixelsPerMeter = 2;

    /// <summary>法线与粗糙度图的横向分辨率（像素/米）—— 细节频率低一档，省内存。</summary>
    public const int DetailPixelsPerMeter = 1;

    /// <summary>颜色贴图边长（像素）。</summary>
    public const int AlbedoPixels = BigMapTerrain.GridCells * AlbedoPixelsPerMeter;

    /// <summary>法线 / 粗糙度贴图边长（像素）。</summary>
    public const int DetailPixels = BigMapTerrain.GridCells * DetailPixelsPerMeter;

    /// <summary>固定噪声种子；同种子重跑画面一致。</summary>
    private const int Seed = 20261028;

    /// <summary>圆盘边上的段数 —— 侧壁的圆柱段数读它，两边同段数，边沿不露缝。</summary>
    public const int DiscSegments = 128;

    /// <summary>三张图与圆盘一次生成；调用方只拿结果。</summary>
    public static (Image Albedo, Image Normal, Image Roughness, ArrayMesh Disc) Build()
    {
        var kinds = BuildKindGrid();
        return (BuildAlbedo(kinds), BuildNormal(kinds), BuildRoughness(kinds), BuildDisc());
    }

    /// <summary>
    /// 逻辑地形表的一层只读缓存：像素循环要查当前格与四邻，直接打上百万次
    /// <see cref="BigMapTerrain.TerrainAtCell"/> 会反复做圆盘判界。缓存只在本次生成里活着，
    /// 不持有、不写回；地形表的唯一来源仍是 <see cref="BigMapTerrain"/>。
    /// </summary>
    private static TerrainKind?[] BuildKindGrid()
    {
        var kinds = new TerrainKind?[BigMapTerrain.GridCells * BigMapTerrain.GridCells];
        for (var z = BigMapTerrain.MinCell; z <= BigMapTerrain.MaxCell; z++)
            for (var x = BigMapTerrain.MinCell; x <= BigMapTerrain.MaxCell; x++)
                kinds[Index(x, z)] = BigMapTerrain.TerrainAtCell(x, z);
        return kinds;
    }

    private static int Index(int x, int z)
        => ((z - BigMapTerrain.MinCell) * BigMapTerrain.GridCells) + (x - BigMapTerrain.MinCell);

    private static TerrainKind? KindAt(TerrainKind?[] kinds, int x, int z)
    {
        if (x < BigMapTerrain.MinCell || x > BigMapTerrain.MaxCell
            || z < BigMapTerrain.MinCell || z > BigMapTerrain.MaxCell) return null;
        return kinds[Index(x, z)];
    }

    private static Image BuildAlbedo(TerrainKind?[] kinds)
    {
        var size = AlbedoPixels;
        var data = new byte[size * size * 3];
        for (var py = 0; py < size; py++)
        {
            // 行号从北往南：第 0 行对应 z = MaxCell + 1 的那一缘。
            var wz = (BigMapTerrain.MaxCell + 1) - ((py + 0.5f) / AlbedoPixelsPerMeter);
            var cz = (int)MathF.Floor(wz);
            for (var px = 0; px < size; px++)
            {
                var wx = BigMapTerrain.MinCell + ((px + 0.5f) / AlbedoPixelsPerMeter);
                var cx = (int)MathF.Floor(wx);
                var kind = KindAt(kinds, cx, cz);
                var color = kind is null ? OutsideColor() : ColorOf(kinds, kind.Value, wx, wz, cx, cz);
                var offset = ((py * size) + px) * 3;
                data[offset] = ToByte(color.R);
                data[offset + 1] = ToByte(color.G);
                data[offset + 2] = ToByte(color.B);
            }
        }

        var image = Image.CreateFromData(size, size, false, Image.Format.Rgb8, data);
        image.GenerateMipmaps();
        return image;
    }

    private static Color ColorOf(
        TerrainKind?[] kinds, TerrainKind kind, float x, float z, int cx, int cz)
    {
        var macro = Fbm(x * 0.025f, z * 0.025f, Seed + 101);
        var micro = Fbm(x * 0.55f, z * 0.55f, Seed + 211);
        var speckle = Hash01((int)MathF.Floor(x), (int)MathF.Floor(z), Seed + 307);

        return kind switch
        {
            TerrainKind.Grass => GrassColor(kinds, x, z, macro, micro, speckle, cx, cz),
            TerrainKind.Desert => DesertColor(x, z, macro, micro, speckle),
            TerrainKind.Road => RoadColor(kinds, x, z, micro, speckle, cx, cz),
            TerrainKind.Water => WaterColor(kinds, x, z, macro, micro, cx, cz),
            _ => OutsideColor(),
        };
    }

    private static Color GrassColor(
        TerrainKind?[] kinds, float x, float z, float macro, float micro, float speckle, int cx, int cz)
    {
        var dark = new Color(0.27f, 0.40f, 0.21f);
        var light = new Color(0.48f, 0.62f, 0.33f);
        var mix = Math.Clamp(0.25f + (0.55f * macro) + (0.20f * micro), 0f, 1f);
        var color = dark.Lerp(light, mix);
        color = color.Lerp(new Color(0.55f, 0.58f, 0.30f), speckle * 0.10f);
        if (AnyNeighbor(kinds, cx, cz, TerrainKind.Water))
            color = color.Lerp(new Color(0.27f, 0.33f, 0.23f), 0.18f);
        return color;
    }

    private static Color DesertColor(float x, float z, float macro, float micro, float speckle)
    {
        var dark = new Color(0.70f, 0.60f, 0.38f);
        var light = new Color(0.93f, 0.85f, 0.62f);
        var dune = 0.5f + (0.5f * MathF.Sin((x * 0.35f) + (z * 0.18f) + (macro * 4f)));
        var mix = Math.Clamp(0.20f + (0.45f * macro) + (0.25f * dune) + (0.10f * micro), 0f, 1f);
        var color = dark.Lerp(light, mix);
        return color.Lerp(new Color(0.64f, 0.54f, 0.34f), speckle * 0.10f);
    }

    private static Color RoadColor(
        TerrainKind?[] kinds, float x, float z, float micro, float speckle, int cx, int cz)
    {
        var dark = new Color(0.47f, 0.35f, 0.22f);
        var light = new Color(0.68f, 0.53f, 0.34f);
        var color = dark.Lerp(light, Math.Clamp(0.20f + (0.70f * micro), 0f, 1f));

        var horizontal = KindAt(kinds, cx - 1, cz) == TerrainKind.Road
                         || KindAt(kinds, cx + 1, cz) == TerrainKind.Road;
        var vertical = KindAt(kinds, cx, cz - 1) == TerrainKind.Road
                       || KindAt(kinds, cx, cz + 1) == TerrainKind.Road;
        if (horizontal != vertical)
        {
            var across = horizontal ? z : x;
            var rut = MathF.Pow(0.5f + (0.5f * MathF.Sin(across * 0.65f)), 8f);
            color = color.Lerp(new Color(0.38f, 0.28f, 0.17f), rut * 0.30f);
        }

        var edge = KindAt(kinds, cx - 1, cz) != TerrainKind.Road
                   || KindAt(kinds, cx + 1, cz) != TerrainKind.Road
                   || KindAt(kinds, cx, cz - 1) != TerrainKind.Road
                   || KindAt(kinds, cx, cz + 1) != TerrainKind.Road;
        if (edge) color = color.Lerp(new Color(0.36f, 0.26f, 0.16f), 0.30f);

        return color.Lerp(new Color(0.76f, 0.64f, 0.44f), speckle * 0.16f);
    }

    private static Color WaterColor(
        TerrainKind?[] kinds, float x, float z, float macro, float micro, int cx, int cz)
    {
        var deep = new Color(0.20f, 0.36f, 0.52f);
        var shallow = new Color(0.34f, 0.56f, 0.68f);
        var waveA = MathF.Sin((x * 1.4f) + (z * 1.9f) + (macro * 4f));
        var waveB = MathF.Sin((x * 2.9f) - (z * 1.3f) + (micro * 3f));
        var wave = 0.5f + (0.25f * waveA) + (0.25f * waveB);
        var mix = Math.Clamp(0.30f + (0.25f * macro) + (0.20f * wave), 0f, 1f);
        var color = deep.Lerp(shallow, mix);

        var rippleA = MathF.Sin((x * 3.3f) + (z * 2.2f));
        var rippleB = MathF.Sin((x * 1.7f) - (z * 2.8f));
        var ripple = MathF.Pow(0.5f + (0.25f * rippleA) + (0.25f * rippleB), 6f);
        color = color.Lerp(new Color(0.52f, 0.70f, 0.80f), ripple * 0.06f);

        var shore = ShoreFactor(kinds, cx, cz);
        if (shore > 0f)
            color = color.Lerp(new Color(0.50f, 0.68f, 0.76f), shore * 0.35f);
        return color;
    }

    /// <summary>
    /// 水格离岸几格：1 / 2 / 3 格分别回 1、0.67、0.33，4 格及以上回 0。
    /// 只看四向，够湖、河这些矩形水域；岸边那一带因此有一圈浅水渐变。
    /// </summary>
    private static float ShoreFactor(TerrainKind?[] kinds, int cx, int cz)
    {
        for (var radius = 1; radius <= 3; radius++)
        {
            if (KindAt(kinds, cx - radius, cz) != TerrainKind.Water
                || KindAt(kinds, cx + radius, cz) != TerrainKind.Water
                || KindAt(kinds, cx, cz - radius) != TerrainKind.Water
                || KindAt(kinds, cx, cz + radius) != TerrainKind.Water)
                return 1f - ((radius - 1) / 3f);
        }

        return 0f;
    }

    private static Image BuildNormal(TerrainKind?[] kinds)
    {
        var size = DetailPixels;
        var data = new byte[size * size * 3];
        const float eps = 0.5f / DetailPixelsPerMeter;
        for (var py = 0; py < size; py++)
        {
            var wz = (BigMapTerrain.MaxCell + 1) - ((py + 0.5f) / DetailPixelsPerMeter);
            var cz = (int)MathF.Floor(wz);
            for (var px = 0; px < size; px++)
            {
                var wx = BigMapTerrain.MinCell + ((px + 0.5f) / DetailPixelsPerMeter);
                var cx = (int)MathF.Floor(wx);
                var kind = KindAt(kinds, cx, cz) ?? TerrainKind.Grass;
                var hL = HeightAt(kind, wx - eps, wz);
                var hR = HeightAt(kind, wx + eps, wz);
                var hD = HeightAt(kind, wx, wz - eps);
                var hU = HeightAt(kind, wx, wz + eps);
                // 切空间：X 沿 +X、Y 沿 -Z（v 从北往南增）、Z 沿法线 +Y。
                // 平地必须落成 (0,0,1) -> (128,128,255)；写成世界轴向会让整块地面成为统一斜面。
                var normal = new Vector3(hL - hR, hU - hD, 2f * eps).Normalized();
                var offset = ((py * size) + px) * 3;
                data[offset] = ToByte((normal.X * 0.5f) + 0.5f);
                data[offset + 1] = ToByte((normal.Y * 0.5f) + 0.5f);
                data[offset + 2] = ToByte((normal.Z * 0.5f) + 0.5f);
            }
        }

        var image = Image.CreateFromData(size, size, false, Image.Format.Rgb8, data);
        image.GenerateMipmaps(renormalize: true);
        return image;
    }

    private static float HeightAt(TerrainKind kind, float x, float z) => kind switch
    {
        TerrainKind.Grass => Fbm(x * 1.1f, z * 1.1f, Seed + 401) * 0.040f,
        TerrainKind.Desert => ((0.5f + (0.5f * MathF.Sin((x * 0.9f) + (z * 0.4f)))) * 0.030f)
                              + (Fbm(x * 0.7f, z * 0.7f, Seed + 403) * 0.010f),
        TerrainKind.Road => Fbm(x * 2.2f, z * 2.2f, Seed + 405) * 0.012f,
        TerrainKind.Water => (0.5f + (0.5f * MathF.Sin((x * 1.5f) + (z * 2.1f)))) * 0.060f,
        _ => 0f,
    };

    private static Image BuildRoughness(TerrainKind?[] kinds)
    {
        var size = DetailPixels;
        var data = new byte[size * size * 3];
        for (var py = 0; py < size; py++)
        {
            var wz = (BigMapTerrain.MaxCell + 1) - ((py + 0.5f) / DetailPixelsPerMeter);
            var cz = (int)MathF.Floor(wz);
            for (var px = 0; px < size; px++)
            {
                var wx = BigMapTerrain.MinCell + ((px + 0.5f) / DetailPixelsPerMeter);
                var cx = (int)MathF.Floor(wx);
                var kind = KindAt(kinds, cx, cz) ?? TerrainKind.Grass;
                var micro = Fbm(wx * 0.55f, wz * 0.55f, Seed + 501);
                var roughness = kind switch
                {
                    TerrainKind.Water => 0.14f + (0.06f * micro),
                    TerrainKind.Road => 0.82f + (0.10f * micro),
                    TerrainKind.Desert => 0.90f + (0.05f * micro),
                    _ => 0.94f + (0.03f * micro),
                };
                var value = ToByte(roughness);
                var offset = ((py * size) + px) * 3;
                data[offset] = value;
                data[offset + 1] = value;
                data[offset + 2] = value;
            }
        }

        var image = Image.CreateFromData(size, size, false, Image.Format.Rgb8, data);
        image.GenerateMipmaps();
        return image;
    }

    private static ArrayMesh BuildDisc()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetNormal(Vector3.Up);
        st.SetUV(new Vector2(0.5f, 0.5f));
        st.AddVertex(Vector3.Zero);

        var radius = (float)BigMapGrid.LandRadiusMeters;
        var span = (float)BigMapTerrain.GridCells;
        if (MathF.Abs((radius * 2f) - span) > 0.01f)
            throw new InvalidOperationException(
                $"地形圆盘直径 {radius.ToString(CultureInfo.InvariantCulture)} 米与地形贴图边长 {span.ToString(CultureInfo.InvariantCulture)} 米不一致。");
        for (var i = 0; i < DiscSegments; i++)
        {
            var angle = MathF.Tau * i / DiscSegments;
            var x = MathF.Cos(angle) * radius;
            var z = MathF.Sin(angle) * radius;
            st.SetNormal(Vector3.Up);
            st.SetUV(new Vector2(0.5f + (x / span), 0.5f - (z / span)));
            st.AddVertex(new Vector3(x, 0f, z));
        }

        for (var i = 0; i < DiscSegments; i++)
        {
            st.AddIndex(0);
            st.AddIndex(1 + i);
            st.AddIndex(1 + ((i + 1) % DiscSegments));
        }

        st.GenerateTangents();
        return st.Commit();
    }

    private static bool AnyNeighbor(TerrainKind?[] kinds, int cx, int cz, TerrainKind want)
        => KindAt(kinds, cx - 1, cz) == want
           || KindAt(kinds, cx + 1, cz) == want
           || KindAt(kinds, cx, cz - 1) == want
           || KindAt(kinds, cx, cz + 1) == want;

    private static Color OutsideColor() => new(0.34f, 0.40f, 0.28f);

    private static byte ToByte(float value)
        => (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);

    private static float Fbm(float x, float z, int seed)
    {
        var sum = 0f;
        var amplitude = 0.5f;
        var frequency = 1f;
        for (var i = 0; i < 3; i++)
        {
            sum += amplitude * ValueNoise(x * frequency, z * frequency, seed + (i * 131));
            frequency *= 2.03f;
            amplitude *= 0.5f;
        }

        return sum / 0.875f;
    }

    private static float ValueNoise(float x, float z, int seed)
    {
        var ix = (int)MathF.Floor(x);
        var iz = (int)MathF.Floor(z);
        var fx = x - ix;
        var fz = z - iz;
        var u = fx * fx * (3f - (2f * fx));
        var v = fz * fz * (3f - (2f * fz));
        var a = Hash01(ix, iz, seed);
        var b = Hash01(ix + 1, iz, seed);
        var c = Hash01(ix, iz + 1, seed);
        var d = Hash01(ix + 1, iz + 1, seed);
        return Lerp(Lerp(a, b, u), Lerp(c, d, u), v);
    }

    private static float Hash01(int x, int z, int seed)
    {
        unchecked
        {
            var hash = (uint)((x * 374761393) + (z * 668265263) + (seed * 1442695041));
            hash = (hash ^ (hash >> 13)) * 1274126177u;
            hash ^= hash >> 16;
            return (hash & 0x00FFFFFFu) / 16777216f;
        }
    }

    private static float Lerp(float from, float to, float weight) => from + ((to - from) * weight);
}
