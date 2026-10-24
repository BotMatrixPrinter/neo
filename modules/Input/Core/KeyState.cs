namespace Neo.Input;

/// <summary>
/// Which flight keys and mouse buttons are held, read by the Input module's Godot view
/// each frame (keys by physical position). <see cref="KeyboardFlight"/> maps them to controls.
/// </summary>
public readonly struct KeyState
{
    /// <summary>Nose down (nose up when pitch is inverted).</summary>
    public bool W { get; init; }

    /// <summary>Nose up (nose down when pitch is inverted).</summary>
    public bool S { get; init; }

    /// <summary>Roll left.</summary>
    public bool A { get; init; }

    /// <summary>Roll right.</summary>
    public bool D { get; init; }

    /// <summary>Yaw left.</summary>
    public bool Q { get; init; }

    /// <summary>Yaw right.</summary>
    public bool E { get; init; }

    /// <summary>Throttle up.</summary>
    public bool Shift { get; init; }

    /// <summary>Throttle down.</summary>
    public bool Ctrl { get; init; }

    /// <summary>Fire.</summary>
    public bool Space { get; init; }

    /// <summary>Repair (held).</summary>
    public bool R { get; init; }

    /// <summary>Left mouse button: fire.</summary>
    public bool LeftMouse { get; init; }
}
