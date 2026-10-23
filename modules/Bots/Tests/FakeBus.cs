using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Neo.Input;
using Neo.Shell;

namespace Neo.Bots.Tests;

/// <summary>Fake of the shell bus: records the published <see cref="ControlInput"/>s (no allocation while publishing).</summary>
internal sealed class FakeBus : IMessageBus
{
    public readonly ControlInput[] Inputs = new ControlInput[64];
    public int Count;
    public readonly List<object> Handlers = new();

    public void Clear() => Count = 0;

    public void Subscribe<T>(IMessageHandler<T> handler) where T : struct => Handlers.Add(handler);

    public void Publish<T>(in T message) where T : struct
    {
        if (typeof(T) == typeof(ControlInput) && Count < Inputs.Length)
            Inputs[Count++] = Unsafe.As<T, ControlInput>(ref Unsafe.AsRef(in message));
    }

    /// <summary>The input published for <paramref name="planeId"/> since the last <see cref="Clear"/>, if any.</summary>
    public bool TryGet(int planeId, out ControlInput input)
    {
        for (int i = 0; i < Count; i++)
        {
            if (Inputs[i].PlaneId == planeId)
            {
                input = Inputs[i];
                return true;
            }
        }
        input = default;
        return false;
    }
}
