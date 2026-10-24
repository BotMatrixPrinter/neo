namespace Neo.Targets;

/// <summary>
/// Published by other modules (e.g. the guns) to hurt a target. The Targets module
/// subscribes to it; damage to a popped target, an unknown id or a non-positive amount
/// is ignored.
/// </summary>
public readonly struct DamageTarget
{
    public DamageTarget(int targetId, int amount)
        : this(targetId, amount, -1)
    {
    }

    public DamageTarget(int targetId, int amount, int shooterPlaneId)
    {
        TargetId = targetId;
        Amount = amount;
        ShooterPlaneId = shooterPlaneId;
    }

    /// <summary>The <see cref="TargetState.TargetId"/> of the target hit.</summary>
    public int TargetId { get; }

    /// <summary>Health to remove.</summary>
    public int Amount { get; }

    /// <summary>PlaneId of the plane whose shot it was; -1 when unknown.</summary>
    public int ShooterPlaneId { get; }
}
