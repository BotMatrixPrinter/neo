namespace Neo.Input;

/// <summary>
/// Raw gamepad readings for one pad, read by the Input module's Godot view each frame
/// (axes as Godot reports them: -1..1, +Y = stick down/back; triggers 0..1).
/// <see cref="GamepadFlight"/> maps them to controls.
/// </summary>
public readonly struct PadState
{
    /// <summary>Left stick, -1 left .. 1 right: yaw (rudder).</summary>
    public float LeftX { get; init; }

    /// <summary>Left stick, -1 up/forward .. 1 down/back: throttle rate (up raises, down lowers; centered = holds).</summary>
    public float LeftY { get; init; }

    /// <summary>Right stick, -1 left .. 1 right: roll.</summary>
    public float RightX { get; init; }

    /// <summary>Right stick, -1 up/forward .. 1 down/back: pitch (back = nose up).</summary>
    public float RightY { get; init; }

    /// <summary>Left trigger, 0..1: nothing for now.</summary>
    public float LeftTrigger { get; init; }

    /// <summary>Right trigger, 0..1: fires past half way.</summary>
    public float RightTrigger { get; init; }

    /// <summary>A (Xbox) / Cross (PlayStation): fire.</summary>
    public bool A { get; init; }

    /// <summary>X (Xbox) / Square (PlayStation): nothing for now.</summary>
    public bool X { get; init; }

    /// <summary>B (Xbox) / Circle (PlayStation): nothing for now.</summary>
    public bool B { get; init; }

    /// <summary>Y (Xbox) / Triangle (PlayStation): repair (held).</summary>
    public bool Y { get; init; }
}
