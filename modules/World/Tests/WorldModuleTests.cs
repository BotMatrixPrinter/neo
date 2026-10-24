using System;
using System.Reflection;
using System.Runtime.Versioning;
using Neo.Shell;
using Xunit;

namespace Neo.World.Tests;

public class WorldModuleTests
{
    [Fact]
    public void StartsAndTicks()
    {
        var bus = new FakeBus();
        var world = new WorldModule();
        world.Start(bus);

        world.Tick(new TickContext(1, 1.0 / 60));
        world.Tick(new TickContext(2, 1.0 / 60));

        Assert.True(world.Started);
        Assert.Equal(2, world.Steps);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public void TickBeforeStartDoesNotThrow()
    {
        var world = new WorldModule();
        world.Tick(new TickContext(1, 1.0 / 60));
        Assert.Equal(1, world.Steps);
    }

    [Fact]
    public void TickDoesNotAllocate()
    {
        var world = new WorldModule();
        world.Start(new FakeBus());
        var tick = new TickContext(1, 1.0 / 60);
        world.Tick(in tick);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
            world.Tick(in tick);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    [Fact]
    public void IsTheModulesOnlyEntryClassAndHasAParameterlessConstructor()
    {
        int entries = 0;
        foreach (var type in typeof(WorldModule).Assembly.GetExportedTypes())
            if (type.IsClass && !type.IsAbstract && typeof(IModule).IsAssignableFrom(type))
                entries++;
        Assert.Equal(1, entries);
        Assert.NotNull(typeof(WorldModule).GetConstructor(Type.EmptyTypes));
    }

    [Fact]
    public void BoundsAreTheFourKilometerGroundAtZero()
    {
        Assert.Equal(4000f, 2 * WorldBounds.HalfSize);
        Assert.Equal(0f, WorldBounds.GroundY);
        Assert.Equal(Terrain.MaxHeight, WorldBounds.MaxHillHeight);
        Assert.Equal(WorldBounds.GroundY, Terrain.WaterLevel);
    }

    [Fact]
    public void CoreAndContractTargetNet10()
    {
        foreach (var assembly in new[] { typeof(WorldModule).Assembly, typeof(WorldBounds).Assembly })
            Assert.Equal(".NETCoreApp,Version=v10.0", assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName);
    }

    [Fact]
    public void CoreAndContractDoNotReferenceGodot()
    {
        foreach (var assembly in new[] { typeof(WorldModule).Assembly, typeof(WorldBounds).Assembly })
            foreach (var name in assembly.GetReferencedAssemblies())
                Assert.DoesNotContain("Godot", name.Name);
    }
}
