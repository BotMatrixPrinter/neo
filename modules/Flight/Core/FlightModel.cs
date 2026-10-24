using System;
using System.Numerics;
using Neo.Input;
using Neo.World;

namespace Neo.Flight;

/// <summary>
/// Arcade flight of one plane: not a simulation. The plane always flies along its nose at
/// a speed between <see cref="MinSpeed"/> and <see cref="DiveMaxSpeed"/> (no stall, no spin; the
/// throttle sets the level speed, gravity along the flight path slows climbs and speeds up dives),
/// turns at fixed body rates, and is turned back gently once it is <see cref="BorderOvershoot"/> beyond World's
/// playable square (<see cref="WorldBounds.HalfSize"/>), so on a map walled by mountains the walls are the real border. Nothing keeps it off the ground: flying into World's terrain is a crash (<see cref="JustCrashed"/>). No hard ceiling: the engine loses
/// power as the plane climbs, until at <see cref="PowerAltitude"/> full throttle only holds level
/// flight, so climbs level off there by themselves. Damage slows it, drags it into a roll and weakens its tail; a downed plane falls
/// to the ground; a crashed or downed plane rests there until it respawns. A collision (<see cref="ApplyImpulse"/>) shoves it aside and sets it spinning briefly, both
/// fading over about a second.
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

    /// <summary>How far (m) beyond World's playable half-size (<see cref="WorldBounds.HalfSize"/>) the gentle
    /// border turn-back starts: inside the playable square nothing turns the plane.</summary>
    public const float BorderOvershoot = 100f;
    /// <summary>Half-size (m) of the square past which a plane heading out is turned back toward the center.</summary>
    public const float GuardHalfSize = WorldBounds.HalfSize + BorderOvershoot;
    /// <summary>Distance (m) beyond <see cref="GuardHalfSize"/> of the hard clamp, a last safety only.</summary>
    public const float BorderMargin = 400f;
    /// <summary>Half-size (m) of the hard clamp on X and Z: the plane never gets beyond it.</summary>
    public const float ClampHalfSize = GuardHalfSize + BorderMargin;
    /// <summary>Height (m) where full throttle only just holds level flight (at <see cref="PowerAltitudeSpeed"/>):
    /// the engine's power falls with height (see <see cref="AltitudePowerLoss"/>), its climbing power is gone here
    /// and above it there is not even enough for level flight, so the plane sinks back. Not a limit: nothing clamps.</summary>
    public const float PowerAltitude = 1000f;
    /// <summary>Level max speed (m/s, healthy engine, full throttle) at <see cref="PowerAltitude"/>: about cruise.</summary>
    public const float PowerAltitudeSpeed = 40f;

    public const float MaxAimBank = 75f * Deg;


    /// <summary>Spawns: Allies on the south half (+Z), Central on the north half (-Z), between these shares of
    /// <see cref="WorldBounds.HalfSize"/> from the center line...</summary>
    public const float SpawnNearShare = 0.3f;
    public const float SpawnFarShare = 0.85f;
    /// <summary>...and within this share of it either side of X = 0.</summary>
    public const float SpawnSideShare = 0.85f;
    /// <summary>Spawn height range (m), and the least height above the terrain there.</summary>
    public const float SpawnMinAltitude = 350f;
    public const float SpawnMaxAltitude = 450f;
    public const float SpawnClearance = 150f;
    /// <summary>A spawn heads toward the center, turned by up to this much either way.</summary>
    public const float SpawnHeadingSpread = 30f * Deg;
    /// <summary>Least distance (m) between planes of a team spawning together: each spawns in its own X lane.</summary>
    public const float SpawnSpacing = 100f;
    /// <summary>Points tried for a spawn low enough over the terrain before taking the last one, raised clear of it.</summary>
    private const int SpawnTries = 16;

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
    /// <summary>Height (m) of the plane's lowest point below its position: it crashes when that point is at or
    /// below the terrain (<see cref="Terrain.HeightAt"/>), and then rests on the ground there.</summary>
    public const float CrashClearance = 1.5f;

    /// <summary>Time constant (s) with which a collision's knock velocity and spin fade: about 4% is left after 1 s.</summary>
    public const float KnockTimeConstant = 0.3f;
    /// <summary>A knock or spin smaller than this (m/s, rad/s) is gone: the plane is back to normal flight.</summary>
    public const float KnockRest = 0.05f;

    private const float Deg = MathF.PI / 180f;

    private Vector3 _position;
    private Quaternion _orientation;
    private float _speed;
    private float _throttle;
    private float _pitchCmd, _rollCmd, _yawCmd;
    private float _engine = 1f, _leftWing = 1f, _rightWing = 1f, _tail = 1f;
    private bool _alive = true;
    private bool _grounded;
    private bool _justCrashed;
    private float _crashSpeed;
    private bool _repairing;
    private int _respawnCount;
    /// <summary>A collision's shove (world m/s), on top of the flight along the nose; fades with <see cref="KnockTimeConstant"/>.</summary>
    private Vector3 _knock;
    /// <summary>A collision's spin (body rad/s: x pitch, y yaw, z roll, right-handed, +Z back), on top of the controls; fades the same.</summary>
    private Vector3 _kick;

    public int PlaneId { get; }

    public FlightModel(int planeId = 0)
    {
        PlaneId = planeId;
        _throttle = CruiseThrottle;
        Place();
    }

    /// <summary>
    /// Where plane <paramref name="planeId"/> spawns for the <paramref name="respawnCount"/>th time (0: the start of
    /// the game): a random point on its team's half, Allies (0..3) at Z between <see cref="SpawnNearShare"/> and
    /// <see cref="SpawnFarShare"/> x <see cref="WorldBounds.HalfSize"/>, Central (4..7) as far on -Z, X within
    /// <see cref="SpawnSideShare"/> x HalfSize (each team slot in its own lane, so a team spawning together is at
    /// least <see cref="SpawnSpacing"/> apart), at <see cref="SpawnMinAltitude"/>..<see cref="SpawnMaxAltitude"/> m and
    /// at least <see cref="SpawnClearance"/> above the terrain there; level, heading toward the center within
    /// <see cref="SpawnHeadingSpread"/>. Random from a generator seeded with the id and the count: same arguments,
    /// same spawn.
    /// </summary>
    public static void SpawnPoint(int planeId, int respawnCount, out Vector3 position, out Quaternion orientation)
    {
        uint state = (uint)planeId * 0x9E3779B9u ^ (uint)respawnCount * 0x85EBCA6Bu ^ 0x27D4EB2Fu;
        Random01(ref state);
        float half = WorldBounds.HalfSize;
        int slot = ((planeId % Teams.TeamSize) + Teams.TeamSize) % Teams.TeamSize;
        float lane = 2f * SpawnSideShare * half / Teams.TeamSize;
        float laneMin = -SpawnSideShare * half + slot * lane + SpawnSpacing * 0.5f;
        float laneSpan = MathF.Max(lane - SpawnSpacing, 0f);
        float side = Teams.IsAllies(planeId) ? 1f : -1f;
        float x = 0f, y = 0f, z = 0f;
        for (int attempt = 0; attempt < SpawnTries; attempt++)
        {
            x = laneMin + laneSpan * Random01(ref state);
            z = side * half * (SpawnNearShare + (SpawnFarShare - SpawnNearShare) * Random01(ref state));
            y = MathF.Max(SpawnMinAltitude + (SpawnMaxAltitude - SpawnMinAltitude) * Random01(ref state),
                Terrain.HeightAt(x, z) + SpawnClearance);
            if (y <= SpawnMaxAltitude)
                break; // else the ground is too high here: try another point (the last one is kept, raised clear)
        }
        position = new Vector3(x, y, z);
        // Forward is (-sin(yaw), 0, -cos(yaw)): toward the center (-x, -z), turned by up to the spread.
        float yaw = MathF.Atan2(x, z) + (Random01(ref state) * 2f - 1f) * SpawnHeadingSpread;
        orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
    }

    /// <summary>Next number in 0..1 from <paramref name="state"/> (a PCG hash step): a generator of its own,
    /// no shared state.</summary>
    private static float Random01(ref uint state)
    {
        state = state * 747796405u + 2891336453u;
        uint word = ((state >> (int)((state >> 28) + 4u)) ^ state) * 277803737u;
        word = (word >> 22) ^ word;
        return (word >> 8) * (1f / 16777216f);
    }

    /// <summary>Respawns so far (0 until the first <see cref="Respawn"/>): with the id, it seeds the spawn point.</summary>
    public int RespawnCount => _respawnCount;

    public bool Alive => _alive;
    /// <summary>Down and come to rest on the ground.</summary>
    public bool Grounded => _grounded;
    /// <summary>True for the step in which the plane hit the ground (flying or falling); false otherwise.</summary>
    public bool JustCrashed => _justCrashed;
    /// <summary>Speed (m/s) the plane hit the ground at; 0 before any crash.</summary>
    public float CrashSpeed => _crashSpeed;
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

    /// <summary>True while the pilot repairs in flight: their pitch, roll, yaw and aim are ignored, as with no input.</summary>
    public bool Repairing => _repairing;

    /// <summary>In-flight repair on or off (Damage's <c>RepairState.Repairing</c>).</summary>
    public void SetRepairing(bool repairing) => _repairing = repairing;

    /// <summary>The plane is shot down: it falls (nose down, slow spin) to the ground and stays there.</summary>
    public void Destroy()
    {
        _alive = false;
        _knock = _kick = Vector3.Zero;
    }

    /// <summary>
    /// A collision's bounce (Collisions' <c>CollisionImpulse</c>): moves the plane by <paramref name="separation"/> at
    /// once (inside the border), changes its speed by the part of <paramref name="velocityChange"/> along the
    /// nose (within <see cref="MinSpeed"/>..<see cref="DiveMaxSpeed"/>) and keeps the rest (sideways, plus what the
    /// limits cut off) as a knock velocity on top of its flight; adds <paramref name="angularKick"/> (body rad/s: x pitch,
    /// y yaw, z roll) to its turn rates. Knock and spin fade with <see cref="KnockTimeConstant"/>. Ignored when the
    /// plane is down or any value is not finite.
    /// </summary>
    public void ApplyImpulse(Vector3 velocityChange, Vector3 angularKick, Vector3 separation)
    {
        if (!_alive || !Finite(velocityChange) || !Finite(angularKick) || !Finite(separation))
            return;
        _position += separation;
        ClampToArea();
        Vector3 forward = Forward;
        float speed = Math.Clamp(_speed + Vector3.Dot(velocityChange, forward), MinSpeed, DiveMaxSpeed);
        _knock += velocityChange - forward * (speed - _speed);
        _speed = speed;
        _kick += angularKick;
    }

    /// <summary>The collision knock still on top of the flight along the nose (world m/s); zero in normal flight.</summary>
    public Vector3 Knock => _knock;
    /// <summary>The collision spin still on top of the controls (body rad/s: x pitch, y yaw, z roll); zero in normal flight.</summary>
    public Vector3 Kick => _kick;

    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    /// <summary>Backstop only: the border guard normally keeps the plane well inside these. No ceiling, no floor.</summary>
    private void ClampToArea()
    {
        _position.X = Math.Clamp(_position.X, -ClampHalfSize, ClampHalfSize);
        _position.Z = Math.Clamp(_position.Z, -ClampHalfSize, ClampHalfSize);
    }

    /// <summary>Back in the fight: at a new random spawn point on its team's half (<see cref="SpawnPoint"/>), level,
    /// at full speed, undamaged.</summary>
    public void Respawn()
    {
        _alive = true;
        _grounded = false;
        _justCrashed = false;
        _crashSpeed = 0f;
        _repairing = false;
        _engine = _leftWing = _rightWing = _tail = 1f;
        _pitchCmd = _rollCmd = _yawCmd = 0f;
        _knock = _kick = Vector3.Zero;
        _respawnCount++;
        Place();
    }

    /// <summary>At this spawn's point and heading, at full speed.</summary>
    private void Place()
    {
        SpawnPoint(PlaneId, _respawnCount, out _position, out _orientation);
        _speed = MaxSpeed;
    }

    private static float Health(float value) => float.IsNaN(value) ? 1f : Math.Clamp(value, 0f, 1f);

    public Vector3 Position => _position;
    public Quaternion Orientation => _orientation;
    public float Speed => _speed;
    public float Throttle => _throttle;
    public Vector3 Forward => Vector3.Transform(-Vector3.UnitZ, _orientation);
    /// <summary>World velocity: along the nose at <see cref="Speed"/>, plus a collision's <see cref="Knock"/> while it lasts.</summary>
    public Vector3 Velocity => _grounded ? Vector3.Zero : Forward * _speed + _knock;

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
        _justCrashed = false;
        if (!_alive)
        {
            Fall(dt);
            return;
        }

        Vector3 forward = Vector3.Transform(-Vector3.UnitZ, _orientation);

        float climb = MathF.Asin(Math.Clamp(forward.Y, -1f, 1f));

        // No stick input means no correction at all: the plane keeps its attitude and turn.
        // Repairing: the pilot's steering is ignored, exactly as with no input (throttle still works).
        float pitch, roll, yaw;
        if (GuardDirection(forward, out Vector3 guard))
            Steer(guard, out pitch, out roll, out yaw);
        else if (_repairing)
            pitch = roll = yaw = 0f;
        else if (input.HasAim)
            Steer(input.AimDirection, out pitch, out roll, out yaw);
        else
        {
            pitch = input.Pitch;
            roll = input.Roll;
            yaw = input.Yaw;
        }

        // Too slow to keep climbing (or, high up, too little power even at the minimum speed): the nose drops
        // gently until the speed is back.
        float maxClimbSine = LowSpeedMaxClimbSine;
        if (maxClimbSine < 1f)
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
        // A collision's spin turns the plane on top of that (only the pilot can work against it).
        float kick = _kick.Length();
        if (kick > 0f)
            body *= Quaternion.CreateFromAxisAngle(_kick / kick, kick * dt);
        Quaternion q = _orientation * body;
        float rightY = Vector3.Transform(Vector3.UnitX, q).Y;
        q = Quaternion.CreateFromAxisAngle(Vector3.UnitY, rightY * BankTurnRate * dt) * q;
        _orientation = Quaternion.Normalize(q);
        forward = Vector3.Transform(-Vector3.UnitZ, _orientation);

        _throttle = input.Throttle;
        _speed = Math.Clamp(_speed + (Thrust(_speed) - forward.Y * Gravity * GravityScale) * dt, MinSpeed, DiveMaxSpeed);

        _position += (forward * _speed + _knock) * dt;
        ClampToArea();
        if (HitGround())
        {
            _alive = false;
            _knock = _kick = Vector3.Zero;
            return;
        }

        // A collision's knock and spin fade smoothly, then the plane flies on normally.
        float fade = MathF.Exp(-dt / KnockTimeConstant);
        _knock *= fade;
        _kick *= fade;
        if (_knock.LengthSquared() < KnockRest * KnockRest)
            _knock = Vector3.Zero;
        if (_kick.LengthSquared() < KnockRest * KnockRest)
            _kick = Vector3.Zero;
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
        ClampToArea();
        HitGround();
    }

    /// <summary>
    /// If the plane's lowest point (<see cref="CrashClearance"/> below it) is at or below the terrain, it has
    /// crashed: it stops there, on the ground, and <see cref="JustCrashed"/> is set for this step.
    /// </summary>
    private bool HitGround()
    {
        float ground = Terrain.HeightAt(_position.X, _position.Z);
        if (_position.Y - CrashClearance > ground)
            return false;
        _position.Y = ground;
        _crashSpeed = _speed;
        _speed = 0f;
        _grounded = true;
        _justCrashed = true;
        return true;
    }

    /// <summary>A direction to steer to instead of the pilot when beyond <see cref="GuardHalfSize"/> and heading out.</summary>
    private bool GuardDirection(Vector3 forward, out Vector3 direction)
    {
        const float edge = GuardHalfSize;
        bool outX = (_position.X > edge && forward.X > 0f) || (_position.X < -edge && forward.X < 0f);
        bool outZ = (_position.Z > edge && forward.Z > 0f) || (_position.Z < -edge && forward.Z < 0f);
        if (!outX && !outZ)
        {
            direction = default;
            return false;
        }

        Vector3 horizontal = new Vector3(-_position.X, 0f, -_position.Z);
        if (horizontal.LengthSquared() < 1e-6f)
            horizontal = -Vector3.UnitZ;
        horizontal = Vector3.Normalize(horizontal);

        // Keep the current climb or dive, mildly.
        float rise = Math.Clamp(forward.Y, -0.3f, 0.3f);
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
