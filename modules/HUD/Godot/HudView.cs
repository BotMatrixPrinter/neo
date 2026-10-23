using Godot;
using Neo.Flight;
using Neo.Guns;
using Neo.Shell;
using Neo.Targets;

namespace Neo.HUD;

/// <summary>
/// Godot side of the HUD module: dark rounded panels with a gold edge and bold outlined text.
/// Bottom left: speed (km/h) and altitude (m), interpolated between fixed ticks, and the gun-heat
/// bar (orange; flashing red with "OVERHEATED" while overheated). Top left: "Balloons popped: N",
/// whose number pops when it rises. Laid out for 1920x1080 and scaled to the screen. No rules
/// here. Opened on its own (no module bound, e.g. by screenshot.sh) it runs a preview: its own
/// Core fed a plane climbing, guns heating up and balloons popping, over a sky backdrop.
/// </summary>
public partial class HudView : CanvasLayer, IModuleView
{
    private static readonly Vector2 Design = new(1920f, 1080f);
    private const float Margin = 36f;
    private const float PopSeconds = 0.5f;
    private const int MaxNumber = 9999;

    private static readonly Color PanelFill = new(0.11f, 0.1f, 0.13f, 0.9f);
    private static readonly Color Gold = new(0.95f, 0.74f, 0.27f);
    private static readonly Color GoldDark = new(0.55f, 0.37f, 0.1f);
    private static readonly Color Ink = new(0.06f, 0.05f, 0.04f);
    private static readonly Color Cream = new(1f, 0.97f, 0.88f);
    private static readonly Color HeatOrange = new(1f, 0.56f, 0.12f);
    private static readonly Color HeatRed = new(0.93f, 0.13f, 0.1f);
    private static readonly Color HeatHot = new(1f, 0.38f, 0.2f);
    private static readonly Color BarTrack = new(0.05f, 0.05f, 0.07f, 0.95f);
    private static readonly Color BalloonRed = new(0.92f, 0.22f, 0.2f);

    // "0".."9999", built once so _Process never formats (no allocations per frame).
    private static string[]? _numbers;

    private HudModule? _hud;
    private MessageBus? _previewBus;
    private double _previewTime;
    private int _previewPops;

    private Control _root = null!;
    private Panel _flightPanel = null!;
    private Panel _popPanel = null!;
    private Label _speed = null!;
    private Label _altitude = null!;
    private Label _popped = null!;
    private Label _plusOne = null!;
    private Label _overheated = null!;
    private Panel _heatFill = null!;
    private StyleBoxFlat _heatStyle = null!;
    private Vector2 _laidOutFor;
    private int _shownSpeed = -1;
    private int _shownAltitude = -1;
    private int _shownPops = -1;
    private float _popAge = PopSeconds;
    private float _clock;

    private const float BarWidth = 290f;

    public void Bind(IModule module) => _hud = (HudModule)module;

    public override void _Ready()
    {
        if (_numbers == null)
        {
            _numbers = new string[MaxNumber + 1];
            for (int i = 0; i <= MaxNumber; i++)
                _numbers[i] = i.ToString();
        }
        if (_hud == null)
            StartPreview();

        var font = new FontVariation { BaseFont = ThemeDB.FallbackFont, VariationEmbolden = 1.1f };

        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        if (_previewBus != null)
            _root.AddChild(Backdrop());
        AddChild(_root);

        // Bottom left: speed, altitude, gun heat.
        _flightPanel = MakePanel(new Vector2(470f, 258f));
        _root.AddChild(_flightPanel);
        _flightPanel.AddChild(MakeLabel("SPEED", font, 26, Gold, new Vector2(30f, 22f), new Vector2(130f, 64f), HorizontalAlignment.Left));
        _speed = MakeLabel("0", font, 58, Cream, new Vector2(150f, 18f), new Vector2(170f, 68f), HorizontalAlignment.Right);
        _flightPanel.AddChild(_speed);
        _flightPanel.AddChild(MakeLabel("km/h", font, 28, Gold, new Vector2(332f, 28f), new Vector2(110f, 64f), HorizontalAlignment.Left));
        _flightPanel.AddChild(MakeLabel("ALT", font, 26, Gold, new Vector2(30f, 94f), new Vector2(130f, 64f), HorizontalAlignment.Left));
        _altitude = MakeLabel("0", font, 58, Cream, new Vector2(150f, 90f), new Vector2(170f, 68f), HorizontalAlignment.Right);
        _flightPanel.AddChild(_altitude);
        _flightPanel.AddChild(MakeLabel("m", font, 28, Gold, new Vector2(332f, 100f), new Vector2(110f, 64f), HorizontalAlignment.Left));
        _flightPanel.AddChild(MakeLabel("GUNS", font, 26, Gold, new Vector2(30f, 172f), new Vector2(130f, 56f), HorizontalAlignment.Left));

        var track = new Panel { Position = new Vector2(150f, 176f), Size = new Vector2(BarWidth, 48f), MouseFilter = Control.MouseFilterEnum.Ignore };
        track.AddThemeStyleboxOverride("panel", Box(BarTrack, GoldDark, 3, 14, 0));
        _flightPanel.AddChild(track);
        _heatStyle = Box(HeatOrange, Ink, 3, 12, 0);
        _heatFill = new Panel { Position = new Vector2(4f, 4f), Size = new Vector2(0f, 40f), MouseFilter = Control.MouseFilterEnum.Ignore };
        _heatFill.AddThemeStyleboxOverride("panel", _heatStyle);
        track.AddChild(_heatFill);
        _overheated = MakeLabel("OVERHEATED", font, 27, Cream, Vector2.Zero, new Vector2(BarWidth, 48f), HorizontalAlignment.Center);
        _overheated.LabelSettings.OutlineSize = 10;
        _overheated.Visible = false;
        track.AddChild(_overheated);

        // Top left: balloons popped, with a little balloon.
        _popPanel = MakePanel(new Vector2(500f, 100f));
        _root.AddChild(_popPanel);
        _popPanel.AddChild(BalloonIcon(new Vector2(26f, 16f)));
        _popPanel.AddChild(MakeLabel("Balloons popped:", font, 36, Cream, new Vector2(88f, 14f), new Vector2(300f, 72f), HorizontalAlignment.Left));
        _popped = MakeLabel("0", font, 54, Gold, new Vector2(378f, 10f), new Vector2(100f, 80f), HorizontalAlignment.Center);
        _popped.PivotOffset = new Vector2(50f, 40f);
        _popPanel.AddChild(_popped);
        _plusOne = MakeLabel("+1", font, 34, Gold, new Vector2(440f, 0f), new Vector2(80f, 50f), HorizontalAlignment.Center);
        _plusOne.Visible = false;
        _popPanel.AddChild(_plusOne);
    }

    public override void _Notification(int what)
    {
        // The shell stops a disabled module's view; a CanvasLayer is not hidden by that, so hide it here.
        if (what == NotificationDisabled)
            Visible = false;
        else if (what == NotificationEnabled)
            Visible = true;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_previewBus == null || _hud == null)
            return;
        _previewTime += delta;
        float t = (float)_previewTime;
        float speed = 54f + 3f * Mathf.Sin(t * 1.3f);
        var position = new System.Numerics.Vector3(0f, 310f + 9f * t, -55f * t);
        _previewBus.Publish(new PlaneState(0, position, System.Numerics.Quaternion.Identity, new System.Numerics.Vector3(0f, 0f, -speed), speed, 0.9f));
        // Guns: heat up over 2.6 s, overheat for 2 s, cool, repeat every 6 s.
        float cycle = t % 6f;
        float heat = cycle < 2.6f ? cycle / 2.6f : cycle < 4.6f ? 1f : Mathf.Max(0f, 1f - (cycle - 4.6f) / 1.4f);
        _previewBus.Publish(new GunState(0, heat, cycle >= 2.6f && cycle < 4.6f, cycle < 2.6f, (int)(t * 20f)));
        // A balloon pops every half second from 0.4 s.
        while (_previewPops < (int)((t - 0.4f) / 0.5f) + 1 && t >= 0.4f)
        {
            _previewBus.Publish(new TargetDestroyed(_previewPops % 8, position));
            _previewPops++;
        }
        _hud.Tick(new TickContext((long)(_previewTime * 60 + 0.5), delta));
    }

    public override void _Process(double delta)
    {
        if (_hud == null)
            return;
        float dt = (float)delta;
        _clock += dt;
        Layout();

        float fraction = (float)Engine.GetPhysicsInterpolationFraction();
        int speed = Number(Mathf.Lerp(_hud.PreviousSpeedKmh, _hud.SpeedKmh, fraction));
        int altitude = Number(Mathf.Lerp(_hud.PreviousAltitudeMeters, _hud.AltitudeMeters, fraction));
        if (speed != _shownSpeed)
        {
            _shownSpeed = speed;
            _speed.Text = _numbers![speed];
        }
        if (altitude != _shownAltitude)
        {
            _shownAltitude = altitude;
            _altitude.Text = _numbers![altitude];
        }

        // Heat bar: orange; flashing red with OVERHEATED while overheated.
        float heat = Mathf.Clamp(_hud.Heat, 0f, 1f);
        _heatFill.Size = new Vector2((BarWidth - 8f) * heat, 40f);
        _heatFill.Visible = heat > 0.02f;
        if (_hud.Overheated)
        {
            bool on = Mathf.PosMod(_clock, 0.4f) < 0.22f;
            _heatStyle.BgColor = on ? HeatRed : HeatHot;
            _overheated.Visible = true;
            _overheated.Modulate = on ? Colors.White : new Color(1f, 1f, 1f, 0.55f);
        }
        else
        {
            _heatStyle.BgColor = HeatOrange.Lerp(HeatHot, heat * heat);
            _overheated.Visible = false;
        }

        // Balloons popped: the number jumps and a "+1" floats up when it rises.
        int pops = Mathf.Min(_hud.BalloonsPopped, MaxNumber);
        if (pops != _shownPops)
        {
            if (_shownPops >= 0 && pops > _shownPops)
                _popAge = 0f;
            _shownPops = pops;
            _popped.Text = _numbers![pops];
        }
        if (_popAge < PopSeconds)
        {
            _popAge += dt;
            float k = Mathf.Clamp(_popAge / PopSeconds, 0f, 1f);
            float bounce = 1f + 0.75f * Mathf.Exp(-6f * k) * Mathf.Cos(k * 11f);
            _popped.Scale = new Vector2(bounce, bounce);
            _popped.Modulate = Colors.White.Lerp(new Color(1.5f, 1.3f, 1f), 1f - k);
            _popped.Rotation = 0.18f * Mathf.Exp(-5f * k) * Mathf.Sin(k * 14f);
            _plusOne.Visible = true;
            _plusOne.Position = new Vector2(440f, -6f - 40f * k);
            _plusOne.Modulate = new Color(1f, 1f, 1f, 1f - k);
        }
        else if (_plusOne.Visible)
        {
            _popped.Scale = Vector2.One;
            _popped.Modulate = Colors.White;
            _popped.Rotation = 0f;
            _plusOne.Visible = false;
        }
    }

    // Design space is 1920x1080; the root is scaled to fit the screen and the panels anchor to its corners.
    private void Layout()
    {
        var screen = GetViewport().GetVisibleRect().Size;
        if (screen == _laidOutFor || screen.X <= 0f || screen.Y <= 0f)
            return;
        _laidOutFor = screen;
        float scale = Mathf.Min(screen.X / Design.X, screen.Y / Design.Y);
        _root.Scale = new Vector2(scale, scale);
        _root.Size = screen / scale;
        _popPanel.Position = new Vector2(Margin, Margin);
        _flightPanel.Position = new Vector2(Margin, _root.Size.Y - Margin - _flightPanel.Size.Y);
    }

    private static int Number(float value) => Mathf.Clamp(Mathf.RoundToInt(value), 0, MaxNumber);

    private static Panel MakePanel(Vector2 size)
    {
        var panel = new Panel { Size = size, MouseFilter = Control.MouseFilterEnum.Ignore };
        var box = Box(PanelFill, Gold, 5, 24, 10);
        box.ShadowOffset = new Vector2(0f, 6f);
        panel.AddThemeStyleboxOverride("panel", box);
        return panel;
    }

    private static StyleBoxFlat Box(Color fill, Color edge, int border, int corner, int shadow)
    {
        var box = new StyleBoxFlat { BgColor = fill, BorderColor = edge, AntiAliasing = true };
        box.SetBorderWidthAll(border);
        box.SetCornerRadiusAll(corner);
        if (shadow > 0)
        {
            box.ShadowSize = shadow;
            box.ShadowColor = new Color(0f, 0f, 0f, 0.35f);
        }
        return box;
    }

    private static Label MakeLabel(string text, Font font, int size, Color color, Vector2 position, Vector2 box, HorizontalAlignment align)
    {
        return new Label
        {
            Text = text,
            Position = position,
            Size = box,
            HorizontalAlignment = align,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            LabelSettings = new LabelSettings
            {
                Font = font,
                FontSize = size,
                FontColor = color,
                OutlineSize = size / 5 + 4,
                OutlineColor = Ink,
                ShadowSize = 2,
                ShadowColor = new Color(0f, 0f, 0f, 0.45f),
                ShadowOffset = new Vector2(0f, 4f),
            },
        };
    }

    // A chunky red balloon with an ink outline, a shine and a string.
    private static Control BalloonIcon(Vector2 position)
    {
        var icon = new Control { Position = position, Size = new Vector2(52f, 68f), MouseFilter = Control.MouseFilterEnum.Ignore };
        var line = new ColorRect { Color = Ink, Position = new Vector2(24f, 50f), Size = new Vector2(4f, 20f), MouseFilter = Control.MouseFilterEnum.Ignore };
        icon.AddChild(line);
        var body = new Panel { Position = new Vector2(4f, 0f), Size = new Vector2(44f, 52f), MouseFilter = Control.MouseFilterEnum.Ignore };
        body.AddThemeStyleboxOverride("panel", Box(BalloonRed, Ink, 4, 22, 0));
        icon.AddChild(body);
        var shine = new Panel { Position = new Vector2(13f, 9f), Size = new Vector2(10f, 15f), MouseFilter = Control.MouseFilterEnum.Ignore };
        shine.AddThemeStyleboxOverride("panel", Box(new Color(1f, 0.75f, 0.7f), new Color(0f, 0f, 0f, 0f), 0, 6, 0));
        icon.AddChild(shine);
        return icon;
    }

    private void StartPreview()
    {
        _previewBus = new MessageBus();
        _hud = new HudModule();
        _hud.Start(_previewBus);
    }

    // Preview only: a sunny sky with a hazy sea below, so the panels are seen over a game-like scene.
    private static Control Backdrop()
    {
        var gradient = new Gradient();
        gradient.SetColor(0, new Color(0.33f, 0.62f, 0.9f));
        gradient.SetColor(1, new Color(0.18f, 0.45f, 0.62f));
        gradient.AddPoint(0.62f, new Color(0.86f, 0.93f, 0.97f));
        gradient.AddPoint(0.64f, new Color(0.3f, 0.6f, 0.72f));
        var texture = new GradientTexture2D
        {
            Gradient = gradient,
            FillFrom = new Vector2(0f, 0f),
            FillTo = new Vector2(0f, 1f),
            Width = 8,
            Height = 256,
        };
        var backdrop = new TextureRect
        {
            Texture = texture,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        return backdrop;
    }
}
