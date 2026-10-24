namespace Neo.World;

/// <summary>
/// Size of the place you fly over: a square of ground centered on the origin at y = 0
/// (meters, Y up). Hills and mountains rise from it (see <see cref="Terrain"/>) but never above
/// <see cref="MaxHillHeight"/>.
/// </summary>
public readonly struct WorldBounds
{
    /// <summary>Half the ground's side: it spans -2000..2000 m on X and Z.</summary>
    public const float HalfSize = 2000f;

    /// <summary>Lowest ground, in meters: the water surface of the river and the lakes
    /// (<see cref="Terrain.WaterLevel"/>). The plain lies a little above it (<see cref="Terrain.PlainHeight"/>).</summary>
    public const float GroundY = 0f;

    /// <summary>The highest point of the terrain, in meters: <see cref="Terrain.MaxHeight"/>.</summary>
    public const float MaxHillHeight = Terrain.MaxHeight;
}
