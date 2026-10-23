namespace Neo.Damage;

/// <summary>
/// The health of one plane's parts, each 0..1 (1 is undamaged). Published by the Damage
/// module every fixed step for every plane it knows, in plane id order.
/// </summary>
public readonly struct PlaneDamageState
{
    public PlaneDamageState(int planeId, float engine, float leftWing, float rightWing, float tail, bool alive)
    {
        PlaneId = planeId;
        Engine = engine;
        LeftWing = leftWing;
        RightWing = rightWing;
        Tail = tail;
        Alive = alive;
    }

    public int PlaneId { get; }

    public float Engine { get; }

    public float LeftWing { get; }

    public float RightWing { get; }

    public float Tail { get; }

    /// <summary>False from the moment a part reaches 0 until the plane respawns.</summary>
    public bool Alive { get; }

    /// <summary>The health of <paramref name="part"/>, 0..1.</summary>
    public float Get(PlanePart part) => part switch
    {
        PlanePart.Engine => Engine,
        PlanePart.LeftWing => LeftWing,
        PlanePart.RightWing => RightWing,
        PlanePart.Tail => Tail,
        _ => 0f,
    };
}
