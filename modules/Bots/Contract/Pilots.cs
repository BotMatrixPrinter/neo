namespace Neo.Bots;

/// <summary>
/// Who flies which plane: plane 0 is the player's, every other plane (1..7: the Allies
/// wingmen 1-3 and Central 4-7) is flown by a bot of the Bots module.
/// </summary>
public static class Pilots
{
    /// <summary>The player's plane.</summary>
    public const int PlayerId = 0;

    /// <summary>Planes in the dogfight (same as Neo.Flight.Teams.PlaneCount).</summary>
    public const int PlaneCount = 8;

    /// <summary>True for the planes a bot flies: 1..7.</summary>
    public static bool IsBot(int planeId) => planeId > PlayerId && planeId < PlaneCount;
}
