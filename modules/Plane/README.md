# Plane

The player's plane as seen in the game: a chunky cartoon WWI biplane drawn where Flight
says it is.

## Messages

Publishes nothing; its Contract (`Contract/`, namespace `Neo.Plane`) is empty for now.
Subscribes to `Neo.Flight.PlaneState` (modules/Flight/Contract) and keeps only `PlaneId` 0.

## Core (`Core/`)

`PlaneModule` (the `IModule`): keeps the latest `PlaneState` of plane 0 (`Latest`), the one
before it (`Previous`, equal to `Latest` after the first), `HasState` and `Updates` (states
received). Other plane ids are ignored. No allocations per Tick or per message.

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

## Model (`Models/`)

`biplane.py` is the model as code, a Blender script; it writes `biplane.glb` (both
committed). Rebuild it from the repository root with:

    blender -b --factory-startup --python modules/Plane/Models/biplane.py

Nose toward -Z, Y up, about 7 m span: fat tapered green fuselage, red cowl and spinner,
cream staggered wings with rounded tips and blue-white-red roundels, wooden struts, cream
tail with a blue-white-red rudder, twin black guns, fat black wheels with red hubs and a
little pilot (leather helmet, goggles, white scarf). Nodes: `Biplane` (everything joined,
one surface per color), `Propeller` (separate, unrotated, at the nose on the center line:
blades span its X/Y and it is thin along its Z, the shaft, which it spins about) and the
empty `Exhaust` (where the smoke starts).

## Godot (`Godot/Main.tscn`, `PlaneView.cs`, `Toon.gdshader(inc)`, `ToonFade.gdshader`, `Outline.gdshader`, `PropDisc.gdshader`)

Shows `biplane.glb` at plane 0's position and orientation, interpolated between the last
two fixed steps (`Previous` to `Latest` by the physics interpolation fraction). Every
surface gets `Toon.gdshader` (three cel bands and a faint rim) in its glTF color, with
`Outline.gdshader` as next pass: an inverted-hull ink outline about 3 px wide (the cel
code is in `Toon.gdshaderinc`). The `Propeller` node turns about its Z (the nose axis) by
`PropellerLook.BladeTurns` each frame, with the frame's real time; its blades use
`ToonFade.gdshader` (the same cel shading with an `alpha`), set to `BladeAlpha`, and keep
their ink outline only while solid. `PropDisc` (child of the propeller, a thin 1.42 m
radius cylinder 0.1 m in front of the blades, `PropDisc.gdshader`: unshaded,
semi-transparent, soft radial streaks, no outline) shows at `DiscAlpha`, so at cruise the
prop reads as a fast blur. A faint white smoke trail (world-space CPU particles) streams
from the exhaust. The plane is hidden until a state arrives. The .glb is
loaded as an imported scene when the editor has imported it, else read at runtime with
`GLTFDocument` (so headless runs need no import step).

Opened on its own (no module bound, as `screenshot.sh` does) it runs a preview: its own
Core fed a gentle banked right circle at 400 m, a sky, a light and a chase camera. In the
game it adds no camera or light.

Screenshot: `screenshot.png` (older, before the prop disc). New screenshots go to
`$SCREENSHOT_DIR`, else `modules/Plane/shots/` (git-ignored, not committed):
`./screenshot.sh modules/Plane/Godot/Main.tscn modules/Plane/shots/plane.png`.

## Tests (`Tests/`)

`PlaneModuleTests` (with a fake bus): no state before one arrives, keeps the latest state of
plane 0 (every field), keeps the previous one for interpolation, ignores other plane ids,
publishes nothing, no allocations, one entry class. `PropellerLookTests`: the blades never
turn more than a sixth of a turn per frame at any frame rate, throttle or hitch; they turn
clearly at idle and faster with throttle; no disc and solid blades near idle; the disc fades
in and the blades partly out, smoothly, to a semi-transparent blur from cruise up;
`biplane.glb` has an unrotated `Propeller` node at the nose, thin along the nose axis.

Test it alone: `./test.sh Plane`.
