using Godot;
using Neo.Damage;

namespace Neo.HUD;

/// <summary>
/// The damage panel's little plane, seen from above, nose up: engine (nose), left and right
/// wing and tail, each filled green, orange or red by its <see cref="PartLevel"/> and crossed out
/// when destroyed; the fuselage is cream. The part being repaired (<see cref="SetRepair"/>) gets a
/// pulsing cream-gold glow. Redraws only when <see cref="Set"/> changes a level, or each frame
/// while a part is being repaired.
/// </summary>
public partial class DamageIcon : Control
{
    private static readonly Color Ink = new(0.06f, 0.05f, 0.04f);
    private static readonly Color Body = new(0.93f, 0.88f, 0.74f);
    private static readonly Color Green = new(0.36f, 0.82f, 0.3f);
    private static readonly Color Orange = new(1f, 0.6f, 0.12f);
    private static readonly Color Red = new(0.92f, 0.16f, 0.12f);
    private static readonly Color Dead = new(0.4f, 0.1f, 0.08f);
    private static readonly Color Glow = new(1f, 0.92f, 0.45f);

    // Parts in a 160x150 box.
    private static readonly Rect2 EngineRect = new(64f, 4f, 32f, 30f);
    private static readonly Rect2 LeftWingRect = new(4f, 40f, 62f, 34f);
    private static readonly Rect2 RightWingRect = new(94f, 40f, 62f, 34f);
    private static readonly Rect2 FuselageRect = new(66f, 30f, 28f, 90f);
    private static readonly Rect2 TailRect = new(40f, 112f, 80f, 24f);

    private PartLevel _engine = PartLevel.Good;
    private PartLevel _leftWing = PartLevel.Good;
    private PartLevel _rightWing = PartLevel.Good;
    private PartLevel _tail = PartLevel.Good;
    private int _repairing = -1;
    private float _pulse;

    public DamageIcon()
    {
        CustomMinimumSize = new Vector2(160f, 150f);
        Size = CustomMinimumSize;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Set(PartLevel engine, PartLevel leftWing, PartLevel rightWing, PartLevel tail)
    {
        if (engine == _engine && leftWing == _leftWing && rightWing == _rightWing && tail == _tail)
            return;
        _engine = engine;
        _leftWing = leftWing;
        _rightWing = rightWing;
        _tail = tail;
        QueueRedraw();
    }

    /// <summary>Highlights <paramref name="part"/> while <paramref name="repairing"/>; <paramref name="pulse"/> 0..1 animates the glow.</summary>
    public void SetRepair(bool repairing, PlanePart part, float pulse)
    {
        int shown = repairing ? (int)part : -1;
        if (shown == _repairing && shown < 0)
            return;
        _repairing = shown;
        _pulse = pulse;
        QueueRedraw();
    }

    public override void _Draw()
    {
        Part(LeftWingRect, _leftWing);
        Part(RightWingRect, _rightWing);
        Part(TailRect, _tail);
        DrawRect(FuselageRect, Body);
        DrawRect(FuselageRect, Ink, false, 4f);
        // Cockpit.
        DrawCircle(new Vector2(80f, 82f), 8f, Ink);
        Part(EngineRect, _engine);
        // Propeller across the nose.
        DrawLine(new Vector2(52f, 3f), new Vector2(108f, 3f), Ink, 5f);
        if (_repairing >= 0)
            Highlight(RectOf((PlanePart)_repairing));
    }

    private static Rect2 RectOf(PlanePart part) => part switch
    {
        PlanePart.Engine => EngineRect,
        PlanePart.LeftWing => LeftWingRect,
        PlanePart.RightWing => RightWingRect,
        _ => TailRect,
    };

    // The part being repaired: a light wash over it and a thick pulsing glow around it.
    private void Highlight(Rect2 rect)
    {
        float k = 0.5f + 0.5f * _pulse;
        DrawRect(rect, new Color(Glow, 0.2f + 0.25f * k));
        DrawRect(rect.Grow(3f + 2f * k), Ink, false, 4f);
        DrawRect(rect.Grow(1f + 2f * k), new Color(Glow, 0.65f + 0.35f * k), false, 5f);
    }

    private void Part(Rect2 rect, PartLevel level)
    {
        DrawRect(rect, Fill(level));
        DrawRect(rect, Ink, false, 4f);
        if (level != PartLevel.Destroyed)
            return;
        var a = rect.Position + new Vector2(5f, 5f);
        var b = rect.End - new Vector2(5f, 5f);
        DrawLine(a, b, Ink, 7f);
        DrawLine(new Vector2(a.X, b.Y), new Vector2(b.X, a.Y), Ink, 7f);
        DrawLine(a, b, Red, 3f);
        DrawLine(new Vector2(a.X, b.Y), new Vector2(b.X, a.Y), Red, 3f);
    }

    private static Color Fill(PartLevel level) => level switch
    {
        PartLevel.Good => Green,
        PartLevel.Worn => Orange,
        PartLevel.Critical => Red,
        _ => Dead,
    };
}
