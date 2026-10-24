using System.Numerics;
using Neo.Collisions;
using Neo.Damage;
using Neo.Input;
using Neo.Shell;

namespace Neo.Flight;

/// <summary>
/// The Flight module: flies planes 0..<see cref="Teams.PlaneCount"/>-1, each with the latest
/// <see cref="ControlInput"/> for it, applies the Damage module's part health, downings and
/// respawns and in-flight repairs (steering ignored while repairing), the Collisions module's
/// bounces (<see cref="CollisionImpulse"/>), and publishes every plane's <see cref="PlaneState"/> after every fixed step, in id
/// order. Allocation-free per tick.
/// </summary>
public sealed class FlightModule : IModule, IMessageHandler<ControlInput>, IMessageHandler<PlaneDamageState>,
    IMessageHandler<PlaneDestroyed>, IMessageHandler<PlaneRespawn>, IMessageHandler<RepairState>,
    IMessageHandler<CollisionImpulse>
{
    private readonly FlightModel[] _models = new FlightModel[Teams.PlaneCount];
    private readonly ControlInput[] _inputs = new ControlInput[Teams.PlaneCount];
    private IMessageBus? _bus;

    public FlightModule()
    {
        for (int id = 0; id < Teams.PlaneCount; id++)
        {
            _models[id] = new FlightModel(id);
            // Until the first input arrives: hands off, cruise throttle.
            _inputs[id] = new ControlInput(id, 0f, 0f, 0f, FlightModel.CruiseThrottle, false, false, Vector3.Zero);
        }
    }

    /// <summary>The player's plane (id 0).</summary>
    public FlightModel Model => _models[0];

    /// <summary>The model of plane <paramref name="planeId"/> (0..<see cref="Teams.PlaneCount"/>-1).</summary>
    public FlightModel Plane(int planeId) => _models[planeId];

    public void Start(IMessageBus bus)
    {
        _bus = bus;
        bus.Subscribe<ControlInput>(this);
        bus.Subscribe<PlaneDamageState>(this);
        bus.Subscribe<PlaneDestroyed>(this);
        bus.Subscribe<PlaneRespawn>(this);
        bus.Subscribe<RepairState>(this);
        bus.Subscribe<CollisionImpulse>(this);
    }

    public void Handle(in ControlInput message)
    {
        if (IsPlane(message.PlaneId))
            _inputs[message.PlaneId] = message;
    }

    public void Handle(in PlaneDamageState message)
    {
        if (IsPlane(message.PlaneId))
            _models[message.PlaneId].SetDamage(message.Engine, message.LeftWing, message.RightWing, message.Tail);
    }

    public void Handle(in PlaneDestroyed message)
    {
        if (IsPlane(message.PlaneId))
            _models[message.PlaneId].Destroy();
    }

    public void Handle(in PlaneRespawn message)
    {
        if (IsPlane(message.PlaneId))
            _models[message.PlaneId].Respawn();
    }

    public void Handle(in RepairState message)
    {
        if (IsPlane(message.PlaneId))
            _models[message.PlaneId].SetRepairing(message.Repairing);
    }

    public void Handle(in CollisionImpulse message)
    {
        if (IsPlane(message.PlaneId))
            _models[message.PlaneId].ApplyImpulse(message.VelocityChange, message.AngularKick, message.Separation);
    }

    public void Tick(in TickContext tick)
    {
        float dt = (float)tick.StepSeconds;
        for (int id = 0; id < _models.Length; id++)
            _models[id].Step(in _inputs[id], dt);
        if (_bus == null)
            return;
        for (int id = 0; id < _models.Length; id++)
            _bus.Publish(_models[id].State);
    }

    private static bool IsPlane(int planeId) => (uint)planeId < Teams.PlaneCount;
}
