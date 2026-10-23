using System.Numerics;

namespace Neo.Input;

/// <summary>
/// Raw device state as read by the Input module's Godot view each frame, already mapped
/// to controls. <see cref="InputModule"/> keeps the latest one and turns it into
/// <see cref="ControlInput"/> on each fixed step.
/// </summary>
public readonly struct DeviceState
{
    public DeviceState(float pitch, float roll, float yaw, float throttleUp, float throttleDown, bool fire, bool hasAim, Vector3 aimDirection)
    {
        Pitch = pitch;
        Roll = roll;
        Yaw = yaw;
        ThrottleUp = throttleUp;
        ThrottleDown = throttleDown;
        Fire = fire;
        HasAim = hasAim;
        AimDirection = aimDirection;
    }

    /// <summary>-1..1, positive = nose up.</summary>
    public float Pitch { get; }

    /// <summary>-1..1, positive = roll right.</summary>
    public float Roll { get; }

    /// <summary>-1..1, positive = yaw right.</summary>
    public float Yaw { get; }

    /// <summary>0..1: how hard throttle-up is held (W key = 1, a trigger is analog).</summary>
    public float ThrottleUp { get; }

    /// <summary>0..1: how hard throttle-down is held (S key = 1, a trigger is analog).</summary>
    public float ThrottleDown { get; }

    public bool Fire { get; }

    public bool HasAim { get; }

    /// <summary>World-space direction to fly toward (need not be unit length); used when <see cref="HasAim"/>.</summary>
    public Vector3 AimDirection { get; }
}
