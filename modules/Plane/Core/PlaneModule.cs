using Neo.Flight;
using Neo.Shell;

namespace Neo.Plane;

/// <summary>
/// The Plane module: keeps the latest <see cref="PlaneState"/> of the player's plane
/// (<see cref="PlayerId"/>) and the one before it, for the view to draw and interpolate
/// between. States of other planes are ignored. Allocation-free.
/// </summary>
public sealed class PlaneModule : IModule
{
    /// <summary>The plane this module draws.</summary>
    public const int PlayerId = 0;

    private readonly StateHandler _states;

    public PlaneModule() => _states = new StateHandler(this);

    /// <summary>True once a state of the player's plane has arrived.</summary>
    public bool HasState { get; private set; }

    /// <summary>The latest state of the player's plane (default until <see cref="HasState"/>).</summary>
    public PlaneState Latest { get; private set; }

    /// <summary>The state before <see cref="Latest"/> (equal to it after the first one).</summary>
    public PlaneState Previous { get; private set; }

    /// <summary>States of the player's plane received so far.</summary>
    public long Updates { get; private set; }

    public void Start(IMessageBus bus) => bus.Subscribe(_states);

    public void Tick(in TickContext tick)
    {
    }

    private void Receive(in PlaneState state)
    {
        if (state.PlaneId != PlayerId)
            return;
        Previous = HasState ? Latest : state;
        Latest = state;
        HasState = true;
        Updates++;
    }

    private sealed class StateHandler : IMessageHandler<PlaneState>
    {
        private readonly PlaneModule _module;

        public StateHandler(PlaneModule module) => _module = module;

        public void Handle(in PlaneState message) => _module.Receive(in message);
    }
}
