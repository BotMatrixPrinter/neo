using System.Collections.Generic;
using Godot;
using Neo.Damage;

namespace Neo.Plane;

/// <summary>
/// One drawn plane (made by <see cref="PlaneView"/> for each plane id): its model, toon
/// shaded with an ink outline, its propeller, its exhaust trail and its damage looks
/// (<see cref="PlaneLook"/>): dark engine smoke, tattered wings with a little smoke, fire and
/// thick smoke while falling, an explosion puff where it hits the ground, hidden until it
/// respawns. Placed each frame by <see cref="Show"/>; no rules here.
/// </summary>
public partial class PlaneVisual : Node3D
{
    private const float DiscForward = 0.1f; // the disc sits this far in front of the blades
    private const float DiscRadius = 1.42f;
    private const float HoleLift = 0.06f; // holes sit just above the wing's crosses and roundels

    // Where the holes in a damaged wing go, around its marker (X across the span, Z along the chord, -Z forward).
    private static readonly Vector3[] HoleSpots =
    {
        new(0f, 0f, 0f), new(0.65f, 0f, -0.2f), new(-0.55f, 0f, 0.22f),
        new(1.1f, 0f, 0.12f), new(-0.95f, 0f, -0.22f), new(0.3f, 0f, 0.28f),
    };
    private static readonly float[] HoleSizes = { 0.2f, 0.14f, 0.17f, 0.12f, 0.15f, 0.11f };

    private static Shader? _smokeShader;

    private static Shader SmokeShader => _smokeShader ??= GD.Load<Shader>("res://modules/Plane/Godot/Smoke.gdshader");

    private Node3D? _model;
    private Node3D? _propeller;
    private Basis _propellerRest = Basis.Identity;
    private readonly List<ShaderMaterial> _blades = new();
    private Material? _bladeOutline;
    private ShaderMaterial? _disc;
    private MeshInstance3D? _discMesh;
    private float _spin;

    private CpuParticles3D _trail = null!;
    private CpuParticles3D _engineSmoke = null!;
    private CpuParticles3D _fire = null!;
    private readonly CpuParticles3D[] _wingSmoke = new CpuParticles3D[2];
    private readonly Node3D[][] _holes = new Node3D[2][];
    private readonly List<Node3D> _flaps = new();
    private Node3D _explosion = null!;
    private CpuParticles3D _blast = null!;
    private CpuParticles3D _blastSmoke = null!;
    private long _crashesSeen;
    private double _time;

    /// <summary>Builds the plane from <paramref name="model"/> (a fresh copy of its .glb scene, or null if it could not be read).</summary>
    public void Build(Node3D? model, PlaneModel kind, Shader toon, Shader toonFade, Shader outlineShader, Shader discShader, Texture2D puff)
    {
        var outline = new ShaderMaterial { Shader = outlineShader };
        _model = model;
        Transform3D exhaust = Transform3D.Identity, left = new(Basis.Identity, new Vector3(-1.5f, 1.6f, -0.6f)), right = new(Basis.Identity, new Vector3(1.5f, 1.6f, -0.6f));
        if (model != null)
        {
            AddChild(model);
            _propeller = model.FindChild("Propeller", true, false) as Node3D;
            if (_propeller != null)
            {
                _propellerRest = _propeller.Basis;
                _bladeOutline = outline;
                Toon(_propeller, toonFade, outline, _blades);
            }
            else
            {
                GD.PushWarning($"PlaneVisual: {PlaneLook.FileName(kind)} has no Propeller node; no propeller spins");
            }
            Toon(model, toon, outline, null);
            if (_propeller != null)
                _propeller.AddChild(_discMesh = MakeDisc(discShader, out _disc));
            exhaust = Marker(model, "Exhaust", exhaust);
            left = Marker(model, "LeftWing", left);
            right = Marker(model, "RightWing", right);
        }

        // Smoke and fire live in the world (they stay behind the plane) and hang off this node, not the
        // model, so they can trail out after the model is hidden.
        _trail = Particles("Trail", puff, 260, 2.2, new Color(1f, 1f, 1f, 0.06f), new Color(1f, 1f, 1f, 0f), additive: true, size: 2f);
        _engineSmoke = Particles("EngineSmoke", puff, 220, 3.0, new Color(0.13f, 0.12f, 0.12f, 0.75f), new Color(0.3f, 0.3f, 0.3f, 0f), additive: false, size: 2.4f);
        _fire = Particles("Fire", puff, 90, 0.45, new Color(1f, 0.85f, 0.35f, 0.9f), new Color(0.9f, 0.2f, 0.02f, 0f), additive: true, size: 1.8f);
        _fire.InitialVelocityMin = 2f;
        _fire.InitialVelocityMax = 4f;
        foreach (var p in new[] { _trail, _engineSmoke, _fire })
        {
            p.Transform = exhaust;
            AddChild(p);
        }

        var wingColor = kind == PlaneModel.Triplane ? new Color(0.88f, 0.05f, 0.04f) : new Color(0.93f, 0.86f, 0.66f);
        var markers = new[] { left, right };
        for (int side = 0; side < 2; side++)
        {
            _wingSmoke[side] = Particles(side == 0 ? "LeftWingSmoke" : "RightWingSmoke", puff, 120, 1.6,
                new Color(0.45f, 0.43f, 0.42f, 0.3f), new Color(0.6f, 0.6f, 0.6f, 0f), additive: false, size: 1.4f);
            _wingSmoke[side].Transform = markers[side];
            AddChild(_wingSmoke[side]);
            _holes[side] = MakeHoles(markers[side], wingColor, side == 0 ? -1f : 1f);
        }

        _explosion = MakeExplosion(puff, out _blast, out _blastSmoke);
        AddChild(_explosion);
        Visible = false;
    }

    /// <summary>
    /// Places and dresses the plane for this frame: <paramref name="previous"/> to
    /// <paramref name="latest"/> by <paramref name="fraction"/>, with its damage and life.
    /// </summary>
    public void Show(in Neo.Flight.PlaneState previous, in Neo.Flight.PlaneState latest, float fraction,
        in PlaneDamageState damage, PlaneLife life, long crashes, double delta)
    {
        _time += delta;
        var from = new Transform3D(new Basis(ToGodot(previous.Orientation)), ToGodot(previous.Position));
        var to = new Transform3D(new Basis(ToGodot(latest.Orientation)), ToGodot(latest.Position));
        Transform = from.InterpolateWith(to, fraction);
        Visible = true;

        bool crashed = life == PlaneLife.Crashed;
        bool falling = life == PlaneLife.Falling;
        if (_model != null)
            _model.Visible = !crashed;
        if (crashes != _crashesSeen)
        {
            _crashesSeen = crashes;
            // A cartoon puff where it hit: fire and smoke, left in the world.
            _explosion.GlobalTransform = new Transform3D(Basis.Identity, GlobalPosition + new Vector3(0f, 1.5f, 0f));
            _blast.Restart();
            _blastSmoke.Restart();
        }

        SetEmitting(_trail, life == PlaneLife.Flying);
        Smoke(_engineSmoke, crashed ? 0f : falling ? 1f : PlaneLook.EngineSmoke(damage.Engine));
        SetEmitting(_fire, falling);
        for (int side = 0; side < 2; side++)
        {
            float wing = side == 0 ? damage.LeftWing : damage.RightWing;
            Smoke(_wingSmoke[side], crashed ? 0f : PlaneLook.WingSmoke(wing));
            int holes = PlaneLook.Holes(wing);
            for (int i = 0; i < _holes[side].Length; i++)
                _holes[side][i].Visible = i < holes;
        }
        for (int i = 0; i < _flaps.Count; i++)
            _flaps[i].Rotation = new Vector3(-0.5f - 0.45f * Mathf.Sin((float)_time * 23f + i * 1.7f), 0f, 0f);

        Propeller(Mathf.Clamp(Mathf.Lerp(previous.Throttle, latest.Throttle, fraction), 0f, 1f), delta);
    }

    private void Propeller(float throttle, double delta)
    {
        _spin = Mathf.Wrap(_spin + Mathf.Tau * PropellerLook.BladeTurns(throttle, delta), 0f, Mathf.Tau);
        if (_propeller == null)
            return;
        var axis = PropellerLook.SpinAxis;
        _propeller.Basis = _propellerRest * new Basis(new Vector3(axis.X, axis.Y, axis.Z), _spin);
        float bladeAlpha = PropellerLook.BladeAlpha(throttle);
        foreach (var blade in _blades)
        {
            blade.SetShaderParameter("alpha", bladeAlpha);
            blade.NextPass = bladeAlpha >= 0.999f ? _bladeOutline : null; // the ink outline only on solid blades
        }
        float discAlpha = PropellerLook.DiscAlpha(throttle);
        _disc?.SetShaderParameter("fade", discAlpha);
        if (_discMesh != null)
            _discMesh.Visible = discAlpha > 0.001f;
    }

    private static void SetEmitting(CpuParticles3D particles, bool on)
    {
        if (particles.Emitting != on)
            particles.Emitting = on;
    }

    // Smoke of a given thickness (0 = none, 1 = thickest): denser and bigger puffs when worse.
    private static void Smoke(CpuParticles3D particles, float amount)
    {
        SetEmitting(particles, amount > 0.001f);
        if (amount <= 0.001f)
            return;
        particles.Color = new Color(1f, 1f, 1f, Mathf.Clamp(amount, 0f, 1f));
        particles.ScaleAmountMin = 0.5f + 0.7f * amount;
        particles.ScaleAmountMax = 0.8f + 1.0f * amount;
    }

    // A marker's transform in the model's space (the .glb's empties), or the fallback if it has none.
    private static Transform3D Marker(Node3D model, string name, Transform3D fallback)
    {
        if (model.FindChild(name, true, false) is not Node3D marker)
        {
            GD.PushWarning($"PlaneVisual: model has no {name} node");
            return fallback;
        }
        var transform = marker.Transform;
        for (var parent = marker.GetParent() as Node3D; parent != null && parent != model; parent = parent.GetParent() as Node3D)
            transform = parent.Transform * transform;
        return transform;
    }

    // Holes punched in the wing's canvas (dark discs) with torn flaps of canvas fluttering
    // behind every other one. Hung on the model, so they hide with it.
    private Node3D[] MakeHoles(Transform3D marker, Color canvas, float side)
    {
        var holeMaterial = new StandardMaterial3D { AlbedoColor = new Color(0.07f, 0.05f, 0.04f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        var flapMaterial = new StandardMaterial3D { AlbedoColor = canvas.Darkened(0.4f), CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        var root = new Node3D { Name = side < 0 ? "LeftWingDamage" : "RightWingDamage", Transform = marker };
        (_model ?? (Node3D)this).AddChild(root);
        var holes = new Node3D[HoleSpots.Length];
        for (int i = 0; i < holes.Length; i++)
        {
            float r = HoleSizes[i];
            var hole = new MeshInstance3D
            {
                Name = "Hole",
                Mesh = new CylinderMesh { TopRadius = r, BottomRadius = r * 0.85f, Height = 0.02f, RadialSegments = 7, Rings = 1, Material = holeMaterial },
                Position = new Vector3(HoleSpots[i].X * side, HoleLift, HoleSpots[i].Z),
                Rotation = new Vector3(0f, i * 0.9f, 0f), // seven ragged sides, each turned its own way
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false,
            };
            if (i % 2 == 0)
            {
                // A strip of canvas hinged at the hole's back edge, flapping in the wind.
                var hinge = new Node3D { Name = "Flap", Position = new Vector3(0f, 0.01f, r) };
                hinge.AddChild(new MeshInstance3D
                {
                    Mesh = new QuadMesh { Size = new Vector2(r * 1.4f, r * 2.2f), Material = flapMaterial },
                    Transform = new Transform3D(new Basis(Vector3.Right, -Mathf.Pi / 2f), new Vector3(0f, 0f, r * 1.1f)),
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                });
                hole.AddChild(hinge);
                _flaps.Add(hinge);
            }
            root.AddChild(hole);
            holes[i] = hole;
        }
        return holes;
    }

    // The crash puff: a burst of fire swelling out, then a big cloud of dark smoke rising. One shot each, restarted per crash.
    private static Node3D MakeExplosion(Texture2D puff, out CpuParticles3D blast, out CpuParticles3D smoke)
    {
        var root = new Node3D { Name = "Explosion", TopLevel = true };
        blast = Particles("Blast", puff, 48, 0.9, new Color(1f, 0.7f, 0.2f, 0.55f), new Color(0.9f, 0.2f, 0.02f, 0f), additive: true, size: 7f);
        blast.OneShot = true;
        blast.Explosiveness = 1f;
        blast.Spread = 180f;
        blast.Direction = Vector3.Up;
        blast.InitialVelocityMin = 6f;
        blast.InitialVelocityMax = 14f;
        blast.DampingMin = 8f;
        blast.DampingMax = 12f;
        smoke = Particles("BlastSmoke", puff, 40, 3.0, new Color(0.3f, 0.28f, 0.27f, 0.85f), new Color(0.55f, 0.55f, 0.55f, 0f), additive: false, size: 10f);
        smoke.OneShot = true;
        smoke.Explosiveness = 0.85f;
        smoke.Spread = 70f;
        smoke.Direction = Vector3.Up;
        smoke.InitialVelocityMin = 2f;
        smoke.InitialVelocityMax = 6f;
        smoke.Gravity = new Vector3(0f, 1.5f, 0f);
        root.AddChild(blast);
        root.AddChild(smoke);
        return root;
    }

    // Puffs left in the world behind the emitter, swelling and fading from `start` to `end`.
    private static CpuParticles3D Particles(string name, Texture2D puff, int amount, double lifetime, Color start, Color end, bool additive, float size)
    {
        Material material;
        if (additive)
        {
            // Light (white smoke, fire) adds to what is behind: particle slots not yet spawned draw black, and adding black shows nothing.
            material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
                VertexColorUseAsAlbedo = true,
                AlbedoTexture = puff,
            };
        }
        else
        {
            // Dark smoke is blended over: Smoke.gdshader drops the slots not yet spawned.
            var smoke = new ShaderMaterial { Shader = SmokeShader };
            smoke.SetShaderParameter("puff", puff);
            material = smoke;
        }
        var fade = new Gradient();
        fade.SetColor(0, start);
        fade.SetColor(1, end);
        var grow = new Curve();
        grow.AddPoint(new Vector2(0f, 0.35f));
        grow.AddPoint(new Vector2(1f, 1f));
        return new CpuParticles3D
        {
            Name = name,
            Emitting = false,
            Amount = amount,
            Lifetime = lifetime,
            LocalCoords = false,
            Mesh = new QuadMesh { Size = new Vector2(size, size), Material = material },
            Direction = new Vector3(0, 0.3f, 1f),
            Spread = 25f,
            InitialVelocityMin = 0.5f,
            InitialVelocityMax = 1.5f,
            Gravity = new Vector3(0, 0.4f, 0),
            DampingMin = 0.5f,
            DampingMax = 1f,
            ScaleAmountMin = 0.7f,
            ScaleAmountMax = 1.2f,
            ScaleAmountCurve = grow,
            ColorRamp = fade,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    // Every surface gets the cel shader in its own color, with the ink outline as next pass;
    // surfaces that already have a material (the propeller's) are left alone. Collects the
    // materials made into `made` when given.
    private static void Toon(Node node, Shader toon, Material outline, List<ShaderMaterial>? made)
    {
        if (node is MeshInstance3D instance && instance.Mesh != null && instance.GetSurfaceOverrideMaterial(0) == null)
        {
            instance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            for (int s = 0; s < instance.Mesh.GetSurfaceCount(); s++)
            {
                var color = instance.Mesh.SurfaceGetMaterial(s) is BaseMaterial3D imported ? imported.AlbedoColor : Colors.White;
                var material = new ShaderMaterial { Shader = toon, NextPass = outline };
                material.SetShaderParameter("albedo", color);
                instance.SetSurfaceOverrideMaterial(s, material);
                made?.Add(material);
            }
        }
        foreach (var child in node.GetChildren())
            Toon(child, toon, outline, made);
    }

    // The prop disc: a thin round mesh just in front of the blades, facing along the shaft,
    // semi-transparent with soft radial streaks, no outline. Turns with the propeller.
    private static MeshInstance3D MakeDisc(Shader shader, out ShaderMaterial material)
    {
        material = new ShaderMaterial { Shader = shader, RenderPriority = 1 };
        material.SetShaderParameter("radius", DiscRadius);
        return new MeshInstance3D
        {
            Name = "PropDisc",
            Mesh = new CylinderMesh { TopRadius = DiscRadius, BottomRadius = DiscRadius, Height = 0.01f, RadialSegments = 48, Rings = 1, CapBottom = false, Material = material },
            // The cylinder's axis is its Y: turn it onto the shaft (local Z), a little forward (-Z).
            Transform = new Transform3D(new Basis(Vector3.Right, Mathf.Pi / 2f), new Vector3(0f, 0f, -DiscForward)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
    }

    private static Vector3 ToGodot(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    private static Quaternion ToGodot(System.Numerics.Quaternion q) => new Quaternion(q.X, q.Y, q.Z, q.W).Normalized();
}
