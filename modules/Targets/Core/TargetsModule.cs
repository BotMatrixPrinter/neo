using System;
using System.Numerics;
using Neo.Shell;
using Neo.World;

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

    /// <summary>Seconds a popped balloon stays down before it comes back.</summary>
    public const double RespawnSeconds = 60;

    /// <summary>Least height (m) of a balloon above the terrain under it.</summary>
    public const float MinClearance = 150f;

    /// <summary>Least distance (m) from a balloon's tether point to the nearest water.</summary>
    public const float ShoreMargin = 30f;

    /// <summary>
    /// Least distance from a tether point to the edge of the playable area, as a fraction of
    /// <see cref="WorldBounds.HalfSize"/>: every tether point lies within
    /// <see cref="MaxReach"/> of the origin on X and Z.
    /// </summary>
    public const float EdgeMarginFraction = 0.2f;

    /// <summary>Farthest a tether point lies from the origin on X or Z (m).</summary>
    public static float MaxReach => (1f - EdgeMarginFraction) * WorldBounds.HalfSize;

    // Wanted tether spots, X and Z as fractions of WorldBounds.HalfSize, Y in meters (300 to
    // 500 m up): one over the center, two toward each side (the teams start on -Z and +Z).
    // PlaceOnDryGround moves them off water and up to clear the terrain.
    private static readonly Vector3[] WantedSpots =
    {
        new(0f, 340f, 0f),
        new(-0.55f, 420f, -0.55f),
        new(0.55f, 470f, -0.55f),
        new(-0.55f, 380f, 0.55f),
        new(0.55f, 450f, 0.55f),
    };

    private static readonly Vector3[] Spots = new Vector3[Count];
    private static readonly Vector3[] Grounds = new Vector3[Count];

    static TargetsModule()
    {
        for (int i = 0; i < Count; i++)
        {
            var w = WantedSpots[i];
            Spots[i] = PlaceOnDryGround(new Vector3(w.X * WorldBounds.HalfSize, w.Y, w.Z * WorldBounds.HalfSize));
            Grounds[i] = new Vector3(Spots[i].X, Terrain.HeightAt(Spots[i].X, Spots[i].Z), Spots[i].Z);
        }
    }

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

    /// <summary>
    /// Where balloon <paramref name="targetId"/>'s cable (and winch) is anchored: the ground
    /// (<see cref="Terrain.HeightAt"/>) under its tether point. Fixed; the balloon sways around
    /// the point straight above it.
    /// </summary>
    public static Vector3 Anchor(int targetId) => Grounds[targetId];

    /// <summary>
    /// The tether spot used for <paramref name="wanted"/>: the nearest point (searched in 10 m
    /// rings, up to 600 m out, never farther than <see cref="MaxReach"/> from the origin on X or Z)
    /// whose ground is dry for <see cref="ShoreMargin"/> m around, at
    /// the wanted height or higher, so that it stays <see cref="MinClearance"/> m (plus room for
    /// the sway) above the highest ground within 20 m.
    /// </summary>
    public static Vector3 PlaceOnDryGround(Vector3 wanted)
    {
        float reach = MaxReach;
        float x = Math.Clamp(wanted.X, -reach, reach), z = Math.Clamp(wanted.Z, -reach, reach);
        float wx = x, wz = z;
        for (int ring = 0; ring <= 60; ring++)
        {
            float r = ring * 10f;
            int directions = ring == 0 ? 1 : 32;
            bool found = false;
            for (int d = 0; d < directions && !found; d++)
            {
                float a = d * MathF.Tau / directions;
                float cx = wx + r * MathF.Cos(a), cz = wz + r * MathF.Sin(a);
                if (MathF.Abs(cx) <= reach && MathF.Abs(cz) <= reach && DryAround(cx, cz))
                {
                    x = cx;
                    z = cz;
                    found = true;
                }
            }
            if (found)
                break;
        }
        float highest = Terrain.WaterLevel;
        for (float dx = -20f; dx <= 20f; dx += 5f)
            for (float dz = -20f; dz <= 20f; dz += 5f)
                highest = MathF.Max(highest, Terrain.HeightAt(x + dx, z + dz));
        return new Vector3(x, MathF.Max(wanted.Y, highest + MinClearance + 5f), z);
    }

    private static bool DryAround(float x, float z)
    {
        for (float dx = -ShoreMargin; dx <= ShoreMargin; dx += 10f)
            for (float dz = -ShoreMargin; dz <= ShoreMargin; dz += 10f)
                if (Terrain.IsWater(x + dx, z + dz))
                    return false;
        return true;
    }

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
        _bus?.Publish(new TargetDestroyed(i, _positions[i], hit.ShooterPlaneId));
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
            _positions[i] = Spots[i] + sway;
        }
    }

    private sealed class DamageHandler : IMessageHandler<DamageTarget>
    {
        private readonly TargetsModule _module;

        public DamageHandler(TargetsModule module) => _module = module;

        public void Handle(in DamageTarget message) => _module.Damage(in message);
    }
}
