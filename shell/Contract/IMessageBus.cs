namespace Neo.Shell;

/// <summary>Receives messages of type <typeparamref name="T"/> from the bus.</summary>
public interface IMessageHandler<T> where T : struct
{
    void Handle(in T message);
}

/// <summary>
/// How modules talk: struct messages, delivered synchronously to every subscriber.
/// Message types live in the publishing module's Contract project.
/// </summary>
public interface IMessageBus
{
    /// <summary>Adds a handler; do it in <see cref="IModule.Start"/>, not per frame.</summary>
    void Subscribe<T>(IMessageHandler<T> handler) where T : struct;

    /// <summary>Delivers <paramref name="message"/> to every handler now. Does not allocate.</summary>
    void Publish<T>(in T message) where T : struct;
}
