using Godot;
using Neo.Flight;
using Neo.Shell;

namespace Neo.Camera;

/// <summary>
/// Godot side of the Camera module: one Camera3D, made current (the Input module finds it
/// with GetViewport().GetCamera3D() for mouse aim), set every frame from the Core's pose,
/// interpolated between the last two fixed ticks. No rules here. Opened on its own (no
/// module bound, e.g. by screenshot.sh) it runs a preview: its own Core following a
/// stand-in biplane on a banked circle, over a sea with clouds.
/// </summary>
public partial class CameraView : Node3D, IModuleView
{
    private static readonly Color Ink = new(0.08f, 0.06f, 0.05f);

    private CameraModule? _camera;
    private Camera3D _node = null!;

    private MessageBus? _previewBus;
    private Node3D? _previewPlane;
    private float _previewAngle;
    private long _previewTick;

    public void Bind(IModule module) => _camera = (CameraModule)module;

    public override void _Ready()
    {
        if (_camera == null)
            StartPreview();
        _node = new Camera3D { Near = 0.3f, Far = 8000f, Fov = CameraModule.MinFov };
        AddChild(_node);
        _node.MakeCurrent();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_previewBus == null || _camera == null)
            return;
        // Preview: a plane circling at 50 m/s, 300 m radius, banked into the turn.
        const float radius = 300f, speed = 50f;
        _previewAngle += speed / radius * (float)delta;
        var state = PreviewState(_previewAngle, radius, speed);
        _previewBus.Publish(state);
        _camera.Tick(new TickContext(++_previewTick, delta));
    }

    public override void _Process(double delta)
    {
        if (_camera == null || !_camera.HasPose)
            return;
        float t = (float)Engine.GetPhysicsInterpolationFraction();
        var pose = CameraPose.Lerp(_camera.PreviousPose, _camera.Pose, t);
        var q = pose.Orientation;
        _node.GlobalTransform = new Transform3D(new Basis(new Quaternion(q.X, q.Y, q.Z, q.W)), ToGodot(pose.Position));
        _node.Fov = pose.FovDegrees;

        if (_previewPlane != null)
        {
            // The stand-in plane is drawn where the Core will see it, interpolated like the camera.
            var state = PreviewState(_previewAngle - 50f / 300f * (1f - t) / Engine.PhysicsTicksPerSecond, 300f, 50f);
            var o = state.Orientation;
            _previewPlane.GlobalTransform = new Transform3D(new Basis(new Quaternion(o.X, o.Y, o.Z, o.W)), ToGodot(state.Position));
        }
    }

    private static Vector3 ToGodot(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    // The preview plane `angle` radians around a circle centered on the origin, 120 m up.
    private static PlaneState PreviewState(float angle, float radius, float speed)
    {
        var position = new System.Numerics.Vector3(radius * Mathf.Cos(angle), 120f, -radius * Mathf.Sin(angle));
        // Counter-clockwise seen from above: the nose points along the tangent, banked left.
        var heading = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitY, angle);
        var bank = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, 0.5f);
        var orientation = System.Numerics.Quaternion.Normalize(heading * bank);
        var forward = System.Numerics.Vector3.Transform(-System.Numerics.Vector3.UnitZ, orientation);
        return new PlaneState(CameraModule.PlaneId, position, orientation, forward * speed, speed, 0.7f);
    }

    private void StartPreview()
    {
        _previewBus = new MessageBus();
        _camera = new CameraModule();
        _camera.Start(_previewBus);

        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.55f, 0.78f, 0.95f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.85f, 0.85f, 0.9f),
            AmbientLightEnergy = 0.6f,
            FogEnabled = true,
            FogLightColor = new Color(0.7f, 0.85f, 0.97f),
            FogDensity = 0.0006f,
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

        // Sea, with a few islands to show the motion.
        Add(this, new PlaneMesh { Size = new Vector2(8000f, 8000f) }, Toon(new Color(0.18f, 0.55f, 0.72f)), null, Vector3.Zero);
        var island = Toon(new Color(0.45f, 0.7f, 0.3f));
        for (int i = 0; i < 8; i++)
        {
            float a = i * 2.399963f;
            float r = 250f + 90f * (i % 4);
            Add(this, new SphereMesh { Radius = 40f + 8f * (i % 3), Height = 30f }, island, null,
                new Vector3(r * Mathf.Cos(a), 0f, r * Mathf.Sin(a)));
        }
        var cloud = Toon(new Color(1f, 1f, 1f));
        var cloudOutline = (StandardMaterial3D)outline.Duplicate();
        cloudOutline.GrowAmount = 0.6f;
        for (int i = 0; i < 14; i++)
        {
            float a = i * 1.7f + 0.3f;
            float r = 200f + 40f * (i % 5);
            var center = new Vector3(r * Mathf.Cos(a), 150f + 25f * (i % 3), r * Mathf.Sin(a));
            for (int p = 0; p < 3; p++)
                Add(this, new SphereMesh { Radius = 14f - 3f * p, Height = 20f - 4f * p }, cloud, cloudOutline,
                    center + new Vector3(12f * p - 10f, 2f * p, 4f * p));
        }

        // Stand-in biplane (the real one belongs to another module): nose -Z.
        _previewPlane = new Node3D();
        AddChild(_previewPlane);
        var red = Toon(new Color(0.85f, 0.2f, 0.15f));
        var cream = Toon(new Color(0.96f, 0.9f, 0.72f));
        var dark = Toon(new Color(0.25f, 0.2f, 0.18f));
        Add(_previewPlane, new BoxMesh { Size = new Vector3(1.2f, 1.2f, 6f) }, red, outline, Vector3.Zero);
        Add(_previewPlane, new BoxMesh { Size = new Vector3(9f, 0.2f, 1.6f) }, cream, outline, new Vector3(0f, 1.4f, -1.2f));
        Add(_previewPlane, new BoxMesh { Size = new Vector3(8f, 0.2f, 1.5f) }, cream, outline, new Vector3(0f, -0.5f, -1f));
        Add(_previewPlane, new BoxMesh { Size = new Vector3(3.2f, 0.15f, 1f) }, red, outline, new Vector3(0f, 0.2f, 2.6f));
        Add(_previewPlane, new BoxMesh { Size = new Vector3(0.15f, 1.4f, 1f) }, red, outline, new Vector3(0f, 0.9f, 2.6f));
        Add(_previewPlane, new CylinderMesh { TopRadius = 0.7f, BottomRadius = 0.7f, Height = 0.4f }, dark, outline, new Vector3(0f, 0f, -3.1f), new Vector3(Mathf.Pi / 2f, 0f, 0f));
    }

    private static MeshInstance3D Add(Node3D parent, Mesh mesh, Material material, Material? outline, Vector3 position, Vector3 rotation = default)
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
}
