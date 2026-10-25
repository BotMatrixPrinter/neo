using System;
using Xunit;

namespace Neo.Input.Tests;

public class GamepadFlightTests
{
    [Fact]
    public void RightStickUpDownIsPitchBackIsNoseUp()
    {
        Assert.Equal(1f, GamepadFlight.Map(new PadState { RightY = 1f }).Pitch); // back = nose up
        Assert.Equal(-1f, GamepadFlight.Map(new PadState { RightY = -1f }).Pitch); // forward = nose down
        Assert.Equal(GamepadFlight.Stick(-0.6f), GamepadFlight.Map(new PadState { RightY = -0.6f }).Pitch);
        Assert.True(GamepadFlight.Map(new PadState { RightY = 0.3f }).Pitch > 0f);
        Assert.True(GamepadFlight.Map(new PadState { RightY = -0.3f }).Pitch < 0f);
        var state = GamepadFlight.Map(new PadState { RightY = 0.8f });
        Assert.Equal(new DeviceState(GamepadFlight.Stick(0.8f), 0, 0, 0, 0, false), state); // pitch only
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(-1f)]
    [InlineData(0.5f)]
    [InlineData(-0.05f)]
    public void LeftStickUpDownDoesNotPitch(float raw)
    {
        Assert.Equal(0f, GamepadFlight.Map(new PadState { LeftY = raw }).Pitch);
        Assert.Equal(0f, GamepadFlight.Map(new PadState { LeftY = raw }).Roll);
        Assert.Equal(0f, GamepadFlight.Map(new PadState { LeftY = raw }).Yaw);
        Assert.Equal(GamepadFlight.Stick(0.7f), GamepadFlight.Map(new PadState { LeftY = raw, RightY = 0.7f }).Pitch);
    }

    [Theory]
    [InlineData(-1f, 1f, 0f)] // full push up = full throttle up
    [InlineData(1f, 0f, 1f)] // full pull down = full throttle down
    [InlineData(-0.5f, 0.4792f, 0f)] // half push: about half rate
    [InlineData(0.5f, 0f, 0.4792f)]
    [InlineData(-0.05f, 0.0104f, 0f)] // a small push: slowly
    [InlineData(0.05f, 0f, 0.0104f)]
    [InlineData(-1.3f, 1f, 0f)] // over-range clamps
    [InlineData(0f, 0f, 0f)] // released: no change
    [InlineData(0.04f, 0f, 0f)] // inside the dead zone
    [InlineData(-0.03f, 0f, 0f)]
    [InlineData(float.NaN, 0f, 0f)]
    public void LeftStickUpDownIsThrottleRate(float raw, float up, float down)
    {
        var state = GamepadFlight.Map(new PadState { LeftY = raw });
        Assert.Equal(up, state.ThrottleUp, 3);
        Assert.Equal(down, state.ThrottleDown, 3);
        Assert.Equal(MathF.Abs(GamepadFlight.Stick(raw)), state.ThrottleUp + state.ThrottleDown);
        Assert.True(state.ThrottleUp == 0f || state.ThrottleDown == 0f);
    }

    [Fact]
    public void LeftStickLeftRightIsYaw()
    {
        var right = GamepadFlight.Map(new PadState { LeftX = 0.7f });
        Assert.Equal(GamepadFlight.Stick(0.7f), right.Yaw);
        Assert.Equal(0f, right.Roll);
        Assert.Equal(0f, right.Pitch);
        Assert.Equal(-1f, GamepadFlight.Map(new PadState { LeftX = -1f }).Yaw);
    }

    [Fact]
    public void RightStickLeftRightIsRoll()
    {
        var right = GamepadFlight.Map(new PadState { RightX = 0.9f });
        Assert.Equal(GamepadFlight.Stick(0.9f), right.Roll);
        Assert.Equal(0f, right.Yaw);
        Assert.Equal(0f, right.Pitch);
        Assert.Equal(1f, GamepadFlight.Map(new PadState { RightX = 1f }).Roll);
        Assert.Equal(GamepadFlight.Stick(-0.5f), GamepadFlight.Map(new PadState { RightX = -0.5f }).Roll);
    }

    [Fact]
    public void XBAndLeftTriggerDoNothing()
    {
        var pads = new[]
        {
            new PadState { X = true }, new PadState { B = true }, new PadState { X = true, B = true }, new PadState { LeftTrigger = 1f },
        };
        foreach (var pad in pads)
        {
            var state = GamepadFlight.Map(pad);
            Assert.Equal(default(DeviceState), state); // no throttle change, nothing else
            Assert.True(GamepadFlight.HasInput(pad)); // still switches to the pad
        }
    }

    [Fact]
    public void YIsRepairAndNothingElse()
    {
        var state = GamepadFlight.Map(new PadState { Y = true });
        Assert.Equal(new DeviceState(0, 0, 0, 0, 0, false, repair: true), state);
        Assert.True(state.Repair);
        Assert.False(GamepadFlight.Map(new PadState { A = true, X = true, B = true, RightTrigger = 1f }).Repair);
        Assert.False(GamepadFlight.Map(default).Repair);
        // Held with the sticks: the axes still come through (Flight decides to ignore them).
        var flying = GamepadFlight.Map(new PadState { Y = true, RightY = 0.8f, LeftY = -1f });
        Assert.True(flying.Repair);
        Assert.Equal(GamepadFlight.Stick(0.8f), flying.Pitch);
        Assert.Equal(1f, flying.ThrottleUp);
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

    [Fact]
    public void DeadZoneIsSmallAndResponseLinear()
    {
        Assert.Equal(0.04f, GamepadFlight.Deadzone);
        Assert.Equal(1f, GamepadFlight.ResponseExponent);
    }

    [Theory]
    [InlineData(0.05f, 0.0104f)] // 5% = a small adjustment
    [InlineData(0.5f, 0.4792f)]
    [InlineData(-0.5f, -0.4792f)]
    [InlineData(0.8f, 0.7917f)]
    [InlineData(-0.05f, -0.0104f)]
    public void StickIsProportionalToHowFarItIsPushed(float raw, float expected)
    {
        Assert.Equal(expected, GamepadFlight.Stick(raw), 3);
        Assert.Equal(GamepadFlight.Stick(raw), GamepadFlight.Map(new PadState { RightY = raw }).Pitch);
        Assert.Equal(GamepadFlight.Stick(raw), GamepadFlight.Map(new PadState { LeftX = raw }).Yaw);
        Assert.Equal(GamepadFlight.Stick(raw), GamepadFlight.Map(new PadState { RightX = raw }).Roll);
        var throttle = GamepadFlight.Map(new PadState { LeftY = raw });
        Assert.Equal(-GamepadFlight.Stick(raw), throttle.ThrottleUp - throttle.ThrottleDown);
    }

    [Theory]
    [InlineData(1f, 1f)]
    [InlineData(-1f, -1f)]
    [InlineData(1.3f, 1f)] // over-range readings clamp
    [InlineData(-2f, -1f)]
    public void FullDeflectionIsExactlyOne(float raw, float expected)
    {
        Assert.Equal(expected, GamepadFlight.Stick(raw));
        Assert.Equal(expected, GamepadFlight.Map(new PadState { RightY = raw }).Pitch);
        Assert.Equal(expected, GamepadFlight.Map(new PadState { LeftX = raw }).Yaw);
        Assert.Equal(expected, GamepadFlight.Map(new PadState { RightX = raw }).Roll);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.01f)]
    [InlineData(-0.03f)]
    [InlineData(0.04f)]
    [InlineData(-0.04f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void ZeroInsideTheDeadZoneOrNotFinite(float raw)
    {
        Assert.Equal(0f, GamepadFlight.Stick(raw));
        Assert.Equal(default(DeviceState), GamepadFlight.Map(new PadState { LeftY = raw, LeftX = raw, RightX = raw, RightY = raw }));
        Assert.False(GamepadFlight.HasInput(new PadState { LeftY = raw, LeftX = raw, RightX = raw, RightY = raw, LeftTrigger = raw, RightTrigger = raw }));
    }

    [Fact]
    public void ContinuousAtTheDeadZoneEdge()
    {
        var dz = GamepadFlight.Deadzone;
        Assert.Equal(0f, GamepadFlight.Stick(dz));
        // Just past the edge the output is tiny, not a jump to the dead zone value.
        Assert.InRange(GamepadFlight.Stick(dz + 0.001f), 0f, 0.002f);
        Assert.InRange(GamepadFlight.Stick(-dz - 0.001f), -0.002f, 0f);
        Assert.True(GamepadFlight.Stick(dz + 0.001f) > 0f);
    }

    [Fact]
    public void LinearBetweenTheDeadZoneAndFullDeflection()
    {
        var dz = GamepadFlight.Deadzone;
        var slope = 1f / (1f - dz);
        for (var v = 0.05f; v <= 1f; v += 0.05f)
        {
            Assert.Equal((v - dz) * slope, GamepadFlight.Stick(v), 5);
            Assert.Equal(-GamepadFlight.Stick(v), GamepadFlight.Stick(-v)); // symmetric
        }
        // Equal steps of push give equal steps of output.
        Assert.Equal(GamepadFlight.Stick(0.6f) - GamepadFlight.Stick(0.4f), GamepadFlight.Stick(0.9f) - GamepadFlight.Stick(0.7f), 5);
    }

    [Fact]
    public void TouchDetectionUsesTheSameSmallDeadZone()
    {
        Assert.False(GamepadFlight.HasInput(new PadState { RightY = 0.04f }));
        Assert.True(GamepadFlight.HasInput(new PadState { RightY = 0.05f }));
        Assert.True(GamepadFlight.HasInput(new PadState { LeftTrigger = -0.05f }));
        Assert.True(GamepadFlight.HasInput(new PadState { RightTrigger = 0.05f }));
        Assert.Equal(0f, GamepadFlight.DeadZone(0.04f));
        Assert.Equal(0.05f, GamepadFlight.DeadZone(0.05f)); // unscaled
        Assert.Equal(0f, GamepadFlight.DeadZone(float.NaN));
    }

    [Fact]
    public void HasInputOnlyWhenSomethingIsTouched()
    {
        Assert.False(GamepadFlight.HasInput(default));
        Assert.False(GamepadFlight.HasInput(new PadState { LeftX = 0.03f, LeftY = -0.03f, RightX = 0.04f, RightY = 0.02f, LeftTrigger = 0.03f, RightTrigger = 0.04f }));
        Assert.True(GamepadFlight.HasInput(new PadState { LeftX = 0.5f }));
        Assert.True(GamepadFlight.HasInput(new PadState { RightTrigger = 0.3f }));
        Assert.True(GamepadFlight.HasInput(new PadState { A = true }));
        Assert.True(GamepadFlight.HasInput(new PadState { X = true }));
        Assert.True(GamepadFlight.HasInput(new PadState { B = true }));
        Assert.True(GamepadFlight.HasInput(new PadState { Y = true }));
    }
}
