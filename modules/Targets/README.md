# Targets

Five tethered observation balloons to shoot at (the first playable build's targets).

## Messages (Contract, namespace `Neo.Targets`)

| Message | Who publishes | When |
| --- | --- | --- |
| `TargetState` (int `TargetId`, `Vector3` `Position`, float `Radius`, int `Health`, bool `Alive`) | Targets | every Tick, once for each target (ids 0 to 4, in order), popped ones too (`Alive` false, `Health` 0) |
| `DamageTarget` (int `TargetId`, int `Amount`, int `ShooterPlaneId`) | other modules (the guns) | to hurt a target; Targets subscribes. `ShooterPlaneId` is the PlaneId of the plane that shot, -1 when unknown (the 2-argument constructor). Ignored for a popped target, an unknown id or `Amount` <= 0 |
| `TargetDestroyed` (int `TargetId`, `Vector3` `Position`, int `ShooterPlaneId`) | Targets | once when a target's health reaches 0, with where it was and the `ShooterPlaneId` of the `DamageTarget` that took its last health (the plane that popped it; -1 when unknown, also the 2-argument constructor's default) |

Positions are `System.Numerics.Vector3` in meters, Y up. A hit is anything inside the sphere
of `Radius` (9 m) around `Position`.

## Core (`Core/`)

`TargetsModule` (the `IModule`): 5 balloons tethered over the playable area (World's
`WorldBounds.HalfSize`, read at run time; no copy of the map size here), 340 to 470 m up: one
over the center and two toward each side (the teams start on -Z and +Z), their wanted spots
written as fractions of `HalfSize` (+-0.55 on X and Z for the four side ones), every tether point at
least 0.2 x `HalfSize` inside the edge (`EdgeMarginFraction`; within `MaxReach` of the origin on X and Z), each swaying under 2 m around its tether point. The tether
points come from World's `Terrain` (its Contract, `Neo.World`): `PlaceOnDryGround` brings a
wanted spot inside `MaxReach` and moves it, if over water (or within 30 m of it, `ShoreMargin`), to
the nearest dry spot inside `MaxReach`, in 10 m rings, and raises it if needed so the balloon stays at least 150 m (`MinClearance`)
above the terrain under it. `Anchor(id)` is where its cable is anchored: the ground
(`Terrain.HeightAt`) under its tether point. Each has 15
health; `DamageTarget` lowers it; at 0 it publishes `TargetDestroyed`, counts a pop
(`Pops`, for the HUD) and comes back with full health 60 s later (`RespawnSeconds`) at the
same spot. `GetState(id)` gives a balloon's current state. No allocations per Tick or per
damage.

`BalloonViews`: what the Godot view shows for each balloon, followed from the module's state
(position, its cable from `CableTop` under the body down to `CableBottom`, the fixed anchor, whether the shape with its cable is shown, pop effect age): a pop hides the shape
and plays the effect for 1.4 s (`PopSeconds`); a comeback shows the shape again at its spot.
No Godot, so it is tested; no allocations per `Update`.

## Godot (`Godot/Main.tscn`, `TargetsView.cs`)

Draws each balloon from its Core state (through `BalloonViews`) as a cartoon sausage-shaped observation balloon:
cream toon-shaded body with red stripes, tail fins, a small basket, an ink outline (an
inverted-hull overlay) and a cable down to its anchor on the terrain
(stretched each frame as the balloon sways; no winch truck is drawn). When one pops it hides and plays a
cartoon pop: smoke puffs swelling and shreds flying out. Opened on its own (no module bound,
as `screenshot.sh` does) it runs a preview with its own Core, sky, light and a camera on
balloon 0, which pops after 1.5 s and comes back at 61.5 s; in the game it adds no camera or light.

Screenshots (not committed): `FRAMES=60 ./screenshot.sh modules/Targets/Godot/Main.tscn` and,
popping, `FRAMES=118 ./screenshot.sh modules/Targets/Godot/Main.tscn Targets-pop`, and back,
`FRAMES=3750 ./screenshot.sh modules/Targets/Godot/Main.tscn Targets-back`.

## Tests (`Tests/`)

`TargetsModuleTests` (with a fake bus): damage and destruction, one `TargetDestroyed` per
pop with the shooter of the last hit (-1 when unknown), respawn after exactly 60 s at full
health, the view state (`BalloonViews`: hidden while popped, shown again at its spot when
back), states published every tick, placement and sway (inside the area 0.2 x `HalfSize` from
the edge, at least 0.4 x `HalfSize` apart, two balloons on each side and one at the center), cables
anchored at `Terrain.HeightAt` under each balloon, no balloon over water (every water spot found by
sampling the area moves to dry ground inside it; a spot outside the area is brought in), at least
150 m above the terrain (the highest dry ground, found by sampling `Terrain.HeightAt`, raises a
balloon wanted too low), no allocations. Every test position is a fraction of
`WorldBounds.HalfSize` or found by sampling the terrain, so the tests hold for any map size.

Test it alone: `./test.sh Targets`.
