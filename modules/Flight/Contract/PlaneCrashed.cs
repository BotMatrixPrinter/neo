using System.Numerics;

namespace Neo.Flight;

/// <summary>
/// A plane hit the ground (World's terrain): published once by the Flight module, in the step it
/// happens, whether it was flying (it is down from then on) or already falling after a downing.
/// The plane then rests at <see cref="Position"/> (<see cref="PlaneState.Alive"/> false) until its respawn.
/// </summary>
public readonly struct PlaneCrashed
{
    public PlaneCrashed(int planeId, Vector3 position, float speed)
    {
        PlaneId = planeId;
        Position = position;
        Speed = speed;
    }

    /// <summary>The plane: 0..7, see <see cref="Teams"/>.</summary>
    public int PlaneId { get; }

    /// <summary>Crash point on the ground (world, meters): Y is the terrain height there.</summary>
    public Vector3 Position { get; }

    /// <summary>Airspeed at impact, m/s.</summary>
    public float Speed { get; }
}
