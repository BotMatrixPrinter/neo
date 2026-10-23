namespace Neo.World;

/// <summary>
/// Size of the place you fly over: a square of ground centered on the origin at y = 0
/// (meters, Y up). Hills rise from it but stay below <see cref="MaxHillHeight"/>.
/// </summary>
public readonly struct WorldBounds
{
    /// <summary>Half the ground's side: it spans -2000..2000 m on X and Z.</summary>
    public const float HalfSize = 2000f;

    /// <summary>Ground level away from hills and the river, in meters.</summary>
    public const float GroundY = 0f;

    /// <summary>Every hill top is lower than this, in meters.</summary>
    public const float MaxHillHeight = 15f;
}
