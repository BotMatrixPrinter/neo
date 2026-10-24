# Collisions

Planes that ram each other: damage to both, the faster one dealing more, and a bounce that
knocks them apart. Any planes collide, allies too.

## Messages

Publishes (Contract, namespace `Neo.Collisions`):

| Message | When |
| --- | --- |
| `CollisionImpulse` (int `PlaneId`, Vector3 `VelocityChange`, Vector3 `AngularKick`, Vector3 `Separation`) | once per plane per collision (two per collision) |

- `VelocityChange`: world m/s to add to the plane's velocity, along the contact normal, away
  from the other plane; the two of a collision are equal and opposite.
- `AngularKick`: body rates to add, rad/s, in the plane's own frame: x pitch, y yaw, z roll
  (right-handed about +X right, +Y up, +Z back: positive pitch raises the nose, positive yaw
  turns it left, positive roll raises the right wing).
- `Separation`: world offset, meters, that moves the plane just clear of the other (half the
  overlap each, plus 1 cm).

Nothing applies them yet: a Flight job is to apply them on its next step.

Also publishes `Neo.Damage.PlaneHit(TargetPlaneId, ShooterPlaneId, Part, Amount)` per plane
hit, with the other plane as the shooter (so ramming earns the kill). Reads
`Neo.Flight.PlaneState` (ids 0..7; others ignored).

## Rules (Core, `CollisionsModule`)

- Each tick every pair of planes whose latest `PlaneState` is `Alive` is tested as two
  spheres of 3.5 m (`PlaneRadius`): closer than 7 m they touch. A pair that touches now but
  did not on the previous tick is one collision; that pair does nothing more until it has
  separated (a pair with a downed plane never touches).
- **Damage**: closing speed v = the length of their relative velocity. The part damage two
  planes of equal speed each take is 0.00433 v + 0.0000667 v² (`DamagePerMps`,
  `DamagePerMpsSquared`): about 0.05 at 10 m/s, 0.19 at 30, 0.5 at 60, 1.1 at 100. The total
  (`TotalDamage`, twice that) is split by the other plane's speed: plane A takes
  total x speedB / (speedA + speedB) (`DamageTaken`, `PlaneState.Speed`), at most 1 (a whole
  part, `MaxPartDamage`). So the faster plane deals more and takes less. A share of 0 publishes
  no `PlaneHit`.
- **Part**: from the contact point (the midpoint of the two centers) in each plane's own
  frame, the guns' rule (`PartAt`): x < -1 m left wing, x > 1 m right wing, z > 2 m (behind)
  tail, otherwise engine.
- **Bounce**: the contact normal n runs from one center to the other. Equal masses: each plane
  changes by (1 + 0.3) / 2 x the closing speed along n (`Restitution` 0.3), so afterwards they
  move apart along n at 0.3 of the speed they closed at; nothing changes sideways.
- **Spin** (`AngularKickAt`, arcade, from the contact point in the plane's frame, scaled with
  the velocity change): struck on a wing, the struck wing is knocked away from the other plane
  (up when the other is level or below, down when above): 0.08 rad/s per m/s at the wingtip
  (`RollKickPerMps`), none inboard of 1 m. Struck toward the nose or tail, that end is knocked
  away up/down (pitch) and sideways (yaw), 0.06 rad/s per m/s at the end of the sphere
  (`PitchYawKickPerMps`); a push straight along the fuselage does not turn it. Each axis at
  most 5 rad/s (`MaxAngularKick`).
- For the view: `Collisions`, `CollisionPosition(n)`, `CollisionSpeed(n)`,
  `CollisionVelocity(n)` (the last 16 kept, `CollisionRing`), `Touching(a, b)`.
- Deterministic; no allocations per tick.

## Godot (`Godot/Main.tscn`, `CollisionsView.cs`)

A cartoon crunch at each contact point: a yellow flash, an outlined smoke puff and splinters of
wood and canvas flying out, tumbling and falling, drifting on with the planes; bigger for a
harder hit. Opened on its own (no module bound, as `screenshot.sh` does) it runs a preview with
its own Core: two stand-in biplanes clipping wingtips every 4 s, sky, light and a camera; in the
game it adds no camera or light.

Screenshot (not committed): `./screenshot.sh modules/Collisions/Godot/Main.tscn` (`Collisions.png`).

## Tests (`Tests/CollisionsModuleTests.cs`)

With a fake bus: impulses equal and opposite along the contact normal with restitution 0.3 and
a separation that leaves the spheres just apart; head-on bounce; a wing-struck plane kicked
into a roll away (level and from above, scaling with the impulse); nose and tail hits kick yaw
and pitch; the faster plane deals more than it takes (tail chase, head-on); damage grows faster
than closing speed (10 m/s small, 60 m/s about half, 100 m/s and up a whole part); one hit per
contact until separated; parts from the contact point in each plane's frame; downed planes
ignored; allies collide; apart planes and unknown ids do nothing; three planes in one spot;
determinism; one entry class, no Godot in Core.

Test it alone: `./test.sh Collisions`.
