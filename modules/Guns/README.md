# Guns

Every plane's twin machine guns: bullets, hits on the balloons and on enemy planes, heat and overheat.

## Messages

Publishes (Contract, namespace `Neo.Guns`):

| Message | When |
| --- | --- |
| `GunState` (int `PlaneId`, float `Heat` 0..1, bool `Overheated`, bool `Firing`, int `ShotsFired`) | every Tick, one per plane 0..7 in id order |

Uses other modules' Contracts: reads `Neo.Input.ControlInput` (`Fire`) and
`Neo.Flight.PlaneState` (planes 0 to 7, `Teams`) and `Neo.Targets.TargetState` (ids 0 to 31);
publishes `Neo.Targets.DamageTarget(TargetId, 1)` for each bullet that hits a balloon and
`Neo.Damage.PlaneHit(TargetPlaneId, ShooterPlaneId, Part, 0.04)` for each bullet that hits a
plane.

## Core (`Core/`)

`GunsModule` (the `IModule`). Each plane 0..7 has its own guns, trigger and heat. While its
`Fire` is held, its `PlaneState` has been received with `Alive` true and its guns are not
overheated, each of its two guns (muzzles 0.5 m left and right of the center
line, 2 m ahead of the plane's position, in body space) fires 10 rounds per second; the first
volley comes on the tick the trigger is pressed. A bullet starts with the plane's velocity
plus 450 m/s along the nose (-Z of `Orientation`), flies straight and lives 1.5 s (90 ticks).
A bullet remembers its shooter. Each tick every live bullet's segment (from where it was to
where it is) is tested against every alive `TargetState` sphere and the 4 m sphere around
every alive plane of the other team (`Teams.IsAllies`; no friendly fire: bullets pass through
teammates; downed planes are not hit); the first sphere crossed removes the bullet. A balloon
gets one `DamageTarget(id, 1)`; a plane one `PlaneHit` whose part comes from the hit point in
the plane's own frame: x < -1 m left wing, x > 1 m right wing, z > 2 m (behind) tail,
otherwise engine (`PartAt`).

Heat rises 1/4 per second while firing (overheated at 1, after 4 s of continuous fire) and
falls 1/2 per second otherwise (2 s from full). Overheated guns stop, trigger held or not,
until heat is below 0.3. A downed plane does not fire and cools.

For the view: `Bullets` (the fixed pool of 256 for all planes, `Bullet` with `Position`,
`PreviousPosition`, `Velocity`, `Age`, `Serial` (per plane), `Gun`, `Shooter`, `Alive`),
`MuzzlePosition(plane, gun)`, `LastVolleyTick(plane)`, `HasPlane(plane)`, `Hits`,
`HitPosition(n)` and `HitPlane(n)` (the plane hit, -1 for a balloon; last 32 kept), `State`
(plane 0) and `StateOf(plane)`. No allocations per Tick or per message.

## Godot (`Godot/Main.tscn`, `GunsView.cs`)

Draws a glowing yellow-orange tracer streak (bright core, see-through orange halo) for
every third bullet of every plane, interpolated between fixed ticks; a small outlined muzzle
flash at each gun of each plane on its volleys; a cartoon spark puff (outlined smoke ball and
flying yellow bits) where a bullet hits, smaller on a plane than on a balloon. No sound yet. Opened on its own (no module bound, as `screenshot.sh` does) it
runs a preview with its own Core: a stand-in biplane firing at a balloon and a wingman
firing at an enemy plane ahead, sky,
light and a chase camera; in the game it adds no camera or light.

Screenshot (not committed): `./screenshot.sh modules/Guns/Godot/Main.tscn` (`Guns.png`).

## Tests (`Tests/`)

`GunsModuleTests` (with a fake bus): fire rate, muzzle position and bullet velocity, straight
travel and 1.5 s life, one `DamageTarget` per hit, none for misses or popped targets, heat,
overheat and cooldown, `GunState` for every plane every tick, two planes firing with their own
trigger and heat, `PlaneHit` with the right part (engine, wings, tail, in the plane's frame)
for hits on enemy planes from either team, no friendly fire, downed planes neither firing nor
hit, a balloon in front of a plane taking the bullet, no allocations with all 8 planes firing
and the pool reused.

Test it alone: `./test.sh Guns`.
