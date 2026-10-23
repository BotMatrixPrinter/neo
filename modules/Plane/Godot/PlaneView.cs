using Godot;
using Neo.Flight;
using Neo.Shell;

namespace Neo.Plane;

/// <summary>
/// Godot side of the Plane module: draws the player's biplane (Models/biplane.glb) where its
/// Core says, interpolated between the last two fixed steps, with toon shading, an ink
/// outline, a propeller spinning with throttle and a faint smoke trail. No rules here.
/// Opened on its own (no module bound, e.g. by screenshot.sh) it runs a preview: its own
/// Core fed a gentle banked circle, a sky, a light and a chase camera.
/// </summary>
public partial class PlaneView : Node3D, IModuleView
{
    private const string ModelPath = "res://modules/Plane/Models/biplane.glb";
    private const float IdleRevsPerSecond = 6f;
    private const float FullRevsPerSecond = 30f;

    private static readonly Color Smoke = new(1f, 1f, 1f, 0.06f);

    private PlaneModule? _plane;
    private MessageBus? _previewBus;
    private double _previewTime;
    private Camera3D? _previewCamera;

    private Node3D _body = null!;
    private Node3D? _propeller;
    private Basis _propellerRest = Basis.Identity;
    private CpuParticles3D? _smoke;
    private float _spin;

    public void Bind(IModule module) => _plane = (PlaneModule)module;

    public override void _Ready()
    {
        if (_plane == null)
            StartPreview();

        _body = new Node3D { Name = "Plane", Visible = false };
        AddChild(_body);
        var model = LoadModel();
        if (model != null)
        {
            _body.AddChild(model);
            Toon(model, GD.Load<Shader>("res://modules/Plane/Godot/Toon.gdshader"),
                new ShaderMaterial { Shader = GD.Load<Shader>("res://modules/Plane/Godot/Outline.gdshader") });
            _propeller = model.FindChild("Propeller", true, false) as Node3D;
            if (_propeller != null)
                _propellerRest = _propeller.Basis;
            var exhaust = model.FindChild("Exhaust", true, false) as Node3D;
            if (exhaust == null)
                GD.PushWarning("PlaneView: biplane.glb has no Exhaust node; smoke starts at the plane's center");
            _smoke = MakeSmoke();
            (exhaust ?? _body).AddChild(_smoke);
        }

        if (_previewBus != null)
            AddPreviewWorld();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_previewBus == null)
            return;
        _previewTime += delta;
        _previewBus.Publish(PreviewState(_previewTime));
    }

    public override void _Process(double delta)
    {
        if (_plane == null || !_plane.HasState)
            return;
        var previous = _plane.Previous;
        var latest = _plane.Latest;
        float f = (float)Engine.GetPhysicsInterpolationFraction();
        var from = new Transform3D(new Basis(ToGodot(previous.Orientation)), ToGodot(previous.Position));
        var to = new Transform3D(new Basis(ToGodot(latest.Orientation)), ToGodot(latest.Position));
        _body.Transform = from.InterpolateWith(to, f);
        _body.Visible = true;
        if (_smoke != null)
            _smoke.Emitting = true;

        float throttle = Mathf.Clamp(Mathf.Lerp(previous.Throttle, latest.Throttle, f), 0f, 1f);
        float revs = Mathf.Lerp(IdleRevsPerSecond, FullRevsPerSecond, throttle);
        _spin = Mathf.Wrap(_spin + Mathf.Tau * revs * (float)delta, 0f, Mathf.Tau);
        if (_propeller != null)
            _propeller.Basis = _propellerRest * new Basis(Vector3.Back, _spin);

        if (_previewCamera != null)
        {
            // Chase camera: level, ahead and to the right of the plane, a bit above, looking back at it.
            var target = _body.GlobalTransform * new Vector3(0f, 0.4f, 0.6f);
            var ahead = new Vector3(-_body.GlobalBasis.Z.X, 0f, -_body.GlobalBasis.Z.Z).Normalized();
            var right = ahead.Cross(Vector3.Up);
            _previewCamera.GlobalPosition = target + right * 10f + ahead * 6f + new Vector3(0f, 2.5f, 0f);
            _previewCamera.LookAt(target, Vector3.Up);
        }
    }

    // The imported scene if the editor imported the .glb, else the .glb read at runtime.
    private static Node3D? LoadModel()
    {
        if (ResourceLoader.Exists(ModelPath, "PackedScene"))
            return GD.Load<PackedScene>(ModelPath).Instantiate<Node3D>();
        var document = new GltfDocument();
        var state = new GltfState();
        var error = document.AppendFromFile(ModelPath, state);
        if (error != Error.Ok)
        {
            GD.PushWarning($"Plane: cannot read {ModelPath}: {error}");
            return null;
        }
        return document.GenerateScene(state) as Node3D;
    }

    // Every surface gets the cel shader in its own color, with the ink outline as next pass.
    private static void Toon(Node node, Shader toon, Material outline)
    {
        if (node is MeshInstance3D instance && instance.Mesh != null)
        {
            instance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            for (int s = 0; s < instance.Mesh.GetSurfaceCount(); s++)
            {
                var color = instance.Mesh.SurfaceGetMaterial(s) is BaseMaterial3D imported ? imported.AlbedoColor : Colors.White;
                var material = new ShaderMaterial { Shader = toon, NextPass = outline };
                material.SetShaderParameter("albedo", color);
                instance.SetSurfaceOverrideMaterial(s, material);
            }
        }
        foreach (var child in node.GetChildren())
            Toon(child, toon, outline);
    }

    // Faint white puffs left in the world behind the exhaust, swelling and fading.
    private static CpuParticles3D MakeSmoke()
    {
        var puff = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add, // particle slots not yet spawned draw black: adding black shows nothing
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = PuffTexture(),
        };
        var fade = new Gradient();
        fade.SetColor(0, Smoke);
        fade.SetColor(1, new Color(1f, 1f, 1f, 0f));
        var grow = new Curve();
        grow.AddPoint(new Vector2(0f, 0.35f));
        grow.AddPoint(new Vector2(1f, 1f));
        return new CpuParticles3D
        {
            Name = "Smoke",
            Emitting = false,
            Amount = 260,
            Lifetime = 2.2,
            LocalCoords = false,
            Mesh = new QuadMesh { Size = new Vector2(2f, 2f), Material = puff },
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

    // A soft round puff: white, alpha falling off from the center.
    private static ImageTexture PuffTexture()
    {
        const int size = 32;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float a = Mathf.Clamp(1f - Mathf.Sqrt(dx * dx + dy * dy), 0f, 1f);
                image.SetPixel(x, y, new Color(1f, 1f, 1f, a * a * (3f - 2f * a)));
            }
        return ImageTexture.CreateFromImage(image);
    }

    private static Vector3 ToGodot(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    private static Quaternion ToGodot(System.Numerics.Quaternion q) => new Quaternion(q.X, q.Y, q.Z, q.W).Normalized();

    private void StartPreview()
    {
        _previewBus = new MessageBus();
        _plane = new PlaneModule();
        _plane.Start(_previewBus);
        _previewBus.Publish(PreviewState(0));
    }

    // A gentle banked right circle (toward the preview camera) at cruise, 400 m up, starting at the origin heading -Z.
    private static PlaneState PreviewState(double time)
    {
        const float speed = 42f, radius = 260f, bank = 0.45f;
        float angle = (float)time * speed / radius;
        var position = new System.Numerics.Vector3(radius * (1f - Mathf.Cos(angle)), 400f, -radius * Mathf.Sin(angle));
        var orientation = System.Numerics.Quaternion.CreateFromYawPitchRoll(-angle, 0.05f, -bank);
        var velocity = new System.Numerics.Vector3(Mathf.Sin(angle), 0f, -Mathf.Cos(angle)) * speed;
        return new PlaneState(PlaneModule.PlayerId, position, orientation, velocity, speed, 0.6f);
    }

    private void AddPreviewWorld()
    {
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.32f, 0.58f, 0.9f),
            SkyHorizonColor = new Color(0.78f, 0.88f, 0.96f),
            GroundHorizonColor = new Color(0.78f, 0.88f, 0.96f),
            GroundBottomColor = new Color(0.45f, 0.62f, 0.4f),
        };
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky },
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.85f, 0.85f, 0.9f),
            AmbientLightEnergy = 0.55f,
        };
        AddChild(new WorldEnvironment { Environment = environment });
        AddChild(new DirectionalLight3D { Rotation = new Vector3(-1.0f, 0.5f, 0), LightEnergy = 1.2f });
        _previewCamera = new Camera3D { Fov = 50f, Far = 2000f };
        AddChild(_previewCamera);
        _previewCamera.MakeCurrent();
    }
}
