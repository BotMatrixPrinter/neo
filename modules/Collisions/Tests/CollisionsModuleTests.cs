using System;
using System.Numerics;
using System.Reflection;
using System.Runtime.Versioning;
using Neo.Damage;
using Neo.Flight;
using Neo.Shell;
using Xunit;

namespace Neo.Collisions.Tests;

public class CollisionsModuleTests
{
    private static readonly TickContext OneStep = new(1, 1.0 / 60);
    private static readonly Vector3 Center = new(0f, 300f, 0f);
    // Turned half round: flying +Z (south), its right wing toward world -X.
    private static readonly Quaternion South = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);

    private static (CollisionsModule module, FakeBus bus) Started()
    {
        var bus = new FakeBus();
        var module = new CollisionsModule();
        module.Start(bus);
        return (module, bus);
    }

    private static PlaneState Plane(int id, Vector3 offset, Vector3 velocity, Quaternion? orientation = null, bool alive = true) =>
        new(id, Center + offset, orientation ?? Quaternion.Identity, velocity, velocity.Length(), 1f, alive);

    // Sends both states and ticks once.
    private static void Step(CollisionsModule module, FakeBus bus, PlaneState a, PlaneState b)
    {
        bus.Send(a);
        bus.Send(b);
        module.Tick(in OneStep);
    }

    private static PlaneHit HitOn(FakeBus bus, int plane)
    {
        var hits = bus.All<PlaneHit>().FindAll(h => h.TargetPlaneId == plane);
        Assert.Single(hits);
        return hits[0];
    }

    private static CollisionImpulse ImpulseOf(FakeBus bus, int plane)
    {
        var impulses = bus.All<CollisionImpulse>().FindAll(i => i.PlaneId == plane);
        Assert.Single(impulses);
        return impulses[0];
    }

    private static void Near(Vector3 expected, Vector3 actual, float tolerance = 1e-3f) =>
        Assert.True(Vector3.Distance(expected, actual) < tolerance, $"expected {expected}, got {actual}");

    // Head-on, nose to nose, both at speed/2, overlapping a little.
    private static (FakeBus bus, CollisionsModule module) HeadOn(float closingSpeed)
    {
        var (module, bus) = Started();
        float v = closingSpeed / 2f;
        Step(module, bus,
            Plane(0, Vector3.Zero, new Vector3(0f, 0f, -v)),
            Plane(4, new Vector3(0f, 0f, -6.5f), new Vector3(0f, 0f, v), South));
        return (bus, module);
    }

    [Fact]
    public void ImpulsesAreEqualAndOppositeAlongTheNormalWithRestitution()
    {
        var (module, bus) = Started();
        // An oblique hit: b ahead, up and to the right of a, both closing.
        var a = Plane(0, Vector3.Zero, new Vector3(5f, 0f, -55f));
        var b = Plane(5, new Vector3(2f, 1.5f, -5.8f), new Vector3(-10f, -4f, 20f), South);
        Step(module, bus, a, b);

        var ia = ImpulseOf(bus, 0);
        var ib = ImpulseOf(bus, 5);
        Near(Vector3.Zero, ia.VelocityChange + ib.VelocityChange);
        Near(Vector3.Zero, ia.Separation + ib.Separation);

        var n = Vector3.Normalize(b.Position - a.Position);
        // Along the normal, b pushed away from a, a away from b.
        Assert.True(Vector3.Dot(ib.VelocityChange, n) > 1f);
        Near(Vector3.Zero, ib.VelocityChange - Vector3.Dot(ib.VelocityChange, n) * n);
        Near(Vector3.Zero, ia.Separation - Vector3.Dot(ia.Separation, n) * n);
        Assert.True(Vector3.Dot(ia.Separation, n) < 0f);

        // After it, they move apart along n at 0.3 of the speed they closed at; sideways nothing changes.
        float closingBefore = -Vector3.Dot(b.Velocity - a.Velocity, n);
        var va = a.Velocity + ia.VelocityChange;
        var vb = b.Velocity + ib.VelocityChange;
        float apartAfter = Vector3.Dot(vb - va, n);
        Assert.True(closingBefore > 0f);
        Assert.Equal(CollisionsModule.Restitution * closingBefore, apartAfter, 3);
        var sideBefore = (b.Velocity - a.Velocity) - Vector3.Dot(b.Velocity - a.Velocity, n) * n;
        var sideAfter = (vb - va) - Vector3.Dot(vb - va, n) * n;
        Near(sideBefore, sideAfter);

        // The separation puts the spheres just apart.
        float gap = Vector3.Distance(a.Position + ia.Separation, b.Position + ib.Separation);
        Assert.InRange(gap, 2f * CollisionsModule.PlaneRadius, 2f * CollisionsModule.PlaneRadius + 0.05f);
    }

    [Fact]
    public void HeadOnBounceKnocksBothBackAlongTheirLine()
    {
        var (bus, _) = HeadOn(100f);
        // Normal from 0 to 4 is -Z; closing 100 m/s along it: each changes by 1.3 x 100 / 2 = 65 m/s.
        Near(new Vector3(0f, 0f, 65f), ImpulseOf(bus, 0).VelocityChange, 1e-2f);
        Near(new Vector3(0f, 0f, -65f), ImpulseOf(bus, 4).VelocityChange, 1e-2f);
        Near(new Vector3(0f, 0f, 0.26f), ImpulseOf(bus, 0).Separation, 1e-3f);
        Near(new Vector3(0f, 0f, -0.26f), ImpulseOf(bus, 4).Separation, 1e-3f);
        // Straight down the fuselage: no turn.
        Near(Vector3.Zero, ImpulseOf(bus, 0).AngularKick);
    }

    [Fact]
    public void AWingStruckPlaneIsKickedIntoARollAway()
    {
        var (module, bus) = Started();
        // Plane 1 slides in from the right, level, onto plane 0's right wingtip; both fly north.
        Step(module, bus,
            Plane(0, Vector3.Zero, new Vector3(0f, 0f, -50f)),
            Plane(1, new Vector3(6.6f, 0.2f, 0f), new Vector3(-25f, 0f, -50f)));
        Assert.Equal(PlanePart.RightWing, HitOn(bus, 0).Part);
        Assert.Equal(PlanePart.LeftWing, HitOn(bus, 1).Part);
        var k0 = ImpulseOf(bus, 0).AngularKick;
        var k1 = ImpulseOf(bus, 1).AngularKick;
        // Struck on the right wing from the right (a little above): wing knocked down, a strong
        // roll right, away from plane 1 (negative z lowers the right wing).
        Assert.True(k0.Z < -0.5f, $"roll {k0}");
        Assert.True(MathF.Abs(k0.Z) > 3f * MathF.Abs(k0.X) && MathF.Abs(k0.Z) > 3f * MathF.Abs(k0.Y), $"mostly roll {k0}");
        // Plane 1, struck on its left wing from below: left wing knocked up, banks right too, away.
        Assert.True(k1.Z < -0.5f, $"roll {k1}");

        // Level from the left onto the left wingtip: the struck wing goes up, banking right, away.
        (module, bus) = Started();
        Step(module, bus,
            Plane(2, Vector3.Zero, new Vector3(0f, 0f, -50f)),
            Plane(3, new Vector3(-6.6f, 0f, 0f), new Vector3(25f, 0f, -50f)));
        float soft = ImpulseOf(bus, 2).AngularKick.Z;
        Assert.True(soft < -0.5f, $"roll {soft}");
        // Plane 3 struck on its right wing, level: right wing up, banking left, away.
        Assert.True(ImpulseOf(bus, 3).AngularKick.Z > 0.5f);

        // The roll grows with the impulse.
        (module, bus) = Started();
        Step(module, bus,
            Plane(2, Vector3.Zero, new Vector3(0f, 0f, -50f)),
            Plane(3, new Vector3(-6.6f, 0f, 0f), new Vector3(50f, 0f, -50f)));
        // Twice the closing speed, twice the roll.
        Assert.Equal(2f * soft, ImpulseOf(bus, 2).AngularKick.Z, 3);
    }

    [Fact]
    public void NoseAndTailHitsKickYawAndPitch()
    {
        var (module, bus) = Started();
        // Plane 4 clips plane 0's nose from the right: the nose is knocked left (positive yaw), no roll.
        Step(module, bus,
            Plane(0, Vector3.Zero, new Vector3(0f, 0f, -50f)),
            Plane(4, new Vector3(1.5f, 0f, -6.5f), new Vector3(-20f, 0f, 30f), South));
        Assert.Equal(PlanePart.Engine, HitOn(bus, 0).Part);
        var nose = ImpulseOf(bus, 0).AngularKick;
        Assert.True(nose.Y > 0.1f, $"yaw {nose}");
        Assert.Equal(0f, nose.Z, 4);

        // Plane 5 drops onto plane 1's tail from above: the tail is knocked down, the nose up.
        (module, bus) = Started();
        Step(module, bus,
            Plane(1, Vector3.Zero, new Vector3(0f, 0f, -40f)),
            Plane(5, new Vector3(0f, 3f, 5.5f), new Vector3(0f, -15f, -60f)));
        Assert.Equal(PlanePart.Tail, HitOn(bus, 1).Part);
        var tail = ImpulseOf(bus, 1).AngularKick;
        Assert.True(tail.X > 0.1f, $"pitch {tail}");
        Assert.True(MathF.Abs(tail.Y) < 1e-3f && MathF.Abs(tail.Z) < 1e-3f, $"only pitch {tail}");
    }

    [Fact]
    public void TheFasterPlaneDealsMoreThanItTakes()
    {
        var (module, bus) = Started();
        // Plane 0 at 80 m/s rams plane 6 at 30 m/s from behind: closing 50 m/s.
        Step(module, bus,
            Plane(0, new Vector3(0f, 0f, 6.5f), new Vector3(0f, 0f, -80f)),
            Plane(6, Vector3.Zero, new Vector3(0f, 0f, -30f)));
        var onSlow = HitOn(bus, 6);
        var onFast = HitOn(bus, 0);
        Assert.Equal(0, onSlow.ShooterPlaneId);
        Assert.Equal(6, onFast.ShooterPlaneId);
        Assert.True(onSlow.Amount > 2f * onFast.Amount, $"{onSlow.Amount} vs {onFast.Amount}");
        float total = CollisionsModule.TotalDamage(50f);
        Assert.Equal(total * 80f / 110f, onSlow.Amount, 4);
        Assert.Equal(total * 30f / 110f, onFast.Amount, 4);
        Assert.Equal(PlanePart.Tail, onSlow.Part);
        Assert.Equal(PlanePart.Engine, onFast.Part);

        // Head-on at 70 and 30 m/s (closing 100): the faster plane takes less, the slower is wrecked.
        (module, bus) = Started();
        Step(module, bus,
            Plane(2, Vector3.Zero, new Vector3(0f, 0f, -70f)),
            Plane(7, new Vector3(0f, 0f, -6.5f), new Vector3(0f, 0f, 30f), South));
        Assert.True(HitOn(bus, 7).Amount > HitOn(bus, 2).Amount);
        Assert.Equal(1f, HitOn(bus, 7).Amount, 4);
    }

    [Fact]
    public void DamageGrowsFasterThanTheClosingSpeed()
    {
        float Each(float closing) => HitOn(HeadOn(closing).bus, 0).Amount;

        float bump = Each(10f);
        float thirty = Each(30f);
        float hard = Each(60f);
        Assert.InRange(bump, 0.03f, 0.08f);
        Assert.InRange(hard, 0.4f, 0.6f);
        Assert.Equal(1f, Each(100f), 4);
        Assert.Equal(1f, Each(150f), 4);
        Assert.True(bump < thirty && thirty < hard);
        Assert.True(hard > 2.2f * thirty && thirty > 3.3f * bump, $"{bump} {thirty} {hard}");
        // Equal speeds share it evenly.
        var (bus, _) = HeadOn(60f);
        Assert.Equal(HitOn(bus, 0).Amount, HitOn(bus, 4).Amount, 5);
        Assert.Equal(CollisionsModule.TotalDamage(60f) / 2f, HitOn(bus, 0).Amount, 5);
    }

    [Fact]
    public void OneHitPerContactUntilSeparated()
    {
        var (module, bus) = Started();
        var a = Plane(0, Vector3.Zero, new Vector3(0f, 0f, -50f));
        // Stuck together for a second: one collision.
        for (int i = 0; i < 60; i++)
            Step(module, bus, a, Plane(1, new Vector3(5f - i * 0.05f, 0f, 0f), new Vector3(-3f, 0f, -50f)));
        Assert.Equal(2, bus.All<PlaneHit>().Count);
        Assert.Equal(2, bus.All<CollisionImpulse>().Count);
        Assert.Equal(1, module.Collisions);
        Assert.True(module.Touching(1, 0));

        // Apart (just over 7 m), then back: a second collision.
        Step(module, bus, a, Plane(1, new Vector3(7.1f, 0f, 0f), new Vector3(-3f, 0f, -50f)));
        Assert.False(module.Touching(0, 1));
        Assert.Equal(2, bus.All<PlaneHit>().Count);
        Step(module, bus, a, Plane(1, new Vector3(6.9f, 0f, 0f), new Vector3(-3f, 0f, -50f)));
        Assert.Equal(4, bus.All<PlaneHit>().Count);
        Assert.Equal(4, bus.All<CollisionImpulse>().Count);
        Assert.Equal(2, module.Collisions);
        Near(Center + new Vector3(3.45f, 0f, 0f), module.CollisionPosition(1));
        Near(new Vector3(-1.5f, 0f, -50f), module.CollisionVelocity(1));
        Assert.Equal(3f, module.CollisionSpeed(1), 4);
    }

    [Fact]
    public void PartsComeFromTheContactPointInEachPlanesFrame()
    {
        // Plane 0 flies north; plane 4 flies south (turned), so the same contact point is a
        // different part for each.
        void Case(Vector3 offset, PlanePart onA, PlanePart onB)
        {
            var (module, bus) = Started();
            Step(module, bus,
                Plane(0, Vector3.Zero, new Vector3(0f, 0f, -50f)),
                Plane(4, offset, new Vector3(0f, 0f, 50f), South));
            Assert.Equal(onA, HitOn(bus, 0).Part);
            Assert.Equal(onB, HitOn(bus, 4).Part);
            Assert.Equal(CollisionsModule.PartAt(module.CollisionPosition(0), Center, Quaternion.Identity), onA);
        }

        // To the right of 0 (world +X): 0's right wing, and 4's right wing too (it faces south,
        // so 0 is on its right).
        Case(new Vector3(6f, 0f, 0f), PlanePart.RightWing, PlanePart.RightWing);
        Case(new Vector3(-6f, 0f, 0f), PlanePart.LeftWing, PlanePart.LeftWing);
        // Nose to nose: both engines.
        Case(new Vector3(0f, 0f, -6f), PlanePart.Engine, PlanePart.Engine);
        // 4 behind 0, flying south: 0's tail, 4's tail too (0 is behind 4 as well).
        Case(new Vector3(0f, 0.5f, 6f), PlanePart.Tail, PlanePart.Tail);
        // Contact (0.75, 0, 3) from 0, inside the wings: 0's tail; for 4 (turned) the same.
        Case(new Vector3(1.5f, 0f, 6f), PlanePart.Tail, PlanePart.Tail);
        Case(new Vector3(1.5f, 0f, -6f), PlanePart.Engine, PlanePart.Engine);
        Case(new Vector3(4f, 0f, -5f), PlanePart.RightWing, PlanePart.RightWing);
    }

    [Fact]
    public void DownedPlanesAreIgnored()
    {
        var (module, bus) = Started();
        var a = Plane(0, Vector3.Zero, new Vector3(0f, 0f, -50f));
        var downed = Plane(4, new Vector3(0f, 0f, -5f), new Vector3(0f, -20f, 50f), South, alive: false);
        for (int i = 0; i < 10; i++)
            Step(module, bus, a, downed);
        Step(module, bus, Plane(0, Vector3.Zero, new Vector3(0f, 0f, -50f), alive: false),
            Plane(4, new Vector3(0f, 0f, -5f), new Vector3(0f, 0f, 50f), South));
        Assert.Empty(bus.Published);
        Assert.Equal(0, module.Collisions);
        Assert.False(module.Touching(0, 4));
    }

    [Fact]
    public void AlliesCollideToo()
    {
        var (module, bus) = Started();
        Step(module, bus,
            Plane(1, Vector3.Zero, new Vector3(0f, 0f, -50f)),
            Plane(2, new Vector3(5f, 0f, 0f), new Vector3(-10f, 0f, -50f)));
        Assert.Equal(2, HitOn(bus, 1).ShooterPlaneId);
        Assert.Equal(1, HitOn(bus, 2).ShooterPlaneId);
        ImpulseOf(bus, 1);
        ImpulseOf(bus, 2);

        (module, bus) = Started();
        Step(module, bus,
            Plane(5, Vector3.Zero, new Vector3(0f, 0f, -50f)),
            Plane(6, new Vector3(0f, 4f, 0f), new Vector3(0f, -10f, -50f)));
        Assert.Equal(2, bus.All<PlaneHit>().Count);
    }

    [Fact]
    public void PlanesApartAndUnknownIdsDoNothing()
    {
        var (module, bus) = Started();
        Step(module, bus,
            Plane(0, Vector3.Zero, new Vector3(0f, 0f, -50f)),
            Plane(3, new Vector3(7.01f, 0f, 0f), new Vector3(-10f, 0f, -50f)));
        bus.Send(Plane(8, Vector3.Zero, new Vector3(0f, 0f, 50f)));
        bus.Send(Plane(-1, Vector3.Zero, new Vector3(0f, 0f, 50f)));
        module.Tick(in OneStep);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public void ThreePlanesInOneSpotMakeThreeCollisions()
    {
        var (module, bus) = Started();
        bus.Send(Plane(0, Vector3.Zero, new Vector3(0f, 0f, -50f)));
        bus.Send(Plane(1, new Vector3(4f, 0f, 0f), new Vector3(-10f, 0f, -50f)));
        bus.Send(Plane(4, new Vector3(2f, 0f, -4f), new Vector3(0f, 0f, 50f), South));
        module.Tick(in OneStep);
        Assert.Equal(3, module.Collisions);
        Assert.Equal(6, bus.All<PlaneHit>().Count);
        Assert.Equal(6, bus.All<CollisionImpulse>().Count);
    }

    [Fact]
    public void IsDeterministic()
    {
        static string Run()
        {
            var (module, bus) = Started();
            var rng = new Random(7);
            for (int t = 0; t < 600; t++)
            {
                for (int p = 0; p < Teams.PlaneCount; p++)
                {
                    var at = new Vector3((float)rng.NextDouble() * 20f, (float)rng.NextDouble() * 10f, (float)rng.NextDouble() * 20f);
                    var v = new Vector3((float)rng.NextDouble() * 60f - 30f, 0f, -40f);
                    var q = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)rng.NextDouble() * 6f);
                    bus.Send(Plane(p, at, v, q));
                }
                module.Tick(in OneStep);
            }
            var text = new System.Text.StringBuilder();
            foreach (var m in bus.Published)
                text.Append(m switch
                {
                    PlaneHit h => $"H{h.TargetPlaneId},{h.ShooterPlaneId},{h.Part},{h.Amount:R};",
                    CollisionImpulse i => $"I{i.PlaneId},{i.VelocityChange},{i.AngularKick},{i.Separation};",
                    _ => "?",
                });
            return text.ToString();
        }

        string first = Run();
        Assert.Contains("H", first);
        Assert.Equal(first, Run());
    }

    [Fact]
    public void TickDoesNotAllocate()
    {
        var bus = new QuietBus();
        var module = new CollisionsModule();
        module.Start(bus);
        var states = new PlaneState[Teams.PlaneCount];
        void Step(int t)
        {
            for (int p = 0; p < Teams.PlaneCount; p++)
            {
                // Planes in a tight bunch, bumping in and out of each other.
                float wobble = MathF.Sin(t * 0.1f + p) * 4f;
                states[p] = Plane(p, new Vector3(p * 3f + wobble, 0f, 0f), new Vector3(-wobble, 0f, -50f));
                bus.Plane!.Handle(in states[p]);
            }
            module.Tick(in OneStep);
        }

        for (int t = 0; t < 100; t++)
            Step(t);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int t = 100; t < 6100; t++)
            Step(t);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        Assert.True(bus.Hits > 100, $"{bus.Hits} hits");
        Assert.True(bus.Impulses > 100, $"{bus.Impulses} impulses");
    }

    [Fact]
    public void IsTheModulesOnlyEntryClassAndCoreHasNoGodot()
    {
        int entries = 0;
        foreach (var type in typeof(CollisionsModule).Assembly.GetExportedTypes())
            if (type.IsClass && !type.IsAbstract && typeof(IModule).IsAssignableFrom(type))
                entries++;
        Assert.Equal(1, entries);
        var assembly = typeof(CollisionsModule).Assembly;
        Assert.Contains(".NETCoreApp,Version=v10.0", assembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName);
        foreach (var reference in assembly.GetReferencedAssemblies())
            Assert.DoesNotContain("Godot", reference.Name!);
    }
}
