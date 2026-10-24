using System;
using Neo.Clock;
using Xunit;

namespace Neo.Clock.Tests;

public class FixedClockTests
{
    [Fact]
    public void NoTimeMeansNoSteps()
    {
        var clock = new FixedClock(0.25);
        Assert.Equal(0, clock.Advance(0));
        Assert.Equal(0, clock.Tick);
        Assert.Equal(0, clock.Alpha);
    }

    [Fact]
    public void LessThanAStepAccumulates()
    {
        var clock = new FixedClock(0.25);
        Assert.Equal(0, clock.Advance(0.125));
        Assert.Equal(0.5, clock.Alpha, 9);
        Assert.Equal(1, clock.Advance(0.125));
        Assert.Equal(1, clock.Tick);
        Assert.Equal(0, clock.Alpha, 9);
    }

    [Fact]
    public void ExactMultipleGivesThatManySteps()
    {
        var clock = new FixedClock(0.25);
        Assert.Equal(3, clock.Advance(0.75));
        Assert.Equal(3, clock.Tick);
    }

    [Fact]
    public void RemainderCarriesIntoAlpha()
    {
        var clock = new FixedClock(0.25);
        Assert.Equal(2, clock.Advance(0.5625));
        Assert.Equal(0.25, clock.Alpha, 9);
    }

    [Fact]
    public void StepsAreCappedAndBacklogDropped()
    {
        var clock = new FixedClock(0.25, maxStepsPerAdvance: 4);
        Assert.Equal(4, clock.Advance(10.0625));
        Assert.Equal(4, clock.Tick);
        Assert.InRange(clock.Alpha, 0, 1);
        Assert.Equal(0, clock.Advance(0));
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void BadElapsedCountsAsZero(double elapsed)
    {
        var clock = new FixedClock(0.25);
        Assert.Equal(0, clock.Advance(elapsed));
        Assert.Equal(0, clock.Alpha);
    }

    [Fact]
    public void ResetClearsTickAndAccumulator()
    {
        var clock = new FixedClock(0.25);
        clock.Advance(0.6);
        clock.Reset();
        Assert.Equal(0, clock.Tick);
        Assert.Equal(0, clock.Alpha);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RejectsBadStep(double step)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedClock(step));
    }

    [Fact]
    public void RejectsZeroStepCap()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedClock(0.25, 0));
    }

    [Fact]
    public void ManySmallFramesSumToExpectedTicks()
    {
        var clock = new FixedClock(1.0 / 60);
        long total = 0;
        for (int i = 0; i < 600; i++)
            total += clock.Advance(1.0 / 120);
        Assert.InRange(total, 299, 300);
        Assert.Equal(total, clock.Tick);
    }
}
