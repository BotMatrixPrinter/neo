using System;
using System.Numerics;
using System.Reflection;
using System.Runtime.Versioning;
using Neo.Flight;
using Neo.Input;
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

    // 4 s at 60 steps per second.
    private const int RepairSteps = 240;

    private static ControlInput Controls(int id, bool repair, float pitch = 0f, float roll = 0f) =>
        new(id, pitch, roll, 0f, 0.7f, false, false, Vector3.Zero, repair);

    /// <summary>Holds Repair on <paramref name="id"/> for <paramref name="steps"/> ticks (one ControlInput per tick, as Input sends).</summary>
    private static void Hold(DamageModule damage, FakeBus bus, int id, int steps)
    {
        for (int i = 0; i < steps; i++)
        {
            bus.Send(Controls(id, true));
            Tick(damage);
        }
    }

    /// <summary>Repair released: ControlInput still arrives every tick, with Repair false.</summary>
    private static void Release(DamageModule damage, FakeBus bus, int id, int steps)
    {
        for (int i = 0; i < steps; i++)
        {
            bus.Send(Controls(id, false));
            Tick(damage);
        }
    }

    private static RepairState LastRepair(FakeBus bus, int id) => bus.All<RepairState>().FindLast(r => r.PlaneId == id);

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

    private static PlaneCrashed Crashed(int id) => new(id, new Vector3(100, 3, -50), 55f);

    [Fact]
    public void CrashDestroysAnAlivePlaneAtOnce()
    {
        var (damage, bus) = Started();
        bus.Send(Flying(2));
        bus.Send(new PlaneHit(2, 6, PlanePart.Tail, 0.2f));
        Tick(damage, 10);
        bus.Clear();

        bus.Send(Crashed(2));
        var destroyed = Assert.Single(bus.All<PlaneDestroyed>());
        Assert.Equal(2, destroyed.PlaneId);
        Assert.False(damage.GetState(2).Alive);
        Tick(damage);
        Assert.False(Assert.Single(bus.All<PlaneDamageState>()).Alive);

        // A plane never hit or seen before also crashes: known and down from then on.
        bus.Clear();
        bus.Send(Crashed(9));
        Assert.True(damage.IsKnown(9));
        Assert.False(damage.GetState(9).Alive);
        Assert.Equal(9, Assert.Single(bus.All<PlaneDestroyed>()).PlaneId);
    }

    [Fact]
    public void CrashWithoutHitsIsCreditedToNobody()
    {
        var (damage, bus) = Started();
        bus.Send(Flying(0));
        Tick(damage, 60);
        bus.Send(Crashed(0));

        Assert.Equal(-1, Assert.Single(bus.All<PlaneDestroyed>()).ShooterPlaneId);
        for (int i = 0; i < 8; i++)
            Assert.Equal(0, damage.Kills(i));
    }

    [Fact]
    public void CrashIsCreditedToTheLastHitterWithinTenSeconds()
    {
        var (damage, bus) = Started();
        bus.Send(new PlaneHit(1, 5, PlanePart.Engine, Bullet));
        Tick(damage, 60);
        // The last hit counts, not the first.
        bus.Send(new PlaneHit(1, 6, PlanePart.LeftWing, Bullet));
        // 10 s later exactly (600 steps): still credited.
        Tick(damage, 600);
        bus.Send(Crashed(1));

        var destroyed = Assert.Single(bus.All<PlaneDestroyed>());
        Assert.Equal(1, destroyed.PlaneId);
        Assert.Equal(6, destroyed.ShooterPlaneId);
        Assert.Equal(1, damage.Kills(6));
        Assert.Equal(0, damage.Kills(5));
    }

    [Fact]
    public void CrashMoreThanTenSecondsAfterTheLastHitIsCreditedToNobody()
    {
        var (damage, bus) = Started();
        bus.Send(new PlaneHit(4, 0, PlanePart.Tail, Bullet));
        Tick(damage, 601);
        bus.Send(Crashed(4));

        Assert.Equal(-1, Assert.Single(bus.All<PlaneDestroyed>()).ShooterPlaneId);
        Assert.Equal(0, damage.Kills(0));
    }

    [Fact]
    public void CrashIgnoresSelfHitsAndHitsFromBeforeARespawn()
    {
        var (damage, bus) = Started();
        // A self hit is no pursuer.
        bus.Send(new PlaneHit(3, 3, PlanePart.Engine, Bullet));
        bus.Send(Crashed(3));
        Assert.Equal(-1, bus.All<PlaneDestroyed>()[0].ShooterPlaneId);

        // Hit by 7 just before being shot down by 5: after the respawn, a crash within 10 s of
        // those hits is still on its own (they were in its previous life).
        Tick(damage, 300);
        Assert.True(damage.GetState(3).Alive);
        bus.Send(new PlaneHit(3, 7, PlanePart.Tail, Bullet));
        Down(bus, 3, 5);
        Tick(damage, 300);
        Assert.True(damage.GetState(3).Alive);
        Tick(damage, 60);
        bus.Send(Crashed(3));

        var destroyed = bus.All<PlaneDestroyed>();
        Assert.Equal(3, destroyed.Count);
        Assert.Equal(5, destroyed[1].ShooterPlaneId);
        Assert.Equal(-1, destroyed[2].ShooterPlaneId);
        Assert.Equal(0, damage.Kills(7));
        Assert.Equal(1, damage.Kills(5));
    }

    [Fact]
    public void CrashOfADownedPlaneChangesNothing()
    {
        var (damage, bus) = Started();
        Down(bus, 5, 1);
        Tick(damage, 120);
        // The shot-down plane falls and hits the ground, twice even: no second downing, no second kill,
        // and the respawn stays 5 s after the shoot-down.
        bus.Send(Crashed(5));
        bus.Send(Crashed(5));
        Assert.Single(bus.All<PlaneDestroyed>());
        Assert.Equal(1, damage.Kills(1));
        Tick(damage, 179);
        Assert.Empty(bus.All<PlaneRespawn>());
        Tick(damage);
        Assert.Single(bus.All<PlaneRespawn>());

        // A crash after a crash is ignored as well.
        bus.Clear();
        bus.Send(Crashed(2));
        Tick(damage, 30);
        bus.Send(Crashed(2));
        Assert.Single(bus.All<PlaneDestroyed>());
        Assert.False(damage.GetState(2).Alive);
    }

    [Fact]
    public void CrashedPlaneRespawnsAfterFiveSecondsAndCanCrashAgain()
    {
        var (damage, bus) = Started();
        bus.Send(new PlaneHit(6, 2, PlanePart.RightWing, 0.5f));
        Tick(damage, 30);
        bus.Send(Crashed(6));
        bus.Clear();

        Tick(damage, 299);
        Assert.Empty(bus.All<PlaneRespawn>());
        Assert.False(damage.GetState(6).Alive);
        bus.Clear();
        Tick(damage);
        Assert.Equal(6, Assert.Single(bus.All<PlaneRespawn>()).PlaneId);
        var state = Assert.Single(bus.All<PlaneDamageState>());
        Assert.True(state.Alive);
        Assert.Equal(1f, state.RightWing);

        Tick(damage, 600);
        Assert.Single(bus.All<PlaneRespawn>());
        bus.Send(Crashed(6));
        Assert.Equal(-1, Assert.Single(bus.All<PlaneDestroyed>()).ShooterPlaneId);
        Tick(damage, 300);
        Assert.Equal(2, bus.All<PlaneRespawn>().Count);
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
    public void RepairFixesOnePartPerCycleMostDamagedFirst()
    {
        var (damage, bus) = Started();
        bus.Send(new PlaneHit(0, 5, PlanePart.Engine, 0.2f));
        bus.Send(new PlaneHit(0, 5, PlanePart.Tail, 0.6f));
        bus.Send(new PlaneHit(0, 5, PlanePart.LeftWing, 0.4f));

        // First cycle: the tail (0.4 left), the most damaged. 239 steps are not enough.
        Hold(damage, bus, 0, RepairSteps - 1);
        var repair = LastRepair(bus, 0);
        Assert.True(repair.Repairing);
        Assert.Equal(PlanePart.Tail, repair.Part);
        Assert.Equal(3, repair.PartsLeft);
        Assert.InRange(repair.Progress, 0.99f, 1f);
        Assert.Equal(0.4f, damage.GetState(0).Tail, 5);

        Hold(damage, bus, 0, 1);
        var state = damage.GetState(0);
        Assert.Equal(1f, state.Tail);
        Assert.Equal(0.6f, state.LeftWing, 5);
        Assert.Equal(0.8f, state.Engine, 5);
        repair = LastRepair(bus, 0);
        Assert.True(repair.Repairing);
        Assert.Equal(PlanePart.LeftWing, repair.Part);
        Assert.Equal(0f, repair.Progress);
        Assert.Equal(2, repair.PartsLeft);
        var published = bus.All<PlaneDamageState>()[^1];
        Assert.Equal(1f, published.Tail);

        // Second cycle: the left wing, again exactly 4 s; the engine still waits.
        Hold(damage, bus, 0, RepairSteps - 1);
        Assert.Equal(0.6f, damage.GetState(0).LeftWing, 5);
        Hold(damage, bus, 0, 1);
        Assert.Equal(1f, damage.GetState(0).LeftWing);
        Assert.Equal(0.8f, damage.GetState(0).Engine, 5);

        // Third cycle: the engine; then nothing is left and the repair stops by itself.
        Hold(damage, bus, 0, RepairSteps);
        state = damage.GetState(0);
        Assert.Equal(1f, state.Engine);
        Assert.Equal(1f, state.LeftWing);
        Assert.Equal(1f, state.RightWing);
        Assert.Equal(1f, state.Tail);
        repair = LastRepair(bus, 0);
        Assert.False(repair.Repairing);
        Assert.Equal(0, repair.PartsLeft);
        Hold(damage, bus, 0, 10);
        Assert.False(LastRepair(bus, 0).Repairing);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void MoreDamagedPartsTakeLonger(int parts)
    {
        var (damage, bus) = Started();
        for (int p = 0; p < parts; p++)
            bus.Send(new PlaneHit(0, 5, (PlanePart)p, 0.1f + 0.1f * p));

        // Done after exactly parts * 4 s of holding, not one step earlier.
        Hold(damage, bus, 0, parts * RepairSteps - 1);
        Assert.True(LastRepair(bus, 0).Repairing);
        Assert.Equal(1, LastRepair(bus, 0).PartsLeft);
        Assert.True(damage.GetState(0).Engine < 1f);
        Hold(damage, bus, 0, 1);
        Assert.False(LastRepair(bus, 0).Repairing);
        Assert.Equal(0, LastRepair(bus, 0).PartsLeft);
        for (int p = 0; p < 4; p++)
            Assert.Equal(1f, damage.GetState(0).Get((PlanePart)p));
    }

    [Fact]
    public void ReleaseLosesTheCurrentCycleAndReholdingRestartsFromZero()
    {
        var (damage, bus) = Started();
        bus.Send(new PlaneHit(0, 5, PlanePart.RightWing, 0.5f));
        bus.Send(new PlaneHit(0, 5, PlanePart.Engine, 0.3f));

        // Fix the right wing, then get 3.5 s into the engine and let go.
        Hold(damage, bus, 0, RepairSteps + 210);
        Assert.Equal(PlanePart.Engine, LastRepair(bus, 0).Part);
        Release(damage, bus, 0, 1);
        var repair = LastRepair(bus, 0);
        Assert.False(repair.Repairing);
        Assert.Equal(0f, repair.Progress);
        Assert.Equal(1, repair.PartsLeft);
        // The fixed part stays fixed; the engine is untouched.
        Assert.Equal(1f, damage.GetState(0).RightWing);
        Assert.Equal(0.7f, damage.GetState(0).Engine, 5);

        // Holding again starts the engine from 0: a full 4 s more.
        Hold(damage, bus, 0, 1);
        repair = LastRepair(bus, 0);
        Assert.True(repair.Repairing);
        Assert.Equal(PlanePart.Engine, repair.Part);
        Assert.InRange(repair.Progress, 0f, 0.01f);
        Hold(damage, bus, 0, RepairSteps - 2);
        Assert.Equal(0.7f, damage.GetState(0).Engine, 5);
        Hold(damage, bus, 0, 1);
        Assert.Equal(1f, damage.GetState(0).Engine);
    }

    [Fact]
    public void ReleaseWithoutAnyControlInputAlsoCancels()
    {
        var (damage, bus) = Started();
        bus.Send(new PlaneHit(0, 5, PlanePart.Tail, 0.5f));
        Hold(damage, bus, 0, RepairSteps - 1);
        // No ControlInput at all this tick (e.g. Input stopped): not held.
        Tick(damage);
        Assert.False(LastRepair(bus, 0).Repairing);
        Hold(damage, bus, 0, 1);
        Assert.Equal(0.5f, damage.GetState(0).Tail, 5);
    }

    [Fact]
    public void SteeringDoesNotCancelARepair()
    {
        var (damage, bus) = Started();
        bus.Send(new PlaneHit(0, 5, PlanePart.Tail, 0.5f));
        for (int i = 0; i < RepairSteps; i++)
        {
            bus.Send(Controls(0, true, pitch: i % 2 == 0 ? 1f : -1f, roll: 0.5f));
            Tick(damage);
        }
        Assert.Equal(1f, damage.GetState(0).Tail);
    }

    [Fact]
    public void DownedCancelsTheRepair()
    {
        var (damage, bus) = Started();
        bus.Send(new PlaneHit(0, 5, PlanePart.LeftWing, 0.5f));
        Hold(damage, bus, 0, RepairSteps / 2);
        Assert.True(LastRepair(bus, 0).Repairing);

        Down(bus, 0, 5);
        Hold(damage, bus, 0, 1);
        var repair = LastRepair(bus, 0);
        Assert.False(repair.Repairing);
        Assert.Equal(0f, repair.Progress);
        // Still held while down: nothing is repaired.
        Hold(damage, bus, 0, RepairSteps);
        Assert.Equal(0.5f, damage.GetState(0).LeftWing, 5);
        Assert.Equal(0f, damage.GetState(0).Engine);
    }

    [Fact]
    public void NoRepairWhenUndamaged()
    {
        var (damage, bus) = Started();
        bus.Send(Flying(0));
        Hold(damage, bus, 0, RepairSteps * 2);
        var repairs = bus.All<RepairState>();
        Assert.Equal(RepairSteps * 2, repairs.Count);
        Assert.All(repairs, r =>
        {
            Assert.Equal(0, r.PlaneId);
            Assert.False(r.Repairing);
            Assert.Equal(0, r.PartsLeft);
            Assert.Equal(0f, r.Progress);
        });
        // A hit while held starts a repair right away.
        bus.Send(new PlaneHit(0, 5, PlanePart.Tail, 0.2f));
        Hold(damage, bus, 0, 1);
        Assert.True(LastRepair(bus, 0).Repairing);
        Assert.Equal(PlanePart.Tail, LastRepair(bus, 0).Part);
    }

    [Fact]
    public void RepairIsPerPlane()
    {
        var (damage, bus) = Started();
        bus.Send(new PlaneHit(0, 5, PlanePart.Tail, 0.5f));
        bus.Send(new PlaneHit(4, 0, PlanePart.Tail, 0.5f));
        Hold(damage, bus, 0, RepairSteps);
        Assert.Equal(1f, damage.GetState(0).Tail);
        Assert.Equal(0.5f, damage.GetState(4).Tail, 5);
        Assert.False(LastRepair(bus, 4).Repairing);
        Assert.Equal(1, LastRepair(bus, 4).PartsLeft);
    }

    [Fact]
    public void HitsDuringARepairStillLand()
    {
        var (damage, bus) = Started();
        bus.Send(new PlaneHit(0, 5, PlanePart.Tail, 0.3f));
        Hold(damage, bus, 0, 100);
        // The part being repaired is hit again, and another part too.
        bus.Send(new PlaneHit(0, 5, PlanePart.Tail, 0.2f));
        bus.Send(new PlaneHit(0, 5, PlanePart.Engine, 0.1f));
        Assert.Equal(0.5f, damage.GetState(0).Tail, 5);
        Assert.Equal(0.9f, damage.GetState(0).Engine, 5);
        Assert.Equal(PlanePart.Tail, LastRepair(bus, 0).Part);

        // The cycle is not restarted by the hit; it ends with the tail at 1.
        Hold(damage, bus, 0, RepairSteps - 100);
        Assert.Equal(1f, damage.GetState(0).Tail);
        Assert.Equal(PlanePart.Engine, LastRepair(bus, 0).Part);
        Assert.Equal(1, LastRepair(bus, 0).PartsLeft);
    }

    [Fact]
    public void RepairStatePublishedEveryTickAfterEachDamageState()
    {
        var (damage, bus) = Started();
        bus.Send(Flying(1));
        bus.Send(Flying(3));
        Tick(damage);
        Assert.Equal(4, bus.Published.Count);
        Assert.Equal(1, Assert.IsType<PlaneDamageState>(bus.Published[0]).PlaneId);
        Assert.Equal(1, Assert.IsType<RepairState>(bus.Published[1]).PlaneId);
        Assert.Equal(3, Assert.IsType<PlaneDamageState>(bus.Published[2]).PlaneId);
        Assert.Equal(3, Assert.IsType<RepairState>(bus.Published[3]).PlaneId);
        Assert.False(damage.GetRepair(1).Repairing);
        Assert.False(damage.GetRepair(9).Repairing);
    }

    [Fact]
    public void RepairDoesNotAllocate()
    {
        var bus = new QuietBus();
        var damage = new DamageModule();
        damage.Start(bus);
        var tick = new TickContext(1, Step);
        var hit = new PlaneHit(0, 4, PlanePart.Engine, 0.01f);
        var held = Controls(0, true);
        var released = Controls(0, false);
        bus.Hits!.Handle(in hit);
        bus.Controls!.Handle(in held);
        damage.Tick(in tick);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            if (i % 50 == 0)
                bus.Hits!.Handle(in hit);
            if (i % 1000 < 900)
                bus.Controls!.Handle(in held);
            else
                bus.Controls!.Handle(in released);
            damage.Tick(in tick);
        }
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        Assert.Equal(10_001, bus.RepairsPublished);
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
