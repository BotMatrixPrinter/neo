# Plane

Every plane as seen in the game, drawn where Flight says it is: the Allies (ids 0-3, the
player is 0) as chunky cartoon WWI biplanes, Central (ids 4-7) as red triplanes, each
showing its damage.

## Messages

Publishes nothing; its Contract (`Contract/`, namespace `Neo.Plane`) is empty for now.
Subscribes to `Neo.Flight.PlaneState` (modules/Flight/Contract) and `Neo.Damage.PlaneDamageState`
(modules/Damage/Contract), for every plane id 0..63 (`MaxPlanes`, Damage's `MaxPlanes`);
other ids are ignored. Uses `Neo.Flight.Teams` and `Neo.World.WorldBounds.GroundY`.

## Core (`Core/`)

`PlaneModule` (the `IModule`): for each plane id seen in a `PlaneState` (`Count`, `IdAt(i)` in
the order they first came) keeps the latest state (`LatestOf(id)`), the one before it
(`PreviousOf(id)`, equal to the latest after the first one and after a respawn, so the view
never slides a plane from its wreck to its start point), `Has(id)` and `UpdatesOf(id)`; its
latest `PlaneDamageState` (`HasDamage(id)`, `DamageOf(id)`: undamaged and alive until one
comes); its life `LifeOf(id)` and `CrashesOf(id)` (times it hit the ground). `HasState`,
`Latest`, `Previous` and `Updates` read the player's plane (id 0). A plane is down when Flight
(`PlaneState.Alive`) or Damage (`PlaneDamageState.Alive`) says so, so it stays down until
Flight has put it back at its start. No allocations per Tick or per message.

`PlaneLook` (static, plain functions the view applies):
- `ModelFor(id)`: `PlaneModel.Biplane` for the Allies (`Teams.IsAllies`), `Triplane` for
  Central; `FileName(model)`: `biplane.glb` / `triplane.glb`.
- Damage thresholds (part health 0..1): a part shows damage below `DamageThreshold` (0.5;
  NaN counts as healthy). `EngineSmoke(engine)`: 0 at or above it, 0.3 just below, thicker
  as it gets worse, 1 at health 0. `WingSmoke(wing)`: a little smoke, up to 0.35.
  `Holes(wing)`: holes and torn canvas, 2 just below the threshold up to 6 at 0.
- `Life(alive, height, speed)` (`PlaneLife`): `Flying` while alive; downed, `Falling` until
  within 1 m of the ground or stopped (under 0.5 m/s), then `Crashed`.

`PropellerLook` (static, plain functions the view applies): how the propeller is drawn. A
real prop is far too fast to draw: a two-blade prop at its true 6 to 30 rev/s turns half a
turn or more per 60 fps frame, so it aliases and looks still or turning backwards
(wagon-wheel effect). So the blades turn at a cartoon rate, 3 rev/s at idle to 8 rev/s at
full throttle (`BladeTurns(throttle, frameSeconds)`), never more than `MaxTurnsPerFrame`
(a sixth of a turn) of the actual frame, whatever the frame rate or a hitch; a two-blade
prop would alias at a quarter. With throttle a prop disc fades in (`DiscFade`, smooth from
throttle 0.15 to fully in at cruise 0.6; `DiscAlpha` up to 0.55) while the blades fade
partly out (`BladeAlpha`, down to 0.4). Near idle: solid blades, clearly turning, no disc.
`SpinAxis` is the propeller's local Z, the plane's nose axis.

## Models (`Models/`)

The models as code, Blender scripts that share `kit.py` (materials, mesh builders, the
propeller, empties, export); each writes its `.glb` next to it (all committed). Rebuild
from the repository root with:

    blender -b --factory-startup --python modules/Plane/Models/biplane.py
    blender -b --factory-startup --python modules/Plane/Models/triplane.py

Both: nose toward -Z, Y up, about 7 m span, the same two-blade wooden `Propeller` node
(separate, unrotated, at the nose on the center line: blades span its X/Y and it is thin
along its Z, the shaft, which it spins about), an empty `Exhaust` (where the smoke starts)
and empties `LeftWing` / `RightWing` on top of the top wing (where wing damage shows).

- `biplane.glb` (the Allies): fat tapered green fuselage, red cowl and spinner, cream
  staggered wings with rounded tips and blue-white-red roundels, wooden struts, cream tail
  with a blue-white-red rudder, twin black guns, fat black wheels with red hubs and a little
  pilot (leather helmet, goggles, white scarf). Body node `Biplane`.
- `triplane.glb` (Central, Red Baron style): short fat bright red fuselage, a cream rotary
  cowl ring with the engine inside, three stacked red wings with rounded tips and a little
  axle wing, black crosses on white on the top and bottom wings, fuselage sides and the
  white rudder, wooden I-struts, twin black guns, black wheels with cream hubs and the same
  little pilot. Body node `Triplane`.

## Godot (`Godot/Main.tscn`, `PlaneView.cs`, `PlaneVisual.cs`, `Toon.gdshader(inc)`, `ToonFade.gdshader`, `Outline.gdshader`, `PropDisc.gdshader`, `Smoke.gdshader`)

`PlaneView` draws every plane id its Core has: the first time an id appears it makes a
`PlaneVisual` with that id's model (`PlaneLook.ModelFor`), and every frame places it
between its last two fixed steps (`PreviousOf` to `LatestOf` by the physics interpolation
fraction). Each .glb is loaded once (as an imported scene when the editor has imported it,
else read at runtime with `GLTFDocument`, so headless runs need no import step) and copied
per plane.

`PlaneVisual`: every surface gets `Toon.gdshader` (three cel bands and a faint rim) in its
glTF color, with `Outline.gdshader` as next pass: an inverted-hull ink outline about 3 px
wide (the cel code is in `Toon.gdshaderinc`). The `Propeller` node turns about its Z (the
nose axis) by `PropellerLook.BladeTurns` each frame, with the frame's real time; its blades
use `ToonFade.gdshader` (the same cel shading with an `alpha`), set to `BladeAlpha`, and keep
their ink outline only while solid. `PropDisc` (child of the propeller, a thin 1.42 m
radius cylinder 0.1 m in front of the blades, `PropDisc.gdshader`: unshaded,
semi-transparent, soft radial streaks, no outline) shows at `DiscAlpha`. Smoke and fire are
world-space CPU particles:
- Flying: a faint white trail from the exhaust; with a damaged engine, dark smoke from the
  exhaust as thick as `EngineSmoke`; a damaged wing shows `Holes` dark ragged holes in the
  top wing's canvas (every other one with a torn flap of canvas fluttering) and trails a
  little grey smoke (`WingSmoke`).
- Falling: fire at the engine and the thickest dark smoke.
- Crashed: a cartoon explosion puff (a burst of fire, then a rising cloud of smoke) where it
  hit, once per crash; the plane is hidden until it respawns (its smoke drifts away).
Dark smoke uses `Smoke.gdshader` (blended over, billboarded), which drops particle slots
not yet spawned so they never show as black squares; light smoke and fire are additive.

Opened on its own (no module bound, as `screenshot.sh` does) it runs a preview: its own
Core fed four planes on a gentle banked right circle at 400 m (the player's biplane, a red
triplane on its tail with a smoking engine and a holed left wing, a wingman and another
triplane further back), a sky, a light and a chase camera. In the game it adds no camera
or light.

Screenshot (not committed): `./screenshot.sh modules/Plane/Godot/Main.tscn` (`Plane.png`).

## Tests (`Tests/`)

`PlaneModuleTests` (with a fake bus): no state before one arrives, keeps the latest state of
plane 0 (every field) and the previous one for interpolation, keeps states for all 8 plane
ids apart (ids in first-seen order), ignores ids out of range, keeps each plane's damage,
a downed plane falls, crashes once on the ground, stays down until Flight respawns it and
then does not slide from the wreck, Flight alone can down a plane, publishes nothing, no
allocations, one entry class. `PlaneLookTests`: team picks the model, no damage looks at or
above half health, engine smoke below half and thicker when worse, wing holes and a little
smoke below half, flying/falling/crashed, both .glb models have their body, `Propeller`,
`Exhaust` and mirrored wing markers, and the same span. `PropellerLookTests`: the blades
never turn more than a sixth of a turn per frame at any frame rate, throttle or hitch; they
turn clearly at idle and faster with throttle; no disc and solid blades near idle; the disc
fades in and the blades partly out, smoothly, to a semi-transparent blur from cruise up;
both models have an unrotated `Propeller` node at the nose, thin along the nose axis.

Test it alone: `./test.sh Plane`.
