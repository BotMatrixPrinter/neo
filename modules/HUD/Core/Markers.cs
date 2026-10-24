using System;
using System.Numerics;
using Neo.Flight;

namespace Neo.HUD;

/// <summary>How a plane marker is drawn, so the teams differ by shape as well as color.</summary>
public enum MarkerShape
{
    /// <summary>Allies: a triangle with well-rounded corners.</summary>
    Rounded,

    /// <summary>Central: a sharp, pointed triangle.</summary>
    Sharp,
}

/// <summary>
/// The math of the plane markers, no Godot. Camera space is the camera's own frame: +X right,
/// +Y up, the camera looks along -Z. Screen space is pixels, +Y down, the center at half the size.
/// </summary>
public static class Markers
{
    /// <summary>Off-screen arrows sit on a circle around the screen center of this fraction of the screen height.</summary>
    public const float EdgeRadiusOfHeight = 0.4f;

    /// <summary>Closer than this in front of the camera counts as behind it (projection breaks down).</summary>
    public const float MinDepth = 0.05f;

    /// <summary>From this many meters the distance is shown in km.</summary>
    public const int KmFrom = 1000;

    /// <summary>Longest distance with its own text, meters; farther shows this.</summary>
    public const int MaxMeters = 99_950;

    /// <summary>Allies' marker color (r, g, b, 0..1): green.</summary>
    public static readonly Vector3 AlliesColor = new(0.3f, 0.9f, 0.35f);

    /// <summary>Central's marker color (r, g, b, 0..1): red.</summary>
    public static readonly Vector3 CentralColor = new(1f, 0.24f, 0.18f);

    private static readonly string[] MeterTexts = BuildMeterTexts();
    private static readonly string[] KmTexts = BuildKmTexts();

    /// <summary>The marker color of <paramref name="planeId"/>'s team.</summary>
    public static Vector3 TeamColor(int planeId) => Teams.IsAllies(planeId) ? AlliesColor : CentralColor;

    /// <summary>The marker shape of <paramref name="planeId"/>'s team: rounded for the Allies, sharp for Central.</summary>
    public static MarkerShape ShapeOf(int planeId) => Teams.IsAllies(planeId) ? MarkerShape.Rounded : MarkerShape.Sharp;

    /// <summary>The point is behind the camera (or too close in front of it to project).</summary>
    public static bool IsBehind(Vector3 view) => !(-view.Z > MinDepth);

    /// <summary>
    /// The plane at camera-space <paramref name="view"/>, projected to <paramref name="projected"/>,
    /// is on a screen of <paramref name="screen"/> pixels: in front of the camera and inside it.
    /// </summary>
    public static bool IsOnScreen(Vector3 view, Vector2 projected, Vector2 screen) =>
        !IsBehind(view)
        && projected.X >= 0f && projected.X <= screen.X
        && projected.Y >= 0f && projected.Y <= screen.Y;

    /// <summary>
    /// The unit screen direction (+Y down) from the center toward camera-space <paramref name="view"/>:
    /// where the player has to turn to see it. Behind the camera it is mirrored the same way, so
    /// behind-left is left and behind-above is up; straight behind is down.
    /// </summary>
    public static Vector2 EdgeDirection(Vector3 view)
    {
        var d = new Vector2(view.X, -view.Y);
        float length = d.Length();
        return length > 1e-6f ? d / length : new Vector2(0f, 1f);
    }

    /// <summary>The angle of <see cref="EdgeDirection"/>, radians, screen space: 0 right, +π/2 down.</summary>
    public static float EdgeAngle(Vector3 view)
    {
        var d = EdgeDirection(view);
        return MathF.Atan2(d.Y, d.X);
    }

    /// <summary>Where the off-screen arrow for camera-space <paramref name="view"/> sits on a screen of <paramref name="screen"/> pixels.</summary>
    public static Vector2 EdgePoint(Vector3 view, Vector2 screen) =>
        screen * 0.5f + EdgeDirection(view) * (EdgeRadiusOfHeight * screen.Y);

    /// <summary>"640 m" below 1000 m (rounded to the meter), from there "1.2 km" (rounded to 0.1 km). Prebuilt: no allocations.</summary>
    public static string DistanceText(float meters)
    {
        if (!(meters > 0f))
            meters = 0f;
        if (meters > MaxMeters)
            meters = MaxMeters;
        int m = (int)MathF.Round(meters, MidpointRounding.AwayFromZero);
        if (m < KmFrom)
            return MeterTexts[m];
        return KmTexts[(m + 50) / 100];
    }

    private static string[] BuildMeterTexts()
    {
        var texts = new string[KmFrom];
        for (int m = 0; m < KmFrom; m++)
            texts[m] = m.ToString(System.Globalization.CultureInfo.InvariantCulture) + " m";
        return texts;
    }

    private static string[] BuildKmTexts()
    {
        int tenths = (MaxMeters + 50) / 100;
        var texts = new string[tenths + 1];
        for (int t = 0; t <= tenths; t++)
            texts[t] = (t / 10).ToString(System.Globalization.CultureInfo.InvariantCulture) + "." + (t % 10) + " km";
        return texts;
    }
}
