using Godot;
using Neo.Flight;
using Neo.Input;
using Neo.Shell;
using Neo.Targets;

namespace Neo.Guns;

/// <summary>
/// Godot side of the Guns module: a glowing yellow-orange tracer streak for every third
/// bullet of every plane (interpolated between fixed ticks), small muzzle flashes on each
/// plane's volleys and a cartoon spark puff where a bullet hits (a smaller one on a plane).
/// No rules here. Opened on its own (no module bound, e.g. by screenshot.sh) it runs a
/// preview: its own Core fed the player and a wingman firing at a balloon and an enemy plane,
/// simple stand-in planes and balloon, and a chase camera.
/// </summary>
public partial class GunsView : Node3D, IModuleView
{
    private const float TracerLength = 22f;
    private const float FlashSeconds = 0.07f;
    private const float SparkSeconds = 0.45f;
    private const int SparkBits = 6;
    private const float PlaneSparkScale = 0.45f;

    private static readonly Color Tracer = new(1f, 0.92f, 0.35f);
    private static readonly Color TracerGlow = new(1f, 0.45f, 0.05f, 0.5f);
    private static readonly Color Flash = new(1f, 0.93f, 0.55f);
    private static readonly Color Spark = new(1f, 0.85f, 0.3f);
    private static readonly Color Smoke = new(0.93f, 0.9f, 0.84f);
    private static readonly Color Ink = new(0.08f, 0.06f, 0.05f);

    private GunsModule? _guns;
    private MessageBus? _previewBus;
    private double _previewTime;
    private Vector3 _previewPlane;
    private Node3D? _previewPlaneNode;
    private Node3D? _previewWingmanNode;
    private Node3D? _previewEnemyNode;
    private Camera3D? _previewCamera;

    private readonly MeshInstance3D[] _tracers = new MeshInstance3D[GunsModule.PoolSize];
    private readonly MeshInstance3D[,] _flashes = new MeshInstance3D[GunsModule.Planes, GunsModule.Guns];
    private readonly Node3D[] _sparks = new Node3D[GunsModule.HitRing];
    private readonly MeshInstance3D[,] _sparkBits = new MeshInstance3D[GunsModule.HitRing, SparkBits];
    private readonly MeshInstance3D[] _sparkPuffs = new MeshInstance3D[GunsModule.HitRing];
    private readonly float[] _sparkAge = new float[GunsModule.HitRing];
    private readonly Vector3[] _bitDirections = new Vector3[SparkBits];
    private readonly long[] _lastVolley = new long[GunsModule.Planes];
    private readonly float[] _flashAge = new float[GunsModule.Planes];
    private int _hitsShown;

    public void Bind(IModule module) => _guns = (GunsModule)module;

    public override void _Ready()
    {
        if (_guns == null)
            StartPreview();
        for (int p = 0; p < GunsModule.Planes; p++)
            _flashAge[p] = FlashSeconds;

        var tracerCore = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = Tracer,
            // Drawn after its see-through halo, so the hot core stays bright.
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            RenderPriority = 1,
            EmissionEnabled = true,
            Emission = Tracer,
            EmissionEnergyMultiplier = 3f,
        };
        var tracerGlow = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = TracerGlow,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        var flash = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = Flash,
            EmissionEnabled = true,
            Emission = Flash,
            EmissionEnergyMultiplier = 2f,
        };
        var spark = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = Spark };
        var smoke = Toon(Smoke);
        var outline = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = Ink,
            CullMode = BaseMaterial3D.CullModeEnum.Front,
            Grow = true,
            GrowAmount = 0.12f,
        };

        // A unit-long streak along Y, scaled to length per frame; the halo around it glows.
        var streak = new CylinderMesh { TopRadius = 0.16f, BottomRadius = 0.08f, Height = 1f, RadialSegments = 6, Rings = 1 };
        var halo = new CylinderMesh { TopRadius = 0.42f, BottomRadius = 0.15f, Height = 1f, RadialSegments = 8, Rings = 1 };
        var star = new SphereMesh { Radius = 0.45f, Height = 0.9f, RadialSegments = 6, Rings = 3 };
        var bit = new BoxMesh { Size = new Vector3(0.35f, 1.6f, 0.35f) };
        var puff = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 12, Rings = 6 };

        for (int i = 0; i < GunsModule.PoolSize; i++)
        {
            var tracer = Add(this, streak, tracerCore, null);
            Add(tracer, halo, tracerGlow, null);
            tracer.Visible = false;
            _tracers[i] = tracer;
        }
        for (int p = 0; p < GunsModule.Planes; p++)
            for (int g = 0; g < GunsModule.Guns; g++)
            {
                _flashes[p, g] = Add(this, star, flash, outline);
                _flashes[p, g].Visible = false;
            }
        for (int b = 0; b < SparkBits; b++)
        {
            float a = b * Mathf.Tau / SparkBits;
            _bitDirections[b] = new Vector3(Mathf.Cos(a), Mathf.Sin(a) * 0.8f + 0.3f, Mathf.Sin(a * 2f) * 0.5f).Normalized();
        }
        for (int s = 0; s < GunsModule.HitRing; s++)
        {
            var node = new Node3D { Visible = false };
            AddChild(node);
            _sparks[s] = node;
            _sparkPuffs[s] = Add(node, puff, smoke, outline);
            for (int b = 0; b < SparkBits; b++)
                _sparkBits[s, b] = Add(node, bit, spark, null);
            _sparkAge[s] = SparkSeconds;
        }

        if (_previewBus != null)
            AddPreviewScene();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_previewBus == null || _guns == null)
            return;
        _previewTime += delta;
        var velocity = new System.Numerics.Vector3(0f, 0f, -55f);
        var position = new System.Numerics.Vector3(0f, 320f, 0f) + velocity * (float)_previewTime;
        _previewPlane = ToGodot(position);
        _previewBus.Publish(new PlaneState(0, position, System.Numerics.Quaternion.Identity, velocity, 55f, 1f));
        _previewBus.Publish(new ControlInput(0, 0, 0, 0, 1f, true, false, System.Numerics.Vector3.Zero));
        // A wingman alongside, firing at an enemy plane crossing slowly ahead of it.
        _previewBus.Publish(new PlaneState(1, position + PreviewWingman, System.Numerics.Quaternion.Identity, velocity, 55f, 1f));
        _previewBus.Publish(new ControlInput(1, 0, 0, 0, 1f, true, false, System.Numerics.Vector3.Zero));
        _previewBus.Publish(new PlaneState(4, position + PreviewEnemy, System.Numerics.Quaternion.Identity, velocity, 55f, 1f));
        _previewBus.Publish(new TargetState(0, PreviewBalloon, 9f, 15, true));
        _guns.Tick(new TickContext((long)(_previewTime * 60 + 0.5), delta));
    }

    public override void _Process(double delta)
    {
        if (_guns == null)
            return;
        float fraction = (float)Engine.GetPhysicsInterpolationFraction();
        float dt = (float)delta;

        var bullets = _guns.Bullets;
        for (int i = 0; i < bullets.Length; i++)
        {
            var b = bullets[i];
            var tracer = _tracers[i];
            if (!b.Alive || b.Serial % 3 != 0)
            {
                tracer.Visible = false;
                continue;
            }
            var previous = ToGodot(b.PreviousPosition);
            var head = previous + (ToGodot(b.Position) - previous) * fraction;
            var velocity = ToGodot(b.Velocity);
            float speed = velocity.Length();
            if (speed < 1e-3f)
            {
                tracer.Visible = false;
                continue;
            }
            var dir = velocity / speed;
            // The tail does not reach back past the muzzle.
            float length = Mathf.Min(TracerLength, speed * ((float)b.Age + fraction / 60f) + 0.5f);
            tracer.Transform = new Transform3D(Along(dir, length), head - dir * (length * 0.5f));
            tracer.Visible = true;
        }

        for (int p = 0; p < GunsModule.Planes; p++)
        {
            long volley = _guns.LastVolleyTick(p);
            if (volley != _lastVolley[p])
            {
                _lastVolley[p] = volley;
                _flashAge[p] = 0;
            }
            _flashAge[p] += dt;
            bool flashing = _flashAge[p] < FlashSeconds && _guns.HasPlane(p);
            for (int g = 0; g < GunsModule.Guns; g++)
            {
                var flash = _flashes[p, g];
                flash.Visible = flashing;
                if (!flashing)
                    continue;
                flash.Position = ToGodot(_guns.MuzzlePosition(p, g));
                float size = 0.8f + 0.4f * ((volley + g + p) % 3);
                flash.Scale = new Vector3(size, size, size * 1.8f);
                flash.Rotation = new Vector3(0, 0, volley * 1.3f + p);
            }
        }

        while (_hitsShown < _guns.Hits)
        {
            int s = _hitsShown % GunsModule.HitRing;
            _sparks[s].Position = ToGodot(_guns.HitPosition(_hitsShown));
            // A hit on a plane is a small puff; on a balloon a full one.
            _sparks[s].Scale = Vector3.One * (_guns.HitPlane(_hitsShown) >= 0 ? PlaneSparkScale : 1f);
            _sparkAge[s] = 0;
            _hitsShown++;
        }
        for (int s = 0; s < GunsModule.HitRing; s++)
            AnimateSpark(s, dt);

        if (_previewCamera != null && _previewPlaneNode != null)
        {
            _previewPlaneNode.Position = _previewPlane;
            if (_previewWingmanNode != null)
                _previewWingmanNode.Position = _previewPlane + ToGodot(PreviewWingman);
            if (_previewEnemyNode != null)
                _previewEnemyNode.Position = _previewPlane + ToGodot(PreviewEnemy);
            _previewCamera.Position = _previewPlane + new Vector3(-7f, 3.5f, 14f);
            _previewCamera.LookAt(_previewPlane + new Vector3(0f, -1.5f, -90f), Vector3.Up);
        }
    }

    // A puff swells and fades, spark bits fly out and shrink.
    private void AnimateSpark(int s, float delta)
    {
        if (_sparkAge[s] >= SparkSeconds)
        {
            _sparks[s].Visible = false;
            return;
        }
        float age = _sparkAge[s] += delta;
        float k = Mathf.Min(1f, age / SparkSeconds);
        _sparks[s].Visible = true;
        _sparkPuffs[s].Scale = Vector3.One * (3.2f * Mathf.Sin(Mathf.Pi * Mathf.Sqrt(k)) + 0.01f);
        for (int b = 0; b < SparkBits; b++)
        {
            var bit = _sparkBits[s, b];
            var dir = _bitDirections[b];
            bit.Transform = new Transform3D(Along(dir, 1f - k), dir * (2f + 7f * Mathf.Sqrt(k)));
        }
    }

    // A basis whose Y axis is `dir` scaled to `length`.
    private static Basis Along(Vector3 dir, float length)
    {
        var side = Mathf.Abs(dir.Y) < 0.9f ? Vector3.Up : Vector3.Right;
        var x = side.Cross(dir).Normalized();
        var z = x.Cross(dir);
        return new Basis(x, dir * Mathf.Max(0.01f, length), z);
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

    private static readonly System.Numerics.Vector3 PreviewBalloon = new(6f, 319f, -400f);
    private static readonly System.Numerics.Vector3 PreviewWingman = new(14f, 1f, 4f);
    private static readonly System.Numerics.Vector3 PreviewEnemy = new(15f, 1.5f, -140f);

    // A simple biplane: fuselage, two wings, tail plane and fin.
    private Node3D StandInPlane(string name, Material body, Material wings, Material outline)
    {
        var plane = new Node3D { Name = name };
        AddChild(plane);
        var fuselage = Add(plane, new BoxMesh { Size = new Vector3(1.1f, 1.1f, 6f) }, body, outline);
        fuselage.Position = new Vector3(0, -0.6f, 1.2f);
        var wing = new BoxMesh { Size = new Vector3(9f, 0.18f, 1.6f) };
        Add(plane, wing, wings, outline).Position = new Vector3(0, -1.3f, 0.2f);
        Add(plane, wing, wings, outline).Position = new Vector3(0, 0.9f, -0.2f);
        var tail = Add(plane, new BoxMesh { Size = new Vector3(3.2f, 0.15f, 1f) }, wings, outline);
        tail.Position = new Vector3(0, -0.4f, 4f);
        var fin = Add(plane, new BoxMesh { Size = new Vector3(0.15f, 1.3f, 1f) }, body, outline);
        fin.Position = new Vector3(0, 0.3f, 4f);
        return plane;
    }

    private void StartPreview()
    {
        _previewBus = new MessageBus();
        _guns = new GunsModule();
        _guns.Start(_previewBus);
    }

    // Sky, light, a stand-in biplane to fire from, a balloon to hit and a chase camera.
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
        var red = Toon(new Color(0.82f, 0.18f, 0.12f));
        var cream = Toon(new Color(0.96f, 0.91f, 0.76f));
        _previewPlaneNode = StandInPlane("PreviewPlane", red, cream, outline);
        _previewWingmanNode = StandInPlane("PreviewWingman", red, cream, outline);
        _previewEnemyNode = StandInPlane("PreviewEnemy", Toon(new Color(0.35f, 0.42f, 0.3f)), Toon(new Color(0.62f, 0.6f, 0.45f)), outline);

        var balloon = Add(this, new CapsuleMesh { Radius = 4.5f, Height = 18f, RadialSegments = 32, Rings = 12 }, cream, outline);
        balloon.Position = ToGodot(PreviewBalloon);
        balloon.Rotation = new Vector3(Mathf.Pi / 2, 0, 0);

        _previewCamera = new Camera3D { Fov = 50f };
        AddChild(_previewCamera);
        _previewCamera.MakeCurrent();
    }
}
