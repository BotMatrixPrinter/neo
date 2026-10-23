using System;
using Godot;
using Neo.Shell;

namespace Neo.World;

/// <summary>
/// Godot side of the World module: builds the countryside you fly over (rolling ground,
/// river, tree clumps) once in <see cref="_Ready"/>. Draws only; no rules here. Three
/// meshes, each drawn with a toon pass and an ink-outline pass.
/// </summary>
public partial class WorldView : Node3D, IModuleView
{
    private const float HalfSize = WorldBounds.HalfSize;
    private const int GridCells = 200;
    private const float RiverHalfWidth = 22f;
    private const ulong Seed = 1917;

    // Hills: x, z, radius, height (m). Spaced so no two add up; tops stay below MaxHillHeight.
    private static readonly Vector4[] Hills =
    {
        new(-950, -650, 330, 12), new(650, -1150, 420, 13), new(1250, 350, 300, 9),
        new(-1350, 650, 380, 11), new(350, 1250, 260, 8), new(-450, -1550, 300, 10),
        new(500, -300, 220, 7), new(-700, 250, 200, 6), new(1500, -500, 260, 10),
    };

    // Tree clumps placed where the preview camera looks (x, z).
    private static readonly Vector2[] ViewClumps = { new(-230, 300), new(120, 60), new(-520, -60), new(430, -260) };

    /// <summary>Its Core, once the shell binds it (none when the scene runs alone).</summary>
    public WorldModule? Module { get; private set; }

    public void Bind(IModule module) => Module = (WorldModule)module;

    public override void _Ready()
    {
        GetNode<MeshInstance3D>("Ground").Mesh = BuildGround();
        PlantTrees(GetNode<MultiMeshInstance3D>("RoundTrees"), GetNode<MultiMeshInstance3D>("Poplars"));
        // The preview camera frames the scene when it runs alone (screenshots, the editor);
        // in the game the shell loads it as a child and the player's camera is used.
        if (GetParent() != GetTree().Root)
            GetNode("PreviewCamera").QueueFree();
    }

    /// <summary>Center of the river at <paramref name="z"/>. Ground.gdshader has the same curve.</summary>
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

    /// <summary>Ground height (m): 0 on the plain, low hills, a shallow dip under the river.</summary>
    public static float Height(float x, float z)
    {
        float hills = 0f;
        foreach (var hill in Hills)
        {
            float dx = x - hill.X, dz = z - hill.Y;
            hills += hill.W * MathF.Exp(-(dx * dx + dz * dz) / (hill.Z * hill.Z));
        }
        float river = RiverDistance(x, z);
        hills = MathF.Min(hills, WorldBounds.MaxHillHeight - 1f) * Mathf.SmoothStep(60f, 220f, river);
        return WorldBounds.GroundY + hills - 2.5f * (1f - Mathf.SmoothStep(RiverHalfWidth - 6f, RiverHalfWidth + 14f, river));
    }

    private static ArrayMesh BuildGround()
    {
        int side = GridCells + 1;
        var vertices = new Vector3[side * side];
        var normals = new Vector3[side * side];
        var indices = new int[GridCells * GridCells * 6];
        float step = 2f * HalfSize / GridCells;
        const float e = 2f;
        for (int iz = 0; iz < side; iz++)
            for (int ix = 0; ix < side; ix++)
            {
                float x = -HalfSize + ix * step, z = -HalfSize + iz * step;
                int i = iz * side + ix;
                vertices[i] = new Vector3(x, Height(x, z), z);
                float dhdx = (Height(x + e, z) - Height(x - e, z)) / (2f * e);
                float dhdz = (Height(x, z + e) - Height(x, z - e)) / (2f * e);
                normals[i] = new Vector3(-dhdx, 1f, -dhdz).Normalized();
            }
        int k = 0;
        for (int iz = 0; iz < GridCells; iz++)
            for (int ix = 0; ix < GridCells; ix++)
            {
                int a = iz * side + ix, b = a + 1, c = a + side, d = c + 1;
                indices[k++] = a; indices[k++] = b; indices[k++] = c;
                indices[k++] = b; indices[k++] = d; indices[k++] = c;
            }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private static void PlantTrees(MultiMeshInstance3D roundNode, MultiMeshInstance3D poplarNode)
    {
        var rng = new RandomNumberGenerator { Seed = Seed };
        var round = new System.Collections.Generic.List<(Transform3D, Color)>();
        var poplars = new System.Collections.Generic.List<(Transform3D, Color)>();
        for (int clump = 0; clump < 46 + ViewClumps.Length; clump++)
        {
            // A few fixed clumps where the preview camera looks, then random ones.
            float cx = clump < ViewClumps.Length ? ViewClumps[clump].X : rng.RandfRange(-HalfSize + 150, HalfSize - 150);
            float cz = clump < ViewClumps.Length ? ViewClumps[clump].Y : rng.RandfRange(-HalfSize + 150, HalfSize - 150);
            if (RiverDistance(cx, cz) < 90f)
                continue;
            float radius = rng.RandfRange(25f, 70f);
            int count = rng.RandiRange(10, 32);
            float poplarShare = rng.Randf() < 0.3f ? 0.7f : 0.1f;
            for (int t = 0; t < count; t++)
            {
                float angle = rng.RandfRange(0f, Mathf.Tau);
                float r = radius * MathF.Sqrt(rng.Randf());
                float x = cx + r * MathF.Cos(angle), z = cz + r * MathF.Sin(angle);
                if (RiverDistance(x, z) < RiverHalfWidth + 12f)
                    continue;
                float scale = rng.RandfRange(0.75f, 1.35f);
                var basis = new Basis(Vector3.Up, rng.RandfRange(0f, Mathf.Tau)).Scaled(new Vector3(scale, scale * rng.RandfRange(0.9f, 1.15f), scale));
                var transform = new Transform3D(basis, new Vector3(x, Height(x, z) - 0.2f, z));
                float shade = rng.RandfRange(0.85f, 1.12f);
                var tint = new Color(shade * rng.RandfRange(0.92f, 1.08f), shade, shade * rng.RandfRange(0.85f, 1f));
                (rng.Randf() < poplarShare ? poplars : round).Add((transform, tint));
            }
        }
        roundNode.Multimesh = BuildMultiMesh(BuildRoundTree(), round);
        poplarNode.Multimesh = BuildMultiMesh(BuildPoplar(), poplars);
    }

    private static MultiMesh BuildMultiMesh(Mesh mesh, System.Collections.Generic.List<(Transform3D Transform, Color Tint)> trees)
    {
        var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mesh };
        multi.InstanceCount = trees.Count;
        for (int i = 0; i < trees.Count; i++)
        {
            multi.SetInstanceTransform(i, trees[i].Transform);
            multi.SetInstanceColor(i, trees[i].Tint);
        }
        return multi;
    }

    private static readonly Color Bark = new(0.45f, 0.29f, 0.17f);

    private static Mesh BuildRoundTree()
    {
        var st = Begin();
        AddBlob(st, new Vector3(0, 1.6f, 0), 0.45f, 1.6f, 6, 1, Bark);
        AddBlob(st, new Vector3(0, 5.6f, 0), 3.6f, 3.0f, 7, 4, new Color(0.30f, 0.62f, 0.22f));
        AddBlob(st, new Vector3(0, 8.4f, 0), 2.5f, 2.0f, 6, 3, new Color(0.38f, 0.70f, 0.26f));
        return End(st);
    }

    private static Mesh BuildPoplar()
    {
        var st = Begin();
        AddBlob(st, new Vector3(0, 1.2f, 0), 0.4f, 1.2f, 6, 1, Bark);
        AddBlob(st, new Vector3(0, 7.5f, 0), 1.9f, 6.2f, 6, 5, new Color(0.22f, 0.52f, 0.22f));
        return End(st);
    }

    private static SurfaceTool Begin()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetSmoothGroup(uint.MaxValue); // flat shading: low-poly facets
        return st;
    }

    private static Mesh End(SurfaceTool st)
    {
        st.GenerateNormals();
        return st.Commit();
    }

    /// <summary>
    /// A low-poly ellipsoid (rings = 1: a prism) with flat faces in <paramref name="color"/>,
    /// slightly varied per face. UV.y holds the part's center height for the outline pass.
    /// </summary>
    private static void AddBlob(SurfaceTool st, Vector3 center, float radius, float halfHeight, int segments, int rings, Color color)
    {
        var uv = new Vector2(0, center.Y);
        Vector3 Point(int ring, int seg)
        {
            float angle = Mathf.Tau * seg / segments + (ring % 2) * Mathf.Pi / segments;
            if (rings == 1)
                return center + new Vector3(radius * MathF.Cos(angle), ring == 0 ? -halfHeight : halfHeight, radius * MathF.Sin(angle));
            float phi = Mathf.Pi * ring / rings;
            float r = radius * MathF.Sin(phi);
            return center + new Vector3(r * MathF.Cos(angle), -halfHeight * MathF.Cos(phi), r * MathF.Sin(angle));
        }
        int face = 0;
        void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            float v = 0.93f + 0.14f * ((face++ * 7919) % 13) / 12f;
            st.SetColor(new Color(color.R * v, color.G * v, color.B * v));
            st.SetUV(uv);
            st.AddVertex(a);
            st.SetUV(uv);
            st.AddVertex(b);
            st.SetUV(uv);
            st.AddVertex(c);
        }
        for (int ring = 0; ring < rings; ring++)
            for (int seg = 0; seg < segments; seg++)
            {
                Vector3 a = Point(ring, seg), b = Point(ring, seg + 1), c = Point(ring + 1, seg), d = Point(ring + 1, seg + 1);
                if (rings == 1 || ring > 0)
                    Tri(a, b, c);
                if (rings == 1 || ring < rings - 1)
                    Tri(b, d, c);
            }
        if (rings == 1)
            for (int seg = 0; seg < segments; seg++)
                Tri(Point(1, seg), Point(1, seg + 1), center + new Vector3(0, halfHeight, 0));
    }
}
