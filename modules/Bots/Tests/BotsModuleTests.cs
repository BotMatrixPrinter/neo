using System;
using System.Numerics;
using Neo.Flight;
using Neo.Input;
using Neo.Shell;
using Xunit;

namespace Neo.Bots.Tests;

public class BotsModuleTests
{
    private const float Dt = 1f / 60f;
    private const float Deg = MathF.PI / 180f;
    private static readonly Vector3 North = -Vector3.UnitZ;

    /// <summary>
    /// The module with a fake bus, and fake planes: bots in <see cref="Flown"/> fly with
    /// <see cref="FakeFlight"/> from the inputs the module publishes; the others fly straight.
    /// </summary>
    private sealed class Rig
    {
        public readonly BotsModule Module = new();
        public readonly FakeBus Bus = new();
        public readonly PlaneState[] States = new PlaneState[Teams.PlaneCount];
        public readonly bool[] Present = new bool[Teams.PlaneCount];
        public readonly bool[] Flown = new bool[Teams.PlaneCount];
        private readonly FakeFlight[] _flights = new FakeFlight[Teams.PlaneCount];
        private long _tick;

        public Rig()
        {
            Module.Start(Bus);
            for (int i = 0; i < _flights.Length; i++)
                _flights[i] = new FakeFlight();
        }

        public void Set(in PlaneState state, bool flown = false)
        {
            States[state.PlaneId] = state;
            Present[state.PlaneId] = true;
            Flown[state.PlaneId] = flown;
        }

        /// <summary>Publishes the fake states to the module, ticks it once, then moves the planes.</summary>
        public void Step()
        {
            Bus.Clear();
            for (int i = 0; i < States.Length; i++)
            {
                if (Present[i])
                    Module.Handle(in States[i]);
            }
            Module.Tick(new TickContext(++_tick, Dt));
            for (int i = 0; i < States.Length; i++)
            {
                if (!Present[i] || !States[i].Alive)
                    continue;
                if (Flown[i] && Bus.TryGet(i, out ControlInput input))
                    States[i] = _flights[i].Step(in States[i], in input, Dt);
                else if (!Flown[i])
                    States[i] = new PlaneState(i, States[i].Position + States[i].Velocity * Dt, States[i].Orientation,
                        States[i].Velocity, States[i].Speed, States[i].Throttle, true);
            }
        }

        public ControlInput Input(int id)
        {
            Assert.True(Bus.TryGet(id, out ControlInput input), $"no input for plane {id}");
            return input;
        }
    }

    private static Vector3 Nose(in PlaneState s) => Vector3.Transform(-Vector3.UnitZ, s.Orientation);

    private static float AngleTo(in PlaneState from, Vector3 point)
        => MathF.Acos(Math.Clamp(Vector3.Dot(Nose(from), Vector3.Normalize(point - from.Position)), -1f, 1f));

    [Fact]
    public void PublishesInputsOnlyForAliveBots()
    {
        var rig = new Rig();
        for (int id = 0; id < Teams.PlaneCount; id++)
            rig.Set(FakeFlight.Plane(id, new Vector3(id * 100f, 400f, Teams.IsAllies(id) ? 800f : -800f),
                Teams.IsAllies(id) ? North : -North, alive: id != 5 && id != 2));
        rig.Step();

        int[] expected = { 1, 3, 4, 6, 7 };
        Assert.Equal(expected.Length, rig.Bus.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            ControlInput input = rig.Bus.Inputs[i];
            Assert.Equal(expected[i], input.PlaneId);
            Assert.False(input.HasAim);
            Assert.InRange(input.Pitch, -1f, 1f);
            Assert.InRange(input.Roll, -1f, 1f);
            Assert.InRange(input.Yaw, -1f, 1f);
            Assert.InRange(input.Throttle, 0f, 1f);
        }
        Assert.False(rig.Bus.TryGet(0, out _)); // the player flies plane 0
    }

    [Fact]
    public void NoInputForPlanesNotSeenYetOrDownedLater()
    {
        var rig = new Rig();
        rig.Step();
        Assert.Equal(0, rig.Bus.Count);

        rig.Set(FakeFlight.Plane(4, new Vector3(0f, 400f, 0f), North));
        rig.Step();
        Assert.Equal(1, rig.Bus.Count);
        Assert.Equal(4, rig.Bus.Inputs[0].PlaneId);

        rig.Set(FakeFlight.Plane(4, new Vector3(0f, 400f, 0f), North, alive: false));
        rig.Step();
        Assert.Equal(0, rig.Bus.Count);
    }

    [Fact]
    public void BanksTowardATargetOnEitherSideAndPullsUpToOneAbove()
    {
        // Central bot 4 heading north; an Allies plane to its right, then left, then above.
        var right = new Rig();
        right.Set(FakeFlight.Plane(4, new Vector3(0f, 400f, 0f), North));
        right.Set(FakeFlight.Plane(1, new Vector3(500f, 400f, -100f), North));
        right.Step();
        Assert.Equal(BotMode.Attack, right.Module.Pilot(4).Mode);
        Assert.Equal(1, right.Module.Pilot(4).TargetId);
        Assert.True(right.Input(4).Roll > 0.5f, $"roll {right.Input(4).Roll}");

        var left = new Rig();
        left.Set(FakeFlight.Plane(4, new Vector3(0f, 400f, 0f), North));
        left.Set(FakeFlight.Plane(1, new Vector3(-500f, 400f, -100f), North));
        left.Step();
        Assert.True(left.Input(4).Roll < -0.5f, $"roll {left.Input(4).Roll}");

        var above = new Rig();
        above.Set(FakeFlight.Plane(4, new Vector3(0f, 400f, 0f), North));
        above.Set(FakeFlight.Plane(1, new Vector3(0f, 700f, -400f), North));
        above.Step();
        Assert.True(above.Input(4).Pitch > 0.5f, $"pitch {above.Input(4).Pitch}");
        Assert.True(MathF.Abs(above.Input(4).Roll) < 0.2f, $"roll {above.Input(4).Roll}");
    }

    [Fact]
    public void PicksTheNearestAlivePlaneOfTheOtherTeam()
    {
        var rig = new Rig();
        rig.Set(FakeFlight.Plane(1, new Vector3(0f, 400f, 0f), North));
        rig.Set(FakeFlight.Plane(2, new Vector3(50f, 400f, 0f), North)); // own team: never a target
        rig.Set(FakeFlight.Plane(5, new Vector3(0f, 400f, -900f), North));
        rig.Set(FakeFlight.Plane(6, new Vector3(0f, 400f, -300f), North, alive: false));
        rig.Set(FakeFlight.Plane(7, new Vector3(600f, 400f, -100f), North));
        rig.Step();
        Assert.Equal(7, rig.Module.Pilot(1).TargetId);
        Assert.Equal(1, rig.Module.Pilot(5).TargetId); // 900 m, plane 2 is a little farther
    }

    [Fact]
    public void FliesTowardATargetWithTheStickUntilItIsAhead()
    {
        // Target crossing 600 m to the right; the bot flies the fake stick model and must
        // swing its nose onto it within a few seconds, turning gradually (no jumps).
        var rig = new Rig();
        rig.Set(FakeFlight.Plane(4, new Vector3(0f, 400f, 0f), North), flown: true);
        rig.Set(FakeFlight.Plane(1, new Vector3(600f, 400f, 0f), Vector3.UnitX, speed: 40f));
        float start = AngleTo(rig.States[4], rig.States[1].Position);
        Quaternion previous = rig.States[4].Orientation;
        float best = start;
        for (int i = 0; i < 60 * 8; i++)
        {
            rig.Step();
            float turned = 2f * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(previous, rig.States[4].Orientation)), 0f, 1f));
            Assert.True(turned < 200f * Deg * Dt * 1.5f, $"turned {turned / Deg} deg in one step");
            previous = rig.States[4].Orientation;
            best = MathF.Min(best, AngleTo(rig.States[4], rig.States[1].Position));
        }
        Assert.True(start > 80f * Deg);
        Assert.True(best < 10f * Deg, $"closest nose angle {best / Deg} deg");
    }

    [Fact]
    public void FiresInBurstsOnlyWhenLinedUpAndInRange()
    {
        // Dead ahead at 200 m, same course: fires, in bursts (some steps on, some off).
        var close = new Rig();
        close.Set(FakeFlight.Plane(4, new Vector3(0f, 400f, 0f), North));
        close.Set(FakeFlight.Plane(1, new Vector3(0f, 400f, -200f), North));
        int on = 0, steps = 120;
        for (int i = 0; i < steps; i++)
        {
            close.Step();
            if (close.Input(4).Fire)
                on++;
        }
        Assert.True(on > steps / 4, $"fired {on} of {steps} steps");
        Assert.True(on < steps, "fires in bursts, not continuously");

        // Lined up but too far (500 m): no fire.
        var far = new Rig();
        far.Set(FakeFlight.Plane(4, new Vector3(0f, 400f, 0f), North));
        far.Set(FakeFlight.Plane(1, new Vector3(0f, 400f, -500f), North));
        // Close but 20 degrees off the nose: no fire.
        var off = new Rig();
        off.Set(FakeFlight.Plane(4, new Vector3(0f, 400f, 0f), North));
        off.Set(FakeFlight.Plane(1, new Vector3(200f * MathF.Sin(20f * Deg), 400f, -200f * MathF.Cos(20f * Deg)), North));
        for (int i = 0; i < steps; i++)
        {
            far.Step();
            off.Step();
            Assert.False(far.Input(4).Fire);
            Assert.False(off.Input(4).Fire);
        }
    }

    [Fact]
    public void LeadsACrossingTarget()
    {
        // Target 300 m ahead crossing to the right at 50 m/s: the lead point is to its right,
        // so the bot fires with its nose ahead of the target, not at it.
        var rig = new Rig();
        float lead = 300f / (BotPilot.BulletSpeed + 50f) * 50f; // ~30 m
        rig.Set(FakeFlight.Plane(4, new Vector3(0f, 400f, 0f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.Atan2(lead, 300f))));
        rig.Set(FakeFlight.Plane(1, new Vector3(0f, 400f, -300f), Vector3.UnitX));
        bool fired = false;
        var still = rig.States[1];
        for (int i = 0; i < 60; i++)
        {
            rig.States[1] = still; // keep the geometry fixed
            rig.Step();
            fired |= rig.Input(4).Fire;
        }
        Assert.True(fired, "fires at the lead point");

        // Nose straight at the crossing target (lead ~6 deg away, plus aim error): it yaws right.
        var at = new Rig();
        at.Set(FakeFlight.Plane(4, new Vector3(0f, 400f, 0f), North));
        at.Set(FakeFlight.Plane(1, new Vector3(0f, 400f, -300f), Vector3.UnitX));
        at.Step();
        Assert.True(at.Input(4).Yaw > 0.1f, $"yaw {at.Input(4).Yaw}");
    }

    [Fact]
    public void BreaksHardWhenAnEnemySitsOnItsTail()
    {
        var rig = new Rig();
        rig.Set(FakeFlight.Plane(4, new Vector3(0f, 400f, 0f), North));
        rig.Set(FakeFlight.Plane(1, new Vector3(20f, 400f, 150f), North)); // 150 m behind, nose on us
        rig.Step();
        Assert.NotEqual(BotMode.Break, rig.Module.Pilot(4).Mode); // reaction delay
        for (int i = 0; i < 60; i++)
            rig.Step();
        Assert.Equal(BotMode.Break, rig.Module.Pilot(4).Mode);
        ControlInput input = rig.Input(4);
        Assert.True(input.Roll > 0.9f, $"hard roll toward the attacker's side, roll {input.Roll}");
        Assert.False(input.Fire);
        Assert.Equal(1f, input.Throttle);

        // Once banked, it pulls hard.
        var banked = new Rig();
        banked.Set(FakeFlight.Plane(4, new Vector3(0f, 400f, 0f), FakeFlight.Facing(North) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -80f * Deg)));
        banked.Set(FakeFlight.Plane(1, new Vector3(0f, 400f, 150f), North));
        for (int i = 0; i < 60; i++)
            banked.Step();
        Assert.Equal(BotMode.Break, banked.Module.Pilot(4).Mode);
        Assert.Equal(1f, banked.Input(4).Pitch);
    }

    [Fact]
    public void DoesNotBreakForAnEnemyFarBehindOrFlyingAway()
    {
        var far = new Rig();
        far.Set(FakeFlight.Plane(4, new Vector3(0f, 400f, 0f), North));
        far.Set(FakeFlight.Plane(1, new Vector3(0f, 400f, 450f), North));
        var away = new Rig();
        away.Set(FakeFlight.Plane(4, new Vector3(0f, 400f, 0f), North));
        away.Set(FakeFlight.Plane(1, new Vector3(0f, 400f, 150f), -North));
        for (int i = 0; i < 60; i++)
        {
            far.Step();
            away.Step();
            Assert.NotEqual(BotMode.Break, far.Module.Pilot(4).Mode);
            Assert.NotEqual(BotMode.Break, away.Module.Pilot(4).Mode);
        }
    }

    [Fact]
    public void ClimbsAwayFromTheGround()
    {
        // Low and diving at a target below: it pulls up instead.
        var rig = new Rig();
        Quaternion dive = FakeFlight.Facing(North) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, -30f * Deg);
        rig.Set(FakeFlight.Plane(4, new Vector3(0f, 120f, 0f), dive), flown: true);
        rig.Set(FakeFlight.Plane(1, new Vector3(0f, 25f, -400f), North, speed: 30f));
        rig.Step();
        Assert.Equal(BotMode.Ground, rig.Module.Pilot(4).Mode);
        Assert.True(rig.Input(4).Pitch > 0.5f, $"pitch {rig.Input(4).Pitch}");

        // Flown for a while chasing that low target, it never gets near the ground.
        float lowest = float.MaxValue;
        for (int i = 0; i < 60 * 20; i++)
        {
            rig.Step();
            lowest = MathF.Min(lowest, rig.States[4].Position.Y);
        }
        Assert.True(lowest > 40f, $"lowest {lowest} m");
    }

    [Fact]
    public void TurnsBackBeforeTheEdge()
    {
        // Near the east edge heading out, chasing a target further out: it turns back.
        var rig = new Rig();
        rig.Set(FakeFlight.Plane(4, new Vector3(1500f, 400f, 0f), Vector3.UnitX), flown: true);
        rig.Set(FakeFlight.Plane(1, new Vector3(1900f, 400f, 0f), Vector3.UnitX, speed: 0f));
        rig.Step();
        Assert.Equal(BotMode.Edge, rig.Module.Pilot(4).Mode);
        float farthest = 0f;
        for (int i = 0; i < 60 * 20; i++)
        {
            rig.Step();
            farthest = MathF.Max(farthest, MathF.Max(MathF.Abs(rig.States[4].Position.X), MathF.Abs(rig.States[4].Position.Z)));
        }
        Assert.True(farthest < 1950f, $"farthest {farthest} m from the center line");
    }

    [Fact]
    public void CirclesNearTheCenterWithoutATarget()
    {
        var rig = new Rig();
        rig.Set(FakeFlight.Plane(4, new Vector3(1000f, 400f, 1000f), North), flown: true);
        rig.Set(FakeFlight.Plane(1, new Vector3(0f, 400f, 0f), North, alive: false));
        rig.Step();
        Assert.Equal(BotMode.Circle, rig.Module.Pilot(4).Mode);
        Assert.Equal(-1, rig.Module.Pilot(4).TargetId);
        Assert.False(rig.Input(4).Fire);
        float farthest = 0f;
        for (int i = 0; i < 60 * 60; i++)
        {
            rig.Step();
            if (i > 60 * 20)
                farthest = MathF.Max(farthest, new Vector2(rig.States[4].Position.X, rig.States[4].Position.Z).Length());
            Assert.True(rig.States[4].Position.Y > 100f);
        }
        Assert.True(farthest < 1500f, $"farthest {farthest} m from the center");
    }

    private static Rig Dogfight()
    {
        var rig = new Rig();
        for (int id = 0; id < Teams.PlaneCount; id++)
        {
            bool allies = Teams.IsAllies(id);
            int slot = id % Teams.TeamSize;
            rig.Set(FakeFlight.Plane(id, new Vector3(-90f + 60f * slot, 400f, allies ? 1500f : -1500f), allies ? North : -North),
                flown: id != 0);
        }
        return rig;
    }

    [Fact]
    public void AFullDogfightStaysAboveTheGroundInsideTheAreaAndShoots()
    {
        var rig = Dogfight();
        int shots = 0;
        for (int i = 0; i < 60 * 90; i++)
        {
            rig.Step();
            for (int id = 1; id < Teams.PlaneCount; id++)
            {
                Vector3 p = rig.States[id].Position;
                Assert.True(p.Y > 40f, $"plane {id} down to {p.Y} m");
                Assert.True(MathF.Abs(p.X) < 2000f && MathF.Abs(p.Z) < 2000f, $"plane {id} left the area at {p}");
            }
            for (int k = 0; k < rig.Bus.Count; k++)
                shots += rig.Bus.Inputs[k].Fire ? 1 : 0;
        }
        Assert.True(shots > 0, "nobody ever fired");
    }

    [Fact]
    public void IsDeterministic()
    {
        var a = Dogfight();
        var b = Dogfight();
        for (int i = 0; i < 60 * 30; i++)
        {
            a.Step();
            b.Step();
            Assert.Equal(a.Bus.Count, b.Bus.Count);
            for (int k = 0; k < a.Bus.Count; k++)
            {
                ControlInput x = a.Bus.Inputs[k], y = b.Bus.Inputs[k];
                Assert.Equal(x.PlaneId, y.PlaneId);
                Assert.Equal(x.Pitch, y.Pitch);
                Assert.Equal(x.Roll, y.Roll);
                Assert.Equal(x.Yaw, y.Yaw);
                Assert.Equal(x.Throttle, y.Throttle);
                Assert.Equal(x.Fire, y.Fire);
            }
        }
    }

    [Fact]
    public void BotsDifferAndDoNotFlyInFormation()
    {
        var module = new BotsModule();
        for (int a = 1; a < Teams.PlaneCount; a++)
        {
            for (int b = a + 1; b < Teams.PlaneCount; b++)
                Assert.False(module.Pilot(a).Reaction == module.Pilot(b).Reaction
                    && module.Pilot(a).AimErrorAngle == module.Pilot(b).AimErrorAngle, $"bots {a} and {b} are identical");
        }
        for (int id = 1; id < Teams.PlaneCount; id++)
        {
            Assert.InRange(module.Pilot(id).Reaction, 0.15f, 0.45f);
            Assert.InRange(module.Pilot(id).AimErrorAngle, 0.5f * Deg, 2.5f * Deg);
        }

        // Wingmen 1-3 start side by side 60 m apart on the same heading; flown by their bots
        // through the merge with the enemy, they leave that formation: their spacing and
        // headings come apart.
        var rig = Dogfight();
        float spacing = 0f, headings = 1f;
        for (int i = 0; i < 60 * 40; i++)
        {
            rig.Step();
            float d12 = Vector3.Distance(rig.States[1].Position, rig.States[2].Position);
            float d23 = Vector3.Distance(rig.States[2].Position, rig.States[3].Position);
            spacing = MathF.Max(spacing, MathF.Max(MathF.Abs(d12 - 60f), MathF.Abs(d23 - 60f)));
            headings = MathF.Min(headings, MathF.Min(Vector3.Dot(Nose(rig.States[1]), Nose(rig.States[2])),
                Vector3.Dot(Nose(rig.States[2]), Nose(rig.States[3]))));
        }
        Assert.True(spacing > 60f, $"spacing changed by at most {spacing} m");
        Assert.True(headings < MathF.Cos(30f * Deg), "headings never 30 degrees apart");
    }

    [Fact]
    public void TickDoesNotAllocate()
    {
        var rig = Dogfight();
        for (int i = 0; i < 120; i++)
            rig.Step(); // warm up (JIT)
        var module = rig.Module;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 600; i++)
            module.Tick(new TickContext(i, Dt));
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }
}
