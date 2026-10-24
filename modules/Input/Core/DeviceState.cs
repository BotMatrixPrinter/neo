namespace Neo.Input;

/// <summary>
/// Device state already mapped to controls: set by the Input module's Godot view each
/// frame for a gamepad (mapped by <see cref="GamepadFlight"/>), or made by <see cref="KeyboardFlight"/> from keys. Has no aim.
/// <see cref="InputModule"/> keeps the latest one and turns it into <see cref="ControlInput"/>
/// on each fixed step.
/// </summary>
public readonly struct DeviceState
{
    public DeviceState(float pitch, float roll, float yaw, float throttleUp, float throttleDown, bool fire)
        : this(pitch, roll, yaw, throttleUp, throttleDown, fire, repair: false)
    {
    }

    public DeviceState(float pitch, float roll, float yaw, float throttleUp, float throttleDown, bool fire, bool repair)
    {
        Pitch = pitch;
        Roll = roll;
        Yaw = yaw;
        ThrottleUp = throttleUp;
        ThrottleDown = throttleDown;
        Fire = fire;
        Repair = repair;
    }

    /// <summary>-1..1, positive = nose up.</summary>
    public float Pitch { get; }

    /// <summary>-1..1, positive = roll right.</summary>
    public float Roll { get; }

    /// <summary>-1..1, positive = yaw right.</summary>
    public float Yaw { get; }

    /// <summary>0..1: how hard throttle-up is held (Shift key or X/Square = 1).</summary>
    public float ThrottleUp { get; }

    /// <summary>0..1: how hard throttle-down is held (Ctrl key or B/Circle = 1).</summary>
    public float ThrottleDown { get; }

    public bool Fire { get; }

    /// <summary>The Repair button is held (R key or Y/Triangle).</summary>
    public bool Repair { get; }
}
