using System;
using Godot;
using Neo.Shell;

namespace Neo.World;

/// <summary>
/// Godot side of the World module: builds the countryside you fly over once in
/// <see cref="_Ready"/>: the ground mesh of the playable square from <see cref="Terrain.HeightAt"/>
/// (hills, mountains, the river and lake, water painted by Ground.gdshader), the range's ring mesh
/// around it (<see cref="WallMesh"/>: foothills, the main ridge and the rows of peaks behind, out to
/// the haze), the tree clumps and the pine forests on the foothills. Draws only; no rules here. Five
/// meshes, each drawn with a toon pass and an ink-outline pass.
/// </summary>
public partial class WorldView : Node3D, IModuleView
{
    private const float HalfSize = WorldBounds.HalfSize;
    private const ulong Seed = 1917;
    /// <summary>Trees grow below this height (m): none on the mountain tops.</summary>
    private const float TreeLine = 110f;
    /// <summary>Pines grow on the range below this height (m), thinning out toward it.</summary>
    private const float PineLine = 950f;
    /// <summary>Pine clumps tried on the range (fewer grow: high, steep or wet spots are skipped).</summary>
    private const int PineClumps = 1400;

    // Tree clumps placed where the preview camera looks (x, z).
    private static readonly Vector2[] ViewClumps = { new(-75, 550), new(210, 475), new(-175, 350), new(-10, 300), new(-280, 625) };

    /// <summary>Its Core, once the shell binds it (none when the scene runs alone).</summary>
    public WorldModule? Module { get; private set; }

    public void Bind(IModule module) => Module = (WorldModule)module;

    public override void _Ready()
    {
        GetNode<MeshInstance3D>("Ground").Mesh = BuildGround();
        GetNode<MeshInstance3D>("Wall").Mesh = BuildWall();
        PlantTrees(GetNode<MultiMeshInstance3D>("RoundTrees"), GetNode<MultiMeshInstance3D>("Poplars"));
        GetNode<MultiMeshInstance3D>("Pines").Multimesh = BuildMultiMesh(BuildPine(), PlantPines());
        // The preview camera frames the scene when it runs alone (screenshots, the editor);
        // in the game the shell loads it as a child and the player's camera is used.
        if (GetParent() != GetTree().Root)
            GetNode("PreviewCamera").QueueFree();
    }

    private static ArrayMesh BuildGround()
    {
        const int cells = Terrain.GridCells;
        int side = cells + 1;
        var vertices = new Vector3[side * side];
        var normals = new Vector3[side * side];
        var water = new Vector2[side * side];
        var indices = new int[cells * cells * 6];
        const float step = Terrain.GridStep;
        for (int iz = 0; iz < side; iz++)
            for (int ix = 0; ix < side; ix++)
            {
                // UV.x: height above the water level on land, minus the water's depth on water
                // (Ground.gdshader paints water below 0, foam near 0).
                int i = iz * side + ix;
                GroundVertex(-HalfSize + ix * step, -HalfSize + iz * step, out vertices[i], out normals[i], out water[i]);
            }
        int k = 0;
        for (int iz = 0; iz < cells; iz++)
            for (int ix = 0; ix < cells; ix++)
            {
                int a = iz * side + ix, b = a + 1, c = a + side, d = c + 1;
                indices[k++] = a; indices[k++] = b; indices[k++] = c;
                indices[k++] = b; indices[k++] = d; indices[k++] = c;
            }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = water;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    /// <summary>A vertex of the ground or the wall at (x, z): on <see cref="Terrain.HeightAt"/>, its normal, its UV (see BuildGround).</summary>
    private static void GroundVertex(float x, float z, out Vector3 vertex, out Vector3 normal, out Vector2 uv)
    {
        const float e = 2f;
        float h = Terrain.HeightAt(x, z);
        vertex = new Vector3(x, h, z);
        float dhdx = (Terrain.HeightAt(x + e, z) - Terrain.HeightAt(x - e, z)) / (2f * e);
        float dhdz = (Terrain.HeightAt(x, z + e) - Terrain.HeightAt(x, z - e)) / (2f * e);
        normal = new Vector3(-dhdx, 1f, -dhdz).Normalized();
        float depth = Terrain.WaterDepthAt(x, z);
        uv = new Vector2(depth > 0f ? -depth : h - Terrain.WaterLevel, 0f);
    }

    /// <summary>The range's ring: <see cref="WallMesh.Rings"/> square rings of <see cref="WallMesh.Segments"/> vertices around the ground mesh.</summary>
    private static ArrayMesh BuildWall()
    {
        const int segments = WallMesh.Segments;
        int rings = WallMesh.Rings;
        var vertices = new Vector3[rings * segments];
        var normals = new Vector3[rings * segments];
        var uvs = new Vector2[rings * segments];
        var indices = new int[(rings - 1) * segments * 6];
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < segments; s++)
            {
                WallMesh.Point(r, s, out float x, out float z);
                int i = r * segments + s;
                GroundVertex(x, z, out vertices[i], out normals[i], out uvs[i]);
            }
        int k = 0;
        for (int r = 0; r < rings - 1; r++)
            for (int s = 0; s < segments; s++)
            {
                int a = r * segments + s, b = r * segments + (s + 1) % segments, c = a + segments, d = b + segments;
                indices[k++] = a; indices[k++] = c; indices[k++] = b;
                indices[k++] = b; indices[k++] = c; indices[k++] = d;
            }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
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
        for (int clump = 0; clump < 70 + ViewClumps.Length; clump++)
        {
            // A few fixed clumps where the preview camera looks, then random ones.
            float cx = clump < ViewClumps.Length ? ViewClumps[clump].X : rng.RandfRange(-HalfSize + 80, HalfSize - 80);
            float cz = clump < ViewClumps.Length ? ViewClumps[clump].Y : rng.RandfRange(-HalfSize + 80, HalfSize - 80);
            if (!CanGrow(cx, cz))
                continue;
            float radius = rng.RandfRange(25f, 70f);
            int count = rng.RandiRange(10, 32);
            float poplarShare = rng.Randf() < 0.3f ? 0.7f : 0.1f;
            for (int t = 0; t < count; t++)
            {
                float angle = rng.RandfRange(0f, Mathf.Tau);
                float r = radius * MathF.Sqrt(rng.Randf());
                float x = cx + r * MathF.Cos(angle), z = cz + r * MathF.Sin(angle);
                if (!CanGrow(x, z))
                    continue;
                float scale = rng.RandfRange(0.75f, 1.35f);
                var basis = new Basis(Vector3.Up, rng.RandfRange(0f, Mathf.Tau)).Scaled(new Vector3(scale, scale * rng.RandfRange(0.9f, 1.15f), scale));
                var transform = new Transform3D(basis, new Vector3(x, Terrain.HeightAt(x, z) - 0.2f, z));
                float shade = rng.RandfRange(0.85f, 1.12f);
                var tint = new Color(shade * rng.RandfRange(0.92f, 1.08f), shade, shade * rng.RandfRange(0.85f, 1f));
                (rng.Randf() < poplarShare ? poplars : round).Add((transform, tint));
            }
        }
        roundNode.Multimesh = BuildMultiMesh(BuildRoundTree(), round);
        poplarNode.Multimesh = BuildMultiMesh(BuildPoplar(), poplars);
    }

    /// <summary>Trees grow on dry land a little away from any shore, below the tree line, on gentle slopes.</summary>
    private static bool CanGrow(float x, float z)
    {
        float h = Terrain.HeightAt(x, z);
        if (h > TreeLine || h < Terrain.PlainHeight - 0.5f)
            return false;
        const float r = 10f;
        if (Terrain.IsWater(x + r, z) || Terrain.IsWater(x - r, z) || Terrain.IsWater(x, z + r) || Terrain.IsWater(x, z - r))
            return false;
        float slope = MathF.Abs(Terrain.HeightAt(x + r, z) - Terrain.HeightAt(x - r, z)) + MathF.Abs(Terrain.HeightAt(x, z + r) - Terrain.HeightAt(x, z - r));
        return slope < 0.9f * 2f * r;
    }

    /// <summary>Pine forests on the range's foothills: clumps beyond the edge (seeded, so the layout never changes),
    /// dense low down and thinning out toward <see cref="PineLine"/>, none on water or steep rock.</summary>
    private static System.Collections.Generic.List<(Transform3D, Color)> PlantPines()
    {
        var rng = new RandomNumberGenerator { Seed = Seed + 1 };
        var pines = new System.Collections.Generic.List<(Transform3D, Color)>();
        const float reach = HalfSize + 1700f;
        for (int clump = 0; clump < PineClumps; clump++)
        {
            float cx = rng.RandfRange(-reach, reach), cz = rng.RandfRange(-reach, reach);
            float beyond = Terrain.BeyondEdge(cx, cz);
            if (beyond < 15f || beyond > 1700f)
                continue;
            int count = rng.RandiRange(6, 22);
            float radius = rng.RandfRange(40f, 110f);
            for (int t = 0; t < count; t++)
            {
                float angle = rng.RandfRange(0f, Mathf.Tau);
                float r = radius * MathF.Sqrt(rng.Randf());
                float x = cx + r * MathF.Cos(angle), z = cz + r * MathF.Sin(angle);
                float h = Terrain.HeightAt(x, z);
                // Thinning with height: fewer the higher, none from the pine line up.
                float chance = 1f - h / PineLine;
                if (Terrain.BeyondEdge(x, z) < 10f || rng.Randf() > chance * chance * 1.3f || !CanGrowPine(x, z))
                    continue;
                float scale = rng.RandfRange(0.8f, 1.4f) * (1f - 0.3f * h / PineLine);
                var basis = new Basis(Vector3.Up, rng.RandfRange(0f, Mathf.Tau)).Scaled(new Vector3(scale, scale * rng.RandfRange(0.9f, 1.25f), scale));
                var transform = new Transform3D(basis, new Vector3(x, h - 0.5f, z));
                float shade = rng.RandfRange(0.82f, 1.1f);
                pines.Add((transform, new Color(shade * rng.RandfRange(0.9f, 1.05f), shade, shade * rng.RandfRange(0.9f, 1.05f))));
            }
        }
        return pines;
    }

    /// <summary>Pines grow on dry ground under the pine line, off steep slopes (under about 40 degrees).</summary>
    private static bool CanGrowPine(float x, float z)
    {
        float h = Terrain.HeightAt(x, z);
        if (h > PineLine || h < Terrain.PlainHeight || Terrain.IsWater(x, z))
            return false;
        const float r = 6f;
        float gx = Terrain.HeightAt(x + r, z) - Terrain.HeightAt(x - r, z), gz = Terrain.HeightAt(x, z + r) - Terrain.HeightAt(x, z - r);
        return gx * gx + gz * gz < 0.7f * (2f * r) * (2f * r);
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

    /// <summary>A low-poly pine: a short trunk under three stacked cones of needles, about 17 m tall.</summary>
    private static Mesh BuildPine()
    {
        var st = Begin();
        AddBlob(st, new Vector3(0, 1.5f, 0), 0.5f, 1.5f, 5, 1, Bark);
        AddCone(st, 1.8f, 8.5f, 4.6f, new Color(0.16f, 0.42f, 0.24f));
        AddCone(st, 6.0f, 12.8f, 3.6f, new Color(0.19f, 0.47f, 0.26f));
        AddCone(st, 9.8f, 17f, 2.5f, new Color(0.22f, 0.53f, 0.28f));
        return End(st);
    }

    /// <summary>A six-sided cone (open below) from height <paramref name="bottom"/> (radius <paramref name="radius"/>) up to a point at
    /// <paramref name="top"/>, flat faces in <paramref name="color"/>; UV.y holds its middle height for the outline pass.</summary>
    private static void AddCone(SurfaceTool st, float bottom, float top, float radius, Color color)
    {
        const int segments = 6;
        var uv = new Vector2(0, 0.5f * (bottom + top));
        var apex = new Vector3(0, top, 0);
        for (int seg = 0; seg < segments; seg++)
        {
            float a0 = Mathf.Tau * seg / segments, a1 = Mathf.Tau * (seg + 1) / segments;
            var p0 = new Vector3(radius * MathF.Cos(a0), bottom, radius * MathF.Sin(a0));
            var p1 = new Vector3(radius * MathF.Cos(a1), bottom, radius * MathF.Sin(a1));
            float v = 0.92f + 0.16f * ((seg * 7919) % 13) / 12f;
            st.SetColor(new Color(color.R * v, color.G * v, color.B * v));
            foreach (var p in new[] { p0, p1, apex })
            {
                st.SetUV(uv);
                st.AddVertex(p);
            }
        }
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
