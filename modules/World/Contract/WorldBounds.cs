namespace Neo.World;

/// <summary>
/// Size of the place you fly over: a playable square of ground centered on the origin at y = 0
/// (meters, Y up), closed in by an impassable mountain range climbing from its edge. Every module uses
/// <see cref="HalfSize"/> for the map's size; none keeps its own copy. Heights: see <see cref="Terrain"/>.
/// </summary>
public readonly struct WorldBounds
{
    /// <summary>Half the playable square's side: it spans -1000..1000 m on X and Z (2 km x 2 km); the
    /// mountain range (<see cref="Terrain.HeightAt"/>) climbs from just beyond it.</summary>
    public const float HalfSize = 1000f;

    /// <summary>Lowest ground, in meters: the water surface of the river and the lake
    /// (<see cref="Terrain.WaterLevel"/>). The plain lies a little above it (<see cref="Terrain.PlainHeight"/>).</summary>
    public const float GroundY = 0f;

    /// <summary>The highest point of the terrain, in meters: <see cref="Terrain.MaxHeight"/> (the range's tallest peak).
    /// Inside the playable square the ground stays below <see cref="Terrain.InnerMaxHeight"/>.</summary>
    public const float MaxHillHeight = Terrain.MaxHeight;
}
