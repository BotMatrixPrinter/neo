using System;
using System.Numerics;
using Neo.Flight;
using Neo.Input;

namespace Neo.Bots.Tests;

/// <summary>
/// A tiny stand-in for the Flight module's stick model (same rates, smoothing and bank turn,
/// throttle sets the level speed 25..60 m/s, diving gains speed and climbing loses it at
/// 6 m/s² per g, clamped to 25..60; a hard ceiling at 1000 m; no floor or border guards), so
/// tests can fly a bot in closed loop with fake PlaneStates.
/// </summary>
internal sealed class FakeFlight
{
    private const float Deg = MathF.PI / 180f;
    /// <summary>Flight's ceiling, m.</summary>
    public const float Ceiling = 1000f;
    private float _pitch, _roll, _yaw;

    public PlaneState Step(in PlaneState s, in ControlInput input, float dt)
    {
        float follow = MathF.Min(1f, 15f * dt);
        _pitch += (input.Pitch - _pitch) * follow;
        _roll += (input.Roll - _roll) * follow;
        _yaw += (input.Yaw - _yaw) * follow;
        Quaternion body = Quaternion.CreateFromAxisAngle(Vector3.UnitX, _pitch * 70f * Deg * dt)
            * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -_roll * 180f * Deg * dt)
            * Quaternion.CreateFromAxisAngle(Vector3.UnitY, -_yaw * 40f * Deg * dt);
        Quaternion q = s.Orientation * body;
        float rightY = Vector3.Transform(Vector3.UnitX, q).Y;
        q = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitY, rightY * 30f * Deg * dt) * q);
        Vector3 forward = Vector3.Transform(-Vector3.UnitZ, q);
        float level = input.Throttle < 0.5f ? 25f + 34f * input.Throttle : 42f + 36f * (input.Throttle - 0.5f);
        float speed = s.Speed + (level - s.Speed) * MathF.Min(1f, 0.5f * dt);
        speed = Math.Clamp(speed - forward.Y * 6f * dt, 25f, 60f);
        Vector3 velocity = forward * speed;
        Vector3 position = s.Position + velocity * dt;
        if (position.Y > Ceiling)
        {
            position.Y = Ceiling;
            velocity.Y = 0f;
        }
        return new PlaneState(s.PlaneId, position, q, velocity, speed, input.Throttle, s.Alive);
    }

    /// <summary>A plane at <paramref name="position"/> flying level toward <paramref name="heading"/> (horizontal), at <paramref name="speed"/>.</summary>
    public static PlaneState Plane(int id, Vector3 position, Vector3 heading, float speed = 50f, bool alive = true)
        => Plane(id, position, Facing(heading), speed, alive);

    public static PlaneState Plane(int id, Vector3 position, Quaternion orientation, float speed = 50f, bool alive = true)
    {
        Vector3 velocity = Vector3.Transform(-Vector3.UnitZ, orientation) * speed;
        return new PlaneState(id, position, orientation, velocity, speed, 0.5f, alive);
    }

    /// <summary>Level orientation whose nose (-Z) points along the horizontal part of <paramref name="heading"/>.</summary>
    public static Quaternion Facing(Vector3 heading)
    {
        float yaw = MathF.Atan2(-heading.X, -heading.Z);
        return Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
    }
}
