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
/// Tick's values kept so the view can interpolate. Publishes nothing; allocation-free.
/// </summary>
public sealed class HudModule : IModule
{
    public const int PlaneId = 0;

    /// <summary>m/s to km/h.</summary>
    public const float KmhPerMetersPerSecond = 3.6f;

    private readonly PlaneHandler _planeHandler;
    private readonly GunHandler _gunHandler;
    private readonly DestroyedHandler _destroyedHandler;
    private PlaneState _plane;
    private GunState _gun;
    private bool _sampled;

    public HudModule()
    {
        _planeHandler = new PlaneHandler(this);
        _gunHandler = new GunHandler(this);
        _destroyedHandler = new DestroyedHandler(this);
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

    public void Start(IMessageBus bus)
    {
        bus.Subscribe(_planeHandler);
        bus.Subscribe(_gunHandler);
        bus.Subscribe(_destroyedHandler);
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
    }

    private sealed class PlaneHandler : IMessageHandler<PlaneState>
    {
        private readonly HudModule _module;

        public PlaneHandler(HudModule module) => _module = module;

        public void Handle(in PlaneState message)
        {
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
}
