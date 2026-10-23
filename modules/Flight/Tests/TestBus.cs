using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Neo.Input;
using Neo.Shell;

namespace Neo.Flight.Tests;

/// <summary>
/// Fake of the shell bus: delivers fake <see cref="ControlInput"/> to subscribers and keeps
/// the last <see cref="PlaneState"/> published. Does not allocate when publishing.
/// </summary>
internal sealed class TestBus : IMessageBus
{
    private readonly List<IMessageHandler<ControlInput>> _inputHandlers = new();

    public PlaneState Last;
    public int StatesPublished;

    public void Subscribe<T>(IMessageHandler<T> handler) where T : struct
    {
        if (handler is IMessageHandler<ControlInput> input)
            _inputHandlers.Add(input);
    }

    public void Publish<T>(in T message) where T : struct
    {
        if (typeof(T) == typeof(ControlInput))
        {
            ref readonly ControlInput input = ref Unsafe.As<T, ControlInput>(ref Unsafe.AsRef(in message));
            for (int i = 0; i < _inputHandlers.Count; i++)
                _inputHandlers[i].Handle(in input);
        }
        else if (typeof(T) == typeof(PlaneState))
        {
            Last = Unsafe.As<T, PlaneState>(ref Unsafe.AsRef(in message));
            StatesPublished++;
        }
    }
}
