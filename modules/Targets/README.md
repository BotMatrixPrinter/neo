# Targets

Five tethered observation balloons to shoot at (the first playable build's targets).

## Messages (Contract, namespace `Neo.Targets`)

| Message | Who publishes | When |
| --- | --- | --- |
| `TargetState` (int `TargetId`, `Vector3` `Position`, float `Radius`, int `Health`, bool `Alive`) | Targets | every Tick, once for each target (ids 0 to 4, in order), popped ones too (`Alive` false, `Health` 0) |
| `DamageTarget` (int `TargetId`, int `Amount`) | other modules (the guns) | to hurt a target; Targets subscribes. Ignored for a popped target, an unknown id or `Amount` <= 0 |
| `TargetDestroyed` (int `TargetId`, `Vector3` `Position`) | Targets | once when a target's health reaches 0, with where it was |

Positions are `System.Numerics.Vector3` in meters, Y up. A hit is anything inside the sphere
of `Radius` (9 m) around `Position`.

## Core (`Core/`)

`TargetsModule` (the `IModule`): 5 balloons tethered around the 4 km x 4 km area centered on
the origin, 340 to 470 m up, each swaying under 2 m around its tether point. Each has 15
health; `DamageTarget` lowers it; at 0 it publishes `TargetDestroyed`, counts a pop
(`Pops`, for the HUD) and comes back with full health 10 s later (`RespawnSeconds`) at the
same spot. `GetState(id)` gives a balloon's current state. No allocations per Tick or per
damage.

## Godot (`Godot/Main.tscn`, `TargetsView.cs`)

Draws each balloon from its Core state as a cartoon sausage-shaped observation balloon:
cream toon-shaded body with red stripes, tail fins, a small basket, an ink outline (an
inverted-hull overlay) and a cable down to the ground. When one pops it hides and plays a
cartoon pop: smoke puffs swelling and shreds flying out. Opened on its own (no module bound,
as `screenshot.sh` does) it runs a preview with its own Core, sky, light and a camera on
balloon 0, which pops after 1.5 s; in the game it adds no camera or light.

Screenshots (not committed): `FRAMES=60 ./screenshot.sh modules/Targets/Godot/Main.tscn` and,
popping, `FRAMES=118 ./screenshot.sh modules/Targets/Godot/Main.tscn Targets-pop`.

## Tests (`Tests/`)

`TargetsModuleTests` (with a fake bus): damage and destruction, one `TargetDestroyed` per
pop, respawn after exactly 10 s at full health, states published every tick, placement and
sway, no allocations.

Test it alone: `./test.sh Targets`.
