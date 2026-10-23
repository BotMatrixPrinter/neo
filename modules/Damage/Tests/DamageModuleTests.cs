using System;
using System.Numerics;
using System.Reflection;
using System.Runtime.Versioning;
using Neo.Flight;
using Neo.Shell;
using Xunit;

namespace Neo.Damage.Tests;

public class DamageModuleTests
{
    private const double Step = 1.0 / 60;
    private const float Bullet = 0.04f;

    private static (DamageModule, FakeBus) Started()
    {
        var bus = new FakeBus();
        var damage = new DamageModule();
        damage.Start(bus);
        return (damage, bus);
    }

    private static void Tick(DamageModule damage, int steps = 1)
    {
        for (int i = 0; i < steps; i++)
            damage.Tick(new TickContext(i + 1, Step));
    }

    private static PlaneState Flying(int id) =>
        new(id, new Vector3(0, 400, 0), Quaternion.Identity, new Vector3(0, 0, -42), 42, 0.5f);

    private static void Down(FakeBus bus, int target, int shooter)
    {
        for (int i = 0; i < 25; i++)
            bus.Send(new PlaneHit(target, shooter, PlanePart.Engine, Bullet));
    }

    [Theory]
    [InlineData(PlanePart.Engine)]
    [InlineData(PlanePart.LeftWing)]
    [InlineData(PlanePart.RightWing)]
    [InlineData(PlanePart.Tail)]
    public void HitLowersOnlyThatPart(PlanePart part)
    {
        var (damage, bus) = Started();
        bus.Send(new PlaneHit(5, 1, part, Bullet));
        bus.Send(new PlaneHit(5, 1, part, Bullet));

        var state = damage.GetState(5);
        foreach (PlanePart other in Enum.GetValues<PlanePart>())
            Assert.Equal(other == part ? 1f - 2 * Bullet : 1f, state.Get(other), 5);
        Assert.True(state.Alive);
        // Other planes are untouched.
        Assert.Equal(1f, damage.GetState(4).Get(part));
    }

    [Fact]
    public void PublishedStateCarriesEachPart()
    {
        var (damage, bus) = Started();
        bus.Send(new PlaneHit(2, 6, PlanePart.Engine, 0.1f));
        bus.Send(new PlaneHit(2, 6, PlanePart.LeftWing, 0.2f));
        bus.Send(new PlaneHit(2, 6, PlanePart.RightWing, 0.3f));
        bus.Send(new PlaneHit(2, 6, PlanePart.Tail, 0.4f));
        Tick(damage);

        var state = Assert.Single(bus.All<PlaneDamageState>());
        Assert.Equal(2, state.PlaneId);
        Assert.Equal(0.9f, state.Engine, 5);
        Assert.Equal(0.8f, state.LeftWing, 5);
        Assert.Equal(0.7f, state.RightWing, 5);
        Assert.Equal(0.6f, state.Tail, 5);
        Assert.True(state.Alive);
    }

    [Fact]
    public void TwentyFiveBulletsDownAPartAndThePlaneOnce()
    {
        var (damage, bus) = Started();
        for (int i = 0; i < 24; i++)
            bus.Send(new PlaneHit(4, 0, PlanePart.Tail, Bullet));
        Assert.True(damage.GetState(4).Alive);
        Assert.Empty(bus.All<PlaneDestroyed>());

        bus.Send(new PlaneHit(4, 0, PlanePart.Tail, Bullet));
        var destroyed = Assert.Single(bus.All<PlaneDestroyed>());
        Assert.Equal(4, destroyed.PlaneId);
        Assert.Equal(0, destroyed.ShooterPlaneId);
        var state = damage.GetState(4);
        Assert.False(state.Alive);
        Assert.Equal(0f, state.Tail);

        // More hits, from anyone, on any part: no second PlaneDestroyed, no kill.
        bus.Send(new PlaneHit(4, 0, PlanePart.Tail, 1f));
        bus.Send(new PlaneHit(4, 1, PlanePart.Engine, 1f));
        Tick(damage, 60);
        Assert.Single(bus.All<PlaneDestroyed>());
        Assert.Equal(1, damage.Kills(0));
        Assert.Equal(0, damage.Kills(1));
    }

    [Fact]
    public void NoDamageWhileDown()
    {
        var (damage, bus) = Started();
        bus.Send(new PlaneHit(3, 7, PlanePart.Engine, 1f));
        Assert.False(damage.GetState(3).Alive);

        bus.Send(new PlaneHit(3, 7, PlanePart.LeftWing, 0.5f));
        bus.Send(new PlaneHit(3, 7, PlanePart.RightWing, 0.5f));
        bus.Send(new PlaneHit(3, 7, PlanePart.Tail, 0.5f));
        var state = damage.GetState(3);
        Assert.Equal(1f, state.LeftWing);
        Assert.Equal(1f, state.RightWing);
        Assert.Equal(1f, state.Tail);
        Assert.Equal(0f, state.Engine);

        Tick(damage);
        var published = Assert.Single(bus.All<PlaneDamageState>());
        Assert.False(published.Alive);
    }

    [Fact]
    public void RespawnsAfterFiveSecondsAtFullHealth()
    {
        var (damage, bus) = Started();
        bus.Send(new PlaneHit(6, 2, PlanePart.LeftWing, 0.5f));
        bus.Send(new PlaneHit(6, 2, PlanePart.Tail, 0.3f));
        Tick(damage, 10);
        bus.Send(new PlaneHit(6, 2, PlanePart.LeftWing, 0.5f));
        Assert.False(damage.GetState(6).Alive);
        bus.Clear();

        // 5 s at 60 steps per second: down for 299 steps, back on the 300th.
        Tick(damage, 299);
        Assert.Empty(bus.All<PlaneRespawn>());
        Assert.False(damage.GetState(6).Alive);
        Assert.All(bus.All<PlaneDamageState>(), s => Assert.False(s.Alive));

        bus.Clear();
        Tick(damage);
        var respawn = Assert.Single(bus.All<PlaneRespawn>());
        Assert.Equal(6, respawn.PlaneId);
        var state = Assert.Single(bus.All<PlaneDamageState>());
        Assert.True(state.Alive);
        Assert.Equal(1f, state.Engine);
        Assert.Equal(1f, state.LeftWing);
        Assert.Equal(1f, state.RightWing);
        Assert.Equal(1f, state.Tail);

        // Only once, and it takes hits again.
        Tick(damage, 600);
        Assert.Single(bus.All<PlaneRespawn>());
        bus.Send(new PlaneHit(6, 2, PlanePart.Engine, Bullet));
        Assert.Equal(1f - Bullet, damage.GetState(6).Engine, 5);
    }

    [Fact]
    public void CanBeDownedAgainAfterRespawn()
    {
        var (damage, bus) = Started();
        Down(bus, 1, 5);
        Tick(damage, 300);
        Assert.True(damage.GetState(1).Alive);
        Down(bus, 1, 4);

        var destroyed = bus.All<PlaneDestroyed>();
        Assert.Equal(2, destroyed.Count);
        Assert.Equal(4, destroyed[1].ShooterPlaneId);
        Assert.Equal(1, damage.Kills(5));
        Assert.Equal(1, damage.Kills(4));
    }

    [Fact]
    public void KillsCountedPerShooter()
    {
        var (damage, bus) = Started();
        Down(bus, 4, 0);
        Down(bus, 5, 0);
        Down(bus, 0, 6);

        Assert.Equal(2, damage.Kills(0));
        Assert.Equal(1, damage.Kills(6));
        Assert.Equal(0, damage.Kills(4));
        Assert.Equal(0, damage.Kills(-1));
        Assert.Equal(0, damage.Kills(1000));
    }

    [Fact]
    public void StatesPublishedEveryTickForEveryKnownPlane()
    {
        var (damage, bus) = Started();
        Tick(damage);
        Assert.Empty(bus.All<PlaneDamageState>());

        // Known from Flight's PlaneState or from a first hit; each only once per tick.
        bus.Send(Flying(0));
        bus.Send(Flying(0));
        bus.Send(Flying(7));
        bus.Send(new PlaneHit(3, 4, PlanePart.Engine, Bullet));
        Assert.True(damage.IsKnown(0));
        Assert.False(damage.IsKnown(1));
        for (int t = 0; t < 5; t++)
        {
            bus.Clear();
            Tick(damage);
            var states = bus.All<PlaneDamageState>();
            Assert.Equal(new[] { 0, 3, 7 }, states.ConvertAll(s => s.PlaneId));
            Assert.Equal(1f, states[0].Engine);
            Assert.Equal(1f - Bullet, states[1].Engine, 5);
        }
    }

    [Fact]
    public void AllEightPlanesFromFlightArePublished()
    {
        var (damage, bus) = Started();
        for (int id = 0; id < 8; id++)
            bus.Send(Flying(id));
        Tick(damage);
        var states = bus.All<PlaneDamageState>();
        Assert.Equal(8, states.Count);
        for (int id = 0; id < 8; id++)
        {
            Assert.Equal(id, states[id].PlaneId);
            Assert.True(states[id].Alive);
        }
    }

    [Fact]
    public void PlaneStateDoesNotHealADamagedPlane()
    {
        var (damage, bus) = Started();
        bus.Send(new PlaneHit(2, 5, PlanePart.Tail, 0.5f));
        bus.Send(Flying(2));
        Assert.Equal(0.5f, damage.GetState(2).Tail, 5);
    }

    [Theory]
    [InlineData(-1, PlanePart.Engine, 0.5f)]
    [InlineData(DamageLimits.MaxPlanes, PlanePart.Engine, 0.5f)]
    [InlineData(1, (PlanePart)9, 0.5f)]
    [InlineData(1, PlanePart.Engine, 0f)]
    [InlineData(1, PlanePart.Engine, -0.5f)]
    [InlineData(1, PlanePart.Engine, float.NaN)]
    public void BadHitsDoNoDamage(int target, PlanePart part, float amount)
    {
        var (damage, bus) = Started();
        bus.Send(new PlaneHit(target, 0, part, amount));
        var state = damage.GetState(1);
        Assert.Equal(1f, state.Engine);
        Assert.True(state.Alive);
        Tick(damage);
        Assert.All(bus.All<PlaneDamageState>(), s => Assert.Equal(1f, s.Engine));
        Assert.Empty(bus.All<PlaneDestroyed>());
    }

    [Fact]
    public void SelfHitDownsButIsNoKill()
    {
        var (damage, bus) = Started();
        Down(bus, 2, 2);
        Assert.Single(bus.All<PlaneDestroyed>());
        Assert.Equal(0, damage.Kills(2));
    }

    [Fact]
    public void TickBeforeStartDoesNotThrow()
    {
        var damage = new DamageModule();
        damage.Tick(new TickContext(1, Step));
        Assert.False(damage.IsKnown(0));
    }

    [Fact]
    public void Deterministic()
    {
        string Run()
        {
            var (damage, bus) = Started();
            for (int t = 0; t < 900; t++)
            {
                bus.Send(new PlaneHit(t % 8, (t + 4) % 8, (PlanePart)(t % 4), 0.03f + (t % 5) * 0.01f));
                Tick(damage);
            }
            var sb = new System.Text.StringBuilder();
            foreach (var m in bus.Published)
                sb.Append(m switch
                {
                    PlaneDamageState s => $"S{s.PlaneId}:{s.Engine},{s.LeftWing},{s.RightWing},{s.Tail},{s.Alive};",
                    PlaneDestroyed d => $"D{d.PlaneId}<{d.ShooterPlaneId};",
                    PlaneRespawn r => $"R{r.PlaneId};",
                    _ => "?",
                });
            return sb.ToString();
        }
        string first = Run();
        Assert.Contains("D", first);
        Assert.Contains("R", first);
        Assert.Equal(first, Run());
    }

    [Fact]
    public void TickAndHitsDoNotAllocate()
    {
        var bus = new QuietBus();
        var damage = new DamageModule();
        damage.Start(bus);
        var tick = new TickContext(1, Step);
        var hit = new PlaneHit(4, 0, PlanePart.Engine, 0.3f);
        bus.Hits!.Handle(in hit);
        damage.Tick(in tick);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            bus.Hits!.Handle(in hit);
            damage.Tick(in tick);
        }
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        Assert.True(bus.Destroyed > 1);
        Assert.True(bus.Respawned > 1);
        Assert.Equal(10_001, bus.StatesPublished);
    }

    [Fact]
    public void IsTheModulesOnlyEntryClass()
    {
        int entries = 0;
        foreach (var type in typeof(DamageModule).Assembly.GetExportedTypes())
            if (type.IsClass && !type.IsAbstract && typeof(IModule).IsAssignableFrom(type))
                entries++;
        Assert.Equal(1, entries);
    }

    [Fact]
    public void CoreAndContractTargetNet10()
    {
        foreach (var assembly in new[] { typeof(DamageModule).Assembly, typeof(PlaneHit).Assembly })
            Assert.Equal(".NETCoreApp,Version=v10.0", assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName);
    }

    [Fact]
    public void CoreDoesNotReferenceGodot()
    {
        foreach (var name in typeof(DamageModule).Assembly.GetReferencedAssemblies())
            Assert.DoesNotContain("Godot", name.Name);
    }
}
