using System.Numerics;
using Neo.Input;
using Neo.Shell;

namespace Neo.Flight;

/// <summary>
/// The Flight module: flies plane 0 with the latest <see cref="ControlInput"/> for it and
/// publishes its <see cref="PlaneState"/> after every fixed step. Allocation-free per tick.
/// </summary>
public sealed class FlightModule : IModule, IMessageHandler<ControlInput>
{
    private readonly FlightModel _model = new(0);
    private IMessageBus? _bus;
    // Until the first input arrives: hands off, cruise throttle.
    private ControlInput _input = new(0, 0f, 0f, 0f, FlightModel.CruiseThrottle, false, false, Vector3.Zero);

    public FlightModel Model => _model;

    public void Start(IMessageBus bus)
    {
        _bus = bus;
        bus.Subscribe<ControlInput>(this);
    }

    public void Handle(in ControlInput message)
    {
        if (message.PlaneId == _model.PlaneId)
            _input = message;
    }

    public void Tick(in TickContext tick)
    {
        _model.Step(in _input, (float)tick.StepSeconds);
        _bus?.Publish(_model.State);
    }
}
