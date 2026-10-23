namespace Neo.Damage;

/// <summary>The Damage module's fixed numbers.</summary>
public static class DamageLimits
{
    /// <summary>Plane ids 0..MaxPlanes-1 are tracked; others are ignored.</summary>
    public const int MaxPlanes = 64;

    /// <summary>Seconds a downed plane stays down before <see cref="PlaneRespawn"/>.</summary>
    public const double RespawnSeconds = 5.0;
}
