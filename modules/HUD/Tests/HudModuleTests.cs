using System;
using System.Numerics;
using Neo.Damage;
using Neo.Flight;
using Neo.Guns;
using Neo.Shell;
using Neo.Targets;
using Neo.World;
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

    private static RepairState Repair(PlanePart part, float progress, int partsLeft, int planeId = 0) =>
        new(planeId, true, part, progress, partsLeft);

    private static RepairState NoRepair(int partsLeft, int planeId = 0) =>
        new(planeId, false, PlanePart.Engine, 0f, partsLeft);

    // One tick of Damage's messages for plane 0, in its order: the parts, then the repair.
    private static void Step(HudModule hud, FakeBus bus, PlaneDamageState parts, RepairState repair)
    {
        bus.Send(parts);
        bus.Send(repair);
        hud.Tick(OneStep);
    }

    [Fact]
    public void Repairing_shows_the_part_progress_and_parts_left()
    {
        var (hud, bus) = Started();
        var parts = new PlaneDamageState(0, 0.9f, 0.5f, 1f, 0.2f, true);
        Step(hud, bus, parts, Repair(PlanePart.Tail, 0.25f, 3));
        Assert.True(hud.Repairing);
        Assert.Equal(PlanePart.Tail, hud.RepairPart);
        Assert.Equal("REPAIRING TAIL", hud.RepairText);
        Assert.Equal(0.25f, hud.RepairProgress);
        Assert.Equal(2, hud.RepairPartsAfter);
        Assert.Equal("2 more", hud.RepairMoreText);
        Assert.False(hud.ShowRepairHint);
        Assert.False(hud.RepairCancelled);

        // The tail done, the next cycle on the left wing; the last part has no "more".
        parts = new PlaneDamageState(0, 0.9f, 0.5f, 1f, 1f, true);
        Step(hud, bus, parts, Repair(PlanePart.LeftWing, 0f, 2));
        Assert.Equal("REPAIRING LEFT WING", hud.RepairText);
        Assert.Equal(0f, hud.RepairProgress);
        Assert.Equal("1 more", hud.RepairMoreText);
        Step(hud, bus, new PlaneDamageState(0, 0.9f, 1f, 1f, 1f, true), Repair(PlanePart.Engine, 0.5f, 1));
        Assert.Equal("REPAIRING ENGINE", hud.RepairText);
        Assert.Equal(0, hud.RepairPartsAfter);
        Assert.Equal("", hud.RepairMoreText);
    }

    [Theory]
    [InlineData(PlanePart.Engine, "REPAIRING ENGINE")]
    [InlineData(PlanePart.LeftWing, "REPAIRING LEFT WING")]
    [InlineData(PlanePart.RightWing, "REPAIRING RIGHT WING")]
    [InlineData(PlanePart.Tail, "REPAIRING TAIL")]
    public void Repairing_texts(PlanePart part, string text) => Assert.Equal(text, HudModule.RepairingText(part));

    [Fact]
    public void Repair_hint_while_damaged_and_not_repairing()
    {
        var (hud, bus) = Started();
        Assert.Equal("Hold Y / R: repair", HudModule.RepairHintText);
        Step(hud, bus, new PlaneDamageState(0, 1f, 1f, 1f, 1f, true), NoRepair(0));
        Assert.False(hud.ShowRepairHint);
        Assert.Equal("", hud.RepairText);

        Step(hud, bus, new PlaneDamageState(0, 1f, 1f, 0.95f, 1f, true), NoRepair(1));
        Assert.True(hud.ShowRepairHint);

        Step(hud, bus, new PlaneDamageState(0, 1f, 1f, 0.95f, 1f, true), Repair(PlanePart.RightWing, 0.1f, 1));
        Assert.False(hud.ShowRepairHint);

        // Downed: no hint, even though the parts are damaged.
        bus.Send(new PlaneDestroyed(0, 4));
        Step(hud, bus, new PlaneDamageState(0, 0f, 1f, 0.95f, 1f, false), NoRepair(2));
        Assert.False(hud.ShowRepairHint);
        bus.Send(new PlaneRespawn(0));
        Step(hud, bus, new PlaneDamageState(0, 1f, 1f, 1f, 1f, true), NoRepair(0));
        Assert.False(hud.ShowRepairHint);
    }

    [Fact]
    public void Letting_go_mid_cycle_shows_repair_cancelled_for_a_moment()
    {
        var (hud, bus) = Started();
        var parts = new PlaneDamageState(0, 0.4f, 1f, 1f, 1f, true);
        Step(hud, bus, parts, Repair(PlanePart.Engine, 0.6f, 1));
        Step(hud, bus, parts, NoRepair(1));
        Assert.False(hud.Repairing);
        Assert.Equal("", hud.RepairText);
        Assert.Equal(0f, hud.RepairProgress);
        Assert.True(hud.RepairCancelled);
        Assert.Equal("Repair cancelled", HudModule.RepairCancelledText);
        Assert.True(hud.ShowRepairHint);

        int ticks = 1;
        while (hud.RepairCancelled)
        {
            Step(hud, bus, parts, NoRepair(1));
            ticks++;
        }
        Assert.Equal((int)Math.Round(HudModule.RepairCancelledSeconds * 60), ticks);

        // Holding again clears it at once.
        Step(hud, bus, parts, Repair(PlanePart.Engine, 0.6f, 1));
        Step(hud, bus, parts, NoRepair(1));
        Assert.True(hud.RepairCancelled);
        Step(hud, bus, parts, Repair(PlanePart.Engine, 0f, 1));
        Assert.False(hud.RepairCancelled);
    }

    [Fact]
    public void Repair_finishing_or_going_down_is_not_cancelled()
    {
        var (hud, bus) = Started();
        Step(hud, bus, new PlaneDamageState(0, 0.4f, 1f, 1f, 1f, true), Repair(PlanePart.Engine, 0.99f, 1));
        Step(hud, bus, new PlaneDamageState(0, 1f, 1f, 1f, 1f, true), NoRepair(0));
        Assert.False(hud.Repairing);
        Assert.False(hud.RepairCancelled);
        Assert.False(hud.ShowRepairHint);

        Step(hud, bus, new PlaneDamageState(0, 0.4f, 1f, 1f, 1f, true), Repair(PlanePart.Engine, 0.5f, 1));
        bus.Send(new PlaneDestroyed(0, 5));
        Step(hud, bus, new PlaneDamageState(0, 0f, 1f, 1f, 1f, false), NoRepair(1));
        Assert.False(hud.Repairing);
        Assert.False(hud.RepairCancelled);
    }

    [Fact]
    public void Other_planes_repairs_are_ignored()
    {
        var (hud, bus) = Started();
        bus.Send(new PlaneDamageState(5, 0.5f, 1f, 1f, 1f, true));
        bus.Send(Repair(PlanePart.Engine, 0.5f, 1, planeId: 5));
        hud.Tick(OneStep);
        Assert.False(hud.Repairing);
        Assert.Equal("", hud.RepairText);
        bus.Send(Repair(PlanePart.Engine, 0.5f, 1));
        bus.Send(NoRepair(1, planeId: 5));
        Assert.True(hud.Repairing);
        Assert.False(hud.RepairCancelled);
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

    [Theory]
    [InlineData(5, -1, "Red 5 crashed")]
    [InlineData(0, -1, "You crashed")]
    [InlineData(2, -1, "Blue 2 crashed")]
    [InlineData(0, 5, "Red 5 forced you into the ground")]
    [InlineData(5, 0, "You forced Red 5 into the ground")]
    [InlineData(6, 2, "Blue 2 forced Red 6 into the ground")]
    [InlineData(3, 1, "Blue 1 forced Blue 3 into the ground")]
    public void Crash_lines(int victim, int shooter, string line) =>
        Assert.Equal(line, HudModule.CrashLine(victim, shooter));

    [Fact]
    public void Destroyed_with_no_shooter_reads_crashed()
    {
        var (hud, bus) = Started();
        bus.Send(new PlaneDestroyed(5, -1));
        bus.Send(new PlaneDestroyed(0, -1));
        Assert.Equal("Red 5 crashed", hud.KillFeedText(0));
        Assert.Equal("You crashed", hud.KillFeedText(1));
        Assert.True(hud.KillFeedIsCrash(0));
        Assert.Equal(-1, hud.KillFeedShooter(1));
        Assert.Equal("Red 5 crashed", HudModule.KillLine(5, -1));
        Assert.Equal(1, hud.Deaths);
        Assert.Equal(0, hud.Kills);
    }

    [Fact]
    public void Crash_forced_by_an_attacker_reads_forced_into_the_ground_in_either_order()
    {
        // Flight's PlaneCrashed and Damage's PlaneDestroyed come in the same step, either first.
        var (hud, bus) = Started();
        bus.Send(new PlaneCrashed(0, Vector3.Zero, 40f));
        bus.Send(new PlaneDestroyed(0, 5));
        bus.Send(new PlaneDestroyed(5, 0));
        bus.Send(new PlaneCrashed(5, Vector3.Zero, 40f));
        Assert.Equal(2, hud.KillFeedCount);
        Assert.Equal("Red 5 forced you into the ground", hud.KillFeedText(0));
        Assert.Equal("You forced Red 5 into the ground", hud.KillFeedText(1));
        Assert.True(hud.KillFeedIsCrash(0));
        Assert.True(hud.KillFeedIsCrash(1));
        Assert.Equal(1, hud.Kills); // the pursuer gets the kill
        Assert.Equal(1, hud.Deaths);
        hud.Tick(OneStep);
        Assert.Equal("You forced Red 5 into the ground", hud.KillFeedText(1));
    }

    [Fact]
    public void Shot_down_plane_hitting_the_ground_later_stays_shot_down()
    {
        var (hud, bus) = Started();
        bus.Send(new PlaneDestroyed(6, 2));
        hud.Tick(OneStep);
        bus.Send(new PlaneCrashed(6, Vector3.Zero, 60f)); // falling wreck lands; Damage sends nothing
        Assert.Equal(1, hud.KillFeedCount);
        Assert.Equal("Blue 2 shot down Red 6", hud.KillFeedText(0));
        Assert.False(hud.KillFeedIsCrash(0));
        // A crash from an earlier step does not mark a later downing of that plane.
        hud.Tick(OneStep);
        bus.Send(new PlaneDestroyed(6, 0));
        Assert.Equal("You shot down Red 6", hud.KillFeedText(1));
    }

    [Fact]
    public void Shoot_down_in_the_same_step_as_another_planes_crash_is_no_crash()
    {
        var (hud, bus) = Started();
        bus.Send(new PlaneCrashed(4, Vector3.Zero, 40f));
        bus.Send(new PlaneDestroyed(5, 0));
        bus.Send(new PlaneDestroyed(4, -1));
        Assert.Equal("You shot down Red 5", hud.KillFeedText(0));
        Assert.False(hud.KillFeedIsCrash(0));
        Assert.Equal("Red 4 crashed", hud.KillFeedText(1));
    }

    private static PlaneState Flying(float x, float y, float z, float verticalSpeed, bool alive = true) =>
        new(0, new Vector3(x, y, z), Quaternion.Identity, new Vector3(0f, verticalSpeed, -50f), 50f, 0.8f, alive);

    [Theory]
    [InlineData(59f, -1f, true)]
    [InlineData(10f, -20f, true)]
    [InlineData(-1f, -5f, true)]
    [InlineData(61f, -10f, false)] // high enough
    [InlineData(30f, 0f, false)]   // level
    [InlineData(30f, -0.2f, false)] // barely sinking: no flicker in level flight
    [InlineData(30f, 5f, false)]   // climbing
    public void Pull_up_rule(float height, float verticalSpeed, bool warn) =>
        Assert.Equal(warn, HudModule.PullUpRule(height, verticalSpeed));

    [Fact]
    public void Pull_up_flashes_faster_when_lower()
    {
        Assert.Equal(HudModule.PullUpSlowHz, HudModule.PullUpFlashHzAt(HudModule.PullUpHeight), 4);
        Assert.Equal(HudModule.PullUpFastHz, HudModule.PullUpFlashHzAt(0f), 4);
        Assert.Equal(HudModule.PullUpFastHz, HudModule.PullUpFlashHzAt(-3f), 4);
        Assert.True(HudModule.PullUpFlashHzAt(10f) > HudModule.PullUpFlashHzAt(30f));
        Assert.True(HudModule.PullUpFlashHzAt(30f) > HudModule.PullUpFlashHzAt(50f));
        Assert.True(HudModule.PullUpFastHz > HudModule.PullUpSlowHz);
    }

    [Fact]
    public void Pull_up_measures_height_above_the_terrain()
    {
        // The same world Y: well clear over the plain, low over a mountain.
        float plain = Terrain.HeightAt(0f, 0f);
        float mountain = Terrain.HeightAt(1350f, 150f);
        Assert.True(mountain > 150f);
        float y = mountain + 40f;
        var (hud, bus) = Started();
        bus.Send(Flying(0f, y, 0f, -5f));
        hud.Tick(OneStep);
        Assert.Equal(y - plain, hud.HeightAboveGround, 3);
        Assert.False(hud.PullUpWarning);

        bus.Send(Flying(1350f, y, 150f, -5f));
        hud.Tick(OneStep);
        Assert.Equal(40f, hud.HeightAboveGround, 3);
        Assert.True(hud.PullUpWarning);
        Assert.Equal(HudModule.PullUpFlashHzAt(40f), hud.PullUpFlashHz, 4);
        Assert.Equal("PULL UP!", HudModule.PullUpText);
    }

    [Fact]
    public void Pull_up_only_while_descending_low_and_flying()
    {
        float ground = Terrain.HeightAt(10f, -20f);
        var (hud, bus) = Started();
        hud.Tick(OneStep);
        Assert.False(hud.PullUpWarning); // no plane yet

        bus.Send(Flying(10f, ground + 30f, -20f, 8f));
        hud.Tick(OneStep);
        Assert.False(hud.PullUpWarning); // climbing
        Assert.Equal(0f, hud.PullUpFlashHz);

        bus.Send(Flying(10f, ground + 30f, -20f, -8f));
        hud.Tick(OneStep);
        Assert.True(hud.PullUpWarning);
        Assert.True(hud.PullUpFlashOn); // lit as soon as it starts

        bus.Send(Flying(10f, ground + 30f, -20f, -8f, alive: false));
        hud.Tick(OneStep);
        Assert.False(hud.PullUpWarning); // a falling wreck gets no warning

        bus.Send(Flying(10f, ground + 30f, -20f, -8f));
        bus.Send(new PlaneDestroyed(0, -1));
        hud.Tick(OneStep);
        Assert.False(hud.PullUpWarning); // down

        bus.Send(new PlaneRespawn(0));
        hud.Tick(OneStep);
        Assert.True(hud.PullUpWarning);

        bus.Send(Plane(50f, 300f, planeId: 3)); // another plane's state changes nothing
        hud.Tick(OneStep);
        Assert.True(hud.PullUpWarning);
    }

    // Counts the lit -> dark flips over 2 s of Ticks held at one height.
    private static int Flashes(float height)
    {
        float ground = Terrain.HeightAt(10f, -20f);
        var (hud, bus) = Started();
        int flips = 0;
        bool was = true;
        for (int i = 0; i < 120; i++)
        {
            bus.Send(Flying(10f, ground + height, -20f, -5f));
            hud.Tick(OneStep);
            Assert.True(hud.PullUpWarning);
            if (was && !hud.PullUpFlashOn)
                flips++;
            was = hud.PullUpFlashOn;
        }
        return flips;
    }

    [Fact]
    public void Pull_up_blinks_at_its_rate()
    {
        int high = Flashes(55f);
        int low = Flashes(5f);
        Assert.InRange(high, 4, 6);   // 2.5 Hz for 2 s
        Assert.InRange(low, 14, 16);  // 7.5 Hz for 2 s
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

    private static PlaneState At(int planeId, Vector3 position, bool alive = true) =>
        new(planeId, position, Quaternion.Identity, new Vector3(0f, 0f, -50f), 50f, 0.8f, alive);

    [Fact]
    public void Markers_for_other_alive_planes_only()
    {
        var (hud, bus) = Started();
        for (int id = 0; id < HudModule.Planes; id++)
            Assert.False(hud.HasMarker(id));
        bus.Send(At(0, Vector3.Zero));
        bus.Send(At(2, new Vector3(100f, 0f, 0f)));
        bus.Send(At(5, new Vector3(0f, 0f, -300f)));
        bus.Send(At(6, new Vector3(0f, 0f, 300f), alive: false));
        bus.Send(At(9, Vector3.Zero));
        Assert.False(hud.HasMarker(0));
        Assert.True(hud.HasMarker(2));
        Assert.True(hud.HasMarker(5));
        Assert.False(hud.HasMarker(6));
        Assert.False(hud.HasMarker(3));
        Assert.False(hud.HasMarker(9));
        Assert.False(hud.HasMarker(-1));

        bus.Send(At(5, new Vector3(0f, 0f, -300f), alive: false));
        Assert.False(hud.HasMarker(5));
        bus.Send(At(6, new Vector3(0f, 0f, 300f)));
        Assert.True(hud.HasMarker(6));
    }

    [Fact]
    public void Plane_positions_interpolate_between_the_last_two_states()
    {
        var (hud, bus) = Started();
        Assert.Equal(Vector3.Zero, hud.PlanePosition(4, 0.5f));
        bus.Send(At(4, new Vector3(0f, 100f, 0f)));
        // First state: nothing before it.
        Assert.Equal(new Vector3(0f, 100f, 0f), hud.PlanePosition(4, 0f));
        bus.Send(At(4, new Vector3(10f, 100f, -20f)));
        Assert.Equal(new Vector3(0f, 100f, 0f), hud.PlanePosition(4, 0f));
        Assert.Equal(new Vector3(5f, 100f, -10f), hud.PlanePosition(4, 0.5f));
        Assert.Equal(new Vector3(10f, 100f, -20f), hud.PlanePosition(4, 1f));
        // The player too (to measure distances from).
        bus.Send(At(0, new Vector3(1f, 2f, 3f)));
        Assert.Equal(new Vector3(1f, 2f, 3f), hud.PlanePosition(0, 0.3f));
    }

    [Fact]
    public void A_respawned_plane_does_not_sweep_from_where_it_went_down()
    {
        var (hud, bus) = Started();
        bus.Send(At(3, new Vector3(500f, 0f, 0f)));
        bus.Send(At(3, new Vector3(500f, 0f, 0f), alive: false));
        bus.Send(At(3, new Vector3(0f, 300f, 0f)));
        Assert.Equal(new Vector3(0f, 300f, 0f), hud.PlanePosition(3, 0f));
        Assert.True(hud.HasMarker(3));
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
    public void Pops_by_anyone_count_and_known_shooters_go_in_the_kill_feed()
    {
        var (hud, bus) = Started();
        bus.Send(new TargetDestroyed(0, Vector3.Zero, 5));
        bus.Send(new TargetDestroyed(1, Vector3.Zero, -1)); // unknown shooter: counted, no line
        bus.Send(new TargetDestroyed(2, Vector3.Zero)); // the 2-argument constructor: -1 too
        bus.Send(new TargetDestroyed(3, Vector3.Zero, 0));
        bus.Send(new PlaneDestroyed(6, 2));
        Assert.Equal(4, hud.BalloonsPopped);
        Assert.Equal(3, hud.KillFeedCount);
        Assert.Equal("Red 5 popped a balloon", hud.KillFeedText(0));
        Assert.True(hud.KillFeedIsBalloon(0));
        Assert.Equal(5, hud.KillFeedShooter(0));
        Assert.Equal(-1, hud.KillFeedVictim(0));
        Assert.Equal("You popped a balloon", hud.KillFeedText(1));
        Assert.Equal(0, hud.KillFeedShooter(1));
        Assert.Equal("Blue 2 shot down Red 6", hud.KillFeedText(2));
        Assert.False(hud.KillFeedIsBalloon(2));
        Assert.Equal(6, hud.KillFeedVictim(2));

        // Balloon lines stay 5 s like the others and share the 6 lines.
        for (int i = 0; i < 299; i++)
            hud.Tick(OneStep);
        Assert.Equal(3, hud.KillFeedCount);
        hud.Tick(OneStep);
        Assert.Equal(0, hud.KillFeedCount);
        for (int i = 0; i < HudModule.KillFeedCapacity; i++)
            bus.Send(new PlaneDestroyed(5, 0));
        bus.Send(new TargetDestroyed(0, Vector3.Zero, 7));
        Assert.Equal(HudModule.KillFeedCapacity, hud.KillFeedCount);
        Assert.Equal("You shot down Red 5", hud.KillFeedText(0));
        Assert.Equal("Red 7 popped a balloon", hud.KillFeedText(HudModule.KillFeedCapacity - 1));
        Assert.True(hud.KillFeedIsBalloon(HudModule.KillFeedCapacity - 1));
        Assert.False(hud.KillFeedIsBalloon(0));
    }

    [Theory]
    [InlineData(0, "You popped a balloon")]
    [InlineData(2, "Blue 2 popped a balloon")]
    [InlineData(5, "Red 5 popped a balloon")]
    [InlineData(7, "Red 7 popped a balloon")]
    public void Balloon_lines(int shooter, string line) => Assert.Equal(line, HudModule.BalloonLine(shooter));

    [Fact]
    public void Boost_badge_follows_the_players_boost_seconds()
    {
        var (hud, bus) = Started();
        Assert.Equal("DAMAGE +50%", HudModule.BoostText);
        Assert.False(hud.Boosted);
        Assert.Equal("", hud.BoostCountdownText);
        Assert.Equal(0, hud.BoostCountdown);

        bus.Send(new GunState(0, 0.2f, false, true, 10, 60f));
        Assert.True(hud.Boosted);
        Assert.Equal(60f, hud.BoostSecondsLeft);
        Assert.Equal("1:00", hud.BoostCountdownText);
        Assert.False(hud.BoostPulsing);

        bus.Send(new GunState(0, 0.2f, false, true, 10, 41.5f));
        Assert.Equal(42, hud.BoostCountdown);
        Assert.Equal("0:42", hud.BoostCountdownText);
        bus.Send(new GunState(0, 0.2f, false, true, 10, 42f));
        Assert.Equal("0:42", hud.BoostCountdownText);
        bus.Send(new GunState(0, 0.2f, false, true, 10, 10.01f));
        Assert.Equal("0:11", hud.BoostCountdownText);
        Assert.False(hud.BoostPulsing);

        // The last 10 s pulse.
        bus.Send(new GunState(0, 0.2f, false, true, 10, 10f));
        Assert.True(hud.BoostPulsing);
        Assert.Equal("0:10", hud.BoostCountdownText);
        bus.Send(new GunState(0, 0.2f, false, true, 10, 1f / 60));
        Assert.True(hud.BoostPulsing);
        Assert.Equal("0:01", hud.BoostCountdownText);

        // Another plane's boost is not the player's.
        bus.Send(new GunState(4, 0f, false, false, 0, 30f));
        Assert.Equal("0:01", hud.BoostCountdownText);

        bus.Send(new GunState(0, 0.2f, false, true, 10, 0f));
        Assert.False(hud.Boosted);
        Assert.False(hud.BoostPulsing);
        Assert.Equal(0f, hud.BoostSecondsLeft);
        Assert.Equal("", hud.BoostCountdownText);

        // The 5-argument constructor: no boost.
        bus.Send(new GunState(0, 0.2f, false, true, 10));
        Assert.False(hud.Boosted);
    }

    [Fact]
    public void Boost_countdown_counts_down_a_whole_boost_by_ticks()
    {
        var (hud, bus) = Started();
        // As Guns publishes it: 60 s at the pop, minus one step a tick.
        string? last = null;
        int changes = 0;
        for (int tick = 0; tick < 3600; tick++)
        {
            bus.Send(new GunState(0, 0f, false, false, 0, (3600 - tick) / 60f));
            hud.Tick(OneStep);
            Assert.True(hud.Boosted);
            Assert.Equal(tick >= 3000, hud.BoostPulsing);
            if (hud.BoostCountdownText != last)
            {
                last = hud.BoostCountdownText;
                changes++;
            }
            if (tick == 1080)
                Assert.Equal("0:42", hud.BoostCountdownText);
        }
        Assert.Equal("0:01", last);
        Assert.Equal(60, changes);
        bus.Send(new GunState(0, 0f, false, false, 0, 0f));
        Assert.False(hud.Boosted);
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(5, "0:05")]
    [InlineData(42, "0:42")]
    [InlineData(60, "1:00")]
    [InlineData(75, "1:15")]
    public void Clock_texts(int seconds, string text) => Assert.Equal(text, HudModule.ClockText(seconds));

    [Fact]
    public void Players_pop_shows_the_damage_pop_up_for_a_moment()
    {
        var (hud, bus) = Started();
        Assert.Equal("+50% damage!", HudModule.BoostPopupText);
        Assert.False(hud.BoostPopup);

        // Others' pops and unknown pops show no pop-up.
        bus.Send(new TargetDestroyed(0, Vector3.Zero, 5));
        bus.Send(new TargetDestroyed(1, Vector3.Zero, 2));
        bus.Send(new TargetDestroyed(2, Vector3.Zero));
        Assert.False(hud.BoostPopup);

        bus.Send(new TargetDestroyed(3, Vector3.Zero, 0));
        Assert.True(hud.BoostPopup);
        Assert.Equal(HudModule.BoostPopupSeconds, hud.BoostPopupSecondsLeft);
        int ticks = 0;
        while (hud.BoostPopup)
        {
            hud.Tick(OneStep);
            ticks++;
        }
        Assert.Equal((int)Math.Round(HudModule.BoostPopupSeconds * 60), ticks);
        Assert.Equal(0.0, hud.BoostPopupSecondsLeft);

        // A new pop restarts it.
        bus.Send(new TargetDestroyed(3, Vector3.Zero, 0));
        for (int i = 0; i < 60; i++)
            hud.Tick(OneStep);
        bus.Send(new TargetDestroyed(4, Vector3.Zero, 0));
        Assert.Equal(HudModule.BoostPopupSeconds, hud.BoostPopupSecondsLeft);
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
        bus.Send(Repair(PlanePart.Engine, 0.5f, 1));
        hud.Tick(OneStep);
        Assert.Empty(bus.Published);
    }
}
