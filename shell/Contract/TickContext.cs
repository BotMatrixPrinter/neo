namespace Neo.Shell;

/// <summary>What the shell passes to every module on each fixed step.</summary>
public readonly struct TickContext
{
    public TickContext(long tick, double stepSeconds)
    {
        Tick = tick;
        StepSeconds = stepSeconds;
    }

    /// <summary>Number of this step, starting at 1.</summary>
    public long Tick { get; }

    /// <summary>Length of one step, in seconds.</summary>
    public double StepSeconds { get; }
}
