using System.Collections.Generic;
using Neo.Shell;

namespace Neo.HUD.Tests;

/// <summary>Fake of the shell bus: delivers what a test sends, as another module's publish would.</summary>
internal sealed class FakeBus : IMessageBus
{
    public readonly List<object> Published = new();
    private readonly List<object> _handlers = new();

    public void Subscribe<T>(IMessageHandler<T> handler) where T : struct => _handlers.Add(handler);

    public void Publish<T>(in T message) where T : struct => Published.Add(message);

    public void Send<T>(T message) where T : struct
    {
        foreach (var handler in _handlers)
            if (handler is IMessageHandler<T> typed)
                typed.Handle(in message);
    }
}
