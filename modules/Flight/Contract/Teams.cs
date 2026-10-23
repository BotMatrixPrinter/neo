namespace Neo.Flight;

/// <summary>
/// Plane ids and teams, so every module agrees: ids 0..3 are team Allies (0 is the player),
/// ids 4..7 team Central.
/// </summary>
public static class Teams
{
    /// <summary>Planes in the dogfight: ids 0..PlaneCount-1.</summary>
    public const int PlaneCount = 8;

    /// <summary>Planes per team.</summary>
    public const int TeamSize = 4;

    /// <summary>True for the Allies (ids 0..3); false for Central (4..7) and any other id.</summary>
    public static bool IsAllies(int planeId) => planeId >= 0 && planeId < TeamSize;
}
