using Xunit;

namespace Neo.Input.Tests;

public class GamepadFlightTests
{
    [Fact]
    public void LeftStickUpDownIsPitchBackIsNoseUp()
    {
        Assert.Equal(1f, GamepadFlight.Map(new PadState { LeftY = 1f }).Pitch); // back = nose up
        Assert.Equal(-0.6f, GamepadFlight.Map(new PadState { LeftY = -0.6f }).Pitch);
        var state = GamepadFlight.Map(new PadState { LeftY = 0.8f });
        Assert.Equal(0f, state.Roll);
        Assert.Equal(0f, state.Yaw);
    }

    [Fact]
    public void LeftStickLeftRightIsYaw()
    {
        var right = GamepadFlight.Map(new PadState { LeftX = 0.7f });
        Assert.Equal(0.7f, right.Yaw);
        Assert.Equal(0f, right.Roll);
        Assert.Equal(0f, right.Pitch);
        Assert.Equal(-1f, GamepadFlight.Map(new PadState { LeftX = -1f }).Yaw);
    }

    [Fact]
    public void RightStickLeftRightIsRoll()
    {
        var right = GamepadFlight.Map(new PadState { RightX = 0.9f });
        Assert.Equal(0.9f, right.Roll);
        Assert.Equal(0f, right.Yaw);
        Assert.Equal(0f, right.Pitch);
        Assert.Equal(-0.5f, GamepadFlight.Map(new PadState { RightX = -0.5f }).Roll);
    }

    [Fact]
    public void RightStickUpDownAndLeftTriggerDoNothing()
    {
        foreach (var pad in new[] { new PadState { RightY = 1f }, new PadState { RightY = -1f }, new PadState { LeftTrigger = 1f } })
        {
            var state = GamepadFlight.Map(pad);
            Assert.Equal(default(DeviceState), state);
            Assert.True(GamepadFlight.HasInput(pad)); // still switches to the pad
        }
    }

    [Fact]
    public void XAndBAreThrottleUpAndDown()
    {
        var up = GamepadFlight.Map(new PadState { X = true });
        Assert.Equal(1f, up.ThrottleUp);
        Assert.Equal(0f, up.ThrottleDown);
        Assert.False(up.Fire);
        var down = GamepadFlight.Map(new PadState { B = true });
        Assert.Equal(0f, down.ThrottleUp);
        Assert.Equal(1f, down.ThrottleDown);
        Assert.False(down.Fire);
    }

    [Fact]
    public void TriggersDoNotChangeThrottle()
    {
        var state = GamepadFlight.Map(new PadState { LeftTrigger = 1f, RightTrigger = 1f });
        Assert.Equal(0f, state.ThrottleUp);
        Assert.Equal(0f, state.ThrottleDown);
    }

    [Theory]
    [InlineData(0f, false, false)]
    [InlineData(0.5f, false, false)]
    [InlineData(0.51f, false, true)]
    [InlineData(1f, false, true)]
    [InlineData(0f, true, true)]
    public void RightTriggerPastHalfOrAFires(float trigger, bool a, bool fire)
    {
        Assert.Equal(fire, GamepadFlight.Map(new PadState { RightTrigger = trigger, A = a }).Fire);
    }

    [Theory]
    [InlineData(0.15f, 0f)]
    [InlineData(-0.15f, 0f)]
    [InlineData(0.1f, 0f)]
    [InlineData(-0.05f, 0f)]
    [InlineData(0.16f, 0.16f)]
    [InlineData(-0.3f, -0.3f)]
    [InlineData(float.NaN, 0f)]
    public void EveryStickAxisHasTheSameDeadZone(float raw, float expected)
    {
        Assert.Equal(0.15f, GamepadFlight.Deadzone);
        Assert.Equal(expected, GamepadFlight.Map(new PadState { LeftY = raw }).Pitch);
        Assert.Equal(expected, GamepadFlight.Map(new PadState { LeftX = raw }).Yaw);
        Assert.Equal(expected, GamepadFlight.Map(new PadState { RightX = raw }).Roll);
        Assert.Equal(expected != 0, GamepadFlight.HasInput(new PadState { RightY = raw }));
    }

    [Fact]
    public void HasInputOnlyWhenSomethingIsTouched()
    {
        Assert.False(GamepadFlight.HasInput(default));
        Assert.False(GamepadFlight.HasInput(new PadState { LeftX = 0.1f, LeftY = -0.1f, RightX = 0.15f, RightY = 0.05f, LeftTrigger = 0.1f, RightTrigger = 0.1f }));
        Assert.True(GamepadFlight.HasInput(new PadState { LeftX = 0.5f }));
        Assert.True(GamepadFlight.HasInput(new PadState { RightTrigger = 0.3f }));
        Assert.True(GamepadFlight.HasInput(new PadState { A = true }));
        Assert.True(GamepadFlight.HasInput(new PadState { X = true }));
        Assert.True(GamepadFlight.HasInput(new PadState { B = true }));
    }
}
