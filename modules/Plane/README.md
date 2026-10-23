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

## Model (`Models/`)

`biplane.py` is the model as code, a Blender script; it writes `biplane.glb` (both
committed). Rebuild it from the repository root with:

    blender -b --factory-startup --python modules/Plane/Models/biplane.py

Nose toward -Z, Y up, about 7 m span: fat tapered green fuselage, red cowl and spinner,
cream staggered wings with rounded tips and blue-white-red roundels, wooden struts, cream
tail with a blue-white-red rudder, twin black guns, fat black wheels with red hubs and a
little pilot (leather helmet, goggles, white scarf). Nodes: `Biplane` (everything joined,
one surface per color), `Propeller` (separate, spins about its Z) and the empty `Exhaust`
(where the smoke starts).

## Godot (`Godot/Main.tscn`, `PlaneView.cs`, `Toon.gdshader`, `Outline.gdshader`)

Shows `biplane.glb` at plane 0's position and orientation, interpolated between the last
two fixed steps (`Previous` to `Latest` by the physics interpolation fraction). Every
surface gets `Toon.gdshader` (three cel bands and a faint rim) in its glTF color, with
`Outline.gdshader` as next pass: an inverted-hull ink outline about 3 px wide. The
propeller spins at 6 to 30 rev/s with throttle; a faint white smoke trail (world-space CPU
particles) streams from the exhaust. The plane is hidden until a state arrives. The .glb is
loaded as an imported scene when the editor has imported it, else read at runtime with
`GLTFDocument` (so headless runs need no import step).

Opened on its own (no module bound, as `screenshot.sh` does) it runs a preview: its own
Core fed a gentle banked right circle at 400 m, a sky, a light and a chase camera. In the
game it adds no camera or light.

Screenshot: `screenshot.png`, made with
`./screenshot.sh modules/Plane/Godot/Main.tscn modules/Plane/screenshot.png`.

## Tests (`Tests/`)

`PlaneModuleTests` (with a fake bus): no state before one arrives, keeps the latest state of
plane 0 (every field), keeps the previous one for interpolation, ignores other plane ids,
publishes nothing, no allocations, one entry class.

Test it alone: `./test.sh Plane`.
