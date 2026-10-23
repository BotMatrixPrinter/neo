namespace Neo.Plane;

/// <summary>Where a plane is in its life, as the view draws it.</summary>
public enum PlaneLife
{
    /// <summary>Alive and flying: drawn, with smoke for its damage.</summary>
    Flying,

    /// <summary>Downed and still in the air: drawn on fire, trailing thick smoke.</summary>
    Falling,

    /// <summary>Downed and on the ground: hidden (an explosion puff where it hit) until it respawns.</summary>
    Crashed,
}
