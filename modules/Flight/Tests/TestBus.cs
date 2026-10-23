using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Neo.Shell;

namespace Neo.Flight.Tests;

/// <summary>
/// Fake of the shell bus: delivers any published message (fake <c>ControlInput</c>, Damage
/// messages) to its subscribers and keeps the last <see cref="PlaneState"/> of each plane, in
/// publishing order. Does not allocate when publishing.
/// </summary>
internal sealed class TestBus : IMessageBus
{
    private readonly List<object> _handlers = new();

    /// <summary>The latest state of each plane id 0..7.</summary>
    public readonly PlaneState[] States = new PlaneState[Teams.PlaneCount];
    /// <summary>Plane ids of the states published, in order, since the last <see cref="ClearOrder"/> (first 64).</summary>
    public readonly int[] Order = new int[64];
    public int StatesPublished;
    private int _orderCount;

    public void ClearOrder() => _orderCount = 0;

    public void Subscribe<T>(IMessageHandler<T> handler) where T : struct => _handlers.Add(handler);

    public void Publish<T>(in T message) where T : struct
    {
        if (typeof(T) == typeof(PlaneState))
        {
            PlaneState state = Unsafe.As<T, PlaneState>(ref Unsafe.AsRef(in message));
            States[state.PlaneId] = state;
            if (_orderCount < Order.Length)
                Order[_orderCount++] = state.PlaneId;
            StatesPublished++;
        }
        for (int i = 0; i < _handlers.Count; i++)
        {
            if (_handlers[i] is IMessageHandler<T> handler)
                handler.Handle(in message);
        }
    }
}
