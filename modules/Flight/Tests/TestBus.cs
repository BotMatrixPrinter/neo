using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Neo.Shell;

namespace Neo.Flight.Tests;

/// <summary>
/// Fake of the shell bus: delivers any published message (fake <c>ControlInput</c>, Damage
/// messages) to its subscribers, keeps the last <see cref="PlaneState"/> of each plane, in
/// publishing order, and records every <see cref="PlaneCrashed"/> (also per plane). Like the shell's bus, a message reaches each handler once per subscription
/// to its type. Does not allocate when publishing.
/// </summary>
internal sealed class TestBus : IMessageBus
{
    private readonly List<object> _handlers = new();
    private readonly List<Type> _types = new();

    /// <summary>The latest state of each plane id 0..7.</summary>
    public readonly PlaneState[] States = new PlaneState[Teams.PlaneCount];
    /// <summary>Plane ids of the states published, in order, since the last <see cref="ClearOrder"/> (first 64).</summary>
    public readonly int[] Order = new int[64];
    public int StatesPublished;
    /// <summary>The <see cref="PlaneCrashed"/> messages published, in order (first 16), and how many in all.</summary>
    public readonly PlaneCrashed[] Crashes = new PlaneCrashed[16];
    public int CrashCount;
    /// <summary>How many <see cref="PlaneCrashed"/> each plane id 0..7 published, and its latest one.</summary>
    public readonly int[] CrashCountOf = new int[Teams.PlaneCount];
    public readonly PlaneCrashed[] LastCrashOf = new PlaneCrashed[Teams.PlaneCount];
    private int _orderCount;

    public void ClearOrder() => _orderCount = 0;

    public void Subscribe<T>(IMessageHandler<T> handler) where T : struct
    {
        _handlers.Add(handler);
        _types.Add(typeof(T));
    }

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
        else if (typeof(T) == typeof(PlaneCrashed))
        {
            PlaneCrashed crash = Unsafe.As<T, PlaneCrashed>(ref Unsafe.AsRef(in message));
            if (CrashCount < Crashes.Length)
                Crashes[CrashCount] = crash;
            CrashCount++;
            CrashCountOf[crash.PlaneId]++;
            LastCrashOf[crash.PlaneId] = crash;
        }
        for (int i = 0; i < _handlers.Count; i++)
        {
            if (_types[i] == typeof(T) && _handlers[i] is IMessageHandler<T> handler)
                handler.Handle(in message);
        }
    }
}
