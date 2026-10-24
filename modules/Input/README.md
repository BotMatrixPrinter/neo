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
| R | Repair (held; `ControlInput.Repair` true on every tick it is down) |

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
| Left stick up / down | Pitch (pull back = nose up) |
| Left stick left / right | Yaw (rudder) |
| Right stick left / right | Roll |
| Right stick up / down | Nothing for now |
| X (Xbox) / Square (PlayStation) | Throttle up (held, same rate as Shift) |
| B (Xbox) / Circle (PlayStation) | Throttle down (held, same rate as Ctrl) |
| Right trigger past half way | Fire |
| A (Xbox) / Cross (PlayStation) | Also fires |
| Y (Xbox) / Triangle (PlayStation) | Repair (held; `ControlInput.Repair` true on every tick it is down) |
| Left trigger | Nothing for now |

The sticks are proportional: how far a stick is pushed is how hard the plane turns (5% right
is a small adjustment, 100% a hard right). `GamepadFlight.Stick` gives each stick axis a small
dead zone (`GamepadFlight.Deadzone`, 0.04, just enough to ignore stick drift), then rescales
so the output starts at 0 right at the dead zone edge and grows to exactly 1 at full
deflection: `sign(v) * ((|v| - 0.04) / 0.96) ^ ResponseExponent`, clamped to -1..1 (5% gives
about 0.01, 50% about 0.48, 100% gives 1; no jump anywhere). `GamepadFlight.ResponseExponent`
(1.0 = linear) is there for later tuning: above 1 gives finer control near center.
Non-finite readings give 0. The same 0.04 dead zone (unscaled, `GamepadFlight.DeadZone`)
decides whether a stick or trigger is being touched; the right trigger fires past 0.5. The triggers do not
change the throttle. With a gamepad there is no aim either (`HasAim` false): the sticks fly
the plane.

## Parts

- **Contract** (`Contract/`, namespace `Neo.Input`): `ControlInput`, published on the bus
  once per fixed step: `PlaneId` (0 for now: one plane), `Pitch`, `Roll`, `Yaw` (-1..1;
  positive = nose up, roll right, yaw right), `Throttle` (0..1), `Fire`, `Repair` (the Repair button is held this step; false from the
  original 8-argument constructor, set by the added one with a trailing `repair`), `HasAim` and
  `AimDirection` (world-space unit vector to fly toward; `Vector3.Zero` when `HasAim` is
  false). Input only reports Repair: what it does (ignoring the pilot's maneuvers, repair
  cycles) is up to the modules that read it. Its constructor clamps the axes, turns non-finite values into 0, normalizes the
  aim direction, and drops the aim (`HasAim` false) if the direction is zero or not finite.
- **Core** (`Core/`): `InputModule` (`IModule`). Its view calls `SetKeys(KeyState)` (mouse
  and keyboard) or `SetState(DeviceState)` (gamepad, mapped from the raw `PadState` by `GamepadFlight`: sticks, dead zone and proportional response,
  buttons, fire, repair) each frame; the last one called picks
  the device. On each `Tick` it turns keys into controls with `KeyboardFlight` (key mapping
  and smoothing, `RampSeconds` 0.15, `ReturnSeconds` 0.05; reset when the pad takes over),
  moves `Throttle` (starts at 0.7) by `(ThrottleUp - ThrottleDown) * 0.5 * step`, clamped to
  0..1, and publishes one `ControlInput` for plane 0, always without aim. `InvertPitch`
  (default `DefaultInvertPitch`, false) swaps W and S. No allocations per tick.
- **Godot** (`Godot/Main.tscn`, `InputView.cs`): a full-screen `Control` (draws nothing,
  ignores the mouse) that reads keys, the left mouse button and the pads (raw `PadState`)
  every frame and hands them to the Core; its exported `InvertPitch` sets the Core's.
- **Tests** (`Tests/`): `ControlInputTests` (ranges, clamping, aim normalization, no-aim, `Repair` default and set),
  `KeyboardFlightTests` (each key's mapping, invert, throttle, fire and repair keys, smoothing: ramp,
  faster return, taps, reversing), `GamepadFlightTests` (each stick axis, buttons,
  triggers, fire, Y repair, the dead zone and proportional response on every stick axis: 0 inside,
continuous at the edge, linear, exactly ±1 at full deflection, 5%/50%/100% examples, non-finite = 0; what counts as touching the pad), `InputModuleTests` (one message per tick for plane 0,
  throttle start/smoothing/clamping, R and Y publish `Repair` every held tick with throttle still working, pad X/B throttle and sticks, keys fly the plane, `HasAim` false on keyboard and pad,
  invert setting, latest state wins, no allocations per tick), with a fake bus.

Test it alone: `./test.sh Input`. The scene draws nothing, so it has no screenshot.
