using Godot;
using Neo.Flight;
using Neo.Shell;

namespace Neo.Collisions;

/// <summary>
/// Godot side of the Collisions module: a cartoon crunch where two planes collide, at the
/// contact point: an outlined smoke puff, a jagged yellow flash and splinters of wood and canvas
/// flying out, spinning and falling. Bigger for a harder hit. No rules here. Opened on its own
/// (no module bound, e.g. by screenshot.sh) it runs a preview: its own Core fed two stand-in
/// planes that clip wings, sky, light and a camera.
/// </summary>
public partial class CollisionsView : Node3D, IModuleView
{
    private const float CrunchSeconds = 0.9f;
    private const int Debris = 10;
    private const float DebrisGravity = 9.81f;
    // Closing speed of a full-size crunch, m/s; slower hits are smaller (down to MinScale).
    private const float FullScaleSpeed = 60f;
    private const float MinScale = 0.45f;
    // The crunch drifts on with the planes' mean velocity, slowed by the air (per second).
    private const float DriftDrag = 2.5f;

    private static readonly Color Smoke = new(0.9f, 0.87f, 0.8f);
    private static readonly Color FlashColor = new(1f, 0.85f, 0.25f);
    private static readonly Color Wood = new(0.55f, 0.36f, 0.2f);
    private static readonly Color Canvas = new(0.95f, 0.9f, 0.75f);
    private static readonly Color Ink = new(0.08f, 0.06f, 0.05f);

    private CollisionsModule? _module;
    private MessageBus? _previewBus;
    private double _previewTime;
    private Node3D? _previewA;
    private Node3D? _previewB;
    private System.Numerics.Vector3 _previewPositionA;
    private System.Numerics.Vector3 _previewPositionB;

    private readonly Node3D[] _crunches = new Node3D[CollisionsModule.CollisionRing];
    private readonly MeshInstance3D[] _puffs = new MeshInstance3D[CollisionsModule.CollisionRing];
    private readonly MeshInstance3D[] _flashes = new MeshInstance3D[CollisionsModule.CollisionRing];
    private readonly MeshInstance3D[,] _debris = new MeshInstance3D[CollisionsModule.CollisionRing, Debris];
    private readonly float[] _age = new float[CollisionsModule.CollisionRing];
    private readonly Vector3[] _start = new Vector3[CollisionsModule.CollisionRing];
    private readonly Vector3[] _drift = new Vector3[CollisionsModule.CollisionRing];
    private readonly Vector3[] _debrisVelocity = new Vector3[Debris];
    private readonly Vector3[] _debrisSpin = new Vector3[Debris];
    private int _shown;

    public void Bind(IModule module) => _module = (CollisionsModule)module;

    public override void _Ready()
    {
        if (_module == null)
            StartPreview();

        var outline = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = Ink,
            CullMode = BaseMaterial3D.CullModeEnum.Front,
            Grow = true,
            GrowAmount = 0.1f,
        };
        var smoke = Toon(Smoke);
        var flash = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = FlashColor,
            EmissionEnabled = true,
            Emission = FlashColor,
            EmissionEnergyMultiplier = 2f,
        };
        var wood = Toon(Wood);
        var canvas = Toon(Canvas);

        var puff = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 12, Rings = 6 };
        // A low-poly ball reads as a jagged star once squashed and turned.
        var star = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 5, Rings = 2 };
        var splinter = new BoxMesh { Size = new Vector3(0.25f, 0.25f, 1.2f) };
        var patch = new BoxMesh { Size = new Vector3(0.9f, 0.06f, 0.7f) };

        for (int d = 0; d < Debris; d++)
        {
            // Fixed spread: out all round, mostly up, so it looks the same every run.
            float a = d * Mathf.Tau / Debris + 0.3f * (d % 3);
            _debrisVelocity[d] = new Vector3(Mathf.Cos(a) * 9f, 5f + 4f * (d % 4), Mathf.Sin(a) * 9f);
            _debrisSpin[d] = new Vector3(3f + d, 5f - d * 0.7f, 2f + (d % 3) * 2f);
        }
        for (int s = 0; s < CollisionsModule.CollisionRing; s++)
        {
            var node = new Node3D { Visible = false };
            AddChild(node);
            _crunches[s] = node;
            _flashes[s] = Add(node, star, flash, outline);
            _puffs[s] = Add(node, puff, smoke, outline);
            for (int d = 0; d < Debris; d++)
                _debris[s, d] = Add(node, d % 2 == 0 ? splinter : patch, d % 2 == 0 ? wood : canvas, outline);
            _age[s] = CrunchSeconds;
        }

        if (_previewBus != null)
            AddPreviewScene();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_previewBus == null || _module == null)
            return;
        // Two planes fly north side by side, drifting together until their wingtips clip at
        // about 1.8 s, then knocked apart; it repeats every 4 s.
        _previewTime += delta;
        float t = (float)(_previewTime % 4.0);
        const float contactTime = 1.8f;
        float drift = t < contactTime ? 12f : -3.6f;
        float half = t < contactTime ? 3.4f + 12f * (contactTime - t) : 3.4f + 3.6f * (t - contactTime);
        var forward = new System.Numerics.Vector3(0f, 0f, -45f);
        var center = new System.Numerics.Vector3(0f, 300f, 0f) + forward * t;
        _previewPositionA = center - new System.Numerics.Vector3(half, 0.3f, 0f);
        _previewPositionB = center + new System.Numerics.Vector3(half, 0.3f, 0f);
        var sideways = new System.Numerics.Vector3(drift, 0f, 0f);
        _previewBus.Publish(new PlaneState(0, _previewPositionA, System.Numerics.Quaternion.Identity, forward + sideways, 46f, 1f));
        _previewBus.Publish(new PlaneState(1, _previewPositionB, System.Numerics.Quaternion.Identity, forward - sideways, 46f, 1f));
        _module.Tick(new TickContext((long)(_previewTime * 60 + 0.5), delta));
    }

    public override void _Process(double delta)
    {
        if (_module == null)
            return;
        float dt = (float)delta;
        while (_shown < _module.Collisions)
        {
            int s = _shown % CollisionsModule.CollisionRing;
            float size = Mathf.Lerp(MinScale, 1f, Mathf.Clamp(_module.CollisionSpeed(_shown) / FullScaleSpeed, 0f, 1f));
            _start[s] = ToGodot(_module.CollisionPosition(_shown));
            _drift[s] = ToGodot(_module.CollisionVelocity(_shown));
            _crunches[s].Position = _start[s];
            _crunches[s].Scale = Vector3.One * size;
            _age[s] = 0f;
            _shown++;
        }
        for (int s = 0; s < CollisionsModule.CollisionRing; s++)
            Animate(s, dt);

        if (_previewA != null && _previewB != null)
        {
            _previewA.Position = ToGodot(_previewPositionA);
            _previewB.Position = ToGodot(_previewPositionB);
            var camera = GetViewport().GetCamera3D();
            var mid = (_previewA.Position + _previewB.Position) * 0.5f;
            camera.Position = mid + new Vector3(-6f, 6f, 16f);
            camera.LookAt(mid + new Vector3(0f, 0f, -2f), Vector3.Up);
        }
    }

    // The flash pops and goes, the puff swells and fades, debris flies out, tumbles and falls.
    private void Animate(int s, float delta)
    {
        if (_age[s] >= CrunchSeconds)
        {
            _crunches[s].Visible = false;
            return;
        }
        float age = _age[s] += delta;
        float k = Mathf.Min(1f, age / CrunchSeconds);
        _crunches[s].Visible = true;
        _crunches[s].Position = _start[s] + _drift[s] * ((1f - Mathf.Exp(-DriftDrag * age)) / DriftDrag);
        float flash = Mathf.Max(0f, 1f - k * 4f);
        _flashes[s].Visible = flash > 0f;
        _flashes[s].Scale = new Vector3(3.2f, 2.2f, 3.2f) * (0.4f + flash);
        _flashes[s].Rotation = new Vector3(0.4f, s * 1.1f, 0.3f);
        _puffs[s].Scale = Vector3.One * (2.4f * Mathf.Sin(Mathf.Pi * Mathf.Sqrt(k)) + 0.01f);
        for (int d = 0; d < Debris; d++)
        {
            var v = _debrisVelocity[d];
            var at = v * age + new Vector3(0f, -0.5f * DebrisGravity * age * age, 0f);
            var bit = _debris[s, d];
            bit.Position = at;
            bit.Rotation = _debrisSpin[d] * age;
            bit.Scale = Vector3.One * Mathf.Max(0.01f, 1f - k * k);
        }
    }

    private static MeshInstance3D Add(Node3D parent, Mesh mesh, Material material, Material? outline)
    {
        var instance = new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = material,
            MaterialOverlay = outline,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        parent.AddChild(instance);
        return instance;
    }

    private static StandardMaterial3D Toon(Color color) => new()
    {
        AlbedoColor = color,
        DiffuseMode = BaseMaterial3D.DiffuseModeEnum.Toon,
        SpecularMode = BaseMaterial3D.SpecularModeEnum.Toon,
        Roughness = 1f,
    };

    private static Vector3 ToGodot(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    // A simple biplane: fuselage, two wings, tail plane and fin.
    private Node3D StandInPlane(string name, Material body, Material wings, Material outline)
    {
        var plane = new Node3D { Name = name };
        AddChild(plane);
        Add(plane, new BoxMesh { Size = new Vector3(1.1f, 1.1f, 6f) }, body, outline).Position = new Vector3(0, -0.6f, 1.2f);
        var wing = new BoxMesh { Size = new Vector3(7f, 0.18f, 1.6f) };
        Add(plane, wing, wings, outline).Position = new Vector3(0, -1.3f, 0.2f);
        Add(plane, wing, wings, outline).Position = new Vector3(0, 0.9f, -0.2f);
        Add(plane, new BoxMesh { Size = new Vector3(3.2f, 0.15f, 1f) }, wings, outline).Position = new Vector3(0, -0.4f, 4f);
        Add(plane, new BoxMesh { Size = new Vector3(0.15f, 1.3f, 1f) }, body, outline).Position = new Vector3(0, 0.3f, 4f);
        return plane;
    }

    private void StartPreview()
    {
        _previewBus = new MessageBus();
        _module = new CollisionsModule();
        _module.Start(_previewBus);
    }

    // Sky, light, two stand-in biplanes and a camera.
    private void AddPreviewScene()
    {
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.55f, 0.75f, 0.92f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.8f, 0.8f, 0.85f),
            AmbientLightEnergy = 0.6f,
        };
        AddChild(new WorldEnvironment { Environment = environment });
        AddChild(new DirectionalLight3D { Rotation = new Vector3(-0.9f, 0.6f, 0), LightEnergy = 1.1f });

        var outline = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = Ink,
            CullMode = BaseMaterial3D.CullModeEnum.Front,
            Grow = true,
            GrowAmount = 0.08f,
        };
        var cream = Toon(new Color(0.96f, 0.91f, 0.76f));
        _previewA = StandInPlane("PreviewA", Toon(new Color(0.82f, 0.18f, 0.12f)), cream, outline);
        _previewB = StandInPlane("PreviewB", Toon(new Color(0.2f, 0.35f, 0.7f)), cream, outline);

        var camera = new Camera3D { Fov = 50f };
        AddChild(camera);
        camera.MakeCurrent();
    }
}
