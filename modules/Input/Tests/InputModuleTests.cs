using System;
using System.Numerics;
using System.Reflection;
using System.Runtime.Versioning;
using Neo.Shell;
using Xunit;

namespace Neo.Input.Tests;

public class InputModuleTests
{
    private const double Step = 1.0 / 60;

    private static ControlInput Only(FakeBus bus, int index) => Assert.IsType<ControlInput>(bus.Published[index]);

    private static void Run(InputModule input, int ticks)
    {
        for (int i = 0; i < ticks; i++)
            input.Tick(new TickContext(i + 1, Step));
    }

    [Fact]
    public void PublishesOneControlInputPerTickForPlaneZero()
    {
        var bus = new FakeBus();
        var input = new InputModule();
        input.Start(bus);

        input.Tick(new TickContext(1, Step));
        Assert.Single(bus.Published);
        input.Tick(new TickContext(2, Step));
        input.Tick(new TickContext(3, Step));

        Assert.Equal(3, bus.Published.Count);
        for (int i = 0; i < 3; i++)
            Assert.Equal(0, Only(bus, i).PlaneId);
    }

    [Fact]
    public void SetStateAloneDoesNotPublish()
    {
        var bus = new FakeBus();
        var input = new InputModule();
        input.Start(bus);
        input.SetState(new DeviceState(1, 0, 0, 0, 0, true));
        input.SetState(new DeviceState(0, 1, 0, 0, 0, true));
        Assert.Empty(bus.Published);
    }

    [Fact]
    public void ThrottleStartsAt0Point7()
    {
        var bus = new FakeBus();
        var input = new InputModule();
        input.Start(bus);
        Assert.Equal(0.7f, input.Throttle);
        input.Tick(new TickContext(1, Step));
        Assert.Equal(0.7f, Only(bus, 0).Throttle, 5);
    }

    [Fact]
    public void ThrottleUpRaisesHalfPerSecond()
    {
        var bus = new FakeBus();
        var input = new InputModule();
        input.Start(bus);
        input.SetState(new DeviceState(0, 0, 0, 1, 0, false));

        Run(input, 30); // 0.5 s
        Assert.Equal(0.95f, input.Throttle, 4);
        Assert.Equal(0.95f, Only(bus, 29).Throttle, 4);
        // Smooth: each step moves a little, never jumps.
        Assert.Equal(0.7f + 0.5f / 60, Only(bus, 0).Throttle, 4);
    }

    [Fact]
    public void ThrottleDownLowersHalfPerSecond()
    {
        var input = new InputModule();
        input.Start(new FakeBus());
        input.SetState(new DeviceState(0, 0, 0, 0, 1, false));
        Run(input, 60); // 1 s
        Assert.Equal(0.2f, input.Throttle, 4);
    }

    [Fact]
    public void PartialThrottleInputScalesRate()
    {
        var input = new InputModule();
        input.Start(new FakeBus());
        input.SetState(new DeviceState(0, 0, 0, 0, 0.5f, false));
        Run(input, 60);
        Assert.Equal(0.45f, input.Throttle, 4);
    }

    [Fact]
    public void ThrottleClampsAtOneAndZero()
    {
        var bus = new FakeBus();
        var input = new InputModule();
        input.Start(bus);
        input.SetState(new DeviceState(0, 0, 0, 1, 0, false));
        Run(input, 600);
        Assert.Equal(1f, input.Throttle);
        Assert.Equal(1f, Only(bus, bus.Published.Count - 1).Throttle);

        input.SetState(new DeviceState(0, 0, 0, 0, 1, false));
        Run(input, 600);
        Assert.Equal(0f, input.Throttle);
        Assert.Equal(0f, Only(bus, bus.Published.Count - 1).Throttle);
    }

    [Fact]
    public void ThrottleHoldsWhenNothingOrBothHeld()
    {
        var input = new InputModule();
        input.Start(new FakeBus());
        Run(input, 60);
        Assert.Equal(0.7f, input.Throttle, 5);
        input.SetState(new DeviceState(0, 0, 0, 1, 1, false));
        Run(input, 60);
        Assert.Equal(0.7f, input.Throttle, 5);
    }

    [Fact]
    public void BadThrottleInputIsIgnored()
    {
        var input = new InputModule();
        input.Start(new FakeBus());
        input.SetState(new DeviceState(0, 0, 0, float.NaN, -5, false));
        Run(input, 60);
        Assert.Equal(0.7f, input.Throttle, 5);
        input.SetState(new DeviceState(0, 0, 0, 10, 0, false));
        Run(input, 6); // capped at full rate: 0.1 s * 0.5
        Assert.Equal(0.75f, input.Throttle, 4);
    }

    [Fact]
    public void LatestStateIsPublished()
    {
        var bus = new FakeBus();
        var input = new InputModule();
        input.Start(bus);
        input.SetState(new DeviceState(-1, -1, -1, 0, 0, false));
        input.SetState(new DeviceState(0.5f, -0.25f, 0.75f, 0, 0, true));
        input.Tick(new TickContext(1, Step));

        var msg = Only(bus, 0);
        Assert.Equal(0.5f, msg.Pitch);
        Assert.Equal(-0.25f, msg.Roll);
        Assert.Equal(0.75f, msg.Yaw);
        Assert.True(msg.Fire);
        Assert.Equal(msg.Pitch, input.Last.Pitch);

        // State persists until the view sets a new one.
        input.Tick(new TickContext(2, Step));
        Assert.True(Only(bus, 1).Fire);
        Assert.Equal(0.5f, Only(bus, 1).Pitch);
    }

    [Fact]
    public void AxesAreClampedInTheMessage()
    {
        var bus = new FakeBus();
        var input = new InputModule();
        input.Start(bus);
        input.SetState(new DeviceState(2, -3, 4, 0, 0, false));
        input.Tick(new TickContext(1, Step));
        var msg = Only(bus, 0);
        Assert.Equal(1f, msg.Pitch);
        Assert.Equal(-1f, msg.Roll);
        Assert.Equal(1f, msg.Yaw);
    }

    [Fact]
    public void GamepadStateHasNoAim()
    {
        var bus = new FakeBus();
        var input = new InputModule();
        input.Start(bus);
        input.SetState(new DeviceState(0.5f, 0.5f, 0, 0, 0, false));
        input.Tick(new TickContext(1, Step));
        var msg = Only(bus, 0);
        Assert.False(msg.HasAim);
        Assert.Equal(Vector3.Zero, msg.AimDirection);
        Assert.Equal(0.5f, msg.Pitch);
    }

    [Fact]
    public void KeyboardNeverHasAim()
    {
        var bus = new FakeBus();
        var input = new InputModule();
        input.Start(bus);
        input.SetKeys(new KeyState { W = true, A = true, Q = true, LeftMouse = true });
        Run(input, 30);
        input.SetKeys(default);
        Run(input, 30);
        Assert.Equal(60, bus.Published.Count);
        for (int i = 0; i < 60; i++)
        {
            Assert.False(Only(bus, i).HasAim);
            Assert.Equal(Vector3.Zero, Only(bus, i).AimDirection);
        }
    }

    [Fact]
    public void KeysFlyThePlaneDirectly()
    {
        var bus = new FakeBus();
        var input = new InputModule();
        input.Start(bus);
        input.SetKeys(new KeyState { W = true, D = true, Q = true, Space = true });
        Run(input, 30); // 0.5 s: well past full deflection
        var msg = Only(bus, 29);
        Assert.Equal(-1f, msg.Pitch); // W = nose down
        Assert.Equal(1f, msg.Roll);
        Assert.Equal(-1f, msg.Yaw);
        Assert.True(msg.Fire);

        input.SetKeys(new KeyState { S = true, A = true, E = true, LeftMouse = true });
        Run(input, 30);
        msg = Only(bus, 59);
        Assert.Equal(1f, msg.Pitch); // S = nose up
        Assert.Equal(-1f, msg.Roll);
        Assert.Equal(1f, msg.Yaw);
        Assert.True(msg.Fire);
    }

    [Fact]
    public void KeyTapMakesASmallCorrection()
    {
        var bus = new FakeBus();
        var input = new InputModule();
        input.Start(bus);
        input.SetKeys(new KeyState { D = true });
        input.Tick(new TickContext(1, Step));
        input.SetKeys(default);
        Run(input, 10);
        float first = Only(bus, 0).Roll;
        Assert.InRange(first, 0.05f, 0.2f); // 1/60 s of 0.15 s
        Assert.Equal(0f, Only(bus, 10).Roll);
    }

    [Fact]
    public void InvertPitchSwapsWAndS()
    {
        var bus = new FakeBus();
        var input = new InputModule();
        Assert.False(InputModule.DefaultInvertPitch);
        Assert.Equal(InputModule.DefaultInvertPitch, input.InvertPitch);
        input.Start(bus);
        input.InvertPitch = true;
        input.SetKeys(new KeyState { W = true });
        Run(input, 30);
        Assert.Equal(1f, Only(bus, 29).Pitch); // W = nose up when inverted
        input.SetKeys(new KeyState { S = true });
        Run(input, 30);
        Assert.Equal(-1f, Only(bus, 59).Pitch);
    }

    [Fact]
    public void ShiftAndCtrlMoveThrottleHalfPerSecond()
    {
        var input = new InputModule();
        input.Start(new FakeBus());
        input.SetKeys(new KeyState { Shift = true });
        Run(input, 30); // 0.5 s
        Assert.Equal(0.95f, input.Throttle, 4);
        input.SetKeys(new KeyState { Ctrl = true });
        Run(input, 60); // 1 s
        Assert.Equal(0.45f, input.Throttle, 4);
        // W and S no longer touch the throttle.
        input.SetKeys(new KeyState { W = true, S = true });
        Run(input, 60);
        Assert.Equal(0.45f, input.Throttle, 4);
    }

    [Fact]
    public void PadLeftStickUpDownMovesThrottleAtARateAndCenterHoldsIt()
    {
        var input = new InputModule();
        input.Start(new FakeBus());
        input.SetState(GamepadFlight.Map(new PadState { LeftY = -1f })); // full push up
        Run(input, 30); // 0.5 s at ThrottleRate
        Assert.Equal(InputModule.StartThrottle + 0.5f * InputModule.ThrottleRate, input.Throttle, 4);
        Assert.Equal(0.95f, input.Throttle, 4);
        input.SetState(GamepadFlight.Map(default)); // released: holds
        Run(input, 120);
        Assert.Equal(0.95f, input.Throttle, 4);
        Assert.Equal(0.95f, input.Last.Throttle, 4);
        input.SetState(GamepadFlight.Map(new PadState { LeftY = 1f })); // full pull down
        Run(input, 60); // 1 s
        Assert.Equal(0.45f, input.Throttle, 4);
        input.SetState(GamepadFlight.Map(new PadState { LeftY = 0.03f })); // back in the dead zone: holds
        Run(input, 60);
        Assert.Equal(0.45f, input.Throttle, 4);
        // Half push: proportional rate, Stick(0.5) * ThrottleRate per second.
        input.SetState(GamepadFlight.Map(new PadState { LeftY = -0.5f }));
        Run(input, 60);
        Assert.Equal(0.45f + GamepadFlight.Stick(0.5f) * InputModule.ThrottleRate, input.Throttle, 4);
        // A small push moves it slowly.
        var before = input.Throttle;
        input.SetState(GamepadFlight.Map(new PadState { LeftY = 0.05f }));
        Run(input, 60);
        Assert.Equal(before - GamepadFlight.Stick(0.05f) * InputModule.ThrottleRate, input.Throttle, 4);
        Assert.InRange(before - input.Throttle, 0.001f, 0.01f);
    }

    [Fact]
    public void PadThrottleClampsAtOneAndZero()
    {
        var input = new InputModule();
        input.Start(new FakeBus());
        input.SetState(GamepadFlight.Map(new PadState { LeftY = -1f }));
        Run(input, 120);
        Assert.Equal(1f, input.Throttle);
        input.SetState(GamepadFlight.Map(new PadState { LeftY = 1f }));
        Run(input, 180);
        Assert.Equal(0f, input.Throttle);
    }

    [Fact]
    public void PadXBAndTriggersLeaveThrottleUnchanged()
    {
        var input = new InputModule();
        input.Start(new FakeBus());
        input.SetState(GamepadFlight.Map(new PadState { X = true }));
        Run(input, 60);
        Assert.Equal(InputModule.StartThrottle, input.Throttle, 5);
        input.SetState(GamepadFlight.Map(new PadState { B = true }));
        Run(input, 60);
        Assert.Equal(InputModule.StartThrottle, input.Throttle, 5);
        input.SetState(GamepadFlight.Map(new PadState { LeftTrigger = 1f, RightTrigger = 1f }));
        Run(input, 60);
        Assert.Equal(InputModule.StartThrottle, input.Throttle, 5);
        Assert.True(input.Last.Fire);
    }

    [Fact]
    public void HoldingRPublishesRepairOnEveryTickAndThrottleStillWorks()
    {
        var bus = new FakeBus();
        var input = new InputModule();
        input.Start(bus);
        input.SetKeys(new KeyState { R = true, Shift = true });
        Run(input, 30); // 0.5 s
        for (int i = 0; i < 30; i++)
            Assert.True(Only(bus, i).Repair);
        Assert.Equal(0.95f, input.Throttle, 4);
        input.SetKeys(default);
        input.Tick(new TickContext(31, Step));
        Assert.False(Only(bus, 30).Repair);
    }

    [Fact]
    public void HoldingPadYPublishesRepairOnEveryTick()
    {
        var bus = new FakeBus();
        var input = new InputModule();
        input.Start(bus);
        input.SetState(GamepadFlight.Map(new PadState { Y = true, LeftY = 1f }));
        Run(input, 20);
        for (int i = 0; i < 20; i++)
            Assert.True(Only(bus, i).Repair);
        Assert.Equal(InputModule.StartThrottle - 20 * InputModule.ThrottleRate / 60f, input.Throttle, 4);
        input.SetState(GamepadFlight.Map(new PadState { A = true }));
        input.Tick(new TickContext(21, Step));
        Assert.False(Only(bus, 20).Repair);
        Assert.True(Only(bus, 20).Fire);
    }

    [Fact]
    public void PadSticksFlyThePlane()
    {
        var bus = new FakeBus();
        var input = new InputModule();
        input.Start(bus);
        input.SetState(GamepadFlight.Map(new PadState { RightY = 0.8f, LeftX = -0.6f, RightX = 0.4f }));
        input.Tick(new TickContext(1, Step));
        var msg = Only(bus, 0);
        Assert.Equal(GamepadFlight.Stick(0.8f), msg.Pitch); // right stick back = nose up
        Assert.Equal(GamepadFlight.Stick(-0.6f), msg.Yaw);
        Assert.Equal(GamepadFlight.Stick(0.4f), msg.Roll);
        Assert.Equal(0.375f, msg.Roll, 4); // (0.4 - 0.04) / 0.96: proportional, not raw
        Assert.False(msg.Fire);
        Assert.Equal(InputModule.StartThrottle, msg.Throttle, 5);
        // Right stick forward = nose down; the left stick does not pitch.
        input.SetState(GamepadFlight.Map(new PadState { RightY = -1f, LeftY = 1f }));
        input.Tick(new TickContext(2, Step));
        Assert.Equal(-1f, Only(bus, 1).Pitch);
        input.SetState(GamepadFlight.Map(new PadState { LeftY = -1f }));
        input.Tick(new TickContext(3, Step));
        Assert.Equal(0f, Only(bus, 2).Pitch);
    }

    [Fact]
    public void PadTakingOverDropsKeySmoothing()
    {
        var bus = new FakeBus();
        var input = new InputModule();
        input.Start(bus);
        input.SetKeys(new KeyState { D = true });
        Run(input, 30);
        input.SetState(new DeviceState(0, 0, 0, 0, 0, false));
        input.Tick(new TickContext(31, Step));
        Assert.Equal(0f, Only(bus, 30).Roll);
        // Back on keys: starts from center again.
        input.SetKeys(new KeyState { D = true });
        input.Tick(new TickContext(32, Step));
        Assert.InRange(Only(bus, 31).Roll, 0.05f, 0.2f);
    }

    [Fact]
    public void NoStateYetIsNeutralWithoutAim()
    {
        var bus = new FakeBus();
        var input = new InputModule();
        input.Start(bus);
        input.Tick(new TickContext(1, Step));
        var msg = Only(bus, 0);
        Assert.Equal(0f, msg.Pitch);
        Assert.Equal(0f, msg.Roll);
        Assert.Equal(0f, msg.Yaw);
        Assert.False(msg.Fire);
        Assert.False(msg.Repair);
        Assert.False(msg.HasAim);
    }

    [Fact]
    public void TickBeforeStartDoesNotThrow()
    {
        var input = new InputModule();
        input.SetState(new DeviceState(0, 0, 0, 1, 0, false));
        input.Tick(new TickContext(1, 1.0));
        Assert.Equal(1f, input.Throttle, 5);
    }

    [Fact]
    public void IsTheModulesOnlyEntryClass()
    {
        int entries = 0;
        foreach (var type in typeof(InputModule).Assembly.GetExportedTypes())
            if (type.IsClass && !type.IsAbstract && typeof(IModule).IsAssignableFrom(type))
                entries++;
        Assert.Equal(1, entries);
    }

    [Fact]
    public void CoreAndContractTargetNet10()
    {
        foreach (var assembly in new[] { typeof(InputModule).Assembly, typeof(ControlInput).Assembly })
            Assert.Equal(".NETCoreApp,Version=v10.0", assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName);
    }

    [Fact]
    public void CoreAndContractDoNotReferenceGodot()
    {
        foreach (var assembly in new[] { typeof(InputModule).Assembly, typeof(ControlInput).Assembly })
            foreach (var name in assembly.GetReferencedAssemblies())
                Assert.DoesNotContain("Godot", name.Name);
    }
}
