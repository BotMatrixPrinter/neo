# neo

A game in Godot 4 with C# (the game itself is not decided yet). This holds the
structure that every feature is built on, made for small, isolated jobs and good
runtime performance.

## Open it

- Install **Godot .NET 4.7.2** (the .NET/mono build, not the standard one) and the **.NET 10 SDK**
  (neo targets `net10.0`, .NET 10 LTS); Godot needs it installed to open and build the project.
- In Godot: Import → `src/Neo.Game/project.godot`. Build (hammer icon), then run.
- Or from a shell: `dotnet build src/Neo.Game` then `godot --path src/Neo.Game`.

## Layout

```
Neo.sln
src/Neo.Core/            class library: game rules, state, simulation. No Godot.
  Core/<Feature>/        one folder per feature: I<Feature>.cs interface + README.md
src/Neo.Game/            Godot project (project.godot, Neo.Game.csproj on Godot.NET.Sdk 4.7.2)
  Main.tscn              main scene; instances each feature's scene
  Features/<Feature>/    that feature's scene and scripts (draw + input only)
tests/Neo.Core.Tests/    xUnit tests of Neo.Core
  <Feature>/             one folder per feature
test.sh                  the test command (hub.json)
```

The example feature is `Clock` (a fixed-timestep clock): `src/Neo.Core/Core/Clock/`,
`tests/Neo.Core.Tests/Clock/`, `src/Neo.Game/Features/Clock/`.

## Tests

`./test.sh` restores offline (packages only from `$NUGET_PACKAGES`; see `nuget.config`),
runs the Core tests, builds the Godot project (and checks it targets `net10.0`), and loads the main scene in headless
Godot. It fails on any failing test, C# compile error, or scene/script load error.

## Rules for every job

- **Core never references Godot; Game holds no rules.** Neo.Core has no Godot
  reference. Godot scripts only draw and pass input to Core.
- **A feature lives in its own folder** in Core (`Core/<Feature>/` with its interface
  file and a README), its tests in `tests/Neo.Core.Tests/<Feature>/`, and its Godot
  side in its own scene and scripts under `src/Neo.Game/Features/<Feature>/`.
  Features use each other only through those interface files.
- **One job changes one feature** (plus at most its Godot scene), and takes under an hour.
- **Performance:** a fixed-timestep simulation in Core; plain structs and arrays; no
  allocations, LINQ or boxing in per-frame code; Godot nodes only render.
- **Tests stay fast:** Core logic is tested without Godot; the Godot check only loads
  the project.
- All projects target `net10.0` (.NET 10 SDK, pinned in `global.json`). Package
  versions are pinned to the offline cache (Godot.NET.Sdk 4.7.2, xunit 2.9.3,
  xunit.runner.visualstudio 4.0.1, Microsoft.NET.Test.Sdk 18.11.0); jobs have no network.
