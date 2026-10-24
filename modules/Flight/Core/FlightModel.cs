using System;
using System.Numerics;
using Neo.Input;

namespace Neo.Flight;

/// <summary>
/// Arcade flight of one plane: not a simulation. The plane always flies along its nose at
/// a speed between <see cref="MinSpeed"/> and <see cref="DiveMaxSpeed"/> (no stall, no spin; the
/// throttle sets the level speed, gravity along the flight path slows climbs and speeds up dives),
/// turns at fixed body rates, and is kept above <see cref="FloorAltitude"/> and inside the
/// <see cref="AreaHalfSize"/> square by steering it gently away. No hard ceiling: the engine loses
/// power as the plane climbs, until at <see cref="PowerAltitude"/> full throttle only holds level
/// flight, so climbs level off there by themselves. Damage slows it, drags it into a roll and weakens its tail; a downed plane falls
/// to the ground until it respawns.
/// Deterministic; Step does not allocate.
/// </summary>
public sealed class FlightModel
{
    public const float MinSpeed = 25f;
    public const float CruiseSpeed = 42f;
    public const float MaxSpeed = 60f;
    /// <summary>Throttle that holds <see cref="CruiseSpeed"/> in level flight.</summary>
    public const float CruiseThrottle = 0.5f;

    public const float RollRate = 180f * Deg;
    public const float PitchRate = 70f * Deg;
    public const float YawRate = 40f * Deg;
    /// <summary>Extra heading change per second from a 90 degree bank, so a banked plane turns.</summary>
    public const float BankTurnRate = 30f * Deg;
    /// <summary>How fast the control rates follow their commands (1/s): snappy but not instant.</summary>
    public const float ControlResponse = 15f;
    /// <summary>How fast speed approaches the throttle's speed (1/s): the throttle's pull and the drag together.</summary>
    public const float SpeedResponse = 0.5f;
    /// <summary>Gravity (m/s^2).</summary>
    public const float Gravity = 9.81f;
    /// <summary>Arcade share of <see cref="Gravity"/> acting along the flight path: speed changes by
    /// -Gravity x GravityScale x sin(climb) per second (7.8 m/s^2 straight up or down).</summary>
    public const float GravityScale = 0.8f;
    /// <summary>Most the throttle can add to the speed (m/s^2, healthy engine): climbs steeper than
    /// about 22 degrees (where gravity takes more) bleed speed even at full throttle.</summary>
    public const float MaxThrustAcceleration = 3f;
    /// <summary>Drag (1/s) on the speed above the engine's level max (<see cref="MaxSpeedNow"/>):
    /// weak, so a dive builds speed well past it, up to <see cref="DiveMaxSpeed"/>.</summary>
    public const float OverspeedDrag = 0.15f;
    /// <summary>Speed never goes above this (m/s), however steep the dive.</summary>
    public const float DiveMaxSpeed = 85f;
    /// <summary>Speeds (m/s) above <see cref="MinSpeed"/> in which the climb is limited: the sine of the
    /// largest climb allowed grows from 0 at the minimum to 1 at this much above it.</summary>
    public const float LowSpeedBand = 10f;
    /// <summary>Time constant (s) with which a climb too steep for the speed is eased down (the nose drops gently).</summary>
    public const float LowSpeedResponse = 0.5f;

    public const float FloorAltitude = 20f;
    public const float AreaHalfSize = 2000f;
    /// <summary>Distance from the border where the plane starts turning back.</summary>
    public const float BorderMargin = 400f;
    /// <summary>Height above the floor kept free, plus the drop of the next seconds of descent.</summary>
    public const float FloorMargin = 40f;
    public const float FloorLookahead = 2.5f;
    /// <summary>Height (m) where full throttle only just holds level flight (at <see cref="PowerAltitudeSpeed"/>):
    /// the engine's power falls with height (see <see cref="AltitudePowerLoss"/>), its climbing power is gone here
    /// and above it there is not even enough for level flight, so the plane sinks back. Not a limit: nothing clamps.</summary>
    public const float PowerAltitude = 1000f;
    /// <summary>Level max speed (m/s, healthy engine, full throttle) at <see cref="PowerAltitude"/>: about cruise.</summary>
    public const float PowerAltitudeSpeed = 40f;

    public const float MaxAimBank = 75f * Deg;

    /// <summary>Sticks inside this (each of pitch, roll, yaw) count as hands off.</summary>
    public const float HandsOffDeadZone = 0.05f;
    /// <summary>Hands off this long (s) before the plane starts levelling itself.</summary>
    public const float HandsOffDelay = 0.25f;
    /// <summary>Time constant (s) of the hands-off levelling: the bank left (and any drift off the held climb) shrinks by e each this long.</summary>
    public const float LevelTimeConstant = 0.6f;
    /// <summary>Largest stick the levelling uses for roll and pitch, so it stays gentle from far off level.</summary>
    public const float LevelMaxRoll = 0.5f;
    public const float LevelMaxPitch = 0.6f;

    /// <summary>Start points: Allies this far south (+Z) of the center, Central this far north (-Z).</summary>
    public const float StartDistance = 1500f;
    public const float StartAltitude = 400f;
    /// <summary>Planes of a team start side by side, this far apart along X.</summary>
    public const float StartSpacing = 60f;

    /// <summary>Max speed with a dead engine, as a share of <see cref="MaxSpeed"/> (1 with a healthy engine).</summary>
    public const float DeadEngineMaxSpeed = 0.6f;
    /// <summary>Roll stick a fully destroyed wing adds toward its side (scaled by the damage).</summary>
    public const float WingRollDrift = 0.4f;
    /// <summary>Pitch and yaw rates with a fully destroyed tail, as a share of the healthy rates.</summary>
    public const float DeadTailResponse = 0.4f;

    /// <summary>A downed plane drops its nose to this climb (negative: nose down) and spins slowly.</summary>
    public const float FallClimb = -60f * Deg;
    public const float FallPitchRate = 40f * Deg;
    public const float FallSpinRate = 90f * Deg;
    public const float FallTurnRate = 25f * Deg;
    /// <summary>Height of the ground (World's GroundY): a downed plane stops here.</summary>
    public const float GroundAltitude = 0f;

    private const float Deg = MathF.PI / 180f;

    private Vector3 _position;
    private Quaternion _orientation;
    private float _speed;
    private float _throttle;
    private float _pitchCmd, _rollCmd, _yawCmd;
    private float _handsOffTime;
    /// <summary>Climb angle (rad) hands off keeps: the one at the release of the sticks, or the one a guard left.</summary>
    private float _holdClimb;
    private float _engine = 1f, _leftWing = 1f, _rightWing = 1f, _tail = 1f;
    private bool _alive = true;
    private bool _grounded;

    public int PlaneId { get; }

    public FlightModel(int planeId = 0)
    {
        PlaneId = planeId;
        _throttle = CruiseThrottle;
        Place(CruiseSpeed);
    }

    /// <summary>
    /// Where plane <paramref name="planeId"/> starts: Allies (0..3) <see cref="StartDistance"/> south
    /// (+Z) of the center, Central (4..7) as far north, side by side <see cref="StartSpacing"/> apart.
    /// </summary>
    public static Vector3 StartPosition(int planeId)
    {
        int slot = ((planeId % Teams.TeamSize) + Teams.TeamSize) % Teams.TeamSize;
        float x = (slot - (Teams.TeamSize - 1) * 0.5f) * StartSpacing;
        return new Vector3(x, StartAltitude, Teams.IsAllies(planeId) ? StartDistance : -StartDistance);
    }

    /// <summary>Start orientation: Allies head north (-Z), Central south (+Z), level.</summary>
    public static Quaternion StartOrientation(int planeId) =>
        Teams.IsAllies(planeId) ? Quaternion.Identity : Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);

    public bool Alive => _alive;
    /// <summary>Down and come to rest on the ground.</summary>
    public bool Grounded => _grounded;
    /// <summary>Max level speed with the engine's current health, at the ground (dives go faster; height lowers it,
    /// see <see cref="LevelMaxSpeed"/>).</summary>
    public float MaxSpeedNow => MaxSpeedNowFor(_engine);

    /// <summary>
    /// Share (0 at the ground, 1 at <see cref="PowerAltitude"/>, more above) of the engine's power lost to height:
    /// (h / <see cref="PowerAltitude"/>)^4, a smooth curve, so low down almost nothing is lost (0.2% at 200 m,
    /// 2.6% at 400 m, 13% at 600 m) and most of it goes in the last few hundred meters. No limit above.
    /// </summary>
    public static float AltitudePowerLoss(float altitude)
    {
        float x = MathF.Max(altitude, 0f) / PowerAltitude;
        x *= x;
        return x * x;
    }

    /// <summary>Max level speed (m/s) with an engine of health <paramref name="engine"/> at <paramref name="altitude"/> m:
    /// <see cref="MaxSpeedNowFor"/> at the ground, falling with the power lost to height to
    /// <see cref="PowerAltitudeSpeed"/> (x the engine's share) at <see cref="PowerAltitude"/>, and further above.</summary>
    public static float LevelMaxSpeed(float engine, float altitude) =>
        MaxSpeedNowFor(engine) * (1f - (1f - PowerAltitudeSpeed / MaxSpeed) * AltitudePowerLoss(altitude));

    /// <summary>Most the throttle can add (m/s^2) with an engine of health <paramref name="engine"/> at
    /// <paramref name="altitude"/> m: <see cref="MaxThrustAcceleration"/> x the engine's share x the power left
    /// (1 - <see cref="AltitudePowerLoss"/>): 0 at <see cref="PowerAltitude"/>, nothing left for climbing,
    /// negative above (a loss even in level flight).</summary>
    public static float ClimbPower(float engine, float altitude) =>
        MaxThrustAcceleration * MaxSpeedNowFor(engine) / MaxSpeed * (1f - AltitudePowerLoss(altitude));
    /// <summary>Max level speed with an engine of health <paramref name="engine"/> (0..1).</summary>
    public static float MaxSpeedNowFor(float engine) => MaxSpeed * (DeadEngineMaxSpeed + (1f - DeadEngineMaxSpeed) * engine);

    /// <summary>Health of each part, 0..1 (from the Damage module); values outside are clamped, NaN counts as 1.</summary>
    public void SetDamage(float engine, float leftWing, float rightWing, float tail)
    {
        _engine = Health(engine);
        _leftWing = Health(leftWing);
        _rightWing = Health(rightWing);
        _tail = Health(tail);
    }

    /// <summary>The plane is shot down: it falls (nose down, slow spin) to the ground and stays there.</summary>
    public void Destroy() => _alive = false;

    /// <summary>Back in the fight: at its team's start point, level, at full speed, undamaged.</summary>
    public void Respawn()
    {
        _alive = true;
        _grounded = false;
        _engine = _leftWing = _rightWing = _tail = 1f;
        _pitchCmd = _rollCmd = _yawCmd = 0f;
        _handsOffTime = 0f;
        _holdClimb = 0f;
        Place(MaxSpeed);
    }

    private void Place(float speed)
    {
        _position = StartPosition(PlaneId);
        _orientation = StartOrientation(PlaneId);
        _speed = speed;
    }

    private static float Health(float value) => float.IsNaN(value) ? 1f : Math.Clamp(value, 0f, 1f);

    public Vector3 Position => _position;
    public Quaternion Orientation => _orientation;
    public float Speed => _speed;
    public float Throttle => _throttle;
    public Vector3 Forward => Vector3.Transform(-Vector3.UnitZ, _orientation);
    public Vector3 Velocity => _grounded ? Vector3.Zero : Forward * _speed;

    public PlaneState State => new(PlaneId, _position, _orientation, Velocity, _speed, _throttle, _alive);

    /// <summary>Level speed the throttle settles at: 0 → min, <see cref="CruiseThrottle"/> → cruise, 1 → max.</summary>
    public static float SpeedForThrottle(float throttle)
    {
        throttle = Math.Clamp(throttle, 0f, 1f);
        return throttle <= CruiseThrottle
            ? MinSpeed + (CruiseSpeed - MinSpeed) * throttle / CruiseThrottle
            : CruiseSpeed + (MaxSpeed - CruiseSpeed) * (throttle - CruiseThrottle) / (1f - CruiseThrottle);
    }

    /// <summary>One fixed step of <paramref name="dt"/> seconds with the pilot's controls.</summary>
    public void Step(in ControlInput input, float dt)
    {
        if (!_alive)
        {
            Fall(dt);
            return;
        }

        Vector3 forward = Vector3.Transform(-Vector3.UnitZ, _orientation);

        bool handsOff = !input.HasAim && MathF.Abs(input.Pitch) < HandsOffDeadZone
            && MathF.Abs(input.Roll) < HandsOffDeadZone && MathF.Abs(input.Yaw) < HandsOffDeadZone;
        float climb = MathF.Asin(Math.Clamp(forward.Y, -1f, 1f));
        if (handsOff && _handsOffTime == 0f)
            _holdClimb = climb; // the sticks were just let go: keep this climb or dive
        _handsOffTime = handsOff ? _handsOffTime + dt : 0f;

        float pitch, roll, yaw;
        bool guarded = GuardDirection(forward, out Vector3 guard, out bool low);
        if (guarded)
        {
            Steer(guard, out pitch, out roll, out yaw);
            // Once clear, hands off keeps what the guard left (never a dive after a floor pull-up).
            _holdClimb = low ? MathF.Max(climb, 0f) : climb;
        }
        else if (input.HasAim)
            Steer(input.AimDirection, out pitch, out roll, out yaw);
        else if (_handsOffTime >= HandsOffDelay)
            Level(climb, out pitch, out roll, out yaw);
        else
        {
            pitch = input.Pitch;
            roll = input.Roll;
            yaw = input.Yaw;
        }

        // Too slow to keep climbing (or, high up, too little power even at the minimum speed): the nose drops
        // gently until the speed is back (the floor guard still wins).
        float maxClimbSine = LowSpeedMaxClimbSine;
        if (maxClimbSine < 1f && !(guarded && low))
            ClimbLimit(climb, MathF.Asin(maxClimbSine), LowSpeedResponse, ref pitch, ref yaw);

        // A damaged wing drags the plane into a roll toward its side.
        roll += ((1f - _rightWing) - (1f - _leftWing)) * WingRollDrift;

        float follow = MathF.Min(1f, ControlResponse * dt);
        _pitchCmd += (pitch - _pitchCmd) * follow;
        _rollCmd += (roll - _rollCmd) * follow;
        _yawCmd += (yaw - _yawCmd) * follow;

        // Body rates (local axes: +X right, +Y up, -Z forward), then the bank's turn about world up.
        // A damaged tail answers pitch and yaw more weakly.
        float tail = DeadTailResponse + (1f - DeadTailResponse) * _tail;
        Quaternion body = Quaternion.CreateFromAxisAngle(Vector3.UnitX, _pitchCmd * PitchRate * tail * dt)
            * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -_rollCmd * RollRate * dt)
            * Quaternion.CreateFromAxisAngle(Vector3.UnitY, -_yawCmd * YawRate * tail * dt);
        Quaternion q = _orientation * body;
        float rightY = Vector3.Transform(Vector3.UnitX, q).Y;
        q = Quaternion.CreateFromAxisAngle(Vector3.UnitY, rightY * BankTurnRate * dt) * q;
        _orientation = Quaternion.Normalize(q);
        forward = Vector3.Transform(-Vector3.UnitZ, _orientation);

        _throttle = input.Throttle;
        _speed = Math.Clamp(_speed + (Thrust(_speed) - forward.Y * Gravity * GravityScale) * dt, MinSpeed, DiveMaxSpeed);

        _position += forward * (_speed * dt);
        // Backstop only: the guard steering normally keeps the plane well inside these. No ceiling.
        _position.Y = MathF.Max(_position.Y, FloorAltitude);
        _position.X = Math.Clamp(_position.X, -AreaHalfSize, AreaHalfSize);
        _position.Z = Math.Clamp(_position.Z, -AreaHalfSize, AreaHalfSize);
    }

    /// <summary>
    /// A downed plane: the nose drops to <see cref="FallClimb"/>, it spins slowly about its nose
    /// and turns, gaining speed in the dive, until it reaches the ground; then it stays there.
    /// </summary>
    private void Fall(float dt)
    {
        if (_grounded)
            return;

        Vector3 forward = Vector3.Transform(-Vector3.UnitZ, _orientation);
        if (MathF.Asin(Math.Clamp(forward.Y, -1f, 1f)) > FallClimb)
        {
            // Pitch the nose toward the ground about the world horizontal axis across it.
            Vector3 axis = Vector3.Cross(forward, -Vector3.UnitY);
            axis = axis.LengthSquared() > 1e-6f ? Vector3.Normalize(axis) : Vector3.Transform(Vector3.UnitX, _orientation);
            _orientation = Quaternion.CreateFromAxisAngle(axis, FallPitchRate * dt) * _orientation;
        }
        _orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, FallTurnRate * dt) * _orientation
            * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -FallSpinRate * dt);
        _orientation = Quaternion.Normalize(_orientation);
        forward = Vector3.Transform(-Vector3.UnitZ, _orientation);

        _speed += (MinSpeed - _speed) * MathF.Min(1f, SpeedResponse * dt);
        _speed -= forward.Y * Gravity * GravityScale * dt;
        _speed = Math.Clamp(_speed, MinSpeed, DiveMaxSpeed);
        _pitchCmd = _rollCmd = _yawCmd = 0f;

        _position += forward * (_speed * dt);
        _position.X = Math.Clamp(_position.X, -AreaHalfSize, AreaHalfSize);
        _position.Z = Math.Clamp(_position.Z, -AreaHalfSize, AreaHalfSize);
        if (_position.Y <= GroundAltitude)
        {
            _position.Y = GroundAltitude;
            _speed = 0f;
            _grounded = true;
        }
    }

    /// <summary>A direction to steer to instead of the pilot when near the floor or the border.</summary>
    private bool GuardDirection(Vector3 forward, out Vector3 direction, out bool low)
    {
        float descent = MathF.Max(0f, -forward.Y) * _speed * FloorLookahead;
        low = _position.Y - FloorAltitude - descent < FloorMargin;
        float edge = AreaHalfSize - BorderMargin;
        bool outX = (_position.X > edge && forward.X > 0f) || (_position.X < -edge && forward.X < 0f);
        bool outZ = (_position.Z > edge && forward.Z > 0f) || (_position.Z < -edge && forward.Z < 0f);
        if (!low && !outX && !outZ)
        {
            direction = default;
            return false;
        }

        Vector3 horizontal;
        if (outX || outZ)
            horizontal = new Vector3(-_position.X, 0f, -_position.Z);
        else
        {
            horizontal = new Vector3(forward.X, 0f, forward.Z);
            if (horizontal.LengthSquared() < 0.01f)
            {
                // Nose straight down: pull out the way the plane's belly-to-canopy points.
                Vector3 up = Vector3.Transform(Vector3.UnitY, _orientation);
                horizontal = new Vector3(up.X, 0f, up.Z);
            }
        }
        if (horizontal.LengthSquared() < 1e-6f)
            horizontal = -Vector3.UnitZ;
        horizontal = Vector3.Normalize(horizontal);

        // Low: climb gently (about 20 degrees). Otherwise keep the current climb or dive, mildly.
        float rise = low ? 0.35f : Math.Clamp(forward.Y, -0.3f, 0.3f);
        direction = Vector3.Normalize(horizontal + new Vector3(0f, rise, 0f));
        return true;
    }

    /// <summary>
    /// The throttle's pull and the drag (m/s^2) at <paramref name="speed"/>, without gravity: toward the
    /// throttle's speed at <see cref="SpeedResponse"/> (never above <see cref="LevelMaxSpeed"/>), adding at most
    /// <see cref="ClimbPower"/> (less with a damaged engine and with height; above <see cref="PowerAltitude"/>
    /// it is negative, a loss at any speed); above the level max only the weak <see cref="OverspeedDrag"/> slows it.
    /// </summary>
    private float Thrust(float speed)
    {
        float maxSpeed = LevelMaxSpeed(_engine, _position.Y);
        float target = MathF.Min(SpeedForThrottle(_throttle), maxSpeed);
        float pull = speed > maxSpeed
            ? SpeedResponse * (target - maxSpeed) - OverspeedDrag * (speed - maxSpeed)
            : SpeedResponse * (target - speed);
        return MathF.Min(pull, ClimbPower(_engine, _position.Y));
    }

    /// <summary>Sine of the largest climb allowed at the current speed: 0 (level) at <see cref="MinSpeed"/>,
    /// growing in proportion up to 1 (90 degrees) at <see cref="LowSpeedBand"/> above it. When the engine lacks
    /// power even at the minimum speed (high above <see cref="PowerAltitude"/>, or a dead engine near it), lower
    /// by that loss over gravity: at the minimum speed the plane glides down just steeply enough to keep it.</summary>
    private float LowSpeedMaxClimbSine
    {
        get
        {
            float loss = MathF.Min(0f, Thrust(MinSpeed));
            return Math.Clamp((_speed - MinSpeed) / LowSpeedBand + loss / (Gravity * GravityScale), -1f, 1f);
        }
    }

    /// <summary>
    /// Climb limit (the low speed's): the pitch command (whoever gives it) may raise the climb only toward
    /// <paramref name="maxClimb"/>, a steeper climb is eased down to it with time constant
    /// <paramref name="response"/>, so the nose drops smoothly. Lowering the nose is never limited.
    /// Yaw raises the nose too when banked (steeply banked, more than pitch): its share is limited first.
    /// </summary>
    private void ClimbLimit(float climb, float maxClimb, float response, ref float pitch, ref float yaw)
    {
        float wantedRate = (maxClimb - climb) / response;
        // Climb rate per unit of pitch stick: the nose moves toward the plane's up, so inverted a pull
        // lowers it (up.Y / cos(climb) is about cos(bank); straight up, a push brings the nose over).
        float tail = DeadTailResponse + (1f - DeadTailResponse) * _tail;
        float upY = Vector3.Transform(Vector3.UnitY, _orientation).Y;
        float rightY = Vector3.Transform(Vector3.UnitX, _orientation).Y;
        float cosClimb = MathF.Cos(climb);
        // Climb rate per unit of yaw: the nose moves toward the plane's right (right.Y / cos(climb) is about -sin(bank)).
        float yawGain = cosClimb > 1e-3f ? YawRate * tail * Math.Clamp(rightY / cosClimb, -1f, 1f) : 0f;
        float yawRate = yaw * yawGain, yawAllowed = MathF.Max(wantedRate, 0f);
        if (yawRate > yawAllowed)
        {
            yaw = yawAllowed / yawGain;
            yawRate = yawAllowed;
        }
        wantedRate -= yawRate;
        float gain = PitchRate * tail * (cosClimb > 1e-3f ? Math.Clamp(upY / cosClimb, -1f, 1f) : 1f);
        if (gain > 1e-3f)
            pitch = MathF.Min(pitch, wantedRate / gain);
        else if (gain < -1e-3f)
            pitch = MathF.Max(pitch, wantedRate / gain);
        pitch = Math.Clamp(pitch, -1f, 1f);
        // Hands off keeps no climb above the limit (too steep for the speed).
        _holdClimb = MathF.Min(_holdClimb, maxClimb);
    }

    /// <summary>
    /// Hands-off levelling: roll back to wings level the shorter way, at a rate proportional to
    /// what is left (so it slows near level and does not overshoot). The nose is not levelled:
    /// it keeps <see cref="_holdClimb"/>, the climb or dive it had when the sticks were let go,
    /// only taking back the little the stick's lag adds after the release (pitch waits for the
    /// wings to be within 90 degrees of level). Heading is kept: no yaw.
    /// </summary>
    private void Level(float climb, out float pitch, out float roll, out float yaw)
    {
        Vector3 right = Vector3.Transform(Vector3.UnitX, _orientation);
        Vector3 up = Vector3.Transform(Vector3.UnitY, _orientation);

        float bank = MathF.Atan2(-right.Y, up.Y); // positive: rolled right; -pi..pi, so -bank is the shorter way
        roll = Math.Clamp(-bank / (LevelTimeConstant * RollRate), -LevelMaxRoll, LevelMaxRoll);

        float wingsLevel = MathF.Max(0f, MathF.Cos(bank));
        pitch = Math.Clamp((_holdClimb - climb) / (LevelTimeConstant * PitchRate), -LevelMaxPitch, LevelMaxPitch) * wingsLevel;
        yaw = 0f;
    }

    /// <summary>
    /// Mouse-aim autopilot: bank toward the target's heading, pull toward it, and fine-tune
    /// with yaw; wings level once it is ahead.
    /// </summary>
    private void Steer(Vector3 direction, out float pitch, out float roll, out float yaw)
    {
        Vector3 local = Vector3.Transform(direction, Quaternion.Conjugate(_orientation));
        float ahead = MathF.Max(-local.Z, 0f);
        pitch = Math.Clamp(MathF.Atan2(local.Y, ahead) * 3f, -1f, 1f);
        yaw = Math.Clamp(MathF.Asin(Math.Clamp(local.X, -1f, 1f)) * 2f, -1f, 1f);

        Vector3 forward = Vector3.Transform(-Vector3.UnitZ, _orientation);
        float fx = forward.X, fz = forward.Z, dx = direction.X, dz = direction.Z;
        float heading = 0f;
        if (fx * fx + fz * fz > 0.0025f && dx * dx + dz * dz > 0.0025f)
            heading = MathF.Atan2(fx * dz - fz * dx, fx * dx + fz * dz); // positive: target to the right
        float wantedBank = Math.Clamp(heading * 1.5f, -MaxAimBank, MaxAimBank);

        Vector3 right = Vector3.Transform(Vector3.UnitX, _orientation);
        Vector3 up = Vector3.Transform(Vector3.UnitY, _orientation);
        float bank = MathF.Atan2(-right.Y, up.Y); // positive: rolled right
        float error = wantedBank - bank;
        if (error > MathF.PI) error -= 2f * MathF.PI;
        else if (error < -MathF.PI) error += 2f * MathF.PI;
        roll = Math.Clamp(error * 3f, -1f, 1f);
    }
}
