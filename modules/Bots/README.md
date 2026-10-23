# Bots

The AI pilots of every plane but the player's: Allies 1, 2, 3 (the player's wingmen) and
Central 4, 5, 6, 7 (see `Neo.Flight.Teams`). They fly with the stick, like the player
(pitch, roll, yaw, throttle, fire; never mouse aim), so the Flight module moves them by
the same rules. Simple and fun rather than perfect; easy difficulty for now.

- **Contract** (`Contract/`, namespace `Neo.Bots`): `Pilots` (static): `PlayerId` (0),
  `PlaneCount` (8), `bool IsBot(int planeId)` (true for 1..7). No messages of its own.
- **Uses**: subscribes to `Neo.Flight.PlaneState` (modules/Flight/Contract) and keeps the
  latest of each plane; publishes `Neo.Input.ControlInput` (modules/Input/Contract).
- **Core** (`Core/`): `BotsModule` (the `IModule`) owns one `BotPilot` per bot
  (`Pilot(id)`). Each tick, for each bot whose plane has been seen and is `Alive`, in id
  order, it publishes one `ControlInput` for that `PlaneId` (`HasAim` false). A downed or
  never-seen bot gets no input and its pilot forgets its target and break. No allocations
  per tick; deterministic (same states, same inputs: no random numbers, each bot's
  "personality" comes from a fixed hash of its id).
- No Godot view.

## Behaviour (`BotPilot`)

In priority order, each step:

1. **Ground**: if altitude plus 3 s of its current descent is below 80 m, climb at 30
   degrees on its heading.
2. **Edge**: within 600 m of an edge of the 4 km area (|x|, |z| <= 2000 m) and heading
   out, turn back toward the center (easing toward 400 m). Flight's own guard starts at
   400 m, so a bot normally turns first.
3. **Break**: an enemy within 300 m, in the 60 degree rear cone and pointing its nose at
   the bot (within 45 degrees), for longer than the bot's reaction time: hard bank (80
   degrees) toward the attacker's side, full pull once banked, full throttle, no fire. It
   lasts while chased and 1.2 s after.
4. **Attack**: the nearest alive plane of the other team. The aim point is the lead point
   (where the target will be when bullets arrive, at 450 m/s plus the bot's own speed),
   plus a slowly wandering aim error, seen with a lag (the reaction time). Far off the
   nose the bot banks so the aim point is "above" the cockpit and pulls; within 12 degrees
   it fine-aims with pitch and yaw. Fires in bursts (0.6 s on, 0.5 s off) only while the
   target is within 350 m and the aim point within 6 degrees of the nose. Throttle is its
   attack throttle, 0.6 when within 150 m behind the target (not to overshoot).
5. **Circle**: no enemy alive: circle the center at 700 m radius and 400 m altitude,
   throttle 0.6.

`Mode` and `TargetId` tell what it did on the last step.

## Tunables (constants in `BotPilot`)

| Constant | Value | What |
|---|---|---|
| `ReactionTime` ± `ReactionSpread` | 0.3 ± 0.1 s | reaction delay per bot: lag of its view of the aim point, and how long a chaser must sit there before it breaks |
| `AimError` ± `AimErrorSpread` | 1.5 ± 0.5 deg | wandering aim error per bot |
| `AttackThrottle` − up to `AttackThrottleSpread` | 1 − 0..0.2 | attack throttle per bot, so a team spreads out |
| `BulletSpeed` | 450 m/s | for the lead point (plus own speed) |
| `FireRange`, `FireCone` | 350 m, 6 deg | when to fire |
| `BurstLength`, `BurstPause` | 0.6 s, 0.5 s | bursts (each bot starts at a different point of the cycle) |
| `ChaseRange`, `ChaseRearCos`, `ChaserNoseCos` | 300 m, 60 deg, 45 deg | what counts as being chased |
| `BreakBank`, `BreakMinTime` | 80 deg, 1.2 s | the break turn |
| `MinAltitude`, `GroundLookahead`, `ClimbAngle` | 80 m, 3 s, 30 deg | ground avoidance |
| `EdgeMargin` | 600 m | turn back before the edge |
| `CircleRadius`, `CircleAltitude`, `CircleThrottle` | 700 m, 400 m, 0.6 | circling without a target |
| `FineAngle` | 12 deg | below it, fine aim instead of bank and pull |
| `CloseRange`, `CloseThrottle` | 150 m, 0.6 | ease off close behind a target |

Harder bots later: less `AimError` and `ReactionTime`.

## Tests

`Tests/BotsModuleTests.cs`, with a fake bus (`FakeBus`) and fake `PlaneState`s; closed-loop
tests fly the bots with `FakeFlight`, a small copy of Flight's stick model (rates,
smoothing, bank turn, throttle speed; no guards): inputs only for alive, seen bots (never
plane 0), in id order, in range, no aim; nearest enemy picked; banks toward a target on
either side and pulls to one above; flies onto a crossing target gradually; fires in
bursts only when lined up and in range; leads a crossing target; breaks hard (after its
reaction delay) when chased, not for an enemy far behind or flying away; climbs away from
the ground; turns back before the edge; circles near the center without a target; a full
90 s 4 v 4 stays above 40 m and inside the area and shoots; determinism; bots differ and
leave formation; no allocations per tick.

Test it alone: `./test.sh Bots`.
