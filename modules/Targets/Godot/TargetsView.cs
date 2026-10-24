using Godot;
using Neo.Shell;

namespace Neo.Targets;

/// <summary>
/// Godot side of the Targets module: draws each balloon as a cartoon sausage-shaped
/// observation balloon at its Core state, and a cartoon pop (smoke puffs and shreds) when
/// one dies. No rules here. Opened on its own (no module bound, e.g. by screenshot.sh) it
/// runs a preview: its own Core, a camera on balloon 0, and one pop after 1.5 s.
/// </summary>
public partial class TargetsView : Node3D, IModuleView
{
    private const int Puffs = 7;
    private const int Shreds = 12;
    private const float PreviewPopAt = 1.5f;

    private static readonly Color Cream = new(0.96f, 0.91f, 0.76f);
    private static readonly Color Stripe = new(0.78f, 0.22f, 0.16f);
    private static readonly Color Ink = new(0.08f, 0.06f, 0.05f);
    private static readonly Color Wicker = new(0.55f, 0.36f, 0.18f);
    private static readonly Color Smoke = new(0.62f, 0.6f, 0.58f);

    private TargetsModule? _targets;
    private BalloonViews? _views;
    private MessageBus? _previewBus;
    private float _previewTime;
    private bool _previewPopped;

    private readonly Node3D[] _balloons = new Node3D[TargetsModule.Count];
    private readonly Node3D[] _shapes = new Node3D[TargetsModule.Count];
    private readonly Node3D[] _pops = new Node3D[TargetsModule.Count];
    private readonly MeshInstance3D[,] _puffs = new MeshInstance3D[TargetsModule.Count, Puffs];
    private readonly MeshInstance3D[,] _shreds = new MeshInstance3D[TargetsModule.Count, Shreds];
    private readonly Vector3[] _puffOffsets = new Vector3[Puffs];
    private readonly Vector3[] _shredVelocities = new Vector3[Shreds];
    private readonly Vector3[] _shredSpins = new Vector3[Shreds];

    public void Bind(IModule module) => _targets = (TargetsModule)module;

    public override void _Ready()
    {
        if (_targets == null)
            StartPreview();
        _views = new BalloonViews(_targets!);

        var cream = Toon(Cream);
        var stripe = Toon(Stripe);
        var wicker = Toon(Wicker);
        var rope = Toon(Ink);
        var smoke = Toon(Smoke);
        var outline = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = Ink,
            CullMode = BaseMaterial3D.CullModeEnum.Front,
            Grow = true,
            GrowAmount = 0.22f,
        };
        // Puffs are scaled up about 4x, and the outline grows with them.
        var puffOutline = (StandardMaterial3D)outline.Duplicate();
        puffOutline.GrowAmount = 0.05f;

        // The body is 18 m long and 9 m across, its long axis along Z (nose -Z), inside the 9 m hit radius.
        var body = new CapsuleMesh { Radius = 4.5f, Height = 18f, RadialSegments = 32, Rings = 12 };
        var band = new CylinderMesh { TopRadius = 4.62f, BottomRadius = 4.62f, Height = 0.9f, RadialSegments = 32 };
        var verticalFin = new BoxMesh { Size = new Vector3(0.35f, 4.2f, 4f) };
        var sideFin = new BoxMesh { Size = new Vector3(4.2f, 0.35f, 4f) };
        var basket = new BoxMesh { Size = new Vector3(1.6f, 1.3f, 1.6f) };
        var rigging = new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.06f, Height = 4.2f, RadialSegments = 6 };
        var puff = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 12, Rings = 6 };
        var shred = new BoxMesh { Size = new Vector3(2.2f, 0.15f, 1.4f) };

        for (int s = 0; s < Puffs; s++)
            _puffOffsets[s] = Spread(s, 5f);
        for (int s = 0; s < Shreds; s++)
        {
            _shredVelocities[s] = Spread(s + 40, 30f) + new Vector3(0, 8f, 0);
            _shredSpins[s] = Spread(s + 80, 9f);
        }

        for (int i = 0; i < TargetsModule.Count; i++)
        {
            var state = _targets!.GetState(i);
            var balloon = new Node3D { Name = "Balloon" + i, Position = ToGodot(state.Position) };
            var shape = new Node3D { Name = "Shape" };
            balloon.AddChild(shape);

            var lying = new Vector3(Mathf.Pi / 2, 0, 0);
            Add(shape, body, cream, outline, Vector3.Zero, lying);
            for (int b = -1; b <= 1; b++)
                Add(shape, band, stripe, null, new Vector3(0, 0, b * 3.6f), lying);
            Add(shape, verticalFin, cream, outline, new Vector3(0, 4.2f, 7.4f), Vector3.Zero);
            Add(shape, sideFin, cream, outline, new Vector3(4.2f, 0, 7.4f), Vector3.Zero);
            Add(shape, sideFin, cream, outline, new Vector3(-4.2f, 0, 7.4f), Vector3.Zero);
            Add(shape, rigging, rope, null, new Vector3(0, -5.8f, 0), Vector3.Zero);
            Add(shape, basket, wicker, outline, new Vector3(0, -8.5f, 0), Vector3.Zero);

            // The tether, from under the body down to the ground (y = 0).
            float height = Mathf.Max(1f, state.Position.Y - 4.5f);
            var cable = new CylinderMesh { TopRadius = 0.15f, BottomRadius = 0.15f, Height = height, RadialSegments = 6 };
            Add(shape, cable, rope, null, new Vector3(0, -4.5f - height / 2, -2f), Vector3.Zero);

            var pop = new Node3D { Name = "Pop", Visible = false };
            balloon.AddChild(pop);
            for (int s = 0; s < Puffs; s++)
                _puffs[i, s] = Add(pop, puff, smoke, puffOutline, _puffOffsets[s], Vector3.Zero);
            for (int s = 0; s < Shreds; s++)
                _shreds[i, s] = Add(pop, shred, s % 3 == 0 ? stripe : cream, null, Vector3.Zero, Vector3.Zero);

            AddChild(balloon);
            _balloons[i] = balloon;
            _shapes[i] = shape;
            _pops[i] = pop;
            shape.Visible = _views.ShapeVisible(i);
        }

        if (_previewBus != null)
            AddPreviewCamera();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_previewBus == null || _targets == null)
            return;
        _previewTime += (float)delta;
        _targets.Tick(new TickContext(0, delta));
        if (!_previewPopped && _previewTime >= PreviewPopAt)
        {
            _previewPopped = true;
            _previewBus.Publish(new DamageTarget(0, TargetsModule.MaxHealth));
        }
    }

    public override void _Process(double delta)
    {
        if (_views == null)
            return;
        _views.Update((float)delta);
        for (int i = 0; i < TargetsModule.Count; i++)
        {
            _balloons[i].Position = ToGodot(_views.Position(i));
            _shapes[i].Visible = _views.ShapeVisible(i);
            _pops[i].Visible = _views.PopPlaying(i);
            if (_views.PopPlaying(i))
                AnimatePop(i, _views.PopAge(i));
        }
    }

    // Puffs swell then shrink away; shreds fly out, fall and tumble.
    private void AnimatePop(int i, float age)
    {
        float k = age / BalloonViews.PopSeconds;
        float puffScale = 4.5f * Mathf.Sin(Mathf.Pi * Mathf.Sqrt(k)) + 0.01f;
        for (int s = 0; s < Puffs; s++)
        {
            var p = _puffs[i, s];
            p.Position = _puffOffsets[s] * (1f + 2.5f * k);
            p.Scale = Vector3.One * puffScale * (0.7f + 0.15f * (s % 3));
        }
        float shredScale = 1f - k * k;
        for (int s = 0; s < Shreds; s++)
        {
            var p = _shreds[i, s];
            p.Position = _shredVelocities[s] * age + new Vector3(0, -6f * age * age, 0);
            p.Rotation = _shredSpins[s] * age;
            p.Scale = Vector3.One * Mathf.Max(0.01f, shredScale);
        }
    }

    private static MeshInstance3D Add(Node3D parent, Mesh mesh, Material material, Material? outline, Vector3 position, Vector3 rotation)
    {
        var instance = new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = material,
            MaterialOverlay = outline,
            Position = position,
            Rotation = rotation,
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

    // A fixed, scattered direction of length up to `size` for effect piece `n`.
    private static Vector3 Spread(int n, float size)
    {
        float a = n * 2.399963f;
        float y = Mathf.Sin(n * 1.7f + 0.4f);
        float r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
        float len = size * (0.6f + 0.4f * Mathf.Abs(Mathf.Sin(n * 3.1f)));
        return new Vector3(r * Mathf.Cos(a), y, r * Mathf.Sin(a)) * len;
    }

    private static Vector3 ToGodot(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    private void StartPreview()
    {
        _previewBus = new MessageBus();
        _targets = new TargetsModule();
        _targets.Start(_previewBus);
    }

    private void AddPreviewCamera()
    {
        var target = ToGodot(_targets!.GetState(0).Position);
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
        var camera = new Camera3D { Position = target + new Vector3(-46f, -6f, -10f), Fov = 55f };
        AddChild(camera);
        camera.LookAt(target, Vector3.Up);
        camera.MakeCurrent();
    }
}
