using System;
using System.Numerics;
using Neo.Damage;
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
        Assert.Equal(0, hud.Kills);
        Assert.Equal(0, hud.Deaths);
        Assert.False(hud.PlayerDown);
        Assert.Equal(0, hud.RespawnCountdown);
        Assert.Equal("", hud.RespawnText);
        Assert.Equal(0, hud.KillFeedCount);
        Assert.Equal(PartLevel.Good, hud.Level(PlanePart.Engine));
        Assert.Equal(1f, hud.Tail);
    }

    [Theory]
    [InlineData(1f, PartLevel.Good)]
    [InlineData(0.6f, PartLevel.Good)]
    [InlineData(0.59f, PartLevel.Worn)]
    [InlineData(0.3f, PartLevel.Worn)]
    [InlineData(0.29f, PartLevel.Critical)]
    [InlineData(0.01f, PartLevel.Critical)]
    [InlineData(0f, PartLevel.Destroyed)]
    public void Part_levels_follow_health(float health, PartLevel level) =>
        Assert.Equal(level, HudModule.LevelOf(health));

    [Fact]
    public void Damage_panel_shows_the_players_parts_only()
    {
        var (hud, bus) = Started();
        bus.Send(new PlaneDamageState(0, 0.8f, 0.5f, 0.2f, 0f, false));
        bus.Send(new PlaneDamageState(4, 0f, 0f, 0f, 0f, false));
        hud.Tick(OneStep);
        Assert.Equal(0.8f, hud.Engine);
        Assert.Equal(0.5f, hud.LeftWing);
        Assert.Equal(0.2f, hud.RightWing);
        Assert.Equal(0f, hud.Tail);
        Assert.Equal(PartLevel.Good, hud.Level(PlanePart.Engine));
        Assert.Equal(PartLevel.Worn, hud.Level(PlanePart.LeftWing));
        Assert.Equal(PartLevel.Critical, hud.Level(PlanePart.RightWing));
        Assert.Equal(PartLevel.Destroyed, hud.Level(PlanePart.Tail));

        bus.Send(new PlaneDamageState(0, 1f, 1f, 1f, 1f, true));
        Assert.Equal(PartLevel.Good, hud.Level(PlanePart.Tail));
    }

    [Theory]
    [InlineData(0, "You")]
    [InlineData(1, "Blue 1")]
    [InlineData(3, "Blue 3")]
    [InlineData(4, "Red 4")]
    [InlineData(7, "Red 7")]
    public void Plane_names(int id, string name) => Assert.Equal(name, HudModule.PlaneName(id));

    [Theory]
    [InlineData(5, 0, "You shot down Red 5")]
    [InlineData(0, 4, "Red 4 shot you down")]
    [InlineData(6, 2, "Blue 2 shot down Red 6")]
    [InlineData(1, 7, "Red 7 shot down Blue 1")]
    [InlineData(2, 0, "You shot down Blue 2")]
    [InlineData(5, 5, "Red 5 went down")]
    [InlineData(0, 0, "You went down")]
    public void Kill_feed_lines(int victim, int shooter, string line)
    {
        var (hud, bus) = Started();
        bus.Send(new PlaneDestroyed(victim, shooter));
        Assert.Equal(1, hud.KillFeedCount);
        Assert.Equal(line, hud.KillFeedText(0));
        Assert.Equal(victim, hud.KillFeedVictim(0));
        Assert.Equal(shooter, hud.KillFeedShooter(0));
        Assert.Equal(line, HudModule.KillLine(victim, shooter));
    }

    [Fact]
    public void Kill_feed_lines_stay_five_seconds_oldest_first()
    {
        var (hud, bus) = Started();
        bus.Send(new PlaneDestroyed(5, 0));
        for (int i = 0; i < 60; i++)
            hud.Tick(OneStep);
        bus.Send(new PlaneDestroyed(0, 4));
        Assert.Equal(2, hud.KillFeedCount);
        Assert.Equal("You shot down Red 5", hud.KillFeedText(0));
        Assert.Equal("Red 4 shot you down", hud.KillFeedText(1));
        Assert.Equal(4.0, hud.KillFeedSecondsLeft(0), 6);
        Assert.Equal(5.0, hud.KillFeedSecondsLeft(1), 6);

        for (int i = 0; i < 239; i++)
            hud.Tick(OneStep);
        Assert.Equal(2, hud.KillFeedCount);
        hud.Tick(OneStep); // 5 s for the first line
        Assert.Equal(1, hud.KillFeedCount);
        Assert.Equal("Red 4 shot you down", hud.KillFeedText(0));

        for (int i = 0; i < 60; i++)
            hud.Tick(OneStep);
        Assert.Equal(0, hud.KillFeedCount);
    }

    [Fact]
    public void Kill_feed_drops_the_oldest_when_full()
    {
        var (hud, bus) = Started();
        for (int i = 0; i < HudModule.KillFeedCapacity + 2; i++)
            bus.Send(new PlaneDestroyed(4 + i % 4, i % 4));
        Assert.Equal(HudModule.KillFeedCapacity, hud.KillFeedCount);
        // The first two (Red 4 by You, Red 5 by Blue 1) were pushed out.
        Assert.Equal("Blue 2 shot down Red 6", hud.KillFeedText(0));
        Assert.Equal("Blue 3 shot down Red 7", hud.KillFeedText(HudModule.KillFeedCapacity - 1));
    }

    [Fact]
    public void Kills_and_deaths_of_the_player()
    {
        var (hud, bus) = Started();
        bus.Send(new PlaneDestroyed(5, 0));
        bus.Send(new PlaneDestroyed(7, 0));
        bus.Send(new PlaneDestroyed(6, 2)); // a wingman's kill
        bus.Send(new PlaneDestroyed(1, 0)); // friendly fire is no kill
        bus.Send(new PlaneDestroyed(0, 4));
        bus.Send(new PlaneDestroyed(3, 6)); // a wingman down
        Assert.Equal(2, hud.Kills);
        Assert.Equal(1, hud.Deaths);
        bus.Send(new PlaneRespawn(0));
        bus.Send(new PlaneDestroyed(0, 0)); // downed itself still counts as a death
        Assert.Equal(2, hud.Kills);
        Assert.Equal(2, hud.Deaths);
    }

    [Fact]
    public void Shot_down_counts_down_to_the_respawn()
    {
        var (hud, bus) = Started();
        bus.Send(new PlaneDestroyed(5, 4));
        Assert.False(hud.PlayerDown);

        bus.Send(new PlaneDestroyed(0, 6));
        Assert.True(hud.PlayerDown);
        Assert.Equal(5, hud.RespawnCountdown);
        Assert.Equal("Back in the air in 5", hud.RespawnText);

        hud.Tick(OneStep);
        Assert.Equal(5, hud.RespawnCountdown);
        for (int i = 1; i < 60; i++)
            hud.Tick(OneStep);
        Assert.Equal(4.0, hud.RespawnSecondsLeft, 6);
        Assert.Equal("Back in the air in 4", hud.RespawnText);
        hud.Tick(OneStep);
        Assert.Equal("Back in the air in 4", hud.RespawnText);
        for (int i = 0; i < 59; i++)
            hud.Tick(OneStep);
        Assert.Equal("Back in the air in 3", hud.RespawnText);
        for (int i = 0; i < 120; i++)
            hud.Tick(OneStep);
        Assert.Equal("Back in the air in 1", hud.RespawnText);
        for (int i = 0; i < 59; i++)
            hud.Tick(OneStep);
        Assert.Equal("Back in the air in 1", hud.RespawnText);
        hud.Tick(OneStep); // 300 steps: 5 s
        Assert.True(hud.PlayerDown);
        Assert.Equal(1, hud.RespawnCountdown);

        bus.Send(new PlaneRespawn(4)); // another plane: still down
        Assert.True(hud.PlayerDown);
        bus.Send(new PlaneRespawn(0));
        Assert.False(hud.PlayerDown);
        Assert.Equal(0, hud.RespawnCountdown);
        Assert.Equal("", hud.RespawnText);
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
        bus.Send(new PlaneDamageState(0, 0.5f, 1f, 1f, 1f, true));
        bus.Send(new PlaneDestroyed(0, 5));
        bus.Send(new PlaneRespawn(0));
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
        var damage = capture.Damage!;
        var down = capture.Down!;
        var respawn = capture.Respawn!;
        var parts = new PlaneDamageState(0, 0.7f, 0.4f, 0.2f, 0f, false);
        var kill = new PlaneDestroyed(5, 0);
        var death = new PlaneDestroyed(0, 4);
        var back = new PlaneRespawn(0);
        var state = Plane(50f, 300f);
        var heat = new GunState(0, 0.5f, false, true, 1);
        var pop = new TargetDestroyed(1, Vector3.Zero);
        for (int i = 0; i < 10; i++)
        {
            plane.Handle(in state);
            gun.Handle(in heat);
            destroyed.Handle(in pop);
            damage.Handle(in parts);
            down.Handle(in kill);
            down.Handle(in death);
            respawn.Handle(in back);
            quiet.Tick(OneStep);
            _ = quiet.KillFeedText(0);
            _ = quiet.RespawnText;
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            plane.Handle(in state);
            gun.Handle(in heat);
            destroyed.Handle(in pop);
            damage.Handle(in parts);
            down.Handle(in kill);
            down.Handle(in death);
            _ = quiet.Level(PlanePart.Tail);
            _ = quiet.KillFeedText(quiet.KillFeedCount - 1);
            _ = quiet.RespawnText;
            respawn.Handle(in back);
            quiet.Tick(OneStep);
        }
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        Assert.Equal(1010, quiet.BalloonsPopped);
        Assert.Equal(1010, quiet.Kills);
        Assert.Equal(1010, quiet.Deaths);
    }

    private sealed class CaptureBus : IMessageBus
    {
        public IMessageHandler<PlaneState>? Plane;
        public IMessageHandler<GunState>? Gun;
        public IMessageHandler<TargetDestroyed>? Destroyed;
        public IMessageHandler<PlaneDamageState>? Damage;
        public IMessageHandler<PlaneDestroyed>? Down;
        public IMessageHandler<PlaneRespawn>? Respawn;

        public void Subscribe<T>(IMessageHandler<T> handler) where T : struct
        {
            if (handler is IMessageHandler<PlaneState> plane)
                Plane = plane;
            if (handler is IMessageHandler<GunState> gun)
                Gun = gun;
            if (handler is IMessageHandler<TargetDestroyed> destroyed)
                Destroyed = destroyed;
            if (handler is IMessageHandler<PlaneDamageState> damage)
                Damage = damage;
            if (handler is IMessageHandler<PlaneDestroyed> down)
                Down = down;
            if (handler is IMessageHandler<PlaneRespawn> respawn)
                Respawn = respawn;
        }

        public void Publish<T>(in T message) where T : struct { }
    }
}
