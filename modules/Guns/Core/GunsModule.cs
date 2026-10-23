using System;
using System.Numerics;
using Neo.Flight;
using Neo.Input;
using Neo.Shell;
using Neo.Targets;

namespace Neo.Guns;

/// <summary>
/// The Guns module: plane 0's twin machine guns. While <see cref="ControlInput.Fire"/> is held
/// and the guns are not overheated, each gun fires <see cref="RoundsPerSecond"/>; bullets fly
/// straight from the muzzles at the plane's velocity plus <see cref="MuzzleSpeed"/> along the
/// nose, live <see cref="BulletLifeSeconds"/>, and each one that crosses an alive target's
/// sphere is removed and publishes one <see cref="DamageTarget"/>(id, 1). Publishes a
/// <see cref="GunState"/> every tick. Bullets live in a fixed pool; allocation-free per tick.
/// </summary>
public sealed class GunsModule : IModule
{
    public const int PlaneId = 0;
    public const int Guns = 2;

    /// <summary>Rounds per second, per gun.</summary>
    public const double RoundsPerSecond = 10;

    /// <summary>Bullet speed along the nose, added to the plane's velocity, m/s.</summary>
    public const float MuzzleSpeed = 450f;

    public const double BulletLifeSeconds = 1.5;

    /// <summary>Size of the bullet pool (2 guns x 10/s x 1.5 s = 30 live at most).</summary>
    public const int PoolSize = 64;

    /// <summary>Seconds of continuous fire from cold to overheated.</summary>
    public const double HeatUpSeconds = 4;

    /// <summary>Seconds to cool from full heat to cold.</summary>
    public const double CoolDownSeconds = 2;

    /// <summary>Overheated guns fire again once heat is below this.</summary>
    public const double ResumeHeat = 0.3;

    /// <summary>Highest target id tracked; targets with larger ids are ignored.</summary>
    public const int MaxTargets = 32;

    /// <summary>Recent hit positions kept for the view's spark puffs.</summary>
    public const int HitRing = 16;

    /// <summary>Muzzle of the left gun in body space (+X right, -Z forward); the right one mirrors it.</summary>
    public static readonly Vector3 LeftMuzzle = new(-0.5f, 0f, -2f);

    private const double Epsilon = 1e-9;

    private readonly Bullet[] _bullets = new Bullet[PoolSize];
    private readonly Vector3[] _targetPositions = new Vector3[MaxTargets];
    private readonly float[] _targetRadii = new float[MaxTargets];
    private readonly bool[] _targetAlive = new bool[MaxTargets];
    private readonly Vector3[] _hits = new Vector3[HitRing];
    private readonly InputHandler _inputHandler;
    private readonly PlaneHandler _planeHandler;
    private readonly TargetHandler _targetHandler;
    private IMessageBus? _bus;
    private ControlInput _input;
    private PlaneState _plane;
    private bool _hasPlane;
    private double _heat;
    private double _shotTimer;

    public GunsModule()
    {
        _inputHandler = new InputHandler(this);
        _planeHandler = new PlaneHandler(this);
        _targetHandler = new TargetHandler(this);
    }

    /// <summary>The state published on the last tick.</summary>
    public GunState State { get; private set; }

    /// <summary>Tick number of the last volley (0 before any), for muzzle flashes.</summary>
    public long LastVolleyTick { get; private set; }

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

    /// <summary>Hits since start.</summary>
    public int Hits { get; private set; }

    /// <summary>Where hit number <paramref name="hit"/> (0-based) happened; only the last <see cref="HitRing"/> are kept.</summary>
    public Vector3 HitPosition(int hit) => _hits[hit % HitRing];

    /// <summary>World position of gun <paramref name="gun"/>'s muzzle (0 left, 1 right) on the last plane state.</summary>
    public Vector3 MuzzlePosition(int gun)
    {
        var offset = gun == 0 ? LeftMuzzle : new Vector3(-LeftMuzzle.X, LeftMuzzle.Y, LeftMuzzle.Z);
        return _plane.Position + Vector3.Transform(offset, _plane.Orientation);
    }

    public void Start(IMessageBus bus)
    {
        _bus = bus;
        bus.Subscribe(_inputHandler);
        bus.Subscribe(_planeHandler);
        bus.Subscribe(_targetHandler);
    }

    public void Tick(in TickContext tick)
    {
        double dt = tick.StepSeconds;
        bool firing = _input.Fire && _hasPlane && !State.Overheated;
        bool overheated = State.Overheated;
        int shots = State.ShotsFired;

        if (firing)
        {
            while (_shotTimer <= Epsilon)
            {
                for (int gun = 0; gun < Guns; gun++)
                    Spawn(gun, shots++);
                _shotTimer += 1.0 / RoundsPerSecond;
                LastVolleyTick = tick.Tick;
            }
            _shotTimer -= dt;
            _heat = Math.Min(1.0, _heat + dt / HeatUpSeconds);
            if (_heat >= 1.0 - Epsilon)
            {
                _heat = 1.0;
                overheated = true;
            }
        }
        else
        {
            _shotTimer = Math.Max(0.0, _shotTimer - dt);
            _heat = Math.Max(0.0, _heat - dt / CoolDownSeconds);
            if (overheated && _heat < ResumeHeat)
                overheated = false;
        }

        State = new GunState(PlaneId, (float)_heat, overheated, firing, shots);
        Fly(dt);
        _bus?.Publish(State);
    }

    private void Spawn(int gun, int serial)
    {
        for (int i = 0; i < PoolSize; i++)
        {
            if (_bullets[i].Alive)
                continue;
            var forward = Vector3.Transform(-Vector3.UnitZ, _plane.Orientation);
            var muzzle = MuzzlePosition(gun);
            _bullets[i] = new Bullet(muzzle, muzzle, _plane.Velocity + forward * MuzzleSpeed, 0, serial, gun, true);
            return;
        }
    }

    // Each live bullet moves one step; its segment is tested against every alive target sphere.
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
            int target = FirstHit(from, to, out float t);
            if (target >= 0)
            {
                bullet = default;
                _hits[Hits % HitRing] = Vector3.Lerp(from, to, t);
                Hits++;
                _bus?.Publish(new DamageTarget(target, 1));
                continue;
            }
            bullet = age >= BulletLifeSeconds - Epsilon
                ? default
                : new Bullet(to, from, bullet.Velocity, age, bullet.Serial, bullet.Gun, true);
        }
    }

    // The alive target whose sphere the segment from..to enters first, or -1.
    private int FirstHit(Vector3 from, Vector3 to, out float hitT)
    {
        int best = -1;
        hitT = 2f;
        var d = to - from;
        float a = d.LengthSquared();
        for (int id = 0; id < MaxTargets; id++)
        {
            if (!_targetAlive[id])
                continue;
            var f = from - _targetPositions[id];
            float r2 = _targetRadii[id] * _targetRadii[id];
            float c = f.LengthSquared() - r2;
            float t;
            if (c <= 0f)
                t = 0f;
            else
            {
                if (a <= 0f)
                    continue;
                float b = 2f * Vector3.Dot(f, d);
                float disc = b * b - 4f * a * c;
                if (disc < 0f)
                    continue;
                t = (-b - MathF.Sqrt(disc)) / (2f * a);
                if (t < 0f || t > 1f)
                    continue;
            }
            if (t < hitT)
            {
                hitT = t;
                best = id;
            }
        }
        return best;
    }

    private sealed class InputHandler : IMessageHandler<ControlInput>
    {
        private readonly GunsModule _module;

        public InputHandler(GunsModule module) => _module = module;

        public void Handle(in ControlInput message)
        {
            if (message.PlaneId == PlaneId)
                _module._input = message;
        }
    }

    private sealed class PlaneHandler : IMessageHandler<PlaneState>
    {
        private readonly GunsModule _module;

        public PlaneHandler(GunsModule module) => _module = module;

        public void Handle(in PlaneState message)
        {
            if (message.PlaneId != PlaneId)
                return;
            _module._plane = message;
            _module._hasPlane = true;
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
    public Bullet(Vector3 position, Vector3 previousPosition, Vector3 velocity, double age, int serial, int gun, bool alive)
    {
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

    /// <summary>Shot number since start (0-based); every third one is a tracer.</summary>
    public int Serial { get; }

    /// <summary>0 left gun, 1 right gun.</summary>
    public int Gun { get; }

    /// <summary>In flight; false for a free pool slot.</summary>
    public bool Alive { get; }
}
