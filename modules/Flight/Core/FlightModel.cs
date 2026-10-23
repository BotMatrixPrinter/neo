using System;
using System.Numerics;
using Neo.Input;

namespace Neo.Flight;

/// <summary>
/// Arcade flight of one plane: not a simulation. The plane always flies along its nose at
/// a speed between <see cref="MinSpeed"/> and <see cref="MaxSpeed"/> (no stall, no spin),
/// turns at fixed body rates, and is kept above <see cref="FloorAltitude"/> and inside the
/// <see cref="AreaHalfSize"/> square by steering it gently away. Deterministic; Step does
/// not allocate.
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
    /// <summary>How fast speed approaches the throttle's speed (1/s).</summary>
    public const float SpeedResponse = 0.5f;
    /// <summary>Speed gained per second per g of nose-down (diving speeds up, climbing slows).</summary>
    public const float DiveAcceleration = 6f;

    public const float FloorAltitude = 20f;
    public const float AreaHalfSize = 2000f;
    /// <summary>Distance from the border where the plane starts turning back.</summary>
    public const float BorderMargin = 400f;
    /// <summary>Height above the floor kept free, plus the drop of the next seconds of descent.</summary>
    public const float FloorMargin = 40f;
    public const float FloorLookahead = 2.5f;

    public const float MaxAimBank = 75f * Deg;

    private const float Deg = MathF.PI / 180f;

    private Vector3 _position = new(0f, 400f, 0f);
    private Quaternion _orientation = Quaternion.Identity;
    private float _speed = CruiseSpeed;
    private float _throttle = CruiseThrottle;
    private float _pitchCmd, _rollCmd, _yawCmd;

    public int PlaneId { get; }

    public FlightModel(int planeId = 0) => PlaneId = planeId;

    public Vector3 Position => _position;
    public Quaternion Orientation => _orientation;
    public float Speed => _speed;
    public float Throttle => _throttle;
    public Vector3 Forward => Vector3.Transform(-Vector3.UnitZ, _orientation);
    public Vector3 Velocity => Forward * _speed;

    public PlaneState State => new(PlaneId, _position, _orientation, Velocity, _speed, _throttle);

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
        Vector3 forward = Vector3.Transform(-Vector3.UnitZ, _orientation);

        float pitch, roll, yaw;
        if (GuardDirection(forward, out Vector3 guard))
            Steer(guard, out pitch, out roll, out yaw);
        else if (input.HasAim)
            Steer(input.AimDirection, out pitch, out roll, out yaw);
        else
        {
            pitch = input.Pitch;
            roll = input.Roll;
            yaw = input.Yaw;
        }

        float follow = MathF.Min(1f, ControlResponse * dt);
        _pitchCmd += (pitch - _pitchCmd) * follow;
        _rollCmd += (roll - _rollCmd) * follow;
        _yawCmd += (yaw - _yawCmd) * follow;

        // Body rates (local axes: +X right, +Y up, -Z forward), then the bank's turn about world up.
        Quaternion body = Quaternion.CreateFromAxisAngle(Vector3.UnitX, _pitchCmd * PitchRate * dt)
            * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -_rollCmd * RollRate * dt)
            * Quaternion.CreateFromAxisAngle(Vector3.UnitY, -_yawCmd * YawRate * dt);
        Quaternion q = _orientation * body;
        float rightY = Vector3.Transform(Vector3.UnitX, q).Y;
        q = Quaternion.CreateFromAxisAngle(Vector3.UnitY, rightY * BankTurnRate * dt) * q;
        _orientation = Quaternion.Normalize(q);
        forward = Vector3.Transform(-Vector3.UnitZ, _orientation);

        _throttle = input.Throttle;
        float target = SpeedForThrottle(_throttle);
        _speed += (target - _speed) * MathF.Min(1f, SpeedResponse * dt);
        _speed -= forward.Y * DiveAcceleration * dt;
        _speed = Math.Clamp(_speed, MinSpeed, MaxSpeed);

        _position += forward * (_speed * dt);
        // Backstop only: the guard steering normally keeps the plane well inside these.
        _position.Y = MathF.Max(_position.Y, FloorAltitude);
        _position.X = Math.Clamp(_position.X, -AreaHalfSize, AreaHalfSize);
        _position.Z = Math.Clamp(_position.Z, -AreaHalfSize, AreaHalfSize);
    }

    /// <summary>A direction to steer to instead of the pilot when near the floor or the border.</summary>
    private bool GuardDirection(Vector3 forward, out Vector3 direction)
    {
        float descent = MathF.Max(0f, -forward.Y) * _speed * FloorLookahead;
        bool low = _position.Y - FloorAltitude - descent < FloorMargin;
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
