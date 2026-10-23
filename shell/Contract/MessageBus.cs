using System;
using System.Collections.Generic;

namespace Neo.Shell;

/// <summary>
/// The shell's message bus. Publishing does not allocate. Each handler can belong to an
/// owner (a module): if it throws, that owner is muted (its handlers are skipped from then
/// on) and <see cref="HandlerFaulted"/> is raised; the publisher and other handlers go on.
/// </summary>
public sealed class MessageBus : IMessageBus
{
    /// <summary>Owner of handlers added straight on the bus; their exceptions reach the publisher.</summary>
    public const int NoOwner = -1;

    private readonly Dictionary<Type, object> _channels = new();
    private readonly HashSet<int> _muted = new();

    /// <summary>Raised when a handler of an owner throws (owner, exception).</summary>
    public event Action<int, Exception>? HandlerFaulted;

    /// <summary>A view of this bus whose subscriptions belong to <paramref name="owner"/>.</summary>
    public IMessageBus For(int owner) => new OwnerView(this, owner);

    /// <summary>Skips every handler of <paramref name="owner"/> from now on.</summary>
    public void Mute(int owner) => _muted.Add(owner);

    public bool IsMuted(int owner) => _muted.Contains(owner);

    public void Subscribe<T>(IMessageHandler<T> handler) where T : struct => Subscribe(handler, NoOwner);

    public void Subscribe<T>(IMessageHandler<T> handler, int owner) where T : struct
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (!_channels.TryGetValue(typeof(T), out var channel))
        {
            channel = new Channel<T>();
            _channels.Add(typeof(T), channel);
        }
        ((Channel<T>)channel).Add(handler, owner);
    }

    public void Publish<T>(in T message) where T : struct
    {
        if (!_channels.TryGetValue(typeof(T), out var found))
            return;
        var channel = (Channel<T>)found;
        // Locals: a handler that subscribes while we deliver does not disturb this loop.
        var handlers = channel.Handlers;
        var owners = channel.Owners;
        int count = channel.Count;
        for (int i = 0; i < count; i++)
        {
            int owner = owners[i];
            if (owner == NoOwner)
            {
                handlers[i].Handle(in message);
                continue;
            }
            if (_muted.Contains(owner))
                continue;
            try
            {
                handlers[i].Handle(in message);
            }
            catch (Exception e)
            {
                _muted.Add(owner);
                HandlerFaulted?.Invoke(owner, e);
            }
        }
    }

    private sealed class Channel<T> where T : struct
    {
        public IMessageHandler<T>[] Handlers = new IMessageHandler<T>[4];
        public int[] Owners = new int[4];
        public int Count;

        public void Add(IMessageHandler<T> handler, int owner)
        {
            if (Count == Handlers.Length)
            {
                var handlers = new IMessageHandler<T>[Count * 2];
                var owners = new int[Count * 2];
                Array.Copy(Handlers, handlers, Count);
                Array.Copy(Owners, owners, Count);
                Handlers = handlers;
                Owners = owners;
            }
            Handlers[Count] = handler;
            Owners[Count] = owner;
            Count++;
        }
    }

    private sealed class OwnerView : IMessageBus
    {
        private readonly MessageBus _bus;
        private readonly int _owner;

        public OwnerView(MessageBus bus, int owner)
        {
            _bus = bus;
            _owner = owner;
        }

        public void Subscribe<T>(IMessageHandler<T> handler) where T : struct => _bus.Subscribe(handler, _owner);

        public void Publish<T>(in T message) where T : struct => _bus.Publish(in message);
    }
}
