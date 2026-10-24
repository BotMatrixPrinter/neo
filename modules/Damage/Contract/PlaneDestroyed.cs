namespace Neo.Damage;

/// <summary>A plane went down (one of its parts reached 0, or it crashed into the ground). Published once per downing.</summary>
public readonly struct PlaneDestroyed
{
    public PlaneDestroyed(int planeId, int shooterPlaneId)
    {
        PlaneId = planeId;
        ShooterPlaneId = shooterPlaneId;
    }

    /// <summary>The plane that went down.</summary>
    public int PlaneId { get; }

    /// <summary>
    /// The plane whose hit brought it down; for a crash, the last plane that hit it within
    /// <see cref="DamageLimits.CrashCreditSeconds"/>, or -1 if it crashed on its own.
    /// </summary>
    public int ShooterPlaneId { get; }
}
