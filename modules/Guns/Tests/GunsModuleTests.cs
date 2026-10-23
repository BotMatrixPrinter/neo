using System;
using System.Numerics;
using System.Reflection;
using System.Runtime.Versioning;
using Neo.Flight;
using Neo.Input;
using Neo.Shell;
using Neo.Targets;
using Xunit;

namespace Neo.Guns.Tests;

public class GunsModuleTests
{
    private const double Step = 1.0 / 60;
    private const float Dt = 1f / 60;
    private static readonly TickContext OneStep = new(1, Step);
    private static readonly Vector3 Start = new(0f, 300f, 0f);
    private static readonly Vector3 PlaneVelocity = new(0f, 0f, -50f);

    private static (GunsModule guns, FakeBus bus) Started(bool fire, Quaternion? orientation = null)
    {
        var bus = new FakeBus();
        var guns = new GunsModule();
        guns.Start(bus);
        var q = orientation ?? Quaternion.Identity;
        bus.Send(new PlaneState(0, Start, q, Vector3.Transform(PlaneVelocity, q), 50f, 1f));
        Trigger(bus, fire);
        return (guns, bus);
    }

    private static void Trigger(FakeBus bus, bool fire) =>
        bus.Send(new ControlInput(0, 0, 0, 0, 1f, fire, false, Vector3.Zero));

    private static void Ticks(GunsModule guns, int count)
    {
        for (int i = 0; i < count; i++)
            guns.Tick(in OneStep);
    }

    private static Bullet Single(GunsModule guns, int serial)
    {
        foreach (var b in guns.Bullets)
            if (b.Alive && b.Serial == serial)
                return b;
        throw new Xunit.Sdk.XunitException($"no live bullet {serial}");
    }

    [Fact]
    public void EachGunFiresTenRoundsPerSecond()
    {
        var (guns, bus) = Started(fire: true);
        Ticks(guns, 60); // 1 s
        Assert.Equal(20, guns.State.ShotsFired);
        Ticks(guns, 60); // 2 s
        Assert.Equal(40, guns.State.ShotsFired);
        int left = 0, right = 0;
        foreach (var b in guns.Bullets)
            if (b.Alive)
            {
                if (b.Gun == 0) left++;
                else right++;
            }
        Assert.InRange(left, 14, 15); // 1.5 s of bullets in flight per gun
        Assert.Equal(left, right);
        Assert.Equal(left + right, guns.LiveBullets);
    }

    [Fact]
    public void NoFireNoShotsAndReleasingStops()
    {
        var (guns, bus) = Started(fire: false);
        Ticks(guns, 60);
        Assert.Equal(0, guns.State.ShotsFired);
        Assert.False(guns.State.Firing);
        Assert.Equal(0, guns.LiveBullets);

        Trigger(bus, true);
        Ticks(guns, 1);
        Assert.Equal(2, guns.State.ShotsFired); // the first volley comes at once
        Assert.True(guns.State.Firing);
        Trigger(bus, false);
        Ticks(guns, 60);
        Assert.Equal(2, guns.State.ShotsFired);
    }

    [Fact]
    public void OtherPlanesControlsAreIgnoredAndNoPlaneStateMeansNoShots()
    {
        var (guns, bus) = Started(fire: false);
        bus.Send(new ControlInput(1, 0, 0, 0, 1f, true, false, Vector3.Zero));
        Ticks(guns, 30);
        Assert.Equal(0, guns.State.ShotsFired);

        var lone = new GunsModule();
        var loneBus = new FakeBus();
        lone.Start(loneBus);
        Trigger(loneBus, true);
        lone.Tick(in OneStep);
        Assert.Equal(0, lone.State.ShotsFired);
    }

    [Fact]
    public void BulletsStartAtTheMuzzlesWithPlaneVelocityPlus450AlongTheNose()
    {
        // Plane yawed 90 degrees left: its nose (-Z) points along world -X, its right (+X) along world -Z.
        var yaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
        var (guns, _) = Started(fire: true, yaw);
        Ticks(guns, 1);
        var nose = new Vector3(-1f, 0f, 0f);
        var expected = new Vector3(-50f, 0f, 0f) + nose * 450f;
        for (int serial = 0; serial < 2; serial++)
        {
            var b = Single(guns, serial);
            Assert.True(Vector3.Distance(expected, b.Velocity) < 1e-3f, $"velocity {b.Velocity}");
            var muzzle = guns.MuzzlePosition(b.Gun);
            Assert.True(Vector3.Distance(muzzle, b.PreviousPosition) < 1e-3f);
            Assert.True(Vector3.Distance(muzzle + expected * Dt, b.Position) < 1e-3f);
        }
        var left = guns.MuzzlePosition(0) - Start;
        var right = guns.MuzzlePosition(1) - Start;
        // About 0.5 m either side of the nose: left gun on world +Z, right on world -Z here.
        Assert.InRange(left.Z, 0.4f, 0.6f);
        Assert.InRange(right.Z, -0.6f, -0.4f);
        Assert.True(left.X < 0 && right.X < 0, "muzzles are forward of the plane's center");
    }

    [Fact]
    public void BulletsFlyStraightForOneAndAHalfSeconds()
    {
        var (guns, bus) = Started(fire: true);
        Ticks(guns, 1);
        Trigger(bus, false);
        var muzzle = guns.MuzzlePosition(0);
        var velocity = Single(guns, 0).Velocity;

        Ticks(guns, 59); // 1 s in flight
        var b = Single(guns, 0);
        Assert.True(Vector3.Distance(muzzle + velocity * 1f, b.Position) < 0.05f, $"at {b.Position}");
        Assert.Equal(velocity, b.Velocity);
        Assert.InRange(Vector3.Distance(muzzle, b.Position), 499f, 501f); // 500 m/s

        Ticks(guns, 29); // 89 steps: 1.483 s
        Assert.Equal(2, guns.LiveBullets);
        Ticks(guns, 1); // 1.5 s
        Assert.Equal(0, guns.LiveBullets);
    }

    [Fact]
    public void AHitRemovesTheBulletAndPublishesOneDamageTarget()
    {
        var (guns, bus) = Started(fire: true);
        bus.Send(new TargetState(3, Start + new Vector3(0f, 0f, -200f), 9f, 15, true));
        Ticks(guns, 1);
        Trigger(bus, false);
        bus.Clear();

        Ticks(guns, 30); // 500 m/s: 200 m takes about 0.4 s
        var damage = bus.All<DamageTarget>();
        Assert.Equal(2, damage.Count); // one per bullet, both guns hit
        foreach (var d in damage)
        {
            Assert.Equal(3, d.TargetId);
            Assert.Equal(1, d.Amount);
        }
        Assert.Equal(0, guns.LiveBullets);
        Assert.Equal(2, guns.Hits);
        Assert.InRange(guns.HitPosition(0).Z, -192f, -190f); // the sphere's near side
    }

    [Fact]
    public void AFastBulletCannotSkipThroughAThinTarget()
    {
        // 8.3 m per step; a 0.6 m sphere between two step ends is still hit (segment test).
        var (guns, bus) = Started(fire: true);
        bus.Send(new TargetState(0, guns.MuzzlePosition(0) + new Vector3(0f, 0f, -104f), 0.6f, 15, true));
        Ticks(guns, 1);
        Trigger(bus, false);
        bus.Clear();
        Ticks(guns, 30);
        var d = Assert.Single(bus.All<DamageTarget>());
        Assert.Equal(0, d.TargetId);
        Assert.Equal(1, guns.LiveBullets); // the right gun's bullet, 1 m to the side, missed
    }

    [Fact]
    public void MissesAndDeadTargetsPublishNoDamage()
    {
        var (guns, bus) = Started(fire: true);
        bus.Send(new TargetState(0, Start + new Vector3(0f, 50f, -200f), 9f, 15, true)); // above the line
        bus.Send(new TargetState(1, Start + new Vector3(0f, 0f, 200f), 9f, 15, true)); // behind
        bus.Send(new TargetState(2, Start + new Vector3(0f, 0f, -300f), 9f, 0, false)); // popped
        bus.Send(new TargetState(3, Start + new Vector3(0f, 0f, -1500f), 9f, 15, true)); // out of range
        Ticks(guns, 120);
        Assert.Equal(40, guns.State.ShotsFired);
        Assert.Empty(bus.All<DamageTarget>());
        Assert.Equal(0, guns.Hits);
    }

    [Fact]
    public void ATargetThatComesBackAliveCanBeHitAgain()
    {
        var (guns, bus) = Started(fire: true);
        var where = Start + new Vector3(0f, 0f, -200f);
        bus.Send(new TargetState(2, where, 9f, 0, false));
        Ticks(guns, 30);
        Assert.Empty(bus.All<DamageTarget>());
        bus.Send(new TargetState(2, where, 9f, 15, true));
        Ticks(guns, 30);
        Assert.NotEmpty(bus.All<DamageTarget>());
    }

    [Fact]
    public void GunStateIsPublishedEveryTick()
    {
        var (guns, bus) = Started(fire: true);
        for (int tick = 1; tick <= 3; tick++)
        {
            bus.Clear();
            guns.Tick(new TickContext(tick, Step));
            var state = Assert.Single(bus.All<GunState>());
            Assert.Equal(0, state.PlaneId);
            Assert.True(state.Firing);
            Assert.Equal(2, state.ShotsFired);
            Assert.Equal(guns.State.Heat, state.Heat);
        }
    }

    [Fact]
    public void HeatRisesWhileFiringAndOverheatsAfterAboutFourSeconds()
    {
        var (guns, bus) = Started(fire: true);
        Ticks(guns, 120);
        Assert.InRange(guns.State.Heat, 0.45f, 0.55f);
        Assert.False(guns.State.Overheated);

        Ticks(guns, 108); // 3.8 s
        Assert.False(guns.State.Overheated);
        Assert.True(guns.State.Firing);
        Ticks(guns, 24); // 4.2 s
        Assert.True(guns.State.Overheated);
        Assert.InRange(guns.State.Heat, 0.85f, 1f); // full at about 4 s, cooling since

        int shots = guns.State.ShotsFired;
        Assert.InRange(shots, 76, 84); // 2 x 10/s for about 4 s
        bus.Clear();
        Ticks(guns, 30); // trigger still held, but the guns are stopped
        Assert.Equal(shots, guns.State.ShotsFired);
        Assert.False(guns.State.Firing);
        Assert.All(bus.All<GunState>(), s => Assert.True(s.Overheated));
    }

    [Fact]
    public void OverheatedGunsCoolAndFireAgainOnlyBelowPointThree()
    {
        var (guns, bus) = Started(fire: true);
        while (!guns.State.Overheated)
            guns.Tick(in OneStep);
        Assert.Equal(1f, guns.State.Heat);
        int shots = guns.State.ShotsFired;

        // Still holding fire: cooling from about 1 to 0.3 takes about 1.4 s.
        int stopped = 0;
        while (guns.State.Overheated && stopped < 600)
        {
            Assert.Equal(shots, guns.State.ShotsFired);
            Assert.True(guns.State.Heat >= 0.3f - 1e-4f);
            guns.Tick(in OneStep);
            stopped++;
        }
        Assert.False(guns.State.Overheated);
        Assert.InRange(stopped / 60.0, 1.0, 1.6);
        Assert.True(guns.State.Heat < 0.3f);

        guns.Tick(in OneStep);
        Assert.True(guns.State.Firing);
        Assert.True(guns.State.ShotsFired > shots);
    }

    [Fact]
    public void HeatCoolsFromFullInAboutTwoSecondsWhenNotFiring()
    {
        var (guns, bus) = Started(fire: true);
        Ticks(guns, 120); // half hot
        Trigger(bus, false);
        Ticks(guns, 30);
        Assert.InRange(guns.State.Heat, 0.2f, 0.3f);
        Assert.False(guns.State.Firing);
        Ticks(guns, 60);
        Assert.Equal(0f, guns.State.Heat);

        Trigger(bus, true);
        Ticks(guns, 300); // overheat
        Assert.True(guns.State.Overheated);
        Trigger(bus, false);
        float heat = guns.State.Heat;
        Ticks(guns, 60);
        Assert.InRange(heat - guns.State.Heat, 0.45f, 0.55f); // half of full heat per second
        Ticks(guns, 60);
        Assert.Equal(0f, guns.State.Heat);
        Assert.False(guns.State.Overheated);
    }

    [Fact]
    public void TickBeforeStartDoesNotThrow()
    {
        var guns = new GunsModule();
        guns.Tick(in OneStep);
        Assert.Equal(0, guns.State.ShotsFired);
    }

    [Fact]
    public void TickDoesNotAllocateAndReusesThePool()
    {
        var bus = new QuietBus();
        var guns = new GunsModule();
        guns.Start(bus);
        var plane = new PlaneState(0, Start, Quaternion.Identity, PlaneVelocity, 50f, 1f);
        var fire = new ControlInput(0, 0, 0, 0, 1f, true, false, Vector3.Zero);
        var target = new TargetState(0, Start + new Vector3(0f, 0f, -300f), 9f, 15, true);
        var miss = new TargetState(1, Start + new Vector3(0f, 80f, -300f), 9f, 15, true);
        bus.Plane!.Handle(in plane);
        bus.Input!.Handle(in fire);
        bus.Target!.Handle(in target);
        bus.Target.Handle(in miss);
        guns.Tick(in OneStep);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 6000; i++) // 100 s: far more shots than the pool holds
        {
            bus.Plane.Handle(in plane);
            bus.Input.Handle(in fire);
            bus.Target.Handle(in target);
            bus.Target.Handle(in miss);
            guns.Tick(in OneStep);
        }
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        Assert.True(guns.State.ShotsFired > GunsModule.PoolSize * 10, $"{guns.State.ShotsFired} shots");
        Assert.True(bus.Damages > GunsModule.PoolSize, $"{bus.Damages} hits");
        Assert.Equal(6001, bus.States);
        Assert.Equal(GunsModule.PoolSize, guns.Bullets.Length);
    }

    [Fact]
    public void IsTheModulesOnlyEntryClass()
    {
        int entries = 0;
        foreach (var type in typeof(GunsModule).Assembly.GetExportedTypes())
            if (type.IsClass && !type.IsAbstract && typeof(IModule).IsAssignableFrom(type))
                entries++;
        Assert.Equal(1, entries);
    }

    [Fact]
    public void CoreAndContractTargetNet10()
    {
        foreach (var assembly in new[] { typeof(GunsModule).Assembly, typeof(GunState).Assembly })
            Assert.Equal(".NETCoreApp,Version=v10.0", assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName);
    }

    [Fact]
    public void CoreAndContractDoNotReferenceGodot()
    {
        foreach (var assembly in new[] { typeof(GunsModule).Assembly, typeof(GunState).Assembly })
            foreach (var name in assembly.GetReferencedAssemblies())
                Assert.DoesNotContain("Godot", name.Name);
    }
}
