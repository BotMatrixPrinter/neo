using Godot;
using Neo.Shell;

namespace Neo.Input;

/// <summary>
/// Godot side of the Input module: reads mouse, keyboard and gamepad each frame, hands
/// the result to its Core as a <see cref="DeviceState"/>, and draws the aim reticle at
/// the cursor. No rules here.
/// </summary>
public partial class InputView : Control, IModuleView
{
    private const float Deadzone = 0.15f;
    private const int MaxPads = 4;
    private const float ReticleRadius = 10f;

    private InputModule? _input;
    private bool _usingPad;
    private bool _playing;
    private bool _headless;

    public void Bind(IModule module) => _input = (InputModule)module;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        _headless = DisplayServer.GetName() == "headless";
        SetPlaying(true);
    }

    public override void _ExitTree()
    {
        if (!_headless)
            Godot.Input.MouseMode = Godot.Input.MouseModeEnum.Visible;
    }

    public override void _Input(InputEvent e)
    {
        switch (e)
        {
            case InputEventKey key when key.Pressed && key.PhysicalKeycode == Key.Escape:
                SetPlaying(false);
                _usingPad = false;
                break;
            case InputEventMouseButton button when button.Pressed:
                if (!_playing)
                {
                    // The click that takes the mouse back does not fire.
                    SetPlaying(true);
                    GetViewport().SetInputAsHandled();
                }
                _usingPad = false;
                break;
            case InputEventMouseMotion:
            case InputEventKey:
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
        _input.SetState(_usingPad ? ReadPad() : ReadMouseAndKeyboard());
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_usingPad || !_playing)
            return;
        var at = GetLocalMousePosition();
        var color = new Color(1f, 0.95f, 0.6f, 0.9f);
        DrawArc(at, ReticleRadius, 0, Mathf.Tau, 24, color, 2f, true);
        DrawLine(at + new Vector2(-ReticleRadius - 6, 0), at + new Vector2(-ReticleRadius + 3, 0), color, 2f);
        DrawLine(at + new Vector2(ReticleRadius - 3, 0), at + new Vector2(ReticleRadius + 6, 0), color, 2f);
        DrawLine(at + new Vector2(0, -ReticleRadius - 6), at + new Vector2(0, -ReticleRadius + 3), color, 2f);
        DrawLine(at + new Vector2(0, ReticleRadius - 3), at + new Vector2(0, ReticleRadius + 6), color, 2f);
        DrawCircle(at, 1.5f, color);
    }

    private void SetPlaying(bool playing)
    {
        _playing = playing;
        if (_headless)
            return;
        // Confined + hidden: the cursor moves freely inside the window and we draw the reticle.
        Godot.Input.MouseMode = playing ? Godot.Input.MouseModeEnum.ConfinedHidden : Godot.Input.MouseModeEnum.Visible;
    }

    private DeviceState ReadMouseAndKeyboard()
    {
        float up = Godot.Input.IsPhysicalKeyPressed(Key.W) ? 1f : 0f;
        float down = Godot.Input.IsPhysicalKeyPressed(Key.S) ? 1f : 0f;
        float yaw = (Godot.Input.IsPhysicalKeyPressed(Key.D) ? 1f : 0f) - (Godot.Input.IsPhysicalKeyPressed(Key.A) ? 1f : 0f);
        bool fire = _playing && Godot.Input.IsMouseButtonPressed(MouseButton.Left);

        bool hasAim = false;
        var aim = System.Numerics.Vector3.Zero;
        var viewport = GetViewport();
        var camera = viewport.GetCamera3D();
        if (_playing && camera != null)
        {
            Vector3 ray = camera.ProjectRayNormal(viewport.GetMousePosition());
            aim = new System.Numerics.Vector3(ray.X, ray.Y, ray.Z);
            hasAim = true;
        }
        return new DeviceState(0f, 0f, yaw, up, down, fire, hasAim, aim);
    }

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
            return new DeviceState(y, x, 0f, rightTrigger, leftTrigger, a || rightTrigger > 0.5f, false, System.Numerics.Vector3.Zero);
        }
        return default;
    }

    private static float Axis(int pad, JoyAxis axis)
    {
        float value = Godot.Input.GetJoyAxis(pad, axis);
        return Mathf.Abs(value) > Deadzone ? value : 0f;
    }
}
