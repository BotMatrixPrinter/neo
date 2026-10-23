using System.Numerics;

namespace Neo.Flight;

/// <summary>
/// Where one plane is and how it flies. Published by the Flight module every fixed step,
/// after the step. World space: meters, seconds, Y up; the plane's forward is -Z rotated
/// by <see cref="Orientation"/>.
/// </summary>
public readonly struct PlaneState
{
    /// <summary>A flying plane (<see cref="Alive"/> true).</summary>
    public PlaneState(int planeId, Vector3 position, Quaternion orientation, Vector3 velocity, float speed, float throttle)
        : this(planeId, position, orientation, velocity, speed, throttle, true)
    {
    }

    public PlaneState(int planeId, Vector3 position, Quaternion orientation, Vector3 velocity, float speed, float throttle, bool alive)
    {
        PlaneId = planeId;
        Position = position;
        Orientation = orientation;
        Velocity = velocity;
        Speed = speed;
        Throttle = throttle;
        Alive = alive;
    }

    /// <summary>The plane: 0..7, see <see cref="Teams"/> (0 is the player).</summary>
    public int PlaneId { get; }

    /// <summary>World position, meters.</summary>
    public Vector3 Position { get; }

    /// <summary>Body to world rotation (unit). Body axes: +X right, +Y up, -Z forward.</summary>
    public Quaternion Orientation { get; }

    /// <summary>World velocity, m/s (along the nose).</summary>
    public Vector3 Velocity { get; }

    /// <summary>Airspeed, m/s (the length of <see cref="Velocity"/>).</summary>
    public float Speed { get; }

    /// <summary>Throttle in use, 0..1.</summary>
    public float Throttle { get; }

    /// <summary>False while the plane is down (falling, or on the ground) until it respawns.</summary>
    public bool Alive { get; }
}
