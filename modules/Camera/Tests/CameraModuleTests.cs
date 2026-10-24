using System;
using System.Numerics;
using Neo.Flight;
using Neo.Shell;
using Xunit;

namespace Neo.Camera.Tests;

public class CameraModuleTests
{
    private const float Dt = 1f / 60f;
    private static readonly TickContext OneStep = new(1, Dt);
    private static readonly Vector3 Start = new(0f, 400f, 0f);

    // A plane flying along its nose; the test moves it each step as Flight would.
    private sealed class Plane
    {
        public Vector3 Position = Start;
        public Quaternion Orientation = Quaternion.Identity;
        public float Speed = 42f;
        public int Id;

        public Vector3 Forward => Vector3.Transform(-Vector3.UnitZ, Orientation);

        public PlaneState State => new(Id, Position, Orientation, Forward * Speed, Speed, 0.5f);

        public void Step(float dt) => Position += Forward * Speed * dt;
    }

    private static (CameraModule camera, FakeBus bus) NewCamera()
    {
        var bus = new FakeBus();
        var camera = new CameraModule();
        camera.Start(bus);
        Assert.NotNull(bus.Plane);
        return (camera, bus);
    }

    // One fixed step: Flight moves the plane and publishes, then the camera ticks.
    private static void Fly(CameraModule camera, FakeBus bus, Plane plane, int steps, Action? perStep = null)
    {
        for (int i = 0; i < steps; i++)
        {
            perStep?.Invoke();
            plane.Step(Dt);
            bus.Send(plane.State);
            camera.Tick(in OneStep);
        }
    }

    // Where the camera should settle: 14 m behind, 4 m above the plane, along its orientation.
    private static Vector3 Spot(Plane plane) =>
        plane.Position + Vector3.Transform(new Vector3(0f, CameraModule.Up, CameraModule.Back), plane.Orientation);

    // The camera position in the plane's body frame (+X right, +Y up, -Z forward).
    private static Vector3 InPlaneFrame(CameraModule camera, Plane plane) =>
        Vector3.Transform(camera.Pose.Position - plane.Position, Quaternion.Conjugate(plane.Orientation));

    private static float AngleBetween(Quaternion a, Quaternion b) =>
        2f * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(a, b)), 0f, 1f));

    [Fact]
    public void HasNoPoseBeforeThePlaneIsSeen()
    {
        var (camera, _) = NewCamera();
        camera.Tick(in OneStep);
        Assert.False(camera.HasPose);
        Assert.Equal(CameraModule.MinFov, camera.Pose.FovDegrees);
    }

    [Fact]
    public void IgnoresOtherPlanes()
    {
        var (camera, bus) = NewCamera();
        var other = new Plane { Id = 3 };
        bus.Send(other.State);
        camera.Tick(in OneStep);
        Assert.False(camera.HasPose);
    }

    [Fact]
    public void SitsBehindAndAboveALevelPlane()
    {
        var (camera, bus) = NewCamera();
        var plane = new Plane();
        Fly(camera, bus, plane, 120);

        Assert.True(camera.HasPose);
        var offset = camera.Pose.Position - plane.Position;
        Assert.Equal(0f, offset.X, 0.01f);
        Assert.Equal(4f, offset.Y, 0.01f);
        Assert.Equal(14f, offset.Z, 0.01f); // behind: the plane flies -Z
    }

    [Fact]
    public void SitsBehindAndAboveAlongThePlanesOrientation()
    {
        var (camera, bus) = NewCamera();
        // Heading +X, nose 20 degrees up, banked 30 degrees.
        var plane = new Plane
        {
            Orientation = Quaternion.CreateFromYawPitchRoll(-MathF.PI / 2f, 20f * MathF.PI / 180f, 30f * MathF.PI / 180f),
        };
        Fly(camera, bus, plane, 120);

        var local = InPlaneFrame(camera, plane);
        Assert.Equal(0f, local.X, 0.01f);
        Assert.Equal(CameraModule.Up, local.Y, 0.01f);
        Assert.Equal(CameraModule.Back, local.Z, 0.01f);
        // World: behind means against the nose, which points +X here.
        Assert.True(camera.Pose.Position.X < plane.Position.X - 10f);
    }

    [Fact]
    public void LooksSlightlyAheadOfThePlane()
    {
        var (camera, bus) = NewCamera();
        var plane = new Plane();
        Fly(camera, bus, plane, 120);

        var look = camera.Pose.Forward;
        var toPlane = Vector3.Normalize(plane.Position - camera.Pose.Position);
        var toAhead = Vector3.Normalize(plane.Position + plane.Forward * CameraModule.LookAhead - camera.Pose.Position);
        Assert.True(Vector3.Dot(look, toAhead) > 0.9999f, "looks at a point ahead of the plane");
        Assert.True(Vector3.Dot(look, toPlane) > 0.97f, "the plane stays near the middle of the view");
        Assert.True(Vector3.Dot(look, -Vector3.UnitZ) > 0.95f, "faces the plane's heading");
        // Slightly down: the plane is below the camera.
        Assert.True(look.Y < 0f);
        // Level plane: the camera is not rolled.
        var right = Vector3.Transform(Vector3.UnitX, camera.Pose.Orientation);
        Assert.Equal(0f, right.Y, 0.001f);
        Assert.Equal(1f, camera.Pose.Orientation.Length(), 0.001f);
    }

    [Fact]
    public void FollowsAPlaneAtSteadySpeedWithoutDrifting()
    {
        var (camera, bus) = NewCamera();
        var plane = new Plane { Speed = 60f };
        Fly(camera, bus, plane, 600);
        Assert.True(Vector3.Distance(camera.Pose.Position, Spot(plane)) < 0.01f);
    }

    [Fact]
    public void LagsBehindWhenThePlaneSpeedsUpThenSettles()
    {
        var (camera, bus) = NewCamera();
        var plane = new Plane { Speed = 25f };
        Fly(camera, bus, plane, 120);
        float settled = Vector3.Distance(camera.Pose.Position, plane.Position);

        plane.Speed = 60f;
        Fly(camera, bus, plane, 6);
        float stretched = Vector3.Distance(camera.Pose.Position, plane.Position);
        Assert.True(stretched > settled + 1f, $"the camera falls back when the plane speeds up ({stretched} vs {settled})");

        Fly(camera, bus, plane, 180);
        Assert.Equal(settled, Vector3.Distance(camera.Pose.Position, plane.Position), 0.02f);
        Assert.True(Vector3.Distance(camera.Pose.Position, Spot(plane)) < 0.02f);
    }

    [Fact]
    public void LagsThroughATurnThenSettlesBehind()
    {
        var (camera, bus) = NewCamera();
        var plane = new Plane();
        Fly(camera, bus, plane, 120);

        // A sharp 90 degree turn to the left (heading -X).
        plane.Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
        Fly(camera, bus, plane, 1);
        float first = Vector3.Distance(camera.Pose.Position, Spot(plane));
        Assert.True(first > 10f, $"does not jump behind the plane at once ({first} m off)");
        Assert.True(AngleBetween(camera.Follow, plane.Orientation) > 1f);

        float last = first;
        for (int i = 0; i < 30; i++)
        {
            Fly(camera, bus, plane, 1);
            float gap = Vector3.Distance(camera.Pose.Position, Spot(plane));
            Assert.True(gap < last, "swings smoothly toward its spot");
            last = gap;
        }
        Assert.True(last > 1f, $"still catching up after half a second ({last} m off)");

        Fly(camera, bus, plane, 180);
        Assert.True(Vector3.Distance(camera.Pose.Position, Spot(plane)) < 0.02f);
        Assert.True(AngleBetween(camera.Follow, plane.Orientation) < 0.001f);
        var local = InPlaneFrame(camera, plane);
        Assert.Equal(CameraModule.Back, local.Z, 0.02f);
        Assert.Equal(CameraModule.Up, local.Y, 0.02f);
    }

    [Fact]
    public void LagsMoreWhileRolling()
    {
        const float rate = 3f; // rad/s, about a roll at full stick
        float Lag(Vector3 axis)
        {
            var (camera, bus) = NewCamera();
            var plane = new Plane();
            Fly(camera, bus, plane, 60);
            Fly(camera, bus, plane, 30, () =>
                plane.Orientation = Quaternion.Normalize(plane.Orientation * Quaternion.CreateFromAxisAngle(axis, rate * Dt)));
            return AngleBetween(camera.Follow, plane.Orientation);
        }

        float yawLag = Lag(Vector3.UnitY);
        float rollLag = Lag(Vector3.UnitZ);
        Assert.True(yawLag > 0.05f, "turns lag too");
        Assert.True(rollLag > yawLag * 1.3f, $"rolling lags more ({rollLag} vs {yawLag} rad)");
    }

    [Fact]
    public void FovWidensWithSpeed()
    {
        Assert.Equal(70f, CameraModule.FovFor(25f), 0.001f);
        Assert.Equal(80f, CameraModule.FovFor(60f), 0.001f);
        Assert.Equal(70f, CameraModule.FovFor(0f), 0.001f);
        Assert.Equal(80f, CameraModule.FovFor(100f), 0.001f);
        Assert.True(CameraModule.FovFor(42f) > 70f && CameraModule.FovFor(42f) < 80f);

        var (camera, bus) = NewCamera();
        var plane = new Plane { Speed = 25f };
        Fly(camera, bus, plane, 60);
        Assert.Equal(70f, camera.Pose.FovDegrees, 0.01f);

        plane.Speed = 60f;
        float last = camera.Pose.FovDegrees;
        Fly(camera, bus, plane, 1);
        Assert.True(camera.Pose.FovDegrees > last && camera.Pose.FovDegrees < last + 1f, "widens smoothly, no jump");
        for (int i = 0; i < 300; i++)
        {
            last = camera.Pose.FovDegrees;
            Fly(camera, bus, plane, 1);
            Assert.True(camera.Pose.FovDegrees >= last);
        }
        Assert.Equal(80f, camera.Pose.FovDegrees, 0.05f);
    }

    [Fact]
    public void KeepsThePreviousPoseForInterpolation()
    {
        var (camera, bus) = NewCamera();
        var plane = new Plane();
        Fly(camera, bus, plane, 10);
        var before = camera.Pose;
        Fly(camera, bus, plane, 1);
        Assert.Equal(before.Position, camera.PreviousPose.Position);
        Assert.Equal(before.Orientation, camera.PreviousPose.Orientation);
        Assert.Equal(before.FovDegrees, camera.PreviousPose.FovDegrees);

        var half = CameraPose.Lerp(camera.PreviousPose, camera.Pose, 0.5f);
        Assert.Equal((camera.PreviousPose.Position + camera.Pose.Position) / 2f, half.Position);
    }

    [Fact]
    public void IsTheModulesOnlyEntryClass()
    {
        int entries = 0;
        foreach (var type in typeof(CameraModule).Assembly.GetExportedTypes())
            if (type.IsClass && !type.IsAbstract && typeof(IModule).IsAssignableFrom(type))
                entries++;
        Assert.Equal(1, entries);
    }
}
