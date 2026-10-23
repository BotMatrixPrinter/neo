using System;
using System.Numerics;
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
    public void SubscribesToPlaneStateInStart()
    {
        var (_, bus) = Started();
        Assert.Contains(bus.Handlers, h => h is IMessageHandler<PlaneState>);
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
    public void IgnoresOtherPlaneIds()
    {
        var (plane, bus) = Started();
        bus.Send(State(1, 5f));
        bus.Send(State(-1, 6f));
        Assert.False(plane.HasState);

        bus.Send(State(0, 1f, 0.3f));
        bus.Send(State(3, 9f, 1f));
        bus.Send(State(7, 8f, 0f));
        Assert.Equal(1, plane.Updates);
        Assert.Equal(new Vector3(1f, 400f, -1f), plane.Latest.Position);
        Assert.Equal(new Vector3(1f, 400f, -1f), plane.Previous.Position);
        Assert.Equal(0.3f, plane.Latest.Throttle);
    }

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
        foreach (var h in bus.Handlers)
            if (h is IMessageHandler<PlaneState> typed)
                handler = typed;
        var mine = State(0, 1f);
        var other = State(1, 2f);
        handler.Handle(in mine);
        plane.Tick(in OneStep);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            handler.Handle(in mine);
            handler.Handle(in other);
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
