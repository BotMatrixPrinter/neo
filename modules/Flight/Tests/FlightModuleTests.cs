using System;
using System.Numerics;
using Neo.Collisions;
using Neo.Damage;
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
            Tick();
            return Bus.States[0];
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
            return Bus.States[0];
        }

        public void Tick() => Module.Tick(new TickContext(++_tick, Dt));

        /// <summary>Ticks <paramref name="seconds"/> with no new input, checking each tick.</summary>
        public void Run(float seconds, Action? check = null)
        {
            int steps = (int)MathF.Round(seconds * 60f);
            for (int i = 0; i < steps; i++)
            {
                Tick();
                check?.Invoke();
            }
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
    public void TeamsHelper_Ids0To3AreAllies_4To7Central()
    {
        for (int id = 0; id < 4; id++)
            Assert.True(Teams.IsAllies(id));
        for (int id = 4; id < 8; id++)
            Assert.False(Teams.IsAllies(id));
        Assert.False(Teams.IsAllies(-1));
        Assert.False(Teams.IsAllies(8));
        Assert.Equal(8, Teams.PlaneCount);
    }

    [Fact]
    public void PlaneState_OldConstructorMeansAlive()
    {
        Assert.True(new PlaneState(3, Vector3.Zero, Quaternion.Identity, Vector3.Zero, 0f, 0f).Alive);
        Assert.False(new PlaneState(3, Vector3.Zero, Quaternion.Identity, Vector3.Zero, 0f, 0f, false).Alive);
        Assert.Equal(3, new PlaneState(3, Vector3.Zero, Quaternion.Identity, Vector3.Zero, 0f, 0f, false).PlaneId);
    }

    [Fact]
    public void AllEightPlanesStartByTeam_AlliesSouthHeadingNorth_CentralNorthHeadingSouth()
    {
        var rig = new Rig();
        rig.Tick();

        Assert.Equal(8, rig.Bus.StatesPublished);
        for (int id = 0; id < 8; id++)
        {
            var s = rig.Bus.States[id];
            bool allies = id < 4;
            Assert.Equal(id, s.PlaneId);
            Assert.True(s.Alive);
            Assert.InRange(s.Position.Y, 399.9f, 400.1f);
            // About 1500 m south (+Z) for Allies, north (-Z) for Central, one step flown.
            Assert.InRange(s.Position.Z * (allies ? 1f : -1f), 1498f, 1500f);
            Assert.True(AngleBetween(Forward(s), allies ? -Vector3.UnitZ : Vector3.UnitZ) < 0.01f);
            Assert.InRange(s.Speed, 41f, 43f);
            Assert.InRange(s.Velocity.Length(), s.Speed - 0.01f, s.Speed + 0.01f);
            Assert.True(Vector3.Transform(Vector3.UnitY, s.Orientation).Y > 0.999f, "level");
        }
        // Side by side, about 60 m apart, around the center line.
        for (int team = 0; team < 8; team += 4)
        {
            float sumX = 0f;
            for (int i = 0; i < 4; i++)
            {
                sumX += rig.Bus.States[team + i].Position.X;
                if (i > 0)
                    Assert.InRange(rig.Bus.States[team + i].Position.X - rig.Bus.States[team + i - 1].Position.X, 59f, 61f);
            }
            Assert.InRange(sumX / 4f, -1f, 1f);
        }
    }

    [Fact]
    public void PublishesAllPlanesEveryTickInIdOrderAfterTheStep()
    {
        var rig = new Rig();
        var first = rig.Step(Sticks());
        rig.Bus.ClearOrder();
        var second = rig.Step(Sticks());
        Assert.Equal(16, rig.Bus.StatesPublished);
        for (int i = 0; i < 8; i++)
            Assert.Equal(i, rig.Bus.Order[i]);
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
            Assert.InRange(s.Speed, FlightModel.MinSpeed, FlightModel.DiveMaxSpeed);
            Assert.InRange(s.Velocity.Length(), s.Speed - 0.01f, s.Speed + 0.01f);
        }

        // Full-throttle dive, then a full-throttle-off climb.
        var rig = new Rig();
        rig.Fly(Aim(Vector3.Normalize(new Vector3(0f, -1f, -0.3f)), throttle: 1f), 6f, InRange);
        rig = new Rig();
        var climb = rig.Fly(Aim(Vector3.Normalize(new Vector3(0f, 1f, -0.2f)), throttle: 0f), 20f, InRange);
        Assert.InRange(climb.Speed, 25f, 26f); // slowed to the minimum, still flying

        // Random flying: whenever it flies into the ground it respawns (as Damage would); in flight the speed stays in range.
        rig = new Rig();
        uint seed = 7;
        int flying = 0;
        for (int i = 0; i < 60 * 120; i++)
        {
            var s = rig.Step(RandomInput(ref seed, i));
            if (s.Alive)
            {
                InRange(s);
                flying++;
            }
            else
                rig.Bus.Publish(new PlaneRespawn(0));
        }
        Assert.True(flying > 60 * 60, $"flew {flying} steps");
    }

    /// <summary>Plane 0 and plane 1 (side by side) fly the same aim for <paramref name="seconds"/>.</summary>
    private static void FlyPair(Rig rig, Vector3 aim, float throttle, float seconds, Action<PlaneState, PlaneState>? check = null)
    {
        for (int i = 0; i < (int)MathF.Round(seconds * 60f); i++)
        {
            rig.Bus.Publish(Aim(aim, throttle));
            rig.Bus.Publish(new ControlInput(1, 0f, 0f, 0f, throttle, false, true, aim));
            rig.Tick();
            check?.Invoke(rig.Bus.States[0], rig.Bus.States[1]);
        }
    }

    private static readonly Vector3 SteepUp = Vector3.Normalize(new Vector3(0f, MathF.Sin(60f * Deg), -MathF.Cos(60f * Deg)));
    private static readonly Vector3 Level = -Vector3.UnitZ;

    [Fact]
    public void Gravity_FullThrottleClimbLosesSpeedDownToALimit_ThenTheNoseDrops()
    {
        Assert.InRange(FlightModel.Gravity * FlightModel.GravityScale, 9.81f * 0.6f, 9.81f);
        var rig = new Rig();
        Assert.InRange(rig.Fly(Aim(Level, throttle: 1f), 20f).Speed, 59f, 60f);

        // Aimed 60 degrees up at full throttle: the speed only bleeds, toward the minimum, never below.
        float previous = rig.Bus.States[0].Speed, steepest = 0f;
        var s = rig.Fly(Aim(SteepUp, throttle: 1f), 3f, p =>
        {
            Assert.True(p.Speed <= previous + 1e-4f, $"climbing gains no speed: {previous} -> {p.Speed}");
            previous = p.Speed;
            steepest = MathF.Max(steepest, Climb(p));
        });
        Assert.True(steepest > 50f * Deg, $"it did climb steeply ({steepest / Deg} deg)");
        Assert.True(s.Speed < 55f, $"slowed in 3 s: {s.Speed}");
        float lowest = float.MaxValue;
        s = rig.Fly(Aim(SteepUp, throttle: 1f), 20f, p =>
        {
            Assert.True(p.Speed >= FlightModel.MinSpeed, "no stall");
            Assert.InRange(p.Velocity.Length(), p.Speed - 0.01f, p.Speed + 0.01f);
            
            lowest = MathF.Min(lowest, p.Speed);
        });
        Assert.InRange(lowest, FlightModel.MinSpeed, FlightModel.MinSpeed + 5f);
        // Too slow to hold it: the climb was eased down to one the engine can keep up, near the minimum speed.
        Assert.True(Climb(s) < 25f * Deg, $"still climbing at {Climb(s) / Deg} deg");
        Assert.InRange(s.Speed, FlightModel.MinSpeed, FlightModel.MinSpeed + 6f);

        // A gentle climb at full throttle keeps most of the speed; a steep one at cruise loses it faster.
        rig = new Rig();
        rig.Fly(Aim(Level, throttle: 1f), 20f);
        s = rig.Fly(Aim(Vector3.Normalize(new Vector3(0f, MathF.Sin(10f * Deg), -MathF.Cos(10f * Deg))), throttle: 1f), 10f);
        Assert.InRange(s.Speed, 52f, 60f);
    }

    [Fact]
    public void Gravity_SlowPlaneCannotHoldASteepClimb_NoseDropsGentlyUntilItHasSpeed()
    {
        foreach (bool aim in new[] { false, true })
        {
            // Idle throttle, full up-stick or aimed straight up: at the minimum speed the nose comes down to level.
            var rig = new Rig();
            rig.Fly(Sticks(throttle: 1f), 20f);
            var input = aim ? Aim(Vector3.Normalize(new Vector3(0f, 1f, -0.05f)), throttle: 0f) : Sticks(pitch: 1f, throttle: 0f);
            float previous = Climb(rig.Bus.States[0]), steepest = 0f;
            var s = rig.Fly(input, 15f, p =>
            {
                steepest = MathF.Max(steepest, Climb(p));
                Assert.True(p.Speed >= FlightModel.MinSpeed, "no stall");
                // Gently: never faster than the pitch rate, no snap.
                Assert.True(MathF.Abs(Climb(p) - previous) * 60f < FlightModel.PitchRate * 1.05f, "eased, not snapped");
                previous = Climb(p);
                
            });
            Assert.True(steepest > 40f * Deg, $"aim {aim}: it pulled up steeply first ({steepest / Deg} deg)");
            Assert.InRange(s.Speed, FlightModel.MinSpeed, FlightModel.MinSpeed + 0.5f);
            Assert.True(Climb(s) < 3f * Deg, $"aim {aim}: still climbing at {Climb(s) / Deg} deg, {s.Speed} m/s");
            Assert.True(Climb(s) > -10f * Deg, $"aim {aim}: the nose only drops gently ({Climb(s) / Deg} deg)");

            // Throttle up: speed comes back, and with it the climb.
            input = aim ? Aim(Vector3.Normalize(new Vector3(0f, 1f, -0.05f)), throttle: 1f) : Sticks(pitch: 1f, throttle: 1f);
            float highest = 0f;
            rig.Fly(input, 6f, p => highest = MathF.Max(highest, Climb(p)));
            Assert.True(highest > 10f * Deg, $"aim {aim}: climbs again with speed ({highest / Deg} deg)");
        }
    }

    /// <summary>Aims level (or <paramref name="rise"/> up: 1 is 45 degrees), the heading turning slowly
    /// (6 deg/s) from north: a wide circle.</summary>
    private static PlaneState FlyLevelCircle(Rig rig, float throttle, float seconds, ref float t, float rise = 0f, Action<PlaneState>? check = null)
    {
        for (int i = 0; i < (int)MathF.Round(seconds * 60f); i++, t += (float)Dt)
        {
            var s = rig.Step(Aim(Vector3.Normalize(new Vector3(MathF.Sin(6f * Deg * t), rise, -MathF.Cos(6f * Deg * t))), throttle));
            check?.Invoke(s);
        }
        return rig.Bus.States[0];
    }

    [Fact]
    public void Gravity_DiveBuildsSpeedPastTheMaxUpToTheCap_LevelFlightSettlesBackAtTheThrottleSpeed()
    {
        Assert.InRange(FlightModel.DiveMaxSpeed, 80f, 90f);
        var rig = ClimbTo(15f, 900f, out _);
        float t0 = 0f;
        FlyLevelCircle(rig, 1f, 30f, ref t0); // speed back (the engine is weaker up here), clear of the border
        float top = 0f, previous = rig.Bus.States[0].Speed;
        Vector3 ahead = Forward(rig.Bus.States[0]) * new Vector3(1f, 0f, 1f);
        // (High up the engine's level max is lower, so drag starts earlier: the dive needs a little longer.)
        var s = rig.Fly(Aim(Vector3.Normalize(ahead * 0.1f - Vector3.UnitY), throttle: 1f), 8f, p =>
        {
            Assert.True(p.Speed <= FlightModel.DiveMaxSpeed, $"capped: {p.Speed}");
            Assert.InRange(p.Velocity.Length(), p.Speed - 0.01f, p.Speed + 0.01f);
            Assert.True(p.Alive && Clearance(p) > 0f, "still in the air");
            if (Climb(p) < -30f * Deg)
                Assert.True(p.Speed >= previous - 1e-4f, "diving loses no speed");
            previous = p.Speed;
            top = MathF.Max(top, p.Speed);
        });
        Assert.True(top > FlightModel.MaxSpeed + 20f, $"dived to {top} m/s");
        Assert.True(top >= FlightModel.DiveMaxSpeed - 0.5f, $"reaches the cap: {top} m/s");

        // Level again (a wide level circle, clear of the border): drag brings it back to the throttle's speed, gradually.
        float t = 0f;
        s = FlyLevelCircle(rig, 1f, 2f, ref t);
        Assert.True(s.Speed > FlightModel.MaxSpeed + 5f, $"keeps its dive speed a while: {s.Speed}");
        s = FlyLevelCircle(rig, 1f, 30f, ref t);
        Assert.InRange(s.Speed, FlightModel.MaxSpeed - 0.5f, FlightModel.MaxSpeed + 0.5f);
        s = FlyLevelCircle(rig, 0.5f, 20f, ref t);
        Assert.InRange(s.Speed, FlightModel.CruiseSpeed - 0.5f, FlightModel.CruiseSpeed + 0.5f);

        // A dive at cruise throttle speeds up too, but less.
        rig = ClimbTo(15f, 900f, out _);
        rig.Fly(Aim(Level, throttle: 0.5f), 20f);
        top = 0f;
        rig.Fly(Aim(Vector3.Normalize(new Vector3(0f, -1f, -1f)), throttle: 0.5f), 5f, p => top = MathF.Max(top, p.Speed));
        Assert.InRange(top, FlightModel.CruiseSpeed + 5f, FlightModel.DiveMaxSpeed - 5f);
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
        rig.Fly(Sticks(), 10f); // away from the start near the south border, so turning back stays clear of it
        var previous = rig.Step(Aim(aim));
        float maxTurnPerStep = (FlightModel.RollRate + FlightModel.PitchRate + FlightModel.YawRate + FlightModel.BankTurnRate) * (float)Dt;
        rig.Fly(Aim(aim), 10f, s =>
        {
            Assert.True(RotationAngle(previous.Orientation, s.Orientation) <= maxTurnPerStep + 1e-4f);
            previous = s;
        });
        Assert.True(AngleBetween(Forward(rig.Bus.States[0]), aim) < 2f * Deg,
            $"still {AngleBetween(Forward(rig.Bus.States[0]), aim) / Deg} deg off");
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

    private static float Bank(in PlaneState s)
    {
        Vector3 right = Vector3.Transform(Vector3.UnitX, s.Orientation);
        Vector3 up = Vector3.Transform(Vector3.UnitY, s.Orientation);
        return MathF.Atan2(-right.Y, up.Y); // positive: rolled right
    }

    private static float Climb(in PlaneState s) => MathF.Asin(Math.Clamp(Forward(s).Y, -1f, 1f));

    private static float Heading(in PlaneState s)
    {
        Vector3 f = Forward(s);
        return MathF.Atan2(f.X, -f.Z);
    }

    /// <summary>Holds <paramref name="input"/> until <paramref name="until"/> is true (at most 5 s).</summary>
    private static PlaneState FlyUntil(Rig rig, ControlInput input, Func<PlaneState, bool> until)
    {
        for (int i = 0; i < 300; i++)
        {
            var s = rig.Step(input);
            if (until(s))
                return s;
        }
        throw new InvalidOperationException("never reached");
    }

    [Fact]
    public void HandsOff_BankedPlaneRollsWingsLevelSmoothlyIn2To3Seconds()
    {
        foreach (float side in new[] { 1f, -1f })
        {
            var rig = new Rig();
            FlyUntil(rig, Sticks(roll: side), s => MathF.Abs(Bank(s)) > 34f * Deg);
            float peak = 0f, previous = 0f, settledAt = float.NaN, t = 0f;
            bool falling = false;
            rig.Fly(Sticks(), 5f, s =>
            {
                t += (float)Dt;
                float bank = Bank(s) * side;
                Assert.True(bank > -0.5f * Deg, $"overshot level: {bank / Deg} deg");
                if (falling)
                    Assert.True(bank <= previous + 0.01f * Deg, $"wobbled: {previous / Deg} -> {bank / Deg} deg");
                else if (bank < previous)
                    falling = true;
                peak = MathF.Max(peak, bank);
                previous = bank;
                if (float.IsNaN(settledAt) && falling && bank < 2f * Deg)
                    settledAt = t;
            });
            Assert.InRange(peak / Deg, 40f, 52f); // about a 45 degree bank
            Assert.InRange(settledAt, 1.5f, 3f);
            Assert.True(MathF.Abs(Bank(rig.Bus.States[0])) < 0.5f * Deg);
            Assert.True(MathF.Abs(Climb(rig.Bus.States[0])) < 1f * Deg);
            // Level now: no more turning, the heading holds.
            float heading = Heading(rig.Bus.States[0]);
            rig.Fly(Sticks(), 3f);
            Assert.True(MathF.Abs(Heading(rig.Bus.States[0]) - heading) < 0.5f * Deg);
        }
    }

    [Fact]
    public void HandsOff_ClimbOrDiveKeepsItsPitchAngleAndHeading()
    {
        foreach (float side in new[] { 1f, -1f })
        {
            var rig = new Rig();
            var released = FlyUntil(rig, Sticks(pitch: side), s => MathF.Abs(Climb(s)) > 25f * Deg);
            float held = Climb(released), heading = Heading(released), t = 0f;
            rig.Fly(Sticks(), 8f, s =>
            {
                t += (float)Dt;
                float off = MathF.Abs(Climb(s) - held);
                // Only the stick's lag nudges it right after the release; then it holds within a degree.
                Assert.True(off < (t < 1.5f ? 6f : 1f) * Deg, $"at {t} s: climb {Climb(s) / Deg} deg, held {held / Deg} deg");
                Assert.True(MathF.Abs(Heading(s) - heading) < 0.5f * Deg, "heading kept");
                Assert.True(MathF.Abs(Bank(s)) < 0.5f * Deg, "wings stay level");
            });
            Assert.True(MathF.Abs(Climb(rig.Bus.States[0]) - held) < 0.5f * Deg);
            Assert.True(MathF.Abs(held) > 25f * Deg, "still climbing or diving, not pulled to the horizon");
        }
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(-1f)]
    public void HandsOff_BankedClimbOrDiveLevelsItsWingsButKeepsItsPitchAngle(float side)
    {
        var rig = new Rig();
        // A 20 degree climb or dive, then a 45 degree bank: let go.
        FlyUntil(rig, Sticks(pitch: side), s => MathF.Abs(Climb(s)) > 17f * Deg);
        rig.Fly(Sticks(), 2f);
        var released = FlyUntil(rig, Sticks(roll: 1f), s => Bank(s) > 45f * Deg);
        float held = Climb(released);
        Assert.InRange(held * side / Deg, 16f, 22f);
        var end = rig.Fly(Sticks(), 5f, s =>
        {
            Assert.True(MathF.Abs(Climb(s) - held) < 1.5f * Deg, $"climb {Climb(s) / Deg} deg, held {held / Deg} deg");
            Assert.True(Bank(s) > -0.5f * Deg, "no overshoot past wings level");
        });
        Assert.True(MathF.Abs(Bank(end)) < 0.5f * Deg, "wings level");
        Assert.True(MathF.Abs(Climb(end) - held) < 1f * Deg, "pitch angle kept");
    }

    [Fact]
    public void HandsOff_WaitsAShortMomentAndCountsTinySticksAsHandsOff()
    {
        var rig = new Rig();
        FlyUntil(rig, Sticks(roll: 1f), s => Bank(s) > 40f * Deg);
        float bank = Bank(rig.Bus.States[0]);
        // For the first 0.2 s after letting go nothing rolls the wings back (they even roll on a bit with the stick's lag).
        Assert.True(Bank(rig.Fly(Sticks(), 0.2f)) >= bank);
        // Sticks inside the dead zone are hands off: it levels its wings and keeps its climb.
        float climb = Climb(rig.Bus.States[0]);
        var s = rig.Fly(Sticks(pitch: 0.03f, roll: -0.04f, yaw: 0.04f), 4f);
        Assert.True(MathF.Abs(Bank(s)) < 1f * Deg);
        Assert.True(MathF.Abs(Climb(s) - climb) < 1f * Deg);
    }

    [Fact]
    public void AnyInputOverridesLevellingAtOnce_ThenItResumes()
    {
        var rig = new Rig();
        FlyUntil(rig, Sticks(roll: 1f), s => Bank(s) > 40f * Deg);
        float bank = Bank(rig.Fly(Sticks(), 0.5f)); // levelling under way
        Assert.True(bank < 40f * Deg);

        // Rolling further right: the stick wins at the usual roll rate.
        var s = rig.Fly(Sticks(roll: 1f), 0.3f);
        Assert.True(Bank(s) > bank + 30f * Deg, $"{bank / Deg} -> {Bank(s) / Deg} deg");

        // A small stick just outside the dead zone holds the plane off level for as long as it is held.
        rig.Fly(Sticks(pitch: 0.06f), 3f, p => Assert.True(Bank(p) > bank, "no levelling while a stick is held"));

        // Let go: it levels its wings again, keeping the climb it had at the release.
        float climb = Climb(rig.Bus.States[0]);
        s = rig.Fly(Sticks(), 4f);
        Assert.True(MathF.Abs(Bank(s)) < 1f * Deg);
        Assert.True(MathF.Abs(Climb(s) - climb) < 2f * Deg, $"climb {climb / Deg} -> {Climb(s) / Deg} deg");

        // Holding a climb hands off, the pitch stick takes over at once (and the nose is free to go anywhere).
        rig = new Rig();
        FlyUntil(rig, Sticks(pitch: 1f), p => Climb(p) > 20f * Deg);
        float held = Climb(rig.Fly(Sticks(), 3f));
        s = rig.Fly(Sticks(pitch: -1f), 0.5f);
        Assert.True(Climb(s) < held - 15f * Deg, $"{held / Deg} -> {Climb(s) / Deg} deg");
        s = rig.Fly(Sticks(pitch: 1f), 1f);
        Assert.True(Climb(s) > held + 20f * Deg, $"{held / Deg} -> {Climb(s) / Deg} deg");

        // Mouse aim is never levelled: it keeps a climb aimed at.
        rig = new Rig();
        var aim = Vector3.Normalize(new Vector3(0f, 0.5f, -1f));
        s = rig.Fly(Aim(aim), 8f);
        Assert.True(AngleBetween(Forward(s), aim) < 3f * Deg);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(-1f)]
    public void HandsOff_InvertedRightsItselfTheShorterWay(float side)
    {
        var rig = new Rig();
        // Rolled past 135 degrees: the shorter way back is the way it came.
        FlyUntil(rig, Sticks(roll: side), s => Bank(s) * side > 135f * Deg);
        float previous = Bank(rig.Bus.States[0]);
        rig.Fly(Sticks(), 6f, s =>
        {
            float bank = Bank(s);
            Assert.True(MathF.Abs(bank - previous) < 10f * Deg, "never rolls through inverted the long way");
            Assert.True(bank * side > -0.5f * Deg, "no overshoot past wings level");
            previous = bank;
        });
        var end = rig.Bus.States[0];
        Assert.True(Vector3.Transform(Vector3.UnitY, end.Orientation).Y > 0.999f, "upright");
        Assert.True(MathF.Abs(Climb(end)) < 1f * Deg);
        Assert.True(end.Alive);
    }

    /// <summary>Height (m) of plane <paramref name="s"/>'s lowest point above World's terrain under it.</summary>
    private static float Clearance(in PlaneState s) =>
        s.Position.Y - FlightModel.CrashClearance - Neo.World.Terrain.HeightAt(s.Position.X, s.Position.Z);

    /// <summary>
    /// Ticks with <paramref name="input"/> for plane <paramref name="input"/>.PlaneId until it is down (at most
    /// 60 s), checking that it was alive and above the ground until then; returns its last state.
    /// </summary>
    private static PlaneState FlyUntilCrash(Rig rig, Func<PlaneState, ControlInput> input)
    {
        if (rig.Bus.StatesPublished == 0)
            rig.Tick();
        int id = input(rig.Bus.States[0]).PlaneId;
        var previous = rig.Bus.States[id];
        for (int i = 0; i < 60 * 60; i++)
        {
            rig.Bus.Publish(input(rig.Bus.States[id]));
            rig.Tick();
            var s = rig.Bus.States[id];
            if (!s.Alive)
            {
                Assert.True(Clearance(previous) > 0f, "flew until its lowest point reached the ground");
                return s;
            }
            Assert.Equal(0, rig.Bus.CrashCount);
            Assert.True(Clearance(s) > 0f, $"alive below ground at {s.Position}");
            previous = s;
        }
        throw new InvalidOperationException("never reached the ground");
    }

    /// <summary>Plane <paramref name="id"/> has just crashed at <paramref name="s"/>: one PlaneCrashed, at rest on the terrain.</summary>
    private static void AssertCrashedOnTheGround(Rig rig, int id, PlaneState s, float minSpeed)
    {
        Assert.False(s.Alive);
        Assert.Equal(Neo.World.Terrain.HeightAt(s.Position.X, s.Position.Z), s.Position.Y);
        Assert.Equal(Vector3.Zero, s.Velocity);
        Assert.Equal(1, rig.Bus.CrashCount);
        var crash = rig.Bus.Crashes[0];
        Assert.Equal(id, crash.PlaneId);
        Assert.Equal(s.Position, crash.Position);
        Assert.True(crash.Speed >= minSpeed, $"hit at {crash.Speed} m/s");
        Assert.True(rig.Module.Plane(id).Grounded);

        // It stays there, down, and crashes only once.
        rig.Run(3f, () =>
        {
            var p = rig.Bus.States[id];
            Assert.False(p.Alive);
            Assert.Equal(s.Position, p.Position);
            Assert.Equal(Vector3.Zero, p.Velocity);
        });
        Assert.Equal(1, rig.Bus.CrashCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void Crash_DivingPlaneHitsTheGroundAtTheTerrainHeight_OnePlaneCrashed(int id)
    {
        // Stick held full forward, full throttle, player and bot: no pull-up, it flies into the ground.
        var rig = new Rig();
        var s = FlyUntilCrash(rig, _ => new ControlInput(id, -1f, 0f, 0f, 1f, false, false, Vector3.Zero));
        AssertCrashedOnTheGround(rig, id, s, 40f);
        for (int other = 0; other < Teams.PlaneCount; other++)
            Assert.Equal(other != id, rig.Bus.States[other].Alive);
    }

    [Fact]
    public void Crash_IntoAHillsideStopsAtTheHillsHeight()
    {
        // Mouse aimed at the top of World's mountain north-west of the Allies' start: it hits the slope.
        var peak = new Vector3(-850f, 0f, 950f);
        peak.Y = Neo.World.Terrain.HeightAt(peak.X, peak.Z);
        var rig = new Rig();
        var s = FlyUntilCrash(rig, p => Aim(Vector3.Normalize(peak - p.Position), throttle: 1f));
        Assert.True(s.Position.Y > 50f, $"on the hill at {s.Position}");
        AssertCrashedOnTheGround(rig, 0, s, 30f);
    }

    [Fact]
    public void NoFloorPullUp_HandsOffSteepDiveIsKeptAllTheWayIntoTheGround()
    {
        // Steep dive (about 70 degrees nose down) from 400 m, then hands off: the dive is kept, no pull-up.
        var rig = new Rig();
        float held = Climb(FlyUntil(rig, Sticks(pitch: -1f, throttle: 1f), s => Climb(s) < -70f * Deg));
        float steepest = held;
        var previous = rig.Bus.States[0];
        while (rig.Bus.States[0].Alive)
        {
            previous = rig.Bus.States[0];
            var s = rig.Step(Sticks(throttle: 1f));
            if (s.Alive)
                Assert.True(MathF.Abs(Climb(s) - held) < 6f * Deg, $"dive kept: {held / Deg} -> {Climb(s) / Deg} deg at {s.Position.Y} m");
        }
        Assert.True(Climb(previous) < -60f * Deg, $"still diving steeply at the ground: {Climb(previous) / Deg} deg");
        AssertCrashedOnTheGround(rig, 0, rig.Bus.States[0], 60f);

        // Inverted and pulling into a dive (a split-S), then hands off, and mouse aimed straight down: both crash.
        rig = new Rig();
        rig.Fly(Sticks(roll: 1f), 1f);
        FlyUntil(rig, Sticks(pitch: 1f, throttle: 1f), p => Climb(p) < -60f * Deg);
        AssertCrashedOnTheGround(rig, 0, FlyUntilCrash(rig, _ => Sticks(throttle: 1f)), 25f);
        rig = new Rig();
        AssertCrashedOnTheGround(rig, 0, FlyUntilCrash(rig, _ => Aim(-Vector3.UnitY, throttle: 1f)), 60f);
    }

    [Fact]
    public void FlyingLowAboveTheGroundIsFine()
    {
        // Plane 0 descends toward the open center (the plain), to 60 m, then to 9 m (7.5 m between its lowest
        // point and the plain, at most 5 m high there): well inside the old floor guard's 60 m.
        const float low = 9f;
        var rig = new Rig();
        rig.Tick();
        ControlInput Hold(PlaneState p, float height)
        {
            float rise = Math.Clamp((height - p.Position.Y) * 0.02f, -0.4f, 0.4f);
            return Aim(Vector3.Normalize(new Vector3(0f, rise, -1f)));
        }
        for (int i = 0; i < 60 * 60 && rig.Bus.States[0].Position.Z > 400f; i++)
            rig.Step(Hold(rig.Bus.States[0], 60f));
        for (int i = 0; i < 60 * 60 && rig.Bus.States[0].Position.Z > 0f; i++)
        {
            var p = rig.Step(Hold(rig.Bus.States[0], low));
            Assert.True(p.Alive && Clearance(p) > 2f, $"at {p.Position}");
        }
        var s = rig.Bus.States[0];
        Assert.InRange(s.Position.Y, low - 1f, low + 1f);

        // ...then flies on hands off over the center: no pull-up, no crash, it keeps its height.
        float lowest = float.MaxValue, highest = float.MinValue;
        rig.Fly(Sticks(), 6f, p =>
        {
            Assert.True(p.Alive);
            Assert.True(Clearance(p) > 2f, $"at {p.Position}");
            lowest = MathF.Min(lowest, p.Position.Y);
            highest = MathF.Max(highest, p.Position.Y);
        });
        Assert.True(highest - lowest < 2f, $"height {lowest}..{highest} m: no pull-up");
        Assert.True(highest < low + 2f, "never pulled up");
        Assert.True(rig.Bus.States[0].Position.Z < -200f, "flew on over the center");
        Assert.Equal(0, rig.Bus.CrashCount);
    }

    [Fact]
    public void Crash_RespawnRestoresThePlane_ItCanCrashAgain()
    {
        var rig = new Rig();
        FlyUntilCrash(rig, _ => Sticks(pitch: -1f, throttle: 1f));
        rig.Run(2f);
        Assert.False(rig.Bus.States[0].Alive);

        rig.Bus.Publish(new PlaneRespawn(0));
        var s = rig.Step(Sticks());
        Assert.True(s.Alive);
        Assert.False(rig.Module.Model.Grounded);
        Assert.True(Vector3.Distance(s.Position, FlightModel.StartPosition(0)) < 2f, $"at {s.Position}");
        Assert.InRange(s.Speed, FlightModel.MaxSpeed - 0.5f, FlightModel.MaxSpeed);
        s = rig.Fly(Sticks(), 5f, p => Assert.True(p.Alive));
        Assert.True(MathF.Abs(Climb(s)) < 1f * Deg, "flies on level");
        Assert.Equal(1, rig.Bus.CrashCount);

        // Into the ground again: a second PlaneCrashed.
        for (int i = 0; i < 60 * 60 && rig.Bus.States[0].Alive; i++)
            rig.Step(Aim(-Vector3.UnitY, throttle: 1f));
        Assert.Equal(2, rig.Bus.CrashCount);
        Assert.Equal(rig.Bus.States[0].Position, rig.Bus.Crashes[1].Position);
    }

    /// <summary>
    /// Climbs hands off at about <paramref name="degrees"/> until <paramref name="altitude"/> m. A climb
    /// steeper than 15 degrees bleeds speed, so it climbs at 15 degrees until 60 m below, then pulls up.
    /// </summary>
    private static Rig ClimbTo(float degrees, float altitude, out PlaneState released)
    {
        var rig = new Rig();
        if (degrees > 15f)
        {
            FlyUntil(rig, Sticks(pitch: 1f, throttle: 1f), s => Climb(s) > 15f * Deg);
            for (int i = 0; i < 60 * 120 && rig.Bus.States[0].Position.Y < altitude - 60f; i++)
                rig.Step(Sticks(throttle: 1f));
        }
        released = FlyUntil(rig, Sticks(pitch: 1f, throttle: 1f), s => Climb(s) > degrees * Deg);
        for (int i = 0; i < 60 * 60 && rig.Bus.States[0].Position.Y < altitude; i++)
            rig.Step(Sticks(throttle: 1f));
        Assert.True(rig.Bus.States[0].Position.Y >= altitude, "climbed up");
        return rig;
    }

    [Fact]
    public void AltitudePower_FallsSmoothlyWithHeight_NothingLeftAt1000m_NoLimit()
    {
        Assert.Equal(1000f, FlightModel.PowerAltitude);
        Assert.InRange(FlightModel.PowerAltitudeSpeed, 38f, 42f); // about cruise
        // Full at the ground (exactly as before: the engine's level max and its most added acceleration).
        foreach (float engine in new[] { 0f, 0.5f, 1f })
        {
            Assert.Equal(FlightModel.MaxSpeedNowFor(engine), FlightModel.LevelMaxSpeed(engine, 0f));
            Assert.Equal(FlightModel.MaxThrustAcceleration * FlightModel.MaxSpeedNowFor(engine) / FlightModel.MaxSpeed,
                FlightModel.ClimbPower(engine, 0f), 5);
        }
        // Falls smoothly and keeps falling, with no step or flat top anywhere up to 1.5 km.
        float previousLoss = -1f, previousStep = 0f;
        for (int h = 0; h <= 1500; h += 10)
        {
            float loss = FlightModel.AltitudePowerLoss(h);
            if (h > 0)
            {
                Assert.True(loss > previousLoss, $"keeps falling at {h} m");
                // Smooth: each 10 m loses a little more than the last, never a jump.
                Assert.True(loss - previousLoss >= previousStep - 1e-6f, $"smooth at {h} m");
                Assert.True(loss - previousLoss < (h <= 1000 ? 0.04f : 0.15f), $"no jump at {h} m");
                previousStep = loss - previousLoss;
            }
            previousLoss = loss;
        }
        // At 1000 m full throttle holds level at about cruise with nothing left to climb; above, not even that.
        Assert.Equal(1f, FlightModel.AltitudePowerLoss(1000f), 5);
        Assert.InRange(FlightModel.LevelMaxSpeed(1f, 1000f), 39.5f, 40.5f);
        Assert.Equal(0f, FlightModel.ClimbPower(1f, 1000f), 4);
        Assert.True(FlightModel.ClimbPower(1f, 1100f) < -0.5f);
        Assert.True(FlightModel.LevelMaxSpeed(1f, 1100f) < FlightModel.LevelMaxSpeed(1f, 1000f));
        // Engine damage lowers it on top of height.
        Assert.True(FlightModel.ClimbPower(0f, 600f) < FlightModel.ClimbPower(1f, 600f) * 0.7f);
        // No hard ceiling left in the model.
        foreach (var field in typeof(FlightModel).GetFields())
            Assert.DoesNotContain("Ceiling", field.Name);
    }

    [Fact]
    public void LowAltitudePerformanceIsUnchanged()
    {
        // Low down the engine is all there (under 1% lost at 200 m).
        Assert.True(FlightModel.LevelMaxSpeed(1f, 200f) > FlightModel.MaxSpeed * 0.997f);
        Assert.True(FlightModel.ClimbPower(1f, 200f) > FlightModel.MaxThrustAcceleration * 0.99f);
        // Down to 200 m, then full throttle level: the old 60 m/s; a 10 degree climb keeps most of it, as before.
        var rig = new Rig();
        for (int i = 0; i < 60 * 30 && rig.Step(Aim(Vector3.Normalize(new Vector3(0f, -0.35f, -1f)))).Position.Y > 230f; i++) { }
        var s = rig.Fly(Aim(Level, throttle: 1f), 25f);
        Assert.InRange(s.Position.Y, 150f, 250f);
        Assert.InRange(s.Speed, 59.5f, 60f);
        s = rig.Fly(Aim(Vector3.Normalize(new Vector3(0f, MathF.Sin(10f * Deg), -MathF.Cos(10f * Deg))), throttle: 1f), 8f);
        Assert.True(s.Position.Y < 400f);
        Assert.InRange(s.Speed, 55f, 60f);
    }

    /// <summary>Flies with the aim (full throttle) for <paramref name="seconds"/>; plane <paramref name="id"/> only.</summary>
    private static PlaneState FlyPlane(Rig rig, int id, Vector3 aim, float seconds, Action<PlaneState>? check = null)
    {
        for (int i = 0; i < (int)MathF.Round(seconds * 60f); i++)
        {
            rig.Bus.Publish(new ControlInput(id, 0f, 0f, 0f, 1f, false, true, aim));
            rig.Tick();
            check?.Invoke(rig.Bus.States[id]);
        }
        return rig.Bus.States[id];
    }

    [Theory]
    [InlineData(0)] // the player
    [InlineData(5)] // a bot
    public void FullThrottleClimb_SlowsWithHeight_AndLevelsOffAround1000mByItself(int id)
    {
        // Aimed straight up at full throttle for 5 minutes: the climb rate falls with height.
        var rig = new Rig();
        float sum400 = 0f, sum700 = 0f, sum900 = 0f, highest = 0f;
        int n400 = 0, n700 = 0, n900 = 0, steps = 0, healthyTo800 = 0;
        var end = FlyPlane(rig, id, Vector3.UnitY, 300f, p =>
        {
            float y = p.Position.Y;
            steps++;
            if (healthyTo800 == 0 && y > 800f) healthyTo800 = steps;
            highest = MathF.Max(highest, y);
            Assert.True(p.Speed >= FlightModel.MinSpeed);
            if (y > 500f && y < 600f) { sum400 += p.Velocity.Y; n400++; }
            else if (y > 750f && y < 850f) { sum700 += p.Velocity.Y; n700++; }
            else if (y > 930f && y < 970f) { sum900 += p.Velocity.Y; n900++; }
        });
        float rate500 = sum400 / n400, rate800 = sum700 / n700, rate950 = sum900 / n900;
        Assert.True(rate500 > rate800 * 1.3f && rate800 > rate950 * 2f && rate950 > 0f,
            $"climb rates {rate500} m/s at 500-600 m, {rate800} at 750-850 m, {rate950} at 930-970 m");
        // It levels off on its own around 1000 m: no clamp, just no power left to climb.
        Assert.InRange(highest, 990f, 1005f);
        Assert.InRange(end.Position.Y, 990f, 1005f);
        Assert.True(MathF.Abs(end.Velocity.Y) < 0.2f, $"still climbing at {end.Velocity.Y} m/s");

        // A dead engine climbs slower and levels off lower.
        rig = new Rig();
        rig.Bus.Publish(Health(id, engine: 0f));
        int deadTo800 = 0;
        steps = 0;
        end = FlyPlane(rig, id, Vector3.UnitY, 300f, p => { steps++; if (deadTo800 == 0 && p.Position.Y > 800f) deadTo800 = steps; });
        Assert.True(deadTo800 > healthyTo800 * 1.2f, $"to 800 m: dead engine {deadTo800 / 60} s, healthy {healthyTo800 / 60} s");
        Assert.True(end.Position.Y < 985f, $"dead engine levels off at {end.Position.Y} m");
    }

    /// <summary>Climbs at 15 degrees (full throttle) to 970 m, then flies a wide level circle at full throttle
    /// for two minutes to gather the most speed that height allows (about 41 m/s).</summary>
    private static Rig FastAt980m(out float t)
    {
        var rig = new Rig();
        var up = Aim(Vector3.Normalize(new Vector3(0f, MathF.Sin(15f * Deg), -MathF.Cos(15f * Deg))), throttle: 1f);
        for (int i = 0; i < 60 * 120 && rig.Bus.States[0].Position.Y < 970f; i++)
            rig.Step(up);
        t = 0f;
        var s = FlyLevelCircle(rig, 1f, 120f, ref t);
        Assert.InRange(s.Position.Y, 970f, 995f);
        Assert.True(s.Speed > 40f, $"{s.Speed} m/s");
        return rig;
    }

    [Fact]
    public void ZoomClimb_PassesPowerAltitudeBriefly_ThenSinksBack_NoClampNoBounce()
    {
        foreach (bool handsOff in new[] { false, true })
        {
            // Fast near the top, then a steep full-throttle zoom (aimed 45 degrees up, still circling):
            // it trades its speed for height, past 1000 m.
            var rig = FastAt980m(out float t);
            float highest = 0f;
            FlyLevelCircle(rig, 1f, 6f, ref t, rise: 1f, p => highest = MathF.Max(highest, p.Position.Y));
            Assert.True(highest > FlightModel.PowerAltitude + 30f, $"zoomed to {highest} m");

            // Then, still aiming 45 degrees up, or hands off: too little power up there, so it sinks back, smoothly.
            float previous = rig.Bus.States[0].Position.Y;
            void Sinks(PlaneState p)
            {
                if (p.Position.Y > FlightModel.PowerAltitude) // (hands off, back below, may meet the border guard)
                    Assert.True(p.Position.Y <= previous + 0.01f, $"bounced: {previous} -> {p.Position.Y} m");
                Assert.True(p.Speed >= FlightModel.MinSpeed);
                previous = p.Position.Y;
            }
            if (handsOff)
            {
                // Hands off keeps its glide: back below 1000 m.
                var end = rig.Fly(Sticks(throttle: 1f), 40f, Sinks);
                Assert.True(end.Position.Y < FlightModel.PowerAltitude, $"hands off at {end.Position.Y} m");
                continue;
            }
            var s = FlyLevelCircle(rig, 1f, 40f, ref t, rise: 1f, Sinks);
            Assert.True(s.Position.Y < highest - 20f, $"at {s.Position.Y} m");
            s = FlyLevelCircle(rig, 1f, 80f, ref t, rise: 1f, Sinks);
            Assert.InRange(s.Position.Y, FlightModel.PowerAltitude - 5f, FlightModel.PowerAltitude + 3f);
        }
    }

    [Fact]
    public void LevelFlightAt1000m_HoldsAboutCruise_AnyClimbThereBleedsSpeed()
    {
        // Up to 1000 m with speed: a very gentle climb from the fast level circle.
        var rig = FastAt980m(out float t);
        while (rig.Bus.States[0].Position.Y < FlightModel.PowerAltitude - 3f)
        {
            rig.Step(Aim(new Vector3(MathF.Sin(6f * Deg * t), 0.02f, -MathF.Cos(6f * Deg * t)), 1f));
            t += (float)Dt;
        }
        // Level at full throttle: the speed holds, about cruise, and so does the height.
        float startY = rig.Bus.States[0].Position.Y, startSpeed = rig.Bus.States[0].Speed;
        Assert.InRange(startSpeed, 36f, 42f);
        var s = FlyLevelCircle(rig, 1f, 30f, ref t);
        Assert.InRange(s.Speed, 36f, 42f);
        Assert.InRange(s.Speed, startSpeed - 1f, startSpeed + 1f);
        Assert.InRange(s.Position.Y, startY - 3f, startY + 3f);

        // A climb there only bleeds speed, until the nose drops near the minimum: no climbing left.
        float previous = s.Speed, highest = 0f;
        for (int i = 0; i < 60 * 25; i++, t += (float)Dt)
        {
            var p = rig.Step(Aim(Vector3.Normalize(new Vector3(MathF.Sin(6f * Deg * t), 0.15f, -MathF.Cos(6f * Deg * t))), 1f));
            Assert.True(p.Speed <= previous + 1e-3f, $"climbing at 1000 m gains no speed: {previous} -> {p.Speed}");
            previous = p.Speed;
            highest = MathF.Max(highest, p.Position.Y);
        }
        s = rig.Bus.States[0];
        Assert.True(s.Speed < FlightModel.MinSpeed + 3f, $"bled to {s.Speed} m/s");
        Assert.True(Climb(s) < 3f * Deg, $"still climbing at {Climb(s) / Deg} deg");
        Assert.True(highest < startY + 60f, $"only the speed's height: {highest} m");
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
        float farthest = 0f, north = 0f;
        rig.Fly(Sticks(throttle: 1f), 300f, s =>
        {
            Inside(s);
            farthest = MathF.Max(farthest, MathF.Max(MathF.Abs(s.Position.X), MathF.Abs(s.Position.Z)));
            north = MathF.Min(north, s.Position.Z);
        });
        Assert.True(north < -1500f, "flew out to the far (north) edge");
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
            if (i == 600)
            {
                // The same damage, downing and respawn on both.
                a.Bus.Publish(new PlaneDamageState(1, 0.5f, 0.2f, 1f, 0.3f, true));
                b.Bus.Publish(new PlaneDamageState(1, 0.5f, 0.2f, 1f, 0.3f, true));
                a.Bus.Publish(new PlaneDestroyed(6, 1));
                b.Bus.Publish(new PlaneDestroyed(6, 1));
            }
            if (i == 300 || i == 900)
            {
                // The same collision bounces on both.
                var bounce = new CollisionImpulse(i == 300 ? 0 : 3, new Vector3(-12f, 3f, 8f), new Vector3(0.5f, -1f, 3f), new Vector3(-0.4f, 0.1f, 0.2f));
                a.Bus.Publish(bounce);
                b.Bus.Publish(bounce);
            }
            if (i == 1200)
            {
                a.Bus.Publish(new PlaneRespawn(6));
                b.Bus.Publish(new PlaneRespawn(6));
            }
            a.Step(RandomInput(ref seedA, i));
            b.Step(RandomInput(ref seedB, i));
            for (int id = 0; id < 8; id++)
            {
                var sa = a.Bus.States[id];
                var sb = b.Bus.States[id];
                Assert.Equal(sa.Position, sb.Position);
                Assert.Equal(sa.Orientation, sb.Orientation);
                Assert.Equal(sa.Velocity, sb.Velocity);
                Assert.Equal(sa.Speed, sb.Speed);
                Assert.Equal(sa.Throttle, sb.Throttle);
                Assert.Equal(sa.Alive, sb.Alive);
            }
        }
    }

    [Fact]
    public void FourPlanesFlyFromTheirOwnInputs()
    {
        var rig = new Rig();
        var aim = Vector3.Normalize(new Vector3(1f, 0f, 0f));
        for (int i = 0; i < 30; i++) // 0.5 s
        {
            rig.Bus.Publish(new ControlInput(0, 1f, 0f, 0f, 0.5f, false, false, Vector3.Zero)); // pull up
            rig.Bus.Publish(new ControlInput(1, 0f, 1f, 0f, 0.5f, false, false, Vector3.Zero)); // roll right
            rig.Bus.Publish(new ControlInput(4, 0f, 0f, 1f, 0.5f, false, false, Vector3.Zero)); // yaw right
            rig.Bus.Publish(new ControlInput(7, 0f, 0f, 0f, 0.5f, false, true, aim));           // aim east
            rig.Tick();
        }
        var s = rig.Bus.States;

        Assert.True(Forward(s[0]).Y > 0.3f, "plane 0 climbs");
        Assert.True(MathF.Abs(Bank(s[0])) < 1f * Deg);
        Assert.True(Bank(s[1]) > 60f * Deg, "plane 1 rolled right");
        Assert.True(MathF.Abs(Climb(s[1])) < 5f * Deg);
        // Plane 4 heads south (+Z): yawing right turns its nose west (-X).
        Assert.True(Forward(s[4]).X < -0.1f, "plane 4 yawed right");
        Assert.True(MathF.Abs(Bank(s[4])) < 1f * Deg);
        // Plane 7 heads south and aims east (+X): a left turn, banked left.
        Assert.True(Bank(s[7]) < -20f * Deg && Forward(s[7]).X > 0.05f, "plane 7 turns toward its aim");

        // Planes with no input fly on hands off, level, at cruise.
        foreach (int id in new[] { 2, 3, 5, 6 })
        {
            Assert.True(AngleBetween(Forward(s[id]), id < 4 ? -Vector3.UnitZ : Vector3.UnitZ) < 0.01f);
            Assert.InRange(s[id].Speed, 41f, 43f);
        }

        // Input for an unknown plane is ignored.
        rig.Bus.Publish(new ControlInput(8, 1f, 1f, 1f, 1f, false, false, Vector3.Zero));
        rig.Bus.Publish(new ControlInput(-1, 1f, 1f, 1f, 1f, false, false, Vector3.Zero));
        rig.Tick();
    }

    private static PlaneDamageState Health(int id, float engine = 1f, float left = 1f, float right = 1f, float tail = 1f) =>
        new(id, engine, left, right, tail, true);

    [Fact]
    public void Damage_EngineLowersMaxSpeedDownTo60Percent()
    {
        var rig = new Rig();
        rig.Bus.Publish(Health(1, engine: 0f));
        rig.Bus.Publish(Health(2, engine: 0.5f));
        float[] previous = { 0f, FlightModel.CruiseSpeed, FlightModel.CruiseSpeed };
        for (int i = 0; i < 60 * 20; i++)
        {
            for (int id = 0; id < 3; id++)
                rig.Bus.Publish(new ControlInput(id, 0f, 0f, 0f, 1f, false, false, Vector3.Zero));
            rig.Tick();
            // Above its max now: it only slows (drag), never speeds up.
            for (int id = 1; id < 3; id++)
            {
                Assert.True(rig.Bus.States[id].Speed <= MathF.Max(previous[id], FlightModel.MaxSpeedNowFor(id == 1 ? 0f : 0.5f)) + 1e-4f, $"plane {id} sped up: {previous[id]} -> {rig.Bus.States[id].Speed} at {i}");
                previous[id] = rig.Bus.States[id].Speed;
            }
        }
        Assert.InRange(rig.Bus.States[0].Speed, 57f, 60f);  // healthy: full speed
        Assert.InRange(rig.Bus.States[1].Speed, 35.5f, 36.5f); // dead engine: 60%
        Assert.InRange(rig.Bus.States[2].Speed, 47.5f, 48.5f); // half: 80%
        rig.Run(20f);
        // At the start's 400 m height loses a little (2.6% of the 1000 m loss) on top of the damage.
        float deadMax = FlightModel.LevelMaxSpeed(0f, rig.Bus.States[1].Position.Y);
        Assert.InRange(deadMax, 35.5f, 36f);
        Assert.InRange(rig.Bus.States[1].Speed, deadMax - 0.1f, deadMax + 0.05f);

        // The same climb bleeds a dead engine's speed faster; the same dive builds less, still capped.
        rig = new Rig();
        rig.Bus.Publish(Health(1, engine: 0f));
        FlyPair(rig, Level, 1f, 30f);
        FlyPair(rig, SteepUp, 1f, 4f);
        Assert.True(rig.Bus.States[0].Speed - rig.Bus.States[1].Speed > 10f,
            $"healthy {rig.Bus.States[0].Speed}, dead engine {rig.Bus.States[1].Speed} m/s");
        Assert.True(rig.Bus.States[1].Speed >= FlightModel.MinSpeed);
        FlyPair(rig, Level, 1f, 20f);
        FlyPair(rig, Vector3.Normalize(new Vector3(0f, -1f, -0.3f)), 1f, 4f, (a, b) =>
        {
            Assert.True(b.Speed <= a.Speed + 1e-3f, $"dead engine dives slower: {b.Speed} vs {a.Speed}");
            Assert.True(a.Speed <= FlightModel.DiveMaxSpeed);
        });
        Assert.True(rig.Bus.States[1].Speed > 0.6f * FlightModel.MaxSpeed + 5f, "a dive still speeds it up");
        Assert.True(rig.Bus.States[0].Speed - rig.Bus.States[1].Speed > 5f, "but less");
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(-1f)]
    public void Damage_WingDragsThePlaneIntoARollTowardItsSide_MoreTheWorseItIs(float side)
    {
        var rig = new Rig();
        // Plane 1 a little damaged, plane 2 badly, plane 3 healthy; all hands off.
        rig.Bus.Publish(side > 0 ? Health(1, right: 0.7f) : Health(1, left: 0.7f));
        rig.Bus.Publish(side > 0 ? Health(2, right: 0f) : Health(2, left: 0f));
        rig.Run(6f);
        float light = Bank(rig.Bus.States[1]) * side, heavy = Bank(rig.Bus.States[2]) * side;
        Assert.True(light > 5f * Deg, $"light damage: {light / Deg} deg");
        Assert.True(heavy > light + 10f * Deg, $"heavy damage: {heavy / Deg} deg vs {light / Deg}");
        Assert.True(MathF.Abs(Bank(rig.Bus.States[3])) < 0.5f * Deg);

        // With the stick: the plane starts to roll toward the damaged side by itself.
        rig = new Rig();
        rig.Bus.Publish(side > 0 ? Health(0, right: 0f) : Health(0, left: 0f));
        var s = rig.Fly(Sticks(pitch: 0.2f), 0.5f);
        Assert.True(Bank(s) * side > 10f * Deg);
    }

    [Fact]
    public void Damage_TailWeakensPitchAndYaw()
    {
        var start = new Rig().Step(Sticks()).Orientation;
        float Turn(ControlInput input, float tail)
        {
            var rig = new Rig();
            rig.Bus.Publish(Health(0, tail: tail));
            return RotationAngle(start, rig.Fly(input, 0.5f).Orientation);
        }

        float pitch = Turn(Sticks(pitch: 1f), 1f), pitchHalf = Turn(Sticks(pitch: 1f), 0.5f), pitchDead = Turn(Sticks(pitch: 1f), 0f);
        float yaw = Turn(Sticks(yaw: 1f), 1f), yawDead = Turn(Sticks(yaw: 1f), 0f);
        float roll = Turn(Sticks(roll: 1f), 1f), rollDead = Turn(Sticks(roll: 1f), 0f);
        Assert.True(pitchHalf < 0.85f * pitch && pitchDead < 0.6f * pitch && pitchDead < pitchHalf);
        Assert.True(yawDead < 0.6f * yaw);
        Assert.InRange(rollDead, roll - 0.01f, roll + 0.01f); // roll is the wings', not the tail's
    }

    [Fact]
    public void Destroyed_FallsNoseDownSpinningToTheGroundAndStaysThere()
    {
        var rig = new Rig();
        rig.Run(1f);
        rig.Bus.Publish(new PlaneDestroyed(2, 5));
        float previousY = rig.Bus.States[2].Position.Y, spun = 0f, previousBank = Bank(rig.Bus.States[2]);
        float lowestClimb = 0f;
        int steps = 0;
        while (!rig.Module.Plane(2).Grounded)
        {
            // The pilot's sticks do nothing any more.
            rig.Bus.Publish(new ControlInput(2, 1f, -1f, 1f, 1f, false, false, Vector3.Zero));
            rig.Tick();
            var s = rig.Bus.States[2];
            Assert.False(s.Alive);
            Assert.True(s.Position.Y <= previousY + 0.5f, "never climbs while down");
            float bank = Bank(s), delta = bank - previousBank;
            if (delta > MathF.PI) delta -= 2f * MathF.PI;
            else if (delta < -MathF.PI) delta += 2f * MathF.PI;
            Assert.True(MathF.Abs(delta) < 4f * Deg, "a slow spin");
            spun += MathF.Abs(delta);
            previousBank = bank;
            previousY = s.Position.Y;
            lowestClimb = MathF.Min(lowestClimb, Climb(s));
            Assert.True(++steps < 60 * 60, "never reached the ground");
            Assert.True(rig.Bus.States[1].Alive && rig.Bus.States[3].Alive, "only plane 2 is down");
        }
        Assert.True(steps > 60 * 3, "a fall, not a teleport");
        Assert.InRange(lowestClimb, -70f * Deg, -45f * Deg); // the nose dropped
        Assert.True(spun > 180f * Deg, "it spins");

        // On the ground (World's terrain there): one PlaneCrashed, and it stays there, still down.
        var ground = rig.Bus.States[2];
        Assert.Equal(Neo.World.Terrain.HeightAt(ground.Position.X, ground.Position.Z), ground.Position.Y);
        Assert.Equal(1, rig.Bus.CrashCount);
        Assert.Equal(2, rig.Bus.Crashes[0].PlaneId);
        Assert.Equal(ground.Position, rig.Bus.Crashes[0].Position);
        Assert.True(rig.Bus.Crashes[0].Speed > FlightModel.MinSpeed, "hit at speed");
        rig.Run(3f, () =>
        {
            var s = rig.Bus.States[2];
            Assert.False(s.Alive);
            Assert.Equal(ground.Position, s.Position);
            Assert.Equal(Vector3.Zero, s.Velocity);
        });
        Assert.Equal(1, rig.Bus.CrashCount);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    public void Respawn_StartsAgainAtItsTeamsStartPointAtFullSpeed(int id)
    {
        var rig = new Rig();
        rig.Bus.Publish(Health(id, engine: 0.2f, right: 0.1f, tail: 0.1f));
        rig.Run(2f);
        rig.Bus.Publish(new PlaneDestroyed(id, 0));
        rig.Run(5f);
        Assert.False(rig.Bus.States[id].Alive);

        rig.Bus.Publish(new PlaneRespawn(id));
        rig.Tick();
        var s = rig.Bus.States[id];
        Vector3 start = FlightModel.StartPosition(id);
        Assert.True(s.Alive);
        Assert.True(Vector3.Distance(s.Position, start) < 2f, $"at {s.Position}, start {start}");
        Assert.InRange(MathF.Abs(start.Z), 1490f, 1510f);
        Assert.Equal(id < 4, start.Z > 0f);
        Assert.True(AngleBetween(Forward(s), id < 4 ? -Vector3.UnitZ : Vector3.UnitZ) < 0.01f);
        Assert.InRange(s.Speed, FlightModel.MaxSpeed - 0.5f, FlightModel.MaxSpeed);

        // Undamaged again (Damage resets the parts too): it flies level hands off, at full max speed.
        for (int i = 0; i < 60 * 5; i++)
        {
            rig.Bus.Publish(new ControlInput(id, 0f, 0f, 0f, 1f, false, false, Vector3.Zero));
            rig.Tick();
        }
        s = rig.Bus.States[id];
        Assert.True(MathF.Abs(Bank(s)) < 0.5f * Deg);
        Assert.InRange(s.Speed, 59f, 60f);

        // It can go down again.
        rig.Bus.Publish(new PlaneDestroyed(id, 0));
        rig.Tick();
        Assert.False(rig.Bus.States[id].Alive);
    }

    private static RepairState Repair(int id, bool repairing) =>
        new(id, repairing, PlanePart.Engine, 0f, repairing ? 1 : 0);

    [Fact]
    public void Repairing_IgnoresSticksAndAim_FliesOnHandsOffKeepingItsHeading_SticksBackAtOnceAfter()
    {
        // Level flight, then repairing: full stick (and mouse aim) in every direction changes nothing.
        var rig = new Rig();
        rig.Bus.Publish(Repair(0, true));
        Assert.True(rig.Module.Model.Repairing);
        float heading = Heading(rig.Fly(Sticks(), 0.1f)), climb = Climb(rig.Bus.States[0]);
        foreach (var input in new[] { Sticks(pitch: 1f, roll: 1f, yaw: 1f), Sticks(pitch: -1f, roll: -1f, yaw: -1f),
                     Aim(Vector3.Normalize(new Vector3(1f, 0.5f, 0f))) })
        {
            rig.Fly(input, 3f, s =>
            {
                Assert.True(MathF.Abs(Heading(s) - heading) < 0.5f * Deg, $"heading {Heading(s) / Deg} deg");
                Assert.True(MathF.Abs(Climb(s) - climb) < 0.5f * Deg, $"climb {Climb(s) / Deg} deg");
                Assert.True(MathF.Abs(Bank(s)) < 0.5f * Deg, $"bank {Bank(s) / Deg} deg");
            });
        }

        // Repairing ends: the sticks act again at once.
        rig.Bus.Publish(Repair(0, false));
        Assert.False(rig.Module.Model.Repairing);
        var s = rig.Fly(Sticks(roll: 1f), 0.3f);
        Assert.True(Bank(s) > 30f * Deg, $"bank {Bank(s) / Deg} deg");

        // Banked in a climb when the repair starts, stick still held: wings level at once like hands off
        // (no 0.25 s wait), the climb is kept, the stick does nothing.
        rig = new Rig();
        FlyUntil(rig, Sticks(pitch: 1f), p => Climb(p) > 17f * Deg);
        rig.Fly(Sticks(), 2f);
        var released = FlyUntil(rig, Sticks(roll: 1f), p => Bank(p) > 45f * Deg);
        float held = Climb(released);
        rig.Bus.Publish(Repair(0, true));
        s = rig.Fly(Sticks(roll: 1f, pitch: -1f), 0.25f);
        Assert.True(Bank(s) < Bank(released), "levelling started at once");
        s = rig.Fly(Sticks(roll: 1f, pitch: -1f), 5f, p => Assert.True(MathF.Abs(Climb(p) - held) < 1.5f * Deg));
        Assert.True(MathF.Abs(Bank(s)) < 0.5f * Deg, "wings level");
        heading = Heading(s);
        s = rig.Fly(Sticks(roll: -1f, yaw: 1f), 3f);
        Assert.True(MathF.Abs(Heading(s) - heading) < 0.5f * Deg, "heading kept");

        // Another plane repairing does not touch plane 0; unknown ids are ignored.
        rig = new Rig();
        rig.Bus.Publish(Repair(1, true));
        rig.Bus.Publish(Repair(8, true));
        rig.Bus.Publish(Repair(-1, true));
        Assert.True(Climb(rig.Fly(Sticks(pitch: 1f), 0.5f)) > 15f * Deg);
        Assert.True(rig.Module.Plane(1).Repairing);
    }

    [Fact]
    public void Repairing_ThrottleStillWorks()
    {
        var rig = new Rig();
        rig.Bus.Publish(Repair(0, true));
        var s = rig.Fly(Sticks(pitch: 1f, throttle: 1f), 30f);
        Assert.InRange(s.Speed, 57f, 60.5f);
        Assert.Equal(1f, s.Throttle);
        s = rig.Fly(Sticks(pitch: -1f, throttle: 0f), 30f);
        Assert.InRange(s.Speed, 25f, 27f);
        Assert.Equal(0f, s.Throttle);
        Assert.True(MathF.Abs(Climb(s)) < 1f * Deg, "level all along");
    }

    [Fact]
    public void Repairing_BorderGuardStillActs()
    {
        // Repairing all the way to the north edge at full throttle: the border guard turns it back.
        var rig = new Rig();
        rig.Bus.Publish(Repair(0, true));
        float north = 0f, farthest = 0f;
        rig.Fly(Sticks(yaw: 1f, roll: 1f, throttle: 1f), 120f, s =>
        {
            north = MathF.Min(north, s.Position.Z);
            farthest = MathF.Max(farthest, MathF.Max(MathF.Abs(s.Position.X), MathF.Abs(s.Position.Z)));
        });
        Assert.True(north < -1500f, "flew on north to the edge");
        Assert.True(farthest < FlightModel.AreaHalfSize - 1f, "turned back before the backstop");
    }

    // Collisions: the Collisions module's CollisionImpulse, applied by every plane.

    private const float CollisionDistance = 7f; // Collisions: two spheres of 3.5 m

    /// <summary>
    /// Fake of the Collisions module's bounce for planes <paramref name="a"/> and <paramref name="b"/>: equal and
    /// opposite velocity changes along the contact normal, restitution 0.3, separation half the overlap each plus 1 cm.
    /// </summary>
    private static void Bounce(Rig rig, int a, int b, out Vector3 changeA)
    {
        FlightModel pa = rig.Module.Plane(a), pb = rig.Module.Plane(b);
        Vector3 offset = pa.Position - pb.Position;
        float distance = offset.Length();
        Vector3 n = offset / distance; // from b to a
        float closing = Vector3.Dot(pb.Velocity - pa.Velocity, n);
        changeA = n * ((1f + 0.3f) / 2f * closing);
        Vector3 separation = n * ((CollisionDistance - distance) / 2f + 0.01f);
        rig.Bus.Publish(new CollisionImpulse(a, changeA, Vector3.Zero, separation));
        rig.Bus.Publish(new CollisionImpulse(b, -changeA, Vector3.Zero, -separation));
    }

    [Fact]
    public void Collision_TwoPlanesOnACollisionCourseSeparateAfterTheirImpulses_ThenFlyOn()
    {
        var rig = new Rig();
        // Planes 0 and 4 start head-on along the same line (X = -90); move 4 a little aside, a glancing head-on.
        rig.Bus.Publish(new CollisionImpulse(4, Vector3.Zero, Vector3.Zero, new Vector3(3f, 0f, 0f)));
        Assert.Equal(-87f, rig.Module.Plane(4).Position.X, 3);
        Assert.Equal(Vector3.Zero, rig.Module.Plane(4).Knock);

        float Distance() => Vector3.Distance(rig.Module.Plane(0).Position, rig.Module.Plane(4).Position);
        int steps = 0;
        while (Distance() >= CollisionDistance)
        {
            rig.Tick();
            Assert.True(++steps < 60 * 60, "never met");
        }

        Vector3 before0 = rig.Module.Plane(0).Velocity, before4 = rig.Module.Plane(4).Velocity;
        Bounce(rig, 0, 4, out Vector3 change);
        // Moved just clear at once, and the whole velocity change is applied (along the nose to the speed, the rest as knock).
        Assert.InRange(Distance(), CollisionDistance, CollisionDistance + 0.05f);
        Assert.True(Vector3.Distance(before0 + change, rig.Module.Plane(0).Velocity) < 1e-3f);
        Assert.True(Vector3.Distance(before4 - change, rig.Module.Plane(4).Velocity) < 1e-3f);
        Vector3 n = Vector3.Normalize(rig.Module.Plane(0).Position - rig.Module.Plane(4).Position);
        Assert.True(Vector3.Dot(rig.Module.Plane(0).Velocity - rig.Module.Plane(4).Velocity, n) > 5f, "now moving apart");

        // They pass each other without touching again, then fly on their way, back in normal flight.
        float closest = Distance(), sideways = 0f;
        rig.Run(4f, () =>
        {
            closest = MathF.Min(closest, Distance());
            sideways = MathF.Max(sideways, MathF.Abs(rig.Module.Plane(0).Position.X - rig.Module.Plane(4).Position.X));
            for (int id = 0; id < 8; id++)
                Assert.InRange(rig.Bus.States[id].Speed, FlightModel.MinSpeed, FlightModel.DiveMaxSpeed);
        });
        Assert.True(closest >= CollisionDistance, $"touched again: {closest} m");
        Assert.True(sideways > CollisionDistance + 2f, $"knocked apart sideways: {sideways} m");
        Assert.True(Distance() > 50f, "flew on past each other");
        Assert.True(rig.Bus.States[0].Position.Z < rig.Bus.States[4].Position.Z, "passed");
        Assert.Equal(Vector3.Zero, rig.Module.Plane(0).Knock);
        Assert.Equal(Vector3.Zero, rig.Module.Plane(4).Knock);
        Assert.True(AngleBetween(Forward(rig.Bus.States[0]), -Vector3.UnitZ) < 0.05f, "plane 0 still heads north");
        Assert.True(AngleBetween(Forward(rig.Bus.States[4]), Vector3.UnitZ) < 0.05f, "plane 4 still heads south");
    }

    [Fact]
    public void Collision_DisplacesThePlane_ItsKnockDecaysToNormalFlightWithin2Seconds()
    {
        var rig = new Rig();
        rig.Run(1f);
        var start = rig.Bus.States[0];
        var shove = new Vector3(15f, 0f, 0f); // sideways, to the east
        rig.Bus.Publish(new CollisionImpulse(0, shove, Vector3.Zero, new Vector3(1f, 0f, 0f)));

        var plane = rig.Module.Plane(0);
        Assert.Equal(start.Position.X + 1f, plane.Position.X, 3); // moved at once
        Assert.Equal(start.Speed, plane.Speed, 3);                // nothing along the nose
        Assert.True(Vector3.Distance(start.Velocity + shove, plane.Velocity) < 1e-3f);

        // The knock fades smoothly: never grows, about a third left at 0.3 s, gone within 2 s.
        float previous = shove.Length(), gone = -1f, t = 0f;
        rig.Run(3f, () =>
        {
            t += 1f / 60f;
            float knock = plane.Knock.Length();
            Assert.True(knock <= previous, "the knock only fades");
            Assert.True(previous - knock < 2f, "smoothly, no jump");
            if (MathF.Abs(t - 0.3f) < 0.001f)
                Assert.InRange(knock, 0.3f * 15f, 0.45f * 15f);
            if (MathF.Abs(t - 1f) < 0.001f)
                Assert.True(knock < 0.1f * 15f, $"mostly gone after 1 s: {knock}");
            if (knock == 0f && gone < 0f)
                gone = t;
            previous = knock;
        });
        Assert.InRange(gone, 1f, 2f);

        // Shoved about 15 m/s x 0.3 s aside, then flies on as before: same heading, level, same speed, straight.
        var s = rig.Bus.States[0];
        Assert.InRange(s.Position.X - start.Position.X, 1f + 4f, 1f + 5f);
        Assert.True(AngleBetween(Forward(s), Forward(start)) < 0.01f);
        Assert.InRange(s.Speed, start.Speed - 0.2f, start.Speed + 0.2f);
        Assert.True(Vector3.Distance(s.Velocity, Forward(s) * s.Speed) < 1e-4f, "back to flying along the nose");
        float x = s.Position.X;
        rig.Run(2f);
        Assert.InRange(rig.Bus.States[0].Position.X, x - 0.5f, x + 0.5f);
    }

    [Fact]
    public void Collision_SpeedAlongTheNoseChangesWithinTheLimits()
    {
        // Knocked back (rammed from ahead): slower, but never below the minimum; the rest is a backward knock.
        var rig = new Rig();
        rig.Run(1f);
        var start = rig.Bus.States[0];
        var back = Forward(start) * -30f;
        rig.Bus.Publish(new CollisionImpulse(0, back, Vector3.Zero, Vector3.Zero));
        var plane = rig.Module.Plane(0);
        Assert.Equal(FlightModel.MinSpeed, plane.Speed);
        Assert.True(Vector3.Distance(start.Velocity + back, plane.Velocity) < 1e-3f);

        // A smaller push from behind: faster by exactly its part along the nose, nothing left over.
        rig = new Rig();
        rig.Run(1f);
        start = rig.Bus.States[0];
        rig.Bus.Publish(new CollisionImpulse(0, Forward(start) * 8f + new Vector3(0f, 2f, 0f), Vector3.Zero, Vector3.Zero));
        plane = rig.Module.Plane(0);
        Assert.Equal(start.Speed + 8f, plane.Speed, 3);
        Assert.True(Vector3.Distance(plane.Knock, new Vector3(0f, 2f, 0f)) < 1e-3f);

        // Rammed hard from behind at full speed: capped at the dive maximum.
        rig = new Rig();
        rig.Fly(Sticks(throttle: 1f), 10f);
        rig.Bus.Publish(new CollisionImpulse(0, Forward(rig.Bus.States[0]) * 60f, Vector3.Zero, Vector3.Zero));
        Assert.Equal(FlightModel.DiveMaxSpeed, rig.Module.Plane(0).Speed);

        // Then speed stays in range and settles back at the throttle's.
        var s = rig.Fly(Sticks(throttle: 1f), 30f, p => Assert.InRange(p.Speed, FlightModel.MinSpeed, FlightModel.DiveMaxSpeed));
        Assert.InRange(s.Speed, 57f, 60f);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(-1f)]
    public void Collision_RollKickRollsThePlane_ThenHandsOffRecovers(float side)
    {
        var rig = new Rig();
        rig.Run(1f);
        // A wingtip strike: positive roll raises the right wing (rolls left, negative bank).
        rig.Bus.Publish(new CollisionImpulse(0, Vector3.Zero, new Vector3(0f, 0f, 4f * side), Vector3.Zero));
        float most = 0f, previous = 0f, t = 0f, recovered = -1f;
        rig.Run(5f, () =>
        {
            t += 1f / 60f;
            float bank = Bank(rig.Bus.States[0]);
            Assert.True(MathF.Abs(bank - previous) < 5f * Deg, "rolls smoothly");
            previous = bank;
            most = MathF.Max(most, -bank * side);
            if (recovered < 0f && t > 0.5f && MathF.Abs(bank) < 3f * Deg)
                recovered = t;
            Assert.True(-bank * side > -5f * Deg, "no overshoot past level");
        });
        Assert.InRange(most, 25f * Deg, 70f * Deg); // a brief roll away (4 rad/s x 0.3 s = 69 degrees, less the levelling)
        Assert.InRange(recovered, 1f, 4.5f);
        var s = rig.Bus.States[0];
        Assert.True(MathF.Abs(Bank(s)) < 2f * Deg, "wings level again");
        Assert.True(MathF.Abs(Climb(s)) < 2f * Deg, "still level");
        Assert.Equal(Vector3.Zero, rig.Module.Plane(0).Kick);
    }

    [Fact]
    public void Collision_PilotInputActsOnTopOfTheKick()
    {
        // Holding roll right against a kick to the left: rolls away less, and comes back right.
        var kicked = new Rig();
        var held = new Rig();
        kicked.Run(1f);
        held.Run(1f);
        var kick = new CollisionImpulse(0, Vector3.Zero, new Vector3(0f, 0f, 4f), Vector3.Zero);
        kicked.Bus.Publish(kick);
        held.Bus.Publish(kick);
        kicked.Fly(Sticks(), 0.4f);
        var s = held.Fly(Sticks(roll: 1f), 0.4f);
        Assert.True(Bank(s) > Bank(kicked.Bus.States[0]) + 30f * Deg);
        s = held.Fly(Sticks(roll: 1f), 0.5f);
        Assert.True(Bank(s) > 0f, "the stick wins once the kick fades");

        // A pitch kick raises the nose; hands off then takes back the climb it had (level).
        var rig = new Rig();
        rig.Run(1f);
        rig.Bus.Publish(new CollisionImpulse(0, Vector3.Zero, new Vector3(1.5f, 0f, 0f), Vector3.Zero));
        float highest = 0f;
        rig.Run(1f, () => highest = MathF.Max(highest, Climb(rig.Bus.States[0])));
        Assert.True(highest > 8f * Deg, $"nose knocked up: {highest / Deg} deg");
        rig.Run(5f);
        Assert.True(MathF.Abs(Climb(rig.Bus.States[0])) < 2f * Deg, "back to level");
    }

    [Fact]
    public void Collision_KnockedIntoTheGroundCrashes_BorderStillHolds()
    {
        // Knocked down hard into the ground: no floor holds it, it crashes there once.
        var rig = new Rig();
        rig.Run(0.5f);
        rig.Bus.Publish(new CollisionImpulse(0, new Vector3(0f, -60f, 0f), new Vector3(-3f, 0f, 0f), new Vector3(0f, -400f, 0f)));
        rig.Tick();
        AssertCrashedOnTheGround(rig, 0, rig.Bus.States[0], 25f);

        // Knocked out of the area: back at the edge at once, and stays inside.
        rig = new Rig();
        rig.Run(0.5f);
        rig.Bus.Publish(new CollisionImpulse(0, new Vector3(60f, 0f, 0f), Vector3.Zero, new Vector3(5000f, 0f, 0f)));
        Assert.Equal(FlightModel.AreaHalfSize, rig.Module.Plane(0).Position.X);
        rig.Run(10f, () =>
        {
            var p = rig.Bus.States[0].Position;
            Assert.True(MathF.Abs(p.X) <= FlightModel.AreaHalfSize && MathF.Abs(p.Z) <= FlightModel.AreaHalfSize);
        });
    }

    [Fact]
    public void Collision_DownedPlanesUnknownIdsAndBadValuesAreIgnored()
    {
        var rig = new Rig();
        rig.Run(0.5f);
        rig.Bus.Publish(new PlaneDestroyed(2, 5));
        rig.Tick();
        var down = rig.Module.Plane(2).Position;
        rig.Bus.Publish(new CollisionImpulse(2, new Vector3(20f, 0f, 0f), new Vector3(0f, 0f, 4f), new Vector3(3f, 0f, 0f)));
        Assert.Equal(down, rig.Module.Plane(2).Position);
        Assert.Equal(Vector3.Zero, rig.Module.Plane(2).Knock);
        Assert.Equal(Vector3.Zero, rig.Module.Plane(2).Kick);

        var before = rig.Module.Plane(1).Position;
        rig.Bus.Publish(new CollisionImpulse(1, new Vector3(float.NaN, 0f, 0f), Vector3.Zero, new Vector3(3f, 0f, 0f)));
        Assert.Equal(before, rig.Module.Plane(1).Position);
        rig.Bus.Publish(new CollisionImpulse(8, Vector3.One, Vector3.One, Vector3.One));
        rig.Bus.Publish(new CollisionImpulse(-1, Vector3.One, Vector3.One, Vector3.One));
        rig.Tick();
        Assert.True(float.IsFinite(rig.Bus.States[1].Position.X));
    }

    [Fact]
    public void TickDoesNotAllocate()
    {
        var rig = new Rig();
        var input = Aim(Vector3.Normalize(new Vector3(1f, 0.2f, 0f)));
        rig.Fly(input, 1f); // warm up (JIT)
        var bounce = new CollisionImpulse(0, new Vector3(5f, 0f, 5f), new Vector3(0f, 0f, 2f), new Vector3(0.5f, 0f, 0f));
        rig.Bus.Publish(bounce);
        rig.Step(input);
        var dive = new ControlInput(1, 0f, 0f, 0f, 1f, false, true, -Vector3.UnitY);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 600; i++)
        {
            if (i % 120 == 0)
                rig.Bus.Publish(bounce); // collisions too
            rig.Bus.Publish(dive); // and plane 1 flies into the ground
            rig.Step(input);
        }
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        Assert.Equal(1, rig.Bus.CrashCount);
    }
}
