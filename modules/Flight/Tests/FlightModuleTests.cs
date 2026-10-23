using System;
using System.Numerics;
using Neo.Input;
using Neo.Shell;
using Xunit;

namespace Neo.Flight.Tests;

public class FlightModuleTests
{
    private const double Dt = 1.0 / 60.0;
    private const float Deg = MathF.PI / 180f;

    private sealed class Rig
    {
        public readonly TestBus Bus = new();
        public readonly FlightModule Module = new();
        private long _tick;

        public Rig() => Module.Start(Bus);

        public PlaneState Step(in ControlInput input)
        {
            Bus.Publish(input);
            Module.Tick(new TickContext(++_tick, Dt));
            return Bus.Last;
        }

        /// <summary>Flies <paramref name="seconds"/> with the same input, checking every state.</summary>
        public PlaneState Fly(ControlInput input, float seconds, Action<PlaneState>? check = null)
        {
            int steps = (int)MathF.Round(seconds * 60f);
            for (int i = 0; i < steps; i++)
            {
                var state = Step(input);
                check?.Invoke(state);
            }
            return Bus.Last;
        }
    }

    private static ControlInput Sticks(float pitch = 0f, float roll = 0f, float yaw = 0f, float throttle = 0.5f) =>
        new(0, pitch, roll, yaw, throttle, false, false, Vector3.Zero);

    private static ControlInput Aim(Vector3 direction, float throttle = 0.5f) =>
        new(0, 0f, 0f, 0f, throttle, false, true, direction);

    private static Vector3 Forward(in PlaneState s) => Vector3.Transform(-Vector3.UnitZ, s.Orientation);

    private static float AngleBetween(Vector3 a, Vector3 b) =>
        MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b)), -1f, 1f));

    private static float RotationAngle(Quaternion a, Quaternion b) =>
        2f * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(a, b)), 0f, 1f));

    /// <summary>A fixed pseudo-random pilot: same seed, same inputs.</summary>
    private static ControlInput RandomInput(ref uint seed, int step)
    {
        float Next(ref uint s) { s = s * 1664525u + 1013904223u; return (s >> 8) / (float)(1 << 24); }
        float pitch = Next(ref seed) * 2f - 1f, roll = Next(ref seed) * 2f - 1f, yaw = Next(ref seed) * 2f - 1f;
        float throttle = Next(ref seed);
        bool aim = (step / 300) % 2 == 1;
        var dir = new Vector3(Next(ref seed) * 2f - 1f, Next(ref seed) * 2f - 1f, Next(ref seed) * 2f - 1f);
        return new ControlInput(0, pitch, roll, yaw, throttle, false, aim, dir);
    }

    [Fact]
    public void StartsAt400MetersHeadingMinusZAtCruise()
    {
        var rig = new Rig();
        var s = rig.Step(Sticks());

        Assert.Equal(1, rig.Bus.StatesPublished);
        Assert.Equal(0, s.PlaneId);
        Assert.InRange(s.Position.Y, 399.9f, 400.1f);
        Assert.InRange(s.Position.X, -0.01f, 0.01f);
        Assert.InRange(s.Position.Z, -1f, -0.5f); // one step forward along -Z
        Assert.True(AngleBetween(Forward(s), -Vector3.UnitZ) < 0.01f);
        Assert.InRange(s.Speed, 41f, 43f);
        Assert.InRange(s.Velocity.Length(), s.Speed - 0.01f, s.Speed + 0.01f);
    }

    [Fact]
    public void PublishesEveryTickAfterTheStep()
    {
        var rig = new Rig();
        var first = rig.Step(Sticks());
        var second = rig.Step(Sticks());
        Assert.Equal(2, rig.Bus.StatesPublished);
        Assert.True(second.Position.Z < first.Position.Z);
    }

    [Fact]
    public void ThrottleSetsLevelSpeed_CruiseMaxAndMin()
    {
        Assert.InRange(new Rig().Fly(Sticks(throttle: 0.5f), 20f).Speed, 40f, 44f);
        var full = new Rig().Fly(Sticks(throttle: 1f), 20f);
        Assert.InRange(full.Speed, 57f, 60f);
        Assert.Equal(1f, full.Throttle);
        var idle = new Rig().Fly(Sticks(throttle: 0f), 20f);
        Assert.InRange(idle.Speed, 25f, 27f);
        Assert.Equal(0f, idle.Throttle);
    }

    [Fact]
    public void SpeedStaysInRange_DivingClimbingAndRandomFlying()
    {
        void InRange(PlaneState s)
        {
            Assert.InRange(s.Speed, FlightModel.MinSpeed, FlightModel.MaxSpeed);
            Assert.InRange(s.Velocity.Length(), s.Speed - 0.01f, s.Speed + 0.01f);
        }

        // Full-throttle dive, then a full-throttle-off climb.
        var rig = new Rig();
        rig.Fly(Aim(Vector3.Normalize(new Vector3(0f, -1f, -0.3f)), throttle: 1f), 6f, InRange);
        rig = new Rig();
        var climb = rig.Fly(Aim(Vector3.Normalize(new Vector3(0f, 1f, -0.2f)), throttle: 0f), 20f, InRange);
        Assert.InRange(climb.Speed, 25f, 26f); // slowed to the minimum, still flying

        rig = new Rig();
        uint seed = 7;
        for (int i = 0; i < 60 * 120; i++)
            InRange(rig.Step(RandomInput(ref seed, i)));
    }

    [Theory]
    [InlineData(1f, 0f, 0f)]       // right
    [InlineData(-1f, 0.3f, -0.5f)] // left, climbing
    [InlineData(0.2f, -0.2f, 1f)]  // nearly behind, a bit down
    [InlineData(0f, 0.5f, -1f)]    // ahead and up
    public void AimSteeringConvergesSmoothly(float x, float y, float z)
    {
        var aim = Vector3.Normalize(new Vector3(x, y, z));
        var rig = new Rig();
        var previous = rig.Step(Aim(aim));
        float maxTurnPerStep = (FlightModel.RollRate + FlightModel.PitchRate + FlightModel.YawRate + FlightModel.BankTurnRate) * (float)Dt;
        rig.Fly(Aim(aim), 10f, s =>
        {
            Assert.True(RotationAngle(previous.Orientation, s.Orientation) <= maxTurnPerStep + 1e-4f);
            previous = s;
        });
        Assert.True(AngleBetween(Forward(rig.Bus.Last), aim) < 2f * Deg,
            $"still {AngleBetween(Forward(rig.Bus.Last), aim) / Deg} deg off");
        // And it stays there.
        rig.Fly(Aim(aim), 2f, s => Assert.True(AngleBetween(Forward(s), aim) < 3f * Deg));
    }

    [Fact]
    public void AimBanksIntoTheTurnThenLevelsWings()
    {
        var aim = Vector3.Normalize(new Vector3(1f, 0f, -0.2f));
        var rig = new Rig();
        var s = rig.Fly(Aim(aim), 0.5f);
        Vector3 right = Vector3.Transform(Vector3.UnitX, s.Orientation);
        Assert.True(right.Y < -MathF.Sin(30f * Deg), "banked right into a right turn");

        s = rig.Fly(Aim(aim), 10f);
        right = Vector3.Transform(Vector3.UnitX, s.Orientation);
        Assert.InRange(right.Y, -0.1f, 0.1f);
    }

    [Fact]
    public void RollsAreQuickerThanPitchAndYaw()
    {
        var start = new Rig().Step(Sticks()).Orientation;
        float roll = RotationAngle(start, new Rig().Fly(Sticks(roll: 1f), 0.5f).Orientation);
        float pitch = RotationAngle(start, new Rig().Fly(Sticks(pitch: 1f), 0.5f).Orientation);
        float yaw = RotationAngle(start, new Rig().Fly(Sticks(yaw: 1f), 0.5f).Orientation);

        Assert.InRange(roll / Deg, 70f, 95f);   // about 180 deg/s
        Assert.InRange(pitch / Deg, 25f, 40f);  // about 70 deg/s
        Assert.True(roll > 2f * pitch);
        Assert.True(yaw < pitch);
    }

    [Fact]
    public void SticksMoveTheNoseTheRightWay()
    {
        Assert.True(Forward(new Rig().Fly(Sticks(pitch: 1f), 0.3f)).Y > 0.2f);
        Assert.True(Forward(new Rig().Fly(Sticks(pitch: -1f), 0.3f)).Y < -0.2f);
        Assert.True(Forward(new Rig().Fly(Sticks(yaw: 1f), 0.3f)).X > 0.1f);
        var rolled = new Rig().Fly(Sticks(roll: 1f), 0.3f);
        Assert.True(Vector3.Transform(Vector3.UnitX, rolled.Orientation).Y < -0.3f, "roll right drops the right wing");
    }

    [Fact]
    public void FloorHolds_PullsUpGentlyFromDives()
    {
        void AboveFloor(PlaneState s) => Assert.True(s.Position.Y >= FlightModel.FloorAltitude, $"at {s.Position.Y} m");

        // Stick held full forward, full throttle, for a minute.
        var rig = new Rig();
        float lowest = float.MaxValue;
        rig.Fly(Sticks(pitch: -1f, throttle: 1f), 60f, s => { AboveFloor(s); lowest = MathF.Min(lowest, s.Position.Y); });
        Assert.True(lowest > FlightModel.FloorAltitude + 1f, "pulled up before touching the backstop");

        // Mouse aimed straight down.
        rig = new Rig();
        rig.Fly(Aim(-Vector3.UnitY, throttle: 1f), 60f, AboveFloor);

        // Inverted and pulling: a split-S into the ground.
        rig = new Rig();
        rig.Fly(Sticks(roll: 1f), 1f, AboveFloor);
        rig.Fly(Sticks(pitch: 1f, throttle: 1f), 60f, AboveFloor);

        uint seed = 3;
        rig = new Rig();
        for (int i = 0; i < 60 * 120; i++)
            AboveFloor(rig.Step(RandomInput(ref seed, i)));
    }

    [Fact]
    public void BorderHolds_TurnsBackAtTheEdge()
    {
        void Inside(PlaneState s)
        {
            Assert.InRange(s.Position.X, -FlightModel.AreaHalfSize, FlightModel.AreaHalfSize);
            Assert.InRange(s.Position.Z, -FlightModel.AreaHalfSize, FlightModel.AreaHalfSize);
        }

        // Hands off at full throttle for five minutes: it would leave the area in 40 s.
        var rig = new Rig();
        float farthest = 0f;
        rig.Fly(Sticks(throttle: 1f), 300f, s =>
        {
            Inside(s);
            farthest = MathF.Max(farthest, MathF.Max(MathF.Abs(s.Position.X), MathF.Abs(s.Position.Z)));
        });
        Assert.True(farthest > 1500f, "flew out to the edge");
        Assert.True(farthest < FlightModel.AreaHalfSize - 1f, "turned back before the backstop");

        // Mouse aimed out of the area, diagonally.
        rig = new Rig();
        rig.Fly(Aim(Vector3.Normalize(new Vector3(1f, 0f, 1f)), throttle: 1f), 300f, Inside);
    }

    [Fact]
    public void Deterministic_SameInputsSameResult()
    {
        var a = new Rig();
        var b = new Rig();
        uint seedA = 42, seedB = 42;
        for (int i = 0; i < 60 * 60; i++)
        {
            var sa = a.Step(RandomInput(ref seedA, i));
            var sb = b.Step(RandomInput(ref seedB, i));
            Assert.Equal(sa.Position, sb.Position);
            Assert.Equal(sa.Orientation, sb.Orientation);
            Assert.Equal(sa.Velocity, sb.Velocity);
            Assert.Equal(sa.Speed, sb.Speed);
            Assert.Equal(sa.Throttle, sb.Throttle);
        }
    }

    [Fact]
    public void IgnoresInputForOtherPlanes()
    {
        var rig = new Rig();
        var s = rig.Fly(new ControlInput(1, 1f, 1f, 1f, 1f, false, false, Vector3.Zero), 2f);
        Assert.True(AngleBetween(Forward(s), -Vector3.UnitZ) < 0.01f);
        Assert.InRange(s.Speed, 41f, 43f);
    }

    [Fact]
    public void TickDoesNotAllocate()
    {
        var rig = new Rig();
        var input = Aim(Vector3.Normalize(new Vector3(1f, 0.2f, 0f)));
        rig.Fly(input, 1f); // warm up (JIT)
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 600; i++)
            rig.Step(input);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }
}
