using System.Collections.Generic;
using Neo.Shell;

namespace Neo.Input.Tests;

/// <summary>Fake of the shell bus: records what is published.</summary>
internal sealed class FakeBus : IMessageBus
{
    public readonly List<object> Published = new();

    public void Subscribe<T>(IMessageHandler<T> handler) where T : struct { }

    public void Publish<T>(in T message) where T : struct => Published.Add(message);
}
