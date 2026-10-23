using System;
using System.Numerics;
using Neo.Shell;

namespace Neo.Targets;

/// <summary>
/// The Targets module: five tethered observation balloons that sway slightly, take
/// <see cref="DamageTarget"/>, pop at 0 health (one <see cref="TargetDestroyed"/>) and come
/// back with full health <see cref="RespawnSeconds"/> later at the same spot. Publishes a
/// <see cref="TargetState"/> for every balloon each tick. Allocation-free per tick.
/// </summary>
public sealed class TargetsModule : IModule
{
    /// <summary>Number of balloons; their ids are 0 to Count - 1.</summary>
    public const int Count = 5;

    public const int MaxHealth = 15;

    /// <summary>Hit sphere radius, in meters.</summary>
    public const float HitRadius = 9f;

    public const double RespawnSeconds = 10;

    // Tether points spread around the 4 km x 4 km area centered on the origin, 300 to 500 m up.
    private static readonly Vector3[] Anchors =
    {
        new(150f, 340f, -700f),
        new(-1450f, 420f, -1300f),
        new(1400f, 470f, -1450f),
        new(-1300f, 380f, 1250f),
        new(1450f, 450f, 1100f),
    };

    private readonly Vector3[] _positions = new Vector3[Count];
    private readonly int[] _health = new int[Count];
    private readonly double[] _respawnAt = new double[Count];
    private readonly DamageHandler _damage;
    private IMessageBus? _bus;
    private double _time;

    public TargetsModule()
    {
        _damage = new DamageHandler(this);
        for (int i = 0; i < Count; i++)
            _health[i] = MaxHealth;
        UpdatePositions();
    }

    /// <summary>Balloons popped since start, for the HUD.</summary>
    public int Pops { get; private set; }

    /// <summary>Current state of balloon <paramref name="targetId"/> (0 to Count - 1).</summary>
    public TargetState GetState(int targetId) =>
        new(targetId, _positions[targetId], HitRadius, _health[targetId], _health[targetId] > 0);

    public void Start(IMessageBus bus)
    {
        _bus = bus;
        bus.Subscribe(_damage);
    }

    public void Tick(in TickContext tick)
    {
        _time += tick.StepSeconds;
        for (int i = 0; i < Count; i++)
            if (_health[i] == 0 && _time >= _respawnAt[i] - 1e-6)
                _health[i] = MaxHealth;
        UpdatePositions();
        if (_bus == null)
            return;
        for (int i = 0; i < Count; i++)
            _bus.Publish(GetState(i));
    }

    private void Damage(in DamageTarget hit)
    {
        int i = hit.TargetId;
        if ((uint)i >= Count || hit.Amount <= 0 || _health[i] == 0)
            return;
        _health[i] = Math.Max(0, _health[i] - hit.Amount);
        if (_health[i] > 0)
            return;
        _respawnAt[i] = _time + RespawnSeconds;
        Pops++;
        _bus?.Publish(new TargetDestroyed(i, _positions[i]));
    }

    // A slow, small drift around each tether point (under 2 m), each balloon out of phase.
    private void UpdatePositions()
    {
        float t = (float)_time;
        for (int i = 0; i < Count; i++)
        {
            float phase = i * 1.37f;
            var sway = new Vector3(
                1.2f * MathF.Sin(0.35f * t + phase),
                0.5f * MathF.Sin(0.5f * t + 2.1f * phase),
                0.9f * MathF.Sin(0.27f * t + 1.6f * phase));
            _positions[i] = Anchors[i] + sway;
        }
    }

    private sealed class DamageHandler : IMessageHandler<DamageTarget>
    {
        private readonly TargetsModule _module;

        public DamageHandler(TargetsModule module) => _module = module;

        public void Handle(in DamageTarget message) => _module.Damage(in message);
    }
}
