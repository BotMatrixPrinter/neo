using System;

namespace Neo.Input;

/// <summary>
/// Flies the plane from a gamepad: maps a <see cref="PadState"/> to pitch, roll, yaw,
/// throttle, fire and repair. Left stick up/down = pitch (back = nose up), left stick left/right =
/// yaw, right stick left/right = roll; X/Square throttle up, B/Circle throttle down; right
/// trigger past half way or A/Cross fires; Y/Triangle held = repair. Every stick axis gets the same
/// <see cref="Deadzone"/>. Right stick up/down and the left trigger do nothing for now.
/// </summary>
public static class GamepadFlight
{
    /// <summary>Stick and trigger readings at or below this (absolute) count as 0.</summary>
    public const float Deadzone = 0.15f;

    /// <summary>Right trigger reading above which it fires.</summary>
    public const float TriggerFire = 0.5f;

    /// <summary>The controls for <paramref name="pad"/> (unsmoothed: the stick is analog).</summary>
    public static DeviceState Map(in PadState pad) => new(
        DeadZone(pad.LeftY),
        DeadZone(pad.RightX),
        DeadZone(pad.LeftX),
        pad.X ? 1f : 0f,
        pad.B ? 1f : 0f,
        pad.A || DeadZone(pad.RightTrigger) > TriggerFire,
        pad.Y);

    /// <summary>Whether any stick, trigger or button on <paramref name="pad"/> is touched (past the dead zone).</summary>
    public static bool HasInput(in PadState pad) =>
        DeadZone(pad.LeftX) != 0 || DeadZone(pad.LeftY) != 0 || DeadZone(pad.RightX) != 0 || DeadZone(pad.RightY) != 0
        || DeadZone(pad.LeftTrigger) != 0 || DeadZone(pad.RightTrigger) != 0 || pad.A || pad.X || pad.B || pad.Y;

    /// <summary><paramref name="value"/>, or 0 when within the dead zone or not finite.</summary>
    public static float DeadZone(float value) => float.IsFinite(value) && MathF.Abs(value) > Deadzone ? value : 0f;
}
