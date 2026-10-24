using Neo.Flight;
using Neo.Input;
using Neo.Shell;

namespace Neo.Bots;

/// <summary>
/// The Bots module: flies every plane but the player's (ids 1..7). Keeps the latest
/// <see cref="PlaneState"/> of each plane and, each fixed step, publishes one
/// <see cref="ControlInput"/> (sticks, no aim) for each bot whose plane is known and alive,
/// in id order. Deterministic; allocation-free per tick.
/// </summary>
public sealed class BotsModule : IModule, IMessageHandler<PlaneState>
{
    private readonly PlaneState[] _states = new PlaneState[Pilots.PlaneCount];
    private readonly bool[] _known = new bool[Pilots.PlaneCount];
    private readonly BotPilot?[] _pilots = new BotPilot?[Pilots.PlaneCount];
    private IMessageBus? _bus;
    private double _time;

    public BotsModule()
        : this(BotPilot.EvasionSkill)
    {
    }

    /// <param name="evasionSkill">The bots' evasion skill (see <see cref="BotPilot.EvasionSkill"/>).</param>
    public BotsModule(float evasionSkill)
    {
        for (int id = 0; id < Pilots.PlaneCount; id++)
        {
            if (Pilots.IsBot(id))
                _pilots[id] = new BotPilot(id, evasionSkill);
        }
    }

    /// <summary>The pilot of bot plane <paramref name="planeId"/> (1..7).</summary>
    public BotPilot Pilot(int planeId) => _pilots[planeId]!;

    public void Start(IMessageBus bus)
    {
        _bus = bus;
        bus.Subscribe<PlaneState>(this);
    }

    public void Handle(in PlaneState message)
    {
        if (message.PlaneId < 0 || message.PlaneId >= Pilots.PlaneCount)
            return;
        _states[message.PlaneId] = message;
        _known[message.PlaneId] = true;
    }

    public void Tick(in TickContext tick)
    {
        float dt = (float)tick.StepSeconds;
        _time += tick.StepSeconds;
        for (int id = 0; id < Pilots.PlaneCount; id++)
        {
            BotPilot? pilot = _pilots[id];
            if (pilot == null)
                continue;
            if (!_known[id] || !_states[id].Alive)
            {
                pilot.Reset();
                continue;
            }
            ControlInput input = pilot.Fly(_states, _known, (float)_time, dt);
            _bus?.Publish(input);
        }
    }
}
