using Neo.Flight;
using Neo.Shell;

namespace Neo.Damage;

/// <summary>
/// The Damage module: keeps the health (0..1) of each plane's engine, wings and tail.
/// A <see cref="PlaneHit"/> lowers its part; when any part reaches 0 the plane goes down
/// (one <see cref="PlaneDestroyed"/>, hits ignored) and <see cref="DamageLimits.RespawnSeconds"/>
/// later comes back at full health (one <see cref="PlaneRespawn"/>). Publishes a
/// <see cref="PlaneDamageState"/> for every known plane each tick. Planes become known from
/// their first <see cref="PlaneHit"/> or <see cref="PlaneState"/>. Allocation-free per tick.
/// </summary>
public sealed class DamageModule : IModule
{
    private const int MaxPlanes = DamageLimits.MaxPlanes;
    private const int Parts = 4;

    // Health per plane and part: [plane * Parts + (int)part].
    private readonly float[] _health = new float[MaxPlanes * Parts];
    private readonly bool[] _known = new bool[MaxPlanes];
    private readonly bool[] _alive = new bool[MaxPlanes];
    private readonly double[] _respawnAt = new double[MaxPlanes];
    private readonly int[] _kills = new int[MaxPlanes];
    private readonly HitHandler _hits;
    private readonly PlaneStateHandler _planes;
    private IMessageBus? _bus;
    private double _time;

    public DamageModule()
    {
        _hits = new HitHandler(this);
        _planes = new PlaneStateHandler(this);
    }

    /// <summary>Planes downed by <paramref name="shooterPlaneId"/> since start, for the HUD (0 for an unknown id).</summary>
    public int Kills(int shooterPlaneId) => (uint)shooterPlaneId < MaxPlanes ? _kills[shooterPlaneId] : 0;

    /// <summary>Whether <paramref name="planeId"/> is known (seen in a hit or a flight state).</summary>
    public bool IsKnown(int planeId) => (uint)planeId < MaxPlanes && _known[planeId];

    /// <summary>Current state of <paramref name="planeId"/>; full health and alive if not known yet.</summary>
    public PlaneDamageState GetState(int planeId)
    {
        if (!IsKnown(planeId))
            return new PlaneDamageState(planeId, 1f, 1f, 1f, 1f, true);
        int b = planeId * Parts;
        return new PlaneDamageState(planeId, _health[b], _health[b + 1], _health[b + 2], _health[b + 3], _alive[planeId]);
    }

    public void Start(IMessageBus bus)
    {
        _bus = bus;
        bus.Subscribe(_hits);
        bus.Subscribe(_planes);
    }

    public void Tick(in TickContext tick)
    {
        _time += tick.StepSeconds;
        for (int i = 0; i < MaxPlanes; i++)
        {
            if (!_known[i] || _alive[i] || _time < _respawnAt[i] - 1e-6)
                continue;
            Reset(i);
            _bus?.Publish(new PlaneRespawn(i));
        }
        if (_bus == null)
            return;
        for (int i = 0; i < MaxPlanes; i++)
            if (_known[i])
                _bus.Publish(GetState(i));
    }

    private void Learn(int planeId)
    {
        if ((uint)planeId >= MaxPlanes || _known[planeId])
            return;
        _known[planeId] = true;
        Reset(planeId);
    }

    private void Reset(int planeId)
    {
        _alive[planeId] = true;
        int b = planeId * Parts;
        for (int p = 0; p < Parts; p++)
            _health[b + p] = 1f;
    }

    private void Hit(in PlaneHit hit)
    {
        int i = hit.TargetPlaneId;
        if ((uint)i >= MaxPlanes || (uint)hit.Part >= Parts)
            return;
        Learn(i);
        if (!_alive[i] || !(hit.Amount > 0))
            return;
        int slot = i * Parts + (int)hit.Part;
        float left = _health[slot] - hit.Amount;
        // A small epsilon so 25 hits of 0.04 really empty a part despite float rounding.
        _health[slot] = left > 1e-5f ? left : 0f;
        if (_health[slot] > 0f)
            return;
        _alive[i] = false;
        _respawnAt[i] = _time + DamageLimits.RespawnSeconds;
        if ((uint)hit.ShooterPlaneId < MaxPlanes && hit.ShooterPlaneId != i)
            _kills[hit.ShooterPlaneId]++;
        _bus?.Publish(new PlaneDestroyed(i, hit.ShooterPlaneId));
    }

    private sealed class HitHandler : IMessageHandler<PlaneHit>
    {
        private readonly DamageModule _owner;

        public HitHandler(DamageModule owner) => _owner = owner;

        public void Handle(in PlaneHit message) => _owner.Hit(in message);
    }

    private sealed class PlaneStateHandler : IMessageHandler<PlaneState>
    {
        private readonly DamageModule _owner;

        public PlaneStateHandler(DamageModule owner) => _owner = owner;

        public void Handle(in PlaneState message) => _owner.Learn(message.PlaneId);
    }
}
