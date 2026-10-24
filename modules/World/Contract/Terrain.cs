using System;

namespace Neo.World;

/// <summary>
/// The ground you fly over (and crash into), in meters, Y up. Inside the playable square
/// (<see cref="WorldBounds.HalfSize"/>): a mostly open plain at <see cref="PlainHeight"/> with
/// rolling hills (tens of meters), two small mountains, the winding river and a lake. Around it,
/// from the square's edge out, a cartoon mountain range: foothills at the edge climbing over about
/// 2 km to rows of peaks, with a continuous main ridge at least <see cref="WallMinHeight"/> high within
/// <see cref="WallReach"/> beyond the edge, so no plane gets over or around it. Water is a flat surface
/// at <see cref="WaterLevel"/> and counts as ground. The World's meshes are built from <see cref="HeightAt"/>
/// (the ground on a <see cref="GridCells"/> grid, the range's ring mesh around it), so what is drawn matches it.
/// Every function is pure, deterministic and allocation-free.
/// </summary>
public static class Terrain
{
    /// <summary>The highest point (m): the top of the tallest peak of the range (north of the map). Nothing reaches above it.</summary>
    public const float MaxHeight = 2600f;

    /// <summary>The highest point (m) inside the playable square: the top of its tallest mountain.</summary>
    public const float InnerMaxHeight = 200f;

    /// <summary>Least height (m) of the range's main ridge, saddles included: every way out of the playable square
    /// crosses ground at least this high within <see cref="WallReach"/> beyond its edge, well above the 1000-1100 m
    /// where engines lose their climbing power.</summary>
    public const float WallMinHeight = RidgeLow;

    /// <summary>
    /// Distance (m) beyond the playable square's edge within which every way out crosses the main ridge (at least
    /// <see cref="WallMinHeight"/> high). The foothills start at the edge and the range climbs from there; inside
    /// the square it adds nothing.
    /// </summary>
    public const float WallReach = RidgeNear + RidgeSpan;

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

    // The range beyond the edge (distances e from the square, rounded at the corners). The main ridge line wanders
    // RidgeNear..RidgeNear + RidgeSpan out, its crest RidgeLow..RidgeLow + RidgeRise high; the slope in front of it
    // starts gently at the edge, climbs evenly and rounds over at the crest, cut by valleys that narrow and rise
    // toward it; behind it the ground dips by up to BehindDip into high valleys. Rows of peaks stand on all of it.
    private const float RidgeNear = 2150f, RidgeSpan = 330f;
    private const float RidgeLow = 1380f, RidgeRise = 160f;
    private const float BehindDip = 220f;
    /// <summary>Size (m) of the main ridge line's slow bends (in and out) along the range.</summary>
    private const float RidgeWander = 4000f;
    /// <summary>Distance (m) the peaks of the ridge row stand behind the ridge line, their fronts merging into its crest.</summary>
    private const float RidgeBehind = 250f;
    /// <summary>Distance (m) beyond the edge over which the inner land (hills, the river) fades into the range.</summary>
    private const float InnerFade = 500f;
    /// <summary>Share of the slope's height a valley cuts away (at its floor, away from the ridge), and its half width (m) at the edge.</summary>
    private const float ValleyDepth = 0.6f, ValleyWidth = 650f;
    /// <summary>Shares of the way to the ridge over which the slope eases in (from the edge) and rounds over (to the crest).</summary>
    private const float EaseIn = 0.1f, EaseOut = 0.25f;
    /// <summary>Height range (m) over which two neighboring peaks blend (adds at most a quarter of it).</summary>
    private const float PeakBlend = 60f;
    /// <summary>Highest top (m) of every peak but the tallest: more than <see cref="PeakBlend"/> under <see cref="MaxHeight"/>
    /// with the blending, so that one's top stays the highest point.</summary>
    private const float PeakCap = 2440f;

    /// <summary>The tallest peak (x, z, radius): its top is <see cref="MaxHeight"/>, north of the map, behind the main ridge.</summary>
    private const float TopX = 150f, TopZ = -3700f, TopR = 2500f;

    // Peak shapes.
    private const int Pointed = 0, Ridged = 1, Broad = 2;
    private const int PeakFloats = 8;

    /// <summary>Per row of <see cref="Peaks"/>: first index, end index, nearest and farthest reach (m beyond the edge), to skip rows quickly.</summary>
    private static readonly float[] PeakRows = new float[6 * 4];

    /// <summary>
    /// The range's peaks, <see cref="PeakFloats"/> floats each: x, z, radius (across the range), top height (m), shape,
    /// along-range stretch, and the range's direction there (x, z). Rows all around: low foothills near the edge, a row
    /// of middle hills, a row on the main ridge, and three more behind it fading into haze. Every peak's footprint
    /// stays beyond the edge. Fixed: built from a hash, not a random generator.
    /// </summary>
    private static readonly float[] Peaks = BuildPeaks();

    private static float[] BuildPeaks()
    {
        // Rows: distance of the tops beyond the edge (0: on the main ridge), spacing along the row, largest
        // radius, lowest and highest prominence above the slope (m); tops are kept under PeakCap.
        ReadOnlySpan<float> rows = stackalloc float[]
        {
            380f, 380f, 360f, 50f, 140f,
            950f, 600f, 750f, 80f, 190f,
            0f, 800f, 1300f, 150f, 700f,
            3000f, 1000f, 1500f, 450f, 950f,
            4400f, 1400f, 1900f, 400f, 900f,
            5800f, 1900f, 2300f, 300f, 800f,
        };
        var peaks = new System.Collections.Generic.List<float>();
        for (int row = 0; row < rows.Length / 5; row++)
        {
            int at = row * 5, first = peaks.Count;
            float beyond = rows[at] > 0f ? rows[at] : RidgeNear + 0.5f * RidgeSpan;
            float perimeter = 8f * WorldBounds.HalfSize + 2f * MathF.PI * beyond;
            int count = (int)MathF.Round(perimeter / rows[at + 1]);
            float near = float.MaxValue, far = 0f;
            for (int i = 0; i < count; i++)
            {
                float along = (i + 0.5f + 0.6f * (Hash(i, row, 90) - 0.5f)) / count;
                float e = beyond * (rows[at] > 0f ? 0.95f + 0.1f * Hash(i, row, 91) : 1f);
                RingPoint(along, e, out float x, out float z, out float tx, out float tz);
                if (rows[at] == 0f)
                    for (int k = 0; k < 6; k++) // onto (just behind) the ridge line (a contraction: the line wanders slower than e grows)
                    {
                        e = RidgeDistance(x, z) + RidgeBehind;
                        RingPoint(along, e, out x, out z, out tx, out tz);
                    }
                int shape = (int)(3f * Hash(i, row, 94));
                float radius = MathF.Min(rows[at + 2] * (0.85f + 0.15f * Hash(i, row, 92)), (row == 0 ? 0.92f : 0.95f) * e);
                float prominence = rows[at + 3] + (rows[at + 4] - rows[at + 3]) * Hash(i, row, 93);
                float top = MathF.Min(prominence, PeakCap - SlopeAt(x, z, e)); // above the slope
                float stretch = shape == Ridged ? 1.9f : shape == Broad ? 1.4f : 1.1f;
                peaks.Add(x);
                peaks.Add(z);
                peaks.Add(radius);
                peaks.Add(top);
                peaks.Add(shape);
                peaks.Add(stretch);
                peaks.Add(tx);
                peaks.Add(tz);
                // Its footprint (warped by up to 6 %) stays within its longest radius of its top, whichever way it is turned.
                near = MathF.Min(near, e - 1.1f * radius * stretch);
                far = MathF.Max(far, e + 1.1f * radius * stretch);
            }
            PeakRows[row * 4] = first;
            PeakRows[row * 4 + 1] = peaks.Count;
            PeakRows[row * 4 + 2] = near;
            PeakRows[row * 4 + 3] = far;
        }
        return peaks.ToArray();
    }

    /// <summary>
    /// The point <paramref name="along"/> (0..1, clockwise from the north-west corner) of the way around the playable
    /// square at <paramref name="e"/> m from it (straight sides, quarter circles at the corners), and the direction
    /// along that way there (x, z).
    /// </summary>
    private static void RingPoint(float along, float e, out float x, out float z, out float tx, out float tz)
    {
        const float h = WorldBounds.HalfSize, side = 2f * h;
        float arc = 0.5f * MathF.PI * e, quarter = side + arc;
        float p = (along - MathF.Floor(along)) * 4f * quarter;
        int k = Math.Min((int)(p / quarter), 3);
        p -= k * quarter;
        float lx, lz, ltx, ltz; // in the frame of the north side: x east, z north (negative), going east
        if (p < side)
            (lx, lz, ltx, ltz) = (-h + p, -h - e, 1f, 0f);
        else
        {
            float a = (p - side) / MathF.Max(e, 1e-3f);
            (lx, lz, ltx, ltz) = (h + e * MathF.Sin(a), -h - e * MathF.Cos(a), MathF.Cos(a), MathF.Sin(a));
        }
        // Rotate the north side's frame clockwise (seen from above, north up) k quarter turns: north, east, south, west.
        (x, z, tx, tz) = k switch
        {
            0 => (lx, lz, ltx, ltz),
            1 => (-lz, lx, -ltz, ltx),
            2 => (-lx, -lz, -ltx, -ltz),
            _ => (lz, -lx, ltz, -ltx),
        };
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

    /// <summary>Distance (m) from the playable square (0 inside), rounded at the corners.</summary>
    private static float EdgeDistance(float x, float z)
    {
        float dx = MathF.Max(MathF.Abs(x) - WorldBounds.HalfSize, 0f), dz = MathF.Max(MathF.Abs(z) - WorldBounds.HalfSize, 0f);
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>Distance (m) of the main ridge line beyond the edge near (x, z): RidgeNear..RidgeNear + RidgeSpan,
    /// wandering slowly (it changes by well under a meter per meter), so every way out crosses it once.</summary>
    private static float RidgeDistance(float x, float z) => RidgeNear + RidgeSpan * Noise(x / RidgeWander, z / RidgeWander, 81);

    /// <summary>Ground under any water: the inner land carved along the river and in the lake, fading into the range beyond the edge.</summary>
    private static float BedAt(float x, float z)
    {
        float inner = InnerBedAt(x, z);
        float e = EdgeDistance(x, z);
        if (e <= 0f)
            return inner;
        return inner * (1f - SmoothStep(0f, InnerFade, e)) + RangeAt(x, z, e);
    }

    /// <summary>The mountain range at (x, z), <paramref name="e"/> m beyond the edge: the slope (0 at the edge, up to the
    /// main ridge) with the peaks standing on it, the tallest blended in where it stands higher.</summary>
    private static float RangeAt(float x, float z, float e)
    {
        float bump = 0f;
        for (int row = 0; row < PeakRows.Length; row += 4)
        {
            if (e < PeakRows[row + 2] || e > PeakRows[row + 3])
                continue;
            for (int i = (int)PeakRows[row]; i < (int)PeakRows[row + 1]; i += PeakFloats)
            {
                float f = PeakShape(x, z, i);
                if (f > 0f)
                    bump = BlendPeak(bump, Peaks[i + 3] * f);
            }
        }
        float h = SlopeAt(x, z, e) + bump;
        float dx = (x - TopX) / TopR, dz = (z - TopZ) / TopR;
        if (dx * dx + dz * dz < 1.6f)
            h = BlendPeak(h, MaxHeight * Profile(x, z, TopX, TopZ, TopR, 1.1f, 1f, 0f, Pointed, 97));
        return h;
    }

    /// <summary>The higher of two heights (0 or more), rounded where they are close (by under <see cref="PeakBlend"/> / 4,
    /// and less where either is near 0, so a peak's footprint joins without a step).</summary>
    private static float BlendPeak(float a, float b) => SmoothMax(a, b, MathF.Min(PeakBlend, MathF.Min(a, b)));

    /// <summary>The range without its peaks, <paramref name="e"/> m beyond the edge: an even rise from the edge to the
    /// main ridge's crest, eased in and rounded over (cut by valleys that fade out toward it), then high valleys behind it.</summary>
    private static float SlopeAt(float x, float z, float e)
    {
        float v = e / RidgeDistance(x, z);
        // The crest is higher where the ridge stands farther out, so the climb to it is about as steep everywhere.
        float crest = RidgeLow + RidgeRise * Noise(x / RidgeWander, z / RidgeWander, 81);
        if (v >= 1f)
            return crest - BehindDip * SmoothStep(1f, 1.5f, v) * Noise(x / 900f, z / 900f, 85);
        // Steepness ramps up over EaseIn, stays even, ramps down over EaseOut to level at the crest.
        const float even = 1f / (1f - 0.5f * EaseIn - 0.5f * EaseOut);
        float rise = v < EaseIn ? even * v * v / (2f * EaseIn)
            : v < 1f - EaseOut ? even * (v - 0.5f * EaseIn)
            : 1f - even * (1f - v) * (1f - v) / (2f * EaseOut);
        float slope = crest * rise;
        return slope * (1f - ValleyDepth * ValleyAt(x, z, e, v) * (1f - v) * MathF.Sqrt(1f - v));
    }

    /// <summary>How deep (0..1) a valley cuts at (x, z): 1 on a valley's winding center line, 0 from its half width out;
    /// valleys narrow as they climb toward the ridge (<paramref name="v"/>: 0 at the edge, 1 at the ridge line). Two run
    /// out from each side, the river's own valley among them on the north and south.</summary>
    private static float ValleyAt(float x, float z, float e, float v)
    {
        // Side: 0 north, 1 east, 2 south, 3 west; t along it.
        int side;
        float t;
        if (MathF.Abs(z) >= MathF.Abs(x))
            (side, t) = z < 0f ? (0, x) : (2, x);
        else
            (side, t) = x > 0f ? (1, z) : (3, z);
        float width = ValleyWidth * (1f - 0.6f * v), best = 0f;
        for (int k = 0; k < 2; k++)
        {
            float center = k == 0 && (side & 1) == 0
                ? RiverX(side == 0 ? -WorldBounds.HalfSize : WorldBounds.HalfSize)
                : (k == 0 ? -1f : 1f) * (250f + 200f * Hash(side, k, 87));
            center += 150f * MathF.Sin(e / 600f + 6f * Hash(side, k, 88)) * SmoothStep(0f, 400f, e);
            best = MathF.Max(best, 1f - SmoothStep(0f, width, MathF.Abs(t - center)));
        }
        // None at the corners, where the sides meet.
        return best * (1f - SmoothStep(300f, WorldBounds.HalfSize, MathF.Abs(t) - 0.5f * width));
    }

    /// <summary>Peak <paramref name="i"/> of <see cref="Peaks"/>, 0..1 of its top height at (x, z).</summary>
    private static float PeakShape(float x, float z, int i) =>
        Profile(x, z, Peaks[i], Peaks[i + 1], Peaks[i + 2], Peaks[i + 5], Peaks[i + 6], Peaks[i + 7], (int)Peaks[i + 4], 71 + i);

    /// <summary>
    /// A peak's shape, 0..1: 1 at its top (<paramref name="cx"/>, <paramref name="cz"/>), 0 from <paramref name="radius"/> out
    /// across the range (<paramref name="stretch"/> times that along it, direction (<paramref name="tx"/>, <paramref name="tz"/>)),
    /// outline gently warped. Pointed: a sharp (slightly rounded) summit, slopes steepest just under it and easing out
    /// to the foot; ridged: a long crest along the range, rounder; broad: a dome.
    /// </summary>
    private static float Profile(float x, float z, float cx, float cz, float radius, float stretch, float tx, float tz, int shape, int seed)
    {
        float dx = x - cx, dz = z - cz;
        float along = (dx * tx + dz * tz) / stretch, across = dz * tx - dx * tz;
        float r2 = along * along + across * across, reach = 1.25f * radius;
        if (r2 >= reach * reach)
            return 0f;
        float s = MathF.Sqrt(r2) / radius * (1f + 0.12f * (Noise(x / radius, z / radius, seed) - 0.5f));
        if (s >= 1f)
            return 0f;
        float f;
        if (shape == Broad)
            f = (1f - s) * (1f - s) * (1f + 2f * s);
        else
        {
            // Steepest just under the top (its tip rounded over a few percent of the radius), easing out to the foot.
            const float tip = 0.05f;
            float u = (MathF.Sqrt(s * s + tip * tip) - tip) / (MathF.Sqrt(1f + tip * tip) - tip);
            f = shape == Pointed ? MathF.Pow(1f - u, 2.2f) * (1f + 0.8f * u) : (1f - u) * (1f - u) * (1f + u);
        }
        return f;
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
