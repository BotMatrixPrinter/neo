using Godot;

namespace Neo.HUD;

/// <summary>
/// The plane markers, under the HUD panels: for every other alive plane, on screen a small
/// downward triangle a few pixels above it with its distance over it ("640 m", "1.2 km"); off
/// screen (or behind the camera) an arrow on a circle around the screen center pointing where to
/// turn. Allies green and rounded, Central red and sharp. Projects with the current 3D camera,
/// positions interpolated between ticks. Drawn in the HUD root's design space (scaled by
/// <see cref="UiScale"/>), so sizes stay constant at any distance. The math is in <see cref="Markers"/>.
/// </summary>
public partial class PlaneMarkers : Control
{
    private const float MarkerSize = 13f;
    private const float ArrowSize = 17f;
    private const float Lift = 30f;
    private const int FontSize = 25;
    private const float TextBox = 200f;
    private const int ArcPoints = 6;

    private static readonly Color Ink = new(0.06f, 0.05f, 0.04f);

    // Unit shapes pointing along +X, built once; drawn through preallocated buffers (no allocations per frame).
    private static readonly Vector2[] Sharp = SharpShape();
    private static readonly Vector2[] Rounded = RoundedShape();
    private readonly Vector2[] _sharp = new Vector2[Sharp.Length];
    private readonly Vector2[] _rounded = new Vector2[Rounded.Length];
    private readonly Vector2[] _sharpEdge = new Vector2[Sharp.Length + 1];
    private readonly Vector2[] _roundedEdge = new Vector2[Rounded.Length + 1];

    public HudModule? Hud { get; set; }

    public Font? LabelFont { get; set; }

    /// <summary>Design units per screen pixel's inverse: the HUD root's scale.</summary>
    public float UiScale { get; set; } = 1f;

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        var hud = Hud;
        var camera = GetViewport().GetCamera3D();
        if (hud == null || camera == null || LabelFont == null || UiScale <= 0f)
            return;
        var screen = GetViewport().GetVisibleRect().Size;
        var screenN = new System.Numerics.Vector2(screen.X, screen.Y);
        float fraction = (float)Engine.GetPhysicsInterpolationFraction();
        var player = hud.PlanePosition(HudModule.PlaneId, fraction);
        var toView = camera.GlobalTransform.AffineInverse();
        float inv = 1f / UiScale;

        for (int id = 0; id < HudModule.Planes; id++)
        {
            if (!hud.HasMarker(id))
                continue;
            var p = hud.PlanePosition(id, fraction);
            var world = new Vector3(p.X, p.Y, p.Z);
            var local = toView * world;
            var view = new System.Numerics.Vector3(local.X, local.Y, local.Z);
            var c = Markers.TeamColor(id);
            var color = new Color(c.X, c.Y, c.Z);
            bool rounded = Markers.ShapeOf(id) == MarkerShape.Rounded;

            if (!Markers.IsBehind(view))
            {
                var projected = camera.UnprojectPosition(world);
                if (Markers.IsOnScreen(view, new System.Numerics.Vector2(projected.X, projected.Y), screenN))
                {
                    var at = projected * inv - new Vector2(0f, Lift);
                    Shape(rounded, at, Mathf.Pi * 0.5f, MarkerSize, color);
                    string text = Markers.DistanceText(System.Numerics.Vector3.Distance(p, player));
                    var baseline = new Vector2(at.X - TextBox * 0.5f, at.Y - MarkerSize - 8f);
                    DrawStringOutline(LabelFont, baseline, text, HorizontalAlignment.Center, TextBox, FontSize, 8, Ink);
                    DrawString(LabelFont, baseline, text, HorizontalAlignment.Center, TextBox, FontSize, color.Lerp(Colors.White, 0.35f));
                    continue;
                }
            }

            var edge = Markers.EdgePoint(view, screenN);
            Shape(rounded, new Vector2(edge.X, edge.Y) * inv, Markers.EdgeAngle(view), ArrowSize, color);
        }
    }

    // The team's shape at center, its tip pointing at angle (screen radians), with a dark outline.
    private void Shape(bool rounded, Vector2 center, float angle, float size, Color color)
    {
        var unit = rounded ? Rounded : Sharp;
        var points = rounded ? _rounded : _sharp;
        var edge = rounded ? _roundedEdge : _sharpEdge;
        float cos = Mathf.Cos(angle);
        float sin = Mathf.Sin(angle);
        for (int i = 0; i < unit.Length; i++)
        {
            var u = unit[i];
            points[i] = center + new Vector2(u.X * cos - u.Y * sin, u.X * sin + u.Y * cos) * size;
            edge[i] = points[i];
        }
        edge[unit.Length] = points[0];
        DrawColoredPolygon(points, color);
        DrawPolyline(edge, Ink, 3f, true);
    }

    // A pointed arrowhead: tip at +X, a narrow back.
    private static Vector2[] SharpShape() => new[] { new Vector2(1.15f, 0f), new Vector2(-0.75f, 0.75f), new Vector2(-0.75f, -0.75f) };

    // A triangle with well-rounded corners (a smaller triangle grown by a circle), about the same size.
    private static Vector2[] RoundedShape()
    {
        const float radius = 0.42f;
        var core = new[] { new Vector2(0.6f, 0f), new Vector2(-0.45f, 0.5f), new Vector2(-0.45f, -0.5f) };
        var points = new Vector2[core.Length * ArcPoints];
        for (int i = 0; i < core.Length; i++)
        {
            var before = core[(i + core.Length - 1) % core.Length];
            var at = core[i];
            var after = core[(i + 1) % core.Length];
            // Outward normals of the edges into and out of this corner (the shape winds clockwise on screen).
            float from = Normal(before, at).Angle();
            float to = Normal(at, after).Angle();
            float sweep = Mathf.Wrap(to - from, -Mathf.Pi, Mathf.Pi);
            for (int k = 0; k < ArcPoints; k++)
            {
                float a = from + sweep * k / (ArcPoints - 1);
                points[i * ArcPoints + k] = at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
            }
        }
        return points;
    }

    private static Vector2 Normal(Vector2 a, Vector2 b)
    {
        var d = (b - a).Normalized();
        return new Vector2(d.Y, -d.X);
    }
}
