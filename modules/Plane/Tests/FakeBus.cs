using System.Collections.Generic;
using Neo.Shell;

namespace Neo.Plane.Tests;

/// <summary>Fake of the shell bus: keeps subscribers and delivers what a test sends, as another module's publish would.</summary>
internal sealed class FakeBus : IMessageBus
{
    public readonly List<object> Handlers = new();
    public int Published;

    public void Subscribe<T>(IMessageHandler<T> handler) where T : struct => Handlers.Add(handler);

    public void Publish<T>(in T message) where T : struct => Published++;

    public void Send<T>(in T message) where T : struct
    {
        for (int i = 0; i < Handlers.Count; i++)
            if (Handlers[i] is IMessageHandler<T> typed)
                typed.Handle(in message);
    }
}
