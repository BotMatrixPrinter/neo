using Neo.Damage;
using Neo.Flight;
using Neo.Shell;

namespace Neo.Plane;

/// <summary>
/// The Plane module: for every plane id seen in a <see cref="PlaneState"/> (0..<see cref="MaxPlanes"/>-1)
/// keeps the latest state and the one before it, for the view to draw and interpolate
/// between, its latest <see cref="PlaneDamageState"/> and its <see cref="PlaneLife"/>.
/// Allocation-free; the player's plane (<see cref="PlayerId"/>) is one of them.
/// </summary>
public sealed class PlaneModule : IModule
{
    /// <summary>The player's plane.</summary>
    public const int PlayerId = 0;

    /// <summary>Plane ids 0..MaxPlanes-1 are kept; others are ignored.</summary>
    public const int MaxPlanes = DamageLimits.MaxPlanes;

    private readonly Track[] _tracks = new Track[MaxPlanes];
    private readonly int[] _ids = new int[MaxPlanes];
    private readonly StateHandler _states;
    private readonly DamageHandler _damage;

    public PlaneModule()
    {
        _states = new StateHandler(this);
        _damage = new DamageHandler(this);
    }

    /// <summary>Plane ids seen so far (in a <see cref="PlaneState"/>); <see cref="IdAt"/> reads them.</summary>
    public int Count { get; private set; }

    /// <summary>The <paramref name="index"/>-th plane id seen (0..<see cref="Count"/>-1), in the order they first came.</summary>
    public int IdAt(int index) => _ids[index];

    /// <summary>True once a state of <paramref name="planeId"/> has arrived.</summary>
    public bool Has(int planeId) => InRange(planeId) && _tracks[planeId].HasState;

    /// <summary>The latest state of <paramref name="planeId"/> (default until <see cref="Has"/>).</summary>
    public PlaneState LatestOf(int planeId) => InRange(planeId) ? _tracks[planeId].Latest : default;

    /// <summary>
    /// The state before <see cref="LatestOf"/> (equal to it after the first one, and after a
    /// respawn, so the view does not slide the plane from its wreck to its start point).
    /// </summary>
    public PlaneState PreviousOf(int planeId) => InRange(planeId) ? _tracks[planeId].Previous : default;

    /// <summary>States of <paramref name="planeId"/> received so far.</summary>
    public long UpdatesOf(int planeId) => InRange(planeId) ? _tracks[planeId].Updates : 0;

    /// <summary>True once a <see cref="PlaneDamageState"/> of <paramref name="planeId"/> has arrived.</summary>
    public bool HasDamage(int planeId) => InRange(planeId) && _tracks[planeId].HasDamage;

    /// <summary>The latest damage of <paramref name="planeId"/>; undamaged and alive until one arrives.</summary>
    public PlaneDamageState DamageOf(int planeId) =>
        HasDamage(planeId) ? _tracks[planeId].Damage : new PlaneDamageState(planeId, 1f, 1f, 1f, 1f, true);

    /// <summary>
    /// Flying, falling or crashed (<see cref="PlaneLook.Life"/>): downed when either Flight
    /// (<see cref="PlaneState.Alive"/>) or Damage (<see cref="PlaneDamageState.Alive"/>) says so,
    /// so it stays down until Flight has put it back at its start point.
    /// </summary>
    public PlaneLife LifeOf(int planeId) => InRange(planeId) ? _tracks[planeId].Life : PlaneLife.Flying;

    /// <summary>Times <paramref name="planeId"/> hit the ground (became <see cref="PlaneLife.Crashed"/>): the view puffs an explosion at each.</summary>
    public long CrashesOf(int planeId) => InRange(planeId) ? _tracks[planeId].Crashes : 0;

    /// <summary>True once a state of the player's plane has arrived.</summary>
    public bool HasState => Has(PlayerId);

    /// <summary>The latest state of the player's plane.</summary>
    public PlaneState Latest => LatestOf(PlayerId);

    /// <summary>The state before <see cref="Latest"/>.</summary>
    public PlaneState Previous => PreviousOf(PlayerId);

    /// <summary>States of the player's plane received so far.</summary>
    public long Updates => UpdatesOf(PlayerId);

    public void Start(IMessageBus bus)
    {
        bus.Subscribe(_states);
        bus.Subscribe(_damage);
    }

    public void Tick(in TickContext tick)
    {
    }

    private static bool InRange(int planeId) => planeId >= 0 && planeId < MaxPlanes;

    private void Receive(in PlaneState state)
    {
        int id = state.PlaneId;
        if (!InRange(id))
            return;
        ref var track = ref _tracks[id];
        if (!track.HasState)
            _ids[Count++] = id;
        bool respawned = track.HasState && state.Alive && !track.Latest.Alive;
        track.Previous = track.HasState && !respawned ? track.Latest : state;
        track.Latest = state;
        track.HasState = true;
        track.Updates++;
        UpdateLife(ref track);
    }

    private void Receive(in PlaneDamageState damage)
    {
        if (!InRange(damage.PlaneId))
            return;
        ref var track = ref _tracks[damage.PlaneId];
        track.Damage = damage;
        track.HasDamage = true;
        if (track.HasState)
            UpdateLife(ref track);
    }

    private static void UpdateLife(ref Track track)
    {
        bool alive = track.Latest.Alive && (!track.HasDamage || track.Damage.Alive);
        var life = PlaneLook.Life(alive, track.Latest.Position.Y, track.Latest.Speed);
        if (life == PlaneLife.Crashed && track.Life != PlaneLife.Crashed)
            track.Crashes++;
        track.Life = life;
    }

    private struct Track
    {
        public bool HasState;
        public PlaneState Latest;
        public PlaneState Previous;
        public long Updates;
        public bool HasDamage;
        public PlaneDamageState Damage;
        public PlaneLife Life;
        public long Crashes;
    }

    private sealed class StateHandler : IMessageHandler<PlaneState>
    {
        private readonly PlaneModule _module;

        public StateHandler(PlaneModule module) => _module = module;

        public void Handle(in PlaneState message) => _module.Receive(in message);
    }

    private sealed class DamageHandler : IMessageHandler<PlaneDamageState>
    {
        private readonly PlaneModule _module;

        public DamageHandler(PlaneModule module) => _module = module;

        public void Handle(in PlaneDamageState message) => _module.Receive(in message);
    }
}
