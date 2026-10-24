using System;
using System.Numerics;
using Neo.Damage;
using Neo.Flight;
using Neo.Shell;
using Xunit;

namespace Neo.Plane.Tests;

public class PlaneModuleTests
{
    private static readonly TickContext OneStep = new(1, 1.0 / 60);

    private static (PlaneModule plane, FakeBus bus) Started()
    {
        var bus = new FakeBus();
        var plane = new PlaneModule();
        plane.Start(bus);
        return (plane, bus);
    }

    private static PlaneState State(int id, float x, float throttle = 0.5f) =>
        new(id, new Vector3(x, 400f, -x), Quaternion.CreateFromYawPitchRoll(x * 0.01f, 0.1f, 0.2f),
            new Vector3(0, 0, -42f), 42f, throttle);

    [Fact]
    public void HasNoStateBeforeAnyArrives()
    {
        var (plane, _) = Started();
        plane.Tick(in OneStep);
        Assert.False(plane.HasState);
        Assert.Equal(0, plane.Updates);
    }

    [Fact]
    public void SubscribesToPlaneStateAndDamageInStart()
    {
        var (_, bus) = Started();
        Assert.Contains(bus.Handlers, h => h is IMessageHandler<PlaneState>);
        Assert.Contains(bus.Handlers, h => h is IMessageHandler<PlaneDamageState>);
    }

    [Fact]
    public void KeepsTheLatestStateOfPlaneZero()
    {
        var (plane, bus) = Started();
        bus.Send(State(0, 1f, 0.2f));
        bus.Send(State(0, 2f, 0.9f));
        plane.Tick(in OneStep);

        Assert.True(plane.HasState);
        Assert.Equal(2, plane.Updates);
        var latest = plane.Latest;
        Assert.Equal(0, latest.PlaneId);
        Assert.Equal(new Vector3(2f, 400f, -2f), latest.Position);
        Assert.Equal(State(0, 2f).Orientation, latest.Orientation);
        Assert.Equal(new Vector3(0, 0, -42f), latest.Velocity);
        Assert.Equal(42f, latest.Speed);
        Assert.Equal(0.9f, latest.Throttle);
    }

    [Fact]
    public void KeepsThePreviousStateForInterpolation()
    {
        var (plane, bus) = Started();
        bus.Send(State(0, 1f));
        Assert.Equal(plane.Latest.Position, plane.Previous.Position);

        bus.Send(State(0, 2f));
        bus.Send(State(0, 3f));
        Assert.Equal(new Vector3(2f, 400f, -2f), plane.Previous.Position);
        Assert.Equal(new Vector3(3f, 400f, -3f), plane.Latest.Position);
    }

    [Fact]
    public void KeepsStatesForEveryPlaneId()
    {
        var (plane, bus) = Started();
        for (int round = 1; round <= 3; round++)
            for (int id = 0; id < Teams.PlaneCount; id++)
                bus.Send(State(id, id * 10f + round, id * 0.1f));

        Assert.Equal(Teams.PlaneCount, plane.Count);
        for (int i = 0; i < plane.Count; i++)
            Assert.Equal(i, plane.IdAt(i));
        for (int id = 0; id < Teams.PlaneCount; id++)
        {
            Assert.True(plane.Has(id));
            Assert.Equal(3, plane.UpdatesOf(id));
            Assert.Equal(id, plane.LatestOf(id).PlaneId);
            Assert.Equal(new Vector3(id * 10f + 3, 400f, -(id * 10f + 3)), plane.LatestOf(id).Position);
            Assert.Equal(new Vector3(id * 10f + 2, 400f, -(id * 10f + 2)), plane.PreviousOf(id).Position);
            Assert.Equal(id * 0.1f, plane.LatestOf(id).Throttle);
        }
        // The player's shortcuts read plane 0.
        Assert.Equal(plane.LatestOf(0).Position, plane.Latest.Position);
        Assert.Equal(plane.PreviousOf(0).Position, plane.Previous.Position);
        Assert.Equal(3, plane.Updates);
    }

    [Fact]
    public void IdsComeInTheOrderFirstSeenAndPlanesStayApart()
    {
        var (plane, bus) = Started();
        bus.Send(State(5, 1f));
        Assert.False(plane.HasState);
        Assert.False(plane.Has(0));
        bus.Send(State(2, 2f));
        bus.Send(State(5, 3f));
        bus.Send(State(0, 4f));

        Assert.Equal(3, plane.Count);
        Assert.Equal(5, plane.IdAt(0));
        Assert.Equal(2, plane.IdAt(1));
        Assert.Equal(0, plane.IdAt(2));
        Assert.Equal(2, plane.UpdatesOf(5));
        Assert.Equal(new Vector3(1f, 400f, -1f), plane.PreviousOf(5).Position);
        Assert.Equal(new Vector3(2f, 400f, -2f), plane.PreviousOf(2).Position);
        Assert.Equal(new Vector3(4f, 400f, -4f), plane.Latest.Position);
        Assert.False(plane.Has(7));
        Assert.Equal(0, plane.UpdatesOf(7));
    }

    [Fact]
    public void IgnoresIdsOutOfRange()
    {
        var (plane, bus) = Started();
        bus.Send(State(-1, 6f));
        bus.Send(State(PlaneModule.MaxPlanes, 6f));
        bus.Send(new PlaneDamageState(-1, 0f, 0f, 0f, 0f, false));
        bus.Send(new PlaneDamageState(PlaneModule.MaxPlanes, 0f, 0f, 0f, 0f, false));
        Assert.Equal(0, plane.Count);
        Assert.False(plane.Has(-1));
        Assert.False(plane.Has(PlaneModule.MaxPlanes));
        Assert.False(plane.HasDamage(-1));
        Assert.Equal(0, plane.UpdatesOf(int.MaxValue));
    }

    [Fact]
    public void KeepsTheDamageOfEachPlane()
    {
        var (plane, bus) = Started();
        Assert.False(plane.HasDamage(4));
        Assert.Equal(1f, plane.DamageOf(4).Engine); // undamaged until told otherwise
        Assert.True(plane.DamageOf(4).Alive);

        bus.Send(new PlaneDamageState(4, 0.3f, 0.9f, 0.2f, 0.7f, true));
        bus.Send(new PlaneDamageState(1, 1f, 0.4f, 1f, 1f, true));
        Assert.True(plane.HasDamage(4));
        var d = plane.DamageOf(4);
        Assert.Equal(0.3f, d.Engine);
        Assert.Equal(0.9f, d.LeftWing);
        Assert.Equal(0.2f, d.RightWing);
        Assert.Equal(0.7f, d.Tail);
        Assert.Equal(0.4f, plane.DamageOf(1).LeftWing);
        Assert.Equal(1f, plane.DamageOf(0).Engine);
        Assert.Equal(0, plane.Count); // damage alone is not a plane to draw
    }

    [Fact]
    public void DownedPlaneFallsThenCrashesOnceThenRespawns()
    {
        var (plane, bus) = Started();
        bus.Send(At(4, 300f, true));
        Assert.Equal(PlaneLife.Flying, plane.LifeOf(4));

        // Damage downs it first; Flight follows.
        bus.Send(new PlaneDamageState(4, 0f, 1f, 1f, 1f, false));
        Assert.Equal(PlaneLife.Falling, plane.LifeOf(4));
        bus.Send(At(4, 250f, false));
        bus.Send(At(4, 20f, false));
        Assert.Equal(PlaneLife.Falling, plane.LifeOf(4));
        Assert.Equal(0, plane.CrashesOf(4));

        bus.Send(At(4, 0f, false, speed: 0f));
        Assert.Equal(PlaneLife.Crashed, plane.LifeOf(4));
        Assert.Equal(1, plane.CrashesOf(4));
        bus.Send(At(4, 0f, false, speed: 0f));
        bus.Send(new PlaneDamageState(4, 0f, 1f, 1f, 1f, false));
        Assert.Equal(1, plane.CrashesOf(4)); // once per crash

        // Damage respawns it before Flight moves it: still down (no plane on the wreck).
        bus.Send(new PlaneDamageState(4, 1f, 1f, 1f, 1f, true));
        Assert.Equal(PlaneLife.Crashed, plane.LifeOf(4));
        // Flight puts it at its start point: flying, and no slide from the wreck.
        bus.Send(At(4, 500f, true, x: 900f));
        Assert.Equal(PlaneLife.Flying, plane.LifeOf(4));
        Assert.Equal(plane.LatestOf(4).Position, plane.PreviousOf(4).Position);
        bus.Send(At(4, 501f, true, x: 900f));
        Assert.Equal(new Vector3(900f, 500f, 0f), plane.PreviousOf(4).Position);

        // And down again: a second crash.
        bus.Send(At(4, 0f, false, speed: 0f));
        Assert.Equal(2, plane.CrashesOf(4));
        Assert.Equal(PlaneLife.Flying, plane.LifeOf(5));
    }

    [Fact]
    public void FlightAloneCanDownAPlane()
    {
        var (plane, bus) = Started();
        bus.Send(At(2, 300f, true));
        bus.Send(At(2, 290f, false));
        Assert.Equal(PlaneLife.Falling, plane.LifeOf(2));
        Assert.Equal(new Vector3(0f, 300f, 0f), plane.PreviousOf(2).Position); // falling still interpolates
        bus.Send(At(2, 0.3f, false, speed: 40f));
        Assert.Equal(PlaneLife.Crashed, plane.LifeOf(2));
        Assert.Equal(1, plane.CrashesOf(2));
    }

    [Fact]
    public void CrashOnAHillExplodesOnItsSlope()
    {
        var (plane, bus) = Started();
        float ground = Neo.World.Terrain.HeightAt(1350f, 150f);
        bus.Send(At(5, ground + 300f, true, x: 1350f, z: 150f));
        bus.Send(At(5, ground + 30f, false, x: 1350f, z: 150f));
        Assert.Equal(PlaneLife.Falling, plane.LifeOf(5));
        bus.Send(At(5, ground + 0.4f, false, x: 1350f, z: 150f)); // far above GroundY
        Assert.Equal(PlaneLife.Crashed, plane.LifeOf(5));
        Assert.Equal(1, plane.CrashesOf(5));
        Assert.Equal(CrashLook.Explosion, plane.CrashLookOf(5));
        Assert.Equal(new Vector3(1350f, ground, 150f), plane.CrashPointOf(5));
        // Flight drags the wreck on into the hill: the crash stays where it hit.
        bus.Send(At(5, ground - 20f, false, x: 1351f, z: 150f));
        Assert.Equal(1, plane.CrashesOf(5));
        Assert.Equal(new Vector3(1350f, ground, 150f), plane.CrashPointOf(5));
    }

    [Fact]
    public void CrashOnWaterSplashesThenOnLandExplodes()
    {
        var (plane, bus) = Started();
        float river = Neo.World.Terrain.RiverX(0f);
        bus.Send(At(6, 300f, true, x: river));
        bus.Send(At(6, 0.3f, false, x: river));
        Assert.Equal(PlaneLife.Crashed, plane.LifeOf(6));
        Assert.Equal(CrashLook.Splash, plane.CrashLookOf(6));
        Assert.Equal(new Vector3(river, Neo.World.Terrain.WaterLevel, 0f), plane.CrashPointOf(6));

        bus.Send(At(6, 500f, true));
        bus.Send(At(6, 0f, false));
        Assert.Equal(2, plane.CrashesOf(6));
        Assert.Equal(CrashLook.Explosion, plane.CrashLookOf(6));
        Assert.Equal(Neo.World.Terrain.HeightAt(0f, 0f), plane.CrashPointOf(6).Y);
    }

    private static PlaneState At(int id, float height, bool alive, float speed = 50f, float x = 0f, float z = 0f) =>
        new(id, new Vector3(x, height, z), Quaternion.Identity, new Vector3(0, 0, -speed), speed, 0.5f, alive);

    [Fact]
    public void PublishesNothing()
    {
        var (plane, bus) = Started();
        bus.Send(State(0, 1f));
        plane.Tick(in OneStep);
        Assert.Equal(0, bus.Published);
    }

    [Fact]
    public void TickAndStatesDoNotAllocate()
    {
        var (plane, bus) = Started();
        IMessageHandler<PlaneState> handler = null!;
        IMessageHandler<PlaneDamageState> damageHandler = null!;
        foreach (var h in bus.Handlers)
        {
            if (h is IMessageHandler<PlaneState> typed)
                handler = typed;
            if (h is IMessageHandler<PlaneDamageState> damageTyped)
                damageHandler = damageTyped;
        }
        var mine = State(0, 1f);
        var other = State(5, 2f);
        var downed = new PlaneState(6, new Vector3(0, 0, 0), Quaternion.Identity, Vector3.Zero, 0f, 0f, false);
        var damage = new PlaneDamageState(5, 0.2f, 0.3f, 1f, 1f, true);
        handler.Handle(in mine);
        plane.Tick(in OneStep);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            handler.Handle(in mine);
            handler.Handle(in other);
            handler.Handle(in downed);
            damageHandler.Handle(in damage);
            plane.Tick(in OneStep);
        }
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        Assert.Equal(1001, plane.Updates);
    }

    [Fact]
    public void IsTheModulesOnlyEntryClass()
    {
        int entries = 0;
        foreach (var type in typeof(PlaneModule).Assembly.GetExportedTypes())
            if (type.IsClass && !type.IsAbstract && typeof(IModule).IsAssignableFrom(type))
                entries++;
        Assert.Equal(1, entries);
        Assert.NotNull(typeof(PlaneModule).GetConstructor(Type.EmptyTypes));
    }
}
