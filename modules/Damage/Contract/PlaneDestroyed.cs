namespace Neo.Damage;

/// <summary>A plane went down (one of its parts reached 0). Published once per downing.</summary>
public readonly struct PlaneDestroyed
{
    public PlaneDestroyed(int planeId, int shooterPlaneId)
    {
        PlaneId = planeId;
        ShooterPlaneId = shooterPlaneId;
    }

    /// <summary>The plane that went down.</summary>
    public int PlaneId { get; }

    /// <summary>The plane whose hit brought it down.</summary>
    public int ShooterPlaneId { get; }
}
