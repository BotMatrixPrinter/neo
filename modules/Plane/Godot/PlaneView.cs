using Godot;
using Neo.Damage;
using Neo.Flight;
using Neo.Shell;

namespace Neo.Plane;

/// <summary>
/// Godot side of the Plane module: draws every plane its Core knows, each a
/// <see cref="PlaneVisual"/> made the first time its id appears: the Allies in
/// Models/biplane.glb, Central in Models/triplane.glb (<see cref="PlaneLook.ModelFor"/>),
/// interpolated between the last two fixed steps, with its propeller and damage looks.
/// No rules here.
/// Opened on its own (no module bound, e.g. by screenshot.sh) it runs a preview: its own
/// Core fed a few planes of both teams in a gentle banked circle (a damaged triplane on the
/// player's tail), a sky, a light and a chase camera.
/// </summary>
public partial class PlaneView : Node3D, IModuleView
{
    private const string ModelDir = "res://modules/Plane/Models/";

    private PlaneModule? _plane;
    private MessageBus? _previewBus;
    private double _previewTime;
    private Camera3D? _previewCamera;

    private readonly PlaneVisual?[] _visuals = new PlaneVisual?[PlaneModule.MaxPlanes];
    private readonly PackedScene?[] _scenes = new PackedScene?[2];
    private readonly Node3D?[] _templates = new Node3D?[2];
    private Shader _toon = null!, _toonFade = null!, _outline = null!, _disc = null!;
    private Texture2D _puff = null!;

    public void Bind(IModule module) => _plane = (PlaneModule)module;

    public override void _Ready()
    {
        if (_plane == null)
            StartPreview();

        _toon = GD.Load<Shader>("res://modules/Plane/Godot/Toon.gdshader");
        _toonFade = GD.Load<Shader>("res://modules/Plane/Godot/ToonFade.gdshader");
        _outline = GD.Load<Shader>("res://modules/Plane/Godot/Outline.gdshader");
        _disc = GD.Load<Shader>("res://modules/Plane/Godot/PropDisc.gdshader");
        _puff = PuffTexture();
        foreach (var model in new[] { PlaneModel.Biplane, PlaneModel.Triplane })
            LoadModel(model);

        if (_previewBus != null)
            AddPreviewWorld();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_previewBus == null)
            return;
        _previewTime += delta;
        PublishPreview(_previewTime);
    }

    public override void _Process(double delta)
    {
        if (_plane == null)
            return;
        float f = (float)Engine.GetPhysicsInterpolationFraction();
        for (int i = 0; i < _plane.Count; i++)
        {
            int id = _plane.IdAt(i);
            var visual = _visuals[id] ??= MakeVisual(id); // once per plane id, the first time it appears
            var previous = _plane.PreviousOf(id);
            var latest = _plane.LatestOf(id);
            var damage = _plane.DamageOf(id);
            visual.Show(in previous, in latest, f, in damage, _plane.LifeOf(id), _plane.CrashesOf(id), delta);
        }

        var player = _visuals[PlaneModule.PlayerId];
        if (_previewCamera != null && player != null)
        {
            // Chase camera: level, ahead and to the right of the player, a bit above, looking back at it.
            var target = player.GlobalTransform * new Vector3(0f, 0.4f, 0.6f);
            var ahead = new Vector3(-player.GlobalBasis.Z.X, 0f, -player.GlobalBasis.Z.Z).Normalized();
            var right = ahead.Cross(Vector3.Up);
            _previewCamera.GlobalPosition = target + right * 11f + ahead * 7f + new Vector3(0f, 3f, 0f);
            _previewCamera.LookAt(target - ahead * 9f, Vector3.Up);
        }
    }

    private PlaneVisual MakeVisual(int planeId)
    {
        var kind = PlaneLook.ModelFor(planeId);
        var visual = new PlaneVisual { Name = $"Plane{planeId}" };
        AddChild(visual);
        visual.Build(NewModel(kind), kind, _toon, _toonFade, _outline, _disc, _puff);
        return visual;
    }

    // The imported scene if the editor imported the .glb, else the .glb read at runtime with
    // GLTFDocument (so headless runs need no import step), kept as a template to copy.
    private void LoadModel(PlaneModel model)
    {
        string path = ModelDir + PlaneLook.FileName(model);
        if (ResourceLoader.Exists(path, "PackedScene"))
        {
            _scenes[(int)model] = GD.Load<PackedScene>(path);
            return;
        }
        var document = new GltfDocument();
        var state = new GltfState();
        var error = document.AppendFromFile(path, state);
        if (error != Error.Ok)
        {
            GD.PushWarning($"Plane: cannot read {path}: {error}");
            return;
        }
        _templates[(int)model] = document.GenerateScene(state) as Node3D;
    }

    private Node3D? NewModel(PlaneModel model)
    {
        var scene = _scenes[(int)model];
        if (scene != null)
            return scene.Instantiate<Node3D>();
        return _templates[(int)model]?.Duplicate() as Node3D;
    }

    public override void _ExitTree()
    {
        foreach (var template in _templates)
            template?.Free(); // never in the tree
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

    private void StartPreview()
    {
        _previewBus = new MessageBus();
        _plane = new PlaneModule();
        _plane.Start(_previewBus);
        PublishPreview(0);
    }

    // The preview's planes, all on one gentle banked right circle (toward the camera) at cruise,
    // 400 m up: the player, a red triplane on its tail with a smoking engine and a holed left
    // wing, a wingman further back on the inside and another triplane behind it.
    private void PublishPreview(double time)
    {
        var bus = _previewBus!;
        bus.Publish(PreviewState(PlaneModule.PlayerId, time, 260f, 0f, 400f));
        bus.Publish(PreviewState(4, time, 266f, -0.075f, 401.5f));
        bus.Publish(PreviewState(1, time, 252f, -0.15f, 398f));
        bus.Publish(PreviewState(5, time, 262f, -0.24f, 403f));
        bus.Publish(new PlaneDamageState(4, 0.15f, 0.25f, 1f, 0.8f, true));
        bus.Publish(new PlaneDamageState(5, 0.45f, 1f, 1f, 1f, true));
    }

    // A point on a right circle of `radius` around (260, height, 0), `lag` radians behind the player.
    private static PlaneState PreviewState(int id, double time, float radius, float lag, float height)
    {
        const float speed = 42f, center = 260f, bank = 0.45f;
        float angle = (float)time * speed / center + lag;
        var position = new System.Numerics.Vector3(center - radius * Mathf.Cos(angle), height, -radius * Mathf.Sin(angle));
        var orientation = System.Numerics.Quaternion.CreateFromYawPitchRoll(-angle, 0.05f, -bank);
        var velocity = new System.Numerics.Vector3(Mathf.Sin(angle), 0f, -Mathf.Cos(angle)) * speed;
        return new PlaneState(id, position, orientation, velocity, speed, 0.6f);
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
