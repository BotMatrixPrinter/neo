# Clock

Fixed-timestep clock, the example feature. Interface: `IFixedClock.cs`.

- `Advance(elapsedSeconds)` returns how many fixed steps to simulate this frame.
- At most `MaxStepsPerAdvance` steps per call; extra backlog is dropped so a slow
  frame cannot snowball.
- `Alpha` is the fraction of the next step already elapsed, for interpolated drawing.
- No allocations; call it every frame.

Tests: `tests/Neo.Core.Tests/Clock/`. Godot side: `src/Neo.Game/Features/Clock/`.
