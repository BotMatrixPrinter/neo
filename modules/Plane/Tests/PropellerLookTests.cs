using System;
using System.Numerics;
using System.Text.Json;
using Xunit;

namespace Neo.Plane.Tests;

public class PropellerLookTests
{
    private static readonly double[] FrameRates = { 240, 144, 120, 60, 50, 30, 20, 15, 10, 5, 1 };

    [Fact]
    public void BladesNeverTurnMoreThanASixthPerFrameAtAnyFrameRateOrThrottle()
    {
        Assert.True(PropellerLook.MaxTurnsPerFrame <= 1f / 6f + 1e-6f);
        foreach (double fps in FrameRates)
            for (float throttle = -0.5f; throttle <= 1.5f; throttle += 0.05f)
            {
                float turns = PropellerLook.BladeTurns(throttle, 1.0 / fps);
                Assert.True(turns > 0f, $"{fps} fps throttle {throttle}: not turning");
                Assert.True(turns <= PropellerLook.MaxTurnsPerFrame, $"{fps} fps throttle {throttle}: {turns} turns/frame");
            }
        // A long hitch does not make the blades jump.
        Assert.Equal(PropellerLook.MaxTurnsPerFrame, PropellerLook.BladeTurns(1f, 2.0));
    }

    [Fact]
    public void BladesTurnClearlyAtIdleAndFasterWithThrottle()
    {
        const double frame = 1.0 / 60;
        float idle = PropellerLook.BladeTurns(0f, frame);
        float cruise = PropellerLook.BladeTurns(0.6f, frame);
        float full = PropellerLook.BladeTurns(1f, frame);
        Assert.InRange(idle, 1f / 36f, 1f / 8f); // at least 10 degrees a frame, well below aliasing
        Assert.True(cruise > idle && full > cruise);
        Assert.InRange(full, 0f, PropellerLook.MaxTurnsPerFrame);
        // Below the cap the turn rate is the same whatever the frame rate.
        Assert.Equal(PropellerLook.BladeTurns(0.3f, 1.0 / 120) * 2f, PropellerLook.BladeTurns(0.3f, 1.0 / 60), 5);
        // Throttle outside 0..1 is clamped.
        Assert.Equal(idle, PropellerLook.BladeTurns(-1f, frame));
        Assert.Equal(full, PropellerLook.BladeTurns(2f, frame));
        Assert.Equal(idle, PropellerLook.BladeTurns(float.NaN, frame));
    }

    [Fact]
    public void BladesDoNotTurnWithoutTime()
    {
        Assert.Equal(0f, PropellerLook.BladeTurns(1f, 0));
        Assert.Equal(0f, PropellerLook.BladeTurns(1f, -0.1));
        Assert.Equal(0f, PropellerLook.BladeTurns(1f, double.NaN));
    }

    [Fact]
    public void NoDiscAndSolidBladesNearIdle()
    {
        foreach (float throttle in new[] { -1f, 0f, 0.05f, 0.1f, PropellerLook.DiscStart })
        {
            Assert.Equal(0f, PropellerLook.DiscFade(throttle));
            Assert.Equal(0f, PropellerLook.DiscAlpha(throttle));
            Assert.Equal(1f, PropellerLook.BladeAlpha(throttle));
        }
    }

    [Fact]
    public void DiscFadesInAndBladesPartlyOutWithThrottle()
    {
        float lastDisc = 0f, lastBlade = 1f;
        for (float throttle = 0f; throttle <= 1f; throttle += 0.02f)
        {
            float disc = PropellerLook.DiscAlpha(throttle);
            float blade = PropellerLook.BladeAlpha(throttle);
            Assert.True(disc >= lastDisc - 1e-6f, $"disc fades out at throttle {throttle}");
            Assert.True(blade <= lastBlade + 1e-6f, $"blades come back at throttle {throttle}");
            Assert.InRange(disc, 0f, 0.8f); // semi-transparent, never solid
            Assert.InRange(blade, 0.25f, 1f); // partly faded, never gone
            // Smooth: no pops between nearby throttles.
            Assert.True(MathF.Abs(disc - PropellerLook.DiscAlpha(throttle + 0.01f)) < 0.05f);
            lastDisc = disc;
            lastBlade = blade;
        }
        // Cruise (0.6) and above: the full prop blur.
        foreach (float throttle in new[] { 0.6f, 0.8f, 1f, 3f })
        {
            Assert.Equal(1f, PropellerLook.DiscFade(throttle), 4);
            Assert.InRange(PropellerLook.DiscAlpha(throttle), 0.4f, 0.8f);
            Assert.InRange(PropellerLook.BladeAlpha(throttle), 0.25f, 0.6f);
        }
        // Halfway it is halfway.
        float mid = (PropellerLook.DiscStart + PropellerLook.DiscFull) / 2f;
        Assert.Equal(0.5f, PropellerLook.DiscFade(mid), 3);
    }

    [Theory]
    [InlineData(PlaneModel.Biplane)]
    [InlineData(PlaneModel.Triplane)]
    public void ModelHasAPropellerNodeThatTurnsAboutTheNoseAxis(PlaneModel model)
    {
        using var gltf = Glb.ReadJson(model);
        var root = gltf.RootElement;
        var propeller = Glb.Node(gltf, "Propeller");
        Assert.True(propeller.HasValue, PlaneLook.FileName(model) + " has no Propeller node");
        var prop = propeller!.Value;

        // Its own axes are the plane's: no rotation or scale, so its local Z is the nose axis (nose toward -Z).
        if (prop.TryGetProperty("rotation", out var rotation))
        {
            var q = rotation.EnumerateArray().GetEnumerator();
            float[] r = new float[4];
            for (int i = 0; i < 4 && q.MoveNext(); i++)
                r[i] = q.Current.GetSingle();
            Assert.True(MathF.Abs(r[0]) < 1e-4f && MathF.Abs(r[1]) < 1e-4f && MathF.Abs(r[2]) < 1e-4f, "Propeller node is rotated");
        }
        Assert.False(prop.TryGetProperty("scale", out _), "Propeller node is scaled");
        Assert.False(prop.TryGetProperty("matrix", out _), "Propeller node has a matrix");
        Assert.Equal(Vector3.UnitZ, PropellerLook.SpinAxis);

        // At the front of the plane, on the center line.
        var t = prop.GetProperty("translation");
        Assert.Equal(0f, t[0].GetSingle(), 3);
        Assert.True(t[2].GetSingle() < -3f, "Propeller is not at the nose");

        // Its mesh is centered on the shaft: blades span X/Y, thin along Z (the spin axis).
        var mesh = root.GetProperty("meshes")[prop.GetProperty("mesh").GetInt32()];
        var accessor = root.GetProperty("accessors")[mesh.GetProperty("primitives")[0].GetProperty("attributes").GetProperty("POSITION").GetInt32()];
        var min = accessor.GetProperty("min");
        var max = accessor.GetProperty("max");
        float spanX = max[0].GetSingle() - min[0].GetSingle();
        float spanY = max[1].GetSingle() - min[1].GetSingle();
        float depthZ = max[2].GetSingle() - min[2].GetSingle();
        Assert.True(MathF.Max(spanX, spanY) > 2f, "blades too short");
        Assert.True(depthZ < 0.2f * MathF.Max(spanX, spanY), "propeller is not thin along the nose axis");
        for (int i = 0; i < 3; i++)
            Assert.Equal(0f, (min[i].GetSingle() + max[i].GetSingle()) / 2f, 2);
    }
}
