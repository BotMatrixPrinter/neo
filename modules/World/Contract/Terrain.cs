using System;

namespace Neo.World;

/// <summary>
/// The ground you fly over (and crash into), in meters, Y up. Inside the playable square
/// (<see cref="WorldBounds.HalfSize"/>): a mostly open plain at <see cref="PlainHeight"/> with
/// rolling hills (tens of meters), two small mountains, the winding river and a lake. Around it,
/// from the square's edge out, a ring of massive mountains: a wall at least <see cref="WallMinHeight"/>
/// high from <see cref="WallReach"/> beyond the edge outward, with taller snowy peaks and more ranges
/// behind, so no plane gets over or around it. Water is a flat surface at <see cref="WaterLevel"/>
/// and counts as ground. The World's meshes are built from <see cref="HeightAt"/> (the ground on a
/// <see cref="GridCells"/> grid, the wall ring around it), so what is drawn matches it.
/// Every function is pure, deterministic and allocation-free.
/// </summary>
public static class Terrain
{
    /// <summary>The highest point (m): the top of the tallest wall peak (north of the map). Nothing reaches above it.</summary>
    public const float MaxHeight = 2750f;

    /// <summary>The highest point (m) inside the playable square: the top of its tallest mountain.</summary>
    public const float InnerMaxHeight = 200f;

    /// <summary>Least height (m) of the wall mountains from <see cref="WallReach"/> beyond the playable
    /// square's edge outward: well above the 1000 m where engines lose their climbing power.</summary>
    public const float WallMinHeight = 1600f;

    /// <summary>
    /// Distance (m) beyond the playable square's edge (on X or Z) from which the ground is at least
    /// <see cref="WallMinHeight"/> high. The wall's foot starts 0..120 m beyond the edge and rises
    /// steeply from there; inside the square the wall adds nothing.
    /// </summary>
    public const float WallReach = WallFootWobble + WallRise;

    /// <summary>Height (m) of the water surface of the river and the lake: the lowest ground.</summary>
    public const float WaterLevel = WorldBounds.GroundY;

    /// <summary>Height (m) of the open plain (the center, the start areas, the river valley).</summary>
    public const float PlainHeight = 2f;

    /// <summary>Cells per side of the ground mesh over the playable square (the vertices sit every <see cref="GridStep"/> m).</summary>
    public const int GridCells = 250;

    /// <summary>Distance (m) between two neighboring vertices of the ground mesh.</summary>
    public const float GridStep = 2f * WorldBounds.HalfSize / GridCells;

    /// <summary>Half the river's width (m) at the water's edge, on the plain.</summary>
    public const float RiverHalfWidth = 16f;

    /// <summary>Distance (m) from the center and from each team's start point (0, +-<see cref="StartDistance"/>) kept open and low.</summary>
    public const float OpenRadius = 175f;

    /// <summary>Distance (m) of each team's start point from the center: Allies at +Z (south), Central at -Z (north).</summary>
    public const float StartDistance = 600f;

    // Inner mountains: x, z, radius, height above the plain (m). Disjoint footprints, away from
    // the center, the start points, the river, the lake and the edge.
    private const float M0X = -620f, M0Z = -120f, M0R = 280f, M0H = InnerMaxHeight - PlainHeight;
    private const float M1X = 600f, M1Z = 200f, M1R = 250f, M1H = 160f;
    /// <summary>How much the ridged noise roughens a mountain's slopes (under 1 keeps its top the highest point).</summary>
    private const float Ridges = 0.6f;

    // The lake: center x, z and half-axes (m) of its ellipse.
    private const float L0X = -330f, L0Z = -470f, L0A = 140f, L0B = 90f;

    /// <summary>Highest rolling hill above the plain (m).</summary>
    private const float HillHeight = 46f;
    private const float MeadowHeight = 3f;
    /// <summary>Lowest point of the river and lake beds (m), below <see cref="WaterLevel"/>.</summary>
    private const float BedHeight = WaterLevel - 2.5f;
    /// <summary>Width (m) over which a shore slopes from the bed up to the land.</summary>
    private const float ShoreWidth = 40f;
    private const float OpenFade = 420f;

    // The wall: its foot wobbles 0..WallFootWobble m beyond the edge (bays and spurs), it rises over WallRise m to
    // WallHeight plus up to WallCrags of crags, and farther out up to WallFar more of rolling ranges.
    private const float WallFootWobble = 120f;
    private const float WallRise = 250f;
    private const float WallHeight = 1650f;
    private const float WallCrags = 380f;
    private const float WallFar = 500f;
    private const float WallRibs = 220f;
    private const float WallFarFade = 900f;
    /// <summary>Height range (m) over which a peak blends into the wall around it (no sharp crease).</summary>
    private const float PeakBlend = 150f;
    /// <summary>Highest top (m) of the wall and its mountains, apart from the tallest peak: more than
    /// <see cref="PeakBlend"/> under <see cref="MaxHeight"/>, so that peak's top stays the highest point.</summary>
    private const float PeakCap = MaxHeight - PeakBlend - 40f;

    /// <summary>The tallest wall peak (x, z, radius): its top is <see cref="MaxHeight"/>, north of the map.</summary>
    private const float TopX = 150f, TopZ = -1720f, TopR = 560f;

    /// <summary>
    /// Snowy wall mountains, x, z, radius, top height (m) each: rows around the square, one along the
    /// crest and more behind it to the horizon, each one's footprint wholly beyond the edge; each rises
    /// from the wall by its top less <see cref="WallHeight"/> and <see cref="WallCrags"/>, every top rounded off under <see cref="PeakCap"/>
    /// (only the tallest peak, apart, reaches <see cref="MaxHeight"/>). Fixed: built from a hash, not a random generator.
    /// </summary>
    private static readonly float[] Peaks = BuildPeaks();

    private static float[] BuildPeaks()
    {
        // Rows: distance of the tops beyond the edge, spacing along the row, radius, lowest and highest top (m).
        ReadOnlySpan<float> rows = stackalloc float[]
        {
            430f, 300f, 340f, 2000f, 2500f,
            850f, 480f, 600f, 2100f, 2600f,
            1600f, 750f, 900f, 2000f, 2600f,
            2800f, 1100f, 1300f, 2000f, 2550f,
        };
        var peaks = new System.Collections.Generic.List<float>();
        for (int row = 0; row < rows.Length; row += 5)
        {
            float beyond = rows[row], spacing = rows[row + 1], half = WorldBounds.HalfSize + beyond;
            int count = (int)MathF.Round(2f * half / spacing);
            for (int side = 0; side < 4; side++)
                for (int i = 0; i < count; i++)
                {
                    float t = -half + 2f * half * (i + 0.5f + 0.5f * (Hash(i, side, 90 + row) - 0.5f)) / count;
                    float out1 = beyond * (0.95f + 0.2f * Hash(i, side, 91 + row));
                    float radius = MathF.Min(rows[row + 2] * (0.85f + 0.3f * Hash(i, side, 92 + row)), out1 / 1.25f - 5f);
                    float top = rows[row + 3] + (rows[row + 4] - rows[row + 3]) * Hash(i, side, 93 + row);
                    float s = WorldBounds.HalfSize + out1;
                    (float x, float z) = side switch { 0 => (t, -s), 1 => (s, t), 2 => (-t, s), _ => (-s, -t) };
                    peaks.Add(x);
                    peaks.Add(z);
                    peaks.Add(radius);
                    peaks.Add(top);
                }
        }
        return peaks.ToArray();
    }

    /// <summary>
    /// Height (m) of the ground, or of the water surface on the river and lake, at
    /// (<paramref name="x"/>, <paramref name="z"/>): always within <see cref="WaterLevel"/>..<see cref="MaxHeight"/>,
    /// and at most <see cref="InnerMaxHeight"/> inside the playable square. Defined everywhere: beyond the
    /// square's edge it is the wall mountains (flying into them is a crash, like any ground).
    /// </summary>
    public static float HeightAt(float x, float z) => MathF.Max(BedAt(x, z), WaterLevel);

    /// <summary>Depth (m) of water at (<paramref name="x"/>, <paramref name="z"/>): 0 on dry land.</summary>
    public static float WaterDepthAt(float x, float z) => MathF.Max(WaterLevel - BedAt(x, z), 0f);

    /// <summary>Whether (<paramref name="x"/>, <paramref name="z"/>) is on the river or the lake.</summary>
    public static bool IsWater(float x, float z) => BedAt(x, z) < WaterLevel;

    /// <summary>Center line of the river (its x) at <paramref name="z"/>.</summary>
    public static float RiverX(float z) =>
        130f * MathF.Sin(z * 0.0022f + 0.6f) + 60f * MathF.Sin(z * 0.0058f + 2.1f) + 20f * MathF.Sin(z * 0.0142f);

    private static float RiverSlope(float z) =>
        0.286f * MathF.Cos(z * 0.0022f + 0.6f) + 0.348f * MathF.Cos(z * 0.0058f + 2.1f) + 0.284f * MathF.Cos(z * 0.0142f);

    /// <summary>Distance (m) from the river's center line.</summary>
    public static float RiverDistance(float x, float z)
    {
        float s = RiverSlope(z);
        return MathF.Abs(x - RiverX(z)) / MathF.Sqrt(1f + s * s);
    }

    /// <summary>Distance (m) beyond the playable square's edge, the far way (on X or Z): 0 or less inside.</summary>
    public static float BeyondEdge(float x, float z) => MathF.Max(MathF.Abs(x), MathF.Abs(z)) - WorldBounds.HalfSize;

    /// <summary>Ground under any water: the inner land carved along the river and in the lake, raised into the wall beyond the edge.</summary>
    private static float BedAt(float x, float z)
    {
        float inner = InnerBedAt(x, z);
        float beyond = BeyondEdge(x, z);
        if (beyond <= 0f)
            return inner;
        // The wall: its foot wobbles outward (never inward), then a steep rise to the crest.
        float d = beyond - WallFootWobble * Noise(x / 450f, z / 450f, 51);
        d = MathF.Max(d, 0f);
        // Ribs and gullies: the rise is shorter in some places than in others (never longer than WallRise).
        float rise = SmoothStep(0f, WallRise * (0.7f + 0.3f * Noise(x / 210f, z / 210f, 52)), d);
        float c1 = 2f * Noise(x / 230f, z / 230f, 53) - 1f, c2 = 2f * Noise(x / 95f, z / 95f, 57) - 1f;
        float crags = WallCrags * (0.5f * Noise(x / 600f, z / 600f, 59) + 0.3f * (1f - c1 * c1) + 0.2f * (1f - c2 * c2));
        float f1 = 2f * Noise(x / 700f, z / 700f, 61) - 1f;
        float far = WallFar * (0.6f * (1f - f1 * f1) + 0.4f * Noise(x / 330f, z / 330f, 67)) * SmoothStep(WallRise, WallRise + WallFarFade, d);
        // Ribs: buttresses and gullies bulging out of the middle of the face.
        float r1 = 2f * Noise(x / 140f, z / 140f, 63) - 1f;
        float ribs = WallRibs * (1f - 2f * r1 * r1) * 4f * rise * (1f - rise);
        float wall = inner * (1f - rise) + rise * (WallHeight + crags) + ribs + far;
        // Mountains on and behind the crest: the tallest bump at each point, its top rounded off under PeakCap.
        float bump = 0f;
        for (int i = 0; i < Peaks.Length; i += 4)
            bump = MathF.Max(bump, (Peaks[i + 3] - WallHeight - WallCrags) * Bump(x, z, Peaks[i], Peaks[i + 1], Peaks[i + 2], 71 + i));
        wall = -SmoothMax(-(wall + bump), -PeakCap, 120f);
        // The tallest peak stands out above them all.
        float mask = 0f, top = 0f;
        Mountain(x, z, TopX, TopZ, TopR, MaxHeight, 1.4f, 97, ref mask, ref top);
        if (mask > 0f)
            wall = SmoothMax(wall, top, PeakBlend * mask);
        return wall;
    }

    /// <summary>The playable land: plain, hills and mountains, carved down to the bed along the river and in the lake.</summary>
    private static float InnerBedAt(float x, float z)
    {
        float river = RiverDistance(x, z) - RiverHalfWidth;
        float lake = LakeDistance(x, z, L0X, L0Z, L0A, L0B, 11);
        float shore = MathF.Min(river, lake);
        float land = LandAt(x, z, shore);
        float wet = 1f - SmoothStep(-ShoreWidth * 0.5f, ShoreWidth * 0.5f, shore);
        return land + (BedHeight - land) * wet;
    }

    /// <summary>Signed distance (m, about) from a lake's wobbly shore: negative inside.</summary>
    private static float LakeDistance(float x, float z, float cx, float cz, float a, float b, int seed)
    {
        float dx = (x - cx) / a, dz = (z - cz) / b;
        float wobble = 0.22f * (Noise(x / 80f, z / 80f, seed) - 0.5f);
        return (MathF.Sqrt(dx * dx + dz * dz) - 1f - wobble) * MathF.Min(a, b);
    }

    /// <summary>Dry land: plain, meadow swells, rolling hills (kept off the open areas and valleys), mountains.</summary>
    private static float LandAt(float x, float z, float shore)
    {
        float open = MathF.Min(Length(x, z), MathF.Min(Length(x, z - StartDistance), Length(x, z + StartDistance)));
        float keep = SmoothStep(OpenRadius, OpenFade, open) * SmoothStep(25f, 120f, shore);
        float rolling = 0.65f * Noise(x / 310f, z / 310f, 3) + 0.35f * Noise(x / 115f, z / 115f, 5);
        float hills = HillHeight * SmoothStep(0.3f, 0.85f, rolling) * keep;
        float meadow = MeadowHeight * Noise(x / 80f, z / 80f, 7);

        float mask = 0f, mountains = 0f;
        Mountain(x, z, M0X, M0Z, M0R, M0H, 0.75f, 31, ref mask, ref mountains);
        Mountain(x, z, M1X, M1Z, M1R, M1H, 0.75f, 37, ref mask, ref mountains);
        return PlainHeight + (meadow + hills) * (1f - mask) + mountains;
    }

    /// <summary>
    /// A wall mountain's shape, 0..1: 1 at its top (<paramref name="cx"/>, <paramref name="cz"/>), broad S-shaped flanks
    /// down to 0 from about <paramref name="radius"/> out (never beyond 1.25 x it), outline warped, slopes creased by
    /// soft ridged noise.
    /// </summary>
    private static float Bump(float x, float z, float cx, float cz, float radius, int seed)
    {
        float dx = x - cx, dz = z - cz;
        float r2 = dx * dx + dz * dz, reach = 1.25f * radius;
        if (r2 >= reach * reach)
            return 0f;
        float s = MathF.Sqrt(r2) / radius * (1f + 0.4f * (Noise(x / 300f, z / 300f, seed) - 0.5f));
        if (s >= 1f)
            return 0f;
        float f = (1f - s) * (1f - s) * (1f + 2f * s);
        float n1 = 2f * Noise(x / 220f, z / 220f, seed + 1) - 1f;
        float n2 = 2f * Noise(x / 120f, z / 120f, seed + 2) - 1f;
        float ridged = 0.65f * (1f - 2f * n1 * n1) + 0.35f * (1f - 2f * n2 * n2);
        return f + Ridges * ridged * f * (1f - f);
    }

    /// <summary>
    /// A mountain: a peak with a rounded top and concave slopes (1 at its top, 0 from about <paramref name="radius"/> out,
    /// never beyond 1.25 x it) with its outline warped and its slopes roughened by ridged noise (its features
    /// <paramref name="scale"/> times the base size). The roughening vanishes at the top and stays monotone in the
    /// dome, so the top (height <paramref name="height"/>) is its highest point.
    /// </summary>
    private static void Mountain(float x, float z, float cx, float cz, float radius, float height, float scale, int seed, ref float mask, ref float sum)
    {
        float dx = x - cx, dz = z - cz;
        float r2 = dx * dx + dz * dz, reach = 1.25f * radius;
        if (r2 >= reach * reach)
            return;
        float s = MathF.Sqrt(r2) / radius;
        s *= 1f + 0.4f * (Noise(x / (260f * scale), z / (260f * scale), seed) - 0.5f);
        if (s >= 1f)
            return;
        float t = 1f - s;
        float f = t * t * t * (1f + 3f * s); // smooth top, concave slopes
        float n1 = 2f * Noise(x / (190f * scale), z / (190f * scale), seed + 1) - 1f;
        float n2 = 2f * Noise(x / (105f * scale), z / (105f * scale), seed + 2) - 1f;
        // Soft creases (smooth, so the mesh can follow them), in -1..1.
        float ridged = 0.65f * (1f - 2f * n1 * n1) + 0.35f * (1f - 2f * n2 * n2);
        float g = f + Ridges * ridged * f * (1f - f);
        mask = MathF.Max(mask, f);
        sum += height * g;
    }

    /// <summary>The larger of <paramref name="a"/> and <paramref name="b"/>, rounded where they are within <paramref name="k"/>
    /// of each other (by at most k / 4); exactly the larger elsewhere.</summary>
    private static float SmoothMax(float a, float b, float k)
    {
        if (MathF.Abs(a - b) >= k)
            return MathF.Max(a, b);
        float h = 0.5f + 0.5f * (b - a) / k;
        return a + (b - a) * h + k * h * (1f - h);
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
