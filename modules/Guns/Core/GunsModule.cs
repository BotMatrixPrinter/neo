using System;
using System.Numerics;
using Neo.Damage;
using Neo.Flight;
using Neo.Input;
using Neo.Shell;
using Neo.Targets;

namespace Neo.Guns;

/// <summary>
/// The Guns module: every plane's twin machine guns. While a plane is alive, its
/// <see cref="ControlInput.Fire"/> is held and its guns are not overheated, each gun fires
/// <see cref="RoundsPerSecond"/>; bullets fly straight from the muzzles at the plane's velocity
/// plus <see cref="MuzzleSpeed"/> along the nose and live <see cref="BulletLifeSeconds"/>. A
/// bullet that crosses an alive target's sphere is removed and publishes one
/// <see cref="DamageTarget"/>(id, 1, shooter); one that crosses an alive enemy plane's sphere
/// (<see cref="PlaneRadius"/>) is removed and publishes one <see cref="PlaneHit"/> for the part
/// hit. A plane that pops a balloon (<see cref="TargetDestroyed.ShooterPlaneId"/>) is boosted
/// for <see cref="BoostSeconds"/>: the bullets it fires meanwhile do <see cref="BoostFactor"/>x
/// damage. Publishes a <see cref="GunState"/> per plane every tick. Bullets live in a fixed pool;
/// allocation-free per tick.
/// </summary>
public sealed class GunsModule : IModule
{
    /// <summary>Planes with guns: ids 0..Planes-1; messages for other ids are ignored.</summary>
    public const int Planes = Teams.PlaneCount;

    public const int Guns = 2;

    /// <summary>Rounds per second, per gun.</summary>
    public const double RoundsPerSecond = 10;

    /// <summary>Bullet speed along the nose, added to the plane's velocity, m/s.</summary>
    public const float MuzzleSpeed = 450f;

    public const double BulletLifeSeconds = 1.5;

    /// <summary>Size of the bullet pool (8 planes x 2 guns x 10/s x 1.5 s = 240 live at most).</summary>
    public const int PoolSize = 256;

    /// <summary>Seconds of continuous fire from cold to overheated.</summary>
    public const double HeatUpSeconds = 4;

    /// <summary>Seconds to cool from full heat to cold.</summary>
    public const double CoolDownSeconds = 2;

    /// <summary>Overheated guns fire again once heat is below this.</summary>
    public const double ResumeHeat = 0.3;

    /// <summary>Highest target id tracked; targets with larger ids are ignored.</summary>
    public const int MaxTargets = 32;

    /// <summary>A plane is hit inside this sphere around its position, meters.</summary>
    public const float PlaneRadius = 4f;

    /// <summary>Health one bullet takes from the part of a plane it hits.</summary>
    public const float BulletDamage = 0.04f;

    /// <summary>Seconds a balloon pop boosts the popping plane's damage; a new pop restarts it (no stacking).</summary>
    public const double BoostSeconds = 60;

    /// <summary>Damage multiplier of a boosted plane's bullets (+50%).</summary>
    public const float BoostFactor = 1.5f;

    /// <summary>Body-space |x| beyond which a hit is on a wing, meters.</summary>
    public const float WingX = 1f;

    /// <summary>Body-space z (behind the center) beyond which a hit is on the tail, meters.</summary>
    public const float TailZ = 2f;

    /// <summary>Recent hit positions kept for the view's spark puffs.</summary>
    public const int HitRing = 32;

    /// <summary>Muzzle of the left gun in body space (+X right, -Z forward); the right one mirrors it.</summary>
    public static readonly Vector3 LeftMuzzle = new(-0.5f, 0f, -2f);

    private const double Epsilon = 1e-9;

    private readonly Bullet[] _bullets = new Bullet[PoolSize];
    private readonly Vector3[] _targetPositions = new Vector3[MaxTargets];
    private readonly float[] _targetRadii = new float[MaxTargets];
    private readonly bool[] _targetAlive = new bool[MaxTargets];
    private readonly Vector3[] _hits = new Vector3[HitRing];
    private readonly int[] _hitPlanes = new int[HitRing];
    private readonly ControlInput[] _inputs = new ControlInput[Planes];
    private readonly PlaneState[] _planes = new PlaneState[Planes];
    private readonly bool[] _hasPlane = new bool[Planes];
    private readonly double[] _heat = new double[Planes];
    private readonly double[] _shotTimer = new double[Planes];
    private readonly GunState[] _states = new GunState[Planes];
    private readonly long[] _lastVolley = new long[Planes];
    private readonly double[] _boost = new double[Planes];
    private readonly float[] _balloonCarry = new float[Planes];
    private readonly InputHandler _inputHandler;
    private readonly PlaneHandler _planeHandler;
    private readonly TargetHandler _targetHandler;
    private readonly DestroyedHandler _destroyedHandler;
    private IMessageBus? _bus;

    public GunsModule()
    {
        _inputHandler = new InputHandler(this);
        _planeHandler = new PlaneHandler(this);
        _targetHandler = new TargetHandler(this);
        _destroyedHandler = new DestroyedHandler(this);
        for (int p = 0; p < Planes; p++)
            _states[p] = new GunState(p, 0f, false, false, 0);
    }

    /// <summary>Plane 0's (the player's) state published on the last tick.</summary>
    public GunState State => _states[0];

    /// <summary>The state of plane <paramref name="planeId"/>'s guns published on the last tick.</summary>
    public GunState StateOf(int planeId) => _states[planeId];

    /// <summary>Tick number of plane <paramref name="planeId"/>'s last volley (0 before any), for muzzle flashes.</summary>
    public long LastVolleyTick(int planeId) => _lastVolley[planeId];

    /// <summary>The bullet pool; only entries with <see cref="Bullet.Alive"/> are in flight.</summary>
    public ReadOnlySpan<Bullet> Bullets => _bullets;

    /// <summary>Bullets in flight now.</summary>
    public int LiveBullets
    {
        get
        {
            int count = 0;
            for (int i = 0; i < PoolSize; i++)
                if (_bullets[i].Alive)
                    count++;
            return count;
        }
    }

    /// <summary>Hits since start, on targets and planes.</summary>
    public int Hits { get; private set; }

    /// <summary>Where hit number <paramref name="hit"/> (0-based) happened; only the last <see cref="HitRing"/> are kept.</summary>
    public Vector3 HitPosition(int hit) => _hits[hit % HitRing];

    /// <summary>The plane hit number <paramref name="hit"/> struck, or -1 for a target (balloon).</summary>
    public int HitPlane(int hit) => _hitPlanes[hit % HitRing];

    /// <summary>True when a <see cref="PlaneState"/> has been received for <paramref name="planeId"/>.</summary>
    public bool HasPlane(int planeId) => _hasPlane[planeId];

    /// <summary>True while plane <paramref name="planeId"/>'s damage boost runs (its bullets fired now are boosted).</summary>
    public bool Boosted(int planeId) => _boost[planeId] > Epsilon;

    /// <summary>The last <see cref="PlaneState"/> received for <paramref name="planeId"/>.</summary>
    public PlaneState Plane(int planeId) => _planes[planeId];

    /// <summary>World position of plane <paramref name="planeId"/>'s gun <paramref name="gun"/> muzzle (0 left, 1 right) on its last state.</summary>
    public Vector3 MuzzlePosition(int planeId, int gun)
    {
        var offset = gun == 0 ? LeftMuzzle : new Vector3(-LeftMuzzle.X, LeftMuzzle.Y, LeftMuzzle.Z);
        ref readonly var plane = ref _planes[planeId];
        return plane.Position + Vector3.Transform(offset, plane.Orientation);
    }

    /// <summary>
    /// The part hit at <paramref name="hit"/> (world space) on a plane at <paramref name="position"/>
    /// turned by <paramref name="orientation"/>: in the plane's frame, x below -1 m the left wing,
    /// above 1 m the right wing, z above 2 m (behind) the tail, otherwise the engine.
    /// </summary>
    public static PlanePart PartAt(Vector3 hit, Vector3 position, Quaternion orientation)
    {
        var local = Vector3.Transform(hit - position, Quaternion.Conjugate(orientation));
        if (local.X < -WingX)
            return PlanePart.LeftWing;
        if (local.X > WingX)
            return PlanePart.RightWing;
        if (local.Z > TailZ)
            return PlanePart.Tail;
        return PlanePart.Engine;
    }

    public void Start(IMessageBus bus)
    {
        _bus = bus;
        bus.Subscribe(_inputHandler);
        bus.Subscribe(_planeHandler);
        bus.Subscribe(_targetHandler);
        bus.Subscribe(_destroyedHandler);
    }

    public void Tick(in TickContext tick)
    {
        double dt = tick.StepSeconds;
        for (int p = 0; p < Planes; p++)
            TickGuns(p, tick.Tick, dt);
        Fly(dt);
        for (int p = 0; p < Planes; p++)
            _bus?.Publish(_states[p]);
    }

    private void TickGuns(int p, long tick, double dt)
    {
        var state = _states[p];
        bool firing = _inputs[p].Fire && _hasPlane[p] && _planes[p].Alive && !state.Overheated;
        bool overheated = state.Overheated;
        int shots = state.ShotsFired;
        bool boosted = _boost[p] > Epsilon;

        if (firing)
        {
            while (_shotTimer[p] <= Epsilon)
            {
                for (int gun = 0; gun < Guns; gun++)
                    Spawn(p, gun, shots++, boosted);
                _shotTimer[p] += 1.0 / RoundsPerSecond;
                _lastVolley[p] = tick;
            }
            _shotTimer[p] -= dt;
            _heat[p] = Math.Min(1.0, _heat[p] + dt / HeatUpSeconds);
            if (_heat[p] >= 1.0 - Epsilon)
            {
                _heat[p] = 1.0;
                overheated = true;
            }
        }
        else
        {
            _shotTimer[p] = Math.Max(0.0, _shotTimer[p] - dt);
            _heat[p] = Math.Max(0.0, _heat[p] - dt / CoolDownSeconds);
            if (overheated && _heat[p] < ResumeHeat)
                overheated = false;
        }

        _boost[p] = _boost[p] - dt > Epsilon ? _boost[p] - dt : 0.0;
        _states[p] = new GunState(p, (float)_heat[p], overheated, firing, shots, (float)_boost[p]);
    }

    private void Spawn(int shooter, int gun, int serial, bool boosted)
    {
        for (int i = 0; i < PoolSize; i++)
        {
            if (_bullets[i].Alive)
                continue;
            ref readonly var plane = ref _planes[shooter];
            var forward = Vector3.Transform(-Vector3.UnitZ, plane.Orientation);
            var muzzle = MuzzlePosition(shooter, gun);
            _bullets[i] = new Bullet(muzzle, muzzle, plane.Velocity + forward * MuzzleSpeed, 0, serial, gun, true, shooter, boosted);
            return;
        }
    }

    // Each live bullet moves one step; its segment is tested against every alive target sphere
    // and every alive enemy plane's sphere, and the first one it enters is hit.
    private void Fly(double dt)
    {
        for (int i = 0; i < PoolSize; i++)
        {
            ref var bullet = ref _bullets[i];
            if (!bullet.Alive)
                continue;
            var from = bullet.Position;
            var to = from + bullet.Velocity * (float)dt;
            double age = bullet.Age + dt;
            int target = FirstTarget(from, to, out float targetT);
            int plane = FirstEnemyPlane(bullet.Shooter, from, to, out float planeT);
            if (plane >= 0 && planeT <= targetT)
            {
                var at = Vector3.Lerp(from, to, planeT);
                ref readonly var hit = ref _planes[plane];
                var part = PartAt(at, hit.Position, hit.Orientation);
                int shooter = bullet.Shooter;
                float amount = bullet.Boosted ? BulletDamage * BoostFactor : BulletDamage;
                bullet = default;
                Record(at, plane);
                _bus?.Publish(new PlaneHit(plane, shooter, part, amount));
                continue;
            }
            if (target >= 0)
            {
                int shooter = bullet.Shooter;
                int amount = bullet.Boosted ? BoostedBalloonDamage(shooter) : 1;
                bullet = default;
                Record(Vector3.Lerp(from, to, targetT), -1);
                _bus?.Publish(new DamageTarget(target, amount, shooter));
                continue;
            }
            bullet = age >= BulletLifeSeconds - Epsilon
                ? default
                : new Bullet(to, from, bullet.Velocity, age, bullet.Serial, bullet.Gun, true, bullet.Shooter, bullet.Boosted);
        }
    }

    // A boosted bullet's balloon damage: 1 x 1.5, rounded down, with the half left over carried
    // to the plane's next boosted balloon hit, so its hits go 1, 2, 1, 2 (1.5 on average) and
    // never below 1.
    private int BoostedBalloonDamage(int shooter)
    {
        float exact = BoostFactor + _balloonCarry[shooter];
        int amount = (int)MathF.Floor(exact + 1e-4f);
        _balloonCarry[shooter] = exact - amount;
        return amount;
    }

    private void Record(Vector3 at, int plane)
    {
        _hits[Hits % HitRing] = at;
        _hitPlanes[Hits % HitRing] = plane;
        Hits++;
    }

    // The alive target whose sphere the segment from..to enters first, or -1.
    private int FirstTarget(Vector3 from, Vector3 to, out float hitT)
    {
        int best = -1;
        hitT = 2f;
        for (int id = 0; id < MaxTargets; id++)
        {
            if (!_targetAlive[id])
                continue;
            float t = Enter(from, to, _targetPositions[id], _targetRadii[id]);
            if (t < hitT)
            {
                hitT = t;
                best = id;
            }
        }
        return best;
    }

    // The alive plane of the other team than `shooter` whose sphere the segment enters first, or -1.
    private int FirstEnemyPlane(int shooter, Vector3 from, Vector3 to, out float hitT)
    {
        int best = -1;
        hitT = 2f;
        bool allies = Teams.IsAllies(shooter);
        for (int id = 0; id < Planes; id++)
        {
            if (!_hasPlane[id] || !_planes[id].Alive || Teams.IsAllies(id) == allies)
                continue;
            float t = Enter(from, to, _planes[id].Position, PlaneRadius);
            if (t < hitT)
            {
                hitT = t;
                best = id;
            }
        }
        return best;
    }

    // Fraction 0..1 along from..to where it enters the sphere (0 if it starts inside), or 2 for a miss.
    private static float Enter(Vector3 from, Vector3 to, Vector3 center, float radius)
    {
        var d = to - from;
        var f = from - center;
        float c = f.LengthSquared() - radius * radius;
        if (c <= 0f)
            return 0f;
        float a = d.LengthSquared();
        if (a <= 0f)
            return 2f;
        float b = 2f * Vector3.Dot(f, d);
        float disc = b * b - 4f * a * c;
        if (disc < 0f)
            return 2f;
        float t = (-b - MathF.Sqrt(disc)) / (2f * a);
        return t < 0f || t > 1f ? 2f : t;
    }

    private sealed class InputHandler : IMessageHandler<ControlInput>
    {
        private readonly GunsModule _module;

        public InputHandler(GunsModule module) => _module = module;

        public void Handle(in ControlInput message)
        {
            if ((uint)message.PlaneId < Planes)
                _module._inputs[message.PlaneId] = message;
        }
    }

    private sealed class PlaneHandler : IMessageHandler<PlaneState>
    {
        private readonly GunsModule _module;

        public PlaneHandler(GunsModule module) => _module = module;

        public void Handle(in PlaneState message)
        {
            int id = message.PlaneId;
            if ((uint)id >= Planes)
                return;
            _module._planes[id] = message;
            _module._hasPlane[id] = true;
        }
    }

    private sealed class DestroyedHandler : IMessageHandler<TargetDestroyed>
    {
        private readonly GunsModule _module;

        public DestroyedHandler(GunsModule module) => _module = module;

        public void Handle(in TargetDestroyed message)
        {
            int id = message.ShooterPlaneId;
            if ((uint)id < Planes)
                _module._boost[id] = BoostSeconds;
        }
    }

    private sealed class TargetHandler : IMessageHandler<TargetState>
    {
        private readonly GunsModule _module;

        public TargetHandler(GunsModule module) => _module = module;

        public void Handle(in TargetState message)
        {
            int id = message.TargetId;
            if ((uint)id >= MaxTargets)
                return;
            _module._targetPositions[id] = message.Position;
            _module._targetRadii[id] = message.Radius;
            _module._targetAlive[id] = message.Alive && message.Radius > 0f;
        }
    }
}

/// <summary>One bullet of the pool, for the view.</summary>
public readonly struct Bullet
{
    public Bullet(Vector3 position, Vector3 previousPosition, Vector3 velocity, double age, int serial, int gun, bool alive, int shooter)
        : this(position, previousPosition, velocity, age, serial, gun, alive, shooter, false)
    {
    }

    public Bullet(Vector3 position, Vector3 previousPosition, Vector3 velocity, double age, int serial, int gun, bool alive, int shooter, bool boosted)
    {
        Boosted = boosted;
        Shooter = shooter;
        Position = position;
        PreviousPosition = previousPosition;
        Velocity = velocity;
        Age = age;
        Serial = serial;
        Gun = gun;
        Alive = alive;
    }

    /// <summary>World position after the last tick, meters.</summary>
    public Vector3 Position { get; }

    /// <summary>World position before the last tick (to interpolate between ticks).</summary>
    public Vector3 PreviousPosition { get; }

    /// <summary>World velocity, m/s; constant (bullets fly straight).</summary>
    public Vector3 Velocity { get; }

    /// <summary>Seconds in flight.</summary>
    public double Age { get; }

    /// <summary>The plane that fired it.</summary>
    public int Shooter { get; }

    /// <summary>Its plane's shot number since start (0-based); every third one is a tracer.</summary>
    public int Serial { get; }

    /// <summary>0 left gun, 1 right gun.</summary>
    public int Gun { get; }

    /// <summary>Fired while its plane's damage boost ran: it does 1.5x damage and its tracer looks hotter.</summary>
    public bool Boosted { get; }

    /// <summary>In flight; false for a free pool slot.</summary>
    public bool Alive { get; }
}
