using Neo.Shell;

namespace Neo.Input;

/// <summary>
/// The Input module: keeps the latest <see cref="DeviceState"/> set by its Godot view and
/// publishes one <see cref="ControlInput"/> per fixed step for plane 0. Throttle moves
/// smoothly at <see cref="ThrottleRate"/> per second while up or down is held.
/// Allocation-free per tick.
/// </summary>
public sealed class InputModule : IModule
{
    /// <summary>The one plane for now.</summary>
    public const int PlaneId = 0;

    /// <summary>Throttle at start, 0..1.</summary>
    public const float StartThrottle = 0.7f;

    /// <summary>Throttle change per second with up (or down) fully held.</summary>
    public const float ThrottleRate = 0.5f;

    private IMessageBus? _bus;
    private DeviceState _state;

    /// <summary>Current throttle, 0..1.</summary>
    public float Throttle { get; private set; } = StartThrottle;

    /// <summary>The message published on the last tick.</summary>
    public ControlInput Last { get; private set; }

    public void Start(IMessageBus bus) => _bus = bus;

    /// <summary>Called by the view each frame; the latest state wins at the next tick.</summary>
    public void SetState(in DeviceState state) => _state = state;

    public void Tick(in TickContext tick)
    {
        float up = Unit(_state.ThrottleUp);
        float down = Unit(_state.ThrottleDown);
        float step = (float)tick.StepSeconds;
        if (!(step > 0) || !float.IsFinite(step))
            step = 0;
        float throttle = Throttle + (up - down) * ThrottleRate * step;
        Throttle = throttle < 0 ? 0 : throttle > 1 ? 1 : throttle;

        Last = new ControlInput(PlaneId, _state.Pitch, _state.Roll, _state.Yaw, Throttle, _state.Fire, _state.HasAim, _state.AimDirection);
        _bus?.Publish(Last);
    }

    private static float Unit(float value) => value > 0 && float.IsFinite(value) ? (value > 1 ? 1 : value) : 0;
}
