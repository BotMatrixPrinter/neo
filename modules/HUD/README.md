# HUD

The on-screen display: speed, altitude, gun heat and balloons popped, in the game's cartoon
style (dark rounded panels with a gold edge, bold outlined text).

## Messages

Publishes nothing; its Contract (`Neo.HUD.Contract`) is empty for now.

Uses other modules' Contracts: reads `Neo.Flight.PlaneState` (plane 0), `Neo.Guns.GunState`
(plane 0) and `Neo.Targets.TargetDestroyed` (any target).

## Core (`Core/`)

`HudModule` (the `IModule`). For the view:

- `SpeedKmh`: `PlaneState.Speed` (m/s) x 3.6, sampled each Tick; `AltitudeMeters`: world Y of
  `PlaneState.Position`, sampled each Tick. `PreviousSpeedKmh` / `PreviousAltitudeMeters` are
  the Tick before's (equal to the current ones on the first sample), to interpolate.
  `HasPlane` once a plane-0 `PlaneState` has arrived.
- `Heat` (0..1) and `Overheated`: from the last plane-0 `GunState`.
- `BalloonsPopped`: one per `TargetDestroyed` received since start.

Messages for other planes are ignored. No allocations per Tick or per message.

## Godot (`Godot/Main.tscn`, `HudView.cs`)

A `CanvasLayer` (layer 10), laid out for 1920x1080 and scaled uniformly to fit the screen,
panels anchored to the corners:

- bottom left: SPEED (km/h) and ALT (m), interpolated between fixed ticks, and the GUNS heat
  bar: orange, warming towards red as it fills; while overheated it flashes red with
  "OVERHEATED" on it;
- top left: a little balloon and "Balloons popped: N"; when N rises the number bounces and
  wiggles and a "+1" floats up.

Numbers come from a prebuilt string table, so `_Process` does not format or allocate. When the
shell disables the module, the layer hides itself. Opened on its own (no module bound, as
`screenshot.sh` does) it runs a preview with its own Core over a sky backdrop: a plane
climbing, the guns heating up and overheating every 6 s, a balloon popping every 0.5 s.

Screenshots: `screenshot.png` (`./screenshot.sh modules/HUD/Godot/Main.tscn modules/HUD/screenshot.png`)
and `screenshot-overheated.png` (same with `FRAMES=200`).

## Tests (`Tests/`)

`HudModuleTests` (with a fake bus): km/h conversion, altitude, other planes ignored, previous
values for interpolation, heat and overheat, counting pops, publishes nothing, no allocations.

Test it alone: `./test.sh HUD`.
