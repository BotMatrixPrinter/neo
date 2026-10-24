using System;
using System.Numerics;
using Neo.Flight;
using Neo.Input;
using Neo.Shell;
using Neo.World;
using Xunit;

namespace Neo.Bots.Tests;

public class BotsModuleTests
{
    private const float Dt = 1f / 60f;
    private const float Deg = MathF.PI / 180f;
    private static readonly Vector3 North = -Vector3.UnitZ;
    /// <summary>Half the area's side (World Contract): every test position is a fraction of it.</summary>
    private const float H = WorldBounds.HalfSize;

    /// <summary>
    /// The module with a fake bus, and fake planes: bots in <see cref="Flown"/> fly with
    /// <see cref="FakeFlight"/> from the inputs the module publishes; the others fly straight.
    /// </summary>
    private sealed class Rig
    {
        public readonly BotsModule Module;
        public readonly FakeBus Bus = new();
        public readonly PlaneState[] States = new PlaneState[Teams.PlaneCount];
        public readonly bool[] Present = new bool[Teams.PlaneCount];
        public readonly bool[] Flown = new bool[Teams.PlaneCount];
        private readonly FakeFlight[] _flights = new FakeFlight[Teams.PlaneCount];
        private long _tick;

        public Rig(float evasionSkill = BotPilot.EvasionSkill, float aimSkill = BotPilot.AimSkill)
        {
            Module = new BotsModule(evasionSkill, aimSkill);
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

    /// <summary>Height above the terrain under the plane, m.</summary>
    private static float Clearance(in PlaneState s) => s.Position.Y - Terrain.HeightAt(s.Position.X, s.Position.Z);

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

    private static bool IsEvasion(BotMode mode)
        => mode is BotMode.Break or BotMode.Extend or BotMode.SplitS or BotMode.RollAway;

    private static float Elevation(in PlaneState s) => MathF.Asin(Math.Clamp(Nose(s).Y, -1f, 1f));

    /// <summary>
    /// Puts <paramref name="chaser"/> <paramref name="behind"/> m behind bot <paramref name="bot"/>
    /// along its nose, <paramref name="right"/> m to its right and 10 m above it (its body
    /// axes), nose on the bot: a pursuer that follows every move and cannot be shaken off.
    /// </summary>
    private static void Stick(Rig rig, int chaser, int bot, float behind = 150f, float right = 30f)
    {
        PlaneState b = rig.States[bot];
        Vector3 p = b.Position + Vector3.Transform(new Vector3(right, 10f, behind), b.Orientation);
        Vector3 look = Vector3.Normalize(b.Position - p);
        Vector3 up = Vector3.Transform(Vector3.UnitY, b.Orientation);
        Quaternion q = Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateWorld(Vector3.Zero, look, up));
        rig.Set(FakeFlight.Plane(chaser, p, q, speed: b.Speed));
    }

    [Fact]
    public void EvadesAfterItsReactionDelayWhenAnEnemySitsOnItsTail()
    {
        var rig = new Rig();
        rig.Set(FakeFlight.Plane(4, new Vector3(0f, 400f, 0f), North));
        rig.Set(FakeFlight.Plane(1, new Vector3(20f, 400f, 150f), North)); // 150 m behind, nose on us
        rig.Step();
        Assert.False(IsEvasion(rig.Module.Pilot(4).Mode)); // reaction delay
        for (int i = 0; i < 60; i++)
            rig.Step();
        Assert.True(IsEvasion(rig.Module.Pilot(4).Mode), $"mode {rig.Module.Pilot(4).Mode}");
        Assert.Equal(1, rig.Module.Pilot(4).ChaserId);
        Assert.False(rig.Input(4).Fire);
        Assert.Equal(1f, rig.Input(4).Throttle);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(BotPilot.EvasionSkill)]
    public void BreaksTowardTheAttackersSide(float skill)
    {
        // Every bot, chased from the right rear and from the left rear at 400 m and 50 m/s:
        // whenever it picks a break, it rolls hard toward the attacker and pulls once banked
        // (softer with less evasion skill).
        int breaks = 0;
        foreach (float sign in new[] { 1f, -1f })
        {
            for (int bot = 1; bot < Teams.PlaneCount; bot++)
            {
                int enemy = Teams.IsAllies(bot) ? 5 : 1;
                var rig = new Rig(skill);
                rig.Set(FakeFlight.Plane(bot, new Vector3(0f, 400f, 0f), North), flown: true);
                Stick(rig, enemy, bot, 150f, 40f * sign);
                int i = 0;
                while (!IsEvasion(rig.Module.Pilot(bot).Mode) && i++ < 60)
                {
                    Stick(rig, enemy, bot, 150f, 40f * sign);
                    rig.Step();
                }
                Assert.True(IsEvasion(rig.Module.Pilot(bot).Mode), $"bot {bot} never evaded");
                if (rig.Module.Pilot(bot).Mode != BotMode.Break)
                    continue;
                breaks++;
                Assert.True(rig.Input(bot).Roll * sign > 0.4f * skill, $"bot {bot} rolls {rig.Input(bot).Roll}, attacker on side {sign}"); // full roll, give or take a jink
                // Over the next second: banks steeply toward the attacker (never the other way)
                // and pulls (full pull once banked, give or take a jink).
                float most = 0f, least = 1f, pull = 0f;
                for (int k = 0; k < 60; k++)
                {
                    Stick(rig, enemy, bot, 150f, 40f * sign);
                    rig.Step();
                    float toward = -Vector3.Transform(Vector3.UnitX, rig.States[bot].Orientation).Y * sign;
                    most = MathF.Max(most, toward);
                    least = MathF.Min(least, toward);
                    if (k >= 30)
                        pull += rig.Input(bot).Pitch / 30f;
                }
                Assert.True(most > MathF.Sin((70f - 80f * (1f - skill)) * Deg), $"bot {bot} banked at most {MathF.Asin(most) / Deg} deg toward side {sign}");
                Assert.True(least > -MathF.Sin(10f * Deg), $"bot {bot} banked away from side {sign}");
                Assert.True(pull > 0.5f - 0.6f * (1f - skill), $"bot {bot} pulls {pull} on average");
            }
        }
        Assert.True(breaks >= 4, $"only {breaks} breaks in 14 encounters");
    }

    [Fact]
    public void VariesItsMovesPerBotAndEncounter()
    {
        // The same bot chased again and again (the pursuer leaves, comes back) picks different
        // moves; and different bots pick different first moves.
        var kinds = new System.Collections.Generic.HashSet<BotMode>();
        var firsts = new System.Collections.Generic.HashSet<BotMode>();
        for (int bot = 4; bot < Teams.PlaneCount; bot++)
        {
            var rig = new Rig();
            rig.Set(FakeFlight.Plane(bot, new Vector3(0f, 600f, 0f), North));
            var bot0 = rig.States[bot];
            var moves = new System.Collections.Generic.HashSet<BotMode>();
            for (int encounter = 0; encounter < 8; encounter++)
            {
                BotMode picked = BotMode.None;
                for (int i = 0; i < 60 && picked == BotMode.None; i++)
                {
                    rig.States[bot] = bot0;
                    Stick(rig, 1, bot, 150f, 20f);
                    rig.Step();
                    if (IsEvasion(rig.Module.Pilot(bot).Mode))
                        picked = rig.Module.Pilot(bot).Mode;
                }
                Assert.NotEqual(BotMode.None, picked);
                if (encounter == 0)
                    firsts.Add(picked);
                moves.Add(picked);
                kinds.Add(picked);
                rig.Set(FakeFlight.Plane(1, new Vector3(0f, 600f, -0.75f * H), North)); // gone: shaken off, then the cooldown
                for (int i = 0; i < 60 * 4; i++)
                {
                    rig.States[bot] = bot0;
                    rig.Step();
                }
                Assert.False(IsEvasion(rig.Module.Pilot(bot).Mode));
            }
            Assert.Equal(8, rig.Module.Pilot(bot).Encounters);
            Assert.True(moves.Count >= 2, $"bot {bot} always picks {Names(moves)}");
        }
        Assert.True(kinds.Count >= 3, $"only {kinds.Count} kinds of moves");
        Assert.True(firsts.Count >= 2, "every bot opens with the same move");

        static string Names(System.Collections.Generic.HashSet<BotMode> m) => string.Join(",", m);
    }

    [Fact]
    public void DivesWhenSlowOrHigh()
    {
        // Slow (35 m/s) at 600 m, or high (900 m) at 50 m/s: every bot evades by diving.
        foreach ((float alt, float speed) in new[] { (600f, 35f), (900f, 50f), (950f, 35f) })
        {
            for (int bot = 4; bot < Teams.PlaneCount; bot++)
            {
                var rig = new Rig();
                rig.Set(FakeFlight.Plane(bot, new Vector3(0f, alt, 0f), North, speed: speed), flown: true);
                float lowestNose = 0f;
                for (int i = 0; i < 60 * 3; i++)
                {
                    Stick(rig, 1, bot);
                    rig.Step();
                    BotMode mode = rig.Module.Pilot(bot).Mode;
                    if (IsEvasion(mode))
                        Assert.NotEqual(BotMode.Break, mode);
                    lowestNose = MathF.Min(lowestNose, Elevation(rig.States[bot]));
                }
                Assert.True(lowestNose < -20f * Deg, $"bot {bot} at {alt} m, {speed} m/s: nose down to {lowestNose / Deg} deg only");
                Assert.True(rig.States[bot].Position.Y < alt - 30f, $"bot {bot} at {alt} m, {speed} m/s: now {rig.States[bot].Position.Y} m");
            }
        }

        // Slow and low (200 m): no room to dive, so a break, but never climbing.
        for (int bot = 4; bot < Teams.PlaneCount; bot++)
        {
            var rig = new Rig();
            rig.Set(FakeFlight.Plane(bot, new Vector3(0f, 200f, 0f), North, speed: 35f), flown: true);
            bool broke = false;
            for (int i = 0; i < 60 * 3; i++)
            {
                Stick(rig, 1, bot);
                rig.Step();
                if (rig.States[bot].Speed < BotPilot.MinClimbSpeed - 1f)
                    Assert.True(rig.States[bot].Velocity.Y < 2f, $"bot {bot} climbs at {rig.States[bot].Speed} m/s");
                BotMode mode = rig.Module.Pilot(bot).Mode;
                if (IsEvasion(mode))
                    Assert.Equal(BotMode.Break, mode);
                broke |= mode == BotMode.Break;
            }
            Assert.True(broke, $"bot {bot} never broke");
        }
    }

    [Fact]
    public void AChasedBotNeverKeepsClimbingAndStaysSafe()
    {
        // A pursuer glued to the tail (it can't be shaken off) for 40 s, from mid altitude,
        // high, near the ceiling, low, and slow: the bot never spirals upward, never climbs
        // while slow, stays above 80 m and inside the area. Each bot, from either side.
        foreach ((float alt, float speed) in new[] { (400f, 50f), (900f, 45f), (990f, 30f), (150f, 42f), (100f, 30f), (510f, 32f) })
        {
            for (int bot = 1; bot < Teams.PlaneCount; bot++)
            {
                int enemy = Teams.IsAllies(bot) ? 5 : 1;
                var rig = new Rig();
                rig.Set(FakeFlight.Plane(bot, new Vector3(0f, alt, 0.4f * H), North, speed: speed), flown: true);
                float climbing = 0f, longestClimb = 0f, slowClimbing = 0f, longestSlowClimb = 0f;
                int evading = 0;
                for (int i = 0; i < 60 * 40; i++)
                {
                    Stick(rig, enemy, bot, 150f, bot % 2 == 0 ? 30f : -30f);
                    rig.Step();
                    PlaneState s = rig.States[bot];
                    climbing = s.Velocity.Y > 1f ? climbing + Dt : 0f;
                    longestClimb = MathF.Max(longestClimb, climbing);
                    slowClimbing = s.Velocity.Y > 1f && s.Speed < BotPilot.MinClimbSpeed ? slowClimbing + Dt : 0f;
                    longestSlowClimb = MathF.Max(longestSlowClimb, slowClimbing);
                    if (IsEvasion(rig.Module.Pilot(bot).Mode))
                        evading++;
                    string at = $"bot {bot} from {alt} m, {speed} m/s, at {i * Dt:0.0} s";
                    Assert.True(Clearance(s) > BotPilot.MinAltitude, $"{at}: down to {Clearance(s)} m above the ground");
                    Assert.True(Inside(s.Position), $"{at}: left the area at {s.Position}");
                }
                string what = $"bot {bot} from {alt} m, {speed} m/s";
                Assert.True(longestClimb < 4f, $"{what}: climbed {longestClimb} s on end");
                Assert.True(longestSlowClimb < 0.75f, $"{what}: climbed {longestSlowClimb} s on end below {BotPilot.MinClimbSpeed} m/s");
                Assert.True(evading > 60 * 5, $"{what}: evaded only {evading} steps");
            }
        }
    }

    [Fact]
    public void EvasionIsTimeLimitedThenItAttacks()
    {
        // Glued pursuer: each evasion ends within its time limit, then the bot attacks (the
        // pursuer) for the cooldown before it evades again.
        var rig = new Rig();
        rig.Set(FakeFlight.Plane(4, new Vector3(0f, 400f, 0f), North), flown: true);
        float evadeFor = 0f, longest = 0f;
        int returns = 0;
        BotMode last = BotMode.None;
        for (int i = 0; i < 60 * 30; i++)
        {
            Stick(rig, 1, 4);
            rig.Step();
            BotMode mode = rig.Module.Pilot(4).Mode;
            evadeFor = IsEvasion(mode) ? evadeFor + Dt : 0f;
            longest = MathF.Max(longest, evadeFor);
            if (IsEvasion(last) && mode == BotMode.Attack)
            {
                returns++;
                Assert.Equal(1, rig.Module.Pilot(4).TargetId);
            }
            if (mode is BotMode.Attack or BotMode.Circle)
                Assert.Equal(-1, rig.Module.Pilot(4).ChaserId);
            last = mode;
        }
        Assert.True(longest <= (BotPilot.EvadeTime + BotPilot.EvadeTimeSpread) * BotPilot.EvasionSkill + 0.1f, $"evaded {longest} s on end");
        Assert.True(returns >= 3, $"went back to attacking {returns} times in 30 s");

        // Shaken off (the pursuer is suddenly far away): back to attacking soon.
        var shaken = new Rig();
        shaken.Set(FakeFlight.Plane(4, new Vector3(0f, 400f, 0f), North), flown: true);
        for (int i = 0; i < 40; i++)
        {
            Stick(shaken, 1, 4);
            shaken.Step();
        }
        Assert.True(IsEvasion(shaken.Module.Pilot(4).Mode));
        shaken.Set(FakeFlight.Plane(1, new Vector3(0f, 400f, -0.6f * H), North));
        for (int i = 0; i < 60 * (BotPilot.ShakenTime + 0.2f); i++)
            shaken.Step();
        Assert.Equal(BotMode.Attack, shaken.Module.Pilot(4).Mode);
        Assert.Equal(1, shaken.Module.Pilot(4).TargetId);
    }

    [Fact]
    public void TurnsOnAPursuerThatOvershoots()
    {
        // The bot (its stick ignored here: flies straight at 30 m/s) is chased by a faster
        // plane that flies past it: once the pursuer is ahead it stops evading and attacks it,
        // even with another enemy nearer.
        var rig = new Rig();
        rig.Set(FakeFlight.Plane(4, new Vector3(0f, 400f, 0f), North, speed: 30f));
        rig.Set(FakeFlight.Plane(1, new Vector3(15f, 405f, 90f), North, speed: 75f));
        bool evaded = false;
        int overshot = -1;
        for (int i = 0; i < 60 * 6; i++)
        {
            if (i == 60 * 2) // an enemy 50 m off its left wing, flying along: nearer than the overshooter later
                rig.Set(FakeFlight.Plane(2, rig.States[4].Position + new Vector3(-50f, 0f, 0f), North, speed: 30f));
            rig.Step();
            BotPilot pilot = rig.Module.Pilot(4);
            evaded |= IsEvasion(pilot.Mode);
            if (overshot < 0 && Vector3.Dot(rig.States[1].Position - rig.States[4].Position, North) > 0f)
                overshot = i;
            if (overshot >= 0 && i > overshot + 60 && i < overshot + 60 * BotPilot.ReverseTime)
            {
                Assert.Equal(BotMode.Attack, pilot.Mode);
                Assert.Equal(1, pilot.TargetId);
            }
        }
        Assert.True(evaded, "never evaded");
        Assert.True(overshot > 0 && overshot < 60 * 4, $"overshoot at step {overshot}");
    }

    [Fact]
    public void JinksWhileShotAt()
    {
        // Pursuer 150 m behind, nose right on the bot: during the evasion (other than a split-S)
        // the stick gets short random pitch and roll changes.
        int jinkers = 0;
        for (int bot = 4; bot < Teams.PlaneCount; bot++)
        {
            var rig = new Rig();
            rig.Set(FakeFlight.Plane(bot, new Vector3(0f, 400f, 0f), North));
            rig.Set(FakeFlight.Plane(1, new Vector3(0f, 400f, 150f), North));
            var sticks = new System.Collections.Generic.HashSet<(float, float)>();
            for (int i = 0; i < 150; i++)
            {
                rig.Step();
                if (rig.Module.Pilot(bot).Jinking)
                    sticks.Add((MathF.Round(rig.Input(bot).Pitch, 2), MathF.Round(rig.Input(bot).Roll, 2)));
            }
            if (rig.Module.Pilot(bot).Mode == BotMode.SplitS)
                continue;
            jinkers++;
            Assert.True(sticks.Count >= 3, $"bot {bot} jinks with {sticks.Count} stick positions");
        }
        Assert.True(jinkers >= 2);
    }

    [Fact]
    public void LessEvasionSkillEvadesLaterSofterAndShorterButAttacksTheSame()
    {
        // EvasionSkill 0.75 against 1.0 (the old bots): the evasion is about 25% weaker, the
        // attack and the choice of moves the same.
        Assert.Equal(0.75f, BotPilot.EvasionSkill);
        for (int bot = 1; bot < Teams.PlaneCount; bot++)
        {
            var weak = new BotPilot(bot);
            var full = new BotPilot(bot, 1f);
            Assert.Equal(full.Reaction, weak.Reaction); // aiming lag unchanged
            Assert.Equal(full.Reaction, full.EvadeReaction);
            Assert.Equal(full.EvadeReaction * 1.25f, weak.EvadeReaction, 4);
            Assert.Equal(full.EvadeLimit * 0.75f, weak.EvadeLimit, 4);
            Assert.Equal(BotPilot.EvadeCooldown, full.Cooldown);
            Assert.Equal(BotPilot.EvadeCooldown * 1.25f, weak.Cooldown, 4);
        }

        int breaks = 0, jinkers = 0;
        for (int bot = 1; bot < Teams.PlaneCount; bot++)
        {
            var weak = Chased(bot, BotPilot.EvasionSkill);
            var full = Chased(bot, 1f);
            // Breaks later (25% longer reaction), picks the same move.
            Assert.True(weak.Start > full.Start, $"bot {bot} evades at step {weak.Start}, with full skill {full.Start}");
            Assert.InRange(weak.Start / (float)full.Start, 1.15f, 1.35f);
            Assert.Equal(full.Move, weak.Move);
            // Evades for less time (its time limit: the pursuer stays glued on).
            Assert.InRange(weak.Evading / full.Evading, 0.7f, 0.8f);
            // Smaller, rarer jinks (none in a split-S).
            if (full.Move != BotMode.SplitS)
            {
                jinkers++;
                Assert.True(weak.Jinks > 0 && weak.Jinks < full.Jinks, $"bot {bot}: {weak.Jinks} jinks, with full skill {full.Jinks}");
                Assert.Equal(0.75f * full.JinkSize, weak.JinkSize, 4); // the same first jink, smaller
            }

            // Flown, chased from the right rear: in a break it pulls less.
            var weakBreak = Break(bot, BotPilot.EvasionSkill);
            var fullBreak = Break(bot, 1f);
            Assert.Equal(fullBreak.Move, weakBreak.Move);
            if (fullBreak.Move != BotMode.Break)
                continue;
            breaks++;
            Assert.InRange(weakBreak.Pull / fullBreak.Pull, 0.65f, 0.85f);
            // Held banked 70 degrees toward the attacker, nose on the break's -3 degrees: the
            // full-skill bot banks on toward its 80, the weaker one back toward 60.
            Assert.True(BankedRoll(bot, 1f) > 0.3f, $"bot {bot} with full skill rolls {BankedRoll(bot, 1f)}");
            Assert.True(BankedRoll(bot, BotPilot.EvasionSkill) < -0.3f, $"bot {bot} rolls {BankedRoll(bot, BotPilot.EvasionSkill)}");
        }
        Assert.True(breaks >= 2, $"only {breaks} breaks");
        Assert.True(jinkers >= 3, $"only {jinkers} bots jinked");

        // Attacking (no enemy on its tail): the very same stick and fire with either skill.
        var a = new Rig(BotPilot.EvasionSkill);
        var b = new Rig(1f);
        foreach (Rig r in new[] { a, b })
        {
            r.Set(FakeFlight.Plane(4, new Vector3(0f, 400f, 0f), North), flown: true);
            r.Set(FakeFlight.Plane(1, new Vector3(60f, 410f, -250f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, -0.5f)));
        }
        bool fired = false;
        for (int i = 0; i < 60 * 8; i++)
        {
            a.Step();
            b.Step();
            Assert.Equal(BotMode.Attack, a.Module.Pilot(4).Mode);
            Assert.Equal(b.Input(4), a.Input(4));
            fired |= a.Input(4).Fire;
        }
        Assert.True(fired, "never fired");

        // A bot flying straight at 600 m (its stick ignored), a pursuer glued on, nose right on
        // it: the step its first evasion starts, the move, how long it lasts, its jinks (how
        // many, and the size of the first).
        static (int Start, BotMode Move, float Evading, int Jinks, float JinkSize) Chased(int bot, float skill)
        {
            int enemy = Teams.IsAllies(bot) ? 5 : 1;
            var rig = new Rig(skill);
            rig.Set(FakeFlight.Plane(bot, new Vector3(0f, 600f, 0f), North));
            var start = rig.States[bot];
            int first = -1, jinks = 0;
            BotMode move = BotMode.None;
            float evading = 0f, jinkSize = 0f, lastJink = float.NaN;
            for (int i = 0; i < 60 * 8; i++)
            {
                rig.States[bot] = start;
                Stick(rig, enemy, bot, 150f, 0f);
                rig.Step();
                BotPilot p = rig.Module.Pilot(bot);
                if (p.Encounters != 1 || !IsEvasion(p.Mode))
                    continue;
                if (first < 0)
                {
                    first = i;
                    move = p.Mode;
                }
                evading += Dt;
                if (p.Jinking)
                {
                    if (p.JinkRollOffset != lastJink)
                        jinks++;
                    lastJink = p.JinkRollOffset;
                    if (jinks == 1)
                        jinkSize = MathF.Abs(p.JinkPitchOffset) + MathF.Abs(p.JinkRollOffset);
                }
            }
            Assert.True(first > 0, $"bot {bot} never evaded");
            return (first, move, evading, jinks, jinkSize);
        }

        // At 400 m, 50 m/s, chased from the right rear (held level until it evades, then
        // flown): its first move, and its average pull over the first second of it.
        static (BotMode Move, float Pull) Break(int bot, float skill)
        {
            int enemy = Teams.IsAllies(bot) ? 5 : 1;
            var rig = new Rig(skill);
            rig.Set(FakeFlight.Plane(bot, new Vector3(0f, 400f, 0f), North), flown: true);
            var start = rig.States[bot];
            for (int i = 0; i < 60 && !IsEvasion(rig.Module.Pilot(bot).Mode); i++)
            {
                rig.States[bot] = start; // level until it starts evading
                Aside(rig, enemy, bot);
                rig.Step();
            }
            float pull = 0f;
            for (int k = 0; k < 60; k++)
            {
                pull += rig.Input(bot).Pitch / 60f;
                Aside(rig, enemy, bot);
                rig.Step();
            }
            Assert.False(rig.Module.Pilot(bot).Jinking);
            return (rig.Module.Pilot(bot).Mode, pull);
        }

        // Its roll stick in a break, held at 400 m, 50 m/s, banked 70 degrees right, nose 3
        // degrees down, the attacker on its right rear.
        static float BankedRoll(int bot, float skill)
        {
            int enemy = Teams.IsAllies(bot) ? 5 : 1;
            var rig = new Rig(skill);
            Quaternion q = FakeFlight.Facing(North) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, -3f * Deg)
                * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -70f * Deg);
            var held = FakeFlight.Plane(bot, new Vector3(0f, 400f, 0f), q);
            for (int i = 0; i < 90 && rig.Module.Pilot(bot).Mode != BotMode.Break; i++)
            {
                rig.Set(held);
                Aside(rig, enemy, bot);
                rig.Step();
            }
            Assert.Equal(BotMode.Break, rig.Module.Pilot(bot).Mode);
            Assert.False(rig.Module.Pilot(bot).Jinking);
            return rig.Input(bot).Roll;
        }

        // Glued on 150 m behind, 40 m to the right, its nose 20 degrees off the bot: a chaser
        // (within 45 degrees) not shooting (beyond the 10 degree jink cone), so no jinks.
        static void Aside(Rig rig, int enemy, int bot)
        {
            Stick(rig, enemy, bot, 150f, 40f);
            PlaneState c = rig.States[enemy];
            Quaternion off = Quaternion.Concatenate(Quaternion.CreateFromAxisAngle(Vector3.UnitY, -20f * Deg), c.Orientation);
            rig.Set(FakeFlight.Plane(enemy, c.Position, off, speed: c.Speed));
        }
    }

    [Fact]
    public void ChasedFlightIsDeterministic()
    {
        Rig Run()
        {
            var rig = new Rig();
            rig.Set(FakeFlight.Plane(5, new Vector3(0f, 600f, 0f), North), flown: true);
            return rig;
        }
        var a = Run();
        var b = Run();
        for (int i = 0; i < 60 * 20; i++)
        {
            Stick(a, 2, 5);
            Stick(b, 2, 5);
            a.Step();
            b.Step();
            Assert.Equal(a.Input(5).Pitch, b.Input(5).Pitch);
            Assert.Equal(a.Input(5).Roll, b.Input(5).Roll);
            Assert.Equal(a.Input(5).Throttle, b.Input(5).Throttle);
            Assert.Equal(a.Module.Pilot(5).Mode, b.Module.Pilot(5).Mode);
            Assert.Equal(a.States[5].Position, b.States[5].Position);
        }
    }

    [Fact]
    public void DoesNotEvadeAnEnemyFarBehindOrFlyingAway()
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
            Assert.False(IsEvasion(far.Module.Pilot(4).Mode));
            Assert.False(IsEvasion(away.Module.Pilot(4).Mode));
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
            lowest = MathF.Min(lowest, Clearance(rig.States[4]));
        }
        Assert.True(lowest > 40f, $"lowest {lowest} m above the ground");
    }

    private static bool Inside(Vector3 p) => MathF.Abs(p.X) < H && MathF.Abs(p.Z) < H;

    /// <summary>
    /// Terrain tops inside the area, found by sampling <see cref="Terrain.HeightAt"/> every 20 m
    /// within 0.7 of the half size (clear of its edges): the highest first, each at least 0.4 of
    /// the half size from the ones before; only those up to <paramref name="maxHeight"/>.
    /// </summary>
    private static System.Collections.Generic.List<Vector3> Tops(int count, float maxHeight = float.MaxValue)
    {
        var points = new System.Collections.Generic.List<Vector3>();
        for (float x = -0.7f * H; x <= 0.7f * H; x += 20f)
        {
            for (float z = -0.7f * H; z <= 0.7f * H; z += 20f)
            {
                float y = Terrain.HeightAt(x, z);
                if (y <= maxHeight)
                    points.Add(new Vector3(x, y, z));
            }
        }
        points.Sort((p, q) => q.Y.CompareTo(p.Y));
        var tops = new System.Collections.Generic.List<Vector3>();
        foreach (Vector3 p in points)
        {
            if (tops.Count == count)
                break;
            if (tops.TrueForAll(t => new Vector2(t.X - p.X, t.Z - p.Z).Length() > 0.4f * H))
                tops.Add(p);
        }
        return tops;
    }

    /// <summary>The horizontal direction from <paramref name="top"/> toward the center (north if it is the center).</summary>
    private static Vector3 TowardCenter(Vector3 top)
    {
        Vector3 d = new(-top.X, 0f, -top.Z);
        return d.LengthSquared() < 1f ? -North : Vector3.Normalize(d);
    }

    [Fact]
    public void ClimbsOrTurnsAwayBeforeTheHighestTerrainAhead()
    {
        // At each of the three highest tops in the area (mountains, or hills on a small map):
        // level 100 m over the ground half the half size away (on its center side), heading
        // at it, chasing a target at that height beyond it: it climbs or turns away in time,
        // never flying into it.
        var tops = Tops(3);
        Assert.Equal(3, tops.Count);
        foreach (Vector3 top in tops)
        {
            Vector3 back = TowardCenter(top);
            Vector3 from = top + back * 0.5f * H;
            from.Y = Terrain.HeightAt(from.X, from.Z) + 100f;
            Vector3 target = top - back * 0.2f * H;
            target.Y = from.Y;
            var rig = new Rig();
            rig.Set(FakeFlight.Plane(4, from, target - from), flown: true);
            rig.Set(FakeFlight.Plane(1, target, North, speed: 0f));
            bool reacted = false;
            float lowest = float.MaxValue;
            for (int i = 0; i < 60 * 40; i++)
            {
                rig.Step();
                PlaneState s = rig.States[4];
                if (!reacted && rig.Module.Pilot(4).Mode == BotMode.Ground)
                {
                    // In time: it reacts to the terrain ahead while still well above the ground under it.
                    reacted = true;
                    Assert.True(Clearance(s) > BotPilot.MinAltitude, $"top {top}: reacted only {Clearance(s)} m above the ground");
                }
                lowest = MathF.Min(lowest, Clearance(s));
            }
            Assert.True(reacted, $"top {top}: never climbed or turned for it");
            Assert.True(lowest > 0.5f * BotPilot.MinAltitude, $"top {top}: down to {lowest} m above the ground");
        }
    }

    [Fact]
    public void KeepsEightyMetersAboveHills()
    {
        // Flying level 90 m over the plain toward a target at that height beyond the highest
        // rolling hill in the area (a top under 70 m, so no mountain): it keeps about 80 m
        // above the ground under it, also over the hill.
        Vector3 hill = Tops(1, 70f)[0];
        Assert.True(hill.Y > 25f, $"highest hill {hill.Y} m only");
        Vector3 back = TowardCenter(hill);
        Vector3 from = hill + back * 0.4f * H;
        from.Y = 90f;
        Vector3 target = hill - back * 0.15f * H;
        target.Y = 90f;
        var rig = new Rig();
        rig.Set(FakeFlight.Plane(4, from, target - from), flown: true);
        rig.Set(FakeFlight.Plane(1, target, North, speed: 0f));
        float lowest = float.MaxValue, highestGround = 0f;
        for (int i = 0; i < 60 * 30; i++)
        {
            rig.Step();
            PlaneState s = rig.States[4];
            lowest = MathF.Min(lowest, Clearance(s));
            highestGround = MathF.Max(highestGround, Terrain.HeightAt(s.Position.X, s.Position.Z));
        }
        Assert.True(highestGround > 0.7f * hill.Y, $"flew over ground of {highestGround} m only (hill {hill.Y} m)");
        Assert.True(lowest > BotPilot.MinAltitude - 5f, $"down to {lowest} m above the ground");
    }

    [Fact]
    public void EvasiveDivesNeverEndInTheTerrain()
    {
        // A pursuer glued to its tail toward each of the three highest tops (each bot), from
        // 300 m off, 310 m above it: its evasive dives level off with room to pull out above
        // the terrain ahead (well above the 80 m safety height), and it stays 80 m above the
        // ground under it.
        foreach (Vector3 top in Tops(3))
        {
            Vector3 back = TowardCenter(top);
            Vector3 from = top + back * 300f;
            from.Y = top.Y + 310f;
            for (int bot = 1; bot < Teams.PlaneCount; bot++)
            {
                int enemy = Teams.IsAllies(bot) ? 5 : 1;
                var rig = new Rig();
                rig.Set(FakeFlight.Plane(bot, from, -back, speed: 50f), flown: true);
                bool dived = false;
                float lowestDive = float.MaxValue, lowest = float.MaxValue;
                for (int i = 0; i < 60 * 30; i++)
                {
                    Stick(rig, enemy, bot, 150f, bot % 2 == 0 ? 30f : -30f);
                    rig.Step();
                    PlaneState s = rig.States[bot];
                    bool diving = rig.Module.Pilot(bot).Mode is BotMode.Extend or BotMode.SplitS or BotMode.RollAway;
                    dived |= diving;
                    if (diving)
                        lowestDive = MathF.Min(lowestDive, Clearance(s));
                    lowest = MathF.Min(lowest, Clearance(s));
                }
                Assert.True(dived, $"top {top}: bot {bot} never dived");
                Assert.True(lowestDive > BotPilot.MinAltitude + 0.5f * BotPilot.DiveFloorMargin, $"top {top}: bot {bot} dived down to {lowestDive} m above the ground");
                Assert.True(lowest > BotPilot.MinAltitude, $"top {top}: bot {bot} down to {lowest} m above the ground");
            }
        }
    }

    [Fact]
    public void TurnsBackBeforeTheEdge()
    {
        // Near the east edge heading out, chasing a target further out: it turns back.
        Assert.Equal(0.3f * H, BotPilot.EdgeMargin);
        var rig = new Rig();
        rig.Set(FakeFlight.Plane(4, new Vector3(0.75f * H, 400f, 0f), Vector3.UnitX), flown: true);
        rig.Set(FakeFlight.Plane(1, new Vector3(0.95f * H, 400f, 0f), Vector3.UnitX, speed: 0f));
        rig.Step();
        Assert.Equal(BotMode.Edge, rig.Module.Pilot(4).Mode);
        float farthest = 0f;
        for (int i = 0; i < 60 * 20; i++)
        {
            rig.Step();
            farthest = MathF.Max(farthest, MathF.Max(MathF.Abs(rig.States[4].Position.X), MathF.Abs(rig.States[4].Position.Z)));
        }
        Assert.True(farthest < 0.9f * H, $"farthest {farthest} m from the center line");
    }

    [Theory]
    [InlineData(1f, 0f)]
    [InlineData(-1f, 0f)]
    [InlineData(0f, 1f)]
    [InlineData(0f, -1f)]
    public void NeverFliesIntoTheWallsAtTheEdge(float dirX, float dirZ)
    {
        // Low (100 m over the ground) from half the half size, heading for an edge and the
        // mountains beyond it (Terrain.HeightAt has them, if the map has walls), chasing a
        // target at the edge: every bot turns back in time, stays inside the area and well
        // above the terrain.
        Vector3 dir = new(dirX, 0f, dirZ);
        for (int bot = 1; bot < Teams.PlaneCount; bot++)
        {
            int enemy = Teams.IsAllies(bot) ? 5 : 1;
            Vector3 from = dir * 0.5f * H;
            from.Y = Terrain.HeightAt(from.X, from.Z) + 100f;
            Vector3 target = dir * 0.98f * H;
            target.Y = from.Y;
            var rig = new Rig();
            rig.Set(FakeFlight.Plane(bot, from, dir), flown: true);
            rig.Set(FakeFlight.Plane(enemy, target, dir, speed: 0f));
            float farthest = 0f, lowest = float.MaxValue;
            for (int i = 0; i < 60 * 30; i++)
            {
                rig.Step();
                PlaneState s = rig.States[bot];
                farthest = MathF.Max(farthest, MathF.Max(MathF.Abs(s.Position.X), MathF.Abs(s.Position.Z)));
                lowest = MathF.Min(lowest, Clearance(s));
            }
            Assert.True(farthest < 0.9f * H, $"bot {bot} toward {dir}: got {farthest} m from the center line");
            Assert.True(lowest > 0.5f * BotPilot.MinAltitude, $"bot {bot} toward {dir}: down to {lowest} m above the ground");
        }
    }

    [Fact]
    public void CirclesNearTheCenterWithoutATarget()
    {
        Assert.Equal(0.35f * H, BotPilot.CircleRadius);
        var rig = new Rig();
        rig.Set(FakeFlight.Plane(4, new Vector3(0.5f * H, 400f, 0.5f * H), North), flown: true);
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
            Assert.True(Clearance(rig.States[4]) > 100f);
            Assert.True(MathF.Abs(Bank(rig.States[4])) < 100f * Deg, $"banked {Bank(rig.States[4]) / Deg} deg while circling");
        }
        Assert.True(farthest < 0.6f * H, $"farthest {farthest} m from the center");
    }

    /// <summary>Bank angle, positive rolled right (180: inverted).</summary>
    private static float Bank(in PlaneState s)
    {
        Vector3 right = Vector3.Transform(Vector3.UnitX, s.Orientation);
        Vector3 up = Vector3.Transform(Vector3.UnitY, s.Orientation);
        return MathF.Atan2(-right.Y, up.Y);
    }

    [Theory]
    [InlineData(0f, -60f, 400f)]   // dead ahead, 60 m below
    [InlineData(80f, -50f, 300f)]  // a little right, 50 m below
    [InlineData(-120f, -40f, 350f)] // left, 40 m below
    [InlineData(250f, -150f, 400f)] // well right (about 30 degrees), 150 m below
    public void StaysUprightChasingATargetBelowAhead(float right, float below, float ahead)
    {
        // Every bot at 500 m chasing a target flying straight on its own heading, ahead and
        // below it: it gets its nose onto the target (pushing down, banking at most about 80
        // degrees) without ever rolling inverted (bank always under 100 degrees).
        for (int bot = 1; bot < Teams.PlaneCount; bot++)
        {
            int enemy = Teams.IsAllies(bot) ? 5 : 1;
            var rig = new Rig();
            Vector3 start = new(0f, 500f, 0.3f * H);
            rig.Set(FakeFlight.Plane(bot, start, North), flown: true);
            rig.Set(FakeFlight.Plane(enemy, start + new Vector3(right, below, -ahead), North, speed: 45f));
            float most = 0f, best = float.MaxValue;
            for (int i = 0; i < 60 * 10; i++)
            {
                rig.Step();
                Assert.Equal(BotMode.Attack, rig.Module.Pilot(bot).Mode);
                most = MathF.Max(most, MathF.Abs(Bank(rig.States[bot])));
                best = MathF.Min(best, AngleTo(rig.States[bot], rig.States[enemy].Position));
            }
            Assert.True(most < 100f * Deg, $"bot {bot}: banked {most / Deg} deg");
            Assert.True(best < 5f * Deg, $"bot {bot}: nose within {best / Deg} deg of the target at best");
        }
    }

    [Theory]
    [InlineData(0f, 0f, 300f, true)]     // target dead ahead, same height: fine aim
    [InlineData(0f, -40f, 400f, true)]   // ahead, a little below
    [InlineData(300f, 0f, 300f, true)]   // 45 degrees right
    [InlineData(0f, 0f, 0f, false)]      // no target: circling
    public void RollsUprightWhenLeftInverted(float right, float below, float ahead, bool target)
    {
        // Every bot left flying inverted (after a move) at 500 m: within 2 s it is upright
        // (bank under 90 degrees) and stays so (under 100); with a target ahead it ends on it
        // in fine aim, not inverted.
        Quaternion inverted = FakeFlight.Facing(North) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI);
        for (int bot = 1; bot < Teams.PlaneCount; bot++)
        {
            int enemy = Teams.IsAllies(bot) ? 5 : 1;
            var rig = new Rig();
            Vector3 start = new(0f, 500f, 0.3f * H);
            rig.Set(FakeFlight.Plane(bot, start, inverted), flown: true);
            rig.Set(FakeFlight.Plane(enemy, start + new Vector3(right, below, -ahead), North, speed: 45f, alive: target));
            Assert.True(MathF.Abs(Bank(rig.States[bot])) > 179f * Deg);
            for (int i = 0; i < 60 * 8; i++)
            {
                rig.Step();
                float bank = MathF.Abs(Bank(rig.States[bot]));
                if (i >= 60 * 2)
                    Assert.True(bank < (i == 60 * 2 ? 90f : 100f) * Deg, $"bot {bot}: banked {bank / Deg} deg after {i * Dt:0.0} s");
            }
            BotPilot pilot = rig.Module.Pilot(bot);
            Assert.Equal(target ? BotMode.Attack : BotMode.Circle, pilot.Mode);
            if (target)
                Assert.True(AngleTo(rig.States[bot], rig.States[enemy].Position) < BotPilot.FineAngle, $"bot {bot} not aiming at the target");
        }
    }

    [Fact]
    public void SplitSAndRollAwayStillGoInvertedThenRollUpright()
    {
        // High (900 m: always a dive), a pursuer glued on until its first evasion ends, then
        // gone: a split-S rolls inverted (past 150 degrees), a roll-away past 100; after the
        // move the bot is upright again within 2.5 s and stays so.
        var seen = new System.Collections.Generic.HashSet<BotMode>();
        for (int bot = 1; bot < Teams.PlaneCount; bot++)
        {
            int enemy = Teams.IsAllies(bot) ? 5 : 1;
            var rig = new Rig();
            rig.Set(FakeFlight.Plane(bot, new Vector3(0f, 900f, 0f), North, speed: 50f), flown: true);
            BotMode move = BotMode.None;
            float most = 0f;
            int i = 0;
            for (; i < 60 * 10; i++)
            {
                Stick(rig, enemy, bot, 150f, 30f);
                rig.Step();
                BotMode mode = rig.Module.Pilot(bot).Mode;
                if (IsEvasion(mode))
                {
                    move = mode;
                    most = MathF.Max(most, MathF.Abs(Bank(rig.States[bot])));
                }
                else if (move != BotMode.None)
                    break;
            }
            Assert.NotEqual(BotMode.None, move);
            seen.Add(move);
            if (move == BotMode.SplitS)
                Assert.True(most > 150f * Deg, $"bot {bot}: split-S banked {most / Deg} deg at most");
            if (move == BotMode.RollAway)
                Assert.True(most > 100f * Deg, $"bot {bot}: roll-away banked {most / Deg} deg at most");

            rig.Set(FakeFlight.Plane(enemy, rig.States[enemy].Position, North, alive: false));
            for (int k = 0; k < 60 * 6; k++)
            {
                rig.Step();
                float bank = MathF.Abs(Bank(rig.States[bot]));
                if (k >= 150)
                    Assert.True(bank < (k == 150 ? 90f : 100f) * Deg, $"bot {bot} after its {move}: banked {bank / Deg} deg {k * Dt:0.0} s later");
            }
        }
        Assert.Contains(BotMode.SplitS, seen);
        Assert.Contains(BotMode.RollAway, seen);
    }

    [Fact]
    public void AimSkillMakesTheAimErrorAThirdLargerAndHitsFewer()
    {
        // AimSkill 0.75 against 1.0 (the old bots): each bot's aim error is 1 / 0.75 as large.
        Assert.Equal(0.75f, BotPilot.AimSkill);
        for (int bot = 1; bot < Teams.PlaneCount; bot++)
            Assert.Equal(new BotPilot(bot, BotPilot.EvasionSkill, 1f).AimErrorAngle / 0.75f, new BotPilot(bot).AimErrorAngle, 5);

        // Flown behind a target 150 m ahead flying 20 degrees off its course, each bot for 15 s:
        // a firing step "hits" when the target, where it is when the bullets get there, is
        // within 4 m of the nose line. With less skill clearly fewer hits (about a quarter fewer), with the same
        // firing rules.
        int weak = Hits(BotPilot.AimSkill), full = Hits(1f);
        Assert.True(full > 50, $"only {full} hits with full skill, {weak} with less");
        Assert.True(weak > 0.62f * full && weak < 0.88f * full, $"{weak} hits, {full} with full skill");

        static int Hits(float skill)
        {
            int hits = 0;
            foreach ((float range, float speed) in new[] { (150f, 40f), (220f, 48f), (280f, 52f) })
            {
                for (int bot = 1; bot < Teams.PlaneCount; bot++)
                {
                    int enemy = Teams.IsAllies(bot) ? 5 : 1;
                    var rig = new Rig(BotPilot.EvasionSkill, skill);
                    rig.Set(FakeFlight.Plane(bot, new Vector3(0f, 500f, 0.3f * H), North), flown: true);
                    Vector3 course = Vector3.Transform(North, Quaternion.CreateFromAxisAngle(Vector3.UnitY, (bot % 2 == 0 ? 20f : -20f) * Deg));
                    rig.Set(FakeFlight.Plane(enemy, rig.States[bot].Position + North * range, course, speed: speed));
                    for (int i = 0; i < 60 * 15; i++)
                    {
                        rig.Step();
                        if (!rig.Input(bot).Fire)
                            continue;
                        PlaneState me = rig.States[bot], t = rig.States[enemy];
                        float flight = Vector3.Distance(t.Position, me.Position) / (BotPilot.BulletSpeed + me.Speed);
                        Vector3 then = t.Position + t.Velocity * flight - me.Position;
                        float along = Vector3.Dot(then, Nose(me));
                        if (along > 0f && (then - Nose(me) * along).Length() < 4f)
                            hits++;
                    }
                }
            }
            return hits;
        }
    }

    private static Rig Dogfight()
    {
        var rig = new Rig();
        for (int id = 0; id < Teams.PlaneCount; id++)
        {
            bool allies = Teams.IsAllies(id);
            int slot = id % Teams.TeamSize;
            rig.Set(FakeFlight.Plane(id, new Vector3(-90f + 60f * slot, 400f, (allies ? 0.75f : -0.75f) * H), allies ? North : -North),
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
                Assert.True(Clearance(rig.States[id]) > 40f, $"plane {id} down to {Clearance(rig.States[id])} m above the ground at {p}");
                Assert.True(Inside(p), $"plane {id} left the area at {p}");
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
            Assert.InRange(module.Pilot(id).AimErrorAngle, 1f / BotPilot.AimSkill * Deg, 2f / BotPilot.AimSkill * Deg);
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
    public void TheHostCanCreateIt()
    {
        // The game creates modules with their parameterless constructor.
        Assert.NotNull(typeof(BotsModule).GetConstructor(Type.EmptyTypes));
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
