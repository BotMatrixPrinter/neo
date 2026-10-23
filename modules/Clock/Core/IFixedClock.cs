namespace Neo.Clock;

/// <summary>
/// Public interface of the Clock feature: a fixed-timestep clock.
/// Feed it real elapsed time with <see cref="Advance"/>; it reports how many
/// fixed simulation steps to run and how far into the next step we are.
/// </summary>
public interface IFixedClock
{
    /// <summary>Length of one simulation step, in seconds.</summary>
    double StepSeconds { get; }

    /// <summary>Total simulation steps taken since creation or <see cref="Reset"/>.</summary>
    long Tick { get; }

    /// <summary>Fraction (0..1) of the next step already elapsed, for render interpolation.</summary>
    double Alpha { get; }

    /// <summary>
    /// Adds <paramref name="elapsedSeconds"/> of real time and returns the number of
    /// fixed steps the caller must simulate now (at most the clock's step cap).
    /// Negative or non-finite input counts as zero.
    /// </summary>
    int Advance(double elapsedSeconds);

    /// <summary>Back to tick 0 with no accumulated time.</summary>
    void Reset();
}
