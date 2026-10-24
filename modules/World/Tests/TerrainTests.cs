using System;
using Xunit;

namespace Neo.World.Tests;

public class TerrainTests
{
    private const float Half = WorldBounds.HalfSize;

    // Documented in README.md: the two inner mountains' tops (x, z, height).
    private static readonly (float X, float Z, float Height)[] Peaks = { (-620f, -120f, 200f), (600f, 200f, 162f) };

    // Documented in README.md: the lake's center, and the tallest wall peak (north).
    private const float LakeX = -330f, LakeZ = -470f;
    private const float TopX = 150f, TopZ = -1720f;

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

    [Fact]
    public void HeightsStayWithinWaterLevelAndMaxHeightEverywhere()
    {
        float highest = float.MinValue;
        for (float z = -Half - 6000f; z <= Half + 6000f; z += 13f)
            for (float x = -Half - 6000f; x <= Half + 6000f; x += 13f)
            {
                float h = Terrain.HeightAt(x, z);
                Assert.True(h >= Terrain.WaterLevel && h <= Terrain.MaxHeight, $"{h} at ({x}, {z})");
                highest = MathF.Max(highest, h);
            }
        Assert.True(highest > Terrain.MaxHeight - 30f, $"highest sample {highest}");
        // MaxHeight is the top of the tallest wall peak, and nothing near it is higher.
        Assert.Equal(2750f, Terrain.MaxHeight);
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
    public void WallMountainsRiseAllAroundJustBeyondTheEdge()
    {
        // Just inside the edge the land is low; from WallReach beyond it (and on out to the
        // horizon) the ground is at least WallMinHeight, far above where engines stop climbing.
        Assert.Equal(1600f, Terrain.WallMinHeight);
        Assert.True(Terrain.WallMinHeight > 1000f + 500f);
        Assert.InRange(Terrain.WallReach, 100f, 400f);
        foreach (var (x, z) in Around(-1f, 2f))
            Assert.True(Terrain.HeightAt(x, z) < 60f, $"{Terrain.HeightAt(x, z)} just inside at ({x}, {z})");
        foreach (var (x, z) in Around(0f, 2f))
            Assert.True(Terrain.HeightAt(x, z) < 60f, $"{Terrain.HeightAt(x, z)} at the edge ({x}, {z})");
        foreach (float beyond in new[] { Terrain.WallReach, Terrain.WallReach + 1f, 400f, 600f, 1000f, 2000f, 4000f, 6000f, 20000f })
            foreach (var (x, z) in Around(beyond, 3f))
                Assert.True(Terrain.HeightAt(x, z) >= Terrain.WallMinHeight, $"{Terrain.HeightAt(x, z)} at ({x}, {z}), {beyond} m beyond the edge");
        // Steep: it climbs from the low edge to the wall's height within WallReach, everywhere.
        for (float beyond = Terrain.WallReach; beyond <= Terrain.WallReach + 200f; beyond += 10f)
            foreach (var (x, z) in Around(beyond, 7f))
                Assert.True(Terrain.HeightAt(x, z) >= Terrain.WallMinHeight);
        Assert.Equal(Terrain.WallReach, Terrain.BeyondEdge(Half + Terrain.WallReach, 0f));
        Assert.Equal(-Half, Terrain.BeyondEdge(0f, 0f));
    }

    [Fact]
    public void TheWallHasSnowyPeaksWellAboveItsCrest()
    {
        // Looking out anywhere along the edge, peaks rise on and behind the crest: on most lines
        // straight out, the ground somewhere 350..900 m beyond the edge tops 2100 m; the crest
        // (WallReach out) is jagged, not a level rim; the ranges far out stay under the tallest top.
        int lines = 0, peaked = 0;
        foreach (var (x, z) in Around(0f, 25f))
        {
            float ox = MathF.Abs(x) >= Half ? MathF.Sign(x) : 0f, oz = MathF.Abs(z) >= Half ? MathF.Sign(z) : 0f;
            float highest = 0f;
            for (float d = 350f; d <= 900f; d += 20f)
                highest = MathF.Max(highest, Terrain.HeightAt(x + ox * d, z + oz * d));
            lines++;
            if (highest > 2100f)
                peaked++;
        }
        Assert.True(peaked > 0.6f * lines, $"peaks behind {peaked} of {lines} lines");
        float crestLow = float.MaxValue, crestHigh = 0f;
        foreach (var (x, z) in Around(Terrain.WallReach, 25f))
        {
            crestLow = MathF.Min(crestLow, Terrain.HeightAt(x, z));
            crestHigh = MathF.Max(crestHigh, Terrain.HeightAt(x, z));
        }
        Assert.True(crestHigh - crestLow > 400f, $"crest {crestLow}..{crestHigh} m");
        foreach (var (x, z) in Around(5000f, 50f))
            Assert.True(Terrain.HeightAt(x, z) < Terrain.MaxHeight - 100f, "far ranges stay under the tallest top");
    }

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
        // From beyond the north-west wall across the map to beyond the south-east one.
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
        Assert.Equal(2004.52f, Terrain.HeightAt(1300f, 40f), 1); // the same terrain on every machine and run
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
    /// The wall ring mesh (WallMesh): ring 0 is the ground mesh's border, vertex for vertex; between
    /// vertices, out to NearReach (the face and the crest), up to 1300 m (above any plane: engines stop
    /// climbing at about 1000 m) the drawn wall is within 5 m plus 2.5 % of the height of HeightAt: on the
    /// steep face a few meters up or down are under a meter sideways, so a crash into it shows where it
    /// looks. Higher up (scenery: crest and peaks) within 8 %.
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
        for (int r = 0; WallMesh.Offset(r + 1) <= WallMesh.NearReach; r++)
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
        Assert.True(worst < 1f, $"wall mesh too far from HeightAt at {where}");
    }
}
