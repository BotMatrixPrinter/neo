using Neo.Shell;

namespace Neo.Clock;

/// <summary>
/// The Clock module: counts fixed simulation steps and publishes <see cref="ClockTicked"/>
/// on each one. Allocation-free per tick.
/// </summary>
public sealed class ClockModule : IModule
{
    private IMessageBus? _bus;

    /// <summary>Steps seen since start.</summary>
    public long Steps { get; private set; }

    /// <summary>Simulated seconds since start.</summary>
    public double Seconds { get; private set; }

    public void Start(IMessageBus bus) => _bus = bus;

    public void Tick(in TickContext tick)
    {
        Steps++;
        Seconds += tick.StepSeconds;
        _bus?.Publish(new ClockTicked(Steps, Seconds));
    }
}
