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
    /// <summary>An enemy is on its tail: hard break turn toward its side, nose level or a little down.</summary>
    Break,
    /// <summary>An enemy is on its tail: diving extension (nose down for speed, then a level run or a turn).</summary>
    Extend,
    /// <summary>An enemy is on its tail, the bot high: split-S (roll inverted, pull through, level out the other way).</summary>
    SplitS,
    /// <summary>An enemy is on its tail, the bot high: roll-away dive (steep, descending spiral turn).</summary>
    RollAway,
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
    /// <summary>
    /// How good the bots are at evading, 1 = full (as before). Applied to the evasion only: its
    /// reaction (before it starts), the cooldown and the time between jinks are 2 - skill times
    /// longer; the evasion time, its stick inputs (bank, pull, roll, push) and the jinks are skill
    /// times as big. Aiming, firing and the choice of moves are not affected.
    /// </summary>
    public const float EvasionSkill = 0.75f;
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
    /// <summary>An enemy within this distance, in the rear cone and pointing at the bot, makes it evade.</summary>
    public const float ChaseRange = 350f;
    /// <summary>Cosine of the half-angle of the rear cone (60 degrees).</summary>
    public const float ChaseRearCos = 0.5f;
    /// <summary>Cosine of how far the chaser's nose may point away from the bot (45 degrees).</summary>
    public const float ChaserNoseCos = 0.707f;
    /// <summary>An evasion lasts at most this long, s (each bot ± <see cref="EvadeTimeSpread"/>), then the bot attacks again.</summary>
    public const float EvadeTime = 4f;
    public const float EvadeTimeSpread = 1f;
    /// <summary>After an evasion, no new one for this long, s (it attacks, or turns on its pursuer).</summary>
    public const float EvadeCooldown = 2f;
    /// <summary>The pursuer counts as shaken off once it is farther than this, m, or no longer pointing at the bot, for <see cref="ShakenTime"/>.</summary>
    public const float ShakeRange = 450f;
    public const float ShakenTime = 1f;
    /// <summary>A pursuer ahead of the bot's wing line within this distance has overshot, m: the bot turns on it.</summary>
    public const float OvershootRange = 500f;
    /// <summary>After an overshoot, the bot attacks the overshooter for this long, s, even if another enemy is nearer.</summary>
    public const float ReverseTime = 3f;
    /// <summary>Bank of a break turn (more when the nose rises, so the turn does not climb).</summary>
    public const float BreakBank = 80f * Deg;
    /// <summary>Nose angle held in a break turn (slightly down: trades a little height for speed).</summary>
    public const float BreakElevation = -3f * Deg;
    /// <summary>Never climbs below this speed, m/s: the allowed climb grows 2 degrees per m/s above it, and below it the nose goes down (up to <see cref="SlowDiveAngle"/>).</summary>
    public const float MinClimbSpeed = 40f;
    public const float SlowDiveAngle = 10f * Deg;
    /// <summary>Above this altitude, m, it does not climb and evades by diving (Flight's ceiling is 1000 m).</summary>
    public const float HighAltitude = 800f;
    /// <summary>A split-S or roll-away needs this altitude, m; a diving extension <see cref="DiveRoomAltitude"/>.</summary>
    public const float SplitSAltitude = 500f;
    public const float DiveRoomAltitude = 280f;
    /// <summary>Dives of an evasion level off this far above <see cref="MinAltitude"/>, m.</summary>
    public const float DiveFloorMargin = 100f;
    /// <summary>Diving extension: dive angle, and the speed at which it levels out (or after <see cref="ExtendDiveTime"/>), m/s.</summary>
    public const float ExtendDiveAngle = 30f * Deg;
    public const float ExtendSpeed = 56f;
    public const float ExtendDiveTime = 3f;
    /// <summary>Roll-away dive: the nose angle it holds.</summary>
    public const float RollAwayElevation = -35f * Deg;
    /// <summary>Jinks while being shot at (pursuer within <see cref="FireRange"/>, its nose within <see cref="JinkCone"/> of the bot): new random pitch and roll offsets every <see cref="JinkPeriod"/> s.</summary>
    public const float JinkCone = 10f * Deg;
    public const float JinkPeriod = 0.45f;
    public const float JinkPitch = 0.35f;
    public const float JinkRoll = 0.5f;
    /// <summary>Share of break turns among the evasions at mid altitude and good speed (each bot ± <see cref="BreakShareSpread"/>).</summary>
    public const float BreakShare = 0.5f;
    public const float BreakShareSpread = 0.15f;
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
    private readonly float _evadeLimit;
    private readonly float _breakShare;
    private readonly float _evasionSkill;
    private readonly float _evadeReaction;
    private readonly float _evadeCooldown;

    private Vector3 _seenAim;
    private bool _hasSeenAim;
    private float _chaseTime;
    private float _burstClock;

    // Evasion: the move, its pursuer, side, phase and clocks.
    private BotMode _evade;
    private int _chaser = -1;
    private float _side;
    private int _evadePhase;
    private float _evadeTime;
    private float _phaseTime;
    private float _shakenTime;
    private float _overshootTime;
    private float _cooldown;
    private int _encounter;
    private bool _extendTurn;
    private int _reverseTarget = -1;
    private float _reverseTime;
    private float _jinkClock;
    private int _jinkCount;
    private float _jinkPitch, _jinkRoll;

    /// <param name="evasionSkill">How good it is at evading (see <see cref="EvasionSkill"/>).</param>
    public BotPilot(int planeId, float evasionSkill = EvasionSkill)
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
        _evasionSkill = evasionSkill;
        float slower = 2f - evasionSkill; // 1.25 at 0.75: reaction, cooldown and jink period 25% longer
        _evadeReaction = _reaction * slower;
        _evadeCooldown = EvadeCooldown * slower;
        _evadeLimit = (EvadeTime + EvadeTimeSpread * Spread(planeId, 7)) * evasionSkill;
        _breakShare = BreakShare + BreakShareSpread * Spread(planeId, 8);
    }

    public int PlaneId => _id;

    /// <summary>This bot's reaction delay, s.</summary>
    public float Reaction => _reaction;

    /// <summary>How long a chaser must sit on its tail before it evades, s (its reaction, slower when <see cref="EvasionSkill"/> is below 1).</summary>
    public float EvadeReaction => _evadeReaction;

    /// <summary>This bot's longest evasion, s.</summary>
    public float EvadeLimit => _evadeLimit;

    /// <summary>Attacking time after an evasion before the next one, s.</summary>
    public float Cooldown => _evadeCooldown;

    /// <summary>This bot's aim error, radians.</summary>
    public float AimErrorAngle => _aimError;

    /// <summary>What it did on the last <see cref="Fly"/>.</summary>
    public BotMode Mode { get; private set; }

    /// <summary>The plane it attacked on the last <see cref="Fly"/>, or -1.</summary>
    public int TargetId { get; private set; } = -1;

    /// <summary>The pursuer it is evading, or -1.</summary>
    public int ChaserId => _evade != BotMode.None ? _chaser : -1;

    /// <summary>True when it jinked on the last <see cref="Fly"/> (evading while shot at).</summary>
    public bool Jinking { get; private set; }

    /// <summary>The current jink offsets added to the pitch and roll stick while <see cref="Jinking"/>.</summary>
    public float JinkPitchOffset => _jinkPitch;
    public float JinkRollOffset => _jinkRoll;

    /// <summary>Evasions started so far (each picks its move afresh).</summary>
    public int Encounters => _encounter;

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

        // Reaction delay: the chaser must stay there a moment before the bot evades.
        if (chaser >= 0)
            _chaseTime += dt;
        else
            _chaseTime = 0f;
        if (_cooldown > 0f)
            _cooldown -= dt;
        if (_reverseTime > 0f)
            _reverseTime -= dt;
        if (_evade != BotMode.None)
            UpdateEvasion(states, known, in me, forward, dt);
        else if (chaser >= 0 && _chaseTime >= _evadeReaction && _cooldown <= 0f)
            StartEvasion(chaser, in states[chaser], in me);

        // Turning on an overshooter: it stays the target for a while.
        if (_reverseTime > 0f && _reverseTarget >= 0 && known[_reverseTarget] && states[_reverseTarget].Alive)
        {
            target = _reverseTarget;
            best = Vector3.Distance(states[target].Position, me.Position);
        }

        TargetId = target;
        Jinking = false;
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
            Steer(LimitClimb(home, in me), in me, out pitch, out roll, out yaw);
        }
        else if (_evade != BotMode.None)
        {
            Mode = _evade;
            throttle = 1f;
            Evade(in me, forward, out pitch, out roll, out yaw);
            if (_evade != BotMode.SplitS && UnderFire(in states[_chaser], in me))
            {
                // Jinks: short random pitch and roll changes, to spoil the shooter's aim.
                Jinking = true;
                _jinkClock -= dt;
                if (_jinkClock <= 0f)
                {
                    _jinkClock = JinkPeriod * (2f - _evasionSkill) * (0.7f + 0.3f * (Spread(_id, 40 + _jinkCount) + 1f));
                    _jinkPitch = JinkPitch * _evasionSkill * Spread(_id, 1000 + _jinkCount);
                    _jinkRoll = JinkRoll * _evasionSkill * Spread(_id, 2000 + _jinkCount);
                    _jinkCount++;
                }
                pitch += _jinkPitch;
                roll += _jinkRoll;
            }
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
            Steer(LimitClimb(aim, in me), in me, out pitch, out roll, out yaw);

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
            Steer(LimitClimb(CirclePoint(me.Position) - me.Position, in me), in me, out pitch, out roll, out yaw);
        }

        if (Mode != BotMode.Ground)
            GuardClimb(in me, forward, ref pitch, ref roll);
        if (target < 0)
            _hasSeenAim = false;
        return new ControlInput(_id, pitch, roll, yaw, throttle, fire, false, Vector3.Zero);
    }

    /// <summary>Clears the per-life memory (after the plane went down).</summary>
    public void Reset()
    {
        _hasSeenAim = false;
        _chaseTime = 0f;
        _evade = BotMode.None;
        _chaser = -1;
        _cooldown = 0f;
        _reverseTarget = -1;
        _reverseTime = 0f;
        Jinking = false;
        Mode = BotMode.None;
        TargetId = -1;
    }

    /// <summary>Starts an evasion of <paramref name="chaser"/>: picks the move for this bot and encounter.</summary>
    private void StartEvasion(int chaser, in PlaneState c, in PlaneState me)
    {
        Vector3 local = Local(c.Position - me.Position, me.Orientation);
        // Toward the attacker's side (to spoil its aim); dead astern, this bot's habit, alternating.
        _side = MathF.Abs(local.X) > 5f ? MathF.Sign(local.X) : _breakSidePreference * ((_encounter & 1) == 0 ? 1f : -1f);
        float r = (Spread(_id, 100 + _encounter) + 1f) * 0.5f;
        _extendTurn = Spread(_id, 300 + _encounter) >= 0f;
        _encounter++;

        float alt = me.Position.Y;
        bool slow = me.Speed < MinClimbSpeed;
        if (alt > HighAltitude)
            _evade = r < 0.4f ? BotMode.SplitS : r < 0.75f ? BotMode.RollAway : BotMode.Extend;
        else if (slow)
            _evade = alt > SplitSAltitude && r > 0.6f ? BotMode.RollAway : alt > DiveRoomAltitude ? BotMode.Extend : BotMode.Break;
        else if (r < _breakShare)
            _evade = BotMode.Break;
        else if (alt > SplitSAltitude && r > 0.85f)
            _evade = r > 0.93f ? BotMode.SplitS : BotMode.RollAway;
        else
            _evade = alt > DiveRoomAltitude ? BotMode.Extend : BotMode.Break;

        _chaser = chaser;
        _evadePhase = 0;
        _evadeTime = 0f;
        _phaseTime = 0f;
        _shakenTime = 0f;
        _overshootTime = 0f;
        _jinkClock = 0f;
    }

    /// <summary>Ends the evasion after its time, once the pursuer is shaken off, or when it overshoots (then turns on it).</summary>
    private void UpdateEvasion(PlaneState[] states, bool[] known, in PlaneState me, Vector3 forward, float dt)
    {
        _evadeTime += dt;
        _phaseTime += dt;
        if (!known[_chaser] || !states[_chaser].Alive)
        {
            EndEvasion(-1);
            return;
        }
        ref readonly PlaneState c = ref states[_chaser];
        Vector3 rel = c.Position - me.Position;
        float d = rel.Length();
        Vector3 chaserNose = Vector3.Transform(-Vector3.UnitZ, c.Orientation);
        bool pointing = d > 1e-3f && Vector3.Dot(chaserNose, -rel / d) > ChaseRearCos;

        // Overshoot: the pursuer is now ahead of the bot's wing line (seen after the reaction delay).
        if (d < OvershootRange && Vector3.Dot(rel, forward) > 0f)
            _overshootTime += dt;
        else
            _overshootTime = 0f;
        if (_overshootTime >= _reaction)
        {
            EndEvasion(_chaser);
            return;
        }

        if (d > ShakeRange || !pointing)
            _shakenTime += dt;
        else
            _shakenTime = 0f;
        if (_shakenTime >= ShakenTime || _evadeTime >= _evadeLimit)
            EndEvasion(-1);
    }

    private void EndEvasion(int overshooter)
    {
        _evade = BotMode.None;
        _cooldown = _evadeCooldown;
        _chaseTime = 0f;
        if (overshooter >= 0)
        {
            _reverseTarget = overshooter;
            _reverseTime = ReverseTime;
        }
    }

    private bool UnderFire(in PlaneState c, in PlaneState me)
    {
        Vector3 rel = me.Position - c.Position;
        float d = rel.Length();
        if (d >= FireRange || d < 1e-3f)
            return false;
        return Vector3.Dot(Vector3.Transform(-Vector3.UnitZ, c.Orientation), rel / d) > MathF.Cos(JinkCone);
    }

    /// <summary>The stick for the current evasion move.</summary>
    private void Evade(in PlaneState me, Vector3 forward, out float pitch, out float roll, out float yaw)
    {
        float elev = MathF.Asin(Math.Clamp(forward.Y, -1f, 1f));
        float bank = Bank(me.Orientation);
        bool lowDive = me.Position.Y < MinAltitude + DiveFloorMargin;
        yaw = 0f;
        switch (_evade)
        {
            case BotMode.Extend:
                if (_evadePhase == 0 && (me.Speed >= ExtendSpeed && me.Position.Y < HighAltitude || lowDive || _phaseTime >= ExtendDiveTime))
                    NextPhase();
                if (_evadePhase == 0)
                {
                    Level(-ExtendDiveAngle, elev, bank, out pitch, out roll); // nose down, wings level
                    if (MathF.Abs(bank) < 90f * Deg)
                        pitch = MathF.Max(pitch, -_evasionSkill); // a softer push (rolling out of a steep bank: as before)
                }
                else if (_extendTurn)
                    Turn(60f * Deg, BreakElevation, 0.6f, in me, elev, bank, out pitch, out roll);
                else
                    Level(0f, elev, bank, out pitch, out roll); // the run
                break;
            case BotMode.SplitS:
                // 0: roll inverted; 1: pull until well down; 2: pull through to level; 3: level run.
                if (_evadePhase == 0 && MathF.Abs(bank) > 150f * Deg)
                    NextPhase();
                else if (_evadePhase == 1 && elev < -60f * Deg)
                    NextPhase();
                else if (_evadePhase == 2 && (elev > -8f * Deg || lowDive && elev > -30f * Deg))
                    NextPhase();
                if (_evadePhase == 0)
                {
                    roll = Math.Clamp(WrapAngle(_side * MathF.PI - bank) * 3f, -_evasionSkill, _evasionSkill);
                    roll = roll == 0f ? _side * _evasionSkill : roll;
                    pitch = 0f;
                }
                else if (_evadePhase < 3)
                {
                    pitch = _evasionSkill;
                    roll = 0f;
                }
                else
                    Level(0f, elev, bank, out pitch, out roll);
                break;
            case BotMode.RollAway:
                Turn(BreakBank, lowDive ? 0f : RollAwayElevation, 1f, in me, elev, bank, out pitch, out roll);
                break;
            default:
                Turn(BreakBank, BreakElevation, 1f, in me, elev, bank, out pitch, out roll);
                break;
        }
    }

    private void NextPhase()
    {
        _evadePhase++;
        _phaseTime = 0f;
    }

    /// <summary>
    /// A hard turn toward <see cref="_side"/>: bank <paramref name="baseBank"/>, more when the
    /// nose is above <paramref name="elevation"/> (so the pull brings it down), less when below;
    /// <paramref name="pull"/> once banked. Never holds a climb when slow.
    /// </summary>
    private void Turn(float baseBank, float elevation, float pull, in PlaneState me, float elev, float bank, out float pitch, out float roll)
    {
        if (me.Speed < MinClimbSpeed)
            elevation = MathF.Min(elevation, -SlowDiveAngle);
        if (me.Position.Y < MinAltitude + DiveFloorMargin)
            elevation = MathF.Max(elevation, 0f);
        float extra = Math.Clamp((elev - elevation) * 8f, -25f * Deg, 70f * Deg);
        // Softer bank (evasion skill), the full one back once the nose is 1 degree above the elevation: never a climbing turn.
        float softer = baseBank * (1f - _evasionSkill) * Math.Clamp(1f - (elev - elevation) / (1f * Deg), 0f, 1f);
        float error = WrapAngle(_side * (baseBank - softer + extra) - bank);
        // Softer roll (evasion skill), back to full as the nose rises above the elevation.
        float rollLimit = Math.Clamp(_evasionSkill + (elev - elevation) / (10f * Deg), _evasionSkill, 1f);
        roll = Math.Clamp(error * 3f, -rollLimit, rollLimit);
        pitch = pull * _evasionSkill * Math.Clamp(1.5f - MathF.Abs(error) / (60f * Deg), 0.3f, 1f); // full within 30 degrees of the bank
    }

    /// <summary>Wings level, nose toward <paramref name="elevation"/>.</summary>
    private static void Level(float elevation, float elev, float bank, out float pitch, out float roll)
    {
        roll = Math.Clamp(-bank * 3f, -1f, 1f);
        pitch = Math.Clamp((elevation - elev) * 4f, -1f, 1f);
    }

    /// <summary>
    /// No climbing when slow or high: below <see cref="MinClimbSpeed"/> the direction is
    /// brought down (a dive of up to <see cref="SlowDiveAngle"/>), above it the climb may grow
    /// 2 degrees per m/s; above <see cref="HighAltitude"/> at most level.
    /// </summary>
    private static Vector3 LimitClimb(Vector3 direction, in PlaneState me)
    {
        float flat = MathF.Sqrt(direction.X * direction.X + direction.Z * direction.Z);
        float maxElev = MaxClimb(in me);
        if (maxElev >= 80f * Deg || flat < 1e-3f)
            return direction;
        float maxY = flat * MathF.Tan(maxElev);
        if (direction.Y > maxY)
            direction.Y = maxY;
        return direction;
    }

    /// <summary>The steepest climb allowed now: 2 degrees per m/s above <see cref="MinClimbSpeed"/> (a dive below it), at most level when high.</summary>
    private static float MaxClimb(in PlaneState me)
    {
        float maxElev = Math.Clamp((me.Speed - MinClimbSpeed) * 2f * Deg, -SlowDiveAngle, 80f * Deg);
        return me.Position.Y > HighAltitude ? MathF.Min(maxElev, 0f) : maxElev;
    }

    /// <summary>
    /// The nose above <see cref="MaxClimb"/> (say, pulling up toward a target behind): wings
    /// near level, push it down; banked, bank past 90 degrees so the pull brings it down.
    /// </summary>
    private static void GuardClimb(in PlaneState me, Vector3 forward, ref float pitch, ref float roll)
    {
        float elev = MathF.Asin(Math.Clamp(forward.Y, -1f, 1f));
        float maxElev = MaxClimb(in me);
        if (elev <= maxElev + 3f * Deg)
            return;
        float bank = Bank(me.Orientation);
        if (MathF.Abs(bank) <= 45f * Deg)
            pitch = MathF.Min(pitch, Math.Clamp((maxElev - elev) * 4f, -1f, 1f));
        else if (MathF.Abs(bank) < 110f * Deg)
            roll = Math.Clamp(WrapAngle(MathF.Sign(bank) * 120f * Deg - bank) * 3f, -1f, 1f);
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
        // Far behind: turn around to one side (a level turn), not over the top in a loop.
        if (local.Z > 0.5f && MathF.Abs(local.X) < 0.3f)
            local.X = local.X >= 0f ? 0.3f : -0.3f;

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
