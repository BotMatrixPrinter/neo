# Damage

Keeps the health of every plane's parts, downs planes and brings them back. No Godot view.

Plane ids and teams (the "Enemy Planes" wave): 0-3 Allies (0 is the player), 4-7 Central.
Damage does not care about teams: any hit counts, including friendly fire.

## Messages (Contract, namespace `Neo.Damage`)

| Message | Published by | Meaning |
|---|---|---|
| `PlaneHit` (int `TargetPlaneId`, int `ShooterPlaneId`, `PlanePart Part`, float `Amount`) | the guns, when a bullet hits a plane | lowers `Part` of the target by `Amount` (a bullet is about 0.04: about 25 hits empty a part). Ignored for a downed plane, an id outside 0..`DamageLimits.MaxPlanes`-1 (64), an unknown part, or `Amount` not > 0 (or NaN) |
| `PlaneDamageState` (int `PlaneId`, float `Engine`, `LeftWing`, `RightWing`, `Tail`, bool `Alive`) | Damage, every tick, for every known plane, in id order | each part's health 0..1 (1 = undamaged); `Get(PlanePart)` reads one part. `Alive` is false while down |
| `PlaneDestroyed` (int `PlaneId`, int `ShooterPlaneId`) | Damage, once, at the hit that downs a plane | the plane went down; `ShooterPlaneId` fired that hit |
| `PlaneRespawn` (int `PlaneId`) | Damage, once, 5 s after `PlaneDestroyed` | the plane is back, all parts at 1 |

`enum PlanePart { Engine, LeftWing, RightWing, Tail }`. `DamageLimits`: `MaxPlanes` (64),
`RespawnSeconds` (5).

It also subscribes to `Neo.Flight.PlaneState` (modules/Flight/Contract), only to learn which
plane ids exist: a plane is known from its first `PlaneState` or its first `PlaneHit`, and
from then on its `PlaneDamageState` is published every tick.

## Rules (Core, `DamageModule`)

- Each part of a newly known plane starts at 1. A hit lowers its part by `Amount`, never
  below 0 (a remainder under 1e-5 counts as 0, so 25 hits of 0.04 empty a part).
- When any part reaches 0 the plane goes down: one `PlaneDestroyed`, `Alive` false, further
  hits ignored. Its parts keep their values while down.
- 5 s of ticks later (300 steps at 60 per second) every part is reset to 1, `Alive` true,
  and one `PlaneRespawn` is published, before that tick's states. It can then be downed again.
- Kills for the HUD: `DamageModule.Kills(shooterPlaneId)` counts planes each shooter
  downed (a plane downing itself is no kill). Other modules can also count `PlaneDestroyed`
  by `ShooterPlaneId`. `GetState(planeId)` and `IsKnown(planeId)` read the current state.
- No allocations per tick or per hit; deterministic.

## Tests

`Tests/DamageModuleTests.cs`, with a fake bus (`FakeBus`, `QuietBus`): hits lower only the
right part, published states carry each part, 25 bullets down a plane exactly once, no
damage while down, respawn after exactly 5 s at full health (once) and downing again,
kills per shooter (no self kill), states published every tick for every plane known from
`PlaneState` or a hit (all 8 planes), bad hits ignored, determinism, no allocations, Core
targets net10.0 without Godot.

Test it alone: `./test.sh Damage`.
