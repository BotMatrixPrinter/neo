using System;
using Neo.Flight;
using Xunit;

namespace Neo.Plane.Tests;

public class PlaneLookTests
{
    [Fact]
    public void TeamPicksTheModel()
    {
        for (int id = 0; id < Teams.PlaneCount; id++)
            Assert.Equal(Teams.IsAllies(id) ? PlaneModel.Biplane : PlaneModel.Triplane, PlaneLook.ModelFor(id));
        Assert.Equal(PlaneModel.Biplane, PlaneLook.ModelFor(0));
        Assert.Equal(PlaneModel.Biplane, PlaneLook.ModelFor(3));
        Assert.Equal(PlaneModel.Triplane, PlaneLook.ModelFor(4));
        Assert.Equal(PlaneModel.Triplane, PlaneLook.ModelFor(7));
        Assert.Equal("biplane.glb", PlaneLook.FileName(PlaneModel.Biplane));
        Assert.Equal("triplane.glb", PlaneLook.FileName(PlaneModel.Triplane));
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(0.8f)]
    [InlineData(0.5f)]
    [InlineData(float.NaN)]
    public void NoDamageLooksAtOrAboveHalfHealth(float health)
    {
        Assert.False(PlaneLook.IsDamaged(health));
        Assert.Equal(0f, PlaneLook.Severity(health));
        Assert.Equal(0f, PlaneLook.EngineSmoke(health));
        Assert.Equal(0f, PlaneLook.WingSmoke(health));
        Assert.Equal(0, PlaneLook.Holes(health));
    }

    [Fact]
    public void EngineBelowHalfTrailsSmokeThickerWhenWorse()
    {
        Assert.Equal(0.5f, PlaneLook.DamageThreshold);
        float justBelow = PlaneLook.EngineSmoke(0.49f);
        Assert.True(justBelow >= PlaneLook.EngineSmokeMin - 1e-6f && justBelow < 0.4f, $"just below: {justBelow}");
        float last = 0f;
        for (float engine = 0.49f; engine >= 0f; engine -= 0.01f)
        {
            float smoke = PlaneLook.EngineSmoke(engine);
            Assert.True(smoke >= last, $"engine {engine}: {smoke} < {last}");
            last = smoke;
        }
        Assert.Equal(1f, PlaneLook.EngineSmoke(0f), 5);
        Assert.Equal(1f, PlaneLook.EngineSmoke(-0.2f), 5);
    }

    [Fact]
    public void WingBelowHalfShowsHolesAndALittleSmoke()
    {
        Assert.True(PlaneLook.IsDamaged(0.49f));
        Assert.Equal(2, PlaneLook.Holes(0.49f));
        Assert.Equal(PlaneLook.MaxHoles, PlaneLook.Holes(0f));
        int lastHoles = 0;
        for (float wing = 0.49f; wing >= 0f; wing -= 0.01f)
        {
            float smoke = PlaneLook.WingSmoke(wing);
            Assert.True(smoke > 0f && smoke <= PlaneLook.WingSmokeMax, $"wing {wing}: smoke {smoke}");
            Assert.True(smoke < PlaneLook.EngineSmoke(wing), "a wing smokes less than an engine");
            int holes = PlaneLook.Holes(wing);
            Assert.True(holes >= lastHoles && holes <= PlaneLook.MaxHoles);
            lastHoles = holes;
        }
    }

    [Fact]
    public void LifeIsFlyingFallingThenCrashedOnTheGround()
    {
        Assert.Equal(PlaneLife.Flying, PlaneLook.Life(true, 400f, 50f));
        Assert.Equal(PlaneLife.Flying, PlaneLook.Life(true, 0f, 0f));
        Assert.Equal(PlaneLife.Falling, PlaneLook.Life(false, 400f, 55f));
        Assert.Equal(PlaneLife.Falling, PlaneLook.Life(false, 2f, 70f));
        Assert.Equal(PlaneLife.Crashed, PlaneLook.Life(false, 0f, 0f));
        Assert.Equal(PlaneLife.Crashed, PlaneLook.Life(false, 0.5f, 60f));
        Assert.Equal(PlaneLife.Crashed, PlaneLook.Life(false, 12f, 0f)); // stopped on a hill
    }

    [Theory]
    [InlineData(PlaneModel.Biplane)]
    [InlineData(PlaneModel.Triplane)]
    public void ModelHasItsMarkersAndBody(PlaneModel model)
    {
        using var gltf = Glb.ReadJson(model);
        Assert.NotNull(Glb.Node(gltf, model == PlaneModel.Biplane ? "Biplane" : "Triplane"));
        Assert.NotNull(Glb.Node(gltf, "Propeller"));
        Assert.NotNull(Glb.Node(gltf, "Exhaust"));
        var left = Glb.Node(gltf, "LeftWing");
        var right = Glb.Node(gltf, "RightWing");
        Assert.NotNull(left);
        Assert.NotNull(right);
        // Left is -X, right +X, mirrored, above the fuselage (on top of the top wing).
        var l = left!.Value.GetProperty("translation");
        var r = right!.Value.GetProperty("translation");
        Assert.True(l[0].GetSingle() < -1f && r[0].GetSingle() > 1f);
        Assert.Equal(-l[0].GetSingle(), r[0].GetSingle(), 3);
        Assert.True(l[1].GetSingle() > 1f && Math.Abs(l[1].GetSingle() - r[1].GetSingle()) < 1e-3f);
    }

    [Fact]
    public void BothModelsAreTheSameScale()
    {
        float bi = Span(PlaneModel.Biplane), tri = Span(PlaneModel.Triplane);
        Assert.InRange(bi, 6.5f, 7.5f);
        Assert.InRange(tri, 6.5f, 7.5f);
    }

    // Wing span (X extent) of the model's body mesh.
    private static float Span(PlaneModel model)
    {
        using var gltf = Glb.ReadJson(model);
        var root = gltf.RootElement;
        var body = Glb.Node(gltf, model == PlaneModel.Biplane ? "Biplane" : "Triplane")!.Value;
        var mesh = root.GetProperty("meshes")[body.GetProperty("mesh").GetInt32()];
        float min = float.MaxValue, max = float.MinValue;
        foreach (var primitive in mesh.GetProperty("primitives").EnumerateArray())
        {
            var accessor = root.GetProperty("accessors")[primitive.GetProperty("attributes").GetProperty("POSITION").GetInt32()];
            min = MathF.Min(min, accessor.GetProperty("min")[0].GetSingle());
            max = MathF.Max(max, accessor.GetProperty("max")[0].GetSingle());
        }
        return max - min;
    }
}
