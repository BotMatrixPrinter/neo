# Input

Reads the pilot's mouse, keyboard or gamepad and publishes what they want the plane to do.

## Controls

**Mouse and keyboard** (the default; any key press or mouse click switches back to it)

| Control | Does |
|---|---|
| W / S | Nose down / nose up (like pushing / pulling a stick; `InvertPitch` swaps them) |
| A / D | Roll left / right |
| Q / E | Yaw left / right |
| Shift / Ctrl | Throttle up / down (about 0.5 per second, held) |
| Left mouse button | Fire |
| Space | Also fires |

The keys fly the plane directly (`HasAim` is always false; the Flight module flies by
Pitch/Roll/Yaw). Each axis is smoothed a little: about 0.15 s from center to full
deflection, 0.05 s back to center, so a tap makes a small correction. The mouse only fires:
it is not captured, no reticle is drawn, and there is no Esc to free it. Keys are read by
physical position, so they sit in the same place on AZERTY and other layouts.

**Invert pitch**: `InputModule.DefaultInvertPitch` (false) is the default; set the exported
`InvertPitch` on the `InputView` node in `Godot/Main.tscn` (Inspector) to true for
W = nose up, S = nose down.

**Gamepad** (any Xbox or PlayStation pad, via Godot's joypad API; touching a stick,
trigger or button switches to it, and the first of up to 4 pads with input is read)

| Control | Does |
|---|---|
| Left stick | Pitch (pull back = nose up) and roll |
| Right trigger / left trigger | Throttle up / down (analog: half pressed = half rate) |
| A (Xbox) / Cross (PlayStation) | Fire |
| Right trigger past half way | Also fires |

With a gamepad there is no aim either (`HasAim` false): the stick flies the plane.
Note: the right trigger both raises throttle and fires, as specified; it may be worth
moving one of them (e.g. fire to a bumper) after play-testing.

## Parts

- **Contract** (`Contract/`, namespace `Neo.Input`): `ControlInput`, published on the bus
  once per fixed step: `PlaneId` (0 for now: one plane), `Pitch`, `Roll`, `Yaw` (-1..1;
  positive = nose up, roll right, yaw right), `Throttle` (0..1), `Fire`, `HasAim` and
  `AimDirection` (world-space unit vector to fly toward; `Vector3.Zero` when `HasAim` is
  false). Its constructor clamps the axes, turns non-finite values into 0, normalizes the
  aim direction, and drops the aim (`HasAim` false) if the direction is zero or not finite.
- **Core** (`Core/`): `InputModule` (`IModule`). Its view calls `SetKeys(KeyState)` (mouse
  and keyboard) or `SetState(DeviceState)` (gamepad) each frame; the last one called picks
  the device. On each `Tick` it turns keys into controls with `KeyboardFlight` (key mapping
  and smoothing, `RampSeconds` 0.15, `ReturnSeconds` 0.05; reset when the pad takes over),
  moves `Throttle` (starts at 0.7) by `(ThrottleUp - ThrottleDown) * 0.5 * step`, clamped to
  0..1, and publishes one `ControlInput` for plane 0, always without aim. `InvertPitch`
  (default `DefaultInvertPitch`, false) swaps W and S. No allocations per tick.
- **Godot** (`Godot/Main.tscn`, `InputView.cs`): a full-screen `Control` (draws nothing,
  ignores the mouse) that reads keys, the left mouse button and the pads every frame and
  hands them to the Core; its exported `InvertPitch` sets the Core's.
- **Tests** (`Tests/`): `ControlInputTests` (ranges, clamping, aim normalization, no-aim),
  `KeyboardFlightTests` (each key's mapping, invert, throttle and fire keys, smoothing: ramp,
  faster return, taps, reversing), `InputModuleTests` (one message per tick for plane 0,
  throttle start/smoothing/clamping, keys fly the plane, `HasAim` false on keyboard and pad,
  invert setting, latest state wins, no allocations per tick), with a fake bus.

Test it alone: `./test.sh Input`. The scene draws nothing, so it has no screenshot.
