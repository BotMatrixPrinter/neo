using System.Numerics;

namespace Neo.Targets;

/// <summary>Published by the Targets module every fixed step, once for each target.</summary>
public readonly struct TargetState
{
    public TargetState(int targetId, Vector3 position, float radius, int health, bool alive)
    {
        TargetId = targetId;
        Position = position;
        Radius = radius;
        Health = health;
        Alive = alive;
    }

    /// <summary>Which target, 0 to count - 1.</summary>
    public int TargetId { get; }

    /// <summary>Center of the target, in meters (Y up).</summary>
    public Vector3 Position { get; }

    /// <summary>Hit sphere radius around <see cref="Position"/>, in meters.</summary>
    public float Radius { get; }

    /// <summary>Health left; 0 while popped.</summary>
    public int Health { get; }

    /// <summary>False while popped and waiting to come back; it cannot be hit then.</summary>
    public bool Alive { get; }
}
