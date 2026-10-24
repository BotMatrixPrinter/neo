using System.Numerics;
using Xunit;

namespace Neo.Input.Tests;

public class ControlInputTests
{
    [Fact]
    public void KeepsValuesInRange()
    {
        var input = new ControlInput(0, 0.5f, -0.25f, 1f, 0.7f, true, true, new Vector3(0, 0, -1));
        Assert.Equal(0, input.PlaneId);
        Assert.Equal(0.5f, input.Pitch);
        Assert.Equal(-0.25f, input.Roll);
        Assert.Equal(1f, input.Yaw);
        Assert.Equal(0.7f, input.Throttle);
        Assert.True(input.Fire);
        Assert.True(input.HasAim);
        Assert.Equal(new Vector3(0, 0, -1), input.AimDirection);
    }

    [Fact]
    public void ClampsAxesAndThrottle()
    {
        var high = new ControlInput(0, 3f, 2f, 1.5f, 1.2f, false, false, Vector3.Zero);
        Assert.Equal(1f, high.Pitch);
        Assert.Equal(1f, high.Roll);
        Assert.Equal(1f, high.Yaw);
        Assert.Equal(1f, high.Throttle);

        var low = new ControlInput(0, -3f, -2f, -1.5f, -0.2f, false, false, Vector3.Zero);
        Assert.Equal(-1f, low.Pitch);
        Assert.Equal(-1f, low.Roll);
        Assert.Equal(-1f, low.Yaw);
        Assert.Equal(0f, low.Throttle);
    }

    [Fact]
    public void NonFiniteAxesCountAsZero()
    {
        var input = new ControlInput(0, float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.NaN, false, false, Vector3.Zero);
        Assert.Equal(0f, input.Pitch);
        Assert.Equal(0f, input.Roll);
        Assert.Equal(0f, input.Yaw);
        Assert.Equal(0f, input.Throttle);
    }

    [Fact]
    public void AimDirectionIsNormalized()
    {
        var input = new ControlInput(0, 0, 0, 0, 0, false, true, new Vector3(3, 0, -4));
        Assert.True(input.HasAim);
        Assert.Equal(0.6f, input.AimDirection.X, 5);
        Assert.Equal(0f, input.AimDirection.Y, 5);
        Assert.Equal(-0.8f, input.AimDirection.Z, 5);
        Assert.Equal(1f, input.AimDirection.Length(), 5);
    }

    [Fact]
    public void NoAimMeansZeroDirection()
    {
        var input = new ControlInput(0, 0, 0, 0, 0, false, false, new Vector3(1, 2, 3));
        Assert.False(input.HasAim);
        Assert.Equal(Vector3.Zero, input.AimDirection);
    }

    [Theory]
    [InlineData(0f, 0f, 0f)]
    [InlineData(float.NaN, 0f, -1f)]
    [InlineData(float.PositiveInfinity, 0f, 0f)]
    public void UnusableAimDirectionMeansNoAim(float x, float y, float z)
    {
        var input = new ControlInput(0, 0, 0, 0, 0, false, true, new Vector3(x, y, z));
        Assert.False(input.HasAim);
        Assert.Equal(Vector3.Zero, input.AimDirection);
    }

    [Fact]
    public void DefaultIsNeutralWithoutAim()
    {
        var input = default(ControlInput);
        Assert.False(input.HasAim);
        Assert.False(input.Fire);
        Assert.False(input.Repair);
        Assert.Equal(0f, input.Throttle);
    }

    [Fact]
    public void RepairIsFalseFromTheOldConstructorAndSetByTheNewOne()
    {
        Assert.False(new ControlInput(0, 0.5f, 0, 0, 0.7f, true, false, Vector3.Zero).Repair);
        var held = new ControlInput(3, 0.5f, -2f, 0, 0.7f, false, true, new Vector3(0, 0, -2), repair: true);
        Assert.True(held.Repair);
        Assert.Equal(3, held.PlaneId);
        Assert.Equal(0.5f, held.Pitch);
        Assert.Equal(-1f, held.Roll);
        Assert.Equal(0.7f, held.Throttle);
        Assert.False(held.Fire);
        Assert.True(held.HasAim);
        Assert.Equal(new Vector3(0, 0, -1), held.AimDirection);
        Assert.False(new ControlInput(0, 0, 0, 0, 0, false, false, Vector3.Zero, repair: false).Repair);
    }
}
