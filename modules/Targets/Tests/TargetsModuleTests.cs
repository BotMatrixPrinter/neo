using System;
using System.Numerics;
using System.Reflection;
using System.Runtime.Versioning;
using Neo.Shell;
using Neo.World;
using Xunit;

namespace Neo.Targets.Tests;

public class TargetsModuleTests
{
    private const double Step = 1.0 / 60;
    private static readonly TickContext OneStep = new(1, Step);

    private static (TargetsModule targets, FakeBus bus) Started()
    {
        var bus = new FakeBus();
        var targets = new TargetsModule();
        targets.Start(bus);
        return (targets, bus);
    }

    private static void Ticks(TargetsModule targets, int count)
    {
        for (int i = 0; i < count; i++)
            targets.Tick(in OneStep);
    }

    [Fact]
    public void FiveBalloonsWithFullHealthAndAHitRadiusOfAboutNineMeters()
    {
        var (targets, _) = Started();
        Assert.Equal(5, TargetsModule.Count);
        for (int id = 0; id < TargetsModule.Count; id++)
        {
            var state = targets.GetState(id);
            Assert.Equal(id, state.TargetId);
            Assert.Equal(15, state.Health);
            Assert.True(state.Alive);
            Assert.InRange(state.Radius, 8f, 10f);
        }
        Assert.Equal(0, targets.Pops);
    }

    [Fact]
    public void StatesArePublishedForEveryTargetEveryTick()
    {
        var (targets, bus) = Started();
        for (int tick = 1; tick <= 3; tick++)
        {
            bus.Clear();
            targets.Tick(new TickContext(tick, Step));
            var states = bus.All<TargetState>();
            Assert.Equal(TargetsModule.Count, states.Count);
            for (int id = 0; id < TargetsModule.Count; id++)
            {
                Assert.Equal(id, states[id].TargetId);
                Assert.Equal(targets.GetState(id).Position, states[id].Position);
            }
        }
    }

    [Fact]
    public void StatesArePublishedWhilePoppedToo()
    {
        var (targets, bus) = Started();
        bus.Send(new DamageTarget(2, 15));
        bus.Clear();
        targets.Tick(in OneStep);
        var states = bus.All<TargetState>();
        Assert.Equal(TargetsModule.Count, states.Count);
        Assert.False(states[2].Alive);
        Assert.Equal(0, states[2].Health);
    }

    [Fact]
    public void BalloonsAreSpreadOverTheAreaAt300To500MetersAndSwaySlightly()
    {
        var (targets, _) = Started();
        float h = WorldBounds.HalfSize;
        float inside = 0.8f * h; // at least 0.2 x HalfSize inside the edge
        Assert.Equal(inside, TargetsModule.MaxReach);
        var first = new Vector3[TargetsModule.Count];
        for (int id = 0; id < TargetsModule.Count; id++)
            first[id] = targets.GetState(id).Position;

        float maxSway = 0;
        for (int i = 0; i < 60 * 60; i++)
        {
            targets.Tick(in OneStep);
            for (int id = 0; id < TargetsModule.Count; id++)
            {
                var p = targets.GetState(id).Position;
                Assert.InRange(p.X, -inside - 2f, inside + 2f);
                Assert.InRange(p.Z, -inside - 2f, inside + 2f);
                Assert.InRange(p.Y, 300f, 500f);
                float sway = Vector3.Distance(p, first[id]);
                Assert.True(sway < 5f, $"balloon {id} drifted {sway} m");
                maxSway = MathF.Max(maxSway, sway);
            }
        }
        Assert.True(maxSway > 0.5f, "balloons should sway");

        // Tether points (and anchors) inside the area too, with the edge margin.
        for (int id = 0; id < TargetsModule.Count; id++)
        {
            var anchor = TargetsModule.Anchor(id);
            Assert.InRange(anchor.X, -inside, inside);
            Assert.InRange(anchor.Z, -inside, inside);
        }

        for (int a = 0; a < TargetsModule.Count; a++)
            for (int b = a + 1; b < TargetsModule.Count; b++)
            {
                var flatA = new Vector2(first[a].X, first[a].Z);
                var flatB = new Vector2(first[b].X, first[b].Z);
                float apart = Vector2.Distance(flatA, flatB);
                Assert.True(apart > 0.4f * h, $"balloons {a} and {b} are only {apart} m apart");
            }

        // Both sides (the teams start on -Z and +Z) get two balloons, one is near the center.
        int south = 0, north = 0, central = 0;
        for (int id = 0; id < TargetsModule.Count; id++)
        {
            var p = first[id];
            if (p.Z < -0.3f * h) south++;
            if (p.Z > 0.3f * h) north++;
            if (new Vector2(p.X, p.Z).Length() < 0.25f * h) central++;
        }
        Assert.Equal(2, south);
        Assert.Equal(2, north);
        Assert.Equal(1, central);
    }

    [Fact]
    public void BalloonsStayOverDryGroundAndAtLeast150MetersAboveTheTerrain()
    {
        var (targets, _) = Started();
        Assert.Equal(150f, TargetsModule.MinClearance);
        for (int i = 0; i < 60 * 60; i++)
        {
            targets.Tick(in OneStep);
            for (int id = 0; id < TargetsModule.Count; id++)
            {
                var p = targets.GetState(id).Position;
                Assert.False(Terrain.IsWater(p.X, p.Z), $"balloon {id} is over water at ({p.X}, {p.Z})");
                float clearance = p.Y - Terrain.HeightAt(p.X, p.Z);
                Assert.True(clearance >= 150f, $"balloon {id} is only {clearance} m above the terrain");
            }
        }
    }

    [Fact]
    public void EachCableIsAnchoredOnTheTerrainUnderItsBalloon()
    {
        var (targets, _) = Started();
        var views = new BalloonViews(targets);
        var anchors = new Vector3[TargetsModule.Count];
        for (int id = 0; id < TargetsModule.Count; id++)
        {
            var anchor = TargetsModule.Anchor(id);
            anchors[id] = anchor;
            Assert.Equal(Terrain.HeightAt(anchor.X, anchor.Z), anchor.Y);
            Assert.False(Terrain.IsWater(anchor.X, anchor.Z), $"balloon {id}'s winch stands in water");
            var p = targets.GetState(id).Position;
            Assert.True(new Vector2(p.X - anchor.X, p.Z - anchor.Z).Length() < 3f, $"balloon {id} is not above its anchor");
        }

        for (int i = 0; i < 600; i++)
        {
            targets.Tick(in OneStep);
            views.Update(1f / 60);
        }
        for (int id = 0; id < TargetsModule.Count; id++)
        {
            Assert.Equal(anchors[id], views.CableBottom(id)); // the anchor stays put
            Assert.Equal(views.Position(id) + BalloonViews.CableAttach, views.CableTop(id)); // the top sways along
            Assert.True(views.CableTop(id).Y - views.CableBottom(id).Y > 140f);
        }
    }

    // Grid points over the area inside the edge margin, every 5 % of HalfSize.
    private static System.Collections.Generic.IEnumerable<(float X, float Z)> AreaSamples()
    {
        float reach = TargetsModule.MaxReach, step = 0.05f * WorldBounds.HalfSize;
        for (float x = -reach; x <= reach + 0.1f; x += step)
            for (float z = -reach; z <= reach + 0.1f; z += step)
                yield return (x, z);
    }

    [Fact]
    public void ASpotOverWaterIsMovedSlightlyToDryGroundInsideTheArea()
    {
        float reach = TargetsModule.MaxReach;
        int wet = 0;
        var spots = new System.Collections.Generic.List<(float X, float Z)>(AreaSamples());
        spots.Add((Terrain.RiverX(0f), 0f)); // on the river's center line
        foreach (var (x, z) in spots)
        {
            if (!Terrain.IsWater(x, z))
                continue;
            wet++;
            var spot = TargetsModule.PlaceOnDryGround(new Vector3(x, 400f, z));
            Assert.False(Terrain.IsWater(spot.X, spot.Z), $"({x}, {z}) stayed over water");
            Assert.True(Terrain.WaterDepthAt(spot.X, spot.Z) == 0f);
            float moved = new Vector2(spot.X - x, spot.Z - z).Length();
            Assert.InRange(moved, 1f, 600f);
            Assert.InRange(spot.X, -reach, reach);
            Assert.InRange(spot.Z, -reach, reach);
            Assert.True(spot.Y >= 400f);
            Assert.True(spot.Y - Terrain.HeightAt(spot.X, spot.Z) >= 150f);
        }
        Assert.True(wet > 3, "the area should have some water (river, lakes) to test against");
    }

    [Fact]
    public void ASpotOutsideTheAreaIsBroughtInside()
    {
        float h = WorldBounds.HalfSize, reach = TargetsModule.MaxReach;
        foreach (var (fx, fz) in new[] { (0.95f, 0.95f), (-0.9f, 0f), (0f, 0.99f), (-0.85f, -0.97f) })
        {
            var spot = TargetsModule.PlaceOnDryGround(new Vector3(fx * h, 400f, fz * h));
            Assert.InRange(spot.X, -reach, reach);
            Assert.InRange(spot.Z, -reach, reach);
            Assert.False(Terrain.IsWater(spot.X, spot.Z));
            Assert.True(spot.Y - Terrain.HeightAt(spot.X, spot.Z) >= 150f);
        }
    }

    [Fact]
    public void ASpotOnDryGroundStaysAndOneTooLowIsRaisedClearOfTheTerrain()
    {
        // A dry spot (no water within 60 m) on low ground: it stays as wanted.
        (float X, float Z)? dry = null;
        (float X, float Z) hill = (0f, 0f);
        float hillHeight = float.MinValue;
        foreach (var (x, z) in AreaSamples())
        {
            bool wetNear = false;
            for (float dx = -60f; dx <= 60f && !wetNear; dx += 10f)
                for (float dz = -60f; dz <= 60f && !wetNear; dz += 10f)
                    wetNear = Terrain.IsWater(x + dx, z + dz);
            if (wetNear)
                continue;
            float y = Terrain.HeightAt(x, z);
            if (dry == null && y < 100f)
                dry = (x, z);
            if (y > hillHeight)
            {
                hillHeight = y;
                hill = (x, z);
            }
        }
        Assert.NotNull(dry);
        var wanted = new Vector3(dry.Value.X, 400f, dry.Value.Z);
        Assert.Equal(wanted, TargetsModule.PlaceOnDryGround(wanted));

        // The highest dry ground in the area (found by sampling): a balloon wanted below 150 m
        // above it goes up, and stays over it.
        Assert.True(hillHeight > 20f, $"the area's highest dry ground is only {hillHeight} m");
        var peak = TargetsModule.PlaceOnDryGround(new Vector3(hill.X, hillHeight + 100f, hill.Z));
        Assert.Equal(hill.X, peak.X);
        Assert.Equal(hill.Z, peak.Z);
        Assert.True(peak.Y - Terrain.HeightAt(peak.X, peak.Z) >= 150f);
        Assert.True(peak.Y > hillHeight + 100f);
    }

    [Fact]
    public void DamageLowersHealth()
    {
        var (targets, bus) = Started();
        bus.Send(new DamageTarget(1, 4));
        Assert.Equal(11, targets.GetState(1).Health);
        Assert.True(targets.GetState(1).Alive);
        Assert.Equal(15, targets.GetState(0).Health);

        bus.Clear();
        targets.Tick(in OneStep);
        Assert.Equal(11, bus.All<TargetState>()[1].Health);
        Assert.Empty(bus.All<TargetDestroyed>());
    }

    [Theory]
    [InlineData(-1, 5)]
    [InlineData(5, 5)]
    [InlineData(0, 0)]
    [InlineData(0, -3)]
    public void BadDamageIsIgnored(int targetId, int amount)
    {
        var (targets, bus) = Started();
        bus.Send(new DamageTarget(targetId, amount));
        for (int id = 0; id < TargetsModule.Count; id++)
            Assert.Equal(15, targets.GetState(id).Health);
    }

    [Fact]
    public void ReachingZeroPopsItAndPublishesOneTargetDestroyed()
    {
        var (targets, bus) = Started();
        Ticks(targets, 10);
        var where = targets.GetState(3).Position;
        bus.Clear();

        bus.Send(new DamageTarget(3, 10));
        Assert.Empty(bus.All<TargetDestroyed>());
        bus.Send(new DamageTarget(3, 10)); // overkill: health stops at 0
        var destroyed = Assert.Single(bus.All<TargetDestroyed>());
        Assert.Equal(3, destroyed.TargetId);
        Assert.Equal(where, destroyed.Position);
        Assert.Equal(-1, destroyed.ShooterPlaneId); // the old constructor: shooter unknown

        var state = targets.GetState(3);
        Assert.False(state.Alive);
        Assert.Equal(0, state.Health);
        Assert.Equal(1, targets.Pops);
    }

    [Fact]
    public void TheShooterOfTheLastHitIsRecordedOnThePop()
    {
        var (targets, bus) = Started();
        bus.Send(new DamageTarget(2, 10, 5));
        bus.Send(new DamageTarget(2, 3, 1));
        Assert.Empty(bus.All<TargetDestroyed>());
        bus.Send(new DamageTarget(2, 4, 6)); // takes the last health
        bus.Send(new DamageTarget(2, 4, 7)); // too late: already popped

        var destroyed = Assert.Single(bus.All<TargetDestroyed>());
        Assert.Equal(2, destroyed.TargetId);
        Assert.Equal(6, destroyed.ShooterPlaneId);
        Assert.Equal(1, targets.Pops);
    }

    [Fact]
    public void AnUnknownShooterIsMinusOne()
    {
        var (_, bus) = Started();
        var hit = new DamageTarget(1, 15);
        Assert.Equal(-1, hit.ShooterPlaneId);
        Assert.Equal(1, hit.TargetId);
        Assert.Equal(15, hit.Amount);
        bus.Send(hit);
        Assert.Equal(-1, Assert.Single(bus.All<TargetDestroyed>()).ShooterPlaneId);

        bus.Send(new DamageTarget(0, 15, -1));
        Assert.Equal(-1, bus.All<TargetDestroyed>()[1].ShooterPlaneId);

        var old = new TargetDestroyed(4, new Vector3(1, 2, 3));
        Assert.Equal(-1, old.ShooterPlaneId);
        Assert.Equal(4, old.TargetId);
        Assert.Equal(new Vector3(1, 2, 3), old.Position);
        Assert.Equal(3, new TargetDestroyed(4, Vector3.Zero, 3).ShooterPlaneId);
    }

    [Fact]
    public void APoppedBalloonIgnoresMoreDamageAndPopsOnlyOnce()
    {
        var (targets, bus) = Started();
        bus.Send(new DamageTarget(0, 15));
        bus.Send(new DamageTarget(0, 15));
        bus.Send(new DamageTarget(0, 1));
        Ticks(targets, 120);
        bus.Send(new DamageTarget(0, 15));

        Assert.Single(bus.All<TargetDestroyed>());
        Assert.Equal(1, targets.Pops);
        Assert.Equal(0, targets.GetState(0).Health);
    }

    [Fact]
    public void ItComesBackWithFullHealthSixtySecondsLaterAtTheSameSpot()
    {
        var (targets, bus) = Started();
        Ticks(targets, 30);
        var before = targets.GetState(4).Position;
        bus.Send(new DamageTarget(4, 15));

        Assert.Equal(60, TargetsModule.RespawnSeconds);
        Ticks(targets, 600); // 10 s: still down (it used to come back here)
        Assert.False(targets.GetState(4).Alive);
        Ticks(targets, 2999); // 59.983 s
        Assert.False(targets.GetState(4).Alive);
        Assert.Equal(0, targets.GetState(4).Health);

        bus.Clear();
        targets.Tick(in OneStep); // 60 s
        var back = targets.GetState(4);
        Assert.True(back.Alive);
        Assert.Equal(15, back.Health);
        Assert.Equal(15, bus.All<TargetState>()[4].Health);
        Assert.True(Vector3.Distance(before, back.Position) < 5f, "it comes back at the same spot");
        Assert.Empty(bus.All<TargetDestroyed>());
    }

    [Fact]
    public void ARespawnedBalloonCanPopAgainAndPopsAreCounted()
    {
        var (targets, bus) = Started();
        bus.Send(new DamageTarget(1, 15));
        bus.Send(new DamageTarget(2, 15));
        Ticks(targets, 3600);
        bus.Send(new DamageTarget(1, 15));

        Assert.Equal(3, targets.Pops);
        var destroyed = bus.All<TargetDestroyed>();
        Assert.Equal(3, destroyed.Count);
        Assert.Equal(new[] { 1, 2, 1 }, new[] { destroyed[0].TargetId, destroyed[1].TargetId, destroyed[2].TargetId });
    }

    [Fact]
    public void TheViewHidesAPoppedBalloonAndShowsItAgainAtItsSpotWhenItComesBack()
    {
        var (targets, bus) = Started();
        var views = new BalloonViews(targets);
        const float frame = 1f / 60;
        for (int id = 0; id < TargetsModule.Count; id++)
        {
            Assert.True(views.ShapeVisible(id));
            Assert.False(views.PopPlaying(id));
        }

        Ticks(targets, 30);
        views.Update(frame);
        var spot = views.Position(3);
        Assert.Equal(targets.GetState(3).Position, spot);

        bus.Send(new DamageTarget(3, 15, 2));
        targets.Tick(in OneStep);
        views.Update(frame);
        Assert.False(views.ShapeVisible(3)); // body, basket and cable hidden
        Assert.True(views.PopPlaying(3));
        Assert.True(views.ShapeVisible(0));
        Assert.False(views.PopPlaying(0));

        // The pop effect plays for PopSeconds, then the balloon is just gone.
        for (int f = 0; f < 120; f++)
        {
            targets.Tick(in OneStep);
            views.Update(frame);
        }
        Assert.False(views.ShapeVisible(3));
        Assert.False(views.PopPlaying(3));

        // Still gone just before 60 s, back (shape, cable and all) at its spot at 60 s.
        Ticks(targets, 3600 - 122);
        views.Update(frame);
        Assert.False(views.ShapeVisible(3));
        targets.Tick(in OneStep);
        views.Update(frame);
        Assert.True(targets.GetState(3).Alive);
        Assert.True(views.ShapeVisible(3));
        Assert.False(views.PopPlaying(3));
        Assert.Equal(targets.GetState(3).Position, views.Position(3));
        Assert.True(Vector3.Distance(spot, views.Position(3)) < 5f, "it is drawn again at its spot");

        // And it can pop again.
        bus.Send(new DamageTarget(3, 15));
        targets.Tick(in OneStep);
        views.Update(frame);
        Assert.False(views.ShapeVisible(3));
        Assert.True(views.PopPlaying(3));
    }

    [Fact]
    public void TickBeforeStartDoesNotThrow()
    {
        var targets = new TargetsModule();
        targets.Tick(in OneStep);
        Assert.True(targets.GetState(0).Alive);
    }

    [Fact]
    public void IsTheModulesOnlyEntryClass()
    {
        int entries = 0;
        foreach (var type in typeof(TargetsModule).Assembly.GetExportedTypes())
            if (type.IsClass && !type.IsAbstract && typeof(IModule).IsAssignableFrom(type))
                entries++;
        Assert.Equal(1, entries);
    }

    [Fact]
    public void CoreAndContractTargetNet10()
    {
        foreach (var assembly in new[] { typeof(TargetsModule).Assembly, typeof(TargetState).Assembly })
            Assert.Equal(".NETCoreApp,Version=v10.0", assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName);
    }

    [Fact]
    public void CoreAndContractDoNotReferenceGodot()
    {
        foreach (var assembly in new[] { typeof(TargetsModule).Assembly, typeof(TargetState).Assembly })
            foreach (var name in assembly.GetReferencedAssemblies())
                Assert.DoesNotContain("Godot", name.Name);
    }
}
