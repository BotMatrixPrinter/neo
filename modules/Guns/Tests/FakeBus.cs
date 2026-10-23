using System.Collections.Generic;
using Neo.Shell;

namespace Neo.Guns.Tests;

/// <summary>Fake of the shell bus: records what is published and delivers what a test sends.</summary>
internal sealed class FakeBus : IMessageBus
{
    public readonly List<object> Published = new();
    private readonly List<object> _handlers = new();

    public void Subscribe<T>(IMessageHandler<T> handler) where T : struct => _handlers.Add(handler);

    public void Publish<T>(in T message) where T : struct => Published.Add(message);

    /// <summary>Delivers <paramref name="message"/> to every subscriber of its type, as another module's publish would.</summary>
    public void Send<T>(T message) where T : struct
    {
        foreach (var handler in _handlers)
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

/// <summary>A bus that keeps the handlers and only counts what is published (no allocations).</summary>
internal sealed class QuietBus : IMessageBus
{
    public IMessageHandler<Neo.Input.ControlInput>? Input;
    public IMessageHandler<Neo.Flight.PlaneState>? Plane;
    public IMessageHandler<Neo.Targets.TargetState>? Target;
    public int Damages;
    public int States;

    public void Subscribe<T>(IMessageHandler<T> handler) where T : struct
    {
        if (handler is IMessageHandler<Neo.Input.ControlInput> input)
            Input = input;
        if (handler is IMessageHandler<Neo.Flight.PlaneState> plane)
            Plane = plane;
        if (handler is IMessageHandler<Neo.Targets.TargetState> target)
            Target = target;
    }

    public void Publish<T>(in T message) where T : struct
    {
        if (typeof(T) == typeof(Neo.Targets.DamageTarget))
            Damages++;
        else if (typeof(T) == typeof(GunState))
            States++;
    }
}
