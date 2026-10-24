# HUD

The on-screen display: markers on the other planes (off-screen arrows toward them), speed, altitude, gun heat, balloons popped, the player's damage, kills
and deaths, the kill feed and the "shot down" countdown, in the game's cartoon
style (dark rounded panels with a gold edge, bold outlined text).

## Messages

Publishes nothing; its Contract (`Neo.HUD.Contract`) is empty for now.

Uses other modules' Contracts: reads `Neo.Flight.PlaneState` (every plane; teams from `Neo.Flight.Teams`), `Neo.Guns.GunState`
(plane 0), `Neo.Targets.TargetDestroyed` (any target), and from `Neo.Damage`:
`PlaneDamageState` (plane 0), `PlaneDestroyed` (any plane), `PlaneRespawn` (plane 0) and
`RepairState` (plane 0).

Plane names (the "Enemy Planes" wave): 0 is the player, "You"; 1-3 are "Blue <id>" (Allies),
4-7 "Red <id>" (Central).

## Core (`Core/`)

`HudModule` (the `IModule`). For the view:

- `SpeedKmh`: `PlaneState.Speed` (m/s) x 3.6, sampled each Tick; `AltitudeMeters`: world Y of
  `PlaneState.Position`, sampled each Tick. `PreviousSpeedKmh` / `PreviousAltitudeMeters` are
  the Tick before's (equal to the current ones on the first sample), to interpolate.
  `HasPlane` once a plane-0 `PlaneState` has arrived.
- `Heat` (0..1) and `Overheated`: from the last plane-0 `GunState`.
- `BalloonsPopped`: one per `TargetDestroyed` received since start.
- Damage panel: `Engine`, `LeftWing`, `RightWing`, `Tail` (0..1, 1 before any state) from the
  last plane-0 `PlaneDamageState`; `Level(PlanePart)` / `LevelOf(health)` give the `PartLevel`:
  `Good` (green) from 0.6, `Worn` (orange) below 0.6, `Critical` (red) below 0.3, `Destroyed`
  (crossed out) at 0.
- `Kills`: `PlaneDestroyed` of an enemy (4-7) shot by plane 0 (friendly fire is no kill);
  `Deaths`: every `PlaneDestroyed` of plane 0.
- Repair (from plane 0's `RepairState`): `Repairing`, `RepairPart`, `RepairProgress` (0..1, 0 when
  not repairing), `RepairText` "REPAIRING ENGINE" / "LEFT WING" / "RIGHT WING" / "TAIL"
  (`RepairingText(part)`; empty when not repairing), `RepairPartsAfter` (`PartsLeft` - 1) and
  `RepairMoreText` "1 more".."3 more" (empty for none). `RepairCancelled` for 1.5 s
  (`RepairCancelledSeconds`, `RepairCancelledSecondsLeft`) when a repair stops while the plane is
  alive and still damaged (Repair let go mid-cycle; not when the last part is done or the plane
  goes down; cleared when a repair runs again); text `RepairCancelledText` "Repair cancelled".
  `ShowRepairHint` while alive, a part below 1 and no repair runs; text `RepairHintText`
  "Hold Y / R: repair".
- Kill feed: one line per `PlaneDestroyed` (any plane), up to 6 (a 7th pushes out the oldest),
  each for 5 s of Ticks. `KillFeedCount`, `KillFeedText(i)` (0 the oldest),
  `KillFeedVictim/Shooter(i)`, `KillFeedSecondsLeft(i)`. Lines: "You shot down Red 5",
  "Red 4 shot you down", "Blue 2 shot down Red 6", "Red 5 went down" (downed itself); built once
  (`KillLine`, `PlaneName`).
- Shot down: `PlayerDown` from plane 0's `PlaneDestroyed` until its `PlaneRespawn`;
  `RespawnSecondsLeft` counts down from 5 each Tick, `RespawnCountdown` is it rounded up (5..1,
  at least 1 while down) and `RespawnText` "Back in the air in N" (prebuilt; empty when up).

- Plane markers: every `PlaneState` (ids 0-7) is kept with the one before (not across a respawn),
  like the Plane view; `HasMarker(id)` for another plane than the player's that is alive in its
  last state; `PlanePosition(id, fraction)` interpolates its world position between the two.

`Markers` (static, the markers' math; camera space +X right, +Y up, looking along -Z; screen +Y
down): `TeamColor(id)` (Allies green, Central red) and `ShapeOf(id)` (`Rounded` / `Sharp`, so the
teams differ by shape too), `IsBehind` / `IsOnScreen` (in front and inside the screen),
`EdgeDirection` / `EdgeAngle` (where to turn to see it, mirrored behind the camera so behind-left
is left; straight behind is down), `EdgePoint` (on a circle of 40% of the screen height around
the center) and `DistanceText` ("640 m", from 1000 m "1.2 km"; prebuilt, no allocations).

Other messages for other planes are ignored (except in the kill feed). No allocations per Tick or per message.

## Godot (`Godot/Main.tscn`, `HudView.cs`)

A `CanvasLayer` (layer 10), laid out for 1920x1080 and scaled uniformly to fit the screen,
panels anchored to the corners:

- bottom left: SPEED (km/h) and ALT (m), interpolated between fixed ticks, and the GUNS heat
  bar: orange, warming towards red as it fills; while overheated it flashes red with
  "OVERHEATED" on it;
- top left: a little balloon and "Balloons popped: N"; when N rises the number bounces and
  wiggles and a "+1" floats up; next to it "Kills N  Deaths N";
- bottom, next to the flight panel: DAMAGE, a little plane from above (`DamageIcon.cs`) whose
  engine, wings and tail are green / orange / red, crossed out when destroyed; the part being
  repaired glows (pulsing cream-gold outline);
- above the damage panel: while repairing, "REPAIRING <part>", "N more" and a green progress bar;
  after letting go mid-cycle, "Repair cancelled" in red (fading out at the end); else, while
  damaged, a small "Hold Y / R: repair" panel;
- top right: the kill feed, newest on top, lines with the player on a gold-brown panel, fading
  out at the end of their 5 s;
- under all the panels (`PlaneMarkers.cs`), for every other alive plane: on screen a small
  downward triangle a fixed 30 px above it with its distance over it (outlined); off screen or
  behind the camera an arrow on the 40%-of-height circle pointing where to turn. Allies green and
  rounded, Central red and sharp. Projected with `GetViewport().GetCamera3D()`, positions
  interpolated between ticks;
- while the player is down: a red tint, a big centered "SHOT DOWN!" that pops in, and
  "Back in the air in N" under it.

Numbers come from a prebuilt string table, so `_Process` does not format or allocate. When the
shell disables the module, the layer hides itself. Opened on its own (no module bound, as
`screenshot.sh` does) it runs a preview with its own Core over a sky backdrop: a plane
climbing, the guns heating up and overheating every 6 s, a balloon popping every 0.5 s, and
six other planes around the player (three ahead with markers, three off screen; one more is down),
with its own camera following the player, and every 10 s two kills, then the player's parts wearing down until shot down at 1.3 s and back 5 s later; at 6.6 s the
engine and left wing are hit (the hint), at 7.1 s the engine is repaired (Repair held) and at 9.5 s
let go ("Repair cancelled").

Screenshots (not committed): `./screenshot.sh modules/HUD/Godot/Main.tscn` (`HUD.png`, shot
down), damaged but flying, with the plane markers, `FRAMES=65 ./screenshot.sh modules/HUD/Godot/Main.tscn HUD-damage`, and,
overheated, `FRAMES=200 ./screenshot.sh modules/HUD/Godot/Main.tscn HUD-overheated`; repair:
`FRAMES=415` (`HUD-repair-hint`), `FRAMES=520` (`HUD-repairing`), `FRAMES=580` (`HUD-repair-cancelled`).

## Tests (`Tests/`)

`HudModuleTests` (with a fake bus): km/h conversion, altitude, other planes ignored, previous
values for interpolation, heat and overheat, counting pops, part levels and the damage panel
(plane 0 only), plane names and kill feed lines, the feed's 5 s and its limit, kills and deaths,
the shot-down countdown to the respawn, markers only for other alive planes and their interpolated
positions (no sweep on a respawn), repair (part texts, progress, "N more", the hint only while
damaged and not repairing or down, "Repair cancelled" for 1.5 s only on a mid-cycle release and
cleared by a new repair, not on finishing or going down, other planes ignored), publishes nothing, no allocations.

`MarkersTests`: team colors and shapes, distance texts, on/off screen (outside the view, behind
the camera), the edge direction and angle in front and behind, the 40% circle.

Test it alone: `./test.sh HUD`.
