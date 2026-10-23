using System;
using System.Reflection;
using System.Runtime.Versioning;
using Neo.Shell;
using Xunit;

namespace Neo.Clock.Tests;

public class ClockModuleTests
{
    [Fact]
    public void EachTickCountsAndPublishesClockTicked()
    {
        var bus = new FakeBus();
        var clock = new ClockModule();
        clock.Start(bus);

        clock.Tick(new TickContext(1, 0.25));
        clock.Tick(new TickContext(2, 0.25));

        Assert.Equal(2, clock.Steps);
        Assert.Equal(0.5, clock.Seconds, 9);
        Assert.Equal(2, bus.Published.Count);
        var last = Assert.IsType<ClockTicked>(bus.Published[1]);
        Assert.Equal(2, last.Tick);
        Assert.Equal(0.5, last.Seconds, 9);
    }

    [Fact]
    public void TickBeforeStartDoesNotThrow()
    {
        var clock = new ClockModule();
        clock.Tick(new TickContext(1, 0.25));
        Assert.Equal(1, clock.Steps);
    }

    [Fact]
    public void TickDoesNotAllocate()
    {
        var clock = new ClockModule();
        clock.Start(new NullBus());
        var tick = new TickContext(1, 1.0 / 60);
        clock.Tick(in tick);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
            clock.Tick(in tick);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    [Fact]
    public void IsTheModulesOnlyEntryClass()
    {
        int entries = 0;
        foreach (var type in typeof(ClockModule).Assembly.GetExportedTypes())
            if (type.IsClass && !type.IsAbstract && typeof(IModule).IsAssignableFrom(type))
                entries++;
        Assert.Equal(1, entries);
    }

    [Fact]
    public void CoreAndContractTargetNet10()
    {
        foreach (var assembly in new[] { typeof(ClockModule).Assembly, typeof(ClockTicked).Assembly })
            Assert.Equal(".NETCoreApp,Version=v10.0", assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName);
    }

    [Fact]
    public void CoreDoesNotReferenceGodot()
    {
        foreach (var name in typeof(ClockModule).Assembly.GetReferencedAssemblies())
            Assert.DoesNotContain("Godot", name.Name);
    }

    private sealed class NullBus : IMessageBus
    {
        public void Subscribe<T>(IMessageHandler<T> handler) where T : struct { }

        public void Publish<T>(in T message) where T : struct { }
    }
}
