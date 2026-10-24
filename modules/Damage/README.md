# Damage

Keeps the health of every plane's parts, downs planes and brings them back. No Godot view.

Plane ids and teams (the "Enemy Planes" wave): 0-3 Allies (0 is the player), 4-7 Central.
Damage does not care about teams: any hit counts, including friendly fire.

## Messages (Contract, namespace `Neo.Damage`)

| Message | Published by | Meaning |
|---|---|---|
| `PlaneHit` (int `TargetPlaneId`, int `ShooterPlaneId`, `PlanePart Part`, float `Amount`) | the guns, when a bullet hits a plane | lowers `Part` of the target by `Amount` (a bullet is about 0.04: about 25 hits empty a part). Ignored for a downed plane, an id outside 0..`DamageLimits.MaxPlanes`-1 (64), an unknown part, or `Amount` not > 0 (or NaN) |
| `PlaneDamageState` (int `PlaneId`, float `Engine`, `LeftWing`, `RightWing`, `Tail`, bool `Alive`) | Damage, every tick, for every known plane, in id order | each part's health 0..1 (1 = undamaged); `Get(PlanePart)` reads one part. `Alive` is false while down |
| `PlaneDestroyed` (int `PlaneId`, int `ShooterPlaneId`) | Damage, once, at the hit that downs a plane or at its crash | the plane went down; `ShooterPlaneId` fired that hit; for a crash, the last plane that hit it within 10 s, or -1 if it crashed on its own |
| `PlaneRespawn` (int `PlaneId`) | Damage, once, 5 s after `PlaneDestroyed` | the plane is back, all parts at 1 |
| `RepairState` (int `PlaneId`, bool `Repairing`, `PlanePart Part`, float `Progress`, int `PartsLeft`) | Damage, every tick, for every known plane, right after its `PlaneDamageState` | in-flight repair: `Repairing` is true while a repair cycle runs (Repair held, plane alive, a part below 1); then Flight should ignore the pilot's pitch/roll/yaw (throttle still works). `Part` is the part being fixed (meaningful only while `Repairing`), `Progress` the current cycle's 0..1 (0 when not repairing), `PartsLeft` the parts below 1, including the one being fixed |

`enum PlanePart { Engine, LeftWing, RightWing, Tail }`. `DamageLimits`: `MaxPlanes` (64),
`RespawnSeconds` (5), `RepairSeconds` (4), `CrashCreditSeconds` (10).

It also subscribes to `Neo.Flight.PlaneState` (modules/Flight/Contract), only to learn which
plane ids exist: a plane is known from its first `PlaneState` or its first `PlaneHit`, and
from then on its `PlaneDamageState` and `RepairState` are published every tick. And to
`Neo.Input.ControlInput` (modules/Input/Contract), only for `Repair` (the button is held). And to
`Neo.Flight.PlaneCrashed` (modules/Flight/Contract): a plane hit the ground.

## Rules (Core, `DamageModule`)

- Each part of a newly known plane starts at 1. A hit lowers its part by `Amount`, never
  below 0 (a remainder under 1e-5 counts as 0, so 25 hits of 0.04 empty a part).
- When any part reaches 0 the plane goes down: one `PlaneDestroyed`, `Alive` false, further
  hits ignored. Its parts keep their values while down.
- Crash: a `PlaneCrashed` for a plane alive in Damage's view (including one not known yet,
  which becomes known) downs it at once, like a downing hit: one `PlaneDestroyed`, `Alive`
  false, parts unchanged, respawn 5 s later. `ShooterPlaneId` is the last other plane whose hit
  landed on it within the last 10 s (`CrashCreditSeconds`, inclusive), so a pursuer who forced
  the crash gets the kill; otherwise -1 (crashed on its own, no kill). Self hits, hits ignored
  while down, and hits from before its last respawn do not count. A crash of a plane already
  down (shot down and falling, or crashed before) changes nothing, nor its respawn time.
- 5 s of ticks later (300 steps at 60 per second) every part is reset to 1, `Alive` true,
  and one `PlaneRespawn` is published, before that tick's states. It can then be downed again.
- In-flight repair: Repair counts as held on a tick when the plane's latest `ControlInput`
  since the previous tick had `Repair` true (no `ControlInput` = released). While it is held
  on an alive plane with any part below 1, a cycle fixes the most damaged part (lowest
  health; the first in `PlanePart` order on a tie): after 4 s (240 steps) of continuous
  holding that part is set to 1 and the next cycle starts at once on the next most damaged
  part, until none is left. So more damaged parts take longer (4 s each). Releasing cancels
  at once and loses the current cycle's progress completely (holding again starts that part
  from 0); parts already fixed stay fixed. Being downed cancels too (and nothing is repaired
  while down). Steering input does not cancel. Hits during a repair still land: the part
  being repaired can be damaged again and is set to 1 when its cycle completes.
  `GetRepair(planeId)` reads the current `RepairState`.
- Kills for the HUD: `DamageModule.Kills(shooterPlaneId)` counts planes each shooter
  downed (a plane downing itself is no kill). Other modules can also count `PlaneDestroyed`
  by `ShooterPlaneId`. `GetState(planeId)` and `IsKnown(planeId)` read the current state.
- No allocations per tick or per hit; deterministic.

## Tests

`Tests/DamageModuleTests.cs`, with a fake bus (`FakeBus`): hits lower only the
right part, published states carry each part, 25 bullets down a plane exactly once, no
damage while down, respawn after exactly 5 s at full health (once) and downing again,
kills per shooter (no self kill), crashes (destroy at once, also an unknown plane; credit to
the last hitter within 10 s, -1 after more than 10 s or with no hit, self hits and hits from
before a respawn not credited, no second destroy for a downed plane, respawn 5 s after a
crash and crashing again), repair (one part per 4 s cycle, most damaged first,
several parts take longer, release loses the current cycle and re-holding restarts it from 0,
no ControlInput counts as released, steering does not cancel, downed cancels, no repair when
undamaged, hits during a repair, per plane, `RepairState` order), states published every tick for every plane known from
`PlaneState` or a hit (all 8 planes), bad hits ignored, determinism, Core
targets net10.0 without Godot.

Test it alone: `./test.sh Damage`.
