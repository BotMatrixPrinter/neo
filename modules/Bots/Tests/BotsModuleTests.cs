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

    [Fact]
    public void BreaksTowardTheAttackersSide()
    {
        // Every bot, chased from the right rear and from the left rear at 400 m and 50 m/s:
        // whenever it picks a break, it rolls hard toward the attacker and pulls once banked.
        int breaks = 0;
        foreach (float sign in new[] { 1f, -1f })
        {
            for (int bot = 1; bot < Teams.PlaneCount; bot++)
            {
                int enemy = Teams.IsAllies(bot) ? 5 : 1;
                var rig = new Rig();
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
                Assert.True(rig.Input(bot).Roll * sign > 0.4f, $"bot {bot} rolls {rig.Input(bot).Roll}, attacker on side {sign}"); // full roll, give or take a jink
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
                Assert.True(most > MathF.Sin(70f * Deg), $"bot {bot} banked at most {MathF.Asin(most) / Deg} deg toward side {sign}");
                Assert.True(least > -MathF.Sin(10f * Deg), $"bot {bot} banked away from side {sign}");
                Assert.True(pull > 0.5f, $"bot {bot} pulls {pull} on average");
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
                rig.Set(FakeFlight.Plane(1, new Vector3(0f, 600f, -1500f), North)); // gone: shaken off, then the cooldown
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
            for (int i = 0; i < 60 * 3; i++)
            {
                Stick(rig, 1, bot);
                rig.Step();
                if (rig.States[bot].Speed < BotPilot.MinClimbSpeed - 1f)
                    Assert.True(rig.States[bot].Velocity.Y < 2f, $"bot {bot} climbs at {rig.States[bot].Speed} m/s");
            }
            Assert.Equal(BotMode.Break, rig.Module.Pilot(bot).Mode);
        }
    }

    [Fact]
    public void AChasedBotNeverKeepsClimbingAndStaysSafe()
    {
        // A pursuer glued to the tail (it can't be shaken off) for 40 s, from mid altitude,
        // high, near the ceiling, low, and slow: the bot never spirals upward, never climbs
        // while slow, stays above 80 m and inside the area. Each bot, from either side.
        foreach ((float alt, float speed) in new[] { (400f, 50f), (900f, 45f), (990f, 30f), (150f, 42f), (100f, 30f), (500f, 32f) })
        {
            for (int bot = 1; bot < Teams.PlaneCount; bot++)
            {
                int enemy = Teams.IsAllies(bot) ? 5 : 1;
                var rig = new Rig();
                rig.Set(FakeFlight.Plane(bot, new Vector3(300f, alt, 800f), North, speed: speed), flown: true);
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
                    Assert.True(s.Position.Y > BotPilot.MinAltitude, $"{at}: down to {s.Position.Y} m");
                    Assert.True(MathF.Abs(s.Position.X) < 2000f && MathF.Abs(s.Position.Z) < 2000f, $"{at}: left the area at {s.Position}");
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
        Assert.True(longest <= BotPilot.EvadeTime + BotPilot.EvadeTimeSpread + 0.1f, $"evaded {longest} s on end");
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
        shaken.Set(FakeFlight.Plane(1, new Vector3(0f, 400f, -1200f), North));
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
        rig.Set(FakeFlight.Plane(1, new Vector3(15f, 405f, 90f), North, speed: 60f));
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
            if (overshot >= 0 && i > overshot + 60)
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
            for (int i = 0; i < 90; i++)
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
