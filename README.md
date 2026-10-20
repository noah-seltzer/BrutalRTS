# BrutalRTS

A small real-time strategy game in C# (.NET 10) built on the **BRUTAL** framework.

## Architecture

```
            +-------------------+
            |     RTS.Core      |  contracts only, no dependencies
            | Vec2, Color, Key, |  IRenderer2D, InputState, IGameApp
            +---------+---------+
                      |
        +-------------+--------------+
        |                            |
+-------v--------+          +--------v---------+
|    RTS.Sim     |<---------+    RTS.Client     |
| deterministic  |          | camera, UI, input |
| simulation +AI |          | mapping; renderer |
+----------------+          | agnostic          |
                            +--------+----------+
                                     | IGameApp (RTS.Client.GameClient)
                 +-------------------+--------------------+
                 |                                        |
       +---------v-----------+                 +----------v-----------+
       |      RTS.Game       |                 |  tools/RTS.Snapshot  |
       | BRUTAL platform:    |                 |  headless driver,    |
       | GLFW window, GPU +  |                 |  scripted input,     |
       | ImGui IRenderer2D,  |                 |  PNG output via      |
       | splash, credits     |                 |  tools/RTS.SoftRender|
       +---------------------+                 +----------------------+
```

| Project | Path | Role |
|---|---|---|
| RTS.Core | `src/RTS.Core` | Shared contracts (`Contracts.cs`): math/color types, `IRenderer2D`, `InputState`, `IGameApp`. No dependencies. |
| RTS.Sim | `src/RTS.Sim` | Deterministic, fixed-step simulation (units, buildings, economy, combat) and AI. No rendering, no platform code. |
| RTS.Client | `src/RTS.Client` | `GameClient : IGameApp` - menus, camera, selection, commands, HUD. Draws only through `IRenderer2D`, so it runs under any renderer. |
| RTS.Game | `src/RTS.Game` | BRUTAL platform layer: GLFW window, GPU context + Dear ImGui draw-list implementation of `IRenderer2D`, input mapping, main loop, BRUTAL splash and credits. The only project that references BRUTAL packages. |
| RTS.SoftRender | `tools/RTS.SoftRender` | CPU `SoftwareRenderer : IRenderer2D` (alpha-blended RGBA framebuffer, built-in 5x7 bitmap font) + dependency-free `PngWriter`. Usable as a fallback renderer. |
| RTS.Snapshot | `tools/RTS.Snapshot` | Headless tool: drives an `IGameApp` for N frames at dt=1/60 with scripted input and writes PNG snapshots. |
| RTS.Sim.Tests | `tests/RTS.Sim.Tests` | Unit tests for the simulation (determinism, pathing, combat, AI). |

Frame loop (platform or snapshot tool): build `InputState` -> `app.Update(input, dt)` -> `app.Draw(renderer)` -> present / save.

## Build, run, test

Requires the .NET 10 SDK.

```powershell
dotnet build BrutalRTS.slnx                 # everything
dotnet run --project src/RTS.Game           # play (opens a window)
dotnet test tests/RTS.Sim.Tests             # simulation tests
```

`RTS.Game` and `RTS.Snapshot` both accept `-p:NoClient=true` to build without `RTS.Client`
(the game then runs a built-in demo app; the snapshot tool supports only `--app demo`).

### Headless snapshots

```powershell
dotnet build tools/RTS.Snapshot
dotnet run --project tools/RTS.Snapshot --no-build -- --app client --script menu  --frames 120 --out snapshots
dotnet run --project tools/RTS.Snapshot --no-build -- --app client --script start --frames 300 --out snapshots
dotnet run --project tools/RTS.Snapshot --no-build -- --app client --script demo  --frames 300 --out snapshots
dotnet run --project tools/RTS.Snapshot --no-build -- --app client --script queue --frames 300 --out snapshots
dotnet run --project tools/RTS.Snapshot --no-build -- --app client --script stats --frames 130 --out snapshots
dotnet run --project tools/RTS.Snapshot --no-build -- --app demo --frames 60 --out snapshots   # renderer self-test
```

Options: `--frames N`, `--out DIR`, `--script menu|start|demo|queue|stats`, `--app client|demo`,
`--every K` (default N/5; the last frame is always written), `--at 10,50,90`,
`--width W --height H` (default 1280x720), `--prefix P`.

Scripts:
- `menu` - no input.
- `start` - press Enter on frame 10, then idle.
- `demo` - `start`, box-select around screen center (frames 60-80), right-click a move order (frame 100),
  pan with Right (130-165) and Up (170-200) arrows, scroll-zoom in (220, 230) and out (245).
- `queue` - `start`, box-select one worker, then with Shift held: three right-click moves (frames 100-130)
  and three depot placements (D, 150-200), Esc at 210; shows the queued waypoint chain and build ghosts.
- `stats` - `start`, box-select one worker, then hover the Turret build button (from frame 100); shows armor,
  attributes and bonus damage in the info panel and command-card tooltip.

## BRUTAL license obligations

BRUTAL is distributed under the BRUTAL License v1.0, a modified MIT license. In addition to the
usual MIT terms, any product built on it must:

1. **Credit the BRUTAL copyright** in the product's credits (see `src/RTS.Game/Credits.cs`).
2. **Show the BRUTAL logo before gameplay** (splash screen at startup, see `src/RTS.Game/SplashScreen.cs`).
3. **Ship `NOTICE.txt`** alongside the product (`src/RTS.Game/NOTICE.txt`, copied to the build/publish output
   together with `LICENSES/*.txt`).

Do not remove the splash, the credits entry, or the NOTICE file from distributed builds.
