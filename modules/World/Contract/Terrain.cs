using System;

namespace Neo.World;

/// <summary>
/// The ground you fly over (and crash into), in meters, Y up: a mostly open plain at
/// <see cref="PlainHeight"/> with rolling hills (tens of meters), three small mountains, the
/// winding river and two lakes. Water is a flat surface at <see cref="WaterLevel"/> and counts
/// as ground. The World's ground mesh is built from <see cref="HeightAt"/> on a
/// <see cref="GridCells"/> grid, so what is drawn matches it (to under 1 m between vertices).
/// Every function is pure, deterministic and allocation-free.
/// </summary>
public static class Terrain
{
    /// <summary>The highest point (m): the top of the tallest mountain. Nothing reaches above it.</summary>
    public const float MaxHeight = 250f;

    /// <summary>Height (m) of the water surface of the river and the lakes: the lowest ground.</summary>
    public const float WaterLevel = WorldBounds.GroundY;

    /// <summary>Height (m) of the open plain (the center, the start areas, the river valley).</summary>
    public const float PlainHeight = 2f;

    /// <summary>Cells per side of the ground mesh (the vertices sit every <see cref="GridStep"/> m).</summary>
    public const int GridCells = 400;

    /// <summary>Distance (m) between two neighboring vertices of the ground mesh.</summary>
    public const float GridStep = 2f * WorldBounds.HalfSize / GridCells;

    /// <summary>Half the river's width (m) at the water's edge, on the plain.</summary>
    public const float RiverHalfWidth = 22f;

    // Mountains: x, z, radius, height above the plain (m). Disjoint footprints, away from the
    // center, the start areas (0, +-1500), the river and the lakes.
    private const float M0X = -1450f, M0Z = -500f, M0R = 430f, M0H = MaxHeight - PlainHeight;
    private const float M1X = 1350f, M1Z = 150f, M1R = 450f, M1H = 205f;
    private const float M2X = -850f, M2Z = 950f, M2R = 380f, M2H = 165f;
    /// <summary>How much the ridged noise roughens a mountain's slopes (under 1 keeps its top the highest point).</summary>
    private const float Ridges = 0.6f;

    // Lakes: center x, z and half-axes (m) of their ellipse.
    private const float L0X = -700f, L0Z = -900f, L0A = 230f, L0B = 140f;
    private const float L1X = 850f, L1Z = 1150f, L1A = 170f, L1B = 125f;

    /// <summary>Highest rolling hill above the plain (m).</summary>
    private const float HillHeight = 46f;
    private const float MeadowHeight = 3f;
    /// <summary>Lowest point of the river and lake beds (m), below <see cref="WaterLevel"/>.</summary>
    private const float BedHeight = WaterLevel - 2.5f;
    /// <summary>Width (m) over which a shore slopes from the bed up to the land.</summary>
    private const float ShoreWidth = 50f;

    /// <summary>Distance (m) from the center and from each team's start point (0, +-1500) kept open and low.</summary>
    public const float OpenRadius = 350f;
    private const float OpenFade = 700f;

    /// <summary>
    /// Height (m) of the ground, or of the water surface on the river and lakes, at
    /// (<paramref name="x"/>, <paramref name="z"/>): always within <see cref="WaterLevel"/>..<see cref="MaxHeight"/>.
    /// </summary>
    public static float HeightAt(float x, float z) => MathF.Max(BedAt(x, z), WaterLevel);

    /// <summary>Depth (m) of water at (<paramref name="x"/>, <paramref name="z"/>): 0 on dry land.</summary>
    public static float WaterDepthAt(float x, float z) => MathF.Max(WaterLevel - BedAt(x, z), 0f);

    /// <summary>Whether (<paramref name="x"/>, <paramref name="z"/>) is on the river or a lake.</summary>
    public static bool IsWater(float x, float z) => BedAt(x, z) < WaterLevel;

    /// <summary>Center line of the river (its x) at <paramref name="z"/>.</summary>
    public static float RiverX(float z) =>
        260f * MathF.Sin(z * 0.0011f + 0.6f) + 120f * MathF.Sin(z * 0.0029f + 2.1f) + 40f * MathF.Sin(z * 0.0071f);

    private static float RiverSlope(float z) =>
        0.286f * MathF.Cos(z * 0.0011f + 0.6f) + 0.348f * MathF.Cos(z * 0.0029f + 2.1f) + 0.284f * MathF.Cos(z * 0.0071f);

    /// <summary>Distance (m) from the river's center line.</summary>
    public static float RiverDistance(float x, float z)
    {
        float s = RiverSlope(z);
        return MathF.Abs(x - RiverX(z)) / MathF.Sqrt(1f + s * s);
    }

    /// <summary>Ground under any water: the land, carved down to the bed along the river and in the lakes.</summary>
    private static float BedAt(float x, float z)
    {
        float river = RiverDistance(x, z) - RiverHalfWidth;
        float lake = MathF.Min(LakeDistance(x, z, L0X, L0Z, L0A, L0B, 11), LakeDistance(x, z, L1X, L1Z, L1A, L1B, 23));
        float shore = MathF.Min(river, lake);
        float land = LandAt(x, z, shore);
        float wet = 1f - SmoothStep(-ShoreWidth * 0.5f, ShoreWidth * 0.5f, shore);
        return land + (BedHeight - land) * wet;
    }

    /// <summary>Signed distance (m, about) from a lake's wobbly shore: negative inside.</summary>
    private static float LakeDistance(float x, float z, float cx, float cz, float a, float b, int seed)
    {
        float dx = (x - cx) / a, dz = (z - cz) / b;
        float wobble = 0.22f * (Noise(x / 140f, z / 140f, seed) - 0.5f);
        return (MathF.Sqrt(dx * dx + dz * dz) - 1f - wobble) * MathF.Min(a, b);
    }

    /// <summary>Dry land: plain, meadow swells, rolling hills (kept off the open areas and valleys), mountains.</summary>
    private static float LandAt(float x, float z, float shore)
    {
        float open = MathF.Min(Length(x, z), MathF.Min(Length(x, z - 1500f), Length(x, z + 1500f)));
        float keep = SmoothStep(OpenRadius, OpenFade, open) * SmoothStep(40f, 220f, shore);
        float rolling = 0.65f * Noise(x / 620f, z / 620f, 3) + 0.35f * Noise(x / 230f, z / 230f, 5);
        float hills = HillHeight * SmoothStep(0.3f, 0.85f, rolling) * keep;
        float meadow = MeadowHeight * Noise(x / 110f, z / 110f, 7);

        float mask = 0f, mountains = 0f;
        Mountain(x, z, M0X, M0Z, M0R, M0H, 31, ref mask, ref mountains);
        Mountain(x, z, M1X, M1Z, M1R, M1H, 37, ref mask, ref mountains);
        Mountain(x, z, M2X, M2Z, M2R, M2H, 41, ref mask, ref mountains);
        return PlainHeight + (meadow + hills) * (1f - mask) + mountains;
    }

    /// <summary>
    /// A mountain: a peak with a rounded top and concave slopes (1 at its top, 0 from <paramref name="radius"/> out) with its
    /// outline warped and its slopes roughened by ridged noise. The roughening vanishes at the
    /// top and stays monotone in the dome, so the top (height <paramref name="height"/>) is its highest point.
    /// </summary>
    private static void Mountain(float x, float z, float cx, float cz, float radius, float height, int seed, ref float mask, ref float sum)
    {
        float dx = x - cx, dz = z - cz;
        float s = MathF.Sqrt(dx * dx + dz * dz) / radius;
        s *= 1f + 0.4f * (Noise(x / 260f, z / 260f, seed) - 0.5f);
        if (s >= 1f)
            return;
        float t = 1f - s;
        float f = t * t * t * (1f + 3f * s); // smooth top, concave slopes
        float n1 = 2f * Noise(x / 190f, z / 190f, seed + 1) - 1f;
        float n2 = 2f * Noise(x / 105f, z / 105f, seed + 2) - 1f;
        // Soft creases (smooth, so the mesh can follow them), in -1..1.
        float ridged = 0.65f * (1f - 2f * n1 * n1) + 0.35f * (1f - 2f * n2 * n2);
        float g = f + Ridges * ridged * f * (1f - f);
        mask = MathF.Max(mask, f);
        sum += height * g;
    }

    private static float Length(float x, float z) => MathF.Sqrt(x * x + z * z);

    private static float SmoothStep(float from, float to, float t)
    {
        float u = Math.Clamp((t - from) / (to - from), 0f, 1f);
        return u * u * (3f - 2f * u);
    }

    /// <summary>Smooth value noise in 0..1, one value per unit cell.</summary>
    private static float Noise(float x, float z, int seed)
    {
        float fx = MathF.Floor(x), fz = MathF.Floor(z);
        int ix = (int)fx, iz = (int)fz;
        float u = Fade(x - fx), v = Fade(z - fz);
        float a = Hash(ix, iz, seed), b = Hash(ix + 1, iz, seed);
        float c = Hash(ix, iz + 1, seed), d = Hash(ix + 1, iz + 1, seed);
        return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
    }

    private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

    private static float Hash(int x, int z, int seed)
    {
        uint h = unchecked((uint)x * 0x8DA6B343u ^ (uint)z * 0xD8163841u ^ (uint)seed * 0xCB1AB31Fu);
        h ^= h >> 13;
        h = unchecked(h * 0x85EBCA6Bu);
        h ^= h >> 16;
        return (h & 0xFFFFFF) / 16777216f;
    }
}
