namespace Neo.HUD;

/// <summary>How the damage panel colors one part of the player's plane.</summary>
public enum PartLevel
{
    /// <summary>Health 0.6 or more: green.</summary>
    Good,

    /// <summary>Health below 0.6: orange.</summary>
    Worn,

    /// <summary>Health below 0.3: red.</summary>
    Critical,

    /// <summary>Health 0: red and crossed out.</summary>
    Destroyed,
}
