using System;
using Xunit;

namespace Neo.World.Tests;

public class TerrainTests
{
    private const float Half = WorldBounds.HalfSize;

    // Documented in README.md: the three mountains' tops (x, z, height).
    private static readonly (float X, float Z, float Height)[] Peaks =
    {
        (-1450f, -500f, 250f), (1350f, 150f, 207f), (-850f, 950f, 167f),
    };

    // Documented in README.md: the two lakes' centers.
    private static readonly (float X, float Z)[] Lakes = { (-700f, -900f), (850f, 1150f) };

    [Fact]
    public void HeightsStayWithinWaterLevelAndMaxHeightEverywhere()
    {
        float highest = float.MinValue;
        for (float z = -Half - 200f; z <= Half + 200f; z += 7f)
            for (float x = -Half - 200f; x <= Half + 200f; x += 7f)
            {
                float h = Terrain.HeightAt(x, z);
                Assert.True(h >= Terrain.WaterLevel && h <= Terrain.MaxHeight, $"{h} at ({x}, {z})");
                highest = MathF.Max(highest, h);
            }
        Assert.True(highest > Terrain.MaxHeight - 5f, $"highest sample {highest}");
    }

    [Fact]
    public void MaxHeightIsTheTallestTopAndLeavesRoomUnderTheBalloons()
    {
        Assert.Equal(Terrain.MaxHeight, Terrain.HeightAt(Peaks[0].X, Peaks[0].Z));
        Assert.Equal(250f, Terrain.MaxHeight);
        // Balloons float 300..500 m up with a 9 m hit sphere: the terrain stays well below.
        Assert.True(Terrain.MaxHeight + 40f < 300f - 9f);
    }

    [Fact]
    public void MountainsStandWhereDocumentedWithTheirTopsHighest()
    {
        foreach (var (px, pz, height) in Peaks)
        {
            Assert.InRange(Terrain.HeightAt(px, pz), height - 0.01f, height + 0.01f);
            for (float dz = -500f; dz <= 500f; dz += 10f)
                for (float dx = -500f; dx <= 500f; dx += 10f)
                    Assert.True(Terrain.HeightAt(px + dx, pz + dz) <= height + 0.01f);
            // A real mountain: still well up 100 m from the top, down at the foot.
            Assert.True(Terrain.HeightAt(px + 100f, pz) > 0.5f * height);
            Assert.True(Terrain.HeightAt(px, pz + 600f) < 60f);
        }
    }

    [Fact]
    public void AwayFromTheMountainsTheLandIsOpenRollingHills()
    {
        int samples = 0, low = 0;
        float highest = 0f;
        for (float z = -Half; z <= Half; z += 20f)
            for (float x = -Half; x <= Half; x += 20f)
            {
                float h = Terrain.HeightAt(x, z);
                samples++;
                if (h < 50f)
                    low++;
                bool nearPeak = false;
                foreach (var (px, pz, _) in Peaks)
                    nearPeak |= (x - px) * (x - px) + (z - pz) * (z - pz) < 520f * 520f;
                if (!nearPeak)
                {
                    Assert.True(h < 60f, $"{h} at ({x}, {z}) away from the mountains");
                    highest = MathF.Max(highest, h);
                }
            }
        Assert.True(low > 0.85f * samples, $"{low} of {samples} below 50 m");
        Assert.True(highest > 20f, $"hills only {highest} m"); // tens of meters, not flat
    }

    [Theory]
    [InlineData(0f, 0f, 300f)]
    [InlineData(0f, 1500f, 300f)]
    [InlineData(0f, -1500f, 300f)]
    public void CenterAndStartAreasAreOpenLowAndFlat(float cx, float cz, float radius)
    {
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
                Assert.True(slope < 0.25f, $"slope {slope} at ({x}, {z})");
            }
        Assert.True(highest < 8f, $"highest {highest}");
        Assert.True(highest - lowest < 7f, $"{lowest}..{highest}");
    }

    [Fact]
    public void RiverAndLakesAreFlatWaterAtWaterLevel()
    {
        for (float z = -Half; z <= Half; z += 25f)
        {
            float x = Terrain.RiverX(z);
            for (float dx = -12f; dx <= 12f; dx += 4f)
            {
                Assert.True(Terrain.IsWater(x + dx, z), $"river at ({x + dx}, {z})");
                Assert.Equal(Terrain.WaterLevel, Terrain.HeightAt(x + dx, z));
                Assert.True(Terrain.WaterDepthAt(x + dx, z) > 0f);
            }
            Assert.False(Terrain.IsWater(x + 80f, z));
        }
        foreach (var (lx, lz) in Lakes)
        {
            Assert.True(Terrain.WaterDepthAt(lx, lz) > 1f);
            for (float dz = -80f; dz <= 80f; dz += 10f)
                for (float dx = -100f; dx <= 100f; dx += 10f)
                    Assert.Equal(Terrain.WaterLevel, Terrain.HeightAt(lx + dx, lz + dz));
            Assert.False(Terrain.IsWater(lx + 400f, lz));
            Assert.True(Terrain.RiverDistance(lx, lz) > 300f, "a lake, not part of the river");
        }
        // Wherever there is water its surface is flat at WaterLevel; dry land never is below it.
        for (float z = -Half; z <= Half; z += 13f)
            for (float x = -Half; x <= Half; x += 13f)
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
        var first = new float[2000];
        for (int i = 0; i < first.Length; i++)
            first[i] = Terrain.HeightAt(-Half + i * 2.003f, Half - i * 1.7f);
        int differing = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int round = 0; round < 3; round++)
            for (int i = 0; i < first.Length; i++)
            {
                float x = -Half + i * 2.003f, z = Half - i * 1.7f;
                if (Terrain.HeightAt(x, z) != first[i] || Terrain.IsWater(x, z) != Terrain.WaterDepthAt(x, z) > 0f)
                    differing++;
            }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, differing);
        Assert.Equal(0, allocated);
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
        (float U, float V)[] spots = { (0.5f, 0.5f), (0.25f, 0.25f), (0.75f, 0.75f), (0.33f, 0.6f), (0.7f, 0.2f) };
        for (int iz = 0; iz < Terrain.GridCells; iz++)
            for (int ix = 0; ix < Terrain.GridCells; ix++)
            {
                float a = grid[iz * side + ix], b = grid[iz * side + ix + 1];
                float c = grid[(iz + 1) * side + ix], d = grid[(iz + 1) * side + ix + 1];
                foreach (var (u, v) in spots)
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
}
