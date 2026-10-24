using System.Numerics;

namespace Neo.Targets;

/// <summary>Published by the Targets module once when a target's health reaches 0 (it pops).</summary>
public readonly struct TargetDestroyed
{
    public TargetDestroyed(int targetId, Vector3 position)
        : this(targetId, position, -1)
    {
    }

    public TargetDestroyed(int targetId, Vector3 position, int shooterPlaneId)
    {
        TargetId = targetId;
        Position = position;
        ShooterPlaneId = shooterPlaneId;
    }

    /// <summary>The target that popped.</summary>
    public int TargetId { get; }

    /// <summary>Where it was when it popped, in meters.</summary>
    public Vector3 Position { get; }

    /// <summary>
    /// <see cref="DamageTarget.ShooterPlaneId"/> of the hit that took its last health (the
    /// plane that popped it); -1 when unknown.
    /// </summary>
    public int ShooterPlaneId { get; }
}
