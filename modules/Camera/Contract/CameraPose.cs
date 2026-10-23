using System.Numerics;

namespace Neo.Camera;

/// <summary>
/// Where the chase camera is and where it looks. World space: meters, Y up; the camera looks
/// along -Z rotated by <see cref="Orientation"/> (Godot's convention), +Y is its up.
/// </summary>
public readonly struct CameraPose
{
    public CameraPose(Vector3 position, Quaternion orientation, float fovDegrees)
    {
        Position = position;
        Orientation = orientation;
        FovDegrees = fovDegrees;
    }

    /// <summary>World position, meters.</summary>
    public Vector3 Position { get; }

    /// <summary>Camera to world rotation (unit). Camera axes: +X right, +Y up, -Z view direction.</summary>
    public Quaternion Orientation { get; }

    /// <summary>Vertical field of view, degrees.</summary>
    public float FovDegrees { get; }

    /// <summary>The direction the camera looks (unit).</summary>
    public Vector3 Forward => Vector3.Transform(-Vector3.UnitZ, Orientation);

    /// <summary>The pose a fraction <paramref name="t"/> (0..1) of the way from <paramref name="a"/> to <paramref name="b"/>.</summary>
    public static CameraPose Lerp(in CameraPose a, in CameraPose b, float t) => new(
        Vector3.Lerp(a.Position, b.Position, t),
        Quaternion.Slerp(a.Orientation, b.Orientation, t),
        a.FovDegrees + (b.FovDegrees - a.FovDegrees) * t);
}
