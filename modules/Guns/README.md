# Guns

The plane's twin machine guns: bullets, hits on the balloons, heat and overheat.

## Messages

Publishes (Contract, namespace `Neo.Guns`):

| Message | When |
| --- | --- |
| `GunState` (int `PlaneId`, float `Heat` 0..1, bool `Overheated`, bool `Firing`, int `ShotsFired`) | every Tick, for plane 0 |

Uses other modules' Contracts: reads `Neo.Input.ControlInput` (`Fire`, plane 0),
`Neo.Flight.PlaneState` (plane 0) and `Neo.Targets.TargetState` (ids 0 to 31); publishes
`Neo.Targets.DamageTarget(TargetId, 1)` for each bullet that hits.

## Core (`Core/`)

`GunsModule` (the `IModule`). While `Fire` is held, a `PlaneState` has been received and the
guns are not overheated, each of the two guns (muzzles 0.5 m left and right of the center
line, 2 m ahead of the plane's position, in body space) fires 10 rounds per second; the first
volley comes on the tick the trigger is pressed. A bullet starts with the plane's velocity
plus 450 m/s along the nose (-Z of `Orientation`), flies straight and lives 1.5 s (90 ticks).
Each tick every live bullet's segment (from where it was to where it is) is tested against
every alive `TargetState` sphere; the first sphere crossed removes the bullet and publishes
one `DamageTarget(id, 1)`.

Heat rises 1/4 per second while firing (overheated at 1, after 4 s of continuous fire) and
falls 1/2 per second otherwise (2 s from full). Overheated guns stop, trigger held or not,
until heat is below 0.3.

For the view: `Bullets` (the fixed pool of 64, `Bullet` with `Position`, `PreviousPosition`,
`Velocity`, `Age`, `Serial`, `Gun`, `Alive`), `MuzzlePosition(gun)`, `LastVolleyTick`,
`Hits` and `HitPosition(n)` (last 16 kept), `State`. No allocations per Tick or per message.

## Godot (`Godot/Main.tscn`, `GunsView.cs`)

Draws a glowing yellow-orange tracer streak (bright core, see-through orange halo) for
every third bullet, interpolated between fixed ticks; a small outlined muzzle flash at each
gun on each volley; a cartoon spark puff (outlined smoke ball and flying yellow bits) where a
bullet hits. No sound yet. Opened on its own (no module bound, as `screenshot.sh` does) it
runs a preview with its own Core: a stand-in biplane flying and firing at a balloon, sky,
light and a chase camera; in the game it adds no camera or light.

Screenshot (not committed): `./screenshot.sh modules/Guns/Godot/Main.tscn` (`Guns.png`).

## Tests (`Tests/`)

`GunsModuleTests` (with a fake bus): fire rate, muzzle position and bullet velocity, straight
travel and 1.5 s life, one `DamageTarget` per hit, none for misses or popped targets, heat,
overheat and cooldown, `GunState` every tick, no allocations with the pool reused.

Test it alone: `./test.sh Guns`.
