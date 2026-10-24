using System;
using Xunit;

namespace Neo.World.Tests;

public class TerrainTests
{
    private const float Half = WorldBounds.HalfSize;

    // Documented in README.md: the two inner mountains' tops (x, z, height).
    private static readonly (float X, float Z, float Height)[] Peaks = { (-620f, -120f, 200f), (600f, 200f, 162f) };

    // Documented in README.md: the lake's center, and the range's tallest peak (north).
    private const float LakeX = -330f, LakeZ = -470f;
    private const float TopX = 150f, TopZ = -3700f;

    /// <summary>Points every <paramref name="step"/> m around the square ring <paramref name="beyond"/> m outside the playable square's edge.</summary>
    private static System.Collections.Generic.IEnumerable<(float X, float Z)> Around(float beyond, float step)
    {
        float s = Half + beyond;
        for (float t = -s; t < s; t += step)
        {
            yield return (t, -s);
            yield return (s, t);
            yield return (-t, s);
            yield return (-s, -t);
        }
    }

    /// <summary>Like <see cref="Around"/>, but only straight out from the square's sides (not off its corners).</summary>
    private static System.Collections.Generic.IEnumerable<(float X, float Z)> AroundSides(float beyond, float step)
    {
        foreach (var (x, z) in Around(beyond, step))
            if (MathF.Min(MathF.Abs(x), MathF.Abs(z)) <= Half)
                yield return (x, z);
    }

    [Fact]
    public void HeightsStayWithinWaterLevelAndMaxHeightEverywhere()
    {
        float highest = float.MinValue;
        for (float z = -Half - 7000f; z <= Half + 7000f; z += 13f)
            for (float x = -Half - 7000f; x <= Half + 7000f; x += 13f)
            {
                float h = Terrain.HeightAt(x, z);
                Assert.True(h >= Terrain.WaterLevel && h <= Terrain.MaxHeight, $"{h} at ({x}, {z})");
                highest = MathF.Max(highest, h);
            }
        Assert.True(highest > Terrain.MaxHeight - 30f, $"highest sample {highest}");
        // MaxHeight is the top of the range's tallest peak, and nothing near it is higher.
        Assert.Equal(2600f, Terrain.MaxHeight);
        Assert.Equal(Terrain.MaxHeight, Terrain.HeightAt(TopX, TopZ));
        for (float dz = -700f; dz <= 700f; dz += 3f)
            for (float dx = -700f; dx <= 700f; dx += 3f)
                Assert.True(Terrain.HeightAt(TopX + dx, TopZ + dz) <= Terrain.MaxHeight);
    }

    [Fact]
    public void InsideThePlayableSquareTheGroundStaysBelowInnerMaxHeight()
    {
        Assert.Equal(200f, Terrain.InnerMaxHeight);
        float highest = float.MinValue;
        for (float z = -Half; z <= Half; z += 4f)
            for (float x = -Half; x <= Half; x += 4f)
            {
                float h = Terrain.HeightAt(x, z);
                Assert.True(h <= Terrain.InnerMaxHeight, $"{h} at ({x}, {z})");
                highest = MathF.Max(highest, h);
            }
        Assert.Equal(Terrain.InnerMaxHeight, highest);
        // Balloons float 300..500 m up with a 9 m hit sphere: the playable ground stays well below.
        Assert.True(Terrain.InnerMaxHeight + 40f < 300f - 9f);
    }

    [Fact]
    public void InsideThePlayableSquareTheLandIsUnchanged()
    {
        // The same inner landscape as before the range was rebuilt (job 729's): its sum over a 4 m grid and a few points.
        double sum = 0;
        for (float z = -Half; z <= Half; z += 4f)
            for (float x = -Half; x <= Half; x += 4f)
                sum += Terrain.HeightAt(x, z);
        Assert.Equal(4058018.09, sum, 0.5);
        Assert.Equal(18.301382f, Terrain.HeightAt(Half, Half), 4);
        Assert.Equal(31.789394f, Terrain.HeightAt(-Half, 317f), 4);
    }

    [Fact]
    public void FoothillsStartLowAtTheEdgeAndTheRangeClimbsGradually()
    {
        // Just inside the edge and at it the land is low; the foothills rise from it (tens to a few hundred meters
        // in the first few hundred meters), and the climb to the ridge takes kilometers, not a sheer face.
        foreach (var (x, z) in Around(-1f, 2f))
            Assert.True(Terrain.HeightAt(x, z) < 60f, $"{Terrain.HeightAt(x, z)} just inside at ({x}, {z})");
        foreach (var (x, z) in Around(0f, 2f))
            Assert.True(Terrain.HeightAt(x, z) < 60f, $"{Terrain.HeightAt(x, z)} at the edge ({x}, {z})");
        // (Distances straight out from the sides: off the corners the range is farther away.)
        foreach (var (x, z) in AroundSides(50f, 4f))
            Assert.True(Terrain.HeightAt(x, z) < 100f, $"{Terrain.HeightAt(x, z)} 50 m out at ({x}, {z})");
        foreach (float beyond in new[] { 200f, 300f, 500f })
        {
            float sum = 0f;
            int count = 0;
            foreach (var (x, z) in AroundSides(beyond, 10f))
            {
                float h = Terrain.HeightAt(x, z);
                Assert.True(h < 400f, $"{h} at ({x}, {z}), {beyond} m out: foothills, not mountains");
                sum += h;
                count++;
            }
            Assert.InRange(sum / count, 50f, 400f);
        }
        // Within 1 km of the edge no ground is beyond a plane's ceiling; the ridge is farther out.
        for (float beyond = 0f; beyond <= 1000f; beyond += 50f)
            foreach (var (x, z) in AroundSides(beyond, 10f))
                Assert.True(Terrain.HeightAt(x, z) < 1100f, $"{Terrain.HeightAt(x, z)} at ({x}, {z}), {beyond} m out");
        Assert.Equal(1000f, Terrain.BeyondEdge(Half + 1000f, 0f));
        Assert.Equal(-Half, Terrain.BeyondEdge(0f, 0f));
    }

    [Fact]
    public void AContinuousRidgeClosesTheMapAllAround()
    {
        // Engines fade out by about 1000-1100 m: every way out crosses ground at least WallMinHeight high within
        // WallReach of the edge, saddles included. Rays out from the center in every direction, and straight out
        // from every point of the edge; each meets such ground.
        Assert.InRange(Terrain.WallMinHeight, 1350f, 1600f);
        Assert.InRange(Terrain.WallReach, 1500f, 2500f);
        int rays = 3600;
        for (int i = 0; i < rays; i++)
        {
            float a = i * MathF.Tau / rays, dx = MathF.Cos(a), dz = MathF.Sin(a);
            float start = Half / MathF.Max(MathF.Abs(dx), MathF.Abs(dz));
            Assert.True(Crosses(0f, 0f, dx, dz, start), $"ray from the center at {a} rad gets out low");
        }
        foreach (var (x, z) in Around(0f, 8f))
        {
            float dx = MathF.Abs(x) >= Half ? MathF.Sign(x) : 0f, dz = MathF.Abs(z) >= Half ? MathF.Sign(z) : 0f;
            float length = MathF.Sqrt(dx * dx + dz * dz);
            Assert.True(Crosses(x, z, dx / length, dz / length, 0f), $"straight out from ({x}, {z}) gets out low");
        }
        // Behind the ridge too the ground stays above where engines give out.
        foreach (float beyond in new[] { Terrain.WallReach, 3000f, 4000f, 6000f, 9000f })
            foreach (var (x, z) in Around(beyond, 9f))
                Assert.True(Terrain.HeightAt(x, z) > 1100f, $"{Terrain.HeightAt(x, z)} at ({x}, {z}), {beyond} m out");
    }

    /// <summary>Whether the ray from (x, z) + <paramref name="start"/> x (dx, dz) outward meets ground at least WallMinHeight high
    /// within WallReach of the edge (sampled every 4 m).</summary>
    private static bool Crosses(float x, float z, float dx, float dz, float start)
    {
        for (float t = start; ; t += 4f)
        {
            float px = x + dx * t, pz = z + dz * t;
            if (Terrain.BeyondEdge(px, pz) > Terrain.WallReach)
                return false;
            if (Terrain.HeightAt(px, pz) >= Terrain.WallMinHeight)
                return true;
        }
    }

    [Fact]
    public void NoSlopeIsSteeperThanSixtyDegreesExceptNearSummits()
    {
        // Mountains, not a wall: below the snow (1400 m; the rocky summits are above it) no slope tops 60 degrees,
        // nowhere tops 65, and most of the range is 45 degrees or gentler. Slopes over 2 m, every 11 m out to 3.5 km.
        float tan60 = MathF.Tan(60f * MathF.PI / 180f), tan65 = MathF.Tan(65f * MathF.PI / 180f), tan45 = 1f;
        int samples = 0, gentle = 0;
        float worst = 0f;
        string where = "";
        for (float z = -Half - 3500f; z <= Half + 3500f; z += 11f)
            for (float x = -Half - 3500f; x <= Half + 3500f; x += 11f)
            {
                if (Terrain.BeyondEdge(x, z) <= 0f)
                    continue;
                float h = Terrain.HeightAt(x, z);
                float gx = (Terrain.HeightAt(x + 1f, z) - Terrain.HeightAt(x - 1f, z)) / 2f;
                float gz = (Terrain.HeightAt(x, z + 1f) - Terrain.HeightAt(x, z - 1f)) / 2f;
                float slope = MathF.Sqrt(gx * gx + gz * gz);
                Assert.True(slope < tan65, $"slope {slope} at ({x}, {z}), {h} m");
                if (h < 1400f && slope > worst)
                    (worst, where) = (slope, $"({x}, {z}), {h} m");
                samples++;
                if (slope <= tan45)
                    gentle++;
            }
        Assert.True(worst < tan60, $"slope {MathF.Atan(worst) * 180f / MathF.PI} degrees at {where}");
        Assert.True(gentle > 0.75f * samples, $"{gentle} of {samples} at 45 degrees or gentler");
    }

    [Fact]
    public void PeaksStandInRowsBehindEachOtherWithValleysAndSaddlesBetween()
    {
        // Tops (highest within 150 m around) on a 50 m grid out to 5 km: many, of different heights, in several rows.
        int near = 0, middle = 0, far = 0;
        float lowest = float.MaxValue, highest = 0f;
        for (float z = -Half - 5000f; z <= Half + 5000f; z += 50f)
            for (float x = -Half - 5000f; x <= Half + 5000f; x += 50f)
            {
                float beyond = Terrain.BeyondEdge(x, z), h = Terrain.HeightAt(x, z);
                if (beyond < 1000f || h < 1500f)
                    continue;
                bool top = true;
                for (float dz = -150f; dz <= 150f && top; dz += 50f)
                    for (float dx = -150f; dx <= 150f && top; dx += 50f)
                        top = (dx == 0f && dz == 0f) || Terrain.HeightAt(x + dx, z + dz) < h;
                if (!top)
                    continue;
                lowest = MathF.Min(lowest, h);
                highest = MathF.Max(highest, h);
                if (beyond < 2600f)
                    near++;
                else if (beyond < 3800f)
                    middle++;
                else
                    far++;
            }
        Assert.True(near >= 8 && middle >= 8 && far >= 8, $"tops {near} near, {middle} middle, {far} far");
        Assert.True(lowest < 1900f && highest > 2300f, $"tops {lowest}..{highest} m");
        // Along the range the main ridge is not level: saddles hundreds of meters under the peaks beside them.
        float crestLow = float.MaxValue, crestHigh = 0f;
        foreach (var (x, z) in Around(Terrain.WallReach, 25f))
        {
            crestLow = MathF.Min(crestLow, Terrain.HeightAt(x, z));
            crestHigh = MathF.Max(crestHigh, Terrain.HeightAt(x, z));
        }
        Assert.True(crestHigh - crestLow > 500f, $"along the ridge {crestLow}..{crestHigh} m");
        foreach (var (x, z) in Around(5000f, 50f))
            Assert.True(Terrain.HeightAt(x, z) < Terrain.MaxHeight - 100f, "far ranges stay under the tallest top");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ValleysCloseInAndRiseTowardTheRidge(int side)
    {
        // Follow the deepest valley of each side (its lowest ground 600 m out, then the lowest within 250 m of it each
        // 200 m farther): its floor rises steadily, its sides rise 40 m within a few hundred meters on both hands, and it
        // narrows (the ground 40 m above its floor closer in) toward the ridge.
        float at = 0f, floor = float.MaxValue;
        for (float t = -700f; t <= 700f; t += 5f)
        {
            float h = Terrain.HeightAt(SidePoint(side, t, 600f).X, SidePoint(side, t, 600f).Z);
            if (h < floor)
                (floor, at) = (h, t);
        }
        float lastFloor = -1f, firstWidth = 0f, lastWidth = 0f;
        for (float beyond = 600f; beyond <= 1400f; beyond += 200f)
        {
            float center = at;
            floor = float.MaxValue;
            for (float t = center - 250f; t <= center + 250f; t += 5f)
            {
                var (x, z) = SidePoint(side, t, beyond);
                if (Terrain.HeightAt(x, z) < floor)
                    (floor, at) = (Terrain.HeightAt(x, z), t);
            }
            float width = 0f;
            foreach (float hand in new[] { -1f, 1f })
            {
                float d = 0f;
                while (d < 400f && Terrain.HeightAt(SidePoint(side, at + hand * d, beyond).X, SidePoint(side, at + hand * d, beyond).Z) < floor + 40f)
                    d += 5f;
                Assert.True(d < 400f, $"valley at {at} m, {beyond} m out ({floor} m) open on one hand");
                width += d;
            }
            Assert.True(floor > lastFloor + 50f, $"valley floor {floor} m at {beyond} m out, {lastFloor} m 200 m before");
            lastFloor = floor;
            if (firstWidth == 0f)
                firstWidth = width;
            lastWidth = width;
        }
        Assert.True(lastWidth < 0.85f * firstWidth, $"valley {firstWidth} m wide 600 m out, {lastWidth} m 1400 m out");
    }

    /// <summary>The point <paramref name="t"/> m along side <paramref name="side"/> (north, east, south, west), <paramref name="beyond"/> m out.</summary>
    private static (float X, float Z) SidePoint(int side, float t, float beyond) => side switch
    {
        0 => (t, -Half - beyond),
        1 => (Half + beyond, t),
        2 => (t, Half + beyond),
        _ => (-Half - beyond, t),
    };

    [Fact]
    public void MountainsStandWhereDocumentedWithTheirTopsHighest()
    {
        foreach (var (px, pz, height) in Peaks)
        {
            Assert.InRange(Terrain.HeightAt(px, pz), height - 0.01f, height + 0.01f);
            for (float dz = -350f; dz <= 350f; dz += 5f)
                for (float dx = -350f; dx <= 350f; dx += 5f)
                    Assert.True(Terrain.HeightAt(px + dx, pz + dz) <= height + 0.01f);
            // A real mountain: still well up 60 m from the top, down at the foot.
            Assert.True(Terrain.HeightAt(px + 60f, pz) > 0.5f * height);
            Assert.True(Terrain.HeightAt(px, pz + 380f) < 60f);
        }
    }

    [Fact]
    public void AwayFromTheMountainsTheLandIsOpenRollingHills()
    {
        int samples = 0, low = 0;
        float highest = 0f;
        for (float z = -Half; z <= Half; z += 10f)
            for (float x = -Half; x <= Half; x += 10f)
            {
                float h = Terrain.HeightAt(x, z);
                samples++;
                if (h < 50f)
                    low++;
                bool nearPeak = false;
                foreach (var (px, pz, _) in Peaks)
                    nearPeak |= (x - px) * (x - px) + (z - pz) * (z - pz) < 330f * 330f;
                if (!nearPeak)
                {
                    Assert.True(h < 60f, $"{h} at ({x}, {z}) away from the mountains");
                    highest = MathF.Max(highest, h);
                }
            }
        Assert.True(low > 0.85f * samples, $"{low} of {samples} below 50 m");
        Assert.True(highest > 25f, $"hills only {highest} m"); // tens of meters, not flat
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(-1f)]
    public void EachSidesSpawnHalfIsMostlyLowAndOpen(float side)
    {
        // Allies spawn on the south half (+Z), Central on the north (-Z): 0.3..0.85 of the half size out.
        int samples = 0, low = 0;
        for (float z = 0.3f * Half; z <= 0.85f * Half; z += 10f)
            for (float x = -0.85f * Half; x <= 0.85f * Half; x += 10f)
            {
                samples++;
                if (Terrain.HeightAt(x, side * z) < 60f)
                    low++;
            }
        Assert.True(low > 0.9f * samples, $"{low} of {samples} below 60 m");
    }

    [Theory]
    [InlineData(0f, 0f, 150f)]
    [InlineData(0f, 600f, 150f)]
    [InlineData(0f, -600f, 150f)]
    public void CenterAndStartAreasAreOpenLowAndFlat(float cx, float cz, float radius)
    {
        Assert.Equal(600f, Terrain.StartDistance);
        float lowest = float.MaxValue, highest = float.MinValue;
        for (float dz = -radius; dz <= radius; dz += 5f)
            for (float dx = -radius; dx <= radius; dx += 5f)
            {
                if (dx * dx + dz * dz > radius * radius)
                    continue;
                float x = cx + dx, z = cz + dz;
                float h = Terrain.HeightAt(x, z);
                lowest = MathF.Min(lowest, h);
                highest = MathF.Max(highest, h);
                float slope = MathF.Max(MathF.Abs(Terrain.HeightAt(x + 2f, z) - Terrain.HeightAt(x - 2f, z)),
                    MathF.Abs(Terrain.HeightAt(x, z + 2f) - Terrain.HeightAt(x, z - 2f))) / 4f;
                Assert.True(slope < 0.3f, $"slope {slope} at ({x}, {z})");
            }
        Assert.True(highest < 8f, $"highest {highest}");
        Assert.True(highest - lowest < 7f, $"{lowest}..{highest}");
    }

    [Fact]
    public void RiverAndLakeAreFlatWaterAtWaterLevel()
    {
        for (float z = -Half; z <= Half; z += 20f)
        {
            float x = Terrain.RiverX(z);
            for (float dx = -8f; dx <= 8f; dx += 4f)
            {
                Assert.True(Terrain.IsWater(x + dx, z), $"river at ({x + dx}, {z})");
                Assert.Equal(Terrain.WaterLevel, Terrain.HeightAt(x + dx, z));
                Assert.True(Terrain.WaterDepthAt(x + dx, z) > 0f);
            }
            Assert.False(Terrain.IsWater(x + 60f, z));
        }
        Assert.True(Terrain.WaterDepthAt(LakeX, LakeZ) > 1f);
        for (float dz = -50f; dz <= 50f; dz += 10f)
            for (float dx = -70f; dx <= 70f; dx += 10f)
                Assert.Equal(Terrain.WaterLevel, Terrain.HeightAt(LakeX + dx, LakeZ + dz));
        Assert.False(Terrain.IsWater(LakeX + 250f, LakeZ));
        Assert.True(Terrain.RiverDistance(LakeX, LakeZ) > 150f, "a lake, not part of the river");
        // Wherever there is water its surface is flat at WaterLevel; dry land never is below it.
        for (float z = -Half - 400f; z <= Half + 400f; z += 9f)
            for (float x = -Half - 400f; x <= Half + 400f; x += 9f)
            {
                bool water = Terrain.IsWater(x, z);
                Assert.Equal(water, Terrain.WaterDepthAt(x, z) > 0f);
                if (water)
                    Assert.Equal(Terrain.WaterLevel, Terrain.HeightAt(x, z));
                else
                    Assert.True(Terrain.HeightAt(x, z) >= Terrain.WaterLevel);
            }
    }

    [Fact]
    public void SameAnswerEveryCallWithoutAllocating()
    {
        // From the north-west foothills across the map to the south-east ones.
        var first = new float[3000];
        for (int i = 0; i < first.Length; i++)
            first[i] = Terrain.HeightAt(-1.5f * Half + i * 1.003f, -1.5f * Half + i * 1.01f);
        int differing = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int round = 0; round < 3; round++)
            for (int i = 0; i < first.Length; i++)
            {
                float x = -1.5f * Half + i * 1.003f, z = -1.5f * Half + i * 1.01f;
                if (Terrain.HeightAt(x, z) != first[i] || Terrain.IsWater(x, z) != Terrain.WaterDepthAt(x, z) > 0f)
                    differing++;
            }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, differing);
        Assert.Equal(0, allocated);
        Assert.Equal(194.31f, Terrain.HeightAt(1300f, 40f), 1); // the same terrain on every machine and run
    }

    /// <summary>
    /// The ground mesh (WorldView.BuildGround) puts a vertex at HeightAt every GridStep and
    /// splits each cell along the diagonal from (ix + 1, iz) to (ix, iz + 1); between vertices
    /// the drawn ground differs from HeightAt by under 1 m.
    /// </summary>
    [Fact]
    public void MeshBuiltOnTheGridMatchesHeightAtWithinOneMeter()
    {
        int side = Terrain.GridCells + 1;
        float step = Terrain.GridStep;
        Assert.Equal(2f * Half, Terrain.GridCells * step, 3);
        var grid = new float[side * side];
        for (int iz = 0; iz < side; iz++)
            for (int ix = 0; ix < side; ix++)
                grid[iz * side + ix] = Terrain.HeightAt(-Half + ix * step, -Half + iz * step);
        float worst = 0f;
        string where = "";
        for (int iz = 0; iz < Terrain.GridCells; iz++)
            for (int ix = 0; ix < Terrain.GridCells; ix++)
            {
                float a = grid[iz * side + ix], b = grid[iz * side + ix + 1];
                float c = grid[(iz + 1) * side + ix], d = grid[(iz + 1) * side + ix + 1];
                foreach (var (u, v) in Spots)
                {
                    float drawn = u + v <= 1f
                        ? a + u * (b - a) + v * (c - a)
                        : d + (1f - u) * (c - d) + (1f - v) * (b - d);
                    float exact = Terrain.HeightAt(-Half + (ix + u) * step, -Half + (iz + v) * step);
                    if (MathF.Abs(drawn - exact) > worst)
                        (worst, where) = (MathF.Abs(drawn - exact), $"({-Half + (ix + u) * step}, {-Half + (iz + v) * step}) at {exact} m");
                }
            }
        Assert.True(worst < 1f, $"mesh differs from HeightAt by {worst} m at {where}");
    }

    private static readonly (float U, float V)[] Spots = { (0.5f, 0.5f), (0.25f, 0.25f), (0.75f, 0.75f), (0.33f, 0.6f), (0.7f, 0.2f) };

    /// <summary>
    /// The range's ring mesh (WallMesh): ring 0 is the ground mesh's border, vertex for vertex; between
    /// vertices, out past the main ridge (WallReach), up to 1300 m (above any plane: engines stop climbing
    /// at about 1000 m) the drawn range is within 5 m plus 2.5 % of the height of HeightAt, so a crash into
    /// a slope shows where it looks. Higher up (scenery: crest and peaks) within 8 %.
    /// </summary>
    [Fact]
    public void WallRingMeshJoinsTheGroundAndFollowsHeightAt()
    {
        Assert.Equal(0f, WallMesh.Offset(0));
        Assert.True(WallMesh.Offset(WallMesh.Rings - 1) >= WallMesh.FarReach);
        for (int r = 1; r < WallMesh.Rings; r++)
            Assert.True(WallMesh.Offset(r) > WallMesh.Offset(r - 1));
        for (int s = 0; s < WallMesh.Segments; s++)
        {
            WallMesh.Point(0, s, out float x, out float z);
            float ix = (x + Half) / Terrain.GridStep, iz = (z + Half) / Terrain.GridStep;
            Assert.Equal(MathF.Round(ix), ix, 3);
            Assert.Equal(MathF.Round(iz), iz, 3);
            Assert.Equal(Half, MathF.Max(MathF.Abs(x), MathF.Abs(z)), 3);
        }
        float worst = 0f;
        string where = "";
        for (int r = 0; WallMesh.Offset(r) <= Terrain.WallReach + 200f; r++)
            for (int s = 0; s < WallMesh.Segments; s++)
            {
                int s1 = (s + 1) % WallMesh.Segments;
                WallMesh.Point(r, s, out float ax, out float az);
                WallMesh.Point(r, s1, out float bx, out float bz);
                WallMesh.Point(r + 1, s, out float cx, out float cz);
                WallMesh.Point(r + 1, s1, out float dx, out float dz);
                float a = Terrain.HeightAt(ax, az), b = Terrain.HeightAt(bx, bz);
                float c = Terrain.HeightAt(cx, cz), d = Terrain.HeightAt(dx, dz);
                foreach (var (u, v) in Spots)
                {
                    // u along the ring, v outward; triangles (a, b, c) and (b, d, c).
                    float x, z, drawn;
                    if (u + v <= 1f)
                        (x, z, drawn) = (ax + u * (bx - ax) + v * (cx - ax), az + u * (bz - az) + v * (cz - az), a + u * (b - a) + v * (c - a));
                    else
                        (x, z, drawn) = (dx + (1f - u) * (cx - dx) + (1f - v) * (bx - dx), dz + (1f - u) * (cz - dz) + (1f - v) * (bz - dz),
                            d + (1f - u) * (c - d) + (1f - v) * (b - d));
                    float exact = Terrain.HeightAt(x, z);
                    float share = MathF.Abs(drawn - exact) / (exact < 1300f ? 5f + 0.025f * exact : 0.08f * exact);
                    if (share > worst)
                        (worst, where) = (share, $"({x}, {z}): {drawn} m drawn, {exact} m exact");
                }
            }
        Assert.True(worst < 1f, $"range mesh too far from HeightAt at {where}");
    }
}
