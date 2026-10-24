# Bots

The AI pilots of every plane but the player's: Allies 1, 2, 3 (the player's wingmen) and
Central 4, 5, 6, 7 (see `Neo.Flight.Teams`). They fly with the stick, like the player
(pitch, roll, yaw, throttle, fire; never mouse aim), so the Flight module moves them by
the same rules. Simple and fun rather than perfect; easy difficulty for now.

- **Contract** (`Contract/`, namespace `Neo.Bots`): `Pilots` (static): `PlayerId` (0),
  `PlaneCount` (8), `bool IsBot(int planeId)` (true for 1..7). No messages of its own.
- **Uses**: subscribes to `Neo.Flight.PlaneState` (modules/Flight/Contract) and keeps the
  latest of each plane; publishes `Neo.Input.ControlInput` (modules/Input/Contract); reads
  the ground's height from `Neo.World.Terrain.HeightAt` (modules/World/Contract).
- **Core** (`Core/`): `BotsModule` (the `IModule`) owns one `BotPilot` per bot
  (`Pilot(id)`). Each tick, for each bot whose plane has been seen and is `Alive`, in id
  order, it publishes one `ControlInput` for that `PlaneId` (`HasAim` false). A downed or
  never-seen bot gets no input and its pilot forgets its target and evasion. No allocations
  per tick; deterministic (same states, same inputs: no random numbers; each bot's
  "personality" and every "random" choice, such as its evasion moves and jinks, comes from a
  fixed hash of its id and a counter).
- No Godot view.

## Behaviour (`BotPilot`)

In priority order, each step:

1. **Ground**: every safety height is measured from the terrain (`Terrain.HeightAt`), not
   from y = 0. It looks under itself and along its path every second over the next 5 s
   (`TerrainLookahead`); if its height above any of those points, less 3 s of its current
   descent, is below 80 m, it climbs at 30 degrees on its heading. When the terrain ahead
   needs a climb steeper than 20 degrees (`TurnAwayAngle`: a hill or mountain too close to
   climb over), it turns away as well (a climbing turn 90 degrees off its path, to the side
   whose terrain is lower, kept until clear).
2. **Edge**: within 600 m of an edge of the 4 km area (|x|, |z| <= 2000 m) and heading
   out, turn back toward the center (easing toward 400 m). Flight's own guard starts at
   400 m, so a bot normally turns first.
3. **Evade**: an enemy within 350 m, in the 60 degree rear cone and pointing its nose at
   the bot (within 45 degrees), for longer than the bot's evasion reaction (its reaction time
   × 1.25 with `EvasionSkill` 0.75) starts an evasion (full throttle, no fire). Its evasive
   stick inputs are softened by `EvasionSkill` (see the table). Each evasion picks one move, from a fixed hash of the bot and
   how many evasions it has had (so it varies per bot and per encounter):
   - **Break** (`BotMode.Break`): hard bank (80 degrees × `EvasionSkill`: 60) toward the attacker's side (to spoil
     its aim; dead astern, the bot's habit, alternating), full pull once banked. The bank
     grows when the nose rises (back to the full 80 once it is 1 degree above), so the turn is
     held level or slightly down (-3 degrees), never a climbing spiral.
   - **Extend** (`Extend`): nose down 30 degrees, wings level, until 56 m/s (and below
     800 m), 3 s, or 180 m; then a level run, or a 60 degree turn toward the attacker's side.
   - **Split-S** (`SplitS`, above 500 m): roll inverted, pull through, level out the other
     way.
   - **Roll-away** (`RollAway`, above 500 m): steep descending spiral turn (nose -35 degrees).

   Choice (its altitude for the 500 m and 280 m rooms below is its height above the highest
   terrain under it and over its next 5 s of flight; the 800 m is from y = 0, for the ceiling):
   above 800 m (near Flight's 1000 m ceiling) always a dive (split-S, roll-away or
   extend); below 40 m/s a dive (extend, or roll-away above 500 m), or a break with the nose
   10 degrees down (level below 180 m) when below 280 m; otherwise a break about half the time (0.5 ± 0.15 per
   bot), else an extension, sometimes a split-S or roll-away when high enough. Dives level
   off 100 m above the 80 m safety height over the highest terrain under it and ahead, so
   they stop with room to pull out above it. While shot at (attacker within 350 m, nose within
   10 degrees of the bot; not during a split-S) it **jinks**: random pitch (±0.35 × 0.75) and
   roll (±0.5 × 0.75) offsets, new every (0.3..0.6 s) × 1.25 (`Jinking`).

   An evasion ends after its time ((4 ± 1 s) × 0.75 per bot), once the pursuer is shaken off (beyond
   450 m, or not pointing at the bot, for 1 s), or when it **overshoots** (ahead of the bot's
   wing line within 500 m, for the reaction time). After an overshoot the bot turns on it:
   it attacks the overshooter for 3 s even if another enemy is nearer. After any evasion it
   attacks for at least 2 s × 1.25 before it evades again. `ChaserId` is the pursuer while evading.
4. **Attack**: the nearest alive plane of the other team. The aim point is the lead point
   (where the target will be when bullets arrive, at 450 m/s plus the bot's own speed),
   plus a slowly wandering aim error, seen with a lag (the reaction time). Far off the
   nose the bot banks so the aim point is "above" the cockpit and pulls; within 12 degrees
   it fine-aims with pitch and yaw. Fires in bursts (0.6 s on, 0.5 s off) only while the
   target is within 350 m and the aim point within 6 degrees of the nose. Throttle is its
   attack throttle, 0.6 when within 150 m behind the target (not to overshoot).
5. **Circle**: no enemy alive: circle the center at 700 m radius and 400 m altitude,
   throttle 0.6.

**No slow climbs** (every step but Ground): the steepest climb allowed is 2 degrees per m/s
above 40 m/s (none at 40, a dive of up to 10 degrees below), and none above 800 m. Aim
points above that are brought down; a nose above it is pushed down (wings near level) or
banked past 90 degrees so the pull brings it down. A target far behind is turned toward in a
level turn, not by a loop.

`Mode` and `TargetId` tell what it did on the last step.

## Tunables (constants in `BotPilot`)

| Constant | Value | What |
|---|---|---|
| `EvasionSkill` | 0.75 | how good the bots are at evading (1.0 = as before; 0.75 since 2026-10-24: easier to shoot down). Evasion only: its reaction (before it starts) and `EvadeCooldown` × (2 − skill); `EvadeTime`, the evasive stick (break bank, pull and roll, extension push (wings within 90 degrees of level), split-S roll and pull, roll-away), `JinkPitch` and `JinkRoll` × skill; `JinkPeriod` × (2 − skill). The anti-climb part of a turn stays full. Aim, fire, `ReactionTime` for aiming, `BreakShare` and move choice unchanged. `new BotsModule(skill)` / `new BotPilot(id, skill)` for another value |
| `ReactionTime` ± `ReactionSpread` | 0.3 ± 0.1 s | reaction delay per bot: lag of its view of the aim point, and (× (2 − `EvasionSkill`)) how long a chaser must sit there before it breaks (`EvadeReaction`) |
| `AimError` ± `AimErrorSpread` | 1.5 ± 0.5 deg | wandering aim error per bot |
| `AttackThrottle` − up to `AttackThrottleSpread` | 1 − 0..0.2 | attack throttle per bot, so a team spreads out |
| `BulletSpeed` | 450 m/s | for the lead point (plus own speed) |
| `FireRange`, `FireCone` | 350 m, 6 deg | when to fire |
| `BurstLength`, `BurstPause` | 0.6 s, 0.5 s | bursts (each bot starts at a different point of the cycle) |
| `ChaseRange`, `ChaseRearCos`, `ChaserNoseCos` | 350 m, 60 deg, 45 deg | what counts as being chased |
| `EvadeTime` ± `EvadeTimeSpread`, `EvadeCooldown` | 4 ± 1 s, 2 s | longest evasion per bot (× `EvasionSkill`: `EvadeLimit`); attacking time before the next (× (2 − `EvasionSkill`): `Cooldown`) |
| `ShakeRange`, `ShakenTime` | 450 m, 1 s | pursuer shaken off |
| `OvershootRange`, `ReverseTime` | 500 m, 3 s | overshoot; how long it then attacks the overshooter |
| `BreakShare` ± `BreakShareSpread` | 0.5 ± 0.15 | share of breaks at mid altitude and good speed, per bot |
| `BreakBank`, `BreakElevation` | 80 deg, -3 deg | the break turn |
| `MinClimbSpeed`, `SlowDiveAngle` | 40 m/s, 10 deg | no climbing below it; dive angle when slow |
| `HighAltitude`, `SplitSAltitude`, `DiveRoomAltitude`, `DiveFloorMargin` | 800, 500, 280, 100 m | dive when high (from y = 0); room for a split-S/roll-away or an extension, and dives level off this far above `MinAltitude` (all above the terrain under it and ahead) |
| `ExtendDiveAngle`, `ExtendSpeed`, `ExtendDiveTime` | 30 deg, 56 m/s, 3 s | diving extension |
| `RollAwayElevation` | -35 deg | roll-away dive |
| `JinkCone`, `JinkPeriod`, `JinkPitch`, `JinkRoll` | 10 deg, 0.45 s (×0.7..1.3), 0.35, 0.5 | jinks while shot at |
| `MinAltitude`, `GroundLookahead`, `ClimbAngle` | 80 m, 3 s, 30 deg | ground avoidance: height above the terrain, seconds of descent counted, climb angle |
| `TerrainLookahead`, `TurnAwayAngle` | 5 s, 20 deg | terrain checked every second this far along its path; terrain ahead needing a steeper climb: turn away too |
| `EdgeMargin` | 600 m | turn back before the edge |
| `CircleRadius`, `CircleAltitude`, `CircleThrottle` | 700 m, 400 m, 0.6 | circling without a target |
| `FineAngle` | 12 deg | below it, fine aim instead of bank and pull |
| `CloseRange`, `CloseThrottle` | 150 m, 0.6 | ease off close behind a target |

Harder bots later: less `AimError` and `ReactionTime`, more `EvasionSkill`.

## Tests

`Tests/BotsModuleTests.cs`, with a fake bus (`FakeBus`) and fake `PlaneState`s; closed-loop
tests fly the bots with `FakeFlight`, a small copy of Flight's stick model (rates,
smoothing, bank turn, throttle speed, climbing costs speed and diving gains it, a hard
1000 m ceiling; no floor or border guards): inputs only for alive, seen bots (never
plane 0), in id order, in range, no aim; nearest enemy picked; banks toward a target on
either side and pulls to one above; flies onto a crossing target gradually; fires in
bursts only when lined up and in range; leads a crossing target; evades (after its reaction
delay) when chased, not for an enemy far behind or flying away; breaks toward the
attacker's side; varies its moves per bot and encounter; dives when slow or high (and, low
and slow, breaks without climbing); with a pursuer glued to its tail (from mid altitude,
high, near the ceiling, low, slow) never climbs more than 4 s on end nor while below 40 m/s,
stays 80 m above the terrain and inside the area; evasions end within their time limit, then it attacks
(also once shaken off); turns on a pursuer that overshoots; jinks while shot at; with `EvasionSkill` 0.75 against 1.0 it
evades later, pulls and banks less, jinks less and smaller and evades for less time, picks the
same moves and attacks with the very same stick; the chased
flight is deterministic; climbs away from
the ground; heading level at 100 m at each of the three mountains, it reacts (climbs or turns)
while still 80 m above the ground and never gets within 40 m of it; flying level at 90 m over
rolling hills it keeps about 80 m (over 75) above them; chased toward each mountain, every bot dives, its
dives stay 130 m above the ground and it stays 80 m above it; turns back before the edge; circles near the center without a target; a full
90 s 4 v 4 stays 40 m above the terrain and inside the area and shoots; determinism; bots differ and
leave formation; the host can create the module (parameterless constructor); no allocations
per tick.

Test it alone: `./test.sh Bots`.
