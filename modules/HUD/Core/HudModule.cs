using System.Numerics;
using Neo.Damage;
using Neo.Flight;
using Neo.Guns;
using Neo.Shell;
using Neo.Targets;

namespace Neo.HUD;

/// <summary>
/// The HUD module: what the on-screen display shows, read from other modules' messages.
/// Plane 0's speed (km/h) and altitude (m) from <see cref="PlaneState"/>, its gun heat and
/// overheat from <see cref="GunState"/>, and the balloons popped (one per
/// <see cref="TargetDestroyed"/>). Speed and altitude are sampled each Tick, with the previous
/// Tick's values kept so the view can interpolate. From the Damage module: plane 0's part health
/// (<see cref="PlaneDamageState"/>), the kill feed and plane 0's kills and deaths
/// (<see cref="PlaneDestroyed"/>), and the "shot down" countdown until its
/// <see cref="PlaneRespawn"/>, and plane 0's in-flight repair (<see cref="RepairState"/>). Publishes nothing; allocation-free (texts come from tables built once).
/// </summary>
public sealed class HudModule : IModule
{
    public const int PlaneId = 0;

    /// <summary>m/s to km/h.</summary>
    public const float KmhPerMetersPerSecond = 3.6f;

    /// <summary>A part below this health is orange.</summary>
    public const float WornBelow = 0.6f;

    /// <summary>A part below this health is red.</summary>
    public const float CriticalBelow = 0.3f;

    /// <summary>Seconds a kill feed line stays up.</summary>
    public const double KillFeedSeconds = 5.0;

    /// <summary>Most kill feed lines shown at once; a new one pushes the oldest out.</summary>
    public const int KillFeedCapacity = 6;

    /// <summary>Plane ids 0-3 are the Allies (0 the player, 1-3 "Blue"), 4-7 the Central ("Red").</summary>
    public const int Planes = 8;

    /// <summary>The first id of team Central.</summary>
    public const int FirstEnemy = 4;

    /// <summary>Seconds "Repair cancelled" stays up after Repair is let go before a cycle finished.</summary>
    public const double RepairCancelledSeconds = 1.5;

    /// <summary>Shown under the repair: "Repair cancelled".</summary>
    public const string RepairCancelledText = "Repair cancelled";

    /// <summary>Shown while a part is damaged and no repair runs.</summary>
    public const string RepairHintText = "Hold Y / R: repair";

    private const int MaxCountdown = 9;
    private const int PartCount = 4;

    private static readonly string[] Names = BuildNames();
    private static readonly string[] KillLines = BuildKillLines();
    private static readonly string[] RespawnTexts = BuildRespawnTexts();
    private static readonly string[] RepairingTexts = { "REPAIRING ENGINE", "REPAIRING LEFT WING", "REPAIRING RIGHT WING", "REPAIRING TAIL" };
    private static readonly string[] MoreTexts = { "", "1 more", "2 more", "3 more" };

    private readonly PlaneHandler _planeHandler;
    private readonly GunHandler _gunHandler;
    private readonly DestroyedHandler _destroyedHandler;
    private readonly DamageHandler _damageHandler;
    private readonly PlaneDownHandler _planeDownHandler;
    private readonly RespawnHandler _respawnHandler;
    private readonly RepairHandler _repairHandler;
    private RepairState _repair;
    private readonly int[] _feedVictim = new int[KillFeedCapacity];
    private readonly int[] _feedShooter = new int[KillFeedCapacity];
    private readonly double[] _feedLeft = new double[KillFeedCapacity];
    private PlaneDamageState _damage = new(PlaneId, 1f, 1f, 1f, 1f, true);
    private PlaneState _plane;
    private readonly PlaneState[] _latest = new PlaneState[Planes];
    private readonly PlaneState[] _previous = new PlaneState[Planes];
    private readonly bool[] _seen = new bool[Planes];
    private GunState _gun;
    private bool _sampled;

    public HudModule()
    {
        _planeHandler = new PlaneHandler(this);
        _gunHandler = new GunHandler(this);
        _destroyedHandler = new DestroyedHandler(this);
        _damageHandler = new DamageHandler(this);
        _planeDownHandler = new PlaneDownHandler(this);
        _respawnHandler = new RespawnHandler(this);
        _repairHandler = new RepairHandler(this);
    }

    /// <summary>A <see cref="PlaneState"/> for plane 0 has been received.</summary>
    public bool HasPlane { get; private set; }

    /// <summary>Airspeed on the last Tick, km/h.</summary>
    public float SpeedKmh { get; private set; }

    /// <summary>Airspeed on the Tick before, km/h (to interpolate).</summary>
    public float PreviousSpeedKmh { get; private set; }

    /// <summary>Altitude (world Y) on the last Tick, meters.</summary>
    public float AltitudeMeters { get; private set; }

    /// <summary>Altitude on the Tick before, meters (to interpolate).</summary>
    public float PreviousAltitudeMeters { get; private set; }

    /// <summary>Gun heat from the last <see cref="GunState"/>, 0 (cold) to 1.</summary>
    public float Heat => _gun.Heat;

    /// <summary>The guns are overheated (last <see cref="GunState"/>).</summary>
    public bool Overheated => _gun.Overheated;

    /// <summary>Balloons popped since start: one per <see cref="TargetDestroyed"/>.</summary>
    public int BalloonsPopped { get; private set; }

    /// <summary>Plane 0's engine health, 0..1, from the last <see cref="PlaneDamageState"/> (1 before any).</summary>
    public float Engine => _damage.Engine;

    /// <summary>Plane 0's left wing health, 0..1.</summary>
    public float LeftWing => _damage.LeftWing;

    /// <summary>Plane 0's right wing health, 0..1.</summary>
    public float RightWing => _damage.RightWing;

    /// <summary>Plane 0's tail health, 0..1.</summary>
    public float Tail => _damage.Tail;

    /// <summary>How the damage panel colors <paramref name="part"/> of plane 0.</summary>
    public PartLevel Level(PlanePart part) => LevelOf(_damage.Get(part));

    /// <summary>Green from 0.6, orange below 0.6, red below 0.3, crossed out at 0.</summary>
    public static PartLevel LevelOf(float health)
    {
        if (!(health > 0f))
            return PartLevel.Destroyed;
        if (health < CriticalBelow)
            return PartLevel.Critical;
        if (health < WornBelow)
            return PartLevel.Worn;
        return PartLevel.Good;
    }

    /// <summary>A repair cycle runs on plane 0 (last <see cref="RepairState"/>).</summary>
    public bool Repairing => _repair.Repairing;

    /// <summary>The part being repaired (meaningful only while <see cref="Repairing"/>); the plane icon highlights it.</summary>
    public PlanePart RepairPart => _repair.Part;

    /// <summary>The current cycle's progress, 0..1 (0 when not repairing): the progress bar.</summary>
    public float RepairProgress => _repair.Repairing ? System.Math.Clamp(_repair.Progress, 0f, 1f) : 0f;

    /// <summary>Damaged parts after the one being repaired (0 when not repairing).</summary>
    public int RepairPartsAfter => _repair.Repairing ? System.Math.Clamp(_repair.PartsLeft - 1, 0, PartCount - 1) : 0;

    /// <summary>"REPAIRING ENGINE" / "LEFT WING" / "RIGHT WING" / "TAIL" while repairing; empty otherwise.</summary>
    public string RepairText => _repair.Repairing ? RepairingText(_repair.Part) : string.Empty;

    /// <summary>"N more" for <see cref="RepairPartsAfter"/>; empty when none or not repairing.</summary>
    public string RepairMoreText => MoreTexts[RepairPartsAfter];

    /// <summary>
    /// "Repair cancelled" is up: Repair was let go (still alive, parts still damaged) before the
    /// running cycle finished; for <see cref="RepairCancelledSeconds"/> or until a repair runs again.
    /// </summary>
    public bool RepairCancelled => RepairCancelledSecondsLeft > 0.0;

    /// <summary>Seconds "Repair cancelled" stays up (0 when not shown).</summary>
    public double RepairCancelledSecondsLeft { get; private set; }

    /// <summary>Show "Hold Y / R: repair": plane 0 alive, a part below full health, no repair running.</summary>
    public bool ShowRepairHint => !_repair.Repairing && !PlayerDown && _damage.Alive && AnyDamaged();

    /// <summary>"REPAIRING " and the part's name, prebuilt.</summary>
    public static string RepairingText(PlanePart part) =>
        (int)part >= 0 && (int)part < PartCount ? RepairingTexts[(int)part] : "REPAIRING";

    /// <summary>Planes the player (plane 0) shot down: <see cref="PlaneDestroyed"/> of an enemy (ids 4-7) by plane 0.</summary>
    public int Kills { get; private set; }

    /// <summary>Times the player (plane 0) went down: every <see cref="PlaneDestroyed"/> of plane 0.</summary>
    public int Deaths { get; private set; }

    /// <summary>Plane 0 is down: from its <see cref="PlaneDestroyed"/> until its <see cref="PlaneRespawn"/>.</summary>
    public bool PlayerDown { get; private set; }

    /// <summary>Seconds left until plane 0 is back (counts down from 5 while <see cref="PlayerDown"/>, then 0).</summary>
    public double RespawnSecondsLeft { get; private set; }

    /// <summary>The whole seconds shown, rounded up: 5, 4, 3, 2, 1 (at least 1 while down; 0 when up).</summary>
    public int RespawnCountdown
    {
        get
        {
            if (!PlayerDown)
                return 0;
            int n = (int)System.Math.Ceiling(RespawnSecondsLeft - 1e-9);
            return n < 1 ? 1 : n > MaxCountdown ? MaxCountdown : n;
        }
    }

    /// <summary>"Back in the air in N" for <see cref="RespawnCountdown"/>; empty when the player is up.</summary>
    public string RespawnText => PlayerDown ? RespawnTexts[RespawnCountdown] : string.Empty;

    /// <summary>Kill feed lines shown now (at most <see cref="KillFeedCapacity"/>).</summary>
    public int KillFeedCount { get; private set; }

    /// <summary>Kill feed line <paramref name="index"/>, 0 the oldest, e.g. "You shot down Red 5".</summary>
    public string KillFeedText(int index) => KillLine(_feedVictim[index], _feedShooter[index]);

    /// <summary>The plane that went down in kill feed line <paramref name="index"/>.</summary>
    public int KillFeedVictim(int index) => _feedVictim[index];

    /// <summary>The plane that shot it down in kill feed line <paramref name="index"/>.</summary>
    public int KillFeedShooter(int index) => _feedShooter[index];

    /// <summary>Seconds kill feed line <paramref name="index"/> stays up (0..<see cref="KillFeedSeconds"/>).</summary>
    public double KillFeedSecondsLeft(int index) => _feedLeft[index];

    /// <summary>
    /// Plane <paramref name="planeId"/> gets a marker: another plane than the player's, seen in a
    /// <see cref="PlaneState"/>, and alive in the last one.
    /// </summary>
    public bool HasMarker(int planeId) =>
        planeId != PlaneId && planeId >= 0 && planeId < Planes && _seen[planeId] && _latest[planeId].Alive;

    /// <summary>
    /// Plane <paramref name="planeId"/>'s world position between its <see cref="PlaneState"/> before
    /// the last (<paramref name="fraction"/> 0) and its last (1), like the Plane view interpolates.
    /// Zero for a plane not seen yet.
    /// </summary>
    public Vector3 PlanePosition(int planeId, float fraction)
    {
        if (planeId < 0 || planeId >= Planes || !_seen[planeId])
            return Vector3.Zero;
        return Vector3.Lerp(_previous[planeId].Position, _latest[planeId].Position, fraction);
    }

    /// <summary>"You", "Blue 1".."Blue 3", "Red 4".."Red 7"; other ids "Plane N".</summary>
    public static string PlaneName(int planeId) =>
        planeId >= 0 && planeId < Names.Length ? Names[planeId] : "Plane " + planeId;

    /// <summary>
    /// The kill feed line for <paramref name="victim"/> downed by <paramref name="shooter"/>:
    /// "You shot down Red 5", "Red 4 shot you down", "Blue 2 shot down Red 6", "Red 5 went down"
    /// (downed itself). Prebuilt for ids 0-7, so it does not allocate for them.
    /// </summary>
    public static string KillLine(int victim, int shooter)
    {
        if (victim >= 0 && victim < Planes && shooter >= 0 && shooter < Planes)
            return KillLines[victim * Planes + shooter];
        return BuildKillLine(victim, shooter);
    }

    public void Start(IMessageBus bus)
    {
        bus.Subscribe(_planeHandler);
        bus.Subscribe(_gunHandler);
        bus.Subscribe(_destroyedHandler);
        bus.Subscribe(_damageHandler);
        bus.Subscribe(_planeDownHandler);
        bus.Subscribe(_respawnHandler);
        bus.Subscribe(_repairHandler);
    }

    public void Tick(in TickContext tick)
    {
        float speed = _plane.Speed * KmhPerMetersPerSecond;
        float altitude = _plane.Position.Y;
        // The first sample of the plane has nothing before it: don't sweep up from zero.
        bool first = HasPlane && !_sampled;
        PreviousSpeedKmh = first ? speed : SpeedKmh;
        PreviousAltitudeMeters = first ? altitude : AltitudeMeters;
        SpeedKmh = speed;
        AltitudeMeters = altitude;
        _sampled = HasPlane;

        double step = tick.StepSeconds;
        if (RepairCancelledSecondsLeft > 0.0)
        {
            double left = RepairCancelledSecondsLeft - step;
            RepairCancelledSecondsLeft = left <= 1e-9 ? 0.0 : left;
        }
        if (PlayerDown)
            RespawnSecondsLeft = System.Math.Max(0.0, RespawnSecondsLeft - step);

        // Age the kill feed; drop lines whose 5 s are up, keeping the rest oldest first.
        int kept = 0;
        for (int i = 0; i < KillFeedCount; i++)
        {
            double left = _feedLeft[i] - step;
            if (left <= 1e-9)
                continue;
            _feedVictim[kept] = _feedVictim[i];
            _feedShooter[kept] = _feedShooter[i];
            _feedLeft[kept] = left;
            kept++;
        }
        KillFeedCount = kept;
    }

    // Each PlaneState (one per plane per tick): the last becomes the one before, except on the first
    // and on a respawn, where there is nothing to sweep from.
    private void Track(in PlaneState state)
    {
        int id = state.PlaneId;
        if (id < 0 || id >= Planes)
            return;
        bool respawned = _seen[id] && state.Alive && !_latest[id].Alive;
        _previous[id] = _seen[id] && !respawned ? _latest[id] : state;
        _latest[id] = state;
        _seen[id] = true;
    }

    private bool AnyDamaged() =>
        _damage.Engine < 1f || _damage.LeftWing < 1f || _damage.RightWing < 1f || _damage.Tail < 1f;

    // Plane 0's RepairState (each tick, right after its PlaneDamageState). A repair that stops while
    // the plane is alive and still damaged was let go mid-cycle: "Repair cancelled". One that stops
    // because nothing is left, or because the plane went down, is not.
    private void OnRepair(in RepairState message)
    {
        bool stopped = _repair.Repairing && !message.Repairing;
        _repair = message;
        if (message.Repairing)
            RepairCancelledSecondsLeft = 0.0;
        else if (stopped && message.PartsLeft > 0 && _damage.Alive && !PlayerDown)
            RepairCancelledSecondsLeft = RepairCancelledSeconds;
    }

    private void OnPlaneDestroyed(in PlaneDestroyed message)
    {
        int victim = message.PlaneId;
        int shooter = message.ShooterPlaneId;
        if (shooter == PlaneId && victim >= FirstEnemy && victim < Planes)
            Kills++;
        if (victim == PlaneId)
        {
            Deaths++;
            PlayerDown = true;
            RepairCancelledSecondsLeft = 0.0;
            RespawnSecondsLeft = DamageLimits.RespawnSeconds;
        }

        if (KillFeedCount == KillFeedCapacity)
        {
            for (int i = 1; i < KillFeedCapacity; i++)
            {
                _feedVictim[i - 1] = _feedVictim[i];
                _feedShooter[i - 1] = _feedShooter[i];
                _feedLeft[i - 1] = _feedLeft[i];
            }
            KillFeedCount--;
        }
        _feedVictim[KillFeedCount] = victim;
        _feedShooter[KillFeedCount] = shooter;
        _feedLeft[KillFeedCount] = KillFeedSeconds;
        KillFeedCount++;
    }

    private static string[] BuildNames()
    {
        var names = new string[Planes];
        for (int id = 0; id < Planes; id++)
            names[id] = id == PlaneId ? "You" : (id < FirstEnemy ? "Blue " : "Red ") + id;
        return names;
    }

    private static string[] BuildKillLines()
    {
        var lines = new string[Planes * Planes];
        for (int victim = 0; victim < Planes; victim++)
            for (int shooter = 0; shooter < Planes; shooter++)
                lines[victim * Planes + shooter] = BuildKillLine(victim, shooter);
        return lines;
    }

    private static string BuildKillLine(int victim, int shooter)
    {
        if (victim == shooter)
            return PlaneName(victim) + " went down";
        if (victim == PlaneId)
            return PlaneName(shooter) + " shot you down";
        return PlaneName(shooter) + " shot down " + PlaneName(victim);
    }

    private static string[] BuildRespawnTexts()
    {
        var texts = new string[MaxCountdown + 1];
        for (int n = 0; n <= MaxCountdown; n++)
            texts[n] = "Back in the air in " + n;
        return texts;
    }

    private sealed class PlaneHandler : IMessageHandler<PlaneState>
    {
        private readonly HudModule _module;

        public PlaneHandler(HudModule module) => _module = module;

        public void Handle(in PlaneState message)
        {
            _module.Track(in message);
            if (message.PlaneId != PlaneId)
                return;
            _module._plane = message;
            _module.HasPlane = true;
        }
    }

    private sealed class GunHandler : IMessageHandler<GunState>
    {
        private readonly HudModule _module;

        public GunHandler(HudModule module) => _module = module;

        public void Handle(in GunState message)
        {
            if (message.PlaneId == PlaneId)
                _module._gun = message;
        }
    }

    private sealed class DestroyedHandler : IMessageHandler<TargetDestroyed>
    {
        private readonly HudModule _module;

        public DestroyedHandler(HudModule module) => _module = module;

        public void Handle(in TargetDestroyed message) => _module.BalloonsPopped++;
    }

    private sealed class DamageHandler : IMessageHandler<PlaneDamageState>
    {
        private readonly HudModule _module;

        public DamageHandler(HudModule module) => _module = module;

        public void Handle(in PlaneDamageState message)
        {
            if (message.PlaneId == PlaneId)
                _module._damage = message;
        }
    }

    private sealed class PlaneDownHandler : IMessageHandler<PlaneDestroyed>
    {
        private readonly HudModule _module;

        public PlaneDownHandler(HudModule module) => _module = module;

        public void Handle(in PlaneDestroyed message) => _module.OnPlaneDestroyed(in message);
    }

    private sealed class RepairHandler : IMessageHandler<RepairState>
    {
        private readonly HudModule _module;

        public RepairHandler(HudModule module) => _module = module;

        public void Handle(in RepairState message)
        {
            if (message.PlaneId == PlaneId)
                _module.OnRepair(in message);
        }
    }

    private sealed class RespawnHandler : IMessageHandler<PlaneRespawn>
    {
        private readonly HudModule _module;

        public RespawnHandler(HudModule module) => _module = module;

        public void Handle(in PlaneRespawn message)
        {
            if (message.PlaneId != PlaneId)
                return;
            _module.PlayerDown = false;
            _module.RespawnSecondsLeft = 0.0;
        }
    }
}
