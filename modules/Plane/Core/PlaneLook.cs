using System;
using Neo.Flight;
using Neo.World;

namespace Neo.Plane;

/// <summary>The two plane models.</summary>
public enum PlaneModel
{
    /// <summary>Models/biplane.glb: the Allies (the player and the wingmen).</summary>
    Biplane,

    /// <summary>Models/triplane.glb: the red triplanes of Central.</summary>
    Triplane,
}

/// <summary>
/// How a plane looks, as plain functions (the view applies them): which model a plane id
/// gets, how its damage shows (from <c>Neo.Damage.PlaneDamageState</c> part health, 0..1)
/// and when a downed plane is on the ground.
/// </summary>
public static class PlaneLook
{
    /// <summary>A part below this health shows its damage (smoke, tattered canvas).</summary>
    public const float DamageThreshold = 0.5f;

    /// <summary>Engine smoke just below <see cref="DamageThreshold"/> (it then grows to 1 at health 0).</summary>
    public const float EngineSmokeMin = 0.3f;

    /// <summary>The most smoke a damaged wing trails (a little, next to the engine's 1).</summary>
    public const float WingSmokeMax = 0.35f;

    /// <summary>Holes a tattered wing shows: from 2 just below the threshold to this at health 0.</summary>
    public const int MaxHoles = 6;

    /// <summary>A downed plane this close above the ground (meters) has hit it.</summary>
    public const float GroundContact = 1f;

    /// <summary>A downed plane this slow (m/s) has stopped on the ground (a falling one always moves).</summary>
    public const float StoppedSpeed = 0.5f;

    /// <summary>The model for <paramref name="planeId"/>: the biplane for the Allies, the triplane for Central (see <see cref="Teams"/>).</summary>
    public static PlaneModel ModelFor(int planeId) => Teams.IsAllies(planeId) ? PlaneModel.Biplane : PlaneModel.Triplane;

    /// <summary>The .glb file name of <paramref name="model"/> in Models/.</summary>
    public static string FileName(PlaneModel model) => model == PlaneModel.Triplane ? "triplane.glb" : "biplane.glb";

    /// <summary>How bad a part is below the threshold: 0 at or above it (and for NaN), 1 at health 0.</summary>
    public static float Severity(float health)
    {
        if (!(health < DamageThreshold))
            return 0f;
        return Math.Clamp((DamageThreshold - health) / DamageThreshold, 0f, 1f);
    }

    /// <summary>True when a part shows its damage (health below <see cref="DamageThreshold"/>).</summary>
    public static bool IsDamaged(float health) => health < DamageThreshold;

    /// <summary>
    /// Dark engine smoke, 0..1: none at or above the threshold, from <see cref="EngineSmokeMin"/>
    /// just below it, thicker as the engine gets worse, 1 at health 0.
    /// </summary>
    public static float EngineSmoke(float engine) =>
        IsDamaged(engine) ? EngineSmokeMin + (1f - EngineSmokeMin) * Severity(engine) : 0f;

    /// <summary>A damaged wing's smoke, 0..<see cref="WingSmokeMax"/>: none at or above the threshold.</summary>
    public static float WingSmoke(float wing) =>
        IsDamaged(wing) ? WingSmokeMax * (0.5f + 0.5f * Severity(wing)) : 0f;

    /// <summary>Holes and torn canvas a wing shows: 0 at or above the threshold, 2 just below, up to <see cref="MaxHoles"/>.</summary>
    public static int Holes(float wing) =>
        IsDamaged(wing) ? 2 + (int)MathF.Round((MaxHoles - 2) * Severity(wing)) : 0;

    /// <summary>
    /// The life of a plane: <see cref="PlaneLife.Flying"/> while alive; downed, it is
    /// <see cref="PlaneLife.Falling"/> until it is within <see cref="GroundContact"/> of the
    /// ground (World's <c>GroundY</c>) or has stopped, then <see cref="PlaneLife.Crashed"/>.
    /// </summary>
    public static PlaneLife Life(bool alive, float height, float speed)
    {
        if (alive)
            return PlaneLife.Flying;
        return height <= WorldBounds.GroundY + GroundContact || speed <= StoppedSpeed ? PlaneLife.Crashed : PlaneLife.Falling;
    }
}
