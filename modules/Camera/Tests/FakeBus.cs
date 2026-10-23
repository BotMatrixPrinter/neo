using Neo.Flight;
using Neo.Shell;

namespace Neo.Camera.Tests;

/// <summary>Fake of the shell bus: keeps the <see cref="PlaneState"/> handler and delivers what a test sends, like Flight's publish would.</summary>
internal sealed class FakeBus : IMessageBus
{
    public IMessageHandler<PlaneState>? Plane;
    public int Published;

    public void Subscribe<T>(IMessageHandler<T> handler) where T : struct
    {
        if (handler is IMessageHandler<PlaneState> plane)
            Plane = plane;
    }

    public void Publish<T>(in T message) where T : struct => Published++;

    public void Send(in PlaneState state) => Plane!.Handle(in state);
}
