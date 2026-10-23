using System;

namespace Neo.Core.Clock;

/// <summary>
/// Fixed-timestep clock. Allocation-free; safe to call every frame.
/// </summary>
public sealed class FixedClock : IFixedClock
{
    private double _accumulator;

    public FixedClock(double stepSeconds, int maxStepsPerAdvance = 8)
    {
        if (!(stepSeconds > 0) || double.IsInfinity(stepSeconds))
            throw new ArgumentOutOfRangeException(nameof(stepSeconds), "Step must be a positive finite number of seconds.");
        if (maxStepsPerAdvance < 1)
            throw new ArgumentOutOfRangeException(nameof(maxStepsPerAdvance), "Must allow at least one step.");
        StepSeconds = stepSeconds;
        MaxStepsPerAdvance = maxStepsPerAdvance;
    }

    public double StepSeconds { get; }

    /// <summary>Most steps one <see cref="Advance"/> returns; extra backlog is dropped (no spiral of death).</summary>
    public int MaxStepsPerAdvance { get; }

    public long Tick { get; private set; }

    public double Alpha => _accumulator / StepSeconds;

    public int Advance(double elapsedSeconds)
    {
        if (elapsedSeconds > 0 && !double.IsInfinity(elapsedSeconds))
            _accumulator += elapsedSeconds;

        int steps = 0;
        while (_accumulator >= StepSeconds && steps < MaxStepsPerAdvance)
        {
            _accumulator -= StepSeconds;
            steps++;
        }
        if (_accumulator >= StepSeconds)
            _accumulator %= StepSeconds;

        Tick += steps;
        return steps;
    }

    public void Reset()
    {
        _accumulator = 0;
        Tick = 0;
    }
}
