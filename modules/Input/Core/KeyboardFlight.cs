using System;

namespace Neo.Input;

/// <summary>
/// Flies the plane directly from keys: maps a <see cref="KeyState"/> to pitch, roll, yaw,
/// throttle, fire and repair, and smooths the axes so a tap makes a small correction. An axis
/// moves toward full deflection over <see cref="RampSeconds"/> and back to center over
/// <see cref="ReturnSeconds"/>. Never aims (<see cref="DeviceState"/> has no aim).
/// </summary>
public sealed class KeyboardFlight
{
    /// <summary>Seconds from center to full deflection with a key held.</summary>
    public const float RampSeconds = 0.15f;

    /// <summary>Seconds from full deflection back to center once the key is released.</summary>
    public const float ReturnSeconds = 0.05f;

    /// <summary>Smoothed pitch, -1..1, positive = nose up.</summary>
    public float Pitch { get; private set; }

    /// <summary>Smoothed roll, -1..1, positive = roll right.</summary>
    public float Roll { get; private set; }

    /// <summary>Smoothed yaw, -1..1, positive = yaw right.</summary>
    public float Yaw { get; private set; }

    /// <summary>
    /// The unsmoothed controls for <paramref name="keys"/>: W/S pitch -1/+1 (nose down/up,
    /// swapped when <paramref name="invertPitch"/>), A/D roll left/right, Q/E yaw left/right,
    /// Shift/Ctrl throttle up/down, Space or the left mouse button fire, R repair.
    /// </summary>
    public static DeviceState Map(in KeyState keys, bool invertPitch)
    {
        float pitch = Axis(keys.S, keys.W);
        if (invertPitch)
            pitch = -pitch;
        return new DeviceState(
            pitch,
            Axis(keys.D, keys.A),
            Axis(keys.E, keys.Q),
            keys.Shift ? 1f : 0f,
            keys.Ctrl ? 1f : 0f,
            keys.Space || keys.LeftMouse,
            keys.R);
    }

    /// <summary>Advances the smoothing by <paramref name="step"/> seconds and returns the smoothed controls.</summary>
    public DeviceState Update(in KeyState keys, bool invertPitch, float step)
    {
        var target = Map(keys, invertPitch);
        Pitch = Approach(Pitch, target.Pitch, step);
        Roll = Approach(Roll, target.Roll, step);
        Yaw = Approach(Yaw, target.Yaw, step);
        return new DeviceState(Pitch, Roll, Yaw, target.ThrottleUp, target.ThrottleDown, target.Fire, target.Repair);
    }

    /// <summary>Back to center at once (e.g. when another device takes over).</summary>
    public void Reset()
    {
        Pitch = 0;
        Roll = 0;
        Yaw = 0;
    }

    private static float Axis(bool positive, bool negative) => (positive ? 1f : 0f) - (negative ? 1f : 0f);

    private static float Approach(float current, float target, float step)
    {
        bool outward = target != 0 && (current == 0 || (current > 0) == (target > 0)) && MathF.Abs(target) > MathF.Abs(current);
        if (outward)
            return MoveToward(current, target, step / RampSeconds);
        // Toward center (fast); when reversing, stop at center first, then ramp out next step.
        float goal = current != 0 && target != 0 && (current > 0) != (target > 0) ? 0f : target;
        return MoveToward(current, goal, step / ReturnSeconds);
    }

    private static float MoveToward(float current, float goal, float delta)
    {
        if (current < goal)
            return MathF.Min(current + delta, goal);
        return MathF.Max(current - delta, goal);
    }
}
