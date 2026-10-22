# Nymeria.OMSI

An independent, cross-platform OMSI-compatible bus simulation built on the
[Nymeria](https://github.com/Rauberhohle-2-0/Nymeria) 3D engine.

> **Current state: Task 02 — map metadata discovery.** The host still runs the
> Task 01 bootstrap (window, engine GPU device, clear frames, clean shutdown). It
> can now also read one explicitly named map folder: `global.cfg` metadata plus
> the `tile_x_y.map` inventory, printed as a read-only summary. It does not render
> a map, load terrain, objects, textures, or vehicles, and it reads no OMSI asset.
> Nothing here claims OMSI compatibility, and real Grundorf compatibility is
> pending owner validation on Windows.

## What exists today

- A minimal host at [src/Nymeria.OMSI](src/Nymeria.OMSI) that consumes the engine's
  real public API: `Nymeria.Platform.Window` for the window and event loop and
  `Nymeria.Graphics.GpuDevice` for the backend and presentation claim.
- A read-only, GPU-free map metadata layer: `global.cfg` decoding and keyword
  parsing, `tile_x_y.map` discovery, a typed manifest, and source-aware
  diagnostics. See [docs/OMSI_MAP_PARSING.md](docs/OMSI_MAP_PARSING.md).
- A clear-only frame path with no shader assets and no shader compiler step.
- Readable startup diagnostics for missing native SDL3, architecture mismatches,
  missing GPU backends, and initialization failures.
- Headless unit tests for the host's command-line contract.
- The engine pinned as a Git submodule at one commit, with no engine source in this
  repository.
- Enforceable policies: [AGENTS.md](AGENTS.md),
  [GIT_GUIDELINES.md](GIT_GUIDELINES.md), architecture and development docs, a
  pull request template, a commit-attribution check, and CI for macOS and Windows.

## Requirements

- .NET SDK **10.0.402** (pinned by [global.json](global.json); the engine pins the
  same SDK), targeting `net10.0`.
- Git.
- No SDL3 installation, no DirectX Shader Compiler, and no shader toolchain. Native
  SDL3 arrives through NuGet with the engine's platform project.

## Clone and build

The engine is a pinned submodule, so a plain clone is not enough:

```bash
git clone --recurse-submodules https://github.com/Rauberhohle-2-0/Nymeria.OMSI.git
cd Nymeria.OMSI

dotnet restore Nymeria.OMSI.slnx
dotnet build Nymeria.OMSI.slnx --configuration Release --no-restore
dotnet test Nymeria.OMSI.slnx --configuration Release --no-build --no-restore
```

For an existing clone:

```bash
git submodule update --init --recursive
```

Verify the engine pin with `git submodule status` and
`git -C external/Nymeria rev-parse HEAD`. Details, including how to update the pin
deliberately, are in [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).

## Run on Windows (project owner)

Runtime validation happens on Windows with Direct3D 12 hardware. It is **not**
performed by the development agent and is **not** part of CI.

```bat
dotnet build Nymeria.OMSI.slnx --configuration Release
src\Nymeria.OMSI\bin\Release\net10.0\Nymeria.OMSI.exe --smoke
```

Expected: a 960 × 540 window appears for about two seconds with a dark clear color,
then the process exits with code 0 and prints:

```text
Nymeria.OMSI bootstrap
host: Microsoft Windows ... (<architecture>)
engine: direct3d12 driver, DXIL shaders, <swapchain format> swapchain
window: 960x540 logical
frames presented: 120
smoke run complete
shutdown: clean
```

Run without arguments for an interactive window; close it and expect
`window closed by the user` followed by `shutdown: clean`.

The full checklist, failure-reporting instructions, and troubleshooting are in
[docs/DEVELOPMENT.md](docs/DEVELOPMENT.md#windows-manual-smoke-test).

## Inspect a map folder (read-only)

Map inspection is a separate command path. It creates no window and no GPU device,
so it works on a machine that cannot present frames:

```bat
src\Nymeria.OMSI\bin\Release\net10.0\Nymeria.OMSI.exe --map "<path>\OMSI 2\maps\Grundorf"
```

The path is always supplied explicitly; no installation is searched for. The
summary reports the encoding that was used, the map name, the tile count with
coordinates, any `global.cfg` directives the parser preserved but did not
interpret, and source-aware diagnostics. `status: complete` exits 0,
`status: partial` exits 0 with the warnings shown, and `status: failed` exits 1.

The supported subset, its documented source, its limits, and the exact Windows
validation procedure are in [docs/OMSI_MAP_PARSING.md](docs/OMSI_MAP_PARSING.md).

## Platform status

| Platform | Build | Headless tests | Runtime (window, GPU, shutdown) |
| --- | --- | --- | --- |
| macOS (development host) | `PASS` — local, macOS 27.0.1 arm64, SDK 10.0.402 | `PASS` — local, 69 tests | `NOT RUN` — intentionally not executed on macOS |
| Windows (owner host) | `NOT RUN` — CI job defined, no run recorded yet | `NOT RUN` — CI job defined, no run recorded yet | `NOT RUN (owner validation pending)` |
| Linux | Not supported | — | Not supported; the engine has no Linux GPU backend |

Map metadata against synthetic fixtures runs headlessly in the unit tests. Map
metadata against a real Grundorf installation is `NOT RUN (owner validation
pending)`: no OMSI files exist on the development machine, and no substitute files
were created to simulate them.

The engine itself documents Metal/MSL as validated on macOS and Direct3D 12/DXIL as
implemented but unverified on Windows hardware. This repository inherits that gap;
the owner's Windows run is what closes it.

## Engine dependency

Nymeria publishes no NuGet packages, so the engine is consumed as a Git submodule
pinned to commit `6e727248627692a663b0651f09be2c662ed14681` (the `main` tip at the
time of Task 01), referenced by MSBuild project references to `Nymeria.Platform` and
`Nymeria.Graphics`. The engine stays independently buildable and is read-only from
here. The decision, the alternatives that were rejected, and the upgrade procedure
are recorded in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Documentation

- [docs/OMSI_MAP_PARSING.md](docs/OMSI_MAP_PARSING.md) — supported map metadata,
  encoding strategy, reference and provenance, limits, Windows validation.
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — ownership boundary, integration
  decision, host lifecycle, build graph, platform backends, limitations.
- [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) — SDK setup, submodule commands, macOS
  verification scope, troubleshooting, Windows smoke test.
- [AGENTS.md](AGENTS.md) — binding rules for coding agents.
- [GIT_GUIDELINES.md](GIT_GUIDELINES.md) — branches, commits, attribution,
  dependency pinning, review, and CI enforcement.

## Next milestone

The immediate next step is the owner's Windows validation of `--map` against a
legally installed Grundorf, so documented behavior can be separated from confirmed
behavior.

Any Task 03 needs explicit approval and a separate scope. The narrowest useful
candidate is a read-only, bounded inventory of one tile file's top-level records
after the owner's run shows the current metadata layer handles the real folder.
No terrain rendering, asset conversion, world streaming, vehicle physics, or OMSI
scripting exists or is approved.

## License

BSD 3-Clause. See [LICENSE](LICENSE).
