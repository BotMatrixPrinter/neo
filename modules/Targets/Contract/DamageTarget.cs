namespace Neo.Targets;

/// <summary>
/// Published by other modules (e.g. the guns) to hurt a target. The Targets module
/// subscribes to it; damage to a popped target, an unknown id or a non-positive amount
/// is ignored.
/// </summary>
public readonly struct DamageTarget
{
    public DamageTarget(int targetId, int amount)
    {
        TargetId = targetId;
        Amount = amount;
    }

    /// <summary>The <see cref="TargetState.TargetId"/> of the target hit.</summary>
    public int TargetId { get; }

    /// <summary>Health to remove.</summary>
    public int Amount { get; }
}
