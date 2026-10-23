using System;
using System.Numerics;
using Neo.Flight;
using Neo.Guns;
using Neo.Shell;
using Neo.Targets;
using Xunit;

namespace Neo.HUD.Tests;

public class HudModuleTests
{
    private static readonly TickContext OneStep = new(1, 1.0 / 60);

    private static (HudModule Hud, FakeBus Bus) Started()
    {
        var bus = new FakeBus();
        var hud = new HudModule();
        hud.Start(bus);
        return (hud, bus);
    }

    private static PlaneState Plane(float speed, float altitude, int planeId = 0) =>
        new(planeId, new Vector3(10f, altitude, -20f), Quaternion.Identity, new Vector3(0f, 0f, -speed), speed, 0.8f);

    [Fact]
    public void Starts_at_zero()
    {
        var (hud, _) = Started();
        hud.Tick(OneStep);
        Assert.False(hud.HasPlane);
        Assert.Equal(0f, hud.SpeedKmh);
        Assert.Equal(0f, hud.AltitudeMeters);
        Assert.Equal(0f, hud.Heat);
        Assert.False(hud.Overheated);
        Assert.Equal(0, hud.BalloonsPopped);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(10f, 36f)]
    [InlineData(55f, 198f)]
    [InlineData(62.5f, 225f)]
    public void Speed_is_shown_in_kmh(float metersPerSecond, float kmh)
    {
        var (hud, bus) = Started();
        bus.Send(Plane(metersPerSecond, 300f));
        hud.Tick(OneStep);
        Assert.True(hud.HasPlane);
        Assert.Equal(kmh, hud.SpeedKmh, 3);
    }

    [Fact]
    public void Altitude_is_world_y_in_meters()
    {
        var (hud, bus) = Started();
        bus.Send(Plane(50f, 412.5f));
        hud.Tick(OneStep);
        Assert.Equal(412.5f, hud.AltitudeMeters, 3);
    }

    [Fact]
    public void Other_planes_are_ignored()
    {
        var (hud, bus) = Started();
        bus.Send(Plane(50f, 300f));
        bus.Send(Plane(90f, 900f, planeId: 1));
        bus.Send(new GunState(0, 0.25f, false, true, 10));
        bus.Send(new GunState(1, 1f, true, false, 99));
        hud.Tick(OneStep);
        Assert.Equal(180f, hud.SpeedKmh, 3);
        Assert.Equal(300f, hud.AltitudeMeters, 3);
        Assert.Equal(0.25f, hud.Heat);
        Assert.False(hud.Overheated);
    }

    [Fact]
    public void Previous_values_trail_by_one_tick_for_interpolation()
    {
        var (hud, bus) = Started();
        bus.Send(Plane(50f, 300f));
        hud.Tick(OneStep);
        // First sample: nothing before it, so no sweep up from zero.
        Assert.Equal(180f, hud.PreviousSpeedKmh, 3);
        Assert.Equal(300f, hud.PreviousAltitudeMeters, 3);

        bus.Send(Plane(60f, 310f));
        hud.Tick(OneStep);
        Assert.Equal(180f, hud.PreviousSpeedKmh, 3);
        Assert.Equal(300f, hud.PreviousAltitudeMeters, 3);
        Assert.Equal(216f, hud.SpeedKmh, 3);
        Assert.Equal(310f, hud.AltitudeMeters, 3);
    }

    [Fact]
    public void Heat_and_overheat_follow_the_last_gun_state()
    {
        var (hud, bus) = Started();
        bus.Send(new GunState(0, 0.6f, false, true, 40));
        Assert.Equal(0.6f, hud.Heat);
        Assert.False(hud.Overheated);

        bus.Send(new GunState(0, 1f, true, false, 80));
        Assert.Equal(1f, hud.Heat);
        Assert.True(hud.Overheated);

        bus.Send(new GunState(0, 0.25f, false, false, 80));
        Assert.Equal(0.25f, hud.Heat);
        Assert.False(hud.Overheated);
    }

    [Fact]
    public void Each_target_destroyed_counts_one_pop()
    {
        var (hud, bus) = Started();
        bus.Send(new TargetDestroyed(0, new Vector3(0f, 300f, -400f)));
        Assert.Equal(1, hud.BalloonsPopped);
        bus.Send(new TargetDestroyed(3, Vector3.Zero));
        bus.Send(new TargetDestroyed(0, Vector3.Zero));
        hud.Tick(OneStep);
        Assert.Equal(3, hud.BalloonsPopped);
    }

    [Fact]
    public void Publishes_nothing()
    {
        var (hud, bus) = Started();
        bus.Send(Plane(50f, 300f));
        bus.Send(new TargetDestroyed(0, Vector3.Zero));
        hud.Tick(OneStep);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public void Tick_and_handlers_do_not_allocate()
    {
        var capture = new CaptureBus();
        var quiet = new HudModule();
        quiet.Start(capture);
        var plane = capture.Plane!;
        var gun = capture.Gun!;
        var destroyed = capture.Destroyed!;
        var state = Plane(50f, 300f);
        var heat = new GunState(0, 0.5f, false, true, 1);
        var pop = new TargetDestroyed(1, Vector3.Zero);
        for (int i = 0; i < 10; i++)
        {
            plane.Handle(in state);
            gun.Handle(in heat);
            destroyed.Handle(in pop);
            quiet.Tick(OneStep);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            plane.Handle(in state);
            gun.Handle(in heat);
            destroyed.Handle(in pop);
            quiet.Tick(OneStep);
        }
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        Assert.Equal(1010, quiet.BalloonsPopped);
    }

    private sealed class CaptureBus : IMessageBus
    {
        public IMessageHandler<PlaneState>? Plane;
        public IMessageHandler<GunState>? Gun;
        public IMessageHandler<TargetDestroyed>? Destroyed;

        public void Subscribe<T>(IMessageHandler<T> handler) where T : struct
        {
            if (handler is IMessageHandler<PlaneState> plane)
                Plane = plane;
            if (handler is IMessageHandler<GunState> gun)
                Gun = gun;
            if (handler is IMessageHandler<TargetDestroyed> destroyed)
                Destroyed = destroyed;
        }

        public void Publish<T>(in T message) where T : struct { }
    }
}
