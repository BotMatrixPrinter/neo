using System;
using System.Numerics;
using Neo.Damage;
using Neo.Flight;
using Neo.Shell;

namespace Neo.Collisions;

/// <summary>
/// The Collisions module: planes that ram each other. Each tick every pair of alive planes
/// (any teams) is tested as two spheres of <see cref="PlaneRadius"/>; a pair that touches now
/// but did not on the previous tick is one collision. A collision publishes, for each of the two
/// planes, one <see cref="PlaneHit"/> (the other plane as the shooter, the part from the contact
/// point) and one <see cref="CollisionImpulse"/> (the bounce). That pair does nothing more
/// until it has separated. Deterministic; allocation-free per tick.
/// </summary>
public sealed class CollisionsModule : IModule
{
    /// <summary>Planes tested: ids 0..Planes-1; states of other ids are ignored.</summary>
    public const int Planes = Teams.PlaneCount;

    /// <summary>Radius of each plane's collision sphere, meters (two planes touch within 7 m).</summary>
    public const float PlaneRadius = 3.5f;

    /// <summary>
    /// Part damage per m/s of closing speed, for two planes of equal speed (linear term). With
    /// <see cref="DamagePerMpsSquared"/>: about 0.05 at 10 m/s, 0.5 at 60 m/s, 1.1 at 100 m/s.
    /// </summary>
    public const float DamagePerMps = 0.013f / 3f;

    /// <summary>Part damage per (m/s)² of closing speed, equal speeds (the faster-than-linear term).</summary>
    public const float DamagePerMpsSquared = 0.0002f / 3f;

    /// <summary>Most damage one collision does to one plane: the whole part.</summary>
    public const float MaxPartDamage = 1f;

    /// <summary>
    /// Restitution along the contact normal: after the collision the planes separate at this
    /// share of the speed they closed at (0 would stick, 1 a full bounce).
    /// </summary>
    public const float Restitution = 0.3f;

    /// <summary>Roll kick, rad/s per m/s of velocity change, for a hit at the wingtip (none inboard of the wings, 1 m out).</summary>
    public const float RollKickPerMps = 0.08f;

    /// <summary>Pitch and yaw kick, rad/s per m/s of velocity change, for a hit at the nose or tail end of the sphere.</summary>
    public const float PitchYawKickPerMps = 0.06f;

    /// <summary>Largest kick on any one axis, rad/s.</summary>
    public const float MaxAngularKick = 5f;

    /// <summary>Extra gap, meters, the separation leaves between the two spheres, so they are clear.</summary>
    public const float SeparationSlack = 0.01f;

    /// <summary>Collisions kept for the view (<see cref="CollisionPosition"/>), a ring.</summary>
    public const int CollisionRing = 16;

    // The parts, as the guns decide them (Neo.Guns.GunsModule.PartAt), in the plane's frame.
    private const float WingX = 1f;
    private const float TailZ = 2f;

    private readonly PlaneState[] _planes = new PlaneState[Planes];
    private readonly bool[] _known = new bool[Planes];
    private readonly bool[] _touching = new bool[Planes * Planes];
    private readonly Vector3[] _collisionPositions = new Vector3[CollisionRing];
    private readonly float[] _collisionSpeeds = new float[CollisionRing];
    private readonly Vector3[] _collisionVelocities = new Vector3[CollisionRing];
    private readonly PlaneHandler _planeHandler;
    private IMessageBus? _bus;

    public CollisionsModule()
    {
        _planeHandler = new PlaneHandler(this);
    }

    /// <summary>Collisions so far.</summary>
    public int Collisions { get; private set; }

    /// <summary>World contact point of collision <paramref name="n"/> (0-based; the last <see cref="CollisionRing"/> are kept).</summary>
    public Vector3 CollisionPosition(int n) => _collisionPositions[n % CollisionRing];

    /// <summary>Closing speed, m/s, of collision <paramref name="n"/>.</summary>
    public float CollisionSpeed(int n) => _collisionSpeeds[n % CollisionRing];

    /// <summary>Mean world velocity, m/s, of the two planes of collision <paramref name="n"/> (where its debris drifts).</summary>
    public Vector3 CollisionVelocity(int n) => _collisionVelocities[n % CollisionRing];

    /// <summary>True while planes <paramref name="a"/> and <paramref name="b"/> touch (as of the last tick).</summary>
    public bool Touching(int a, int b) =>
        (uint)a < Planes && (uint)b < Planes && a != b && _touching[Math.Min(a, b) * Planes + Math.Max(a, b)];

    /// <summary>
    /// Damage the whole collision does at <paramref name="closingSpeed"/> m/s, before the split:
    /// twice the part damage two planes of equal speed each take.
    /// </summary>
    public static float TotalDamage(float closingSpeed)
    {
        float v = MathF.Max(0f, closingSpeed);
        return 2f * (DamagePerMps * v + DamagePerMpsSquared * v * v);
    }

    /// <summary>
    /// Damage to a plane at <paramref name="ownSpeed"/> hit by one at <paramref name="otherSpeed"/>:
    /// the total times the other's share of the two speeds (half each if both are 0), at most a part.
    /// </summary>
    public static float DamageTaken(float closingSpeed, float ownSpeed, float otherSpeed)
    {
        float own = MathF.Max(0f, ownSpeed);
        float other = MathF.Max(0f, otherSpeed);
        float sum = own + other;
        float share = sum > 0f ? other / sum : 0.5f;
        return MathF.Min(MaxPartDamage, TotalDamage(closingSpeed) * share);
    }

    /// <summary>
    /// The part at <paramref name="point"/> (world) on a plane at <paramref name="position"/> turned
    /// by <paramref name="orientation"/>, as the guns decide it: in the plane's frame, x below -1 m
    /// the left wing, above 1 m the right wing, z above 2 m (behind) the tail, otherwise the engine.
    /// </summary>
    public static PlanePart PartAt(Vector3 point, Vector3 position, Quaternion orientation)
    {
        var local = Vector3.Transform(point - position, Quaternion.Conjugate(orientation));
        if (local.X < -WingX)
            return PlanePart.LeftWing;
        if (local.X > WingX)
            return PlanePart.RightWing;
        if (local.Z > TailZ)
            return PlanePart.Tail;
        return PlanePart.Engine;
    }

    /// <summary>
    /// The arcade spin a plane gets when struck at <paramref name="localContact"/> (the contact
    /// point in its own frame, meters from its center) by a velocity change of
    /// <paramref name="deltaSpeed"/> m/s. The contact point lies toward the other plane:
    /// <list type="bullet">
    /// <item>roll (z): the struck side's wing is knocked away from the other plane: up when it is
    /// level or below, down when it is above (struck on the right wing, level: banks left, away),
    /// <see cref="RollKickPerMps"/> at the wingtip, scaled down to none at 1 m out (a hit on the
    /// fuselage does not roll it);</item>
    /// <item>pitch (x) and yaw (y): the nose or tail is knocked away from the other plane, up/down
    /// and sideways by the direction it came from, <see cref="PitchYawKickPerMps"/> at the end of
    /// the sphere, scaled by z / <see cref="PlaneRadius"/> (a straight push along the fuselage
    /// does not turn it).</item>
    /// </list>
    /// Each axis is capped at <see cref="MaxAngularKick"/>.
    /// </summary>
    public static Vector3 AngularKickAt(Vector3 localContact, float deltaSpeed)
    {
        float length = localContact.Length();
        if (length < 1e-6f || !(deltaSpeed > 0f))
            return Vector3.Zero;
        var dir = localContact / length;
        // Only the wing outboard of the fuselage rolls the plane: 0 at WingX, 1 at the wingtip.
        float outboard = Math.Clamp((MathF.Abs(localContact.X) - WingX) / (PlaneRadius - WingX), 0f, 1f);
        float lateral = MathF.Sign(localContact.X) * outboard;
        float lengthwise = Math.Clamp(localContact.Z / PlaneRadius, -1f, 1f);
        float roll = RollKickPerMps * deltaSpeed * lateral * (localContact.Y > 0f ? -1f : 1f);
        float pitch = PitchYawKickPerMps * deltaSpeed * lengthwise * dir.Y;
        float yaw = -PitchYawKickPerMps * deltaSpeed * lengthwise * dir.X;
        return new Vector3(
            Math.Clamp(pitch, -MaxAngularKick, MaxAngularKick),
            Math.Clamp(yaw, -MaxAngularKick, MaxAngularKick),
            Math.Clamp(roll, -MaxAngularKick, MaxAngularKick));
    }

    public void Start(IMessageBus bus)
    {
        _bus = bus;
        bus.Subscribe(_planeHandler);
    }

    public void Tick(in TickContext tick)
    {
        for (int a = 0; a < Planes; a++)
        {
            for (int b = a + 1; b < Planes; b++)
            {
                int pair = a * Planes + b;
                bool touching = _known[a] && _known[b] && _planes[a].Alive && _planes[b].Alive && Overlap(a, b);
                if (touching && !_touching[pair])
                    Collide(a, b);
                _touching[pair] = touching;
            }
        }
    }

    private bool Overlap(int a, int b)
    {
        const float reach = 2f * PlaneRadius;
        return Vector3.DistanceSquared(_planes[a].Position, _planes[b].Position) < reach * reach;
    }

    private void Collide(int a, int b)
    {
        ref readonly var pa = ref _planes[a];
        ref readonly var pb = ref _planes[b];
        var relative = pb.Velocity - pa.Velocity;
        float closing = relative.Length();

        // Contact normal from a's center to b's; for planes on the same spot, the way b moves
        // relative to a reversed (it came from there), else straight up.
        var offset = pb.Position - pa.Position;
        float distance = offset.Length();
        Vector3 n;
        if (distance > 1e-4f)
            n = offset / distance;
        else if (closing > 1e-4f)
            n = -relative / closing;
        else
            n = Vector3.UnitY;
        var contact = pa.Position + n * (distance * 0.5f);

        // Equal masses: each takes half of the impulse that turns the closing speed along n into
        // Restitution times it, apart.
        float approach = MathF.Max(0f, -Vector3.Dot(relative, n));
        float deltaSpeed = 0.5f * (1f + Restitution) * approach;
        var push = (0.5f * (2f * PlaneRadius - distance) + SeparationSlack) * n;

        float speedA = pa.Speed;
        float speedB = pb.Speed;
        Hit(a, b, contact, DamageTaken(closing, speedA, speedB));
        Hit(b, a, contact, DamageTaken(closing, speedB, speedA));

        var bus = _bus;
        if (bus != null)
        {
            var localA = Vector3.Transform(contact - pa.Position, Quaternion.Conjugate(pa.Orientation));
            var localB = Vector3.Transform(contact - pb.Position, Quaternion.Conjugate(pb.Orientation));
            bus.Publish(new CollisionImpulse(a, -deltaSpeed * n, AngularKickAt(localA, deltaSpeed), -push));
            bus.Publish(new CollisionImpulse(b, deltaSpeed * n, AngularKickAt(localB, deltaSpeed), push));
        }

        int slot = Collisions % CollisionRing;
        _collisionPositions[slot] = contact;
        _collisionSpeeds[slot] = closing;
        _collisionVelocities[slot] = 0.5f * (pa.Velocity + pb.Velocity);
        Collisions++;
    }

    private void Hit(int target, int other, Vector3 contact, float amount)
    {
        if (!(amount > 0f) || _bus == null)
            return;
        ref readonly var plane = ref _planes[target];
        _bus.Publish(new PlaneHit(target, other, PartAt(contact, plane.Position, plane.Orientation), amount));
    }

    private sealed class PlaneHandler : IMessageHandler<PlaneState>
    {
        private readonly CollisionsModule _module;

        public PlaneHandler(CollisionsModule module) => _module = module;

        public void Handle(in PlaneState message)
        {
            int id = message.PlaneId;
            if ((uint)id >= Planes)
                return;
            _module._planes[id] = message;
            _module._known[id] = true;
        }
    }
}
