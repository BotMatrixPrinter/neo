# Flight

Flies the plane: an arcade model, not a simulation. One plane for now, `PlaneId` 0.

- **Contract** (`Contract/`): `PlaneState` (namespace `Neo.Flight`), published on the bus
  every fixed step, after the step:
  - `int PlaneId`
  - `Vector3 Position`: world, meters, Y up
  - `Quaternion Orientation`: body to world (System.Numerics); body axes +X right, +Y up,
    -Z forward (Godot's convention), so the nose is `Vector3.Transform(-Vector3.UnitZ, Orientation)`
  - `Vector3 Velocity`: world m/s, always along the nose
  - `float Speed`: m/s, the length of `Velocity`
  - `float Throttle`: 0..1, the throttle in use
- **Input**: subscribes to `Neo.Input.ControlInput` (modules/Input/Contract) and uses the
  latest one for its plane; others are ignored. Before any arrives: hands off, throttle 0.5.
- **Core** (`Core/`): `FlightModule` (the `IModule`) steps a `FlightModel` each tick and
  publishes its state. No allocations per tick; deterministic (same inputs, same result).

## The model (`FlightModel`)

- **Start**: in the air at (0, 400, 0), level, heading -Z, at cruise.
- **Speed**: throttle sets the level speed: 0 → 25 m/s (minimum), 0.5 → 42 m/s (cruise,
  about 150 km/h), 1 → 60 m/s (maximum), approached at 0.5/s. Diving gains speed and
  climbing loses it (6 m/s² per g of nose-down), always clamped to 25..60. The plane flies
  where its nose points: no stall, no spin, no slip.
- **Sticks** (no `HasAim`): `Pitch`, `Roll`, `Yaw` (-1..1) are body rates of 70, 180 and
  40 deg/s; commands are smoothed (15/s) so rates are snappy but never jump. A banked plane
  also turns about world up (30 deg/s at 90 degrees of bank).
- **Hands off** (no `HasAim`, and `Pitch`, `Roll`, `Yaw` all inside a 0.05 dead zone for
  0.25 s): the plane levels itself, like an easy arcade plane. It rolls back to wings level
  the shorter way (so from inverted it rolls back the way it came) and eases its nose back
  to the horizon, each at a rate proportional to what is left (time constant 0.6 s, capped
  at half roll stick and 0.6 pitch stick): about 2 s to settle from a 45 degree bank or a
  30 degree climb, slowing near level, no overshoot. Pitch waits until the wings are within
  90 degrees of level. No yaw: the heading is kept (a bank still turns the plane while it
  rolls out). Any stick outside the dead zone takes over at once; levelling resumes 0.25 s
  after letting go. The floor and border guards still win.
- **Mouse aim** (`HasAim`): an autopilot steers smoothly toward `AimDirection`, like War
  Thunder: it banks toward the target's heading (up to 75 degrees), pulls toward it and
  fine-tunes with yaw; wings come level once the target is ahead. Pilot sticks are ignored.
- **Floor**: below 20 m is never reached. When the plane is within 40 m of the floor plus
  2.5 s of its current descent, the autopilot takes over and climbs gently (about 20
  degrees) until clear. A hard clamp at 20 m is only a backstop.
- **Border**: a 4 km x 4 km area centered on the origin (|x|, |z| <= 2000 m). Within 400 m
  of an edge and heading out, the autopilot turns back toward the origin, keeping a mild
  climb or dive. A hard clamp at the edge is only a backstop. The floor wins over the pilot,
  and both guards win over the pilot's sticks and aim.

## Tests

`Tests/FlightModuleTests.cs`, with a fake bus (`TestBus`) that publishes fake
`ControlInput`s: start state, speeds stay in 25..60 (cruise, max, min, dives, climbs,
random flying), aim steering converges smoothly and banks into turns, rolls are quicker
than pitch and yaw, hands-off levelling (banks, climbs and dives settle in 2 to 3 s
without overshoot or wobble, heading kept, the short delay and dead zone, sticks override
at once, inverted rights itself the shorter way, steep dives recover above the floor),
floor and border hold, determinism, other planes' input ignored, no
allocations per tick.

Test it alone: `./test.sh Flight`. No Godot view: the Plane module draws the plane.
