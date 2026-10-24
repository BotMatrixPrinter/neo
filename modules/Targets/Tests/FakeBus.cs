using System.Collections.Generic;
using Neo.Shell;

namespace Neo.Targets.Tests;

/// <summary>Fake of the shell bus: records what is published and delivers what a test sends.</summary>
internal sealed class FakeBus : IMessageBus
{
    public readonly List<object> Published = new();
    private readonly List<object> _handlers = new();

    public void Subscribe<T>(IMessageHandler<T> handler) where T : struct => _handlers.Add(handler);

    public void Publish<T>(in T message) where T : struct
    {
        Published.Add(message);
        Send(message);
    }

    /// <summary>Delivers <paramref name="message"/> to every subscriber of its type, as another module's publish would.</summary>
    public void Send<T>(T message) where T : struct
    {
        foreach (var handler in _handlers.ToArray())
            if (handler is IMessageHandler<T> typed)
                typed.Handle(in message);
    }

    public List<T> All<T>() where T : struct
    {
        var found = new List<T>();
        foreach (var message in Published)
            if (message is T typed)
                found.Add(typed);
        return found;
    }

    public void Clear() => Published.Clear();
}
