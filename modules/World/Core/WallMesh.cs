using System;

namespace Neo.World;

/// <summary>
/// Layout of the mountain range's ring mesh (WorldView builds it, the tests check it against <see cref="Terrain.HeightAt"/>):
/// <see cref="Rings"/> square rings around the playable square, ring <c>r</c> at <see cref="Offset"/> m beyond its
/// edge, each with <see cref="Segments"/> vertices; ring 0 is the ground mesh's border, so the two meet without a
/// seam. Rings sit every <see cref="NearStep"/> m up to <see cref="NearReach"/> (the foothills), then each a little
/// farther apart (denser near the edge, coarser far away) out to <see cref="FarReach"/> (the rows behind, fading into haze). Quad (r, s) joins vertices
/// s and s + 1 of rings r and r + 1, split along the diagonal from (r, s + 1) to (r + 1, s). Pure, no Godot.
/// </summary>
public static class WallMesh
{
    /// <summary>Vertices per ring: one per ground-mesh border vertex (the last joins the first).</summary>
    public const int Segments = 4 * Terrain.GridCells;

    /// <summary>Distance (m) between rings up to <see cref="NearReach"/>.</summary>
    public const float NearStep = 8f;

    /// <summary>Distance (m) beyond the edge up to which rings sit every <see cref="NearStep"/> m.</summary>
    public const float NearReach = 400f;

    /// <summary>Distance (m) beyond the edge of the outermost ring (at least).</summary>
    public const float FarReach = 6000f;

    private const int NearRings = (int)(NearReach / NearStep);
    private const float Growth = 1.05f;

    /// <summary>Number of rings, ring 0 (at the edge) included.</summary>
    public static readonly int Rings = CountRings();

    private static int CountRings()
    {
        int r = NearRings;
        while (Offset(r) < FarReach)
            r++;
        return r + 1;
    }

    /// <summary>Distance (m) of ring <paramref name="ring"/> beyond the playable square's edge.</summary>
    public static float Offset(int ring)
    {
        if (ring <= NearRings)
            return ring * NearStep;
        // Beyond: each step Growth times the one before.
        int k = ring - NearRings;
        return NearReach + NearStep * Growth * (MathF.Pow(Growth, k) - 1f) / (Growth - 1f);
    }

    /// <summary>World X, Z of vertex <paramref name="segment"/> (0..<see cref="Segments"/> - 1, around the square from
    /// its north-west corner, eastward first) of ring <paramref name="ring"/>.</summary>
    public static void Point(int ring, int segment, out float x, out float z)
    {
        float s = WorldBounds.HalfSize + Offset(ring);
        int side = segment / Terrain.GridCells;
        float t = -s + 2f * s * (segment % Terrain.GridCells) / Terrain.GridCells;
        (x, z) = side switch
        {
            0 => (t, -s),
            1 => (s, t),
            2 => (-t, s),
            _ => (-s, -t),
        };
    }
}
