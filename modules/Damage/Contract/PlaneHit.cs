namespace Neo.Damage;

/// <summary>
/// A bullet hit a plane. Published by the guns; the Damage module subscribes. Ignored for a
/// downed plane, a plane id outside 0..<see cref="DamageLimits.MaxPlanes"/>-1, an unknown part
/// or an <see cref="Amount"/> that is not positive.
/// </summary>
public readonly struct PlaneHit
{
    public PlaneHit(int targetPlaneId, int shooterPlaneId, PlanePart part, float amount)
    {
        TargetPlaneId = targetPlaneId;
        ShooterPlaneId = shooterPlaneId;
        Part = part;
        Amount = amount;
    }

    /// <summary>The plane that was hit.</summary>
    public int TargetPlaneId { get; }

    /// <summary>The plane whose gun fired the bullet.</summary>
    public int ShooterPlaneId { get; }

    /// <summary>The part hit.</summary>
    public PlanePart Part { get; }

    /// <summary>Health taken from that part (a part has 1; a bullet is about 0.04).</summary>
    public float Amount { get; }
}
