namespace Neo.Damage;

/// <summary>The Damage module's fixed numbers.</summary>
public static class DamageLimits
{
    /// <summary>Plane ids 0..MaxPlanes-1 are tracked; others are ignored.</summary>
    public const int MaxPlanes = 64;

    /// <summary>Seconds a downed plane stays down before <see cref="PlaneRespawn"/>.</summary>
    public const double RespawnSeconds = 5.0;

    /// <summary>Seconds of continuously holding Repair to fix one part (see <see cref="RepairState"/>).</summary>
    public const double RepairSeconds = 4.0;

    /// <summary>A crash (<c>Neo.Flight.PlaneCrashed</c>) is credited to the last plane that hit it within these seconds.</summary>
    public const double CrashCreditSeconds = 10.0;
}
