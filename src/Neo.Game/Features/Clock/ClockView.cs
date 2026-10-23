using Godot;
using Neo.Core.Clock;

namespace Neo.Game.Features.Clock;

/// <summary>
/// Godot side of the Clock feature: feeds frame time to Core and draws the tick.
/// No rules here.
/// </summary>
public partial class ClockView : Node2D
{
    private const double StepSeconds = 1.0 / 60.0;

    private readonly IFixedClock _clock = new FixedClock(StepSeconds);
    private Label _label = null!;
    private long _shownTick = -1;

    public override void _Ready()
    {
        _label = GetNode<Label>("Label");
        // test.sh looks for this line to know the main scene and its C# loaded.
        GD.Print("NEO_READY");
    }

    public override void _Process(double delta)
    {
        _clock.Advance(delta);
        if (_clock.Tick != _shownTick)
        {
            _shownTick = _clock.Tick;
            _label.Text = "tick " + _shownTick.ToString();
        }
    }
}
