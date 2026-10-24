using System.Numerics;

namespace Neo.Input;

/// <summary>
/// What the pilot of one plane wants this step. Published by the Input module once per
/// fixed step. Axes are clamped to their ranges; non-finite values count as zero.
/// </summary>
public readonly struct ControlInput
{
    public ControlInput(int planeId, float pitch, float roll, float yaw, float throttle, bool fire, bool hasAim, Vector3 aimDirection)
        : this(planeId, pitch, roll, yaw, throttle, fire, hasAim, aimDirection, repair: false)
    {
    }

    public ControlInput(int planeId, float pitch, float roll, float yaw, float throttle, bool fire, bool hasAim, Vector3 aimDirection, bool repair)
    {
        Repair = repair;
        PlaneId = planeId;
        Pitch = Clamp(pitch, -1f, 1f);
        Roll = Clamp(roll, -1f, 1f);
        Yaw = Clamp(yaw, -1f, 1f);
        Throttle = Clamp(throttle, 0f, 1f);
        Fire = fire;
        float length = aimDirection.Length();
        if (hasAim && float.IsFinite(length) && length > 1e-6f)
        {
            HasAim = true;
            AimDirection = aimDirection / length;
        }
        else
        {
            HasAim = false;
            AimDirection = Vector3.Zero;
        }
    }

    /// <summary>The plane these controls are for (0 for now: one plane).</summary>
    public int PlaneId { get; }

    /// <summary>-1..1, positive = nose up.</summary>
    public float Pitch { get; }

    /// <summary>-1..1, positive = roll right.</summary>
    public float Roll { get; }

    /// <summary>-1..1, positive = yaw right.</summary>
    public float Yaw { get; }

    /// <summary>0..1.</summary>
    public float Throttle { get; }

    /// <summary>The guns are held down.</summary>
    public bool Fire { get; }

    /// <summary>
    /// The Repair button is held down this step (gamepad Y / Triangle, keyboard R); false
    /// from the constructor without <c>repair</c>.
    /// </summary>
    public bool Repair { get; }

    /// <summary>True when <see cref="AimDirection"/> is valid (mouse aim); false with a gamepad.</summary>
    public bool HasAim { get; }

    /// <summary>World-space unit vector the pilot wants to fly toward; zero when not <see cref="HasAim"/>.</summary>
    public Vector3 AimDirection { get; }

    private static float Clamp(float value, float min, float max)
    {
        if (!float.IsFinite(value))
            return 0f;
        return value < min ? min : value > max ? max : value;
    }
}
