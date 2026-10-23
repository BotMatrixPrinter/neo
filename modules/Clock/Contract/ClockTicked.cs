namespace Neo.Clock;

/// <summary>Published by the Clock module once per fixed simulation step.</summary>
public readonly struct ClockTicked
{
    public ClockTicked(long tick, double seconds)
    {
        Tick = tick;
        Seconds = seconds;
    }

    /// <summary>Steps since the game started.</summary>
    public long Tick { get; }

    /// <summary>Simulated time since the game started, in seconds.</summary>
    public double Seconds { get; }
}
