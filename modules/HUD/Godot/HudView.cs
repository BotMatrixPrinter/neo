using Godot;
using Neo.Damage;
using Neo.Flight;
using Neo.Guns;
using Neo.Shell;
using Neo.Targets;

namespace Neo.HUD;

/// <summary>
/// Godot side of the HUD module: dark rounded panels with a gold edge and bold outlined text.
/// Bottom left: speed (km/h) and altitude (m), interpolated between fixed ticks, and the gun-heat
/// bar (orange; flashing red with "OVERHEATED" while overheated). Top left: "Balloons popped: N",
/// whose number pops when it rises, and the player's kills / deaths. Next to the flight panel: the
/// damage panel, a plane icon colored by the player's part health. Top right: the kill feed.
/// While the player is down: a big centered "SHOT DOWN!" and the respawn countdown. Laid out for
/// 1920x1080 and scaled to the screen. No rules here. Opened on its own (no module bound, e.g. by
/// screenshot.sh) it runs a preview: its own Core fed a plane climbing, guns heating up, balloons
/// popping, damage, kills and the player shot down, over a sky backdrop.
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
    private static readonly Color ShotRed = new(1f, 0.25f, 0.18f);
    private static readonly Color FeedFill = new(0.11f, 0.1f, 0.13f, 0.82f);
    private static readonly Color FeedMine = new(0.32f, 0.2f, 0.05f, 0.9f);

    private const float FeedWidth = 560f;
    private const float FeedHeight = 58f;
    private const float FeedFadeSeconds = 0.6f;

    // "0".."9999", built once so _Process never formats (no allocations per frame).
    private static string[]? _numbers;

    private HudModule? _hud;
    private MessageBus? _previewBus;
    private double _previewTime;
    private int _previewPops;
    private int _previewEvent;
    private int _previewCycle;

    private Control _root = null!;
    private PlaneMarkers _markers = null!;
    private Camera3D? _previewCamera;
    private Panel _flightPanel = null!;
    private Panel _popPanel = null!;
    private Label _speed = null!;
    private Label _altitude = null!;
    private Label _popped = null!;
    private Label _plusOne = null!;
    private Panel _scorePanel = null!;
    private Label _kills = null!;
    private Label _deaths = null!;
    private Panel _damagePanel = null!;
    private DamageIcon _damageIcon = null!;
    private Control _feed = null!;
    private readonly Panel[] _feedRows = new Panel[HudModule.KillFeedCapacity];
    private readonly StyleBoxFlat[] _feedStyles = new StyleBoxFlat[HudModule.KillFeedCapacity];
    private readonly Label[] _feedLabels = new Label[HudModule.KillFeedCapacity];
    private readonly string?[] _feedShown = new string?[HudModule.KillFeedCapacity];
    private Control _downLayer = null!;
    private Control _downBox = null!;
    private Label _downCountdown = null!;
    private string? _downShown;
    private float _downAge;
    private Label _overheated = null!;
    private Panel _heatFill = null!;
    private StyleBoxFlat _heatStyle = null!;
    private Vector2 _laidOutFor;
    private int _shownSpeed = -1;
    private int _shownAltitude = -1;
    private int _shownPops = -1;
    private int _shownKills = -1;
    private int _shownDeaths = -1;
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

        // Under every panel: the other planes' markers and off-screen arrows.
        _markers = new PlaneMarkers { Hud = _hud, LabelFont = font, MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.AddChild(_markers);
        if (_previewBus != null)
        {
            _previewCamera = new Camera3D { Near = 0.3f, Far = 8000f };
            AddChild(_previewCamera);
            _previewCamera.MakeCurrent();
        }

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

        // Top left, next to the balloons: the player's kills and deaths.
        _scorePanel = MakePanel(new Vector2(420f, 100f));
        _root.AddChild(_scorePanel);
        _scorePanel.AddChild(MakeLabel("Kills", font, 36, Cream, new Vector2(28f, 14f), new Vector2(110f, 72f), HorizontalAlignment.Left));
        _kills = MakeLabel("0", font, 54, Gold, new Vector2(122f, 10f), new Vector2(90f, 80f), HorizontalAlignment.Center);
        _scorePanel.AddChild(_kills);
        _scorePanel.AddChild(MakeLabel("Deaths", font, 36, Cream, new Vector2(214f, 14f), new Vector2(130f, 72f), HorizontalAlignment.Left));
        _deaths = MakeLabel("0", font, 54, ShotRed, new Vector2(326f, 10f), new Vector2(80f, 80f), HorizontalAlignment.Center);
        _scorePanel.AddChild(_deaths);

        // Bottom left, next to the flight panel: the damage panel.
        _damagePanel = MakePanel(new Vector2(220f, 258f));
        _root.AddChild(_damagePanel);
        _damagePanel.AddChild(MakeLabel("DAMAGE", font, 26, Gold, new Vector2(0f, 16f), new Vector2(220f, 50f), HorizontalAlignment.Center));
        _damageIcon = new DamageIcon { Position = new Vector2(30f, 84f) };
        _damagePanel.AddChild(_damageIcon);

        // Top right: the kill feed, newest on top.
        _feed = new Control { Size = new Vector2(FeedWidth, (FeedHeight + 10f) * HudModule.KillFeedCapacity), MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.AddChild(_feed);
        for (int i = 0; i < HudModule.KillFeedCapacity; i++)
        {
            var row = new Panel { Position = new Vector2(0f, i * (FeedHeight + 10f)), Size = new Vector2(FeedWidth, FeedHeight), MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
            _feedStyles[i] = Box(FeedFill, Gold, 3, 16, 6);
            row.AddThemeStyleboxOverride("panel", _feedStyles[i]);
            _feedLabels[i] = MakeLabel("", font, 30, Cream, new Vector2(16f, 0f), new Vector2(FeedWidth - 32f, FeedHeight), HorizontalAlignment.Right);
            row.AddChild(_feedLabels[i]);
            _feed.AddChild(row);
            _feedRows[i] = row;
        }

        // Center: SHOT DOWN! and the countdown, over a red tint.
        _downLayer = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _root.AddChild(_downLayer);
        var tint = new ColorRect { Color = new Color(0.5f, 0.05f, 0.02f, 0.28f), MouseFilter = Control.MouseFilterEnum.Ignore };
        tint.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _downLayer.AddChild(tint);
        _downBox = new Control { Size = new Vector2(1100f, 300f), PivotOffset = new Vector2(550f, 150f), MouseFilter = Control.MouseFilterEnum.Ignore };
        _downLayer.AddChild(_downBox);
        var shot = MakeLabel("SHOT DOWN!", font, 150, ShotRed, Vector2.Zero, new Vector2(1100f, 190f), HorizontalAlignment.Center);
        shot.LabelSettings.OutlineSize = 30;
        _downBox.AddChild(shot);
        _downCountdown = MakeLabel("", font, 56, Cream, new Vector2(0f, 196f), new Vector2(1100f, 90f), HorizontalAlignment.Center);
        _downBox.AddChild(_downCountdown);
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
        PreviewCombat(t);
        PreviewPlanes(t, position);
        _hud.Tick(new TickContext((long)(_previewTime * 60 + 0.5), delta));
    }

    // Preview: the other planes around the player. Ahead: an ally near left, an enemy at 640 m and
    // one far off (km); off screen: an ally behind-left, an enemy behind-right and high, one to the
    // right, one below; plane 3 is down (no marker).
    private void PreviewPlanes(float t, System.Numerics.Vector3 player)
    {
        float w = 6f * Mathf.Sin(t * 0.9f);
        PreviewPlane(1, player, -55f + w, -8f, -260f, true);
        PreviewPlane(5, player, 120f, 50f + w, -625f, true);
        PreviewPlane(6, player, -380f, 120f, -1550f + w, true);
        PreviewPlane(2, player, -240f, 10f + w, 180f, true);
        PreviewPlane(4, player, 150f + w, 220f, 300f, true);
        PreviewPlane(7, player, 700f, -40f + w, -150f, true);
        PreviewPlane(3, player, 0f, -60f, -200f, false);
    }

    private void PreviewPlane(int id, System.Numerics.Vector3 player, float x, float y, float z, bool alive)
    {
        var at = player + new System.Numerics.Vector3(x, y, z);
        _previewBus!.Publish(new PlaneState(id, at, System.Numerics.Quaternion.Identity, new System.Numerics.Vector3(0f, 0f, -50f), 50f, 0.8f, alive));
    }

    // Preview, every 10 s: two kills in the feed, then the player shot down at 1.3 s (its parts
    // worn down to a destroyed tail) and back 5 s later at full health.
    private void PreviewCombat(float t)
    {
        int cycle = (int)(t / 10f);
        if (cycle != _previewCycle)
        {
            _previewCycle = cycle;
            _previewEvent = 0;
        }
        float c = t - cycle * 10f;
        if (_previewEvent == 0 && c >= 0.3f)
        {
            _previewBus!.Publish(new PlaneDestroyed(5, 0));
            _previewEvent++;
        }
        if (_previewEvent == 1 && c >= 0.8f)
        {
            _previewBus!.Publish(new PlaneDestroyed(6, 2));
            _previewEvent++;
        }
        if (_previewEvent == 2 && c >= 1.3f)
        {
            _previewBus!.Publish(new PlaneDestroyed(0, 4));
            _previewEvent++;
        }
        if (_previewEvent == 3 && c >= 6.3f)
        {
            _previewBus!.Publish(new PlaneRespawn(0));
            _previewEvent++;
        }
        bool down = _previewEvent == 3;
        float k = _previewEvent >= 4 ? 0f : Mathf.Min(c / 1.3f, 1f);
        _previewBus!.Publish(new PlaneDamageState(0, 1f - 0.3f * k, 1f - 0.5f * k, 1f - 0.8f * k, 1f - k, !down));
    }

    public override void _Process(double delta)
    {
        if (_hud == null)
            return;
        float dt = (float)delta;
        _clock += dt;
        Layout();
        if (_previewCamera != null)
        {
            // Preview chase camera: just behind and above the player, looking ahead.
            var p = _hud.PlanePosition(HudModule.PlaneId, (float)Engine.GetPhysicsInterpolationFraction());
            _previewCamera.GlobalPosition = new Vector3(p.X, p.Y + 2f, p.Z + 8f);
        }

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

        int kills = Mathf.Min(_hud.Kills, MaxNumber);
        if (kills != _shownKills)
        {
            _shownKills = kills;
            _kills.Text = _numbers![kills];
        }
        int deaths = Mathf.Min(_hud.Deaths, MaxNumber);
        if (deaths != _shownDeaths)
        {
            _shownDeaths = deaths;
            _deaths.Text = _numbers![deaths];
        }

        _damageIcon.Set(_hud.Level(PlanePart.Engine), _hud.Level(PlanePart.LeftWing), _hud.Level(PlanePart.RightWing), _hud.Level(PlanePart.Tail));

        // Kill feed: newest on top; lines with the player in them on a gold-brown panel; fading out at the end.
        int count = _hud.KillFeedCount;
        for (int row = 0; row < HudModule.KillFeedCapacity; row++)
        {
            if (row >= count)
            {
                _feedRows[row].Visible = false;
                continue;
            }
            int line = count - 1 - row;
            string text = _hud.KillFeedText(line);
            if (!ReferenceEquals(text, _feedShown[row]))
            {
                _feedShown[row] = text;
                _feedLabels[row].Text = text;
                bool mine = _hud.KillFeedVictim(line) == HudModule.PlaneId || _hud.KillFeedShooter(line) == HudModule.PlaneId;
                _feedStyles[row].BgColor = mine ? FeedMine : FeedFill;
            }
            float alpha = Mathf.Clamp((float)_hud.KillFeedSecondsLeft(line) / FeedFadeSeconds, 0f, 1f);
            _feedRows[row].Modulate = new Color(1f, 1f, 1f, alpha);
            _feedRows[row].Visible = true;
        }

        // Shot down: pops in big, then the countdown.
        if (_hud.PlayerDown)
        {
            if (!_downLayer.Visible)
            {
                _downLayer.Visible = true;
                _downAge = 0f;
            }
            _downAge += dt;
            float k = Mathf.Clamp(_downAge / 0.45f, 0f, 1f);
            float scale = 1f + 0.6f * Mathf.Exp(-6f * k) * Mathf.Cos(k * 9f) * (1f - k);
            _downBox.Scale = new Vector2(scale, scale);
            string countdown = _hud.RespawnText;
            if (!ReferenceEquals(countdown, _downShown))
            {
                _downShown = countdown;
                _downCountdown.Text = countdown;
            }
        }
        else if (_downLayer.Visible)
        {
            _downLayer.Visible = false;
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
        _markers.UiScale = scale;
        _markers.Size = _root.Size;
        _popPanel.Position = new Vector2(Margin, Margin);
        _flightPanel.Position = new Vector2(Margin, _root.Size.Y - Margin - _flightPanel.Size.Y);
        _scorePanel.Position = new Vector2(Margin + _popPanel.Size.X + 20f, Margin);
        _damagePanel.Position = new Vector2(Margin + _flightPanel.Size.X + 20f, _flightPanel.Position.Y);
        _feed.Position = new Vector2(_root.Size.X - Margin - FeedWidth, Margin);
        _downLayer.Size = _root.Size;
        _downBox.Position = new Vector2((_root.Size.X - _downBox.Size.X) * 0.5f, (_root.Size.Y - _downBox.Size.Y) * 0.5f - 60f);
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
