using System;

namespace Neo.Input;

/// <summary>
/// Flies the plane from a gamepad: maps a <see cref="PadState"/> to pitch, roll, yaw,
/// throttle, fire and repair. Right stick up/down = pitch (back = nose up), right stick left/right =
/// roll; left stick up/down = throttle rate (up raises, down lowers, as <see cref="DeviceState.ThrottleUp"/>
/// / <see cref="DeviceState.ThrottleDown"/>; released = throttle holds), left stick left/right = yaw;
/// right trigger past half way or A/Cross fires; Y/Triangle held = repair. Every stick axis goes through
/// <see cref="Stick"/>: a small <see cref="Deadzone"/>, then proportional to how far it is pushed.
/// X/Square, B/Circle and the left trigger do nothing for now.
/// </summary>
public static class GamepadFlight
{
    /// <summary>
    /// Stick and trigger readings at or below this (absolute) count as 0: just enough to ignore
    /// stick drift. Also the threshold for "this pad is being touched".
    /// </summary>
    public const float Deadzone = 0.04f;

    /// <summary>
    /// Response curve of the sticks past the dead zone: output = (rescaled push) ^ this, sign kept.
    /// 1 = linear (the default); above 1 gives finer control near center, for later tuning.
    /// </summary>
    public const float ResponseExponent = 1f;

    /// <summary>Right trigger reading above which it fires.</summary>
    public const float TriggerFire = 0.5f;

    /// <summary>The controls for <paramref name="pad"/> (unsmoothed: the stick is analog).</summary>
    public static DeviceState Map(in PadState pad)
    {
        var throttle = -Stick(pad.LeftY); // stick up (-Y) = throttle up
        return new(
            Stick(pad.RightY),
            Stick(pad.RightX),
            Stick(pad.LeftX),
            throttle > 0 ? throttle : 0f,
            throttle < 0 ? -throttle : 0f,
            pad.A || DeadZone(pad.RightTrigger) > TriggerFire,
            pad.Y);
    }

    /// <summary>Whether any stick, trigger or button on <paramref name="pad"/> is touched (past the dead zone).</summary>
    public static bool HasInput(in PadState pad) =>
        DeadZone(pad.LeftX) != 0 || DeadZone(pad.LeftY) != 0 || DeadZone(pad.RightX) != 0 || DeadZone(pad.RightY) != 0
        || DeadZone(pad.LeftTrigger) != 0 || DeadZone(pad.RightTrigger) != 0 || pad.A || pad.X || pad.B || pad.Y;

    /// <summary>
    /// The control value for stick reading <paramref name="value"/>: 0 within the dead zone (or not
    /// finite), then sign(v) * ((|v| - Deadzone) / (1 - Deadzone)) ^ ResponseExponent, clamped to
    /// -1..1. Starts at 0 right at the dead zone edge (no jump) and reaches 1 at full deflection.
    /// </summary>
    public static float Stick(float value)
    {
        if (!float.IsFinite(value))
            return 0f;
        var magnitude = MathF.Abs(value);
        if (magnitude <= Deadzone)
            return 0f;
        var scaled = MathF.Pow(MathF.Min(1f, (magnitude - Deadzone) / (1f - Deadzone)), ResponseExponent);
        return MathF.CopySign(scaled, value);
    }

    /// <summary><paramref name="value"/> unscaled, or 0 when within the dead zone or not finite (touch detection, triggers).</summary>
    public static float DeadZone(float value) => float.IsFinite(value) && MathF.Abs(value) > Deadzone ? value : 0f;
}
