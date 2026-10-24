# Guns

Every plane's twin machine guns: bullets, hits on the balloons and on enemy planes, heat and overheat.

## Messages

Publishes (Contract, namespace `Neo.Guns`):

| Message | When |
| --- | --- |
| `GunState` (int `PlaneId`, float `Heat` 0..1, bool `Overheated`, bool `Firing`, int `ShotsFired`, float `BoostSecondsLeft`) | every Tick, one per plane 0..7 in id order. `BoostSecondsLeft`: seconds left of the plane's balloon-pop damage boost, 0 when not boosted (and from the 5-argument constructor) |

Uses other modules' Contracts: reads `Neo.Input.ControlInput` (`Fire`) and
`Neo.Flight.PlaneState` (planes 0 to 7, `Teams`), `Neo.Targets.TargetState` (ids 0 to 31) and
`Neo.Targets.TargetDestroyed` (`ShooterPlaneId`, for the boost);
publishes `Neo.Targets.DamageTarget(TargetId, Amount, ShooterPlaneId)` (Amount 1, boosted 1 or 2)
for each bullet that hits a balloon and
`Neo.Damage.PlaneHit(TargetPlaneId, ShooterPlaneId, Part, Amount)` (0.04, boosted 0.06) for each
bullet that hits a plane; `ShooterPlaneId` is always the bullet's shooter.

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

Balloon-pop boost: a `TargetDestroyed` whose `ShooterPlaneId` is 0..7 boosts that plane (only)
for 60 s (`BoostSeconds`, counted down by ticks); a new pop restarts it at 60 s, it never
stacks above +50% or past 60 s; other ids (-1) boost nobody. A bullet is boosted when fired
while its plane's boost runs (`Bullet.Boosted`, `Boosted(plane)`); bullets already in flight
at the pop stay unboosted, boosted ones stay boosted after the boost ends. A boosted bullet's
`PlaneHit` amount is 1.5x (`BoostFactor`: 0.06). Its balloon damage is 1 x 1.5 rounded down
with the 0.5 left over carried to that plane's next boosted balloon hit, so its boosted hits
go 1, 2, 1, 2 (exactly 1.5 on average, never below 1). While a plane is boosted its guns also
heat up 25% more slowly while firing (`BoostHeatFactor` 0.75: 3/16 per second, overheated
after about 5.3 s of continuous fire instead of 4 s); cooling, the overheat threshold and the
0.3 recovery are the same as unboosted.

Heat rises 1/4 per second while firing (overheated at 1, after 4 s of continuous fire) and
falls 1/2 per second otherwise (2 s from full). Overheated guns stop, trigger held or not,
until heat is below 0.3. A downed plane does not fire and cools.

For the view: `Bullets` (the fixed pool of 256 for all planes, `Bullet` with `Position`,
`PreviousPosition`, `Velocity`, `Age`, `Serial` (per plane), `Gun`, `Shooter`, `Boosted`, `Alive`),
`MuzzlePosition(plane, gun)`, `LastVolleyTick(plane)`, `HasPlane(plane)`, `Hits`,
`HitPosition(n)` and `HitPlane(n)` (the plane hit, -1 for a balloon; last 32 kept), `State`
(plane 0) and `StateOf(plane)`. No allocations per Tick or per message.

## Godot (`Godot/Main.tscn`, `GunsView.cs`)

Draws a glowing yellow-orange tracer streak (bright core, see-through orange halo) for
every third bullet of every plane, interpolated between fixed ticks; a small outlined muzzle
flash at each gun of each plane on its volleys; a cartoon spark puff (outlined smoke ball and
flying yellow bits) where a bullet hits, smaller on a plane than on a balloon. A boosted
bullet's tracer looks a little hotter: a whiter, brighter core and a redder glow, 35% wider and
15% longer. No sound yet. Opened on its own (no module bound, as `screenshot.sh` does) it
runs a preview with its own Core: a stand-in biplane (boosted, as if it had just popped a
balloon) firing at a balloon and a wingman (not boosted) firing at an enemy plane ahead, sky,
light and a chase camera; in the game it adds no camera or light.

Screenshot (not committed): `./screenshot.sh modules/Guns/Godot/Main.tscn` (`Guns.png`).

## Tests (`Tests/`)

`GunsModuleTests` (with a fake bus): fire rate, muzzle position and bullet velocity, straight
travel and 1.5 s life, one `DamageTarget` per hit with the shooter's id, none for misses or popped targets, heat,
overheat and cooldown, `GunState` for every plane every tick, two planes firing with their own
trigger and heat, `PlaneHit` with the right part (engine, wings, tail, in the plane's frame)
for hits on enemy planes from either team, no friendly fire, downed planes neither firing nor
hit, a balloon in front of a plane taking the bullet, the balloon-pop boost (only the popping
plane, `GunState.BoostSecondsLeft`, exactly 60 s, restarted by a new pop and not stacking,
0.06 `PlaneHit`s, boosted balloon hits 1, 2, 1, 2, boosted heat rising at 0.75x next to an
unboosted plane at 1x and overheating at about 5.3 s, boosted cooling and recovery unchanged), the pool reused with all 8 planes firing
(half of them boosted until mid-run).

Test it alone: `./test.sh Guns`.
