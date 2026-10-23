using System.Numerics;

namespace Neo.Targets;

/// <summary>Published by the Targets module once when a target's health reaches 0 (it pops).</summary>
public readonly struct TargetDestroyed
{
    public TargetDestroyed(int targetId, Vector3 position)
    {
        TargetId = targetId;
        Position = position;
    }

    /// <summary>The target that popped.</summary>
    public int TargetId { get; }

    /// <summary>Where it was when it popped, in meters.</summary>
    public Vector3 Position { get; }
}
