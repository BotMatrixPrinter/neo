using System;
using System.Collections.Generic;
using Neo.Shell.Host;
using Xunit;

namespace Neo.Shell.Tests;

public class ModuleHostTests
{
    private readonly struct Ping { }

    private class CountingModule : IModule
    {
        public IMessageBus? Bus;
        public int Ticks;
        public long LastTick;
        public double LastStep;

        public virtual void Start(IMessageBus bus) => Bus = bus;

        public virtual void Tick(in TickContext tick)
        {
            Ticks++;
            LastTick = tick.Tick;
            LastStep = tick.StepSeconds;
        }
    }

    private sealed class ThrowsInTick : CountingModule
    {
        public override void Tick(in TickContext tick)
        {
            base.Tick(tick);
            if (Ticks == 2)
                throw new InvalidOperationException("broken module");
        }
    }

    private sealed class ThrowsInStart : CountingModule
    {
        public override void Start(IMessageBus bus) => throw new InvalidOperationException("no start");
    }

    private sealed class PingsEveryTick : CountingModule
    {
        public override void Tick(in TickContext tick)
        {
            base.Tick(tick);
            Bus!.Publish(new Ping());
        }
    }

    private sealed class ThrowsOnPing : CountingModule, IMessageHandler<Ping>
    {
        public int Pings;
        public override void Start(IMessageBus bus) => bus.Subscribe<Ping>(this);
        public void Handle(in Ping message)
        {
            Pings++;
            throw new InvalidOperationException("bad handler");
        }
    }

    private readonly List<string> _log = new();

    private ModuleHost NewHost() => new(new MessageBus(), _log.Add);

    [Fact]
    public void StartsAndTicksEveryModuleWithFixedSteps()
    {
        var host = NewHost();
        var a = new CountingModule();
        var b = new CountingModule();
        host.Add("A", () => a);
        host.Add("B", () => b);
        host.Start();

        host.Step(0.5);
        host.Step(0.5);
        host.Step(0.5);

        Assert.NotNull(a.Bus);
        Assert.Equal(3, a.Ticks);
        Assert.Equal(3, b.Ticks);
        Assert.Equal(3, a.LastTick);
        Assert.Equal(0.5, b.LastStep);
        Assert.Equal(3, host.Tick);
        Assert.Empty(_log);
    }

    [Fact]
    public void ModuleThatThrowsInTickIsDisabledLoggedAndOthersGoOn()
    {
        var host = NewHost();
        var before = new CountingModule();
        var broken = new ThrowsInTick();
        var after = new CountingModule();
        host.Add("Before", () => before);
        var slot = host.Add("Broken", () => broken);
        host.Add("After", () => after);
        var disabled = new List<string>();
        host.ModuleDisabled += s => disabled.Add(s.Name);
        host.Start();

        for (int i = 0; i < 5; i++)
            host.Step(0.1);

        Assert.Equal(2, broken.Ticks);
        Assert.Equal(5, before.Ticks);
        Assert.Equal(5, after.Ticks);
        Assert.False(slot.Enabled);
        Assert.Equal("broken module", slot.Fault?.Message);
        Assert.Equal(new[] { "Broken" }, disabled);
        var line = Assert.Single(_log);
        Assert.StartsWith(ModuleHost.DisabledMarker + " Broken:", line);
    }

    [Fact]
    public void ModuleThatThrowsInStartIsNeverTicked()
    {
        var host = NewHost();
        var broken = new ThrowsInStart();
        var ok = new CountingModule();
        var slot = host.Add("Broken", () => broken);
        host.Add("Ok", () => ok);
        host.Start();
        host.Step(0.1);

        Assert.False(slot.Enabled);
        Assert.Equal(0, broken.Ticks);
        Assert.Equal(1, ok.Ticks);
        Assert.StartsWith(ModuleHost.DisabledMarker + " Broken:", Assert.Single(_log));
    }

    [Fact]
    public void ModuleThatThrowsInConstructorIsDisabled()
    {
        var host = NewHost();
        var slot = host.Add("Broken", () => throw new InvalidOperationException("ctor"));
        host.Start();
        host.Step(0.1);
        Assert.False(slot.Enabled);
        Assert.Null(slot.Module);
        Assert.Single(_log);
    }

    [Fact]
    public void ModuleWhoseHandlerThrowsIsDisabledAndPublisherGoesOn()
    {
        var host = NewHost();
        var publisher = new PingsEveryTick();
        var listener = new ThrowsOnPing();
        var pubSlot = host.Add("Publisher", () => publisher);
        var badSlot = host.Add("Listener", () => listener);
        host.Start();

        host.Step(0.1);
        host.Step(0.1);

        Assert.True(pubSlot.Enabled);
        Assert.Equal(2, publisher.Ticks);
        Assert.False(badSlot.Enabled);
        Assert.Equal(1, listener.Pings);
        Assert.Equal(0, listener.Ticks);
        Assert.StartsWith(ModuleHost.DisabledMarker + " Listener:", Assert.Single(_log));
    }

    public sealed class Entry : IModule
    {
        public void Start(IMessageBus bus) { }
        public void Tick(in TickContext tick) { }
    }

    public sealed class OtherEntry : IModule
    {
        public void Start(IMessageBus bus) { }
        public void Tick(in TickContext tick) { }
    }

    public abstract class AbstractEntry : IModule
    {
        public abstract void Start(IMessageBus bus);
        public abstract void Tick(in TickContext tick);
    }

    public sealed class NoDefaultConstructor : IModule
    {
        public NoDefaultConstructor(int x) { }
        public void Start(IMessageBus bus) { }
        public void Tick(in TickContext tick) { }
    }

    public sealed class ThrowingConstructor : IModule
    {
        public ThrowingConstructor() => throw new InvalidOperationException("ctor");
        public void Start(IMessageBus bus) { }
        public void Tick(in TickContext tick) { }
    }

    [Fact]
    public void DiscoversTheOneModuleClass()
    {
        var host = NewHost();
        var slot = host.Add("M", new[] { typeof(string), typeof(AbstractEntry), typeof(Entry), typeof(Ping) });
        Assert.True(slot.Enabled);
        Assert.IsType<Entry>(slot.Module);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("two")]
    [InlineData("noctor")]
    [InlineData("throws")]
    public void BadModuleClassDisablesOnlyThatModule(string kind)
    {
        var types = kind switch
        {
            "none" => new[] { typeof(string) },
            "two" => new[] { typeof(Entry), typeof(OtherEntry) },
            "noctor" => new[] { typeof(NoDefaultConstructor) },
            _ => new[] { typeof(ThrowingConstructor) },
        };
        var host = NewHost();
        var bad = host.Add("Bad", types);
        var good = host.Add("Good", new[] { typeof(Entry) });
        host.Start();
        host.Step(0.1);

        Assert.False(bad.Enabled);
        Assert.True(good.Enabled);
        Assert.StartsWith(ModuleHost.DisabledMarker + " Bad:", Assert.Single(_log));
    }

    [Fact]
    public void DiscoversFromAnAssembly()
    {
        var host = NewHost();
        // This test assembly holds several IModule classes, so discovery must refuse it.
        var slot = host.Add("Tests", typeof(ModuleHostTests).Assembly);
        Assert.False(slot.Enabled);
        Assert.Contains("more than one", slot.Fault?.Message);
    }
}
