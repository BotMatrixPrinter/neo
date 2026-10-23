# neo

A Godot 4.7.2 / .NET 10 C# game: cartoon-style WWI arcade aerial combat, 4v4, with a
tug-of-war front line. It is built from independent modules so that many jobs can work at
once, each on one module, and one module breaking never breaks another.

## Open it

- Install **Godot .NET 4.7.2** (the .NET/mono build, not the standard one) and the **.NET 10 SDK**
  (neo targets `net10.0`, .NET 10 LTS); Godot needs it installed to open and build the project.
- In Godot: Import → `project.godot` at the repo root. Build (hammer icon), then run.
- Or from a shell: `dotnet build Neo.csproj` then `godot --path .`.

## Layout

```
project.godot, Neo.csproj   the Godot project (the shell's); main scene shell/Godot/Main.tscn
shell/                      the shell: whole-repo jobs only, changed rarely
  Contract/                 Neo.Shell.Contract: IModule, TickContext, IModuleView, IMessageBus, MessageBus
  Host/                     Neo.Shell.Host: ModuleHost (finds, starts and ticks modules, each guarded). No Godot.
  Godot/                    Bootstrap.cs + Main.tscn: runs the ModuleHost inside Godot
tests/Neo.Shell.Tests/      xUnit tests of the shell (bus, host, guard)
modules/<Name>/             one module per folder, e.g. modules/Clock/
  Contract/                 Neo.<Name>.Contract.csproj: its public interfaces, messages, data
  Core/                     Neo.<Name>.Core.csproj: its rules and state; one IModule class. No Godot.
  Godot/                    Main.tscn (loaded by the shell if present) and its scripts: draw, input, sound
  Tests/                    Neo.<Name>.Tests.csproj: xUnit tests of its Core
  README.md                 what it does, what its Contract offers
test.sh                     the test command (hub.json "test" and "module_test")
```

`Neo.csproj` picks up every `modules/*/Contract` and `modules/*/Core` project and every
`modules/*/Godot/**/*.cs` script by wildcard; nothing lists modules by name. At build time
it records each `modules/<Name>/Core/*.csproj` assembly with its folder name; at startup
`Bootstrap` loads each, creates its one `IModule` class, calls `Start(bus)`, loads
`res://modules/<Name>/Godot/Main.tscn` if it exists (its root may implement `IModuleView`
to receive its own module), then calls every module's `Tick` on Godot's fixed physics
step (60 per second).

## Tests

- `./test.sh` runs the shell's tests and every module's tests, builds the Godot project
  (and checks it targets `net10.0`), and loads the main scene headless for a few fixed
  steps. It fails on any failing test, compile error, scene/script load error, a module not
  found at startup, or any module the shell disabled at runtime.
- `./test.sh <Name>` (hub.json `module_test`) runs only module `<Name>`'s tests, plus the
  tests of every module whose projects reference `modules/<Name>/Contract/`, then the same
  Godot build and headless load; at runtime it fails only if `<Name>` itself was not
  started or was disabled.

Restores are offline (packages only from `$NUGET_PACKAGES`; see `nuget.config`).

## Rules for every job

- **Shell vs modules.** The shell (`project.godot`, `Neo.csproj`, `shell/`, `tests/`,
  `test.sh`, `hub.json`, this README) is changed only by whole-repo jobs, rarely. Everything
  else is a module under `modules/<Name>/`.
- **One job per module.** A module job changes only its own folder `modules/<Name>/`, and
  is tested with `./test.sh <Name>`. Many module jobs run side by side; they share no files
  and no tests. A job takes under an hour.
- **Modules couple only through Contracts.** A module's Core references only Contract
  projects (the shell's and other modules' `Contract/`), never another module's Core or
  Godot. Modules talk through the shell's message bus: struct messages defined in the
  sender's Contract, published with `bus.Publish(msg)`, received with
  `bus.Subscribe<T>(handler)` in `Start`. Changing a Contract is a breaking change for the
  modules that use it (`./test.sh <Name>` runs their tests too): add to it, don't break it.
- **Core never references Godot; Godot holds no rules.** `modules/<Name>/Godot/` only
  draws, reads input and plays sound, and talks only to its own Core.
- **Per-module tests with fakes.** `modules/<Name>/Tests/` tests its own Core, with fakes
  of the Contracts it uses (e.g. a fake `IMessageBus`); never another module's Core.
- **Runtime guard.** The shell runs each module's construction, `Start`, `Tick` and
  message handlers inside its own guard: a module that throws is disabled and logged
  (`NEO_MODULE_DISABLED <Name>: ...`), its view is hidden, and the game goes on. A module
  that does not compile breaks the shared build, so it cannot pass its own
  `./test.sh <Name>` and never lands.
- **Performance:** a fixed-timestep simulation in Core (`Tick` is the fixed step); plain
  structs and arrays; no allocations, LINQ or boxing in per-frame code (`Tick`, message
  handlers, `_Process`); bus messages are structs and publishing does not allocate;
  Godot nodes only render.
- **Tests stay fast:** Core logic is tested without Godot; the Godot check only loads
  the project.
- All projects target `net10.0` (.NET 10 SDK, pinned in `global.json`). Package
  versions are pinned to the offline cache (Godot.NET.Sdk 4.7.2, xunit 2.9.3,
  xunit.runner.visualstudio 4.0.1, Microsoft.NET.Test.Sdk 18.11.0); jobs have no network.

## Add a module

Create `modules/<Name>/` and nothing else (copy `modules/Clock/` as a template):

1. `Contract/Neo.<Name>.Contract.csproj` (no references, or other Contracts only) with its
   message structs and interfaces.
2. `Core/Neo.<Name>.Core.csproj` referencing `../../../shell/Contract/Neo.Shell.Contract.csproj`,
   `../Contract/Neo.<Name>.Contract.csproj` and any other module's `Contract` it uses. In it,
   exactly one public class implementing `Neo.Shell.IModule` with a public parameterless
   constructor.
3. Optional `Godot/Main.tscn` with its scripts (namespace `Neo.<Name>`); its root node may
   implement `Neo.Shell.IModuleView` to get its module.
4. `Tests/Neo.<Name>.Tests.csproj` (xUnit, same package versions as Clock's) referencing
   its Core.
5. `README.md`.

Then `./test.sh <Name>`. The shell finds the new module by wildcard at build time and
starts it at runtime; no file outside `modules/<Name>/` changes.
