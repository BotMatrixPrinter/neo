using System;
using System.Numerics;
using Xunit;

namespace Neo.HUD.Tests;

public class MarkersTests
{
    private static readonly Vector2 Screen = new(1920f, 1080f);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Allies_are_green_and_rounded(int id)
    {
        var color = Markers.TeamColor(id);
        Assert.True(color.Y > 0.7f && color.X < 0.5f && color.Z < 0.5f);
        Assert.Equal(MarkerShape.Rounded, Markers.ShapeOf(id));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    public void Central_is_red_and_sharp(int id)
    {
        var color = Markers.TeamColor(id);
        Assert.True(color.X > 0.7f && color.Y < 0.5f && color.Z < 0.5f);
        Assert.Equal(MarkerShape.Sharp, Markers.ShapeOf(id));
    }

    [Theory]
    [InlineData(0f, "0 m")]
    [InlineData(-5f, "0 m")]
    [InlineData(7.4f, "7 m")]
    [InlineData(640f, "640 m")]
    [InlineData(640.5f, "641 m")]
    [InlineData(999.4f, "999 m")]
    [InlineData(999.6f, "1.0 km")]
    [InlineData(1000f, "1.0 km")]
    [InlineData(1200f, "1.2 km")]
    [InlineData(1249f, "1.2 km")]
    [InlineData(1250f, "1.3 km")]
    [InlineData(12345f, "12.3 km")]
    [InlineData(1e7f, "100.0 km")]
    public void Distance_text(float meters, string text) => Assert.Equal(text, Markers.DistanceText(meters));

    [Fact]
    public void In_front_and_inside_is_on_screen()
    {
        var ahead = new Vector3(0f, 0f, -300f);
        Assert.True(Markers.IsOnScreen(ahead, new Vector2(960f, 540f), Screen));
        Assert.True(Markers.IsOnScreen(ahead, new Vector2(0f, 1080f), Screen));
        Assert.False(Markers.IsBehind(ahead));
    }

    [Theory]
    [InlineData(-1f, 540f)]
    [InlineData(1921f, 540f)]
    [InlineData(960f, -3f)]
    [InlineData(960f, 1200f)]
    public void In_front_but_outside_the_view_is_off_screen(float x, float y) =>
        Assert.False(Markers.IsOnScreen(new Vector3(0f, 0f, -300f), new Vector2(x, y), Screen));

    [Theory]
    [InlineData(300f)]
    [InlineData(0f)]
    [InlineData(-0.01f)]
    public void Behind_the_camera_is_off_screen_even_if_it_projects_inside(float z)
    {
        var view = new Vector3(5f, 0f, z);
        Assert.True(Markers.IsBehind(view));
        Assert.False(Markers.IsOnScreen(view, new Vector2(960f, 540f), Screen));
    }

    [Theory]
    // In front: right, left, above (screen up is -Y), below.
    [InlineData(100f, 0f, -50f, 1f, 0f)]
    [InlineData(-100f, 0f, -50f, -1f, 0f)]
    [InlineData(0f, 100f, -50f, 0f, -1f)]
    [InlineData(0f, -100f, -50f, 0f, 1f)]
    // Behind: behind-left is still left, behind-right right, behind-above up.
    [InlineData(-100f, 0f, 400f, -1f, 0f)]
    [InlineData(100f, 0f, 400f, 1f, 0f)]
    [InlineData(0f, 100f, 400f, 0f, -1f)]
    // Straight behind: turn around, shown at the bottom.
    [InlineData(0f, 0f, 400f, 0f, 1f)]
    public void Edge_direction_points_where_to_turn(float x, float y, float z, float dx, float dy)
    {
        var d = Markers.EdgeDirection(new Vector3(x, y, z));
        Assert.Equal(dx, d.X, 4);
        Assert.Equal(dy, d.Y, 4);
    }

    [Fact]
    public void Edge_direction_follows_the_projection_in_front_and_mirrors_behind()
    {
        // Up and to the right, in front: 45 degrees up-right on screen (-45 degrees, screen Y down).
        Assert.Equal(-MathF.PI / 4f, Markers.EdgeAngle(new Vector3(50f, 50f, -10f)), 4);
        // The same direction behind the camera: still up-right, not flipped down-left as a projection would.
        Assert.Equal(-MathF.PI / 4f, Markers.EdgeAngle(new Vector3(50f, 50f, 300f)), 4);
        // Behind, low left: down-left.
        Assert.Equal(3f * MathF.PI / 4f, Markers.EdgeAngle(new Vector3(-20f, -20f, 300f)), 4);
    }

    [Fact]
    public void Edge_point_is_on_a_circle_of_forty_percent_of_the_height()
    {
        var left = Markers.EdgePoint(new Vector3(-100f, 0f, 200f), Screen);
        Assert.Equal(960f - 432f, left.X, 2);
        Assert.Equal(540f, left.Y, 2);
        var p = Markers.EdgePoint(new Vector3(30f, -40f, -5f), Screen);
        Assert.Equal(432f, Vector2.Distance(p, Screen * 0.5f), 2);
        Assert.True(p.X > 960f && p.Y > 540f);
    }
}
