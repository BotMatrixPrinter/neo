using System;
using System.Numerics;
using Neo.Flight;
using Neo.Input;

namespace Neo.Bots;

/// <summary>What a bot is doing this step (for tests and debugging).</summary>
public enum BotMode
{
    /// <summary>No input this step (plane down or unknown).</summary>
    None,
    /// <summary>No enemy alive: circling near the center.</summary>
    Circle,
    /// <summary>Chasing the nearest enemy, firing when lined up.</summary>
    Attack,
    /// <summary>An enemy is on its tail: hard break turn.</summary>
    Break,
    /// <summary>Too close to the edge of the area: turning back toward the center.</summary>
    Edge,
    /// <summary>Too low: climbing.</summary>
    Ground,
}

/// <summary>
/// The brain of one bot plane: reads plane states, flies with the stick (pitch, roll, yaw,
/// throttle, fire), like a pilot. Easy difficulty: a short reaction delay and some aim
/// error, varied per bot. Deterministic; no allocations in <see cref="Fly"/>.
/// </summary>
public sealed class BotPilot
{
    private const float Deg = MathF.PI / 180f;

    // Tunables (see README).
    /// <summary>Bullet speed used for the lead point, m/s (plus the bot's own speed).</summary>
    public const float BulletSpeed = 450f;
    /// <summary>Fires only when the target is closer than this, m.</summary>
    public const float FireRange = 350f;
    /// <summary>Fires only when the lead point is within this angle of the nose.</summary>
    public const float FireCone = 6f * Deg;
    /// <summary>Seconds of fire in each burst, then <see cref="BurstPause"/> seconds off.</summary>
    public const float BurstLength = 0.6f;
    public const float BurstPause = 0.5f;
    /// <summary>Reaction delay, s: the time constant of the bot's view of its target, and how long an enemy must sit on its tail before it breaks. Each bot uses this ± <see cref="ReactionSpread"/>.</summary>
    public const float ReactionTime = 0.3f;
    public const float ReactionSpread = 0.1f;
    /// <summary>Aim error, radians, wandering slowly around the lead point. Each bot uses this ± <see cref="AimErrorSpread"/>.</summary>
    public const float AimError = 1.5f * Deg;
    public const float AimErrorSpread = 0.5f * Deg;
    /// <summary>An enemy within this distance, in the rear cone and pointing at the bot, makes it break.</summary>
    public const float ChaseRange = 300f;
    /// <summary>Cosine of the half-angle of the rear cone (60 degrees).</summary>
    public const float ChaseRearCos = 0.5f;
    /// <summary>Cosine of how far the chaser's nose may point away from the bot (45 degrees).</summary>
    public const float ChaserNoseCos = 0.707f;
    /// <summary>A break lasts at least this long, s.</summary>
    public const float BreakMinTime = 1.2f;
    /// <summary>Bank of a break turn.</summary>
    public const float BreakBank = 80f * Deg;
    /// <summary>Climbs when altitude plus <see cref="GroundLookahead"/> seconds of descent is below this, m.</summary>
    public const float MinAltitude = 80f;
    public const float GroundLookahead = 3f;
    /// <summary>Climb angle when too low.</summary>
    public const float ClimbAngle = 30f * Deg;
    /// <summary>Half size of the square area (Flight's), m.</summary>
    public const float AreaHalfSize = 2000f;
    /// <summary>Turns back toward the center within this distance of an edge when heading out, m.</summary>
    public const float EdgeMargin = 600f;
    /// <summary>Without a target: circles the center at this radius and altitude, m.</summary>
    public const float CircleRadius = 700f;
    public const float CircleAltitude = 400f;
    /// <summary>Within this angle of the aim point the bot fine-aims with pitch and yaw instead of banking.</summary>
    public const float FineAngle = 12f * Deg;
    /// <summary>Throttle when attacking, when close behind the target (to not overshoot) and when circling.</summary>
    public const float AttackThrottle = 1f;
    /// <summary>Each bot's attack throttle is up to this much lower, so a team spreads out.</summary>
    public const float AttackThrottleSpread = 0.2f;
    public const float CloseThrottle = 0.6f;
    public const float CloseRange = 150f;
    public const float CircleThrottle = 0.6f;

    private readonly int _id;
    private readonly bool _allies;
    private readonly float _reaction;
    private readonly float _aimError;
    private readonly float _phase;
    private readonly float _breakSidePreference;
    private readonly float _attackThrottle;

    private Vector3 _seenAim;
    private bool _hasSeenAim;
    private float _chaseTime;
    private float _breakTime;
    private float _breakSide;
    private float _burstClock;

    public BotPilot(int planeId)
    {
        _id = planeId;
        _allies = Teams.IsAllies(planeId);
        _reaction = ReactionTime + ReactionSpread * Spread(planeId, 1);
        _aimError = AimError + AimErrorSpread * Spread(planeId, 2);
        _phase = (Spread(planeId, 3) + 1f) * MathF.PI;
        _breakSidePreference = Spread(planeId, 4) >= 0f ? 1f : -1f;
        _attackThrottle = AttackThrottle - AttackThrottleSpread * (Spread(planeId, 6) + 1f) * 0.5f;
        // Each bot starts its burst cycle at a different point.
        _burstClock = (Spread(planeId, 5) + 1f) * 0.5f * (BurstLength + BurstPause);
    }

    public int PlaneId => _id;

    /// <summary>This bot's reaction delay, s.</summary>
    public float Reaction => _reaction;

    /// <summary>This bot's aim error, radians.</summary>
    public float AimErrorAngle => _aimError;

    /// <summary>What it did on the last <see cref="Fly"/>.</summary>
    public BotMode Mode { get; private set; }

    /// <summary>The plane it attacked on the last <see cref="Fly"/>, or -1.</summary>
    public int TargetId { get; private set; } = -1;

    /// <summary>
    /// One step: <paramref name="states"/>/<paramref name="known"/> are the latest state of
    /// each plane 0..7; the bot's own must be known and alive. <paramref name="time"/> is the
    /// simulated time, s.
    /// </summary>
    public ControlInput Fly(PlaneState[] states, bool[] known, float time, float dt)
    {
        ref readonly PlaneState me = ref states[_id];
        Vector3 forward = Vector3.Transform(-Vector3.UnitZ, me.Orientation);
        _burstClock += dt;
        if (_burstClock >= BurstLength + BurstPause)
            _burstClock -= BurstLength + BurstPause;

        // The nearest alive enemy, and whether one sits on our tail.
        int target = -1;
        float best = float.MaxValue;
        int chaser = -1;
        for (int i = 0; i < states.Length; i++)
        {
            if (!known[i] || !states[i].Alive || Teams.IsAllies(i) == _allies)
                continue;
            Vector3 rel = states[i].Position - me.Position;
            float d = rel.Length();
            if (d < best)
            {
                best = d;
                target = i;
            }
            if (d < ChaseRange && d > 1e-3f)
            {
                Vector3 toEnemy = rel / d;
                Vector3 enemyNose = Vector3.Transform(-Vector3.UnitZ, states[i].Orientation);
                if (Vector3.Dot(toEnemy, forward) < -ChaseRearCos && Vector3.Dot(enemyNose, -toEnemy) > ChaserNoseCos)
                    chaser = i;
            }
        }

        // Reaction delay on breaking: the chaser must stay there a moment. While it stays,
        // the break goes on; after it is gone, the break lasts BreakMinTime more at most.
        if (_breakTime > 0f)
            _breakTime -= dt;
        if (chaser >= 0)
        {
            _chaseTime += dt;
            if (_chaseTime >= _reaction)
            {
                if (_breakTime <= 0f)
                {
                    // Turn into the attacker: toward its side, else this bot's habit.
                    Vector3 local = Local(states[chaser].Position - me.Position, me.Orientation);
                    _breakSide = MathF.Abs(local.X) > 5f ? MathF.Sign(local.X) : _breakSidePreference;
                }
                _breakTime = BreakMinTime;
            }
        }
        else
            _chaseTime = 0f;

        TargetId = target;
        float pitch, roll, yaw;
        float throttle = _attackThrottle;
        bool fire = false;

        if (NeedsClimb(in me, forward, out Vector3 climb))
        {
            Mode = BotMode.Ground;
            Steer(climb, in me, out pitch, out roll, out yaw);
        }
        else if (NeedsTurnBack(in me, forward, out Vector3 home))
        {
            Mode = BotMode.Edge;
            Steer(home, in me, out pitch, out roll, out yaw);
        }
        else if (_breakTime > 0f)
        {
            Mode = BotMode.Break;
            throttle = 1f;
            float error = WrapAngle(_breakSide * BreakBank - Bank(me.Orientation));
            roll = Math.Clamp(error * 3f, -1f, 1f);
            pitch = MathF.Abs(error) < 30f * Deg ? 1f : 0.4f;
            yaw = 0f;
        }
        else if (target >= 0)
        {
            Mode = BotMode.Attack;
            ref readonly PlaneState t = ref states[target];
            // Lead point: where the target will be when the bullets get there.
            float bulletSpeed = BulletSpeed + me.Speed;
            float flight = best / bulletSpeed;
            Vector3 lead = t.Position + t.Velocity * flight;
            flight = Vector3.Distance(lead, me.Position) / bulletSpeed;
            lead = t.Position + t.Velocity * flight;

            // Aim error: a small offset wandering slowly around the lead point.
            float dist = Vector3.Distance(lead, me.Position);
            Vector3 wobble = new(MathF.Sin(time * 0.9f + _phase), MathF.Sin(time * 1.3f + 2f * _phase), MathF.Cos(time * 0.7f + _phase));
            lead += wobble * (dist * MathF.Tan(_aimError) * 0.58f); // 0.58: a unit-ish wobble of three axes

            // Reaction delay: the bot's idea of the aim point lags the real one.
            if (!_hasSeenAim)
            {
                _seenAim = lead;
                _hasSeenAim = true;
            }
            else
                _seenAim += (lead - _seenAim) * MathF.Min(1f, dt / _reaction);

            Vector3 aim = _seenAim - me.Position;
            Steer(aim, in me, out pitch, out roll, out yaw);

            float aimLength = aim.Length();
            bool linedUp = aimLength > 1e-3f && Vector3.Dot(aim / aimLength, forward) > MathF.Cos(FireCone);
            bool burst = _burstClock < BurstLength;
            fire = linedUp && best < FireRange && burst;
            if (best < CloseRange && Vector3.Dot(t.Position - me.Position, forward) > 0f)
                throttle = CloseThrottle;
        }
        else
        {
            Mode = BotMode.Circle;
            throttle = CircleThrottle;
            Steer(CirclePoint(me.Position) - me.Position, in me, out pitch, out roll, out yaw);
        }

        if (target < 0)
            _hasSeenAim = false;
        return new ControlInput(_id, pitch, roll, yaw, throttle, fire, false, Vector3.Zero);
    }

    /// <summary>Clears the per-life memory (after the plane went down).</summary>
    public void Reset()
    {
        _hasSeenAim = false;
        _chaseTime = 0f;
        _breakTime = 0f;
        Mode = BotMode.None;
        TargetId = -1;
    }

    private static bool NeedsClimb(in PlaneState me, Vector3 forward, out Vector3 climb)
    {
        float predicted = me.Position.Y + MathF.Min(0f, me.Velocity.Y) * GroundLookahead;
        climb = default;
        if (predicted >= MinAltitude)
            return false;
        Vector3 flat = new(forward.X, 0f, forward.Z);
        if (flat.LengthSquared() < 1e-4f)
            flat = -Vector3.UnitZ;
        climb = Vector3.Normalize(flat) * MathF.Cos(ClimbAngle) + Vector3.UnitY * MathF.Sin(ClimbAngle);
        return true;
    }

    private static bool NeedsTurnBack(in PlaneState me, Vector3 forward, out Vector3 home)
    {
        float limit = AreaHalfSize - EdgeMargin;
        Vector3 p = me.Position;
        bool outX = MathF.Abs(p.X) > limit && p.X * forward.X > 0f;
        bool outZ = MathF.Abs(p.Z) > limit && p.Z * forward.Z > 0f;
        home = default;
        if (!outX && !outZ)
            return false;
        Vector3 flat = new(-p.X, 0f, -p.Z);
        home = Vector3.Normalize(flat);
        // Ease toward a sensible altitude while turning back.
        home.Y = Math.Clamp((CircleAltitude - p.Y) / 1000f, -0.2f, 0.2f);
        return true;
    }

    private static Vector3 CirclePoint(Vector3 p)
    {
        float r = MathF.Sqrt(p.X * p.X + p.Z * p.Z);
        if (r > 2f * CircleRadius)
            return new Vector3(0f, CircleAltitude, 0f);
        float a = MathF.Atan2(p.Z, p.X) + 0.6f;
        return new Vector3(MathF.Cos(a) * CircleRadius, CircleAltitude, MathF.Sin(a) * CircleRadius);
    }

    /// <summary>
    /// Stick toward a world direction: far off the nose, bank so the target is "above" the
    /// cockpit and pull; near the nose, fine-aim with pitch and yaw.
    /// </summary>
    private static void Steer(Vector3 direction, in PlaneState me, out float pitch, out float roll, out float yaw)
    {
        Vector3 local = Local(direction, me.Orientation);
        float length = local.Length();
        if (length < 1e-4f)
        {
            pitch = roll = yaw = 0f;
            return;
        }
        local /= length;
        float off = MathF.Acos(Math.Clamp(-local.Z, -1f, 1f));

        // Bank and pull: roll the lift vector onto the target, pull once it is about there.
        float liftAngle = MathF.Atan2(local.X, local.Y); // 0: straight "up" in the cockpit; positive: to the right
        float bankRoll = Math.Clamp(liftAngle * 2.5f, -1f, 1f);
        float bankPitch = MathF.Max(0f, MathF.Cos(liftAngle));
        float bankYaw = Math.Clamp(local.X * 1.5f, -1f, 1f) * 0.5f;

        // Fine aim: small corrections with pitch and yaw, roll fading out.
        float ahead = MathF.Max(-local.Z, 1e-3f);
        float finePitch = Math.Clamp(MathF.Atan2(local.Y, ahead) * 4f, -1f, 1f);
        float fineYaw = Math.Clamp(MathF.Atan2(local.X, ahead) * 4f, -1f, 1f);
        float fineRoll = bankRoll * Math.Clamp(off / FineAngle, 0f, 1f);

        float w = Math.Clamp((off - 0.5f * FineAngle) / (0.5f * FineAngle), 0f, 1f);
        pitch = finePitch + (bankPitch - finePitch) * w;
        roll = fineRoll + (bankRoll - fineRoll) * w;
        yaw = fineYaw + (bankYaw - fineYaw) * w;
    }

    private static Vector3 Local(Vector3 world, Quaternion orientation)
        => Vector3.Transform(world, Quaternion.Conjugate(orientation));

    /// <summary>Bank angle, positive rolled right.</summary>
    private static float Bank(Quaternion orientation)
    {
        Vector3 right = Vector3.Transform(Vector3.UnitX, orientation);
        Vector3 up = Vector3.Transform(Vector3.UnitY, orientation);
        return MathF.Atan2(-right.Y, up.Y);
    }

    private static float WrapAngle(float a)
    {
        if (a > MathF.PI) a -= 2f * MathF.PI;
        else if (a < -MathF.PI) a += 2f * MathF.PI;
        return a;
    }

    /// <summary>A fixed pseudo-random number in -1..1 for this plane and salt (deterministic).</summary>
    private static float Spread(int planeId, int salt)
    {
        uint h = (uint)(planeId * 73856093) ^ (uint)(salt * 19349663);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return (h & 0xFFFF) / 32767.5f - 1f;
    }
}
