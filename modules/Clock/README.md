# Clock

The first module: counts fixed simulation steps.

- **Contract** (`Contract/`): `ClockTicked` (step number, simulated seconds), published
  on the bus once per fixed step. Other modules subscribe to it; they never see Core.
- **Core** (`Core/`): `ClockModule`, the module's entry class (`IModule`): counts steps and
  publishes `ClockTicked`; no allocations per tick. Also `FixedClock` (`IFixedClock`), a
  fixed-timestep accumulator: `Advance(elapsedSeconds)` returns how many fixed steps to run,
  at most `MaxStepsPerAdvance` per call (extra backlog is dropped so a slow frame cannot
  snowball), and `Alpha` is the fraction of the next step already elapsed.
- **Godot** (`Godot/Main.tscn`, `ClockView.cs`): draws "tick N" from its `ClockModule`.
- **Tests** (`Tests/`): `FixedClockTests`, `ClockModuleTests` (with a fake bus).

Test it alone: `./test.sh Clock`.
