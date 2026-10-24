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
  - `Vector3 Velocity`: world m/s, along the nose, plus a collision's fading knock (see
    "Collisions") for about a second after one
  - `float Speed`: m/s along the nose (25..85), the length of `Velocity` except during a knock
  - `float Throttle`: 0..1, the throttle in use
  - `bool Alive`: false while the plane is down (falling or on the ground). Added after the
    others: the old six-argument constructor still works and means `Alive` true.
  - `PlaneCrashed` (`int PlaneId`, `Vector3 Position`, `float Speed`): published once when a plane
    hits the ground (see "Ground"), flying or already falling after a downing, in the step it
    happens, after that step's `PlaneState`s. `Position` is the crash point on the ground (Y the
    terrain height there), `Speed` the airspeed at impact (m/s).
- **Input**: subscribes to `Neo.Input.ControlInput` (modules/Input/Contract); each plane uses
  the latest one with its `PlaneId`; ids outside 0..7 are ignored. Before any arrives for a
  plane: hands off (it flies on level, as it started), throttle 0.5.
- **Damage**: subscribes to `Neo.Damage.PlaneDamageState`, `PlaneDestroyed`, `PlaneRespawn`
  and `RepairState` (modules/Damage/Contract); see "Damage" below.
- **Collisions**: subscribes to `Neo.Collisions.CollisionImpulse` (modules/Collisions/Contract);
  see "Collisions" below.
- **World**: reads `Neo.World.Terrain.HeightAt(x, z)` (modules/World/Contract) for the ground.
- **Core** (`Core/`): `FlightModule` (the `IModule`) keeps one `FlightModel` per plane
  (`Plane(id)`, `Model` is plane 0), steps them all each tick and publishes their states, then a
  `PlaneCrashed` for each plane that hit the ground in that step (`FlightModel.JustCrashed`).
  No allocations per tick; deterministic (same inputs, same result).

## The model (`FlightModel`)

- **Start** (`StartPosition(id)`, `StartOrientation(id)`): in the air at 400 m, level, at
  cruise. Allies 1500 m south of the center (Z = +1500) heading north (-Z), Central 1500 m
  north (Z = -1500) heading south (+Z); each team side by side 60 m apart along X, centered
  on X = 0 (X = -90, -30, 30, 90 for the 1st..4th of the team).
- **Speed**: throttle sets the level speed: 0 → 25 m/s (`MinSpeed`), 0.5 → 42 m/s (cruise,
  about 150 km/h), 1 → 60 m/s (`MaxSpeed`, the level maximum). Each step the speed changes by
  the throttle's pull and drag (toward the throttle's speed at 0.5/s, `SpeedResponse`, adding
  at most 3 m/s², `MaxThrustAcceleration`) plus gravity along the flight path,
  -9.81 x 0.8 (`Gravity` x `GravityScale`, the arcade share) x sin(climb angle). So a climb
  steeper than about 22 degrees bleeds speed even at full throttle, a gentle one keeps most of
  it (about 57 m/s at 10 degrees). Above the level maximum only a weak drag (0.15/s,
  `OverspeedDrag`) acts, so a full-throttle dive builds speed up to 85 m/s (`DiveMaxSpeed`, the
  cap); back in level flight the speed settles at the throttle's again. Always 25..85 m/s.
  The plane flies where its nose points: no stall, no spin, no slip.
- **Height** (no hard ceiling): the engine loses power as the plane climbs. The share lost is
  (h / 1000 m)^4 (`AltitudePowerLoss`, `PowerAltitude` = 1000 m): almost nothing low down
  (0.2% at 200 m, 2.6% at 400 m, 13% at 600 m), most of it in the last few hundred meters,
  smoothly, and it keeps growing above. The throttle's most added acceleration
  (`ClimbPower`) is 3 m/s² x the engine's share x (1 - that loss), and the level max speed
  (`LevelMaxSpeed`) falls from 60 m/s at the ground to 40 m/s (`PowerAltitudeSpeed`, about
  cruise) at 1000 m. So full-throttle climbs get slower with height and level off around
  1000 m by themselves: there full throttle only holds level flight at about cruise, with
  nothing left to climb, and any climb bleeds speed until the low-speed limit drops the nose.
  Above 1000 m (reachable briefly by zooming up with speed, to about 1045 m from 980 m at
  41 m/s) the power is a loss even in level flight: the plane slows to the minimum speed
  and then glides down just steeply enough to keep it, back to 1000 m (hands off it keeps
  that glide, below 1000 m). No clamp, no bounce. Up high the overspeed drag starts at the
  lower level max, so dives build speed a little slower there.
- **Too slow to climb**: below 35 m/s (`MinSpeed` + `LowSpeedBand`) the climb is limited:
  the sine of the largest climb allowed is the share of that 10 m/s band left (level at
  25 m/s, free at 35 m/s), lowered when the engine lacks power even at the minimum speed
  (above 1000 m, or a dead engine near it) by that loss over gravity, so the plane glides
  down instead; a steeper climb, whoever commands it (sticks, mouse aim, hands off, border
  guard), is eased down (time constant 0.5 s, `LowSpeedResponse`), its yaw share first
  (steeply banked, yaw raises the nose too): the nose drops gently until the plane has speed
  again, so a full-throttle climb settles at a gentle climb near the minimum speed. Lowering
  the nose is never limited.
- **Sticks** (no `HasAim`): `Pitch`, `Roll`, `Yaw` (-1..1) are body rates of 70, 180 and
  40 deg/s; commands are smoothed (15/s) so rates are snappy but never jump. A banked plane
  also turns about world up (30 deg/s at 90 degrees of bank).
- **Hands off** (no `HasAim`, and `Pitch`, `Roll`, `Yaw` all inside a 0.05 dead zone for
  0.25 s): the plane levels its wings, like an easy arcade plane, but not its nose. It rolls
  back to wings level the shorter way (so from inverted it rolls back the way it came), at a
  rate proportional to what is left (time constant 0.6 s, capped at half roll stick): about
  2 s to settle from a 45 degree bank, slowing near level, no overshoot. The nose keeps the
  climb or dive it had when the sticks were let go (within a degree): the few degrees the
  stick's lag adds just after the release are eased back (same time constant, capped at 0.6
  pitch stick, only while the wings are within 90 degrees of level). No yaw: the heading is
  kept (a bank still turns the plane while it rolls out). Any stick outside the dead zone
  takes over at once; levelling resumes 0.25 s after letting go. The border guard still
  wins; once clear, hands off keeps the climb or dive the guard left. Nothing pulls up near the
  ground: a plane let go in a dive dives on into the ground and crashes.
- **Mouse aim** (`HasAim`): an autopilot steers smoothly toward `AimDirection`, like War
  Thunder: it banks toward the target's heading (up to 75 degrees), pulls toward it and
  fine-tunes with yaw; wings come level once the target is ahead. Pilot sticks are ignored.
- **Ground** (no floor, no pull-up): the ground is World's terrain, `Terrain.HeightAt(x, z)`.
  Each step, after moving, a plane whose lowest point (its position's Y minus 1.5 m,
  `CrashClearance`) is at or below the terrain there has crashed: it stops at that point on the
  ground (Y = the terrain height, speed and velocity 0, `Grounded` true), `Alive` is false from
  then on (as a downed plane), and the module publishes one `PlaneCrashed`. It stays there until
  its `PlaneRespawn`. Flying low just above the ground is fine. A falling downed plane ends the
  same way (see "Damage").
- **Border**: a 4 km x 4 km area centered on the origin (|x|, |z| <= 2000 m). Within 400 m
  of an edge and heading out, the autopilot turns back toward the origin, keeping a mild
  climb or dive. A hard clamp at the edge is only a backstop. The guard wins over the pilot's
  sticks and aim.

## Damage

Each plane keeps the latest part health from `PlaneDamageState` (0..1, 1 undamaged):

- **Engine**: the level max speed (`MaxSpeedNow`, at the ground) is `MaxSpeed` times 0.6 + 0.4 x health
  (60 m/s healthy, 36 m/s dead), and the throttle's most added acceleration is scaled the same,
  on top of the loss to height (a dead engine levels off lower, about 980 m).
  A plane faster than that slows down to it (by the overspeed drag, gradually); it climbs with
  less speed and dives slower (the overspeed drag acts above its lower max), still capped at 85 m/s.
- **Wings**: a damaged wing adds roll stick toward its side, 0.4 x (right damage - left
  damage). Hands off, the levelling settles at a bank toward the damaged wing (about 13
  degrees at 0.7 health, about 40 at 0); with sticks the pilot has to hold against it.
- **Tail**: pitch and yaw rates are scaled by 0.4 + 0.6 x tail health (roll is not).
- **`PlaneDestroyed`**: the plane falls, ignoring its pilot: the nose drops to about 60
  degrees down (40 deg/s), it spins slowly about its nose (90 deg/s) and turns a little,
  gaining speed in the dive, until it reaches the ground (World's terrain, same rule as
  "Ground"): then it publishes one `PlaneCrashed` and stays there, at the terrain height, speed
  and velocity 0. `Alive` is false all that time.
- **`RepairState`** (in-flight repair): while a plane's latest `Repairing` is true
  (`FlightModel.Repairing`), its pilot's `Pitch`, `Roll`, `Yaw` and aim are ignored: it flies on
  hands off at once (no 0.25 s wait): wings level, the climb or dive it had kept, its heading
  kept; throttle still applies, and the border guard still acts. When `Repairing`
  turns false the pilot's input applies again at once. Respawn clears it.
- **`PlaneRespawn`** (also after a crash): back at its team's start point and heading, level,
  at full speed (60 m/s), `Alive` true, parts back at full health.

## Collisions

On a `CollisionImpulse` for a flying plane (`FlightModel.ApplyImpulse`; downed planes, ids
outside 0..7 and non-finite values are ignored), at once:

- **Separation**: the plane moves by it (still inside the border backstop; knocked into the
  ground, it crashes in the next step).
- **VelocityChange**: its part along the nose changes the speed (kept within 25..85 m/s); the
  rest (sideways, and whatever the speed limits cut off) becomes a **knock** velocity
  (`FlightModel.Knock`) added to the flight along the nose, so the whole change applies at once.
  The knock fades smoothly (time constant 0.3 s, `KnockTimeConstant`: about a third left at 0.3 s,
  4% at 1 s) and is gone (below 0.05 m/s, `KnockRest`) within about 1.5-2 s: the plane is shoved
  sideways or back (about 0.3 s x the knock, e.g. 4.5 m for 15 m/s), then flies on as before.
- **AngularKick** (body rad/s, x pitch, y yaw, z roll, right-handed about +X right, +Y up,
  +Z back): added as a spin (`FlightModel.Kick`) that turns the plane on top of its controls and
  fades the same way. A wingtip strike sends it into a brief roll (4 rad/s: about 40 degrees);
  the pilot's sticks, aim or the hands-off levelling act on top and bring it back (hands off,
  wings level again within about 3 s; a pitch kick's climb is taken back to the one held).

The border clamp still holds.
Destroy and respawn clear knock and spin. Deterministic, no allocation.

## Tests

`Tests/FlightModuleTests.cs`, with a fake bus (`TestBus`) that publishes fake
`ControlInput`s: start state, speeds stay in 25..85 (cruise, max, min, dives, climbs,
random flying, respawning after any crash), height (the power curve is full at the ground, smooth, 0 climb power and
about 40 m/s level at 1000 m, a loss above, no ceiling constant left; low altitude unchanged;
full-throttle climbs, player and bot, slow with height and level off around 1000 m, a dead
engine slower and lower; a fast zoom passes 1000 m, then still pulling up or hands off sinks
back with no bounce; level flight at 1000 m holds about cruise, a climb there only bleeds
speed), gravity (a 60 degree full-throttle climb only bleeds speed, down to near the
minimum, then the climb is eased to a gentle one; a gentle climb keeps most of it; a slow plane,
by stick or aim, cannot hold a steep climb, the nose drops gently and climbs again with
throttle; a dive builds speed past 60 up to the 85 cap, level flight settles back at the
throttle's speed, at full and cruise throttle), aim steering converges smoothly and banks into turns, rolls are quicker
than pitch and yaw, hands-off levelling (banks settle in 2 to 3 s without overshoot or
wobble; climbs and dives, banked or not, keep their pitch angle within a degree and their
heading; the short delay and dead zone, sticks override at once, inverted rights itself the
shorter way, a steep dive is kept all the way into the ground: no pull-up), the ground
(stick-forward dives, player and bot, a split-S and an aim straight down crash where the lowest
point reaches the terrain, resting at its height, with exactly one `PlaneCrashed`; a dive aimed
at a mountain crashes on the slope at its height; flying at 9 m over the plain, hands off, keeps
its height with no pull-up and no crash; respawn after a crash flies again and can crash again),
the border holds, determinism (with damage, downing and respawn), no allocations per
tick; eight planes start by team and are published every tick in id order, four planes fly
from their own inputs (others hands off), the `Teams` helper, `PlaneState.Alive` with the
old constructor, engine damage (lower level speed, slows down gradually to it, climbs and
dives slower), wing and tail damage, falling to the ground (one `PlaneCrashed`, at the terrain height) and
staying there on `PlaneDestroyed`, respawn at the team's start point at full speed, repairing (sticks and aim
ignored, wings level and heading and climb kept, sticks back at once after; other and unknown ids;
throttle still works; the border guard still acts), collisions (two planes on a
head-on course, given a fake of Collisions' bounce, move just clear, get the whole velocity
change, pass each other without touching again and fly on; a sideways shove moves the plane at
once and its knock fades smoothly, gone within 2 s, then it flies on straight; speed along the
nose changes within 25..85 and settles back; a roll kick either way rolls it and hands off
recovers without overshoot; sticks act on top of a kick; a pitch kick is taken back; knocked into the
ground it crashes once; the border holds; downed planes, unknown ids and NaN ignored; deterministic and allocation-free with
impulses). The fake bus delivers each message once per subscription to its type, like the shell's, and
records the `PlaneCrashed` messages.

Test it alone: `./test.sh Flight`. No Godot view: the Plane module draws the plane.
