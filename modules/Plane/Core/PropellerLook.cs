using System;
using System.Numerics;

namespace Neo.Plane;

/// <summary>
/// How the propeller looks, as plain functions (the view applies them). A real prop turns
/// far too fast for any frame rate: drawn at its true speed a two-blade prop jumps half a
/// turn or more per frame and seems still or turning backwards (wagon-wheel effect). So the
/// blades turn at a cartoon rate never above <see cref="MaxTurnsPerFrame"/> of the actual
/// frame, and with throttle a soft prop disc fades in while the blades fade partly out.
/// </summary>
public static class PropellerLook
{
    /// <summary>The axis the propeller turns about in its own node: local Z, the plane's nose axis (nose toward -Z).</summary>
    public static readonly Vector3 SpinAxis = Vector3.UnitZ;

    /// <summary>The blades never turn more than this many turns in one frame (a two-blade prop aliases at 1/4).</summary>
    public const float MaxTurnsPerFrame = 1f / 6f;

    /// <summary>Shown blade speed at idle (throttle 0), turns per second.</summary>
    public const float IdleTurnsPerSecond = 3f;

    /// <summary>Shown blade speed at full throttle, turns per second (before the per-frame cap).</summary>
    public const float FullTurnsPerSecond = 8f;

    /// <summary>Throttle where the disc starts to show, and where it is fully in.</summary>
    public const float DiscStart = 0.15f, DiscFull = 0.6f;

    /// <summary>The disc's opacity when fully in.</summary>
    public const float DiscMaxAlpha = 0.55f;

    /// <summary>The blades' opacity when the disc is fully in.</summary>
    public const float BladeMinAlpha = 0.4f;

    /// <summary>
    /// Turns the blades make this frame, at <paramref name="throttle"/> (0..1) over a frame
    /// of <paramref name="frameSeconds"/>: a cartoon speed rising with throttle, capped at
    /// <see cref="MaxTurnsPerFrame"/> so it never aliases, however slow or uneven the frames.
    /// </summary>
    public static float BladeTurns(float throttle, double frameSeconds)
    {
        if (!(frameSeconds > 0))
            return 0f;
        float rate = IdleTurnsPerSecond + (FullTurnsPerSecond - IdleTurnsPerSecond) * Clamp01(throttle);
        return MathF.Min(rate * (float)frameSeconds, MaxTurnsPerFrame);
    }

    /// <summary>How far the prop disc has faded in at <paramref name="throttle"/>: 0 near idle, 1 from cruise up, smooth between.</summary>
    public static float DiscFade(float throttle)
    {
        float t = Clamp01((Clamp01(throttle) - DiscStart) / (DiscFull - DiscStart));
        return t * t * (3f - 2f * t);
    }

    /// <summary>The prop disc's opacity at <paramref name="throttle"/>.</summary>
    public static float DiscAlpha(float throttle) => DiscMaxAlpha * DiscFade(throttle);

    /// <summary>The blades' opacity at <paramref name="throttle"/>: solid at idle, partly faded once the disc is in.</summary>
    public static float BladeAlpha(float throttle) => 1f - (1f - BladeMinAlpha) * DiscFade(throttle);

    private static float Clamp01(float v) => float.IsNaN(v) ? 0f : Math.Clamp(v, 0f, 1f);
}
