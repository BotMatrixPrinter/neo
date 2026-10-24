using System.Numerics;
using Neo.Shell;

namespace Neo.Input;

/// <summary>
/// The Input module: keeps the latest keys (<see cref="SetKeys"/>) or gamepad state
/// (<see cref="SetState"/>) set by its Godot view and publishes one <see cref="ControlInput"/>
/// per fixed step for plane 0, never with aim. Keys fly the plane directly through
/// <see cref="KeyboardFlight"/> (smoothed each step). Throttle moves smoothly at
/// <see cref="ThrottleRate"/> per second while up or down is held. Allocation-free per tick.
/// </summary>
public sealed class InputModule : IModule
{
    /// <summary>The one plane for now.</summary>
    public const int PlaneId = 0;

    /// <summary>Throttle at start, 0..1.</summary>
    public const float StartThrottle = 0.7f;

    /// <summary>Throttle change per second with up (or down) fully held.</summary>
    public const float ThrottleRate = 0.5f;

    /// <summary>
    /// Default for <see cref="InvertPitch"/>: false = W nose down, S nose up (like pushing
    /// and pulling a stick); true = W nose up, S nose down.
    /// </summary>
    public const bool DefaultInvertPitch = false;

    private readonly KeyboardFlight _keyboard = new();
    private IMessageBus? _bus;
    private DeviceState _state;
    private KeyState _keys;
    private bool _usingKeys = true;

    /// <summary>Swaps W and S (the view's exported <c>InvertPitch</c> sets it).</summary>
    public bool InvertPitch { get; set; } = DefaultInvertPitch;

    /// <summary>Current throttle, 0..1.</summary>
    public float Throttle { get; private set; } = StartThrottle;

    /// <summary>The message published on the last tick.</summary>
    public ControlInput Last { get; private set; }

    public void Start(IMessageBus bus) => _bus = bus;

    /// <summary>Gamepad: called by the view each frame; the latest state wins at the next tick.</summary>
    public void SetState(in DeviceState state)
    {
        _state = state;
        _usingKeys = false;
    }

    /// <summary>Mouse and keyboard: called by the view each frame; the latest keys win at the next tick.</summary>
    public void SetKeys(in KeyState keys)
    {
        _keys = keys;
        _usingKeys = true;
    }

    public void Tick(in TickContext tick)
    {
        float step = (float)tick.StepSeconds;
        if (!(step > 0) || !float.IsFinite(step))
            step = 0;

        DeviceState state;
        if (_usingKeys)
        {
            state = _keyboard.Update(_keys, InvertPitch, step);
        }
        else
        {
            _keyboard.Reset();
            state = _state;
        }

        float up = Unit(state.ThrottleUp);
        float down = Unit(state.ThrottleDown);
        float throttle = Throttle + (up - down) * ThrottleRate * step;
        Throttle = throttle < 0 ? 0 : throttle > 1 ? 1 : throttle;

        Last = new ControlInput(PlaneId, state.Pitch, state.Roll, state.Yaw, Throttle, state.Fire, false, Vector3.Zero, state.Repair);
        _bus?.Publish(Last);
    }

    private static float Unit(float value) => value > 0 && float.IsFinite(value) ? (value > 1 ? 1 : value) : 0;
}
