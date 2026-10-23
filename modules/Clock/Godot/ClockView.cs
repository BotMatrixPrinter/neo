using Godot;
using Neo.Shell;

namespace Neo.Clock;

/// <summary>
/// Godot side of the Clock module: draws its Core's step count. No rules here.
/// </summary>
public partial class ClockView : Node2D, IModuleView
{
    private ClockModule? _clock;
    private Label _label = null!;
    private long _shownTick = -1;

    public void Bind(IModule module) => _clock = (ClockModule)module;

    public override void _Ready()
    {
        _label = GetNode<Label>("Label");
    }

    public override void _Process(double delta)
    {
        if (_clock == null || _clock.Steps == _shownTick)
            return;
        _shownTick = _clock.Steps;
        _label.Text = "tick " + _shownTick.ToString();
    }
}
