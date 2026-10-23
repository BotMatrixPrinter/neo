using System;
using System.Numerics;
using Neo.Flight;
using Neo.Shell;

namespace Neo.Camera;

/// <summary>
/// The Camera module: a chase camera on plane 0. Each tick it moves its <see cref="Pose"/>
/// toward a spot <see cref="Back"/> m behind and <see cref="Up"/> m above the plane (along
/// the plane's orientation), looking <see cref="LookAhead"/> m ahead of the plane, with
/// damping: the position lags when the plane speeds up, slows or turns and settles back;
/// the rotation follows more slowly while the plane rolls. The field of view widens with
/// speed. The view reads <see cref="Pose"/> and <see cref="PreviousPose"/> and interpolates
/// between them. Allocation-free per tick.
/// </summary>
public sealed class CameraModule : IModule, IMessageHandler<PlaneState>
{
    public const int PlaneId = 0;

    /// <summary>Meters behind the plane, along its tail.</summary>
    public const float Back = 14f;
    /// <summary>Meters above the plane, along its up.</summary>
    public const float Up = 4f;
    /// <summary>The camera looks at this many meters ahead of the plane's nose.</summary>
    public const float LookAhead = 12f;

    /// <summary>Field of view at <see cref="SlowSpeed"/> and below, degrees.</summary>
    public const float MinFov = 70f;
    /// <summary>Field of view at <see cref="FastSpeed"/> and above, degrees.</summary>
    public const float MaxFov = 80f;
    /// <summary>The plane's slowest and fastest speeds (Flight: 25..60 m/s).</summary>
    public const float SlowSpeed = 25f;
    public const float FastSpeed = 60f;

    /// <summary>How fast the position closes its gap to the target spot, per second.</summary>
    public const float PositionRate = 5f;
    /// <summary>How fast the velocity the camera is carried with follows the plane's, per second.</summary>
    public const float VelocityRate = 3f;
    /// <summary>How fast the rotation follows the plane's, per second, when not rolling.</summary>
    public const float RotationRate = 6f;
    /// <summary>Rolling slows the rotation: rate / (1 + this * roll rate in rad/s).</summary>
    public const float RollLag = 0.35f;
    /// <summary>How fast the field of view follows speed, per second.</summary>
    public const float FovRate = 2f;

    private CameraPose _pose = new(Vector3.Zero, Quaternion.Identity, MinFov);
    private CameraPose _previous = new(Vector3.Zero, Quaternion.Identity, MinFov);
    private PlaneState _plane;
    private bool _hasPlane;
    private bool _started;
    private Quaternion _follow = Quaternion.Identity;
    private Quaternion _lastPlaneOrientation = Quaternion.Identity;
    private Vector3 _position;
    private Vector3 _velocity;
    private float _fov = MinFov;
    private float _rollRate;

    /// <summary>The camera pose after the latest tick.</summary>
    public CameraPose Pose => _pose;

    /// <summary>The camera pose after the tick before; the view interpolates from it to <see cref="Pose"/>.</summary>
    public CameraPose PreviousPose => _previous;

    /// <summary>True once a pose follows the plane (a <see cref="PlaneState"/> arrived and a tick ran).</summary>
    public bool HasPose => _started;

    /// <summary>The lagged plane rotation the camera's offset and up come from.</summary>
    public Quaternion Follow => _follow;

    /// <summary>The plane's roll rate seen at the latest tick, rad/s (absolute).</summary>
    public float RollRate => _rollRate;

    public void Start(IMessageBus bus) => bus.Subscribe<PlaneState>(this);

    public void Handle(in PlaneState message)
    {
        if (message.PlaneId != PlaneId)
            return;
        _plane = message;
        _hasPlane = true;
    }

    public void Tick(in TickContext tick)
    {
        if (!_hasPlane)
            return;
        float dt = (float)tick.StepSeconds;
        var orientation = Quaternion.Normalize(_plane.Orientation);

        if (!_started)
        {
            // First sight of the plane: no lag to recover from, start in place.
            _started = true;
            _follow = orientation;
            _lastPlaneOrientation = orientation;
            _position = Target(_plane.Position, orientation);
            _velocity = _plane.Velocity;
            _fov = FovFor(_plane.Speed);
            _pose = Compose(orientation);
            _previous = _pose;
            return;
        }

        _previous = _pose;
        _rollRate = dt > 0f ? MathF.Abs(RollAngle(_lastPlaneOrientation, orientation)) / dt : 0f;
        _lastPlaneOrientation = orientation;

        // Rotation: follows the plane's, slower while it rolls.
        float rotationRate = RotationRate / (1f + RollLag * _rollRate);
        _follow = Quaternion.Normalize(Quaternion.Slerp(_follow, orientation, Blend(rotationRate, dt)));

        // Position: carried with a lagged copy of the plane's velocity, then closes its gap to
        // the target spot, so a steady plane is followed exactly and changes of speed or
        // heading stretch the camera back or swing it out, then settle.
        _velocity = Vector3.Lerp(_velocity, _plane.Velocity, Blend(VelocityRate, dt));
        _position += _velocity * dt;
        _position = Vector3.Lerp(_position, Target(_plane.Position, _follow), Blend(PositionRate, dt));

        _fov += (FovFor(_plane.Speed) - _fov) * Blend(FovRate, dt);
        _pose = Compose(orientation);
    }

    /// <summary>The field of view the camera settles to at <paramref name="speed"/> m/s.</summary>
    public static float FovFor(float speed)
    {
        float k = Math.Clamp((speed - SlowSpeed) / (FastSpeed - SlowSpeed), 0f, 1f);
        return MinFov + (MaxFov - MinFov) * k;
    }

    // The spot behind and above a plane at `position` with `orientation`.
    private static Vector3 Target(Vector3 position, Quaternion orientation) =>
        position + Vector3.Transform(new Vector3(0f, Up, Back), orientation);

    // Looks from the camera's position at a point ahead of the plane, its up from the lagged rotation.
    private CameraPose Compose(Quaternion planeOrientation)
    {
        var lookAt = _plane.Position + Vector3.Transform(new Vector3(0f, 0f, -LookAhead), planeOrientation);
        var up = Vector3.Transform(Vector3.UnitY, _follow);
        return new CameraPose(_position, LookRotation(lookAt - _position, up), _fov);
    }

    // Rotation whose -Z is `forward` and whose +Y is as close to `up` as possible.
    private static Quaternion LookRotation(Vector3 forward, Vector3 up)
    {
        var back = Vector3.Normalize(-forward);
        var right = Vector3.Cross(up, back);
        if (right.LengthSquared() < 1e-8f)
            right = MathF.Abs(back.X) < 0.9f ? Vector3.Cross(back, Vector3.UnitX) : Vector3.Cross(back, Vector3.UnitY);
        right = Vector3.Normalize(right);
        var trueUp = Vector3.Cross(back, right);
        var m = new Matrix4x4(
            right.X, right.Y, right.Z, 0f,
            trueUp.X, trueUp.Y, trueUp.Z, 0f,
            back.X, back.Y, back.Z, 0f,
            0f, 0f, 0f, 1f);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
    }

    // Signed roll (radians, about the body's Z axis) from orientation `a` to `b`.
    private static float RollAngle(Quaternion a, Quaternion b)
    {
        var delta = Quaternion.Conjugate(a) * b;
        if (delta.W < 0f)
            delta = Quaternion.Negate(delta);
        return 2f * MathF.Atan2(delta.Z, delta.W);
    }

    // Fraction of the gap closed in `dt` seconds at `rate` per second (frame-rate independent).
    private static float Blend(float rate, float dt) => 1f - MathF.Exp(-rate * dt);
}
