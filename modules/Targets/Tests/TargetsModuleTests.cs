using System;
using System.Numerics;
using System.Reflection;
using System.Runtime.Versioning;
using Neo.Shell;
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
                Assert.InRange(p.X, -2000f, 2000f);
                Assert.InRange(p.Z, -2000f, 2000f);
                Assert.InRange(p.Y, 300f, 500f);
                float sway = Vector3.Distance(p, first[id]);
                Assert.True(sway < 5f, $"balloon {id} drifted {sway} m");
                maxSway = MathF.Max(maxSway, sway);
            }
        }
        Assert.True(maxSway > 0.5f, "balloons should sway");

        for (int a = 0; a < TargetsModule.Count; a++)
            for (int b = a + 1; b < TargetsModule.Count; b++)
            {
                var flatA = new Vector2(first[a].X, first[a].Z);
                var flatB = new Vector2(first[b].X, first[b].Z);
                Assert.True(Vector2.Distance(flatA, flatB) > 800f, $"balloons {a} and {b} are too close");
            }
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

        var state = targets.GetState(3);
        Assert.False(state.Alive);
        Assert.Equal(0, state.Health);
        Assert.Equal(1, targets.Pops);
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
    public void ItComesBackWithFullHealthTenSecondsLaterAtTheSameSpot()
    {
        var (targets, bus) = Started();
        Ticks(targets, 30);
        var before = targets.GetState(4).Position;
        bus.Send(new DamageTarget(4, 15));

        Ticks(targets, 599); // 9.983 s
        Assert.False(targets.GetState(4).Alive);
        Assert.Equal(0, targets.GetState(4).Health);

        bus.Clear();
        targets.Tick(in OneStep); // 10 s
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
        Ticks(targets, 600);
        bus.Send(new DamageTarget(1, 15));

        Assert.Equal(3, targets.Pops);
        var destroyed = bus.All<TargetDestroyed>();
        Assert.Equal(3, destroyed.Count);
        Assert.Equal(new[] { 1, 2, 1 }, new[] { destroyed[0].TargetId, destroyed[1].TargetId, destroyed[2].TargetId });
    }

    [Fact]
    public void TickBeforeStartDoesNotThrow()
    {
        var targets = new TargetsModule();
        targets.Tick(in OneStep);
        Assert.True(targets.GetState(0).Alive);
    }

    [Fact]
    public void TickAndDamageDoNotAllocate()
    {
        var bus = new QuietBus();
        var targets = new TargetsModule();
        targets.Start(bus);
        Assert.NotNull(bus.Damage);
        var hit = new DamageTarget(0, 15);
        targets.Tick(in OneStep);
        bus.Damage!.Handle(in hit);
        Ticks(targets, 600);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10; i++)
        {
            bus.Damage.Handle(in hit);
            Ticks(targets, 600);
        }
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        Assert.Equal(11, bus.Destroyed);
        Assert.Equal(TargetsModule.Count * 6601, bus.StatesPublished);
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
