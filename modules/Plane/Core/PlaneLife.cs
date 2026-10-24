namespace Neo.Plane;

/// <summary>Where a plane is in its life, as the view draws it.</summary>
public enum PlaneLife
{
    /// <summary>Alive and flying: drawn, with smoke for its damage.</summary>
    Flying,

    /// <summary>Downed and still in the air: drawn on fire, trailing thick smoke.</summary>
    Falling,

    /// <summary>Downed and on the ground: hidden (an explosion puff or a splash where it hit) until it respawns.</summary>
    Crashed,
}

/// <summary>How a crash shows where the plane hit (<see cref="PlaneLook.CrashAt"/>).</summary>
public enum CrashLook
{
    /// <summary>On land: a burst of fire, then a rising cloud of smoke.</summary>
    Explosion,

    /// <summary>On the river or a lake: a cartoon splash of white water and foam.</summary>
    Splash,
}
