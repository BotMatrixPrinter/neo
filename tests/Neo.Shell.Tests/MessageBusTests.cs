using System;
using System.Collections.Generic;
using Xunit;

namespace Neo.Shell.Tests;

public class MessageBusTests
{
    private readonly struct Ping
    {
        public Ping(int value) => Value = value;
        public int Value { get; }
    }

    private readonly struct Pong { }

    private sealed class Recorder<T> : IMessageHandler<T> where T : struct
    {
        public readonly List<T> Got = new();
        public void Handle(in T message) => Got.Add(message);
    }

    private sealed class Counter : IMessageHandler<Ping>
    {
        public long Sum;
        public void Handle(in Ping message) => Sum += message.Value;
    }

    private sealed class Thrower : IMessageHandler<Ping>
    {
        public int Calls;
        public void Handle(in Ping message)
        {
            Calls++;
            throw new InvalidOperationException("boom");
        }
    }

    [Fact]
    public void DeliversToEverySubscriberOfThatTypeOnly()
    {
        var bus = new MessageBus();
        var a = new Recorder<Ping>();
        var b = new Recorder<Ping>();
        var pong = new Recorder<Pong>();
        bus.Subscribe(a);
        bus.Subscribe(b);
        bus.Subscribe(pong);

        bus.Publish(new Ping(7));

        Assert.Equal(7, Assert.Single(a.Got).Value);
        Assert.Equal(7, Assert.Single(b.Got).Value);
        Assert.Empty(pong.Got);
    }

    [Fact]
    public void PublishWithNoSubscriberDoesNothing()
    {
        new MessageBus().Publish(new Ping(1));
    }

    [Fact]
    public void ManySubscribersAllGetIt()
    {
        var bus = new MessageBus();
        var counters = new Counter[20];
        for (int i = 0; i < counters.Length; i++)
            bus.For(i).Subscribe(counters[i] = new Counter());
        bus.Publish(new Ping(3));
        foreach (var c in counters)
            Assert.Equal(3, c.Sum);
    }

    [Fact]
    public void PublishDoesNotAllocate()
    {
        var bus = new MessageBus();
        var owned = new Counter();
        var unowned = new Counter();
        bus.For(0).Subscribe(owned);
        bus.Subscribe(unowned);
        bus.Publish(new Ping(1));
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
            bus.Publish(new Ping(1));
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        Assert.Equal(10_001, owned.Sum);
    }

    [Fact]
    public void OwnedHandlerThatThrowsIsMutedAndOthersGoOn()
    {
        var bus = new MessageBus();
        var faults = new List<(int, Exception)>();
        bus.HandlerFaulted += (owner, e) => faults.Add((owner, e));
        var thrower = new Thrower();
        var after = new Counter();
        bus.For(4).Subscribe(thrower);
        bus.For(5).Subscribe(after);

        bus.Publish(new Ping(1));
        bus.Publish(new Ping(1));

        Assert.Equal(1, thrower.Calls);
        Assert.Equal(2, after.Sum);
        var (owner, error) = Assert.Single(faults);
        Assert.Equal(4, owner);
        Assert.Equal("boom", error.Message);
        Assert.True(bus.IsMuted(4));
        Assert.False(bus.IsMuted(5));
    }

    [Fact]
    public void MutedOwnerGetsNothing()
    {
        var bus = new MessageBus();
        var c = new Counter();
        bus.For(2).Subscribe(c);
        bus.Mute(2);
        bus.Publish(new Ping(1));
        Assert.Equal(0, c.Sum);
    }

    [Fact]
    public void UnownedHandlerExceptionReachesPublisher()
    {
        var bus = new MessageBus();
        bus.Subscribe(new Thrower());
        Assert.Throws<InvalidOperationException>(() => bus.Publish(new Ping(1)));
    }

    [Fact]
    public void SubscribingWhileDeliveringIsSafe()
    {
        var bus = new MessageBus();
        var late = new Counter();
        bus.Subscribe(new SubscribesOnFirst(bus, late));
        for (int i = 0; i < 4; i++)
            bus.Subscribe(new Counter());
        bus.Publish(new Ping(1));
        Assert.Equal(0, late.Sum);
        bus.Publish(new Ping(1));
        Assert.Equal(1, late.Sum);
    }

    private sealed class SubscribesOnFirst : IMessageHandler<Ping>
    {
        private readonly MessageBus _bus;
        private IMessageHandler<Ping>? _pending;

        public SubscribesOnFirst(MessageBus bus, IMessageHandler<Ping> pending)
        {
            _bus = bus;
            _pending = pending;
        }

        public void Handle(in Ping message)
        {
            if (_pending == null)
                return;
            _bus.Subscribe(_pending);
            _pending = null;
        }
    }
}
