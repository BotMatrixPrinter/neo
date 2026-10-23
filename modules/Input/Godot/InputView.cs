using Godot;
using Neo.Shell;

namespace Neo.Input;

/// <summary>
/// Godot side of the Input module: reads the keyboard, the left mouse button and the
/// gamepad each frame and hands them to its Core (<see cref="InputModule.SetKeys"/> or
/// <see cref="InputModule.SetState"/>). The mouse only fires: it is not captured and
/// nothing is drawn. No rules here.
/// </summary>
public partial class InputView : Control, IModuleView
{
    private const float Deadzone = 0.15f;
    private const int MaxPads = 4;

    /// <summary>W nose up, S nose down instead of the default (see <see cref="InputModule.DefaultInvertPitch"/>).</summary>
    [Export]
    public bool InvertPitch { get; set; } = InputModule.DefaultInvertPitch;

    private InputModule? _input;
    private bool _usingPad;

    public void Bind(IModule module) => _input = (InputModule)module;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public override void _Input(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { Pressed: true }:
            case InputEventKey { Pressed: true }:
                _usingPad = false;
                break;
            case InputEventJoypadMotion motion when Mathf.Abs(motion.AxisValue) > Deadzone:
            case InputEventJoypadButton { Pressed: true }:
                _usingPad = true;
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (_input == null)
            return;
        _input.InvertPitch = InvertPitch;
        if (_usingPad)
            _input.SetState(ReadPad());
        else
            _input.SetKeys(ReadKeys());
    }

    private static KeyState ReadKeys() => new()
    {
        // Physical keys: the same places on AZERTY and other layouts.
        W = Held(Key.W),
        S = Held(Key.S),
        A = Held(Key.A),
        D = Held(Key.D),
        Q = Held(Key.Q),
        E = Held(Key.E),
        Shift = Held(Key.Shift),
        Ctrl = Held(Key.Ctrl),
        Space = Held(Key.Space),
        LeftMouse = Godot.Input.IsMouseButtonPressed(MouseButton.Left),
    };

    private static bool Held(Key key) => Godot.Input.IsPhysicalKeyPressed(key);

    private static DeviceState ReadPad()
    {
        for (int pad = 0; pad < MaxPads; pad++)
        {
            float x = Axis(pad, JoyAxis.LeftX);
            float y = Axis(pad, JoyAxis.LeftY);
            float leftTrigger = Axis(pad, JoyAxis.TriggerLeft);
            float rightTrigger = Axis(pad, JoyAxis.TriggerRight);
            bool a = Godot.Input.IsJoyButtonPressed(pad, JoyButton.A); // A on Xbox, Cross on PlayStation
            if (x == 0 && y == 0 && leftTrigger == 0 && rightTrigger == 0 && !a)
                continue;
            // Stick back (down, +Y in Godot) = nose up, like a real control column.
            return new DeviceState(y, x, 0f, rightTrigger, leftTrigger, a || rightTrigger > 0.5f);
        }
        return default;
    }

    private static float Axis(int pad, JoyAxis axis)
    {
        float value = Godot.Input.GetJoyAxis(pad, axis);
        return Mathf.Abs(value) > Deadzone ? value : 0f;
    }
}
