using System.Numerics;

namespace Neo.Collisions;

/// <summary>
/// How a collision knocks one plane: published by the Collisions module once per plane per
/// collision (two per collision, equal and opposite). Meant for Flight to apply on its next step.
/// </summary>
public readonly struct CollisionImpulse
{
    public CollisionImpulse(int planeId, Vector3 velocityChange, Vector3 angularKick, Vector3 separation)
    {
        PlaneId = planeId;
        VelocityChange = velocityChange;
        AngularKick = angularKick;
        Separation = separation;
    }

    /// <summary>The plane knocked.</summary>
    public int PlaneId { get; }

    /// <summary>World velocity to add, m/s: along the contact normal, away from the other plane.</summary>
    public Vector3 VelocityChange { get; }

    /// <summary>
    /// Body rates to add, radians per second, in the plane's own frame: x pitch, y yaw, z roll
    /// (right-handed about the body axes +X right, +Y up, +Z back).
    /// </summary>
    public Vector3 AngularKick { get; }

    /// <summary>World position offset, meters, that moves the plane just clear of the other.</summary>
    public Vector3 Separation { get; }
}
