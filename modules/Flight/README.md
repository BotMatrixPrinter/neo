# Flight

Flies the planes: an arcade model, not a simulation. Eight planes, `PlaneId` 0..7 (the
"Enemy Planes" 4 v 4): ids 0-3 team Allies (0 is the player, 1-3 bots), ids 4-7 team Central.

- **Contract** (`Contract/`, namespace `Neo.Flight`):
  - `Teams` (static): `PlaneCount` (8), `TeamSize` (4), `bool IsAllies(int planeId)` (true
    for 0-3; false for 4-7 and any other id), so every module agrees on teams.
  - `PlaneState`, published on the bus every fixed step, after the step, for every plane,
    in id order:
  - `int PlaneId`
  - `Vector3 Position`: world, meters, Y up
  - `Quaternion Orientation`: body to world (System.Numerics); body axes +X right, +Y up,
    -Z forward (Godot's convention), so the nose is `Vector3.Transform(-Vector3.UnitZ, Orientation)`
  - `Vector3 Velocity`: world m/s, always along the nose
  - `float Speed`: m/s, the length of `Velocity`
  - `float Throttle`: 0..1, the throttle in use
  - `bool Alive`: false while the plane is down (falling or on the ground). Added after the
    others: the old six-argument constructor still works and means `Alive` true.
- **Input**: subscribes to `Neo.Input.ControlInput` (modules/Input/Contract); each plane uses
  the latest one with its `PlaneId`; ids outside 0..7 are ignored. Before any arrives for a
  plane: hands off (it levels itself), throttle 0.5.
- **Damage**: subscribes to `Neo.Damage.PlaneDamageState`, `PlaneDestroyed` and
  `PlaneRespawn` (modules/Damage/Contract); see "Damage" below.
- **Core** (`Core/`): `FlightModule` (the `IModule`) keeps one `FlightModel` per plane
  (`Plane(id)`, `Model` is plane 0), steps them all each tick and publishes their states.
  No allocations per tick; deterministic (same inputs, same result).

## The model (`FlightModel`)

- **Start** (`StartPosition(id)`, `StartOrientation(id)`): in the air at 400 m, level, at
  cruise. Allies 1500 m south of the center (Z = +1500) heading north (-Z), Central 1500 m
  north (Z = -1500) heading south (+Z); each team side by side 60 m apart along X, centered
  on X = 0 (X = -90, -30, 30, 90 for the 1st..4th of the team).
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

## Damage

Each plane keeps the latest part health from `PlaneDamageState` (0..1, 1 undamaged):

- **Engine**: max speed is `MaxSpeed` times 0.6 + 0.4 x health (60 m/s healthy, 36 m/s
  dead); neither throttle nor diving goes faster.
- **Wings**: a damaged wing adds roll stick toward its side, 0.4 x (right damage - left
  damage). Hands off, the levelling settles at a bank toward the damaged wing (about 13
  degrees at 0.7 health, about 40 at 0); with sticks the pilot has to hold against it.
- **Tail**: pitch and yaw rates are scaled by 0.4 + 0.6 x tail health (roll is not).
- **`PlaneDestroyed`**: the plane falls, ignoring its pilot: the nose drops to about 60
  degrees down (40 deg/s), it spins slowly about its nose (90 deg/s) and turns a little,
  gaining speed in the dive, until it reaches the ground (Y = 0, World's `GroundY`); then
  it stays there, speed and velocity 0. `Alive` is false all that time.
- **`PlaneRespawn`**: back at its team's start point and heading, level, at full speed
  (60 m/s), `Alive` true, parts back at full health.

## Tests

`Tests/FlightModuleTests.cs`, with a fake bus (`TestBus`) that publishes fake
`ControlInput`s: start state, speeds stay in 25..60 (cruise, max, min, dives, climbs,
random flying), aim steering converges smoothly and banks into turns, rolls are quicker
than pitch and yaw, hands-off levelling (banks, climbs and dives settle in 2 to 3 s
without overshoot or wobble, heading kept, the short delay and dead zone, sticks override
at once, inverted rights itself the shorter way, steep dives recover above the floor),
floor and border hold, determinism (with damage, downing and respawn), no allocations per
tick; eight planes start by team and are published every tick in id order, four planes fly
from their own inputs (others hands off), the `Teams` helper, `PlaneState.Alive` with the
old constructor, engine, wing and tail damage, falling to the ground and staying there on
`PlaneDestroyed`, respawn at the team's start point at full speed.

Test it alone: `./test.sh Flight`. No Godot view: the Plane module draws the plane.
