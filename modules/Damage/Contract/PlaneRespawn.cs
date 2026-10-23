namespace Neo.Damage;

/// <summary>
/// A downed plane is back, all parts at full health. Published once,
/// <see cref="DamageLimits.RespawnSeconds"/> after its <see cref="PlaneDestroyed"/>.
/// </summary>
public readonly struct PlaneRespawn
{
    public PlaneRespawn(int planeId) => PlaneId = planeId;

    public int PlaneId { get; }
}
