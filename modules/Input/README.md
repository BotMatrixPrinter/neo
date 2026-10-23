# Input

Reads the pilot's mouse, keyboard or gamepad and publishes what they want the plane to do.

## Controls

**Mouse and keyboard** (the default; any mouse move, click or key switches back to it)

| Control | Does |
|---|---|
| Mouse | Aim: the plane flies toward the reticle (the ray from the 3D camera through the cursor) |
| Left mouse button | Fire |
| W / S | Throttle up / down (about 0.5 per second, held) |
| A / D | Yaw left / right |
| Esc | Free the mouse cursor; click in the window to take it back (that click does not fire) |

While playing the cursor is hidden and kept inside the window; a small reticle is drawn
where it is. Keys are read by physical position, so W/A/S/D sit in the same place on
AZERTY and other layouts.

**Gamepad** (any Xbox or PlayStation pad, via Godot's joypad API; touching a stick,
trigger or button switches to it, and the first of up to 4 pads with input is read)

| Control | Does |
|---|---|
| Left stick | Pitch (pull back = nose up) and roll |
| Right trigger / left trigger | Throttle up / down (analog: half pressed = half rate) |
| A (Xbox) / Cross (PlayStation) | Fire |
| Right trigger past half way | Also fires |

With a gamepad there is no aim (`HasAim` false) and no reticle: the stick flies the plane.
Note: the right trigger both raises throttle and fires, as specified; it may be worth
moving one of them (e.g. fire to a bumper) after play-testing.

## Parts

- **Contract** (`Contract/`, namespace `Neo.Input`): `ControlInput`, published on the bus
  once per fixed step: `PlaneId` (0 for now: one plane), `Pitch`, `Roll`, `Yaw` (-1..1;
  positive = nose up, roll right, yaw right), `Throttle` (0..1), `Fire`, `HasAim` and
  `AimDirection` (world-space unit vector to fly toward; `Vector3.Zero` when `HasAim` is
  false). Its constructor clamps the axes, turns non-finite values into 0, normalizes the
  aim direction, and drops the aim (`HasAim` false) if the direction is zero or not finite.
- **Core** (`Core/`): `InputModule` (`IModule`). Its view calls `SetState(DeviceState)`
  each frame; on each `Tick` it moves `Throttle` (starts at 0.7) by
  `(ThrottleUp - ThrottleDown) * 0.5 * step`, clamped to 0..1, and publishes one
  `ControlInput` for plane 0 from the latest state. No allocations per tick.
- **Godot** (`Godot/Main.tscn`, `InputView.cs`): a full-screen `Control` that reads the
  devices every frame, builds the `DeviceState`, captures/frees the mouse, and draws the
  reticle. The aim ray uses `GetViewport().GetCamera3D()`; with no 3D camera there is no aim.
- **Tests** (`Tests/`): `ControlInputTests` (ranges, clamping, aim normalization, no-aim),
  `InputModuleTests` (one message per tick for plane 0, throttle start/smoothing/clamping,
  latest state wins, `HasAim` handling, no allocations per tick), with a fake bus.

Test it alone: `./test.sh Input`. Screenshot of the scene (reticle): `screenshot.png`.
