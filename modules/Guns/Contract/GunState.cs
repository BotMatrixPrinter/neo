namespace Neo.Guns;

/// <summary>State of one plane's twin machine guns. Published by the Guns module every fixed step.</summary>
public readonly struct GunState
{
    public GunState(int planeId, float heat, bool overheated, bool firing, int shotsFired)
    {
        PlaneId = planeId;
        Heat = heat;
        Overheated = overheated;
        Firing = firing;
        ShotsFired = shotsFired;
    }

    /// <summary>The plane (0 for now: one plane).</summary>
    public int PlaneId { get; }

    /// <summary>Barrel heat, 0 (cold) to 1 (overheated).</summary>
    public float Heat { get; }

    /// <summary>True from reaching full heat until heat drops below 0.3; the guns do not fire then.</summary>
    public bool Overheated { get; }

    /// <summary>The guns are firing this step (trigger held and not overheated).</summary>
    public bool Firing { get; }

    /// <summary>Rounds fired since start, both guns together.</summary>
    public int ShotsFired { get; }
}
